using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrainLedger.Modules.Identity;
using GrainLedger.Modules.Ledger;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace GrainLedger.Modules.Exports;

// This is a scoped stock read for the UI. F14 proposal must re-read the full
// candidate set and validate lineage, product compatibility and compliance.
public sealed record ExportStockPositionDto(Guid LocationId, Guid FacilityId, string OnHandKg, string AvailableKg);
public sealed record ExportStockLotDto(Guid LotId, string LotCode, string Stage, string ProductIdentity,
    Guid LedgerCommodityId, IReadOnlyList<string> Restrictions, IReadOnlyList<ExportStockPositionDto> Positions);
public sealed record ExportStockPreviewDto(Guid ContainerId, Guid FacilityId, int Page, int PageSize,
    int TotalCount, bool HasMore, IReadOnlyList<ExportStockLotDto> Items);
public sealed record ExportInventoryScopeDto(Guid ContainerId, Guid FacilityId, int TotalLots,
    string AvailableMassKg, int RestrictedLots, bool Revisioned, bool ComplianceQualified);

public static partial class ExportModule
{
    private static async Task<IResult> InventoryScopeAsync(Guid id, HttpContext http,
        IdentityDbContext identity, ICurrentAccess access, ExportDbContext db, LedgerService ledger,
        CancellationToken ct)
    {
        var scope = await ScopeAsync(http, identity, access, Permissions.ExportOrdersRead, ct);
        if (scope is null) return Results.Forbid();
        if (!await db.Containers.AnyAsync(x => x.Id == id && x.CompanyId == scope.Value.Company, ct))
            return Results.NotFound();
        if (!Guid.TryParse(http.Request.Query["facilityId"], out var facilityId) || facilityId == Guid.Empty)
            return Invalid(new() { ["facilityId"] = ["Choose a facility."] });
        var inventory = await ledger.ReadInventoryScopeAsync(scope.Value.Company, facilityId, ct);
        var available = inventory.Lots.SelectMany(x => x.Positions).Sum(x => x.AvailableKg);
        return Results.Ok(new ExportInventoryScopeDto(id, facilityId, inventory.TotalLots,
            ExportRules.Text(available), inventory.Lots.Count(x => x.Restrictions.Count > 0), false, false));
    }

    private static async Task<IResult> StockPreviewAsync(Guid id, HttpContext http,
        IdentityDbContext identity, ICurrentAccess access, ExportDbContext db, LedgerService ledger,
        CancellationToken ct)
    {
        var scope = await ScopeAsync(http, identity, access, Permissions.ExportOrdersRead, ct);
        if (scope is null) return Results.Forbid();
        if (!await db.Containers.AnyAsync(x => x.Id == id && x.CompanyId == scope.Value.Company, ct))
            return Results.NotFound();
        var rawFacility = http.Request.Query["facilityId"].ToString();
        if (!Guid.TryParse(rawFacility, out var facilityId) || facilityId == Guid.Empty ||
            !Page(http.Request.Query["page"], http.Request.Query["pageSize"], out var page, out var size))
            return Invalid(new() { ["query"] = ["Choose a facility and valid pagination."] });

        // The Ledger read routine enforces persisted actor and owner access.
        // Its paging belongs only to this preview, never to the solver input.
        var result = await ledger.ReadAsync("lots", new JsonObject
        {
            ["ownerId"] = scope.Value.Company.ToString(), ["facilityId"] = facilityId.ToString(),
            ["page"] = page.ToString(CultureInfo.InvariantCulture),
            ["pageSize"] = size.ToString(CultureInfo.InvariantCulture),
            ["sortBy"] = "code", ["sortDirection"] = "asc"
        }, ct);
        var items = new List<ExportStockLotDto>();
        foreach (var lot in result.GetProperty("items").EnumerateArray())
        {
            if (lot.GetProperty("ownerId").GetGuid() != scope.Value.Company)
                throw new InvalidOperationException("Ledger returned stock outside the requested company.");
            var positions = lot.GetProperty("positions").EnumerateArray()
                .Where(x => x.GetProperty("facilityId").GetGuid() == facilityId)
                .Select(x => new ExportStockPositionDto(x.GetProperty("locationId").GetGuid(), facilityId,
                    x.GetProperty("quantityKg").GetString()!, x.GetProperty("availableKg").GetString()!))
                .ToArray();
            items.Add(new ExportStockLotDto(lot.GetProperty("id").GetGuid(), lot.GetProperty("code").GetString()!,
                lot.GetProperty("stage").GetString()!, lot.GetProperty("productIdentity").GetString()!,
                lot.GetProperty("commodityId").GetGuid(),
                lot.GetProperty("restrictions").EnumerateArray().Select(x => x.GetString()!).ToArray(), positions));
        }
        return Results.Ok(new ExportStockPreviewDto(id, facilityId, result.GetProperty("page").GetInt32(),
            result.GetProperty("pageSize").GetInt32(), result.GetProperty("totalCount").GetInt32(),
            result.GetProperty("hasMore").GetBoolean(), items));
    }
}
