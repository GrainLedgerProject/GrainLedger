using System.Data;
using System.Globalization;
using System.Security.Cryptography;
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
public sealed record ContainerScopeReviewRequest(Guid OperationId, int Version, Guid ProposalId, string Scope, string Decision, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ContainerHandoffContent(Guid LotId, Guid FromLocationId, Guid ToLocationId, string QuantityKg,
    DateTimeOffset OccurredAt, long? PoolRevision = null, Guid? EquipmentId = null, string? OutputKg = null, string? DeclaredLossKg = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ContainerHandoffDraftRequest(Guid OperationId, int Version, Guid ProposalId, string Kind, ContainerHandoffContent Content);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ContainerHandoffReviewRequest(Guid OperationId, int Version, Guid HandoffId, int HandoffVersion, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ContainerHandoffCommitRequest(Guid OperationId, int Version, Guid HandoffId, int HandoffVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ContainerDocumentRequest(Guid OperationId, int Version, string DocumentType, string Filename, string ContentType, string Content, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ContainerSupplementRequest(Guid OperationId, int Version, Guid EvidenceId, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ContainerNoticeReadRequest(Guid OperationId, int Version, Guid NoticeId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ContainerHoldResponseRequest(Guid OperationId, int Version, Guid HoldId, Guid FacilityId, string ResponseAction, Guid EvidenceId, string Reason);

public static partial class ExportModule
{
    private static void MapContainerWorkflow(IEndpointRouteBuilder endpoints)
    {
        var routes = endpoints.MapGroup("/api/export-containers/{id:guid}/workflow").RequireAuthorization()
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>().WithTags("Container workflow");
        routes.MapGet("", async (Guid id, LedgerService ledger, AppDbContext db, CancellationToken ct) =>
        {
            try { return Results.Ok(await WorkflowSqlAsync(await ledger.ActorAsync(ct), id, "detail", EmptyContainerPayload, null, db, ct)); }
            catch (PostgresException ex) { return ContainerDatabaseError(ex); }
        });
        endpoints.MapGet("/api/container-notifications", async (LedgerService ledger, AppDbContext db, CancellationToken ct) =>
        {
            try { return Results.Ok(await WorkflowSqlAsync(await ledger.ActorAsync(ct), Guid.Empty, "notifications", EmptyContainerPayload, null, db, ct)); }
            catch (PostgresException ex) { return ContainerDatabaseError(ex); }
        }).RequireAuthorization().AddEndpointFilter<NoStoreFilter>();
        endpoints.MapGet("/api/container-work-items", async (LedgerService ledger, ICurrentAccess access, AppDbContext db, CancellationToken ct) =>
        {
            if (!await access.HasPermissionAsync(Permissions.ContainersOperate, ct) && !await access.HasPermissionAsync(Permissions.HoldsProcess, ct)) return Results.Forbid();
            try { return Results.Ok(await WorkflowSqlAsync(await ledger.ActorAsync(ct), Guid.Empty, "work-items", EmptyContainerPayload, null, db, ct)); }
            catch (PostgresException ex) { return ContainerDatabaseError(ex); }
        }).RequireAuthorization().AddEndpointFilter<NoStoreFilter>();
        routes.MapGet("/documents/{document:guid}/file", async (Guid id, Guid document, LedgerService ledger, AppDbContext db, HttpContext http, CancellationToken ct) =>
        {
            try
            {
                var file = await WorkflowSqlAsync(await ledger.ActorAsync(ct), id, "document-file", JsonSerializer.SerializeToElement(new { id = document }), null, db, ct);
                http.Response.Headers.XContentTypeOptions = "nosniff";
                return Results.File(Convert.FromBase64String(file.GetProperty("content").GetString()!), file.GetProperty("contentType").GetString(), file.GetProperty("filename").GetString());
            }
            catch (PostgresException ex) { return ContainerDatabaseError(ex); }
        }).RequireAuthorization(Permissions.ExportOrdersRead);
        routes.MapPost("/reviews", (Guid id, ContainerScopeReviewRequest request, HttpContext http, ICurrentAccess access, AppDbContext db,
            LedgerService ledger, ResidueEvaluationService residue, ApplicableMrlRead mrl, BlendAssessmentPolicyRead policies, CancellationToken ct) =>
            WorkflowMutationAsync(id, "review", request, http, access, db, ledger, residue, mrl, policies, ct)).RequireAuthorization(Permissions.BlendApprove);
        routes.MapPost("/handoffs", (Guid id, ContainerHandoffDraftRequest request, HttpContext http, ICurrentAccess access, AppDbContext db,
            LedgerService ledger, ResidueEvaluationService residue, ApplicableMrlRead mrl, BlendAssessmentPolicyRead policies, CancellationToken ct) =>
            WorkflowMutationAsync(id, "handoff-draft", request, http, access, db, ledger, residue, mrl, policies, ct)).RequireAuthorization(Permissions.ConversionsWrite);
        routes.MapPost("/handoffs/approve", (Guid id, ContainerHandoffReviewRequest request, HttpContext http, ICurrentAccess access, AppDbContext db,
            LedgerService ledger, ResidueEvaluationService residue, ApplicableMrlRead mrl, BlendAssessmentPolicyRead policies, CancellationToken ct) =>
            WorkflowMutationAsync(id, "handoff-approve", request, http, access, db, ledger, residue, mrl, policies, ct)).RequireAuthorization(Permissions.BlendApprove);
        routes.MapPost("/handoffs/commit", (Guid id, ContainerHandoffCommitRequest request, HttpContext http, ICurrentAccess access, AppDbContext db,
            LedgerService ledger, ResidueEvaluationService residue, ApplicableMrlRead mrl, BlendAssessmentPolicyRead policies, CancellationToken ct) =>
            WorkflowMutationAsync(id, "handoff-commit", request, http, access, db, ledger, residue, mrl, policies, ct)).RequireAuthorization(Permissions.ConversionsWrite);
        routes.MapPost("/documents", (Guid id, ContainerDocumentRequest request, HttpContext http, ICurrentAccess access, AppDbContext db,
            LedgerService ledger, ResidueEvaluationService residue, ApplicableMrlRead mrl, BlendAssessmentPolicyRead policies, CancellationToken ct) =>
            WorkflowMutationAsync(id, "document", request, http, access, db, ledger, residue, mrl, policies, ct)).RequireAuthorization(Permissions.ContainersWrite);
        routes.MapPost("/supplements", (Guid id, ContainerSupplementRequest request, HttpContext http, ICurrentAccess access, AppDbContext db,
            LedgerService ledger, ResidueEvaluationService residue, ApplicableMrlRead mrl, BlendAssessmentPolicyRead policies, CancellationToken ct) =>
            WorkflowMutationAsync(id, "supplement", request, http, access, db, ledger, residue, mrl, policies, ct)).RequireAuthorization(Permissions.ContainersWrite);
        routes.MapPost("/notifications/read", (Guid id, ContainerNoticeReadRequest request, HttpContext http, ICurrentAccess access, AppDbContext db,
            LedgerService ledger, ResidueEvaluationService residue, ApplicableMrlRead mrl, BlendAssessmentPolicyRead policies, CancellationToken ct) =>
            WorkflowMutationAsync(id, "notice-read", request, http, access, db, ledger, residue, mrl, policies, ct));
        routes.MapPost("/hold-responses", (Guid id, ContainerHoldResponseRequest request, HttpContext http, ICurrentAccess access, AppDbContext db,
            LedgerService ledger, ResidueEvaluationService residue, ApplicableMrlRead mrl, BlendAssessmentPolicyRead policies, CancellationToken ct) =>
            WorkflowMutationAsync(id, "hold-response", request, http, access, db, ledger, residue, mrl, policies, ct)).RequireAuthorization(Permissions.HoldsProcess);
    }

    private static readonly JsonElement EmptyContainerPayload = JsonSerializer.SerializeToElement(new { });
    private static Task<JsonElement> WorkflowSqlAsync(Guid actor, Guid id, string action, JsonElement payload, JsonElement? basis, AppDbContext db, CancellationToken ct) =>
        ContainerSqlAsync("SELECT grainledger.ledger_container_workflow($1,$2,$3,$4,$5)::text", db, ct,
            new() { Value = actor }, new() { Value = id }, new() { Value = action }, new() { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = payload.GetRawText() },
            new() { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = basis?.GetRawText() ?? (object)DBNull.Value });

    private static async Task<IResult> WorkflowMutationAsync<T>(Guid id, string action, T request, HttpContext http, ICurrentAccess access,
        AppDbContext db, LedgerService ledger, ResidueEvaluationService residue, ApplicableMrlRead mrl, BlendAssessmentPolicyRead policies, CancellationToken ct)
    {
        var permission = action is "review" or "handoff-approve" ? Permissions.BlendApprove : action is "handoff-draft" or "handoff-commit" ? Permissions.ConversionsWrite :
            action == "hold-response" ? Permissions.HoldsProcess : Permissions.ContainersWrite;
        var role = action is "review" or "handoff-approve" ? Roles.CustomerManager : action is "handoff-draft" or "handoff-commit" ? Roles.MillOperator :
            action == "hold-response" ? Roles.MillManager : Roles.ExportStaff;
        var payload = JsonSerializer.SerializeToElement(request, ContainerJson);
        if (payload.GetProperty("operationId").GetGuid() == Guid.Empty || payload.GetProperty("version").GetInt32() < 1)
            return Problem(400, "WORKFLOW_REQUEST_INVALID", "Stable operation ID and positive container version required.");
        if (payload.TryGetProperty("reason", out var reason) && (string.IsNullOrWhiteSpace(reason.GetString()) || reason.GetString()!.Length > 1000))
            return Problem(400, "WORKFLOW_REQUEST_INVALID", "A reason of one to 1000 characters is required.");
        if (request is ContainerScopeReviewRequest review && (review.ProposalId == Guid.Empty || review.Scope is not ("ALL_REMAINING" or "OUTPUT_ONLY") || review.Decision is not ("APPROVED" or "REJECTED")))
            return Problem(400, "WORKFLOW_REQUEST_INVALID", "Choose proposal, review scope and decision.");
        if (request is ContainerHandoffDraftRequest draft && (draft.ProposalId == Guid.Empty || draft.Kind is not ("TRANSFER" or "BAGGING") || draft.Content is null ||
            draft.Content.LotId == Guid.Empty || draft.Content.FromLocationId == Guid.Empty || draft.Content.ToLocationId == Guid.Empty || draft.Content.OccurredAt == default ||
            draft.Content.OccurredAt > DateTimeOffset.UtcNow || !ExportRules.TryMass(draft.Content.QuantityKg, "KG", out _) ||
            draft.Kind == "BAGGING" && (!ExportRules.TryMass(draft.Content.OutputKg, "KG", out _) || draft.Content.DeclaredLossKg != "0" || draft.Content.EquipmentId.GetValueOrDefault() == Guid.Empty)))
            return Problem(400, "WORKFLOW_REQUEST_INVALID", "Provide actual handoff time, positions and exact quantities; quantity exceptions are unsupported.");
        if (request is ContainerHoldResponseRequest response && (response.HoldId == Guid.Empty || response.FacilityId == Guid.Empty || response.EvidenceId == Guid.Empty || response.ResponseAction is not ("ACKNOWLEDGE" or "ISOLATE" or "RECORD_RESPONSE")))
            return Problem(400, "WORKFLOW_REQUEST_INVALID", "Provide HOLD, assigned facility, evidence and supported handling action.");
        if (request is ContainerDocumentRequest document)
        {
            if (string.IsNullOrWhiteSpace(document.DocumentType) || document.DocumentType.Trim().Length > 80 || string.IsNullOrWhiteSpace(document.Filename) ||
                document.Filename.Length > 200 || document.Filename != Path.GetFileName(document.Filename) || document.Filename.Any(c => char.IsControl(c) || c is '/' or '\\') ||
                string.IsNullOrWhiteSpace(document.ContentType) || document.ContentType.Length > 120 || document.ContentType.Any(char.IsControl) || !System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(document.ContentType, out _) ||
                document.Content is null || document.Content.Length > 14_000_000)
                return Problem(400, "DOCUMENT_INVALID", "Provide document type, safe filename, media type and a file up to 10 MiB.");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(document.Content); }
            catch (FormatException) { return Problem(400, "DOCUMENT_INVALID", "File must contain valid base64 bytes."); }
            if (bytes.Length is < 1 or > 10_485_760) return Problem(400, "DOCUMENT_INVALID", "File must be between one byte and 10 MiB.");
            payload = JsonSerializer.SerializeToElement(new { document.OperationId, document.Version, document.DocumentType, document.Filename, document.ContentType,
                content = Convert.ToBase64String(bytes), document.Reason, sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) }, ContainerJson);
        }
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
            if (action != "notice-read" && !await access.HasPermissionAsync(permission, ct)) return Results.Forbid();
            var actor = await ledger.ActorAsync(ct);
            if (action != "notice-read" && !await db.Users.AsNoTracking().AnyAsync(x => x.Id == actor && x.IsActive && x.Role == role, ct)) return Results.Forbid();
            await WorkflowSqlAsync(actor, id, "detail", EmptyContainerPayload, null, db, ct);
            var company = await db.Containers.AsNoTracking().Where(x => x.Id == id).Select(x => x.CompanyId).SingleAsync(ct);
            await LockAsync(db, company, ct);
            var replayPayload = System.Text.Json.Nodes.JsonNode.Parse(payload.GetRawText())!.AsObject(); replayPayload["replayAction"] = action;
            var replay = await WorkflowSqlAsync(actor, id, "replay", JsonSerializer.SerializeToElement(replayPayload), null, db, ct);
            if (!replay.TryGetProperty("replayMissing", out _)) { await tx.CommitAsync(ct); return Results.Ok(replay); }
            JsonElement? basis = null;
            if (request is ContainerScopeReviewRequest { Decision: "APPROVED" } approved)
            {
                if (!await access.HasPermissionAsync(Permissions.ResidueEvaluate, ct)) return Results.Forbid();
                basis = await AssessWorkflowReviewAsync(actor, id, approved.ProposalId, approved.Scope, db, ledger, residue, mrl, policies, ct);
            }
            else if (action is "handoff-approve" or "handoff-commit")
                basis = await AssessHandoffAsync(actor, id, payload.GetProperty("handoffId").GetGuid(), db, ledger, residue, mrl, policies, ct);
            if (basis is { } b && b.GetProperty("assessmentStatus").GetString() != "PASS")
                return Results.Json(new { code = "WORKFLOW_BLOCKED", readiness = b }, statusCode: 409);
            var result = await WorkflowSqlAsync(actor, id, action, payload, basis, db, ct);
            if (basis is not null)
            {
                var final = request is ContainerScopeReviewRequest aReview
                    ? await AssessWorkflowReviewAsync(actor, id, aReview.ProposalId, aReview.Scope, db, ledger, residue, mrl, policies, ct)
                    : await AssessHandoffAsync(actor, id, payload.GetProperty("handoffId").GetGuid(), db, ledger, residue, mrl, policies, ct);
                if (final.GetProperty("assessmentStatus").GetString() != "PASS" || final.GetProperty("dependencyRevision").GetString() != basis.Value.GetProperty("dependencyRevision").GetString())
                    return Problem(409, "WORKFLOW_EVIDENCE_CHANGED", "Scientific inputs changed before final commit; all workflow changes rolled back.");
                await WorkflowSqlAsync(actor, id, "assert-basis", EmptyContainerPayload, final, db, ct);
            }
            await tx.CommitAsync(ct);
            return Results.Ok(result);
        }
        catch (PostgresException ex) { return ContainerDatabaseError(ex); }
        catch (LedgerException ex) { return Problem(ex.StatusCode, "WORKFLOW_BLOCKED", ex.Message); }
    }

    private static async Task<JsonElement> AssessWorkflowReviewAsync(Guid actor, Guid containerId, Guid proposalId, string scope, AppDbContext db,
        LedgerService ledger, ResidueEvaluationService residue, ApplicableMrlRead mrl, BlendAssessmentPolicyRead policies, CancellationToken ct)
    {
        var context = await WorkflowSqlAsync(actor, containerId, "context", JsonSerializer.SerializeToElement(new { proposalId }), null, db, ct);
        var container = await db.Containers.AsNoTracking().SingleAsync(x => x.Id == containerId, ct);
        var proposal = await db.BlendPlanProposals.AsNoTracking().SingleAsync(x => x.Id == proposalId, ct);
        var plan = await db.BlendPlanDrafts.AsNoTracking().SingleAsync(x => x.Id == proposal.PlanId, ct);
        var order = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == container.OrderId, ct);
        if (!DemandContentIsCurrent(plan, order, container) && scope != "OUTPUT_ONLY") throw new LedgerException(409, "Plan demand changed; create a current plan for unproduced demand. Existing outputs may receive an OUTPUT_ONLY review against current requirements.");
        var facility = context.GetProperty("facilityId").GetGuid();
        var inputs = ReadWorkflowSelection(context.GetProperty("inputs")); var outputs = ReadWorkflowSelection(context.GetProperty("outputs"));
        JsonElement? input = null, output = null;
        if (scope == "ALL_REMAINING" && inputs.Length > 0)
            input = await AssessWorkflowSelectionAsync(order, facility, inputs, "BLEND_INPUT_STOCK", ParseKg(context.GetProperty("remainingProductionKg").GetString()!),
                context.GetProperty("compatibilityPolicyId").GetGuid(), db, ledger, residue, mrl, policies, ct);
        if (outputs.Length > 0)
            output = await AssessWorkflowSelectionAsync(order, facility, outputs, "DIRECT_STOCK", outputs.Sum(x => x.QuantityKg), null, db, ledger, residue, mrl, policies, ct);
        var findings = new List<string>();
        foreach (var assessment in new[] { input, output }.Where(x => x.HasValue).Select(x => x!.Value))
            findings.AddRange(assessment.GetProperty("findings").EnumerateArray().Select(x => x.GetString()!));
        if (input is null && output is null) findings.Add("REVIEW_SCOPE_EMPTY");
        if (scope == "OUTPUT_ONLY" && output is null) findings.Add("ACTUAL_OUTPUT_REQUIRED");
        return JsonSerializer.SerializeToElement(new
        {
            scope, context, assessmentStatus = findings.Count == 0 ? "PASS" : "INCOMPLETE", findings,
            dependencyRevision = ExportRules.Hash("container-scope-review", new { input = input?.GetProperty("dependencyRevision").GetString(), output = output?.GetProperty("dependencyRevision").GetString(), context }),
            evidence = (output ?? input)?.GetProperty("evidence"), inputSnapshot = input?.GetProperty("snapshot"), outputSnapshot = output?.GetProperty("snapshot")
        }, ContainerJson);
    }

    private static async Task<JsonElement> AssessHandoffAsync(Guid actor, Guid containerId, Guid handoffId, AppDbContext db, LedgerService ledger,
        ResidueEvaluationService residue, ApplicableMrlRead mrl, BlendAssessmentPolicyRead policies, CancellationToken ct)
    {
        var detail = await WorkflowSqlAsync(actor, containerId, "detail", EmptyContainerPayload, null, db, ct);
        var handoff = detail.GetProperty("handoffs").EnumerateArray().SingleOrDefault(x => x.GetProperty("id").GetGuid() == handoffId);
        if (handoff.ValueKind != JsonValueKind.Object) throw new LedgerException(403, "Handoff is unavailable.");
        var content = handoff.GetProperty("content");
        var container = await db.Containers.AsNoTracking().SingleAsync(x => x.Id == containerId, ct);
        var order = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == container.OrderId, ct);
        CandidateGroupItem[] selected = [new(content.GetProperty("lotId").GetGuid(), content.GetProperty("fromLocationId").GetGuid(), ParseKg(content.GetProperty("quantityKg").GetString()!), "HANDOFF_INPUT")];
        return await AssessWorkflowSelectionAsync(order, handoff.GetProperty("facilityId").GetGuid(), selected, "DIRECT_STOCK", selected[0].QuantityKg, null, db, ledger, residue, mrl, policies, ct,
            handoffQuality: at => WorkflowSqlAsync(actor, containerId, "handoff-quality", JsonSerializer.SerializeToElement(new { handoffId, at }), null, db, ct));
    }

    private static JsonElement IncompleteWorkflowAssessment(string finding) => JsonSerializer.SerializeToElement(new
    {
        assessmentStatus = "INCOMPLETE", findings = new[] { finding }, dependencyRevision = (string?)null,
        evidence = (object?)null, snapshot = (object?)null
    }, ContainerJson);

    private static decimal ParseKg(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
    private static CandidateGroupItem[] ReadWorkflowSelection(JsonElement items) => items.EnumerateArray()
        .Where(x => ParseKg(x.GetProperty("quantityKg").GetString()!) > 0).Select(x => new CandidateGroupItem(x.GetProperty("lotId").GetGuid(), x.GetProperty("locationId").GetGuid(), ParseKg(x.GetProperty("quantityKg").GetString()!), "CURRENT_REVIEW")).ToArray();

    private static async Task<JsonElement> AssessWorkflowSelectionAsync(ExportOrder order, Guid facility, CandidateGroupItem[] selected, string mode, decimal target,
        Guid? compatibility, AppDbContext db, LedgerService ledger, ResidueEvaluationService residue, ApplicableMrlRead mrlReader, BlendAssessmentPolicyRead policyReader, CancellationToken ct,
        IReadOnlyDictionary<Guid, Guid>? lotFacilities = null, Func<DateTimeOffset, Task<JsonElement>>? handoffQuality = null)
    {
        var policy = await policyReader.ResolveAsync(order.CompanyId, order.MarketId, order.CommodityId, null, ct);
        if (policy is null) return IncompleteWorkflowAssessment("ASSESSMENT_POLICY_MISSING");
        PlanningYieldRule? yield = compatibility is Guid compatible ? await ledger.ReadPlanningYieldRuleAsync(order.CompanyId, facility, compatible, DateTimeOffset.UtcNow, ct) : null;
        if (yield is not null) yield = yield with { OutputFraction = ParseKg(yield.OutputFraction).ToString("0.############", CultureInfo.InvariantCulture) };
        Guid? outputCommodity = null;
        if (compatibility is Guid policyId)
        {
            var (_, production) = await ledger.ReadPlanningCandidatesWithCompatibilityAsync(order.CompanyId, facility, ct);
            var rule = production?.EnumerateArray().SingleOrDefault(x => x.GetProperty("id").GetGuid() == policyId) ?? default;
            if (rule.ValueKind == JsonValueKind.Object) outputCommodity = rule.GetProperty("outputLedgerCommodityId").GetGuid();
        }
        var boundsRead = await residue.PreviewSelectionAsync(selected.GroupBy(x => x.LotId).Select(g => new HypotheticalBlendInput(g.Key, ExportRules.Text(g.Sum(x => x.QuantityKg)))).ToArray(), outputCommodity, policy.RequiredSubstanceIds, ct);
        if ((await policyReader.ResolveAsync(order.CompanyId, order.MarketId, order.CommodityId, boundsRead.SnapshotAt, ct))?.Id != policy.Id)
            throw new LedgerException(409, "Assessment policy changed during review.");
        // Bagging preserves product identity but creates a different physical stage. Retain and
        // assess those stages independently; never average concentrations or invent a conversion factor.
        var physicalGroups = boundsRead.Inputs.GroupBy(x =>
        {
            var identity = x.Results.FirstOrDefault();
            return (identity?.CommodityId, identity?.ProductStage, identity?.ProductIdentity);
        }).ToArray();
        var directBounds = physicalGroups.Select(g => DirectStockResidueAssessment.ConservativeBounds(g.ToArray(), policy.RequiredSubstanceIds)).ToArray();
        IReadOnlyList<ResidueResult>? bounds = mode == "DIRECT_STOCK" ? directBounds.SelectMany(x => x).ToArray() : boundsRead.HypotheticalBlend?.Results;
        var mrl = await mrlReader.ResolveAsync(order.MarketId, order.CommodityId, boundsRead.SnapshotAt, ct);
        var observationsList = new List<BlendQualityObservation>();
        if (handoffQuality is not null)
        {
            var quality = await handoffQuality(boundsRead.SnapshotAt);
            observationsList.AddRange(quality.EnumerateArray().Select(q => new BlendQualityObservation(q.GetProperty("lot_id").GetGuid(), q.GetProperty("attribute_id").GetGuid(),
                ParseKg(ParseKg(q.GetProperty("measured_value").GetString()!).ToString("0.############", CultureInfo.InvariantCulture)), q.GetProperty("unit").GetString()!, q.GetProperty("id").GetGuid(), q.GetProperty("version_no").GetInt32())));
        }
        else foreach (var group in selected.GroupBy(x => lotFacilities?.GetValueOrDefault(x.LotId) ?? facility))
        {
            var quality = await ledger.ReadPlanningQualityObservationsAsync(order.CompanyId, group.Key, group.Select(x => x.LotId).Distinct().ToArray(), boundsRead.SnapshotAt, ct);
            observationsList.AddRange(quality.Select(q => new BlendQualityObservation(q.LotId, q.AttributeId,
                mode == "BLEND_INPUT_STOCK" ? ParseKg(ParseKg(q.MeasuredValue).ToString("0.############", CultureInfo.InvariantCulture)) : ParseKg(q.MeasuredValue), q.Unit, q.Id, q.VersionNo)));
        }
        var observations = observationsList.OrderBy(x => x.LotId).ThenBy(x => x.AttributeId).ToArray();
        var specification = await db.Specifications.AsNoTracking().SingleAsync(x => x.OrderId == order.Id && x.VersionNo == order.CurrentSpecificationVersion, ct);
        var limits = await db.Limits.AsNoTracking().Where(x => x.SpecificationId == specification.Id).ToArrayAsync(ct);
        var ruleDefinition = new BlendAssessmentRule(policy.Id, policy.VersionNo, policy.MarketId, policy.CommodityId, policy.RequiredSubstanceIds, ParseKg(policy.MrlMarginFraction), policy.ResidueBasis, policy.EvidenceReference);
        var qualityRules = policy.QualityRules.Select(q => new BlendQualityRule(policy.Id, policy.VersionNo, q.AttributeId, q.Unit, q.Calculation, policy.EvidenceReference)).ToArray();
        var assessment = BlendAssessment.Evaluate(mode, target, selected, yield is null ? null : new BlendYieldRule(yield.Id, yield.VersionNo, ParseKg(yield.OutputFraction), yield.EvidenceId.ToString()), ruleDefinition, mrl, bounds, limits, qualityRules, observations);
        if (mode == "DIRECT_STOCK" && physicalGroups.Length > 1)
        {
            var retained = assessment.Findings.Where(x => !x.Code.StartsWith("RESIDUE_", StringComparison.Ordinal)).ToList();
            for (var i = 0; i < physicalGroups.Length; i++)
            {
                var ids = physicalGroups[i].Select(x => x.LotId).ToHashSet();
                var allocations = selected.Where(x => ids.Contains(x.LotId)).ToArray();
                var perStage = BlendAssessment.Evaluate(mode, allocations.Sum(x => x.QuantityKg), allocations, null, ruleDefinition, mrl, directBounds[i], limits, qualityRules, observations);
                retained.AddRange(perStage.Findings.Where(x => x.Code.StartsWith("RESIDUE_", StringComparison.Ordinal)));
            }
            var identity = physicalGroups[0].Key;
            if (physicalGroups.Any(g => g.Key.CommodityId != identity.CommodityId || g.Key.ProductIdentity != identity.ProductIdentity ||
                g.Key.ProductStage != g.Key.ProductIdentity && g.Key.ProductStage != "BAGGED_RICE"))
                retained.Add(new("OUTPUT_PRODUCT_IDENTITY_CHANGED", "INCOMPLETE", null, null, null, null));
            assessment = assessment with { Findings = retained, Status = retained.Any(x => x.Status == "FAIL") ? "FAIL" : retained.Any(x => x.Status == "INCOMPLETE") ? "INCOMPLETE" : "PASS" };
        }
        var dependency = ExportRules.Hash("blend-assessment-dependencies", new { mrl, policy, yield, bounds, observations });
        var status = assessment.Status; var findings = assessment.Findings.Where(f => f.Status != "PASS").Select(f => f.Code).ToArray();
        return JsonSerializer.SerializeToElement(new
        {
            assessmentStatus = status, findings, dependencyRevision = dependency,
            evidence = new { applicableMrl = mrl, assessmentPolicy = policy, outputYieldRule = yield, residueBounds = bounds, qualityObservations = observations },
            snapshot = new { outcome = status, assessmentStatus = status, targetKg = ExportRules.Text(target), dependencyRevision = dependency, selection = new { mode, compatibilityPolicyId = compatibility,
                allocations = selected.Select(x => new { x.LotId, x.LocationId, quantityKg = ExportRules.Text(x.QuantityKg), lotCode = "Reviewed stock", candidateRevision = x.CandidateRevision }) } }
        }, ContainerJson);
    }

    private static async Task<JsonElement> AssessWholeContainerAsync(JsonElement detail, List<string> findings, AppDbContext db, LedgerService ledger,
        ResidueEvaluationService residue, ApplicableMrlRead mrl, BlendAssessmentPolicyRead policies, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == detail.GetProperty("orderId").GetGuid(), ct);
        var scopes = detail.GetProperty("closingScopes").EnumerateArray().ToArray();
        var revisions = new List<object>();
        var stale = new List<Guid>();
        var facilities = scopes.ToDictionary(x => x.GetProperty("proposalId").GetGuid(), x => x.GetProperty("facilityId").GetGuid());
        foreach (var scope in scopes)
        {
            var proposal = scope.GetProperty("proposalId").GetGuid();
            if (scope.GetProperty("latestDecision").GetString() is { } decision && decision != "APPROVED") { findings.Add("CLOSE_SCOPE_REVIEW_REQUIRED"); stale.Add(proposal); }
            var snapshot = scope.GetProperty("snapshot");
            var saved = snapshot.ValueKind == JsonValueKind.Object ? ReadSavedBlendSelection(snapshot.GetRawText()) : null;
            if (saved is null) { findings.Add("CLOSE_SCOPE_REVIEW_REQUIRED"); stale.Add(proposal); continue; }
            var selected = saved.Value.Selection.Allocations.Select(x => new CandidateGroupItem(x.LotId, x.LocationId, x.QuantityKg, x.CandidateRevision)).ToArray();
            var assessment = await AssessWorkflowSelectionAsync(order, scope.GetProperty("facilityId").GetGuid(), selected, "DIRECT_STOCK", selected.Sum(x => x.QuantityKg), null, db, ledger, residue, mrl, policies, ct);
            findings.AddRange(assessment.GetProperty("findings").EnumerateArray().Select(x => x.GetString()!));
            if (assessment.GetProperty("assessmentStatus").GetString() != "PASS" || assessment.GetProperty("dependencyRevision").GetString() != saved.Value.DependencyRevision)
            { findings.Add("CLOSE_SCOPE_REVIEW_REQUIRED"); stale.Add(proposal); }
            revisions.Add(new { proposalId = proposal, reviewId = scope.GetProperty("reviewId"), productionOperations = scope.GetProperty("productionOperations"), dependency = assessment.GetProperty("dependencyRevision").GetString() });
        }
        var lots = new Dictionary<Guid, Guid>();
        var actual = new List<CandidateGroupItem>();
        foreach (var load in detail.GetProperty("loads").EnumerateArray())
        foreach (var line in load.GetProperty("lines").EnumerateArray())
        {
            var lot = line.GetProperty("lotId").GetGuid();
            if (line.TryGetProperty("restricted", out var restricted) && restricted.GetBoolean())
            { findings.Add("LOT_RESTRICTED"); stale.Add(load.GetProperty("proposalId").GetGuid()); }
            lots[lot] = facilities[load.GetProperty("proposalId").GetGuid()];
            actual.Add(new(lot, line.GetProperty("locationId").GetGuid(), ParseKg(line.GetProperty("quantityKg").GetString()!), "ACTUAL_MANIFEST"));
        }
        JsonElement? whole = null;
        if (actual.Count == 0) findings.Add("NO_ACTUAL_LOADING");
        else
        {
            var selected = actual.GroupBy(x => (x.LotId, x.LocationId)).Select(g => new CandidateGroupItem(g.Key.LotId, g.Key.LocationId, g.Sum(x => x.QuantityKg), "ACTUAL_MANIFEST")).OrderBy(x => x.LotId).ThenBy(x => x.LocationId).ToArray();
            whole = await AssessWorkflowSelectionAsync(order, lots.First().Value, selected, "DIRECT_STOCK", ParseKg(detail.GetProperty("targetKg").GetString()!), null, db, ledger, residue, mrl, policies, ct, lots);
            findings.AddRange(whole.Value.GetProperty("findings").EnumerateArray().Select(x => x.GetString()!));
        }
        return JsonSerializer.SerializeToElement(new
        {
            phase = "CLOSE", checkedAt = DateTimeOffset.UtcNow, assessmentStatus = findings.Count == 0 ? "PASS" : "INCOMPLETE", findings = findings.Distinct(),
            dependencyRevision = ExportRules.Hash("whole-container-close", new { revisions, whole = whole?.GetProperty("dependencyRevision").GetString() }),
            evidence = whole?.GetProperty("evidence"), wholeManifest = whole?.GetProperty("snapshot"), reviewedScopes = revisions, staleProposalIds = stale.Distinct(),
            calculationVersion = "CONTAINER_WHOLE_MANIFEST_V2"
        }, ContainerJson);
    }

    private static bool InvalidatesUnfinishedAuthority(JsonElement basis) => basis.TryGetProperty("findings", out var findings) && findings.EnumerateArray().Any(x =>
        x.GetString() is { } code && (code.Contains("EVIDENCE_CHANGED", StringComparison.Ordinal) || code.Contains("MRL", StringComparison.Ordinal) ||
        code is "CLOSE_SCOPE_REVIEW_REQUIRED" or "ASSESSMENT_POLICY_MISSING" or "QUALITY_LIMIT" or "RESIDUE_LIMIT" or "LOT_RESTRICTED" or "CONTAINER_HOLD"));

    private static bool InvalidatesActiveAuthority(JsonElement detail, JsonElement basis)
    {
        if (!InvalidatesUnfinishedAuthority(basis)) return false;
        return !basis.TryGetProperty("staleProposalIds", out var stale) || detail.GetProperty("holds").GetArrayLength() > 0 ||
            stale.EnumerateArray().Any(x => x.GetGuid() == detail.GetProperty("approval").GetProperty("proposalId").GetGuid());
    }

    private static Task<JsonElement> InvalidateUnfinishedAuthorityAsync(Guid actor, Guid container, int version, JsonElement basis, AppDbContext db, CancellationToken ct) =>
        WorkflowSqlAsync(actor, container, "invalidate", JsonSerializer.SerializeToElement(new { operationId = Guid.NewGuid(), version }), basis, db, ct);
}
