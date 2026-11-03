using Npgsql;

namespace GrainLedger.Api.Features.Capture;

public sealed record LabReportResidueBinding(Guid ResultId, Guid SourceId, bool Published, IReadOnlyList<string> Reasons);

// Capture owns confirmation; Ledger appends separate residue audit facts without committing this transaction.
