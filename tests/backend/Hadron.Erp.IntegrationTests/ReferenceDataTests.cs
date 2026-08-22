using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hadron.Erp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hadron.Erp.IntegrationTests;

[Collection("ReferenceData")]
public sealed class ReferenceDataTests : IClassFixture<FoundationApiFactory>, IAsyncLifetime
{
    private readonly FoundationApiFactory _factory;
    private readonly HttpClient _client;

    public ReferenceDataTests(FoundationApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HadronDbContext>>();
        await using var context = await db.CreateDbContextAsync();
        await context.Database.MigrateAsync();
        TestDatabaseSafety.EnsureSafeDatabaseName(context.Database.GetDbConnection().Database);
        Assert.Equal(_factory.DatabaseName, context.Database.GetDbConnection().Database);
        await context.ReferenceValues.ExecuteDeleteAsync();
        await context.Currencies.ExecuteDeleteAsync();
        await context.Units.ExecuteDeleteAsync();
        _factory.AuditSink.Clear();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Seed_contains_only_the_approved_reference_type_definitions()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HadronDbContext>>();
        await using var context = await factory.CreateDbContextAsync();
        await context.Database.MigrateAsync();
        await context.Database.MigrateAsync();

        var types = await GetArray("/api/reference-data/types");

        Assert.Equal(8, types.GetArrayLength());
        Assert.Equal(8, await context.ReferenceTypes.CountAsync());
        Assert.Equal(8, await context.ReferenceTypes.Select(x => x.Code).Distinct().CountAsync());
        Assert.Equal(0, (await GetArray("/api/reference-data/types/COUNTRY/values")).GetArrayLength());
        Assert.Equal(0, (await GetArray("/api/reference-data/currencies")).GetArrayLength());
        Assert.Equal(0, (await GetArray("/api/reference-data/units")).GetArrayLength());
        Assert.DoesNotContain(types.EnumerateArray(), x => x.GetProperty("code").GetString() is "CURRENCY" or "UNIT");
    }

    [Fact]
    public async Task Reference_value_enforces_code_but_allows_duplicate_localized_names()
    {
        var first = await CreateValue("IR", "ایران", "Iran");
        var duplicateName = await CreateValue("AF", "ایران", "Iran");
        Assert.Equal(HttpStatusCode.Created, duplicateName.StatusCode);

        using var duplicateCode = await PostJson("/api/reference-data/types/COUNTRY/values",
            new { code = "ir", nameFa = "نام دیگر", nameEn = "Different", sortOrder = 2 });
        Assert.Equal(HttpStatusCode.Conflict, duplicateCode.StatusCode);
        Assert.Equal("DUPLICATE_CODE", await ProblemCode(duplicateCode));

        var original = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("IR", original.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Ordinary_edit_keeps_code_and_advances_version()
    {
        using var created = await CreateValue("FAST", "سریع", "Fast");
        var value = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = value.GetProperty("id").GetGuid();

        using var updated = await PutJson($"/api/reference-data/values/{id}", new
        {
            code = "RENAMED",
            nameFa = "سریع‌تر",
            nameEn = "Faster",
            sortOrder = 3,
            expectedVersion = value.GetProperty("version").GetInt64()
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var result = await updated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("FAST", result.GetProperty("code").GetString());
        Assert.Equal(2, result.GetProperty("version").GetInt64());
    }

    [Fact]
    public async Task Stale_update_deactivate_and_reactivate_return_structured_conflict()
    {
        using var created = await CreateValue("SEA", "دریایی", "Sea");
        var value = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = value.GetProperty("id").GetGuid();
        var version = value.GetProperty("version").GetInt64();

        using var updated = await PutJson($"/api/reference-data/values/{id}",
            new { nameFa = "حمل دریایی", nameEn = "Sea Freight", sortOrder = 1, expectedVersion = version });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        using var staleUpdate = await PutJson($"/api/reference-data/values/{id}",
            new { nameFa = "قدیمی", nameEn = "Stale", sortOrder = 1, expectedVersion = version });
        await AssertStale(staleUpdate);
        using var staleDeactivate = await PostJson($"/api/reference-data/values/{id}/deactivate", new { expectedVersion = version });
        await AssertStale(staleDeactivate);

        var current = await updated.Content.ReadFromJsonAsync<JsonElement>();
        using var deactivated = await PostJson($"/api/reference-data/values/{id}/deactivate",
            new { expectedVersion = current.GetProperty("version").GetInt64() });
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        var inactive = await deactivated.Content.ReadFromJsonAsync<JsonElement>();

        using var staleReactivate = await PostJson($"/api/reference-data/values/{id}/reactivate",
            new { expectedVersion = current.GetProperty("version").GetInt64() });
        await AssertStale(staleReactivate);
        using var reactivated = await PostJson($"/api/reference-data/values/{id}/reactivate",
            new { expectedVersion = inactive.GetProperty("version").GetInt64() });
        Assert.Equal(HttpStatusCode.OK, reactivated.StatusCode);
        Assert.True((await reactivated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Inactive_values_are_excluded_from_selection_but_resolve_by_id()
    {
        using var created = await CreateValue("CASH", "نقدی", "Cash");
        var value = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = value.GetProperty("id").GetGuid();
        using var deactivated = await PostJson($"/api/reference-data/values/{id}/deactivate",
            new { expectedVersion = value.GetProperty("version").GetInt64() });
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);

        Assert.Equal(0, (await GetArray("/api/reference-data/types/COUNTRY/values")).GetArrayLength());
        Assert.Equal(1, (await GetArray("/api/reference-data/types/COUNTRY/values?includeInactive=true")).GetArrayLength());
        using var historical = await _client.GetAsync($"/api/reference-data/values/{id}");
        Assert.Equal(HttpStatusCode.OK, historical.StatusCode);
        Assert.False((await historical.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Currency_validates_precision_and_iso_code_uniqueness()
    {
        using var invalid = await PostJson("/api/reference-data/currencies",
            new { isoCode = "USD", nameFa = "دلار", nameEn = "Dollar", symbol = "$", decimalPrecision = 10 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var created = await PostJson("/api/reference-data/currencies",
            new { isoCode = "USD", nameFa = "دلار", nameEn = "Dollar", symbol = "$", decimalPrecision = 2 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(1, (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt64());

        using var duplicate = await PostJson("/api/reference-data/currencies",
            new { isoCode = "usd", nameFa = "دلار آمریکا", nameEn = "US Dollar", symbol = "$", decimalPrecision = 2 });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Unit_code_is_unique_and_unit_has_no_decimal_precision()
    {
        using var created = await PostJson("/api/reference-data/units",
            new { code = "PCS", nameFa = "عدد", nameEn = "Piece", symbol = (string?)null, sortOrder = 1 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.TryGetProperty("decimalPrecision", out _));

        using var duplicate = await PostJson("/api/reference-data/units",
            new { code = "pcs", nameFa = "قطعه", nameEn = "Piece duplicate", symbol = (string?)null, sortOrder = 2 });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Currency_supports_lifecycle_concurrency_and_historical_resolution()
    {
        using var created = await CreateCurrency("USD", "دلار", "Dollar");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var value = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = value.GetProperty("id").GetGuid();
        var version = value.GetProperty("version").GetInt64();

        using var edited = await PutJson($"/api/reference-data/currencies/{id}",
            new { nameFa = "دلار آمریکا", nameEn = "US Dollar", symbol = "$", decimalPrecision = 2, expectedVersion = version });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var current = await edited.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(version + 1, current.GetProperty("version").GetInt64());

        using var staleEdit = await PutJson($"/api/reference-data/currencies/{id}",
            new { nameFa = "قدیمی", nameEn = "Stale", symbol = "$", decimalPrecision = 2, expectedVersion = version });
        await AssertStale(staleEdit);
        using var staleDeactivate = await PostJson($"/api/reference-data/currencies/{id}/deactivate", new { expectedVersion = version });
        await AssertStale(staleDeactivate);

        using var deactivated = await PostJson($"/api/reference-data/currencies/{id}/deactivate",
            new { expectedVersion = current.GetProperty("version").GetInt64() });
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        var inactive = await deactivated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(inactive.GetProperty("isActive").GetBoolean());
        Assert.Equal(0, (await GetArray("/api/reference-data/currencies")).GetArrayLength());
        Assert.Equal(1, (await GetArray("/api/reference-data/currencies?includeInactive=true")).GetArrayLength());
        using var historical = await _client.GetAsync($"/api/reference-data/currencies/{id}");
        Assert.Equal(HttpStatusCode.OK, historical.StatusCode);

        using var staleReactivate = await PostJson($"/api/reference-data/currencies/{id}/reactivate",
            new { expectedVersion = current.GetProperty("version").GetInt64() });
        await AssertStale(staleReactivate);
        using var reactivated = await PostJson($"/api/reference-data/currencies/{id}/reactivate",
            new { expectedVersion = inactive.GetProperty("version").GetInt64() });
        Assert.Equal(HttpStatusCode.OK, reactivated.StatusCode);
        Assert.Equal(inactive.GetProperty("version").GetInt64() + 1,
            (await reactivated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt64());

        var auditEvents = _factory.AuditSink.Snapshot().Where(x => x.EntityType == "Currency").ToArray();
        Assert.Equal(["CREATE", "EDIT", "DEACTIVATE", "REACTIVATE"], auditEvents.Select(x => x.Action));
        Assert.All(auditEvents, x => Assert.False(string.IsNullOrWhiteSpace(x.CorrelationId)));
    }

    [Fact]
    public async Task Unit_supports_lifecycle_concurrency_and_historical_resolution()
    {
        using var created = await CreateUnit("PCS", "عدد", "Piece");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var value = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = value.GetProperty("id").GetGuid();
        var version = value.GetProperty("version").GetInt64();

        using var edited = await PutJson($"/api/reference-data/units/{id}",
            new { nameFa = "قطعه", nameEn = "Piece", symbol = "pc", sortOrder = 2, expectedVersion = version });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var current = await edited.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(version + 1, current.GetProperty("version").GetInt64());

        using var staleEdit = await PutJson($"/api/reference-data/units/{id}",
            new { nameFa = "قدیمی", nameEn = "Stale", symbol = "pc", sortOrder = 2, expectedVersion = version });
        await AssertStale(staleEdit);
        using var staleDeactivate = await PostJson($"/api/reference-data/units/{id}/deactivate", new { expectedVersion = version });
        await AssertStale(staleDeactivate);

        using var deactivated = await PostJson($"/api/reference-data/units/{id}/deactivate",
            new { expectedVersion = current.GetProperty("version").GetInt64() });
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        var inactive = await deactivated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(inactive.GetProperty("isActive").GetBoolean());
        Assert.Equal(0, (await GetArray("/api/reference-data/units")).GetArrayLength());
        Assert.Equal(1, (await GetArray("/api/reference-data/units?includeInactive=true")).GetArrayLength());
        using var historical = await _client.GetAsync($"/api/reference-data/units/{id}");
        Assert.Equal(HttpStatusCode.OK, historical.StatusCode);

        using var staleReactivate = await PostJson($"/api/reference-data/units/{id}/reactivate",
            new { expectedVersion = current.GetProperty("version").GetInt64() });
        await AssertStale(staleReactivate);
        using var reactivated = await PostJson($"/api/reference-data/units/{id}/reactivate",
            new { expectedVersion = inactive.GetProperty("version").GetInt64() });
        Assert.Equal(HttpStatusCode.OK, reactivated.StatusCode);
        Assert.Equal(inactive.GetProperty("version").GetInt64() + 1,
            (await reactivated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt64());

        var auditEvents = _factory.AuditSink.Snapshot().Where(x => x.EntityType == "Unit").ToArray();
        Assert.Equal(["CREATE", "EDIT", "DEACTIVATE", "REACTIVATE"], auditEvents.Select(x => x.Action));
        Assert.All(auditEvents, x => Assert.False(string.IsNullOrWhiteSpace(x.CorrelationId)));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("@@@")]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("U$D")]
    public async Task Currency_rejects_invalid_iso_alpha3_codes(string code)
    {
        using var response = await CreateCurrency(code, "ارز", "Currency");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("", "نام", "Name")]
    [InlineData("CODE", "", "Name")]
    [InlineData("CODE", "نام", "")]
    public async Task Required_code_and_labels_are_validated(string code, string nameFa, string nameEn)
    {
        using var referenceValue = await CreateValue(code, nameFa, nameEn);
        Assert.Equal(HttpStatusCode.BadRequest, referenceValue.StatusCode);
        using var unit = await CreateUnit(code, nameFa, nameEn);
        Assert.Equal(HttpStatusCode.BadRequest, unit.StatusCode);
    }

    [Theory]
    [InlineData("", "نام", "Name")]
    [InlineData("USD", "", "Name")]
    [InlineData("USD", "نام", "")]
    public async Task Currency_requires_iso_code_and_both_labels(string code, string nameFa, string nameEn)
    {
        using var response = await CreateCurrency(code, nameFa, nameEn);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Dedicated_entities_allow_duplicate_localized_names_with_distinct_codes()
    {
        using var firstCurrency = await CreateCurrency("USD", "ارز مشترک", "Shared name");
        using var secondCurrency = await CreateCurrency("EUR", "ارز مشترک", "Shared name");
        Assert.Equal(HttpStatusCode.Created, firstCurrency.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondCurrency.StatusCode);

        using var firstUnit = await CreateUnit("PCS", "واحد مشترک", "Shared unit");
        using var secondUnit = await CreateUnit("BOX", "واحد مشترک", "Shared unit");
        Assert.Equal(HttpStatusCode.Created, firstUnit.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondUnit.StatusCode);
    }

    [Fact]
    public async Task Reference_value_mutations_emit_audit_integration_events()
    {
        using var created = await CreateValue("AUDIT", "ممیزی", "Audit");
        var value = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = value.GetProperty("id").GetGuid();
        using var edited = await PutJson($"/api/reference-data/values/{id}",
            new { nameFa = "ممیزی جدید", nameEn = "Updated audit", sortOrder = 2, expectedVersion = 1 });
        var editedValue = await edited.Content.ReadFromJsonAsync<JsonElement>();
        using var deactivated = await PostJson($"/api/reference-data/values/{id}/deactivate",
            new { expectedVersion = editedValue.GetProperty("version").GetInt64() });
        var inactive = await deactivated.Content.ReadFromJsonAsync<JsonElement>();
        using var reactivated = await PostJson($"/api/reference-data/values/{id}/reactivate",
            new { expectedVersion = inactive.GetProperty("version").GetInt64() });

        var events = _factory.AuditSink.Snapshot().Where(x => x.EntityType == "ReferenceValue").ToArray();
        Assert.Equal(["CREATE", "EDIT", "DEACTIVATE", "REACTIVATE"], events.Select(x => x.Action));
        Assert.Null(events[0].OldValues);
        Assert.NotNull(events[0].NewValues);
        Assert.NotNull(events[1].OldValues);
        Assert.NotNull(events[1].NewValues);
    }

    [Fact]
    public async Task Delete_is_not_exposed()
    {
        using var created = await CreateValue("AIR", "هوایی", "Air");
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var response = await _client.DeleteAsync($"/api/reference-data/values/{id}");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);

        using var currency = await CreateCurrency("EUR", "یورو", "Euro");
        var currencyId = (await currency.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var currencyDelete = await _client.DeleteAsync($"/api/reference-data/currencies/{currencyId}");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, currencyDelete.StatusCode);

        using var unit = await CreateUnit("KG", "کیلوگرم", "Kilogram");
        var unitId = (await unit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var unitDelete = await _client.DeleteAsync($"/api/reference-data/units/{unitId}");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, unitDelete.StatusCode);
    }

    [Fact]
    public void Destructive_database_guard_rejects_development_and_unknown_names()
    {
        TestDatabaseSafety.EnsureSafeDatabaseName(_factory.DatabaseName);
        Assert.Throws<InvalidOperationException>(() => TestDatabaseSafety.EnsureSafeDatabaseName("hadron_erp"));
        Assert.Throws<InvalidOperationException>(() => TestDatabaseSafety.EnsureSafeDatabaseName("production"));
        Assert.Throws<InvalidOperationException>(() => TestDatabaseSafety.EnsureSafeDatabaseName("some_database"));
    }

    private Task<HttpResponseMessage> CreateValue(string code, string nameFa, string nameEn) =>
        PostJson("/api/reference-data/types/COUNTRY/values", new { code, nameFa, nameEn, sortOrder = 1 });

    private Task<HttpResponseMessage> CreateCurrency(string code, string nameFa, string nameEn) =>
        PostJson("/api/reference-data/currencies",
            new { isoCode = code, nameFa, nameEn, symbol = (string?)null, decimalPrecision = 2 });

    private Task<HttpResponseMessage> CreateUnit(string code, string nameFa, string nameEn) =>
        PostJson("/api/reference-data/units",
            new { code, nameFa, nameEn, symbol = (string?)null, sortOrder = 1 });

    private Task<HttpResponseMessage> PostJson<T>(string path, T value) => _client.PostAsJsonAsync(path, value);
    private Task<HttpResponseMessage> PutJson<T>(string path, T value) => _client.PutAsJsonAsync(path, value);

    private async Task<JsonElement> GetArray(string path)
    {
        using var response = await _client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string?> ProblemCode(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private static async Task AssertStale(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("STALE_VERSION", await ProblemCode(response));
    }
}

[CollectionDefinition("ReferenceData", DisableParallelization = true)]
public sealed class ReferenceDataCollection;
