using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SchoolAdmission.Tests.Infrastructure;

/// <summary>
/// Boots the real API (routing, validation, background expiry worker, PostgreSQL access)
/// against the test database. Several instances can share one database - that is exactly how
/// the "two backend processes" scenario from the assignment is exercised.
/// </summary>
public sealed class AdmissionAppFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly int _holdDurationSeconds;

    public AdmissionAppFactory(string connectionString, int holdDurationSeconds = 600)
    {
        _connectionString = connectionString;
        _holdDurationSeconds = holdDurationSeconds;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        builder.UseSetting("HOLD_DURATION_SECONDS", _holdDurationSeconds.ToString());
    }
}
