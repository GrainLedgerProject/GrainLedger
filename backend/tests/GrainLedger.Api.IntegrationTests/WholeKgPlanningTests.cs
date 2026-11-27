using System.Text.Json;
using GrainLedger.Api.Features.Exports;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class WholeKgPlanningTests
{
    private static PlanningCandidate Stock(decimal kg) => new(Guid.NewGuid(), "WEIGHABLE", Guid.NewGuid(), "Store", Guid.NewGuid(), Guid.NewGuid(),
        "HEAD_RICE", "HEAD_RICE", kg, kg, "ORDINARY", false, false, null, "r", 1, true, true, [], []);

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void Every_objective_solves_with_integer_kg_and_floored_stock(int value)
    {
        var priority = (BlendPlanningObjective)value;
        var candidates = new[] { Stock(10.9m), Stock(14.1m), Stock(.9m) };
        var costs = priority is BlendPlanningObjective.LowestMoisture or BlendPlanningObjective.RetainHeadRice ? new[] { 1m, 2m, 0m } : null;
        decimal[][]? ratios = priority == BlendPlanningObjective.ResidueHeadroom ? [[.9m,.2m,.6m],[.1m,.9m,.3m]] : null;
        var result = BlendMilpSolver.Solve(candidates, 20, [], priority, 2000, costs, ratios);
        Assert.Equal("OPTIMAL", result.Status);
        var kg = Assert.IsType<decimal[]>(result.Quantities);
        Assert.Equal(20m, kg.Sum());
        for (var i = 0; i < kg.Length; i++)
        {
            Assert.Equal(decimal.Truncate(kg[i]), kg[i]);
            Assert.InRange(kg[i], 0, decimal.Floor(candidates[i].AvailableKg));
        }
        Assert.Equal(0m, kg[2]);
    }

    [Fact]
    public void Fraction_only_solution_is_infeasible_in_the_model_instead_of_rounded_afterwards()
    {
        var result = BlendMilpSolver.Solve([Stock(1), Stock(1)], 1, [new(0,0,new decimal[] { 1,-1 })], BlendPlanningObjective.None, 1000);
        Assert.Equal("INFEASIBLE", result.Status);
        Assert.Null(result.Quantities);
    }

    [Fact]
    public void Fractional_remnants_and_noninteger_direct_targets_cannot_be_selected()
    {
        var empty = BlendMilpSolver.Solve([Stock(.9m),Stock(.9m)], 1, [], BlendPlanningObjective.None, 1000);
        Assert.Equal("INFEASIBLE", empty.Status);
        Assert.Null(empty.Quantities);
        Assert.Equal("WHOLE_KG_TARGET_REQUIRED", BlendMilpSolver.Solve([Stock(10)], 9.5m, [], BlendPlanningObjective.None, 1000).Status);
    }

    [Theory]
    [InlineData("1000","0.9","1112")]
    [InlineData("103.987","1","104")]
    [InlineData("1","0.1234567","10")]
    public void Yield_input_is_whole_and_output_keeps_ledger_precision(string target, string fraction, string expected)
    {
        decimal Read(string s) => decimal.Parse(s,System.Globalization.CultureInfo.InvariantCulture);
        var input = AssessedMilpSearch.MinimumBlendInput(Read(target),Read(fraction));
        Assert.Equal(Read(expected),input);
        var output = input!.Value*Read(fraction);
        Assert.True(output>=Read(target));
        Assert.Equal(decimal.Round(output,6),output);
    }

    [Fact]
    public void Fallback_does_not_spend_fractional_stock_or_add_dust_anchors()
    {
        var commodity = Guid.NewGuid();var ledger = Guid.NewGuid();var policy = Guid.NewGuid();
        var a = Stock(10.9m) with { CatalogCommodityId=commodity,LedgerCommodityId=ledger };
        var b = Stock(20.8m) with { CatalogCommodityId=commodity,LedgerCommodityId=ledger,Stage="BROKEN_RICE",ProductIdentity="BROKEN_RICE" };
        var policies = JsonSerializer.SerializeToElement(new[] { new { id=policy,outputCatalogCommodityId=commodity,inputProducts=new[] { ledger+":HEAD_RICE",ledger+":BROKEN_RICE" } } });
        var result = GreedyStockSelection.Select([a,b],25,"BLENDED_RICE",commodity,policies);
        Assert.Equal("MASS_COVERED",result.Outcome);
        Assert.Equal(25m,result.Allocations.Sum(x=>x.QuantityKg));
        Assert.All(result.Allocations,x=>Assert.True(x.QuantityKg>=1 && decimal.Truncate(x.QuantityKg)==x.QuantityKg));
    }
}
