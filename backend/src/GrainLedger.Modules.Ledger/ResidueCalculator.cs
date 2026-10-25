using GrainLedger.Modules.Catalogs;
using System.Globalization;
using System.Numerics;

namespace GrainLedger.Modules.Ledger;

public sealed record ResidueParent(Guid LotId, string QuantityKg);
public sealed record ResidueNode(Guid LotId, Guid CompanyId, Guid CommodityId, string Stage, string ProductIdentity,
    DateTimeOffset At, string Kind, Guid? OperationId, Guid? EventId, IReadOnlyList<ResidueParent> Parents,
    bool Inconsistent = false, string? Conditions = null);
public sealed record ResidueProvenance(string Kind, Guid SourceLotId, Guid? ReportId = null, Guid? ResultId = null,
    Guid? GroupVersionId = null, Guid? SelectionId = null, Guid? RuleId = null,
    Guid? SourceId = null, Guid? AssociationId = null, Guid? ResultSetId = null,
    string? UncertaintyMode = null, string? RelativeUncertaintyPercent = null, string? AbsoluteUncertainty = null,
    string? OriginalUnit = null, string? CanonicalUnit = null, string? ConcentrationScale = null,
    string? OriginalNumericValue = null, string? OriginalLod = null, string? OriginalLoq = null,
    string? OriginalAbsoluteUncertainty = null, string? OriginalReportingThreshold = null);
public sealed record ResidueRuleReference(Guid Id, int Version, string Kind, string EvidenceReference,
    string? PolicyOrigin = null, Guid? PolicySettingId = null, int? PolicySettingVersion = null, string? RuleCode = null);
public sealed record ResidueDependency(Guid LotId, Guid? ParentLotId, Guid? EventId, Guid? OperationId, string Kind, string? QuantityKg);
public sealed record ResidueSourceBound(Guid LotId, Guid SubstanceId, string? Lower, string? Upper, string? Unit,
    string? Basis, int? DecimalPlaces, IReadOnlyList<string> Reasons, IReadOnlyList<ResidueProvenance> Provenance,
    IReadOnlyList<ResidueRuleReference> Rules, string ResidueDefinition = "");
public sealed record ResidueFactor(Guid SubstanceId, Guid? CommodityId, string Kind, string InputStage, string OutputStage,
    string InputBasis, string OutputBasis, string Unit, string Lower, string Upper, int DecimalPlaces,
    string Semantics, string? Conditions, ResidueRuleReference Rule, string ResidueDefinition = "",
    string? OutputFraction = null, bool HomogeneousMixing = false, DateTimeOffset? EffectiveFrom = null,
    DateTimeOffset? EffectiveTo = null, Guid? SupersedesId = null);
public sealed record ResidueResult(Guid SubstanceId, string Status, string? Lower, string? Upper, string? Unit,
    string? Basis, IReadOnlyList<string> Reasons, IReadOnlyList<ResidueProvenance> Provenance,
    IReadOnlyList<ResidueRuleReference> Rules, IReadOnlyList<ResidueDependency> Dependencies);

// BigInteger rationals retain positive exposure even below accounting's micro-kg allocation.
public readonly record struct ResidueRational : IComparable<ResidueRational>
{
    public BigInteger Numerator { get; }
    public BigInteger Denominator { get; }
    public ResidueRational(BigInteger numerator, BigInteger denominator)
    {
        if (denominator <= 0) throw new ArgumentOutOfRangeException(nameof(denominator));
        var gcd = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        Numerator = numerator / gcd; Denominator = denominator / gcd;
    }
    public static ResidueRational Zero => new(0, 1);
    public static ResidueRational Parse(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 256) throw new FormatException("Exact decimal required");
        var negative = value[0] == '-';
        var start = negative ? 1 : 0;
        var dot = value.IndexOf('.');
        if (start == value.Length || dot == start || dot == value.Length - 1) throw new FormatException("Exact decimal required");
        for (var i = start; i < value.Length; i++)
            if (value[i] is < '0' or > '9' && i != dot) throw new FormatException("Exact decimal required");
        var digits = dot < 0 ? value[start..] : value[start..dot] + value[(dot + 1)..];
        var n = BigInteger.Parse(digits, CultureInfo.InvariantCulture);
        return new(negative ? -n : n, BigInteger.Pow(10, dot < 0 ? 0 : value.Length - dot - 1));
    }
    public int CompareTo(ResidueRational other) => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
    public static ResidueRational operator +(ResidueRational a, ResidueRational b) => new(a.Numerator * b.Denominator + b.Numerator * a.Denominator, a.Denominator * b.Denominator);
    public static ResidueRational operator -(ResidueRational a, ResidueRational b) => new(a.Numerator * b.Denominator - b.Numerator * a.Denominator, a.Denominator * b.Denominator);
    public static ResidueRational operator *(ResidueRational a, ResidueRational b) => new(a.Numerator * b.Numerator, a.Denominator * b.Denominator);
    public static ResidueRational operator /(ResidueRational a, ResidueRational b) => b.Numerator > 0
        ? new(a.Numerator * b.Denominator, a.Denominator * b.Numerator) : throw new DivideByZeroException();
    public string ExactDecimal()
    {
        var denominator = Denominator;
        var twos = 0; var fives = 0;
        while (denominator % 2 == 0) { denominator /= 2; twos++; }
        while (denominator % 5 == 0) { denominator /= 5; fives++; }
        if (denominator != 1) throw new FormatException("Terminating decimal required");
        var places = Math.Max(twos, fives);
        var units = Numerator * BigInteger.Pow(2, places - twos) * BigInteger.Pow(5, places - fives);
        var text = BigInteger.Abs(units).ToString(CultureInfo.InvariantCulture).PadLeft(places + 1, '0');
        return (units.Sign < 0 ? "-" : "") + (places == 0 ? text : text[..^places] + "." + text[^places..]);
    }
    public string Round(int places, bool upward)
    {
        if (places is < 0 or > 18) throw new ArgumentOutOfRangeException(nameof(places));
        var scale = BigInteger.Pow(10, places);
        var units = BigInteger.DivRem(Numerator * scale, Denominator, out var remainder);
        if (remainder != 0 && upward && Numerator > 0) units++;
        if (remainder != 0 && !upward && Numerator < 0) units--;
        var sign = units < 0 ? "-" : "";
        var text = BigInteger.Abs(units).ToString(CultureInfo.InvariantCulture).PadLeft(places + 1, '0');
        return places == 0 ? sign + text : sign + text[..^places] + "." + text[^places..];
    }
}

public static class ResidueCalculator
{
    public const string EngineVersion = "residue-policy-exact-2";
    private sealed record Bound(ResidueRational? Lower, ResidueRational? Upper, string? Unit, string? Basis,
        int? Precision, string Definition, IReadOnlyList<string> Reasons, ResidueSourceBound? Source,
        ResidueFactor? Factor, IReadOnlyList<Guid> Inputs, IReadOnlyList<ResidueRuleReference>? AdditionalRules = null);

    public static IReadOnlyList<ResidueResult> Calculate(Guid targetLotId, IReadOnlyList<Guid> substanceIds,
        IReadOnlyList<ResidueNode> nodes, IReadOnlyList<ResidueSourceBound> sources, IReadOnlyList<ResidueFactor> factors,
        ResiduePolicySnapshot? policy = null)
    {
        var graph = nodes.ToDictionary(x => x.LotId);
        var sourceMap = sources.GroupBy(x => (x.LotId, x.SubstanceId)).ToDictionary(x => x.Key, x => x.ToArray());
        var factorMap = factors.GroupBy(x => x.SubstanceId).ToDictionary(x => x.Key, x => x.ToArray());
        var factorPredecessors = factors.DistinctBy(x => x.Rule.Id).ToDictionary(x => x.Rule.Id, x => x.SupersedesId);
        var order = new List<ResidueNode>();
        var marks = new Dictionary<Guid, int>();
        var cyclic = false;
        var stack = new Stack<(Guid Id, bool Exit)>(); stack.Push((targetLotId, false));
        while (stack.TryPop(out var item))
        {
            if (item.Exit) { marks[item.Id] = 2; order.Add(graph[item.Id]); continue; }
            if (marks.TryGetValue(item.Id, out var mark)) { if (mark == 1) cyclic = true; continue; }
            if (!graph.TryGetValue(item.Id, out var node)) continue;
            marks[item.Id] = 1; stack.Push((item.Id, true));
            foreach (var parent in node.Parents) stack.Push((parent.LotId, false));
        }
        // One linear anchor pass per configured input stage; cumulative paths never rescan long chains.
        var anchorMaps = new Dictionary<string, Dictionary<Guid, Guid?>>(StringComparer.Ordinal);
        foreach (var stage in factors.Where(x => x.Semantics == "CUMULATIVE").Select(x => x.InputStage).Distinct())
        {
            var anchors = new Dictionary<Guid, Guid?>();
            foreach (var node in order)
            {
                var validParent = node.Parents.Count == 1 && PositiveMass(node.Parents[0].QuantityKg);
                anchors[node.LotId] = node.Inconsistent ? null : node.Stage == stage ? node.LotId
                    : validParent ? anchors.GetValueOrDefault(node.Parents[0].LotId) : null;
            }
            anchorMaps[stage] = anchors;
        }
        var dependencies = order.SelectMany(n => n.Parents.Count == 0
            ? [new ResidueDependency(n.LotId, null, n.EventId, n.OperationId, n.Kind, null)]
            : n.Parents.Select(p => new ResidueDependency(n.LotId, p.LotId, n.EventId, n.OperationId, n.Kind, p.QuantityKg))).ToArray();
        var results = new List<ResidueResult>();
        foreach (var substance in substanceIds)
        {
            var values = new Dictionary<Guid, Bound>();
            foreach (var node in order)
            {
                try { values[node.LotId] = Evaluate(node); }
                catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or DivideByZeroException)
                { values[node.LotId] = Unknown("Unsupported exact arithmetic or invalid interval", node.Parents.Select(x => x.LotId).ToArray()); }
            }
            var target = values.GetValueOrDefault(targetLotId) ?? Unknown("Target ancestry missing", []);
            if (cyclic) target = Unknown("Cyclic physical ancestry", [targetLotId]);
            var provenance = new HashSet<ResidueProvenance>();
            var rules = new HashSet<ResidueRuleReference>();
            var reasons = new HashSet<string>(target.Reasons);
            var visited = new HashSet<Guid>(); var pending = new Stack<Guid>(); pending.Push(targetLotId);
            while (pending.TryPop(out var id))
            {
                if (!visited.Add(id) || !values.TryGetValue(id, out var bound)) continue;
                foreach (var reason in bound.Reasons) reasons.Add(reason);
                if (bound.Source is { } source)
                { provenance.UnionWith(source.Provenance); rules.UnionWith(source.Rules); }
                if (bound.AdditionalRules is { } additional) rules.UnionWith(additional);
                if (bound.Factor is { } factor) rules.Add(factor.Rule);
                foreach (var input in bound.Inputs) pending.Push(input);
            }
            var complete = target.Lower is not null && target.Upper is not null && target.Precision is >= 0 and <= 18 && reasons.Count == 0;
            results.Add(new(substance, complete ? "COMPLETE" : "UNRESOLVED",
                complete ? target.Lower!.Value.Round(target.Precision!.Value, false) : null,
                complete ? target.Upper!.Value.Round(target.Precision!.Value, true) : null,
                target.Unit, target.Basis, reasons.Order(StringComparer.Ordinal).ToArray(),
                provenance.OrderBy(x => x.SourceLotId).ThenBy(x => x.ResultId).ToArray(),
                rules.OrderBy(x => x.Id).ToArray(), dependencies));

            Bound Evaluate(ResidueNode node)
            {
                var inputs = node.Parents.Select(x => x.LotId).ToArray();
                if (node.Inconsistent) return Unknown("Inconsistent or corrected ancestor applicability", inputs);
                if (node.Kind == "SOURCE")
                {
                    if (node.Parents.Count != 0) return Unknown("Source has unexplained ancestry", inputs);
                    if (!sourceMap.TryGetValue((node.LotId, substance), out var sourceCandidates)) return Unknown("No approved source evidence or fallback", []);
                    if (sourceCandidates.Length != 1) return Unknown("Conflicting source bounds", []);
                    var source = sourceCandidates[0];
                    if (source.Reasons.Count != 0 || source.Lower is null || source.Upper is null || source.DecimalPlaces is not (>= 0 and <= 18))
                        return new(null, null, source.Unit, source.Basis, source.DecimalPlaces, source.ResidueDefinition,
                            source.Reasons.Count == 0 ? ["Missing source interpretation or approved precision"] : source.Reasons, source, null, []);
                    var lower = ResidueRational.Parse(source.Lower); var upper = ResidueRational.Parse(source.Upper);
                    if (lower.CompareTo(ResidueRational.Zero) < 0 || lower.CompareTo(upper) > 0 || string.IsNullOrWhiteSpace(source.Unit) || string.IsNullOrWhiteSpace(source.Basis))
                        return Unknown("Invalid approved source interval", []);
                    return new(lower, upper, source.Unit, source.Basis, source.DecimalPlaces, source.ResidueDefinition, [], source, null, []);
                }
                if (inputs.Length == 0) return Unknown("Missing physical input ancestry", inputs);
                if (!factorMap.TryGetValue(substance, out var versions)) return Unknown("No applicable approved processing rule", inputs);
                var available = versions.Where(x => (x.EffectiveFrom is null || node.At >= x.EffectiveFrom)
                    && (x.EffectiveTo is null || node.At < x.EffectiveTo)).ToArray();
                var superseded = new HashSet<Guid>();
                foreach (var version in available)
                {
                    var id = version.Rule.Id;
                    while (factorPredecessors.TryGetValue(id, out var predecessor) && predecessor is { } prior && superseded.Add(prior))
                        id = prior;
                }
                var candidates = available.Where(factor => !superseded.Contains(factor.Rule.Id)
                    && factor.Kind == node.Kind && factor.OutputStage == node.Stage
                    && (factor.CommodityId is null || factor.CommodityId == node.CommodityId)
                    && (factor.OutputFraction is null || factor.OutputFraction == node.ProductIdentity)
                    && (factor.Conditions == "UNCONDITIONAL" || (factor.Conditions ?? "") == (node.Conditions ?? ""))).ToArray();
                var selected = new List<(ResidueFactor Factor, Guid Anchor)>();
                foreach (var physical in node.Parents)
                {
                    var matches = new List<(ResidueFactor Factor, Guid Anchor)>();
                    ResidueFactor? blockedCumulative = null;
                    foreach (var factor in candidates)
                    {
                        var id = factor.Semantics == "CUMULATIVE"
                            ? anchorMaps[factor.InputStage].GetValueOrDefault(physical.LotId) : physical.LotId;
                        if (factor.Semantics == "CUMULATIVE" && (factor.InputStage == factor.OutputStage
                            || id is null || node.Parents.Count > 1 && id != physical.LotId))
                        { blockedCumulative = factor; continue; }
                        if (id is null || !graph.TryGetValue(id.Value, out var match) || match.Stage != factor.InputStage) continue;
                        if (values.TryGetValue(id.Value, out var value) && value.Basis is not null
                            && (value.Basis != factor.InputBasis || value.Unit != factor.Unit || value.Definition != factor.ResidueDefinition)) continue;
                        matches.Add((factor, id.Value));
                    }
                    if (policy is not null)
                    {
                        var matchingIds = matches.Select(x => x.Factor.Rule.Id).ToHashSet();
                        if (matches.Count == 0 && blockedCumulative is not null) matchingIds.Add(blockedCumulative.Rule.Id);
                        var resolved = ResidueEvidenceResolver.SelectPolicy(policy, node.CompanyId, node.At,
                            rule => rule.Kind == "PROCESS" && rule.SubstanceId == substance && matchingIds.Contains(rule.Id));
                        var resolvedReferences = resolved.Rules.Select(x => ResidueEvidenceResolver.Reference(x.Rule, x.Setting)).ToArray();
                        if (resolved.Reason is not null)
                            return new(null, null, null, null, null, "", [resolved.Reason], null, null, inputs, resolvedReferences);
                        var referencesById = resolvedReferences.ToDictionary(x => x.Id);
                        matches = matches.Where(x => referencesById.ContainsKey(x.Factor.Rule.Id))
                            .Select(x => (x.Factor with { Rule = referencesById[x.Factor.Rule.Id] }, x.Anchor)).ToList();
                        blockedCumulative = blockedCumulative is not null && referencesById.TryGetValue(blockedCumulative.Rule.Id, out var reference)
                            ? blockedCumulative with { Rule = reference } : null;
                    }
                    if (matches.Count == 0 && blockedCumulative is not null)
                        return new(null, null, blockedCumulative.Unit, blockedCumulative.OutputBasis,
                            blockedCumulative.DecimalPlaces, blockedCumulative.ResidueDefinition,
                            ["Cumulative rule cannot rewind branching ancestry or changed mixture mass basis"], null, blockedCumulative, inputs);
                    if (matches.Count != 1)
                        return new(null, null, null, null, null, "",
                            [matches.Count == 0 ? "Missing compatible process basis, stage, fraction or effective rule" : "Conflicting approved processing rules"],
                            null, null, inputs, matches.Select(x => x.Factor.Rule).ToArray());
                    selected.Add(matches[0]);
                }
                var actual = selected.Select(x => x.Anchor).ToArray();
                var references = selected.Select(x => x.Factor.Rule).Distinct().ToArray();
                var output = selected[0].Factor;
                if (selected.Any(x => x.Factor.OutputBasis != output.OutputBasis || x.Factor.Unit != output.Unit
                    || x.Factor.ResidueDefinition != output.ResidueDefinition || x.Factor.DecimalPlaces != output.DecimalPlaces))
                    return new(null, null, null, null, null, "", ["Conflicting output basis, unit, residue definition or precision"],
                        null, null, actual, references);
                var low = ResidueRational.Zero; var high = ResidueRational.Zero; var mass = ResidueRational.Zero;
                for (var i = 0; i < actual.Length; i++)
                {
                    var factor = selected[i].Factor;
                    if (factor.Semantics is not ("STEP" or "CUMULATIVE") || factor.DecimalPlaces is < 0 or > 18)
                        return Unknown("Unsupported processing factor semantics or precision", actual);
                    if (node.Parents.Count > 1 && !factor.HomogeneousMixing)
                        return new(null, null, output.Unit, output.OutputBasis, output.DecimalPlaces, output.ResidueDefinition,
                            ["Homogeneous mixing is not approved"], null, null, actual, references);
                    var weight = ResidueRational.Parse(node.Parents[i].QuantityKg);
                    if (weight.CompareTo(ResidueRational.Zero) <= 0) return Unknown("Nonpositive physical input mass", actual);
                    if (!values.TryGetValue(actual[i], out var parent) || parent.Lower is null || parent.Upper is null || parent.Reasons.Count != 0)
                        return new(null, null, output.Unit, output.OutputBasis, output.DecimalPlaces, output.ResidueDefinition,
                            ["Unresolved positive parent dependency"], null, null, actual, references);
                    var factorLow = ResidueRational.Parse(factor.Lower); var factorHigh = ResidueRational.Parse(factor.Upper);
                    if (factorLow.CompareTo(ResidueRational.Zero) < 0 || factorLow.CompareTo(factorHigh) > 0) return Unknown("Invalid factor interval", actual);
                    low += weight * parent.Lower.Value * factorLow;
                    high += weight * parent.Upper.Value * factorHigh;
                    mass += weight;
                }
                return new(low / mass, high / mass, output.Unit, output.OutputBasis,
                    output.DecimalPlaces, output.ResidueDefinition, [], null, null, actual, references);
            }
        }
        return results;
    }
    private static bool PositiveMass(string value)
    {
        try { return ResidueRational.Parse(value).CompareTo(ResidueRational.Zero) > 0; }
        catch (FormatException) { return false; }
    }
    private static Bound Unknown(string reason, IReadOnlyList<Guid> inputs) => new(null, null, null, null, null, "", [reason], null, null, inputs);
}
