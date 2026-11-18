using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using YuChat.Data;
using YuChat.Domain;
using YuChat.Infrastructure.Errors;
using YuChat.Infrastructure.Options;

namespace YuChat.Infrastructure.Security;

public sealed record TokenPair(string AccessToken, DateTime AccessTokenExpiresAt, string RefreshToken, DateTime RefreshTokenExpiresAt);

public interface ITokenService
{
    TokenPair IssueTokens(User user, Guid? familyId = null);

    Task<TokenPair> RotateAsync(string refreshToken, CancellationToken cancellationToken);

    Task RevokeAsync(string refreshToken, CancellationToken cancellationToken);
}

public sealed class TokenService(
    AppDbContext db,
    IOptions<JwtOptions> jwtOptions,
    TimeProvider time) : ITokenService
{
    public TokenPair IssueTokens(User user, Guid? familyId = null)
    {
        var options = jwtOptions.Value;
        var now = time.GetUtcNow().UtcDateTime;

        var access = CreateAccessToken(user, now, options);
        var (refreshEntity, refreshValue) = CreateRefreshToken(user, now, options, familyId ?? Guid.CreateVersion7());

        return access with { RefreshToken = refreshValue, RefreshTokenExpiresAt = refreshEntity.ExpiresAt };
    }

    public async Task<TokenPair> RotateAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var hash = Hash(refreshToken);

        var stored = await db.RefreshTokens
            .Include(x => x.User)
            .SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken)
            ?? throw AppException.Unauthorized(ErrorCodes.InvalidRefreshToken, "Refresh token is not valid.");

        var now = time.GetUtcNow().UtcDateTime;

        if (stored.RevokedAt is not null)
        {
            // Replaying an already rotated token means the token leaked: kill the whole family.
            await RevokeFamilyAsync(stored.FamilyId, now, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            throw AppException.Unauthorized(ErrorCodes.InvalidRefreshToken, "Refresh token is not valid.");
        }

        if (stored.ExpiresAt <= now)
        {
            throw AppException.Unauthorized(ErrorCodes.InvalidRefreshToken, "Refresh token has expired.");
        }

        var options = jwtOptions.Value;
        var access = CreateAccessToken(stored.User, now, options);
        var (replacement, replacementValue) = CreateRefreshToken(stored.User, now, options, stored.FamilyId);

        stored.RevokedAt = now;
        stored.ReplacedByTokenId = replacement.Id;

        await db.SaveChangesAsync(cancellationToken);

        return access with { RefreshToken = replacementValue, RefreshTokenExpiresAt = replacement.ExpiresAt };
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var hash = Hash(refreshToken);

        var stored = await db.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);

        if (stored is null || stored.RevokedAt is not null)
        {
            return;
        }

        stored.RevokedAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
    }

    private TokenPair CreateAccessToken(User user, DateTime now, JwtOptions options)
    {
        var expiresAt = now.AddMinutes(options.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
                new Claim("login", user.Login),
            ]),
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expiresAt,
            SigningCredentials = new SigningCredentials(CreateKey(options.Key), SecurityAlgorithms.HmacSha256),
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);

        return new TokenPair(token, expiresAt, string.Empty, DateTime.MinValue);
    }

    private (RefreshToken Entity, string Value) CreateRefreshToken(User user, DateTime now, JwtOptions options, Guid familyId)
    {
        var value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var entity = new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            TokenHash = Hash(value),
            FamilyId = familyId,
            CreatedAt = now,
            ExpiresAt = now.AddDays(options.RefreshTokenDays),
        };

        db.RefreshTokens.Add(entity);

        return (entity, value);
    }

    private async Task RevokeFamilyAsync(Guid familyId, DateTime now, CancellationToken cancellationToken)
    {
        var tokens = await db.RefreshTokens
            .Where(x => x.FamilyId == familyId && x.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
        {
            token.RevokedAt = now;
        }
    }

    internal static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    internal static SymmetricSecurityKey CreateKey(string key) =>
        new(Encoding.UTF8.GetBytes(key));
}
