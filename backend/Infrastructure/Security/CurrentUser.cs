using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace YuChat.Infrastructure.Security;

public interface ICurrentUser
{
    Guid? UserId { get; }

    Guid RequireUserId();
}

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId
    {
        get
        {
            var value = accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? accessor.HttpContext?.User.FindFirstValue("sub");

            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public Guid RequireUserId() =>
        UserId ?? throw Errors.AppException.Unauthorized(Errors.ErrorCodes.Unauthorized, "Authentication is required.");
}

/// <summary>Maps SignalR connections to a stable user id taken from the validated principal only.</summary>
public sealed class UserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirst("sub")?.Value ?? connection.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
}
