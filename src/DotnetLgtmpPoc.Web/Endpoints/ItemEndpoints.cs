using Microsoft.EntityFrameworkCore;
using DotnetLgtmpPoc.Core.Data;
using DotnetLgtmpPoc.Core.Models;

namespace DotnetLgtmpPoc.Web.Endpoints;

public static class ItemEndpoints
{
    public static void MapItemEndpoints(this IEndpointRouteBuilder app)
    {
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
    }
}
