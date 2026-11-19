using Microsoft.EntityFrameworkCore;
using YuChat.Data;
using YuChat.Domain;
using YuChat.Features;
using YuChat.Realtime;

namespace YuChat.Features.Notifications;

/// <summary>
/// Per-user feed that behaves like a chat: one system room per user, events appended as
/// <see cref="MessageKind.System"/> messages. Only the event type and references are persisted,
/// so the timeline renders in whatever language the reader has selected.
/// </summary>
public sealed class SystemFeedService(
    AppDbContext db,
    RealtimeNotifier realtime,
    TimeProvider time)
{
    private DateTime Now => time.GetUtcNow().UtcDateTime;

    /// <summary>Returns the caller's system room, creating it on first use.</summary>
    public async Task<ChatRoom> EnsureSystemRoomAsync(Guid userId, CancellationToken cancellationToken)
    {
        var existing = await db.ChatRooms
            .SingleOrDefaultAsync(x => x.SystemOwnerId == userId, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var now = Now;

        var room = new ChatRoom
        {
            Id = Guid.CreateVersion7(),
            Kind = ChatRoomKind.System,
            CreatedById = userId,
            SystemOwnerId = userId,
            CreatedAt = now,
            LastMessageAt = now,
        };

        db.ChatRooms.Add(room);
        db.ChatMembers.Add(new ChatMember
        {
            ChatRoomId = room.Id,
            UserId = userId,
            JoinedAt = now,
            LastReadAt = now,
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (Features.Users.DbErrors.IsUniqueViolation(exception))
        {
            // Two concurrent requests raced on first use: the other one created the room.
            return await db.ChatRooms.SingleAsync(x => x.SystemOwnerId == userId, cancellationToken);
        }

        return room;
    }

    /// <summary>
    /// Appends an event to the recipient's feed. Does not call SaveChanges: the caller controls the
    /// transaction so the event lands atomically with the business change it describes.
    /// </summary>
    public async Task<Message> AppendAsync(
        Guid recipientId,
        SystemEvent systemEvent,
        Guid? referenceId,
        Guid? actorId,
        string? referenceName,
        CancellationToken cancellationToken)
    {
        var room = await EnsureSystemRoomAsync(recipientId, cancellationToken);
        var now = Now;

        var message = new Message
        {
            Id = Guid.CreateVersion7(),
            ChatRoomId = room.Id,
            Kind = MessageKind.System,
            SenderId = null,
            SystemEvent = systemEvent,
            ReferenceId = referenceId,
            ReferenceName = referenceName,
            ActorId = actorId,
            ClientMessageId = Guid.CreateVersion7(),
            SentAt = now,
        };

        db.Messages.Add(message);

        room.LastMessageAt = now;
        room.LastMessageId = message.Id;

        return message;
    }

    /// <summary>Pushes an already persisted event to its owner after the transaction committed.</summary>
    public async Task PublishAsync(Guid recipientId, Message message, CancellationToken cancellationToken)
    {
        var dto = await BuildDtoAsync(message, cancellationToken);

        await realtime.ToUserAsync(recipientId, RealtimeEvents.ReceiveMessage, dto, cancellationToken);
    }

    public async Task<MessageDto> BuildDtoAsync(Message message, CancellationToken cancellationToken)
    {
        var actorHandle = message.ActorId is null
            ? null
            : await db.Users
                .AsNoTracking()
                .Where(x => x.Id == message.ActorId)
                .Select(x => x.UserHandle ?? x.Login)
                .SingleOrDefaultAsync(cancellationToken);

        var systemEvent = message.SystemEvent is null
            ? null
            : new SystemEventPreviewDto(
                message.SystemEvent.Value.ToString(),
                message.ActorId,
                actorHandle,
                message.ReferenceId,
                message.ReferenceName);

        return new MessageDto(
            message.Id,
            message.ChatRoomId,
            message.Kind.ToString(),
            message.SenderId,
            actorHandle,
            null,
            message.ClientMessageId,
            message.SentAt,
            systemEvent);
    }

    public async Task<Dictionary<Guid, string>> ActorHandlesAsync(
        IEnumerable<Guid?> actorIds,
        CancellationToken cancellationToken)
    {
        var ids = actorIds.Where(x => x is not null).Select(x => x!.Value).Distinct().ToArray();

        if (ids.Length == 0)
        {
            return [];
        }

        return await db.Users
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.UserHandle ?? x.Login, cancellationToken);
    }
}
