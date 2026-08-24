using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;

namespace Hadron.Erp.Api.Modules.Inventory.Domain;

public static class InventoryTransactionTypes
{
    public const string Opening = "OPENING";
    public const string Receipt = "RECEIPT";
    public const string Issue = "ISSUE";
    public const string AdjustmentIn = "ADJ_IN";
    public const string AdjustmentOut = "ADJ_OUT";
    public const string Transfer = "TRANSFER";
    public const string Reversal = "REVERSAL";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Opening, Receipt, Issue, AdjustmentIn, AdjustmentOut, Transfer, Reversal
    };
}

public static class InventorySourceDocumentTypes
{
    public const string PurchaseReceipt = "PURCHASE_RECEIPT";
    public const string ProductionIssue = "PRODUCTION_ISSUE";
    public const string LegacyImport = "LEGACY_IMPORT";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        PurchaseReceipt, ProductionIssue, LegacyImport
    };
}

public sealed class Warehouse
{
    private Warehouse() { }

    public Warehouse(string businessNumber, string nameFa, string? nameEn, string? notes)
    {
        Id = Guid.NewGuid();
        BusinessNumber = businessNumber;
        NameFa = nameFa;
        NameEn = nameEn;
        Notes = notes;
    }

    public Guid Id { get; private set; }
    public string BusinessNumber { get; private set; } = string.Empty;
    public string NameFa { get; private set; } = string.Empty;
    public string? NameEn { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string? Notes { get; private set; }
    public long Version { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public void Edit(string nameFa, string? nameEn, string? notes)
    {
        NameFa = nameFa;
        NameEn = nameEn;
        Notes = notes;
        Advance();
    }

    public void ChangeState(bool active)
    {
        IsActive = active;
        Advance();
    }

    private void Advance()
    {
        Version++;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}

public sealed class InventoryTransaction
{
    private InventoryTransaction() { }

    public InventoryTransaction(string transactionType, Guid itemId, Guid warehouseId,
        Guid? destinationWarehouseId, decimal quantity, Guid unitId, DateOnly effectiveDate,
        string? reason, Guid? originalTransactionId, string? sourceDocumentType,
        Guid? sourceDocumentId, string? actorIdentifier)
    {
        Id = Guid.NewGuid();
        TransactionType = transactionType;
        ItemId = itemId;
        WarehouseId = warehouseId;
        DestinationWarehouseId = destinationWarehouseId;
        Quantity = quantity;
        UnitId = unitId;
        EffectiveDate = effectiveDate;
        Reason = reason;
        OriginalTransactionId = originalTransactionId;
        SourceDocumentType = sourceDocumentType;
        SourceDocumentId = sourceDocumentId;
        ActorIdentifier = actorIdentifier;
    }

    public Guid Id { get; private set; }
    public string TransactionType { get; private set; } = string.Empty;
    public Guid ItemId { get; private set; }
    public Item Item { get; private set; } = null!;
    public Guid WarehouseId { get; private set; }
    public Warehouse Warehouse { get; private set; } = null!;
    public Guid? DestinationWarehouseId { get; private set; }
    public Warehouse? DestinationWarehouse { get; private set; }
    public decimal Quantity { get; private set; }
    public Guid UnitId { get; private set; }
    public Unit Unit { get; private set; } = null!;
    public DateOnly EffectiveDate { get; private set; }
    public string? Reason { get; private set; }
    public Guid? OriginalTransactionId { get; private set; }
    public InventoryTransaction? OriginalTransaction { get; private set; }
    public string? SourceDocumentType { get; private set; }
    public Guid? SourceDocumentId { get; private set; }
    public string? ActorIdentifier { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}

public sealed class InventoryLedgerEffect
{
    public Guid TransactionId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid UnitId { get; private set; }
    public DateOnly EffectiveDate { get; private set; }
    public decimal QuantityEffect { get; private set; }
}
