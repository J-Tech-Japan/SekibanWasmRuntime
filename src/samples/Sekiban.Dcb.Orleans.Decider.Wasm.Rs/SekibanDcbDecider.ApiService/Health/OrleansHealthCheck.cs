using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SekibanDcbDecider.ApiService.Health;

/// <summary>
///     Orleans Silo health check
///     Liveness: Verifies that the Orleans GrainFactory is available from DI,
///     indicating that the Orleans infrastructure has started.
/// </summary>
public class OrleansHealthCheck(IGrainFactory grainFactory, ILogger<OrleansHealthCheck> logger) : IHealthCheck
{
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly ILogger<OrleansHealthCheck> _logger = logger;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (_grainFactory is null)
            {
                _logger.LogWarning("Orleans GrainFactory is not available");
                return Task.FromResult(HealthCheckResult.Unhealthy("Orleans GrainFactory is not available"));
            }

            return Task.FromResult(HealthCheckResult.Healthy("Orleans GrainFactory is available"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Orleans health check failed");
            return Task.FromResult(HealthCheckResult.Unhealthy($"Orleans health check failed: {ex.Message}", ex));
        }
    }
}
