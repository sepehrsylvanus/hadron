using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Hadron.Erp.Api.Modules.Sourcing.Domain;
using Hadron.Erp.Api.Modules.Suppliers.Domain;

namespace Hadron.Erp.Api.Modules.PurchaseSupply.Domain;

public static class PurchaseOrderStatuses
{
    public const string Draft = "DRAFT";
    public const string Approved = "APPROVED";
    public const string Ordered = "ORDERED";
    public const string Shipped = "SHIPPED";
    public const string Customs = "CUSTOMS";
    public const string PartiallyReceived = "PARTIALLY_RECEIVED";
    public const string Received = "RECEIVED";
    public const string Cancelled = "CANCELLED";

    public static readonly string[] All =
    [Draft, Approved, Ordered, Shipped, Customs, PartiallyReceived, Received, Cancelled];

    public static bool IsFirm(string status) => status is Ordered or Shipped or Customs or PartiallyReceived;
}

public static class PurchasePlanTypes
{
    public const string Original = "ORIGINAL";
    public const string Revised = "REVISED";
}

public static class FirmEtaStatuses
{
    public const string OnTime = "ON_TIME";
    public const string Overdue = "OVERDUE";
    public const string Missing = "MISSING";
}

public static class PurchaseMilestoneCodes
{
    public const string SupplierReady = "SUPPLIER_READY";
    public const string HandoverToForwarder = "HANDOVER_TO_FORWARDER";
    public const string Shipped = "SHIPPED";
    public const string ArrivedDestination = "ARRIVED_DESTINATION";
    public const string CustomsCleared = "CUSTOMS_CLEARED";
    public const string WarehouseReceipt = "WAREHOUSE_RECEIPT";

    public static readonly string[] All =
    [SupplierReady, HandoverToForwarder, Shipped, ArrivedDestination, CustomsCleared, WarehouseReceipt];
}

public sealed class PurchaseOrder
{
    private PurchaseOrder() { }

    public PurchaseOrder(
        string purchaseOrderNumber,
        Guid supplierId,
        Guid currencyId,
        Guid paymentTermId,
        decimal advancePercent,
        decimal beforeShipmentPercent,
        decimal creditPercent,
        int? creditDays,
        string? creditAnchor,
        Guid? shippingModeId,
        Guid? incotermId,
        string? notes)
    {
        Id = Guid.NewGuid();
        PurchaseOrderNumber = purchaseOrderNumber;
        SupplierId = supplierId;
        CurrencyId = currencyId;
        PaymentTermId = paymentTermId;
        AdvancePercent = advancePercent;
        BeforeShipmentPercent = beforeShipmentPercent;
        CreditPercent = creditPercent;
        CreditDays = creditDays;
        CreditAnchor = creditAnchor;
        ShippingModeId = shippingModeId;
        IncotermId = incotermId;
        Notes = notes;
    }

    public Guid Id { get; private set; }
    public string PurchaseOrderNumber { get; private set; } = string.Empty;
    public Guid SupplierId { get; private set; }
    public Supplier Supplier { get; private set; } = null!;
    public Guid CurrencyId { get; private set; }
    public Currency Currency { get; private set; } = null!;
    public Guid PaymentTermId { get; private set; }
    public ReferenceValue PaymentTerm { get; private set; } = null!;
    public decimal AdvancePercent { get; private set; }
    public decimal BeforeShipmentPercent { get; private set; }
    public decimal CreditPercent { get; private set; }
    public int? CreditDays { get; private set; }
    public string? CreditAnchor { get; private set; }
    public Guid? ShippingModeId { get; private set; }
    public ReferenceValue? ShippingMode { get; private set; }
    public Guid? IncotermId { get; private set; }
    public ReferenceValue? Incoterm { get; private set; }
    public string Status { get; private set; } = PurchaseOrderStatuses.Draft;
    public string? Notes { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public DateOnly? OrderedDate { get; private set; }
    public DateTime? OrderedAtUtc { get; private set; }
    public long Version { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public ICollection<PurchaseOrderLine> Lines { get; private set; } = [];
    public ICollection<PurchaseOrderLifecycleEvent> LifecycleEvents { get; private set; } = [];

    public void EditDraftNotes(string? notes)
    {
        EnsureDraft();
        Notes = notes;
        Advance();
    }

    public void RefreshDraftSnapshot(
        Guid paymentTermId,
        decimal advancePercent,
        decimal beforeShipmentPercent,
        decimal creditPercent,
        int? creditDays,
        string? creditAnchor,
        Guid? shippingModeId,
        Guid? incotermId)
    {
        EnsureDraft();
        PaymentTermId = paymentTermId;
        AdvancePercent = advancePercent;
        BeforeShipmentPercent = beforeShipmentPercent;
        CreditPercent = creditPercent;
        CreditDays = creditDays;
        CreditAnchor = creditAnchor;
        ShippingModeId = shippingModeId;
        IncotermId = incotermId;
        Advance();
    }

    public void TouchDraft()
    {
        EnsureDraft();
        Advance();
    }

    public void Approve(DateTime timestampUtc)
    {
        if (Status != PurchaseOrderStatuses.Draft)
            throw new InvalidOperationException("PO_NOT_DRAFT");
        Status = PurchaseOrderStatuses.Approved;
        ApprovedAtUtc = timestampUtc;
        Advance(timestampUtc);
    }

    public void ReturnToDraft(DateTime timestampUtc)
    {
        if (Status != PurchaseOrderStatuses.Approved)
            throw new InvalidOperationException("PO_NOT_APPROVED");
        Status = PurchaseOrderStatuses.Draft;
        ApprovedAtUtc = null;
        Advance(timestampUtc);
    }

    public void MarkOrdered(DateOnly orderedDate, DateTime timestampUtc)
    {
        if (Status != PurchaseOrderStatuses.Approved)
            throw new InvalidOperationException("PO_NOT_APPROVED");
        Status = PurchaseOrderStatuses.Ordered;
        OrderedDate = orderedDate;
        OrderedAtUtc = timestampUtc;
        Advance(timestampUtc);
    }

    public void TouchForPlanRevision()
    {
        if (!PurchaseOrderStatuses.IsFirm(Status))
            throw new InvalidOperationException("PO_NOT_FIRM");
        Advance();
    }

    private void EnsureDraft()
    {
        if (Status != PurchaseOrderStatuses.Draft)
            throw new InvalidOperationException("PO_SNAPSHOT_LOCKED");
    }

    private void Advance(DateTime? timestampUtc = null)
    {
        Version++;
        UpdatedAtUtc = timestampUtc ?? DateTime.UtcNow;
    }
}

public sealed class PurchaseOrderLine
{
    private PurchaseOrderLine() { }

    public PurchaseOrderLine(
        Guid purchaseOrderId,
        int lineNumber,
        Guid itemId,
        Guid sourcingOptionId,
        Guid sourcingPriceId,
        Guid itemBaseUnitId,
        decimal orderedQuantity,
        decimal unitPrice,
        decimal? minimumOrderQuantity,
        int? supplierLeadTimeDays,
        int? forwarderToShipmentDays,
        int? shippingPeriodDays,
        int? destinationCustomsDays,
        string? notes)
    {
        Id = Guid.NewGuid();
        PurchaseOrderId = purchaseOrderId;
        LineNumber = lineNumber;
        ItemId = itemId;
        SourcingOptionId = sourcingOptionId;
        SourcingPriceId = sourcingPriceId;
        ItemBaseUnitId = itemBaseUnitId;
        OrderedQuantity = orderedQuantity;
        UnitPrice = unitPrice;
        MinimumOrderQuantity = minimumOrderQuantity;
        SupplierLeadTimeDays = supplierLeadTimeDays;
        ForwarderToShipmentDays = forwarderToShipmentDays;
        ShippingPeriodDays = shippingPeriodDays;
        DestinationCustomsDays = destinationCustomsDays;
        Notes = notes;
    }

    public Guid Id { get; private set; }
    public Guid PurchaseOrderId { get; private set; }
    public PurchaseOrder PurchaseOrder { get; private set; } = null!;
    public int LineNumber { get; private set; }
    public Guid ItemId { get; private set; }
    public Item Item { get; private set; } = null!;
    public Guid SourcingOptionId { get; private set; }
    public SourcingOption SourcingOption { get; private set; } = null!;
    public Guid SourcingPriceId { get; private set; }
    public SourcingPrice SourcingPrice { get; private set; } = null!;
    public Guid ItemBaseUnitId { get; private set; }
    public Unit ItemBaseUnit { get; private set; } = null!;
    public decimal OrderedQuantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal? MinimumOrderQuantity { get; private set; }
    public int? SupplierLeadTimeDays { get; private set; }
    public int? ForwarderToShipmentDays { get; private set; }
    public int? ShippingPeriodDays { get; private set; }
    public int? DestinationCustomsDays { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public ICollection<DeliverySchedule> Schedules { get; private set; } = [];

    public decimal LineTotal => OrderedQuantity * UnitPrice;

    public void ReplaceDraftSnapshot(
        Guid itemId,
        Guid sourcingOptionId,
        Guid sourcingPriceId,
        Guid itemBaseUnitId,
        decimal orderedQuantity,
        decimal unitPrice,
        decimal? minimumOrderQuantity,
        int? supplierLeadTimeDays,
        int? forwarderToShipmentDays,
        int? shippingPeriodDays,
        int? destinationCustomsDays,
        string? notes)
    {
        ItemId = itemId;
        SourcingOptionId = sourcingOptionId;
        SourcingPriceId = sourcingPriceId;
        ItemBaseUnitId = itemBaseUnitId;
        OrderedQuantity = orderedQuantity;
        UnitPrice = unitPrice;
        MinimumOrderQuantity = minimumOrderQuantity;
        SupplierLeadTimeDays = supplierLeadTimeDays;
        ForwarderToShipmentDays = forwarderToShipmentDays;
        ShippingPeriodDays = shippingPeriodDays;
        DestinationCustomsDays = destinationCustomsDays;
        Notes = notes;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void RefreshDraftSnapshot(
        Guid itemBaseUnitId,
        decimal unitPrice,
        decimal? minimumOrderQuantity,
        int? supplierLeadTimeDays,
        int? forwarderToShipmentDays,
        int? shippingPeriodDays,
        int? destinationCustomsDays)
    {
        ItemBaseUnitId = itemBaseUnitId;
        UnitPrice = unitPrice;
        MinimumOrderQuantity = minimumOrderQuantity;
        SupplierLeadTimeDays = supplierLeadTimeDays;
        ForwarderToShipmentDays = forwarderToShipmentDays;
        ShippingPeriodDays = shippingPeriodDays;
        DestinationCustomsDays = destinationCustomsDays;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}

public sealed class DeliverySchedule
{
    private DeliverySchedule() { }

    public DeliverySchedule(Guid purchaseOrderLineId, int scheduleNumber, decimal allocatedQuantity, DateOnly? requestedReceiptDate)
    {
        Id = Guid.NewGuid();
        PurchaseOrderLineId = purchaseOrderLineId;
        ScheduleNumber = scheduleNumber;
        AllocatedQuantity = allocatedQuantity;
        RequestedReceiptDate = requestedReceiptDate;
    }

    public Guid Id { get; private set; }
    public Guid PurchaseOrderLineId { get; private set; }
    public PurchaseOrderLine PurchaseOrderLine { get; private set; } = null!;
    public int ScheduleNumber { get; private set; }
    public decimal AllocatedQuantity { get; private set; }
    public DateOnly? RequestedReceiptDate { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public ICollection<DeliverySchedulePlanRevision> PlanRevisions { get; private set; } = [];
    public ICollection<DeliveryScheduleActualMilestone> ActualMilestones { get; private set; } = [];
}

public sealed class DeliverySchedulePlanRevision
{
    private DeliverySchedulePlanRevision() { }

    public DeliverySchedulePlanRevision(
        Guid deliveryScheduleId,
        string planType,
        int revisionNumber,
        DateOnly? supplierReadyDate,
        DateOnly? handoverToForwarderDate,
        DateOnly? shippedDate,
        DateOnly? arrivedDestinationDate,
        DateOnly? customsClearedDate,
        DateOnly? warehouseReceiptDate,
        string? reason,
        string? actorIdentifier,
        DateTime createdAtUtc)
    {
        Id = Guid.NewGuid();
        DeliveryScheduleId = deliveryScheduleId;
        PlanType = planType;
        RevisionNumber = revisionNumber;
        SupplierReadyDate = supplierReadyDate;
        HandoverToForwarderDate = handoverToForwarderDate;
        ShippedDate = shippedDate;
        ArrivedDestinationDate = arrivedDestinationDate;
        CustomsClearedDate = customsClearedDate;
        WarehouseReceiptDate = warehouseReceiptDate;
        Reason = reason;
        ActorIdentifier = actorIdentifier;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid DeliveryScheduleId { get; private set; }
    public DeliverySchedule DeliverySchedule { get; private set; } = null!;
    public string PlanType { get; private set; } = string.Empty;
    public int RevisionNumber { get; private set; }
    public DateOnly? SupplierReadyDate { get; private set; }
    public DateOnly? HandoverToForwarderDate { get; private set; }
    public DateOnly? ShippedDate { get; private set; }
    public DateOnly? ArrivedDestinationDate { get; private set; }
    public DateOnly? CustomsClearedDate { get; private set; }
    public DateOnly? WarehouseReceiptDate { get; private set; }
    public string? Reason { get; private set; }
    public string? ActorIdentifier { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}

public sealed class DeliveryScheduleActualMilestone
{
    private DeliveryScheduleActualMilestone() { }

    internal DeliveryScheduleActualMilestone(Guid deliveryScheduleId, string milestoneCode, DateOnly actualDate,
        string sourceType, Guid sourceId, string? actorIdentifier, DateTime createdAtUtc)
    {
        Id = Guid.NewGuid();
        DeliveryScheduleId = deliveryScheduleId;
        MilestoneCode = milestoneCode;
        ActualDate = actualDate;
        SourceType = sourceType;
        SourceId = sourceId;
        ActorIdentifier = actorIdentifier;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid DeliveryScheduleId { get; private set; }
    public DeliverySchedule DeliverySchedule { get; private set; } = null!;
    public string MilestoneCode { get; private set; } = string.Empty;
    public DateOnly ActualDate { get; private set; }
    public string SourceType { get; private set; } = string.Empty;
    public Guid SourceId { get; private set; }
    public string? ActorIdentifier { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}

public sealed class PurchaseOrderLifecycleEvent
{
    private PurchaseOrderLifecycleEvent() { }

    public PurchaseOrderLifecycleEvent(Guid purchaseOrderId, string fromStatus, string toStatus, string? reason,
        string? actorIdentifier, DateTime createdAtUtc)
    {
        Id = Guid.NewGuid();
        PurchaseOrderId = purchaseOrderId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Reason = reason;
        ActorIdentifier = actorIdentifier;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid PurchaseOrderId { get; private set; }
    public PurchaseOrder PurchaseOrder { get; private set; } = null!;
    public string FromStatus { get; private set; } = string.Empty;
    public string ToStatus { get; private set; } = string.Empty;
    public string? Reason { get; private set; }
    public string? ActorIdentifier { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}
