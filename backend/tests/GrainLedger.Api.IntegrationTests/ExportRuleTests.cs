using GrainLedger.Modules.Exports;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ExportRuleTests
{
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
