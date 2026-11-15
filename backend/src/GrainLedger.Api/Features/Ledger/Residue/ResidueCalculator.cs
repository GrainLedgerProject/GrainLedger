using GrainLedger.Api.Features.Catalogs;
using System.Globalization;
using System.Numerics;

namespace GrainLedger.Api.Features.Ledger;

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
    string? PolicyOrigin = null, Guid? PolicySettingId = null, int? PolicySettingVersion = null, string? RuleCode = null,
    string? ComponentKey = null);
public sealed record ResidueDependency(Guid LotId, Guid? ParentLotId, Guid? EventId, Guid? OperationId, string Kind, string? QuantityKg);
public sealed record ResidueSourceBound(Guid LotId, Guid SubstanceId, string? Lower, string? Upper, string? Unit,
    int? DecimalPlaces, IReadOnlyList<string> Reasons, IReadOnlyList<ResidueProvenance> Provenance,
    IReadOnlyList<ResidueRuleReference> Rules, string ResidueDefinition = "", string? ProfileCode = null);
public sealed record ResidueFactor(Guid SubstanceId, Guid? CommodityId, string Kind, string InputStage, string OutputStage,
    string Unit, string Lower, string Upper, int DecimalPlaces,
    string Semantics, string? Conditions, ResidueRuleReference Rule, string ResidueDefinition = "",
    string? OutputFraction = null, bool HomogeneousMixing = false, DateTimeOffset? EffectiveFrom = null,
    DateTimeOffset? EffectiveTo = null, Guid? SupersedesId = null, string ProfileCode = "", Guid StepId = default,
    string? AnchorStage = null);
public sealed record ResidueResult(Guid SubstanceId, string Status, string? Lower, string? Upper, string? Unit,
    IReadOnlyList<string> Reasons, IReadOnlyList<ResidueProvenance> Provenance,
    IReadOnlyList<ResidueRuleReference> Rules, IReadOnlyList<ResidueDependency> Dependencies,
    string? ProfileCode, Guid CommodityId, string ProductStage, string ProductIdentity);

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
    public const string EngineVersion = "residue-flow-as-received-3";
    private sealed record Bound(ResidueRational? Lower, ResidueRational? Upper, string? Unit,
        int? Precision, string Definition, string? ProfileCode, IReadOnlyList<string> Reasons, ResidueSourceBound? Source,
        IReadOnlyList<Guid> Inputs, IReadOnlyList<ResidueRuleReference> Rules);

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
        var anchorMaps = new Dictionary<string, Dictionary<Guid, (Guid? Anchor, ResidueRational? Mass)>>(StringComparer.Ordinal);
        foreach (var stage in factors.Where(x => x.Semantics == "CUMULATIVE" && x.AnchorStage is not null).Select(x => x.AnchorStage!).Distinct())
        {
            var anchors = new Dictionary<Guid, (Guid? Anchor, ResidueRational? Mass)>();
            foreach (var node in order)
            {
                if (node.Inconsistent) { anchors[node.LotId] = (null, null); continue; }
                if (node.Stage == stage) { anchors[node.LotId] = (node.LotId, null); continue; }
                if (node.Parents.Count != 1 || !PositiveMass(node.Parents[0].QuantityKg))
                { anchors[node.LotId] = (null, null); continue; }
                var previous = anchors.GetValueOrDefault(node.Parents[0].LotId);
                var mass = ResidueRational.Parse(node.Parents[0].QuantityKg);
                anchors[node.LotId] = previous.Anchor is not null && (previous.Mass is null || previous.Mass.Value.CompareTo(mass) == 0)
                    ? (previous.Anchor, mass) : (null, null);
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
                rules.UnionWith(bound.Rules);
                foreach (var input in bound.Inputs) pending.Push(input);
            }
            var complete = target.Lower is not null && target.Upper is not null && target.Precision is >= 0 and <= 18 && reasons.Count == 0;
            results.Add(new(substance, complete ? "COMPLETE" : "UNRESOLVED",
                complete ? target.Lower!.Value.Round(target.Precision!.Value, false) : null,
                complete ? target.Upper!.Value.Round(target.Precision!.Value, true) : null,
                target.Unit, reasons.Order(StringComparer.Ordinal).ToArray(),
                provenance.OrderBy(x => x.SourceLotId).ThenBy(x => x.ResultId).ToArray(),
                rules.OrderBy(x => x.Id).ThenBy(x => x.ComponentKey, StringComparer.Ordinal).ToArray(), dependencies,
                target.ProfileCode, graph.GetValueOrDefault(targetLotId)?.CommodityId ?? Guid.Empty,
                graph.GetValueOrDefault(targetLotId)?.Stage ?? "", graph.GetValueOrDefault(targetLotId)?.ProductIdentity ?? ""));

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
                    if (source.Reasons.Count != 0 || source.Lower is null || source.Upper is null || source.DecimalPlaces is not (>= 0 and <= 18)
                        || string.IsNullOrWhiteSpace(source.ProfileCode))
                        return new(null, null, source.Unit, source.DecimalPlaces, source.ResidueDefinition, source.ProfileCode,
                            source.Reasons.Count == 0 ? ["Missing source interpretation, profile or approved precision"] : source.Reasons, source, [], []);
                    var lower = ResidueRational.Parse(source.Lower); var upper = ResidueRational.Parse(source.Upper);
                    if (lower.CompareTo(ResidueRational.Zero) < 0 || lower.CompareTo(upper) > 0 || source.Unit != "mg/kg")
                        return Unknown("Invalid approved source interval", []);
                    return new(lower, upper, source.Unit, source.DecimalPlaces, source.ResidueDefinition, source.ProfileCode, [], source, [], []);
                }
                if (inputs.Length == 0) return Unknown("Missing physical input ancestry", inputs);
                if (node.Parents.Any(p => !PositiveMass(p.QuantityKg))) return Unknown("Nonpositive physical input mass", inputs);
                var parentBounds = inputs.Select(id => values.GetValueOrDefault(id)).ToArray();
                if (parentBounds.Any(x => x is null || x.Lower is null || x.Upper is null || x.Reasons.Count != 0 || x.ProfileCode is null))
                    return Unknown("Unresolved positive parent dependency", inputs);
                var code = parentBounds[0]!.ProfileCode!;
                if (parentBounds.Any(x => x!.ProfileCode != code)) return Unknown("Conflicting parent profile codes", inputs);
                if (!factorMap.TryGetValue(substance, out var versions)) return Unknown("Missing applicable profile process step", inputs);
                ResidueRuleReference? policyReference = null;
                Guid? selectedProfile = null;
                if (policy is not null)
                {
                    var resolved = ResidueEvidenceResolver.SelectPolicy(policy, node.CompanyId, node.At, node.CommodityId, substance, code);
                    if (resolved.Reason is not null || resolved.Rules.Count != 1)
                        return Unknown(resolved.Reason ?? "Missing adopted profile at process event", inputs);
                    var selectedProfilePolicy = resolved.Rules[0];
                    selectedProfile = selectedProfilePolicy.Rule.Id;
                    policyReference = ResidueEvidenceResolver.Reference(selectedProfilePolicy.Rule, selectedProfilePolicy.Setting);
                }
                var available = versions.Where(x => x.ProfileCode == code && (x.EffectiveFrom is null || node.At >= x.EffectiveFrom)
                    && (x.EffectiveTo is null || node.At < x.EffectiveTo)).ToArray();
                if (selectedProfile is not null) available = available.Where(x => x.Rule.Id == selectedProfile).ToArray();
                else
                {
                    var removed = new HashSet<Guid>();
                    foreach (var version in available)
                    {
                        var id = version.Rule.Id;
                        while (factorPredecessors.TryGetValue(id, out var previous) && previous is { } prior && removed.Add(prior)) id = prior;
                    }
                    available = available.Where(x => !removed.Contains(x.Rule.Id)).ToArray();
                }
                var selected = new List<(ResidueFactor Factor, Guid Anchor)>();
                foreach (var physical in node.Parents)
                {
                    if (!graph.TryGetValue(physical.LotId, out var physicalNode)) return Unknown("Missing physical input ancestry", inputs);
                    var candidates = available.Where(x => x.Kind == node.Kind && x.InputStage == physicalNode.Stage
                        && x.OutputStage == node.Stage && (x.CommodityId is null || x.CommodityId == node.CommodityId)
                        && (x.OutputFraction is null || x.OutputFraction == node.ProductIdentity)
                        && (x.Conditions == "UNCONDITIONAL" || x.Conditions == node.Conditions)).ToArray();
                    if (candidates.Length != 1)
                        return Unknown(candidates.Length == 0 ? $"Missing applicable profile process step for {physicalNode.Stage} to {node.Stage}" : "Conflicting approved process steps", inputs);
                    var factor = candidates[0];
                    if (policyReference is not null) factor = factor with { Rule = policyReference with { ComponentKey = factor.StepId.ToString() } };
                    Guid? anchor = physical.LotId;
                    if (factor.Semantics == "CUMULATIVE")
                    {
                        var path = factor.AnchorStage is not null && anchorMaps.TryGetValue(factor.AnchorStage, out var map)
                            ? map.GetValueOrDefault(physical.LotId) : default;
                        anchor = path.Anchor;
                        if (anchor is null || node.Parents.Count > 1 && anchor != physical.LotId
                            || path.Mass is { } pathMass && pathMass.CompareTo(ResidueRational.Parse(physical.QuantityKg)) != 0)
                            return Unknown("Cumulative rule cannot rewind branching ancestry or changed mixture mass basis", inputs);
                    }
                    selected.Add((factor, anchor.Value));
                }
                var references = selected.Select(x => x.Factor.Rule).Distinct().ToArray();
                var output = selected[0].Factor;
                if (selected.Any(x => x.Factor.Unit != output.Unit || x.Factor.ResidueDefinition != output.ResidueDefinition
                    || x.Factor.DecimalPlaces != output.DecimalPlaces))
                    return Unknown("Conflicting unit, residue definition or precision", inputs);
                var low = ResidueRational.Zero; var high = ResidueRational.Zero; var mass = ResidueRational.Zero;
                for (var i = 0; i < selected.Count; i++)
                {
                    var (factor, anchor) = selected[i];
                    if (factor.Semantics is not ("STEP" or "CUMULATIVE") || factor.DecimalPlaces is < 0 or > 18)
                        return Unknown("Unsupported processing factor semantics or precision", inputs);
                    if (node.Parents.Count > 1 && !factor.HomogeneousMixing) return Unknown("Homogeneous mixing is not approved", inputs);
                    if (!values.TryGetValue(anchor, out var parent) || parent.Lower is null || parent.Upper is null || parent.Reasons.Count != 0
                        || parent.ProfileCode != code || parent.Unit != factor.Unit || parent.Definition != factor.ResidueDefinition
                        || parentBounds[i]!.Definition != factor.ResidueDefinition)
                        return Unknown("Unresolved or incompatible parent profile dependency", inputs);
                    var weight = ResidueRational.Parse(node.Parents[i].QuantityKg);
                    var factorLow = ResidueRational.Parse(factor.Lower); var factorHigh = ResidueRational.Parse(factor.Upper);
                    if (factorLow.CompareTo(ResidueRational.Zero) < 0 || factorLow.CompareTo(factorHigh) > 0) return Unknown("Invalid factor interval", inputs);
                    low += weight * parent.Lower.Value * factorLow;
                    high += weight * parent.Upper.Value * factorHigh;
                    mass += weight;
                }
                return new(low / mass, high / mass, output.Unit, output.DecimalPlaces, output.ResidueDefinition, code, [], null, inputs, references);
            }
        }
        return results;
    }
    private static bool PositiveMass(string value)
    {
        try { return ResidueRational.Parse(value).CompareTo(ResidueRational.Zero) > 0; }
        catch (FormatException) { return false; }
    }
    private static Bound Unknown(string reason, IReadOnlyList<Guid> inputs) => new(null, null, null, null, "", null, [reason], null, inputs, []);
}
