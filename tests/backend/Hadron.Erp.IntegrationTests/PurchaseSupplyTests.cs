using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Bom.Domain;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Mrp.Application;
using Hadron.Erp.Api.Modules.Mrp.Domain;
using Hadron.Erp.Api.Modules.Inventory.Domain;
using Hadron.Erp.Api.Modules.OrderPlanning.Domain;
using Hadron.Erp.Api.Modules.Planning.Application;
using Hadron.Erp.Api.Modules.Planning.Domain;
using Hadron.Erp.Api.Modules.Products.Domain;
using Hadron.Erp.Api.Modules.PurchaseSupply.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Hadron.Erp.Api.Modules.Sourcing.Domain;
using Hadron.Erp.Api.Modules.Suppliers.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Hadron.Erp.IntegrationTests;

[Collection("ReferenceData")]
public sealed class PurchaseSupplyTests : IClassFixture<FoundationApiFactory>, IAsyncLifetime
{
    private readonly FoundationApiFactory _factory;
    private readonly HttpClient _client;
    private Guid _supplier;
    private Guid _currency;
    private Guid _unit;
    private Guid _shipping;
    private Guid _incoterm;
    private Guid _option;
    private Guid _price;
    private Guid _item;
    private readonly List<Guid> _createdItems = [];
    private readonly List<Guid> _createdOptions = [];
    private readonly List<Guid> _createdSuppliers = [];
    private readonly List<Guid> _createdRefs = [];
    private readonly List<Guid> _createdUnits = [];
    private readonly List<Guid> _createdCurrencies = [];

    public PurchaseSupplyTests(FoundationApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await using var db = await Db();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE procurement.purchase_orders RESTART IDENTITY CASCADE");
        _unit = (await AddUnit(db, "PO-U")).Id;
        _currency = (await AddCurrency(db, "EUR")).Id;
        _shipping = (await AddReference(db, "SHIPPING_MODE", "PO-SEA")).Id;
        _incoterm = (await AddReference(db, "INCOTERM", "PO-FOB")).Id;
        var payment = await AddReference(db, "PAYMENT_TERM", "PO-30");
        var country = await AddReference(db, "COUNTRY", "PO-IR");
        var supplier = new Supplier($"SUP-{Random.Shared.Next(1000, 9999)}", "تأمین‌کننده سفارش", "تأمین‌کننده سفارش",
            country.Id, null, null, null, null);
        supplier.PaymentProfile.Set(payment.Id, 20, 30, 50, 60, "WAREHOUSE_RECEIPT");
        supplier.ChangeState(true);
        db.Suppliers.Add(supplier);
        _supplier = supplier.Id;
        _createdSuppliers.Add(supplier.Id);
        var tuple = await AddSourcing(db, supplier.Id, _currency, _unit, _shipping, _incoterm, "PO-PART");
        _item = tuple.Item;
        _option = tuple.Option;
        _price = tuple.Price;
        await db.SaveChangesAsync();
        _factory.AuditSink.Clear();
        _factory.PurchaseReceiptFaultInjector.Reset();
    }

    public async Task DisposeAsync()
    {
        _factory.PurchaseReceiptFaultInjector.Reset();
        await using var db = await Db();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE procurement.purchase_orders RESTART IDENTITY CASCADE");
        await db.Database.ExecuteSqlRawAsync("TRUNCATE inventory.transactions RESTART IDENTITY CASCADE");
        await db.SourcingPrices.Where(x => _createdOptions.Contains(x.SourcingOptionId)).ExecuteDeleteAsync();
        await db.SourcingOptions.Where(x => _createdOptions.Contains(x.Id)).ExecuteDeleteAsync();
        await db.SupplierPaymentProfiles.Where(x => _createdSuppliers.Contains(x.SupplierId)).ExecuteDeleteAsync();
        await db.Suppliers.Where(x => _createdSuppliers.Contains(x.Id)).ExecuteDeleteAsync();
        await db.MaterialItemProfiles.Where(x => _createdItems.Contains(x.ItemId)).ExecuteDeleteAsync();
        await db.Items.Where(x => _createdItems.Contains(x.Id)).ExecuteDeleteAsync();
        await db.ReferenceValues.Where(x => _createdRefs.Contains(x.Id)).ExecuteDeleteAsync();
        await db.Units.Where(x => _createdUnits.Contains(x.Id)).ExecuteDeleteAsync();
        await db.Currencies.Where(x => _createdCurrencies.Contains(x.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task GD05_partial_receipt_cancellation_reversal_and_inventory_are_one_traceable_truth()
    {
        Guid gdOption; Guid gdPrice;
        await using (var gdDb = await Db())
        {
            var cny = new Currency("CNY", "CNY", "یوان چین", "Chinese Yuan", "¥", 2);
            gdDb.Currencies.Add(cny); _createdCurrencies.Add(cny.Id); _currency = cny.Id;
            var supplier = await gdDb.Suppliers.Include(x => x.PaymentProfile)
                .SingleAsync(x => x.Id == _supplier);
            supplier.PaymentProfile.Set(supplier.PaymentProfile.PaymentTermId, 20m, 50m, 30m, 60,
                "WAREHOUSE_RECEIPT");
            var gd = await AddSourcing(gdDb, _supplier, _currency, _unit, _shipping, _incoterm, "GD05", priceValue: 7m);
            await gdDb.SaveChangesAsync(); _item = gd.Item; gdOption = gd.Option; gdPrice = gd.Price;
        }
        Guid warehouse;
        await using (var warehouseDb = await Db())
        {
            var value = new Warehouse($"WH-{Random.Shared.Next(100000, 999999)}",
                "انبار مواد اولیه", "Raw material warehouse", null);
            warehouseDb.Warehouses.Add(value); await warehouseDb.SaveChangesAsync(); warehouse = value.Id;
        }
        var draftResponse = await CreateResponse(optionId: gdOption, priceId: gdPrice,
            orderedQuantity: 80m, currencyId: _currency);
        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);
        var draft = await Read(draftResponse); var poId = draft.GetProperty("id").GetGuid();
        Assert.Empty(await Firm());
        var approved = await Post($"/api/purchase-orders/{poId}/approve", new
        { expectedVersion = draft.GetProperty("version").GetInt64() });
        Assert.Empty(await Firm());
        var ordered = await Post($"/api/purchase-orders/{poId}/mark-ordered", new
        { expectedVersion = approved.GetProperty("version").GetInt64(), orderedDate = "2026-08-02" });
        Assert.Equal(80m, (await Firm(new DateOnly(2026, 8, 2))).Sum(x => x.GetProperty("openQuantity").GetDecimal()));
        Assert.Equal("CNY", ordered.GetProperty("currency").GetProperty("code").GetString());
        Assert.Equal(30m, ordered.GetProperty("creditPercent").GetDecimal());
        Assert.Equal(60, ordered.GetProperty("creditDays").GetInt32());
        Assert.Equal("WAREHOUSE_RECEIPT", ordered.GetProperty("creditAnchor").GetString());
        var line = ordered.GetProperty("lines")[0];
        Assert.Equal(560m, line.GetProperty("lineTotal").GetDecimal());
        var split = await Post($"/api/purchase-orders/{poId}/lines/{line.GetProperty("id").GetGuid()}/reallocate-schedules", new
        {
            expectedPurchaseOrderVersion = ordered.GetProperty("version").GetInt64(),
            reason = "GD-05 approved split",
            schedules = new[] { new { allocatedQuantity = 30m, requestedReceiptDate = "2026-08-05" },
                new { allocatedQuantity = 50m, requestedReceiptDate = "2026-08-07" } }
        });
        var schedules = split.GetProperty("lines")[0].GetProperty("schedules");
        Assert.Equal(80m, schedules.EnumerateArray().Sum(x => x.GetProperty("allocatedQuantity").GetDecimal()));
        var scheduleA = schedules[0]; var scheduleB = schedules[1];
        var over = await CreateReceipt(poId, warehouse, line.GetProperty("id").GetGuid(),
            scheduleA.GetProperty("id").GetGuid(), 31m, new DateOnly(2026, 8, 5));
        var overResponse = await _client.PostAsJsonAsync(
            $"/api/purchase-receipts/{over.GetProperty("id").GetGuid()}/post", new
            {
                expectedReceiptVersion = over.GetProperty("version").GetInt64(),
                expectedPurchaseOrderVersion = split.GetProperty("version").GetInt64()
            });
        Assert.Equal(HttpStatusCode.Conflict, overResponse.StatusCode);
        Assert.Equal("OVER_RECEIPT_NOT_ALLOWED", await ProblemCode(overResponse));
        var receipt1 = await CreateReceipt(poId, warehouse, line.GetProperty("id").GetGuid(),
            scheduleA.GetProperty("id").GetGuid(), 20m, new DateOnly(2026, 8, 5));
        var posted1 = await Post($"/api/purchase-receipts/{receipt1.GetProperty("id").GetGuid()}/post", new
        {
            expectedReceiptVersion = receipt1.GetProperty("version").GetInt64(),
            expectedPurchaseOrderVersion = split.GetProperty("version").GetInt64()
        });
        Assert.Equal("POSTED", posted1.GetProperty("status").GetString());
        Assert.NotEqual(Guid.Empty, posted1.GetProperty("lines")[0].GetProperty("inventoryTransactionId").GetGuid());
        var partial = await Get(poId);
        Assert.Equal("PARTIALLY_RECEIVED", partial.GetProperty("status").GetString());
        Assert.Equal(20m, partial.GetProperty("lines")[0].GetProperty("receivedQuantity").GetDecimal());
        Assert.Equal(60m, partial.GetProperty("lines")[0].GetProperty("openQuantity").GetDecimal());
        Assert.Equal("OPEN", partial.GetProperty("completionOutcome").GetString());
        Assert.Equal(JsonValueKind.Null, partial.GetProperty("finalReceiptDate").ValueKind);
        var receiptActual = Assert.Single(partial.GetProperty("lines")[0].GetProperty("schedules")[0]
            .GetProperty("actualMilestones").EnumerateArray(), x =>
                x.GetProperty("milestoneCode").GetString() == "WAREHOUSE_RECEIPT");
        Assert.Equal("PURCHASE_RECEIPT", receiptActual.GetProperty("sourceType").GetString());
        Assert.Equal(receipt1.GetProperty("id").GetGuid(), receiptActual.GetProperty("sourceId").GetGuid());
        Assert.Equal("2026-08-05", receiptActual.GetProperty("actualDate").GetString());
        Assert.Equal(60m, (await Firm()).Sum(x => x.GetProperty("openQuantity").GetDecimal()));

        var cancelled = await Post($"/api/purchase-orders/{poId}/schedules/{scheduleA.GetProperty("id").GetGuid()}/cancel", new
        {
            expectedPurchaseOrderVersion = partial.GetProperty("version").GetInt64(),
            quantity = 10m,
            effectiveDate = "2026-08-05",
            reason = "GD-05 schedule A remainder cancelled"
        });
        Assert.Equal(20m, cancelled.GetProperty("lines")[0].GetProperty("receivedQuantity").GetDecimal());
        Assert.Equal(10m, cancelled.GetProperty("lines")[0].GetProperty("cancelledQuantity").GetDecimal());
        Assert.Equal(50m, cancelled.GetProperty("lines")[0].GetProperty("openQuantity").GetDecimal());
        var receipt2 = await CreateReceipt(poId, warehouse, line.GetProperty("id").GetGuid(),
            scheduleB.GetProperty("id").GetGuid(), 50m, new DateOnly(2026, 8, 7));
        var posted2 = await Post($"/api/purchase-receipts/{receipt2.GetProperty("id").GetGuid()}/post", new
        { expectedReceiptVersion = receipt2.GetProperty("version").GetInt64(), expectedPurchaseOrderVersion = cancelled.GetProperty("version").GetInt64() });
        var completed = await Get(poId);
        Assert.Equal("RECEIVED", completed.GetProperty("status").GetString());
        Assert.Equal("RECEIVED_WITH_CANCELLATION", completed.GetProperty("completionOutcome").GetString());
        Assert.Equal("2026-08-05", completed.GetProperty("firstReceiptDate").GetString());
        Assert.Equal("2026-08-07", completed.GetProperty("finalReceiptDate").GetString());
        Assert.Empty(await Firm());

        var reversed = await Post($"/api/purchase-receipts/{receipt2.GetProperty("id").GetGuid()}/reverse", new
        {
            expectedReceiptVersion = posted2.GetProperty("version").GetInt64(),
            expectedPurchaseOrderVersion = completed.GetProperty("version").GetInt64(),
            reason = "physical receipt void"
        });
        Assert.Equal("REVERSED", reversed.GetProperty("status").GetString());
        await using (var linkageDb = await Db())
        {
            var reversedLineId = reversed.GetProperty("lines")[0].GetProperty("id").GetGuid();
            await Assert.ThrowsAsync<PostgresException>(() => linkageDb.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE procurement.purchase_receipt_lines SET \"ReversalInventoryTransactionId\"=NULL WHERE \"Id\"={reversedLineId}"));
        }
        var reopened = await Get(poId);
        Assert.Equal("PARTIALLY_RECEIVED", reopened.GetProperty("status").GetString());
        Assert.Equal(50m, reopened.GetProperty("lines")[0].GetProperty("openQuantity").GetDecimal());
        Assert.Equal(JsonValueKind.Null, reopened.GetProperty("finalReceiptDate").ValueKind);
        Assert.DoesNotContain(reopened.GetProperty("lines")[0].GetProperty("schedules")[1]
            .GetProperty("actualMilestones").EnumerateArray(), x =>
                x.GetProperty("sourceId").GetGuid() == receipt2.GetProperty("id").GetGuid());
        var receipt3 = await CreateReceipt(poId, warehouse, line.GetProperty("id").GetGuid(),
            scheduleB.GetProperty("id").GetGuid(), 50m, new DateOnly(2026, 8, 9));
        await Post($"/api/purchase-receipts/{receipt3.GetProperty("id").GetGuid()}/post", new
        { expectedReceiptVersion = receipt3.GetProperty("version").GetInt64(), expectedPurchaseOrderVersion = reopened.GetProperty("version").GetInt64() });
        var recompleted = await Get(poId);
        Assert.Equal("RECEIVED_WITH_CANCELLATION", recompleted.GetProperty("completionOutcome").GetString());
        Assert.Equal("2026-08-09", recompleted.GetProperty("finalReceiptDate").GetString());

        var (fullyCancelled, _) = await OrderedWithWarehouse(50m, new DateOnly(2026, 8, 2),
            gdOption, gdPrice, _currency);
        var cancelledPoId = fullyCancelled.GetProperty("id").GetGuid();
        var cancelledSchedule = fullyCancelled.GetProperty("lines")[0].GetProperty("schedules")[0];
        var fullyCancelledResult = await Post($"/api/purchase-orders/{cancelledPoId}/schedules/{cancelledSchedule.GetProperty("id").GetGuid()}/cancel", new
        {
            expectedPurchaseOrderVersion = fullyCancelled.GetProperty("version").GetInt64(),
            quantity = 50m,
            effectiveDate = "2026-08-05",
            reason = "GD-05 fully-cancelled micro-case"
        });
        Assert.Equal("FULLY_CANCELLED", fullyCancelledResult.GetProperty("completionOutcome").GetString());
        Assert.Equal(JsonValueKind.Null, fullyCancelledResult.GetProperty("finalReceiptDate").ValueKind);
        await using var db = await Db();
        Assert.Equal(3, await db.InventoryTransactions.CountAsync(x => x.TransactionType == InventoryTransactionTypes.Receipt));
        Assert.Equal(1, await db.InventoryTransactions.CountAsync(x => x.TransactionType == InventoryTransactionTypes.Reversal));
        Assert.Equal(70m, await db.InventoryLedgerEffects.Where(x => x.ItemId == _item && x.WarehouseId == warehouse)
            .SumAsync(x => x.QuantityEffect));
    }

    [Fact]
    public async Task GD08_receipts_reduce_later_run_firm_supply_and_increase_actual_inventory_without_duplicate_procurement()
    {
        var asOf = new DateOnly(2026, 8, 25);
        var calendar = _factory.Services.GetRequiredService<IPlanningCalendar>();
        var (jalaliYear, jalaliMonth) = calendar.GetJalaliPeriod(asOf);
        var bounds = calendar.GetPeriodBounds(jalaliYear, jalaliMonth);
        Guid productId;
        await using (var setup = await Db())
        {
            var category = await AddReference(setup, "PRODUCT_CATEGORY", "GD08-PRODUCT");
            var productItem = new Item(ItemTypes.Product, $"PRD-{Random.Shared.NextInt64(100000, 999999)}",
                "محصول GD08", "GD08 product", _unit, null);
            var product = new Product(productItem.Id, category.Id, null, 0, 1, 2, 3);
            var period = new BusinessPeriod(jalaliYear, jalaliMonth, bounds.StartDate, bounds.EndDate);
            setup.AddRange(productItem, product, period);
            await setup.SaveChangesAsync();
            productId = productItem.Id;
            _createdItems.Add(productId);

            var series = new ForecastSeries(productId, period.Id, _unit);
            setup.ForecastSeries.Add(series);
            await setup.SaveChangesAsync();
            var forecast = new ForecastRevision(series.Id, 1, 12.5m, null, "GD-08");
            setup.ForecastRevisions.Add(forecast);
            await setup.SaveChangesAsync();
            series.SetCurrent(forecast);

            var bom = new BomRevision($"BOM-GD08-{Guid.NewGuid():N}"[..28], productId, _unit, 1,
                bounds.StartDate, bounds.EndDate, "GD-08 receipt feedback");
            var edge = new BomLine(bom.Id, _item, _unit, 1, 0, 1);
            setup.AddRange(bom, edge);
            await setup.SaveChangesAsync();
            bom.Approve(null);
            await setup.SaveChangesAsync();
        }

        try
        {
            var (ordered, warehouse) = await OrderedWithWarehouse(12.5m, asOf);
            var poId = ordered.GetProperty("id").GetGuid();
            var line = ordered.GetProperty("lines")[0];
            var lineId = line.GetProperty("id").GetGuid();
            var scheduleId = line.GetProperty("schedules")[0].GetProperty("id").GetGuid();

            async Task<MrpRunResponse> ExecuteRun()
            {
                var response = await _client.PostAsJsonAsync("/api/mrp-runs",
                    new CreateMrpRunRequest(asOf, bounds.EndDate, []));
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                return (await response.Content.ReadFromJsonAsync<MrpRunResponse>())!;
            }

            var r1 = await ExecuteRun();
            await using (var verifyR1 = await Db())
            {
                var firm = Assert.Single(await verifyR1.MrpRunFirmSupplyInputs.AsNoTracking()
                    .Where(x => x.RunId == r1.Id && x.DeliveryScheduleId == scheduleId).ToListAsync());
                Assert.Equal(12.5m, firm.OpenQty);
                Assert.False(await verifyR1.MrpRunInventoryInputs.AnyAsync(x => x.RunId == r1.Id &&
                    x.ItemId == _item && x.WarehouseId == warehouse));
            }

            var partial = await CreateReceipt(poId, warehouse, lineId, scheduleId, 5m, asOf);
            await Post($"/api/purchase-receipts/{partial.GetProperty("id").GetGuid()}/post", new
            {
                expectedReceiptVersion = partial.GetProperty("version").GetInt64(),
                expectedPurchaseOrderVersion = ordered.GetProperty("version").GetInt64()
            });
            var afterPartial = await Get(poId);
            var r2 = await ExecuteRun();
            await using (var verifyR2 = await Db())
            {
                var firm = Assert.Single(await verifyR2.MrpRunFirmSupplyInputs.AsNoTracking()
                    .Where(x => x.RunId == r2.Id && x.DeliveryScheduleId == scheduleId).ToListAsync());
                Assert.Equal(7.5m, firm.OpenQty);
                Assert.Equal(5m, await verifyR2.MrpRunInventoryInputs.AsNoTracking().Where(x => x.RunId == r2.Id &&
                    x.ItemId == _item && x.WarehouseId == warehouse).SumAsync(x => x.OnHandQty));
            }

            var final = await CreateReceipt(poId, warehouse, lineId, scheduleId, 7.5m, asOf);
            await Post($"/api/purchase-receipts/{final.GetProperty("id").GetGuid()}/post", new
            {
                expectedReceiptVersion = final.GetProperty("version").GetInt64(),
                expectedPurchaseOrderVersion = afterPartial.GetProperty("version").GetInt64()
            });
            var r3 = await ExecuteRun();
            await using var verifyR3 = await Db();
            Assert.False(await verifyR3.MrpRunFirmSupplyInputs.AnyAsync(x => x.RunId == r3.Id &&
                x.DeliveryScheduleId == scheduleId));
            Assert.Equal(12.5m, await verifyR3.MrpRunInventoryInputs.AsNoTracking().Where(x => x.RunId == r3.Id &&
                x.ItemId == _item && x.WarehouseId == warehouse).SumAsync(x => x.OnHandQty));
            Assert.Equal(1, await verifyR3.PurchaseOrders.CountAsync(x => x.Id == poId));
        }
        finally
        {
            await using var cleanup = await Db();
            await cleanup.Database.ExecuteSqlRawAsync("""
                TRUNCATE planning.mrp_run_demand_traces, planning.mrp_run_daily_results,
                  planning.mrp_run_item_summaries, planning.mrp_run_exceptions,
                  planning.mrp_run_external_source_inputs, planning.mrp_run_forecast_period_inputs,
                  planning.mrp_run_route_decisions, planning.mrp_run_route_availability_inputs,
                  planning.mrp_run_firm_supply_inputs, planning.mrp_run_inventory_inputs,
                  planning.mrp_run_bom_inputs, planning.mrp_run_forecast_inputs,
                  planning.mrp_run_item_inputs, planning.mrp_runs RESTART IDENTITY CASCADE;
                TRUNCATE planning.daily_actual_revisions, planning.daily_actuals,
                  planning.forecast_revisions, planning.forecast_series,
                  planning.period_lifecycle_events, planning.business_periods RESTART IDENTITY CASCADE;
                TRUNCATE engineering.bom_lines, engineering.bom_revisions RESTART IDENTITY CASCADE;
                """);
            await cleanup.Products.Where(x => x.ItemId == productId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Concurrent_receipts_and_receipt_cancel_races_cannot_overreceive_or_double_count_firm_supply()
    {
        var (ordered, warehouse) = await OrderedWithWarehouse();
        var poId = ordered.GetProperty("id").GetGuid(); var line = ordered.GetProperty("lines")[0];
        var lineId = line.GetProperty("id").GetGuid(); var scheduleId = line.GetProperty("schedules")[0].GetProperty("id").GetGuid();
        var first = await CreateReceipt(poId, warehouse, lineId, scheduleId, 8m);
        var second = await CreateReceipt(poId, warehouse, lineId, scheduleId, 8m);
        var version = ordered.GetProperty("version").GetInt64();
        var calls = await Task.WhenAll(new[] { first, second }.Select(r => _client.PostAsJsonAsync(
            $"/api/purchase-receipts/{r.GetProperty("id").GetGuid()}/post", new
            { expectedReceiptVersion = r.GetProperty("version").GetInt64(), expectedPurchaseOrderVersion = version })).ToArray());
        Assert.Equal(1, calls.Count(x => x.IsSuccessStatusCode));
        Assert.Equal(1, calls.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        var after = await Get(poId);
        Assert.Equal(8m, after.GetProperty("lines")[0].GetProperty("receivedQuantity").GetDecimal());
        Assert.Equal(4.5m, after.GetProperty("lines")[0].GetProperty("openQuantity").GetDecimal());

        var currentVersion = after.GetProperty("version").GetInt64();
        var third = await CreateReceipt(poId, warehouse, lineId, scheduleId, 4.5m);
        var receiptCall = _client.PostAsJsonAsync($"/api/purchase-receipts/{third.GetProperty("id").GetGuid()}/post", new
        { expectedReceiptVersion = 1, expectedPurchaseOrderVersion = currentVersion });
        var cancelCall = _client.PostAsJsonAsync($"/api/purchase-orders/{poId}/schedules/{scheduleId}/cancel", new
        { expectedPurchaseOrderVersion = currentVersion, quantity = 4.5m, effectiveDate = "2026-08-25", reason = "race" });
        var race = await Task.WhenAll(receiptCall, cancelCall);
        Assert.Equal(1, race.Count(x => x.IsSuccessStatusCode));
        Assert.Equal(1, race.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        var final = await Get(poId);
        Assert.Equal(12.5m, final.GetProperty("lines")[0].GetProperty("receivedQuantity").GetDecimal() +
            final.GetProperty("lines")[0].GetProperty("cancelledQuantity").GetDecimal());
        Assert.Equal(0m, final.GetProperty("lines")[0].GetProperty("openQuantity").GetDecimal());
    }

    [Fact]
    public async Task Direct_SQL_overreceipt_and_history_mutation_are_blocked()
    {
        var (ordered, warehouse) = await OrderedWithWarehouse();
        var poId = ordered.GetProperty("id").GetGuid(); var line = ordered.GetProperty("lines")[0];
        var lineId = line.GetProperty("id").GetGuid(); var scheduleId = line.GetProperty("schedules")[0].GetProperty("id").GetGuid();
        var receipt = await CreateReceipt(poId, warehouse, lineId, scheduleId, 13m);
        await using var db = await Db();
        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE procurement.purchase_receipts SET \"Status\"='POSTED', \"PostedAtUtc\"=CURRENT_TIMESTAMP, \"Version\"=2 WHERE \"Id\"={receipt.GetProperty("id").GetGuid()}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        Assert.Contains("OVER_RECEIPT_NOT_ALLOWED", ex.Message);
        var milestone = await Post($"/api/purchase-orders/{poId}/schedules/{scheduleId}/actual-milestones", new
        { expectedPurchaseOrderVersion = ordered.GetProperty("version").GetInt64(), milestoneCode = "SHIPPED", actualDate = "2026-08-25" });
        var milestoneId = milestone.GetProperty("lines")[0].GetProperty("schedules")[0]
            .GetProperty("actualMilestones")[0].GetProperty("id").GetGuid();
        await using var db2 = await Db();
        await Assert.ThrowsAsync<PostgresException>(() => db2.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE procurement.delivery_schedule_actual_milestones SET \"ActualDate\"='2026-08-24' WHERE \"Id\"={milestoneId}"));
    }

    [Fact]
    public async Task Receipt_inventory_fault_rolls_back_all_facts_and_success_audit()
    {
        var (ordered, warehouse) = await OrderedWithWarehouse(); var poId = ordered.GetProperty("id").GetGuid();
        var line = ordered.GetProperty("lines")[0]; var receipt = await CreateReceipt(poId, warehouse,
            line.GetProperty("id").GetGuid(), line.GetProperty("schedules")[0].GetProperty("id").GetGuid(), 5m);
        var audit = _factory.AuditSink.Snapshot().Count;
        _factory.PurchaseReceiptFaultInjector.FailAfterReceipt = true;
        var beforeInventory = await _client.PostAsJsonAsync($"/api/purchase-receipts/{receipt.GetProperty("id").GetGuid()}/post", new
        { expectedReceiptVersion = 1, expectedPurchaseOrderVersion = ordered.GetProperty("version").GetInt64() });
        Assert.Equal(HttpStatusCode.InternalServerError, beforeInventory.StatusCode);
        _factory.PurchaseReceiptFaultInjector.Reset();
        _factory.PurchaseReceiptFaultInjector.FailAfterInventory = true;
        var afterInventory = await _client.PostAsJsonAsync($"/api/purchase-receipts/{receipt.GetProperty("id").GetGuid()}/post", new
        { expectedReceiptVersion = 1, expectedPurchaseOrderVersion = ordered.GetProperty("version").GetInt64() });
        Assert.Equal(HttpStatusCode.InternalServerError, afterInventory.StatusCode);
        _factory.PurchaseReceiptFaultInjector.Reset();
        await using var db = await Db();
        Assert.Equal(PurchaseReceiptStatuses.Draft, (await db.PurchaseReceipts.SingleAsync(x => x.Id == receipt.GetProperty("id").GetGuid())).Status);
        Assert.False(await db.InventoryTransactions.AnyAsync(x => x.SourceDocumentType == InventorySourceDocumentTypes.PurchaseReceipt));
        Assert.Equal(audit, _factory.AuditSink.Snapshot().Count);
    }

    [Fact]
    public async Task Receipt_reversal_that_would_make_inventory_negative_rolls_back_without_stale_final_state()
    {
        var (ordered, warehouse) = await OrderedWithWarehouse(); var poId = ordered.GetProperty("id").GetGuid();
        var line = ordered.GetProperty("lines")[0]; var receipt = await CreateReceipt(poId, warehouse,
            line.GetProperty("id").GetGuid(), line.GetProperty("schedules")[0].GetProperty("id").GetGuid(), 5m);
        var posted = await Post($"/api/purchase-receipts/{receipt.GetProperty("id").GetGuid()}/post", new
        { expectedReceiptVersion = 1, expectedPurchaseOrderVersion = ordered.GetProperty("version").GetInt64() });
        var afterPost = await Get(poId);
        var issue = await _client.PostAsJsonAsync("/api/inventory/issues", new
        { itemId = _item, warehouseId = warehouse, quantity = 5m, effectiveDate = "2026-08-25", reason = "consumed" });
        Assert.Equal(HttpStatusCode.Created, issue.StatusCode);
        var beforeAudit = _factory.AuditSink.Snapshot().Count;
        var reversal = await _client.PostAsJsonAsync($"/api/purchase-receipts/{receipt.GetProperty("id").GetGuid()}/reverse", new
        { expectedReceiptVersion = posted.GetProperty("version").GetInt64(), expectedPurchaseOrderVersion = afterPost.GetProperty("version").GetInt64(), reason = "invalid after issue" });
        Assert.Equal(HttpStatusCode.Conflict, reversal.StatusCode);
        var persistedReceipt = await _client.GetFromJsonAsync<JsonElement>($"/api/purchase-receipts/{receipt.GetProperty("id").GetGuid()}");
        Assert.Equal("POSTED", persistedReceipt.GetProperty("status").GetString());
        await using var db = await Db();
        Assert.False(await db.InventoryTransactions.AnyAsync(x => x.OriginalTransactionId ==
            posted.GetProperty("lines")[0].GetProperty("inventoryTransactionId").GetGuid()));
        Assert.Equal(beforeAudit, _factory.AuditSink.Snapshot().Count);
    }

    [Fact]
    public async Task Expert_decision_conversion_is_lineage_complete_idempotent_and_core_immutable()
    {
        var fixture = await AddExpertDecision();
        var converted = await Post("/api/purchase-orders/convert-decisions", new { decisionIds = new[] { fixture.DecisionId } });
        var group = Assert.Single(converted.GetProperty("groups").EnumerateArray());
        Assert.True(group.GetProperty("success").GetBoolean());
        var poId = group.GetProperty("purchaseOrderId").GetGuid();
        var po = await Get(poId); var line = po.GetProperty("lines")[0];
        Assert.Equal("DRAFT", po.GetProperty("status").GetString());
        Assert.Equal(fixture.DecisionId, line.GetProperty("expertDecisionId").GetGuid());
        Assert.Equal(fixture.RecommendationId, line.GetProperty("recommendationId").GetGuid());
        Assert.Equal(fixture.GenerationId, line.GetProperty("generationId").GetGuid());
        Assert.Equal(fixture.RunId, line.GetProperty("mrpRunId").GetGuid());
        Assert.Equal(12.5m, line.GetProperty("orderedQuantity").GetDecimal());
        Assert.Equal(12.5m, line.GetProperty("schedules")[0].GetProperty("allocatedQuantity").GetDecimal());

        var retry = await Post("/api/purchase-orders/convert-decisions", new { decisionIds = new[] { fixture.DecisionId } });
        Assert.Equal(poId, retry.GetProperty("groups")[0].GetProperty("purchaseOrderId").GetGuid());
        await using (var db = await Db())
            Assert.Equal(1, await db.PurchaseOrderLines.CountAsync(x => x.ExpertDecisionId == fixture.DecisionId));
        var edit = await _client.PutAsJsonAsync($"/api/purchase-orders/{poId}/lines/{line.GetProperty("id").GetGuid()}", new
        {
            expectedVersion = po.GetProperty("version").GetInt64(),
            line = new
            {
                sourcingOptionId = _option,
                sourcingPriceId = _price,
                orderedQuantity = 20m,
                requestedReceiptDate = "2026-09-01",
                notes = "forbidden"
            }
        });
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        Assert.Equal("DECISION_DERIVED_CORE_IMMUTABLE", await ProblemCode(edit));
        await using var direct = await Db();
        await Assert.ThrowsAsync<PostgresException>(() => direct.Database.ExecuteSqlInterpolatedAsync($$"""
            SELECT set_config('hadron.mrp13_conversion','on',false);
            UPDATE procurement.purchase_order_lines SET "UnitPrice"={{99m}}
            WHERE "Id"={{line.GetProperty("id").GetGuid()}};
            """));
    }

    [Fact]
    public async Task Concurrent_expert_decision_conversion_creates_exactly_one_purchase_order()
    {
        var fixture = await AddExpertDecision(); var payload = new { decisionIds = new[] { fixture.DecisionId } };
        var responses = await Task.WhenAll(_client.PostAsJsonAsync("/api/purchase-orders/convert-decisions", payload),
            _client.PostAsJsonAsync("/api/purchase-orders/convert-decisions", payload));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var results = await Task.WhenAll(responses.Select(Read));
        Assert.Contains(results, result => result.GetProperty("groups").EnumerateArray().Any(group => group.GetProperty("success").GetBoolean()));
        await using var db = await Db();
        var line = Assert.Single(await db.PurchaseOrderLines.Where(x => x.ExpertDecisionId == fixture.DecisionId).ToListAsync());
        Assert.Equal(1, await db.PurchaseOrders.CountAsync(x => x.Id == line.PurchaseOrderId));
    }

    [Fact]
    public async Task Batch_conversion_commits_a_valid_group_when_an_independent_group_fails()
    {
        var fixture = await AddExpertDecision(); var missing = Guid.NewGuid();
        var result = await Post("/api/purchase-orders/convert-decisions", new
        { decisionIds = new[] { fixture.DecisionId, missing } });
        var groups = result.GetProperty("groups").EnumerateArray().ToList();
        var success = Assert.Single(groups, x => x.GetProperty("success").GetBoolean());
        Assert.NotEqual(Guid.Empty, success.GetProperty("purchaseOrderId").GetGuid());
        var failure = Assert.Single(groups, x => !x.GetProperty("success").GetBoolean());
        Assert.Equal("DECISION_NOT_FOUND", failure.GetProperty("decisions")[0].GetProperty("code").GetString());
        await using var db = await Db();
        Assert.Equal(1, await db.PurchaseOrderLines.CountAsync(x => x.ExpertDecisionId == fixture.DecisionId));
        Assert.False(await db.PurchaseOrderLines.AnyAsync(x => x.ExpertDecisionId == missing));
    }

    [Fact]
    public async Task Conversion_detects_price_drift_without_refreshing_or_creating_PO()
    {
        var fixture = await AddExpertDecision();
        await using (var db = await Db())
        {
            var price = await db.SourcingPrices.SingleAsync(x => x.Id == _price);
            price.Close(new DateOnly(2026, 8, 24)); await db.SaveChangesAsync();
        }
        var result = await Post("/api/purchase-orders/convert-decisions", new { decisionIds = new[] { fixture.DecisionId } });
        var decision = result.GetProperty("groups")[0].GetProperty("decisions")[0];
        Assert.False(decision.GetProperty("success").GetBoolean());
        Assert.Equal("DECISION_RECONFIRMATION_REQUIRED", decision.GetProperty("code").GetString());
        Assert.Contains("PRICE_CHANGED", decision.GetProperty("detailReasons").EnumerateArray().Select(x => x.GetString()));
        await using var verify = await Db();
        Assert.False(await verify.PurchaseOrderLines.AnyAsync(x => x.ExpertDecisionId == fixture.DecisionId));
    }

    [Fact]
    public async Task Actual_milestones_are_append_only_and_an_ordered_inactive_item_can_be_settled_with_warning()
    {
        var (ordered, warehouse) = await OrderedWithWarehouse(); var poId = ordered.GetProperty("id").GetGuid();
        var line = ordered.GetProperty("lines")[0]; var scheduleId = line.GetProperty("schedules")[0].GetProperty("id").GetGuid();
        var ready = await Post($"/api/purchase-orders/{poId}/schedules/{scheduleId}/actual-milestones", new
        { expectedPurchaseOrderVersion = ordered.GetProperty("version").GetInt64(), milestoneCode = "SUPPLIER_READY", actualDate = "2026-08-20" });
        var shipped = await Post($"/api/purchase-orders/{poId}/schedules/{scheduleId}/actual-milestones", new
        { expectedPurchaseOrderVersion = ready.GetProperty("version").GetInt64(), milestoneCode = "SHIPPED", actualDate = "2026-08-21" });
        var original = Assert.Single(shipped.GetProperty("lines")[0].GetProperty("schedules")[0]
            .GetProperty("actualMilestones").EnumerateArray(), x => x.GetProperty("milestoneCode").GetString() == "SHIPPED");
        var corrected = await Post($"/api/purchase-orders/{poId}/schedules/{scheduleId}/actual-milestones", new
        {
            expectedPurchaseOrderVersion = shipped.GetProperty("version").GetInt64(),
            milestoneCode = "SHIPPED",
            actualDate = "2026-08-22",
            supersedesMilestoneId = original.GetProperty("id").GetGuid(),
            reason = "carrier correction"
        });
        var history = corrected.GetProperty("lines")[0].GetProperty("schedules")[0].GetProperty("actualMilestones");
        Assert.Equal(2, history.EnumerateArray().Count(x => x.GetProperty("milestoneCode").GetString() == "SHIPPED"));
        Assert.Contains(history.EnumerateArray(), x => x.GetProperty("actualDate").GetString() == "2026-08-21");
        Assert.Contains(history.EnumerateArray(), x => x.GetProperty("actualDate").GetString() == "2026-08-22" &&
            x.GetProperty("supersedesMilestoneId").GetGuid() == original.GetProperty("id").GetGuid());
        var future = await _client.PostAsJsonAsync($"/api/purchase-orders/{poId}/schedules/{scheduleId}/actual-milestones", new
        { expectedPurchaseOrderVersion = corrected.GetProperty("version").GetInt64(), milestoneCode = "ARRIVED_DESTINATION", actualDate = "2026-08-26" });
        Assert.Equal(HttpStatusCode.Conflict, future.StatusCode);
        Assert.Equal("FUTURE_ACTUAL_MILESTONE_NOT_ALLOWED", await ProblemCode(future));
        var manualReceiptMilestone = await _client.PostAsJsonAsync($"/api/purchase-orders/{poId}/schedules/{scheduleId}/actual-milestones", new
        { expectedPurchaseOrderVersion = corrected.GetProperty("version").GetInt64(), milestoneCode = "WAREHOUSE_RECEIPT", actualDate = "2026-08-22" });
        Assert.Equal(HttpStatusCode.BadRequest, manualReceiptMilestone.StatusCode);

        await using (var db = await Db())
        {
            var item = await db.Items.SingleAsync(x => x.Id == _item); item.ChangeState(false); await db.SaveChangesAsync();
        }
        var receipt = await CreateReceipt(poId, warehouse, line.GetProperty("id").GetGuid(), scheduleId, 12.5m);
        var posted = await Post($"/api/purchase-receipts/{receipt.GetProperty("id").GetGuid()}/post", new
        { expectedReceiptVersion = receipt.GetProperty("version").GetInt64(), expectedPurchaseOrderVersion = corrected.GetProperty("version").GetInt64() });
        Assert.Contains("INACTIVE_ITEM_EXISTING_COMMITMENT", posted.GetProperty("warnings").EnumerateArray().Select(x => x.GetString()));
        var settled = await Get(poId); Assert.Equal("RECEIVED", settled.GetProperty("status").GetString());
        Assert.Contains("INACTIVE_ITEM_EXISTING_COMMITMENT", settled.GetProperty("warnings").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task Create_snapshots_commercial_truth_and_default_schedule_without_duplicate_counters()
    {
        var created = await Create();
        Assert.Matches("^PO-[0-9]{6,}$", created.GetProperty("purchaseOrderNumber").GetString());
        Assert.Equal("DRAFT", created.GetProperty("status").GetString());
        Assert.Equal(1, created.GetProperty("lines").GetArrayLength());
        var line = created.GetProperty("lines")[0];
        Assert.Equal(_item, line.GetProperty("item").GetProperty("id").GetGuid());
        Assert.StartsWith("PRT-", line.GetProperty("item").GetProperty("code").GetString());
        Assert.Equal(_option, line.GetProperty("sourcingOptionId").GetGuid());
        Assert.Equal(_price, line.GetProperty("sourcingPriceId").GetGuid());
        Assert.Equal(_unit, line.GetProperty("itemBaseUnit").GetProperty("id").GetGuid());
        Assert.Equal(12.5m, line.GetProperty("orderedQuantity").GetDecimal());
        Assert.Equal(3m, line.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(37.5m, line.GetProperty("lineTotal").GetDecimal());
        Assert.Equal(12.5m, line.GetProperty("openQuantity").GetDecimal());
        Assert.Equal(0m, line.GetProperty("receivedQuantity").GetDecimal());
        Assert.Equal(0m, line.GetProperty("cancelledQuantity").GetDecimal());
        var schedule = line.GetProperty("schedules")[0];
        Assert.Equal(12.5m, schedule.GetProperty("allocatedQuantity").GetDecimal());
        Assert.Equal("MISSING", schedule.GetProperty("firmEtaSource").GetString());
        Assert.Equal("WAREHOUSE_RECEIPT", created.GetProperty("creditAnchor").GetString());
        Assert.Equal(8, await (await Db()).ReferenceTypes.CountAsync());
    }

    [Fact]
    public async Task Lifecycle_enforces_approval_boundary_return_reason_and_firm_plan_precedence()
    {
        var draft = await Create(requestedReceipt: new DateOnly(2030, 1, 1));
        var id = draft.GetProperty("id").GetGuid();
        var version = draft.GetProperty("version").GetInt64();
        Assert.Empty(await Firm());
        var approved = await Post($"/api/purchase-orders/{id}/approve", new { expectedVersion = version });
        Assert.Equal("APPROVED", approved.GetProperty("status").GetString());
        Assert.Empty(await Firm());
        var noReason = await _client.PostAsJsonAsync($"/api/purchase-orders/{id}/return-to-draft",
            new { expectedVersion = approved.GetProperty("version").GetInt64(), reason = " " });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        var returned = await Post($"/api/purchase-orders/{id}/return-to-draft", new
        { expectedVersion = approved.GetProperty("version").GetInt64(), reason = "commercial correction" });
        Assert.Equal("DRAFT", returned.GetProperty("status").GetString());
        approved = await Post($"/api/purchase-orders/{id}/approve", new
        { expectedVersion = returned.GetProperty("version").GetInt64() });
        var ordered = await Post($"/api/purchase-orders/{id}/mark-ordered", new
        {
            expectedVersion = approved.GetProperty("version").GetInt64(),
            orderedDate = "2026-08-24"
        });
        Assert.Equal("ORDERED", ordered.GetProperty("status").GetString());
        var schedule = ordered.GetProperty("lines")[0].GetProperty("schedules")[0];
        Assert.Equal("ORIGINAL_PLANNED_WAREHOUSE_RECEIPT", schedule.GetProperty("firmEtaSource").GetString());
        Assert.Equal("2026-08-28", schedule.GetProperty("firmEta").GetString());
        var original = schedule.GetProperty("planRevisions")[0];
        Assert.Equal(JsonValueKind.Null, original.GetProperty("handoverToForwarderDate").ValueKind);
        Assert.Equal(JsonValueKind.Null, original.GetProperty("customsClearedDate").ValueKind);
        var revised = await Post($"/api/purchase-orders/{id}/schedules/{schedule.GetProperty("id").GetGuid()}/revise-plan", new
        {
            expectedVersion = ordered.GetProperty("version").GetInt64(),
            reason = "supplier update",
            supplierReadyDate = "2026-08-26",
            shippedDate = "2026-08-27",
            arrivedDestinationDate = "2026-08-29",
            warehouseReceiptDate = "2026-09-02",
            handoverToForwarderDate = (string?)null,
            customsClearedDate = (string?)null
        });
        var revisedSchedule = revised.GetProperty("lines")[0].GetProperty("schedules")[0];
        Assert.Equal("REVISED_PLANNED_WAREHOUSE_RECEIPT", revisedSchedule.GetProperty("firmEtaSource").GetString());
        Assert.Equal("2026-09-02", revisedSchedule.GetProperty("firmEta").GetString());
        Assert.NotEqual("2030-01-01", revisedSchedule.GetProperty("firmEta").GetString());
    }

    [Fact]
    public async Task Aggregate_version_schedule_reconciliation_and_locked_snapshots_are_enforced()
    {
        var draft = await Create();
        var id = draft.GetProperty("id").GetGuid();
        var lineId = draft.GetProperty("lines")[0].GetProperty("id").GetGuid();
        var version = draft.GetProperty("version").GetInt64();
        var auditCount = _factory.AuditSink.Snapshot().Count;
        var stale = await _client.PutAsJsonAsync($"/api/purchase-orders/{id}/lines/{lineId}/schedules", new
        {
            expectedVersion = version + 10,
            schedules = new[] { new { allocatedQuantity = 12.5m, requestedReceiptDate = (string?)null } }
        });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("STALE_VERSION", await ProblemCode(stale));
        Assert.Equal(auditCount, _factory.AuditSink.Snapshot().Count);
        var mismatch = await _client.PutAsJsonAsync($"/api/purchase-orders/{id}/lines/{lineId}/schedules", new
        {
            expectedVersion = version,
            schedules = new[] { new { allocatedQuantity = 1m, requestedReceiptDate = (string?)null } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        var split = await Put($"/api/purchase-orders/{id}/lines/{lineId}/schedules", new
        {
            expectedVersion = version,
            schedules = new[] { new { allocatedQuantity = 5m, requestedReceiptDate = "2026-09-01" },
                new { allocatedQuantity = 7.5m, requestedReceiptDate = "2026-09-10" } }
        });
        Assert.Equal(2, split.GetProperty("lines")[0].GetProperty("schedules").GetArrayLength());
        var approved = await Post($"/api/purchase-orders/{id}/approve", new { expectedVersion = split.GetProperty("version").GetInt64() });
        var locked = await _client.PutAsJsonAsync($"/api/purchase-orders/{id}", new
        { expectedVersion = approved.GetProperty("version").GetInt64(), notes = "forbidden" });
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
        await using var db = await Db();
        var direct = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE procurement.purchase_order_lines SET \"UnitPrice\" = 99 WHERE \"Id\" = {lineId}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, direct.SqlState);
    }

    [Fact]
    public async Task Concurrent_numbers_are_unique_and_grouping_mismatch_is_blocked()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => CreateResponse()));
        Assert.All(responses, x => Assert.Equal(HttpStatusCode.Created, x.StatusCode));
        var numbers = new List<string>();
        foreach (var response in responses)
            numbers.Add((await Read(response)).GetProperty("purchaseOrderNumber").GetString()!);
        Assert.Equal(numbers.Count, numbers.Distinct().Count());

        await using var db = await Db();
        var otherIncoterm = await AddReference(db, "INCOTERM", "PO-CIF");
        var other = await AddSourcing(db, _supplier, _currency, _unit, _shipping, otherIncoterm.Id, "PO-OTHER");
        await db.SaveChangesAsync();
        var invalid = await _client.PostAsJsonAsync("/api/purchase-orders", new
        {
            supplierId = _supplier,
            currencyId = _currency,
            notes = (string?)null,
            lines = new object[]
            {
                new { sourcingOptionId = _option, sourcingPriceId = _price, orderedQuantity = 12.5m, requestedReceiptDate = (string?)null, notes = (string?)null },
                new { sourcingOptionId = other.Option, sourcingPriceId = other.Price, orderedQuantity = 12.5m, requestedReceiptDate = (string?)null, notes = (string?)null }
            }
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Snapshot_is_historical_and_scope_has_no_receipt_inventory_or_generic_status_commands()
    {
        var draft = await Create();
        var id = draft.GetProperty("id").GetGuid();
        var version = draft.GetProperty("version").GetInt64();
        var approved = await Post($"/api/purchase-orders/{id}/approve", new { expectedVersion = version });
        await using (var db = await Db())
        {
            var option = await db.SourcingOptions.SingleAsync(x => x.Id == _option);
            option.Edit(999, _shipping, _incoterm, 20, 20, 20, 20);
            await db.SaveChangesAsync();
        }
        var persisted = await Get(id);
        var line = persisted.GetProperty("lines")[0];
        Assert.Equal(2, line.GetProperty("supplierLeadTimeDays").GetInt32());
        Assert.Equal(3m, line.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(10m, line.GetProperty("minimumOrderQuantity").GetDecimal());
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsJsonAsync($"/api/purchase-orders/{id}/status", new { status = "SHIPPED" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsJsonAsync($"/api/purchase-orders/{id}/receipts", new { quantity = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsJsonAsync($"/api/purchase-orders/{id}/actual-milestones", new { milestone = "WAREHOUSE_RECEIPT" })).StatusCode);
        Assert.Contains(_factory.AuditSink.Snapshot(), x => x.EntityId == id && x.Action == "APPROVED");
        Assert.Equal("APPROVED", approved.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Active_assignment_is_required_but_inactive_historical_references_remain_resolvable()
    {
        await using (var db = await Db())
        {
            var currency = await db.Currencies.SingleAsync(x => x.Id == _currency);
            currency.SetActive(false);
            currency.AdvanceVersion();
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateResponse()).StatusCode);
        await using (var db = await Db())
        {
            var currency = await db.Currencies.SingleAsync(x => x.Id == _currency);
            currency.SetActive(true);
            currency.AdvanceVersion();
            await db.SaveChangesAsync();
        }
        var draft = await Create();
        await using (var db = await Db())
        {
            var currency = await db.Currencies.SingleAsync(x => x.Id == _currency);
            currency.SetActive(false); currency.AdvanceVersion();
            var unit = await db.Units.SingleAsync(x => x.Id == _unit);
            unit.SetActive(false); unit.AdvanceVersion();
            var item = await db.Items.SingleAsync(x => x.Id == _item);
            item.ChangeState(false);
            var supplier = await db.Suppliers.SingleAsync(x => x.Id == _supplier);
            supplier.ChangeState(false);
            await db.SaveChangesAsync();
        }
        var historical = await Get(draft.GetProperty("id").GetGuid());
        Assert.False(historical.GetProperty("currency").GetProperty("isActive").GetBoolean());
        Assert.False(historical.GetProperty("supplier").GetProperty("isActive").GetBoolean());
        Assert.False(historical.GetProperty("lines")[0].GetProperty("item").GetProperty("isActive").GetBoolean());
        Assert.False(historical.GetProperty("lines")[0].GetProperty("itemBaseUnit").GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Post_commit_audit_failure_does_not_turn_committed_purchase_order_into_api_failure()
    {
        _factory.AuditSink.ThrowOnWrite = true;
        var response = await CreateResponse();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await Read(response);
        await using var db = await Db();
        Assert.True(await db.PurchaseOrders.AnyAsync(x => x.Id == created.GetProperty("id").GetGuid()));
        _factory.AuditSink.ThrowOnWrite = false;
    }

    [Fact]
    public async Task Product_is_rejected_while_part_and_component_are_accepted()
    {
        Assert.Equal(HttpStatusCode.Created, (await CreateResponse()).StatusCode);
        await using var db = await Db();
        var component = await AddSourcing(db, _supplier, _currency, _unit, _shipping, _incoterm, "PO-COMPONENT",
            ItemTypes.Component);
        var product = await AddSourcing(db, _supplier, _currency, _unit, _shipping, _incoterm, "PO-PRODUCT",
            ItemTypes.Product);
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Created, (await CreateResponse(optionId: component.Option, priceId: component.Price)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateResponse(optionId: product.Option, priceId: product.Price)).StatusCode);
    }

    [Fact]
    public async Task Approved_snapshot_does_not_drift_with_supplier_sourcing_price_or_reference_masters()
    {
        var draft = await Create();
        var approved = await Post($"/api/purchase-orders/{draft.GetProperty("id").GetGuid()}/approve", new
        { expectedVersion = draft.GetProperty("version").GetInt64() });
        await using (var db = await Db())
        {
            var newPayment = await AddReference(db, "PAYMENT_TERM", "PO-NEW-PAYMENT");
            var newShipping = await AddReference(db, "SHIPPING_MODE", "PO-AIR");
            var newIncoterm = await AddReference(db, "INCOTERM", "PO-CIF-SNAPSHOT");
            var supplier = await db.Suppliers.Include(x => x.PaymentProfile).SingleAsync(x => x.Id == _supplier);
            supplier.PaymentProfile.Set(newPayment.Id, 10, 20, 70, 90, "ORDERED");
            var option = await db.SourcingOptions.SingleAsync(x => x.Id == _option);
            option.Edit(99, newShipping.Id, newIncoterm.Id, 8, 7, 6, 5);
            var selectedPrice = await db.SourcingPrices.SingleAsync(x => x.Id == _price);
            selectedPrice.Close(new DateOnly(2026, 8, 31));
            db.SourcingPrices.Add(new(_option, 77, _currency, new DateOnly(2026, 9, 1), null));
            await db.SaveChangesAsync();
        }
        var persisted = await Get(approved.GetProperty("id").GetGuid());
        var line = persisted.GetProperty("lines")[0];
        Assert.Equal(20m, persisted.GetProperty("advancePercent").GetDecimal());
        Assert.Equal(30m, persisted.GetProperty("beforeShipmentPercent").GetDecimal());
        Assert.Equal(50m, persisted.GetProperty("creditPercent").GetDecimal());
        Assert.Equal(60, persisted.GetProperty("creditDays").GetInt32());
        Assert.Equal("WAREHOUSE_RECEIPT", persisted.GetProperty("creditAnchor").GetString());
        Assert.Equal(_shipping, persisted.GetProperty("shippingMode").GetProperty("id").GetGuid());
        Assert.Equal(_incoterm, persisted.GetProperty("incoterm").GetProperty("id").GetGuid());
        Assert.Equal(_price, line.GetProperty("sourcingPriceId").GetGuid());
        Assert.Equal(3m, line.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(10m, line.GetProperty("minimumOrderQuantity").GetDecimal());
        Assert.Equal(new[] { 2, 1, 1, 0 }, new[]
        {
            line.GetProperty("supplierLeadTimeDays").GetInt32(), line.GetProperty("forwarderToShipmentDays").GetInt32(),
            line.GetProperty("shippingPeriodDays").GetInt32(), line.GetProperty("destinationCustomsDays").GetInt32()
        });
    }

    [Fact]
    public async Task Base_unit_drift_blocks_commitment_and_preserves_the_historical_snapshot()
    {
        var draft = await Create();
        var id = draft.GetProperty("id").GetGuid();
        await using var unitDb = await Db();
        var newUnit = await AddUnit(unitDb, "PO-U2");
        await using (var db = await Db())
        {
            var item = await db.Items.SingleAsync(x => x.Id == _item);
            item.Edit(item.NameFa, item.NameEn, newUnit.Id, item.Notes);
            await db.SaveChangesAsync();
        }
        var auditCount = _factory.AuditSink.Snapshot().Count;
        var response = await _client.PostAsJsonAsync($"/api/purchase-orders/{id}/approve", new
        { expectedVersion = draft.GetProperty("version").GetInt64() });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var persisted = await Get(id);
        Assert.Equal("DRAFT", persisted.GetProperty("status").GetString());
        Assert.Equal(draft.GetProperty("version").GetInt64(), persisted.GetProperty("version").GetInt64());
        Assert.Equal(_unit, persisted.GetProperty("lines")[0].GetProperty("itemBaseUnit").GetProperty("id").GetGuid());
        Assert.Equal(auditCount, _factory.AuditSink.Snapshot().Count);
    }

    [Fact]
    public async Task Schedule_grain_reconciles_without_double_counting_and_DB_rejects_mismatch()
    {
        var draft = await Create();
        var id = draft.GetProperty("id").GetGuid();
        var line = draft.GetProperty("lines")[0];
        Assert.Equal(line.GetProperty("orderedQuantity").GetDecimal(),
            line.GetProperty("schedules")[0].GetProperty("allocatedQuantity").GetDecimal());
        var split = await Put($"/api/purchase-orders/{id}/lines/{line.GetProperty("id").GetGuid()}/schedules", new
        {
            expectedVersion = draft.GetProperty("version").GetInt64(),
            schedules = new[] { new { allocatedQuantity = 5m, requestedReceiptDate = (string?)null },
                new { allocatedQuantity = 7.5m, requestedReceiptDate = (string?)null } }
        });
        var approved = await Post($"/api/purchase-orders/{id}/approve", new { expectedVersion = split.GetProperty("version").GetInt64() });
        var ordered = await Post($"/api/purchase-orders/{id}/mark-ordered", new
        { expectedVersion = approved.GetProperty("version").GetInt64(), orderedDate = "2026-08-24" });
        var firm = await Firm(new DateOnly(2026, 8, 24));
        Assert.Equal(2, firm.Count);
        Assert.Equal(12.5m, firm.Sum(x => x.GetProperty("openQuantity").GetDecimal()));
        Assert.True(PurchaseOrderStatuses.IsFirm(PurchaseOrderStatuses.Ordered));
        Assert.True(PurchaseOrderStatuses.IsFirm(PurchaseOrderStatuses.Shipped));
        Assert.True(PurchaseOrderStatuses.IsFirm(PurchaseOrderStatuses.Customs));
        Assert.True(PurchaseOrderStatuses.IsFirm(PurchaseOrderStatuses.PartiallyReceived));
        Assert.False(PurchaseOrderStatuses.IsFirm(PurchaseOrderStatuses.Approved));
        Assert.False(PurchaseOrderStatuses.IsFirm(PurchaseOrderStatuses.Received));
        Assert.False(PurchaseOrderStatuses.IsFirm(PurchaseOrderStatuses.Cancelled));
        Assert.Equal("ORDERED", ordered.GetProperty("status").GetString());
        await using var db = await Db();
        var scheduleId = ordered.GetProperty("lines")[0].GetProperty("schedules")[0].GetProperty("id").GetGuid();
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE procurement.delivery_schedules SET \"AllocatedQuantity\" = {1m} WHERE \"Id\" = {scheduleId}"));
    }

    [Fact]
    public async Task Original_plan_DB_integrity_revised_history_and_explicit_ETA_states_are_enforced()
    {
        var draft = await Create();
        var approved = await Post($"/api/purchase-orders/{draft.GetProperty("id").GetGuid()}/approve", new
        { expectedVersion = draft.GetProperty("version").GetInt64() });
        var ordered = await Post($"/api/purchase-orders/{draft.GetProperty("id").GetGuid()}/mark-ordered", new
        { expectedVersion = approved.GetProperty("version").GetInt64(), orderedDate = "2026-08-24" });
        var schedule = ordered.GetProperty("lines")[0].GetProperty("schedules")[0];
        var scheduleId = schedule.GetProperty("id").GetGuid();
        Assert.Single(schedule.GetProperty("planRevisions").EnumerateArray());
        Assert.Equal("ORIGINAL", schedule.GetProperty("planRevisions")[0].GetProperty("planType").GetString());
        Assert.Equal(1, schedule.GetProperty("planRevisions")[0].GetProperty("revisionNumber").GetInt32());
        Assert.Equal("ON_TIME", (await Firm(new DateOnly(2026, 8, 24))).Single().GetProperty("etaStatus").GetString());
        Assert.Equal("OVERDUE", (await Firm(new DateOnly(2026, 9, 1))).Single().GetProperty("etaStatus").GetString());

        await using (var db = await Db())
        {
            async Task Insert(string type, int revision) => await db.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO procurement.delivery_schedule_plan_revisions
                ("Id","DeliveryScheduleId","PlanType","RevisionNumber","WarehouseReceiptDate","Reason","CreatedAtUtc")
                VALUES ({{Guid.NewGuid()}},{{scheduleId}},{{type}},{{revision}},{{new DateOnly(2026, 9, 10)}},{{"direct test"}},{{DateTime.UtcNow}})
                """);
            await Assert.ThrowsAsync<PostgresException>(() => Insert("ORIGINAL", 1));
            await Assert.ThrowsAsync<PostgresException>(() => Insert("ORIGINAL", 2));
            await Assert.ThrowsAsync<PostgresException>(() => Insert("REVISED", 1));
            await db.Database.OpenConnectionAsync();
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT indexdef FROM pg_indexes WHERE schemaname='procurement' AND indexname='ux_delivery_schedule_single_original'";
            var originalIndex = (string)(await command.ExecuteScalarAsync())!;
            Assert.Contains("UNIQUE INDEX", originalIndex);
            Assert.Contains("WHERE", originalIndex);
            Assert.Contains("\"PlanType\"", originalIndex);
            Assert.Contains("ORIGINAL", originalIndex);
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE procurement.delivery_schedule_plan_revisions SET \"WarehouseReceiptDate\" = {new DateOnly(2030, 1, 1)} WHERE \"DeliveryScheduleId\" = {scheduleId} AND \"PlanType\" = {'O' + "RIGINAL"}"));
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM procurement.delivery_schedule_plan_revisions WHERE \"DeliveryScheduleId\" = {scheduleId} AND \"PlanType\" = {'O' + "RIGINAL"}"));
        }

        var revised2 = await Post($"/api/purchase-orders/{ordered.GetProperty("id").GetGuid()}/schedules/{scheduleId}/revise-plan", new
        { expectedVersion = ordered.GetProperty("version").GetInt64(), reason = "revision two", warehouseReceiptDate = "2026-09-02" });
        var blankReason = await _client.PostAsJsonAsync($"/api/purchase-orders/{ordered.GetProperty("id").GetGuid()}/schedules/{scheduleId}/revise-plan", new
        { expectedVersion = revised2.GetProperty("version").GetInt64(), reason = " ", warehouseReceiptDate = "2026-09-03" });
        Assert.Equal(HttpStatusCode.BadRequest, blankReason.StatusCode);
        var revised3 = await Post($"/api/purchase-orders/{ordered.GetProperty("id").GetGuid()}/schedules/{scheduleId}/revise-plan", new
        { expectedVersion = revised2.GetProperty("version").GetInt64(), reason = "revision three", warehouseReceiptDate = "2026-09-04" });
        var plans = revised3.GetProperty("lines")[0].GetProperty("schedules")[0].GetProperty("planRevisions");
        Assert.Equal(3, plans.GetArrayLength());
        Assert.Equal("2026-09-04", (await Firm(new DateOnly(2026, 9, 1))).Single().GetProperty("eta").GetString());
    }

    [Fact]
    public async Task Missing_ETA_does_not_fall_back_to_requested_actual_or_current_sourcing()
    {
        await using var setup = await Db();
        var incomplete = await AddSourcing(setup, _supplier, _currency, _unit, _shipping, _incoterm, "PO-MISSING",
            ItemTypes.Part, null, null, null, null);
        await setup.SaveChangesAsync();
        var draft = await Create(requestedReceipt: new DateOnly(2035, 1, 1), optionId: incomplete.Option, priceId: incomplete.Price);
        var approved = await Post($"/api/purchase-orders/{draft.GetProperty("id").GetGuid()}/approve", new
        { expectedVersion = draft.GetProperty("version").GetInt64() });
        var ordered = await Post($"/api/purchase-orders/{draft.GetProperty("id").GetGuid()}/mark-ordered", new
        { expectedVersion = approved.GetProperty("version").GetInt64(), orderedDate = "2026-08-24" });
        var scheduleId = ordered.GetProperty("lines")[0].GetProperty("schedules")[0].GetProperty("id").GetGuid();
        await Post($"/api/purchase-orders/{ordered.GetProperty("id").GetGuid()}/schedules/{scheduleId}/actual-milestones", new
        {
            expectedPurchaseOrderVersion = ordered.GetProperty("version").GetInt64(),
            milestoneCode = "SHIPPED",
            actualDate = "2026-08-24"
        });
        var option = await setup.SourcingOptions.SingleAsync(x => x.Id == incomplete.Option);
        option.Edit(10, _shipping, _incoterm, 1, 1, 1, 1);
        await setup.SaveChangesAsync();
        var firm = (await Firm(new DateOnly(2026, 9, 1))).Single();
        Assert.Equal(JsonValueKind.Null, firm.GetProperty("eta").ValueKind);
        Assert.Equal("MISSING", firm.GetProperty("etaSource").GetString());
        Assert.Equal("MISSING", firm.GetProperty("etaStatus").GetString());
    }

    [Fact]
    public async Task Stale_commands_and_failed_lifecycle_are_atomic_and_emit_no_successful_audit()
    {
        var draft = await Create();
        var id = draft.GetProperty("id").GetGuid();
        var originalVersion = draft.GetProperty("version").GetInt64();
        var edited = await Put($"/api/purchase-orders/{id}", new { expectedVersion = originalVersion, notes = "advanced" });
        foreach (var call in new[]
        {
            await _client.PutAsJsonAsync($"/api/purchase-orders/{id}", new { expectedVersion = originalVersion, notes = "stale" }),
            await _client.PostAsJsonAsync($"/api/purchase-orders/{id}/approve", new { expectedVersion = originalVersion })
        })
        {
            Assert.Equal(HttpStatusCode.Conflict, call.StatusCode);
            Assert.Equal("STALE_VERSION", await ProblemCode(call));
        }
        var approved = await Post($"/api/purchase-orders/{id}/approve", new { expectedVersion = edited.GetProperty("version").GetInt64() });
        var auditCount = _factory.AuditSink.Snapshot().Count;
        var lifecycleCount = approved.GetProperty("lifecycle").GetArrayLength();
        var staleOrder = await _client.PostAsJsonAsync($"/api/purchase-orders/{id}/mark-ordered", new
        { expectedVersion = edited.GetProperty("version").GetInt64(), orderedDate = "2026-08-24" });
        Assert.Equal(HttpStatusCode.Conflict, staleOrder.StatusCode);
        var unchanged = await Get(id);
        Assert.Equal("APPROVED", unchanged.GetProperty("status").GetString());
        Assert.Equal(approved.GetProperty("version").GetInt64(), unchanged.GetProperty("version").GetInt64());
        Assert.Equal(lifecycleCount, unchanged.GetProperty("lifecycle").GetArrayLength());
        Assert.Equal(auditCount, _factory.AuditSink.Snapshot().Count);
        var ordered = await Post($"/api/purchase-orders/{id}/mark-ordered", new
        { expectedVersion = approved.GetProperty("version").GetInt64(), orderedDate = "2026-08-24" });
        var scheduleId = ordered.GetProperty("lines")[0].GetProperty("schedules")[0].GetProperty("id").GetGuid();
        var staleRevision = await _client.PostAsJsonAsync($"/api/purchase-orders/{id}/schedules/{scheduleId}/revise-plan", new
        { expectedVersion = approved.GetProperty("version").GetInt64(), reason = "stale", warehouseReceiptDate = "2026-09-01" });
        Assert.Equal(HttpStatusCode.Conflict, staleRevision.StatusCode);
    }

    [Fact]
    public async Task MRP09_has_no_inventory_or_receipt_side_effect()
    {
        await using var beforeDb = await Db();
        var before = await beforeDb.InventoryTransactions.CountAsync();
        var draft = await Create();
        var approved = await Post($"/api/purchase-orders/{draft.GetProperty("id").GetGuid()}/approve", new
        { expectedVersion = draft.GetProperty("version").GetInt64() });
        await Post($"/api/purchase-orders/{draft.GetProperty("id").GetGuid()}/mark-ordered", new
        { expectedVersion = approved.GetProperty("version").GetInt64(), orderedDate = "2026-08-24" });
        await using var afterDb = await Db();
        Assert.Equal(before, await afterDb.InventoryTransactions.CountAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsJsonAsync(
            $"/api/purchase-orders/{draft.GetProperty("id").GetGuid()}/receipts", new { quantity = 1 })).StatusCode);
    }

    [Fact]
    public async Task Material_planning_supply_trace_keeps_PO_line_schedule_supplier_and_ETA_snapshot_after_live_revision()
    {
        var draft = await Create();
        var purchaseOrderId = draft.GetProperty("id").GetGuid();
        var approved = await Post($"/api/purchase-orders/{purchaseOrderId}/approve", new
        { expectedVersion = draft.GetProperty("version").GetInt64() });
        var ordered = await Post($"/api/purchase-orders/{purchaseOrderId}/mark-ordered", new
        { expectedVersion = approved.GetProperty("version").GetInt64(), orderedDate = "2026-08-24" });
        var line = ordered.GetProperty("lines")[0];
        var schedule = line.GetProperty("schedules")[0];
        var lineId = line.GetProperty("id").GetGuid();
        var scheduleId = schedule.GetProperty("id").GetGuid();
        var runId = Guid.Empty;
        var supplierNumber = string.Empty;
        try
        {
            await using (var db = await Db())
            {
                var item = await db.Items.Include(x => x.BaseUnit).SingleAsync(x => x.Id == _item);
                var supplier = await db.Suppliers.SingleAsync(x => x.Id == _supplier);
                supplierNumber = supplier.SupplierNumber;
                var run = new MrpRun($"MRP-RUN-{Random.Shared.Next(100000, 999999)}",
                    new DateOnly(2026, 8, 24), new DateOnly(2026, 9, 25), "MRP-10-v1", null, DateTime.UtcNow);
                runId = run.Id;
                db.MrpRuns.Add(run);
                db.MrpRunItemInputs.Add(new MrpRunItemInput(run.Id, item.Id, item.BusinessNumber,
                    item.ItemType, item.NameFa, item.NameEn, item.IsActive, item.BaseUnitId,
                    item.BaseUnit.Code, item.BaseUnit.NameFa, item.BaseUnit.NameEn, SupplyMethods.Buy,
                    0, 0, Guid.NewGuid(), "CAT", "گروه Snapshot", "Snapshot category", 3, 5, 10));
                db.MrpRunDailyResults.Add(new MrpRunDailyResult(run.Id, item.Id,
                    new DateOnly(2026, 8, 28), 1, 12.5m, 11.5m, -1, 0, 11.5m, false,
                    MrpCompleteness.Complete));
                db.MrpRunDailyResults.Add(new MrpRunDailyResult(run.Id, item.Id,
                    new DateOnly(2026, 8, 27), 1, 0, -1, 0, 1, 0, false,
                    MrpCompleteness.Complete));
                db.MrpRunFirmSupplyInputs.Add(new MrpRunFirmSupplyInput(run.Id, item.Id,
                    purchaseOrderId, ordered.GetProperty("purchaseOrderNumber").GetString()!, lineId,
                    scheduleId, 12.5m, new DateOnly(2026, 8, 28),
                    "ORIGINAL_PLANNED_WAREHOUSE_RECEIPT", FirmEtaStatuses.OnTime,
                    PurchaseOrderStatuses.Ordered, supplier.Id, supplier.SupplierNumber,
                    supplier.Name, null));
                await db.SaveChangesAsync();
                run.MarkSnapshotCaptured(DateTime.UtcNow);
                run.Complete(MrpCompleteness.Complete, false, DateTime.UtcNow);
                await db.SaveChangesAsync();
            }

            var beforeTrace = (await _client.GetFromJsonAsync<MrpRequirementTraceResponse>(
                $"/api/material-planning/runs/{runId}/items/{_item}/trace?demandDate=2026-08-28"))!;
            var beforeSnapshot = Assert.Single(beforeTrace.FirmSupply);

            await using (var db = await Db())
            {
                var supplier = await db.Suppliers.SingleAsync(x => x.Id == _supplier);
                supplier.Edit("تأمین‌کننده زنده تغییرکرده", "LIVE SUPPLIER CHANGED", supplier.CountryId,
                    supplier.OwnerId, supplier.Notes, supplier.ExpertLeadTimeScore, supplier.ExpertQualityScore);
                await db.SaveChangesAsync();
            }

            await Post($"/api/purchase-orders/{purchaseOrderId}/schedules/{scheduleId}/revise-plan", new
            {
                expectedVersion = ordered.GetProperty("version").GetInt64(),
                reason = "live ETA changed after MRP snapshot",
                warehouseReceiptDate = "2026-09-02"
            });

            var trace = (await _client.GetFromJsonAsync<MrpRequirementTraceResponse>(
                $"/api/material-planning/runs/{runId}/items/{_item}/trace?demandDate=2026-08-28"))!;
            var snapshot = Assert.Single(trace.FirmSupply);
            Assert.Equal(beforeSnapshot, snapshot);
            Assert.Equal(purchaseOrderId, snapshot.PurchaseOrderId);
            Assert.Equal(lineId, snapshot.PurchaseOrderLineId);
            Assert.Equal(scheduleId, snapshot.DeliveryScheduleId);
            Assert.Equal(new DateOnly(2026, 8, 28), snapshot.Eta);
            Assert.Equal(supplierNumber, snapshot.SupplierNumber);
            Assert.Equal(12.5m, snapshot.OpenQty);
            Assert.True(snapshot.AppliedByDemandDate);
            var beforeEta = (await _client.GetFromJsonAsync<MrpRequirementTraceResponse>(
                $"/api/material-planning/runs/{runId}/items/{_item}/trace?demandDate=2026-08-27"))!;
            Assert.False(Assert.Single(beforeEta.FirmSupply).AppliedByDemandDate);
        }
        finally
        {
            if (runId != Guid.Empty)
            {
                await using var cleanup = await Db();
                await cleanup.Database.ExecuteSqlRawAsync("""
                    TRUNCATE planning.mrp_run_demand_traces, planning.mrp_run_daily_results,
                      planning.mrp_run_item_summaries, planning.mrp_run_exceptions,
                      planning.mrp_run_external_source_inputs, planning.mrp_run_forecast_period_inputs,
                      planning.mrp_run_route_decisions, planning.mrp_run_route_availability_inputs,
                      planning.mrp_run_firm_supply_inputs, planning.mrp_run_inventory_inputs,
                      planning.mrp_run_bom_inputs, planning.mrp_run_forecast_inputs,
                      planning.mrp_run_item_inputs, planning.mrp_runs RESTART IDENTITY CASCADE
                    """);
            }
        }
    }

    [Fact]
    public async Task Conversion_racing_with_a_newer_expert_decision_never_converts_the_stale_decision()
    {
        var fixture = await AddExpertDecision();
        await using var writer = await Db();
        await using var transaction = await writer.Database.BeginTransactionAsync();
        await writer.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM planning.order_recommendations WHERE \"Id\"={fixture.RecommendationId} FOR UPDATE");

        var conversionTask = _client.PostAsJsonAsync("/api/purchase-orders/convert-decisions",
            new { decisionIds = new[] { fixture.DecisionId } });
        await Task.Delay(200);
        Assert.False(conversionTask.IsCompleted);

        var recommendation = await writer.OrderRecommendations.SingleAsync(x => x.Id == fixture.RecommendationId);
        var prior = await writer.OrderRecommendationDecisions.AsNoTracking()
            .SingleAsync(x => x.Id == fixture.DecisionId);
        recommendation.AdvanceDecisionVersion();
        writer.OrderRecommendationDecisions.Add(new OrderRecommendationDecision(prior.RecommendationId,
            recommendation.DecisionVersion, ExpertDecisionTypes.Accept, null, prior.DecisionDate,
            DateTime.UtcNow, "concurrent-planner", null, prior.FinalSourcingOptionId,
            prior.FinalSourcingNumber, prior.FinalSupplierId, prior.FinalSupplierNumber,
            prior.FinalSupplierNameFa, prior.FinalQty, prior.FinalTargetReceiptDate, prior.FinalOrderByDate,
            prior.FinalPriceId, prior.FinalUnitPrice, prior.FinalCurrencyId, prior.FinalCurrencyCode,
            prior.FinalMinimumOrderQuantity, prior.FinalTotalLeadTimeDays, prior.FinalBaseUnitId,
            prior.WarningCodes, prior.FinalSourceSnapshot));
        await writer.SaveChangesAsync();
        await transaction.CommitAsync();

        var conversion = await conversionTask;
        conversion.EnsureSuccessStatusCode();
        var payload = await Read(conversion);
        var result = payload.GetProperty("groups")[0].GetProperty("decisions")[0];
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Equal("DECISION_NOT_CURRENT", result.GetProperty("code").GetString());
        await using var verify = await Db();
        Assert.Equal(2, await verify.OrderRecommendationDecisions.CountAsync(
            x => x.RecommendationId == fixture.RecommendationId));
        Assert.False(await verify.PurchaseOrderLines.AnyAsync(x => x.ExpertDecisionId == fixture.DecisionId));
    }

    [Fact]
    public async Task Conversion_reports_the_complete_commercial_drift_reason_matrix_without_refreshing_snapshot()
    {
        var fixture = await AddExpertDecision();
        await using (var db = await Db())
        {
            var newUnit = await AddUnit(db, "PO-DRIFT-U");
            var newShipping = await AddReference(db, "SHIPPING_MODE", "PO-DRIFT-SHIP");
            var newIncoterm = await AddReference(db, "INCOTERM", "PO-DRIFT-INC");
            var item = await db.Items.SingleAsync(x => x.Id == _item);
            item.Edit(item.NameFa, item.NameEn, newUnit.Id, item.Notes);
            var option = await db.SourcingOptions.SingleAsync(x => x.Id == _option);
            option.Edit(11m, newShipping.Id, newIncoterm.Id, 9, 8, 7, 6);
            var price = await db.SourcingPrices.SingleAsync(x => x.Id == _price);
            price.Close(new DateOnly(2026, 8, 24));
            var supplier = await db.Suppliers.Include(x => x.PaymentProfile).SingleAsync(x => x.Id == _supplier);
            supplier.PaymentProfile.Set(supplier.PaymentProfile.PaymentTermId, 10m, 20m, 70m, 90, "ORDERED");
            await db.SaveChangesAsync();
        }

        var response = await Post("/api/purchase-orders/convert-decisions",
            new { decisionIds = new[] { fixture.DecisionId } });
        var decision = response.GetProperty("groups")[0].GetProperty("decisions")[0];
        Assert.False(decision.GetProperty("success").GetBoolean());
        Assert.Equal("DECISION_RECONFIRMATION_REQUIRED", decision.GetProperty("code").GetString());
        var reasons = decision.GetProperty("detailReasons").EnumerateArray().Select(x => x.GetString()).ToHashSet();
        foreach (var expected in new[]
        {
            "READINESS_CHANGED", "BASE_UNIT_CHANGED", "MOQ_CHANGED", "SHIPPING_MODE_CHANGED",
            "INCOTERM_CHANGED", "LEAD_TIME_CHANGED", "PAYMENT_TERMS_CHANGED", "PRICE_CHANGED"
        }) Assert.Contains(expected, reasons);
        await using var verify = await Db();
        Assert.False(await verify.PurchaseOrderLines.AnyAsync(x => x.ExpertDecisionId == fixture.DecisionId));
    }

    [Fact]
    public async Task Direct_SQL_flags_cannot_bypass_PO_schedule_receipt_or_inventory_link_integrity()
    {
        var (ordered, warehouse) = await OrderedWithWarehouse();
        var poId = ordered.GetProperty("id").GetGuid();
        var line = ordered.GetProperty("lines")[0];
        var lineId = line.GetProperty("id").GetGuid();
        var scheduleId = line.GetProperty("schedules")[0].GetProperty("id").GetGuid();
        var receipt = await CreateReceipt(poId, warehouse, lineId, scheduleId, 5m);
        var posted = await Post($"/api/purchase-receipts/{receipt.GetProperty("id").GetGuid()}/post", new
        {
            expectedReceiptVersion = receipt.GetProperty("version").GetInt64(),
            expectedPurchaseOrderVersion = ordered.GetProperty("version").GetInt64()
        });
        var receiptId = posted.GetProperty("id").GetGuid();
        var receiptLine = posted.GetProperty("lines")[0];
        var receiptLineId = receiptLine.GetProperty("id").GetGuid();

        await using var db = await Db();
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($$"""
            SELECT set_config('hadron.mrp13_operation','on',false);
            UPDATE procurement.purchase_orders SET "Status"={{"SHIPPED"}},"Version"="Version"+1 WHERE "Id"={{poId}};
            """));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($$"""
            SELECT set_config('hadron.mrp13_schedule_change','on',false);
            UPDATE procurement.delivery_schedules SET "AllocatedQuantity"={{12m}} WHERE "Id"={{scheduleId}};
            """));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($$"""
            SELECT set_config('hadron.mrp13_conversion','on',false);
            UPDATE procurement.purchase_order_lines SET "UnitPrice"={{99m}} WHERE "Id"={{lineId}};
            """));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE procurement.purchase_receipt_lines SET \"Quantity\"={4m} WHERE \"Id\"={receiptLineId}"));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE procurement.purchase_receipt_lines SET \"InventoryTransactionId\"={Guid.NewGuid()} WHERE \"Id\"={receiptLineId}"));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE procurement.purchase_receipt_lines SET \"InventoryTransactionId\"=NULL WHERE \"Id\"={receiptLineId}"));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE procurement.purchase_receipts SET \"WarehouseId\"={Guid.NewGuid()},\"Version\"=\"Version\"+1 WHERE \"Id\"={receiptId}"));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE procurement.purchase_receipts SET \"PurchaseOrderId\"={Guid.NewGuid()},\"Version\"=\"Version\"+1 WHERE \"Id\"={receiptId}"));

        async Task InsertReceipt(DateOnly date) => await db.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO procurement.purchase_receipts
              ("Id","ReceiptNumber","PurchaseOrderId","WarehouseId","EffectiveDate","Status","Version","CreatedAtUtc","UpdatedAtUtc")
            VALUES ({{Guid.NewGuid()}},{{"RCV-" + Random.Shared.Next(100000, 999999)}},{{poId}},{{warehouse}},{{date}},
              {{"DRAFT"}},{{1L}},{{DateTime.UtcNow}},{{DateTime.UtcNow}})
            """);
        await Assert.ThrowsAsync<PostgresException>(() => InsertReceipt(new DateOnly(2026, 8, 24)));
        await Assert.ThrowsAsync<PostgresException>(() => InsertReceipt(new DateOnly(2026, 8, 26)));

        var persisted = await db.PurchaseReceiptLines.AsNoTracking().SingleAsync(x => x.Id == receiptLineId);
        Assert.Equal(5m, persisted.Quantity);
        Assert.Equal(receiptLine.GetProperty("inventoryTransactionId").GetGuid(), persisted.InventoryTransactionId);
        Assert.Null(persisted.ReversalInventoryTransactionId);
    }

    [Fact]
    public async Task Receipt_and_schedule_revision_race_is_serialized_without_partial_state()
    {
        var (ordered, warehouse) = await OrderedWithWarehouse();
        var poId = ordered.GetProperty("id").GetGuid();
        var line = ordered.GetProperty("lines")[0];
        var lineId = line.GetProperty("id").GetGuid();
        var schedule = line.GetProperty("schedules")[0];
        var scheduleId = schedule.GetProperty("id").GetGuid();
        var receipt = await CreateReceipt(poId, warehouse, lineId, scheduleId, 5m);
        var version = ordered.GetProperty("version").GetInt64();
        var posting = _client.PostAsJsonAsync($"/api/purchase-receipts/{receipt.GetProperty("id").GetGuid()}/post", new
        { expectedReceiptVersion = 1, expectedPurchaseOrderVersion = version });
        var revision = _client.PostAsJsonAsync($"/api/purchase-orders/{poId}/lines/{lineId}/reallocate-schedules", new
        {
            expectedPurchaseOrderVersion = version,
            reason = "concurrent schedule date correction",
            schedules = new[] { new { allocatedQuantity = 12.5m, requestedReceiptDate = "2026-08-25" } }
        });
        var results = await Task.WhenAll(posting, revision);
        Assert.Equal(1, results.Count(x => x.IsSuccessStatusCode));
        Assert.Equal(1, results.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        await using var db = await Db();
        var postedCount = await db.PurchaseReceipts.CountAsync(x => x.Id == receipt.GetProperty("id").GetGuid() &&
            x.Status == PurchaseReceiptStatuses.Posted);
        var inventoryCount = await db.InventoryTransactions.CountAsync(x =>
            x.SourceDocumentType == InventorySourceDocumentTypes.PurchaseReceipt &&
            x.SourceDocumentId == receipt.GetProperty("lines")[0].GetProperty("id").GetGuid());
        Assert.Equal(postedCount, inventoryCount);
        Assert.InRange(await db.DeliveryScheduleAllocationEvents.CountAsync(x => x.PurchaseOrderLineId == lineId), 0, 1);
    }

    [Fact]
    public async Task Inactive_ordered_item_allows_cancellation_and_reversal_with_explicit_warning()
    {
        var (ordered, warehouse) = await OrderedWithWarehouse();
        var poId = ordered.GetProperty("id").GetGuid(); var line = ordered.GetProperty("lines")[0];
        var scheduleId = line.GetProperty("schedules")[0].GetProperty("id").GetGuid();
        var receipt = await CreateReceipt(poId, warehouse, line.GetProperty("id").GetGuid(), scheduleId, 5m);
        await using (var db = await Db())
        {
            var item = await db.Items.SingleAsync(x => x.Id == _item);
            item.ChangeState(false); await db.SaveChangesAsync();
        }
        var posted = await Post($"/api/purchase-receipts/{receipt.GetProperty("id").GetGuid()}/post", new
        { expectedReceiptVersion = 1, expectedPurchaseOrderVersion = ordered.GetProperty("version").GetInt64() });
        Assert.Contains("INACTIVE_ITEM_EXISTING_COMMITMENT",
            posted.GetProperty("warnings").EnumerateArray().Select(x => x.GetString()));
        var partial = await Get(poId);
        var cancelled = await Post($"/api/purchase-orders/{poId}/schedules/{scheduleId}/cancel", new
        {
            expectedPurchaseOrderVersion = partial.GetProperty("version").GetInt64(),
            quantity = 2m,
            effectiveDate = "2026-08-25",
            reason = "settle inactive commitment"
        });
        Assert.Contains("INACTIVE_ITEM_EXISTING_COMMITMENT",
            cancelled.GetProperty("warnings").EnumerateArray().Select(x => x.GetString()));
        var reversed = await Post($"/api/purchase-receipts/{receipt.GetProperty("id").GetGuid()}/reverse", new
        {
            expectedReceiptVersion = posted.GetProperty("version").GetInt64(),
            expectedPurchaseOrderVersion = cancelled.GetProperty("version").GetInt64(),
            reason = "reverse inactive commitment receipt"
        });
        Assert.Contains("INACTIVE_ITEM_EXISTING_COMMITMENT",
            reversed.GetProperty("warnings").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task GD09_PO_snapshot_non_drift_and_GD10_currency_isolation_use_fixed_canonical_values()
    {
        Guid cnyId; Guid usdId; Guid cnyOption; Guid cnyPrice; Guid usdOption; Guid usdPrice;
        Guid gd10CnyOption; Guid gd10CnyPrice;
        await using (var db = await Db())
        {
            var cny = new Currency("CNY", "CNY", "یوان چین", "Chinese Yuan", "¥", 2);
            var usd = new Currency("USD", "USD", "دلار آمریکا", "US Dollar", "$", 2);
            db.Currencies.AddRange(cny, usd); _createdCurrencies.AddRange([cny.Id, usd.Id]);
            var cnySource = await AddSourcing(db, _supplier, cny.Id, _unit, _shipping, _incoterm,
                "GD09-CNY", priceValue: 7m);
            var usdSource = await AddSourcing(db, _supplier, usd.Id, _unit, _shipping, _incoterm,
                "GD10-USD", priceValue: 10m);
            var gd10CnySource = await AddSourcing(db, _supplier, cny.Id, _unit, _shipping, _incoterm,
                "GD10-CNY", priceValue: 5m);
            await db.SaveChangesAsync();
            cnyId = cny.Id; usdId = usd.Id; cnyOption = cnySource.Option; cnyPrice = cnySource.Price;
            usdOption = usdSource.Option; usdPrice = usdSource.Price;
            gd10CnyOption = gd10CnySource.Option; gd10CnyPrice = gd10CnySource.Price;
        }
        var cnyDraftResponse = await CreateResponse(optionId: cnyOption, priceId: cnyPrice,
            orderedQuantity: 80m, currencyId: cnyId);
        cnyDraftResponse.EnsureSuccessStatusCode();
        var cnyDraft = await Read(cnyDraftResponse);
        var cnyCommitted = await Post($"/api/purchase-orders/{cnyDraft.GetProperty("id").GetGuid()}/approve", new
        { expectedVersion = cnyDraft.GetProperty("version").GetInt64() });
        var usdDraftResponse = await CreateResponse(optionId: usdOption, priceId: usdPrice,
            orderedQuantity: 10m, currencyId: usdId);
        usdDraftResponse.EnsureSuccessStatusCode();
        var usdDraft = await Read(usdDraftResponse);
        var gd10CnyResponse = await CreateResponse(optionId: gd10CnyOption, priceId: gd10CnyPrice,
            orderedQuantity: 100m, currencyId: cnyId);
        gd10CnyResponse.EnsureSuccessStatusCode();
        var gd10CnyDraft = await Read(gd10CnyResponse);

        await using (var db = await Db())
        {
            var cnyPriceRow = await db.SourcingPrices.SingleAsync(x => x.Id == cnyPrice);
            cnyPriceRow.Close(new DateOnly(2026, 8, 25));
            db.SourcingPrices.Add(new SourcingPrice(cnyOption, 8m, cnyId, new DateOnly(2026, 8, 26), null));
            var supplier = await db.Suppliers.Include(x => x.PaymentProfile).SingleAsync(x => x.Id == _supplier);
            supplier.PaymentProfile.Set(supplier.PaymentProfile.PaymentTermId, 10m, 20m, 70m, 90, "ORDERED");
            await db.SaveChangesAsync();
        }

        var historical = await Get(cnyCommitted.GetProperty("id").GetGuid());
        Assert.Equal("CNY", historical.GetProperty("currency").GetProperty("code").GetString());
        Assert.Equal(7m, historical.GetProperty("lines")[0].GetProperty("unitPrice").GetDecimal());
        Assert.Equal(560m, historical.GetProperty("total").GetDecimal());
        Assert.Equal("WAREHOUSE_RECEIPT", historical.GetProperty("creditAnchor").GetString());

        var usdPo = await Get(usdDraft.GetProperty("id").GetGuid());
        var gd10Cny = await Get(gd10CnyDraft.GetProperty("id").GetGuid());
        Assert.Equal("USD", usdPo.GetProperty("currency").GetProperty("code").GetString());
        Assert.Equal(100m, usdPo.GetProperty("total").GetDecimal());
        Assert.Equal("CNY", gd10Cny.GetProperty("currency").GetProperty("code").GetString());
        Assert.Equal(500m, gd10Cny.GetProperty("total").GetDecimal());
        var grouped = new Dictionary<string, decimal>
        {
            [gd10Cny.GetProperty("currency").GetProperty("code").GetString()!] = gd10Cny.GetProperty("total").GetDecimal(),
            [usdPo.GetProperty("currency").GetProperty("code").GetString()!] = usdPo.GetProperty("total").GetDecimal()
        };
        Assert.Equal(500m, grouped["CNY"]);
        Assert.Equal(100m, grouped["USD"]);
        Assert.NotEqual(600m, grouped["CNY"]);
        Assert.NotEqual(600m, grouped["USD"]);
        Assert.False(historical.TryGetProperty("grandTotal", out _));
        Assert.False(usdPo.TryGetProperty("grandTotal", out _));
    }

    private async Task<HttpResponseMessage> CreateResponse(DateOnly? requestedReceipt = null, Guid? optionId = null,
        Guid? priceId = null, decimal orderedQuantity = 12.5m, Guid? currencyId = null) =>
        await _client.PostAsJsonAsync("/api/purchase-orders", new
        {
            supplierId = _supplier,
            currencyId = currencyId ?? _currency,
            notes = "foundation",
            lines = new[] { new { sourcingOptionId = optionId ?? _option, sourcingPriceId = priceId ?? _price, orderedQuantity,
                requestedReceiptDate = requestedReceipt?.ToString("yyyy-MM-dd"), notes = (string?)null } }
        });

    private async Task<(JsonElement Ordered, Guid WarehouseId)> OrderedWithWarehouse(decimal orderedQuantity = 12.5m,
        DateOnly? orderedDate = null, Guid? optionId = null, Guid? priceId = null, Guid? currencyId = null)
    {
        Guid warehouseId;
        await using (var db = await Db())
        {
            var warehouse = new Warehouse($"WH-{Random.Shared.Next(100000, 999999)}", "انبار رسید خرید", "Purchase receipt warehouse", null);
            db.Warehouses.Add(warehouse); await db.SaveChangesAsync(); warehouseId = warehouse.Id;
        }
        var response = await CreateResponse(optionId: optionId, priceId: priceId,
            orderedQuantity: orderedQuantity, currencyId: currencyId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); var draft = await Read(response); var poId = draft.GetProperty("id").GetGuid();
        var approved = await Post($"/api/purchase-orders/{poId}/approve", new
        { expectedVersion = draft.GetProperty("version").GetInt64() });
        var ordered = await Post($"/api/purchase-orders/{poId}/mark-ordered", new
        { expectedVersion = approved.GetProperty("version").GetInt64(), orderedDate = (orderedDate ?? new DateOnly(2026, 8, 25)).ToString("yyyy-MM-dd") });
        return (ordered, warehouseId);
    }

    private async Task<JsonElement> CreateReceipt(Guid poId, Guid warehouseId, Guid lineId, Guid scheduleId,
        decimal quantity, DateOnly? effectiveDate = null)
    {
        var response = await _client.PostAsJsonAsync("/api/purchase-receipts", new
        {
            purchaseOrderId = poId,
            warehouseId,
            effectiveDate = (effectiveDate ?? new DateOnly(2026, 8, 25)).ToString("yyyy-MM-dd"),
            notes = "MRP-13 test",
            lines = new[] { new { purchaseOrderLineId = lineId, deliveryScheduleId = scheduleId, quantity } }
        });
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await Read(response);
    }

    private async Task<(Guid DecisionId, Guid RecommendationId, Guid GenerationId, Guid RunId)> AddExpertDecision()
    {
        await using var db = await Db();
        var option = await db.SourcingOptions.Include(x => x.Supplier).SingleAsync(x => x.Id == _option);
        var price = await db.SourcingPrices.SingleAsync(x => x.Id == _price);
        var currency = await db.Currencies.SingleAsync(x => x.Id == price.CurrencyId);
        var supplier = await db.Suppliers.Include(x => x.PaymentProfile).SingleAsync(x => x.Id == _supplier);
        var run = new MrpRun($"MRP-RUN-{Random.Shared.Next(100000, 999999)}", new DateOnly(2026, 8, 25),
            new DateOnly(2027, 8, 25), "MRP-10-v1", "test", DateTime.UtcNow);
        db.MrpRuns.Add(run); await db.SaveChangesAsync();
        run.MarkSnapshotCaptured(DateTime.UtcNow); run.Complete(MrpCompleteness.Complete, false, DateTime.UtcNow);
        await db.SaveChangesAsync();
        var generation = new OrderPlanningGeneration(run.Id, 1, new DateOnly(2026, 8, 25), DateTime.UtcNow, "test");
        db.OrderPlanningGenerations.Add(generation); await db.SaveChangesAsync();
        var recommendation = new OrderRecommendation(generation.Id, _item, "PRT-TEST", ItemTypes.Part,
            "قلم خرید", "Purchase Item", _unit, "PO-U", new DateOnly(2026, 9, 1), "BUY", 12.5m, 0,
            3, 5, 10, RecommendationStates.Ready, RecommendationTimingStates.OnTime,
            new DateOnly(2026, 8, 29), new DateOnly(2026, 9, 1), new DateOnly(2026, 8, 28),
            new DateOnly(2026, 8, 30), 0, 10m, 12.5m, 12.5m, RecommendationPriorities.DueNow,
            RecommendationNextActions.OrderNow, null, 37.5m, "", "PREFERRED", "{}", "MRP-12-v1");
        db.OrderRecommendations.Add(recommendation); await db.SaveChangesAsync(); recommendation.AdvanceDecisionVersion();
        var profile = supplier.PaymentProfile;
        var snapshot = JsonSerializer.Serialize(new
        {
            EvaluationDate = new DateOnly(2026, 8, 25),
            SourcingOptionId = option.Id,
            option.SourcingNumber,
            SupplierId = supplier.Id,
            supplier.SupplierNumber,
            SupplierNameFa = supplier.Name,
            SupplierNameEn = (string?)null,
            ReadinessState = "READY",
            ItemBaseUnitId = _unit,
            option.MinimumOrderQuantity,
            option.ShippingModeId,
            option.IncotermId,
            option.SupplierLeadTimeDays,
            option.ForwarderToShipmentDays,
            option.ShippingPeriodDays,
            option.DestinationCustomsDays,
            TotalLeadTimeDays = option.SupplierLeadTimeDays + option.ForwarderToShipmentDays +
                option.ShippingPeriodDays + option.DestinationCustomsDays,
            PriceId = price.Id,
            price.UnitPrice,
            CurrencyId = currency.Id,
            currency.Code,
            PaymentTermId = profile.PaymentTermId!.Value,
            PaymentTermCode = "PO-30",
            profile.AdvancePercent,
            profile.BeforeShipmentPercent,
            profile.CreditPercent,
            profile.CreditDays,
            CreditAnchor = profile.CreditAnchor
        });
        var decision = new OrderRecommendationDecision(recommendation.Id, 1, ExpertDecisionTypes.Accept, null,
            new DateOnly(2026, 8, 25), DateTime.UtcNow, "test", null, option.Id, option.SourcingNumber,
            supplier.Id, supplier.SupplierNumber, supplier.Name, 12.5m, new DateOnly(2026, 9, 1),
            new DateOnly(2026, 8, 28), price.Id, price.UnitPrice, currency.Id, currency.Code,
            option.MinimumOrderQuantity, option.SupplierLeadTimeDays + option.ForwarderToShipmentDays +
                option.ShippingPeriodDays + option.DestinationCustomsDays, _unit, "", snapshot);
        db.OrderRecommendationDecisions.Add(decision); await db.SaveChangesAsync();
        generation.Complete(DateTime.UtcNow); await db.SaveChangesAsync();
        return (decision.Id, recommendation.Id, generation.Id, run.Id);
    }

    private async Task<JsonElement> Create(DateOnly? requestedReceipt = null, Guid? optionId = null, Guid? priceId = null)
    {
        var response = await CreateResponse(requestedReceipt, optionId, priceId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await Read(response);
    }

    private async Task<JsonElement> Get(Guid id)
    {
        var response = await _client.GetAsync($"/api/purchase-orders/{id}");
        response.EnsureSuccessStatusCode();
        return await Read(response);
    }

    private async Task<JsonElement> Post(string url, object body)
    {
        var response = await _client.PostAsJsonAsync(url, body);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await Read(response);
    }

    private async Task<JsonElement> Put(string url, object body)
    {
        var response = await _client.PutAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        return await Read(response);
    }

    private async Task<List<JsonElement>> Firm(DateOnly? evaluationDate = null)
    {
        var date = evaluationDate ?? new DateOnly(2026, 8, 24);
        var response = await _client.GetAsync($"/api/purchase-orders/firm-incoming?evaluationDate={date:yyyy-MM-dd}");
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<List<JsonElement>>(await response.Content.ReadAsStringAsync(), Json())!;
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), Json());
    private static async Task<string?> ProblemCode(HttpResponseMessage response) =>
        (await Read(response)).GetProperty("code").GetString();
    private static JsonSerializerOptions Json() => new(JsonSerializerDefaults.Web);

    private async Task<HadronDbContext> Db() =>
        await _factory.Services.GetRequiredService<IDbContextFactory<HadronDbContext>>().CreateDbContextAsync();

    private async Task<(Guid Item, Guid Option, Guid Price)> AddSourcing(HadronDbContext db, Guid supplierId,
        Guid currencyId, Guid unitId, Guid shippingId, Guid incotermId, string suffix, string itemType = ItemTypes.Part,
        int? supplierLeadTimeDays = 2, int? forwarderToShipmentDays = 1, int? shippingPeriodDays = 1,
        int? destinationCustomsDays = 0, decimal priceValue = 3m)
    {
        ReferenceValue? category = null;
        if (itemType != ItemTypes.Product)
            category = await AddReference(db, itemType == ItemTypes.Part ? "PART_CATEGORY" : "COMPONENT_CATEGORY", $"{suffix}-CAT");
        var prefix = itemType == ItemTypes.Product ? "PRD" : itemType == ItemTypes.Part ? "PRT" : "CMP";
        var item = new Item(itemType, $"{prefix}-{Random.Shared.NextInt64(100000, 999999)}", "قلم خرید", "Purchase Item", unitId, null);
        db.Items.Add(item);
        if (category is not null)
            db.MaterialItemProfiles.Add(new(item.Id, category.Id, SupplyMethods.Buy, null, 0, 0, 0, 0, 0));
        var option = new SourcingOption($"SRC-{Random.Shared.NextInt64(100000, 999999)}", item.Id, supplierId, unitId,
            10m, shippingId, incotermId, supplierLeadTimeDays, forwarderToShipmentDays, shippingPeriodDays,
            destinationCustomsDays);
        db.SourcingOptions.Add(option);
        var price = new SourcingPrice(option.Id, priceValue, currencyId, new DateOnly(2026, 1, 1), null);
        db.SourcingPrices.Add(price);
        _createdItems.Add(item.Id);
        _createdOptions.Add(option.Id);
        return (item.Id, option.Id, price.Id);
    }

    private async Task<Unit> AddUnit(HadronDbContext db, string code)
    {
        var unit = new Unit($"{code}-{Guid.NewGuid():N}"[..20], $"{code}-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            "واحد سفارش", "Purchase unit", null, 1);
        db.Units.Add(unit); _createdUnits.Add(unit.Id); await db.SaveChangesAsync(); return unit;
    }

    private async Task<Currency> AddCurrency(HadronDbContext db, string prefix)
    {
        var value = Random.Shared.Next(0, 26 * 26);
        var code = $"{prefix[..1]}{(char)('A' + value / 26)}{(char)('A' + value % 26)}";
        var currency = new Currency(code, code, "ارز سفارش", "Purchase currency", code, 2);
        db.Currencies.Add(currency); _createdCurrencies.Add(currency.Id); await db.SaveChangesAsync(); return currency;
    }

    private async Task<ReferenceValue> AddReference(HadronDbContext db, string type, string code)
    {
        var typeId = await db.ReferenceTypes.Where(x => x.Code == type).Select(x => x.Id).SingleAsync();
        var actual = $"{code}-{Guid.NewGuid():N}"[..Math.Min(30, code.Length + 9)];
        var value = new ReferenceValue(typeId, actual, actual.ToUpperInvariant(), $"فارسی {code}", $"English {code}", 1);
        db.ReferenceValues.Add(value); _createdRefs.Add(value.Id); await db.SaveChangesAsync(); return value;
    }
}
