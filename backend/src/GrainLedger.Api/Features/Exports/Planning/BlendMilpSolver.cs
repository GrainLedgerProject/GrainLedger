using System.Diagnostics;
using Google.OrTools.LinearSolver;

namespace GrainLedger.Api.Features.Exports;

internal enum BlendPlanningObjective { None, FewestLocations, RetainHeadRice, FewestLots, LowestMoisture, ResidueHeadroom }

internal static class BlendMilpSolver
{
    // Operational planning uses whole kg; Ledger measurements retain their own 6-decimal precision.
    internal const decimal Quantum = 1m;
    internal sealed record Row(decimal? Lower, decimal? Upper, IReadOnlyList<decimal> Coefficients);
    internal sealed record Run(string Status, int Variables, int Constraints, long ElapsedMs,
        double? Objective, double? BestBound, long? Nodes, decimal[]? Quantities);

    internal static Run Solve(IReadOnlyList<PlanningCandidate> candidates, decimal inputKg,
        IReadOnlyList<Row> rows, BlendPlanningObjective priority, int timeLimitMs,
        decimal[]? massCosts = null, IReadOnlyList<decimal[]>? residueRatios = null, bool directStock = false)
    {
        var elapsed = Stopwatch.StartNew();
        if (inputKg <= 0 || decimal.Truncate(inputKg) != inputKg)
            return new("WHOLE_KG_TARGET_REQUIRED", 0, 0, 0, null, null, null, null);
        using var solver = Solver.CreateSolver("SCIP");
        if (solver is null)
            return new("SOLVER_UNAVAILABLE", 0, 0, elapsed.ElapsedMilliseconds, null, null, null, null);
        solver.SetTimeLimit(timeLimitMs);
        solver.SetSolverSpecificParametersAsString("numerics/feastol = 1e-9\nlimits/gap = 0");
        var caps = candidates.Select(c => decimal.Floor(Math.Min(c.AvailableKg, inputKg))).ToArray();
        var x = caps.Select((cap, i) => solver.MakeIntVar(0, (double)cap, $"kg_{i}")).ToArray();
        var mass = solver.MakeConstraint((double)inputKg, (double)inputKg);
        foreach (var variable in x) mass.SetCoefficient(variable, 1);
        foreach (var row in rows)
        {
            var scale = row.Coefficients.Select(Math.Abs).DefaultIfEmpty().Max();
            if (scale == 0) scale = 1;
            var constraint = solver.MakeConstraint(
                row.Lower is { } lower ? (double)(lower / scale) : double.NegativeInfinity,
                row.Upper is { } upper ? (double)(upper / scale) : double.PositiveInfinity);
            for (var i = 0; i < x.Length; i++)
                if (row.Coefficients[i] != 0)
                    constraint.SetCoefficient(x[i], (double)(row.Coefficients[i] / scale));
        }

        var objective = solver.Objective();
        if (priority is BlendPlanningObjective.FewestLocations or BlendPlanningObjective.FewestLots)
            foreach (var location in candidates.Select((candidate, index) => (candidate, index))
                .GroupBy(item => priority == BlendPlanningObjective.FewestLots ? item.candidate.LotId : item.candidate.LocationId))
            {
                var used = solver.MakeBoolVar($"location_{location.Key:N}");
                var link = solver.MakeConstraint(double.NegativeInfinity, 0);
                link.SetCoefficient(used, -(double)Math.Min(inputKg, location.Sum(item => caps[item.index])));
                foreach (var item in location) link.SetCoefficient(x[item.index], 1);
                objective.SetCoefficient(used, 1);
            }
        if (massCosts is not null)
            for (var i = 0; i < x.Length; i++)
                if (massCosts[i] != 0) objective.SetCoefficient(x[i], (double)massCosts[i]);
        if (residueRatios is { Count: > 0 })
        {
            // Express the worst ratio in kg-equivalent units to avoid tiny coefficients.
            var worst = solver.MakeNumVar(0, (double)inputKg, "worst_residue");
            objective.SetCoefficient(worst, 1 / (double)inputKg);
            if (directStock)
                for (var i = 0; i < x.Length; i++)
                {
                    var used = solver.MakeBoolVar($"residue_input_{i}");
                    var capacity = solver.MakeConstraint(double.NegativeInfinity, 0);
                    capacity.SetCoefficient(x[i], 1);
                    capacity.SetCoefficient(used, -(double)caps[i]);
                    var limit = solver.MakeConstraint(double.NegativeInfinity, 0);
                    limit.SetCoefficient(worst, -1);
                    limit.SetCoefficient(used, (double)(residueRatios.Max(r => r[i]) * inputKg));
                }
            else
                foreach (var ratios in residueRatios)
                {
                    var limit = solver.MakeConstraint(double.NegativeInfinity, 0);
                    var scale = Math.Max(1, ratios.Max());
                    limit.SetCoefficient(worst, (double)(-1 / scale));
                    for (var i = 0; i < x.Length; i++)
                        limit.SetCoefficient(x[i], (double)(ratios[i] / scale));
                }
        }
        objective.SetMinimization();
        var status = solver.Solve();
        var hasSolution = status is Solver.ResultStatus.OPTIMAL or Solver.ResultStatus.FEASIBLE;
        var raw = hasSolution ? x.Select(v => v.SolutionValue()).ToArray() : null;
        var quantities = raw is null ? null : Normalize(raw, caps, inputKg);
        // A rounded display is not a valid plan. Verify the integer solver result
        // with decimal arithmetic before handing it to the F12/F14 assessment.
        if (quantities is not null && !Satisfies(quantities, rows)) quantities = null;
        var hasObjective = priority != BlendPlanningObjective.None;
        var bound = hasObjective && hasSolution ? objective.BestBound() : double.NaN;
        return new(status.ToString(), solver.NumVariables(), solver.NumConstraints(),
            elapsed.ElapsedMilliseconds, hasSolution && hasObjective ? objective.Value() : null,
            double.IsFinite(bound) ? bound : null, solver.Nodes(), quantities);
    }

    private static bool Satisfies(decimal[] kg, IReadOnlyList<Row> rows) => rows.All(row =>
    {
        var value = Dot(row.Coefficients, kg);
        return !(value < row.Lower || value > row.Upper);
    });

    private static decimal Dot(IReadOnlyList<decimal> coefficients, decimal[] values) =>
        values.Select((kg, i) => coefficients[i] * kg).Sum();

    // Remove only solver integrality noise, never redistribute kg after solving.
    private static decimal[]? Normalize(double[] raw, decimal[] caps, decimal target)
    {
        var kg = new decimal[raw.Length];
        for (var i = 0; i < raw.Length; i++)
        {
            if (!double.IsFinite(raw[i])) return null;
            var value = (decimal)raw[i];
            var integer = decimal.Round(value, 0, MidpointRounding.AwayFromZero);
            if (Math.Abs(value - integer) > 0.000001m || integer < 0 || integer > caps[i]) return null;
            kg[i] = integer;
        }
        return kg.Sum() == target ? kg : null;
    }
}
