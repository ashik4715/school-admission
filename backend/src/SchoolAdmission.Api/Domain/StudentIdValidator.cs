namespace SchoolAdmission.Api.Domain;

/// <summary>Validation for the caller identity: 17-digit birth registration number.</summary>
public static class StudentIdValidator
{
    public const string HeaderName = "X-Student-Id";

    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length == 17 &&
        value.All(char.IsAsciiDigit);

    public static string Require(HttpRequest request)
    {
        if (!request.Headers.TryGetValue(HeaderName, out var raw) || string.IsNullOrWhiteSpace(raw.ToString()))
        {
            throw ApiException.BadRequest(
                "STUDENT_ID_REQUIRED",
                $"The {HeaderName} header is required. It must contain the 17-digit birth registration number.");
        }

        var value = raw.ToString().Trim();
        if (!IsValid(value))
        {
            throw ApiException.BadRequest(
                "INVALID_STUDENT_ID",
                $"{HeaderName} must be exactly 17 digits (the student's birth registration number).");
        }

        return value;
    }
}
