using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using YuChat.Domain;
using YuChat.Features;
using YuChat.Features.Chats;
using YuChat.Features.Friends;
using YuChat.Features.Notifications;
using YuChat.Infrastructure.Errors;

namespace YuChat.Api.Tests;

public sealed class SystemFeedTests
{
    [Fact]
    public async Task Every_user_gets_exactly_one_pinned_system_room()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");

        var chats = await harness.ChatServiceAsync();
        await chats.GetChatsAsync(alice.Id, CancellationToken.None);
        var second = await chats.GetChatsAsync(alice.Id, CancellationToken.None);

        var systemRooms = await harness.WithDbAsync(db => db.ChatRooms.CountAsync(x => x.SystemOwnerId == alice.Id));

        Assert.Equal(1, systemRooms);
        Assert.Single(second);
        Assert.Equal("System", second[0].Kind);
        Assert.True(second[0].IsGroup);
    }

    [Fact]
    public async Task System_room_stays_pinned_on_top_of_the_chat_list()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");
        var bob = await harness.CreateUserAsync("bob", "bob_dev");

        await MakeFriendsAsync(harness, alice, bob);

        var chats = await harness.ChatServiceAsync();
        var direct = await chats.StartDirectAsync(alice.Id, bob.Id, CancellationToken.None);

        // A fresh message makes the direct chat the most recent activity; the feed must still lead.
        await chats.SendMessageAsync(direct.Id, bob.Id, Guid.CreateVersion7(), "newest", CancellationToken.None);

        var list = await chats.GetChatsAsync(alice.Id, CancellationToken.None);

        Assert.Equal("System", list[0].Kind);
        Assert.Equal(direct.Id, list[1].Id);
    }

    [Fact]
    public async Task Friend_request_lands_in_the_addressee_feed()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");
        var bob = await harness.CreateUserAsync("bob", "bob_dev");

        var friends = await harness.FriendServiceAsync();
        await friends.RequestAsync(alice.Id, "bob_dev", CancellationToken.None);

        var feed = await harness.WithDbAsync(db => db.Messages
            .Where(x => x.Kind == MessageKind.System)
            .OrderByDescending(x => x.SentAt)
            .FirstAsync());

        Assert.Equal(SystemEvent.FriendRequestReceived, feed.SystemEvent);
        Assert.Equal(alice.Id, feed.ActorId);
        Assert.Null(feed.SenderId);
        Assert.Empty(feed.CipherText);

        var rooms = await harness.WithDbAsync(db => db.ChatRooms.SingleAsync(x => x.SystemOwnerId == bob.Id));
        Assert.Equal(rooms.Id, feed.ChatRoomId);
    }

    [Fact]
    public async Task Accepting_a_request_writes_an_event_for_the_requester()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");
        var bob = await harness.CreateUserAsync("bob", "bob_dev");

        var friends = await harness.FriendServiceAsync();
        await friends.RequestAsync(alice.Id, "bob_dev", CancellationToken.None);

        var friendshipId = await harness.WithDbAsync(db => db.Friendships.Select(x => x.Id).SingleAsync());
        await friends.RespondAsync(bob.Id, friendshipId, "accept", CancellationToken.None);

        var aliceFeed = await FeedEventsAsync(harness, alice.Id);
        var bobFeed = await FeedEventsAsync(harness, bob.Id);

        Assert.Contains(SystemEvent.FriendRequestReceived, bobFeed);
        Assert.Contains(SystemEvent.FriendRequestAccepted, aliceFeed);
    }

    [Fact]
    public async Task Removing_a_friend_notifies_the_other_side()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");
        var bob = await harness.CreateUserAsync("bob", "bob_dev");

        await MakeFriendsAsync(harness, alice, bob);

        var friendshipId = await harness.WithDbAsync(db => db.Friendships.Select(x => x.Id).SingleAsync());
        var friends = await harness.FriendServiceAsync();

        await friends.RemoveAsync(alice.Id, friendshipId, CancellationToken.None);

        Assert.Empty(await harness.WithDbAsync(db => db.Friendships.ToListAsync()));

        var bobFeed = await FeedEventsAsync(harness, bob.Id);
        Assert.Contains(SystemEvent.FriendRemoved, bobFeed);
    }

    [Fact]
    public async Task Removing_someone_elses_friendship_is_not_possible()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");
        var bob = await harness.CreateUserAsync("bob", "bob_dev");
        var eve = await harness.CreateUserAsync("eve", "eve_dev");

        await MakeFriendsAsync(harness, alice, bob);

        var friendshipId = await harness.WithDbAsync(db => db.Friendships.Select(x => x.Id).SingleAsync());
        var friends = await harness.FriendServiceAsync();

        var exception = await Assert.ThrowsAsync<AppException>(() =>
            friends.RemoveAsync(eve.Id, friendshipId, CancellationToken.None));

        Assert.Equal(404, exception.StatusCode);
        Assert.Equal(1, await harness.WithDbAsync(db => db.Friendships.CountAsync()));
    }

    [Fact]
    public async Task After_removal_existing_chat_survives_but_no_new_direct_chat_is_allowed()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");
        var bob = await harness.CreateUserAsync("bob", "bob_dev");

        await MakeFriendsAsync(harness, alice, bob);

        var chats = await harness.ChatServiceAsync();
        var chat = await chats.StartDirectAsync(alice.Id, bob.Id, CancellationToken.None);
        await chats.SendMessageAsync(chat.Id, alice.Id, Guid.CreateVersion7(), "history stays", CancellationToken.None);

        var friendshipId = await harness.WithDbAsync(db => db.Friendships.Select(x => x.Id).SingleAsync());
        var friends = await harness.FriendServiceAsync();
        await friends.RemoveAsync(alice.Id, friendshipId, CancellationToken.None);

        // History is preserved and still readable by both sides.
        var page = await chats.GetMessagesAsync(chat.Id, bob.Id, null, 30, CancellationToken.None);
        Assert.Single(page.Messages);

        var exception = await Assert.ThrowsAsync<AppException>(() =>
            chats.StartDirectAsync(bob.Id, alice.Id, CancellationToken.None));
        Assert.Equal(ErrorCodes.NotFriends, exception.Code);
    }

    [Fact]
    public async Task Cannot_write_into_the_system_feed()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");

        var chats = await harness.ChatServiceAsync();
        var list = await chats.GetChatsAsync(alice.Id, CancellationToken.None);
        var systemChat = list.Single(x => x.Kind == "System");

        var exception = await Assert.ThrowsAsync<AppException>(() =>
            chats.SendMessageAsync(systemChat.Id, alice.Id, Guid.CreateVersion7(), "hi", CancellationToken.None));

        Assert.Equal(ErrorCodes.Unsupported, exception.Code);
    }

    [Fact]
    public async Task Group_invite_and_acceptance_are_recorded()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");
        var carol = await harness.CreateUserAsync("carol", "carol_dev");

        var chats = await harness.ChatServiceAsync();
        var group = await chats.CreateGroupAsync(
            alice.Id,
            new CreateGroupRequest("Squad", [], [new AdditionalGroupUser("carol_dev", false)]),
            CancellationToken.None);

        var carolFeed = await FeedEventsAsync(harness, carol.Id);
        Assert.Contains(SystemEvent.ChatInvitationReceived, carolFeed);

        var invitationId = await harness.WithDbAsync(db => db.ChatInvitations.Select(x => x.Id).SingleAsync());
        await chats.RespondToInvitationAsync(carol.Id, invitationId, "accept", CancellationToken.None);

        var aliceFeed = await FeedEventsAsync(harness, alice.Id);
        Assert.Contains(SystemEvent.ChatInvitationAccepted, aliceFeed);
        Assert.NotNull(group.Chat);
    }

    [Fact]
    public async Task Unread_badge_counts_system_events()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");
        var bob = await harness.CreateUserAsync("bob", "bob_dev");

        var friends = await harness.FriendServiceAsync();
        await friends.RequestAsync(alice.Id, "bob_dev", CancellationToken.None);

        var chats = await harness.ChatServiceAsync();
        var list = await chats.GetChatsAsync(bob.Id, CancellationToken.None);
        var systemChat = list.Single(x => x.Kind == "System");

        Assert.Equal(1, systemChat.UnreadCount);
        Assert.NotNull(systemChat.LastSystemEvent);
        Assert.Equal(nameof(SystemEvent.FriendRequestReceived), systemChat.LastSystemEvent!.Event);
        Assert.Equal("alice_dev", systemChat.LastSystemEvent.ActorHandle);
    }

    [Fact]
    public async Task Marking_the_feed_read_clears_the_badge()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");
        var bob = await harness.CreateUserAsync("bob", "bob_dev");

        var friends = await harness.FriendServiceAsync();
        await friends.RequestAsync(alice.Id, "bob_dev", CancellationToken.None);

        var chats = await harness.ChatServiceAsync();
        var systemChat = (await chats.GetChatsAsync(bob.Id, CancellationToken.None)).Single(x => x.Kind == "System");

        var page = await chats.GetMessagesAsync(systemChat.Id, bob.Id, null, 30, CancellationToken.None);
        await chats.MarkReadAsync(systemChat.Id, bob.Id, page.Messages[^1].Id, CancellationToken.None);

        var after = (await chats.GetChatsAsync(bob.Id, CancellationToken.None)).Single(x => x.Kind == "System");
        Assert.Equal(0, after.UnreadCount);
    }

    [Fact]
    public async Task System_messages_carry_no_plaintext_and_no_ciphertext()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");
        var bob = await harness.CreateUserAsync("bob", "bob_dev");

        var friends = await harness.FriendServiceAsync();
        await friends.RequestAsync(alice.Id, "bob_dev", CancellationToken.None);

        var stored = await harness.WithDbAsync(db => db.Messages.SingleAsync(x => x.Kind == MessageKind.System));

        Assert.Empty(stored.CipherText);
        Assert.Empty(stored.Nonce);
        Assert.Empty(stored.Tag);
        Assert.Null(stored.SenderId);
    }

    private static async Task<List<SystemEvent>> FeedEventsAsync(TestHarness harness, Guid userId) =>
        await harness.WithDbAsync(async db =>
        {
            var roomId = await db.ChatRooms
                .Where(x => x.SystemOwnerId == userId)
                .Select(x => x.Id)
                .SingleAsync();

            return await db.Messages
                .Where(x => x.ChatRoomId == roomId && x.SystemEvent != null)
                .Select(x => x.SystemEvent!.Value)
                .ToListAsync();
        });

    private static async Task MakeFriendsAsync(TestHarness harness, User a, User b)
    {
        var friends = await harness.FriendServiceAsync();
        await friends.RequestAsync(a.Id, b.UserHandle!, CancellationToken.None);

        var friendshipId = await harness.WithDbAsync(db => db.Friendships
            .Where(x => x.RequesterId == a.Id && x.AddresseeId == b.Id)
            .Select(x => x.Id)
            .SingleAsync());

        await friends.RespondAsync(b.Id, friendshipId, "accept", CancellationToken.None);
    }
}
