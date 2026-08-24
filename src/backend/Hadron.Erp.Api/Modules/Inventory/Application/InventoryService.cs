using System.Data;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Catalog.Application;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Inventory.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Hadron.Erp.Api.Platform.Audit;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hadron.Erp.Api.Modules.Inventory.Application;

public sealed class InventoryHistoryItemGuard(IDbContextFactory<HadronDbContext> factory)
    : IItemBaseUnitChangeGuard
{
    public async Task EnsureCanChange(Guid itemId, Guid currentUnitId, Guid requestedUnitId,
        CancellationToken cancellationToken)
    {
        if (currentUnitId == requestedUnitId) return;
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        if (await db.InventoryTransactions.AsNoTracking().AnyAsync(x => x.ItemId == itemId, cancellationToken))
            throw new ItemBaseUnitChangeBlockedException();
    }
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
        int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Warehouses.AsNoTracking().Where(x => includeInactive || x.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.BusinessNumber.Contains(term) || x.NameFa.Contains(term) ||
                (x.NameEn != null && x.NameEn.Contains(term)));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.BusinessNumber).Skip((page - 1) * pageSize)
            .Take(pageSize).ToListAsync(ct);
        return new(rows.Select(WarehouseResponseOf).ToList(), page, pageSize, total);
    }

    public async Task<WarehouseResponse?> GetWarehouse(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var warehouse = await db.Warehouses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return warehouse is null ? null : WarehouseResponseOf(warehouse);
    }

    public async Task<WarehouseResponse> CreateWarehouse(CreateWarehouseRequest request, CancellationToken ct)
    {
        ValidateWarehouse(request.NameFa, request.NameEn, request.Notes, null);
        await using var db = await factory.CreateDbContextAsync(ct);
        var warehouse = new Warehouse(await NextWarehouseNumber(db, ct), request.NameFa.Trim(),
            Clean(request.NameEn), Clean(request.Notes));
        db.Warehouses.Add(warehouse);
        await Save(db, ct);
        var response = WarehouseResponseOf(warehouse);
        await Emit("Warehouse", warehouse.Id, "CREATE", null, Snapshot(response), null, ct);
        return response;
    }

    public async Task<WarehouseResponse> UpdateWarehouse(Guid id, UpdateWarehouseRequest request,
        CancellationToken ct)
    {
        ValidateWarehouse(request.NameFa, request.NameEn, request.Notes, request.ExpectedVersion);
        await using var db = await factory.CreateDbContextAsync(ct);
        var warehouse = await db.Warehouses.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException();
        EnsureVersion(warehouse.Version, request.ExpectedVersion);
        var before = Snapshot(WarehouseResponseOf(warehouse));
        warehouse.Edit(request.NameFa.Trim(), Clean(request.NameEn), Clean(request.Notes));
        await Save(db, ct);
        var response = WarehouseResponseOf(warehouse);
        await Emit("Warehouse", warehouse.Id, "EDIT", before, Snapshot(response), null, ct);
        return response;
    }

    public async Task<WarehouseResponse> ChangeWarehouseState(Guid id, bool active,
        ChangeWarehouseStateRequest request, CancellationToken ct)
    {
        if (request.ExpectedVersion < 1) throw Validation("expectedVersion", "نسخه معتبر الزامی است.");
        await using var db = await factory.CreateDbContextAsync(ct);
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
        var before = Snapshot(WarehouseResponseOf(warehouse));
        warehouse.ChangeState(active);
        await Save(db, ct);
        await tx.CommitAsync(ct);
        var response = WarehouseResponseOf(warehouse);
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

    public async Task<InventoryBalanceListResponse> ListBalances(DateOnly? asOfDate, string? search,
        Guid? itemId, Guid? warehouseId, string? itemType, int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        var date = asOfDate ?? TehranToday;
        await using var db = await factory.CreateDbContextAsync(ct);
        var effects = db.InventoryLedgerEffects.AsNoTracking().Where(x => x.EffectiveDate <= date);
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
        var item = await db.Items.AsNoTracking().Include(x => x.BaseUnit)
            .SingleOrDefaultAsync(x => x.Id == itemId, ct);
        if (item is null) return null;
        var grouped = await db.InventoryLedgerEffects.AsNoTracking()
            .Where(x => x.ItemId == itemId && x.EffectiveDate <= date)
            .GroupBy(x => x.WarehouseId)
            .Select(x => new { WarehouseId = x.Key, Balance = x.Sum(v => v.QuantityEffect) })
            .ToListAsync(ct);
        var ids = grouped.Select(x => x.WarehouseId).ToArray();
        var warehouses = await db.Warehouses.AsNoTracking().Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var rows = grouped.OrderBy(x => warehouses[x.WarehouseId].BusinessNumber)
            .Select(x => new ItemWarehouseBalance(WarehouseRefOf(warehouses[x.WarehouseId]), x.Balance,
                !warehouses[x.WarehouseId].IsActive && x.Balance != 0)).ToList();
        return new(date, ItemRef(item), rows.Sum(x => x.ActualOnHand),
            rows.Any(x => x.InactiveWarehouseStockWarning), rows);
    }

    public async Task<InventoryTransactionListResponse> ListTransactions(Guid? itemId,
        Guid? warehouseId, string? transactionType, DateOnly? from, DateOnly? to,
        int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        if (transactionType is not null && !InventoryTransactionTypes.All.Contains(transactionType))
            throw Validation("transactionType", "نوع گردش موجودی معتبر نیست.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = TransactionQuery(db);
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
            request.EffectiveDate, request.Reason), ct);
    }

    public async Task<InventoryTransactionResponse> PostTransfer(PostTransferRequest request,
        CancellationToken ct)
    {
        ValidatePosting(request.Quantity, request.EffectiveDate);
        if (request.SourceWarehouseId == request.DestinationWarehouseId)
            throw Validation("destinationWarehouseId", "انبار مبدأ و مقصد باید متفاوت باشند.");
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockInventoryResources(db, request.ItemId,
            [request.SourceWarehouseId, request.DestinationWarehouseId], ct);
        var item = await ItemForPosting(db, request.ItemId, ct);
        await WarehouseForPosting(db, request.SourceWarehouseId, ct);
        await WarehouseForPosting(db, request.DestinationWarehouseId, ct);
        var transaction = new InventoryTransaction(InventoryTransactionTypes.Transfer, item.Id,
            request.SourceWarehouseId, request.DestinationWarehouseId, request.Quantity,
            item.BaseUnitId, request.EffectiveDate, Clean(request.Reason), null, null, null, Actor());
        db.InventoryTransactions.Add(transaction);
        await Save(db, ct);
        await tx.CommitAsync(ct);
        var response = (await GetTransaction(transaction.Id, ct))!;
        await Emit("InventoryTransaction", transaction.Id, "POST_TRANSFER", null,
            Snapshot(response), request.Reason, ct);
        return response;
    }

    public async Task<InventoryTransactionResponse> Reverse(Guid originalId,
        ReverseInventoryTransactionRequest request, CancellationToken ct)
    {
        var reason = Clean(request.Reason) ?? throw Validation("reason", "دلیل برگشت الزامی است.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var identity = await db.InventoryTransactions.AsNoTracking().Where(x => x.Id == originalId)
            .Select(x => new { x.ItemId, x.WarehouseId, x.DestinationWarehouseId })
            .SingleOrDefaultAsync(ct) ?? throw new KeyNotFoundException();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockInventoryResources(db, identity.ItemId,
            identity.DestinationWarehouseId.HasValue
                ? [identity.WarehouseId, identity.DestinationWarehouseId.Value]
                : [identity.WarehouseId], ct);
        await LockTransactionRow(db, originalId, ct);
        var original = await db.InventoryTransactions.SingleAsync(x => x.Id == originalId, ct);
        if (original.TransactionType == InventoryTransactionTypes.Reversal)
            throw new InventoryConflictException("REVERSAL_CANNOT_BE_REVERSED",
                "گردش REVERSAL قابل برگشت نیست.");
        if (await db.InventoryTransactions.AnyAsync(x => x.OriginalTransactionId == originalId, ct))
            throw new InventoryConflictException("TRANSACTION_ALREADY_REVERSED",
                "این گردش قبلاً برگشت داده شده است.");
        var reversal = new InventoryTransaction(InventoryTransactionTypes.Reversal, original.ItemId,
            original.WarehouseId, original.DestinationWarehouseId, original.Quantity, original.UnitId,
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
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockInventoryResources(db, request.ItemId, [request.WarehouseId], ct);
        var item = await ItemForPosting(db, request.ItemId, ct);
        await WarehouseForPosting(db, request.WarehouseId, ct);
        if (type == InventoryTransactionTypes.Opening && await db.InventoryTransactions.AnyAsync(x =>
            x.TransactionType == InventoryTransactionTypes.Opening && x.ItemId == request.ItemId &&
            x.WarehouseId == request.WarehouseId, ct))
            throw new InventoryConflictException("OPENING_ALREADY_EXISTS",
                "برای این قلم و انبار قبلاً Opening ثبت شده است.");
        var transaction = new InventoryTransaction(type, item.Id, request.WarehouseId, null,
            request.Quantity, item.BaseUnitId, request.EffectiveDate, Clean(request.Reason), null,
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

    private static async Task<Warehouse> WarehouseForPosting(HadronDbContext db, Guid id, CancellationToken ct)
    {
        var warehouse = await db.Warehouses.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException();
        if (!warehouse.IsActive) throw new InventoryConflictException("INACTIVE_WAREHOUSE",
            "برای انبار غیرفعال گردش عادی ثبت نمی‌شود.");
        return warehouse;
    }

    private static IQueryable<InventoryTransaction> TransactionQuery(HadronDbContext db) =>
        db.InventoryTransactions.AsNoTracking().Include(x => x.Item).ThenInclude(x => x.BaseUnit)
            .Include(x => x.Unit).Include(x => x.Warehouse).Include(x => x.DestinationWarehouse);

    private static InventoryTransactionResponse TransactionResponse(InventoryTransaction row) =>
        new(row.Id, row.TransactionType, ItemRef(row.Item), WarehouseRefOf(row.Warehouse),
            row.DestinationWarehouse is null ? null : WarehouseRefOf(row.DestinationWarehouse),
            row.Quantity, UnitRef(row.Unit), row.EffectiveDate, row.Reason, row.OriginalTransactionId,
            row.SourceDocumentType, row.SourceDocumentId, row.ActorIdentifier, row.CreatedAtUtc);

    private static InventoryItemRef ItemRef(Item item) => ItemRef(item, item.BaseUnit);
    private static InventoryItemRef ItemRef(Item item, Unit unit) =>
        new(item.Id, item.BusinessNumber, item.ItemType, item.NameFa, item.NameEn, item.IsActive, UnitRef(unit));
    private static InventoryUnitRef UnitRef(Unit unit) =>
        new(unit.Id, unit.Code, unit.NameFa, unit.NameEn, unit.Symbol, unit.IsActive);
    private static WarehouseRef WarehouseRefOf(Warehouse warehouse) =>
        new(warehouse.Id, warehouse.BusinessNumber, warehouse.NameFa, warehouse.NameEn,
            warehouse.IsActive, warehouse.Version);
    private static WarehouseResponse WarehouseResponseOf(Warehouse warehouse) =>
        new(warehouse.Id, warehouse.BusinessNumber, warehouse.NameFa, warehouse.NameEn,
            warehouse.IsActive, warehouse.Notes, warehouse.Version,
            warehouse.CreatedAtUtc, warehouse.UpdatedAtUtc);

    private static void ValidateWarehouse(string nameFa, string? nameEn, string? notes, long? version)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(nameFa) || nameFa.Trim().Length > 200)
            errors["nameFa"] = ["نام فارسی الزامی و حداکثر ۲۰۰ نویسه است."];
        if (Clean(nameEn)?.Length > 200) errors["nameEn"] = ["نام انگلیسی حداکثر ۲۰۰ نویسه است."];
        if (Clean(notes)?.Length > 2000) errors["notes"] = ["یادداشت حداکثر ۲۰۰۰ نویسه است."];
        if (version.HasValue && version < 1) errors["expectedVersion"] = ["نسخه معتبر الزامی است."];
        if (errors.Count > 0) throw new InventoryValidationException(errors);
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

    private static async Task<string> NextWarehouseNumber(HadronDbContext db, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT nextval('inventory.warehouse_number_seq')", connection);
        var value = (long)(await command.ExecuteScalarAsync(ct))!;
        return $"WH-{value:D4}";
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
        ["IsActive"] = value.IsActive,
        ["Notes"] = value.Notes,
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
        ["EffectiveDate"] = value.EffectiveDate,
        ["OriginalTransactionId"] = value.OriginalTransactionId
    };
}
