using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Hadron.Erp.Api.Modules.Trading.Domain;
using Hadron.Erp.Api.Modules.Identity.Domain;
using Hadron.Erp.Api.Platform.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Hadron.Erp.IntegrationTests;

public sealed class TradingTests : IClassFixture<FoundationApiFactory>, IAsyncLifetime
{
    private readonly FoundationApiFactory _factory;
    private readonly HttpClient _client;
    private Guid _currencyId;

    public TradingTests(FoundationApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HadronDbContext>>();
        await using var db = await contextFactory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
        await db.TradeCosts.ExecuteDeleteAsync();
        await db.TradeCustomsClearances.ExecuteDeleteAsync();
        await db.TradeFxAllocations.ExecuteDeleteAsync();
        await db.TradeShipmentLegs.ExecuteDeleteAsync();
        await db.TradeProjectShipments.ExecuteDeleteAsync();
        await db.TradeOrderRegistrations.ExecuteDeleteAsync();
        await db.TradeReportNotes.ExecuteDeleteAsync();
        await db.TradeShipments.ExecuteDeleteAsync();
        await db.TradeProjects.ExecuteDeleteAsync();
        await db.TradeReferenceValues.ExecuteDeleteAsync();
        var currency = await db.Currencies.SingleOrDefaultAsync(x => x.NormalizedCode == "USD")
            ?? new Currency("USD", "USD", "دلار آمریکا", "US Dollar", "$", 2);
        if (db.Entry(currency).State == EntityState.Detached) db.Currencies.Add(currency);
        if (db.Entry(currency).State != EntityState.Added)
        {
            currency.SetNames("دلار آمریکا", "US Dollar"); currency.SetActive(true); currency.Touch();
        }
        await db.SaveChangesAsync();
        _currencyId = currency.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task First_step_saves_only_initial_facts_with_real_references_and_audit()
    {
        var owner = await SetupUser();
        var customer = await Created("/api/trading/references", new { referenceType = "CUSTOMER", nameFa = "مشتری مرحله اول", nameEn = (string?)null, sortOrder = 0 });
        var card = await Created("/api/trading/references", new { referenceType = "BUSINESS_CARD", nameFa = "کارت مرحله اول", nameEn = (string?)null, sortOrder = 0 });
        var result = await Created("/api/trading/project-setup", new
        {
            titleFa = "  موضوع مرحله اول  ", startDate = "2026-10-18", responsibleUserId = owner.Id,
            customerId = customer.GetProperty("id").GetGuid(), businessCardId = card.GetProperty("id").GetGuid(),
            notes = "توضیحات مرحله اول", status = "WAITING_ORDER", priority = "CRITICAL"
        });
        var project = result.GetProperty("project");
        var id = project.GetProperty("id").GetGuid();
        Assert.Equal("موضوع مرحله اول", project.GetProperty("titleFa").GetString());
        Assert.Equal("2026-10-18", project.GetProperty("startDate").GetString());
        Assert.Equal(owner.Id, project.GetProperty("responsibleUserId").GetGuid());
        Assert.Equal(owner.FullName, project.GetProperty("responsibleUserName").GetString());
        Assert.Equal("کارت مرحله اول", project.GetProperty("businessCardNameFa").GetString());
        Assert.Equal("DRAFT", project.GetProperty("status").GetString());
        Assert.Equal("NORMAL", project.GetProperty("priority").GetString());
        Assert.Matches("^TRD-[0-9]{6}$", project.GetProperty("projectNumber").GetString()!);
        Assert.Empty(result.GetProperty("orderRegistrations").EnumerateArray());
        Assert.Empty(result.GetProperty("fxAllocations").EnumerateArray());
        Assert.Empty(result.GetProperty("shipments").EnumerateArray());
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HadronDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var stored = await db.TradeProjects.SingleAsync(x => x.Id == id);
        Assert.Equal(new DateOnly(2026, 10, 18), stored.StartDate);
        Assert.Equal("توضیحات مرحله اول", stored.Notes);
        Assert.NotEqual(default, stored.CreatedAtUtc);
        var audit = await db.TradeAuditEvents.SingleAsync(x => x.EntityId == id && x.Action == "CREATE");
        Assert.Contains("responsibleUserId", audit.CurrentJson!);
        var cardEntity = await db.TradeReferenceValues.SingleAsync(x => x.Id == stored.BusinessCardId);
        cardEntity.Edit("نام جدید کارت", null, 0);
        await db.SaveChangesAsync();
        var reloaded = await _client.GetFromJsonAsync<JsonElement>($"/api/trading/projects/{id}");
        Assert.Equal("کارت مرحله اول", reloaded.GetProperty("project").GetProperty("businessCardNameFa").GetString());
    }

    [Fact]
    public async Task First_step_rejects_missing_dates_inactive_assignees_and_wrong_reference_roles()
    {
        var owner = await SetupUser();
        var inactive = await SetupUser(false);
        var customer = await Created("/api/trading/references", new { referenceType = "CUSTOMER", nameFa = "مشتری نه کارت", nameEn = (string?)null, sortOrder = 0 });
        foreach (var body in new object[]
        {
            new { titleFa = "موضوع", responsibleUserId = owner.Id },
            new { titleFa = "موضوع", startDate = "2026-10-18", responsibleUserId = Guid.Empty },
            new { titleFa = "موضوع", startDate = "2026-10-18", responsibleUserId = inactive.Id },
            new { titleFa = "موضوع", startDate = "2026-10-18", responsibleUserId = owner.Id, businessCardId = customer.GetProperty("id").GetGuid() },
            new { titleFa = "موضوع", startDate = "2026-10-18", responsibleUserId = owner.Id, notes = new string('x', 4001) }
        })
        {
            using var response = await _client.PostAsJsonAsync("/api/trading/project-setup", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HadronDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        Assert.Equal(0, await db.TradeProjects.CountAsync());
    }

    [Fact]
    public async Task First_step_lookups_are_paged_filtered_and_do_not_expose_admin_profile_fields()
    {
        var owner = await SetupUser();
        var inactive = await SetupUser(false);
        for (var i = 0; i < 26; i++)
            await Created("/api/trading/references", new { referenceType = "BUSINESS_CARD", nameFa = $"کارت صفحه {i:D2}", nameEn = (string?)null, sortOrder = i });
        var first = await _client.GetFromJsonAsync<JsonElement>("/api/trading/project-setup/lookups?kind=BUSINESS_CARD&page=1&pageSize=25");
        var second = await _client.GetFromJsonAsync<JsonElement>("/api/trading/project-setup/lookups?kind=BUSINESS_CARD&page=2&pageSize=25");
        Assert.Equal(26, first.GetProperty("totalCount").GetInt32());
        Assert.Equal(25, first.GetProperty("items").GetArrayLength());
        Assert.Equal(1, second.GetProperty("items").GetArrayLength());
        var users = await _client.GetFromJsonAsync<JsonElement>("/api/trading/project-setup/lookups?kind=ASSIGNEE&page=1&pageSize=100");
        Assert.Contains(users.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == owner.Id);
        Assert.DoesNotContain(users.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == inactive.Id);
        Assert.DoesNotContain("email", users.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", users.GetRawText(), StringComparison.OrdinalIgnoreCase);
        using var invalid = await _client.GetAsync("/api/trading/project-setup/lookups?kind=BANK");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task First_step_requires_manage_permission_for_both_lookup_and_create()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Hadron-Test-Permissions", Permissions.TradingProjectView);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/trading/project-setup/lookups?kind=ASSIGNEE")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/trading/project-setup", new { titleFa = "موضوع" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/trading/project-setup/resolve-jalali-date", new { jalaliDate = "14050726" })).StatusCode);
    }

    [Fact]
    public async Task First_step_jalali_conversion_uses_shared_calendar_without_granting_mrp_permissions()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Hadron-Test-Permissions", Permissions.TradingProjectManage);
        using var response = await client.PostAsJsonAsync("/api/trading/project-setup/resolve-jalali-date", new { jalaliDate = "۱۴۰۵۰۷۲۶" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resolved = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("2026-10-18", resolved.GetProperty("gregorianDate").GetString());
        using var invalid = await client.PostAsJsonAsync("/api/trading/project-setup/resolve-jalali-date", new { jalaliDate = "14051301" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var denied = await client.PostAsJsonAsync("/api/planning/resolve-jalali-date", new { jalaliDate = "14050726" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    private async Task<ApplicationUser> SetupUser(bool active = true)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HadronDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var unit = new OrganizationalUnit("واحد تست بازرگانی", null);
        var token = Guid.NewGuid().ToString("N");
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = token, NormalizedUserName = token.ToUpperInvariant(),
            Email = token + "@test.invalid", NormalizedEmail = token.ToUpperInvariant() + "@TEST.INVALID", PhoneNumber = "09000000000",
            FirstName = "کارشناس", LastName = "تست " + token, OrganizationalUnitId = unit.Id,
            IsActive = active, ProfilePhotoObjectKey = "test/photo", ProfilePhotoContentType = "image/png", ProfilePhotoSize = 1
        };
        db.OrganizationalUnits.Add(unit); db.Users.Add(user); await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Project_object_preserves_complete_operational_lineage_and_snapshots()
    {
        var customer = await Created("/api/trading/references", new
        {
            referenceType = "CUSTOMER",
            nameFa = "مشتری تست بازرگانی",
            nameEn = "Trading test customer",
            sortOrder = 10
        });
        Assert.Matches("^CUS-[0-9]{6}$", customer.GetProperty("code").GetString()!);

        var project = await Created("/api/trading/projects", new
        {
            titleFa = "پرونده تست واردات",
            titleEn = "Import test file",
            customerId = customer.GetProperty("id").GetGuid(),
            supplierId = (Guid?)null,
            priority = "HIGH",
            targetDate = "2026-12-31",
            notes = "isolated trading fixture"
        });
        var projectId = project.GetProperty("project").GetProperty("id").GetGuid();
        Assert.Matches("^TRD-[0-9]{6}$", project.GetProperty("project").GetProperty("projectNumber").GetString()!);

        var order = await Created("/api/trading/order-registrations", new
        {
            projectId,
            registrationNumber = "REG-TEST-001",
            status = "DRAFT",
            registrationDate = "2026-09-01",
            registeredAmount = 125000.75m,
            currencyId = _currencyId,
            notes = "canonical order"
        });
        var orderId = order.GetProperty("id").GetGuid();

        await Created("/api/trading/fx-allocations", new
        {
            projectId,
            orderRegistrationId = orderId,
            processType = "REQUEST",
            dealStatus = "NOT_STARTED",
            receiptStatus = "NOT_RECEIVED",
            amount = 75000.25m,
            currencyId = _currencyId,
            bankId = (Guid?)null,
            requestDate = "2026-09-02",
            allocationDate = (string?)null,
            referenceNumber = "FX-001",
            notes = "partial allocation"
        });

        var shipmentLink = await Created("/api/trading/shipments", new
        {
            projectId,
            status = "DRAFT",
            transportMode = "SEA",
            forwarderId = (Guid?)null,
            origin = "Shanghai",
            destination = "Bandar Abbas",
            estimatedDeparture = "2026-09-10",
            estimatedArrival = "2026-10-05",
            grossWeight = 18250.5m,
            volumeCbm = 32.75m,
            allocationMethod = "EQUAL",
            allocationValue = 1m,
            notes = "one shipment"
        });
        var shipmentId = shipmentLink.GetProperty("shipment").GetProperty("id").GetGuid();
        Assert.Matches("^SHP-[0-9]{6}$", shipmentLink.GetProperty("shipment").GetProperty("shipmentNumber").GetString()!);

        await Created($"/api/trading/shipments/{shipmentId}/legs", new
        {
            sequence = 1,
            fromLocation = "Shanghai",
            toLocation = "Jebel Ali",
            transportMode = "SEA",
            status = "PLANNED",
            estimatedDeparture = "2026-09-10",
            estimatedArrival = "2026-09-25",
            trackingNumber = "TRK-001",
            notes = "first leg"
        });
        await Created("/api/trading/customs-clearances", new
        {
            shipmentId,
            clearanceNumber = "CLR-TEST-001",
            clearanceType = "DEFINITIVE",
            status = "NOT_STARTED",
            customsOfficeId = (Guid?)null,
            declarationDate = "2026-10-06",
            releaseDate = (string?)null,
            notes = "customs fixture"
        });
        await Created("/api/trading/costs", new
        {
            projectId,
            scopeType = "SHIPMENT",
            scopeId = shipmentId,
            costType = "FREIGHT",
            amount = 2500.125m,
            currencyId = _currencyId,
            effectiveDate = "2026-09-10",
            referenceNumber = "COST-001",
            notes = "freight"
        });

        var detail = await Get($"/api/trading/projects/{projectId}");
        Assert.Single(detail.GetProperty("orderRegistrations").EnumerateArray());
        Assert.Single(detail.GetProperty("fxAllocations").EnumerateArray());
        Assert.Single(detail.GetProperty("shipments").EnumerateArray());
        Assert.Single(detail.GetProperty("shipments")[0].GetProperty("shipment").GetProperty("legs").EnumerateArray());
        Assert.Single(detail.GetProperty("shipments")[0].GetProperty("customs").EnumerateArray());
        Assert.Single(detail.GetProperty("costs").EnumerateArray());
        Assert.True(detail.GetProperty("audit").GetArrayLength() >= 5);
        Assert.Equal(125000.75m, detail.GetProperty("orderRegistrations")[0].GetProperty("registeredAmount").GetDecimal());
        Assert.Equal("USD", detail.GetProperty("costs")[0].GetProperty("currencyCode").GetString());

        var lookups = await Get("/api/trading/lookups?includeInactive=true");
        Assert.Equal(10, lookups.GetProperty("referenceTypes").GetArrayLength());
        Assert.Contains("CUSTOMS_OFFICE", lookups.GetProperty("referenceTypes").EnumerateArray().Select(x => x.GetString()));
        var shipments = await Get("/api/trading/shipments?page=1&pageSize=25");
        Assert.Equal(projectId, shipments.GetProperty("items")[0].GetProperty("projects")[0].GetProperty("id").GetGuid());
        var customs = await Get("/api/trading/customs-clearances?page=1&pageSize=25");
        Assert.Equal("CLR-TEST-001", customs.GetProperty("items")[0].GetProperty("customs").GetProperty("clearanceNumber").GetString());
        var costs = await Get("/api/trading/costs?page=1&pageSize=25");
        Assert.Equal(2500.125m, costs.GetProperty("items")[0].GetProperty("cost").GetProperty("amount").GetDecimal());
        var report = await Get("/api/trading/reports?page=1&pageSize=25");
        Assert.Equal(1, report.GetProperty("items")[0].GetProperty("shipmentCount").GetInt32());
        Assert.Equal(2500.125m, report.GetProperty("items")[0].GetProperty("costTotals")[0].GetProperty("amount").GetDecimal());

        await using var scope = _factory.Services.CreateAsyncScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HadronDbContext>>();
        await using var db = await contextFactory.CreateDbContextAsync();
        var currency = await db.Currencies.SingleAsync(x => x.Id == _currencyId);
        currency.SetNames("نام تغییر یافته", "Changed currency");
        currency.Touch();
        await db.SaveChangesAsync();

        var historical = await Get($"/api/trading/projects/{projectId}");
        Assert.Equal("دلار آمریکا", historical.GetProperty("orderRegistrations")[0].GetProperty("currencyNameFa").GetString());
        Assert.Equal("دلار آمریکا", historical.GetProperty("costs")[0].GetProperty("currencyNameFa").GetString());
    }

    [Fact]
    public async Task Validation_rejects_invalid_chronology_and_cross_project_cost_scope()
    {
        var first = await CreateProject("پرونده اول");
        var second = await CreateProject("پرونده دوم");
        using var invalidShipment = await _client.PostAsJsonAsync("/api/trading/shipments", new
        {
            projectId = first,
            status = "DRAFT",
            transportMode = "AIR",
            forwarderId = (Guid?)null,
            origin = "A",
            destination = "B",
            estimatedDeparture = "2026-10-10",
            estimatedArrival = "2026-10-01",
            grossWeight = (decimal?)null,
            volumeCbm = (decimal?)null,
            allocationMethod = "EQUAL",
            allocationValue = 1m,
            notes = (string?)null
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidShipment.StatusCode);

        var goodShipment = await Created("/api/trading/shipments", new
        {
            projectId = first,
            status = "DRAFT",
            transportMode = "AIR",
            forwarderId = (Guid?)null,
            origin = "A",
            destination = "B",
            estimatedDeparture = "2026-10-01",
            estimatedArrival = "2026-10-10",
            grossWeight = (decimal?)null,
            volumeCbm = (decimal?)null,
            allocationMethod = "EQUAL",
            allocationValue = 1m,
            notes = (string?)null
        });
        var shipmentId = goodShipment.GetProperty("shipment").GetProperty("id").GetGuid();
        using var response = await _client.PostAsJsonAsync("/api/trading/costs", new
        {
            projectId = second,
            scopeType = "SHIPMENT",
            scopeId = shipmentId,
            costType = "FREIGHT",
            amount = 1m,
            currencyId = _currencyId,
            effectiveDate = "2026-10-01",
            referenceNumber = (string?)null,
            notes = (string?)null
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Project_worklist_is_server_paged_and_records_beyond_first_page_are_reachable()
    {
        for (var index = 1; index <= 27; index++) await CreateProject($"پرونده صفحه‌بندی {index:D2}");
        var first = await Get("/api/trading/projects?page=1&pageSize=25");
        var second = await Get("/api/trading/projects?page=2&pageSize=25");
        Assert.Equal(27, first.GetProperty("totalCount").GetInt32());
        Assert.Equal(25, first.GetProperty("items").GetArrayLength());
        Assert.Equal(2, second.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Lookups_are_bounded_references_beyond_100_are_reachable_and_worklists_sort_on_server()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HadronDbContext>>();
            await using var db = await contextFactory.CreateDbContextAsync();
            for (var index = 1; index <= 105; index++)
                db.TradeReferenceValues.Add(new TradeReferenceValue("CUSTOMER", $"CUS-T{index:D4}",
                    $"مشتری {index:D3}", $"Customer {index:D3}", index));
            await db.SaveChangesAsync();
        }

        var lookup = await Get("/api/trading/lookups?includeInactive=true&limit=10");
        Assert.Equal(10, lookup.GetProperty("references").GetArrayLength());
        var pageFive = await Get("/api/trading/references?includeInactive=true&page=5&pageSize=25");
        Assert.Equal(105, pageFive.GetProperty("totalCount").GetInt32());
        Assert.Equal(5, pageFive.GetProperty("items").GetArrayLength());

        await CreateProject("الف"); await CreateProject("ی");
        var ascending = await Get("/api/trading/projects?page=1&pageSize=25&sortBy=PROJECT_NUMBER&sortDirection=ASC");
        var numbers = ascending.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("projectNumber").GetString()).ToArray();
        Assert.Equal(numbers.Order(StringComparer.Ordinal), numbers);
    }

    [Fact]
    public async Task Lifecycle_rejects_skips_supports_audited_correction_and_blocks_closed_project_children()
    {
        var project = await Created("/api/trading/projects", new
        {
            titleFa = "چرخه عمر",
            titleEn = (string?)null,
            customerId = (Guid?)null,
            supplierId = (Guid?)null,
            priority = "NORMAL",
            targetDate = (string?)null,
            notes = (string?)null
        });
        var projectId = project.GetProperty("project").GetProperty("id").GetGuid();
        var version = project.GetProperty("project").GetProperty("version").GetInt64();

        using (var skip = await _client.PostAsJsonAsync($"/api/trading/projects/{projectId}/status",
                   new { status = "WAITING_ORDER", expectedVersion = version, reason = "skip forbidden" }))
            Assert.Equal(HttpStatusCode.Conflict, skip.StatusCode);

        var active = await Ok($"/api/trading/projects/{projectId}/status", new
        { status = "ACTIVE", expectedVersion = version, reason = "activate" });
        version = active.GetProperty("project").GetProperty("version").GetInt64();
        var corrected = await Ok($"/api/trading/projects/{projectId}/status", new
        { status = "DRAFT", expectedVersion = version, reason = "owner-approved correction", isCorrection = true });
        Assert.Equal("DRAFT", corrected.GetProperty("project").GetProperty("status").GetString());
        version = corrected.GetProperty("project").GetProperty("version").GetInt64();

        foreach (var status in new[] { "ACTIVE", "WAITING_ORDER", "FX_PROCESS", "READY_TO_SHIP", "IN_TRANSIT",
                     "IN_CUSTOMS", "CLEARED", "DELIVERED", "CLOSED" })
        {
            var changed = await Ok($"/api/trading/projects/{projectId}/status",
                new { status, expectedVersion = version, reason = $"advance to {status}" });
            version = changed.GetProperty("project").GetProperty("version").GetInt64();
        }

        using var blocked = await _client.PostAsJsonAsync("/api/trading/order-registrations", new
        {
            projectId,
            registrationNumber = "REG-CLOSED",
            status = "DRAFT",
            registrationDate = "2026-09-01",
            registeredAmount = 1m,
            currencyId = _currencyId,
            notes = (string?)null
        });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        var reopened = await Ok($"/api/trading/projects/{projectId}/reopen",
            new { expectedVersion = version, reason = "authorized reopen" });
        Assert.Equal("DELIVERED", reopened.GetProperty("project").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Project_detail_and_report_mutation_respect_independent_permissions()
    {
        var projectId = await CreateProject("تفکیک مجوز");
        await Created("/api/trading/order-registrations", new
        {
            projectId,
            registrationNumber = "REG-PERM",
            status = "DRAFT",
            registrationDate = "2026-09-01",
            registeredAmount = 2m,
            currencyId = _currencyId,
            notes = (string?)null
        });

        using var projectOnly = _factory.CreateClient();
        projectOnly.DefaultRequestHeaders.Add("X-Hadron-Test-Permissions", Permissions.TradingProjectView);
        using var detailResponse = await projectOnly.GetAsync($"/api/trading/projects/{projectId}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(detail.GetProperty("orderRegistrations").EnumerateArray());
        Assert.Empty(detail.GetProperty("fxAllocations").EnumerateArray());
        Assert.Empty(detail.GetProperty("shipments").EnumerateArray());
        Assert.Empty(detail.GetProperty("costs").EnumerateArray());
        Assert.Empty(detail.GetProperty("audit").EnumerateArray());
        Assert.False(detail.GetProperty("capabilities").GetProperty("canViewOrders").GetBoolean());

        using var reportViewer = _factory.CreateClient();
        reportViewer.DefaultRequestHeaders.Add("X-Hadron-Test-Permissions", Permissions.TradingReportView);
        using var note = await reportViewer.PostAsJsonAsync("/api/trading/report-notes", new { projectId, note = "forbidden" });
        Assert.Equal(HttpStatusCode.Forbidden, note.StatusCode);
    }

    [Fact]
    public async Task Shared_shipment_link_and_allocation_update_preserve_both_project_contexts()
    {
        var first = await CreateProject("پرونده حمل اول");
        var second = await CreateProject("پرونده حمل دوم");
        var shipment = await Created("/api/trading/shipments", new
        {
            projectId = first,
            status = "DRAFT",
            transportMode = "SEA",
            forwarderId = (Guid?)null,
            origin = "Shanghai",
            destination = "Bandar Abbas",
            estimatedDeparture = "2026-09-01",
            estimatedArrival = "2026-09-20",
            grossWeight = 100m,
            volumeCbm = 4m,
            allocationMethod = "EQUAL",
            allocationValue = 1m,
            notes = (string?)null
        });
        var shipmentId = shipment.GetProperty("shipment").GetProperty("id").GetGuid();
        var link = await Created($"/api/trading/shipments/{shipmentId}/projects",
            new { projectId = second, allocationMethod = "WEIGHT", allocationValue = 40m, notes = "shared" });
        var linkId = link.GetProperty("linkId").GetGuid();
        var linkVersion = link.GetProperty("version").GetInt64();
        var updated = await Put($"/api/trading/project-shipments/{linkId}", new
        {
            allocationMethod = "MANUAL_PERCENT",
            allocationValue = 35m,
            notes = "approved split",
            expectedVersion = linkVersion,
            reason = "correct allocation"
        });
        Assert.Equal(35m, updated.GetProperty("allocationValue").GetDecimal());
        Assert.Single((await Get($"/api/trading/projects/{first}")).GetProperty("shipments").EnumerateArray());
        Assert.Single((await Get($"/api/trading/projects/{second}")).GetProperty("shipments").EnumerateArray());
    }

    [Fact]
    public async Task Database_rejects_audit_mutation_invalid_status_and_cross_project_cost_scope()
    {
        var first = await CreateProject("دفاع دیتابیس اول");
        var second = await CreateProject("دفاع دیتابیس دوم");
        var shipment = await Created("/api/trading/shipments", new
        {
            projectId = first,
            status = "DRAFT",
            transportMode = "AIR",
            forwarderId = (Guid?)null,
            origin = "A",
            destination = "B",
            estimatedDeparture = "2026-09-01",
            estimatedArrival = "2026-09-02",
            grossWeight = 1m,
            volumeCbm = 1m,
            allocationMethod = "EQUAL",
            allocationValue = 1m,
            notes = (string?)null
        });
        var shipmentId = shipment.GetProperty("shipment").GetProperty("id").GetGuid();
        var cost = await Created("/api/trading/costs", new
        {
            projectId = first,
            scopeType = "SHIPMENT",
            scopeId = shipmentId,
            costType = "FREIGHT",
            amount = 3m,
            currencyId = _currencyId,
            effectiveDate = "2026-09-01",
            referenceNumber = (string?)null,
            notes = (string?)null
        });
        var costId = cost.GetProperty("id").GetGuid();

        await using var scope = _factory.Services.CreateAsyncScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HadronDbContext>>();
        await using var db = await contextFactory.CreateDbContextAsync();
        var auditId = await db.TradeAuditEvents.Where(x => x.EntityId == first).Select(x => x.Id).FirstAsync();
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE trading.audit_events SET \"Reason\" = {"x"} WHERE \"Id\" = {auditId}"));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE trading.projects SET \"Status\" = {"INVALID"} WHERE \"Id\" = {first}"));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE trading.costs SET \"ProjectId\" = {second} WHERE \"Id\" = {costId}"));
    }

    [Fact]
    public async Task Order_management_pages_filter_by_project_and_preserves_exact_decimal_text()
    {
        var first = await CreateProject("پرونده سفارش اول");
        var other = await CreateProject("پرونده سفارش دوم");
        for (var i = 0; i < 27; i++) await CreateManagedOrder(first, $"REG-{i:D3}");
        await CreateManagedOrder(other, "OTHER-PROJECT");
        var secondPage = await Get($"/api/trading/projects/{first}/order-registrations?page=2&pageSize=25");
        Assert.Equal(27, secondPage.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, secondPage.GetProperty("items").GetArrayLength());
        Assert.All(secondPage.GetProperty("items").EnumerateArray(), x => Assert.Equal(first, x.GetProperty("projectId").GetGuid()));
        var filtered = await Get($"/api/trading/projects/{first}/order-registrations?search=reg-026&status=DRAFT");
        var row = Assert.Single(filtered.GetProperty("items").EnumerateArray());
        Assert.Equal("9007199254740993.123456", row.GetProperty("registeredAmount").GetString());
        Assert.False(row.TryGetProperty("bankId", out _));
        using var invalidPage = await _client.GetAsync($"/api/trading/projects/{first}/order-registrations?page=2147483647");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);
        using var unknownProject = await _client.GetAsync($"/api/trading/projects/{Guid.NewGuid()}/order-registrations");
        Assert.Equal(HttpStatusCode.NotFound, unknownProject.StatusCode);
    }

    [Fact]
    public async Task Draft_order_edit_is_versioned_audited_and_does_not_create_later_operations()
    {
        var project = await CreateProject("ویرایش سفارش");
        var created = await CreateManagedOrder(project, "REG-EDIT");
        var id = created.GetProperty("id").GetGuid();
        var version = created.GetProperty("version").GetInt64();
        var request = new { registrationNumber = "REG-CORRECTED", registrationDate = "2026-10-19",
            registeredAmount = "999999999999999999.999999", currencyId = _currencyId, notes = "یادداشت سند",
            expectedVersion = version, reason = "اصلاح مشخصات پیش‌نویس", projectId = Guid.NewGuid(), status = "ACTIVE" };
        var edited = await Put($"/api/trading/order-registrations/{id}", request);
        Assert.Equal("999999999999999999.999999", edited.GetProperty("registeredAmount").GetString());
        var resource = await Get($"/api/trading/order-registrations/{id}");
        Assert.Equal("999999999999999999.999999", resource.GetProperty("registeredAmount").GetString());
        Assert.Equal(version + 1, edited.GetProperty("version").GetInt64());
        Assert.Equal("DRAFT", edited.GetProperty("status").GetString());
        Assert.Equal(project, edited.GetProperty("projectId").GetGuid());
        using var stale = await _client.PutAsJsonAsync($"/api/trading/order-registrations/{id}", request);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<HadronDbContext>>().CreateDbContextAsync();
        var audit = await db.TradeAuditEvents.SingleAsync(x => x.EntityId == project && x.Action == "ORDER_REGISTRATION_EDIT");
        Assert.Contains("REG-EDIT", audit.PreviousJson!);
        Assert.Contains("REG-CORRECTED", audit.CurrentJson!);
        Assert.Equal(request.reason, audit.Reason);
        Assert.Equal(0, await db.TradeFxAllocations.CountAsync(x => x.ProjectId == project));
        Assert.Equal(0, await db.TradeProjectShipments.CountAsync(x => x.ProjectId == project));
        Assert.Equal("DRAFT", (await db.TradeProjects.SingleAsync(x => x.Id == project)).Status);
        Assert.Single(await db.TradeAuditEvents.Where(x => x.EntityId == project && x.Action == "ORDER_REGISTRATION_EDIT").ToListAsync());
    }

    [Fact]
    public async Task Order_commands_validate_precision_duplicates_and_lifecycle_boundaries()
    {
        var project = await CreateProject("اعتبارسنجی سفارش");
        foreach (var amount in new[] { "0", "-1", "0.0000001", "1000000000000000000" })
        {
            using var invalid = await _client.PostAsJsonAsync("/api/trading/order-registrations", new {
                projectId = project, registrationNumber = "INVALID", status = "DRAFT", registrationDate = "2026-10-18",
                registeredAmount = amount, currencyId = _currencyId });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using var missingDate = await _client.PostAsJsonAsync("/api/trading/order-registrations", new {
            projectId = project, registrationNumber = "INVALID", status = "DRAFT", registeredAmount = "1", currencyId = _currencyId });
        Assert.Equal(HttpStatusCode.BadRequest, missingDate.StatusCode);
        using var invalidCurrency = await _client.PostAsJsonAsync("/api/trading/order-registrations", new {
            projectId = project, registrationNumber = "INVALID", status = "DRAFT", registrationDate = "2026-10-18", registeredAmount = "1", currencyId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, invalidCurrency.StatusCode);
        var created = await CreateManagedOrder(project, "REG-UNIQUE");
        using var duplicate = await _client.PostAsJsonAsync("/api/trading/order-registrations", new {
            projectId = project, registrationNumber = "REG-UNIQUE", status = "DRAFT", registrationDate = "2026-10-18", registeredAmount = "1", currencyId = _currencyId });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var id = created.GetProperty("id").GetGuid();
        var submitted = await Ok($"/api/trading/order-registrations/{id}/status", new { status = "SUBMITTED", expectedVersion = 1, reason = "ارسال سند" });
        using var forbiddenEdit = await _client.PutAsJsonAsync($"/api/trading/order-registrations/{id}", new {
            registrationNumber = "REG-UNIQUE", registrationDate = "2026-10-18", registeredAmount = "2", currencyId = _currencyId,
            expectedVersion = submitted.GetProperty("version").GetInt64(), reason = "تغییر نامعتبر" });
        Assert.Equal(HttpStatusCode.Conflict, forbiddenEdit.StatusCode);
        await Ok($"/api/trading/order-registrations/{id}/status", new { status = "CANCELLED", expectedVersion = submitted.GetProperty("version").GetInt64(), reason = "لغو سند" });
        using var revive = await _client.PostAsJsonAsync($"/api/trading/order-registrations/{id}/status", new { status = "DRAFT", expectedVersion = 3, reason = "بازگشت نامعتبر", isCorrection = true });
        Assert.Equal(HttpStatusCode.Conflict, revive.StatusCode);
    }

    [Fact]
    public async Task Order_management_reuses_only_order_permissions_for_lookup_and_date_conversion()
    {
        var project = await CreateProject("مجوز سفارش");
        var order = await CreateManagedOrder(project, "REG-PERMISSION");
        var id = order.GetProperty("id").GetGuid();
        using var viewer = _factory.CreateClient();
        viewer.DefaultRequestHeaders.Add("X-Hadron-Test-Permissions", Permissions.TradingOrderRegistrationView);
        using var read = await viewer.GetAsync($"/api/trading/projects/{project}/order-registrations");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var deniedWrite = await viewer.PutAsJsonAsync($"/api/trading/order-registrations/{id}", new {
            registrationNumber = "REG-PERMISSION", registrationDate = "2026-10-18", registeredAmount = "5", currencyId = _currencyId, expectedVersion = 1, reason = "forbidden" });
        Assert.Equal(HttpStatusCode.Forbidden, deniedWrite.StatusCode);
        using var deniedCurrencies = await viewer.GetAsync("/api/trading/order-registrations/currencies");
        Assert.Equal(HttpStatusCode.Forbidden, deniedCurrencies.StatusCode);
        using var deniedDate = await viewer.PostAsJsonAsync("/api/trading/order-registrations/resolve-jalali-date", new { jalaliDate = "14050726" });
        Assert.Equal(HttpStatusCode.Forbidden, deniedDate.StatusCode);
        using var projectViewer = _factory.CreateClient();
        projectViewer.DefaultRequestHeaders.Add("X-Hadron-Test-Permissions", Permissions.TradingProjectView);
        using var deniedRead = await projectViewer.GetAsync($"/api/trading/projects/{project}/order-registrations");
        Assert.Equal(HttpStatusCode.Forbidden, deniedRead.StatusCode);
        using var manager = _factory.CreateClient();
        manager.DefaultRequestHeaders.Add("X-Hadron-Test-Permissions", Permissions.TradingOrderRegistrationManage);
        using var date = await manager.PostAsJsonAsync("/api/trading/order-registrations/resolve-jalali-date", new { jalaliDate = "۱۴۰۵۰۷۲۶" });
        Assert.Equal(HttpStatusCode.OK, date.StatusCode);
        using var currencies = await manager.GetAsync("/api/trading/order-registrations/currencies?search=usd");
        Assert.Equal(HttpStatusCode.OK, currencies.StatusCode);
        var lookup = await currencies.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(lookup.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == _currencyId);
        using var planning = await manager.PostAsJsonAsync("/api/planning/resolve-jalali-date", new { jalaliDate = "14050726" });
        Assert.Equal(HttpStatusCode.Forbidden, planning.StatusCode);
    }

    [Fact]
    public async Task Closed_projects_and_fx_linked_drafts_reject_order_data_rewrites()
    {
        var project = await CreateProject("مرز عملیات سفارش");
        var order = await CreateManagedOrder(project, "REG-LINKED");
        var id = order.GetProperty("id").GetGuid();
        await Created("/api/trading/fx-allocations", new { projectId = project, orderRegistrationId = id,
            processType = "REQUEST", dealStatus = "NOT_STARTED", receiptStatus = "NOT_RECEIVED",
            amount = 1m, currencyId = _currencyId, requestDate = "2026-10-18" });
        using var linkedEdit = await _client.PutAsJsonAsync($"/api/trading/order-registrations/{id}", new {
            registrationNumber = "REG-LINKED", registrationDate = "2026-10-18", registeredAmount = "2", currencyId = _currencyId, expectedVersion = 1, reason = "تغییر سند متصل" });
        Assert.Equal(HttpStatusCode.Conflict, linkedEdit.StatusCode);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<HadronDbContext>>().CreateDbContextAsync();
            (await db.TradeProjects.SingleAsync(x => x.Id == project)).ChangeStatus("CLOSED");
            await db.SaveChangesAsync();
        }
        using var closedCreate = await _client.PostAsJsonAsync("/api/trading/order-registrations", new {
            projectId = project, registrationNumber = "REG-CLOSED", status = "DRAFT", registrationDate = "2026-10-18", registeredAmount = "1", currencyId = _currencyId });
        Assert.Equal(HttpStatusCode.Conflict, closedCreate.StatusCode);
        using var closedRead = await _client.GetAsync($"/api/trading/projects/{project}/order-registrations");
        Assert.Equal(HttpStatusCode.OK, closedRead.StatusCode);
    }

    [Fact]
    public async Task Inactive_currencies_are_hidden_for_new_orders_but_existing_snapshots_survive_edit()
    {
        var project = await CreateProject("مرجع ارز سفارش");
        var order = await CreateManagedOrder(project, "REG-SNAPSHOT");
        var id = order.GetProperty("id").GetGuid();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<HadronDbContext>>().CreateDbContextAsync();
            var currency = await db.Currencies.SingleAsync(x => x.Id == _currencyId);
            currency.SetNames("نام جدید مرجع", "Renamed currency"); currency.SetActive(false); currency.Touch();
            await db.SaveChangesAsync();
        }
        var lookup = await Get("/api/trading/order-registrations/currencies?search=usd");
        Assert.Empty(lookup.GetProperty("items").EnumerateArray());
        using var create = await _client.PostAsJsonAsync("/api/trading/order-registrations", new {
            projectId = project, registrationNumber = "REG-INACTIVE", status = "DRAFT", registrationDate = "2026-10-18", registeredAmount = "1", currencyId = _currencyId });
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        var edited = await Put($"/api/trading/order-registrations/{id}", new {
            registrationNumber = "REG-SNAPSHOT", registrationDate = "2026-10-19", registeredAmount = "1.000001", currencyId = _currencyId, expectedVersion = 1, reason = "اصلاح بدون تعویض ارز" });
        Assert.Equal("دلار آمریکا", edited.GetProperty("currencyNameFa").GetString());
        Assert.Equal("USD", edited.GetProperty("currencyCode").GetString());
        Assert.Equal("1.000001", edited.GetProperty("registeredAmount").GetString());
    }

    private Task<JsonElement> CreateManagedOrder(Guid projectId, string number) => Created("/api/trading/order-registrations", new {
        projectId, registrationNumber = number, status = "DRAFT", registrationDate = "2026-10-18",
        registeredAmount = "9007199254740993.123456", currencyId = _currencyId, notes = "توضیحات سفارش" });

    private async Task<Guid> CreateProject(string title)
    {
        var body = await Created("/api/trading/projects", new { titleFa = title, titleEn = (string?)null, customerId = (Guid?)null, supplierId = (Guid?)null, priority = "NORMAL", targetDate = (string?)null, notes = (string?)null });
        return body.GetProperty("project").GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> Get(string path)
    {
        using var response = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<JsonElement> Created(string path, object body, HttpStatusCode expected = HttpStatusCode.Created)
    {
        using var response = await _client.PostAsJsonAsync(path, body);
        Assert.Equal(expected, response.StatusCode);
        return response.Content.Headers.ContentLength > 0 ? await response.Content.ReadFromJsonAsync<JsonElement>() : default;
    }

    private async Task<JsonElement> Ok(string path, object body)
    {
        using var response = await _client.PostAsJsonAsync(path, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<JsonElement> Put(string path, object body)
    {
        using var response = await _client.PutAsJsonAsync(path, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
