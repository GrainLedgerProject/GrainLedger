using GrainLedger.Modules.Exports;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ExportRuleTests
{
    [Theory]
    [InlineData("RICE", "HEAD_RICE", true)]
    [InlineData("RICE", "RAW_PADDY", false)]
    [InlineData("RICE", "BRAN", false)]
    [InlineData("RICE_POLISHED", "BLENDED_RICE", true)]
    [InlineData("RICE_BRAN", "BRAN", true)]
    [InlineData("PADDY", "HEAD_RICE", true)]
    [InlineData("RICE_HUSKED", "HEAD_RICE", false)]
    [InlineData("CUSTOM", "HEAD_RICE", false)]
    public void Target_product_matches_supported_catalog_commodity(string commodityCode, string product, bool allowed)
    {
        Assert.Equal(allowed, ExportTargetProducts.ForCommodity(commodityCode).Contains(product));
    }

    [Theory]
    [InlineData("0.000000000001")]
    [InlineData("0.0000001")]
    [InlineData("999999999999.999999999999")]
    public void Quality_bounds_round_trip_without_six_place_mass_rounding(string input)
    {
        Assert.True(ExportRules.TryBound(input, out var value));
        Assert.Equal(input, ExportRules.BoundText(value!.Value));
    }
}
