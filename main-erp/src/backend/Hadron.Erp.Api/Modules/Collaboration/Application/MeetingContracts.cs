namespace Hadron.Erp.Api.Modules.Collaboration.Application;

public sealed record MeetingPolicyRequest(
    bool AttendeeMicrophone,
    bool AttendeeCamera,
    bool AttendeeScreenShare,
    bool GuestMicrophone,
    bool GuestCamera,
    bool GuestScreenShare,
    bool ChatEnabled,
    bool FileSharingEnabled,
    bool EphemeralWhiteboardEnabled);

public sealed record MeetingPolicyResponse(
    bool AttendeeMicrophone,
    bool AttendeeCamera,
    bool AttendeeScreenShare,
    bool GuestMicrophone,
    bool GuestCamera,
    bool GuestScreenShare,
    bool ChatEnabled,
    bool FileSharingEnabled,
    bool EphemeralWhiteboardEnabled,
    bool RecordingEnabled);

public sealed record MeetingInternalParticipantInput(Guid UserId, string Role);

public sealed record CreateMeetingRequest(
    string Title,
    DateTime? ScheduledStartAtUtc,
    DateTime? ScheduledEndAtUtc,
    IReadOnlyList<MeetingInternalParticipantInput>? Participants,
    MeetingPolicyRequest? Policy);

public sealed record UpdateMeetingRequest(
    string Title,
    DateTime ScheduledStartAtUtc,
    DateTime ScheduledEndAtUtc,
    IReadOnlyList<MeetingInternalParticipantInput>? Participants,
    MeetingPolicyRequest? Policy,
    long ExpectedVersion);

public sealed record MeetingVersionRequest(long ExpectedVersion);
public sealed record MeetingParticipantRoleRequest(string Role, long ExpectedVersion);
public sealed record CreateMeetingGuestInviteRequest(string GuestDisplayName, DateTime ExpiresAtUtc);
public sealed record MeetingGuestTokenRequest(string InviteToken);
public sealed record MeetingGuestTicketRequest(string AccessTicket);

public sealed record ResolveMeetingJalaliDateTimeRequest(string JalaliDate, string LocalTime);
public sealed record ResolveMeetingJalaliDateTimeResponse(
    string JalaliDate,
    string LocalTime,
    DateOnly GregorianDate,
    DateTime UtcDateTimeUtc,
    string TimeZoneId);

public sealed record MeetingParticipantResponse(
    Guid Id,
    Guid? UserId,
    string DisplayName,
    string Role,
    string AdmissionStatus,
    DateTime CreatedAtUtc,
    DateTime? AdmittedAtUtc,
    DateTime? DeniedAtUtc,
    DateTime? RevokedAtUtc,
    long Version);

public sealed record MeetingSummaryResponse(
    Guid Id,
    Guid ConversationId,
    string Title,
    string Status,
    bool IsLocked,
    DateTime? ScheduledStartAtUtc,
    DateTime? ScheduledEndAtUtc,
    DateTime? StartedAtUtc,
    DateTime? EndedAtUtc,
    int ParticipantCount,
    int WaitingCount,
    long Version);

public sealed record MeetingSessionResponse(
    Guid Id,
    Guid ConversationId,
    string Title,
    string Status,
    bool IsLocked,
    string Provider,
    DateTime? ScheduledStartAtUtc,
    DateTime? ScheduledEndAtUtc,
    DateTime? StartedAtUtc,
    DateTime? EndedAtUtc,
    DateTime? CancelledAtUtc,
    Guid CreatedByUserId,
    string CreatedByDisplayName,
    DateTime CreatedAtUtc,
    long Version,
    MeetingPolicyResponse Policy,
    IReadOnlyList<MeetingParticipantResponse> Participants);

public sealed record MeetingJoinCapabilities(
    bool Microphone,
    bool Camera,
    bool ScreenShare,
    bool Chat,
    bool FileSharing,
    bool EphemeralWhiteboard,
    bool Recording);

public sealed record MeetingJoinResponse(
    MeetingSessionResponse Meeting,
    MeetingParticipantResponse Participant,
    string ServerUrl,
    string AccessToken,
    MeetingJoinCapabilities Capabilities);

public sealed record MeetingGuestParticipantResponse(
    Guid Id,
    string DisplayName,
    string Role,
    string AdmissionStatus);

public sealed record MeetingGuestRoomResponse(
    string Title,
    IReadOnlyList<MeetingGuestParticipantResponse> Participants);

public sealed record MeetingGuestJoinResponse(
    MeetingGuestRoomResponse Meeting,
    MeetingGuestParticipantResponse Participant,
    string ServerUrl,
    string AccessToken,
    MeetingJoinCapabilities Capabilities);

public sealed record MeetingGuestInviteResponse(
    Guid Id,
    Guid MeetingId,
    MeetingParticipantResponse Participant,
    string InviteToken,
    DateTime ExpiresAtUtc,
    DateTime CreatedAtUtc);

public sealed record MeetingGuestPreviewResponse(
    Guid MeetingId,
    string Title,
    string Status,
    bool IsLocked,
    DateTime? ScheduledStartAtUtc,
    DateTime? ScheduledEndAtUtc,
    MeetingPolicyResponse Policy);

public sealed record MeetingGuestRedeemResponse(
    string AccessTicket,
    DateTime AccessTicketExpiresAtUtc,
    MeetingGuestPreviewResponse Meeting,
    MeetingGuestParticipantResponse Participant);

public sealed record MeetingGuestStatusResponse(
    MeetingGuestPreviewResponse Meeting,
    MeetingGuestParticipantResponse Participant,
    bool CanJoin);

public sealed record MeetingAttendanceIntervalResponse(
    Guid Id,
    DateTime ConnectedAtUtc,
    DateTime? DisconnectedAtUtc,
    string Source);

public sealed record MeetingEventResponse(
    Guid Id,
    string EventType,
    Guid? ParticipantId,
    Guid? ActorUserId,
    string ActorDisplayName,
    string Source,
    DateTime OccurredAtUtc,
    string? Detail);

public sealed record MeetingParticipantReportResponse(
    MeetingParticipantResponse Participant,
    IReadOnlyList<MeetingAttendanceIntervalResponse> AttendanceIntervals,
    long TotalConnectedSeconds);

public sealed record MeetingReportResponse(
    MeetingSessionResponse Meeting,
    IReadOnlyList<MeetingParticipantReportResponse> Participants,
    IReadOnlyList<MeetingEventResponse> Events,
    DateTime GeneratedAtUtc);
