using System.Globalization;
using System.Text.RegularExpressions;

namespace GrainLedger.Modules.Capture;

public sealed record FarmerRequest(Guid Id, Guid OwnerOrganizationId, string Name, string? PhoneNumber);
public sealed record PlotRequest(Guid Id, Guid FarmerId, string Name, string Latitude, string Longitude);
public sealed record SeasonRequest(Guid Id, Guid OwnerOrganizationId, string Name, DateOnly StartsOn, DateOnly EndsOn);
public sealed record PlotSeasonRequest(Guid Id, Guid PlotId, Guid SeasonId);
public sealed record ReceiptLineRequest(Guid Id, Guid PlotSeasonId, Guid CommodityId, string LotCode, string Quantity, string Unit);
public sealed record ReceiptRequest(Guid Id, Guid OwnerOrganizationId, Guid FarmerId, Guid SeasonId,
    Guid LocationId, DateTimeOffset OccurredAt, ReceiptLineRequest[] Lines);
public sealed record UpdateReceiptRequest(Guid Id, Guid OwnerOrganizationId, Guid FarmerId, Guid SeasonId,
    Guid LocationId, DateTimeOffset OccurredAt, ReceiptLineRequest[] Lines, Guid Version)
{
    public ReceiptRequest Content => new(Id, OwnerOrganizationId, FarmerId, SeasonId, LocationId, OccurredAt, Lines);
}
public sealed record CommitReceiptRequest(Guid CommandId, Guid Version);
public sealed record ReceiptLineDto(Guid Id, Guid PlotSeasonId, Guid CommodityId, string LotCode, string Quantity,
    string Unit, string QuantityKg, Guid? LotId);
public sealed record ReceiptDto(Guid Id, Guid OwnerOrganizationId, Guid FarmerId, Guid SeasonId,
    Guid LocationId, DateTimeOffset OccurredAt, ReceiptLineDto[] Lines, string Status, Guid Version,
    Guid? ReplacementForReceiptId);
public sealed record EvidenceRequest(Guid Id, Guid OwnerOrganizationId, Guid LocationId, string FileName,
    string MediaType, string ContentBase64);
public sealed record ArrivalRequest(Guid Id, Guid ReceiptLineId, string Quantity, string Unit,
    Guid EvidenceId, DateTimeOffset ObservedAt);
public sealed record ArrivalCorrectionRequest(Guid Id, string Quantity, string Unit, Guid EvidenceId, string Reason);
public sealed record ArrivalInvestigationRequest(Guid Id, string Cause, Guid EvidenceId, string Reason);
public sealed record ArrivalCorrectionDto(Guid Id, string Quantity, string Unit, string QuantityKg,
    Guid EvidenceId, string Reason, DateTimeOffset ObservedAt);
public sealed record ArrivalInvestigationDto(Guid Id, string Cause, Guid EvidenceId, string Reason, DateTimeOffset CreatedAt);
public sealed record ArrivalDto(Guid Id, Guid ReceiptLineId, string Quantity, string Unit, string QuantityKg,
    Guid EvidenceId, DateTimeOffset ObservedAt, string ExpectedQuantityKg, string Status,
    ArrivalCorrectionDto[] Corrections, ArrivalInvestigationDto[] Investigations);
public sealed record ReceiptCorrectionRequest(Guid Id, string Reason, ReceiptRequest Replacement);
public sealed record VerifiedReceiptLine(Guid ReceiptLineId, string QuantityKg);
public sealed record ReceiptReconciliationRequest(Guid CommandId, Guid EvidenceId, string Reason, VerifiedReceiptLine[] VerifiedLines);
public sealed record CommitCorrectionRequest(Guid CommandId);
public sealed record ReceiptCorrectionDto(Guid Id, Guid ReceiptId, string Reason, Guid ReplacementReceiptId,
    ReceiptDto Replacement, string Status, Guid? EvidenceId, string? ReconciliationReason,
    VerifiedReceiptLine[] VerifiedLines, DateTimeOffset CreatedAt);

public sealed class CaptureFault(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public static class CaptureMeasure
{
    public const decimal MaximumKg = 99999999999999.999999m;

    // No rounding: PostgreSQL numeric(20,6) is an exact boundary, including unit conversion.
    public static decimal ToKg(string quantity, string unit)
    {
        var value = Decimal(quantity, false);
        decimal kg;
        try
        {
            kg = unit switch
            {
                "kg" => value,
                "g" => value / 1000m,
                "t" => checked(value * 1000m),
                _ => throw new CaptureFault(400, "Supported measurement units are kg, g and t.")
            };
        }
        catch (OverflowException) { throw new CaptureFault(400, "Measurement exceeds numeric(20,6) capacity."); }
        if (kg <= 0 || kg > MaximumKg || decimal.Truncate(kg * 1000000m) != kg * 1000000m)
            throw new CaptureFault(400, "Measurement must be positive and convert exactly to numeric(20,6) kg.");
        return kg;
    }

    public static string Kg(decimal quantity) => quantity.ToString("0.######", CultureInfo.InvariantCulture);

    public static decimal Coordinate(string value, decimal limit)
    {
        var coordinate = Decimal(value, true);
        if (coordinate < -limit || coordinate > limit) throw new CaptureFault(400, "Coordinates are outside geographic bounds.");
        return coordinate;
    }

    private static decimal Decimal(string value, bool signed)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 40 ||
            !Regex.IsMatch(value, signed ? @"^-?[0-9]+(?:\.[0-9]{1,6})?$" : @"^[0-9]+(?:\.[0-9]{1,6})?$", RegexOptions.CultureInvariant) ||
            !decimal.TryParse(value, signed ? NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint : NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var result))
            throw new CaptureFault(400, "Decimal strings must have at most six fractional digits; exponent notation is unsupported.");
        return result;
    }
}
