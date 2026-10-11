using System.Text.Json;
using GrainLedger.Modules.Capture;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class PlotGeometryTests
{
    [Theory]
    [InlineData("{\"type\":\"Point\",\"coordinates\":[106.35,10.4]}", true)]
    [InlineData("{\"type\":\"Point\",\"coordinates\":[106.35,100]}", false)]
    [InlineData("{\"type\":\"Point\",\"coordinates\":[10.4,106.35]}", false)]
    [InlineData("{\"type\":\"Polygon\",\"coordinates\":[[[106,10],[107,10],[107,11],[106,11],[106,10]]]}", true)]
    [InlineData("{\"type\":\"Polygon\",\"coordinates\":[[[106,10],[107,11],[106,11],[107,10],[106,10]]]}", false)]
    [InlineData("{\"type\":\"Polygon\",\"coordinates\":[[[106,10],[107,10],[107,11],[106,11]]]}", false)]
    public void Accepts_only_valid_wgs84_point_or_polygon(string input, bool expected)
    {
        using var document = JsonDocument.Parse(input);
        Assert.Equal(expected, PlotGeometry.TryNormalize(document.RootElement, out _));
    }
}
