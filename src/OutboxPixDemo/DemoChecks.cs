using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace OutboxPixDemo;

public static class DemoChecks
{
    public static async Task RunAsync()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PixDb>().UseSqlite(connection).Options;
        await using var db = new PixDb(options);
        await db.Database.EnsureCreatedAsync();

        // Falha real de persistência na tabela Outbox: Pix também deve ser desfeito.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER fail_outbox BEFORE INSERT ON outbox
            BEGIN SELECT RAISE(ABORT, 'falha simulada'); END;
            """);
        try
        {
            await PixService.RegisterAsync(db, 1000);
            throw new Exception("Esperava rollback.");
        }
        catch (DbUpdateException) { }
        db.ChangeTracker.Clear();
        Check(await db.Pix.CountAsync() == 0 && await db.Outbox.CountAsync() == 0,
            "Falha no Outbox desfaz também o Pix");
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_outbox;");

        await PixService.RegisterAsync(db, 2590);
        Check(await db.Pix.CountAsync() == 1 && await db.Outbox.CountAsync() == 1,
            "Pix e evento gravados juntos");
        var eventId = (await db.Outbox.SingleAsync()).EventId;
        var publisher = new FakePublisher { Fail = true };
        try { await OutboxRelay.PublishNextAsync(db, publisher); }
        catch (IOException) { }
        db.ChangeTracker.Clear();
        Check((await db.Outbox.SingleAsync()).PublishedAtUtc is null,
            "Broker indisponível mantém evento pendente");

        // ACK recebido, mas marca de publicado falha: haverá reenvio.
        publisher.Fail = false;
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER fail_mark BEFORE UPDATE ON outbox
            BEGIN SELECT RAISE(ABORT, 'falha após ACK'); END;
            """);
        try { await OutboxRelay.PublishNextAsync(db, publisher); }
        catch (DbUpdateException) { }
        db.ChangeTracker.Clear();
        Check((await db.Outbox.SingleAsync()).PublishedAtUtc is null && publisher.Deliveries.Count == 1,
            "Falha após ACK deixa evento elegível para reenvio");
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_mark;");

        await OutboxRelay.PublishNextAsync(db, publisher);
        Check((await db.Outbox.SingleAsync()).PublishedAtUtc is not null,
            "Nova tentativa publica e marca o evento");
        Check(publisher.Deliveries.SequenceEqual(new[] { eventId, eventId }),
            "Reenvio preserva EventId para deduplicação no consumidor");
        Check(!await OutboxRelay.PublishNextAsync(db, publisher),
            "Evento já marcado não é publicado novamente");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        Console.WriteLine($"OK: {description}");
    }

    private sealed class FakePublisher : IEventPublisher
    {
        public bool Fail { get; set; }
        public List<Guid> Deliveries { get; } = [];
        public Task PublishAsync(OutboxMessage message, CancellationToken ct)
        {
            if (Fail) throw new IOException("Kafka indisponível (simulado)");
            Deliveries.Add(message.EventId);
            return Task.CompletedTask;
        }
    }
}
