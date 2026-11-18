using YuChat.Infrastructure.Errors;
using YuChat.Infrastructure.Security;
using YuChat.Features.Chats;

namespace YuChat.Features.Chats;

public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/chats").WithTags("Chats").RequireAuthorization();

        group.MapGet("/", async (ChatService chats, ICurrentUser currentUser, CancellationToken cancellationToken) =>
            Results.Ok(await chats.GetChatsAsync(currentUser.RequireUserId(), cancellationToken)))
            .WithName("GetChats");

        group.MapGet("/{chatId:guid}", async (
            Guid chatId,
            ChatService chats,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await chats.GetChatAsync(chatId, currentUser.RequireUserId(), cancellationToken)))
            .WithName("GetChat");

        group.MapPost("/direct", async (
            DirectChatRequest request,
            ChatService chats,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await chats.StartDirectAsync(currentUser.RequireUserId(), request.TargetUserId, cancellationToken)))
            .WithName("StartDirectChat");

        group.MapPost("/group", async (
            CreateGroupRequest request,
            ChatService chats,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await chats.CreateGroupAsync(currentUser.RequireUserId(), request, cancellationToken)))
            .WithName("CreateGroupChat");

        group.MapGet("/{chatId:guid}/messages", async (
            Guid chatId,
            DateTime? beforeSentAt,
            int? limit,
            ChatService chats,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await chats.GetMessagesAsync(
                chatId,
                currentUser.RequireUserId(),
                beforeSentAt,
                limit ?? 30,
                cancellationToken)))
            .WithName("GetMessages");

        group.MapPost("/{chatId:guid}/read", async (
            Guid chatId,
            ReadRequest request,
            ChatService chats,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await chats.MarkReadAsync(chatId, currentUser.RequireUserId(), request.LastReadMessageId, cancellationToken)))
            .WithName("MarkChatRead");

        group.MapGet("/invitations", async (ChatService chats, ICurrentUser currentUser, CancellationToken cancellationToken) =>
            Results.Ok(await chats.GetInvitationsAsync(currentUser.RequireUserId(), cancellationToken)))
            .WithName("GetChatInvitations");

        group.MapPost("/invitations/{invitationId:guid}/respond", async (
            Guid invitationId,
            InvitationDecisionRequest request,
            ChatService chats,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await chats.RespondToInvitationAsync(
                currentUser.RequireUserId(),
                invitationId,
                request.Action ?? string.Empty,
                cancellationToken)))
            .WithName("RespondToChatInvitation");

        return app;
    }

    public sealed record ReadRequest(Guid LastReadMessageId);
}
