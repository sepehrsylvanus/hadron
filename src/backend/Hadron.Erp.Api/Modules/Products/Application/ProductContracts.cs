namespace Hadron.Erp.Api.Modules.Products.Application;

public sealed record ProductReferenceSummary(Guid Id, string Code, string NameFa, string NameEn, bool IsActive);
public sealed record ProductUnitSummary(Guid Id, string Code, string NameFa, string NameEn, string? Symbol, bool IsActive);
public sealed record ProductResponse(Guid Id, string ProductNumber, string Code, string NameFa, string? NameEn,
    ProductReferenceSummary ProductCategory, ProductUnitSummary BaseUnit, ProductReferenceSummary? BusinessUnit,
    decimal SafetyStockQty, int CriticalDays, int SafeStartDays, int ExcessStartDays, bool IsActive,
    string? Notes, long Version, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
public sealed record ProductMutationResponse(ProductResponse Product, IReadOnlyList<string> Warnings);
public sealed record ProductListResponse(IReadOnlyList<ProductResponse> Items, int Page, int PageSize, int TotalCount);
public sealed record CreateProductRequest(string Code, string NameFa, string? NameEn, Guid ProductCategoryId,
    Guid BaseUnitId, Guid? BusinessUnitId, decimal SafetyStockQty, int CriticalDays, int SafeStartDays,
    int ExcessStartDays, string? Notes);
public sealed record UpdateProductRequest(string Code, string NameFa, string? NameEn, Guid ProductCategoryId,
    Guid BaseUnitId, Guid? BusinessUnitId, decimal SafetyStockQty, int CriticalDays, int SafeStartDays,
    int ExcessStartDays, string? Notes, long ExpectedVersion);
public sealed record ChangeProductStateRequest(long ExpectedVersion, string? Reason);

public sealed class ProductValidationException(IReadOnlyDictionary<string, string[]> errors) : Exception("Product validation failed.")
{ public IReadOnlyDictionary<string, string[]> Errors { get; } = errors; }
public sealed class ProductConflictException(string code, string message, object? details = null) : Exception(message)
{ public string Code { get; } = code; public object? Details { get; } = details; }
