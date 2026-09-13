using FlashSale.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlashSale.Infrastructure.Outbox.Processing;

public sealed class OutboxWorker(
    IServiceScopeFactory scopes,
    IOptions<OutboxOptions> options,
    TimeProvider clock,
    ILogger<OutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.WorkerEnabled)
        {
            return;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    if (await scope.ServiceProvider.GetRequiredService<PaymentOutboxProcessor>().ProcessNextAsync(stoppingToken))
                    {
                        continue;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Outbox polling failed");
                }

                await Task.Delay(options.Value.PollInterval, clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
