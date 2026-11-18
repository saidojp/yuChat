namespace YuChat.Features;

// ---- Auth & users ----

public sealed record RegisterRequest(string Login, string Password);

public sealed record LoginRequest(string Login, string Password);

public sealed record SetHandleRequest(string UserHandle);

public sealed record UserProfileDto(
    Guid Id,
    string Login,
    string? UserHandle,
    DateTime CreatedAt,
    bool NeedsOnboarding);

public sealed record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    UserProfileDto User);

public sealed record RefreshResponse(string AccessToken, DateTime AccessTokenExpiresAt);

public enum FriendshipState
{
    None = 0,
    PendingSent = 1,
    PendingReceived = 2,
    Friends = 3,
}

public sealed record UserSearchResultDto(
    Guid Id,
    string UserHandle,
    FriendshipState Friendship,
    Guid? FriendshipId);

// ---- Friends ----

public sealed record FriendRequestDto(string UserHandle);

public sealed record RespondRequest(string Action);

public sealed record FriendshipDto(
    Guid Id,
    UserSearchResultDto User,
    string Status,
    bool IsIncoming,
    DateTime CreatedAt);

public sealed record FriendsOverviewDto(
    IReadOnlyList<FriendshipDto> Friends,
    IReadOnlyList<FriendshipDto> Incoming,
    IReadOnlyList<FriendshipDto> Outgoing);

// ---- Chats ----

public sealed record DirectChatRequest(Guid TargetUserId);

public sealed record AdditionalGroupUser(string UserHandle, bool AlsoSendFriendRequest);

public sealed record CreateGroupRequest(
    string Name,
    IReadOnlyList<Guid> FriendIds,
    IReadOnlyList<AdditionalGroupUser>? AdditionalUsers);

public sealed record InvitationDecisionRequest(string Action);

public sealed record ChatParticipantDto(Guid UserId, string UserHandle);

public sealed record ChatSummaryDto(
    Guid Id,
    bool IsGroup,
    string Title,
    IReadOnlyList<ChatParticipantDto> Participants,
    string? LastMessagePreview,
    DateTime LastMessageAt,
    Guid? LastMessageSenderId,
    int UnreadCount);

public sealed record ChatDetailsDto(
    Guid Id,
    bool IsGroup,
    string Title,
    Guid CreatedById,
    IReadOnlyList<ChatParticipantDto> Participants,
    DateTime JoinedAt);

public sealed record MessageDto(
    Guid Id,
    Guid ChatRoomId,
    Guid SenderId,
    string SenderHandle,
    string Content,
    Guid ClientMessageId,
    DateTime SentAt);

public sealed record MessagePageDto(
    IReadOnlyList<MessageDto> Messages,
    bool HasMore);

public sealed record SendMessageAck(Guid ChatRoomId, Guid Id, Guid ClientMessageId, DateTime SentAt);

public sealed record ChatInvitationDto(
    Guid Id,
    Guid ChatRoomId,
    string ChatTitle,
    string InviterHandle,
    DateTime CreatedAt);

public sealed record GroupCreationResultDto(
    ChatSummaryDto Chat,
    IReadOnlyList<string> InvitedHandles);

// ---- Notifications ----

public sealed record NotificationDto(
    Guid Id,
    string Type,
    Guid ReferenceId,
    string? ActorHandle,
    bool IsRead,
    DateTime CreatedAt);

public sealed record NotificationsPageDto(
    IReadOnlyList<NotificationDto> Items,
    int UnreadCount);

// ---- Realtime payloads ----

public sealed record TypingEventDto(Guid ChatRoomId, Guid UserId, string UserHandle, bool IsTyping);

public sealed record PresenceEventDto(Guid UserId, bool IsOnline);

public sealed record PresenceChangedDto(Guid UserId, bool IsOnline);

public sealed record PresenceSnapshotDto(IReadOnlyList<Guid> OnlineUserIds);

public sealed record MessageReadDto(Guid ChatRoomId, Guid UserId, Guid LastReadMessageId, DateTime ReadAt);

// ---- Validation helpers ----

public static class InputRules
{
    public const int PasswordMinLength = 8;
    public const int PasswordMaxLength = 128;
    public const int LoginMinLength = 3;
    public const int LoginMaxLength = 64;
    public const int HandleMinLength = 4;
    public const int HandleMaxLength = 24;
    public const int MessageMaxLength = 4000;
    public const int GroupNameMaxLength = 80;
    public const int MaxGroupSize = 100;
    public const int MaxAdditionalUsers = 20;
    public const int MaxMessagePageSize = 50;

    public static readonly System.Text.RegularExpressions.Regex HandlePattern =
        new("^[a-zA-Z0-9_]{4,24}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static readonly System.Text.RegularExpressions.Regex LoginPattern =
        new("^[a-zA-Z0-9_.-]{3,64}$", System.Text.RegularExpressions.RegexOptions.Compiled);
}
