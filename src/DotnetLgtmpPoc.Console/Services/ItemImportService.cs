using System.Diagnostics;
using System.Globalization;
using CsvHelper;
using Microsoft.Extensions.Logging;
using DotnetLgtmpPoc.Core.Data;
using DotnetLgtmpPoc.Core.Models;

namespace DotnetLgtmpPoc.Console.Services;

public class ItemImportService(AppDbContext db, ILogger<ItemImportService> logger)
{
    public const string ActivitySourceName = "DotnetLgtmpPoc.Console";
    private static readonly ActivitySource Source = new(ActivitySourceName);

    public async Task RunAsync(string filename)
    {
        using var activity = Source.StartActivity();
        activity?.SetTag("etl.filename", filename);

        logger.LogInformation("Starting ETL for file {Filename}", filename);

        var records = Extract(filename);
        activity?.SetTag("etl.rows.extracted", records.Count);

        var items = Transform(records);
        activity?.SetTag("etl.rows.valid", items.Count);

        await Load(items);
        activity?.SetTag("etl.rows.inserted", items.Count);

        logger.LogInformation("ETL complete for file {Filename} — inserted {Count} items", filename, items.Count);
    }

    private List<ItemCsvRow> Extract(string filename)
    {
        using var activity = Source.StartActivity();

        using var reader = new StreamReader(filename);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        var records = csv.GetRecords<ItemCsvRow>().ToList();

        logger.LogInformation("Extracted {Count} rows from {Filename}", records.Count, filename);
        return records;
    }

    private List<Item> Transform(List<ItemCsvRow> records)
    {
        using var activity = Source.StartActivity();

        var items = records
            .Where(r => !string.IsNullOrWhiteSpace(r.Name))
            .Select(r => new Item
            {
                Name = r.Name.Trim(),
                Description = r.Description?.Trim(),
                CreatedAt = DateTime.UtcNow,
            })
            .ToList();

        logger.LogInformation("Transformed to {Count} valid items", items.Count);
        return items;
    }

    private async Task Load(List<Item> items)
    {
        using var activity = Source.StartActivity();

        db.Items.AddRange(items);
        await db.SaveChangesAsync();
    }
}

// ── CSV shape (maps to column headers) ──
public record ItemCsvRow(string Name, string? Description);
