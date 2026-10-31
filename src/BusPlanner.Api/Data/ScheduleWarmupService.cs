using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BusPlanner.Api.Data;

/// <summary>Starts loading the shared static schedule while the Functions worker starts.</summary>
public sealed class ScheduleWarmupService(
    TransitDataService transitData,
    ILogger<ScheduleWarmupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var result = await transitData.GetScheduleAsync(stoppingToken);
            logger.LogInformation(
                "Static schedule warm-up completed. Download: {DownloadMs:F0} ms; parse: {ParseMs:F0} ms.",
                result.DownloadMs,
                result.ParseMs);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The worker is shutting down; the next instance will warm its own cache.
        }
        catch (Exception exception)
        {
            // Startup remains available; the first journey request retries the shared load.
            logger.LogWarning(exception, "Static schedule warm-up failed. A journey request will retry it.");
        }
    }
}
