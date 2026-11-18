namespace YuChat.Domain;

public sealed class ChatMember
{
    public Guid ChatRoomId { get; set; }
    public ChatRoom ChatRoom { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public DateTime JoinedAt { get; set; }

    /// <summary>Watermark for unread counters. Messages after this instant are unread.</summary>
    public DateTime LastReadAt { get; set; }

    public Guid? LastReadMessageId { get; set; }
}
