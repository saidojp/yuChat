namespace YuChat.Domain;

public sealed class Message
{
    public Guid Id { get; set; }

    public Guid ChatRoomId { get; set; }
    public ChatRoom ChatRoom { get; set; } = null!;

    public Guid SenderId { get; set; }
    public User Sender { get; set; } = null!;

    /// <summary>AES-256-GCM ciphertext. Never contains plaintext.</summary>
    public byte[] CipherText { get; set; } = [];

    public byte[] Nonce { get; set; } = [];

    public byte[] Tag { get; set; } = [];

    /// <summary>Version of the encryption key used for this message, enables key rotation.</summary>
    public int KeyVersion { get; set; }

    /// <summary>Client-generated idempotency key, unique per sender.</summary>
    public Guid ClientMessageId { get; set; }

    public DateTime SentAt { get; set; }
}
