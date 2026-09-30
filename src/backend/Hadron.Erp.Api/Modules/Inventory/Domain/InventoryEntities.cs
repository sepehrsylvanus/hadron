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
    public const string ProductionReceipt = "PRODUCTION_RECEIPT";
    public const string InternalTransferDispatch = "INTERNAL_TRANSFER_DISPATCH";
    public const string InternalTransferReceipt = "INTERNAL_TRANSFER_RECEIPT";
    public const string LegacyImport = "LEGACY_IMPORT";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        PurchaseReceipt, ProductionIssue, ProductionReceipt, InternalTransferDispatch, InternalTransferReceipt,
        LegacyImport
    };
}

public static class InventoryTransferStatuses
{
    public const string Draft = "DRAFT";
    public const string Dispatched = "DISPATCHED";
    public const string PartiallyReceived = "PARTIALLY_RECEIVED";
    public const string Received = "RECEIVED";
    public const string Cancelled = "CANCELLED";
}

public static class WarehouseTypes
{
    public const string Storage = "STORAGE";
    public const string ProductionStaging = "PRODUCTION_STAGING";
    public const string FinishedGoods = "FINISHED_GOODS";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Storage, ProductionStaging, FinishedGoods
    };
}

public sealed class Warehouse
{
    private Warehouse() { }

    public Warehouse(string businessNumber, string nameFa, string? nameEn, string? notes)
        : this(businessNumber, nameFa, nameEn, null, null, null, notes) { }

    public Warehouse(string businessNumber, string nameFa, string? nameEn, string? addressFa,
        string? addressEn, Guid? primaryResponsibleUserId, string? notes)
        : this(businessNumber, nameFa, nameEn, addressFa, addressEn, primaryResponsibleUserId,
            WarehouseTypes.Storage, notes)
    { }

    public Warehouse(string businessNumber, string nameFa, string? nameEn, string? addressFa,
        string? addressEn, Guid? primaryResponsibleUserId, string warehouseType, string? notes)
    {
        Id = Guid.NewGuid();
        BusinessNumber = businessNumber;
        NameFa = nameFa;
        NameEn = nameEn;
        AddressFa = addressFa;
        AddressEn = addressEn;
        PrimaryResponsibleUserId = primaryResponsibleUserId;
        WarehouseType = warehouseType;
        IncludeInMrpAvailable = warehouseType != WarehouseTypes.ProductionStaging;
        Notes = notes;
    }

    public Guid Id { get; private set; }
    public string BusinessNumber { get; private set; } = string.Empty;
    public string NameFa { get; private set; } = string.Empty;
    public string? NameEn { get; private set; }
    public string? AddressFa { get; private set; }
    public string? AddressEn { get; private set; }
    public Guid? PrimaryResponsibleUserId { get; private set; }
    public string WarehouseType { get; private set; } = WarehouseTypes.Storage;
    public bool IncludeInMrpAvailable { get; private set; } = true;
    public bool IsActive { get; private set; } = true;
    public string? Notes { get; private set; }
    public long Version { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public void Edit(string nameFa, string? nameEn, string? addressFa, string? addressEn,
        Guid? primaryResponsibleUserId, string? notes)
        => Edit(nameFa, nameEn, addressFa, addressEn, primaryResponsibleUserId,
            WarehouseType, notes);

    public void Edit(string nameFa, string? nameEn, string? addressFa, string? addressEn,
        Guid? primaryResponsibleUserId, string warehouseType, string? notes)
    {
        NameFa = nameFa;
        NameEn = nameEn;
        AddressFa = addressFa;
        AddressEn = addressEn;
        PrimaryResponsibleUserId = primaryResponsibleUserId;
        WarehouseType = warehouseType;
        IncludeInMrpAvailable = warehouseType != WarehouseTypes.ProductionStaging;
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

public sealed class InventoryItemUnitConversion
{
    private InventoryItemUnitConversion() { }

    public InventoryItemUnitConversion(Guid itemId, Guid transactionUnitId, Guid baseUnitId,
        decimal factorToBase)
    {
        Id = Guid.NewGuid();
        ItemId = itemId;
        TransactionUnitId = transactionUnitId;
        BaseUnitId = baseUnitId;
        FactorToBase = factorToBase;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid ItemId { get; private set; }
    public Item Item { get; private set; } = null!;
    public Guid TransactionUnitId { get; private set; }
    public Unit TransactionUnit { get; private set; } = null!;
    public Guid BaseUnitId { get; private set; }
    public Unit BaseUnit { get; private set; } = null!;
    public decimal FactorToBase { get; private set; }
    public bool IsActive { get; private set; } = true;
    public long Version { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public void Edit(decimal factorToBase)
    {
        FactorToBase = factorToBase;
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

public sealed class InventoryTransfer
{
    private InventoryTransfer() { }

    public InventoryTransfer(string businessNumber, Guid itemId, Guid unitId,
        Guid sourceWarehouseId, Guid destinationWarehouseId, decimal requestedQuantity,
        string? reason, string? actorIdentifier)
        : this(businessNumber, itemId, unitId, sourceWarehouseId, destinationWarehouseId,
            requestedQuantity, requestedQuantity, unitId, 1m, reason, actorIdentifier)
    { }

    public InventoryTransfer(string businessNumber, Guid itemId, Guid unitId,
        Guid sourceWarehouseId, Guid destinationWarehouseId, decimal requestedQuantity,
        decimal enteredRequestedQuantity, Guid transactionUnitId, decimal conversionFactorToBase,
        string? reason, string? actorIdentifier)
    {
        Id = Guid.NewGuid();
        BusinessNumber = businessNumber;
        ItemId = itemId;
        UnitId = unitId;
        SourceWarehouseId = sourceWarehouseId;
        DestinationWarehouseId = destinationWarehouseId;
        RequestedQuantity = requestedQuantity;
        EnteredRequestedQuantity = enteredRequestedQuantity;
        TransactionUnitId = transactionUnitId;
        ConversionFactorToBase = conversionFactorToBase;
        Reason = reason;
        ActorIdentifier = actorIdentifier;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }
    public string BusinessNumber { get; private set; } = string.Empty;
    public string Status { get; private set; } = InventoryTransferStatuses.Draft;
    public Guid ItemId { get; private set; }
    public Item Item { get; private set; } = null!;
    public Guid UnitId { get; private set; }
    public Unit Unit { get; private set; } = null!;
    public Guid SourceWarehouseId { get; private set; }
    public Warehouse SourceWarehouse { get; private set; } = null!;
    public Guid DestinationWarehouseId { get; private set; }
    public Warehouse DestinationWarehouse { get; private set; } = null!;
    public decimal RequestedQuantity { get; private set; }
    public decimal EnteredRequestedQuantity { get; private set; }
    public Guid TransactionUnitId { get; private set; }
    public Unit TransactionUnit { get; private set; } = null!;
    public decimal ConversionFactorToBase { get; private set; } = 1m;
    public Guid? DispatchTransactionId { get; private set; }
    public InventoryTransaction? DispatchTransaction { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }
    public string? Reason { get; private set; }
    public string? ActorIdentifier { get; private set; }
    public long Version { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public void Edit(Guid itemId, Guid unitId, Guid sourceWarehouseId,
        Guid destinationWarehouseId, decimal requestedQuantity, string? reason)
        => Edit(itemId, unitId, sourceWarehouseId, destinationWarehouseId, requestedQuantity,
            requestedQuantity, unitId, 1m, reason);

    public void Edit(Guid itemId, Guid unitId, Guid sourceWarehouseId,
        Guid destinationWarehouseId, decimal requestedQuantity, decimal enteredRequestedQuantity,
        Guid transactionUnitId, decimal conversionFactorToBase, string? reason)
    {
        ItemId = itemId;
        UnitId = unitId;
        SourceWarehouseId = sourceWarehouseId;
        DestinationWarehouseId = destinationWarehouseId;
        RequestedQuantity = requestedQuantity;
        EnteredRequestedQuantity = enteredRequestedQuantity;
        TransactionUnitId = transactionUnitId;
        ConversionFactorToBase = conversionFactorToBase;
        Reason = reason;
        Advance();
    }

    public void Dispatch(Guid transactionId)
    {
        DispatchTransactionId = transactionId;
        Status = InventoryTransferStatuses.Dispatched;
        Advance();
    }

    public void SetEffectiveReceivedQuantity(decimal effectiveReceivedQuantity)
    {
        Status = effectiveReceivedQuantity <= 0
            ? InventoryTransferStatuses.Dispatched
            : effectiveReceivedQuantity >= RequestedQuantity
                ? InventoryTransferStatuses.Received
                : InventoryTransferStatuses.PartiallyReceived;
        Advance();
    }

    public void Cancel(string reason)
    {
        CancelledAtUtc = DateTime.UtcNow;
        CancellationReason = reason;
        Status = InventoryTransferStatuses.Cancelled;
        Advance();
    }

    private void Advance()
    {
        Version++;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}

public sealed class InventoryTransferReceipt
{
    private InventoryTransferReceipt() { }

    public InventoryTransferReceipt(Guid transferId, decimal quantity, DateOnly effectiveDate,
        string? reason, string? actorIdentifier)
        : this(transferId, quantity, quantity, Guid.Empty, 1m, effectiveDate, reason,
            actorIdentifier)
    { }

    public InventoryTransferReceipt(Guid transferId, decimal quantity,
        decimal enteredQuantity, Guid transactionUnitId, decimal conversionFactorToBase,
        DateOnly effectiveDate, string? reason, string? actorIdentifier)
    {
        Id = Guid.NewGuid();
        TransferId = transferId;
        Quantity = quantity;
        EnteredQuantity = enteredQuantity;
        TransactionUnitId = transactionUnitId;
        ConversionFactorToBase = conversionFactorToBase;
        EffectiveDate = effectiveDate;
        Reason = reason;
        ActorIdentifier = actorIdentifier;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid TransferId { get; private set; }
    public InventoryTransfer Transfer { get; private set; } = null!;
    public decimal Quantity { get; private set; }
    public decimal EnteredQuantity { get; private set; }
    public Guid TransactionUnitId { get; private set; }
    public Unit TransactionUnit { get; private set; } = null!;
    public decimal ConversionFactorToBase { get; private set; } = 1m;
    public DateOnly EffectiveDate { get; private set; }
    public string? Reason { get; private set; }
    public string? ActorIdentifier { get; private set; }
    public Guid? InventoryTransactionId { get; private set; }
    public InventoryTransaction? InventoryTransaction { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public void LinkTransaction(Guid transactionId) => InventoryTransactionId = transactionId;
}

public sealed class InventoryTransaction
{
    private InventoryTransaction() { }

    public InventoryTransaction(string transactionType, Guid itemId, Guid warehouseId,
        Guid? destinationWarehouseId, decimal quantity, Guid unitId, DateOnly effectiveDate,
        string? reason, Guid? originalTransactionId, string? sourceDocumentType,
        Guid? sourceDocumentId, string? actorIdentifier)
        : this(transactionType, itemId, warehouseId, destinationWarehouseId, quantity, unitId,
            quantity, unitId, 1m, effectiveDate, reason, originalTransactionId,
            sourceDocumentType, sourceDocumentId, actorIdentifier)
    { }

    public InventoryTransaction(string transactionType, Guid itemId, Guid warehouseId,
        Guid? destinationWarehouseId, decimal quantity, Guid unitId, decimal enteredQuantity,
        Guid transactionUnitId, decimal conversionFactorToBase, DateOnly effectiveDate,
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
        EnteredQuantity = enteredQuantity;
        TransactionUnitId = transactionUnitId;
        ConversionFactorToBase = conversionFactorToBase;
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
    public decimal EnteredQuantity { get; private set; }
    public Guid TransactionUnitId { get; private set; }
    public Unit TransactionUnit { get; private set; } = null!;
    public decimal ConversionFactorToBase { get; private set; } = 1m;
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
