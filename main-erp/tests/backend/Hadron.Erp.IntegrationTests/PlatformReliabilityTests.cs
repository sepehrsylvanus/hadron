using System.Text.Json;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Platform.Audit;
using Hadron.Erp.Api.Platform.Errors;
using Hadron.Erp.Api.Platform.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Hadron.Erp.Api.Platform.Security;
using System.Net;
using System.Net.Http.Json;
using Npgsql;

namespace Hadron.Erp.IntegrationTests;

public sealed class PlatformReliabilityTests : IClassFixture<FoundationApiFactory>
{
    private readonly FoundationApiFactory _factory;

    public PlatformReliabilityTests(FoundationApiFactory factory) => _factory = factory;

    [Fact]
    public async Task PostgreSql_audit_sink_persists_searchable_redacted_and_bounded_payload()
    {
        await using (var migrationContext = await Context())
            await migrationContext.Database.MigrateAsync();

        var sink = ActivatorUtilities.CreateInstance<PostgresAuditEventSink>(_factory.Services);
        var entityId = Guid.NewGuid();
        var longText = new string('x', 30_000);
        await sink.WriteAsync(new AuditEvent(
            "Product",
            entityId,
            "UPDATE",
            DateTime.UtcNow,
            "test-actor",
            "trace-audit-123",
            new Dictionary<string, object?>
            {
                ["name"] = "before",
                ["password"] = "must-not-be-stored",
                ["nested"] = new Dictionary<string, object?> { ["accessToken"] = "also-secret" }
            },
            new Dictionary<string, object?> { ["description"] = longText },
            "token=reason-secret scheduled correction",
            "API"), CancellationToken.None);

        await using var db = await Context();
        var stored = await db.OperationalAuditEvents.AsNoTracking()
            .SingleAsync(x => x.EntityId == entityId && x.Action == "UPDATE");

        Assert.Equal("Product", stored.EntityType);
        Assert.Equal("trace-audit-123", stored.CorrelationId);
        Assert.Equal("API", stored.Source);
        Assert.Contains("[REDACTED]", stored.PreviousValuesJson);
        Assert.DoesNotContain("must-not-be-stored", stored.PreviousValuesJson);
        Assert.DoesNotContain("also-secret", stored.PreviousValuesJson);
        Assert.DoesNotContain("reason-secret", stored.Reason);
        Assert.NotNull(stored.CurrentValuesJson);
        Assert.True(stored.CurrentValuesJson!.Length <= 16_000);
        using var _ = JsonDocument.Parse(stored.CurrentValuesJson);

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var delete = new NpgsqlCommand(
            "DELETE FROM platform.operational_audit_events WHERE \"Id\" = @id", connection);
        delete.Parameters.AddWithValue("id", stored.Id);
        var databaseError = await Assert.ThrowsAsync<PostgresException>(() => delete.ExecuteNonQueryAsync());
        Assert.Equal("23514", databaseError.SqlState);
    }

    [Fact]
    public async Task Exception_handler_returns_safe_localized_problem_details_with_trace_id()
    {
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "trace-safe-500";
        context.Request.Path = "/api/test";
        context.Request.Headers.AcceptLanguage = "fa-IR";
        context.Response.Body = new MemoryStream();
        var handler = new HadronExceptionHandler(NullLogger<HadronExceptionHandler>.Instance);

        var handled = await handler.TryHandleAsync(context,
            new InvalidOperationException("database-password=TOP-SECRET raw stack detail"),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("INTERNAL_ERROR", json.RootElement.GetProperty("code").GetString());
        Assert.Equal("trace-safe-500", json.RootElement.GetProperty("traceId").GetString());
        Assert.Equal("خطای داخلی سامانه", json.RootElement.GetProperty("title").GetString());
        var body = json.RootElement.GetRawText();
        Assert.DoesNotContain("TOP-SECRET", body);
        Assert.DoesNotContain("InvalidOperationException", body);
    }

    [Fact]
    public async Task Exception_handler_classifies_malformed_requests_without_leaking_parser_details()
    {
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "trace-safe-400";
        context.Request.Path = "/api/test";
        context.Request.Headers.AcceptLanguage = "en";
        context.Response.Body = new MemoryStream();
        var handler = new HadronExceptionHandler(NullLogger<HadronExceptionHandler>.Instance);

        await handler.TryHandleAsync(context, new JsonException("raw parser detail"), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("INVALID_REQUEST", json.RootElement.GetProperty("code").GetString());
        Assert.Equal("Invalid request", json.RootElement.GetProperty("title").GetString());
        Assert.DoesNotContain("raw parser detail", json.RootElement.GetRawText());
    }

    [Fact]
    public async Task Operational_audit_endpoint_requires_permission_and_pages_without_exposing_payloads()
    {
        await using (var migrationContext = await Context())
            await migrationContext.Database.MigrateAsync();

        var sink = ActivatorUtilities.CreateInstance<PostgresAuditEventSink>(_factory.Services);
        var correlationId = $"audit-page-{Guid.NewGuid():N}";
        for (var index = 0; index < 3; index++)
        {
            await sink.WriteAsync(new AuditEvent("Product", Guid.NewGuid(), "UPDATE", DateTime.UtcNow,
                "test-actor", correlationId,
                new Dictionary<string, object?> { ["password"] = $"secret-{index}" },
                new Dictionary<string, object?> { ["value"] = index }, null), CancellationToken.None);
        }

        using var deniedClient = _factory.CreateClient();
        deniedClient.DefaultRequestHeaders.Add("X-Hadron-Test-Permissions", Permissions.IdentityUserView);
        using var denied = await deniedClient.GetAsync($"/api/admin/operational-audit?correlationId={correlationId}");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var authorizedClient = _factory.CreateClient();
        authorizedClient.DefaultRequestHeaders.Add("X-Hadron-Test-Permissions", Permissions.IdentitySecurityAuditView);
        using var response = await authorizedClient.GetAsync(
            $"/api/admin/operational-audit?search=test-actor&entityType=product&action=update&correlationId={correlationId}&page=1&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(3, json.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("items").GetArrayLength());
        var body = json.RootElement.GetRawText();
        Assert.DoesNotContain("previousValuesJson", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("currentValuesJson", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Anonymous_login_is_rate_limited_with_safe_retry_contract()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("fa-IR");
        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt < 11; attempt++)
        {
            response?.Dispose();
            response = await client.PostAsJsonAsync("/api/auth/login",
                new { userName = $"missing-{Guid.NewGuid():N}", password = "Wrong-password-1!" });
        }

        using (response)
        {
            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            Assert.True(response.Headers.RetryAfter is not null || response.Headers.Contains("Retry-After"));
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("RATE_LIMITED", json.RootElement.GetProperty("code").GetString());
            Assert.True(json.RootElement.TryGetProperty("traceId", out _));
        }
    }

    [Fact]
    public async Task Readiness_fails_closed_when_required_object_storage_is_unavailable_while_liveness_remains_healthy()
    {
        using var unavailableStorageFactory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IProfilePhotoStoreHealthProbe>();
                services.AddSingleton<IProfilePhotoStoreHealthProbe>(new UnavailablePhotoStoreHealthProbe());
            }));
        using var client = unavailableStorageFactory.CreateClient();

        using var readiness = await client.GetAsync("/health/ready");
        using var liveness = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);
        Assert.Equal("Unhealthy", await readiness.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
        Assert.Equal("Healthy", await liveness.Content.ReadAsStringAsync());
    }

    private Task<HadronDbContext> Context() =>
        _factory.Services.GetRequiredService<IDbContextFactory<HadronDbContext>>().CreateDbContextAsync();

    private sealed class UnavailablePhotoStoreHealthProbe : IProfilePhotoStoreHealthProbe
    {
        public Task CheckAvailability(CancellationToken ct) =>
            Task.FromException(new IOException("simulated object storage outage"));
    }
}
