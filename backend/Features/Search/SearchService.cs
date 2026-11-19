using Microsoft.EntityFrameworkCore;
using YuChat.Data;
using YuChat.Domain;
using YuChat.Features.Chats;
using YuChat.Features.Users;

namespace YuChat.Features.Search;

/// <summary>
/// Global search behind the sidebar search bar: people by user handle, conversations by title
/// (group name) or by the other participant's handle.
/// </summary>
public sealed class SearchService(AppDbContext db, UserService users)
{
    private const int ChatLimit = 20;
    private const int UserLimit = 20;

    public async Task<SearchResponseDto> SearchAsync(Guid currentUserId, string query, CancellationToken cancellationToken)
    {
        var term = UserService.Normalize(query);

        if (term.Length < 2)
        {
            return new SearchResponseDto([], []);
        }

        var people = await users.SearchAsync(currentUserId, query, cancellationToken);
        var chats = await SearchChatsAsync(currentUserId, term, cancellationToken);

        return new SearchResponseDto(chats, people.Take(UserLimit).ToList());
    }

    private async Task<IReadOnlyList<ChatSearchResultDto>> SearchChatsAsync(
        Guid currentUserId,
        string term,
        CancellationToken cancellationToken)
    {
        var myRoomIds = await db.ChatMembers
            .AsNoTracking()
            .Where(x => x.UserId == currentUserId)
            .Select(x => x.ChatRoomId)
            .ToListAsync(cancellationToken);

        var myRoomIdSet = myRoomIds.ToHashSet();

        // Groups match by name for every caller, so a group can be discovered without being a
        // member. Direct chats are private: they only match inside the caller's own rooms, and
        // then only through the peer's handle (resolved below from participants).
        var groupIds = await db.ChatRooms
            .AsNoTracking()
            .Where(x => x.Kind == ChatRoomKind.Group && x.Name != null && x.Name.ToLower().Contains(term))
            .OrderBy(x => x.Name!.ToLower().StartsWith(term) ? 0 : 1)
            .ThenBy(x => x.Name)
            .Take(ChatLimit)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var candidateIds = groupIds.Concat(myRoomIds).Distinct().ToArray();

        if (candidateIds.Length == 0)
        {
            return [];
        }

        var rooms = await db.ChatRooms
            .AsNoTracking()
            .Where(x => candidateIds.Contains(x.Id) && x.Kind != ChatRoomKind.System)
            .Select(x => new { x.Id, x.Kind, x.Name })
            .ToListAsync(cancellationToken);

        var participants = await LoadParticipantsAsync(candidateIds, cancellationToken);

        var results = new List<ChatSearchResultDto>();

        foreach (var room in rooms)
        {
            var people = participants.GetValueOrDefault(room.Id) ?? [];

            string title;

            if (room.Kind == ChatRoomKind.Group)
            {
                title = room.Name ?? "Group";
            }
            else
            {
                title = people.FirstOrDefault(x => x.UserId != currentUserId)?.UserHandle ?? string.Empty;
            }

            // One rule for both kinds: the visible title has to contain the term. Direct chats
            // therefore only match through the peer's handle, groups through their name.
            if (!title.ToLowerInvariant().Contains(term))
            {
                continue;
            }

            results.Add(new ChatSearchResultDto(
                room.Id,
                room.Kind.ToString(),
                room.Kind == ChatRoomKind.Group,
                title,
                people,
                0));
        }

        return results
            .OrderByDescending(x => myRoomIdSet.Contains(x.Id))
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .Take(ChatLimit)
            .ToList();
    }

    private async Task<Dictionary<Guid, List<ChatParticipantDto>>> LoadParticipantsAsync(
        Guid[] roomIds,
        CancellationToken cancellationToken)
    {
        var rows = await db.ChatMembers
            .AsNoTracking()
            .Where(x => roomIds.Contains(x.ChatRoomId))
            .Select(x => new { x.ChatRoomId, x.UserId, Handle = x.User.UserHandle ?? x.User.Login })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => x.ChatRoomId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => new ChatParticipantDto(x.UserId, x.Handle))
                    .OrderBy(x => x.UserHandle, StringComparer.OrdinalIgnoreCase)
                    .ToList());
    }
}
