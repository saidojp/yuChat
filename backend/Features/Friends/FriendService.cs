using Microsoft.EntityFrameworkCore;
using YuChat.Data;
using YuChat.Domain;
using YuChat.Features.Notifications;
using YuChat.Infrastructure.Errors;
using YuChat.Features.Users;
using YuChat.Infrastructure.Security;
using YuChat.Realtime;

namespace YuChat.Features.Friends;

public sealed class FriendService(
    AppDbContext db,
    NotificationService notifications,
    RealtimeNotifier realtime,
    TimeProvider time)
{
    public async Task<FriendsOverviewDto> GetOverviewAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rows = await db.Friendships
            .AsNoTracking()
            .Where(x => x.RequesterId == userId || x.AddresseeId == userId)
            .Where(x => x.Status != FriendshipStatus.Declined)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.RequesterId,
                x.AddresseeId,
                x.Status,
                x.CreatedAt,
                Other = x.RequesterId == userId
                    ? new { x.Addressee.Id, Handle = x.Addressee.UserHandle ?? x.Addressee.Login }
                    : new { x.Requester.Id, Handle = x.Requester.UserHandle ?? x.Requester.Login },
            })
            .ToListAsync(cancellationToken);

        var dtos = rows
            .Select(x => new FriendshipDto(
                x.Id,
                new UserSearchResultDto(
                    x.Other.Id,
                    x.Other.Handle,
                    x.Status == FriendshipStatus.Accepted
                        ? FriendshipState.Friends
                        : x.RequesterId == userId ? FriendshipState.PendingSent : FriendshipState.PendingReceived,
                    x.Id),
                x.Status.ToString(),
                x.AddresseeId == userId,
                x.CreatedAt))
            .ToList();

        return new FriendsOverviewDto(
            dtos.Where(x => x.Status == nameof(FriendshipStatus.Accepted)).ToList(),
            dtos.Where(x => x.Status == nameof(FriendshipStatus.Pending) && x.IsIncoming).ToList(),
            dtos.Where(x => x.Status == nameof(FriendshipStatus.Pending) && !x.IsIncoming).ToList());
    }

    public async Task<FriendshipDto> RequestAsync(Guid requesterId, string rawHandle, CancellationToken cancellationToken)
    {
        var handle = UserService.Normalize(rawHandle);

        var target = await db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserHandleNormalized == handle, cancellationToken)
            ?? throw AppException.NotFound("No user with this ID exists.", ErrorCodes.UserNotFound);

        if (target.Id == requesterId)
        {
            throw AppException.BadRequest(ErrorCodes.SelfAction, "You cannot send a friend request to yourself.");
        }

        var pairKey = Friendship.BuildPairKey(requesterId, target.Id);
        var now = time.GetUtcNow().UtcDateTime;

        var existing = await db.Friendships.SingleOrDefaultAsync(x => x.PairKey == pairKey, cancellationToken);

        if (existing is not null)
        {
            switch (existing.Status)
            {
                case FriendshipStatus.Accepted:
                    throw AppException.Conflict(ErrorCodes.AlreadyFriends, "You are already friends.");

                case FriendshipStatus.Pending when existing.RequesterId == requesterId:
                    throw AppException.Conflict(ErrorCodes.RequestPending, "A friend request is already pending.");

                case FriendshipStatus.Pending:
                    // The other side already asked us: accept it instead of creating a mirrored request.
                    existing.Status = FriendshipStatus.Accepted;
                    existing.RespondedAt = now;

                    var self = await db.Users.AsNoTracking().SingleAsync(x => x.Id == requesterId, cancellationToken);
                    var acceptedNotification = new Notification
                    {
                        Id = Guid.CreateVersion7(),
                        UserId = target.Id,
                        Type = NotificationType.FriendAccepted,
                        ReferenceId = existing.Id,
                        CreatedAt = now,
                    };

                    db.Notifications.Add(acceptedNotification);
                    await db.SaveChangesAsync(cancellationToken);

                    await realtime.ToUserAsync(requesterId, RealtimeEvents.FriendRequestUpdated, BuildDto(existing, target.Id, target.UserHandle!, FriendshipState.Friends, true), cancellationToken);
                    await realtime.ToUserAsync(target.Id, RealtimeEvents.FriendRequestUpdated, BuildDto(existing, self.Id, self.UserHandle ?? self.Login, FriendshipState.Friends, true), cancellationToken);
                    await realtime.ToUserAsync(target.Id, RealtimeEvents.Notification, await notifications.BuildAsync(acceptedNotification, cancellationToken), cancellationToken);

                    return new FriendshipDto(
                        existing.Id,
                        new UserSearchResultDto(target.Id, target.UserHandle!, FriendshipState.Friends, existing.Id),
                        existing.Status.ToString(),
                        false,
                        existing.CreatedAt);

                default:
                    // Declined before: reopen the same row for the new requester.
                    existing.Status = FriendshipStatus.Pending;
                    existing.RequesterId = requesterId;
                    existing.AddresseeId = target.Id;
                    existing.RespondedAt = null;
                    existing.CreatedAt = now;
                    break;
            }
        }
        else
        {
            existing = new Friendship
            {
                Id = Guid.CreateVersion7(),
                RequesterId = requesterId,
                AddresseeId = target.Id,
                PairKey = pairKey,
                Status = FriendshipStatus.Pending,
                CreatedAt = now,
            };

            db.Friendships.Add(existing);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DbErrors.IsUniqueViolation(exception))
        {
            throw AppException.Conflict(ErrorCodes.RequestPending, "A friend request is already pending.");
        }

        var requester = await db.Users.AsNoTracking().SingleAsync(x => x.Id == requesterId, cancellationToken);

        await realtime.ToUserAsync(target.Id, RealtimeEvents.FriendRequestUpdated, new FriendshipDto(
            existing.Id,
            new UserSearchResultDto(requesterId, requester.UserHandle ?? requester.Login, FriendshipState.PendingReceived, existing.Id),
            existing.Status.ToString(),
            true,
            existing.CreatedAt), cancellationToken);

        return new FriendshipDto(
            existing.Id,
            new UserSearchResultDto(target.Id, target.UserHandle!, FriendshipState.PendingSent, existing.Id),
            existing.Status.ToString(),
            false,
            existing.CreatedAt);
    }

    public async Task<FriendshipDto> RespondAsync(Guid userId, Guid friendshipId, string action, CancellationToken cancellationToken)
    {
        var friendship = await db.Friendships
            .Include(x => x.Requester)
            .Include(x => x.Addressee)
            .SingleOrDefaultAsync(x => x.Id == friendshipId, cancellationToken)
            ?? throw AppException.NotFound("Friend request was not found.");

        if (friendship.AddresseeId != userId)
        {
            throw AppException.NotFound("Friend request was not found.");
        }

        if (friendship.Status != FriendshipStatus.Pending)
        {
            throw AppException.Conflict(ErrorCodes.Conflict, "This friend request has already been answered.");
        }

        var now = time.GetUtcNow().UtcDateTime;

        var accepted = action switch
        {
            "accept" => true,
            "decline" => false,
            _ => throw AppException.BadRequest(ErrorCodes.ValidationFailed, "Action must be 'accept' or 'decline'."),
        };

        friendship.Status = accepted ? FriendshipStatus.Accepted : FriendshipStatus.Declined;
        friendship.RespondedAt = now;

        Notification? notification = null;

        if (accepted)
        {
            notification = new Notification
            {
                Id = Guid.CreateVersion7(),
                UserId = friendship.RequesterId,
                Type = NotificationType.FriendAccepted,
                ReferenceId = friendship.Id,
                CreatedAt = now,
            };

            db.Notifications.Add(notification);
        }

        await db.SaveChangesAsync(cancellationToken);

        var selfHandle = friendship.Addressee.UserHandle ?? friendship.Addressee.Login;

        var selfDto = new FriendshipDto(
            friendship.Id,
            new UserSearchResultDto(
                friendship.RequesterId,
                friendship.Requester.UserHandle ?? friendship.Requester.Login,
                accepted ? FriendshipState.Friends : FriendshipState.None,
                friendship.Id),
            friendship.Status.ToString(),
            true,
            friendship.CreatedAt);

        var requesterDto = new FriendshipDto(
            friendship.Id,
            new UserSearchResultDto(
                friendship.AddresseeId,
                selfHandle,
                accepted ? FriendshipState.Friends : FriendshipState.None,
                friendship.Id),
            friendship.Status.ToString(),
            false,
            friendship.CreatedAt);

        await realtime.ToUserAsync(friendship.RequesterId, RealtimeEvents.FriendRequestUpdated, requesterDto, cancellationToken);
        await realtime.ToUserAsync(friendship.AddresseeId, RealtimeEvents.FriendRequestUpdated, selfDto, cancellationToken);

        if (notification is not null)
        {
            await realtime.ToUserAsync(friendship.RequesterId, RealtimeEvents.Notification, await notifications.BuildAsync(notification, cancellationToken), cancellationToken);
        }

        return selfDto;
    }

    internal async Task<IReadOnlyList<Guid>> GetFriendIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Friendships
            .AsNoTracking()
            .Where(x => x.Status == FriendshipStatus.Accepted && (x.RequesterId == userId || x.AddresseeId == userId))
            .Select(x => x.RequesterId == userId ? x.AddresseeId : x.RequesterId)
            .ToListAsync(cancellationToken);

    private static FriendshipDto BuildDto(Friendship friendship, Guid otherUserId, string otherHandle, FriendshipState state, bool isIncoming) =>
        new(friendship.Id, new UserSearchResultDto(otherUserId, otherHandle, state, friendship.Id), friendship.Status.ToString(), isIncoming, friendship.CreatedAt);
}
