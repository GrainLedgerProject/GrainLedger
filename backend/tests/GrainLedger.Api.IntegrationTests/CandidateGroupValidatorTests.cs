using System.Text.Json;
using GrainLedger.Modules.Exports;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class CandidateGroupValidatorTests
{
    private static readonly Guid TargetCommodity = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid OtherCommodity = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Policy = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Head = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid Broken = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid Location = Guid.Parse("66666666-6666-4666-8666-666666666666");

    private static PlanningCandidate Item(Guid lot, string product, Guid catalogCommodity, decimal available) =>
        new(lot, product, Location, "Store", OtherCommodity, catalogCommodity,
            product, product, available, available, "ORDINARY", false, false, null,
            "candidate-r1", 1, true, true, [], []);

    private static PlanningCandidateSnapshot Snapshot() => new(Guid.NewGuid(), Guid.NewGuid(),
        1, 1, "PLANNED", TargetCommodity, 10m, DateTimeOffset.UtcNow, "scope-r1",
        JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new[]
        {
            new { id = Policy, outputCatalogCommodityId = TargetCommodity,
                inputProducts = new[] { $"{OtherCommodity}:HEAD_RICE", $"{OtherCommodity}:BROKEN_RICE" } }
        }), [Item(Head, "HEAD_RICE", OtherCommodity, 12m),
            Item(Broken, "BROKEN_RICE", OtherCommodity, 18m)]);

    [Fact]
    public void Cross_commodity_blend_checks_the_whole_policy_set_and_requires_output_yield()
    {
        var snapshot = Snapshot();
        var selected = new[] { new CandidateGroupItem(Head, Location, 4m, "candidate-r1"),
            new CandidateGroupItem(Broken, Location, 6m, "candidate-r1") };
        var result = CandidateGroupValidator.Validate(snapshot, selected, "BLENDED_RICE",
            "BLEND_INPUT_STOCK", "scope-r1", Policy);
        Assert.Equal("INCOMPLETE", result.Status);
        Assert.Equal(10m, result.InputMassKg);
        Assert.Equal(["OUTPUT_YIELD_POLICY_MISSING"], result.Findings);
        Assert.Contains("COMPATIBILITY_SET_MISMATCH", CandidateGroupValidator.Validate(snapshot,
            selected[..1], "BLENDED_RICE", "BLEND_INPUT_STOCK", "scope-r1", Policy).Findings);
        var insufficient = CandidateGroupValidator.Validate(snapshot,
            [new CandidateGroupItem(Head, Location, 3m, "candidate-r1"),
             new CandidateGroupItem(Broken, Location, 6m, "candidate-r1")],
            "BLENDED_RICE", "BLEND_INPUT_STOCK", "scope-r1", Policy);
        Assert.Equal("INVALID", insufficient.Status);
        Assert.Contains("TARGET_MASS_EXCEEDS_INPUT", insufficient.Findings);
    }

    [Fact]
    public void Direct_selection_rejects_other_commodity_duplicate_stale_and_excess_stock()
    {
        var snapshot = Snapshot();
        var item = new CandidateGroupItem(Head, Location, 13m, "old-revision");
        var result = CandidateGroupValidator.Validate(snapshot, [item, item], "HEAD_RICE",
            "DIRECT_STOCK", "old-scope");
        Assert.Equal("INVALID", result.Status);
        Assert.Contains("DIRECT_PRODUCT_MISMATCH", result.Findings);
        Assert.Contains("DUPLICATE_POSITION", result.Findings);
        Assert.Contains("CANDIDATE_SNAPSHOT_STALE", result.Findings);
        Assert.Contains("QUANTITY_EXCEEDS_AVAILABLE", result.Findings);
    }
}
