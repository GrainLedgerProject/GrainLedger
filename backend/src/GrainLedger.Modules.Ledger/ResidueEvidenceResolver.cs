using GrainLedger.Modules.Capture;
using GrainLedger.Modules.Catalogs;

namespace GrainLedger.Modules.Ledger;

public static class ResidueEvidenceResolver
{
    public static IReadOnlyList<ResidueSourceBound> Resolve(IReadOnlyList<ResidueNode> nodes,
        IReadOnlyList<Guid> substances, ResidueEvidenceSnapshot evidence, ResiduePolicySnapshot policy,
        DateTimeOffset snapshotAt)
    {
        var mappings = evidence.Sources.ToDictionary(x => x.LotId);
        var results = evidence.Results.GroupBy(x => (x.LotId, x.SubstanceId)).ToDictionary(x => x.Key, x => x.ToArray());
        var selections = evidence.Selections.GroupBy(x => (x.SourceId, x.SubstanceId)).ToDictionary(x => x.Key, x => x.ToArray());
        var groupCodes = evidence.Groups.SelectMany(g => g.SourceIds.Select(id => (Id: id, g.SubstanceId, g.Code)))
            .GroupBy(x => (x.Id, x.SubstanceId)).ToDictionary(x => x.Key, x => x.Select(y => y.Code).Distinct().ToArray());
        var groups = evidence.Groups.GroupBy(x => (x.CompanyId, x.SubstanceId, x.Code)).ToDictionary(x => x.Key, x => x.ToArray());
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
            var numeric = result.ResultKind == "NUMERIC";
            var kind = result.ResultKind switch { "BELOW_LIMIT" => "BELOW_LIMIT", "NOT_DETECTED" => "ND", _ => "UNSUPPORTED" };
            var resolved = SelectPolicy(policy, source.CompanyId, source.ReceivedAt, x =>
                x.Kind == "INTERPRETATION" && x.SubstanceId == substance
                && (numeric ? x.InterpretationKind is "NUMERIC_ABSOLUTE_UNCERTAINTY" or "NUMERIC_RELATIVE_UNCERTAINTY" : x.InterpretationKind == kind)
                && x.InputStage == source.ProductStage && x.OutputStage == source.ProductStage
                && x.InputBasis == selection.MaterialBasis && x.OutputBasis == selection.MaterialBasis
                && x.ResidueDefinition == selection.ResidueDefinition && x.Unit == result.Unit
                && x.AnalyticalMethod == result.Method && x.CommodityId == node.CommodityId);
            var applicable = resolved.Rules;
            var provenance = new[] { new ResidueProvenance("DIRECT", node.LotId, result.ReportId, result.ResultId, SelectionId: selection.Id,
                RuleId: applicable.Count == 1 ? applicable[0].Rule.Id : null, SourceId: source.SourceId,
                AssociationId: result.AssociationId, ResultSetId: result.ResultSetId) };
            var references = new List<ResidueRuleReference> { new(selection.Id, 1, "REPORT_SELECTION", "Company-scoped reviewed evidence") };
            references.AddRange(applicable.Select(x => Reference(x.Rule, x.Setting)));
            if (resolved.Reason is not null || applicable.Count != 1)
            { output.Add(new(node.LotId, substance, null, null, result.Unit, selection.MaterialBasis, null,
                [resolved.Reason ?? (applicable.Count == 0 ? "Missing compatible reviewed interpretation, method, unit or material basis" : "Conflicting interpretation policies")],
                provenance, references, selection.ResidueDefinition)); continue; }
            var rule = applicable[0].Rule;
            if (numeric && selection.AbsoluteUncertainty is null && !string.IsNullOrWhiteSpace(result.Uncertainty))
            {
                output.Add(new(node.LotId, substance, null, null, rule.Unit, selection.MaterialBasis, rule.DecimalPlaces,
                    ["Reported uncertainty requires explicit reviewed normalization"], provenance, references, selection.ResidueDefinition));
                continue;
            }
            try
            {
                var zero = ResidueRational.Zero; ResidueRational lower; ResidueRational upper;
                if (numeric)
                {
                    if (result.NumericValue is null) throw new FormatException();
                    var value = ResidueRational.Parse(result.NumericValue);
                    if (value.CompareTo(zero) < 0) throw new FormatException();
                    ResidueRational uncertainty; string mode; string? percent = null;
                    if (selection.AbsoluteUncertainty is { } absolute)
                    {
                        // Explicit reviewed normalization owns report uncertainty, never the smaller profile assumption.
                        if (string.IsNullOrWhiteSpace(result.Uncertainty)) throw new FormatException();
                        uncertainty = ResidueRational.Parse(absolute); mode = "NUMERIC_ABSOLUTE_UNCERTAINTY";
                    }
                    else
                    {
                        if (rule.InterpretationKind != "NUMERIC_RELATIVE_UNCERTAINTY" || rule.RelativeUncertaintyPercent is null
                            || value.CompareTo(zero) == 0 && !rule.RelativeUncertaintyAtZero) throw new FormatException();
                        percent = rule.RelativeUncertaintyPercent;
                        var relative = ResidueRational.Parse(percent);
                        if (relative.CompareTo(zero) < 0) throw new FormatException();
                        uncertainty = value * relative / ResidueRational.Parse("100");
                        mode = "NUMERIC_RELATIVE_UNCERTAINTY";
                    }
                    if (uncertainty.CompareTo(zero) < 0) throw new FormatException();
                    lower = value - uncertainty; if (lower.CompareTo(zero) < 0) lower = zero;
                    upper = value + uncertainty;
                    provenance[0] = provenance[0] with { UncertaintyMode = mode,
                        RelativeUncertaintyPercent = percent, AbsoluteUncertainty = uncertainty.ExactDecimal() };
                }
                else
                {
                    var threshold = kind == "BELOW_LIMIT" ? result.NumericValue : rule.NdThresholdKind switch
                    { "REPORTING_THRESHOLD" => selection.ReportingThreshold, "LOD" => result.Lod, "LOQ" => result.Loq, _ => null };
                    if (threshold is null) throw new FormatException();
                    lower = zero; upper = ResidueRational.Parse(threshold);
                    if (upper.CompareTo(zero) <= 0) throw new FormatException();
                }
                // Preserve every terminating digit before downstream weighting; only final lot output rounds outward.
                output.Add(new(node.LotId, substance, lower.ExactDecimal(), upper.ExactDecimal(),
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

    internal static ResidueRuleReference Reference(ResidueRuleVersion rule, ResiduePolicySettingVersion setting) =>
        new(rule.Id, rule.VersionNo, rule.Kind, rule.EvidenceRef, setting.Mode, setting.Id, setting.VersionNo, rule.Code);

    internal static (IReadOnlyList<(ResidueRuleVersion Rule, ResiduePolicySettingVersion Setting)> Rules, string? Reason)
        SelectPolicy(ResiduePolicySnapshot snapshot, Guid companyId, DateTimeOffset at, Func<ResidueRuleVersion, bool> matches)
    {
        var history = snapshot.Rules.Where(x => x.CompanyId is null || x.CompanyId == companyId).ToArray();
        var active = history.Where(x => Effective(x.EffectiveFrom, x.EffectiveTo, at)).ToArray();
        var removed = Superseded(active.Select(x => x.Id), history.ToDictionary(x => x.Id, x => x.SupersedesId));
        active = active.Where(x => !removed.Contains(x.Id)).ToArray();
        var settings = snapshot.Settings.Where(x => x.CompanyId == companyId).ToArray();
        var effectiveSettings = settings.Where(x => Effective(x.EffectiveFrom, x.EffectiveTo, at)).ToArray();
        var removedSettings = Superseded(effectiveSettings.Select(x => x.Id), settings.ToDictionary(x => x.Id, x => x.SupersedesId));
        effectiveSettings = effectiveSettings.Where(x => !removedSettings.Contains(x.Id)).ToArray();
        var selected = new List<(ResidueRuleVersion Rule, ResiduePolicySettingVersion Setting)>();
        string? reason = null;
        foreach (var adoption in effectiveSettings.GroupBy(x => x.RuleCode, StringComparer.Ordinal))
        {
            // Returned inheritance ignores company-only applicability; intended overrides still block matching defaults.
            var includesCompanyScope = adoption.Any(x => x.Mode != "SYSTEM_DEFAULT");
            var applicable = active.Where(x => x.Code == adoption.Key && (x.CompanyId is null || includesCompanyScope)
                && matches(x)).ToArray();
            if (applicable.Length == 0) continue;
            var versions = adoption.ToArray();
            if (versions.Length != 1)
            { reason = "Conflicting company policy settings"; continue; }
            var setting = versions[0];
            if (setting.Mode is not ("SYSTEM_DEFAULT" or "COMPANY_OVERRIDE"))
            { reason = "Unsupported company policy setting"; continue; }
            var scope = setting.Mode == "COMPANY_OVERRIDE" ? (Guid?)companyId : null;
            var candidates = applicable.Where(x => x.CompanyId == scope).ToArray();
            if (candidates.Length == 0)
            { reason = setting.Mode == "COMPANY_OVERRIDE" ? "Intended company override is missing or inapplicable" : "Adopted system profile is missing or inapplicable"; continue; }
            selected.AddRange(candidates.Select(x => (x, setting)));
            if (candidates.Length != 1) reason = "Conflicting rules at selected policy scope";
        }
        return (selected, reason);
    }
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
