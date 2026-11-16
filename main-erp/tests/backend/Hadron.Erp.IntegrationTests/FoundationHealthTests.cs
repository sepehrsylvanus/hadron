using System.Net;
using System.Text.RegularExpressions;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Collaboration.Application;
using Hadron.Erp.Api.Modules.Identity.Domain;
using Hadron.Erp.Api.Modules.OrderPlanning.Application;
using Hadron.Erp.Api.Modules.Planning.Application;
using Hadron.Erp.Api.Modules.PurchaseSupply.Application;
using Hadron.Erp.Api.Platform.Audit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
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
    public TestMeetingMediaGateway MeetingMediaGateway { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("POSTGRES_DB", DatabaseName);
        builder.ConfigureLogging(logging => logging.ClearProviders().AddConsole().SetMinimumLevel(LogLevel.Warning));
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["POSTGRES_DB"] = DatabaseName
            }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDataProtectionProvider>();
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
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
            services.RemoveAll<IMeetingMediaGateway>();
            services.AddSingleton<IMeetingMediaGateway>(MeetingMediaGateway);
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

public sealed class TestMeetingMediaGateway : IMeetingMediaGateway
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<MeetingMediaTokenDescriptor> tokens = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<(string RoomName, string Identity)> removals = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> endedRooms = new();

    public bool Available { get; set; } = true;
    public string? UnavailabilityReason => Available ? null : "TEST_PROVIDER_UNAVAILABLE";
    public string ServerUrl => "wss://meeting.test.invalid";
    public MeetingRoomEndResult EndResult { get; set; } = MeetingRoomEndResult.Ended();
    public MeetingParticipantRemoveResult RemoveResult { get; set; } = MeetingParticipantRemoveResult.Removed();
    public IReadOnlyList<MeetingMediaTokenDescriptor> Tokens => tokens.ToArray();
    public IReadOnlyList<(string RoomName, string Identity)> Removals => removals.ToArray();
    public IReadOnlyList<string> EndedRooms => endedRooms.ToArray();

    public string CreateJoinToken(MeetingMediaTokenDescriptor descriptor)
    {
        if (!Available) throw new InvalidOperationException(UnavailabilityReason);
        tokens.Enqueue(descriptor);
        return $"meeting-test-token:{descriptor.ParticipantIdentity}";
    }

    public Task<MeetingRoomEndResult> EndRoomAsync(string roomName, CancellationToken cancellationToken)
    {
        endedRooms.Enqueue(roomName);
        return Task.FromResult(EndResult);
    }

    public Task<MeetingParticipantRemoveResult> RemoveParticipantAsync(string roomName,
        string participantIdentity, CancellationToken cancellationToken)
    {
        removals.Enqueue((roomName, participantIdentity));
        return Task.FromResult(RemoveResult);
    }

    public void Reset()
    {
        Available = true;
        EndResult = MeetingRoomEndResult.Ended();
        RemoveResult = MeetingParticipantRemoveResult.Removed();
        while (tokens.TryDequeue(out _)) { }
        while (removals.TryDequeue(out _)) { }
        while (endedRooms.TryDequeue(out _)) { }
    }
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
    public DateOnly GetGregorianDate(int jalaliYear, int jalaliMonth, int jalaliDay) =>
        _inner.GetGregorianDate(jalaliYear, jalaliMonth, jalaliDay);
    public (int JalaliYear, int JalaliMonth, int JalaliDay) GetJalaliDate(DateOnly gregorianDate) =>
        _inner.GetJalaliDate(gregorianDate);
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

public static class IntegrationTestIdentity
{
    public static async Task<Guid> EnsureActiveWarehouseResponsible(HadronDbContext db,
        string discriminator)
    {
        var normalizedUserName = $"WAREHOUSE-{discriminator}".ToUpperInvariant();
        var existing = await db.Users.AsNoTracking()
            .Where(x => x.NormalizedUserName == normalizedUserName)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync();
        if (existing.HasValue) return existing.Value;

        var unit = new OrganizationalUnit($"واحد تست {discriminator}", $"Test unit {discriminator}");
        var now = DateTime.UtcNow;
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"warehouse-{discriminator}",
            NormalizedUserName = normalizedUserName,
            Email = $"warehouse-{discriminator}@example.test",
            NormalizedEmail = $"WAREHOUSE-{discriminator}@EXAMPLE.TEST".ToUpperInvariant(),
            EmailConfirmed = true,
            FirstName = "Warehouse",
            LastName = "Responsible",
            PhoneNumber = "+980000000000",
            PhoneNumberConfirmed = true,
            OrganizationalUnitId = unit.Id,
            IsActive = true,
            MustChangePassword = false,
            AccessVersion = 1,
            ProfilePhotoObjectKey = $"tests/{discriminator}.png",
            ProfilePhotoContentType = "image/png",
            ProfilePhotoSize = 1,
            ProfilePhotoSha256 = new string('0', 64),
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.OrganizationalUnits.Add(unit);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
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
