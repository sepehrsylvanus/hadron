using System.Net;
using System.Text.RegularExpressions;
using Hadron.Erp.Api.Infrastructure;
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
