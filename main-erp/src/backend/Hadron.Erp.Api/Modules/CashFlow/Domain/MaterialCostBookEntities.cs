using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Hadron.Erp.Api.Modules.Suppliers.Domain;

namespace Hadron.Erp.Api.Modules.CashFlow.Domain;

public static class MaterialCostBookRevisionStatuses
{
    public const string Draft = "DRAFT";
    public const string Published = "PUBLISHED";
}

public static class MaterialCostBookOperationTypes
{
    public const string UpdateDraft = "UPDATE_DRAFT";
    public const string UpsertLine = "UPSERT_LINE";
    public const string RemoveLine = "REMOVE_LINE";
    public const string Publish = "PUBLISH";
}

public sealed class MaterialCostBook
{
    private MaterialCostBook() { }

    public MaterialCostBook(string bookNumber, string? actorIdentifier, DateTime createdAtUtc)
    {
        Id = Guid.NewGuid();
        BookNumber = bookNumber;
        CreatedByIdentifier = actorIdentifier;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public string BookNumber { get; private set; } = string.Empty;
    public string? CreatedByIdentifier { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public ICollection<MaterialCostBookRevision> Revisions { get; private set; } = [];
}

public sealed class MaterialCostBookRevision
{
    private MaterialCostBookRevision() { }

    public MaterialCostBookRevision(Guid bookId, int revision, string nameFa, string? nameEn,
        DateOnly effectiveDate, Guid? supersedesRevisionId, Guid clientRequestId,
        string requestFingerprint, string? changeReason, string? actorIdentifier,
        DateTime createdAtUtc)
    {
        Id = Guid.NewGuid();
        MaterialCostBookId = bookId;
        Revision = revision;
        NameFa = nameFa;
        NameEn = nameEn;
        EffectiveDate = effectiveDate;
        SupersedesRevisionId = supersedesRevisionId;
        ClientRequestId = clientRequestId;
        RequestFingerprint = requestFingerprint;
        ChangeReason = changeReason;
        CreatedByIdentifier = actorIdentifier;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid MaterialCostBookId { get; private set; }
    public MaterialCostBook MaterialCostBook { get; private set; } = null!;
    public int Revision { get; private set; }
    public string NameFa { get; private set; } = string.Empty;
    public string? NameEn { get; private set; }
    public DateOnly EffectiveDate { get; private set; }
    public string Status { get; private set; } = MaterialCostBookRevisionStatuses.Draft;
    public Guid? SupersedesRevisionId { get; private set; }
    public MaterialCostBookRevision? SupersedesRevision { get; private set; }
    public Guid ClientRequestId { get; private set; }
    public string RequestFingerprint { get; private set; } = string.Empty;
    public string? ChangeReason { get; private set; }
    public string? CreatedByIdentifier { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public string? UpdatedByIdentifier { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public string? PublishedByIdentifier { get; private set; }
    public DateTime? PublishedAtUtc { get; private set; }
    public string? PublishReason { get; private set; }
    public long Version { get; private set; } = 1;
    public ICollection<MaterialCostBookLine> Lines { get; private set; } = [];

    public void UpdateDraft(string nameFa, string? nameEn, DateOnly effectiveDate,
        string? changeReason, string? actorIdentifier, DateTime updatedAtUtc)
    {
        EnsureDraft();
        NameFa = nameFa;
        NameEn = nameEn;
        EffectiveDate = effectiveDate;
        ChangeReason = changeReason;
        UpdatedByIdentifier = actorIdentifier;
        UpdatedAtUtc = updatedAtUtc;
        Version++;
    }

    public void TouchDraft(string? actorIdentifier, DateTime updatedAtUtc)
    {
        EnsureDraft();
        UpdatedByIdentifier = actorIdentifier;
        UpdatedAtUtc = updatedAtUtc;
        Version++;
    }

    public void Publish(string reason, string? actorIdentifier, DateTime publishedAtUtc)
    {
        EnsureDraft();
        Status = MaterialCostBookRevisionStatuses.Published;
        PublishReason = reason;
        PublishedByIdentifier = actorIdentifier;
        PublishedAtUtc = publishedAtUtc;
        UpdatedByIdentifier = actorIdentifier;
        UpdatedAtUtc = publishedAtUtc;
        Version++;
    }

    private void EnsureDraft()
    {
        if (Status != MaterialCostBookRevisionStatuses.Draft)
            throw new InvalidOperationException("A published material cost book is immutable.");
    }
}

public sealed class MaterialCostBookLine
{
    private MaterialCostBookLine() { }

    public MaterialCostBookLine(Guid revisionId, Guid itemId, string itemCode,
        string itemNameFa, string? itemNameEn, Guid currencyId, string currencyCode,
        string currencyNameFa, string? currencyNameEn, int currencyDecimalPrecision,
        decimal originalPriceAmount,
        decimal priceBasisQuantity, Guid priceUnitId, string priceUnitCode,
        string priceUnitNameFa, string? priceUnitNameEn, decimal priceUnitFactorToBase,
        Guid itemBaseUnitId, string itemBaseUnitCode, string itemBaseUnitNameFa,
        string? itemBaseUnitNameEn, Guid? supplierId, string? supplierNumber,
        string? supplierName, string? sourceReference, string? notes,
        DateTime createdAtUtc)
    {
        Id = Guid.NewGuid();
        MaterialCostBookRevisionId = revisionId;
        ItemId = itemId;
        ItemCode = itemCode;
        ItemNameFa = itemNameFa;
        ItemNameEn = itemNameEn;
        CurrencyId = currencyId;
        CurrencyCode = currencyCode;
        CurrencyNameFa = currencyNameFa;
        CurrencyNameEn = currencyNameEn;
        CurrencyDecimalPrecision = currencyDecimalPrecision;
        OriginalPriceAmount = originalPriceAmount;
        PriceBasisQuantity = priceBasisQuantity;
        PriceUnitId = priceUnitId;
        PriceUnitCode = priceUnitCode;
        PriceUnitNameFa = priceUnitNameFa;
        PriceUnitNameEn = priceUnitNameEn;
        PriceUnitFactorToBase = priceUnitFactorToBase;
        ItemBaseUnitId = itemBaseUnitId;
        ItemBaseUnitCode = itemBaseUnitCode;
        ItemBaseUnitNameFa = itemBaseUnitNameFa;
        ItemBaseUnitNameEn = itemBaseUnitNameEn;
        SupplierId = supplierId;
        SupplierNumber = supplierNumber;
        SupplierName = supplierName;
        SourceReference = sourceReference;
        Notes = notes;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid MaterialCostBookRevisionId { get; private set; }
    public MaterialCostBookRevision MaterialCostBookRevision { get; private set; } = null!;
    public Guid ItemId { get; private set; }
    public Item Item { get; private set; } = null!;
    public string ItemCode { get; private set; } = string.Empty;
    public string ItemNameFa { get; private set; } = string.Empty;
    public string? ItemNameEn { get; private set; }
    public Guid CurrencyId { get; private set; }
    public Currency Currency { get; private set; } = null!;
    public string CurrencyCode { get; private set; } = string.Empty;
    public string CurrencyNameFa { get; private set; } = string.Empty;
    public string? CurrencyNameEn { get; private set; }
    public int CurrencyDecimalPrecision { get; private set; }
    public decimal OriginalPriceAmount { get; private set; }
    public decimal PriceBasisQuantity { get; private set; }
    public Guid PriceUnitId { get; private set; }
    public Unit PriceUnit { get; private set; } = null!;
    public string PriceUnitCode { get; private set; } = string.Empty;
    public string PriceUnitNameFa { get; private set; } = string.Empty;
    public string? PriceUnitNameEn { get; private set; }
    public decimal PriceUnitFactorToBase { get; private set; }
    public Guid ItemBaseUnitId { get; private set; }
    public Unit ItemBaseUnit { get; private set; } = null!;
    public string ItemBaseUnitCode { get; private set; } = string.Empty;
    public string ItemBaseUnitNameFa { get; private set; } = string.Empty;
    public string? ItemBaseUnitNameEn { get; private set; }
    public Guid? SupplierId { get; private set; }
    public Supplier? Supplier { get; private set; }
    public string? SupplierNumber { get; private set; }
    public string? SupplierName { get; private set; }
    public string? SourceReference { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public decimal NormalizedUnitPrice =>
        OriginalPriceAmount / (PriceBasisQuantity * PriceUnitFactorToBase);

    public void Edit(Guid currencyId, string currencyCode, string currencyNameFa,
        string? currencyNameEn, int currencyDecimalPrecision, decimal originalPriceAmount,
        decimal priceBasisQuantity,
        Guid priceUnitId, string priceUnitCode, string priceUnitNameFa,
        string? priceUnitNameEn, decimal priceUnitFactorToBase, Guid itemBaseUnitId,
        string itemBaseUnitCode, string itemBaseUnitNameFa, string? itemBaseUnitNameEn,
        Guid? supplierId, string? supplierNumber, string? supplierName,
        string? sourceReference, string? notes, DateTime updatedAtUtc)
    {
        CurrencyId = currencyId;
        CurrencyCode = currencyCode;
        CurrencyNameFa = currencyNameFa;
        CurrencyNameEn = currencyNameEn;
        CurrencyDecimalPrecision = currencyDecimalPrecision;
        OriginalPriceAmount = originalPriceAmount;
        PriceBasisQuantity = priceBasisQuantity;
        PriceUnitId = priceUnitId;
        PriceUnitCode = priceUnitCode;
        PriceUnitNameFa = priceUnitNameFa;
        PriceUnitNameEn = priceUnitNameEn;
        PriceUnitFactorToBase = priceUnitFactorToBase;
        ItemBaseUnitId = itemBaseUnitId;
        ItemBaseUnitCode = itemBaseUnitCode;
        ItemBaseUnitNameFa = itemBaseUnitNameFa;
        ItemBaseUnitNameEn = itemBaseUnitNameEn;
        SupplierId = supplierId;
        SupplierNumber = supplierNumber;
        SupplierName = supplierName;
        SourceReference = sourceReference;
        Notes = notes;
        UpdatedAtUtc = updatedAtUtc;
    }
}

public sealed class MaterialCostBookOperationReceipt
{
    private MaterialCostBookOperationReceipt() { }

    public MaterialCostBookOperationReceipt(Guid revisionId, string operationType,
        Guid clientRequestId, string requestFingerprint, long resultVersion,
        DateTime createdAtUtc)
    {
        Id = Guid.NewGuid();
        MaterialCostBookRevisionId = revisionId;
        OperationType = operationType;
        ClientRequestId = clientRequestId;
        RequestFingerprint = requestFingerprint;
        ResultVersion = resultVersion;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid MaterialCostBookRevisionId { get; private set; }
    public MaterialCostBookRevision MaterialCostBookRevision { get; private set; } = null!;
    public string OperationType { get; private set; } = string.Empty;
    public Guid ClientRequestId { get; private set; }
    public string RequestFingerprint { get; private set; } = string.Empty;
    public long ResultVersion { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}
