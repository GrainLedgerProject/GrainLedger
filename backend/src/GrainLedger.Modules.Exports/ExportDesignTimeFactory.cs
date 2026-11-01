using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GrainLedger.Modules.Exports;

public sealed class ExportDesignTimeFactory : IDesignTimeDbContextFactory<ExportDbContext>
{
    public ExportDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__GrainLedgerMigration");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Set ConnectionStrings__GrainLedgerMigration for explicit Exports migrations.");
        var options = new DbContextOptionsBuilder<ExportDbContext>().UseNpgsql(connection,
            n => n.MigrationsHistoryTable("__EFMigrationsHistory", "exports")).Options;
        return new ExportDbContext(options);
    }
}
