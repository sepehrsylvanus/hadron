using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Bom.Domain;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace Hadron.Erp.Api.Modules.Bom.Application;

public sealed class BomStructureService(IDbContextFactory<HadronDbContext> factory)
{
    public async Task<BomBuilderTreeResponse> GetBuilderTree(Guid rootItemId, Guid? rootRevisionId,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Items.AsNoTracking().AnyAsync(x => x.Id == rootItemId, ct))
            throw new KeyNotFoundException();

        var selected = new Dictionary<Guid, BuilderRevision>();
        var missing = new HashSet<Guid>();
        var expanded = new HashSet<Guid>();
        var frontier = new HashSet<Guid> { rootItemId };

        while (frontier.Count > 0)
        {
            var parentIds = frontier.Where(expanded.Add).ToArray();
            frontier.Clear();
            if (parentIds.Length == 0) break;

            var candidates = await db.BomRevisions.AsNoTracking()
                .Where(x => parentIds.Contains(x.ParentItemId))
                .Select(x => new BuilderRevision(x.Id, x.ParentItemId, x.ParentBaseUnitId,
                    x.RevisionCode, x.Status, x.BaseOutputQty, x.Version, x.CreatedAtUtc,
                    x.Lines.OrderBy(line => line.SortOrder).ThenBy(line => line.Id)
                        .Select(line => new BuilderLine(line.Id, line.ChildItemId,
                            line.ChildBaseUnitId, line.UsageQty, line.ScrapOverridePercent,
                            line.SortOrder)).ToArray()))
                .ToListAsync(ct);

            foreach (var parentId in parentIds)
            {
                var parentCandidates = candidates.Where(x => x.ParentItemId == parentId).ToArray();
                BuilderRevision? revision;
                if (parentId == rootItemId && rootRevisionId.HasValue)
                {
                    revision = parentCandidates.SingleOrDefault(x => x.Id == rootRevisionId.Value)
                        ?? throw new KeyNotFoundException();
                }
                else
                {
                    revision = parentCandidates
                        .OrderByDescending(x => x.Status == BomStatuses.Draft)
                        .ThenByDescending(x => x.CreatedAtUtc)
                        .FirstOrDefault();
                }

                if (revision is null)
                {
                    missing.Add(parentId);
                    continue;
                }

                selected[parentId] = revision;
                foreach (var childId in revision.Lines.Select(x => x.ChildItemId))
                    if (!expanded.Contains(childId)) frontier.Add(childId);
            }
        }

        var itemIds = selected.Values.SelectMany(x => x.Lines.Select(line => line.ChildItemId))
            .Concat(selected.Keys).Append(rootItemId).Distinct().ToArray();
        var items = await LoadItems(db, itemIds, ct);
        var unitIds = selected.Values.Select(x => x.ParentBaseUnitId)
            .Concat(selected.Values.SelectMany(x => x.Lines.Select(line => line.ChildBaseUnitId)))
            .Distinct().ToArray();
        var units = await LoadUnits(db, unitIds, ct);
        var defaultScrap = await db.MaterialItemProfiles.AsNoTracking()
            .Where(x => itemIds.Contains(x.ItemId))
            .ToDictionaryAsync(x => x.ItemId, x => x.DefaultPlanningScrapPercent, ct);

        var revisions = selected.Values.OrderBy(x => x.CreatedAtUtc).Select(revision =>
            new BomBuilderRevisionResponse(revision.Id, revision.RevisionCode,
                items[revision.ParentItemId], units[revision.ParentBaseUnitId], revision.Status,
                revision.BaseOutputQty, revision.Version,
                revision.Lines.Select(line => new BomBuilderLineResponse(line.Id,
                    items[line.ChildItemId], units[line.ChildBaseUnitId], line.UsageQty,
                    line.ScrapOverridePercent,
                    line.ScrapOverridePercent ?? defaultScrap.GetValueOrDefault(line.ChildItemId),
                    line.SortOrder,
                    items[line.ChildItemId].CurrentBaseUnit.Id == line.ChildBaseUnitId
                        ? [] : ["BASE_UNIT_CHANGED"])).ToArray())).ToArray();

        return new(items[rootItemId], revisions, missing.Order().ToArray());
    }

    public async Task<BomProductTreeResponse> GetProductTree(Guid rootItemId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Items.AsNoTracking().AnyAsync(x => x.Id == rootItemId, ct))
            throw new KeyNotFoundException();

        var revisions = await LoadReachableRevisions(db, rootItemId, ct);
        var itemIds = revisions.Values.SelectMany(x => x.Lines.Select(line => line.ChildItemId))
            .Append(rootItemId).Distinct().ToArray();
        var items = await LoadItems(db, itemIds, ct);
        var units = await LoadUnits(db, revisions.Values.SelectMany(x => x.Lines)
            .Select(x => x.ChildBaseUnitId).Distinct().ToArray(), ct);
        var root = BuildNode(rootItemId, null, revisions, items, units, []);
        return new(root);
    }

    public async Task<BomWhereUsedResponse> GetWhereUsed(Guid itemId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Items.AsNoTracking().AnyAsync(x => x.Id == itemId, ct))
            throw new KeyNotFoundException();

        var direct = new List<ReleasedLine>();
        var allParents = new Dictionary<Guid, ReleasedLine>();
        var frontier = new HashSet<Guid> { itemId };
        var visited = new HashSet<Guid> { itemId };
        var firstLevel = true;

        while (frontier.Count > 0)
        {
            var childIds = frontier.ToArray();
            var parentRows = await db.BomLines.AsNoTracking()
                .Where(x => childIds.Contains(x.ChildItemId) && x.BomRevision.Status == BomStatuses.Approved)
                .Select(x => new ReleasedLine(x.BomRevisionId, x.BomRevision.ParentItemId,
                    x.BomRevision.RevisionCode, x.BomRevision.BaseOutputQty,
                    x.ChildItemId, x.ChildBaseUnitId, x.UsageQty, x.ScrapOverridePercent))
                .ToListAsync(ct);

            if (firstLevel) direct.AddRange(parentRows.Where(x => x.ChildItemId == itemId));
            frontier.Clear();
            foreach (var row in parentRows)
            {
                allParents.TryAdd(row.ParentItemId, row);
                if (visited.Add(row.ParentItemId)) frontier.Add(row.ParentItemId);
            }
            firstLevel = false;
        }

        var ids = allParents.Keys.Append(itemId).Distinct().ToArray();
        var items = await LoadItems(db, ids, ct);
        var units = await LoadUnits(db, direct.Select(x => x.ChildBaseUnitId).Distinct().ToArray(), ct);
        var directParents = direct.OrderBy(x => items[x.ParentItemId].Code, StringComparer.Ordinal)
            .Select(x => new BomWhereUsedParent(items[x.ParentItemId], Revision(x), Relationship(x, units)))
            .ToArray();
        var products = allParents.Keys.Where(x => items[x].ItemType == ItemTypes.Product)
            .Select(x => items[x]).OrderBy(x => x.Code, StringComparer.Ordinal).ToArray();
        return new(items[itemId], directParents, products);
    }

    private static async Task<Dictionary<Guid, ReleasedRevision>> LoadReachableRevisions(HadronDbContext db,
        Guid rootItemId, CancellationToken ct)
    {
        var result = new Dictionary<Guid, ReleasedRevision>();
        var frontier = new HashSet<Guid> { rootItemId };
        var expanded = new HashSet<Guid>();
        while (frontier.Count > 0)
        {
            var parentIds = frontier.Where(expanded.Add).ToArray();
            frontier.Clear();
            if (parentIds.Length == 0) break;
            var rows = await db.BomRevisions.AsNoTracking()
                .Where(x => parentIds.Contains(x.ParentItemId) && x.Status == BomStatuses.Approved)
                .Select(x => new ReleasedRevision(x.Id, x.ParentItemId, x.RevisionCode, x.BaseOutputQty,
                    x.Lines.OrderBy(line => line.SortOrder).ThenBy(line => line.Id)
                        .Select(line => new ReleasedLine(x.Id, x.ParentItemId, x.RevisionCode, x.BaseOutputQty,
                            line.ChildItemId, line.ChildBaseUnitId,
                            line.UsageQty, line.ScrapOverridePercent)).ToArray()))
                .ToListAsync(ct);

            foreach (var group in rows.GroupBy(x => x.ParentItemId))
            {
                if (group.Count() != 1)
                    throw new BomConflictException("BOM_EFFECTIVITY_OVERLAP",
                        "بیش از یک BOM تأییدشده در تاریخ جاری مؤثر است.");
                var revision = group.Single();
                result.Add(revision.ParentItemId, revision);
                foreach (var childId in revision.Lines.Select(x => x.ChildItemId))
                    if (!expanded.Contains(childId)) frontier.Add(childId);
            }
        }
        return result;
    }

    private static async Task<Dictionary<Guid, BomStructureItemRef>> LoadItems(HadronDbContext db,
        IReadOnlyCollection<Guid> itemIds, CancellationToken ct)
    {
        var items = await db.Items.AsNoTracking().Where(x => itemIds.Contains(x.Id))
            .Include(x => x.BaseUnit).ToListAsync(ct);
        var profiles = await db.MaterialItemProfiles.AsNoTracking().Where(x => itemIds.Contains(x.ItemId))
            .ToDictionaryAsync(x => x.ItemId, ct);
        return items.ToDictionary(x => x.Id, x =>
        {
            profiles.TryGetValue(x.Id, out var profile);
            return new BomStructureItemRef(x.Id, x.BusinessNumber, x.NameFa, x.NameEn, x.ItemType,
                x.IsActive, profile?.SupplyMethod, profile?.NetWeightKg,
                new BomUnitRef(x.BaseUnit.Id, x.BaseUnit.Code, x.BaseUnit.NameFa, x.BaseUnit.NameEn,
                    x.BaseUnit.Symbol, x.BaseUnit.IsActive));
        });
    }

    private static async Task<Dictionary<Guid, BomUnitRef>> LoadUnits(HadronDbContext db,
        IReadOnlyCollection<Guid> unitIds, CancellationToken ct) =>
        await db.Units.AsNoTracking().Where(x => unitIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id,
            x => new BomUnitRef(x.Id, x.Code, x.NameFa, x.NameEn, x.Symbol, x.IsActive), ct);

    private static BomTreeNode BuildNode(Guid itemId, ReleasedLine? relationship,
        IReadOnlyDictionary<Guid, ReleasedRevision> revisions,
        IReadOnlyDictionary<Guid, BomStructureItemRef> items, IReadOnlyDictionary<Guid, BomUnitRef> units,
        HashSet<Guid> path)
    {
        if (!path.Add(itemId))
            throw new BomConflictException("BOM_CYCLE_DETECTED", "چرخه در ساختار BOM تأییدشده شناسایی شد.");
        revisions.TryGetValue(itemId, out var revision);
        var children = revision?.Lines.Select(line => BuildNode(line.ChildItemId, line, revisions, items, units,
            new HashSet<Guid>(path))).ToArray() ?? [];
        return new(items[itemId], revision is null ? null : Revision(revision),
            relationship is null ? null : Relationship(relationship, units), children);
    }

    private static BomStructureRevisionRef Revision(ReleasedRevision x) =>
        new(x.Id, x.RevisionCode, x.BaseOutputQty);
    private static BomStructureRevisionRef Revision(ReleasedLine x) =>
        new(x.RevisionId, x.RevisionCode, x.BaseOutputQty);
    private static BomStructureRelationship Relationship(ReleasedLine x,
        IReadOnlyDictionary<Guid, BomUnitRef> units) =>
        new(x.UsageQty, x.ScrapOverridePercent, units[x.ChildBaseUnitId]);

    private sealed record ReleasedRevision(Guid Id, Guid ParentItemId, string RevisionCode,
        decimal BaseOutputQty, ReleasedLine[] Lines);
    private sealed record ReleasedLine(Guid RevisionId, Guid ParentItemId, string RevisionCode,
        decimal BaseOutputQty, Guid ChildItemId, Guid ChildBaseUnitId, decimal UsageQty,
        decimal? ScrapOverridePercent);
    private sealed record BuilderRevision(Guid Id, Guid ParentItemId, Guid ParentBaseUnitId,
        string RevisionCode, string Status, decimal BaseOutputQty, long Version, DateTime CreatedAtUtc,
        BuilderLine[] Lines);
    private sealed record BuilderLine(Guid Id, Guid ChildItemId, Guid ChildBaseUnitId,
        decimal UsageQty, decimal? ScrapOverridePercent, int SortOrder);
}
