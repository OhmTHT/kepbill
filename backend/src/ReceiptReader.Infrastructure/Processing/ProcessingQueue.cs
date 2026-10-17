using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ReceiptReader.Application.Abstractions;
using ReceiptReader.Application.Receipts;
using ReceiptReader.Domain.Enums;
using ReceiptReader.Infrastructure.Persistence;

namespace ReceiptReader.Infrastructure.Processing;

/// <summary>In-process queue. Good enough for one machine; swap for RabbitMQ/Hangfire if you scale out.</summary>
public sealed class ChannelProcessingQueue : IProcessingQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<Guid> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(Guid receiptId, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(receiptId, ct);
}

/// <summary>
/// Processes receipts one at a time (a single local GPU is fastest serving one request at a time).
/// On startup, re-queues anything left Pending/Processing by a previous crash or restart.
/// </summary>
public sealed class ReceiptProcessingWorker(
    ChannelProcessingQueue queue,
    IServiceScopeFactory scopes,
    ILogger<ReceiptProcessingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeueUnfinishedAsync(stoppingToken);

        await foreach (var id in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<ReceiptProcessingService>();
                await processor.ProcessAsync(id, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error while processing receipt {Id}", id);
            }
        }
    }

    private async Task RequeueUnfinishedAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ids = await db.Receipts
                .Where(r => r.Status == ReceiptStatus.Pending || r.Status == ReceiptStatus.Processing)
                .OrderBy(r => r.CreatedAt)
                .Select(r => r.Id)
                .ToListAsync(ct);
            foreach (var id in ids) await queue.EnqueueAsync(id, ct);
            if (ids.Count > 0) logger.LogInformation("Re-queued {Count} unfinished receipt(s)", ids.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not re-queue unfinished receipts");
        }
    }
}
