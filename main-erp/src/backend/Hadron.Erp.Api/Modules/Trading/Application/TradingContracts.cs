using System.Text.Json.Serialization;

namespace Hadron.Erp.Api.Modules.Trading.Application;

public sealed record TradeReferenceResponse(Guid Id, string ReferenceType, string Code, string NameFa,
    string? NameEn, int SortOrder, bool IsActive, long Version);
public sealed record TradingLookupResponse(IReadOnlyList<TradingLookupItem> Currencies,
    IReadOnlyList<TradingLookupItem> Suppliers, IReadOnlyList<TradeReferenceResponse> References,
    IReadOnlyList<string> ReferenceTypes);
public sealed record TradingLookupItem(Guid Id, string Code, string NameFa, string? NameEn, bool IsActive);
public sealed record TradeReferenceListResponse(IReadOnlyList<TradeReferenceResponse> Items,
    int Page, int PageSize, int TotalCount);
public sealed record TradingOverviewResponse(int OpenProjects, int? ActiveShipments, int? CustomsInProgress,
    int CriticalProjects, IReadOnlyList<TradeProjectSummary> RecentProjects, IReadOnlyList<TradeShipmentResponse> UpcomingShipments);
public sealed record TradeProjectSummary(Guid Id, string ProjectNumber, string TitleFa, string? TitleEn,
    string? CustomerNameFa, string? CustomerNameEn, string? SupplierName, string Priority, string Status,
    DateOnly? TargetDate, long Version, DateTime UpdatedAtUtc,
    DateOnly? StartDate = null, Guid? BusinessCardId = null, string? BusinessCardCode = null,
    string? BusinessCardNameFa = null, string? BusinessCardNameEn = null,
    Guid? ResponsibleUserId = null, string? ResponsibleUserName = null);
public sealed record TradeProjectListResponse(IReadOnlyList<TradeProjectSummary> Items, int Page, int PageSize, int TotalCount);
public sealed record TradeProjectContext(Guid Id, string ProjectNumber, string TitleFa, string? TitleEn);
public sealed record TradeShipmentWorklistItem(TradeShipmentResponse Shipment,
    IReadOnlyList<TradeProjectContext> Projects);
public sealed record TradeShipmentListResponse(IReadOnlyList<TradeShipmentWorklistItem> Items,
    int Page, int PageSize, int TotalCount);
public sealed record TradeCustomsWorklistItem(TradeCustomsResponse Customs, string ShipmentNumber,
    IReadOnlyList<TradeProjectContext> Projects);
public sealed record TradeCustomsListResponse(IReadOnlyList<TradeCustomsWorklistItem> Items,
    int Page, int PageSize, int TotalCount);
public sealed record TradeCostWorklistItem(TradeCostResponse Cost, TradeProjectContext Project);
public sealed record TradeCostListResponse(IReadOnlyList<TradeCostWorklistItem> Items,
    int Page, int PageSize, int TotalCount);
public sealed record TradeCurrencyTotal(string CurrencyCode, string CurrencyNameFa, string? CurrencyNameEn,
    decimal Amount);
public sealed record TradeReportWorklistItem(TradeProjectSummary Project, int OrderRegistrationCount,
    int FxAllocationCount, int ShipmentCount, int CustomsCount, IReadOnlyList<TradeCurrencyTotal> CostTotals);
public sealed record TradeReportListResponse(IReadOnlyList<TradeReportWorklistItem> Items,
    int Page, int PageSize, int TotalCount);
public sealed record TradeProjectDetailResponse(TradeProjectSummary Project, string? Notes,
    IReadOnlyList<TradeOrderRegistrationResponse> OrderRegistrations, IReadOnlyList<TradeFxAllocationResponse> FxAllocations,
    IReadOnlyList<TradeProjectShipmentResponse> Shipments, IReadOnlyList<TradeCostResponse> Costs,
    IReadOnlyList<TradeAuditResponse> Audit, TradingProjectCapabilities Capabilities);
public sealed record TradingProjectCapabilities(bool CanViewOrders, bool CanViewFx, bool CanViewShipments,
    bool CanViewCustoms, bool CanViewCosts, bool CanViewAudit);
public sealed record TradingProjectReadAccess(bool Orders, bool Fx, bool Shipments, bool Customs, bool Costs, bool Audit)
{
    public static TradingProjectReadAccess Full { get; } = new(true, true, true, true, true, true);
}
public sealed record TradingOverviewReadAccess(bool Shipments, bool Customs)
{
    public static TradingOverviewReadAccess Full { get; } = new(true, true);
}
public sealed record TradeOrderRegistrationResponse(Guid Id, Guid ProjectId, string RegistrationNumber, string Status,
    DateOnly RegistrationDate, decimal RegisteredAmount, Guid CurrencyId, string CurrencyCode, string CurrencyNameFa,
    string? CurrencyNameEn, string? Notes, long Version);
public sealed record TradeFxAllocationResponse(Guid Id, Guid ProjectId, Guid? OrderRegistrationId, string ProcessType,
    string DealStatus, string ReceiptStatus, decimal Amount, Guid CurrencyId, string CurrencyCode, string CurrencyNameFa,
    string? CurrencyNameEn, string? BankNameFa, string? BankNameEn, DateOnly RequestDate, DateOnly? AllocationDate,
    string? ReferenceNumber, string? Notes, long Version);
public sealed record TradeShipmentResponse(Guid Id, string ShipmentNumber, string Status, string TransportMode,
    string? ForwarderNameFa, string? ForwarderNameEn, string Origin, string Destination, DateOnly? EstimatedDeparture,
    DateOnly? EstimatedArrival, DateOnly? ActualDeparture, DateOnly? ActualArrival, decimal? GrossWeight,
    decimal? VolumeCbm, string? Notes, long Version, IReadOnlyList<TradeShipmentLegResponse> Legs);
public sealed record TradeShipmentLegResponse(Guid Id, int Sequence, string FromLocation, string ToLocation,
    string TransportMode, string Status, DateOnly? EstimatedDeparture, DateOnly? EstimatedArrival,
    DateOnly? ActualDeparture, DateOnly? ActualArrival, string? TrackingNumber, string? Notes, long Version);
public sealed record TradeProjectShipmentResponse(Guid LinkId, string AllocationMethod, decimal AllocationValue,
    string? Notes, long Version, TradeShipmentResponse Shipment, IReadOnlyList<TradeCustomsResponse> Customs);
public sealed record TradeCustomsResponse(Guid Id, Guid ShipmentId, string ClearanceNumber, string ClearanceType,
    string Status, string? CustomsOfficeNameFa, string? CustomsOfficeNameEn, DateOnly? DeclarationDate,
    DateOnly? ReleaseDate, string? Notes, long Version);
public sealed record TradeCostResponse(Guid Id, Guid ProjectId, string ScopeType, Guid ScopeId, string CostType,
    decimal Amount, Guid CurrencyId, string CurrencyCode, string CurrencyNameFa, string? CurrencyNameEn,
    DateOnly EffectiveDate, string? ReferenceNumber, string? Notes, bool IsVoided, DateTime CreatedAtUtc);
public sealed record TradeAuditResponse(Guid Id, Guid ActorUserId, string EntityType, Guid EntityId, string Action,
    string? Reason, DateTime OccurredAtUtc);

public sealed record CreateTradeReferenceRequest(string ReferenceType, string NameFa, string? NameEn, int SortOrder);
public sealed record UpdateTradeReferenceRequest(string NameFa, string? NameEn, int SortOrder, long ExpectedVersion);
public sealed record ChangeTradeReferenceStateRequest(bool IsActive, long ExpectedVersion, string Reason);
public sealed record CreateTradeProjectRequest(string TitleFa, string? TitleEn, Guid? CustomerId, Guid? SupplierId,
    string Priority, DateOnly? TargetDate, string? Notes);
public sealed record UpdateTradeProjectRequest(string TitleFa, string? TitleEn, string Priority, DateOnly? TargetDate,
    string? Notes, long ExpectedVersion);
public sealed record ChangeTradeProjectStatusRequest(string Status, long ExpectedVersion, string Reason, bool IsCorrection = false);
public sealed record ReopenTradeProjectRequest(long ExpectedVersion, string Reason);
public sealed record CreateOrderRegistrationRequest(Guid ProjectId, string RegistrationNumber, string Status,
    DateOnly RegistrationDate, [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal RegisteredAmount,
    Guid CurrencyId, string? Notes);
public sealed record CreateFxAllocationRequest(Guid ProjectId, Guid? OrderRegistrationId, string ProcessType,
    string DealStatus, string ReceiptStatus, decimal Amount, Guid CurrencyId, Guid? BankId, DateOnly RequestDate,
    DateOnly? AllocationDate, string? ReferenceNumber, string? Notes);
public sealed record CreateTradeShipmentRequest(Guid ProjectId, string Status, string TransportMode, Guid? ForwarderId,
    string Origin, string Destination, DateOnly? EstimatedDeparture, DateOnly? EstimatedArrival,
    decimal? GrossWeight, decimal? VolumeCbm, string AllocationMethod, decimal AllocationValue, string? Notes);
public sealed record UpdateTradeShipmentActualsRequest(string Status, DateOnly? ActualDeparture, DateOnly? ActualArrival,
    string? Notes, long ExpectedVersion, string Reason, bool IsCorrection = false);
public sealed record CreateTradeShipmentLegRequest(int Sequence, string FromLocation, string ToLocation,
    string TransportMode, string Status, DateOnly? EstimatedDeparture, DateOnly? EstimatedArrival,
    string? TrackingNumber, string? Notes);
public sealed record CreateTradeCustomsRequest(Guid ShipmentId, string ClearanceNumber, string ClearanceType,
    string Status, Guid? CustomsOfficeId, DateOnly? DeclarationDate, DateOnly? ReleaseDate, string? Notes);
public sealed record CreateTradeCostRequest(Guid ProjectId, string ScopeType, Guid ScopeId, string CostType,
    decimal Amount, Guid CurrencyId, DateOnly EffectiveDate, string? ReferenceNumber, string? Notes);
public sealed record VoidTradeCostRequest(string Reason);
public sealed record CreateTradeReportNoteRequest(Guid ProjectId, string Note);
public sealed record ChangeOrderRegistrationStatusRequest(string Status, long ExpectedVersion, string Reason, bool IsCorrection = false);
public sealed record ChangeFxStatusRequest(string ProcessType, string DealStatus, string ReceiptStatus,
    DateOnly? AllocationDate, string? Notes, long ExpectedVersion, string Reason, bool IsCorrection = false);
public sealed record ChangeShipmentLegStatusRequest(string Status, DateOnly? ActualDeparture, DateOnly? ActualArrival,
    string? Notes, long ExpectedVersion, string Reason, bool IsCorrection = false);
public sealed record ChangeCustomsStatusRequest(string Status, DateOnly? ReleaseDate, string? Notes,
    long ExpectedVersion, string Reason, bool IsCorrection = false);
public sealed record LinkExistingShipmentRequest(Guid ProjectId, string AllocationMethod, decimal AllocationValue, string? Notes);
public sealed record UpdateShipmentAllocationRequest(string AllocationMethod, decimal AllocationValue, string? Notes,
    long ExpectedVersion, string Reason);

public sealed class TradingValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Trading validation failed.")
{ public IReadOnlyDictionary<string, string[]> Errors { get; } = errors; }
public sealed class TradingConflictException(string code, string message) : Exception(message) { public string Code { get; } = code; }

// The scoped first-step command deliberately has no priority, FX, shipment or financial fields.
public sealed record CreateTradeProjectSetupRequest(string TitleFa, DateOnly StartDate,
    Guid ResponsibleUserId, Guid? CustomerId, Guid? BusinessCardId, string? Notes);
public sealed record TradeProjectSetupLookupResponse(IReadOnlyList<TradingLookupItem> Items,
    int Page, int PageSize, int TotalCount);

public sealed record ResolveTradeProjectSetupDateRequest(string? JalaliDate);
public sealed record ResolvedTradeProjectSetupDate(string JalaliDate, DateOnly GregorianDate);
