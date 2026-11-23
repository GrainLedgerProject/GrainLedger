namespace GrainLedger.Api.Features.Ledger;

// Opt in only after the propagation migration and restricted grants are deployed.
public sealed class ResiduePropagationWorker(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<ResiduePropagationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Read the final host configuration here, after all providers have loaded.
        // Disabled workers return before opening a database connection.
        if (!configuration.GetValue<bool>("ResiduePropagation:Enabled")) return;
        var batch = Math.Clamp(configuration.GetValue("ResiduePropagation:BatchSize", 25), 1, 100);
        var poll = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("ResiduePropagation:PollSeconds", 5), 1, 60));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var processed = await scope.ServiceProvider.GetRequiredService<ResiduePropagationRunner>().RunOnceAsync(stoppingToken, batch);
                if (processed > 0) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "Residue worker iteration failed; pending jobs remain durable"); }
            try { await Task.Delay(poll, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
