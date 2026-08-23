namespace Hadron.Erp.Api.Modules.Catalog.Application;

public sealed record MaterialReferenceSummary(Guid Id, string Code, string NameFa, string NameEn, bool IsActive);
public sealed record MaterialUnitSummary(Guid Id, string Code, string NameFa, string NameEn, string? Symbol, bool IsActive);
public sealed record MaterialItemResponse(Guid Id, string ItemType, string Number, string? Code, string NameFa,
    string? NameEn, MaterialReferenceSummary Category, MaterialUnitSummary BaseUnit, string SupplyMethod,
    decimal? NetWeightKg, decimal DefaultPlanningScrapPercent, decimal SafetyStockQty, int CriticalDays,
    int SafeStartDays, int ExcessStartDays, bool IsActive, string? Notes, long Version,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
public sealed record MaterialItemMutationResponse(MaterialItemResponse Item, IReadOnlyList<string> Warnings);
public sealed record MaterialItemListResponse(IReadOnlyList<MaterialItemResponse> Items, int Page, int PageSize,
    int TotalCount);
public sealed record CreateMaterialItemRequest(string? Code, string NameFa, string? NameEn, Guid CategoryId,
    Guid BaseUnitId, string SupplyMethod, decimal? NetWeightKg, decimal DefaultPlanningScrapPercent,
    decimal SafetyStockQty, int CriticalDays, int SafeStartDays, int ExcessStartDays, string? Notes);
public sealed record UpdateMaterialItemRequest(string? Code, string NameFa, string? NameEn, Guid CategoryId,
    Guid BaseUnitId, string SupplyMethod, decimal? NetWeightKg, decimal DefaultPlanningScrapPercent,
    decimal SafetyStockQty, int CriticalDays, int SafeStartDays, int ExcessStartDays, string? Notes,
    long ExpectedVersion);
public sealed record ChangeMaterialItemStateRequest(long ExpectedVersion, string? Reason);

public sealed class MaterialItemValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Material Item validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class MaterialItemConflictException(string code, string message, object? details = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public object? Details { get; } = details;
}
