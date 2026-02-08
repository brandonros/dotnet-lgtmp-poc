using Microsoft.EntityFrameworkCore;
using DotnetLgtmpPoc.Core.Models;

namespace DotnetLgtmpPoc.Core.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Item> Items => Set<Item>();
}
