using Hadron.Erp.Api.Modules.ReferenceData.Domain;

namespace Hadron.Erp.Api.Modules.Catalog.Domain;

public static class SupplyMethods
{
    public const string Buy = "BUY";
    public const string Make = "MAKE";
    public const string Outsource = "OUTSOURCE";
    public const string Hybrid = "HYBRID";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Buy, Make, Outsource, Hybrid
    };
}

public sealed class MaterialItemProfile
{
    private MaterialItemProfile() { }

    public MaterialItemProfile(Guid itemId, Guid categoryId, string supplyMethod, decimal? netWeightKg,
        decimal defaultPlanningScrapPercent, decimal safetyStockQty, int criticalDays, int safeStartDays,
        int excessStartDays)
    {
        ItemId = itemId;
        CategoryId = categoryId;
        SupplyMethod = supplyMethod;
        NetWeightKg = netWeightKg;
        DefaultPlanningScrapPercent = defaultPlanningScrapPercent;
        SafetyStockQty = safetyStockQty;
        CriticalDays = criticalDays;
        SafeStartDays = safeStartDays;
        ExcessStartDays = excessStartDays;
    }

    public Guid ItemId { get; private set; }
    public Item Item { get; private set; } = null!;
    public Guid CategoryId { get; private set; }
    public ReferenceValue Category { get; private set; } = null!;
    public string SupplyMethod { get; private set; } = string.Empty;
    public decimal? NetWeightKg { get; private set; }
    public decimal DefaultPlanningScrapPercent { get; private set; }
    public decimal SafetyStockQty { get; private set; }
    public int CriticalDays { get; private set; }
    public int SafeStartDays { get; private set; }
    public int ExcessStartDays { get; private set; }

    public void Edit(Guid categoryId, string supplyMethod, decimal? netWeightKg,
        decimal defaultPlanningScrapPercent, decimal safetyStockQty, int criticalDays, int safeStartDays,
        int excessStartDays)
    {
        CategoryId = categoryId;
        SupplyMethod = supplyMethod;
        NetWeightKg = netWeightKg;
        DefaultPlanningScrapPercent = defaultPlanningScrapPercent;
        SafetyStockQty = safetyStockQty;
        CriticalDays = criticalDays;
        SafeStartDays = safeStartDays;
        ExcessStartDays = excessStartDays;
    }
}
