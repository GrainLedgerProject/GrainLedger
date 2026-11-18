using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using GrainLedger.Api.Features.Catalogs;
using GrainLedger.Api.Features.Ledger;

namespace GrainLedger.Api.Features.Exports;

internal static class AssessedMilpSearch
{
    internal sealed record Attempt(string Mode, Guid? CompatibilityId, int CandidateCount, decimal InputKg,
        string Status, int Variables, int Constraints, long ElapsedMs, double? Objective, double? BestBound,
        long? Nodes, string? AssessmentStatus);
    internal sealed record RatioRange(string MinimumKg, string MaximumKg);
    internal sealed record Result(GreedyStockSelection.Result? Selection, IReadOnlyList<Attempt> Attempts,
        bool Optimal, int? Positions, RatioRange? Range = null);
    private sealed record Group(string Mode, Guid? Id, Guid? OutputId, Guid? OutputCatalogId, decimal InputKg,
        string[] ProductKeys, PlanningCandidate[] Candidates);
    private sealed record Input(PlanningCandidate Candidate, decimal[] Quality,
        decimal[] Residue, string ResidueProfile);
    private sealed record Threshold(Guid SubstanceId, decimal Value, string Unit);

    internal static async Task<Result> SearchAsync(PlanningCandidateSnapshot snapshot, string target,
        IReadOnlyList<SpecificationLimit> limits, IReadOnlyList<BlendQualityRule> rules,
        IReadOnlyList<BlendQualityObservation> observations, BlendAssessmentRule policy, ApplicableMrlResult mrl,
        Func<Guid, decimal?> yieldTarget, BlendPlanningObjective priority, int maxAttempts, TimeSpan budget,
        Func<Guid[], Guid?, CancellationToken, Task<IReadOnlyDictionary<Guid, IReadOnlyList<ResidueResult>>>> bounds,
        Func<GreedyStockSelection.Result, CancellationToken, Task<BlendAssessmentResult>> assess,
        CancellationToken ct, Guid? moistureAttributeId = null)
    {
        if (limits.Count == 0 || mrl.Status != "FOUND" || mrl.MarketId != policy.MarketId ||
            mrl.CommodityId != policy.CommodityId || policy.ResidueBasis != "AS_RECEIVED" ||
            policy.MrlMarginFraction is < 0 or >= 1)
            return new(null, [], false, null);
        var thresholds = new List<Threshold>();
        foreach (var id in policy.RequiredSubstances)
        {
            var found = mrl.Limits.Where(limit => limit.SubstanceId == id).ToArray();
            if (found.Length != 1) return new(null, [], false, null);
            thresholds.Add(new(id, found[0].LimitValue * (1 - policy.MrlMarginFraction), found[0].Unit));
        }

        var watch = Stopwatch.StartNew();
        var attempts = new List<Attempt>();
        var groups = Groups(snapshot, target, yieldTarget).ToArray();
        var useLotCount = priority == BlendPlanningObjective.FewestLots;
        var positionLowerBound = groups.Select(g => PositionLowerBound(g.Candidates, g.InputKg, useLotCount))
            .DefaultIfEmpty(int.MaxValue).Min();
        var moistureIndex = Array.FindIndex(limits.ToArray(), x => x.AttributeId == moistureAttributeId);
        if (priority == BlendPlanningObjective.LowestMoisture && moistureIndex < 0)
            return new(null, [], false, null);
        GreedyStockSelection.Result? best = null;
        int? bestCount = null;
        decimal? bestScore = null;
        RatioRange? bestRange = null;
        var qualityByLot = observations.ToLookup(q => (q.LotId, q.AttributeId));
        var boundsByOutput = new Dictionary<Guid, IReadOnlyDictionary<Guid, IReadOnlyList<ResidueResult>>>();
        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();
            if (watch.Elapsed >= budget || attempts.Count >= maxAttempts) break;
            if (group.Candidates.Length is 0 or > 100 || group.Candidates.Sum(c => c.AvailableKg) < group.InputKg)
                continue;
            var blend = group.Mode == "BLEND_INPUT_STOCK";
            if (blend && (group.OutputId is null || limits.Any(limit => !rules.Any(rule =>
                rule.AttributeId == limit.AttributeId && rule.Unit == limit.Unit &&
                rule.Calculation == "MASS_WEIGHTED_MEAN"))))
                continue;
            var outputKey = group.OutputId ?? Guid.Empty;
            if (!boundsByOutput.TryGetValue(outputKey, out var residueByLot))
            {
                var ids = groups.Where(other => other.OutputId == group.OutputId)
                    .SelectMany(other => other.Candidates).Select(c => c.LotId).Distinct().ToArray();
                if (ids.Length > 100) ids = group.Candidates.Select(c => c.LotId).Distinct().ToArray();
                residueByLot = await bounds(ids, group.OutputId, ct);
                if (groups.Where(other => other.OutputId == group.OutputId)
                        .SelectMany(other => other.Candidates).All(c => residueByLot.ContainsKey(c.LotId)))
                    boundsByOutput.Add(outputKey, residueByLot);
            }
            var inputs = new List<Input>();
            foreach (var candidate in group.Candidates)
            {
                var quality = new decimal[limits.Count];
                var valid = true;
                for (var i = 0; i < limits.Count; i++)
                {
                    var limit = limits[i];
                    var found = qualityByLot[(candidate.LotId, limit.AttributeId)]
                        .Where(q => q.Unit == limit.Unit && q.EvidenceId != Guid.Empty && q.EvidenceVersion > 0).ToArray();
                    if (found.Length != 1 || (!blend &&
                        (found[0].Value < limit.MinValue || found[0].Value > limit.MaxValue)))
                    { valid = false; break; }
                    quality[i] = found[0].Value;
                }
                if (!valid || !residueByLot.TryGetValue(candidate.LotId, out var residueRows)) continue;

                var residue = new decimal[thresholds.Count];
                var profiles = new string[thresholds.Count];
                for (var i = 0; i < thresholds.Count; i++)
                {
                    var threshold = thresholds[i];
                    var bound = residueRows.SingleOrDefault(row => row.SubstanceId == threshold.SubstanceId);
                    if (bound is not { Status: "COMPLETE", Upper: not null } || bound.Unit != threshold.Unit ||
                        !ExportProductMatch.IsResidueForProduct(bound, group.OutputCatalogId ?? candidate.CatalogCommodityId,
                            snapshot, target) ||
                        !decimal.TryParse(bound.Upper, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out residue[i]) ||
                        (!blend && residue[i] > threshold.Value))
                    { valid = false; break; }
                    // F12 combines compatible profile/basis/precision only. Final assessment
                    // also validates the full residue definition and processing graph.
                    var dot = bound.Upper.IndexOf('.');
                    profiles[i] = $"{bound.ProfileCode}:{(dot < 0 ? 0 : bound.Upper.Length - dot - 1)}";
                }
                if (valid) inputs.Add(new(candidate, quality, residue, blend ? JsonSerializer.Serialize(profiles) : ""));
            }

            foreach (var profile in inputs.GroupBy(input => input.ResidueProfile))
            {
                ct.ThrowIfCancellationRequested();
                var remainingMs = (budget - watch.Elapsed).TotalMilliseconds;
                if (remainingMs < 1 || attempts.Count >= maxAttempts) break;
                var input = profile.ToArray();
                var candidates = input.Select(x => x.Candidate).ToArray();
                if (candidates.Sum(c => c.AvailableKg) < group.InputKg ||
                    group.ProductKeys.Any(key => candidates.All(c => Key(c) != key))) continue;
                var rows = Rows(group, input, limits, thresholds);
                var massCosts = priority switch
                {
                    BlendPlanningObjective.RetainHeadRice => candidates.Select(c => c.ProductIdentity == "HEAD_RICE" ? 1m : 0m).ToArray(),
                    BlendPlanningObjective.LowestMoisture => input.Select(x => x.Quality[moistureIndex] / group.InputKg).ToArray(),
                    _ => null
                };
                decimal[][]? ratios = null;
                if (priority == BlendPlanningObjective.ResidueHeadroom)
                {
                    try
                    {
                        ratios = thresholds.Select((threshold, i) => (threshold, i)).Where(x => x.threshold.Value > 0)
                            .Select(x => input.Select(candidate => candidate.Residue[x.i] / x.threshold.Value).ToArray()).ToArray();
                    }
                    catch (OverflowException) { continue; }
                }
                var run = BlendMilpSolver.Solve(candidates, group.InputKg, rows, priority, (int)remainingMs,
                    massCosts, ratios, directStock: !blend);
                BlendAssessmentResult? assessment = null;
                if (run.Quantities is { } quantities)
                {
                    var allocations = candidates.Select((c, i) => new GreedyStockSelection.Allocation(
                        c.LotId, c.LocationId, c.LotCode, quantities[i], c.CandidateRevision))
                        .Where(x => x.QuantityKg > 0).ToArray();
                    var selection = new GreedyStockSelection.Result("MASS_COVERED", group.Mode, group.Id,
                        group.InputKg, 0, allocations);
                    assessment = await assess(selection, ct);
                    if (assessment.Status == "PASS")
                    {
                        var count = allocations.Select(x => x.LocationId).Distinct().Count();
                        var score = priority switch
                        {
                            BlendPlanningObjective.FewestLots => allocations.Select(a => a.LotId).Distinct().Count(),
                            BlendPlanningObjective.RetainHeadRice or BlendPlanningObjective.LowestMoisture =>
                                quantities.Select((kg, i) => kg * massCosts![i]).Sum(),
                            BlendPlanningObjective.ResidueHeadroom => ResidueHeadroom(assessment.Findings)?.Ratio ?? 0,
                            _ => count
                        };
                        if (bestScore is null || score < bestScore)
                        {
                            best = selection;
                            bestCount = count;
                            bestScore = score;
                            try
                            {
                                bestRange = blend ? AdjustableRange(candidates, quantities, group.InputKg, rows) : null;
                            }
                            catch (OverflowException) { bestRange = null; }
                        }
                    }
                }
                attempts.Add(new(group.Mode, group.Id, candidates.Length, group.InputKg, run.Status,
                    run.Variables, run.Constraints, run.ElapsedMs, run.Objective, run.BestBound, run.Nodes,
                    assessment?.Status));
                var proven = priority switch
                {
                    BlendPlanningObjective.FewestLocations or BlendPlanningObjective.FewestLots => bestScore == positionLowerBound,
                    BlendPlanningObjective.RetainHeadRice or BlendPlanningObjective.LowestMoisture or
                        BlendPlanningObjective.ResidueHeadroom => bestScore == 0,
                    _ => false
                };
                if (best is not null && (priority == BlendPlanningObjective.None || proven))
                    return new(best, attempts, proven, bestCount, bestRange);
            }
        }
        // Coefficients from F12 individually rounded upward are conservative.
        // An optimum of that model alone is not proof for the complete F14 domain.
        return new(best, attempts, false, bestCount, bestRange);
    }

    private static IEnumerable<Group> Groups(PlanningCandidateSnapshot snapshot, string target,
        Func<Guid, decimal?> yieldTarget)
    {
        var eligible = snapshot.Candidates.Where(c => c.InventoryEligible && c.AvailableKg > 0)
            .OrderBy(c => c.LotId).ThenBy(c => c.LocationId).ToArray();
        yield return new("DIRECT_STOCK", null, null, null, snapshot.TargetMassKg, [],
            eligible.Where(c => ExportProductMatch.IsDirect(c, target, snapshot.CatalogCommodityId,
                snapshot.OrderCommodityCode)).ToArray());
        if (target != "BLENDED_RICE" || snapshot.Compatibilities is null) yield break;
        foreach (var policy in snapshot.Compatibilities.Value.EnumerateArray().OrderBy(x => x.GetProperty("id").GetGuid()))
        {
            var id = policy.GetProperty("id").GetGuid();
            if (!ExportProductMatch.TryMatchBlendOutput(policy, snapshot.CatalogCommodityId, snapshot.OrderCommodityCode,
                    snapshot.CommodityCodes, out var outputCatalogId) ||
                !policy.TryGetProperty("outputLedgerCommodityId", out var output) ||
                output.ValueKind != JsonValueKind.String || !output.TryGetGuid(out var outputId) ||
                yieldTarget(id) is not { } inputKg) continue;
            var keys = policy.GetProperty("inputProducts").EnumerateArray()
                .Select(x => x.GetString()!).Distinct(StringComparer.Ordinal).ToArray();
            if (keys.Length == 0) continue;
            yield return new("BLEND_INPUT_STOCK", id, outputId, outputCatalogId, inputKg, keys,
                eligible.Where(c => (c.Stage is "HEAD_RICE" or "BROKEN_RICE" or "BLENDED_RICE") &&
                    keys.Contains(Key(c), StringComparer.Ordinal)).ToArray());
        }
    }

    private static string Key(PlanningCandidate c) => $"{c.LedgerCommodityId}:{c.ProductIdentity}";

    private static IReadOnlyList<BlendMilpSolver.Row> Rows(Group group, Input[] input,
        IReadOnlyList<SpecificationLimit> limits, IReadOnlyList<Threshold> thresholds)
    {
        var rows = new List<BlendMilpSolver.Row>();
        if (group.Mode == "DIRECT_STOCK") return rows;
        foreach (var key in group.ProductKeys)
            rows.Add(new(BlendMilpSolver.Quantum, null, input.Select(i => Key(i.Candidate) == key ? 1m : 0m).ToArray()));
        for (var i = 0; i < limits.Count; i++)
        {
            if (limits[i].MinValue is { } min)
                rows.Add(new(0, null, input.Select(x => x.Quality[i] - min).ToArray()));
            if (limits[i].MaxValue is { } max)
                rows.Add(new(null, 0, input.Select(x => x.Quality[i] - max).ToArray()));
        }
        for (var i = 0; i < thresholds.Count; i++)
        {
            // F12 rounds the hypothetical upper result upward to its published precision.
            // Lower the numeric threshold to the largest value representable at that precision.
            var scale = (decimal)BigInteger.Pow(10, (decimal.GetBits(input[0].Residue[i])[3] >> 16) & 0xff);
            var threshold = thresholds[i].Value;
            if (threshold <= decimal.MaxValue / scale)
                threshold = decimal.Floor(threshold * scale) / scale;
            rows.Add(new(null, 0, input.Select(x => x.Residue[i] - threshold).ToArray()));
        }
        return rows;
    }

    private static RatioRange? AdjustableRange(PlanningCandidate[] candidates, decimal[] quantities,
        decimal total, IReadOnlyList<BlendMilpSolver.Row> rows)
    {
        var selected = Enumerable.Range(0, quantities.Length).Where(i => quantities[i] > 0).ToArray();
        if (selected.Length != 2) return null;
        var a = selected[0];
        var b = selected[1];
        var quantum = BlendMilpSolver.Quantum;
        var min = Math.Max(quantum, total - candidates[b].AvailableKg);
        var max = Math.Min(candidates[a].AvailableKg, total - quantum);
        foreach (var row in rows)
        {
            var slope = row.Coefficients[a] - row.Coefficients[b];
            var fixedTerm = row.Coefficients[b] * total;
            if (slope == 0)
            {
                if (fixedTerm < row.Lower || fixedTerm > row.Upper) return null;
                continue;
            }
            if (row.Lower is { } lower) ApplyBound((lower - fixedTerm) / slope, slope > 0);
            if (row.Upper is { } upper) ApplyBound((upper - fixedTerm) / slope, slope < 0);
        }
        if (min >= max) return null;
        min = decimal.Ceiling(min / quantum) * quantum;
        max = decimal.Floor(max / quantum) * quantum;
        if (min >= max || quantities[a] < min || quantities[a] > max) return null;
        return Satisfies(min) && Satisfies(max) ? new(ExportRules.Text(min), ExportRules.Text(max)) : null;

        void ApplyBound(decimal value, bool lower)
        {
            if (lower) min = Math.Max(min, value);
            else max = Math.Min(max, value);
        }

        bool Satisfies(decimal kg) => rows.All(row =>
        {
            var value = kg * row.Coefficients[a] + (total - kg) * row.Coefficients[b];
            return !(value < row.Lower || value > row.Upper);
        });
    }

    internal static decimal? MinimumBlendInput(decimal target, decimal fraction)
    {
        if (fraction is <= 0 or > 1) return null;
        try
        {
            var yield = ResidueRational.Parse(fraction.ToString(CultureInfo.InvariantCulture));
            var targetUnits = new BigInteger(target / BlendMilpSolver.Quantum);
            var steps = (targetUnits + yield.Numerator - 1) / yield.Numerator;
            return (decimal)(steps * yield.Denominator) * BlendMilpSolver.Quantum;
        }
        catch (OverflowException) { return null; }
    }

    internal static (string? Substance, decimal Ratio)? ResidueHeadroom(IEnumerable<BlendAssessmentFinding> findings)
    {
        (string? Substance, decimal Ratio)? worst = null;
        foreach (var finding in findings)
        {
            if (finding.Code != "RESIDUE_LIMIT" ||
                !decimal.TryParse(finding.Actual, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var actual) ||
                !decimal.TryParse(finding.Limit, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var limit) || limit <= 0)
                continue;
            decimal ratio;
            try { ratio = actual / limit; }
            catch (OverflowException) { continue; }
            if (worst is null || ratio > worst.Value.Ratio) worst = (finding.Reference, ratio);
        }
        return worst;
    }

    internal static bool ProvesFewestPositions(PlanningCandidateSnapshot snapshot, string target,
        Func<Guid, decimal?> yieldTarget, GreedyStockSelection.Result selection, bool useLotCount)
    {
        var minimum = Groups(snapshot, target, yieldTarget).Select(
            group => PositionLowerBound(group.Candidates, group.InputKg, useLotCount)).DefaultIfEmpty(int.MaxValue).Min();
        return selection.Allocations.Select(a => useLotCount ? a.LotId : a.LocationId).Distinct().Count() == minimum;
    }

    private static int PositionLowerBound(IReadOnlyList<PlanningCandidate> candidates, decimal target, bool useLotCount)
    {
        var total = 0m;
        var count = 0;
        foreach (var capacity in candidates.GroupBy(c => useLotCount ? c.LotId : c.LocationId)
            .Select(g => g.Sum(c => c.AvailableKg)).OrderDescending())
        {
            total += capacity;
            count++;
            if (total >= target) return count;
        }
        return int.MaxValue;
    }
}
