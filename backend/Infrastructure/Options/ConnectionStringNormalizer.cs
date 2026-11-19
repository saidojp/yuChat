using Npgsql;

namespace YuChat.Infrastructure.Options;

/// <summary>
/// Render and Heroku hand out a URI-style URL
/// (<c>postgres://user:pass@host:5432/db</c>) while Npgsql expects a key/value string.
/// This normalizes both forms and applies the SSL settings required for external access.
/// </summary>
public static class ConnectionStringNormalizer
{
    /// <summary>Default pool size; small managed plans cap total connections (Heroku essential-0: 20).</summary>
    public const int DefaultMaxPoolSize = 20;

    public static string Normalize(string connectionString, int? maxPoolSize = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Database connection string is empty.");
        }

        var trimmed = connectionString.Trim();
        var builder = IsUri(trimmed)
            ? FromUri(trimmed)
            : new NpgsqlConnectionStringBuilder(trimmed);

        if (maxPoolSize is > 0)
        {
            builder.MaxPoolSize = maxPoolSize.Value;
        }
        else if (builder.MaxPoolSize <= 0)
        {
            builder.MaxPoolSize = DefaultMaxPoolSize;
        }

        return builder.ConnectionString;
    }

    private static bool IsUri(string connectionString) =>
        connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
        || connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);

    private static NpgsqlConnectionStringBuilder FromUri(string connectionString)
    {
        var uri = new Uri(connectionString);
        var userInfo = uri.UserInfo.Split(':', 2);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty,
            SslMode = SslMode.Require,
            Pooling = true,
            MaxPoolSize = DefaultMaxPoolSize,
        };

        // Render includes ?sslmode=disable on the internal URL; honour it explicitly.
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var sslMode = query["sslmode"];

        if (sslMode is not null)
        {
            builder.SslMode = sslMode.ToLowerInvariant() switch
            {
                "disable" => SslMode.Disable,
                "allow" => SslMode.Allow,
                "prefer" => SslMode.Prefer,
                "require" => SslMode.Require,
                "verify-ca" => SslMode.VerifyCA,
                "verify-full" => SslMode.VerifyFull,
                _ => SslMode.Require,
            };
        }

        // Heroku's DATABASE_URL carries only the credentials; keep TLS verify at the driver default
        // (VerifyFull on Npgsql 8+) unless the URL asks otherwise.
        return builder;
    }
}
