using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Catalog.Application;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Products.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Hadron.Erp.Api.Platform.Audit;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hadron.Erp.Api.Modules.Products.Application;

public sealed class ProductService(
    IDbContextFactory<HadronDbContext> factory,
    IAuditEventSink auditSink,
    IHttpContextAccessor accessor,
    ILogger<ProductService> logger,
    IItemBaseUnitChangeGuard? baseUnitChangeGuard = null)
{
    public async Task<ProductListResponse> GetList(string? search, bool includeInactive, Guid? productCategoryId,
        Guid? businessUnitId, int page, int pageSize, CancellationToken token)
    {
        if (page < 1 || pageSize is < 1 or > 100) throw Validation("page", "صفحه باید مثبت و اندازه صفحه بین ۱ تا ۱۰۰ باشد.");
        await using var db = await factory.CreateDbContextAsync(token);
        var query = db.Products.AsNoTracking().Where(x => includeInactive || x.Item.IsActive);
        if (productCategoryId.HasValue) query = query.Where(x => x.ProductCategoryId == productCategoryId);
        if (businessUnitId.HasValue) query = query.Where(x => x.BusinessUnitId == businessUnitId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Item.BusinessNumber.Contains(term) ||
                x.Item.NameFa.Contains(term) || (x.Item.NameEn != null && x.Item.NameEn.Contains(term)));
        }
        var total = await query.CountAsync(token);
        var rows = await query.OrderBy(x => x.Item.BusinessNumber).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                Product = x,
                Item = x.Item,
                Category = x.ProductCategory,
                Unit = x.Item.BaseUnit,
                BusinessUnit = x.BusinessUnit
            }).ToListAsync(token);
        var items = rows.Select(x => ToResponse(x.Product, x.Item, x.Category, x.Unit, x.BusinessUnit)).ToList();
        return new(items, page, pageSize, total);
    }

    public async Task<ProductResponse?> Get(Guid id, CancellationToken token)
    { await using var db = await factory.CreateDbContextAsync(token); return await LoadResponse(db, id, token); }

    public async Task<ProductMutationResponse> Create(CreateProductRequest request, CancellationToken token)
    {
        Validate(request.NameFa, request.NameEn, request.Notes, request.SafetyStockQty,
            request.CriticalDays, request.SafeStartDays, request.ExcessStartDays, null);
        await using var db = await factory.CreateDbContextAsync(token);
        await ValidateReferences(db, null, request.ProductCategoryId, request.BaseUnitId, request.BusinessUnitId, true, token);
        var number = await NextNumber(db, token);
        var item = new Item(ItemTypes.Product, number, request.NameFa.Trim(), Clean(request.NameEn), request.BaseUnitId, Clean(request.Notes));
        var product = new Product(item.Id, request.ProductCategoryId, request.BusinessUnitId, request.SafetyStockQty,
            request.CriticalDays, request.SafeStartDays, request.ExcessStartDays);
        db.AddRange(item, product);
        await Save(db, token);
        var response = (await LoadResponse(db, item.Id, token))!;
        var warnings = await DuplicateWarnings(db, item.Id, response.NameFa, response.NameEn, token);
        await Emit(item, "CREATE", null, Snapshot(response), null, token);
        return new(response, warnings);
    }

    public async Task<ProductMutationResponse> Update(Guid id, UpdateProductRequest request, CancellationToken token)
    {
        Validate(request.NameFa, request.NameEn, request.Notes, request.SafetyStockQty,
            request.CriticalDays, request.SafeStartDays, request.ExcessStartDays, request.ExpectedVersion);
        await using var db = await factory.CreateDbContextAsync(token);
        var product = await db.Products.Include(x => x.Item).SingleOrDefaultAsync(x => x.ItemId == id, token) ?? throw new KeyNotFoundException();
        EnsureVersion(product.Item.Version, request.ExpectedVersion);
        if (baseUnitChangeGuard is not null)
            await baseUnitChangeGuard.EnsureCanChange(product.Item.Id, product.Item.BaseUnitId,
                request.BaseUnitId, token);
        await ValidateReferences(db, product, request.ProductCategoryId, request.BaseUnitId, request.BusinessUnitId, false, token);
        var old = Snapshot((await LoadResponse(db, id, token))!);
        product.Item.Edit(request.NameFa.Trim(), Clean(request.NameEn), request.BaseUnitId, Clean(request.Notes));
        product.Edit(request.ProductCategoryId, request.BusinessUnitId, request.SafetyStockQty, request.CriticalDays, request.SafeStartDays, request.ExcessStartDays);
        await Save(db, token);
        var response = (await LoadResponse(db, id, token))!;
        var warnings = await DuplicateWarnings(db, id, response.NameFa, response.NameEn, token);
        await Emit(product.Item, "EDIT", old, Snapshot(response), null, token);
        return new(response, warnings);
    }

    public async Task<ProductResponse> ChangeState(Guid id, bool active, ChangeProductStateRequest request, CancellationToken token)
    {
        if (request.ExpectedVersion < 1) throw Validation("expectedVersion", "نسخه معتبر الزامی است.");
        await using var db = await factory.CreateDbContextAsync(token);
        var product = await db.Products.Include(x => x.Item).SingleOrDefaultAsync(x => x.ItemId == id, token) ?? throw new KeyNotFoundException();
        EnsureVersion(product.Item.Version, request.ExpectedVersion);
        if (active)
        {
            var invalid = await InvalidReferences(db, product.ProductCategoryId, product.Item.BaseUnitId, product.BusinessUnitId, token);
            if (invalid.Count > 0) throw new ProductConflictException("PRODUCT_REACTIVATION_BLOCKED", "مراجع عملیاتی محصول معتبر و فعال نیستند.", invalid);
        }
        var old = Snapshot((await LoadResponse(db, id, token))!);
        product.Item.ChangeState(active);
        await Save(db, token);
        var response = (await LoadResponse(db, id, token))!;
        await Emit(product.Item, active ? "REACTIVATE" : "DEACTIVATE", old, Snapshot(response), request.Reason, token);
        return response;
    }

    private static async Task ValidateReferences(HadronDbContext db, Product? current, Guid categoryId, Guid unitId, Guid? businessUnitId, bool create, CancellationToken token)
    {
        var changedCategory = create || current!.ProductCategoryId != categoryId;
        var changedUnit = create || current!.Item.BaseUnitId != unitId;
        var changedBusiness = create || current!.BusinessUnitId != businessUnitId;
        var invalid = await InvalidReferences(db, categoryId, unitId, businessUnitId, token,
            changedCategory, changedUnit, changedBusiness);
        if (invalid.Count > 0) throw new ProductValidationException(invalid.ToDictionary(x => x.Key, x => new[] { x.Value }));
    }

    private static async Task<Dictionary<string, string>> InvalidReferences(HadronDbContext db, Guid categoryId, Guid unitId,
        Guid? businessUnitId, CancellationToken token, bool checkCategory = true, bool checkUnit = true, bool checkBusiness = true)
    {
        var invalid = new Dictionary<string, string>();
        if (checkCategory && !await db.ReferenceValues.AnyAsync(x => x.Id == categoryId && x.ReferenceType.Code == "PRODUCT_CATEGORY" && x.IsActive, token)) invalid["productCategoryId"] = "گروه محصول باید از نوع PRODUCT_CATEGORY و فعال باشد.";
        if (checkUnit && !await db.Units.AnyAsync(x => x.Id == unitId && x.IsActive, token)) invalid["baseUnitId"] = "واحد پایه باید فعال باشد.";
        if (checkBusiness && businessUnitId.HasValue && !await db.ReferenceValues.AnyAsync(x => x.Id == businessUnitId && x.ReferenceType.Code == "BUSINESS_UNIT" && x.IsActive, token)) invalid["businessUnitId"] = "واحد سازمانی باید از نوع BUSINESS_UNIT و فعال باشد.";
        return invalid;
    }

    private static void Validate(string nameFa, string? nameEn, string? notes, decimal safety, int critical, int safe, int excess, long? version)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(nameFa) || nameFa.Trim().Length > 200) errors["nameFa"] = ["نام فارسی الزامی و حداکثر ۲۰۰ نویسه است."];
        if (Clean(nameEn)?.Length > 200) errors["nameEn"] = ["نام انگلیسی حداکثر ۲۰۰ نویسه است."];
        if (Clean(notes)?.Length > 2000) errors["notes"] = ["یادداشت حداکثر ۲۰۰۰ نویسه است."];
        if (safety < 0) errors["safetyStockQty"] = ["موجودی اطمینان نمی‌تواند منفی باشد."];
        if (critical < 0 || critical > safe || safe > excess) errors["thresholds"] = ["باید ۰ ≤ CriticalDays ≤ SafeStartDays ≤ ExcessStartDays باشد."];
        if (version.HasValue && version < 1) errors["expectedVersion"] = ["نسخه معتبر الزامی است."];
        if (errors.Count > 0) throw new ProductValidationException(errors);
    }

    private static async Task<string> NextNumber(HadronDbContext db, CancellationToken token)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(token);
        await using var command = new NpgsqlCommand("SELECT nextval('catalog.product_number_seq')", connection);
        var value = (long)(await command.ExecuteScalarAsync(token))!;
        return $"PRD-{value:D4}";
    }

    private static async Task<IReadOnlyList<string>> DuplicateWarnings(HadronDbContext db, Guid id, string nameFa, string? nameEn, CancellationToken token)
    {
        var warnings = new List<string>(); var fa = nameFa.Trim().ToUpper();
        if (await db.Items.AnyAsync(x => x.Id != id && x.ItemType == "PRODUCT" && x.NameFa.Trim().ToUpper() == fa, token)) warnings.Add("DUPLICATE_NAME_FA");
        var en = Clean(nameEn);
        if (en != null && await db.Items.AnyAsync(x => x.Id != id && x.ItemType == "PRODUCT" && x.NameEn != null && x.NameEn.Trim().ToUpper() == en.ToUpper(), token)) warnings.Add("DUPLICATE_NAME_EN");
        return warnings;
    }

    private static async Task<ProductResponse?> LoadResponse(HadronDbContext db, Guid id, CancellationToken token)
    {
        var row = await db.Products.AsNoTracking().Where(x => x.ItemId == id).Select(x => new
        {
            Product = x,
            Item = x.Item,
            Category = x.ProductCategory,
            Unit = x.Item.BaseUnit,
            BusinessUnit = x.BusinessUnit
        }).SingleOrDefaultAsync(token);
        if (row == null) return null;
        return ToResponse(row.Product, row.Item, row.Category, row.Unit, row.BusinessUnit);
    }

    private static ProductResponse ToResponse(Product product, Item item, ReferenceValue category, Unit unit, ReferenceValue? businessUnit) =>
        new(item.Id, item.BusinessNumber, item.NameFa, item.NameEn,
            new(category.Id, category.Code, category.NameFa, category.NameEn, category.IsActive),
            new(unit.Id, unit.Code, unit.NameFa, unit.NameEn, unit.Symbol, unit.IsActive),
            businessUnit == null ? null : new(businessUnit.Id, businessUnit.Code, businessUnit.NameFa, businessUnit.NameEn, businessUnit.IsActive),
            product.SafetyStockQty, product.CriticalDays, product.SafeStartDays, product.ExcessStartDays, item.IsActive,
            item.Notes, item.Version, item.CreatedAtUtc, item.UpdatedAtUtc);

    private async Task Emit(Item item, string action, IReadOnlyDictionary<string, object?>? oldValues,
        IReadOnlyDictionary<string, object?>? newValues, string? reason, CancellationToken token)
    {
        var context = accessor.HttpContext;
        try
        {
            await auditSink.WriteAsync(new("Product", item.Id, action, DateTime.UtcNow, AuditEventContext.ActorId(context),
                AuditEventContext.CorrelationId(context), oldValues, newValues, reason), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Post-commit Product audit emission failed for {ProductId} action {Action}.", item.Id, action);
        }
    }
    private static Dictionary<string, object?> Snapshot(ProductResponse p) => new() { ["Code"] = p.Code, ["NameFa"] = p.NameFa, ["NameEn"] = p.NameEn, ["ProductCategoryId"] = p.ProductCategory.Id, ["BaseUnitId"] = p.BaseUnit.Id, ["BusinessUnitId"] = p.BusinessUnit?.Id, ["SafetyStockQty"] = p.SafetyStockQty, ["CriticalDays"] = p.CriticalDays, ["SafeStartDays"] = p.SafeStartDays, ["ExcessStartDays"] = p.ExcessStartDays, ["IsActive"] = p.IsActive, ["Notes"] = p.Notes, ["Version"] = p.Version };
    private static async Task Save(HadronDbContext db, CancellationToken token)
    {
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new ProductConflictException("STALE_VERSION", "نسخه ارسالی قدیمی است."); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        { ConstraintName: "ck_item_base_unit_inventory" })
        { throw new ItemBaseUnitChangeBlockedException(); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) { throw new ProductConflictException("DUPLICATE_CODE", "کد محصول قبلاً ثبت شده است."); }
    }
    private static void EnsureVersion(long actual, long expected) { if (actual != expected) throw new ProductConflictException("STALE_VERSION", "نسخه ارسالی قدیمی است."); }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static ProductValidationException Validation(string key, string message) => new(new Dictionary<string, string[]> { [key] = [message] });
}
