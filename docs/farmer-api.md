# Farmer API (backend)

The Capture module implements farmer registration and lookup. It uses the authenticated user's customer organization as `company_id`. The client cannot choose a company or actor.

## Routes

| Method | Route | Permission | Result |
| --- | --- | --- | --- |
| POST | `/api/farmers` | `farms.write` | Create a farmer; a replay of the same operation returns the prior result. |
| GET | `/api/farmers?search=&page=&pageSize=&sortBy=&sortDirection=` | `farms.read` or `farms.write` | Search current company farmers. Default page size 25; maximum 100; response includes `totalCount` and `hasMore`. `q` remains an alias for `search`. Sort fields: `name`, `code`, `createdAt`; direction: `asc` or `desc`. |
| GET | `/api/farmers/{id}` | `farms.read` or `farms.write` | Return an active farmer in current company; otherwise 404. |
| PUT | `/api/farmers/{id}` | `farms.write` | Edit own unreferenced farmer with `expectedVersion`. |
| DELETE | `/api/farmers/{id}` | `farms.write` | Soft-delete own unreferenced farmer with body `{ \"version\": 1 }`. |


List items include `createdBy`, `version`, `canEdit`, and `canDelete`. Server derives creator from authenticated actor; clients cannot set ownership. Only creator can edit or retire an active record. Legacy rows with `createdBy: null` are read-only. Edits and soft deletes require matching version; stale versions return 412. A farmer referenced by active plots or immutable procurements cannot be changed. Soft-deleted rows stay stored, disappear from active reads and selectors, and cannot be restored. Their unique codes remain reserved.
All routes require authentication. Use a Bearer access token to test them in Scalar. Cookie-authenticated write requests also require `X-CSRF-TOKEN` from `GET /api/auth/csrf`.

## Create request

```json
{
  "deviceId": "web-device-01",
  "clientOperationId": "5a43f7da-0a8c-47fc-bc90-041777867dbc",
  "capturedAt": "2026-10-08T03:00:00Z",
  "payloadSchemaVersion": 1,
  "payload": {
    "entityId": "5d7b2e9a-555c-4621-b17a-3fcda0d54ca9",
    "name": "Nguyễn Văn A",
    "contact": "Optional contact details"
  }
}
```

The client generates both UUIDs. Keep `entityId`, `clientOperationId`, `capturedAt`, `deviceId`, and payload unchanged when retrying a request after a network failure. `entityId` identifies the farmer; `clientOperationId` identifies the write attempt. The Collection Agent does not enter these technical identifiers.

The client normally omits `payload.code`. The API generates a farmer code such as `ND-5D7B2E9A555C` from `entityId` and returns it in the response. Existing integrations may still supply a code; if supplied, it must be nonblank and unique within the customer company. The database keeps the same `farmers.code` column and unique constraint.

On first success the API returns 201 with the farmer and its code. A retry with the same operation key and content returns 200 with the same farmer. A retry with changed content returns 409 `OPERATION_CONFLICT`. A new operation that reuses an existing farmer code returns 409.

The database writes the farmer, business event, and accepted operation in one transaction. A failure rolls them all back. The server derives `company_id`, actor, and creation time.

This endpoint accepts the offline operation envelope now; the batch synchronization route and browser queue are separate work.

## Local verification

### Test manually with Scalar

1. Start the local database with `docker compose up -d postgres`.
2. Apply migrations with `dotnet run --project backend/src/GrainLedger.Api -- --migrate`.
3. Start the API with `dotnet run --project backend/src/GrainLedger.Api`.
4. Open `http://localhost:5080/scalar/v1`. Scalar is available in Development only.
5. Call `POST /api/auth/login` with a verified `PurchasingStaff` account and copy only its `accessToken` value.
6. In Scalar, select **Authentication → Bearer** and paste the access token.
7. Call `POST /api/farmers` with the example body above. A Bearer-authenticated request does not need a CSRF token.
8. Use `GET /api/farmers` and `GET /api/farmers/{id}` to read the saved record.

Browser requests authenticated with a session cookie still need the `X-CSRF-TOKEN` header for writes. Bearer-authenticated requests do not. Docker Compose starts only PostgreSQL; it does not start the API or Scalar.

### Run automated integration tests

```powershell
dotnet build GrainLedger.slnx
dotnet test GrainLedger.slnx --filter FullyQualifiedName~FarmerTests
```

The integration tests start isolated PostgreSQL through Testcontainers. Docker Desktop must be running. The API migration command applies Identity before Capture:

```powershell
dotnet run --project backend/src/GrainLedger.Api -- --migrate
```

Do not point this command at a production database without the normal database backup and deployment procedure.

## F04 reference editing and duplicate warnings

The responsive web app reads `GET /api/farmers/{id}` and `GET /api/plots/{id}` before editing. It sends their current `version` as `expectedVersion` to the existing PUT endpoints. The farmer code and the plot's code/farmer link remain unchanged. Only the creator with `farms.write` can edit; records already used by a plot, procurement, or crop season may return `409 REFERENCE_IN_USE`. A stale version returns `412 REFERENCE_STALE`. The UI does not silently retry an uncertain PUT.

`POST /api/farmers/duplicate-check` accepts `{ "name": "Nguyễn Văn A", "contact": "0900 123 456", "excludeId": null }`. It returns up to five active records in the caller's customer company, with `SAME_NAME` and/or `SAME_CONTACT` reasons and `hasMore`. The matching is advisory: it normalizes Unicode names and whitespace, and removes simple phone separators. It does not merge or reject records.

`POST /api/plots/duplicate-check` accepts `{ "geometry": { "type": "Point", "coordinates": [106.35, 10.4] }, "excludeId": null }`. It returns up to five same-company active points within 20 m, with distance, `hasMore`, `radiusMeters: 20`, and `coverage: "POINT_ONLY"`. It does not compare polygon boundaries. Both endpoints require `farms.write`; cookie requests use the existing CSRF mechanism, while Bearer requests do not. No database migration is needed for these checks.

The plot map uses OpenStreetMap tiles only in Development. A production deployment can set `VITE_MAP_TILE_URL` to a same-origin tile path and `VITE_MAP_ATTRIBUTION` to the provider's required attribution in the web app's build environment. Without a configured tile path, the map shows a coordinate grid and the text coordinates remain available.
