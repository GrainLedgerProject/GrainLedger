using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GrainLedger.Modules.Capture;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;

namespace GrainLedger.Modules.Ledger;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResidueEvaluationRequest(Guid Id, Guid[] SubstanceIds);
public sealed record ResidueEvaluation(Guid Id, Guid LotId, Guid CompanyId, DateTimeOffset SnapshotAt,
    DateTimeOffset? PersistedAt, string EngineVersion, IReadOnlyList<ResidueResult> Results);
public sealed record ResidueEvaluationPreview(Guid LotId, Guid CompanyId, DateTimeOffset SnapshotAt,
    string EngineVersion, IReadOnlyList<ResidueResult> Results);
public sealed record HypotheticalBlendInput(Guid LotId, string QuantityKg);
public sealed record HypotheticalBlendPreview(DateTimeOffset SnapshotAt, string EngineVersion,
    IReadOnlyList<ResidueResult> Results);
public sealed record ResidueSelectionPreview(DateTimeOffset SnapshotAt,
    IReadOnlyList<ResidueEvaluationPreview> Inputs, HypotheticalBlendPreview? HypotheticalBlend);

public sealed partial class ResidueEvaluationService(IConfiguration configuration, LedgerService ledger,
    IResiduePolicyReader policyReader, IResidueEvidenceReader evidenceReader)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<JsonElement> EvaluateAsync(Guid lotId, ResidueEvaluationRequest request, CancellationToken ct)
    {
        if (request.Id == Guid.Empty || request.SubstanceIds is null || request.SubstanceIds.Length is < 1 or > 200
            || request.SubstanceIds.Contains(Guid.Empty) || request.SubstanceIds.Distinct().Count() != request.SubstanceIds.Length)
            throw new LedgerException(400, "Evaluation requires nonempty ID and 1–200 distinct substance IDs");
        var substances = request.SubstanceIds.Order().ToArray();
        var canonical = JsonSerializer.SerializeToElement(new { substanceIds = substances }, Json);
        var actor = await ledger.ActorAsync(ct);
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("GrainLedger"));
        await connection.OpenAsync(ct);
        DateTimeOffset snapshotAt;
        (ResidueNode[] Nodes, ResiduePolicySnapshot Policies, ResidueEvidenceSnapshot Evidence) materialized;
        await using (var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
        {
            await ExecuteAsync(connection, tx, "SET TRANSACTION READ ONLY", ct);
            var captured = await ExecuteAsync(connection, tx, "SELECT clock_timestamp()", ct);
            snapshotAt = new DateTimeOffset((DateTime)captured!);
            var replay = await ExecuteAsync(connection, tx, "SELECT ledger.residue_replay($1,$2,$3,$4)::text", ct,
                actor, lotId, request.Id, canonical);
            if (replay is string old) { await tx.CommitAsync(ct); return Parse(old); }
            materialized = await ReadSnapshotAsync(connection, tx, actor, lotId, substances, snapshotAt, ct);
            await tx.CommitAsync(ct);
        }
        var results = CalculateResults(lotId, substances, materialized.Nodes, materialized.Evidence, materialized.Policies, snapshotAt);
        var snapshot = new ResidueEvaluation(request.Id, lotId, materialized.Nodes.Single(x => x.LotId == lotId).CompanyId,
            snapshotAt, null, ResidueCalculator.EngineVersion, results);
        await using var persist = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var appended = (string)(await ExecuteAsync(connection, persist,
            "SELECT ledger.residue_append($1,$2,$3,$4,$5)::text", ct, actor, lotId, request.Id,
            JsonSerializer.SerializeToElement(new { substanceIds = substances }, Json),
            JsonSerializer.SerializeToElement(snapshot, Json)))!;
        await persist.CommitAsync(ct);
        return Parse(appended);
    }

    public async Task<ResidueEvaluationPreview> PreviewLotAsync(Guid lotId, string? substanceIds, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(substanceIds))
            throw new LedgerException(400, "Preview requires 1–200 distinct substance IDs");
        var values = substanceIds.Split(',', StringSplitOptions.TrimEntries);
        if (values.Length is < 1 or > 200 || values.Any(value => !Guid.TryParse(value, out _)))
            throw new LedgerException(400, "Preview requires 1–200 distinct substance IDs");
        var substances = values.Select(value => Guid.Parse(value)).ToArray();
        if (substances.Contains(Guid.Empty) || substances.Distinct().Count() != substances.Length)
            throw new LedgerException(400, "Preview requires 1–200 distinct substance IDs");
        return (await PreviewLotsAsync([lotId], substances, ct))[0];
    }

    // F14 can read several selected lots at one database snapshot. Each graph
    // remains owner-scoped by Ledger; this method does not append evaluations.
    public async Task<IReadOnlyList<ResidueEvaluationPreview>> PreviewLotsAsync(
        IReadOnlyList<Guid> lotIds, IReadOnlyList<Guid> substanceIds, CancellationToken ct)
    {
        return (await PreviewSelectionAsync(lotIds.Select(x => new HypotheticalBlendInput(x, "1")).ToArray(),
            null, substanceIds, ct)).Inputs;
    }

    // All input bounds and the optional in-memory BLENDING output share one
    // repeatable-read snapshot and one evidence/policy selection.
    public async Task<ResidueSelectionPreview> PreviewSelectionAsync(
        IReadOnlyList<HypotheticalBlendInput> inputs, Guid? outputLedgerCommodityId,
        IReadOnlyList<Guid> substanceIds, CancellationToken ct)
    {
        if (inputs.Count is < 1 or > 100 || outputLedgerCommodityId == Guid.Empty ||
            inputs.Any(x => x.LotId == Guid.Empty) || inputs.Select(x => x.LotId).Distinct().Count() != inputs.Count ||
            substanceIds.Count is < 1 or > 200 || substanceIds.Contains(Guid.Empty) ||
            substanceIds.Distinct().Count() != substanceIds.Count)
            throw new LedgerException(400, "Preview requires distinct inputs and substances, and a valid output commodity when blending");
        foreach (var input in inputs)
        {
            try
            {
                if (ResidueRational.Parse(input.QuantityKg).CompareTo(ResidueRational.Zero) <= 0)
                    throw new LedgerException(400, "Positive exact input kg required");
            }
            catch (FormatException) { throw new LedgerException(400, "Positive exact input kg required"); }
        }
        var actor = await ledger.ActorAsync(ct);
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("GrainLedger"));
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await ExecuteAsync(connection, tx, "SET TRANSACTION READ ONLY", ct);
        var captured = await ExecuteAsync(connection, tx, "SELECT clock_timestamp()", ct);
        var at = new DateTimeOffset((DateTime)captured!);
        var nodes = new Dictionary<Guid, ResidueNode>();
        Guid? company = null;
        foreach (var input in inputs)
        {
            var graph = (string)(await ExecuteAsync(connection, tx,
                "SELECT ledger.residue_graph($1,$2)::text", ct, actor, input.LotId))!;
            var graphNodes = JsonSerializer.Deserialize<ResidueNode[]>(graph, Json)!;
            var root = graphNodes.Single(x => x.LotId == input.LotId);
            if (company is not null && root.CompanyId != company)
                throw new LedgerException(403, "Blend inputs must have one owner");
            company = root.CompanyId;
            foreach (var node in graphNodes) nodes.TryAdd(node.LotId, node);
        }
        Guid? syntheticId = null;
        if (outputLedgerCommodityId is { } output)
        {
            // residue_graph exposes Catalog commodity IDs for physical nodes.
            // The hypothetical output must use that same identity so approved
            // processing factors match both physical and synthetic nodes.
            var catalogOutput = await ExecuteAsync(connection, tx,
                "SELECT catalog_commodity_id FROM ledger.catalog_commodities WHERE ledger_commodity_id=$1",
                ct, output);
            if (catalogOutput is not Guid catalogCommodityId)
                throw new LedgerException(422, "Hypothetical blend output has no Catalog commodity mapping");
            // A transient random ID would change the F12 dependency graph on
            // every read and make an otherwise identical F14 preview stale.
            var syntheticKey = output.ToString("N") + ":" + string.Join(";",
                inputs.OrderBy(x => x.LotId).Select(x =>
                    $"{x.LotId:N}:{x.QuantityKg}"));
            syntheticId = new Guid(SHA256.HashData(
                Encoding.UTF8.GetBytes(syntheticKey)).AsSpan(0, 16));
            if (nodes.ContainsKey(syntheticId.Value))
                throw new LedgerException(409, "Hypothetical blend identity collides with a physical lot");
            nodes.Add(syntheticId.Value, new ResidueNode(syntheticId.Value, company!.Value,
                catalogCommodityId, "BLENDED_RICE", "BLENDED_RICE", at,
                "BLENDING", null, null, inputs.Select(x => new ResidueParent(x.LotId, x.QuantityKg)).ToArray()));
        }
        var substances = substanceIds.ToArray();
        var owner = company ?? throw new LedgerException(400, "Preview needs at least one owned lot");
        var policies = await policyReader.ReadAsync(connection, tx, owner, substances, at, ct);
        var evidence = await evidenceReader.ReadAsync(connection, tx, owner,
            nodes.Values.Where(x => x.Kind == "SOURCE").Select(x => x.LotId).Distinct().ToArray(),
            substances, at, ct);
        await tx.CommitAsync(ct);
        var snapshotNodes = nodes.Values.ToArray();
        var previews = inputs.Select(x => new ResidueEvaluationPreview(x.LotId, owner, at,
            ResidueCalculator.EngineVersion,
            CalculateResults(x.LotId, substances, snapshotNodes, evidence, policies, at))).ToArray();
        var hypothetical = syntheticId is { } id
            ? new HypotheticalBlendPreview(at, ResidueCalculator.EngineVersion,
                CalculateResults(id, substances, snapshotNodes, evidence, policies, at)) : null;
        return new ResidueSelectionPreview(at, previews, hypothetical);
    }

    private async Task<(ResidueNode[] Nodes, ResiduePolicySnapshot Policies, ResidueEvidenceSnapshot Evidence)> ReadSnapshotAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx, Guid actor, Guid lotId, Guid[] substances,
        DateTimeOffset snapshotAt, CancellationToken ct)
    {
        var graph = (string)(await ExecuteAsync(connection, tx, "SELECT ledger.residue_graph($1,$2)::text", ct, actor, lotId))!;
        var nodes = JsonSerializer.Deserialize<ResidueNode[]>(graph, Json)!;
        var target = nodes.Single(x => x.LotId == lotId);
        var policies = await policyReader.ReadAsync(connection, tx, target.CompanyId, substances, snapshotAt, ct);
        var evidence = await evidenceReader.ReadAsync(connection, tx, target.CompanyId,
            nodes.Where(x => x.Kind == "SOURCE").Select(x => x.LotId).ToArray(), substances, snapshotAt, ct);
        return (nodes, policies, evidence);
    }

    private static IReadOnlyList<ResidueResult> CalculateResults(Guid lotId, Guid[] substances,
        IReadOnlyList<ResidueNode> nodes, ResidueEvidenceSnapshot evidence, ResiduePolicySnapshot policies, DateTimeOffset snapshotAt)
    {
        var sources = ResidueEvidenceResolver.Resolve(nodes, substances, evidence, policies, snapshotAt);
        return ResidueCalculator.Calculate(lotId, substances, nodes, sources,
            ResidueEvidenceResolver.Factors(policies.Rules), policies);
    }

    public async Task<ResidueSourceBound> PreviewAsync(ResidueReportSelection request, CancellationToken ct)
    {
        if (request.Id == Guid.Empty || request.CompanyId == Guid.Empty || request.SourceId == Guid.Empty
            || request.SubstanceId == Guid.Empty || request.SelectedResultId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.MaterialBasis) || string.IsNullOrWhiteSpace(request.ResidueDefinition)
            || request.EffectiveTo is { } end && end <= request.EffectiveFrom
            || request.SupersededResultIds is null || request.SupersededResultIds.Length > 1000
            || request.SupersededResultIds.Contains(request.SelectedResultId)
            || request.SupersededResultIds.Contains(Guid.Empty)
            || request.SupersededResultIds.Distinct().Count() != request.SupersededResultIds.Length
            || request.AbsoluteUncertainty is not null && !ResidueReview.Number(request.AbsoluteUncertainty)
            || request.ReportingThreshold is not null && !ResidueReview.PositiveNumber(request.ReportingThreshold))
            throw new LedgerException(400, "Preview requires valid source/result selection, exact uncertainty and effective interval");
        var actor = await ledger.ActorAsync(ct);
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("GrainLedger"));
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await ExecuteAsync(connection, tx, "SET TRANSACTION READ ONLY", ct);
        var snapshotAt = new DateTimeOffset((DateTime)(await ExecuteAsync(connection, tx, "SELECT clock_timestamp()", ct))!);
        if (!await ResidueReview.AuthorizeAsync(connection, tx, actor, "residue.evidence.manage", request.CompanyId, ct))
            throw new LedgerException(403, "Residue evidence company access denied");
        var sourceLot = await ExecuteAsync(connection, tx,
            "SELECT lot_id FROM capture.source_lots WHERE id=$1 AND company_id=$2", ct, request.SourceId, request.CompanyId);
        if (sourceLot is not Guid lotId) throw new LedgerException(404, "Confirmed own-company source not found");
        var graph = (string)(await ExecuteAsync(connection, tx, "SELECT ledger.residue_graph($1,$2)::text", ct, actor, lotId))!;
        var nodes = JsonSerializer.Deserialize<ResidueNode[]>(graph, Json)!;
        var sourceNode = nodes.SingleOrDefault(x => x.LotId == lotId && x.CompanyId == request.CompanyId && x.Kind == "SOURCE");
        if (sourceNode is null) throw new LedgerException(400, "Selection must refer to original owned source lot");
        var substances = new[] { request.SubstanceId };
        var evidence = await evidenceReader.ReadAsync(connection, tx, sourceNode.CompanyId, [lotId], substances, snapshotAt, ct);
        var results = evidence.Results.Where(x => x.SourceId == request.SourceId && x.SubstanceId == request.SubstanceId).ToArray();
        var selected = results.FirstOrDefault(x => x.ResultId == request.SelectedResultId);
        if (selected is null || request.SupersededResultIds.Any(id => !results.Any(x => x.ResultId == id)))
            throw new LedgerException(400, "Selected and superseded results require confirmed same-company/source/substance associations");
        if (request.AbsoluteUncertainty is not null && (selected.ResultKind != "NUMERIC" || string.IsNullOrWhiteSpace(selected.Uncertainty)))
            throw new LedgerException(400, "Normalized uncertainty requires numeric result with retained raw uncertainty evidence");
        if (request.ReportingThreshold is not null && selected.ResultKind != "NOT_DETECTED")
            throw new LedgerException(400, "Explicit reporting threshold applies only to NOT_DETECTED result");
        if (evidence.Selections.Any(x => x.Id == request.Id) || request.SupersedesId is { } prior
            && (!evidence.Selections.Any(x => x.Id == prior && x.SourceId == request.SourceId && x.SubstanceId == request.SubstanceId)
                || evidence.Selections.Any(x => x.SupersedesId == prior)))
            throw new LedgerException(409, "Selection ID or reviewed supersession conflicts with history");
        var policies = await policyReader.ReadAsync(connection, tx, sourceNode.CompanyId, substances, snapshotAt, ct);
        var proposed = evidence with { Selections = evidence.Selections.Append(request).ToArray() };
        var preview = ResidueEvidenceResolver.Resolve([sourceNode], substances, proposed, policies, snapshotAt).Single();
        await tx.CommitAsync(ct);
        return preview;
    }

    public async Task<JsonElement> HistoryAsync(Guid lotId, Guid? evaluationId, CancellationToken ct)
    {
        var actor = await ledger.ActorAsync(ct);
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("GrainLedger"));
        await connection.OpenAsync(ct);
        return Parse((string)(await ExecuteAsync(connection, null, "SELECT ledger.residue_history($1,$2,$3)::text", ct,
            actor, lotId, evaluationId))!);
    }

    private static JsonElement Parse(string text) => JsonSerializer.Deserialize<JsonElement>(text);
    private static async Task<object?> ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction? tx,
        string sql, CancellationToken ct, params object?[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection, tx);
        foreach (var value in values)
            command.Parameters.Add(value is JsonElement element
                ? new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = element.GetRawText() }
                : value is null ? new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = DBNull.Value }
                : new NpgsqlParameter { Value = value });
        try { return await command.ExecuteScalarAsync(ct); }
        catch (PostgresException ex) when (ex.SqlState is "42501" or "23505" or "23514" or "P0002")
        { throw new LedgerException(ex.SqlState == "42501" ? 403 : ex.SqlState == "P0002" ? 404 : 409, ex.MessageText); }
    }
}

public static class ResidueEvaluationEndpoints
{
    public static void MapResiduePreviewRoute(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup("/api/residue-report-selections").RequireAuthorization("residue.evidence.manage")
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>()
            .AddEndpointFilter<LedgerErrorFilter>()
            .MapPost("/preview", async (ResidueReportSelection request, ResidueEvaluationService service, CancellationToken ct) =>
                Results.Ok(await service.PreviewAsync(request, ct)));
        var reportReview = endpoints.MapGroup("/api/lab-reports/{reportId:guid}/residue-review")
            .RequireAuthorization("residue.evidence.manage")
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>()
            .AddEndpointFilter<LedgerErrorFilter>();
        reportReview.MapPost("/preview", async (Guid reportId, ResidueReportReviewRequest request,
            ResidueEvaluationService service, CancellationToken ct) =>
            Results.Ok(await service.ReviewReportAsync(reportId, request, false, ct)));
        reportReview.MapPost("/confirm", async (Guid reportId, ResidueReportReviewRequest request,
            ResidueEvaluationService service, CancellationToken ct) =>
            Results.Ok(await service.ReviewReportAsync(reportId, request, true, ct)));
    }

    public static void MapResidueEvaluationRoutes(RouteGroupBuilder ledger)
    {
        var routes = ledger.MapGroup("/lots/{lotId:guid}/residue-evaluations").RequireAuthorization(Permissions.ResidueEvaluate);
        ledger.MapGet("/lots/{lotId:guid}/residue-preview", async (Guid lotId, string? substanceIds,
            ResidueEvaluationService service, CancellationToken ct) =>
            Results.Ok(await service.PreviewLotAsync(lotId, substanceIds, ct)))
            .RequireAuthorization(Permissions.ResidueEvaluate);
        routes.MapPost("", async (Guid lotId, ResidueEvaluationRequest request, ResidueEvaluationService service, CancellationToken ct) =>
            Results.Ok(await service.EvaluateAsync(lotId, request, ct)));
        routes.MapGet("", async (Guid lotId, ResidueEvaluationService service, CancellationToken ct) =>
            Results.Ok(await service.HistoryAsync(lotId, null, ct)));
        routes.MapGet("/{evaluationId:guid}", async (Guid lotId, Guid evaluationId, ResidueEvaluationService service, CancellationToken ct) =>
            Results.Ok(await service.HistoryAsync(lotId, evaluationId, ct)));
    }
}
