using Microsoft.EntityFrameworkCore;
using YuChat.Data;
using YuChat.Domain;
using YuChat.Infrastructure.Encryption;

namespace YuChat.Seed;

/// <summary>
/// Development seed: two friends with a direct chat plus a small group.
/// Usage: dotnet run -- --seed
/// </summary>
public static class SeedData
{
    public static async Task RunAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var encryption = scope.ServiceProvider.GetRequiredService<IMessageEncryptionService>();

        await db.Database.MigrateAsync();

        if (await db.Users.AnyAsync())
        {
            Console.WriteLine("Seed skipped: the database already contains users.");
            return;
        }

        var now = DateTime.UtcNow;
        var joinedAt = now.AddMinutes(-10);

        var alice = new User
        {
            Id = Guid.CreateVersion7(),
            Login = "alice",
            LoginNormalized = "alice",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123", 12),
            UserHandle = "alice_dev",
            UserHandleNormalized = "alice_dev",
            CreatedAt = now,
            HandleSetAt = now,
        };

        var bob = new User
        {
            Id = Guid.CreateVersion7(),
            Login = "bob",
            LoginNormalized = "bob",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123", 12),
            UserHandle = "bob_design",
            UserHandleNormalized = "bob_design",
            CreatedAt = now,
            HandleSetAt = now,
        };

        var carol = new User
        {
            Id = Guid.CreateVersion7(),
            Login = "carol",
            LoginNormalized = "carol",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123", 12),
            UserHandle = "carol_ops",
            UserHandleNormalized = "carol_ops",
            CreatedAt = now,
            HandleSetAt = now,
        };

        db.Users.AddRange(alice, bob, carol);

        db.Friendships.Add(new Friendship
        {
            Id = Guid.CreateVersion7(),
            RequesterId = alice.Id,
            AddresseeId = bob.Id,
            PairKey = Friendship.BuildPairKey(alice.Id, bob.Id),
            Status = FriendshipStatus.Accepted,
            CreatedAt = now,
            RespondedAt = now,
        });

        var direct = new ChatRoom
        {
            Id = Guid.CreateVersion7(),
            IsGroup = false,
            CreatedById = alice.Id,
            DirectKey = ChatRoom.BuildDirectKey(alice.Id, bob.Id),
            CreatedAt = joinedAt,
            LastMessageAt = joinedAt,
        };

        db.ChatRooms.Add(direct);

        db.ChatMembers.AddRange(
            new ChatMember { ChatRoomId = direct.Id, UserId = alice.Id, JoinedAt = joinedAt, LastReadAt = joinedAt },
            new ChatMember { ChatRoomId = direct.Id, UserId = bob.Id, JoinedAt = joinedAt, LastReadAt = joinedAt });

        var group = new ChatRoom
        {
            Id = Guid.CreateVersion7(),
            Name = "Design sync",
            IsGroup = true,
            CreatedById = alice.Id,
            CreatedAt = joinedAt,
            LastMessageAt = joinedAt,
        };

        db.ChatRooms.Add(group);

        db.ChatMembers.AddRange(
            new ChatMember { ChatRoomId = group.Id, UserId = alice.Id, JoinedAt = joinedAt, LastReadAt = joinedAt.AddMinutes(-3) },
            new ChatMember { ChatRoomId = group.Id, UserId = bob.Id, JoinedAt = joinedAt, LastReadAt = joinedAt.AddMinutes(-3) });

        await db.SaveChangesAsync();

        AddMessage(db, encryption, direct.Id, alice.Id, "Hey Bob! Welcome to YuChat. こんにちは!", now.AddMinutes(-5));
        var lastDirect = AddMessage(db, encryption, direct.Id, bob.Id, "Thanks! The UI looks great.", now.AddMinutes(-4));
        var lastGroup = AddMessage(db, encryption, group.Id, alice.Id, "Let's review the new palette tomorrow.", now.AddMinutes(-2));

        direct.LastMessageAt = lastDirect.SentAt;
        direct.LastMessageId = lastDirect.Id;
        group.LastMessageAt = lastGroup.SentAt;
        group.LastMessageId = lastGroup.Id;

        await db.SaveChangesAsync();

        Console.WriteLine("Seed complete.");
        Console.WriteLine("  alice / password123  (@alice_dev)");
        Console.WriteLine("  bob   / password123  (@bob_design)");
        Console.WriteLine("  carol / password123  (@carol_ops)");
    }

    private static Message AddMessage(
        AppDbContext db,
        IMessageEncryptionService encryption,
        Guid chatRoomId,
        Guid senderId,
        string text,
        DateTime sentAt)
    {
        var id = Guid.CreateVersion7();
        var payload = encryption.Encrypt(text, chatRoomId, id);

        var message = new Message
        {
            Id = id,
            ChatRoomId = chatRoomId,
            SenderId = senderId,
            CipherText = payload.CipherText,
            Nonce = payload.Nonce,
            Tag = payload.Tag,
            KeyVersion = payload.KeyVersion,
            ClientMessageId = Guid.CreateVersion7(),
            SentAt = sentAt,
        };

        db.Messages.Add(message);

        return message;
    }
}
