using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Identity.Domain;

namespace Hadron.Erp.Api.Modules.Production.Domain;

public static class ManufacturingRouteStatuses
{
    public const string Draft = "DRAFT";
    public const string Approved = "APPROVED";
    public const string Obsolete = "OBSOLETE";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    { Draft, Approved, Obsolete };
}

public sealed class ProductionWorkCenter
{
    private ProductionWorkCenter() { }

    public ProductionWorkCenter(string businessNumber, string nameFa, string? nameEn,
        Guid organizationalUnitId, int workingWeekMask)
    {
        Id = Guid.NewGuid();
        BusinessNumber = businessNumber;
        NameFa = nameFa.Trim();
        NameEn = Clean(nameEn);
        OrganizationalUnitId = organizationalUnitId;
        WorkingWeekMask = workingWeekMask;
        CreatedAtUtc = UpdatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public string BusinessNumber { get; private set; } = string.Empty;
    public string NameFa { get; private set; } = string.Empty;
    public string? NameEn { get; private set; }
    public Guid OrganizationalUnitId { get; private set; }
    public OrganizationalUnit OrganizationalUnit { get; private set; } = null!;
    public int WorkingWeekMask { get; private set; }
    public bool IsActive { get; private set; } = true;
    public long Version { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public void Edit(string nameFa, string? nameEn, Guid organizationalUnitId, int workingWeekMask)
    {
        NameFa = nameFa.Trim();
        NameEn = Clean(nameEn);
        OrganizationalUnitId = organizationalUnitId;
        WorkingWeekMask = workingWeekMask;
        Advance();
    }

    public void SetActive(bool active) { IsActive = active; Advance(); }
    private void Advance() { Version++; UpdatedAtUtc = DateTime.UtcNow; }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class ManufacturingRoute
{
    private ManufacturingRoute() { }

    public ManufacturingRoute(string businessNumber, Guid itemId, Guid itemBaseUnitId,
        int revisionNumber, string? notes)
    {
        Id = Guid.NewGuid();
        BusinessNumber = businessNumber;
        ItemId = itemId;
        ItemBaseUnitId = itemBaseUnitId;
        RevisionNumber = revisionNumber;
        Notes = Clean(notes);
        CreatedAtUtc = UpdatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public string BusinessNumber { get; private set; } = string.Empty;
    public Guid ItemId { get; private set; }
    public Item Item { get; private set; } = null!;
    public Guid ItemBaseUnitId { get; private set; }
    public int RevisionNumber { get; private set; }
    public string Status { get; private set; } = ManufacturingRouteStatuses.Draft;
    public string? Notes { get; private set; }
    public long Version { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public DateTime? ObsoletedAtUtc { get; private set; }
    public ICollection<ManufacturingRouteOperation> Operations { get; private set; } = [];

    public void Edit(string? notes)
    {
        EnsureDraft();
        Notes = Clean(notes);
        Advance();
    }

    public void Approve()
    {
        EnsureDraft();
        Status = ManufacturingRouteStatuses.Approved;
        ApprovedAtUtc = DateTime.UtcNow;
        Advance();
    }

    public void Obsolete()
    {
        if (Status != ManufacturingRouteStatuses.Approved) throw new InvalidOperationException();
        Status = ManufacturingRouteStatuses.Obsolete;
        ObsoletedAtUtc = DateTime.UtcNow;
        Advance();
    }

    private void EnsureDraft()
    {
        if (Status != ManufacturingRouteStatuses.Draft) throw new InvalidOperationException();
    }
    private void Advance() { Version++; UpdatedAtUtc = DateTime.UtcNow; }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class ManufacturingRouteOperation
{
    private ManufacturingRouteOperation() { }

    public ManufacturingRouteOperation(Guid routeId, int sequence, Guid workCenterId,
        int preparationWorkingDays, int executionWorkingDays, string? notes)
    {
        Id = Guid.NewGuid();
        RouteId = routeId;
        Sequence = sequence;
        WorkCenterId = workCenterId;
        PreparationWorkingDays = preparationWorkingDays;
        ExecutionWorkingDays = executionWorkingDays;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid RouteId { get; private set; }
    public ManufacturingRoute Route { get; private set; } = null!;
    public int Sequence { get; private set; }
    public Guid WorkCenterId { get; private set; }
    public ProductionWorkCenter WorkCenter { get; private set; } = null!;
    public int PreparationWorkingDays { get; private set; }
    public int ExecutionWorkingDays { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}
