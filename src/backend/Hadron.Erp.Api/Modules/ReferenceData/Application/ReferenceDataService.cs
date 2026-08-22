using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Hadron.Erp.Api.Platform.Audit;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hadron.Erp.Api.Modules.ReferenceData.Application;

public sealed class ReferenceDataService(
    IDbContextFactory<HadronDbContext> dbContextFactory,
    IAuditEventSink auditEventSink,
    IHttpContextAccessor httpContextAccessor)
{
    public async Task<IReadOnlyList<ReferenceTypeResponse>> GetTypes(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ReferenceTypes.AsNoTracking().OrderBy(x => x.NameFa)
            .Select(x => new ReferenceTypeResponse(x.Id, x.Code, x.NameFa, x.NameEn))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReferenceValueResponse>> GetValues(
        string typeCode, bool includeInactive, string? search, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var normalizedType = NormalizeCode(typeCode);
        var query = db.ReferenceValues.AsNoTracking().Where(x => x.ReferenceType.Code == normalizedType);
        if (!includeInactive) query = query.Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Code.Contains(term) || x.NameFa.Contains(term) || x.NameEn.Contains(term));
        }

        return await query.OrderBy(x => x.SortOrder).ThenBy(x => x.Code)
            .Select(x => new ReferenceValueResponse(x.Id, x.ReferenceType.Code, x.Code, x.NameFa, x.NameEn,
                x.IsActive, x.SortOrder, x.Version, x.CreatedAtUtc, x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<ReferenceValueResponse?> GetValue(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ReferenceValues.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new ReferenceValueResponse(x.Id, x.ReferenceType.Code, x.Code, x.NameFa, x.NameEn,
                x.IsActive, x.SortOrder, x.Version, x.CreatedAtUtc, x.UpdatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ReferenceValueResponse> CreateValue(
        string typeCode, CreateReferenceValueRequest request, CancellationToken cancellationToken)
    {
        ValidateCommon(request.Code, request.NameFa, request.NameEn);
        ValidateSortOrder(request.SortOrder);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var normalizedType = NormalizeCode(typeCode);
        var type = await db.ReferenceTypes.SingleOrDefaultAsync(x => x.Code == normalizedType, cancellationToken)
            ?? throw new ReferenceDataValidationException(Error("typeCode", "نوع داده مرجع معتبر نیست."));
        var entity = new ReferenceValue(type.Id, CleanCode(request.Code), NormalizeCode(request.Code),
            request.NameFa.Trim(), request.NameEn.Trim(), request.SortOrder);
        db.ReferenceValues.Add(entity);
        await Save(db, cancellationToken);
        await Emit("ReferenceValue", entity.Id, "CREATE", null, Snapshot(entity), cancellationToken);
        return new(entity.Id, type.Code, entity.Code, entity.NameFa, entity.NameEn, entity.IsActive,
            entity.SortOrder, entity.Version, entity.CreatedAtUtc, entity.UpdatedAtUtc);
    }

    public async Task<ReferenceValueResponse> UpdateValue(
        Guid id, UpdateReferenceValueRequest request, CancellationToken cancellationToken)
    {
        ValidateLabels(request.NameFa, request.NameEn);
        ValidateVersion(request.ExpectedVersion);
        ValidateSortOrder(request.SortOrder);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.ReferenceValues.Include(x => x.ReferenceType).SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException();
        EnsureVersion(entity.Version, request.ExpectedVersion);
        var oldValues = Snapshot(entity);
        entity.Edit(request.NameFa.Trim(), request.NameEn.Trim(), request.SortOrder);
        entity.AdvanceVersion();
        entity.Touch();
        await Save(db, cancellationToken);
        await Emit("ReferenceValue", entity.Id, "EDIT", oldValues, Snapshot(entity), cancellationToken);
        return new(entity.Id, entity.ReferenceType.Code, entity.Code, entity.NameFa, entity.NameEn, entity.IsActive,
            entity.SortOrder, entity.Version, entity.CreatedAtUtc, entity.UpdatedAtUtc);
    }

    public async Task<ReferenceValueResponse> ChangeValueState(
        Guid id, bool active, ChangeActiveStateRequest request, CancellationToken cancellationToken)
    {
        ValidateVersion(request.ExpectedVersion);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.ReferenceValues.Include(x => x.ReferenceType).SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException();
        EnsureVersion(entity.Version, request.ExpectedVersion);
        var oldValues = Snapshot(entity);
        entity.SetActive(active);
        entity.AdvanceVersion();
        entity.Touch();
        await Save(db, cancellationToken);
        await Emit("ReferenceValue", entity.Id, active ? "REACTIVATE" : "DEACTIVATE", oldValues, Snapshot(entity), cancellationToken);
        return new(entity.Id, entity.ReferenceType.Code, entity.Code, entity.NameFa, entity.NameEn, entity.IsActive,
            entity.SortOrder, entity.Version, entity.CreatedAtUtc, entity.UpdatedAtUtc);
    }

    public async Task<IReadOnlyList<CurrencyResponse>> GetCurrencies(bool includeInactive, string? search, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Currencies.AsNoTracking();
        if (!includeInactive) query = query.Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Code.Contains(term) || x.NameFa.Contains(term) || x.NameEn.Contains(term));
        }
        return await query.OrderBy(x => x.Code).Select(x => new CurrencyResponse(x.Id, x.Code, x.NameFa,
            x.NameEn, x.Symbol, x.DecimalPrecision, x.IsActive, x.Version, x.CreatedAtUtc, x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<CurrencyResponse?> GetCurrency(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Currencies.AsNoTracking().Where(x => x.Id == id).Select(x => new CurrencyResponse(x.Id,
            x.Code, x.NameFa, x.NameEn, x.Symbol, x.DecimalPrecision, x.IsActive, x.Version, x.CreatedAtUtc, x.UpdatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<CurrencyResponse> CreateCurrency(CreateCurrencyRequest request, CancellationToken cancellationToken)
    {
        ValidateCommon(request.IsoCode, request.NameFa, request.NameEn);
        ValidateCurrency(request.IsoCode, request.DecimalPrecision);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = new Currency(CleanCode(request.IsoCode), NormalizeCode(request.IsoCode), request.NameFa.Trim(),
            request.NameEn.Trim(), CleanOptional(request.Symbol), request.DecimalPrecision);
        db.Currencies.Add(entity);
        await Save(db, cancellationToken);
        await Emit("Currency", entity.Id, "CREATE", null, Snapshot(entity), cancellationToken);
        return ToResponse(entity);
    }

    public async Task<CurrencyResponse> UpdateCurrency(Guid id, UpdateCurrencyRequest request, CancellationToken cancellationToken)
    {
        ValidateLabels(request.NameFa, request.NameEn);
        ValidateCurrencyPrecision(request.DecimalPrecision);
        ValidateVersion(request.ExpectedVersion);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Currencies.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException();
        EnsureVersion(entity.Version, request.ExpectedVersion);
        var oldValues = Snapshot(entity);
        entity.Edit(request.NameFa.Trim(), request.NameEn.Trim(), CleanOptional(request.Symbol), request.DecimalPrecision);
        entity.AdvanceVersion();
        entity.Touch();
        await Save(db, cancellationToken);
        await Emit("Currency", entity.Id, "EDIT", oldValues, Snapshot(entity), cancellationToken);
        return ToResponse(entity);
    }

    public async Task<CurrencyResponse> ChangeCurrencyState(Guid id, bool active, ChangeActiveStateRequest request, CancellationToken cancellationToken)
    {
        ValidateVersion(request.ExpectedVersion);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Currencies.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException();
        EnsureVersion(entity.Version, request.ExpectedVersion);
        var oldValues = Snapshot(entity);
        entity.SetActive(active);
        entity.AdvanceVersion();
        entity.Touch();
        await Save(db, cancellationToken);
        await Emit("Currency", entity.Id, active ? "REACTIVATE" : "DEACTIVATE", oldValues, Snapshot(entity), cancellationToken);
        return ToResponse(entity);
    }

    public async Task<IReadOnlyList<UnitResponse>> GetUnits(bool includeInactive, string? search, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Units.AsNoTracking();
        if (!includeInactive) query = query.Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Code.Contains(term) || x.NameFa.Contains(term) || x.NameEn.Contains(term));
        }
        return await query.OrderBy(x => x.SortOrder).ThenBy(x => x.Code).Select(x => new UnitResponse(x.Id,
            x.Code, x.NameFa, x.NameEn, x.Symbol, x.IsActive, x.SortOrder, x.Version, x.CreatedAtUtc, x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<UnitResponse?> GetUnit(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Units.AsNoTracking().Where(x => x.Id == id).Select(x => new UnitResponse(x.Id, x.Code,
            x.NameFa, x.NameEn, x.Symbol, x.IsActive, x.SortOrder, x.Version, x.CreatedAtUtc, x.UpdatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<UnitResponse> CreateUnit(CreateUnitRequest request, CancellationToken cancellationToken)
    {
        ValidateCommon(request.Code, request.NameFa, request.NameEn);
        ValidateSortOrder(request.SortOrder);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = new Unit(CleanCode(request.Code), NormalizeCode(request.Code), request.NameFa.Trim(), request.NameEn.Trim(),
            CleanOptional(request.Symbol), request.SortOrder);
        db.Units.Add(entity);
        await Save(db, cancellationToken);
        await Emit("Unit", entity.Id, "CREATE", null, Snapshot(entity), cancellationToken);
        return ToResponse(entity);
    }

    public async Task<UnitResponse> UpdateUnit(Guid id, UpdateUnitRequest request, CancellationToken cancellationToken)
    {
        ValidateLabels(request.NameFa, request.NameEn);
        ValidateSortOrder(request.SortOrder);
        ValidateVersion(request.ExpectedVersion);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Units.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException();
        EnsureVersion(entity.Version, request.ExpectedVersion);
        var oldValues = Snapshot(entity);
        entity.Edit(request.NameFa.Trim(), request.NameEn.Trim(), CleanOptional(request.Symbol), request.SortOrder);
        entity.AdvanceVersion();
        entity.Touch();
        await Save(db, cancellationToken);
        await Emit("Unit", entity.Id, "EDIT", oldValues, Snapshot(entity), cancellationToken);
        return ToResponse(entity);
    }

    public async Task<UnitResponse> ChangeUnitState(Guid id, bool active, ChangeActiveStateRequest request, CancellationToken cancellationToken)
    {
        ValidateVersion(request.ExpectedVersion);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Units.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException();
        EnsureVersion(entity.Version, request.ExpectedVersion);
        var oldValues = Snapshot(entity);
        entity.SetActive(active);
        entity.AdvanceVersion();
        entity.Touch();
        await Save(db, cancellationToken);
        await Emit("Unit", entity.Id, active ? "REACTIVATE" : "DEACTIVATE", oldValues, Snapshot(entity), cancellationToken);
        return ToResponse(entity);
    }

    private static CurrencyResponse ToResponse(Currency x) => new(x.Id, x.Code, x.NameFa, x.NameEn, x.Symbol,
        x.DecimalPrecision, x.IsActive, x.Version, x.CreatedAtUtc, x.UpdatedAtUtc);
    private static UnitResponse ToResponse(Unit x) => new(x.Id, x.Code, x.NameFa, x.NameEn, x.Symbol,
        x.IsActive, x.SortOrder, x.Version, x.CreatedAtUtc, x.UpdatedAtUtc);

    private async Task Emit(
        string entityType,
        Guid entityId,
        string action,
        IReadOnlyDictionary<string, object?>? oldValues,
        IReadOnlyDictionary<string, object?>? newValues,
        CancellationToken cancellationToken)
    {
        var context = httpContextAccessor.HttpContext;
        await auditEventSink.WriteAsync(new AuditEvent(
            entityType,
            entityId,
            action,
            DateTime.UtcNow,
            AuditEventContext.ActorId(context),
            AuditEventContext.CorrelationId(context),
            oldValues,
            newValues,
            null), cancellationToken);
    }

    private static Dictionary<string, object?> Snapshot(MutableReferenceData entity)
    {
        var values = new Dictionary<string, object?>
        {
            ["Code"] = entity.Code,
            ["NameFa"] = entity.NameFa,
            ["NameEn"] = entity.NameEn,
            ["IsActive"] = entity.IsActive,
            ["Version"] = entity.Version
        };

        switch (entity)
        {
            case ReferenceValue value:
                values["ReferenceTypeId"] = value.ReferenceTypeId;
                values["SortOrder"] = value.SortOrder;
                break;
            case Currency currency:
                values["Symbol"] = currency.Symbol;
                values["DecimalPrecision"] = currency.DecimalPrecision;
                break;
            case Unit unit:
                values["Symbol"] = unit.Symbol;
                values["SortOrder"] = unit.SortOrder;
                break;
        }

        return values;
    }

    private static async Task Save(HadronDbContext db, CancellationToken cancellationToken)
    {
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            throw new ReferenceDataConflictException("STALE_VERSION", "رکورد توسط کاربر دیگری تغییر کرده است. اطلاعات را دوباره بارگذاری کنید.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ReferenceDataConflictException("DUPLICATE_CODE", "کد واردشده قبلاً ثبت شده است.");
        }
    }

    private static void EnsureVersion(long current, long expected)
    {
        if (current != expected) throw new ReferenceDataConflictException("STALE_VERSION", "نسخه ارسالی قدیمی است. اطلاعات را دوباره بارگذاری کنید.");
    }

    private static void ValidateCommon(string code, string nameFa, string nameEn)
    {
        ValidateLabels(nameFa, nameEn);
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 50)
            throw new ReferenceDataValidationException(Error("code", "کد الزامی و حداکثر ۵۰ نویسه است."));
    }

    private static void ValidateLabels(string nameFa, string nameEn)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(nameFa) || nameFa.Trim().Length > 200) errors["nameFa"] = ["نام فارسی الزامی و حداکثر ۲۰۰ نویسه است."];
        if (string.IsNullOrWhiteSpace(nameEn) || nameEn.Trim().Length > 200) errors["nameEn"] = ["نام انگلیسی الزامی و حداکثر ۲۰۰ نویسه است."];
        if (errors.Count > 0) throw new ReferenceDataValidationException(errors);
    }

    private static void ValidateCurrency(string code, int precision)
    {
        var normalizedCode = CleanCode(code);
        if (normalizedCode.Length != 3 || normalizedCode.Any(character => character is < 'A' or > 'Z'))
            throw new ReferenceDataValidationException(Error("isoCode", "کد ارز باید دقیقاً سه حرف انگلیسی A-Z باشد."));
        ValidateCurrencyPrecision(precision);
    }

    private static void ValidateCurrencyPrecision(int precision)
    {
        if (precision is < 0 or > 9) throw new ReferenceDataValidationException(Error("decimalPrecision", "دقت اعشار باید بین صفر و ۹ باشد."));
    }

    private static void ValidateSortOrder(int value)
    {
        if (value < 0) throw new ReferenceDataValidationException(Error("sortOrder", "ترتیب نمایش نمی‌تواند منفی باشد."));
    }

    private static void ValidateVersion(long value)
    {
        if (value < 1) throw new ReferenceDataValidationException(Error("expectedVersion", "نسخه معتبر الزامی است."));
    }

    private static Dictionary<string, string[]> Error(string key, string message) => new() { [key] = [message] };
    private static string CleanCode(string value) => value.Trim().ToUpperInvariant();
    private static string NormalizeCode(string value) => CleanCode(value);
    private static string? CleanOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
