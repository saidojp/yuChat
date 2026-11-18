using YuChat.Infrastructure.Security;

namespace YuChat.Features.Friends;

public static class FriendEndpoints
{
    public static IEndpointRouteBuilder MapFriendEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/friends").WithTags("Friends").RequireAuthorization();

        group.MapGet("/", async (FriendService friends, ICurrentUser currentUser, CancellationToken cancellationToken) =>
            Results.Ok(await friends.GetOverviewAsync(currentUser.RequireUserId(), cancellationToken)))
            .WithName("GetFriends");

        group.MapPost("/request", async (
            FriendRequestDto request,
            FriendService friends,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await friends.RequestAsync(currentUser.RequireUserId(), request.UserHandle ?? string.Empty, cancellationToken)))
            .WithName("SendFriendRequest");

        group.MapPost("/{friendshipId:guid}/respond", async (
            Guid friendshipId,
            RespondRequest request,
            FriendService friends,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await friends.RespondAsync(currentUser.RequireUserId(), friendshipId, request.Action ?? string.Empty, cancellationToken)))
            .WithName("RespondToFriendRequest");

        return app;
    }
}
