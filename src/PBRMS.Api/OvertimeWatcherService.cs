using PBRMS.Data;

namespace PBRMS.Api;

/// <summary>
/// Runs the overtime check every minute while the API is running. The check itself is
/// in <see cref="OvertimeWatcher"/>.
/// On IIS the site must be set to Always Running, or this stops when the site goes idle.
/// </summary>
public class OvertimeWatcherService(IServiceScopeFactory scopes, ILogger<OvertimeWatcherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<OvertimeWatcher>().CheckAsync(DateTimeOffset.UtcNow);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // A database blip must not stop the checks. The next tick tries again,
                // and nobody has been marked as told, so nothing is missed.
                logger.LogError(error, "The overtime check failed.");
            }
        }
    }
}
