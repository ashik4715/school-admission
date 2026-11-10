using SchoolAdmission.Api.BackgroundServices;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Endpoints;
using SchoolAdmission.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();

var connectionString = DatabaseConfig.ResolveConnectionString(builder.Configuration);

builder.Services.AddSingleton(new Db(connectionString));
builder.Services.AddSingleton<SchemaInitializer>();
builder.Services.AddScoped<AdmissionService>();
builder.Services.AddHostedService<HoldExpiryWorker>();

// OpenAPI / Swagger so the API can be explored and tested from the browser
// without Postman (http://localhost:8080/swagger).
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "School Admission System API",
        Version = "v1",
        Description = "First-come-first-served school admission. Hold a seat, then confirm by paying before the hold window runs out."
    });
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

// The UI is served from a different origin in development (and can be deployed anywhere),
// so the API is open. There are no cookies/credentials in this system.
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseCors();
app.UseMiddleware<ErrorHandlingMiddleware>();

// Swagger UI at /swagger (and the raw spec at /swagger/v1/swagger.json).
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "School Admission System API v1");
    options.RoutePrefix = "swagger";
});

// Friendly landing page instead of a bare 404: send browsers to Swagger.
app.MapGet("/", () => Results.Redirect("/swagger"));

// Fail fast with a clear message if the database is not reachable, retrying briefly so
// `docker compose up` works even when PostgreSQL is still warming up.
await WaitForDatabaseAsync(app.Services.GetRequiredService<Db>(), app.Logger);

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<SchemaInitializer>().InitializeAsync();
}

app.MapAdmissionEndpoints();

app.Logger.LogInformation("School Admission API listening on {Urls} (hold duration {Seconds}s)",
    string.Join(", ", app.Urls.DefaultIfEmpty("http://0.0.0.0:8080")),
    Environment.GetEnvironmentVariable("HOLD_DURATION_SECONDS") ?? "600");

app.Run();

static async Task WaitForDatabaseAsync(Db db, ILogger logger)
{
    var attempts = 0;
    while (attempts < 30)
    {
        attempts++;
        try
        {
            await using var connection = await db.OpenConnectionAsync();
            return;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Waiting for PostgreSQL ({Attempt}/30): {Message}", attempts, ex.Message);
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

    throw new InvalidOperationException(
        "Could not connect to PostgreSQL. Check ConnectionStrings__Default (or DATABASE_URL).");
}

// Exposed so the integration tests can boot the real application with WebApplicationFactory.
public partial class Program { }
