using Microsoft.EntityFrameworkCore;
using YuChat.Data;
using YuChat.Domain;
using YuChat.Infrastructure.Errors;

namespace YuChat.Features.Users;

public sealed class UserService(AppDbContext db, TimeProvider time)
{
    public static string Normalize(string value) => value.Trim().ToLowerInvariant();

    public async Task<UserProfileDto> GetProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken)
            ?? throw AppException.NotFound("User was not found.", ErrorCodes.UserNotFound);

        return ToProfile(user);
    }

    public async Task<UserProfileDto> SetHandleAsync(Guid userId, string rawHandle, CancellationToken cancellationToken)
    {
        var handle = rawHandle.Trim();
        var normalized = Normalize(handle);

        if (!Features.InputRules.HandlePattern.IsMatch(handle))
        {
            throw AppException.BadRequest(
                ErrorCodes.ValidationFailed,
                "User ID must be 4-24 characters long and contain only letters, digits and underscores.");
        }

        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken)
            ?? throw AppException.NotFound("User was not found.", ErrorCodes.UserNotFound);

        if (user.UserHandleNormalized == normalized)
        {
            return ToProfile(user);
        }

        var taken = await db.Users
            .AsNoTracking()
            .AnyAsync(x => x.UserHandleNormalized == normalized && x.Id != userId, cancellationToken);

        if (taken)
        {
            throw AppException.Conflict(ErrorCodes.HandleTaken, $"User ID @{handle} is already taken.");
        }

        user.UserHandle = handle;
        user.UserHandleNormalized = normalized;
        user.HandleSetAt = time.GetUtcNow().UtcDateTime;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DbErrors.IsUniqueViolation(exception))
        {
            throw AppException.Conflict(ErrorCodes.HandleTaken, $"User ID @{handle} is already taken.");
        }

        return ToProfile(user);
    }

    public async Task<IReadOnlyList<UserSearchResultDto>> SearchAsync(Guid currentUserId, string query, CancellationToken cancellationToken)
    {
        var term = Normalize(query);

        if (term.Length < 2)
        {
            return [];
        }

        var users = await db.Users
            .AsNoTracking()
            .Where(x => x.Id != currentUserId
                        && x.UserHandleNormalized != null
                        && EF.Functions.Like(x.UserHandleNormalized, $"%{term}%"))
            .OrderBy(x => x.UserHandleNormalized!.StartsWith(term) ? 0 : 1)
            .ThenBy(x => x.UserHandleNormalized)
            .Take(20)
            .Select(x => new { x.Id, Handle = x.UserHandle! })
            .ToListAsync(cancellationToken);

        if (users.Count == 0)
        {
            return [];
        }

        var ids = users.Select(x => x.Id).ToArray();
        var friendships = await db.Friendships
            .AsNoTracking()
            .Where(x => ids.Contains(x.RequesterId) || ids.Contains(x.AddresseeId))
            .Where(x => x.RequesterId == currentUserId || x.AddresseeId == currentUserId)
            .ToListAsync(cancellationToken);

        var results = users
            .Select(user =>
            {
                var friendship = friendships.FirstOrDefault(x =>
                    (x.RequesterId == currentUserId && x.AddresseeId == user.Id) ||
                    (x.AddresseeId == currentUserId && x.RequesterId == user.Id));

                var state = friendship?.Status switch
                {
                    null => FriendshipState.None,
                    FriendshipStatus.Accepted => FriendshipState.Friends,
                    FriendshipStatus.Pending when friendship.RequesterId == currentUserId => FriendshipState.PendingSent,
                    FriendshipStatus.Pending => FriendshipState.PendingReceived,
                    _ => FriendshipState.None,
                };

                return new UserSearchResultDto(user.Id, user.Handle, state, friendship?.Id);
            })
            .ToList();

        return results;
    }

    internal static UserProfileDto ToProfile(User user) =>
        new(user.Id, user.Login, user.UserHandle, user.CreatedAt, user.UserHandle is null);
}

/// <summary>Detects unique-constraint violations independent of the database provider.</summary>
internal static class DbErrors
{
    public static bool IsUniqueViolation(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message ?? exception.Message;

        return message.Contains("23505", StringComparison.Ordinal)
               || message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
               || message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase);
    }
}
