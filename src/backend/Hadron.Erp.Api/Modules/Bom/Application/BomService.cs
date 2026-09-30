using System.Data;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Bom.Domain;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Platform.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Hadron.Erp.Api.Modules.Bom.Application;

public sealed class BomService(IDbContextFactory<HadronDbContext> factory, IAuditEventSink auditSink,
    IHttpContextAccessor accessor, ILogger<BomService> logger)
{
    private const long GraphLockKey = 62006;

    public async Task<BomRevisionListResponse> List(string? search, Guid? parentItemId, string? itemType,
        string? status, int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > 100) throw Validation("page", "صفحه و اندازه صفحه معتبر نیست.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var q = db.BomRevisions.AsNoTracking().AsQueryable();
        if (parentItemId.HasValue) q = q.Where(x => x.ParentItemId == parentItemId);
        if (!string.IsNullOrWhiteSpace(itemType)) q = q.Where(x => x.ParentItem.ItemType == itemType);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(x => x.RevisionCode.Contains(term) || x.ParentItem.BusinessNumber.Contains(term) ||
                x.ParentItem.NameFa.Contains(term));
        }
        var total = await q.CountAsync(ct);
        var revisions = await q.OrderByDescending(x => x.CreatedAtUtc).Skip((page - 1) * pageSize).Take(pageSize)
            .Include(x => x.ParentItem).ThenInclude(x => x.BaseUnit).Include(x => x.ParentBaseUnit)
            .Select(x => new { Revision = x, LineCount = x.Lines.Count }).ToListAsync(ct);
        var parentIds = revisions.Select(x => x.Revision.ParentItemId).Distinct().ToArray();
        var profiles = await db.MaterialItemProfiles.AsNoTracking().Where(x => parentIds.Contains(x.ItemId))
            .ToDictionaryAsync(x => x.ItemId, ct);
        var rows = revisions.Select(x =>
        {
            profiles.TryGetValue(x.Revision.ParentItemId, out var profile);
            var integrity = x.Revision.ParentItem.BaseUnitId == x.Revision.ParentBaseUnitId
                ? Array.Empty<string>() : ["BASE_UNIT_CHANGED"];
            return new BomRevisionResponse(x.Revision.Id, x.Revision.RevisionCode,
                Item(x.Revision.ParentItem, profile), Unit(x.Revision.ParentBaseUnit), x.Revision.Status,
                x.Revision.BaseOutputQty, x.Revision.RevisionNotes, x.Revision.CopiedFromRevisionId,
                x.Revision.Version,
                x.Revision.CreatedAtUtc, x.Revision.UpdatedAtUtc, x.Revision.ApprovedAtUtc,
                x.Revision.ObsoletedAtUtc, x.LineCount, Array.Empty<string>(), integrity,
                Array.Empty<BomLineResponse>());
        }).ToList();
        return new(rows, page, pageSize, total);
    }

    public async Task<BomRevisionResponse?> Get(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await LoadResponse(db, id, true, ct);
    }

    public async Task<BomRevisionResponse> Create(CreateBomRevisionRequest request, CancellationToken ct)
    {
        ValidateHeader(request.BaseOutputQty, request.RevisionNotes);
        await using var db = await factory.CreateDbContextAsync(ct);
        var parent = await db.Items.SingleOrDefaultAsync(x => x.Id == request.ParentItemId, ct)
            ?? throw new KeyNotFoundException();
        EnsureParent(parent, true);
        var revision = new BomRevision(await NextCode(db, ct), parent.Id, parent.BaseUnitId,
            request.BaseOutputQty, Clean(request.RevisionNotes));
        db.BomRevisions.Add(revision);
        await Save(db, ct);
        var response = (await LoadResponse(db, revision.Id, true, ct))!;
        await Emit(revision.Id, "CREATE_DRAFT", response, null, ct);
        return response;
    }

    public async Task<BomRevisionResponse> Copy(Guid sourceId, CopyBomRevisionRequest request, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockRevisions(db, transaction, [sourceId], ct);
        var source = await db.BomRevisions.AsNoTracking().Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == sourceId, ct) ?? throw new KeyNotFoundException();
        var targetParentId = request.TargetParentItemId ?? source.ParentItemId;
        var itemIds = source.Lines.Select(x => x.ChildItemId).Append(source.ParentItemId)
            .Append(targetParentId).Distinct().ToArray();
        var items = await db.Items.Where(x => itemIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var sourceParent = items[source.ParentItemId];
        if (!items.TryGetValue(targetParentId, out var targetParent)) throw new KeyNotFoundException();
        EnsureParent(targetParent, true);
        if (targetParent.ItemType != sourceParent.ItemType)
            throw Validation("targetParentItemId", "نوع قلم مقصد باید با Parent مبدأ یکسان باشد.");
        if (targetParent.BaseUnitId != sourceParent.BaseUnitId)
            throw Validation("targetParentItemId", "واحد پایه قلم مقصد باید با Parent مبدأ یکسان باشد.");
        ValidateHeader(source.BaseOutputQty, request.RevisionNotes);
        var revision = new BomRevision(await NextCode(db, ct), targetParent.Id, targetParent.BaseUnitId,
            source.BaseOutputQty, Clean(request.RevisionNotes) ?? source.RevisionNotes, source.Id);
        foreach (var line in source.Lines)
        {
            var child = items[line.ChildItemId];
            if (!child.IsActive) throw Validation("childItemId", "همه Childهای Revision کپی‌شده باید فعال باشند.");
            EnsureRelationship(targetParent.Id, targetParent.ItemType, child.Id, child.ItemType);
            revision.Lines.Add(new(revision.Id, line.ChildItemId, child.BaseUnitId, line.UsageQty,
                line.ScrapOverridePercent, line.SortOrder));
        }
        db.BomRevisions.Add(revision);
        await Save(db, ct);
        await Commit(transaction, ct);
        var response = (await LoadResponse(db, revision.Id, true, ct))!;
        await Emit(revision.Id, targetParent.Id == source.ParentItemId ? "COPY_REVISION" : "COPY_TO_PARENT",
            response, null, ct);
        return response;
    }

    public async Task<DiscardBomRevisionResponse> DiscardDraft(Guid id, BomMutationRequest request,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockRevisions(db, transaction, [id], ct);
        var revision = await db.BomRevisions.Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException();
        EnsureVersion(revision.Version, request.ExpectedVersion);
        if (revision.Status != BomStatuses.Draft)
            throw new BomConflictException("RELEASED_BOM_IMMUTABLE",
                "فقط پیش‌نویس BOM قابل حذف است؛ تاریخچه منتشرشده حفظ می‌شود.");
        if (await db.BomRevisions.AnyAsync(x => x.CopiedFromRevisionId == id, ct))
            throw new BomConflictException("BOM_DRAFT_HAS_SUCCESSOR",
                "این پیش‌نویس مبدأ Revision دیگری است و قابل حذف نیست.");

        var result = new DiscardBomRevisionResponse(revision.Id, revision.RevisionCode, revision.ParentItemId);
        var oldValues = new Dictionary<string, object?>
        {
            ["RevisionCode"] = revision.RevisionCode,
            ["Status"] = revision.Status,
            ["Version"] = revision.Version,
            ["ParentItemId"] = revision.ParentItemId,
            ["LineCount"] = revision.Lines.Count
        };
        db.BomLines.RemoveRange(revision.Lines);
        db.BomRevisions.Remove(revision);
        await Save(db, ct);
        await Commit(transaction, ct);
        await EmitDiscard(id, oldValues, request.Reason, ct);
        return result;
    }

    public async Task<BomRevisionResponse> Update(Guid id, UpdateBomRevisionRequest request, CancellationToken ct)
    {
        ValidateHeader(request.BaseOutputQty, request.RevisionNotes);
        await using var db = await factory.CreateDbContextAsync(ct);
        var revision = await Draft(db, id, request.ExpectedVersion, ct);
        revision.EditDraft(request.BaseOutputQty, Clean(request.RevisionNotes));
        await Save(db, ct);
        var response = (await LoadResponse(db, id, true, ct))!;
        await Emit(id, "EDIT_HEADER", response, null, ct);
        return response;
    }

    public async Task<BomRevisionResponse> AddLine(Guid id, AddBomLineRequest request, CancellationToken ct)
    {
        ValidateLine(request.UsageQty, request.ScrapOverridePercent, request.SortOrder);
        await using var db = await factory.CreateDbContextAsync(ct);
        var revision = await Draft(db, id, request.ExpectedVersion, ct);
        var child = await db.Items.SingleOrDefaultAsync(x => x.Id == request.ChildItemId, ct)
            ?? throw new KeyNotFoundException();
        var parent = await db.Items.SingleAsync(x => x.Id == revision.ParentItemId, ct);
        if (!child.IsActive) throw Validation("childItemId", "Child فعال الزامی است.");
        EnsureRelationship(parent.Id, parent.ItemType, child.Id, child.ItemType);
        if (await db.BomLines.AnyAsync(x => x.BomRevisionId == id && x.ChildItemId == child.Id, ct))
            throw new BomConflictException("DUPLICATE_CHILD", "Child در Revision تکراری است.");
        var applicability = await db.BomItemApplicabilities.SingleOrDefaultAsync(x =>
            x.ParentItemId == parent.Id && x.ChildItemId == child.Id, ct);
        if (applicability is null)
            db.BomItemApplicabilities.Add(new(parent.Id, child.Id));
        else if (!applicability.IsActive)
            applicability.ChangeState(true);
        db.BomLines.Add(new(id, child.Id, child.BaseUnitId, request.UsageQty, request.ScrapOverridePercent, request.SortOrder));
        revision.TouchDraft();
        await Save(db, ct);
        var response = (await LoadResponse(db, id, true, ct))!;
        await Emit(id, "ADD_LINE", response, null, ct);
        return response;
    }

    public async Task<BomRevisionResponse> UpdateLine(Guid id, Guid lineId, UpdateBomLineRequest request,
        CancellationToken ct)
    {
        ValidateLine(request.UsageQty, request.ScrapOverridePercent, request.SortOrder);
        await using var db = await factory.CreateDbContextAsync(ct);
        var revision = await Draft(db, id, request.ExpectedVersion, ct);
        var line = await db.BomLines.SingleOrDefaultAsync(x => x.Id == lineId && x.BomRevisionId == id, ct)
            ?? throw new KeyNotFoundException();
        line.Edit(request.UsageQty, request.ScrapOverridePercent, request.SortOrder);
        revision.TouchDraft();
        await Save(db, ct);
        var response = (await LoadResponse(db, id, true, ct))!;
        await Emit(id, "EDIT_LINE", response, null, ct);
        return response;
    }

    public async Task<BomRevisionResponse> RemoveLine(Guid id, Guid lineId, BomMutationRequest request,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var revision = await Draft(db, id, request.ExpectedVersion, ct);
        var line = await db.BomLines.SingleOrDefaultAsync(x => x.Id == lineId && x.BomRevisionId == id, ct)
            ?? throw new KeyNotFoundException();
        db.BomLines.Remove(line);
        revision.TouchDraft();
        await Save(db, ct);
        var response = (await LoadResponse(db, id, true, ct))!;
        await Emit(id, "REMOVE_LINE", response, request.Reason, ct);
        return response;
    }

    public async Task<BomValidationResponse> Validate(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var revision = await db.BomRevisions.AsNoTracking().Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException();
        var blocking = await BlockingCodes(db, revision, false, ct);
        var warnings = new List<string>();
        if (await HasCandidateCycle(db, revision, ct)) warnings.Add("BOM_CYCLE_DETECTED");
        return new(blocking.Count == 0 && warnings.Count == 0, warnings, blocking);
    }

    public async Task<BomRevisionResponse> Approve(Guid id, ApproveBomRevisionRequest request, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockGraph(db, transaction, ct);
        await LockRevisions(db, transaction, [id], ct);
        db.ChangeTracker.Clear();
        var revision = await db.BomRevisions.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException();
        EnsureVersion(revision.Version, request.ExpectedVersion);
        if (revision.Status != BomStatuses.Draft) throw new BomConflictException("APPROVED_IMMUTABLE", "فقط Draft قابل تأیید است.");
        await LockParentRevisions(db, transaction, revision.ParentItemId, ct);
        db.ChangeTracker.Clear();
        revision = await db.BomRevisions.Include(x => x.Lines).SingleAsync(x => x.Id == id, ct);
        EnsureVersion(revision.Version, request.ExpectedVersion);
        await LockItems(db, transaction, revision.Lines.Select(x => x.ChildItemId).Append(revision.ParentItemId), ct);
        var blocking = await BlockingCodes(db, revision, true, ct);
        if (blocking.Count > 0) throw new BomConflictException(blocking[0], "BOM قابل تأیید نیست.", blocking);
        if (await HasCurrentCycle(db, revision, ct))
            throw new BomConflictException("BOM_CYCLE_DETECTED", "چرخه BOM تأیید را مسدود کرد.");

        var previousRows = await db.BomRevisions.Where(x => x.ParentItemId == revision.ParentItemId &&
            x.Id != revision.Id && x.Status == BomStatuses.Approved).ToListAsync(ct);
        if (previousRows.Count > 1)
            throw new BomConflictException("MULTIPLE_CURRENT_BOMS", "برای Parent بیش از یک BOM جاری ثبت شده است.");
        var previous = previousRows.SingleOrDefault();
        if (previous is not null)
        {
            previous.Obsolete(Actor());
            // The partial unique index is immediate. Persist the old-current transition first,
            // while keeping both transitions inside the same transaction.
            await Save(db, ct);
        }
        revision.Approve(Actor());
        await Save(db, ct);
        await Commit(transaction, ct);
        var response = (await Get(id, ct))!;
        if (previous is not null)
        {
            var superseded = (await Get(previous.Id, ct))!;
            await Emit(previous.Id, "SUPERSEDED", superseded, request.Reason, ct);
        }
        await Emit(id, "APPROVE", response, request.Reason, ct);
        return response;
    }

    public async Task<BomRevisionResponse> Obsolete(Guid id, ObsoleteBomRevisionRequest request, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockGraph(db, transaction, ct);
        await LockRevisions(db, transaction, [id], ct);
        db.ChangeTracker.Clear();
        var revision = await db.BomRevisions.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException();
        EnsureVersion(revision.Version, request.ExpectedVersion);
        try { revision.Obsolete(Actor()); }
        catch (InvalidOperationException) { throw new BomConflictException("BOM_NOT_CURRENT", "فقط BOM جاری قابل منسوخ‌کردن است."); }
        await Save(db, ct);
        await Commit(transaction, ct);
        var response = (await Get(id, ct))!;
        await Emit(id, "OBSOLETE", response, request.Reason, ct);
        return response;
    }

    public async Task<BomRevisionResponse?> Current(Guid parentId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var id = await db.BomRevisions.AsNoTracking().Where(x => x.ParentItemId == parentId &&
            x.Status == BomStatuses.Approved).Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(ct);
        if (!id.HasValue) return null;
        var response = (await LoadResponse(db, id.Value, true, ct))!;
        if (response.IntegrityReasonCodes.Contains("BASE_UNIT_CHANGED"))
            throw new BomConflictException("BASE_UNIT_CHANGED", "واحد پایه فعلی با snapshot BOM تطابق ندارد.");
        return response;
    }

    private static async Task<BomRevision> Draft(HadronDbContext db, Guid id, long version, CancellationToken ct)
    {
        var revision = await db.BomRevisions.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException();
        EnsureVersion(revision.Version, version);
        if (revision.Status != BomStatuses.Draft) throw new BomConflictException("APPROVED_IMMUTABLE", "Revision منتشرشده immutable است.");
        return revision;
    }

    private static async Task<List<string>> BlockingCodes(HadronDbContext db, BomRevision revision, bool approval,
        CancellationToken ct)
    {
        var codes = new List<string>();
        if (revision.BaseOutputQty <= 0) codes.Add("BASE_OUTPUT_QTY_INVALID");
        if (approval && revision.Lines.Count == 0) codes.Add("EMPTY_BOM");
        var items = await db.Items.Where(x => x.Id == revision.ParentItemId || revision.Lines.Select(l => l.ChildItemId).Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        if (!items.TryGetValue(revision.ParentItemId, out var parent) || !parent.IsActive) codes.Add("PARENT_INACTIVE");
        else if (parent.BaseUnitId != revision.ParentBaseUnitId) codes.Add("BASE_UNIT_CHANGED");
        foreach (var line in revision.Lines)
        {
            if (!items.TryGetValue(line.ChildItemId, out var child) || !child.IsActive) codes.Add("CHILD_INACTIVE");
            else
            {
                if (child.BaseUnitId != line.ChildBaseUnitId) codes.Add("BASE_UNIT_CHANGED");
                try { EnsureRelationship(parent!.Id, parent.ItemType, child.Id, child.ItemType); }
                catch (BomValidationException) { codes.Add("INVALID_ITEM_RELATIONSHIP"); }
            }
        }
        return codes.Distinct().ToList();
    }

    private static Task<bool> HasCandidateCycle(HadronDbContext db, BomRevision candidate, CancellationToken ct) =>
        HasCurrentCycle(db, candidate, ct);

    private static async Task<bool> HasCurrentCycle(HadronDbContext db, BomRevision candidate,
        CancellationToken ct)
    {
        var released = await LoadReachableCurrentGraph(db, candidate, ct);
        IEnumerable<Guid> Children(Guid parent)
        {
            if (parent == candidate.ParentItemId) return candidate.Lines.Select(x => x.ChildItemId);
            return released.SingleOrDefault(x => x.ParentItemId == parent)?.Lines.Select(x => x.ChildItemId) ?? [];
        }
        return HasCycle(candidate.ParentItemId, Children);
    }

    private static async Task<List<BomRevision>> LoadReachableCurrentGraph(HadronDbContext db,
        BomRevision candidate, CancellationToken ct)
    {
        var result = new List<BomRevision>();
        var visitedParents = new HashSet<Guid> { candidate.ParentItemId };
        var frontier = candidate.Lines.Select(x => x.ChildItemId).Distinct().ToArray();
        while (frontier.Length > 0)
        {
            var batch = frontier.Where(visitedParents.Add).ToArray();
            if (batch.Length == 0) break;
            var revisions = await db.BomRevisions.AsNoTracking()
                .Where(x => batch.Contains(x.ParentItemId) && x.Status == BomStatuses.Approved)
                .Include(x => x.Lines).ToListAsync(ct);
            result.AddRange(revisions);
            frontier = revisions.SelectMany(x => x.Lines).Select(x => x.ChildItemId)
                .Where(x => !visitedParents.Contains(x)).Distinct().ToArray();
        }
        return result;
    }

    private static bool HasCycle(Guid root, Func<Guid, IEnumerable<Guid>> children)
    {
        var visiting = new HashSet<Guid>(); var visited = new HashSet<Guid>();
        bool Visit(Guid node)
        {
            if (!visiting.Add(node)) return true;
            if (visited.Contains(node)) { visiting.Remove(node); return false; }
            foreach (var child in children(node)) if (Visit(child)) return true;
            visiting.Remove(node); visited.Add(node); return false;
        }
        return Visit(root);
    }

    private static async Task<BomRevisionResponse?> LoadResponse(HadronDbContext db, Guid id, bool lines, CancellationToken ct)
    {
        var q = db.BomRevisions.AsNoTracking().Include(x => x.ParentItem).ThenInclude(x => x.BaseUnit)
            .Include(x => x.ParentBaseUnit).AsQueryable();
        if (lines) q = q.Include(x => x.Lines).ThenInclude(x => x.ChildItem).ThenInclude(x => x.BaseUnit)
            .Include(x => x.Lines).ThenInclude(x => x.ChildBaseUnit);
        var r = await q.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return null;
        var profileIds = r.Lines.Select(x => x.ChildItemId).Append(r.ParentItemId).ToArray();
        var profiles = await db.MaterialItemProfiles.AsNoTracking().Where(x => profileIds.Contains(x.ItemId))
            .ToDictionaryAsync(x => x.ItemId, ct);
        var integrity = new List<string>();
        if (r.ParentItem.BaseUnitId != r.ParentBaseUnitId) integrity.Add("BASE_UNIT_CHANGED");
        var lineResponses = r.Lines.OrderBy(x => x.SortOrder).Select(x =>
        {
            var reasons = x.ChildItem.BaseUnitId == x.ChildBaseUnitId ? Array.Empty<string>() : ["BASE_UNIT_CHANGED"];
            if (reasons.Length > 0) integrity.Add("BASE_UNIT_CHANGED");
            profiles.TryGetValue(x.ChildItemId, out var p);
            return new BomLineResponse(x.Id, Item(x.ChildItem, p), Unit(x.ChildBaseUnit), x.UsageQty,
                x.ScrapOverridePercent, x.ScrapOverridePercent ?? p?.DefaultPlanningScrapPercent ?? 0,
                x.SortOrder, reasons);
        }).ToList();
        var warnings = r.Status == BomStatuses.Draft && await HasCandidateCycle(db, r, ct)
            ? new[] { "BOM_CYCLE_DETECTED" } : Array.Empty<string>();
        profiles.TryGetValue(r.ParentItemId, out var parentProfile);
        return new(r.Id, r.RevisionCode, Item(r.ParentItem, parentProfile), Unit(r.ParentBaseUnit), r.Status,
            r.BaseOutputQty, r.RevisionNotes, r.CopiedFromRevisionId, r.Version,
            r.CreatedAtUtc, r.UpdatedAtUtc, r.ApprovedAtUtc, r.ObsoletedAtUtc, lineResponses.Count, warnings,
            integrity.Distinct().ToArray(), lineResponses);
    }

    private static BomItemRef Item(Item x, MaterialItemProfile? p) => new(x.Id, x.BusinessNumber, x.NameFa,
        x.NameEn, x.ItemType, x.IsActive, p?.SupplyMethod, Unit(x.BaseUnit));
    private static BomUnitRef Unit(Hadron.Erp.Api.Modules.ReferenceData.Domain.Unit x) =>
        new(x.Id, x.Code, x.NameFa, x.NameEn, x.Symbol, x.IsActive);

    private static void EnsureParent(Item item, bool active)
    {
        if (item.ItemType is not (ItemTypes.Product or ItemTypes.Part or ItemTypes.Component))
            throw Validation("parentItemId", "نوع Parent معتبر نیست.");
        if (active && !item.IsActive) throw Validation("parentItemId", "Parent فعال الزامی است.");
    }
    private static void EnsureRelationship(Guid parentId, string parentType, Guid childId, string childType)
    {
        if (parentId == childId) throw Validation("childItemId", "ارجاع مستقیم به خود مجاز نیست.");
        var valid = parentType == ItemTypes.Product && childType == ItemTypes.Part ||
            parentType == ItemTypes.Part && childType == ItemTypes.Component ||
            parentType == ItemTypes.Component && childType == ItemTypes.Component;
        if (!valid) throw Validation("childItemId", "رابطه Parent/Child مجاز نیست.");
    }
    private static void ValidateHeader(decimal qty, string? notes)
    {
        var e = new Dictionary<string, string[]>();
        if (qty <= 0) e["baseOutputQty"] = ["مقدار خروجی باید مثبت باشد."];
        if (Clean(notes)?.Length > 2000) e["revisionNotes"] = ["یادداشت حداکثر ۲۰۰۰ نویسه است."];
        if (e.Count > 0) throw new BomValidationException(e);
    }
    private static void ValidateLine(decimal qty, decimal? scrap, int sort)
    {
        var e = new Dictionary<string, string[]>();
        if (qty <= 0) e["usageQty"] = ["مصرف باید مثبت باشد."];
        if (scrap is < 0 or > 100) e["scrapOverridePercent"] = ["ضایعات باید بین صفر و صد باشد."];
        if (sort < 0) e["sortOrder"] = ["ترتیب نمی‌تواند منفی باشد."];
        if (e.Count > 0) throw new BomValidationException(e);
    }
    private static void EnsureVersion(long actual, long expected)
    { if (actual != expected) throw new BomConflictException("STALE_VERSION", "نسخه ارسالی قدیمی است."); }
    private static string? Clean(string? x) => string.IsNullOrWhiteSpace(x) ? null : x.Trim();
    private static BomValidationException Validation(string key, string message) => new(new Dictionary<string, string[]> { [key] = [message] });

    private static async Task<string> NextCode(HadronDbContext db, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT nextval('engineering.bom_revision_number_seq')", connection);
        return $"BOM-{(long)(await command.ExecuteScalarAsync(ct))!:D6}";
    }
    private static async Task LockGraph(HadronDbContext db, IDbContextTransaction tx, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", connection,
            (NpgsqlTransaction)tx.GetDbTransaction());
        command.Parameters.AddWithValue("key", GraphLockKey);
        await command.ExecuteNonQueryAsync(ct);
    }
    private static async Task LockItems(HadronDbContext db, IDbContextTransaction tx, IEnumerable<Guid> itemIds,
        CancellationToken ct)
    {
        var ids = itemIds.Distinct().Order().ToArray();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(
            "SELECT \"Id\" FROM catalog.items WHERE \"Id\" = ANY(@ids) ORDER BY \"Id\" FOR UPDATE", connection,
            (NpgsqlTransaction)tx.GetDbTransaction());
        command.Parameters.AddWithValue("ids", ids);
        await command.ExecuteNonQueryAsync(ct);
    }
    private static async Task LockRevisions(HadronDbContext db, IDbContextTransaction tx,
        IEnumerable<Guid> revisionIds, CancellationToken ct)
    {
        var ids = revisionIds.Distinct().Order().ToArray();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(
            "SELECT \"Id\" FROM engineering.bom_revisions WHERE \"Id\" = ANY(@ids) ORDER BY \"Id\" FOR UPDATE",
            connection, (NpgsqlTransaction)tx.GetDbTransaction());
        command.Parameters.AddWithValue("ids", ids);
        await command.ExecuteNonQueryAsync(ct);
    }
    private static async Task LockParentRevisions(HadronDbContext db, IDbContextTransaction tx,
        Guid parentItemId, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(
            "SELECT \"Id\" FROM engineering.bom_revisions WHERE \"ParentItemId\" = @parentId ORDER BY \"Id\" FOR UPDATE",
            connection, (NpgsqlTransaction)tx.GetDbTransaction());
        command.Parameters.AddWithValue("parentId", parentItemId);
        await command.ExecuteNonQueryAsync(ct);
    }
    private static async Task Save(HadronDbContext db, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new BomConflictException("STALE_VERSION", "نسخه ارسالی قدیمی است."); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new BomConflictException("CURRENT_BOM_CONFLICT", "برای هر Parent فقط یک BOM جاری مجاز است."); }
    }
    private static async Task Commit(IDbContextTransaction transaction, CancellationToken ct)
    {
        await transaction.CommitAsync(ct);
    }
    private string? Actor() => AuditEventContext.ActorId(accessor.HttpContext);
    private async Task Emit(Guid id, string action, BomRevisionResponse response, string? reason, CancellationToken ct)
    {
        try
        {
            await auditSink.WriteAsync(new("BomRevision", id, action, DateTime.UtcNow, Actor(),
                AuditEventContext.CorrelationId(accessor.HttpContext), null,
                new Dictionary<string, object?>
                {
                    ["RevisionCode"] = response.RevisionCode,
                    ["Status"] = response.Status,
                    ["Version"] = response.Version
                }, reason), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { logger.LogError(ex, "Post-commit BOM audit failed for {BomId} {Action}.", id, action); }
    }
    private async Task EmitDiscard(Guid id, IReadOnlyDictionary<string, object?> oldValues, string? reason,
        CancellationToken ct)
    {
        try
        {
            await auditSink.WriteAsync(new("BomRevision", id, "DISCARD_DRAFT", DateTime.UtcNow, Actor(),
                AuditEventContext.CorrelationId(accessor.HttpContext), oldValues, null, reason), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { logger.LogError(ex, "Post-commit BOM audit failed for {BomId} DISCARD_DRAFT.", id); }
    }
}
