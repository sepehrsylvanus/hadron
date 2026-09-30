using System.Data;
using System.Security.Claims;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Bom.Domain;
using Hadron.Erp.Api.Modules.Catalog.Application;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Inventory.Domain;
using Hadron.Erp.Api.Modules.Identity.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Hadron.Erp.Api.Platform.Audit;
using Hadron.Erp.Api.Platform.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hadron.Erp.Api.Modules.Inventory.Application;

public sealed class OperationalItemBaseUnitChangeGuard(IDbContextFactory<HadronDbContext> factory)
    : IItemBaseUnitChangeGuard
{
    public async Task EnsureCanChange(Guid itemId, Guid currentUnitId, Guid requestedUnitId,
        CancellationToken cancellationToken)
    {
        if (currentUnitId == requestedUnitId) return;
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        if (await db.InventoryTransactions.AsNoTracking().AnyAsync(x => x.ItemId == itemId, cancellationToken))
            throw new ItemBaseUnitChangeBlockedException();

        var parentReferences = await db.BomRevisions.AsNoTracking()
            .Where(x => x.ParentItemId == itemId && x.Status != BomStatuses.Obsolete)
            .Select(x => new BomUnitReference(x.RevisionCode, x.Status, "PARENT", x.ParentBaseUnitId))
            .ToListAsync(cancellationToken);
        var childReferences = await db.BomLines.AsNoTracking()
            .Where(x => x.ChildItemId == itemId && x.BomRevision.Status != BomStatuses.Obsolete)
            .Select(x => new BomUnitReference(x.BomRevision.RevisionCode, x.BomRevision.Status, "CHILD",
                x.ChildBaseUnitId))
            .ToListAsync(cancellationToken);
        var references = parentReferences.Concat(childReferences).ToArray();
        var conflicts = references.Where(x => x.SnapshotUnitId != requestedUnitId).ToArray();
        if (conflicts.Length > 0)
        {
            throw new ItemBaseUnitChangeBlockedException("ITEM_BASE_UNIT_LOCKED_BY_BOM",
                $"تغییر واحد پایه، Snapshot تعداد {conflicts.Length} ساختار BOM را نامعتبر می‌کند. " +
                "ابتدا Revisionهای اعلام‌شده را تعیین تکلیف کنید؛ Snapshot BOM به‌صورت خودکار بازنویسی نمی‌شود.",
                new
                {
                    currentUnitId,
                    requestedUnitId,
                    references = conflicts.Select(x => new
                    {
                        x.RevisionCode,
                        x.Status,
                        x.Role,
                        x.SnapshotUnitId
                    }).ToArray()
                });
        }
    }

    private sealed record BomUnitReference(string RevisionCode, string Status, string Role, Guid SnapshotUnitId);
}

public sealed class InventoryService(
    IDbContextFactory<HadronDbContext> factory,
    TimeProvider timeProvider,
    IAuditEventSink auditSink,
    IHttpContextAccessor accessor,
    ILogger<InventoryService> logger)
{
    public DateOnly TehranToday => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
        timeProvider.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran")).DateTime);

    public async Task<WarehouseListResponse> ListWarehouses(string? search, bool includeInactive,
        string? warehouseType, int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Warehouses.AsNoTracking().Where(x => includeInactive || x.IsActive);
        if (!string.IsNullOrWhiteSpace(warehouseType))
        {
            var normalizedType = NormalizeWarehouseType(warehouseType);
            query = query.Where(x => x.WarehouseType == normalizedType);
        }
        var scope = await WarehouseScope(db, ct);
        if (scope is not null) query = query.Where(x => scope.Contains(x.Id));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.BusinessNumber.Contains(term) || x.NameFa.Contains(term) ||
                (x.NameEn != null && x.NameEn.Contains(term)));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.BusinessNumber).Skip((page - 1) * pageSize)
            .Take(pageSize).ToListAsync(ct);
        var users = await ResponsibleUsers(db, rows.Select(x => x.PrimaryResponsibleUserId), ct);
        return new(rows.Select(x => WarehouseResponseOf(x,
            x.PrimaryResponsibleUserId.HasValue ? users.GetValueOrDefault(x.PrimaryResponsibleUserId.Value) : null))
            .ToList(), page, pageSize, total);
    }

    public async Task<WarehouseResponse?> GetWarehouse(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await EnsureWarehouseAccess(db, [id], ct);
        var warehouse = await db.Warehouses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (warehouse is null) return null;
        var user = warehouse.PrimaryResponsibleUserId.HasValue
            ? await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == warehouse.PrimaryResponsibleUserId.Value, ct)
            : null;
        return WarehouseResponseOf(warehouse, user);
    }

    public async Task<WarehouseResponsibleUserListResponse> ListResponsibleUsers(string? search,
        int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Users.AsNoTracking().Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.UserName!.Contains(term) || x.FirstName.Contains(term) ||
                x.LastName.Contains(term) || x.Email!.Contains(term));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.LastName).ThenBy(x => x.FirstName)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(rows.Select(x => new WarehouseResponsibleUserRef(x.Id, x.UserName!, x.FullName,
            $"/api/auth/users/{x.Id}/photo", x.IsActive)).ToList(), page, pageSize, total);
    }

    public async Task<WarehouseResponse> CreateWarehouse(CreateWarehouseRequest request, CancellationToken ct)
    {
        var warehouseType = NormalizeWarehouseType(request.WarehouseType);
        ValidateWarehouse(request.NameFa, request.NameEn, request.AddressFa, request.AddressEn,
            request.PrimaryResponsibleUserId, warehouseType, request.Notes, null);
        await using var db = await factory.CreateDbContextAsync(ct);
        await EnsureAllWarehouseScope(db, ct);
        var responsible = await ResponsibleUserForAssignment(db, request.PrimaryResponsibleUserId, ct);
        var warehouse = new Warehouse(await NextWarehouseNumber(db, ct), request.NameFa.Trim(),
            Clean(request.NameEn), request.AddressFa.Trim(), Clean(request.AddressEn),
            responsible.Id, warehouseType, Clean(request.Notes));
        db.Warehouses.Add(warehouse);
        await Save(db, ct);
        var response = WarehouseResponseOf(warehouse, responsible);
        await Emit("Warehouse", warehouse.Id, "CREATE", null, Snapshot(response), null, ct);
        return response;
    }

    public async Task<WarehouseResponse> UpdateWarehouse(Guid id, UpdateWarehouseRequest request,
        CancellationToken ct)
    {
        var warehouseType = NormalizeWarehouseType(request.WarehouseType);
        ValidateWarehouse(request.NameFa, request.NameEn, request.AddressFa, request.AddressEn,
            request.PrimaryResponsibleUserId, warehouseType, request.Notes,
            request.ExpectedVersion);
        await using var db = await factory.CreateDbContextAsync(ct);
        await EnsureWarehouseAccess(db, [id], ct);
        var responsible = await ResponsibleUserForAssignment(db, request.PrimaryResponsibleUserId, ct);
        var warehouse = await db.Warehouses.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException();
        EnsureVersion(warehouse.Version, request.ExpectedVersion);
        var previousResponsible = warehouse.PrimaryResponsibleUserId.HasValue
            ? await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == warehouse.PrimaryResponsibleUserId.Value, ct)
            : null;
        var before = Snapshot(WarehouseResponseOf(warehouse, previousResponsible));
        warehouse.Edit(request.NameFa.Trim(), Clean(request.NameEn), request.AddressFa.Trim(),
            Clean(request.AddressEn), responsible.Id, warehouseType, Clean(request.Notes));
        await Save(db, ct);
        var response = WarehouseResponseOf(warehouse, responsible);
        await Emit("Warehouse", warehouse.Id, "EDIT", before, Snapshot(response), null, ct);
        return response;
    }

    public async Task<WarehouseResponse> ChangeWarehouseState(Guid id, bool active,
        ChangeWarehouseStateRequest request, CancellationToken ct)
    {
        if (request.ExpectedVersion < 1) throw Validation("expectedVersion", "نسخه معتبر الزامی است.");
        await using var db = await factory.CreateDbContextAsync(ct);
        await EnsureWarehouseAccess(db, [id], ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockWarehouseInventory(db, id, ct);
        await LockWarehouseRow(db, id, ct);
        var warehouse = await db.Warehouses.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException();
        EnsureVersion(warehouse.Version, request.ExpectedVersion);
        if (!active)
        {
            var hasStock = await db.InventoryLedgerEffects.AsNoTracking()
                .Where(x => x.WarehouseId == id)
                .GroupBy(x => x.ItemId).AnyAsync(x => x.Sum(v => v.QuantityEffect) != 0, ct);
            if (hasStock)
                throw new InventoryConflictException("WAREHOUSE_HAS_NONZERO_STOCK",
                    "انبار دارای موجودی غیرصفر است و قابل غیرفعال‌سازی نیست.");
        }
        var responsible = warehouse.PrimaryResponsibleUserId.HasValue
            ? await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == warehouse.PrimaryResponsibleUserId.Value, ct)
            : null;
        var before = Snapshot(WarehouseResponseOf(warehouse, responsible));
        warehouse.ChangeState(active);
        await Save(db, ct);
        await tx.CommitAsync(ct);
        var response = WarehouseResponseOf(warehouse, responsible);
        await Emit("Warehouse", warehouse.Id, active ? "REACTIVATE" : "DEACTIVATE", before,
            Snapshot(response), request.Reason, ct);
        return response;
    }

    public async Task<InventoryItemListResponse> ListItems(string? search, string? itemType,
        bool includeInactive, int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        if (itemType is not null && itemType is not (ItemTypes.Product or ItemTypes.Part or ItemTypes.Component))
            throw Validation("itemType", "نوع قلم معتبر نیست.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Items.AsNoTracking().Where(x => includeInactive || x.IsActive);
        if (itemType is not null) query = query.Where(x => x.ItemType == itemType);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.BusinessNumber.Contains(term) || x.NameFa.Contains(term) ||
                (x.NameEn != null && x.NameEn.Contains(term)));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.BusinessNumber).Skip((page - 1) * pageSize)
            .Take(pageSize).Include(x => x.BaseUnit).ToListAsync(ct);
        return new(items.Select(ItemRef).ToList(), page, pageSize, total);
    }

    public async Task<InventoryItemUnitConversionListResponse?> ItemUnitConversions(Guid itemId,
        bool includeInactive, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var item = await db.Items.AsNoTracking().Include(x => x.BaseUnit)
            .SingleOrDefaultAsync(x => x.Id == itemId, ct);
        if (item is null) return null;
        var rows = await db.InventoryItemUnitConversions.AsNoTracking()
            .Where(x => x.ItemId == itemId && (includeInactive || x.IsActive))
            .Include(x => x.TransactionUnit).Include(x => x.BaseUnit)
            .OrderBy(x => x.TransactionUnit.Code).ToListAsync(ct);
        var values = new List<InventoryItemUnitConversionResponse>
        {
            new(null, UnitRef(item.BaseUnit), UnitRef(item.BaseUnit), 1m, true,
                item.BaseUnit.IsActive, 1)
        };
        values.AddRange(rows.Select(x => new InventoryItemUnitConversionResponse(x.Id,
            UnitRef(x.TransactionUnit), UnitRef(x.BaseUnit), x.FactorToBase, false,
            x.IsActive, x.Version)));
        return new(ItemRef(item), values);
    }

    public async Task<InventoryItemUnitConversionResponse> CreateItemUnitConversion(Guid itemId,
        CreateInventoryItemUnitConversionRequest request, CancellationToken ct)
    {
        ValidateConversionFactor(request.FactorToBase);
        await using var db = await factory.CreateDbContextAsync(ct);
        var item = await db.Items.Include(x => x.BaseUnit).SingleOrDefaultAsync(x => x.Id == itemId, ct)
            ?? throw new KeyNotFoundException();
        if (!item.IsActive) throw new InventoryConflictException("INACTIVE_ITEM",
            "برای قلم غیرفعال واحد تراکنشی جدید تعریف نمی‌شود.");
        if (request.TransactionUnitId == item.BaseUnitId)
            throw Validation("transactionUnitId", "واحد پایه به‌صورت خودکار با ضریب یک در دسترس است.");
        var unit = await db.Units.SingleOrDefaultAsync(x => x.Id == request.TransactionUnitId, ct)
            ?? throw new KeyNotFoundException();
        if (!unit.IsActive || !item.BaseUnit.IsActive)
            throw new InventoryConflictException("INACTIVE_UNIT", "واحدهای تبدیل باید فعال باشند.");
        if (await db.InventoryItemUnitConversions.AnyAsync(x => x.ItemId == itemId &&
            x.TransactionUnitId == request.TransactionUnitId, ct))
            throw new InventoryConflictException("ITEM_UNIT_CONVERSION_EXISTS",
                "این واحد تراکنشی قبلاً برای قلم تعریف شده است.");
        var row = new InventoryItemUnitConversion(itemId, unit.Id, item.BaseUnitId,
            request.FactorToBase);
        db.InventoryItemUnitConversions.Add(row);
        await Save(db, ct);
        await Emit("InventoryItemUnitConversion", row.Id, "CREATE", null,
            ConversionSnapshot(row, unit), null, ct);
        return new(row.Id, UnitRef(unit), UnitRef(item.BaseUnit), row.FactorToBase,
            false, row.IsActive, row.Version);
    }

    public async Task<InventoryItemUnitConversionResponse> UpdateItemUnitConversion(Guid itemId,
        Guid conversionId, UpdateInventoryItemUnitConversionRequest request, CancellationToken ct)
    {
        ValidateConversionFactor(request.FactorToBase);
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.InventoryItemUnitConversions.Include(x => x.TransactionUnit)
            .Include(x => x.BaseUnit).SingleOrDefaultAsync(x => x.Id == conversionId &&
                x.ItemId == itemId, ct) ?? throw new KeyNotFoundException();
        EnsureVersion(row.Version, request.ExpectedVersion);
        var before = ConversionSnapshot(row, row.TransactionUnit);
        row.Edit(request.FactorToBase);
        await Save(db, ct);
        await Emit("InventoryItemUnitConversion", row.Id, "EDIT", before,
            ConversionSnapshot(row, row.TransactionUnit), null, ct);
        return new(row.Id, UnitRef(row.TransactionUnit), UnitRef(row.BaseUnit),
            row.FactorToBase, false, row.IsActive, row.Version);
    }

    public async Task<InventoryItemUnitConversionResponse> ChangeItemUnitConversionState(Guid itemId,
        Guid conversionId, bool active, ChangeInventoryItemUnitConversionStateRequest request,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.InventoryItemUnitConversions.Include(x => x.TransactionUnit)
            .Include(x => x.BaseUnit).SingleOrDefaultAsync(x => x.Id == conversionId &&
                x.ItemId == itemId, ct) ?? throw new KeyNotFoundException();
        EnsureVersion(row.Version, request.ExpectedVersion);
        if (active && (!row.TransactionUnit.IsActive || !row.BaseUnit.IsActive))
            throw new InventoryConflictException("INACTIVE_UNIT", "واحدهای تبدیل باید فعال باشند.");
        var before = ConversionSnapshot(row, row.TransactionUnit);
        row.ChangeState(active);
        await Save(db, ct);
        await Emit("InventoryItemUnitConversion", row.Id,
            active ? "REACTIVATE" : "DEACTIVATE", before,
            ConversionSnapshot(row, row.TransactionUnit), request.Reason, ct);
        return new(row.Id, UnitRef(row.TransactionUnit), UnitRef(row.BaseUnit),
            row.FactorToBase, false, row.IsActive, row.Version);
    }

    public async Task<InventoryBalanceListResponse> ListBalances(DateOnly? asOfDate, string? search,
        Guid? itemId, Guid? warehouseId, string? itemType, int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        var date = asOfDate ?? TehranToday;
        await using var db = await factory.CreateDbContextAsync(ct);
        var scope = await WarehouseScope(db, ct);
        var effects = db.InventoryLedgerEffects.AsNoTracking().Where(x => x.EffectiveDate <= date);
        if (scope is not null) effects = effects.Where(x => scope.Contains(x.WarehouseId));
        var balances = effects.GroupBy(x => new { x.ItemId, x.WarehouseId, x.UnitId })
            .Select(x => new { x.Key.ItemId, x.Key.WarehouseId, x.Key.UnitId, Balance = x.Sum(v => v.QuantityEffect) });
        var query = from balance in balances
                    join item in db.Items.AsNoTracking() on balance.ItemId equals item.Id
                    join warehouse in db.Warehouses.AsNoTracking() on balance.WarehouseId equals warehouse.Id
                    join unit in db.Units.AsNoTracking() on balance.UnitId equals unit.Id
                    select new { balance, item, warehouse, unit };
        if (itemId.HasValue) query = query.Where(x => x.item.Id == itemId.Value);
        if (warehouseId.HasValue) query = query.Where(x => x.warehouse.Id == warehouseId.Value);
        if (!string.IsNullOrWhiteSpace(itemType)) query = query.Where(x => x.item.ItemType == itemType);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.item.BusinessNumber.Contains(term) || x.item.NameFa.Contains(term) ||
                (x.item.NameEn != null && x.item.NameEn.Contains(term)) ||
                x.warehouse.BusinessNumber.Contains(term) || x.warehouse.NameFa.Contains(term) ||
                (x.warehouse.NameEn != null && x.warehouse.NameEn.Contains(term)));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.item.BusinessNumber).ThenBy(x => x.warehouse.BusinessNumber)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(date, rows.Select(x => new InventoryBalanceRow(
            ItemRef(x.item, x.unit), WarehouseRefOf(x.warehouse), x.balance.Balance,
            !x.warehouse.IsActive && x.balance.Balance != 0)).ToList(), page, pageSize, total);
    }

    public async Task<ItemInventoryDetailResponse?> GetItemInventory(Guid itemId, DateOnly? asOfDate,
        CancellationToken ct)
    {
        var date = asOfDate ?? TehranToday;
        await using var db = await factory.CreateDbContextAsync(ct);
        var scope = await WarehouseScope(db, ct);
        var item = await db.Items.AsNoTracking().Include(x => x.BaseUnit)
            .SingleOrDefaultAsync(x => x.Id == itemId, ct);
        if (item is null) return null;
        var grouped = await db.InventoryLedgerEffects.AsNoTracking()
            .Where(x => x.ItemId == itemId && x.EffectiveDate <= date)
            .Where(x => scope == null || scope.Contains(x.WarehouseId))
            .GroupBy(x => x.WarehouseId)
            .Select(x => new { WarehouseId = x.Key, Balance = x.Sum(v => v.QuantityEffect) })
            .ToListAsync(ct);
        var ids = grouped.Select(x => x.WarehouseId).ToArray();
        var warehouses = await db.Warehouses.AsNoTracking().Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var rows = grouped.OrderBy(x => warehouses[x.WarehouseId].BusinessNumber)
            .Select(x => new ItemWarehouseBalance(WarehouseRefOf(warehouses[x.WarehouseId]), x.Balance,
                !warehouses[x.WarehouseId].IsActive && x.Balance != 0)).ToList();
        var inTransit = await InTransitQuantity(db, itemId, scope, ct);
        var stagingWarehouseIds = warehouses.Values.Where(x =>
            x.WarehouseType == WarehouseTypes.ProductionStaging).Select(x => x.Id).ToHashSet();
        var staging = grouped.Where(x => stagingWarehouseIds.Contains(x.WarehouseId))
            .Sum(x => x.Balance);
        var physical = rows.Sum(x => x.ActualOnHand);
        var usable = physical - staging;
        return new(date, ItemRef(item), usable, staging, inTransit, physical + inTransit,
            rows.Any(x => x.InactiveWarehouseStockWarning), rows);
    }

    public async Task<InventoryOverviewResponse> Overview(DateOnly? asOfDate, CancellationToken ct)
    {
        var date = asOfDate ?? TehranToday;
        await using var db = await factory.CreateDbContextAsync(ct);
        var scope = await WarehouseScope(db, ct);
        var warehouses = db.Warehouses.AsNoTracking().AsQueryable();
        if (scope is not null) warehouses = warehouses.Where(x => scope.Contains(x.Id));
        var activeWarehouses = await warehouses.CountAsync(x => x.IsActive, ct);
        var inactiveWarehouses = await warehouses.CountAsync(x => !x.IsActive, ct);
        var effects = db.InventoryLedgerEffects.AsNoTracking().Where(x => x.EffectiveDate <= date);
        if (scope is not null) effects = effects.Where(x => scope.Contains(x.WarehouseId));
        var positions = await effects
            .GroupBy(x => new { x.ItemId, x.WarehouseId }).CountAsync(ct);
        var negative = await effects
            .GroupBy(x => new { x.ItemId, x.WarehouseId })
            .CountAsync(x => x.Sum(v => v.QuantityEffect) < 0, ct);
        var openStatuses = new[] { InventoryTransferStatuses.Dispatched, InventoryTransferStatuses.PartiallyReceived };
        var openTransfers = await db.InventoryTransfers.AsNoTracking()
            .Where(x => openStatuses.Contains(x.Status)).ToListAsync(ct);
        if (scope is not null)
            openTransfers = openTransfers.Where(x => scope.Contains(x.SourceWarehouseId) &&
                scope.Contains(x.DestinationWarehouseId)).ToList();
        var openIds = openTransfers.Select(x => x.Id).ToArray();
        var received = openIds.Length == 0 ? 0m : await db.InventoryTransferReceipts.AsNoTracking()
            .Where(x => openIds.Contains(x.TransferId) && x.InventoryTransactionId != null &&
                !db.InventoryTransactions.Any(reverse => reverse.OriginalTransactionId == x.InventoryTransactionId))
            .SumAsync(x => x.Quantity, ct);
        return new(date, activeWarehouses, inactiveWarehouses, positions,
            openTransfers.Sum(x => x.RequestedQuantity) - received, openTransfers.Count, negative);
    }

    public async Task<InventoryTransferListResponse> ListTransfers(string? search, string? status,
        Guid? warehouseId, int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        if (!string.IsNullOrWhiteSpace(status) && status is not (
            InventoryTransferStatuses.Draft or InventoryTransferStatuses.Dispatched or
            InventoryTransferStatuses.PartiallyReceived or InventoryTransferStatuses.Received or
            InventoryTransferStatuses.Cancelled))
            throw Validation("status", "وضعیت انتقال معتبر نیست.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = TransferQuery(db);
        var scope = await WarehouseScope(db, ct);
        if (scope is not null) query = query.Where(x => scope.Contains(x.SourceWarehouseId) &&
            scope.Contains(x.DestinationWarehouseId));
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        if (warehouseId.HasValue) query = query.Where(x => x.SourceWarehouseId == warehouseId.Value ||
            x.DestinationWarehouseId == warehouseId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.BusinessNumber.Contains(term) || x.Item.BusinessNumber.Contains(term) ||
                x.Item.NameFa.Contains(term) || (x.Item.NameEn != null && x.Item.NameEn.Contains(term)) ||
                x.SourceWarehouse.BusinessNumber.Contains(term) || x.DestinationWarehouse.BusinessNumber.Contains(term));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.BusinessNumber)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(await TransferResponses(db, rows, ct), page, pageSize, total);
    }

    public async Task<InventoryTransferResponse?> GetTransfer(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var scope = await WarehouseScope(db, ct);
        if (scope is not null && !await db.InventoryTransfers.AnyAsync(x => x.Id == id &&
            scope.Contains(x.SourceWarehouseId) && scope.Contains(x.DestinationWarehouseId), ct))
            throw new InventoryConflictException("WAREHOUSE_SCOPE_DENIED", "دسترسی به انبار انتقال وجود ندارد.");
        var transfer = await TransferQuery(db).SingleOrDefaultAsync(x => x.Id == id, ct);
        return transfer is null ? null : (await TransferResponses(db, [transfer], ct))[0];
    }

    public async Task<InventoryTransferResponse> CreateTransfer(CreateInventoryTransferRequest request,
        CancellationToken ct)
    {
        ValidateTransferShape(request.SourceWarehouseId, request.DestinationWarehouseId, request.Quantity);
        await using var db = await factory.CreateDbContextAsync(ct);
        await EnsureWarehouseAccess(db, [request.SourceWarehouseId, request.DestinationWarehouseId], ct);
        var item = await ItemForPosting(db, request.ItemId, ct);
        var conversion = await ResolveQuantity(db, item, request.Quantity,
            request.TransactionUnitId, ct);
        await WarehouseForPosting(db, request.SourceWarehouseId, ct);
        await WarehouseForPosting(db, request.DestinationWarehouseId, ct);
        var transfer = new InventoryTransfer(await NextTransferNumber(db, ct), item.Id, item.BaseUnitId,
            request.SourceWarehouseId, request.DestinationWarehouseId, conversion.BaseQuantity,
            request.Quantity, conversion.TransactionUnit.Id, conversion.FactorToBase,
            Clean(request.Reason), Actor());
        db.InventoryTransfers.Add(transfer);
        await Save(db, ct);
        var response = (await GetTransfer(transfer.Id, ct))!;
        await Emit("InventoryTransfer", transfer.Id, "CREATE_DRAFT", null, Snapshot(response),
            request.Reason, ct);
        return response;
    }

    public async Task<InventoryTransferResponse> UpdateTransfer(Guid id,
        UpdateInventoryTransferRequest request, CancellationToken ct)
    {
        ValidateTransferShape(request.SourceWarehouseId, request.DestinationWarehouseId, request.Quantity);
        await using var db = await factory.CreateDbContextAsync(ct);
        await EnsureWarehouseAccess(db, [request.SourceWarehouseId, request.DestinationWarehouseId], ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockTransferRow(db, id, ct);
        var transfer = await db.InventoryTransfers.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException();
        EnsureVersion(transfer.Version, request.ExpectedVersion);
        EnsureDraft(transfer);
        var item = await ItemForPosting(db, request.ItemId, ct);
        var conversion = await ResolveQuantity(db, item, request.Quantity,
            request.TransactionUnitId, ct);
        await WarehouseForPosting(db, request.SourceWarehouseId, ct);
        await WarehouseForPosting(db, request.DestinationWarehouseId, ct);
        var before = (await GetTransferSnapshot(db, transfer.Id, ct))!;
        transfer.Edit(item.Id, item.BaseUnitId, request.SourceWarehouseId,
            request.DestinationWarehouseId, conversion.BaseQuantity, request.Quantity,
            conversion.TransactionUnit.Id, conversion.FactorToBase, Clean(request.Reason));
        await Save(db, ct);
        await tx.CommitAsync(ct);
        var response = (await GetTransfer(id, ct))!;
        await Emit("InventoryTransfer", id, "EDIT_DRAFT", Snapshot(before), Snapshot(response),
            request.Reason, ct);
        return response;
    }

    public async Task<InventoryTransferResponse> DispatchTransfer(Guid id,
        DispatchInventoryTransferRequest request, CancellationToken ct)
    {
        ValidatePosting(1m, request.EffectiveDate);
        var identity = await TransferIdentity(id, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        await EnsureWarehouseAccess(db, [identity.SourceWarehouseId, identity.DestinationWarehouseId], ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockInventoryResources(db, identity.ItemId,
            [identity.SourceWarehouseId, identity.DestinationWarehouseId], ct);
        await LockTransferRow(db, id, ct);
        var transfer = await db.InventoryTransfers.SingleAsync(x => x.Id == id, ct);
        EnsureVersion(transfer.Version, request.ExpectedVersion);
        EnsureDraft(transfer);
        var item = await ItemForPosting(db, transfer.ItemId, ct);
        if (item.BaseUnitId != transfer.UnitId)
            throw new InventoryConflictException("BASE_UNIT_CHANGED", "واحد پایه قلم پس از ایجاد انتقال تغییر کرده است.");
        await WarehouseForPosting(db, transfer.SourceWarehouseId, ct);
        await WarehouseForPosting(db, transfer.DestinationWarehouseId, ct);
        var posting = new InventoryTransaction(InventoryTransactionTypes.Issue, transfer.ItemId,
            transfer.SourceWarehouseId, null, transfer.RequestedQuantity, transfer.UnitId,
            transfer.EnteredRequestedQuantity, transfer.TransactionUnitId,
            transfer.ConversionFactorToBase, request.EffectiveDate,
            Clean(request.Reason) ?? transfer.Reason, null,
            InventorySourceDocumentTypes.InternalTransferDispatch, transfer.Id, Actor());
        db.InventoryTransactions.Add(posting);
        transfer.Dispatch(posting.Id);
        await Save(db, ct);
        await tx.CommitAsync(ct);
        var response = (await GetTransfer(id, ct))!;
        await Emit("InventoryTransfer", id, "DISPATCH", null, Snapshot(response), request.Reason, ct);
        return response;
    }

    public async Task<InventoryTransferResponse> ReceiveTransfer(Guid id,
        ReceiveInventoryTransferRequest request, CancellationToken ct)
    {
        ValidatePosting(request.Quantity, request.EffectiveDate);
        var identity = await TransferIdentity(id, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        await EnsureWarehouseAccess(db, [identity.SourceWarehouseId, identity.DestinationWarehouseId], ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockInventoryResources(db, identity.ItemId,
            [identity.SourceWarehouseId, identity.DestinationWarehouseId], ct);
        await LockTransferRow(db, id, ct);
        var transfer = await db.InventoryTransfers.Include(x => x.DispatchTransaction)
            .Include(x => x.TransactionUnit)
            .SingleAsync(x => x.Id == id, ct);
        EnsureVersion(transfer.Version, request.ExpectedVersion);
        EnsureReceivable(transfer);
        if (request.EffectiveDate < transfer.DispatchTransaction!.EffectiveDate)
            throw Validation("effectiveDate", "تاریخ دریافت نمی‌تواند قبل از تاریخ ارسال باشد.");
        var item = await ItemForPosting(db, transfer.ItemId, ct);
        if (item.BaseUnitId != transfer.UnitId)
            throw new InventoryConflictException("BASE_UNIT_CHANGED", "واحد پایه قلم با snapshot انتقال سازگار نیست.");
        await WarehouseForPosting(db, transfer.DestinationWarehouseId, ct);
        var transactionUnitId = request.TransactionUnitId ?? transfer.TransactionUnitId;
        if (transactionUnitId != transfer.TransactionUnitId)
            throw Validation("transactionUnitId", "واحد دریافت باید با واحد ثبت‌شده انتقال یکسان باشد.");
        var conversion = await ResolveQuantity(db, item, request.Quantity, transactionUnitId, ct);
        if (conversion.FactorToBase != transfer.ConversionFactorToBase)
            conversion = new QuantityConversion(request.Quantity,
                NormalizeBaseQuantity(request.Quantity * transfer.ConversionFactorToBase),
                transfer.TransactionUnit, transfer.ConversionFactorToBase);
        var received = await EffectiveReceivedQuantity(db, id, ct);
        if (conversion.BaseQuantity > transfer.RequestedQuantity - received)
            throw new InventoryConflictException("TRANSFER_OVER_RECEIPT",
                "مقدار دریافت از مانده انتقال بیشتر است.", new { OpenQuantity = transfer.RequestedQuantity - received });
        var receipt = new InventoryTransferReceipt(id, conversion.BaseQuantity, request.Quantity,
            transfer.TransactionUnitId, transfer.ConversionFactorToBase, request.EffectiveDate,
            Clean(request.Reason), Actor());
        var posting = new InventoryTransaction(InventoryTransactionTypes.Receipt, transfer.ItemId,
            transfer.DestinationWarehouseId, null, conversion.BaseQuantity, transfer.UnitId,
            request.Quantity, transfer.TransactionUnitId, transfer.ConversionFactorToBase,
            request.EffectiveDate, Clean(request.Reason), null,
            InventorySourceDocumentTypes.InternalTransferReceipt, receipt.Id, Actor());
        receipt.LinkTransaction(posting.Id);
        db.InventoryTransferReceipts.Add(receipt);
        db.InventoryTransactions.Add(posting);
        transfer.SetEffectiveReceivedQuantity(received + conversion.BaseQuantity);
        await Save(db, ct);
        await tx.CommitAsync(ct);
        var response = (await GetTransfer(id, ct))!;
        await Emit("InventoryTransfer", id, "RECEIVE", null, Snapshot(response), request.Reason, ct);
        return response;
    }

    public async Task<InventoryTransferResponse> CancelTransfer(Guid id,
        CancelInventoryTransferRequest request, CancellationToken ct)
    {
        var reason = Clean(request.Reason) ?? throw Validation("reason", "دلیل لغو الزامی است.");
        var identity = await TransferIdentity(id, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        await EnsureWarehouseAccess(db, [identity.SourceWarehouseId, identity.DestinationWarehouseId], ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockInventoryResources(db, identity.ItemId,
            [identity.SourceWarehouseId, identity.DestinationWarehouseId], ct);
        await LockTransferRow(db, id, ct);
        var transfer = await db.InventoryTransfers.Include(x => x.DispatchTransaction)
            .SingleAsync(x => x.Id == id, ct);
        EnsureVersion(transfer.Version, request.ExpectedVersion);
        if (transfer.Status == InventoryTransferStatuses.Cancelled)
            throw new InventoryConflictException("TRANSFER_ALREADY_CANCELLED", "انتقال قبلاً لغو شده است.");
        if (transfer.Status == InventoryTransferStatuses.Received)
            throw new InventoryConflictException("RECEIVED_TRANSFER_CANNOT_BE_CANCELLED",
                "انتقال دریافت‌شده قابل لغو نیست؛ ابتدا دریافت باید برگشت داده شود.");
        var received = await EffectiveReceivedQuantity(db, id, ct);
        if (received > 0)
            throw new InventoryConflictException("PARTIALLY_RECEIVED_TRANSFER_CANNOT_BE_CANCELLED",
                "انتقال دارای دریافت مؤثر است؛ ابتدا دریافت‌ها باید برگشت داده شوند.");
        if (transfer.DispatchTransactionId.HasValue)
        {
            if (await db.InventoryTransactions.AnyAsync(x =>
                x.OriginalTransactionId == transfer.DispatchTransactionId.Value, ct))
                throw new InventoryConflictException("TRANSFER_DISPATCH_ALREADY_REVERSED",
                    "ارسال انتقال قبلاً برگشت داده شده است.");
            var original = transfer.DispatchTransaction!;
            db.InventoryTransactions.Add(new InventoryTransaction(InventoryTransactionTypes.Reversal,
                original.ItemId, original.WarehouseId, null, original.Quantity, original.UnitId,
                original.EnteredQuantity, original.TransactionUnitId, original.ConversionFactorToBase,
                original.EffectiveDate, reason, original.Id, null, null, Actor()));
        }
        transfer.Cancel(reason);
        await Save(db, ct);
        await tx.CommitAsync(ct);
        var response = (await GetTransfer(id, ct))!;
        await Emit("InventoryTransfer", id, "CANCEL", null, Snapshot(response), reason, ct);
        return response;
    }

    public async Task<InventoryTransferResponse> ReverseTransferReceipt(Guid transferId, Guid receiptId,
        ReverseInventoryTransferReceiptRequest request, CancellationToken ct)
    {
        var reason = Clean(request.Reason) ?? throw Validation("reason", "دلیل برگشت الزامی است.");
        var identity = await TransferIdentity(transferId, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        await EnsureWarehouseAccess(db, [identity.SourceWarehouseId, identity.DestinationWarehouseId], ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockInventoryResources(db, identity.ItemId,
            [identity.SourceWarehouseId, identity.DestinationWarehouseId], ct);
        await LockTransferRow(db, transferId, ct);
        var transfer = await db.InventoryTransfers.SingleAsync(x => x.Id == transferId, ct);
        EnsureVersion(transfer.Version, request.ExpectedVersion);
        if (transfer.Status == InventoryTransferStatuses.Cancelled)
            throw new InventoryConflictException("CANCELLED_TRANSFER_IMMUTABLE", "انتقال لغوشده تغییرپذیر نیست.");
        var receipt = await db.InventoryTransferReceipts.SingleOrDefaultAsync(x =>
            x.Id == receiptId && x.TransferId == transferId, ct) ?? throw new KeyNotFoundException();
        if (!receipt.InventoryTransactionId.HasValue) throw new InvalidOperationException("Transfer receipt is not posted.");
        if (await db.InventoryTransactions.AnyAsync(x => x.OriginalTransactionId == receipt.InventoryTransactionId, ct))
            throw new InventoryConflictException("TRANSACTION_ALREADY_REVERSED", "این دریافت قبلاً برگشت داده شده است.");
        var original = await db.InventoryTransactions.SingleAsync(x => x.Id == receipt.InventoryTransactionId, ct);
        db.InventoryTransactions.Add(new InventoryTransaction(InventoryTransactionTypes.Reversal,
            original.ItemId, original.WarehouseId, null, original.Quantity, original.UnitId,
            original.EnteredQuantity, original.TransactionUnitId, original.ConversionFactorToBase,
            original.EffectiveDate, reason, original.Id, null, null, Actor()));
        var effectiveBefore = await EffectiveReceivedQuantity(db, transferId, ct);
        transfer.SetEffectiveReceivedQuantity(effectiveBefore - receipt.Quantity);
        await Save(db, ct);
        await tx.CommitAsync(ct);
        var response = (await GetTransfer(transferId, ct))!;
        await Emit("InventoryTransfer", transferId, "REVERSE_RECEIPT", null, Snapshot(response), reason, ct);
        return response;
    }

    public async Task<InventoryTransactionListResponse> ListTransactions(Guid? itemId,
        Guid? warehouseId, string? transactionType, DateOnly? from, DateOnly? to,
        int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        transactionType = string.IsNullOrWhiteSpace(transactionType) ? null : transactionType.Trim();
        if (transactionType is not null && !InventoryTransactionTypes.All.Contains(transactionType))
            throw Validation("transactionType", "نوع گردش موجودی معتبر نیست.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = TransactionQuery(db);
        var scope = await WarehouseScope(db, ct);
        if (scope is not null) query = query.Where(x => scope.Contains(x.WarehouseId) &&
            (!x.DestinationWarehouseId.HasValue || scope.Contains(x.DestinationWarehouseId.Value)));
        if (itemId.HasValue) query = query.Where(x => x.ItemId == itemId.Value);
        if (warehouseId.HasValue) query = query.Where(x => x.WarehouseId == warehouseId.Value ||
            x.DestinationWarehouseId == warehouseId.Value);
        if (transactionType is not null) query = query.Where(x => x.TransactionType == transactionType);
        if (from.HasValue) query = query.Where(x => x.EffectiveDate >= from.Value);
        if (to.HasValue) query = query.Where(x => x.EffectiveDate <= to.Value);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(rows.Select(TransactionResponse).ToList(), page, pageSize, total);
    }

    public async Task<InventoryTransactionResponse?> GetTransaction(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var scope = await WarehouseScope(db, ct);
        if (scope is not null && !await db.InventoryTransactions.AnyAsync(x => x.Id == id &&
            scope.Contains(x.WarehouseId) && (!x.DestinationWarehouseId.HasValue ||
                scope.Contains(x.DestinationWarehouseId.Value)), ct))
            throw new InventoryConflictException("WAREHOUSE_SCOPE_DENIED", "دسترسی به انبار گردش وجود ندارد.");
        var transaction = await TransactionQuery(db).SingleOrDefaultAsync(x => x.Id == id, ct);
        return transaction is null ? null : TransactionResponse(transaction);
    }

    public Task<InventoryTransactionResponse> PostOpening(PostInventoryRequest request, CancellationToken ct) =>
        PostSimple(InventoryTransactionTypes.Opening, request, ct);
    public Task<InventoryTransactionResponse> PostReceipt(PostInventoryRequest request, CancellationToken ct) =>
        PostSimple(InventoryTransactionTypes.Receipt, request, ct);
    public Task<InventoryTransactionResponse> PostIssue(PostInventoryRequest request, CancellationToken ct) =>
        PostSimple(InventoryTransactionTypes.Issue, request, ct);

    public Task<InventoryTransactionResponse> PostAdjustment(PostAdjustmentRequest request, CancellationToken ct)
    {
        var direction = request.Direction.Trim().ToUpperInvariant();
        var type = direction switch
        {
            "IN" => InventoryTransactionTypes.AdjustmentIn,
            "OUT" => InventoryTransactionTypes.AdjustmentOut,
            _ => throw Validation("direction", "جهت تعدیل باید IN یا OUT باشد.")
        };
        return PostSimple(type, new(request.ItemId, request.WarehouseId, request.Quantity,
            request.EffectiveDate, request.Reason, request.TransactionUnitId), ct);
    }

    public async Task<InventoryTransactionResponse> PostTransfer(PostTransferRequest request,
        CancellationToken ct)
    {
        await Task.CompletedTask;
        throw new InventoryConflictException("USE_TRANSFER_WORKFLOW",
            "انتقال جدید باید از فرایند پیش‌نویس، ارسال و دریافت انتقال انبار ثبت شود.");
    }

    public async Task<InventoryTransactionResponse> Reverse(Guid originalId,
        ReverseInventoryTransactionRequest request, CancellationToken ct)
    {
        var reason = Clean(request.Reason) ?? throw Validation("reason", "دلیل برگشت الزامی است.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var identity = await db.InventoryTransactions.AsNoTracking().Where(x => x.Id == originalId)
            .Select(x => new { x.ItemId, x.WarehouseId, x.DestinationWarehouseId })
            .SingleOrDefaultAsync(ct) ?? throw new KeyNotFoundException();
        await EnsureWarehouseAccess(db, identity.DestinationWarehouseId.HasValue
            ? [identity.WarehouseId, identity.DestinationWarehouseId.Value]
            : [identity.WarehouseId], ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockInventoryResources(db, identity.ItemId,
            identity.DestinationWarehouseId.HasValue
                ? [identity.WarehouseId, identity.DestinationWarehouseId.Value]
                : [identity.WarehouseId], ct);
        await LockTransactionRow(db, originalId, ct);
        var original = await db.InventoryTransactions.SingleAsync(x => x.Id == originalId, ct);
        if (original.SourceDocumentType is InventorySourceDocumentTypes.InternalTransferDispatch or
            InventorySourceDocumentTypes.InternalTransferReceipt)
            throw new InventoryConflictException("USE_TRANSFER_WORKFLOW",
                "برگشت اثر انتقال باید از خود سند انتقال انجام شود.");
        if (original.TransactionType == InventoryTransactionTypes.Reversal)
            throw new InventoryConflictException("REVERSAL_CANNOT_BE_REVERSED",
                "گردش REVERSAL قابل برگشت نیست.");
        if (await db.InventoryTransactions.AnyAsync(x => x.OriginalTransactionId == originalId, ct))
            throw new InventoryConflictException("TRANSACTION_ALREADY_REVERSED",
                "این گردش قبلاً برگشت داده شده است.");
        var reversal = new InventoryTransaction(InventoryTransactionTypes.Reversal, original.ItemId,
            original.WarehouseId, original.DestinationWarehouseId, original.Quantity, original.UnitId,
            original.EnteredQuantity, original.TransactionUnitId, original.ConversionFactorToBase,
            original.EffectiveDate, reason, original.Id, null, null, Actor());
        db.InventoryTransactions.Add(reversal);
        await Save(db, ct);
        await tx.CommitAsync(ct);
        var response = (await GetTransaction(reversal.Id, ct))!;
        await Emit("InventoryTransaction", reversal.Id, "POST_REVERSAL", null,
            Snapshot(response), reason, ct);
        return response;
    }

    private async Task<InventoryTransactionResponse> PostSimple(string type,
        PostInventoryRequest request, CancellationToken ct)
    {
        ValidatePosting(request.Quantity, request.EffectiveDate);
        if (type is InventoryTransactionTypes.AdjustmentIn or InventoryTransactionTypes.AdjustmentOut &&
            Clean(request.Reason) is null)
            throw Validation("reason", "دلیل تعدیل الزامی است.");
        await using var db = await factory.CreateDbContextAsync(ct);
        await EnsureWarehouseAccess(db, [request.WarehouseId], ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockInventoryResources(db, request.ItemId, [request.WarehouseId], ct);
        var item = await ItemForPosting(db, request.ItemId, ct);
        var conversion = await ResolveQuantity(db, item, request.Quantity,
            request.TransactionUnitId, ct);
        await WarehouseForPosting(db, request.WarehouseId, ct);
        if (type == InventoryTransactionTypes.Opening && await db.InventoryTransactions.AnyAsync(x =>
            x.TransactionType == InventoryTransactionTypes.Opening && x.ItemId == request.ItemId &&
            x.WarehouseId == request.WarehouseId, ct))
            throw new InventoryConflictException("OPENING_ALREADY_EXISTS",
                "برای این قلم و انبار قبلاً Opening ثبت شده است.");
        var transaction = new InventoryTransaction(type, item.Id, request.WarehouseId, null,
            conversion.BaseQuantity, item.BaseUnitId, request.Quantity,
            conversion.TransactionUnit.Id, conversion.FactorToBase, request.EffectiveDate,
            Clean(request.Reason), null,
            null, null, Actor());
        db.InventoryTransactions.Add(transaction);
        await Save(db, ct);
        await tx.CommitAsync(ct);
        var response = (await GetTransaction(transaction.Id, ct))!;
        await Emit("InventoryTransaction", transaction.Id, $"POST_{type}", null,
            Snapshot(response), request.Reason, ct);
        return response;
    }

    private void ValidatePosting(decimal quantity, DateOnly effectiveDate)
    {
        if (quantity <= 0 || DecimalScale(quantity) > 6 || quantity > 999999999999.999999m)
            throw Validation("quantity", "مقدار باید مثبت و با دقت numeric(18,6) باشد.");
        if (effectiveDate > TehranToday)
            throw new InventoryConflictException("FUTURE_INVENTORY_POSTING_NOT_ALLOWED",
                "ثبت گردش فیزیکی برای تاریخ آینده مجاز نیست.");
    }

    private static async Task<Item> ItemForPosting(HadronDbContext db, Guid id, CancellationToken ct)
    {
        var item = await db.Items.Include(x => x.BaseUnit).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException();
        if (!item.IsActive) throw new InventoryConflictException("INACTIVE_ITEM",
            "برای قلم غیرفعال گردش عادی ثبت نمی‌شود.");
        if (!item.BaseUnit.IsActive) throw new InventoryConflictException("INACTIVE_UNIT",
            "واحد پایه قلم برای ثبت گردش جدید باید فعال باشد.");
        return item;
    }

    private static async Task<QuantityConversion> ResolveQuantity(HadronDbContext db, Item item,
        decimal enteredQuantity, Guid? transactionUnitId, CancellationToken ct)
    {
        var unitId = transactionUnitId ?? item.BaseUnitId;
        if (unitId == item.BaseUnitId)
            return new(enteredQuantity, NormalizeBaseQuantity(enteredQuantity), item.BaseUnit, 1m);
        var conversion = await db.InventoryItemUnitConversions.Include(x => x.TransactionUnit)
            .SingleOrDefaultAsync(x => x.ItemId == item.Id && x.TransactionUnitId == unitId, ct)
            ?? throw new InventoryConflictException("ITEM_UNIT_CONVERSION_NOT_FOUND",
                "برای واحد انتخاب‌شده تبدیل فعال این قلم تعریف نشده است.");
        if (!conversion.IsActive || !conversion.TransactionUnit.IsActive)
            throw new InventoryConflictException("INACTIVE_UNIT",
                "واحد تراکنشی یا تبدیل آن غیرفعال است.");
        if (conversion.BaseUnitId != item.BaseUnitId)
            throw new InventoryConflictException("BASE_UNIT_CHANGED",
                "واحد پایه قلم با snapshot تبدیل سازگار نیست.");
        return new(enteredQuantity,
            NormalizeBaseQuantity(enteredQuantity * conversion.FactorToBase),
            conversion.TransactionUnit, conversion.FactorToBase);
    }

    private static decimal NormalizeBaseQuantity(decimal value)
    {
        var rounded = decimal.Round(value, 6, MidpointRounding.AwayFromZero);
        if (rounded <= 0 || rounded > 999999999999.999999m)
            throw Validation("quantity", "مقدار تبدیل‌شده باید مثبت و در دامنه numeric(18,6) باشد.");
        return rounded;
    }

    private static void ValidateConversionFactor(decimal factor)
    {
        if (factor <= 0 || DecimalScale(factor) > 12 || factor > 9999999999999999.999999999999m)
            throw Validation("factorToBase", "ضریب تبدیل باید مثبت و با دقت numeric(28,12) باشد.");
    }

    private sealed record QuantityConversion(decimal EnteredQuantity, decimal BaseQuantity,
        Unit TransactionUnit, decimal FactorToBase);

    private static async Task<Warehouse> WarehouseForPosting(HadronDbContext db, Guid id, CancellationToken ct)
    {
        var warehouse = await db.Warehouses.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException();
        if (!warehouse.IsActive) throw new InventoryConflictException("INACTIVE_WAREHOUSE",
            "برای انبار غیرفعال گردش عادی ثبت نمی‌شود.");
        return warehouse;
    }

    private static IQueryable<InventoryTransfer> TransferQuery(HadronDbContext db) =>
        db.InventoryTransfers.AsNoTracking()
            .Include(x => x.Item).ThenInclude(x => x.BaseUnit)
            .Include(x => x.Unit)
            .Include(x => x.TransactionUnit)
            .Include(x => x.SourceWarehouse)
            .Include(x => x.DestinationWarehouse)
            .Include(x => x.DispatchTransaction);

    private static async Task<IReadOnlyList<InventoryTransferResponse>> TransferResponses(
        HadronDbContext db, IReadOnlyList<InventoryTransfer> transfers, CancellationToken ct)
    {
        if (transfers.Count == 0) return [];
        var ids = transfers.Select(x => x.Id).ToArray();
        var receipts = await db.InventoryTransferReceipts.AsNoTracking()
            .Where(x => ids.Contains(x.TransferId) && x.InventoryTransactionId != null)
            .Include(x => x.TransactionUnit)
            .OrderBy(x => x.EffectiveDate).ThenBy(x => x.CreatedAtUtc).ToListAsync(ct);
        var transactionIds = receipts.Select(x => x.InventoryTransactionId!.Value).ToArray();
        var reversed = transactionIds.Length == 0
            ? new HashSet<Guid>()
            : (await db.InventoryTransactions.AsNoTracking()
                .Where(x => x.OriginalTransactionId != null && transactionIds.Contains(x.OriginalTransactionId.Value))
                .Select(x => x.OriginalTransactionId!.Value).ToListAsync(ct)).ToHashSet();
        var grouped = receipts.GroupBy(x => x.TransferId).ToDictionary(x => x.Key, x => x.ToList());
        return transfers.Select(transfer =>
        {
            var transferReceipts = grouped.GetValueOrDefault(transfer.Id, []);
            var effectiveReceived = transferReceipts.Where(x => !reversed.Contains(x.InventoryTransactionId!.Value))
                .Sum(x => x.Quantity);
            var inTransit = transfer.Status is InventoryTransferStatuses.Dispatched or
                InventoryTransferStatuses.PartiallyReceived
                ? transfer.RequestedQuantity - effectiveReceived
                : 0m;
            return new InventoryTransferResponse(transfer.Id, transfer.BusinessNumber, transfer.Status,
                ItemRef(transfer.Item), UnitRef(transfer.Unit), WarehouseRefOf(transfer.SourceWarehouse),
                WarehouseRefOf(transfer.DestinationWarehouse), transfer.RequestedQuantity,
                transfer.EnteredRequestedQuantity, UnitRef(transfer.TransactionUnit),
                transfer.ConversionFactorToBase,
                transfer.DispatchTransactionId.HasValue ? transfer.RequestedQuantity : 0m,
                effectiveReceived, inTransit, transfer.DispatchTransaction?.EffectiveDate,
                transfer.Reason, transfer.CancellationReason, transfer.ActorIdentifier, transfer.Version,
                transfer.CreatedAtUtc, transfer.UpdatedAtUtc,
                transferReceipts.Select(receipt => new InventoryTransferReceiptResponse(receipt.Id,
                    receipt.Quantity, receipt.EnteredQuantity, UnitRef(receipt.TransactionUnit),
                    receipt.ConversionFactorToBase, receipt.EffectiveDate, receipt.Reason,
                    receipt.ActorIdentifier,
                    receipt.InventoryTransactionId!.Value,
                    reversed.Contains(receipt.InventoryTransactionId.Value), receipt.CreatedAtUtc)).ToList());
        }).ToList();
    }

    private async Task<InventoryTransferResponse?> GetTransferSnapshot(HadronDbContext db, Guid id,
        CancellationToken ct)
    {
        var row = await TransferQuery(db).SingleOrDefaultAsync(x => x.Id == id, ct);
        return row is null ? null : (await TransferResponses(db, [row], ct))[0];
    }

    private async Task<(Guid ItemId, Guid SourceWarehouseId, Guid DestinationWarehouseId)>
        TransferIdentity(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var value = await db.InventoryTransfers.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.ItemId, x.SourceWarehouseId, x.DestinationWarehouseId })
            .SingleOrDefaultAsync(ct) ?? throw new KeyNotFoundException();
        return (value.ItemId, value.SourceWarehouseId, value.DestinationWarehouseId);
    }

    private static Task<decimal> EffectiveReceivedQuantity(HadronDbContext db, Guid transferId,
        CancellationToken ct) => db.InventoryTransferReceipts.AsNoTracking()
        .Where(x => x.TransferId == transferId && x.InventoryTransactionId != null &&
            !db.InventoryTransactions.Any(reverse => reverse.OriginalTransactionId == x.InventoryTransactionId))
        .SumAsync(x => x.Quantity, ct);

    private static async Task<decimal> InTransitQuantity(HadronDbContext db, Guid itemId,
        HashSet<Guid>? scope, CancellationToken ct)
    {
        var statuses = new[] { InventoryTransferStatuses.Dispatched, InventoryTransferStatuses.PartiallyReceived };
        var query = db.InventoryTransfers.AsNoTracking()
            .Where(x => x.ItemId == itemId && statuses.Contains(x.Status));
        if (scope is not null) query = query.Where(x => scope.Contains(x.SourceWarehouseId) &&
            scope.Contains(x.DestinationWarehouseId));
        var transfers = await query
            .Select(x => new { x.Id, x.RequestedQuantity }).ToListAsync(ct);
        if (transfers.Count == 0) return 0m;
        var ids = transfers.Select(x => x.Id).ToArray();
        var received = await db.InventoryTransferReceipts.AsNoTracking()
            .Where(x => ids.Contains(x.TransferId) && x.InventoryTransactionId != null &&
                !db.InventoryTransactions.Any(reverse => reverse.OriginalTransactionId == x.InventoryTransactionId))
            .SumAsync(x => x.Quantity, ct);
        return transfers.Sum(x => x.RequestedQuantity) - received;
    }

    private async Task<HashSet<Guid>?> WarehouseScope(HadronDbContext db, CancellationToken ct)
    {
        var principal = accessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true ||
            principal.HasClaim(SecurityClaimTypes.TestAuthentication, "true") ||
            principal.IsInRole(SystemRoleNames.SystemAdministrator))
            return null;
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return [];
        var scopes = await db.UserDataScopes.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => new { x.ScopeType, x.ScopeValue }).ToListAsync(ct);
        if (scopes.Any(x => x.ScopeType == DataScopeTypes.All)) return null;
        return scopes.Where(x => x.ScopeType == DataScopeTypes.Warehouse &&
                Guid.TryParse(x.ScopeValue, out _))
            .Select(x => Guid.Parse(x.ScopeValue!)).ToHashSet();
    }

    private async Task EnsureAllWarehouseScope(HadronDbContext db, CancellationToken ct)
    {
        if (await WarehouseScope(db, ct) is not null)
            throw new InventoryConflictException("WAREHOUSE_SCOPE_DENIED",
                "ایجاد انبار جدید به محدوده کامل انبارها نیاز دارد.");
    }

    private async Task EnsureWarehouseAccess(HadronDbContext db, IEnumerable<Guid> warehouseIds,
        CancellationToken ct)
    {
        var scope = await WarehouseScope(db, ct);
        if (scope is null) return;
        var requested = warehouseIds.Distinct().ToArray();
        if (requested.Any(x => !scope.Contains(x)))
            throw new InventoryConflictException("WAREHOUSE_SCOPE_DENIED",
                "دسترسی به یک یا چند انبار انتخاب‌شده وجود ندارد.");
    }

    private static void ValidateTransferShape(Guid sourceWarehouseId, Guid destinationWarehouseId,
        decimal quantity)
    {
        if (sourceWarehouseId == Guid.Empty) throw Validation("sourceWarehouseId", "انبار مبدأ الزامی است.");
        if (destinationWarehouseId == Guid.Empty) throw Validation("destinationWarehouseId", "انبار مقصد الزامی است.");
        if (sourceWarehouseId == destinationWarehouseId)
            throw Validation("destinationWarehouseId", "انبار مبدأ و مقصد باید متفاوت باشند.");
        if (quantity <= 0 || DecimalScale(quantity) > 6 || quantity > 999999999999.999999m)
            throw Validation("quantity", "مقدار باید مثبت و با دقت numeric(18,6) باشد.");
    }

    private static void EnsureDraft(InventoryTransfer transfer)
    {
        if (transfer.Status != InventoryTransferStatuses.Draft)
            throw new InventoryConflictException("TRANSFER_NOT_DRAFT",
                "فقط انتقال پیش‌نویس قابل ویرایش یا ارسال است.");
    }

    private static void EnsureReceivable(InventoryTransfer transfer)
    {
        if (transfer.Status is not (InventoryTransferStatuses.Dispatched or
            InventoryTransferStatuses.PartiallyReceived))
            throw new InventoryConflictException("TRANSFER_NOT_RECEIVABLE",
                "فقط انتقال ارسال‌شده و دارای مانده قابل دریافت است.");
    }

    private static IQueryable<InventoryTransaction> TransactionQuery(HadronDbContext db) =>
        db.InventoryTransactions.AsNoTracking().Include(x => x.Item).ThenInclude(x => x.BaseUnit)
            .Include(x => x.Unit).Include(x => x.TransactionUnit)
            .Include(x => x.Warehouse).Include(x => x.DestinationWarehouse);

    private static InventoryTransactionResponse TransactionResponse(InventoryTransaction row) =>
        new(row.Id, row.TransactionType, ItemRef(row.Item), WarehouseRefOf(row.Warehouse),
            row.DestinationWarehouse is null ? null : WarehouseRefOf(row.DestinationWarehouse),
            row.Quantity, UnitRef(row.Unit), row.EnteredQuantity, UnitRef(row.TransactionUnit),
            row.ConversionFactorToBase, row.EffectiveDate, row.Reason, row.OriginalTransactionId,
            row.SourceDocumentType, row.SourceDocumentId, row.ActorIdentifier, row.CreatedAtUtc);

    private static InventoryItemRef ItemRef(Item item) => ItemRef(item, item.BaseUnit);
    private static InventoryItemRef ItemRef(Item item, Unit unit) =>
        new(item.Id, item.BusinessNumber, item.ItemType, item.NameFa, item.NameEn, item.IsActive, UnitRef(unit));
    private static InventoryUnitRef UnitRef(Unit unit) =>
        new(unit.Id, unit.Code, unit.NameFa, unit.NameEn, unit.Symbol, unit.IsActive);
    private static WarehouseRef WarehouseRefOf(Warehouse warehouse) =>
        new(warehouse.Id, warehouse.BusinessNumber, warehouse.NameFa, warehouse.NameEn,
            warehouse.WarehouseType, warehouse.IncludeInMrpAvailable,
            warehouse.IsActive, warehouse.Version);
    private static WarehouseResponse WarehouseResponseOf(Warehouse warehouse, ApplicationUser? responsible) =>
        new(warehouse.Id, warehouse.BusinessNumber, warehouse.NameFa, warehouse.NameEn,
            warehouse.AddressFa, warehouse.AddressEn,
            responsible is null ? null : new WarehouseResponsibleUserRef(responsible.Id,
                responsible.UserName!, responsible.FullName,
                $"/api/auth/users/{responsible.Id}/photo", responsible.IsActive),
            warehouse.WarehouseType, warehouse.IncludeInMrpAvailable,
            warehouse.IsActive, warehouse.Notes, warehouse.Version,
            warehouse.CreatedAtUtc, warehouse.UpdatedAtUtc);

    private static void ValidateWarehouse(string nameFa, string? nameEn, string? addressFa,
        string? addressEn, Guid primaryResponsibleUserId, string? warehouseType,
        string? notes, long? version)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(nameFa) || nameFa.Trim().Length > 200)
            errors["nameFa"] = ["نام فارسی الزامی و حداکثر ۲۰۰ نویسه است."];
        if (Clean(nameEn)?.Length > 200) errors["nameEn"] = ["نام انگلیسی حداکثر ۲۰۰ نویسه است."];
        if (string.IsNullOrWhiteSpace(addressFa) || addressFa.Trim().Length > 1000)
            errors["addressFa"] = ["نشانی/موقعیت فارسی الزامی و حداکثر ۱۰۰۰ نویسه است."];
        if (Clean(addressEn)?.Length > 1000)
            errors["addressEn"] = ["نشانی/موقعیت انگلیسی حداکثر ۱۰۰۰ نویسه است."];
        if (primaryResponsibleUserId == Guid.Empty)
            errors["primaryResponsibleUserId"] = ["مسئول اصلی انبار الزامی است."];
        if (string.IsNullOrWhiteSpace(warehouseType) || !WarehouseTypes.All.Contains(warehouseType))
            errors["warehouseType"] = ["نوع انبار معتبر نیست."];
        if (Clean(notes)?.Length > 2000) errors["notes"] = ["یادداشت حداکثر ۲۰۰۰ نویسه است."];
        if (version.HasValue && version < 1) errors["expectedVersion"] = ["نسخه معتبر الزامی است."];
        if (errors.Count > 0) throw new InventoryValidationException(errors);
    }

    private static string NormalizeWarehouseType(string? warehouseType) =>
        string.IsNullOrWhiteSpace(warehouseType)
            ? WarehouseTypes.Storage
            : warehouseType.Trim().ToUpperInvariant();

    private static async Task<ApplicationUser> ResponsibleUserForAssignment(HadronDbContext db,
        Guid id, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Validation("primaryResponsibleUserId", "کاربر مسئول انبار یافت نشد.");
        if (!user.IsActive)
            throw new InventoryConflictException("INACTIVE_WAREHOUSE_RESPONSIBLE_USER",
                "مسئول اصلی جدید انبار باید کاربر فعال باشد.");
        return user;
    }

    private static async Task<Dictionary<Guid, ApplicationUser>> ResponsibleUsers(HadronDbContext db,
        IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var values = ids.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        return values.Length == 0
            ? []
            : await db.Users.AsNoTracking().Where(x => values.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
    }

    private static async Task LockInventoryResources(HadronDbContext db, Guid itemId,
        IEnumerable<Guid> warehouseIds, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT inventory.lock_item_inventory({itemId})", ct);
        foreach (var warehouseId in warehouseIds.Distinct().OrderBy(x => x))
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT inventory.lock_warehouse_inventory({warehouseId})", ct);
    }

    private static Task LockWarehouseInventory(HadronDbContext db, Guid id, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT inventory.lock_warehouse_inventory({id})", ct);

    private static Task LockWarehouseRow(HadronDbContext db, Guid id, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM inventory.warehouses WHERE \"Id\" = {id} FOR UPDATE", ct);
    private static Task LockTransactionRow(HadronDbContext db, Guid id, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM inventory.transactions WHERE \"Id\" = {id} FOR UPDATE", ct);
    private static Task LockTransferRow(HadronDbContext db, Guid id, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM inventory.transfers WHERE \"Id\" = {id} FOR UPDATE", ct);

    private static async Task<string> NextWarehouseNumber(HadronDbContext db, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT nextval('inventory.warehouse_number_seq')", connection);
        var value = (long)(await command.ExecuteScalarAsync(ct))!;
        return $"WH-{value:D4}";
    }

    private static async Task<string> NextTransferNumber(HadronDbContext db, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            "SELECT nextval('inventory.inventory_transfer_number_seq')", connection);
        var value = (long)(await command.ExecuteScalarAsync(ct))!;
        return $"ITR-{value:D6}";
    }

    private async Task Emit(string entityType, Guid id, string action,
        IReadOnlyDictionary<string, object?>? oldValues,
        IReadOnlyDictionary<string, object?>? newValues, string? reason, CancellationToken ct)
    {
        try
        {
            await auditSink.WriteAsync(new(entityType, id, action, DateTime.UtcNow, Actor(),
                AuditEventContext.CorrelationId(accessor.HttpContext), oldValues, newValues,
                Clean(reason)), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "Post-commit Inventory audit emission failed for {EntityId} {Action}.",
                id, action);
        }
    }

    private static async Task Save(HadronDbContext db, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        { throw new InventoryConflictException("STALE_VERSION", "نسخه ارسالی قدیمی است."); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException postgres)
        { throw MapPostgres(postgres); }
    }

    private static Exception MapPostgres(PostgresException exception) => exception.ConstraintName switch
    {
        "ck_inventory_nonnegative" => new InventoryConflictException("NEGATIVE_STOCK_NOT_ALLOWED",
            "این گردش باعث موجودی منفی در تاریخ مؤثر یا یکی از تاریخ‌های بعدی می‌شود."),
        "ck_inventory_future_date" => new InventoryConflictException("FUTURE_INVENTORY_POSTING_NOT_ALLOWED",
            "ثبت گردش فیزیکی برای تاریخ آینده مجاز نیست."),
        "ck_inventory_base_unit" => new InventoryConflictException("BASE_UNIT_CHANGED",
            "واحد پایه قلم با snapshot گردش موجودی سازگار نیست."),
        "ck_inventory_active_assignment" => new InventoryConflictException("INACTIVE_REFERENCE",
            "قلم، واحد یا انبار برای ثبت گردش جدید باید فعال باشد."),
        "ck_inventory_reversal_rules" => new InventoryConflictException("INVALID_REVERSAL",
            "قواعد برگشت گردش رعایت نشده است."),
        "IX_transactions_ItemId_WarehouseId" => new InventoryConflictException("OPENING_ALREADY_EXISTS",
            "برای این قلم و انبار قبلاً Opening ثبت شده است."),
        "IX_transactions_OriginalTransactionId" => new InventoryConflictException("TRANSACTION_ALREADY_REVERSED",
            "این گردش قبلاً برگشت داده شده است."),
        "ck_warehouse_nonzero_stock" => new InventoryConflictException("WAREHOUSE_HAS_NONZERO_STOCK",
            "انبار دارای موجودی غیرصفر است و قابل غیرفعال‌سازی نیست."),
        "ck_item_base_unit_inventory" => new InventoryConflictException("ITEM_BASE_UNIT_LOCKED_BY_INVENTORY",
            "پس از ثبت گردش موجودی، تغییر واحد پایه قلم مجاز نیست."),
        "ck_inventory_transfer_over_receipt" => new InventoryConflictException("TRANSFER_OVER_RECEIPT",
            "مقدار دریافت از مانده انتقال بیشتر است."),
        "ck_inventory_transfer_receipt_date" => new InventoryConflictException("TRANSFER_RECEIPT_BEFORE_DISPATCH",
            "تاریخ دریافت نمی‌تواند قبل از تاریخ ارسال باشد."),
        "ck_inventory_transfer_workflow_required" => new InventoryConflictException("USE_TRANSFER_WORKFLOW",
            "انتقال داخلی باید فقط از گردش دومرحله‌ای انتقال انبار ثبت شود."),
        "ck_inventory_transfer_immutable" or "ck_inventory_transfer_receipt_immutable" =>
            new InventoryConflictException("POSTED_TRANSFER_IMMUTABLE",
                "سابقه عملیاتی انتقال پس از ثبت تغییرناپذیر است."),
        "ck_inventory_transfer_lifecycle" or "ck_inventory_transfer_receivable" or
        "ck_inventory_transfer_dispatch_link" or "ck_inventory_transfer_receipt_link" or
        "ck_inventory_transfer_reversal_link" =>
            new InventoryConflictException("INVALID_TRANSFER_LIFECYCLE",
                "وضعیت یا پیوند عملیاتی انتقال معتبر نیست."),
        _ when exception.SqlState == PostgresErrorCodes.UniqueViolation =>
            new InventoryConflictException("INVENTORY_INTEGRITY_CONFLICT", "تعارض یکتایی موجودی رخ داد."),
        _ => exception
    };

    private static int DecimalScale(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0x7F;
    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            throw Validation("page", "صفحه باید مثبت و اندازه صفحه بین ۱ تا ۱۰۰ باشد.");
    }
    private static void EnsureVersion(long actual, long expected)
    {
        if (actual != expected) throw new InventoryConflictException("STALE_VERSION",
            "نسخه ارسالی قدیمی است.", new { ActualVersion = actual, ExpectedVersion = expected });
    }
    private string? Actor() => AuditEventContext.ActorId(accessor.HttpContext);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static InventoryValidationException Validation(string key, string message) =>
        InventoryValidationException.Single(key, message);
    private static Dictionary<string, object?> Snapshot(WarehouseResponse value) => new()
    {
        ["Code"] = value.Code,
        ["NameFa"] = value.NameFa,
        ["NameEn"] = value.NameEn,
        ["AddressFa"] = value.AddressFa,
        ["AddressEn"] = value.AddressEn,
        ["PrimaryResponsibleUserId"] = value.PrimaryResponsibleUser?.Id,
        ["WarehouseType"] = value.WarehouseType,
        ["IncludeInMrpAvailable"] = value.IncludeInMrpAvailable,
        ["IsActive"] = value.IsActive,
        ["Notes"] = value.Notes,
        ["Version"] = value.Version
    };
    private static Dictionary<string, object?> Snapshot(InventoryTransferResponse value) => new()
    {
        ["Code"] = value.Code,
        ["Status"] = value.Status,
        ["ItemId"] = value.Item.Id,
        ["UnitId"] = value.Unit.Id,
        ["SourceWarehouseId"] = value.SourceWarehouse.Id,
        ["DestinationWarehouseId"] = value.DestinationWarehouse.Id,
        ["RequestedQuantity"] = value.RequestedQuantity,
        ["ReceivedQuantity"] = value.ReceivedQuantity,
        ["InTransitQuantity"] = value.InTransitQuantity,
        ["Version"] = value.Version
    };
    private static Dictionary<string, object?> Snapshot(InventoryTransactionResponse value) => new()
    {
        ["TransactionType"] = value.TransactionType,
        ["ItemId"] = value.Item.Id,
        ["WarehouseId"] = value.Warehouse.Id,
        ["DestinationWarehouseId"] = value.DestinationWarehouse?.Id,
        ["Quantity"] = value.Quantity,
        ["UnitId"] = value.Unit.Id,
        ["EnteredQuantity"] = value.EnteredQuantity,
        ["TransactionUnitId"] = value.TransactionUnit.Id,
        ["ConversionFactorToBase"] = value.ConversionFactorToBase,
        ["EffectiveDate"] = value.EffectiveDate,
        ["OriginalTransactionId"] = value.OriginalTransactionId
    };

    private static Dictionary<string, object?> ConversionSnapshot(
        InventoryItemUnitConversion value, Unit transactionUnit) => new()
        {
            ["ItemId"] = value.ItemId,
            ["TransactionUnitId"] = transactionUnit.Id,
            ["BaseUnitId"] = value.BaseUnitId,
            ["FactorToBase"] = value.FactorToBase,
            ["IsActive"] = value.IsActive,
            ["Version"] = value.Version
        };
}
