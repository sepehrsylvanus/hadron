using System.Net;
using System.Net.Http.Json;
using Hadron.Erp.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Hadron.Erp.IntegrationTests;

public sealed class SessionRestartContinuityTests
{
    [Fact]
    public async Task Authenticated_cookie_and_server_session_survive_application_restart()
    {
        var database = $"hadron_erp_test_{Guid.NewGuid():N}";
        var keysPath = Path.Combine(Path.GetTempPath(), $"hadron-session-keys-{Guid.NewGuid():N}");
        TestDatabaseSafety.EnsureSafeDatabaseName(database);
        await CreateDatabase(database);

        try
        {
            string sessionCookieHeader;
            using (var firstHost = new RestartableApiFactory(database, keysPath))
            {
                await using (var scope = firstHost.Services.CreateAsyncScope())
                    await scope.ServiceProvider.GetRequiredService<HadronDbContext>().Database.MigrateAsync();

                using var client = firstHost.CreateClient();
                using var bootstrap = await Bootstrap(client);
                Assert.Equal(HttpStatusCode.Created, bootstrap.StatusCode);

                using var login = await Login(client);
                Assert.Equal(HttpStatusCode.OK, login.StatusCode);
                sessionCookieHeader = string.Join("; ", login.Headers.GetValues("Set-Cookie")
                    .Where(value => value.StartsWith("hadron.erp.session", StringComparison.Ordinal))
                    .Select(value => value.Split(';', 2)[0]));
            }

            using (var restartedHost = new RestartableApiFactory(database, keysPath))
            using (var restartedClient = restartedHost.CreateClient(
                       new WebApplicationFactoryClientOptions { HandleCookies = false }))
            using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me"))
            {
                var cookieContext = new DefaultHttpContext();
                cookieContext.Request.Headers.Cookie = sessionCookieHeader;
                var cookieValue = new ChunkingCookieManager()
                    .GetRequestCookie(cookieContext, "hadron.erp.session");
                Assert.NotNull(cookieValue);
                var cookieOptions = restartedHost.Services
                    .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                    .Get(IdentityConstants.ApplicationScheme);
                Assert.NotNull(cookieOptions.TicketDataFormat.Unprotect(cookieValue));
                request.Headers.TryAddWithoutValidation("Cookie", sessionCookieHeader);
                using var currentUser = await restartedClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, currentUser.StatusCode);
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await DropDatabase(database);
            if (Directory.Exists(keysPath)) Directory.Delete(keysPath, recursive: true);
        }
    }

    private static async Task<HttpResponseMessage> Bootstrap(HttpClient client)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent("restart-admin"), "userName" },
            { new StringContent("RestartPass!234"), "temporaryPassword" },
            { new StringContent("Restart"), "firstName" },
            { new StringContent("Admin"), "lastName" },
            { new StringContent("restart-admin@example.test"), "emailAddress" },
            { new StringContent("09120000000"), "cellphoneNumber" },
            { new StringContent("true"), "isActive" },
            { new StringContent("واحد آزمون پایداری"), "organizationalUnitNameFa" },
            { new StringContent("Reliability Test Unit"), "organizationalUnitNameEn" }
        };
        var photo = new ByteArrayContent([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
        photo.Headers.ContentType = new("image/png");
        form.Add(photo, "photo", "person.png");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/bootstrap") { Content = form };
        request.Headers.Add("X-Hadron-Bootstrap-Token", RestartableApiFactory.BootstrapToken);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> Login(HttpClient client)
    {
        var csrf = await (await client.GetAsync("/api/auth/csrf"))
            .Content.ReadFromJsonAsync<CsrfContract>();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { userName = "restart-admin", password = "RestartPass!234" })
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf!.Token);
        return await client.SendAsync(request);
    }

    private static async Task CreateDatabase(string database)
    {
        await using var connection = new NpgsqlConnection(Connection("postgres"));
        await connection.OpenAsync();
        await new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection).ExecuteNonQueryAsync();
    }

    private static async Task DropDatabase(string database)
    {
        TestDatabaseSafety.EnsureSafeDatabaseName(database);
        await using var connection = new NpgsqlConnection(Connection("postgres"));
        await connection.OpenAsync();
        await new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", connection)
            .ExecuteNonQueryAsync();
    }

    private static string Connection(string database) => new NpgsqlConnectionStringBuilder
    {
        Host = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost",
        Port = int.Parse(Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432"),
        Database = database,
        Username = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "hadron_app",
        Password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ??
                   throw new InvalidOperationException("POSTGRES_PASSWORD is required.")
    }.ConnectionString;

    private sealed record CsrfContract(string Token);

    private sealed class RestartableApiFactory(string database, string keysPath)
        : WebApplicationFactory<Program>
    {
        public const string BootstrapToken = "restart-bootstrap-test-token-2026";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("POSTGRES_DB", database);
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["POSTGRES_DB"] = database,
                    ["IDENTITY_BOOTSTRAP_TOKEN"] = BootstrapToken,
                    ["Identity:UseInMemoryPhotoStore"] = "true",
                    ["Security:DataProtectionKeysPath"] = keysPath,
                    ["MrpAutomation:Enabled"] = "false"
                }));
        }
    }
}
