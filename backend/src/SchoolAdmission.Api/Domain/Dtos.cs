using System.Text.Json.Serialization;

namespace SchoolAdmission.Api.Domain;

// ---------- Requests ----------

public sealed class CreateClassRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("totalSeats")]
    public int? TotalSeats { get; set; }
}

// ---------- Responses ----------
// Extra fields are allowed by the assignment, so responses carry a few helpful
// additions (className, createdAt, ...). Names of the required fields are exact.

public sealed record ClassResponse(
    string Id,
    string Name,
    int TotalSeats,
    int HeldSeats,
    int ConfirmedSeats,
    int AvailableSeats,
    DateTime CreatedAt);

public sealed record HoldResponse(
    string HoldId,
    string ClassId,
    string ClassName,
    string StudentId,
    string Status,
    DateTime ExpiresAt,
    DateTime CreatedAt,
    DateTime? ConfirmedAt);

public sealed record HoldStatusResponse(
    string HoldId,
    string ClassId,
    string ClassName,
    string StudentId,
    string Status,
    DateTime ExpiresAt);

public sealed record ConfirmResponse(
    string HoldId,
    string Status,
    string ClassId,
    DateTime ExpiresAt,
    DateTime? ConfirmedAt);

public sealed record MyHoldResponse(
    string HoldId,
    string ClassId,
    string ClassName,
    string Status,
    DateTime ExpiresAt,
    DateTime CreatedAt);

public sealed record ErrorResponse(string Error, string Message)
{
    /// <summary>Machine readable error code, mirrors <see cref="Error"/>.</summary>
    [JsonPropertyName("code")]
    public string Code => Error;
}

public static class Mapping
{
    public static ClassResponse ToResponse(this SchoolClass c) => new(
        c.Id, c.Name, c.TotalSeats, c.HeldSeats, c.ConfirmedSeats, c.AvailableSeats, c.CreatedAt);

    public static HoldResponse ToHoldResponse(this Hold h) => new(
        h.Id, h.ClassId, h.ClassName, h.StudentId, h.Status.ToApiValue(), h.ExpiresAt, h.CreatedAt, h.ConfirmedAt);

    public static HoldStatusResponse ToStatusResponse(this Hold h) => new(
        h.Id, h.ClassId, h.ClassName, h.StudentId, h.Status.ToApiValue(), h.ExpiresAt);

    public static ConfirmResponse ToConfirmResponse(this Hold h) => new(
        h.Id, h.Status.ToApiValue(), h.ClassId, h.ExpiresAt, h.ConfirmedAt);

    public static MyHoldResponse ToMyHoldResponse(this Hold h) => new(
        h.Id, h.ClassId, h.ClassName, h.Status.ToApiValue(), h.ExpiresAt, h.CreatedAt);
}
