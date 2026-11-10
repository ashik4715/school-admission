namespace SchoolAdmission.Api.Domain;

/// <summary>
/// A "class" in this system is always one class *and* one shift, e.g.
/// "Class 6 - Morning Shift". Every shift has its own seat pool.
/// </summary>
public sealed class SchoolClass
{
    public string Id { get; init; } = null!;
    public string Name { get; init; } = null!;
    public int TotalSeats { get; init; }
    public int HeldSeats { get; init; }
    public int ConfirmedSeats { get; init; }
    public int AvailableSeats => TotalSeats - HeldSeats - ConfirmedSeats;
    public DateTime CreatedAt { get; init; }
}

public sealed class Hold
{
    public string Id { get; init; } = null!;
    public string ClassId { get; init; } = null!;
    public string ClassName { get; init; } = null!;
    public string StudentId { get; init; } = null!;
    public HoldStatus Status { get; init; }
    public DateTime ExpiresAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? ConfirmedAt { get; init; }
    public DateTime? CancelledAt { get; init; }
}

public enum HoldStatus
{
    HELD,
    CONFIRMED,
    EXPIRED,
    CANCELLED
}

public static class HoldStatusExtensions
{
    public static string ToApiValue(this HoldStatus status) => status switch
    {
        HoldStatus.HELD => "HELD",
        HoldStatus.CONFIRMED => "CONFIRMED",
        HoldStatus.EXPIRED => "EXPIRED",
        HoldStatus.CANCELLED => "CANCELLED",
        _ => status.ToString()
    };
}
