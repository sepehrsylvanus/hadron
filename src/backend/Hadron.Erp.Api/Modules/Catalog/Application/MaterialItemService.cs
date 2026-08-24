using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Hadron.Erp.Api.Platform.Audit;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hadron.Erp.Api.Modules.Catalog.Application;

public sealed class MaterialItemService(
    IDbContextFactory<HadronDbContext> factory,
    IAuditEventSink auditSink,
    IHttpContextAccessor accessor,
    ILogger<MaterialItemService> logger)
{
    public async Task<MaterialItemListResponse> GetList(string itemType, string? search, bool includeInactive,
        Guid? categoryId, string? supplyMethod, int page, int pageSize, CancellationToken token)
    {
        EnsureItemType(itemType);
        if (page < 1 || pageSize is < 1 or > 100)
        {
            throw Validation("page", "صفحه باید مثبت و اندازه صفحه بین ۱ تا ۱۰۰ باشد.");
        }

        await using var db = await factory.CreateDbContextAsync(token);
        var query = db.MaterialItemProfiles.AsNoTracking()
            .Where(x => x.Item.ItemType == itemType && (includeInactive || x.Item.IsActive));
        if (categoryId.HasValue) query = query.Where(x => x.CategoryId == categoryId);
        if (!string.IsNullOrWhiteSpace(supplyMethod))
        {
            var method = NormalizeSupplyMethod(supplyMethod);
            if (!SupplyMethods.All.Contains(method)) throw Validation("supplyMethod", "روش تأمین معتبر نیست.");
            query = query.Where(x => x.SupplyMethod == method);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Item.BusinessNumber.Contains(term) || x.Item.NameFa.Contains(term) ||
                (x.Item.NameEn != null && x.Item.NameEn.Contains(term)));
        }

        var total = await query.CountAsync(token);
        var rows = await query.OrderBy(x => x.Item.BusinessNumber).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { Profile = x, Item = x.Item, Category = x.Category, Unit = x.Item.BaseUnit })
            .ToListAsync(token);
        return new(rows.Select(x => ToResponse(x.Profile, x.Item, x.Category, x.Unit)).ToList(), page, pageSize, total);
    }

    public async Task<MaterialItemResponse?> Get(string itemType, Guid id, CancellationToken token)
    {
        EnsureItemType(itemType);
        await using var db = await factory.CreateDbContextAsync(token);
        return await LoadResponse(db, itemType, id, token);
    }

    public async Task<MaterialItemMutationResponse> Create(string itemType, CreateMaterialItemRequest request,
        CancellationToken token)
    {
        EnsureItemType(itemType);
        Validate(request.NameFa, request.NameEn, request.SupplyMethod, request.NetWeightKg,
            request.DefaultPlanningScrapPercent, request.SafetyStockQty, request.CriticalDays,
            request.SafeStartDays, request.ExcessStartDays, request.Notes, null);
        await using var db = await factory.CreateDbContextAsync(token);
        await ValidateReferences(db, itemType, null, request.CategoryId, request.BaseUnitId, true, token);
        var number = await NextNumber(db, itemType, token);
        var item = new Item(itemType, number, request.NameFa.Trim(), Clean(request.NameEn), request.BaseUnitId,
            Clean(request.Notes));
        var profile = new MaterialItemProfile(item.Id, request.CategoryId,
            NormalizeSupplyMethod(request.SupplyMethod), request.NetWeightKg, request.DefaultPlanningScrapPercent,
            request.SafetyStockQty, request.CriticalDays, request.SafeStartDays, request.ExcessStartDays);
        db.AddRange(item, profile);
        await Save(db, token);
        var response = (await LoadResponse(db, itemType, item.Id, token))!;
        var warnings = await DuplicateWarnings(db, itemType, item.Id, response.NameFa, response.NameEn, token);
        await Emit(item, "CREATE", null, Snapshot(response), null, token);
        return new(response, warnings);
    }

    public async Task<MaterialItemMutationResponse> Update(string itemType, Guid id,
        UpdateMaterialItemRequest request, CancellationToken token)
    {
        EnsureItemType(itemType);
        Validate(request.NameFa, request.NameEn, request.SupplyMethod, request.NetWeightKg,
            request.DefaultPlanningScrapPercent, request.SafetyStockQty, request.CriticalDays,
            request.SafeStartDays, request.ExcessStartDays, request.Notes, request.ExpectedVersion);
        await using var db = await factory.CreateDbContextAsync(token);
        var profile = await db.MaterialItemProfiles.Include(x => x.Item)
            .SingleOrDefaultAsync(x => x.ItemId == id && x.Item.ItemType == itemType, token)
            ?? throw new KeyNotFoundException();
        EnsureVersion(profile.Item.Version, request.ExpectedVersion);
        await ValidateReferences(db, itemType, profile, request.CategoryId, request.BaseUnitId, false, token);
        var old = Snapshot((await LoadResponse(db, itemType, id, token))!);
        profile.Item.Edit(request.NameFa.Trim(), Clean(request.NameEn),
            request.BaseUnitId, Clean(request.Notes));
        profile.Edit(request.CategoryId, NormalizeSupplyMethod(request.SupplyMethod), request.NetWeightKg,
            request.DefaultPlanningScrapPercent, request.SafetyStockQty, request.CriticalDays,
            request.SafeStartDays, request.ExcessStartDays);
        await Save(db, token);
        var response = (await LoadResponse(db, itemType, id, token))!;
        var warnings = await DuplicateWarnings(db, itemType, id, response.NameFa, response.NameEn, token);
        await Emit(profile.Item, "EDIT", old, Snapshot(response), null, token);
        return new(response, warnings);
    }

    public async Task<MaterialItemResponse> ChangeState(string itemType, Guid id, bool active,
        ChangeMaterialItemStateRequest request, CancellationToken token)
    {
        EnsureItemType(itemType);
        if (request.ExpectedVersion < 1) throw Validation("expectedVersion", "نسخه معتبر الزامی است.");
        await using var db = await factory.CreateDbContextAsync(token);
        var profile = await db.MaterialItemProfiles.Include(x => x.Item)
            .SingleOrDefaultAsync(x => x.ItemId == id && x.Item.ItemType == itemType, token)
            ?? throw new KeyNotFoundException();
        EnsureVersion(profile.Item.Version, request.ExpectedVersion);
        if (active)
        {
            var invalid = await InvalidReferences(db, itemType, profile.CategoryId, profile.Item.BaseUnitId, token);
            if (invalid.Count > 0)
            {
                throw new MaterialItemConflictException("ITEM_REACTIVATION_BLOCKED",
                    "مراجع عملیاتی آیتم معتبر و فعال نیستند.", invalid);
            }
        }

        var old = Snapshot((await LoadResponse(db, itemType, id, token))!);
        profile.Item.ChangeState(active);
        await Save(db, token);
        var response = (await LoadResponse(db, itemType, id, token))!;
        await Emit(profile.Item, active ? "REACTIVATE" : "DEACTIVATE", old, Snapshot(response), request.Reason, token);
        return response;
    }

    private static async Task ValidateReferences(HadronDbContext db, string itemType, MaterialItemProfile? current,
        Guid categoryId, Guid unitId, bool create, CancellationToken token)
    {
        var changedCategory = create || current!.CategoryId != categoryId;
        var changedUnit = create || current!.Item.BaseUnitId != unitId;
        var invalid = await InvalidReferences(db, itemType, categoryId, unitId, token, changedCategory, changedUnit);
        if (invalid.Count > 0)
        {
            throw new MaterialItemValidationException(invalid.ToDictionary(x => x.Key, x => new[] { x.Value }));
        }
    }

    private static async Task<Dictionary<string, string>> InvalidReferences(HadronDbContext db, string itemType,
        Guid categoryId, Guid unitId, CancellationToken token, bool checkCategory = true, bool checkUnit = true)
    {
        var invalid = new Dictionary<string, string>();
        var categoryType = itemType == ItemTypes.Part ? "PART_CATEGORY" : "COMPONENT_CATEGORY";
        if (checkCategory && !await db.ReferenceValues.AnyAsync(x => x.Id == categoryId &&
            x.ReferenceType.Code == categoryType && x.IsActive, token))
        {
            invalid["categoryId"] = $"گروه باید از نوع {categoryType} و فعال باشد.";
        }
        if (checkUnit && !await db.Units.AnyAsync(x => x.Id == unitId && x.IsActive, token))
        {
            invalid["baseUnitId"] = "واحد پایه باید فعال باشد.";
        }
        return invalid;
    }

    private static void Validate(string nameFa, string? nameEn, string supplyMethod,
        decimal? netWeightKg, decimal scrap, decimal safety, int critical, int safe, int excess, string? notes,
        long? version)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(nameFa) || nameFa.Trim().Length > 200)
            errors["nameFa"] = ["نام فارسی الزامی و حداکثر ۲۰۰ نویسه است."];
        if (Clean(nameEn)?.Length > 200) errors["nameEn"] = ["نام انگلیسی حداکثر ۲۰۰ نویسه است."];
        if (Clean(notes)?.Length > 2000) errors["notes"] = ["یادداشت حداکثر ۲۰۰۰ نویسه است."];
        if (!SupplyMethods.All.Contains(NormalizeSupplyMethod(supplyMethod)))
            errors["supplyMethod"] = ["روش تأمین معتبر نیست."];
        if (netWeightKg < 0) errors["netWeightKg"] = ["وزن خالص نمی‌تواند منفی باشد."];
        if (scrap is < 0 or > 100) errors["defaultPlanningScrapPercent"] = ["ضایعات باید بین صفر و صد باشد."];
        if (safety < 0) errors["safetyStockQty"] = ["موجودی اطمینان نمی‌تواند منفی باشد."];
        if (critical < 0 || critical > safe || safe > excess)
            errors["thresholds"] = ["باید ۰ ≤ CriticalDays ≤ SafeStartDays ≤ ExcessStartDays باشد."];
        if (version.HasValue && version < 1) errors["expectedVersion"] = ["نسخه معتبر الزامی است."];
        if (errors.Count > 0) throw new MaterialItemValidationException(errors);
    }

    private static async Task<string> NextNumber(HadronDbContext db, string itemType, CancellationToken token)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(token);
        var sequence = itemType == ItemTypes.Part ? "part_number_seq" : "component_number_seq";
        var prefix = itemType == ItemTypes.Part ? "PRT" : "CMP";
        await using var command = new NpgsqlCommand($"SELECT nextval('catalog.{sequence}')", connection);
        var value = (long)(await command.ExecuteScalarAsync(token))!;
        return $"{prefix}-{value:D4}";
    }

    private static async Task<IReadOnlyList<string>> DuplicateWarnings(HadronDbContext db, string itemType,
        Guid id, string nameFa, string? nameEn, CancellationToken token)
    {
        var warnings = new List<string>();
        var fa = nameFa.Trim().ToUpperInvariant();
        if (await db.Items.AnyAsync(x => x.Id != id && x.ItemType == itemType &&
            x.NameFa.Trim().ToUpper() == fa, token)) warnings.Add("DUPLICATE_NAME_FA");
        var en = Clean(nameEn);
        if (en != null && await db.Items.AnyAsync(x => x.Id != id && x.ItemType == itemType &&
            x.NameEn != null && x.NameEn.Trim().ToUpper() == en.ToUpperInvariant(), token))
            warnings.Add("DUPLICATE_NAME_EN");
        return warnings;
    }

    private static async Task<MaterialItemResponse?> LoadResponse(HadronDbContext db, string itemType, Guid id,
        CancellationToken token)
    {
        var row = await db.MaterialItemProfiles.AsNoTracking()
            .Where(x => x.ItemId == id && x.Item.ItemType == itemType)
            .Select(x => new { Profile = x, Item = x.Item, Category = x.Category, Unit = x.Item.BaseUnit })
            .SingleOrDefaultAsync(token);
        return row == null ? null : ToResponse(row.Profile, row.Item, row.Category, row.Unit);
    }

    private static MaterialItemResponse ToResponse(MaterialItemProfile profile, Item item,
        ReferenceValue category, Unit unit) => new(item.Id, item.ItemType, item.BusinessNumber,
        item.NameFa, item.NameEn, new(category.Id, category.Code, category.NameFa, category.NameEn, category.IsActive),
        new(unit.Id, unit.Code, unit.NameFa, unit.NameEn, unit.Symbol, unit.IsActive), profile.SupplyMethod,
        profile.NetWeightKg, profile.DefaultPlanningScrapPercent, profile.SafetyStockQty, profile.CriticalDays,
        profile.SafeStartDays, profile.ExcessStartDays, item.IsActive, item.Notes, item.Version,
        item.CreatedAtUtc, item.UpdatedAtUtc);

    private async Task Emit(Item item, string action, IReadOnlyDictionary<string, object?>? oldValues,
        IReadOnlyDictionary<string, object?>? newValues, string? reason, CancellationToken token)
    {
        var context = accessor.HttpContext;
        var entityType = item.ItemType == ItemTypes.Part ? "Part" : "Component";
        try
        {
            await auditSink.WriteAsync(new(entityType, item.Id, action, DateTime.UtcNow,
                AuditEventContext.ActorId(context), AuditEventContext.CorrelationId(context), oldValues,
                newValues, reason), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "Post-commit {EntityType} audit emission failed for {ItemId} action {Action}.",
                entityType, item.Id, action);
        }
    }

    private static Dictionary<string, object?> Snapshot(MaterialItemResponse item) => new()
    {
        ["Code"] = item.Code,
        ["NameFa"] = item.NameFa,
        ["NameEn"] = item.NameEn,
        ["CategoryId"] = item.Category.Id,
        ["BaseUnitId"] = item.BaseUnit.Id,
        ["SupplyMethod"] = item.SupplyMethod,
        ["NetWeightKg"] = item.NetWeightKg,
        ["DefaultPlanningScrapPercent"] = item.DefaultPlanningScrapPercent,
        ["SafetyStockQty"] = item.SafetyStockQty,
        ["CriticalDays"] = item.CriticalDays,
        ["SafeStartDays"] = item.SafeStartDays,
        ["ExcessStartDays"] = item.ExcessStartDays,
        ["IsActive"] = item.IsActive,
        ["Notes"] = item.Notes,
        ["Version"] = item.Version
    };

    private static async Task Save(HadronDbContext db, CancellationToken token)
    {
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException)
        { throw new MaterialItemConflictException("STALE_VERSION", "نسخه ارسالی قدیمی است."); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new MaterialItemConflictException("DUPLICATE_CODE", "کد آیتم قبلاً ثبت شده است."); }
    }

    private static void EnsureItemType(string itemType)
    {
        if (itemType is not (ItemTypes.Part or ItemTypes.Component)) throw new ArgumentOutOfRangeException(nameof(itemType));
    }
    private static void EnsureVersion(long actual, long expected)
    { if (actual != expected) throw new MaterialItemConflictException("STALE_VERSION", "نسخه ارسالی قدیمی است."); }
    private static string NormalizeSupplyMethod(string? value) => Clean(value)?.ToUpperInvariant() ?? string.Empty;
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static MaterialItemValidationException Validation(string key, string message) =>
        new(new Dictionary<string, string[]> { [key] = [message] });
}
