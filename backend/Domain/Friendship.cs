namespace YuChat.Domain;

public sealed class Friendship
{
    public Guid Id { get; set; }

    public Guid RequesterId { get; set; }
    public User Requester { get; set; } = null!;

    public Guid AddresseeId { get; set; }
    public User Addressee { get; set; } = null!;

    /// <summary>Deterministic "{min}:{max}" pair key with a unique index. One row per relationship.</summary>
    public required string PairKey { get; set; }

    public FriendshipStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? RespondedAt { get; set; }

    /// <summary>True when the last transition was initiated by the requester (used when a declined pair is re-opened).</summary>
    public static string BuildPairKey(Guid a, Guid b) =>
        a.CompareTo(b) <= 0 ? $"{a:N}:{b:N}" : $"{b:N}:{a:N}";
}
