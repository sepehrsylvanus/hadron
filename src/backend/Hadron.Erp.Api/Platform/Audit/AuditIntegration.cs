using System.Security.Claims;

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
    string? Reason);

public interface IAuditEventSink
{
    Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken);
}

public sealed class NoOpAuditEventSink : IAuditEventSink
{
    public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken) => Task.CompletedTask;
}

public static class AuditEventContext
{
    public static string? ActorId(HttpContext? context) =>
        context?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context?.User.Identity?.Name;

    public static string? CorrelationId(HttpContext? context) => context?.TraceIdentifier;
}
