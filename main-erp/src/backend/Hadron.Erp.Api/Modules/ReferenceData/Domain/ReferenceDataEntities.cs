namespace Hadron.Erp.Api.Modules.ReferenceData.Domain;

public sealed class ReferenceType
{
    private ReferenceType() { }

    public ReferenceType(Guid id, string code, string nameFa, string nameEn)
    {
        Id = id;
        Code = code;
        NameFa = nameFa;
        NameEn = nameEn;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string NameFa { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
}

public abstract class MutableReferenceData
{
    public Guid Id { get; protected set; }
    public string Code { get; protected set; } = string.Empty;
    public string NormalizedCode { get; protected set; } = string.Empty;
    public string NameFa { get; protected set; } = string.Empty;
    public string NameEn { get; protected set; } = string.Empty;
    public bool IsActive { get; protected set; } = true;
    public long Version { get; protected set; } = 1;
    public DateTime CreatedAtUtc { get; protected set; }
    public DateTime UpdatedAtUtc { get; protected set; }

    public void SetNames(string nameFa, string nameEn)
    {
        NameFa = nameFa;
        NameEn = nameEn;
    }

    public void SetActive(bool isActive) => IsActive = isActive;
    public void AdvanceVersion() => Version++;
    public void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}

public sealed class ReferenceValue : MutableReferenceData
{
    private ReferenceValue() { }

    public ReferenceValue(Guid referenceTypeId, string code, string normalizedCode, string nameFa, string nameEn, int sortOrder)
    {
        Id = Guid.NewGuid();
        ReferenceTypeId = referenceTypeId;
        Code = code;
        NormalizedCode = normalizedCode;
        NameFa = nameFa;
        NameEn = nameEn;
        SortOrder = sortOrder;
    }

    public Guid ReferenceTypeId { get; private set; }
    public ReferenceType ReferenceType { get; private set; } = null!;
    public int SortOrder { get; private set; }

    public void Edit(string nameFa, string nameEn, int sortOrder)
    {
        SetNames(nameFa, nameEn);
        SortOrder = sortOrder;
    }
}

public sealed class Currency : MutableReferenceData
{
    private Currency() { }

    public Currency(string isoCode, string normalizedCode, string nameFa, string nameEn, string? symbol, int decimalPrecision)
    {
        Id = Guid.NewGuid();
        Code = isoCode;
        NormalizedCode = normalizedCode;
        NameFa = nameFa;
        NameEn = nameEn;
        Symbol = symbol;
        DecimalPrecision = decimalPrecision;
    }

    public string? Symbol { get; private set; }
    public int DecimalPrecision { get; private set; }

    public void Edit(string nameFa, string nameEn, string? symbol, int decimalPrecision)
    {
        SetNames(nameFa, nameEn);
        Symbol = symbol;
        DecimalPrecision = decimalPrecision;
    }
}

public sealed class Unit : MutableReferenceData
{
    private Unit() { }

    public Unit(string code, string normalizedCode, string nameFa, string nameEn, string? symbol, int sortOrder)
    {
        Id = Guid.NewGuid();
        Code = code;
        NormalizedCode = normalizedCode;
        NameFa = nameFa;
        NameEn = nameEn;
        Symbol = symbol;
        SortOrder = sortOrder;
    }

    public string? Symbol { get; private set; }
    public int SortOrder { get; private set; }

    public void Edit(string nameFa, string nameEn, string? symbol, int sortOrder)
    {
        SetNames(nameFa, nameEn);
        Symbol = symbol;
        SortOrder = sortOrder;
    }
}
