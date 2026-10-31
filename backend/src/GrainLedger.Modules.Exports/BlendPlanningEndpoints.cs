using System.Text.Json;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GrainLedger.Modules.Exports;

public static partial class ExportModule
{
    private sealed class PlanningReadyFilter(ExportDbContext db) : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
            await PlanningReadyAsync(db, context.HttpContext.RequestAborted)
                ? await next(context)
                : Problem(503, "PLANNING_SCHEMA_NOT_READY", "Blend plan storage is not deployed yet.");
    }

    private static Task<bool> PlanningReadyAsync(ExportDbContext db, CancellationToken ct) =>
        db.Database.SqlQueryRaw<bool>("""
            SELECT COALESCE(bool_and(to_regclass('exports.' || name) IS NOT NULL
                AND has_table_privilege(current_user, to_regclass('exports.' || name), 'SELECT,INSERT,UPDATE')), false) AS "Value"
            FROM (VALUES ('blend_plan_drafts'), ('blend_plan_events')) AS required(name)
            """).SingleAsync(ct);

    private static void MapBlendPlanning(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/export-containers/{id:guid}/planning-context", PlanningContextAsync)
            .RequireAuthorization(Permissions.ExportOrdersRead).AddEndpointFilter<NoStoreFilter>()
            .AddEndpointFilter<ReadyFilter>();
        endpoints.MapGet("/api/export-containers/{id:guid}/stock-preview", StockPreviewAsync)
            .RequireAuthorization(Permissions.ExportOrdersRead).AddEndpointFilter<NoStoreFilter>()
            .AddEndpointFilter<ReadyFilter>();
        endpoints.MapGet("/api/export-containers/{id:guid}/inventory-scope", InventoryScopeAsync)
            .RequireAuthorization(Permissions.ExportOrdersRead).AddEndpointFilter<NoStoreFilter>()
            .AddEndpointFilter<ReadyFilter>();
        endpoints.MapGet("/api/export-containers/{id:guid}/candidates", CandidatesAsync)
            .RequireAuthorization(Permissions.ExportOrdersRead).AddEndpointFilter<NoStoreFilter>()
            .AddEndpointFilter<ReadyFilter>().WithTags("Blend planning")
            .WithDescription("Requires facilityId; optional includeExcluded, page, pageSize, scopeRevision and planId. A current draft's planId adds product-match hints. Physical inventory and hints do not assess compliance or authorize a proposal.");
        endpoints.MapGet("/api/export-containers/{id:guid}/candidates/{lotId:guid}/lineage", CandidateLineageAsync)
            .RequireAuthorization(Permissions.ExportOrdersRead).AddEndpointFilter<NoStoreFilter>()
            .AddEndpointFilter<ReadyFilter>().WithTags("Blend planning")
            .WithDescription("Requires facilityId and locationId; optional candidateRevision returns 409 if stale.");
        endpoints.MapGet("/api/export-containers/{id:guid}/candidates/{lotId:guid}/residue-preview", CandidateResiduePreviewAsync)
            .RequireAuthorization(Permissions.ResidueEvaluate).AddEndpointFilter<NoStoreFilter>()
            .AddEndpointFilter<ReadyFilter>().WithTags("Blend planning")
            .WithDescription("Requires facilityId, locationId, candidateRevision and substanceIds. Returns a read-only F12 lot interval, not a market assessment or plan.");
        var containers = endpoints.MapGroup("/api/export-containers/{id:guid}/blend-plans")
            .RequireAuthorization().AddEndpointFilter<NoStoreFilter>()
            .AddEndpointFilter<MutationAntiforgeryFilter>().AddEndpointFilter<ReadyFilter>()
            .AddEndpointFilter<PlanningReadyFilter>().WithTags("Blend planning");
        containers.MapGet("", ListPlanDraftsAsync).RequireAuthorization(Permissions.ExportOrdersRead);
        containers.MapPost("/drafts", CreatePlanDraftAsync).RequireAuthorization(Permissions.BlendPropose);
        var plans = endpoints.MapGroup("/api/blend-plans/{id:guid}").RequireAuthorization()
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>()
            .AddEndpointFilter<ReadyFilter>().AddEndpointFilter<PlanningReadyFilter>().WithTags("Blend planning");
        plans.MapGet("", GetPlanDraftAsync).RequireAuthorization(Permissions.ExportOrdersRead);
        plans.MapGet("/history", PlanHistoryAsync).RequireAuthorization(Permissions.ExportOrdersRead);
        plans.MapPost("/residue-preview", PlanResiduePreviewAsync).RequireAuthorization(Permissions.ResidueEvaluate)
            .WithDescription("Checks selected candidate positions and reads F12 intervals for all lots at one residue snapshot. This does not assess MRL or save a plan.");
        plans.MapPost("/preview", PreviewBlendPlanAsync).RequireAuthorization(Permissions.BlendPropose)
            .WithDescription("Runs bounded deterministic selection and fail-closed assessment. A PASS result receives a short-lived server-owned reference for proposal.");
        plans.MapPost("/propose", ProposeBlendPlanAsync).RequireAuthorization(Permissions.BlendPropose);
        plans.MapGet("/proposal", GetBlendPlanProposalAsync).RequireAuthorization(Permissions.ExportOrdersRead);
        plans.MapPost("/cancel", CancelPlanDraftAsync).RequireAuthorization(Permissions.BlendPropose);
    }

    private static string? TargetProductFromSnapshot(string snapshot)
    {
        try
        {
            using var document = JsonDocument.Parse(snapshot);
            return document.RootElement.TryGetProperty("targetProductIdentity", out var target) &&
                target.ValueKind == JsonValueKind.String ? target.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static BlendPlanDraftDto PlanDto(BlendPlanDraft p, bool canWrite, bool demandCurrent,
        bool proposed = false) => new(p.Id, p.OrderId,
        p.ContainerId, p.SpecificationId, p.VersionNo, p.Version, p.Status, ExportRules.Text(p.TargetMassKg),
        TargetProductFromSnapshot(p.DemandSnapshot), p.DemandSnapshot, p.CreatedAt, p.CancelledAt,
        demandCurrent, canWrite && p.Status == "DRAFT" && !proposed, proposed);

    private static bool DemandIsCurrent(BlendPlanDraft plan, ExportOrder order, ExportContainer container)
    {
        // A draft is a historical snapshot, not a lock on F13. Staleness is derived
        // from the current versions; a malformed legacy snapshot cannot be current.
        try
        {
            using var document = JsonDocument.Parse(plan.DemandSnapshot);
            var demand = document.RootElement;
            return demand.GetProperty("orderVersion").GetInt32() == order.Version &&
                demand.GetProperty("containerVersion").GetInt32() == container.Version &&
                plan.SpecificationId == (Guid)demand.GetProperty("specificationId").GetGuid() &&
                plan.TargetMassKg == container.TargetMassKg &&
                demand.GetProperty("specificationVersion").GetInt32() == order.CurrentSpecificationVersion;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private static async Task<IResult> PlanningContextAsync(Guid id, HttpContext http,
        IdentityDbContext identity, ICurrentAccess access, ExportDbContext db, CatalogDbContext catalog,
        CancellationToken ct)
    {
        var scope = await ScopeAsync(http, identity, access, Permissions.ExportOrdersRead, ct);
        if (scope is null) return Results.Forbid();
        var container = await db.Containers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.CompanyId == scope.Value.Company, ct);
        if (container is null) return Results.NotFound();
        var order = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == container.OrderId, ct);
        var commodityCode = await catalog.Commodities.AsNoTracking().Where(x => x.Id == order.CommodityId)
            .Select(x => x.Code).SingleAsync(ct);
        var allowedProducts = ExportTargetProducts.ForCommodity(commodityCode);
        var ready = await PlanningReadyAsync(db, ct);
        var candidateReady = await db.Database.SqlQueryRaw<bool>("""
            SELECT COALESCE(has_function_privilege(current_user,
                to_regprocedure('ledger.planning_candidates(uuid,uuid,uuid)'), 'EXECUTE'), false) AS "Value"
            """).SingleAsync(ct);
        var compatibilityReady = await db.Database.SqlQueryRaw<bool>("""
            SELECT COALESCE(has_function_privilege(current_user,
                to_regprocedure('ledger.planning_compatibilities(uuid,uuid,uuid)'), 'EXECUTE'), false) AS "Value"
            """).SingleAsync(ct);
        var dependencies = new List<PlanningDependency>();
        if (!candidateReady) dependencies.Add(new("CANDIDATE_READ_NOT_READY",
            "Apply the reviewed Ledger candidate migration and restricted runtime grant."));
        if (!compatibilityReady) dependencies.Add(new("COMPATIBILITY_READ_NOT_READY",
            "Apply the reviewed Ledger compatibility migration and restricted runtime grant."));
        var qualityReady = await db.Database.SqlQueryRaw<bool>("""
            SELECT COALESCE(has_function_privilege(current_user,
                to_regprocedure('ledger.planning_quality_observations(uuid,uuid,uuid,uuid[],timestamptz)'), 'EXECUTE'), false) AS "Value"
            """).SingleAsync(ct);
        if (!qualityReady) dependencies.Add(new("QUALITY_EVIDENCE_NOT_READY",
            "Apply the reviewed finished-lot quality observation migration and restricted runtime grant."));
        if (!await ProposalReadyAsync(db, ct)) dependencies.Add(new("PROPOSAL_STORAGE_NOT_READY",
            "Apply the reviewed Export proposal migration and runtime grants."));
        return Results.Ok(new BlendPlanningContextDto(id, order.Version, container.Version, ready,
            ready && allowedProducts.Count > 0 && container.Status == "PLANNED" &&
            await access.HasPermissionAsync(Permissions.BlendPropose, ct),
            candidateReady, compatibilityReady, false, dependencies, allowedProducts));
    }

    private static async Task<IResult> CreatePlanDraftAsync(Guid id, CreateBlendPlanDraftRequest request,
        HttpContext http, IdentityDbContext identity, ICurrentAccess access, ExportDbContext db,
        CatalogDbContext catalog,
        TimeProvider clock, CancellationToken ct)
    {
        var scope = await ScopeAsync(http, identity, access, Permissions.BlendPropose, ct);
        if (scope is null) return Results.Forbid();
        var errors = new Dictionary<string, string[]>();
        if (request.OperationId == Guid.Empty || request.OrderVersion <= 0 || request.ContainerVersion <= 0)
            errors["request"] = ["Provide an operation ID and positive order/container versions."];
        if (request.TargetProductIdentity is not ("RAW_PADDY" or "DRIED_PADDY" or "HEAD_RICE" or
            "BROKEN_RICE" or "BRAN" or "HUSK" or "BLENDED_RICE"))
            errors["targetProductIdentity"] = ["Choose a supported Ledger product identity."];
        if (errors.Count > 0) return Invalid(errors);
        var hash = ExportRules.Hash("plan-draft:" + id, request);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(db, scope.Value.Company, ct);
        var prior = await db.Operations.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == scope.Value.Company &&
            x.ActorId == scope.Value.Actor && x.OperationId == request.OperationId, ct);
        if (prior is not null) return ReplayPlan(prior, hash, http);
        var container = await db.Containers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.CompanyId == scope.Value.Company, ct);
        if (container is null) return Results.NotFound();
        var order = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == container.OrderId, ct);
        if (request.ContainerVersion != container.Version || request.OrderVersion != order.Version)
            return Problem(412, "EXPORT_VERSION_STALE", "Reload the order and container before creating a plan.");
        if (container.Status != "PLANNED") return Problem(409, "CONTAINER_IN_USE", "Only planned containers accept a draft.");
        var commodityCode = await catalog.Commodities.AsNoTracking().Where(x => x.Id == order.CommodityId)
            .Select(x => x.Code).SingleAsync(ct);
        if (!ExportTargetProducts.ForCommodity(commodityCode).Contains(request.TargetProductIdentity))
            return Invalid(new() { ["targetProductIdentity"] = ["Choose a product compatible with the order commodity."] });
        var spec = await db.Specifications.AsNoTracking().SingleAsync(x => x.OrderId == order.Id &&
            x.VersionNo == order.CurrentSpecificationVersion, ct);
        var limits = await db.Limits.AsNoTracking().Where(x => x.SpecificationId == spec.Id)
            .OrderBy(x => x.AttributeCode).ToListAsync(ct);
        var version = (await db.BlendPlanDrafts.Where(x => x.ContainerId == id).MaxAsync(x => (int?)x.VersionNo, ct) ?? 0) + 1;
        var now = clock.GetUtcNow();
        var plan = new BlendPlanDraft
        {
            Id = Guid.NewGuid(), CompanyId = scope.Value.Company, OrderId = order.Id, ContainerId = id,
            SpecificationId = spec.Id, VersionNo = version, TargetMassKg = container.TargetMassKg,
            CreatedBy = scope.Value.Actor, CreatedAt = now,
            DemandSnapshot = JsonSerializer.Serialize(new
            {
                orderId = order.Id, orderVersion = order.Version, marketId = order.MarketId, commodityId = order.CommodityId,
                shippingFrom = order.ShippingFrom, shippingTo = order.ShippingTo, containerId = id,
                containerVersion = container.Version, targetMassKg = ExportRules.Text(container.TargetMassKg),
                targetProductIdentity = request.TargetProductIdentity,
                specificationId = spec.Id, specificationVersion = spec.VersionNo, limits = limits.Select(ToDto).ToArray()
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
        db.BlendPlanDrafts.Add(plan);
        await db.SaveChangesAsync(ct);
        PlanEvent(db, plan, scope.Value.Actor, "DRAFT_CREATED", now);
        Event(db, order, scope.Value.Actor, "BLEND_PLAN_DRAFT_CREATED", new { planId = plan.Id, containerId = id, versionNo = version }, now);
        await db.SaveChangesAsync(ct);
        var response = PlanDto(plan, true, true);
        await StoreResultAsync(db, new ExportOperation { Id = Guid.NewGuid(), CompanyId = scope.Value.Company,
            ActorId = scope.Value.Actor, OperationId = request.OperationId, RequestHash = hash,
            OrderId = order.Id, ContainerId = id }, response, 201, plan.Version, ct);
        await transaction.CommitAsync(ct);
        SetEtag(http, plan.Version);
        return Results.Created($"/api/blend-plans/{plan.Id}", response);
    }

    private static IResult ReplayPlan(ExportOperation prior, string hash, HttpContext http)
    {
        var result = Replay(prior, hash, http);
        if (prior.RequestHash == hash && prior.ResponseStatus == 201)
            http.Response.Headers.Location = $"/api/blend-plans/{JsonSerializer.Deserialize<JsonElement>(prior.ResponseJson).GetProperty("id").GetGuid()}";
        return result;
    }

    private static void PlanEvent(ExportDbContext db, BlendPlanDraft plan, Guid actor, string action, DateTimeOffset now) =>
        db.BlendPlanEvents.Add(new BlendPlanEvent { Id = Guid.NewGuid(), PlanId = plan.Id, CompanyId = plan.CompanyId,
            ActorId = actor, Action = action, RecordedAt = now });

    private static async Task<IResult> ListPlanDraftsAsync(Guid id, HttpContext http, IdentityDbContext identity,
        ICurrentAccess access, ExportDbContext db, CancellationToken ct)
    {
        var scope = await ScopeAsync(http, identity, access, Permissions.ExportOrdersRead, ct);
        if (scope is null) return Results.Forbid();
        if (!await db.Containers.AnyAsync(x => x.Id == id && x.CompanyId == scope.Value.Company, ct)) return Results.NotFound();
        if (!Page(http.Request.Query["page"], http.Request.Query["pageSize"], out var page, out var size))
            return Invalid(new() { ["query"] = ["Invalid pagination."] });
        var query = db.BlendPlanDrafts.AsNoTracking().Where(x => x.ContainerId == id && x.CompanyId == scope.Value.Company);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.VersionNo).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var canWrite = await access.HasPermissionAsync(Permissions.BlendPropose, ct);
        var rowIds = rows.Select(x => x.Id).ToArray();
        var proposedIds = await ProposalReadyAsync(db, ct)
            ? (await db.BlendPlanProposals.AsNoTracking().Where(x => x.CompanyId == scope.Value.Company &&
                rowIds.Contains(x.PlanId)).Select(x => x.PlanId).ToArrayAsync(ct)).ToHashSet()
            : new HashSet<Guid>();
        var container = await db.Containers.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        var order = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == container.OrderId, ct);
        return Results.Ok(new ExportPage<BlendPlanDraftDto>(rows.Select(x => PlanDto(x, canWrite,
            DemandIsCurrent(x, order, container), proposedIds.Contains(x.Id))).ToArray(), page, size, total, (long)page * size < total));
    }

    private static async Task<IResult> GetPlanDraftAsync(Guid id, HttpContext http, IdentityDbContext identity,
        ICurrentAccess access, ExportDbContext db, CancellationToken ct)
    {
        var scope = await ScopeAsync(http, identity, access, Permissions.ExportOrdersRead, ct);
        if (scope is null) return Results.Forbid();
        var plan = await db.BlendPlanDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.CompanyId == scope.Value.Company, ct);
        if (plan is null) return Results.NotFound();
        var container = await db.Containers.AsNoTracking().SingleAsync(x => x.Id == plan.ContainerId, ct);
        var order = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == plan.OrderId, ct);
        SetEtag(http, plan.Version);
        var proposed = await ProposalReadyAsync(db, ct) && await db.BlendPlanProposals.AsNoTracking()
            .AnyAsync(x => x.PlanId == id && x.CompanyId == scope.Value.Company, ct);
        return Results.Ok(PlanDto(plan, await access.HasPermissionAsync(Permissions.BlendPropose, ct),
            DemandIsCurrent(plan, order, container), proposed));
    }

    private static async Task<IResult> CancelPlanDraftAsync(Guid id, CancelBlendPlanDraftRequest request,
        HttpContext http, IdentityDbContext identity, ICurrentAccess access, ExportDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var scope = await ScopeAsync(http, identity, access, Permissions.BlendPropose, ct);
        if (scope is null) return Results.Forbid();
        if (request.OperationId == Guid.Empty) return Invalid(new() { ["operationId"] = ["Provide a stable operation ID."] });
        var hash = ExportRules.Hash("cancel-plan-draft:" + id, request);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(db, scope.Value.Company, ct);
        var prior = await db.Operations.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == scope.Value.Company &&
            x.ActorId == scope.Value.Actor && x.OperationId == request.OperationId, ct);
        if (prior is not null) return ReplayPlan(prior, hash, http);
        var plan = await db.BlendPlanDrafts.SingleOrDefaultAsync(x => x.Id == id && x.CompanyId == scope.Value.Company, ct);
        if (plan is null) return Results.NotFound();
        if (!Matches(http, plan.Version)) return Problem(412, "PLAN_VERSION_STALE", "Reload the plan before cancelling.");
        if (plan.Status != "DRAFT") return Problem(409, "PLAN_NOT_DRAFT", "Only an active draft can be cancelled.");
        if (await ProposalReadyAsync(db, ct) && await db.BlendPlanProposals.AnyAsync(x =>
            x.PlanId == id && x.CompanyId == scope.Value.Company, ct))
            return Problem(409, "PLAN_ALREADY_PROPOSED", "A proposed plan cannot be cancelled as a draft.");
        plan.Status = "CANCELLED"; plan.Version++; plan.CancelledBy = scope.Value.Actor; plan.CancelledAt = clock.GetUtcNow();
        PlanEvent(db, plan, scope.Value.Actor, "DRAFT_CANCELLED", plan.CancelledAt.Value);
        var order = await db.Orders.SingleAsync(x => x.Id == plan.OrderId, ct);
        var container = await db.Containers.AsNoTracking().SingleAsync(x => x.Id == plan.ContainerId, ct);
        Event(db, order, scope.Value.Actor, "BLEND_PLAN_DRAFT_CANCELLED", new { planId = id }, plan.CancelledAt.Value);
        await db.SaveChangesAsync(ct);
        var response = PlanDto(plan, true, DemandIsCurrent(plan, order, container));
        await StoreResultAsync(db, new ExportOperation { Id = Guid.NewGuid(), CompanyId = scope.Value.Company,
            ActorId = scope.Value.Actor, OperationId = request.OperationId, RequestHash = hash,
            OrderId = plan.OrderId, ContainerId = plan.ContainerId }, response, 200, plan.Version, ct);
        await transaction.CommitAsync(ct);
        SetEtag(http, plan.Version);
        return Results.Ok(response);
    }

    private static async Task<IResult> PlanHistoryAsync(Guid id, HttpContext http, IdentityDbContext identity,
        ICurrentAccess access, ExportDbContext db, CancellationToken ct)
    {
        var scope = await ScopeAsync(http, identity, access, Permissions.ExportOrdersRead, ct);
        if (scope is null) return Results.Forbid();
        if (!await db.BlendPlanDrafts.AnyAsync(x => x.Id == id && x.CompanyId == scope.Value.Company, ct)) return Results.NotFound();
        if (!Page(http.Request.Query["page"], http.Request.Query["pageSize"], out var page, out var size))
            return Invalid(new() { ["query"] = ["Invalid pagination."] });
        var query = db.BlendPlanEvents.AsNoTracking().Where(x => x.PlanId == id && x.CompanyId == scope.Value.Company);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.RecordedAt).ThenBy(x => x.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var actors = rows.Select(x => x.ActorId).Distinct().ToArray();
        var names = await identity.Users.AsNoTracking().Where(x => actors.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        return Results.Ok(new ExportPage<ExportEventDto>(rows.Select(x => new ExportEventDto(x.Action, x.RecordedAt,
            JsonSerializer.Serialize(new { planId = id }), names.GetValueOrDefault(x.ActorId, "Unknown user"))).ToArray(), page, size, total, (long)page * size < total));
    }
}
