using YuChat.Infrastructure.Security;

namespace YuChat.Features.Notifications;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").WithTags("Notifications").RequireAuthorization();

        group.MapGet("/", async (
            int? take,
            NotificationService notifications,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await notifications.GetAsync(currentUser.RequireUserId(), take ?? 50, cancellationToken)))
            .WithName("GetNotifications");

        group.MapPost("/read-all", async (
            NotificationService notifications,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            await notifications.MarkAllReadAsync(currentUser.RequireUserId(), cancellationToken);
            return Results.NoContent();
        })
        .WithName("MarkAllNotificationsRead");

        return app;
    }
}
