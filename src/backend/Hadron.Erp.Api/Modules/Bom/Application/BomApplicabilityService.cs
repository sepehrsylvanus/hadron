using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Bom.Domain;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Platform.Audit;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hadron.Erp.Api.Modules.Bom.Application;

public sealed class BomApplicabilityService(
    IDbContextFactory<HadronDbContext> factory,
    IAuditEventSink auditSink,
    IHttpContextAccessor accessor,
    ILogger<BomApplicabilityService> logger)
{
    public async Task<BomApplicableItemListResponse> Candidates(Guid parentItemId, bool showAll,
        string? search, int page, int pageSize, CancellationToken ct)
    {
        ValidatePaging(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        var parent = await db.Items.AsNoTracking().SingleOrDefaultAsync(x => x.Id == parentItemId, ct)
            ?? throw new KeyNotFoundException();
        var childType = parent.ItemType == ItemTypes.Product ? ItemTypes.Part : ItemTypes.Component;
        if (parent.ItemType is not (ItemTypes.Product or ItemTypes.Part or ItemTypes.Component))
            throw Validation("parentItemId", "نوع Parent برای BOM معتبر نیست.");

        var query = db.Items.AsNoTracking().Where(x => x.Id != parentItemId && x.ItemType == childType && x.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.BusinessNumber.Contains(term) || x.NameFa.Contains(term) ||
                (x.NameEn != null && x.NameEn.Contains(term)));
        }

        if (!showAll)
            query = query.Where(x => db.BomItemApplicabilities.Any(a => a.ParentItemId == parentItemId &&
                a.ChildItemId == x.Id && a.IsActive));

        var total = await query.CountAsync(ct);
        var items = await query.Include(x => x.BaseUnit).OrderBy(x => x.BusinessNumber)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var itemIds = items.Select(x => x.Id).ToArray();
        var profiles = await db.MaterialItemProfiles.AsNoTracking().Where(x => itemIds.Contains(x.ItemId))
            .ToDictionaryAsync(x => x.ItemId, ct);
        var applicable = await db.BomItemApplicabilities.AsNoTracking()
            .Where(x => x.ParentItemId == parentItemId && x.IsActive && itemIds.Contains(x.ChildItemId))
            .Select(x => x.ChildItemId).ToHashSetAsync(ct);
        return new(items.Select(item => new BomApplicableItemResponse(
            ToItem(item, profiles.GetValueOrDefault(item.Id)), applicable.Contains(item.Id))).ToArray(),
            page, pageSize, total);
    }

    public async Task<BomItemApplicabilityListResponse> ListForChild(Guid childItemId, bool includeInactive,
        string? search, int page, int pageSize, CancellationToken ct)
    {
        ValidatePaging(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Items.AsNoTracking().AnyAsync(x => x.Id == childItemId, ct))
            throw new KeyNotFoundException();
        var query = db.BomItemApplicabilities.AsNoTracking().Where(x => x.ChildItemId == childItemId &&
            (includeInactive || x.IsActive));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.ParentItem.BusinessNumber.Contains(term) ||
                x.ParentItem.NameFa.Contains(term) ||
                (x.ParentItem.NameEn != null && x.ParentItem.NameEn.Contains(term)));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.ParentItem.BusinessNumber).Skip((page - 1) * pageSize)
            .Take(pageSize).ToListAsync(ct);
        return new(await Responses(db, rows, ct), page, pageSize, total);
    }

    public async Task<BomItemApplicabilityResponse> Create(CreateBomItemApplicabilityRequest request,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var items = await db.Items.Where(x => x.Id == request.ParentItemId || x.Id == request.ChildItemId)
            .ToDictionaryAsync(x => x.Id, ct);
        if (!items.TryGetValue(request.ParentItemId, out var parent) ||
            !items.TryGetValue(request.ChildItemId, out var child)) throw new KeyNotFoundException();
        EnsureRelationship(parent, child);
        if (!parent.IsActive || !child.IsActive)
            throw Validation("items", "برای ایجاد دامنه مصرف، Parent و Child باید فعال باشند.");

        var row = await db.BomItemApplicabilities.SingleOrDefaultAsync(x =>
            x.ParentItemId == parent.Id && x.ChildItemId == child.Id, ct);
        var action = "CREATE";
        if (row is null)
        {
            row = new BomItemApplicability(parent.Id, child.Id);
            db.BomItemApplicabilities.Add(row);
        }
        else if (!row.IsActive)
        {
            row.ChangeState(true);
            action = "REACTIVATE";
        }
        else
        {
            return (await Responses(db, [row], ct))[0];
        }

        await Save(db, ct);
        var response = (await Responses(db, [row], ct))[0];
        await Emit(row.Id, action, response, null, ct);
        return response;
    }

    public async Task<BomItemApplicabilityResponse> ChangeState(Guid id, bool active,
        ChangeBomItemApplicabilityStateRequest request, CancellationToken ct)
    {
        if (request.ExpectedVersion < 1) throw Validation("expectedVersion", "نسخه معتبر الزامی است.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.BomItemApplicabilities.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException();
        if (row.Version != request.ExpectedVersion)
            throw new BomConflictException("STALE_VERSION", "نسخه ارسالی قدیمی است.");
        if (active)
        {
            var items = await db.Items.Where(x => x.Id == row.ParentItemId || x.Id == row.ChildItemId)
                .ToDictionaryAsync(x => x.Id, ct);
            if (!items[row.ParentItemId].IsActive || !items[row.ChildItemId].IsActive)
                throw Validation("items", "رابطه فقط برای اقلام فعال قابل بازفعال‌سازی است.");
        }
        row.ChangeState(active);
        await Save(db, ct);
        var response = (await Responses(db, [row], ct))[0];
        await Emit(row.Id, active ? "REACTIVATE" : "DEACTIVATE", response, request.Reason, ct);
        return response;
    }

    private static async Task<IReadOnlyList<BomItemApplicabilityResponse>> Responses(HadronDbContext db,
        IReadOnlyCollection<BomItemApplicability> rows, CancellationToken ct)
    {
        var itemIds = rows.SelectMany(x => new[] { x.ParentItemId, x.ChildItemId }).Distinct().ToArray();
        var items = await db.Items.AsNoTracking().Include(x => x.BaseUnit).Where(x => itemIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var profiles = await db.MaterialItemProfiles.AsNoTracking().Where(x => itemIds.Contains(x.ItemId))
            .ToDictionaryAsync(x => x.ItemId, ct);
        return rows.Select(row => new BomItemApplicabilityResponse(row.Id,
            ToItem(items[row.ParentItemId], profiles.GetValueOrDefault(row.ParentItemId)),
            ToItem(items[row.ChildItemId], profiles.GetValueOrDefault(row.ChildItemId)),
            row.IsActive, row.Version, row.CreatedAtUtc, row.UpdatedAtUtc)).ToArray();
    }

    private static BomStructureItemRef ToItem(Item item, MaterialItemProfile? profile) => new(item.Id,
        item.BusinessNumber, item.NameFa, item.NameEn, item.ItemType, item.IsActive, profile?.SupplyMethod,
        profile?.NetWeightKg, new(item.BaseUnit.Id, item.BaseUnit.Code, item.BaseUnit.NameFa,
            item.BaseUnit.NameEn, item.BaseUnit.Symbol, item.BaseUnit.IsActive));

    internal static void EnsureRelationship(Item parent, Item child)
    {
        if (parent.Id == child.Id) throw Validation("childItemId", "ارجاع مستقیم به خود مجاز نیست.");
        var valid = parent.ItemType == ItemTypes.Product && child.ItemType == ItemTypes.Part ||
            parent.ItemType == ItemTypes.Part && child.ItemType == ItemTypes.Component ||
            parent.ItemType == ItemTypes.Component && child.ItemType == ItemTypes.Component;
        if (!valid) throw Validation("childItemId", "رابطه Parent/Child مجاز نیست.");
    }

    private static void ValidatePaging(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            throw Validation("page", "صفحه باید مثبت و اندازه صفحه بین ۱ تا ۱۰۰ باشد.");
    }

    private static async Task Save(HadronDbContext db, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        { throw new BomConflictException("STALE_VERSION", "نسخه ارسالی قدیمی است."); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new BomConflictException("APPLICABILITY_ALREADY_EXISTS", "دامنه مصرف قبلاً ثبت شده است."); }
    }

    private async Task Emit(Guid id, string action, BomItemApplicabilityResponse response, string? reason,
        CancellationToken ct)
    {
        try
        {
            await auditSink.WriteAsync(new("BomItemApplicability", id, action, DateTime.UtcNow,
                AuditEventContext.ActorId(accessor.HttpContext), AuditEventContext.CorrelationId(accessor.HttpContext),
                null, new Dictionary<string, object?>
                {
                    ["ParentItemId"] = response.ParentItem.Id,
                    ["ChildItemId"] = response.ChildItem.Id,
                    ["IsActive"] = response.IsActive,
                    ["Version"] = response.Version
                }, reason), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "Post-commit applicability audit failed for {ApplicabilityId} {Action}.",
                id, action);
        }
    }

    private static BomValidationException Validation(string key, string message) =>
        new(new Dictionary<string, string[]> { [key] = [message] });
}
