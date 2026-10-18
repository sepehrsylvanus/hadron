using System.Data;
using System.Text.Json;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Inventory.Application;
using Hadron.Erp.Api.Modules.Inventory.Domain;
using Hadron.Erp.Api.Modules.Mrp.Domain;
using Hadron.Erp.Api.Modules.OrderPlanning.Domain;
using Hadron.Erp.Api.Modules.PurchaseSupply.Domain;
using Hadron.Erp.Api.Modules.Sourcing.Application;
using Hadron.Erp.Api.Modules.Sourcing.Domain;
using Hadron.Erp.Api.Platform.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Hadron.Erp.Api.Modules.PurchaseSupply.Application;

public interface IPurchaseReceiptFaultInjector
{
    Task AfterReceiptBeforeInventory(CancellationToken ct);
    Task AfterInventoryBeforeReceiptCommit(CancellationToken ct);
}

public sealed class NoOpPurchaseReceiptFaultInjector : IPurchaseReceiptFaultInjector
{
    public Task AfterReceiptBeforeInventory(CancellationToken ct) => Task.CompletedTask;
    public Task AfterInventoryBeforeReceiptCommit(CancellationToken ct) => Task.CompletedTask;
}

public sealed class PurchaseOrderOperationsService(
    IDbContextFactory<HadronDbContext> factory,
    PurchaseReceiptInventoryCoordinator inventory,
    IPurchaseReceiptFaultInjector faultInjector,
    TimeProvider timeProvider,
    IAuditEventSink auditSink,
    IHttpContextAccessor accessor,
    ILogger<PurchaseOrderOperationsService> logger)
{
    private DateOnly TehranToday => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
        timeProvider.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "Iran Standard Time" : "Asia/Tehran")).DateTime);
    private string? Actor() => AuditEventContext.ActorId(accessor.HttpContext);

    public async Task<ConvertExpertDecisionsResponse> Convert(ConvertExpertDecisionsRequest request, CancellationToken ct)
    {
        if (request.DecisionIds.Count == 0 || request.DecisionIds.Any(x => x == Guid.Empty))
            throw Validation("decisionIds", "حداقل یک تصمیم معتبر الزامی است.");
        if (request.DecisionIds.Distinct().Count() != request.DecisionIds.Count)
            throw Validation("decisionIds", "تصمیم تکراری در درخواست مجاز نیست.");

        await using var read = await factory.CreateDbContextAsync(ct);
        var rows = await LoadDecisions(read, request.DecisionIds, ct);
        var found = rows.Select(x => x.Decision.Id).ToHashSet();
        var failures = request.DecisionIds.Where(x => !found.Contains(x)).Select(x =>
            new ConversionGroupResult($"decision:{x}", false, null, null,
                [new(x, false, null, null, "DECISION_NOT_FOUND", [])])).ToList();

        var candidates = new List<ConversionCandidate>();
        foreach (var row in rows)
        {
            var existing = await read.PurchaseOrderLines.AsNoTracking()
                .Where(x => x.ExpertDecisionId == row.Decision.Id)
                .Select(x => new { x.PurchaseOrderId, x.PurchaseOrder.PurchaseOrderNumber }).SingleOrDefaultAsync(ct);
            if (existing is not null)
            {
                failures.Add(new($"existing:{existing.PurchaseOrderId}", true, existing.PurchaseOrderId,
                    existing.PurchaseOrderNumber, [new(row.Decision.Id, true, existing.PurchaseOrderId,
                        existing.PurchaseOrderNumber, null, [])]));
                continue;
            }
            var eligibility = ValidateDecisionEligibility(row);
            if (eligibility is not null)
            {
                failures.Add(Failed(row.Decision.Id, eligibility));
                continue;
            }
            var snapshot = Parse(row.Decision.FinalSourceSnapshot!);
            candidates.Add(new(row, snapshot, GroupKey(snapshot)));
        }

        foreach (var group in candidates.GroupBy(x => x.GroupKey).OrderBy(x => x.Key))
        {
            try { failures.Add(await ConvertGroup(group.Key, group.ToList(), ct)); }
            catch (PurchaseSupplyConflictException ex)
            {
                failures.Add(new(group.Key, false, null, null, group.Select(x =>
                    new DecisionConversionResult(x.Row.Decision.Id, false, null, null, ex.Code,
                        ex is DecisionReconfirmationException d ? d.Reasons : [])).ToList()));
            }
            catch (PurchaseSupplyValidationException)
            {
                failures.Add(new(group.Key, false, null, null, group.Select(x =>
                    new DecisionConversionResult(x.Row.Decision.Id, false, null, null,
                        "DECISION_CONVERSION_VALIDATION_FAILED", [])).ToList()));
            }
        }
        return new(failures);
    }

    private async Task<ConversionGroupResult> ConvertGroup(string key, IReadOnlyList<ConversionCandidate> candidates,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        foreach (var id in candidates.Select(x => x.Row.Recommendation.Id).Distinct().OrderBy(x => x))
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM planning.order_recommendations WHERE \"Id\"={id} FOR UPDATE", ct);
        foreach (var id in candidates.Select(x => x.Row.Generation.Id).Distinct().OrderBy(x => x))
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM planning.order_planning_generations WHERE \"Id\"={id} FOR UPDATE", ct);
        foreach (var id in candidates.Select(x => x.Row.Decision.Id).OrderBy(x => x))
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM planning.order_recommendation_decisions WHERE \"Id\"={id} FOR UPDATE", ct);
        var decisionIds = candidates.Select(c => c.Row.Decision.Id).ToList();
        var existing = await db.PurchaseOrderLines.Where(x => x.ExpertDecisionId != null &&
                decisionIds.Contains(x.ExpertDecisionId.Value))
            .Select(x => new
            {
                DecisionId = x.ExpertDecisionId!.Value,
                x.PurchaseOrderId,
                x.PurchaseOrder.PurchaseOrderNumber
            }).ToListAsync(ct);
        if (existing.Count > 0)
        {
            if (existing.Count == candidates.Count && existing.Select(x => x.PurchaseOrderId).Distinct().Count() == 1)
            {
                var converted = existing[0]; await tx.CommitAsync(ct);
                return new(key, true, converted.PurchaseOrderId, converted.PurchaseOrderNumber,
                    candidates.Select(candidate => new DecisionConversionResult(candidate.Row.Decision.Id, true,
                        converted.PurchaseOrderId, converted.PurchaseOrderNumber, null, [])).ToList());
            }
            throw Conflict("DECISION_ALREADY_CONVERTED", "حداقل یک تصمیم قبلاً در گروه دیگری تبدیل شده است.");
        }

        var authoritativeRows = await LoadDecisions(db, decisionIds, ct);
        var latestRunId = await db.MrpPlanSlots.AsNoTracking()
            .Where(x => x.SlotName == MrpPlanSlotNames.Current)
            .Select(x => (Guid?)x.RunId).SingleOrDefaultAsync(ct) ??
            await db.MrpRuns.AsNoTracking()
                .Where(x => x.Status == MrpRunStatuses.Completed || x.Status == MrpRunStatuses.CompletedWithExceptions)
                .OrderByDescending(x => x.CompletedAtUtc).ThenByDescending(x => x.StartedAtUtc)
                .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        var authoritativeCandidates = new List<ConversionCandidate>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var row = authoritativeRows.SingleOrDefault(x => x.Decision.Id == candidate.Row.Decision.Id)
                ?? throw Conflict("DECISION_NOT_FOUND", "تصمیم در زمان تبدیل دیگر قابل بازیابی نیست.");
            var latestDecisionVersion = await db.OrderRecommendationDecisions.AsNoTracking()
                .Where(x => x.RecommendationId == row.Recommendation.Id)
                .MaxAsync(x => (long?)x.DecisionVersion, ct);
            var eligibility = ValidateDecisionEligibility(row);
            if (eligibility is not null || latestDecisionVersion != row.Decision.DecisionVersion ||
                latestRunId != row.Generation.RunId)
                throw Conflict(eligibility ?? (latestRunId != row.Generation.RunId ? "OUTDATED_RUN" : "DECISION_NOT_CURRENT"),
                    "فقط آخرین تصمیم معتبر از آخرین Run تکمیل‌شده قابل تبدیل است.");
            var snapshot = Parse(row.Decision.FinalSourceSnapshot!);
            if (!string.Equals(GroupKey(snapshot), key, StringComparison.Ordinal))
                throw Conflict("DECISION_GROUP_CHANGED", "کلید تجاری گروه تصمیم تغییر کرده است.");
            authoritativeCandidates.Add(new(row, snapshot, key));
        }

        foreach (var candidate in authoritativeCandidates) await Revalidate(db, candidate, ct);
        var first = authoritativeCandidates[0]; var s = first.Snapshot;
        var order = new PurchaseOrder(await Next(db, "procurement.purchase_order_number_seq", "PO", 6, ct),
            s.SupplierId, s.CurrencyId, s.PaymentTermId, s.AdvancePercent, s.BeforeShipmentPercent,
            s.CreditPercent, s.CreditDays, s.CreditAnchor, s.ShippingModeId, s.IncotermId, null, "EXPERT_DECISION");
        db.PurchaseOrders.Add(order);
        var lineNumber = 0;
        foreach (var candidate in authoritativeCandidates.OrderBy(x => x.Row.Recommendation.ItemCode))
        {
            var d = candidate.Row.Decision; var snapshot = candidate.Snapshot; lineNumber++;
            var orderedQuantity = PurchaseQuantity(d.FinalQty!.Value);
            var line = new PurchaseOrderLine(order.Id, lineNumber, candidate.Row.Recommendation.ItemId,
                snapshot.SourcingOptionId, null, snapshot.ItemBaseUnitId, orderedQuantity,
                d.FinalUnitPrice!.Value, snapshot.MinimumOrderQuantity, snapshot.SupplierLeadTimeDays,
                snapshot.ForwarderToShipmentDays, snapshot.ShippingPeriodDays, snapshot.DestinationCustomsDays,
                null, d.Id, d.RecommendationId, candidate.Row.Recommendation.GenerationId,
                candidate.Row.Generation.RunId);
            db.PurchaseOrderLines.Add(line);
            db.DeliverySchedules.Add(new(line.Id, 1, line.OrderedQuantity, d.FinalTargetReceiptDate));
        }
        await Save(db, ct); await tx.CommitAsync(ct);
        await Emit("PurchaseOrder", order.Id, "CONVERT_EXPERT_DECISIONS", key, null, ct);
        return new(key, true, order.Id, order.PurchaseOrderNumber, authoritativeCandidates.Select(x =>
            new DecisionConversionResult(x.Row.Decision.Id, true, order.Id, order.PurchaseOrderNumber, null, [])).ToList());
    }

    public async Task<PurchaseReceiptResponse> CreateReceipt(CreatePurchaseReceiptRequest request, CancellationToken ct)
    {
        if (request.PurchaseOrderId == Guid.Empty || request.WarehouseId == Guid.Empty || request.Lines.Count == 0)
            throw Validation("receipt", "PO، انبار و حداقل یک ردیف رسید الزامی است.");
        if (request.Lines.Any(x => x.Quantity <= 0 || x.PurchaseOrderLineId == Guid.Empty || x.DeliveryScheduleId == Guid.Empty))
            throw Validation("lines", "ردیف رسید معتبر و مقدار مثبت الزامی است.");
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var order = await LockOrder(db, request.PurchaseOrderId, ct);
        EnsureOrdered(order);
        ValidateReceiptDate(order, request.EffectiveDate);
        var warehouse = await db.Warehouses.SingleOrDefaultAsync(x => x.Id == request.WarehouseId, ct)
            ?? throw new KeyNotFoundException();
        if (!warehouse.IsActive) throw Conflict("INACTIVE_WAREHOUSE", "انبار مقصد غیرفعال است.");
        var receipt = new PurchaseReceipt(await Next(db, "procurement.purchase_receipt_number_seq", "RCV", 6, ct),
            order.Id, warehouse.Id, request.EffectiveDate, Clean(request.Notes), Actor());
        db.PurchaseReceipts.Add(receipt);
        var duplicate = request.Lines.GroupBy(x => new { x.PurchaseOrderLineId, x.DeliveryScheduleId })
            .Any(x => x.Count() > 1);
        if (duplicate) throw Validation("lines", "هر Schedule در یک Receipt فقط یک ردیف دارد.");
        var lineIds = request.Lines.Select(x => x.PurchaseOrderLineId).Distinct().ToList();
        var scheduleIds = request.Lines.Select(x => x.DeliveryScheduleId).Distinct().ToList();
        var lines = await db.PurchaseOrderLines.Where(x => lineIds.Contains(x.Id) && x.PurchaseOrderId == order.Id)
            .ToDictionaryAsync(x => x.Id, ct);
        var schedules = await db.DeliverySchedules.Where(x => scheduleIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        foreach (var input in request.Lines)
        {
            if (!lines.TryGetValue(input.PurchaseOrderLineId, out var line))
                throw Validation("lines", "Line به PO انتخابی تعلق ندارد.");
            if (!schedules.TryGetValue(input.DeliveryScheduleId, out var schedule) || schedule.PurchaseOrderLineId != line.Id)
                throw Validation("lines", "Schedule به Line انتخابی تعلق ندارد.");
            db.PurchaseReceiptLines.Add(new(receipt.Id, line.Id, schedule.Id, line.ItemId, line.ItemBaseUnitId, input.Quantity));
        }
        await Save(db, ct); await tx.CommitAsync(ct);
        await Emit("PurchaseReceipt", receipt.Id, "CREATE_DRAFT", receipt.ReceiptNumber, null, ct);
        return (await GetReceipt(receipt.Id, ct))!;
    }

    public async Task<PurchaseReceiptResponse> PostReceipt(Guid id, PostPurchaseReceiptRequest request, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var receipt = await LockReceipt(db, id, ct); EnsureVersion(receipt.Version, request.ExpectedReceiptVersion);
        var order = await LockOrder(db, receipt.PurchaseOrderId, ct); EnsureVersion(order.Version, request.ExpectedPurchaseOrderVersion);
        EnsureOrdered(order); ValidateReceiptDate(order, receipt.EffectiveDate);
        var lines = await db.PurchaseReceiptLines.Where(x => x.ReceiptId == id).OrderBy(x => x.DeliveryScheduleId).ToListAsync(ct);
        foreach (var scheduleId in lines.Select(x => x.DeliveryScheduleId).Distinct().OrderBy(x => x))
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM procurement.delivery_schedules WHERE \"Id\"={scheduleId} FOR UPDATE", ct);
        var scheduleIds = lines.Select(x => x.DeliveryScheduleId).Distinct().ToList();
        var scheduleBalances = await db.DeliverySchedules.Where(x => scheduleIds.Contains(x.Id)).Select(x => new
        {
            x.Id,
            x.AllocatedQuantity,
            Received = x.ReceiptLines.Where(r => r.Receipt.Status == PurchaseReceiptStatuses.Posted && r.ReceiptId != id)
                .Sum(r => (decimal?)r.Quantity) ?? 0,
            Cancelled = x.Cancellations.Sum(c => (decimal?)c.Quantity) ?? 0
        }).ToDictionaryAsync(x => x.Id, ct);
        foreach (var group in lines.GroupBy(x => x.DeliveryScheduleId))
            if (group.Sum(x => x.Quantity) > scheduleBalances[group.Key].AllocatedQuantity -
                scheduleBalances[group.Key].Received - scheduleBalances[group.Key].Cancelled)
                throw Conflict("OVER_RECEIPT_NOT_ALLOWED", "مقدار رسید از Open Qty برنامه تحویل بیشتر است.");
        var poLines = await db.PurchaseOrderLines.Include(x => x.Item)
            .Where(x => lines.Select(line => line.PurchaseOrderLineId).Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var warnings = new HashSet<string>();
        if (poLines.Values.Any(x => !x.Item.IsActive)) warnings.Add("INACTIVE_ITEM_EXISTING_COMMITMENT");
        receipt.Post(timeProvider.GetUtcNow().UtcDateTime);
        await Save(db, ct);
        await faultInjector.AfterReceiptBeforeInventory(ct);
        var transactions = await inventory.PostBatch(db, lines.Select(line => new PurchaseReceiptInventoryInput(
            line.Id, line.ItemId, line.UnitId, line.Quantity)).ToList(), receipt.WarehouseId,
            receipt.EffectiveDate, Actor(), ct);
        foreach (var line in lines) line.LinkInventory(transactions[line.Id].Id);
        await Save(db, ct);
        await faultInjector.AfterInventoryBeforeReceiptCommit(ct);
        await RefreshStatus(db, order, ct);
        await Save(db, ct); await tx.CommitAsync(ct);
        await Emit("PurchaseReceipt", receipt.Id, "POST", receipt.ReceiptNumber, null, ct);
        return (await GetReceipt(id, ct))! with { Warnings = warnings.ToList() };
    }

    public async Task<PurchaseReceiptResponse> ReverseReceipt(Guid id, ReversePurchaseReceiptRequest request, CancellationToken ct)
    {
        var reason = Clean(request.Reason) ?? throw Validation("reason", "دلیل برگشت الزامی است.");
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var receipt = await LockReceipt(db, id, ct); EnsureVersion(receipt.Version, request.ExpectedReceiptVersion);
        var order = await LockOrder(db, receipt.PurchaseOrderId, ct); EnsureVersion(order.Version, request.ExpectedPurchaseOrderVersion);
        EnsureOrdered(order);
        var lines = await db.PurchaseReceiptLines.Where(x => x.ReceiptId == id).OrderBy(x => x.DeliveryScheduleId).ToListAsync(ct);
        if (lines.Any(x => !x.InventoryTransactionId.HasValue))
            throw Conflict("RECEIPT_INVENTORY_LINK_MISSING", "پیوند موجودی رسید ناقص است.");
        var reversals = await inventory.ReverseBatch(db, lines.ToDictionary(x => x.Id,
            x => x.InventoryTransactionId!.Value), reason, Actor(), ct);
        foreach (var line in lines) line.LinkReversal(reversals[line.Id].Id);
        receipt.Reverse(reason, timeProvider.GetUtcNow().UtcDateTime); await Save(db, ct);
        await RefreshStatus(db, order, ct); await Save(db, ct); await tx.CommitAsync(ct);
        await Emit("PurchaseReceipt", receipt.Id, "REVERSE", receipt.ReceiptNumber, reason, ct);
        return (await GetReceipt(id, ct))!;
    }

    public async Task<PurchaseOrderResponse> Cancel(Guid orderId, Guid scheduleId, CancelScheduleRequest request,
        PurchaseSupplyService purchaseSupply, CancellationToken ct)
    {
        var reason = Clean(request.Reason) ?? throw Validation("reason", "دلیل لغو الزامی است.");
        if (request.Quantity <= 0) throw Validation("quantity", "مقدار لغو باید مثبت باشد.");
        if (request.EffectiveDate > TehranToday) throw Conflict("FUTURE_PURCHASE_OPERATION_NOT_ALLOWED", "تاریخ آینده مجاز نیست.");
        await using var db = await factory.CreateDbContextAsync(ct); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var order = await LockOrder(db, orderId, ct); EnsureVersion(order.Version, request.ExpectedPurchaseOrderVersion); EnsureOrdered(order);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM procurement.delivery_schedules WHERE \"Id\"={scheduleId} FOR UPDATE", ct);
        var schedule = await db.DeliverySchedules.Include(x => x.PurchaseOrderLine).ThenInclude(x => x.Item)
            .SingleOrDefaultAsync(x => x.Id == scheduleId && x.PurchaseOrderLine.PurchaseOrderId == orderId, ct)
            ?? throw new KeyNotFoundException();
        var open = await ScheduleOpen(db, scheduleId, null, ct);
        if (request.Quantity > open) throw Conflict("CANCELLATION_EXCEEDS_OPEN_QTY", "مقدار لغو از Open Qty بیشتر است.");
        db.PurchaseScheduleCancellations.Add(new(orderId, schedule.PurchaseOrderLineId, scheduleId,
            request.Quantity, request.EffectiveDate, reason, Actor(), timeProvider.GetUtcNow().UtcDateTime));
        await Save(db, ct); await RefreshStatus(db, order, ct); await Save(db, ct); await tx.CommitAsync(ct);
        await Emit("PurchaseOrder", orderId, "CANCEL_SCHEDULE_QTY", scheduleId, reason, ct);
        return (await purchaseSupply.Get(orderId, ct))!;
    }

    public async Task<PurchaseOrderResponse> RecordMilestone(Guid orderId, Guid scheduleId,
        RecordActualMilestoneRequest request, PurchaseSupplyService purchaseSupply, CancellationToken ct)
    {
        if (!PurchaseMilestoneCodes.All.Contains(request.MilestoneCode) || request.MilestoneCode == PurchaseMilestoneCodes.WarehouseReceipt)
            throw Validation("milestoneCode", "Milestone معتبر و غیر Warehouse Receipt الزامی است.");
        if (request.ActualDate > TehranToday) throw Conflict("FUTURE_ACTUAL_MILESTONE_NOT_ALLOWED", "Actual آینده مجاز نیست.");
        await using var db = await factory.CreateDbContextAsync(ct); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var order = await LockOrder(db, orderId, ct); EnsureVersion(order.Version, request.ExpectedPurchaseOrderVersion); EnsureOrdered(order);
        var schedule = await db.DeliverySchedules.SingleOrDefaultAsync(x => x.Id == scheduleId &&
            x.PurchaseOrderLine.PurchaseOrderId == orderId, ct) ?? throw new KeyNotFoundException();
        DeliveryScheduleActualMilestone? superseded = null;
        if (request.SupersedesMilestoneId.HasValue)
        {
            superseded = await db.DeliveryScheduleActualMilestones.SingleOrDefaultAsync(x =>
                x.Id == request.SupersedesMilestoneId && x.DeliveryScheduleId == scheduleId, ct)
                ?? throw new KeyNotFoundException();
            if (string.IsNullOrWhiteSpace(request.Reason)) throw Validation("reason", "دلیل اصلاح Milestone الزامی است.");
            if (superseded.MilestoneCode != request.MilestoneCode) throw Validation("supersedesMilestoneId", "نوع Milestone اصلاح‌شده باید یکسان باشد.");
        }
        else if (await CurrentMilestones(db, scheduleId).AnyAsync(x => x.MilestoneCode == request.MilestoneCode, ct))
            throw Conflict("ACTUAL_MILESTONE_ALREADY_EXISTS", "برای اصلاح Milestone باید رکورد فعلی مشخص شود.");
        var dates = await CurrentMilestones(db, scheduleId).Where(x => x.Id != request.SupersedesMilestoneId)
            .ToDictionaryAsync(x => x.MilestoneCode, x => x.ActualDate, ct);
        dates[request.MilestoneCode] = request.ActualDate; ValidateActualChronology(dates);
        var id = Guid.NewGuid();
        db.DeliveryScheduleActualMilestones.Add(new(scheduleId, request.MilestoneCode, request.ActualDate,
            "MANUAL_OPERATION", id, Actor(), timeProvider.GetUtcNow().UtcDateTime,
            superseded?.Id, Clean(request.Reason)));
        await Save(db, ct); await RefreshStatus(db, order, ct); await Save(db, ct); await tx.CommitAsync(ct);
        await Emit("PurchaseOrder", orderId, "ACTUAL_MILESTONE", scheduleId, request.Reason, ct);
        return (await purchaseSupply.Get(orderId, ct))!;
    }

    public async Task<PurchaseOrderResponse> ReallocateSchedules(Guid orderId, Guid lineId,
        ReallocateSchedulesRequest request, PurchaseSupplyService purchaseSupply, CancellationToken ct)
    {
        var reason = Clean(request.Reason) ?? throw Validation("reason", "دلیل تغییر Schedule الزامی است.");
        if (request.Schedules.Count == 0 || request.Schedules.Any(x => x.AllocatedQuantity <= 0))
            throw Validation("schedules", "حداقل یک Schedule با مقدار مثبت الزامی است.");
        await using var db = await factory.CreateDbContextAsync(ct); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var order = await LockOrder(db, orderId, ct); EnsureVersion(order.Version, request.ExpectedPurchaseOrderVersion); EnsureOrdered(order);
        var line = await db.PurchaseOrderLines.SingleOrDefaultAsync(x => x.Id == lineId && x.PurchaseOrderId == orderId, ct)
            ?? throw new KeyNotFoundException();
        if (request.Schedules.Sum(x => x.AllocatedQuantity) != line.OrderedQuantity)
            throw Validation("schedules", "جمع Scheduleها باید دقیقاً برابر OrderedQty باشد.");
        var current = await db.DeliverySchedules.Where(x => x.PurchaseOrderLineId == lineId)
            .OrderBy(x => x.ScheduleNumber).ToListAsync(ct);
        if (request.Schedules.Count < current.Count)
            throw Validation("schedules", "پس از ORDERED حذف Schedule مجاز نیست.");
        var before = JsonSerializer.Serialize(current.Select(x => new { x.Id, x.ScheduleNumber, x.AllocatedQuantity, x.RequestedReceiptDate }));
        for (var i = 0; i < current.Count; i++)
        {
            var settled = await db.PurchaseReceiptLines.Where(x => x.DeliveryScheduleId == current[i].Id &&
                x.Receipt.Status == PurchaseReceiptStatuses.Posted).SumAsync(x => (decimal?)x.Quantity, ct) ?? 0;
            settled += await db.PurchaseScheduleCancellations.Where(x => x.DeliveryScheduleId == current[i].Id)
                .SumAsync(x => (decimal?)x.Quantity, ct) ?? 0;
            if (request.Schedules[i].AllocatedQuantity < settled)
                throw Conflict("SCHEDULE_RECEIVED_QTY_CANNOT_MOVE", "AllocatedQty از مقدار settled کمتر نمی‌شود.");
            current[i].Reallocate(request.Schedules[i].AllocatedQuantity, request.Schedules[i].RequestedReceiptDate);
        }
        for (var i = current.Count; i < request.Schedules.Count; i++)
            db.DeliverySchedules.Add(new(lineId, i + 1, request.Schedules[i].AllocatedQuantity,
                request.Schedules[i].RequestedReceiptDate));
        db.DeliveryScheduleAllocationEvents.Add(new(orderId, lineId, before,
            JsonSerializer.Serialize(request.Schedules), reason, Actor(), timeProvider.GetUtcNow().UtcDateTime));
        order.TouchForPlanRevision(); await Save(db, ct); await tx.CommitAsync(ct);
        await Emit("PurchaseOrder", orderId, "SCHEDULE_REALLOCATED", lineId, reason, ct);
        return (await purchaseSupply.Get(orderId, ct))!;
    }

    public async Task<PurchaseReceiptListResponse> ListReceipts(Guid? purchaseOrderId, string? status,
        int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > 100) throw Validation("page", "Paging نامعتبر است.");
        await using var db = await factory.CreateDbContextAsync(ct); var query = ReceiptQuery(db);
        if (purchaseOrderId.HasValue) query = query.Where(x => x.PurchaseOrderId == purchaseOrderId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        var total = await query.CountAsync(ct); var rows = await query.OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize).AsSplitQuery().ToListAsync(ct);
        return new(rows.Select(ReceiptResponse).ToList(), total, page, pageSize);
    }

    public async Task<PurchaseReceiptResponse?> GetReceipt(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await ReceiptQuery(db).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, ct);
        return row is null ? null : ReceiptResponse(row);
    }

    private async Task Revalidate(HadronDbContext db, ConversionCandidate candidate, CancellationToken ct)
    {
        var s = candidate.Snapshot; var reasons = new List<string>();
        var option = await db.SourcingOptions.Include(x => x.Item).Include(x => x.Supplier)
            .ThenInclude(x => x.PaymentProfile)
            .Include(x => x.ItemBaseUnit).Include(x => x.ShippingMode).Include(x => x.Incoterm)
            .SingleOrDefaultAsync(x => x.Id == s.SourcingOptionId, ct);
        var profile = await db.MaterialItemProfiles.SingleOrDefaultAsync(x => x.ItemId == candidate.Row.Recommendation.ItemId, ct);
        if (option is null || !option.IsActive || option.SupplierId != s.SupplierId || option.ItemId != candidate.Row.Recommendation.ItemId)
            reasons.Add("SOURCE_NOT_OPERATIONAL");
        else
        {
            var readiness = SourcingReadinessEvaluator.Evaluate(option, profile?.SupplyMethod, s.ItemBaseUnitId);
            if (readiness.State != SourcingReadiness.Ready) reasons.Add("READINESS_CHANGED");
            if (option.ItemBaseUnitId != s.ItemBaseUnitId || option.Item.BaseUnitId != s.ItemBaseUnitId) reasons.Add("BASE_UNIT_CHANGED");
            if (option.MinimumOrderQuantity != s.MinimumOrderQuantity) reasons.Add("MOQ_CHANGED");
            if (option.ShippingModeId != s.ShippingModeId) reasons.Add("SHIPPING_MODE_CHANGED");
            if (option.IncotermId != s.IncotermId) reasons.Add("INCOTERM_CHANGED");
            if (option.SupplierLeadTimeDays != s.SupplierLeadTimeDays || option.ForwarderToShipmentDays != s.ForwarderToShipmentDays ||
                option.ShippingPeriodDays != s.ShippingPeriodDays || option.DestinationCustomsDays != s.DestinationCustomsDays)
                reasons.Add("LEAD_TIME_CHANGED");
            var payment = option.Supplier.PaymentProfile;
            if (payment is null || payment.PaymentTermId != s.PaymentTermId ||
                payment.AdvancePercent != s.AdvancePercent ||
                payment.BeforeShipmentPercent != s.BeforeShipmentPercent ||
                payment.CreditPercent != s.CreditPercent || payment.CreditDays != s.CreditDays ||
                payment.CreditAnchor != s.CreditAnchor)
                reasons.Add("PAYMENT_TERMS_CHANGED");
        }
        if (!await db.Currencies.AsNoTracking().AnyAsync(x => x.Id == s.CurrencyId && x.IsActive, ct))
            reasons.Add("CURRENCY_INACTIVE");
        if (reasons.Count > 0) throw new DecisionReconfirmationException(reasons.Distinct().ToList());
    }

    private static string? ValidateDecisionEligibility(DecisionRow row)
    {
        if (row.Decision.DecisionType is not (ExpertDecisionTypes.Accept or ExpertDecisionTypes.Modify)) return "DECISION_NOT_CONVERTIBLE";
        if (row.Decision.DecisionVersion != row.Recommendation.DecisionVersion) return "DECISION_NOT_CURRENT";
        if (row.Generation.Status != OrderPlanningGenerationStatuses.Completed || !row.Generation.IsCurrent) return "OUTDATED_RUN";
        if (row.Recommendation.State == RecommendationStates.Blocked) return "BLOCKED";
        if (row.Decision.FinalSourceSnapshot is null || !row.Decision.FinalQty.HasValue || row.Decision.FinalQty <= 0 ||
            !row.Decision.FinalUnitPrice.HasValue || row.Decision.FinalUnitPrice < 0 || !row.Decision.FinalCurrencyId.HasValue)
            return "DECISION_SNAPSHOT_INCOMPLETE";
        return null;
    }

    private static async Task<List<DecisionRow>> LoadDecisions(HadronDbContext db, IEnumerable<Guid> ids, CancellationToken ct) =>
        await (from d in db.OrderRecommendationDecisions.AsNoTracking()
               join r in db.OrderRecommendations.AsNoTracking() on d.RecommendationId equals r.Id
               join g in db.OrderPlanningGenerations.AsNoTracking() on r.GenerationId equals g.Id
               where ids.Contains(d.Id)
               select new DecisionRow(d, r, g)).ToListAsync(ct);

    private static DecisionSnapshot Parse(string json) => JsonSerializer.Deserialize<DecisionSnapshot>(json,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw Validation("decision", "Snapshot تصمیم نامعتبر است.");
    private static string GroupKey(DecisionSnapshot s) => string.Join('|', s.SupplierId, s.CurrencyId,
        s.PaymentTermId, s.AdvancePercent, s.BeforeShipmentPercent, s.CreditPercent, s.CreditDays,
        s.CreditAnchor, s.ShippingModeId, s.IncotermId);
    private static ConversionGroupResult Failed(Guid id, string code) => new($"decision:{id}", false, null, null,
        [new(id, false, null, null, code, [])]);

    private static IQueryable<PurchaseReceipt> ReceiptQuery(HadronDbContext db) => db.PurchaseReceipts.AsNoTracking()
        .Include(x => x.PurchaseOrder).Include(x => x.Warehouse).Include(x => x.Lines).ThenInclude(x => x.Item)
        .Include(x => x.Lines).ThenInclude(x => x.Unit);
    private static PurchaseReceiptResponse ReceiptResponse(PurchaseReceipt x) => new(x.Id, x.ReceiptNumber,
        x.PurchaseOrderId, x.PurchaseOrder.PurchaseOrderNumber, x.WarehouseId, x.Warehouse.BusinessNumber,
        x.Warehouse.NameFa, x.Warehouse.NameEn, x.EffectiveDate, x.Status, x.Notes, x.Version, x.PostedAtUtc,
        x.ReversedAtUtc, x.ReversalReason, x.Lines.Select(l => new PurchaseReceiptLineResponse(l.Id,
            l.PurchaseOrderLineId, l.DeliveryScheduleId, new(l.Item.Id, l.Item.BusinessNumber, l.Item.NameFa,
                l.Item.NameEn, l.Item.ItemType, l.Item.IsActive), new(l.Unit.Id, l.Unit.Code, l.Unit.NameFa,
                l.Unit.NameEn, l.Unit.IsActive), l.Quantity, l.InventoryTransactionId,
            l.ReversalInventoryTransactionId)).ToList(), x.Lines.Any(l => !l.Item.IsActive)
                ? ["INACTIVE_ITEM_EXISTING_COMMITMENT"] : []);

    private async Task RefreshStatus(HadronDbContext db, PurchaseOrder order, CancellationToken ct)
    {
        var totals = await db.DeliverySchedules.Where(x => x.PurchaseOrderLine.PurchaseOrderId == order.Id)
            .Select(x => new
            {
                x.AllocatedQuantity,
                Received = x.ReceiptLines.Where(r => r.Receipt.Status == PurchaseReceiptStatuses.Posted).Sum(r => (decimal?)r.Quantity) ?? 0,
                Cancelled = x.Cancellations.Sum(c => (decimal?)c.Quantity) ?? 0
            }).ToListAsync(ct);
        var received = totals.Sum(x => x.Received); var cancelled = totals.Sum(x => x.Cancelled);
        var open = totals.Sum(x => x.AllocatedQuantity - x.Received - x.Cancelled);
        var actual = await CurrentMilestones(db, null).Where(x => x.DeliverySchedule.PurchaseOrderLine.PurchaseOrderId == order.Id)
            .Select(x => x.MilestoneCode).ToListAsync(ct);
        var status = open == 0 ? received > 0 ? PurchaseOrderStatuses.Received : PurchaseOrderStatuses.Cancelled
            : received > 0 ? PurchaseOrderStatuses.PartiallyReceived
            : actual.Contains(PurchaseMilestoneCodes.CustomsCleared) ? PurchaseOrderStatuses.Customs
            : actual.Contains(PurchaseMilestoneCodes.Shipped) ? PurchaseOrderStatuses.Shipped : PurchaseOrderStatuses.Ordered;
        if (status != order.Status) order.ApplyOperationalStatus(status); else order.TouchForPlanRevision();
    }

    private static IQueryable<DeliveryScheduleActualMilestone> CurrentMilestones(HadronDbContext db, Guid? scheduleId) =>
        db.DeliveryScheduleActualMilestones.Where(x => (!scheduleId.HasValue || x.DeliveryScheduleId == scheduleId) &&
            !db.DeliveryScheduleActualMilestones.Any(n => n.SupersedesMilestoneId == x.Id));
    private static void ValidateActualChronology(IReadOnlyDictionary<string, DateOnly> dates)
    {
        var sequence = new[] { PurchaseMilestoneCodes.SupplierReady, PurchaseMilestoneCodes.HandoverToForwarder,
            PurchaseMilestoneCodes.Shipped, PurchaseMilestoneCodes.ArrivedDestination,
            PurchaseMilestoneCodes.CustomsCleared, PurchaseMilestoneCodes.WarehouseReceipt };
        DateOnly? previous = null;
        foreach (var code in sequence) if (dates.TryGetValue(code, out var date))
        { if (previous.HasValue && date < previous.Value) throw Validation("actualDate", "ترتیب زمانی Milestoneها معتبر نیست."); previous = date; }
    }
    private void ValidateReceiptDate(PurchaseOrder order, DateOnly date)
    {
        if (!order.OrderedDate.HasValue || date < order.OrderedDate || date > TehranToday)
            throw Conflict("INVALID_RECEIPT_EFFECTIVE_DATE", "تاریخ رسید باید بین OrderedDate و امروز تهران باشد.");
    }
    private static async Task<decimal> ScheduleOpen(HadronDbContext db, Guid scheduleId, Guid? excludingDraftReceiptId, CancellationToken ct)
    {
        var row = await db.DeliverySchedules.Where(x => x.Id == scheduleId).Select(x => new
        {
            x.AllocatedQuantity,
            Received = x.ReceiptLines.Where(r => r.Receipt.Status == PurchaseReceiptStatuses.Posted &&
            (!excludingDraftReceiptId.HasValue || r.ReceiptId != excludingDraftReceiptId)).Sum(r => (decimal?)r.Quantity) ?? 0,
            Cancelled = x.Cancellations.Sum(c => (decimal?)c.Quantity) ?? 0
        }).SingleAsync(ct);
        return row.AllocatedQuantity - row.Received - row.Cancelled;
    }
    private static async Task<PurchaseOrder> LockOrder(HadronDbContext db, Guid id, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM procurement.purchase_orders WHERE \"Id\"={id} FOR UPDATE", ct);
        return await db.PurchaseOrders.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException();
    }
    private static async Task<PurchaseReceipt> LockReceipt(HadronDbContext db, Guid id, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM procurement.purchase_receipts WHERE \"Id\"={id} FOR UPDATE", ct);
        return await db.PurchaseReceipts.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException();
    }
    private static void EnsureOrdered(PurchaseOrder order)
    { if (order.Status is PurchaseOrderStatuses.Draft or PurchaseOrderStatuses.Approved) throw Conflict("PO_NOT_ORDERED", "PO هنوز تعهد خارجی نیست."); }
    private static void EnsureVersion(long actual, long expected)
    { if (expected < 1 || actual != expected) throw Conflict("STALE_VERSION", "نسخه ارسالی قدیمی است."); }
    private static decimal PurchaseQuantity(decimal value) =>
        decimal.Round(value, 6, MidpointRounding.AwayFromZero);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static async Task<string> Next(HadronDbContext db, string sequence, string prefix, int digits, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection(); if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);
        var transaction = db.Database.CurrentTransaction is null ? null : (NpgsqlTransaction)db.Database.CurrentTransaction.GetDbTransaction();
        await using var command = new NpgsqlCommand($"SELECT nextval('{sequence}')", connection, transaction);
        var value = (long)(await command.ExecuteScalarAsync(ct))!; return $"{prefix}-{value.ToString($"D{digits}")}";
    }
    private static async Task Save(HadronDbContext db, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw Conflict("STALE_VERSION", "نسخه ارسالی قدیمی است."); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw Conflict("PURCHASE_OPERATION_INTEGRITY_CONFLICT", ex.InnerException!.Message); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.CheckViolation or PostgresErrorCodes.RaiseException })
        { var message = ex.InnerException!.Message; throw Conflict(message.Split(':')[0], message); }
    }
    private async Task Emit(string type, Guid id, string action, object? value, string? reason, CancellationToken ct)
    {
        try
        {
            await auditSink.WriteAsync(new(type, id, action, timeProvider.GetUtcNow().UtcDateTime, Actor(),
            AuditEventContext.CorrelationId(accessor.HttpContext), null,
            new Dictionary<string, object?> { ["Value"] = value }, reason), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { logger.LogError(ex, "Post-commit purchase operation audit failed for {Id}", id); }
    }
    private static PurchaseSupplyValidationException Validation(string key, string message) =>
        new(new Dictionary<string, string[]> { [key] = [message] });
    private static PurchaseSupplyConflictException Conflict(string code, string message) => new(code, message);

    private sealed record DecisionRow(OrderRecommendationDecision Decision, OrderRecommendation Recommendation,
        OrderPlanningGeneration Generation);
    private sealed record ConversionCandidate(DecisionRow Row, DecisionSnapshot Snapshot, string GroupKey);
    private sealed record DecisionSnapshot(DateOnly EvaluationDate, Guid SourcingOptionId, string SourcingNumber,
        Guid SupplierId, string SupplierNumber, string SupplierNameFa, string? SupplierNameEn,
        string ReadinessState, Guid ItemBaseUnitId, decimal MinimumOrderQuantity, Guid? ShippingModeId,
        Guid? IncotermId, int? SupplierLeadTimeDays, int? ForwarderToShipmentDays, int? ShippingPeriodDays,
        int? DestinationCustomsDays, int TotalLeadTimeDays, Guid? PriceId, decimal UnitPrice, Guid CurrencyId,
        string CurrencyCode, Guid PaymentTermId, string PaymentTermCode, decimal AdvancePercent,
        decimal BeforeShipmentPercent, decimal CreditPercent, int? CreditDays, string? CreditAnchor);
}

public sealed class DecisionReconfirmationException(IReadOnlyList<string> reasons)
    : PurchaseSupplyConflictException("DECISION_RECONFIRMATION_REQUIRED", "تصمیم به‌علت تغییر Master Data نیازمند تأیید مجدد است.")
{
    public IReadOnlyList<string> Reasons { get; } = reasons;
}
