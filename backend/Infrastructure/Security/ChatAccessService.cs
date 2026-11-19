using Microsoft.EntityFrameworkCore;
using YuChat.Data;
using YuChat.Domain;
using YuChat.Infrastructure.Errors;

namespace YuChat.Infrastructure.Security;

public sealed record ChatMembershipContext(ChatRoom Room, ChatMember Member, DateTime HistoryFloor);

/// <summary>
/// Single source of truth for chat authorization. Every controller and hub method
/// that touches a room, message or invitation must go through this service
/// to avoid duplicated (and eventually forgotten) access checks.
/// </summary>
public sealed class ChatAccessService(AppDbContext db)
{
    public async Task<ChatMembershipContext> RequireMemberAsync(Guid chatRoomId, Guid userId, CancellationToken cancellationToken)
    {
        var member = await db.ChatMembers
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.ChatRoomId == chatRoomId && x.UserId == userId, cancellationToken)
            ?? throw AppException.NotFound("Chat was not found.");

        var room = await db.ChatRooms
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == chatRoomId, cancellationToken)
            ?? throw AppException.NotFound("Chat was not found.");

        return new ChatMembershipContext(room, member, member.JoinedAt);
    }

    public async Task<ChatRoom> RequireGroupOwnerAsync(Guid chatRoomId, Guid userId, CancellationToken cancellationToken)
    {
        var room = await db.ChatRooms
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == chatRoomId, cancellationToken)
            ?? throw AppException.NotFound("Chat was not found.");

        if (!room.IsGroup)
        {
            throw AppException.BadRequest(ErrorCodes.Unsupported, "This operation is only available for group chats.");
        }

        if (room.CreatedById != userId)
        {
            throw AppException.Forbidden("Only the group creator can perform this action.");
        }

        return room;
    }

    public async Task<ChatInvitation> RequireInviteeAsync(Guid invitationId, Guid userId, CancellationToken cancellationToken)
    {
        var invitation = await db.ChatInvitations
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == invitationId, cancellationToken)
            ?? throw AppException.NotFound("Invitation was not found.");

        if (invitation.InviteeId != userId)
        {
            // Do not reveal that the invitation exists for somebody else.
            throw AppException.NotFound("Invitation was not found.");
        }

        return invitation;
    }

    public async Task<bool> AreFriendsAsync(Guid a, Guid b, CancellationToken cancellationToken)
    {
        if (a == b)
        {
            return false;
        }

        var pairKey = Friendship.BuildPairKey(a, b);

        return await db.Friendships
            .AsNoTracking()
            .AnyAsync(x => x.PairKey == pairKey && x.Status == FriendshipStatus.Accepted, cancellationToken);
    }

    public async Task<Dictionary<Guid, string>> GetHandlesAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToArray();

        return await db.Users
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.UserHandle ?? x.Login, cancellationToken);
    }

    /// <summary>Nullable-friendly overload: system events have either no sender or no actor.</summary>
    public Task<Dictionary<Guid, string>> GetHandlesAsync(IEnumerable<Guid?> userIds, CancellationToken cancellationToken) =>
        GetHandlesAsync(userIds.Where(x => x.HasValue).Select(x => x!.Value), cancellationToken);
}
