using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GrainLedger.Modules.Identity;
using GrainLedger.Modules.Ledger;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace GrainLedger.Modules.MockCapture;

public static class MockCaptureSeed
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static Guid Id(int suffix) => Guid.Parse($"10000000-0000-0000-0000-{suffix:D12}");
    private sealed record Actor(Guid Id, Guid OrganizationId, string Role);
    private static string Serialize(object value) => JsonSerializer.Serialize(value, Json);
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(value))));

    public static async Task RunAsync(string connectionString, string environment, CancellationToken ct = default)
    {
        if (environment != Environments.Development)
            throw new InvalidOperationException("--seed-mock is only available in Development.");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:GrainLedgerMigration is required for explicit owner mock seeding.");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        try
        {
            await SeedAsync(connection, transaction, ct);
            await transaction.CommitAsync(ct);
        }
        catch (PostgresException error) when (error.SqlState is "42P01" or "42883" or "3F000")
        {
            throw new InvalidOperationException("--seed-mock requires existing Identity, Ledger and MockCapture migrations. Run --migrate separately in Development first.", error);
        }
        catch (PostgresException error) when (error.SqlState == "42501")
        {
            throw new InvalidOperationException("--seed-mock requires the explicit migration-owner connection with access to Identity, Ledger and MockCapture; runtime privileges are not changed.", error);
        }
        catch (PostgresException error) when (error.SqlState is "23505" or "23503" or "23514")
        {
            throw new InvalidOperationException("--seed-mock conflicts with existing data or protected receipt constraints; no seed changes committed. " + error.MessageText, error);
        }
    }

    private static async Task SeedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        // ponytail: same global stock lock as live commands; per-resource locks only if throughput requires.
        await ScalarAsync(connection, transaction, "SELECT pg_advisory_xact_lock(714928352)", ct);
        var admin = await ActorAsync(connection, transaction, "admin", Roles.Administrator, "Mill", ct);
        var purchasing = await ActorAsync(connection, transaction, "purchasing", Roles.PurchasingStaff, "Customer", ct);
        var manager = await ActorAsync(connection, transaction, "mill.manager", Roles.MillManager, "Mill", ct);
        var operatorActor = await ActorAsync(connection, transaction, "mill.operator", Roles.MillOperator, "Mill", ct);
        if (admin.OrganizationId != manager.OrganizationId || admin.OrganizationId != operatorActor.OrganizationId)
            throw new InvalidOperationException("--seed-mock requires demo administrator, manager and operator in the same mill organization. Existing access is not changed.");

        var owner = purchasing.OrganizationId;
        var facility = new { id = Id(1), name = "SYNTHETIC MOCK demo mill" };
        var store = new { id = Id(2), facilityId = Id(1), name = "SYNTHETIC MOCK store", kind = "STORE" };
        var isolation = new { id = Id(3), facilityId = Id(1), name = "SYNTHETIC MOCK isolation", kind = "ISOLATION" };
        var equipment = new { id = Id(4), facilityId = Id(1), name = "SYNTHETIC MOCK equipment" };
        var commodity = new { id = Id(5), name = "SYNTHETIC MOCK paddy" };
        var farmer = new FarmerRequest(Id(6), owner, "SYNTHETIC MOCK farmer", null);
        var plot = new PlotRequest(Id(7), farmer.Id, "SYNTHETIC MOCK plot", "10.123456", "106.123456");
        var season = new SeasonRequest(Id(8), owner, "SYNTHETIC MOCK season", new(2026, 1, 1), new(2026, 12, 31));
        var plotSeason = new PlotSeasonRequest(Id(9), plot.Id, season.Id);
        var occurredAt = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
        var receipt = new ReceiptRequest(Id(10), owner, farmer.Id, season.Id, store.id, occurredAt,
            [new(Id(11), plotSeason.Id, commodity.id, "MOCK-DEV-001", "1000", "kg")]);
        var draft = new ReceiptRequest(Id(12), owner, farmer.Id, season.Id, store.id, occurredAt,
            [new(Id(13), plotSeason.Id, commodity.id, "MOCK-DEV-DRAFT-001", "200", "kg")]);
        var content = new { admin, purchasing, manager, operatorActor, facility, store, isolation, equipment, commodity,
            farmer, plot, season, plotSeason, receipt, draft, commitId = Id(14), lotId = Id(15), receiptVersion = Id(16), draftVersion = Id(17) };
        var hash = Hash(content);
        var replay = false;
        await using (var command = Command(connection, transaction,
            "SELECT actor_id,actor_organization_id,actor_role,content_hash FROM capture_mock.commands WHERE kind='seed-mock' AND id=@id", ("id", Id(18))))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                if (reader.GetGuid(0) != purchasing.Id || reader.GetGuid(1) != owner || reader.GetString(2) != purchasing.Role || reader.GetString(3) != hash)
                    throw Collision("seed command content or persisted actor access");
                replay = true;
            }
        }

        await SetupAsync(connection, transaction, admin, "facilities", facility.id, facility,
            "SELECT jsonb_build_object('id',id,'name',name)=@body::jsonb FROM ledger.facilities WHERE id=@id", facility, replay, ct);
        await SetupAsync(connection, transaction, admin, "locations", store.id, store,
            "SELECT jsonb_build_object('id',id,'facilityId',facility_id,'name',name,'kind',kind)=@body::jsonb FROM ledger.locations WHERE id=@id", store, replay, ct);
        await SetupAsync(connection, transaction, admin, "locations", isolation.id, isolation,
            "SELECT jsonb_build_object('id',id,'facilityId',facility_id,'name',name,'kind',kind)=@body::jsonb FROM ledger.locations WHERE id=@id", isolation, replay, ct);
        await SetupAsync(connection, transaction, admin, "equipment", equipment.id, equipment,
            "SELECT jsonb_build_object('id',id,'facilityId',facility_id,'name',name)=@body::jsonb FROM ledger.equipment WHERE id=@id", equipment, replay, ct);
        await SetupAsync(connection, transaction, admin, "commodities", commodity.id, commodity,
            "SELECT jsonb_build_object('id',id,'name',name)=@body::jsonb FROM ledger.commodities WHERE id=@id", commodity, replay, ct);
        foreach (var staff in new[] { manager, operatorActor })
        {
            var assigned = await ScalarAsync(connection, transaction,
                "SELECT EXISTS(SELECT 1 FROM ledger.assignments WHERE user_id=@user AND facility_id=@facility)", ct,
                ("user", staff.Id), ("facility", facility.id));
            if (replay && assigned is not true) throw Collision("staff assignment removed");
            if (assigned is not true)
                await LedgerCommandAsync(connection, transaction, admin.Id, "assignments", new { userId = staff.Id, facilityId = facility.id }, ct);
        }

        await SourceAsync(connection, transaction, purchasing, "farmer", "farmers", farmer.Id, farmer,
            "INSERT INTO capture_mock.farmers(id,owner_id,body) VALUES(@id,@owner,@body::jsonb)", replay, ct, ("owner", owner));
        await SourceAsync(connection, transaction, purchasing, "plot", "plots", plot.Id, plot,
            "INSERT INTO capture_mock.plots(id,owner_id,farmer_id,body,latitude,longitude) VALUES(@id,@owner,@farmer,@body::jsonb,@lat,@lng)", replay, ct,
            ("owner", owner), ("farmer", farmer.Id), ("lat", 10.123456m), ("lng", 106.123456m));
        await SourceAsync(connection, transaction, purchasing, "season", "seasons", season.Id, season,
            "INSERT INTO capture_mock.seasons(id,owner_id,body,starts_on,ends_on) VALUES(@id,@owner,@body::jsonb,@start,@end)", replay, ct,
            ("owner", owner), ("start", season.StartsOn), ("end", season.EndsOn));
        await SourceAsync(connection, transaction, purchasing, "plot-season", "plot_seasons", plotSeason.Id, plotSeason,
            "INSERT INTO capture_mock.plot_seasons(id,owner_id,farmer_id,plot_id,season_id,body) VALUES(@id,@owner,@farmer,@plot,@season,@body::jsonb)", replay, ct,
            ("owner", owner), ("farmer", farmer.Id), ("plot", plot.Id), ("season", season.Id));
        await ReceiptAsync(connection, transaction, purchasing, receipt, Id(16), replay, ct);
        await ReceiptAsync(connection, transaction, purchasing, draft, Id(17), replay, ct);

        // Completed marker is immutable and commits with whole package. Never replenish moved, processed or corrected stock.
        if (replay) return;
        var line = receipt.Lines[0];
        var stock = new ReceiptStockLine(receipt.Id, line.Id, Id(15), line.LotCode, owner, line.CommodityId,
            receipt.LocationId, "1000", line.Quantity, line.Unit, receipt.OccurredAt);
        await ScalarAsync(connection, transaction, "SELECT ledger.receipt(@actor,@body::jsonb)", ct,
            ("actor", purchasing.Id), ("body", Serialize(stock)));
        await ScalarAsync(connection, transaction,
            "INSERT INTO capture_mock.receipt_lots(receipt_line_id,lot_id) VALUES(@line,@lot)", ct, ("line", line.Id), ("lot", stock.LotId));
        await ScalarAsync(connection, transaction,
            "UPDATE capture_mock.receipts SET status='COMMITTED',committed_at=now() WHERE id=@id", ct, ("id", receipt.Id));
        await RecordCommandAsync(connection, transaction, purchasing, "receipt-commit:" + receipt.Id, Id(14),
            new CommitReceiptRequest(Id(14), Id(16)), ReceiptDto(receipt, Id(16), stock.LotId), ct);
        await RecordCommandAsync(connection, transaction, purchasing, "seed-mock", Id(18), content,
            new { receiptId = receipt.Id, draftId = draft.Id, lotId = stock.LotId }, ct);
    }

    private static async Task<Actor> ActorAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string alias, string role, string kind, CancellationToken ct)
    {
        var email = alias + "@grainledger.local";
        await using var command = Command(connection, transaction, """
            SELECT u."Id",u."OrganizationId",u."Role",u."IsActive",o."Kind",o."Name"
            FROM identity.users u JOIN identity.organizations o ON o."Id"=u."OrganizationId"
            WHERE u."Provider"='local' AND u."NormalizedEmail"=@email FOR SHARE OF u,o
            """, ("email", LocalAuthService.NormalizeEmail(email)));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.IsDBNull(2) || reader.GetString(2) != role || !reader.GetBoolean(3) || reader.GetString(4) != kind
            || (kind == "Customer" && reader.GetString(5) != "GrainLedger Demo Customer"))
            throw new InvalidOperationException($"--seed-mock requires active local {email} with {role} role in the seeded {kind} organization. Run --seed-dev separately; existing access is not repaired.");
        var actor = new Actor(reader.GetGuid(0), reader.GetGuid(1), role);
        if (await reader.ReadAsync(ct)) throw Collision("duplicate demo account");
        return actor;
    }

    private static async Task SetupAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Actor admin,
        string action, Guid id, object payload, string equalitySql, object expected, bool replay, CancellationToken ct)
    {
        var equal = await ScalarAsync(connection, transaction, equalitySql, ct, ("id", id), ("body", Serialize(expected)));
        if (equal is false || (replay && equal is null)) throw Collision(action + " " + id);
        if (equal is null) await LedgerCommandAsync(connection, transaction, admin.Id, action, payload, ct);
    }

    private static async Task SourceAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Actor actor,
        string kind, string table, Guid id, object body, string insertSql, bool replay, CancellationToken ct,
        params (string Key, object Value)[] parameters)
    {
        var equal = await ScalarAsync(connection, transaction, $"SELECT body=@body::jsonb FROM capture_mock.{table} WHERE id=@id", ct,
            ("id", id), ("body", Serialize(body)));
        if (equal is false || (replay && equal is null) || (!replay && equal is not null)) throw Collision(kind + " " + id);
        if (replay) return;
        await ScalarAsync(connection, transaction, insertSql, ct, [("id", id), ("body", Serialize(body)), .. parameters]);
        await RecordCommandAsync(connection, transaction, actor, kind, id, body, body, ct);
    }

    private static async Task ReceiptAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Actor actor,
        ReceiptRequest receipt, Guid version, bool replay, CancellationToken ct)
    {
        var equal = await ScalarAsync(connection, transaction, """
            SELECT body=@body::jsonb AND created_by=@actor AND replacement_for IS NULL
            FROM capture_mock.receipts WHERE id=@id
            """, ct, ("id", receipt.Id), ("body", Serialize(receipt)), ("actor", actor.Id));
        if (equal is false || (replay && equal is null) || (!replay && equal is not null)) throw Collision("receipt " + receipt.Id);
        if (replay) return;
        await ScalarAsync(connection, transaction, """
            INSERT INTO capture_mock.receipts(id,owner_id,farmer_id,season_id,location_id,body,version,created_by)
            VALUES(@id,@owner,@farmer,@season,@location,@body::jsonb,@version,@actor)
            """, ct, ("id", receipt.Id), ("owner", receipt.OwnerOrganizationId), ("farmer", receipt.FarmerId),
            ("season", receipt.SeasonId), ("location", receipt.LocationId), ("body", Serialize(receipt)), ("version", version), ("actor", actor.Id));
        foreach (var line in receipt.Lines)
            await ScalarAsync(connection, transaction, """
                INSERT INTO capture_mock.receipt_lines(id,receipt_id,owner_id,farmer_id,season_id,plot_season_id,commodity_id,body,quantity_kg)
                VALUES(@id,@receipt,@owner,@farmer,@season,@plot,@commodity,@body::jsonb,@kg)
                """, ct, ("id", line.Id), ("receipt", receipt.Id), ("owner", receipt.OwnerOrganizationId), ("farmer", receipt.FarmerId),
                ("season", receipt.SeasonId), ("plot", line.PlotSeasonId), ("commodity", line.CommodityId), ("body", Serialize(line)),
                ("kg", MockCaptureMeasure.ToKg(line.Quantity, line.Unit)));
        await RecordCommandAsync(connection, transaction, actor, "receipt", receipt.Id, receipt, ReceiptDto(receipt, version, null), ct);
    }

    private static ReceiptDto ReceiptDto(ReceiptRequest receipt, Guid version, Guid? lotId) => new(receipt.Id,
        receipt.OwnerOrganizationId, receipt.FarmerId, receipt.SeasonId, receipt.LocationId, receipt.OccurredAt,
        receipt.Lines.Select(line => new ReceiptLineDto(line.Id, line.PlotSeasonId, line.CommodityId, line.LotCode,
            line.Quantity, line.Unit, MockCaptureMeasure.Kg(MockCaptureMeasure.ToKg(line.Quantity, line.Unit)), lotId)).ToArray(),
        lotId is null ? "DRAFT" : "COMMITTED", version, null);

    private static Task<object?> LedgerCommandAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid actorId, string action, object body, CancellationToken ct) =>
        ScalarAsync(connection, transaction, "SELECT ledger.command(@actor,@action,@body::jsonb)", ct,
            ("actor", actorId), ("action", action), ("body", Serialize(body)));

    private static Task<object?> RecordCommandAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Actor actor, string kind, Guid id, object content, object response, CancellationToken ct) =>
        ScalarAsync(connection, transaction, """
            INSERT INTO capture_mock.commands(kind,id,actor_id,actor_organization_id,actor_role,content_hash,response)
            VALUES(@kind,@id,@actor,@organization,@role,@hash,@response::jsonb)
            """, ct, ("kind", kind), ("id", id), ("actor", actor.Id), ("organization", actor.OrganizationId),
            ("role", actor.Role), ("hash", Hash(content)), ("response", Serialize(response)));

    private static InvalidOperationException Collision(string record) =>
        new($"--seed-mock content collision: {record}. Existing data is not overwritten or recreated; no seed changes committed.");

    private static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql,
        params (string Key, object Value)[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (key, value) in parameters) command.Parameters.AddWithValue(key, value);
        return command;
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql,
        CancellationToken ct, params (string Key, object Value)[] parameters)
    {
        await using var command = Command(connection, transaction, sql, parameters);
        var value = await command.ExecuteScalarAsync(ct);
        return value is DBNull ? null : value;
    }
}
