using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Hadron.Erp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Hadron.Erp.Api.Platform.Audit;

public sealed record AuditEvent(
    string EntityType,
    Guid EntityId,
    string Action,
    DateTime TimestampUtc,
    string? ActorId,
    string? CorrelationId,
    IReadOnlyDictionary<string, object?>? OldValues,
    IReadOnlyDictionary<string, object?>? NewValues,
    string? Reason,
    string Source = "UI");

public interface IAuditEventSink
{
    Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken);
}

public sealed class PostgresAuditEventSink(
    IDbContextFactory<HadronDbContext> contextFactory,
    ILogger<PostgresAuditEventSink> logger) : IAuditEventSink
{
    public async Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        var storedEvent = OperationalAuditEvent.From(auditEvent);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        db.OperationalAuditEvents.Add(storedEvent);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogDebug("Persisted audit event {AuditEventId} for {EntityType}/{EntityId}.",
            storedEvent.Id, storedEvent.EntityType, storedEvent.EntityId);
    }
}

public sealed class OperationalAuditEvent
{
    private OperationalAuditEvent() { }

    internal OperationalAuditEvent(string entityType, Guid entityId, string action, DateTime occurredAtUtc,
        string? actorId, string? correlationId, string? previousValuesJson, string? currentValuesJson,
        string? reason, string source)
    {
        Id = Guid.NewGuid();
        EntityType = entityType;
        EntityId = entityId;
        Action = action;
        OccurredAtUtc = occurredAtUtc;
        ActorId = actorId;
        CorrelationId = correlationId;
        PreviousValuesJson = previousValuesJson;
        CurrentValuesJson = currentValuesJson;
        Reason = reason;
        Source = source;
    }

    internal static OperationalAuditEvent From(AuditEvent auditEvent) => new(
            AuditPayloadSanitizer.CleanIdentifier(auditEvent.EntityType, 120, "Unknown"),
            auditEvent.EntityId,
            AuditPayloadSanitizer.CleanIdentifier(auditEvent.Action, 120, "UNKNOWN"),
            auditEvent.TimestampUtc.Kind == DateTimeKind.Utc
                ? auditEvent.TimestampUtc
                : auditEvent.TimestampUtc.ToUniversalTime(),
            AuditPayloadSanitizer.CleanNullable(auditEvent.ActorId, 200),
            AuditPayloadSanitizer.CleanNullable(auditEvent.CorrelationId, 200),
            AuditPayloadSanitizer.Serialize(auditEvent.OldValues),
            AuditPayloadSanitizer.Serialize(auditEvent.NewValues),
            AuditPayloadSanitizer.CleanFreeText(auditEvent.Reason, 1000),
            AuditPayloadSanitizer.CleanIdentifier(auditEvent.Source, 50, "UI"));

    public Guid Id { get; private set; }
    public string EntityType { get; private set; } = string.Empty;
    public Guid EntityId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public DateTime OccurredAtUtc { get; private set; }
    public string? ActorId { get; private set; }
    public string? CorrelationId { get; private set; }
    public string? PreviousValuesJson { get; private set; }
    public string? CurrentValuesJson { get; private set; }
    public string? Reason { get; private set; }
    public string Source { get; private set; } = string.Empty;
}

internal static partial class AuditPayloadSanitizer
{
    private const int MaxPayloadCharacters = 16_000;
    private const int MaxScalarCharacters = 2_000;
    private const int MaxProperties = 100;
    private const int MaxArrayItems = 100;
    private const int MaxDepth = 12;
    private const string Redacted = "[REDACTED]";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
    };

    public static string? Serialize(IReadOnlyDictionary<string, object?>? values)
    {
        if (values is null) return null;

        JsonNode? root;
        try
        {
            root = JsonSerializer.SerializeToNode(values, JsonOptions);
        }
        catch (JsonException)
        {
            root = new JsonObject { ["serializationError"] = true };
        }
        catch (NotSupportedException)
        {
            root = new JsonObject { ["serializationError"] = true };
        }

        var sanitized = SanitizeNode(root, null, 0);
        var json = sanitized?.ToJsonString(JsonOptions) ?? "null";
        if (json.Length <= MaxPayloadCharacters) return json;

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        return new JsonObject
        {
            ["truncated"] = true,
            ["sha256"] = hash,
            ["preview"] = json[..Math.Min(8_000, json.Length)]
        }.ToJsonString(JsonOptions);
    }

    public static string CleanIdentifier(string? value, int maxLength, string fallback)
    {
        var clean = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return clean[..Math.Min(clean.Length, maxLength)];
    }

    public static string? CleanNullable(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, maxLength)];

    public static string? CleanFreeText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var clean = InlineSecretPattern().Replace(value.Trim(), "$1=" + Redacted);
        return clean[..Math.Min(clean.Length, maxLength)];
    }

    private static JsonNode? SanitizeNode(JsonNode? node, string? propertyName, int depth)
    {
        if (propertyName is not null && SensitiveNamePattern().IsMatch(propertyName))
            return JsonValue.Create(Redacted);
        if (node is null) return null;
        if (depth >= MaxDepth) return JsonValue.Create("[MAX_DEPTH]");

        if (node is JsonObject sourceObject)
        {
            var result = new JsonObject();
            var count = 0;
            foreach (var pair in sourceObject)
            {
                if (count++ >= MaxProperties)
                {
                    result["_truncatedProperties"] = true;
                    break;
                }
                result[pair.Key] = SanitizeNode(pair.Value, pair.Key, depth + 1);
            }
            return result;
        }

        if (node is JsonArray sourceArray)
        {
            var result = new JsonArray();
            for (var index = 0; index < Math.Min(sourceArray.Count, MaxArrayItems); index++)
                result.Add(SanitizeNode(sourceArray[index], null, depth + 1));
            if (sourceArray.Count > MaxArrayItems) result.Add("[TRUNCATED_ITEMS]");
            return result;
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var text))
            return JsonValue.Create(CleanFreeText(text, MaxScalarCharacters));
        return node.DeepClone();
    }

    [GeneratedRegex("(?i)(password|passphrase|pwd|secret|token|authorization|cookie|credential|api[-_]?key|connection[-_]?string)", RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveNamePattern();

    [GeneratedRegex("(?i)\\b(password|passphrase|pwd|secret|token|authorization|cookie|credential|api[-_]?key|connection[-_]?string)\\s*=\\s*[^\\s;,]+", RegexOptions.CultureInvariant)]
    private static partial Regex InlineSecretPattern();
}

public static class AuditEventContext
{
    public static string? ActorId(HttpContext? context) =>
        context?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context?.User.Identity?.Name;

    public static string? CorrelationId(HttpContext? context) => context?.TraceIdentifier;
}
