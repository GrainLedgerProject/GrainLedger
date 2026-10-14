# Farmer API (backend)

The Capture module implements farmer registration and lookup. It uses the authenticated user's customer organization as `company_id`. The client cannot choose a company or actor.

## Routes

| Method | Route | Permission | Result |
| --- | --- | --- | --- |
| POST | `/api/farmers` | `farms.write` | Create a farmer; a replay of the same operation returns the prior result. |
| GET | `/api/farmers?q=&page=&pageSize=` | `farms.read` or `farms.write` | Search the current company's farmers. Default page size 20; maximum 100. |
| GET | `/api/farmers/{id}` | `farms.read` or `farms.write` | Return a farmer in the current company; otherwise 404. |

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
