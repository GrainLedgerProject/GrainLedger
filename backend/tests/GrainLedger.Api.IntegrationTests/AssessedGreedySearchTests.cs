using System.Text.Json;
using GrainLedger.Modules.Exports;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class AssessedGreedySearchTests
{
    [Fact]
    public async Task Replaces_physical_first_choice_when_assessment_fails()
    {
        var commodity = Guid.NewGuid();
        PlanningCandidate Candidate(decimal kg) => new(Guid.NewGuid(), "SYNTHETIC",
            Guid.NewGuid(), "Synthetic location", Guid.NewGuid(), commodity,
            "HEAD_RICE", "HEAD_RICE", kg, kg, "ORDINARY", false, false, null,
            Guid.NewGuid().ToString(), 1, true, true, [], []);
        var bad = Candidate(10);
        var goodA = Candidate(6);
        var goodB = Candidate(4);
        var result = await AssessedGreedySearch.SearchAsync([bad, goodA, goodB], 10,
            "HEAD_RICE", commodity, JsonSerializer.SerializeToElement(Array.Empty<object>()),
            (selection, _) => Task.FromResult(new BlendAssessmentResult(
                selection.Allocations.Any(x => x.LotId == bad.LotId) ? "FAIL" : "PASS", 10,
                [])), 10, CancellationToken.None);
        Assert.Equal("PASS", result.Outcome);
        Assert.Equal(2, result.Iterations);
        Assert.DoesNotContain(result.Selection!.Allocations, x => x.LotId == bad.LotId);
        Assert.Equal(10, result.Selection.SelectedMassKg);
    }

    [Fact]
    public async Task Iteration_cap_does_not_claim_global_infeasibility()
    {
        var commodity = Guid.NewGuid();
        var candidate = new PlanningCandidate(Guid.NewGuid(), "SYNTHETIC", Guid.NewGuid(),
            "Synthetic location", Guid.NewGuid(), commodity, "HEAD_RICE", "HEAD_RICE",
            10, 10, "ORDINARY", false, false, null, "revision", 1, true, true, [], []);
        var result = await AssessedGreedySearch.SearchAsync([candidate], 10, "HEAD_RICE",
            commodity, JsonSerializer.SerializeToElement(Array.Empty<object>()),
            (_, _) => Task.FromResult(new BlendAssessmentResult("INCOMPLETE", null, [])),
            1, CancellationToken.None);
        Assert.Equal("SEARCH_EXHAUSTED", result.Outcome);
        Assert.Equal("INCOMPLETE", result.Assessment?.Status);
        Assert.Contains("ITERATION_LIMIT_REACHED", result.Reasons);
    }
}
