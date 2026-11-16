namespace Hadron.Erp.Api.Modules.Bom.Application;

public sealed record BomUnitRef(Guid Id, string Code, string NameFa, string NameEn, string? Symbol, bool IsActive);
public sealed record BomItemRef(Guid Id, string Code, string NameFa, string? NameEn,
    string ItemType, bool IsActive, string? SupplyMethod, BomUnitRef CurrentBaseUnit);
public sealed record BomLineResponse(Guid Id, BomItemRef ChildItem, BomUnitRef ChildBaseUnitSnapshot,
    decimal UsageQty, decimal? ScrapOverridePercent, decimal EffectiveScrapPercent, int SortOrder,
    IReadOnlyList<string> IntegrityReasonCodes);
public sealed record BomRevisionResponse(Guid Id, string RevisionCode, BomItemRef ParentItem,
    BomUnitRef ParentBaseUnitSnapshot, string Status, decimal BaseOutputQty,
    string? RevisionNotes, Guid? CopiedFromRevisionId, long Version,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc, DateTime? ApprovedAtUtc, DateTime? ObsoletedAtUtc,
    int LineCount, IReadOnlyList<string> WarningCodes, IReadOnlyList<string> IntegrityReasonCodes,
    IReadOnlyList<BomLineResponse> Lines);
public sealed record BomRevisionListResponse(IReadOnlyList<BomRevisionResponse> Items, int Page, int PageSize,
    int TotalCount);

public sealed record CreateBomRevisionRequest(Guid ParentItemId, decimal BaseOutputQty, string? RevisionNotes);
public sealed record CopyBomRevisionRequest(string? RevisionNotes, Guid? TargetParentItemId = null);
public sealed record DiscardBomRevisionResponse(Guid Id, string RevisionCode, Guid ParentItemId);
public sealed record UpdateBomRevisionRequest(decimal BaseOutputQty, string? RevisionNotes, long ExpectedVersion);
public sealed record AddBomLineRequest(Guid ChildItemId, decimal UsageQty, decimal? ScrapOverridePercent,
    int SortOrder, long ExpectedVersion);
public sealed record UpdateBomLineRequest(decimal UsageQty, decimal? ScrapOverridePercent, int SortOrder,
    long ExpectedVersion);
public sealed record BomMutationRequest(long ExpectedVersion, string? Reason);
public sealed record ApproveBomRevisionRequest(long ExpectedVersion, string? Reason);
public sealed record ObsoleteBomRevisionRequest(long ExpectedVersion, string? Reason);
public sealed record BomValidationResponse(bool IsValidForApproval, IReadOnlyList<string> WarningCodes,
    IReadOnlyList<string> BlockingCodes);

public sealed record BomStructureItemRef(Guid Id, string Code, string NameFa, string? NameEn,
    string ItemType, bool IsActive, string? SupplyMethod, decimal? NetWeightKg, BomUnitRef CurrentBaseUnit);
public sealed record BomStructureRevisionRef(Guid Id, string RevisionCode, decimal BaseOutputQty);
public sealed record BomStructureRelationship(decimal UsageQty, decimal? ScrapOverridePercent,
    BomUnitRef ChildBaseUnitSnapshot);
public sealed record BomTreeNode(BomStructureItemRef Item, BomStructureRevisionRef? Revision,
    BomStructureRelationship? RelationshipFromParent, IReadOnlyList<BomTreeNode> Children);
public sealed record BomProductTreeResponse(BomTreeNode Root);
public sealed record BomBuilderLineResponse(Guid Id, BomStructureItemRef ChildItem,
    BomUnitRef ChildBaseUnitSnapshot, decimal UsageQty, decimal? ScrapOverridePercent,
    decimal EffectiveScrapPercent, int SortOrder, IReadOnlyList<string> IntegrityReasonCodes);
public sealed record BomBuilderRevisionResponse(Guid Id, string RevisionCode,
    BomStructureItemRef ParentItem, BomUnitRef ParentBaseUnitSnapshot, string Status,
    decimal BaseOutputQty, long Version,
    IReadOnlyList<BomBuilderLineResponse> Lines);
public sealed record BomBuilderTreeResponse(BomStructureItemRef RootItem,
    IReadOnlyList<BomBuilderRevisionResponse> Revisions,
    IReadOnlyList<Guid> MissingParentItemIds);
public sealed record BomWhereUsedParent(BomStructureItemRef ParentItem, BomStructureRevisionRef Revision,
    BomStructureRelationship Relationship);
public sealed record BomWhereUsedResponse(BomStructureItemRef Item,
    IReadOnlyList<BomWhereUsedParent> DirectParents, IReadOnlyList<BomStructureItemRef> FinalProducts);
public sealed record BomApplicableItemResponse(BomStructureItemRef Item, bool IsApplicable);
public sealed record BomApplicableItemListResponse(IReadOnlyList<BomApplicableItemResponse> Items,
    int Page, int PageSize, int TotalCount);
public sealed record BomItemApplicabilityResponse(Guid Id, BomStructureItemRef ParentItem,
    BomStructureItemRef ChildItem, bool IsActive, long Version, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
public sealed record BomItemApplicabilityListResponse(IReadOnlyList<BomItemApplicabilityResponse> Items,
    int Page, int PageSize, int TotalCount);
public sealed record CreateBomItemApplicabilityRequest(Guid ParentItemId, Guid ChildItemId);
public sealed record ChangeBomItemApplicabilityStateRequest(long ExpectedVersion, string? Reason);

public sealed class BomValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("BOM validation failed.")
{ public IReadOnlyDictionary<string, string[]> Errors { get; } = errors; }

public sealed class BomConflictException(string code, string message, object? details = null) : Exception(message)
{
    public string Code { get; } = code;
    public object? Details { get; } = details;
}
