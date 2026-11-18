namespace YuChat.Domain;

public sealed class ChatRoom
{
    public Guid Id { get; set; }

    /// <summary>Null for 1-on-1 chats.</summary>
    public string? Name { get; set; }

    public bool IsGroup { get; set; }

    public Guid CreatedById { get; set; }

    /// <summary>Deterministic "{min}:{max}" key for direct chats; unique index guards against duplicate rooms.</summary>
    public string? DirectKey { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime LastMessageAt { get; set; }

    /// <summary>Denormalized pointer to the newest message, keeps the chat list query free of N+1.</summary>
    public Guid? LastMessageId { get; set; }

    public List<ChatMember> Members { get; set; } = [];

    public static string BuildDirectKey(Guid a, Guid b) =>
        a.CompareTo(b) <= 0 ? $"{a:N}:{b:N}" : $"{b:N}:{a:N}";
}
