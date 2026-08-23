using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Catalog.Application;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Platform.Audit;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Hadron.Erp.IntegrationTests;

[Collection("ReferenceData")]
public sealed class MaterialItemTests : IClassFixture<FoundationApiFactory>, IAsyncLifetime
{
    private readonly FoundationApiFactory _factory;
    private readonly HttpClient _client;
    private Guid _partCategory;
    private Guid _componentCategory;
    private Guid _productCategory;
    private Guid _unit;

    public MaterialItemTests(FoundationApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HadronDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
        TestDatabaseSafety.EnsureSafeDatabaseName(db.Database.GetDbConnection().Database);
        await db.MaterialItemProfiles.ExecuteDeleteAsync();
        await db.Products.ExecuteDeleteAsync();
        await db.Items.ExecuteDeleteAsync();
        await db.ReferenceValues.ExecuteDeleteAsync();
        await db.Units.ExecuteDeleteAsync();
        _factory.AuditSink.Clear();
        _partCategory = await CreateReference("PART_CATEGORY", "PART-GENERAL", "گروه قطعه");
        _componentCategory = await CreateReference("COMPONENT_CATEGORY", "COMP-GENERAL", "گروه کامپوننت");
        _productCategory = await CreateReference("PRODUCT_CATEGORY", "PRODUCT-GENERAL", "گروه محصول");
        _unit = await CreateUnit();
        _factory.AuditSink.Clear();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Part_and_component_create_with_server_identity_nullable_code_and_audit()
    {
        var part = await ReadCreated(await Create("parts", Request("parts", "  part-code  ")));
        var component = await ReadCreated(await Create("components", Request("components", null)));

        Assert.Matches("^PRT-[0-9]{4,}$", part.Number);
        Assert.Matches("^CMP-[0-9]{4,}$", component.Number);
        Assert.Equal("PART-CODE", part.Code);
        Assert.Null(component.Code);
        Assert.Equal("PART", part.ItemType);
        Assert.Equal("COMPONENT", component.ItemType);
        Assert.Contains(_factory.AuditSink.Snapshot(), x => x.EntityType == "Part" && x.Action == "CREATE");
        Assert.Contains(_factory.AuditSink.Snapshot(), x => x.EntityType == "Component" && x.Action == "CREATE");
    }

    [Theory]
    [InlineData("parts", "^PRT-[0-9]{4,}$")]
    [InlineData("components", "^CMP-[0-9]{4,}$")]
    public async Task Concurrent_number_generation_is_unique(string resource, string pattern)
    {
        var tasks = Enumerable.Range(0, 12).Select(index => Create(resource, Request(resource, $"C-{index}")));
        var responses = await Task.WhenAll(tasks);
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var rows = new List<ItemDto>();
        foreach (var response in responses) rows.Add(await ReadCreated(response));
        Assert.Equal(rows.Count, rows.Select(x => x.Number).Distinct().Count());
        Assert.All(rows, row => Assert.Matches(pattern, row.Number));
    }

    [Fact]
    public async Task Non_null_code_is_globally_unique_across_product_part_and_component()
    {
        Assert.Equal(HttpStatusCode.Created, (await CreateProduct("GLOBAL-CODE")).StatusCode);
        var partCollision = await Create("parts", Request("parts", " global-code "));
        Assert.Equal(HttpStatusCode.Conflict, partCollision.StatusCode);
        Assert.Equal("DUPLICATE_CODE", await ProblemCode(partCollision));

        Assert.Equal(HttpStatusCode.Created, (await Create("parts", Request("parts", "PART-ONLY"))).StatusCode);
        var componentCollision = await Create("components", Request("components", "part-only"));
        Assert.Equal(HttpStatusCode.Conflict, componentCollision.StatusCode);
    }

    [Fact]
    public async Task Multiple_null_codes_are_allowed_and_persisted()
    {
        var first = await ReadCreated(await Create("parts", Request("parts", null) with { NameFa = "قطعه بدون کد یک" }));
        var second = await ReadCreated(await Create("parts", Request("parts", "   ") with { NameFa = "قطعه بدون کد دو" }));
        Assert.Null(first.Code);
        Assert.Null(second.Code);
        await using var db = await Db();
        Assert.Equal(2, await db.Items.CountAsync(x => (x.Id == first.Id || x.Id == second.Id) && x.Code == null && x.NormalizedCode == null));
    }

    [Fact]
    public async Task Duplicate_name_warnings_are_type_scoped_self_excluding_and_ignore_empty_english()
    {
        var first = await ReadCreated(await Create("parts", Request("parts", "NAME-1") with { NameFa = "نام مشترک", NameEn = "Shared" }));
        var selfResponse = await _client.PutAsJsonAsync($"/api/parts/{first.Id}", Request("parts", "NAME-1") with { NameFa = "نام مشترک", NameEn = "Shared", ExpectedVersion = first.Version });
        Assert.Equal(HttpStatusCode.OK, selfResponse.StatusCode);
        Assert.Empty(await Warnings(selfResponse));

        var samePart = await Create("parts", Request("parts", "NAME-2") with { NameFa = " نام مشترک ", NameEn = "shared" });
        Assert.Contains("DUPLICATE_NAME_FA", await Warnings(samePart));
        Assert.Contains("DUPLICATE_NAME_EN", await Warnings(samePart));

        var component = await Create("components", Request("components", "NAME-3") with { NameFa = "نام مشترک", NameEn = "Shared" });
        Assert.Empty(await Warnings(component));
        var emptyOne = await Create("components", Request("components", "NAME-4") with { NameFa = "چهار", NameEn = null });
        var emptyTwo = await Create("components", Request("components", "NAME-5") with { NameFa = "پنج", NameEn = " " });
        Assert.DoesNotContain("DUPLICATE_NAME_EN", await Warnings(emptyOne));
        Assert.DoesNotContain("DUPLICATE_NAME_EN", await Warnings(emptyTwo));
    }

    [Fact]
    public async Task Correct_active_category_and_unit_are_required_but_unchanged_inactive_history_resolves()
    {
        var wrong = await Create("parts", Request("parts", "WRONG") with { CategoryId = _componentCategory });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        var created = await ReadCreated(await Create("parts", Request("parts", "HISTORY")));
        await DeactivateReference($"/api/reference-data/values/{_partCategory}");
        await DeactivateReference($"/api/reference-data/units/{_unit}");

        var edit = await _client.PutAsJsonAsync($"/api/parts/{created.Id}", Request("parts", "HISTORY-EDIT") with { ExpectedVersion = created.Version });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var edited = await ReadMutation(edit);
        Assert.False(edited.Category.IsActive);
        Assert.False(edited.BaseUnit.IsActive);

        var inactiveCreate = await Create("parts", Request("parts", "INACTIVE"));
        Assert.Equal(HttpStatusCode.BadRequest, inactiveCreate.StatusCode);
        var deactivated = await _client.PostAsJsonAsync($"/api/parts/{created.Id}/deactivate", new { expectedVersion = edited.Version });
        var inactive = await deactivated.Content.ReadFromJsonAsync<ItemDto>();
        var blocked = await _client.PostAsJsonAsync($"/api/parts/{created.Id}/reactivate", new { expectedVersion = inactive!.Version });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("ITEM_REACTIVATION_BLOCKED", await ProblemCode(blocked));
    }

    [Theory]
    [InlineData("BAD-SUPPLY")]
    [InlineData("")]
    public async Task Invalid_supply_method_is_rejected(string supplyMethod)
    {
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Create("parts", Request("parts", "BAD-SUPPLY") with { SupplyMethod = supplyMethod })).StatusCode);
    }

    [Fact]
    public async Task Numeric_precision_and_validation_boundaries_are_enforced()
    {
        var exact = Request("components", "DECIMAL") with
        {
            NetWeightKg = 12345678901.123456m,
            SafetyStockQty = 98765432101.654321m,
            DefaultPlanningScrapPercent = 99.99m,
            CriticalDays = 4,
            SafeStartDays = 4,
            ExcessStartDays = 4
        };
        var created = await ReadCreated(await Create("components", exact));
        Assert.Equal(exact.NetWeightKg, created.NetWeightKg);
        Assert.Equal(exact.SafetyStockQty, created.SafetyStockQty);
        Assert.Equal(exact.DefaultPlanningScrapPercent, created.DefaultPlanningScrapPercent);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create("components", exact with { Code = "NEG-W", NetWeightKg = -0.000001m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create("components", exact with { Code = "SCRAP-LOW", DefaultPlanningScrapPercent = -0.01m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create("components", exact with { Code = "SCRAP-HIGH", DefaultPlanningScrapPercent = 100.01m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create("components", exact with { Code = "NEG-S", SafetyStockQty = -0.000001m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create("components", exact with { Code = "ORDER", CriticalDays = 2, SafeStartDays = 1 })).StatusCode);
    }

    [Fact]
    public async Task Required_names_optional_english_and_field_lengths_are_enforced()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Create("parts", Request("parts", "NO-FA") with { NameFa = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Create("parts", Request("parts", "NULL-EN") with { NameEn = null })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create("parts", Request("parts", new string('A', 65)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create("parts", Request("parts", "LONG-EN") with { NameEn = new string('E', 201) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create("parts", Request("parts", "LONG-NOTE") with { Notes = new string('ن', 2001) })).StatusCode);
    }

    [Fact]
    public async Task Lifecycle_and_stale_mutations_preserve_state_version_and_audit()
    {
        var created = await ReadCreated(await Create("components", Request("components", "LIFE")));
        var update = await _client.PutAsJsonAsync($"/api/components/{created.Id}", Request("components", "LIFE-EDIT") with { ExpectedVersion = created.Version });
        var edited = await ReadMutation(update);
        var editAuditCount = _factory.AuditSink.Snapshot().Count(x => x.EntityType == "Component" && x.Action == "EDIT");
        var stale = await _client.PutAsJsonAsync($"/api/components/{created.Id}", Request("components", "STALE") with { ExpectedVersion = created.Version });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("STALE_VERSION", await ProblemCode(stale));
        var current = await _client.GetFromJsonAsync<ItemDto>($"/api/components/{created.Id}");
        Assert.Equal("LIFE-EDIT", current!.Code);
        Assert.Equal(edited.Version, current.Version);
        Assert.Equal(editAuditCount, _factory.AuditSink.Snapshot().Count(x => x.EntityType == "Component" && x.Action == "EDIT"));

        var deactivated = await _client.PostAsJsonAsync($"/api/components/{created.Id}/deactivate", new { expectedVersion = edited.Version });
        var inactive = await deactivated.Content.ReadFromJsonAsync<ItemDto>();
        var staleLifecycle = await _client.PostAsJsonAsync($"/api/components/{created.Id}/reactivate", new { expectedVersion = edited.Version });
        Assert.Equal(HttpStatusCode.Conflict, staleLifecycle.StatusCode);
        var reactivated = await _client.PostAsJsonAsync($"/api/components/{created.Id}/reactivate", new { expectedVersion = inactive!.Version });
        Assert.Equal(HttpStatusCode.OK, reactivated.StatusCode);
    }

    [Fact]
    public async Task Client_cannot_override_number_type_or_active_state_and_no_delete_exists()
    {
        var request = Request("parts", "SERVER") with { NameFa = "قطعه سرور" };
        var payload = new Dictionary<string, object?>
        {
            ["number"] = "PRT-9999",
            ["businessNumber"] = "CMP-9999",
            ["itemType"] = "COMPONENT",
            ["isActive"] = false,
            ["code"] = request.Code,
            ["nameFa"] = request.NameFa,
            ["nameEn"] = request.NameEn,
            ["categoryId"] = request.CategoryId,
            ["baseUnitId"] = request.BaseUnitId,
            ["supplyMethod"] = request.SupplyMethod,
            ["netWeightKg"] = request.NetWeightKg,
            ["defaultPlanningScrapPercent"] = request.DefaultPlanningScrapPercent,
            ["safetyStockQty"] = request.SafetyStockQty,
            ["criticalDays"] = request.CriticalDays,
            ["safeStartDays"] = request.SafeStartDays,
            ["excessStartDays"] = request.ExcessStartDays,
            ["notes"] = request.Notes
        };
        var created = await ReadCreated(await _client.PostAsJsonAsync("/api/parts", payload));
        Assert.NotEqual("PRT-9999", created.Number);
        Assert.Equal("PART", created.ItemType);
        Assert.True(created.IsActive);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await _client.DeleteAsync($"/api/parts/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Database_enforces_product_required_code_and_code_pair_integrity()
    {
        var product = await ReadProduct(await CreateProduct("DB-PRODUCT"));
        var part = await ReadCreated(await Create("parts", Request("parts", "DB-PART")));
        await using var db = await Db();
        var productFailure = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE catalog.items SET \"Code\" = NULL, \"NormalizedCode\" = NULL WHERE \"Id\" = {product.Id}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, productFailure.SqlState);
        db.ChangeTracker.Clear();
        var pairFailure = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE catalog.items SET \"NormalizedCode\" = NULL WHERE \"Id\" = {part.Id}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, pairFailure.SqlState);
    }

    [Fact]
    public async Task Audit_sink_failure_is_post_commit_best_effort_and_failed_mutations_emit_no_success_event()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HadronDbContext>>();
        var service = new MaterialItemService(dbFactory, new ThrowingAuditSink(), new HttpContextAccessor(),
            NullLogger<MaterialItemService>.Instance);
        var result = await service.Create(ItemTypes.Part, new("AUDIT-FAIL", "قطعه ممیزی", null, _partCategory,
            _unit, "BUY", null, 0, 0, 0, 0, 0, null), CancellationToken.None);
        await using var db = await dbFactory.CreateDbContextAsync();
        Assert.True(await db.Items.AnyAsync(x => x.Id == result.Item.Id));

        _factory.AuditSink.Clear();
        var invalid = await Create("components", Request("components", "INVALID-AUDIT") with { NameFa = "" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.DoesNotContain(_factory.AuditSink.Snapshot(), x => x.EntityType == "Component");
    }

    [Fact]
    public void Model_contains_only_approved_master_fields_and_no_future_scope()
    {
        var names = typeof(MaterialItemProfile).GetProperties().Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var forbidden in new[] { "Supplier", "Price", "Currency", "Moq", "LeadTime", "ShippingMode",
            "Incoterm", "ForwarderDays", "ShippingDays", "CustomsDays", "PreferredSupplier", "InitialInventory",
            "GrossWeight", "OutputPart", "Ready", "Image", "File" }) Assert.DoesNotContain(forbidden, names);
        Assert.DoesNotContain(typeof(Item).GetProperties(), property => property.Name is "Ready" or "HasBom" or "PlanningReady");
    }

    private Task<HttpResponseMessage> Create(string resource, MaterialRequest request) =>
        _client.PostAsJsonAsync($"/api/{resource}", request);
    private MaterialRequest Request(string resource, string? code) => new(code, "آیتم آزمایشی", null,
        resource == "parts" ? _partCategory : _componentCategory, _unit, "BUY", 1.123456m, 2.50m,
        3.654321m, 1, 2, 3, "یادداشت", null);
    private Task<HttpResponseMessage> CreateProduct(string code) => _client.PostAsJsonAsync("/api/products", new
    {
        code,
        nameFa = "محصول تست",
        nameEn = (string?)null,
        productCategoryId = _productCategory,
        baseUnitId = _unit,
        businessUnitId = (Guid?)null,
        safetyStockQty = 0,
        criticalDays = 0,
        safeStartDays = 0,
        excessStartDays = 0,
        notes = (string?)null
    });
    private async Task<Guid> CreateReference(string type, string code, string name)
    {
        var response = await _client.PostAsJsonAsync($"/api/reference-data/types/{type}/values",
            new { code, nameFa = name, nameEn = code, sortOrder = 0 });
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }
    private async Task<Guid> CreateUnit()
    {
        var response = await _client.PostAsJsonAsync("/api/reference-data/units",
            new { code = "PCS", nameFa = "عدد", nameEn = "Piece", symbol = (string?)null, sortOrder = 0 });
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }
    private async Task DeactivateReference(string path)
    {
        using var current = JsonDocument.Parse(await (await _client.GetAsync(path)).Content.ReadAsStringAsync());
        var response = await _client.PostAsJsonAsync($"{path}/deactivate",
            new { expectedVersion = current.RootElement.GetProperty("version").GetInt64() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
    private async Task<HadronDbContext> Db()
    {
        return await _factory.Services.GetRequiredService<IDbContextFactory<HadronDbContext>>().CreateDbContextAsync();
    }
    private static async Task<ItemDto> ReadCreated(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return JsonSerializer.Deserialize<ItemDto>(json.RootElement.GetProperty("item"), JsonOptions)!;
    }
    private static async Task<ItemDto> ReadMutation(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return JsonSerializer.Deserialize<ItemDto>(json.RootElement.GetProperty("item"), JsonOptions)!;
    }
    private static async Task<IReadOnlyList<string>> Warnings(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("warnings").EnumerateArray().Select(x => x.GetString()!).ToList();
    }
    private static async Task<string?> ProblemCode(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("code").GetString();
    }
    private static async Task<ProductDto> ReadProduct(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new(json.RootElement.GetProperty("product").GetProperty("id").GetGuid());
    }
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private sealed record MaterialRequest(string? Code, string NameFa, string? NameEn, Guid CategoryId,
        Guid BaseUnitId, string SupplyMethod, decimal? NetWeightKg, decimal DefaultPlanningScrapPercent,
        decimal SafetyStockQty, int CriticalDays, int SafeStartDays, int ExcessStartDays, string? Notes,
        long? ExpectedVersion);
    private sealed record ItemDto(Guid Id, string ItemType, string Number, string? Code, string NameFa,
        RefDto Category, RefDto BaseUnit, decimal? NetWeightKg, decimal DefaultPlanningScrapPercent,
        decimal SafetyStockQty, bool IsActive, long Version);
    private sealed record RefDto(bool IsActive);
    private sealed record ProductDto(Guid Id);
    private sealed class ThrowingAuditSink : IAuditEventSink
    { public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken) => throw new InvalidOperationException("audit unavailable"); }
}
