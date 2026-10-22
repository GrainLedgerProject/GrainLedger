using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;
using static GrainLedger.Api.IntegrationTests.LabReportTests;

namespace GrainLedger.Api.IntegrationTests;

// Explicitly synthetic inputs, never scientific approvals or production seeds.
internal sealed class ResidueScenario(LedgerScenario ledger)
{
    public LedgerScenario Ledger { get; } = ledger;
    public Guid SubstanceId { get; private set; }
    public Guid OtherSubstanceId { get; private set; }
    public const string Basis = "SYNTHETIC_AS_RECEIVED";
    public const string Review = "SYNTHETIC TEST ONLY; isolated regression fixture, not operational scientific approval.";

    public static async Task<ResidueScenario> CreateAsync(LedgerScenario ledger)
    {
        var scenario = new ResidueScenario(ledger);
        foreach (var other in new[] { false, true })
        {
            var substance = await SendAsync(ledger.Admin, HttpMethod.Post, "/api/active-substances", new
            {
                code = $"F12{Guid.NewGuid():N}", name = $"SYNTHETIC residue {(other ? "panel gap" : "analyte")}"
            });
            if (other) scenario.OtherSubstanceId = substance.GetProperty("id").GetGuid();
            else scenario.SubstanceId = substance.GetProperty("id").GetGuid();
        }
        return scenario;
    }

    public object Result(string? value = "0.15", string? uncertainty = "±0.05", string kind = "NUMERIC",
        string unit = "mg/kg", string? lod = "0.01", string? loq = "0.02", Guid? substanceId = null) => new
    {
        testLabel = "SYNTHETIC residue analyte", substanceId = substanceId ?? SubstanceId,
        originalResultText = kind == "NOT_DETECTED" ? "ND" : kind == "BELOW_LIMIT" ? $"<{value}" : value ?? "Unknown",
        resultKind = kind, numericValue = value, unit, lod, loq,
        method = "SYNTHETIC_METHOD", uncertainty, reference = Review
    };

    public async Task<(Guid ReportId, JsonElement Report)> ReportAsync(SampleIdentity sample, object[]? results = null,
        bool confirm = true, string kind = "FINAL", Guid? relatedReportId = null)
    {
        var request = ReportRequest([ResultSet(sample, results ?? [Result()])], kind, relatedReportId,
            relatedReportId is null ? null : Review);
        var reportId = EntityId(request);
        await SendAsync(Ledger.Customer, HttpMethod.Post, "/api/lab-reports", request);
        var report = await GetAsync(Ledger.Customer, $"/api/lab-reports/{reportId}");
        if (confirm)
        {
            await SendAsync(Ledger.Customer, HttpMethod.Post, $"/api/lab-reports/{reportId}/confirm", ConfirmRequest(report, sample));
            report = await GetAsync(Ledger.Customer, $"/api/lab-reports/{reportId}");
        }
        return (reportId, report);
    }

    public Task<JsonElement> EvaluateAsync(Guid lotId, Guid? evaluationId = null, Guid[]? substances = null, HttpClient? client = null) =>
        SendAsync(client ?? Ledger.Customer, HttpMethod.Post, $"/api/ledger/lots/{lotId}/residue-evaluations",
            new { id = evaluationId ?? Guid.NewGuid(), substanceIds = substances ?? [SubstanceId] });

    public static JsonElement ResultOf(JsonElement evaluation, Guid substanceId) =>
        evaluation.GetProperty("results").EnumerateArray().Single(x => x.GetProperty("substanceId").GetGuid() == substanceId);

    public static void Complete(JsonElement result, decimal lower, decimal upper)
    {
        Assert.Equal("COMPLETE", result.GetProperty("status").GetString());
        Assert.Equal(lower, Decimal(result.GetProperty("lower")));
        Assert.Equal(upper, Decimal(result.GetProperty("upper")));
        Assert.Empty(result.GetProperty("reasons").EnumerateArray());
    }

    public static void Unresolved(JsonElement result)
    {
        Assert.Equal("UNRESOLVED", result.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("lower").ValueKind);
        Assert.Equal(JsonValueKind.Null, result.GetProperty("upper").ValueKind);
        Assert.NotEmpty(result.GetProperty("reasons").EnumerateArray());
    }

    public JsonObject RuleBody(string interpretation = "NUMERIC_ABSOLUTE_UNCERTAINTY", string? ndThreshold = null,
        string kind = "INTERPRETATION", string input = "RAW_PADDY", string output = "RAW_PADDY",
        string lower = "1", string upper = "1", string semantics = "STEP_CONCENTRATION",
        string? basis = null, Guid? supersedesId = null, int version = 1, string? code = null) =>
        JsonSerializer.SerializeToNode(new
        {
            id = Guid.NewGuid(), code = code ?? $"SYNTHETIC-{Guid.NewGuid():N}", versionNo = version, kind,
            commodityId = (Guid?)Ledger.CatalogCommodityId, substanceId = SubstanceId,
            residueDefinition = "SYNTHETIC_ANALYTE", inputStage = input, outputStage = output,
            inputBasis = basis ?? Basis, outputBasis = basis ?? Basis,
            outputFraction = kind == "PROCESS" ? output : "", process = kind == "PROCESS" ? interpretation : "",
            conditions = kind == "PROCESS" ? "UNCONDITIONAL" : "", analyticalMethod = kind == "INTERPRETATION" ? "SYNTHETIC_METHOD" : "",
            unit = "mg/kg", decimalPlaces = 12,
            interpretationKind = kind == "INTERPRETATION" ? interpretation : null,
            ndThresholdKind = ndThreshold,
            factorLower = kind == "PROCESS" ? lower : null, factorUpper = kind == "PROCESS" ? upper : null,
            factorSemantics = kind == "PROCESS" ? semantics : null, homogeneousMixing = kind == "PROCESS",
            effectiveFrom = DateTimeOffset.UtcNow.AddYears(-1), effectiveTo = (DateTimeOffset?)null, supersedesId,
            evidenceRef = Review, applicabilityStatement = Review, reviewedBy = "Synthetic reviewer",
            reviewAuthority = "Isolated integration test", reviewedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        })!.AsObject();

    public async Task<Guid> PublishRuleAsync(JsonObject? body = null)
    {
        var request = body ?? RuleBody();
        await SendAsync(Ledger.Admin, HttpMethod.Post, "/api/residue-rules", request);
        return request["id"]!.GetValue<Guid>();
    }

    public Task<Guid> ProcessRuleAsync(string process, string input, string output, string lower = "1", string upper = "1",
        string semantics = "STEP_CONCENTRATION") =>
        PublishRuleAsync(RuleBody(process, kind: "PROCESS", input: input, output: output, lower: lower, upper: upper, semantics: semantics));

    public JsonObject SelectionBody(SampleIdentity sample, JsonElement report, string? uncertainty = "0.05",
        string? threshold = null, string? basis = null, Guid[]? supersededResults = null, Guid? supersedesId = null,
        Guid? substanceId = null) => JsonSerializer.SerializeToNode(new
        {
            id = Guid.NewGuid(), companyId = Ledger.OwnerId, sourceId = sample.SourceLotId,
            substanceId = substanceId ?? SubstanceId,
            selectedResultId = report.GetProperty("resultSets")[0].GetProperty("results").EnumerateArray()
                .Single(x => x.GetProperty("substanceId").GetGuid() == (substanceId ?? SubstanceId)).GetProperty("id").GetGuid(),
            supersededResultIds = supersededResults ?? [], materialBasis = basis ?? Basis,
            residueDefinition = "SYNTHETIC_ANALYTE", absoluteUncertainty = uncertainty, reportingThreshold = threshold,
            effectiveFrom = DateTimeOffset.UtcNow.AddYears(-1), effectiveTo = (DateTimeOffset?)null, supersedesId,
            evidenceRef = Review, applicabilityStatement = Review, reviewedBy = "Synthetic reviewer",
            reviewAuthority = "Isolated integration test", reviewedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        })!.AsObject();

    public async Task<Guid> SelectAsync(SampleIdentity sample, JsonElement report, string? uncertainty = "0.05",
        string? threshold = null, string? basis = null, Guid[]? supersededResults = null, Guid? supersedesId = null)
    {
        var body = SelectionBody(sample, report, uncertainty, threshold, basis, supersededResults, supersedesId);
        await SendAsync(Ledger.Customer, HttpMethod.Post, "/api/residue-report-selections", body);
        return body["id"]!.GetValue<Guid>();
    }

    public JsonObject GroupBody(Guid[] sourceIds, Guid[] supportingResultIds, string lower = "0.3", string upper = "0.8",
        string precedence = "UNTESTED_ONLY") => JsonSerializer.SerializeToNode(new
        {
            id = Guid.NewGuid(), companyId = Ledger.OwnerId, code = $"SYNTHETIC-GROUP-{Guid.NewGuid():N}", versionNo = 1,
            substanceId = SubstanceId, sourceIds, productStage = "RAW_PADDY", productIdentity = "RAW_PADDY",
            materialBasis = Basis, residueDefinition = "SYNTHETIC_ANALYTE", lower, upper, unit = "mg/kg", precedence,
            decimalPlaces = 12,
            supportingResultIds, effectiveFrom = DateTimeOffset.UtcNow.AddYears(-1), effectiveTo = (DateTimeOffset?)null,
            supersedesId = (Guid?)null, evidenceRef = Review, applicabilityStatement = Review,
            reviewedBy = "Synthetic reviewer", reviewAuthority = "Isolated integration test",
            reviewedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        })!.AsObject();

    public async Task<Guid> MixedLocationAsync(string stage = "BLENDED_RICE", string? product = null)
    {
        var locationId = Guid.NewGuid();
        await SendAsync(Ledger.Admin, HttpMethod.Post, "/api/ledger/locations", new
        {
            id = locationId, facilityId = Ledger.FacilityId, name = "SYNTHETIC F12 pool", kind = "STORE", mixedBulk = true
        });
        await SendAsync(Ledger.Admin, HttpMethod.Post, "/api/ledger/mixed-bulk-policies", new
        {
            id = Guid.NewGuid(), locationId, ownerId = Ledger.OwnerId, commodityId = Ledger.CommodityId,
            stage, productIdentity = product ?? stage, effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
            evidenceId = Ledger.EvidenceId, identityScopeConfirmed = true
        });
        return locationId;
    }

    public Task<JsonElement> BlendCompatibilityAsync(params string[] stages) =>
        SendAsync(Ledger.Admin, HttpMethod.Post, "/api/ledger/compatibilities", new
        {
            id = Guid.NewGuid(), facilityId = Ledger.FacilityId,
            inputProducts = stages.Distinct().Select(stage => $"{Ledger.CommodityId}:{stage}").ToArray(),
            outputCommodityId = Ledger.CommodityId, effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
            evidenceId = Ledger.EvidenceId
        });

    public async Task<(Guid LotId, long Revision)> PoolAsync(Guid locationId)
    {
        await using var connection = new NpgsqlConnection(Ledger.Factory.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT lot_id, revision FROM ledger.mixed_bulk_pools WHERE location_id=$1", connection);
        command.Parameters.AddWithValue(locationId);
        await using var row = await command.ExecuteReaderAsync();
        Assert.True(await row.ReadAsync());
        return (row.GetGuid(0), row.GetInt64(1));
    }

    public async Task<Guid> WithdrawAsync(Guid locationId, Guid lotId, long revision, string quantity)
    {
        var transfer = await SendAsync(Ledger.Operator, HttpMethod.Post, "/api/ledger/transfers", new
        {
            id = Guid.NewGuid(), lotId, fromLocationId = locationId, toLocationId = Ledger.SecondLocationId,
            quantityKg = quantity, poolRevision = revision, occurredAt = DateTimeOffset.UtcNow
        });
        return transfer.GetProperty("actualLotId").GetGuid();
    }

    public async Task<string> BusinessStateAsync()
    {
        // Physical projections, source facts, lab originals, HOLD and legal data must not change when evaluating.
        await using var connection = new NpgsqlConnection(Ledger.Factory.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT jsonb_build_object(
                'positions', (SELECT jsonb_agg(to_jsonb(t) ORDER BY lot_id, location_id) FROM ledger.positions t),
                'postings', (SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM ledger.postings t),
                'holds', (SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM ledger.holds t),
                'sources', (SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM capture.source_lots t),
                'procurements', (SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM capture.procurements t),
                'reports', (SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM capture.lab_reports t),
                'results', (SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM capture.lab_results t),
                'mrl', (SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM catalog.mrl_versions t))::text
            """, connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
