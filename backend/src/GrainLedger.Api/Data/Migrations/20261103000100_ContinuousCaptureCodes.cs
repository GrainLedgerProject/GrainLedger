using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261103000100_ContinuousCaptureCodes")]
public partial class ContinuousCaptureCodes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        INSERT INTO grainledger.capture_code_counters (company_id, kind, scope, next_value)
        SELECT company_id, kind, '', MAX(next_value)::bigint
        FROM (
            SELECT company_id, kind, next_value::numeric AS next_value, true AS persisted_counter
            FROM grainledger.capture_code_counters
            WHERE kind IN ('PRC', 'SMP')
            UNION ALL
            SELECT company_id, 'PRC',
                CASE WHEN code ~ '^PRC-([0-9]{6}|[0-9]{8})-[0-9]+$'
                    THEN split_part(code, '-', 3)::numeric + 1 END, false
            FROM grainledger.capture_procurements
            WHERE code ~ '^PRC-([0-9]{6}|[0-9]{8})-[0-9]+$'
            UNION ALL
            SELECT company_id, 'SMP',
                CASE WHEN code ~ '^SMP-([0-9]{6}|[0-9]{8})-[0-9]+$'
                    THEN split_part(code, '-', 3)::numeric + 1 END, false
            FROM grainledger.capture_samples
            WHERE code ~ '^SMP-([0-9]{6}|[0-9]{8})-[0-9]+$'
        ) AS existing
        WHERE persisted_counter OR next_value <= 9223372036854775807
        GROUP BY company_id, kind
        ON CONFLICT (company_id, kind, scope) DO UPDATE
        SET next_value = GREATEST(grainledger.capture_code_counters.next_value, EXCLUDED.next_value);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Capture code counters cannot be rolled back safely.");
}
