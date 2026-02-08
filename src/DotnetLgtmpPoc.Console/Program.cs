using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using DotnetLgtmpPoc.Core.Data;
using DotnetLgtmpPoc.Core.Telemetry;

var builder = Host.CreateApplicationBuilder(args);

builder.AddOtelDefaults();

var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING")
    ?? throw new InvalidOperationException("CONNECTION_STRING env var is required");
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

var host = builder.Build();

// ── Do work and exit ──
using var scope = host.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
db.Database.EnsureCreated();

// TODO: ETL / batch logic here

await host.StopAsync();
