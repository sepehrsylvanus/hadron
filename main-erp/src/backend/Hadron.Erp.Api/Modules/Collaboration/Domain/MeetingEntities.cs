namespace Hadron.Erp.Api.Modules.Collaboration.Domain;

public static class MeetingStatuses
{
    public const string Scheduled = "SCHEDULED";
    public const string Live = "LIVE";
    public const string Ended = "ENDED";
    public const string Cancelled = "CANCELLED";
}

public static class MeetingRoles
{
    public const string Host = "HOST";
    public const string CoHost = "COHOST";
    public const string Presenter = "PRESENTER";
    public const string Attendee = "ATTENDEE";
    public const string ExternalGuest = "EXTERNAL_GUEST";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Host, CoHost, Presenter, Attendee, ExternalGuest
    };
}

public static class MeetingAdmissionStatuses
{
    public const string Invited = "INVITED";
    public const string Waiting = "WAITING";
    public const string Admitted = "ADMITTED";
    public const string Denied = "DENIED";
    public const string Revoked = "REVOKED";
}

public static class MeetingEventSources
{
    public const string Ui = "UI";
    public const string Client = "CLIENT";
    public const string Provider = "PROVIDER";
}

public sealed record MeetingPolicySnapshot(
    bool AttendeeMicrophone,
    bool AttendeeCamera,
    bool AttendeeScreenShare,
    bool GuestMicrophone,
    bool GuestCamera,
    bool GuestScreenShare,
    bool ChatEnabled,
    bool FileSharingEnabled,
    bool EphemeralWhiteboardEnabled)
{
    public const bool RecordingEnabled = false;
}

public sealed class MeetingSession
{
    private MeetingSession() { }

    public MeetingSession(Guid conversationId, string title, DateTime? scheduledStartAtUtc,
        DateTime? scheduledEndAtUtc, Guid createdByUserId, string createdByDisplayName,
        string? roomName, MeetingPolicySnapshot policy, DateTime nowUtc)
    {
        Id = Guid.NewGuid();
        ConversationId = conversationId;
        Title = title.Trim();
        ScheduledStartAtUtc = scheduledStartAtUtc;
        ScheduledEndAtUtc = scheduledEndAtUtc;
        CreatedByUserId = createdByUserId;
        CreatedByDisplayName = createdByDisplayName.Trim();
        CreatedAtUtc = nowUtc;
        Status = scheduledStartAtUtc.HasValue ? MeetingStatuses.Scheduled : MeetingStatuses.Live;
        StartedAtUtc = Status == MeetingStatuses.Live ? nowUtc : null;
        RoomName = Status == MeetingStatuses.Live ? roomName : null;
        Provider = "LIVEKIT";
        Apply(policy);
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid ConversationId { get; private set; }
    public Conversation Conversation { get; private set; } = null!;
    public string Title { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public bool IsLocked { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public string? RoomName { get; private set; }
    public DateTime? ScheduledStartAtUtc { get; private set; }
    public DateTime? ScheduledEndAtUtc { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? EndedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public string CreatedByDisplayName { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public long Version { get; private set; }

    public bool AttendeeMicrophone { get; private set; }
    public bool AttendeeCamera { get; private set; }
    public bool AttendeeScreenShare { get; private set; }
    public bool GuestMicrophone { get; private set; }
    public bool GuestCamera { get; private set; }
    public bool GuestScreenShare { get; private set; }
    public bool ChatEnabled { get; private set; }
    public bool FileSharingEnabled { get; private set; }
    public bool EphemeralWhiteboardEnabled { get; private set; }
    public bool RecordingEnabled { get; private set; }

    public ICollection<MeetingParticipant> Participants { get; private set; } = [];
    public ICollection<MeetingEvent> Events { get; private set; } = [];

    public MeetingPolicySnapshot Policy => new(AttendeeMicrophone, AttendeeCamera, AttendeeScreenShare,
        GuestMicrophone, GuestCamera, GuestScreenShare, ChatEnabled, FileSharingEnabled,
        EphemeralWhiteboardEnabled);

    public void Start(string roomName, long expectedVersion, DateTime nowUtc)
    {
        EnsureVersion(expectedVersion);
        if (Status != MeetingStatuses.Scheduled)
            throw new CollaborationConflictException("MEETING_NOT_SCHEDULED", "فقط جلسه زمان‌بندی‌شده قابل شروع است.");
        RoomName = roomName;
        Status = MeetingStatuses.Live;
        StartedAtUtc = nowUtc;
        Version++;
    }

    public void UpdateScheduled(string title, DateTime scheduledStartAtUtc, DateTime scheduledEndAtUtc,
        MeetingPolicySnapshot policy, long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (Status != MeetingStatuses.Scheduled)
            throw new CollaborationConflictException("MEETING_NOT_EDITABLE",
                "فقط جلسه زمان‌بندی‌شده و شروع‌نشده قابل ویرایش است.");
        Title = title.Trim();
        ScheduledStartAtUtc = scheduledStartAtUtc;
        ScheduledEndAtUtc = scheduledEndAtUtc;
        Apply(policy);
        Version++;
    }

    public void Cancel(long expectedVersion, DateTime nowUtc)
    {
        EnsureVersion(expectedVersion);
        if (Status != MeetingStatuses.Scheduled)
            throw new CollaborationConflictException("MEETING_CANNOT_BE_CANCELLED", "فقط جلسه زمان‌بندی‌شده قابل لغو است.");
        Status = MeetingStatuses.Cancelled;
        CancelledAtUtc = nowUtc;
        IsLocked = true;
        Version++;
    }

    public void SetLocked(bool locked, long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (Status is MeetingStatuses.Ended or MeetingStatuses.Cancelled)
            throw new CollaborationConflictException("MEETING_CLOSED", "جلسه پایان یافته یا لغو شده است.");
        if (IsLocked == locked) return;
        IsLocked = locked;
        Version++;
    }

    public void End(long expectedVersion, DateTime nowUtc)
    {
        EnsureVersion(expectedVersion);
        if (Status != MeetingStatuses.Live)
            throw new CollaborationConflictException("MEETING_NOT_LIVE", "فقط جلسه در حال برگزاری قابل پایان است.");
        Status = MeetingStatuses.Ended;
        EndedAtUtc = nowUtc;
        IsLocked = true;
        Version++;
    }

    private void Apply(MeetingPolicySnapshot policy)
    {
        AttendeeMicrophone = policy.AttendeeMicrophone;
        AttendeeCamera = policy.AttendeeCamera;
        AttendeeScreenShare = policy.AttendeeScreenShare;
        GuestMicrophone = policy.GuestMicrophone;
        GuestCamera = policy.GuestCamera;
        GuestScreenShare = policy.GuestScreenShare;
        ChatEnabled = policy.ChatEnabled;
        FileSharingEnabled = policy.FileSharingEnabled;
        EphemeralWhiteboardEnabled = policy.EphemeralWhiteboardEnabled;
        RecordingEnabled = false;
    }

    private void EnsureVersion(long expectedVersion)
    {
        if (Version != expectedVersion)
            throw new CollaborationConflictException("MEETING_VERSION_CONFLICT",
                "جلسه توسط کاربر دیگری تغییر کرده است؛ اطلاعات را تازه‌سازی کنید.");
    }
}

public sealed class MeetingParticipant
{
    private MeetingParticipant() { }

    private MeetingParticipant(Guid meetingSessionId, Guid? userId, string displayName, string role,
        string admissionStatus, DateTime nowUtc)
    {
        Id = Guid.NewGuid();
        MeetingSessionId = meetingSessionId;
        UserId = userId;
        DisplayName = displayName.Trim();
        Role = role;
        AdmissionStatus = admissionStatus;
        CreatedAtUtc = nowUtc;
        AdmittedAtUtc = admissionStatus == MeetingAdmissionStatuses.Admitted ? nowUtc : null;
        Version = 1;
    }

    public static MeetingParticipant Internal(Guid meetingSessionId, Guid userId, string displayName,
        string role, DateTime nowUtc) => new(meetingSessionId, userId, displayName, role,
        MeetingAdmissionStatuses.Admitted, nowUtc);

    public static MeetingParticipant Guest(Guid meetingSessionId, string displayName, DateTime nowUtc) =>
        new(meetingSessionId, null, displayName, MeetingRoles.ExternalGuest,
            MeetingAdmissionStatuses.Invited, nowUtc);

    public Guid Id { get; private set; }
    public Guid MeetingSessionId { get; private set; }
    public MeetingSession MeetingSession { get; private set; } = null!;
    public Guid? UserId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string Role { get; private set; } = string.Empty;
    public string AdmissionStatus { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? AdmittedAtUtc { get; private set; }
    public DateTime? DeniedAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public long Version { get; private set; }
    public ICollection<MeetingAttendanceInterval> AttendanceIntervals { get; private set; } = [];

    public bool IsExternal => UserId is null;

    public void EnterWaitingRoom(DateTime nowUtc)
    {
        if (AdmissionStatus != MeetingAdmissionStatuses.Invited)
            throw new CollaborationConflictException("MEETING_INVITE_ALREADY_USED", "دعوت‌نامه قبلاً استفاده شده است.");
        AdmissionStatus = MeetingAdmissionStatuses.Waiting;
        Version++;
    }

    public void Admit(DateTime nowUtc)
    {
        if (AdmissionStatus != MeetingAdmissionStatuses.Waiting)
            throw new CollaborationConflictException("MEETING_GUEST_NOT_WAITING", "مهمان در اتاق انتظار نیست.");
        AdmissionStatus = MeetingAdmissionStatuses.Admitted;
        AdmittedAtUtc = nowUtc;
        Version++;
    }

    public void Deny(DateTime nowUtc)
    {
        if (AdmissionStatus is not (MeetingAdmissionStatuses.Waiting or MeetingAdmissionStatuses.Invited))
            throw new CollaborationConflictException("MEETING_GUEST_CANNOT_BE_DENIED", "این مهمان قابل رد کردن نیست.");
        AdmissionStatus = MeetingAdmissionStatuses.Denied;
        DeniedAtUtc = nowUtc;
        Version++;
    }

    public void Revoke(DateTime nowUtc)
    {
        if (Role == MeetingRoles.Host)
            throw new CollaborationConflictException("MEETING_HOST_CANNOT_BE_REVOKED", "میزبان اصلی قابل لغو نیست.");
        if (AdmissionStatus == MeetingAdmissionStatuses.Revoked) return;
        AdmissionStatus = MeetingAdmissionStatuses.Revoked;
        RevokedAtUtc = nowUtc;
        Version++;
    }

    public bool ChangeRole(string role, long expectedVersion)
    {
        if (Version != expectedVersion)
            throw new CollaborationConflictException("MEETING_PARTICIPANT_VERSION_CONFLICT",
                "نقش شرکت‌کننده تغییر کرده است؛ اطلاعات را تازه‌سازی کنید.");
        if (Role is MeetingRoles.Host or MeetingRoles.ExternalGuest)
            throw new CollaborationConflictException("MEETING_ROLE_FIXED", "نقش این شرکت‌کننده قابل تغییر نیست.");
        if (Role == role) return false;
        Role = role;
        Version++;
        return true;
    }
}

public sealed class MeetingGuestInvite
{
    private MeetingGuestInvite() { }
    public MeetingGuestInvite(Guid meetingSessionId, Guid participantId, string tokenHash,
        DateTime expiresAtUtc, Guid createdByUserId, DateTime nowUtc)
    {
        Id = Guid.NewGuid(); MeetingSessionId = meetingSessionId; ParticipantId = participantId;
        TokenHash = tokenHash; ExpiresAtUtc = expiresAtUtc; CreatedByUserId = createdByUserId;
        CreatedAtUtc = nowUtc; Version = 1;
    }
    public Guid Id { get; private set; }
    public Guid MeetingSessionId { get; private set; }
    public MeetingSession MeetingSession { get; private set; } = null!;
    public Guid ParticipantId { get; private set; }
    public MeetingParticipant Participant { get; private set; } = null!;
    public string TokenHash { get; private set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? RedeemedAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public long Version { get; private set; }

    public void Redeem(DateTime nowUtc)
    {
        if (RevokedAtUtc.HasValue || RedeemedAtUtc.HasValue || ExpiresAtUtc <= nowUtc)
            throw new MeetingGuestAccessException();
        RedeemedAtUtc = nowUtc; Version++;
    }

    public void Revoke(DateTime nowUtc)
    {
        if (RevokedAtUtc.HasValue) return;
        RevokedAtUtc = nowUtc; Version++;
    }
}

public sealed class MeetingGuestAccessTicket
{
    private MeetingGuestAccessTicket() { }
    public MeetingGuestAccessTicket(Guid meetingSessionId, Guid participantId, string tokenHash,
        DateTime expiresAtUtc, DateTime nowUtc)
    {
        Id = Guid.NewGuid(); MeetingSessionId = meetingSessionId; ParticipantId = participantId;
        TokenHash = tokenHash; ExpiresAtUtc = expiresAtUtc; CreatedAtUtc = nowUtc;
    }
    public Guid Id { get; private set; }
    public Guid MeetingSessionId { get; private set; }
    public MeetingSession MeetingSession { get; private set; } = null!;
    public Guid ParticipantId { get; private set; }
    public MeetingParticipant Participant { get; private set; } = null!;
    public string TokenHash { get; private set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public void Revoke(DateTime nowUtc) => RevokedAtUtc ??= nowUtc;
}

public sealed class MeetingAttendanceInterval
{
    private MeetingAttendanceInterval() { }
    public MeetingAttendanceInterval(Guid meetingSessionId, Guid participantId, DateTime connectedAtUtc,
        string source)
    {
        Id = Guid.NewGuid(); MeetingSessionId = meetingSessionId; ParticipantId = participantId;
        ConnectedAtUtc = connectedAtUtc; Source = source;
    }
    public Guid Id { get; private set; }
    public Guid MeetingSessionId { get; private set; }
    public MeetingSession MeetingSession { get; private set; } = null!;
    public Guid ParticipantId { get; private set; }
    public MeetingParticipant Participant { get; private set; } = null!;
    public DateTime ConnectedAtUtc { get; private set; }
    public DateTime? DisconnectedAtUtc { get; private set; }
    public string Source { get; private set; } = string.Empty;
    public void Disconnect(DateTime nowUtc) => DisconnectedAtUtc ??= nowUtc;
}

public sealed class MeetingEvent
{
    private MeetingEvent() { }
    public MeetingEvent(Guid meetingSessionId, string eventType, Guid? participantId, Guid? actorUserId,
        string actorDisplayName, string source, DateTime occurredAtUtc, string? detail = null)
    {
        Id = Guid.NewGuid(); MeetingSessionId = meetingSessionId; EventType = eventType;
        ParticipantId = participantId; ActorUserId = actorUserId; ActorDisplayName = actorDisplayName.Trim();
        Source = source; OccurredAtUtc = occurredAtUtc; Detail = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();
    }
    public Guid Id { get; private set; }
    public Guid MeetingSessionId { get; private set; }
    public MeetingSession MeetingSession { get; private set; } = null!;
    public string EventType { get; private set; } = string.Empty;
    public Guid? ParticipantId { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string ActorDisplayName { get; private set; } = string.Empty;
    public string Source { get; private set; } = string.Empty;
    public DateTime OccurredAtUtc { get; private set; }
    public string? Detail { get; private set; }
}

public sealed class MeetingGuestAccessException : Exception
{
    public MeetingGuestAccessException() : base("Guest access is invalid or expired.") { }
}
