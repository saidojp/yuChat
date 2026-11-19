namespace YuChat.Domain;

public sealed class Message
{
    public Guid Id { get; set; }

    public Guid ChatRoomId { get; set; }
    public ChatRoom ChatRoom { get; set; } = null!;

    public MessageKind Kind { get; set; }

    /// <summary>Author of a user message; null for system events.</summary>
    public Guid? SenderId { get; set; }
    public User? Sender { get; set; }

    /// <summary>AES-256-GCM ciphertext. Never contains plaintext. Empty for system events.</summary>
    public byte[] CipherText { get; set; } = [];

    public byte[] Nonce { get; set; } = [];

    public byte[] Tag { get; set; } = [];

    /// <summary>Version of the encryption key used for this message, enables key rotation.</summary>
    public int KeyVersion { get; set; }

    /// <summary>Client-generated idempotency key, unique per sender.</summary>
    public Guid ClientMessageId { get; set; }

    public DateTime SentAt { get; set; }

    // ---- system event payload (Kind == System) ----

    public SystemEvent? SystemEvent { get; set; }

    /// <summary>Friendship id, invitation id or chat room id the event refers to.</summary>
    public Guid? ReferenceId { get; set; }

    /// <summary>Room name snapshot for "invited you to X" and "added you to X".</summary>
    public string? ReferenceName { get; set; }

    /// <summary>User who triggered the event.</summary>
    public Guid? ActorId { get; set; }
}
