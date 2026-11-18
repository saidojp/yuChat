namespace YuChat.Domain;

public sealed class RefreshToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>SHA-256 of the raw refresh token. The raw value is never stored.</summary>
    public required string TokenHash { get; set; }

    /// <summary>Groups tokens produced by rotation; a reused token revokes the whole family.</summary>
    public Guid FamilyId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public Guid? ReplacedByTokenId { get; set; }
}
