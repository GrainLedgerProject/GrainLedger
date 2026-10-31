using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GrainLedger.Modules.Exports;

public sealed record ExportLimitInput(Guid AttributeId, string? MinValue, string? MaxValue);
public sealed record ExportContainerInput(string? TargetMass, string? Unit);
public sealed record CreateExportOrderRequest(Guid OperationId, Guid MarketId, Guid CommodityId,
    string? RequiredMass, string? Unit, DateOnly ShippingFrom, DateOnly ShippingTo,
    IReadOnlyList<ExportLimitInput>? Limits, IReadOnlyList<ExportContainerInput>? Containers);
public sealed record UpdateExportOrderRequest(Guid OperationId, Guid MarketId, Guid CommodityId,
    string? RequiredMass, string? Unit, DateOnly ShippingFrom, DateOnly ShippingTo);
public sealed record AddExportSpecificationRequest(Guid OperationId, IReadOnlyList<ExportLimitInput>? Limits);
public sealed record AddExportContainersRequest(Guid OperationId, IReadOnlyList<ExportContainerInput>? Containers);
public sealed record UpdateExportContainerRequest(Guid OperationId, string? TargetMass, string? Unit);

public sealed record ExportLimitDto(Guid AttributeId, string AttributeCode, string AttributeName,
    string Unit, string? MinValue, string? MaxValue);
public sealed record ExportContainerDto(Guid Id, Guid OrderId, string InternalCode, string TargetMassKg,
    string OriginalMass, string OriginalUnit, string Status, int Version);
public sealed record ExportOrderSummary(Guid Id, string Code, Guid MarketId, Guid CommodityId,
    string RequiredMassKg, DateOnly ShippingFrom, DateOnly ShippingTo, DateTimeOffset CreatedAt, int Version);
public sealed record ExportOrderDto(Guid Id, string Code, Guid MarketId, Guid CommodityId,
    string RequiredMassKg, string OriginalMass, string OriginalUnit, DateOnly ShippingFrom,
    DateOnly ShippingTo, int Version, int SpecificationVersion, IReadOnlyList<ExportLimitDto> Limits,
    string AllocatedMassKg, string RemainingMassKg,
    bool CanEdit);
public sealed record ExportPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, bool HasMore);
public sealed record ExportSpecificationDto(int VersionNo, DateTimeOffset RecordedAt, IReadOnlyList<ExportLimitDto> Limits);
public sealed record ExportEventDto(string Action, DateTimeOffset RecordedAt, string Snapshot, string ActorName);

public static class ExportRules
{
    private static readonly Regex Number = new(@"^[0-9]+(?:\.[0-9]+)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public static string Text(decimal value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    public static string BoundText(decimal value) => value.ToString("0.############", CultureInfo.InvariantCulture);
    public static string Hash(string kind, object payload) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(kind + ":" + JsonSerializer.Serialize(payload))));

    public static bool TryMass(string? raw, string? unit, out decimal kg)
    {
        kg = 0;
        if (raw is null || raw.Length is < 1 or > 30 || !Number.IsMatch(raw) ||
            raw.Split('.').ElementAtOrDefault(1)?.Length > 6 ||
            !decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ||
            value <= 0 || unit is not ("KG" or "G" or "T")) return false;
        try
        {
            kg = unit switch { "G" => value / 1000m, "T" => value * 1000m, _ => value };
            return kg > 0 && kg <= 99999999999999.999999m && decimal.Round(kg, 6) == kg;
        }
        catch (OverflowException) { return false; }
    }

    public static bool TryBound(string? raw, out decimal? value)
    {
        value = null;
        if (raw is null) return true;
        if (raw.Length is < 1 or > 40 || !Number.IsMatch(raw) ||
            raw.Split('.').ElementAtOrDefault(1)?.Length > 12 ||
            !decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed) ||
            parsed >= 1000000000000m) return false;
        value = parsed;
        return true;
    }

    public static Dictionary<string, string[]> ValidateLimits(IReadOnlyList<ExportLimitInput>? limits)
    {
        var errors = new Dictionary<string, string[]>();
        if (limits is not { Count: > 0 and <= 100 })
        {
            errors["specification.limits"] = ["Add at least one quality limit (maximum 100)."];
            return errors;
        }
        var seen = new HashSet<Guid>();
        for (var i = 0; i < limits.Count; i++)
        {
            var item = limits[i];
            if (item.AttributeId == Guid.Empty || !seen.Add(item.AttributeId))
                errors[$"specification.limits[{i}].attributeId"] = ["Select each attribute once."];
            if (!TryBound(item.MinValue, out var min))
                errors[$"specification.limits[{i}].minValue"] = ["Enter a nonnegative decimal with at most 12 places."];
            if (!TryBound(item.MaxValue, out var max))
                errors[$"specification.limits[{i}].maxValue"] = ["Enter a nonnegative decimal with at most 12 places."];
            if (item.MinValue is null && item.MaxValue is null ||
                min is not null && max is not null && min > max)
                errors[$"specification.limits[{i}]"] = ["Set at least one bound, with minimum no greater than maximum."];
        }
        return errors;
    }

    public static Dictionary<string, string[]> ValidateContainers(IReadOnlyList<ExportContainerInput>? containers,
        out decimal total)
    {
        total = 0;
        var errors = new Dictionary<string, string[]>();
        if (containers is not { Count: > 0 and <= 100 })
        {
            errors["containers"] = ["Add at least one container target (maximum 100)."];
            return errors;
        }
        for (var i = 0; i < containers.Count; i++)
        {
            if (!TryMass(containers[i].TargetMass, containers[i].Unit, out var mass))
                errors[$"containers[{i}].targetMass"] = ["Enter a positive mass in a configured KG, G or T unit without rounding."];
            else total += mass;
        }
        return errors;
    }

    public static Dictionary<string, string[]> ValidateHeader(Guid marketId, Guid commodityId,
        string? mass, string? unit, DateOnly from, DateOnly to, out decimal kg)
    {
        var errors = new Dictionary<string, string[]>();
        if (marketId == Guid.Empty) errors["marketId"] = ["Choose an export market."];
        if (commodityId == Guid.Empty) errors["commodityId"] = ["Choose a commodity."];
        if (!TryMass(mass, unit, out kg)) errors["requiredMass"] = ["Enter a positive mass in a configured KG, G or T unit without rounding."];
        if (from == default) errors["shippingFrom"] = ["Choose the first shipping date."];
        if (to == default || to < from) errors["shippingTo"] = ["End date must be on or after start date."];
        return errors;
    }
}
