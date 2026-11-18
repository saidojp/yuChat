namespace YuChat.Domain;

public sealed class ChatInvitation
{
    public Guid Id { get; set; }

    public Guid ChatRoomId { get; set; }
    public ChatRoom ChatRoom { get; set; } = null!;

    public Guid InviterId { get; set; }
    public User Inviter { get; set; } = null!;

    public Guid InviteeId { get; set; }
    public User Invitee { get; set; } = null!;

    public InvitationStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? RespondedAt { get; set; }
}
