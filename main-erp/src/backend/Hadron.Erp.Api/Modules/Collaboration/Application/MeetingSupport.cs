using System.Security.Cryptography;
using System.Text;
using Hadron.Erp.Api.Modules.Collaboration.Domain;

namespace Hadron.Erp.Api.Modules.Collaboration.Application;

internal static class MeetingSupport
{
    internal static readonly MeetingPolicySnapshot DefaultPolicy = new(
        AttendeeMicrophone: true,
        AttendeeCamera: true,
        AttendeeScreenShare: false,
        GuestMicrophone: true,
        GuestCamera: true,
        GuestScreenShare: false,
        ChatEnabled: true,
        FileSharingEnabled: true,
        EphemeralWhiteboardEnabled: false);

    internal static MeetingPolicySnapshot Policy(MeetingPolicyRequest? value) => value is null
        ? DefaultPolicy
        : new(value.AttendeeMicrophone, value.AttendeeCamera, value.AttendeeScreenShare,
            value.GuestMicrophone, value.GuestCamera, value.GuestScreenShare, value.ChatEnabled,
            value.FileSharingEnabled, value.EphemeralWhiteboardEnabled);

    internal static MeetingPolicyResponse Policy(MeetingSession value) => new(
        value.AttendeeMicrophone, value.AttendeeCamera, value.AttendeeScreenShare,
        value.GuestMicrophone, value.GuestCamera, value.GuestScreenShare,
        value.ChatEnabled, value.FileSharingEnabled, value.EphemeralWhiteboardEnabled,
        RecordingEnabled: false);

    internal static MeetingParticipantResponse Participant(MeetingParticipant value) => new(
        value.Id, value.UserId, value.DisplayName, value.Role, value.AdmissionStatus,
        value.CreatedAtUtc, value.AdmittedAtUtc, value.DeniedAtUtc, value.RevokedAtUtc, value.Version);

    internal static MeetingGuestParticipantResponse GuestParticipant(MeetingParticipant value) => new(
        value.Id, value.DisplayName, value.Role, value.AdmissionStatus);

    internal static MeetingSessionResponse Meeting(MeetingSession value) => new(
        value.Id, value.ConversationId, value.Title, value.Status, value.IsLocked, value.Provider,
        value.ScheduledStartAtUtc, value.ScheduledEndAtUtc, value.StartedAtUtc, value.EndedAtUtc,
        value.CancelledAtUtc, value.CreatedByUserId, value.CreatedByDisplayName, value.CreatedAtUtc,
        value.Version, Policy(value), value.Participants.OrderBy(x => RoleOrder(x.Role))
            .ThenBy(x => x.DisplayName).Select(Participant).ToArray());

    internal static MeetingSummaryResponse Summary(MeetingSession value) => new(
        value.Id, value.ConversationId, value.Title, value.Status, value.IsLocked,
        value.ScheduledStartAtUtc, value.ScheduledEndAtUtc, value.StartedAtUtc, value.EndedAtUtc,
        value.Participants.Count(x => x.AdmissionStatus is not (MeetingAdmissionStatuses.Denied or MeetingAdmissionStatuses.Revoked)),
        value.Participants.Count(x => x.AdmissionStatus == MeetingAdmissionStatuses.Waiting), value.Version);

    internal static MeetingGuestPreviewResponse GuestPreview(MeetingSession value)
    {
        var guestSafePolicy = Policy(value) with { FileSharingEnabled = false };
        return new(value.Id, value.Title, value.Status, value.IsLocked, value.ScheduledStartAtUtc,
            value.ScheduledEndAtUtc, guestSafePolicy);
    }

    internal static MeetingGuestRoomResponse GuestRoom(MeetingSession value) => new(
        value.Title,
        value.Participants.Where(x => x.AdmissionStatus == MeetingAdmissionStatuses.Admitted)
            .OrderBy(x => RoleOrder(x.Role)).ThenBy(x => x.DisplayName)
            .Select(GuestParticipant).ToArray());

    internal static MeetingJoinCapabilities Capabilities(MeetingSession meeting, MeetingParticipant participant)
    {
        var microphone = participant.Role switch
        {
            MeetingRoles.Attendee => meeting.AttendeeMicrophone,
            MeetingRoles.ExternalGuest => meeting.GuestMicrophone,
            _ => true
        };
        var camera = participant.Role switch
        {
            MeetingRoles.Attendee => meeting.AttendeeCamera,
            MeetingRoles.ExternalGuest => meeting.GuestCamera,
            _ => true
        };
        var screenShare = participant.Role switch
        {
            MeetingRoles.Host or MeetingRoles.CoHost or MeetingRoles.Presenter => true,
            MeetingRoles.Attendee => meeting.AttendeeScreenShare,
            MeetingRoles.ExternalGuest => meeting.GuestScreenShare,
            _ => false
        };
        return new(microphone, camera, screenShare, meeting.ChatEnabled,
            participant.IsExternal ? false : meeting.FileSharingEnabled,
            meeting.EphemeralWhiteboardEnabled, Recording: false);
    }

    internal static string NewOpaqueToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    internal static string HashToken(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));

    internal static int RoleOrder(string role) => role switch
    {
        MeetingRoles.Host => 0,
        MeetingRoles.CoHost => 1,
        MeetingRoles.Presenter => 2,
        MeetingRoles.Attendee => 3,
        _ => 4
    };
}
