using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GrainLedger.Api.Data;

public sealed class AppDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__GrainLedgerMigration");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Set ConnectionStrings__GrainLedgerMigration for explicit migrations.");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection,
            n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName).MigrationsHistoryTable("__EFMigrationsHistory", "grainledger")).Options;
        return new AppDbContext(options);
    }
}
