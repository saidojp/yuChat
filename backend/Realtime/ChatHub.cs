using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using YuChat.Features;
using YuChat.Features.Chats;
using YuChat.Infrastructure.Security;
using YuChat.Realtime;

namespace YuChat.Realtime;

[Authorize]
public sealed class ChatHub(ChatService chats, ChatAccessService access, PresenceTracker presence, RealtimeNotifier notifier) : Hub
{
    public static string GroupName(Guid chatRoomId) => $"chat:{chatRoomId:N}";

    public static string UserGroupName(Guid userId) => $"user:{userId:N}";

    public override async Task OnConnectedAsync()
    {
        var userId = RequireUserId();

        presence.AddConnection(userId, Context.ConnectionId);

        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroupName(userId));

        foreach (var chatId in await chats.GetChatIdsForUserAsync(userId, CancellationToken.None))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(chatId));
        }

        // Tell the caller who is already online, then announce this user to co-members.
        await Clients.Caller.SendAsync(RealtimeEvents.PresenceSnapshot, new PresenceSnapshotDto(presence.OnlineUsers()));

        var coMembers = await chats.GetCoMemberIdsAsync(userId, CancellationToken.None);
        await notifier.ToUsersAsync(coMembers, RealtimeEvents.PresenceChanged, new PresenceChangedDto(userId, true));

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (TryGetUserId(out var userId) && presence.RemoveConnection(userId, Context.ConnectionId))
        {
            var coMembers = await chats.GetCoMemberIdsAsync(userId, CancellationToken.None);
            await notifier.ToUsersAsync(coMembers, RealtimeEvents.PresenceChanged, new PresenceChangedDto(userId, false));
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Online co-members of the caller. Used when a chat is opened so the UI shows the current
    /// status even if the peer connected before the chat existed (no presence event was sent then).
    /// </summary>
    public async Task<IReadOnlyList<Guid>> GetOnlineCoMembers()
    {
        var userId = RequireUserId();
        var coMembers = await chats.GetCoMemberIdsAsync(userId, CancellationToken.None);

        return coMembers.Where(presence.IsOnline).ToList();
    }

    public async Task JoinChat(Guid chatId)
    {
        var userId = RequireUserId();

        await access.RequireMemberAsync(chatId, userId, CancellationToken.None);

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(chatId));
    }

    public async Task<SendMessageAck> SendMessage(Guid chatId, Guid clientMessageId, string content)
    {
        var userId = RequireUserId();

        var result = await chats.SendMessageAsync(chatId, userId, clientMessageId, content, CancellationToken.None);

        await notifier.ToChatAsync(chatId, RealtimeEvents.ReceiveMessage, result.Message);

        return new SendMessageAck(result.Message.ChatRoomId, result.Message.Id, result.Message.ClientMessageId, result.Message.SentAt);
    }

    public async Task Typing(Guid chatId, bool isTyping)
    {
        var userId = RequireUserId();

        await access.RequireMemberAsync(chatId, userId, CancellationToken.None);

        // Resolve the handle from the database instead of trusting the JWT claim: the token was
        // issued at login time and does not reflect a handle claimed later during onboarding.
        var handles = await access.GetHandlesAsync([userId], CancellationToken.None);
        var handle = handles.GetValueOrDefault(userId, string.Empty);

        // Delivered to member ids rather than to the SignalR group: a member who joined a group
        // but has not opened it yet is subscribed on connect only, so group-targeted fan-out can
        // miss connections after membership changes. Targeting users is deterministic.
        var memberIds = await chats.GetMemberIdsAsync(chatId, CancellationToken.None);

        await notifier.ToUsersAsync(
            memberIds.Where(id => id != userId),
            RealtimeEvents.TypingChanged,
            new TypingEventDto(chatId, userId, handle, isTyping));
    }

    public async Task<MessageReadDto> MarkRead(Guid chatId, Guid lastReadMessageId)
    {
        var userId = RequireUserId();

        var result = await chats.MarkReadAsync(chatId, userId, lastReadMessageId, CancellationToken.None);

        await notifier.ToChatAsync(chatId, RealtimeEvents.MessageRead, result);

        return result;
    }

    private Guid RequireUserId() => TryGetUserId(out var id)
        ? id
        : throw new HubException("unauthorized");

    private bool TryGetUserId(out Guid userId)
    {
        var value = Context.User?.FindFirst("sub")?.Value ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(value, out userId);
    }
}
