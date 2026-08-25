using System.Text.Json;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.PurchaseSupply.Domain;
using Microsoft.EntityFrameworkCore;

namespace Hadron.Erp.Api.Modules.Analytics.Application;

internal sealed record DeliveryScheduleAtCutoff(DeliverySchedule Source, decimal AllocatedQuantity);

internal static class AnalyticsCutoffContext
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static async Task<IReadOnlyList<DeliveryScheduleAtCutoff>> Schedules(
        HadronDbContext db, IReadOnlyCollection<DeliverySchedule> schedules, DateTime cutoff,
        CancellationToken ct)
    {
        if (schedules.Count == 0) return [];
        var lineIds = schedules.Select(x => x.PurchaseOrderLineId).Distinct().ToArray();
        var firstFutureEvents = await db.DeliveryScheduleAllocationEvents.AsNoTracking()
            .Where(x => lineIds.Contains(x.PurchaseOrderLineId) && x.CreatedAtUtc > cutoff)
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).ToListAsync(ct);
        var eventByLine = firstFutureEvents.GroupBy(x => x.PurchaseOrderLineId)
            .ToDictionary(x => x.Key, x => x.First());
        var result = new List<DeliveryScheduleAtCutoff>();

        foreach (var group in schedules.GroupBy(x => x.PurchaseOrderLineId))
        {
            if (!eventByLine.TryGetValue(group.Key, out var futureEvent))
            {
                result.AddRange(group.Where(x => x.CreatedAtUtc <= cutoff)
                    .Select(x => new DeliveryScheduleAtCutoff(x, x.AllocatedQuantity)));
                continue;
            }

            var before = JsonSerializer.Deserialize<ScheduleAllocationSnapshot[]>(futureEvent.BeforeJson, Json) ?? [];
            foreach (var snapshot in before)
            {
                var source = snapshot.Id.HasValue
                    ? group.SingleOrDefault(x => x.Id == snapshot.Id.Value)
                    : group.SingleOrDefault(x => x.ScheduleNumber == snapshot.ScheduleNumber);
                if (source is not null && source.CreatedAtUtc <= cutoff)
                    result.Add(new DeliveryScheduleAtCutoff(source, snapshot.AllocatedQuantity));
            }
        }

        return result;
    }

    private sealed class ScheduleAllocationSnapshot
    {
        public Guid? Id { get; init; }
        public int ScheduleNumber { get; init; }
        public decimal AllocatedQuantity { get; init; }
    }
}
