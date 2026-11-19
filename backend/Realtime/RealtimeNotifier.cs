using Microsoft.AspNetCore.SignalR;
using YuChat.Features;

namespace YuChat.Realtime;

/// <summary>
/// Wraps SignalR so HTTP handlers can push events only after their database
/// transaction committed. Failures are logged and never bubble into the API response.
/// </summary>
public sealed class RealtimeNotifier(IHubContext<ChatHub> hub, ILogger<RealtimeNotifier> logger)
{
    public Task ToUserAsync(Guid userId, string method, object payload, CancellationToken cancellationToken = default) =>
        SendAsync(() => hub.Clients.User(userId.ToString()).SendCoreAsync(method, [payload], cancellationToken));

    public Task ToUsersAsync(IEnumerable<Guid> userIds, string method, object payload, CancellationToken cancellationToken = default)
    {
        var targets = userIds.Distinct().Select(x => x.ToString()).ToArray();
        return targets.Length == 0
            ? Task.CompletedTask
            : SendAsync(() => hub.Clients.Users(targets).SendCoreAsync(method, [payload], cancellationToken));
    }

    public Task ToChatAsync(Guid chatRoomId, string method, object payload, CancellationToken cancellationToken = default) =>
        SendAsync(() => hub.Clients.Group(ChatHub.GroupName(chatRoomId)).SendCoreAsync(method, [payload], cancellationToken));

    private async Task SendAsync(Func<Task> send)
    {
        try
        {
            await send();
        }
        catch (Exception exception)
        {
            // Realtime is best-effort: HTTP writes already succeeded, clients refetch on reconnect.
            logger.LogWarning(exception, "Failed to dispatch a realtime event");
        }
    }
}

public static class RealtimeEvents
{
    public const string ReceiveMessage = "ReceiveMessage";
    public const string ChatCreated = "ChatCreated";
    public const string ChatUpdated = "ChatUpdated";
    public const string MessageRead = "MessageRead";
    public const string TypingChanged = "TypingChanged";
    public const string PresenceChanged = "PresenceChanged";
    public const string PresenceSnapshot = "PresenceSnapshot";
    public const string FriendRequestUpdated = "FriendRequestUpdated";
    public const string InvitationUpdated = "InvitationUpdated";
    public const string InvitationReceived = "InvitationReceived";
}
