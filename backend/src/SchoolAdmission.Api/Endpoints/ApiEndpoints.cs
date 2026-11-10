using Microsoft.AspNetCore.Mvc;
using SchoolAdmission.Api.Domain;
using SchoolAdmission.Api.Services;

namespace SchoolAdmission.Api.Endpoints;

public static class ApiEndpoints
{
    public static void MapAdmissionEndpoints(this WebApplication app)
    {
        var api = app.MapGroup(string.Empty);

        // 1) POST /classes - create a class (admin)
        api.MapPost("/classes", async (CreateClassRequest? body, AdmissionService service, CancellationToken ct) =>
        {
            if (body is null)
            {
                throw ApiException.BadRequest("INVALID_REQUEST", "A JSON body with name and totalSeats is required.");
            }

            var created = await service.CreateClassAsync(body.Name, body.TotalSeats, ct);
            return Results.Created($"/classes/{created.Id}", created.ToResponse());
        });

        // 2) GET /classes - all classes with seat counts
        api.MapGet("/classes", async (AdmissionService service, CancellationToken ct) =>
        {
            var classes = await service.ListClassesAsync(ct);
            return Results.Ok(classes.Select(c => c.ToResponse()));
        });

        // 3) GET /classes/{classId} - seat state of one class
        api.MapGet("/classes/{classId}", async (string classId, AdmissionService service, CancellationToken ct) =>
        {
            var schoolClass = await service.GetClassAsync(classId, ct)
                              ?? throw ApiException.NotFound($"Class '{classId}' was not found.");
            return Results.Ok(schoolClass.ToResponse());
        });

        // 4) POST /classes/{classId}/holds - apply (seat hold)
        api.MapPost("/classes/{classId}/holds", async (string classId, [FromHeader(Name = "X-Student-Id")] string? xStudentId, HttpRequest request, AdmissionService service, CancellationToken ct) =>
        {
            var studentId = StudentIdValidator.Require(request);
            var hold = await service.CreateHoldAsync(classId, studentId, ct);
            return Results.Created($"/holds/{hold.Id}", hold.ToHoldResponse());
        });

        // 5) GET /holds/{holdId} - current state of one application
        api.MapGet("/holds/{holdId}", async (string holdId, AdmissionService service, CancellationToken ct) =>
        {
            var hold = await service.GetHoldAsync(holdId, ct)
                       ?? throw ApiException.NotFound($"Application '{holdId}' was not found.");
            return Results.Ok(hold.ToStatusResponse());
        });

        // 6) POST /holds/{holdId}/confirm - pay the fee, confirm admission
        api.MapPost("/holds/{holdId}/confirm", async (string holdId, [FromHeader(Name = "X-Student-Id")] string? xStudentId, HttpRequest request, AdmissionService service, CancellationToken ct) =>
        {
            var studentId = StudentIdValidator.Require(request);
            var hold = await service.ConfirmHoldAsync(holdId, studentId, ct);
            return Results.Ok(hold.ToConfirmResponse());
        });

        // 7) DELETE /holds/{holdId} - cancel the application
        api.MapDelete("/holds/{holdId}", async (string holdId, [FromHeader(Name = "X-Student-Id")] string? xStudentId, HttpRequest request, AdmissionService service, CancellationToken ct) =>
        {
            var studentId = StudentIdValidator.Require(request);
            await service.CancelHoldAsync(holdId, studentId, ct);
            return Results.NoContent();
        });

        // 8) GET /me/holds - every application of this student
        api.MapGet("/me/holds", async ([FromHeader(Name = "X-Student-Id")] string? xStudentId, HttpRequest request, AdmissionService service, CancellationToken ct) =>
        {
            var studentId = StudentIdValidator.Require(request);
            var holds = await service.GetHoldsForStudentAsync(studentId, ct);
            return Results.Ok(holds.Select(h => h.ToMyHoldResponse()));
        });

        // Convenience endpoint for the UI footer/health checks (not part of the contract).
        api.MapGet("/health", (AdmissionService service) => Results.Ok(new
        {
            status = "ok",
            holdDurationSeconds = service.HoldDurationSeconds,
            serverTimeUtc = DateTime.UtcNow
        }));
    }
}
