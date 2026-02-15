namespace PeopleMapPlus.Addon;

public sealed class OneDriveSyncWorker : BackgroundService
{
    private readonly OneDriveSyncOrchestrator _orchestrator;
    private readonly AddonOptionsProvider _optionsProvider;
    private readonly ILogger<OneDriveSyncWorker> _logger;

    public OneDriveSyncWorker(
        OneDriveSyncOrchestrator orchestrator,
        AddonOptionsProvider optionsProvider,
        ILogger<OneDriveSyncWorker> logger)
    {
        _orchestrator = orchestrator;
        _optionsProvider = optionsProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _optionsProvider.Load();
        if (options.RunSyncOnStartup)
        {
            await SafeRunAsync("startup", stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            options = _optionsProvider.Load();
            var delay = TimeSpan.FromHours(options.SyncIntervalHours);
            _logger.LogInformation("Next OneDrive sync in {DelayHours}h.", options.SyncIntervalHours);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await SafeRunAsync("schedule", stoppingToken);
        }
    }

    private async Task SafeRunAsync(string reason, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _orchestrator.RunOnceAsync(reason, cancellationToken);
            if (!result.Success)
            {
                _logger.LogWarning("OneDrive sync finished with status {Status}: {Message}", result.Status, result.Message);
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected OneDrive sync failure.");
        }
    }
}

