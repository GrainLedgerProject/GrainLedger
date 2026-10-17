using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace GrainLedger.Modules.Capture;

public sealed record FarmerDuplicateRequest(string? Name, string? Contact, Guid? ExcludeId);
public sealed record FarmerDuplicateItem(Guid Id, string Code, string Name, string? Contact,
    IReadOnlyList<string> Reasons);
public sealed record FarmerDuplicateResponse(IReadOnlyList<FarmerDuplicateItem> Items, bool HasMore);
public sealed record PlotDuplicateRequest(JsonElement Geometry, Guid? ExcludeId);
public sealed record PlotDuplicateItem(Guid Id, Guid FarmerId, string Code, double DistanceMeters,
    IReadOnlyList<string> Reasons);
public sealed record PlotDuplicateResponse(IReadOnlyList<PlotDuplicateItem> Items, bool HasMore,
    int RadiusMeters, string Coverage);

public static class CaptureDuplicateEndpoints
{
    private const int Limit = 5;
    private const int RadiusMeters = 20;

    public static async Task<IResult> CheckFarmerAsync(FarmerDuplicateRequest request,
        ClaimsPrincipal principal, ICurrentAccess access, IdentityDbContext identityDb,
        CaptureDbContext db, CancellationToken ct)
    {
        var companyId = await GetCompanyAsync(principal, access, identityDb, ct);
        if (companyId is null) return Results.Forbid();
        var name = NormalizeName(request.Name);
        var contact = NormalizeContact(request.Contact);
        var errors = new Dictionary<string, string[]>();
        if (name.Length is < 2 or > 200)
            errors["name"] = ["Enter a farmer name of 2 to 200 characters."];
        if (request.Contact?.Length > 100)
            errors["contact"] = ["Contact must not exceed 100 characters."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        if (request.ExcludeId.HasValue && !await db.Farmers.AnyAsync(f =>
                f.CompanyId == companyId.Value && f.Id == request.ExcludeId.Value, ct))
            return Results.NotFound();
        var excludedId = request.ExcludeId ?? Guid.Empty;

        // This is an advisory check, not a uniqueness constraint. Search stays inside the tenant.
        var candidates = await db.Farmers.FromSqlInterpolated($"""
            SELECT * FROM capture.farmers
            WHERE company_id = {companyId.Value} AND is_deleted = false
              AND id <> {excludedId}
              AND (lower(regexp_replace(btrim(normalize(name, NFKC)), '[[:space:]]+', ' ', 'g')) = {name}
                   OR ({contact} <> '' AND
                       CASE WHEN contact ~ '^[+0-9 ()-]+$'
                            THEN regexp_replace(contact, '[[:space:]().-]', '', 'g')
                            ELSE lower(btrim(contact)) END = {contact}))
            ORDER BY created_at, id
            """).AsNoTracking().Take(Limit + 1).ToListAsync(ct);
        var items = candidates.Take(Limit).Select(f =>
        {
            var reasons = new List<string>();
            if (NormalizeName(f.Name) == name) reasons.Add("SAME_NAME");
            if (contact.Length > 0 && NormalizeContact(f.Contact) == contact) reasons.Add("SAME_CONTACT");
            return new FarmerDuplicateItem(f.Id, f.Code, f.Name, f.Contact, reasons);
        }).ToArray();
        return Results.Ok(new FarmerDuplicateResponse(items, candidates.Count > Limit));
    }

    public static async Task<IResult> CheckPlotAsync(PlotDuplicateRequest request,
        ClaimsPrincipal principal, ICurrentAccess access, IdentityDbContext identityDb,
        CaptureDbContext db, CancellationToken ct)
    {
        var companyId = await GetCompanyAsync(principal, access, identityDb, ct);
        if (companyId is null) return Results.Forbid();
        if (request.ExcludeId.HasValue && !await db.Plots.AnyAsync(p =>
                p.CompanyId == companyId.Value && p.Id == request.ExcludeId.Value, ct))
            return Results.NotFound();
        var excludedId = request.ExcludeId ?? Guid.Empty;
        if (!PlotGeometry.TryNormalize(request.Geometry, out var geometry))
            return Results.ValidationProblem(new Dictionary<string, string[]>
                { ["geometry"] = ["Enter a valid WGS84 point."] });
        using var document = JsonDocument.Parse(geometry);
        var root = document.RootElement;
        if (root.GetProperty("type").GetString() != "Point")
            return Results.ValidationProblem(new Dictionary<string, string[]>
                { ["geometry"] = ["Nearby-plot checking supports points only."] });
        var coordinates = root.GetProperty("coordinates");
        var longitude = coordinates[0].GetDouble();
        var latitude = coordinates[1].GetDouble();
        var latitudeDelta = RadiusMeters / 111_195.0;
        // Longitude degrees shrink towards the poles. At a pole, include every longitude.
        var longitudeDelta = Math.Abs(latitude) + latitudeDelta >= 90
            ? 180.0 : Math.Min(180.0, latitudeDelta / Math.Cos((Math.Abs(latitude) + latitudeDelta) * Math.PI / 180));

        // Coordinates are validated when plots are written. This SQL compares points only;
        // polygon overlap is intentionally outside this advisory check.
        var candidates = await db.Plots.FromSqlInterpolated($"""
            SELECT nearby.* FROM (
                SELECT p.*, 6371000.0 * 2 * asin(least(1.0, sqrt(
                    power(sin(radians(((p.boundary_geojson->'coordinates'->>1)::double precision - {latitude}) / 2)), 2)
                    + cos(radians({latitude})) * cos(radians((p.boundary_geojson->'coordinates'->>1)::double precision))
                    * power(sin(radians(((p.boundary_geojson->'coordinates'->>0)::double precision - {longitude}) / 2)), 2)
                ))) AS distance_meters
                FROM capture.plots p
                WHERE p.company_id = {companyId.Value} AND p.is_deleted = false
                  AND p.id <> {excludedId}
                  AND p.boundary_geojson->>'type' = 'Point'
                  AND (p.boundary_geojson->'coordinates'->>1)::double precision BETWEEN {latitude - latitudeDelta} AND {latitude + latitudeDelta}
                  AND least(abs((p.boundary_geojson->'coordinates'->>0)::double precision - {longitude}),
                            360.0 - abs((p.boundary_geojson->'coordinates'->>0)::double precision - {longitude})) <= {longitudeDelta}
            ) AS nearby
            WHERE nearby.distance_meters <= {RadiusMeters}
            ORDER BY nearby.distance_meters, nearby.id
            LIMIT {Limit + 1}
            """).AsNoTracking().ToListAsync(ct);
        var items = candidates.Take(Limit).Select(p =>
        {
            using var point = JsonDocument.Parse(p.BoundaryGeojson);
            var other = point.RootElement.GetProperty("coordinates");
            var distance = DistanceMeters(latitude, longitude, other[1].GetDouble(), other[0].GetDouble());
            return new PlotDuplicateItem(p.Id, p.FarmerId, p.Code, Math.Round(distance, 1), ["NEARBY_POINT"]);
        }).ToArray();
        return Results.Ok(new PlotDuplicateResponse(items, candidates.Count > Limit, RadiusMeters, "POINT_ONLY"));
    }

    private static string NormalizeName(string? value) =>
        Regex.Replace((value ?? "").Normalize(NormalizationForm.FormKC).Trim(), @"\s+", " ").ToLowerInvariant();

    private static string NormalizeContact(string? value)
    {
        var text = value?.Trim() ?? "";
        return Regex.IsMatch(text, @"^[+0-9 ()-]+$")
            ? Regex.Replace(text, @"[\s().-]", "") : text.ToLowerInvariant();
    }

    private static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        var phi = (lat2 - lat1) * Math.PI / 180;
        var lambda = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Pow(Math.Sin(phi / 2), 2) + Math.Cos(lat1 * Math.PI / 180)
            * Math.Cos(lat2 * Math.PI / 180) * Math.Pow(Math.Sin(lambda / 2), 2);
        return 6371000 * 2 * Math.Asin(Math.Sqrt(Math.Min(1, a)));
    }

    private static async Task<Guid?> GetCompanyAsync(ClaimsPrincipal principal, ICurrentAccess access,
        IdentityDbContext db, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return null;
        var user = await db.Users.AsNoTracking().Include(x => x.Organization)
            .SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, ct);
        if (user?.OrganizationId is not { } companyId ||
            user.Organization?.Kind != OrganizationKind.Customer ||
            !await access.CanAccessOrganizationAsync(companyId, ct)) return null;
        return companyId;
    }
}
