using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Hadron.Erp.Api.Modules.Suppliers.Domain;

namespace Hadron.Erp.Api.Modules.Sourcing.Domain;

public static class SourcingRouteScopes
{
    public const string Domestic = "DOMESTIC";
    public const string Import = "IMPORT";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    { Domestic, Import };
}

public sealed class SourcingOption
{
    private SourcingOption() { }

    // Compatibility constructor for historical imports and tests. Existing records are
    // intentionally classified as IMPORT by the migration; new API writes must be explicit.
    public SourcingOption(string number, Guid itemId, Guid supplierId, Guid itemBaseUnitId,
        decimal? minimumOrderQuantity, Guid? shippingModeId, Guid? incotermId,
        int? supplierLeadTimeDays, int? forwarderToShipmentDays, int? shippingPeriodDays,
        int? destinationCustomsDays)
        : this(number, itemId, supplierId, itemBaseUnitId, SourcingRouteScopes.Import,
            minimumOrderQuantity, shippingModeId, incotermId, supplierLeadTimeDays,
            forwarderToShipmentDays, shippingPeriodDays, destinationCustomsDays) { }

    public SourcingOption(string number, Guid itemId, Guid supplierId, Guid itemBaseUnitId, string routeScope,
        decimal? minimumOrderQuantity,
        Guid? shippingModeId, Guid? incotermId, int? supplierLeadTimeDays, int? forwarderToShipmentDays,
        int? shippingPeriodDays, int? destinationCustomsDays)
    {
        Id = Guid.NewGuid();
        SourcingNumber = number;
        ItemId = itemId;
        SupplierId = supplierId;
        ItemBaseUnitId = itemBaseUnitId;
        RouteScope = routeScope;
        MinimumOrderQuantity = minimumOrderQuantity;
        ShippingModeId = shippingModeId;
        IncotermId = incotermId;
        SupplierLeadTimeDays = supplierLeadTimeDays;
        ForwarderToShipmentDays = forwarderToShipmentDays;
        ShippingPeriodDays = shippingPeriodDays;
        DestinationCustomsDays = destinationCustomsDays;
    }

    public Guid Id { get; private set; }
    public string SourcingNumber { get; private set; } = string.Empty;
    public Guid ItemId { get; private set; }
    public Item Item { get; private set; } = null!;
    public Guid SupplierId { get; private set; }
    public Supplier Supplier { get; private set; } = null!;
    public Guid ItemBaseUnitId { get; private set; }
    public Unit ItemBaseUnit { get; private set; } = null!;
    public string RouteScope { get; private set; } = SourcingRouteScopes.Import;
    public decimal? MinimumOrderQuantity { get; private set; }
    public Guid? ShippingModeId { get; private set; }
    public ReferenceValue? ShippingMode { get; private set; }
    public Guid? IncotermId { get; private set; }
    public ReferenceValue? Incoterm { get; private set; }
    public int? SupplierLeadTimeDays { get; private set; }
    public int? ForwarderToShipmentDays { get; private set; }
    public int? ShippingPeriodDays { get; private set; }
    public int? DestinationCustomsDays { get; private set; }
    public bool IsPreferred { get; private set; }
    public bool IsActive { get; private set; } = true;
    public long Version { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public ICollection<SourcingPrice> Prices { get; private set; } = [];

    public void Edit(string routeScope, decimal? minimumOrderQuantity, Guid? shippingModeId, Guid? incotermId,
        int? supplierLeadTimeDays, int? forwarderToShipmentDays, int? shippingPeriodDays, int? destinationCustomsDays)
    {
        RouteScope = routeScope;
        MinimumOrderQuantity = minimumOrderQuantity;
        ShippingModeId = shippingModeId;
        IncotermId = incotermId;
        SupplierLeadTimeDays = supplierLeadTimeDays;
        ForwarderToShipmentDays = forwarderToShipmentDays;
        ShippingPeriodDays = shippingPeriodDays;
        DestinationCustomsDays = destinationCustomsDays;
        Advance();
    }

    public void Edit(decimal? minimumOrderQuantity, Guid? shippingModeId, Guid? incotermId,
        int? supplierLeadTimeDays, int? forwarderToShipmentDays, int? shippingPeriodDays,
        int? destinationCustomsDays) => Edit(RouteScope, minimumOrderQuantity, shippingModeId,
            incotermId, supplierLeadTimeDays, forwarderToShipmentDays, shippingPeriodDays,
            destinationCustomsDays);

    public void ChangeState(bool active)
    {
        IsActive = active;
        if (!active) IsPreferred = false;
        Advance();
    }

    public void SetPreferred(bool preferred)
    {
        IsPreferred = preferred;
        Advance();
    }

    public void Touch() => Advance();

    private void Advance()
    {
        Version++;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}

public sealed class SourcingPrice
{
    private SourcingPrice() { }

    public SourcingPrice(Guid sourcingOptionId, decimal unitPrice, Guid currencyId, DateOnly effectiveFrom,
        DateOnly? effectiveTo)
    {
        Id = Guid.NewGuid();
        SourcingOptionId = sourcingOptionId;
        UnitPrice = unitPrice;
        CurrencyId = currencyId;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
    }

    public Guid Id { get; private set; }
    public Guid SourcingOptionId { get; private set; }
    public SourcingOption SourcingOption { get; private set; } = null!;
    public decimal UnitPrice { get; private set; }
    public Guid CurrencyId { get; private set; }
    public Currency Currency { get; private set; } = null!;
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public void Close(DateOnly effectiveTo)
    {
        EffectiveTo = effectiveTo;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}

public static class LeadTimeStageCodes
{
    public const string Supplier = "SUPPLIER";
    public const string ForwarderPreShipment = "FORWARDER_PRE_SHIPMENT";
    public const string Shipping = "SHIPPING";
    public const string DestinationCustoms = "DESTINATION_CUSTOMS";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    { Supplier, ForwarderPreShipment, Shipping, DestinationCustoms };
}

/// <summary>Durable Sourcing-owned audit history for an explicitly applied analytics recommendation.</summary>
public sealed class LeadTimeRecommendationApplication
{
    private LeadTimeRecommendationApplication() { }

    public LeadTimeRecommendationApplication(Guid sourcingOptionId, string stageCode, int? previousDays,
        int recommendedDays, int sampleCount, DateOnly windowStartExclusive, DateOnly windowEndInclusive,
        string? reason, string? actorIdentifier)
    {
        Id = Guid.NewGuid();
        SourcingOptionId = sourcingOptionId;
        StageCode = stageCode;
        PreviousDays = previousDays;
        RecommendedDays = recommendedDays;
        AppliedDays = recommendedDays;
        SampleCount = sampleCount;
        WindowStartExclusive = windowStartExclusive;
        WindowEndInclusive = windowEndInclusive;
        Reason = reason;
        ActorIdentifier = actorIdentifier;
    }

    public Guid Id { get; private set; }
    public Guid SourcingOptionId { get; private set; }
    public SourcingOption SourcingOption { get; private set; } = null!;
    public string StageCode { get; private set; } = string.Empty;
    public int? PreviousDays { get; private set; }
    public int RecommendedDays { get; private set; }
    public int AppliedDays { get; private set; }
    public int SampleCount { get; private set; }
    public DateOnly WindowStartExclusive { get; private set; }
    public DateOnly WindowEndInclusive { get; private set; }
    public string Method { get; private set; } = "P80_PERCENTILE_CONT_CEILING";
    public string? Reason { get; private set; }
    public string? ActorIdentifier { get; private set; }
    public DateTime AppliedAtUtc { get; private set; }
}
