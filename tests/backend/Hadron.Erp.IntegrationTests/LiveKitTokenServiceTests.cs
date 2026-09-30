using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hadron.Erp.Api.Modules.Collaboration.Application;
using Microsoft.Extensions.Configuration;

namespace Hadron.Erp.IntegrationTests;

public sealed class LiveKitTokenServiceTests
{
    private static readonly Guid UserId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string Secret = "collaboration-test-secret-with-sufficient-entropy";

    [Fact]
    public void Join_token_is_scoped_signed_short_lived_and_connection_unique()
    {
        var service = new LiveKitTokenService(new LiveKitOptions
        {
            ServerUrl = "wss://calls.hadron.test",
            ApiKey = "hadron-test-key",
            ApiSecret = Secret
        }, TimeProvider.System);

        var token = service.CreateJoinToken("hadron-room-001",
            new CollaborationActor(UserId, "کاربر تست Hadron", false));
        var secondToken = service.CreateJoinToken("hadron-room-001",
            new CollaborationActor(UserId, "کاربر تست Hadron", false));
        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);

        using var payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));
        Assert.Equal("hadron-test-key", payload.RootElement.GetProperty("iss").GetString());
        var participantIdentity = payload.RootElement.GetProperty("sub").GetString();
        Assert.StartsWith($"{UserId:N}:", participantIdentity);
        Assert.Equal(payload.RootElement.GetProperty("jti").GetString(), participantIdentity![33..]);
        using var metadata = JsonDocument.Parse(payload.RootElement.GetProperty("metadata").GetString()!);
        Assert.Equal(UserId.ToString(), metadata.RootElement.GetProperty("userId").GetString());
        Assert.Equal(payload.RootElement.GetProperty("jti").GetString(),
            metadata.RootElement.GetProperty("connectionId").GetGuid().ToString("N"));
        using var secondPayload = JsonDocument.Parse(Base64UrlDecode(secondToken.Split('.')[1]));
        Assert.NotEqual(participantIdentity, secondPayload.RootElement.GetProperty("sub").GetString());
        Assert.Equal("hadron-room-001", payload.RootElement.GetProperty("video").GetProperty("room").GetString());
        var videoGrant = payload.RootElement.GetProperty("video");
        Assert.True(videoGrant.GetProperty("roomJoin").GetBoolean());
        Assert.True(videoGrant.GetProperty("canPublish").GetBoolean());
        Assert.True(videoGrant.GetProperty("canSubscribe").GetBoolean());
        Assert.True(videoGrant.GetProperty("canPublishData").GetBoolean());
        Assert.Equal(new[] { "camera", "microphone", "screen_share", "screen_share_audio" },
            videoGrant.GetProperty("canPublishSources").EnumerateArray().Select(source => source.GetString()));
        Assert.InRange(payload.RootElement.GetProperty("exp").GetInt64() -
            payload.RootElement.GetProperty("nbf").GetInt64(), 7_195, 7_205);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var expected = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}"));
        Assert.True(CryptographicOperations.FixedTimeEquals(expected, Base64UrlDecode(parts[2])));
    }

    [Theory]
    [InlineData("CHANGE_ME_livekit_api_key", "collaboration-test-secret-with-sufficient-entropy")]
    [InlineData("hadron-test-key", "CHANGE_ME_livekit_secret_that_is_long_enough_to_pass_length")]
    [InlineData("replace-me-key", "collaboration-test-secret-with-sufficient-entropy")]
    public void Configuration_rejects_placeholder_credentials(string apiKey, string apiSecret)
    {
        var options = new LiveKitOptions
        {
            ServerUrl = "wss://calls.hadron.test",
            ApiKey = apiKey,
            ApiSecret = apiSecret,
            RequireSecureWebSocket = true
        };

        Assert.False(options.Available);
        Assert.Contains("پیکربندی", options.UnavailabilityReason!);
    }

    [Fact]
    public void Configuration_requires_wss_in_production_only()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LIVEKIT_URL"] = "ws://192.168.0.175:7880",
            ["LIVEKIT_API_KEY"] = "hadron-test-key",
            ["LIVEKIT_API_SECRET"] = Secret
        }).Build();

        var development = LiveKitOptions.From(configuration, requireSecureWebSocket: false);
        Assert.True(development.Available);

        var production = LiveKitOptions.From(configuration, requireSecureWebSocket: true);
        Assert.False(production.Available);
        Assert.Contains("wss://", production.UnavailabilityReason!);
        Assert.Contains("wss://", new LiveKitTokenService(production, TimeProvider.System).Configuration().Reason!);

        configuration["LIVEKIT_URL"] = "wss://calls.hadron.test";
        Assert.True(LiveKitOptions.From(configuration, requireSecureWebSocket: true).Available);
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}
