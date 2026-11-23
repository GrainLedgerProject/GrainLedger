using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GrainLedger.Api.Data;
using GrainLedger.Api.Features.Catalogs;
using GrainLedger.Api.Features.Ledger;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace GrainLedger.Api.Features.Exports;

// A job capability, never an impersonated customer, authorizes the snapshot.
public sealed class ExportResiduePropagation(NpgsqlDataSource dataSource,
    ResiduePolicyReader policies, ResidueEvidenceReader evidence)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal async Task ProcessAsync(Guid job, Guid token, int generation, int batchSize, CancellationToken ct)
    {
        await CallAsync(job, token, generation, "DISCOVER", null, new { batchSize }, ct);
        var pending = await CallAsync(job, token, generation, "NEXT", null, new { batchSize }, ct);
        foreach (var entry in pending.EnumerateArray())
        {
            var target = entry.GetGuid();
            var prepared = await PrepareAsync(job, token, generation, target, ct);
            await AppendAsync(job, token, generation, target, prepared, ct);
        }
    }

    internal async Task<ResidueWorkSnapshot> PrepareAsync(Guid job, Guid token, int generation, Guid target, CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, tx))
            await readOnly.ExecuteNonQueryAsync(ct);
        var ctx = await InvokeAsync(connection, tx, job, token, generation, "READ", target, new { }, ct);
        var at = ctx.GetProperty("snapshotAt").GetDateTimeOffset();
        var owner = ctx.GetProperty("companyId").GetGuid();
        var market = ctx.GetProperty("marketId").GetGuid();
        var commodity = ctx.GetProperty("commodityId").GetGuid();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
        await db.Database.UseTransactionAsync(tx, ct);
        var policy = await new BlendAssessmentPolicyRead(db).ResolveAsync(owner, market, commodity, at, ct);
        var mrl = await new ApplicableMrlRead(db).ResolveAsync(market, commodity, at, ct);
        var substances = policy?.RequiredSubstanceIds ?? [];
        var nodes = ctx.GetProperty("graphs").EnumerateArray()
            .SelectMany(g => g.GetProperty("nodes").Deserialize<ResidueNode[]>(Json)!)
            .DistinctBy(n => n.LotId).ToArray();
        var rules = await policies.ReadAsync(connection, tx, owner, substances, at, ct);
        var facts = await evidence.ReadAsync(connection, tx, owner,
            nodes.Where(n => n.Kind == "SOURCE").Select(n => n.LotId).Distinct().ToArray(), substances, at, ct);
        await tx.CommitAsync(ct);

        var assessmentPolicy = policy is null ? null : new BlendAssessmentRule(policy.Id, policy.VersionNo,
            market, commodity, substances, decimal.Parse(policy.MrlMarginFraction, CultureInfo.InvariantCulture),
            policy.ResidueBasis, policy.EvidenceReference);
        var qualityRules = policy?.QualityRules.Select(r => new BlendQualityRule(policy.Id, policy.VersionNo,
            r.AttributeId, r.Unit, r.Calculation, policy.EvidenceReference)).ToArray() ?? [];
        var quality = ctx.GetProperty("quality").EnumerateArray().Select(q => new BlendQualityObservation(
            q.GetProperty("lot_id").GetGuid(), q.GetProperty("attribute_id").GetGuid(),
            decimal.Parse(ExportRules.BoundText(decimal.Parse(q.GetProperty("measured_value").GetString()!, CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture),
            q.GetProperty("unit").GetString()!, q.GetProperty("id").GetGuid(), q.GetProperty("version_no").GetInt32())).ToArray();
        var limits = ctx.GetProperty("limits").EnumerateArray().Select(l => new SpecificationLimit
        {
            Id = l.GetProperty("id").GetGuid(),
            AttributeId = l.GetProperty("attribute_id").GetGuid(),
            Unit = l.GetProperty("unit").GetString()!,
            MinValue = l.GetProperty("min_value").ValueKind == JsonValueKind.Null ? null : l.GetProperty("min_value").GetDecimal(),
            MaxValue = l.GetProperty("max_value").ValueKind == JsonValueKind.Null ? null : l.GetProperty("max_value").GetDecimal()
        }).ToArray();
        var yield = ctx.GetProperty("yield").ValueKind == JsonValueKind.Object
            ? ctx.GetProperty("yield").Deserialize<PlanningYieldRule>(Json) : null;
        if (yield is not null) yield = yield with { OutputFraction = ExportRules.BoundText(decimal.Parse(yield.OutputFraction, CultureInfo.InvariantCulture)) };
        var compatibility = ctx.GetProperty("compatibility");
        var codes = ctx.GetProperty("commodityCodes").EnumerateObject().ToDictionary(x => Guid.Parse(x.Name), x => x.Value.GetString()!);
        var orderCode = codes.GetValueOrDefault(commodity);
        var allFindings = new List<BlendAssessmentFinding>();
        var results = new List<ResidueResult>();
        var sectionResults = new List<object>();
        var evidenceRevision = (string?)null;
        object? evidenceSnapshot = null;
        // Distinct physical fractions in a container are never averaged together.
        var sections = ctx.GetProperty("sections").EnumerateArray().SelectMany(section =>
        {
            if (ctx.GetProperty("targetKind").GetString() != "CONTAINER" || section.GetProperty("mode").GetString() != "DIRECT_STOCK")
                return new[] { section };
            return section.GetProperty("allocations").EnumerateArray().GroupBy(a =>
            {
                var n = nodes.Single(n => n.LotId == a.GetProperty("lotId").GetGuid());
                return (n.CommodityId, n.Stage, n.ProductIdentity);
            }).Select(group => JsonSerializer.SerializeToElement(new
            {
                scope = section.GetProperty("scope").GetString(),
                mode = "DIRECT_STOCK",
                targetKg = ExportRules.Text(group.Sum(a => decimal.Parse(a.GetProperty("quantityKg").GetString()!, CultureInfo.InvariantCulture))),
                allocations = group.ToArray()
            }, Json)).ToArray();
        });
        foreach (var section in sections)
        {
            var mode = section.GetProperty("mode").GetString()!;
            var selected = section.GetProperty("allocations").EnumerateArray().Select(a => new CandidateGroupItem(
                a.GetProperty("lotId").GetGuid(), a.GetProperty("locationId").GetGuid(),
                decimal.Parse(a.GetProperty("quantityKg").GetString()!, CultureInfo.InvariantCulture), "")).ToArray();
            var targetKg = decimal.Parse(section.GetProperty("targetKg").GetString()!, CultureInfo.InvariantCulture);
            var inputEvidence = selected.Select(x => x.LotId).Distinct().Select(id => new ResidueEvaluationPreview(id,
                owner, at, ResidueCalculator.EngineVersion,
                ResidueEvaluationService.CalculateResults(id, substances, nodes, facts, rules, at))).ToArray();
            var blend = mode == "BLEND_INPUT_STOCK";
            IReadOnlyList<ResidueResult> bounds = [];
            var currentNodes = nodes;
            if (blend && compatibility.ValueKind == JsonValueKind.Object)
            {
                var output = compatibility.GetProperty("output_commodity_id").GetGuid();
                var catalogOutput = compatibility.GetProperty("outputCatalogCommodityId").GetGuid();
                var inputs = selected.GroupBy(x => x.LotId).Select(x => new HypotheticalBlendInput(x.Key, ExportRules.Text(x.Sum(a => a.QuantityKg)))).ToArray();
                var key = output.ToString("N") + ":" + string.Join(";", inputs.OrderBy(x => x.LotId).Select(x => $"{x.LotId:N}:{x.QuantityKg}"));
                var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));
                var node = new ResidueNode(id, owner, catalogOutput, "BLENDED_RICE", "BLENDED_RICE", at,
                    "BLENDING", null, null, inputs.Select(x => new ResidueParent(x.LotId, x.QuantityKg)).ToArray());
                currentNodes = [.. nodes, node];
                bounds = ResidueEvaluationService.CalculateResults(id, substances, currentNodes, facts, rules, at);
            }
            else if (!blend) bounds = DirectStockResidueAssessment.ConservativeBounds(inputEvidence, substances);
            var selectedQuality = quality.Where(q => selected.Any(a => a.LotId == q.LotId)).ToArray();
            var physicalCodes = currentNodes.Select(n => n.CommodityId).Distinct()
                .ToDictionary(id => id, id => codes.GetValueOrDefault(id)!);
            var candidates = selected.Select(a =>
            {
                var n = nodes.Single(n => n.LotId == a.LotId);
                return new PlanningCandidate(a.LotId, "", a.LocationId, "", ctx.GetProperty("lotCommodities").GetProperty(a.LotId.ToString()).GetGuid(), n.CommodityId,
                    n.Stage, n.ProductIdentity, a.QuantityKg, a.QuantityKg, "LOT", false, false, null, "", 1,
                    !n.Inconsistent, true, [], [], codes.GetValueOrDefault(n.CommodityId));
            }).ToArray();
            var snapshot = new PlanningCandidateSnapshot(Guid.Empty, ctx.GetProperty("facilityId").GetGuid(), 0, 0, "",
                commodity, targetKg, at, "", default, null, candidates, orderCode, physicalCodes);
            bool Match(ResidueResult b) => b.CommodityId is { } id &&
                ExportProductMatch.IsResidueForProduct(b, id, snapshot, b.ProductIdentity ?? "");
            var yieldRule = blend && yield is not null ? new BlendYieldRule(yield.Id, yield.VersionNo,
                decimal.Parse(yield.OutputFraction, CultureInfo.InvariantCulture), yield.EvidenceId.ToString()) : null;
            var assessment = BlendAssessment.Evaluate(mode, targetKg, selected, yieldRule, assessmentPolicy, mrl,
                bounds, limits, qualityRules, selectedQuality, Match);
            if (blend)
            {
                assessment = BlendInputResidue.Apply(assessment, BlendInputResidue.Screen(snapshot,
                    selected.Select(a => a.LotId).Distinct().ToArray(), inputEvidence, assessmentPolicy, mrl));
                if (compatibility.ValueKind != JsonValueKind.Object || candidates.Any(c =>
                    !compatibility.GetProperty("input_products").EnumerateArray().Any(p => p.GetString() == c.LedgerCommodityId + ":" + c.ProductIdentity)))
                    assessment = assessment with
                    {
                        Status = "INCOMPLETE",
                        Findings = [.. assessment.Findings,
                        new("BLEND_COMPATIBILITY_UNAVAILABLE", "INCOMPLETE", null, null, null, null)]
                    };
            }
            if (blend && compatibility.ValueKind == JsonValueKind.Object &&
                (!ExportProductMatch.TryMatchBlendOutput(compatibility, commodity, orderCode, codes, out _) ||
                 ctx.GetProperty("targetProduct").GetString() != "BLENDED_RICE"))
                assessment = assessment with
                {
                    Status = "INCOMPLETE",
                    Findings = [.. assessment.Findings,
                    new("BLEND_OUTPUT_COMMODITY_MISMATCH","INCOMPLETE",null,null,null,null)]
                };
            allFindings.AddRange(assessment.Findings);
            results.AddRange(bounds);
            sectionResults.Add(new { scope = section.GetProperty("scope").GetString(), assessment.Status, assessment.Findings });
            evidenceSnapshot = new { applicableMrl = mrl, assessmentPolicy = policy, outputYieldRule = yield, residueBounds = bounds, qualityObservations = selectedQuality };
            evidenceRevision = ExportRules.Hash("blend-assessment-dependencies", new { mrl, policy, yield, bounds, observations = selectedQuality });
        }
        if (sectionResults.Count == 0) allFindings.Add(new("EXPORT_CONTEXT_EMPTY", "INCOMPLETE", null, null, null, null));
        if (ctx.GetProperty("demandCurrent").ValueKind != JsonValueKind.True &&
            (ctx.GetProperty("targetKind").GetString() == "PLAN" || ctx.GetProperty("sections").EnumerateArray().Any(s => s.GetProperty("scope").GetString() == "REMAINING_INPUT")))
            allFindings.Add(new("DEMAND_CHANGED", "INCOMPLETE", null, null, null, null));
        var status = allFindings.Any(f => f.Status == "FAIL") ? "FAIL" :
            allFindings.Any(f => f.Status == "INCOMPLETE") ? "INCOMPLETE" : "PASS";
        var baselineUnchanged = ctx.GetProperty("targetKind").GetString() == "PLAN" &&
            evidenceRevision == ctx.GetProperty("savedDependencyRevision").GetString();
        var snapshotResult = JsonSerializer.SerializeToElement(new
        {
            engineVersion = "export-residue-reassessment-1",
            targetKind = ctx.GetProperty("targetKind").GetString(),
            targetId = ctx.GetProperty("targetId").GetGuid(),
            proposalId = ctx.GetProperty("proposalId").GetGuid(),
            marketId = market,
            commodityId = commodity,
            materialBasis = policy?.ResidueBasis,
            mrlVersionId = mrl.VersionId,
            marginPolicyId = policy?.Id,
            snapshotAt = at,
            assessmentStatus = status,
            historicalDecisionId = ctx.GetProperty("historicalDecisionVersion").ValueKind == JsonValueKind.Null ? (Guid?)null : ctx.GetProperty("proposalId").GetGuid(),
            historicalDecisionVersion = ctx.GetProperty("historicalDecisionVersion"),
            historicalDossierId = ctx.GetProperty("historicalDossierId"),
            freshness = status != "PASS" ? "STALE" : baselineUnchanged ? "CURRENT" : "REVIEW_REQUIRED",
            baselineUnchanged,
            findings = allFindings.Select(f => new
            {
                f.Code,
                f.Status,
                f.Actual,
                f.Limit,
                f.Unit,
                substanceId = f.Code.StartsWith("RESIDUE", StringComparison.Ordinal) && Guid.TryParse(f.Reference, out var substance) ? (Guid?)substance : null,
                attributeId = f.Code.StartsWith("QUALITY", StringComparison.Ordinal) && Guid.TryParse(f.Reference, out var attribute) ? (Guid?)attribute : null
            }),
            results,
            sections = sectionResults,
            dependencyRevision = evidenceRevision,
            evidence = evidenceSnapshot
        }, Json);
        return new(snapshotResult, ctx.GetProperty("revision").GetString()!);
    }

    internal Task<JsonElement> AppendAsync(Guid job, Guid token, int generation, Guid target, ResidueWorkSnapshot prepared, CancellationToken ct = default) =>
        CallAsync(job, token, generation, "APPEND", target, new { revision = prepared.Revision, snapshot = prepared.Snapshot }, ct);

    private async Task<JsonElement> CallAsync(Guid job, Guid token, int generation, string action, Guid? target, object payload, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var result = await InvokeAsync(connection, tx, job, token, generation, action, target, payload, ct);
        await tx.CommitAsync(ct);
        return result;
    }
    private static async Task<JsonElement> InvokeAsync(NpgsqlConnection connection, NpgsqlTransaction tx, Guid job, Guid token,
        int generation, string action, Guid? target, object payload, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT grainledger.exports_residue_job($1,$2,$3,$4,$5,$6)::text", connection, tx);
        command.Parameters.AddWithValue(job); command.Parameters.AddWithValue(token); command.Parameters.AddWithValue(generation);
        command.Parameters.AddWithValue(action);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)target ?? DBNull.Value });
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(payload, Json));
        using var doc = JsonDocument.Parse((string)(await command.ExecuteScalarAsync(ct))!);
        return doc.RootElement.Clone();
    }
}
