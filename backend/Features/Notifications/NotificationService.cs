using Microsoft.EntityFrameworkCore;
using YuChat.Data;
using YuChat.Domain;
using YuChat.Features;

namespace YuChat.Features.Notifications;

public sealed class NotificationService(AppDbContext db)
{
    /// <summary>
    /// Adds an informational notification. Actionable items (friend requests, chat invitations)
    /// are derived from their own tables and are never duplicated here.
    /// </summary>
    public void Add(Guid userId, NotificationType type, Guid referenceId, DateTime now)
    {
        db.Notifications.Add(new Notification
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Type = type,
            ReferenceId = referenceId,
            CreatedAt = now,
        });
    }

    public async Task<NotificationsPageDto> GetAsync(Guid userId, int take, CancellationToken cancellationToken)
    {
        var items = await db.Notifications
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(Math.Clamp(take, 1, 100))
            .Select(x => new { x.Id, x.Type, x.ReferenceId, x.IsRead, x.CreatedAt })
            .ToListAsync(cancellationToken);

        var unreadCount = await db.Notifications
            .AsNoTracking()
            .CountAsync(x => x.UserId == userId && !x.IsRead, cancellationToken);

        var actors = await ResolveActorsAsync(items.Select(x => (x.Type, x.ReferenceId)), cancellationToken);

        var dtos = items
            .Select(x => new NotificationDto(
                x.Id,
                x.Type.ToString(),
                x.ReferenceId,
                actors.GetValueOrDefault((x.Type, x.ReferenceId)),
                x.IsRead,
                x.CreatedAt))
            .ToList();

        return new NotificationsPageDto(dtos, unreadCount);
    }

    public async Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken)
    {
        await db.Notifications
            .Where(x => x.UserId == userId && !x.IsRead)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsRead, true), cancellationToken);
    }

    public async Task<NotificationDto> BuildAsync(Notification notification, CancellationToken cancellationToken)
    {
        var actors = await ResolveActorsAsync([(notification.Type, notification.ReferenceId)], cancellationToken);

        return new NotificationDto(
            notification.Id,
            notification.Type.ToString(),
            notification.ReferenceId,
            actors.GetValueOrDefault((notification.Type, notification.ReferenceId)),
            notification.IsRead,
            notification.CreatedAt);
    }

    private async Task<Dictionary<(NotificationType, Guid), string>> ResolveActorsAsync(
        IEnumerable<(NotificationType Type, Guid ReferenceId)> items,
        CancellationToken cancellationToken)
    {
        var list = items.Distinct().ToArray();

        var friendshipIds = list.Where(x => x.Type == NotificationType.FriendAccepted).Select(x => x.ReferenceId).ToArray();
        var roomIds = list.Where(x => x.Type == NotificationType.AddedToGroup).Select(x => x.ReferenceId).ToArray();
        var invitationIds = list.Where(x => x.Type == NotificationType.InvitationAccepted).Select(x => x.ReferenceId).ToArray();

        var friendshipActors = await db.Friendships
            .AsNoTracking()
            .Where(x => friendshipIds.Contains(x.Id))
            .Select(x => new { x.Id, Handle = x.Addressee.UserHandle ?? x.Addressee.Login })
            .ToDictionaryAsync(x => x.Id, x => x.Handle, cancellationToken);

        var roomCreators = await db.ChatRooms
            .AsNoTracking()
            .Where(x => roomIds.Contains(x.Id))
            .Select(x => new { x.Id, x.CreatedById })
            .ToDictionaryAsync(x => x.Id, x => x.CreatedById, cancellationToken);

        var invitationActors = await db.ChatInvitations
            .AsNoTracking()
            .Where(x => invitationIds.Contains(x.Id))
            .Select(x => new { x.Id, Handle = x.Invitee.UserHandle ?? x.Invitee.Login })
            .ToDictionaryAsync(x => x.Id, x => x.Handle, cancellationToken);

        var creatorIds = roomCreators.Values.Distinct().ToArray();
        var creatorHandles = await db.Users
            .AsNoTracking()
            .Where(x => creatorIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.UserHandle ?? x.Login, cancellationToken);

        var result = new Dictionary<(NotificationType, Guid), string>();

        foreach (var (type, referenceId) in list)
        {
            var handle = type switch
            {
                NotificationType.FriendAccepted => friendshipActors.GetValueOrDefault(referenceId),
                NotificationType.AddedToGroup => roomCreators.TryGetValue(referenceId, out var creatorId)
                    ? creatorHandles.GetValueOrDefault(creatorId)
                    : null,
                NotificationType.InvitationAccepted => invitationActors.GetValueOrDefault(referenceId),
                _ => null,
            };

            if (handle is not null)
            {
                result[(type, referenceId)] = handle;
            }
        }

        return result;
    }
}
