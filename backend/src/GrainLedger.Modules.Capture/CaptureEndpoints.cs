using GrainLedger.Modules.Identity;
using GrainLedger.Modules.Ledger;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Npgsql;

namespace GrainLedger.Modules.Capture;

public static class CaptureEndpoints
{
    public static IEndpointRouteBuilder MapCaptureModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/capture").RequireAuthorization()
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>().AddEndpointFilter<CaptureErrorFilter>();
        group.MapGet("/farmers", async (CaptureService service, CancellationToken ct) => Results.Ok(await service.ListSourcesAsync<FarmerRequest>("farmers", ct)));
        group.MapPost("/farmers", async (FarmerRequest request, CaptureService service, CancellationToken ct) => Results.Ok(await service.CreateFarmerAsync(request, ct)));
        group.MapGet("/plots", async (CaptureService service, CancellationToken ct) => Results.Ok(await service.ListSourcesAsync<PlotRequest>("plots", ct)));
        group.MapPost("/plots", async (PlotRequest request, CaptureService service, CancellationToken ct) => Results.Ok(await service.CreatePlotAsync(request, ct)));
        group.MapGet("/seasons", async (CaptureService service, CancellationToken ct) => Results.Ok(await service.ListSourcesAsync<SeasonRequest>("seasons", ct)));
        group.MapPost("/seasons", async (SeasonRequest request, CaptureService service, CancellationToken ct) => Results.Ok(await service.CreateSeasonAsync(request, ct)));
        group.MapGet("/plot-seasons", async (CaptureService service, CancellationToken ct) => Results.Ok(await service.ListSourcesAsync<PlotSeasonRequest>("plot_seasons", ct)));
        group.MapPost("/plot-seasons", async (PlotSeasonRequest request, CaptureService service, CancellationToken ct) => Results.Ok(await service.CreatePlotSeasonAsync(request, ct)));
        group.MapGet("/receipts", async (CaptureService service, CancellationToken ct) => Results.Ok(await service.ListReceiptsAsync(ct)));
        group.MapGet("/receipts/{id:guid}", async (Guid id, CaptureService service, CancellationToken ct) => Results.Ok(await service.GetReceiptAsync(id, ct)));
        group.MapPost("/receipts", async (ReceiptRequest request, CaptureService service, CancellationToken ct) => Results.Ok(await service.CreateReceiptAsync(request, ct)));
        group.MapPut("/receipts/{id:guid}", async (Guid id, UpdateReceiptRequest request, CaptureService service, CancellationToken ct) => Results.Ok(await service.UpdateReceiptAsync(id, request, ct)));
        group.MapPost("/receipts/{id:guid}/commit", async (Guid id, CommitReceiptRequest request, CaptureService service, CancellationToken ct) => Results.Ok(await service.CommitReceiptAsync(id, request, ct)));
        group.MapPost("/evidence", async (EvidenceRequest request, CaptureService service, CancellationToken ct) => Results.Ok(await service.StoreEvidenceAsync(request, ct)));
        group.MapGet("/evidence/{id:guid}", async (Guid id, LedgerService ledger, CancellationToken ct) =>
        {
            var evidence = await ledger.ReadEvidenceAsync(id, ct);
            return Results.File(evidence.Content, evidence.ContentType, evidence.FileName);
        });
        group.MapGet("/arrivals", async (CaptureService service, CancellationToken ct) => Results.Ok(await service.ListArrivalsAsync(ct)));
        group.MapGet("/arrivals/{id:guid}", async (Guid id, CaptureService service, CancellationToken ct) => Results.Ok(await service.GetArrivalAsync(id, ct)));
        group.MapPost("/arrivals", async (ArrivalRequest request, CaptureService service, CancellationToken ct) => Results.Ok(await service.CreateArrivalAsync(request, ct)));
        group.MapPost("/arrivals/{id:guid}/corrections", async (Guid id, ArrivalCorrectionRequest request, CaptureService service, CancellationToken ct) => Results.Ok(await service.CorrectArrivalAsync(id, request, ct)));
        group.MapPost("/arrivals/{id:guid}/investigations", async (Guid id, ArrivalInvestigationRequest request, CaptureService service, CancellationToken ct) => Results.Ok(await service.InvestigateArrivalAsync(id, request, ct)));
        group.MapPost("/receipts/{id:guid}/corrections", async (Guid id, ReceiptCorrectionRequest request, CaptureService service, CancellationToken ct) => Results.Ok(await service.RequestReceiptCorrectionAsync(id, request, ct)));
        group.MapGet("/receipt-corrections", async (CaptureService service, CancellationToken ct) => Results.Ok(await service.ListReceiptCorrectionsAsync(ct)));
        group.MapGet("/receipt-corrections/{id:guid}", async (Guid id, CaptureService service, CancellationToken ct) => Results.Ok(await service.GetReceiptCorrectionAsync(id, ct)));
        group.MapPost("/receipt-corrections/{id:guid}/reconcile", async (Guid id, ReceiptReconciliationRequest request, CaptureService service, CancellationToken ct) => Results.Ok(await service.ReconcileReceiptCorrectionAsync(id, request, ct)));
        group.MapPost("/receipt-corrections/{id:guid}/commit", async (Guid id, CommitCorrectionRequest request, CaptureService service, CancellationToken ct) => Results.Ok(await service.CommitReceiptCorrectionAsync(id, request, ct)));
        return endpoints;
    }
}

public sealed class CaptureErrorFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { return await next(context); }
        catch (CaptureFault fault) { return Results.Problem(statusCode: fault.StatusCode, title: fault.Message); }
        catch (LedgerException fault) { return Results.Problem(statusCode: fault.StatusCode, title: fault.Message); }
        catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.CheckViolation or PostgresErrorCodes.RaiseException)
        {
            var title = exception.SqlState switch
            {
                PostgresErrorCodes.UniqueViolation => "Record UUID or source identity already exists; conflicting retries are rejected.",
                PostgresErrorCodes.ForeignKeyViolation => "Source record does not exist or persisted source ownership disagrees.",
                _ => exception.MessageText
            };
            return Results.Problem(statusCode: exception.SqlState == PostgresErrorCodes.ForeignKeyViolation ? 400 : 409, title: title);
        }
    }
}
