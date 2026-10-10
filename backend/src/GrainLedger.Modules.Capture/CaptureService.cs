using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GrainLedger.Modules.Identity;
using GrainLedger.Modules.Ledger;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;

namespace GrainLedger.Modules.Capture;

public sealed class CaptureService(IConfiguration configuration, IHttpContextAccessor http, LedgerService ledger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record Actor(Guid Id, Guid OrganizationId, string Role)
    {
        public bool Has(string permission) => Roles.All.TryGetValue(Role, out var definition) && definition.Permissions.Contains(permission);
    }

    private NpgsqlConnection Connection() => new(configuration.GetConnectionString("GrainLedger")
        ?? throw new InvalidOperationException("ConnectionStrings:GrainLedger is required."));
    private static string Serialize(object value) => JsonSerializer.Serialize(value, Json);
    private static T Decode<T>(string value) => JsonSerializer.Deserialize<T>(value, Json) ?? throw new InvalidOperationException("Stored Capture document is invalid.");
    private static void Id(Guid id) { if (id == Guid.Empty) throw new CaptureFault(400, "A nonempty UUID is required."); }
    private static void Text(string? value, string field, int maximum = 2000)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value.Contains('\0'))
            throw new CaptureFault(400, $"{field} is required, must not contain NUL, and must not exceed {maximum} characters.");
    }
    private static void Permission(Actor actor, string permission)
    {
        if (!actor.Has(permission)) throw new CaptureFault(403, $"Permission {permission} is required.");
    }
    private static void Own(Actor actor, Guid owner)
    {
        if (actor.OrganizationId != owner || !Roles.All.TryGetValue(actor.Role, out var role) || role.OrganizationKind != OrganizationKind.Customer)
            throw new CaptureFault(403, "Customer records require persisted company ownership.");
    }

    private async Task<Actor> CurrentAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, CancellationToken ct, bool lockAccess = true)
    {
        var principal = http.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            throw new CaptureFault(401, "Authentication is required.");
        await using var command = Command(connection, transaction, "SELECT \"OrganizationId\",\"Role\" FROM identity.users WHERE \"Id\"=@id AND \"IsActive\"" + (transaction is null || !lockAccess ? "" : " FOR SHARE"), ("id", userId));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.IsDBNull(0) || reader.IsDBNull(1)) throw new CaptureFault(403, "An active persisted organization role is required.");
        var role = reader.GetString(1);
        if (!Roles.All.ContainsKey(role)) throw new CaptureFault(403, "Unknown persisted role.");
        return new(userId, reader.GetGuid(0), role);
    }

    private static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, params (string Key, object? Value)[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (key, value) in parameters)
        {
            if (value is null) command.Parameters.Add(new NpgsqlParameter(key, NpgsqlDbType.Unknown) { Value = DBNull.Value });
            else command.Parameters.AddWithValue(key, value);
        }
        return command;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken ct, params (string Key, object? Value)[] parameters)
    {
        await using var command = Command(connection, transaction, sql, parameters);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, CancellationToken ct, params (string Key, object? Value)[] parameters)
    {
        await using var command = Command(connection, transaction, sql, parameters);
        var value = await command.ExecuteScalarAsync(ct);
        return value is DBNull ? null : value;
    }

    private async Task<T> MutationAsync<T>(string kind, Guid id, object content,
        Func<NpgsqlConnection, NpgsqlTransaction, Actor, Task<T>> action, CancellationToken ct)
    {
        Id(id);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(content))));
        await using var connection = Connection();
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        // ponytail: shared stock-write lock; per-resource locks can replace it when throughput requires.
        // Evidence upload owns a separate Ledger transaction; no source/stock/access row locks held here.
        if (kind != "evidence") await ExecuteAsync(connection, transaction, "SELECT pg_advisory_xact_lock(714928352)", ct);
        var actor = await CurrentAsync(connection, transaction, ct, lockAccess: kind != "evidence");
        await ExecuteAsync(connection, transaction, "SELECT pg_advisory_xact_lock(hashtextextended(@key,0))", ct, ("key", "capture:" + kind + ":" + id));
        var replay = false;
        T result = default!;
        await using (var command = Command(connection, transaction, "SELECT actor_id,content_hash,response::text,actor_organization_id,actor_role FROM capture.commands WHERE kind=@kind AND id=@id", ("kind", kind), ("id", id)))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                if (reader.GetGuid(0) != actor.Id || reader.GetGuid(3) != actor.OrganizationId || reader.GetString(4) != actor.Role)
                    throw new CaptureFault(403, "Command no longer belongs to current persisted actor access.");
                if (reader.GetString(1) != hash) throw new CaptureFault(409, "Command UUID was already used with different content.");
                result = Decode<T>(reader.GetString(2));
                replay = true;
            }
        }
        if (replay)
        {
            await AuthorizeReplayAsync(connection, transaction, actor, kind, content, result!, ct);
            return result;
        }
        result = await action(connection, transaction, actor);
        await ExecuteAsync(connection, transaction,
            "INSERT INTO capture.commands(kind,id,actor_id,actor_organization_id,actor_role,content_hash,response) VALUES(@kind,@id,@actor,@organization,@role,@hash,@response::jsonb)", ct,
            ("kind", kind), ("id", id), ("actor", actor.Id), ("organization", actor.OrganizationId), ("role", actor.Role), ("hash", hash), ("response", Serialize(result!)));
        await transaction.CommitAsync(ct);
        return result;
    }

    private async Task AuthorizeReplayAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Actor actor, string kind, object content, object result, CancellationToken ct)
    {
        var permission = kind.Split(':')[0] switch
        {
            "farmer" or "plot" or "season" or "plot-season" => "farms.write",
            "receipt" or "receipt-update" or "receipt-commit" or "receipt-correction" => "purchases.write",
            "arrival" => "lots.receive",
            "arrival-correction" or "arrival-investigation" => "receiving.reconcile",
            "receipt-reconcile" or "receipt-correction-commit" => "corrections.reconcile",
            _ => null
        };
        if (permission is not null) Permission(actor, permission);
        if (result is ReceiptDto receipt) Own(actor, receipt.OwnerOrganizationId);
        else if (result is ReceiptCorrectionDto correction)
        {
            if (permission == "purchases.write") Own(actor, correction.Replacement.OwnerOrganizationId);
            else await ReceiveScopeAsync(actor, correction.Replacement, "corrections.reconcile", ct);
        }
        else if (result is ArrivalDto arrival)
        {
            var (source, _) = await ReceiptLineAsync(connection, transaction, arrival.ReceiptLineId, false, ct);
            await ReceiveScopeAsync(actor, source, permission!, ct);
        }
        else if (content is EvidenceRequest evidence)
        {
            var facility = await ledger.GetLocationFacilityAsync(evidence.LocationId, ct);
            if (!await ledger.CanAccessEvidenceAsync(evidence.Id, evidence.OwnerOrganizationId, facility, ct))
                throw new CaptureFault(403, "Evidence is outside current company or facility scope.");
        }
    }

    public Task<FarmerRequest> CreateFarmerAsync(FarmerRequest request, CancellationToken ct) => MutationAsync("farmer", request.Id, request, async (connection, transaction, actor) =>
    {
        Permission(actor, "farms.write"); Own(actor, request.OwnerOrganizationId); Text(request.Name, "Farmer name", 160);
        if (request.PhoneNumber?.Length > 32 || request.PhoneNumber?.Contains('\0') == true)
            throw new CaptureFault(400, "Phone number must not contain NUL or exceed 32 characters.");
        await ExecuteAsync(connection, transaction, "INSERT INTO capture.farmers(id,owner_id,body) VALUES(@id,@owner,@body::jsonb)", ct,
            ("id", request.Id), ("owner", request.OwnerOrganizationId), ("body", Serialize(request)));
        return request;
    }, ct);

    public Task<PlotRequest> CreatePlotAsync(PlotRequest request, CancellationToken ct) => MutationAsync("plot", request.Id, request, async (connection, transaction, actor) =>
    {
        Permission(actor, "farms.write"); Text(request.Name, "Plot name", 160); Id(request.FarmerId);
        var owner = await ScalarAsync(connection, transaction, "SELECT owner_id FROM capture.farmers WHERE id=@id", ct, ("id", request.FarmerId));
        if (owner is not Guid ownerId) throw new CaptureFault(404, "Farmer not found.");
        Own(actor, ownerId);
        var latitude = CaptureMeasure.Coordinate(request.Latitude, 90); var longitude = CaptureMeasure.Coordinate(request.Longitude, 180);
        await ExecuteAsync(connection, transaction, "INSERT INTO capture.plots(id,owner_id,farmer_id,body,latitude,longitude) VALUES(@id,@owner,@farmer,@body::jsonb,@lat,@lng)", ct,
            ("id", request.Id), ("owner", ownerId), ("farmer", request.FarmerId), ("body", Serialize(request)), ("lat", latitude), ("lng", longitude));
        return request;
    }, ct);

    public Task<SeasonRequest> CreateSeasonAsync(SeasonRequest request, CancellationToken ct) => MutationAsync("season", request.Id, request, async (connection, transaction, actor) =>
    {
        Permission(actor, "farms.write"); Own(actor, request.OwnerOrganizationId); Text(request.Name, "Season name", 160);
        if (request.StartsOn == default || request.EndsOn < request.StartsOn) throw new CaptureFault(400, "Season dates must form a valid inclusive interval.");
        await ExecuteAsync(connection, transaction, "INSERT INTO capture.seasons(id,owner_id,body,starts_on,ends_on) VALUES(@id,@owner,@body::jsonb,@start,@end)", ct,
            ("id", request.Id), ("owner", request.OwnerOrganizationId), ("body", Serialize(request)), ("start", request.StartsOn), ("end", request.EndsOn));
        return request;
    }, ct);

    public Task<PlotSeasonRequest> CreatePlotSeasonAsync(PlotSeasonRequest request, CancellationToken ct) => MutationAsync("plot-season", request.Id, request, async (connection, transaction, actor) =>
    {
        Permission(actor, "farms.write"); Id(request.PlotId); Id(request.SeasonId);
        Guid owner, farmer;
        await using (var command = Command(connection, transaction, "SELECT owner_id,farmer_id FROM capture.plots WHERE id=@id", ("id", request.PlotId)))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw new CaptureFault(404, "Plot not found.");
            owner = reader.GetGuid(0); farmer = reader.GetGuid(1);
        }
        Own(actor, owner);
        var seasonOwner = await ScalarAsync(connection, transaction, "SELECT owner_id FROM capture.seasons WHERE id=@id", ct, ("id", request.SeasonId));
        if (seasonOwner is not Guid seasonOwnerId || seasonOwnerId != owner) throw new CaptureFault(400, "Plot and season ownership must agree.");
        await ExecuteAsync(connection, transaction, "INSERT INTO capture.plot_seasons(id,owner_id,farmer_id,plot_id,season_id,body) VALUES(@id,@owner,@farmer,@plot,@season,@body::jsonb)", ct,
            ("id", request.Id), ("owner", owner), ("farmer", farmer), ("plot", request.PlotId), ("season", request.SeasonId), ("body", Serialize(request)));
        return request;
    }, ct);

    public async Task<T[]> ListSourcesAsync<T>(string table, CancellationToken ct)
    {
        if (table is not ("farmers" or "plots" or "seasons" or "plot_seasons")) throw new ArgumentOutOfRangeException(nameof(table));
        await using var connection = Connection(); await connection.OpenAsync(ct);
        var actor = await CurrentAsync(connection, null, ct);
        if (!actor.Has("farms.read") && !actor.Has("farms.write")) throw new CaptureFault(403, "Source read permission is required.");
        Own(actor, actor.OrganizationId);
        await using var command = Command(connection, null, $"SELECT body::text FROM capture.{table} WHERE owner_id=@owner ORDER BY id", ("owner", actor.OrganizationId));
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<T>(); while (await reader.ReadAsync(ct)) rows.Add(Decode<T>(reader.GetString(0)));
        return rows.ToArray();
    }

    private static void ValidateReceipt(ReceiptRequest request)
    {
        Id(request.Id); Id(request.OwnerOrganizationId); Id(request.FarmerId); Id(request.SeasonId); Id(request.LocationId);
        if (request.OccurredAt == default) throw new CaptureFault(400, "Procurement occurrence time is required.");
        if (request.Lines is null || request.Lines.Length is < 1 or > 200) throw new CaptureFault(400, "Receipt requires one to 200 positive lines.");
        if (request.Lines.Any(x => x is null) || request.Lines.Select(x => x.Id).Distinct().Count() != request.Lines.Length)
            throw new CaptureFault(400, "Receipt line UUIDs must be distinct.");
        foreach (var line in request.Lines)
        {
            Id(line.Id); Id(line.PlotSeasonId); Id(line.CommodityId); Text(line.LotCode, "Lot code", 120);
            CaptureMeasure.ToKg(line.Quantity, line.Unit);
        }
    }

    private static async Task ValidateSourcesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ReceiptRequest request, CancellationToken ct)
    {
        foreach (var line in request.Lines)
        {
            var matches = await ScalarAsync(connection, transaction,
                "SELECT EXISTS(SELECT 1 FROM capture.plot_seasons WHERE id=@plot AND owner_id=@owner AND farmer_id=@farmer AND season_id=@season)", ct,
                ("plot", line.PlotSeasonId), ("owner", request.OwnerOrganizationId), ("farmer", request.FarmerId), ("season", request.SeasonId));
            if (matches is not true) throw new CaptureFault(400, "Farmer, plot, season and procurement ownership must agree.");
        }
    }

    private static async Task InsertLinesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ReceiptRequest request, CancellationToken ct)
    {
        foreach (var line in request.Lines)
            await ExecuteAsync(connection, transaction,
                "INSERT INTO capture.receipt_lines(id,receipt_id,owner_id,farmer_id,season_id,plot_season_id,commodity_id,body,quantity_kg) VALUES(@id,@receipt,@owner,@farmer,@season,@plot,@commodity,@body::jsonb,@kg)", ct,
                ("id", line.Id), ("receipt", request.Id), ("owner", request.OwnerOrganizationId), ("farmer", request.FarmerId), ("season", request.SeasonId),
                ("plot", line.PlotSeasonId), ("commodity", line.CommodityId), ("body", Serialize(line)), ("kg", CaptureMeasure.ToKg(line.Quantity, line.Unit)));
    }

    private static async Task InsertReceiptAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Actor actor, ReceiptRequest request, Guid? originalId, CancellationToken ct)
    {
        ValidateReceipt(request); await ValidateSourcesAsync(connection, transaction, request, ct);
        await ExecuteAsync(connection, transaction,
            "INSERT INTO capture.receipts(id,owner_id,farmer_id,season_id,location_id,body,version,replacement_for,created_by) VALUES(@id,@owner,@farmer,@season,@location,@body::jsonb,@version,@original,@actor)", ct,
            ("id", request.Id), ("owner", request.OwnerOrganizationId), ("farmer", request.FarmerId), ("season", request.SeasonId), ("location", request.LocationId),
            ("body", Serialize(request)), ("version", Guid.NewGuid()), ("original", originalId), ("actor", actor.Id));
        await InsertLinesAsync(connection, transaction, request, ct);
    }

    public Task<ReceiptDto> CreateReceiptAsync(ReceiptRequest request, CancellationToken ct) => MutationAsync("receipt", request.Id, request, async (connection, transaction, actor) =>
    {
        Permission(actor, "purchases.write"); Own(actor, request.OwnerOrganizationId);
        await InsertReceiptAsync(connection, transaction, actor, request, null, ct);
        return await ReceiptAsync(connection, transaction, request.Id, false, ct);
    }, ct);

    private static async Task<ReceiptDto> ReceiptAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid id, bool locked, CancellationToken ct)
    {
        ReceiptRequest body; string status; Guid version; Guid? original;
        await using (var command = Command(connection, transaction, "SELECT body::text,status,version,replacement_for FROM capture.receipts WHERE id=@id" + (locked ? " FOR UPDATE" : ""), ("id", id)))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw new CaptureFault(404, "Receipt not found.");
            body = Decode<ReceiptRequest>(reader.GetString(0)); status = reader.GetString(1); version = reader.GetGuid(2); original = reader.IsDBNull(3) ? null : reader.GetGuid(3);
        }
        var lots = new Dictionary<Guid, Guid>();
        await using (var command = Command(connection, transaction, "SELECT l.id,m.lot_id FROM capture.receipt_lines l JOIN capture.receipt_lots m ON m.receipt_line_id=l.id WHERE l.receipt_id=@id", ("id", id)))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) lots.Add(reader.GetGuid(0), reader.GetGuid(1));
        return new(body.Id, body.OwnerOrganizationId, body.FarmerId, body.SeasonId, body.LocationId, body.OccurredAt,
            body.Lines.Select(line => new ReceiptLineDto(line.Id, line.PlotSeasonId, line.CommodityId, line.LotCode, line.Quantity, line.Unit,
                CaptureMeasure.Kg(CaptureMeasure.ToKg(line.Quantity, line.Unit)), lots.TryGetValue(line.Id, out var lot) ? lot : null)).ToArray(), status, version, original);
    }

    private async Task ReadScopeAsync(Actor actor, ReceiptDto receipt, CancellationToken ct)
    {
        if (actor.OrganizationId == receipt.OwnerOrganizationId && (actor.Has("purchases.write") || actor.Has("farms.read") || actor.Has("lots.read.tenant"))) return;
        if ((actor.Has("lots.receive") || actor.Has("corrections.reconcile")) && await ledger.CanAccessLocationAsync(receipt.LocationId,
            actor.Has("lots.receive") ? "lots.receive" : "corrections.reconcile", ct)) return;
        throw new CaptureFault(403, "Receipt is outside company or assigned facility scope.");
    }

    public async Task<ReceiptDto> GetReceiptAsync(Guid id, CancellationToken ct)
    {
        await using var connection = Connection(); await connection.OpenAsync(ct);
        var actor = await CurrentAsync(connection, null, ct); var receipt = await ReceiptAsync(connection, null, id, false, ct);
        await ReadScopeAsync(actor, receipt, ct); return receipt;
    }

    public async Task<ReceiptDto[]> ListReceiptsAsync(CancellationToken ct)
    {
        await using var connection = Connection(); await connection.OpenAsync(ct); var actor = await CurrentAsync(connection, null, ct);
        var ids = new List<Guid>();
        await using (var command = Command(connection, null, "SELECT id FROM capture.receipts ORDER BY created_at,id"))
        await using (var reader = await command.ExecuteReaderAsync(ct)) while (await reader.ReadAsync(ct)) ids.Add(reader.GetGuid(0));
        var receipts = new List<ReceiptDto>();
        foreach (var id in ids)
        {
            var receipt = await ReceiptAsync(connection, null, id, false, ct);
            try { await ReadScopeAsync(actor, receipt, ct); receipts.Add(receipt); }
            catch (CaptureFault error) when (error.StatusCode == 403) { }
        }
        return receipts.ToArray();
    }

    public Task<ReceiptDto> UpdateReceiptAsync(Guid id, UpdateReceiptRequest request, CancellationToken ct) => MutationAsync("receipt-update:" + id, request.Version, request, async (connection, transaction, actor) =>
    {
        Permission(actor, "purchases.write");
        var current = await ReceiptAsync(connection, transaction, id, true, ct); Own(actor, current.OwnerOrganizationId);
        if (id != request.Id || current.OwnerOrganizationId != request.OwnerOrganizationId) throw new CaptureFault(400, "Receipt identity and ownership cannot change.");
        if (current.Status != "DRAFT" || current.ReplacementForReceiptId is not null) throw new CaptureFault(409, "Only an ordinary receipt draft may be edited.");
        if (current.Version != request.Version) throw new CaptureFault(409, "Receipt draft version is stale.");
        ValidateReceipt(request.Content); await ValidateSourcesAsync(connection, transaction, request.Content, ct);
        await ExecuteAsync(connection, transaction, "DELETE FROM capture.receipt_lines WHERE receipt_id=@id", ct, ("id", id));
        await ExecuteAsync(connection, transaction, "UPDATE capture.receipts SET farmer_id=@farmer,season_id=@season,location_id=@location,body=@body::jsonb,version=@version WHERE id=@id", ct,
            ("farmer", request.FarmerId), ("season", request.SeasonId), ("location", request.LocationId), ("body", Serialize(request.Content)), ("version", Guid.NewGuid()), ("id", id));
        await InsertLinesAsync(connection, transaction, request.Content, ct);
        return await ReceiptAsync(connection, transaction, id, false, ct);
    }, ct);

    private async Task PostReceiptAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Actor actor, ReceiptDto receipt, CancellationToken ct)
    {
        foreach (var line in receipt.Lines.OrderBy(x => x.Id))
        {
            var lot = Guid.NewGuid();
            await ledger.PostReceiptAsync(connection, transaction, actor.Id, new ReceiptStockLine(receipt.Id, line.Id, lot, line.LotCode,
                receipt.OwnerOrganizationId, line.CommodityId, receipt.LocationId, line.QuantityKg, line.Quantity, line.Unit, receipt.OccurredAt,
                receipt.ReplacementForReceiptId), ct);
            await ExecuteAsync(connection, transaction, "INSERT INTO capture.receipt_lots(receipt_line_id,lot_id) VALUES(@line,@lot)", ct, ("line", line.Id), ("lot", lot));
        }
        await ExecuteAsync(connection, transaction, "UPDATE capture.receipts SET status='COMMITTED',committed_at=now() WHERE id=@id", ct, ("id", receipt.Id));
    }

    public Task<ReceiptDto> CommitReceiptAsync(Guid id, CommitReceiptRequest request, CancellationToken ct) => MutationAsync("receipt-commit:" + id, request.CommandId, request, async (connection, transaction, actor) =>
    {
        Permission(actor, "purchases.write");
        var receipt = await ReceiptAsync(connection, transaction, id, true, ct); Own(actor, receipt.OwnerOrganizationId);
        if (receipt.Version != request.Version) throw new CaptureFault(409, "Receipt draft version is stale or conflicts with committed content.");
        if (receipt.ReplacementForReceiptId is not null) throw new CaptureFault(409, "Replacement receipt must commit inside its reconciled correction package.");
        if (receipt.Status == "COMMITTED") return receipt;
        await PostReceiptAsync(connection, transaction, actor, receipt, ct);
        return await ReceiptAsync(connection, transaction, id, false, ct);
    }, ct);

    public Task<object> StoreEvidenceAsync(EvidenceRequest request, CancellationToken ct) => MutationAsync<object>("evidence", request.Id, request, async (_, _, actor) =>
    {
        Id(request.OwnerOrganizationId); Id(request.LocationId); Text(request.FileName, "Evidence file name", 255); Text(request.MediaType, "Evidence media type", 120);
        if (request.FileName.Any(char.IsControl) || request.MediaType.Any(char.IsControl) ||
            !Microsoft.Net.Http.Headers.MediaTypeHeaderValue.TryParse(request.MediaType, out _))
            throw new CaptureFault(400, "Evidence needs a valid media type and a file name without control characters.");
        if (string.IsNullOrEmpty(request.ContentBase64) || request.ContentBase64.Length > 14 * 1024 * 1024) throw new CaptureFault(400, "Evidence must contain at most 10 MiB.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(request.ContentBase64); }
        catch (FormatException) { throw new CaptureFault(400, "Evidence content must be valid base64."); }
        if (bytes.Length is < 1 or > 10 * 1024 * 1024) throw new CaptureFault(400, "Evidence must contain between one byte and 10 MiB.");
        if (actor.OrganizationId != request.OwnerOrganizationId && !actor.Has("lots.receive") && !actor.Has("receiving.reconcile") && !actor.Has("corrections.reconcile"))
            throw new CaptureFault(403, "Evidence is outside company scope.");
        var facility = await ledger.GetLocationFacilityAsync(request.LocationId, ct);
        var id = await ledger.StoreEvidenceAsync(request.Id, request.OwnerOrganizationId, facility, request.FileName, request.MediaType, bytes, ct);
        return new { id, sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() };
    }, ct);

    private async Task EvidenceAsync(Guid evidence, ReceiptDto receipt, CancellationToken ct)
    {
        Id(evidence);
        var facility = await ledger.GetLocationFacilityAsync(receipt.LocationId, ct);
        if (!await ledger.CanAccessEvidenceAsync(evidence, receipt.OwnerOrganizationId, facility, ct))
            throw new CaptureFault(403, "Evidence must be durable and match receipt owner and assigned facility.");
    }

    private static async Task<(ReceiptDto Receipt, ReceiptLineDto Line)> ReceiptLineAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid lineId, bool locked, CancellationToken ct)
    {
        var value = await ScalarAsync(connection, transaction, "SELECT receipt_id FROM capture.receipt_lines WHERE id=@id", ct, ("id", lineId));
        if (value is not Guid receiptId) throw new CaptureFault(404, "Receipt line not found.");
        var receipt = await ReceiptAsync(connection, transaction, receiptId, locked, ct);
        if (receipt.Status != "COMMITTED") throw new CaptureFault(409, "Arrival observations require committed procurement.");
        var line = receipt.Lines.Single(x => x.Id == lineId);
        if (line.LotId is null) throw new CaptureFault(409, "Receipt line has no source lot.");
        return (receipt, line);
    }

    private async Task ReceiveScopeAsync(Actor actor, ReceiptDto receipt, string permission, CancellationToken ct)
    {
        Permission(actor, permission);
        if (!await ledger.CanAccessLocationAsync(receipt.LocationId, permission, ct)) throw new CaptureFault(403, "Receipt is outside assigned receiving facility.");
    }

    public Task<ArrivalDto> CreateArrivalAsync(ArrivalRequest request, CancellationToken ct) => MutationAsync("arrival", request.Id, request, async (connection, transaction, actor) =>
    {
        var (receipt, line) = await ReceiptLineAsync(connection, transaction, request.ReceiptLineId, true, ct);
        await ReceiveScopeAsync(actor, receipt, "lots.receive", ct); await EvidenceAsync(request.EvidenceId, receipt, ct);
        if (await ScalarAsync(connection, transaction, "SELECT EXISTS(SELECT 1 FROM capture.receipt_corrections WHERE receipt_id=@id AND status='COMMITTED')", ct, ("id", receipt.Id)) is true)
            throw new CaptureFault(409, "Arrival must reference replacement receipt after original receipt correction.");
        if (request.ObservedAt == default) throw new CaptureFault(400, "Arrival observation time is required.");
        var quantity = CaptureMeasure.ToKg(request.Quantity, request.Unit);
        if (CaptureMeasure.Kg(quantity) != line.QuantityKg)
            await ledger.SetReceivingBlockAsync(connection, transaction, actor.Id, request.Id, line.LotId!.Value, true, request.EvidenceId, "ARRIVAL_MISMATCH", ct);
        await ExecuteAsync(connection, transaction, "INSERT INTO capture.arrivals(id,receipt_line_id,body,quantity_kg,actor_id) VALUES(@id,@line,@body::jsonb,@kg,@actor)", ct,
            ("id", request.Id), ("line", request.ReceiptLineId), ("body", Serialize(request)), ("kg", quantity), ("actor", actor.Id));
        return await ArrivalAsync(connection, transaction, request.Id, ct);
    }, ct);

    private static async Task<ArrivalDto> ArrivalAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid id, CancellationToken ct)
    {
        var raw = await ScalarAsync(connection, transaction, "SELECT body::text FROM capture.arrivals WHERE id=@id", ct, ("id", id));
        if (raw is not string body) throw new CaptureFault(404, "Arrival observation not found.");
        var original = Decode<ArrivalRequest>(body);
        var (_, line) = await ReceiptLineAsync(connection, transaction, original.ReceiptLineId, false, ct);
        var corrections = new List<ArrivalCorrectionDto>();
        await using (var command = Command(connection, transaction, "SELECT body::text,created_at FROM capture.arrival_corrections WHERE arrival_id=@id ORDER BY sequence", ("id", id)))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
            {
                var correction = Decode<ArrivalCorrectionRequest>(reader.GetString(0));
                corrections.Add(new(correction.Id, correction.Quantity, correction.Unit, CaptureMeasure.Kg(CaptureMeasure.ToKg(correction.Quantity, correction.Unit)), correction.EvidenceId, correction.Reason, reader.GetFieldValue<DateTimeOffset>(1)));
            }
        var investigations = new List<ArrivalInvestigationDto>();
        await using (var command = Command(connection, transaction, "SELECT body::text,created_at FROM capture.arrival_investigations WHERE arrival_id=@id ORDER BY created_at,id", ("id", id)))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
            {
                var investigation = Decode<ArrivalInvestigationRequest>(reader.GetString(0));
                investigations.Add(new(investigation.Id, investigation.Cause, investigation.EvidenceId, investigation.Reason, reader.GetFieldValue<DateTimeOffset>(1)));
            }
        var kg = CaptureMeasure.Kg(CaptureMeasure.ToKg(original.Quantity, original.Unit));
        var latest = corrections.Count == 0 ? kg : corrections[^1].QuantityKg;
        var status = latest != line.QuantityKg ? "BLOCKED" : corrections.Count == 0 ? "MATCHED" : "RESOLVED";
        var receiptCorrected = await ScalarAsync(connection, transaction,
            "SELECT EXISTS(SELECT 1 FROM capture.receipt_corrections c JOIN capture.receipt_lines l ON l.receipt_id=c.receipt_id WHERE l.id=@line AND c.status='COMMITTED')",
            ct, ("line", original.ReceiptLineId));
        if (receiptCorrected is true) status = "RECEIPT_CORRECTED";
        return new(original.Id, original.ReceiptLineId, original.Quantity, original.Unit, kg, original.EvidenceId, original.ObservedAt,
            line.QuantityKg, status, corrections.ToArray(), investigations.ToArray());
    }

    private static async Task<ArrivalRequest> LockArrivalAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, CancellationToken ct)
    {
        var raw = await ScalarAsync(connection, transaction, "SELECT body::text FROM capture.arrivals WHERE id=@id FOR UPDATE", ct, ("id", id));
        if (raw is not string body) throw new CaptureFault(404, "Original arrival observation not found.");
        return Decode<ArrivalRequest>(body);
    }

    public Task<ArrivalDto> CorrectArrivalAsync(Guid id, ArrivalCorrectionRequest request, CancellationToken ct) => MutationAsync("arrival-correction:" + id, request.Id, request, async (connection, transaction, actor) =>
    {
        Text(request.Reason, "Measurement correction reason");
        var observation = await ArrivalAsync(connection, transaction, id, ct);
        var (receipt, line) = await ReceiptLineAsync(connection, transaction, observation.ReceiptLineId, true, ct);
        await LockArrivalAsync(connection, transaction, id, ct);
        observation = await ArrivalAsync(connection, transaction, id, ct);
        await ReceiveScopeAsync(actor, receipt, "receiving.reconcile", ct); await EvidenceAsync(request.EvidenceId, receipt, ct);
        if (observation.Investigations.Any(x => x.Cause == "REAL_DIFFERENCE"))
            throw new CaptureFault(409, "Genuine receiving loss or gain requires a separately approved adjustment rule and remains blocked.");
        if (await ScalarAsync(connection, transaction, "SELECT EXISTS(SELECT 1 FROM capture.receipt_corrections WHERE receipt_id=@id AND status='COMMITTED')", ct, ("id", receipt.Id)) is true)
            throw new CaptureFault(409, "Original observation cannot change after receipt replacement; record arrival against replacement source.");
        var quantity = CaptureMeasure.ToKg(request.Quantity, request.Unit);
        var blocked = CaptureMeasure.Kg(quantity) != line.QuantityKg;
        if (blocked || observation.Status == "BLOCKED")
            await ledger.SetReceivingBlockAsync(connection, transaction, actor.Id, id, line.LotId!.Value, blocked, request.EvidenceId,
                blocked ? "CORRECTED_ARRIVAL_STILL_DIFFERS" : "ARRIVAL_MEASUREMENT_CORRECTED", ct);
        await ExecuteAsync(connection, transaction, "INSERT INTO capture.arrival_corrections(id,arrival_id,body,quantity_kg,actor_id) VALUES(@id,@arrival,@body::jsonb,@kg,@actor)", ct,
            ("id", request.Id), ("arrival", id), ("body", Serialize(request)), ("kg", quantity), ("actor", actor.Id));
        return await ArrivalAsync(connection, transaction, id, ct);
    }, ct);

    public Task<ArrivalDto> InvestigateArrivalAsync(Guid id, ArrivalInvestigationRequest request, CancellationToken ct) => MutationAsync("arrival-investigation:" + id, request.Id, request, async (connection, transaction, actor) =>
    {
        if (request.Cause is not ("REAL_DIFFERENCE" or "UNRESOLVED")) throw new CaptureFault(400, "Only REAL_DIFFERENCE or UNRESOLVED may be recorded; procurement entry errors use receipt correction.");
        Text(request.Reason, "Investigation reason");
        var observation = await ArrivalAsync(connection, transaction, id, ct);
        var (receipt, line) = await ReceiptLineAsync(connection, transaction, observation.ReceiptLineId, true, ct);
        await LockArrivalAsync(connection, transaction, id, ct);
        await ReceiveScopeAsync(actor, receipt, "receiving.reconcile", ct); await EvidenceAsync(request.EvidenceId, receipt, ct);
        var current = await ArrivalAsync(connection, transaction, id, ct);
        if (current.Status != "BLOCKED") throw new CaptureFault(409, "Investigation requires an unresolved measurement difference.");
        await ledger.SetReceivingBlockAsync(connection, transaction, actor.Id, id, line.LotId!.Value, true, request.EvidenceId, request.Cause, ct);
        await ExecuteAsync(connection, transaction, "INSERT INTO capture.arrival_investigations(id,arrival_id,body,actor_id) VALUES(@id,@arrival,@body::jsonb,@actor)", ct,
            ("id", request.Id), ("arrival", id), ("body", Serialize(request)), ("actor", actor.Id));
        return await ArrivalAsync(connection, transaction, id, ct);
    }, ct);

    public async Task<ArrivalDto> GetArrivalAsync(Guid id, CancellationToken ct)
    {
        await using var connection = Connection(); await connection.OpenAsync(ct); var actor = await CurrentAsync(connection, null, ct);
        var arrival = await ArrivalAsync(connection, null, id, ct);
        var (receipt, _) = await ReceiptLineAsync(connection, null, arrival.ReceiptLineId, false, ct); await ReadScopeAsync(actor, receipt, ct);
        return arrival;
    }

    public async Task<ArrivalDto[]> ListArrivalsAsync(CancellationToken ct)
    {
        await using var connection = Connection(); await connection.OpenAsync(ct); var actor = await CurrentAsync(connection, null, ct);
        var ids = new List<Guid>();
        await using (var command = Command(connection, null, "SELECT id FROM capture.arrivals ORDER BY created_at,id"))
        await using (var reader = await command.ExecuteReaderAsync(ct)) while (await reader.ReadAsync(ct)) ids.Add(reader.GetGuid(0));
        var result = new List<ArrivalDto>();
        foreach (var id in ids)
        {
            var arrival = await ArrivalAsync(connection, null, id, ct); var (receipt, _) = await ReceiptLineAsync(connection, null, arrival.ReceiptLineId, false, ct);
            try { await ReadScopeAsync(actor, receipt, ct); result.Add(arrival); } catch (CaptureFault error) when (error.StatusCode == 403) { }
        }
        return result.ToArray();
    }

    public Task<ReceiptCorrectionDto> RequestReceiptCorrectionAsync(Guid id, ReceiptCorrectionRequest request, CancellationToken ct) => MutationAsync("receipt-correction:" + id, request.Id, request, async (connection, transaction, actor) =>
    {
        Permission(actor, "purchases.write"); Text(request.Reason, "Receipt correction reason");
        var original = await ReceiptAsync(connection, transaction, id, true, ct); Own(actor, original.OwnerOrganizationId);
        if (original.Status != "COMMITTED") throw new CaptureFault(409, "Only committed procurement may be corrected.");
        if (request.Replacement is null) throw new CaptureFault(400, "Corrected receipt content is required.");
        if (request.Replacement.OwnerOrganizationId != original.OwnerOrganizationId) throw new CaptureFault(409, "Wrong-owner corrections are excluded; ownership transfer is not supported.");
        if (request.Replacement.LocationId != original.LocationId) throw new CaptureFault(409, "Unused receipt correction cannot represent a physical transfer.");
        ValidateReceipt(request.Replacement);
        if (request.Replacement.FarmerId != original.FarmerId || request.Replacement.SeasonId != original.SeasonId ||
            request.Replacement.Lines.Length != original.Lines.Length ||
            request.Replacement.Lines.Where((line, index) => line.PlotSeasonId != original.Lines[index].PlotSeasonId ||
                line.CommodityId != original.Lines[index].CommodityId).Any())
            throw new CaptureFault(409, "Unused receipt correction preserves registered farmer, plot, season and commodity provenance.");
        await ValidateReceivingCorrectionAsync(connection, transaction, original, request.Replacement.Lines.Select(x =>
            CaptureMeasure.Kg(CaptureMeasure.ToKg(x.Quantity, x.Unit))).ToArray(), ct);
        await ledger.SetReceiptCorrectionBlockAsync(connection, transaction, actor.Id, request.Id, id, original.OwnerOrganizationId, ct);
        await InsertReceiptAsync(connection, transaction, actor, request.Replacement, id, ct);
        await ExecuteAsync(connection, transaction,
            "INSERT INTO capture.receipt_corrections(id,receipt_id,replacement_receipt_id,body,created_by) VALUES(@id,@receipt,@replacement,@body::jsonb,@actor)", ct,
            ("id", request.Id), ("receipt", id), ("replacement", request.Replacement.Id), ("body", Serialize(request)), ("actor", actor.Id));
        return await CorrectionAsync(connection, transaction, request.Id, false, ct);
    }, ct);

    private static async Task ValidateReceivingCorrectionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ReceiptDto original, string[] replacementKg, CancellationToken ct)
    {
        for (var index = 0; index < original.Lines.Length; index++)
        {
            var ids = new List<Guid>();
            await using (var command = Command(connection, transaction, "SELECT id FROM capture.arrivals WHERE receipt_line_id=@line ORDER BY id", ("line", original.Lines[index].Id)))
            await using (var reader = await command.ExecuteReaderAsync(ct)) while (await reader.ReadAsync(ct)) ids.Add(reader.GetGuid(0));
            foreach (var id in ids)
            {
                var arrival = await ArrivalAsync(connection, transaction, id, ct);
                if (arrival.Investigations.Any(x => x.Cause == "REAL_DIFFERENCE"))
                    throw new CaptureFault(409, "Genuine receiving loss or gain cannot be settled by unused receipt correction.");
                var measuredKg = arrival.Corrections.Length == 0 ? arrival.QuantityKg : arrival.Corrections[^1].QuantityKg;
                if (measuredKg != replacementKg[index])
                    throw new CaptureFault(409, "Corrected procurement must match each retained current arrival measurement; unresolved differences stay blocked.");
            }
        }
    }

    private static async Task<ReceiptCorrectionDto> CorrectionAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid id, bool locked, CancellationToken ct)
    {
        ReceiptCorrectionRequest request; Guid receipt; string status; Guid? evidence; ReceiptReconciliationRequest? reconciliation; DateTimeOffset created;
        await using (var command = Command(connection, transaction, "SELECT receipt_id,body::text,status,evidence_id,reconciliation::text,created_at FROM capture.receipt_corrections WHERE id=@id" + (locked ? " FOR UPDATE" : ""), ("id", id)))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw new CaptureFault(404, "Receipt correction not found.");
            receipt = reader.GetGuid(0); request = Decode<ReceiptCorrectionRequest>(reader.GetString(1)); status = reader.GetString(2);
            evidence = reader.IsDBNull(3) ? null : reader.GetGuid(3); reconciliation = reader.IsDBNull(4) ? null : Decode<ReceiptReconciliationRequest>(reader.GetString(4)); created = reader.GetFieldValue<DateTimeOffset>(5);
        }
        var replacement = await ReceiptAsync(connection, transaction, request.Replacement.Id, false, ct);
        return new(id, receipt, request.Reason, replacement.Id, replacement, status, evidence, reconciliation?.Reason,
            reconciliation?.VerifiedLines ?? [], created);
    }

    public Task<ReceiptCorrectionDto> ReconcileReceiptCorrectionAsync(Guid id, ReceiptReconciliationRequest request, CancellationToken ct) => MutationAsync("receipt-reconcile:" + id, request.CommandId, request, async (connection, transaction, actor) =>
    {
        Permission(actor, "corrections.reconcile"); Text(request.Reason, "Physical reconciliation reason");
        var correction = await CorrectionAsync(connection, transaction, id, true, ct);
        var original = await ReceiptAsync(connection, transaction, correction.ReceiptId, true, ct);
        await ReceiveScopeAsync(actor, original, "corrections.reconcile", ct); await EvidenceAsync(request.EvidenceId, original, ct);
        if (correction.Status != "REQUESTED") throw new CaptureFault(409, "Receipt reconciliation is already recorded and immutable.");
        if (request.VerifiedLines is null || request.VerifiedLines.Length != correction.Replacement.Lines.Length ||
            request.VerifiedLines.Any(x => x is null) || request.VerifiedLines.Select(x => x.ReceiptLineId).Distinct().Count() != request.VerifiedLines.Length)
            throw new CaptureFault(400, "Reconciliation must identify every corrected receipt line exactly once.");
        foreach (var line in correction.Replacement.Lines)
        {
            var verified = request.VerifiedLines.SingleOrDefault(x => x.ReceiptLineId == line.Id);
            if (verified is null || CaptureMeasure.Kg(CaptureMeasure.ToKg(verified.QuantityKg, "kg")) != line.QuantityKg)
                throw new CaptureFault(409, "Verified physical quantities must exactly match immutable replacement content.");
        }
        await ExecuteAsync(connection, transaction,
            "UPDATE capture.receipt_corrections SET status='RECONCILED',reconciliation=@body::jsonb,evidence_id=@evidence,reconciled_by=@actor WHERE id=@id", ct,
            ("body", Serialize(request)), ("evidence", request.EvidenceId), ("actor", actor.Id), ("id", id));
        return await CorrectionAsync(connection, transaction, id, false, ct);
    }, ct);

    public Task<ReceiptCorrectionDto> CommitReceiptCorrectionAsync(Guid id, CommitCorrectionRequest request, CancellationToken ct) => MutationAsync("receipt-correction-commit:" + id, request.CommandId, request, async (connection, transaction, actor) =>
    {
        Permission(actor, "corrections.reconcile");
        var correction = await CorrectionAsync(connection, transaction, id, true, ct);
        var original = await ReceiptAsync(connection, transaction, correction.ReceiptId, true, ct);
        await ReceiveScopeAsync(actor, original, "corrections.reconcile", ct);
        if (correction.Status == "COMMITTED") return correction;
        if (correction.Status != "RECONCILED" || correction.EvidenceId is not Guid evidence) throw new CaptureFault(409, "Immutable physical reconciliation is required before correction commit.");
        await EvidenceAsync(evidence, original, ct);
        var replacement = await ReceiptAsync(connection, transaction, correction.ReplacementReceiptId, true, ct);
        if (replacement.Status != "DRAFT" || replacement.OwnerOrganizationId != original.OwnerOrganizationId || replacement.LocationId != original.LocationId)
            throw new CaptureFault(409, "Replacement violates unused same-owner receipt correction contract.");
        await ValidateReceivingCorrectionAsync(connection, transaction, original, replacement.Lines.Select(x => x.QuantityKg).ToArray(), ct);
        await ledger.ReverseUnusedReceiptAsync(connection, transaction, actor.Id, id, original.Id, original.OwnerOrganizationId, evidence, correction.Reason, ct);
        await PostReceiptAsync(connection, transaction, actor, replacement, ct);
        await ExecuteAsync(connection, transaction, "UPDATE capture.receipt_corrections SET status='COMMITTED',committed_at=now() WHERE id=@id", ct, ("id", id));
        return await CorrectionAsync(connection, transaction, id, false, ct);
    }, ct);

    public async Task<ReceiptCorrectionDto> GetReceiptCorrectionAsync(Guid id, CancellationToken ct)
    {
        await using var connection = Connection(); await connection.OpenAsync(ct); var actor = await CurrentAsync(connection, null, ct);
        var correction = await CorrectionAsync(connection, null, id, false, ct);
        var original = await ReceiptAsync(connection, null, correction.ReceiptId, false, ct); await ReadScopeAsync(actor, original, ct);
        return correction;
    }

    public async Task<ReceiptCorrectionDto[]> ListReceiptCorrectionsAsync(CancellationToken ct)
    {
        await using var connection = Connection(); await connection.OpenAsync(ct); var actor = await CurrentAsync(connection, null, ct);
        var ids = new List<Guid>();
        await using (var command = Command(connection, null, "SELECT id FROM capture.receipt_corrections ORDER BY created_at,id"))
        await using (var reader = await command.ExecuteReaderAsync(ct)) while (await reader.ReadAsync(ct)) ids.Add(reader.GetGuid(0));
        var result = new List<ReceiptCorrectionDto>();
        foreach (var id in ids)
        {
            var correction = await CorrectionAsync(connection, null, id, false, ct); var original = await ReceiptAsync(connection, null, correction.ReceiptId, false, ct);
            try { await ReadScopeAsync(actor, original, ct); result.Add(correction); } catch (CaptureFault error) when (error.StatusCode == 403) { }
        }
        return result.ToArray();
    }
}
