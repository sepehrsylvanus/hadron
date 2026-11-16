using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Collaboration.Application;
using Hadron.Erp.Api.Modules.Collaboration.Domain;
using Hadron.Erp.Api.Modules.Identity.Domain;
using Hadron.Erp.Api.Platform.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Hadron.Erp.IntegrationTests;

[Collection("ReferenceData")]
public sealed class MeetingTests : IClassFixture<FoundationApiFactory>, IAsyncLifetime
{
    private static readonly Guid Host = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid CoHost = Guid.Parse("00000000-0000-0000-0000-000000000012");
    private static readonly Guid Presenter = Guid.Parse("00000000-0000-0000-0000-000000000013");
    private static readonly Guid Attendee = Guid.Parse("00000000-0000-0000-0000-000000000014");
    private static readonly Guid Outsider = Guid.Parse("00000000-0000-0000-0000-000000000015");
    private readonly FoundationApiFactory factory;
    private readonly HttpClient client;

    public MeetingTests(FoundationApiFactory factory)
    {
        this.factory = factory;
        client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await using var db = await Db();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE collaboration.conversations RESTART IDENTITY CASCADE");
        await SeedUsers(db);
        factory.AuditSink.Clear();
        factory.MeetingMediaGateway.Reset();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Scheduled_meeting_can_be_resolved_edited_started_listed_ended_and_cancelled_safely()
    {
        var resolved = await Post<ResolveMeetingJalaliDateTimeResponse>(client,
            "/api/collaboration/meetings/resolve-jalali-datetime",
            new { jalaliDate = "۱۴۰۵۰۶۲۴", localTime = "۱۴:۳۰" }, HttpStatusCode.OK);
        Assert.Equal(new DateOnly(2026, 9, 15), resolved.GregorianDate);
        Assert.Equal(new DateTime(2026, 9, 15, 11, 0, 0, DateTimeKind.Utc), resolved.UtcDateTimeUtc);
        Assert.Equal("Asia/Tehran", resolved.TimeZoneId);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            "/api/collaboration/meetings/resolve-jalali-datetime",
            new { jalaliDate = "14051301", localTime = "14:30" })).StatusCode);

        var start = DateTime.UtcNow.AddDays(5);
        var scheduled = await CreateMeeting(new
        {
            title = "جلسه برنامه تولید هفته آینده",
            scheduledStartAtUtc = start,
            scheduledEndAtUtc = start.AddHours(1),
            participants = new[]
            {
                new { userId = CoHost, role = MeetingRoles.CoHost },
                new { userId = Attendee, role = MeetingRoles.Attendee }
            },
            policy = (object?)null
        });
        Assert.Equal(MeetingStatuses.Scheduled, scheduled.Status);
        Assert.False(scheduled.Policy.RecordingEnabled);

        var revisedStart = start.AddHours(2);
        var updated = await Put<MeetingSessionResponse>(client, $"/api/collaboration/meetings/{scheduled.Id}", new
        {
            title = "جلسه اصلاح‌شده برنامه تولید",
            scheduledStartAtUtc = revisedStart,
            scheduledEndAtUtc = revisedStart.AddMinutes(90),
            participants = new[] { new { userId = Presenter, role = MeetingRoles.Presenter } },
            policy = new
            {
                attendeeMicrophone = false, attendeeCamera = true, attendeeScreenShare = false,
                guestMicrophone = false, guestCamera = false, guestScreenShare = false,
                chatEnabled = true, fileSharingEnabled = true, ephemeralWhiteboardEnabled = true
            },
            expectedVersion = scheduled.Version
        });
        Assert.Equal("جلسه اصلاح‌شده برنامه تولید", updated.Title);
        Assert.Collection(updated.Participants.OrderBy(x => x.Role),
            participant => Assert.Equal(MeetingRoles.Host, participant.Role),
            participant => Assert.Equal(MeetingRoles.Presenter, participant.Role));

        await using (var db = await Db())
        {
            var conversation = await db.CollaborationConversations.AsNoTracking()
                .Include(x => x.Members).SingleAsync(x => x.Id == updated.ConversationId);
            Assert.True(conversation.IsPrivate);
            Assert.Equal(2, conversation.Members.Count);
            Assert.Contains(conversation.Members, x => x.UserId == Host);
            Assert.Contains(conversation.Members, x => x.UserId == Presenter);
        }

        var live = await Post<MeetingSessionResponse>(client, $"/api/collaboration/meetings/{updated.Id}/start",
            new { expectedVersion = updated.Version }, HttpStatusCode.OK);
        Assert.Equal(MeetingStatuses.Live, live.Status);
        Assert.NotNull(live.StartedAtUtc);

        using (var presenterClient = ClientFor(Presenter, "ارائه‌دهنده تست"))
        {
            var notifications = await presenterClient.GetFromJsonAsync<CollaborationNotificationSummaryResponse>(
                "/api/collaboration/notifications?limit=12");
            Assert.NotNull(notifications);
            Assert.Equal(1, notifications.ActiveCallCount);
            Assert.Contains(notifications.Items, item => item.Kind == "CALL" && item.EntityId == live.Id &&
                item.TargetUrl == $"/collaboration/meetings/{live.Id}");
        }

        using (var meetingViewDenied = ClientFor(Presenter, "ارائه‌دهنده بدون مشاهده جلسه"))
        {
            meetingViewDenied.DefaultRequestHeaders.Add("X-Hadron-Test-Denied-Permissions",
                Permissions.CollaborationMeetingView);
            var notifications = await meetingViewDenied.GetFromJsonAsync<CollaborationNotificationSummaryResponse>(
                "/api/collaboration/notifications?limit=12");
            Assert.NotNull(notifications);
            Assert.Equal(0, notifications.ActiveCallCount);
            Assert.DoesNotContain(notifications.Items, item => item.EntityId == live.Id);
        }

        var page = await client.GetFromJsonAsync<CollaborationPage<MeetingSummaryResponse>>(
            "/api/collaboration/meetings?search=اصلاح‌شده&status=LIVE&page=1&pageSize=10");
        Assert.Equal(live.Id, Assert.Single(page!.Items).Id);
        using (var outsider = ClientFor(Outsider, "کاربر بیرون جلسه"))
            Assert.Equal(HttpStatusCode.NotFound,
                (await outsider.GetAsync($"/api/collaboration/meetings/{live.Id}")).StatusCode);

        var ended = await Post<MeetingSessionResponse>(client, $"/api/collaboration/meetings/{live.Id}/end",
            new { expectedVersion = live.Version }, HttpStatusCode.OK);
        Assert.Equal(MeetingStatuses.Ended, ended.Status);
        Assert.Single(factory.MeetingMediaGateway.EndedRooms);

        using (var presenterClient = ClientFor(Presenter, "ارائه‌دهنده تست"))
        {
            var notifications = await presenterClient.GetFromJsonAsync<CollaborationNotificationSummaryResponse>(
                "/api/collaboration/notifications?limit=12");
            Assert.NotNull(notifications);
            Assert.Equal(0, notifications.ActiveCallCount);
            Assert.DoesNotContain(notifications.Items, item => item.EntityId == ended.Id);
        }

        var presenterParticipant = ended.Participants.Single(x => x.UserId == Presenter);
        var terminalMutation = await client.PostAsJsonAsync(
            $"/api/collaboration/meetings/{ended.Id}/participants/{presenterParticipant.Id}/role",
            new { role = MeetingRoles.Attendee, expectedVersion = presenterParticipant.Version });
        Assert.Equal(HttpStatusCode.Conflict, terminalMutation.StatusCode);
        Assert.Contains("MEETING_CLOSED", await terminalMutation.Content.ReadAsStringAsync());

        var cancelStart = DateTime.UtcNow.AddDays(7);
        var cancellable = await CreateMeeting(new
        {
            title = "جلسه قابل لغو", scheduledStartAtUtc = cancelStart,
            scheduledEndAtUtc = cancelStart.AddHours(1), participants = Array.Empty<object>(), policy = (object?)null
        });
        var cancelled = await Post<MeetingSessionResponse>(client,
            $"/api/collaboration/meetings/{cancellable.Id}/cancel",
            new { expectedVersion = cancellable.Version }, HttpStatusCode.OK);
        Assert.Equal(MeetingStatuses.Cancelled, cancelled.Status);
    }

    [Fact]
    public async Task Meeting_list_search_matches_title_full_id_and_the_displayed_short_id()
    {
        var start = DateTime.UtcNow.AddDays(2);
        var target = await CreateMeeting(new
        {
            title = "جلسه تطبیق شناسه برنامه تولید",
            scheduledStartAtUtc = start,
            scheduledEndAtUtc = start.AddHours(1),
            participants = Array.Empty<object>(),
            policy = (object?)null
        });
        _ = await CreateMeeting(new
        {
            title = "جلسه نامرتبط کنترل",
            scheduledStartAtUtc = start.AddHours(2),
            scheduledEndAtUtc = start.AddHours(3),
            participants = Array.Empty<object>(),
            policy = (object?)null
        });

        var titlePage = await client.GetFromJsonAsync<CollaborationPage<MeetingSummaryResponse>>(
            $"/api/collaboration/meetings?search={Uri.EscapeDataString("تطبیق شناسه")}&page=1&pageSize=25");
        Assert.Equal(target.Id, Assert.Single(titlePage!.Items).Id);

        var fullIdPage = await client.GetFromJsonAsync<CollaborationPage<MeetingSummaryResponse>>(
            $"/api/collaboration/meetings?search={target.Id:D}&page=1&pageSize=25");
        Assert.Equal(target.Id, Assert.Single(fullIdPage!.Items).Id);

        var displayedShortId = target.Id.ToString("D")[..8].ToUpperInvariant();
        var shortIdPage = await client.GetFromJsonAsync<CollaborationPage<MeetingSummaryResponse>>(
            $"/api/collaboration/meetings?search={displayedShortId}&page=1&pageSize=25");
        Assert.Equal(target.Id, Assert.Single(shortIdPage!.Items).Id);
    }

    [Fact]
    public async Task Role_boundaries_and_media_grants_are_derived_server_side()
    {
        var meeting = await CreateMeeting(new
        {
            title = "جلسه نقش و رسانه", scheduledStartAtUtc = (DateTime?)null,
            scheduledEndAtUtc = (DateTime?)null,
            participants = new[]
            {
                new { userId = CoHost, role = MeetingRoles.CoHost },
                new { userId = Presenter, role = MeetingRoles.Presenter },
                new { userId = Attendee, role = MeetingRoles.Attendee }
            },
            policy = new
            {
                attendeeMicrophone = false, attendeeCamera = true, attendeeScreenShare = false,
                guestMicrophone = false, guestCamera = false, guestScreenShare = false,
                chatEnabled = true, fileSharingEnabled = true, ephemeralWhiteboardEnabled = false
            }
        });

        using var cohostClient = ClientFor(CoHost, "هم‌میزبان تست");
        using var presenterClient = ClientFor(Presenter, "ارائه‌دهنده تست");
        using var attendeeClient = ClientFor(Attendee, "شرکت‌کننده تست");
        var cohostJoin = await Post<MeetingJoinResponse>(cohostClient,
            $"/api/collaboration/meetings/{meeting.Id}/join", new { }, HttpStatusCode.OK);
        var presenterJoin = await Post<MeetingJoinResponse>(presenterClient,
            $"/api/collaboration/meetings/{meeting.Id}/join", new { }, HttpStatusCode.OK);
        var attendeeJoin = await Post<MeetingJoinResponse>(attendeeClient,
            $"/api/collaboration/meetings/{meeting.Id}/join", new { }, HttpStatusCode.OK);
        Assert.True(cohostJoin.Capabilities.ScreenShare);
        Assert.True(presenterJoin.Capabilities.ScreenShare);
        Assert.False(attendeeJoin.Capabilities.Microphone);
        Assert.True(attendeeJoin.Capabilities.Camera);
        Assert.False(attendeeJoin.Capabilities.ScreenShare);
        Assert.False(attendeeJoin.Capabilities.Recording);
        var attendeeParticipant = meeting.Participants.Single(x => x.UserId == Attendee);
        Assert.Equal($"participant:{attendeeParticipant.Id:N}",
            factory.MeetingMediaGateway.Tokens.Last().ParticipantIdentity);

        var presenterParticipant = meeting.Participants.Single(x => x.UserId == Presenter);
        factory.MeetingMediaGateway.RemoveResult = MeetingParticipantRemoveResult.Failed("TEST_ROLE_REFRESH_TIMEOUT");
        var failedDowngrade = await client.PostAsJsonAsync(
            $"/api/collaboration/meetings/{meeting.Id}/participants/{presenterParticipant.Id}/role",
            new { role = MeetingRoles.Attendee, expectedVersion = presenterParticipant.Version });
        Assert.Equal(HttpStatusCode.Conflict, failedDowngrade.StatusCode);
        Assert.Contains("MEETING_PROVIDER_ROLE_REFRESH_FAILED", await failedDowngrade.Content.ReadAsStringAsync());
        await using (var db = await Db())
            Assert.Equal(MeetingRoles.Presenter, await db.CollaborationMeetingParticipants.AsNoTracking()
                .Where(x => x.Id == presenterParticipant.Id).Select(x => x.Role).SingleAsync());
        factory.MeetingMediaGateway.RemoveResult = MeetingParticipantRemoveResult.Removed();

        var forbiddenPromotion = await cohostClient.PostAsJsonAsync(
            $"/api/collaboration/meetings/{meeting.Id}/participants/{attendeeParticipant.Id}/role",
            new { role = MeetingRoles.CoHost, expectedVersion = attendeeParticipant.Version });
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenPromotion.StatusCode);

        var promoted = await Post<MeetingSessionResponse>(client,
            $"/api/collaboration/meetings/{meeting.Id}/participants/{attendeeParticipant.Id}/role",
            new { role = MeetingRoles.CoHost, expectedVersion = attendeeParticipant.Version }, HttpStatusCode.OK);
        Assert.Equal(MeetingRoles.CoHost, promoted.Participants.Single(x => x.Id == attendeeParticipant.Id).Role);

        var attendeeLock = await presenterClient.PostAsJsonAsync($"/api/collaboration/meetings/{meeting.Id}/lock",
            new { expectedVersion = meeting.Version });
        Assert.Equal(HttpStatusCode.Forbidden, attendeeLock.StatusCode);
        using var outsider = ClientFor(Outsider, "کاربر بیرون جلسه");
        Assert.Equal(HttpStatusCode.NotFound,
            (await outsider.PostAsJsonAsync($"/api/collaboration/meetings/{meeting.Id}/join", new { })).StatusCode);
        using var noJoinPermission = ClientFor(Attendee, "کاربر بدون مجوز اتصال");
        noJoinPermission.DefaultRequestHeaders.Add("X-Hadron-Test-Denied-Permissions",
            Permissions.CollaborationMeetingJoin);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await noJoinPermission.PostAsJsonAsync($"/api/collaboration/meetings/{meeting.Id}/join", new { }))
            .StatusCode);
    }

    [Fact]
    public async Task Meeting_chat_and_file_policies_are_enforced_by_conversation_endpoints()
    {
        var meeting = await CreateMeeting(new
        {
            title = "جلسه بدون گفتگو و فایل", scheduledStartAtUtc = (DateTime?)null,
            scheduledEndAtUtc = (DateTime?)null, participants = Array.Empty<object>(),
            policy = new
            {
                attendeeMicrophone = true, attendeeCamera = true, attendeeScreenShare = false,
                guestMicrophone = false, guestCamera = false, guestScreenShare = false,
                chatEnabled = false, fileSharingEnabled = false, ephemeralWhiteboardEnabled = false
            }
        });

        var blockedChat = await client.PostAsJsonAsync(
            $"/api/collaboration/conversations/{meeting.ConversationId}/messages",
            new { type = "TEXT", body = "این پیام نباید ثبت شود", stickerCode = (string?)null,
                replyToMessageId = (Guid?)null });
        Assert.Equal(HttpStatusCode.Conflict, blockedChat.StatusCode);
        Assert.Contains("MEETING_CHAT_DISABLED", await blockedChat.Content.ReadAsStringAsync());

        Guid messageId;
        await using (var db = await Db())
        {
            var systemMessage = new ConversationMessage(meeting.ConversationId, Host, "میزبان تست",
                MessageTypes.CallEvent, "رخداد سیستمی جلسه", null, null, null);
            messageId = systemMessage.Id;
            db.CollaborationMessages.Add(systemMessage);
            await db.SaveChangesAsync();
        }
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
        content.Headers.ContentType = new("image/png");
        form.Add(content, "file", "blocked.png");
        var blockedFile = await client.PostAsync(
            $"/api/collaboration/conversations/{meeting.ConversationId}/messages/{messageId}/attachments", form);
        Assert.Equal(HttpStatusCode.Conflict, blockedFile.StatusCode);
        Assert.Contains("MEETING_FILE_SHARING_DISABLED", await blockedFile.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Twenty_way_guest_race_waiting_lock_reconnect_attendance_and_revoke_are_honest()
    {
        var meeting = await CreateMeeting(new
        {
            title = "جلسه مهمان و اتاق انتظار", scheduledStartAtUtc = (DateTime?)null,
            scheduledEndAtUtc = (DateTime?)null, participants = Array.Empty<object>(),
            policy = new
            {
                attendeeMicrophone = true, attendeeCamera = true, attendeeScreenShare = false,
                guestMicrophone = true, guestCamera = false, guestScreenShare = false,
                chatEnabled = true, fileSharingEnabled = true, ephemeralWhiteboardEnabled = true
            }
        });
        var invite = await Post<MeetingGuestInviteResponse>(client,
            $"/api/collaboration/meetings/{meeting.Id}/guest-invites",
            new { guestDisplayName = "مهمان مسابقه بیست‌تایی", expiresAtUtc = DateTime.UtcNow.AddHours(1) },
            HttpStatusCode.Created);
        var nonAdmittedInvite = await Post<MeetingGuestInviteResponse>(client,
            $"/api/collaboration/meetings/{meeting.Id}/guest-invites",
            new { guestDisplayName = "مهمان دعوت‌شده غایب", expiresAtUtc = DateTime.UtcNow.AddHours(1) },
            HttpStatusCode.Created);

        var redemptions = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            client.PostAsJsonAsync("/api/collaboration/meeting-guests/redeem",
                new { inviteToken = invite.InviteToken })));
        Assert.Single(redemptions, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Equal(19, redemptions.Count(response => response.StatusCode == HttpStatusCode.Unauthorized));
        var redemptionBody = await redemptions.Single(x => x.StatusCode == HttpStatusCode.OK)
            .Content.ReadAsStringAsync();
        using (var redemptionPayload = JsonDocument.Parse(redemptionBody))
            AssertGuestParticipantIsRedacted(redemptionPayload.RootElement.GetProperty("participant"));
        var redemption = JsonSerializer.Deserialize<MeetingGuestRedeemResponse>(redemptionBody,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        await using (var db = await Db())
        {
            var storedInvite = await db.CollaborationMeetingGuestInvites.AsNoTracking()
                .SingleAsync(x => x.Id == invite.Id);
            var storedTicket = await db.CollaborationMeetingGuestAccessTickets.AsNoTracking()
                .SingleAsync(x => x.ParticipantId == invite.Participant.Id);
            Assert.Equal(64, storedInvite.TokenHash.Length);
            Assert.Equal(64, storedTicket.TokenHash.Length);
            Assert.NotEqual(invite.InviteToken, storedInvite.TokenHash);
            Assert.NotEqual(redemption.AccessTicket, storedTicket.TokenHash);
        }

        var waiting = await Post<MeetingGuestStatusResponse>(client,
            "/api/collaboration/meeting-guests/status", new { accessTicket = redemption.AccessTicket },
            HttpStatusCode.OK);
        Assert.Equal(MeetingAdmissionStatuses.Waiting, waiting.Participant.AdmissionStatus);
        Assert.False(waiting.CanJoin);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(
            "/api/collaboration/meeting-guests/join", new { accessTicket = redemption.AccessTicket })).StatusCode);

        var locked = await Post<MeetingSessionResponse>(client, $"/api/collaboration/meetings/{meeting.Id}/lock",
            new { expectedVersion = meeting.Version }, HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(
            $"/api/collaboration/meetings/{meeting.Id}/participants/{invite.Participant.Id}/admit", new { }))
            .StatusCode);
        var unlocked = await Post<MeetingSessionResponse>(client, $"/api/collaboration/meetings/{meeting.Id}/unlock",
            new { expectedVersion = locked.Version }, HttpStatusCode.OK);
        await Post<MeetingSessionResponse>(client,
            $"/api/collaboration/meetings/{meeting.Id}/participants/{invite.Participant.Id}/admit", new { },
            HttpStatusCode.OK);

        var firstJoinResponse = await client.PostAsJsonAsync("/api/collaboration/meeting-guests/join",
            new { accessTicket = redemption.AccessTicket });
        Assert.Equal(HttpStatusCode.OK, firstJoinResponse.StatusCode);
        var firstJoinBody = await firstJoinResponse.Content.ReadAsStringAsync();
        var firstJoin = JsonSerializer.Deserialize<MeetingGuestJoinResponse>(firstJoinBody,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        using (var joinPayload = JsonDocument.Parse(firstJoinBody))
        {
            var guestMeeting = joinPayload.RootElement.GetProperty("meeting");
            Assert.Equal(new[] { "participants", "title" }, guestMeeting.EnumerateObject()
                .Select(property => property.Name).Order().ToArray());
            var guestRoster = guestMeeting.GetProperty("participants").EnumerateArray().ToArray();
            Assert.All(guestRoster, participant =>
            {
                AssertGuestParticipantIsRedacted(participant);
                Assert.Equal(MeetingAdmissionStatuses.Admitted,
                    participant.GetProperty("admissionStatus").GetString());
            });
            Assert.Contains(guestRoster,
                participant => participant.GetProperty("id").GetGuid() == invite.Participant.Id);
            Assert.Contains(guestRoster,
                participant => participant.GetProperty("id").GetGuid() ==
                    meeting.Participants.Single(x => x.UserId == Host).Id);
            Assert.DoesNotContain(guestRoster,
                participant => participant.GetProperty("id").GetGuid() == nonAdmittedInvite.Participant.Id);
        }
        Assert.DoesNotContain(meeting.ConversationId.ToString(), firstJoinBody,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Host.ToString(), firstJoinBody, StringComparison.OrdinalIgnoreCase);

        var secondJoin = await Post<MeetingGuestJoinResponse>(client, "/api/collaboration/meeting-guests/join",
            new { accessTicket = redemption.AccessTicket }, HttpStatusCode.OK);
        Assert.Equal(firstJoin.Participant.Id, secondJoin.Participant.Id);
        Assert.False(firstJoin.Capabilities.FileSharing);
        Assert.True(firstJoin.Capabilities.EphemeralWhiteboard);
        Assert.Equal($"participant:{invite.Participant.Id:N}",
            factory.MeetingMediaGateway.Tokens[^1].ParticipantIdentity);
        Assert.Equal(factory.MeetingMediaGateway.Tokens[^1].ParticipantIdentity,
            factory.MeetingMediaGateway.Tokens[^2].ParticipantIdentity);

        var connected = await Post<MeetingAttendanceIntervalResponse>(client,
            "/api/collaboration/meeting-guests/connected", new { accessTicket = redemption.AccessTicket },
            HttpStatusCode.OK);
        var duplicateConnected = await Post<MeetingAttendanceIntervalResponse>(client,
            "/api/collaboration/meeting-guests/connected", new { accessTicket = redemption.AccessTicket },
            HttpStatusCode.OK);
        Assert.Equal(connected.Id, duplicateConnected.Id);
        await Post<MeetingAttendanceIntervalResponse>(client, "/api/collaboration/meeting-guests/disconnected",
            new { accessTicket = redemption.AccessTicket }, HttpStatusCode.OK);

        locked = await Post<MeetingSessionResponse>(client, $"/api/collaboration/meetings/{meeting.Id}/lock",
            new { expectedVersion = unlocked.Version }, HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            "/api/collaboration/meeting-guests/join", new { accessTicket = redemption.AccessTicket })).StatusCode);
        unlocked = await Post<MeetingSessionResponse>(client, $"/api/collaboration/meetings/{meeting.Id}/unlock",
            new { expectedVersion = locked.Version }, HttpStatusCode.OK);

        var deniedInvite = await Post<MeetingGuestInviteResponse>(client,
            $"/api/collaboration/meetings/{meeting.Id}/guest-invites",
            new { guestDisplayName = "مهمان ردشده", expiresAtUtc = DateTime.UtcNow.AddHours(1) },
            HttpStatusCode.Created);
        var deniedTicket = await Post<MeetingGuestRedeemResponse>(client,
            "/api/collaboration/meeting-guests/redeem", new { inviteToken = deniedInvite.InviteToken },
            HttpStatusCode.OK);
        await Post<MeetingSessionResponse>(client,
            $"/api/collaboration/meetings/{meeting.Id}/participants/{deniedInvite.Participant.Id}/deny", new { },
            HttpStatusCode.OK);
        var deniedStatus = await Post<MeetingGuestStatusResponse>(client,
            "/api/collaboration/meeting-guests/status", new { accessTicket = deniedTicket.AccessTicket },
            HttpStatusCode.OK);
        Assert.False(deniedStatus.CanJoin);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(
            "/api/collaboration/meeting-guests/join", new { accessTicket = deniedTicket.AccessTicket })).StatusCode);

        factory.MeetingMediaGateway.RemoveResult = MeetingParticipantRemoveResult.Failed("TEST_TIMEOUT");
        var failedRemoval = await client.PostAsJsonAsync(
            $"/api/collaboration/meetings/{meeting.Id}/participants/{invite.Participant.Id}/revoke", new { });
        Assert.Equal(HttpStatusCode.Conflict, failedRemoval.StatusCode);
        Assert.Contains("MEETING_PROVIDER_REMOVE_FAILED", await failedRemoval.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(
            "/api/collaboration/meeting-guests/status", new { accessTicket = redemption.AccessTicket })).StatusCode);

        factory.MeetingMediaGateway.RemoveResult = MeetingParticipantRemoveResult.Absent();
        var revoked = await Post<MeetingSessionResponse>(client,
            $"/api/collaboration/meetings/{meeting.Id}/participants/{invite.Participant.Id}/revoke", new { },
            HttpStatusCode.OK);
        Assert.Equal(MeetingAdmissionStatuses.Revoked,
            revoked.Participants.Single(x => x.Id == invite.Participant.Id).AdmissionStatus);
        Assert.Equal(2, factory.MeetingMediaGateway.Removals.Count);

        var report = await client.GetFromJsonAsync<MeetingReportResponse>(
            $"/api/collaboration/meetings/{meeting.Id}/report");
        var guestReport = report!.Participants.Single(x => x.Participant.Id == invite.Participant.Id);
        Assert.Single(guestReport.AttendanceIntervals);
        Assert.Equal(MeetingEventSources.Client, guestReport.AttendanceIntervals[0].Source);
        Assert.Contains(report.Events, x => x.EventType == "PROVIDER_PARTICIPANT_REMOVE_FAILED");
        Assert.Contains(report.Events, x => x.EventType == "PROVIDER_PARTICIPANT_REMOVED");
        Assert.Contains(factory.AuditSink.Snapshot(), x => x.EntityType == "MeetingSession" &&
            x.EntityId == meeting.Id && x.Action == "GuestInviteRedeemed");
    }

    [Fact]
    public async Task One_hundred_guest_status_pollers_behind_one_address_are_accepted_without_loading_roster()
    {
        client.DefaultRequestHeaders.Add("X-Hadron-Test-Client-Ip", "198.51.100.100");
        var meeting = await CreateMeeting(new
        {
            title = "جلسه تست وضعیت صد مهمان", scheduledStartAtUtc = (DateTime?)null,
            scheduledEndAtUtc = (DateTime?)null, participants = Array.Empty<object>(), policy = (object?)null
        });
        var inviteTokens = new List<string>(100);
        for (var index = 0; index < 100; index++)
        {
            var invite = await Post<MeetingGuestInviteResponse>(client,
                $"/api/collaboration/meetings/{meeting.Id}/guest-invites",
                new { guestDisplayName = $"مهمان وضعیت {index + 1:000}",
                    expiresAtUtc = DateTime.UtcNow.AddHours(1) }, HttpStatusCode.Created);
            inviteTokens.Add(invite.InviteToken);
        }

        var previews = new List<MeetingGuestPreviewResponse>(100);
        foreach (var batch in inviteTokens.Chunk(25))
        {
            var responses = await Task.WhenAll(batch.Select(inviteToken => client.PostAsJsonAsync(
                "/api/collaboration/meeting-guests/preview", new { inviteToken })));
            foreach (var response in responses)
            {
                using (response)
                {
                    Assert.True(response.StatusCode == HttpStatusCode.OK,
                        $"Guest preview failed with {response.StatusCode}: " +
                        await response.Content.ReadAsStringAsync());
                    previews.Add((await response.Content
                        .ReadFromJsonAsync<MeetingGuestPreviewResponse>())!);
                }
            }
        }
        Assert.Equal(100, previews.Count);
        Assert.All(previews, preview => Assert.Equal(meeting.Id, preview.MeetingId));

        var redemptions = new List<MeetingGuestRedeemResponse>(100);
        foreach (var batch in inviteTokens.Chunk(25))
        {
            var responses = await Task.WhenAll(batch.Select(inviteToken => client.PostAsJsonAsync(
                "/api/collaboration/meeting-guests/redeem", new { inviteToken })));
            foreach (var response in responses)
            {
                using (response)
                {
                    Assert.True(response.StatusCode == HttpStatusCode.OK,
                        $"Guest redeem failed with {response.StatusCode}: " +
                        await response.Content.ReadAsStringAsync());
                    redemptions.Add((await response.Content
                        .ReadFromJsonAsync<MeetingGuestRedeemResponse>())!);
                }
            }
        }
        Assert.Equal(100, redemptions.Count);
        Assert.Equal(100, redemptions.Select(x => x.Participant.Id).Distinct().Count());
        var accessTickets = redemptions.Select(x => x.AccessTicket).ToArray();

        using var commandCapture = new EfCommandCapture();
        var payloads = new List<MeetingGuestStatusResponse>(600);
        // Six complete rounds represent one minute at the public page's ten-second
        // cadence. Batches keep the assertion independent of DB pool size.
        for (var round = 0; round < 6; round++)
        {
            foreach (var batch in accessTickets.Chunk(25))
            {
                var responses = await Task.WhenAll(batch.Select(accessTicket => client.PostAsJsonAsync(
                    "/api/collaboration/meeting-guests/status", new { accessTicket })));
                foreach (var response in responses)
                {
                    using (response)
                    {
                        Assert.True(response.StatusCode == HttpStatusCode.OK,
                            $"Guest status failed with {response.StatusCode}: " +
                            await response.Content.ReadAsStringAsync());
                        payloads.Add((await response.Content
                            .ReadFromJsonAsync<MeetingGuestStatusResponse>())!);
                    }
                }
            }
        }

        Assert.Equal(600, payloads.Count);
        Assert.Equal(100, payloads.Select(x => x.Participant.Id).Distinct().Count());
        Assert.All(payloads, payload =>
        {
            Assert.Equal(meeting.Id, payload.Meeting.MeetingId);
            Assert.Equal(MeetingAdmissionStatuses.Waiting, payload.Participant.AdmissionStatus);
            Assert.False(payload.CanJoin);
        });

        var ticketLookupCommands = commandCapture.Commands.Where(command => command.Contains(
            "meeting_guest_access_tickets", StringComparison.Ordinal)).ToArray();
        Assert.Equal(600, ticketLookupCommands.Length);
        Assert.All(ticketLookupCommands, command => Assert.Equal(1,
            CountOccurrences(command, "meeting_participants")));
    }

    [Fact]
    public async Task Fifty_internal_users_and_one_hundred_guests_complete_the_control_plane_lifecycle()
    {
        client.DefaultRequestHeaders.Add("X-Hadron-Test-Client-Ip", "198.51.100.150");
        var internalAttendeeIds = Enumerable.Range(1, 49)
            .Select(index => Guid.Parse($"20000000-0000-0000-0000-{index:000000000000}"))
            .ToArray();
        await using (var db = await Db())
            await SeedScaleUsers(db, internalAttendeeIds);

        var meeting = await CreateMeeting(new
        {
            title = "جلسه احراز مقیاس ۵۰ کاربر و ۱۰۰ مهمان",
            scheduledStartAtUtc = (DateTime?)null,
            scheduledEndAtUtc = (DateTime?)null,
            participants = internalAttendeeIds.Select((userId, index) => new
            {
                userId,
                role = index < 2 ? MeetingRoles.CoHost :
                    index < 6 ? MeetingRoles.Presenter : MeetingRoles.Attendee
            }).ToArray(),
            policy = new
            {
                attendeeMicrophone = true, attendeeCamera = true, attendeeScreenShare = false,
                guestMicrophone = true, guestCamera = false, guestScreenShare = false,
                chatEnabled = true, fileSharingEnabled = true, ephemeralWhiteboardEnabled = true
            }
        });
        Assert.Equal(MeetingStatuses.Live, meeting.Status);
        Assert.Equal(50, meeting.Participants.Count);

        var invites = new List<MeetingGuestInviteResponse>(100);
        for (var index = 0; index < 100; index++)
        {
            invites.Add(await Post<MeetingGuestInviteResponse>(client,
                $"/api/collaboration/meetings/{meeting.Id}/guest-invites",
                new
                {
                    guestDisplayName = $"مهمان احراز مقیاس {index + 1:000}",
                    expiresAtUtc = DateTime.UtcNow.AddHours(1)
                }, HttpStatusCode.Created));
        }

        var redemptions = await ExecuteJsonBatch<MeetingGuestRedeemResponse>(invites.Select(invite =>
            (Func<Task<HttpResponseMessage>>)(() => client.PostAsJsonAsync(
                "/api/collaboration/meeting-guests/redeem", new { inviteToken = invite.InviteToken }))).ToArray());
        Assert.Equal(100, redemptions.Count);
        Assert.Equal(100, redemptions.Select(x => x.AccessTicket).Distinct().Count());

        MeetingSessionResponse? admittedMeeting = null;
        foreach (var invite in invites)
        {
            admittedMeeting = await Post<MeetingSessionResponse>(client,
                $"/api/collaboration/meetings/{meeting.Id}/participants/{invite.Participant.Id}/admit",
                new { }, HttpStatusCode.OK);
        }
        Assert.NotNull(admittedMeeting);
        Assert.Equal(150, admittedMeeting.Participants.Count);
        Assert.Equal(100, admittedMeeting.Participants.Count(x => x.UserId == null &&
            x.AdmissionStatus == MeetingAdmissionStatuses.Admitted));

        var internalClients = new List<HttpClient>(50) { ClientFor(Host, "میزبان تست مقیاس") };
        internalClients.AddRange(internalAttendeeIds.Select((id, index) =>
            ClientFor(id, $"کاربر داخلی مقیاس {index + 1:00}")));
        try
        {
            var internalJoins = await ExecuteJsonBatch<MeetingJoinResponse>(internalClients.Select(source =>
                (Func<Task<HttpResponseMessage>>)(() => source.PostAsJsonAsync(
                    $"/api/collaboration/meetings/{meeting.Id}/join", new { }))).ToArray());
            var guestJoins = await ExecuteJsonBatch<MeetingGuestJoinResponse>(redemptions.Select(redemption =>
                (Func<Task<HttpResponseMessage>>)(() => client.PostAsJsonAsync(
                    "/api/collaboration/meeting-guests/join",
                    new { accessTicket = redemption.AccessTicket }))).ToArray());
            Assert.Equal(50, internalJoins.Count);
            Assert.Equal(100, guestJoins.Count);
            Assert.Equal(150, factory.MeetingMediaGateway.Tokens.Count);
            Assert.Equal(150, factory.MeetingMediaGateway.Tokens.Select(x => x.ParticipantIdentity)
                .Distinct().Count());

            var internalConnectedOperations = internalClients.Select(source =>
                (Func<Task<HttpResponseMessage>>)(() => source.PostAsJsonAsync(
                    $"/api/collaboration/meetings/{meeting.Id}/connected", new { }))).ToArray();
            var guestConnectedOperations = redemptions.Select(redemption =>
                (Func<Task<HttpResponseMessage>>)(() => client.PostAsJsonAsync(
                    "/api/collaboration/meeting-guests/connected",
                    new { accessTicket = redemption.AccessTicket }))).ToArray();
            var firstInternalAttendance = await ExecuteJsonBatch<MeetingAttendanceIntervalResponse>(
                internalConnectedOperations);
            var firstGuestAttendance = await ExecuteJsonBatch<MeetingAttendanceIntervalResponse>(
                guestConnectedOperations);
            var duplicateInternalAttendance = await ExecuteJsonBatch<MeetingAttendanceIntervalResponse>(
                internalConnectedOperations);
            var duplicateGuestAttendance = await ExecuteJsonBatch<MeetingAttendanceIntervalResponse>(
                guestConnectedOperations);
            Assert.Equal(firstInternalAttendance.Select(x => x.Id), duplicateInternalAttendance.Select(x => x.Id));
            Assert.Equal(firstGuestAttendance.Select(x => x.Id), duplicateGuestAttendance.Select(x => x.Id));

            await ExecuteSuccessBatch(internalClients.Select(source =>
                (Func<Task<HttpResponseMessage>>)(() => source.PostAsJsonAsync(
                    $"/api/collaboration/meetings/{meeting.Id}/disconnected", new { }))).ToArray());
            await ExecuteSuccessBatch(redemptions.Select(redemption =>
                (Func<Task<HttpResponseMessage>>)(() => client.PostAsJsonAsync(
                    "/api/collaboration/meeting-guests/disconnected",
                    new { accessTicket = redemption.AccessTicket }))).ToArray());
        }
        finally
        {
            foreach (var internalClient in internalClients) internalClient.Dispose();
        }

        var report = await client.GetFromJsonAsync<MeetingReportResponse>(
            $"/api/collaboration/meetings/{meeting.Id}/report");
        Assert.NotNull(report);
        Assert.Equal(150, report.Participants.Count);
        Assert.All(report.Participants, row =>
        {
            var interval = Assert.Single(row.AttendanceIntervals);
            Assert.NotNull(interval.DisconnectedAtUtc);
        });

        var ended = await Post<MeetingSessionResponse>(client,
            $"/api/collaboration/meetings/{meeting.Id}/end",
            new { expectedVersion = meeting.Version }, HttpStatusCode.OK);
        Assert.Equal(MeetingStatuses.Ended, ended.Status);
    }

    [Fact]
    public async Task Concurrent_guest_invites_cannot_exceed_the_hard_three_hundred_person_capacity()
    {
        Guid meetingId;
        await using (var db = await Db())
        {
            var conversation = new Conversation(ConversationTypes.Channel, "جلسه ظرفیت", null, true,
                Host, "میزبان تست");
            conversation.Members.Add(new ConversationMember(conversation.Id, Host, "میزبان تست",
                ConversationMemberRoles.Owner));
            var meeting = new MeetingSession(conversation.Id, "جلسه ظرفیت ۳۰۰ نفر", null, null,
                Host, "میزبان تست", $"capacity-{Guid.NewGuid():N}", MeetingSupport.DefaultPolicy,
                DateTime.UtcNow);
            meetingId = meeting.Id;
            meeting.Participants.Add(MeetingParticipant.Internal(meeting.Id, Host, "میزبان تست",
                MeetingRoles.Host, DateTime.UtcNow));
            for (var index = 0; index < 298; index++)
                meeting.Participants.Add(MeetingParticipant.Guest(meeting.Id, $"مهمان ظرفیت {index:000}",
                    DateTime.UtcNow));
            db.CollaborationConversations.Add(conversation);
            db.CollaborationMeetingSessions.Add(meeting);
            await db.SaveChangesAsync();
        }

        var responses = await Task.WhenAll(Enumerable.Range(1, 2).Select(index => client.PostAsJsonAsync(
            $"/api/collaboration/meetings/{meetingId}/guest-invites",
            new { guestDisplayName = $"مهمان نهایی {index}", expiresAtUtc = DateTime.UtcNow.AddHours(1) })));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Contains("MEETING_CAPACITY_REACHED", await responses.Single(x =>
            x.StatusCode == HttpStatusCode.Conflict).Content.ReadAsStringAsync());
        await using var verify = await Db();
        Assert.Equal(300, await verify.CollaborationMeetingParticipants.CountAsync(x =>
            x.MeetingSessionId == meetingId && x.AdmissionStatus != MeetingAdmissionStatuses.Denied &&
            x.AdmissionStatus != MeetingAdmissionStatuses.Revoked));
    }

    [Fact]
    public async Task Provider_end_failure_keeps_meeting_live_and_records_the_failure_before_retry()
    {
        var meeting = await CreateMeeting(new
        {
            title = "جلسه پایان قابل اعتماد", scheduledStartAtUtc = (DateTime?)null,
            scheduledEndAtUtc = (DateTime?)null, participants = Array.Empty<object>(), policy = (object?)null
        });
        factory.MeetingMediaGateway.EndResult = MeetingRoomEndResult.Failed("TEST_PROVIDER_TIMEOUT");
        var failure = await client.PostAsJsonAsync($"/api/collaboration/meetings/{meeting.Id}/end",
            new { expectedVersion = meeting.Version });
        Assert.Equal(HttpStatusCode.Conflict, failure.StatusCode);
        Assert.Contains("MEETING_PROVIDER_END_FAILED", await failure.Content.ReadAsStringAsync());
        await using (var db = await Db())
        {
            var persisted = await db.CollaborationMeetingSessions.AsNoTracking()
                .SingleAsync(x => x.Id == meeting.Id);
            Assert.Equal(MeetingStatuses.Live, persisted.Status);
            Assert.Null(persisted.EndedAtUtc);
            Assert.True(await db.CollaborationMeetingEvents.AnyAsync(x => x.MeetingSessionId == meeting.Id &&
                x.EventType == "PROVIDER_END_FAILED" && x.Detail == "TEST_PROVIDER_TIMEOUT"));
        }

        factory.MeetingMediaGateway.EndResult = MeetingRoomEndResult.Absent();
        var ended = await Post<MeetingSessionResponse>(client, $"/api/collaboration/meetings/{meeting.Id}/end",
            new { expectedVersion = meeting.Version }, HttpStatusCode.OK);
        Assert.Equal(MeetingStatuses.Ended, ended.Status);
        Assert.Equal(2, factory.MeetingMediaGateway.EndedRooms.Count);
    }

    private Task<MeetingSessionResponse> CreateMeeting(object payload) => Post<MeetingSessionResponse>(client,
        "/api/collaboration/meetings", payload, HttpStatusCode.Created);

    private HttpClient ClientFor(Guid id, string name)
    {
        var result = factory.CreateClient();
        result.DefaultRequestHeaders.Add("X-Hadron-Test-User-Id", id.ToString());
        result.DefaultRequestHeaders.Add("X-Hadron-Test-Full-Name", name);
        return result;
    }

    private async Task<HadronDbContext> Db() => await factory.Services
        .GetRequiredService<IDbContextFactory<HadronDbContext>>().CreateDbContextAsync();

    private static async Task SeedUsers(HadronDbContext db)
    {
        var users = new[]
        {
            (Host, "میزبان", "تست", "meeting-host"),
            (CoHost, "هم‌میزبان", "تست", "meeting-cohost"),
            (Presenter, "ارائه‌دهنده", "تست", "meeting-presenter"),
            (Attendee, "شرکت‌کننده", "تست", "meeting-attendee"),
            (Outsider, "بیرون", "جلسه", "meeting-outsider")
        };
        var existing = await db.Users.Where(x => users.Select(user => user.Item1).Contains(x.Id))
            .Select(x => x.Id).ToArrayAsync();
        if (existing.Length == users.Length) return;
        var unitId = await db.OrganizationalUnits.Select(x => (Guid?)x.Id).FirstOrDefaultAsync();
        if (!unitId.HasValue)
        {
            var unit = new OrganizationalUnit("واحد جلسات تست", "Meeting test unit");
            db.OrganizationalUnits.Add(unit);
            unitId = unit.Id;
        }
        var now = DateTime.UtcNow;
        foreach (var (id, first, last, username) in users.Where(x => !existing.Contains(x.Item1)))
            db.Users.Add(User(id, first, last, username, unitId.Value, now));
        await db.SaveChangesAsync();
    }

    private static async Task SeedScaleUsers(HadronDbContext db, IReadOnlyCollection<Guid> userIds)
    {
        var existing = await db.Users.Where(x => userIds.Contains(x.Id)).Select(x => x.Id).ToArrayAsync();
        if (existing.Length == userIds.Count) return;
        var unitId = await db.OrganizationalUnits.Select(x => x.Id).FirstAsync();
        var now = DateTime.UtcNow;
        var index = 0;
        foreach (var id in userIds.Where(id => !existing.Contains(id)))
        {
            index++;
            db.Users.Add(User(id, "کاربر", $"مقیاس {index:00}", $"meeting-scale-{index:00}",
                unitId, now));
        }
        await db.SaveChangesAsync();
    }

    private static ApplicationUser User(Guid id, string first, string last, string name, Guid unitId,
        DateTime now) => new()
        {
            Id = id, UserName = name, NormalizedUserName = name.ToUpperInvariant(),
            Email = $"{name}@example.test", NormalizedEmail = $"{name}@example.test".ToUpperInvariant(),
            EmailConfirmed = true, FirstName = first, LastName = last, PhoneNumber = "+980000000000",
            PhoneNumberConfirmed = true, OrganizationalUnitId = unitId, IsActive = true,
            MustChangePassword = false, AccessVersion = 1, ProfilePhotoObjectKey = $"tests/{name}.png",
            ProfilePhotoContentType = "image/png", ProfilePhotoSize = 1,
            ProfilePhotoSha256 = new string('0', 64), SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"), CreatedAtUtc = now, UpdatedAtUtc = now
        };

    private static async Task<T> Post<T>(HttpClient source, string url, object payload,
        HttpStatusCode expected)
    {
        var response = await source.PostAsJsonAsync(url, payload);
        Assert.True(response.StatusCode == expected,
            $"Expected {expected}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<T> Put<T>(HttpClient source, string url, object payload)
    {
        var response = await source.PutAsJsonAsync(url, payload);
        Assert.True(response.IsSuccessStatusCode,
            $"PUT failed {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<IReadOnlyList<T>> ExecuteJsonBatch<T>(
        IReadOnlyList<Func<Task<HttpResponseMessage>>> operations, int batchSize = 25)
    {
        var values = new List<T>(operations.Count);
        foreach (var batch in operations.Chunk(batchSize))
        {
            var responses = await Task.WhenAll(batch.Select(operation => operation()));
            foreach (var response in responses)
            {
                using (response)
                {
                    Assert.True(response.StatusCode == HttpStatusCode.OK,
                        $"Expected OK, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
                    values.Add((await response.Content.ReadFromJsonAsync<T>())!);
                }
            }
        }
        return values;
    }

    private static async Task ExecuteSuccessBatch(IReadOnlyList<Func<Task<HttpResponseMessage>>> operations,
        int batchSize = 25)
    {
        foreach (var batch in operations.Chunk(batchSize))
        {
            var responses = await Task.WhenAll(batch.Select(operation => operation()));
            foreach (var response in responses)
            {
                using (response)
                    Assert.True(response.IsSuccessStatusCode,
                        $"Expected success, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            }
        }
    }

    private static void AssertGuestParticipantIsRedacted(JsonElement participant) =>
        Assert.Equal(new[] { "admissionStatus", "displayName", "id", "role" }, participant.EnumerateObject()
            .Select(property => property.Name).Order().ToArray());

    private static int CountOccurrences(string value, string fragment) =>
        value.Split(fragment, StringSplitOptions.None).Length - 1;

    private sealed class EfCommandCapture :
        IObserver<DiagnosticListener>,
        IObserver<KeyValuePair<string, object?>>,
        IDisposable
    {
        private readonly ConcurrentQueue<string> commands = new();
        private readonly List<IDisposable> listenerSubscriptions = [];
        private readonly IDisposable allListenersSubscription;
        private readonly object sync = new();
        private bool disposed;

        public EfCommandCapture()
        {
            allListenersSubscription = DiagnosticListener.AllListeners.Subscribe(this);
        }

        public IReadOnlyList<string> Commands => commands.ToArray();

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name != DbLoggerCategory.Name) return;
            lock (sync)
            {
                if (disposed) return;
                listenerSubscriptions.Add(listener.Subscribe(this,
                    eventName => eventName == RelationalEventId.CommandExecuting.Name));
            }
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Key == RelationalEventId.CommandExecuting.Name &&
                value.Value is CommandEventData eventData)
                commands.Enqueue(eventData.Command.CommandText);
        }

        public void OnCompleted() { }

        public void OnError(Exception error) { }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                foreach (var subscription in listenerSubscriptions) subscription.Dispose();
                listenerSubscriptions.Clear();
                allListenersSubscription.Dispose();
            }
        }
    }
}
