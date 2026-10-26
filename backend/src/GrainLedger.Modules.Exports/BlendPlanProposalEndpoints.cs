using System.Text.Json;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Identity;
using GrainLedger.Modules.Ledger;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace GrainLedger.Modules.Exports;

public sealed record ProposeBlendPlanRequest(Guid OperationId, Guid PreviewReference);

public static partial class ExportModule
{
    private static Task<bool> ProposalReadyAsync(ExportDbContext db, CancellationToken ct) =>
        db.Database.SqlQueryRaw<bool>("""
            SELECT to_regclass('exports.blend_plan_previews') IS NOT NULL AND
                   to_regclass('exports.blend_plan_proposals') IS NOT NULL AS "Value"
            """).SingleAsync(ct);

    private static async Task<IResult> ProposeBlendPlanAsync(Guid id,
        ProposeBlendPlanRequest request, HttpContext http, IdentityDbContext identity,
        ICurrentAccess access, ExportDbContext db, LedgerService ledger,
        ResidueEvaluationService residue, ApplicableMrlRead applicableMrl,
        BlendAssessmentPolicyRead assessmentPolicies, TimeProvider clock,
        CancellationToken ct)
    {
        var scope = await ScopeAsync(http, identity, access, Permissions.BlendPropose, ct);
        if (scope is null) return Results.Forbid();
        if (!await access.HasPermissionAsync(Permissions.ResidueEvaluate, ct)) return Results.Forbid();
        if (request.OperationId == Guid.Empty || request.PreviewReference == Guid.Empty)
            return Invalid(new() { ["request"] = ["Provide an operation ID and preview reference."] });
        if (!await ProposalReadyAsync(db, ct))
            return Problem(503, "PROPOSAL_SCHEMA_NOT_READY", "Apply the reviewed Export proposal migration.");
        var hash = ExportRules.Hash("propose-blend-plan:" + id, request);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // The same global lock is acquired by Ledger stock/evidence writes and
        // Catalog MRL/assessment-policy publication. F13 writes acquire the
        // company lock below. Never invert this order in a proposal writer.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
        await LockAsync(db, scope.Value.Company, ct);
        var prior = await db.Operations.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == scope.Value.Company && x.ActorId == scope.Value.Actor &&
            x.OperationId == request.OperationId, ct);
        if (prior is not null)
        {
            var replay = Replay(prior, hash, http);
            if (prior.RequestHash == hash && prior.ResponseStatus == 201)
                http.Response.Headers.Location = $"/api/blend-plans/{id}/proposal";
            return replay;
        }
        var preview = await db.BlendPlanPreviews.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == request.PreviewReference && x.PlanId == id &&
            x.CompanyId == scope.Value.Company && x.ActorId == scope.Value.Actor, ct);
        if (preview is null) return Results.NotFound();
        if (preview.ExpiresAt <= clock.GetUtcNow())
            return Problem(409, "PLAN_PREVIEW_EXPIRED", "Run a new preview before proposing.");
        if (await db.BlendPlanProposals.AnyAsync(x => x.PlanId == id &&
            x.CompanyId == scope.Value.Company, ct))
            return Problem(409, "PLAN_ALREADY_PROPOSED", "This draft already has a proposal.");
        var previewRequest = JsonSerializer.Deserialize<PreviewBlendPlanRequest>(preview.RequestJson);
        if (previewRequest is null)
            return Problem(409, "PLAN_PREVIEW_INVALID", "Run a new preview before proposing.");
        var recomputed = await ComputeBlendPlanPreviewAsync(id, previewRequest,
            http, identity, access, db, ledger, residue, applicableMrl,
            assessmentPolicies, clock, false, Permissions.BlendPropose, ct);
        if (recomputed is not IStatusCodeHttpResult { StatusCode: StatusCodes.Status200OK } ||
            recomputed is not IValueHttpResult { Value: { } value }) return recomputed;
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var data = document.RootElement;
        if (data.GetProperty("outcome").GetString() != "PASS" ||
            data.GetProperty("assessmentStatus").GetString() != "PASS" ||
            data.GetProperty("snapshotFingerprint").GetString() != preview.Fingerprint)
            return Problem(409, "PLAN_PREVIEW_STALE", "Stock, demand, evidence or policy changed. Run a new preview.");
        var plan = await db.BlendPlanDrafts.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        var now = clock.GetUtcNow();
        var proposal = new BlendPlanProposal
        {
            Id = Guid.NewGuid(), CompanyId = scope.Value.Company, PlanId = id,
            PreviewId = preview.Id, ActorId = scope.Value.Actor,
            SnapshotJson = preview.SnapshotJson, ProposedAt = now
        };
        db.BlendPlanProposals.Add(proposal);
        PlanEvent(db, plan, scope.Value.Actor, "PLAN_PROPOSED", now);
        var order = await db.Orders.SingleAsync(x => x.Id == plan.OrderId, ct);
        Event(db, order, scope.Value.Actor, "BLEND_PLAN_PROPOSED",
            new { planId = id, proposalId = proposal.Id, previewId = preview.Id }, now);
        await db.SaveChangesAsync(ct);
        var response = new { proposal.Id, planId = id, proposal.PreviewId,
            proposal.ProposedAt, assessment = JsonSerializer.Deserialize<JsonElement>(proposal.SnapshotJson),
            freshness = "NOT_RECHECKED", stockReserved = false };
        await StoreResultAsync(db, new ExportOperation
        {
            Id = Guid.NewGuid(), CompanyId = scope.Value.Company, ActorId = scope.Value.Actor,
            OperationId = request.OperationId, RequestHash = hash,
            OrderId = plan.OrderId, ContainerId = plan.ContainerId
        }, response, 201, plan.Version, ct);
        await transaction.CommitAsync(ct);
        http.Response.Headers.Location = $"/api/blend-plans/{id}/proposal";
        return Results.Created($"/api/blend-plans/{id}/proposal", response);
    }

    private static async Task<IResult> GetBlendPlanProposalAsync(Guid id,
        HttpContext http, IdentityDbContext identity, ICurrentAccess access,
        ExportDbContext db, LedgerService ledger, ResidueEvaluationService residue,
        ApplicableMrlRead applicableMrl, BlendAssessmentPolicyRead assessmentPolicies,
        TimeProvider clock, CancellationToken ct)
    {
        var scope = await ScopeAsync(http, identity, access, Permissions.ExportOrdersRead, ct);
        if (scope is null) return Results.Forbid();
        if (!await ProposalReadyAsync(db, ct))
            return Problem(503, "PROPOSAL_SCHEMA_NOT_READY", "Apply the reviewed Export proposal migration.");
        var proposal = await db.BlendPlanProposals.AsNoTracking().SingleOrDefaultAsync(x =>
            x.PlanId == id && x.CompanyId == scope.Value.Company, ct);
        if (proposal is null) return Results.NotFound();
        var plan = await db.BlendPlanDrafts.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        var container = await db.Containers.AsNoTracking().SingleAsync(x => x.Id == plan.ContainerId, ct);
        var order = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == plan.OrderId, ct);
        var storedPreview = await db.BlendPlanPreviews.AsNoTracking()
            .SingleAsync(x => x.Id == proposal.PreviewId && x.CompanyId == scope.Value.Company, ct);
        var storedRequest = JsonSerializer.Deserialize<PreviewBlendPlanRequest>(storedPreview.RequestJson);
        var reasons = new List<string>();
        if (!DemandIsCurrent(plan, order, container)) reasons.Add("DEMAND_CHANGED");
        if (storedRequest is null) reasons.Add("PREVIEW_INVALID");
        if (reasons.Count == 0)
        {
            var current = await ComputeBlendPlanPreviewAsync(id, storedRequest!,
                http, identity, access, db, ledger, residue, applicableMrl,
                assessmentPolicies, clock, false, Permissions.ExportOrdersRead, ct);
            if (current is not IStatusCodeHttpResult { StatusCode: StatusCodes.Status200OK } ||
                current is not IValueHttpResult { Value: { } value })
                reasons.Add("PLANNING_INPUTS_CHANGED_OR_UNAVAILABLE");
            else
            {
                using var document = JsonDocument.Parse(JsonSerializer.Serialize(value,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                var data = document.RootElement;
                if (data.GetProperty("outcome").GetString() != "PASS" ||
                    data.GetProperty("assessmentStatus").GetString() != "PASS" ||
                    data.GetProperty("snapshotFingerprint").GetString() != storedPreview.Fingerprint)
                    reasons.Add("PREVIEW_DEPENDENCIES_CHANGED");
            }
        }
        return Results.Ok(new { proposal.Id, proposal.PlanId, proposal.PreviewId,
            proposal.ProposedAt, assessment = JsonSerializer.Deserialize<JsonElement>(proposal.SnapshotJson),
            demandCurrent = DemandIsCurrent(plan, order, container),
            stockAndEvidenceCurrent = reasons.Count == 0,
            freshness = reasons.Count == 0 ? "CURRENT" : "STALE",
            freshnessReasons = reasons, stockReserved = false });
    }
}
