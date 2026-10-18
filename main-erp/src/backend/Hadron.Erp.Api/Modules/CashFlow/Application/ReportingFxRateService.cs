using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.CashFlow.Domain;
using Hadron.Erp.Api.Platform.Audit;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hadron.Erp.Api.Modules.CashFlow.Application;

public sealed class ReportingFxRateService(
    IDbContextFactory<HadronDbContext> dbContextFactory,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider)
{
    private const string BaseCurrencyCode = "USD";
    private const decimal Numeric28Scale12UpperBound = 10_000_000_000_000_000m;

    public async Task<IReadOnlyList<ReportingFxCurrencyOption>> GetCurrencies(
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Currencies.AsNoTracking()
            .OrderBy(x => x.Code)
            .Select(x => new ReportingFxCurrencyOption(
                x.Id, x.Code, x.NameFa, x.NameEn, x.IsActive, x.NormalizedCode == BaseCurrencyCode))
            .ToListAsync(cancellationToken);
    }

    public async Task<ReportingFxRateSetPage> GetRateSets(
        int page, int pageSize, string? status, CancellationToken cancellationToken)
    {
        var offset = ValidatePageAndGetOffset(page, pageSize);
        var normalizedStatus = NormalizeStatus(status);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.ReportingFxRateSets.AsNoTracking();
        if (normalizedStatus is not null)
            query = query.Where(x => x.Status == normalizedStatus);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.EffectiveDate)
            .ThenByDescending(x => x.Revision)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Skip(offset)
            .Take(pageSize)
            .Select(x => new ReportingFxRateSetSummary(
                x.Id,
                x.BaseCurrencyId,
                x.BaseCurrency.Code,
                x.EffectiveDate,
                x.Revision,
                x.Status,
                x.SupersedesRateSetId,
                x.CreatedAtUtc,
                x.UpdatedAtUtc,
                x.PublishedAtUtc,
                x.Rates.Count,
                x.Version))
            .ToListAsync(cancellationToken);

        return new ReportingFxRateSetPage(items, page, pageSize, totalCount);
    }

    public async Task<ReportingFxRateSetResponse?> GetRateSet(
        Guid id, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await RateSetQuery(db, tracking: false)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return entity is null ? null : ToResponse(entity);
    }

    public async Task<ReportingFxRateSetResponse?> GetCurrentPublished(
        DateOnly asOfDate, CancellationToken cancellationToken)
    {
        ValidateDate(asOfDate);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await RateSetQuery(db, tracking: false)
            .Where(x => x.Status == ReportingFxRateSetStatuses.Published &&
                        x.EffectiveDate <= asOfDate &&
                        x.BaseCurrency.NormalizedCode == BaseCurrencyCode)
            .OrderByDescending(x => x.EffectiveDate)
            .ThenByDescending(x => x.Revision)
            .FirstOrDefaultAsync(cancellationToken);
        return entity is null ? null : ToResponse(entity);
    }

    public async Task<ReportingFxRateSetResponse> Create(
        CreateReportingFxRateSetRequest request, CancellationToken cancellationToken)
    {
        ValidateDate(request.EffectiveDate);
        ValidateClientRequestId(request.ClientRequestId);
        var reason = CleanOptionalReason(request.ChangeReason, required: false, "changeReason");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var baseCurrency = await GetBaseCurrency(db, cancellationToken);
        var rates = await ValidateAndNormalizeRates(db, baseCurrency.Id, request.Rates, cancellationToken);
        var requestFingerprint = CreateFingerprint(request.EffectiveDate, reason, rates);

        var idempotent = await FindByClientRequestId(db, request.ClientRequestId, cancellationToken);
        if (idempotent is not null)
            return EnsureIdempotent(idempotent, requestFingerprint);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (await db.ReportingFxRateSets.AnyAsync(x =>
                x.BaseCurrencyId == baseCurrency.Id && x.EffectiveDate == request.EffectiveDate,
                cancellationToken))
        {
            throw new ReportingFxConflictException(
                "RATE_DATE_ALREADY_EXISTS",
                "برای این تاریخ قبلاً جدول نرخ ثبت شده است؛ برای اصلاح نسخه منتشرشده از ایجاد نسخه اصلاحی استفاده کنید.");
        }

        var now = UtcNow();
        var entity = new ReportingFxRateSet(
            baseCurrency.Id,
            request.EffectiveDate,
            1,
            null,
            request.ClientRequestId,
            requestFingerprint,
            reason,
            ActorIdentifier(),
            now,
            rates);
        db.ReportingFxRateSets.Add(entity);
        AppendAudit(db, entity, "CREATE", null, Snapshot(entity), reason, now);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception, out var constraint))
        {
            await transaction.RollbackAsync(cancellationToken);
            if (IsClientRequestConstraint(constraint))
            {
                var existing = await LoadByClientRequestIdFresh(request.ClientRequestId, cancellationToken);
                if (existing is not null)
                    return EnsureIdempotent(existing, requestFingerprint);
            }

            throw UniqueConflict();
        }
        catch (DbUpdateException exception) when (HasPostgresState(
                   exception, PostgresErrorCodes.SerializationFailure))
        {
            throw ConcurrentChange();
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            throw ConcurrentChange();
        }

        return await LoadRequiredFresh(entity.Id, cancellationToken);
    }

    public async Task<ReportingFxRateSetResponse> UpdateDraft(
        Guid id, UpdateReportingFxRateSetRequest request, CancellationToken cancellationToken)
    {
        ValidateClientRequestId(request.ClientRequestId);
        ValidateDate(request.EffectiveDate);
        ValidateVersion(request.ExpectedVersion);
        var reason = CleanOptionalReason(request.ChangeReason, required: false, "changeReason");
        var requestFingerprint = UpdateFingerprint(id, request, reason);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var replay = await FindOperationReceipt(
            db, ReportingFxOperationTypes.UpdateDraft, request.ClientRequestId, cancellationToken);
        if (replay is not null)
            return await EnsureOperationReplay(db, replay, id, requestFingerprint, cancellationToken);

        // Take the before-image without tracking the required child rows. Tracking the old
        // lines and then replacing them makes EF retain an orphan relationship snapshot and
        // issue a spurious UPDATE after the set-based DELETE.
        var beforeImage = await RateSetQuery(db, tracking: false)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException();
        var oldValues = Snapshot(beforeImage);

        var entity = await db.ReportingFxRateSets
            .Include(x => x.BaseCurrency)
            .SingleAsync(x => x.Id == id, cancellationToken);
        EnsureDraft(entity);
        EnsureVersion(entity.Version, request.ExpectedVersion);

        if (entity.SupersedesRateSetId.HasValue && request.EffectiveDate != entity.EffectiveDate)
        {
            throw new ReportingFxValidationException(Error(
                "effectiveDate", "تاریخ اثر نسخه اصلاحی قابل تغییر نیست."));
        }

        if (request.EffectiveDate != entity.EffectiveDate &&
            await db.ReportingFxRateSets.AnyAsync(x =>
                x.Id != id && x.BaseCurrencyId == entity.BaseCurrencyId &&
                x.EffectiveDate == request.EffectiveDate, cancellationToken))
        {
            throw new ReportingFxConflictException(
                "RATE_DATE_ALREADY_EXISTS", "برای تاریخ انتخاب‌شده قبلاً جدول نرخ ثبت شده است.");
        }

        var rates = await ValidateAndNormalizeRates(
            db, entity.BaseCurrencyId, request.Rates, cancellationToken);
        // Replace the required child collection inside the same transaction. The old rows were
        // intentionally loaded only as a no-tracking before-image above, so EF cannot later try
        // to null their non-nullable FK.
        await db.ReportingFxRates.Where(x => x.RateSetId == entity.Id)
            .ExecuteDeleteAsync(cancellationToken);
        var now = UtcNow();
        entity.ReplaceDraft(request.EffectiveDate, reason, ActorIdentifier(), now, rates);
        // These domain-created children already have application-generated GUIDs. When they are
        // discovered from an existing tracked parent, EF convention otherwise assumes that a
        // non-default generated key denotes an existing row and marks them Modified. State the
        // insert intent explicitly.
        db.ReportingFxRates.AddRange(entity.Rates);
        db.ReportingFxOperationReceipts.Add(new ReportingFxOperationReceipt(
            entity.Id, ReportingFxOperationTypes.UpdateDraft, request.ClientRequestId,
            requestFingerprint, entity.Version, now));
        AppendAudit(db, entity, "UPDATE_DRAFT", oldValues, Snapshot(entity), reason, now);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw StaleVersion(exception);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception, out var constraint))
        {
            await transaction.RollbackAsync(cancellationToken);
            if (IsOperationReceiptConstraint(constraint))
            {
                var idempotent = await LoadOperationReplayFresh(
                    ReportingFxOperationTypes.UpdateDraft, request.ClientRequestId,
                    id, requestFingerprint, cancellationToken);
                if (idempotent is not null) return idempotent;
            }
            throw UniqueConflict();
        }
        catch (DbUpdateException exception) when (HasPostgresState(
                   exception, PostgresErrorCodes.SerializationFailure))
        {
            throw ConcurrentChange();
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            throw ConcurrentChange();
        }

        return await LoadRequiredFresh(entity.Id, cancellationToken);
    }

    public async Task<ReportingFxRateSetResponse> Publish(
        Guid id, PublishReportingFxRateSetRequest request, CancellationToken cancellationToken)
    {
        ValidateClientRequestId(request.ClientRequestId);
        ValidateVersion(request.ExpectedVersion);
        var reason = CleanOptionalReason(request.Reason, required: true, "reason")!;
        var requestFingerprint = PublishFingerprint(id, request, reason);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var replay = await FindOperationReceipt(
            db, ReportingFxOperationTypes.Publish, request.ClientRequestId, cancellationToken);
        if (replay is not null)
            return await EnsureOperationReplay(db, replay, id, requestFingerprint, cancellationToken);

        var entity = await RateSetQuery(db, tracking: true)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException();
        EnsureDraft(entity);
        EnsureVersion(entity.Version, request.ExpectedVersion);
        await EnsurePublishable(db, entity, cancellationToken);

        var oldValues = Snapshot(entity);
        var now = UtcNow();
        entity.Publish(reason, ActorIdentifier(), now);
        db.ReportingFxOperationReceipts.Add(new ReportingFxOperationReceipt(
            entity.Id, ReportingFxOperationTypes.Publish, request.ClientRequestId,
            requestFingerprint, entity.Version, now));
        AppendAudit(db, entity, "PUBLISH", oldValues, Snapshot(entity), reason, now);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw StaleVersion(exception);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception, out var constraint))
        {
            await transaction.RollbackAsync(cancellationToken);
            if (IsOperationReceiptConstraint(constraint))
            {
                var idempotent = await LoadOperationReplayFresh(
                    ReportingFxOperationTypes.Publish, request.ClientRequestId,
                    id, requestFingerprint, cancellationToken);
                if (idempotent is not null) return idempotent;
            }
            throw UniqueConflict();
        }
        catch (DbUpdateException exception) when (HasPostgresState(
                   exception, PostgresErrorCodes.SerializationFailure))
        {
            throw ConcurrentChange();
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            throw ConcurrentChange();
        }

        return await LoadRequiredFresh(entity.Id, cancellationToken);
    }

    public async Task<ReportingFxRateSetResponse> CreateCorrection(
        Guid publishedRateSetId, CreateReportingFxCorrectionRequest request,
        CancellationToken cancellationToken)
    {
        ValidateClientRequestId(request.ClientRequestId);
        ValidateVersion(request.ExpectedVersion);
        var reason = CleanOptionalReason(request.Reason, required: true, "reason")!;
        var requestFingerprint = CorrectionFingerprint(
            publishedRateSetId, request.ExpectedVersion, reason);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var idempotent = await FindByClientRequestId(db, request.ClientRequestId, cancellationToken);
        if (idempotent is not null)
            return EnsureIdempotent(idempotent, requestFingerprint);

        var source = await RateSetQuery(db, tracking: false)
            .SingleOrDefaultAsync(x => x.Id == publishedRateSetId, cancellationToken)
            ?? throw new KeyNotFoundException();

        if (source.Status != ReportingFxRateSetStatuses.Published)
        {
            throw new ReportingFxConflictException(
                "CORRECTION_SOURCE_NOT_PUBLISHED", "فقط برای جدول نرخ منتشرشده می‌توان نسخه اصلاحی ساخت.");
        }
        EnsureVersion(source.Version, request.ExpectedVersion);

        var latestRevision = await db.ReportingFxRateSets
            .Where(x => x.BaseCurrencyId == source.BaseCurrencyId &&
                        x.EffectiveDate == source.EffectiveDate &&
                        x.Status == ReportingFxRateSetStatuses.Published)
            .MaxAsync(x => x.Revision, cancellationToken);
        if (source.Revision != latestRevision)
        {
            throw new ReportingFxConflictException(
                "SUPERSEDED_RATE_SET", "این نسخه دیگر آخرین نسخه منتشرشده نیست؛ اصلاح را از آخرین نسخه آغاز کنید.");
        }

        if (await db.ReportingFxRateSets.AnyAsync(x =>
                x.BaseCurrencyId == source.BaseCurrencyId &&
                x.EffectiveDate == source.EffectiveDate &&
                x.Status == ReportingFxRateSetStatuses.Draft, cancellationToken))
        {
            throw new ReportingFxConflictException(
                "CORRECTION_DRAFT_ALREADY_EXISTS", "برای این تاریخ یک نسخه اصلاحی پیش‌نویس وجود دارد.");
        }

        var now = UtcNow();
        var correction = new ReportingFxRateSet(
            source.BaseCurrencyId,
            source.EffectiveDate,
            latestRevision + 1,
            source.Id,
            request.ClientRequestId,
            requestFingerprint,
            reason,
            ActorIdentifier(),
            now,
            source.Rates.Select(x => (x.CurrencyId, x.UnitsPerUsd)));
        db.ReportingFxRateSets.Add(correction);
        AppendAudit(db, correction, "CREATE_CORRECTION", Snapshot(source), Snapshot(correction), reason, now);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception, out var constraint))
        {
            await transaction.RollbackAsync(cancellationToken);
            if (IsClientRequestConstraint(constraint))
            {
                var existing = await LoadByClientRequestIdFresh(request.ClientRequestId, cancellationToken);
                if (existing is not null)
                    return EnsureIdempotent(existing, requestFingerprint);
            }

            throw UniqueConflict();
        }
        catch (DbUpdateException exception) when (HasPostgresState(
                   exception, PostgresErrorCodes.SerializationFailure))
        {
            throw ConcurrentChange();
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            throw ConcurrentChange();
        }

        return await LoadRequiredFresh(correction.Id, cancellationToken);
    }

    private static IQueryable<ReportingFxRateSet> RateSetQuery(HadronDbContext db, bool tracking)
    {
        var query = db.ReportingFxRateSets
            .Include(x => x.BaseCurrency)
            .Include(x => x.Rates).ThenInclude(x => x.Currency)
            .AsSplitQuery();
        return tracking ? query : query.AsNoTracking();
    }

    private static async Task<Hadron.Erp.Api.Modules.ReferenceData.Domain.Currency> GetBaseCurrency(
        HadronDbContext db, CancellationToken cancellationToken)
    {
        var currency = await db.Currencies.SingleOrDefaultAsync(
            x => x.NormalizedCode == BaseCurrencyCode, cancellationToken);
        if (currency is null)
        {
            throw new ReportingFxConflictException(
                "USD_CURRENCY_MISSING", "ارز پایه USD در اطلاعات مرجع تعریف نشده است.");
        }
        if (!currency.IsActive)
        {
            throw new ReportingFxConflictException(
                "USD_CURRENCY_INACTIVE", "ارز پایه USD غیرفعال است و جدول نرخ قابل ثبت نیست.");
        }
        return currency;
    }

    private static async Task<IReadOnlyList<(Guid CurrencyId, decimal UnitsPerUsd)>> ValidateAndNormalizeRates(
        HadronDbContext db, Guid baseCurrencyId, IReadOnlyList<ReportingFxRateInput>? inputs,
        CancellationToken cancellationToken)
    {
        inputs ??= [];
        var errors = new Dictionary<string, string[]>();
        var duplicateIds = inputs.GroupBy(x => x.CurrencyId).Where(x => x.Count() > 1)
            .Select(x => x.Key).ToArray();
        if (duplicateIds.Length > 0)
            errors["rates"] = ["برای هر ارز فقط یک نرخ می‌توان ثبت کرد."];

        if (inputs.Any(x => x.CurrencyId == Guid.Empty))
            errors["rates.currencyId"] = ["شناسه ارز معتبر الزامی است."];
        if (inputs.Any(x => x.CurrencyId == baseCurrencyId))
            errors["rates.unitsPerUsd"] = ["نرخ USD همیشه دقیقاً ۱ است و نباید به‌صورت دستی ثبت شود."];
        if (inputs.Any(x => x.UnitsPerUsd <= 0))
            errors["rates.unitsPerUsd"] = ["همه نرخ‌ها باید بزرگ‌تر از صفر باشند."];
        if (inputs.Any(x => DecimalScale(x.UnitsPerUsd) > 12 ||
                            x.UnitsPerUsd >= Numeric28Scale12UpperBound ||
                            x.UnitsPerUsd <= -Numeric28Scale12UpperBound))
        {
            errors["rates.unitsPerUsd.precision"] =
                ["نرخ باید حداکثر ۱۲ رقم اعشار و کمتر از ۱۰ به توان ۱۶ باشد."];
        }

        var distinctIds = inputs.Select(x => x.CurrencyId).Where(x => x != Guid.Empty).Distinct().ToArray();
        var existingIds = await db.Currencies.AsNoTracking()
            .Where(x => distinctIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        if (existingIds.Count != distinctIds.Length)
            errors["rates.currencyId"] = ["یک یا چند ارز در اطلاعات مرجع وجود ندارد."];

        if (errors.Count > 0) throw new ReportingFxValidationException(errors);
        return inputs.OrderBy(x => x.CurrencyId)
            .Select(x => (x.CurrencyId, x.UnitsPerUsd))
            .ToArray();
    }

    private static async Task EnsurePublishable(
        HadronDbContext db, ReportingFxRateSet entity, CancellationToken cancellationToken)
    {
        var definedCurrencies = await db.Currencies.AsNoTracking()
            .Where(x => x.Id != entity.BaseCurrencyId)
            .OrderBy(x => x.Code)
            .Select(x => new { x.Id, x.Code })
            .ToListAsync(cancellationToken);
        var entered = entity.Rates.Select(x => x.CurrencyId).ToHashSet();
        var missing = definedCurrencies.Where(x => !entered.Contains(x.Id)).Select(x => x.Code).ToArray();
        if (missing.Length > 0)
        {
            throw new ReportingFxValidationException(new Dictionary<string, string[]>
            {
                ["rates"] = [$"نرخ ارزهای تعریف‌شده زیر ثبت نشده است: {string.Join("، ", missing)}"]
            });
        }
    }

    private async Task<ReportingFxRateSet?> FindByClientRequestId(
        HadronDbContext db, Guid clientRequestId, CancellationToken cancellationToken) =>
        await RateSetQuery(db, tracking: false)
            .SingleOrDefaultAsync(x => x.ClientRequestId == clientRequestId, cancellationToken);

    private async Task<ReportingFxRateSet?> LoadByClientRequestIdFresh(
        Guid clientRequestId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await FindByClientRequestId(db, clientRequestId, cancellationToken);
    }

    private static async Task<ReportingFxOperationReceipt?> FindOperationReceipt(
        HadronDbContext db, string operationType, Guid clientRequestId,
        CancellationToken cancellationToken) =>
        await db.ReportingFxOperationReceipts.AsNoTracking().SingleOrDefaultAsync(
            x => x.OperationType == operationType && x.ClientRequestId == clientRequestId,
            cancellationToken);

    private static async Task<ReportingFxRateSetResponse> EnsureOperationReplay(
        HadronDbContext db, ReportingFxOperationReceipt receipt, Guid rateSetId,
        string requestFingerprint, CancellationToken cancellationToken)
    {
        if (receipt.RateSetId != rateSetId ||
            !string.Equals(receipt.RequestFingerprint, requestFingerprint, StringComparison.Ordinal))
        {
            throw new ReportingFxConflictException(
                "IDEMPOTENCY_KEY_REUSED",
                "شناسه درخواست قبلاً برای درخواست دیگری استفاده شده است.",
                new { receipt.OperationType, receipt.RateSetId, receipt.ResultVersion });
        }

        var entity = await RateSetQuery(db, tracking: false)
            .SingleOrDefaultAsync(x => x.Id == receipt.RateSetId, cancellationToken)
            ?? throw new InvalidOperationException("The idempotent reporting FX result no longer exists.");
        if (entity.Version != receipt.ResultVersion)
        {
            throw new ReportingFxConflictException(
                "IDEMPOTENT_RESULT_SUPERSEDED",
                "نتیجه این درخواست با تغییر جدیدتری جایگزین شده است. اطلاعات را دوباره بارگذاری کنید.",
                new { receipt.OperationType, receipt.ResultVersion, currentVersion = entity.Version });
        }
        return ToResponse(entity);
    }

    private async Task<ReportingFxRateSetResponse?> LoadOperationReplayFresh(
        string operationType, Guid clientRequestId, Guid rateSetId,
        string requestFingerprint, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var receipt = await FindOperationReceipt(db, operationType, clientRequestId, cancellationToken);
        return receipt is null
            ? null
            : await EnsureOperationReplay(db, receipt, rateSetId, requestFingerprint, cancellationToken);
    }

    private async Task<ReportingFxRateSetResponse> LoadRequiredFresh(
        Guid id, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await RateSetQuery(db, tracking: false)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("The saved reporting FX rate set could not be reloaded.");
        return ToResponse(entity);
    }

    private static ReportingFxRateSetResponse EnsureIdempotent(
        ReportingFxRateSet existing, string requestFingerprint)
    {
        if (!string.Equals(existing.RequestFingerprint, requestFingerprint, StringComparison.Ordinal))
        {
            throw new ReportingFxConflictException(
                "IDEMPOTENCY_KEY_REUSED", "شناسه درخواست قبلاً برای درخواست دیگری استفاده شده است.");
        }

        // Create and correction requests persist their idempotency identity on the new
        // rate set itself. Version 1 is therefore the exact result of that request. Once
        // a later operation changes the set, returning the current representation would
        // falsely replay a result the original request never produced.
        if (existing.Version != 1)
        {
            throw new ReportingFxConflictException(
                "IDEMPOTENT_RESULT_SUPERSEDED",
                "نتیجه این درخواست با تغییر جدیدتری جایگزین شده است. اطلاعات را دوباره بارگذاری کنید.",
                new { resultVersion = 1, currentVersion = existing.Version });
        }

        return ToResponse(existing);
    }

    private static ReportingFxRateSetResponse ToResponse(ReportingFxRateSet x) => new(
        x.Id,
        x.BaseCurrencyId,
        x.BaseCurrency.Code,
        x.EffectiveDate,
        x.Revision,
        x.Status,
        x.SupersedesRateSetId,
        x.ClientRequestId,
        x.ChangeReason,
        x.CreatedByIdentifier,
        x.CreatedAtUtc,
        x.UpdatedAtUtc,
        x.PublishedByIdentifier,
        x.PublishedAtUtc,
        x.PublishReason,
        x.Version,
        x.Rates.OrderBy(rate => rate.Currency.Code)
            .Select(rate => new ReportingFxRateResponse(
                rate.CurrencyId,
                rate.Currency.Code,
                rate.Currency.NameFa,
                rate.Currency.NameEn,
                rate.Currency.IsActive,
                rate.UnitsPerUsd))
            .ToArray());

    private void AppendAudit(
        HadronDbContext db,
        ReportingFxRateSet entity,
        string action,
        IReadOnlyDictionary<string, object?>? oldValues,
        IReadOnlyDictionary<string, object?> newValues,
        string? reason,
        DateTime occurredAtUtc)
    {
        db.OperationalAuditEvents.Add(OperationalAuditEvent.From(new AuditEvent(
            "ReportingFxRateSet",
            entity.Id,
            action,
            occurredAtUtc,
            ActorIdentifier(),
            AuditEventContext.CorrelationId(httpContextAccessor.HttpContext),
            oldValues,
            newValues,
            reason)));
    }

    private static IReadOnlyDictionary<string, object?> Snapshot(ReportingFxRateSet entity) =>
        new Dictionary<string, object?>
        {
            ["BaseCurrencyId"] = entity.BaseCurrencyId,
            ["EffectiveDate"] = entity.EffectiveDate,
            ["Revision"] = entity.Revision,
            ["Status"] = entity.Status,
            ["SupersedesRateSetId"] = entity.SupersedesRateSetId,
            ["ClientRequestId"] = entity.ClientRequestId,
            ["RequestFingerprint"] = entity.RequestFingerprint,
            ["ChangeReason"] = entity.ChangeReason,
            ["PublishedAtUtc"] = entity.PublishedAtUtc,
            ["PublishedByIdentifier"] = entity.PublishedByIdentifier,
            ["PublishReason"] = entity.PublishReason,
            ["Version"] = entity.Version,
            ["Rates"] = entity.Rates.OrderBy(x => x.CurrencyId)
                .Select(x => new { x.CurrencyId, x.UnitsPerUsd }).ToArray()
        };

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
    private string? ActorIdentifier() => AuditEventContext.ActorId(httpContextAccessor.HttpContext);

    private static void EnsureDraft(ReportingFxRateSet entity)
    {
        if (entity.Status != ReportingFxRateSetStatuses.Draft)
            throw new ReportingFxConflictException("RATE_SET_IMMUTABLE", "جدول نرخ منتشرشده قابل ویرایش نیست.");
    }

    private static void EnsureVersion(long current, long expected)
    {
        if (current != expected)
        {
            throw new ReportingFxConflictException(
                "STALE_VERSION",
                "نسخه ارسالی قدیمی است. اطلاعات را دوباره بارگذاری کنید.",
                new { currentVersion = current, expectedVersion = expected });
        }
    }

    private static void ValidateDate(DateOnly date)
    {
        if (date == default)
            throw new ReportingFxValidationException(Error("effectiveDate", "تاریخ اثر معتبر الزامی است."));
    }

    private static void ValidateClientRequestId(Guid clientRequestId)
    {
        if (clientRequestId == Guid.Empty)
            throw new ReportingFxValidationException(Error(
                "clientRequestId", "شناسه یکتای درخواست الزامی است."));
    }

    private static void ValidateVersion(long version)
    {
        if (version < 1)
            throw new ReportingFxValidationException(Error("expectedVersion", "نسخه معتبر الزامی است."));
    }

    private static int ValidatePageAndGetOffset(int page, int pageSize)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1) errors["page"] = ["شماره صفحه باید حداقل ۱ باشد."];
        if (pageSize is < 1 or > 100) errors["pageSize"] = ["اندازه صفحه باید بین ۱ و ۱۰۰ باشد."];
        if (errors.Count == 0 && ((long)page - 1L) * pageSize > int.MaxValue)
            errors["page"] = ["شماره صفحه برای اندازه صفحه انتخاب‌شده بیش از حد مجاز است."];
        if (errors.Count > 0) throw new ReportingFxValidationException(errors);
        return checked((page - 1) * pageSize);
    }

    private static string? NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;
        var normalized = status.Trim().ToUpperInvariant();
        if (!ReportingFxRateSetStatuses.All.Contains(normalized))
            throw new ReportingFxValidationException(Error("status", "وضعیت جدول نرخ معتبر نیست."));
        return normalized;
    }

    private static string? CleanOptionalReason(string? reason, bool required, string field)
    {
        var clean = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (required && clean is null)
            throw new ReportingFxValidationException(Error(field, "دلیل تغییر الزامی است."));
        if (clean?.Length > 1000)
            throw new ReportingFxValidationException(Error(field, "دلیل تغییر حداکثر ۱۰۰۰ نویسه است."));
        return clean;
    }

    private static int DecimalScale(decimal value) =>
        (decimal.GetBits(value)[3] >> 16) & 0x7F;

    private static string CreateFingerprint(DateOnly effectiveDate, string? reason,
        IEnumerable<(Guid CurrencyId, decimal UnitsPerUsd)> rates)
    {
        var canonicalRates = string.Join(";", rates.OrderBy(x => x.CurrencyId).Select(x =>
            $"{x.CurrencyId:N}={x.UnitsPerUsd.ToString("G29", CultureInfo.InvariantCulture)}"));
        return Sha256($"CREATE|{effectiveDate:yyyy-MM-dd}|{reason ?? string.Empty}|{canonicalRates}");
    }

    private static string UpdateFingerprint(Guid rateSetId, UpdateReportingFxRateSetRequest request,
        string? reason)
    {
        var canonicalRates = string.Join(";", (request.Rates ?? []).OrderBy(x => x.CurrencyId).Select(x =>
            $"{x.CurrencyId:N}={x.UnitsPerUsd.ToString("G29", CultureInfo.InvariantCulture)}"));
        return Sha256($"UPDATE_DRAFT|{rateSetId:N}|{request.EffectiveDate:yyyy-MM-dd}|" +
                      $"{request.ExpectedVersion}|{reason ?? string.Empty}|{canonicalRates}");
    }

    private static string PublishFingerprint(Guid rateSetId, PublishReportingFxRateSetRequest request,
        string reason) =>
        Sha256($"PUBLISH|{rateSetId:N}|{request.ExpectedVersion}|{reason}");

    private static string CorrectionFingerprint(Guid sourceId, long expectedVersion, string reason) =>
        Sha256($"CORRECTION|{sourceId:N}|{expectedVersion}|{reason}");

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool IsUniqueViolation(DbUpdateException exception, out string? constraint)
    {
        var postgres = exception.InnerException as PostgresException ??
                       exception.GetBaseException() as PostgresException;
        constraint = postgres?.ConstraintName;
        return postgres?.SqlState == PostgresErrorCodes.UniqueViolation;
    }

    private static bool HasPostgresState(DbUpdateException exception, string sqlState) =>
        (exception.InnerException as PostgresException ??
         exception.GetBaseException() as PostgresException)?.SqlState == sqlState;

    private static bool IsClientRequestConstraint(string? constraint) =>
        constraint?.Contains("ClientRequestId", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsOperationReceiptConstraint(string? constraint) =>
        constraint?.Contains("reporting_fx_operation_receipts", StringComparison.OrdinalIgnoreCase) == true;

    private static ReportingFxConflictException UniqueConflict() =>
        new("RATE_SET_CONFLICT",
            "جدول نرخ هم‌زمان توسط درخواست دیگری تغییر کرده است. اطلاعات را دوباره بارگذاری کنید.");

    private static ReportingFxConflictException StaleVersion() =>
        new("STALE_VERSION", "نسخه ارسالی قدیمی است. اطلاعات را دوباره بارگذاری کنید.");

    private static ReportingFxConflictException StaleVersion(DbUpdateConcurrencyException exception) =>
        new("STALE_VERSION", "نسخه ارسالی قدیمی است. اطلاعات را دوباره بارگذاری کنید.",
            new
            {
                entries = exception.Entries.Select(x => new
                {
                    entityType = x.Metadata.ClrType.Name,
                    state = x.State.ToString()
                }).ToArray()
            });

    private static ReportingFxConflictException ConcurrentChange() =>
        new("CONCURRENT_CHANGE", "تغییر هم‌زمان تشخیص داده شد. اطلاعات را دوباره بارگذاری و تلاش کنید.");

    private static Dictionary<string, string[]> Error(string key, string message) =>
        new() { [key] = [message] };
}
