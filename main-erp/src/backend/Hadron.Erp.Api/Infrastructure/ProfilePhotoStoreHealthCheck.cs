using Hadron.Erp.Api.Platform.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Hadron.Erp.Api.Infrastructure;

internal sealed class ProfilePhotoStoreHealthCheck(
    IProfilePhotoStoreHealthProbe profilePhotoStore) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await profilePhotoStore.CheckAvailability(cancellationToken);
            return HealthCheckResult.Healthy("Personnel-photo object storage is available.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "Personnel-photo object storage is unavailable.", exception);
        }
    }
}
