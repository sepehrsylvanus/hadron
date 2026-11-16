using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Inventory.Domain;
using Microsoft.EntityFrameworkCore;

namespace Hadron.Erp.Api.Modules.Inventory.Application;

public sealed record PurchaseReceiptInventoryInput(
    Guid ReceiptLineId,
    Guid ItemId,
    Guid UnitId,
    decimal Quantity);

public sealed class PurchaseReceiptInventoryCoordinator
{
    public static async Task LockAggregate(HadronDbContext db, IEnumerable<Guid> itemIds,
        Guid warehouseId, CancellationToken ct)
    {
        foreach (var itemId in itemIds.Distinct().OrderBy(x => x))
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT inventory.lock_item_inventory({itemId})", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT inventory.lock_warehouse_inventory({warehouseId})", ct);
    }

    public async Task<IReadOnlyDictionary<Guid, InventoryTransaction>> PostBatch(
        HadronDbContext db,
        IReadOnlyList<PurchaseReceiptInventoryInput> inputs,
        Guid warehouseId,
        DateOnly effectiveDate,
        string? actorIdentifier,
        CancellationToken ct)
    {
        await LockAggregate(db, inputs.Select(x => x.ItemId), warehouseId, ct);
        var itemIds = inputs.Select(x => x.ItemId).Distinct().ToList();
        var items = await db.Items.Include(x => x.BaseUnit)
            .Where(x => itemIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var warehouse = await db.Warehouses.SingleOrDefaultAsync(x => x.Id == warehouseId, ct)
            ?? throw new KeyNotFoundException();
        if (!warehouse.IsActive)
            throw new InventoryConflictException("INACTIVE_WAREHOUSE", "انبار مقصد غیرفعال است.");

        var result = new Dictionary<Guid, InventoryTransaction>(inputs.Count);
        foreach (var input in inputs)
        {
            if (!items.TryGetValue(input.ItemId, out var item)) throw new KeyNotFoundException();
            if (item.BaseUnitId != input.UnitId)
                throw new InventoryConflictException("BASE_UNIT_CHANGED", "واحد پایه قلم تغییر کرده است.");
            if (!item.BaseUnit.IsActive)
                throw new InventoryConflictException("INACTIVE_UNIT", "واحد پایه برای ثبت گردش باید فعال باشد.");
            var transaction = new InventoryTransaction(InventoryTransactionTypes.Receipt, input.ItemId,
                warehouseId, null, input.Quantity, input.UnitId, effectiveDate, null, null,
                InventorySourceDocumentTypes.PurchaseReceipt, input.ReceiptLineId, actorIdentifier);
            result.Add(input.ReceiptLineId, transaction);
        }
        db.InventoryTransactions.AddRange(result.Values);
        return result;
    }

    public async Task<IReadOnlyDictionary<Guid, InventoryTransaction>> ReverseBatch(
        HadronDbContext db,
        IReadOnlyDictionary<Guid, Guid> receiptLineTransactions,
        string reason,
        string? actorIdentifier,
        CancellationToken ct)
    {
        var transactionIds = receiptLineTransactions.Values.Distinct().ToList();
        var originals = await db.InventoryTransactions
            .Where(x => transactionIds.Contains(x.Id)).ToListAsync(ct);
        if (originals.Count != transactionIds.Count) throw new KeyNotFoundException();

        foreach (var itemId in originals.Select(x => x.ItemId).Distinct().OrderBy(x => x))
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT inventory.lock_item_inventory({itemId})", ct);
        foreach (var warehouseId in originals.Select(x => x.WarehouseId).Distinct().OrderBy(x => x))
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT inventory.lock_warehouse_inventory({warehouseId})", ct);

        var alreadyReversed = await db.InventoryTransactions.AsNoTracking()
            .Where(x => x.OriginalTransactionId.HasValue && transactionIds.Contains(x.OriginalTransactionId.Value))
            .Select(x => x.OriginalTransactionId!.Value).ToListAsync(ct);
        if (alreadyReversed.Count > 0)
            throw new InventoryConflictException("TRANSACTION_ALREADY_REVERSED", "گردش موجودی قبلاً برگشت شده است.");

        var byId = originals.ToDictionary(x => x.Id);
        var result = new Dictionary<Guid, InventoryTransaction>(receiptLineTransactions.Count);
        foreach (var pair in receiptLineTransactions)
        {
            var original = byId[pair.Value];
            if (original.TransactionType != InventoryTransactionTypes.Receipt ||
                original.SourceDocumentType != InventorySourceDocumentTypes.PurchaseReceipt ||
                original.SourceDocumentId != pair.Key)
                throw new InventoryConflictException("INVALID_PURCHASE_RECEIPT_INVENTORY_LINK",
                    "گردش به ردیف رسید خرید تعلق ندارد.");
            result.Add(pair.Key, new InventoryTransaction(InventoryTransactionTypes.Reversal, original.ItemId,
                original.WarehouseId, null, original.Quantity, original.UnitId, original.EffectiveDate,
                reason, original.Id, null, null, actorIdentifier));
        }
        db.InventoryTransactions.AddRange(result.Values);
        return result;
    }

}
