using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Bom.Application;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Planning.Domain;
using Hadron.Erp.Api.Modules.Products.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Hadron.Erp.Api.Platform.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Hadron.Erp.Api.Modules.Planning.Application;

public sealed class PlanningService(
    IDbContextFactory<HadronDbContext> factory,
    IPlanningCalendar calendar,
    IBomOperationalReadinessQuery bomReadinessQuery,
    IAuditEventSink auditSink,
    IHttpContextAccessor accessor,
    ILogger<PlanningService> logger)
{
    public BusinessDateResponse GetBusinessDate()
    {
        var today = calendar.TehranToday;
        var period = calendar.GetJalaliPeriod(today);
        return new(today, period.JalaliYear, period.JalaliMonth);
    }

    public async Task<BusinessPeriodListResponse> ListPeriods(int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        var total = await db.BusinessPeriods.CountAsync(ct);
        var periods = await db.BusinessPeriods.AsNoTracking()
            .OrderByDescending(x => x.JalaliYear).ThenByDescending(x => x.JalaliMonth)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(periods.Select(Period).ToList(), page, pageSize, total);
    }

    public async Task<BusinessPeriodResponse> CreatePeriod(CreateBusinessPeriodRequest request, CancellationToken ct)
    {
        var bounds = calendar.GetPeriodBounds(request.JalaliYear, request.JalaliMonth);
        await using var db = await factory.CreateDbContextAsync(ct);
        var period = new BusinessPeriod(request.JalaliYear, request.JalaliMonth, bounds.StartDate, bounds.EndDate);
        db.BusinessPeriods.Add(period);
        await Save(db, ct);
        var response = Period(period);
        await Emit("BusinessPeriod", period.Id, "CREATE", null, Snapshot(response), null, ct);
        return response;
    }

    public async Task<BusinessPeriodResponse> ChangePeriodState(Guid id, bool close,
        ChangeBusinessPeriodStateRequest request, CancellationToken ct)
    {
        if (request.ExpectedVersion < 1) throw Validation("expectedVersion", "نسخه معتبر الزامی است.");
        var reason = Clean(request.Reason);
        if (!close && reason is null) throw Validation("reason", "دلیل بازگشایی الزامی است.");

        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockPeriod(db, id, false, ct);
        var period = await db.BusinessPeriods.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException();
        EnsureVersion(period.Version, request.ExpectedVersion);
        var expected = close ? BusinessPeriodStatuses.Open : BusinessPeriodStatuses.Closed;
        if (period.Status != expected)
        {
            throw new PlanningConflictException("PERIOD_STATE_CHANGED", "وضعیت دوره با عملیات درخواستی سازگار نیست.");
        }

        var before = Period(period);
        var from = period.Status;
        if (close) period.Close(); else period.Reopen();
        db.PeriodLifecycleEvents.Add(new(period.Id, from, period.Status, reason, Actor()));
        await Save(db, ct);
        await tx.CommitAsync(ct);
        var response = Period(period);
        await Emit("BusinessPeriod", period.Id, close ? "CLOSE" : "REOPEN", Snapshot(before),
            Snapshot(response), reason, ct);
        return response;
    }

    public async Task<ForecastMutationResponse> CreateForecast(CreateForecastRequest request, CancellationToken ct)
    {
        ValidateQty(request.ForecastQty, "forecastQty");
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockPeriod(db, request.BusinessPeriodId, true, ct);
        var period = await OpenPeriod(db, request.BusinessPeriodId, ct);
        var product = await ProductForNewAssignment(db, request.ProductId, ct);

        if (await db.ForecastSeries.AnyAsync(x => x.ProductId == request.ProductId &&
            x.BusinessPeriodId == request.BusinessPeriodId, ct))
        {
            throw new PlanningConflictException("FORECAST_SERIES_EXISTS", "برای محصول و دوره انتخاب‌شده Forecast ثبت شده است.");
        }

        await EnsureActualUnitCompatibility(db, product.ItemId, period.Id, product.Item.BaseUnitId, ct);

        var series = new ForecastSeries(product.ItemId, period.Id, product.Item.BaseUnitId);
        db.ForecastSeries.Add(series);
        await Save(db, ct);
        var revision = new ForecastRevision(series.Id, 1, request.ForecastQty, Clean(request.Reason), Actor());
        db.ForecastRevisions.Add(revision);
        await Save(db, ct);
        series.SetCurrent(revision);
        await Save(db, ct);
        await tx.CommitAsync(ct);

        var response = await LoadForecastMutation(db, series.Id, ct);
        await Emit("ForecastSeries", series.Id, "CREATE", null, Snapshot(response), request.Reason, ct);
        return response;
    }

    public async Task<ForecastMutationResponse> AddForecastRevision(Guid seriesId,
        CreateForecastRevisionRequest request, CancellationToken ct)
    {
        ValidateQty(request.ForecastQty, "forecastQty");
        if (request.ExpectedVersion < 1) throw Validation("expectedVersion", "نسخه معتبر الزامی است.");
        var reason = Clean(request.Reason);
        if (reason is null) throw Validation("reason", "دلیل Revision دوم و بعد از آن الزامی است.");

        await using var db = await factory.CreateDbContextAsync(ct);
        var identity = await db.ForecastSeries.AsNoTracking().Where(x => x.Id == seriesId)
            .Select(x => new { x.BusinessPeriodId }).SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockPeriod(db, identity.BusinessPeriodId, true, ct);
        await LockRow(db, "planning.forecast_series", seriesId, ct);
        await OpenPeriod(db, identity.BusinessPeriodId, ct);
        var series = await db.ForecastSeries.Include(x => x.Product).ThenInclude(x => x.Item)
            .Include(x => x.Revisions).SingleAsync(x => x.Id == seriesId, ct);
        EnsureVersion(series.Version, request.ExpectedVersion);
        if (!series.Product.Item.IsActive)
            throw new PlanningConflictException("INACTIVE_PRODUCT", "برای محصول غیرفعال Revision جدید ثبت نمی‌شود.");
        EnsureBaseUnit(series.Product.Item.BaseUnitId, series.ProductBaseUnitId);

        var before = await LoadForecastMutation(db, series.Id, ct);
        var next = series.Revisions.Max(x => x.RevisionNumber) + 1;
        var revision = new ForecastRevision(series.Id, next, request.ForecastQty, reason, Actor());
        db.ForecastRevisions.Add(revision);
        series.Advance(revision);
        await Save(db, ct);
        await tx.CommitAsync(ct);
        var response = await LoadForecastMutation(db, series.Id, ct);
        await Emit("ForecastSeries", series.Id, "REVISE", Snapshot(before), Snapshot(response), reason, ct);
        return response;
    }

    public async Task<ForecastHistoryResponse?> GetForecastHistory(Guid seriesId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var series = await db.ForecastSeries.AsNoTracking().Include(x => x.Product).ThenInclude(x => x.Item)
            .ThenInclude(x => x.BaseUnit).Include(x => x.Product).ThenInclude(x => x.ProductCategory)
            .Include(x => x.Product).ThenInclude(x => x.BusinessUnit).Include(x => x.BusinessPeriod)
            .Include(x => x.ProductBaseUnit).Include(x => x.Revisions)
            .SingleOrDefaultAsync(x => x.Id == seriesId, ct);
        if (series is null || !series.CurrentRevisionId.HasValue) return null;
        return new(series.Id, Product(series.Product), Period(series.BusinessPeriod), Unit(series.ProductBaseUnit),
            series.CurrentRevisionId.Value, series.Version,
            series.Revisions.OrderByDescending(x => x.RevisionNumber).Select(Revision).ToList());
    }

    public async Task<DailyActualResponse> CreateActual(CreateDailyActualRequest request, CancellationToken ct)
    {
        ValidateQty(request.ActualQty, "actualQty");
        if (request.ProductionDate > calendar.TehranToday)
            throw new PlanningConflictException("FUTURE_PRODUCTION_ACTUAL_NOT_ALLOWED",
                "ثبت تولید واقعی برای تاریخ آینده مجاز نیست.");

        await using var db = await factory.CreateDbContextAsync(ct);
        var periodId = await db.BusinessPeriods.AsNoTracking().Where(x => x.StartDate <= request.ProductionDate &&
            x.EndDate >= request.ProductionDate).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct)
            ?? throw Validation("productionDate", "Business Period متناظر با تاریخ تولید ثبت نشده است.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockPeriod(db, periodId, true, ct);
        await OpenPeriod(db, periodId, ct);
        var product = await ProductForNewAssignment(db, request.ProductId, ct);
        await EnsureActualUnitCompatibility(db, product.ItemId, periodId, product.Item.BaseUnitId, ct);
        if (await db.DailyActuals.AnyAsync(x => x.ProductId == request.ProductId &&
            x.ProductionDate == request.ProductionDate, ct))
            throw new PlanningConflictException("DAILY_ACTUAL_EXISTS", "برای محصول و تاریخ انتخاب‌شده Actual ثبت شده است.");

        var actual = new DailyActual(product.ItemId, periodId, request.ProductionDate, product.Item.BaseUnitId);
        db.DailyActuals.Add(actual);
        await Save(db, ct);
        var revision = new DailyActualRevision(actual.Id, 1, request.ActualQty, null, Actor());
        db.DailyActualRevisions.Add(revision);
        await Save(db, ct);
        actual.SetCurrent(revision);
        await Save(db, ct);
        await tx.CommitAsync(ct);
        var response = (await LoadActual(db, actual.Id, ct))!;
        await Emit("DailyActual", actual.Id, "CREATE", null, Snapshot(response), null, ct);
        return response;
    }

    public async Task<DailyActualResponse> CorrectActual(Guid actualId, CorrectDailyActualRequest request,
        CancellationToken ct)
    {
        ValidateQty(request.ActualQty, "actualQty");
        if (request.ExpectedVersion < 1) throw Validation("expectedVersion", "نسخه معتبر الزامی است.");
        var reason = Clean(request.Reason) ?? throw Validation("reason", "دلیل اصلاح Actual الزامی است.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var identity = await db.DailyActuals.AsNoTracking().Where(x => x.Id == actualId)
            .Select(x => new { x.BusinessPeriodId }).SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockPeriod(db, identity.BusinessPeriodId, true, ct);
        await LockRow(db, "planning.daily_actuals", actualId, ct);
        await OpenPeriod(db, identity.BusinessPeriodId, ct);
        var actual = await db.DailyActuals.Include(x => x.Product).ThenInclude(x => x.Item)
            .Include(x => x.Revisions).SingleAsync(x => x.Id == actualId, ct);
        EnsureVersion(actual.Version, request.ExpectedVersion);
        if (actual.ProductionDate > calendar.TehranToday)
            throw new PlanningConflictException("FUTURE_PRODUCTION_ACTUAL_NOT_ALLOWED",
                "اصلاح تولید واقعی برای تاریخ آینده مجاز نیست.");
        EnsureBaseUnit(actual.Product.Item.BaseUnitId, actual.UnitId);
        await EnsureActualUnitCompatibility(db, actual.ProductId, actual.BusinessPeriodId, actual.UnitId, ct);

        var before = (await LoadActual(db, actual.Id, ct))!;
        var next = actual.Revisions.Max(x => x.RevisionNumber) + 1;
        var revision = new DailyActualRevision(actual.Id, next, request.ActualQty, reason, Actor());
        db.DailyActualRevisions.Add(revision);
        actual.Advance(revision);
        await Save(db, ct);
        await tx.CommitAsync(ct);
        var response = (await LoadActual(db, actual.Id, ct))!;
        await Emit("DailyActual", actual.Id, "CORRECT", Snapshot(before), Snapshot(response), reason, ct);
        return response;
    }

    public async Task<DailyActualListResponse> ListActuals(Guid? periodId, Guid? productId,
        DateOnly? from, DateOnly? to, string? search, int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        var q = db.DailyActuals.AsNoTracking().Where(x => x.CurrentRevisionId != null);
        if (periodId.HasValue) q = q.Where(x => x.BusinessPeriodId == periodId);
        if (productId.HasValue) q = q.Where(x => x.ProductId == productId);
        if (from.HasValue) q = q.Where(x => x.ProductionDate >= from);
        if (to.HasValue) q = q.Where(x => x.ProductionDate <= to);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(x => x.Product.Item.BusinessNumber.Contains(term) || x.Product.Item.NameFa.Contains(term) ||
                (x.Product.Item.NameEn != null && x.Product.Item.NameEn.Contains(term)));
        }
        var total = await q.CountAsync(ct);
        var records = await q.OrderByDescending(x => x.ProductionDate).ThenBy(x => x.Product.Item.BusinessNumber)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Include(x => x.Product).ThenInclude(x => x.Item).ThenInclude(x => x.BaseUnit)
            .Include(x => x.Product).ThenInclude(x => x.ProductCategory)
            .Include(x => x.Product).ThenInclude(x => x.BusinessUnit)
            .Include(x => x.BusinessPeriod).Include(x => x.Unit).Include(x => x.CurrentRevision)
            .ToListAsync(ct);
        var rows = records.Select(row => new DailyActualResponse(row.Id, Product(row.Product),
            Period(row.BusinessPeriod), row.ProductionDate, Unit(row.Unit), ActualRevision(row.CurrentRevision!),
            row.Version, row.CreatedAtUtc, row.UpdatedAtUtc)).ToList();
        return new(rows, page, pageSize, total);
    }

    public async Task<DailyActualHistoryResponse?> GetActualHistory(Guid actualId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var actual = await LoadActual(db, actualId, ct);
        if (actual is null) return null;
        var revisions = await db.DailyActualRevisions.AsNoTracking().Where(x => x.DailyActualId == actualId)
            .OrderByDescending(x => x.RevisionNumber).Select(x => new DailyActualRevisionResponse(x.Id,
                x.RevisionNumber, x.ActualQty, x.Reason, x.ActorIdentifier, x.CreatedAtUtc)).ToListAsync(ct);
        return new(actual, revisions);
    }

    public async Task<ForecastPlanningListResponse> GetPlanningRows(Guid periodId, DateOnly evaluationDate,
        string? search, bool includeInactive, Guid? categoryId, Guid? businessUnitId,
        int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        var period = await db.BusinessPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.Id == periodId, ct)
            ?? throw new KeyNotFoundException();
        var products = db.Products.AsNoTracking().Where(x => includeInactive || x.Item.IsActive ||
            db.ForecastSeries.Any(s => s.BusinessPeriodId == periodId && s.ProductId == x.ItemId) ||
            db.DailyActuals.Any(a => a.BusinessPeriodId == periodId && a.ProductId == x.ItemId));
        if (categoryId.HasValue) products = products.Where(x => x.ProductCategoryId == categoryId);
        if (businessUnitId.HasValue) products = products.Where(x => x.BusinessUnitId == businessUnitId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            products = products.Where(x => x.Item.BusinessNumber.Contains(term) || x.Item.NameFa.Contains(term) ||
                (x.Item.NameEn != null && x.Item.NameEn.Contains(term)));
        }
        var total = await products.CountAsync(ct);
        var productRows = await products.OrderBy(x => x.Item.BusinessNumber).Skip((page - 1) * pageSize)
            .Take(pageSize).Include(x => x.Item).ThenInclude(x => x.BaseUnit)
            .Include(x => x.ProductCategory).Include(x => x.BusinessUnit).ToListAsync(ct);
        var productIds = productRows.Select(x => x.ItemId).ToArray();
        var series = await db.ForecastSeries.AsNoTracking().Include(x => x.CurrentRevision)
            .Where(x => x.BusinessPeriodId == periodId && productIds.Contains(x.ProductId))
            .ToDictionaryAsync(x => x.ProductId, ct);
        var actuals = await db.DailyActuals.AsNoTracking().Include(x => x.CurrentRevision)
            .Where(x => x.BusinessPeriodId == periodId && productIds.Contains(x.ProductId))
            .ToListAsync(ct);
        var position = evaluationDate < period.StartDate ? "FUTURE" :
            evaluationDate > period.EndDate ? "PAST" : "CURRENT";
        var readiness = await EvaluateBomReadiness(productRows, period, evaluationDate, position, ct);
        var result = new List<ForecastPlanningRow>();
        foreach (var product in productRows)
        {
            series.TryGetValue(product.ItemId, out var forecast);
            var productActuals = actuals.Where(x => x.ProductId == product.ItemId).ToList();
            var monthly = productActuals.Sum(x => x.CurrentRevision!.ActualQty);
            var mtd = productActuals.Where(x => x.ProductionDate <= evaluationDate)
                .Sum(x => x.CurrentRevision!.ActualQty);
            var qty = forecast?.CurrentRevision?.ForecastQty;
            decimal? remaining = position == "CURRENT" && qty.HasValue ? Math.Max(qty.Value - mtd, 0) : null;
            decimal? planningQty = position switch
            {
                "PAST" => monthly,
                "CURRENT" => remaining,
                _ => qty
            };
            var eligibleStart = position == "CURRENT" ? evaluationDate.AddDays(1) : (DateOnly?)null;
            var exception = position == "CURRENT" && remaining > 0 && eligibleStart > period.EndDate
                ? "CURRENT_PERIOD_REMAINDER_UNALLOCATABLE" : null;
            var state = qty.HasValue ? qty.Value == 0 ? "EXPLICIT_ZERO" : "OFFICIAL" : "MISSING";
            result.Add(new(Product(product), forecast?.Id, forecast?.CurrentRevisionId,
                forecast?.CurrentRevision?.RevisionNumber, qty, forecast?.Version, monthly, mtd, remaining,
                planningQty, position, state, eligibleStart, exception, readiness[product.ItemId]));
        }
        return new(Period(period), evaluationDate, result, page, pageSize, total);
    }

    private async Task<Dictionary<Guid, BomReadinessResponse>> EvaluateBomReadiness(
        IReadOnlyList<Product> products, BusinessPeriod period, DateOnly evaluationDate, string position,
        CancellationToken ct)
    {
        var result = new Dictionary<Guid, BomReadinessResponse>();
        if (position == "PAST" || (position == "CURRENT" && evaluationDate >= period.EndDate))
        {
            foreach (var product in products)
                result[product.ItemId] = new("NOT_APPLICABLE", [], null, null);
            return result;
        }
        var from = position == "FUTURE" ? period.StartDate : evaluationDate.AddDays(1);
        var to = period.EndDate;
        var ids = products.Select(x => x.ItemId).ToArray();
        var coverage = await bomReadinessQuery.Evaluate(ids, from, to, ct);
        foreach (var product in products)
        {
            if (!product.Item.IsActive)
            {
                result[product.ItemId] = new("BLOCKED", ["PARENT_INACTIVE"], from, to);
                continue;
            }
            var operational = coverage[product.ItemId];
            if (operational.BlockingCodes.Count > 0)
            {
                result[product.ItemId] = new("BLOCKED", operational.BlockingCodes, from, to);
                continue;
            }
            var relevant = operational.Intervals.OrderBy(x => x.StartDate).ToList();
            var cursor = from;
            foreach (var interval in relevant)
            {
                var start = interval.StartDate < from ? from : interval.StartDate;
                var end = interval.EndDate > to ? to : interval.EndDate;
                if (start > cursor) break;
                if (end >= cursor) cursor = end == DateOnly.MaxValue ? end : end.AddDays(1);
                if (cursor > to) break;
            }
            result[product.ItemId] = cursor > to
                ? new("READY", [], from, to)
                : new(relevant.Count == 0 ? "MISSING_BOM" : "PARTIAL_PERIOD_BOM_COVERAGE",
                    [relevant.Count == 0 ? "MISSING_BOM" : "PARTIAL_PERIOD_BOM_COVERAGE"], from, to);
        }
        return result;
    }

    private static async Task<Product> ProductForNewAssignment(HadronDbContext db, Guid productId, CancellationToken ct)
    {
        var product = await db.Products.Include(x => x.Item).ThenInclude(x => x.BaseUnit)
            .Include(x => x.ProductCategory).Include(x => x.BusinessUnit)
            .SingleOrDefaultAsync(x => x.ItemId == productId, ct) ?? throw new KeyNotFoundException();
        var errors = new Dictionary<string, string[]>();
        if (!product.Item.IsActive) errors["productId"] = ["محصول باید فعال باشد."];
        if (!product.Item.BaseUnit.IsActive) errors["baseUnitId"] = ["واحد پایه محصول باید فعال باشد."];
        if (!product.ProductCategory.IsActive) errors["productCategoryId"] = ["گروه محصول باید فعال باشد."];
        if (product.BusinessUnit is { IsActive: false }) errors["businessUnitId"] = ["واحد سازمانی باید فعال باشد."];
        if (errors.Count > 0) throw new PlanningValidationException(errors);
        return product;
    }

    private static async Task EnsureActualUnitCompatibility(HadronDbContext db, Guid productId,
        Guid periodId, Guid unitId, CancellationToken ct)
    {
        var forecastUnit = await db.ForecastSeries.AsNoTracking().Where(x => x.ProductId == productId &&
            x.BusinessPeriodId == periodId).Select(x => (Guid?)x.ProductBaseUnitId).SingleOrDefaultAsync(ct);
        if (forecastUnit.HasValue && forecastUnit.Value != unitId) BaseUnitChanged();
        if (await db.DailyActuals.AnyAsync(x => x.ProductId == productId && x.BusinessPeriodId == periodId &&
            x.UnitId != unitId, ct)) BaseUnitChanged();
    }

    private static void EnsureBaseUnit(Guid current, Guid snapshot)
    {
        if (current != snapshot) BaseUnitChanged();
    }

    private static void BaseUnitChanged() => throw new PlanningConflictException("BASE_UNIT_CHANGED",
        "واحد پایه با snapshot ثبت‌شده یکسان نیست و تبدیل خودکار مجاز نیست.");

    private static async Task<BusinessPeriod> OpenPeriod(HadronDbContext db, Guid periodId, CancellationToken ct)
    {
        var period = await db.BusinessPeriods.SingleOrDefaultAsync(x => x.Id == periodId, ct)
            ?? throw new KeyNotFoundException();
        if (period.Status != BusinessPeriodStatuses.Open)
            throw new PlanningConflictException("PERIOD_CLOSED", "Business Period بسته است.");
        return period;
    }

    private static async Task LockPeriod(HadronDbContext db, Guid id, bool shared, CancellationToken ct) =>
        await Lock(db, $"SELECT \"Id\" FROM planning.business_periods WHERE \"Id\" = @id FOR {(shared ? "SHARE" : "UPDATE")}", id, ct);

    private static async Task LockRow(HadronDbContext db, string table, Guid id, CancellationToken ct) =>
        await Lock(db, $"SELECT \"Id\" FROM {table} WHERE \"Id\" = @id FOR UPDATE", id, ct);

    private static async Task Lock(HadronDbContext db, string sql, Guid id, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(ct);
        var transaction = (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction();
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task<ForecastMutationResponse> LoadForecastMutation(HadronDbContext db, Guid id, CancellationToken ct)
    {
        var row = await db.ForecastSeries.AsNoTracking().Include(x => x.ProductBaseUnit)
            .Include(x => x.CurrentRevision).SingleAsync(x => x.Id == id, ct);
        return new(row.Id, row.ProductId, row.BusinessPeriodId, Unit(row.ProductBaseUnit),
            Revision(row.CurrentRevision!), row.Version);
    }

    private static async Task<DailyActualResponse?> LoadActual(HadronDbContext db, Guid id, CancellationToken ct)
    {
        var row = await db.DailyActuals.AsNoTracking().Include(x => x.Product).ThenInclude(x => x.Item)
            .ThenInclude(x => x.BaseUnit).Include(x => x.Product).ThenInclude(x => x.ProductCategory)
            .Include(x => x.Product).ThenInclude(x => x.BusinessUnit).Include(x => x.BusinessPeriod)
            .Include(x => x.Unit).Include(x => x.CurrentRevision).SingleOrDefaultAsync(x => x.Id == id, ct);
        return row is null ? null : new(row.Id, Product(row.Product), Period(row.BusinessPeriod), row.ProductionDate,
            Unit(row.Unit), ActualRevision(row.CurrentRevision!), row.Version, row.CreatedAtUtc, row.UpdatedAtUtc);
    }

    private static BusinessPeriodResponse Period(BusinessPeriod x) => new(x.Id, x.JalaliYear, x.JalaliMonth,
        x.StartDate, x.EndDate, x.Status, x.Version, x.CreatedAtUtc, x.UpdatedAtUtc);
    private static PlanningUnitRef Unit(Unit x) => new(x.Id, x.Code, x.NameFa, x.NameEn, x.Symbol, x.IsActive);
    private static PlanningReferenceRef Reference(ReferenceValue x) => new(x.Id, x.Code, x.NameFa, x.NameEn, x.IsActive);
    private static PlanningProductRef Product(Product x) => new(x.ItemId, x.Item.BusinessNumber, x.Item.NameFa,
        x.Item.NameEn, x.Item.IsActive, Reference(x.ProductCategory),
        x.BusinessUnit is null ? null : Reference(x.BusinessUnit), Unit(x.Item.BaseUnit));
    private static ForecastRevisionResponse Revision(ForecastRevision x) => new(x.Id, x.RevisionNumber,
        x.ForecastQty, x.Reason, x.ActorIdentifier, x.CreatedAtUtc);
    private static DailyActualRevisionResponse ActualRevision(DailyActualRevision x) => new(x.Id,
        x.RevisionNumber, x.ActualQty, x.Reason, x.ActorIdentifier, x.CreatedAtUtc);

    private string? Actor() => AuditEventContext.ActorId(accessor.HttpContext);

    private async Task Emit(string entityType, Guid id, string action,
        IReadOnlyDictionary<string, object?>? oldValues, IReadOnlyDictionary<string, object?>? newValues,
        string? reason, CancellationToken ct)
    {
        try
        {
            await auditSink.WriteAsync(new(entityType, id, action, DateTime.UtcNow, Actor(),
                AuditEventContext.CorrelationId(accessor.HttpContext), oldValues, newValues, Clean(reason)), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "Post-commit planning audit emission failed for {EntityType} {EntityId} {Action}.",
                entityType, id, action);
        }
    }

    private static Dictionary<string, object?> Snapshot(object value) => value switch
    {
        BusinessPeriodResponse x => new() { ["Status"] = x.Status, ["Version"] = x.Version },
        ForecastMutationResponse x => new()
        {
            ["CurrentRevisionId"] = x.CurrentRevision.Id,
            ["ForecastQty"] = x.CurrentRevision.ForecastQty,
            ["Version"] = x.Version
        },
        DailyActualResponse x => new()
        {
            ["CurrentRevisionId"] = x.CurrentRevision.Id,
            ["ActualQty"] = x.CurrentRevision.ActualQty,
            ["Version"] = x.Version
        },
        _ => []
    };

    private static void ValidateQty(decimal qty, string key)
    {
        if (qty < 0) throw Validation(key, "مقدار نمی‌تواند منفی باشد.");
        if (decimal.Round(qty, 6) != qty) throw Validation(key, "حداکثر ۶ رقم اعشار مجاز است.");
    }

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            throw Validation("page", "صفحه باید مثبت و اندازه صفحه بین ۱ تا ۱۰۰ باشد.");
    }

    private static void EnsureVersion(long actual, long expected)
    {
        if (actual != expected) throw new PlanningConflictException("STALE_VERSION", "نسخه ارسالی قدیمی است.");
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static PlanningValidationException Validation(string key, string message) =>
        PlanningValidationException.Single(key, message);

    private static async Task Save(HadronDbContext db, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        { throw new PlanningConflictException("STALE_VERSION", "نسخه ارسالی قدیمی است."); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException postgres)
        {
            var code = postgres.ConstraintName switch
            {
                "IX_business_periods_JalaliYear_JalaliMonth" => "BUSINESS_PERIOD_EXISTS",
                "IX_forecast_series_ProductId_BusinessPeriodId" => "FORECAST_SERIES_EXISTS",
                "IX_daily_actuals_ProductId_ProductionDate" => "DAILY_ACTUAL_EXISTS",
                _ => "DATABASE_INTEGRITY_CONFLICT"
            };
            throw new PlanningConflictException(code, "عملیات با یک قاعده یکپارچگی پایگاه داده تعارض دارد.");
        }
    }
}
