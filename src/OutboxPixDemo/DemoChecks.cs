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

        // Os mesmos três valores mostrados no GIF: R$ 25,90, R$ 42,00 e R$ 100,00.
        // Cada Pix tem sua própria transação com o respectivo evento.
        foreach (var amountInCents in new long[] { 2590, 4200, 10000 })
            await PixService.RegisterAsync(db, amountInCents);
        Check(await db.Pix.CountAsync() == 3 && await db.Outbox.CountAsync() == 3,
            "3 Pix e seus 3 eventos gravados, cada par na mesma transação");
        var events = await db.Outbox.OrderBy(m => m.Sequence).ToListAsync();
        Check(events.Select(m => m.EventId).Distinct().Count() == 3
            && events.Select(m => m.AggregateId).Distinct().Count() == 3,
            "Cada Pix possui um EventId distinto");
        var eventId = events[0].EventId;
        var publisher = new FakePublisher { Fail = true };
        try { await OutboxRelay.PublishNextAsync(db, publisher); }
        catch (IOException) { }
        db.ChangeTracker.Clear();
        Check(await db.Outbox.CountAsync(m => m.PublishedAtUtc == null) == 3,
            "Broker indisponível mantém os 3 eventos pendentes");

        // ACK recebido, mas marca de publicado falha: haverá reenvio.
        publisher.Fail = false;
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER fail_mark BEFORE UPDATE ON outbox
            BEGIN SELECT RAISE(ABORT, 'falha após ACK'); END;
            """);
        try { await OutboxRelay.PublishNextAsync(db, publisher); }
        catch (DbUpdateException) { }
        db.ChangeTracker.Clear();
        Check(await db.Outbox.CountAsync(m => m.PublishedAtUtc == null) == 3 && publisher.Deliveries.Count == 1,
            "Falha após ACK deixa evento elegível para reenvio");
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_mark;");

        await OutboxRelay.PublishNextAsync(db, publisher);
        Check(await db.Outbox.CountAsync(m => m.PublishedAtUtc != null) == 1,
            "Nova tentativa publica e marca o primeiro evento");
        Check(publisher.Deliveries.SequenceEqual(new[] { eventId, eventId }),
            "Reenvio preserva EventId para deduplicação no consumidor");
        while (await OutboxRelay.PublishNextAsync(db, publisher)) { }
        Check(await db.Outbox.CountAsync(m => m.PublishedAtUtc != null) == 3
            && publisher.Deliveries.Distinct().Count() == 3,
            "Recuperação publica os 3 eventos distintos, sem perder nenhum Pix");
        Check(!await OutboxRelay.PublishNextAsync(db, publisher) && publisher.Deliveries.Count == 4,
            "Fila esvaziada: 3 eventos distintos e 1 reenvio demonstrado, sem novos envios");
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
