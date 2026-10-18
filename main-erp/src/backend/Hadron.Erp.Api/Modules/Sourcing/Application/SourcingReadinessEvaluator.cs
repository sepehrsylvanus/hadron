using Hadron.Erp.Api.Modules.Sourcing.Domain;

namespace Hadron.Erp.Api.Modules.Sourcing.Application;

public sealed record SourcingReadinessEvaluation(string State, IReadOnlyList<string> ReasonCodes,
    int? TotalLeadTimeDays);

public static class SourcingReadinessEvaluator
{
    private static readonly IReadOnlySet<string> ExternalMethods =
        new HashSet<string>(StringComparer.Ordinal) { "BUY", "OUTSOURCE", "HYBRID" };

    public static SourcingReadinessEvaluation Evaluate(SourcingOption option, string? supplyMethod,
        Guid expectedBaseUnitId)
    {
        var leadStages = option.RouteScope == SourcingRouteScopes.Domestic
            ? new[] { option.SupplierLeadTimeDays, option.ShippingPeriodDays }
            : new[] { option.SupplierLeadTimeDays, option.ForwarderToShipmentDays,
                option.ShippingPeriodDays, option.DestinationCustomsDays };
        int? totalLeadTime = leadStages.All(x => x.HasValue) ? leadStages.Sum(x => x!.Value) : null;
        var reasons = new List<string>();
        if (!option.Item.IsActive) reasons.Add("ITEM_INACTIVE");
        if (!option.Supplier.IsActive) reasons.Add("SUPPLIER_INACTIVE");
        if (supplyMethod is null || !ExternalMethods.Contains(supplyMethod)) reasons.Add("SUPPLY_METHOD_INCOMPATIBLE");
        if (option.ItemBaseUnitId != expectedBaseUnitId || option.Item.BaseUnitId != expectedBaseUnitId) reasons.Add("BASE_UNIT_CHANGED");
        if (option.ShippingMode is { IsActive: false }) reasons.Add("SHIPPING_MODE_INACTIVE");
        if (option.RouteScope == SourcingRouteScopes.Import && option.Incoterm is { IsActive: false })
            reasons.Add("INCOTERM_INACTIVE");
        if (!option.IsActive) return new(SourcingReadiness.Inactive, reasons, totalLeadTime);
        if (reasons.Count > 0) return new(SourcingReadiness.Blocked, reasons, totalLeadTime);

        if (!option.MinimumOrderQuantity.HasValue) reasons.Add("MOQ_MISSING");
        if (!option.ShippingModeId.HasValue) reasons.Add("SHIPPING_MODE_MISSING");
        if (!option.SupplierLeadTimeDays.HasValue) reasons.Add("SUPPLIER_LEAD_TIME_MISSING");
        if (!option.ShippingPeriodDays.HasValue) reasons.Add("SHIPPING_PERIOD_MISSING");
        if (option.RouteScope == SourcingRouteScopes.Import)
        {
            if (!option.ForwarderToShipmentDays.HasValue) reasons.Add("FORWARDER_LEAD_TIME_MISSING");
            if (!option.DestinationCustomsDays.HasValue) reasons.Add("DESTINATION_CUSTOMS_MISSING");
        }
        if (reasons.Count > 0) return new(SourcingReadiness.IncompleteData, reasons, totalLeadTime);

        return new(SourcingReadiness.Ready, [], totalLeadTime);
    }
}
