using System.Text.Json;
using GrainLedger.Modules.Exports;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class GreedyStockSelectionTests
{
    [Fact]
    public void Blend_selection_covers_input_required_by_explicit_yield()
    {
        var commodity = Guid.NewGuid();
        var inputCommodity = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var lot = new PlanningCandidate(Guid.NewGuid(), "SYNTHETIC", Guid.NewGuid(),
            "Test location", inputCommodity, commodity, "HEAD_RICE", "HEAD_RICE",
            12, 12, "ORDINARY", false, false, null, "revision", 1, true, true, [], []);
        var policy = JsonSerializer.SerializeToElement(new[] { new {
            id = policyId, outputCatalogCommodityId = commodity,
            inputProducts = new[] { inputCommodity + ":HEAD_RICE" }
        } });

        var result = GreedyStockSelection.Select([lot], 10, "BLENDED_RICE", commodity,
            policy, id => id == policyId ? 12 : null);

        Assert.Equal("MASS_COVERED", result.Outcome);
        Assert.Equal("BLEND_INPUT_STOCK", result.Mode);
        Assert.Equal(12, result.SelectedMassKg);
    }

    [Fact]
    public void Direct_stock_never_uses_a_different_catalog_commodity_with_the_same_product_identity()
    {
        var expectedCommodity = Guid.NewGuid();
        var wrongCommodity = Guid.NewGuid();
        PlanningCandidate Candidate(Guid commodity, decimal kg) => new(
            Guid.NewGuid(), "TEST-LOT", Guid.NewGuid(), "Test location", Guid.NewGuid(), commodity,
            "HEAD_RICE", "HEAD_RICE", kg, kg, "ORDINARY", false, false, null,
            Guid.NewGuid().ToString(), 1, true, true, [], []);
        var correct = Candidate(expectedCommodity, 4);
        var wrong = Candidate(wrongCommodity, 100);

        var result = GreedyStockSelection.Select([wrong, correct], 10, "HEAD_RICE",
            expectedCommodity, JsonSerializer.SerializeToElement(Array.Empty<object>()));

        Assert.Equal("SEARCH_EXHAUSTED", result.Outcome);
        Assert.Equal(4, result.SelectedMassKg);
        Assert.Equal(correct.LotId, Assert.Single(result.Allocations).LotId);
    }
}
