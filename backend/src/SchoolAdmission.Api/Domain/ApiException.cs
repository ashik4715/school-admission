namespace SchoolAdmission.Api.Domain;

/// <summary>
/// Business/validation failure that maps 1:1 to an HTTP error response.
/// Handled centrally by <see cref="Endpoints.ErrorHandlingMiddleware"/> so that the
/// UI always receives a JSON body it can show to the user.
/// </summary>
public sealed class ApiException : Exception
{
    public int StatusCode { get; }
    public string Code { get; }

    public ApiException(int statusCode, string code, string message) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public static ApiException NotFound(string message) => new(404, "NOT_FOUND", message);
    public static ApiException BadRequest(string code, string message) => new(400, code, message);
    public static ApiException Conflict(string code, string message) => new(409, code, message);
}
