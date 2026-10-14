# Crop season and plot-season API

This Capture step follows Report 3 §3.3.1, GrainLedger V2 F04–F07, and the approved DB diagram. A crop season belongs to one customer company. A plot-season links one plot to one crop season and records its rice variety. A plot can have different varieties in different seasons.

## Sequence

1. An Administrator with `platform.configure` creates a season: `POST /api/crop-seasons` with `companyId`, `name`, `startsOn`, `endsOn` (`yyyy-MM-dd`). For example, `{"companyId":"ff710fe4-2d7a-4920-92b8-76617a871830","name":"Dong Xuan","startsOn":"2026-11-01","endsOn":"2027-03-31"}`. This online configuration call does not use the offline envelope.
2. The Administrator reviews seasons with `GET /api/crop-seasons?companyId={customerCompanyId}`. A PurchasingStaff user calls `GET /api/crop-seasons` to see only their company's seasons, then selects a plot from `GET /api/plots`. A customer user cannot select another company's `companyId`. Season detail is `GET /api/crop-seasons/{id}`.

Do not send `?companyId=` with an empty value. A present but empty or invalid company ID returns HTTP 400 for either role. The Administrator must provide a valid customer company ID; PurchasingStaff can omit the parameter.
3. The client submits `POST /api/plot-seasons` with the same offline envelope as Farmer and Plot. The user enters only the rice variety and selects the plot and season; the client supplies the UUIDs and capture metadata.
4. Use `GET /api/plot-seasons?plotId={id}&seasonId={id}` or `GET /api/plot-seasons/{id}` to read the result. Plot-season list and detail responses include `plotCode` and `seasonName`.

In the web Ledger, **Setup** combines the commodity/unit Catalog with facility setup. **Receive stock** exposes source records to `farms.read`/`farms.write` users, source registration to `farms.write`, and procurement entry only with `purchases.write` and source-read access. Administrators select a customer company before creating its crop season there. Source registration steps are farmer, plot, administrator-created crop-season prerequisite, then plot-season link with rice variety; the season does not depend on a plot. Source-record pagination also supplies the current selection options.

All four Capture reference lists use `page` (default 1), `pageSize` (default 25, maximum 100), `totalCount`, and `hasMore`. `search` accepts `q` as alias. Sort direction is `asc` or `desc`; sort fields are farmer `name|code|createdAt`, plot `code|areaHa`, season `name|startsOn`, and plot-season `id`. Plot lists support `farmerId`; plot-season lists support `plotId` and `seasonId`.

Farmers, plots, seasons, and plot-seasons return `createdBy`, `version`, `canEdit`, and `canDelete`. Only creator can edit or soft-delete an unreferenced active record; `PUT` sends the normal fields plus `expectedVersion` (crop-season `companyId` must remain unchanged), and `DELETE` sends `{ "version": 1 }`. Stale versions return 412. Legacy records with no creator remain read-only. Dependent active references and immutable procurement/spray history block mutation. Soft-deleted rows remain stored, disappear from lists and selectors, cannot be restored, and retain reserved unique values.

Example registration request:

```json
{
  "deviceId": "web-test-01",
  "clientOperationId": "e22ee714-2759-4eaa-91c2-71ea287a73ef",
  "capturedAt": "2026-10-11T09:00:00Z",
  "payloadSchemaVersion": 1,
  "payload": {
    "entityId": "6178312b-7c52-43e1-972d-944e4e5c31d1",
    "plotId": "11408862-f173-49d8-9f02-fe57483bd6e6",
    "seasonId": "d2f3a912-e0a4-4bf3-8ef8-df0f64477662",
    "riceVariety": "ST25"
  }
}
```

The plot and season must already exist in the signed-in customer's company. Re-send exactly the same request with the same operation ID after an uncertain network result; the API returns the saved result without creating a second link. A different request with the same operation ID is rejected. A plot-season pair may occur only once.

The migration `AddCaptureCropAndPlotSeasons` adds `capture.crop_seasons` and `capture.plot_seasons` with company-scoped foreign keys, date CHECK, and unique plot-season pair. Apply only the Capture migration to a verified target database; do not run the API-wide `--migrate` command for this step. The API process already running on port 5080 must be restarted after building to expose the new routes.
