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
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
    public async Task Part_and_component_create_with_server_generated_official_code_and_audit()
    {
        var part = await ReadCreated(await Create("parts", Request("parts", "  part-code  ")));
        var component = await ReadCreated(await Create("components", Request("components", null)));

        Assert.Matches("^PRT-[0-9]{4,}$", part.Code);
        Assert.Matches("^CMP-[0-9]{4,}$", component.Code);
        Assert.NotEqual("PART-CODE", part.Code);
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
        Assert.Equal(rows.Count, rows.Select(x => x.Code).Distinct().Count());
        Assert.All(rows, row => Assert.Matches(pattern, row.Code));
    }

    [Fact]
    public async Task Official_codes_are_globally_unique_across_product_part_and_component()
    {
        var product = await ReadProductCode(await CreateProduct("GLOBAL-CODE"));
        var part = await ReadCreated(await Create("parts", Request("parts", " global-code ")));
        var component = await ReadCreated(await Create("components", Request("components", "global-code")));
        Assert.Equal(3, new[] { product.Code, part.Code, component.Code }.Distinct().Count());
    }

    [Theory]
    [InlineData("parts", "^PRT-[0-9]{4,}$")]
    [InlineData("components", "^CMP-[0-9]{4,}$")]
    public async Task Official_generated_material_item_code_is_searchable(string resource, string pattern)
    {
        var created = await ReadCreated(await Create(resource, Request(resource, "SEARCH-OFFICIAL-CODE")));
        Assert.Matches(pattern, created.Code);

        var result = await _client.GetFromJsonAsync<MaterialListDto>(
            $"/api/{resource}?search={Uri.EscapeDataString(created.Code)}&page=1&pageSize=25");

        var item = Assert.Single(result!.Items);
        Assert.Equal(created.Id, item.Id);
        Assert.Equal(created.Code, item.Code);
    }

    [Fact]
    public async Task Client_null_or_manual_codes_are_ignored_and_generated_codes_persist()
    {
        var first = await ReadCreated(await Create("parts", Request("parts", null) with { NameFa = "قطعه بدون کد یک" }));
        var second = await ReadCreated(await Create("parts", Request("parts", "   ") with { NameFa = "قطعه بدون کد دو" }));
        Assert.Matches("^PRT-[0-9]{4,}$", first.Code);
        Assert.Matches("^PRT-[0-9]{4,}$", second.Code);
        Assert.NotEqual(first.Code, second.Code);
        await using var db = await Db();
        Assert.Equal(2, await db.Items.CountAsync(x => (x.Id == first.Id || x.Id == second.Id) && x.BusinessNumber != null));
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
        Assert.Equal(HttpStatusCode.BadRequest, (await Create("parts", Request("parts", "LONG-EN") with { NameEn = new string('E', 201) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create("parts", Request("parts", "LONG-NOTE") with { Notes = new string('ن', 2001) })).StatusCode);
    }

    [Fact]
    public async Task Lifecycle_and_stale_mutations_preserve_state_version_and_audit()
    {
        var created = await ReadCreated(await Create("components", Request("components", "LIFE")));
        var update = await _client.PutAsJsonAsync($"/api/components/{created.Id}", Request("components", "LIFE-EDIT") with { ExpectedVersion = created.Version, RequestedVersion = created.Version });
        var edited = await ReadMutation(update);
        Assert.Equal(created.Version + 1, edited.Version);
        var editAuditCount = _factory.AuditSink.Snapshot().Count(x => x.EntityType == "Component" && x.Action == "EDIT");
        var stale = await _client.PutAsJsonAsync($"/api/components/{created.Id}", Request("components", "STALE") with { ExpectedVersion = created.Version });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("STALE_VERSION", await ProblemCode(stale));
        var current = await _client.GetFromJsonAsync<ItemDto>($"/api/components/{created.Id}");
        Assert.Equal(created.Code, current!.Code);
        Assert.Equal(edited.Version, current.Version);
        Assert.Equal(editAuditCount, _factory.AuditSink.Snapshot().Count(x => x.EntityType == "Component" && x.Action == "EDIT"));

        var deactivated = await _client.PostAsJsonAsync($"/api/components/{created.Id}/deactivate", new { expectedVersion = edited.Version });
        var inactive = await deactivated.Content.ReadFromJsonAsync<ItemDto>();
        var staleLifecycle = await _client.PostAsJsonAsync($"/api/components/{created.Id}/reactivate", new { expectedVersion = edited.Version });
        Assert.Equal(HttpStatusCode.Conflict, staleLifecycle.StatusCode);
        var reactivated = await _client.PostAsJsonAsync($"/api/components/{created.Id}/reactivate", new { expectedVersion = inactive!.Version });
        Assert.Equal(HttpStatusCode.OK, reactivated.StatusCode);
    }

    [Theory]
    [InlineData("parts")]
    [InlineData("components")]
    public async Task Requested_version_can_only_move_forward_without_weakening_stale_write_protection(string resource)
    {
        var created = await ReadCreated(await Create(resource, Request(resource, $"VERSION-{resource}")));
        var requested = await _client.PutAsJsonAsync($"/api/{resource}/{created.Id}",
            Request(resource, $"VERSION-RAISED-{resource}") with
            { ExpectedVersion = created.Version, RequestedVersion = 9 });
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        var raised = await ReadMutation(requested);
        Assert.Equal(9, raised.Version);
        var editAuditCount = _factory.AuditSink.Snapshot().Count(x => x.EntityId == created.Id && x.Action == "EDIT");

        var lower = await _client.PutAsJsonAsync($"/api/{resource}/{created.Id}",
            Request(resource, $"VERSION-LOWER-{resource}") with
            { ExpectedVersion = raised.Version, RequestedVersion = raised.Version - 1 });
        Assert.Equal(HttpStatusCode.BadRequest, lower.StatusCode);

        var stale = await _client.PutAsJsonAsync($"/api/{resource}/{created.Id}",
            Request(resource, $"VERSION-STALE-{resource}") with
            { ExpectedVersion = created.Version, RequestedVersion = 12 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("STALE_VERSION", await ProblemCode(stale));
        var current = await _client.GetFromJsonAsync<ItemDto>($"/api/{resource}/{created.Id}");
        Assert.Equal(raised.Version, current!.Version);
        Assert.Equal(editAuditCount, _factory.AuditSink.Snapshot().Count(x => x.EntityId == created.Id && x.Action == "EDIT"));
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
        Assert.NotEqual("PRT-9999", created.Code);
        Assert.NotEqual("SERVER", created.Code);
        Assert.Equal("PART", created.ItemType);
        Assert.True(created.IsActive);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await _client.DeleteAsync($"/api/parts/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Database_enforces_official_code_immutability_format_and_removed_legacy_columns()
    {
        var product = await ReadProductCode(await CreateProduct("DB-PRODUCT"));
        var part = await ReadCreated(await Create("parts", Request("parts", "DB-PART")));
        await using var db = await Db();
        var renameFailure = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE catalog.items SET \"BusinessNumber\" = {"X"} WHERE \"Id\" = {product.Id}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, renameFailure.SqlState);
        db.ChangeTracker.Clear();
        var invalidFormat = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO catalog.items (\"Id\", \"ItemType\", \"BusinessNumber\", \"NameFa\", \"BaseUnitId\", \"IsActive\", \"Version\", \"CreatedAtUtc\", \"UpdatedAtUtc\") VALUES ({Guid.NewGuid()}, {"PART"}, {"BAD-0001"}, {"بد"}, {_unit}, {true}, {1L}, {DateTime.UtcNow}, {DateTime.UtcNow})"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, invalidFormat.SqlState);
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO catalog.items (\"Id\", \"ItemType\", \"BusinessNumber\", \"NameFa\", \"BaseUnitId\", \"IsActive\", \"Version\", \"CreatedAtUtc\", \"UpdatedAtUtc\") VALUES ({Guid.NewGuid()}, {"PRODUCT"}, {product.Code}, {"تکراری"}, {_unit}, {true}, {1L}, {DateTime.UtcNow}, {DateTime.UtcNow})"));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
        var legacyColumns = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM information_schema.columns WHERE table_schema='catalog' AND table_name='items' AND column_name IN ('Code','NormalizedCode')").SingleAsync();
        Assert.Equal(0, legacyColumns);
        Assert.NotEqual(product.Code, part.Code);
    }

    [Fact]
    public async Task Forward_migration_preserves_legacy_item_relationships_and_safely_synchronizes_sequences()
    {
        await using var source = await Db();
        var sourceBuilder = new NpgsqlConnectionStringBuilder(source.Database.GetConnectionString());
        var databaseName = $"hadron_erp_test_{Guid.NewGuid():N}";
        TestDatabaseSafety.EnsureSafeDatabaseName(databaseName);
        var adminBuilder = new NpgsqlConnectionStringBuilder(sourceBuilder.ConnectionString)
        { Database = "postgres", Pooling = false };
        var targetBuilder = new NpgsqlConnectionStringBuilder(sourceBuilder.ConnectionString)
        { Database = databaseName, Pooling = false };

        await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var options = new DbContextOptionsBuilder<HadronDbContext>()
                .UseNpgsql(targetBuilder.ConnectionString).Options;
            await using var context = new HadronDbContext(options);
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync("20260823121632_AddMultiLevelBom");

            var unitId = Guid.NewGuid();
            var countryId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            var partId = Guid.NewGuid();
            var supplierId = Guid.NewGuid();
            var sourcingId = Guid.NewGuid();
            var revisionId = Guid.NewGuid();
            var lineId = Guid.NewGuid();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO reference_data.units (\"Id\", \"Code\", \"NormalizedCode\", \"NameFa\", \"NameEn\", \"IsActive\", \"SortOrder\", \"Version\") VALUES ({unitId}, {"MIG"}, {"MIG"}, {"واحد مهاجرت"}, {"Migration Unit"}, {true}, {0}, {1L})");
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO reference_data.reference_values (\"Id\", \"ReferenceTypeId\", \"SortOrder\", \"Code\", \"NormalizedCode\", \"NameFa\", \"NameEn\", \"IsActive\", \"Version\") VALUES ({countryId}, {Guid.Parse("10000000-0000-0000-0000-000000000001")}, {0}, {"MIG-COUNTRY"}, {"MIG-COUNTRY"}, {"کشور مهاجرت"}, {"Migration Country"}, {true}, {1L})");
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO catalog.items (\"Id\", \"ItemType\", \"BusinessNumber\", \"Code\", \"NormalizedCode\", \"NameFa\", \"BaseUnitId\", \"IsActive\", \"Version\") VALUES ({productId}, {"PRODUCT"}, {"PRD-0010"}, {"LEGACY-PRODUCT"}, {"LEGACY-PRODUCT"}, {"محصول میراثی"}, {unitId}, {true}, {1L})");
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO catalog.items (\"Id\", \"ItemType\", \"BusinessNumber\", \"Code\", \"NormalizedCode\", \"NameFa\", \"BaseUnitId\", \"IsActive\", \"Version\") VALUES ({partId}, {"PART"}, {"PRT-0010"}, {"LEGACY-PART"}, {"LEGACY-PART"}, {"قطعه میراثی"}, {unitId}, {true}, {1L})");
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO procurement.suppliers (\"Id\", \"SupplierNumber\", \"Name\", \"NormalizedName\", \"CountryId\", \"IsActive\", \"Version\") VALUES ({supplierId}, {"SUP-MIGRATION"}, {"تامین‌کننده مهاجرت"}, {"تامین‌کننده مهاجرت"}, {countryId}, {false}, {1L})");
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO procurement.sourcing_options (\"Id\", \"SourcingNumber\", \"ItemId\", \"SupplierId\", \"ItemBaseUnitId\", \"IsPreferred\", \"IsActive\", \"Version\") VALUES ({sourcingId}, {"SRC-MIGRATION"}, {partId}, {supplierId}, {unitId}, {false}, {true}, {1L})");
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO engineering.bom_revisions (\"Id\", \"RevisionCode\", \"ParentItemId\", \"ParentBaseUnitId\", \"Status\", \"BaseOutputQty\", \"Version\") VALUES ({revisionId}, {"BOM-MIGRATION"}, {productId}, {unitId}, {"DRAFT"}, {1m}, {1L})");
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO engineering.bom_lines (\"Id\", \"BomRevisionId\", \"ChildItemId\", \"ChildBaseUnitId\", \"UsageQty\", \"SortOrder\") VALUES ({lineId}, {revisionId}, {partId}, {unitId}, {1m}, {0})");

            await context.Database.ExecuteSqlRawAsync("SELECT setval('catalog.product_number_seq', 10, false)");
            await context.Database.ExecuteSqlRawAsync("SELECT setval('catalog.part_number_seq', 50, false)");
            await context.Database.ExecuteSqlRawAsync("SELECT setval('catalog.component_number_seq', 1, false)");

            await migrator.MigrateAsync();

            await using var connection = new NpgsqlConnection(targetBuilder.ConnectionString);
            await connection.OpenAsync();
            await using var verify = new NpgsqlCommand(
                """
                SELECT
                    (SELECT COUNT(*) FROM catalog.item_legacy_codes WHERE "ItemId" = @product_id AND "LegacyCode" = 'LEGACY-PRODUCT' AND "LegacyNormalizedCode" = 'LEGACY-PRODUCT'),
                    (SELECT COUNT(*) FROM catalog.item_legacy_codes WHERE "ItemId" = @part_id AND "LegacyCode" = 'LEGACY-PART' AND "LegacyNormalizedCode" = 'LEGACY-PART'),
                    (SELECT COUNT(*) FROM catalog.items WHERE ("Id" = @product_id AND "BusinessNumber" = 'PRD-0010') OR ("Id" = @part_id AND "BusinessNumber" = 'PRT-0010')),
                    (SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'catalog' AND table_name = 'items' AND column_name IN ('Code', 'NormalizedCode')),
                    (SELECT COUNT(*) FROM procurement.sourcing_options so JOIN catalog.items i ON i."Id" = so."ItemId" WHERE so."Id" = @sourcing_id AND i."Id" = @part_id),
                    (SELECT COUNT(*) FROM engineering.bom_revisions br JOIN engineering.bom_lines bl ON bl."BomRevisionId" = br."Id" WHERE br."Id" = @revision_id AND br."ParentItemId" = @product_id AND bl."ChildItemId" = @part_id),
                    (SELECT i."BusinessNumber" FROM procurement.sourcing_options so JOIN catalog.items i ON i."Id" = so."ItemId" WHERE so."Id" = @sourcing_id),
                    (SELECT i."BusinessNumber" FROM engineering.bom_revisions br JOIN catalog.items i ON i."Id" = br."ParentItemId" WHERE br."Id" = @revision_id),
                    (SELECT i."BusinessNumber" FROM engineering.bom_lines bl JOIN catalog.items i ON i."Id" = bl."ChildItemId" WHERE bl."Id" = @line_id),
                    nextval('catalog.product_number_seq'),
                    nextval('catalog.part_number_seq'),
                    nextval('catalog.component_number_seq');
                """, connection);
            verify.Parameters.AddWithValue("product_id", productId);
            verify.Parameters.AddWithValue("part_id", partId);
            verify.Parameters.AddWithValue("sourcing_id", sourcingId);
            verify.Parameters.AddWithValue("revision_id", revisionId);
            verify.Parameters.AddWithValue("line_id", lineId);
            await using var reader = await verify.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1L, reader.GetInt64(0));
            Assert.Equal(1L, reader.GetInt64(1));
            Assert.Equal(2L, reader.GetInt64(2));
            Assert.Equal(0L, reader.GetInt64(3));
            Assert.Equal(1L, reader.GetInt64(4));
            Assert.Equal(1L, reader.GetInt64(5));
            Assert.Equal("PRT-0010", reader.GetString(6));
            Assert.Equal("PRD-0010", reader.GetString(7));
            Assert.Equal("PRT-0010", reader.GetString(8));
            Assert.Equal(11L, reader.GetInt64(9));
            Assert.Equal(50L, reader.GetInt64(10));
            Assert.Equal(1L, reader.GetInt64(11));

            await reader.CloseAsync();
            context.ChangeTracker.Clear();
            var sourcing = await context.SourcingOptions.AsNoTracking().Include(x => x.Item)
                .SingleAsync(x => x.Id == sourcingId);
            Assert.Equal(partId, sourcing.ItemId);
            Assert.Equal("PRT-0010", sourcing.Item.BusinessNumber);
            var bom = await context.BomRevisions.AsNoTracking().Include(x => x.ParentItem)
                .Include(x => x.Lines).ThenInclude(x => x.ChildItem).SingleAsync(x => x.Id == revisionId);
            Assert.Equal(productId, bom.ParentItemId);
            Assert.Equal("PRD-0010", bom.ParentItem.BusinessNumber);
            Assert.Equal(partId, Assert.Single(bom.Lines).ChildItemId);
            Assert.Equal("PRT-0010", Assert.Single(bom.Lines).ChildItem.BusinessNumber);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(adminBuilder.ConnectionString);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Audit_sink_failure_is_post_commit_best_effort_and_failed_mutations_emit_no_success_event()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HadronDbContext>>();
        var service = new MaterialItemService(dbFactory, new ThrowingAuditSink(), new HttpContextAccessor(),
            NullLogger<MaterialItemService>.Instance);
        var result = await service.Create(ItemTypes.Part, new("قطعه ممیزی", null, _partCategory,
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
    private static async Task<ProductCodeDto> ReadProductCode(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var product = json.RootElement.GetProperty("product");
        return new(product.GetProperty("id").GetGuid(), product.GetProperty("code").GetString()!);
    }
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private sealed record MaterialRequest(string? Code, string NameFa, string? NameEn, Guid CategoryId,
        Guid BaseUnitId, string SupplyMethod, decimal? NetWeightKg, decimal DefaultPlanningScrapPercent,
        decimal SafetyStockQty, int CriticalDays, int SafeStartDays, int ExcessStartDays, string? Notes,
        long? ExpectedVersion, long? RequestedVersion = null);
    private sealed record ItemDto(Guid Id, string ItemType, string Code, string NameFa,
        RefDto Category, RefDto BaseUnit, decimal? NetWeightKg, decimal DefaultPlanningScrapPercent,
        decimal SafetyStockQty, bool IsActive, long Version);
    private sealed record RefDto(bool IsActive);
    private sealed record ProductCodeDto(Guid Id, string Code);
    private sealed record MaterialListDto(IReadOnlyList<ItemDto> Items);
    private sealed class ThrowingAuditSink : IAuditEventSink
    { public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken) => throw new InvalidOperationException("audit unavailable"); }
}
