using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;

namespace OutboxPixDemo;

public interface IEventPublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken ct);
}

public sealed class KafkaPublisher(IProducer<string, string> producer) : IEventPublisher
{
    public async Task PublishAsync(OutboxMessage message, CancellationToken ct)
    {
        var delivery = await producer.ProduceAsync("pix", new Message<string, string>
        {
            Key = message.AggregateId.ToString(), Value = message.Payload
        }, ct);
        if (delivery.Status != PersistenceStatus.Persisted)
            throw new InvalidOperationException("Kafka não confirmou a persistência do evento.");
    }
}

public static class OutboxRelay
{
    // Exemplo com apenas UM relay ativo. Não faz claim/lease entre réplicas.
    public static async Task<bool> PublishNextAsync(PixDb db, IEventPublisher publisher,
        CancellationToken ct = default)
    {
        var message = await db.Outbox.Where(m => m.PublishedAtUtc == null)
            .OrderBy(m => m.Sequence).FirstOrDefaultAsync(ct);
        if (message is null) return false;

        await publisher.PublishAsync(message, ct);
        // Só marca depois do ACK. Se o processo cair antes de salvar esta marca,
        // o mesmo EventId será reenviado. O consumidor precisa ser idempotente.
        message.PublishedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class OutboxWorker(IDbContextFactory<PixDb> factory, IEventPublisher publisher,
    ILogger<OutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var wait = TimeSpan.FromSeconds(2);
            try
            {
                await using var db = await factory.CreateDbContextAsync(stoppingToken);
                if (await OutboxRelay.PublishNextAsync(db, publisher, stoppingToken))
                    wait = TimeSpan.Zero;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Evento continua pendente; nova tentativa em 2 segundos.");
            }
            try { await Task.Delay(wait, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
