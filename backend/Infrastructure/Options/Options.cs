using Microsoft.Extensions.Options;

namespace YuChat.Infrastructure.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "yuchat";
    public string Audience { get; set; } = "yuchat";
    public string Key { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;

    public sealed class Validator : IValidateOptions<JwtOptions>
    {
        public ValidateOptionsResult Validate(string? name, JwtOptions options)
        {
            var failures = new List<string>();

            if (string.IsNullOrWhiteSpace(options.Issuer))
            {
                failures.Add("Jwt:Issuer is required.");
            }

            if (string.IsNullOrWhiteSpace(options.Audience))
            {
                failures.Add("Jwt:Audience is required.");
            }

            if (string.IsNullOrWhiteSpace(options.Key))
            {
                failures.Add("Jwt:Key is required. Set it via user-secrets or the Jwt__Key environment variable.");
            }
            else if (System.Text.Encoding.UTF8.GetByteCount(options.Key) < 32)
            {
                failures.Add("Jwt:Key must be at least 32 bytes for HS256.");
            }

            if (options.AccessTokenMinutes is < 1 or > 24 * 60)
            {
                failures.Add("Jwt:AccessTokenMinutes must be between 1 and 1440.");
            }

            if (options.RefreshTokenDays is < 1 or > 365)
            {
                failures.Add("Jwt:RefreshTokenDays must be between 1 and 365.");
            }

            return failures.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(failures);
        }
    }
}

public sealed class EncryptionOptions
{
    public const string SectionName = "Encryption";

    /// <summary>Base64-encoded 32-byte key, used for new messages.</summary>
    public string MasterKey { get; set; } = string.Empty;

    public int ActiveKeyVersion { get; set; } = 1;

    /// <summary>
    /// Previously used keys keyed by version, so old messages stay readable after rotation.
    /// The active version is always resolved from <see cref="MasterKey"/>.
    /// </summary>
    public Dictionary<int, string> KeyRing { get; set; } = [];

    public sealed class Validator : IValidateOptions<EncryptionOptions>
    {
        public ValidateOptionsResult Validate(string? name, EncryptionOptions options)
        {
            var failures = new List<string>();

            if (!TryDecode(options.MasterKey, out _))
            {
                failures.Add("Encryption:MasterKey must be a Base64-encoded 32-byte key (for example: openssl rand -base64 32).");
            }

            if (options.ActiveKeyVersion < 1)
            {
                failures.Add("Encryption:ActiveKeyVersion must be positive.");
            }

            foreach (var (version, key) in options.KeyRing)
            {
                if (version < 1)
                {
                    failures.Add($"Encryption:KeyRing contains an invalid version '{version}'.");
                }

                if (!TryDecode(key, out _))
                {
                    failures.Add($"Encryption:KeyRing[{version}] must be a Base64-encoded 32-byte key.");
                }
            }

            return failures.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(failures);
        }

        internal static bool TryDecode(string? base64, out byte[] key)
        {
            key = [];
            if (string.IsNullOrWhiteSpace(base64))
            {
                return false;
            }

            try
            {
                key = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                return false;
            }

            if (key.Length != 32)
            {
                return false;
            }

            return true;
        }
    }
}

public sealed class FrontendOptions
{
    public const string SectionName = "Frontend";

    /// <summary>Origin of the Vite dev server (or production frontend) allowed by CORS.</summary>
    public string Origin { get; set; } = "http://localhost:5173";

    /// <summary>When true refresh tokens are written to an httpOnly cookie instead of the response body.</summary>
    public bool UseRefreshCookie { get; set; } = true;
}
