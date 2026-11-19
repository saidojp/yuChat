using YuChat.Infrastructure.Security;

namespace YuChat.Features.Search;

public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/search").WithTags("Search").RequireAuthorization();

        group.MapGet("/", async (
            string? query,
            SearchService search,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await search.SearchAsync(currentUser.RequireUserId(), query ?? string.Empty, cancellationToken)))
            .WithName("GlobalSearch")
            .RequireRateLimiting(RateLimitPolicies.Search);

        return app;
    }
}
