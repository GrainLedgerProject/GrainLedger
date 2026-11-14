using GrainLedger.Api.Features.Capture;
using GrainLedger.Api.Features.Catalogs;

namespace GrainLedger.Api.Features.Ledger;

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
                output.Add(new(node.LotId, substance, null, null, null, null, ["Conflicting approved source-group versions"],
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
            var activeSelections = ActiveSelections(selectionVersions, snapshotAt);
            if (activeSelections.Length != 1)
            { output.Add(Unknown(node.LotId, substance, activeSelections.Length == 0 ? "Missing reviewed report selection" : "Conflicting reviewed report selections", direct)); continue; }
            var selection = activeSelections[0];
            var remaining = direct.Where(x => !selection.SupersededResultIds.Contains(x.ResultId)).DistinctBy(x => x.ResultId).ToArray();
            if (remaining.Length != 1 || remaining[0].ResultId != selection.SelectedResultId)
            { output.Add(Unknown(node.LotId, substance, "Conflicting confirmed reports require reviewed supersession", direct)); continue; }
            var raw = remaining[0];
            var rawSelection = selection;
            var normalization = ResidueConcentration.Normalize(raw, selection);
            if (normalization.Reason is { } normalizationReason)
            { output.Add(Unknown(node.LotId, substance, normalizationReason, [raw])); continue; }
            var result = normalization.Evidence;
            selection = normalization.Selection;
            var numeric = result.ResultKind == "NUMERIC";
            var kind = result.ResultKind switch { "BELOW_LIMIT" => "BELOW_LIMIT", "NOT_DETECTED" => "ND", _ => "UNSUPPORTED" };
            if (selection.SchemaVersion != 2 || !selection.AsReceivedConfirmed || string.IsNullOrWhiteSpace(selection.ProfileCode)
                || selection.ProfileRuleId is null || selection.ProfileSettingId is null || selection.LaboratoryMethod != result.Method)
            { output.Add(Unknown(node.LotId, substance, "Missing reviewed as-received profile binding", [raw])); continue; }
            var resolved = SelectPolicy(policy, source.CompanyId, source.ReceivedAt, node.CommodityId, substance, selection.ProfileCode);
            var applicable = resolved.Rules;
            var rule = applicable.Count == 1 ? applicable[0].Rule : null;
            var laboratory = rule?.Laboratory.SingleOrDefault(x => x.AnalyticalMethod == result.Method);
            var supported = laboratory is not null && (numeric ? laboratory.NumericInterpretationKind is "NUMERIC_ABSOLUTE_UNCERTAINTY" or "NUMERIC_RELATIVE_UNCERTAINTY"
                : kind == "BELOW_LIMIT" ? laboratory.BelowLimit : kind == "ND" && laboratory.NdThresholdKind is not null);
            var componentKey = $"laboratory:{result.Method}:{result.ResultKind}";
            var provenance = new[] { new ResidueProvenance("DIRECT", node.LotId, result.ReportId, result.ResultId, SelectionId: selection.Id,
                RuleId: applicable.Count == 1 ? applicable[0].Rule.Id : null, SourceId: source.SourceId,
                AssociationId: result.AssociationId, ResultSetId: result.ResultSetId,
                OriginalUnit: raw.Unit, CanonicalUnit: result.Unit, ConcentrationScale: raw.Unit == "mg/kg" ? "1" : "0.001",
                OriginalNumericValue: raw.NumericValue, OriginalLod: raw.Lod, OriginalLoq: raw.Loq,
                OriginalAbsoluteUncertainty: rawSelection.AbsoluteUncertainty, OriginalReportingThreshold: rawSelection.ReportingThreshold) };
            var references = new List<ResidueRuleReference> { new(selection.Id, 1, "REPORT_SELECTION", "Company-scoped reviewed evidence") };
            var reviewedRule = policy.Rules.SingleOrDefault(x => x.Id == selection.ProfileRuleId && x.Code == selection.ProfileCode);
            var reviewedSetting = policy.Settings.SingleOrDefault(x => x.Id == selection.ProfileSettingId && x.RuleCode == selection.ProfileCode);
            if (reviewedRule is not null && reviewedSetting is not null)
                references.Add(Reference(reviewedRule, reviewedSetting, componentKey));
            references.AddRange(applicable.Select(x => Reference(x.Rule, x.Setting, componentKey)));
            if (resolved.Reason is not null || applicable.Count != 1 || rule is null || !supported
                || reviewedRule is null || reviewedSetting is null
                || rule.ResidueDefinition != selection.ResidueDefinition || rule.Unit != result.Unit)
            { output.Add(new(node.LotId, substance, null, null, result.Unit, null,
                [resolved.Reason ?? "Missing compatible reviewed profile, laboratory method or result form"],
                provenance, references, selection.ResidueDefinition, selection.ProfileCode)); continue; }
            if (numeric && selection.AbsoluteUncertainty is null && !string.IsNullOrWhiteSpace(result.Uncertainty))
            {
                output.Add(new(node.LotId, substance, null, null, rule.Unit, rule.DecimalPlaces,
                    ["Reported uncertainty requires explicit reviewed normalization"], provenance, references, selection.ResidueDefinition, selection.ProfileCode));
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
                        if (laboratory!.NumericInterpretationKind != "NUMERIC_RELATIVE_UNCERTAINTY" || laboratory.RelativeUncertaintyPercent is null
                            || value.CompareTo(zero) == 0 && !laboratory.RelativeUncertaintyAtZero) throw new FormatException();
                        percent = laboratory.RelativeUncertaintyPercent;
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
                    var threshold = kind == "BELOW_LIMIT" ? result.NumericValue : laboratory!.NdThresholdKind switch
                    { "REPORTING_THRESHOLD" => selection.ReportingThreshold, "LOD" => result.Lod, "LOQ" => result.Loq, _ => null };
                    if (threshold is null) throw new FormatException();
                    lower = zero; upper = ResidueRational.Parse(threshold);
                    if (upper.CompareTo(zero) <= 0) throw new FormatException();
                }
                // Preserve every terminating digit before downstream weighting; only final lot output rounds outward.
                output.Add(new(node.LotId, substance, lower.ExactDecimal(), upper.ExactDecimal(),
                    rule.Unit, rule.DecimalPlaces, [], provenance, references, selection.ResidueDefinition, selection.ProfileCode));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException)
            { output.Add(new(node.LotId, substance, null, null, rule.Unit, rule.DecimalPlaces,
                ["Missing or unsupported reviewed uncertainty/reporting threshold"], provenance, references, selection.ResidueDefinition, selection.ProfileCode)); }

            ResidueSourceBound GroupBound(ResidueSourceGroupVersion g)
            {
                var bindings = g.ProfileBindings?.Where(x => x.SourceId == source.SourceId).ToArray() ?? [];
                var binding = bindings.Length == 1 ? bindings[0] : null;
                var groupPolicy = SelectPolicy(policy, source.CompanyId, source.ReceivedAt, node.CommodityId, substance, g.ProfileCode);
                var groupReferences = new List<ResidueRuleReference> { new(g.Id, g.VersionNo, "SOURCE_GROUP", "Company-scoped reviewed evidence") };
                var groupReviewedRule = policy.Rules.SingleOrDefault(x => x.Id == binding?.ProfileRuleId && x.Code == g.ProfileCode);
                var groupReviewedSetting = policy.Settings.SingleOrDefault(x => x.Id == binding?.ProfileSettingId && x.RuleCode == g.ProfileCode);
                if (groupReviewedRule is not null && groupReviewedSetting is not null) groupReferences.Add(Reference(groupReviewedRule, groupReviewedSetting));
                groupReferences.AddRange(groupPolicy.Rules.Select(x => Reference(x.Rule, x.Setting)));
                if (g.SchemaVersion != 2 || !g.AsReceivedConfirmed || string.IsNullOrWhiteSpace(g.ProfileCode) || binding is null
                    || groupReviewedRule is null || groupReviewedSetting is null
                    || groupPolicy.Reason is not null || groupPolicy.Rules.Count != 1
                    || groupPolicy.Rules[0].Rule.ResidueDefinition != g.ResidueDefinition || groupPolicy.Rules[0].Rule.Unit != g.Unit)
                    return new(node.LotId, substance, null, null, g.Unit, g.DecimalPlaces,
                        [groupPolicy.Reason ?? "Missing compatible reviewed source-group profile binding"],
                        [new("SOURCE_GROUP", node.LotId, GroupVersionId: g.Id, SourceId: source.SourceId)], groupReferences, g.ResidueDefinition, g.ProfileCode);
                return new(node.LotId, substance, g.Lower, g.Upper, g.Unit, g.DecimalPlaces, [],
                    [new("SOURCE_GROUP", node.LotId, GroupVersionId: g.Id, SourceId: source.SourceId)],
                    groupReferences, g.ResidueDefinition, g.ProfileCode);
            }
        }
        return output;
    }

    public static IReadOnlyList<ResidueFactor> Factors(IReadOnlyList<ResidueProfileVersion> profiles) => profiles
        .SelectMany(x => x.Steps.Select(step => new ResidueFactor(x.SubstanceId, x.CommodityId, step.Process,
            step.InputStage, step.OutputStage, x.Unit, step.FactorLower, step.FactorUpper,
            x.DecimalPlaces, step.FactorSemantics == "STEP_CONCENTRATION" ? "STEP" : step.FactorSemantics == "CUMULATIVE_CONCENTRATION" ? "CUMULATIVE" : "UNSUPPORTED",
            step.Conditions, Reference(x) with { ComponentKey = step.Id.ToString() }, x.ResidueDefinition, step.OutputFraction, step.HomogeneousMixing,
            x.EffectiveFrom, x.EffectiveTo, x.SupersedesId, x.Code, step.Id, step.AnchorStage))).ToArray();

    private static bool Effective(DateTimeOffset from, DateTimeOffset? to, DateTimeOffset at) => at >= from && (to is null || at < to);
    internal static ResidueReportSelection[] ActiveSelections(IReadOnlyList<ResidueReportSelection> history, DateTimeOffset at)
    {
        var active = history.Where(x => Effective(x.EffectiveFrom, x.EffectiveTo, at)).ToArray();
        var removed = Superseded(active.Select(x => x.Id), history.ToDictionary(x => x.Id, x => x.SupersedesId));
        return active.Where(x => !removed.Contains(x.Id)).ToArray();
    }
    private static ResidueRuleReference Reference(ResidueProfileVersion rule) => new(rule.Id, rule.VersionNo, rule.Kind, rule.EvidenceRef, RuleCode: rule.Code);

    internal static ResidueRuleReference Reference(ResidueProfileVersion rule, ResiduePolicySettingVersion setting, string? componentKey = null) =>
        new(rule.Id, rule.VersionNo, rule.Kind, rule.EvidenceRef, setting.Mode, setting.Id, setting.VersionNo, rule.Code, componentKey);

    internal static (IReadOnlyList<(ResidueProfileVersion Rule, ResiduePolicySettingVersion Setting)> Rules, string? Reason)
        SelectPolicy(ResiduePolicySnapshot snapshot, Guid companyId, DateTimeOffset at, Guid commodityId, Guid substanceId, string? profileCode = null)
    {
        var history = snapshot.Rules.Where(x => (x.CompanyId is null || x.CompanyId == companyId)
            && x.SubstanceId == substanceId && x.CommodityId == commodityId && (profileCode is null || x.Code == profileCode)).ToArray();
        var active = history.Where(x => Effective(x.EffectiveFrom, x.EffectiveTo, at)).ToArray();
        var removed = Superseded(active.Select(x => x.Id), history.ToDictionary(x => x.Id, x => x.SupersedesId));
        active = active.Where(x => !removed.Contains(x.Id)).ToArray();
        var settings = snapshot.Settings.Where(x => x.CompanyId == companyId).ToArray();
        var effectiveSettings = settings.Where(x => Effective(x.EffectiveFrom, x.EffectiveTo, at)).ToArray();
        var removedSettings = Superseded(effectiveSettings.Select(x => x.Id), settings.ToDictionary(x => x.Id, x => x.SupersedesId));
        effectiveSettings = effectiveSettings.Where(x => !removedSettings.Contains(x.Id)).ToArray();
        var selected = new List<(ResidueProfileVersion Rule, ResiduePolicySettingVersion Setting)>();
        string? reason = null;
        foreach (var adoption in effectiveSettings.GroupBy(x => x.RuleCode, StringComparer.Ordinal))
        {
            // Returned inheritance ignores company-only applicability; intended overrides still block matching defaults.
            var includesCompanyScope = adoption.Any(x => x.Mode != "SYSTEM_DEFAULT");
            var applicable = active.Where(x => x.Code == adoption.Key && (x.CompanyId is null || includesCompanyScope)).ToArray();
            if (applicable.Length == 0) continue;
            var versions = adoption.ToArray();
            if (versions.Length != 1)
            { reason = "Conflicting company policy settings"; continue; }
            var setting = versions[0];
            if (setting.Mode is not ("SYSTEM_DEFAULT" or "COMPANY_OVERRIDE"))
            { reason = "Unsupported company policy setting"; continue; }
            var scope = setting.Mode == "COMPANY_OVERRIDE" ? (Guid?)companyId : null;
            var candidates = applicable.Where(x => x.CompanyId == scope).ToArray();
            if (history.Any(x => x.Code == adoption.Key && x.CompanyId == scope && x.Retirement is not null))
            { reason = "Adopted residue profile series is retired"; continue; }
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
        new(lot, substance, null, null, null, null, [reason],
            results?.Select(x => new ResidueProvenance("DIRECT", lot, x.ReportId, x.ResultId,
                SourceId: x.SourceId, AssociationId: x.AssociationId, ResultSetId: x.ResultSetId)).ToArray() ?? [], []);
}
