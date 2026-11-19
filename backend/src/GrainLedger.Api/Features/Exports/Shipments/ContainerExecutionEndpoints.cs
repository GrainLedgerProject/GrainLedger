using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GrainLedger.Api.Features.Catalogs;
using GrainLedger.Api.Features.Identity;
using GrainLedger.Api.Features.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace GrainLedger.Api.Features.Exports;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ContainerMetadataRequest(Guid OperationId, int Version, string? ContainerNumber, string? SealNumber);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ContainerHoldRequest(Guid OperationId, int Version, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ActualLoadLine(Guid LotId, Guid LocationId, string QuantityKg);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ActualLoadRequest(Guid OperationId, int Version, Guid ProposalId, int DecisionVersion,
    DateTimeOffset LoadedAt, ActualLoadLine[] Lines);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ContainerCloseRequest(Guid OperationId, int Version);

public static partial class ExportModule
{
    private static readonly JsonSerializerOptions ContainerJson = new(JsonSerializerDefaults.Web);

    private static void MapContainerExecution(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/export-containers/{id:guid}/execution")
            .RequireAuthorization().WithTags("Container execution")
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>();
        group.MapGet("", ContainerExecutionDetailAsync).RequireAuthorization(Permissions.ExportOrdersRead);
        group.MapGet("/history", async (Guid id, int? page, int? pageSize, LedgerService ledger, AppDbContext db, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await ContainerSqlAsync("SELECT grainledger.ledger_export_container_read($1,$2,'history',$3,$4)::text", db, ct,
                new() { Value = await ledger.ActorAsync(ct) }, new() { Value = id }, new() { Value = page ?? 1 }, new() { Value = pageSize ?? 25 }));
            }
            catch (PostgresException ex) { return ContainerDatabaseError(ex); }
        }).RequireAuthorization(Permissions.ExportOrdersRead);
        group.MapGet("/dossier", async (Guid id, LedgerService ledger, AppDbContext db, CancellationToken ct) =>
            await ContainerReadResultAsync(id, "dossier", ledger, db, ct)).RequireAuthorization(Permissions.ExportOrdersRead);
        group.MapPost("/metadata", async (Guid id, ContainerMetadataRequest request, HttpContext http, ICurrentAccess access,
            AppDbContext db, LedgerService ledger, ResidueEvaluationService residue, ApplicableMrlRead mrl,
            BlendAssessmentPolicyRead policies, CancellationToken ct) =>
            await ContainerMutationAsync(id, "metadata", request, http, access, db, ledger, residue, mrl, policies, ct))
            .RequireAuthorization(Permissions.ContainersWrite);
        group.MapPost("/holds", async (Guid id, ContainerHoldRequest request, HttpContext http, ICurrentAccess access,
            AppDbContext db, LedgerService ledger, ResidueEvaluationService residue, ApplicableMrlRead mrl,
            BlendAssessmentPolicyRead policies, CancellationToken ct) =>
            await ContainerMutationAsync(id, "hold", request, http, access, db, ledger, residue, mrl, policies, ct))
            .RequireAuthorization(Permissions.HoldsManage);
        group.MapPost("/loads", async (Guid id, ActualLoadRequest request, HttpContext http, ICurrentAccess access,
            AppDbContext db, LedgerService ledger, ResidueEvaluationService residue, ApplicableMrlRead mrl,
            BlendAssessmentPolicyRead policies, CancellationToken ct) =>
            await ContainerMutationAsync(id, "load", request, http, access, db, ledger, residue, mrl, policies, ct))
            .RequireAuthorization(Permissions.ContainersWrite);
        group.MapPost("/close", async (Guid id, ContainerCloseRequest request, HttpContext http, ICurrentAccess access,
            AppDbContext db, LedgerService ledger, ResidueEvaluationService residue, ApplicableMrlRead mrl,
            BlendAssessmentPolicyRead policies, CancellationToken ct) =>
            await ContainerMutationAsync(id, "close", request, http, access, db, ledger, residue, mrl, policies, ct))
            .RequireAuthorization(Permissions.ContainersWrite);
    }

    private static async Task<IResult> ContainerExecutionDetailAsync(Guid id, LedgerService ledger, ICurrentAccess access,
        AppDbContext db, ResidueEvaluationService residue, ApplicableMrlRead mrl,
        BlendAssessmentPolicyRead policies, CancellationToken ct)
    {
        try
        {
            // Stock/policy publishers share this lock. No lock remains held while the user chooses/loads.
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
            var actor = await ledger.ActorAsync(ct);
            var detail = await ReadExecutionAsync(actor, id, "detail", db, ct);
            var basis = !await access.HasPermissionAsync(Permissions.ResidueEvaluate, ct) ? JsonSerializer.SerializeToElement(new { phase = "LOADING", checkedAt = DateTimeOffset.UtcNow, assessmentStatus = "INCOMPLETE", findings = new[] { "RESIDUE_EVALUATE_PERMISSION_REQUIRED" } }) : await AssessContainerAsync(detail, detail.GetProperty("status").GetString() == "LOADING" &&
                detail.GetProperty("remainingKg").GetString() == "0.000000" ? "CLOSE" : "LOADING", db, ledger, residue, mrl, policies, ct);
            if (detail.GetProperty("status").GetString() != "CLOSED" && detail.GetProperty("approval").ValueKind == JsonValueKind.Object && InvalidatesActiveAuthority(detail, basis))
            {
                await InvalidateUnfinishedAuthorityAsync(actor, id, detail.GetProperty("version").GetInt32(), basis, db, ct);
                detail = await ReadExecutionAsync(actor, id, "detail", db, ct);
            }
            await tx.CommitAsync(ct);
            return Results.Ok(new { container = PublicContainer(detail), readiness = basis });
        }
        catch (PostgresException ex) { return ContainerDatabaseError(ex); }
        catch (LedgerException ex) { return Problem(ex.StatusCode, "CONTAINER_UNAVAILABLE", ex.Message); }
    }

    private static async Task<IResult> ContainerReadResultAsync(Guid id, string action,
        LedgerService ledger, AppDbContext db, CancellationToken ct)
    {
        try
        {
            var detail = await ReadExecutionAsync(await ledger.ActorAsync(ct), id, action, db, ct);
            return Results.Ok(detail);
        }
        catch (PostgresException ex) { return ContainerDatabaseError(ex); }
    }

    private static async Task<IResult> ContainerMutationAsync<T>(Guid id, string action, T request,
        HttpContext http, ICurrentAccess access, AppDbContext db, LedgerService ledger,
        ResidueEvaluationService residue, ApplicableMrlRead mrl, BlendAssessmentPolicyRead policies, CancellationToken ct)
    {
        var permission = action == "hold" ? Permissions.HoldsManage : Permissions.ContainersWrite;
        var scope = await ScopeAsync(http, db, access, permission, ct);
        if (scope is null) return Results.Forbid();
        var payload = JsonSerializer.SerializeToElement(request, ContainerJson);
        if (payload.GetProperty("operationId").GetGuid() == Guid.Empty || payload.GetProperty("version").GetInt32() < 1)
            return Problem(400, "CONTAINER_REQUEST_INVALID", "Operation ID and positive current version are required.");
        if (request is ActualLoadRequest load && (load.ProposalId == Guid.Empty || load.DecisionVersion < 1 ||
            load.LoadedAt == default || load.Lines is not { Length: > 0 and <= 100 } || load.Lines.Any(x => x is null ||
                x.LotId == Guid.Empty || x.LocationId == Guid.Empty || !ExportRules.TryMass(x.QuantityKg, "KG", out _))))
            return Problem(400, "CONTAINER_REQUEST_INVALID", "Provide approval, loading time and positive exact kg lines.");
        if (request is ContainerMetadataRequest metadata && (metadata.ContainerNumber?.Trim().Length > 80 || metadata.SealNumber?.Trim().Length > 80))
            return Problem(400, "CONTAINER_REQUEST_INVALID", "Container number and seal have maximum 80 characters.");
        if (request is ContainerHoldRequest hold && (string.IsNullOrWhiteSpace(hold.Reason) || hold.Reason.Length > 1000))
            return Problem(400, "CONTAINER_REQUEST_INVALID", "A HOLD reason of one to 1000 characters is required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
            await LockAsync(db, scope.Value.Company, ct);
            // Permission/company can have changed while waiting, including before replay.
            var current = await ScopeAsync(http, db, access, permission, ct);
            if (current != scope) return Results.Forbid();
            var role = action == "hold" ? Roles.CustomerManager : Roles.ExportStaff;
            if (!await db.Users.AsNoTracking().AnyAsync(x => x.Id == scope.Value.Actor && x.IsActive &&
                x.OrganizationId == scope.Value.Company && x.Role == role, ct)) return Results.Forbid();
            if (action is "load" or "close")
            {
                var replay = await ExecutionCommandAsync(scope.Value.Actor, id, action, payload, null, db, ct);
                if (!replay.TryGetProperty("replayMissing", out _))
                {
                    await tx.CommitAsync(ct);
                    return Results.Ok(PublicContainer(replay));
                }
            }
            JsonElement basis = JsonSerializer.SerializeToElement(new { });
            if (action is "load" or "close")
            {
                if (!await access.HasPermissionAsync(Permissions.ResidueEvaluate, ct)) return Results.Forbid();
                var detail = await ReadExecutionAsync(scope.Value.Actor, id, "detail", db, ct);
                basis = await AssessContainerAsync(detail, action == "load" ? "LOADING" : "CLOSE", db, ledger, residue, mrl, policies, ct);
                if (basis.GetProperty("assessmentStatus").GetString() != "PASS")
                {
                    if (detail.GetProperty("approval").ValueKind == JsonValueKind.Object && InvalidatesActiveAuthority(detail, basis))
                    {
                        await InvalidateUnfinishedAuthorityAsync(scope.Value.Actor, id, detail.GetProperty("version").GetInt32(), basis, db, ct);
                        await tx.CommitAsync(ct);
                    }
                    return Results.Json(new { code = "CONTAINER_BLOCKED", title = "Current container checks block this operation. Refresh checks to review the reasons.", readiness = basis }, statusCode: 409);
                }
            }
            var result = await ExecutionCommandAsync(scope.Value.Actor, id, action, payload, basis, db, ct);
            if (action is "load" or "close")
            {
                var finalDetail = await ReadExecutionAsync(scope.Value.Actor, id, "detail", db, ct);
                var final = await AssessContainerAsync(finalDetail, action == "load" ? "LOADING" : "CLOSE", db, ledger, residue, mrl, policies, ct, finalCheck: true);
                if (final.GetProperty("assessmentStatus").GetString() != "PASS" || final.GetProperty("dependencyRevision").GetString() != basis.GetProperty("dependencyRevision").GetString())
                    return Problem(409, "CONTAINER_EVIDENCE_CHANGED", "Current evidence changed before commit; the operation was rolled back.");
                await WorkflowSqlAsync(scope.Value.Actor, id, "assert-basis", EmptyContainerPayload, final, db, ct);
            }
            await tx.CommitAsync(ct);
            return Results.Ok(PublicContainer(result));
        }
        catch (PostgresException ex) { return ContainerDatabaseError(ex); }
        catch (LedgerException ex) { return Problem(ex.StatusCode, "CONTAINER_BLOCKED", ex.Message); }
    }

    private static object PublicContainer(JsonElement raw)
    {
        // Internal assessment inputs contain the original proposal snapshot. Do not duplicate them in UI reads/receipts.
        var data = System.Text.Json.Nodes.JsonNode.Parse(raw.GetRawText())!.AsObject();
        data.Remove("closingScopes");
        if (data["approval"] is System.Text.Json.Nodes.JsonObject approval)
        {
            approval.Remove("outputSnapshot"); approval.Remove("snapshot"); approval.Remove("previewRequest"); approval.Remove("demandSnapshot");
        }
        return data;
    }

    private static async Task<JsonElement> ReadExecutionAsync(Guid actor, Guid id, string action, AppDbContext db, CancellationToken ct) =>
        await ContainerSqlAsync("SELECT grainledger.ledger_export_container_read($1,$2,$3)::text", db, ct,
            new() { Value = actor }, new() { Value = id }, new() { Value = action });

    private static async Task<JsonElement> ExecutionCommandAsync(Guid actor, Guid id, string action,
        JsonElement payload, JsonElement? basis, AppDbContext db, CancellationToken ct) =>
        await ContainerSqlAsync("SELECT grainledger.ledger_export_container_command($1,$2,$3,$4,$5)::text", db, ct,
            new() { Value = actor }, new() { Value = id }, new() { Value = action }, new() { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = payload.GetRawText() },
            new() { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = basis?.GetRawText() ?? (object)DBNull.Value });

    private static async Task<JsonElement> ContainerSqlAsync(string sql, AppDbContext db, CancellationToken ct,
        params NpgsqlParameter[] parameters)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await db.Database.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection, (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction());
        command.Parameters.AddRange(parameters);
        return JsonSerializer.Deserialize<JsonElement>((string)(await command.ExecuteScalarAsync(ct))!);
    }

    private static IResult ContainerDatabaseError(PostgresException ex) => Problem(
        ex.SqlState == "42501" ? 403 : ex.SqlState is "42883" or "42P01" ? 503 :
        ex.SqlState.StartsWith("22", StringComparison.Ordinal) || ex.SqlState == "23502" ? 400 :
        ex.SqlState is "23514" or "23505" or "23503" or "40001" ? 409 : 500,
        "CONTAINER_" + (ex.SqlState is "42883" or "42P01" ? "SCHEMA_NOT_READY" : "CONFLICT"),
        ex.SqlState is "42883" or "42P01" ? "Container execution migration/runtime grants are not installed." : ex.SqlState is "42501" or "23514" or "23505" or "23503" or "40001" or "23502" || ex.SqlState.StartsWith("22", StringComparison.Ordinal) ? ex.MessageText : "Container execution failed. Retry the same operation after service recovery.");

    private static async Task<JsonElement> AssessContainerAsync(JsonElement detail, string phase, AppDbContext db,
        LedgerService ledger, ResidueEvaluationService residue, ApplicableMrlRead mrlReader,
        BlendAssessmentPolicyRead policyReader, CancellationToken ct, bool finalCheck = false)
    {
        var findings = new List<string>();
        if (!finalCheck && detail.GetProperty("status").GetString() == "CLOSED") findings.Add("CONTAINER_CLOSED");
        if (detail.GetProperty("holds").GetArrayLength() != 0) findings.Add("CONTAINER_HOLD");
        if (phase == "CLOSE")
        {
            if (detail.GetProperty("remainingKg").GetString() != "0.000000") findings.Add("EXACT_TARGET_NOT_LOADED");
            if (detail.GetProperty("containerNumber").ValueKind == JsonValueKind.Null || detail.GetProperty("sealNumber").ValueKind == JsonValueKind.Null)
                findings.Add("SEAL_METADATA_MISSING");
            if (detail.TryGetProperty("closingScopes", out _))
                return await AssessWholeContainerAsync(detail, findings, db, ledger, residue, mrlReader, policyReader, ct);
        }
        var approval = detail.GetProperty("approval");
        object? evidence = null; string? dependency = null;
        if (approval.ValueKind == JsonValueKind.Null) findings.Add("ACTIVE_APPROVAL_REQUIRED");
        else if (approval.TryGetProperty("scopeReviewDecision", out var scopeDecision) && scopeDecision.GetString() != "APPROVED") findings.Add("OUTPUT_REVIEW_REJECTED");
        else if (approval.GetProperty("mode").GetString() != "DIRECT_STOCK" && (!approval.TryGetProperty("outputSnapshot", out var actualOutput) || actualOutput.ValueKind == JsonValueKind.Null)) findings.Add(approval.TryGetProperty("productionState", out var productionState) ? productionState.GetString() switch
        {
            "PRODUCED" => "OUTPUT_REVIEW_REQUIRED", "REJECTED" => "OUTPUT_REVIEW_REJECTED",
            "REQUESTED" or "DRAFT" => "APPROVED_PRODUCTION_PENDING", _ => "BLEND_OUTPUT_HANDOFF_REQUIRED"
        } : "BLEND_OUTPUT_HANDOFF_REQUIRED");
        else
        {
            var snapshot = approval.TryGetProperty("outputSnapshot", out var reviewedOutput) && reviewedOutput.ValueKind != JsonValueKind.Null ? reviewedOutput : approval.GetProperty("snapshot");
            var saved = ReadSavedBlendSelection(snapshot.GetRawText());
            var demand = JsonDocument.Parse(approval.GetProperty("demandSnapshot").GetString()!);
            using (demand)
            {
                var order = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == detail.GetProperty("orderId").GetGuid(), ct);
                if (saved is null || (!approval.TryGetProperty("scopeReviewId", out _) && (demand.RootElement.GetProperty("orderVersion").GetInt32() != order.Version ||
                    demand.RootElement.GetProperty("specificationVersion").GetInt32() != order.CurrentSpecificationVersion)) ||
                    ParseKg((demand.RootElement.TryGetProperty("containerTargetMassKg", out var containerTarget) ? containerTarget.GetString() : demand.RootElement.GetProperty("targetMassKg").GetString())!) != ParseKg(detail.GetProperty("targetKg").GetString()!))
                    findings.Add("APPROVED_DEMAND_CHANGED");
                else
                {
                    var selection = saved.Value.Selection;
                    var request = JsonSerializer.Deserialize<PreviewBlendPlanRequest>(approval.GetProperty("previewRequest").GetRawText())!;
                    var policy = await policyReader.ResolveAsync(order.CompanyId, order.MarketId, order.CommodityId, null, ct);
                    if (policy is null) findings.Add("ASSESSMENT_POLICY_MISSING");
                    else
                    {
                        var selected = selection.Allocations.Select(x => new CandidateGroupItem(x.LotId, x.LocationId, x.QuantityKg, x.CandidateRevision)).ToArray();
                        var assessment = await AssessWorkflowSelectionAsync(order, request.FacilityId, selected, "DIRECT_STOCK", selected.Sum(x => x.QuantityKg), null,
                            db, ledger, residue, mrlReader, policyReader, ct);
                        findings.AddRange(assessment.GetProperty("findings").EnumerateArray().Select(x => x.GetString()!));
                        dependency = assessment.GetProperty("dependencyRevision").GetString();
                        if (dependency != saved.Value.DependencyRevision) findings.Add("APPROVAL_EVIDENCE_CHANGED");
                        evidence = assessment.GetProperty("evidence");
                    }
                    // Only immutable approved allocations are loadable, including their reserved rather than free stock.
                    foreach (var item in approval.GetProperty("allocations").EnumerateArray())
                    {
                        if (item.GetProperty("restricted").GetBoolean()) findings.Add("LOT_RESTRICTED");
                        if (phase == "LOADING" && decimal.Parse(item.GetProperty("remainingKg").GetString()!, CultureInfo.InvariantCulture) > 0 &&
                            !item.GetProperty("loadingPositionReady").GetBoolean()) findings.Add("POSITION_OR_SOURCE_HANDOFF_REQUIRED");
                        if (phase == "LOADING" && ParseKg(item.GetProperty("remainingKg").GetString()!) > 0 && !selection.Allocations.Any(x => x.LotId == item.GetProperty("lotId").GetGuid() && x.LocationId == item.GetProperty("locationId").GetGuid()))
                            findings.Add("OUTPUT_REVIEW_REQUIRED");
                    }
                    if (!finalCheck && phase == "LOADING" && detail.GetProperty("remainingKg").GetString() == "0.000000") findings.Add("TARGET_ALREADY_LOADED");
                }
            }
        }
        return JsonSerializer.SerializeToElement(new
        {
            phase,
            checkedAt = DateTimeOffset.UtcNow,
            assessmentStatus = findings.Count == 0 ? "PASS" : "INCOMPLETE",
            findings = findings.Distinct().ToArray(),
            dependencyRevision = dependency,
            evidence,
            planAuthorization = approval.ValueKind == JsonValueKind.Object ? new { proposalId = approval.GetProperty("proposalId").GetGuid(), decisionVersion = approval.GetProperty("decisionVersion").GetInt32(),
                reviewId = approval.TryGetProperty("scopeReviewId", out var scopeReview) ? scopeReview : JsonSerializer.SerializeToElement((Guid?)null),
                productionOperations = approval.TryGetProperty("productionOperations", out var operations) ? operations : JsonSerializer.SerializeToElement(Array.Empty<Guid>()) } : null,
            outputAuthorization = approval.ValueKind == JsonValueKind.Object && approval.TryGetProperty("outputReviewId", out var outputReview) && outputReview.ValueKind != JsonValueKind.Null
                ? new { proposalId = approval.GetProperty("proposalId").GetGuid(), reviewId = outputReview.GetGuid(), operationId = approval.TryGetProperty("productionOperationId", out var productionOperation) && productionOperation.ValueKind == JsonValueKind.String && productionOperation.TryGetGuid(out var operation) ? (Guid?)operation : null } : null,
            calculationVersion = "F16_DIRECT_STOCK_V1"
        }, ContainerJson);
    }
}
