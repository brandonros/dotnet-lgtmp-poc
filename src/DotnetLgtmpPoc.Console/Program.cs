using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using DotnetLgtmpPoc.Console.Services;
using DotnetLgtmpPoc.Core.Data;
using DotnetLgtmpPoc.Core.Telemetry;

IHost host;

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.AddOtelDefaults();
    builder.Services.ConfigureOpenTelemetryTracerProvider(tracing =>
        tracing.AddSource(ItemImportService.ActivitySourceName));

    var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING")
        ?? throw new InvalidOperationException("CONNECTION_STRING env var is required");
    builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
    builder.Services.AddScoped<ItemImportService>();

    host = builder.Build();
}
catch (Exception ex)
{
    using var loggerFactory = LoggerFactory.Create(l => l.AddJsonConsole());
    var logger = loggerFactory.CreateLogger("Program");
    logger.LogCritical(ex, "Failed during host construction");
    throw;
}

string? filename = null;

try
{
    if (args.Length == 0)
        throw new ArgumentException("Usage: dotnet DotnetLgtmpPoc.Console.dll <filename>");

    filename = args[0];

    using var scope = host.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    var importService = scope.ServiceProvider.GetRequiredService<ItemImportService>();
    await importService.RunAsync(filename);
}
catch (Exception ex)
{
    using var loggerFactory = LoggerFactory.Create(l => l.AddJsonConsole());
    var logger = loggerFactory.CreateLogger("Program");
    logger.LogCritical(ex, "Unhandled exception during ETL for file {Filename}", filename);
    throw;
}
finally
{
    await host.StopAsync();
}
