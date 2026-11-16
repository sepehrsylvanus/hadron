using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Bom.Application;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Planning.Domain;
using Hadron.Erp.Api.Modules.Products.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Hadron.Erp.Api.Platform.Audit;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

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
        var jalali = calendar.GetJalaliDate(today);
        return new(today, jalali.JalaliYear, jalali.JalaliMonth, jalali.JalaliDay,
            $"{jalali.JalaliYear:D4}{jalali.JalaliMonth:D2}{jalali.JalaliDay:D2}");
    }

    public ResolveJalaliDateResponse ResolveJalaliDate(ResolveJalaliDateRequest request)
    {
        var normalized = NormalizeDigits(request.JalaliDate).Trim();
        if (normalized.Length != 8 || normalized.Any(x => !char.IsAsciiDigit(x)) ||
            !int.TryParse(normalized.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year) ||
            !int.TryParse(normalized.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var month) ||
            !int.TryParse(normalized.AsSpan(6, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var day))
            throw Validation("jalaliDate", "تاریخ باید با قالب YYYYMMDD وارد شود؛ مانند 14050608.");

        return new(normalized, calendar.GetGregorianDate(year, month, day));
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

    public async Task<ForecastEntryGridResponse> GetForecastEntryGrid(string periodCode, string? search,
        bool includeInactive, Guid? categoryId, Guid? businessUnitId, int page, int pageSize,
        CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        var identity = ParsePeriodCode(periodCode);
        var bounds = calendar.GetPeriodBounds(identity.Year, identity.Month);
        await using var db = await factory.CreateDbContextAsync(ct);
        var period = await db.BusinessPeriods.AsNoTracking().SingleOrDefaultAsync(
            x => x.JalaliYear == identity.Year && x.JalaliMonth == identity.Month, ct);

        var products = db.Products.AsNoTracking().Where(x => includeInactive || x.Item.IsActive ||
            (period != null && db.ForecastSeries.Any(s => s.BusinessPeriodId == period.Id && s.ProductId == x.ItemId)));
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
        var series = period is null || productIds.Length == 0
            ? new Dictionary<Guid, ForecastSeries>()
            : await db.ForecastSeries.AsNoTracking().Include(x => x.CurrentRevision)
                .Where(x => x.BusinessPeriodId == period.Id && productIds.Contains(x.ProductId))
                .ToDictionaryAsync(x => x.ProductId, ct);
        var rows = productRows.Select(product =>
        {
            series.TryGetValue(product.ItemId, out var current);
            return new ForecastEntryRow(Product(product), current?.Id,
                current?.CurrentRevision?.RevisionNumber, current?.CurrentRevision?.ForecastQty, current?.Version);
        }).ToList();
        var periodResponse = new ForecastEntryPeriodResponse(identity.Code, identity.Year, identity.Month,
            bounds.StartDate, bounds.EndDate, period?.Id, period?.Status ?? "NOT_CREATED", period?.Version,
            period is not null);
        return new(periodResponse, rows, page, pageSize, total);
    }

    public async Task<SaveForecastBatchResponse> SaveForecastBatch(SaveForecastBatchRequest request,
        CancellationToken ct)
    {
        var identity = ParsePeriodCode(request.PeriodCode);
        if (request.Entries is null || request.Entries.Count is < 1 or > 500)
            throw Validation("entries", "حداقل یک و حداکثر ۵۰۰ ردیف تغییر در هر ذخیره مجاز است.");
        if (request.Entries.Select(x => x.ProductId).Distinct().Count() != request.Entries.Count)
            throw Validation("entries", "هر محصول فقط یک‌بار می‌تواند در درخواست ذخیره وجود داشته باشد.");
        foreach (var entry in request.Entries) ValidateQty(entry.ForecastQty, "forecastQty");
        var reason = Clean(request.Reason);
        var bounds = calendar.GetPeriodBounds(identity.Year, identity.Month);

        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockPeriodIdentity(db, identity.Year * 100 + identity.Month, ct);
        var periodId = await db.BusinessPeriods.AsNoTracking()
            .Where(x => x.JalaliYear == identity.Year && x.JalaliMonth == identity.Month)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        BusinessPeriod period;
        var createdPeriod = false;
        if (periodId.HasValue)
        {
            await LockPeriod(db, periodId.Value, false, ct);
            period = await OpenPeriod(db, periodId.Value, ct);
        }
        else
        {
            if (await db.BusinessPeriods.AnyAsync(x => x.StartDate <= bounds.EndDate && x.EndDate >= bounds.StartDate, ct))
                throw new PlanningConflictException("BUSINESS_PERIOD_DATE_OVERLAP",
                    "بازه زمانی دوره با یک Business Period موجود هم‌پوشانی دارد.");
            period = new BusinessPeriod(identity.Year, identity.Month, bounds.StartDate, bounds.EndDate);
            db.BusinessPeriods.Add(period);
            await Save(db, ct);
            createdPeriod = true;
        }

        var productIds = request.Entries.Select(x => x.ProductId).OrderBy(x => x).ToArray();
        var products = await db.Products.Include(x => x.Item).ThenInclude(x => x.BaseUnit)
            .Include(x => x.ProductCategory).Include(x => x.BusinessUnit)
            .Where(x => productIds.Contains(x.ItemId)).ToListAsync(ct);
        if (products.Count != productIds.Length) throw new KeyNotFoundException();
        foreach (var product in products) ValidateForecastProduct(product);

        await LockForecastSeriesRows(db, period.Id, productIds, ct);
        var existingSeries = await db.ForecastSeries.Include(x => x.CurrentRevision).Include(x => x.Revisions)
            .Include(x => x.ProductBaseUnit).Where(x => x.BusinessPeriodId == period.Id &&
                productIds.Contains(x.ProductId)).ToDictionaryAsync(x => x.ProductId, ct);
        var actualUnits = await db.DailyActuals.AsNoTracking().Where(x => x.BusinessPeriodId == period.Id &&
            productIds.Contains(x.ProductId)).Select(x => new { x.ProductId, x.UnitId }).ToListAsync(ct);
        var productMap = products.ToDictionary(x => x.ItemId);
        var newSeries = new List<ForecastSeries>();
        var changes = new List<(ForecastSeries Series, ForecastRevision Revision, bool Created,
            IReadOnlyDictionary<string, object?>? Before)>();
        var unchanged = 0;

        foreach (var entry in request.Entries.OrderBy(x => x.ProductId))
        {
            var product = productMap[entry.ProductId];
            if (actualUnits.Any(x => x.ProductId == product.ItemId && x.UnitId != product.Item.BaseUnitId))
                BaseUnitChanged();
            if (existingSeries.TryGetValue(product.ItemId, out var series))
            {
                if (!entry.ExpectedVersion.HasValue)
                    throw Validation("expectedVersion", "نسخه فعلی Forecast برای ویرایش الزامی است.");
                EnsureVersion(series.Version, entry.ExpectedVersion.Value);
                EnsureBaseUnit(product.Item.BaseUnitId, series.ProductBaseUnitId);
                if (series.CurrentRevision!.ForecastQty == entry.ForecastQty)
                {
                    unchanged++;
                    continue;
                }
                if (reason is null)
                    throw Validation("reason", "برای تغییر مقادیر ثبت‌شده، دلیل ویرایش دسته‌ای الزامی است.");
                var before = new Dictionary<string, object?>
                {
                    ["CurrentRevisionId"] = series.CurrentRevisionId,
                    ["ForecastQty"] = series.CurrentRevision.ForecastQty,
                    ["Version"] = series.Version
                };
                var revision = new ForecastRevision(series.Id, series.Revisions.Max(x => x.RevisionNumber) + 1,
                    entry.ForecastQty, reason, Actor());
                db.ForecastRevisions.Add(revision);
                changes.Add((series, revision, false, before));
            }
            else
            {
                if (entry.ExpectedVersion.HasValue)
                    throw new PlanningConflictException("FORECAST_STATE_CHANGED",
                        "وضعیت Forecast محصول پس از نمایش جدول تغییر کرده است.");
                var newSeriesRow = new ForecastSeries(product.ItemId, period.Id, product.Item.BaseUnitId);
                db.ForecastSeries.Add(newSeriesRow);
                newSeries.Add(newSeriesRow);
                changes.Add((newSeriesRow, new ForecastRevision(newSeriesRow.Id, 1, entry.ForecastQty,
                    Clean(request.Reason), Actor()), true, null));
            }
        }

        if (newSeries.Count > 0) await Save(db, ct);
        foreach (var change in changes.Where(x => x.Created)) db.ForecastRevisions.Add(change.Revision);
        if (changes.Count > 0) await Save(db, ct);
        foreach (var change in changes)
        {
            if (change.Created) change.Series.SetCurrent(change.Revision);
            else change.Series.Advance(change.Revision);
        }
        if (changes.Count > 0) await Save(db, ct);
        await tx.CommitAsync(ct);

        if (createdPeriod)
            await Emit("BusinessPeriod", period.Id, "CREATE", null, Snapshot(Period(period)), null, ct);
        foreach (var change in changes)
        {
            var after = new Dictionary<string, object?>
            {
                ["CurrentRevisionId"] = change.Revision.Id,
                ["ForecastQty"] = change.Revision.ForecastQty,
                ["Version"] = change.Series.Version
            };
            await Emit("ForecastSeries", change.Series.Id, change.Created ? "CREATE" : "REVISE",
                change.Before, after, change.Created ? request.Reason : reason, ct);
        }
        return new(Period(period), changes.Count(x => x.Created), changes.Count(x => !x.Created), unchanged);
    }

    public async Task<ForecastProductOptionListResponse> ListActiveForecastProducts(string? search,
        int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        var products = db.Products.AsNoTracking().Where(x => x.Item.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            products = products.Where(x => x.Item.BusinessNumber.Contains(term) || x.Item.NameFa.Contains(term) ||
                (x.Item.NameEn != null && x.Item.NameEn.Contains(term)));
        }
        var total = await products.CountAsync(ct);
        var rows = await products.OrderBy(x => x.Item.BusinessNumber).Skip((page - 1) * pageSize)
            .Take(pageSize).Include(x => x.Item).ThenInclude(x => x.BaseUnit)
            .Include(x => x.ProductCategory).Include(x => x.BusinessUnit).ToListAsync(ct);
        return new(rows.Select(Product).ToList(), page, pageSize, total);
    }

    public async Task<ForecastPlanPreviewResponse> PreviewForecastPlan(ForecastPlanPreviewRequest request,
        CancellationToken ct)
    {
        var definition = BuildForecastPlanDefinition(request.Name, request.StartJalaliDate,
            request.EndJalaliDate, request.ProductIds);
        await using var db = await factory.CreateDbContextAsync(ct);
        var products = await LoadActiveForecastProducts(db, definition.ProductIds, ct);
        var periods = await db.BusinessPeriods.AsNoTracking()
            .Where(x => x.StartDate >= definition.StartDate && x.StartDate <= definition.EndDate)
            .ToListAsync(ct);
        var periodIds = periods.Select(x => x.Id).ToArray();
        var series = periodIds.Length == 0
            ? []
            : await db.ForecastSeries.AsNoTracking().Include(x => x.CurrentRevision)
                .Where(x => definition.ProductIds.Contains(x.ProductId) && periodIds.Contains(x.BusinessPeriodId))
                .ToListAsync(ct);
        var periodMap = periods.ToDictionary(x => x.JalaliYear * 100 + x.JalaliMonth);
        var seriesMap = series.ToDictionary(x => (x.ProductId, x.BusinessPeriodId));
        var months = definition.Months.Select(month =>
        {
            periodMap.TryGetValue(month.Year * 100 + month.Month, out var period);
            return new ForecastPlanMonthResponse(month.Code, month.Year, month.Month, month.StartDate,
                month.EndDate, period?.Id, period?.Status ?? "NOT_CREATED");
        }).ToList();
        var cells = new List<ForecastPlanCellResponse>(definition.Months.Count * products.Count);
        foreach (var month in definition.Months)
        {
            periodMap.TryGetValue(month.Year * 100 + month.Month, out var period);
            foreach (var product in products)
            {
                ForecastSeries? current = null;
                if (period is not null) seriesMap.TryGetValue((product.ItemId, period.Id), out current);
                cells.Add(new(product.ItemId, month.Code, current?.CurrentRevision?.ForecastQty,
                    current?.CurrentRevision?.RevisionNumber, current?.Version));
            }
        }
        return new(definition.Name, definition.StartCode, definition.EndCode, definition.StartDate,
            definition.EndDate, months, products.Select(Product).ToList(), cells);
    }

    public async Task<SaveForecastPlanResponse> SaveForecastPlan(SaveForecastPlanRequest request,
        CancellationToken ct)
    {
        if (request.Entries is null || request.Entries.Count is < 1 or > 10000)
            throw Validation("entries", "حداقل یک و حداکثر ۱۰۰۰۰ مقدار پیش‌بینی در هر برنامه مجاز است.");
        var productIds = request.Entries.Select(x => x.ProductId).Distinct().OrderBy(x => x).ToArray();
        var definition = BuildForecastPlanDefinition(request.Name, request.StartJalaliDate,
            request.EndJalaliDate, productIds);
        if (request.Entries.Select(x => (x.ProductId, x.PeriodCode)).Distinct().Count() != request.Entries.Count)
            throw Validation("entries", "هر ترکیب محصول و ماه فقط یک‌بار می‌تواند ثبت شود.");
        if (request.Entries.Count != productIds.Length * definition.Months.Count)
            throw Validation("entries", "مقدار پیش‌بینی تمام محصولات در تمام ماه‌های بازه باید تکمیل شود.");
        var monthCodes = definition.Months.Select(x => x.Code).ToHashSet(StringComparer.Ordinal);
        if (request.Entries.Any(x => !monthCodes.Contains(x.PeriodCode)))
            throw Validation("entries", "ماه خارج از بازه برنامه ارسال شده است.");
        var entryKeys = request.Entries.Select(x => (x.ProductId, x.PeriodCode)).ToHashSet();
        if (definition.Months.Any(month => definition.ProductIds.Any(productId =>
            !entryKeys.Contains((productId, month.Code)))))
            throw Validation("entries", "مقدار پیش‌بینی تمام محصولات در تمام ماه‌های بازه باید تکمیل شود.");
        foreach (var entry in request.Entries) ValidateQty(entry.ForecastQty, "forecastQty");
        var reason = Clean(request.Reason);

        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var month in definition.Months)
            await LockPeriodIdentity(db, month.Year * 100 + month.Month, ct);

        var existingPeriods = await db.BusinessPeriods
            .Where(x => x.StartDate >= definition.StartDate && x.StartDate <= definition.EndDate)
            .ToListAsync(ct);
        var periodMap = existingPeriods.ToDictionary(x => x.JalaliYear * 100 + x.JalaliMonth);
        var createdPeriods = new List<BusinessPeriod>();
        foreach (var month in definition.Months)
        {
            var key = month.Year * 100 + month.Month;
            if (periodMap.TryGetValue(key, out var existing))
            {
                await LockPeriod(db, existing.Id, false, ct);
                if (existing.Status != BusinessPeriodStatuses.Open)
                    throw new PlanningConflictException("PERIOD_CLOSED", $"دوره {month.Code} بسته است.");
                continue;
            }
            if (await db.BusinessPeriods.AnyAsync(x => x.StartDate <= month.EndDate && x.EndDate >= month.StartDate, ct))
                throw new PlanningConflictException("BUSINESS_PERIOD_DATE_OVERLAP",
                    $"بازه ماه {month.Code} با یک Business Period موجود هم‌پوشانی دارد.");
            var period = new BusinessPeriod(month.Year, month.Month, month.StartDate, month.EndDate);
            db.BusinessPeriods.Add(period);
            periodMap[key] = period;
            createdPeriods.Add(period);
        }
        if (createdPeriods.Count > 0) await Save(db, ct);

        var products = await LoadActiveForecastProducts(db, definition.ProductIds, ct);
        foreach (var month in definition.Months)
            await LockForecastSeriesRows(db, periodMap[month.Year * 100 + month.Month].Id,
                definition.ProductIds, ct);
        var periodIds = periodMap.Values.Select(x => x.Id).ToArray();
        var existingSeries = await db.ForecastSeries.Include(x => x.CurrentRevision).Include(x => x.Revisions)
            .Include(x => x.ProductBaseUnit).Where(x => periodIds.Contains(x.BusinessPeriodId) &&
                definition.ProductIds.Contains(x.ProductId)).ToListAsync(ct);
        var seriesMap = existingSeries.ToDictionary(x => (x.ProductId, x.BusinessPeriodId));
        var actualUnits = await db.DailyActuals.AsNoTracking().Where(x => periodIds.Contains(x.BusinessPeriodId) &&
            definition.ProductIds.Contains(x.ProductId)).Select(x => new { x.ProductId, x.BusinessPeriodId, x.UnitId })
            .ToListAsync(ct);
        var productMap = products.ToDictionary(x => x.ItemId);
        var entries = request.Entries.ToDictionary(x => (x.ProductId, x.PeriodCode));
        var conflicts = new List<ForecastValueConflict>();
        foreach (var month in definition.Months)
        {
            var period = periodMap[month.Year * 100 + month.Month];
            foreach (var productId in definition.ProductIds)
            {
                var entry = entries[(productId, month.Code)];
                if (seriesMap.TryGetValue((productId, period.Id), out var series) &&
                    series.CurrentRevision!.ForecastQty != entry.ForecastQty)
                {
                    var product = productMap[productId];
                    conflicts.Add(new(productId, product.Item.BusinessNumber, product.Item.NameFa,
                        product.Item.NameEn, month.Code, series.CurrentRevision.ForecastQty,
                        entry.ForecastQty, series.Version));
                }
            }
        }
        if (conflicts.Count > 0 && !request.ConfirmDifferences)
            throw new PlanningConflictException("FORECAST_VALUE_CONFLICT",
                "برخی مقادیر با پیش‌بینی ثبت‌شده قبلی متفاوت‌اند.", conflicts);
        if (conflicts.Count > 0 && reason is null)
            throw Validation("reason", "برای تأیید مقادیر متفاوت، دلیل اصلاح الزامی است.");

        var newSeries = new List<ForecastSeries>();
        var changes = new List<(ForecastSeries Series, ForecastRevision Revision, bool Created,
            IReadOnlyDictionary<string, object?>? Before)>();
        var resolvedRevisionIds = new Dictionary<(Guid ProductId, string PeriodCode), Guid>();
        var unchanged = 0;
        foreach (var month in definition.Months)
        {
            var period = periodMap[month.Year * 100 + month.Month];
            foreach (var productId in definition.ProductIds)
            {
                var entry = entries[(productId, month.Code)];
                var product = productMap[productId];
                if (actualUnits.Any(x => x.ProductId == productId && x.BusinessPeriodId == period.Id &&
                    x.UnitId != product.Item.BaseUnitId)) BaseUnitChanged();
                if (seriesMap.TryGetValue((productId, period.Id), out var series))
                {
                    EnsureBaseUnit(product.Item.BaseUnitId, series.ProductBaseUnitId);
                    if (series.CurrentRevision!.ForecastQty == entry.ForecastQty)
                    {
                        resolvedRevisionIds[(productId, month.Code)] = series.CurrentRevisionId!.Value;
                        unchanged++;
                        continue;
                    }
                    if (!entry.ExpectedVersion.HasValue)
                        throw Validation("expectedVersion", "نسخه فعلی Forecast برای اصلاح الزامی است.");
                    EnsureVersion(series.Version, entry.ExpectedVersion.Value);
                    var before = new Dictionary<string, object?>
                    {
                        ["CurrentRevisionId"] = series.CurrentRevisionId,
                        ["ForecastQty"] = series.CurrentRevision.ForecastQty,
                        ["Version"] = series.Version
                    };
                    var revision = new ForecastRevision(series.Id, series.Revisions.Max(x => x.RevisionNumber) + 1,
                        entry.ForecastQty, reason, Actor());
                    changes.Add((series, revision, false, before));
                    resolvedRevisionIds[(productId, month.Code)] = revision.Id;
                }
                else
                {
                    if (entry.ExpectedVersion.HasValue)
                        throw new PlanningConflictException("FORECAST_STATE_CHANGED",
                            $"وضعیت Forecast محصول {product.Item.BusinessNumber} در ماه {month.Code} تغییر کرده است.");
                    var newSeriesRow = new ForecastSeries(productId, period.Id, product.Item.BaseUnitId);
                    db.ForecastSeries.Add(newSeriesRow);
                    newSeries.Add(newSeriesRow);
                    var revision = new ForecastRevision(newSeriesRow.Id, 1, entry.ForecastQty, reason, Actor());
                    changes.Add((newSeriesRow, revision, true, null));
                    resolvedRevisionIds[(productId, month.Code)] = revision.Id;
                }
            }
        }

        if (newSeries.Count > 0) await Save(db, ct);
        foreach (var change in changes) db.ForecastRevisions.Add(change.Revision);
        if (changes.Count > 0) await Save(db, ct);
        foreach (var change in changes)
        {
            if (change.Created) change.Series.SetCurrent(change.Revision);
            else change.Series.Advance(change.Revision);
        }
        if (changes.Count > 0) await Save(db, ct);

        var plan = new ForecastPlan(definition.Name, definition.Start.Year, definition.Start.Month,
            definition.End.Year, definition.End.Month, definition.StartDate, definition.EndDate, Actor());
        db.ForecastPlans.Add(plan);
        foreach (var month in definition.Months)
        {
            var period = periodMap[month.Year * 100 + month.Month];
            foreach (var productId in definition.ProductIds)
            {
                var entry = entries[(productId, month.Code)];
                db.ForecastPlanEntries.Add(new(plan.Id, productId, period.Id,
                    resolvedRevisionIds[(productId, month.Code)], entry.ForecastQty));
            }
        }
        await Save(db, ct);
        await tx.CommitAsync(ct);

        foreach (var period in createdPeriods)
            await Emit("BusinessPeriod", period.Id, "CREATE", null, Snapshot(Period(period)), null, ct);
        foreach (var change in changes)
        {
            var after = new Dictionary<string, object?>
            {
                ["CurrentRevisionId"] = change.Revision.Id,
                ["ForecastQty"] = change.Revision.ForecastQty,
                ["Version"] = change.Series.Version,
                ["ForecastPlanId"] = plan.Id
            };
            await Emit("ForecastSeries", change.Series.Id, change.Created ? "CREATE" : "REVISE",
                change.Before, after, reason, ct);
        }
        await Emit("ForecastPlan", plan.Id, "CREATE", null, Snapshot(new
        {
            plan.Name,
            plan.StartDate,
            plan.EndDate,
            EntryCount = request.Entries.Count
        }), reason, ct);
        return new(plan.Id, plan.Name, plan.StartDate, plan.EndDate, changes.Count(x => x.Created),
            changes.Count(x => !x.Created), unchanged, request.Entries.Count);
    }

    public async Task<ForecastPlanListResponse> ListForecastPlans(string? search, int page,
        int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        var plans = db.ForecastPlans.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            plans = plans.Where(x => EF.Functions.ILike(x.Name, pattern) || x.Entries.Any(entry =>
                EF.Functions.ILike(entry.Product.Item.BusinessNumber, pattern) ||
                EF.Functions.ILike(entry.Product.Item.NameFa, pattern) ||
                (entry.Product.Item.NameEn != null && EF.Functions.ILike(entry.Product.Item.NameEn, pattern))));
        }

        var total = await plans.CountAsync(ct);
        var rows = await plans.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                Plan = x,
                ProductCount = x.Entries.Select(entry => entry.ProductId).Distinct().Count(),
                EntryCount = x.Entries.Count
            }).ToListAsync(ct);
        return new(rows.Select(x => PlanSummary(x.Plan, x.ProductCount, x.EntryCount)).ToList(),
            page, pageSize, total);
    }

    public async Task<ForecastPlanDetailResponse?> GetForecastPlan(Guid planId, int page,
        int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        var plan = await db.ForecastPlans.AsNoTracking().SingleOrDefaultAsync(x => x.Id == planId, ct);
        if (plan is null) return null;

        var productIds = db.ForecastPlanEntries.AsNoTracking().Where(x => x.ForecastPlanId == planId)
            .Select(x => x.ProductId).Distinct();
        var totalProducts = await productIds.CountAsync(ct);
        var selectedProducts = await db.Products.AsNoTracking().Where(x => productIds.Contains(x.ItemId))
            .OrderBy(x => x.Item.BusinessNumber).Skip((page - 1) * pageSize).Take(pageSize)
            .Include(x => x.Item).ThenInclude(x => x.BaseUnit).Include(x => x.ProductCategory)
            .Include(x => x.BusinessUnit).ToListAsync(ct);
        var selectedProductIds = selectedProducts.Select(x => x.ItemId).ToArray();

        var periodIds = db.ForecastPlanEntries.AsNoTracking().Where(x => x.ForecastPlanId == planId)
            .Select(x => x.BusinessPeriodId).Distinct();
        var periods = await db.BusinessPeriods.AsNoTracking().Where(x => periodIds.Contains(x.Id))
            .OrderBy(x => x.StartDate).ToListAsync(ct);
        var entries = selectedProductIds.Length == 0
            ? []
            : await db.ForecastPlanEntries.AsNoTracking().AsSplitQuery()
                .Where(x => x.ForecastPlanId == planId && selectedProductIds.Contains(x.ProductId))
                .Include(x => x.ForecastRevision).ThenInclude(x => x.ForecastSeries)
                    .ThenInclude(x => x.CurrentRevision)
                .Include(x => x.Corrections).ThenInclude(x => x.ForecastRevision)
                .ToListAsync(ct);

        var selectedPeriodIds = periods.Select(x => x.Id).ToArray();
        var actuals = selectedProductIds.Length == 0 || selectedPeriodIds.Length == 0
            ? []
            : await db.DailyActuals.AsNoTracking()
                .Where(x => selectedProductIds.Contains(x.ProductId) &&
                    selectedPeriodIds.Contains(x.BusinessPeriodId) && x.CurrentRevisionId != null)
                .GroupBy(x => new { x.ProductId, x.BusinessPeriodId })
                .Select(group => new
                {
                    group.Key.ProductId,
                    group.Key.BusinessPeriodId,
                    MonthlyActualQty = group.Sum(x => x.CurrentRevision!.ActualQty),
                    DailyActualEntryCount = group.Count()
                }).ToListAsync(ct);
        var actualMap = actuals.ToDictionary(x => (x.ProductId, x.BusinessPeriodId));

        var periodMap = periods.ToDictionary(x => x.Id);
        var cells = entries.Select(entry =>
        {
            var latest = entry.Corrections.OrderByDescending(x => x.RevisionNumber).FirstOrDefault();
            var effectiveRevision = latest?.ForecastRevision ?? entry.ForecastRevision;
            var series = entry.ForecastRevision.ForecastSeries;
            var planRevision = latest?.RevisionNumber ?? 1;
            var history = new List<ForecastPlanEntryHistoryResponse>
            {
                new(1, entry.ForecastQty, entry.ForecastRevision.Reason,
                    entry.ForecastRevision.ActorIdentifier, entry.CreatedAtUtc)
            };
            history.AddRange(entry.Corrections.OrderBy(x => x.RevisionNumber).Select(x =>
                new ForecastPlanEntryHistoryResponse(x.RevisionNumber, x.ForecastQty, x.Reason,
                    x.ActorIdentifier, x.CreatedAtUtc)));
            actualMap.TryGetValue((entry.ProductId, entry.BusinessPeriodId), out var actual);
            return new ForecastPlanDetailCellResponse(entry.Id, entry.ProductId,
                PeriodCode(periodMap[entry.BusinessPeriodId]), entry.ForecastQty,
                latest?.ForecastQty ?? entry.ForecastQty, planRevision, effectiveRevision.Id,
                effectiveRevision.RevisionNumber, series.Version, series.CurrentRevision!.ForecastQty,
                series.CurrentRevision.RevisionNumber, series.CurrentRevisionId == effectiveRevision.Id,
                actual?.MonthlyActualQty ?? 0m, actual?.DailyActualEntryCount ?? 0, history);
        }).OrderBy(x => x.PeriodCode).ThenBy(x => x.ProductId).ToList();

        var entryCount = await db.ForecastPlanEntries.CountAsync(x => x.ForecastPlanId == planId, ct);
        var months = periods.Select(x => new ForecastPlanMonthResponse(PeriodCode(x), x.JalaliYear,
            x.JalaliMonth, x.StartDate, x.EndDate, x.Id, x.Status)).ToList();
        return new(PlanSummary(plan, totalProducts, entryCount), months,
            selectedProducts.Select(Product).ToList(), cells, page, pageSize, totalProducts);
    }

    public async Task<CorrectForecastPlanEntryResponse> CorrectForecastPlanEntry(Guid planId,
        Guid entryId, CorrectForecastPlanEntryRequest request, CancellationToken ct)
    {
        ValidateQty(request.ForecastQty, "forecastQty");
        if (request.ExpectedSeriesVersion < 1)
            throw Validation("expectedSeriesVersion", "نسخه فعلی Forecast الزامی است.");
        if (request.ExpectedPlanRevisionNumber < 1)
            throw Validation("expectedPlanRevisionNumber", "نسخه فعلی مقدار برنامه الزامی است.");
        var reason = Clean(request.Reason);
        if (reason is null) throw Validation("reason", "دلیل اصلاح مقدار ماهانه الزامی است.");

        await using var db = await factory.CreateDbContextAsync(ct);
        var identity = await db.ForecastPlanEntries.AsNoTracking().Where(x => x.Id == entryId &&
                x.ForecastPlanId == planId)
            .Select(x => new { x.BusinessPeriodId, x.ProductId, x.ForecastRevision.ForecastSeriesId })
            .SingleOrDefaultAsync(ct) ?? throw new KeyNotFoundException();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockPeriod(db, identity.BusinessPeriodId, true, ct);
        await LockRow(db, "planning.forecast_plan_entries", entryId, ct);
        await LockRow(db, "planning.forecast_series", identity.ForecastSeriesId, ct);
        await OpenPeriod(db, identity.BusinessPeriodId, ct);

        var entry = await db.ForecastPlanEntries.Include(x => x.BusinessPeriod)
            .Include(x => x.ForecastRevision).Include(x => x.Corrections).ThenInclude(x => x.ForecastRevision)
            .SingleAsync(x => x.Id == entryId && x.ForecastPlanId == planId, ct);
        var product = await db.Products.Include(x => x.Item).ThenInclude(x => x.BaseUnit)
            .SingleAsync(x => x.ItemId == identity.ProductId, ct);
        var series = await db.ForecastSeries.Include(x => x.CurrentRevision).Include(x => x.Revisions)
            .SingleAsync(x => x.Id == identity.ForecastSeriesId, ct);
        if (!product.Item.IsActive)
            throw new PlanningConflictException("INACTIVE_PRODUCT", "برای محصول غیرفعال Revision جدید ثبت نمی‌شود.");
        EnsureBaseUnit(product.Item.BaseUnitId, series.ProductBaseUnitId);

        var latest = entry.Corrections.OrderByDescending(x => x.RevisionNumber).FirstOrDefault();
        var effectiveRevisionId = latest?.ForecastRevisionId ?? entry.ForecastRevisionId;
        var effectiveQty = latest?.ForecastQty ?? entry.ForecastQty;
        var planRevisionNumber = latest?.RevisionNumber ?? 1;
        if (planRevisionNumber != request.ExpectedPlanRevisionNumber)
            throw new PlanningConflictException("STALE_VERSION", "نسخه ارسال‌شده قدیمی است.");
        if (request.ForecastQty == effectiveQty)
            throw Validation("forecastQty", "مقدار جدید باید با مقدار فعلی برنامه متفاوت باشد.");

        if (series.CurrentRevisionId != effectiveRevisionId && !request.ConfirmDifference)
        {
            throw new PlanningConflictException("FORECAST_VALUE_CONFLICT",
                "Forecast رسمی پس از ثبت این برنامه تغییر کرده است.", new[]
                {
                    new ForecastPlanCorrectionConflict(product.ItemId, product.Item.BusinessNumber,
                        product.Item.NameFa, product.Item.NameEn, PeriodCode(entry.BusinessPeriod), effectiveQty,
                        series.CurrentRevision!.ForecastQty, request.ForecastQty, series.Version,
                        series.CurrentRevision.RevisionNumber)
                });
        }
        EnsureVersion(series.Version, request.ExpectedSeriesVersion);

        var beforeSeries = new Dictionary<string, object?>
        {
            ["CurrentRevisionId"] = series.CurrentRevisionId,
            ["ForecastQty"] = series.CurrentRevision!.ForecastQty,
            ["Version"] = series.Version
        };
        var selectedRevision = series.CurrentRevision;
        var createdForecastRevision = selectedRevision.ForecastQty != request.ForecastQty;
        if (createdForecastRevision)
        {
            selectedRevision = new ForecastRevision(series.Id,
                series.Revisions.Max(x => x.RevisionNumber) + 1, request.ForecastQty, reason, Actor());
            db.ForecastRevisions.Add(selectedRevision);
            await Save(db, ct);
            series.Advance(selectedRevision);
            await Save(db, ct);
        }

        var correction = new ForecastPlanEntryCorrection(entry.Id, selectedRevision.Id,
            planRevisionNumber + 1, request.ForecastQty, reason, Actor());
        db.ForecastPlanEntryCorrections.Add(correction);
        await Save(db, ct);
        await tx.CommitAsync(ct);

        if (createdForecastRevision)
        {
            await Emit("ForecastSeries", series.Id, "REVISE", beforeSeries, new Dictionary<string, object?>
            {
                ["CurrentRevisionId"] = selectedRevision.Id,
                ["ForecastQty"] = selectedRevision.ForecastQty,
                ["Version"] = series.Version,
                ["ForecastPlanId"] = planId,
                ["ForecastPlanEntryId"] = entry.Id
            }, reason, ct);
        }
        await Emit("ForecastPlanEntry", entry.Id, "CORRECT", new Dictionary<string, object?>
        {
            ["ForecastQty"] = effectiveQty,
            ["PlanRevisionNumber"] = planRevisionNumber
        }, new Dictionary<string, object?>
        {
            ["ForecastQty"] = correction.ForecastQty,
            ["PlanRevisionNumber"] = correction.RevisionNumber,
            ["ForecastRevisionId"] = correction.ForecastRevisionId
        }, reason, ct);

        return new(planId, entry.Id, correction.ForecastQty, correction.RevisionNumber,
            selectedRevision.Id, selectedRevision.RevisionNumber, series.Version, createdForecastRevision,
            new(correction.RevisionNumber, correction.ForecastQty, correction.Reason,
                correction.ActorIdentifier, correction.CreatedAtUtc));
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

        var jalali = calendar.GetJalaliDate(request.ProductionDate);
        var bounds = calendar.GetPeriodBounds(jalali.JalaliYear, jalali.JalaliMonth);
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockPeriodIdentity(db, jalali.JalaliYear * 100 + jalali.JalaliMonth, ct);
        var periodId = await db.BusinessPeriods.AsNoTracking()
            .Where(x => x.JalaliYear == jalali.JalaliYear && x.JalaliMonth == jalali.JalaliMonth)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        BusinessPeriod period;
        var createdPeriod = false;
        if (periodId.HasValue)
        {
            await LockPeriod(db, periodId.Value, false, ct);
            period = await OpenPeriod(db, periodId.Value, ct);
        }
        else
        {
            if (await db.BusinessPeriods.AnyAsync(x => x.StartDate <= bounds.EndDate &&
                x.EndDate >= bounds.StartDate, ct))
                throw new PlanningConflictException("BUSINESS_PERIOD_DATE_OVERLAP",
                    "ماه تاریخ تولید با یک دوره موجود هم‌پوشانی دارد.");
            period = new BusinessPeriod(jalali.JalaliYear, jalali.JalaliMonth,
                bounds.StartDate, bounds.EndDate);
            db.BusinessPeriods.Add(period);
            await Save(db, ct);
            createdPeriod = true;
        }
        var product = await ProductForNewAssignment(db, request.ProductId, ct);
        await EnsureActualUnitCompatibility(db, product.ItemId, period.Id, product.Item.BaseUnitId, ct);
        if (await db.DailyActuals.AnyAsync(x => x.ProductId == request.ProductId &&
            x.ProductionDate == request.ProductionDate, ct))
            throw new PlanningConflictException("DAILY_ACTUAL_EXISTS", "برای محصول و تاریخ انتخاب‌شده Actual ثبت شده است.");

        var actual = new DailyActual(product.ItemId, period.Id, request.ProductionDate, product.Item.BaseUnitId);
        db.DailyActuals.Add(actual);
        await Save(db, ct);
        var revision = new DailyActualRevision(actual.Id, 1, request.ActualQty, null, Actor());
        db.DailyActualRevisions.Add(revision);
        await Save(db, ct);
        actual.SetCurrent(revision);
        await Save(db, ct);
        await tx.CommitAsync(ct);
        var response = (await LoadActual(db, actual.Id, ct))!;
        if (createdPeriod)
            await Emit("BusinessPeriod", period.Id, "CREATE", null, Snapshot(Period(period)), null, ct);
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

    private ForecastPlanDefinition BuildForecastPlanDefinition(string name, string startValue,
        string endValue, IReadOnlyList<Guid> productIds)
    {
        var cleanName = Clean(name);
        if (cleanName is null || cleanName.Length > 200)
            throw Validation("name", "نام برنامه پیش‌بینی الزامی و حداکثر ۲۰۰ کاراکتر است.");
        var start = ParseJalaliMonthAnchor(startValue, "startJalaliDate");
        var end = ParseJalaliMonthAnchor(endValue, "endJalaliDate");
        var startKey = start.Year * 100 + start.Month;
        var endKey = end.Year * 100 + end.Month;
        if (endKey < startKey) throw Validation("endJalaliDate", "تاریخ پایان نمی‌تواند قبل از تاریخ شروع باشد.");
        var distinctProducts = productIds.Distinct().OrderBy(x => x).ToArray();
        if (distinctProducts.Length is < 1 or > 200)
            throw Validation("productIds", "حداقل یک و حداکثر ۲۰۰ محصول فعال قابل انتخاب است.");
        var months = new List<ForecastPlanMonth>();
        var year = start.Year;
        var month = start.Month;
        while (year * 100 + month <= endKey)
        {
            if (months.Count >= 120)
                throw Validation("endJalaliDate", "بازه یک برنامه پیش‌بینی نمی‌تواند بیش از ۱۲۰ ماه باشد.");
            var bounds = calendar.GetPeriodBounds(year, month);
            months.Add(new($"{year:0000}{month:00}", year, month, bounds.StartDate, bounds.EndDate));
            if (month == 12) { year++; month = 1; }
            else month++;
        }
        return new(cleanName, start.Code, end.Code, start, end, months,
            months[0].StartDate, months[^1].EndDate, distinctProducts);
    }

    private (string Code, int Year, int Month) ParseJalaliMonthAnchor(string value, string key)
    {
        var code = value.Trim();
        if (code.Length != 8 || code.Any(x => !char.IsAsciiDigit(x)) ||
            !int.TryParse(code.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year) ||
            !int.TryParse(code.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var month) ||
            !int.TryParse(code.AsSpan(6, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var day) ||
            year < 1 || month is < 1 or > 12 || day != 1)
        {
            throw Validation(key, "تاریخ باید با قالب YYYYMMDD و روز اول ماه وارد شود؛ مانند 14050101.");
        }
        try { _ = calendar.GetPeriodBounds(year, month); }
        catch (ArgumentOutOfRangeException)
        { throw Validation(key, "تاریخ شمسی معتبر نیست."); }
        return (code, year, month);
    }

    private static async Task<List<Product>> LoadActiveForecastProducts(HadronDbContext db,
        Guid[] productIds, CancellationToken ct)
    {
        var products = await db.Products.Include(x => x.Item).ThenInclude(x => x.BaseUnit)
            .Include(x => x.ProductCategory).Include(x => x.BusinessUnit)
            .Where(x => productIds.Contains(x.ItemId)).OrderBy(x => x.Item.BusinessNumber).ToListAsync(ct);
        if (products.Count != productIds.Length) throw new KeyNotFoundException();
        foreach (var product in products) ValidateForecastProduct(product);
        return products;
    }

    private sealed record ForecastPlanMonth(string Code, int Year, int Month,
        DateOnly StartDate, DateOnly EndDate);
    private sealed record ForecastPlanDefinition(string Name, string StartCode, string EndCode,
        (string Code, int Year, int Month) Start, (string Code, int Year, int Month) End,
        IReadOnlyList<ForecastPlanMonth> Months, DateOnly StartDate, DateOnly EndDate,
        Guid[] ProductIds);

    private static (string Code, int Year, int Month) ParsePeriodCode(string value)
    {
        var code = value.Trim();
        if (code.Length != 6 || code.Any(x => !char.IsAsciiDigit(x)) ||
            !int.TryParse(code.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year) ||
            !int.TryParse(code.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var month) ||
            year < 1 || month is < 1 or > 12)
        {
            throw Validation("periodCode", "دوره شمسی باید دقیقاً با قالب YYYYMM، مانند 140501، وارد شود.");
        }
        return (code, year, month);
    }

    private static void ValidateForecastProduct(Product product)
    {
        var errors = new Dictionary<string, string[]>();
        if (!product.Item.IsActive) errors["productId"] = ["محصول باید فعال باشد."];
        if (!product.Item.BaseUnit.IsActive) errors["baseUnitId"] = ["واحد پایه محصول باید فعال باشد."];
        if (!product.ProductCategory.IsActive) errors["productCategoryId"] = ["گروه محصول باید فعال باشد."];
        if (product.BusinessUnit is { IsActive: false }) errors["businessUnitId"] = ["واحد سازمانی باید فعال باشد."];
        if (errors.Count > 0) throw new PlanningValidationException(errors);
    }

    private static async Task LockPeriodIdentity(HadronDbContext db, int periodKey, CancellationToken ct)
    {
        var key = new NpgsqlParameter<int>("period_key", NpgsqlDbType.Integer) { TypedValue = periodKey };
        await db.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(71007, @period_key)", [key], ct);
    }

    private static async Task LockForecastSeriesRows(HadronDbContext db, Guid periodId,
        Guid[] productIds, CancellationToken ct)
    {
        if (productIds.Length == 0) return;
        var period = new NpgsqlParameter<Guid>("period_id", NpgsqlDbType.Uuid) { TypedValue = periodId };
        var products = new NpgsqlParameter<Guid[]>("product_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
        {
            TypedValue = productIds
        };
        await db.Database.ExecuteSqlRawAsync("""
            SELECT "Id"
            FROM planning.forecast_series
            WHERE "BusinessPeriodId" = @period_id AND "ProductId" = ANY(@product_ids)
            ORDER BY "ProductId"
            FOR UPDATE
            """, [period, products], ct);
    }

    private static async Task<Product> ProductForNewAssignment(HadronDbContext db, Guid productId, CancellationToken ct)
    {
        var product = await db.Products.Include(x => x.Item).ThenInclude(x => x.BaseUnit)
            .Include(x => x.ProductCategory).Include(x => x.BusinessUnit)
            .SingleOrDefaultAsync(x => x.ItemId == productId, ct) ?? throw new KeyNotFoundException();
        ValidateForecastProduct(product);
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
    private static ForecastPlanSummaryResponse PlanSummary(ForecastPlan x, int productCount,
        int entryCount) => new(x.Id, x.Name,
        $"{x.StartJalaliYear:D4}{x.StartJalaliMonth:D2}01",
        $"{x.EndJalaliYear:D4}{x.EndJalaliMonth:D2}01", x.StartDate, x.EndDate,
        productCount, (x.EndJalaliYear - x.StartJalaliYear) * 12 + x.EndJalaliMonth -
        x.StartJalaliMonth + 1, entryCount, x.ActorIdentifier, x.CreatedAtUtc);
    private static string PeriodCode(BusinessPeriod x) => $"{x.JalaliYear:D4}{x.JalaliMonth:D2}";
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

    private static string NormalizeDigits(string value)
    {
        Span<char> normalized = stackalloc char[value.Length];
        for (var index = 0; index < value.Length; index++)
        {
            normalized[index] = value[index] switch
            {
                >= '\u06F0' and <= '\u06F9' => (char)('0' + value[index] - '\u06F0'),
                >= '\u0660' and <= '\u0669' => (char)('0' + value[index] - '\u0660'),
                _ => value[index]
            };
        }
        return new string(normalized);
    }
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
