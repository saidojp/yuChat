using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using YuChat.Data;
using YuChat.Domain;
using YuChat.Features.Users;
using YuChat.Infrastructure.Errors;
using YuChat.Infrastructure.Options;
using YuChat.Infrastructure.Security;

namespace YuChat.Features.Auth;

public sealed class AuthService(
    AppDbContext db,
    ITokenService tokens,
    IOptions<JwtOptions> jwtOptions,
    IOptions<FrontendOptions> frontendOptions,
    TimeProvider time)
{
    private const int BcryptWorkFactor = 12;

    /// <summary>Deliberately identical for unknown logins and wrong passwords.</summary>
    private const string InvalidCredentialsMessage = "Login or password is incorrect.";

    public sealed record AuthResult(AuthResponse Response, string RefreshToken, DateTime RefreshExpiresAt);

    public sealed record RefreshResult(RefreshResponse Response, string RefreshToken, DateTime RefreshExpiresAt);

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var login = request.Login?.Trim() ?? string.Empty;

        if (!InputRules.LoginPattern.IsMatch(login))
        {
            throw AppException.BadRequest(
                ErrorCodes.ValidationFailed,
                $"Login must be {InputRules.LoginMinLength}-{InputRules.LoginMaxLength} characters long and may contain letters, digits, dots, dashes and underscores.");
        }

        if (!IsPasswordAcceptable(request.Password))
        {
            throw AppException.BadRequest(
                ErrorCodes.ValidationFailed,
                $"Password must be {InputRules.PasswordMinLength}-{InputRules.PasswordMaxLength} characters long.");
        }

        var normalized = UserService.Normalize(login);

        var exists = await db.Users.AsNoTracking().AnyAsync(x => x.LoginNormalized == normalized, cancellationToken);

        if (exists)
        {
            throw AppException.Conflict(ErrorCodes.LoginTaken, "This login is already registered.");
        }

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Login = login,
            LoginNormalized = normalized,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, BcryptWorkFactor),
            CreatedAt = time.GetUtcNow().UtcDateTime,
        };

        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (Users.DbErrors.IsUniqueViolation(exception))
        {
            throw AppException.Conflict(ErrorCodes.LoginTaken, "This login is already registered.");
        }

        var pair = tokens.IssueTokens(user);
        await db.SaveChangesAsync(cancellationToken);

        return new AuthResult(
            new AuthResponse(pair.AccessToken, pair.AccessTokenExpiresAt, UserService.ToProfile(user)),
            pair.RefreshToken,
            pair.RefreshTokenExpiresAt);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalized = UserService.Normalize(request.Login ?? string.Empty);

        var user = await db.Users.SingleOrDefaultAsync(x => x.LoginNormalized == normalized, cancellationToken);

        // Always run a hash comparison to keep the timing profile flat.
        var hash = user?.PasswordHash ?? BCrypt.Net.BCrypt.HashPassword("invalid-placeholder-value", BcryptWorkFactor);
        var valid = BCrypt.Net.BCrypt.Verify(request.Password ?? string.Empty, hash);

        if (user is null || !valid)
        {
            throw AppException.Unauthorized(ErrorCodes.InvalidCredentials, InvalidCredentialsMessage);
        }

        var pair = tokens.IssueTokens(user);
        await db.SaveChangesAsync(cancellationToken);

        return new AuthResult(
            new AuthResponse(pair.AccessToken, pair.AccessTokenExpiresAt, UserService.ToProfile(user)),
            pair.RefreshToken,
            pair.RefreshTokenExpiresAt);
    }

    public async Task<RefreshResult> RefreshAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw AppException.Unauthorized(ErrorCodes.InvalidRefreshToken, "Refresh token is missing.");
        }

        var pair = await tokens.RotateAsync(refreshToken, cancellationToken);

        return new RefreshResult(
            new RefreshResponse(pair.AccessToken, pair.AccessTokenExpiresAt),
            pair.RefreshToken,
            pair.RefreshTokenExpiresAt);
    }

    public Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(refreshToken)
            ? Task.CompletedTask
            : tokens.RevokeAsync(refreshToken, cancellationToken);

    public void WriteRefreshCookie(HttpResponse response, string refreshToken, DateTime expiresAt)
    {
        if (!frontendOptions.Value.UseRefreshCookie)
        {
            return;
        }

        response.Cookies.Append(RefreshCookieName, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
            Expires = expiresAt,
            IsEssential = true,
        });
    }

    public void ClearRefreshCookie(HttpResponse response)
    {
        response.Cookies.Delete(RefreshCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
        });
    }

    public string ReadRefreshToken(HttpRequest request) =>
        request.Cookies.TryGetValue(RefreshCookieName, out var cookie) ? cookie : string.Empty;

    public bool UseCookie => frontendOptions.Value.UseRefreshCookie;

    public DateTime RefreshExpiry() => time.GetUtcNow().UtcDateTime.AddDays(jwtOptions.Value.RefreshTokenDays);

    private const string RefreshCookieName = "yuchat_refresh";

    private static bool IsPasswordAcceptable(string? password) =>
        password is not null
        && password.Length >= InputRules.PasswordMinLength
        && password.Length <= InputRules.PasswordMaxLength;
}
