using Microsoft.EntityFrameworkCore;
using YuChat.Data;
using YuChat.Domain;
using YuChat.Features.Notifications;
using YuChat.Features.Users;
using YuChat.Infrastructure.Encryption;
using YuChat.Infrastructure.Errors;
using YuChat.Infrastructure.Security;
using YuChat.Realtime;

namespace YuChat.Features.Chats;

public sealed record OutgoingMessage(MessageDto Message, IReadOnlyList<Guid> MemberIds);

public sealed class ChatService(
    AppDbContext db,
    ChatAccessService access,
    IMessageEncryptionService encryption,
    SystemFeedService feeds,
    RealtimeNotifier realtime,
    TimeProvider time)
{
    private DateTime Now => time.GetUtcNow().UtcDateTime;

    // ---------------------------------------------------------------- list & details

    public async Task<IReadOnlyList<ChatSummaryDto>> GetChatsAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Make sure the pinned notifications feed exists before listing, so it is always present
        // even for accounts created before system rooms were introduced.
        await feeds.EnsureSystemRoomAsync(userId, cancellationToken);

        var memberships = await db.ChatMembers
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new
            {
                x.ChatRoomId,
                x.JoinedAt,
                x.LastReadAt,
                x.ChatRoom.Kind,
                Name = x.ChatRoom.Name,
                SystemOwnerId = x.ChatRoom.SystemOwnerId,
                x.ChatRoom.LastMessageAt,
                x.ChatRoom.LastMessageId,
            })
            .ToListAsync(cancellationToken);

        if (memberships.Count == 0)
        {
            return [];
        }

        var roomIds = memberships.Select(x => x.ChatRoomId).ToArray();
        var participants = await LoadParticipantsAsync(roomIds, cancellationToken);

        // System events carry no sender, so "not written by me" has to accept NULL as well;
        // a plain `SenderId != userId` comparison would silently drop them from the count.
        var unreadByRoom = await db.Messages
            .AsNoTracking()
            .Where(m => roomIds.Contains(m.ChatRoomId) && (m.SenderId == null || m.SenderId != userId))
            .Join(
                db.ChatMembers.AsNoTracking().Where(cm => cm.UserId == userId),
                m => m.ChatRoomId,
                cm => cm.ChatRoomId,
                (m, cm) => new { m.ChatRoomId, m.SentAt, m.SenderId, cm.LastReadAt, cm.JoinedAt })
            .Where(x => (x.SenderId == null || x.SenderId != userId) && x.SentAt > x.LastReadAt && x.SentAt >= x.JoinedAt)
            .GroupBy(x => x.ChatRoomId)
            .Select(g => new { ChatRoomId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var unreadCounts = unreadByRoom.ToDictionary(x => x.ChatRoomId, x => x.Count);

        var lastMessageIds = memberships
            .Where(x => x.LastMessageId is not null)
            .Select(x => x.LastMessageId!.Value)
            .ToArray();

        var lastMessages = lastMessageIds.Length == 0
            ? []
            : await db.Messages
                .AsNoTracking()
                .Where(x => lastMessageIds.Contains(x.Id))
                .Select(x => new PreviewRow(
                    x.Id,
                    x.ChatRoomId,
                    x.SenderId,
                    x.Kind,
                    x.SystemEvent,
                    x.ReferenceId,
                    x.ReferenceName,
                    x.ActorId,
                    x.CipherText,
                    x.Nonce,
                    x.Tag,
                    x.KeyVersion,
                    x.SentAt))
                .ToListAsync(cancellationToken);

        var previewById = lastMessages.ToDictionary(x => x.Id);
        var actorHandles = await feeds.ActorHandlesAsync(lastMessages.Select(x => x.ActorId), cancellationToken);

        var result = new List<ChatSummaryDto>(memberships.Count);

        foreach (var membership in memberships)
        {
            var people = participants.GetValueOrDefault(membership.ChatRoomId) ?? [];
            var isSystem = membership.Kind == ChatRoomKind.System;
            var title = ResolveTitle(membership.Kind, membership.Name, people, userId);

            previewById.TryGetValue(membership.LastMessageId ?? Guid.Empty, out var preview);

            var visible = preview is not null && preview.SentAt >= membership.JoinedAt;

            result.Add(new ChatSummaryDto(
                membership.ChatRoomId,
                membership.Kind.ToString(),
                isSystem,
                title,
                people,
                visible && preview!.Kind == MessageKind.User ? TryDecrypt(preview) : null,
                visible ? preview!.SentAt : membership.LastMessageAt,
                visible ? preview!.SenderId : null,
                unreadCounts.GetValueOrDefault(membership.ChatRoomId),
                visible ? BuildSystemPreview(preview!, actorHandles) : null));
        }

        // The system feed behaves like a pinned conversation: it always sits on top.
        return result
            .OrderByDescending(x => x.Kind == nameof(ChatRoomKind.System))
            .ThenByDescending(x => x.LastMessageAt)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<ChatDetailsDto> GetChatAsync(Guid chatRoomId, Guid userId, CancellationToken cancellationToken)
    {
        var membership = await access.RequireMemberAsync(chatRoomId, userId, cancellationToken);
        var people = await LoadParticipantsAsync([chatRoomId], cancellationToken).ContinueWith(
            task => task.Result.GetValueOrDefault(chatRoomId) ?? [],
            cancellationToken,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return new ChatDetailsDto(
            membership.Room.Id,
            membership.Room.Kind.ToString(),
            membership.Room.IsGroup,
            ResolveTitle(membership.Room.Kind, membership.Room.Name, people, userId),
            membership.Room.CreatedById,
            people,
            membership.Member.JoinedAt);
    }

    public async Task<ChatSummaryDto> BuildSummaryAsync(Guid chatRoomId, Guid userId, CancellationToken cancellationToken)
    {
        var membership = await access.RequireMemberAsync(chatRoomId, userId, cancellationToken);
        var participants = await LoadParticipantsAsync([chatRoomId], cancellationToken);
        var people = participants.GetValueOrDefault(chatRoomId) ?? [];

        var joinedAt = membership.Member.JoinedAt;
        var lastReadAt = membership.Member.LastReadAt;

        var unread = await db.Messages
            .AsNoTracking()
            .CountAsync(
                x => x.ChatRoomId == chatRoomId
                     && (x.SenderId == null || x.SenderId != userId)
                     && x.SentAt > lastReadAt
                     && x.SentAt >= joinedAt,
                cancellationToken);

        return new ChatSummaryDto(
            membership.Room.Id,
            membership.Room.Kind.ToString(),
            membership.Room.IsGroup,
            ResolveTitle(membership.Room.Kind, membership.Room.Name, people, userId),
            people,
            null,
            membership.Room.LastMessageAt,
            null,
            unread,
            null);
    }

    // ---------------------------------------------------------------- direct chats

    public async Task<ChatSummaryDto> StartDirectAsync(Guid userId, Guid targetUserId, CancellationToken cancellationToken)
    {
        if (userId == targetUserId)
        {
            throw AppException.BadRequest(ErrorCodes.SelfAction, "You cannot start a chat with yourself.");
        }

        if (!await access.AreFriendsAsync(userId, targetUserId, cancellationToken))
        {
            throw AppException.BadRequest(ErrorCodes.NotFriends, "You can only start a direct chat with a friend.");
        }

        var directKey = ChatRoom.BuildDirectKey(userId, targetUserId);

        var existingId = await db.ChatRooms
            .AsNoTracking()
            .Where(x => x.DirectKey == directKey)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (existingId is not null)
        {
            return await BuildSummaryAsync(existingId.Value, userId, cancellationToken);
        }

        var now = Now;

        var room = new ChatRoom
        {
            Id = Guid.CreateVersion7(),
            Kind = ChatRoomKind.Direct,
            CreatedById = userId,
            DirectKey = directKey,
            CreatedAt = now,
            LastMessageAt = now,
        };

        db.ChatRooms.Add(room);
        db.ChatMembers.AddRange(
            new ChatMember { ChatRoomId = room.Id, UserId = userId, JoinedAt = now, LastReadAt = now },
            new ChatMember { ChatRoomId = room.Id, UserId = targetUserId, JoinedAt = now, LastReadAt = now });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DbErrors.IsUniqueViolation(exception))
        {
            // Concurrent creation: another request won the race, reuse its room.
            var racedId = await db.ChatRooms.AsNoTracking()
                .Where(x => x.DirectKey == directKey)
                .Select(x => x.Id)
                .SingleAsync(cancellationToken);

            return await BuildSummaryAsync(racedId, userId, cancellationToken);
        }

        await realtime.ToUserAsync(
            targetUserId,
            RealtimeEvents.ChatCreated,
            await BuildSummaryAsync(room.Id, targetUserId, cancellationToken),
            cancellationToken);

        return await BuildSummaryAsync(room.Id, userId, cancellationToken);
    }

    // ---------------------------------------------------------------- groups

    public async Task<GroupCreationResultDto> CreateGroupAsync(Guid creatorId, CreateGroupRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;

        if (name.Length is 0 or > InputRules.GroupNameMaxLength)
        {
            throw AppException.BadRequest(
                ErrorCodes.ValidationFailed,
                $"Group name must be 1-{InputRules.GroupNameMaxLength} characters long.");
        }

        var additional = (request.AdditionalUsers ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.UserHandle))
            .GroupBy(x => UserService.Normalize(x.UserHandle))
            .Select(g => g.First())
            .ToList();

        if (additional.Count > InputRules.MaxAdditionalUsers)
        {
            throw AppException.BadRequest(
                ErrorCodes.ValidationFailed,
                $"You can add at most {InputRules.MaxAdditionalUsers} users by ID at once.");
        }

        var friendIds = (request.FriendIds ?? []).Where(id => id != creatorId).Distinct().ToArray();

        var friendIdSet = (await db.Friendships
                .AsNoTracking()
                .Where(x => x.Status == FriendshipStatus.Accepted && (x.RequesterId == creatorId || x.AddresseeId == creatorId))
                .Select(x => x.RequesterId == creatorId ? x.AddresseeId : x.RequesterId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        if (friendIds.Any(id => !friendIdSet.Contains(id)))
        {
            throw AppException.BadRequest(ErrorCodes.NotFriends, "Only friends can be added directly to a group.");
        }

        var now = Now;

        var room = new ChatRoom
        {
            Id = Guid.CreateVersion7(),
            Kind = ChatRoomKind.Group,
            Name = name,
            CreatedById = creatorId,
            CreatedAt = now,
            LastMessageAt = now,
        };

        db.ChatRooms.Add(room);
        db.ChatMembers.Add(new ChatMember { ChatRoomId = room.Id, UserId = creatorId, JoinedAt = now, LastReadAt = now });

        var directMemberIds = new HashSet<Guid> { creatorId };

        foreach (var friendId in friendIds)
        {
            db.ChatMembers.Add(new ChatMember { ChatRoomId = room.Id, UserId = friendId, JoinedAt = now, LastReadAt = now });
            directMemberIds.Add(friendId);
        }

        var invitedHandles = new List<string>();
        var invitations = new List<ChatInvitation>();
        var friendRequests = new List<(Guid RequesterId, Guid AddresseeId)>();

        foreach (var entry in additional)
        {
            var handle = UserService.Normalize(entry.UserHandle);

            var user = await db.Users
                .AsNoTracking()
                .Where(x => x.UserHandleNormalized == handle)
                .Select(x => new { x.Id, x.UserHandle })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw AppException.BadRequest(
                    ErrorCodes.UserNotFound,
                    $"No user with ID @{entry.UserHandle.Trim()} exists.");

            if (user.Id == creatorId || directMemberIds.Contains(user.Id))
            {
                continue;
            }

            if (friendIdSet.Contains(user.Id))
            {
                db.ChatMembers.Add(new ChatMember { ChatRoomId = room.Id, UserId = user.Id, JoinedAt = now, LastReadAt = now });
                directMemberIds.Add(user.Id);
                continue;
            }

            var invitation = new ChatInvitation
            {
                Id = Guid.CreateVersion7(),
                ChatRoomId = room.Id,
                InviterId = creatorId,
                InviteeId = user.Id,
                Status = InvitationStatus.Pending,
                CreatedAt = now,
            };

            db.ChatInvitations.Add(invitation);
            invitations.Add(invitation);
            invitedHandles.Add(user.UserHandle!);

            if (entry.AlsoSendFriendRequest)
            {
                friendRequests.Add((creatorId, user.Id));
            }
        }

        if (directMemberIds.Count > InputRules.MaxGroupSize)
        {
            throw AppException.BadRequest(
                ErrorCodes.ValidationFailed,
                $"A group can have at most {InputRules.MaxGroupSize} members.");
        }

        foreach (var (requesterId, addresseeId) in friendRequests)
        {
            await AddFriendRequestIfPossibleAsync(requesterId, addresseeId, now, cancellationToken);
        }

        // Members added directly get an informational entry; invited users get an actionable card.
        var feedEvents = new List<(Guid RecipientId, Message Event)>();

        foreach (var memberId in directMemberIds.Except([creatorId]))
        {
            feedEvents.Add((memberId, await feeds.AppendAsync(
                memberId, SystemEvent.AddedToGroup, room.Id, creatorId, name, cancellationToken)));
        }

        foreach (var invitation in invitations)
        {
            feedEvents.Add((invitation.InviteeId, await feeds.AppendAsync(
                invitation.InviteeId, SystemEvent.ChatInvitationReceived, invitation.Id, creatorId, name, cancellationToken)));

            if (friendRequests.Any(x => x.AddresseeId == invitation.InviteeId))
            {
                feedEvents.Add((invitation.InviteeId, await feeds.AppendAsync(
                    invitation.InviteeId, SystemEvent.FriendRequestReceived, invitation.Id, creatorId, null, cancellationToken)));
            }
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DbErrors.IsUniqueViolation(exception))
        {
            throw AppException.Conflict(ErrorCodes.Conflict, "The group changed concurrently. Please retry.");
        }

        var summary = await BuildSummaryAsync(room.Id, creatorId, cancellationToken);
        var creatorHandle = await access.GetHandlesAsync([creatorId], cancellationToken);

        await realtime.ToUsersAsync(directMemberIds.Except([creatorId]), RealtimeEvents.ChatCreated, summary, cancellationToken);

        foreach (var invitation in invitations)
        {
            await realtime.ToUserAsync(
                invitation.InviteeId,
                RealtimeEvents.InvitationReceived,
                new ChatInvitationDto(invitation.Id, room.Id, name, creatorHandle.GetValueOrDefault(creatorId, "unknown"), invitation.CreatedAt),
                cancellationToken);
        }

        foreach (var (recipientId, feedEvent) in feedEvents)
        {
            await feeds.PublishAsync(recipientId, feedEvent, cancellationToken);
        }

        return new GroupCreationResultDto(summary, invitedHandles);
    }

    public async Task<IReadOnlyList<ChatInvitationDto>> GetInvitationsAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.ChatInvitations
            .AsNoTracking()
            .Where(x => x.InviteeId == userId && x.Status == InvitationStatus.Pending)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new ChatInvitationDto(
                x.Id,
                x.ChatRoomId,
                x.ChatRoom.Name ?? "Group",
                x.Inviter.UserHandle ?? x.Inviter.Login,
                x.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<ChatSummaryDto> RespondToInvitationAsync(
        Guid userId,
        Guid invitationId,
        string action,
        CancellationToken cancellationToken)
    {
        var accept = action switch
        {
            "accept" => true,
            "decline" => false,
            _ => throw AppException.BadRequest(ErrorCodes.ValidationFailed, "Action must be 'accept' or 'decline'."),
        };

        var invitation = await db.ChatInvitations
            .SingleOrDefaultAsync(x => x.Id == invitationId && x.InviteeId == userId, cancellationToken)
            ?? throw AppException.NotFound("Invitation was not found.");

        if (invitation.Status != InvitationStatus.Pending)
        {
            throw AppException.Conflict(ErrorCodes.Conflict, "This invitation has already been answered.");
        }

        var now = Now;

        invitation.Status = accept ? InvitationStatus.Accepted : InvitationStatus.Declined;
        invitation.RespondedAt = now;

        Message? feedEvent = null;

        if (accept)
        {
            var alreadyMember = await db.ChatMembers
                .AnyAsync(x => x.ChatRoomId == invitation.ChatRoomId && x.UserId == userId, cancellationToken);

            if (!alreadyMember)
            {
                db.ChatMembers.Add(new ChatMember
                {
                    ChatRoomId = invitation.ChatRoomId,
                    UserId = userId,
                    JoinedAt = now,
                    LastReadAt = now,
                });
            }

            var roomName = await db.ChatRooms
                .AsNoTracking()
                .Where(x => x.Id == invitation.ChatRoomId)
                .Select(x => x.Name)
                .SingleAsync(cancellationToken);

            feedEvent = await feeds.AppendAsync(
                invitation.InviterId,
                SystemEvent.ChatInvitationAccepted,
                invitation.Id,
                userId,
                roomName,
                cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        if (!accept)
        {
            return await BuildSummaryAsync(invitation.ChatRoomId, userId, cancellationToken);
        }

        var summary = await BuildSummaryAsync(invitation.ChatRoomId, userId, cancellationToken);

        await realtime.ToUserAsync(userId, RealtimeEvents.ChatCreated, summary, cancellationToken);

        var memberIds = await GetMemberIdsAsync(invitation.ChatRoomId, cancellationToken);
        await realtime.ToUsersAsync(memberIds.Except([userId]), RealtimeEvents.ChatUpdated, summary, cancellationToken);

        if (feedEvent is not null)
        {
            await feeds.PublishAsync(invitation.InviterId, feedEvent, cancellationToken);
        }

        return summary;
    }

    // ---------------------------------------------------------------- messages

    public async Task<MessagePageDto> GetMessagesAsync(
        Guid chatRoomId,
        Guid userId,
        DateTime? beforeSentAt,
        int limit,
        CancellationToken cancellationToken)
    {
        var membership = await access.RequireMemberAsync(chatRoomId, userId, cancellationToken);

        var pageSize = Math.Clamp(limit, 1, InputRules.MaxMessagePageSize);
        var historyFloor = membership.HistoryFloor;

        var query = db.Messages
            .AsNoTracking()
            .Where(x => x.ChatRoomId == chatRoomId && x.SentAt >= historyFloor);

        if (beforeSentAt is { } cursor)
        {
            // SentAt is strictly increasing within a room (see SendMessageAsync), so it alone
            // is a valid, stable keyset cursor and needs no provider-specific id comparison.
            query = query.Where(x => x.SentAt < cursor);
        }

        var rows = await query
            .OrderByDescending(x => x.SentAt)
            .Take(pageSize + 1)
            .Select(x => new MessageRow(
                x.Id,
                x.ChatRoomId,
                x.SenderId,
                x.Kind,
                x.SystemEvent,
                x.ReferenceId,
                x.ReferenceName,
                x.ActorId,
                x.CipherText,
                x.Nonce,
                x.Tag,
                x.KeyVersion,
                x.ClientMessageId,
                x.SentAt))
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > pageSize;

        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        // System events are authored by the actor (or nobody), so both id sets need resolving.
        var handles = await access.GetHandlesAsync(
            rows.SelectMany(x => new[] { x.SenderId, x.ActorId }),
            cancellationToken);

        // Queried newest-first for the keyset cursor, returned oldest-first for rendering.
        rows.Reverse();

        var messages = rows
            .Select(row => ToDto(row, handles))
            .ToList();

        return new MessagePageDto(messages, hasMore);
    }

    public async Task<OutgoingMessage> SendMessageAsync(
        Guid chatRoomId,
        Guid senderId,
        Guid clientMessageId,
        string content,
        CancellationToken cancellationToken)
    {
        var membership = await access.RequireMemberAsync(chatRoomId, senderId, cancellationToken);

        if (membership.Room.IsSystem)
        {
            throw AppException.BadRequest(
                ErrorCodes.Unsupported,
                "System notifications are generated by the server and cannot be written to.");
        }

        var text = content?.Trim() ?? string.Empty;

        if (text.Length == 0)
        {
            throw AppException.BadRequest(ErrorCodes.ValidationFailed, "Message content cannot be empty.");
        }

        if (text.Length > InputRules.MessageMaxLength)
        {
            throw AppException.BadRequest(
                ErrorCodes.ValidationFailed,
                $"Message content cannot exceed {InputRules.MessageMaxLength} characters.");
        }

        if (clientMessageId == Guid.Empty)
        {
            clientMessageId = Guid.CreateVersion7();
        }

        var senderHandle = await db.Users
            .AsNoTracking()
            .Where(x => x.Id == senderId)
            .Select(x => x.UserHandle ?? x.Login)
            .SingleAsync(cancellationToken);

        var existing = await db.Messages
            .AsNoTracking()
            .Where(x => x.SenderId == senderId && x.ClientMessageId == clientMessageId)
            .Select(x => new MessageRow(
                x.Id,
                x.ChatRoomId,
                x.SenderId,
                x.Kind,
                x.SystemEvent,
                x.ReferenceId,
                x.ReferenceName,
                x.ActorId,
                x.CipherText,
                x.Nonce,
                x.Tag,
                x.KeyVersion,
                x.ClientMessageId,
                x.SentAt))
            .SingleOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            return await ToOutgoingAsync(existing, senderHandle, cancellationToken);
        }

        var room = await db.ChatRooms.SingleAsync(x => x.Id == chatRoomId, cancellationToken);

        // SentAt must be strictly increasing inside a room: it is the message ordering key
        // and the keyset pagination cursor, and .NET 10+ can return the same tick twice.
        var now = Now > room.LastMessageAt ? Now : room.LastMessageAt.AddTicks(1);

        var messageId = Guid.CreateVersion7();
        var payload = encryption.Encrypt(text, chatRoomId, messageId);

        db.Messages.Add(new Message
        {
            Id = messageId,
            ChatRoomId = chatRoomId,
            SenderId = senderId,
            CipherText = payload.CipherText,
            Nonce = payload.Nonce,
            Tag = payload.Tag,
            KeyVersion = payload.KeyVersion,
            ClientMessageId = clientMessageId,
            SentAt = now,
        });

        room.LastMessageAt = now;
        room.LastMessageId = messageId;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DbErrors.IsUniqueViolation(exception))
        {
            var raced = await db.Messages
                .AsNoTracking()
                .Where(x => x.SenderId == senderId && x.ClientMessageId == clientMessageId)
                .Select(x => new MessageRow(
                    x.Id,
                    x.ChatRoomId,
                    x.SenderId,
                    x.Kind,
                    x.SystemEvent,
                    x.ReferenceId,
                    x.ReferenceName,
                    x.ActorId,
                    x.CipherText,
                    x.Nonce,
                    x.Tag,
                    x.KeyVersion,
                    x.ClientMessageId,
                    x.SentAt))
                .SingleAsync(cancellationToken);

            return await ToOutgoingAsync(raced, senderHandle, cancellationToken);
        }

        var saved = await db.Messages
            .AsNoTracking()
            .Where(x => x.Id == messageId)
            .Select(x => new MessageRow(
                x.Id,
                x.ChatRoomId,
                x.SenderId,
                x.Kind,
                x.SystemEvent,
                x.ReferenceId,
                x.ReferenceName,
                x.ActorId,
                x.CipherText,
                x.Nonce,
                x.Tag,
                x.KeyVersion,
                x.ClientMessageId,
                x.SentAt))
            .SingleAsync(cancellationToken);

        return new OutgoingMessage(
            ToDto(saved, new Dictionary<Guid, string> { [senderId] = senderHandle }),
            await GetMemberIdsAsync(chatRoomId, cancellationToken));
    }

    public async Task<MessageReadDto> MarkReadAsync(
        Guid chatRoomId,
        Guid userId,
        Guid lastReadMessageId,
        CancellationToken cancellationToken)
    {
        var membership = await db.ChatMembers
            .SingleOrDefaultAsync(x => x.ChatRoomId == chatRoomId && x.UserId == userId, cancellationToken)
            ?? throw AppException.NotFound("Chat was not found.");

        var message = await db.Messages
            .AsNoTracking()
            .Where(x => x.Id == lastReadMessageId && x.ChatRoomId == chatRoomId)
            .Select(x => new { x.Id, x.SentAt })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw AppException.NotFound("Message was not found.");

        if (message.SentAt <= membership.LastReadAt)
        {
            return new MessageReadDto(chatRoomId, userId, membership.LastReadMessageId ?? message.Id, membership.LastReadAt);
        }

        membership.LastReadAt = message.SentAt;
        membership.LastReadMessageId = message.Id;

        await db.SaveChangesAsync(cancellationToken);

        return new MessageReadDto(chatRoomId, userId, message.Id, Now);
    }

    // ---------------------------------------------------------------- membership helpers

    public async Task<IReadOnlyList<Guid>> GetMemberIdsAsync(Guid chatRoomId, CancellationToken cancellationToken) =>
        await db.ChatMembers
            .AsNoTracking()
            .Where(x => x.ChatRoomId == chatRoomId)
            .Select(x => x.UserId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetChatIdsForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.ChatMembers
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.ChatRoomId)
            .ToListAsync(cancellationToken);

    /// <summary>Users who share at least one chat with the given user, used for presence fan-out.</summary>
    public async Task<IReadOnlyList<Guid>> GetCoMemberIdsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var chatIds = await GetChatIdsForUserAsync(userId, cancellationToken);

        if (chatIds.Count == 0)
        {
            return [];
        }

        return await db.ChatMembers
            .AsNoTracking()
            .Where(x => x.UserId != userId && chatIds.Contains(x.ChatRoomId))
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    // ---------------------------------------------------------------- internals

    private async Task<OutgoingMessage> ToOutgoingAsync(MessageRow row, string senderHandle, CancellationToken cancellationToken)
    {
        var dto = ToDto(row, new Dictionary<Guid, string> { [row.SenderId!.Value] = senderHandle });

        return new OutgoingMessage(dto, await GetMemberIdsAsync(row.ChatRoomId, cancellationToken));
    }

    private MessageDto ToDto(MessageRow row, Dictionary<Guid, string> handles)
    {
        if (row.Kind == MessageKind.System)
        {
            var systemEvent = row.SystemEvent is null
                ? null
                : new SystemEventPreviewDto(
                    row.SystemEvent.Value.ToString(),
                    row.ActorId,
                    row.ActorId is null ? null : handles.GetValueOrDefault(row.ActorId.Value),
                    row.ReferenceId,
                    row.ReferenceName);

            return new MessageDto(
                row.Id,
                row.ChatRoomId,
                nameof(MessageKind.System),
                null,
                systemEvent?.ActorHandle,
                null,
                row.ClientMessageId,
                row.SentAt,
                systemEvent);
        }

        return new MessageDto(
            row.Id,
            row.ChatRoomId,
            nameof(MessageKind.User),
            row.SenderId,
            row.SenderId is null ? null : handles.GetValueOrDefault(row.SenderId.Value, "unknown"),
            encryption.Decrypt(row.CipherText, row.Nonce, row.Tag, row.KeyVersion, row.ChatRoomId, row.Id),
            row.ClientMessageId,
            row.SentAt,
            null);
    }

    private string? TryDecrypt(PreviewRow row)
    {
        try
        {
            return encryption.Decrypt(row.CipherText, row.Nonce, row.Tag, row.KeyVersion, row.ChatRoomId, row.Id);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // A message encrypted with a key that is no longer configured: hide the preview instead of failing the whole list.
            return null;
        }
    }

    private async Task AddFriendRequestIfPossibleAsync(
        Guid requesterId,
        Guid addresseeId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var pairKey = Friendship.BuildPairKey(requesterId, addresseeId);

        var tracked = db.Friendships.Local.FirstOrDefault(x => x.PairKey == pairKey);

        if (tracked is not null)
        {
            return;
        }

        var existing = await db.Friendships.SingleOrDefaultAsync(x => x.PairKey == pairKey, cancellationToken);

        if (existing is not null)
        {
            if (existing.Status == FriendshipStatus.Declined)
            {
                existing.Status = FriendshipStatus.Pending;
                existing.RequesterId = requesterId;
                existing.AddresseeId = addresseeId;
                existing.CreatedAt = now;
                existing.RespondedAt = null;
            }

            return;
        }

        db.Friendships.Add(new Friendship
        {
            Id = Guid.CreateVersion7(),
            RequesterId = requesterId,
            AddresseeId = addresseeId,
            PairKey = pairKey,
            Status = FriendshipStatus.Pending,
            CreatedAt = now,
        });
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

    private static string ResolveTitle(ChatRoomKind kind, string? name, List<ChatParticipantDto> people, Guid userId) =>
        kind switch
        {
            ChatRoomKind.Group => name ?? "Group",
            ChatRoomKind.System => "Notifications",
            _ => people.FirstOrDefault(x => x.UserId != userId)?.UserHandle ?? "Direct chat",
        };

    private static SystemEventPreviewDto? BuildSystemPreview(PreviewRow row, Dictionary<Guid, string> actorHandles)
    {
        if (row.Kind != MessageKind.System || row.SystemEvent is null)
        {
            return null;
        }

        return new SystemEventPreviewDto(
            row.SystemEvent.Value.ToString(),
            row.ActorId,
            row.ActorId is null ? null : actorHandles.GetValueOrDefault(row.ActorId.Value),
            row.ReferenceId,
            row.ReferenceName);
    }

    private sealed record MessageRow(
        Guid Id,
        Guid ChatRoomId,
        Guid? SenderId,
        MessageKind Kind,
        SystemEvent? SystemEvent,
        Guid? ReferenceId,
        string? ReferenceName,
        Guid? ActorId,
        byte[] CipherText,
        byte[] Nonce,
        byte[] Tag,
        int KeyVersion,
        Guid ClientMessageId,
        DateTime SentAt);

    private sealed record PreviewRow(
        Guid Id,
        Guid ChatRoomId,
        Guid? SenderId,
        MessageKind Kind,
        SystemEvent? SystemEvent,
        Guid? ReferenceId,
        string? ReferenceName,
        Guid? ActorId,
        byte[] CipherText,
        byte[] Nonce,
        byte[] Tag,
        int KeyVersion,
        DateTime SentAt);
}
