using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Suppliers.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Hadron.Erp.IntegrationTests;

[Collection("ReferenceData")]
public sealed class SupplierTests : IClassFixture<FoundationApiFactory>, IAsyncLifetime
{
    private readonly FoundationApiFactory _factory;
    private readonly HttpClient _client;
    private ReferenceSeed _country = default!;
    private ReferenceSeed _term = default!;
    private PersonDto _owner = default!;

    public SupplierTests(FoundationApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await using var db = await CreateDbContext();
        await db.Database.MigrateAsync();
        await db.SupplierContacts.ExecuteDeleteAsync();
        await db.SupplierPaymentProfiles.ExecuteDeleteAsync();
        await db.Suppliers.ExecuteDeleteAsync();
        await db.InternalPeople.ExecuteDeleteAsync();
        await db.ReferenceValues.ExecuteDeleteAsync();
        _factory.AuditSink.Clear();
        _country = await Reference("COUNTRY", "IR", "ایران");
        _term = await Reference("PAYMENT_TERM", "NET60", "اعتبار ۶۰ روزه");
        _owner = await Person("مالک تأمین");
        _factory.AuditSink.Clear();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_is_inactive_sequence_numbered_and_country_is_required_active()
    {
        var response = await Create("تأمین‌کننده الف");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var supplier = json.RootElement.GetProperty("supplier");
        Assert.Matches("^SUP-[0-9]{4,}$", supplier.GetProperty("supplierNumber").GetString()!);
        Assert.False(supplier.GetProperty("isActive").GetBoolean());
        Assert.Equal(1, supplier.GetProperty("version").GetInt64());
        Assert.Contains(_factory.AuditSink.Snapshot(), x => x.EntityType == "Supplier" && x.Action == "CREATE");

        var invalid = Payload("بدون کشور") with { CountryId = Guid.Empty };
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/suppliers", invalid)).StatusCode);

        await DeactivateReference(_country);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create("کشور غیرفعال")).StatusCode);
    }

    [Fact]
    public async Task Inactive_allows_incomplete_profile_but_activation_requires_active_complete_references()
    {
        var supplier = await Created(await _client.PostAsJsonAsync(
            "/api/suppliers",
            Payload("ناقص") with
            {
                OwnerId = null,
                PaymentTermId = null,
                AdvancePercent = 20,
                BeforeShipmentPercent = 0,
                CreditPercent = 0
            }));
        Assert.Equal(HttpStatusCode.BadRequest, (await Reactivate(supplier)).StatusCode);

        var update = await _client.PutAsJsonAsync(
            $"/api/suppliers/{supplier.Id}",
            Payload("کامل") with
            {
                ExpectedVersion = supplier.Version,
                AdvancePercent = 40,
                BeforeShipmentPercent = 30,
                CreditPercent = 30,
                CreditDays = 60,
                CreditAnchor = "WAREHOUSE_RECEIPT"
            });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var active = await Reactivate(await Mutation(update));
        Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        Assert.True((await Detail(active)).IsActive);
    }

    [Fact]
    public async Task Active_supplier_edit_preserves_full_activation_validity_atomically()
    {
        var active = await Activate(await Created(await Create("فعال معتبر")));
        var successfulEditCount = SuccessfulSupplierAudits("EDIT");
        var invalidTotal = await _client.PutAsJsonAsync(
            $"/api/suppliers/{active.Id}",
            Payload("نباید ذخیره شود") with
            {
                ExpectedVersion = active.Version,
                AdvancePercent = 99.99m,
                BeforeShipmentPercent = 0,
                CreditPercent = 0
            });

        Assert.Equal(HttpStatusCode.BadRequest, invalidTotal.StatusCode);
        var unchanged = await GetSupplier(active.Id);
        Assert.Equal("فعال معتبر", unchanged.Name);
        Assert.Equal(active.Version, unchanged.Version);
        Assert.True(unchanged.IsActive);
        Assert.Equal(successfulEditCount, SuccessfulSupplierAudits("EDIT"));

        await DeactivateReference(_country);
        await DeactivateReference(_term);
        await DeactivatePerson(_owner);
        var invalidReferences = await _client.PutAsJsonAsync(
            $"/api/suppliers/{active.Id}",
            Payload("ویرایش نامرتبط") with { ExpectedVersion = active.Version });
        Assert.Equal(HttpStatusCode.BadRequest, invalidReferences.StatusCode);
        var errors = await Errors(invalidReferences);
        Assert.Contains("countryId", errors.Keys);
        Assert.Contains("ownerId", errors.Keys);
        Assert.Contains("paymentTermId", errors.Keys);
        unchanged = await GetSupplier(active.Id);
        Assert.Equal("فعال معتبر", unchanged.Name);
        Assert.Equal(active.Version, unchanged.Version);
        Assert.Equal(successfulEditCount, SuccessfulSupplierAudits("EDIT"));
    }

    [Fact]
    public async Task Inactive_historical_references_resolve_and_unchanged_references_survive_unrelated_edit()
    {
        var supplier = await Created(await Create("تاریخی"));
        await DeactivateReference(_country);
        await DeactivateReference(_term);
        await DeactivatePerson(_owner);

        var historical = await GetSupplier(supplier.Id);
        Assert.False(historical.Country.IsActive);
        Assert.False(historical.Owner!.IsActive);
        Assert.False(historical.PaymentProfile.PaymentTerm!.IsActive);

        var update = await _client.PutAsJsonAsync(
            $"/api/suppliers/{supplier.Id}",
            Payload("تاریخی ویرایش‌شده") with { ExpectedVersion = supplier.Version });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var changed = await Mutation(update);
        Assert.Equal("تاریخی ویرایش‌شده", changed.Name);
        Assert.Equal(supplier.Version + 1, changed.Version);
        Assert.False(changed.Country.IsActive);
        Assert.False(changed.Owner!.IsActive);
        Assert.False(changed.PaymentProfile.PaymentTerm!.IsActive);
    }

    [Fact]
    public async Task Payment_precision_boundaries_exact_total_and_zero_credit_are_enforced()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/suppliers", Payload("منفی") with { AdvancePercent = -0.01m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/suppliers", Payload("بیشینه") with { AdvancePercent = 100.01m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/suppliers", Payload("بدون روز") with { CreditPercent = 10, CreditDays = null, CreditAnchor = "ORDERED" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/suppliers", Payload("مبنای بد") with { CreditPercent = 10, CreditDays = 1, CreditAnchor = "FIRST_RECEIPT" })).StatusCode);

        var zero = await Created(await _client.PostAsJsonAsync(
            "/api/suppliers",
            Payload("صفر") with { CreditPercent = 0, CreditDays = 12, CreditAnchor = "ORDERED" }));
        Assert.Null(zero.PaymentProfile.CreditDays);
        Assert.Null(zero.PaymentProfile.CreditAnchor);

        var exact = await Created(await _client.PostAsJsonAsync(
            "/api/suppliers",
            Payload("دقیق") with { AdvancePercent = 33.33m, BeforeShipmentPercent = 33.33m, CreditPercent = 33.34m, CreditDays = 30, CreditAnchor = "ORDERED" }));
        Assert.Equal(HttpStatusCode.OK, (await Reactivate(exact)).StatusCode);
        var inexact = await Created(await _client.PostAsJsonAsync(
            "/api/suppliers",
            Payload("غیردقیق") with { AdvancePercent = 33.33m, BeforeShipmentPercent = 33.33m, CreditPercent = 33.33m, CreditDays = 30, CreditAnchor = "ORDERED" }));
        Assert.Equal(HttpStatusCode.BadRequest, (await Reactivate(inexact)).StatusCode);

        await using var db = await CreateDbContext();
        var entity = db.Model.FindEntityType("Hadron.Erp.Api.Modules.Suppliers.Domain.SupplierPaymentProfile")!;
        foreach (var propertyName in new[] { "AdvancePercent", "BeforeShipmentPercent", "CreditPercent" })
        {
            var property = entity.FindProperty(propertyName)!;
            Assert.Equal(5, property.GetPrecision());
            Assert.Equal(2, property.GetScale());
        }
    }

    [Fact]
    public async Task Duplicate_name_warns_only_and_concurrent_numbers_are_unique()
    {
        Assert.Equal(HttpStatusCode.Created, (await Create("نام مشترک")).StatusCode);
        var duplicate = await Create(" نام مشترک ");
        Assert.Equal(HttpStatusCode.Created, duplicate.StatusCode);
        using var json = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync());
        Assert.Contains("DUPLICATE_SUPPLIER_NAME", json.RootElement.GetProperty("warnings").EnumerateArray().Select(x => x.GetString()));
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Create($"همزمان {i}")));
        Assert.All(responses, x => Assert.Equal(HttpStatusCode.Created, x.StatusCode));
        var numbers = new List<string>();
        foreach (var response in responses)
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            numbers.Add(document.RootElement.GetProperty("supplier").GetProperty("supplierNumber").GetString()!);
        }

        Assert.Equal(6, numbers.Distinct().Count());
    }

    [Fact]
    public async Task Contact_rules_stale_mutations_and_lifecycle_are_enforced()
    {
        var supplier = await Created(await Create("مخاطب‌دار"));
        var first = await AddContact(supplier, "مخاطب اول", true);
        Assert.Equal(supplier.Version + 1, first.Version);
        Assert.Single(first.Contacts);
        Assert.True(first.Contacts[0].IsPrimary);

        var edit = await _client.PutAsJsonAsync(
            $"/api/suppliers/{supplier.Id}/contacts/{first.Contacts[0].Id}",
            ContactPayload("مخاطب ویرایش‌شده", false, first.Version));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var edited = await Detail(edit);
        Assert.Equal("مخاطب ویرایش‌شده", edited.Contacts.Single().FullName);

        var auditCount = SuccessfulSupplierAudits("CONTACT_EDIT");
        var staleEdit = await _client.PutAsJsonAsync(
            $"/api/suppliers/{supplier.Id}/contacts/{first.Contacts[0].Id}",
            ContactPayload("قدیمی", true, first.Version));
        Assert.Equal(HttpStatusCode.Conflict, staleEdit.StatusCode);
        Assert.Equal("STALE_VERSION", await Code(staleEdit));
        Assert.Equal(auditCount, SuccessfulSupplierAudits("CONTACT_EDIT"));

        var staleLifecycle = await _client.PostAsJsonAsync(
            $"/api/suppliers/{supplier.Id}/contacts/{first.Contacts[0].Id}/deactivate",
            new { expectedVersion = first.Version });
        Assert.Equal(HttpStatusCode.Conflict, staleLifecycle.StatusCode);
        Assert.Equal("STALE_VERSION", await Code(staleLifecycle));

        var deactivate = await _client.PostAsJsonAsync(
            $"/api/suppliers/{supplier.Id}/contacts/{first.Contacts[0].Id}/deactivate",
            new { expectedVersion = edited.Version });
        var final = await Detail(deactivate);
        Assert.False(final.Contacts[0].IsActive);
        Assert.False(final.Contacts[0].IsPrimary);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await _client.DeleteAsync($"/api/suppliers/{supplier.Id}/contacts/{first.Contacts[0].Id}")).StatusCode);
    }

    [Fact]
    public async Task Concurrent_set_primary_and_database_index_cannot_create_two_active_primaries()
    {
        var supplier = await Created(await Create("رقابت مخاطب"));
        var withFirst = await AddContact(supplier, "اول", false);
        var withSecond = await AddContact(withFirst, "دوم", false);
        var first = withSecond.Contacts.Single(x => x.FullName == "اول");
        var second = withSecond.Contacts.Single(x => x.FullName == "دوم");

        var attempts = await Task.WhenAll(
            _client.PutAsJsonAsync($"/api/suppliers/{supplier.Id}/contacts/{first.Id}", ContactPayload(first.FullName, true, withSecond.Version)),
            _client.PutAsJsonAsync($"/api/suppliers/{supplier.Id}/contacts/{second.Id}", ContactPayload(second.FullName, true, withSecond.Version)));
        Assert.Single(attempts, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(attempts, x => x.StatusCode == HttpStatusCode.Conflict);
        var current = await GetSupplier(supplier.Id);
        Assert.Single(current.Contacts, x => x.IsActive && x.IsPrimary);

        var nonPrimary = current.Contacts.Single(x => !x.IsPrimary);
        await using var db = await CreateDbContext();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE procurement.supplier_contacts SET \"IsPrimary\" = TRUE WHERE \"Id\" = {nonPrimary.Id}"));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Single((await GetSupplier(supplier.Id)).Contacts, x => x.IsActive && x.IsPrimary);
    }

    [Fact]
    public async Task Stale_supplier_and_person_mutations_return_409_without_lost_update_or_audit()
    {
        var supplier = await Created(await Create("نسخه"));
        var update = await _client.PutAsJsonAsync($"/api/suppliers/{supplier.Id}", Payload("نسخه جدید") with { ExpectedVersion = supplier.Version });
        var current = await Mutation(update);
        var audits = SuccessfulSupplierAudits("EDIT");
        var stale = await _client.PutAsJsonAsync($"/api/suppliers/{supplier.Id}", Payload("خراب") with { ExpectedVersion = supplier.Version });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("STALE_VERSION", await Code(stale));
        var detail = await GetSupplier(supplier.Id);
        Assert.Equal("نسخه جدید", detail.Name);
        Assert.Equal(current.Version, detail.Version);
        Assert.Equal(audits, SuccessfulSupplierAudits("EDIT"));

        var changed = await _client.PutAsJsonAsync($"/api/internal-people/{_owner.Id}", new { displayNameFa = "مالک جدید", displayNameEn = (string?)null, expectedVersion = _owner.Version });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var stalePerson = await _client.PutAsJsonAsync($"/api/internal-people/{_owner.Id}", new { displayNameFa = "از دست رفته", displayNameEn = (string?)null, expectedVersion = _owner.Version });
        Assert.Equal(HttpStatusCode.Conflict, stalePerson.StatusCode);
    }

    [Fact]
    public async Task Throwing_audit_sink_does_not_turn_committed_supplier_mutation_into_failure()
    {
        _factory.AuditSink.ThrowOnWrite = true;
        var response = await Create("ممیزی ناموفق");
        _factory.AuditSink.ThrowOnWrite = false;
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var supplier = await Created(response);
        var persisted = await GetSupplier(supplier.Id);
        Assert.Equal("ممیزی ناموفق", persisted.Name);
        Assert.Equal(supplier.Version, persisted.Version);
    }

    [Fact]
    public async Task Sourcing_boundary_is_absent_from_model_schema_and_api()
    {
        var forbidden = new[] { "Price", "Currency", "Moq", "LeadTime", "ShippingMode", "Incoterm", "Customs", "PreferredSupplier", "ReceiptDate", "ProductId", "ItemId" };
        var modelProperties = typeof(Supplier).GetProperties().Select(x => x.Name)
            .Concat(typeof(SupplierPaymentProfile).GetProperties().Select(x => x.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.All(forbidden, name => Assert.DoesNotContain(name, modelProperties));

        await using var db = await CreateDbContext();
        await db.Database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT column_name FROM information_schema.columns WHERE table_schema = 'procurement' AND table_name IN ('suppliers','supplier_payment_profiles')",
            (NpgsqlConnection)db.Database.GetDbConnection());
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync()) columns.Add(reader.GetString(0));
        Assert.All(forbidden, name => Assert.DoesNotContain(name, columns));

        var supplier = await Created(await Create("مرز API"));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await _client.DeleteAsync($"/api/suppliers/{supplier.Id}")).StatusCode);
    }

    private Task<HttpResponseMessage> Create(string name) => _client.PostAsJsonAsync("/api/suppliers", Payload(name));

    private SupplierRequest Payload(string name) => new(name, _country.Id, _owner.Id, _term.Id, 100, 0, 0, null, null, null, null, null, null);

    private static object ContactPayload(string fullName, bool isPrimary, long expectedVersion) => new
    {
        fullName,
        jobTitle = (string?)null,
        email = (string?)null,
        phone = (string?)null,
        mobile = (string?)null,
        isPrimary,
        expectedVersion
    };

    private async Task<SupplierDto> AddContact(SupplierDto supplier, string fullName, bool isPrimary)
    {
        var response = await _client.PostAsJsonAsync($"/api/suppliers/{supplier.Id}/contacts", ContactPayload(fullName, isPrimary, supplier.Version));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Detail(response);
    }

    private async Task<SupplierDto> Activate(SupplierDto supplier)
    {
        var response = await Reactivate(supplier);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Detail(response);
    }

    private Task<HttpResponseMessage> Reactivate(SupplierDto supplier) => _client.PostAsJsonAsync($"/api/suppliers/{supplier.Id}/reactivate", new { expectedVersion = supplier.Version });

    private async Task<ReferenceSeed> Reference(string type, string code, string name)
    {
        var response = await _client.PostAsJsonAsync($"/api/reference-data/types/{type}/values", new { code, nameFa = name, nameEn = code, sortOrder = 0 });
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new(json.RootElement.GetProperty("id").GetGuid(), json.RootElement.GetProperty("version").GetInt64());
    }

    private async Task<PersonDto> Person(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/internal-people", new { displayNameFa = name, displayNameEn = (string?)null });
        return (await response.Content.ReadFromJsonAsync<PersonDto>())!;
    }

    private async Task DeactivateReference(ReferenceSeed reference)
    {
        var response = await _client.PostAsJsonAsync($"/api/reference-data/values/{reference.Id}/deactivate", new { expectedVersion = reference.Version });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task DeactivatePerson(PersonDto person)
    {
        var response = await _client.PostAsJsonAsync($"/api/internal-people/{person.Id}/deactivate", new { expectedVersion = person.Version });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HadronDbContext> CreateDbContext()
    {
        var scope = _factory.Services.CreateAsyncScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HadronDbContext>>();
        var db = await contextFactory.CreateDbContextAsync();
        db.Database.SetCommandTimeout(TimeSpan.FromSeconds(15));
        return db;
    }

    private int SuccessfulSupplierAudits(string action) => _factory.AuditSink.Snapshot().Count(x => x.EntityType == "Supplier" && x.Action == action);
    private async Task<SupplierDto> GetSupplier(Guid id) => await _client.GetFromJsonAsync<SupplierDto>($"/api/suppliers/{id}") ?? throw new InvalidOperationException();

    private static async Task<SupplierDto> Created(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("supplier").Deserialize<SupplierDto>(JsonOptions)!;
    }

    private static async Task<SupplierDto> Mutation(HttpResponseMessage response) => await Created(response);

    private static async Task<SupplierDto> Detail(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SupplierDto>() ?? throw new InvalidOperationException();
    }

    private static async Task<string?> Code(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("code").GetString();
    }

    private static async Task<Dictionary<string, string[]>> Errors(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("errors").Deserialize<Dictionary<string, string[]>>(JsonOptions)!;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record SupplierRequest(string Name, Guid CountryId, Guid? OwnerId, Guid? PaymentTermId, decimal AdvancePercent, decimal BeforeShipmentPercent, decimal CreditPercent, int? CreditDays, string? CreditAnchor, int? ExpertLeadTimeScore, int? ExpertQualityScore, string? Notes, long? ExpectedVersion);
    private sealed record SupplierDto(Guid Id, string SupplierNumber, string Name, SupplierRefDto Country, PersonDto? Owner, PaymentProfileDto PaymentProfile, bool IsActive, long Version, List<ContactDto> Contacts);
    private sealed record SupplierRefDto(Guid Id, bool IsActive);
    private sealed record PaymentProfileDto(SupplierRefDto? PaymentTerm, int? CreditDays, string? CreditAnchor);
    private sealed record ContactDto(Guid Id, string FullName, bool IsPrimary, bool IsActive);
    private sealed record PersonDto(Guid Id, bool IsActive, long Version);
    private sealed record ReferenceSeed(Guid Id, long Version);
}
