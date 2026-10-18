namespace Hadron.Erp.Api.Modules.PurchaseSupply.Application;

public sealed record CreatePurchaseOrderRequest(
    Guid SupplierId,
    Guid CurrencyId,
    IReadOnlyList<PurchaseOrderLineInput> Lines,
    string? Notes);

public sealed record PurchaseOrderLineInput(
    Guid SourcingOptionId,
    decimal UnitPrice,
    decimal OrderedQuantity,
    DateOnly? RequestedReceiptDate,
    string? Notes);

public sealed record UpdatePurchaseOrderRequest(long ExpectedVersion, string? Notes);
public sealed record AddPurchaseOrderLineRequest(long ExpectedVersion, PurchaseOrderLineInput Line);
public sealed record UpdatePurchaseOrderLineRequest(long ExpectedVersion, PurchaseOrderLineInput Line);
public sealed record ReplaceSchedulesRequest(long ExpectedVersion, IReadOnlyList<DeliveryScheduleInput> Schedules,
    string? Reason = null);
public sealed record DeliveryScheduleInput(decimal AllocatedQuantity, DateOnly? RequestedReceiptDate);
public sealed record ExpectedVersionRequest(long ExpectedVersion);
public sealed record ReturnToDraftRequest(long ExpectedVersion, string Reason);
public sealed record MarkOrderedRequest(long ExpectedVersion, DateOnly OrderedDate);
public sealed record ReviseSchedulePlanRequest(
    long ExpectedVersion,
    string Reason,
    DateOnly? SupplierReadyDate,
    DateOnly? HandoverToForwarderDate,
    DateOnly? ShippedDate,
    DateOnly? ArrivedDestinationDate,
    DateOnly? CustomsClearedDate,
    DateOnly? WarehouseReceiptDate);

public sealed record PurchaseOrderRef(Guid Id, string Code, string NameFa, string? NameEn, bool IsActive);
public sealed record PurchaseOrderSupplierRef(Guid Id, string SupplierNumber, string Name, bool IsActive);
public sealed record PurchaseOrderItemRef(Guid Id, string Code, string NameFa, string? NameEn, string ItemType, bool IsActive);

public sealed record PurchaseOrderListItem(
    Guid Id,
    string PurchaseOrderNumber,
    PurchaseOrderSupplierRef Supplier,
    PurchaseOrderRef Currency,
    string Status,
    int LineCount,
    decimal Total,
    decimal OpenQuantity,
    DateOnly? NextFirmEta,
    bool HasMissingFirmEta,
    bool HasOverdueFirmEta,
    long Version,
    DateTime CreatedAtUtc);

public sealed record PurchaseOrderListResponse(IReadOnlyList<PurchaseOrderListItem> Items, int TotalCount, int Page, int PageSize);

public sealed record PurchaseOrderResponse(
    Guid Id,
    string PurchaseOrderNumber,
    PurchaseOrderSupplierRef Supplier,
    PurchaseOrderRef Currency,
    PurchaseOrderRef PaymentTerm,
    decimal AdvancePercent,
    decimal BeforeShipmentPercent,
    decimal CreditPercent,
    int? CreditDays,
    string? CreditAnchor,
    PurchaseOrderRef? ShippingMode,
    PurchaseOrderRef? Incoterm,
    string Status,
    string? Notes,
    DateTime? ApprovedAtUtc,
    DateOnly? OrderedDate,
    DateTime? OrderedAtUtc,
    long Version,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    decimal Total,
    IReadOnlyList<PurchaseOrderLineResponse> Lines,
    IReadOnlyList<PurchaseOrderLifecycleResponse> Lifecycle,
    string CompletionOutcome,
    DateOnly? FirstReceiptDate,
    DateOnly? FinalReceiptDate,
    IReadOnlyList<string> Warnings);

public sealed record PurchaseOrderLineResponse(
    Guid Id,
    int LineNumber,
    PurchaseOrderItemRef Item,
    Guid SourcingOptionId,
    string SourcingNumber,
    PurchaseOrderRef ItemBaseUnit,
    decimal OrderedQuantity,
    decimal UnitPrice,
    decimal? MinimumOrderQuantity,
    int? SupplierLeadTimeDays,
    int? ForwarderToShipmentDays,
    int? ShippingPeriodDays,
    int? DestinationCustomsDays,
    decimal LineTotal,
    decimal ReceivedQuantity,
    decimal CancelledQuantity,
    decimal OpenQuantity,
    string? Notes,
    Guid? ExpertDecisionId,
    Guid? RecommendationId,
    Guid? GenerationId,
    Guid? MrpRunId,
    IReadOnlyList<DeliveryScheduleResponse> Schedules);

public sealed record DeliveryScheduleResponse(
    Guid Id,
    int ScheduleNumber,
    decimal AllocatedQuantity,
    decimal ReceivedQuantity,
    decimal CancelledQuantity,
    decimal OpenQuantity,
    DateOnly? RequestedReceiptDate,
    DateOnly? FirmEta,
    string FirmEtaSource,
    IReadOnlyList<SchedulePlanRevisionResponse> PlanRevisions,
    IReadOnlyList<ActualMilestoneResponse> ActualMilestones);

public sealed record SchedulePlanRevisionResponse(
    Guid Id,
    string PlanType,
    int RevisionNumber,
    DateOnly? SupplierReadyDate,
    DateOnly? HandoverToForwarderDate,
    DateOnly? ShippedDate,
    DateOnly? ArrivedDestinationDate,
    DateOnly? CustomsClearedDate,
    DateOnly? WarehouseReceiptDate,
    string? Reason,
    string? ActorIdentifier,
    DateTime CreatedAtUtc);

public sealed record ActualMilestoneResponse(
    Guid Id,
    string MilestoneCode,
    DateOnly ActualDate,
    string SourceType,
    Guid SourceId,
    DateTime CreatedAtUtc,
    Guid? SupersedesMilestoneId,
    string? Reason);

public sealed record PurchaseOrderLifecycleResponse(
    Guid Id,
    string FromStatus,
    string ToStatus,
    string? Reason,
    string? ActorIdentifier,
    DateTime CreatedAtUtc);

public sealed record FirmIncomingResponse(
    Guid PurchaseOrderId,
    string PurchaseOrderNumber,
    Guid PurchaseOrderLineId,
    Guid DeliveryScheduleId,
    PurchaseOrderItemRef Item,
    decimal OpenQuantity,
    DateOnly? Eta,
    string EtaSource,
    string EtaStatus,
    string Status);

public sealed record PurchaseSupplyError(string Code, string Message, int StatusCode, IReadOnlyDictionary<string, string[]>? Errors = null);
public sealed record PurchaseSupplyResult<T>(T? Value, PurchaseSupplyError? Error)
{
    public static PurchaseSupplyResult<T> Success(T value) => new(value, null);
    public static PurchaseSupplyResult<T> Failure(string code, string message, int statusCode = 400,
        IReadOnlyDictionary<string, string[]>? errors = null) => new(default, new(code, message, statusCode, errors));
}

public sealed record ConvertExpertDecisionsRequest(IReadOnlyList<Guid> DecisionIds);
public sealed record DecisionConversionResult(Guid DecisionId, bool Success, Guid? PurchaseOrderId,
    string? PurchaseOrderNumber, string? Code, IReadOnlyList<string> DetailReasons);
public sealed record ConversionGroupResult(string GroupKey, bool Success, Guid? PurchaseOrderId,
    string? PurchaseOrderNumber, IReadOnlyList<DecisionConversionResult> Decisions);
public sealed record ConvertExpertDecisionsResponse(IReadOnlyList<ConversionGroupResult> Groups);

public sealed record CreatePurchaseReceiptRequest(Guid PurchaseOrderId, Guid WarehouseId,
    DateOnly EffectiveDate, string? Notes, IReadOnlyList<PurchaseReceiptLineInput> Lines);
public sealed record PurchaseReceiptLineInput(Guid PurchaseOrderLineId, Guid DeliveryScheduleId, decimal Quantity);
public sealed record PostPurchaseReceiptRequest(long ExpectedReceiptVersion, long ExpectedPurchaseOrderVersion);
public sealed record ReversePurchaseReceiptRequest(long ExpectedReceiptVersion, long ExpectedPurchaseOrderVersion, string Reason);
public sealed record CancelScheduleRequest(long ExpectedPurchaseOrderVersion, decimal Quantity,
    DateOnly EffectiveDate, string Reason);
public sealed record RecordActualMilestoneRequest(long ExpectedPurchaseOrderVersion, string MilestoneCode,
    DateOnly ActualDate, Guid? SupersedesMilestoneId, string? Reason);
public sealed record ReallocateSchedulesRequest(long ExpectedPurchaseOrderVersion, string Reason,
    IReadOnlyList<DeliveryScheduleInput> Schedules);

public sealed record PurchaseReceiptListResponse(IReadOnlyList<PurchaseReceiptResponse> Items, int TotalCount, int Page, int PageSize);
public sealed record PurchaseReceiptResponse(Guid Id, string ReceiptNumber, Guid PurchaseOrderId,
    string PurchaseOrderNumber, Guid WarehouseId, string WarehouseCode, string WarehouseNameFa,
    string? WarehouseNameEn, DateOnly EffectiveDate, string Status, string? Notes, long Version,
    DateTime? PostedAtUtc, DateTime? ReversedAtUtc, string? ReversalReason,
    IReadOnlyList<PurchaseReceiptLineResponse> Lines, IReadOnlyList<string> Warnings);
public sealed record PurchaseReceiptLineResponse(Guid Id, Guid PurchaseOrderLineId, Guid DeliveryScheduleId,
    PurchaseOrderItemRef Item, PurchaseOrderRef Unit, decimal Quantity, Guid? InventoryTransactionId,
    Guid? ReversalInventoryTransactionId);
