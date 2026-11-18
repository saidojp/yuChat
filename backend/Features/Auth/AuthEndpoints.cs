using YuChat.Features.Auth;
using YuChat.Infrastructure.Errors;
using YuChat.Infrastructure.Security;

namespace YuChat.Features.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", async (
            RegisterRequest request,
            AuthService auth,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var result = await auth.RegisterAsync(request, cancellationToken);
            auth.WriteRefreshCookie(http.Response, result.RefreshToken, result.RefreshExpiresAt);
            return Results.Ok(result.Response);
        })
        .WithName("Register")
        .AllowAnonymous()
        .RequireRateLimiting(RateLimitPolicies.Auth);

        group.MapPost("/login", async (
            LoginRequest request,
            AuthService auth,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var result = await auth.LoginAsync(request, cancellationToken);
            auth.WriteRefreshCookie(http.Response, result.RefreshToken, result.RefreshExpiresAt);
            return Results.Ok(result.Response);
        })
        .WithName("Login")
        .AllowAnonymous()
        .RequireRateLimiting(RateLimitPolicies.Auth);

        group.MapPost("/refresh", async (
            AuthService auth,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var refreshToken = ReadRefreshToken(auth, http);

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                throw AppException.Unauthorized(ErrorCodes.InvalidRefreshToken, "Refresh token is missing.");
            }

            var rotated = await auth.RefreshAsync(refreshToken, cancellationToken);
            auth.WriteRefreshCookie(http.Response, rotated.RefreshToken, rotated.RefreshExpiresAt);

            return Results.Ok(rotated.Response);
        })
        .WithName("Refresh")
        .AllowAnonymous()
        .RequireRateLimiting(RateLimitPolicies.Auth);

        group.MapPost("/logout", async (
            AuthService auth,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            await auth.LogoutAsync(ReadRefreshToken(auth, http), cancellationToken);
            auth.ClearRefreshCookie(http.Response);
            return Results.NoContent();
        })
        .WithName("Logout")
        .AllowAnonymous();

        return app;
    }

    private static string ReadRefreshToken(AuthService auth, HttpContext http) =>
        auth.UseCookie
            ? auth.ReadRefreshToken(http.Request)
            : http.Request.Headers.TryGetValue("X-Refresh-Token", out var header) ? header.ToString() : string.Empty;
}
