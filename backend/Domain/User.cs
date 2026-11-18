namespace YuChat.Domain;

public sealed class User
{
    public Guid Id { get; set; }

    public required string Login { get; set; }

    /// <summary>Lower-cased login used for uniqueness and lookups.</summary>
    public required string LoginNormalized { get; set; }

    public required string PasswordHash { get; set; }

    /// <summary>Public user id (for example <c>alex_dev</c>). Null until onboarding completes.</summary>
    public string? UserHandle { get; set; }

    /// <summary>Lower-cased handle used for uniqueness and search.</summary>
    public string? UserHandleNormalized { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? HandleSetAt { get; set; }
}
