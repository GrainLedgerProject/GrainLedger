using GrainLedger.Modules.Capture;
using GrainLedger.Modules.Catalogs;

namespace GrainLedger.Modules.Ledger;

public static class ResidueEvidenceResolver
{
    public static IReadOnlyList<ResidueSourceBound> Resolve(IReadOnlyList<ResidueNode> nodes,
        IReadOnlyList<Guid> substances, ResidueEvidenceSnapshot evidence, IReadOnlyList<ResidueRuleVersion> policies,
        DateTimeOffset snapshotAt)
    {
        var mappings = evidence.Sources.ToDictionary(x => x.LotId);
        var results = evidence.Results.GroupBy(x => (x.LotId, x.SubstanceId)).ToDictionary(x => x.Key, x => x.ToArray());
        var selections = evidence.Selections.GroupBy(x => (x.SourceId, x.SubstanceId)).ToDictionary(x => x.Key, x => x.ToArray());
        var groupCodes = evidence.Groups.SelectMany(g => g.SourceIds.Select(id => (Id: id, g.SubstanceId, g.Code)))
            .GroupBy(x => (x.Id, x.SubstanceId)).ToDictionary(x => x.Key, x => x.Select(y => y.Code).Distinct().ToArray());
        var groups = evidence.Groups.GroupBy(x => (x.CompanyId, x.SubstanceId, x.Code)).ToDictionary(x => x.Key, x => x.ToArray());
        var interpretations = policies.Where(x => x.Kind == "INTERPRETATION").GroupBy(x => x.SubstanceId)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var output = new List<ResidueSourceBound>();
        foreach (var node in nodes.Where(x => x.Kind == "SOURCE"))
        foreach (var substance in substances)
        {
            if (!mappings.TryGetValue(node.LotId, out var source) || source.CompanyId != node.CompanyId
                || source.ProductStage != node.Stage || source.ProductIdentity != node.ProductIdentity)
            { output.Add(Unknown(node.LotId, substance, "Missing or inconsistent procurement source mapping")); continue; }
            var direct = results.GetValueOrDefault((node.LotId, substance)) ?? [];
            var candidates = (groupCodes.GetValueOrDefault((source.SourceId, substance)) ?? [])
                .SelectMany(code => groups[(source.CompanyId, substance, code)]).ToArray();
            var applicableGroups = candidates.Where(g => Effective(g.EffectiveFrom, g.EffectiveTo, source.ReceivedAt)).ToArray();
            var removedGroups = Superseded(applicableGroups.Select(x => x.Id), candidates.ToDictionary(x => x.Id, x => x.SupersedesId));
            applicableGroups = applicableGroups.Where(x => !removedGroups.Contains(x.Id) && x.SourceIds.Contains(source.SourceId)
                && x.ProductStage == source.ProductStage && x.ProductIdentity == source.ProductIdentity
                && (direct.Length == 0 || x.Precedence == "GROUP_FIRST")).ToArray();
            if (applicableGroups.Length > 1)
            {
                output.Add(new(node.LotId, substance, null, null, null, null, null, ["Conflicting approved source-group versions"],
                    applicableGroups.Select(g => new ResidueProvenance("SOURCE_GROUP", node.LotId, GroupVersionId: g.Id, SourceId: source.SourceId)).ToArray(),
                    applicableGroups.Select(g => new ResidueRuleReference(g.Id, g.VersionNo, "SOURCE_GROUP", "Company-scoped reviewed evidence")).ToArray()));
                continue;
            }
            var group = applicableGroups.SingleOrDefault();
            if (group is not null && (group.Precedence == "GROUP_FIRST" || direct.Length == 0))
            { output.Add(GroupBound(group)); continue; }
            if (direct.Length == 0)
            { output.Add(Unknown(node.LotId, substance, "No confirmed substance evidence or approved fallback")); continue; }
            var selectionVersions = selections.GetValueOrDefault((source.SourceId, substance)) ?? [];
            var activeSelections = selectionVersions.Where(x => Effective(x.EffectiveFrom, x.EffectiveTo, snapshotAt)).ToArray();
            var removedSelections = Superseded(activeSelections.Select(x => x.Id), selectionVersions.ToDictionary(x => x.Id, x => x.SupersedesId));
            activeSelections = activeSelections.Where(x => !removedSelections.Contains(x.Id)).ToArray();
            if (activeSelections.Length != 1)
            { output.Add(Unknown(node.LotId, substance, activeSelections.Length == 0 ? "Missing reviewed material basis and report selection" : "Conflicting reviewed report selections", direct)); continue; }
            var selection = activeSelections[0];
            var remaining = direct.Where(x => !selection.SupersededResultIds.Contains(x.ResultId)).DistinctBy(x => x.ResultId).ToArray();
            if (remaining.Length != 1 || remaining[0].ResultId != selection.SelectedResultId)
            { output.Add(Unknown(node.LotId, substance, "Conflicting confirmed reports require reviewed supersession", direct)); continue; }
            var result = remaining[0];
            var kind = result.ResultKind switch { "NUMERIC" => "NUMERIC_ABSOLUTE_UNCERTAINTY", "BELOW_LIMIT" => "BELOW_LIMIT", "NOT_DETECTED" => "ND", _ => "UNSUPPORTED" };
            var interpretationVersions = interpretations.GetValueOrDefault(substance) ?? [];
            var activeInterpretations = interpretationVersions.Where(x => Effective(x.EffectiveFrom, x.EffectiveTo, source.ReceivedAt)).ToArray();
            var superseded = Superseded(activeInterpretations.Select(x => x.Id), interpretationVersions.ToDictionary(x => x.Id, x => x.SupersedesId));
            var applicable = activeInterpretations.Where(x => !superseded.Contains(x.Id)
                    && x.InterpretationKind == kind && x.InputStage == source.ProductStage
                    && x.OutputStage == source.ProductStage && x.InputBasis == selection.MaterialBasis
                    && x.OutputBasis == selection.MaterialBasis && x.ResidueDefinition == selection.ResidueDefinition
                    && x.Unit == result.Unit && x.AnalyticalMethod == result.Method
                    && x.CommodityId == node.CommodityId).ToArray();
            var provenance = new[] { new ResidueProvenance("DIRECT", node.LotId, result.ReportId, result.ResultId, SelectionId: selection.Id,
                RuleId: applicable.Length == 1 ? applicable[0].Id : null, SourceId: source.SourceId,
                AssociationId: result.AssociationId, ResultSetId: result.ResultSetId) };
            var references = new List<ResidueRuleReference> { new(selection.Id, 1, "REPORT_SELECTION", "Company-scoped reviewed evidence") };
            if (applicable.Length != 1)
            { output.Add(new(node.LotId, substance, null, null, result.Unit, selection.MaterialBasis, null,
                [applicable.Length == 0 ? "Missing compatible reviewed interpretation, method, unit or material basis" : "Conflicting interpretation policies"], provenance, references, selection.ResidueDefinition)); continue; }
            var rule = applicable[0]; references.Add(Reference(rule));
            try
            {
                var zero = ResidueRational.Zero; ResidueRational lower; ResidueRational upper;
                if (kind == "NUMERIC_ABSOLUTE_UNCERTAINTY")
                {
                    if (result.NumericValue is null || selection.AbsoluteUncertainty is null) throw new FormatException();
                    var value = ResidueRational.Parse(result.NumericValue); var uncertainty = ResidueRational.Parse(selection.AbsoluteUncertainty);
                    if (value.CompareTo(zero) < 0 || uncertainty.CompareTo(zero) < 0) throw new FormatException();
                    lower = value - uncertainty; if (lower.CompareTo(zero) < 0) lower = zero;
                    upper = value + uncertainty;
                }
                else
                {
                    var threshold = kind == "BELOW_LIMIT" ? result.NumericValue : rule.NdThresholdKind switch
                    { "REPORTING_THRESHOLD" => selection.ReportingThreshold, "LOD" => result.Lod, "LOQ" => result.Loq, _ => null };
                    if (threshold is null) throw new FormatException();
                    lower = zero; upper = ResidueRational.Parse(threshold);
                    if (upper.CompareTo(zero) <= 0) throw new FormatException();
                }
                // Exact decimal evidence +/- uncertainty has a terminating expansion; retain all input digits.
                var exactPlaces = new[] { result.NumericValue, selection.AbsoluteUncertainty, selection.ReportingThreshold, result.Lod, result.Loq }
                    .Where(x => x is not null).Select(x => x!.Contains('.') ? x.Length - x.IndexOf('.') - 1 : 0).DefaultIfEmpty(0).Max();
                if (exactPlaces > 18) throw new FormatException();
                output.Add(new(node.LotId, substance, lower.Round(exactPlaces, false), upper.Round(exactPlaces, true),
                    rule.Unit, selection.MaterialBasis, rule.DecimalPlaces, [], provenance, references, selection.ResidueDefinition));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException)
            { output.Add(new(node.LotId, substance, null, null, rule.Unit, selection.MaterialBasis, rule.DecimalPlaces,
                ["Missing or unsupported reviewed uncertainty/reporting threshold"], provenance, references, selection.ResidueDefinition)); }

            ResidueSourceBound GroupBound(ResidueSourceGroupVersion g) => new(node.LotId, substance, g.Lower, g.Upper,
                g.Unit, g.MaterialBasis, g.DecimalPlaces, [],
                [new("SOURCE_GROUP", node.LotId, GroupVersionId: g.Id, SourceId: source.SourceId)],
                [new(g.Id, g.VersionNo, "SOURCE_GROUP", "Company-scoped reviewed evidence")], g.ResidueDefinition);
        }
        return output;
    }

    public static IReadOnlyList<ResidueFactor> Factors(IReadOnlyList<ResidueRuleVersion> policies) => policies
        .Where(x => x.Kind == "PROCESS").Select(x => new ResidueFactor(x.SubstanceId, x.CommodityId, x.Process,
            x.InputStage, x.OutputStage, x.InputBasis, x.OutputBasis, x.Unit, x.FactorLower!, x.FactorUpper!,
            x.DecimalPlaces, x.FactorSemantics == "STEP_CONCENTRATION" ? "STEP" : x.FactorSemantics == "CUMULATIVE_CONCENTRATION" ? "CUMULATIVE" : "UNSUPPORTED",
            x.Conditions, Reference(x), x.ResidueDefinition, x.OutputFraction, x.HomogeneousMixing,
            x.EffectiveFrom, x.EffectiveTo, x.SupersedesId)).ToArray();

    private static bool Effective(DateTimeOffset from, DateTimeOffset? to, DateTimeOffset at) => at >= from && (to is null || at < to);
    private static ResidueRuleReference Reference(ResidueRuleVersion rule) => new(rule.Id, rule.VersionNo, rule.Kind, rule.EvidenceRef);
    private static HashSet<Guid> Superseded(IEnumerable<Guid> applicable, IReadOnlyDictionary<Guid, Guid?> predecessors)
    {
        var removed = new HashSet<Guid>();
        foreach (var id in applicable)
        {
            var current = id;
            while (predecessors.TryGetValue(current, out var previous) && previous is { } prior && removed.Add(prior))
                current = prior;
        }
        return removed;
    }
    private static ResidueSourceBound Unknown(Guid lot, Guid substance, string reason, IReadOnlyList<ResidueLabEvidence>? results = null) =>
        new(lot, substance, null, null, null, null, null, [reason],
            results?.Select(x => new ResidueProvenance("DIRECT", lot, x.ReportId, x.ResultId,
                SourceId: x.SourceId, AssociationId: x.AssociationId, ResultSetId: x.ResultSetId)).ToArray() ?? [], []);
}
