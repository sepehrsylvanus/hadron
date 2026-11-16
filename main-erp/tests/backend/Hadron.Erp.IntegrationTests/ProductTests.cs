using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hadron.Erp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Hadron.Erp.Api.Modules.Products.Application;

namespace Hadron.Erp.IntegrationTests;

[Collection("ReferenceData")]
public sealed class ProductTests : IClassFixture<FoundationApiFactory>, IAsyncLifetime
{
    private readonly FoundationApiFactory _factory; private readonly HttpClient _client;
    private Guid _category; private Guid _unit; private Guid _business;
    public ProductTests(FoundationApiFactory factory) { _factory = factory; _client = factory.CreateClient(); }
    public async Task InitializeAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HadronDbContext>>();
        await using var db = await factory.CreateDbContextAsync(); await db.Database.MigrateAsync();
        TestDatabaseSafety.EnsureSafeDatabaseName(db.Database.GetDbConnection().Database);
        await db.Products.ExecuteDeleteAsync(); await db.Items.ExecuteDeleteAsync(); await db.ReferenceValues.ExecuteDeleteAsync(); await db.Units.ExecuteDeleteAsync();
        _factory.AuditSink.Clear();
        _category = await CreateReference("PRODUCT_CATEGORY", "FG", "محصول نهایی");
        _business = await CreateReference("BUSINESS_UNIT", "MAIN", "واحد اصلی");
        _unit = await CreateUnit(); _factory.AuditSink.Clear();
    }
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_generates_uuid_and_official_code_and_ignores_client_code()
    {
        using var response = await Create("  prd-a  "); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var product = json.RootElement.GetProperty("product");
        Assert.NotEqual(Guid.Empty, product.GetProperty("id").GetGuid()); Assert.Matches("^PRD-[0-9]{4,}$", product.GetProperty("code").GetString()!);
        Assert.NotEqual("PRD-A", product.GetProperty("code").GetString()); Assert.Equal(1, product.GetProperty("version").GetInt64());
        Assert.False(product.TryGetProperty("productNumber", out _));
        Assert.Contains(_factory.AuditSink.Snapshot(), x => x.EntityType == "Product" && x.Action == "CREATE");
    }

    [Fact]
    public async Task Generated_codes_are_unique_and_duplicate_name_saves_with_warning()
    {
        var first = await ReadCreatedDocument(await Create("ABC"));
        var secondResponse = await Create(" abc "); Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        var second = await ReadCreatedDocument(secondResponse); Assert.NotEqual(first.Code, second.Code);
        using var json = JsonDocument.Parse(await (await Create("DEF")).Content.ReadAsStringAsync());
        Assert.Contains("DUPLICATE_NAME_FA", json.RootElement.GetProperty("warnings").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task Edit_preserves_generated_code_advances_version_and_stale_write_conflicts()
    {
        var created = await ReadCreated(await Create("OLD"));
        var payload = Request("NEW") with { ExpectedVersion = created.Version, RequestedVersion = created.Version };
        var update = await _client.PutAsJsonAsync($"/api/products/{created.Id}", payload); Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        using var json = JsonDocument.Parse(await update.Content.ReadAsStringAsync());
        var updatedProduct = json.RootElement.GetProperty("product");
        var officialCode = (await _client.GetFromJsonAsync<ProductCodeDto>($"/api/products/{created.Id}"))!.Code;
        Assert.Equal(officialCode, updatedProduct.GetProperty("code").GetString());
        var updatedVersion = updatedProduct.GetProperty("version").GetInt64();
        Assert.Equal(created.Version + 1, updatedVersion);
        var editAuditCount = _factory.AuditSink.Snapshot().Count(x => x.EntityType == "Product" && x.Action == "EDIT");

        var stale = await _client.PutAsJsonAsync($"/api/products/{created.Id}", payload);

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("STALE_VERSION", await ProblemCode(stale));
        using var persisted = JsonDocument.Parse(await (await _client.GetAsync($"/api/products/{created.Id}")).Content.ReadAsStringAsync());
        Assert.Equal(officialCode, persisted.RootElement.GetProperty("code").GetString());
        Assert.Equal(updatedVersion, persisted.RootElement.GetProperty("version").GetInt64());
        Assert.Equal(editAuditCount, _factory.AuditSink.Snapshot().Count(x => x.EntityType == "Product" && x.Action == "EDIT"));
    }

    [Fact]
    public async Task Requested_version_can_only_move_forward_without_weakening_stale_write_protection()
    {
        var created = await ReadCreated(await Create("VERSION"));
        var requested = await _client.PutAsJsonAsync($"/api/products/{created.Id}",
            Request("VERSION-RAISED") with { ExpectedVersion = created.Version, RequestedVersion = 9 });
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        var raised = await ReadMutation(requested);
        Assert.Equal(9, raised.Version);
        var editAuditCount = _factory.AuditSink.Snapshot().Count(x => x.EntityId == created.Id && x.Action == "EDIT");

        var lower = await _client.PutAsJsonAsync($"/api/products/{created.Id}",
            Request("VERSION-LOWER") with { ExpectedVersion = raised.Version, RequestedVersion = raised.Version - 1 });
        Assert.Equal(HttpStatusCode.BadRequest, lower.StatusCode);

        var stale = await _client.PutAsJsonAsync($"/api/products/{created.Id}",
            Request("VERSION-STALE") with { ExpectedVersion = created.Version, RequestedVersion = 12 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("STALE_VERSION", await ProblemCode(stale));
        var current = await _client.GetFromJsonAsync<ProductDto>($"/api/products/{created.Id}");
        Assert.Equal(raised.Version, current!.Version);
        Assert.Equal(editAuditCount, _factory.AuditSink.Snapshot().Count(x => x.EntityId == created.Id && x.Action == "EDIT"));
    }

    [Fact]
    public async Task Inactive_historical_reference_resolves_but_blocks_reactivation()
    {
        var created = await ReadCreated(await Create("LIFE"));
        var category = await _client.GetFromJsonAsync<ReferenceDto>($"/api/reference-data/values/{_category}");
        await _client.PostAsJsonAsync($"/api/reference-data/values/{_category}/deactivate", new { expectedVersion = category!.Version });
        var unrelated = Request("LIFE-EDIT") with { ExpectedVersion = created.Version };
        var edit = await _client.PutAsJsonAsync($"/api/products/{created.Id}", unrelated); Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var edited = await ReadMutation(edit);
        var deactivate = await _client.PostAsJsonAsync($"/api/products/{created.Id}/deactivate", new { expectedVersion = edited.Version }); Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        var deactivated = await deactivate.Content.ReadFromJsonAsync<ProductDto>();
        var eventsBefore = _factory.AuditSink.Snapshot().Count;
        var reactivate = await _client.PostAsJsonAsync($"/api/products/{created.Id}/reactivate", new { expectedVersion = deactivated!.Version });
        Assert.Equal(HttpStatusCode.Conflict, reactivate.StatusCode); Assert.Equal(eventsBefore, _factory.AuditSink.Snapshot().Count);
        var detail = await _client.GetFromJsonAsync<ProductDto>($"/api/products/{created.Id}"); Assert.Equal(deactivated.Version, detail!.Version); Assert.False(detail.ProductCategory.IsActive);
    }

    [Fact]
    public async Task Validation_paging_and_no_delete_are_enforced()
    {
        var invalid = Request("BAD") with { SafetyStockQty = -1, CriticalDays = 3, SafeStartDays = 2 };
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/products", invalid)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/products?page=1&pageSize=101")).StatusCode);
        var created = await ReadCreated(await Create("NODELETE"));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await _client.DeleteAsync($"/api/products/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Concurrent_creates_generate_unique_numbers()
    {
        var responses = await Task.WhenAll(Enumerable.Range(1, 8).Select(index => Create($"CON-{index}")));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var products = await Task.WhenAll(responses.Select(ReadCreatedDocument));
        Assert.Equal(8, products.Select(x => x.Code).Distinct().Count());
        Assert.Equal(8, products.Select(x => x.Id).Distinct().Count());
    }

    [Fact]
    public async Task Client_cannot_override_product_number_or_internal_fields()
    {
        var request = new
        {
            id = Guid.NewGuid(),
            productNumber = "PRD-9999",
            itemType = "COMPONENT",
            isActive = false,
            code = "SERVER",
            nameFa = "محصول سرور",
            nameEn = (string?)null,
            productCategoryId = _category,
            baseUnitId = _unit,
            businessUnitId = (Guid?)null,
            safetyStockQty = 0,
            criticalDays = 0,
            safeStartDays = 0,
            excessStartDays = 0,
            notes = (string?)null
        };
        var response = await _client.PostAsJsonAsync("/api/products", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await ReadCreatedDocument(response);
        Assert.NotEqual("PRD-9999", created.Code);
        var detail = await _client.GetFromJsonAsync<ProductDetailDto>($"/api/products/{created.Id}");
        Assert.True(detail!.IsActive);
    }

    [Fact]
    public async Task Wrong_reference_types_and_inactive_new_assignments_are_rejected()
    {
        var wrongCategory = await CreateReference("BUSINESS_UNIT", "WRONG-CAT", "گروه اشتباه");
        var wrongBusiness = await CreateReference("PRODUCT_CATEGORY", "WRONG-BU", "واحد اشتباه");
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/products", Request("BAD-CAT") with { ProductCategoryId = wrongCategory })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/products", Request("BAD-BU") with { BusinessUnitId = wrongBusiness })).StatusCode);
        var unit = await _client.GetFromJsonAsync<ReferenceDto>($"/api/reference-data/units/{_unit}");
        await _client.PostAsJsonAsync($"/api/reference-data/units/{_unit}/deactivate", new { expectedVersion = unit!.Version });
        Assert.Equal(HttpStatusCode.BadRequest, (await Create("INACTIVE-UNIT")).StatusCode);
    }

    [Fact]
    public async Task Lifecycle_filter_and_reactivation_with_active_references_work()
    {
        var created = await ReadCreated(await Create("ACTIVE-LIFE"));
        var deactivatedResponse = await _client.PostAsJsonAsync($"/api/products/{created.Id}/deactivate", new { expectedVersion = created.Version });
        var deactivated = await deactivatedResponse.Content.ReadFromJsonAsync<ProductDto>();
        var normal = await _client.GetFromJsonAsync<ProductListDto>("/api/products?page=1&pageSize=25");
        Assert.DoesNotContain(normal!.Items, x => x.Id == created.Id);
        var historical = await _client.GetFromJsonAsync<ProductListDto>("/api/products?includeInactive=true&page=1&pageSize=25");
        Assert.Contains(historical!.Items, x => x.Id == created.Id);
        var reactivated = await _client.PostAsJsonAsync($"/api/products/{created.Id}/reactivate", new { expectedVersion = deactivated!.Version });
        Assert.Equal(HttpStatusCode.OK, reactivated.StatusCode);
        var value = await reactivated.Content.ReadFromJsonAsync<ProductDto>(); Assert.True(value!.Version > deactivated.Version);
    }

    [Fact]
    public async Task Names_and_notes_boundaries_are_enforced()
    {
        var accepted = Request(new string('A', 64)) with { NameFa = new string('ف', 200), NameEn = new string('E', 200), Notes = new string('ن', 2000) };
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/products", accepted)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/products", Request("NO-FA") with { NameFa = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/products", Request("LONG-FA") with { NameFa = new string('ف', 201) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/products", Request("LONG-EN") with { NameEn = new string('E', 201) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/products", Request("LONG-NOTE") with { Notes = new string('ن', 2001) })).StatusCode);
        var nullable = await _client.PostAsJsonAsync("/api/products", Request("NULL-EN") with { NameEn = null, Notes = null });
        Assert.Equal(HttpStatusCode.Created, nullable.StatusCode);
        var whitespace = await ReadCreated(await _client.PostAsJsonAsync("/api/products", Request("EMPTY-EN") with { NameEn = "   " }));
        using var detail = JsonDocument.Parse(await (await _client.GetAsync($"/api/products/{whitespace.Id}")).Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, detail.RootElement.GetProperty("nameEn").ValueKind);
    }

    [Fact]
    public async Task Safety_stock_and_threshold_boundaries_round_trip_exactly()
    {
        const decimal exact = 12345678901.123456m;
        var response = await _client.PostAsJsonAsync("/api/products", Request("DECIMAL") with { SafetyStockQty = exact, CriticalDays = 4, SafeStartDays = 4, ExcessStartDays = 4 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); Assert.Equal(exact, json.RootElement.GetProperty("product").GetProperty("safetyStockQty").GetDecimal());
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/products", Request("NEG") with { SafetyStockQty = -0.000001m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/products", Request("CRIT") with { CriticalDays = 3, SafeStartDays = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/products", Request("SAFE") with { SafeStartDays = 4, ExcessStartDays = 3 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/products", Request("NEG-CRIT") with { CriticalDays = -1 })).StatusCode);
    }

    [Fact]
    public async Task Client_supplied_code_on_edit_is_ignored_and_official_code_is_immutable()
    {
        var created = await ReadCreated(await Create("RENAME-ME"));
        var before = await _client.GetFromJsonAsync<ProductCodeDto>($"/api/products/{created.Id}"); _factory.AuditSink.Clear();
        var update = await _client.PutAsJsonAsync($"/api/products/{created.Id}", Request("MANUAL-RENAME") with { ExpectedVersion = created.Version });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        using var detail = JsonDocument.Parse(await (await _client.GetAsync($"/api/products/{created.Id}")).Content.ReadAsStringAsync());
        Assert.Equal(before!.Code, detail.RootElement.GetProperty("code").GetString()); Assert.True(detail.RootElement.GetProperty("version").GetInt64() > created.Version);
        Assert.Single(_factory.AuditSink.Snapshot(), x => x.EntityType == "Product" && x.Action == "EDIT");
    }

    [Fact]
    public async Task Inactive_category_unit_and_business_unit_remain_historically_resolvable_and_editable_when_unchanged()
    {
        var created = await ReadCreated(await Create("HISTORY-ALL"));
        await DeactivateReference($"/api/reference-data/values/{_category}");
        await DeactivateReference($"/api/reference-data/units/{_unit}");
        await DeactivateReference($"/api/reference-data/values/{_business}");
        var edit = await _client.PutAsJsonAsync($"/api/products/{created.Id}", Request("HISTORY-EDIT") with { ExpectedVersion = created.Version });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        using var detail = JsonDocument.Parse(await (await _client.GetAsync($"/api/products/{created.Id}")).Content.ReadAsStringAsync());
        Assert.False(detail.RootElement.GetProperty("productCategory").GetProperty("isActive").GetBoolean());
        Assert.False(detail.RootElement.GetProperty("baseUnit").GetProperty("isActive").GetBoolean());
        Assert.False(detail.RootElement.GetProperty("businessUnit").GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Inactive_category_and_business_unit_new_assignments_are_rejected_while_null_business_unit_is_allowed()
    {
        await DeactivateReference($"/api/reference-data/values/{_category}");
        Assert.Equal(HttpStatusCode.BadRequest, (await Create("INACTIVE-CATEGORY")).StatusCode);
        _category = await CreateReference("PRODUCT_CATEGORY", "FG2", "محصول دوم");
        await DeactivateReference($"/api/reference-data/values/{_business}");
        Assert.Equal(HttpStatusCode.BadRequest, (await Create("INACTIVE-BUSINESS")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/products", Request("NULL-BUSINESS") with { BusinessUnitId = null })).StatusCode);
    }

    [Fact]
    public async Task Duplicate_name_warning_matrix_and_self_exclusion_are_correct()
    {
        var first = await ReadCreated(await _client.PostAsJsonAsync("/api/products", Request("NAME-1") with { NameFa = " نام مشترک ", NameEn = " Shared Name " }));
        var self = await _client.PutAsJsonAsync($"/api/products/{first.Id}", Request("NAME-1") with { NameFa = "نام مشترک", NameEn = "Shared Name", ExpectedVersion = first.Version });
        Assert.Equal(HttpStatusCode.OK, self.StatusCode);
        using var selfJson = JsonDocument.Parse(await self.Content.ReadAsStringAsync());
        var selfWarnings = selfJson.RootElement.GetProperty("warnings").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.DoesNotContain("DUPLICATE_NAME_FA", selfWarnings);
        Assert.DoesNotContain("DUPLICATE_NAME_EN", selfWarnings);

        var secondResponse = await _client.PostAsJsonAsync("/api/products", Request("NAME-2") with { NameFa = "نام مشترک", NameEn = "shared name" });
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode); using var second = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());
        var warnings = second.RootElement.GetProperty("warnings").EnumerateArray().Select(x => x.GetString()).ToArray(); Assert.Contains("DUPLICATE_NAME_FA", warnings); Assert.Contains("DUPLICATE_NAME_EN", warnings);
        var empty = await _client.PostAsJsonAsync("/api/products", Request("NAME-3") with { NameFa = "نام سوم", NameEn = " " });
        using var emptyJson = JsonDocument.Parse(await empty.Content.ReadAsStringAsync()); Assert.Empty(emptyJson.RootElement.GetProperty("warnings").EnumerateArray());
        Assert.NotNull(await _client.GetFromJsonAsync<ProductDetailDto>($"/api/products/{second.RootElement.GetProperty("product").GetProperty("id").GetGuid()}"));
    }

    [Fact]
    public async Task Multiple_null_english_names_save_without_duplicate_english_warning()
    {
        var firstResponse = await _client.PostAsJsonAsync("/api/products", Request("NULL-NAME-1") with { NameFa = "محصول بدون نام انگلیسی یک", NameEn = null });
        var secondResponse = await _client.PostAsJsonAsync("/api/products", Request("NULL-NAME-2") with { NameFa = "محصول بدون نام انگلیسی دو", NameEn = null });

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        using var firstJson = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
        using var secondJson = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());
        Assert.DoesNotContain("DUPLICATE_NAME_EN", firstJson.RootElement.GetProperty("warnings").EnumerateArray().Select(x => x.GetString()));
        Assert.DoesNotContain("DUPLICATE_NAME_EN", secondJson.RootElement.GetProperty("warnings").EnumerateArray().Select(x => x.GetString()));
        var firstId = firstJson.RootElement.GetProperty("product").GetProperty("id").GetGuid();
        var secondId = secondJson.RootElement.GetProperty("product").GetProperty("id").GetGuid();
        Assert.NotNull(await _client.GetFromJsonAsync<ProductDetailDto>($"/api/products/{firstId}"));
        Assert.NotNull(await _client.GetFromJsonAsync<ProductDetailDto>($"/api/products/{secondId}"));
    }

    [Fact]
    public async Task Official_generated_product_code_is_searchable()
    {
        var created = await ReadCreatedDocument(await Create("SEARCH-OFFICIAL-CODE"));

        var result = await _client.GetFromJsonAsync<ProductListDto>(
            $"/api/products?search={Uri.EscapeDataString(created.Code)}&page=1&pageSize=25");

        var product = Assert.Single(result!.Items);
        Assert.Equal(created.Id, product.Id);
        Assert.Equal(created.Code, product.Code);
    }

    [Fact]
    public async Task Stale_deactivate_and_reactivate_preserve_state_version_and_audit()
    {
        var created = await ReadCreated(await Create("STALE-LIFE")); _factory.AuditSink.Clear();
        var deactivate = await _client.PostAsJsonAsync($"/api/products/{created.Id}/deactivate", new { expectedVersion = created.Version });
        var inactive = await deactivate.Content.ReadFromJsonAsync<ProductDto>();
        var staleDeactivate = await _client.PostAsJsonAsync($"/api/products/{created.Id}/deactivate", new { expectedVersion = created.Version });
        Assert.Equal(HttpStatusCode.Conflict, staleDeactivate.StatusCode); Assert.Equal("STALE_VERSION", await ProblemCode(staleDeactivate));
        var reactivate = await _client.PostAsJsonAsync($"/api/products/{created.Id}/reactivate", new { expectedVersion = inactive!.Version });
        var active = await reactivate.Content.ReadFromJsonAsync<ProductDto>();
        var staleReactivate = await _client.PostAsJsonAsync($"/api/products/{created.Id}/reactivate", new { expectedVersion = inactive.Version });
        Assert.Equal(HttpStatusCode.Conflict, staleReactivate.StatusCode); Assert.Equal("STALE_VERSION", await ProblemCode(staleReactivate));
        var detail = await _client.GetFromJsonAsync<ProductStateDto>($"/api/products/{created.Id}"); Assert.True(detail!.IsActive); Assert.Equal(active!.Version, detail.Version);
        Assert.Single(_factory.AuditSink.Snapshot(), x => x.Action == "DEACTIVATE"); Assert.Single(_factory.AuditSink.Snapshot(), x => x.Action == "REACTIVATE");
    }

    [Fact]
    public async Task Post_commit_audit_sink_failure_does_not_turn_committed_product_into_failure()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HadronDbContext>>();
        var service = new ProductService(dbFactory, new ThrowingAuditSink(), new Microsoft.AspNetCore.Http.HttpContextAccessor(), NullLogger<ProductService>.Instance);
        var result = await service.Create(new("محصول ممیزی", null, _category, _unit, null, 0, 0, 0, 0, null), CancellationToken.None);
        Assert.Matches("^PRD-[0-9]{4,}$", result.Product.Code);
        await using var db = await dbFactory.CreateDbContextAsync(); Assert.True(await db.Items.AnyAsync(x => x.Id == result.Product.Id && x.BusinessNumber == result.Product.Code));
    }

    [Theory]
    [InlineData("category", "productCategoryId")]
    [InlineData("unit", "baseUnitId")]
    [InlineData("business", "businessUnitId")]
    public async Task Reactivation_reports_each_inactive_reference_without_mutation_or_audit(string reference, string detailKey)
    {
        var created = await ReadCreated(await Create($"BLOCK-{reference}"));
        var deactivate = await _client.PostAsJsonAsync($"/api/products/{created.Id}/deactivate", new { expectedVersion = created.Version });
        using var inactiveJson = JsonDocument.Parse(await deactivate.Content.ReadAsStringAsync());
        var beforeVersion = inactiveJson.RootElement.GetProperty("version").GetInt64();
        var beforeUpdated = inactiveJson.RootElement.GetProperty("updatedAtUtc").GetDateTime();
        await DeactivateReference(reference switch { "category" => $"/api/reference-data/values/{_category}", "unit" => $"/api/reference-data/units/{_unit}", _ => $"/api/reference-data/values/{_business}" });
        _factory.AuditSink.Clear();
        var blocked = await _client.PostAsJsonAsync($"/api/products/{created.Id}/reactivate", new { expectedVersion = beforeVersion });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode); Assert.Equal("PRODUCT_REACTIVATION_BLOCKED", await ProblemCode(blocked));
        using var problem = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync()); Assert.True(problem.RootElement.GetProperty("details").TryGetProperty(detailKey, out _));
        using var after = JsonDocument.Parse(await (await _client.GetAsync($"/api/products/{created.Id}")).Content.ReadAsStringAsync());
        Assert.False(after.RootElement.GetProperty("isActive").GetBoolean()); Assert.Equal(beforeVersion, after.RootElement.GetProperty("version").GetInt64()); Assert.Equal(beforeUpdated, after.RootElement.GetProperty("updatedAtUtc").GetDateTime());
        Assert.DoesNotContain(_factory.AuditSink.Snapshot(), x => x.EntityType == "Product" && x.Action == "REACTIVATE");
    }

    [Fact]
    public async Task Validation_and_duplicate_create_failures_emit_no_successful_product_audit()
    {
        _factory.AuditSink.Clear();
        var invalid = await _client.PostAsJsonAsync("/api/products", Request("INVALID-AUDIT") with { NameFa = "" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); Assert.DoesNotContain(_factory.AuditSink.Snapshot(), x => x.EntityType == "Product");
        await Create("DUP-AUDIT"); _factory.AuditSink.Clear();
        Assert.DoesNotContain(_factory.AuditSink.Snapshot(), x => x.EntityType == "Product");
    }

    private Task<HttpResponseMessage> Create(string code) => _client.PostAsJsonAsync("/api/products", Request(code));
    private ProductRequest Request(string code) => new(code, "محصول آزمایشی", null, _category, _unit, _business, 12.345678m, 1, 2, 3, "یادداشت", null);
    private async Task<Guid> CreateReference(string type, string code, string name) { var response = await _client.PostAsJsonAsync($"/api/reference-data/types/{type}/values", new { code, nameFa = name, nameEn = code, sortOrder = 0 }); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); return json.RootElement.GetProperty("id").GetGuid(); }
    private async Task<Guid> CreateUnit() { var response = await _client.PostAsJsonAsync("/api/reference-data/units", new { code = "PCS", nameFa = "عدد", nameEn = "Piece", symbol = (string?)null, sortOrder = 0 }); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); return json.RootElement.GetProperty("id").GetGuid(); }
    private static async Task<ProductDto> ReadCreated(HttpResponseMessage response) { using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var p = json.RootElement.GetProperty("product"); return new(p.GetProperty("id").GetGuid(), p.GetProperty("version").GetInt64(), new(p.GetProperty("productCategory").GetProperty("isActive").GetBoolean())); }
    private static async Task<ProductDto> ReadMutation(HttpResponseMessage response) => await ReadCreated(response);
    private static async Task<(Guid Id, string Code)> ReadCreatedDocument(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var product = json.RootElement.GetProperty("product");
        return (product.GetProperty("id").GetGuid(), product.GetProperty("code").GetString()!);
    }
    private async Task DeactivateReference(string path) { var current = await _client.GetFromJsonAsync<ReferenceDto>(path); var response = await _client.PostAsJsonAsync($"{path}/deactivate", new { expectedVersion = current!.Version }); Assert.Equal(HttpStatusCode.OK, response.StatusCode); }
    private static async Task<string?> ProblemCode(HttpResponseMessage response) { using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); return json.RootElement.GetProperty("code").GetString(); }
    private sealed record ProductRequest(string Code, string NameFa, string? NameEn, Guid ProductCategoryId, Guid BaseUnitId, Guid? BusinessUnitId, decimal SafetyStockQty, int CriticalDays, int SafeStartDays, int ExcessStartDays, string? Notes, long? ExpectedVersion, long? RequestedVersion = null);
    private sealed record ProductDto(Guid Id, long Version, RefDto ProductCategory);
    private sealed record ProductDetailDto(Guid Id, string Code, bool IsActive);
    private sealed record ProductStateDto(Guid Id, bool IsActive, long Version);
    private sealed record ProductCodeDto(Guid Id, string Code);
    private sealed record ProductListDto(IReadOnlyList<ProductDetailDto> Items);
    private sealed record RefDto(bool IsActive);
    private sealed record ReferenceDto(long Version);
    private sealed class ThrowingAuditSink : Hadron.Erp.Api.Platform.Audit.IAuditEventSink { public Task WriteAsync(Hadron.Erp.Api.Platform.Audit.AuditEvent auditEvent, CancellationToken cancellationToken) => throw new InvalidOperationException("audit unavailable"); }
}
