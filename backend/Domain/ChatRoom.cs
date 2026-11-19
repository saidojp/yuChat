namespace YuChat.Domain;

public sealed class ChatRoom
{
    public Guid Id { get; set; }

    /// <summary>Null for direct and system rooms.</summary>
    public string? Name { get; set; }

    public ChatRoomKind Kind { get; set; }

    public Guid CreatedById { get; set; }

    /// <summary>Deterministic "{min}:{max}" key for direct chats; unique index guards against duplicate rooms.</summary>
    public string? DirectKey { get; set; }

    /// <summary>Owner of a system feed; unique index guarantees one feed per user.</summary>
    public Guid? SystemOwnerId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime LastMessageAt { get; set; }

    /// <summary>Denormalized pointer to the newest message, keeps the chat list query free of N+1.</summary>
    public Guid? LastMessageId { get; set; }

    public List<ChatMember> Members { get; set; } = [];

    public bool IsGroup => Kind == ChatRoomKind.Group;

    public bool IsSystem => Kind == ChatRoomKind.System;

    public static string BuildDirectKey(Guid a, Guid b) =>
        a.CompareTo(b) <= 0 ? $"{a:N}:{b:N}" : $"{b:N}:{a:N}";
}
