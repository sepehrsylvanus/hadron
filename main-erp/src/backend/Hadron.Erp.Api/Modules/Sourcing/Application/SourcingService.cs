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

    public async Task<ItemSupplierLinkListResponse> ItemSuppliers(Guid itemId, bool includeInactive,
        int page, int pageSize, CancellationToken ct)
    {
        ValidatePaging(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Items.AsNoTracking().AnyAsync(x => x.Id == itemId &&
            (x.ItemType == ItemTypes.Part || x.ItemType == ItemTypes.Component), ct))
            throw new KeyNotFoundException();
        var query = db.SourcingOptions.AsNoTracking().Where(x => x.ItemId == itemId &&
            (includeInactive || x.IsActive));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.IsPreferred).ThenBy(x => x.SourcingNumber)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new ItemSupplierLinkResponse(x.Id, x.SourcingNumber,
                new SourcingSupplierReference(x.Supplier.Id, x.Supplier.SupplierNumber, x.Supplier.Name,
                    x.Supplier.IsActive), x.IsPreferred, x.IsActive, x.Version))
            .ToListAsync(ct);
        return new(rows, page, pageSize, total);
    }

    public async Task<SourcingSupplierCandidateListResponse> SupplierCandidates(Guid itemId, string? search,
        int page, int pageSize, CancellationToken ct)
    {
        ValidatePaging(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Items.AsNoTracking().AnyAsync(x => x.Id == itemId &&
            (x.ItemType == ItemTypes.Part || x.ItemType == ItemTypes.Component), ct))
            throw new KeyNotFoundException();
        var linkedSupplierIds = db.SourcingOptions.Where(x => x.ItemId == itemId).Select(x => x.SupplierId);
        var query = db.Suppliers.AsNoTracking().Where(x => x.IsActive && !linkedSupplierIds.Contains(x.Id));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.SupplierNumber.Contains(term) || x.Name.Contains(term));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.SupplierNumber).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new SourcingSupplierCandidateResponse(x.Id, x.SupplierNumber, x.Name)).ToListAsync(ct);
        return new(rows, page, pageSize, total);
    }

    public async Task<SourcingOptionListResponse> List(DateOnly evaluationDate, string? search, bool includeInactive,
        Guid? itemId, Guid? supplierId, string? itemType, bool? preferred, string? readiness, int page, int pageSize,
        CancellationToken ct)
    {
        ValidatePaging(page, pageSize);
        ValidateReadiness(readiness);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = ApplyFilters(db, search, includeInactive, itemId, supplierId, itemType, preferred, readiness);
        var total = await query.CountAsync(ct);
        var loaded = await LoadQuery(query).OrderBy(x => x.SourcingNumber).Skip((page - 1) * pageSize)
            .Take(pageSize).ToListAsync(ct);
        var responses = await ToResponses(db, loaded, evaluationDate, false, ct);
        return new(responses, page, pageSize, total, evaluationDate);
    }

    public async Task<SourcingTreeListResponse> Tree(DateOnly evaluationDate, string? search, bool includeInactive,
        Guid? itemId, Guid? supplierId, string? itemType, bool? preferred, string? readiness, int page, int pageSize,
        CancellationToken ct)
    {
        ValidatePaging(page, pageSize);
        ValidateReadiness(readiness);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = ApplyFilters(db, search, includeInactive, itemId, supplierId, itemType, preferred, readiness);
        var totalOptionCount = await query.CountAsync(ct);
        var itemKeys = query.Select(x => new { x.ItemId, x.Item.BusinessNumber }).Distinct();
        var totalItemCount = await itemKeys.CountAsync(ct);
        var pageItemIds = await itemKeys.OrderBy(x => x.BusinessNumber).ThenBy(x => x.ItemId)
            .Skip((page - 1) * pageSize).Take(pageSize).Select(x => x.ItemId).ToListAsync(ct);
        if (pageItemIds.Count == 0)
            return new([], page, pageSize, totalItemCount, totalOptionCount, evaluationDate);

        var loaded = await LoadQuery(query.Where(x => pageItemIds.Contains(x.ItemId)))
            .OrderBy(x => x.Item.BusinessNumber).ThenByDescending(x => x.IsPreferred)
            .ThenBy(x => x.Supplier.Name).ThenBy(x => x.SourcingNumber).ToListAsync(ct);
        var responses = await ToResponses(db, loaded, evaluationDate, false, ct);
        var itemOrder = pageItemIds.Select((id, index) => new { id, index }).ToDictionary(x => x.id, x => x.index);
        var groups = responses.GroupBy(x => x.Item.Id)
            .OrderBy(group => itemOrder[group.Key])
            .Select(group =>
            {
                var options = group.ToList();
                return new SourcingItemGroupResponse(options[0].Item, options.Count,
                    options.Count(x => x.Readiness.State == SourcingReadiness.Ready),
                    options.FirstOrDefault(x => x.IsPreferred)?.Id, options);
            }).ToList();
        return new(groups, page, pageSize, totalItemCount, totalOptionCount, evaluationDate);
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
        ValidateImageUrl(request.ImageUrl);
        var routeScope = ValidateRouteScope(request.RouteScope);
        ValidateCommercial(request.MinimumOrderQuantity, request.SupplierLeadTimeDays,
            request.ForwarderToShipmentDays, request.ShippingPeriodDays, request.DestinationCustomsDays, null);
        ValidateScopeFields(routeScope, request.IncotermId, request.ForwarderToShipmentDays,
            request.DestinationCustomsDays);
        await using var db = await factory.CreateDbContextAsync(ct);
        var item = await db.Items.Include(x => x.BaseUnit).SingleOrDefaultAsync(x => x.Id == request.ItemId, ct)
            ?? throw Validation("itemId", "قلم پیدا نشد.");
        var profile = await db.MaterialItemProfiles.SingleOrDefaultAsync(x => x.ItemId == item.Id, ct)
            ?? throw Validation("itemId", "قلم باید Part یا Component باشد.");
        if (item.ItemType is not (ItemTypes.Part or ItemTypes.Component) || !item.IsActive)
            throw Validation("itemId", "قلم Part/Component فعال الزامی است.");
        if (!ExternallySourcedMethods.Contains(profile.SupplyMethod))
            throw Validation("itemId", "این قلم «ساخت داخلی» است و تأمین‌کننده نمی‌پذیرد؛ مرکز کاری و زمان آماده‌سازی را در «مراکز کاری و مسیر ساخت» تعریف کنید. اگر قلم خریدنی یا برون‌سپاری است، ابتدا روش تأمین Master قلم را اصلاح کنید.");
        if (!await db.Suppliers.AnyAsync(x => x.Id == request.SupplierId && x.IsActive, ct))
            throw Validation("supplierId", "تأمین‌کننده فعال الزامی است.");
        await ValidateAssignedReferences(db, request.ShippingModeId, request.IncotermId, true,
            routeScope == SourcingRouteScopes.Import, ct);
        var option = new SourcingOption(await NextNumber(db, ct), item.Id, request.SupplierId, item.BaseUnitId,
            routeScope,
            request.MinimumOrderQuantity, request.ShippingModeId, request.IncotermId, request.SupplierLeadTimeDays,
            request.ForwarderToShipmentDays, request.ShippingPeriodDays, request.DestinationCustomsDays,
            Clean(request.ImageUrl));
        db.SourcingOptions.Add(option);
        await Save(db, ct);
        var response = (await Get(option.Id, evaluationDate, ct))!;
        await Emit(option.Id, "CREATE", response, null, ct);
        return response;
    }

    public async Task<SourcingOptionResponse> Update(Guid id, UpdateSourcingOptionRequest request,
        DateOnly evaluationDate, CancellationToken ct)
    {
        ValidateImageUrl(request.ImageUrl);
        ValidateCommercial(request.MinimumOrderQuantity, request.SupplierLeadTimeDays,
            request.ForwarderToShipmentDays, request.ShippingPeriodDays, request.DestinationCustomsDays,
            request.ExpectedVersion);
        await using var db = await factory.CreateDbContextAsync(ct);
        var option = await db.SourcingOptions.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException();
        EnsureVersion(option.Version, request.ExpectedVersion);
        // Older clients did not send RouteScope. Preserve the stored route on edit instead of
        // silently converting a domestic source back to IMPORT.
        var routeScope = ValidateRouteScope(request.RouteScope ?? option.RouteScope);
        ValidateScopeFields(routeScope, request.IncotermId, request.ForwarderToShipmentDays,
            request.DestinationCustomsDays);
        await ValidateAssignedReferences(db, request.ShippingModeId, request.IncotermId,
            option.ShippingModeId != request.ShippingModeId,
            routeScope == SourcingRouteScopes.Import && option.IncotermId != request.IncotermId, ct);
        option.Edit(routeScope, request.MinimumOrderQuantity, request.ShippingModeId, request.IncotermId,
            request.SupplierLeadTimeDays, request.ForwarderToShipmentDays, request.ShippingPeriodDays,
            request.DestinationCustomsDays, request.ImageUrl is null ? option.ImageUrl : Clean(request.ImageUrl));
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

    public async Task<IReadOnlyList<LeadTimeRecommendationApplicationResponse>> ApplyLeadTimeRecommendation(
        Guid optionId, ApplyLeadTimeRecommendationCommand command, DateOnly evaluationDate, CancellationToken ct)
    {
        if (command.ExpectedVersion < 1) throw Validation("expectedVersion", "نسخه معتبر الزامی است.");
        if (command.Stages.Count == 0) throw Validation("stages", "حداقل یک مرحله باید صریحاً انتخاب شود.");
        if (command.Stages.Select(x => x.StageCode).Distinct(StringComparer.Ordinal).Count() != command.Stages.Count)
            throw Validation("stages", "هر مرحله فقط یک بار قابل انتخاب است.");
        if (command.Stages.Any(x => !LeadTimeStageCodes.All.Contains(x.StageCode) || x.RecommendedDays < 0 ||
            x.SampleCount < 5 || x.WindowEndInclusive <= x.WindowStartExclusive))
            throw Validation("stages", "پیشنهاد Lead Time معتبر نیست.");

        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var option = await db.SourcingOptions.FromSqlInterpolated(
            $"SELECT * FROM procurement.sourcing_options WHERE \"Id\" = {optionId} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw new KeyNotFoundException();
        EnsureVersion(option.Version, command.ExpectedVersion);
        if (option.RouteScope == SourcingRouteScopes.Domestic && command.Stages.Any(x =>
            x.StageCode is LeadTimeStageCodes.ForwarderPreShipment or LeadTimeStageCodes.DestinationCustoms))
            throw Validation("stages", "مرحله فورواردر یا گمرک برای مسیر داخلی قابل اعمال نیست.");

        var actor = AuditEventContext.ActorId(accessor.HttpContext);
        var applications = new List<LeadTimeRecommendationApplication>();
        foreach (var stage in command.Stages)
        {
            var previous = stage.StageCode switch
            {
                LeadTimeStageCodes.Supplier => option.SupplierLeadTimeDays,
                LeadTimeStageCodes.ForwarderPreShipment => option.ForwarderToShipmentDays,
                LeadTimeStageCodes.Shipping => option.ShippingPeriodDays,
                LeadTimeStageCodes.DestinationCustoms => option.DestinationCustomsDays,
                _ => null
            };
            applications.Add(new(option.Id, stage.StageCode, previous, stage.RecommendedDays,
                stage.SampleCount, stage.WindowStartExclusive, stage.WindowEndInclusive, command.Reason, actor));
        }

        var values = command.Stages.ToDictionary(x => x.StageCode, StringComparer.Ordinal);
        option.Edit(option.RouteScope, option.MinimumOrderQuantity, option.ShippingModeId, option.IncotermId,
            values.TryGetValue(LeadTimeStageCodes.Supplier, out var supplier) ? supplier.RecommendedDays : option.SupplierLeadTimeDays,
            values.TryGetValue(LeadTimeStageCodes.ForwarderPreShipment, out var forwarder) ? forwarder.RecommendedDays : option.ForwarderToShipmentDays,
            values.TryGetValue(LeadTimeStageCodes.Shipping, out var shipping) ? shipping.RecommendedDays : option.ShippingPeriodDays,
            values.TryGetValue(LeadTimeStageCodes.DestinationCustoms, out var destination) ? destination.RecommendedDays : option.DestinationCustomsDays);
        db.LeadTimeRecommendationApplications.AddRange(applications);
        await Save(db, ct);
        await transaction.CommitAsync(ct);
        var response = applications.Select(ToApplicationResponse).ToList();
        await Emit(optionId, "APPLY_LEAD_TIME_RECOMMENDATION", response, command.Reason, ct);
        return response;
    }

    public async Task<IReadOnlyList<LeadTimeRecommendationApplicationResponse>> ListLeadTimeApplications(
        Guid optionId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.LeadTimeRecommendationApplications.AsNoTracking().Where(x => x.SourcingOptionId == optionId)
            .OrderByDescending(x => x.AppliedAtUtc).Select(x => new LeadTimeRecommendationApplicationResponse(
                x.Id, x.SourcingOptionId, x.StageCode, x.PreviousDays, x.RecommendedDays, x.AppliedDays,
                x.SampleCount, x.WindowStartExclusive, x.WindowEndInclusive, x.Method, x.Reason,
                x.ActorIdentifier, x.AppliedAtUtc)).ToListAsync(ct);
    }

    private static LeadTimeRecommendationApplicationResponse ToApplicationResponse(LeadTimeRecommendationApplication x) =>
        new(x.Id, x.SourcingOptionId, x.StageCode, x.PreviousDays, x.RecommendedDays, x.AppliedDays,
            x.SampleCount, x.WindowStartExclusive, x.WindowEndInclusive, x.Method, x.Reason,
            x.ActorIdentifier, x.AppliedAtUtc);

    private static IQueryable<SourcingOption> LoadQuery(IQueryable<SourcingOption> query) => query
        .Include(x => x.Item).ThenInclude(x => x.BaseUnit).Include(x => x.Supplier).Include(x => x.ItemBaseUnit)
        .Include(x => x.ShippingMode).Include(x => x.Incoterm);

    private static IQueryable<SourcingOption> ApplyFilters(HadronDbContext db, string? search,
        bool includeInactive, Guid? itemId, Guid? supplierId, string? itemType, bool? preferred, string? readiness)
    {
        var query = db.SourcingOptions.AsNoTracking().Where(x => includeInactive || x.IsActive);
        if (itemId.HasValue) query = query.Where(x => x.ItemId == itemId);
        if (supplierId.HasValue) query = query.Where(x => x.SupplierId == supplierId);
        if (!string.IsNullOrWhiteSpace(itemType)) query = query.Where(x => x.Item.ItemType == itemType);
        if (preferred.HasValue) query = query.Where(x => x.IsPreferred == preferred);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.SourcingNumber.Contains(term) || x.Item.BusinessNumber.Contains(term) ||
                x.Item.NameFa.Contains(term) || x.Item.NameEn != null && x.Item.NameEn.Contains(term) ||
                x.Supplier.SupplierNumber.Contains(term) || x.Supplier.Name.Contains(term));
        }

        var blocked = query.Where(x => x.IsActive && (!x.Item.IsActive || !x.Supplier.IsActive ||
            (x.RouteScope != SourcingRouteScopes.Domestic && x.RouteScope != SourcingRouteScopes.Import) ||
            x.ItemBaseUnitId != x.Item.BaseUnitId ||
            !db.MaterialItemProfiles.Any(p => p.ItemId == x.ItemId && ExternallySourcedMethods.Contains(p.SupplyMethod)) ||
            (x.ShippingModeId != null && !x.ShippingMode!.IsActive) ||
            (x.RouteScope == SourcingRouteScopes.Import && x.IncotermId != null && !x.Incoterm!.IsActive)));
        var incomplete = query.Where(x => x.IsActive && !blocked.Any(b => b.Id == x.Id) &&
            (x.MinimumOrderQuantity == null || x.ShippingModeId == null ||
             x.SupplierLeadTimeDays == null || x.ShippingPeriodDays == null ||
             (x.RouteScope == SourcingRouteScopes.Import &&
              (x.ForwarderToShipmentDays == null || x.DestinationCustomsDays == null))));
        return readiness switch
        {
            SourcingReadiness.Inactive => query.Where(x => !x.IsActive),
            SourcingReadiness.Blocked => query.Where(x => blocked.Any(b => b.Id == x.Id)),
            SourcingReadiness.IncompleteData => query.Where(x => incomplete.Any(b => b.Id == x.Id)),
            SourcingReadiness.Ready => query.Where(x => x.IsActive && !blocked.Any(b => b.Id == x.Id) &&
                !incomplete.Any(b => b.Id == x.Id)),
            _ => query
        };
    }

    private static async Task<List<SourcingOptionResponse>> ToResponses(HadronDbContext db,
        IReadOnlyList<SourcingOption> options, DateOnly evaluationDate, bool includeHistory, CancellationToken ct)
    {
        if (options.Count == 0) return [];
        var itemIds = options.Select(x => x.ItemId).Distinct().ToArray();
        var methods = await db.MaterialItemProfiles.AsNoTracking().Where(x => itemIds.Contains(x.ItemId))
            .ToDictionaryAsync(x => x.ItemId, x => x.SupplyMethod, ct);
        return options.Select(option =>
        {
            methods.TryGetValue(option.ItemId, out var method);
            method ??= string.Empty;
            var readiness = SourcingReadinessEvaluator.Evaluate(option, method, option.Item.BaseUnitId);
            return new SourcingOptionResponse(option.Id, option.SourcingNumber,
                new(option.Item.Id, option.Item.BusinessNumber, option.Item.NameFa, option.Item.NameEn,
                    option.Item.ItemType, method, option.Item.IsActive, Unit(option.Item.BaseUnit)),
                new(option.Supplier.Id, option.Supplier.SupplierNumber, option.Supplier.Name, option.Supplier.IsActive),
                Unit(option.ItemBaseUnit), option.RouteScope, option.MinimumOrderQuantity,
                Ref(option.ShippingMode), Ref(option.Incoterm),
                option.SupplierLeadTimeDays, option.ForwarderToShipmentDays, option.ShippingPeriodDays,
                option.DestinationCustomsDays, readiness.TotalLeadTimeDays, option.IsPreferred, option.IsActive, option.Version,
                evaluationDate, new(readiness.State, readiness.ReasonCodes), option.CreatedAtUtc, option.UpdatedAtUtc,
                option.ImageUrl);
        }).ToList();
    }
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
        if (option.RouteScope == SourcingRouteScopes.Import && option.IncotermId.HasValue &&
            !await ActiveReference(db, option.IncotermId.Value, "INCOTERM", ct))
            errors["incotermId"] = ["اینکوترم تخصیص‌یافته باید فعال باشد."];
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

    private static string? Clean(string? x) => string.IsNullOrWhiteSpace(x) ? null : x.Trim();

    private static void ValidateImageUrl(string? value)
    {
        var imageUrl = Clean(value);
        if (imageUrl is not null && (imageUrl.Length > 2048 ||
            !Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
        {
            throw Validation("imageUrl", "نشانی تصویر باید URL معتبر HTTPS و حداکثر ۲۰۴۸ نویسه باشد.");
        }
    }

    private static string ValidateRouteScope(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return SourcingRouteScopes.Import;
        var normalized = value?.Trim().ToUpperInvariant();
        if (normalized is null || !SourcingRouteScopes.All.Contains(normalized))
            throw Validation("routeScope", "نوع مسیر تأمین باید داخلی یا وارداتی باشد.");
        return normalized;
    }

    private static void ValidateScopeFields(string routeScope, Guid? incotermId,
        int? forwarderDays, int? customsDays)
    {
        if (routeScope != SourcingRouteScopes.Domestic) return;
        var errors = new Dictionary<string, string[]>();
        if (incotermId.HasValue) errors["incotermId"] = ["اینکوترم برای تأمین داخلی کاربرد ندارد."];
        if (forwarderDays.HasValue) errors["forwarderToShipmentDays"] = ["مرحله فورواردر برای تأمین داخلی کاربرد ندارد."];
        if (customsDays.HasValue) errors["destinationCustomsDays"] = ["مرحله گمرک برای تأمین داخلی کاربرد ندارد."];
        if (errors.Count > 0) throw new SourcingValidationException(errors);
    }

    private static void ValidatePaging(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100) throw Validation("page", "صفحه معتبر و اندازه ۱ تا ۱۰۰ الزامی است.");
    }

    private static void ValidateReadiness(string? readiness)
    {
        if (readiness is not null && !new[] { SourcingReadiness.Inactive, SourcingReadiness.Blocked,
                SourcingReadiness.IncompleteData, SourcingReadiness.Ready }.Contains(readiness))
            throw Validation("readiness", "وضعیت آمادگی معتبر نیست.");
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
