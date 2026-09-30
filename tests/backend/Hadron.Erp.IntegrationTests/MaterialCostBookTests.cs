using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Bom.Domain;
using Hadron.Erp.Api.Modules.CashFlow.Application;
using Hadron.Erp.Api.Modules.CashFlow.Domain;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Inventory.Domain;
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
using Npgsql;

namespace Hadron.Erp.IntegrationTests;

[Collection("ReferenceData")]
public sealed class MaterialCostBookTests : IClassFixture<FoundationApiFactory>, IAsyncLifetime
{
    private static readonly Guid CountryTypeId =
        Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid PaymentTermTypeId =
        Guid.Parse("10000000-0000-0000-0000-000000000006");
    private static int _sequence = 930_000;
    private static int _currencySequence;
    private readonly FoundationApiFactory _factory;
    private readonly HttpClient _client;

    public MaterialCostBookTests(FoundationApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await using var db = await Db();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE cash_flow.material_cost_books RESTART IDENTITY CASCADE");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Endpoints_have_bounded_permissions_and_self_contained_editor_options()
    {
        var routes = new[]
        {
            ("/api/v1/cash-flow/material-cost-books", HttpMethods.Get,
                Permissions.CashFlowMaterialCostBookView),
            ("/api/v1/cash-flow/material-cost-books/item-options", HttpMethods.Get,
                Permissions.CashFlowMaterialCostBookView),
            ("/api/v1/cash-flow/material-cost-books/currency-options", HttpMethods.Get,
                Permissions.CashFlowMaterialCostBookView),
            ("/api/v1/cash-flow/material-cost-books/supplier-options", HttpMethods.Get,
                Permissions.CashFlowMaterialCostBookView),
            ("/api/v1/cash-flow/material-cost-books/resolve-jalali-date", HttpMethods.Get,
                Permissions.CashFlowMaterialCostBookView),
            ("/api/v1/cash-flow/material-cost-books/effective-selections", HttpMethods.Get,
                Permissions.CashFlowCostingView),
            ("/api/v1/cash-flow/material-cost-books/{bookId:guid}", HttpMethods.Get,
                Permissions.CashFlowMaterialCostBookView),
            ("/api/v1/cash-flow/material-cost-book-revisions/{revisionId:guid}", HttpMethods.Get,
                Permissions.CashFlowMaterialCostBookView),
            ("/api/v1/cash-flow/material-cost-book-revisions/{revisionId:guid}/lines", HttpMethods.Get,
                Permissions.CashFlowMaterialCostBookView),
            ("/api/v1/cash-flow/material-cost-books", HttpMethods.Post,
                Permissions.CashFlowMaterialCostBookManage),
            ("/api/v1/cash-flow/material-cost-book-revisions/{revisionId:guid}", HttpMethods.Put,
                Permissions.CashFlowMaterialCostBookManage),
            ("/api/v1/cash-flow/material-cost-book-revisions/{revisionId:guid}/lines/{itemId:guid}", HttpMethods.Put,
                Permissions.CashFlowMaterialCostBookManage),
            ("/api/v1/cash-flow/material-cost-book-revisions/{revisionId:guid}/lines/{itemId:guid}/remove", HttpMethods.Post,
                Permissions.CashFlowMaterialCostBookManage),
            ("/api/v1/cash-flow/material-cost-book-revisions/{revisionId:guid}/publish", HttpMethods.Post,
                Permissions.CashFlowMaterialCostBookPublish),
            ("/api/v1/cash-flow/material-cost-book-revisions/{revisionId:guid}/corrections", HttpMethods.Post,
                Permissions.CashFlowMaterialCostBookManage)
        };
        var endpoints = _factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(x => x.Endpoints).OfType<RouteEndpoint>().ToArray();
        foreach (var (route, method, permission) in routes)
        {
            var endpoint = Assert.Single(endpoints, candidate =>
                candidate.RoutePattern.RawText == route &&
                candidate.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(method) == true);
            Assert.Equal(permission,
                endpoint.Metadata.GetMetadata<RequiredPermissionMetadata>()?.Code);
        }

        var seed = await AddEditorMaster();
        using var viewOnly = ClientWith(Permissions.CashFlowMaterialCostBookView);
        Assert.Equal(HttpStatusCode.OK, (await viewOnly.GetAsync(
            "/api/v1/cash-flow/material-cost-books/item-options?page=1&pageSize=100")).StatusCode);
        var currencies = await viewOnly.GetFromJsonAsync<MaterialCostBookCurrencyOption[]>(
            "/api/v1/cash-flow/material-cost-books/currency-options");
        Assert.Contains(currencies!, x => x.CurrencyId == seed.ActiveCurrency.Id);
        Assert.DoesNotContain(currencies!, x => x.CurrencyId == seed.InactiveCurrency.Id);
        var suppliers = await viewOnly.GetFromJsonAsync<MaterialCostBookSupplierOptionPage>(
            $"/api/v1/cash-flow/material-cost-books/supplier-options?search={seed.Supplier.SupplierNumber}&page=1&pageSize=25");
        Assert.Equal(seed.Supplier.Id, Assert.Single(suppliers!.Items).SupplierId);
        Assert.True(Assert.Single(suppliers.Items).IsActive);
        var selectedOutsidePage = await viewOnly.GetFromJsonAsync<MaterialCostBookSupplierOptionPage>(
            $"/api/v1/cash-flow/material-cost-books/supplier-options?search=NO-MATCH-{Guid.NewGuid():N}" +
            $"&selectedSupplierId={seed.Supplier.Id}&page=1&pageSize=25");
        Assert.Empty(selectedOutsidePage!.Items);
        Assert.Equal(seed.Supplier.Id, selectedOutsidePage.Selected!.SupplierId);
        Assert.True(selectedOutsidePage.Selected.IsActive);
        await using (var supplierDb = await Db())
        {
            var supplier = await supplierDb.Suppliers.SingleAsync(x => x.Id == seed.Supplier.Id);
            supplier.ChangeState(false);
            await supplierDb.SaveChangesAsync();
        }
        var inactiveSelected = await viewOnly.GetFromJsonAsync<MaterialCostBookSupplierOptionPage>(
            $"/api/v1/cash-flow/material-cost-books/supplier-options?selectedSupplierId={seed.Supplier.Id}&page=1&pageSize=25");
        Assert.DoesNotContain(inactiveSelected!.Items, x => x.SupplierId == seed.Supplier.Id);
        Assert.Equal(seed.Supplier.Id, inactiveSelected.Selected!.SupplierId);
        Assert.False(inactiveSelected.Selected.IsActive);
        var date = await viewOnly.GetAsync(
            "/api/v1/cash-flow/material-cost-books/resolve-jalali-date?jalaliDate=14050616");
        Assert.Equal(HttpStatusCode.OK, date.StatusCode);
        var invalidDate = await viewOnly.GetAsync(
            "/api/v1/cash-flow/material-cost-books/resolve-jalali-date?jalaliDate=14051301");
        Assert.Equal(HttpStatusCode.BadRequest, invalidDate.StatusCode);

        var create = new CreateMaterialCostBookRequest("دفتر دسترسی", null,
            new DateOnly(2026, 9, 7), Guid.NewGuid(), null);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewOnly.PostAsJsonAsync("/api/v1/cash-flow/material-cost-books", create)).StatusCode);

        using var manageOnly = ClientWith(Permissions.CashFlowMaterialCostBookManage);
        var createResponse = await manageOnly.PostAsJsonAsync(
            "/api/v1/cash-flow/material-cost-books", create);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await ReadRevision(createResponse);
        var publish = new PublishMaterialCostBookRequest(created.Version, Guid.NewGuid(),
            "انتشار مستقل");
        Assert.Equal(HttpStatusCode.Forbidden, (await manageOnly.PostAsJsonAsync(
            $"/api/v1/cash-flow/material-cost-book-revisions/{created.RevisionId}/publish",
            publish)).StatusCode);

        using var publishOnly = ClientWith(Permissions.CashFlowMaterialCostBookPublish);
        var publishResponse = await publishOnly.PostAsJsonAsync(
            $"/api/v1/cash-flow/material-cost-book-revisions/{created.RevisionId}/publish",
            publish);
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
        Assert.Equal(MaterialCostBookRevisionStatuses.Published,
            (await ReadRevision(publishResponse)).Status);
    }

    [Fact]
    public async Task Lifecycle_snapshots_exact_item_conversion_is_idempotent_and_published_rows_are_immutable()
    {
        var seed = await AddEditorMaster();
        var effectiveDate = new DateOnly(2026, 10, 1);
        var createRequest = new CreateMaterialCostBookRequest("دفتر پلیمر", "Polymer book",
            effectiveDate, Guid.NewGuid(), "آزمون چرخه");
        var createdResponse = await _client.PostAsJsonAsync(
            "/api/v1/cash-flow/material-cost-books", createRequest);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await ReadRevision(createdResponse);

        var createReplay = await _client.PostAsJsonAsync(
            "/api/v1/cash-flow/material-cost-books", createRequest);
        Assert.Equal(created.RevisionId, (await ReadRevision(createReplay)).RevisionId);

        var lineRequest = new UpsertMaterialCostBookLineRequest(
            seed.ActiveCurrency.Id, 5_500_000m, 1m, seed.AlternateUnit.Id,
            seed.Supplier.Id, "QUOTE-001", "کیسه ۲۵ کیلوگرمی", created.Version,
            Guid.NewGuid());
        var lineResponse = await _client.PutAsJsonAsync(
            $"/api/v1/cash-flow/material-cost-book-revisions/{created.RevisionId}/lines/{seed.Item.Id}",
            lineRequest);
        Assert.Equal(HttpStatusCode.OK, lineResponse.StatusCode);
        var lineMutation = await lineResponse.Content
            .ReadFromJsonAsync<MaterialCostBookLineMutationResponse>();
        Assert.NotNull(lineMutation);
        Assert.Equal(220_000m, lineMutation!.Line.NormalizedUnitPrice);
        Assert.Equal(25m, lineMutation.Line.PriceUnitFactorToBase);
        Assert.Equal(seed.Item.BaseUnitId, lineMutation.Line.ItemBaseUnitId);
        Assert.Equal(seed.ActiveCurrency.DecimalPrecision,
            lineMutation.Line.CurrencyDecimalPrecision);
        Assert.Equal(seed.Supplier.Id, lineMutation.Line.SupplierId);

        var lineReplay = await _client.PutAsJsonAsync(
            $"/api/v1/cash-flow/material-cost-book-revisions/{created.RevisionId}/lines/{seed.Item.Id}",
            lineRequest);
        var replayMutation = await lineReplay.Content
            .ReadFromJsonAsync<MaterialCostBookLineMutationResponse>();
        Assert.Equal(lineMutation.RevisionVersion, replayMutation!.RevisionVersion);

        var reused = lineRequest with { Notes = "درخواست متفاوت" };
        var reusedResponse = await _client.PutAsJsonAsync(
            $"/api/v1/cash-flow/material-cost-book-revisions/{created.RevisionId}/lines/{seed.Item.Id}",
            reused);
        Assert.Equal(HttpStatusCode.Conflict, reusedResponse.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", await ProblemCode(reusedResponse));

        var editRequest = lineRequest with
        {
            OriginalPriceAmount = 6_000_000m,
            ExpectedVersion = lineMutation.RevisionVersion,
            ClientRequestId = Guid.NewGuid()
        };
        var editResponse = await _client.PutAsJsonAsync(
            $"/api/v1/cash-flow/material-cost-book-revisions/{created.RevisionId}/lines/{seed.Item.Id}",
            editRequest);
        var edited = await editResponse.Content
            .ReadFromJsonAsync<MaterialCostBookLineMutationResponse>();
        Assert.Equal(240_000m, edited!.Line.NormalizedUnitPrice);
        var lines = await _client.GetFromJsonAsync<MaterialCostBookLinePageResponse>(
            $"/api/v1/cash-flow/material-cost-book-revisions/{created.RevisionId}/lines?page=1&pageSize=100");
        Assert.Single(lines!.Items);

        var stale = editRequest with { ClientRequestId = Guid.NewGuid(), ExpectedVersion = 1 };
        var staleResponse = await _client.PutAsJsonAsync(
            $"/api/v1/cash-flow/material-cost-book-revisions/{created.RevisionId}/lines/{seed.Item.Id}",
            stale);
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        Assert.Equal("STALE_VERSION", await ProblemCode(staleResponse));

        var publishResponse = await _client.PostAsJsonAsync(
            $"/api/v1/cash-flow/material-cost-book-revisions/{created.RevisionId}/publish",
            new PublishMaterialCostBookRequest(edited.RevisionVersion, Guid.NewGuid(),
                "تأیید مالی"));
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
        var published = await ReadRevision(publishResponse);

        var beforeEffective = await _client.GetFromJsonAsync<MaterialCostBookSelectionOption[]>(
            "/api/v1/cash-flow/material-cost-books/effective-selections?asOfDate=2026-09-30");
        Assert.DoesNotContain(beforeEffective!, x => x.RevisionId == published.RevisionId);
        var effective = await _client.GetFromJsonAsync<MaterialCostBookSelectionOption[]>(
            "/api/v1/cash-flow/material-cost-books/effective-selections?asOfDate=2026-10-01");
        Assert.Contains(effective!, x => x.RevisionId == published.RevisionId);

        var immutableResponse = await _client.PutAsJsonAsync(
            $"/api/v1/cash-flow/material-cost-book-revisions/{published.RevisionId}",
            new UpdateMaterialCostBookDraftRequest("نباید ویرایش شود", null, effectiveDate,
                published.Version, Guid.NewGuid(), "ممنوع"));
        Assert.Equal(HttpStatusCode.Conflict, immutableResponse.StatusCode);
        Assert.Equal("MATERIAL_COST_BOOK_IMMUTABLE", await ProblemCode(immutableResponse));

        await using (var immutableDb = await Db())
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                immutableDb.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE cash_flow.material_cost_book_lines SET \"Notes\" = {"x"} WHERE \"MaterialCostBookRevisionId\" = {published.RevisionId}"));
            Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        }

        var correctionResponse = await _client.PostAsJsonAsync(
            $"/api/v1/cash-flow/material-cost-book-revisions/{published.RevisionId}/corrections",
            new CreateMaterialCostBookCorrectionRequest(published.Version, Guid.NewGuid(),
                "اصلاح مستند قیمت"));
        Assert.Equal(HttpStatusCode.Created, correctionResponse.StatusCode);
        var correction = await ReadRevision(correctionResponse);
        Assert.Equal(published.RevisionId, correction.SupersedesRevisionId);
        Assert.Equal(published.EffectiveDate, correction.EffectiveDate);
        Assert.Equal(2, correction.Revision);
        var copiedLines = await _client.GetFromJsonAsync<MaterialCostBookLinePageResponse>(
            $"/api/v1/cash-flow/material-cost-book-revisions/{correction.RevisionId}/lines?page=1&pageSize=100");
        Assert.Equal(240_000m, Assert.Single(copiedLines!.Items).NormalizedUnitPrice);

        var dateChange = await _client.PutAsJsonAsync(
            $"/api/v1/cash-flow/material-cost-book-revisions/{correction.RevisionId}",
            new UpdateMaterialCostBookDraftRequest(correction.NameFa, correction.NameEn,
                correction.EffectiveDate.AddDays(1), correction.Version, Guid.NewGuid(),
                "تغییر تاریخ غیرمجاز"));
        Assert.Equal(HttpStatusCode.BadRequest, dateChange.StatusCode);

        await using var auditDb = await Db();
        Assert.Contains(await auditDb.OperationalAuditEvents.AsNoTracking()
            .Where(x => x.EntityId == published.RevisionId).Select(x => x.Action).ToArrayAsync(),
            x => x == "PUBLISH");
    }

    [Fact]
    public async Task Idempotency_fingerprint_distinguishes_delimiters_inside_user_text()
    {
        var created = await CreateBook("دفتر برخورد کلید", new DateOnly(2026, 9, 1));
        var clientRequestId = Guid.NewGuid();
        var first = new UpdateMaterialCostBookDraftRequest("الف|ب", "ج",
            created.EffectiveDate, created.Version, clientRequestId, "آزمون");
        var firstResponse = await _client.PutAsJsonAsync(
            $"/api/v1/cash-flow/material-cost-book-revisions/{created.RevisionId}", first);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var collidingUnderDelimitedEncoding = first with { NameFa = "الف", NameEn = "ب|ج" };
        var secondResponse = await _client.PutAsJsonAsync(
            $"/api/v1/cash-flow/material-cost-book-revisions/{created.RevisionId}",
            collidingUnderDelimitedEncoding);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", await ProblemCode(secondResponse));
    }

    [Fact]
    public async Task Invalid_or_changed_price_unit_and_inactive_currency_block_publish()
    {
        var seed = await AddEditorMaster();
        var created = await CreateBook("دفتر اعتبارسنجی", new DateOnly(2026, 9, 1));

        var zero = await _client.PutAsJsonAsync(
            $"/api/v1/cash-flow/material-cost-book-revisions/{created.RevisionId}/lines/{seed.Item.Id}",
            new UpsertMaterialCostBookLineRequest(seed.ActiveCurrency.Id, 0m, 1m,
                seed.Item.BaseUnitId, null, null, null, created.Version, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);

        await using (var db = await Db())
        {
            var conversion = await db.InventoryItemUnitConversions
                .SingleAsync(x => x.ItemId == seed.Item.Id &&
                    x.TransactionUnitId == seed.AlternateUnit.Id);
            conversion.ChangeState(false);
            await db.SaveChangesAsync();
        }
        var inactiveUnit = await _client.PutAsJsonAsync(
            $"/api/v1/cash-flow/material-cost-book-revisions/{created.RevisionId}/lines/{seed.Item.Id}",
            new UpsertMaterialCostBookLineRequest(seed.ActiveCurrency.Id, 100m, 1m,
                seed.AlternateUnit.Id, null, null, null, created.Version, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.BadRequest, inactiveUnit.StatusCode);

        await using (var db = await Db())
        {
            var supplier = await db.Suppliers.SingleAsync(x => x.Id == seed.Supplier.Id);
            supplier.ChangeState(false);
            await db.SaveChangesAsync();
        }
        var inactiveSupplier = await _client.PutAsJsonAsync(
            $"/api/v1/cash-flow/material-cost-book-revisions/{created.RevisionId}/lines/{seed.Item.Id}",
            new UpsertMaterialCostBookLineRequest(seed.ActiveCurrency.Id, 100m, 1m,
                seed.Item.BaseUnitId, seed.Supplier.Id, null, null, created.Version,
                Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.BadRequest, inactiveSupplier.StatusCode);
        await using (var db = await Db())
        {
            var supplier = await db.Suppliers.SingleAsync(x => x.Id == seed.Supplier.Id);
            supplier.ChangeState(true);
            await db.SaveChangesAsync();
        }

        var valid = await _client.PutAsJsonAsync(
            $"/api/v1/cash-flow/material-cost-book-revisions/{created.RevisionId}/lines/{seed.Item.Id}",
            new UpsertMaterialCostBookLineRequest(seed.ActiveCurrency.Id, 100m, 1m,
                seed.Item.BaseUnitId, null, null, null, created.Version, Guid.NewGuid()));
        var mutation = await valid.Content.ReadFromJsonAsync<MaterialCostBookLineMutationResponse>();
        await using (var db = await Db())
        {
            var currency = await db.Currencies.SingleAsync(x => x.Id == seed.ActiveCurrency.Id);
            currency.SetActive(false);
            currency.AdvanceVersion();
            currency.Touch();
            await db.SaveChangesAsync();
        }
        var publish = await _client.PostAsJsonAsync(
            $"/api/v1/cash-flow/material-cost-book-revisions/{created.RevisionId}/publish",
            new PublishMaterialCostBookRequest(mutation!.RevisionVersion, Guid.NewGuid(),
                "نباید منتشر شود"));
        Assert.Equal(HttpStatusCode.Conflict, publish.StatusCode);
        Assert.Equal("CURRENCY_INACTIVE", await ProblemCode(publish));
    }

    [Fact]
    public async Task Effective_selections_are_paged_and_material_cost_book_search_is_literal_and_bounded()
    {
        var effectiveDate = new DateOnly(2026, 9, 1);
        var percentOne = await PublishBook(
            await CreateBook("دفتر % یک", effectiveDate), "انتشار آزمون جست‌وجو");
        var percentTwo = await PublishBook(
            await CreateBook("دفتر % دو", effectiveDate), "انتشار آزمون جست‌وجو");
        var plain = await PublishBook(
            await CreateBook("دفتر معمولی", effectiveDate), "انتشار آزمون جست‌وجو");

        var books = await _client.GetFromJsonAsync<MaterialCostBookPageResponse>(
            "/api/v1/cash-flow/material-cost-books?search=%25&page=1&pageSize=100");
        Assert.Equal(2, books!.TotalCount);
        Assert.DoesNotContain(books.Items, x => x.RevisionId == plain.RevisionId);

        var firstPage = await _client.GetFromJsonAsync<MaterialCostBookSelectionOption[]>(
            "/api/v1/cash-flow/material-cost-books/effective-selections" +
            "?asOfDate=2026-09-30&search=%25&page=1&pageSize=1");
        var secondPage = await _client.GetFromJsonAsync<MaterialCostBookSelectionOption[]>(
            "/api/v1/cash-flow/material-cost-books/effective-selections" +
            "?asOfDate=2026-09-30&search=%25&page=2&pageSize=1");
        var first = Assert.Single(firstPage!);
        var second = Assert.Single(secondPage!);
        Assert.NotEqual(first.RevisionId, second.RevisionId);
        Assert.Contains(first.RevisionId, new[] { percentOne.RevisionId, percentTwo.RevisionId });
        Assert.Contains(second.RevisionId, new[] { percentOne.RevisionId, percentTwo.RevisionId });

        var invalidPage = await _client.GetAsync(
            "/api/v1/cash-flow/material-cost-books/effective-selections" +
            "?asOfDate=2026-09-30&page=1&pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);

        var tooLongSearch = Uri.EscapeDataString(new string('x', 201));
        var invalidSearch = await _client.GetAsync(
            $"/api/v1/cash-flow/material-cost-books?search={tooLongSearch}&page=1&pageSize=25");
        Assert.Equal(HttpStatusCode.BadRequest, invalidSearch.StatusCode);
    }

    [Fact]
    public async Task Inactive_item_base_unit_is_not_selectable_and_blocks_upsert_and_publish()
    {
        var seed = await AddEditorMaster();
        var created = await CreateBook("دفتر واحد پایه", new DateOnly(2026, 9, 1));
        var lineResponse = await _client.PutAsJsonAsync(
            $"/api/v1/cash-flow/material-cost-book-revisions/{created.RevisionId}/lines/{seed.Item.Id}",
            new UpsertMaterialCostBookLineRequest(seed.ActiveCurrency.Id, 100m, 1m,
                seed.Item.BaseUnitId, null, null, null, created.Version, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.OK, lineResponse.StatusCode);
        var mutation = await lineResponse.Content
            .ReadFromJsonAsync<MaterialCostBookLineMutationResponse>();

        await using (var db = await Db())
        {
            var baseUnit = await db.Units.SingleAsync(x => x.Id == seed.Item.BaseUnitId);
            baseUnit.SetActive(false);
            baseUnit.AdvanceVersion();
            baseUnit.Touch();
            await db.SaveChangesAsync();
        }

        var itemOptions = await _client.GetFromJsonAsync<MaterialCostBookItemOptionPage>(
            "/api/v1/cash-flow/material-cost-books/item-options" +
            $"?search={Uri.EscapeDataString(seed.Item.BusinessNumber)}&page=1&pageSize=25");
        Assert.DoesNotContain(itemOptions!.Items, x => x.ItemId == seed.Item.Id);

        var secondBook = await CreateBook("دفتر واحد غیرفعال", new DateOnly(2026, 9, 2));
        var rejectedLine = await _client.PutAsJsonAsync(
            $"/api/v1/cash-flow/material-cost-book-revisions/{secondBook.RevisionId}/lines/{seed.Item.Id}",
            new UpsertMaterialCostBookLineRequest(seed.ActiveCurrency.Id, 100m, 1m,
                seed.AlternateUnit.Id, null, null, null, secondBook.Version, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.BadRequest, rejectedLine.StatusCode);

        var publish = await _client.PostAsJsonAsync(
            $"/api/v1/cash-flow/material-cost-book-revisions/{created.RevisionId}/publish",
            new PublishMaterialCostBookRequest(mutation!.RevisionVersion, Guid.NewGuid(),
                "واحد پایه غیرفعال است"));
        Assert.Equal(HttpStatusCode.Conflict, publish.StatusCode);
        Assert.Equal("ITEM_BASE_UNIT_INACTIVE", await ProblemCode(publish));
    }

    [Fact]
    public async Task Per_thousand_price_basis_is_persisted_and_normalized_after_reload()
    {
        var seed = await AddEditorMaster();
        var created = await CreateBook("دفتر مبنای هزار", new DateOnly(2026, 9, 1));
        var response = await _client.PutAsJsonAsync(
            $"/api/v1/cash-flow/material-cost-book-revisions/{created.RevisionId}/lines/{seed.Item.Id}",
            new UpsertMaterialCostBookLineRequest(seed.ActiveCurrency.Id, 100m, 1_000m,
                seed.Item.BaseUnitId, null, "QUOTE-PER-1000", null,
                created.Version, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var mutation = await response.Content
            .ReadFromJsonAsync<MaterialCostBookLineMutationResponse>();
        Assert.NotNull(mutation);
        Assert.Equal(100m, mutation!.Line.OriginalPriceAmount);
        Assert.Equal(1_000m, mutation.Line.PriceBasisQuantity);
        Assert.Equal(1m, mutation.Line.PriceUnitFactorToBase);
        Assert.Equal(0.1m, mutation.Line.NormalizedUnitPrice);

        var reloaded = await _client.GetFromJsonAsync<MaterialCostBookLinePageResponse>(
            $"/api/v1/cash-flow/material-cost-book-revisions/{created.RevisionId}/lines" +
            "?page=1&pageSize=25");
        var persisted = Assert.Single(reloaded!.Items);
        Assert.Equal(100m, persisted.OriginalPriceAmount);
        Assert.Equal(1_000m, persisted.PriceBasisQuantity);
        Assert.Equal(0.1m, persisted.NormalizedUnitPrice);

        await using var db = await Db();
        var stored = await db.MaterialCostBookLines.AsNoTracking()
            .SingleAsync(x => x.MaterialCostBookRevisionId == created.RevisionId &&
                x.ItemId == seed.Item.Id);
        Assert.Equal(100m, stored.OriginalPriceAmount);
        Assert.Equal(1_000m, stored.PriceBasisQuantity);
        Assert.Equal(0.1m, stored.NormalizedUnitPrice);
    }

    [Fact]
    public async Task Published_revision_database_trigger_rejects_all_revision_and_line_mutations()
    {
        var seed = await AddEditorMaster();
        var publishedDraft = await CreateBook("دفتر قفل‌شده", new DateOnly(2026, 9, 1));
        var publishedLineResponse = await _client.PutAsJsonAsync(
            $"/api/v1/cash-flow/material-cost-book-revisions/{publishedDraft.RevisionId}/lines/{seed.Item.Id}",
            new UpsertMaterialCostBookLineRequest(seed.ActiveCurrency.Id, 100m, 1m,
                seed.Item.BaseUnitId, null, null, null,
                publishedDraft.Version, Guid.NewGuid()));
        var publishedLine = await publishedLineResponse.Content
            .ReadFromJsonAsync<MaterialCostBookLineMutationResponse>();
        var published = await PublishBook(publishedDraft with
        {
            Version = publishedLine!.RevisionVersion
        }, "تست کامل قفل دیتابیس");

        var editable = await CreateBook("دفتر مبدأ جابه‌جایی", new DateOnly(2026, 9, 2));
        var editableLineResponse = await _client.PutAsJsonAsync(
            $"/api/v1/cash-flow/material-cost-book-revisions/{editable.RevisionId}/lines/{seed.Item.Id}",
            new UpsertMaterialCostBookLineRequest(seed.ActiveCurrency.Id, 200m, 1m,
                seed.Item.BaseUnitId, null, null, null,
                editable.Version, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.OK, editableLineResponse.StatusCode);

        Guid publishedLineId;
        Guid editableLineId;
        await using (var db = await Db())
        {
            publishedLineId = await db.MaterialCostBookLines.AsNoTracking()
                .Where(x => x.MaterialCostBookRevisionId == published.RevisionId)
                .Select(x => x.Id).SingleAsync();
            editableLineId = await db.MaterialCostBookLines.AsNoTracking()
                .Where(x => x.MaterialCostBookRevisionId == editable.RevisionId)
                .Select(x => x.Id).SingleAsync();
        }

        await AssertDatabaseCheckViolation(
            $"UPDATE cash_flow.material_cost_book_revisions SET \"NameFa\" = {'x'} WHERE \"Id\" = {published.RevisionId}");
        await AssertDatabaseCheckViolation(
            $"DELETE FROM cash_flow.material_cost_book_revisions WHERE \"Id\" = {published.RevisionId}");
        await AssertDatabaseCheckViolation(
            $"INSERT INTO cash_flow.material_cost_book_lines (\"Id\", \"MaterialCostBookRevisionId\") VALUES ({Guid.NewGuid()}, {published.RevisionId})");
        await AssertDatabaseCheckViolation(
            $"UPDATE cash_flow.material_cost_book_lines SET \"Notes\" = {'x'} WHERE \"Id\" = {publishedLineId}");
        await AssertDatabaseCheckViolation(
            $"DELETE FROM cash_flow.material_cost_book_lines WHERE \"Id\" = {publishedLineId}");
        await AssertDatabaseCheckViolation(
            $"UPDATE cash_flow.material_cost_book_lines SET \"MaterialCostBookRevisionId\" = {published.RevisionId} WHERE \"Id\" = {editableLineId}");
    }

    [Fact]
    public async Task Partial_book_costing_is_strict_has_provenance_and_never_falls_back_to_purchase_history()
    {
        var seed = await AddCostingSeed();
        var book = await CreateBook("دفتر ناقص کنترل‌شده", new DateOnly(2026, 9, 1));
        var lineResponse = await _client.PutAsJsonAsync(
            $"/api/v1/cash-flow/material-cost-book-revisions/{book.RevisionId}/lines/{seed.PricedLeaf.Id}",
            new UpsertMaterialCostBookLineRequest(seed.Currency.Id, 5_500_000m, 1m,
                seed.BagUnit.Id, seed.Supplier.Id, "QUOTE-STRICT", null,
                book.Version, Guid.NewGuid()));
        var mutation = await lineResponse.Content
            .ReadFromJsonAsync<MaterialCostBookLineMutationResponse>();
        var publishedResponse = await _client.PostAsJsonAsync(
            $"/api/v1/cash-flow/material-cost-book-revisions/{book.RevisionId}/publish",
            new PublishMaterialCostBookRequest(mutation!.RevisionVersion, Guid.NewGuid(),
                "انتشار ناقص آگاهانه"));
        var published = await ReadRevision(publishedResponse);

        var detailUrl = $"/api/v1/cash-flow/costed-boms/{seed.Root.Id}" +
            $"?asOfDate=2026-09-15&costBasisMode=MATERIAL_COST_BOOK" +
            $"&priceBookRevisionId={published.RevisionId}";
        var detailResponse = await _client.GetAsync(detailUrl);
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<CostedBomResponse>();
        Assert.NotNull(detail);
        Assert.Equal(MaterialCostBookCostBasisModes.MaterialCostBook,
            detail!.CostBasisSelection.Mode);
        Assert.Equal("CURRENT_APPROVED_BOM_STRUCTURE_NOT_MRP_ROUTE_AWARE",
            detail.CostBasisSelection.TraversalSemantics);
        Assert.Equal(published.RevisionId,
            detail.CostBasisSelection.PriceBookRevisionId);
        Assert.Null(detail.MaterialCostUsd);
        var missing = Assert.Single(detail.MissingCosts);
        Assert.Equal(seed.MissingLeaf.Id, missing.ItemId);
        Assert.Equal("NO_MATERIAL_COST_BOOK_PRICE", missing.Reason);
        var missingLine = Assert.Single(detail.Lines, x => x.ItemId == seed.MissingLeaf.Id);
        Assert.Equal(CostBasisKinds.Missing, missingLine.CostBasis!.Kind);
        Assert.Null(missingLine.ExtendedCost);

        var priced = Assert.Single(detail.Lines, x => x.ItemId == seed.PricedLeaf.Id);
        Assert.Equal(CostBasisKinds.MaterialCostBook, priced.CostBasis!.Kind);
        Assert.Equal(5_500_000m, priced.CostBasis.OriginalPriceAmount);
        Assert.Equal(1m, priced.CostBasis.PriceBasisQuantity);
        Assert.Equal(25m, priced.CostBasis.PriceUnitFactorToBase);
        Assert.Equal(seed.BagUnit.Id, priced.CostBasis.PriceUnitId);
        Assert.Equal(seed.Supplier.Id, priced.CostBasis.SupplierId);
        Assert.Equal(published.RevisionId, priced.CostBasis.PriceBookRevisionId);
        Assert.Equal(3_080m, priced.ExtendedCost);
        var partialOriginalTotal = Assert.Single(detail.CurrencyTotals);
        Assert.Equal(3_080m, partialOriginalTotal.MaterialCost);

        var listResponse = await _client.GetAsync(
            $"/api/v1/cash-flow/costed-boms?asOfDate=2026-09-15" +
            $"&search={seed.Root.BusinessNumber}&page=1&pageSize=25" +
            $"&costBasisMode=MATERIAL_COST_BOOK&priceBookRevisionId={published.RevisionId}");
        var page = await listResponse.Content.ReadFromJsonAsync<CostedBomPage>();
        var summary = Assert.Single(page!.Items);
        Assert.Equal(1, summary.MissingCostCount);
        Assert.Null(summary.MaterialCostUsd);
        Assert.Equal(3_080m, Assert.Single(summary.CurrencyTotals).MaterialCost);

        var defaultWithBookId = await _client.GetAsync(
            $"/api/v1/cash-flow/costed-boms/{seed.Root.Id}?asOfDate=2026-09-15" +
            $"&priceBookRevisionId={published.RevisionId}");
        Assert.Equal(HttpStatusCode.BadRequest, defaultWithBookId.StatusCode);
    }

    private async Task<EditorSeed> AddEditorMaster()
    {
        await using var db = await Db();
        var number = Next();
        var itemNumber = Next();
        var baseUnit = new Unit($"MCB-KG-{number}", $"MCB-KG-{number}",
            "کیلوگرم", "Kilogram", "kg", 1);
        var alternate = new Unit($"MCB-BAG-{number}", $"MCB-BAG-{number}",
            "کیسه", "Bag", "bag", 2);
        var activeCurrencyCode = NextCurrencyCode();
        var inactiveCurrencyCode = NextCurrencyCode();
        var activeCurrency = new Currency(activeCurrencyCode, activeCurrencyCode,
            "ارز فعال", "Active currency", null, 2);
        var inactiveCurrency = new Currency(inactiveCurrencyCode, inactiveCurrencyCode,
            "ارز غیرفعال", "Inactive currency", null, 3);
        inactiveCurrency.SetActive(false);
        var country = new ReferenceValue(CountryTypeId, $"MCBC-{number}", $"MCBC-{number}",
            "کشور دفتر بها", "Cost-book country", 0);
        var supplier = new Supplier($"MCB-SUP-{number}", "تأمین‌کننده دفتر بها",
            $"MCB-SUPPLIER-{number}", country.Id, null, null, null, null);
        supplier.ChangeState(true);
        db.AddRange(baseUnit, alternate, activeCurrency, inactiveCurrency, country, supplier);
        await db.SaveChangesAsync();
        var item = new Item(ItemTypes.Component, $"CMP-{itemNumber}",
            "پلیمر دفتر بها", "Cost-book polymer", baseUnit.Id, null);
        db.Items.Add(item);
        await db.SaveChangesAsync();
        db.InventoryItemUnitConversions.Add(new InventoryItemUnitConversion(
            item.Id, alternate.Id, baseUnit.Id, 25m));
        await db.SaveChangesAsync();
        return new(item, alternate, activeCurrency, inactiveCurrency, supplier);
    }

    private async Task<CostingSeed> AddCostingSeed()
    {
        await using var db = await Db();
        var number = Next();
        var rootNumber = Next();
        var pricedNumber = Next();
        var missingNumber = Next();
        var piece = new Unit($"MCBP-{number}", $"MCBP-{number}",
            "عدد", "Piece", "pcs", 0);
        var kilogram = new Unit($"MCBK-{number}", $"MCBK-{number}",
            "کیلوگرم", "Kilogram", "kg", 1);
        var bag = new Unit($"MCBB-{number}", $"MCBB-{number}",
            "کیسه", "Bag", "bag", 2);
        var currency = await EnsureUsd(db);
        var country = new ReferenceValue(CountryTypeId, $"MCBSC-{number}",
            $"MCBSC-{number}", "کشور تست بها", "Cost country", 0);
        var payment = new ReferenceValue(PaymentTermTypeId, $"MCBSP-{number}",
            $"MCBSP-{number}", "پرداخت تست بها", "Cost payment", 0);
        var supplier = new Supplier($"MCB-SUP-{number}", "تأمین‌کننده قیمت جایگزین",
            $"MCB-STRICT-SUPPLIER-{number}", country.Id, null, null, null, null);
        supplier.ChangeState(true);
        supplier.PaymentProfile.Set(payment.Id, 100m, 0m, 0m, null, null);
        db.AddRange(piece, kilogram, bag, country, payment, supplier);
        await db.SaveChangesAsync();

        var root = new Item(ItemTypes.Product, $"PRD-{rootNumber}", "محصول دفتر بها",
            "Cost-book product", piece.Id, null);
        var pricedLeaf = new Item(ItemTypes.Component, $"CMP-{pricedNumber}",
            "پلیمر قیمت‌دار", "Priced polymer", kilogram.Id, null);
        var missingLeaf = new Item(ItemTypes.Component, $"CMP-{missingNumber}",
            "قطعه دارای سابقه خرید", "Purchase-history-only part", piece.Id, null);
        db.AddRange(root, pricedLeaf, missingLeaf);
        await db.SaveChangesAsync();
        db.InventoryItemUnitConversions.Add(new InventoryItemUnitConversion(
            pricedLeaf.Id, bag.Id, kilogram.Id, 25m));

        var bom = new BomRevision($"BOM-MCB-{number}", root.Id, piece.Id, 1m, null);
        db.AddRange(bom,
            new BomLine(bom.Id, pricedLeaf.Id, kilogram.Id, 0.014m, 0m, 1),
            new BomLine(bom.Id, missingLeaf.Id, piece.Id, 2m, 0m, 2));
        await db.SaveChangesAsync();
        bom.Approve("material-cost-book-test");
        await db.SaveChangesAsync();

        var sourcing = new SourcingOption($"MCB-SRC-{number}", missingLeaf.Id,
            supplier.Id, piece.Id, SourcingRouteScopes.Domestic, null, null, null,
            1, null, null, null);
        db.SourcingOptions.Add(sourcing);
        await db.SaveChangesAsync();
        var order = new PurchaseOrder($"PO-{number}", supplier.Id, currency.Id,
            payment.Id, 100m, 0m, 0m, null, null, null, null, null);
        var orderLine = new PurchaseOrderLine(order.Id, 1, missingLeaf.Id, sourcing.Id,
            null, piece.Id, 10m, 99m, null, 1, null, 1, null, null);
        var schedule = new DeliverySchedule(orderLine.Id, 1, 10m, null);
        db.AddRange(order, orderLine, schedule);
        await db.SaveChangesAsync();
        var now = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        order.Approve(now);
        await db.SaveChangesAsync();
        order.MarkOrdered(new DateOnly(2026, 9, 1), now.AddMinutes(1));
        await db.SaveChangesAsync();
        return new(root, pricedLeaf, missingLeaf, bag, currency, supplier);
    }

    private async Task<MaterialCostBookRevisionResponse> CreateBook(string name,
        DateOnly effectiveDate)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/cash-flow/material-cost-books",
            new CreateMaterialCostBookRequest(name, null, effectiveDate, Guid.NewGuid(), null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadRevision(response);
    }

    private async Task<MaterialCostBookRevisionResponse> PublishBook(
        MaterialCostBookRevisionResponse revision, string reason)
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/cash-flow/material-cost-book-revisions/{revision.RevisionId}/publish",
            new PublishMaterialCostBookRequest(revision.Version, Guid.NewGuid(), reason));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadRevision(response);
    }

    private static async Task<Currency> EnsureUsd(HadronDbContext db)
    {
        var existing = await db.Currencies.SingleOrDefaultAsync(x => x.Code == "USD");
        if (existing is not null) return existing;
        var currency = new Currency("USD", "USD", "دلار آمریکا", "US Dollar", "$", 2);
        db.Currencies.Add(currency);
        await db.SaveChangesAsync();
        return currency;
    }

    private HttpClient ClientWith(string permission)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Hadron-Test-Permissions", permission);
        return client;
    }

    private static async Task<MaterialCostBookRevisionResponse> ReadRevision(
        HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<MaterialCostBookRevisionResponse>())!;

    private static async Task<string?> ProblemCode(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("code", out var code)
            ? code.GetString()
            : null;
    }

    private async Task AssertDatabaseCheckViolation(FormattableString command)
    {
        await using var db = await Db();
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(command));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    private async Task<HadronDbContext> Db() => await _factory.Services
        .GetRequiredService<IDbContextFactory<HadronDbContext>>().CreateDbContextAsync();

    private static int Next() => Interlocked.Increment(ref _sequence);

    private static string NextCurrencyCode()
    {
        var value = Interlocked.Increment(ref _currencySequence) - 1;
        return $"M{(char)('A' + value / 26 % 26)}{(char)('A' + value % 26)}";
    }

    private sealed record EditorSeed(Item Item, Unit AlternateUnit, Currency ActiveCurrency,
        Currency InactiveCurrency, Supplier Supplier);

    private sealed record CostingSeed(Item Root, Item PricedLeaf, Item MissingLeaf,
        Unit BagUnit, Currency Currency, Supplier Supplier);
}
