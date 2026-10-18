using Hadron.Erp.Api.Platform.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Hadron.Erp.IntegrationTests;

public sealed class DataProtectionPersistenceTests
{
    [Fact]
    public void Production_hosts_share_cookie_protection_keys_through_the_configured_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hadron-data-protection-{Guid.NewGuid():N}");
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DataProtectionPersistence.ConfigurationKey] = "keys"
            }).Build();
            var environment = new TestHostEnvironment(root);

            string protectedValue;
            using (var firstHost = Services(configuration, environment))
            {
                protectedValue = firstHost.GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector("Hadron.Session.Persistence.Test")
                    .Protect("session-remains-valid");
            }

            using (var restartedHost = Services(configuration, environment))
            {
                var unprotected = restartedHost.GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector("Hadron.Session.Persistence.Test")
                    .Unprotect(protectedValue);
                Assert.Equal("session-remains-valid", unprotected);
            }

            Assert.NotEmpty(Directory.EnumerateFiles(Path.Combine(root, "keys"), "*.xml"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static ServiceProvider Services(IConfiguration configuration, IHostEnvironment environment)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHadronDataProtection(configuration, environment);
        return services.BuildServiceProvider();
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Hadron.Erp.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
