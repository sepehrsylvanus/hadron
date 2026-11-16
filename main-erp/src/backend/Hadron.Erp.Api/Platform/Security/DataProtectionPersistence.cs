using Microsoft.AspNetCore.DataProtection;

namespace Hadron.Erp.Api.Platform.Security;

public static class DataProtectionPersistence
{
    public const string ConfigurationKey = "Security:DataProtectionKeysPath";
    public const string EnvironmentVariableKey = "DATA_PROTECTION_KEYS_PATH";

    public static IServiceCollection AddHadronDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var dataProtection = services.AddDataProtection().SetApplicationName("Hadron.Erp");
        if (environment.IsEnvironment("Testing") || environment.IsEnvironment("IdentityTesting"))
            return services;

        var directory = ResolveKeysDirectory(configuration, environment);
        Directory.CreateDirectory(directory.FullName);
        dataProtection.PersistKeysToFileSystem(directory);
        return services;
    }

    public static DirectoryInfo ResolveKeysDirectory(IConfiguration configuration, IHostEnvironment environment)
    {
        var configuredPath = configuration[ConfigurationKey] ?? configuration[EnvironmentVariableKey];
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var expandedPath = Environment.ExpandEnvironmentVariables(configuredPath.Trim());
            var absolutePath = Path.IsPathRooted(expandedPath)
                ? Path.GetFullPath(expandedPath)
                : Path.GetFullPath(expandedPath, environment.ContentRootPath);
            return new DirectoryInfo(absolutePath);
        }

        if (environment.IsDevelopment())
        {
            var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localApplicationData))
                return new DirectoryInfo(Path.Combine(localApplicationData, "HadronErp", "DataProtectionKeys"));
        }

        return new DirectoryInfo(Path.Combine(environment.ContentRootPath, "data-protection-keys"));
    }
}
