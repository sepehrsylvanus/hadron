using Hadron.Erp.Api.Modules.ReferenceData.Domain;

namespace Hadron.Erp.Api.Modules.Catalog.Domain;

public static class ItemTypes
{
    public const string Product = "PRODUCT";
    public const string Part = "PART";
    public const string Component = "COMPONENT";
}

public sealed class Item
{
    private Item() { }

    public Item(string itemType, string businessNumber, string nameFa, string? nameEn,
        Guid baseUnitId, string? notes)
    {
        Id = Guid.NewGuid();
        ItemType = itemType;
        BusinessNumber = businessNumber;
        NameFa = nameFa;
        NameEn = nameEn;
        BaseUnitId = baseUnitId;
        Notes = notes;
    }

    public Guid Id { get; private set; }
    public string ItemType { get; private set; } = string.Empty;
    public string BusinessNumber { get; private set; } = string.Empty;
    public string NameFa { get; private set; } = string.Empty;
    public string? NameEn { get; private set; }
    public Guid BaseUnitId { get; private set; }
    public Unit BaseUnit { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;
    public string? Notes { get; private set; }
    public long Version { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public void Edit(string nameFa, string? nameEn, Guid baseUnitId, string? notes)
    {
        NameFa = nameFa;
        NameEn = nameEn;
        BaseUnitId = baseUnitId;
        Notes = notes;
        Advance();
    }

    public void ChangeState(bool active)
    {
        IsActive = active;
        Advance();
    }

    private void Advance()
    {
        Version++;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
