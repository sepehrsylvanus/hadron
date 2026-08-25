using System.Net;
using System.Text.RegularExpressions;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.OrderPlanning.Application;
using Hadron.Erp.Api.Modules.Planning.Application;
using Hadron.Erp.Api.Modules.PurchaseSupply.Application;
using Hadron.Erp.Api.Platform.Audit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Hadron.Erp.IntegrationTests;

public sealed class FoundationHealthTests : IClassFixture<FoundationApiFactory>
{
    private readonly HttpClient _client;

    public FoundationHealthTests(FoundationApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Liveness_endpoint_reports_healthy()
    {
        using var response = await _client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Readiness_endpoint_reaches_postgresql()
    {
        using var response = await _client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}

public sealed class FoundationApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string DatabaseName { get; } = $"hadron_erp_test_{Guid.NewGuid():N}";
    public RecordingAuditEventSink AuditSink { get; } = new();
    public MutablePlanningCalendar PlanningCalendar { get; } = new();
    public TestOrderPlanningFaultInjector OrderPlanningFaultInjector { get; } = new();
    public TestPurchaseReceiptFaultInjector PurchaseReceiptFaultInjector { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders().AddConsole());
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["POSTGRES_DB"] = DatabaseName
            }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDbContextFactory<HadronDbContext>>();
            services.AddDbContextFactory<HadronDbContext>(options =>
                options.UseNpgsql(TestDatabaseConnectionString(DatabaseName)));
            services.RemoveAll<IAuditEventSink>();
            services.AddSingleton<IAuditEventSink>(AuditSink);
            services.RemoveAll<IPlanningCalendar>();
            services.AddSingleton<IPlanningCalendar>(PlanningCalendar);
            services.RemoveAll<IOrderPlanningGenerationFaultInjector>();
            services.AddSingleton<IOrderPlanningGenerationFaultInjector>(OrderPlanningFaultInjector);
            services.RemoveAll<IPurchaseReceiptFaultInjector>();
            services.AddSingleton<IPurchaseReceiptFaultInjector>(PurchaseReceiptFaultInjector);
        });
    }

    async Task IAsyncLifetime.InitializeAsync()
    {
        TestDatabaseSafety.EnsureSafeDatabaseName(DatabaseName);
        await using var connection = new NpgsqlConnection(TestDatabaseConnectionString("postgres"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{DatabaseName}\"", connection);
        await command.ExecuteNonQueryAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        Dispose();
        NpgsqlConnection.ClearAllPools();
        TestDatabaseSafety.EnsureSafeDatabaseName(DatabaseName);
        await using var connection = new NpgsqlConnection(TestDatabaseConnectionString("postgres"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static string TestDatabaseConnectionString(string database) => new NpgsqlConnectionStringBuilder
    {
        Host = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost",
        Port = int.Parse(Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432"),
        Database = database,
        Username = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "hadron_app",
        Password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD")
            ?? throw new InvalidOperationException("POSTGRES_PASSWORD is required for integration tests."),
        ApplicationName = "Hadron.Erp.IntegrationTests"
    }.ConnectionString;
}

public sealed class TestPurchaseReceiptFaultInjector : IPurchaseReceiptFaultInjector
{
    public bool FailAfterReceipt { get; set; }
    public bool FailAfterInventory { get; set; }
    public Task AfterReceiptBeforeInventory(CancellationToken ct)
    {
        if (FailAfterReceipt) throw new InvalidOperationException("Injected receipt-before-inventory failure");
        return Task.CompletedTask;
    }
    public Task AfterInventoryBeforeReceiptCommit(CancellationToken ct)
    {
        if (FailAfterInventory) throw new InvalidOperationException("Injected receipt failure");
        return Task.CompletedTask;
    }
    public void Reset()
    {
        FailAfterReceipt = false;
        FailAfterInventory = false;
    }
}

public sealed class MutablePlanningCalendar : IPlanningCalendar
{
    private readonly PlanningCalendar _inner = new(TimeProvider.System);
    public DateOnly? TodayOverride { get; set; }
    public DateOnly TehranToday => TodayOverride ?? _inner.TehranToday;
    public (DateOnly StartDate, DateOnly EndDate) GetPeriodBounds(int jalaliYear, int jalaliMonth) =>
        _inner.GetPeriodBounds(jalaliYear, jalaliMonth);
    public (int JalaliYear, int JalaliMonth) GetJalaliPeriod(DateOnly gregorianDate) =>
        _inner.GetJalaliPeriod(gregorianDate);
}

public sealed class TestOrderPlanningFaultInjector : IOrderPlanningGenerationFaultInjector
{
    private readonly object _sync = new();
    private TaskCompletionSource<Guid>? _pauseEntered;
    private TaskCompletionSource? _pauseRelease;

    public bool FailAfterCandidateCapture { get; set; }

    public Task<Guid> PauseNext()
    {
        lock (_sync)
        {
            _pauseEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _pauseRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return _pauseEntered.Task;
        }
    }

    public void ReleasePaused()
    {
        lock (_sync)
        {
            var release = _pauseRelease;
            _pauseRelease = null;
            release?.TrySetResult();
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _pauseRelease?.TrySetResult();
            _pauseEntered = null;
            _pauseRelease = null;
            FailAfterCandidateCapture = false;
        }
    }

    public Task AfterCandidateCapture(Guid generationId, CancellationToken cancellationToken)
    {
        if (FailAfterCandidateCapture) throw new InvalidOperationException("Injected generation failure");
        lock (_sync)
        {
            if (_pauseEntered is null || _pauseRelease is null) return Task.CompletedTask;
            var entered = _pauseEntered;
            var release = _pauseRelease;
            _pauseEntered = null;
            entered.TrySetResult(generationId);
            return release.Task.WaitAsync(cancellationToken);
        }
    }
}

public static partial class TestDatabaseSafety
{
    [GeneratedRegex("^hadron_erp_test_[a-f0-9]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeDatabasePattern();

    public static void EnsureSafeDatabaseName(string databaseName)
    {
        if (!SafeDatabasePattern().IsMatch(databaseName))
        {
            throw new InvalidOperationException(
                $"Destructive integration-test operation refused for non-test database '{databaseName}'.");
        }
    }
}

public sealed class RecordingAuditEventSink : IAuditEventSink
{
    private readonly List<AuditEvent> _events = [];
    private readonly object _gate = new();
    public bool ThrowOnWrite { get; set; }

    public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        if (ThrowOnWrite)
        {
            throw new InvalidOperationException("Simulated audit sink failure.");
        }

        lock (_gate) _events.Add(auditEvent);
        return Task.CompletedTask;
    }

    public IReadOnlyList<AuditEvent> Snapshot()
    {
        lock (_gate) return _events.ToArray();
    }

    public void Clear()
    {
        lock (_gate) _events.Clear();
        ThrowOnWrite = false;
    }
}
