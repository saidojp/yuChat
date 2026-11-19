namespace YuChat.Domain;

public enum FriendshipStatus
{
    Pending = 0,
    Accepted = 1,
    Declined = 2,
}

public enum InvitationStatus
{
    Pending = 0,
    Accepted = 1,
    Declined = 2,
}

public enum ChatRoomKind
{
    /// <summary>One-on-one conversation between two friends.</summary>
    Direct = 0,

    /// <summary>Named multi-user room.</summary>
    Group = 1,

    /// <summary>Per-user feed for friend requests, invitations and other account events.</summary>
    System = 2,
}

public enum MessageKind
{
    /// <summary>Encrypted user-authored text.</summary>
    User = 0,

    /// <summary>Structured event; renders from <see cref="SystemEvent"/> plus references, never from stored text.</summary>
    System = 1,
}

/// <summary>
/// Event types rendered in a user's system feed. Only the type and references are stored, so the
/// feed is re-rendered in the viewer's language at any time.
/// </summary>
public enum SystemEvent
{
    FriendRequestReceived = 0,
    FriendRequestAccepted = 1,
    FriendRequestDeclined = 2,
    FriendRequestCancelled = 3,
    ChatInvitationReceived = 4,
    ChatInvitationAccepted = 5,
    ChatInvitationDeclined = 6,
    AddedToGroup = 7,
    FriendRemoved = 8,
}
