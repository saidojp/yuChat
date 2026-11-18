namespace YuChat.Domain;

/// <summary>
/// Informational entry only. Friend requests and chat invitations are actionable
/// and are derived from <see cref="Friendship"/> / <see cref="ChatInvitation"/>.
/// </summary>
public sealed class Notification
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public NotificationType Type { get; set; }

    /// <summary>Friendship id, chat room id or invitation id depending on <see cref="Type"/>.</summary>
    public Guid ReferenceId { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; }
}
