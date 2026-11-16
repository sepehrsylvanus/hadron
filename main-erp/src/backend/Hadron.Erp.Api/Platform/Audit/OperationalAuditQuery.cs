using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Platform.Security;
using Microsoft.EntityFrameworkCore;

namespace Hadron.Erp.Api.Platform.Audit;

public sealed record OperationalAuditRecord(
    Guid Id,
    string EntityType,
    Guid EntityId,
    string Action,
    DateTime OccurredAtUtc,
    string? ActorId,
    string? CorrelationId,
    string? Reason,
    string Source,
    bool HasPreviousValues,
    bool HasCurrentValues);

public sealed record OperationalAuditPage(
    IReadOnlyList<OperationalAuditRecord> Items,
    int Total,
    int Page,
    int PageSize);

public sealed class OperationalAuditQuery(IDbContextFactory<HadronDbContext> contextFactory)
{
    public async Task<OperationalAuditPage> Search(string? search, string? entityType, string? action, string? correlationId,
        DateTime? fromUtc, DateTime? toUtc, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var from = NormalizeUtc(fromUtc);
        var to = NormalizeUtc(toUtc);
        if (from.HasValue && to.HasValue && to < from)
            throw new BadHttpRequestException("The end date must not precede the start date.");

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.OperationalAuditEvents.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            if (term.Length > 100) term = term[..100];
            var pattern = $"%{EscapeLike(term)}%";
            query = query.Where(x =>
                EF.Functions.ILike(x.EntityType, pattern, "\\") ||
                EF.Functions.ILike(x.Action, pattern, "\\") ||
                (x.ActorId != null && EF.Functions.ILike(x.ActorId, pattern, "\\")) ||
                (x.CorrelationId != null && EF.Functions.ILike(x.CorrelationId, pattern, "\\")) ||
                (x.Reason != null && EF.Functions.ILike(x.Reason, pattern, "\\")));
        }
        if (!string.IsNullOrWhiteSpace(entityType))
        {
            var value = entityType.Trim();
            query = query.Where(x => EF.Functions.ILike(x.EntityType, value));
        }
        if (!string.IsNullOrWhiteSpace(action))
        {
            var value = action.Trim();
            query = query.Where(x => EF.Functions.ILike(x.Action, value));
        }
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            var value = correlationId.Trim();
            query = query.Where(x => x.CorrelationId == value);
        }
        if (from.HasValue) query = query.Where(x => x.OccurredAtUtc >= from.Value);
        if (to.HasValue) query = query.Where(x => x.OccurredAtUtc <= to.Value);

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new OperationalAuditRecord(x.Id, x.EntityType, x.EntityId, x.Action,
                x.OccurredAtUtc, x.ActorId, x.CorrelationId, x.Reason, x.Source,
                x.PreviousValuesJson != null, x.CurrentValuesJson != null))
            .ToListAsync(cancellationToken);
        return new(items, total, page, pageSize);
    }

    private static DateTime? NormalizeUtc(DateTime? value)
    {
        if (!value.HasValue) return null;
        return value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        };
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}

public static class OperationalAuditEndpoints
{
    public static void MapOperationalAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/operational-audit",
            (string? search, string? entityType, string? action, string? correlationId, DateTime? fromUtc, DateTime? toUtc,
                int? page, int? pageSize, OperationalAuditQuery query, CancellationToken cancellationToken) =>
                query.Search(search, entityType, action, correlationId, fromUtc, toUtc, page ?? 1, pageSize ?? 25,
                    cancellationToken))
            .WithTags("Operational Audit")
            .RequirePermission(Permissions.IdentitySecurityAuditView);
    }
}
