using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Hadron.Erp.Api.Modules.Sourcing.Domain;
using Hadron.Erp.Api.Platform.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Hadron.Erp.Api.Modules.Sourcing.Application;

public sealed class SourcingService(IDbContextFactory<HadronDbContext> factory, IAuditEventSink auditSink,
    IHttpContextAccessor accessor, ILogger<SourcingService> logger)
{
    private static readonly string[] ExternallySourcedMethods = [SupplyMethods.Buy, SupplyMethods.Outsource, SupplyMethods.Hybrid];

    public async Task<SourcingOptionListResponse> List(DateOnly evaluationDate, string? search, bool includeInactive,
        Guid? itemId, Guid? supplierId, string? itemType, bool? preferred, string? readiness, int page, int pageSize,
        CancellationToken ct)
    {
        ValidatePaging(page, pageSize);
        if (readiness is not null && !new[] { SourcingReadiness.Inactive, SourcingReadiness.Blocked,
                SourcingReadiness.IncompleteData, SourcingReadiness.Ready }.Contains(readiness))
            throw Validation("readiness", "وضعیت آمادگی معتبر نیست.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.SourcingOptions.AsNoTracking().Where(x => includeInactive || x.IsActive);
        if (itemId.HasValue) query = query.Where(x => x.ItemId == itemId);
        if (supplierId.HasValue) query = query.Where(x => x.SupplierId == supplierId);
        if (!string.IsNullOrWhiteSpace(itemType)) query = query.Where(x => x.Item.ItemType == itemType);
        if (preferred.HasValue) query = query.Where(x => x.IsPreferred == preferred);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.SourcingNumber.Contains(term) || x.Item.BusinessNumber.Contains(term) ||
                x.Item.NameFa.Contains(term) ||
                x.Supplier.SupplierNumber.Contains(term) || x.Supplier.Name.Contains(term));
        }

        var blocked = query.Where(x => x.IsActive && (!x.Item.IsActive || !x.Supplier.IsActive ||
            x.ItemBaseUnitId != x.Item.BaseUnitId ||
            !db.MaterialItemProfiles.Any(p => p.ItemId == x.ItemId && ExternallySourcedMethods.Contains(p.SupplyMethod)) ||
            (x.ShippingModeId != null && !x.ShippingMode!.IsActive) ||
            (x.IncotermId != null && !x.Incoterm!.IsActive) ||
            x.Prices.Any(p => p.EffectiveFrom <= evaluationDate && (p.EffectiveTo == null || p.EffectiveTo >= evaluationDate) && !p.Currency.IsActive)));
        var incomplete = query.Where(x => x.IsActive && !blocked.Any(b => b.Id == x.Id) &&
            (x.MinimumOrderQuantity == null || x.ShippingModeId == null ||
             x.SupplierLeadTimeDays == null || x.ForwarderToShipmentDays == null || x.ShippingPeriodDays == null ||
             x.DestinationCustomsDays == null ||
             !x.Prices.Any(p => p.EffectiveFrom <= evaluationDate && (p.EffectiveTo == null || p.EffectiveTo >= evaluationDate) && p.Currency.IsActive)));
        if (readiness == SourcingReadiness.Inactive) query = query.Where(x => !x.IsActive);
        else if (readiness == SourcingReadiness.Blocked) query = query.Where(x => blocked.Any(b => b.Id == x.Id));
        else if (readiness == SourcingReadiness.IncompleteData) query = query.Where(x => incomplete.Any(b => b.Id == x.Id));
        else if (readiness == SourcingReadiness.Ready) query = query.Where(x => x.IsActive &&
            !blocked.Any(b => b.Id == x.Id) && !incomplete.Any(b => b.Id == x.Id));
        var total = await query.CountAsync(ct);
        var loaded = await LoadQuery(query).OrderBy(x => x.SourcingNumber).Skip((page - 1) * pageSize)
            .Take(pageSize).ToListAsync(ct);
        var responses = await ToResponses(db, loaded, evaluationDate, false, ct);
        return new(responses, page, pageSize, total, evaluationDate);
    }

    public async Task<SourcingOptionResponse?> Get(Guid id, DateOnly evaluationDate, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var option = await LoadQuery(db.SourcingOptions.AsNoTracking()).SingleOrDefaultAsync(x => x.Id == id, ct);
        return option is null ? null : (await ToResponses(db, [option], evaluationDate, true, ct))[0];
    }

    public async Task<SourcingOptionResponse> Create(CreateSourcingOptionRequest request, DateOnly evaluationDate,
        CancellationToken ct)
    {
        ValidateCommercial(request.MinimumOrderQuantity, request.SupplierLeadTimeDays,
            request.ForwarderToShipmentDays, request.ShippingPeriodDays, request.DestinationCustomsDays, null);
        await using var db = await factory.CreateDbContextAsync(ct);
        var item = await db.Items.Include(x => x.BaseUnit).SingleOrDefaultAsync(x => x.Id == request.ItemId, ct)
            ?? throw Validation("itemId", "قلم پیدا نشد.");
        var profile = await db.MaterialItemProfiles.SingleOrDefaultAsync(x => x.ItemId == item.Id, ct)
            ?? throw Validation("itemId", "قلم باید Part یا Component باشد.");
        if (item.ItemType is not (ItemTypes.Part or ItemTypes.Component) || !item.IsActive)
            throw Validation("itemId", "قلم Part/Component فعال الزامی است.");
        if (!ExternallySourcedMethods.Contains(profile.SupplyMethod))
            throw Validation("itemId", "روش تأمین قلم با تأمین بیرونی سازگار نیست.");
        if (!await db.Suppliers.AnyAsync(x => x.Id == request.SupplierId && x.IsActive, ct))
            throw Validation("supplierId", "تأمین‌کننده فعال الزامی است.");
        await ValidateAssignedReferences(db, request.ShippingModeId, request.IncotermId, true, true, ct);
        var option = new SourcingOption(await NextNumber(db, ct), item.Id, request.SupplierId, item.BaseUnitId,
            request.MinimumOrderQuantity, request.ShippingModeId, request.IncotermId, request.SupplierLeadTimeDays,
            request.ForwarderToShipmentDays, request.ShippingPeriodDays, request.DestinationCustomsDays);
        db.SourcingOptions.Add(option);
        await Save(db, ct);
        var response = (await Get(option.Id, evaluationDate, ct))!;
        await Emit(option.Id, "CREATE", response, null, ct);
        return response;
    }

    public async Task<SourcingOptionResponse> Update(Guid id, UpdateSourcingOptionRequest request,
        DateOnly evaluationDate, CancellationToken ct)
    {
        ValidateCommercial(request.MinimumOrderQuantity, request.SupplierLeadTimeDays,
            request.ForwarderToShipmentDays, request.ShippingPeriodDays, request.DestinationCustomsDays,
            request.ExpectedVersion);
        await using var db = await factory.CreateDbContextAsync(ct);
        var option = await db.SourcingOptions.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException();
        EnsureVersion(option.Version, request.ExpectedVersion);
        await ValidateAssignedReferences(db, request.ShippingModeId, request.IncotermId,
            option.ShippingModeId != request.ShippingModeId, option.IncotermId != request.IncotermId, ct);
        option.Edit(request.MinimumOrderQuantity, request.ShippingModeId, request.IncotermId,
            request.SupplierLeadTimeDays, request.ForwarderToShipmentDays, request.ShippingPeriodDays,
            request.DestinationCustomsDays);
        await Save(db, ct);
        var response = (await Get(id, evaluationDate, ct))!;
        await Emit(id, "EDIT", response, null, ct);
        return response;
    }

    public async Task<SourcingOptionResponse> ChangeState(Guid id, bool active,
        ChangeSourcingOptionStateRequest request, DateOnly evaluationDate, CancellationToken ct)
    {
        if (request.ExpectedVersion < 1) throw Validation("expectedVersion", "نسخه معتبر الزامی است.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var itemId = await db.SourcingOptions.Where(x => x.Id == id).Select(x => (Guid?)x.ItemId).SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockItem(db, transaction, itemId, ct);
        db.ChangeTracker.Clear();
        var option = await db.SourcingOptions.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException();
        EnsureVersion(option.Version, request.ExpectedVersion);
        if (active) await ValidateReactivation(db, option, ct);
        option.ChangeState(active);
        await Save(db, ct);
        await transaction.CommitAsync(ct);
        var response = (await Get(id, evaluationDate, ct))!;
        await Emit(id, active ? "REACTIVATE" : "DEACTIVATE", response, request.Reason, ct);
        return response;
    }

    public async Task<SourcingOptionResponse> SetPreferred(Guid id, SetPreferredSourcingOptionRequest request,
        DateOnly evaluationDate, CancellationToken ct)
    {
        if (request.ExpectedVersion < 1) throw Validation("expectedVersion", "نسخه معتبر الزامی است.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var itemId = await db.SourcingOptions.Where(x => x.Id == id).Select(x => (Guid?)x.ItemId).SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockItem(db, transaction, itemId, ct);
        db.ChangeTracker.Clear();
        var options = await db.SourcingOptions.Where(x => x.ItemId == itemId).ToListAsync(ct);
        var target = options.Single(x => x.Id == id);
        var current = options.SingleOrDefault(x => x.IsActive && x.IsPreferred);
        if (current?.Id != request.ExpectedCurrentPreferredId ||
            (current is not null && current.Version != request.ExpectedCurrentPreferredVersion))
            throw new SourcingConflictException("PREFERRED_STATE_CHANGED", "وضعیت قلم ترجیحی تغییر کرده است.",
                new { CurrentPreferredId = current?.Id, CurrentPreferredVersion = current?.Version });
        if (target.Version != request.ExpectedVersion)
            throw new SourcingConflictException("PREFERRED_STATE_CHANGED", "وضعیت گزینه هدف تغییر کرده است.",
                new { TargetId = target.Id, TargetVersion = target.Version });
        if (!target.IsActive) throw Validation("id", "فقط گزینه فعال می‌تواند ترجیحی شود.");
        if (current?.Id != target.Id)
        {
            if (current is not null)
            {
                current.SetPreferred(false);
                await Save(db, ct);
            }

            target.SetPreferred(true);
            await Save(db, ct);
        }
        await transaction.CommitAsync(ct);
        var response = (await Get(id, evaluationDate, ct))!;
        await Emit(id, "SET_PREFERRED", response, request.Reason, ct);
        return response;
    }

    public async Task<SourcingOptionResponse> AddPrice(Guid optionId, CreateSourcingPriceRequest request,
        DateOnly evaluationDate, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.UnitPrice < 0) errors["unitPrice"] = ["قیمت باید بزرگ‌تر یا مساوی صفر باشد."];
        if (request.EffectiveTo.HasValue && request.EffectiveTo < request.EffectiveFrom)
            errors["effectiveTo"] = ["تاریخ پایان نمی‌تواند قبل از شروع باشد."];
        if (request.ExpectedVersion < 1) errors["expectedVersion"] = ["نسخه معتبر الزامی است."];
        if (errors.Count > 0) throw new SourcingValidationException(errors);
        await using var db = await factory.CreateDbContextAsync(ct);
        var option = await db.SourcingOptions.SingleOrDefaultAsync(x => x.Id == optionId, ct) ?? throw new KeyNotFoundException();
        EnsureVersion(option.Version, request.ExpectedVersion);
        if (!await db.Currencies.AnyAsync(x => x.Id == request.CurrencyId && x.IsActive, ct))
            throw Validation("currencyId", "ارز فعال الزامی است.");
        db.SourcingPrices.Add(new(optionId, request.UnitPrice, request.CurrencyId, request.EffectiveFrom, request.EffectiveTo));
        option.Touch();
        await Save(db, ct);
        var response = (await Get(optionId, evaluationDate, ct))!;
        await Emit(optionId, "PRICE_ADD", response, null, ct);
        return response;
    }

    public async Task<SourcingOptionResponse> ClosePrice(Guid optionId, Guid priceId,
        CloseSourcingPriceRequest request, DateOnly evaluationDate, CancellationToken ct)
    {
        if (request.ExpectedVersion < 1) throw Validation("expectedVersion", "نسخه معتبر الزامی است.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var option = await db.SourcingOptions.SingleOrDefaultAsync(x => x.Id == optionId, ct) ?? throw new KeyNotFoundException();
        EnsureVersion(option.Version, request.ExpectedVersion);
        var price = await db.SourcingPrices.SingleOrDefaultAsync(x => x.Id == priceId && x.SourcingOptionId == optionId, ct)
            ?? throw new KeyNotFoundException();
        if (request.EffectiveTo < price.EffectiveFrom) throw Validation("effectiveTo", "تاریخ پایان نمی‌تواند قبل از شروع باشد.");
        price.Close(request.EffectiveTo);
        option.Touch();
        await Save(db, ct);
        var response = (await Get(optionId, evaluationDate, ct))!;
        await Emit(optionId, "PRICE_CLOSE", response, request.Reason, ct);
        return response;
    }

    private static IQueryable<SourcingOption> LoadQuery(IQueryable<SourcingOption> query) => query
        .Include(x => x.Item).ThenInclude(x => x.BaseUnit).Include(x => x.Supplier).Include(x => x.ItemBaseUnit)
        .Include(x => x.ShippingMode).Include(x => x.Incoterm);

    private static async Task<List<SourcingOptionResponse>> ToResponses(HadronDbContext db,
        IReadOnlyList<SourcingOption> options, DateOnly evaluationDate, bool includeHistory, CancellationToken ct)
    {
        if (options.Count == 0) return [];
        var ids = options.Select(x => x.Id).ToArray();
        var itemIds = options.Select(x => x.ItemId).Distinct().ToArray();
        var methods = await db.MaterialItemProfiles.AsNoTracking().Where(x => itemIds.Contains(x.ItemId))
            .ToDictionaryAsync(x => x.ItemId, x => x.SupplyMethod, ct);
        var prices = await db.SourcingPrices.AsNoTracking().Include(x => x.Currency)
            .Where(x => ids.Contains(x.SourcingOptionId) && (includeHistory ||
                (x.EffectiveFrom <= evaluationDate && (x.EffectiveTo == null || x.EffectiveTo >= evaluationDate))))
            .OrderByDescending(x => x.EffectiveFrom).ToListAsync(ct);
        return options.Select(option =>
        {
            var optionPrices = prices.Where(x => x.SourcingOptionId == option.Id).Select(ToPrice).ToList();
            var current = optionPrices.FirstOrDefault(x => x.EffectiveFrom <= evaluationDate &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo >= evaluationDate));
            methods.TryGetValue(option.ItemId, out var method);
            method ??= string.Empty;
            var reasons = BlockReasons(option, method, current);
            string state;
            if (!option.IsActive) state = SourcingReadiness.Inactive;
            else if (reasons.Count > 0) state = SourcingReadiness.Blocked;
            else
            {
                reasons = IncompleteReasons(option, current);
                state = reasons.Count > 0 ? SourcingReadiness.IncompleteData : SourcingReadiness.Ready;
            }
            int? total = new[] { option.SupplierLeadTimeDays, option.ForwarderToShipmentDays,
                option.ShippingPeriodDays, option.DestinationCustomsDays }.All(x => x.HasValue)
                ? option.SupplierLeadTimeDays + option.ForwarderToShipmentDays + option.ShippingPeriodDays + option.DestinationCustomsDays
                : null;
            return new SourcingOptionResponse(option.Id, option.SourcingNumber,
                new(option.Item.Id, option.Item.BusinessNumber, option.Item.NameFa, option.Item.NameEn,
                    option.Item.ItemType, method, option.Item.IsActive, Unit(option.Item.BaseUnit)),
                new(option.Supplier.Id, option.Supplier.SupplierNumber, option.Supplier.Name, option.Supplier.IsActive),
                Unit(option.ItemBaseUnit), option.MinimumOrderQuantity, Ref(option.ShippingMode), Ref(option.Incoterm),
                option.SupplierLeadTimeDays, option.ForwarderToShipmentDays, option.ShippingPeriodDays,
                option.DestinationCustomsDays, total, option.IsPreferred, option.IsActive, option.Version,
                evaluationDate, new(state, reasons), current, option.CreatedAtUtc, option.UpdatedAtUtc, optionPrices);
        }).ToList();
    }

    private static List<string> BlockReasons(SourcingOption option, string method, SourcingPriceResponse? current)
    {
        var reasons = new List<string>();
        if (!option.Item.IsActive) reasons.Add("ITEM_INACTIVE");
        if (!option.Supplier.IsActive) reasons.Add("SUPPLIER_INACTIVE");
        if (!ExternallySourcedMethods.Contains(method)) reasons.Add("SUPPLY_METHOD_INCOMPATIBLE");
        if (option.ItemBaseUnitId != option.Item.BaseUnitId) reasons.Add("BASE_UNIT_CHANGED");
        if (option.ShippingMode is { IsActive: false }) reasons.Add("SHIPPING_MODE_INACTIVE");
        if (option.Incoterm is { IsActive: false }) reasons.Add("INCOTERM_INACTIVE");
        if (current?.Currency.IsActive == false) reasons.Add("CURRENCY_INACTIVE");
        return reasons;
    }

    private static List<string> IncompleteReasons(SourcingOption option, SourcingPriceResponse? current)
    {
        var reasons = new List<string>();
        if (!option.MinimumOrderQuantity.HasValue) reasons.Add("MOQ_MISSING");
        if (!option.ShippingModeId.HasValue) reasons.Add("SHIPPING_MODE_MISSING");
        if (!option.SupplierLeadTimeDays.HasValue) reasons.Add("SUPPLIER_LEAD_TIME_MISSING");
        if (!option.ForwarderToShipmentDays.HasValue) reasons.Add("FORWARDER_LEAD_TIME_MISSING");
        if (!option.ShippingPeriodDays.HasValue) reasons.Add("SHIPPING_PERIOD_MISSING");
        if (!option.DestinationCustomsDays.HasValue) reasons.Add("DESTINATION_CUSTOMS_MISSING");
        if (current is null) reasons.Add("CURRENT_PRICE_MISSING");
        return reasons;
    }

    private static SourcingPriceResponse ToPrice(SourcingPrice x) => new(x.Id, x.UnitPrice,
        new(x.Currency.Id, x.Currency.Code, x.Currency.NameFa, x.Currency.NameEn, x.Currency.IsActive),
        x.EffectiveFrom, x.EffectiveTo, x.CreatedAtUtc, x.UpdatedAtUtc);
    private static SourcingReference? Ref(ReferenceValue? x) => x is null ? null : new(x.Id, x.Code, x.NameFa, x.NameEn, x.IsActive);
    private static SourcingUnitReference Unit(Unit x) => new(x.Id, x.Code, x.NameFa, x.NameEn, x.Symbol, x.IsActive);

    private static async Task ValidateReactivation(HadronDbContext db, SourcingOption option, CancellationToken ct)
    {
        var item = await db.Items.SingleAsync(x => x.Id == option.ItemId, ct);
        var method = await db.MaterialItemProfiles.Where(x => x.ItemId == option.ItemId).Select(x => x.SupplyMethod).SingleOrDefaultAsync(ct);
        var errors = new Dictionary<string, string[]>();
        if (!item.IsActive) errors["itemId"] = ["قلم فعال الزامی است."];
        if (!await db.Suppliers.AnyAsync(x => x.Id == option.SupplierId && x.IsActive, ct)) errors["supplierId"] = ["تأمین‌کننده فعال الزامی است."];
        if (!ExternallySourcedMethods.Contains(method ?? string.Empty)) errors["supplyMethod"] = ["روش تأمین سازگار نیست."];
        if (item.BaseUnitId != option.ItemBaseUnitId) errors["itemBaseUnitId"] = ["BASE_UNIT_CHANGED"];
        if (option.ShippingModeId.HasValue && !await ActiveReference(db, option.ShippingModeId.Value, "SHIPPING_MODE", ct)) errors["shippingModeId"] = ["روش حمل تخصیص‌یافته باید فعال باشد."];
        if (option.IncotermId.HasValue && !await ActiveReference(db, option.IncotermId.Value, "INCOTERM", ct)) errors["incotermId"] = ["اینکوترم تخصیص‌یافته باید فعال باشد."];
        if (errors.Count > 0) throw new SourcingValidationException(errors);
    }

    private static async Task ValidateAssignedReferences(HadronDbContext db, Guid? shipping, Guid? incoterm,
        bool checkShipping, bool checkIncoterm, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (checkShipping && shipping.HasValue && !await ActiveReference(db, shipping.Value, "SHIPPING_MODE", ct)) errors["shippingModeId"] = ["روش حمل فعال الزامی است."];
        if (checkIncoterm && incoterm.HasValue && !await ActiveReference(db, incoterm.Value, "INCOTERM", ct)) errors["incotermId"] = ["اینکوترم فعال الزامی است."];
        if (errors.Count > 0) throw new SourcingValidationException(errors);
    }

    private static Task<bool> ActiveReference(HadronDbContext db, Guid id, string type, CancellationToken ct) =>
        db.ReferenceValues.AnyAsync(x => x.Id == id && x.ReferenceType.Code == type && x.IsActive, ct);

    private static void ValidateCommercial(decimal? moq, int? supplier, int? forwarder, int? shipping,
        int? customs, long? version)
    {
        var errors = new Dictionary<string, string[]>();
        if (moq < 0) errors["minimumOrderQuantity"] = ["MOQ باید بزرگ‌تر یا مساوی صفر باشد."];
        if (new[] { supplier, forwarder, shipping, customs }.Any(x => x < 0)) errors["leadTime"] = ["مدت مراحل نمی‌تواند منفی باشد."];
        if (version.HasValue && version < 1) errors["expectedVersion"] = ["نسخه معتبر الزامی است."];
        if (errors.Count > 0) throw new SourcingValidationException(errors);
    }

    private static void ValidatePaging(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100) throw Validation("page", "صفحه معتبر و اندازه ۱ تا ۱۰۰ الزامی است.");
    }

    private static async Task<string> NextNumber(HadronDbContext db, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT nextval('procurement.sourcing_option_number_seq')", connection);
        var value = (long)(await command.ExecuteScalarAsync(ct))!;
        return $"SRC-{value:D4}";
    }

    private static async Task LockItem(HadronDbContext db, IDbContextTransaction transaction, Guid itemId,
        CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var npgsqlTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();
        await using var command = new NpgsqlCommand(
            "SELECT \"Id\" FROM catalog.items WHERE \"Id\" = @id FOR UPDATE", connection, npgsqlTransaction);
        command.Parameters.AddWithValue("id", itemId);
        if (await command.ExecuteScalarAsync(ct) is null) throw new KeyNotFoundException();
    }

    private static async Task Save(HadronDbContext db, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new SourcingConflictException("STALE_VERSION", "نسخه ارسالی قدیمی است."); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation })
        { throw new SourcingConflictException("PRICE_PERIOD_OVERLAP", "بازه‌های قیمت یک گزینه نباید هم‌پوشانی داشته باشند."); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new SourcingConflictException("SOURCING_INTEGRITY_CONFLICT", "تعارض یکتایی داده تأمین‌یابی."); }
    }

    private static void EnsureVersion(long actual, long expected)
    {
        if (actual != expected) throw new SourcingConflictException("STALE_VERSION", "نسخه ارسالی قدیمی است.",
            new { ActualVersion = actual, ExpectedVersion = expected });
    }

    private async Task Emit(Guid id, string action, object value, string? reason, CancellationToken ct)
    {
        try
        {
            await auditSink.WriteAsync(new("SourcingOption", id, action, DateTime.UtcNow,
                AuditEventContext.ActorId(accessor.HttpContext), AuditEventContext.CorrelationId(accessor.HttpContext),
                null, new Dictionary<string, object?> { ["Value"] = value }, reason), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { logger.LogError(ex, "Post-commit sourcing audit failed for {Id} {Action}", id, action); }
    }

    private static SourcingValidationException Validation(string key, string message) =>
        new(new Dictionary<string, string[]> { [key] = [message] });
}
