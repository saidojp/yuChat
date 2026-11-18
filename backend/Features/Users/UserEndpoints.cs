using YuChat.Infrastructure.Security;

namespace YuChat.Features.Users;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users").RequireAuthorization();

        group.MapGet("/me", async (UserService users, ICurrentUser currentUser, CancellationToken cancellationToken) =>
            Results.Ok(await users.GetProfileAsync(currentUser.RequireUserId(), cancellationToken)))
            .WithName("GetCurrentUser");

        group.MapPost("/handle", async (
            SetHandleRequest request,
            UserService users,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await users.SetHandleAsync(currentUser.RequireUserId(), request.UserHandle ?? string.Empty, cancellationToken)))
            .WithName("SetHandle");

        group.MapGet("/search", async (
            string? query,
            UserService users,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await users.SearchAsync(currentUser.RequireUserId(), query ?? string.Empty, cancellationToken)))
            .WithName("SearchUsers")
            .RequireRateLimiting(RateLimitPolicies.Search);

        return app;
    }
}
