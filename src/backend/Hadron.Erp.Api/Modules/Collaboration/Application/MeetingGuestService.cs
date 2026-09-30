using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Collaboration.Domain;
using Hadron.Erp.Api.Platform.Audit;
using Microsoft.EntityFrameworkCore;

namespace Hadron.Erp.Api.Modules.Collaboration.Application;

public sealed class MeetingGuestService(
    IDbContextFactory<HadronDbContext> factory,
    MeetingService meetings,
    IAuditEventSink audit,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider,
    ILogger<MeetingGuestService> logger)
{
    public async Task<MeetingGuestPreviewResponse> Preview(MeetingGuestTokenRequest request,
        CancellationToken cancellationToken)
    {
        var hash = RequiredHash(request.InviteToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var invite = await db.CollaborationMeetingGuestInvites.AsNoTracking().Include(x => x.MeetingSession)
            .SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken)
            ?? throw new MeetingGuestAccessException();
        EnsureInviteValid(invite, UtcNow());
        return MeetingSupport.GuestPreview(invite.MeetingSession);
    }

    public async Task<MeetingGuestRedeemResponse> Redeem(MeetingGuestTokenRequest request,
        CancellationToken cancellationToken)
    {
        var hash = RequiredHash(request.InviteToken);
        var now = UtcNow();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var invite = await db.CollaborationMeetingGuestInvites.Include(x => x.MeetingSession)
            .Include(x => x.Participant)
            .SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken)
            ?? throw new MeetingGuestAccessException();
        EnsureInviteValid(invite, now);
        MeetingService.EnsureOpenForExternal(invite.MeetingSession);
        var ticketValue = MeetingSupport.NewOpaqueToken();
        var ticketExpiry = now.AddHours(2);
        if (invite.MeetingSession.ScheduledEndAtUtc is { } scheduledEnd && scheduledEnd.AddHours(1) < ticketExpiry)
            ticketExpiry = scheduledEnd.AddHours(1);
        if (ticketExpiry <= now) throw new MeetingGuestAccessException();
        invite.Redeem(now);
        invite.Participant.EnterWaitingRoom(now);
        var ticket = new MeetingGuestAccessTicket(invite.MeetingSessionId, invite.ParticipantId,
            MeetingSupport.HashToken(ticketValue), ticketExpiry, now);
        db.CollaborationMeetingGuestAccessTickets.Add(ticket);
        db.CollaborationMeetingEvents.Add(new MeetingEvent(invite.MeetingSessionId, "GUEST_WAITING",
            invite.ParticipantId, null, invite.Participant.DisplayName, MeetingEventSources.Ui, now));
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new MeetingGuestAccessException(); }
        catch (DbUpdateException) { throw new MeetingGuestAccessException(); }
        await WriteAudit(invite.MeetingSessionId, "GuestInviteRedeemed", invite.ParticipantId,
            cancellationToken);
        return new(ticketValue, ticketExpiry, MeetingSupport.GuestPreview(invite.MeetingSession),
            MeetingSupport.GuestParticipant(invite.Participant));
    }

    public async Task<MeetingGuestStatusResponse> Status(MeetingGuestTicketRequest request,
        CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var ticket = await LoadTicket(db, request.AccessTicket, cancellationToken,
            includeMeetingParticipants: false);
        var meeting = ticket.MeetingSession;
        var participant = ticket.Participant;
        return new(MeetingSupport.GuestPreview(meeting), MeetingSupport.GuestParticipant(participant),
            meeting.Status == MeetingStatuses.Live &&
            participant.AdmissionStatus == MeetingAdmissionStatuses.Admitted);
    }

    public async Task<MeetingGuestJoinResponse> Join(MeetingGuestTicketRequest request,
        CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var ticket = await LoadTicket(db, request.AccessTicket, cancellationToken,
            includeMeetingParticipants: true);
        MeetingService.EnsureCanJoin(ticket.MeetingSession, ticket.Participant);
        return meetings.GuestJoinResponse(ticket.MeetingSession, ticket.Participant, ticket.ExpiresAtUtc);
    }

    public async Task<MeetingAttendanceIntervalResponse> Connected(MeetingGuestTicketRequest request,
        CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var ticket = await LoadTicket(db, request.AccessTicket, cancellationToken,
            includeMeetingParticipants: false);
        return await meetings.RecordConnected(db, ticket.MeetingSession, ticket.Participant, null,
            ticket.Participant.DisplayName, cancellationToken);
    }

    public async Task<MeetingAttendanceIntervalResponse?> Disconnected(MeetingGuestTicketRequest request,
        CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var ticket = await LoadTicket(db, request.AccessTicket, cancellationToken,
            includeMeetingParticipants: false);
        return await meetings.RecordDisconnected(db, ticket.MeetingSession, ticket.Participant, null,
            ticket.Participant.DisplayName, cancellationToken);
    }

    private async Task<MeetingGuestAccessTicket> LoadTicket(HadronDbContext db, string token,
        CancellationToken cancellationToken, bool includeMeetingParticipants)
    {
        var hash = RequiredHash(token);
        IQueryable<MeetingGuestAccessTicket> query = db.CollaborationMeetingGuestAccessTickets
            .AsNoTracking()
            .Include(x => x.MeetingSession)
            .Include(x => x.Participant);
        if (includeMeetingParticipants)
            query = query.Include(x => x.MeetingSession).ThenInclude(x => x.Participants);
        var ticket = await query.SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken)
            ?? throw new MeetingGuestAccessException();
        var now = UtcNow();
        if (ticket.RevokedAtUtc.HasValue || ticket.ExpiresAtUtc <= now ||
            ticket.Participant.AdmissionStatus == MeetingAdmissionStatuses.Revoked)
            throw new MeetingGuestAccessException();
        return ticket;
    }

    private static void EnsureInviteValid(MeetingGuestInvite invite, DateTime now)
    {
        if (invite.RevokedAtUtc.HasValue || invite.RedeemedAtUtc.HasValue || invite.ExpiresAtUtc <= now ||
            invite.MeetingSession.Status is MeetingStatuses.Ended or MeetingStatuses.Cancelled)
            throw new MeetingGuestAccessException();
    }

    private static string RequiredHash(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length is < 40 or > 200)
            throw new MeetingGuestAccessException();
        return MeetingSupport.HashToken(token);
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private async Task WriteAudit(Guid meetingId, string action, Guid participantId,
        CancellationToken cancellationToken)
    {
        try
        {
            await audit.WriteAsync(new AuditEvent("MeetingSession", meetingId, action, UtcNow(), null,
                AuditEventContext.CorrelationId(httpContextAccessor.HttpContext), null,
                new Dictionary<string, object?> { ["participantId"] = participantId }, null, "GUEST"),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "Post-commit guest meeting audit failed for {MeetingId}/{Action}.",
                meetingId, action);
        }
    }
}
