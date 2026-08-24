namespace Hadron.Erp.Api.Modules.Sourcing.Application;

public static class SourcingReadiness
{
    public const string Inactive = "INACTIVE";
    public const string Blocked = "BLOCKED";
    public const string IncompleteData = "INCOMPLETE_DATA";
    public const string Ready = "READY";
}

public sealed record SourcingReference(Guid Id, string Code, string NameFa, string NameEn, bool IsActive);
public sealed record SourcingUnitReference(Guid Id, string Code, string NameFa, string NameEn, string? Symbol, bool IsActive);
public sealed record SourcingItemReference(Guid Id, string Code, string NameFa, string? NameEn,
    string ItemType, string SupplyMethod, bool IsActive, SourcingUnitReference BaseUnit);
public sealed record SourcingSupplierReference(Guid Id, string SupplierNumber, string Name, bool IsActive);
public sealed record SourcingPriceResponse(Guid Id, decimal UnitPrice, SourcingReference Currency,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
public sealed record SourcingReadinessResponse(string State, IReadOnlyList<string> ReasonCodes);
public sealed record SourcingOptionResponse(Guid Id, string SourcingNumber, SourcingItemReference Item,
    SourcingSupplierReference Supplier, SourcingUnitReference ItemBaseUnit, decimal? MinimumOrderQuantity,
    SourcingReference? ShippingMode, SourcingReference? Incoterm, int? SupplierLeadTimeDays,
    int? ForwarderToShipmentDays, int? ShippingPeriodDays, int? DestinationCustomsDays,
    int? TotalConfiguredLeadTimeDays, bool IsPreferred, bool IsActive, long Version, DateOnly EvaluationDate,
    SourcingReadinessResponse Readiness, SourcingPriceResponse? CurrentPrice, DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc, IReadOnlyList<SourcingPriceResponse> Prices);
public sealed record SourcingOptionListResponse(IReadOnlyList<SourcingOptionResponse> Items, int Page, int PageSize,
    int TotalCount, DateOnly EvaluationDate);

public sealed record CreateSourcingOptionRequest(Guid ItemId, Guid SupplierId, decimal? MinimumOrderQuantity,
    Guid? ShippingModeId, Guid? IncotermId, int? SupplierLeadTimeDays, int? ForwarderToShipmentDays,
    int? ShippingPeriodDays, int? DestinationCustomsDays);
public sealed record UpdateSourcingOptionRequest(decimal? MinimumOrderQuantity, Guid? ShippingModeId,
    Guid? IncotermId, int? SupplierLeadTimeDays, int? ForwarderToShipmentDays, int? ShippingPeriodDays,
    int? DestinationCustomsDays, long ExpectedVersion);
public sealed record ChangeSourcingOptionStateRequest(long ExpectedVersion, string? Reason);
public sealed record SetPreferredSourcingOptionRequest(long ExpectedVersion, Guid? ExpectedCurrentPreferredId,
    long? ExpectedCurrentPreferredVersion, string? Reason);
public sealed record CreateSourcingPriceRequest(decimal UnitPrice, Guid CurrencyId, DateOnly EffectiveFrom,
    DateOnly? EffectiveTo, long ExpectedVersion);
public sealed record CloseSourcingPriceRequest(DateOnly EffectiveTo, long ExpectedVersion, string? Reason);

public sealed class SourcingValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Sourcing validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class SourcingConflictException(string code, string message, object? details = null) : Exception(message)
{
    public string Code { get; } = code;
    public object? Details { get; } = details;
}
