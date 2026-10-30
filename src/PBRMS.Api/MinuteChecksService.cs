using PBRMS.Data;

namespace PBRMS.Api;

/// <summary>
/// Runs the checks that depend on the clock, once a minute while the API is running: has
/// a production gone overtime, and has the daily roster fallen due with crew undecided?
/// On IIS the site must be set to Always Running, or this stops when the site goes idle.
/// </summary>
public class MinuteChecksService(IServiceScopeFactory scopes, ILogger<MinuteChecksService> logger) : BackgroundService
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
                await scope.ServiceProvider.GetRequiredService<CrewDeadlineWatcher>().CheckAsync(DateTimeOffset.UtcNow);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // A database blip must not stop the checks. The next tick tries again,
                // and nobody has been marked as told, so nothing is missed.
                logger.LogError(error, "The minute checks failed.");
            }
        }
    }
}
