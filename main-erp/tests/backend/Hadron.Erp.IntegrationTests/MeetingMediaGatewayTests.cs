using System.Net;
using System.Text;
using System.Text.Json;
using Hadron.Erp.Api.Modules.Collaboration.Application;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hadron.Erp.IntegrationTests;

public sealed class MeetingMediaGatewayTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly LiveKitOptions Options = new()
    {
        ServerUrl = "wss://calls.hadron.example",
        ApiKey = "hadron-meetings",
        ApiSecret = "a-test-secret-that-is-longer-than-thirty-two-bytes"
    };

    [Fact]
    public void Join_token_contains_only_allowed_publish_sources_and_redacted_participant_metadata()
    {
        using var http = new HttpClient(new DelegateHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));
        var gateway = new LiveKitMeetingMediaGateway(http, Options, new FixedTimeProvider(Now),
            NullLogger<LiveKitMeetingMediaGateway>.Instance);
        var participantId = Guid.NewGuid();
        var participantIdentity = $"participant:{participantId:N}";

        var token = gateway.CreateJoinToken(new MeetingMediaTokenDescriptor("room-1", participantIdentity,
            "مهمان", participantId,
            new MeetingMediaCapabilities(Microphone: true, Camera: false, ScreenShare: true, Data: false),
            Now.AddHours(2).UtcDateTime));

        using var payload = JsonDocument.Parse(Decode(token.Split('.')[1]));
        var root = payload.RootElement;
        Assert.Equal(participantIdentity, root.GetProperty("sub").GetString());
        Assert.Equal(Now.AddMinutes(15).ToUnixTimeSeconds(), root.GetProperty("exp").GetInt64());
        var video = root.GetProperty("video");
        Assert.True(video.GetProperty("canPublish").GetBoolean());
        Assert.False(video.GetProperty("canPublishData").GetBoolean());
        Assert.Equal(new[] { "microphone", "screen_share", "screen_share_audio" },
            video.GetProperty("canPublishSources").EnumerateArray().Select(x => x.GetString()));
        Assert.DoesNotContain("camera", video.GetProperty("canPublishSources").GetRawText());

        using var metadata = JsonDocument.Parse(root.GetProperty("metadata").GetString()!);
        Assert.Equal(participantId, metadata.RootElement.GetProperty("participantId").GetGuid());
        Assert.Equal(new[] { "participantId" },
            metadata.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
    }

    [Fact]
    public async Task Room_delete_and_participant_remove_treat_provider_not_found_as_idempotent_success()
    {
        var requests = new List<(string Path, string Body, string? Authorization)>();
        using var http = new HttpClient(new DelegateHandler(async request =>
        {
            requests.Add((request.RequestUri!.AbsolutePath, await request.Content!.ReadAsStringAsync(),
                request.Headers.Authorization?.ToString()));
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        var gateway = new LiveKitMeetingMediaGateway(http, Options, new FixedTimeProvider(Now),
            NullLogger<LiveKitMeetingMediaGateway>.Instance);

        var removed = await gateway.RemoveParticipantAsync("room-2", "guest:stable", CancellationToken.None);
        var ended = await gateway.EndRoomAsync("room-2", CancellationToken.None);

        Assert.True(removed.Success);
        Assert.True(removed.AlreadyAbsent);
        Assert.True(ended.Success);
        Assert.True(ended.AlreadyAbsent);
        Assert.Collection(requests,
            request =>
            {
                Assert.Equal("/twirp/livekit.RoomService/RemoveParticipant", request.Path);
                Assert.Contains("\"identity\":\"guest:stable\"", request.Body);
                Assert.StartsWith("Bearer ", request.Authorization);
            },
            request =>
            {
                Assert.Equal("/twirp/livekit.RoomService/DeleteRoom", request.Path);
                Assert.Contains("\"room\":\"room-2\"", request.Body);
                Assert.StartsWith("Bearer ", request.Authorization);
            });
    }

    [Fact]
    public async Task Provider_admin_tokens_use_the_grant_required_by_each_livekit_operation()
    {
        var tokens = new Dictionary<string, string>();
        using var http = new HttpClient(new DelegateHandler(request =>
        {
            tokens[request.RequestUri!.AbsolutePath] = request.Headers.Authorization!.Parameter!;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }));
        var gateway = new LiveKitMeetingMediaGateway(http, Options, new FixedTimeProvider(Now),
            NullLogger<LiveKitMeetingMediaGateway>.Instance);

        await gateway.RemoveParticipantAsync("room-3", "guest:stable", CancellationToken.None);
        await gateway.EndRoomAsync("room-3", CancellationToken.None);

        using var removePayload = JsonDocument.Parse(Decode(tokens["/twirp/livekit.RoomService/RemoveParticipant"].Split('.')[1]));
        var removeGrant = removePayload.RootElement.GetProperty("video");
        Assert.True(removeGrant.GetProperty("roomAdmin").GetBoolean());
        Assert.Equal("room-3", removeGrant.GetProperty("room").GetString());

        using var deletePayload = JsonDocument.Parse(Decode(tokens["/twirp/livekit.RoomService/DeleteRoom"].Split('.')[1]));
        var deleteGrant = deletePayload.RootElement.GetProperty("video");
        Assert.True(deleteGrant.GetProperty("roomCreate").GetBoolean());
        Assert.False(deleteGrant.TryGetProperty("roomAdmin", out _));
    }

    private static byte[] Decode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '=');
        return Convert.FromBase64String(normalized);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request);
    }
}
