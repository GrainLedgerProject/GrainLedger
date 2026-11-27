using System.Text.Json;

namespace GrainLedger.Api.Features.Exports;

internal static class RiceVarietyEligibility
{
    internal static bool Requires(string? target) => target is
        "RAW_PADDY" or "DRIED_PADDY" or "HEAD_RICE" or "BROKEN_RICE" or "BLENDED_RICE";

    internal static string? Reason(Guid? required, Guid? actual, string? status, string? target)
    {
        if (!Requires(target)) return null;
        if (required is null || required == Guid.Empty) return "VARIETY_REQUIREMENT_MISSING";
        if (status == "MIXED") return "VARIETY_MIXED";
        if (status != "SINGLE" || actual is null || actual == Guid.Empty) return "VARIETY_UNKNOWN";
        return actual == required ? null : "VARIETY_MISMATCH";
    }

    internal static string? Reason(PlanningCandidateSnapshot snapshot, PlanningCandidate candidate, string target) =>
        Reason(snapshot.RequiredRiceVarietyId, candidate.RiceVarietyId, candidate.VarietyStatus, target);

    internal sealed record LotIdentity(Guid LotId, Guid? RiceVarietyId, string VarietyStatus);

    internal static LotIdentity[] Basis(IEnumerable<LotIdentity> identities) => identities
        .Distinct().OrderBy(x => x.LotId).ToArray();

    internal static Guid? FromDemand(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("requiredRiceVarietyId", out var value) &&
                value.ValueKind == JsonValueKind.String && value.TryGetGuid(out var id) && id != Guid.Empty ? id : null;
        }
        catch (JsonException) { return null; }
    }
}
