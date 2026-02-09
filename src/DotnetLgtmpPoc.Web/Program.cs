using System.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using DotnetLgtmpPoc.Core.Data;
using DotnetLgtmpPoc.Core.Telemetry;
using Scalar.AspNetCore;
using DotnetLgtmpPoc.Web.Endpoints;

WebApplication app;

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Logging.AddJsonConsole();
    builder.AddOtelDefaults();
    builder.Services.ConfigureOpenTelemetryTracerProvider(tracing =>
        tracing.AddAspNetCoreInstrumentation());
    builder.Services.ConfigureOpenTelemetryMeterProvider(metrics =>
        metrics.AddAspNetCoreInstrumentation());

    var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING")
        ?? throw new InvalidOperationException("CONNECTION_STRING env var is required");
    builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

    builder.Services.AddOpenApi();

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
    // ── Respect X-Forwarded-Proto so OpenAPI/Scalar URLs use HTTPS behind the reverse proxy ──
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    });

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

    app.MapOpenApi();
    app.MapScalarApiReference();

    app.MapItemEndpoints();
    app.MapGet("/health", () => Results.Ok("healthy"));

    app.Run();
}
catch (Exception ex)
{
    app.Logger.LogCritical(ex, "Unhandled startup exception");
    throw;
}
