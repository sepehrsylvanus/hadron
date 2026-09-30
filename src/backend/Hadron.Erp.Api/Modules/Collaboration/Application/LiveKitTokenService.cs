using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hadron.Erp.Api.Modules.Collaboration.Domain;

namespace Hadron.Erp.Api.Modules.Collaboration.Application;

public sealed class LiveKitOptions
{
    public string ServerUrl { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public string ApiSecret { get; init; } = string.Empty;
    public bool RequireSecureWebSocket { get; init; }
    public bool Available => UnavailabilityReason is null;
    public string? UnavailabilityReason
    {
        get
        {
            if (!Uri.TryCreate(ServerUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("ws" or "wss"))
                return "نشانی سرویس تماس معتبر نیست.";
            if (RequireSecureWebSocket && uri.Scheme != "wss")
                return "در محیط عملیاتی، نشانی سرویس تماس باید با wss:// پیکربندی شود.";
            if (ApiKey.Trim().Length < 3 || Encoding.UTF8.GetByteCount(ApiSecret) < 32 ||
                IsPlaceholder(ApiKey) || IsPlaceholder(ApiSecret))
                return "کلیدهای امن سرویس تماس پیکربندی نشده‌اند.";
            return null;
        }
    }

    private static bool IsPlaceholder(string value)
    {
        var normalized = value.Trim().Replace("-", "_", StringComparison.Ordinal).ToUpperInvariant();
        return normalized.Contains("CHANGE_ME", StringComparison.Ordinal) ||
               normalized.Contains("REPLACE_ME", StringComparison.Ordinal) ||
               normalized.Contains("YOUR_SECRET", StringComparison.Ordinal);
    }

    public static LiveKitOptions From(IConfiguration configuration, bool requireSecureWebSocket) => new()
    {
        ServerUrl = configuration["LiveKit:ServerUrl"] ?? configuration["LIVEKIT_URL"] ?? string.Empty,
        ApiKey = configuration["LiveKit:ApiKey"] ?? configuration["LIVEKIT_API_KEY"] ?? string.Empty,
        ApiSecret = configuration["LiveKit:ApiSecret"] ?? configuration["LIVEKIT_API_SECRET"] ?? string.Empty,
        RequireSecureWebSocket = requireSecureWebSocket
    };
}

public sealed class LiveKitTokenService(LiveKitOptions options, TimeProvider timeProvider)
{
    public CallConfigurationResponse Configuration() => options.Available
        ? new(true, options.ServerUrl, "LIVEKIT", null)
        : new(false, null, "LIVEKIT", options.UnavailabilityReason);

    public string CreateJoinToken(string roomName, CollaborationActor actor)
    {
        if (!options.Available) throw new CollaborationConflictException(
            "CALL_PROVIDER_NOT_CONFIGURED", options.UnavailabilityReason!);
        var now = timeProvider.GetUtcNow();
        var connectionId = Guid.NewGuid();
        var participantIdentity = $"{actor.UserId:N}:{connectionId:N}";
        var participantMetadata = JsonSerializer.Serialize(new
        {
            userId = actor.UserId,
            connectionId
        });
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = options.ApiKey,
            sub = participantIdentity,
            jti = connectionId.ToString("N"),
            name = actor.DisplayName,
            metadata = participantMetadata,
            nbf = now.ToUnixTimeSeconds() - 5,
            exp = now.AddHours(2).ToUnixTimeSeconds(),
            video = new
            {
                room = roomName,
                roomJoin = true,
                canPublish = true,
                canSubscribe = true,
                canPublishData = true,
                canPublishSources = new[] { "camera", "microphone", "screen_share", "screen_share_audio" }
            }
        }));
        var unsigned = $"{header}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(options.ApiSecret));
        return $"{unsigned}.{Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(unsigned)))}";
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
