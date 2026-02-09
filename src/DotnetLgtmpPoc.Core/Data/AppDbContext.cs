using Microsoft.EntityFrameworkCore;
using DotnetLgtmpPoc.Core.Models;

namespace DotnetLgtmpPoc.Core.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Item> Items => Set<Item>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Item>()
            .HasIndex(i => i.Name)
            .IsUnique();
    }
}
