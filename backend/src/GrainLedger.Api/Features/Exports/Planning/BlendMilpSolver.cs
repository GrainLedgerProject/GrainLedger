using System.Diagnostics;
using Google.OrTools.LinearSolver;

namespace GrainLedger.Api.Features.Exports;

internal enum BlendPlanningObjective { None, FewestLocations, RetainHeadRice, FewestLots, LowestMoisture, ResidueHeadroom }

internal static class BlendMilpSolver
{
    internal const decimal Quantum = 0.000001m;
    internal sealed record Row(decimal? Lower, decimal? Upper, IReadOnlyList<decimal> Coefficients);
    internal sealed record Run(string Status, int Variables, int Constraints, long ElapsedMs,
        double? Objective, double? BestBound, long? Nodes, decimal[]? Quantities);

    internal static Run Solve(IReadOnlyList<PlanningCandidate> candidates, decimal inputKg,
        IReadOnlyList<Row> rows, BlendPlanningObjective priority, int timeLimitMs,
        decimal[]? massCosts = null, IReadOnlyList<decimal[]>? residueRatios = null, bool directStock = false)
    {
        var elapsed = Stopwatch.StartNew();
        using var solver = Solver.CreateSolver("SCIP");
        if (solver is null)
            return new("SOLVER_UNAVAILABLE", 0, 0, elapsed.ElapsedMilliseconds, null, null, null, null);
        solver.SetTimeLimit(timeLimitMs);
        solver.SetSolverSpecificParametersAsString("numerics/feastol = 1e-9\nlimits/gap = 0");
        var caps = candidates.Select(c => Math.Min(c.AvailableKg, inputKg)).ToArray();
        var x = caps.Select((cap, i) => solver.MakeNumVar(0, (double)cap, $"kg_{i}")).ToArray();
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
        if (quantities is not null && !Satisfies(quantities, rows))
            quantities = RepairOnGrid(quantities, caps, rows,
                massCosts,
                timeLimitMs - (int)elapsed.ElapsedMilliseconds);
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

    // Move integer micro-kg around the rounded solution. The shifted model
    // keeps small integer variables even for large orders and checks the result
    // in decimal, so rounding at a quality boundary cannot create a false PASS.
    private static decimal[]? RepairOnGrid(decimal[] rounded, decimal[] caps, IReadOnlyList<Row> rows,
        decimal[]? objectiveCoefficients, int milliseconds)
    {
        if (milliseconds < 1) return null;
        using var repair = Solver.CreateSolver("SCIP");
        if (repair is null) return null;
        repair.SetTimeLimit(Math.Min(milliseconds, 100));
        repair.SetSolverSpecificParametersAsString("numerics/feastol = 1e-9");
        var radius = Math.Max(4, rounded.Length + 1);
        var delta = rounded.Select((kg, i) => repair.MakeIntVar(
            kg > 0 ? -(double)Math.Min(radius, kg / Quantum) : 0,
            kg > 0 ? (double)Math.Min(radius, (caps[i] - kg) / Quantum) : 0, $"delta_{i}")).ToArray();
        var mass = repair.MakeConstraint(0, 0);
        foreach (var variable in delta) mass.SetCoefficient(variable, 1);
        foreach (var row in rows)
        {
            var offset = Dot(row.Coefficients, rounded);
            var scale = row.Coefficients.Select(Math.Abs).DefaultIfEmpty().Max();
            if (scale == 0) scale = 1;
            var shifted = repair.MakeConstraint(
                row.Lower is { } lo ? (double)((lo - offset) / Quantum / scale) : double.NegativeInfinity,
                row.Upper is { } hi ? (double)((hi - offset) / Quantum / scale) : double.PositiveInfinity);
            for (var i = 0; i < delta.Length; i++)
                if (row.Coefficients[i] != 0)
                    shifted.SetCoefficient(delta[i], (double)(row.Coefficients[i] / scale));
        }
        if (objectiveCoefficients is not null)
        {
            var objective = repair.Objective();
            for (var i = 0; i < delta.Length; i++)
                if (objectiveCoefficients[i] != 0) objective.SetCoefficient(delta[i], (double)objectiveCoefficients[i]);
            objective.SetMinimization();
        }
        if (repair.Solve() is not (Solver.ResultStatus.OPTIMAL or Solver.ResultStatus.FEASIBLE)) return null;
        var adjusted = rounded.Select((kg, i) => kg + (decimal)Math.Round(delta[i].SolutionValue()) * Quantum).ToArray();
        return adjusted.Sum() == rounded.Sum() && adjusted.Select((kg, i) => kg >= 0 && kg <= caps[i]).All(ok => ok)
            && Satisfies(adjusted, rows) ? adjusted : null;
    }

    // Normalize only numerical noise. Do not add a position the solver left unused.
    // F14 rechecks every adjusted result with exact quantities.
    private static decimal[]? Normalize(double[] raw, decimal[] caps, decimal target)
    {
        var rounded = raw.Select((mass, i) => Math.Clamp(
            decimal.Round((decimal)mass, 6), 0, caps[i])).ToArray();
        var difference = target - rounded.Sum();
        if (Math.Abs(difference) > (raw.Length + 1) * Quantum) return null;
        foreach (var i in Enumerable.Range(0, raw.Length).Where(i => rounded[i] > 0))
        {
            var change = Math.Clamp(difference, -rounded[i], caps[i] - rounded[i]);
            rounded[i] += change;
            difference -= change;
            if (difference == 0) break;
        }
        return difference == 0 ? rounded : null;
    }
}
