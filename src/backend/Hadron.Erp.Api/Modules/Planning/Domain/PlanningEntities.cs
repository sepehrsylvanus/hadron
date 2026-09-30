using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Products.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;

namespace Hadron.Erp.Api.Modules.Planning.Domain;

public static class BusinessPeriodStatuses
{
    public const string Open = "OPEN";
    public const string Closed = "CLOSED";
}

public sealed class ForecastPlan
{
    private ForecastPlan() { }

    public ForecastPlan(string name, int startJalaliYear, int startJalaliMonth,
        int endJalaliYear, int endJalaliMonth, DateOnly startDate, DateOnly endDate,
        string? actorIdentifier)
    {
        Id = Guid.NewGuid();
        Name = name;
        StartJalaliYear = startJalaliYear;
        StartJalaliMonth = startJalaliMonth;
        EndJalaliYear = endJalaliYear;
        EndJalaliMonth = endJalaliMonth;
        StartDate = startDate;
        EndDate = endDate;
        ActorIdentifier = actorIdentifier;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int StartJalaliYear { get; private set; }
    public int StartJalaliMonth { get; private set; }
    public int EndJalaliYear { get; private set; }
    public int EndJalaliMonth { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public string? ActorIdentifier { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public ICollection<ForecastPlanEntry> Entries { get; private set; } = [];
}

public sealed class ForecastPlanEntry
{
    private ForecastPlanEntry() { }

    public ForecastPlanEntry(Guid forecastPlanId, Guid productId, Guid businessPeriodId,
        Guid forecastRevisionId, decimal forecastQty)
    {
        Id = Guid.NewGuid();
        ForecastPlanId = forecastPlanId;
        ProductId = productId;
        BusinessPeriodId = businessPeriodId;
        ForecastRevisionId = forecastRevisionId;
        ForecastQty = forecastQty;
    }

    public Guid Id { get; private set; }
    public Guid ForecastPlanId { get; private set; }
    public ForecastPlan ForecastPlan { get; private set; } = null!;
    public Guid ProductId { get; private set; }
    public Product Product { get; private set; } = null!;
    public Guid BusinessPeriodId { get; private set; }
    public BusinessPeriod BusinessPeriod { get; private set; } = null!;
    public Guid ForecastRevisionId { get; private set; }
    public ForecastRevision ForecastRevision { get; private set; } = null!;
    public decimal ForecastQty { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public ICollection<ForecastPlanEntryCorrection> Corrections { get; private set; } = [];
}

public sealed class ForecastPlanEntryCorrection
{
    private ForecastPlanEntryCorrection() { }

    public ForecastPlanEntryCorrection(Guid forecastPlanEntryId, Guid forecastRevisionId,
        int revisionNumber, decimal forecastQty, string reason, string? actorIdentifier)
    {
        Id = Guid.NewGuid();
        ForecastPlanEntryId = forecastPlanEntryId;
        ForecastRevisionId = forecastRevisionId;
        RevisionNumber = revisionNumber;
        ForecastQty = forecastQty;
        Reason = reason;
        ActorIdentifier = actorIdentifier;
    }

    public Guid Id { get; private set; }
    public Guid ForecastPlanEntryId { get; private set; }
    public ForecastPlanEntry ForecastPlanEntry { get; private set; } = null!;
    public Guid ForecastRevisionId { get; private set; }
    public ForecastRevision ForecastRevision { get; private set; } = null!;
    public int RevisionNumber { get; private set; }
    public decimal ForecastQty { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string? ActorIdentifier { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}

public sealed class BusinessPeriod
{
    private BusinessPeriod() { }

    public BusinessPeriod(int jalaliYear, int jalaliMonth, DateOnly startDate, DateOnly endDate)
    {
        Id = Guid.NewGuid();
        JalaliYear = jalaliYear;
        JalaliMonth = jalaliMonth;
        StartDate = startDate;
        EndDate = endDate;
        Status = BusinessPeriodStatuses.Open;
    }

    public Guid Id { get; private set; }
    public int JalaliYear { get; private set; }
    public int JalaliMonth { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public string Status { get; private set; } = BusinessPeriodStatuses.Open;
    public long Version { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public void Close()
    {
        Status = BusinessPeriodStatuses.Closed;
        Advance();
    }

    public void Reopen()
    {
        Status = BusinessPeriodStatuses.Open;
        Advance();
    }

    private void Advance()
    {
        Version++;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}

public sealed class ForecastSeries
{
    private ForecastSeries() { }

    public ForecastSeries(Guid productId, Guid businessPeriodId, Guid productBaseUnitId)
    {
        Id = Guid.NewGuid();
        ProductId = productId;
        BusinessPeriodId = businessPeriodId;
        ProductBaseUnitId = productBaseUnitId;
    }

    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }
    public Product Product { get; private set; } = null!;
    public Guid BusinessPeriodId { get; private set; }
    public BusinessPeriod BusinessPeriod { get; private set; } = null!;
    public Guid ProductBaseUnitId { get; private set; }
    public Unit ProductBaseUnit { get; private set; } = null!;
    public Guid? CurrentRevisionId { get; private set; }
    public ForecastRevision? CurrentRevision { get; private set; }
    public ICollection<ForecastRevision> Revisions { get; private set; } = [];
    public long Version { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public void SetCurrent(ForecastRevision revision)
    {
        CurrentRevisionId = revision.Id;
        CurrentRevision = revision;
    }

    public void Advance(ForecastRevision revision)
    {
        SetCurrent(revision);
        Version++;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}

public sealed class ForecastRevision
{
    private ForecastRevision() { }

    public ForecastRevision(Guid forecastSeriesId, int revisionNumber, decimal forecastQty,
        string? reason, string? actorIdentifier)
    {
        Id = Guid.NewGuid();
        ForecastSeriesId = forecastSeriesId;
        RevisionNumber = revisionNumber;
        ForecastQty = forecastQty;
        Reason = reason;
        ActorIdentifier = actorIdentifier;
    }

    public Guid Id { get; private set; }
    public Guid ForecastSeriesId { get; private set; }
    public ForecastSeries ForecastSeries { get; private set; } = null!;
    public int RevisionNumber { get; private set; }
    public decimal ForecastQty { get; private set; }
    public string? Reason { get; private set; }
    public string? ActorIdentifier { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}

public sealed class DailyActual
{
    private DailyActual() { }

    public DailyActual(Guid productId, Guid businessPeriodId, DateOnly productionDate, Guid unitId)
    {
        Id = Guid.NewGuid();
        ProductId = productId;
        BusinessPeriodId = businessPeriodId;
        ProductionDate = productionDate;
        UnitId = unitId;
    }

    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }
    public Product Product { get; private set; } = null!;
    public Guid BusinessPeriodId { get; private set; }
    public BusinessPeriod BusinessPeriod { get; private set; } = null!;
    public DateOnly ProductionDate { get; private set; }
    public Guid UnitId { get; private set; }
    public Unit Unit { get; private set; } = null!;
    public Guid? CurrentRevisionId { get; private set; }
    public DailyActualRevision? CurrentRevision { get; private set; }
    public ICollection<DailyActualRevision> Revisions { get; private set; } = [];
    public long Version { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public void SetCurrent(DailyActualRevision revision)
    {
        CurrentRevisionId = revision.Id;
        CurrentRevision = revision;
    }

    public void Advance(DailyActualRevision revision)
    {
        SetCurrent(revision);
        Version++;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}

public sealed class DailyActualRevision
{
    private DailyActualRevision() { }

    public DailyActualRevision(Guid dailyActualId, int revisionNumber, decimal actualQty,
        string? reason, string? actorIdentifier)
    {
        Id = Guid.NewGuid();
        DailyActualId = dailyActualId;
        RevisionNumber = revisionNumber;
        ActualQty = actualQty;
        Reason = reason;
        ActorIdentifier = actorIdentifier;
    }

    public Guid Id { get; private set; }
    public Guid DailyActualId { get; private set; }
    public DailyActual DailyActual { get; private set; } = null!;
    public int RevisionNumber { get; private set; }
    public decimal ActualQty { get; private set; }
    public string? Reason { get; private set; }
    public string? ActorIdentifier { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}

public sealed class PeriodLifecycleEvent
{
    private PeriodLifecycleEvent() { }

    public PeriodLifecycleEvent(Guid businessPeriodId, string fromStatus, string toStatus,
        string? reason, string? actorIdentifier)
    {
        Id = Guid.NewGuid();
        BusinessPeriodId = businessPeriodId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Reason = reason;
        ActorIdentifier = actorIdentifier;
    }

    public Guid Id { get; private set; }
    public Guid BusinessPeriodId { get; private set; }
    public BusinessPeriod BusinessPeriod { get; private set; } = null!;
    public string FromStatus { get; private set; } = string.Empty;
    public string ToStatus { get; private set; } = string.Empty;
    public string? Reason { get; private set; }
    public string? ActorIdentifier { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
}
