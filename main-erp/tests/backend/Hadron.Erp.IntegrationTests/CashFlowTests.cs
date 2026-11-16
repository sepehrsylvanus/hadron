using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Bom.Domain;
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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Hadron.Erp.IntegrationTests;

[Collection("ReferenceData")]
public sealed class CashFlowTests : IClassFixture<FoundationApiFactory>, IAsyncLifetime
{
    private static readonly Guid CountryTypeId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid PaymentTermTypeId = Guid.Parse("10000000-0000-0000-0000-000000000006");
    private static int _businessSequence = 700_000;
    private static int _currencySequence;

    private readonly FoundationApiFactory _factory;
    private readonly HttpClient _client;

    public CashFlowTests(FoundationApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await using var db = await Db();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE procurement.purchase_orders RESTART IDENTITY CASCADE");
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE engineering.bom_revisions RESTART IDENTITY CASCADE");
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE cash_flow.reporting_fx_rate_sets RESTART IDENTITY CASCADE");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Endpoints_are_fail_closed_and_publish_exact_permission_metadata()
    {
        var cases = new[]
        {
            ("/api/v1/cash-flow/dashboard", HttpMethods.Get, Permissions.CashFlowDashboardView),
            ("/api/v1/cash-flow/dashboard/resolve-jalali-date", HttpMethods.Get, Permissions.CashFlowDashboardView),
            ("/api/v1/cash-flow/obligations", HttpMethods.Get, Permissions.CashFlowObligationView),
            ("/api/v1/cash-flow/obligations/resolve-jalali-date", HttpMethods.Get, Permissions.CashFlowObligationView),
            ("/api/v1/cash-flow/costed-boms", HttpMethods.Get, Permissions.CashFlowCostingView),
            ("/api/v1/cash-flow/costed-boms/resolve-jalali-date", HttpMethods.Get, Permissions.CashFlowCostingView),
            ("/api/v1/cash-flow/costed-boms/{parentItemId:guid}", HttpMethods.Get, Permissions.CashFlowCostingView),
        };
        var endpoints = _factory.Services.GetServices<EndpointDataSource>().SelectMany(x => x.Endpoints)
            .OfType<RouteEndpoint>().ToArray();

        foreach (var (route, method, permission) in cases)
        {
            var endpoint = Assert.Single(endpoints, candidate =>
                candidate.RoutePattern.RawText == route &&
                candidate.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(method) == true);
            Assert.Equal(permission,
                endpoint.Metadata.GetMetadata<RequiredPermissionMetadata>()?.Code);
        }

        var cashFlowEndpoints = endpoints.Where(candidate => cases.Any(expected =>
            candidate.RoutePattern.RawText == expected.Item1 &&
            candidate.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods
                .Contains(expected.Item2) == true))
            .ToArray();
        Assert.Equal(cases.Length, cashFlowEndpoints.Length);
        Assert.All(cashFlowEndpoints, endpoint =>
            Assert.Equal([HttpMethods.Get],
                endpoint.Metadata.GetRequiredMetadata<IHttpMethodMetadata>().HttpMethods));

        await using var db = await Db();
        var master = await AddMaster(db);
        var root = await AddItem(db, ItemTypes.Product, master.PieceUnit, "محصول مجوز جریان نقدی");
        var leaf = await AddPurchasableItem(db, master, master.PieceUnit, "قطعه مجوز جریان نقدی");
        var bom = new BomRevision(NextBomNumber(), root.Id, master.PieceUnit.Id, 1m, null);
        db.AddRange(bom, new BomLine(bom.Id, leaf.Item.Id, master.PieceUnit.Id, 1m, 0m, 1));
        await db.SaveChangesAsync();
        bom.Approve("cash-flow-auth-test");
        await db.SaveChangesAsync();

        var runtimeCases = new[]
        {
            ("/api/v1/cash-flow/dashboard?asOfDate=2026-08-01&horizonEnd=2026-09-01",
                Permissions.CashFlowDashboardView),
            ("/api/v1/cash-flow/dashboard/resolve-jalali-date?jalaliDate=14050616",
                Permissions.CashFlowDashboardView),
            ("/api/v1/cash-flow/obligations?asOfDate=2026-08-01&page=1&pageSize=25",
                Permissions.CashFlowObligationView),
            ("/api/v1/cash-flow/obligations/resolve-jalali-date?jalaliDate=14050616",
                Permissions.CashFlowObligationView),
            ("/api/v1/cash-flow/costed-boms?asOfDate=2026-08-01&page=1&pageSize=25",
                Permissions.CashFlowCostingView),
            ("/api/v1/cash-flow/costed-boms/resolve-jalali-date?jalaliDate=14050616",
                Permissions.CashFlowCostingView),
            ($"/api/v1/cash-flow/costed-boms/{root.Id}?asOfDate=2026-08-01",
                Permissions.CashFlowCostingView)
        };
        foreach (var grantedPermission in new[]
                 {
                     Permissions.CashFlowDashboardView,
                     Permissions.CashFlowObligationView,
                     Permissions.CashFlowCostingView
                 })
        {
            using var permissionOnly = _factory.CreateClient();
            permissionOnly.DefaultRequestHeaders.Add("X-Hadron-Test-Permissions", grantedPermission);
            foreach (var (path, requiredPermission) in runtimeCases)
            {
                using var response = await permissionOnly.GetAsync(path);
                Assert.Equal(grantedPermission == requiredPermission
                    ? HttpStatusCode.OK
                    : HttpStatusCode.Forbidden, response.StatusCode);
            }
        }

        using var denied = _factory.CreateClient();
        denied.DefaultRequestHeaders.Add("X-Hadron-Test-Denied-Permissions",
            Permissions.CashFlowDashboardView);
        using var deniedResponse = await denied.GetAsync(
            "/api/v1/cash-flow/dashboard?asOfDate=2026-08-01&horizonEnd=2026-09-01");
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        using var dashboardOnly = _factory.CreateClient();
        dashboardOnly.DefaultRequestHeaders.Add("X-Hadron-Test-Permissions",
            Permissions.CashFlowDashboardView);
        using var allowedResponse = await dashboardOnly.GetAsync(
            "/api/v1/cash-flow/dashboard?asOfDate=2026-08-01&horizonEnd=2026-09-01");
        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);

        // A System Administrator cookie can predate a newly registered permission.
        // The protected role must still receive every current and future permission
        // without forcing an otherwise valid session to log in again.
        using var staleSystemAdministrator = _factory.CreateClient();
        staleSystemAdministrator.DefaultRequestHeaders.Add("X-Hadron-Test-Permissions", "none");
        staleSystemAdministrator.DefaultRequestHeaders.Add("X-Hadron-Test-Roles",
            SystemRoleNames.SystemAdministrator);
        using var staleAdministratorResponse = await staleSystemAdministrator.GetAsync(
            "/api/v1/cash-flow/dashboard?asOfDate=2026-08-01&horizonEnd=2026-09-01");
        Assert.Equal(HttpStatusCode.OK, staleAdministratorResponse.StatusCode);

        using var conversionDenied = await dashboardOnly.GetAsync(
            "/api/v1/cash-flow/obligations/resolve-jalali-date?jalaliDate=14050616");
        Assert.Equal(HttpStatusCode.Forbidden, conversionDenied.StatusCode);

        using var conversionAllowed = await dashboardOnly.GetAsync(
            "/api/v1/cash-flow/dashboard/resolve-jalali-date?jalaliDate=14050616");
        Assert.Equal(HttpStatusCode.OK, conversionAllowed.StatusCode);
        var converted = await conversionAllowed.Content.ReadFromJsonAsync<JalaliDateResponse>();
        Assert.NotNull(converted);
        Assert.Equal(new DateOnly(2026, 9, 7), converted!.GregorianDate);

        using var invalidConversion = await dashboardOnly.GetAsync(
            "/api/v1/cash-flow/dashboard/resolve-jalali-date?jalaliDate=14051301");
        Assert.Equal(HttpStatusCode.BadRequest, invalidConversion.StatusCode);
        using var missingConversion = await dashboardOnly.GetAsync(
            "/api/v1/cash-flow/dashboard/resolve-jalali-date");
        Assert.Equal(HttpStatusCode.BadRequest, missingConversion.StatusCode);
        using var mutation = await dashboardOnly.PostAsJsonAsync(
            "/api/v1/cash-flow/dashboard/resolve-jalali-date?jalaliDate=14050616", new { });
        Assert.Equal(HttpStatusCode.MethodNotAllowed, mutation.StatusCode);
    }

    [Fact]
    public async Task Anonymous_cash_flow_request_returns_401_in_cookie_auth_environment()
    {
        var factory = new IdentityApiFactory();
        await ((IAsyncLifetime)factory).InitializeAsync();
        try
        {
            using var anonymous = factory.CreateClient();
            using var response = await anonymous.GetAsync(
                "/api/v1/cash-flow/dashboard?asOfDate=2026-08-01&horizonEnd=2026-09-01");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            await ((IAsyncLifetime)factory).DisposeAsync();
        }
    }

    [Fact]
    public async Task Payment_projection_keeps_installments_schedules_cancellations_and_date_provenance_exact()
    {
        await using var db = await Db();
        var master = await AddMaster(db);
        var committedItem = await AddPurchasableItem(db, master, master.PieceUnit, "تعهد قطعی");
        var plannedItem = await AddPurchasableItem(db, master, master.PieceUnit, "تعهد برنامه‌ای");

        var committed = await AddOrder(db, master, committedItem, master.PrimaryCurrency,
            unitPrice: 10m, advance: 20m, beforeShipment: 30m, credit: 50m,
            creditDays: 10, creditAnchor: PurchaseMilestoneCodes.WarehouseReceipt,
            ordered: true,
            [
                new(20m,
                    new(new DateOnly(2026, 8, 4), new DateOnly(2026, 8, 12)),
                    new(new DateOnly(2026, 8, 6), new DateOnly(2026, 8, 14)), 0m),
                new(30m,
                    new(new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 25)),
                    new(null, new DateOnly(2026, 8, 28)), 5m),
                new(10m, null, null, 0m),
                new(5m, null, null, 5m),
            ]);

        var planned = await AddOrder(db, master, plannedItem, master.SecondaryCurrency,
            unitPrice: 2m, advance: 100m, beforeShipment: 0m, credit: 0m,
            creditDays: null, creditAnchor: null, ordered: false,
            [new(100m, null, null, 0m)]);

        var milestoneResponse = await _client.PostAsJsonAsync(
            $"/api/purchase-orders/{committed.Order.Id}/schedules/{committed.Schedules[0].Id}/actual-milestones",
            new RecordActualMilestoneRequest(committed.Order.Version,
                PurchaseMilestoneCodes.SupplierReady, new DateOnly(2026, 8, 5), null, null));
        Assert.Equal(HttpStatusCode.OK, milestoneResponse.StatusCode);
        var withActual = await milestoneResponse.Content.ReadFromJsonAsync<PurchaseOrderResponse>();
        Assert.NotNull(withActual);

        await PostFullReceipt(committed, committed.Schedules[0], master.PieceUnit,
            new DateOnly(2026, 8, 10), withActual!.Version);

        var committedRows = await Obligations(committed.Order.PurchaseOrderNumber, includePlanned: false);
        Assert.Equal(7, committedRows.TotalCount);
        Assert.All(committedRows.Items, row => Assert.Equal(CashFlowConfidenceLevels.Committed, row.Confidence));
        Assert.Equal(550m, committedRows.Items.Sum(x => x.ProjectedAmount));

        var advance = Assert.Single(committedRows.Items, x => x.Stage == CashFlowObligationStages.Advance);
        Assert.Equal(110m, advance.ProjectedAmount);
        Assert.Equal(new DateOnly(2026, 8, 1), advance.ExpectedPaymentDate);
        Assert.Equal(CashFlowDateSources.Ordered, advance.DateSource);
        Assert.Null(advance.PurchaseOrderLineId);
        Assert.Null(advance.PurchaseOrderLineNumber);
        Assert.Null(advance.ItemId);

        var schedule1Before = Assert.Single(committedRows.Items, x =>
            x.DeliveryScheduleId == committed.Schedules[0].Id &&
            x.Stage == CashFlowObligationStages.BeforeShipment);
        Assert.Equal(60m, schedule1Before.ProjectedAmount);
        Assert.Equal(committed.Line.Id, schedule1Before.PurchaseOrderLineId);
        Assert.Equal(committed.Line.LineNumber, schedule1Before.PurchaseOrderLineNumber);
        Assert.Equal(committedItem.Item.Id, schedule1Before.ItemId);
        Assert.Equal(committedItem.Item.BusinessNumber, schedule1Before.ItemCode);
        Assert.Equal(committedItem.Item.NameFa, schedule1Before.ItemNameFa);
        Assert.Equal(committedItem.Item.NameEn, schedule1Before.ItemNameEn);
        Assert.Equal(new DateOnly(2026, 8, 5), schedule1Before.ExpectedPaymentDate);
        Assert.Equal(CashFlowDateSources.Actual, schedule1Before.DateSource);
        var schedule1Credit = Assert.Single(committedRows.Items, x =>
            x.DeliveryScheduleId == committed.Schedules[0].Id &&
            x.Stage == CashFlowObligationStages.Credit);
        Assert.Equal(100m, schedule1Credit.ProjectedAmount);
        Assert.Equal(new DateOnly(2026, 8, 20), schedule1Credit.ExpectedPaymentDate);
        Assert.Equal(CashFlowDateSources.Actual, schedule1Credit.DateSource);

        var schedule2Before = Assert.Single(committedRows.Items, x =>
            x.DeliveryScheduleId == committed.Schedules[1].Id &&
            x.Stage == CashFlowObligationStages.BeforeShipment);
        Assert.Equal(75m, schedule2Before.ProjectedAmount);
        Assert.Equal(new DateOnly(2026, 8, 20), schedule2Before.ExpectedPaymentDate);
        Assert.Equal(CashFlowDateSources.Original, schedule2Before.DateSource);
        var schedule2Credit = Assert.Single(committedRows.Items, x =>
            x.DeliveryScheduleId == committed.Schedules[1].Id &&
            x.Stage == CashFlowObligationStages.Credit);
        Assert.Equal(125m, schedule2Credit.ProjectedAmount);
        Assert.Equal(new DateOnly(2026, 9, 7), schedule2Credit.ExpectedPaymentDate);
        Assert.Equal(CashFlowDateSources.Revised, schedule2Credit.DateSource);

        var unresolved = committedRows.Items.Where(x =>
            x.DeliveryScheduleId == committed.Schedules[2].Id).ToArray();
        Assert.Equal(2, unresolved.Length);
        Assert.All(unresolved, row =>
        {
            Assert.Null(row.ExpectedPaymentDate);
            Assert.Equal(CashFlowDateSources.Unresolved, row.DateSource);
            Assert.Equal(CashFlowObligationStates.Unresolved, row.State);
        });
        Assert.DoesNotContain(committedRows.Items,
            row => row.DeliveryScheduleId == committed.Schedules[3].Id);

        using var unresolvedResponse = await _client.GetAsync(
            $"/api/v1/cash-flow/obligations?asOfDate=2026-08-01&fromDate=2026-08-01" +
            $"&toDate=2026-08-31&state=UNRESOLVED&search={committed.Order.PurchaseOrderNumber}" +
            "&page=1&pageSize=25");
        Assert.Equal(HttpStatusCode.OK, unresolvedResponse.StatusCode);
        var unresolvedWithRange = await unresolvedResponse.Content.ReadFromJsonAsync<CashFlowObligationPage>();
        Assert.NotNull(unresolvedWithRange);
        Assert.Equal(2, unresolvedWithRange!.TotalCount);

        var plannedHidden = await Obligations(planned.Order.PurchaseOrderNumber, includePlanned: false);
        Assert.Empty(plannedHidden.Items);
        var plannedVisible = await Obligations(planned.Order.PurchaseOrderNumber, includePlanned: true);
        var plannedAdvance = Assert.Single(plannedVisible.Items);
        Assert.Equal(CashFlowConfidenceLevels.Planned, plannedAdvance.Confidence);
        Assert.Equal(200m, plannedAdvance.ProjectedAmount);
        Assert.Equal(CashFlowDateSources.Unresolved, plannedAdvance.DateSource);

        var committedDashboard = await Dashboard(includePlanned: false);
        Assert.DoesNotContain(committedDashboard.Currencies,
            x => x.CurrencyId == master.SecondaryCurrency.Id);
        var allDashboard = await Dashboard(includePlanned: true);
        var currencyCodes = allDashboard.Currencies.Select(x => x.CurrencyCode).ToArray();
        Assert.Contains(master.PrimaryCurrency.Code, currencyCodes);
        Assert.Contains(master.SecondaryCurrency.Code, currencyCodes);
        Assert.Equal(2, currencyCodes.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(1, allDashboard.Currencies.Single(x =>
            x.CurrencyId == master.SecondaryCurrency.Id).ObligationCount);
    }

    [Fact]
    public async Task Projection_uses_safe_decimal_ratios_and_keeps_multi_line_schedule_provenance()
    {
        await using var db = await Db();
        var master = await AddMaster(db);
        var purchasable = await AddPurchasableItem(db, master, master.PieceUnit,
            "قطعه مبلغ بزرگ جریان نقدی");
        var order = new PurchaseOrder($"PO-{NextNumber()}", master.Supplier.Id,
            master.PrimaryCurrency.Id, master.PaymentTerm.Id,
            99m, 1m, 0m, null, null, null, null, null);
        const decimal quantity = 999_999_999_999m;
        const decimal unitPrice = 999_999_999_999m;
        const int lineCount = 1_000;
        var lines = Enumerable.Range(1, lineCount)
            .Select(lineNumber => new PurchaseOrderLine(order.Id, lineNumber,
                purchasable.Item.Id, purchasable.Option.Id, null, purchasable.Item.BaseUnitId,
                quantity, unitPrice, null, 1, null, 1, null, null))
            .ToArray();
        var schedules = lines.Select(line => new DeliverySchedule(line.Id, 1, quantity, null))
            .ToArray();
        db.PurchaseOrders.Add(order);
        db.PurchaseOrderLines.AddRange(lines);
        db.DeliverySchedules.AddRange(schedules);
        await db.SaveChangesAsync();
        var now = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc);
        order.Approve(now);
        await db.SaveChangesAsync();
        order.MarkOrdered(new DateOnly(2026, 8, 1), now.AddMinutes(1));
        await db.SaveChangesAsync();

        using var advanceResponse = await _client.GetAsync(
            $"/api/v1/cash-flow/obligations?asOfDate=2026-08-01&search={order.PurchaseOrderNumber}" +
            "&stage=ADVANCE&page=1&pageSize=25");
        Assert.Equal(HttpStatusCode.OK, advanceResponse.StatusCode);
        var advancePage = await advanceResponse.Content.ReadFromJsonAsync<CashFlowObligationPage>();
        var advance = Assert.Single(advancePage!.Items);
        var totalValue = quantity * unitPrice * lineCount;
        Assert.Equal(totalValue * 0.99m, advance.ProjectedAmount);

        using var scheduleResponse = await _client.GetAsync(
            $"/api/v1/cash-flow/obligations?asOfDate=2026-08-01&search={order.PurchaseOrderNumber}" +
            "&stage=BEFORE_SHIPMENT&page=1&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, scheduleResponse.StatusCode);
        var schedulePage = await scheduleResponse.Content.ReadFromJsonAsync<CashFlowObligationPage>();
        Assert.Equal(lineCount, schedulePage!.TotalCount);
        var first = schedulePage.Items[0];
        Assert.Equal(lines[0].Id, first.PurchaseOrderLineId);
        Assert.Equal(1, first.PurchaseOrderLineNumber);
        Assert.Equal(purchasable.Item.Id, first.ItemId);
        Assert.Equal(purchasable.Item.BusinessNumber, first.ItemCode);
        Assert.Equal(purchasable.Item.NameFa, first.ItemNameFa);
        Assert.Equal(purchasable.Item.NameEn, first.ItemNameEn);
        Assert.Equal(quantity * unitPrice * 0.01m, first.ProjectedAmount);
    }

    [Fact]
    public async Task Obligation_projection_has_no_legacy_row_ceiling_and_pages_and_aggregates_in_postgresql()
    {
        await using var db = await Db();
        var master = await AddMaster(db);
        var purchasable = await AddPurchasableItem(db, master, master.PieceUnit,
            "قطعه آزمون سقف جریان نقدی");
        var order = new PurchaseOrder($"PO-{NextNumber()}", master.Supplier.Id,
            master.PrimaryCurrency.Id, master.PaymentTerm.Id,
            0m, 100m, 0m, null, null, null, null, null);
        const int scheduleCount = 25_001;
        var line = new PurchaseOrderLine(order.Id, 1, purchasable.Item.Id,
            purchasable.Option.Id, null, purchasable.Item.BaseUnitId, scheduleCount, 1m,
            null, 1, null, 1, null, null);
        await using (var seedTransaction = await db.Database.BeginTransactionAsync())
        {
            db.AddRange(order, line);
            await db.SaveChangesAsync();
            // This isolated read-model scale fixture is already reconciled by the line-level
            // deferred constraint. Disable per-schedule row triggers only while bulk-seeding;
            // otherwise 25,001 identical reconciliation scans make the regression itself O(n²).
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE procurement.delivery_schedules DISABLE TRIGGER USER");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO procurement.delivery_schedules
                    ("Id", "PurchaseOrderLineId", "ScheduleNumber", "AllocatedQuantity",
                     "RequestedReceiptDate")
                SELECT md5(CAST({line.Id} AS text) || series_number::text)::uuid,
                       {line.Id}, series_number, 1, NULL
                FROM generate_series(1, {scheduleCount}) AS series_number
                """);
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE procurement.delivery_schedules ENABLE TRIGGER USER");
            await seedTransaction.CommitAsync();
        }

        var now = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc);
        order.Approve(now);
        await db.SaveChangesAsync();
        order.MarkOrdered(new DateOnly(2026, 8, 1), now.AddMinutes(1));
        await db.SaveChangesAsync();

        using var commandCapture = new EfCommandCapture();
        using var pageResponse = await _client.GetAsync(
            $"/api/v1/cash-flow/obligations?asOfDate=2026-08-01" +
            $"&search={order.PurchaseOrderNumber}&state=UNRESOLVED&page=251&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
        var page = await pageResponse.Content.ReadFromJsonAsync<CashFlowObligationPage>();
        Assert.NotNull(page);
        Assert.Equal(scheduleCount, page!.TotalCount);
        var finalRow = Assert.Single(page.Items);
        Assert.Equal(scheduleCount, finalRow.ScheduleNumber);
        Assert.Equal(1m, finalRow.ProjectedAmount);

        var dashboard = await Dashboard(includePlanned: false);
        Assert.Equal(scheduleCount, dashboard.UnresolvedCount);
        var currency = Assert.Single(dashboard.Currencies);
        Assert.Equal(scheduleCount, currency.ObligationCount);

        var projectionCommands = commandCapture.Commands
            .Where(command => command.Contains("WITH candidate_orders AS",
                StringComparison.Ordinal))
            .ToArray();
        Assert.Contains(projectionCommands, command =>
            command.Contains("count(*)", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projectionCommands, command =>
            command.Contains("LIMIT", StringComparison.OrdinalIgnoreCase) &&
            command.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projectionCommands, command =>
            command.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase) &&
            command.Contains("SUM", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(projectionCommands, command =>
            command.Contains("LIMIT 25000", StringComparison.OrdinalIgnoreCase) ||
            command.Contains("LIMIT 100000", StringComparison.OrdinalIgnoreCase) ||
            command.Contains("LIMIT 150000", StringComparison.OrdinalIgnoreCase) ||
            command.Contains("LIMIT 250000", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Fx_conversion_uses_one_flat_latest_published_revision_ignores_future_and_preserves_original_money()
    {
        await using var db = await Db();
        var master = await AddMaster(db);
        var usd = await EnsureCurrency(db, "USD", "دلار آمریکا", "US Dollar", 2);
        var irr = await EnsureCurrency(db, "IRR", "ریال ایران", "Iranian Rial", 0);
        var cny = await EnsureCurrency(db, "CNY", "یوان چین", "Chinese Yuan", 2);
        var eur = await EnsureCurrency(db, "EUR", "یورو", "Euro", 2);
        var readyDate = new DateOnly(2026, 8, 10);

        var irrItem = await AddPurchasableItem(db, master, master.PieceUnit, "قطعه ریالی FX");
        var cnyItem = await AddPurchasableItem(db, master, master.PieceUnit, "قطعه یوانی FX");
        var eurItem = await AddPurchasableItem(db, master, master.PieceUnit, "قطعه یورویی FX");
        await AddOrder(db, master, irrItem, irr, 600_000m, 0m, 100m, 0m,
            null, null, true, [new(1m, new(readyDate, null), null, 0m)]);
        await AddOrder(db, master, cnyItem, cny, 7m, 0m, 100m, 0m,
            null, null, true, [new(1m, new(readyDate, null), null, 0m)]);
        await AddOrder(db, master, eurItem, eur, 0.9m, 0m, 100m, 0m,
            null, null, true, [new(1m, new(readyDate, null), null, 0m)]);

        // Historical/open obligations remain reportable after a reference currency is
        // deactivated. Currency activity controls new operational selection, not the
        // validity of persisted money or its governed reporting rate.
        eur.SetActive(false);
        eur.AdvanceVersion();
        eur.Touch();
        await db.SaveChangesAsync();

        var revision1 = await PublishFxRateSet(db, usd, new DateOnly(2026, 7, 31), 1,
            null, [(irr.Id, 300_000m), (cny.Id, 3.5m), (eur.Id, 0.45m)]);
        var revision2 = await PublishFxRateSet(db, usd, new DateOnly(2026, 7, 31), 2,
            revision1.Id, [(irr.Id, 600_000m), (cny.Id, 7m), (eur.Id, 0.9m)]);
        await PublishFxRateSet(db, usd, new DateOnly(2026, 9, 1), 1,
            null, [(irr.Id, 1m), (cny.Id, 1m), (eur.Id, 1m)]);

        using var commandCapture = new EfCommandCapture();
        var dashboard = await _client.GetFromJsonAsync<CashFlowDashboardResponse>(
            "/api/v1/cash-flow/dashboard?asOfDate=2026-08-01&horizonEnd=2026-08-31");
        Assert.NotNull(dashboard);
        Assert.Equal(revision2.Id, dashboard!.FxConversionContext.RateSetId);
        Assert.Equal(new DateOnly(2026, 7, 31), dashboard.FxConversionContext.EffectiveDate);
        Assert.Equal(2, dashboard.FxConversionContext.Revision);
        Assert.Equal(FxConversionCompleteness.Complete,
            dashboard.FxConversionContext.Completeness);
        Assert.Empty(dashboard.FxConversionContext.MissingCurrencyCodes);
        Assert.Equal(600_000m, dashboard.Currencies.Single(x => x.CurrencyId == irr.Id).HorizonAmount);
        Assert.Equal(7m, dashboard.Currencies.Single(x => x.CurrencyId == cny.Id).HorizonAmount);
        Assert.Equal(0.9m, dashboard.Currencies.Single(x => x.CurrencyId == eur.Id).HorizonAmount);
        Assert.Equal(3m, dashboard.UsdSummary.Due30DaysAmount);
        Assert.Equal(3m, dashboard.UsdSummary.Due90DaysAmount);
        Assert.Equal(3m, dashboard.UsdSummary.HorizonAmount);
        var usdBucket = Assert.Single(dashboard.UsdBuckets);
        Assert.Equal(readyDate, usdBucket.Date);
        Assert.Equal(3m, usdBucket.Amount);
        Assert.Equal(3, usdBucket.Count);

        var obligations = await _client.GetFromJsonAsync<CashFlowObligationPage>(
            "/api/v1/cash-flow/obligations?asOfDate=2026-08-01&page=1&pageSize=100");
        Assert.NotNull(obligations);
        Assert.Equal(3m, obligations!.TotalUsdAmount);
        Assert.Equal(FxConversionCompleteness.Complete,
            obligations.FxConversionContext.Completeness);
        Assert.Equal(3, obligations.TotalCount);
        Assert.Equal(600_000m, obligations.Items.Single(x => x.CurrencyId == irr.Id).ProjectedAmount);
        Assert.Equal(7m, obligations.Items.Single(x => x.CurrencyId == cny.Id).ProjectedAmount);
        Assert.Equal(0.9m, obligations.Items.Single(x => x.CurrencyId == eur.Id).ProjectedAmount);
        Assert.All(obligations.Items, row => Assert.Equal(1m, row.ProjectedAmountUsd));
        Assert.Equal(600_000m, obligations.Items.Single(x => x.CurrencyId == irr.Id).UnitsPerUsd);
        Assert.Equal(7m, obligations.Items.Single(x => x.CurrencyId == cny.Id).UnitsPerUsd);
        Assert.Equal(0.9m, obligations.Items.Single(x => x.CurrencyId == eur.Id).UnitsPerUsd);
        Assert.False(await db.Currencies.AsNoTracking()
            .Where(x => x.Id == eur.Id).Select(x => x.IsActive).SingleAsync());

        var fxCommands = commandCapture.Commands.Where(command =>
            command.Contains("WITH selected_rate_set AS", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, fxCommands.Length);

        var gbp = await EnsureCurrency(db, "GBP", "پوند بریتانیا", "Pound Sterling", 2);
        var missingRateItem = await AddPurchasableItem(db, master, master.PieceUnit,
            "قطعه بدون نرخ گزارش");
        await AddOrder(db, master, missingRateItem, gbp, 2m, 0m, 100m, 0m,
            null, null, true, [new(1m, new(readyDate, null), null, 0m)]);
        await AddOrder(db, master, missingRateItem, gbp, 3m, 0m, 100m, 0m,
            null, null, true, [new(1m, new(readyDate, null), null, 0m)]);

        var incompleteDashboard = await _client.GetFromJsonAsync<CashFlowDashboardResponse>(
            "/api/v1/cash-flow/dashboard?asOfDate=2026-08-01&horizonEnd=2026-08-31");
        Assert.NotNull(incompleteDashboard);
        Assert.Equal(FxConversionCompleteness.Incomplete,
            incompleteDashboard!.FxConversionContext.Completeness);
        Assert.Equal(["GBP"], incompleteDashboard.FxConversionContext.MissingCurrencyCodes);
        Assert.Equal(0m, incompleteDashboard.UsdSummary.OverdueAmount);
        Assert.Equal(0m, incompleteDashboard.UsdSummary.Due7DaysAmount);
        Assert.Null(incompleteDashboard.UsdSummary.Due30DaysAmount);
        Assert.Null(incompleteDashboard.UsdSummary.Due90DaysAmount);
        Assert.Null(incompleteDashboard.UsdSummary.HorizonAmount);
        Assert.Equal(5m, incompleteDashboard.Currencies.Single(x => x.CurrencyId == gbp.Id).HorizonAmount);
        var incompleteBucket = Assert.Single(incompleteDashboard.UsdBuckets);
        Assert.Equal(readyDate, incompleteBucket.Date);
        Assert.Null(incompleteBucket.Amount);
        Assert.Equal(5, incompleteBucket.Count);

        var incompleteObligations = await _client.GetFromJsonAsync<CashFlowObligationPage>(
            "/api/v1/cash-flow/obligations?asOfDate=2026-08-01&page=1&pageSize=100");
        Assert.NotNull(incompleteObligations);
        Assert.Equal(FxConversionCompleteness.Incomplete,
            incompleteObligations!.FxConversionContext.Completeness);
        Assert.Equal(["GBP"], incompleteObligations.FxConversionContext.MissingCurrencyCodes);
        Assert.Null(incompleteObligations.TotalUsdAmount);
        var missingRateRows = incompleteObligations.Items.Where(x => x.CurrencyId == gbp.Id)
            .ToArray();
        Assert.Equal(2, missingRateRows.Length);
        Assert.Equal(5m, missingRateRows.Sum(x => x.ProjectedAmount));
        Assert.All(missingRateRows, row =>
        {
            Assert.Null(row.UnitsPerUsd);
            Assert.Null(row.ProjectedAmountUsd);
        });
        Assert.All(incompleteObligations.Items.Where(x => x.CurrencyId != gbp.Id),
            row => Assert.Equal(1m, row.ProjectedAmountUsd));
    }

    [Fact]
    public async Task Fx_conversion_overflow_returns_an_explicit_conflict_instead_of_500_or_missing_rate()
    {
        await using var db = await Db();
        var master = await AddMaster(db);
        var usd = await EnsureCurrency(db, "USD", "دلار آمریکا", "US Dollar", 2);
        var readyDate = new DateOnly(2026, 8, 10);
        var item = await AddPurchasableItem(db, master, master.PieceUnit,
            "قطعه با مبلغ بزرگ برای کنترل محدوده FX");
        var order = await AddOrder(db, master, item, master.PrimaryCurrency,
            999_999_999_999m, 0m, 100m, 0m, null, null, true,
            [new(999_999_999_999m, new(readyDate, null), null, 0m)]);
        await PublishFxRateSet(db, usd, new DateOnly(2026, 8, 1), 1, null,
            [(master.PrimaryCurrency.Id, 0.000000000001m)]);

        using var response = await _client.GetAsync(
            $"/api/v1/cash-flow/obligations?asOfDate=2026-08-01&search={order.Order.PurchaseOrderNumber}&page=1&pageSize=100");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("FX_CONVERSION_OUT_OF_RANGE",
            problem.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("missing", problem.RootElement.GetProperty("detail").GetString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Fx_conversion_uses_implicit_usd_and_reports_missing_set_without_losing_originals()
    {
        await using var db = await Db();
        var master = await AddMaster(db);
        var usd = await EnsureCurrency(db, "USD", "دلار آمریکا", "US Dollar", 2);
        var cny = await EnsureCurrency(db, "CNY", "یوان چین", "Chinese Yuan", 2);
        var gbp = await EnsureCurrency(db, "GBP", "پوند بریتانیا", "Pound Sterling", 2);
        var readyDate = new DateOnly(2026, 8, 10);
        var usdItem = await AddPurchasableItem(db, master, master.PieceUnit, "قطعه دلاری بدون مجموعه نرخ");
        var cnyItem = await AddPurchasableItem(db, master, master.PieceUnit, "قطعه یوانی بدون مجموعه نرخ");
        var zeroItem = await AddPurchasableItem(db, master, master.PieceUnit, "قطعه صفر بدون نرخ");
        await AddOrder(db, master, usdItem, usd, 2m, 0m, 100m, 0m,
            null, null, true, [new(1m, new(readyDate, null), null, 0m)]);
        await AddOrder(db, master, cnyItem, cny, 7m, 0m, 100m, 0m,
            null, null, true, [new(1m, new(readyDate, null), null, 0m)]);
        var zeroOrder = await AddOrder(db, master, zeroItem, gbp, 0m, 0m, 100m, 0m,
            null, null, true, [new(1m, new(readyDate, null), null, 0m)]);

        var zeroOnly = await _client.GetFromJsonAsync<CashFlowObligationPage>(
            $"/api/v1/cash-flow/obligations?asOfDate=2026-08-01&search={zeroOrder.Order.PurchaseOrderNumber}&page=1&pageSize=100");
        Assert.NotNull(zeroOnly);
        Assert.Equal(FxConversionCompleteness.MissingSet,
            zeroOnly!.FxConversionContext.Completeness);
        Assert.Equal(["GBP"], zeroOnly.FxConversionContext.MissingCurrencyCodes);
        Assert.Equal(0m, zeroOnly.TotalUsdAmount);
        var zeroRow = Assert.Single(zeroOnly.Items);
        Assert.Equal(0m, zeroRow.ProjectedAmount);
        Assert.Null(zeroRow.UnitsPerUsd);
        Assert.Null(zeroRow.ProjectedAmountUsd);

        var obligations = await _client.GetFromJsonAsync<CashFlowObligationPage>(
            "/api/v1/cash-flow/obligations?asOfDate=2026-08-01&page=1&pageSize=100");
        Assert.NotNull(obligations);
        Assert.Equal(FxConversionCompleteness.MissingSet,
            obligations!.FxConversionContext.Completeness);
        Assert.Null(obligations.FxConversionContext.RateSetId);
        Assert.Null(obligations.FxConversionContext.EffectiveDate);
        Assert.Null(obligations.FxConversionContext.Revision);
        Assert.Equal(["CNY", "GBP"], obligations.FxConversionContext.MissingCurrencyCodes);
        Assert.Null(obligations.TotalUsdAmount);
        var usdRow = Assert.Single(obligations.Items, x => x.CurrencyId == usd.Id);
        Assert.Equal(2m, usdRow.ProjectedAmount);
        Assert.Equal(1m, usdRow.UnitsPerUsd);
        Assert.Equal(2m, usdRow.ProjectedAmountUsd);
        var cnyRow = Assert.Single(obligations.Items, x => x.CurrencyId == cny.Id);
        Assert.Equal(7m, cnyRow.ProjectedAmount);
        Assert.Null(cnyRow.UnitsPerUsd);
        Assert.Null(cnyRow.ProjectedAmountUsd);

        var dashboard = await _client.GetFromJsonAsync<CashFlowDashboardResponse>(
            "/api/v1/cash-flow/dashboard?asOfDate=2026-08-01&horizonEnd=2026-08-31");
        Assert.NotNull(dashboard);
        Assert.Equal(FxConversionCompleteness.MissingSet,
            dashboard!.FxConversionContext.Completeness);
        Assert.Equal(["CNY", "GBP"], dashboard.FxConversionContext.MissingCurrencyCodes);
        Assert.Null(dashboard.UsdSummary.HorizonAmount);
        Assert.Equal(2m, dashboard.Currencies.Single(x => x.CurrencyId == usd.Id).HorizonAmount);
        Assert.Equal(7m, dashboard.Currencies.Single(x => x.CurrencyId == cny.Id).HorizonAmount);
    }

    [Fact]
    public async Task Costed_BOM_uses_latest_valid_receipt_then_fallback_and_never_hides_missing_or_unit_mismatch()
    {
        await using var db = await Db();
        var master = await AddMaster(db);
        var usd = await EnsureCurrency(db, "USD", "دلار آمریکا", "US Dollar", 2);
        await PublishFxRateSet(db, usd, new DateOnly(2026, 8, 1), 1, null,
            [(master.PrimaryCurrency.Id, 10m), (master.SecondaryCurrency.Id, 7m)]);
        var root = await AddItem(db, ItemTypes.Product, master.PieceUnit, "محصول قیمت‌گذاری");
        var assembly = await AddItem(db, ItemTypes.Part, master.PieceUnit, "زیرمونتاژ");
        var receiptLeaf = await AddPurchasableItem(db, master, master.PieceUnit, "قطعه رسیددار");
        var fallbackLeaf = await AddPurchasableItem(db, master, master.KilogramUnit, "ماده برآوردی");
        var missingLeaf = await AddPurchasableItem(db, master, master.PieceUnit, "قیمت ناموجود");
        var mismatchLeaf = await AddPurchasableItem(db, master, master.PieceUnit, "واحد ناسازگار");

        var rootBom = new BomRevision(NextBomNumber(), root.Id, master.PieceUnit.Id, 2m, null);
        var assemblyBom = new BomRevision(NextBomNumber(), assembly.Id, master.PieceUnit.Id, 5m, null);
        db.AddRange(rootBom, assemblyBom,
            new BomLine(rootBom.Id, assembly.Id, master.PieceUnit.Id, 4m, 10m, 1),
            new BomLine(rootBom.Id, fallbackLeaf.Item.Id, master.KilogramUnit.Id, 3m, 0m, 2),
            new BomLine(rootBom.Id, missingLeaf.Item.Id, master.PieceUnit.Id, 2m, 0m, 3),
            new BomLine(rootBom.Id, mismatchLeaf.Item.Id, master.PieceUnit.Id, 1m, 0m, 4),
            new BomLine(assemblyBom.Id, receiptLeaf.Item.Id, master.PieceUnit.Id, 10m, 20m, 1));
        await db.SaveChangesAsync();
        rootBom.Approve("cash-flow-test");
        assemblyBom.Approve("cash-flow-test");
        await db.SaveChangesAsync();

        var oldReceiptOrder = await AddOrder(db, master, receiptLeaf, master.PrimaryCurrency,
            10m, 100m, 0m, 0m, null, null, true,
            [new(10m, null, null, 0m)], new DateOnly(2026, 7, 1));
        var oldReceipt = await PostFullReceipt(oldReceiptOrder, oldReceiptOrder.Schedules[0],
            master.PieceUnit, new DateOnly(2026, 8, 1), oldReceiptOrder.Order.Version);

        var newReceiptOrder = await AddOrder(db, master, receiptLeaf, master.PrimaryCurrency,
            12m, 100m, 0m, 0m, null, null, true,
            [new(10m, null, null, 0m)], new DateOnly(2026, 7, 2));
        var newReceipt = await PostFullReceipt(newReceiptOrder, newReceiptOrder.Schedules[0],
            master.PieceUnit, new DateOnly(2026, 8, 5), newReceiptOrder.Order.Version);

        await AddOrder(db, master, fallbackLeaf, master.SecondaryCurrency,
            7m, 100m, 0m, 0m, null, null, true,
            [new(10m, null, null, 0m)], new DateOnly(2026, 7, 1));
        await AddOrder(db, master, missingLeaf, master.PrimaryCurrency,
            0m, 100m, 0m, 0m, null, null, true,
            [new(10m, null, null, 0m)], new DateOnly(2026, 7, 1));
        await AddOrder(db, master, mismatchLeaf, master.PrimaryCurrency,
            3m, 100m, 0m, 0m, null, null, true,
            [new(10m, null, null, 0m)], new DateOnly(2026, 7, 1));

        using var fxCommandCapture = new EfCommandCapture();
        using (var listResponse = await _client.GetAsync(
                   $"/api/v1/cash-flow/costed-boms?asOfDate=2026-08-31&search={root.BusinessNumber}&page=1&pageSize=25"))
        {
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            using var listDocument = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
            var listRoot = listDocument.RootElement;
            Assert.Equal(CashFlowDataSemantics.CurrentValidFactsWithSourceDateCutoff,
                listRoot.GetProperty("valuationSemantics").GetString());
            var summary = Assert.Single(listRoot.GetProperty("items").EnumerateArray());
            Assert.Equal(JsonValueKind.String,
                summary.GetProperty("costedOutputQuantity").ValueKind);
            Assert.All(summary.GetProperty("currencyTotals").EnumerateArray(), total =>
                Assert.Equal(JsonValueKind.String, total.GetProperty("materialCost").ValueKind));
            Assert.Equal(JsonValueKind.Null, summary.GetProperty("materialCostUsd").ValueKind);
        }

        var costed = await CostedBom(root.Id);
        Assert.Equal(1m, costed.CostedOutputQuantity);
        Assert.Equal(root.NameFa, costed.ParentItemNameFa);
        Assert.Equal(root.NameEn, costed.ParentItemNameEn);
        Assert.True(costed.HasMixedCurrencies);
        Assert.Equal(2, costed.CurrencyTotals.Count);
        Assert.Equal(FxConversionCompleteness.Complete,
            costed.FxConversionContext.Completeness);
        Assert.Empty(costed.FxConversionContext.MissingCurrencyCodes);
        Assert.Null(costed.MaterialCostUsd);

        var assemblyRow = Assert.Single(costed.Lines, x => x.ItemId == assembly.Id);
        Assert.False(assemblyRow.IsLeaf);
        Assert.Equal(2.2m, assemblyRow.Quantity);
        var receiptRow = Assert.Single(costed.Lines, x => x.ItemId == receiptLeaf.Item.Id);
        Assert.Equal(receiptLeaf.Item.NameFa, receiptRow.ItemNameFa);
        Assert.Equal(receiptLeaf.Item.NameEn, receiptRow.ItemNameEn);
        Assert.Equal(5.28m, receiptRow.Quantity);
        Assert.Equal(CostBasisKinds.Receipt, receiptRow.CostBasis?.Kind);
        Assert.Equal(12m, receiptRow.CostBasis?.UnitPrice);
        Assert.Equal(newReceipt.Id, receiptRow.CostBasis?.SourceDocumentId);
        Assert.Equal(63.36m, receiptRow.ExtendedCost);
        Assert.Equal(6.336m, receiptRow.ExtendedCostUsd);

        var fallbackRow = Assert.Single(costed.Lines, x => x.ItemId == fallbackLeaf.Item.Id);
        Assert.Equal(1.5m, fallbackRow.Quantity);
        Assert.Equal(CostBasisKinds.PurchaseOrderEstimate, fallbackRow.CostBasis?.Kind);
        Assert.Equal(7m, fallbackRow.CostBasis?.UnitPrice);
        Assert.Equal(10.5m, fallbackRow.ExtendedCost);
        Assert.Equal(1.5m, fallbackRow.ExtendedCostUsd);

        var missingRow = Assert.Single(costed.Lines, x => x.ItemId == missingLeaf.Item.Id);
        Assert.Equal(CostBasisKinds.Missing, missingRow.CostBasis?.Kind);
        Assert.Equal("NO_VALID_PURCHASE_COST", missingRow.CostBasis?.MissingReason);
        Assert.Null(missingRow.ExtendedCost);
        Assert.Null(missingRow.ExtendedCostUsd);
        var directlyCostedRow = Assert.Single(costed.Lines, x => x.ItemId == mismatchLeaf.Item.Id);
        Assert.Equal(CostBasisKinds.PurchaseOrderEstimate, directlyCostedRow.CostBasis?.Kind);
        Assert.Equal(3m, directlyCostedRow.CostBasis?.UnitPrice);
        Assert.Equal(1.5m, directlyCostedRow.ExtendedCost);
        Assert.Equal(0.15m, directlyCostedRow.ExtendedCostUsd);
        Assert.Single(costed.MissingCosts);
        Assert.Contains(costed.MissingCosts, x => x.ItemId == missingLeaf.Item.Id &&
            x.Reason == "NO_VALID_PURCHASE_COST");
        var primaryTotal = costed.CurrencyTotals.Single(x =>
            x.CurrencyId == master.PrimaryCurrency.Id);
        Assert.Equal(64.86m, primaryTotal.MaterialCost);
        Assert.Equal(10m, primaryTotal.UnitsPerUsd);
        Assert.Equal(6.486m, primaryTotal.MaterialCostUsd);
        var secondaryTotal = costed.CurrencyTotals.Single(x =>
            x.CurrencyId == master.SecondaryCurrency.Id);
        Assert.Equal(10.5m, secondaryTotal.MaterialCost);
        Assert.Equal(7m, secondaryTotal.UnitsPerUsd);
        Assert.Equal(1.5m, secondaryTotal.MaterialCostUsd);
        Assert.Equal(2, fxCommandCapture.Commands.Count(command =>
            command.Contains("WITH selected_rate_set AS", StringComparison.Ordinal)));

        var latestOrder = await _client.GetFromJsonAsync<PurchaseOrderResponse>(
            $"/api/purchase-orders/{newReceiptOrder.Order.Id}");
        Assert.NotNull(latestOrder);
        using var reversedResponse = await _client.PostAsJsonAsync(
            $"/api/purchase-receipts/{newReceipt.Id}/reverse",
            new ReversePurchaseReceiptRequest(newReceipt.Version, latestOrder!.Version,
                "cash-flow latest-cost reversal"));
        Assert.Equal(HttpStatusCode.OK, reversedResponse.StatusCode);

        var afterReversal = await CostedBom(root.Id);
        var revertedReceiptRow = Assert.Single(afterReversal.Lines,
            x => x.ItemId == receiptLeaf.Item.Id);
        Assert.Equal(10m, revertedReceiptRow.CostBasis?.UnitPrice);
        Assert.Equal(oldReceipt.Id, revertedReceiptRow.CostBasis?.SourceDocumentId);
        Assert.Equal(52.8m, revertedReceiptRow.ExtendedCost);
        Assert.Equal(5.28m, revertedReceiptRow.ExtendedCostUsd);

        var staleRoot = await AddItem(db, ItemTypes.Product, master.PieceUnit,
            "محصول با snapshot واحد قدیمی");
        var staleBom = new BomRevision(NextBomNumber(), staleRoot.Id,
            master.PieceUnit.Id, 1m, null);
        db.AddRange(staleBom,
            new BomLine(staleBom.Id, mismatchLeaf.Item.Id, master.KilogramUnit.Id,
                1m, 0m, 1));
        await db.SaveChangesAsync();
        staleBom.Approve("cash-flow-test");
        await db.SaveChangesAsync();

        using var staleResponse = await _client.GetAsync(
            $"/api/v1/cash-flow/costed-boms/{staleRoot.Id}?asOfDate=2026-08-31");
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        using var staleProblem = JsonDocument.Parse(await staleResponse.Content.ReadAsStringAsync());
        Assert.Equal("BASE_UNIT_CHANGED", staleProblem.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Costed_BOM_returns_domain_conflict_when_valid_decimal_inputs_overflow_the_calculation()
    {
        await using var db = await Db();
        var master = await AddMaster(db);
        var root = await AddItem(db, ItemTypes.Product, master.PieceUnit,
            "محصول با محاسبه خارج از محدوده");
        var leaf = await AddPurchasableItem(db, master, master.PieceUnit,
            "قطعه با محاسبه خارج از محدوده");
        const decimal maximumDatabaseValue = 999_999_999_999.999999m;
        var bom = new BomRevision(NextBomNumber(), root.Id, master.PieceUnit.Id,
            0.000001m, null);
        db.AddRange(bom,
            new BomLine(bom.Id, leaf.Item.Id, master.PieceUnit.Id,
                maximumDatabaseValue, 0m, 1));
        await db.SaveChangesAsync();
        bom.Approve("cash-flow-overflow-test");
        await db.SaveChangesAsync();
        await AddOrder(db, master, leaf, master.PrimaryCurrency,
            maximumDatabaseValue, 100m, 0m, 0m, null, null, true,
            [new(1m, null, null, 0m)], new DateOnly(2026, 8, 1));

        using var summaryResponse = await _client.GetAsync(
            $"/api/v1/cash-flow/costed-boms?asOfDate=2026-08-31&search={root.BusinessNumber}" +
            "&page=1&pageSize=25");
        Assert.Equal(HttpStatusCode.Conflict, summaryResponse.StatusCode);
        using var summaryProblem = JsonDocument.Parse(
            await summaryResponse.Content.ReadAsStringAsync());
        Assert.Equal("COSTED_BOM_CALCULATION_OUT_OF_RANGE",
            summaryProblem.RootElement.GetProperty("code").GetString());

        using var detailResponse = await _client.GetAsync(
            $"/api/v1/cash-flow/costed-boms/{root.Id}?asOfDate=2026-08-31");
        Assert.Equal(HttpStatusCode.Conflict, detailResponse.StatusCode);
        using var detailProblem = JsonDocument.Parse(
            await detailResponse.Content.ReadAsStringAsync());
        Assert.Equal("COSTED_BOM_CALCULATION_OUT_OF_RANGE",
            detailProblem.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Costed_BOM_summary_is_memoized_and_detail_has_a_deterministic_expansion_budget()
    {
        await using var db = await Db();
        var master = await AddMaster(db);
        var root = new Item(ItemTypes.Product, $"PRD-{NextNumber()}",
            "محصول شبکه BOM", "BOM graph product", master.PieceUnit.Id, null);
        var layers = new List<Item[]> { new[] { root } };
        for (var level = 1; level <= 15; level++)
        {
            layers.Add(Enumerable.Range(0, 2).Select(index =>
                new Item(ItemTypes.Part, $"PRT-{NextNumber()}",
                    $"قلم شبکه {level}-{index}", $"Graph item {level}-{index}",
                    master.PieceUnit.Id, null)).ToArray());
        }
        db.Items.AddRange(layers.SelectMany(x => x));
        await db.SaveChangesAsync();

        var revisions = new List<BomRevision>();
        var lines = new List<BomLine>();
        for (var level = 0; level < layers.Count - 1; level++)
        {
            foreach (var parent in layers[level])
            {
                var revision = new BomRevision(NextBomNumber(), parent.Id,
                    master.PieceUnit.Id, 1m, null);
                revisions.Add(revision);
                lines.AddRange(layers[level + 1].Select((child, index) =>
                    new BomLine(revision.Id, child.Id, master.PieceUnit.Id, 1m, 0m,
                        index + 1)));
            }
        }
        db.BomRevisions.AddRange(revisions);
        db.BomLines.AddRange(lines);
        await db.SaveChangesAsync();
        foreach (var revision in revisions) revision.Approve("cash-flow-budget-test");
        await db.SaveChangesAsync();

        using var summaryResponse = await _client.GetAsync(
            $"/api/v1/cash-flow/costed-boms?asOfDate=2026-08-31&search={root.BusinessNumber}" +
            "&page=1&pageSize=25");
        Assert.Equal(HttpStatusCode.OK, summaryResponse.StatusCode);
        var page = await summaryResponse.Content.ReadFromJsonAsync<CostedBomPage>();
        var summary = Assert.Single(page!.Items);
        Assert.Equal(32_768, summary.MissingCostCount);
        Assert.Equal(root.NameFa, summary.ParentItemNameFa);
        Assert.Equal(root.NameEn, summary.ParentItemNameEn);

        using var detailResponse = await _client.GetAsync(
            $"/api/v1/cash-flow/costed-boms/{root.Id}?asOfDate=2026-08-31");
        Assert.Equal(HttpStatusCode.Conflict, detailResponse.StatusCode);
        using var problem = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        Assert.Equal("COSTED_BOM_EXPANSION_LIMIT_EXCEEDED",
            problem.RootElement.GetProperty("code").GetString());
    }

    private async Task<CashFlowObligationPage> Obligations(string search, bool includePlanned)
    {
        var response = await _client.GetAsync(
            $"/api/v1/cash-flow/obligations?asOfDate=2026-08-01&includePlanned={includePlanned}" +
            $"&search={search}&page=1&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        Assert.Equal(CashFlowDataSemantics.CurrentValidFacts,
            document.RootElement.GetProperty("dataSemantics").GetString());
        Assert.All(document.RootElement.GetProperty("items").EnumerateArray(), row =>
        {
            Assert.Equal(JsonValueKind.String, row.GetProperty("projectedAmount").ValueKind);
            Assert.Contains(row.GetProperty("unitsPerUsd").ValueKind,
                new[] { JsonValueKind.String, JsonValueKind.Null });
            Assert.Contains(row.GetProperty("projectedAmountUsd").ValueKind,
                new[] { JsonValueKind.String, JsonValueKind.Null });
        });
        Assert.Contains(document.RootElement.GetProperty("totalUsdAmount").ValueKind,
            new[] { JsonValueKind.String, JsonValueKind.Null });
        return Deserialize<CashFlowObligationPage>(json);
    }

    private async Task<CashFlowDashboardResponse> Dashboard(bool includePlanned)
    {
        var response = await _client.GetAsync(
            $"/api/v1/cash-flow/dashboard?asOfDate=2026-08-01&horizonEnd=2026-12-01" +
            $"&includePlanned={includePlanned}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        Assert.Equal(CashFlowDataSemantics.CurrentValidFacts,
            document.RootElement.GetProperty("dataSemantics").GetString());
        foreach (var currency in document.RootElement.GetProperty("currencies").EnumerateArray())
            foreach (var field in new[] { "overdueAmount", "due7DaysAmount", "due30DaysAmount", "due90DaysAmount", "horizonAmount" })
                Assert.Equal(JsonValueKind.String, currency.GetProperty(field).ValueKind);
        Assert.All(document.RootElement.GetProperty("buckets").EnumerateArray(), bucket =>
            Assert.Equal(JsonValueKind.String, bucket.GetProperty("amount").ValueKind));
        foreach (var field in new[] { "overdueAmount", "due7DaysAmount", "due30DaysAmount", "due90DaysAmount", "horizonAmount" })
            Assert.Contains(document.RootElement.GetProperty("usdSummary").GetProperty(field).ValueKind,
                new[] { JsonValueKind.String, JsonValueKind.Null });
        Assert.All(document.RootElement.GetProperty("usdBuckets").EnumerateArray(), bucket =>
            Assert.Contains(bucket.GetProperty("amount").ValueKind,
                new[] { JsonValueKind.String, JsonValueKind.Null }));
        return Deserialize<CashFlowDashboardResponse>(json);
    }

    private async Task<CostedBomResponse> CostedBom(Guid parentItemId)
    {
        var response = await _client.GetAsync(
            $"/api/v1/cash-flow/costed-boms/{parentItemId}?asOfDate=2026-08-31");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(CashFlowDataSemantics.CurrentValidFactsWithSourceDateCutoff,
            root.GetProperty("valuationSemantics").GetString());
        Assert.Equal(JsonValueKind.String, root.GetProperty("costedOutputQuantity").ValueKind);
        foreach (var line in root.GetProperty("lines").EnumerateArray())
        {
            Assert.Equal(JsonValueKind.String, line.GetProperty("quantity").ValueKind);
            Assert.Equal(JsonValueKind.String, line.GetProperty("scrapPercent").ValueKind);
            if (line.GetProperty("costBasis").ValueKind != JsonValueKind.Null &&
                line.GetProperty("costBasis").GetProperty("unitPrice").ValueKind != JsonValueKind.Null)
                Assert.Equal(JsonValueKind.String,
                    line.GetProperty("costBasis").GetProperty("unitPrice").ValueKind);
            if (line.GetProperty("extendedCost").ValueKind != JsonValueKind.Null)
                Assert.Equal(JsonValueKind.String, line.GetProperty("extendedCost").ValueKind);
            Assert.Contains(line.GetProperty("extendedCostUsd").ValueKind,
                new[] { JsonValueKind.String, JsonValueKind.Null });
        }
        Assert.All(root.GetProperty("currencyTotals").EnumerateArray(), total =>
        {
            Assert.Equal(JsonValueKind.String, total.GetProperty("materialCost").ValueKind);
            Assert.Contains(total.GetProperty("unitsPerUsd").ValueKind,
                new[] { JsonValueKind.String, JsonValueKind.Null });
            Assert.Contains(total.GetProperty("materialCostUsd").ValueKind,
                new[] { JsonValueKind.String, JsonValueKind.Null });
        });
        Assert.Contains(root.GetProperty("materialCostUsd").ValueKind,
            new[] { JsonValueKind.String, JsonValueKind.Null });
        return Deserialize<CostedBomResponse>(json);
    }

    private static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json,
        new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static async Task<Currency> EnsureCurrency(HadronDbContext db, string code,
        string nameFa, string nameEn, int decimalPrecision)
    {
        var existing = await db.Currencies.SingleOrDefaultAsync(x => x.Code == code);
        if (existing is not null) return existing;
        var currency = new Currency(code, code, nameFa, nameEn, null, decimalPrecision);
        db.Currencies.Add(currency);
        await db.SaveChangesAsync();
        return currency;
    }

    private static async Task<ReportingFxRateSet> PublishFxRateSet(
        HadronDbContext db,
        Currency usd,
        DateOnly effectiveDate,
        int revision,
        Guid? supersedesRateSetId,
        IReadOnlyCollection<(Guid CurrencyId, decimal UnitsPerUsd)> rates)
    {
        var requestId = Guid.NewGuid();
        var fingerprintPart = requestId.ToString("N").ToUpperInvariant();
        var rateSet = new ReportingFxRateSet(usd.Id, effectiveDate, revision,
            supersedesRateSetId, requestId, fingerprintPart + fingerprintPart,
            "cash-flow FX regression", "cash-flow-test",
            new DateTime(2026, 7, 1, revision, 0, 0, DateTimeKind.Utc), rates);
        db.ReportingFxRateSets.Add(rateSet);
        await db.SaveChangesAsync();
        rateSet.Publish("cash-flow FX regression publish", "cash-flow-test",
            new DateTime(2026, 7, 2, revision, 0, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync();
        return rateSet;
    }

    private async Task<PurchaseReceiptResponse> PostFullReceipt(OrderSeed order,
        DeliverySchedule schedule, Unit unit, DateOnly effectiveDate, long expectedOrderVersion)
    {
        await using var db = await Db();
        var warehouse = new Warehouse($"WH-{NextNumber()}", "انبار تست جریان نقدی",
            "Cash-flow test warehouse", null);
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync();

        using var createResponse = await _client.PostAsJsonAsync("/api/purchase-receipts",
            new CreatePurchaseReceiptRequest(order.Order.Id, warehouse.Id, effectiveDate,
                "cash-flow test receipt",
                [new PurchaseReceiptLineInput(order.Line.Id, schedule.Id,
                    schedule.AllocatedQuantity - order.Cancellations.GetValueOrDefault(schedule.Id))]));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var draft = await createResponse.Content.ReadFromJsonAsync<PurchaseReceiptResponse>();
        Assert.NotNull(draft);

        using var postResponse = await _client.PostAsJsonAsync(
            $"/api/purchase-receipts/{draft!.Id}/post",
            new PostPurchaseReceiptRequest(draft.Version, expectedOrderVersion));
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        return (await postResponse.Content.ReadFromJsonAsync<PurchaseReceiptResponse>())!;
    }

    private static async Task<MasterSeed> AddMaster(HadronDbContext db)
    {
        var number = NextNumber();
        var country = new ReferenceValue(CountryTypeId, $"CF-C-{number}", $"CF-C-{number}",
            "کشور تست", "Test country", 0);
        var payment = new ReferenceValue(PaymentTermTypeId, $"CF-P-{number}", $"CF-P-{number}",
            "شرایط تست", "Test payment", 0);
        var piece = new Unit($"CF-PCS-{number}", $"CF-PCS-{number}", "عدد", "Piece", "pcs", 0);
        var kilogram = new Unit($"CF-KG-{number}", $"CF-KG-{number}", "کیلوگرم", "Kilogram", "kg", 1);
        var primaryCode = NextCurrencyCode();
        var secondaryCode = NextCurrencyCode();
        var primary = new Currency(primaryCode, primaryCode, "ارز اول", "Primary currency", null, 2);
        var secondary = new Currency(secondaryCode, secondaryCode, "ارز دوم", "Secondary currency", null, 2);
        var supplier = new Supplier($"CF-SUP-{number}", "تأمین‌کننده تست جریان نقدی",
            $"CF-SUPPLIER-{number}", country.Id, null, null, null, null);
        supplier.ChangeState(true);
        supplier.PaymentProfile.Set(payment.Id, 20m, 30m, 50m, 10,
            PurchaseMilestoneCodes.WarehouseReceipt);
        db.AddRange(country, payment, piece, kilogram, primary, secondary, supplier);
        await db.SaveChangesAsync();
        return new(country, payment, piece, kilogram, primary, secondary, supplier);
    }

    private static async Task<PurchasableSeed> AddPurchasableItem(HadronDbContext db,
        MasterSeed master, Unit unit, string name)
    {
        var item = await AddItem(db, ItemTypes.Component, unit, name);
        var option = new SourcingOption($"CF-SRC-{NextNumber()}", item.Id, master.Supplier.Id,
            unit.Id, SourcingRouteScopes.Domestic, null, null, null, 1, null, 1, null);
        db.SourcingOptions.Add(option);
        await db.SaveChangesAsync();
        return new(item, option);
    }

    private static async Task<Item> AddItem(HadronDbContext db, string type, Unit unit, string name)
    {
        var prefix = type == ItemTypes.Product ? "PRD" : type == ItemTypes.Part ? "PRT" : "CMP";
        var item = new Item(type, $"{prefix}-{NextNumber()}", name, name, unit.Id, null);
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    private static async Task<OrderSeed> AddOrder(HadronDbContext db, MasterSeed master,
        PurchasableSeed purchasable, Currency currency, decimal unitPrice,
        decimal advance, decimal beforeShipment, decimal credit, int? creditDays,
        string? creditAnchor, bool ordered, IReadOnlyList<ScheduleSeed> scheduleSeeds,
        DateOnly? orderedDate = null)
    {
        var order = new PurchaseOrder($"PO-{NextNumber()}", master.Supplier.Id, currency.Id,
            master.PaymentTerm.Id, advance, beforeShipment, credit, creditDays, creditAnchor,
            null, null, null);
        var line = new PurchaseOrderLine(order.Id, 1, purchasable.Item.Id, purchasable.Option.Id,
            null, purchasable.Item.BaseUnitId, scheduleSeeds.Sum(x => x.Quantity), unitPrice,
            null, 1, null, 1, null, null);
        var schedules = scheduleSeeds.Select((seed, index) =>
            new DeliverySchedule(line.Id, index + 1, seed.Quantity, null)).ToArray();
        var now = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc);
        db.AddRange(order, line);
        db.DeliverySchedules.AddRange(schedules);
        await db.SaveChangesAsync();

        order.Approve(now);
        await db.SaveChangesAsync();
        if (ordered)
        {
            order.MarkOrdered(orderedDate ?? new DateOnly(2026, 8, 1), now.AddMinutes(1));
            await db.SaveChangesAsync();
        }

        for (var index = 0; index < scheduleSeeds.Count; index++)
        {
            var seed = scheduleSeeds[index];
            if (seed.Original is not null)
                db.DeliverySchedulePlanRevisions.Add(Plan(schedules[index].Id,
                    PurchasePlanTypes.Original, 1, seed.Original, null, now));
            if (seed.Revised is not null)
                db.DeliverySchedulePlanRevisions.Add(Plan(schedules[index].Id,
                    PurchasePlanTypes.Revised, 2, seed.Revised, "cash-flow revised plan", now.AddHours(1)));
        }
        await db.SaveChangesAsync();

        var cancellations = new Dictionary<Guid, decimal>();
        for (var index = 0; index < scheduleSeeds.Count; index++)
        {
            var quantity = scheduleSeeds[index].CancelledQuantity;
            if (quantity <= 0) continue;
            var schedule = schedules[index];
            db.PurchaseScheduleCancellations.Add(new PurchaseScheduleCancellation(order.Id,
                line.Id, schedule.Id, quantity, new DateOnly(2026, 8, 1),
                "cash-flow cancellation", "cash-flow-test", now.AddDays(1)));
            cancellations[schedule.Id] = quantity;
        }
        await db.SaveChangesAsync();
        return new(order, line, schedules, cancellations);
    }

    private static DeliverySchedulePlanRevision Plan(Guid scheduleId, string type, int revision,
        PlanDates dates, string? reason, DateTime createdAtUtc) => new(scheduleId, type, revision,
        dates.SupplierReady, null, null, null, null, dates.WarehouseReceipt,
        reason, "cash-flow-test", createdAtUtc);

    private async Task<HadronDbContext> Db()
        => await _factory.Services.GetRequiredService<IDbContextFactory<HadronDbContext>>()
            .CreateDbContextAsync();

    private static int NextNumber() => Interlocked.Increment(ref _businessSequence);

    private static string NextCurrencyCode()
    {
        var value = Interlocked.Increment(ref _currencySequence) - 1;
        return $"Q{(char)('A' + value / 26 % 26)}{(char)('A' + value % 26)}";
    }

    private static string NextBomNumber() => $"BOM-CF-{NextNumber()}";

    private sealed class EfCommandCapture :
        IObserver<DiagnosticListener>,
        IObserver<KeyValuePair<string, object?>>,
        IDisposable
    {
        private readonly ConcurrentQueue<string> _commands = new();
        private readonly List<IDisposable> _listenerSubscriptions = [];
        private readonly IDisposable _allListenersSubscription;
        private readonly object _sync = new();
        private bool _disposed;

        public EfCommandCapture()
        {
            _allListenersSubscription = DiagnosticListener.AllListeners.Subscribe(this);
        }

        public IReadOnlyList<string> Commands => _commands.ToArray();

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name != DbLoggerCategory.Name) return;
            lock (_sync)
            {
                if (_disposed) return;
                _listenerSubscriptions.Add(listener.Subscribe(this,
                    eventName => eventName == RelationalEventId.CommandExecuting.Name));
            }
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Key == RelationalEventId.CommandExecuting.Name &&
                value.Value is CommandEventData eventData)
                _commands.Enqueue(eventData.Command.CommandText);
        }

        public void OnCompleted() { }

        public void OnError(Exception error) { }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                foreach (var subscription in _listenerSubscriptions) subscription.Dispose();
                _listenerSubscriptions.Clear();
                _allListenersSubscription.Dispose();
            }
        }
    }

    private sealed record MasterSeed(ReferenceValue Country, ReferenceValue PaymentTerm,
        Unit PieceUnit, Unit KilogramUnit, Currency PrimaryCurrency, Currency SecondaryCurrency,
        Supplier Supplier);
    private sealed record PurchasableSeed(Item Item, SourcingOption Option);
    private sealed record PlanDates(DateOnly? SupplierReady, DateOnly? WarehouseReceipt);
    private sealed record ScheduleSeed(decimal Quantity, PlanDates? Original, PlanDates? Revised,
        decimal CancelledQuantity);
    private sealed record OrderSeed(PurchaseOrder Order, PurchaseOrderLine Line,
        DeliverySchedule[] Schedules, IReadOnlyDictionary<Guid, decimal> Cancellations);
    private sealed record JalaliDateResponse(string JalaliDate, DateOnly GregorianDate);
}
