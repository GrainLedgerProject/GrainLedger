using Npgsql;

namespace GrainLedger.Modules.Capture;

public sealed record LabReportResidueBinding(Guid ResultId, Guid SourceId, bool Published, IReadOnlyList<string> Reasons);

// Capture owns confirmation; Ledger appends separate residue audit facts without committing this transaction.
public interface IConfirmedLabReportResidueBinder
{
    Task<IReadOnlyList<LabReportResidueBinding>> BindConfirmedReportAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid actorId, Guid companyId, Guid reportId, DateTimeOffset confirmedAt, CancellationToken ct);
}
