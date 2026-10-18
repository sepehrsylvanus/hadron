using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Bom.Domain;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace Hadron.Erp.Api.Modules.Bom.Application;

public sealed record BomEffectiveInterval(DateOnly StartDate, DateOnly EndDate);

public sealed record BomOperationalCoverage(
    IReadOnlyList<BomEffectiveInterval> Intervals,
    IReadOnlyList<string> BlockingCodes);

public interface IBomOperationalReadinessQuery
{
    Task<IReadOnlyDictionary<Guid, BomOperationalCoverage>> Evaluate(
        IReadOnlyCollection<Guid> parentItemIds, DateOnly from, DateOnly to, CancellationToken ct);
}

public sealed class BomOperationalReadinessQuery(IDbContextFactory<HadronDbContext> factory)
    : IBomOperationalReadinessQuery
{
    public async Task<IReadOnlyDictionary<Guid, BomOperationalCoverage>> Evaluate(
        IReadOnlyCollection<Guid> parentItemIds, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var ids = parentItemIds.Distinct().ToArray();
        var revisions = await db.BomRevisions.AsNoTracking()
            .Include(x => x.ParentItem)
            .Include(x => x.Lines).ThenInclude(x => x.ChildItem)
            .Where(x => ids.Contains(x.ParentItemId) && x.Status == BomStatuses.Approved)
            .OrderBy(x => x.ParentItemId)
            .ToListAsync(ct);

        return ids.ToDictionary(parentId => parentId, parentId =>
        {
            var relevant = revisions.Where(x => x.ParentItemId == parentId).ToList();
            var blockers = new HashSet<string>(StringComparer.Ordinal);
            var intervals = new List<BomEffectiveInterval>();
            foreach (var revision in relevant)
            {
                if (!revision.ParentItem.IsActive) blockers.Add("PARENT_INACTIVE");
                if (revision.ParentItem.BaseUnitId != revision.ParentBaseUnitId)
                    blockers.Add("BASE_UNIT_CHANGED");
                if (revision.Lines.Count == 0) blockers.Add("EMPTY_BOM");
                foreach (var line in revision.Lines)
                {
                    if (!line.ChildItem.IsActive) blockers.Add("CHILD_INACTIVE");
                    if (line.ChildItem.BaseUnitId != line.ChildBaseUnitId)
                        blockers.Add("BASE_UNIT_CHANGED");
                    if (!ValidRelationship(revision.ParentItem.ItemType, line.ChildItem.ItemType))
                        blockers.Add("INVALID_ITEM_RELATIONSHIP");
                }

                intervals.Add(new(from, to));
            }

            return new BomOperationalCoverage(intervals, blockers.Order().ToArray());
        });
    }

    private static bool ValidRelationship(string parentType, string childType) =>
        parentType == ItemTypes.Product && childType == ItemTypes.Part ||
        parentType == ItemTypes.Part && childType == ItemTypes.Component ||
        parentType == ItemTypes.Component && childType == ItemTypes.Component;
}
