using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace GrainLedger.Modules.Capture;

public sealed record ProcurementLineInput(Guid ItemId, int LineNo, Guid PlotSeasonId,
    string? MeasuredValue, string? MeasuredUnit, string? MoisturePercent,
    Guid LotId, Guid SourceLotId);
public sealed record ProcurementInput(Guid EntityId, Guid FarmerId, Guid CollectionSiteId,
    Guid ReceivingLocationId, Guid CommodityId, DateTimeOffset ReceivedAt,
    IReadOnlyList<ProcurementLineInput?>? Items);
public sealed record CommitProcurementRequest(string? DeviceId, Guid ClientOperationId,
    DateTimeOffset CapturedAt, int PayloadSchemaVersion, ProcurementInput? Payload);
public sealed record ProcurementInitialStock(Guid LocationId, string ReceivedKg);
public sealed record ProcurementLineResponse(Guid Id, int LineNo, Guid PlotSeasonId, Guid PlotId,
    Guid SeasonId, string RiceVariety, string MeasuredValue, string MeasuredUnit,
    string NetMassKg, string MoisturePercent, Guid SourceLotId, Guid LotId,
    string LotCode, string Stage, ProcurementInitialStock InitialStock);
public sealed record ProcurementResponse(Guid Id, string Code, Guid FarmerId, Guid CollectionSiteId,
    Guid ReceivingLocationId, Guid CommodityId, string Status, Guid ReceivedBy,
    DateTimeOffset ReceivedAt, DateTimeOffset CapturedAt, DateTimeOffset RecordedAt,
    Guid EventId, string TotalNetMassKg, IReadOnlyList<ProcurementLineResponse> Items);
public sealed record ProcurementSummary(Guid Id, string Code, Guid FarmerId, string FarmerName,
    DateTimeOffset ReceivedAt, string Status, int ItemCount, string TotalNetMassKg);
public sealed record ProcurementListResponse(IReadOnlyList<ProcurementSummary> Items,
    int Page, int PageSize, bool HasMore);
public sealed record ProcurementCommodity(Guid Id, string Code, string Name);
public sealed record ProcurementMassUnit(string Code, string Name, string KgFactor);
public sealed record ProcurementReferenceResponse(IReadOnlyList<ProcurementReceivingOption> ReceivingOptions,
    ProcurementCommodity Commodity, IReadOnlyList<ProcurementMassUnit> MassUnits,
    int Page, int PageSize, bool HasMore, int MaxLinesPerReceipt);
public sealed record SourceLotResponse(Guid Id, Guid LotId, string LotCode, Guid CommodityId, string Stage,
    Guid ProcurementId, string ProcurementCode, Guid ProcurementItemId, Guid FarmerId,
    Guid PlotId, Guid SeasonId, Guid PlotSeasonId, string RiceVariety,
    DateTimeOffset ReceivedAt, Guid CollectionSiteId, Guid InitialReceivingLocationId,
    string InitialNetMassKg, string MoisturePercent,
    IReadOnlyList<ProcurementStockPosition> CurrentStock, DateTimeOffset StockAsOf);

public static class ProcurementEndpoints
{
    private const string EventType = "PROCUREMENT_COMMITTED";
    private static readonly Regex DecimalPattern = new(@"^[0-9]+(?:\.[0-9]+)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IEndpointRouteBuilder MapProcurementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/procurement-reference-data", ReferenceAsync).RequireAuthorization("purchases.write")
            .AddEndpointFilter<NoStoreFilter>().WithTags("Procurements");
        var group = endpoints.MapGroup("/api/procurements").RequireAuthorization()
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>()
            .WithTags("Procurements");
        group.MapPost("/commit", CommitAsync).RequireAuthorization("purchases.write");
        group.MapGet("", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        endpoints.MapGet("/api/source-lots/{id:guid}", GetSourceLotAsync).RequireAuthorization()
            .AddEndpointFilter<NoStoreFilter>().WithTags("Procurements");
        return endpoints;
    }

    private static async Task<IResult> ReferenceAsync(HttpContext http, ClaimsPrincipal principal,
        ICurrentAccess access, IdentityDbContext identityDb, CatalogDbContext catalog,
        IProcurementLedgerPort ledger, CancellationToken ct)
    {
        var companyId = await CompanyAsync(principal, access, identityDb, ct);
        if (companyId is null) return Results.Forbid();
        if (!TryPage(http, out var page, out var size)) return Validation("query", "Invalid pagination value.");
        Guid? siteId = null;
        if (http.Request.Query.TryGetValue("collectionSiteId", out var siteValue))
        {
            if (siteValue.Count != 1 || !Guid.TryParse(siteValue[0], out var parsed) || parsed == Guid.Empty)
                return Validation("collectionSiteId", "Provide one non-empty UUID.");
            siteId = parsed;
        }
        var commodity = await catalog.Commodities.AsNoTracking().SingleOrDefaultAsync(x => x.Code == "PADDY", ct);
        if (commodity is null) return Problem(503, "PADDY_NOT_CONFIGURED", "Configure the PADDY commodity first.");
        var knownUnits = await catalog.MeasurementUnits.AsNoTracking()
            .Where(x => x.QuantityKind == "MASS" && (x.Code == "KG" || x.Code == "G" || x.Code == "T"))
            .OrderBy(x => x.Code).ToListAsync(ct);
        var units = knownUnits.Select(x => new ProcurementMassUnit(x.Code, x.Name,
            x.Code switch { "G" => "0.001", "T" => "1000", _ => "1" })).ToArray();
        if (units.Length == 0) return Problem(503, "MASS_UNITS_NOT_CONFIGURED", "Configure a supported mass unit first.");
        try
        {
            var options = await ledger.ListReceivingOptionsAsync(companyId.Value, siteId, (page - 1) * size, size + 1, ct);
            return Results.Ok(new ProcurementReferenceResponse(options.Take(size).ToArray(),
                new ProcurementCommodity(commodity.Id, commodity.Code, commodity.Name),
                units, page, size, options.Count > size, 100));
        }
        catch (ProcurementLedgerUnavailableException) { return LedgerUnavailable(); }
    }

    private static async Task<IResult> CommitAsync(CommitProcurementRequest request, ClaimsPrincipal principal,
        ICurrentAccess access, IdentityDbContext identityDb, CaptureDbContext db,
        CatalogDbContext catalog, IProcurementLedgerPort ledger, TimeProvider clock, CancellationToken ct)
    {
        var companyId = await CompanyAsync(principal, access, identityDb, ct);
        if (companyId is null) return Results.Forbid();
        var errors = Validate(request, out var lines);
        if (errors.Count != 0) return Results.ValidationProblem(errors);
        var payload = request.Payload!;
        var actorId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var deviceId = request.DeviceId!.Trim();
        var capturedAt = request.CapturedAt.ToUniversalTime();
        var receivedAt = payload.ReceivedAt.ToUniversalTime();
        var canonical = JsonSerializer.Serialize(new { operationType = EventType, request.PayloadSchemaVersion,
            capturedAt, payload.EntityId, payload.FarmerId, payload.CollectionSiteId,
            payload.ReceivingLocationId, payload.CommodityId, receivedAt,
            items = lines.Select(x => new { x.Input.ItemId, x.Input.LineNo, x.Input.PlotSeasonId,
                measuredValue = Number(x.Measured), x.Unit, moisturePercent = Number(x.Moisture),
                x.Input.LotId, x.Input.SourceLotId }) });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        var lockKey = $"{companyId.Value:N}:{deviceId}:{request.ClientOperationId:N}";
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);
        var prior = await db.OfflineOperations.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.DeviceId == deviceId && x.ClientOperationId == request.ClientOperationId, ct);
        if (prior is not null)
        {
            if (prior.PayloadHash != hash)
                return Problem(409, "OPERATION_CONFLICT", "The operation ID was used with different content.");
            if (prior.Status == "APPLIED" && prior.ResultPayload is not null)
                return Results.Ok(JsonSerializer.Deserialize<ProcurementResponse>(prior.ResultPayload));
            return Problem(409, "OPERATION_NOT_APPLIED", "This operation has no accepted result.");
        }

        var farmer = await db.Farmers.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.Id == payload.FarmerId, ct);
        if (farmer is null) return Problem(409, "SOURCE_RECORD_NOT_READY", "Farmer is not available for this company.");
        var seasonIds = lines.Select(x => x.Input.PlotSeasonId).Distinct().ToArray();
        var sources = await (from ps in db.PlotSeasons.AsNoTracking()
            join p in db.Plots.AsNoTracking() on new { ps.CompanyId, Id = ps.PlotId } equals new { p.CompanyId, p.Id }
            join s in db.CropSeasons.AsNoTracking() on new { ps.CompanyId, Id = ps.SeasonId } equals new { s.CompanyId, s.Id }
            where ps.CompanyId == companyId && seasonIds.Contains(ps.Id) && p.FarmerId == farmer.Id
            select new { ps.Id, ps.PlotId, ps.SeasonId, ps.RiceVariety }).ToListAsync(ct);
        if (sources.Count != seasonIds.Length)
            return Problem(409, "SOURCE_RECORD_NOT_READY", "Every plot-season must belong to this farmer and company.");
        var commodity = await catalog.Commodities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == payload.CommodityId, ct);
        if (commodity?.Code != "PADDY") return Validation("payload.commodityId", "Select the configured PADDY commodity.");
        var unitCodes = lines.Select(x => x.Unit).Distinct().ToArray();
        var unitCount = await catalog.MeasurementUnits.AsNoTracking().CountAsync(x =>
            unitCodes.Contains(x.Code) && x.QuantityKind == "MASS", ct);
        if (unitCount != unitCodes.Length)
            return Validation("payload.items", "Each mass unit must exist in the MASS catalog.");
        try
        {
            if (!await ledger.IsValidReceivingOptionAsync(companyId.Value, payload.CollectionSiteId,
                payload.ReceivingLocationId, ct))
                return Validation("payload.receivingLocationId", "Select an active receiving location for the collection site.");
        }
        catch (ProcurementLedgerUnavailableException) { return LedgerUnavailable(); }

        if (await db.Procurements.AsNoTracking().AnyAsync(x => x.Id == payload.EntityId, ct) ||
            await db.ProcurementItems.AsNoTracking().AnyAsync(x => lines.Select(y => y.Input.ItemId).Contains(x.Id), ct) ||
            await db.SourceLots.AsNoTracking().AnyAsync(x => lines.Select(y => y.Input.SourceLotId).Contains(x.Id) ||
                lines.Select(y => y.Input.LotId).Contains(x.LotId), ct))
            return Problem(409, "ENTITY_ALREADY_EXISTS", "A procurement, line or lot identifier already exists.");

        var now = clock.GetUtcNow();
        var businessEvent = new CaptureBusinessEvent { CompanyId = companyId, ActorId = actorId,
            EventType = EventType, OccurredAt = receivedAt, RecordedAt = now,
            CorrelationId = request.ClientOperationId, Payload = "{}" };
        var code = $"TM-{payload.EntityId:N}".ToUpperInvariant();
        var header = new Procurement { Id = payload.EntityId, CompanyId = companyId.Value,
            Code = code, FarmerId = farmer.Id, CollectionSiteId = payload.CollectionSiteId,
            ReceivedBy = actorId, ReceivedAt = receivedAt, Status = "COMMITTED", EventId = businessEvent.Id };
        var newItems = new List<ProcurementItem>();
        var newSources = new List<SourceLot>();
        var itemResponses = lines.Select(x =>
        {
            var source = sources.Single(s => s.Id == x.Input.PlotSeasonId);
            var lotCode = $"LL-{x.Input.LotId:N}".ToUpperInvariant();
            newItems.Add(new ProcurementItem { Id = x.Input.ItemId, CompanyId = companyId.Value,
                ProcurementId = payload.EntityId, LineNo = x.Input.LineNo, PlotSeasonId = x.Input.PlotSeasonId,
                NetMassKg = x.Kg, MeasuredValue = x.Measured, MeasuredUnit = x.Unit,
                MoisturePercent = x.Moisture });
            newSources.Add(new SourceLot { Id = x.Input.SourceLotId, CompanyId = companyId.Value,
                LotId = x.Input.LotId, ProcurementItemId = x.Input.ItemId });
            return new ProcurementLineResponse(x.Input.ItemId, x.Input.LineNo, x.Input.PlotSeasonId,
                source.PlotId, source.SeasonId, source.RiceVariety, Number(x.Measured), x.Unit,
                Number(x.Kg), Number(x.Moisture), x.Input.SourceLotId, x.Input.LotId,
                lotCode, "RAW_PADDY", new ProcurementInitialStock(payload.ReceivingLocationId, Number(x.Kg)));
        }).ToArray();
        var total = lines.Sum(x => x.Kg);
        var result = new ProcurementResponse(payload.EntityId, code, farmer.Id, payload.CollectionSiteId,
            payload.ReceivingLocationId, payload.CommodityId, "COMMITTED", actorId, receivedAt,
            capturedAt, now, businessEvent.Id, Number(total), itemResponses);
        businessEvent.Payload = JsonSerializer.Serialize(new { schemaVersion = 1, procurementId = payload.EntityId,
            result });
        var operation = new CaptureOperation { CompanyId = companyId.Value, SubmittedBy = actorId,
            DeviceId = deviceId, ClientOperationId = request.ClientOperationId, PayloadHash = hash,
            Payload = canonical, CapturedAt = capturedAt, ReceivedAt = now, Status = "APPLIED",
            AppliedEventId = businessEvent.Id, ResultPayload = JsonSerializer.Serialize(result) };
        try
        {
            // FK targets are inserted in dependency order; the transaction remains uncommitted until Ledger succeeds.
            db.BusinessEvents.Add(businessEvent);
            await db.SaveChangesAsync(ct);
            db.Procurements.Add(header);
            await db.SaveChangesAsync(ct);
            db.ProcurementItems.AddRange(newItems);
            await db.SaveChangesAsync(ct);
            db.SourceLots.AddRange(newSources);
            db.OfflineOperations.Add(operation);
            await db.SaveChangesAsync(ct);
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            var dbTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();
            var stockLines = itemResponses.Select(x => new ProcurementStockLine(x.LotId, x.LotCode,
                x.SourceLotId, x.Id, decimal.Parse(x.NetMassKg, CultureInfo.InvariantCulture))).ToArray();
            await ledger.PostReceiptAsync(connection, dbTransaction, companyId.Value, actorId,
                businessEvent.Id, payload.EntityId, payload.CommodityId, payload.CollectionSiteId,
                payload.ReceivingLocationId, stockLines, ct);
            await transaction.CommitAsync(ct);
        }
        catch (ProcurementLedgerUnavailableException) { return LedgerUnavailable(); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return Problem(409, "ENTITY_ALREADY_EXISTS", "A procurement, line or lot identifier already exists."); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        { return Problem(409, "SOURCE_RECORD_NOT_READY", "A source record changed. Reload it and try again."); }
        return Results.Created($"/api/procurements/{payload.EntityId}", result);
    }

    private static async Task<IResult> ListAsync(HttpContext http, ClaimsPrincipal principal,
        ICurrentAccess access, IdentityDbContext identityDb, CaptureDbContext db, CancellationToken ct)
    {
        if (!await CanReadAsync(access, ct)) return Results.Forbid();
        var companyId = await CompanyAsync(principal, access, identityDb, ct);
        if (companyId is null) return Results.Forbid();
        if (!TryPage(http, out var page, out var size)) return Validation("query", "Invalid pagination value.");
        Guid? farmerId = null;
        if (http.Request.Query.TryGetValue("farmerId", out var farmerValue))
        {
            if (farmerValue.Count != 1 || !Guid.TryParse(farmerValue[0], out var parsed) || parsed == Guid.Empty)
                return Validation("farmerId", "Provide one non-empty UUID.");
            farmerId = parsed;
        }
        if (!TryDate(http, "from", out var from) || !TryDate(http, "to", out var to) ||
            (from.HasValue && to.HasValue && from > to)) return Validation("query", "Invalid date range.");
        var query = db.Procurements.AsNoTracking().Where(x => x.CompanyId == companyId && x.Status == "COMMITTED");
        if (farmerId is { } id) query = query.Where(x => x.FarmerId == id);
        if (from is { } first) query = query.Where(x => x.ReceivedAt >= new DateTimeOffset(first.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).ToUniversalTime());
        if (to is { } last) query = query.Where(x => x.ReceivedAt < new DateTimeOffset(last.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).ToUniversalTime());
        var rows = await (from p in query
            join f in db.Farmers.AsNoTracking() on new { p.CompanyId, Id = p.FarmerId } equals new { f.CompanyId, f.Id }
            orderby p.ReceivedAt descending, p.Id
            select new { p.Id, p.Code, p.FarmerId, FarmerName = f.Name, p.ReceivedAt, p.Status })
            .Skip((page - 1) * size).Take(size + 1).ToListAsync(ct);
        var ids = rows.Take(size).Select(x => x.Id).ToArray();
        var totals = await db.ProcurementItems.AsNoTracking().Where(x => x.CompanyId == companyId && ids.Contains(x.ProcurementId))
            .GroupBy(x => x.ProcurementId).Select(g => new { Id = g.Key, Count = g.Count(), Total = g.Sum(x => x.NetMassKg) })
            .ToDictionaryAsync(x => x.Id, ct);
        var summaries = rows.Take(size).Select(x => new ProcurementSummary(x.Id, x.Code, x.FarmerId,
            x.FarmerName, x.ReceivedAt, x.Status, totals[x.Id].Count, Number(totals[x.Id].Total))).ToArray();
        return Results.Ok(new ProcurementListResponse(summaries, page, size, rows.Count > size));
    }

    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal principal, ICurrentAccess access,
        IdentityDbContext identityDb, CaptureDbContext db, CancellationToken ct)
    {
        if (!await CanReadAsync(access, ct)) return Results.Forbid();
        var companyId = await CompanyAsync(principal, access, identityDb, ct);
        if (companyId is null) return Results.Forbid();
        var header = await db.Procurements.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, ct);
        if (header?.EventId is null) return Problem(404, "PROCUREMENT_NOT_FOUND", "Procurement not found.");
        var ev = await db.BusinessEvents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == header.EventId && x.CompanyId == companyId, ct);
        if (ev is null) return Problem(500, "PROCUREMENT_EVENT_MISSING", "The procurement history is incomplete.");
        using var json = JsonDocument.Parse(ev.Payload);
        var result = json.RootElement.GetProperty("result").Deserialize<ProcurementResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return result is null ? Problem(500, "PROCUREMENT_EVENT_INVALID", "The procurement history is incomplete.") : Results.Ok(result);
    }

    private static async Task<IResult> GetSourceLotAsync(Guid id, ClaimsPrincipal principal, ICurrentAccess access,
        IdentityDbContext identityDb, CaptureDbContext db, IProcurementLedgerPort ledger,
        TimeProvider clock, CancellationToken ct)
    {
        if (!await CanReadAsync(access, ct)) return Results.Forbid();
        var companyId = await CompanyAsync(principal, access, identityDb, ct);
        if (companyId is null) return Results.Forbid();
        var source = await db.SourceLots.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, ct);
        if (source is null) return Problem(404, "SOURCE_LOT_NOT_FOUND", "Source lot not found.");
        var item = await db.ProcurementItems.AsNoTracking().SingleAsync(x => x.Id == source.ProcurementItemId && x.CompanyId == companyId, ct);
        var header = await db.Procurements.AsNoTracking().SingleAsync(x => x.Id == item.ProcurementId && x.CompanyId == companyId, ct);
        var ev = await db.BusinessEvents.AsNoTracking().SingleAsync(x => x.Id == header.EventId && x.CompanyId == companyId, ct);
        using var json = JsonDocument.Parse(ev.Payload);
        var snapshot = json.RootElement.GetProperty("result").Deserialize<ProcurementResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var line = snapshot.Items.Single(x => x.Id == item.Id);
        try
        {
            var stock = await ledger.GetCurrentStockAsync(companyId.Value, source.LotId, ct);
            return Results.Ok(new SourceLotResponse(source.Id, source.LotId, line.LotCode,
                snapshot.CommodityId, line.Stage, header.Id, header.Code, item.Id, header.FarmerId,
                line.PlotId, line.SeasonId, line.PlotSeasonId, line.RiceVariety, header.ReceivedAt,
                header.CollectionSiteId, snapshot.ReceivingLocationId, line.NetMassKg,
                line.MoisturePercent, stock, clock.GetUtcNow()));
        }
        catch (ProcurementLedgerUnavailableException) { return LedgerUnavailable(); }
    }

    internal sealed record ParsedLine(ProcurementLineInput Input, decimal Measured, string Unit, decimal Kg, decimal Moisture);
    internal static Dictionary<string, string[]> Validate(CommitProcurementRequest request, out ParsedLine[] lines)
    {
        var errors = new Dictionary<string, string[]>();
        var parsed = new List<ParsedLine>();
        if (string.IsNullOrWhiteSpace(request.DeviceId) || request.DeviceId.Trim().Length > 100) errors["deviceId"] = ["Device ID is required and must not exceed 100 characters."];
        if (request.ClientOperationId == Guid.Empty) errors["clientOperationId"] = ["Operation ID is required."];
        if (request.CapturedAt == default) errors["capturedAt"] = ["Capture time is required."];
        if (request.PayloadSchemaVersion != 1) errors["payloadSchemaVersion"] = ["Unsupported payload version."];
        if (request.Payload is null) { errors["payload"] = ["Procurement data is required."]; lines = []; return errors; }
        var p = request.Payload;
        if (p.EntityId == Guid.Empty) errors["payload.entityId"] = ["Procurement ID is required."];
        if (p.FarmerId == Guid.Empty) errors["payload.farmerId"] = ["Farmer ID is required."];
        if (p.CollectionSiteId == Guid.Empty) errors["payload.collectionSiteId"] = ["Collection site ID is required."];
        if (p.ReceivingLocationId == Guid.Empty) errors["payload.receivingLocationId"] = ["Receiving location ID is required."];
        if (p.CommodityId == Guid.Empty) errors["payload.commodityId"] = ["Commodity ID is required."];
        if (p.ReceivedAt == default) errors["payload.receivedAt"] = ["Receiving time is required."];
        if (p.Items is null || p.Items.Count is < 1 or > 100) { errors["payload.items"] = ["Provide 1 to 100 lines."]; lines = []; return errors; }
        var itemIds = new HashSet<Guid>(); var lotIds = new HashSet<Guid>(); var sourceIds = new HashSet<Guid>(); var lineNos = new HashSet<int>();
        for (var i = 0; i < p.Items.Count; i++)
        {
            var x = p.Items[i]; var path = $"payload.items[{i}]";
            if (x is null) { errors[path] = ["Line is required."]; continue; }
            if (x.ItemId == Guid.Empty || !itemIds.Add(x.ItemId)) errors[$"{path}.itemId"] = ["Unique item ID is required."];
            if (x.LotId == Guid.Empty || !lotIds.Add(x.LotId)) errors[$"{path}.lotId"] = ["Unique lot ID is required."];
            if (x.SourceLotId == Guid.Empty || !sourceIds.Add(x.SourceLotId)) errors[$"{path}.sourceLotId"] = ["Unique source lot ID is required."];
            if (x.PlotSeasonId == Guid.Empty) errors[$"{path}.plotSeasonId"] = ["Plot-season ID is required."];
            if (x.LineNo <= 0 || !lineNos.Add(x.LineNo)) errors[$"{path}.lineNo"] = ["Positive unique line number is required."];
            var unit = x.MeasuredUnit?.Trim().ToUpperInvariant() ?? "";
            if (unit is not ("KG" or "G" or "T")) errors[$"{path}.measuredUnit"] = ["Use KG, G or T."];
            if (!TryDecimal(x.MeasuredValue, 12, out var measured) || measured <= 0) errors[$"{path}.measuredValue"] = ["Enter a positive decimal fitting numeric(24,12)."];
            if (!TryDecimal(x.MoisturePercent, 12, out var moisture) || moisture is < 0 or > 100) errors[$"{path}.moisturePercent"] = ["Enter a decimal from 0 to 100."];
            if (errors.Keys.Any(key => key.StartsWith(path + ".", StringComparison.Ordinal))) continue;
            decimal kg;
            try { checked { kg = unit switch { "G" => measured / 1000m, "T" => measured * 1000m, _ => measured }; } }
            catch (OverflowException) { errors[$"{path}.measuredValue"] = ["Converted mass exceeds the supported range."]; continue; }
            if (kg <= 0 || kg >= 100_000_000_000_000m || kg * 1_000_000m != decimal.Truncate(kg * 1_000_000m))
            { errors[$"{path}.measuredValue"] = ["Mass must fit numeric(20,6) kg exactly."]; continue; }
            parsed.Add(new ParsedLine(x, measured, unit, kg, moisture));
        }
        if (!Enumerable.Range(1, p.Items.Count).All(lineNos.Contains)) errors["payload.items.lineNo"] = ["Line numbers must be consecutive from 1."];
        lines = parsed.OrderBy(x => x.Input.LineNo).ToArray();
        try { _ = lines.Sum(x => x.Kg); } catch (OverflowException) { errors["payload.items"] = ["Total mass exceeds the supported range."]; }
        return errors;
    }

    internal static bool TryDecimal(string? text, int scale, out decimal value)
    {
        value = 0;
        if (text is null || !DecimalPattern.IsMatch(text.Trim())) return false;
        var parts = text.Trim().Split('.', 2);
        var integer = parts[0].TrimStart('0');
        var fraction = parts.Length == 2 ? parts[1].TrimEnd('0') : "";
        if (integer.Length > 12 || fraction.Length > scale) return false;
        return decimal.TryParse(text.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }
    private static string Number(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
    private static async Task<Guid?> CompanyAsync(ClaimsPrincipal principal, ICurrentAccess access,
        IdentityDbContext identityDb, CancellationToken ct)
    {
        var id = await CaptureActor.GetCustomerCompanyAsync(principal, identityDb, ct);
        return id is not null && await access.CanAccessOrganizationAsync(id.Value, ct) ? id : null;
    }
    private static async Task<bool> CanReadAsync(ICurrentAccess access, CancellationToken ct) =>
        await access.HasPermissionAsync("purchases.write", ct) || await access.HasPermissionAsync("lots.read.tenant", ct);
    private static bool TryPage(HttpContext http, out int page, out int size)
    {
        page = 1; size = 20;
        if (http.Request.Query.TryGetValue("page", out var p) &&
            (p.Count != 1 || !int.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out page) || page < 1)) return false;
        if (http.Request.Query.TryGetValue("pageSize", out var s) &&
            (s.Count != 1 || !int.TryParse(s[0], NumberStyles.None, CultureInfo.InvariantCulture, out size) || size is < 1 or > 100)) return false;
        return page - 1 <= int.MaxValue / size;
    }
    private static bool TryDate(HttpContext http, string key, out DateOnly? value)
    {
        value = null;
        if (!http.Request.Query.TryGetValue(key, out var raw)) return true;
        if (raw.Count != 1 || !DateOnly.TryParseExact(raw[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return false;
        value = parsed; return true;
    }
    private static IResult Validation(string key, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });
    private static IResult Problem(int status, string code, string title) => Results.Problem(statusCode: status, title: title,
        extensions: new Dictionary<string, object?> { ["code"] = code });
    private static IResult LedgerUnavailable() => Problem(503, "LEDGER_INTEGRATION_UNAVAILABLE",
        "Custody Ledger integration is not configured; no procurement was committed.");
}
