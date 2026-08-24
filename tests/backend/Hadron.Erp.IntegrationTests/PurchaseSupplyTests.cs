using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Catalog.Domain;
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
    }

    public async Task DisposeAsync()
    {
        await using var db = await Db();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE procurement.purchase_orders RESTART IDENTITY CASCADE");
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
        await setup.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO procurement.delivery_schedule_actual_milestones
            ("Id","DeliveryScheduleId","MilestoneCode","ActualDate","SourceType","SourceId","CreatedAtUtc")
            VALUES ({{Guid.NewGuid()}},{{scheduleId}},{{"WAREHOUSE_RECEIPT"}},{{new DateOnly(2026, 8, 30)}},{{"RECEIPT"}},{{Guid.NewGuid()}},{{DateTime.UtcNow}})
            """);
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

    private async Task<HttpResponseMessage> CreateResponse(DateOnly? requestedReceipt = null, Guid? optionId = null,
        Guid? priceId = null) =>
        await _client.PostAsJsonAsync("/api/purchase-orders", new
        {
            supplierId = _supplier,
            currencyId = _currency,
            notes = "foundation",
            lines = new[] { new { sourcingOptionId = optionId ?? _option, sourcingPriceId = priceId ?? _price, orderedQuantity = 12.5m,
                requestedReceiptDate = requestedReceipt?.ToString("yyyy-MM-dd"), notes = (string?)null } }
        });

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
        int? destinationCustomsDays = 0)
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
        var price = new SourcingPrice(option.Id, 3m, currencyId, new DateOnly(2026, 1, 1), null);
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
