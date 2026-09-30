using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Inventory.Application;
using Hadron.Erp.Api.Modules.Inventory.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Hadron.Erp.IntegrationTests;

[Collection("ReferenceData")]
public sealed class InventoryTests : IClassFixture<FoundationApiFactory>, IAsyncLifetime
{
    private readonly FoundationApiFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _items = [];
    private readonly List<Guid> _units = [];
    private readonly List<Guid> _referenceValues = [];
    private Guid _responsibleUserId;

    public InventoryTests(FoundationApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await using var db = await Db();
        await db.Database.MigrateAsync();
        TestDatabaseSafety.EnsureSafeDatabaseName(db.Database.GetDbConnection().Database);
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE inventory.transactions, inventory.warehouses RESTART IDENTITY CASCADE");
        _responsibleUserId = await IntegrationTestIdentity.EnsureActiveWarehouseResponsible(db, "inventory");
        _factory.AuditSink.Clear();
    }

    public async Task DisposeAsync()
    {
        await using var db = await Db();
        TestDatabaseSafety.EnsureSafeDatabaseName(db.Database.GetDbConnection().Database);
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE inventory.transactions, inventory.warehouses RESTART IDENTITY CASCADE");
        await db.InventoryItemUnitConversions.Where(x => _items.Contains(x.ItemId)).ExecuteDeleteAsync();
        await db.MaterialItemProfiles.Where(x => _items.Contains(x.ItemId)).ExecuteDeleteAsync();
        await db.Products.Where(x => _items.Contains(x.ItemId)).ExecuteDeleteAsync();
        await db.Items.Where(x => _items.Contains(x.Id)).ExecuteDeleteAsync();
        await db.ReferenceValues.Where(x => _referenceValues.Contains(x.Id)).ExecuteDeleteAsync();
        await db.Units.Where(x => _units.Contains(x.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Warehouse_code_is_generated_concurrently_immutable_and_lifecycle_is_versioned()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(index =>
            _client.PostAsJsonAsync("/api/inventory/warehouses", new
            {
                code = "WH-MANUAL",
                nameFa = $"انبار {index}",
                nameEn = $"Warehouse {index}",
                addressFa = $"موقعیت {index}",
                addressEn = $"Location {index}",
                primaryResponsibleUserId = _responsibleUserId,
                notes = (string?)null
            })));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var rows = await Task.WhenAll(responses.Select(x => x.Content.ReadFromJsonAsync<WarehouseDto>()));
        Assert.Equal(4, rows.Select(x => x!.Code).Distinct().Count());
        Assert.All(rows, row => Assert.Matches("^WH-[0-9]{4,}$", row!.Code));
        Assert.DoesNotContain(rows, row => row!.Code == "WH-MANUAL");

        var warehouse = rows[0]!;
        var stale = await _client.PutAsJsonAsync($"/api/inventory/warehouses/{warehouse.Id}", new
        { nameFa = "قدیمی", nameEn = "Stale", addressFa = "موقعیت قدیمی", addressEn = "Stale location", primaryResponsibleUserId = _responsibleUserId, notes = (string?)null, expectedVersion = warehouse.Version + 99 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("STALE_VERSION", await Code(stale));
        var editedResponse = await _client.PutAsJsonAsync($"/api/inventory/warehouses/{warehouse.Id}", new
        { nameFa = "انبار ویرایش", nameEn = "Edited warehouse", addressFa = "موقعیت ویرایش‌شده", addressEn = "Edited location", primaryResponsibleUserId = _responsibleUserId, notes = "note", expectedVersion = warehouse.Version });
        editedResponse.EnsureSuccessStatusCode();
        var edited = (await editedResponse.Content.ReadFromJsonAsync<WarehouseDto>())!;
        Assert.Equal(warehouse.Code, edited.Code);
        Assert.True(edited.Version > warehouse.Version);
        var deactivate = await _client.PostAsJsonAsync($"/api/inventory/warehouses/{warehouse.Id}/deactivate",
            new { expectedVersion = edited.Version, reason = "unused" });
        deactivate.EnsureSuccessStatusCode();
        var inactive = (await deactivate.Content.ReadFromJsonAsync<WarehouseDto>())!;
        Assert.False(inactive.IsActive);
        var reactivate = await _client.PostAsJsonAsync($"/api/inventory/warehouses/{warehouse.Id}/reactivate",
            new { expectedVersion = inactive.Version, reason = "needed" });
        reactivate.EnsureSuccessStatusCode();

        await using var db = await Db();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE inventory.warehouses SET \"BusinessNumber\" = {'W' + "H-9999"} WHERE \"Id\" = {warehouse.Id}"));
        Assert.Equal("ck_warehouse_code_immutable", exception.ConstraintName);
        Assert.Equal(8, await db.ReferenceTypes.CountAsync());
    }

    [Fact]
    public async Task Ledger_supports_all_item_types_canonical_codes_fractional_balances_and_as_of()
    {
        var unit = await AddUnit("واحد موجودی", "Inventory unit");
        var product = await AddItem(ItemTypes.Product, unit.Id, "محصول", "Product");
        var part = await AddItem(ItemTypes.Part, unit.Id, "قطعه", "Part");
        var component = await AddItem(ItemTypes.Component, unit.Id, "جزء", null);
        var warehouse = await CreateWarehouse("انبار اصلی", "Main warehouse");
        var day1 = new DateOnly(2025, 1, 1);
        var day2 = day1.AddDays(1);
        var day3 = day2.AddDays(1);

        await Post("openings", product, warehouse.Id, 10.125001m, day1);
        await Post("receipts", product, warehouse.Id, 2.5m, day2);
        await Post("issues", product, warehouse.Id, 1.125001m, day3);
        await Adjustment(product, warehouse.Id, "IN", 1.25m, day3, "count in");
        await Adjustment(product, warehouse.Id, "OUT", .75m, day3, "count out");
        await Post("openings", part, warehouse.Id, 3m, day1);
        await Post("openings", component, warehouse.Id, 4m, day1);

        var asOf1 = await Balances(day1);
        Assert.Equal(10.125001m, asOf1.Items.Single(x => x.Item.Id == product).ActualOnHand);
        var current = await Balances(day3);
        Assert.Equal(12m, current.Items.Single(x => x.Item.Id == product).ActualOnHand);
        Assert.Equal(3m, current.Items.Single(x => x.Item.Id == part).ActualOnHand);
        Assert.Equal(4m, current.Items.Single(x => x.Item.Id == component).ActualOnHand);
        Assert.All(current.Items, row => Assert.Equal(unit.Id, row.Item.BaseUnit.Id));

        var ledger = await _client.GetFromJsonAsync<TransactionListDto>(
            "/api/inventory/transactions?page=1&pageSize=100");
        Assert.Equal(["ADJ_IN", "ADJ_OUT", "ISSUE", "OPENING", "RECEIPT"],
            ledger!.Items.Select(x => x.TransactionType).Distinct().Order().ToArray());
        Assert.DoesNotContain(ledger.Items, x => x.TransactionType.Contains("ADJUSTMENT"));
        Assert.All(ledger.Items, x => Assert.Matches("^(PRD|PRT|CMP)-[0-9]{4,}$", x.Item.Code));

        var unfilteredLedger = await _client.GetFromJsonAsync<TransactionListDto>(
            "/api/inventory/transactions?transactionType=&page=1&pageSize=100");
        Assert.Equal(ledger.Items.Count(), unfilteredLedger!.Items.Count());
    }

    [Fact]
    public async Task Opening_is_positive_historically_unique_and_reversed_opening_cannot_be_replaced()
    {
        var unit = await AddUnit("عدد", "Piece");
        var item = await AddItem(ItemTypes.Part, unit.Id, "قطعه افتتاحیه", "Opening part");
        var warehouse = await CreateWarehouse("انبار افتتاحیه", "Opening warehouse");
        var date = new DateOnly(2025, 2, 1);
        var zero = await _client.PostAsJsonAsync("/api/inventory/openings",
            new { itemId = item, warehouseId = warehouse.Id, quantity = 0, effectiveDate = date, reason = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        var opening = await Post("openings", item, warehouse.Id, 10, date);
        var reversal = await Reverse(opening.Id, "wrong opening");
        Assert.Equal(date, reversal.EffectiveDate);
        Assert.Equal(opening.Id, reversal.OriginalTransactionId);
        var duplicate = await _client.PostAsJsonAsync("/api/inventory/openings",
            new { itemId = item, warehouseId = warehouse.Id, quantity = 12, effectiveDate = date, reason = (string?)null });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("OPENING_ALREADY_EXISTS", await Code(duplicate));
        await Adjustment(item, warehouse.Id, "IN", 12, date.AddDays(1), "corrected current stock");
        Assert.Equal(12, (await Balances(date.AddDays(1))).Items.Single().ActualOnHand);
    }

    [Fact]
    public async Task Transfer_is_two_step_company_neutral_reversible_and_same_warehouse_is_rejected()
    {
        var unit = await AddUnit("کیلوگرم", "Kilogram");
        var item = await AddItem(ItemTypes.Component, unit.Id, "ماده", "Material");
        var source = await CreateWarehouse("مبدأ", "Source");
        var destination = await CreateWarehouse("مقصد", "Destination");
        var date = new DateOnly(2025, 3, 1);
        await Post("openings", item, source.Id, 20, date);
        var transferResponse = await _client.PostAsJsonAsync("/api/inventory/transfers", new
        {
            itemId = item,
            sourceWarehouseId = source.Id,
            destinationWarehouseId = destination.Id,
            quantity = 7.5m,
            reason = "move"
        });
        Assert.Equal(HttpStatusCode.Created, transferResponse.StatusCode);
        var draft = (await transferResponse.Content.ReadFromJsonAsync<TransferDto>())!;
        Assert.Equal("DRAFT", draft.Status);
        var dispatchResponse = await _client.PostAsJsonAsync($"/api/inventory/transfers/{draft.Id}/dispatch",
            new { effectiveDate = date, expectedVersion = draft.Version, reason = "loaded" });
        dispatchResponse.EnsureSuccessStatusCode();
        var dispatched = (await dispatchResponse.Content.ReadFromJsonAsync<TransferDto>())!;
        Assert.Equal("DISPATCHED", dispatched.Status);
        var detail = await _client.GetFromJsonAsync<ItemPositionDto>(
            $"/api/inventory/items/{item}/position?asOfDate={date:yyyy-MM-dd}");
        Assert.Equal(20, detail!.CompanyOwnedTotal);
        Assert.Equal(12.5m, detail.UsablePhysicalTotal);
        Assert.Equal(7.5m, detail.InTransitTotal);
        Assert.Equal(12.5m, detail.Warehouses.Single(x => x.Warehouse.Id == source.Id).ActualOnHand);

        var partialResponse = await _client.PostAsJsonAsync($"/api/inventory/transfers/{draft.Id}/receive",
            new { quantity = 3m, effectiveDate = date, expectedVersion = dispatched.Version, reason = "first pallet" });
        partialResponse.EnsureSuccessStatusCode();
        var partial = (await partialResponse.Content.ReadFromJsonAsync<TransferDto>())!;
        Assert.Equal("PARTIALLY_RECEIVED", partial.Status);
        Assert.Equal(3m, partial.ReceivedQuantity);
        Assert.Equal(4.5m, partial.InTransitQuantity);
        var overReceipt = await _client.PostAsJsonAsync($"/api/inventory/transfers/{draft.Id}/receive",
            new { quantity = 5m, effectiveDate = date, expectedVersion = partial.Version, reason = "too much" });
        Assert.Equal(HttpStatusCode.Conflict, overReceipt.StatusCode);
        Assert.Equal("TRANSFER_OVER_RECEIPT", await Code(overReceipt));
        var receiveResponse = await _client.PostAsJsonAsync($"/api/inventory/transfers/{draft.Id}/receive",
            new { quantity = 4.5m, effectiveDate = date, expectedVersion = partial.Version, reason = "final pallet" });
        receiveResponse.EnsureSuccessStatusCode();
        var received = (await receiveResponse.Content.ReadFromJsonAsync<TransferDto>())!;
        Assert.Equal("RECEIVED", received.Status);
        Assert.Equal(2, received.Receipts.Length);
        detail = await _client.GetFromJsonAsync<ItemPositionDto>(
            $"/api/inventory/items/{item}/position?asOfDate={date:yyyy-MM-dd}");
        Assert.Equal(20, detail!.CompanyOwnedTotal);
        Assert.Equal(20, detail.UsablePhysicalTotal);
        Assert.Equal(0, detail.InTransitTotal);
        Assert.Equal(7.5m, detail.Warehouses.Single(x => x.Warehouse.Id == destination.Id).ActualOnHand);

        var reverseFinalResponse = await _client.PostAsJsonAsync(
            $"/api/inventory/transfers/{draft.Id}/receipts/{received.Receipts[1].Id}/reverse",
            new { expectedVersion = received.Version, reason = "wrong receipt" });
        reverseFinalResponse.EnsureSuccessStatusCode();
        var partiallyReversed = (await reverseFinalResponse.Content.ReadFromJsonAsync<TransferDto>())!;
        Assert.Equal("PARTIALLY_RECEIVED", partiallyReversed.Status);
        var blockedCancel = await _client.PostAsJsonAsync($"/api/inventory/transfers/{draft.Id}/cancel",
            new { expectedVersion = partiallyReversed.Version, reason = "still received" });
        Assert.Equal(HttpStatusCode.Conflict, blockedCancel.StatusCode);
        Assert.Equal("PARTIALLY_RECEIVED_TRANSFER_CANNOT_BE_CANCELLED", await Code(blockedCancel));
        var reverseFirstResponse = await _client.PostAsJsonAsync(
            $"/api/inventory/transfers/{draft.Id}/receipts/{received.Receipts[0].Id}/reverse",
            new { expectedVersion = partiallyReversed.Version, reason = "reverse first pallet" });
        reverseFirstResponse.EnsureSuccessStatusCode();
        var reversed = (await reverseFirstResponse.Content.ReadFromJsonAsync<TransferDto>())!;
        Assert.Equal("DISPATCHED", reversed.Status);
        var cancelResponse = await _client.PostAsJsonAsync($"/api/inventory/transfers/{draft.Id}/cancel",
            new { expectedVersion = reversed.Version, reason = "return to source" });
        cancelResponse.EnsureSuccessStatusCode();
        var cancelled = (await cancelResponse.Content.ReadFromJsonAsync<TransferDto>())!;
        Assert.Equal("CANCELLED", cancelled.Status);
        detail = await _client.GetFromJsonAsync<ItemPositionDto>(
            $"/api/inventory/items/{item}/position?asOfDate={date:yyyy-MM-dd}");
        Assert.Equal(20, detail!.CompanyOwnedTotal);
        Assert.Equal(20, detail.UsablePhysicalTotal);
        Assert.Equal(0, detail.InTransitTotal);
        Assert.Equal(20, detail.Warehouses.Single(x => x.Warehouse.Id == source.Id).ActualOnHand);

        var invalid = await _client.PostAsJsonAsync("/api/inventory/transfers", new
        {
            itemId = item,
            sourceWarehouseId = source.Id,
            destinationWarehouseId = source.Id,
            quantity = 1,
            reason = (string?)null
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        await using var db = await Db();
        Assert.Equal(7, await db.InventoryTransactions.CountAsync());
        Assert.Single(await db.InventoryTransfers.ToListAsync());
        Assert.Equal(2, await db.InventoryTransferReceipts.CountAsync());
        var immutableTransfer = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE inventory.transfers SET \"RequestedQuantity\" = {99m} WHERE \"Id\" = {draft.Id}"));
        Assert.Equal("ck_inventory_transfer_immutable", immutableTransfer.ConstraintName);
        var immutableReceipt = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM inventory.transfer_receipts WHERE \"Id\" = {received.Receipts[0].Id}"));
        Assert.Equal("ck_inventory_transfer_receipt_immutable", immutableReceipt.ConstraintName);
        var directLegacyTransfer = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO inventory.transactions
                  ("Id","TransactionType","ItemId","WarehouseId","DestinationWarehouseId",
                   "Quantity","UnitId","EnteredQuantity","TransactionUnitId",
                   "ConversionFactorToBase","EffectiveDate")
                VALUES ({Guid.NewGuid()}, {'T' + "RANSFER"}, {item}, {source.Id}, {destination.Id},
                        {1m}, {unit.Id}, {1m}, {unit.Id}, {1m}, {date})
                """));
        Assert.Equal("ck_inventory_transfer_workflow_required", directLegacyTransfer.ConstraintName);
    }

    [Fact]
    public async Task Negative_stock_guard_covers_current_concurrent_backdated_and_direct_sql_writes()
    {
        var unit = await AddUnit("عدد منفی", "Negative unit");
        var item = await AddItem(ItemTypes.Part, unit.Id, "قطعه منفی", "Negative part");
        var warehouse = await CreateWarehouse("انبار منفی", "Negative warehouse");
        var day1 = new DateOnly(2025, 4, 1);
        var day2 = day1.AddDays(1);
        var day3 = day2.AddDays(1);
        await Post("openings", item, warehouse.Id, 10, day1);
        await Post("issues", item, warehouse.Id, 8, day3);

        foreach (var (date, quantity) in new[] { (day1.AddDays(-1), 1m), (day2, 3m), (day3, 3m) })
        {
            var response = await _client.PostAsJsonAsync("/api/inventory/issues",
                new { itemId = item, warehouseId = warehouse.Id, quantity, effectiveDate = date, reason = (string?)null });
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("NEGATIVE_STOCK_NOT_ALLOWED", await Code(response));
        }
        Assert.Equal(2, (await Balances(day3)).Items.Single().ActualOnHand);

        var concurrentItem = await AddItem(ItemTypes.Component, unit.Id, "همزمان", "Concurrent");
        await Post("openings", concurrentItem, warehouse.Id, 10, day1);
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            _client.PostAsJsonAsync("/api/inventory/issues", new
            {
                itemId = concurrentItem,
                warehouseId = warehouse.Id,
                quantity = 7,
                effectiveDate = day1,
                reason = (string?)null
            })));
        Assert.Equal(1, concurrent.Count(x => x.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, concurrent.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal("NEGATIVE_STOCK_NOT_ALLOWED",
            await Code(concurrent.Single(x => x.StatusCode == HttpStatusCode.Conflict)));

        await using var db = await Db();
        await using var tx = await db.Database.BeginTransactionAsync();
        var direct = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO inventory.transactions
              ("Id","TransactionType","ItemId","WarehouseId","Quantity","UnitId","EffectiveDate")
            VALUES ({Guid.NewGuid()}, {'I' + "SSUE"}, {item}, {warehouse.Id}, {3m}, {unit.Id}, {day2})
            """));
        Assert.Equal("ck_inventory_nonnegative", direct.ConstraintName);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Reversal_is_exact_single_use_non_reversible_and_preserves_original()
    {
        var unit = await AddUnit("واحد برگشت", "Reversal unit");
        var item = await AddItem(ItemTypes.Product, unit.Id, "محصول برگشت", "Reversal product");
        var warehouse = await CreateWarehouse("انبار برگشت", "Reversal warehouse");
        var date = new DateOnly(2025, 5, 1);
        var receipt = await Post("receipts", item, warehouse.Id, 6, date);
        var reversal = await Reverse(receipt.Id, "duplicate receipt");
        Assert.Equal("REVERSAL", reversal.TransactionType);
        Assert.Equal(receipt.Quantity, reversal.Quantity);
        Assert.Equal(receipt.Unit.Id, reversal.Unit.Id);
        Assert.Equal(receipt.EffectiveDate, reversal.EffectiveDate);
        var second = await _client.PostAsJsonAsync($"/api/inventory/transactions/{receipt.Id}/reverse",
            new { reason = "again" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("TRANSACTION_ALREADY_REVERSED", await Code(second));
        var reverseReversal = await _client.PostAsJsonAsync(
            $"/api/inventory/transactions/{reversal.Id}/reverse", new { reason = "invalid" });
        Assert.Equal(HttpStatusCode.Conflict, reverseReversal.StatusCode);
        Assert.Equal("REVERSAL_CANNOT_BE_REVERSED", await Code(reverseReversal));
        var original = await _client.GetFromJsonAsync<TransactionDto>(
            $"/api/inventory/transactions/{receipt.Id}");
        Assert.Equal("RECEIPT", original!.TransactionType);

        await using var db = await Db();
        var mutation = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE inventory.transactions SET \"Quantity\" = {99m} WHERE \"Id\" = {receipt.Id}"));
        Assert.Equal("ck_inventory_posted_immutable", mutation.ConstraintName);
        var deletion = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM inventory.transactions WHERE \"Id\" = {receipt.Id}"));
        Assert.Equal("ck_inventory_posted_immutable", deletion.ConstraintName);
        Assert.Equal(HttpStatusCode.MethodNotAllowed,
            (await _client.DeleteAsync($"/api/inventory/transactions/{receipt.Id}")).StatusCode);
    }

    [Fact]
    public async Task Future_posting_unit_snapshot_and_item_base_unit_lock_are_enforced_at_api_and_db()
    {
        var unit = await AddUnit("واحد ثابت", "Stable unit");
        var replacement = await AddUnit("واحد جدید", "Replacement unit");
        var item = await AddItem(ItemTypes.Part, unit.Id, "قطعه ثابت", "Stable part");
        var warehouse = await CreateWarehouse("انبار ثابت", "Stable warehouse");
        var businessDate = await _client.GetFromJsonAsync<BusinessDateDto>("/api/planning/business-date");
        var today = await Post("receipts", item, warehouse.Id, 1, businessDate!.TehranBusinessDate);
        Assert.Equal(unit.Id, today.Unit.Id);
        var future = await _client.PostAsJsonAsync("/api/inventory/receipts", new
        {
            itemId = item,
            warehouseId = warehouse.Id,
            quantity = 1,
            effectiveDate = businessDate.TehranBusinessDate.AddDays(1),
            reason = (string?)null
        });
        Assert.Equal(HttpStatusCode.Conflict, future.StatusCode);
        Assert.Equal("FUTURE_INVENTORY_POSTING_NOT_ALLOWED", await Code(future));

        await using var db = await Db();
        var unitMutation = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE catalog.items SET \"BaseUnitId\" = {replacement.Id} WHERE \"Id\" = {item}"));
        Assert.Equal("ck_item_base_unit_inventory", unitMutation.ConstraintName);
        var wrongSnapshot = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO inventory.transactions
              ("Id","TransactionType","ItemId","WarehouseId","Quantity","UnitId","EffectiveDate")
            VALUES ({Guid.NewGuid()}, {'R' + "ECEIPT"}, {item}, {warehouse.Id}, {1m}, {replacement.Id}, {businessDate.TehranBusinessDate})
            """));
        Assert.Equal("ck_inventory_base_unit", wrongSnapshot.ConstraintName);

        var categoryResponse = await _client.PostAsJsonAsync(
            "/api/reference-data/types/PART_CATEGORY/values", new
            {
                code = $"INV-CAT-{Guid.NewGuid():N}"[..20],
                nameFa = "گروه قفل موجودی",
                nameEn = "Inventory lock category",
                sortOrder = 0
            });
        categoryResponse.EnsureSuccessStatusCode();
        using var categoryJson = JsonDocument.Parse(await categoryResponse.Content.ReadAsStringAsync());
        var categoryId = categoryJson.RootElement.GetProperty("id").GetGuid();
        _referenceValues.Add(categoryId);
        var partResponse = await _client.PostAsJsonAsync("/api/parts", new
        {
            nameFa = "قطعه قفل API",
            nameEn = "API locked part",
            categoryId,
            baseUnitId = unit.Id,
            supplyMethod = "BUY",
            netWeightKg = (decimal?)null,
            defaultPlanningScrapPercent = 0,
            safetyStockQty = 0,
            criticalDays = 0,
            safeStartDays = 0,
            excessStartDays = 0,
            notes = (string?)null
        });
        partResponse.EnsureSuccessStatusCode();
        using var partJson = JsonDocument.Parse(await partResponse.Content.ReadAsStringAsync());
        var part = partJson.RootElement.GetProperty("item");
        var partId = part.GetProperty("id").GetGuid();
        var partVersion = part.GetProperty("version").GetInt64();
        _items.Add(partId);
        await Post("receipts", partId, warehouse.Id, 1, businessDate.TehranBusinessDate);
        _factory.AuditSink.Clear();
        var apiUnitChange = await _client.PutAsJsonAsync($"/api/parts/{partId}", new
        {
            nameFa = "قطعه قفل API",
            nameEn = "API locked part",
            categoryId,
            baseUnitId = replacement.Id,
            supplyMethod = "BUY",
            netWeightKg = (decimal?)null,
            defaultPlanningScrapPercent = 0,
            safetyStockQty = 0,
            criticalDays = 0,
            safeStartDays = 0,
            excessStartDays = 0,
            notes = (string?)null,
            expectedVersion = partVersion
        });
        Assert.Equal(HttpStatusCode.Conflict, apiUnitChange.StatusCode);
        Assert.Equal("ITEM_BASE_UNIT_LOCKED_BY_INVENTORY", await Code(apiUnitChange));
        Assert.DoesNotContain(_factory.AuditSink.Snapshot(),
            x => x.EntityId == partId && x.Action == "EDIT");
    }

    [Fact]
    public async Task Warehouse_deactivation_blocks_stock_but_inactive_reversal_stock_counts_with_warning()
    {
        var unit = await AddUnit("واحد انبار", "Warehouse unit");
        var item = await AddItem(ItemTypes.Component, unit.Id, "جزء انبار", "Warehouse component");
        var warehouse = await CreateWarehouse("انبار چرخه", "Lifecycle warehouse");
        var date = new DateOnly(2025, 6, 1);
        await Post("openings", item, warehouse.Id, 10, date);
        var blocked = await _client.PostAsJsonAsync($"/api/inventory/warehouses/{warehouse.Id}/deactivate",
            new { expectedVersion = warehouse.Version, reason = "try" });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("WAREHOUSE_HAS_NONZERO_STOCK", await Code(blocked));
        var issue = await Post("issues", item, warehouse.Id, 10, date);
        var deactivated = await _client.PostAsJsonAsync($"/api/inventory/warehouses/{warehouse.Id}/deactivate",
            new { expectedVersion = warehouse.Version, reason = "empty" });
        deactivated.EnsureSuccessStatusCode();
        var inactive = (await deactivated.Content.ReadFromJsonAsync<WarehouseDto>())!;
        Assert.False(inactive.IsActive);
        await Reverse(issue.Id, "controlled correction");
        var position = await _client.GetFromJsonAsync<ItemPositionDto>(
            $"/api/inventory/items/{item}/position?asOfDate={date:yyyy-MM-dd}");
        Assert.Equal(10, position!.CompanyOwnedTotal);
        Assert.True(position.HasInactiveWarehouseStockWarning);
        Assert.True(position.Warehouses.Single().InactiveWarehouseStockWarning);
    }

    [Fact]
    public async Task Historical_inactive_references_remain_bilingual_and_public_posting_cannot_forge_source()
    {
        var unit = await AddUnit("واحد فارسی", "English unit");
        var item = await AddItem(ItemTypes.Product, unit.Id, "نام فارسی", "English item");
        var warehouse = await CreateWarehouse("انبار فارسی", "English warehouse");
        var date = new DateOnly(2025, 7, 1);
        var response = await _client.PostAsJsonAsync("/api/inventory/receipts", new
        {
            itemId = item,
            warehouseId = warehouse.Id,
            quantity = 2,
            effectiveDate = date,
            reason = (string?)null,
            sourceDocumentType = "PURCHASE_RECEIPT",
            sourceDocumentId = Guid.NewGuid()
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var transaction = (await response.Content.ReadFromJsonAsync<TransactionDto>())!;
        Assert.Null(transaction.SourceDocumentType);
        Assert.Null(transaction.SourceDocumentId);
        await using (var db = await Db())
        {
            var storedUnit = await db.Units.SingleAsync(x => x.Id == unit.Id);
            storedUnit.SetActive(false);
            storedUnit.AdvanceVersion();
            var storedItem = await db.Items.SingleAsync(x => x.Id == item);
            storedItem.ChangeState(false);
            await db.SaveChangesAsync();
        }
        var historical = await _client.GetFromJsonAsync<TransactionDto>(
            $"/api/inventory/transactions/{transaction.Id}");
        Assert.Equal("نام فارسی", historical!.Item.NameFa);
        Assert.Equal("English item", historical.Item.NameEn);
        Assert.Equal("واحد فارسی", historical.Unit.NameFa);
        Assert.Equal("English unit", historical.Unit.NameEn);
        Assert.False(historical.Unit.IsActive);
        var blocked = await _client.PostAsJsonAsync("/api/inventory/receipts", new
        {
            itemId = item,
            warehouseId = warehouse.Id,
            quantity = 1,
            effectiveDate = date,
            reason = (string?)null
        });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("INACTIVE_ITEM", await Code(blocked));
    }

    [Fact]
    public async Task Shared_item_lock_serializes_first_receipt_and_base_unit_change_repeatedly()
    {
        var originalUnit = await AddUnit("واحد مسابقه قلم", "Item race unit");
        var replacementUnit = await AddUnit("واحد جایگزین مسابقه", "Replacement race unit");
        var date = new DateOnly(2025, 9, 1);

        for (var iteration = 0; iteration < 3; iteration++)
        {
            var receiptFirstItem = await AddItem(ItemTypes.Part, originalUnit.Id,
                $"قلم دریافت اول {iteration}", $"Receipt first {iteration}");
            var receiptFirstWarehouse = await CreateWarehouse(
                $"انبار دریافت اول {iteration}", $"Receipt first warehouse {iteration}");

            await using (var postingConnection = await OpenConnection())
            await using (var postingTransaction = await postingConnection.BeginTransactionAsync())
            {
                await InsertDirectReceipt(postingConnection, postingTransaction, receiptFirstItem,
                    receiptFirstWarehouse.Id, originalUnit.Id, date);
                var baseUnitChange = ChangeBaseUnit(receiptFirstItem, replacementUnit.Id);
                await Task.Delay(100);
                Assert.False(baseUnitChange.IsCompleted);
                await postingTransaction.CommitAsync();
                var blocked = await Assert.ThrowsAsync<PostgresException>(async () =>
                {
                    await baseUnitChange;
                });
                Assert.Equal("ck_item_base_unit_inventory", blocked.ConstraintName);
            }

            await using (var db = await Db())
            {
                Assert.Equal(originalUnit.Id,
                    await db.Items.Where(x => x.Id == receiptFirstItem).Select(x => x.BaseUnitId).SingleAsync());
                Assert.Equal(originalUnit.Id, await db.InventoryTransactions
                    .Where(x => x.ItemId == receiptFirstItem).Select(x => x.UnitId).SingleAsync());
            }

            var unitFirstItem = await AddItem(ItemTypes.Component, originalUnit.Id,
                $"قلم واحد اول {iteration}", $"Unit first {iteration}");
            var unitFirstWarehouse = await CreateWarehouse(
                $"انبار واحد اول {iteration}", $"Unit first warehouse {iteration}");
            await using (var unitConnection = await OpenConnection())
            await using (var unitTransaction = await unitConnection.BeginTransactionAsync())
            {
                await Execute(unitConnection, unitTransaction,
                    "UPDATE catalog.items SET \"BaseUnitId\" = @unit WHERE \"Id\" = @item",
                    ("unit", replacementUnit.Id), ("item", unitFirstItem));
                var receipt = _client.PostAsJsonAsync("/api/inventory/receipts", new
                {
                    itemId = unitFirstItem,
                    warehouseId = unitFirstWarehouse.Id,
                    quantity = 1,
                    effectiveDate = date,
                    reason = (string?)null
                });
                await Task.Delay(100);
                Assert.False(receipt.IsCompleted);
                await unitTransaction.CommitAsync();
                var response = await receipt;
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                var posted = (await response.Content.ReadFromJsonAsync<TransactionDto>())!;
                Assert.Equal(replacementUnit.Id, posted.Unit.Id);
            }
        }
    }

    [Fact]
    public async Task Shared_warehouse_lock_serializes_receipt_and_deactivation_repeatedly()
    {
        var unit = await AddUnit("واحد مسابقه انبار", "Warehouse race unit");
        var date = new DateOnly(2025, 10, 1);

        for (var iteration = 0; iteration < 3; iteration++)
        {
            var receiptFirstItem = await AddItem(ItemTypes.Product, unit.Id,
                $"قلم انبار دریافت اول {iteration}", $"Warehouse receipt first {iteration}");
            var receiptFirstWarehouse = await CreateWarehouse(
                $"انبار مسابقه دریافت اول {iteration}", $"Warehouse receipt first {iteration}");
            _factory.AuditSink.Clear();

            await using (var postingConnection = await OpenConnection())
            await using (var postingTransaction = await postingConnection.BeginTransactionAsync())
            {
                await InsertDirectReceipt(postingConnection, postingTransaction, receiptFirstItem,
                    receiptFirstWarehouse.Id, unit.Id, date);
                var deactivation = _client.PostAsJsonAsync(
                    $"/api/inventory/warehouses/{receiptFirstWarehouse.Id}/deactivate",
                    new { expectedVersion = receiptFirstWarehouse.Version, reason = "race" });
                await Task.Delay(100);
                Assert.False(deactivation.IsCompleted);
                await postingTransaction.CommitAsync();
                var response = await deactivation;
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
                Assert.Equal("WAREHOUSE_HAS_NONZERO_STOCK", await Code(response));
            }

            var persistedWarehouse = await _client.GetFromJsonAsync<WarehouseDto>(
                $"/api/inventory/warehouses/{receiptFirstWarehouse.Id}");
            Assert.True(persistedWarehouse!.IsActive);
            Assert.DoesNotContain(_factory.AuditSink.Snapshot(),
                x => x.EntityId == receiptFirstWarehouse.Id && x.Action == "DEACTIVATE");

            var deactivateFirstItem = await AddItem(ItemTypes.Part, unit.Id,
                $"قلم انبار تعطیلی اول {iteration}", $"Warehouse deactivate first {iteration}");
            var deactivateFirstWarehouse = await CreateWarehouse(
                $"انبار تعطیلی اول {iteration}", $"Warehouse deactivate first {iteration}");
            _factory.AuditSink.Clear();
            await using (var warehouseConnection = await OpenConnection())
            await using (var warehouseTransaction = await warehouseConnection.BeginTransactionAsync())
            {
                await Execute(warehouseConnection, warehouseTransaction,
                    "UPDATE inventory.warehouses SET \"IsActive\" = false WHERE \"Id\" = @warehouse",
                    ("warehouse", deactivateFirstWarehouse.Id));
                var receipt = _client.PostAsJsonAsync("/api/inventory/receipts", new
                {
                    itemId = deactivateFirstItem,
                    warehouseId = deactivateFirstWarehouse.Id,
                    quantity = 1,
                    effectiveDate = date,
                    reason = (string?)null
                });
                await Task.Delay(100);
                Assert.False(receipt.IsCompleted);
                await warehouseTransaction.CommitAsync();
                var response = await receipt;
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
                Assert.Equal("INACTIVE_WAREHOUSE", await Code(response));
            }

            await using var db = await Db();
            Assert.False(await db.Warehouses.Where(x => x.Id == deactivateFirstWarehouse.Id)
                .Select(x => x.IsActive).SingleAsync());
            Assert.False(await db.InventoryTransactions.AnyAsync(x =>
                x.ItemId == deactivateFirstItem && x.WarehouseId == deactivateFirstWarehouse.Id));
            Assert.DoesNotContain(_factory.AuditSink.Snapshot(),
                x => x.EntityType == "InventoryTransaction");
        }
    }

    [Fact]
    public async Task Warehouse_search_supports_code_fa_en_and_bounded_server_paging()
    {
        var first = await CreateWarehouse("انبار جستجوی ویژه", "Distinct search warehouse");
        await CreateWarehouse("انبار صفحه مشترک یک", "Paged warehouse one");
        await CreateWarehouse("انبار صفحه مشترک دو", "Paged warehouse two");
        await CreateWarehouse("انبار صفحه مشترک سه", "Paged warehouse three");

        var byCode = await Warehouses(first.Code, 1, 25);
        Assert.Single(byCode.Items);
        Assert.Equal(first.Id, byCode.Items[0].Id);
        var byFa = await Warehouses("جستجوی ویژه", 1, 25);
        Assert.Single(byFa.Items);
        Assert.Equal(first.Id, byFa.Items[0].Id);
        var byEn = await Warehouses("Distinct search", 1, 25);
        Assert.Single(byEn.Items);
        Assert.Equal(first.Id, byEn.Items[0].Id);

        var page1 = await Warehouses("Paged warehouse", 1, 2);
        var page2 = await Warehouses("Paged warehouse", 2, 2);
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.Items.Length);
        Assert.Single(page2.Items);
        Assert.Empty(page1.Items.Select(x => x.Id).Intersect(page2.Items.Select(x => x.Id)));
    }

    [Fact]
    public async Task Transaction_unit_conversion_is_exact_snapshotted_and_supports_piece_to_weight()
    {
        var kilogram = await AddUnit("کیلوگرم تبدیل", "Conversion kilogram");
        var piece = await AddUnit("عدد تبدیل", "Conversion piece");
        var item = await AddItem(ItemTypes.Part, kilogram.Id, "بدنه چهارده گرمی", "Fourteen gram body");
        var warehouse = await CreateWarehouse("انبار تبدیل واحد", "Unit conversion warehouse");

        var create = await _client.PostAsJsonAsync($"/api/inventory/items/{item}/units", new
        {
            transactionUnitId = piece.Id,
            factorToBase = .014m
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var conversion = (await create.Content.ReadFromJsonAsync<UnitConversionDto>())!;
        Assert.Equal(.014m, conversion.FactorToBase);
        Assert.True(conversion.IsActive);

        var firstResponse = await _client.PostAsJsonAsync("/api/inventory/receipts", new
        {
            itemId = item,
            warehouseId = warehouse.Id,
            quantity = 1000m,
            transactionUnitId = piece.Id,
            effectiveDate = new DateOnly(2025, 11, 1),
            reason = (string?)null
        });
        firstResponse.EnsureSuccessStatusCode();
        var first = (await firstResponse.Content.ReadFromJsonAsync<TransactionDto>())!;
        Assert.Equal(1000m, first.EnteredQuantity);
        Assert.Equal(piece.Id, first.TransactionUnit.Id);
        Assert.Equal(.014m, first.ConversionFactorToBase);
        Assert.Equal(14m, first.Quantity);
        Assert.Equal(kilogram.Id, first.Unit.Id);

        var edit = await _client.PutAsJsonAsync(
            $"/api/inventory/items/{item}/units/{conversion.Id}", new
            {
                factorToBase = .015m,
                expectedVersion = conversion.Version
            });
        edit.EnsureSuccessStatusCode();
        var changed = (await edit.Content.ReadFromJsonAsync<UnitConversionDto>())!;
        Assert.Equal(.015m, changed.FactorToBase);

        var secondResponse = await _client.PostAsJsonAsync("/api/inventory/receipts", new
        {
            itemId = item,
            warehouseId = warehouse.Id,
            quantity = 1000m,
            transactionUnitId = piece.Id,
            effectiveDate = new DateOnly(2025, 11, 2),
            reason = (string?)null
        });
        secondResponse.EnsureSuccessStatusCode();
        var second = (await secondResponse.Content.ReadFromJsonAsync<TransactionDto>())!;
        Assert.Equal(15m, second.Quantity);
        Assert.Equal(.015m, second.ConversionFactorToBase);

        var persistedFirst = await _client.GetFromJsonAsync<TransactionDto>(
            $"/api/inventory/transactions/{first.Id}");
        Assert.Equal(14m, persistedFirst!.Quantity);
        Assert.Equal(.014m, persistedFirst.ConversionFactorToBase);
        Assert.Equal(29m, (await Balances(new DateOnly(2025, 11, 2))).Items.Single().ActualOnHand);
    }

    [Fact]
    public async Task Production_staging_is_company_owned_but_excluded_from_usable_mrp_stock()
    {
        var unit = await AddUnit("واحد انبار تولید", "Production warehouse unit");
        var item = await AddItem(ItemTypes.Component, unit.Id, "ماده تولید", "Production material");
        var source = await CreateWarehouse("انبار مواد", "Material storage", "STORAGE");
        var staging = await CreateWarehouse("پای خط تولید", "Production staging", "PRODUCTION_STAGING");
        var date = new DateOnly(2025, 12, 1);
        await Post("openings", item, source.Id, 100m, date);

        var create = await _client.PostAsJsonAsync("/api/inventory/transfers", new
        {
            itemId = item,
            sourceWarehouseId = source.Id,
            destinationWarehouseId = staging.Id,
            quantity = 40m,
            reason = "آماده‌سازی تولید"
        });
        create.EnsureSuccessStatusCode();
        var transfer = (await create.Content.ReadFromJsonAsync<TransferDto>())!;
        var dispatch = await _client.PostAsJsonAsync($"/api/inventory/transfers/{transfer.Id}/dispatch",
            new { effectiveDate = date, expectedVersion = transfer.Version, reason = "تحویل به خط" });
        dispatch.EnsureSuccessStatusCode();
        transfer = (await dispatch.Content.ReadFromJsonAsync<TransferDto>())!;
        var receive = await _client.PostAsJsonAsync($"/api/inventory/transfers/{transfer.Id}/receive",
            new { quantity = 40m, effectiveDate = date, expectedVersion = transfer.Version, reason = "دریافت پای خط" });
        receive.EnsureSuccessStatusCode();

        var position = await _client.GetFromJsonAsync<ItemPositionDto>(
            $"/api/inventory/items/{item}/position?asOfDate={date:yyyy-MM-dd}");
        Assert.Equal(100m, position!.CompanyOwnedTotal);
        Assert.Equal(60m, position.UsablePhysicalTotal);
        Assert.Equal(40m, position.ProductionStagingTotal);
        Assert.Equal(0m, position.InTransitTotal);
        Assert.False(position.Warehouses.Single(x => x.Warehouse.Id == staging.Id)
            .Warehouse.IncludeInMrpAvailable);

        var filtered = await _client.GetFromJsonAsync<WarehouseListDto>(
            "/api/inventory/warehouses?warehouseType=PRODUCTION_STAGING&page=1&pageSize=25");
        Assert.Single(filtered!.Items);
        Assert.Equal(staging.Id, filtered.Items[0].Id);
        Assert.Equal("PRODUCTION_STAGING", filtered.Items[0].WarehouseType);

        await using var db = await Db();
        var forbidden = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE inventory.warehouses SET \"WarehouseType\" = {'S' + "TORAGE"}, \"IncludeInMrpAvailable\" = true WHERE \"Id\" = {staging.Id}"));
        Assert.Equal("ck_warehouse_type_immutable_with_history", forbidden.ConstraintName);
    }

    [Fact]
    public async Task Failed_mutations_emit_no_success_audit_and_post_commit_sink_failure_keeps_transaction()
    {
        var unit = await AddUnit("واحد ممیزی", "Audit unit");
        var item = await AddItem(ItemTypes.Part, unit.Id, "قطعه ممیزی", "Audit part");
        var warehouse = await CreateWarehouse("انبار ممیزی", "Audit warehouse");
        _factory.AuditSink.Clear();
        var failed = await _client.PostAsJsonAsync("/api/inventory/issues", new
        {
            itemId = item,
            warehouseId = warehouse.Id,
            quantity = 1,
            effectiveDate = new DateOnly(2025, 8, 1),
            reason = (string?)null
        });
        Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode);
        Assert.DoesNotContain(_factory.AuditSink.Snapshot(), x => x.EntityType == "InventoryTransaction");
        _factory.AuditSink.ThrowOnWrite = true;
        var receipt = await _client.PostAsJsonAsync("/api/inventory/receipts", new
        {
            itemId = item,
            warehouseId = warehouse.Id,
            quantity = 1,
            effectiveDate = new DateOnly(2025, 8, 1),
            reason = (string?)null
        });
        Assert.Equal(HttpStatusCode.Created, receipt.StatusCode);
        _factory.AuditSink.ThrowOnWrite = false;
        Assert.Equal(1, (await Balances(new DateOnly(2025, 8, 1))).Items.Single().ActualOnHand);
    }

    private async Task<Unit> AddUnit(string nameFa, string nameEn)
    {
        await using var db = await Db();
        var unit = new Unit($"INV-{Guid.NewGuid():N}"[..20], $"INV-{Guid.NewGuid():N}"[..20],
            nameFa, nameEn, "u", 0);
        db.Units.Add(unit);
        await db.SaveChangesAsync();
        _units.Add(unit.Id);
        return unit;
    }

    private async Task<Guid> AddItem(string type, Guid unitId, string nameFa, string? nameEn)
    {
        await using var db = await Db();
        var prefix = type == ItemTypes.Product ? "PRD" : type == ItemTypes.Part ? "PRT" : "CMP";
        var item = new Item(type, $"{prefix}-{Random.Shared.NextInt64(1_000_000, 9_999_999)}",
            nameFa, nameEn, unitId, null);
        db.Items.Add(item);
        await db.SaveChangesAsync();
        _items.Add(item.Id);
        return item.Id;
    }

    private async Task<WarehouseDto> CreateWarehouse(string nameFa, string? nameEn,
        string? warehouseType = null)
    {
        var response = await _client.PostAsJsonAsync("/api/inventory/warehouses",
            new { nameFa, nameEn, addressFa = $"موقعیت {nameFa}", addressEn = $"Location {nameEn}", primaryResponsibleUserId = _responsibleUserId, notes = (string?)null, warehouseType });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WarehouseDto>())!;
    }

    private async Task<TransactionDto> Post(string endpoint, Guid itemId, Guid warehouseId,
        decimal quantity, DateOnly effectiveDate)
    {
        var response = await _client.PostAsJsonAsync($"/api/inventory/{endpoint}",
            new { itemId, warehouseId, quantity, effectiveDate, reason = (string?)null });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TransactionDto>())!;
    }

    private async Task<TransactionDto> Adjustment(Guid itemId, Guid warehouseId, string direction,
        decimal quantity, DateOnly effectiveDate, string reason)
    {
        var response = await _client.PostAsJsonAsync("/api/inventory/adjustments",
            new { itemId, warehouseId, direction, quantity, effectiveDate, reason });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TransactionDto>())!;
    }

    private async Task<TransactionDto> Reverse(Guid id, string reason)
    {
        var response = await _client.PostAsJsonAsync($"/api/inventory/transactions/{id}/reverse",
            new { reason });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TransactionDto>())!;
    }

    private async Task<WarehouseListDto> Warehouses(string search, int page, int pageSize) =>
        (await _client.GetFromJsonAsync<WarehouseListDto>(
            $"/api/inventory/warehouses?search={Uri.EscapeDataString(search)}&page={page}&pageSize={pageSize}"))!;

    private async Task<NpgsqlConnection> OpenConnection()
    {
        await using var db = await Db();
        var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync();
        return connection;
    }

    private async Task<int> ChangeBaseUnit(Guid itemId, Guid unitId)
    {
        await using var connection = await OpenConnection();
        await using var command = new NpgsqlCommand(
            "UPDATE catalog.items SET \"BaseUnitId\" = @unit WHERE \"Id\" = @item", connection);
        command.Parameters.AddWithValue("unit", unitId);
        command.Parameters.AddWithValue("item", itemId);
        return await command.ExecuteNonQueryAsync();
    }

    private async Task InsertDirectReceipt(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid itemId, Guid warehouseId, Guid unitId, DateOnly effectiveDate) =>
        await Execute(connection, transaction, """
            INSERT INTO inventory.transactions
              ("Id","TransactionType","ItemId","WarehouseId","Quantity","UnitId",
               "EnteredQuantity","TransactionUnitId","ConversionFactorToBase","EffectiveDate")
            VALUES (@id,'RECEIPT',@item,@warehouse,1,@unit,1,@unit,1,@date)
            """, ("id", Guid.NewGuid()), ("item", itemId), ("warehouse", warehouseId),
            ("unit", unitId), ("date", effectiveDate));

    private static async Task<int> Execute(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        return await command.ExecuteNonQueryAsync();
    }

    private Task<BalanceListDto> Balances(DateOnly date) =>
        _client.GetFromJsonAsync<BalanceListDto>(
            $"/api/inventory/balances?asOfDate={date:yyyy-MM-dd}&page=1&pageSize=100")!;

    private static async Task<string?> Code(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private async Task<HadronDbContext> Db()
    {
        var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDbContextFactory<HadronDbContext>>()
            .CreateDbContextAsync();
    }

    private sealed record UnitDto(Guid Id, string Code, string NameFa, string? NameEn,
        string? Symbol, bool IsActive);
    private sealed record ItemDto(Guid Id, string Code, string ItemType, string NameFa,
        string? NameEn, bool IsActive, UnitDto BaseUnit);
    private sealed record WarehouseDto(Guid Id, string Code, string NameFa, string? NameEn,
        string WarehouseType, bool IncludeInMrpAvailable, bool IsActive, string? Notes, long Version);
    private sealed record WarehouseListDto(WarehouseDto[] Items, int Page, int PageSize, int TotalCount);
    private sealed record TransactionDto(Guid Id, string TransactionType, ItemDto Item,
        WarehouseDto Warehouse, WarehouseDto? DestinationWarehouse, decimal Quantity, UnitDto Unit,
        decimal EnteredQuantity, UnitDto TransactionUnit, decimal ConversionFactorToBase,
        DateOnly EffectiveDate, string? Reason, Guid? OriginalTransactionId,
        string? SourceDocumentType, Guid? SourceDocumentId);
    private sealed record TransactionListDto(TransactionDto[] Items, int TotalCount);
    private sealed record BalanceRowDto(ItemDto Item, WarehouseDto Warehouse, decimal ActualOnHand,
        bool InactiveWarehouseStockWarning);
    private sealed record BalanceListDto(DateOnly AsOfDate, BalanceRowDto[] Items, int TotalCount);
    private sealed record ItemWarehouseBalanceDto(WarehouseDto Warehouse, decimal ActualOnHand,
        bool InactiveWarehouseStockWarning);
    private sealed record ItemPositionDto(DateOnly AsOfDate, ItemDto Item, decimal UsablePhysicalTotal,
        decimal ProductionStagingTotal, decimal InTransitTotal, decimal CompanyOwnedTotal,
        bool HasInactiveWarehouseStockWarning,
        ItemWarehouseBalanceDto[] Warehouses);
    private sealed record TransferReceiptDto(Guid Id, decimal Quantity, decimal EnteredQuantity,
        UnitDto TransactionUnit, decimal ConversionFactorToBase, DateOnly EffectiveDate,
        Guid InventoryTransactionId, bool IsReversed);
    private sealed record TransferDto(Guid Id, string Code, string Status, ItemDto Item, UnitDto Unit,
        WarehouseDto SourceWarehouse, WarehouseDto DestinationWarehouse, decimal RequestedQuantity,
        decimal EnteredRequestedQuantity, UnitDto TransactionUnit, decimal ConversionFactorToBase,
        decimal DispatchedQuantity, decimal ReceivedQuantity, decimal InTransitQuantity,
        DateOnly? DispatchDate, long Version, TransferReceiptDto[] Receipts);
    private sealed record UnitConversionDto(Guid? Id, UnitDto TransactionUnit, UnitDto BaseUnit,
        decimal FactorToBase, bool IsBaseUnit, bool IsActive, long Version);
    private sealed record BusinessDateDto(DateOnly TehranBusinessDate, int JalaliYear, int JalaliMonth);
}
