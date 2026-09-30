using Hadron.Erp.Api.Modules.Bom.Domain;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Inventory.Domain;
using Hadron.Erp.Api.Modules.Planning.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;

namespace Hadron.Erp.Api.Modules.Production.Domain;

public static class ProductionOrderStatuses
{
    public const string Draft = "DRAFT";
    public const string Approved = "APPROVED";
    public const string Released = "RELEASED";
    public const string InProgress = "IN_PROGRESS";
    public const string Completed = "COMPLETED";
    public const string Cancelled = "CANCELLED";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    { Draft, Approved, Released, InProgress, Completed, Cancelled };
}

public sealed class ProductionOrder
{
    private ProductionOrder() { }

    public ProductionOrder(string businessNumber, Guid productId, Guid productBaseUnitId,
        decimal plannedQuantity, DateOnly plannedStartDate, DateOnly plannedEndDate,
        Guid destinationWarehouseId, Guid forecastSeriesId, Guid forecastRevisionId,
        string? notes, string? actorIdentifier)
    {
        Id = Guid.NewGuid();
        BusinessNumber = businessNumber;
        ProductId = productId;
        ProductBaseUnitId = productBaseUnitId;
        PlannedQuantity = plannedQuantity;
        PlannedStartDate = plannedStartDate;
        PlannedEndDate = plannedEndDate;
        DestinationWarehouseId = destinationWarehouseId;
        ForecastSeriesId = forecastSeriesId;
        ForecastRevisionId = forecastRevisionId;
        Notes = notes;
        ActorIdentifier = actorIdentifier;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }
    public string BusinessNumber { get; private set; } = string.Empty;
    public string Status { get; private set; } = ProductionOrderStatuses.Draft;
    public Guid ProductId { get; private set; }
    public Item Product { get; private set; } = null!;
    public Guid ProductBaseUnitId { get; private set; }
    public Unit ProductBaseUnit { get; private set; } = null!;
    public decimal PlannedQuantity { get; private set; }
    public decimal ReportedGoodQuantity { get; private set; }
    public decimal ReportedRejectedQuantity { get; private set; }
    public DateOnly PlannedStartDate { get; private set; }
    public DateOnly PlannedEndDate { get; private set; }
    public Guid DestinationWarehouseId { get; private set; }
    public Warehouse DestinationWarehouse { get; private set; } = null!;
    public Guid ForecastSeriesId { get; private set; }
    public ForecastSeries ForecastSeries { get; private set; } = null!;
    public Guid ForecastRevisionId { get; private set; }
    public ForecastRevision ForecastRevision { get; private set; } = null!;
    public Guid? BomRevisionId { get; private set; }
    public BomRevision? BomRevision { get; private set; }
    public string? Notes { get; private set; }
    public string? ActorIdentifier { get; private set; }
    public long Version { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public DateTime? ReleasedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public ICollection<ProductionOrderMaterial> Materials { get; private set; } = [];
    public ICollection<ProductionReport> Reports { get; private set; } = [];

    public void EditDraft(decimal plannedQuantity, DateOnly startDate, DateOnly endDate,
        Guid destinationWarehouseId, Guid forecastSeriesId, Guid forecastRevisionId,
        string? notes)
    {
        EnsureStatus(ProductionOrderStatuses.Draft);
        PlannedQuantity = plannedQuantity;
        PlannedStartDate = startDate;
        PlannedEndDate = endDate;
        DestinationWarehouseId = destinationWarehouseId;
        ForecastSeriesId = forecastSeriesId;
        ForecastRevisionId = forecastRevisionId;
        Notes = notes;
        Advance();
    }

    public void Approve()
    {
        EnsureStatus(ProductionOrderStatuses.Draft);
        Status = ProductionOrderStatuses.Approved;
        ApprovedAtUtc = DateTime.UtcNow;
        Advance();
    }

    public void Release(Guid bomRevisionId)
    {
        EnsureStatus(ProductionOrderStatuses.Approved);
        BomRevisionId = bomRevisionId;
        Status = ProductionOrderStatuses.Released;
        ReleasedAtUtc = DateTime.UtcNow;
        Advance();
    }

    public void RegisterStaging()
    {
        if (Status is not (ProductionOrderStatuses.Released or ProductionOrderStatuses.InProgress))
            throw new InvalidOperationException();
        Advance();
    }

    public void RecordProduction(decimal goodQuantity, decimal rejectedQuantity)
    {
        if (Status is not (ProductionOrderStatuses.Released or ProductionOrderStatuses.InProgress))
            throw new InvalidOperationException("Production order is not reportable.");
        ReportedGoodQuantity += goodQuantity;
        ReportedRejectedQuantity += rejectedQuantity;
        var total = ReportedGoodQuantity + ReportedRejectedQuantity;
        if (total >= PlannedQuantity)
        {
            Status = ProductionOrderStatuses.Completed;
            CompletedAtUtc = DateTime.UtcNow;
        }
        else Status = ProductionOrderStatuses.InProgress;
        Advance();
    }

    private void EnsureStatus(string status)
    {
        if (Status != status) throw new InvalidOperationException($"Expected {status}.");
    }

    private void Advance()
    {
        Version++;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}

public sealed class ProductionOrderMaterial
{
    private ProductionOrderMaterial() { }

    public ProductionOrderMaterial(Guid productionOrderId, Guid bomLineId, Guid itemId,
        Guid unitId, decimal requiredQuantity, decimal usageQty, decimal baseOutputQty,
        int sortOrder)
    {
        Id = Guid.NewGuid();
        ProductionOrderId = productionOrderId;
        BomLineId = bomLineId;
        ItemId = itemId;
        UnitId = unitId;
        RequiredQuantity = requiredQuantity;
        UsageQty = usageQty;
        BaseOutputQty = baseOutputQty;
        SortOrder = sortOrder;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid ProductionOrderId { get; private set; }
    public ProductionOrder ProductionOrder { get; private set; } = null!;
    public Guid BomLineId { get; private set; }
    public BomLine BomLine { get; private set; } = null!;
    public Guid ItemId { get; private set; }
    public Item Item { get; private set; } = null!;
    public Guid UnitId { get; private set; }
    public Unit Unit { get; private set; } = null!;
    public decimal RequiredQuantity { get; private set; }
    public decimal UsageQty { get; private set; }
    public decimal BaseOutputQty { get; private set; }
    public int SortOrder { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public ICollection<ProductionMaterialStaging> Stagings { get; private set; } = [];
}

public sealed class ProductionMaterialStaging
{
    private ProductionMaterialStaging() { }
    public ProductionMaterialStaging(Guid productionOrderMaterialId, Guid inventoryTransferId,
        decimal baseQuantity)
    {
        Id = Guid.NewGuid();
        ProductionOrderMaterialId = productionOrderMaterialId;
        InventoryTransferId = inventoryTransferId;
        BaseQuantity = baseQuantity;
        CreatedAtUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid ProductionOrderMaterialId { get; private set; }
    public ProductionOrderMaterial ProductionOrderMaterial { get; private set; } = null!;
    public Guid InventoryTransferId { get; private set; }
    public InventoryTransfer InventoryTransfer { get; private set; } = null!;
    public decimal BaseQuantity { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}

public sealed class ProductionReport
{
    private ProductionReport() { }
    public ProductionReport(Guid id, Guid productionOrderId, DateOnly productionDate, decimal goodQuantity,
        decimal rejectedQuantity, Guid destinationWarehouseId, Guid finishedGoodsTransactionId,
        Guid dailyActualId, Guid dailyActualRevisionId, string? notes, string? actorIdentifier)
    {
        Id = id; ProductionOrderId = productionOrderId; ProductionDate = productionDate;
        GoodQuantity = goodQuantity; RejectedQuantity = rejectedQuantity;
        DestinationWarehouseId = destinationWarehouseId;
        FinishedGoodsTransactionId = finishedGoodsTransactionId; DailyActualId = dailyActualId;
        DailyActualRevisionId = dailyActualRevisionId; Notes = notes;
        ActorIdentifier = actorIdentifier; CreatedAtUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid ProductionOrderId { get; private set; }
    public ProductionOrder ProductionOrder { get; private set; } = null!;
    public DateOnly ProductionDate { get; private set; }
    public decimal GoodQuantity { get; private set; }
    public decimal RejectedQuantity { get; private set; }
    public Guid DestinationWarehouseId { get; private set; }
    public Warehouse DestinationWarehouse { get; private set; } = null!;
    public Guid FinishedGoodsTransactionId { get; private set; }
    public InventoryTransaction FinishedGoodsTransaction { get; private set; } = null!;
    public Guid DailyActualId { get; private set; }
    public DailyActual DailyActual { get; private set; } = null!;
    public Guid DailyActualRevisionId { get; private set; }
    public DailyActualRevision DailyActualRevision { get; private set; } = null!;
    public string? Notes { get; private set; }
    public string? ActorIdentifier { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public ICollection<ProductionReportMaterial> Materials { get; private set; } = [];
}

public sealed class ProductionReportMaterial
{
    private ProductionReportMaterial() { }
    public ProductionReportMaterial(Guid productionReportId, Guid productionOrderMaterialId,
        decimal standardQuantity, decimal actualQuantity, string? varianceReason,
        Guid inventoryTransactionId)
    {
        Id = Guid.NewGuid(); ProductionReportId = productionReportId;
        ProductionOrderMaterialId = productionOrderMaterialId;
        StandardQuantity = standardQuantity; ActualQuantity = actualQuantity;
        VarianceReason = varianceReason; InventoryTransactionId = inventoryTransactionId;
        CreatedAtUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid ProductionReportId { get; private set; }
    public ProductionReport ProductionReport { get; private set; } = null!;
    public Guid ProductionOrderMaterialId { get; private set; }
    public ProductionOrderMaterial ProductionOrderMaterial { get; private set; } = null!;
    public decimal StandardQuantity { get; private set; }
    public decimal ActualQuantity { get; private set; }
    public string? VarianceReason { get; private set; }
    public Guid InventoryTransactionId { get; private set; }
    public InventoryTransaction InventoryTransaction { get; private set; } = null!;
    public DateTime CreatedAtUtc { get; private set; }
}

public sealed class ProductionOrderLifecycleEvent
{
    private ProductionOrderLifecycleEvent() { }
    public ProductionOrderLifecycleEvent(Guid productionOrderId, string fromStatus,
        string toStatus, string? reason, string? actorIdentifier)
    {
        Id = Guid.NewGuid(); ProductionOrderId = productionOrderId; FromStatus = fromStatus;
        ToStatus = toStatus; Reason = reason; ActorIdentifier = actorIdentifier;
        OccurredAtUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid ProductionOrderId { get; private set; }
    public ProductionOrder ProductionOrder { get; private set; } = null!;
    public string FromStatus { get; private set; } = string.Empty;
    public string ToStatus { get; private set; } = string.Empty;
    public string? Reason { get; private set; }
    public string? ActorIdentifier { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
}
