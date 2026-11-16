using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hadron.Erp.Api.Modules.Collaboration.Domain;

namespace Hadron.Erp.Api.Modules.Collaboration.Application;

public sealed record MeetingMediaCapabilities(
    bool Microphone,
    bool Camera,
    bool ScreenShare,
    bool Data);

public sealed record MeetingMediaTokenDescriptor(
    string RoomName,
    string ParticipantIdentity,
    string ParticipantName,
    Guid ParticipantId,
    MeetingMediaCapabilities Capabilities,
    DateTime ExpiresAtUtc);

public sealed record MeetingRoomEndResult(bool Success, bool AlreadyAbsent, string? FailureCode)
{
    public static MeetingRoomEndResult Ended() => new(true, false, null);
    public static MeetingRoomEndResult Absent() => new(true, true, null);
    public static MeetingRoomEndResult Failed(string code) => new(false, false, code);
}

public sealed record MeetingParticipantRemoveResult(bool Success, bool AlreadyAbsent, string? FailureCode)
{
    public static MeetingParticipantRemoveResult Removed() => new(true, false, null);
    public static MeetingParticipantRemoveResult Absent() => new(true, true, null);
    public static MeetingParticipantRemoveResult Failed(string code) => new(false, false, code);
}

public interface IMeetingMediaGateway
{
    bool Available { get; }
    string? UnavailabilityReason { get; }
    string ServerUrl { get; }
    string CreateJoinToken(MeetingMediaTokenDescriptor descriptor);
    Task<MeetingRoomEndResult> EndRoomAsync(string roomName, CancellationToken cancellationToken);
    Task<MeetingParticipantRemoveResult> RemoveParticipantAsync(string roomName, string participantIdentity,
        CancellationToken cancellationToken);
}

public sealed class LiveKitMeetingMediaGateway(
    HttpClient httpClient,
    LiveKitOptions options,
    TimeProvider timeProvider,
    ILogger<LiveKitMeetingMediaGateway> logger) : IMeetingMediaGateway
{
    public bool Available => options.Available;
    public string? UnavailabilityReason => options.UnavailabilityReason;
    public string ServerUrl => options.ServerUrl;

    public string CreateJoinToken(MeetingMediaTokenDescriptor descriptor)
    {
        EnsureAvailable();
        var now = timeProvider.GetUtcNow();
        var requestedExpiry = new DateTimeOffset(DateTime.SpecifyKind(descriptor.ExpiresAtUtc, DateTimeKind.Utc));
        var expiry = requestedExpiry < now.AddMinutes(15) ? requestedExpiry : now.AddMinutes(15);
        if (expiry <= now) throw new MeetingGuestAccessException();

        var publishSources = new List<string>(4);
        if (descriptor.Capabilities.Microphone) publishSources.Add("microphone");
        if (descriptor.Capabilities.Camera) publishSources.Add("camera");
        if (descriptor.Capabilities.ScreenShare)
        {
            publishSources.Add("screen_share");
            publishSources.Add("screen_share_audio");
        }

        var metadata = JsonSerializer.Serialize(new
        {
            participantId = descriptor.ParticipantId
        });
        return Sign(new
        {
            iss = options.ApiKey,
            sub = descriptor.ParticipantIdentity,
            jti = Guid.NewGuid().ToString("N"),
            name = descriptor.ParticipantName,
            metadata,
            nbf = now.ToUnixTimeSeconds() - 5,
            exp = expiry.ToUnixTimeSeconds(),
            video = new
            {
                room = descriptor.RoomName,
                roomJoin = true,
                canPublish = publishSources.Count > 0,
                canSubscribe = true,
                canPublishData = descriptor.Capabilities.Data,
                canPublishSources = publishSources
            }
        });
    }

    public async Task<MeetingRoomEndResult> EndRoomAsync(string roomName, CancellationToken cancellationToken)
    {
        EnsureAvailable();
        var now = timeProvider.GetUtcNow();
        var adminToken = Sign(new
        {
            iss = options.ApiKey,
            sub = "hadron-erp-room-management",
            jti = Guid.NewGuid().ToString("N"),
            nbf = now.ToUnixTimeSeconds() - 5,
            exp = now.AddMinutes(2).ToUnixTimeSeconds(),
            // LiveKit requires roomCreate (not roomAdmin) for DeleteRoom. The token is
            // deliberately short-lived and used only by this server-side operation.
            video = new { roomCreate = true }
        });
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(ApiBaseUri(), "/twirp/livekit.RoomService/DeleteRoom"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        request.Content = JsonContent.Create(new { room = roomName });
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode) return MeetingRoomEndResult.Ended();
            if (response.StatusCode == HttpStatusCode.NotFound) return MeetingRoomEndResult.Absent();
            logger.LogWarning("LiveKit DeleteRoom failed for {RoomName} with HTTP {StatusCode}.",
                roomName, (int)response.StatusCode);
            return MeetingRoomEndResult.Failed($"HTTP_{(int)response.StatusCode}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "LiveKit DeleteRoom failed for {RoomName}.", roomName);
            return MeetingRoomEndResult.Failed("PROVIDER_UNREACHABLE");
        }
    }

    public async Task<MeetingParticipantRemoveResult> RemoveParticipantAsync(string roomName,
        string participantIdentity, CancellationToken cancellationToken)
    {
        EnsureAvailable();
        using var request = CreateRoomAdminRequest("/twirp/livekit.RoomService/RemoveParticipant", roomName,
            new { room = roomName, identity = participantIdentity });
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode) return MeetingParticipantRemoveResult.Removed();
            if (response.StatusCode == HttpStatusCode.NotFound) return MeetingParticipantRemoveResult.Absent();
            logger.LogWarning(
                "LiveKit RemoveParticipant failed for {RoomName}/{ParticipantIdentity} with HTTP {StatusCode}.",
                roomName, participantIdentity, (int)response.StatusCode);
            return MeetingParticipantRemoveResult.Failed($"HTTP_{(int)response.StatusCode}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "LiveKit RemoveParticipant failed for {RoomName}/{ParticipantIdentity}.", roomName,
                participantIdentity);
            return MeetingParticipantRemoveResult.Failed("PROVIDER_UNREACHABLE");
        }
    }

    private HttpRequestMessage CreateRoomAdminRequest(string path, string roomName, object content)
    {
        var now = timeProvider.GetUtcNow();
        var adminToken = Sign(new
        {
            iss = options.ApiKey,
            sub = "hadron-erp-room-management",
            jti = Guid.NewGuid().ToString("N"),
            nbf = now.ToUnixTimeSeconds() - 5,
            exp = now.AddMinutes(2).ToUnixTimeSeconds(),
            video = new { room = roomName, roomAdmin = true }
        });
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(ApiBaseUri(), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        request.Content = JsonContent.Create(content);
        return request;
    }

    private Uri ApiBaseUri()
    {
        var source = new Uri(options.ServerUrl);
        var builder = new UriBuilder(source)
        {
            Scheme = source.Scheme == "wss" ? Uri.UriSchemeHttps : Uri.UriSchemeHttp,
            Path = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri;
    }

    private string Sign(object payload)
    {
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
        var body = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var unsigned = $"{header}.{body}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(options.ApiSecret));
        return $"{unsigned}.{Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(unsigned)))}";
    }

    private void EnsureAvailable()
    {
        if (!options.Available)
            throw new CollaborationConflictException("CALL_PROVIDER_NOT_CONFIGURED",
                options.UnavailabilityReason ?? "سرویس تماس پیکربندی نشده است.");
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=')
        .Replace('+', '-').Replace('/', '_');
}
