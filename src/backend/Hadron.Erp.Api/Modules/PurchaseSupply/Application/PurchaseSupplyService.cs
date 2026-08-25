using System.Data;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.PurchaseSupply.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Hadron.Erp.Api.Modules.Sourcing.Domain;
using Hadron.Erp.Api.Modules.Suppliers.Domain;
using Hadron.Erp.Api.Platform.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Hadron.Erp.Api.Modules.PurchaseSupply.Application;

public sealed class PurchaseSupplyService(
    IDbContextFactory<HadronDbContext> factory,
    IAuditEventSink auditSink,
    IHttpContextAccessor accessor,
    ILogger<PurchaseSupplyService> logger)
{
    public async Task<PurchaseOrderListResponse> List(DateOnly evaluationDate, string? search, string? status, Guid? supplierId,
        Guid? currencyId, int page, int pageSize, CancellationToken ct)
    {
        ValidatePaging(page, pageSize);
        status = string.IsNullOrWhiteSpace(status) ? null : status.Trim();
        if (status is not null && !PurchaseOrderStatuses.All.Contains(status))
            throw Validation("status", "وضعیت سفارش خرید معتبر نیست.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.PurchaseOrders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.PurchaseOrderNumber.Contains(term) || x.Supplier.SupplierNumber.Contains(term) ||
                x.Supplier.Name.Contains(term) || x.Lines.Any(l => l.Item.BusinessNumber.Contains(term) || l.Item.NameFa.Contains(term) ||
                    (l.Item.NameEn != null && l.Item.NameEn.Contains(term))));
        }
        if (status is not null) query = query.Where(x => x.Status == status);
        if (supplierId.HasValue) query = query.Where(x => x.SupplierId == supplierId);
        if (currencyId.HasValue) query = query.Where(x => x.CurrencyId == currencyId);

        var totalCount = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.PurchaseOrderNumber,
                Supplier = new { x.Supplier.Id, x.Supplier.SupplierNumber, x.Supplier.Name, x.Supplier.IsActive },
                Currency = new { x.Currency.Id, x.Currency.Code, x.Currency.NameFa, x.Currency.NameEn, x.Currency.IsActive },
                x.Status,
                x.Version,
                x.CreatedAtUtc,
                LineCount = x.Lines.Count,
                Total = x.Lines.Sum(l => l.OrderedQuantity * l.UnitPrice),
                Open = x.Lines.SelectMany(l => l.Schedules).Sum(s => s.AllocatedQuantity -
                    (s.ReceiptLines.Where(r => r.Receipt.Status == PurchaseReceiptStatuses.Posted)
                        .Sum(r => (decimal?)r.Quantity) ?? 0) -
                    (s.Cancellations.Sum(c => (decimal?)c.Quantity) ?? 0)),
                Etas = x.Lines.SelectMany(l => l.Schedules).Select(s => new
                {
                    Revised = s.PlanRevisions.Where(p => p.PlanType == PurchasePlanTypes.Revised)
                        .OrderByDescending(p => p.RevisionNumber).Select(p => p.WarehouseReceiptDate).FirstOrDefault(),
                    Original = s.PlanRevisions.Where(p => p.PlanType == PurchasePlanTypes.Original)
                        .Select(p => p.WarehouseReceiptDate).FirstOrDefault()
                }).ToList()
            }).AsSplitQuery().ToListAsync(ct);

        var items = rows.Select(row =>
        {
            var firmEtas = row.Etas.Select(x => x.Revised ?? x.Original).ToList();
            return new PurchaseOrderListItem(row.Id, row.PurchaseOrderNumber,
                new(row.Supplier.Id, row.Supplier.SupplierNumber, row.Supplier.Name, row.Supplier.IsActive),
                new(row.Currency.Id, row.Currency.Code, row.Currency.NameFa, row.Currency.NameEn, row.Currency.IsActive), row.Status, row.LineCount,
                row.Total, row.Open, firmEtas.Where(x => x.HasValue).Min(),
                PurchaseOrderStatuses.IsFirm(row.Status) && firmEtas.Any(x => !x.HasValue),
                PurchaseOrderStatuses.IsFirm(row.Status) && firmEtas.Any(x => x.HasValue && x.Value < evaluationDate),
                row.Version, row.CreatedAtUtc);
        }).ToList();
        return new(items, totalCount, page, pageSize);
    }

    public async Task<PurchaseOrderResponse?> Get(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var order = await FullQuery(db.PurchaseOrders.AsNoTracking()).AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        return order is null ? null : ToResponse(order);
    }

    public async Task<PurchaseOrderResponse> Create(CreatePurchaseOrderRequest request, CancellationToken ct)
    {
        ValidateCreate(request);
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var supplier = await db.Suppliers.Include(x => x.PaymentProfile).ThenInclude(x => x.PaymentTerm)
            .SingleOrDefaultAsync(x => x.Id == request.SupplierId, ct)
            ?? throw Validation("supplierId", "تأمین‌کننده پیدا نشد.");
        ValidateSupplier(supplier);
        if (!await ActiveReference(db, supplier.PaymentProfile.PaymentTermId!.Value, "PAYMENT_TERM", ct))
            throw Validation("supplierId", "Payment Term فعال و معتبر الزامی است.");
        var currency = await db.Currencies.SingleOrDefaultAsync(x => x.Id == request.CurrencyId, ct)
            ?? throw Validation("currencyId", "ارز پیدا نشد.");
        if (!currency.IsActive) throw Validation("currencyId", "ارز فعال الزامی است.");

        var snapshots = new List<LineSnapshot>();
        foreach (var line in request.Lines)
            snapshots.Add(await ResolveLine(db, supplier.Id, currency.Id, line, null, ct));
        ValidateGrouping(snapshots);
        var first = snapshots[0];
        var profile = supplier.PaymentProfile;
        var order = new PurchaseOrder(await NextNumber(db, transaction, ct), supplier.Id, currency.Id,
            profile.PaymentTermId!.Value, profile.AdvancePercent, profile.BeforeShipmentPercent, profile.CreditPercent,
            profile.CreditDays, profile.CreditAnchor, first.Option.ShippingModeId, first.Option.IncotermId, Clean(request.Notes));
        db.PurchaseOrders.Add(order);
        for (var index = 0; index < snapshots.Count; index++)
        {
            var snapshot = snapshots[index];
            var line = NewLine(order.Id, index + 1, snapshot, request.Lines[index]);
            db.PurchaseOrderLines.Add(line);
            db.DeliverySchedules.Add(new(line.Id, 1, line.OrderedQuantity, request.Lines[index].RequestedReceiptDate));
        }
        await Save(db, ct);
        await transaction.CommitAsync(ct);
        var response = (await Get(order.Id, ct))!;
        await Emit(order.Id, "CREATE", response, null, ct);
        return response;
    }

    public async Task<PurchaseOrderResponse> Update(Guid id, UpdatePurchaseOrderRequest request, CancellationToken ct)
    {
        EnsureExpected(request.ExpectedVersion);
        await using var db = await factory.CreateDbContextAsync(ct);
        var order = await db.PurchaseOrders.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException();
        EnsureVersion(order.Version, request.ExpectedVersion);
        EnsureDraft(order);
        order.EditDraftNotes(Clean(request.Notes));
        await Save(db, ct);
        var response = (await Get(id, ct))!;
        await Emit(id, "EDIT", response, null, ct);
        return response;
    }

    public async Task<PurchaseOrderResponse> UpdateLine(Guid id, Guid lineId, UpdatePurchaseOrderLineRequest request,
        CancellationToken ct)
    {
        EnsureExpected(request.ExpectedVersion);
        ValidateLine(request.Line);
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await LockAggregate(db, transaction, id, ct);
        EnsureVersion(order.Version, request.ExpectedVersion);
        EnsureDraft(order);
        EnsureManualCore(order);
        var line = await db.PurchaseOrderLines.SingleOrDefaultAsync(x => x.Id == lineId && x.PurchaseOrderId == id, ct)
            ?? throw new KeyNotFoundException();
        var snapshot = await ResolveLine(db, order.SupplierId, order.CurrencyId, request.Line, order, ct);
        line.ReplaceDraftSnapshot(snapshot.Option.ItemId, snapshot.Option.Id, snapshot.Price.Id, snapshot.Option.ItemBaseUnitId,
            request.Line.OrderedQuantity, snapshot.Price.UnitPrice, snapshot.Option.MinimumOrderQuantity,
            snapshot.Option.SupplierLeadTimeDays, snapshot.Option.ForwarderToShipmentDays, snapshot.Option.ShippingPeriodDays,
            snapshot.Option.DestinationCustomsDays, Clean(request.Line.Notes));
        var oldSchedules = await db.DeliverySchedules.Where(x => x.PurchaseOrderLineId == lineId).ToListAsync(ct);
        db.DeliverySchedules.RemoveRange(oldSchedules);
        db.DeliverySchedules.Add(new(line.Id, 1, line.OrderedQuantity, request.Line.RequestedReceiptDate));
        order.TouchDraft();
        await Save(db, ct);
        await transaction.CommitAsync(ct);
        var response = (await Get(id, ct))!;
        await Emit(id, "LINE_EDIT", response, null, ct);
        return response;
    }

    public async Task<PurchaseOrderResponse> AddLine(Guid id, AddPurchaseOrderLineRequest request, CancellationToken ct)
    {
        EnsureExpected(request.ExpectedVersion);
        ValidateLine(request.Line);
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await LockAggregate(db, transaction, id, ct);
        EnsureVersion(order.Version, request.ExpectedVersion);
        EnsureDraft(order);
        EnsureManualCore(order);
        var snapshot = await ResolveLine(db, order.SupplierId, order.CurrencyId, request.Line, order, ct);
        var nextLineNumber = (await db.PurchaseOrderLines.Where(x => x.PurchaseOrderId == id)
            .MaxAsync(x => (int?)x.LineNumber, ct) ?? 0) + 1;
        var line = NewLine(id, nextLineNumber, snapshot, request.Line);
        db.PurchaseOrderLines.Add(line);
        db.DeliverySchedules.Add(new(line.Id, 1, line.OrderedQuantity, request.Line.RequestedReceiptDate));
        order.TouchDraft();
        await Save(db, ct);
        await transaction.CommitAsync(ct);
        var response = (await Get(id, ct))!;
        await Emit(id, "LINE_ADD", response, null, ct);
        return response;
    }

    public async Task<PurchaseOrderResponse> RemoveLine(Guid id, Guid lineId, ExpectedVersionRequest request,
        CancellationToken ct)
    {
        EnsureExpected(request.ExpectedVersion);
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await LockAggregate(db, transaction, id, ct);
        EnsureVersion(order.Version, request.ExpectedVersion);
        EnsureDraft(order);
        EnsureManualCore(order);
        if (await db.PurchaseOrderLines.CountAsync(x => x.PurchaseOrderId == id, ct) <= 1)
            throw Validation("lineId", "آخرین Line قابل حذف نیست.");
        var line = await db.PurchaseOrderLines.SingleOrDefaultAsync(x => x.Id == lineId && x.PurchaseOrderId == id, ct)
            ?? throw new KeyNotFoundException();
        var schedules = await db.DeliverySchedules.Where(x => x.PurchaseOrderLineId == lineId).ToListAsync(ct);
        db.DeliverySchedules.RemoveRange(schedules);
        db.PurchaseOrderLines.Remove(line);
        order.TouchDraft();
        await Save(db, ct);
        await transaction.CommitAsync(ct);
        var response = (await Get(id, ct))!;
        await Emit(id, "LINE_REMOVE", response, null, ct);
        return response;
    }

    public async Task<PurchaseOrderResponse> ReplaceSchedules(Guid id, Guid lineId, ReplaceSchedulesRequest request,
        CancellationToken ct)
    {
        EnsureExpected(request.ExpectedVersion);
        if (request.Schedules.Count == 0 || request.Schedules.Any(x => x.AllocatedQuantity <= 0))
            throw Validation("schedules", "حداقل یک Schedule با مقدار مثبت الزامی است.");
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await LockAggregate(db, transaction, id, ct);
        EnsureVersion(order.Version, request.ExpectedVersion);
        EnsureDraft(order);
        if (order.CreationSource == "EXPERT_DECISION" && string.IsNullOrWhiteSpace(request.Reason))
            throw Validation("reason", "دلیل تغییر برنامه تحویل PO مشتق از تصمیم الزامی است.");
        var line = await db.PurchaseOrderLines.SingleOrDefaultAsync(x => x.Id == lineId && x.PurchaseOrderId == id, ct)
            ?? throw new KeyNotFoundException();
        if (request.Schedules.Sum(x => x.AllocatedQuantity) != line.OrderedQuantity)
            throw Validation("schedules", "جمع Scheduleها باید دقیقاً برابر مقدار Line باشد.");
        var current = await db.DeliverySchedules.Where(x => x.PurchaseOrderLineId == lineId).ToListAsync(ct);
        if (order.CreationSource == "EXPERT_DECISION")
            db.DeliveryScheduleAllocationEvents.Add(new(order.Id, lineId,
                System.Text.Json.JsonSerializer.Serialize(current.OrderBy(x => x.ScheduleNumber).Select(x => new { x.AllocatedQuantity, x.RequestedReceiptDate })),
                System.Text.Json.JsonSerializer.Serialize(request.Schedules), request.Reason!.Trim(),
                AuditEventContext.ActorId(accessor.HttpContext), DateTime.UtcNow));
        db.DeliverySchedules.RemoveRange(current);
        db.DeliverySchedules.AddRange(request.Schedules.Select((x, index) =>
            new DeliverySchedule(lineId, index + 1, x.AllocatedQuantity, x.RequestedReceiptDate)));
        order.TouchDraft();
        await Save(db, ct);
        await transaction.CommitAsync(ct);
        var response = (await Get(id, ct))!;
        await Emit(id, "SCHEDULE_REPLACE", response, request.Reason, ct);
        return response;
    }

    public async Task<PurchaseOrderResponse> RefreshSnapshot(Guid id, ExpectedVersionRequest request, CancellationToken ct)
    {
        EnsureExpected(request.ExpectedVersion);
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await LockAggregate(db, transaction, id, ct);
        EnsureVersion(order.Version, request.ExpectedVersion);
        EnsureDraft(order);
        EnsureManualCore(order);
        var supplier = await db.Suppliers.Include(x => x.PaymentProfile).ThenInclude(x => x.PaymentTerm)
            .SingleAsync(x => x.Id == order.SupplierId, ct);
        ValidateSupplier(supplier);
        if (!await ActiveReference(db, supplier.PaymentProfile.PaymentTermId!.Value, "PAYMENT_TERM", ct))
            throw Validation("supplierId", "Payment Term فعال و معتبر الزامی است.");
        var lines = await db.PurchaseOrderLines.Where(x => x.PurchaseOrderId == id).OrderBy(x => x.LineNumber).ToListAsync(ct);
        var snapshots = new List<LineSnapshot>();
        foreach (var line in lines)
        {
            var input = new PurchaseOrderLineInput(line.SourcingOptionId, line.SourcingPriceId, line.OrderedQuantity, null, line.Notes);
            snapshots.Add(await ResolveLine(db, order.SupplierId, order.CurrencyId, input, null, ct));
        }
        ValidateGrouping(snapshots);
        var first = snapshots[0];
        for (var i = 0; i < lines.Count; i++)
        {
            var snapshot = snapshots[i];
            lines[i].RefreshDraftSnapshot(snapshot.Option.ItemBaseUnitId, snapshot.Price.UnitPrice,
                snapshot.Option.MinimumOrderQuantity, snapshot.Option.SupplierLeadTimeDays,
                snapshot.Option.ForwarderToShipmentDays, snapshot.Option.ShippingPeriodDays,
                snapshot.Option.DestinationCustomsDays);
        }
        var profile = supplier.PaymentProfile;
        order.RefreshDraftSnapshot(profile.PaymentTermId!.Value, profile.AdvancePercent, profile.BeforeShipmentPercent,
            profile.CreditPercent, profile.CreditDays, profile.CreditAnchor, first.Option.ShippingModeId, first.Option.IncotermId);
        await Save(db, ct);
        await transaction.CommitAsync(ct);
        var response = (await Get(id, ct))!;
        await Emit(id, "SNAPSHOT_REFRESH", response, null, ct);
        return response;
    }

    public Task<PurchaseOrderResponse> Approve(Guid id, ExpectedVersionRequest request, CancellationToken ct) =>
        ChangeLifecycle(id, request.ExpectedVersion, PurchaseOrderStatuses.Approved, null, null, ct);

    public Task<PurchaseOrderResponse> ReturnToDraft(Guid id, ReturnToDraftRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) throw Validation("reason", "علت بازگشت به Draft الزامی است.");
        return ChangeLifecycle(id, request.ExpectedVersion, PurchaseOrderStatuses.Draft, request.Reason.Trim(), null, ct);
    }

    public Task<PurchaseOrderResponse> MarkOrdered(Guid id, MarkOrderedRequest request, CancellationToken ct) =>
        ChangeLifecycle(id, request.ExpectedVersion, PurchaseOrderStatuses.Ordered, null, request.OrderedDate, ct);

    private async Task<PurchaseOrderResponse> ChangeLifecycle(Guid id, long expectedVersion, string targetStatus,
        string? reason, DateOnly? orderedDate, CancellationToken ct)
    {
        EnsureExpected(expectedVersion);
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var order = await LockAggregate(db, transaction, id, ct);
        EnsureVersion(order.Version, expectedVersion);
        var from = order.Status;
        var now = DateTime.UtcNow;
        var originalPlans = new List<DeliverySchedulePlanRevision>();
        if (targetStatus == PurchaseOrderStatuses.Approved)
        {
            await ValidateCommitment(db, order, ct);
            order.Approve(now);
        }
        else if (targetStatus == PurchaseOrderStatuses.Draft)
        {
            order.ReturnToDraft(now);
        }
        else if (targetStatus == PurchaseOrderStatuses.Ordered)
        {
            if (!orderedDate.HasValue) throw Validation("orderedDate", "تاریخ سفارش الزامی است.");
            await ValidateCommitment(db, order, ct);
            var lines = await db.PurchaseOrderLines.Where(x => x.PurchaseOrderId == id)
                .Include(x => x.Schedules).ToListAsync(ct);
            order.MarkOrdered(orderedDate.Value, now);
            foreach (var line in lines)
                foreach (var schedule in line.Schedules)
                {
                    var supplierReady = line.SupplierLeadTimeDays.HasValue
                        ? orderedDate.Value.AddDays(line.SupplierLeadTimeDays.Value) : (DateOnly?)null;
                    DateOnly? shipped = supplierReady.HasValue && line.ForwarderToShipmentDays.HasValue
                        ? supplierReady.Value.AddDays(line.ForwarderToShipmentDays.Value) : null;
                    DateOnly? arrived = shipped.HasValue && line.ShippingPeriodDays.HasValue
                        ? shipped.Value.AddDays(line.ShippingPeriodDays.Value) : null;
                    DateOnly? receipt = arrived.HasValue && line.DestinationCustomsDays.HasValue
                        ? arrived.Value.AddDays(line.DestinationCustomsDays.Value) : null;
                    originalPlans.Add(new(schedule.Id, PurchasePlanTypes.Original, 1,
                        supplierReady, null, shipped, arrived, null, receipt, null,
                        AuditEventContext.ActorId(accessor.HttpContext), now));
                }
        }
        else throw new InvalidOperationException("UNSUPPORTED_PURCHASE_ORDER_TRANSITION");

        db.PurchaseOrderLifecycleEvents.Add(new(id, from, targetStatus, reason,
            AuditEventContext.ActorId(accessor.HttpContext), now));
        await Save(db, ct);
        if (originalPlans.Count > 0)
        {
            db.DeliverySchedulePlanRevisions.AddRange(originalPlans);
            await Save(db, ct);
        }
        await transaction.CommitAsync(ct);
        var response = (await Get(id, ct))!;
        await Emit(id, targetStatus == PurchaseOrderStatuses.Draft ? "RETURN_TO_DRAFT" : targetStatus,
            response, reason, ct);
        return response;
    }

    public async Task<PurchaseOrderResponse> ReviseSchedule(Guid id, Guid scheduleId,
        ReviseSchedulePlanRequest request, CancellationToken ct)
    {
        EnsureExpected(request.ExpectedVersion);
        if (string.IsNullOrWhiteSpace(request.Reason)) throw Validation("reason", "علت اصلاح برنامه الزامی است.");
        ValidateChronology(request.SupplierReadyDate, request.ShippedDate, request.ArrivedDestinationDate,
            request.WarehouseReceiptDate);
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await LockAggregate(db, transaction, id, ct);
        EnsureVersion(order.Version, request.ExpectedVersion);
        if (!PurchaseOrderStatuses.IsFirm(order.Status))
            throw Validation("status", "اصلاح برنامه فقط برای سفارش Firm مجاز است.");
        var schedule = await db.DeliverySchedules
            .Include(x => x.PurchaseOrderLine)
            .SingleOrDefaultAsync(x => x.Id == scheduleId && x.PurchaseOrderLine.PurchaseOrderId == id, ct)
            ?? throw new KeyNotFoundException();
        var next = await db.DeliverySchedulePlanRevisions.Where(x => x.DeliveryScheduleId == scheduleId)
            .MaxAsync(x => (int?)x.RevisionNumber, ct) + 1 ?? 1;
        db.DeliverySchedulePlanRevisions.Add(new(scheduleId, PurchasePlanTypes.Revised, next,
            request.SupplierReadyDate, request.HandoverToForwarderDate, request.ShippedDate,
            request.ArrivedDestinationDate, request.CustomsClearedDate, request.WarehouseReceiptDate,
            request.Reason.Trim(), AuditEventContext.ActorId(accessor.HttpContext), DateTime.UtcNow));
        order.TouchForPlanRevision();
        await Save(db, ct);
        await transaction.CommitAsync(ct);
        var response = (await Get(id, ct))!;
        await Emit(id, "PLAN_REVISED", response, request.Reason.Trim(), ct);
        return response;
    }

    public async Task<IReadOnlyList<FirmIncomingResponse>> FirmIncoming(DateOnly evaluationDate, Guid? itemId, DateOnly? etaFrom,
        DateOnly? etaTo, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.PurchaseOrders.AsNoTracking().Where(x =>
            x.Status == PurchaseOrderStatuses.Ordered || x.Status == PurchaseOrderStatuses.Shipped ||
            x.Status == PurchaseOrderStatuses.Customs || x.Status == PurchaseOrderStatuses.PartiallyReceived);
        if (itemId.HasValue) query = query.Where(x => x.Lines.Any(l => l.ItemId == itemId));
        var orders = await query.Include(x => x.Lines).ThenInclude(x => x.Item)
            .Include(x => x.Lines).ThenInclude(x => x.Schedules).ThenInclude(x => x.PlanRevisions)
            .Include(x => x.Lines).ThenInclude(x => x.Schedules).ThenInclude(x => x.ReceiptLines).ThenInclude(x => x.Receipt)
            .Include(x => x.Lines).ThenInclude(x => x.Schedules).ThenInclude(x => x.Cancellations)
            .AsSplitQuery().ToListAsync(ct);
        var result = new List<FirmIncomingResponse>();
        foreach (var order in orders)
            foreach (var line in order.Lines.Where(x => !itemId.HasValue || x.ItemId == itemId))
                foreach (var schedule in line.Schedules)
                {
                    var received = schedule.ReceiptLines.Where(x => x.Receipt.Status == PurchaseReceiptStatuses.Posted).Sum(x => x.Quantity);
                    var cancelled = schedule.Cancellations.Sum(x => x.Quantity);
                    var open = schedule.AllocatedQuantity - received - cancelled;
                    if (open <= 0) continue;
                    var (eta, source) = ResolveFirmEta(schedule);
                    if (etaFrom.HasValue && (!eta.HasValue || eta < etaFrom)) continue;
                    if (etaTo.HasValue && (!eta.HasValue || eta > etaTo)) continue;
                    result.Add(new(order.Id, order.PurchaseOrderNumber, line.Id, schedule.Id, ItemRef(line.Item),
                        open, eta, source, ResolveEtaStatus(eta, open, evaluationDate),
                        order.Status));
                }
        return result.OrderBy(x => x.Eta.HasValue ? 0 : 1).ThenBy(x => x.Eta).ThenBy(x => x.PurchaseOrderNumber).ToList();
    }

    private static IQueryable<PurchaseOrder> FullQuery(IQueryable<PurchaseOrder> query) => query
        .Include(x => x.Supplier).Include(x => x.Currency).Include(x => x.PaymentTerm)
        .Include(x => x.ShippingMode).Include(x => x.Incoterm)
        .Include(x => x.LifecycleEvents)
        .Include(x => x.Lines).ThenInclude(x => x.Item)
        .Include(x => x.Lines).ThenInclude(x => x.SourcingOption)
        .Include(x => x.Lines).ThenInclude(x => x.ItemBaseUnit)
        .Include(x => x.Lines).ThenInclude(x => x.Schedules).ThenInclude(x => x.PlanRevisions)
        .Include(x => x.Lines).ThenInclude(x => x.Schedules).ThenInclude(x => x.ActualMilestones)
        .Include(x => x.Lines).ThenInclude(x => x.Schedules).ThenInclude(x => x.ReceiptLines).ThenInclude(x => x.Receipt)
        .Include(x => x.Lines).ThenInclude(x => x.Schedules).ThenInclude(x => x.Cancellations);

    private static PurchaseOrderResponse ToResponse(PurchaseOrder order)
    {
        var lines = order.Lines.OrderBy(x => x.LineNumber).Select(line =>
        {
            var schedules = line.Schedules.OrderBy(x => x.ScheduleNumber).Select(schedule =>
            {
                var (eta, source) = ResolveFirmEta(schedule);
                var received = schedule.ReceiptLines.Where(x => x.Receipt.Status == PurchaseReceiptStatuses.Posted).Sum(x => x.Quantity);
                var cancelled = schedule.Cancellations.Sum(x => x.Quantity);
                var actualMilestones = schedule.ActualMilestones.Select(x => new ActualMilestoneResponse(
                        x.Id, x.MilestoneCode, x.ActualDate, x.SourceType, x.SourceId, x.CreatedAtUtc,
                        x.SupersedesMilestoneId, x.Reason))
                    .Concat(schedule.ReceiptLines
                        .Where(x => x.Receipt.Status == PurchaseReceiptStatuses.Posted)
                        .Select(x => new ActualMilestoneResponse(x.Id, PurchaseMilestoneCodes.WarehouseReceipt,
                            x.Receipt.EffectiveDate, "PURCHASE_RECEIPT", x.ReceiptId,
                            x.Receipt.PostedAtUtc ?? x.Receipt.CreatedAtUtc, null, null)))
                    .OrderBy(x => x.ActualDate).ThenBy(x => x.CreatedAtUtc).ToList();
                return new DeliveryScheduleResponse(schedule.Id, schedule.ScheduleNumber, schedule.AllocatedQuantity,
                    received, cancelled, schedule.AllocatedQuantity - received - cancelled, schedule.RequestedReceiptDate, eta, source,
                    schedule.PlanRevisions.OrderBy(x => x.RevisionNumber).Select(PlanResponse).ToList(),
                    actualMilestones);
            }).ToList();
            return new PurchaseOrderLineResponse(line.Id, line.LineNumber, ItemRef(line.Item), line.SourcingOptionId,
                line.SourcingOption.SourcingNumber, line.SourcingPriceId, Ref(line.ItemBaseUnit), line.OrderedQuantity,
                line.UnitPrice, line.MinimumOrderQuantity, line.SupplierLeadTimeDays, line.ForwarderToShipmentDays,
                line.ShippingPeriodDays, line.DestinationCustomsDays, line.LineTotal,
                schedules.Sum(x => x.ReceivedQuantity), schedules.Sum(x => x.CancelledQuantity),
                schedules.Sum(x => x.OpenQuantity),
                line.Notes, line.ExpertDecisionId, line.RecommendationId, line.OrderPlanningGenerationId,
                line.MrpRunId, schedules);
        }).ToList();
        var receivedTotal = lines.Sum(x => x.ReceivedQuantity);
        var cancelledTotal = lines.Sum(x => x.CancelledQuantity);
        var openTotal = lines.Sum(x => x.OpenQuantity);
        var completion = openTotal > 0 ? PurchaseCompletionOutcomes.Open : receivedTotal == 0
            ? PurchaseCompletionOutcomes.FullyCancelled : cancelledTotal > 0
                ? PurchaseCompletionOutcomes.ReceivedWithCancellation : PurchaseCompletionOutcomes.FullyReceived;
        var validReceipts = order.Lines.SelectMany(x => x.Schedules).SelectMany(x => x.ReceiptLines)
            .Where(x => x.Receipt.Status == PurchaseReceiptStatuses.Posted).Select(x => x.Receipt.EffectiveDate).ToList();
        DateOnly? firstReceipt = validReceipts.Count == 0 ? null : validReceipts.Min();
        DateOnly? finalReceipt = completion is PurchaseCompletionOutcomes.FullyReceived or PurchaseCompletionOutcomes.ReceivedWithCancellation
            ? validReceipts.Count == 0 ? null : validReceipts.Max() : null;
        var warnings = order.Lines.Any(x => !x.Item.IsActive) ? new[] { "INACTIVE_ITEM_EXISTING_COMMITMENT" } : [];
        return new(order.Id, order.PurchaseOrderNumber, SupplierRef(order.Supplier), Ref(order.Currency),
            RefRequired(order.PaymentTerm), order.AdvancePercent, order.BeforeShipmentPercent, order.CreditPercent,
            order.CreditDays, order.CreditAnchor, Ref(order.ShippingMode), Ref(order.Incoterm), order.Status,
            order.Notes, order.ApprovedAtUtc, order.OrderedDate, order.OrderedAtUtc, order.Version,
            order.CreatedAtUtc, order.UpdatedAtUtc, lines.Sum(x => x.LineTotal), lines,
            order.LifecycleEvents.OrderBy(x => x.CreatedAtUtc).Select(x => new PurchaseOrderLifecycleResponse(
                x.Id, x.FromStatus, x.ToStatus, x.Reason, x.ActorIdentifier, x.CreatedAtUtc)).ToList(),
            completion, firstReceipt, finalReceipt, warnings);
    }

    private static SchedulePlanRevisionResponse PlanResponse(DeliverySchedulePlanRevision x) => new(x.Id,
        x.PlanType, x.RevisionNumber, x.SupplierReadyDate, x.HandoverToForwarderDate, x.ShippedDate,
        x.ArrivedDestinationDate, x.CustomsClearedDate, x.WarehouseReceiptDate, x.Reason, x.ActorIdentifier, x.CreatedAtUtc);

    private static (DateOnly? Eta, string Source) ResolveFirmEta(DeliverySchedule schedule)
    {
        var revised = schedule.PlanRevisions.Where(x => x.PlanType == PurchasePlanTypes.Revised)
            .OrderByDescending(x => x.RevisionNumber).Select(x => x.WarehouseReceiptDate).FirstOrDefault();
        if (revised.HasValue) return (revised, "REVISED_PLANNED_WAREHOUSE_RECEIPT");
        var original = schedule.PlanRevisions.Where(x => x.PlanType == PurchasePlanTypes.Original)
            .Select(x => x.WarehouseReceiptDate).FirstOrDefault();
        return original.HasValue ? (original, "ORIGINAL_PLANNED_WAREHOUSE_RECEIPT") : (null, "MISSING");
    }

    private static string ResolveEtaStatus(DateOnly? eta, decimal openQuantity, DateOnly evaluationDate)
    {
        if (!eta.HasValue) return FirmEtaStatuses.Missing;
        return openQuantity > 0 && eta.Value < evaluationDate ? FirmEtaStatuses.Overdue : FirmEtaStatuses.OnTime;
    }

    private static PurchaseOrderLine NewLine(Guid orderId, int lineNumber, LineSnapshot snapshot,
        PurchaseOrderLineInput input) => new(orderId, lineNumber, snapshot.Option.ItemId, snapshot.Option.Id,
        snapshot.Price.Id, snapshot.Option.ItemBaseUnitId, input.OrderedQuantity, snapshot.Price.UnitPrice,
        snapshot.Option.MinimumOrderQuantity, snapshot.Option.SupplierLeadTimeDays,
        snapshot.Option.ForwarderToShipmentDays, snapshot.Option.ShippingPeriodDays,
        snapshot.Option.DestinationCustomsDays, Clean(input.Notes));

    private static async Task<LineSnapshot> ResolveLine(HadronDbContext db, Guid supplierId, Guid currencyId,
        PurchaseOrderLineInput input, PurchaseOrder? existingOrder, CancellationToken ct)
    {
        ValidateLine(input);
        var option = await db.SourcingOptions.Include(x => x.Item).ThenInclude(x => x.BaseUnit)
            .Include(x => x.Supplier).Include(x => x.ItemBaseUnit).Include(x => x.ShippingMode)
            .Include(x => x.Incoterm).SingleOrDefaultAsync(x => x.Id == input.SourcingOptionId, ct)
            ?? throw Validation("sourcingOptionId", "گزینه تأمین پیدا نشد.");
        if (!option.IsActive) throw Validation("sourcingOptionId", "گزینه تأمین فعال الزامی است.");
        if (option.SupplierId != supplierId) throw Validation("sourcingOptionId", "همه Lineها باید متعلق به Supplier سفارش باشند.");
        if (!option.Item.IsActive || option.Item.ItemType is not (ItemTypes.Part or ItemTypes.Component))
            throw Validation("sourcingOptionId", "قلم فعال Part/Component الزامی است.");
        if (option.Item.BaseUnitId != option.ItemBaseUnitId || !option.ItemBaseUnit.IsActive)
            throw Validation("sourcingOptionId", "BASE_UNIT_CHANGED");
        if (option.ShippingModeId.HasValue && option.ShippingMode?.IsActive != true)
            throw Validation("sourcingOptionId", "روش حمل تخصیص‌یافته باید فعال باشد.");
        if (option.ShippingModeId.HasValue && !await ActiveReference(db, option.ShippingModeId.Value, "SHIPPING_MODE", ct))
            throw Validation("sourcingOptionId", "نوع Reference روش حمل معتبر نیست.");
        if (option.IncotermId.HasValue && option.Incoterm?.IsActive != true)
            throw Validation("sourcingOptionId", "اینکوترم تخصیص‌یافته باید فعال باشد.");
        if (option.IncotermId.HasValue && !await ActiveReference(db, option.IncotermId.Value, "INCOTERM", ct))
            throw Validation("sourcingOptionId", "نوع Reference اینکوترم معتبر نیست.");
        var price = await db.SourcingPrices.Include(x => x.Currency)
            .SingleOrDefaultAsync(x => x.Id == input.SourcingPriceId && x.SourcingOptionId == option.Id, ct)
            ?? throw Validation("sourcingPriceId", "ردیف قیمت انتخابی متعلق به گزینه تأمین نیست.");
        if (price.CurrencyId != currencyId) throw Validation("sourcingPriceId", "همه Lineها باید ارز PO را داشته باشند.");
        if (!price.Currency.IsActive) throw Validation("sourcingPriceId", "ارز فعال الزامی است.");
        if (option.MinimumOrderQuantity.HasValue && input.OrderedQuantity < option.MinimumOrderQuantity)
            throw Validation("orderedQuantity", "مقدار سفارش از MOQ کمتر است.");
        if (existingOrder is not null && (existingOrder.ShippingModeId != option.ShippingModeId ||
            existingOrder.IncotermId != option.IncotermId))
            throw Validation("sourcingOptionId", "Shipping Mode و Incoterm باید با PO سازگار باشند.");
        return new(option, price);
    }

    private static void ValidateGrouping(IReadOnlyList<LineSnapshot> snapshots)
    {
        if (snapshots.Select(x => x.Option.SupplierId).Distinct().Count() != 1 ||
            snapshots.Select(x => x.Price.CurrencyId).Distinct().Count() != 1 ||
            snapshots.Select(x => x.Option.ShippingModeId).Distinct().Count() != 1 ||
            snapshots.Select(x => x.Option.IncotermId).Distinct().Count() != 1)
            throw Validation("lines", "Supplier/Currency/Shipping Mode/Incoterm همه Lineها باید یکسان باشد.");
    }

    private static void ValidateSupplier(Supplier supplier)
    {
        if (!supplier.IsActive) throw Validation("supplierId", "تأمین‌کننده فعال الزامی است.");
        var profile = supplier.PaymentProfile;
        if (!profile.PaymentTermId.HasValue || profile.PaymentTerm?.IsActive != true)
            throw Validation("supplierId", "Payment Term فعال برای Supplier الزامی است.");
        if (profile.AdvancePercent + profile.BeforeShipmentPercent + profile.CreditPercent != 100m)
            throw Validation("supplierId", "جمع درصدهای پرداخت Supplier باید دقیقاً ۱۰۰ باشد.");
        if (profile.CreditPercent > 0 && (profile.CreditDays is null or <= 0 ||
            profile.CreditAnchor is not ("ORDERED" or "SHIPPED" or "WAREHOUSE_RECEIPT")))
            throw Validation("supplierId", "قواعد Credit Supplier معتبر نیست.");
        if (profile.CreditPercent == 0 && (profile.CreditDays.HasValue || profile.CreditAnchor is not null))
            throw Validation("supplierId", "Credit صفر باید Days/Anchor خالی داشته باشد.");
    }

    private static async Task ValidateCommitment(HadronDbContext db, PurchaseOrder order, CancellationToken ct)
    {
        var loaded = await db.PurchaseOrders.AsNoTracking().Include(x => x.Supplier).ThenInclude(x => x.PaymentProfile)
            .Include(x => x.Currency).Include(x => x.PaymentTerm).Include(x => x.ShippingMode).Include(x => x.Incoterm)
            .Include(x => x.Lines).ThenInclude(x => x.Item)
            .Include(x => x.Lines).ThenInclude(x => x.ItemBaseUnit)
            .Include(x => x.Lines).ThenInclude(x => x.SourcingOption)
            .Include(x => x.Lines).ThenInclude(x => x.SourcingPrice).ThenInclude(x => x.Currency)
            .Include(x => x.Lines).ThenInclude(x => x.Schedules)
            .AsSplitQuery().SingleAsync(x => x.Id == order.Id, ct);
        if (!loaded.Supplier.IsActive || !loaded.Currency.IsActive || !loaded.PaymentTerm.IsActive ||
            loaded.ShippingMode is { IsActive: false } || loaded.Incoterm is { IsActive: false })
            throw Validation("references", "همه Referenceهای تعهد باید فعال باشند.");
        if (!await ActiveReference(db, loaded.PaymentTermId, "PAYMENT_TERM", ct) ||
            (loaded.ShippingModeId.HasValue && !await ActiveReference(db, loaded.ShippingModeId.Value, "SHIPPING_MODE", ct)) ||
            (loaded.IncotermId.HasValue && !await ActiveReference(db, loaded.IncotermId.Value, "INCOTERM", ct)))
            throw Validation("references", "نوع Referenceهای تعهد معتبر نیست.");
        if (loaded.Lines.Count == 0) throw Validation("lines", "حداقل یک Line الزامی است.");
        foreach (var line in loaded.Lines)
        {
            if (!line.Item.IsActive || !line.ItemBaseUnit.IsActive || line.Item.BaseUnitId != line.ItemBaseUnitId ||
                !line.SourcingOption.IsActive || line.SourcingOption.SupplierId != loaded.SupplierId ||
                line.SourcingPrice.CurrencyId != loaded.CurrencyId)
                throw Validation("lines", "Line برای تعهد عملیاتی معتبر نیست.");
            if (line.Schedules.Count == 0 || line.Schedules.Sum(x => x.AllocatedQuantity) != line.OrderedQuantity)
                throw Validation("schedules", "Scheduleهای هر Line باید دقیقاً با OrderedQty تطبیق داشته باشند.");
        }
    }

    private static void ValidateCreate(CreatePurchaseOrderRequest request)
    {
        if (request.SupplierId == Guid.Empty) throw Validation("supplierId", "Supplier الزامی است.");
        if (request.CurrencyId == Guid.Empty) throw Validation("currencyId", "Currency الزامی است.");
        if (request.Lines.Count == 0) throw Validation("lines", "حداقل یک Line الزامی است.");
        foreach (var line in request.Lines) ValidateLine(line);
    }

    private static Task<bool> ActiveReference(HadronDbContext db, Guid id, string type, CancellationToken ct) =>
        db.ReferenceValues.AnyAsync(x => x.Id == id && x.IsActive && x.ReferenceType.Code == type, ct);

    private static void ValidateLine(PurchaseOrderLineInput input)
    {
        if (input.SourcingOptionId == Guid.Empty) throw Validation("sourcingOptionId", "گزینه تأمین الزامی است.");
        if (input.SourcingPriceId == Guid.Empty) throw Validation("sourcingPriceId", "ردیف قیمت الزامی است.");
        if (input.OrderedQuantity <= 0) throw Validation("orderedQuantity", "مقدار سفارش باید مثبت باشد.");
    }

    private static void ValidateChronology(DateOnly? supplierReady, DateOnly? shipped, DateOnly? arrived,
        DateOnly? receipt)
    {
        if ((supplierReady.HasValue && shipped.HasValue && supplierReady > shipped) ||
            (shipped.HasValue && arrived.HasValue && shipped > arrived) ||
            (arrived.HasValue && receipt.HasValue && arrived > receipt))
            throw Validation("milestones", "ترتیب تاریخ‌های اصلی برنامه معتبر نیست.");
    }

    private static void EnsureDraft(PurchaseOrder order)
    {
        if (order.Status != PurchaseOrderStatuses.Draft) throw Conflict("PO_SNAPSHOT_LOCKED", "Snapshot سفارش قفل است.");
    }

    private static void EnsureManualCore(PurchaseOrder order)
    {
        if (order.CreationSource == "EXPERT_DECISION")
            throw Conflict("DECISION_DERIVED_CORE_IMMUTABLE", "فیلدهای اصلی PO مشتق از تصمیم Expert قابل تغییر نیستند.");
    }

    private static void EnsureExpected(long expected)
    {
        if (expected < 1) throw Validation("expectedVersion", "نسخه معتبر الزامی است.");
    }

    private static void EnsureVersion(long actual, long expected)
    {
        if (actual != expected) throw Conflict("STALE_VERSION", "نسخه ارسالی قدیمی است.");
    }

    private static void ValidatePaging(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100) throw Validation("page", "صفحه معتبر و اندازه ۱ تا ۱۰۰ الزامی است.");
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static PurchaseOrderRef Ref(MutableReferenceData x) => new(x.Id, x.Code, x.NameFa, x.NameEn, x.IsActive);
    private static PurchaseOrderRef RefRequired(ReferenceValue x) => new(x.Id, x.Code, x.NameFa, x.NameEn, x.IsActive);
    private static PurchaseOrderRef? Ref(ReferenceValue? x) => x is null ? null : new(x.Id, x.Code, x.NameFa, x.NameEn, x.IsActive);
    private static PurchaseOrderSupplierRef SupplierRef(Supplier x) => new(x.Id, x.SupplierNumber, x.Name, x.IsActive);
    private static PurchaseOrderItemRef ItemRef(Item x) => new(x.Id, x.BusinessNumber, x.NameFa, x.NameEn, x.ItemType, x.IsActive);

    private static async Task<string> NextNumber(HadronDbContext db, IDbContextTransaction transaction, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);
        var npgsqlTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();
        await using var command = new NpgsqlCommand("SELECT nextval('procurement.purchase_order_number_seq')", connection, npgsqlTransaction);
        var value = (long)(await command.ExecuteScalarAsync(ct))!;
        return $"PO-{value:D6}";
    }

    private static async Task<PurchaseOrder> LockAggregate(HadronDbContext db, IDbContextTransaction transaction,
        Guid id, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var npgsqlTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();
        await using var command = new NpgsqlCommand(
            "SELECT \"Id\" FROM procurement.purchase_orders WHERE \"Id\" = @id FOR UPDATE", connection, npgsqlTransaction);
        command.Parameters.AddWithValue("id", id);
        if (await command.ExecuteScalarAsync(ct) is null) throw new KeyNotFoundException();
        db.ChangeTracker.Clear();
        return await db.PurchaseOrders.SingleAsync(x => x.Id == id, ct);
    }

    private static async Task Save(HadronDbContext db, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw Conflict("STALE_VERSION", "نسخه ارسالی قدیمی است."); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw Conflict("PURCHASE_SUPPLY_INTEGRITY_CONFLICT", "تعارض یکتایی در سفارش خرید."); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.CheckViolation or PostgresErrorCodes.RaiseException })
        { throw Validation("integrity", ex.InnerException.Message); }
    }

    private async Task Emit(Guid id, string action, object value, string? reason, CancellationToken ct)
    {
        try
        {
            await auditSink.WriteAsync(new("PurchaseOrder", id, action, DateTime.UtcNow,
                AuditEventContext.ActorId(accessor.HttpContext), AuditEventContext.CorrelationId(accessor.HttpContext),
                null, new Dictionary<string, object?> { ["Value"] = value }, reason), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { logger.LogError(ex, "Post-commit PO audit failed for {Id} {Action}", id, action); }
    }

    private static PurchaseSupplyValidationException Validation(string key, string message) =>
        new(new Dictionary<string, string[]> { [key] = [message] });
    private static PurchaseSupplyConflictException Conflict(string code, string message) => new(code, message);
    private sealed record LineSnapshot(SourcingOption Option, SourcingPrice Price);
}

public sealed class PurchaseSupplyValidationException(IReadOnlyDictionary<string, string[]> errors) : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public class PurchaseSupplyConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
