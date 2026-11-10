using Npgsql;

namespace SchoolAdmission.Api.Data;

/// <summary>
/// Resolves the PostgreSQL connection string from the environment so the same image can
/// run against any database. Supported (in order):
///   1. ConnectionStrings__Default   (standard .NET configuration / docker-compose)
///   2. DATABASE_CONNECTION_STRING   (plain alias)
///   3. DATABASE_URL                 (libpq style, e.g. postgres://user:pass@host:5432/db)
///   4. local development default
/// </summary>
public static class DatabaseConfig
{
    private const string LocalDefault =
        "Host=localhost;Port=5432;Database=school_admission;Username=school;Password=school";

    public static string ResolveConnectionString(IConfiguration configuration)
    {
        var candidate =
            configuration.GetConnectionString("Default")
            ?? Environment.GetEnvironmentVariable("DATABASE_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("DATABASE_URL");

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return LocalDefault;
        }

        return Normalize(candidate.Trim());
    }

    private static string Normalize(string value)
    {
        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var uri = new Uri(value);
        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = uri.AbsolutePath.Trim('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty
        };

        if (uri.Query.Contains("sslmode=require", StringComparison.OrdinalIgnoreCase))
        {
            builder.SslMode = SslMode.Require;
        }

        return builder.ConnectionString;
    }
}

/// <summary>
/// Source of short-lived Npgsql connections. Npgsql pools them internally, so opening one per
/// unit of work is cheap.
///
/// The pool is capped (DB_MAX_POOL_SIZE, default 10) on purpose: an application that takes a
/// row lock and then queues behind it keeps its connection while waiting, so an unbounded
/// crowd of applicants could otherwise exhaust the server's max_connections. Behind the cap
/// requests simply wait for a pooled connection instead of failing.
/// </summary>
public sealed class Db : IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public Db(string connectionString)
    {
        _dataSource = NpgsqlDataSource.Create(ApplyPoolDefaults(connectionString));
    }

    public async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct = default) =>
        await _dataSource.OpenConnectionAsync(ct);

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    private static string ApplyPoolDefaults(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        builder.ApplicationName = "school-admission";

        var explicitPoolSize = connectionString.Contains("pool", StringComparison.OrdinalIgnoreCase);
        var configured = Environment.GetEnvironmentVariable("DB_MAX_POOL_SIZE");

        if (int.TryParse(configured, out var poolSize) && poolSize > 0)
        {
            builder.MaxPoolSize = poolSize;
        }
        else if (!explicitPoolSize && builder.MaxPoolSize > 10)
        {
            builder.MaxPoolSize = 10;
        }

        return builder.ConnectionString;
    }
}
