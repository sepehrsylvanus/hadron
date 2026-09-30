using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.CashFlow.Domain;
using Hadron.Erp.Api.Platform.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Hadron.Erp.Api.Modules.CashFlow.Application;

public sealed class MaterialCostBookService(
    IDbContextFactory<HadronDbContext> factory,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider)
{
    public const int MaximumPageSize = 100;
    private const decimal Numeric28Scale12UpperBound = 10_000_000_000_000_000m;

    public async Task<MaterialCostBookPageResponse> GetBooks(int page, int pageSize,
        string? search, string? status, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        var normalizedStatus = NormalizeStatus(status);
        var pattern = SearchPattern(search);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.MaterialCostBookRevisions.AsNoTracking();
        if (normalizedStatus is not null) query = query.Where(x => x.Status == normalizedStatus);
        if (pattern is not null)
        {
            query = query.Where(x => EF.Functions.ILike(
                    x.MaterialCostBook.BookNumber, pattern, "\\") ||
                EF.Functions.ILike(x.NameFa, pattern, "\\") ||
                (x.NameEn != null && EF.Functions.ILike(x.NameEn, pattern, "\\")));
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.MaterialCostBook.BookNumber)
            .ThenByDescending(x => x.Revision)
            .Skip(checked((page - 1) * pageSize)).Take(pageSize)
            .Select(x => new MaterialCostBookSummaryResponse(
                x.MaterialCostBookId, x.MaterialCostBook.BookNumber, x.Id, x.Revision,
                x.NameFa, x.NameEn, x.EffectiveDate, x.Status, x.SupersedesRevisionId,
                x.Lines.Count, x.CreatedAtUtc, x.UpdatedAtUtc, x.PublishedAtUtc, x.Version))
            .ToArrayAsync(ct);
        return new(rows, page, pageSize, total);
    }

    public async Task<MaterialCostBookRevisionResponse?> GetBook(Guid bookId,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await RevisionQuery(db, false)
            .Where(x => x.MaterialCostBookId == bookId)
            .OrderByDescending(x => x.Status == MaterialCostBookRevisionStatuses.Draft)
            .ThenByDescending(x => x.Revision)
            .FirstOrDefaultAsync(ct);
        return entity is null ? null : ToResponse(entity);
    }

    public async Task<MaterialCostBookRevisionResponse?> GetRevision(Guid revisionId,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await RevisionQuery(db, false)
            .SingleOrDefaultAsync(x => x.Id == revisionId, ct);
        return entity is null ? null : ToResponse(entity);
    }

    public async Task<MaterialCostBookLinePageResponse?> GetLines(Guid revisionId,
        int page, int pageSize, string? search, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        var pattern = SearchPattern(search);
        await using var db = await factory.CreateDbContextAsync(ct);
        var revision = await db.MaterialCostBookRevisions.AsNoTracking()
            .Where(x => x.Id == revisionId)
            .Select(x => new { x.Id, x.Version }).SingleOrDefaultAsync(ct);
        if (revision is null) return null;
        var query = db.MaterialCostBookLines.AsNoTracking()
            .Where(x => x.MaterialCostBookRevisionId == revisionId);
        if (pattern is not null)
        {
            query = query.Where(x => EF.Functions.ILike(x.ItemCode, pattern, "\\") ||
                EF.Functions.ILike(x.ItemNameFa, pattern, "\\") ||
                (x.ItemNameEn != null && EF.Functions.ILike(x.ItemNameEn, pattern, "\\")) ||
                EF.Functions.ILike(x.CurrencyCode, pattern, "\\") ||
                (x.SupplierNumber != null && EF.Functions.ILike(x.SupplierNumber, pattern, "\\")) ||
                (x.SupplierName != null && EF.Functions.ILike(x.SupplierName, pattern, "\\")));
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.ItemCode).ThenBy(x => x.ItemId)
            .Skip(checked((page - 1) * pageSize)).Take(pageSize).ToArrayAsync(ct);
        return new(rows.Select(ToResponse).ToArray(), page, pageSize, total, revision.Version);
    }

    public async Task<MaterialCostBookItemOptionPage> GetItemOptions(int page, int pageSize,
        string? search, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        var pattern = SearchPattern(search);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Items.AsNoTracking().Where(x => x.IsActive && x.BaseUnit.IsActive);
        if (pattern is not null)
        {
            query = query.Where(x => EF.Functions.ILike(x.BusinessNumber, pattern, "\\") ||
                EF.Functions.ILike(x.NameFa, pattern, "\\") ||
                (x.NameEn != null && EF.Functions.ILike(x.NameEn, pattern, "\\")));
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.BusinessNumber)
            .Skip(checked((page - 1) * pageSize)).Take(pageSize)
            .Select(x => new
            {
                x.Id,
                Code = x.BusinessNumber,
                x.NameFa,
                x.NameEn,
                x.BaseUnitId,
                BaseUnitCode = x.BaseUnit.Code,
                BaseUnitNameFa = x.BaseUnit.NameFa,
                BaseUnitNameEn = x.BaseUnit.NameEn,
                BaseUnitSymbol = x.BaseUnit.Symbol
            }).ToArrayAsync(ct);
        var ids = items.Select(x => x.Id).ToArray();
        var conversions = await db.InventoryItemUnitConversions.AsNoTracking()
            .Where(x => ids.Contains(x.ItemId) && x.IsActive &&
                x.TransactionUnit.IsActive)
            .Select(x => new
            {
                x.ItemId,
                x.TransactionUnitId,
                x.TransactionUnit.Code,
                x.TransactionUnit.NameFa,
                x.TransactionUnit.NameEn,
                x.TransactionUnit.Symbol,
                x.FactorToBase,
                x.BaseUnitId
            }).OrderBy(x => x.Code).ToArrayAsync(ct);
        var grouped = conversions.GroupBy(x => x.ItemId).ToDictionary(x => x.Key, x => x.ToArray());
        var result = items.Select(item =>
        {
            var units = new List<MaterialCostBookPriceUnitOption>
            {
                new(item.BaseUnitId, item.BaseUnitCode, item.BaseUnitNameFa,
                    item.BaseUnitNameEn, item.BaseUnitSymbol, 1m, true)
            };
            if (grouped.TryGetValue(item.Id, out var alternateUnits))
            {
                units.AddRange(alternateUnits.Where(x => x.BaseUnitId == item.BaseUnitId)
                    .Select(x => new MaterialCostBookPriceUnitOption(
                        x.TransactionUnitId, x.Code, x.NameFa, x.NameEn, x.Symbol,
                        x.FactorToBase, false)));
            }
            return new MaterialCostBookItemOption(item.Id, item.Code, item.NameFa,
                item.NameEn, item.BaseUnitId, item.BaseUnitCode, units);
        }).ToArray();
        return new(result, page, pageSize, total);
    }

    public async Task<IReadOnlyList<MaterialCostBookCurrencyOption>> GetCurrencyOptions(
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Currencies.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.Code)
            .Select(x => new MaterialCostBookCurrencyOption(x.Id, x.Code, x.NameFa,
                x.NameEn, x.Symbol, x.DecimalPrecision))
            .ToArrayAsync(ct);
    }

    public async Task<MaterialCostBookSupplierOptionPage> GetSupplierOptions(
        int page, int pageSize, string? search, Guid? selectedSupplierId, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        var pattern = SearchPattern(search);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Suppliers.AsNoTracking().Where(x => x.IsActive);
        if (pattern is not null)
        {
            query = query.Where(x => EF.Functions.ILike(x.SupplierNumber, pattern, "\\") ||
                EF.Functions.ILike(x.Name, pattern, "\\"));
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.SupplierNumber).ThenBy(x => x.Id)
            .Skip(checked((page - 1) * pageSize)).Take(pageSize)
            .Select(x => new MaterialCostBookSupplierOption(
                x.Id, x.SupplierNumber, x.Name, x.IsActive))
            .ToArrayAsync(ct);
        var selected = selectedSupplierId.HasValue
            ? await db.Suppliers.AsNoTracking()
                .Where(x => x.Id == selectedSupplierId.Value)
                .Select(x => new MaterialCostBookSupplierOption(
                    x.Id, x.SupplierNumber, x.Name, x.IsActive))
                .SingleOrDefaultAsync(ct)
            : null;
        return new(rows, page, pageSize, total, selected);
    }

    public async Task<IReadOnlyList<MaterialCostBookSelectionOption>> GetEffectiveSelections(
        DateOnly asOfDate, int page, int pageSize, string? search, CancellationToken ct)
    {
        ValidateDate(asOfDate);
        ValidatePage(page, pageSize);
        var pattern = SearchPattern(search);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.MaterialCostBookRevisions.AsNoTracking()
            .Where(x => x.Status == MaterialCostBookRevisionStatuses.Published &&
                x.EffectiveDate <= asOfDate);
        if (pattern is not null)
        {
            query = query.Where(x => EF.Functions.ILike(
                    x.MaterialCostBook.BookNumber, pattern, "\\") ||
                EF.Functions.ILike(x.NameFa, pattern, "\\") ||
                (x.NameEn != null && EF.Functions.ILike(x.NameEn, pattern, "\\")));
        }

        return await query
            .OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.Revision)
            .ThenBy(x => x.MaterialCostBook.BookNumber)
            .Skip(checked((page - 1) * pageSize)).Take(pageSize)
            .Select(x => new MaterialCostBookSelectionOption(
                x.MaterialCostBookId, x.MaterialCostBook.BookNumber, x.Id, x.Revision,
                x.NameFa, x.NameEn, x.EffectiveDate, x.PublishedAtUtc!.Value, x.Lines.Count))
            .ToArrayAsync(ct);
    }

    public async Task<MaterialCostBookRevisionResponse> Create(
        CreateMaterialCostBookRequest request, CancellationToken ct)
    {
        var nameFa = RequiredText(request.NameFa, 250, "nameFa");
        var nameEn = OptionalText(request.NameEn, 250, "nameEn");
        var reason = OptionalText(request.ChangeReason, 1000, "changeReason");
        ValidateDate(request.EffectiveDate);
        ValidateClientRequestId(request.ClientRequestId);
        var fingerprint = Fingerprint("CREATE", nameFa, nameEn,
            request.EffectiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), reason);

        await using var db = await factory.CreateDbContextAsync(ct);
        var replay = await RevisionQuery(db, false)
            .SingleOrDefaultAsync(x => x.ClientRequestId == request.ClientRequestId, ct);
        if (replay is not null) return EnsureCreateReplay(replay, fingerprint);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var now = UtcNow();
        var book = new MaterialCostBook(await NextBookNumber(db, ct), Actor(), now);
        var revision = new MaterialCostBookRevision(book.Id, 1, nameFa, nameEn,
            request.EffectiveDate, null, request.ClientRequestId, fingerprint, reason,
            Actor(), now);
        db.MaterialCostBooks.Add(book);
        db.MaterialCostBookRevisions.Add(revision);
        AppendAudit(db, revision.Id, "CREATE", null, RevisionSnapshot(revision, book.BookNumber),
            reason, now);
        await SaveAndCommit(db, transaction, ct);
        return await LoadRequired(revision.Id, ct);
    }

    public async Task<MaterialCostBookRevisionResponse> UpdateDraft(Guid revisionId,
        UpdateMaterialCostBookDraftRequest request, CancellationToken ct)
    {
        var nameFa = RequiredText(request.NameFa, 250, "nameFa");
        var nameEn = OptionalText(request.NameEn, 250, "nameEn");
        var reason = OptionalText(request.ChangeReason, 1000, "changeReason");
        ValidateDate(request.EffectiveDate);
        ValidateMutation(request.ExpectedVersion, request.ClientRequestId);
        var fingerprint = Fingerprint("UPDATE_DRAFT", revisionId.ToString("N"),
            request.ExpectedVersion.ToString(CultureInfo.InvariantCulture), nameFa, nameEn,
            request.EffectiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), reason);

        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var replay = await FindOperation(db, MaterialCostBookOperationTypes.UpdateDraft,
            request.ClientRequestId, ct);
        if (replay is not null)
            return await ReplayRevision(db, replay, revisionId, fingerprint, ct);
        var entity = await RevisionQuery(db, true).SingleOrDefaultAsync(x => x.Id == revisionId, ct)
            ?? throw new KeyNotFoundException();
        EnsureDraft(entity);
        EnsureVersion(entity.Version, request.ExpectedVersion);
        if (entity.SupersedesRevisionId.HasValue && entity.EffectiveDate != request.EffectiveDate)
            throw Validation("effectiveDate", "تاریخ اثر نسخه اصلاحی قابل تغییر نیست.");
        var before = RevisionSnapshot(entity, entity.MaterialCostBook.BookNumber);
        var now = UtcNow();
        entity.UpdateDraft(nameFa, nameEn, request.EffectiveDate, reason, Actor(), now);
        AddOperation(db, entity, MaterialCostBookOperationTypes.UpdateDraft,
            request.ClientRequestId, fingerprint, now);
        AppendAudit(db, entity.Id, "UPDATE_DRAFT", before,
            RevisionSnapshot(entity, entity.MaterialCostBook.BookNumber), reason, now);
        await SaveAndCommit(db, transaction, ct);
        return await LoadRequired(entity.Id, ct);
    }

    public async Task<MaterialCostBookLineMutationResponse> UpsertLine(Guid revisionId,
        Guid itemId, UpsertMaterialCostBookLineRequest request, CancellationToken ct)
    {
        ValidateMutation(request.ExpectedVersion, request.ClientRequestId);
        ValidatePositive(request.OriginalPriceAmount, "originalPriceAmount");
        ValidatePositive(request.PriceBasisQuantity, "priceBasisQuantity");
        var sourceReference = OptionalText(request.SourceReference, 250, "sourceReference");
        var notes = OptionalText(request.Notes, 1000, "notes");
        var fingerprint = Fingerprint("UPSERT_LINE", revisionId.ToString("N"),
            itemId.ToString("N"), request.ExpectedVersion.ToString(CultureInfo.InvariantCulture),
            request.CurrencyId.ToString("N"), Canonical(request.OriginalPriceAmount),
            Canonical(request.PriceBasisQuantity), request.PriceUnitId.ToString("N"),
            request.SupplierId?.ToString("N"), sourceReference, notes);

        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var replay = await FindOperation(db, MaterialCostBookOperationTypes.UpsertLine,
            request.ClientRequestId, ct);
        if (replay is not null)
            return await ReplayLine(db, replay, revisionId, itemId, fingerprint, ct);
        var revision = await RevisionQuery(db, true).SingleOrDefaultAsync(x => x.Id == revisionId, ct)
            ?? throw new KeyNotFoundException();
        EnsureDraft(revision);
        EnsureVersion(revision.Version, request.ExpectedVersion);

        var item = await db.Items.Include(x => x.BaseUnit)
            .SingleOrDefaultAsync(x => x.Id == itemId && x.IsActive, ct)
            ?? throw Validation("itemId", "قلم فعال پیدا نشد.");
        if (!item.BaseUnit.IsActive)
            throw Validation("priceUnitId",
                "واحد پایه قلم غیرفعال است؛ ابتدا واحد پایه قلم را فعال یا اصلاح کنید.");
        var currency = await db.Currencies.SingleOrDefaultAsync(x =>
            x.Id == request.CurrencyId && x.IsActive, ct)
            ?? throw Validation("currencyId", "ارز فعال پیدا نشد.");
        var (priceUnit, factor) = await ResolvePriceUnit(db, item.Id, item.BaseUnitId,
            request.PriceUnitId, ct);
        var supplier = request.SupplierId.HasValue
            ? await db.Suppliers.SingleOrDefaultAsync(x =>
                x.Id == request.SupplierId.Value && x.IsActive, ct)
            : null;
        if (request.SupplierId.HasValue && supplier is null)
            throw Validation("supplierId", "تأمین‌کننده فعال پیدا نشد.");
        ValidateNormalizedPrice(request.OriginalPriceAmount, request.PriceBasisQuantity, factor);

        var existing = await db.MaterialCostBookLines.SingleOrDefaultAsync(x =>
            x.MaterialCostBookRevisionId == revisionId && x.ItemId == itemId, ct);
        var before = existing is null ? null : LineSnapshot(existing);
        var now = UtcNow();
        if (existing is null)
        {
            existing = new MaterialCostBookLine(revision.Id, item.Id, item.BusinessNumber,
                item.NameFa, item.NameEn, currency.Id, currency.Code, currency.NameFa,
                currency.NameEn, currency.DecimalPrecision, request.OriginalPriceAmount,
                request.PriceBasisQuantity,
                priceUnit.Id, priceUnit.Code, priceUnit.NameFa, priceUnit.NameEn, factor,
                item.BaseUnitId, item.BaseUnit.Code, item.BaseUnit.NameFa, item.BaseUnit.NameEn,
                supplier?.Id, supplier?.SupplierNumber, supplier?.Name, sourceReference, notes, now);
            db.MaterialCostBookLines.Add(existing);
        }
        else
        {
            existing.Edit(currency.Id, currency.Code, currency.NameFa, currency.NameEn,
                currency.DecimalPrecision,
                request.OriginalPriceAmount, request.PriceBasisQuantity, priceUnit.Id,
                priceUnit.Code, priceUnit.NameFa, priceUnit.NameEn, factor,
                item.BaseUnitId, item.BaseUnit.Code, item.BaseUnit.NameFa, item.BaseUnit.NameEn,
                supplier?.Id, supplier?.SupplierNumber, supplier?.Name, sourceReference, notes, now);
        }
        revision.TouchDraft(Actor(), now);
        AddOperation(db, revision, MaterialCostBookOperationTypes.UpsertLine,
            request.ClientRequestId, fingerprint, now);
        AppendAudit(db, existing.Id, before is null ? "ADD_LINE" : "UPDATE_LINE", before,
            LineSnapshot(existing), null, now, "MaterialCostBookLine");
        await SaveAndCommit(db, transaction, ct);
        var line = await LoadRequiredLine(revisionId, itemId, ct);
        return new(line, revision.Version);
    }

    public async Task<MaterialCostBookRevisionResponse> RemoveLine(Guid revisionId,
        Guid itemId, RemoveMaterialCostBookLineRequest request, CancellationToken ct)
    {
        ValidateMutation(request.ExpectedVersion, request.ClientRequestId);
        var reason = RequiredText(request.Reason, 1000, "reason");
        var fingerprint = Fingerprint("REMOVE_LINE", revisionId.ToString("N"),
            itemId.ToString("N"), request.ExpectedVersion.ToString(CultureInfo.InvariantCulture),
            reason);
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var replay = await FindOperation(db, MaterialCostBookOperationTypes.RemoveLine,
            request.ClientRequestId, ct);
        if (replay is not null)
            return await ReplayRevision(db, replay, revisionId, fingerprint, ct);
        var revision = await RevisionQuery(db, true).SingleOrDefaultAsync(x => x.Id == revisionId, ct)
            ?? throw new KeyNotFoundException();
        EnsureDraft(revision);
        EnsureVersion(revision.Version, request.ExpectedVersion);
        var line = await db.MaterialCostBookLines.SingleOrDefaultAsync(x =>
            x.MaterialCostBookRevisionId == revisionId && x.ItemId == itemId, ct)
            ?? throw new KeyNotFoundException();
        var before = LineSnapshot(line);
        var now = UtcNow();
        db.MaterialCostBookLines.Remove(line);
        revision.TouchDraft(Actor(), now);
        AddOperation(db, revision, MaterialCostBookOperationTypes.RemoveLine,
            request.ClientRequestId, fingerprint, now);
        AppendAudit(db, line.Id, "REMOVE_LINE", before, null, reason, now,
            "MaterialCostBookLine");
        await SaveAndCommit(db, transaction, ct);
        return await LoadRequired(revision.Id, ct);
    }

    public async Task<MaterialCostBookRevisionResponse> Publish(Guid revisionId,
        PublishMaterialCostBookRequest request, CancellationToken ct)
    {
        ValidateMutation(request.ExpectedVersion, request.ClientRequestId);
        var reason = RequiredText(request.Reason, 1000, "reason");
        var fingerprint = Fingerprint("PUBLISH", revisionId.ToString("N"),
            request.ExpectedVersion.ToString(CultureInfo.InvariantCulture), reason);
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var replay = await FindOperation(db, MaterialCostBookOperationTypes.Publish,
            request.ClientRequestId, ct);
        if (replay is not null)
            return await ReplayRevision(db, replay, revisionId, fingerprint, ct);
        var revision = await RevisionQuery(db, true).SingleOrDefaultAsync(x => x.Id == revisionId, ct)
            ?? throw new KeyNotFoundException();
        EnsureDraft(revision);
        EnsureVersion(revision.Version, request.ExpectedVersion);
        await ValidatePublishable(db, revision, ct);
        var before = RevisionSnapshot(revision, revision.MaterialCostBook.BookNumber);
        var now = UtcNow();
        revision.Publish(reason, Actor(), now);
        AddOperation(db, revision, MaterialCostBookOperationTypes.Publish,
            request.ClientRequestId, fingerprint, now);
        AppendAudit(db, revision.Id, "PUBLISH", before,
            RevisionSnapshot(revision, revision.MaterialCostBook.BookNumber), reason, now);
        await SaveAndCommit(db, transaction, ct);
        return await LoadRequired(revision.Id, ct);
    }

    public async Task<MaterialCostBookRevisionResponse> CreateCorrection(Guid sourceRevisionId,
        CreateMaterialCostBookCorrectionRequest request, CancellationToken ct)
    {
        ValidateMutation(request.ExpectedVersion, request.ClientRequestId);
        var reason = RequiredText(request.Reason, 1000, "reason");
        var fingerprint = Fingerprint("CORRECTION", sourceRevisionId.ToString("N"),
            request.ExpectedVersion.ToString(CultureInfo.InvariantCulture), reason);
        await using var db = await factory.CreateDbContextAsync(ct);
        var existing = await RevisionQuery(db, false)
            .SingleOrDefaultAsync(x => x.ClientRequestId == request.ClientRequestId, ct);
        if (existing is not null) return EnsureCreateReplay(existing, fingerprint);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var source = await RevisionQuery(db, false)
            .SingleOrDefaultAsync(x => x.Id == sourceRevisionId, ct)
            ?? throw new KeyNotFoundException();
        if (source.Status != MaterialCostBookRevisionStatuses.Published)
            throw Conflict("CORRECTION_SOURCE_NOT_PUBLISHED",
                "فقط از نسخه منتشرشده می‌توان نسخه اصلاحی ساخت.");
        EnsureVersion(source.Version, request.ExpectedVersion);
        if (await db.MaterialCostBookRevisions.AnyAsync(x =>
            x.MaterialCostBookId == source.MaterialCostBookId &&
            x.Status == MaterialCostBookRevisionStatuses.Draft, ct))
            throw Conflict("DRAFT_ALREADY_EXISTS", "برای این دفتر بها یک پیش‌نویس باز وجود دارد.");
        var nextRevision = await db.MaterialCostBookRevisions
            .Where(x => x.MaterialCostBookId == source.MaterialCostBookId)
            .MaxAsync(x => x.Revision, ct) + 1;
        var now = UtcNow();
        var correction = new MaterialCostBookRevision(source.MaterialCostBookId,
            nextRevision, source.NameFa, source.NameEn, source.EffectiveDate, source.Id,
            request.ClientRequestId, fingerprint, reason, Actor(), now);
        db.MaterialCostBookRevisions.Add(correction);
        foreach (var line in source.Lines)
        {
            db.MaterialCostBookLines.Add(new MaterialCostBookLine(correction.Id,
                line.ItemId, line.ItemCode, line.ItemNameFa, line.ItemNameEn,
                line.CurrencyId, line.CurrencyCode, line.CurrencyNameFa, line.CurrencyNameEn,
                line.CurrencyDecimalPrecision,
                line.OriginalPriceAmount, line.PriceBasisQuantity, line.PriceUnitId,
                line.PriceUnitCode, line.PriceUnitNameFa, line.PriceUnitNameEn,
                line.PriceUnitFactorToBase, line.ItemBaseUnitId, line.ItemBaseUnitCode,
                line.ItemBaseUnitNameFa, line.ItemBaseUnitNameEn, line.SupplierId,
                line.SupplierNumber, line.SupplierName, line.SourceReference, line.Notes, now));
        }
        AppendAudit(db, correction.Id, "CREATE_CORRECTION", null,
            RevisionSnapshot(correction, source.MaterialCostBook.BookNumber), reason, now);
        await SaveAndCommit(db, transaction, ct);
        return await LoadRequired(correction.Id, ct);
    }

    private static async Task<(Hadron.Erp.Api.Modules.ReferenceData.Domain.Unit Unit, decimal Factor)> ResolvePriceUnit(
        HadronDbContext db, Guid itemId, Guid baseUnitId, Guid priceUnitId, CancellationToken ct)
    {
        if (priceUnitId == baseUnitId)
        {
            var baseUnit = await db.Units.SingleOrDefaultAsync(x => x.Id == baseUnitId && x.IsActive, ct)
                ?? throw Validation("priceUnitId", "واحد پایه فعال پیدا نشد.");
            return (baseUnit, 1m);
        }
        var conversion = await db.InventoryItemUnitConversions
            .Include(x => x.TransactionUnit)
            .SingleOrDefaultAsync(x => x.ItemId == itemId &&
                x.TransactionUnitId == priceUnitId && x.BaseUnitId == baseUnitId &&
                x.IsActive && x.TransactionUnit.IsActive, ct)
            ?? throw Validation("priceUnitId",
                "واحد قیمت باید واحد پایه یا یکی از تبدیل‌های فعال همین قلم باشد.");
        return (conversion.TransactionUnit, conversion.FactorToBase);
    }

    private static async Task ValidatePublishable(HadronDbContext db,
        MaterialCostBookRevision revision, CancellationToken ct)
    {
        var itemIds = revision.Lines.Select(x => x.ItemId).ToArray();
        var items = await db.Items.AsNoTracking().Where(x => itemIds.Contains(x.Id))
            .Select(x => new { x.Id, x.BaseUnitId, x.IsActive, BaseUnitIsActive = x.BaseUnit.IsActive })
            .ToDictionaryAsync(x => x.Id, ct);
        var currencyIds = revision.Lines.Select(x => x.CurrencyId).Distinct().ToArray();
        var activeCurrencyIds = await db.Currencies.AsNoTracking()
            .Where(x => currencyIds.Contains(x.Id) && x.IsActive)
            .Select(x => x.Id).ToHashSetAsync(ct);
        var alternateLines = revision.Lines.Where(x => x.PriceUnitId != x.ItemBaseUnitId).ToArray();
        var conversionItemIds = alternateLines.Select(x => x.ItemId).Distinct().ToArray();
        var conversions = await db.InventoryItemUnitConversions.AsNoTracking()
            .Where(x => conversionItemIds.Contains(x.ItemId) && x.IsActive &&
                x.TransactionUnit.IsActive)
            .ToArrayAsync(ct);
        foreach (var line in revision.Lines)
        {
            if (!items.TryGetValue(line.ItemId, out var item) || !item.IsActive)
                throw Conflict("ITEM_INACTIVE",
                    $"قلم {line.ItemCode} غیرفعال یا حذف شده است؛ پیش از انتشار خط بها را اصلاح کنید.");
            if (!item.BaseUnitIsActive)
                throw Conflict("ITEM_BASE_UNIT_INACTIVE",
                    $"واحد پایه قلم {line.ItemCode} غیرفعال شده است؛ پیش از انتشار خط بها را اصلاح کنید.");
            if (item.BaseUnitId != line.ItemBaseUnitId)
                throw Conflict("ITEM_BASE_UNIT_CHANGED",
                    $"واحد پایه قلم {line.ItemCode} پس از ثبت خط بها تغییر کرده است؛ خط را دوباره ثبت کنید.");
            if (!activeCurrencyIds.Contains(line.CurrencyId))
                throw Conflict("CURRENCY_INACTIVE",
                    $"ارز {line.CurrencyCode} غیرفعال شده است؛ پیش از انتشار خط بها را اصلاح کنید.");
            if (line.PriceUnitId == line.ItemBaseUnitId)
            {
                if (line.PriceUnitFactorToBase != 1m)
                    throw Conflict("PRICE_UNIT_CONVERSION_CHANGED",
                        $"ضریب واحد پایه قلم {line.ItemCode} نامعتبر است؛ خط را دوباره ثبت کنید.");
                continue;
            }
            var conversion = conversions.SingleOrDefault(x => x.ItemId == line.ItemId &&
                x.TransactionUnitId == line.PriceUnitId && x.BaseUnitId == line.ItemBaseUnitId);
            if (conversion is null || conversion.FactorToBase != line.PriceUnitFactorToBase)
                throw Conflict("PRICE_UNIT_CONVERSION_CHANGED",
                    $"تبدیل واحد قیمت قلم {line.ItemCode} تغییر کرده یا غیرفعال شده است؛ خط را دوباره ثبت کنید.");
        }
    }

    private static IQueryable<MaterialCostBookRevision> RevisionQuery(HadronDbContext db,
        bool tracking)
    {
        var query = db.MaterialCostBookRevisions
            .Include(x => x.MaterialCostBook).Include(x => x.Lines).AsQueryable();
        return tracking ? query : query.AsNoTracking();
    }

    private async Task<MaterialCostBookRevisionResponse> LoadRequired(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await RevisionQuery(db, false).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException("Saved material cost-book revision could not be reloaded.");
        return ToResponse(entity);
    }

    private async Task<MaterialCostBookLineResponse> LoadRequiredLine(Guid revisionId,
        Guid itemId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var line = await db.MaterialCostBookLines.AsNoTracking().SingleOrDefaultAsync(x =>
            x.MaterialCostBookRevisionId == revisionId && x.ItemId == itemId, ct)
            ?? throw new InvalidOperationException("Saved material cost-book line could not be reloaded.");
        return ToResponse(line);
    }

    private static MaterialCostBookRevisionResponse ToResponse(MaterialCostBookRevision x) => new(
        x.MaterialCostBookId, x.MaterialCostBook.BookNumber, x.Id, x.Revision,
        x.NameFa, x.NameEn, x.EffectiveDate, x.Status, x.SupersedesRevisionId,
        x.ClientRequestId, x.ChangeReason, x.CreatedByIdentifier, x.CreatedAtUtc,
        x.UpdatedByIdentifier, x.UpdatedAtUtc, x.PublishedByIdentifier, x.PublishedAtUtc,
        x.PublishReason, x.Lines.Count, x.Version);

    private static MaterialCostBookLineResponse ToResponse(MaterialCostBookLine x) => new(
        x.Id, x.ItemId, x.ItemCode, x.ItemNameFa, x.ItemNameEn, x.CurrencyId,
        x.CurrencyCode, x.CurrencyNameFa, x.CurrencyNameEn, x.CurrencyDecimalPrecision,
        x.OriginalPriceAmount,
        x.PriceBasisQuantity, x.PriceUnitId, x.PriceUnitCode, x.PriceUnitNameFa,
        x.PriceUnitNameEn, x.PriceUnitFactorToBase, x.ItemBaseUnitId, x.ItemBaseUnitCode,
        x.ItemBaseUnitNameFa, x.ItemBaseUnitNameEn, x.NormalizedUnitPrice, x.SupplierId,
        x.SupplierNumber, x.SupplierName, x.SourceReference, x.Notes,
        x.CreatedAtUtc, x.UpdatedAtUtc);

    private static MaterialCostBookRevisionResponse EnsureCreateReplay(
        MaterialCostBookRevision entity, string fingerprint)
    {
        if (entity.RequestFingerprint != fingerprint)
            throw Conflict("IDEMPOTENCY_KEY_REUSED",
                "شناسه درخواست قبلاً برای درخواست دیگری استفاده شده است.");
        if (entity.Version != 1)
            throw Conflict("IDEMPOTENT_RESULT_SUPERSEDED",
                "نتیجه درخواست قبلی با تغییر جدیدتری جایگزین شده است.",
                new { resultVersion = 1, currentVersion = entity.Version });
        return ToResponse(entity);
    }

    private static async Task<MaterialCostBookOperationReceipt?> FindOperation(HadronDbContext db,
        string type, Guid clientRequestId, CancellationToken ct) =>
        await db.MaterialCostBookOperationReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
            x.OperationType == type && x.ClientRequestId == clientRequestId, ct);

    private static void AddOperation(HadronDbContext db, MaterialCostBookRevision revision,
        string type, Guid clientRequestId, string fingerprint, DateTime now) =>
        db.MaterialCostBookOperationReceipts.Add(new MaterialCostBookOperationReceipt(
            revision.Id, type, clientRequestId, fingerprint, revision.Version, now));

    private static async Task<MaterialCostBookRevisionResponse> ReplayRevision(HadronDbContext db,
        MaterialCostBookOperationReceipt receipt, Guid revisionId, string fingerprint,
        CancellationToken ct)
    {
        EnsureReplay(receipt, revisionId, fingerprint);
        var revision = await RevisionQuery(db, false).SingleAsync(x => x.Id == revisionId, ct);
        EnsureReplayVersion(receipt, revision.Version);
        return ToResponse(revision);
    }

    private static async Task<MaterialCostBookLineMutationResponse> ReplayLine(HadronDbContext db,
        MaterialCostBookOperationReceipt receipt, Guid revisionId, Guid itemId,
        string fingerprint, CancellationToken ct)
    {
        EnsureReplay(receipt, revisionId, fingerprint);
        var revision = await db.MaterialCostBookRevisions.AsNoTracking()
            .SingleAsync(x => x.Id == revisionId, ct);
        EnsureReplayVersion(receipt, revision.Version);
        var line = await db.MaterialCostBookLines.AsNoTracking().SingleAsync(x =>
            x.MaterialCostBookRevisionId == revisionId && x.ItemId == itemId, ct);
        return new(ToResponse(line), revision.Version);
    }

    private static void EnsureReplay(MaterialCostBookOperationReceipt receipt,
        Guid revisionId, string fingerprint)
    {
        if (receipt.MaterialCostBookRevisionId != revisionId || receipt.RequestFingerprint != fingerprint)
            throw Conflict("IDEMPOTENCY_KEY_REUSED",
                "شناسه درخواست قبلاً برای درخواست دیگری استفاده شده است.");
    }

    private static void EnsureReplayVersion(MaterialCostBookOperationReceipt receipt,
        long currentVersion)
    {
        if (receipt.ResultVersion != currentVersion)
            throw Conflict("IDEMPOTENT_RESULT_SUPERSEDED",
                "نتیجه درخواست قبلی با تغییر جدیدتری جایگزین شده است.",
                new { resultVersion = receipt.ResultVersion, currentVersion });
    }

    private static async Task<string> NextBookNumber(HadronDbContext db, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);
        var transaction = db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;
        await using var command = new NpgsqlCommand(
            "SELECT nextval('cash_flow.material_cost_book_number_seq')", connection, transaction);
        var value = (long)(await command.ExecuteScalarAsync(ct))!;
        return $"MCB-{value:D6}";
    }

    private static async Task SaveAndCommit(HadronDbContext db,
        IDbContextTransaction transaction, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw Conflict("STALE_VERSION",
                "نسخه ارسالی قدیمی است. اطلاعات را دوباره بارگذاری کنید.",
                new { entries = ex.Entries.Select(x => x.Metadata.ClrType.Name).ToArray() });
        }
        catch (DbUpdateException ex) when ((ex.InnerException as PostgresException ??
            ex.GetBaseException() as PostgresException)?.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw Conflict("MATERIAL_COST_BOOK_CONFLICT",
                "داده هم‌زمان تغییر کرده یا مقدار یکتا تکرار شده است؛ اطلاعات را تازه کنید.");
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            throw Conflict("CONCURRENT_CHANGE",
                "تغییر هم‌زمان تشخیص داده شد. اطلاعات را دوباره بارگذاری کنید.");
        }
    }

    private void AppendAudit(HadronDbContext db, Guid entityId, string action,
        IReadOnlyDictionary<string, object?>? before, IReadOnlyDictionary<string, object?>? after,
        string? reason, DateTime timestamp, string entityType = "MaterialCostBookRevision") =>
        db.OperationalAuditEvents.Add(OperationalAuditEvent.From(new AuditEvent(
            entityType, entityId, action, timestamp, Actor(),
            AuditEventContext.CorrelationId(httpContextAccessor.HttpContext), before, after, reason)));

    private static IReadOnlyDictionary<string, object?> RevisionSnapshot(
        MaterialCostBookRevision x, string bookNumber) => new Dictionary<string, object?>
    {
        ["BookId"] = x.MaterialCostBookId,
        ["BookNumber"] = bookNumber,
        ["Revision"] = x.Revision,
        ["NameFa"] = x.NameFa,
        ["NameEn"] = x.NameEn,
        ["EffectiveDate"] = x.EffectiveDate,
        ["Status"] = x.Status,
        ["SupersedesRevisionId"] = x.SupersedesRevisionId,
        ["Version"] = x.Version,
        ["LineCount"] = x.Lines.Count
    };

    private static IReadOnlyDictionary<string, object?> LineSnapshot(MaterialCostBookLine x) =>
        new Dictionary<string, object?>
        {
            ["RevisionId"] = x.MaterialCostBookRevisionId,
            ["ItemId"] = x.ItemId,
            ["ItemCode"] = x.ItemCode,
            ["CurrencyId"] = x.CurrencyId,
            ["CurrencyCode"] = x.CurrencyCode,
            ["CurrencyDecimalPrecision"] = x.CurrencyDecimalPrecision,
            ["OriginalPriceAmount"] = x.OriginalPriceAmount,
            ["PriceBasisQuantity"] = x.PriceBasisQuantity,
            ["PriceUnitId"] = x.PriceUnitId,
            ["PriceUnitCode"] = x.PriceUnitCode,
            ["PriceUnitFactorToBase"] = x.PriceUnitFactorToBase,
            ["ItemBaseUnitId"] = x.ItemBaseUnitId,
            ["ItemBaseUnitCode"] = x.ItemBaseUnitCode,
            ["NormalizedUnitPrice"] = x.NormalizedUnitPrice,
            ["SupplierId"] = x.SupplierId,
            ["SupplierNumber"] = x.SupplierNumber,
            ["SourceReference"] = x.SourceReference,
            ["Notes"] = x.Notes
        };

    private static void EnsureDraft(MaterialCostBookRevision x)
    {
        if (x.Status != MaterialCostBookRevisionStatuses.Draft)
            throw Conflict("MATERIAL_COST_BOOK_IMMUTABLE",
                "نسخه منتشرشده دفتر بهای مواد قابل ویرایش نیست.");
    }

    private static void EnsureVersion(long current, long expected)
    {
        if (current != expected)
            throw Conflict("STALE_VERSION",
                "نسخه ارسالی قدیمی است. اطلاعات را دوباره بارگذاری کنید.",
                new { currentVersion = current, expectedVersion = expected });
    }

    private static void ValidateMutation(long version, Guid clientRequestId)
    {
        if (version < 1) throw Validation("expectedVersion", "نسخه معتبر الزامی است.");
        ValidateClientRequestId(clientRequestId);
    }

    private static void ValidateClientRequestId(Guid value)
    {
        if (value == Guid.Empty)
            throw Validation("clientRequestId", "شناسه یکتای درخواست الزامی است.");
    }

    private static void ValidateDate(DateOnly value)
    {
        if (value == default) throw Validation("effectiveDate", "تاریخ اثر معتبر الزامی است.");
    }

    private static void ValidatePositive(decimal value, string field)
    {
        if (value <= 0) throw Validation(field, "مقدار باید بزرگ‌تر از صفر باشد.");
        if (value >= Numeric28Scale12UpperBound || DecimalScale(value) > 12)
            throw Validation(field, "مقدار از دقت مجاز numeric(28,12) خارج است.");
    }

    private static void ValidateNormalizedPrice(decimal amount, decimal basis, decimal factor)
    {
        try
        {
            var denominator = checked(basis * factor);
            if (denominator <= 0) throw new OverflowException();
            var normalized = amount / denominator;
            if (normalized <= 0 || normalized >= Numeric28Scale12UpperBound)
                throw new OverflowException();
        }
        catch (OverflowException)
        {
            throw Validation("priceConversion",
                "قیمت نرمال‌شده از محدوده عددی ایمن خارج است؛ مقدار و ضریب تبدیل را بررسی کنید.");
        }
    }

    private static string RequiredText(string? value, int maximum, string field)
    {
        var clean = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (clean is null) throw Validation(field, "مقدار الزامی است.");
        if (clean.Length > maximum) throw Validation(field, $"حداکثر {maximum} نویسه مجاز است.");
        return clean;
    }

    private static string? OptionalText(string? value, int maximum, string field)
    {
        var clean = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (clean?.Length > maximum) throw Validation(field, $"حداکثر {maximum} نویسه مجاز است.");
        return clean;
    }

    private static string? SearchPattern(string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return null;
        var value = search.Trim();
        if (value.Length > 200)
            throw Validation("search", "عبارت جست‌وجو حداکثر ۲۰۰ نویسه است.");
        value = value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{value}%";
    }

    private static string? NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;
        var value = status.Trim().ToUpperInvariant();
        if (value is not (MaterialCostBookRevisionStatuses.Draft or
            MaterialCostBookRevisionStatuses.Published))
            throw Validation("status", "وضعیت دفتر بها معتبر نیست.");
        return value;
    }

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > MaximumPageSize ||
            ((long)page - 1) * pageSize > int.MaxValue)
            throw Validation("paging", $"page باید مثبت و pageSize بین ۱ و {MaximumPageSize} باشد.");
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
    private string? Actor() => AuditEventContext.ActorId(httpContextAccessor.HttpContext);
    private static int DecimalScale(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0x7F;
    private static string Canonical(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);
    private static string Fingerprint(params string?[] fields)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(fields.Length);
        foreach (var field in fields)
        {
            if (field is null)
            {
                writer.Write(-1);
                continue;
            }

            var bytes = Encoding.UTF8.GetBytes(field);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }
    private static MaterialCostBookValidationException Validation(string key, string message) =>
        new(new Dictionary<string, string[]> { [key] = [message] });
    private static MaterialCostBookConflictException Conflict(string code, string message,
        object? details = null) => new(code, message, details);
}
