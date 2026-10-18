using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.CashFlow.Application;
using Hadron.Erp.Api.Modules.CashFlow.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Hadron.Erp.Api.Platform.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Hadron.Erp.IntegrationTests;

[Collection("ReferenceData")]
public sealed class ReportingFxRateTests : IClassFixture<FoundationApiFactory>, IAsyncLifetime
{
    private readonly FoundationApiFactory _factory;
    private readonly HttpClient _client;

    public ReportingFxRateTests(FoundationApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await using var db = await Db();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE cash_flow.reporting_fx_rate_sets CASCADE");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Rate_set_list_rejects_an_offset_that_cannot_be_represented_safely()
    {
        using var response = await _client.GetAsync(
            "/api/v1/cash-flow/reporting-rate-sets?page=2147483647&pageSize=25");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty("page", out _));
    }

    [Fact]
    public async Task Endpoints_publish_exact_permission_metadata_and_fail_closed()
    {
        var cases = new[]
        {
            ("/api/v1/cash-flow/reporting-rate-currencies", HttpMethods.Get,
                Permissions.CashFlowReportingRateView),
            ("/api/v1/cash-flow/reporting-rate-sets", HttpMethods.Get,
                Permissions.CashFlowReportingRateView),
            ("/api/v1/cash-flow/reporting-rate-sets/current", HttpMethods.Get,
                Permissions.CashFlowReportingRateView),
            ("/api/v1/cash-flow/reporting-rate-sets/resolve-jalali-date", HttpMethods.Get,
                Permissions.CashFlowReportingRateView),
            ("/api/v1/cash-flow/reporting-rate-sets/{id:guid}", HttpMethods.Get,
                Permissions.CashFlowReportingRateView),
            ("/api/v1/cash-flow/reporting-rate-sets", HttpMethods.Post,
                Permissions.CashFlowReportingRateManage),
            ("/api/v1/cash-flow/reporting-rate-sets/{id:guid}", HttpMethods.Put,
                Permissions.CashFlowReportingRateManage),
            ("/api/v1/cash-flow/reporting-rate-sets/{id:guid}/publish", HttpMethods.Post,
                Permissions.CashFlowReportingRateManage),
            ("/api/v1/cash-flow/reporting-rate-sets/{id:guid}/corrections", HttpMethods.Post,
                Permissions.CashFlowReportingRateManage)
        };
        var endpoints = _factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(x => x.Endpoints).OfType<RouteEndpoint>().ToArray();

        foreach (var (route, method, permission) in cases)
        {
            var endpoint = Assert.Single(endpoints, candidate =>
                candidate.RoutePattern.RawText == route &&
                candidate.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(method) == true);
            Assert.Equal(permission,
                endpoint.Metadata.GetMetadata<RequiredPermissionMetadata>()?.Code);
        }

        var currencies = await SeedCurrencies();
        using var viewOnly = _factory.CreateClient();
        viewOnly.DefaultRequestHeaders.Add("X-Hadron-Test-Permissions",
            Permissions.CashFlowReportingRateView);
        Assert.Equal(HttpStatusCode.OK,
            (await viewOnly.GetAsync("/api/v1/cash-flow/reporting-rate-currencies")).StatusCode);
        var forbiddenMutation = await viewOnly.PostAsJsonAsync(
            "/api/v1/cash-flow/reporting-rate-sets", ValidCreate(currencies));
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenMutation.StatusCode);
        var forbiddenId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await viewOnly.PutAsJsonAsync(
            $"/api/v1/cash-flow/reporting-rate-sets/{forbiddenId}",
            new UpdateReportingFxRateSetRequest(Guid.NewGuid(), new DateOnly(2026, 9, 1), 1,
                "forbidden", ValidCreate(currencies).Rates))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewOnly.PostAsJsonAsync(
            $"/api/v1/cash-flow/reporting-rate-sets/{forbiddenId}/publish",
            new PublishReportingFxRateSetRequest(Guid.NewGuid(), 1, "forbidden"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewOnly.PostAsJsonAsync(
            $"/api/v1/cash-flow/reporting-rate-sets/{forbiddenId}/corrections",
            new CreateReportingFxCorrectionRequest(Guid.NewGuid(), 1, "forbidden"))).StatusCode);

        using var manageOnly = _factory.CreateClient();
        manageOnly.DefaultRequestHeaders.Add("X-Hadron-Test-Permissions",
            Permissions.CashFlowReportingRateManage);
        foreach (var readPath in new[]
                 {
                     "/api/v1/cash-flow/reporting-rate-currencies",
                     "/api/v1/cash-flow/reporting-rate-sets?page=1&pageSize=25",
                     "/api/v1/cash-flow/reporting-rate-sets/current?asOfDate=2026-09-01",
                     "/api/v1/cash-flow/reporting-rate-sets/resolve-jalali-date?jalaliDate=14050610",
                     $"/api/v1/cash-flow/reporting-rate-sets/{forbiddenId}"
                 })
        {
            Assert.Equal(HttpStatusCode.Forbidden,
                (await manageOnly.GetAsync(readPath)).StatusCode);
        }
        var allowedMutation = await manageOnly.PostAsJsonAsync(
            "/api/v1/cash-flow/reporting-rate-sets", ValidCreate(currencies));
        Assert.Equal(HttpStatusCode.Created, allowedMutation.StatusCode);

        using var explicitlyDenied = _factory.CreateClient();
        explicitlyDenied.DefaultRequestHeaders.Add("X-Hadron-Test-Denied-Permissions",
            Permissions.CashFlowReportingRateManage);
        var deniedMutation = await explicitlyDenied.PostAsJsonAsync(
            "/api/v1/cash-flow/reporting-rate-sets",
            ValidCreate(currencies) with { ClientRequestId = Guid.NewGuid(), EffectiveDate = new(2026, 9, 2) });
        Assert.Equal(HttpStatusCode.Forbidden, deniedMutation.StatusCode);
    }

    [Fact]
    public async Task Every_reporting_rate_route_returns_401_without_a_cookie()
    {
        var factory = new IdentityApiFactory();
        await ((IAsyncLifetime)factory).InitializeAsync();
        try
        {
            using var anonymous = factory.CreateClient();
            var id = Guid.NewGuid();
            var requests = new Func<HttpRequestMessage>[]
            {
                () => new(HttpMethod.Get, "/api/v1/cash-flow/reporting-rate-currencies"),
                () => new(HttpMethod.Get, "/api/v1/cash-flow/reporting-rate-sets?page=1&pageSize=25"),
                () => new(HttpMethod.Get, "/api/v1/cash-flow/reporting-rate-sets/current?asOfDate=2026-09-01"),
                () => new(HttpMethod.Get, "/api/v1/cash-flow/reporting-rate-sets/resolve-jalali-date?jalaliDate=14050610"),
                () => new(HttpMethod.Get, $"/api/v1/cash-flow/reporting-rate-sets/{id}"),
                () => new(HttpMethod.Post, "/api/v1/cash-flow/reporting-rate-sets")
                    { Content = JsonContent.Create(new { }) },
                () => new(HttpMethod.Put, $"/api/v1/cash-flow/reporting-rate-sets/{id}")
                    { Content = JsonContent.Create(new { }) },
                () => new(HttpMethod.Post, $"/api/v1/cash-flow/reporting-rate-sets/{id}/publish")
                    { Content = JsonContent.Create(new { }) },
                () => new(HttpMethod.Post, $"/api/v1/cash-flow/reporting-rate-sets/{id}/corrections")
                    { Content = JsonContent.Create(new { }) }
            };

            foreach (var requestFactory in requests)
            {
                using var request = requestFactory();
                using var response = await anonymous.SendAsync(request);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }
        }
        finally
        {
            await ((IAsyncLifetime)factory).DisposeAsync();
        }
    }

    [Fact]
    public async Task Draft_publish_correction_workflow_is_exact_immutable_audited_and_resolves_current_revision()
    {
        var currencies = await SeedCurrencies();
        var create = ValidCreate(currencies);

        var createdResponse = await _client.PostAsJsonAsync(
            "/api/v1/cash-flow/reporting-rate-sets", create);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await Read(createdResponse);
        Assert.Equal(ReportingFxRateSetStatuses.Draft, created.Status);
        Assert.Equal(1, created.Revision);
        Assert.Equal(1, created.Version);
        Assert.Equal(currencies.AllNonUsd.Count, created.Rates.Count);
        Assert.Equal(2_200_000m, Rate(created, "IRR"));
        Assert.Equal(6.8m, Rate(created, "CNY"));
        Assert.Equal(0.86m, Rate(created, "EUR"));
        Assert.Equal(3.6725m, Rate(created, "AED"));
        Assert.False(created.Rates.Single(x => x.CurrencyCode == "AED").CurrencyIsActive);
        Assert.DoesNotContain(created.Rates, x => x.CurrencyCode == "USD");

        var replayResponse = await _client.PostAsJsonAsync(
            "/api/v1/cash-flow/reporting-rate-sets", create);
        Assert.Equal(HttpStatusCode.Created, replayResponse.StatusCode);
        Assert.Equal(created.Id, (await Read(replayResponse)).Id);
        await using (var auditDb = await Db())
        {
            Assert.Single(await auditDb.OperationalAuditEvents.AsNoTracking()
                .Where(x => x.EntityType == "ReportingFxRateSet" && x.EntityId == created.Id)
                .ToListAsync());
        }

        var updatedRates = create.Rates.Select(x =>
            x.CurrencyId == currencies.Cny.Id ? x with { UnitsPerUsd = 7.125m } : x).ToArray();
        var updateRequest = new UpdateReportingFxRateSetRequest(
            Guid.NewGuid(), create.EffectiveDate, created.Version,
            "به‌روزرسانی نرخ بانک", updatedRates);
        var updateResponse = await _client.PutAsJsonAsync(
            $"/api/v1/cash-flow/reporting-rate-sets/{created.Id}",
            updateRequest);
        Assert.True(updateResponse.StatusCode == HttpStatusCode.OK,
            await updateResponse.Content.ReadAsStringAsync());
        var updated = await Read(updateResponse);
        Assert.Equal(2, updated.Version);
        Assert.Equal(7.125m, Rate(updated, "CNY"));

        // A transport retry after a lost response returns the committed result without
        // advancing Version, duplicating rate rows or writing a second audit event.
        var updateReplayResponse = await _client.PutAsJsonAsync(
            $"/api/v1/cash-flow/reporting-rate-sets/{created.Id}", updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateReplayResponse.StatusCode);
        var updateReplay = await Read(updateReplayResponse);
        Assert.Equal(updated.Id, updateReplay.Id);
        Assert.Equal(updated.Version, updateReplay.Version);

        var reusedUpdateKey = updateRequest with { ChangeReason = "درخواست متفاوت" };
        var reusedUpdateResponse = await _client.PutAsJsonAsync(
            $"/api/v1/cash-flow/reporting-rate-sets/{created.Id}", reusedUpdateKey);
        Assert.Equal(HttpStatusCode.Conflict, reusedUpdateResponse.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", await ProblemCode(reusedUpdateResponse));

        var createReplayAfterUpdate = await _client.PostAsJsonAsync(
            "/api/v1/cash-flow/reporting-rate-sets", create);
        Assert.Equal(HttpStatusCode.Conflict, createReplayAfterUpdate.StatusCode);
        Assert.Equal("IDEMPOTENT_RESULT_SUPERSEDED", await ProblemCode(createReplayAfterUpdate));

        var staleResponse = await _client.PutAsJsonAsync(
            $"/api/v1/cash-flow/reporting-rate-sets/{created.Id}",
            new UpdateReportingFxRateSetRequest(Guid.NewGuid(), create.EffectiveDate, 1,
                "ویرایش قدیمی", updatedRates));
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        Assert.Equal("STALE_VERSION", await ProblemCode(staleResponse));

        var publishRequest = new PublishReportingFxRateSetRequest(
            Guid.NewGuid(), updated.Version, "تأیید مدیر مالی");
        var publishedResponse = await _client.PostAsJsonAsync(
            $"/api/v1/cash-flow/reporting-rate-sets/{created.Id}/publish",
            publishRequest);
        Assert.Equal(HttpStatusCode.OK, publishedResponse.StatusCode);
        var published = await Read(publishedResponse);
        Assert.Equal(ReportingFxRateSetStatuses.Published, published.Status);
        Assert.Equal(3, published.Version);
        Assert.NotNull(published.PublishedAtUtc);

        var publishReplayResponse = await _client.PostAsJsonAsync(
            $"/api/v1/cash-flow/reporting-rate-sets/{created.Id}/publish", publishRequest);
        Assert.Equal(HttpStatusCode.OK, publishReplayResponse.StatusCode);
        var publishReplay = await Read(publishReplayResponse);
        Assert.Equal(published.Id, publishReplay.Id);
        Assert.Equal(published.Version, publishReplay.Version);

        var reusedPublishResponse = await _client.PostAsJsonAsync(
            $"/api/v1/cash-flow/reporting-rate-sets/{created.Id}/publish",
            publishRequest with { Reason = "درخواست انتشار متفاوت" });
        Assert.Equal(HttpStatusCode.Conflict, reusedPublishResponse.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", await ProblemCode(reusedPublishResponse));

        var updateReplayAfterPublish = await _client.PutAsJsonAsync(
            $"/api/v1/cash-flow/reporting-rate-sets/{created.Id}", updateRequest);
        Assert.Equal(HttpStatusCode.Conflict, updateReplayAfterPublish.StatusCode);
        Assert.Equal("IDEMPOTENT_RESULT_SUPERSEDED", await ProblemCode(updateReplayAfterPublish));

        var immutableResponse = await _client.PutAsJsonAsync(
            $"/api/v1/cash-flow/reporting-rate-sets/{created.Id}",
            new UpdateReportingFxRateSetRequest(Guid.NewGuid(), create.EffectiveDate, published.Version,
                "نباید اعمال شود", updatedRates));
        Assert.Equal(HttpStatusCode.Conflict, immutableResponse.StatusCode);
        Assert.Equal("RATE_SET_IMMUTABLE", await ProblemCode(immutableResponse));

        var correctionRequest = new CreateReportingFxCorrectionRequest(
            Guid.NewGuid(), published.Version, "اصلاح نرخ قطعی بانک");
        var correctionResponse = await _client.PostAsJsonAsync(
            $"/api/v1/cash-flow/reporting-rate-sets/{created.Id}/corrections", correctionRequest);
        Assert.Equal(HttpStatusCode.Created, correctionResponse.StatusCode);
        var correction = await Read(correctionResponse);
        Assert.Equal(2, correction.Revision);
        Assert.Equal(ReportingFxRateSetStatuses.Draft, correction.Status);
        Assert.Equal(created.Id, correction.SupersedesRateSetId);
        Assert.Equal(7.125m, Rate(correction, "CNY"));

        var correctionReplay = await _client.PostAsJsonAsync(
            $"/api/v1/cash-flow/reporting-rate-sets/{created.Id}/corrections", correctionRequest);
        Assert.Equal(correction.Id, (await Read(correctionReplay)).Id);

        var reusedCorrectionResponse = await _client.PostAsJsonAsync(
            $"/api/v1/cash-flow/reporting-rate-sets/{created.Id}/corrections",
            correctionRequest with { ExpectedVersion = correctionRequest.ExpectedVersion + 1 });
        Assert.Equal(HttpStatusCode.Conflict, reusedCorrectionResponse.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", await ProblemCode(reusedCorrectionResponse));

        var correctedRates = correction.Rates.Select(x => new ReportingFxRateInput(
            x.CurrencyId, x.CurrencyCode == "CNY" ? 7.2m : x.UnitsPerUsd)).ToArray();
        var correctedUpdateResponse = await _client.PutAsJsonAsync(
            $"/api/v1/cash-flow/reporting-rate-sets/{correction.Id}",
            new UpdateReportingFxRateSetRequest(Guid.NewGuid(), correction.EffectiveDate, correction.Version,
                "نرخ اصلاح‌شده", correctedRates));
        var correctedUpdate = await Read(correctedUpdateResponse);

        // A retry cannot pretend that the later edited representation was the original
        // correction result; the UI must refresh and start a new user action.
        var correctionReplayAfterEdit = await _client.PostAsJsonAsync(
            $"/api/v1/cash-flow/reporting-rate-sets/{created.Id}/corrections", correctionRequest);
        Assert.Equal(HttpStatusCode.Conflict, correctionReplayAfterEdit.StatusCode);
        Assert.Equal("IDEMPOTENT_RESULT_SUPERSEDED", await ProblemCode(correctionReplayAfterEdit));

        var correctedPublishResponse = await _client.PostAsJsonAsync(
            $"/api/v1/cash-flow/reporting-rate-sets/{correction.Id}/publish",
            new PublishReportingFxRateSetRequest(Guid.NewGuid(), correctedUpdate.Version, "انتشار اصلاحیه"));
        Assert.Equal(HttpStatusCode.OK, correctedPublishResponse.StatusCode);
        var correctedPublished = await Read(correctedPublishResponse);

        var currentResponse = await _client.GetAsync(
            $"/api/v1/cash-flow/reporting-rate-sets/current?asOfDate={create.EffectiveDate:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, currentResponse.StatusCode);
        var current = await Read(currentResponse);
        Assert.Equal(correctedPublished.Id, current.Id);
        Assert.Equal(2, current.Revision);
        Assert.Equal(7.2m, Rate(current, "CNY"));

        await using var db = await Db();
        var rateSetIds = new[] { created.Id, correction.Id };
        var audits = await db.OperationalAuditEvents.AsNoTracking()
            .Where(x => x.EntityType == "ReportingFxRateSet" && rateSetIds.Contains(x.EntityId))
            .ToListAsync();
        Assert.Equal(6, audits.Count);
        Assert.Single(audits, x => x.Action == "CREATE");
        Assert.Equal(2, audits.Count(x => x.Action == "UPDATE_DRAFT"));
        Assert.Equal(2, audits.Count(x => x.Action == "PUBLISH"));
        Assert.Single(audits, x => x.Action == "CREATE_CORRECTION");
        Assert.All(audits, audit =>
        {
            Assert.NotNull(audit.CorrelationId);
            Assert.NotNull(audit.CurrentValuesJson);
        });
        Assert.Equal(4, await db.ReportingFxOperationReceipts.CountAsync(
            x => rateSetIds.Contains(x.RateSetId)));
        Assert.Equal(currencies.AllNonUsd.Count,
            await db.ReportingFxRates.CountAsync(x => x.RateSetId == created.Id));
    }

    [Fact]
    public async Task Audit_captures_actor_source_utc_and_before_image()
    {
        var currencies = await SeedCurrencies();
        var fixedUtc = new DateTimeOffset(2026, 9, 7, 8, 9, 10, TimeSpan.Zero);
        var actor = "cash-flow-audit-tester";
        var correlationId = "cash-flow-audit-correlation";
        var service = CreateService(actor, correlationId, fixedUtc);
        var createRequest = ValidCreate(currencies) with
        {
            EffectiveDate = new DateOnly(2026, 9, 7),
            ClientRequestId = Guid.NewGuid(),
            ChangeReason = "ثبت نرخ کنترل‌شده"
        };

        var created = await service.Create(createRequest, CancellationToken.None);
        var updatedRates = createRequest.Rates.Select(x =>
            x.CurrencyId == currencies.Cny.Id ? x with { UnitsPerUsd = 7.01m } : x).ToArray();
        var updated = await service.UpdateDraft(created.Id,
            new UpdateReportingFxRateSetRequest(
                Guid.NewGuid(), created.EffectiveDate, created.Version,
                "اصلاح نرخ کنترل‌شده", updatedRates), CancellationToken.None);
        var published = await service.Publish(updated.Id,
            new PublishReportingFxRateSetRequest(
                Guid.NewGuid(), updated.Version, "انتشار نرخ کنترل‌شده"), CancellationToken.None);

        await using var db = await Db();
        var audits = await db.OperationalAuditEvents.AsNoTracking()
            .Where(x => x.EntityType == "ReportingFxRateSet" && x.EntityId == created.Id)
            .OrderBy(x => x.Action)
            .ToListAsync();
        Assert.Equal(3, audits.Count);
        Assert.All(audits, audit =>
        {
            Assert.Equal(actor, audit.ActorId);
            Assert.Equal(correlationId, audit.CorrelationId);
            Assert.Equal("UI", audit.Source);
            Assert.Equal(DateTimeKind.Utc, audit.OccurredAtUtc.Kind);
            Assert.Equal(fixedUtc.UtcDateTime, audit.OccurredAtUtc);
            Assert.NotNull(audit.CurrentValuesJson);
        });

        var createAudit = Assert.Single(audits, x => x.Action == "CREATE");
        Assert.Null(createAudit.PreviousValuesJson);
        Assert.Equal(createRequest.ChangeReason, createAudit.Reason);

        var updateAudit = Assert.Single(audits, x => x.Action == "UPDATE_DRAFT");
        Assert.NotNull(updateAudit.PreviousValuesJson);
        Assert.Equal("اصلاح نرخ کنترل‌شده", updateAudit.Reason);
        using var previous = JsonDocument.Parse(updateAudit.PreviousValuesJson!);
        using var current = JsonDocument.Parse(updateAudit.CurrentValuesJson!);
        Assert.Equal(created.Version, JsonProperty(previous.RootElement, "Version").GetInt64());
        Assert.Equal(updated.Version, JsonProperty(current.RootElement, "Version").GetInt64());
        Assert.Equal(ReportingFxRateSetStatuses.Draft,
            JsonProperty(previous.RootElement, "Status").GetString());
        Assert.Contains("6.8", updateAudit.PreviousValuesJson!, StringComparison.Ordinal);
        Assert.Contains("7.01", updateAudit.CurrentValuesJson!, StringComparison.Ordinal);

        var publishAudit = Assert.Single(audits, x => x.Action == "PUBLISH");
        Assert.NotNull(publishAudit.PreviousValuesJson);
        Assert.Equal("انتشار نرخ کنترل‌شده", publishAudit.Reason);
        using var beforePublish = JsonDocument.Parse(publishAudit.PreviousValuesJson!);
        using var afterPublish = JsonDocument.Parse(publishAudit.CurrentValuesJson!);
        Assert.Equal(updated.Version, JsonProperty(beforePublish.RootElement, "Version").GetInt64());
        Assert.Equal(published.Version, JsonProperty(afterPublish.RootElement, "Version").GetInt64());
        Assert.Equal(ReportingFxRateSetStatuses.Published,
            JsonProperty(afterPublish.RootElement, "Status").GetString());
    }

    [Fact]
    public async Task Audit_storage_failure_rolls_back_the_rate_set_and_lines_atomically()
    {
        const string failureReason = "force-reporting-fx-audit-rollback-regression";
        var currencies = await SeedCurrencies();
        var request = ValidCreate(currencies) with
        {
            EffectiveDate = new DateOnly(2026, 9, 8),
            ClientRequestId = Guid.NewGuid(),
            ChangeReason = failureReason
        };
        await using (var db = await Db())
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE platform.operational_audit_events " +
                "DROP CONSTRAINT IF EXISTS ck_test_reject_reporting_fx_audit");
            await db.Database.ExecuteSqlRawAsync("""
                ALTER TABLE platform.operational_audit_events
                ADD CONSTRAINT ck_test_reject_reporting_fx_audit
                CHECK ("Reason" IS DISTINCT FROM 'force-reporting-fx-audit-rollback-regression')
                """);
        }

        try
        {
            var service = CreateService(
                "cash-flow-audit-tester", "cash-flow-audit-rollback",
                new DateTimeOffset(2026, 9, 7, 9, 0, 0, TimeSpan.Zero));
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                service.Create(request, CancellationToken.None));

            await using var verification = await Db();
            Assert.False(await verification.ReportingFxRateSets.AsNoTracking()
                .AnyAsync(x => x.ClientRequestId == request.ClientRequestId));
            Assert.False(await verification.ReportingFxRates.AsNoTracking()
                .AnyAsync(x => x.RateSet.ClientRequestId == request.ClientRequestId));
            Assert.False(await verification.OperationalAuditEvents.AsNoTracking()
                .AnyAsync(x => x.Reason == failureReason));
        }
        finally
        {
            await using var db = await Db();
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE platform.operational_audit_events " +
                "DROP CONSTRAINT IF EXISTS ck_test_reject_reporting_fx_audit");
        }
    }

    [Fact]
    public async Task PostgreSql_constraint_diagnostics_are_not_exposed_to_api_clients()
    {
        const string privateConstraint = "cash_flow_internal_secret_constraint";
        const string privateDiagnostic = "database-only reporting FX diagnostic";
        var currencies = await SeedCurrencies();
        await using (var db = await Db())
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE OR REPLACE FUNCTION cash_flow.test_reporting_fx_unique_violation()
                RETURNS trigger AS $$
                BEGIN
                    IF NEW."EffectiveDate" = DATE '2099-12-31' THEN
                        RAISE EXCEPTION 'database-only reporting FX diagnostic'
                            USING ERRCODE = '23505',
                                  CONSTRAINT = 'cash_flow_internal_secret_constraint';
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                DROP TRIGGER IF EXISTS trg_test_reporting_fx_unique_violation
                    ON cash_flow.reporting_fx_rate_sets;
                CREATE TRIGGER trg_test_reporting_fx_unique_violation
                    BEFORE INSERT ON cash_flow.reporting_fx_rate_sets
                    FOR EACH ROW EXECUTE FUNCTION cash_flow.test_reporting_fx_unique_violation();
                """);
        }

        HttpResponseMessage? response = null;
        try
        {
            response = await _client.PostAsJsonAsync(
                "/api/v1/cash-flow/reporting-rate-sets",
                ValidCreate(currencies) with
                {
                    EffectiveDate = new DateOnly(2099, 12, 31),
                    ClientRequestId = Guid.NewGuid()
                });
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal("RATE_SET_CONFLICT", await ProblemCode(response));
            using var problem = JsonDocument.Parse(body);
            Assert.Equal(
                "جدول نرخ هم‌زمان توسط درخواست دیگری تغییر کرده است. اطلاعات را دوباره بارگذاری کنید.",
                problem.RootElement.GetProperty("detail").GetString());
            Assert.False(problem.RootElement.TryGetProperty("details", out _));
            Assert.DoesNotContain(privateConstraint, body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(privateDiagnostic, body, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            response?.Dispose();
            await using var db = await Db();
            await db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER IF EXISTS trg_test_reporting_fx_unique_violation
                    ON cash_flow.reporting_fx_rate_sets;
                DROP FUNCTION IF EXISTS cash_flow.test_reporting_fx_unique_violation();
                """);
        }
    }

    [Fact]
    public async Task Units_per_usd_requires_a_json_string_and_preserves_twelve_decimal_places()
    {
        var currencies = await SeedCurrencies();
        var exact = 2_200_000.123456789012m;
        var numericJson = $$"""
            {
              "effectiveDate": "2026-09-03",
              "clientRequestId": "{{Guid.NewGuid()}}",
              "changeReason": "numeric token must be rejected",
              "rates": [
                { "currencyId": "{{currencies.Irr.Id}}", "unitsPerUsd": 2200000.123456789012 }
              ]
            }
            """;
        using var numericResponse = await _client.PostAsync(
            "/api/v1/cash-flow/reporting-rate-sets",
            new StringContent(numericJson, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, numericResponse.StatusCode);

        var stringJson = $$"""
            {
              "effectiveDate": "2026-09-03",
              "clientRequestId": "{{Guid.NewGuid()}}",
              "changeReason": "exact invariant string",
              "rates": [
                { "currencyId": "{{currencies.Irr.Id}}", "unitsPerUsd": "2200000.123456789012" }
              ]
            }
            """;
        using var stringResponse = await _client.PostAsync(
            "/api/v1/cash-flow/reporting-rate-sets",
            new StringContent(stringJson, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, stringResponse.StatusCode);
        Assert.Equal(exact, Rate(await Read(stringResponse), "IRR"));
    }

    [Fact]
    public async Task Validation_and_failed_publish_are_transactional_and_preserve_the_draft()
    {
        var currencies = await SeedCurrencies();
        var invalid = ValidCreate(currencies) with
        {
            Rates =
            [
                new(currencies.Usd.Id, 1m),
                new(currencies.Irr.Id, 2_200_000m),
                new(currencies.Cny.Id, 1.1234567890123m),
                new(Guid.NewGuid(), 0m)
            ]
        };
        var invalidResponse = await _client.PostAsJsonAsync(
            "/api/v1/cash-flow/reporting-rate-sets", invalid);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        await using (var db = await Db())
        {
            Assert.Equal(0, await db.ReportingFxRateSets.CountAsync());
            Assert.Equal(0, await db.ReportingFxRates.CountAsync());
        }

        var incomplete = ValidCreate(currencies) with
        {
            ClientRequestId = Guid.NewGuid(),
            Rates =
            [
                new(currencies.Irr.Id, 2_200_000m),
                new(currencies.Cny.Id, 6.8m)
            ]
        };
        var draft = await Read(await _client.PostAsJsonAsync(
            "/api/v1/cash-flow/reporting-rate-sets", incomplete));
        var publishResponse = await _client.PostAsJsonAsync(
            $"/api/v1/cash-flow/reporting-rate-sets/{draft.Id}/publish",
            new PublishReportingFxRateSetRequest(Guid.NewGuid(), draft.Version, "تلاش انتشار ناقص"));
        Assert.Equal(HttpStatusCode.BadRequest, publishResponse.StatusCode);

        var missingCode = await MissingCurrencyCode(publishResponse);
        Assert.Contains("EUR", missingCode);
        await using (var db = await Db())
        {
            var persisted = await db.ReportingFxRateSets.AsNoTracking()
                .SingleAsync(x => x.Id == draft.Id);
            Assert.Equal(ReportingFxRateSetStatuses.Draft, persisted.Status);
            Assert.Equal(1, persisted.Version);
            Assert.Null(persisted.PublishedAtUtc);
            Assert.Equal(2, await db.ReportingFxRates.CountAsync(x => x.RateSetId == draft.Id));
        }

        var reusedKey = incomplete with
        {
            EffectiveDate = incomplete.EffectiveDate.AddDays(1)
        };
        var reusedResponse = await _client.PostAsJsonAsync(
            "/api/v1/cash-flow/reporting-rate-sets", reusedKey);
        Assert.Equal(HttpStatusCode.Conflict, reusedResponse.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", await ProblemCode(reusedResponse));
    }

    [Fact]
    public async Task Database_guards_block_delete_and_rate_reparenting_from_published_sets()
    {
        var currencies = await SeedCurrencies();
        var published = await CreateAndPublish(currencies, new DateOnly(2026, 9, 1));
        var draft = await Read(await _client.PostAsJsonAsync(
            "/api/v1/cash-flow/reporting-rate-sets",
            ValidCreate(currencies) with
            {
                EffectiveDate = new DateOnly(2026, 9, 2),
                ClientRequestId = Guid.NewGuid()
            }));

        await using var db = await Db();
        var publishedLineId = await db.ReportingFxRates.AsNoTracking()
            .Where(x => x.RateSetId == published.Id).Select(x => x.Id).FirstAsync();
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE cash_flow.reporting_fx_rates SET \"RateSetId\" = {draft.Id} WHERE \"Id\" = {publishedLineId}"));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM cash_flow.reporting_fx_rate_sets WHERE \"Id\" = {published.Id}"));
    }

    private async Task<ReportingFxRateSetResponse> CreateAndPublish(
        CurrencyFixture currencies, DateOnly effectiveDate)
    {
        var draft = await Read(await _client.PostAsJsonAsync(
            "/api/v1/cash-flow/reporting-rate-sets",
            ValidCreate(currencies) with
            {
                EffectiveDate = effectiveDate,
                ClientRequestId = Guid.NewGuid()
            }));
        return await Read(await _client.PostAsJsonAsync(
            $"/api/v1/cash-flow/reporting-rate-sets/{draft.Id}/publish",
            new PublishReportingFxRateSetRequest(Guid.NewGuid(), draft.Version, "انتشار تست")));
    }

    private static CreateReportingFxRateSetRequest ValidCreate(CurrencyFixture currencies) => new(
        new DateOnly(2026, 9, 1),
        Guid.NewGuid(),
        "نرخ روزانه خزانه‌داری",
        currencies.AllNonUsd
            .OrderBy(x => x.NormalizedCode)
            .Select(x => new ReportingFxRateInput(x.Id, x.NormalizedCode switch
            {
                "IRR" => 2_200_000m,
                "CNY" => 6.8m,
                "EUR" => 0.86m,
                "AED" => 3.6725m,
                _ => 1m
            }))
            .ToArray());

    private async Task<CurrencyFixture> SeedCurrencies()
    {
        await using var db = await Db();
        var usd = await GetOrAddCurrency(db, "USD", "دلار آمریکا", "$", 2);
        var irr = await GetOrAddCurrency(db, "IRR", "ریال ایران", "﷼", 0);
        var cny = await GetOrAddCurrency(db, "CNY", "یوان چین", "¥", 2);
        var eur = await GetOrAddCurrency(db, "EUR", "یورو", "€", 2);
        var aed = await GetOrAddCurrency(db, "AED", "درهم امارات", "د.إ", 2);
        usd.SetActive(true);
        aed.SetActive(false);
        await db.SaveChangesAsync();
        var allNonUsd = await db.Currencies.AsNoTracking()
            .Where(x => x.Id != usd.Id)
            .OrderBy(x => x.Code)
            .ToArrayAsync();
        return new(usd, irr, cny, eur, aed, allNonUsd);
    }

    private static async Task<Currency> GetOrAddCurrency(
        HadronDbContext db, string code, string nameFa, string symbol, int precision)
    {
        var existing = await db.Currencies.SingleOrDefaultAsync(x => x.NormalizedCode == code);
        if (existing is not null) return existing;
        var currency = new Currency(code, code, nameFa, code, symbol, precision);
        db.Currencies.Add(currency);
        await db.SaveChangesAsync();
        return currency;
    }

    private static decimal Rate(ReportingFxRateSetResponse rateSet, string currencyCode) =>
        rateSet.Rates.Single(x => x.CurrencyCode == currencyCode).UnitsPerUsd;

    private static async Task<ReportingFxRateSetResponse> Read(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<ReportingFxRateSetResponse>()
        ?? throw new InvalidOperationException("FX response body is missing.");

    private static async Task<string?> ProblemCode(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static async Task<string> MissingCurrencyCode(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("errors").GetProperty("rates")[0].GetString() ?? string.Empty;
    }

    private async Task<HadronDbContext> Db() =>
        await _factory.Services.GetRequiredService<IDbContextFactory<HadronDbContext>>()
            .CreateDbContextAsync();

    private ReportingFxRateService CreateService(
        string actor, string correlationId, DateTimeOffset utcNow)
    {
        var context = new DefaultHttpContext { TraceIdentifier = correlationId };
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, actor)], "ReportingFxAuditTest"));
        return new ReportingFxRateService(
            _factory.Services.GetRequiredService<IDbContextFactory<HadronDbContext>>(),
            new HttpContextAccessor { HttpContext = context },
            new FixedTimeProvider(utcNow));
    }

    private static JsonElement JsonProperty(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        throw new Xunit.Sdk.XunitException($"Audit JSON property '{name}' was not found.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed record CurrencyFixture(
        Currency Usd,
        Currency Irr,
        Currency Cny,
        Currency Eur,
        Currency Aed,
        IReadOnlyList<Currency> AllNonUsd);
}
