using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YuChat.Domain;

namespace YuChat.Data.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Login).HasMaxLength(64).IsRequired();
        builder.Property(x => x.LoginNormalized).HasMaxLength(64).IsRequired();
        builder.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
        builder.Property(x => x.UserHandle).HasMaxLength(24);
        builder.Property(x => x.UserHandleNormalized).HasMaxLength(24);

        builder.HasIndex(x => x.LoginNormalized).IsUnique();
        builder.HasIndex(x => x.UserHandleNormalized).IsUnique();
    }
}

public sealed class FriendshipConfiguration : IEntityTypeConfiguration<Friendship>
{
    public void Configure(EntityTypeBuilder<Friendship> builder)
    {
        builder.ToTable("Friendships");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.PairKey).HasMaxLength(65).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);

        builder.HasIndex(x => x.PairKey).IsUnique();
        builder.HasIndex(x => x.AddresseeId);

        builder.HasOne(x => x.Requester)
            .WithMany()
            .HasForeignKey(x => x.RequesterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Addressee)
            .WithMany()
            .HasForeignKey(x => x.AddresseeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ChatRoomConfiguration : IEntityTypeConfiguration<ChatRoom>
{
    public void Configure(EntityTypeBuilder<ChatRoom> builder)
    {
        builder.ToTable("ChatRooms");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(80);
        builder.Property(x => x.DirectKey).HasMaxLength(65);
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);

        builder.HasIndex(x => x.DirectKey).IsUnique();
        builder.HasIndex(x => x.SystemOwnerId).IsUnique();
        builder.HasIndex(x => x.LastMessageAt);
    }
}

public sealed class ChatMemberConfiguration : IEntityTypeConfiguration<ChatMember>
{
    public void Configure(EntityTypeBuilder<ChatMember> builder)
    {
        builder.ToTable("ChatMembers");

        builder.HasKey(x => new { x.ChatRoomId, x.UserId });

        builder.HasIndex(x => x.UserId);

        builder.HasOne(x => x.ChatRoom)
            .WithMany(x => x.Members)
            .HasForeignKey(x => x.ChatRoomId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ChatInvitationConfiguration : IEntityTypeConfiguration<ChatInvitation>
{
    public void Configure(EntityTypeBuilder<ChatInvitation> builder)
    {
        builder.ToTable("ChatInvitations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);

        // One invitation row per (room, invitee); re-inviting reactivates the existing row.
        builder.HasIndex(x => new { x.ChatRoomId, x.InviteeId }).IsUnique();
        builder.HasIndex(x => new { x.InviteeId, x.Status });

        builder.HasOne(x => x.ChatRoom)
            .WithMany()
            .HasForeignKey(x => x.ChatRoomId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Inviter)
            .WithMany()
            .HasForeignKey(x => x.InviterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Invitee)
            .WithMany()
            .HasForeignKey(x => x.InviteeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("Messages");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.SystemEvent).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.ReferenceName).HasMaxLength(80);
        builder.Property(x => x.CipherText).IsRequired();
        builder.Property(x => x.Nonce).HasMaxLength(12).IsRequired();
        builder.Property(x => x.Tag).HasMaxLength(16).IsRequired();

        // System events have no sender; the unique index must therefore only cover user messages.
        builder.HasIndex(x => new { x.SenderId, x.ClientMessageId })
            .IsUnique()
            .HasFilter("\"SenderId\" IS NOT NULL");
        builder.HasIndex(x => new { x.ChatRoomId, x.SentAt, x.Id });

        builder.HasOne(x => x.ChatRoom)
            .WithMany()
            .HasForeignKey(x => x.ChatRoomId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Sender)
            .WithMany()
            .HasForeignKey(x => x.SenderId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();

        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.ExpiresAt });

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
