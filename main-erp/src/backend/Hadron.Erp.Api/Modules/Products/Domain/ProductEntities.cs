using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;

namespace Hadron.Erp.Api.Modules.Products.Domain;

public sealed class Product
{
    private Product() { }
    public Product(Guid itemId, Guid productCategoryId, Guid? businessUnitId, decimal safetyStockQty,
        int criticalDays, int safeStartDays, int excessStartDays)
    {
        ItemId = itemId;
        ProductCategoryId = productCategoryId;
        BusinessUnitId = businessUnitId;
        SafetyStockQty = safetyStockQty;
        CriticalDays = criticalDays;
        SafeStartDays = safeStartDays;
        ExcessStartDays = excessStartDays;
    }

    public Guid ItemId { get; private set; }
    public Item Item { get; private set; } = null!;
    public Guid ProductCategoryId { get; private set; }
    public ReferenceValue ProductCategory { get; private set; } = null!;
    public Guid? BusinessUnitId { get; private set; }
    public ReferenceValue? BusinessUnit { get; private set; }
    public decimal SafetyStockQty { get; private set; }
    public int CriticalDays { get; private set; }
    public int SafeStartDays { get; private set; }
    public int ExcessStartDays { get; private set; }

    public void Edit(Guid productCategoryId, Guid? businessUnitId, decimal safetyStockQty,
        int criticalDays, int safeStartDays, int excessStartDays)
    {
        ProductCategoryId = productCategoryId;
        BusinessUnitId = businessUnitId;
        SafetyStockQty = safetyStockQty;
        CriticalDays = criticalDays;
        SafeStartDays = safeStartDays;
        ExcessStartDays = excessStartDays;
    }
}
