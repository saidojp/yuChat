using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using YuChat.Domain;
using YuChat.Features;
using YuChat.Infrastructure.Errors;

namespace YuChat.Api.Tests;

public sealed class AccessControlTests
{
    [Fact]
    public async Task Non_member_cannot_read_messages_of_a_foreign_chat()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");
        var bob = await harness.CreateUserAsync("bob", "bob_dev");
        var eve = await harness.CreateUserAsync("eve", "eve_dev");

        await using var scope = harness.Provider.CreateAsyncScope();
        var friends = scope.ServiceProvider.GetRequiredService<YuChat.Features.Friends.FriendService>();
        await friends.RequestAsync(alice.Id, "bob_dev", CancellationToken.None);
        await RespondAsync(harness, alice, bob);

        var chatService = scope.ServiceProvider.GetRequiredService<YuChat.Features.Chats.ChatService>();
        var chat = await chatService.StartDirectAsync(alice.Id, bob.Id, CancellationToken.None);
        await chatService.SendMessageAsync(chat.Id, alice.Id, Guid.CreateVersion7(), "private", CancellationToken.None);

        var exception = await Assert.ThrowsAsync<AppException>(() =>
            chatService.GetMessagesAsync(chat.Id, eve.Id, null, 30, CancellationToken.None));

        Assert.Equal(404, exception.StatusCode);
    }

    [Fact]
    public async Task Non_member_cannot_send_messages_into_a_foreign_chat()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");
        var bob = await harness.CreateUserAsync("bob", "bob_dev");
        var eve = await harness.CreateUserAsync("eve", "eve_dev");

        await using var scope = harness.Provider.CreateAsyncScope();
        var friends = scope.ServiceProvider.GetRequiredService<YuChat.Features.Friends.FriendService>();
        await friends.RequestAsync(alice.Id, "bob_dev", CancellationToken.None);
        await RespondAsync(harness, alice, bob);

        var chatService = scope.ServiceProvider.GetRequiredService<YuChat.Features.Chats.ChatService>();
        var chat = await chatService.StartDirectAsync(alice.Id, bob.Id, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<AppException>(() =>
            chatService.SendMessageAsync(chat.Id, eve.Id, Guid.CreateVersion7(), "intrusion", CancellationToken.None));

        Assert.Equal(404, exception.StatusCode);
    }

    [Fact]
    public async Task Direct_chat_requires_friendship()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");
        var bob = await harness.CreateUserAsync("bob", "bob_dev");

        var chatService = await harness.ChatServiceAsync();

        var exception = await Assert.ThrowsAsync<AppException>(() =>
            chatService.StartDirectAsync(alice.Id, bob.Id, CancellationToken.None));

        Assert.Equal(ErrorCodes.NotFriends, exception.Code);
    }

    [Fact]
    public async Task Starting_a_direct_chat_twice_reuses_the_room()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");
        var bob = await harness.CreateUserAsync("bob", "bob_dev");

        await MakeFriendsAsync(harness, alice, bob);

        var chatService = await harness.ChatServiceAsync();

        var first = await chatService.StartDirectAsync(alice.Id, bob.Id, CancellationToken.None);
        var second = await chatService.StartDirectAsync(bob.Id, alice.Id, CancellationToken.None);

        Assert.Equal(first.Id, second.Id);

        var count = await harness.WithDbAsync(db => db.ChatRooms.CountAsync(x => x.Kind == ChatRoomKind.Direct));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Invitee_only_sees_history_from_join_time()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");
        var bob = await harness.CreateUserAsync("bob", "bob_dev");
        var carol = await harness.CreateUserAsync("carol", "carol_dev");

        await MakeFriendsAsync(harness, alice, bob);

        var chatService = await harness.ChatServiceAsync();

        var group = await chatService.CreateGroupAsync(
            alice.Id,
            new CreateGroupRequest("Squad", [bob.Id], [new AdditionalGroupUser("carol_dev", false)]),
            CancellationToken.None);

        await chatService.SendMessageAsync(group.Chat.Id, alice.Id, Guid.CreateVersion7(), "before carol joined", CancellationToken.None);

        var invitation = await harness.WithDbAsync(db => db.ChatInvitations.SingleAsync());
        await chatService.RespondToInvitationAsync(carol.Id, invitation.Id, "accept", CancellationToken.None);

        await chatService.SendMessageAsync(group.Chat.Id, alice.Id, Guid.CreateVersion7(), "after carol joined", CancellationToken.None);

        var page = await chatService.GetMessagesAsync(group.Chat.Id, carol.Id, null, 30, CancellationToken.None);

        Assert.Single(page.Messages);
        Assert.Equal("after carol joined", page.Messages[0].Content);
    }

    [Fact]
    public async Task Pending_invitation_does_not_grant_access()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");
        var carol = await harness.CreateUserAsync("carol", "carol_dev");

        var chatService = await harness.ChatServiceAsync();

        var group = await chatService.CreateGroupAsync(
            alice.Id,
            new CreateGroupRequest("Squad", [], [new AdditionalGroupUser("carol_dev", false)]),
            CancellationToken.None);

        await chatService.SendMessageAsync(group.Chat.Id, alice.Id, Guid.CreateVersion7(), "members only", CancellationToken.None);

        var exception = await Assert.ThrowsAsync<AppException>(() =>
            chatService.GetMessagesAsync(group.Chat.Id, carol.Id, null, 30, CancellationToken.None));

        Assert.Equal(404, exception.StatusCode);
    }

    [Fact]
    public async Task Messages_are_encrypted_in_the_database()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");
        var bob = await harness.CreateUserAsync("bob", "bob_dev");

        await MakeFriendsAsync(harness, alice, bob);

        var chatService = await harness.ChatServiceAsync();
        var chat = await chatService.StartDirectAsync(alice.Id, bob.Id, CancellationToken.None);

        await chatService.SendMessageAsync(chat.Id, alice.Id, Guid.CreateVersion7(), "top secret plaintext", CancellationToken.None);

        var stored = await harness.WithDbAsync(db => db.Messages.SingleAsync(x => x.Kind == MessageKind.User));
        var storedBytes = System.Text.Encoding.UTF8.GetString(stored.CipherText);

        Assert.DoesNotContain("top secret", storedBytes, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(stored.Nonce);
        Assert.NotEmpty(stored.Tag);
    }

    [Fact]
    public async Task Duplicate_client_message_id_is_idempotent()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");
        var bob = await harness.CreateUserAsync("bob", "bob_dev");

        await MakeFriendsAsync(harness, alice, bob);

        var chatService = await harness.ChatServiceAsync();
        var chat = await chatService.StartDirectAsync(alice.Id, bob.Id, CancellationToken.None);
        var clientId = Guid.CreateVersion7();

        var first = await chatService.SendMessageAsync(chat.Id, alice.Id, clientId, "hello", CancellationToken.None);
        var second = await chatService.SendMessageAsync(chat.Id, alice.Id, clientId, "hello", CancellationToken.None);

        Assert.Equal(first.Message.Id, second.Message.Id);
        Assert.Equal(1, await harness.WithDbAsync(db => db.Messages.CountAsync(x => x.Kind == MessageKind.User)));
    }

    [Fact]
    public async Task User_cannot_request_friendship_with_self()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");

        var friends = await harness.FriendServiceAsync();

        var exception = await Assert.ThrowsAsync<AppException>(() =>
            friends.RequestAsync(alice.Id, "alice_dev", CancellationToken.None));

        Assert.Equal(ErrorCodes.SelfAction, exception.Code);
    }

    [Fact]
    public async Task Reverse_request_accepts_the_existing_invitation()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");
        var bob = await harness.CreateUserAsync("bob", "bob_dev");

        var friends = await harness.FriendServiceAsync();

        await friends.RequestAsync(alice.Id, "bob_dev", CancellationToken.None);
        var mutual = await friends.RequestAsync(bob.Id, "alice_dev", CancellationToken.None);

        Assert.Equal(nameof(FriendshipStatus.Accepted), mutual.Status);
        Assert.Equal(1, await harness.WithDbAsync(db => db.Friendships.CountAsync()));
    }

    [Fact]
    public async Task Handle_is_unique_case_insensitively()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice");
        var bob = await harness.CreateUserAsync("bob");

        await using var scope = harness.Provider.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<YuChat.Features.Users.UserService>();

        await users.SetHandleAsync(alice.Id, "Alex_Dev", CancellationToken.None);

        var exception = await Assert.ThrowsAsync<AppException>(() =>
            users.SetHandleAsync(bob.Id, "alex_dev", CancellationToken.None));

        Assert.Equal(ErrorCodes.HandleTaken, exception.Code);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("has space")]
    [InlineData("emoji😀")]
    [InlineData("")]
    public async Task Invalid_handles_are_rejected(string handle)
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice");

        await using var scope = harness.Provider.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<YuChat.Features.Users.UserService>();

        await Assert.ThrowsAsync<AppException>(() => users.SetHandleAsync(alice.Id, handle, CancellationToken.None));
    }

    [Fact]
    public async Task Unread_counter_excludes_own_messages_and_resets_after_read()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");
        var bob = await harness.CreateUserAsync("bob", "bob_dev");

        await MakeFriendsAsync(harness, alice, bob);

        var chatService = await harness.ChatServiceAsync();
        var chat = await chatService.StartDirectAsync(alice.Id, bob.Id, CancellationToken.None);

        await chatService.SendMessageAsync(chat.Id, alice.Id, Guid.CreateVersion7(), "one", CancellationToken.None);
        await chatService.SendMessageAsync(chat.Id, alice.Id, Guid.CreateVersion7(), "two", CancellationToken.None);

        // The pinned system feed is part of the list, so select the direct room explicitly.
        var chats = await chatService.GetChatsAsync(bob.Id, CancellationToken.None);
        Assert.Equal(2, chats.Single(x => x.Id == chat.Id).UnreadCount);

        var last = await harness.WithDbAsync(db => db.Messages
            .Where(x => x.Kind == MessageKind.User)
            .OrderByDescending(x => x.SentAt)
            .FirstAsync());
        await chatService.MarkReadAsync(chat.Id, bob.Id, last.Id, CancellationToken.None);

        var afterRead = await chatService.GetChatsAsync(bob.Id, CancellationToken.None);
        Assert.Equal(0, afterRead.Single(x => x.Id == chat.Id).UnreadCount);
    }

    [Fact]
    public async Task History_pagination_walks_backwards_without_duplicates()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");
        var bob = await harness.CreateUserAsync("bob", "bob_dev");

        await MakeFriendsAsync(harness, alice, bob);

        var chatService = await harness.ChatServiceAsync();
        var chat = await chatService.StartDirectAsync(alice.Id, bob.Id, CancellationToken.None);

        for (var i = 0; i < 7; i++)
        {
            await chatService.SendMessageAsync(chat.Id, alice.Id, Guid.CreateVersion7(), $"message {i}", CancellationToken.None);
        }

        var first = await chatService.GetMessagesAsync(chat.Id, bob.Id, null, 3, CancellationToken.None);
        Assert.Equal(3, first.Messages.Count);
        Assert.True(first.HasMore);

        var oldest = first.Messages[0];
        var second = await chatService.GetMessagesAsync(chat.Id, bob.Id, oldest.SentAt, 3, CancellationToken.None);

        Assert.Equal(3, second.Messages.Count);
        Assert.DoesNotContain(second.Messages, x => first.Messages.Any(y => y.Id == x.Id));

        var secondOldest = second.Messages[0];
        var third = await chatService.GetMessagesAsync(chat.Id, bob.Id, secondOldest.SentAt, 3, CancellationToken.None);

        Assert.Single(third.Messages);
        Assert.False(third.HasMore);
    }

    private static async Task MakeFriendsAsync(TestHarness harness, User a, User b)
    {
        var friends = await harness.FriendServiceAsync();
        await friends.RequestAsync(a.Id, b.UserHandle!, CancellationToken.None);
        await RespondAsync(harness, a, b);
    }

    private static async Task RespondAsync(TestHarness harness, User requester, User addressee)
    {
        var id = await harness.WithDbAsync(db => db.Friendships
            .Where(x => x.RequesterId == requester.Id && x.AddresseeId == addressee.Id)
            .Select(x => x.Id)
            .SingleAsync());

        var friends = await harness.FriendServiceAsync();
        await friends.RespondAsync(addressee.Id, id, "accept", CancellationToken.None);
    }
}
