using Dapper;

namespace SchoolAdmission.Api.Data;

/// <summary>
/// Creates the schema on startup. Idempotent, so it is safe to run from several backend
/// processes/containers that share one database (guarded by a PostgreSQL advisory lock).
/// No ORM migrations are needed; the whole schema is small and explicit.
/// </summary>
public sealed class SchemaInitializer
{
    // Arbitrary constant: "school-admission" schema lock.
    private const long AdvisoryLockKey = 841_229_917L;

    private readonly Db _db;
    private readonly ILogger<SchemaInitializer> _logger;

    public SchemaInitializer(Db db, ILogger<SchemaInitializer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        await connection.ExecuteAsync("SELECT pg_advisory_lock(@key);", new { key = AdvisoryLockKey });

        try
        {
            await connection.ExecuteAsync(Schema);
            _logger.LogInformation("Database schema is up to date.");
        }
        finally
        {
            await connection.ExecuteAsync("SELECT pg_advisory_unlock(@key);", new { key = AdvisoryLockKey });
        }
    }

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS school_classes (
            id          text        NOT NULL PRIMARY KEY,
            name        text        NOT NULL,
            total_seats integer     NOT NULL CHECK (total_seats > 0),
            created_at  timestamptz NOT NULL DEFAULT now()
        );

        CREATE TABLE IF NOT EXISTS holds (
            id           text        NOT NULL PRIMARY KEY,
            class_id     text        NOT NULL REFERENCES school_classes (id) ON DELETE CASCADE,
            student_id   text        NOT NULL,
            status       text        NOT NULL CHECK (status IN ('HELD', 'CONFIRMED', 'EXPIRED', 'CANCELLED')),
            expires_at   timestamptz NOT NULL,
            created_at   timestamptz NOT NULL DEFAULT now(),
            confirmed_at timestamptz NULL,
            cancelled_at timestamptz NULL
        );

        CREATE INDEX IF NOT EXISTS ix_holds_class_status
            ON holds (class_id, status);
        CREATE INDEX IF NOT EXISTS ix_holds_student
            ON holds (student_id, created_at DESC);
        CREATE INDEX IF NOT EXISTS ix_holds_pending_expiry
            ON holds (expires_at) WHERE status = 'HELD';

        -- Safety net (independent of application code): a student can never have two
        -- seats in the same class, no matter how many backend processes are running.
        CREATE UNIQUE INDEX IF NOT EXISTS ux_holds_active_per_student_class
            ON holds (class_id, student_id) WHERE status IN ('HELD', 'CONFIRMED');
        """;
}
