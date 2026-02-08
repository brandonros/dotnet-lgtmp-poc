using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using DotnetLgtmpPoc.Core.Data;
using DotnetLgtmpPoc.Core.Models;
using DotnetLgtmpPoc.Core.Telemetry;

WebApplication app;

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.AddOtelDefaults();

    var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING")
        ?? throw new InvalidOperationException("CONNECTION_STRING env var is required");
    builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

    app = builder.Build();
}
catch (Exception ex)
{
    using var loggerFactory = LoggerFactory.Create(l => l.AddJsonConsole());
    var logger = loggerFactory.CreateLogger("Program");
    logger.LogCritical(ex, "Failed during host construction");
    throw;
}

try
{
    // ── Auto-create schema on startup (PoC — no migration files needed) ──
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
    }

    // ── Trace ID response header (lets caller look up their trace in Tempo) ──
    app.Use(async (context, next) =>
    {
        var activity = Activity.Current;
        if (activity is not null)
        {
            context.Response.Headers["X-Trace-Id"] = activity.TraceId.ToString();
            context.Response.Headers["X-Span-Id"] = activity.SpanId.ToString();
        }
        await next();
    });

    // ── CRUD endpoints ──
    var items = app.MapGroup("/api/items");

    items.MapGet("/", async (AppDbContext db) =>
        await db.Items.ToListAsync());

    items.MapGet("/{id:int}", async (int id, AppDbContext db) =>
        await db.Items.FindAsync(id) is Item item
            ? Results.Ok(item)
            : Results.NotFound());

    items.MapPost("/", async (Item item, AppDbContext db) =>
    {
        item.CreatedAt = DateTime.UtcNow;
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return Results.Created($"/api/items/{item.Id}", item);
    });

    items.MapPut("/{id:int}", async (int id, Item input, AppDbContext db) =>
    {
        var item = await db.Items.FindAsync(id);
        if (item is null) return Results.NotFound();
        item.Name = input.Name;
        item.Description = input.Description;
        await db.SaveChangesAsync();
        return Results.Ok(item);
    });

    items.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
    {
        var item = await db.Items.FindAsync(id);
        if (item is null) return Results.NotFound();
        db.Items.Remove(item);
        await db.SaveChangesAsync();
        return Results.NoContent();
    });

    // ── Health check ──
    app.MapGet("/health", () => Results.Ok("healthy"));

    app.Run();
}
catch (Exception ex)
{
    app.Logger.LogCritical(ex, "Unhandled startup exception");
    throw;
}
