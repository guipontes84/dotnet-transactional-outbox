using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using OutboxPixDemo;

if (args.Contains("--self-test"))
{
    await DemoChecks.RunAsync();
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContextFactory<PixDb>(options => options.UseSqlite(
    builder.Configuration.GetConnectionString("Pix") ?? "Data Source=pix-demo.db"));
builder.Services.AddSingleton<IProducer<string, string>>(_ =>
    new ProducerBuilder<string, string>(new ProducerConfig
    {
        BootstrapServers = builder.Configuration["Kafka:BootstrapServers"] ?? "localhost:9092",
        EnableIdempotence = true,
        Acks = Acks.All,
        MessageTimeoutMs = 5000
    }).Build());
builder.Services.AddSingleton<IEventPublisher, KafkaPublisher>();
builder.Services.AddHostedService<OutboxWorker>();

var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<PixDb>().Database.EnsureCreatedAsync();

app.MapPost("/pix", async (CreatePix request, PixDb db, CancellationToken ct) =>
{
    if (request.AmountInCents <= 0)
        return Results.BadRequest(new { error = "AmountInCents deve ser maior que zero." });

    var pix = await PixService.RegisterAsync(db, request.AmountInCents, ct);
    return Results.Created($"/pix/{pix.Id}", pix);
});
app.MapGet("/pix/{id:guid}", async (Guid id, PixDb db, CancellationToken ct) =>
    await db.Pix.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct)
        is { } pix ? Results.Ok(pix) : Results.NotFound());
app.MapGet("/outbox", async (PixDb db, CancellationToken ct) =>
    await db.Outbox.AsNoTracking().OrderBy(m => m.Sequence).ToListAsync(ct));
await app.RunAsync();

record CreatePix(long AmountInCents);
