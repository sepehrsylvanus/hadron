using System.Data;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Collaboration.Api;
using Hadron.Erp.Api.Modules.Collaboration.Domain;
using Hadron.Erp.Api.Modules.Collaboration.Infrastructure;
using Hadron.Erp.Api.Platform.Audit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Hadron.Erp.Api.Modules.Collaboration.Application;

public sealed class CollaborationService(
    IDbContextFactory<HadronDbContext> factory,
    IAuditEventSink audit,
    IHubContext<CollaborationHub> hub,
    LiveKitTokenService liveKit,
    IMeetingMediaGateway meetingMedia,
    ICollaborationFileStore fileStore,
    TimeProvider timeProvider,
    ILogger<CollaborationService> logger)
{
    public async Task<CollaborationPage<DirectoryUserResponse>> Directory(string? search, int page, int pageSize,
        CancellationToken ct)
    {
        ValidatePage(page, pageSize); var pattern = SearchPattern(search);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Users.AsNoTracking().Where(x => x.IsActive);
        if (pattern is not null) query = query.Where(x => EF.Functions.ILike(x.UserName!, pattern, "\\") ||
            EF.Functions.ILike(x.FirstName, pattern, "\\") || EF.Functions.ILike(x.LastName, pattern, "\\") ||
            EF.Functions.ILike(x.FirstName + " " + x.LastName, pattern, "\\"));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.FirstName).ThenBy(x => x.LastName)
            .Skip(checked((page - 1) * pageSize)).Take(pageSize)
            .Select(x => new DirectoryUserResponse(x.Id, x.UserName!, (x.FirstName + " " + x.LastName).Trim(),
                x.OrganizationalUnit.NameFa, $"/api/collaboration/directory/users/{x.Id}/photo")).ToArrayAsync(ct);
        return new(rows, page, pageSize, total);
    }

    public async Task<CollaborationPage<ConversationSummaryResponse>> Conversations(CollaborationActor actor,
        string? search, int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize); var pattern = SearchPattern(search); var now = timeProvider.GetUtcNow().UtcDateTime;
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.CollaborationConversations.AsNoTracking().Where(x => !x.IsArchived &&
            (!x.IsPrivate || x.Members.Any(m => m.UserId == actor.UserId)));
        if (pattern is not null) query = query.Where(x => EF.Functions.ILike(x.Name, pattern, "\\") ||
            (x.Description != null && EF.Functions.ILike(x.Description, pattern, "\\")));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.Messages.Max(m => (DateTime?)m.CreatedAtUtc) ?? x.UpdatedAtUtc)
            .Skip(checked((page - 1) * pageSize)).Take(pageSize)
            .Select(x => new ConversationSummaryResponse(x.Id, x.Type,
                x.Type == ConversationTypes.Direct
                    ? x.Members.Where(member => member.UserId != actor.UserId)
                        .Select(member => member.DisplayName).FirstOrDefault() ?? x.Name
                    : x.Name, x.Description, x.IsPrivate,
                x.IsArchived, x.Members.Count, x.Messages.Count(m => m.CreatedAtUtc >
                    (x.Members.Where(mm => mm.UserId == actor.UserId).Select(mm => mm.LastReadAtUtc).FirstOrDefault() ?? DateTime.MinValue)
                    && m.AuthorUserId != actor.UserId),
                x.Messages.Max(m => (DateTime?)m.CreatedAtUtc),
                x.Messages.OrderByDescending(m => m.CreatedAtUtc).Select(m => m.DeletedAtUtc != null ? "پیام حذف شده" :
                    m.Type == MessageTypes.Sticker ? "استیکر Hadron" : m.Type == MessageTypes.CallEvent ? "رویداد تماس" :
                    m.Type == MessageTypes.LetterReference ? "نامه رسمی" : m.Body).FirstOrDefault(), x.Version))
            .ToArrayAsync(ct);
        _ = now;
        return new(rows, page, pageSize, total);
    }

    public async Task<CollaborationNotificationSummaryResponse> NotificationSummary(CollaborationActor actor,
        int limit, bool includeLetters, bool includeCalls, bool includeMeetings, CancellationToken ct)
    {
        if (limit is < 1 or > 30) throw Validation("limit", "تعداد اعلان‌ها باید بین ۱ و ۳۰ باشد.");
        await using var db = await factory.CreateDbContextAsync(ct);

        var membershipQuery = db.CollaborationConversations.AsNoTracking().Where(conversation =>
            !conversation.IsArchived && conversation.Members.Any(member => member.UserId == actor.UserId));
        var unreadQuery = membershipQuery.Select(conversation => new
        {
            conversation.Id,
            conversation.Type,
            Name = conversation.Type == ConversationTypes.Direct
                ? conversation.Members.Where(member => member.UserId != actor.UserId)
                    .Select(member => member.DisplayName).FirstOrDefault() ?? conversation.Name
                : conversation.Name,
            UnreadCount = conversation.Messages.Count(message => message.AuthorUserId != actor.UserId &&
                message.CreatedAtUtc > (conversation.Members.Where(member => member.UserId == actor.UserId)
                    .Select(member => member.LastReadAtUtc).FirstOrDefault() ?? DateTime.MinValue)),
            Latest = conversation.Messages.Where(message => message.AuthorUserId != actor.UserId)
                .OrderByDescending(message => message.CreatedAtUtc).Select(message => new
                {
                    message.Id, message.AuthorDisplayName, message.Type, message.Body, message.DeletedAtUtc,
                    message.CreatedAtUtc
                }).FirstOrDefault()
        }).Where(row => row.UnreadCount > 0 && row.Latest != null);

        var unreadMessageCount = await unreadQuery.SumAsync(row => row.UnreadCount, ct);
        var unreadRows = await unreadQuery.OrderByDescending(row => row.Latest!.CreatedAtUtc).Take(limit)
            .ToArrayAsync(ct);
        var items = unreadRows.Select(row => new CollaborationNotificationItemResponse(
            "MESSAGE", row.Latest!.Id, row.Id, row.Name,
            row.Latest.DeletedAtUtc.HasValue ? "پیام حذف شده" : row.Latest.Type == MessageTypes.Sticker
                ? "استیکر Hadron" : row.Latest.Type == MessageTypes.CallEvent ? "رویداد تماس"
                : row.Latest.Type == MessageTypes.LetterReference ? "نامه رسمی" : row.Latest.Body,
            row.Latest.AuthorDisplayName, row.Latest.CreatedAtUtc, row.UnreadCount, false, false,
            $"/collaboration/messages/{row.Id}")).ToList();

        var pendingLetterCount = 0;
        if (includeLetters)
        {
            var pendingLetters = db.OfficialLetters.AsNoTracking().Where(letter =>
                (letter.Status == LetterStatuses.Issued && letter.Recipients.Any(recipient =>
                    recipient.UserId == actor.UserId)) ||
                (letter.Status == LetterStatuses.Acknowledged && letter.SenderUserId == actor.UserId));
            pendingLetterCount = await pendingLetters.CountAsync(ct);
            var letterRows = await pendingLetters.OrderByDescending(letter => letter.UpdatedAtUtc).Take(limit)
                .Select(letter => new
                {
                    letter.Id, letter.Number, letter.Subject, letter.Status, letter.SenderDisplayName,
                    letter.IssuedAtUtc, letter.UpdatedAtUtc
                }).ToArrayAsync(ct);
            items.AddRange(letterRows.Select(letter => new CollaborationNotificationItemResponse(
                "LETTER", letter.Id, null, $"{letter.Number} — {letter.Subject}",
                letter.Status == LetterStatuses.Issued ? "نیازمند اعلام دریافت" : "دریافت شد؛ نیازمند بستن",
                letter.SenderDisplayName, letter.IssuedAtUtc ?? letter.UpdatedAtUtc, 1, true, false,
                $"/collaboration/correspondence/{letter.Id}")));
        }

        var activeCallCount = 0;
        if (includeCalls)
        {
            var activeCalls = db.CollaborationCallSessions.AsNoTracking().Where(call =>
                call.Status == CallStatuses.Active && call.Conversation.Members.Any(member =>
                    member.UserId == actor.UserId));
            activeCallCount = await activeCalls.CountAsync(ct);
            var callRows = await activeCalls.OrderByDescending(call => call.StartedAtUtc).Take(limit)
                .Select(call => new
                {
                    call.Id, call.ConversationId, call.HostDisplayName, call.VideoEnabled, call.StartedAtUtc,
                    ConversationName = call.Conversation.Type == ConversationTypes.Direct
                        ? call.Conversation.Members.Where(member => member.UserId != actor.UserId)
                            .Select(member => member.DisplayName).FirstOrDefault() ?? call.Conversation.Name
                        : call.Conversation.Name
                }).ToArrayAsync(ct);
            items.AddRange(callRows.Select(call => new CollaborationNotificationItemResponse(
                "CALL", call.Id, call.ConversationId, call.ConversationName,
                call.VideoEnabled ? "تماس تصویری فعال" : "تماس صوتی فعال", call.HostDisplayName,
                call.StartedAtUtc, 1, true, call.VideoEnabled,
                $"/collaboration/messages/{call.ConversationId}")));
        }

        if (includeMeetings)
        {
            var activeMeetings = db.CollaborationMeetingSessions.AsNoTracking().Where(meeting =>
                meeting.Status == MeetingStatuses.Live && meeting.Participants.Any(participant =>
                    participant.UserId == actor.UserId &&
                    participant.AdmissionStatus == MeetingAdmissionStatuses.Admitted));
            activeCallCount += await activeMeetings.CountAsync(ct);
            var meetingRows = await activeMeetings.OrderByDescending(meeting => meeting.StartedAtUtc)
                .Take(limit).Select(meeting => new
                {
                    meeting.Id, meeting.ConversationId, meeting.Title, meeting.CreatedByDisplayName,
                    meeting.StartedAtUtc, meeting.CreatedAtUtc
                }).ToArrayAsync(ct);
            items.AddRange(meetingRows.Select(meeting => new CollaborationNotificationItemResponse(
                "CALL", meeting.Id, meeting.ConversationId, meeting.Title,
                "جلسه در حال برگزاری", meeting.CreatedByDisplayName,
                meeting.StartedAtUtc ?? meeting.CreatedAtUtc, 1, true, true,
                $"/collaboration/meetings/{meeting.Id}")));
        }

        var ordered = items.OrderByDescending(item => item.RequiresAction)
            .ThenByDescending(item => item.OccurredAtUtc).Take(limit).ToArray();
        return new(unreadMessageCount + pendingLetterCount + activeCallCount, unreadMessageCount,
            pendingLetterCount, activeCallCount, timeProvider.GetUtcNow().UtcDateTime, ordered);
    }

    public async Task<ConversationResponse> Conversation(Guid id, CollaborationActor actor, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.CollaborationConversations.AsNoTracking().Include(x => x.Members)
            .SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound("CONVERSATION_NOT_FOUND", "گفتگو یافت نشد.");
        EnsureAccess(entity, actor);
        var call = await db.CollaborationCallSessions.AsNoTracking().Where(x => x.ConversationId == id && x.Status == CallStatuses.Active)
            .OrderByDescending(x => x.StartedAtUtc).FirstOrDefaultAsync(ct);
        return ToResponse(entity, call);
    }

    public async Task<CollaborationPage<MessageResponse>> Messages(Guid conversationId, CollaborationActor actor,
        DateTime? beforeUtc, int pageSize, CancellationToken ct)
    {
        if (pageSize is < 1 or > 100) throw Validation("pageSize", "تعداد پیام هر صفحه باید بین ۱ و ۱۰۰ باشد.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var conversation = await db.CollaborationConversations.AsNoTracking().Include(x => x.Members)
            .SingleOrDefaultAsync(x => x.Id == conversationId, ct) ?? throw NotFound("CONVERSATION_NOT_FOUND", "گفتگو یافت نشد.");
        EnsureAccess(conversation, actor);
        var query = db.CollaborationMessages.AsNoTracking().Where(x => x.ConversationId == conversationId);
        if (beforeUtc.HasValue) query = query.Where(x => x.CreatedAtUtc < beforeUtc.Value);
        var entities = await query.OrderByDescending(x => x.CreatedAtUtc).Take(pageSize).AsSplitQuery()
            .Include(x => x.Reactions).Include(x => x.Attachments).ToArrayAsync(ct);
        var total = await db.CollaborationMessages.CountAsync(x => x.ConversationId == conversationId, ct);
        return new(entities.Reverse().Select(x => ToResponse(x, actor.UserId)).ToArray(), 1, pageSize, total);
    }

    public async Task<ConversationResponse> CreateChannel(CreateChannelRequest request, CollaborationActor actor,
        CancellationToken ct)
    {
        ValidateConversation(request.Name, request.Description, ConversationTypes.Channel);
        await using var db = await factory.CreateDbContextAsync(ct);
        var members = await ResolveUsers(db, (request.MemberUserIds ?? []).Append(actor.UserId), ct);
        var entity = new Conversation(ConversationTypes.Channel, request.Name, request.Description, request.IsPrivate,
            actor.UserId, actor.DisplayName);
        foreach (var member in members.Values) entity.Members.Add(new(entity.Id, member.Id, member.Name,
            member.Id == actor.UserId ? ConversationMemberRoles.Owner : ConversationMemberRoles.Member));
        db.CollaborationConversations.Add(entity); await db.SaveChangesAsync(ct);
        await Audit(entity.Id, "Created", actor, null, new Dictionary<string, object?>
            { ["type"] = entity.Type, ["name"] = entity.Name, ["isPrivate"] = entity.IsPrivate, ["memberCount"] = entity.Members.Count }, ct);
        return ToResponse(entity, null);
    }

    public async Task<ConversationResponse> CreateDirect(CreateDirectConversationRequest request,
        CollaborationActor actor, CancellationToken ct)
    {
        if (request.OtherUserId == Guid.Empty || request.OtherUserId == actor.UserId)
            throw Validation("otherUserId", "برای گفتگوی مستقیم یک همکار دیگر را انتخاب کنید.");
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var existing = await db.CollaborationConversations.Include(x => x.Members)
            .Where(x => x.Type == ConversationTypes.Direct && !x.IsArchived && x.Members.Count == 2 &&
                x.Members.Any(m => m.UserId == actor.UserId) && x.Members.Any(m => m.UserId == request.OtherUserId))
            .SingleOrDefaultAsync(ct);
        if (existing is not null) { await transaction.CommitAsync(ct); return ToResponse(existing, null); }
        var users = await ResolveUsers(db, [actor.UserId, request.OtherUserId], ct);
        var other = users[request.OtherUserId];
        var conversation = new Conversation(ConversationTypes.Direct, other.Name, null, true, actor.UserId, actor.DisplayName);
        foreach (var user in users.Values) conversation.Members.Add(new(conversation.Id, user.Id, user.Name,
            user.Id == actor.UserId ? ConversationMemberRoles.Owner : ConversationMemberRoles.Member));
        db.CollaborationConversations.Add(conversation); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        await Audit(conversation.Id, "DirectConversationCreated", actor, null,
            new Dictionary<string, object?> { ["otherUserId"] = request.OtherUserId }, ct);
        return ToResponse(conversation, null);
    }

    public async Task<ConversationResponse> UpdateConversation(Guid id, UpdateConversationRequest request,
        CollaborationActor actor, CancellationToken ct)
    {
        ValidateConversation(request.Name, request.Description, ConversationTypes.Channel);
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.CollaborationConversations.Include(x => x.Members).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw NotFound("CONVERSATION_NOT_FOUND", "گفتگو یافت نشد.");
        EnsureOwner(entity, actor); if (entity.Type != ConversationTypes.Channel)
            throw new CollaborationConflictException("DIRECT_CONVERSATION_IMMUTABLE", "مشخصات گفتگوی مستقیم قابل ویرایش نیست.");
        var previous = new Dictionary<string, object?> { ["name"] = entity.Name, ["isPrivate"] = entity.IsPrivate, ["memberCount"] = entity.Members.Count };
        var members = await ResolveUsers(db, (request.MemberUserIds ?? []).Append(actor.UserId), ct);
        entity.Update(request.Name, request.Description, request.IsPrivate, request.ExpectedVersion);
        foreach (var removed in entity.Members.Where(current => !members.ContainsKey(current.UserId)).ToArray())
        {
            entity.Members.Remove(removed);
            db.CollaborationConversationMembers.Remove(removed);
        }
        foreach (var user in members.Values.Where(user => entity.Members.All(current => current.UserId != user.Id)))
        {
            var added = new ConversationMember(entity.Id, user.Id, user.Name,
                user.Id == actor.UserId ? ConversationMemberRoles.Owner : ConversationMemberRoles.Member);
            entity.Members.Add(added);
            db.CollaborationConversationMembers.Add(added);
        }
        await Save(db, ct); await Audit(entity.Id, "Updated", actor, previous,
            new Dictionary<string, object?> { ["name"] = entity.Name, ["isPrivate"] = entity.IsPrivate, ["memberCount"] = entity.Members.Count }, ct);
        await hub.Clients.Group(Group(id)).SendAsync("conversationUpdated", ToResponse(entity, null), ct);
        return ToResponse(entity, null);
    }

    public async Task ArchiveConversation(Guid id, ArchiveConversationRequest request, CollaborationActor actor,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.CollaborationConversations.Include(x => x.Members).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw NotFound("CONVERSATION_NOT_FOUND", "گفتگو یافت نشد.");
        EnsureOwner(entity, actor); entity.Archive(request.ExpectedVersion); await Save(db, ct);
        await Audit(entity.Id, "Archived", actor, null, null, ct);
        await hub.Clients.Group(Group(id)).SendAsync("conversationArchived", new { entity.Id }, ct);
    }

    public async Task<MessageResponse> SendMessage(Guid conversationId, SendMessageRequest request,
        CollaborationActor actor, CancellationToken ct)
    {
        ValidateMessage(request);
        await using var db = await factory.CreateDbContextAsync(ct);
        var conversation = await db.CollaborationConversations.Include(x => x.Members)
            .SingleOrDefaultAsync(x => x.Id == conversationId, ct) ?? throw NotFound("CONVERSATION_NOT_FOUND", "گفتگو یافت نشد.");
        EnsureAccess(conversation, actor);
        await EnsureMeetingConversationPolicy(db, conversationId, requireChat: true,
            requireFileSharing: false, ct);
        if (request.ReplyToMessageId.HasValue && !await db.CollaborationMessages.AnyAsync(x =>
                x.Id == request.ReplyToMessageId && x.ConversationId == conversationId && x.DeletedAtUtc == null, ct))
            throw Validation("replyToMessageId", "پیام مرجع در این گفتگو وجود ندارد.");
        var entity = new ConversationMessage(conversationId, actor.UserId, actor.DisplayName,
            request.Type.Trim().ToUpperInvariant(), request.Body, request.StickerCode, request.ReplyToMessageId, null);
        db.CollaborationMessages.Add(entity); await db.SaveChangesAsync(ct);
        var response = ToResponse(entity, actor.UserId);
        await hub.Clients.Group(Group(conversationId)).SendAsync("messageCreated", response, ct);
        await NotifyUsers(conversation.Members.Select(member => member.UserId), "MESSAGE", entity.Id,
            conversationId, ct);
        return response;
    }

    public async Task<MessageResponse> EditMessage(Guid conversationId, Guid messageId, EditMessageRequest request,
        CollaborationActor actor, bool canModerate, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Trim().Length > 10_000)
            throw Validation("body", "متن پیام الزامی و حداکثر ۱۰٬۰۰۰ نویسه است.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var message = await db.CollaborationMessages.AsSplitQuery().Include(x => x.Conversation).ThenInclude(x => x.Members)
            .Include(x => x.Reactions).Include(x => x.Attachments)
            .SingleOrDefaultAsync(x => x.Id == messageId && x.ConversationId == conversationId, ct)
            ?? throw NotFound("MESSAGE_NOT_FOUND", "پیام یافت نشد.");
        EnsureAccess(message.Conversation, actor);
        if (message.AuthorUserId != actor.UserId && !canModerate) throw new UnauthorizedAccessException();
        var oldBody = message.Body; message.Edit(request.Body, request.ExpectedVersion); await Save(db, ct);
        await Audit(message.Id, "Edited", actor, new Dictionary<string, object?> { ["body"] = oldBody },
            new Dictionary<string, object?> { ["body"] = message.Body }, ct);
        var response = ToResponse(message, actor.UserId);
        await hub.Clients.Group(Group(conversationId)).SendAsync("messageUpdated", response, ct);
        return response;
    }

    public async Task DeleteMessage(Guid conversationId, Guid messageId, DeleteMessageRequest request,
        CollaborationActor actor, bool canModerate, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var message = await db.CollaborationMessages.Include(x => x.Conversation).ThenInclude(x => x.Members)
            .SingleOrDefaultAsync(x => x.Id == messageId && x.ConversationId == conversationId, ct)
            ?? throw NotFound("MESSAGE_NOT_FOUND", "پیام یافت نشد.");
        EnsureAccess(message.Conversation, actor);
        if (message.AuthorUserId != actor.UserId && !canModerate) throw new UnauthorizedAccessException();
        message.Delete(request.ExpectedVersion); await Save(db, ct);
        await Audit(message.Id, "Deleted", actor, null, null, ct);
        await hub.Clients.Group(Group(conversationId)).SendAsync("messageDeleted", new { message.Id, message.Version }, ct);
    }

    public async Task<IReadOnlyList<ReactionResponse>> ToggleReaction(Guid conversationId, Guid messageId,
        ToggleReactionRequest request, CollaborationActor actor, CancellationToken ct)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (!AllowedReactions.Contains(code)) throw Validation("code", "واکنش انتخاب‌شده معتبر نیست.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var message = await db.CollaborationMessages.AsSplitQuery().Include(x => x.Conversation).ThenInclude(x => x.Members)
            .Include(x => x.Reactions).SingleOrDefaultAsync(x => x.Id == messageId && x.ConversationId == conversationId, ct)
            ?? throw NotFound("MESSAGE_NOT_FOUND", "پیام یافت نشد.");
        EnsureAccess(message.Conversation, actor);
        var current = message.Reactions.SingleOrDefault(x => x.UserId == actor.UserId && x.Code == code);
        if (current is null)
        {
            var reaction = new MessageReaction(message.Id, actor.UserId, code);
            message.Reactions.Add(reaction);
            db.CollaborationMessageReactions.Add(reaction);
        }
        else db.Remove(current);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            LogConcurrency(exception, "ToggleReaction");
            throw;
        }
        var response = MapReactions(message.Reactions, actor.UserId);
        await hub.Clients.Group(Group(conversationId)).SendAsync("messageReactionsUpdated", new { message.Id, Reactions = response }, ct);
        return response;
    }

    public async Task MarkRead(Guid conversationId, CollaborationActor actor, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.CollaborationConversations.Include(x => x.Members).SingleOrDefaultAsync(x => x.Id == conversationId, ct)
            ?? throw NotFound("CONVERSATION_NOT_FOUND", "گفتگو یافت نشد."); EnsureAccess(entity, actor);
        var member = entity.Members.SingleOrDefault(x => x.UserId == actor.UserId);
        if (member is null && !entity.IsPrivate)
        {
            member = new(entity.Id, actor.UserId, actor.DisplayName, ConversationMemberRoles.Member);
            entity.Members.Add(member);
            db.CollaborationConversationMembers.Add(member);
        }
        member?.MarkRead(timeProvider.GetUtcNow().UtcDateTime); await db.SaveChangesAsync(ct);
        await NotifyUsers([actor.UserId], "READ", conversationId, conversationId, ct);
    }

    public async Task<AttachmentResponse> AttachToMessage(Guid conversationId, Guid messageId, IFormFile file,
        CollaborationActor actor, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var message = await db.CollaborationMessages.Include(x => x.Conversation).ThenInclude(x => x.Members)
            .SingleOrDefaultAsync(x => x.Id == messageId && x.ConversationId == conversationId, ct)
            ?? throw NotFound("MESSAGE_NOT_FOUND", "پیام یافت نشد."); EnsureAccess(message.Conversation, actor);
        if (message.DeletedAtUtc.HasValue) throw new CollaborationConflictException("MESSAGE_DELETED", "به پیام حذف‌شده نمی‌توان فایل افزود.");
        await EnsureMeetingConversationPolicy(db, conversationId, requireChat: false,
            requireFileSharing: true, ct);
        var content = await CollaborationFileValidator.Read(file, ct);
        return await StoreAttachment(db, messageId, null, content, actor, ct);
    }

    public async Task<AttachmentResponse> AttachToLetter(Guid letterId, IFormFile file, CollaborationActor actor,
        CancellationToken ct)
    {
        var content = await CollaborationFileValidator.Read(file, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var letter = await db.OfficialLetters.Include(x => x.Recipients).SingleOrDefaultAsync(x => x.Id == letterId, ct)
            ?? throw NotFound("LETTER_NOT_FOUND", "نامه یافت نشد."); EnsureLetterAccess(letter, actor);
        if (letter.Status != LetterStatuses.Draft || letter.SenderUserId != actor.UserId)
            throw new CollaborationConflictException("ISSUED_LETTER_IMMUTABLE", "فقط به پیش‌نویس خودتان می‌توانید فایل اضافه کنید.");
        return await StoreAttachment(db, null, letterId, content, actor, ct);
    }

    public async Task<(CollaborationAttachment Attachment, Stream Content)> Download(Guid attachmentId,
        CollaborationActor actor, ICollaborationFileStore store, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.CollaborationAttachments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == attachmentId, ct)
            ?? throw NotFound("ATTACHMENT_NOT_FOUND", "فایل یافت نشد.");
        if (entity.MessageId.HasValue)
        {
            var conversationId = await db.CollaborationMessages.AsNoTracking()
                .Where(x => x.Id == entity.MessageId.Value)
                .Select(x => x.ConversationId)
                .SingleOrDefaultAsync(ct);
            if (conversationId == Guid.Empty)
                throw NotFound("ATTACHMENT_OWNER_NOT_FOUND", "گفتگوی مرتبط با فایل یافت نشد.");
            var conversation = await db.CollaborationConversations.AsNoTracking().Include(x => x.Members)
                .SingleAsync(x => x.Id == conversationId, ct);
            EnsureAccess(conversation, actor);
            await EnsureMeetingConversationPolicy(db, conversationId, requireChat: false,
                requireFileSharing: true, ct);
        }
        else
        {
            var letter = await db.OfficialLetters.AsNoTracking().Include(x => x.Recipients)
                .SingleAsync(x => x.Id == entity.OfficialLetterId, ct); EnsureLetterAccess(letter, actor);
        }
        return (entity, await store.Get(entity.ObjectKey, ct));
    }

    public async Task<CollaborationPage<OfficialLetterSummaryResponse>> Letters(CollaborationActor actor,
        string? search, string? status, int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize); var pattern = SearchPattern(search); var normalizedStatus = NormalizeStatus(status);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.OfficialLetters.AsNoTracking().Where(x => actor.IsSystemAdministrator ||
            x.SenderUserId == actor.UserId || x.Recipients.Any(r => r.UserId == actor.UserId));
        if (pattern is not null) query = query.Where(x => EF.Functions.ILike(x.Number, pattern, "\\") ||
            EF.Functions.ILike(x.Subject, pattern, "\\"));
        if (normalizedStatus is not null) query = query.Where(x => x.Status == normalizedStatus);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedAtUtc).Skip(checked((page - 1) * pageSize)).Take(pageSize)
            .Select(x => new OfficialLetterSummaryResponse(x.Id, x.Number, x.Subject, x.Classification, x.Status,
                x.SenderUserId, x.SenderDisplayName, x.DueDate, x.IssuedAtUtc, x.CreatedAtUtc, x.Version)).ToArrayAsync(ct);
        return new(rows, page, pageSize, total);
    }

    public async Task<OfficialLetterResponse> Letter(Guid id, CollaborationActor actor, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await LetterQuery(db).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw NotFound("LETTER_NOT_FOUND", "نامه یافت نشد."); EnsureLetterAccess(entity, actor); return ToResponse(entity);
    }

    public async Task<OfficialLetterResponse> CreateLetter(CreateOfficialLetterRequest request,
        CollaborationActor actor, CancellationToken ct)
    {
        ValidateLetter(request.Subject, request.Body, request.Classification, request.Recipients);
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var recipients = await ResolveUsers(db, request.Recipients.Select(x => x.UserId), ct);
        var number = await NextLetterNumber(db, transaction, ct);
        var entity = new OfficialLetter(number, request.Subject, request.Body, request.Classification.Trim().ToUpperInvariant(),
            actor.UserId, actor.DisplayName, request.DueDate);
        ApplyRecipients(entity, request.Recipients, recipients); entity.Events.Add(new(entity.Id, "CREATED", actor.UserId, actor.DisplayName, null));
        db.OfficialLetters.Add(entity); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        await Audit(entity.Id, "Created", actor, null, new Dictionary<string, object?>
            { ["number"] = number, ["classification"] = entity.Classification, ["recipientCount"] = entity.Recipients.Count }, ct);
        return ToResponse(entity);
    }

    public async Task<OfficialLetterResponse> UpdateLetter(Guid id, UpdateOfficialLetterRequest request,
        CollaborationActor actor, CancellationToken ct)
    {
        ValidateLetter(request.Subject, request.Body, request.Classification, request.Recipients);
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await LetterQuery(db).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw NotFound("LETTER_NOT_FOUND", "نامه یافت نشد.");
        if (entity.SenderUserId != actor.UserId) throw new UnauthorizedAccessException();
        var previous = new Dictionary<string, object?> { ["subject"] = entity.Subject, ["classification"] = entity.Classification, ["recipientCount"] = entity.Recipients.Count };
        var recipients = await ResolveUsers(db, request.Recipients.Select(x => x.UserId), ct);
        entity.Update(request.Subject, request.Body, request.Classification.Trim().ToUpperInvariant(), request.DueDate, request.ExpectedVersion);
        SynchronizeRecipients(db, entity, request.Recipients, recipients);
        var updatedEvent = new OfficialLetterEvent(entity.Id, "UPDATED", actor.UserId, actor.DisplayName, null);
        entity.Events.Add(updatedEvent);
        db.OfficialLetterEvents.Add(updatedEvent);
        await Save(db, ct);
        await Audit(entity.Id, "Updated", actor, previous, new Dictionary<string, object?>
            { ["subject"] = entity.Subject, ["classification"] = entity.Classification, ["recipientCount"] = entity.Recipients.Count }, ct);
        return ToResponse(entity);
    }

    public async Task<OfficialLetterResponse> TransitionLetter(Guid id, string transition, LetterTransitionRequest request,
        CollaborationActor actor, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await LetterQuery(db).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw NotFound("LETTER_NOT_FOUND", "نامه یافت نشد."); EnsureLetterAccess(entity, actor);
        switch (transition)
        {
            case "ISSUED":
                if (entity.SenderUserId != actor.UserId) throw new UnauthorizedAccessException(); entity.Issue(request.ExpectedVersion); break;
            case "ACKNOWLEDGED":
                if (!entity.Recipients.Any(x => x.UserId == actor.UserId)) throw new UnauthorizedAccessException(); entity.Acknowledge(request.ExpectedVersion); break;
            case "CLOSED":
                if (entity.SenderUserId != actor.UserId) throw new UnauthorizedAccessException(); entity.Close(request.ExpectedVersion); break;
            default: throw Validation("transition", "گذار وضعیت نامه معتبر نیست.");
        }
        var transitionEvent = new OfficialLetterEvent(entity.Id, transition, actor.UserId, actor.DisplayName, request.Note);
        entity.Events.Add(transitionEvent);
        db.OfficialLetterEvents.Add(transitionEvent);
        await Save(db, ct);
        await Audit(entity.Id, transition, actor, null, new Dictionary<string, object?> { ["status"] = entity.Status, ["note"] = request.Note }, ct);
        await NotifyUsers(entity.Recipients.Select(recipient => recipient.UserId).Append(entity.SenderUserId),
            "LETTER", entity.Id, null, ct);
        return ToResponse(entity);
    }

    public CallConfigurationResponse CallConfiguration() => liveKit.Configuration();

    public async Task<CallJoinResponse> StartCall(Guid conversationId, StartCallRequest request,
        CollaborationActor actor, CancellationToken ct)
    {
        var configuration = liveKit.Configuration();
        if (!configuration.Available) throw new CollaborationConflictException("CALL_PROVIDER_NOT_CONFIGURED", configuration.Reason!);
        await using var db = await factory.CreateDbContextAsync(ct);
        var conversation = await db.CollaborationConversations.Include(x => x.Members).SingleOrDefaultAsync(x => x.Id == conversationId, ct)
            ?? throw NotFound("CONVERSATION_NOT_FOUND", "گفتگو یافت نشد."); EnsureAccess(conversation, actor);
        var active = await db.CollaborationCallSessions.SingleOrDefaultAsync(x => x.ConversationId == conversationId && x.Status == CallStatuses.Active, ct);
        var createdNew = false;
        if (active is null)
        {
            active = new(conversationId, $"hadron-{conversationId:N}-{Guid.NewGuid():N}", actor.UserId, actor.DisplayName, request.VideoEnabled);
            db.CollaborationCallSessions.Add(active);
            db.CollaborationMessages.Add(new(conversationId, actor.UserId, actor.DisplayName, MessageTypes.CallEvent,
                request.VideoEnabled ? "تماس تصویری آغاز شد." : "تماس صوتی آغاز شد.", null, null, null));
            try
            {
                await db.SaveChangesAsync(ct);
                createdNew = true;
            }
            catch (DbUpdateException exception) when (IsActiveCallUniquenessViolation(exception))
            {
                // A concurrent request won the race. The database constraint is the authority;
                // return the winning active session instead of leaking a provider-independent 500.
                db.ChangeTracker.Clear();
                active = await db.CollaborationCallSessions.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.ConversationId == conversationId && x.Status == CallStatuses.Active, ct);
                if (active is null) throw;
            }

            if (createdNew)
            {
                await Audit(active.Id, "Started", actor, null,
                    new Dictionary<string, object?> { ["conversationId"] = conversationId, ["videoEnabled"] = request.VideoEnabled }, ct);
                await hub.Clients.Group(Group(conversationId)).SendAsync("callStarted", ToResponse(active), ct);
                await NotifyUsers(conversation.Members.Select(member => member.UserId), "CALL", active.Id,
                    conversationId, ct);
            }
        }
        return new(
            ToResponse(active),
            configuration.ServerUrl!,
            liveKit.CreateJoinToken(active.RoomName, actor),
            createdNew);
    }

    public async Task<CallJoinResponse> JoinCall(Guid callId, CollaborationActor actor, CancellationToken ct)
    {
        var configuration = liveKit.Configuration();
        if (!configuration.Available) throw new CollaborationConflictException("CALL_PROVIDER_NOT_CONFIGURED", configuration.Reason!);
        await using var db = await factory.CreateDbContextAsync(ct);
        var call = await db.CollaborationCallSessions.Include(x => x.Conversation).ThenInclude(x => x.Members)
            .SingleOrDefaultAsync(x => x.Id == callId, ct) ?? throw NotFound("CALL_NOT_FOUND", "تماس یافت نشد.");
        EnsureAccess(call.Conversation, actor); if (call.Status != CallStatuses.Active)
            throw new CollaborationConflictException("CALL_ENDED", "این تماس پایان یافته است.");
        return new(ToResponse(call), configuration.ServerUrl!, liveKit.CreateJoinToken(call.RoomName, actor), false);
    }

    public async Task EndCall(Guid callId, EndCallRequest request, CollaborationActor actor, bool canModerate,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var call = await db.CollaborationCallSessions.Include(x => x.Conversation).ThenInclude(x => x.Members)
            .SingleOrDefaultAsync(x => x.Id == callId, ct) ?? throw NotFound("CALL_NOT_FOUND", "تماس یافت نشد.");
        EnsureAccess(call.Conversation, actor);
        if (call.HostUserId != actor.UserId && !canModerate) throw new UnauthorizedAccessException();
        if (call.Version != request.ExpectedVersion)
            throw new CollaborationConflictException("CALL_VERSION_CONFLICT",
                "وضعیت تماس تغییر کرده است؛ گفتگو را تازه‌سازی کنید.");
        if (call.Status == CallStatuses.Ended) return;
        if (!meetingMedia.Available)
            throw new CollaborationConflictException("CALL_PROVIDER_NOT_CONFIGURED",
                meetingMedia.UnavailabilityReason ?? "سرویس تماس پیکربندی نشده است.");

        var providerResult = await meetingMedia.EndRoomAsync(call.RoomName, ct);
        if (!providerResult.Success)
        {
            await Audit(call.Id, "ProviderEndFailed", actor, null,
                new Dictionary<string, object?> { ["failureCode"] = providerResult.FailureCode }, ct);
            throw new CollaborationConflictException("CALL_PROVIDER_END_FAILED",
                "پایان تماس در سرویس رسانه تأیید نشد؛ تماس همچنان فعال است و باید دوباره تلاش شود.");
        }

        call.End(request.ExpectedVersion); db.CollaborationMessages.Add(new(call.ConversationId, actor.UserId, actor.DisplayName,
            MessageTypes.CallEvent, "تماس پایان یافت.", null, null, null)); await Save(db, ct);
        await Audit(call.Id, "Ended", actor, null,
            new Dictionary<string, object?> { ["providerRoomAlreadyAbsent"] = providerResult.AlreadyAbsent }, ct);
        await hub.Clients.Group(Group(call.ConversationId)).SendAsync("callEnded", ToResponse(call), ct);
        await NotifyUsers(call.Conversation.Members.Select(member => member.UserId), "CALL", call.Id,
            call.ConversationId, ct);
    }

    public async Task EnsureConversationAccess(Guid conversationId, CollaborationActor actor, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var conversation = await db.CollaborationConversations.AsNoTracking().Include(x => x.Members)
            .SingleOrDefaultAsync(x => x.Id == conversationId, ct) ?? throw NotFound("CONVERSATION_NOT_FOUND", "گفتگو یافت نشد.");
        EnsureAccess(conversation, actor);
    }

    private async Task<AttachmentResponse> StoreAttachment(HadronDbContext db, Guid? messageId, Guid? letterId,
        CollaborationFileContent content, CollaborationActor actor, CancellationToken ct)
    {
        var key = $"{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid():N}";
        await using var lease = new CollaborationFileWriteLease(fileStore, key, logger);
        await lease.Upload(content, ct);
        var entity = new CollaborationAttachment(messageId, letterId, actor.UserId, actor.DisplayName, key,
            content.FileName, content.ContentType, content.Bytes.LongLength, content.Sha256, "TYPE_VALIDATED");
        db.CollaborationAttachments.Add(entity); await db.SaveChangesAsync(ct); lease.Complete();
        await Audit(entity.Id, "Uploaded", actor, null, new Dictionary<string, object?>
            { ["fileName"] = entity.OriginalFileName, ["contentType"] = entity.ContentType, ["size"] = entity.Size, ["sha256"] = entity.Sha256 }, ct);
        return ToResponse(entity);
    }

    private static async Task EnsureMeetingConversationPolicy(HadronDbContext db, Guid conversationId,
        bool requireChat, bool requireFileSharing, CancellationToken ct)
    {
        var policy = await db.CollaborationMeetingSessions.AsNoTracking()
            .Where(x => x.ConversationId == conversationId)
            .Select(x => new { x.ChatEnabled, x.FileSharingEnabled })
            .SingleOrDefaultAsync(ct);
        if (policy is null) return;
        if (requireChat && !policy.ChatEnabled)
            throw new CollaborationConflictException("MEETING_CHAT_DISABLED",
                "گفتگوی متنی برای این جلسه غیرفعال است.");
        if (requireFileSharing && !policy.FileSharingEnabled)
            throw new CollaborationConflictException("MEETING_FILE_SHARING_DISABLED",
                "اشتراک فایل برای این جلسه غیرفعال است.");
    }

    private async Task Audit(Guid id, string action, CollaborationActor actor, IReadOnlyDictionary<string, object?>? oldValues,
        IReadOnlyDictionary<string, object?>? newValues, CancellationToken ct) => await audit.WriteAsync(new(
            "Collaboration", id, action, timeProvider.GetUtcNow().UtcDateTime, actor.UserId.ToString(), null,
            oldValues, newValues, null, "UI"), ct);

    private async Task NotifyUsers(IEnumerable<Guid> userIds, string kind, Guid entityId,
        Guid? conversationId, CancellationToken ct)
    {
        foreach (var userId in userIds.Distinct())
            await hub.Clients.Group(UserGroup(userId)).SendAsync("notificationUpdated",
                new { Kind = kind, EntityId = entityId, ConversationId = conversationId }, ct);
    }

    private async Task Save(HadronDbContext db, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException exception)
        {
            LogConcurrency(exception, "Save");
            throw new CollaborationConflictException(
                "COLLABORATION_VERSION_CONFLICT", "اطلاعات هم‌زمان تغییر کرده است؛ صفحه را تازه‌سازی کنید.");
        }
    }

    private void LogConcurrency(DbUpdateConcurrencyException exception, string operation) =>
        logger.LogWarning(exception,
            "Collaboration concurrency conflict in {Operation}. Entries: {Entries}", operation,
            string.Join(", ", exception.Entries.Select(entry =>
                $"{entry.Metadata.ClrType.Name}:{entry.State}:" +
                string.Join("|", entry.Properties.Where(property => property.Metadata.IsConcurrencyToken)
                    .Select(property => $"{property.Metadata.Name}={property.OriginalValue}->{property.CurrentValue}")))));

    private static async Task<Dictionary<Guid, (Guid Id, string Name)>> ResolveUsers(HadronDbContext db,
        IEnumerable<Guid> ids, CancellationToken ct)
    {
        var requested = ids.Where(x => x != Guid.Empty).Distinct().ToArray();
        var users = await db.Users.AsNoTracking().Where(x => requested.Contains(x.Id) && x.IsActive)
            .Select(x => new { x.Id, Name = (x.FirstName + " " + x.LastName).Trim() }).ToArrayAsync(ct);
        if (users.Length != requested.Length) throw Validation("memberUserIds", "یک یا چند کاربر انتخاب‌شده فعال یا معتبر نیستند.");
        return users.ToDictionary(x => x.Id, x => (x.Id, string.IsNullOrWhiteSpace(x.Name) ? "کاربر Hadron" : x.Name));
    }

    private static void ApplyRecipients(OfficialLetter letter, IReadOnlyList<LetterRecipientInput> inputs,
        IReadOnlyDictionary<Guid, (Guid Id, string Name)> users)
    {
        foreach (var input in inputs.DistinctBy(x => new { x.UserId, Type = x.Type.Trim().ToUpperInvariant() }))
            letter.Recipients.Add(new(letter.Id, input.UserId, users[input.UserId].Name, input.Type.Trim().ToUpperInvariant()));
    }

    private static void SynchronizeRecipients(HadronDbContext db, OfficialLetter letter,
        IReadOnlyList<LetterRecipientInput> inputs, IReadOnlyDictionary<Guid, (Guid Id, string Name)> users)
    {
        var desired = inputs.DistinctBy(x => new { x.UserId, Type = x.Type.Trim().ToUpperInvariant() })
            .Select(x => (x.UserId, Type: x.Type.Trim().ToUpperInvariant())).ToHashSet();
        foreach (var existing in letter.Recipients.Where(x => !desired.Contains((x.UserId, x.Type))).ToArray())
            db.Remove(existing);
        foreach (var (userId, type) in desired.Where(x => !letter.Recipients.Any(existing =>
                     existing.UserId == x.UserId && existing.Type == x.Type)))
        {
            var recipient = new OfficialLetterRecipient(letter.Id, userId, users[userId].Name, type);
            letter.Recipients.Add(recipient);
            db.OfficialLetterRecipients.Add(recipient);
        }
    }

    private static void ValidateConversation(string name, string? description, string type)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 160) errors["name"] = ["نام گفتگو الزامی و حداکثر ۱۶۰ نویسه است."];
        if (description?.Trim().Length > 1000) errors["description"] = ["توضیح حداکثر ۱۰۰۰ نویسه است."];
        if (type != ConversationTypes.Channel) errors["type"] = ["نوع گفتگو معتبر نیست."];
        if (errors.Count > 0) throw new CollaborationValidationException(errors);
    }

    private static void ValidateMessage(SendMessageRequest request)
    {
        var type = request.Type?.Trim().ToUpperInvariant(); var errors = new Dictionary<string, string[]>();
        if (type is not (MessageTypes.Text or MessageTypes.Sticker)) errors["type"] = ["نوع پیام معتبر نیست."];
        if (type == MessageTypes.Text && (string.IsNullOrWhiteSpace(request.Body) || request.Body.Trim().Length > 10_000))
            errors["body"] = ["متن پیام الزامی و حداکثر ۱۰٬۰۰۰ نویسه است."];
        if (type == MessageTypes.Sticker && (string.IsNullOrWhiteSpace(request.StickerCode) || !AllowedStickers.Contains(request.StickerCode.Trim().ToUpperInvariant())))
            errors["stickerCode"] = ["استیکر Hadron معتبر نیست."];
        if (errors.Count > 0) throw new CollaborationValidationException(errors);
    }

    private static void ValidateLetter(string subject, string body, string classification, IReadOnlyList<LetterRecipientInput> recipients)
    {
        var errors = new Dictionary<string, string[]>(); var cls = classification?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(subject) || subject.Trim().Length > 300) errors["subject"] = ["موضوع الزامی و حداکثر ۳۰۰ نویسه است."];
        if (string.IsNullOrWhiteSpace(body) || body.Trim().Length > 40_000) errors["body"] = ["متن نامه الزامی و حداکثر ۴۰٬۰۰۰ نویسه است."];
        if (cls is not (LetterClassifications.Normal or LetterClassifications.Confidential or LetterClassifications.HighlyConfidential))
            errors["classification"] = ["طبقه‌بندی نامه معتبر نیست."];
        if (recipients.Count == 0 || recipients.All(x => !string.Equals(x.Type, LetterRecipientTypes.To, StringComparison.OrdinalIgnoreCase)))
            errors["recipients"] = ["حداقل یک گیرنده اصلی الزامی است."];
        if (recipients.Any(x => x.UserId == Guid.Empty || x.Type.Trim().ToUpperInvariant() is not (LetterRecipientTypes.To or LetterRecipientTypes.Cc)))
            errors["recipients"] = ["گیرنده یا نوع گیرنده معتبر نیست."];
        if (errors.Count > 0) throw new CollaborationValidationException(errors);
    }

    private static string? NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null; var value = status.Trim().ToUpperInvariant();
        if (value is not (LetterStatuses.Draft or LetterStatuses.Issued or LetterStatuses.Acknowledged or LetterStatuses.Closed))
            throw Validation("status", "وضعیت نامه معتبر نیست.");
        return value;
    }

    private static void EnsureAccess(Conversation conversation, CollaborationActor actor)
    {
        if (conversation.IsArchived) throw new CollaborationConflictException("CONVERSATION_ARCHIVED", "این گفتگو بایگانی شده است.");
        if (conversation.IsPrivate && !conversation.Members.Any(x => x.UserId == actor.UserId) && !actor.IsSystemAdministrator)
            throw new UnauthorizedAccessException();
    }

    private static void EnsureOwner(Conversation conversation, CollaborationActor actor)
    {
        EnsureAccess(conversation, actor);
        if (!actor.IsSystemAdministrator && !conversation.Members.Any(x => x.UserId == actor.UserId && x.Role == ConversationMemberRoles.Owner))
            throw new UnauthorizedAccessException();
    }

    private static void EnsureLetterAccess(OfficialLetter letter, CollaborationActor actor)
    {
        if (!actor.IsSystemAdministrator && letter.SenderUserId != actor.UserId && !letter.Recipients.Any(x => x.UserId == actor.UserId))
            throw new UnauthorizedAccessException();
    }

    private static IQueryable<OfficialLetter> LetterQuery(HadronDbContext db) => db.OfficialLetters.AsSplitQuery()
        .Include(x => x.Recipients).Include(x => x.Events).Include(x => x.Attachments);

    private static async Task<string> NextLetterNumber(HadronDbContext db, IDbContextTransaction transaction, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT nextval('collaboration.official_letter_number_seq')", connection,
            (NpgsqlTransaction)transaction.GetDbTransaction());
        return $"HT-COR-{(long)(await command.ExecuteScalarAsync(ct))!:D7}";
    }

    private static ConversationResponse ToResponse(Conversation x, CallSession? call) => new(x.Id, x.Type, x.Name,
        x.Description, x.IsPrivate, x.IsArchived, x.CreatedByUserId, x.CreatedByDisplayName, x.CreatedAtUtc, x.UpdatedAtUtc,
        x.Version, x.Members.OrderBy(m => m.DisplayName).Select(m => new ConversationMemberResponse(m.UserId, m.DisplayName,
            m.Role, m.JoinedAtUtc, m.LastReadAtUtc)).ToArray(), call is null ? null : ToResponse(call));

    private static MessageResponse ToResponse(ConversationMessage x, Guid actorId) => new(x.Id, x.ConversationId,
        x.AuthorUserId, x.AuthorDisplayName, x.Type, x.Body, x.StickerCode, x.ReplyToMessageId, x.OfficialLetterId,
        x.CreatedAtUtc, x.EditedAtUtc, x.DeletedAtUtc, x.Version, MapReactions(x.Reactions, actorId),
        x.Attachments.OrderBy(a => a.CreatedAtUtc).Select(ToResponse).ToArray());

    private static IReadOnlyList<ReactionResponse> MapReactions(IEnumerable<MessageReaction> reactions, Guid actorId) =>
        reactions.GroupBy(x => x.Code).OrderBy(x => x.Key).Select(x => new ReactionResponse(x.Key, x.Count(),
            x.Any(r => r.UserId == actorId))).ToArray();

    private static AttachmentResponse ToResponse(CollaborationAttachment x) => new(x.Id, x.OriginalFileName,
        x.ContentType, x.Size, x.Sha256, x.ValidationStatus, $"/api/collaboration/attachments/{x.Id}",
        x.UploadedByUserId, x.UploadedByDisplayName, x.CreatedAtUtc);

    private static OfficialLetterResponse ToResponse(OfficialLetter x) => new(x.Id, x.Number, x.Subject, x.Body,
        x.Classification, x.Status, x.SenderUserId, x.SenderDisplayName, x.DueDate, x.IssuedAtUtc, x.AcknowledgedAtUtc,
        x.ClosedAtUtc, x.CreatedAtUtc, x.UpdatedAtUtc, x.Version,
        x.Recipients.OrderBy(r => r.Type).ThenBy(r => r.DisplayName).Select(r => new OfficialLetterRecipientResponse(
            r.UserId, r.DisplayName, r.Type)).ToArray(), x.Events.OrderBy(e => e.OccurredAtUtc).Select(e =>
            new OfficialLetterEventResponse(e.Id, e.EventType, e.ActorUserId, e.ActorDisplayName, e.Note, e.OccurredAtUtc)).ToArray(),
        x.Attachments.OrderBy(a => a.CreatedAtUtc).Select(ToResponse).ToArray());

    private static CallSessionResponse ToResponse(CallSession x) => new(x.Id, x.ConversationId, x.Provider, x.RoomName,
        x.Status, x.HostUserId, x.HostDisplayName, x.VideoEnabled, x.StartedAtUtc, x.EndedAtUtc, x.Version);

    private static bool IsActiveCallUniquenessViolation(DbUpdateException exception) =>
        (exception.InnerException as PostgresException ?? exception.GetBaseException() as PostgresException) is
        { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_call_sessions_one_active_per_conversation" };

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1) throw Validation("page", "شماره صفحه باید مثبت باشد.");
        if (pageSize is < 1 or > 100) throw Validation("pageSize", "اندازه صفحه باید بین ۱ و ۱۰۰ باشد.");
    }
    private static string? SearchPattern(string? search) => string.IsNullOrWhiteSpace(search) ? null :
        $"%{search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
    private static CollaborationValidationException Validation(string key, string value) => new(new Dictionary<string, string[]> { [key] = [value] });
    private static CollaborationNotFoundException NotFound(string code, string value) => new(code, value);
    public static string Group(Guid conversationId) => $"conversation:{conversationId:N}";
    public static string UserGroup(Guid userId) => $"collaboration-user:{userId:N}";

    private static readonly HashSet<string> AllowedStickers = new(StringComparer.OrdinalIgnoreCase)
        { "HELLO", "DONE", "CHECKING", "WAIT", "THANKS", "SENDING", "SORRY", "GREAT" };
    private static readonly HashSet<string> AllowedReactions = new(StringComparer.OrdinalIgnoreCase)
        { "LIKE", "THANKS", "DONE", "CHECK", "HEART" };
}
