using System.Text.Json;
using GrainLedger.Modules.Capture;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class SprayPayloadContractTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Spray_item_accepts_product_and_substance_without_rate()
    {
        var item = JsonSerializer.Deserialize<SprayItemInput>(
            """{"productName":"Test product","substanceId":null}""", Options);

        Assert.NotNull(item);
        Assert.Equal("Test product", item.ProductName);
        Assert.Null(item.SubstanceId);
    }

    [Theory]
    [InlineData("rate")]
    [InlineData("rateUnit")]
    public void Spray_item_rejects_legacy_rate_fields(string field)
    {
        var json = $$"""{"productName":"Test product","substanceId":null,"{{field}}":"1"}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SprayItemInput>(json, Options));
    }
}
