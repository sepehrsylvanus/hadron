using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Collaboration.Api;
using Hadron.Erp.Api.Modules.Collaboration.Domain;
using Hadron.Erp.Api.Platform.Audit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Hadron.Erp.Api.Modules.Collaboration.Application;

public sealed class MeetingService(
    IDbContextFactory<HadronDbContext> factory,
    IMeetingMediaGateway media,
    IAuditEventSink audit,
    IHubContext<CollaborationHub> hub,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider,
    ILogger<MeetingService> logger)
{
    private const int MaximumParticipants = 300;

    public async Task<CollaborationPage<MeetingSummaryResponse>> List(CollaborationActor actor, string? search,
        string? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var query = db.CollaborationMeetingSessions.AsNoTracking().Include(x => x.Participants).AsQueryable();
        if (!actor.IsSystemAdministrator)
            query = query.Where(x => x.Participants.Any(p => p.UserId == actor.UserId));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var titlePattern = $"%{EscapeLike(term)}%";
            if (Guid.TryParseExact(term, "D", out var meetingId))
            {
                query = query.Where(x => x.Id == meetingId ||
                    EF.Functions.ILike(x.Title, titlePattern, "\\"));
            }
            else if (term.Length == 8 && term.All(Uri.IsHexDigit))
            {
                // The worklist displays the first eight canonical UUID characters as its
                // human-readable identifier. Npgsql translates Guid.ToString() to a UUID
                // text cast; no provider-side Guid parsing is attempted here.
                var idPrefixPattern = $"{term.ToLowerInvariant()}%";
                query = query.Where(x => EF.Functions.ILike(x.Id.ToString(), idPrefixPattern, "\\") ||
                    EF.Functions.ILike(x.Title, titlePattern, "\\"));
            }
            else
            {
                query = query.Where(x => EF.Functions.ILike(x.Title, titlePattern, "\\"));
            }
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.Trim().ToUpperInvariant();
            if (normalizedStatus is not (MeetingStatuses.Scheduled or MeetingStatuses.Live or
                MeetingStatuses.Ended or MeetingStatuses.Cancelled))
                throw new CollaborationValidationException(new Dictionary<string, string[]>
                    { ["status"] = ["وضعیت جلسه معتبر نیست."] });
            query = query.Where(x => x.Status == normalizedStatus);
        }
        var count = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.Status == MeetingStatuses.Live)
            .ThenBy(x => x.ScheduledStartAtUtc ?? x.CreatedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken);
        return new(rows.Select(MeetingSupport.Summary).ToArray(), page, pageSize, count);
    }

    public async Task<MeetingSessionResponse> Get(Guid meetingId, CollaborationActor actor,
        CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var meeting = await Load(db, meetingId, cancellationToken);
        EnsureVisible(meeting, actor);
        return MeetingSupport.Meeting(meeting);
    }

    public async Task<MeetingSessionResponse> Create(CreateMeetingRequest request, CollaborationActor actor,
        CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var title = (request.Title ?? string.Empty).Trim();
        var errors = new Dictionary<string, string[]>();
        if (title.Length is < 2 or > 200)
            errors["title"] = ["عنوان جلسه باید بین ۲ تا ۲۰۰ نویسه باشد."];
        ValidateSchedule(request.ScheduledStartAtUtc, request.ScheduledEndAtUtc, now, errors);
        var inputs = request.Participants?.ToArray() ?? [];
        if (inputs.Length + 1 > MaximumParticipants)
            errors["participants"] = [$"حداکثر ظرفیت جلسه {MaximumParticipants} نفر است."];
        if (inputs.GroupBy(x => x.UserId).Any(x => x.Count() > 1) || inputs.Any(x => x.UserId == actor.UserId))
            errors["participants"] = ["هر شرکت‌کننده داخلی باید دقیقاً یک بار ثبت شود؛ میزبان خودکار افزوده می‌شود."];
        if (inputs.Any(x => x.Role is not (MeetingRoles.CoHost or MeetingRoles.Presenter or MeetingRoles.Attendee)))
            errors["participants.role"] = ["نقش اولیه باید COHOST، PRESENTER یا ATTENDEE باشد."];
        if (errors.Count > 0) throw new CollaborationValidationException(errors);
        if (!request.ScheduledStartAtUtc.HasValue && !media.Available)
            throw new CollaborationConflictException("CALL_PROVIDER_NOT_CONFIGURED",
                media.UnavailabilityReason ?? "سرویس تماس پیکربندی نشده است.");

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var requestedIds = inputs.Select(x => x.UserId).Distinct().ToArray();
        var users = await db.Users.AsNoTracking().Where(x => requestedIds.Contains(x.Id) && x.IsActive)
            .Select(x => new { x.Id, Name = (x.FirstName + " " + x.LastName).Trim() })
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        if (users.Count != requestedIds.Length)
            throw new CollaborationValidationException(new Dictionary<string, string[]>
                { ["participants"] = ["یک یا چند کاربر انتخاب‌شده فعال یا موجود نیستند."] });

        var conversation = new Conversation(ConversationTypes.Channel, $"جلسه — {title}", null, true,
            actor.UserId, actor.DisplayName);
        conversation.Members.Add(new ConversationMember(conversation.Id, actor.UserId, actor.DisplayName,
            ConversationMemberRoles.Owner));
        foreach (var input in inputs)
            conversation.Members.Add(new ConversationMember(conversation.Id, input.UserId, users[input.UserId].Name,
                ConversationMemberRoles.Member));

        var roomName = request.ScheduledStartAtUtc.HasValue ? null : $"hadron-meeting-{Guid.NewGuid():N}";
        var meeting = new MeetingSession(conversation.Id, title, request.ScheduledStartAtUtc,
            request.ScheduledEndAtUtc, actor.UserId, actor.DisplayName, roomName,
            MeetingSupport.Policy(request.Policy), now);
        meeting.Participants.Add(MeetingParticipant.Internal(meeting.Id, actor.UserId, actor.DisplayName,
            MeetingRoles.Host, now));
        foreach (var input in inputs)
            meeting.Participants.Add(MeetingParticipant.Internal(meeting.Id, input.UserId, users[input.UserId].Name,
                input.Role, now));
        meeting.Events.Add(Event(meeting.Id, "CREATED", null, actor, now));
        if (meeting.Status == MeetingStatuses.Live)
            meeting.Events.Add(Event(meeting.Id, "STARTED", null, actor, now));
        db.CollaborationConversations.Add(conversation);
        db.CollaborationMeetingSessions.Add(meeting);
        await db.SaveChangesAsync(cancellationToken);
        await WriteAudit(meeting.Id, meeting.Status == MeetingStatuses.Live ? "CreatedInstant" : "Scheduled",
            actor, new Dictionary<string, object?>
            {
                ["conversationId"] = meeting.ConversationId,
                ["scheduledStartAtUtc"] = meeting.ScheduledStartAtUtc,
                ["participantCount"] = meeting.Participants.Count,
                ["recordingEnabled"] = false
            }, cancellationToken);
        if (meeting.Status == MeetingStatuses.Live)
            await NotifyMeetingChanged(meeting, cancellationToken);
        return MeetingSupport.Meeting(meeting);
    }

    public async Task<MeetingSessionResponse> Update(Guid meetingId, UpdateMeetingRequest request,
        CollaborationActor actor, CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var title = (request.Title ?? string.Empty).Trim();
        var inputs = request.Participants?.ToArray() ?? [];
        var errors = new Dictionary<string, string[]>();
        if (title.Length is < 2 or > 200)
            errors["title"] = ["عنوان جلسه باید بین ۲ تا ۲۰۰ نویسه باشد."];
        ValidateSchedule(request.ScheduledStartAtUtc, request.ScheduledEndAtUtc, now, errors);
        if (inputs.GroupBy(x => x.UserId).Any(x => x.Count() > 1) || inputs.Any(x => x.UserId == actor.UserId))
            errors["participants"] = ["هر شرکت‌کننده داخلی باید دقیقاً یک بار ثبت شود؛ میزبان در فهرست وارد نمی‌شود."];
        if (inputs.Any(x => x.Role is not (MeetingRoles.CoHost or MeetingRoles.Presenter or MeetingRoles.Attendee)))
            errors["participants.role"] = ["نقش باید COHOST، PRESENTER یا ATTENDEE باشد."];
        if (errors.Count > 0) throw new CollaborationValidationException(errors);

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        _ = await db.Database.SqlQueryRaw<int>(
                "SELECT 1 AS \"Value\" FROM collaboration.meeting_sessions WHERE \"Id\" = {0} FOR UPDATE",
                meetingId).SingleOrDefaultAsync(cancellationToken);
        var meeting = await db.CollaborationMeetingSessions.Include(x => x.Participants)
            .Include(x => x.Conversation).ThenInclude(x => x.Members)
            .SingleOrDefaultAsync(x => x.Id == meetingId, cancellationToken)
            ?? throw NotFound("MEETING_NOT_FOUND", "جلسه یافت نشد.");
        var organizer = EnsureOrganizer(meeting, actor);
        if (organizer.Role != MeetingRoles.Host)
            throw new UnauthorizedAccessException("Only the host can edit a scheduled meeting.");
        if (meeting.Status != MeetingStatuses.Scheduled)
            throw new CollaborationConflictException("MEETING_NOT_EDITABLE",
                "فقط جلسه زمان‌بندی‌شده و شروع‌نشده قابل ویرایش است.");
        var activeExternalCount = meeting.Participants.Count(x => x.IsExternal &&
            x.AdmissionStatus is not (MeetingAdmissionStatuses.Denied or MeetingAdmissionStatuses.Revoked));
        if (1 + inputs.Length + activeExternalCount > MaximumParticipants)
            throw new CollaborationConflictException("MEETING_CAPACITY_REACHED",
                $"ظرفیت {MaximumParticipants} نفره جلسه تکمیل است.");

        var requestedIds = inputs.Select(x => x.UserId).Distinct().ToArray();
        var users = await db.Users.AsNoTracking().Where(x => requestedIds.Contains(x.Id) && x.IsActive)
            .Select(x => new { x.Id, Name = (x.FirstName + " " + x.LastName).Trim() })
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        if (users.Count != requestedIds.Length)
            throw new CollaborationValidationException(new Dictionary<string, string[]>
                { ["participants"] = ["یک یا چند کاربر انتخاب‌شده فعال یا موجود نیستند."] });

        var requested = inputs.ToDictionary(x => x.UserId);
        foreach (var existing in meeting.Participants.Where(x => !x.IsExternal && x.Role != MeetingRoles.Host)
                     .ToArray())
        {
            if (requested.TryGetValue(existing.UserId!.Value, out var input))
            {
                if (existing.Role != input.Role) existing.ChangeRole(input.Role, existing.Version);
                continue;
            }
            var member = meeting.Conversation.Members.SingleOrDefault(x => x.UserId == existing.UserId);
            if (member is not null)
            {
                meeting.Conversation.Members.Remove(member);
                db.CollaborationConversationMembers.Remove(member);
            }
            meeting.Participants.Remove(existing);
            db.CollaborationMeetingParticipants.Remove(existing);
        }
        var existingIds = meeting.Participants.Where(x => x.UserId.HasValue).Select(x => x.UserId!.Value)
            .ToHashSet();
        foreach (var input in inputs.Where(x => !existingIds.Contains(x.UserId)))
        {
            var name = users[input.UserId].Name;
            var participant = MeetingParticipant.Internal(meeting.Id, input.UserId, name, input.Role, now);
            meeting.Participants.Add(participant);
            db.CollaborationMeetingParticipants.Add(participant);
            var member = new ConversationMember(meeting.ConversationId, input.UserId, name,
                ConversationMemberRoles.Member);
            meeting.Conversation.Members.Add(member);
            db.CollaborationConversationMembers.Add(member);
        }
        meeting.UpdateScheduled(title, request.ScheduledStartAtUtc, request.ScheduledEndAtUtc,
            MeetingSupport.Policy(request.Policy), request.ExpectedVersion);
        meeting.Conversation.Update($"جلسه — {title}", null, true, meeting.Conversation.Version);
        db.CollaborationMeetingEvents.Add(Event(meeting.Id, "UPDATED", null, actor, now));
        await Save(db, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await WriteAudit(meeting.Id, "Updated", actor, new Dictionary<string, object?>
        {
            ["scheduledStartAtUtc"] = meeting.ScheduledStartAtUtc,
            ["scheduledEndAtUtc"] = meeting.ScheduledEndAtUtc,
            ["internalParticipantCount"] = inputs.Length + 1,
            ["recordingEnabled"] = false
        }, cancellationToken);
        return MeetingSupport.Meeting(meeting);
    }

    public async Task<MeetingSessionResponse> Start(Guid meetingId, MeetingVersionRequest request,
        CollaborationActor actor, CancellationToken cancellationToken)
    {
        if (!media.Available) throw new CollaborationConflictException("CALL_PROVIDER_NOT_CONFIGURED",
            media.UnavailabilityReason ?? "سرویس تماس پیکربندی نشده است.");
        var now = UtcNow();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var meeting = await Load(db, meetingId, cancellationToken);
        EnsureOrganizer(meeting, actor);
        meeting.Start($"hadron-meeting-{Guid.NewGuid():N}", request.ExpectedVersion, now);
        db.CollaborationMeetingEvents.Add(Event(meeting.Id, "STARTED", null, actor, now));
        await Save(db, cancellationToken);
        await WriteAudit(meeting.Id, "Started", actor, null, cancellationToken);
        await NotifyMeetingChanged(meeting, cancellationToken);
        return MeetingSupport.Meeting(meeting);
    }

    public async Task<MeetingSessionResponse> Cancel(Guid meetingId, MeetingVersionRequest request,
        CollaborationActor actor, CancellationToken cancellationToken)
    {
        var now = UtcNow();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var meeting = await Load(db, meetingId, cancellationToken);
        EnsureOrganizer(meeting, actor);
        meeting.Cancel(request.ExpectedVersion, now);
        db.CollaborationMeetingEvents.Add(Event(meeting.Id, "CANCELLED", null, actor, now));
        await Save(db, cancellationToken);
        await WriteAudit(meeting.Id, "Cancelled", actor, null, cancellationToken);
        return MeetingSupport.Meeting(meeting);
    }

    public async Task<MeetingSessionResponse> SetLock(Guid meetingId, MeetingVersionRequest request, bool locked,
        CollaborationActor actor, CancellationToken cancellationToken)
    {
        var now = UtcNow();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var meeting = await Load(db, meetingId, cancellationToken);
        EnsureOrganizer(meeting, actor);
        var changed = meeting.IsLocked != locked;
        meeting.SetLocked(locked, request.ExpectedVersion);
        if (changed)
            db.CollaborationMeetingEvents.Add(Event(meeting.Id, locked ? "LOCKED" : "UNLOCKED", null, actor, now));
        await Save(db, cancellationToken);
        if (changed) await WriteAudit(meeting.Id, locked ? "Locked" : "Unlocked", actor, null, cancellationToken);
        return MeetingSupport.Meeting(meeting);
    }

    public async Task<MeetingSessionResponse> End(Guid meetingId, MeetingVersionRequest request,
        CollaborationActor actor, CancellationToken cancellationToken)
    {
        var now = UtcNow();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var meeting = await Load(db, meetingId, cancellationToken);
        EnsureOrganizer(meeting, actor);
        if (meeting.Version != request.ExpectedVersion)
            throw new CollaborationConflictException("MEETING_VERSION_CONFLICT",
                "جلسه توسط کاربر دیگری تغییر کرده است؛ اطلاعات را تازه‌سازی کنید.");
        if (meeting.Status != MeetingStatuses.Live || string.IsNullOrWhiteSpace(meeting.RoomName))
            throw new CollaborationConflictException("MEETING_NOT_LIVE", "فقط جلسه در حال برگزاری قابل پایان است.");
        var providerResult = await media.EndRoomAsync(meeting.RoomName, cancellationToken);
        if (!providerResult.Success)
        {
            db.CollaborationMeetingEvents.Add(Event(meeting.Id, "PROVIDER_END_FAILED", null, actor, now,
                providerResult.FailureCode));
            await db.SaveChangesAsync(cancellationToken);
            await WriteAudit(meeting.Id, "ProviderEndFailed", actor,
                new Dictionary<string, object?> { ["failureCode"] = providerResult.FailureCode }, cancellationToken);
            throw new CollaborationConflictException("MEETING_PROVIDER_END_FAILED",
                "پایان تماس در سرویس رسانه تأیید نشد؛ جلسه همچنان فعال است و باید دوباره تلاش شود.");
        }
        meeting.End(request.ExpectedVersion, now);
        db.CollaborationMeetingEvents.Add(Event(meeting.Id, "ENDED", null, actor, now,
            providerResult.AlreadyAbsent ? "PROVIDER_ROOM_ALREADY_ABSENT" : null));
        await Save(db, cancellationToken);
        await WriteAudit(meeting.Id, "Ended", actor,
            new Dictionary<string, object?> { ["providerRoomAlreadyAbsent"] = providerResult.AlreadyAbsent },
            cancellationToken);
        await NotifyMeetingChanged(meeting, cancellationToken);
        return MeetingSupport.Meeting(meeting);
    }

    public async Task<MeetingSessionResponse> ChangeRole(Guid meetingId, Guid participantId,
        MeetingParticipantRoleRequest request, CollaborationActor actor, CancellationToken cancellationToken)
    {
        var role = (request.Role ?? string.Empty).Trim().ToUpperInvariant();
        if (role is not (MeetingRoles.CoHost or MeetingRoles.Presenter or MeetingRoles.Attendee))
            throw new CollaborationValidationException(new Dictionary<string, string[]>
                { ["role"] = ["نقش مقصد باید COHOST، PRESENTER یا ATTENDEE باشد."] });
        var now = UtcNow();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var meeting = await Load(db, meetingId, cancellationToken);
        var organizer = EnsureOrganizer(meeting, actor);
        EnsureMutable(meeting);
        var participant = meeting.Participants.SingleOrDefault(x => x.Id == participantId)
            ?? throw NotFound("MEETING_PARTICIPANT_NOT_FOUND", "شرکت‌کننده جلسه یافت نشد.");
        if (participant.IsExternal)
            throw new CollaborationConflictException("MEETING_GUEST_ROLE_FIXED", "نقش مهمان بیرونی قابل تغییر نیست.");
        if ((role == MeetingRoles.CoHost || participant.Role == MeetingRoles.CoHost) && organizer.Role != MeetingRoles.Host)
            throw new UnauthorizedAccessException("Only the host can change co-host authority.");
        var previous = participant.Role;
        if (previous == role)
        {
            if (participant.Version != request.ExpectedVersion)
                throw new CollaborationConflictException("MEETING_PARTICIPANT_VERSION_CONFLICT",
                    "نقش شرکت‌کننده تغییر کرده است؛ اطلاعات را تازه‌سازی کنید.");
            return MeetingSupport.Meeting(meeting);
        }

        if (meeting.Status == MeetingStatuses.Live && participant.AdmissionStatus == MeetingAdmissionStatuses.Admitted &&
            !string.IsNullOrWhiteSpace(meeting.RoomName))
        {
            var identity = ParticipantIdentity(participant);
            var providerResult = media.Available
                ? await media.RemoveParticipantAsync(meeting.RoomName, identity, cancellationToken)
                : MeetingParticipantRemoveResult.Failed("PROVIDER_NOT_CONFIGURED");
            if (!providerResult.Success)
            {
                db.CollaborationMeetingEvents.Add(Event(meeting.Id, "PROVIDER_ROLE_REFRESH_FAILED", participant.Id,
                    actor, now, providerResult.FailureCode));
                await db.SaveChangesAsync(cancellationToken);
                await WriteAudit(meeting.Id, "ProviderRoleRefreshFailed", actor,
                    new Dictionary<string, object?>
                    {
                        ["participantId"] = participant.Id,
                        ["requestedRole"] = role,
                        ["failureCode"] = providerResult.FailureCode
                    }, cancellationToken);
                throw new CollaborationConflictException("MEETING_PROVIDER_ROLE_REFRESH_FAILED",
                    "اعمال نقش جدید در سرویس تماس تأیید نشد؛ نقش قبلی حفظ شد و باید دوباره تلاش شود.");
            }
            db.CollaborationMeetingEvents.Add(Event(meeting.Id, "PROVIDER_ROLE_REFRESHED", participant.Id,
                actor, now, providerResult.AlreadyAbsent ? "PROVIDER_PARTICIPANT_ALREADY_ABSENT" : null));
        }

        participant.ChangeRole(role, request.ExpectedVersion);
        db.CollaborationMeetingEvents.Add(Event(meeting.Id, "ROLE_CHANGED", participant.Id, actor, now,
            $"{previous}->{role}"));
        await Save(db, cancellationToken);
        await WriteAudit(meeting.Id, "ParticipantRoleChanged", actor, new Dictionary<string, object?>
        {
            ["participantId"] = participant.Id, ["previousRole"] = previous, ["role"] = role
        }, cancellationToken);
        return MeetingSupport.Meeting(meeting);
    }

    public Task<MeetingSessionResponse> Admit(Guid meetingId, Guid participantId, CollaborationActor actor,
        CancellationToken cancellationToken) => ChangeAdmission(meetingId, participantId, "ADMIT", actor,
        cancellationToken);

    public Task<MeetingSessionResponse> Deny(Guid meetingId, Guid participantId, CollaborationActor actor,
        CancellationToken cancellationToken) => ChangeAdmission(meetingId, participantId, "DENY", actor,
        cancellationToken);

    public Task<MeetingSessionResponse> Revoke(Guid meetingId, Guid participantId, CollaborationActor actor,
        CancellationToken cancellationToken) => ChangeAdmission(meetingId, participantId, "REVOKE", actor,
        cancellationToken);

    public async Task<MeetingGuestInviteResponse> CreateGuestInvite(Guid meetingId,
        CreateMeetingGuestInviteRequest request, CollaborationActor actor, CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var name = (request.GuestDisplayName ?? string.Empty).Trim();
        var errors = new Dictionary<string, string[]>();
        if (name.Length is < 2 or > 200) errors["guestDisplayName"] = ["نام مهمان باید بین ۲ تا ۲۰۰ نویسه باشد."];
        if (request.ExpiresAtUtc.Kind != DateTimeKind.Utc || request.ExpiresAtUtc <= now ||
            request.ExpiresAtUtc > now.AddDays(7))
            errors["expiresAtUtc"] = ["انقضای دعوت‌نامه باید UTC، در آینده و حداکثر تا ۷ روز بعد باشد."];
        if (errors.Count > 0) throw new CollaborationValidationException(errors);

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        _ = await db.Database.SqlQueryRaw<int>(
                "SELECT 1 AS \"Value\" FROM collaboration.meeting_sessions WHERE \"Id\" = {0} FOR UPDATE",
                meetingId).SingleOrDefaultAsync(cancellationToken);
        var meeting = await Load(db, meetingId, cancellationToken);
        EnsureOrganizer(meeting, actor);
        EnsureOpenForExternal(meeting);
        var occupied = meeting.Participants.Count(x =>
            x.AdmissionStatus is not (MeetingAdmissionStatuses.Denied or MeetingAdmissionStatuses.Revoked));
        if (occupied >= MaximumParticipants)
            throw new CollaborationConflictException("MEETING_CAPACITY_REACHED",
                $"ظرفیت {MaximumParticipants} نفره جلسه تکمیل است.");
        var token = MeetingSupport.NewOpaqueToken();
        var participant = MeetingParticipant.Guest(meeting.Id, name, now);
        var invite = new MeetingGuestInvite(meeting.Id, participant.Id, MeetingSupport.HashToken(token),
            request.ExpiresAtUtc, actor.UserId, now);
        meeting.Participants.Add(participant);
        db.CollaborationMeetingParticipants.Add(participant);
        db.CollaborationMeetingGuestInvites.Add(invite);
        db.CollaborationMeetingEvents.Add(Event(meeting.Id, "GUEST_INVITED", participant.Id, actor, now));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await WriteAudit(meeting.Id, "GuestInvited", actor, new Dictionary<string, object?>
        {
            ["inviteId"] = invite.Id, ["participantId"] = participant.Id,
            ["expiresAtUtc"] = invite.ExpiresAtUtc
        }, cancellationToken);
        return new(invite.Id, meeting.Id, MeetingSupport.Participant(participant), token,
            invite.ExpiresAtUtc, invite.CreatedAtUtc);
    }

    public async Task<MeetingJoinResponse> Join(Guid meetingId, CollaborationActor actor,
        CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var meeting = await Load(db, meetingId, cancellationToken);
        var participant = EnsureInternalParticipant(meeting, actor);
        EnsureCanJoin(meeting, participant);
        return JoinResponse(meeting, participant, null);
    }

    public async Task<MeetingAttendanceIntervalResponse> Connected(Guid meetingId, CollaborationActor actor,
        CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var meeting = await Load(db, meetingId, cancellationToken);
        var participant = EnsureInternalParticipant(meeting, actor);
        return await RecordConnected(db, meeting, participant, actor.UserId, actor.DisplayName,
            cancellationToken);
    }

    public async Task<MeetingAttendanceIntervalResponse?> Disconnected(Guid meetingId, CollaborationActor actor,
        CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var meeting = await Load(db, meetingId, cancellationToken);
        var participant = EnsureInternalParticipant(meeting, actor);
        return await RecordDisconnected(db, meeting, participant, actor.UserId, actor.DisplayName,
            cancellationToken);
    }

    public async Task<MeetingReportResponse> Report(Guid meetingId, CollaborationActor actor,
        CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var meeting = await db.CollaborationMeetingSessions.AsNoTracking()
            .Include(x => x.Participants).ThenInclude(x => x.AttendanceIntervals)
            .Include(x => x.Events).SingleOrDefaultAsync(x => x.Id == meetingId, cancellationToken)
            ?? throw NotFound("MEETING_NOT_FOUND", "جلسه یافت نشد.");
        EnsureVisible(meeting, actor);
        var now = UtcNow();
        var rows = meeting.Participants.OrderBy(x => MeetingSupport.RoleOrder(x.Role)).ThenBy(x => x.DisplayName)
            .Select(participant =>
            {
                var intervals = participant.AttendanceIntervals.OrderBy(x => x.ConnectedAtUtc).ToArray();
                var total = intervals.Sum(interval => Math.Max(0L, (long)((interval.DisconnectedAtUtc ??
                    meeting.EndedAtUtc ?? now) - interval.ConnectedAtUtc).TotalSeconds));
                return new MeetingParticipantReportResponse(MeetingSupport.Participant(participant),
                    intervals.Select(Attendance).ToArray(), total);
            }).ToArray();
        var events = meeting.Events.OrderBy(x => x.OccurredAtUtc).Select(x => new MeetingEventResponse(
            x.Id, x.EventType, x.ParticipantId, x.ActorUserId, x.ActorDisplayName, x.Source,
            x.OccurredAtUtc, x.Detail)).ToArray();
        return new(MeetingSupport.Meeting(meeting), rows, events, now);
    }

    internal async Task<MeetingAttendanceIntervalResponse> RecordConnected(HadronDbContext db,
        MeetingSession meeting, MeetingParticipant participant, Guid? actorUserId, string actorDisplayName,
        CancellationToken cancellationToken)
    {
        EnsureCanJoin(meeting, participant);
        var existing = await db.CollaborationMeetingAttendanceIntervals.SingleOrDefaultAsync(x =>
            x.ParticipantId == participant.Id && x.DisconnectedAtUtc == null, cancellationToken);
        if (existing is not null) return Attendance(existing);
        var now = UtcNow();
        var interval = new MeetingAttendanceInterval(meeting.Id, participant.Id, now, MeetingEventSources.Client);
        db.CollaborationMeetingAttendanceIntervals.Add(interval);
        db.CollaborationMeetingEvents.Add(new MeetingEvent(meeting.Id, "CONNECTED", participant.Id, actorUserId,
            actorDisplayName, MeetingEventSources.Client, now));
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            existing = await db.CollaborationMeetingAttendanceIntervals.AsNoTracking().SingleOrDefaultAsync(x =>
                x.ParticipantId == participant.Id && x.DisconnectedAtUtc == null, cancellationToken);
            if (existing is null) throw;
            return Attendance(existing);
        }
        await WriteAudit(meeting.Id, "ParticipantConnected", actorUserId?.ToString(), "CLIENT",
            new Dictionary<string, object?> { ["participantId"] = participant.Id, ["source"] = "CLIENT" },
            cancellationToken);
        return Attendance(interval);
    }

    internal async Task<MeetingAttendanceIntervalResponse?> RecordDisconnected(HadronDbContext db,
        MeetingSession meeting, MeetingParticipant participant, Guid? actorUserId, string actorDisplayName,
        CancellationToken cancellationToken)
    {
        var interval = await db.CollaborationMeetingAttendanceIntervals.SingleOrDefaultAsync(x =>
            x.ParticipantId == participant.Id && x.DisconnectedAtUtc == null, cancellationToken);
        if (interval is null) return null;
        var now = UtcNow();
        interval.Disconnect(now);
        db.CollaborationMeetingEvents.Add(new MeetingEvent(meeting.Id, "DISCONNECTED", participant.Id, actorUserId,
            actorDisplayName, MeetingEventSources.Client, now));
        await db.SaveChangesAsync(cancellationToken);
        await WriteAudit(meeting.Id, "ParticipantDisconnected", actorUserId?.ToString(), "CLIENT",
            new Dictionary<string, object?> { ["participantId"] = participant.Id, ["source"] = "CLIENT" },
            cancellationToken);
        return Attendance(interval);
    }

    internal static MeetingParticipant EnsureGuestParticipant(MeetingSession meeting, Guid participantId)
    {
        var participant = meeting.Participants.SingleOrDefault(x => x.Id == participantId && x.IsExternal)
            ?? throw new MeetingGuestAccessException();
        return participant;
    }

    internal MeetingJoinResponse JoinResponse(MeetingSession meeting, MeetingParticipant participant,
        DateTime? ticketExpiry)
    {
        var (token, capabilities) = CreateJoinAccess(meeting, participant, ticketExpiry);
        return new(MeetingSupport.Meeting(meeting), MeetingSupport.Participant(participant), media.ServerUrl,
            token, capabilities);
    }

    internal MeetingGuestJoinResponse GuestJoinResponse(MeetingSession meeting, MeetingParticipant participant,
        DateTime? ticketExpiry)
    {
        var (token, capabilities) = CreateJoinAccess(meeting, participant, ticketExpiry);
        return new(MeetingSupport.GuestRoom(meeting), MeetingSupport.GuestParticipant(participant), media.ServerUrl,
            token, capabilities);
    }

    private (string Token, MeetingJoinCapabilities Capabilities) CreateJoinAccess(MeetingSession meeting,
        MeetingParticipant participant, DateTime? ticketExpiry)
    {
        if (!media.Available) throw new CollaborationConflictException("CALL_PROVIDER_NOT_CONFIGURED",
            media.UnavailabilityReason ?? "سرویس تماس پیکربندی نشده است.");
        var capabilities = MeetingSupport.Capabilities(meeting, participant);
        var now = UtcNow();
        var expiry = ticketExpiry ?? meeting.ScheduledEndAtUtc?.AddHours(1) ?? now.AddHours(2);
        if (expiry <= now) expiry = now.AddMinutes(15);
        var token = media.CreateJoinToken(new MeetingMediaTokenDescriptor(meeting.RoomName!,
            ParticipantIdentity(participant), participant.DisplayName, participant.Id,
            new MeetingMediaCapabilities(capabilities.Microphone, capabilities.Camera,
                capabilities.ScreenShare, capabilities.Chat || capabilities.EphemeralWhiteboard), expiry));
        return (token, capabilities);
    }

    internal static void EnsureCanJoin(MeetingSession meeting, MeetingParticipant participant)
    {
        if (meeting.Status != MeetingStatuses.Live || string.IsNullOrWhiteSpace(meeting.RoomName))
            throw new CollaborationConflictException("MEETING_NOT_LIVE", "جلسه هنوز شروع نشده یا پایان یافته است.");
        if (participant.AdmissionStatus == MeetingAdmissionStatuses.Waiting)
            throw new CollaborationConflictException("MEETING_GUEST_WAITING", "مهمان در انتظار پذیرش میزبان است.");
        if (participant.AdmissionStatus != MeetingAdmissionStatuses.Admitted)
            throw new MeetingGuestAccessException();
    }

    internal static void EnsureOpenForExternal(MeetingSession meeting)
    {
        if (meeting.Status is MeetingStatuses.Ended or MeetingStatuses.Cancelled)
            throw new CollaborationConflictException("MEETING_CLOSED", "جلسه پایان یافته یا لغو شده است.");
        if (meeting.IsLocked)
            throw new CollaborationConflictException("MEETING_LOCKED", "جلسه قفل است و مهمان جدید پذیرفته نمی‌شود.");
    }

    private static void EnsureMutable(MeetingSession meeting)
    {
        if (meeting.Status is MeetingStatuses.Ended or MeetingStatuses.Cancelled)
            throw new CollaborationConflictException("MEETING_CLOSED",
                "جلسه پایان یافته یا لغو شده است و قابل ویرایش نیست.");
    }

    private async Task<MeetingSessionResponse> ChangeAdmission(Guid meetingId, Guid participantId, string action,
        CollaborationActor actor, CancellationToken cancellationToken)
    {
        var now = UtcNow();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var meeting = await Load(db, meetingId, cancellationToken);
        var organizer = EnsureOrganizer(meeting, actor);
        EnsureMutable(meeting);
        var participant = meeting.Participants.SingleOrDefault(x => x.Id == participantId)
            ?? throw NotFound("MEETING_PARTICIPANT_NOT_FOUND", "شرکت‌کننده جلسه یافت نشد.");
        if (participant.Role == MeetingRoles.CoHost && organizer.Role != MeetingRoles.Host)
            throw new UnauthorizedAccessException("Only the host can revoke co-host authority.");
        var removeFromProvider = false;
        switch (action)
        {
            case "ADMIT":
                if (!participant.IsExternal)
                    throw new CollaborationConflictException("MEETING_INTERNAL_AUTO_ADMITTED", "کاربر داخلی از ابتدا پذیرفته است.");
                EnsureOpenForExternal(meeting);
                participant.Admit(now);
                break;
            case "DENY":
                if (!participant.IsExternal)
                    throw new CollaborationConflictException("MEETING_INTERNAL_CANNOT_BE_DENIED", "کاربر داخلی قابل رد کردن نیست.");
                participant.Deny(now);
                break;
            case "REVOKE":
                participant.Revoke(now);
                removeFromProvider = meeting.Status == MeetingStatuses.Live &&
                    !string.IsNullOrWhiteSpace(meeting.RoomName);
                var invite = await db.CollaborationMeetingGuestInvites.SingleOrDefaultAsync(x =>
                    x.ParticipantId == participant.Id, cancellationToken);
                invite?.Revoke(now);
                var tickets = await db.CollaborationMeetingGuestAccessTickets.Where(x =>
                    x.ParticipantId == participant.Id && x.RevokedAtUtc == null).ToArrayAsync(cancellationToken);
                foreach (var ticket in tickets) ticket.Revoke(now);
                break;
            default: throw new InvalidOperationException("Unknown admission action.");
        }
        db.CollaborationMeetingEvents.Add(Event(meeting.Id, action switch
        {
            "ADMIT" => "ADMITTED", "DENY" => "DENIED", _ => "REVOKED"
        }, participant.Id, actor, now));
        await Save(db, cancellationToken);
        await WriteAudit(meeting.Id, action switch
        {
            "ADMIT" => "ParticipantAdmitted", "DENY" => "ParticipantDenied", _ => "ParticipantRevoked"
        }, actor, new Dictionary<string, object?> { ["participantId"] = participant.Id }, cancellationToken);

        if (removeFromProvider)
        {
            var identity = ParticipantIdentity(participant);
            var providerResult = media.Available
                ? await media.RemoveParticipantAsync(meeting.RoomName!, identity, cancellationToken)
                : MeetingParticipantRemoveResult.Failed("PROVIDER_NOT_CONFIGURED");
            if (!providerResult.Success)
            {
                db.CollaborationMeetingEvents.Add(Event(meeting.Id, "PROVIDER_PARTICIPANT_REMOVE_FAILED", participant.Id,
                    actor, UtcNow(), providerResult.FailureCode));
                await db.SaveChangesAsync(cancellationToken);
                await WriteAudit(meeting.Id, "ProviderParticipantRemoveFailed", actor,
                    new Dictionary<string, object?>
                    {
                        ["participantId"] = participant.Id,
                        ["failureCode"] = providerResult.FailureCode,
                        ["accessRevoked"] = true
                    }, cancellationToken);
                throw new CollaborationConflictException("MEETING_PROVIDER_REMOVE_FAILED",
                    "دسترسی شرکت‌کننده لغو شد، اما خروج او از سرویس تماس تأیید نشد؛ دوباره تلاش کنید.");
            }

            db.CollaborationMeetingEvents.Add(Event(meeting.Id, "PROVIDER_PARTICIPANT_REMOVED", participant.Id, actor,
                UtcNow(), providerResult.AlreadyAbsent ? "PROVIDER_PARTICIPANT_ALREADY_ABSENT" : null));
            await db.SaveChangesAsync(cancellationToken);
            await WriteAudit(meeting.Id, "ProviderParticipantRemoved", actor,
                new Dictionary<string, object?>
                {
                    ["participantId"] = participant.Id,
                    ["providerParticipantAlreadyAbsent"] = providerResult.AlreadyAbsent
                }, cancellationToken);
        }
        return MeetingSupport.Meeting(meeting);
    }

    private static void ValidateSchedule(DateTime? start, DateTime? end, DateTime now,
        IDictionary<string, string[]> errors)
    {
        if (start.HasValue != end.HasValue)
        {
            errors["schedule"] = ["برای جلسه زمان‌بندی‌شده، زمان شروع و پایان هر دو الزامی است."];
            return;
        }
        if (!start.HasValue) return;
        if (start.Value.Kind != DateTimeKind.Utc || end!.Value.Kind != DateTimeKind.Utc)
            errors["schedule"] = ["زمان شروع و پایان باید به‌صورت UTC ارسال شود."];
        else if (start.Value < now || end.Value <= start.Value)
            errors["schedule"] = ["شروع باید در آینده و پایان بعد از شروع باشد."];
    }

    private static MeetingEvent Event(Guid meetingId, string type, Guid? participantId,
        CollaborationActor actor, DateTime now, string? detail = null) => new(meetingId, type, participantId,
        actor.UserId, actor.DisplayName, MeetingEventSources.Ui, now, detail);

    private static async Task<MeetingSession> Load(HadronDbContext db, Guid id, CancellationToken cancellationToken) =>
        await db.CollaborationMeetingSessions.Include(x => x.Participants)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
        ?? throw NotFound("MEETING_NOT_FOUND", "جلسه یافت نشد.");

    private static void EnsureVisible(MeetingSession meeting, CollaborationActor actor)
    {
        if (!actor.IsSystemAdministrator && meeting.Participants.All(x => x.UserId != actor.UserId))
            throw NotFound("MEETING_NOT_FOUND", "جلسه یافت نشد.");
    }

    private static MeetingParticipant EnsureInternalParticipant(MeetingSession meeting, CollaborationActor actor) =>
        meeting.Participants.SingleOrDefault(x => x.UserId == actor.UserId)
        ?? throw NotFound("MEETING_NOT_FOUND", "جلسه یافت نشد.");

    private static MeetingParticipant EnsureOrganizer(MeetingSession meeting, CollaborationActor actor)
    {
        var participant = EnsureInternalParticipant(meeting, actor);
        if (participant.AdmissionStatus != MeetingAdmissionStatuses.Admitted ||
            participant.Role is not (MeetingRoles.Host or MeetingRoles.CoHost))
            throw new UnauthorizedAccessException("Meeting organizer authority is required.");
        return participant;
    }

    private static CollaborationNotFoundException NotFound(string code, string message) => new(code, message);

    private static string ParticipantIdentity(MeetingParticipant participant) =>
        $"participant:{participant.Id:N}";

    private static string EscapeLike(string value) => value.Replace("\\", "\\\\")
        .Replace("%", "\\%").Replace("_", "\\_");

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private async Task NotifyMeetingChanged(MeetingSession meeting, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var userId in meeting.Participants.Where(participant => participant.UserId.HasValue)
                         .Select(participant => participant.UserId!.Value).Distinct())
                await hub.Clients.Group(CollaborationService.UserGroup(userId)).SendAsync(
                    "notificationUpdated",
                    new { Kind = "CALL", EntityId = meeting.Id, ConversationId = meeting.ConversationId },
                    cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Meeting {MeetingId} persisted but its global communication notification could not be delivered.",
                meeting.Id);
        }
    }

    private static MeetingAttendanceIntervalResponse Attendance(MeetingAttendanceInterval value) => new(
        value.Id, value.ConnectedAtUtc, value.DisconnectedAtUtc, value.Source);

    private static async Task Save(HadronDbContext db, CancellationToken cancellationToken)
    {
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            throw new CollaborationConflictException("MEETING_VERSION_CONFLICT",
                "جلسه توسط کاربر دیگری تغییر کرده است؛ اطلاعات را تازه‌سازی کنید.");
        }
    }

    private Task WriteAudit(Guid id, string action, CollaborationActor actor,
        IReadOnlyDictionary<string, object?>? values, CancellationToken cancellationToken) =>
        WriteAudit(id, action, actor.UserId.ToString(), "UI", values, cancellationToken);

    private async Task WriteAudit(Guid id, string action, string? actorId, string source,
        IReadOnlyDictionary<string, object?>? values, CancellationToken cancellationToken)
    {
        try
        {
            await audit.WriteAsync(new AuditEvent("MeetingSession", id, action, UtcNow(), actorId,
                AuditEventContext.CorrelationId(httpContextAccessor.HttpContext), null, values, null, source),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "Post-commit meeting audit failed for {MeetingId}/{Action}.", id, action);
        }
    }
}
