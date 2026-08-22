using Hadron.Erp.Api.Modules.ReferenceData.Domain;

namespace Hadron.Erp.Api.Modules.Products.Domain;

public sealed class Item
{
    private Item() { }

    public Item(string businessNumber, string code, string nameFa, string? nameEn, Guid baseUnitId, string? notes)
    {
        Id = Guid.NewGuid();
        ItemType = "PRODUCT";
        BusinessNumber = businessNumber;
        Code = code;
        NormalizedCode = code;
        NameFa = nameFa;
        NameEn = nameEn;
        BaseUnitId = baseUnitId;
        Notes = notes;
    }

    public Guid Id { get; private set; }
    public string ItemType { get; private set; } = string.Empty;
    public string BusinessNumber { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string NormalizedCode { get; private set; } = string.Empty;
    public string NameFa { get; private set; } = string.Empty;
    public string? NameEn { get; private set; }
    public Guid BaseUnitId { get; private set; }
    public Unit BaseUnit { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;
    public string? Notes { get; private set; }
    public long Version { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public void Edit(string code, string nameFa, string? nameEn, Guid baseUnitId, string? notes)
    {
        Code = code;
        NormalizedCode = code;
        NameFa = nameFa;
        NameEn = nameEn;
        BaseUnitId = baseUnitId;
        Notes = notes;
        Advance();
    }

    public void ChangeState(bool active) { IsActive = active; Advance(); }
    private void Advance() { Version++; UpdatedAtUtc = DateTime.UtcNow; }
}

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
