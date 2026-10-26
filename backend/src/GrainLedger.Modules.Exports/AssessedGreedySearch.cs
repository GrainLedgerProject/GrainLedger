using System.Text.Json;

namespace GrainLedger.Modules.Exports;

// Bounded, deterministic local search. Excluding one used position after an
// unsuccessful assessment lets a smaller compliant lot replace a larger
// noncompliant one. Exhaustion is never proof that no feasible plan exists.
internal static class AssessedGreedySearch
{
    internal sealed record Result(string Outcome, int Iterations,
        GreedyStockSelection.Result? Selection, BlendAssessmentResult? Assessment,
        IReadOnlyList<string> Reasons);

    internal static async Task<Result> SearchAsync(IReadOnlyList<PlanningCandidate> candidates,
        decimal inputTargetKg, string targetProductIdentity, Guid catalogCommodityId,
        JsonElement compatibilities,
        Func<GreedyStockSelection.Result, CancellationToken, Task<BlendAssessmentResult>> assess,
        int maxIterations, CancellationToken ct,
        Func<Guid, decimal?>? blendInputTarget = null)
    {
        if (maxIterations is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(maxIterations));
        if (candidates.Count > 100) return new("SEARCH_EXHAUSTED", 0, null, null,
            ["CANDIDATE_LIMIT_EXCEEDED"]);
        var ordered = candidates.OrderBy(x => x.LotId).ThenBy(x => x.LocationId).ToArray();
        var queue = new Queue<HashSet<(Guid LotId, Guid LocationId)>>();
        queue.Enqueue([]);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        GreedyStockSelection.Result? bestSelection = null;
        BlendAssessmentResult? bestAssessment = null;
        var iterations = 0;
        while (queue.Count > 0 && iterations < maxIterations)
        {
            ct.ThrowIfCancellationRequested();
            var excluded = queue.Dequeue();
            var fingerprint = string.Join(';', excluded.OrderBy(x => x.LotId).ThenBy(x => x.LocationId)
                .Select(x => $"{x.LotId:N}:{x.LocationId:N}"));
            if (!seen.Add(fingerprint)) continue;
            iterations++;
            var selected = GreedyStockSelection.Select(ordered.Where(x =>
                !excluded.Contains((x.LotId, x.LocationId))).ToArray(), inputTargetKg,
                targetProductIdentity, catalogCommodityId, compatibilities, blendInputTarget);
            if (selected.Outcome != "MASS_COVERED") continue;
            var assessment = await assess(selected, ct);
            if (assessment.Status == "PASS")
                return new("PASS", iterations, selected, assessment, []);
            if (bestAssessment is null ||
                (bestAssessment.Status == "FAIL" && assessment.Status == "INCOMPLETE"))
            {
                bestSelection = selected;
                bestAssessment = assessment;
            }
            foreach (var allocation in selected.Allocations.OrderBy(x => x.LotId).ThenBy(x => x.LocationId))
            {
                var next = new HashSet<(Guid LotId, Guid LocationId)>(excluded)
                { (allocation.LotId, allocation.LocationId) };
                if (next.Count <= ordered.Length) queue.Enqueue(next);
            }
        }
        return new("SEARCH_EXHAUSTED", iterations, bestSelection, bestAssessment,
            [queue.Count > 0 ? "ITERATION_LIMIT_REACHED" : "SEARCH_SPACE_EXHAUSTED"]);
    }
}
