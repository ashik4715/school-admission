using Dapper;
using Npgsql;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Domain;

namespace SchoolAdmission.Api.Services;

/// <summary>
/// All admission business rules live here. Every method that changes seat allocation runs
/// inside a single database transaction that first takes a row lock on the class
/// (<c>SELECT ... FOR UPDATE</c>). That serialises seat allocation per class across all
/// requests *and* across multiple backend processes sharing the same database, which is
/// what guarantees "held + confirmed never exceeds totalSeats".
/// </summary>
public sealed class AdmissionService
{
    private static readonly TimeSpan ClockSkewTolerance = TimeSpan.Zero;

    private readonly Db _db;
    private readonly ILogger<AdmissionService> _logger;
    private readonly int _holdDurationSeconds;

    public AdmissionService(Db db, ILogger<AdmissionService> logger, IConfiguration configuration)
    {
        _db = db;
        _logger = logger;

        // Configuration already contains environment variables (HOLD_DURATION_SECONDS),
        // which also lets the integration tests override it per host.
        var raw = configuration["HOLD_DURATION_SECONDS"];

        _holdDurationSeconds = int.TryParse(raw, out var seconds) && seconds > 0
            ? seconds
            : 600;
    }

    public int HoldDurationSeconds => _holdDurationSeconds;

    // ---------------------------------------------------------------- classes

    public async Task<SchoolClass> CreateClassAsync(string? name, int? totalSeats, CancellationToken ct = default)
    {
        var cleanName = (name ?? string.Empty).Trim();

        if (cleanName.Length == 0)
        {
            throw ApiException.BadRequest("INVALID_CLASS_NAME", "Class name is required.");
        }

        if (cleanName.Length > 200)
        {
            throw ApiException.BadRequest("INVALID_CLASS_NAME", "Class name must be at most 200 characters.");
        }

        if (totalSeats is null or < 1 or > 100_000)
        {
            throw ApiException.BadRequest("INVALID_TOTAL_SEATS", "totalSeats must be a whole number between 1 and 100000.");
        }

        await using var connection = await _db.OpenConnectionAsync(ct);
        var id = NewId("c");

        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO school_classes (id, name, total_seats) VALUES (@id, @name, @totalSeats);",
            new { id, name = cleanName, totalSeats }, cancellationToken: ct));

        return await GetClassAsync(id, ct)
               ?? throw new ApiException(500, "INTERNAL_ERROR", "Class could not be created.");
    }

    public async Task<IReadOnlyList<SchoolClass>> ListClassesAsync(CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        var rows = await connection.QueryAsync<SchoolClass>(new CommandDefinition(
            $"{ClassSeatProjection} GROUP BY c.id, c.name, c.total_seats, c.created_at ORDER BY c.created_at, c.name",
            cancellationToken: ct));

        return rows.ToList();
    }

    public async Task<SchoolClass?> GetClassAsync(string classId, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<SchoolClass>(new CommandDefinition(
            $"{ClassSeatProjection} WHERE c.id = @classId GROUP BY c.id, c.name, c.total_seats, c.created_at",
            new { classId }, cancellationToken: ct));
    }

    // ------------------------------------------------------------------ holds

    /// <summary>
    /// Applies for a seat: creates a HELD hold for the student, or 409 if the class is full
    /// or the student already holds/owns a seat in it.
    /// </summary>
    public async Task<Hold> CreateHoldAsync(string classId, string studentId, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        try
        {
            // 1) Lock the class row. Concurrent applicants for this class queue up here,
            //    so the count + insert below cannot interleave.
            ClassRow? classRow = await connection.QuerySingleOrDefaultAsync<ClassRow>(
                new CommandDefinition(
                    "SELECT id AS Id, name AS Name, total_seats AS TotalSeats FROM school_classes WHERE id = @classId FOR UPDATE;",
                    new { classId }, tx, cancellationToken: ct));

            if (classRow is null)
            {
                throw ApiException.NotFound($"Class '{classId}' was not found.");
            }

            // 2) Give back seats whose hold already ran out (the background sweeper does this
            //    continuously, but this keeps the answer correct even between sweeps).
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE holds SET status = 'EXPIRED' WHERE class_id = @classId AND status = 'HELD' AND expires_at <= now();",
                new { classId }, tx, cancellationToken: ct));

            // 3) One active application / confirmed seat per student per class.
            var existing = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                "SELECT id FROM holds WHERE class_id = @classId AND student_id = @studentId AND status IN ('HELD', 'CONFIRMED') LIMIT 1;",
                new { classId, studentId }, tx, cancellationToken: ct));

            if (existing is not null)
            {
                throw ApiException.Conflict(
                    "DUPLICATE_ACTIVE_HOLD",
                    "This student already has an active application or a confirmed seat in this class.");
            }

            // 4) Capacity check against the live counts.
            var counts = await connection.QuerySingleAsync<SeatCounts>(new CommandDefinition(
                """
                SELECT COUNT(*) FILTER (WHERE status = 'HELD')::int      AS Held,
                       COUNT(*) FILTER (WHERE status = 'CONFIRMED')::int AS Confirmed
                FROM holds WHERE class_id = @classId;
                """,
                new { classId }, tx, cancellationToken: ct));

            if (counts.Held + counts.Confirmed >= classRow.TotalSeats)
            {
                throw ApiException.Conflict("NO_SEATS_AVAILABLE", "Sorry, this class is full. No seats are available.");
            }

            // 5) Create the hold. expires_at is computed by the database clock so every
            //    process agrees on the deadline.
            var holdId = NewId("h");
            var expiresAt = await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
                """
                INSERT INTO holds (id, class_id, student_id, status, expires_at)
                VALUES (@holdId, @classId, @studentId, 'HELD', now() + make_interval(secs => @duration::double precision))
                RETURNING expires_at;
                """,
                new { holdId, classId, studentId, duration = _holdDurationSeconds }, tx, cancellationToken: ct));

            await tx.CommitAsync(ct);

            return new Hold
            {
                Id = holdId,
                ClassId = classId,
                ClassName = classRow.Name,
                StudentId = studentId,
                Status = HoldStatus.HELD,
                ExpiresAt = expiresAt,
                CreatedAt = DateTime.UtcNow
            };
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Safety net: the partial unique index caught a duplicate that slipped past the
            // check above (only possible if two requests raced between count and insert).
            await tx.RollbackAsync(ct);
            throw ApiException.Conflict(
                "DUPLICATE_ACTIVE_HOLD",
                "This student already has an active application or a confirmed seat in this class.");
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>Public view of a single hold. Expiry is derived from the clock, not from the sweeper.</summary>
    public async Task<Hold?> GetHoldAsync(string holdId, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        var hold = await connection.QuerySingleOrDefaultAsync<Hold>(new CommandDefinition(
            $"{HoldProjection} WHERE h.id = @holdId", new { holdId }, cancellationToken: ct));

        return hold is null ? null : WithEffectiveStatus(hold);
    }

    public async Task<IReadOnlyList<Hold>> GetHoldsForStudentAsync(string studentId, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        var holds = await connection.QueryAsync<Hold>(new CommandDefinition(
            $"{HoldProjection} WHERE h.student_id = @studentId ORDER BY h.created_at DESC",
            new { studentId }, cancellationToken: ct));

        return holds.Select(WithEffectiveStatus).ToList();
    }

    /// <summary>
    /// Pays the fee: HELD -> CONFIRMED. Idempotent - repeating the call never increases the
    /// number of confirmed seats, because the seat was already counted while held.
    /// </summary>
    public async Task<Hold> ConfirmHoldAsync(string holdId, string studentId, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var completed = false;

        try
        {
            var hold = await connection.QuerySingleOrDefaultAsync<Hold>(new CommandDefinition(
                $"{HoldProjection} WHERE h.id = @holdId FOR UPDATE OF h", new { holdId }, tx, cancellationToken: ct));

            if (hold is null || hold.StudentId != studentId)
            {
                // Do not reveal other students' holds.
                throw ApiException.NotFound($"Application '{holdId}' was not found.");
            }

            if (hold.Status == HoldStatus.CONFIRMED)
            {
                await tx.CommitAsync(ct);
                completed = true;
                return hold; // idempotent replay
            }

            if (hold.Status != HoldStatus.HELD)
            {
                throw ApiException.Conflict("HOLD_NOT_ACTIVE", $"This application is {hold.Status.ToApiValue()} and can no longer be confirmed.");
            }

            if (hold.ExpiresAt <= DateTime.UtcNow)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE holds SET status = 'EXPIRED' WHERE id = @holdId AND status = 'HELD';",
                    new { holdId }, tx, cancellationToken: ct));
                await tx.CommitAsync(ct);
                completed = true;
                throw ApiException.Conflict("HOLD_EXPIRED", "The payment window for this application has expired.");
            }

            var confirmedAt = await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
                "UPDATE holds SET status = 'CONFIRMED', confirmed_at = now() WHERE id = @holdId AND status = 'HELD' RETURNING confirmed_at;",
                new { holdId }, tx, cancellationToken: ct));

            await tx.CommitAsync(ct);
            completed = true;

            return new Hold
            {
                Id = hold.Id,
                ClassId = hold.ClassId,
                ClassName = hold.ClassName,
                StudentId = hold.StudentId,
                Status = HoldStatus.CONFIRMED,
                ExpiresAt = hold.ExpiresAt,
                CreatedAt = hold.CreatedAt,
                ConfirmedAt = confirmedAt
            };
        }
        catch
        {
            if (!completed)
            {
                try { await tx.RollbackAsync(ct); } catch (Exception) { /* connection already closed */ }
            }

            throw;
        }
    }

    /// <summary>Cancels an active application and frees the seat immediately.</summary>
    public async Task CancelHoldAsync(string holdId, string studentId, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var completed = false;

        try
        {
            var hold = await connection.QuerySingleOrDefaultAsync<Hold>(new CommandDefinition(
                $"{HoldProjection} WHERE h.id = @holdId FOR UPDATE OF h", new { holdId }, tx, cancellationToken: ct));

            if (hold is null || hold.StudentId != studentId)
            {
                throw ApiException.NotFound($"Application '{holdId}' was not found.");
            }

            switch (hold.Status)
            {
                case HoldStatus.CANCELLED:
                    await tx.CommitAsync(ct);
                    completed = true;
                    return; // idempotent

                case HoldStatus.CONFIRMED:
                    throw ApiException.Conflict(
                        "ALREADY_CONFIRMED",
                        "Admission is already confirmed for this application, so it can no longer be cancelled.");

                case HoldStatus.EXPIRED:
                    throw ApiException.Conflict("HOLD_EXPIRED", "This application has already expired.");

                case HoldStatus.HELD when hold.ExpiresAt <= DateTime.UtcNow:
                    await connection.ExecuteAsync(new CommandDefinition(
                        "UPDATE holds SET status = 'EXPIRED' WHERE id = @holdId AND status = 'HELD';",
                        new { holdId }, tx, cancellationToken: ct));
                    await tx.CommitAsync(ct);
                    completed = true;
                    throw ApiException.Conflict("HOLD_EXPIRED", "This application has already expired.");

                case HoldStatus.HELD:
                    await connection.ExecuteAsync(new CommandDefinition(
                        "UPDATE holds SET status = 'CANCELLED', cancelled_at = now() WHERE id = @holdId AND status = 'HELD';",
                        new { holdId }, tx, cancellationToken: ct));
                    await tx.CommitAsync(ct);
                    completed = true;
                    return;
            }
        }
        catch
        {
            if (!completed)
            {
                try { await tx.RollbackAsync(ct); } catch (Exception) { /* connection already closed */ }
            }

            throw;
        }
    }

    /// <summary>Marks every out-of-time hold as EXPIRED. Called by the background sweeper.</summary>
    public async Task<int> ExpireOverdueHoldsAsync(CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE holds SET status = 'EXPIRED' WHERE status = 'HELD' AND expires_at <= now();",
            cancellationToken: ct));
    }

    // ---------------------------------------------------------------- helpers

    private sealed record ClassRow(string Id, string Name, int TotalSeats);
    private sealed record SeatCounts(int Held, int Confirmed);

    private static Hold WithEffectiveStatus(Hold hold)
    {
        if (hold.Status == HoldStatus.HELD && hold.ExpiresAt <= DateTime.UtcNow + ClockSkewTolerance)
        {
            return new Hold
            {
                Id = hold.Id,
                ClassId = hold.ClassId,
                ClassName = hold.ClassName,
                StudentId = hold.StudentId,
                Status = HoldStatus.EXPIRED,
                ExpiresAt = hold.ExpiresAt,
                CreatedAt = hold.CreatedAt,
                ConfirmedAt = hold.ConfirmedAt,
                CancelledAt = hold.CancelledAt
            };
        }

        return hold;
    }

    private static string NewId(string prefix) =>
        $"{prefix}_{Guid.NewGuid():N}"[..(prefix.Length + 1 + 12)];

    // Seat counts are always derived from the holds table (no denormalised counters that
    // could drift); an expired-but-not-yet-swept hold already stops counting as held.
    private const string ClassSeatProjection = """
        SELECT c.id                                        AS Id,
               c.name                                      AS Name,
               c.total_seats                               AS TotalSeats,
               c.created_at                                AS CreatedAt,
               COUNT(h.id) FILTER (WHERE h.status = 'HELD' AND h.expires_at > now())::int AS HeldSeats,
               COUNT(h.id) FILTER (WHERE h.status = 'CONFIRMED')::int                     AS ConfirmedSeats
        FROM school_classes c
        LEFT JOIN holds h ON h.class_id = c.id
        """;

    private const string HoldProjection = """
        SELECT h.id           AS Id,
               h.class_id     AS ClassId,
               c.name         AS ClassName,
               h.student_id   AS StudentId,
               h.status       AS Status,
               h.expires_at   AS ExpiresAt,
               h.created_at   AS CreatedAt,
               h.confirmed_at AS ConfirmedAt,
               h.cancelled_at AS CancelledAt
        FROM holds h
        JOIN school_classes c ON c.id = h.class_id
        """;
}
