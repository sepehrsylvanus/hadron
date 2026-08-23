using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Hadron.Erp.Api.Modules.Suppliers.Domain;

namespace Hadron.Erp.Api.Modules.Sourcing.Domain;

public sealed class SourcingOption
{
    private SourcingOption() { }

    public SourcingOption(string number, Guid itemId, Guid supplierId, Guid itemBaseUnitId, decimal? minimumOrderQuantity,
        Guid? shippingModeId, Guid? incotermId, int? supplierLeadTimeDays, int? forwarderToShipmentDays,
        int? shippingPeriodDays, int? destinationCustomsDays)
    {
        Id = Guid.NewGuid();
        SourcingNumber = number;
        ItemId = itemId;
        SupplierId = supplierId;
        ItemBaseUnitId = itemBaseUnitId;
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

    public void Edit(decimal? minimumOrderQuantity, Guid? shippingModeId, Guid? incotermId,
        int? supplierLeadTimeDays, int? forwarderToShipmentDays, int? shippingPeriodDays, int? destinationCustomsDays)
    {
        MinimumOrderQuantity = minimumOrderQuantity;
        ShippingModeId = shippingModeId;
        IncotermId = incotermId;
        SupplierLeadTimeDays = supplierLeadTimeDays;
        ForwarderToShipmentDays = forwarderToShipmentDays;
        ShippingPeriodDays = shippingPeriodDays;
        DestinationCustomsDays = destinationCustomsDays;
        Advance();
    }

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
