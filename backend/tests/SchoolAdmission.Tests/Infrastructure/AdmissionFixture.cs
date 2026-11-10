using Npgsql;
using Xunit;

namespace SchoolAdmission.Tests.Infrastructure;

/// <summary>
/// Test-run setup:
///  * one throw-away PostgreSQL database (dropped afterwards),
///  * two independent API hosts on that same database - the "two backend processes" setup
///    from the assignment.
///
/// Everything is shared through one collection fixture so the whole suite stays fast and
/// opens only a handful of connections.
/// </summary>
public sealed class AdmissionFixture : IAsyncLifetime
{
    private const string DefaultAdminConnectionString =
        "Host=localhost;Port=5432;Database=postgres;Username=school;Password=school";

    private readonly List<AdmissionAppFactory> _extraFactories = new();

    public string AdminConnectionString { get; private set; } = null!;
    public string DatabaseName { get; private set; } = null!;
    public string ConnectionString { get; private set; } = null!;

    public AdmissionAppFactory Primary { get; private set; } = null!;
    public AdmissionAppFactory Secondary { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        AdminConnectionString = Environment.GetEnvironmentVariable("TEST_POSTGRES_ADMIN")
                                ?? DefaultAdminConnectionString;
        DatabaseName = $"school_admission_test_{Guid.NewGuid():N}";

        await using (var connection = new NpgsqlConnection(AdminConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{DatabaseName}\";", connection);
            await command.ExecuteNonQueryAsync();
        }

        ConnectionString = new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Database = DatabaseName
        }.ConnectionString;

        Primary = new AdmissionAppFactory(ConnectionString);
        Secondary = new AdmissionAppFactory(ConnectionString);

        // Warm both hosts up so the schema exists before the first test runs.
        using var warmup = Primary.CreateClient();
        (await warmup.GetAsync("/health")).EnsureSuccessStatusCode();
    }

    /// <summary>A separate host with a custom hold window (kept alive until the run ends).</summary>
    public AdmissionAppFactory CreateFactory(int holdDurationSeconds)
    {
        var factory = new AdmissionAppFactory(ConnectionString, holdDurationSeconds);
        _extraFactories.Add(factory);
        return factory;
    }

    public async Task DisposeAsync()
    {
        await Primary.DisposeAsync();
        await Secondary.DisposeAsync();

        foreach (var factory in _extraFactories)
        {
            await factory.DisposeAsync();
        }

        NpgsqlConnection.ClearAllPools();

        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE);", connection);
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class AdmissionCollection : ICollectionFixture<AdmissionFixture>
{
    public const string Name = "admission-api";
}
