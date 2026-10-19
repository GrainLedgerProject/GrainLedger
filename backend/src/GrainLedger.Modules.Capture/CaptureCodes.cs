using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace GrainLedger.Modules.Capture;

public sealed class CaptureCodeCounter
{
    public Guid CompanyId { get; set; }
    public required string Kind { get; set; }
    public required string Scope { get; set; }
    public long NextValue { get; set; }
}

internal static class CaptureCodes
{
    private static readonly TimeZoneInfo Vietnam = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    // Call only while holding Capture's shared write lock and inside the caller's transaction.
    private static async Task<long> NextAsync(CaptureDbContext db, Guid companyId, string kind,
        string scope, CancellationToken ct)
    {
        var counter = db.CodeCounters.Local.FirstOrDefault(x =>
            x.CompanyId == companyId && x.Kind == kind && x.Scope == scope)
            ?? await db.CodeCounters.SingleOrDefaultAsync(x =>
                x.CompanyId == companyId && x.Kind == kind && x.Scope == scope, ct);
        if (counter is null)
        {
            counter = new CaptureCodeCounter { CompanyId = companyId, Kind = kind, Scope = scope, NextValue = 2 };
            db.CodeCounters.Add(counter);
            return 1;
        }
        var value = counter.NextValue;
        counter.NextValue = checked(value + 1);
        return value;
    }

    public static async Task<(long Number, string Code)> FarmerAsync(CaptureDbContext db,
        Guid companyId, CancellationToken ct)
    {
        while (true)
        {
            var number = await NextAsync(db, companyId, "FRM", "", ct);
            var code = $"FRM-{number.ToString("D4", CultureInfo.InvariantCulture)}";
            if (!await db.Farmers.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Code == code, ct))
                return (number, code);
        }
    }

    public static async Task<long> FarmerNumberAsync(CaptureDbContext db, Farmer farmer, CancellationToken ct)
    {
        if (farmer.CodeNumber is { } assigned) return assigned;
        farmer.CodeNumber = await NextAsync(db, farmer.CompanyId, "FRM", "", ct);
        return farmer.CodeNumber.Value;
    }

    public static async Task<string> PlotAsync(CaptureDbContext db, Guid companyId,
        Guid farmerId, long farmerNumber, CancellationToken ct)
    {
        while (true)
        {
            var number = await NextAsync(db, companyId, "PLT", farmerId.ToString("N"), ct);
            var code = $"PLT-{farmerNumber.ToString("D4", CultureInfo.InvariantCulture)}-{number.ToString("D2", CultureInfo.InvariantCulture)}";
            if (!await db.Plots.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Code == code, ct))
                return code;
        }
    }

    public static async Task<string> DatedAsync(CaptureDbContext db, Guid companyId,
        string kind, DateTimeOffset occurredAt, CancellationToken ct)
    {
        var localDate = TimeZoneInfo.ConvertTime(occurredAt, Vietnam);
        var scope = localDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var shortDate = localDate.ToString("yyMMdd", CultureInfo.InvariantCulture);
        while (true)
        {
            var number = await NextAsync(db, companyId, kind, scope, ct);
            var code = $"{kind}-{shortDate}-{number.ToString("D4", CultureInfo.InvariantCulture)}";
            var exists = kind == "PRC"
                ? await db.Procurements.AnyAsync(x => x.CompanyId == companyId && x.Code == code, ct)
                : await db.Samples.AnyAsync(x => x.CompanyId == companyId && x.Code == code, ct);
            if (!exists) return code;
        }
    }
}
