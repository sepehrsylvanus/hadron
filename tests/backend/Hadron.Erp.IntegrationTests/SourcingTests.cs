using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Hadron.Erp.Api.Modules.Sourcing.Domain;
using Hadron.Erp.Api.Modules.Suppliers.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Hadron.Erp.IntegrationTests;

[Collection("ReferenceData")]
public sealed class SourcingTests : IClassFixture<FoundationApiFactory>, IAsyncLifetime
{
    private const string Date = "2026-08-23";
    private readonly FoundationApiFactory _factory;
    private readonly HttpClient _client;
    private Guid _unit;
    private Guid _currency;
    private Guid _shipping;
    private Guid _incoterm;
    private Guid _item;
    private Guid _supplier;

    public SourcingTests(FoundationApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await using var db = await Db();
        await db.Database.MigrateAsync();
        await Cleanup(db);
        _unit = (await AddUnit(db, "KG-SRC")).Id;
        _currency = (await AddCurrency(db, "USD")).Id;
        _shipping = (await AddReference(db, "SHIPPING_MODE", "SEA-SRC")).Id;
        _incoterm = (await AddReference(db, "INCOTERM", "FOB-SRC")).Id;
        var country = await AddReference(db, "COUNTRY", "IR-SRC");
        var item = new Item(ItemTypes.Part, $"PRT-{Guid.NewGuid():N}"[..20], "SRC-PART", "قطعه تأمین", "Sourcing Part", _unit, null);
        db.Items.Add(item);
        db.MaterialItemProfiles.Add(new(item.Id, (await AddReference(db, "PART_CATEGORY", "SRC-CAT")).Id,
            SupplyMethods.Buy, null, 0, 0, 0, 0, 0));
        var supplier = new Supplier($"SUP-{Guid.NewGuid():N}"[..20], "تأمین‌کننده تأمین‌یابی", "تأمین‌کننده تأمین‌یابی",
            country.Id, null, null, null, null);
        supplier.ChangeState(true);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        _item = item.Id;
        _supplier = supplier.Id;
        _factory.AuditSink.Clear();
    }

    public async Task DisposeAsync()
    {
        await using var db = await Db();
        await Cleanup(db);
    }

    [Fact]
    public async Task Multiple_routes_are_allowed_and_relationship_snapshot_are_server_owned_immutable()
    {
        var first = await Created(await Create());
        var second = await Created(await Create());
        Assert.NotEqual(first.Id, second.Id);
        Assert.Matches("^SRC-[0-9]{4,}$", first.Number);
        Assert.False(first.Preferred);
        Assert.Equal(_unit, first.BaseUnitId);

        var otherItem = await AddItem(SupplyMethods.Buy, "SRC-OTHER");
        var response = await _client.PutAsJsonAsync(Url(first.Id), new
        {
            itemId = otherItem,
            supplierId = Guid.NewGuid(),
            itemBaseUnitId = Guid.NewGuid(),
            isPreferred = true,
            minimumOrderQuantity = 0m,
            shippingModeId = (Guid?)null,
            incotermId = (Guid?)null,
            supplierLeadTimeDays = (int?)null,
            forwarderToShipmentDays = (int?)null,
            shippingPeriodDays = (int?)null,
            destinationCustomsDays = (int?)null,
            expectedVersion = first.Version
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var edited = await Read(response);
        Assert.Equal(_item, edited.ItemId);
        Assert.Equal(_supplier, edited.SupplierId);
        Assert.Equal(_unit, edited.BaseUnitId);
        Assert.False(edited.Preferred);
        Assert.Equal(0m, edited.Moq);
        Assert.Equal(first.Version + 1, edited.Version);
    }

    [Fact]
    public async Task Concurrent_numbers_are_unique_and_item_compatibility_is_explicit()
    {
        var creates = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Create()));
        Assert.All(creates, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var options = new List<OptionDto>();
        foreach (var response in creates) options.Add(await Read(response));
        Assert.Equal(options.Count, options.Select(x => x.Number).Distinct().Count());
        Assert.All(options, x => Assert.Matches("^SRC-[0-9]{4,}$", x.Number));

        var product = await AddItem(null, "SRC-PRODUCT", ItemTypes.Product);
        var make = await AddItem(SupplyMethods.Make, "SRC-MAKE");
        var buy = await AddItem(SupplyMethods.Buy, "SRC-BUY");
        var outsource = await AddItem(SupplyMethods.Outsource, "SRC-OUTSOURCE");
        var hybrid = await AddItem(SupplyMethods.Hybrid, "SRC-HYBRID");
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(itemId: product)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(itemId: make)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Create(itemId: buy)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Create(itemId: outsource)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Create(itemId: hybrid)).StatusCode);
    }

    [Fact]
    public async Task Preferred_command_locks_item_enforces_single_active_and_reports_stale_state()
    {
        var first = await Created(await Create());
        var second = await Created(await Create());
        var requests = new[] { first, second }.Select(x => _client.PostAsJsonAsync(
            $"/api/sourcing-options/{x.Id}/set-preferred?evaluationDate={Date}",
            new
            {
                expectedVersion = x.Version,
                expectedCurrentPreferredId = (Guid?)null,
                expectedCurrentPreferredVersion = (long?)null,
                reason = "test"
            })).ToArray();
        var results = await Task.WhenAll(requests);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
        var conflict = Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("PREFERRED_STATE_CHANGED", await ProblemCode(conflict));

        await using (var db = await Db())
        {
            Assert.Equal(1, await db.SourcingOptions.CountAsync(x => x.ItemId == _item && x.IsActive && x.IsPreferred));
            var loserId = first.Id == (await Read(results.Single(x => x.StatusCode == HttpStatusCode.OK))).Id
                ? second.Id
                : first.Id;
            await using var transaction = await db.Database.BeginTransactionAsync();
            var violation = await Assert.ThrowsAsync<PostgresException>(async () =>
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE procurement.sourcing_options SET \"IsPreferred\" = TRUE WHERE \"Id\" = {loserId}"));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, violation.SqlState);
            await transaction.RollbackAsync();
        }

        var winner = await Read(results.Single(x => x.StatusCode == HttpStatusCode.OK));
        var deactivated = await _client.PostAsJsonAsync($"/api/sourcing-options/{winner.Id}/deactivate?evaluationDate={Date}",
            new { expectedVersion = winner.Version, reason = "test" });
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        Assert.False((await Read(deactivated)).Preferred);
        var inactivePreferred = await _client.PostAsJsonAsync($"/api/sourcing-options/{winner.Id}/set-preferred?evaluationDate={Date}",
            new
            {
                expectedVersion = winner.Version + 1,
                expectedCurrentPreferredId = (Guid?)null,
                expectedCurrentPreferredVersion = (long?)null
            });
        Assert.Equal(HttpStatusCode.BadRequest, inactivePreferred.StatusCode);
    }

    [Fact]
    public async Task Preferred_and_lifecycle_share_lock_and_reactivation_never_restores_preferred()
    {
        var current = await Created(await Create());
        var target = await Created(await Create());
        current = await Read(await SetPreferred(current, null));
        Assert.True(current.Preferred);

        var setTask = SetPreferred(target, current);
        var deactivateTask = _client.PostAsJsonAsync(
            $"/api/sourcing-options/{current.Id}/deactivate?evaluationDate={Date}",
            new { expectedVersion = current.Version, reason = "race" });
        var setResult = await setTask;
        var lifecycleResult = await deactivateTask;
        Assert.Single(new[] { setResult, lifecycleResult }, x => x.StatusCode == HttpStatusCode.OK);
        var conflict = Assert.Single(new[] { setResult, lifecycleResult }, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(setResult == conflict ? "PREFERRED_STATE_CHANGED" : "STALE_VERSION", await ProblemCode(conflict));
        await using (var db = await Db())
            Assert.True(await db.SourcingOptions.CountAsync(x => x.ItemId == _item && x.IsActive && x.IsPreferred) <= 1);

        var latestCurrent = await Get(current.Id);
        if (latestCurrent.Active)
            latestCurrent = await Read(await _client.PostAsJsonAsync(
                $"/api/sourcing-options/{current.Id}/deactivate?evaluationDate={Date}",
                new { expectedVersion = latestCurrent.Version }));
        Assert.False(latestCurrent.Preferred);
        var reactivated = await Read(await _client.PostAsJsonAsync(
            $"/api/sourcing-options/{current.Id}/reactivate?evaluationDate={Date}",
            new { expectedVersion = latestCurrent.Version }));
        Assert.True(reactivated.Active);
        Assert.False(reactivated.Preferred);
    }

    [Fact]
    public async Task Mutations_advance_version_stale_requests_fail_and_audit_has_only_successes()
    {
        _factory.AuditSink.Clear();
        var option = await Created(await Create());
        Assert.Contains(_factory.AuditSink.Snapshot(), x => x.EntityId == option.Id && x.Action == "CREATE");

        var editedResponse = await Update(option, moq: 1.000001m);
        var edited = await Read(editedResponse);
        Assert.Equal(option.Version + 1, edited.Version);
        Assert.Contains(_factory.AuditSink.Snapshot(), x => x.EntityId == option.Id && x.Action == "EDIT");
        var successfulEvents = _factory.AuditSink.Snapshot().Count;

        var staleEdit = await Update(option, moq: 2m);
        Assert.Equal(HttpStatusCode.Conflict, staleEdit.StatusCode);
        Assert.Equal("STALE_VERSION", await ProblemCode(staleEdit));
        var staleDeactivate = await _client.PostAsJsonAsync($"/api/sourcing-options/{option.Id}/deactivate?evaluationDate={Date}",
            new { expectedVersion = option.Version });
        Assert.Equal(HttpStatusCode.Conflict, staleDeactivate.StatusCode);
        Assert.Equal("STALE_VERSION", await ProblemCode(staleDeactivate));
        Assert.Equal(successfulEvents, _factory.AuditSink.Snapshot().Count);

        var inactive = await Read(await _client.PostAsJsonAsync($"/api/sourcing-options/{option.Id}/deactivate?evaluationDate={Date}",
            new { expectedVersion = edited.Version }));
        Assert.Equal(edited.Version + 1, inactive.Version);
        var staleReactivate = await _client.PostAsJsonAsync($"/api/sourcing-options/{option.Id}/reactivate?evaluationDate={Date}",
            new { expectedVersion = edited.Version });
        Assert.Equal(HttpStatusCode.Conflict, staleReactivate.StatusCode);
        Assert.Equal("STALE_VERSION", await ProblemCode(staleReactivate));
        var active = await Read(await _client.PostAsJsonAsync($"/api/sourcing-options/{option.Id}/reactivate?evaluationDate={Date}",
            new { expectedVersion = inactive.Version }));
        Assert.Equal(inactive.Version + 1, active.Version);

        var priceResponse = await _client.PostAsJsonAsync($"/api/sourcing-options/{option.Id}/prices?evaluationDate={Date}",
            new { unitPrice = 5m, currencyId = _currency, effectiveFrom = "2040-01-01", expectedVersion = active.Version });
        var priced = await Read(priceResponse);
        Assert.Equal(active.Version + 1, priced.Version);
        Assert.Contains(_factory.AuditSink.Snapshot(), x => x.EntityId == option.Id && x.Action == "PRICE_ADD");
        var afterPriceEvents = _factory.AuditSink.Snapshot().Count;
        var stalePrice = await _client.PostAsJsonAsync($"/api/sourcing-options/{option.Id}/prices?evaluationDate={Date}",
            new { unitPrice = 6m, currencyId = _currency, effectiveFrom = "2041-01-01", expectedVersion = active.Version });
        Assert.Equal(HttpStatusCode.Conflict, stalePrice.StatusCode);
        Assert.Equal("STALE_VERSION", await ProblemCode(stalePrice));
        var invalidPrice = await _client.PostAsJsonAsync($"/api/sourcing-options/{option.Id}/prices?evaluationDate={Date}",
            new { unitPrice = -1m, currencyId = _currency, effectiveFrom = "2041-01-01", expectedVersion = priced.Version });
        Assert.Equal(HttpStatusCode.BadRequest, invalidPrice.StatusCode);
        Assert.Equal(afterPriceEvents, _factory.AuditSink.Snapshot().Count);

        var preferredTarget = await Created(await Create());
        var preferred = await Read(await SetPreferred(preferredTarget, null));
        Assert.Equal(preferredTarget.Version + 1, preferred.Version);
        Assert.Contains(_factory.AuditSink.Snapshot(), x => x.EntityId == preferred.Id && x.Action == "SET_PREFERRED");
        var afterPreferredEvents = _factory.AuditSink.Snapshot().Count;
        var stalePreferred = await SetPreferred(option, null);
        Assert.Equal(HttpStatusCode.Conflict, stalePreferred.StatusCode);
        Assert.Equal("PREFERRED_STATE_CHANGED", await ProblemCode(stalePreferred));
        Assert.Equal(afterPreferredEvents, _factory.AuditSink.Snapshot().Count);
    }

    [Fact]
    public async Task Moq_and_all_four_lead_stages_distinguish_null_zero_positive_and_negative()
    {
        var option = await Created(await Create());
        Assert.Null(option.Moq);
        Assert.Null(option.TotalLeadTime);
        Assert.Contains("MOQ_MISSING", option.Reasons);
        Assert.Contains("SUPPLIER_LEAD_TIME_MISSING", option.Reasons);
        Assert.Contains("FORWARDER_LEAD_TIME_MISSING", option.Reasons);
        Assert.Contains("SHIPPING_PERIOD_MISSING", option.Reasons);
        Assert.Contains("DESTINATION_CUSTOMS_MISSING", option.Reasons);

        var zero = await Read(await Update(option, 0m, _shipping, null, 0, 0, 0, 0));
        Assert.Equal(0m, zero.Moq);
        Assert.Equal(0, zero.TotalLeadTime);
        Assert.DoesNotContain("INCOTERM_MISSING", zero.Reasons);
        var ready = await Read(await _client.PostAsJsonAsync($"/api/sourcing-options/{option.Id}/prices?evaluationDate={Date}",
            new { unitPrice = 0m, currencyId = _currency, effectiveFrom = "2026-01-01", expectedVersion = zero.Version }));
        Assert.Equal("READY", ready.Readiness);
        Assert.DoesNotContain("INCOTERM_MISSING", ready.Reasons);

        var exact = await Read(await Update(ready, 12.345678m, _shipping, null, 1, 2, 3, 4));
        Assert.Equal(12.345678m, exact.Moq);
        Assert.Equal(10, exact.TotalLeadTime);
        var oneMissing = await Read(await Update(exact, 12.345678m, _shipping, null, 1, 2, 3, null));
        Assert.Null(oneMissing.TotalLeadTime);
        Assert.Equal("INCOMPLETE_DATA", oneMissing.Readiness);
        Assert.Contains("DESTINATION_CUSTOMS_MISSING", oneMissing.Reasons);

        var negativeMoq = await Update(oneMissing, -0.000001m, _shipping, null, 1, 2, 3, 4);
        Assert.Equal(HttpStatusCode.BadRequest, negativeMoq.StatusCode);
        var negativeStages = new[]
        {
            (Supplier: (int?)-1, Forwarder: (int?)0, Shipping: (int?)0, Customs: (int?)0),
            (Supplier: (int?)0, Forwarder: (int?)-1, Shipping: (int?)0, Customs: (int?)0),
            (Supplier: (int?)0, Forwarder: (int?)0, Shipping: (int?)-1, Customs: (int?)0),
            (Supplier: (int?)0, Forwarder: (int?)0, Shipping: (int?)0, Customs: (int?)-1)
        };
        foreach (var stages in negativeStages)
        {
            var response = await Update(oneMissing, 0m, _shipping, null, stages.Supplier, stages.Forwarder,
                stages.Shipping, stages.Customs);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.Equal(oneMissing.Version, (await Get(option.Id)).Version);
    }

    [Fact]
    public async Task Prices_are_exact_zero_valid_inclusive_nonoverlapping_and_evaluation_date_is_explicit()
    {
        var option = await Created(await Create(moq: 0m, complete: true));
        var missingDate = await _client.GetAsync($"/api/sourcing-options/{option.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, missingDate.StatusCode);
        var first = await _client.PostAsJsonAsync($"/api/sourcing-options/{option.Id}/prices?evaluationDate=2026-08-05",
            new { unitPrice = 0m, currencyId = _currency, effectiveFrom = "2026-08-01", effectiveTo = "2026-08-10", expectedVersion = option.Version });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var afterFirst = await Read(first);
        Assert.Equal("2026-08-05", afterFirst.EvaluationDate);
        Assert.Equal(0m, afterFirst.CurrentPrice);
        Assert.Equal("READY", afterFirst.Readiness);
        var auditCountAfterFirst = _factory.AuditSink.Snapshot().Count;
        var readyList = await _client.GetAsync($"/api/sourcing-options?evaluationDate=2026-08-05&readiness=READY&pageSize=25");
        Assert.Equal(HttpStatusCode.OK, readyList.StatusCode);
        using (var readyJson = JsonDocument.Parse(await readyList.Content.ReadAsStringAsync()))
            Assert.Contains(readyJson.RootElement.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == option.Id);

        var inclusiveOverlap = await _client.PostAsJsonAsync($"/api/sourcing-options/{option.Id}/prices?evaluationDate={Date}",
            new { unitPrice = 1.123456m, currencyId = _currency, effectiveFrom = "2026-08-10", effectiveTo = "2026-08-20", expectedVersion = afterFirst.Version });
        Assert.Equal(HttpStatusCode.Conflict, inclusiveOverlap.StatusCode);
        Assert.Equal("PRICE_PERIOD_OVERLAP", await ProblemCode(inclusiveOverlap));
        Assert.Equal(auditCountAfterFirst, _factory.AuditSink.Snapshot().Count);

        var next = await _client.PostAsJsonAsync($"/api/sourcing-options/{option.Id}/prices?evaluationDate=2026-08-11",
            new { unitPrice = 1.123456m, currencyId = _currency, effectiveFrom = "2026-08-11", effectiveTo = (string?)null, expectedVersion = afterFirst.Version });
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        var detail = await Read(next);
        Assert.Equal(1.123456m, detail.CurrentPrice);
        Assert.Equal(2, detail.PriceCount);
        Assert.Null((await Get(option.Id, "2026-07-31")).CurrentPrice);
        Assert.Equal(0m, (await Get(option.Id, "2026-08-10")).CurrentPrice);
        Assert.Equal(1.123456m, (await Get(option.Id, "2030-01-01")).CurrentPrice);
        using var nextJson = JsonDocument.Parse(await next.Content.ReadAsStringAsync());
        var openPrice = nextJson.RootElement.GetProperty("prices").EnumerateArray()
            .Single(x => x.GetProperty("unitPrice").GetDecimal() == 1.123456m);
        Assert.Equal(JsonValueKind.Null, openPrice.GetProperty("effectiveTo").ValueKind);
        var openPriceId = openPrice.GetProperty("id").GetGuid();

        _factory.AuditSink.Clear();
        var closedResponse = await _client.PostAsJsonAsync(
            $"/api/sourcing-options/{option.Id}/prices/{openPriceId}/close?evaluationDate=2026-08-20",
            new { effectiveTo = "2026-08-20", expectedVersion = detail.Version, reason = "close" });
        var closed = await Read(closedResponse);
        Assert.Equal(detail.Version + 1, closed.Version);
        Assert.Equal(2, closed.PriceCount);
        Assert.Equal(1.123456m, closed.CurrentPrice);
        using (var closedJson = JsonDocument.Parse(await closedResponse.Content.ReadAsStringAsync()))
            Assert.Contains(closedJson.RootElement.GetProperty("prices").EnumerateArray(),
                x => x.GetProperty("id").GetGuid() == openPriceId && x.GetProperty("effectiveTo").GetString() == "2026-08-20");
        Assert.Null((await Get(option.Id, "2026-08-21")).CurrentPrice);
        Assert.Contains(_factory.AuditSink.Snapshot(), x => x.EntityId == option.Id && x.Action == "PRICE_CLOSE");
        var staleClose = await _client.PostAsJsonAsync(
            $"/api/sourcing-options/{option.Id}/prices/{openPriceId}/close?evaluationDate={Date}",
            new { effectiveTo = "2026-08-19", expectedVersion = detail.Version });
        Assert.Equal(HttpStatusCode.Conflict, staleClose.StatusCode);
        Assert.Equal("STALE_VERSION", await ProblemCode(staleClose));

        var negative = await _client.PostAsJsonAsync($"/api/sourcing-options/{option.Id}/prices?evaluationDate={Date}",
            new { unitPrice = -0.000001m, currencyId = _currency, effectiveFrom = "2030-01-01", expectedVersion = closed.Version });
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
        var missingCurrency = await _client.PostAsJsonAsync($"/api/sourcing-options/{option.Id}/prices?evaluationDate={Date}",
            new { unitPrice = 10m, currencyId = Guid.Empty, effectiveFrom = "2030-01-01", expectedVersion = closed.Version });
        Assert.Equal(HttpStatusCode.BadRequest, missingCurrency.StatusCode);
        var invalidPeriod = await _client.PostAsJsonAsync($"/api/sourcing-options/{option.Id}/prices?evaluationDate={Date}",
            new { unitPrice = 10m, currencyId = _currency, effectiveFrom = "2030-02-01", effectiveTo = "2030-01-01", expectedVersion = closed.Version });
        Assert.Equal(HttpStatusCode.BadRequest, invalidPeriod.StatusCode);
    }

    [Fact]
    public async Task Concurrent_overlapping_price_writes_are_stopped_by_database_exclusion_constraint()
    {
        var option = await Created(await Create());
        async Task<bool> TryInsert(decimal value)
        {
            await using var db = await Db();
            db.SourcingPrices.Add(new SourcingPrice(option.Id, value, _currency,
                new DateOnly(2035, 1, 1), new DateOnly(2035, 12, 31)));
            try { await db.SaveChangesAsync(); return true; }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation })
            { return false; }
        }
        var outcomes = await Task.WhenAll(TryInsert(10m), TryInsert(20m));
        Assert.Single(outcomes, x => x);
        Assert.Single(outcomes, x => !x);
    }

    [Fact]
    public async Task Readiness_precedence_base_unit_drift_and_reactivation_rules_are_enforced()
    {
        var option = await Created(await Create(moq: 0m, complete: true));
        Assert.Equal("INCOMPLETE_DATA", option.Readiness);
        Assert.Contains("CURRENT_PRICE_MISSING", option.Reasons);
        var pricedResponse = await _client.PostAsJsonAsync($"/api/sourcing-options/{option.Id}/prices?evaluationDate={Date}",
            new { unitPrice = 100.123456m, currencyId = _currency, effectiveFrom = "2026-01-01", expectedVersion = option.Version });
        var ready = await Read(pricedResponse);
        Assert.Equal("READY", ready.Readiness);
        Assert.Equal(10, ready.TotalLeadTime);

        await using (var db = await Db())
        {
            var supplier = await db.Suppliers.SingleAsync(x => x.Id == _supplier);
            supplier.ChangeState(false);
            await db.SaveChangesAsync();
        }
        var supplierBlocked = await Get(option.Id);
        Assert.Equal("BLOCKED", supplierBlocked.Readiness);
        Assert.Contains("SUPPLIER_INACTIVE", supplierBlocked.Reasons);
        await using (var db = await Db())
        {
            var supplier = await db.Suppliers.SingleAsync(x => x.Id == _supplier);
            supplier.ChangeState(true);
            await db.SaveChangesAsync();
        }

        var replacementUnit = await AddUnitToDatabase("EA-SRC");
        await using (var db = await Db())
        {
            var item = await db.Items.SingleAsync(x => x.Id == _item);
            item.Edit(item.Code, item.NameFa, item.NameEn, replacementUnit, item.Notes);
            await db.SaveChangesAsync();
        }
        var blocked = await Get(option.Id);
        Assert.Equal("BLOCKED", blocked.Readiness);
        Assert.Contains("BASE_UNIT_CHANGED", blocked.Reasons);
        Assert.Equal(_unit, blocked.BaseUnitId);
        Assert.Equal(100.123456m, blocked.CurrentPrice);

        var deactivated = await Read(await _client.PostAsJsonAsync($"/api/sourcing-options/{option.Id}/deactivate?evaluationDate={Date}",
            new { expectedVersion = blocked.Version }));
        Assert.Equal("INACTIVE", deactivated.Readiness);
        var reactivate = await _client.PostAsJsonAsync($"/api/sourcing-options/{option.Id}/reactivate?evaluationDate={Date}",
            new { expectedVersion = deactivated.Version });
        Assert.Equal(HttpStatusCode.BadRequest, reactivate.StatusCode);
    }

    [Fact]
    public async Task Pure_make_is_rejected_but_incomplete_external_option_can_reactivate()
    {
        var make = await AddItem(SupplyMethods.Make, "SRC-MAKE");
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(itemId: make)).StatusCode);
        var draft = await Created(await Create());
        var inactive = await Read(await _client.PostAsJsonAsync($"/api/sourcing-options/{draft.Id}/deactivate?evaluationDate={Date}",
            new { expectedVersion = draft.Version }));
        var response = await _client.PostAsJsonAsync($"/api/sourcing-options/{draft.Id}/reactivate?evaluationDate={Date}",
            new { expectedVersion = inactive.Version });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("INCOMPLETE_DATA", (await Read(response)).Readiness);
    }

    [Fact]
    public async Task Historical_inactive_references_resolve_and_audit_is_post_commit_best_effort()
    {
        var option = await Created(await Create(complete: true));
        await using (var db = await Db())
        {
            var shipping = await db.ReferenceValues.SingleAsync(x => x.Id == _shipping);
            shipping.SetActive(false);
            await db.SaveChangesAsync();
        }
        var historical = await Get(option.Id);
        Assert.Equal("BLOCKED", historical.Readiness);
        Assert.Contains("SHIPPING_MODE_INACTIVE", historical.Reasons);
        Assert.Equal(_shipping, historical.ShippingModeId);

        _factory.AuditSink.ThrowOnWrite = true;
        var response = await _client.PutAsJsonAsync(Url(option.Id), new
        {
            minimumOrderQuantity = 12.345678m,
            shippingModeId = _shipping,
            incotermId = _incoterm,
            supplierLeadTimeDays = 1,
            forwarderToShipmentDays = 2,
            shippingPeriodDays = 3,
            destinationCustomsDays = 4,
            expectedVersion = historical.Version
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await Get(option.Id);
        Assert.Equal(12.345678m, updated.Moq);
        var inactive = await Read(await _client.PostAsJsonAsync($"/api/sourcing-options/{option.Id}/deactivate?evaluationDate={Date}",
            new { expectedVersion = updated.Version }));
        var reactivate = await _client.PostAsJsonAsync($"/api/sourcing-options/{option.Id}/reactivate?evaluationDate={Date}",
            new { expectedVersion = inactive.Version });
        Assert.Equal(HttpStatusCode.BadRequest, reactivate.StatusCode);
    }

    [Fact]
    public async Task Migration_contains_database_integrity_and_scope_has_no_payment_or_future_modules()
    {
        await using var db = await Db();
        var types = await db.Database.SqlQueryRaw<ColumnInfo>("""
            SELECT column_name AS "ColumnName", data_type AS "DataType", numeric_precision AS "NumericPrecision",
                   numeric_scale AS "NumericScale"
            FROM information_schema.columns
            WHERE table_schema='procurement' AND table_name IN ('sourcing_options','sourcing_prices')
            """).ToListAsync();
        Assert.Contains(types, x => x.ColumnName == "MinimumOrderQuantity" && x.NumericPrecision == 18 && x.NumericScale == 6);
        Assert.Contains(types, x => x.ColumnName == "UnitPrice" && x.NumericPrecision == 18 && x.NumericScale == 6);
        var constraints = await db.Database.SqlQueryRaw<string>("""
            SELECT conname AS "Value" FROM pg_constraint
            WHERE conname IN ('ex_sourcing_prices_no_overlap','ux_sourcing_options_active_preferred_item')
            UNION ALL
            SELECT indexname AS "Value" FROM pg_indexes
            WHERE indexname='ux_sourcing_options_active_preferred_item'
            """).ToListAsync();
        Assert.Contains("ex_sourcing_prices_no_overlap", constraints);
        Assert.Contains("ux_sourcing_options_active_preferred_item", constraints);
        var columns = types.Select(x => x.ColumnName).ToArray();
        Assert.DoesNotContain(columns, x => x.Contains("Payment", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(columns, x => x.Contains("Inventory", StringComparison.OrdinalIgnoreCase) ||
            x.Contains("Bom", StringComparison.OrdinalIgnoreCase) || x.Contains("Ready", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<HttpResponseMessage> Create(decimal? moq = null, bool complete = false, Guid? itemId = null) =>
        await _client.PostAsJsonAsync($"/api/sourcing-options?evaluationDate={Date}", new
        {
            itemId = itemId ?? _item,
            supplierId = _supplier,
            minimumOrderQuantity = moq,
            shippingModeId = complete ? _shipping : (Guid?)null,
            incotermId = complete ? _incoterm : (Guid?)null,
            supplierLeadTimeDays = complete ? 1 : (int?)null,
            forwarderToShipmentDays = complete ? 2 : (int?)null,
            shippingPeriodDays = complete ? 3 : (int?)null,
            destinationCustomsDays = complete ? 4 : (int?)null,
            isPreferred = true,
            isReady = true,
            readiness = "READY",
            itemBaseUnitId = Guid.NewGuid()
        });

    private string Url(Guid id) => $"/api/sourcing-options/{id}?evaluationDate={Date}";
    private async Task<OptionDto> Get(Guid id) => await Read(await _client.GetAsync(Url(id)));
    private async Task<OptionDto> Get(Guid id, string evaluationDate) =>
        await Read(await _client.GetAsync($"/api/sourcing-options/{id}?evaluationDate={evaluationDate}"));

    private async Task<HttpResponseMessage> Update(
        OptionDto option,
        decimal? moq,
        Guid? shippingModeId = null,
        Guid? incotermId = null,
        int? supplierLeadTimeDays = null,
        int? forwarderToShipmentDays = null,
        int? shippingPeriodDays = null,
        int? destinationCustomsDays = null) =>
        await _client.PutAsJsonAsync(Url(option.Id), new
        {
            minimumOrderQuantity = moq,
            shippingModeId,
            incotermId,
            supplierLeadTimeDays,
            forwarderToShipmentDays,
            shippingPeriodDays,
            destinationCustomsDays,
            expectedVersion = option.Version
        });

    private async Task<HttpResponseMessage> SetPreferred(OptionDto target, OptionDto? current) =>
        await _client.PostAsJsonAsync(
            $"/api/sourcing-options/{target.Id}/set-preferred?evaluationDate={Date}",
            new
            {
                expectedVersion = target.Version,
                expectedCurrentPreferredId = current?.Id,
                expectedCurrentPreferredVersion = current?.Version,
                reason = "test"
            });

    private static async Task<OptionDto> Created(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await Read(response);
    }

    private static async Task<OptionDto> Read(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        return new(root.GetProperty("id").GetGuid(), root.GetProperty("sourcingNumber").GetString()!,
            root.GetProperty("item").GetProperty("id").GetGuid(), root.GetProperty("supplier").GetProperty("id").GetGuid(),
            root.GetProperty("itemBaseUnit").GetProperty("id").GetGuid(),
            root.GetProperty("minimumOrderQuantity").ValueKind == JsonValueKind.Null ? null : root.GetProperty("minimumOrderQuantity").GetDecimal(),
            root.GetProperty("shippingMode").ValueKind == JsonValueKind.Null ? null : root.GetProperty("shippingMode").GetProperty("id").GetGuid(),
            root.GetProperty("isPreferred").GetBoolean(), root.GetProperty("isActive").GetBoolean(),
            root.GetProperty("version").GetInt64(), root.GetProperty("evaluationDate").GetString()!,
            root.GetProperty("readiness").GetProperty("state").GetString()!,
            root.GetProperty("readiness").GetProperty("reasonCodes").EnumerateArray().Select(x => x.GetString()!).ToArray(),
            root.GetProperty("currentPrice").ValueKind == JsonValueKind.Null ? null : root.GetProperty("currentPrice").GetProperty("unitPrice").GetDecimal(),
            root.GetProperty("totalConfiguredLeadTimeDays").ValueKind == JsonValueKind.Null ? null : root.GetProperty("totalConfiguredLeadTimeDays").GetInt32(),
            root.GetProperty("prices").GetArrayLength());
    }

    private static async Task<string?> ProblemCode(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private async Task<Guid> AddItem(string? supplyMethod, string code, string itemType = ItemTypes.Part)
    {
        await using var db = await Db();
        var businessNumber = itemType == ItemTypes.Product
            ? $"PRD-{Guid.NewGuid():N}"[..20]
            : $"PRT-{Guid.NewGuid():N}"[..20];
        var item = new Item(itemType, businessNumber, code, code, code, _unit, null);
        db.Items.Add(item);
        if (supplyMethod is not null)
        {
            var category = await AddReference(db, "PART_CATEGORY", $"CAT-{Guid.NewGuid():N}");
            db.MaterialItemProfiles.Add(new(item.Id, category.Id, supplyMethod, null, 0, 0, 0, 0, 0));
        }

        await db.SaveChangesAsync();
        return item.Id;
    }

    private async Task<Guid> AddUnitToDatabase(string code)
    {
        await using var db = await Db();
        return (await AddUnit(db, code)).Id;
    }

    private static async Task<Unit> AddUnit(HadronDbContext db, string code)
    {
        var unit = new Unit(code, code, code, code, null, 0);
        db.Units.Add(unit);
        await db.SaveChangesAsync();
        return unit;
    }

    private static async Task<Currency> AddCurrency(HadronDbContext db, string code)
    {
        var currency = new Currency(code, code, code, code, "$", 2);
        db.Currencies.Add(currency);
        await db.SaveChangesAsync();
        return currency;
    }

    private static async Task<ReferenceValue> AddReference(HadronDbContext db, string type, string code)
    {
        var typeId = await db.ReferenceTypes.Where(x => x.Code == type).Select(x => x.Id).SingleAsync();
        var value = new ReferenceValue(typeId, code, code, code, code, 0);
        db.ReferenceValues.Add(value);
        await db.SaveChangesAsync();
        return value;
    }

    private async Task<HadronDbContext> Db()
    {
        var scope = _factory.Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HadronDbContext>>();
        return await factory.CreateDbContextAsync();
    }

    private static async Task Cleanup(HadronDbContext db)
    {
        await db.SourcingPrices.ExecuteDeleteAsync();
        await db.SourcingOptions.ExecuteDeleteAsync();
        await db.SupplierContacts.ExecuteDeleteAsync();
        await db.SupplierPaymentProfiles.ExecuteDeleteAsync();
        await db.Suppliers.ExecuteDeleteAsync();
        await db.InternalPeople.ExecuteDeleteAsync();
        await db.MaterialItemProfiles.ExecuteDeleteAsync();
        await db.Products.ExecuteDeleteAsync();
        await db.Items.ExecuteDeleteAsync();
        await db.ReferenceValues.ExecuteDeleteAsync();
        await db.Currencies.ExecuteDeleteAsync();
        await db.Units.ExecuteDeleteAsync();
    }

    private sealed record OptionDto(Guid Id, string Number, Guid ItemId, Guid SupplierId, Guid BaseUnitId,
        decimal? Moq, Guid? ShippingModeId, bool Preferred, bool Active, long Version, string EvaluationDate,
        string Readiness, IReadOnlyList<string> Reasons, decimal? CurrentPrice, int? TotalLeadTime, int PriceCount);
    private sealed class ColumnInfo
    {
        public string ColumnName { get; init; } = string.Empty;
        public string DataType { get; init; } = string.Empty;
        public int? NumericPrecision { get; init; }
        public int? NumericScale { get; init; }
    }
}
