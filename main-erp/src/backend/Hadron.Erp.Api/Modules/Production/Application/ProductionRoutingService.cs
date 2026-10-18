using System.Data;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Production.Domain;
using Hadron.Erp.Api.Platform.Audit;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hadron.Erp.Api.Modules.Production.Application;

public sealed class ProductionRoutingService(IDbContextFactory<HadronDbContext> factory,
    IAuditEventSink auditSink, IHttpContextAccessor accessor, ILogger<ProductionRoutingService> logger)
{
    public async Task<ProductionWorkCenterListResponse> ListWorkCenters(string? search,
        bool includeInactive, int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.ProductionWorkCenters.AsNoTracking().Where(x => includeInactive || x.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.BusinessNumber.Contains(term) || x.NameFa.Contains(term) ||
                x.NameEn != null && x.NameEn.Contains(term) || x.OrganizationalUnit.NameFa.Contains(term));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.Include(x => x.OrganizationalUnit).OrderBy(x => x.BusinessNumber)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(rows.Select(WorkCenter).ToList(), page, pageSize, total);
    }

    public async Task<ProductionWorkCenterResponse?> GetWorkCenter(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.ProductionWorkCenters.AsNoTracking().Include(x => x.OrganizationalUnit)
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        return row is null ? null : WorkCenter(row);
    }

    public async Task<IReadOnlyList<ProductionOrganizationalUnitRef>> ListActiveOrganizationalUnits(
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.OrganizationalUnits.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.NameFa)
            .Select(x => new ProductionOrganizationalUnitRef(x.Id, x.NameFa, x.NameEn, x.IsActive))
            .ToListAsync(ct);
    }

    public async Task<ProductionWorkCenterResponse> CreateWorkCenter(
        CreateProductionWorkCenterRequest request, CancellationToken ct)
    {
        ValidateWorkCenter(request.NameFa, request.WorkingWeekMask, null);
        await using var db = await factory.CreateDbContextAsync(ct);
        await RequireActiveUnit(db, request.OrganizationalUnitId, ct);
        var row = new ProductionWorkCenter(await NextWorkCenterNumber(db, ct), request.NameFa,
            request.NameEn, request.OrganizationalUnitId, request.WorkingWeekMask);
        db.ProductionWorkCenters.Add(row);
        await Save(db, ct);
        var response = (await GetWorkCenter(row.Id, ct))!;
        await Emit("ProductionWorkCenter", row.Id, "CREATE", response, null, ct);
        return response;
    }

    public async Task<ProductionWorkCenterResponse> UpdateWorkCenter(Guid id,
        UpdateProductionWorkCenterRequest request, CancellationToken ct)
    {
        ValidateWorkCenter(request.NameFa, request.WorkingWeekMask, request.ExpectedVersion);
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.ProductionWorkCenters.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException();
        EnsureVersion(row.Version, request.ExpectedVersion);
        await RequireActiveUnit(db, request.OrganizationalUnitId, ct);
        row.Edit(request.NameFa, request.NameEn, request.OrganizationalUnitId, request.WorkingWeekMask);
        await Save(db, ct);
        var response = (await GetWorkCenter(id, ct))!;
        await Emit("ProductionWorkCenter", id, "EDIT", response, null, ct);
        return response;
    }

    public async Task<ProductionWorkCenterResponse> ChangeWorkCenterState(Guid id, bool active,
        ChangeProductionWorkCenterStateRequest request, CancellationToken ct)
    {
        if (request.ExpectedVersion < 1) throw Validation("expectedVersion", "نسخه معتبر الزامی است.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.ProductionWorkCenters.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException();
        EnsureVersion(row.Version, request.ExpectedVersion);
        if (!active && await db.ManufacturingRoutes.AnyAsync(x => x.Status == ManufacturingRouteStatuses.Approved &&
            x.Operations.Any(o => o.WorkCenterId == id), ct))
            throw new ProductionConflictException("WORK_CENTER_IN_APPROVED_ROUTE",
                "مرکز کاری در یک مسیر ساخت تأییدشده استفاده می‌شود و قابل غیرفعال‌سازی نیست.");
        row.SetActive(active);
        await Save(db, ct);
        var response = (await GetWorkCenter(id, ct))!;
        await Emit("ProductionWorkCenter", id, active ? "REACTIVATE" : "DEACTIVATE", response,
            request.Reason, ct);
        return response;
    }

    public async Task<ManufacturingRouteListResponse> ListRoutes(string? search, string? status,
        Guid? itemId, int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        if (!string.IsNullOrWhiteSpace(status) && !ManufacturingRouteStatuses.All.Contains(status))
            throw Validation("status", "وضعیت مسیر ساخت معتبر نیست.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.ManufacturingRoutes.AsNoTracking();
        if (itemId.HasValue) query = query.Where(x => x.ItemId == itemId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.BusinessNumber.Contains(term) || x.Item.BusinessNumber.Contains(term) ||
                x.Item.NameFa.Contains(term) || x.Item.NameEn != null && x.Item.NameEn.Contains(term));
        }
        var total = await query.CountAsync(ct);
        var rows = await LoadRoutes(query).OrderBy(x => x.Item.BusinessNumber)
            .ThenByDescending(x => x.RevisionNumber).Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);
        return new(rows.Select(Route).ToList(), page, pageSize, total);
    }

    public async Task<ManufacturingRouteItemCandidateListResponse> ListRouteItemCandidates(
        string? search, int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Items.AsNoTracking().Include(x => x.BaseUnit)
            .Where(x => x.IsActive && x.BaseUnit.IsActive &&
                (x.ItemType == ItemTypes.Product || db.MaterialItemProfiles.Any(profile =>
                    profile.ItemId == x.Id && (profile.SupplyMethod == SupplyMethods.Make ||
                                               profile.SupplyMethod == SupplyMethods.Hybrid))));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.BusinessNumber.Contains(term) || x.NameFa.Contains(term) ||
                x.NameEn != null && x.NameEn.Contains(term));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.BusinessNumber).Skip((page - 1) * pageSize)
            .Take(pageSize).ToListAsync(ct);
        return new(rows.Select(ItemRef).ToList(), page, pageSize, total);
    }

    public async Task<ManufacturingRouteResponse?> GetRoute(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await LoadRoutes(db.ManufacturingRoutes.AsNoTracking())
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        return row is null ? null : Route(row);
    }

    public async Task<ManufacturingRouteResponse> CreateRoute(CreateManufacturingRouteRequest request,
        CancellationToken ct)
    {
        ValidateOperations(request.Operations, null);
        await using var db = await factory.CreateDbContextAsync(ct);
        var item = await RequireBuildableItem(db, request.ItemId, ct);
        await RequireActiveWorkCenters(db, request.Operations.Select(x => x.WorkCenterId), ct);
        if (await db.ManufacturingRoutes.AnyAsync(x => x.ItemId == item.Id &&
            x.Status == ManufacturingRouteStatuses.Draft, ct))
            throw new ProductionConflictException("DRAFT_MANUFACTURING_ROUTE_EXISTS",
                "برای این قلم یک مسیر ساخت پیش‌نویس وجود دارد.");
        var revision = (await db.ManufacturingRoutes.Where(x => x.ItemId == item.Id)
            .MaxAsync(x => (int?)x.RevisionNumber, ct) ?? 0) + 1;
        var row = new ManufacturingRoute(await NextRouteNumber(db, ct), item.Id,
            item.BaseUnitId, revision, request.Notes);
        db.ManufacturingRoutes.Add(row);
        AddOperations(db, row.Id, request.Operations);
        await Save(db, ct);
        var response = (await GetRoute(row.Id, ct))!;
        await Emit("ManufacturingRoute", row.Id, "CREATE_DRAFT", response, null, ct);
        return response;
    }

    public async Task<ManufacturingRouteResponse> UpdateRoute(Guid id,
        UpdateManufacturingRouteRequest request, CancellationToken ct)
    {
        ValidateOperations(request.Operations, request.ExpectedVersion);
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.ManufacturingRoutes.Include(x => x.Operations)
            .SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException();
        EnsureVersion(row.Version, request.ExpectedVersion);
        if (row.Status != ManufacturingRouteStatuses.Draft)
            throw new ProductionConflictException("MANUFACTURING_ROUTE_NOT_DRAFT",
                "فقط مسیر ساخت پیش‌نویس قابل ویرایش است.");
        await RequireActiveWorkCenters(db, request.Operations.Select(x => x.WorkCenterId), ct);
        row.Edit(request.Notes);
        db.ManufacturingRouteOperations.RemoveRange(row.Operations);
        AddOperations(db, row.Id, request.Operations);
        await Save(db, ct);
        var response = (await GetRoute(id, ct))!;
        await Emit("ManufacturingRoute", id, "EDIT_DRAFT", response, null, ct);
        return response;
    }

    public async Task<ManufacturingRouteResponse> ApproveRoute(Guid id,
        ManufacturingRouteLifecycleRequest request, CancellationToken ct)
    {
        if (request.ExpectedVersion < 1) throw Validation("expectedVersion", "نسخه معتبر الزامی است.");
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var itemId = await db.ManufacturingRoutes.Where(x => x.Id == id).Select(x => (Guid?)x.ItemId)
            .SingleOrDefaultAsync(ct) ?? throw new KeyNotFoundException();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM catalog.items WHERE \"Id\" = {itemId} FOR UPDATE", ct);
        var route = await db.ManufacturingRoutes.Include(x => x.Item).Include(x => x.Operations)
            .ThenInclude(x => x.WorkCenter).SingleAsync(x => x.Id == id, ct);
        EnsureVersion(route.Version, request.ExpectedVersion);
        if (route.Status != ManufacturingRouteStatuses.Draft)
            throw new ProductionConflictException("MANUFACTURING_ROUTE_NOT_DRAFT",
                "فقط مسیر ساخت پیش‌نویس قابل تأیید است.");
        if (route.ItemBaseUnitId != route.Item.BaseUnitId)
            throw new ProductionConflictException("MANUFACTURING_ROUTE_BASE_UNIT_CHANGED",
                "واحد پایه قلم پس از ساخت مسیر تغییر کرده است؛ مسیر جدید بسازید.");
        if (route.Operations.Count == 0 || route.Operations.Any(x => !x.WorkCenter.IsActive))
            throw new ProductionConflictException("MANUFACTURING_ROUTE_NOT_READY",
                "حداقل یک عملیات با مرکز کاری فعال الزامی است.");
        var current = await db.ManufacturingRoutes.SingleOrDefaultAsync(x => x.ItemId == itemId &&
            x.Status == ManufacturingRouteStatuses.Approved, ct);
        current?.Obsolete();
        route.Approve();
        await Save(db, ct);
        await tx.CommitAsync(ct);
        var response = (await GetRoute(id, ct))!;
        await Emit("ManufacturingRoute", id, "APPROVE", response, request.Reason, ct);
        return response;
    }

    public async Task<ManufacturingRouteScheduleResponse> Preview(Guid id, DateOnly completionDate,
        CancellationToken ct)
    {
        var route = await GetRoute(id, ct) ?? throw new KeyNotFoundException();
        if (route.Status != ManufacturingRouteStatuses.Approved)
            throw new ProductionConflictException("APPROVED_MANUFACTURING_ROUTE_REQUIRED",
                "پیش‌نمایش عملیاتی فقط برای مسیر ساخت تأییدشده است.");
        var cursor = completionDate;
        var rows = new List<ManufacturingOperationScheduleResponse>();
        foreach (var operation in route.Operations.OrderByDescending(x => x.Sequence))
        {
            var end = cursor;
            var start = SubtractWorkingDays(end,
                operation.PreparationWorkingDays + operation.ExecutionWorkingDays,
                operation.WorkCenter.WorkingWeekMask);
            rows.Add(new(operation.Id, operation.Sequence, operation.WorkCenter.Id,
                operation.WorkCenter.Code, operation.WorkCenter.NameFa, start, end,
                operation.PreparationWorkingDays, operation.ExecutionWorkingDays));
            cursor = start;
        }
        rows.Reverse();
        return new(route.Id, route.Code, route.Item.Id, completionDate, cursor, rows);
    }

    public static DateOnly SubtractWorkingDays(DateOnly finish, int days, int workingWeekMask)
    {
        var cursor = finish;
        var remaining = days;
        while (remaining > 0)
        {
            cursor = cursor.AddDays(-1);
            if ((workingWeekMask & (1 << (int)cursor.DayOfWeek)) != 0) remaining--;
        }
        return cursor;
    }

    private static IQueryable<ManufacturingRoute> LoadRoutes(IQueryable<ManufacturingRoute> query) => query
        .Include(x => x.Item).ThenInclude(x => x.BaseUnit)
        .Include(x => x.Operations).ThenInclude(x => x.WorkCenter).ThenInclude(x => x.OrganizationalUnit)
        .AsSplitQuery();

    private static ProductionWorkCenterResponse WorkCenter(ProductionWorkCenter x) =>
        new(x.Id, x.BusinessNumber, x.NameFa, x.NameEn,
            new(x.OrganizationalUnit.Id, x.OrganizationalUnit.NameFa,
                x.OrganizationalUnit.NameEn, x.OrganizationalUnit.IsActive),
            x.WorkingWeekMask, x.IsActive, x.Version, x.CreatedAtUtc, x.UpdatedAtUtc);

    private static ManufacturingRouteResponse Route(ManufacturingRoute x) =>
        new(x.Id, x.BusinessNumber,
            new(x.Item.Id, x.Item.BusinessNumber, x.Item.ItemType, x.Item.NameFa, x.Item.NameEn,
                x.Item.IsActive, new(x.Item.BaseUnit.Id, x.Item.BaseUnit.Code, x.Item.BaseUnit.NameFa,
                    x.Item.BaseUnit.NameEn, x.Item.BaseUnit.Symbol, x.Item.BaseUnit.IsActive)),
            x.RevisionNumber, x.Status, x.Notes,
            x.Operations.Sum(o => o.PreparationWorkingDays + o.ExecutionWorkingDays),
            x.Version, x.CreatedAtUtc, x.UpdatedAtUtc,
            x.Operations.OrderBy(o => o.Sequence).Select(o => new ManufacturingRouteOperationResponse(
                o.Id, o.Sequence, WorkCenter(o.WorkCenter), o.PreparationWorkingDays,
                o.ExecutionWorkingDays, o.Notes)).ToList());

    private static ProductionItemRef ItemRef(Item x) => new(x.Id, x.BusinessNumber, x.ItemType,
        x.NameFa, x.NameEn, x.IsActive, new(x.BaseUnit.Id, x.BaseUnit.Code, x.BaseUnit.NameFa,
            x.BaseUnit.NameEn, x.BaseUnit.Symbol, x.BaseUnit.IsActive));

    private static void AddOperations(HadronDbContext db, Guid routeId,
        IReadOnlyList<ManufacturingRouteOperationInput> operations) =>
        db.ManufacturingRouteOperations.AddRange(operations.Select(x =>
            new ManufacturingRouteOperation(routeId, x.Sequence, x.WorkCenterId,
                x.PreparationWorkingDays, x.ExecutionWorkingDays, x.Notes)));

    private static async Task RequireActiveUnit(HadronDbContext db, Guid id, CancellationToken ct)
    {
        if (!await db.OrganizationalUnits.AnyAsync(x => x.Id == id && x.IsActive, ct))
            throw Validation("organizationalUnitId", "واحد سازمانی فعال الزامی است.");
    }

    private static async Task<Item> RequireBuildableItem(HadronDbContext db, Guid id, CancellationToken ct)
    {
        var item = await db.Items.Include(x => x.BaseUnit).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException();
        if (!item.IsActive || !item.BaseUnit.IsActive)
            throw Validation("itemId", "قلم و واحد پایه باید فعال باشند.");
        if (item.ItemType == ItemTypes.Product) return item;
        var method = await db.MaterialItemProfiles.Where(x => x.ItemId == id)
            .Select(x => x.SupplyMethod).SingleOrDefaultAsync(ct);
        if (method is not (SupplyMethods.Make or SupplyMethods.Hybrid))
            throw Validation("itemId", "مسیر ساخت فقط برای محصول یا قلم MAKE/HYBRID قابل تعریف است.");
        return item;
    }

    private static async Task RequireActiveWorkCenters(HadronDbContext db, IEnumerable<Guid> ids,
        CancellationToken ct)
    {
        var requested = ids.Distinct().ToArray();
        var count = await db.ProductionWorkCenters.CountAsync(x => requested.Contains(x.Id) && x.IsActive, ct);
        if (count != requested.Length) throw Validation("operations", "همه مراکز کاری باید فعال و معتبر باشند.");
    }

    private static void ValidateWorkCenter(string nameFa, int mask, long? version)
    {
        if (string.IsNullOrWhiteSpace(nameFa)) throw Validation("nameFa", "نام فارسی مرکز کاری الزامی است.");
        if (mask is < 1 or > 127) throw Validation("workingWeekMask", "حداقل یک روز کاری معتبر انتخاب کنید.");
        if (version.HasValue && version < 1) throw Validation("expectedVersion", "نسخه معتبر الزامی است.");
    }

    private static void ValidateOperations(IReadOnlyList<ManufacturingRouteOperationInput>? rows, long? version)
    {
        if (rows is null || rows.Count == 0) throw Validation("operations", "حداقل یک عملیات الزامی است.");
        if (rows.Select(x => x.Sequence).Distinct().Count() != rows.Count || rows.Any(x => x.Sequence < 10))
            throw Validation("operations", "ترتیب عملیات باید یکتا و از ۱۰ به بالا باشد.");
        if (rows.Any(x => x.WorkCenterId == Guid.Empty || x.PreparationWorkingDays < 0 ||
            x.ExecutionWorkingDays < 0 || x.PreparationWorkingDays + x.ExecutionWorkingDays == 0))
            throw Validation("operations", "مرکز کاری و حداقل یک روز آماده‌سازی یا اجرا برای هر عملیات الزامی است.");
        if (version.HasValue && version < 1) throw Validation("expectedVersion", "نسخه معتبر الزامی است.");
    }

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            throw Validation("page", "صفحه معتبر و اندازه ۱ تا ۱۰۰ الزامی است.");
    }

    private static void EnsureVersion(long actual, long expected)
    {
        if (actual != expected) throw new ProductionConflictException("STALE_VERSION",
            "نسخه ارسالی قدیمی است.");
    }

    private static ProductionValidationException Validation(string key, string message) =>
        ProductionValidationException.Single(key, message);

    private static async Task Save(HadronDbContext db, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        { throw new ProductionConflictException("STALE_VERSION", "نسخه ارسالی قدیمی است."); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException postgres)
        { throw new ProductionConflictException(postgres.ConstraintName ?? "PRODUCTION_ROUTING_INTEGRITY", postgres.MessageText); }
    }

    private static async Task<string> NextWorkCenterNumber(HadronDbContext db, CancellationToken ct) =>
        $"WC-{await NextSequence(db, "production.production_work_center_number_seq", ct):D4}";
    private static async Task<string> NextRouteNumber(HadronDbContext db, CancellationToken ct) =>
        $"RTE-{await NextSequence(db, "production.manufacturing_route_number_seq", ct):D4}";
    private static async Task<long> NextSequence(HadronDbContext db, string sequence, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand($"SELECT nextval('{sequence}')", connection);
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    private async Task Emit(string entityType, Guid id, string action, object value,
        string? reason, CancellationToken ct)
    {
        try
        {
            await auditSink.WriteAsync(new(entityType, id, action, DateTime.UtcNow,
                AuditEventContext.ActorId(accessor.HttpContext),
                AuditEventContext.CorrelationId(accessor.HttpContext), null,
                new Dictionary<string, object?> { ["Value"] = value }, reason), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { logger.LogError(ex, "Post-commit routing audit failed for {Type} {Id}", entityType, id); }
    }
}
