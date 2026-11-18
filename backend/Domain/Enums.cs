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

/// <summary>Informational notifications only. Actionable items live in their own tables.</summary>
public enum NotificationType
{
    FriendAccepted = 0,
    AddedToGroup = 1,
    InvitationAccepted = 2,
}
