using System.Data;
using System.Security.Claims;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Bom.Domain;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Inventory.Application;
using Hadron.Erp.Api.Modules.Inventory.Domain;
using Hadron.Erp.Api.Modules.Planning.Application;
using Hadron.Erp.Api.Modules.Planning.Domain;
using Hadron.Erp.Api.Modules.Production.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Hadron.Erp.Api.Platform.Audit;
using Hadron.Erp.Api.Platform.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hadron.Erp.Api.Modules.Production.Application;

public sealed class ProductionService(
    IDbContextFactory<HadronDbContext> factory,
    IPlanningCalendar calendar,
    TimeProvider timeProvider,
    IAuditEventSink auditSink,
    IHttpContextAccessor accessor,
    ILogger<ProductionService> logger)
{
    private DateOnly TehranToday => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
        timeProvider.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran")).DateTime);

    public async Task<ProductionOrderListResponse> List(string? search, string? status,
        int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        if (!string.IsNullOrWhiteSpace(status) && !ProductionOrderStatuses.All.Contains(status))
            throw Validation("status", "وضعیت دستور تولید معتبر نیست.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.ProductionOrders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.BusinessNumber.Contains(term) ||
                x.Product.BusinessNumber.Contains(term) || x.Product.NameFa.Contains(term) ||
                (x.Product.NameEn != null && x.Product.NameEn.Contains(term)));
        }
        var total = await query.CountAsync(ct);
        var orders = await query.Include(x => x.Product).ThenInclude(x => x.BaseUnit)
            .Include(x => x.DestinationWarehouse)
            .Include(x => x.ForecastSeries).ThenInclude(x => x.BusinessPeriod)
            .Include(x => x.ForecastRevision).Include(x => x.BomRevision)
            .OrderByDescending(x => x.CreatedAtUtc).Skip((page - 1) * pageSize)
            .Take(pageSize).ToListAsync(ct);
        var rows = orders.Select(order =>
        {
            var period = order.ForecastSeries.BusinessPeriod;
            return new ProductionOrderResponse(order.Id, order.BusinessNumber, order.Status,
                ItemRef(order.Product), order.PlannedQuantity, order.ReportedGoodQuantity,
                order.ReportedRejectedQuantity, order.PlannedStartDate, order.PlannedEndDate,
                WarehouseRef(order.DestinationWarehouse), new(order.ForecastSeriesId,
                    order.ForecastRevisionId, order.ForecastRevision.RevisionNumber,
                    order.ForecastRevision.ForecastQty,
                    $"{period.JalaliYear:D4}{period.JalaliMonth:D2}", period.StartDate,
                    period.EndDate), order.BomRevisionId, order.BomRevision?.RevisionCode,
                order.Notes, order.ActorIdentifier, order.Version, order.CreatedAtUtc,
                order.UpdatedAtUtc, [], []);
        }).ToList();
        return new(rows, page, pageSize, total);
    }

    public async Task<ProductionOrderResponse?> Get(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await GetWithDb(db, id, ct);
    }

    public async Task<ProductionOrderResponse> Create(CreateProductionOrderRequest request,
        CancellationToken ct)
    {
        ValidateOrder(request.PlannedQuantity, request.PlannedStartDate, request.PlannedEndDate,
            request.DestinationWarehouseId);
        await using var db = await factory.CreateDbContextAsync(ct);
        var product = await Product(db, request.ProductId, ct);
        var warehouse = await FinishedGoodsWarehouse(db, request.DestinationWarehouseId, ct);
        var forecast = await Forecast(db, product.Id, request.PlannedStartDate, ct);
        var row = new ProductionOrder(await NextOrderNumber(db, ct), product.Id,
            product.BaseUnitId, request.PlannedQuantity, request.PlannedStartDate,
            request.PlannedEndDate, warehouse.Id, forecast.Series.Id, forecast.Revision.Id,
            Clean(request.Notes), Actor());
        db.ProductionOrders.Add(row);
        db.ProductionOrderLifecycleEvents.Add(new(row.Id, "NONE", ProductionOrderStatuses.Draft,
            null, Actor()));
        await Save(db, ct);
        var response = (await GetWithDb(db, row.Id, ct))!;
        await Emit(row.Id, "CREATE", response, null, ct);
        return response;
    }

    public async Task<ProductionOrderResponse> Update(Guid id, UpdateProductionOrderRequest request,
        CancellationToken ct)
    {
        ValidateOrder(request.PlannedQuantity, request.PlannedStartDate, request.PlannedEndDate,
            request.DestinationWarehouseId);
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockOrder(db, id, ct);
        var row = await db.ProductionOrders.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException();
        EnsureVersion(row.Version, request.ExpectedVersion);
        if (row.Status != ProductionOrderStatuses.Draft)
            throw new ProductionConflictException("PRODUCTION_ORDER_IMMUTABLE",
                "فقط دستور تولید پیش‌نویس قابل ویرایش است.");
        await FinishedGoodsWarehouse(db, request.DestinationWarehouseId, ct);
        var forecast = await Forecast(db, row.ProductId, request.PlannedStartDate, ct);
        row.EditDraft(request.PlannedQuantity, request.PlannedStartDate, request.PlannedEndDate,
            request.DestinationWarehouseId, forecast.Series.Id, forecast.Revision.Id,
            Clean(request.Notes));
        await Save(db, ct);
        await tx.CommitAsync(ct);
        var response = (await Get(id, ct))!;
        await Emit(id, "EDIT_DRAFT", response, null, ct);
        return response;
    }

    public Task<ProductionOrderResponse> Approve(Guid id, ProductionLifecycleRequest request,
        CancellationToken ct) => Lifecycle(id, request, ProductionOrderStatuses.Approved, ct);

    public async Task<ProductionOrderResponse> Release(Guid id, ProductionLifecycleRequest request,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockOrder(db, id, ct);
        var row = await db.ProductionOrders.Include(x => x.Product)
            .SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException();
        EnsureVersion(row.Version, request.ExpectedVersion);
        if (row.Status != ProductionOrderStatuses.Approved)
            throw new ProductionConflictException("PRODUCTION_ORDER_NOT_APPROVED",
                "فقط دستور تولید تأییدشده قابل آزادسازی است.");
        if (row.Product.BaseUnitId != row.ProductBaseUnitId)
            throw new ProductionConflictException("BASE_UNIT_CHANGED",
                "واحد پایه محصول پس از ایجاد دستور تولید تغییر کرده است.");
        var bom = await db.BomRevisions.Include(x => x.Lines).ThenInclude(x => x.ChildItem)
            .Where(x => x.ParentItemId == row.ProductId && x.Status == BomStatuses.Approved)
            .OrderByDescending(x => x.ApprovedAtUtc).ThenByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct) ?? throw new ProductionConflictException("APPROVED_BOM_REQUIRED",
                "برای آزادسازی دستور تولید، BOM تأییدشده الزامی است.");
        if (bom.ParentBaseUnitId != row.ProductBaseUnitId)
            throw new ProductionConflictException("BASE_UNIT_CHANGED",
                "واحد پایه محصول با snapshot BOM سازگار نیست.");
        if (bom.Lines.Count == 0)
            throw new ProductionConflictException("EMPTY_BOM_NOT_ALLOWED", "BOM تأییدشده فاقد قلم است.");
        foreach (var line in bom.Lines.OrderBy(x => x.SortOrder))
        {
            if (line.ChildItem.BaseUnitId != line.ChildBaseUnitId)
                throw new ProductionConflictException("BASE_UNIT_CHANGED",
                    $"واحد پایه قلم {line.ChildItem.BusinessNumber} با BOM سازگار نیست.");
            var required = BaseQuantity(row.PlannedQuantity * line.UsageQty / bom.BaseOutputQty);
            db.ProductionOrderMaterials.Add(new(row.Id, line.Id, line.ChildItemId,
                line.ChildBaseUnitId, required, line.UsageQty, bom.BaseOutputQty, line.SortOrder));
        }
        var from = row.Status;
        row.Release(bom.Id);
        db.ProductionOrderLifecycleEvents.Add(new(row.Id, from, row.Status,
            Clean(request.Reason), Actor()));
        await Save(db, ct);
        await tx.CommitAsync(ct);
        var response = (await Get(id, ct))!;
        await Emit(id, "RELEASE", response, request.Reason, ct);
        return response;
    }

    public async Task<ProductionOrderResponse> Stage(Guid orderId, Guid materialId,
        StageProductionMaterialRequest request, CancellationToken ct)
    {
        if (request.Quantity <= 0) throw Validation("quantity", "مقدار آماده‌سازی باید مثبت باشد.");
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockOrder(db, orderId, ct);
        var order = await db.ProductionOrders.SingleOrDefaultAsync(x => x.Id == orderId, ct)
            ?? throw new KeyNotFoundException();
        EnsureVersion(order.Version, request.ExpectedOrderVersion);
        if (order.Status is not (ProductionOrderStatuses.Released or ProductionOrderStatuses.InProgress))
            throw new ProductionConflictException("PRODUCTION_ORDER_NOT_RELEASED",
                "مواد فقط برای دستور تولید آزادشده آماده می‌شوند.");
        var material = await db.ProductionOrderMaterials.SingleOrDefaultAsync(x =>
            x.Id == materialId && x.ProductionOrderId == orderId, ct) ?? throw new KeyNotFoundException();
        var source = await ActiveWarehouse(db, request.SourceWarehouseId, ct);
        var destination = await ActiveWarehouse(db, request.StagingWarehouseId, ct);
        if (destination.WarehouseType != WarehouseTypes.ProductionStaging)
            throw Validation("stagingWarehouseId", "انبار مقصد باید از نوع انبار موقت تولید باشد.");
        if (source.Id == destination.Id) throw Validation("sourceWarehouseId", "انبار مبدأ و مقصد متفاوت باشند.");
        var staged = await EffectiveStaged(db, materialId, ct);
        var pending = material.RequiredQuantity - staged;
        if (request.Quantity > pending)
            throw new ProductionConflictException("MATERIAL_STAGE_OVER_REQUIRED",
                "مقدار آماده‌سازی از مانده نیاز دستور بیشتر است.", new { PendingQuantity = pending });
        var transfer = new InventoryTransfer(await NextTransferNumber(db, ct), material.ItemId,
            material.UnitId, source.Id, destination.Id, BaseQuantity(request.Quantity),
            request.Quantity, material.UnitId, 1m, Clean(request.Reason), Actor());
        db.InventoryTransfers.Add(transfer);
        db.ProductionMaterialStagings.Add(new(material.Id, transfer.Id, transfer.RequestedQuantity));
        order.RegisterStaging();
        await Save(db, ct);
        await tx.CommitAsync(ct);
        var response = (await Get(orderId, ct))!;
        await Emit(orderId, "STAGE_MATERIAL", response, request.Reason, ct);
        return response;
    }

    public async Task<ProductionOrderResponse> PostReport(Guid orderId,
        PostProductionReportRequest request, CancellationToken ct)
    {
        ValidateReport(request);
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockOrder(db, orderId, ct);
        var order = await db.ProductionOrders.Include(x => x.Product).ThenInclude(x => x.BaseUnit)
            .Include(x => x.Materials).ThenInclude(x => x.Item)
            .SingleOrDefaultAsync(x => x.Id == orderId, ct) ?? throw new KeyNotFoundException();
        EnsureVersion(order.Version, request.ExpectedVersion);
        if (order.Status is not (ProductionOrderStatuses.Released or ProductionOrderStatuses.InProgress))
            throw new ProductionConflictException("PRODUCTION_ORDER_NOT_REPORTABLE",
                "گزارش تولید فقط برای دستور آزادشده یا در حال اجرا مجاز است.");
        if (request.ProductionDate > TehranToday)
            throw new ProductionConflictException("FUTURE_PRODUCTION_ACTUAL_NOT_ALLOWED",
                "ثبت تولید برای تاریخ آینده تهران مجاز نیست.");
        if (order.ReportedGoodQuantity + order.ReportedRejectedQuantity +
            request.GoodQuantity + request.RejectedQuantity > order.PlannedQuantity)
            throw new ProductionConflictException("PRODUCTION_OVER_PLAN_NOT_ALLOWED",
                "مجموع تولید سالم و مردود از مقدار برنامه دستور بیشتر است.");
        var staging = await ActiveWarehouse(db, request.StagingWarehouseId, ct);
        if (staging.WarehouseType != WarehouseTypes.ProductionStaging)
            throw Validation("stagingWarehouseId", "انبار مصرف باید انبار موقت تولید باشد.");
        var destination = await ActiveWarehouse(db, order.DestinationWarehouseId, ct);
        if (destination.WarehouseType == WarehouseTypes.ProductionStaging)
            throw new ProductionConflictException("FINISHED_GOODS_WAREHOUSE_REQUIRED",
                "محصول نهایی نباید وارد انبار موقت تولید شود.");
        var overrideByMaterial = (request.Materials ?? []).ToDictionary(x => x.ProductionOrderMaterialId);
        var totalOutput = request.GoodQuantity + request.RejectedQuantity;
        var consumptions = order.Materials.OrderBy(x => x.SortOrder).Select(material =>
        {
            var standard = BaseQuantity(material.RequiredQuantity * totalOutput / order.PlannedQuantity);
            overrideByMaterial.TryGetValue(material.Id, out var value);
            var actual = BaseQuantity(value?.ActualQuantity ?? standard);
            var reason = Clean(value?.VarianceReason);
            if (actual != standard && reason is null)
                throw Validation("materials", $"دلیل انحراف مصرف {material.Item.BusinessNumber} الزامی است.");
            return new { Material = material, Standard = standard, Actual = actual, Reason = reason };
        }).ToList();

        var consumedByMaterial = await db.ProductionReportMaterials.AsNoTracking()
            .Where(x => x.ProductionReport.ProductionOrderId == orderId)
            .GroupBy(x => x.ProductionOrderMaterialId)
            .Select(x => new { Id = x.Key, Quantity = x.Sum(v => v.ActualQuantity) })
            .ToDictionaryAsync(x => x.Id, x => x.Quantity, ct);
        foreach (var consumption in consumptions)
        {
            var staged = await EffectiveStaged(db, consumption.Material.Id, ct);
            var available = staged - consumedByMaterial.GetValueOrDefault(consumption.Material.Id);
            if (consumption.Actual > available)
                throw new ProductionConflictException("PRODUCTION_MATERIAL_NOT_STAGED",
                    $"موجودی آماده‌شده {consumption.Material.Item.BusinessNumber} برای این دستور کافی نیست.",
                    new { AvailableQuantity = available, RequiredQuantity = consumption.Actual });
        }

        await LockInventoryResources(db,
            consumptions.Select(x => x.Material.ItemId).Append(order.ProductId),
            [staging.Id, destination.Id], ct);

        var reportId = Guid.NewGuid();
        var reportMaterials = new List<ProductionReportMaterial>();
        foreach (var consumption in consumptions)
        {
            var transaction = new InventoryTransaction(InventoryTransactionTypes.Issue,
                consumption.Material.ItemId, staging.Id, null, consumption.Actual,
                consumption.Material.UnitId, consumption.Actual, consumption.Material.UnitId, 1m,
                request.ProductionDate, consumption.Reason, null,
                InventorySourceDocumentTypes.ProductionIssue, reportId, Actor());
            db.InventoryTransactions.Add(transaction);
            reportMaterials.Add(new ProductionReportMaterial(reportId, consumption.Material.Id,
                consumption.Standard, consumption.Actual, consumption.Reason, transaction.Id));
        }
        var finished = new InventoryTransaction(InventoryTransactionTypes.Receipt, order.ProductId,
            destination.Id, null, request.GoodQuantity, order.ProductBaseUnitId,
            request.GoodQuantity, order.ProductBaseUnitId, 1m, request.ProductionDate,
            Clean(request.Notes), null, InventorySourceDocumentTypes.ProductionReceipt,
            reportId, Actor());
        db.InventoryTransactions.Add(finished);

        var (actual, revision) = await AppendDailyActual(db, order, request.ProductionDate,
            request.GoodQuantity, ct);
        var report = new ProductionReport(reportId, order.Id, request.ProductionDate,
            request.GoodQuantity, request.RejectedQuantity, destination.Id, finished.Id,
            actual.Id, revision.Id, Clean(request.Notes), Actor());
        db.ProductionReports.Add(report);
        db.ProductionReportMaterials.AddRange(reportMaterials);
        var from = order.Status;
        order.RecordProduction(request.GoodQuantity, request.RejectedQuantity);
        if (from != order.Status)
            db.ProductionOrderLifecycleEvents.Add(new(order.Id, from, order.Status,
                Clean(request.Notes), Actor()));
        await Save(db, ct);
        await tx.CommitAsync(ct);
        var response = (await Get(orderId, ct))!;
        await Emit(orderId, "POST_REPORT", response, request.Notes, ct);
        return response;
    }

    private async Task<ProductionOrderResponse> Lifecycle(Guid id, ProductionLifecycleRequest request,
        string target, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockOrder(db, id, ct);
        var row = await db.ProductionOrders.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException();
        EnsureVersion(row.Version, request.ExpectedVersion);
        var from = row.Status;
        try
        {
            if (target == ProductionOrderStatuses.Approved) row.Approve();
            else throw new InvalidOperationException();
        }
        catch (InvalidOperationException)
        {
            throw new ProductionConflictException("INVALID_PRODUCTION_LIFECYCLE",
                "گذار وضعیت دستور تولید معتبر نیست.");
        }
        db.ProductionOrderLifecycleEvents.Add(new(row.Id, from, row.Status,
            Clean(request.Reason), Actor()));
        await Save(db, ct);
        await tx.CommitAsync(ct);
        var response = (await Get(id, ct))!;
        await Emit(id, target, response, request.Reason, ct);
        return response;
    }

    private async Task<(DailyActual Actual, DailyActualRevision Revision)> AppendDailyActual(
        HadronDbContext db, ProductionOrder order, DateOnly date, decimal goodQuantity,
        CancellationToken ct)
    {
        var jalali = calendar.GetJalaliDate(date);
        var period = await db.BusinessPeriods.SingleOrDefaultAsync(x =>
            x.JalaliYear == jalali.JalaliYear && x.JalaliMonth == jalali.JalaliMonth, ct);
        if (period is null)
        {
            var bounds = calendar.GetPeriodBounds(jalali.JalaliYear, jalali.JalaliMonth);
            period = new BusinessPeriod(jalali.JalaliYear, jalali.JalaliMonth,
                bounds.StartDate, bounds.EndDate);
            db.BusinessPeriods.Add(period);
            await db.SaveChangesAsync(ct);
        }
        if (period.Status != BusinessPeriodStatuses.Open)
            throw new ProductionConflictException("BUSINESS_PERIOD_CLOSED",
                "دوره تولید واقعی بسته است.");
        var actual = await db.DailyActuals.Include(x => x.CurrentRevision)
            .Include(x => x.Revisions).SingleOrDefaultAsync(x => x.ProductId == order.ProductId &&
                x.ProductionDate == date, ct);
        if (actual is null)
        {
            actual = new DailyActual(order.ProductId, period.Id, date, order.ProductBaseUnitId);
            db.DailyActuals.Add(actual);
            await db.SaveChangesAsync(ct);
            var first = new DailyActualRevision(actual.Id, 1, goodQuantity,
                "PRODUCTION_ORDER_REPORT", Actor());
            db.DailyActualRevisions.Add(first);
            actual.SetCurrent(first);
            return (actual, first);
        }
        if (actual.UnitId != order.ProductBaseUnitId)
            throw new ProductionConflictException("BASE_UNIT_CHANGED",
                "واحد تولید واقعی با snapshot دستور تولید سازگار نیست.");
        var next = actual.Revisions.Max(x => x.RevisionNumber) + 1;
        var revision = new DailyActualRevision(actual.Id, next,
            actual.CurrentRevision!.ActualQty + goodQuantity,
            $"PRODUCTION_ORDER:{order.BusinessNumber}", Actor());
        db.DailyActualRevisions.Add(revision);
        actual.Advance(revision);
        return (actual, revision);
    }

    private async Task<ProductionOrderResponse?> GetWithDb(HadronDbContext db, Guid id,
        CancellationToken ct)
    {
        var order = await db.ProductionOrders.AsNoTracking()
            .Include(x => x.Product).ThenInclude(x => x.BaseUnit)
            .Include(x => x.ProductBaseUnit).Include(x => x.DestinationWarehouse)
            .Include(x => x.ForecastSeries).ThenInclude(x => x.BusinessPeriod)
            .Include(x => x.ForecastRevision).Include(x => x.BomRevision)
            .Include(x => x.Materials).ThenInclude(x => x.Item).ThenInclude(x => x.BaseUnit)
            .Include(x => x.Materials).ThenInclude(x => x.Unit)
            .Include(x => x.Materials).ThenInclude(x => x.Stagings).ThenInclude(x => x.InventoryTransfer)
            .Include(x => x.Reports).ThenInclude(x => x.Materials)
            .AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (order is null) return null;
        var stagingTransferIds = order.Materials.SelectMany(x => x.Stagings)
            .Select(x => x.InventoryTransferId).ToArray();
        var receipts = stagingTransferIds.Length == 0 ? [] : await db.InventoryTransferReceipts.AsNoTracking()
            .Where(x => stagingTransferIds.Contains(x.TransferId) && x.InventoryTransactionId != null)
            .Select(x => new { x.TransferId, x.Quantity, TransactionId = x.InventoryTransactionId!.Value })
            .ToListAsync(ct);
        var receiptIds = receipts.Select(x => x.TransactionId).ToArray();
        var reversed = receiptIds.Length == 0 ? new HashSet<Guid>() : (await db.InventoryTransactions
            .AsNoTracking().Where(x => x.OriginalTransactionId.HasValue &&
                receiptIds.Contains(x.OriginalTransactionId.Value))
            .Select(x => x.OriginalTransactionId!.Value).ToListAsync(ct)).ToHashSet();
        var consumed = await db.ProductionReportMaterials.AsNoTracking()
            .Where(x => x.ProductionReport.ProductionOrderId == id)
            .GroupBy(x => x.ProductionOrderMaterialId)
            .Select(x => new { Id = x.Key, Qty = x.Sum(v => v.ActualQuantity) }).ToDictionaryAsync(x => x.Id, x => x.Qty, ct);
        var materialRows = order.Materials.OrderBy(x => x.SortOrder).Select(material =>
        {
            var stageRows = material.Stagings.Select(link =>
            {
                var receivedQty = receipts.Where(x => x.TransferId == link.InventoryTransferId &&
                    !reversed.Contains(x.TransactionId)).Sum(x => x.Quantity);
                return new ProductionMaterialStagingRef(link.Id, link.InventoryTransferId,
                    link.InventoryTransfer.BusinessNumber, link.InventoryTransfer.Status,
                    link.BaseQuantity, receivedQty);
            }).ToList();
            var staged = stageRows.Sum(x => x.EffectiveReceivedQuantity);
            return new ProductionOrderMaterialResponse(material.Id, material.BomLineId,
                ItemRef(material.Item), UnitRef(material.Unit), material.RequiredQuantity,
                staged, consumed.GetValueOrDefault(material.Id),
                Math.Max(material.RequiredQuantity - staged, 0), material.UsageQty,
                material.BaseOutputQty, material.SortOrder, stageRows);
        }).ToList();
        var reportRows = order.Reports.OrderByDescending(x => x.ProductionDate).Select(report =>
            new ProductionReportResponse(report.Id, report.ProductionDate, report.GoodQuantity,
                report.RejectedQuantity, report.DestinationWarehouseId,
                report.FinishedGoodsTransactionId, report.DailyActualId,
                report.DailyActualRevisionId, report.Notes, report.ActorIdentifier,
                report.CreatedAtUtc, report.Materials.Select(x =>
                    new ProductionReportMaterialResponse(x.Id, x.ProductionOrderMaterialId,
                        x.StandardQuantity, x.ActualQuantity, x.VarianceReason,
                        x.InventoryTransactionId)).ToList())).ToList();
        var period = order.ForecastSeries.BusinessPeriod;
        return new(order.Id, order.BusinessNumber, order.Status, ItemRef(order.Product),
            order.PlannedQuantity, order.ReportedGoodQuantity, order.ReportedRejectedQuantity,
            order.PlannedStartDate, order.PlannedEndDate, WarehouseRef(order.DestinationWarehouse),
            new(order.ForecastSeriesId, order.ForecastRevisionId,
                order.ForecastRevision.RevisionNumber, order.ForecastRevision.ForecastQty,
                $"{period.JalaliYear:D4}{period.JalaliMonth:D2}", period.StartDate, period.EndDate),
            order.BomRevisionId, order.BomRevision?.RevisionCode, order.Notes,
            order.ActorIdentifier, order.Version, order.CreatedAtUtc, order.UpdatedAtUtc,
            materialRows, reportRows);
    }

    private static ProductionItemRef ItemRef(Item item) => new(item.Id, item.BusinessNumber,
        item.ItemType, item.NameFa, item.NameEn, item.IsActive, UnitRef(item.BaseUnit));
    private static InventoryUnitRef UnitRef(Unit unit) => new(unit.Id, unit.Code, unit.NameFa,
        unit.NameEn, unit.Symbol, unit.IsActive);
    private static WarehouseRef WarehouseRef(Warehouse warehouse) => new(warehouse.Id,
        warehouse.BusinessNumber, warehouse.NameFa, warehouse.NameEn, warehouse.WarehouseType,
        warehouse.IncludeInMrpAvailable, warehouse.IsActive, warehouse.Version);

    private static async Task<Item> Product(HadronDbContext db, Guid id, CancellationToken ct)
    {
        var item = await db.Items.Include(x => x.BaseUnit).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException();
        if (item.ItemType != ItemTypes.Product || !item.IsActive || !item.BaseUnit.IsActive)
            throw new ProductionConflictException("ACTIVE_PRODUCT_REQUIRED",
                "دستور تولید فقط برای محصول فعال با واحد پایه فعال ساخته می‌شود.");
        return item;
    }

    private static async Task<Warehouse> FinishedGoodsWarehouse(HadronDbContext db, Guid id,
        CancellationToken ct)
    {
        var warehouse = await ActiveWarehouse(db, id, ct);
        if (warehouse.WarehouseType == WarehouseTypes.ProductionStaging)
            throw new ProductionConflictException("FINISHED_GOODS_WAREHOUSE_REQUIRED",
                "مقصد محصول نهایی باید انبار عادی یا محصول نهایی باشد.");
        return warehouse;
    }

    private static async Task<Warehouse> ActiveWarehouse(HadronDbContext db, Guid id,
        CancellationToken ct)
    {
        var row = await db.Warehouses.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException();
        if (!row.IsActive) throw new ProductionConflictException("INACTIVE_WAREHOUSE",
            "انبار انتخاب‌شده غیرفعال است.");
        return row;
    }

    private static async Task<(ForecastSeries Series, ForecastRevision Revision)> Forecast(
        HadronDbContext db, Guid productId, DateOnly date, CancellationToken ct)
    {
        var series = await db.ForecastSeries.Include(x => x.BusinessPeriod)
            .Include(x => x.CurrentRevision).SingleOrDefaultAsync(x => x.ProductId == productId &&
                x.BusinessPeriod.StartDate <= date && x.BusinessPeriod.EndDate >= date, ct)
            ?? throw new ProductionConflictException("OFFICIAL_FORECAST_REQUIRED",
                "برای تاریخ شروع دستور تولید، پیش‌بینی رسمی محصول وجود ندارد.");
        if (series.CurrentRevision is null) throw new ProductionConflictException("OFFICIAL_FORECAST_REQUIRED",
            "پیش‌بینی رسمی جاری محصول وجود ندارد.");
        return (series, series.CurrentRevision);
    }

    private static async Task<decimal> EffectiveStaged(HadronDbContext db, Guid materialId,
        CancellationToken ct)
    {
        var transferIds = await db.ProductionMaterialStagings.AsNoTracking()
            .Where(x => x.ProductionOrderMaterialId == materialId)
            .Select(x => x.InventoryTransferId).ToArrayAsync(ct);
        if (transferIds.Length == 0) return 0;
        return await db.InventoryTransferReceipts.AsNoTracking().Where(x =>
            transferIds.Contains(x.TransferId) && x.InventoryTransactionId.HasValue &&
            !db.InventoryTransactions.Any(reverse => reverse.OriginalTransactionId == x.InventoryTransactionId))
            .SumAsync(x => x.Quantity, ct);
    }

    private static async Task LockInventoryResources(HadronDbContext db, IEnumerable<Guid> itemIds,
        IEnumerable<Guid> warehouseIds, CancellationToken ct)
    {
        foreach (var itemId in itemIds.Distinct().OrderBy(x => x))
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT inventory.lock_item_inventory({itemId})", ct);
        foreach (var warehouseId in warehouseIds.Distinct().OrderBy(x => x))
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT inventory.lock_warehouse_inventory({warehouseId})", ct);
    }

    private static Task LockOrder(HadronDbContext db, Guid id, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM production.orders WHERE \"Id\" = {id} FOR UPDATE", ct);

    private static async Task<string> NextOrderNumber(HadronDbContext db, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT nextval('production.production_order_number_seq')", connection);
        return $"MO-{(long)(await command.ExecuteScalarAsync(ct))!:D6}";
    }

    private static async Task<string> NextTransferNumber(HadronDbContext db, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT nextval('inventory.inventory_transfer_number_seq')", connection);
        return $"ITR-{(long)(await command.ExecuteScalarAsync(ct))!:D6}";
    }

    private static void ValidateOrder(decimal qty, DateOnly start, DateOnly end, Guid warehouseId)
    {
        if (qty <= 0 || Scale(qty) > 6) throw Validation("plannedQuantity", "مقدار برنامه باید مثبت و حداکثر ۶ رقم اعشار باشد.");
        if (start > end) throw Validation("plannedEndDate", "پایان برنامه نمی‌تواند قبل از شروع باشد.");
        if (warehouseId == Guid.Empty) throw Validation("destinationWarehouseId", "انبار محصول نهایی الزامی است.");
    }

    private void ValidateReport(PostProductionReportRequest request)
    {
        if (request.GoodQuantity <= 0 || request.RejectedQuantity < 0 ||
            Scale(request.GoodQuantity) > 6 || Scale(request.RejectedQuantity) > 6)
            throw Validation("goodQuantity", "تولید سالم باید مثبت و مقدار مردود نامنفی باشد.");
        if (request.ProductionDate > TehranToday)
            throw new ProductionConflictException("FUTURE_PRODUCTION_ACTUAL_NOT_ALLOWED",
                "ثبت تولید برای تاریخ آینده تهران مجاز نیست.");
    }

    private static decimal BaseQuantity(decimal value)
    {
        var rounded = decimal.Round(value, 6, MidpointRounding.AwayFromZero);
        if (rounded <= 0 || rounded > 999999999999.999999m)
            throw Validation("quantity", "مقدار پایه خارج از دامنه numeric(18,6) است.");
        return rounded;
    }

    private static int Scale(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0x7F;
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            throw Validation("page", "صفحه باید مثبت و اندازه صفحه بین ۱ تا ۱۰۰ باشد.");
    }
    private static void EnsureVersion(long actual, long expected)
    {
        if (actual != expected) throw new ProductionConflictException("STALE_VERSION",
            "نسخه ارسالی قدیمی است.");
    }
    private static ProductionValidationException Validation(string key, string message) =>
        ProductionValidationException.Single(key, message);
    private string? Actor() => AuditEventContext.ActorId(accessor.HttpContext);

    private static async Task Save(HadronDbContext db, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        { throw new ProductionConflictException("STALE_VERSION", "نسخه ارسالی قدیمی است."); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException postgres)
        {
            throw new ProductionConflictException(postgres.ConstraintName ?? "PRODUCTION_INTEGRITY_CONFLICT",
                postgres.MessageText);
        }
    }

    private async Task Emit(Guid id, string action, ProductionOrderResponse value,
        string? reason, CancellationToken ct)
    {
        try
        {
            await auditSink.WriteAsync(new("ProductionOrder", id, action, DateTime.UtcNow,
                Actor(), AuditEventContext.CorrelationId(accessor.HttpContext), null,
                new Dictionary<string, object?>
                {
                    ["Code"] = value.Code,
                    ["Status"] = value.Status,
                    ["ProductId"] = value.Product.Id,
                    ["PlannedQuantity"] = value.PlannedQuantity,
                    ["ReportedGoodQuantity"] = value.ReportedGoodQuantity,
                    ["Version"] = value.Version
                }, Clean(reason)), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { logger.LogError(ex, "Production audit sink failed after commit."); }
    }
}
