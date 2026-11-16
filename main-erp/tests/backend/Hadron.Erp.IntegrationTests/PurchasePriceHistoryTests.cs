using System.Net;
using System.Net.Http.Json;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.CashFlow.Application;
using Hadron.Erp.Api.Modules.CashFlow.Domain;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Inventory.Domain;
using Hadron.Erp.Api.Modules.PurchaseSupply.Application;
using Hadron.Erp.Api.Modules.PurchaseSupply.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Hadron.Erp.Api.Modules.Sourcing.Domain;
using Hadron.Erp.Api.Modules.Suppliers.Domain;
using Hadron.Erp.Api.Platform.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hadron.Erp.IntegrationTests;

[Collection("ReferenceData")]
public sealed class PurchasePriceHistoryTests : IClassFixture<FoundationApiFactory>, IAsyncLifetime
{
    private static readonly Guid CountryTypeId =
        Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid PaymentTermTypeId =
        Guid.Parse("10000000-0000-0000-0000-000000000006");
    private static int _sequence = 880_000;
    private static int _currencySequence;

    private readonly FoundationApiFactory _factory;

    public PurchasePriceHistoryTests(FoundationApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await using var db = await Db();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE procurement.purchase_orders RESTART IDENTITY CASCADE");
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE cash_flow.reporting_fx_rate_sets RESTART IDENTITY CASCADE");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Endpoint_is_fail_closed_and_publishes_its_independent_permission()
    {
        var routes = new[]
        {
            "/api/v1/cash-flow/purchase-price-history",
            "/api/v1/cash-flow/purchase-price-history/resolve-jalali-date",
            "/api/v1/cash-flow/purchase-price-history/filter-options/items",
            "/api/v1/cash-flow/purchase-price-history/filter-options/suppliers",
            "/api/v1/cash-flow/purchase-price-history/filter-options/values"
        };
        var endpoints = _factory.Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();
        foreach (var route in routes)
        {
            var endpoint = Assert.Single(endpoints, candidate =>
                candidate.RoutePattern.RawText == route &&
                candidate.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods
                    .Contains(HttpMethods.Get) == true);
            Assert.Equal(Permissions.CashFlowPurchasePriceHistoryView,
                endpoint.Metadata.GetMetadata<RequiredPermissionMetadata>()?.Code);
        }

        var requestPaths = new[]
        {
            "/api/v1/cash-flow/purchase-price-history?page=1&pageSize=25",
            "/api/v1/cash-flow/purchase-price-history/resolve-jalali-date?jalaliDate=14050616",
            "/api/v1/cash-flow/purchase-price-history/filter-options/items?page=1&pageSize=25",
            "/api/v1/cash-flow/purchase-price-history/filter-options/suppliers?page=1&pageSize=25",
            "/api/v1/cash-flow/purchase-price-history/filter-options/values"
        };

        using var allowed = _factory.CreateClient();
        allowed.DefaultRequestHeaders.Add("X-Hadron-Test-Permissions",
            Permissions.CashFlowPurchasePriceHistoryView);
        foreach (var requestPath in requestPaths)
        {
            using var allowedResponse = await allowed.GetAsync(requestPath);
            Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
        }
        using var invalidDate = await allowed.GetAsync(
            "/api/v1/cash-flow/purchase-price-history/resolve-jalali-date?jalaliDate=14051301");
        Assert.Equal(HttpStatusCode.BadRequest, invalidDate.StatusCode);

        using var forbidden = _factory.CreateClient();
        forbidden.DefaultRequestHeaders.Add("X-Hadron-Test-Permissions",
            Permissions.CashFlowCostingView);
        foreach (var requestPath in requestPaths)
        {
            using var forbiddenResponse = await forbidden.GetAsync(requestPath);
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
        }
    }

    [Fact]
    public async Task Endpoint_returns_401_without_an_authenticated_cookie()
    {
        var factory = new IdentityApiFactory();
        await ((IAsyncLifetime)factory).InitializeAsync();
        try
        {
            using var anonymous = factory.CreateClient();
            var requestPaths = new[]
            {
                "/api/v1/cash-flow/purchase-price-history?page=1&pageSize=25",
                "/api/v1/cash-flow/purchase-price-history/resolve-jalali-date?jalaliDate=14050616",
                "/api/v1/cash-flow/purchase-price-history/filter-options/items?page=1&pageSize=25",
                "/api/v1/cash-flow/purchase-price-history/filter-options/suppliers?page=1&pageSize=25",
                "/api/v1/cash-flow/purchase-price-history/filter-options/values"
            };
            foreach (var requestPath in requestPaths)
            {
                using var response = await anonymous.GetAsync(requestPath);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }
        }
        finally
        {
            await ((IAsyncLifetime)factory).DisposeAsync();
        }
    }

    [Fact]
    public async Task Query_keeps_one_row_per_ordered_line_and_summarizes_only_posted_receipts()
    {
        await using var db = await Db();
        var master = await AddMaster(db);
        var primary = await EnsureCurrency(db, NextCurrencyCode(), "ارز اول", "Primary");
        var secondary = await EnsureCurrency(db, NextCurrencyCode(), "ارز دوم", "Secondary");
        var firstItem = await AddItemAndOption(db, master.Unit, master.FirstSupplier,
            "قطعه تاریخچه اول");
        var secondItem = await AddItemAndOption(db, master.Unit, master.SecondSupplier,
            "قطعه تاریخچه دوم");

        var receivedOrder = await AddOrder(db, master, firstItem, master.FirstSupplier,
            primary, new DateOnly(2026, 8, 10), 100m, 6.8m);
        await AddReceipt(db, master.Warehouse, receivedOrder, 10m,
            new DateOnly(2026, 8, 12), PurchaseReceiptStatuses.Posted);
        await AddReceipt(db, master.Warehouse, receivedOrder, 15m,
            new DateOnly(2026, 8, 14), PurchaseReceiptStatuses.Posted);
        await AddReceipt(db, master.Warehouse, receivedOrder, 20m,
            new DateOnly(2026, 8, 16), PurchaseReceiptStatuses.Reversed);
        await AddReceipt(db, master.Warehouse, receivedOrder, 30m,
            new DateOnly(2026, 8, 18), PurchaseReceiptStatuses.Draft);

        var unreceivedOrder = await AddOrder(db, master, firstItem, master.FirstSupplier,
            primary, new DateOnly(2026, 8, 11), 20m, 7.1m);
        var cancelledOrder = await AddOrder(db, master, secondItem, master.SecondSupplier,
            secondary, new DateOnly(2026, 8, 9), 5m, 2m, cancelled: true);
        await AddOrder(db, master, secondItem, master.SecondSupplier, secondary,
            null, 5m, 3m);

        var page = await Service().Get(null, null, null, null, null,
            null, null, null, null, 1, 100, CancellationToken.None);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(3, page.Items.Count);
        Assert.Equal(PurchasePriceHistorySemantics.CurrentOrderedPurchaseFacts,
            page.DataSemantics);
        Assert.False(page.UsdValuationContext.Requested);
        Assert.Equal(PurchasePriceHistoryFxValuationStatuses.NotRequested,
            page.UsdValuationContext.Status);
        Assert.All(page.Items, row =>
        {
            Assert.Null(row.ValuationUnitsPerUsd);
            Assert.Null(row.ValuationUnitPriceUsd);
        });

        var received = Assert.Single(page.Items,
            row => row.PurchaseOrderLineId == receivedOrder.Line.Id);
        Assert.Equal(6.8m, received.OriginalUnitPrice);
        Assert.Equal(680m, received.OriginalOrderAmount);
        Assert.Equal(primary.Code, received.OriginalCurrencyCode);
        Assert.Equal(master.Unit.Id, received.PriceBaseUnitId);
        Assert.Equal(2, received.Receipts.PostedReceiptCount);
        Assert.Equal(25m, received.Receipts.PostedQuantity);
        Assert.Equal(master.Unit.Id, received.Receipts.QuantityUnitId);
        Assert.Equal(master.Unit.Code, received.Receipts.QuantityUnitCode);
        Assert.Equal(new DateOnly(2026, 8, 12), received.Receipts.FirstPostedReceiptDate);
        Assert.Equal(new DateOnly(2026, 8, 14), received.Receipts.LastPostedReceiptDate);

        var noReceipt = Assert.Single(page.Items,
            row => row.PurchaseOrderLineId == unreceivedOrder.Line.Id);
        Assert.Equal(0, noReceipt.Receipts.PostedReceiptCount);
        Assert.Equal(0m, noReceipt.Receipts.PostedQuantity);
        Assert.Null(noReceipt.Receipts.FirstPostedReceiptDate);
        Assert.Null(noReceipt.Receipts.LastPostedReceiptDate);

        var cancelled = Assert.Single(page.Items,
            row => row.PurchaseOrderLineId == cancelledOrder.Line.Id);
        Assert.Equal(PurchaseOrderStatuses.Cancelled, cancelled.PurchaseOrderStatus);

        var filtered = await Service().Get(firstItem.Item.BusinessNumber,
            firstItem.Item.Id, master.FirstSupplier.Id, primary.Code,
            PurchaseOrderStatuses.Ordered, new DateOnly(2026, 8, 10),
            new DateOnly(2026, 8, 11), null, null, 1, 100, CancellationToken.None);
        Assert.Equal(1, filtered.TotalCount);
        Assert.All(filtered.Items, row => Assert.Equal(firstItem.Item.Id, row.ItemId));

        var partiallyReceivedOnly = await Service().Get(null, firstItem.Item.Id,
            master.FirstSupplier.Id, primary.Code,
            PurchaseOrderStatuses.PartiallyReceived, null, null,
            null, null, 1, 100, CancellationToken.None);
        Assert.Equal(receivedOrder.Line.Id,
            Assert.Single(partiallyReceivedOnly.Items).PurchaseOrderLineId);

        var cancelledOnly = await Service().Get(null, null, master.SecondSupplier.Id,
            secondary.Code, PurchaseOrderStatuses.Cancelled, null, null,
            null, null, 1, 100, CancellationToken.None);
        Assert.Single(cancelledOnly.Items);

        var itemPage = await Service().GetItemFilterOptions(null, 1, 1,
            CancellationToken.None);
        Assert.Equal(2, itemPage.TotalCount);
        Assert.Single(itemPage.Items);
        Assert.Equal(1, itemPage.PageSize);
        var searchedItems = await Service().GetItemFilterOptions(
            firstItem.Item.BusinessNumber, 1, 25, CancellationToken.None);
        Assert.Equal(firstItem.Item.Id, Assert.Single(searchedItems.Items).ItemId);

        var supplierPage = await Service().GetSupplierFilterOptions(null, 1, 1,
            CancellationToken.None);
        Assert.Equal(2, supplierPage.TotalCount);
        Assert.Single(supplierPage.Items);
        var searchedSuppliers = await Service().GetSupplierFilterOptions(
            master.SecondSupplier.SupplierNumber, 1, 25, CancellationToken.None);
        Assert.Equal(master.SecondSupplier.Id,
            Assert.Single(searchedSuppliers.Items).SupplierId);

        var filterValues = await Service().GetFilterValues(CancellationToken.None);
        Assert.Contains(primary.Code, filterValues.CurrencyCodes);
        Assert.Contains(secondary.Code, filterValues.CurrencyCodes);
        Assert.Contains(PurchaseOrderStatuses.Ordered,
            filterValues.PurchaseOrderStatuses);
        Assert.Contains(PurchaseOrderStatuses.Cancelled,
            filterValues.PurchaseOrderStatuses);

        using var optionClient = _factory.CreateClient();
        optionClient.DefaultRequestHeaders.Add("X-Hadron-Test-Permissions",
            Permissions.CashFlowPurchasePriceHistoryView);
        var httpItems = await optionClient.GetFromJsonAsync<
            PurchasePriceHistoryItemFilterOptionPage>(
            $"/api/v1/cash-flow/purchase-price-history/filter-options/items?search={Uri.EscapeDataString(firstItem.Item.BusinessNumber)}&page=1&pageSize=1");
        Assert.Equal(firstItem.Item.Id, Assert.Single(httpItems!.Items).ItemId);
        var httpSuppliers = await optionClient.GetFromJsonAsync<
            PurchasePriceHistorySupplierFilterOptionPage>(
            $"/api/v1/cash-flow/purchase-price-history/filter-options/suppliers?search={Uri.EscapeDataString(master.FirstSupplier.SupplierNumber)}&page=1&pageSize=1");
        Assert.Equal(master.FirstSupplier.Id,
            Assert.Single(httpSuppliers!.Items).SupplierId);
        var httpValues = await optionClient.GetFromJsonAsync<PurchasePriceHistoryFilterValues>(
            "/api/v1/cash-flow/purchase-price-history/filter-options/values");
        Assert.Contains(primary.Code, httpValues!.CurrencyCodes);
        Assert.Contains(PurchaseOrderStatuses.Cancelled,
            httpValues.PurchaseOrderStatuses);
    }

    [Fact]
    public async Task Query_uses_one_explicit_published_fx_context_without_overwriting_original_money()
    {
        await using var db = await Db();
        var master = await AddMaster(db);
        var usd = await EnsureCurrency(db, "USD", "دلار", "US Dollar");
        var firstCurrency = await EnsureCurrency(db, NextCurrencyCode(), "ارز اول", "Primary");
        var secondCurrency = await EnsureCurrency(db, NextCurrencyCode(), "ارز دوم", "Secondary");
        var firstItem = await AddItemAndOption(db, master.Unit, master.FirstSupplier,
            "قطعه ارزش‌گذاری اول");
        var secondItem = await AddItemAndOption(db, master.Unit, master.SecondSupplier,
            "قطعه ارزش‌گذاری دوم");
        await AddOrder(db, master, firstItem, master.FirstSupplier, firstCurrency,
            new DateOnly(2026, 8, 10), 10m, 6.8m);
        await AddOrder(db, master, secondItem, master.SecondSupplier, secondCurrency,
            new DateOnly(2026, 8, 11), 20m, 0.86m);

        var rateSet = await PublishRateSet(db, usd, new DateOnly(2026, 8, 1),
            [(firstCurrency.Id, 6.8m), (secondCurrency.Id, 0.86m)]);

        var exact = await Service().Get(null, null, null, null, null,
            null, null, rateSet.Id, null, 1, 100, CancellationToken.None);

        Assert.True(exact.UsdValuationContext.Requested);
        Assert.Equal(PurchasePriceHistorySemantics.UsdValuationPurpose,
            exact.UsdValuationContext.Purpose);
        Assert.Equal(PurchasePriceHistoryFxSelectionModes.ExactRateSet,
            exact.UsdValuationContext.SelectionMode);
        Assert.Equal(rateSet.Id, exact.UsdValuationContext.RateSetId);
        Assert.Equal(PurchasePriceHistoryFxValuationStatuses.Complete,
            exact.UsdValuationContext.Status);
        Assert.Empty(exact.UsdValuationContext.MissingCurrencyCodes);
        Assert.Equal(2, exact.Items.Count);
        Assert.All(exact.Items, row =>
        {
            Assert.Equal(1m, row.ValuationUnitPriceUsd);
            Assert.Equal(row.OrderedQuantity, row.ValuationOrderAmountUsd);
        });
        Assert.Equal(6.8m, exact.Items.Single(row =>
            row.OriginalCurrencyId == firstCurrency.Id).OriginalUnitPrice);
        Assert.Equal(0.86m, exact.Items.Single(row =>
            row.OriginalCurrencyId == secondCurrency.Id).OriginalUnitPrice);

        var asOf = await Service().Get(null, null, null, null, null,
            null, null, null, new DateOnly(2026, 8, 31), 1, 100,
            CancellationToken.None);
        Assert.Equal(rateSet.Id, asOf.UsdValuationContext.RateSetId);
        Assert.Equal(PurchasePriceHistoryFxSelectionModes.AsOfLatestPublished,
            asOf.UsdValuationContext.SelectionMode);

        await Assert.ThrowsAsync<CashFlowValidationException>(() => Service().Get(
            null, null, null, null, null, null, null, rateSet.Id,
            new DateOnly(2026, 8, 31), 1, 100, CancellationToken.None));
    }

    [Fact]
    public async Task Requested_fx_without_an_applicable_or_complete_set_returns_null_usd_without_fallback()
    {
        await using var db = await Db();
        var master = await AddMaster(db);
        var usd = await EnsureCurrency(db, "USD", "دلار", "US Dollar");
        var configuredCurrency = await EnsureCurrency(db, NextCurrencyCode(),
            "ارز دارای نرخ", "Currency with rate");
        var configuredItem = await AddItemAndOption(db, master.Unit,
            master.FirstSupplier, "قطعه دارای نرخ گزارشی");
        await AddOrder(db, master, configuredItem, master.FirstSupplier,
            configuredCurrency, new DateOnly(2026, 8, 10), 10m, 6.8m);

        var rateSet = await PublishRateSet(db, usd, new DateOnly(2026, 8, 1),
            [(configuredCurrency.Id, 6.8m)]);

        // A currency introduced after publication is deliberately absent from the
        // selected immutable set and must make only its own USD projection incomplete.
        var missingCurrency = await EnsureCurrency(db, NextCurrencyCode(),
            "ارز بدون نرخ", "Currency without rate");
        var missingItem = await AddItemAndOption(db, master.Unit,
            master.SecondSupplier, "قطعه بدون نرخ گزارشی");
        await AddOrder(db, master, missingItem, master.SecondSupplier,
            missingCurrency, new DateOnly(2026, 8, 11), 20m, 0.86m);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Hadron-Test-Permissions",
            Permissions.CashFlowPurchasePriceHistoryView);

        using var missingSetResponse = await client.GetAsync(
            "/api/v1/cash-flow/purchase-price-history?valuationAsOfDate=2026-07-31&page=1&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, missingSetResponse.StatusCode);
        var missingSet = await missingSetResponse.Content
            .ReadFromJsonAsync<PurchasePriceHistoryPage>();
        Assert.NotNull(missingSet);
        Assert.Equal(2, missingSet.TotalCount);
        Assert.True(missingSet.UsdValuationContext.Requested);
        Assert.Equal(PurchasePriceHistorySemantics.UsdValuationPurpose,
            missingSet.UsdValuationContext.Purpose);
        Assert.Equal(PurchasePriceHistoryFxSelectionModes.AsOfLatestPublished,
            missingSet.UsdValuationContext.SelectionMode);
        Assert.Equal(new DateOnly(2026, 7, 31),
            missingSet.UsdValuationContext.ValuationAsOfDate);
        Assert.Equal(PurchasePriceHistoryFxValuationStatuses.MissingSet,
            missingSet.UsdValuationContext.Status);
        Assert.Null(missingSet.UsdValuationContext.RateSetId);
        Assert.Null(missingSet.UsdValuationContext.RateSetEffectiveDate);
        Assert.Null(missingSet.UsdValuationContext.RateSetRevision);
        Assert.Equal(2, missingSet.UsdValuationContext.MissingCurrencyCodes.Count);
        Assert.Contains(configuredCurrency.Code,
            missingSet.UsdValuationContext.MissingCurrencyCodes);
        Assert.Contains(missingCurrency.Code,
            missingSet.UsdValuationContext.MissingCurrencyCodes);
        Assert.All(missingSet.Items, row =>
        {
            Assert.True(row.OriginalUnitPrice > 0m);
            Assert.True(row.OriginalOrderAmount > 0m);
            Assert.Null(row.ValuationUnitsPerUsd);
            Assert.Null(row.ValuationUnitPriceUsd);
            Assert.Null(row.ValuationOrderAmountUsd);
            Assert.Null(row.ValuationPostedReceiptAmountUsd);
        });

        using var incompleteResponse = await client.GetAsync(
            $"/api/v1/cash-flow/purchase-price-history?valuationFxRateSetId={rateSet.Id}&page=1&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, incompleteResponse.StatusCode);
        var incomplete = await incompleteResponse.Content
            .ReadFromJsonAsync<PurchasePriceHistoryPage>();
        Assert.NotNull(incomplete);
        Assert.Equal(PurchasePriceHistoryFxSelectionModes.ExactRateSet,
            incomplete.UsdValuationContext.SelectionMode);
        Assert.Equal(PurchasePriceHistoryFxValuationStatuses.Incomplete,
            incomplete.UsdValuationContext.Status);
        Assert.Equal(rateSet.Id, incomplete.UsdValuationContext.RateSetId);
        Assert.Equal(rateSet.EffectiveDate,
            incomplete.UsdValuationContext.RateSetEffectiveDate);
        Assert.Equal(rateSet.Revision, incomplete.UsdValuationContext.RateSetRevision);
        Assert.Equal([missingCurrency.Code],
            incomplete.UsdValuationContext.MissingCurrencyCodes);

        var configuredRow = Assert.Single(incomplete.Items,
            row => row.OriginalCurrencyId == configuredCurrency.Id);
        Assert.Equal(6.8m, configuredRow.OriginalUnitPrice);
        Assert.Equal(68m, configuredRow.OriginalOrderAmount);
        Assert.Equal(6.8m, configuredRow.ValuationUnitsPerUsd);
        Assert.Equal(1m, configuredRow.ValuationUnitPriceUsd);
        Assert.Equal(10m, configuredRow.ValuationOrderAmountUsd);

        var missingRow = Assert.Single(incomplete.Items,
            row => row.OriginalCurrencyId == missingCurrency.Id);
        Assert.Equal(0.86m, missingRow.OriginalUnitPrice);
        Assert.Equal(17.2m, missingRow.OriginalOrderAmount);
        Assert.Null(missingRow.ValuationUnitsPerUsd);
        Assert.Null(missingRow.ValuationUnitPriceUsd);
        Assert.Null(missingRow.ValuationOrderAmountUsd);
        Assert.Null(missingRow.ValuationPostedReceiptAmountUsd);
    }

    private PurchasePriceHistoryService Service() => new(
        _factory.Services.GetRequiredService<IDbContextFactory<HadronDbContext>>(),
        TimeProvider.System);

    private static async Task<Master> AddMaster(HadronDbContext db)
    {
        var number = Next();
        var country = new ReferenceValue(CountryTypeId, $"PPH-C-{number}",
            $"PPH-C-{number}", "کشور تست", "Test country", 0);
        var payment = new ReferenceValue(PaymentTermTypeId, $"PPH-P-{number}",
            $"PPH-P-{number}", "پرداخت تست", "Test payment", 0);
        var unit = new Unit($"PPH-U-{number}", $"PPH-U-{number}",
            "عدد", "Piece", "pcs", 0);
        var firstSupplier = Supplier(number, "تأمین‌کننده اول", country.Id, payment.Id);
        var secondSupplier = Supplier(number + 1, "تأمین‌کننده دوم", country.Id, payment.Id);
        var warehouse = new Warehouse($"WH-{number:D4}", "انبار تست", "Test warehouse", null);
        db.AddRange(country, payment, unit, firstSupplier, secondSupplier, warehouse);
        await db.SaveChangesAsync();
        return new(country, payment, unit, firstSupplier, secondSupplier, warehouse);
    }

    private static Supplier Supplier(int number, string name, Guid countryId, Guid paymentId)
    {
        var supplier = new Supplier($"PPH-SUP-{number}", name, $"PPH-SUPPLIER-{number}",
            countryId, null, null, null, null);
        supplier.ChangeState(true);
        supplier.PaymentProfile.Set(paymentId, 100m, 0m, 0m, null, null);
        return supplier;
    }

    private static async Task<ItemOption> AddItemAndOption(HadronDbContext db, Unit unit,
        Supplier supplier, string name)
    {
        var number = Next();
        var item = new Item(ItemTypes.Component, $"CMP-{number}", name, name,
            unit.Id, null);
        var option = new SourcingOption($"PPH-SRC-{number}", item.Id, supplier.Id,
            unit.Id, SourcingRouteScopes.Domestic, null, null, null, 1, null, 1, null);
        db.AddRange(item, option);
        await db.SaveChangesAsync();
        return new(item, option);
    }

    private async Task<OrderLine> AddOrder(HadronDbContext db, Master master,
        ItemOption item, Supplier supplier, Currency currency, DateOnly? orderedDate,
        decimal quantity, decimal unitPrice, bool cancelled = false)
    {
        var number = Next();
        var order = new PurchaseOrder($"PO-{number}", supplier.Id, currency.Id,
            master.Payment.Id, 100m, 0m, 0m, null, null, null, null, null);
        var line = new PurchaseOrderLine(order.Id, 1, item.Item.Id, item.Option.Id,
            null, master.Unit.Id, quantity, unitPrice, null, 1, null, 1, null, null);
        var schedule = new DeliverySchedule(line.Id, 1, quantity, null);
        db.AddRange(order, line, schedule);
        await db.SaveChangesAsync();
        var now = new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc);
        order.Approve(now);
        await db.SaveChangesAsync();
        if (orderedDate.HasValue)
        {
            order.MarkOrdered(orderedDate.Value, now.AddMinutes(1));
            await db.SaveChangesAsync();
            if (cancelled)
            {
                using var client = _factory.CreateClient();
                using var cancelResponse = await client.PostAsJsonAsync(
                    $"/api/purchase-orders/{order.Id}/schedules/{schedule.Id}/cancel",
                    new CancelScheduleRequest(order.Version, quantity, orderedDate.Value,
                        "purchase-price-history cancellation test"));
                Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
                await db.Entry(order).ReloadAsync();
            }
        }
        return new(order, line, schedule, item.Item, master.Unit);
    }

    private async Task AddReceipt(HadronDbContext db, Warehouse warehouse,
        OrderLine order, decimal quantity, DateOnly date, string finalStatus)
    {
        using var client = _factory.CreateClient();
        using var createResponse = await client.PostAsJsonAsync("/api/purchase-receipts/",
            new CreatePurchaseReceiptRequest(order.Order.Id, warehouse.Id, date,
                "purchase-price-history-test",
                [new PurchaseReceiptLineInput(order.Line.Id, order.Schedule.Id, quantity)]));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var receipt = await createResponse.Content.ReadFromJsonAsync<PurchaseReceiptResponse>();
        Assert.NotNull(receipt);
        if (finalStatus is PurchaseReceiptStatuses.Posted or PurchaseReceiptStatuses.Reversed)
        {
            await db.Entry(order.Order).ReloadAsync();
            using var postResponse = await client.PostAsJsonAsync(
                $"/api/purchase-receipts/{receipt!.Id}/post",
                new PostPurchaseReceiptRequest(receipt.Version, order.Order.Version));
            Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
            receipt = await postResponse.Content.ReadFromJsonAsync<PurchaseReceiptResponse>();
            Assert.NotNull(receipt);
        }
        if (finalStatus == PurchaseReceiptStatuses.Reversed)
        {
            await db.Entry(order.Order).ReloadAsync();
            using var reverseResponse = await client.PostAsJsonAsync(
                $"/api/purchase-receipts/{receipt!.Id}/reverse",
                new ReversePurchaseReceiptRequest(receipt.Version, order.Order.Version,
                    "reversed test receipt"));
            Assert.Equal(HttpStatusCode.OK, reverseResponse.StatusCode);
        }
    }

    private static async Task<Currency> EnsureCurrency(HadronDbContext db, string code,
        string nameFa, string nameEn)
    {
        var existing = await db.Currencies.SingleOrDefaultAsync(currency => currency.Code == code);
        if (existing is not null) return existing;
        var currency = new Currency(code, code, nameFa, nameEn, null, 2);
        db.Currencies.Add(currency);
        await db.SaveChangesAsync();
        return currency;
    }

    private static async Task<ReportingFxRateSet> PublishRateSet(HadronDbContext db,
        Currency usd, DateOnly effectiveDate,
        IReadOnlyCollection<(Guid CurrencyId, decimal UnitsPerUsd)> rates)
    {
        var set = new ReportingFxRateSet(usd.Id, effectiveDate, 1, null,
            Guid.NewGuid(), new string('A', 64), "purchase price history test",
            "purchase-price-history-test",
            new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc), rates);
        db.ReportingFxRateSets.Add(set);
        await db.SaveChangesAsync();
        set.Publish("purchase price history test publish", "purchase-price-history-test",
            new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync();
        return set;
    }

    private async Task<HadronDbContext> Db() => await _factory.Services
        .GetRequiredService<IDbContextFactory<HadronDbContext>>()
        .CreateDbContextAsync();

    private static int Next() => Interlocked.Increment(ref _sequence);

    private static string NextCurrencyCode()
    {
        var value = Interlocked.Increment(ref _currencySequence) - 1;
        return $"R{(char)('A' + value / 26 % 26)}{(char)('A' + value % 26)}";
    }

    private sealed record Master(ReferenceValue Country, ReferenceValue Payment, Unit Unit,
        Supplier FirstSupplier, Supplier SecondSupplier, Warehouse Warehouse);
    private sealed record ItemOption(Item Item, SourcingOption Option);
    private sealed record OrderLine(PurchaseOrder Order, PurchaseOrderLine Line,
        DeliverySchedule Schedule, Item Item, Unit Unit);
}
