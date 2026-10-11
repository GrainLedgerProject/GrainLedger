using System.Text.Json;
using NetTopologySuite.Geometries;

namespace GrainLedger.Modules.Capture;

public static class PlotGeometry
{
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    public static bool TryNormalize(JsonElement input, out string json)
    {
        json = string.Empty;
        if (input.ValueKind != JsonValueKind.Object || input.GetRawText().Length > 1_000_000 ||
            !input.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
            !input.TryGetProperty("coordinates", out var coordinates) || coordinates.ValueKind != JsonValueKind.Array)
            return false;

        if (type.GetString() == "Point")
        {
            if (!TryPosition(coordinates, out var position)) return false;
            json = JsonSerializer.Serialize(new { type = "Point", coordinates = position });
            return true;
        }

        if (type.GetString() != "Polygon" || coordinates.GetArrayLength() == 0) return false;
        var rings = new List<double[][]>();
        foreach (var ringElement in coordinates.EnumerateArray())
        {
            if (ringElement.ValueKind != JsonValueKind.Array || ringElement.GetArrayLength() < 4) return false;
            var ring = new List<double[]>();
            foreach (var positionElement in ringElement.EnumerateArray())
            {
                if (!TryPosition(positionElement, out var position)) return false;
                ring.Add(position);
            }
            if (ring[0][0] != ring[^1][0] || ring[0][1] != ring[^1][1] ||
                ring.Take(ring.Count - 1).Select(p => (p[0], p[1])).Distinct().Count() < 3)
                return false;
            rings.Add(ring.ToArray());
        }

        try
        {
            LinearRing ToLinearRing(double[][] ring) => Factory.CreateLinearRing(
                ring.Select(p => new Coordinate(p[0], p[1])).ToArray());
            var polygon = Factory.CreatePolygon(ToLinearRing(rings[0]),
                rings.Skip(1).Select(ToLinearRing).ToArray());
            if (!polygon.IsValid || polygon.Area <= 0) return false;
        }
        catch (ArgumentException)
        {
            return false;
        }

        json = JsonSerializer.Serialize(new { type = "Polygon", coordinates = rings });
        return true;
    }

    private static bool TryPosition(JsonElement element, out double[] position)
    {
        position = [];
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != 2) return false;
        var items = element.EnumerateArray().ToArray();
        if (items.Any(x => x.ValueKind != JsonValueKind.Number) ||
            !items[0].TryGetDouble(out var longitude) || !items[1].TryGetDouble(out var latitude) ||
            !double.IsFinite(longitude) || !double.IsFinite(latitude) ||
            longitude is < -180 or > 180 || latitude is < -90 or > 90)
            return false;
        position = [longitude, latitude];
        return true;
    }
}
