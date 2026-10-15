using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrainLedger.Modules.Identity;
using Npgsql;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class LedgerManagementTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("facilities")]
    [InlineData("locations")]
    [InlineData("equipment")]
    public async Task Setup_records_require_creator_and_current_version_for_update_and_delete(string resource)
    {
        using var s = await CreateAsync(factory, policies: false);
        var id = Guid.NewGuid();
        var created = await SendAsync(s.Admin, HttpMethod.Post, $"/api/ledger/{resource}", SetupBody(resource, id, s.FacilityId));
        var version = created.GetProperty("version").GetString();
        Assert.Equal(s.Accounts["admin"].User.Id, created.GetProperty("createdBy").GetGuid());
        var (other, session) = await factory.SeedUserAsync(Roles.Administrator, s.Accounts["admin"].User.OrganizationId);
        using var otherAdmin = factory.CreateRealCookieClient(other, session);
        await CsrfAsync(otherAdmin);
        await RejectedAsync(otherAdmin, HttpMethod.Put, $"/api/ledger/{resource}/{id}", new { name = "Unauthorized", version }, HttpStatusCode.Forbidden);
        await RejectedAsync(otherAdmin, HttpMethod.Delete, $"/api/ledger/{resource}/{id}", new { version }, HttpStatusCode.Forbidden);
        await RejectedAsync(s.Admin, HttpMethod.Put, $"/api/ledger/{resource}/{id}", new { name = "No version" }, HttpStatusCode.BadRequest);
        await RejectedAsync(s.Admin, HttpMethod.Delete, $"/api/ledger/{resource}/{id}", new { version = 7 }, HttpStatusCode.BadRequest);
        await RejectedAsync(s.Admin, HttpMethod.Put, $"/api/ledger/{resource}/{id}", new { name = "Stale", version = Guid.NewGuid() }, HttpStatusCode.Conflict);
        var edited = await SendAsync(s.Admin, HttpMethod.Put, $"/api/ledger/{resource}/{id}", new { name = "Renamed", version });
        Assert.Equal("Renamed", edited.GetProperty("name").GetString());
        Assert.NotEqual(version, edited.GetProperty("version").GetString());
        await RejectedAsync(s.Admin, HttpMethod.Delete, $"/api/ledger/{resource}/{id}", new { version }, HttpStatusCode.Conflict);
        await SendAsync(s.Admin, HttpMethod.Delete, $"/api/ledger/{resource}/{id}", new { version = edited.GetProperty("version").GetString() });
        Assert.DoesNotContain((await GetAsync(s.Admin, "/api/ledger/setup")).GetProperty(resource).EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
        await AssertRetainedAsync(resource, id, edited.GetProperty("version").GetString(), s.Accounts["admin"].User.Id);
        Assert.DoesNotContain((await GetAsync(s.Admin, "/api/ledger/lookups")).GetProperty(resource).EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
        await RejectedAsync(s.Admin, HttpMethod.Put, $"/api/ledger/{resource}/{id}", new { name = "Restore denied", version = edited.GetProperty("version").GetString() }, HttpStatusCode.Conflict);
        await RejectedAsync(s.Admin, HttpMethod.Post, $"/api/ledger/{resource}", SetupBody(resource, id, s.FacilityId), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Setup_version_serializes_competing_edits_without_lost_update()
    {
        using var s = await CreateAsync(factory, policies: false);
        var id = Guid.NewGuid();
        var created = await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/facilities", new { id, name = "Race fixture" });
        var version = created.GetProperty("version").GetString();
        var responses = await Task.WhenAll(
            RawAsync(s.Admin, HttpMethod.Put, $"/api/ledger/facilities/{id}", new { name = "First", version }),
            RawAsync(s.Admin, HttpMethod.Put, $"/api/ledger/facilities/{id}", new { name = "Second", version }));
        using var first = responses[0];
        using var second = responses[1];
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Contains((await SetupRowAsync(s, "facilities", id)).GetProperty("name").GetString(), new[] { "First", "Second" });
    }

    [Fact]
    public async Task Pending_procurement_and_draft_references_block_structural_changes_but_not_names()
    {
        using var s = await CreateAsync(factory, policies: false);
        var procurement = await SendAsync(s.Purchaser, HttpMethod.Post, "/api/procurements", s.ReceiptBody(Guid.NewGuid(), ["10"]));
        var procurementId = procurement.GetProperty("id").GetGuid();
        var sourceSnapshot = await OwnerScalarAsync("SELECT payload::text FROM capture.business_events WHERE id=(SELECT event_id FROM capture.procurements WHERE id=$1)", procurementId);
        var location = await SetupRowAsync(s, "locations", s.LocationId);
        var locationVersion = location.GetProperty("version").GetString();
        await RejectedAsync(s.Admin, HttpMethod.Put, $"/api/ledger/locations/{s.LocationId}", new { name = "Keep source", kind = "ISOLATION", version = locationVersion }, HttpStatusCode.Conflict);
        await RejectedAsync(s.Admin, HttpMethod.Put, $"/api/ledger/locations/{s.LocationId}", new { name = "Keep source", facilityId = s.ForeignFacilityId, version = locationVersion }, HttpStatusCode.Conflict);
        var renamed = await SendAsync(s.Admin, HttpMethod.Put, $"/api/ledger/locations/{s.LocationId}", new { name = "Safe renamed source", version = locationVersion });
        var locationState = await OwnerScalarAsync("SELECT row_to_json(x)::text FROM ledger.locations x WHERE id=$1", s.LocationId);
        await RejectedAsync(s.Admin, HttpMethod.Delete, $"/api/ledger/locations/{s.LocationId}", new { version = renamed.GetProperty("version").GetString() }, HttpStatusCode.Conflict);
        Assert.Equal(locationState, await OwnerScalarAsync("SELECT row_to_json(x)::text FROM ledger.locations x WHERE id=$1", s.LocationId));
        var facility = await SetupRowAsync(s, "facilities", s.FacilityId);
        var renamedFacility = await SendAsync(s.Admin, HttpMethod.Put, $"/api/ledger/facilities/{s.FacilityId}", new { name = "Safe mill rename", version = facility.GetProperty("version").GetString() });
        var facilityState = await OwnerScalarAsync("SELECT row_to_json(x)::text FROM ledger.facilities x WHERE id=$1", s.FacilityId);
        await RejectedAsync(s.Admin, HttpMethod.Delete, $"/api/ledger/facilities/{s.FacilityId}", new { version = renamedFacility.GetProperty("version").GetString() }, HttpStatusCode.Conflict);
        Assert.Equal(facilityState, await OwnerScalarAsync("SELECT row_to_json(x)::text FROM ledger.facilities x WHERE id=$1", s.FacilityId));
        var source = Lots(await s.ReceiptAsync("10"))[0];
        await s.DraftAsync("DRYING", [(source, "10")], [("DRIED_PADDY", "10")]);
        var equipment = await SetupRowAsync(s, "equipment", s.EquipmentId);
        await RejectedAsync(s.Admin, HttpMethod.Put, $"/api/ledger/equipment/{s.EquipmentId}", new { name = "Keep equipment", facilityId = s.ForeignFacilityId, version = equipment.GetProperty("version").GetString() }, HttpStatusCode.Conflict);
        var renamedEquipment = await SendAsync(s.Admin, HttpMethod.Put, $"/api/ledger/equipment/{s.EquipmentId}", new { name = "Safe equipment rename", version = equipment.GetProperty("version").GetString() });
        var equipmentState = await OwnerScalarAsync("SELECT row_to_json(x)::text FROM ledger.equipment x WHERE id=$1", s.EquipmentId);
        await RejectedAsync(s.Admin, HttpMethod.Delete, $"/api/ledger/equipment/{s.EquipmentId}", new { version = renamedEquipment.GetProperty("version").GetString() }, HttpStatusCode.Conflict);
        Assert.Equal(equipmentState, await OwnerScalarAsync("SELECT row_to_json(x)::text FROM ledger.equipment x WHERE id=$1", s.EquipmentId));
        Assert.Equal(sourceSnapshot, await OwnerScalarAsync("SELECT payload::text FROM capture.business_events WHERE id=(SELECT event_id FROM capture.procurements WHERE id=$1)", procurementId));
        Assert.Equal(true, await OwnerScalarAsync("SELECT ledger.procurement_package_complete($1)", procurementId));
    }

    [Fact]
    public async Task Assignment_composite_update_preserves_creator_and_rejects_stale_or_invalid_targets()
    {
        using var s = await CreateAsync(factory, policies: false);
        var operatorId = s.Accounts["operator"].User.Id;
        var assignment = (await GetAsync(s.Admin, "/api/ledger/setup")).GetProperty("assignments").EnumerateArray()
            .Single(x => x.GetProperty("userId").GetGuid() == operatorId && x.GetProperty("facilityId").GetGuid() == s.FacilityId);
        var version = assignment.GetProperty("version").GetString();
        var route = $"/api/ledger/assignments/{operatorId}/{s.FacilityId}";
        var (other, session) = await factory.SeedUserAsync(Roles.Administrator, s.Accounts["admin"].User.OrganizationId);
        using var otherAdmin = factory.CreateRealCookieClient(other, session);
        await CsrfAsync(otherAdmin);
        var replay = await SendAsync(otherAdmin, HttpMethod.Post, "/api/ledger/assignments", new { userId = operatorId, facilityId = s.FacilityId });
        Assert.Equal(s.Accounts["admin"].User.Id, replay.GetProperty("createdBy").GetGuid());
        Assert.False(replay.GetProperty("canEdit").GetBoolean());
        await RejectedAsync(otherAdmin, HttpMethod.Delete, route, new { version }, HttpStatusCode.Forbidden);
        await RejectedAsync(s.Admin, HttpMethod.Put, route, new { version, newUserId = s.Accounts["customer"].User.Id }, HttpStatusCode.Conflict);
        await RejectedAsync(s.Admin, HttpMethod.Put, route, new { version = Guid.NewGuid(), newFacilityId = s.ForeignFacilityId }, HttpStatusCode.Conflict);
        var edited = await SendAsync(s.Admin, HttpMethod.Put, route, new { version, newFacilityId = s.ForeignFacilityId });
        Assert.Equal(operatorId, edited.GetProperty("userId").GetGuid());
        Assert.Equal(s.ForeignFacilityId, edited.GetProperty("facilityId").GetGuid());
        var newRoute = $"/api/ledger/assignments/{operatorId}/{s.ForeignFacilityId}";
        await RejectedAsync(s.Admin, HttpMethod.Delete, newRoute, new { version }, HttpStatusCode.Conflict);
        await SendAsync(s.Admin, HttpMethod.Delete, newRoute, new { version = edited.GetProperty("version").GetString() });
        Assert.DoesNotContain((await GetAsync(s.Admin, "/api/ledger/setup")).GetProperty("assignments").EnumerateArray(), x => x.GetProperty("userId").GetGuid() == operatorId);
        Assert.Equal(true, await OwnerScalarAsync("SELECT is_deleted FROM ledger.assignments WHERE user_id=$1 AND facility_id=$2", operatorId, s.ForeignFacilityId));
        Assert.Equal(false, await OwnerScalarAsync("SELECT ledger.assigned($1,$2)", operatorId, s.ForeignFacilityId));
        Assert.Equal(s.Accounts["admin"].User.Id, await OwnerScalarAsync("SELECT created_by FROM ledger.assignments WHERE user_id=$1 AND facility_id=$2", operatorId, s.ForeignFacilityId));
        Assert.NotEqual(edited.GetProperty("version").GetString(), await OwnerScalarAsync("SELECT version::text FROM ledger.assignments WHERE user_id=$1 AND facility_id=$2", operatorId, s.ForeignFacilityId));
        await RejectedAsync(s.Admin, HttpMethod.Post, "/api/ledger/assignments", new { userId = operatorId, facilityId = s.ForeignFacilityId }, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Draft_update_delete_require_creator_scope_and_current_version()
    {
        using var s = await CreateAsync(factory);
        var source = Lots(await s.ReceiptAsync("10"))[0];
        var draft = await s.DraftAsync("DRYING", [(source, "10")], [("DRIED_PADDY", "10")]);
        var id = draft.GetProperty("id").GetGuid();
        var version = draft.GetProperty("version").GetString();
        var (other, session) = await factory.SeedUserAsync(Roles.MillOperator, s.Accounts["operator"].User.OrganizationId);
        using var otherOperator = factory.CreateRealCookieClient(other, session);
        await CsrfAsync(otherOperator);
        await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/assignments", new { userId = other.Id, facilityId = s.FacilityId });
        var body = DraftEditBody(s, id, source, version);
        await RejectedAsync(otherOperator, HttpMethod.Put, $"/api/ledger/operations/{id}", body, HttpStatusCode.Forbidden);
        await RejectedAsync(otherOperator, HttpMethod.Delete, $"/api/ledger/operations/{id}", new { version }, HttpStatusCode.Forbidden);
        body.Remove("version");
        await RejectedAsync(s.Operator, HttpMethod.Put, $"/api/ledger/operations/{id}", body, HttpStatusCode.BadRequest);
        body["version"] = Guid.NewGuid().ToString();
        await RejectedAsync(s.Operator, HttpMethod.Put, $"/api/ledger/operations/{id}", body, HttpStatusCode.Conflict);
        body["version"] = version;
        var edited = await SendAsync(s.Operator, HttpMethod.Put, $"/api/ledger/operations/{id}", body);
        Assert.NotEqual(version, edited.GetProperty("version").GetString());
        Assert.Equal(s.Accounts["operator"].User.Id, edited.GetProperty("createdBy").GetGuid());
        await RejectedAsync(s.Operator, HttpMethod.Delete, $"/api/ledger/operations/{id}", new { version }, HttpStatusCode.Conflict);
        await SendAsync(s.Operator, HttpMethod.Delete, $"/api/ledger/operations/{id}", new { version = edited.GetProperty("version").GetString() });
        await RejectedAsync(s.Operator, HttpMethod.Put, $"/api/ledger/operations/{id}", DraftEditBody(s, id, source, edited.GetProperty("version").GetString()), HttpStatusCode.Conflict);
        await AssertRetainedAsync("operations", id, edited.GetProperty("version").GetString(), s.Accounts["operator"].User.Id);
        Assert.Empty((await GetAsync(s.Operator, $"/api/ledger/operations?ids={id}")).GetProperty("items").EnumerateArray());
        using var detail = await s.Operator.GetAsync($"/api/ledger/operations/{id}");
        Assert.Equal(HttpStatusCode.Forbidden, detail.StatusCode);
        await RejectedAsync(s.Operator, HttpMethod.Post, $"/api/ledger/operations/{id}/commit", new { }, HttpStatusCode.Conflict);
        await RejectedAsync(s.Manager, HttpMethod.Post, $"/api/ledger/operations/{id}/confirm", new { version = edited.GetProperty("version").GetString(), evidenceId = s.EvidenceId }, HttpStatusCode.Conflict);
        await RejectedAsync(s.Operator, HttpMethod.Post, "/api/ledger/operations", s.OperationBody(id, "DRYING", [(source, "10")], [("DRIED_PADDY", "10")]), HttpStatusCode.Conflict);
        Assert.Equal(10m, Quantity(await s.LotAsync(source)));
    }

    [Fact]
    public async Task Confirmation_history_and_correction_references_prevent_draft_deletion()
    {
        using var s = await CreateAsync(factory, policies: false);
        await s.PolicyAsync("DRYING", "1");
        var source = Lots(await s.ReceiptAsync("100"))[0];
        var draft = await s.DraftAsync("DRYING", [(source, "100")], [("DRIED_PADDY", "99")]);
        var id = draft.GetProperty("id").GetGuid();
        var version = draft.GetProperty("version").GetString();
        await SendAsync(s.Manager, HttpMethod.Post, $"/api/ledger/operations/{id}/confirm", new { version, evidenceId = s.EvidenceId });
        var edit = JsonSerializer.SerializeToNode(s.OperationBody(id, "DRYING", [(source, "100")], [("DRIED_PADDY", "99.5")]))!.AsObject();
        edit["version"] = version;
        var edited = await SendAsync(s.Operator, HttpMethod.Put, $"/api/ledger/operations/{id}", edit);
        Assert.Equal(JsonValueKind.Null, edited.GetProperty("confirmedVersion").ValueKind);
        Assert.Equal(JsonValueKind.Null, edited.GetProperty("confirmedBy").ValueKind);
        await RejectedAsync(s.Operator, HttpMethod.Delete, $"/api/ledger/operations/{id}", new { version = edited.GetProperty("version").GetString() }, HttpStatusCode.Conflict);
        var original = await s.CommitAsync(await s.DraftAsync("DRYING", [(source, "100")], [("DRIED_PADDY", "100")]));
        var replacement = await s.DraftAsync("DRYING", [(source, "100")], [("DRIED_PADDY", "99")], "1");
        await s.CorrectionAsync(original, replacement);
        var replacementId = replacement.GetProperty("id").GetGuid();
        await RejectedAsync(s.Operator, HttpMethod.Delete, $"/api/ledger/operations/{replacementId}", new { version = replacement.GetProperty("version").GetString() }, HttpStatusCode.Conflict);
        var originalId = original.GetProperty("id").GetGuid();
        await RejectedAsync(s.Operator, HttpMethod.Delete, $"/api/ledger/operations/{originalId}", new { version = original.GetProperty("version").GetString() }, HttpStatusCode.Conflict);
        await RejectedAsync(s.Operator, HttpMethod.Put, $"/api/ledger/operations/{originalId}", DraftEditBody(s, originalId, source, original.GetProperty("version").GetString()), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Legacy_creator_null_is_immutable_and_duplicate_assignment_cannot_claim_it()
    {
        using var s = await CreateAsync(factory);
        var source = Lots(await s.ReceiptAsync("10"))[0];
        var draft = await s.DraftAsync("DRYING", [(source, "10")], [("DRIED_PADDY", "10")]);
        var id = draft.GetProperty("id").GetGuid();
        await OwnerSqlAsync("UPDATE ledger.facilities SET created_by=NULL WHERE id=$1", s.FacilityId);
        await OwnerSqlAsync("UPDATE ledger.operations SET created_by=NULL WHERE id=$1", id);
        await OwnerSqlAsync("UPDATE ledger.assignments SET created_by=NULL WHERE user_id=$1 AND facility_id=$2", s.Accounts["operator"].User.Id, s.FacilityId);
        var facility = await SetupRowAsync(s, "facilities", s.FacilityId);
        Assert.Equal(JsonValueKind.Null, facility.GetProperty("createdBy").ValueKind);
        Assert.False(facility.GetProperty("canEdit").GetBoolean());
        await RejectedAsync(s.Admin, HttpMethod.Put, $"/api/ledger/facilities/{s.FacilityId}", new { name = "Claim denied", version = facility.GetProperty("version").GetString() }, HttpStatusCode.Forbidden);
        var replay = await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/assignments", new { userId = s.Accounts["operator"].User.Id, facilityId = s.FacilityId });
        Assert.Equal(JsonValueKind.Null, replay.GetProperty("createdBy").ValueKind);
        Assert.False(replay.GetProperty("canDelete").GetBoolean());
        var legacyDraft = await GetAsync(s.Operator, $"/api/ledger/operations/{id}");
        Assert.False(legacyDraft.GetProperty("canEdit").GetBoolean());
        await RejectedAsync(s.Operator, HttpMethod.Put, $"/api/ledger/operations/{id}", DraftEditBody(s, id, source, draft.GetProperty("version").GetString()), HttpStatusCode.Forbidden);
        await RejectedAsync(s.Operator, HttpMethod.Delete, $"/api/ledger/operations/{id}", new { version = draft.GetProperty("version").GetString() }, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Restricted_direct_commands_enforce_creator_active_role_facility_and_version()
    {
        using var s = await CreateAsync(factory);
        var source = Lots(await s.ReceiptAsync("10"))[0];
        var draft = await s.DraftAsync("DRYING", [(source, "10")], [("DRIED_PADDY", "10")]);
        var id = draft.GetProperty("id").GetGuid();
        var version = draft.GetProperty("version").GetString();
        var (other, _) = await factory.SeedUserAsync(Roles.MillOperator, s.Accounts["operator"].User.OrganizationId);
        await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/assignments", new { userId = other.Id, facilityId = s.FacilityId });
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => DirectAsync(other.Id, "draft_delete", new { id, version }))).SqlState);
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await Assert.ThrowsAsync<PostgresException>(() => DirectAsync(s.Accounts["operator"].User.Id, "draft_delete", new { id, version = Guid.NewGuid() }))).SqlState);
        Assert.Equal(PostgresErrorCodes.InvalidParameterValue, (await Assert.ThrowsAsync<PostgresException>(() => DirectAsync(s.Accounts["admin"].User.Id, "facilities", new { id = Guid.NewGuid(), name = "Forged", createdBy = other.Id }))).SqlState);
        await OwnerSqlAsync("UPDATE identity.users SET \"IsActive\"=false WHERE \"Id\"=$1", other.Id);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => DirectAsync(other.Id, "facilities", new { id = Guid.NewGuid(), name = "Inactive" }))).SqlState);
        var assignment = (await GetAsync(s.Admin, "/api/ledger/setup")).GetProperty("assignments").EnumerateArray()
            .Single(x => x.GetProperty("userId").GetGuid() == s.Accounts["operator"].User.Id && x.GetProperty("facilityId").GetGuid() == s.FacilityId);
        await SendAsync(s.Admin, HttpMethod.Delete, $"/api/ledger/assignments/{s.Accounts["operator"].User.Id}/{s.FacilityId}", new { version = assignment.GetProperty("version").GetString() });
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => DirectAsync(s.Accounts["operator"].User.Id, "draft_delete", new { id, version }))).SqlState);
        await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString);
        await runtime.OpenAsync();
        foreach (var sql in new[] { "DELETE FROM ledger.operations", "SELECT ledger.record_in_use('operations',gen_random_uuid())" })
        {
            await using var denied = new NpgsqlCommand(sql, runtime);
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => denied.ExecuteNonQueryAsync())).SqlState);
        }
    }

    [Fact]
    public async Task Reviewed_snapshots_retain_old_location_and_equipment_after_draft_edit()
    {
        using var s = await CreateAsync(factory, policies: false);
        var source = Lots(await s.ReceiptAsync("100"))[0];
        var locationId = Guid.NewGuid();
        var equipmentId = Guid.NewGuid();
        await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/locations", new { id = locationId, facilityId = s.FacilityId, name = "Reviewed output", kind = "STORE" });
        await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/equipment", new { id = equipmentId, facilityId = s.FacilityId, name = "Reviewed equipment" });
        var id = Guid.NewGuid();
        var body = JsonSerializer.SerializeToNode(s.OperationBody(id, "DRYING", [(source, "100")], [("DRIED_PADDY", "99")], outputLocationId: locationId))!.AsObject();
        body["equipmentId"] = equipmentId.ToString();
        var draft = await SendAsync(s.Operator, HttpMethod.Post, "/api/ledger/operations", body);
        var version = draft.GetProperty("version").GetString();
        await SendAsync(s.Manager, HttpMethod.Post, $"/api/ledger/operations/{id}/confirm", new { version, evidenceId = s.EvidenceId });
        body["version"] = version;
        body["equipmentId"] = s.EquipmentId.ToString();
        body["outputs"]!.AsArray()[0]!.AsObject()["locationId"] = s.SecondLocationId.ToString();
        await SendAsync(s.Operator, HttpMethod.Put, $"/api/ledger/operations/{id}", body);
        var reviewSnapshot = await OwnerScalarAsync("SELECT details::text FROM ledger.events WHERE operation_id=$1 AND kind='VARIANCE_CONFIRMATION'", id);
        var eventCount = await OwnerScalarAsync("SELECT count(*) FROM ledger.events WHERE operation_id=$1", id);
        foreach (var (resource, recordId) in new[] { ("locations", locationId), ("equipment", equipmentId) })
        {
            var record = await SetupRowAsync(s, resource, recordId);
            var state = await OwnerScalarAsync($"SELECT row_to_json(x)::text FROM ledger.{resource} x WHERE id=$1", recordId);
            await RejectedAsync(s.Admin, HttpMethod.Delete, $"/api/ledger/{resource}/{recordId}", new { version = record.GetProperty("version").GetString() }, HttpStatusCode.Conflict);
            await RejectedAsync(s.Admin, HttpMethod.Put, $"/api/ledger/{resource}/{recordId}", new { name = "History retained", facilityId = s.ForeignFacilityId, version = record.GetProperty("version").GetString() }, HttpStatusCode.Conflict);
            Assert.Equal(state, await OwnerScalarAsync($"SELECT row_to_json(x)::text FROM ledger.{resource} x WHERE id=$1", recordId));
        }
        Assert.Equal(reviewSnapshot, await OwnerScalarAsync("SELECT details::text FROM ledger.events WHERE operation_id=$1 AND kind='VARIANCE_CONFIRMATION'", id));
        Assert.Equal(eventCount, await OwnerScalarAsync("SELECT count(*) FROM ledger.events WHERE operation_id=$1", id));
        Assert.Equal(100m, Quantity(await s.LotAsync(source)));
    }

    [Fact]
    public async Task Archived_catalog_commodity_cannot_reenter_drafts_or_compatibility_via_ledger_ids()
    {
        using var s = await CreateAsync(factory);
        var source = Lots(await s.ReceiptAsync("10"))[0];
        var created = await SendAsync(s.Admin, HttpMethod.Post, "/api/commodities", new { code = "TEST" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), name = "Archive guard fixture" });
        var catalogId = created.GetProperty("id").GetGuid();
        var commodity = (await GetAsync(s.Admin, "/api/ledger/setup")).GetProperty("commodities").EnumerateArray()
            .Single(x => x.GetProperty("catalogId").ValueKind == JsonValueKind.String && x.GetProperty("catalogId").GetGuid() == catalogId).GetProperty("id").GetGuid();
        await OwnerSqlAsync("UPDATE catalog.commodities SET is_archived=true WHERE id=$1", catalogId);
        var id = Guid.NewGuid();
        var body = DraftEditBody(s, id, source, null);
        body.Remove("version");
        body["outputs"]!.AsArray()[0]!.AsObject()["commodityId"] = commodity.ToString();
        await RejectedAsync(s.Operator, HttpMethod.Post, "/api/ledger/operations", body, HttpStatusCode.Conflict);
        var draft = await s.DraftAsync("DRYING", [(source, "10")], [("DRIED_PADDY", "10")]);
        body["version"] = draft.GetProperty("version").GetString();
        await RejectedAsync(s.Operator, HttpMethod.Put, $"/api/ledger/operations/{draft.GetProperty("id").GetGuid()}", body, HttpStatusCode.Conflict);
        await RejectedAsync(s.Admin, HttpMethod.Post, "/api/ledger/compatibilities", new
        {
            id = Guid.NewGuid(), facilityId = s.FacilityId, evidenceId = s.EvidenceId,
            inputProducts = new[] { $"{commodity}:HEAD_RICE" }, outputCommodityId = s.CommodityId,
            effectiveFrom = DateTimeOffset.UtcNow
        }, HttpStatusCode.Conflict);
        await RejectedAsync(s.Admin, HttpMethod.Post, "/api/ledger/compatibilities", new
        {
            id = Guid.NewGuid(), facilityId = s.FacilityId, evidenceId = s.EvidenceId,
            inputProducts = new[] { $"{s.CommodityId}:HEAD_RICE" }, outputCommodityId = commodity,
            effectiveFrom = DateTimeOffset.UtcNow
        }, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Unused_hold_payload_fields_cannot_poison_or_forge_setup_and_draft_history_references()
    {
        using var s = await CreateAsync(factory);
        var source = Lots(await s.ReceiptAsync("10"))[0];
        var draft = await s.DraftAsync("DRYING", [(source, "10")], [("DRIED_PADDY", "10")]);
        var id = draft.GetProperty("id").GetGuid();
        var locationId = Guid.NewGuid();
        var equipmentId = Guid.NewGuid();
        var location = await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/locations", new { id = locationId, facilityId = s.FacilityId, name = "Unrelated location", kind = "STORE" });
        var equipment = await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/equipment", new { id = equipmentId, facilityId = s.FacilityId, name = "Unrelated equipment" });
        await SendAsync(s.Customer, HttpMethod.Post, "/api/ledger/holds", new
        {
            id = Guid.NewGuid(), lotId = source, reason = "Invalid unused extras",
            locationId = "not-a-uuid", replacementOperationId = "not-a-uuid",
            reviewedContent = new { inputs = 42, outputs = "not-an-array", equipmentId = "not-a-uuid" },
            reconciliation = "not-an-array"
        });
        await SendAsync(s.Customer, HttpMethod.Post, "/api/ledger/holds", new
        {
            id = Guid.NewGuid(), lotId = source, reason = "Valid but forged unused references",
            locationId, replacementOperationId = id,
            reviewedContent = new { inputs = new[] { new { locationId } }, outputs = new[] { new { locationId } }, equipmentId }
        });
        Assert.True((await SetupRowAsync(s, "locations", locationId)).GetProperty("canDelete").GetBoolean());
        Assert.True((await SetupRowAsync(s, "equipment", equipmentId)).GetProperty("canDelete").GetBoolean());
        location = await SendAsync(s.Admin, HttpMethod.Put, $"/api/ledger/locations/{locationId}", new { name = "Unrelated rename", version = location.GetProperty("version").GetString() });
        equipment = await SendAsync(s.Admin, HttpMethod.Put, $"/api/ledger/equipment/{equipmentId}", new { name = "Unrelated rename", version = equipment.GetProperty("version").GetString() });
        await SendAsync(s.Admin, HttpMethod.Delete, $"/api/ledger/locations/{locationId}", new { version = location.GetProperty("version").GetString() });
        await SendAsync(s.Admin, HttpMethod.Delete, $"/api/ledger/equipment/{equipmentId}", new { version = equipment.GetProperty("version").GetString() });
        await SendAsync(s.Operator, HttpMethod.Delete, $"/api/ledger/operations/{id}", new { version = draft.GetProperty("version").GetString() });
        Assert.Equal(10m, Quantity(await s.LotAsync(source)));
        Assert.Equal(0m, Quantity(await s.LotAsync(source), "availableKg"));
    }

    [Fact]
    public async Task Pending_registration_rejects_archived_catalog_but_retains_existing_source_package_validity()
    {
        using var s = await CreateAsync(factory);
        var receipt = await s.ReceiptAsync("10");
        var id = receipt.GetProperty("id").GetGuid();
        await OwnerSqlAsync("UPDATE catalog.commodities SET is_archived=true WHERE id=$1", s.CatalogCommodityId);
        try
        {
            await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString);
            await runtime.OpenAsync();
            await using var register = new NpgsqlCommand("SELECT ledger.register_pending_procurement($1,$2)", runtime);
            register.Parameters.AddWithValue(s.Accounts["purchaser"].User.Id);
            register.Parameters.AddWithValue(id);
            var error = await Assert.ThrowsAsync<PostgresException>(() => register.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
            Assert.Equal("Active PADDY commodity required for new procurement", error.MessageText);
            await using var owner = new NpgsqlConnection(factory.OwnerConnectionString);
            await owner.OpenAsync();
            await using var package = new NpgsqlCommand("SELECT ledger.procurement_package_complete($1)", owner);
            package.Parameters.AddWithValue(id);
            Assert.Equal(true, await package.ExecuteScalarAsync());
        }
        finally
        {
            await OwnerSqlAsync("UPDATE catalog.commodities SET is_archived=false WHERE id=$1", s.CatalogCommodityId);
        }
    }

    [Fact]
    public async Task Retiring_unused_setup_children_allows_parent_retirement_without_cascade_or_restore()
    {
        using var s = await CreateAsync(factory, policies: false);
        var facilityId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var equipmentId = Guid.NewGuid();
        var operatorId = s.Accounts["operator"].User.Id;
        var facility = await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/facilities", new { id = facilityId, name = "Retained parent" });
        var location = await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/locations", new { id = locationId, facilityId, name = "Retained child", kind = "STORE" });
        var equipment = await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/equipment", new { id = equipmentId, facilityId, name = "Retained equipment" });
        var assignment = await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/assignments", new { userId = operatorId, facilityId });
        await RejectedAsync(s.Admin, HttpMethod.Delete, $"/api/ledger/facilities/{facilityId}", new { version = facility.GetProperty("version").GetString() }, HttpStatusCode.Conflict);
        await SendAsync(s.Admin, HttpMethod.Delete, $"/api/ledger/locations/{locationId}", new { version = location.GetProperty("version").GetString() });
        await SendAsync(s.Admin, HttpMethod.Delete, $"/api/ledger/equipment/{equipmentId}", new { version = equipment.GetProperty("version").GetString() });
        await SendAsync(s.Admin, HttpMethod.Delete, $"/api/ledger/assignments/{operatorId}/{facilityId}", new { version = assignment.GetProperty("version").GetString() });
        await SendAsync(s.Admin, HttpMethod.Delete, $"/api/ledger/facilities/{facilityId}", new { version = facility.GetProperty("version").GetString() });
        foreach (var (resource, id, original) in new[] { ("facilities", facilityId, facility), ("locations", locationId, location), ("equipment", equipmentId, equipment) })
            await AssertRetainedAsync(resource, id, original.GetProperty("version").GetString(), s.Accounts["admin"].User.Id);
        Assert.Equal(facilityId, await OwnerScalarAsync("SELECT facility_id FROM ledger.locations WHERE id=$1", locationId));
        Assert.Equal(true, await OwnerScalarAsync("SELECT is_deleted FROM ledger.assignments WHERE user_id=$1 AND facility_id=$2", operatorId, facilityId));
        Assert.Equal(false, await OwnerScalarAsync("SELECT ledger.assigned($1,$2)", operatorId, facilityId));
        await RejectedAsync(s.Admin, HttpMethod.Post, "/api/ledger/locations", new { id = Guid.NewGuid(), facilityId, name = "No retired parent", kind = "STORE" }, HttpStatusCode.Conflict);
        await RejectedAsync(s.Admin, HttpMethod.Post, "/api/ledger/assignments", new { userId = operatorId, facilityId }, HttpStatusCode.Conflict);
        await RejectedAsync(s.Admin, HttpMethod.Post, "/api/ledger/evidence", new { id = Guid.NewGuid(), ownerId = s.OwnerId, facilityId, fileName = "retired.txt", contentType = "text/plain", contentBase64 = "eA==" }, HttpStatusCode.Conflict);
        Assert.DoesNotContain((await GetAsync(s.Admin, "/api/ledger/setup")).GetProperty("facilities").EnumerateArray(), x => x.GetProperty("id").GetGuid() == facilityId);
    }

    [Fact]
    public async Task Retired_draft_releases_mutable_setup_references_but_removed_targets_cannot_be_reused()
    {
        using var s = await CreateAsync(factory);
        var source = Lots(await s.ReceiptAsync("10"))[0];
        var locationId = Guid.NewGuid();
        var equipmentId = Guid.NewGuid();
        var location = await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/locations", new { id = locationId, facilityId = s.FacilityId, name = "Draft output", kind = "STORE" });
        var equipment = await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/equipment", new { id = equipmentId, facilityId = s.FacilityId, name = "Draft equipment" });
        var id = Guid.NewGuid();
        var body = JsonSerializer.SerializeToNode(s.OperationBody(id, "DRYING", [(source, "10")], [("DRIED_PADDY", "10")], outputLocationId: locationId))!.AsObject();
        body["equipmentId"] = equipmentId.ToString();
        var draft = await SendAsync(s.Operator, HttpMethod.Post, "/api/ledger/operations", body);
        await RejectedAsync(s.Admin, HttpMethod.Delete, $"/api/ledger/equipment/{equipmentId}", new { version = equipment.GetProperty("version").GetString() }, HttpStatusCode.Conflict);
        await SendAsync(s.Operator, HttpMethod.Delete, $"/api/ledger/operations/{id}", new { version = draft.GetProperty("version").GetString() });
        await SendAsync(s.Admin, HttpMethod.Delete, $"/api/ledger/locations/{locationId}", new { version = location.GetProperty("version").GetString() });
        await SendAsync(s.Admin, HttpMethod.Delete, $"/api/ledger/equipment/{equipmentId}", new { version = equipment.GetProperty("version").GetString() });
        await AssertRetainedAsync("operations", id, draft.GetProperty("version").GetString(), s.Accounts["operator"].User.Id);
        await AssertRetainedAsync("locations", locationId, location.GetProperty("version").GetString(), s.Accounts["admin"].User.Id);
        await AssertRetainedAsync("equipment", equipmentId, equipment.GetProperty("version").GetString(), s.Accounts["admin"].User.Id);
        var removedEquipment = JsonSerializer.SerializeToNode(s.OperationBody(Guid.NewGuid(), "DRYING", [(source, "10")], [("DRIED_PADDY", "10")]))!.AsObject();
        removedEquipment["equipmentId"] = equipmentId.ToString();
        await RejectedAsync(s.Operator, HttpMethod.Post, "/api/ledger/operations", removedEquipment, HttpStatusCode.Forbidden);
        await RejectedAsync(s.Operator, HttpMethod.Post, "/api/ledger/operations", s.OperationBody(Guid.NewGuid(), "DRYING", [(source, "10")], [("DRIED_PADDY", "10")], outputLocationId: locationId), HttpStatusCode.Forbidden);
        await RejectedAsync(s.Operator, HttpMethod.Post, "/api/ledger/transfers", new { id = Guid.NewGuid(), lotId = source, fromLocationId = s.LocationId, toLocationId = locationId, quantityKg = "1", occurredAt = DateTimeOffset.UtcNow }, HttpStatusCode.Conflict);
        await RejectedAsync(s.Admin, HttpMethod.Post, $"/api/ledger/locations/{locationId}/clearance", new { evidenceId = s.EvidenceId, reason = "No retired clearance" }, HttpStatusCode.Conflict);
        await RejectedAsync(s.Purchaser, HttpMethod.Post, "/api/procurements", s.ReceiptBody(Guid.NewGuid(), ["1"], locationId), HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
        Assert.Equal(10m, Quantity(await s.LotAsync(source)));
    }

    [Fact]
    public async Task Soft_deleted_replacement_cannot_be_reconciled_even_with_its_current_tombstone_version()
    {
        using var s = await CreateAsync(factory);
        var source = Lots(await s.ReceiptAsync("10"))[0];
        var original = await s.CommitAsync(await s.DraftAsync("DRYING", [(source, "10")], [("DRIED_PADDY", "10")]));
        var originalId = original.GetProperty("id").GetGuid();
        var replacement = await s.DraftAsync("DRYING", [(source, "10")], [("DRIED_PADDY", "9")], "1");
        var replacementId = replacement.GetProperty("id").GetGuid();
        await SendAsync(s.Operator, HttpMethod.Delete, $"/api/ledger/operations/{replacementId}", new { version = replacement.GetProperty("version").GetString() });
        var currentVersion = await OwnerScalarAsync("SELECT version::text FROM ledger.operations WHERE id=$1", replacementId);
        var correctionId = Guid.NewGuid();
        await SendAsync(s.Operator, HttpMethod.Post, "/api/ledger/corrections", new { id = correctionId, operationId = originalId, reason = "Retired replacement regression" });
        var eventsBefore = await OwnerScalarAsync("SELECT count(*) FROM ledger.events WHERE operation_id=$1", originalId);
        await RejectedAsync(s.Manager, HttpMethod.Post, $"/api/ledger/corrections/{correctionId}/reconcile", new
        {
            evidenceId = s.EvidenceId, replacementOperationId = replacementId, replacementVersion = currentVersion,
            reconciliation = new[] { new { lotId = Output(original), locationId = s.LocationId, quantityKg = "10" } },
            reason = "Retired replacement must not bind"
        }, HttpStatusCode.Conflict);
        Assert.Equal("REQUESTED", await OwnerScalarAsync("SELECT status FROM ledger.corrections WHERE id=$1", correctionId));
        Assert.Equal(DBNull.Value, await OwnerScalarAsync("SELECT replacement_operation_id FROM ledger.corrections WHERE id=$1", correctionId));
        Assert.Equal(eventsBefore, await OwnerScalarAsync("SELECT count(*) FROM ledger.events WHERE operation_id=$1", originalId));
        Assert.Equal(0m, Quantity(await s.LotAsync(source)));
        Assert.Equal(10m, Quantity(await s.LotAsync(Output(original))));
    }

    [Theory]
    [InlineData("farmers")]
    [InlineData("plots")]
    [InlineData("crop_seasons")]
    [InlineData("plot_seasons")]
    public async Task Direct_new_source_registration_rejects_deleted_capture_references_without_rewriting_history(string resource)
    {
        using var s = await CreateAsync(factory);
        var receipt = await s.ReceiptAsync("10");
        var id = receipt.GetProperty("id").GetGuid();
        var referenceId = resource switch
        {
            "farmers" => s.FarmerId,
            "crop_seasons" => s.SeasonId,
            "plot_seasons" => s.PlotSeasonId,
            _ => (Guid)(await OwnerScalarAsync("SELECT plot_id FROM capture.plot_seasons WHERE id=$1", s.PlotSeasonId))!
        };
        await OwnerSqlAsync($"UPDATE capture.{resource} SET is_deleted=true WHERE id=$1", referenceId);
        try
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => DirectPendingAsync(s.Accounts["purchaser"].User.Id, id));
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
            Assert.Equal("Active farmer, plot and season references required for new procurement", error.MessageText);
            Assert.Equal(true, await OwnerScalarAsync("SELECT ledger.procurement_package_complete($1)", id));
        }
        finally
        {
            await OwnerSqlAsync($"UPDATE capture.{resource} SET is_deleted=false WHERE id=$1", referenceId);
        }
    }

    [Fact]
    public async Task Deleted_mass_unit_rejects_new_registration_and_arrival_without_invalidating_pending_history()
    {
        using var s = await CreateAsync(factory);
        var receipt = await SendAsync(s.Purchaser, HttpMethod.Post, "/api/procurements", s.ReceiptBody(Guid.NewGuid(), ["10"]));
        var id = receipt.GetProperty("id").GetGuid();
        await OwnerSqlAsync("UPDATE catalog.measurement_units SET is_deleted=true WHERE code='KG'");
        try
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => DirectPendingAsync(s.Accounts["purchaser"].User.Id, id));
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
            Assert.Equal("Active mass units required for new procurement", error.MessageText);
            await RejectedAsync(s.Operator, HttpMethod.Post, $"/api/ledger/receiving/{id}/measurements", MeasurementBody(receipt), HttpStatusCode.Conflict);
            Assert.Equal(true, await OwnerScalarAsync("SELECT ledger.procurement_package_complete($1)", id));
            Assert.Equal(0L, await OwnerScalarAsync("SELECT count(*) FROM ledger.arrival_measurement_sets WHERE procurement_id=$1", id));
            Assert.Equal(0L, await OwnerScalarAsync("SELECT count(*) FROM ledger.lots WHERE receipt_id=$1", id));
        }
        finally
        {
            await OwnerSqlAsync("UPDATE catalog.measurement_units SET is_deleted=false WHERE code='KG'");
        }
    }

    [Theory]
    [InlineData("facilities")]
    [InlineData("locations")]
    public async Task Direct_new_source_registration_rejects_deleted_receiving_setup(string resource)
    {
        using var s = await CreateAsync(factory);
        var receipt = await s.ReceiptAsync("10");
        var referenceId = resource == "facilities" ? s.FacilityId : s.LocationId;
        await OwnerSqlAsync($"UPDATE ledger.{resource} SET is_deleted=true WHERE id=$1", referenceId);
        try
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => DirectPendingAsync(s.Accounts["purchaser"].User.Id, receipt.GetProperty("id").GetGuid()));
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
            Assert.Equal("Receiving location no longer eligible", error.MessageText);
        }
        finally
        {
            await OwnerSqlAsync($"UPDATE ledger.{resource} SET is_deleted=false WHERE id=$1", referenceId);
        }
    }

    private static object SetupBody(string resource, Guid id, Guid facilityId) => resource switch
    {
        "facilities" => new { id, name = "Unused setup fixture" },
        "locations" => new { id, facilityId, name = "Unused setup fixture", kind = "STORE" },
        _ => new { id, facilityId, name = "Unused setup fixture" }
    };

    private static JsonObject DraftEditBody(LedgerScenario s, Guid id, Guid source, string? version)
    {
        var body = JsonSerializer.SerializeToNode(s.OperationBody(id, "DRYING", [(source, "10")], [("DRIED_PADDY", "9")], "1"))!.AsObject();
        body["version"] = version;
        return body;
    }

    private static async Task<JsonElement> SetupRowAsync(LedgerScenario s, string resource, Guid id) =>
        (await GetAsync(s.Admin, "/api/ledger/setup")).GetProperty(resource).EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == id).Clone();

    private async Task AssertRetainedAsync(string resource, Guid id, string? previousVersion, Guid creator)
    {
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString);
        await owner.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT is_deleted,version,created_by FROM ledger.{resource} WHERE id=$1", owner);
        command.Parameters.AddWithValue(id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0));
        Assert.NotEqual(previousVersion, reader.GetGuid(1).ToString());
        Assert.Equal(creator, reader.GetGuid(2));
        Assert.False(await reader.ReadAsync());
    }

    private async Task<object?> OwnerScalarAsync(string sql, params object[] values)
    {
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString);
        await owner.OpenAsync();
        await using var command = new NpgsqlCommand(sql, owner);
        foreach (var value in values) command.Parameters.AddWithValue(value);
        return await command.ExecuteScalarAsync();
    }

    private async Task OwnerSqlAsync(string sql, params object[] values)
    {
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString);
        await owner.OpenAsync();
        await using var command = new NpgsqlCommand(sql, owner);
        foreach (var value in values) command.Parameters.AddWithValue(value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task DirectPendingAsync(Guid actor, Guid procurementId)
    {
        await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString);
        await runtime.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT ledger.register_pending_procurement($1,$2)", runtime);
        command.Parameters.AddWithValue(actor);
        command.Parameters.AddWithValue(procurementId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task DirectAsync(Guid actor, string action, object payload)
    {
        await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString);
        await runtime.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT ledger.command($1,$2,$3::jsonb)", runtime);
        command.Parameters.AddWithValue(actor);
        command.Parameters.AddWithValue(action);
        command.Parameters.AddWithValue(JsonSerializer.Serialize(payload));
        await command.ExecuteNonQueryAsync();
    }
}
