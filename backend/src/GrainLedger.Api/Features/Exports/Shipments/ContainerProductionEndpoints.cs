using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GrainLedger.Api.Features.Catalogs;
using GrainLedger.Api.Features.Identity;
using GrainLedger.Api.Features.Ledger;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace GrainLedger.Api.Features.Exports;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProductionRequest(Guid OperationId, int Version, Guid ProposalId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProductionDraftRequest(Guid OperationId, int Version, JsonElement Draft);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProductionCommitRequest(Guid OperationId, int Version);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProductionReviewRequest(Guid OperationId, int Version, string Reason);

public static partial class ExportModule
{
    private static void MapContainerProduction(IEndpointRouteBuilder endpoints)
    {
        var routes = endpoints.MapGroup("/api/export-containers/{id:guid}/production").RequireAuthorization()
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>().WithTags("Container production");
        routes.MapGet("", async (Guid id, LedgerService ledger, AppDbContext db, CancellationToken ct) =>
            await ProductionReadAsync(await ledger.ActorAsync(ct), id, "detail", db, ct));
        endpoints.MapGet("/api/container-production", async (LedgerService ledger, AppDbContext db, CancellationToken ct) =>
            await ProductionReadAsync(await ledger.ActorAsync(ct), Guid.Empty, "list", db, ct))
            .RequireAuthorization(Permissions.ContainersOperate);
        // The list is also available to the operator, whose business permission is conversions.write.
        routes.MapPost("/request", (Guid id, ProductionRequest request, HttpContext http, ICurrentAccess access, AppDbContext db,
            LedgerService ledger, ResidueEvaluationService residue, ApplicableMrlRead mrl, BlendAssessmentPolicyRead policies, CancellationToken ct) =>
            ProductionMutationAsync(id, "request", request, http, access, db, ledger, residue, mrl, policies, ct)).RequireAuthorization(Permissions.ContainersWrite);
        routes.MapPost("/draft", (Guid id, ProductionDraftRequest request, HttpContext http, ICurrentAccess access, AppDbContext db,
            LedgerService ledger, ResidueEvaluationService residue, ApplicableMrlRead mrl, BlendAssessmentPolicyRead policies, CancellationToken ct) =>
            ProductionMutationAsync(id, "draft", request, http, access, db, ledger, residue, mrl, policies, ct)).RequireAuthorization(Permissions.ConversionsWrite);
        routes.MapPost("/commit", (Guid id, ProductionCommitRequest request, HttpContext http, ICurrentAccess access, AppDbContext db,
            LedgerService ledger, ResidueEvaluationService residue, ApplicableMrlRead mrl, BlendAssessmentPolicyRead policies, CancellationToken ct) =>
            ProductionMutationAsync(id, "commit", request, http, access, db, ledger, residue, mrl, policies, ct)).RequireAuthorization(Permissions.ConversionsWrite);
        routes.MapPost("/next", (Guid id, ProductionCommitRequest request, HttpContext http, ICurrentAccess access, AppDbContext db,
            LedgerService ledger, ResidueEvaluationService residue, ApplicableMrlRead mrl, BlendAssessmentPolicyRead policies, CancellationToken ct) =>
            ProductionMutationAsync(id, "next", request, http, access, db, ledger, residue, mrl, policies, ct)).RequireAuthorization(Permissions.ConversionsWrite);
        foreach (var decision in new[] { "approve", "reject" })
        {
            var action = decision;
            routes.MapPost("/" + action, (Guid id, ProductionReviewRequest request, HttpContext http, ICurrentAccess access, AppDbContext db,
                LedgerService ledger, ResidueEvaluationService residue, ApplicableMrlRead mrl, BlendAssessmentPolicyRead policies, CancellationToken ct) =>
                ProductionMutationAsync(id, action, request, http, access, db, ledger, residue, mrl, policies, ct)).RequireAuthorization(Permissions.BlendApprove);
        }
    }

    private static async Task<IResult> ProductionReadAsync(Guid actor, Guid id, string action, AppDbContext db, CancellationToken ct)
    {
        try { return Results.Ok(await ProductionSqlAsync(actor, id, action, JsonSerializer.SerializeToElement(new { }), null, db, ct)); }
        catch (PostgresException ex) { return ContainerDatabaseError(ex); }
    }

    private static Task<JsonElement> ProductionSqlAsync(Guid actor, Guid id, string action, JsonElement payload, JsonElement? basis, AppDbContext db, CancellationToken ct) =>
        ContainerSqlAsync("SELECT grainledger.ledger_container_production($1,$2,$3,$4,$5)::text", db, ct,
            new() { Value = actor }, new() { Value = id }, new() { Value = action }, new() { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = payload.GetRawText() },
            new() { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = basis?.GetRawText() ?? (object)DBNull.Value });

    private static async Task<IResult> ProductionMutationAsync<T>(Guid id, string action, T request, HttpContext http, ICurrentAccess access,
        AppDbContext db, LedgerService ledger, ResidueEvaluationService residue, ApplicableMrlRead mrl, BlendAssessmentPolicyRead policies, CancellationToken ct)
    {
        var permission = action == "request" ? Permissions.ContainersWrite : action is "draft" or "commit" or "next" ? Permissions.ConversionsWrite : Permissions.BlendApprove;
        var role = action == "request" ? Roles.ExportStaff : action is "draft" or "commit" or "next" ? Roles.MillOperator : Roles.CustomerManager;
        var payload = JsonSerializer.SerializeToElement(request, ContainerJson);
        if (payload.GetProperty("operationId").GetGuid() == Guid.Empty || payload.GetProperty("version").GetInt32() < 0)
            return Problem(400, "PRODUCTION_REQUEST_INVALID", "Stable operation ID and current version required.");
        if (request is ProductionDraftRequest draft && (draft.Draft.ValueKind != JsonValueKind.Object || draft.Draft.EnumerateObject().Any(p =>
            !new[] { "id", "ownerId", "facilityId", "kind", "occurredAt", "equipmentId", "inputs", "outputs", "declaredLossKg", "version" }.Contains(p.Name))))
            return Problem(400, "PRODUCTION_DRAFT_INVALID", "Only processing draft fields are accepted.");
        if (request is ProductionReviewRequest review && (string.IsNullOrWhiteSpace(review.Reason) || review.Reason.Length > 1000))
            return Problem(400, "PRODUCTION_REVIEW_INVALID", "A reason of one to 1000 characters is required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
            if (!await access.HasPermissionAsync(permission, ct)) return Results.Forbid();
            var actor = await ledger.ActorAsync(ct);
            if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == actor && u.IsActive && u.Role == role, ct)) return Results.Forbid();
            // Scope the read first, then follow the existing stock -> company -> row lock order.
            await ProductionSqlAsync(actor, id, "detail", JsonSerializer.SerializeToElement(new { }), null, db, ct);
            var containerCompany = await db.Containers.AsNoTracking().Where(x => x.Id == id).Select(x => x.CompanyId).SingleAsync(ct);
            await LockAsync(db, containerCompany, ct);
            var replay = await ProductionSqlAsync(actor, id, "replay", payload, JsonSerializer.SerializeToElement(new { action }), db, ct);
            if (!replay.TryGetProperty("replayMissing", out _)) { await tx.CommitAsync(ct); return Results.Ok(replay); }
            JsonElement? basis = null;
            if (action is "request" or "commit" or "approve")
            {
                basis = await AssessProductionAsync(actor, id, action == "approve", db, ledger, residue, mrl, policies, ct);
                if (basis.Value.GetProperty("assessmentStatus").GetString() != "PASS")
                {
                    if ((action is "request" or "commit") && InvalidatesUnfinishedAuthority(basis.Value))
                    {
                        var version = await db.Containers.AsNoTracking().Where(x => x.Id == id).Select(x => x.Version).SingleAsync(ct);
                        await InvalidateUnfinishedAuthorityAsync(actor, id, version, basis.Value, db, ct);
                        await tx.CommitAsync(ct);
                    }
                    return Results.Json(new { code = "PRODUCTION_BLOCKED", title = "Current production checks do not pass.", readiness = new { assessmentStatus = basis.Value.GetProperty("assessmentStatus"), findings = basis.Value.GetProperty("findings") } }, statusCode: 409);
                }
            }
            var result = await ProductionSqlAsync(actor, id, action, payload, basis, db, ct);
            if (basis is not null)
            {
                // Effective-time boundaries can pass without a publisher write. Re-read every scientific
                // dependency after the final posting/review, retaining the original reviewed basis.
                var finalBasis = await AssessProductionAsync(actor, id, action == "approve", db, ledger, residue, mrl, policies, ct);
                if (finalBasis.GetProperty("assessmentStatus").GetString() != "PASS" ||
                    finalBasis.GetProperty("dependencyRevision").GetString() != basis.Value.GetProperty("dependencyRevision").GetString())
                    return Problem(409, "PRODUCTION_EVIDENCE_CHANGED", "Evidence changed during production/review; no changes were committed.");
                await ProductionSqlAsync(actor, id, "context", JsonSerializer.SerializeToElement(new { }), finalBasis, db, ct);
            }
            await tx.CommitAsync(ct);
            return Results.Ok(result);
        }
        catch (PostgresException ex) { return ContainerDatabaseError(ex); }
        catch (LedgerException ex) { return Problem(ex.StatusCode, "PRODUCTION_BLOCKED", ex.Message); }
    }

    private static async Task<JsonElement> AssessProductionAsync(Guid actor, Guid id, bool output, AppDbContext db, LedgerService ledger,
        ResidueEvaluationService residue, ApplicableMrlRead mrlReader, BlendAssessmentPolicyRead policyReader, CancellationToken ct)
    {
        var context = await ProductionSqlAsync(actor, id, "context", JsonSerializer.SerializeToElement(new { }), null, db, ct);
        var proposal = await db.BlendPlanProposals.AsNoTracking().SingleAsync(x => x.Id == context.GetProperty("proposalId").GetGuid(), ct);
        var plan = await db.BlendPlanDrafts.AsNoTracking().SingleAsync(x => x.Id == proposal.PlanId, ct);
        var container = await db.Containers.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        var order = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == plan.OrderId, ct);
        if (!DemandContentIsCurrent(plan, order, container))
            throw new LedgerException(409, "Approved demand changed.");
        var saved = ReadSavedBlendSelection(proposal.SnapshotJson) ?? throw new LedgerException(409, "Invalid saved approval.");
        var assessmentTarget = plan.TargetMassKg;
        if (!output && context.TryGetProperty("inputSnapshot", out var reviewedInputs) && reviewedInputs.ValueKind == JsonValueKind.Object)
        {
            saved = ReadSavedBlendSelection(reviewedInputs.GetRawText()) ?? throw new LedgerException(409, "Invalid input review scope.");
            assessmentTarget = ParseKg(reviewedInputs.GetProperty("targetKg").GetString()!);
        }
        var facility = context.GetProperty("facilityId").GetGuid();
        var selected = saved.Selection.Allocations.Select(a => new CandidateGroupItem(a.LotId, a.LocationId, a.QuantityKg, a.CandidateRevision)).ToArray();
        if (output)
        {
            if (context.GetProperty("outputId").ValueKind != JsonValueKind.String) throw new LedgerException(409, "Production output is required before review.");
            assessmentTarget = ParseKg(context.GetProperty("outputKg").GetString()!);
            selected = [new(context.GetProperty("outputId").GetGuid(), context.GetProperty("outputLocationId").GetGuid(), assessmentTarget, "ACTUAL_OUTPUT")];
        }
        var policy = await policyReader.ResolveAsync(order.CompanyId, order.MarketId, order.CommodityId, null, ct);
        if (policy is null) return IncompleteWorkflowAssessment("ASSESSMENT_POLICY_MISSING");
        var boundsRead = await residue.PreviewSelectionAsync(selected.GroupBy(x => x.LotId).Select(g => new HypotheticalBlendInput(g.Key, ExportRules.Text(g.Sum(x => x.QuantityKg)))).ToArray(),
            output ? null : context.GetProperty("outputCommodityId").GetGuid(), policy.RequiredSubstanceIds, ct, requireSourceLab: true);
        var currentPolicy = await policyReader.ResolveAsync(order.CompanyId, order.MarketId, order.CommodityId, boundsRead.SnapshotAt, ct);
        if (currentPolicy?.Id != policy.Id) throw new LedgerException(409, "Assessment policy changed during calculation.");
        var bounds = output ? DirectStockResidueAssessment.ConservativeBounds(boundsRead.Inputs, policy.RequiredSubstanceIds) : boundsRead.HypotheticalBlend?.Results;
        var mrl = await mrlReader.ResolveAsync(order.MarketId, order.CommodityId, boundsRead.SnapshotAt, ct);
        PlanningYieldRule? yield = output || context.GetProperty("yieldRule").ValueKind == JsonValueKind.Null ? null :
            JsonSerializer.Deserialize<PlanningYieldRule>(context.GetProperty("yieldRule").GetRawText(), ContainerJson);
        if (yield is not null) yield = yield with { OutputFraction = decimal.Parse(yield.OutputFraction, CultureInfo.InvariantCulture).ToString("0.############", CultureInfo.InvariantCulture) };
        BlendQualityObservation[] observations;
        if (output)
        {
            var quality = await ledger.ReadPlanningQualityObservationsAsync(order.CompanyId, facility, [selected[0].LotId], boundsRead.SnapshotAt, ct);
            observations = quality.Select(q => new BlendQualityObservation(q.LotId, q.AttributeId, decimal.Parse(q.MeasuredValue, CultureInfo.InvariantCulture), q.Unit, q.Id, q.VersionNo)).ToArray();
        }
        else observations = context.GetProperty("quality").EnumerateArray().Select(q => new BlendQualityObservation(q.GetProperty("lot_id").GetGuid(), q.GetProperty("attribute_id").GetGuid(),
            decimal.Parse(decimal.Parse(q.GetProperty("measured_value").GetString()!, CultureInfo.InvariantCulture).ToString("0.############", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture), q.GetProperty("unit").GetString()!, q.GetProperty("id").GetGuid(), q.GetProperty("version_no").GetInt32())).ToArray();
        var limits = await db.Limits.AsNoTracking().Where(x => x.SpecificationId == plan.SpecificationId).ToArrayAsync(ct);
        var rule = new BlendAssessmentRule(policy.Id, policy.VersionNo, policy.MarketId, policy.CommodityId, policy.RequiredSubstanceIds,
            decimal.Parse(policy.MrlMarginFraction, CultureInfo.InvariantCulture), policy.ResidueBasis, policy.EvidenceReference);
        var qualityRules = policy.QualityRules.Select(q => new BlendQualityRule(policy.Id, policy.VersionNo, q.AttributeId, q.Unit, q.Calculation, policy.EvidenceReference)).ToArray();
        var assessment = BlendAssessment.Evaluate(output ? "DIRECT_STOCK" : "BLEND_INPUT_STOCK", assessmentTarget, selected,
            yield is null ? null : new BlendYieldRule(yield.Id, yield.VersionNo, decimal.Parse(yield.OutputFraction, CultureInfo.InvariantCulture), yield.EvidenceId.ToString()), rule, mrl, bounds, limits, qualityRules, observations);
        var dependency = ExportRules.Hash("blend-assessment-dependencies", new { mrl, policy, yield, bounds, observations });
        var findings = assessment.Findings.Where(f => f.Status != "PASS").Select(f => f.Code).ToList();
        if (!output && dependency != saved.DependencyRevision) findings.Add("APPROVAL_EVIDENCE_CHANGED");
        var status = findings.Count == 0 ? "PASS" : "INCOMPLETE";
        return JsonSerializer.SerializeToElement(new
        {
            assessmentStatus = status,
            findings,
            dependencyRevision = dependency,
            evidence = new { applicableMrl = mrl, assessmentPolicy = policy, outputYieldRule = yield, residueBounds = bounds, qualityObservations = observations },
            outputSnapshot = output ? new
            {
                outcome = status,
                assessmentStatus = status,
                dependencyRevision = dependency,
                selection = new
                {
                    mode = "DIRECT_STOCK",
                    compatibilityPolicyId = (Guid?)null,
                    allocations = selected.Select(x => new { x.LotId, x.LocationId, quantityKg = ExportRules.Text(x.QuantityKg), lotCode = "Actual production output", candidateRevision = x.CandidateRevision })
                }
            } : null,
            production = new { proposalId = proposal.Id, operationId = context.GetProperty("operationId"), outputId = context.GetProperty("outputId") }
        }, ContainerJson);
    }
}
