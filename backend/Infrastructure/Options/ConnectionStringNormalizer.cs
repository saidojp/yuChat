using Npgsql;

namespace YuChat.Infrastructure.Options;

/// <summary>
/// Render (and most managed providers) hand out a URI-style URL
/// (<c>postgres://user:pass@host:5432/db</c>) while Npgsql expects a key/value string.
/// This normalizes both forms and applies the SSL settings required for external access.
/// </summary>
public static class ConnectionStringNormalizer
{
    public static string Normalize(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Database connection string is empty.");
        }

        var trimmed = connectionString.Trim();

        if (!trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var uri = new Uri(trimmed);
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
            MaxPoolSize = 20,
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

        return builder.ConnectionString;
    }
}
