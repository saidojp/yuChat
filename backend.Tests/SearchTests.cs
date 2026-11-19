using Microsoft.EntityFrameworkCore;
using YuChat.Domain;
using YuChat.Features;
using YuChat.Features.Search;

namespace YuChat.Api.Tests;

public sealed class SearchTests
{
    [Fact]
    public async Task Short_queries_return_nothing()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");
        await harness.CreateUserAsync("bob", "bob_dev");

        var search = await harness.SearchServiceAsync();

        Assert.Empty((await search.SearchAsync(alice.Id, "b", CancellationToken.None)).Users);
        Assert.Empty((await search.SearchAsync(alice.Id, "  ", CancellationToken.None)).Users);
    }

    [Fact]
    public async Task User_search_matches_handle_and_excludes_self()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");
        await harness.CreateUserAsync("bob", "bob_dev");
        await harness.CreateUserAsync("carol", "carol_dev");

        var search = await harness.SearchServiceAsync();
        var result = await search.SearchAsync(alice.Id, "dev", CancellationToken.None);

        Assert.Equal(2, result.Users.Count);
        Assert.DoesNotContain(result.Users, x => x.Id == alice.Id);
    }

    [Fact]
    public async Task Prefix_matches_rank_before_contains()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");
        await harness.CreateUserAsync("bob", "bob_dev");
        await harness.CreateUserAsync("x", "xx_bob_xx");

        var search = await harness.SearchServiceAsync();
        var result = await search.SearchAsync(alice.Id, "bob", CancellationToken.None);

        Assert.Equal("bob_dev", result.Users[0].UserHandle);
    }

    [Fact]
    public async Task Direct_chat_is_found_by_the_peer_handle()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");
        var bob = await harness.CreateUserAsync("bob", "bob_dev");

        var friends = await harness.FriendServiceAsync();
        await friends.RequestAsync(alice.Id, "bob_dev", CancellationToken.None);
        var friendshipId = await harness.WithDbAsync(db => db.Friendships.Select(x => x.Id).SingleAsync());
        await friends.RespondAsync(bob.Id, friendshipId, "accept", CancellationToken.None);

        var chats = await harness.ChatServiceAsync();
        var chat = await chats.StartDirectAsync(alice.Id, bob.Id, CancellationToken.None);

        var search = await harness.SearchServiceAsync();
        var result = await search.SearchAsync(alice.Id, "bob", CancellationToken.None);

        Assert.Contains(result.Chats, x => x.Id == chat.Id && x.Kind == "Direct");
    }

    [Fact]
    public async Task Group_is_found_by_partial_name_for_non_members()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");
        var carol = await harness.CreateUserAsync("carol", "carol_dev");

        var chats = await harness.ChatServiceAsync();
        var group = await chats.CreateGroupAsync(alice.Id, new CreateGroupRequest("Design sync", [], []), CancellationToken.None);

        var search = await harness.SearchServiceAsync();
        var result = await search.SearchAsync(carol.Id, "design", CancellationToken.None);

        // Groups are discoverable by name, but the caller is still not a member.
        Assert.Contains(result.Chats, x => x.Id == group.Chat.Id);

        var accessException = await Assert.ThrowsAsync<Infrastructure.Errors.AppException>(() =>
            chats.GetMessagesAsync(group.Chat.Id, carol.Id, null, 30, CancellationToken.None));

        Assert.Equal(404, accessException.StatusCode);
    }

    [Fact]
    public async Task Unmatched_queries_return_empty_results()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");

        var chats = await harness.ChatServiceAsync();
        await chats.CreateGroupAsync(alice.Id, new CreateGroupRequest("Design sync", [], []), CancellationToken.None);

        var search = await harness.SearchServiceAsync();
        var result = await search.SearchAsync(alice.Id, "zzzz", CancellationToken.None);

        Assert.Empty(result.Chats);
        Assert.Empty(result.Users);
    }

    [Fact]
    public async Task Own_groups_are_never_matched_by_an_unrelated_term()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");

        var chats = await harness.ChatServiceAsync();
        await chats.CreateGroupAsync(alice.Id, new CreateGroupRequest("Design sync", [], []), CancellationToken.None);

        var search = await harness.SearchServiceAsync();

        Assert.Empty((await search.SearchAsync(alice.Id, "zzzz", CancellationToken.None)).Chats);
        Assert.Single((await search.SearchAsync(alice.Id, "sync", CancellationToken.None)).Chats);
    }

    [Fact]
    public async Task System_feed_is_never_returned()
    {
        await using var harness = await TestHarness.CreateAsync();
        var alice = await harness.CreateUserAsync("alice", "alice_dev");

        var chats = await harness.ChatServiceAsync();
        await chats.GetChatsAsync(alice.Id, CancellationToken.None);

        var search = await harness.SearchServiceAsync();

        Assert.DoesNotContain(
            (await search.SearchAsync(alice.Id, "notifications", CancellationToken.None)).Chats,
            x => x.Kind == nameof(ChatRoomKind.System));
    }
}
