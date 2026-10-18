using System.Security.Claims;
using System.Globalization;
using Hadron.Erp.Api.Modules.Planning.Application;
using Hadron.Erp.Api.Modules.Identity.Application;
using System.Text.Json;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Trading.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Hadron.Erp.Api.Platform.Security;

namespace Hadron.Erp.Api.Modules.Trading.Application;

public sealed class TradingService(IDbContextFactory<HadronDbContext> factory, IHttpContextAccessor accessor, IActiveUserDirectory userDirectory, IPlanningCalendar calendar)
{
    private static readonly string[] ReferenceTypes = ["CUSTOMER", "BUSINESS_CARD", "BANK", "FORWARDER", "CARRIER", "DESTINATION", "INSURANCE_COMPANY", "INSPECTION_COMPANY", "CUSTOMS_OFFICE", "CUSTOMS_BROKER"];
    private static readonly string[] Priorities = ["LOW", "NORMAL", "HIGH", "CRITICAL"];
    private static readonly string[] OrderStatuses = ["DRAFT", "SUBMITTED", "ACTIVE", "SUSPENDED", "CANCELLED"];
    private static readonly string[] FxProcesses = ["REQUEST", "ALLOCATION", "DEAL", "RECEIPT", "CORRECTION"];
    private static readonly string[] FxDeals = ["NOT_STARTED", "IN_QUEUE", "PRIORITY_CONFIRMED", "ALLOCATED", "DEAL_DONE", "CANCELLED"];
    private static readonly string[] FxReceipts = ["NOT_RECEIVED", "PARTIAL", "COMPLETE", "CANCELLED"];
    private static readonly string[] ShipmentStatuses = ["DRAFT", "WAITING_LINK", "READY", "IN_TRANSIT", "ARRIVED", "IN_CUSTOMS", "PARTIAL_CLEARED", "CLEARED", "DELIVERED", "CANCELLED"];
    private static readonly string[] LegStatuses = ["PLANNED", "DEPARTED", "IN_TRANSIT", "ARRIVED", "DELAYED", "CANCELLED"];
    private static readonly string[] CustomsStatuses = ["NOT_STARTED", "DOCUMENTS_PENDING", "IN_PROGRESS", "PARTIAL", "COMPLETE", "BLOCKED", "CANCELLED"];
    private static readonly string[] TransportModes = ["SEA", "AIR", "ROAD", "RAIL", "MULTIMODAL"];
    private static readonly string[] ClearanceTypes = ["DEFINITIVE", "TEMPORARY", "TRANSIT", "RETURN"];
    private static readonly string[] AllocationMethods = ["GOODS_VALUE", "CBM", "WEIGHT", "COUNT", "MANUAL_PERCENT", "EQUAL"];
    private static readonly string[] CostScopes = ["PROJECT", "SHIPMENT", "SHIPMENT_LEG", "CUSTOMS"];
    private static readonly IReadOnlySet<(string From, string To)> ProjectTransitions = Pairs(
        ("DRAFT", "ACTIVE"), ("ACTIVE", "WAITING_ORDER"), ("WAITING_ORDER", "FX_PROCESS"),
        ("FX_PROCESS", "READY_TO_SHIP"), ("READY_TO_SHIP", "IN_TRANSIT"), ("IN_TRANSIT", "IN_CUSTOMS"),
        ("IN_CUSTOMS", "CLEARED"), ("CLEARED", "DELIVERED"), ("DELIVERED", "CLOSED"),
        ("DRAFT", "CANCELLED"), ("ACTIVE", "CANCELLED"), ("WAITING_ORDER", "CANCELLED"),
        ("FX_PROCESS", "CANCELLED"), ("READY_TO_SHIP", "CANCELLED"), ("IN_TRANSIT", "CANCELLED"),
        ("IN_CUSTOMS", "CANCELLED"), ("CLEARED", "CANCELLED"));
    private static readonly IReadOnlySet<(string From, string To)> OrderTransitions = Pairs(
        ("DRAFT", "SUBMITTED"), ("SUBMITTED", "ACTIVE"), ("SUBMITTED", "SUSPENDED"),
        ("ACTIVE", "SUSPENDED"), ("SUSPENDED", "ACTIVE"), ("DRAFT", "CANCELLED"),
        ("SUBMITTED", "CANCELLED"), ("ACTIVE", "CANCELLED"), ("SUSPENDED", "CANCELLED"));
    private static readonly IReadOnlySet<(string From, string To)> ShipmentTransitions = Pairs(
        ("DRAFT", "WAITING_LINK"), ("WAITING_LINK", "READY"), ("READY", "IN_TRANSIT"),
        ("IN_TRANSIT", "ARRIVED"), ("ARRIVED", "IN_CUSTOMS"), ("IN_CUSTOMS", "PARTIAL_CLEARED"),
        ("IN_CUSTOMS", "CLEARED"), ("PARTIAL_CLEARED", "CLEARED"), ("CLEARED", "DELIVERED"),
        ("DRAFT", "CANCELLED"), ("WAITING_LINK", "CANCELLED"), ("READY", "CANCELLED"),
        ("IN_TRANSIT", "CANCELLED"), ("ARRIVED", "CANCELLED"), ("IN_CUSTOMS", "CANCELLED"),
        ("PARTIAL_CLEARED", "CANCELLED"), ("CLEARED", "CANCELLED"));
    private static readonly IReadOnlySet<(string From, string To)> LegTransitions = Pairs(
        ("PLANNED", "DEPARTED"), ("DEPARTED", "IN_TRANSIT"), ("DEPARTED", "DELAYED"),
        ("IN_TRANSIT", "ARRIVED"), ("IN_TRANSIT", "DELAYED"), ("DELAYED", "IN_TRANSIT"),
        ("DELAYED", "ARRIVED"), ("PLANNED", "CANCELLED"), ("DEPARTED", "CANCELLED"),
        ("IN_TRANSIT", "CANCELLED"), ("DELAYED", "CANCELLED"));
    private static readonly IReadOnlySet<(string From, string To)> CustomsTransitions = Pairs(
        ("NOT_STARTED", "DOCUMENTS_PENDING"), ("DOCUMENTS_PENDING", "IN_PROGRESS"),
        ("DOCUMENTS_PENDING", "BLOCKED"), ("IN_PROGRESS", "PARTIAL"), ("IN_PROGRESS", "COMPLETE"),
        ("IN_PROGRESS", "BLOCKED"), ("PARTIAL", "COMPLETE"), ("PARTIAL", "BLOCKED"),
        ("BLOCKED", "DOCUMENTS_PENDING"), ("BLOCKED", "IN_PROGRESS"), ("NOT_STARTED", "CANCELLED"),
        ("DOCUMENTS_PENDING", "CANCELLED"), ("IN_PROGRESS", "CANCELLED"), ("PARTIAL", "CANCELLED"),
        ("BLOCKED", "CANCELLED"));

    public Task<TradingOverviewResponse> Overview(CancellationToken ct) => Overview(TradingOverviewReadAccess.Full, ct);

    public async Task<TradingOverviewResponse> Overview(TradingOverviewReadAccess access, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var openProjects = await db.TradeProjects.CountAsync(x => x.Status != "CLOSED" && x.Status != "CANCELLED", ct);
        var critical = await db.TradeProjects.CountAsync(x => x.Priority == "CRITICAL" && x.Status != "CLOSED" && x.Status != "CANCELLED", ct);
        int? activeShipments = access.Shipments ? await db.TradeShipments.CountAsync(x => x.Status != "DELIVERED" && x.Status != "CANCELLED", ct) : null;
        int? customs = access.Customs ? await db.TradeCustomsClearances.CountAsync(x => x.Status != "COMPLETE" && x.Status != "CANCELLED", ct) : null;
        var recent = await db.TradeProjects.AsNoTracking().OrderByDescending(x => x.UpdatedAtUtc).Take(8).Select(ProjectSummary()).ToListAsync(ct);
        var upcomingEntities = access.Shipments
            ? await db.TradeShipments.AsNoTracking().Include(x => x.Legs)
                .Where(x => x.Status != "DELIVERED" && x.Status != "CANCELLED")
                .OrderBy(x => x.EstimatedArrival).Take(8).ToListAsync(ct)
            : [];
        return new(openProjects, activeShipments, customs, critical, recent, upcomingEntities.Select(ShipmentOf).ToList());
    }

    public async Task<TradeProjectListResponse> ListProjects(string? search, string? status, string? priority,
        string? sortBy, string? sortDirection, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize); await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.TradeProjects.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim(); query = query.Where(x => x.ProjectNumber.Contains(term) || x.TitleFa.Contains(term) || (x.TitleEn != null && x.TitleEn.Contains(term)) || (x.CustomerNameFaSnapshot != null && x.CustomerNameFaSnapshot.Contains(term)) || (x.SupplierNameSnapshot != null && x.SupplierNameSnapshot.Contains(term))); }
        if (!string.IsNullOrWhiteSpace(status)) { var value = Code(status, TradeProjectStatuses.All, "status"); query = query.Where(x => x.Status == value); }
        if (!string.IsNullOrWhiteSpace(priority)) { var value = Code(priority, Priorities, "priority"); query = query.Where(x => x.Priority == value); }
        var total = await query.CountAsync(ct);
        var descending = Descending(sortDirection);
        var ordered = (sortBy?.Trim().ToUpperInvariant()) switch
        {
            "PROJECT_NUMBER" => descending ? query.OrderByDescending(x => x.ProjectNumber) : query.OrderBy(x => x.ProjectNumber),
            "TARGET_DATE" => descending ? query.OrderByDescending(x => x.TargetDate) : query.OrderBy(x => x.TargetDate),
            "STATUS" => descending ? query.OrderByDescending(x => x.Status) : query.OrderBy(x => x.Status),
            "PRIORITY" => descending ? query.OrderByDescending(x => x.Priority) : query.OrderBy(x => x.Priority),
            _ => query.OrderByDescending(x => x.UpdatedAtUtc)
        };
        var rows = await ordered.ThenBy(x => x.ProjectNumber).Skip((page - 1) * pageSize).Take(pageSize).Select(ProjectSummary()).ToListAsync(ct);
        return new(rows, page, pageSize, total);
    }

    public async Task<TradeShipmentListResponse> ListShipments(string? search, string? status,
        string? sortBy, string? sortDirection, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize); await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.TradeShipments.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.ShipmentNumber.Contains(term) || x.Origin.Contains(term) ||
                x.Destination.Contains(term) || x.ProjectLinks.Any(link =>
                    link.Project.ProjectNumber.Contains(term) || link.Project.TitleFa.Contains(term) ||
                    (link.Project.TitleEn != null && link.Project.TitleEn.Contains(term))));
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            var value = Code(status, ShipmentStatuses, "status"); query = query.Where(x => x.Status == value);
        }
        var total = await query.CountAsync(ct);
        var descending = Descending(sortDirection);
        var ordered = (sortBy?.Trim().ToUpperInvariant()) switch
        {
            "SHIPMENT_NUMBER" => descending ? query.OrderByDescending(x => x.ShipmentNumber) : query.OrderBy(x => x.ShipmentNumber),
            "STATUS" => descending ? query.OrderByDescending(x => x.Status) : query.OrderBy(x => x.Status),
            "DEPARTURE" => descending ? query.OrderByDescending(x => x.EstimatedDeparture) : query.OrderBy(x => x.EstimatedDeparture),
            "ARRIVAL" => descending ? query.OrderByDescending(x => x.EstimatedArrival) : query.OrderBy(x => x.EstimatedArrival),
            _ => query.OrderBy(x => x.EstimatedArrival)
        };
        var rows = await ordered.ThenByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize).Include(x => x.Legs)
            .Include(x => x.ProjectLinks).ThenInclude(x => x.Project).AsSplitQuery().ToListAsync(ct);
        return new(rows.Select(x => new TradeShipmentWorklistItem(ShipmentOf(x),
            x.ProjectLinks.OrderBy(link => link.Project.ProjectNumber).Select(link => ProjectContextOf(link.Project)).ToList())).ToList(),
            page, pageSize, total);
    }

    public async Task<TradeCustomsListResponse> ListCustoms(string? search, string? status,
        string? sortBy, string? sortDirection, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize); await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.TradeCustomsClearances.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.ClearanceNumber.Contains(term) || x.Shipment.ShipmentNumber.Contains(term) ||
                x.Shipment.ProjectLinks.Any(link => link.Project.ProjectNumber.Contains(term) ||
                    link.Project.TitleFa.Contains(term) || (link.Project.TitleEn != null && link.Project.TitleEn.Contains(term))));
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            var value = Code(status, CustomsStatuses, "status"); query = query.Where(x => x.Status == value);
        }
        var total = await query.CountAsync(ct);
        var descending = Descending(sortDirection);
        var ordered = (sortBy?.Trim().ToUpperInvariant()) switch
        {
            "CLEARANCE_NUMBER" => descending ? query.OrderByDescending(x => x.ClearanceNumber) : query.OrderBy(x => x.ClearanceNumber),
            "STATUS" => descending ? query.OrderByDescending(x => x.Status) : query.OrderBy(x => x.Status),
            "RELEASE_DATE" => descending ? query.OrderByDescending(x => x.ReleaseDate) : query.OrderBy(x => x.ReleaseDate),
            _ => query.OrderBy(x => x.ReleaseDate)
        };
        var rows = await ordered.ThenByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize).Include(x => x.Shipment)
            .ThenInclude(x => x.ProjectLinks).ThenInclude(x => x.Project).AsSplitQuery().ToListAsync(ct);
        return new(rows.Select(x => new TradeCustomsWorklistItem(CustomsOf(x), x.Shipment.ShipmentNumber,
            x.Shipment.ProjectLinks.OrderBy(link => link.Project.ProjectNumber).Select(link => ProjectContextOf(link.Project)).ToList())).ToList(),
            page, pageSize, total);
    }

    public async Task<TradeCostListResponse> ListCosts(string? search, string? currencyCode, bool includeVoided,
        string? sortBy, string? sortDirection, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize); await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.TradeCosts.AsNoTracking().Where(x => includeVoided || !x.IsVoided);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.CostType.Contains(term) || (x.ReferenceNumber != null && x.ReferenceNumber.Contains(term)) ||
                x.Project.ProjectNumber.Contains(term) || x.Project.TitleFa.Contains(term) ||
                (x.Project.TitleEn != null && x.Project.TitleEn.Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(currencyCode))
        {
            var value = currencyCode.Trim().ToUpperInvariant(); query = query.Where(x => x.CurrencyCodeSnapshot == value);
        }
        var total = await query.CountAsync(ct);
        var descending = Descending(sortDirection);
        var ordered = (sortBy?.Trim().ToUpperInvariant()) switch
        {
            "COST_TYPE" => descending ? query.OrderByDescending(x => x.CostType) : query.OrderBy(x => x.CostType),
            "AMOUNT" => descending ? query.OrderByDescending(x => x.Amount) : query.OrderBy(x => x.Amount),
            "CURRENCY" => descending ? query.OrderByDescending(x => x.CurrencyCodeSnapshot) : query.OrderBy(x => x.CurrencyCodeSnapshot),
            "EFFECTIVE_DATE" => descending ? query.OrderByDescending(x => x.EffectiveDate) : query.OrderBy(x => x.EffectiveDate),
            _ => query.OrderByDescending(x => x.EffectiveDate)
        };
        var rows = await ordered.ThenByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize).Include(x => x.Project).ToListAsync(ct);
        return new(rows.Select(x => new TradeCostWorklistItem(CostOf(x), ProjectContextOf(x.Project))).ToList(),
            page, pageSize, total);
    }

    public async Task<TradeReportListResponse> ListReports(string? search, string? status,
        string? sortBy, string? sortDirection, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize); await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.TradeProjects.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim(); query = query.Where(x => x.ProjectNumber.Contains(term) || x.TitleFa.Contains(term) ||
                (x.TitleEn != null && x.TitleEn.Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            var value = Code(status, TradeProjectStatuses.All, "status"); query = query.Where(x => x.Status == value);
        }
        var total = await query.CountAsync(ct);
        var descending = Descending(sortDirection);
        var ordered = (sortBy?.Trim().ToUpperInvariant()) switch
        {
            "PROJECT_NUMBER" => descending ? query.OrderByDescending(x => x.ProjectNumber) : query.OrderBy(x => x.ProjectNumber),
            "STATUS" => descending ? query.OrderByDescending(x => x.Status) : query.OrderBy(x => x.Status),
            "TARGET_DATE" => descending ? query.OrderByDescending(x => x.TargetDate) : query.OrderBy(x => x.TargetDate),
            _ => query.OrderByDescending(x => x.UpdatedAtUtc)
        };
        var projects = await ordered.ThenBy(x => x.ProjectNumber).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(ProjectSummary()).ToListAsync(ct);
        var projectIds = projects.Select(x => x.Id).ToArray();
        var orderCounts = await db.TradeOrderRegistrations.AsNoTracking().Where(x => projectIds.Contains(x.ProjectId))
            .GroupBy(x => x.ProjectId).Select(x => new { ProjectId = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.ProjectId, x => x.Count, ct);
        var fxCounts = await db.TradeFxAllocations.AsNoTracking().Where(x => projectIds.Contains(x.ProjectId))
            .GroupBy(x => x.ProjectId).Select(x => new { ProjectId = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.ProjectId, x => x.Count, ct);
        var shipmentCounts = await db.TradeProjectShipments.AsNoTracking().Where(x => projectIds.Contains(x.ProjectId))
            .GroupBy(x => x.ProjectId).Select(x => new { ProjectId = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.ProjectId, x => x.Count, ct);
        var customsCounts = await db.TradeProjectShipments.AsNoTracking().Where(x => projectIds.Contains(x.ProjectId))
            .SelectMany(x => x.Shipment.CustomsClearances.Select(clearance => new { x.ProjectId, clearance.Id }))
            .Distinct().GroupBy(x => x.ProjectId).Select(x => new { ProjectId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.ProjectId, x => x.Count, ct);
        var costRows = await db.TradeCosts.AsNoTracking().Where(x => projectIds.Contains(x.ProjectId) && !x.IsVoided)
            .GroupBy(x => new { x.ProjectId, x.CurrencyCodeSnapshot, x.CurrencyNameFaSnapshot, x.CurrencyNameEnSnapshot })
            .Select(x => new
            {
                x.Key.ProjectId,
                x.Key.CurrencyCodeSnapshot,
                x.Key.CurrencyNameFaSnapshot,
                x.Key.CurrencyNameEnSnapshot,
                Amount = x.Sum(v => v.Amount)
            }).ToListAsync(ct);
        var rows = projects.Select(project => new TradeReportWorklistItem(project,
            orderCounts.GetValueOrDefault(project.Id), fxCounts.GetValueOrDefault(project.Id),
            shipmentCounts.GetValueOrDefault(project.Id), customsCounts.GetValueOrDefault(project.Id),
            costRows.Where(x => x.ProjectId == project.Id).OrderBy(x => x.CurrencyCodeSnapshot)
                .Select(x => new TradeCurrencyTotal(x.CurrencyCodeSnapshot, x.CurrencyNameFaSnapshot,
                    x.CurrencyNameEnSnapshot, x.Amount)).ToList())).ToList();
        return new(rows, page, pageSize, total);
    }

    public Task<TradeProjectDetailResponse?> GetProject(Guid id, CancellationToken ct) =>
        GetProject(id, TradingProjectReadAccess.Full, ct);

    public async Task<TradeProjectDetailResponse?> GetProject(Guid id, TradingProjectReadAccess access, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var project = await db.TradeProjects.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (project is null) return null;
        var orders = access.Orders ? await db.TradeOrderRegistrations.AsNoTracking().Where(x => x.ProjectId == id).OrderByDescending(x => x.RegistrationDate).ToListAsync(ct) : [];
        var fx = access.Fx ? await db.TradeFxAllocations.AsNoTracking().Where(x => x.ProjectId == id).OrderByDescending(x => x.RequestDate).ToListAsync(ct) : [];
        var links = access.Shipments ? await db.TradeProjectShipments.AsNoTracking().Where(x => x.ProjectId == id)
            .Include(x => x.Shipment).ThenInclude(x => x.Legs).OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct) : [];
        var shipmentIds = links.Select(x => x.ShipmentId).ToArray();
        var customs = access.Shipments && access.Customs ? await db.TradeCustomsClearances.AsNoTracking().Where(x => shipmentIds.Contains(x.ShipmentId)).ToListAsync(ct) : [];
        var costs = access.Costs ? await db.TradeCosts.AsNoTracking().Where(x => x.ProjectId == id).OrderByDescending(x => x.EffectiveDate).ToListAsync(ct) : [];
        var audit = access.Audit ? await db.TradeAuditEvents.AsNoTracking().Where(x => x.EntityId == id || shipmentIds.Contains(x.EntityId)).OrderByDescending(x => x.OccurredAtUtc).Take(100).ToListAsync(ct) : [];
        return new(ProjectSummary().Compile()(project), project.Notes, orders.Select(OrderOf).ToList(), fx.Select(FxOf).ToList(),
            links.Select(link => new TradeProjectShipmentResponse(link.Id, link.AllocationMethod, link.AllocationValue, link.Notes, link.Version,
                ShipmentOf(link.Shipment), customs.Where(x => x.ShipmentId == link.ShipmentId).Select(CustomsOf).ToList())).ToList(),
            costs.Select(CostOf).ToList(), audit.Select(AuditOf).ToList(),
            new(access.Orders, access.Fx, access.Shipments, access.Customs, access.Costs, access.Audit));
    }

    public async Task<TradingLookupResponse> Lookups(bool includeInactive, string? search, int limit, CancellationToken ct)
    {
        if (limit is < 1 or > 100) throw Validation("limit", "حداکثر تعداد Lookup باید بین ۱ تا ۱۰۰ باشد.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var term = search?.Trim();
        var currencyQuery = db.Currencies.AsNoTracking().Where(x => includeInactive || x.IsActive);
        var supplierQuery = db.Suppliers.AsNoTracking().Where(x => includeInactive || x.IsActive);
        var referenceQuery = db.TradeReferenceValues.AsNoTracking().Where(x => includeInactive || x.IsActive);
        if (!string.IsNullOrWhiteSpace(term))
        {
            currencyQuery = currencyQuery.Where(x => x.Code.Contains(term) || x.NameFa.Contains(term) || (x.NameEn != null && x.NameEn.Contains(term)));
            supplierQuery = supplierQuery.Where(x => x.SupplierNumber.Contains(term) || x.Name.Contains(term));
            referenceQuery = referenceQuery.Where(x => x.Code.Contains(term) || x.NameFa.Contains(term) || (x.NameEn != null && x.NameEn.Contains(term)));
        }
        var currencies = await currencyQuery.OrderBy(x => x.Code).Take(limit)
            .Select(x => new TradingLookupItem(x.Id, x.Code, x.NameFa, x.NameEn, x.IsActive)).ToListAsync(ct);
        var suppliers = await supplierQuery.OrderBy(x => x.SupplierNumber).Take(limit)
            .Select(x => new TradingLookupItem(x.Id, x.SupplierNumber, x.Name, null, x.IsActive)).ToListAsync(ct);
        var references = await referenceQuery.OrderBy(x => x.ReferenceType).ThenBy(x => x.SortOrder).ThenBy(x => x.Code).Take(limit)
            .Select(x => new TradeReferenceResponse(x.Id, x.ReferenceType, x.Code, x.NameFa, x.NameEn, x.SortOrder, x.IsActive, x.Version)).ToListAsync(ct);
        return new(currencies, suppliers, references, ReferenceTypes);
    }

    public async Task<TradeReferenceListResponse> ListReferences(string? search, string? referenceType,
        bool includeInactive, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize); await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.TradeReferenceValues.AsNoTracking().Where(x => includeInactive || x.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim(); query = query.Where(x => x.Code.Contains(term) || x.NameFa.Contains(term) ||
                (x.NameEn != null && x.NameEn.Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(referenceType))
        {
            var type = Code(referenceType, ReferenceTypes, "referenceType"); query = query.Where(x => x.ReferenceType == type);
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.ReferenceType).ThenBy(x => x.SortOrder).ThenBy(x => x.Code)
            .Skip((page - 1) * pageSize).Take(pageSize).Select(x => new TradeReferenceResponse(x.Id, x.ReferenceType,
                x.Code, x.NameFa, x.NameEn, x.SortOrder, x.IsActive, x.Version)).ToListAsync(ct);
        return new(items, page, pageSize, total);
    }

    public async Task<TradeReferenceResponse> CreateReference(CreateTradeReferenceRequest request, CancellationToken ct)
    {
        var type = Code(request.ReferenceType, ReferenceTypes, "referenceType"); Text(request.NameFa, "nameFa", 200); Sort(request.SortOrder);
        await using var db = await factory.CreateDbContextAsync(ct);
        var reference = new TradeReferenceValue(type, await Next(db, "trade_reference_value_number_seq", type[..Math.Min(3, type.Length)], ct), request.NameFa.Trim(), Clean(request.NameEn), request.SortOrder);
        db.TradeReferenceValues.Add(reference); AddAudit(db, "TradeReferenceValue", reference.Id, "CREATE", null, ReferenceOf(reference), null); await Save(db, ct);
        return ReferenceOf(reference);
    }

    public async Task<TradeReferenceResponse> UpdateReference(Guid id, UpdateTradeReferenceRequest request, CancellationToken ct)
    {
        Text(request.NameFa, "nameFa", 200); Sort(request.SortOrder); await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.TradeReferenceValues.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException(); Version(entity.Version, request.ExpectedVersion);
        var before = Json(ReferenceOf(entity)); entity.Edit(request.NameFa.Trim(), Clean(request.NameEn), request.SortOrder); AddAudit(db, "TradeReferenceValue", id, "EDIT", before, ReferenceOf(entity), null); await Save(db, ct); return ReferenceOf(entity);
    }

    public async Task<TradeReferenceResponse> StateReference(Guid id, ChangeTradeReferenceStateRequest request, CancellationToken ct)
    {
        Reason(request.Reason); await using var db = await factory.CreateDbContextAsync(ct); var entity = await db.TradeReferenceValues.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException(); Version(entity.Version, request.ExpectedVersion);
        var before = Json(ReferenceOf(entity)); entity.ChangeState(request.IsActive); AddAudit(db, "TradeReferenceValue", id, request.IsActive ? "REACTIVATE" : "DEACTIVATE", before, ReferenceOf(entity), request.Reason); await Save(db, ct); return ReferenceOf(entity);
    }

    public ResolvedTradeProjectSetupDate ResolveSetupDate(ResolveTradeProjectSetupDateRequest request)
    {
        var normalized = string.Concat((request.JalaliDate ?? "").Select(c => c switch
        {
            >= '\u06f0' and <= '\u06f9' => (char)('0' + c - '\u06f0'),
            >= '\u0660' and <= '\u0669' => (char)('0' + c - '\u0660'),
            _ => c
        })).Trim();
        if (normalized.Length != 8 || normalized.Any(c => !char.IsAsciiDigit(c)) ||
            !int.TryParse(normalized.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year) ||
            !int.TryParse(normalized.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var month) ||
            !int.TryParse(normalized.AsSpan(6, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var day))
            throw Validation("jalaliDate", "تاریخ شمسی باید هشت رقم و با قالب YYYYMMDD باشد.");
        try { return new(normalized, calendar.GetGregorianDate(year, month, day)); }
        catch (PlanningValidationException) { throw Validation("jalaliDate", "تاریخ شمسی معتبر نیست."); }
    }

    public async Task<TradeProjectSetupLookupResponse> ProjectSetupLookups(string kind, string? search,
        int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize);
        if ((long)(page - 1) * pageSize > int.MaxValue) throw Validation("page", "شمارهٔ صفحه خارج از محدوده است.");
        var category = Code(kind, ["CUSTOMER", "BUSINESS_CARD", "ASSIGNEE"], "kind");
        if (category == "ASSIGNEE")
        {
            var result = await userDirectory.Search(search, page, pageSize, ct);
            return new(result.Items.Select(x => new TradingLookupItem(x.Id, "", x.FullName, null, true)).ToList(),
                page, pageSize, result.TotalCount);
        }
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.TradeReferenceValues.AsNoTracking().Where(x => x.IsActive && x.ReferenceType == category);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Code.Contains(term) || x.NameFa.Contains(term) || (x.NameEn != null && x.NameEn.Contains(term)));
        }
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.SortOrder).ThenBy(x => x.Code).ThenBy(x => x.Id)
            .Skip(checked((page - 1) * pageSize)).Take(pageSize)
            .Select(x => new TradingLookupItem(x.Id, x.Code, x.NameFa, x.NameEn, x.IsActive)).ToListAsync(ct);
        return new(rows, page, pageSize, count);
    }

    public async Task<TradeProjectDetailResponse> CreateProjectSetup(CreateTradeProjectSetupRequest request,
        TradingProjectReadAccess access, CancellationToken ct)
    {
        Text(request.TitleFa, "titleFa", 250);
        if (request.StartDate == default) throw Validation("startDate", "تاریخ شروع / ارجاع الزامی است.");
        if (request.ResponsibleUserId == Guid.Empty) throw Validation("responsibleUserId", "مسئول پیگیری الزامی است.");
        if (request.Notes?.Length > 4000) throw Validation("notes", "توضیحات حداکثر ۴۰۰۰ کاراکتر است.");
        var responsible = await userDirectory.Find(request.ResponsibleUserId, ct)
            ?? throw Validation("responsibleUserId", "کاربر فعال برای مسئول پیگیری یافت نشد.");
        if (string.IsNullOrWhiteSpace(responsible.FullName)) throw Validation("responsibleUserId", "پروفایل مسئول پیگیری نام معتبر ندارد.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var customer = request.CustomerId.HasValue ? await ActiveReference(db, request.CustomerId.Value, "CUSTOMER", ct) : null;
        var card = request.BusinessCardId.HasValue ? await ActiveReference(db, request.BusinessCardId.Value, "BUSINESS_CARD", ct) : null;
        // Preserve canonical DRAFT lifecycle and legacy NORMAL storage; neither is user-editable here.
        var project = new TradeProject(await Next(db, "trade_project_number_seq", "TRD", ct),
            request.TitleFa.Trim(), null, customer?.Id, customer?.Code, customer?.NameFa, customer?.NameEn,
            null, null, null, "NORMAL", null, UserId(), Clean(request.Notes));
        project.SetInitialSetup(request.StartDate, responsible.Id, responsible.FullName,
            card?.Id, card?.Code, card?.NameFa, card?.NameEn);
        db.TradeProjects.Add(project);
        AddAudit(db, "TradeProject", project.Id, "CREATE", null, new { Project = ProjectSummary().Compile()(project), project.Notes }, null);
        await Save(db, ct); // One SaveChanges: the project and its immutable audit are atomic.
        return (await GetProject(project.Id, access, ct))!;
    }

    public Task<TradeProjectDetailResponse> CreateProject(CreateTradeProjectRequest request, CancellationToken ct) =>
        CreateProject(request, TradingProjectReadAccess.Full, ct);

    public async Task<TradeProjectDetailResponse> CreateProject(CreateTradeProjectRequest request, TradingProjectReadAccess access, CancellationToken ct)
    {
        Text(request.TitleFa, "titleFa", 250); var priority = Code(request.Priority, Priorities, "priority"); await using var db = await factory.CreateDbContextAsync(ct);
        TradeReferenceValue? customer = null; if (request.CustomerId.HasValue) customer = await ActiveReference(db, request.CustomerId.Value, "CUSTOMER", ct);
        var supplier = request.SupplierId.HasValue ? await db.Suppliers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.SupplierId && x.IsActive, ct) ?? throw Validation("supplierId", "تأمین‌کننده فعال یافت نشد.") : null;
        var project = new TradeProject(await Next(db, "trade_project_number_seq", "TRD", ct), request.TitleFa.Trim(), Clean(request.TitleEn), customer?.Id, customer?.Code, customer?.NameFa, customer?.NameEn, supplier?.Id, supplier?.SupplierNumber, supplier?.Name, priority, request.TargetDate, UserId(), Clean(request.Notes));
        db.TradeProjects.Add(project); AddAudit(db, "TradeProject", project.Id, "CREATE", null, ProjectSummary().Compile()(project), null); await Save(db, ct); return (await GetProject(project.Id, access, ct))!;
    }

    public Task<TradeProjectDetailResponse> UpdateProject(Guid id, UpdateTradeProjectRequest request, CancellationToken ct) =>
        UpdateProject(id, request, TradingProjectReadAccess.Full, ct);

    public async Task<TradeProjectDetailResponse> UpdateProject(Guid id, UpdateTradeProjectRequest request, TradingProjectReadAccess access, CancellationToken ct)
    {
        Text(request.TitleFa, "titleFa", 250); var priority = Code(request.Priority, Priorities, "priority"); await using var db = await factory.CreateDbContextAsync(ct); var project = await db.TradeProjects.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException(); Version(project.Version, request.ExpectedVersion); if (project.Status is "CLOSED" or "CANCELLED") throw Conflict("PROJECT_NOT_EDITABLE", "پرونده بسته یا لغوشده قابل ویرایش نیست.");
        var summary = ProjectSummary().Compile(); var before = Json(summary(project)); project.Edit(request.TitleFa.Trim(), Clean(request.TitleEn), priority, request.TargetDate, Clean(request.Notes)); AddAudit(db, "TradeProject", id, "EDIT", before, summary(project), null); await Save(db, ct); return (await GetProject(id, access, ct))!;
    }

    public Task<TradeProjectDetailResponse> ChangeProjectStatus(Guid id, ChangeTradeProjectStatusRequest request, CancellationToken ct) =>
        ChangeProjectStatus(id, request, TradingProjectReadAccess.Full, ct);

    public async Task<TradeProjectDetailResponse> ChangeProjectStatus(Guid id, ChangeTradeProjectStatusRequest request, TradingProjectReadAccess access, CancellationToken ct)
    {
        var status = Code(request.Status, TradeProjectStatuses.All, "status"); Reason(request.Reason); await using var db = await factory.CreateDbContextAsync(ct); var project = await db.TradeProjects.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException(); Version(project.Version, request.ExpectedVersion);
        EnsureTransition(project.Status, status, ProjectTransitions, TradeProjectStatuses.All, request.IsCorrection, "PROJECT_STATUS_TRANSITION_INVALID");
        var summary = ProjectSummary().Compile(); var before = Json(summary(project)); project.ChangeStatus(status); AddAudit(db, "TradeProject", id, request.IsCorrection ? "STATUS_CORRECTION" : "STATUS_CHANGE", before, summary(project), request.Reason); await Save(db, ct); return (await GetProject(id, access, ct))!;
    }

    public Task<TradeProjectDetailResponse> ReopenProject(Guid id, ReopenTradeProjectRequest request, CancellationToken ct) =>
        ReopenProject(id, request, TradingProjectReadAccess.Full, ct);

    public async Task<TradeProjectDetailResponse> ReopenProject(Guid id, ReopenTradeProjectRequest request, TradingProjectReadAccess access, CancellationToken ct)
    {
        Reason(request.Reason); await using var db = await factory.CreateDbContextAsync(ct);
        var project = await db.TradeProjects.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException();
        Version(project.Version, request.ExpectedVersion);
        if (project.Status != "CLOSED") throw Conflict("PROJECT_REOPEN_INVALID", "فقط پرونده بسته‌شده قابل بازگشایی است.");
        var summary = ProjectSummary().Compile(); var before = Json(summary(project)); project.ChangeStatus("DELIVERED");
        AddAudit(db, "TradeProject", id, "REOPEN", before, summary(project), request.Reason); await Save(db, ct);
        return (await GetProject(id, access, ct))!;
    }

    public async Task<TradeOrderRegistrationResponse> CreateOrder(CreateOrderRegistrationRequest request, CancellationToken ct)
    {
        Text(request.RegistrationNumber, "registrationNumber", 80); var status = Code(request.Status, OrderStatuses, "status"); if (status != "DRAFT") throw Validation("status", "ثبت سفارش جدید باید از وضعیت پیش‌نویس آغاز شود."); Positive(request.RegisteredAmount, "registeredAmount"); await using var db = await factory.CreateDbContextAsync(ct); await OpenProject(db, request.ProjectId, ct); var currency = await Currency(db, request.CurrencyId, ct);
        var entity = new TradeOrderRegistration(request.ProjectId, request.RegistrationNumber.Trim(), status, request.RegistrationDate, request.RegisteredAmount, currency.Id, currency.Code, currency.NameFa, currency.NameEn, Clean(request.Notes)); db.TradeOrderRegistrations.Add(entity); AddAudit(db, "TradeProject", request.ProjectId, "ORDER_REGISTRATION_CREATE", null, OrderOf(entity), null); await Save(db, ct); return OrderOf(entity);
    }

    public async Task<TradeOrderRegistrationResponse> ChangeOrderStatus(Guid id, ChangeOrderRegistrationStatusRequest request, CancellationToken ct)
    {
        var status = Code(request.Status, OrderStatuses, "status"); Reason(request.Reason); await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.TradeOrderRegistrations.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException();
        await OpenProject(db, entity.ProjectId, ct); Version(entity.Version, request.ExpectedVersion);
        EnsureTransition(entity.Status, status, OrderTransitions, OrderStatuses, request.IsCorrection, "ORDER_STATUS_TRANSITION_INVALID");
        var before = Json(OrderOf(entity)); entity.ChangeStatus(status); AddAudit(db, "TradeProject", entity.ProjectId,
            request.IsCorrection ? "ORDER_STATUS_CORRECTION" : "ORDER_STATUS_CHANGE", before, OrderOf(entity), request.Reason);
        await Save(db, ct); return OrderOf(entity);
    }

    public async Task<TradeFxAllocationResponse> CreateFx(CreateFxAllocationRequest request, CancellationToken ct)
    {
        var process = Code(request.ProcessType, FxProcesses, "processType"); var deal = Code(request.DealStatus, FxDeals, "dealStatus"); var receipt = Code(request.ReceiptStatus, FxReceipts, "receiptStatus"); if (process != "REQUEST" || deal != "NOT_STARTED" || receipt != "NOT_RECEIVED") throw Validation("status", "فرایند ارزی جدید باید از وضعیت اولیه آغاز شود."); Positive(request.Amount, "amount"); if (request.AllocationDate < request.RequestDate) throw Validation("allocationDate", "تاریخ تخصیص نمی‌تواند پیش از درخواست باشد.");
        await using var db = await factory.CreateDbContextAsync(ct); await OpenProject(db, request.ProjectId, ct); if (request.OrderRegistrationId.HasValue && !await db.TradeOrderRegistrations.AnyAsync(x => x.Id == request.OrderRegistrationId && x.ProjectId == request.ProjectId, ct)) throw Validation("orderRegistrationId", "ثبت سفارش متعلق به این پرونده نیست."); var currency = await Currency(db, request.CurrencyId, ct); TradeReferenceValue? bank = request.BankId.HasValue ? await ActiveReference(db, request.BankId.Value, "BANK", ct) : null;
        var entity = new TradeFxAllocation(request.ProjectId, request.OrderRegistrationId, process, deal, receipt, request.Amount, currency.Id, currency.Code, currency.NameFa, currency.NameEn, bank?.Id, bank?.Code, bank?.NameFa, bank?.NameEn, request.RequestDate, request.AllocationDate, Clean(request.ReferenceNumber), Clean(request.Notes)); db.TradeFxAllocations.Add(entity); AddAudit(db, "TradeProject", request.ProjectId, "FX_CREATE", null, FxOf(entity), null); await Save(db, ct); return FxOf(entity);
    }

    public async Task<TradeFxAllocationResponse> ChangeFxStatus(Guid id, ChangeFxStatusRequest request, CancellationToken ct)
    {
        var process = Code(request.ProcessType, FxProcesses, "processType"); var deal = Code(request.DealStatus, FxDeals, "dealStatus"); var receipt = Code(request.ReceiptStatus, FxReceipts, "receiptStatus"); Reason(request.Reason);
        await using var db = await factory.CreateDbContextAsync(ct); var entity = await db.TradeFxAllocations.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException();
        await OpenProject(db, entity.ProjectId, ct); Version(entity.Version, request.ExpectedVersion);
        EnsureFxTransition(entity, process, deal, receipt, request.IsCorrection);
        if (request.AllocationDate < entity.RequestDate) throw Validation("allocationDate", "تاریخ تخصیص نمی‌تواند پیش از درخواست باشد.");
        var before = Json(FxOf(entity)); entity.ChangeStatus(process, deal, receipt, request.AllocationDate, Clean(request.Notes));
        AddAudit(db, "TradeProject", entity.ProjectId, request.IsCorrection ? "FX_STATUS_CORRECTION" : "FX_STATUS_CHANGE", before, FxOf(entity), request.Reason);
        await Save(db, ct); return FxOf(entity);
    }

    public async Task<TradeProjectShipmentResponse> CreateShipment(CreateTradeShipmentRequest request, CancellationToken ct)
    {
        var status = Code(request.Status, ShipmentStatuses, "status"); if (status != "DRAFT") throw Validation("status", "محموله جدید باید از وضعیت پیش‌نویس آغاز شود."); var transportMode = Code(request.TransportMode, TransportModes, "transportMode"); var method = Code(request.AllocationMethod, AllocationMethods, "allocationMethod"); Text(request.Origin, "origin", 200); Text(request.Destination, "destination", 200); NonNegative(request.GrossWeight, "grossWeight"); NonNegative(request.VolumeCbm, "volumeCbm"); NonNegative(request.AllocationValue, "allocationValue"); if (request.EstimatedArrival < request.EstimatedDeparture) throw Validation("estimatedArrival", "تاریخ رسیدن نمی‌تواند پیش از حرکت باشد.");
        await using var db = await factory.CreateDbContextAsync(ct); await using var tx = await db.Database.BeginTransactionAsync(ct); await OpenProject(db, request.ProjectId, ct); TradeReferenceValue? forwarder = request.ForwarderId.HasValue ? await ActiveReference(db, request.ForwarderId.Value, "FORWARDER", ct) : null;
        var shipment = new TradeShipment(await Next(db, "trade_shipment_number_seq", "SHP", ct), status, transportMode, forwarder?.Id, forwarder?.Code, forwarder?.NameFa, forwarder?.NameEn, request.Origin.Trim(), request.Destination.Trim(), request.EstimatedDeparture, request.EstimatedArrival, request.GrossWeight, request.VolumeCbm, Clean(request.Notes));
        var link = new TradeProjectShipment(request.ProjectId, shipment.Id, method, request.AllocationValue, Clean(request.Notes)); db.TradeShipments.Add(shipment); db.TradeProjectShipments.Add(link); AddAudit(db, "TradeProject", request.ProjectId, "SHIPMENT_CREATE_AND_LINK", null, ShipmentOf(shipment), null); await Save(db, ct); await tx.CommitAsync(ct); return new(link.Id, link.AllocationMethod, link.AllocationValue, link.Notes, link.Version, ShipmentOf(shipment), []);
    }

    public async Task<TradeShipmentResponse> UpdateShipmentActuals(Guid id, UpdateTradeShipmentActualsRequest request, CancellationToken ct)
    {
        var status = Code(request.Status, ShipmentStatuses, "status"); Reason(request.Reason); if (request.ActualArrival < request.ActualDeparture) throw Validation("actualArrival", "تاریخ رسیدن واقعی نمی‌تواند پیش از حرکت باشد."); await using var db = await factory.CreateDbContextAsync(ct); var shipment = await db.TradeShipments.Include(x => x.Legs).Include(x => x.ProjectLinks).ThenInclude(x => x.Project).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException(); Version(shipment.Version, request.ExpectedVersion); EnsureTransition(shipment.Status, status, ShipmentTransitions, ShipmentStatuses, request.IsCorrection, "SHIPMENT_STATUS_TRANSITION_INVALID"); if (shipment.ProjectLinks.Any(x => x.Project.Status is "CLOSED" or "CANCELLED")) throw Conflict("PROJECT_NOT_OPERATIONAL", "یکی از پرونده‌های مرتبط بسته یا لغوشده است."); var before = Json(ShipmentOf(shipment)); shipment.UpdateActuals(status, request.ActualDeparture, request.ActualArrival, Clean(request.Notes)); AddAudit(db, "TradeShipment", id, request.IsCorrection ? "SHIPMENT_STATUS_CORRECTION" : "ACTUALS_UPDATE", before, ShipmentOf(shipment), request.Reason); await Save(db, ct); return ShipmentOf(shipment);
    }

    public async Task<TradeProjectShipmentResponse> LinkShipment(Guid shipmentId, LinkExistingShipmentRequest request, CancellationToken ct)
    {
        var method = Code(request.AllocationMethod, AllocationMethods, "allocationMethod"); NonNegative(request.AllocationValue, "allocationValue");
        await using var db = await factory.CreateDbContextAsync(ct); await using var tx = await db.Database.BeginTransactionAsync(ct);
        await OpenProject(db, request.ProjectId, ct); var shipment = await OperationalShipment(db, shipmentId, ct);
        if (await db.TradeProjectShipments.AnyAsync(x => x.ProjectId == request.ProjectId && x.ShipmentId == shipmentId, ct))
            throw Conflict("SHIPMENT_ALREADY_LINKED", "این محموله قبلاً به پرونده متصل شده است.");
        var link = new TradeProjectShipment(request.ProjectId, shipmentId, method, request.AllocationValue, Clean(request.Notes));
        db.TradeProjectShipments.Add(link); AddAudit(db, "TradeProject", request.ProjectId, "SHIPMENT_LINK", null,
            new { ShipmentId = shipmentId, link.AllocationMethod, link.AllocationValue }, null);
        await Save(db, ct); await tx.CommitAsync(ct);
        return new(link.Id, link.AllocationMethod, link.AllocationValue, link.Notes, link.Version, ShipmentOf(shipment), []);
    }

    public async Task<TradeProjectShipmentResponse> UpdateShipmentAllocation(Guid linkId, UpdateShipmentAllocationRequest request, CancellationToken ct)
    {
        var method = Code(request.AllocationMethod, AllocationMethods, "allocationMethod"); NonNegative(request.AllocationValue, "allocationValue"); Reason(request.Reason);
        await using var db = await factory.CreateDbContextAsync(ct); var link = await db.TradeProjectShipments.Include(x => x.Project)
            .Include(x => x.Shipment).ThenInclude(x => x.Legs).SingleOrDefaultAsync(x => x.Id == linkId, ct) ?? throw new KeyNotFoundException();
        await OpenProject(db, link.ProjectId, ct); Version(link.Version, request.ExpectedVersion);
        var before = Json(new { link.AllocationMethod, link.AllocationValue, link.Notes }); link.Edit(method, request.AllocationValue, Clean(request.Notes));
        AddAudit(db, "TradeProject", link.ProjectId, "SHIPMENT_ALLOCATION_CHANGE", before,
            new { link.AllocationMethod, link.AllocationValue, link.Notes }, request.Reason); await Save(db, ct);
        var customs = await db.TradeCustomsClearances.AsNoTracking().Where(x => x.ShipmentId == link.ShipmentId).ToListAsync(ct);
        return new(link.Id, link.AllocationMethod, link.AllocationValue, link.Notes, link.Version, ShipmentOf(link.Shipment), customs.Select(CustomsOf).ToList());
    }

    public async Task<TradeShipmentLegResponse> CreateLeg(Guid shipmentId, CreateTradeShipmentLegRequest request, CancellationToken ct)
    {
        if (request.Sequence < 1) throw Validation("sequence", "ترتیب مسیر باید بزرگ‌تر از صفر باشد."); Text(request.FromLocation, "fromLocation", 200); Text(request.ToLocation, "toLocation", 200); var transportMode = Code(request.TransportMode, TransportModes, "transportMode"); var status = Code(request.Status, LegStatuses, "status"); if (status != "PLANNED") throw Validation("status", "مسیر جدید باید از وضعیت برنامه‌ریزی‌شده آغاز شود."); if (request.EstimatedArrival < request.EstimatedDeparture) throw Validation("estimatedArrival", "تاریخ رسیدن نمی‌تواند پیش از حرکت باشد."); await using var db = await factory.CreateDbContextAsync(ct); await OperationalShipment(db, shipmentId, ct);
        var leg = new TradeShipmentLeg(shipmentId, request.Sequence, request.FromLocation.Trim(), request.ToLocation.Trim(), transportMode, status, request.EstimatedDeparture, request.EstimatedArrival, Clean(request.TrackingNumber), Clean(request.Notes)); db.TradeShipmentLegs.Add(leg); AddAudit(db, "TradeShipment", shipmentId, "LEG_CREATE", null, LegOf(leg), null); await Save(db, ct); return LegOf(leg);
    }

    public async Task<TradeShipmentLegResponse> ChangeLegStatus(Guid id, ChangeShipmentLegStatusRequest request, CancellationToken ct)
    {
        var status = Code(request.Status, LegStatuses, "status"); Reason(request.Reason); if (request.ActualArrival < request.ActualDeparture) throw Validation("actualArrival", "تاریخ رسیدن واقعی نمی‌تواند پیش از حرکت باشد.");
        await using var db = await factory.CreateDbContextAsync(ct); var leg = await db.TradeShipmentLegs.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException();
        await OperationalShipment(db, leg.ShipmentId, ct); Version(leg.Version, request.ExpectedVersion);
        EnsureTransition(leg.Status, status, LegTransitions, LegStatuses, request.IsCorrection, "LEG_STATUS_TRANSITION_INVALID");
        var before = Json(LegOf(leg)); leg.ChangeStatus(status, request.ActualDeparture, request.ActualArrival, Clean(request.Notes));
        AddAudit(db, "TradeShipment", leg.ShipmentId, request.IsCorrection ? "LEG_STATUS_CORRECTION" : "LEG_STATUS_CHANGE", before, LegOf(leg), request.Reason);
        await Save(db, ct); return LegOf(leg);
    }

    public async Task<TradeCustomsResponse> CreateCustoms(CreateTradeCustomsRequest request, CancellationToken ct)
    {
        Text(request.ClearanceNumber, "clearanceNumber", 80); var clearanceType = Code(request.ClearanceType, ClearanceTypes, "clearanceType"); var status = Code(request.Status, CustomsStatuses, "status"); if (status != "NOT_STARTED") throw Validation("status", "پرونده گمرکی جدید باید از وضعیت شروع‌نشده آغاز شود."); if (request.ReleaseDate < request.DeclarationDate) throw Validation("releaseDate", "تاریخ ترخیص نمی‌تواند پیش از اظهار باشد."); await using var db = await factory.CreateDbContextAsync(ct); await OperationalShipment(db, request.ShipmentId, ct); TradeReferenceValue? office = request.CustomsOfficeId.HasValue ? await ActiveReference(db, request.CustomsOfficeId.Value, "CUSTOMS_OFFICE", ct) : null;
        var entity = new TradeCustomsClearance(request.ShipmentId, request.ClearanceNumber.Trim(), clearanceType, status, office?.Id, office?.Code, office?.NameFa, office?.NameEn, request.DeclarationDate, request.ReleaseDate, Clean(request.Notes)); db.TradeCustomsClearances.Add(entity); AddAudit(db, "TradeShipment", request.ShipmentId, "CUSTOMS_CREATE", null, CustomsOf(entity), null); await Save(db, ct); return CustomsOf(entity);
    }

    public async Task<TradeCustomsResponse> ChangeCustomsStatus(Guid id, ChangeCustomsStatusRequest request, CancellationToken ct)
    {
        var status = Code(request.Status, CustomsStatuses, "status"); Reason(request.Reason); await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.TradeCustomsClearances.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException();
        await OperationalShipment(db, entity.ShipmentId, ct); Version(entity.Version, request.ExpectedVersion);
        EnsureTransition(entity.Status, status, CustomsTransitions, CustomsStatuses, request.IsCorrection, "CUSTOMS_STATUS_TRANSITION_INVALID");
        if (request.ReleaseDate < entity.DeclarationDate) throw Validation("releaseDate", "تاریخ ترخیص نمی‌تواند پیش از اظهار باشد.");
        var before = Json(CustomsOf(entity)); entity.ChangeStatus(status, request.ReleaseDate, Clean(request.Notes));
        AddAudit(db, "TradeShipment", entity.ShipmentId, request.IsCorrection ? "CUSTOMS_STATUS_CORRECTION" : "CUSTOMS_STATUS_CHANGE", before, CustomsOf(entity), request.Reason);
        await Save(db, ct); return CustomsOf(entity);
    }

    public async Task<TradeCostResponse> CreateCost(CreateTradeCostRequest request, CancellationToken ct)
    {
        var scope = Code(request.ScopeType, CostScopes, "scopeType"); Text(request.CostType, "costType", 50); Positive(request.Amount, "amount"); await using var db = await factory.CreateDbContextAsync(ct); await OpenProject(db, request.ProjectId, ct); await ValidateScope(db, request.ProjectId, scope, request.ScopeId, ct); var currency = await Currency(db, request.CurrencyId, ct);
        var entity = new TradeCost(request.ProjectId, scope, request.ScopeId, request.CostType.Trim().ToUpperInvariant(), request.Amount, currency.Id, currency.Code, currency.NameFa, currency.NameEn, request.EffectiveDate, Clean(request.ReferenceNumber), Clean(request.Notes)); db.TradeCosts.Add(entity); AddAudit(db, "TradeProject", request.ProjectId, "COST_CREATE", null, CostOf(entity), null); await Save(db, ct); return CostOf(entity);
    }

    public async Task<TradeCostResponse> VoidCost(Guid id, VoidTradeCostRequest request, CancellationToken ct)
    {
        Reason(request.Reason); await using var db = await factory.CreateDbContextAsync(ct); var entity = await db.TradeCosts.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException(); if (entity.IsVoided) throw Conflict("COST_ALREADY_VOIDED", "هزینه قبلاً ابطال شده است."); var before = Json(CostOf(entity)); entity.Void(UserId(), request.Reason.Trim()); AddAudit(db, "TradeProject", entity.ProjectId, "COST_VOID", before, CostOf(entity), request.Reason); await Save(db, ct); return CostOf(entity);
    }

    public async Task AddReportNote(CreateTradeReportNoteRequest request, CancellationToken ct)
    {
        Text(request.Note, "note", 4000); await using var db = await factory.CreateDbContextAsync(ct); await Project(db, request.ProjectId, ct); var entity = new TradeReportNote(request.ProjectId, UserId(), request.Note.Trim()); db.TradeReportNotes.Add(entity); AddAudit(db, "TradeProject", request.ProjectId, "REPORT_NOTE_CREATE", null, new { entity.Id, entity.ProjectId, entity.AuthorUserId, entity.Note }, null); await Save(db, ct);
    }

    private static System.Linq.Expressions.Expression<Func<TradeProject, TradeProjectSummary>> ProjectSummary() => x => new(x.Id, x.ProjectNumber, x.TitleFa, x.TitleEn, x.CustomerNameFaSnapshot, x.CustomerNameEnSnapshot, x.SupplierNameSnapshot, x.Priority, x.Status, x.TargetDate, x.Version, x.UpdatedAtUtc, x.StartDate, x.BusinessCardId, x.BusinessCardCodeSnapshot, x.BusinessCardNameFaSnapshot, x.BusinessCardNameEnSnapshot, x.ResponsibleUserId, x.ResponsibleUserNameSnapshot);
    private static TradeReferenceResponse ReferenceOf(TradeReferenceValue x) => new(x.Id, x.ReferenceType, x.Code, x.NameFa, x.NameEn, x.SortOrder, x.IsActive, x.Version);
    private static TradeProjectContext ProjectContextOf(TradeProject x) => new(x.Id, x.ProjectNumber, x.TitleFa, x.TitleEn);
    private static TradeOrderRegistrationResponse OrderOf(TradeOrderRegistration x) => new(x.Id, x.ProjectId, x.RegistrationNumber, x.Status, x.RegistrationDate, x.RegisteredAmount, x.CurrencyId, x.CurrencyCodeSnapshot, x.CurrencyNameFaSnapshot, x.CurrencyNameEnSnapshot, x.Notes, x.Version);
    private static TradeFxAllocationResponse FxOf(TradeFxAllocation x) => new(x.Id, x.ProjectId, x.OrderRegistrationId, x.ProcessType, x.DealStatus, x.ReceiptStatus, x.Amount, x.CurrencyId, x.CurrencyCodeSnapshot, x.CurrencyNameFaSnapshot, x.CurrencyNameEnSnapshot, x.BankNameFaSnapshot, x.BankNameEnSnapshot, x.RequestDate, x.AllocationDate, x.ReferenceNumber, x.Notes, x.Version);
    private static TradeShipmentResponse ShipmentOf(TradeShipment x) => new(x.Id, x.ShipmentNumber, x.Status, x.TransportMode, x.ForwarderNameFaSnapshot, x.ForwarderNameEnSnapshot, x.Origin, x.Destination, x.EstimatedDeparture, x.EstimatedArrival, x.ActualDeparture, x.ActualArrival, x.GrossWeight, x.VolumeCbm, x.Notes, x.Version, x.Legs.OrderBy(v => v.Sequence).Select(LegOf).ToList());
    private static TradeShipmentLegResponse LegOf(TradeShipmentLeg x) => new(x.Id, x.Sequence, x.FromLocation, x.ToLocation, x.TransportMode, x.Status, x.EstimatedDeparture, x.EstimatedArrival, x.ActualDeparture, x.ActualArrival, x.TrackingNumber, x.Notes, x.Version);
    private static TradeCustomsResponse CustomsOf(TradeCustomsClearance x) => new(x.Id, x.ShipmentId, x.ClearanceNumber, x.ClearanceType, x.Status, x.CustomsOfficeNameFaSnapshot, x.CustomsOfficeNameEnSnapshot, x.DeclarationDate, x.ReleaseDate, x.Notes, x.Version);
    private static TradeCostResponse CostOf(TradeCost x) => new(x.Id, x.ProjectId, x.ScopeType, x.ScopeId, x.CostType, x.Amount, x.CurrencyId, x.CurrencyCodeSnapshot, x.CurrencyNameFaSnapshot, x.CurrencyNameEnSnapshot, x.EffectiveDate, x.ReferenceNumber, x.Notes, x.IsVoided, x.CreatedAtUtc);
    private static TradeAuditResponse AuditOf(TradeAuditEvent x) => new(x.Id, x.ActorUserId, x.EntityType, x.EntityId, x.Action, x.Reason, x.OccurredAtUtc);

    private Guid UserId()
    {
        var user = accessor.HttpContext?.User;
        if (Guid.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return id;
        if (user?.HasClaim(SecurityClaimTypes.TestAuthentication, "true") == true)
            return Guid.Parse("00000000-0000-0000-0000-000000000001");
        throw new UnauthorizedAccessException();
    }
    private void AddAudit(HadronDbContext db, string type, Guid id, string action, string? before, object? after, string? reason) => db.TradeAuditEvents.Add(new TradeAuditEvent(UserId(), type, id, action, before, after is null ? null : Json(after), Clean(reason)));
    private static string Json(object value) => JsonSerializer.Serialize(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static async Task Save(HadronDbContext db, CancellationToken ct) { try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { throw Conflict("STALE_VERSION", "اطلاعات توسط کاربر دیگری تغییر کرده است؛ صفحه را تازه کنید."); } catch (DbUpdateException ex) when (ex.InnerException is PostgresException p && p.SqlState == PostgresErrorCodes.UniqueViolation) { throw Conflict("DUPLICATE_VALUE", "رکورد تکراری است."); } }
    private static async Task<string> Next(HadronDbContext db, string sequence, string prefix, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(ct);
        var transaction = db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;
        await using var command = new NpgsqlCommand($"SELECT nextval('trading.{sequence}')", connection, transaction);
        var value = (long)(await command.ExecuteScalarAsync(ct))!;
        return $"{prefix}-{value:D6}";
    }
    private static async Task<TradeProject> Project(HadronDbContext db, Guid id, CancellationToken ct) => await db.TradeProjects.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException();
    private static async Task<TradeProject> OpenProject(HadronDbContext db, Guid id, CancellationToken ct)
    {
        var project = await Project(db, id, ct);
        if (project.Status is "CLOSED" or "CANCELLED") throw Conflict("PROJECT_NOT_OPERATIONAL", "پرونده بسته یا لغوشده عملیات جدید نمی‌پذیرد.");
        return project;
    }
    private static async Task<TradeShipment> OperationalShipment(HadronDbContext db, Guid id, CancellationToken ct)
    {
        var shipment = await db.TradeShipments.Include(x => x.Legs).Include(x => x.ProjectLinks).ThenInclude(x => x.Project)
            .SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException();
        if (shipment.ProjectLinks.Any(x => x.Project.Status is "CLOSED" or "CANCELLED"))
            throw Conflict("PROJECT_NOT_OPERATIONAL", "یکی از پرونده‌های مرتبط بسته یا لغوشده است.");
        return shipment;
    }
    private static async Task<TradeReferenceValue> ActiveReference(HadronDbContext db, Guid id, string type, CancellationToken ct) => await db.TradeReferenceValues.SingleOrDefaultAsync(x => x.Id == id && x.ReferenceType == type && x.IsActive, ct) ?? throw Validation(type.ToLowerInvariant(), $"مرجع فعال {type} یافت نشد.");
    private static async Task<Modules.ReferenceData.Domain.Currency> Currency(HadronDbContext db, Guid id, CancellationToken ct) => await db.Currencies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.IsActive, ct) ?? throw Validation("currencyId", "ارز فعال یافت نشد.");
    private static async Task ValidateScope(HadronDbContext db, Guid projectId, string scope, Guid id, CancellationToken ct)
    {
        var valid = scope switch { "PROJECT" => id == projectId, "SHIPMENT" => await db.TradeProjectShipments.AnyAsync(x => x.ProjectId == projectId && x.ShipmentId == id, ct), "SHIPMENT_LEG" => await db.TradeProjectShipments.AnyAsync(x => x.ProjectId == projectId && x.Shipment.Legs.Any(l => l.Id == id), ct), "CUSTOMS" => await db.TradeProjectShipments.AnyAsync(x => x.ProjectId == projectId && x.Shipment.CustomsClearances.Any(c => c.Id == id), ct), _ => false };
        if (!valid) throw Validation("scopeId", "دامنه هزینه متعلق به این پرونده نیست.");
    }
    private static void Page(int page, int size) { if (page < 1 || size is < 1 or > 100) throw Validation("page", "صفحه‌بندی نامعتبر است."); }
    private static bool Descending(string? direction) => string.Equals(direction?.Trim(), "DESC", StringComparison.OrdinalIgnoreCase);
    private static void Text(string? value, string field, int max) { if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > max) throw Validation(field, "مقدار الزامی یا طول آن نامعتبر است."); }
    private static string Code(string value, IReadOnlyCollection<string> allowed, string field) { var code = value.Trim().ToUpperInvariant(); if (!allowed.Contains(code)) throw Validation(field, "کد انتخاب‌شده معتبر نیست."); return code; }
    private static void Positive(decimal value, string field) { if (value <= 0) throw Validation(field, "مقدار باید بزرگ‌تر از صفر باشد."); }
    private static void NonNegative(decimal? value, string field) { if (value < 0) throw Validation(field, "مقدار نمی‌تواند منفی باشد."); }
    private static void NonNegative(decimal value, string field) { if (value < 0) throw Validation(field, "مقدار نمی‌تواند منفی باشد."); }
    private static void Sort(int value) { if (value is < 0 or > 100000) throw Validation("sortOrder", "ترتیب نمایش نامعتبر است."); }
    private static void Version(long actual, long expected) { if (expected < 1 || actual != expected) throw Conflict("STALE_VERSION", "اطلاعات توسط کاربر دیگری تغییر کرده است؛ صفحه را تازه کنید."); }
    private static void Reason(string? reason) { if (string.IsNullOrWhiteSpace(reason)) throw Validation("reason", "دلیل تغییر الزامی است."); }
    private static IReadOnlySet<(string From, string To)> Pairs(params (string From, string To)[] values) => values.ToHashSet();
    private static void EnsureTransition(string current, string next, IReadOnlySet<(string From, string To)> allowed,
        IReadOnlyList<string> orderedStatuses, bool correction, string code)
    {
        if (current == next) throw Conflict(code, "وضعیت جدید با وضعیت فعلی یکسان است.");
        if (current is "CANCELLED" or "CLOSED") throw Conflict(code, "وضعیت نهایی قابل تغییر نیست.");
        if (allowed.Contains((current, next))) return;
        var currentIndex = Position(orderedStatuses, current); var nextIndex = Position(orderedStatuses, next);
        if (correction && currentIndex >= 0 && nextIndex >= 0 && nextIndex < currentIndex && next is not "CANCELLED" and not "CLOSED") return;
        throw Conflict(code, "انتقال وضعیت مطابق چرخه‌عمر مصوب نیست.");
    }
    private static void EnsureFxTransition(TradeFxAllocation entity, string process, string deal, string receipt, bool correction)
    {
        if (process == "CORRECTION" && !correction) throw Conflict("FX_STATUS_TRANSITION_INVALID", "مرحله اصلاح فقط با علامت اصلاح و دلیل قابل ثبت است.");
        EnsureLinear(entity.ProcessType, process, FxProcesses, correction, "FX_PROCESS_TRANSITION_INVALID");
        EnsureLinear(entity.DealStatus, deal, FxDeals, correction, "FX_DEAL_TRANSITION_INVALID");
        EnsureLinear(entity.ReceiptStatus, receipt, FxReceipts, correction, "FX_RECEIPT_TRANSITION_INVALID");
    }
    private static void EnsureLinear(string current, string next, IReadOnlyList<string> sequence, bool correction, string code)
    {
        if (current == next) return;
        if (current == "CANCELLED") throw Conflict(code, "وضعیت لغوشده قابل تغییر نیست.");
        if (next == "CANCELLED") return;
        if (next == "CORRECTION" && correction) return;
        var currentIndex = Position(sequence, current); var nextIndex = Position(sequence, next);
        if (currentIndex >= 0 && nextIndex == currentIndex + 1) return;
        if (correction && currentIndex >= 0 && nextIndex >= 0 && nextIndex < currentIndex) return;
        throw Conflict(code, "انتقال وضعیت مطابق چرخه‌عمر مصوب نیست.");
    }
    private static int Position(IReadOnlyList<string> values, string value)
    {
        for (var index = 0; index < values.Count; index++)
            if (string.Equals(values[index], value, StringComparison.Ordinal)) return index;
        return -1;
    }
    private static TradingValidationException Validation(string field, string message) => new(new Dictionary<string, string[]> { [field] = [message] });
    private static TradingConflictException Conflict(string code, string message) => new(code, message);
}
