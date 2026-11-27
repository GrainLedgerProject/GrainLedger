using System.Text.Json;
using GrainLedger.Api.Features.Exports;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ManualBlendAdjustmentTests
{
    private static readonly Guid Commodity = Guid.NewGuid(), Variety = Guid.NewGuid(), Location = Guid.NewGuid();
    private static readonly Guid Head = Guid.NewGuid(), Broken = Guid.NewGuid(), Extra = Guid.NewGuid();
    private static readonly Guid BothPolicy = Guid.NewGuid(), HeadPolicy = Guid.NewGuid();
    private static PlanningCandidate Item(Guid id, string product) => new(id, product, Location, "Store", Commodity,
        Commodity, product, product, 20, 20, "ORDINARY", false, false, null, "current", 1, true, true, [], [],
        RiceVarietyId: Variety, VarietyStatus: "SINGLE");
    private static PlanningCandidateSnapshot Snapshot() => new(Guid.NewGuid(), Guid.NewGuid(), 1, 1, "PLANNED",
        Commodity, 10, DateTimeOffset.UtcNow, "scope", JsonSerializer.SerializeToElement(new {}),
        JsonSerializer.SerializeToElement(new[] {
            new { id = BothPolicy, outputCatalogCommodityId = Commodity,
                inputProducts = new[] { $"{Commodity}:HEAD_RICE", $"{Commodity}:BROKEN_RICE" } },
            new { id = HeadPolicy, outputCatalogCommodityId = Commodity, inputProducts = new[] { $"{Commodity}:HEAD_RICE" } }
        }), [Item(Head, "HEAD_RICE"), Item(Broken, "BROKEN_RICE"), Item(Extra, "HEAD_RICE")], RequiredRiceVarietyId: Variety);
    private static GreedyStockSelection.Result Initial() => new("MASS_COVERED", "BLEND_INPUT_STOCK", BothPolicy, 10, 0,
        [new(Head, Location, "Head", 9, "old"), new(Broken, Location, "Broken", 1, "old")]);
    private static CandidateGroupResult Validate(PlanningCandidateSnapshot snapshot, GreedyStockSelection.Result result) =>
        CandidateGroupValidator.Validate(snapshot, result.Allocations.Select(a => new CandidateGroupItem(a.LotId,
            a.LocationId, a.QuantityKg, a.CandidateRevision)).ToArray(), "BLENDED_RICE", result.Mode, "scope", result.CompatibilityPolicyId, true);

    [Fact]
    public void Removing_broken_and_adding_head_resolves_an_approved_head_only_policy()
    {
        var snapshot = Snapshot();
        var result = ManualBlendAdjustment.Build(snapshot, Initial(), [new(Head, Location, "6"), new(Extra, Location, "4")],
            "BLENDED_RICE", _ => true, out var error);
        Assert.Null(error); Assert.NotNull(result);
        Assert.Equal(HeadPolicy, result.CompatibilityPolicyId);
        Assert.All(result.Allocations, a => Assert.Equal("current", a.CandidateRevision));
        Assert.Equal("VALID", Validate(snapshot, result).Status);
    }

    [Theory]
    [InlineData("9.5")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1e1")]
    [InlineData("9")]
    public void Malformed_fractional_or_changed_total_is_rejected(string kg)
    {
        Assert.Null(ManualBlendAdjustment.Build(Snapshot(), Initial(), [new(Head, Location, kg)], "BLENDED_RICE", _ => true, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Duplicate_and_out_of_scope_positions_are_rejected()
    {
        Assert.Null(ManualBlendAdjustment.Build(Snapshot(), Initial(), [new(Head, Location, "5"), new(Head, Location, "5")], "BLENDED_RICE", _ => true, out _));
        Assert.Null(ManualBlendAdjustment.Build(Snapshot(), Initial(), [new(Guid.NewGuid(), Location, "10")], "BLENDED_RICE", _ => true, out _));
    }

    [Theory]
    [InlineData("stock", "QUANTITY_EXCEEDS_AVAILABLE")]
    [InlineData("variety", "VARIETY_MISMATCH")]
    [InlineData("hold", "HELD")]
    public void Shared_validation_rejects_excess_stock_wrong_variety_and_restricted_positions(string change, string expected)
    {
        var snapshot = Snapshot();
        snapshot = snapshot with { Candidates = snapshot.Candidates.Select(c => c.LotId != Head ? c : change switch {
            "stock" => c with { AvailableKg = 5 },
            "variety" => c with { RiceVarietyId = Guid.NewGuid() },
            _ => c with { InventoryEligible = false, Reasons = ["HELD"] }
        }).ToArray() };
        var result = ManualBlendAdjustment.Build(snapshot, Initial(), [new(Head, Location, "10")], "BLENDED_RICE", _ => true, out _)!;
        Assert.Contains(expected, Validate(snapshot, result).Findings);
    }

    [Fact]
    public void Removing_a_product_cannot_invent_a_missing_compatibility_or_yield()
    {
        var snapshot = Snapshot();
        var result = ManualBlendAdjustment.Build(snapshot, Initial(), [new(Head, Location, "10")], "BLENDED_RICE", _ => false, out _)!;
        Assert.Null(result.CompatibilityPolicyId);
        Assert.Contains("COMPATIBILITY_POLICY_MISSING", Validate(snapshot, result).Findings);
    }
}
