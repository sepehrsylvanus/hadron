using Npgsql;

namespace Hadron.Erp.Api.Infrastructure;

internal static class DatabaseConnectionString
{
    public static string Create(IConfiguration configuration)
    {
        var password = configuration["POSTGRES_PASSWORD"];
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "POSTGRES_PASSWORD must be supplied through environment configuration.");
        }

        var portValue = configuration["POSTGRES_PORT"] ?? "5432";
        if (!int.TryParse(portValue, out var port))
        {
            throw new InvalidOperationException("POSTGRES_PORT must be a valid integer.");
        }

        return new NpgsqlConnectionStringBuilder
        {
            Host = configuration["POSTGRES_HOST"] ?? "localhost",
            Port = port,
            Database = configuration["POSTGRES_DB"] ?? "hadron_erp",
            Username = configuration["POSTGRES_USER"] ?? "hadron_app",
            Password = password,
            ApplicationName = "Hadron.Erp.Api",
            IncludeErrorDetail = configuration.GetValue("Database:IncludeErrorDetail", false)
        }.ConnectionString;
    }
}
