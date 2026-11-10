using SchoolAdmission.Api.Domain;

namespace SchoolAdmission.Api.Endpoints;

/// <summary>
/// Turns <see cref="ApiException"/> into the JSON error shape the UI understands:
/// <c>{ "error": "CODE", "code": "CODE", "message": "human readable" }</c>.
/// Anything unexpected becomes a 500 with the same shape, so the client never gets an
/// empty response body.
/// </summary>
public sealed class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ApiException ex)
        {
            await WriteAsync(context, ex.StatusCode, ex.Code, ex.Message);
        }
        catch (BadHttpRequestException ex)
        {
            await WriteAsync(context, 400, "INVALID_REQUEST", $"The request could not be read: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception while processing {Method} {Path}", context.Request.Method, context.Request.Path);
            await WriteAsync(context, 500, "INTERNAL_ERROR", "Something went wrong on the server. Please try again.");
        }
    }

    private static async Task WriteAsync(HttpContext context, int statusCode, string code, string message)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new ErrorResponse(code, message));
    }
}
