using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace OutboxPixDemo;

public sealed class Pix
{
    public Guid Id { get; set; }
    public long AmountInCents { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class OutboxMessage
{
    public long Sequence { get; set; }
    public Guid EventId { get; set; }
    public Guid AggregateId { get; set; }
    public string Payload { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
}

public sealed class PixDb(DbContextOptions<PixDb> options) : DbContext(options)
{
    public DbSet<Pix> Pix => Set<Pix>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Pix>().ToTable("pix");
        model.Entity<OutboxMessage>().ToTable("outbox").HasKey(m => m.Sequence);
        model.Entity<OutboxMessage>().HasIndex(m => m.EventId).IsUnique();
        model.Entity<OutboxMessage>().HasIndex(m => new { m.PublishedAtUtc, m.Sequence });
    }
}

public static class PixService
{
    // Demonstra registro interno; não realiza uma transferência Pix real.
    public static async Task<Pix> RegisterAsync(PixDb db, long amountInCents,
        CancellationToken ct = default)
    {
        if (amountInCents <= 0) throw new ArgumentOutOfRangeException(nameof(amountInCents));
        var pix = new Pix
        {
            Id = Guid.NewGuid(), AmountInCents = amountInCents, CreatedAtUtc = DateTime.UtcNow
        };
        var eventId = Guid.NewGuid();
        db.Pix.Add(pix);
        db.Outbox.Add(new OutboxMessage
        {
            EventId = eventId,
            AggregateId = pix.Id,
            CreatedAtUtc = pix.CreatedAtUtc,
            Payload = JsonSerializer.Serialize(new
            {
                EventId = eventId, Type = "PixRegistrado", Version = 1,
                PixId = pix.Id, pix.AmountInCents, pix.CreatedAtUtc
            })
        });

        // EF Core usa uma transação para esta única chamada:
        // ou grava Pix + Outbox, ou desfaz ambos. Não chama Kafka aqui.
        await db.SaveChangesAsync(ct);
        return pix;
    }
}
