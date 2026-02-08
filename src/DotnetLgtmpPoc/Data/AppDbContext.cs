using Microsoft.EntityFrameworkCore;
using DotnetLgtmpPoc.Models;

namespace DotnetLgtmpPoc.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Item> Items => Set<Item>();
}
