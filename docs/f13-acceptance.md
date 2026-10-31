# F13 acceptance — 20 October 2026

## Environment

The production web build ran on `http://localhost:5183` through the opt-in `BrowserWorkflowHost`, backed by disposable PostgreSQL 17 and the restricted runtime role. Browser sessions used the persisted Identity session checks and CSRF protection. All accounts, catalogs, orders and stock were synthetic. No shared database writes or migrations were performed for this acceptance.

## Fix and regression coverage

An unreadable success response can follow a committed write. Export forms now retain the original payload and operation ID for response-reading failures, unknown client failures and HTTP 408/5xx. Create also checks that the response contains an order ID before clearing its saved request. The form stays locked until replay succeeds or the API definitively rejects the request.

Frontend regression tests cover network errors, corrupt JSON, empty create responses, missing order IDs, and corrupt responses when editing an order or target. Reload/remount preserves the request, including the original If-Match header for edits.

New PostgreSQL tests exercise order-header and container-target edits: successful writes, over-allocation rejection, original quantity/unit retention, stale versions, changed-payload operation conflicts, replay after a later edit, one audit event per successful mutation and unchanged Ledger stock.

## Browser results

| Scenario | Observed result |
| --- | --- |
| Create a 48 T order with moisture maximum 14% and a 24 T target | Server generated one order and one planned target. |
| Corrupt the HTTP 201 body after the server commits | Form showed the uncertain-result message and disabled editing. |
| Reload the page and retry | Form remained locked before retry; replay opened the original order. Database contained exactly one order. |
| Return to the list and reopen | Saved values and generated codes were present. |
| Change required mass to 50 T | Detail showed 50,000 kg required and 26,000 kg remaining. |
| Save specification v2 with maximum 13%, then select v1 | v1 retained 14%; v2 retained 13%. |
| Add 27 T when only 26 T remains | API rejected the request and FE showed the allocation error. |
| Add a valid 24 T target | Two planned targets appeared. |
| Edit the first target on a 390 px viewport | 27 T was rejected; 25 T was saved. |
| Add nineteen 10 kg targets through the UI | 21 targets and 24 audit events were available. Container and history Next controls opened page 2. |
| Inspect create and detail at 375 px | No document-level horizontal overflow; the create form used one column. Desktop used two columns. |
| Open as CustomerManager | Order was readable; Edit order and Add container target were absent. |

After the browser workflow, the test bridge reported:

```json
{
  "stockUnchanged": true,
  "stock": { "massKg": 10, "postings": 1, "positions": 1 },
  "orders": [{ "code": "EXP-20261020-000001", "version": 24,
    "requiredMassKg": 50000, "currentSpecificationVersion": 2 }],
  "containerCount": 21,
  "eventCount": 24
}
```

The total planned mass was 49,190 kg; the remaining mass was 810 kg. The failed allocations and the replay did not create extra audit events.

## Repeatable checks

Run from the repository root:

```powershell
dotnet test GrainLedger.slnx --filter 'FullyQualifiedName~ExportOrderTests|FullyQualifiedName~ExportRuleTests'
npm test --workspace @grainledger/web
npm run build --workspace @grainledger/web
```

To start the optional browser host after building the frontend:

```powershell
$env:GRAINLEDGER_BROWSER_SESSION_FILE = Join-Path ([IO.Path]::GetTempPath()) ('grainledger-f13-' + [guid]::NewGuid().ToString('N') + '.json')
$env:GRAINLEDGER_BROWSER_PORT = '5183'
$env:GRAINLEDGER_BROWSER_UI_DIST = Join-Path (Get-Location) 'apps/web/dist'
dotnet test backend/tests/GrainLedger.Api.IntegrationTests --filter FullyQualifiedName~BrowserWorkflowHost
```

Open `http://localhost:5183/__browser/session/export` for ExportStaff or `/__browser/session/customer` for CustomerManager. The opt-in test bridge serves the built app and proxies API requests to the isolated host. Its `POST /__browser/corrupt-next-export-response` hook damages one successful order mutation response after the upstream write commits. `GET /__browser/export-check` reports order/event counts and compares Ledger stock with the baseline. These hooks exist only in the test project. Keep the generated session metadata out of source control; stop the test host when acceptance finishes.

## Verification scope

The focused backend Export suite passed 8 tests; the full frontend suite passed 96 tests, including 9 Export tests. The production web build and whitespace checks passed. The five previously reported Sample, Identity and Ledger backend failures are deferred at the user's request and were not changed in this work. F14–F17 remain separate milestones.
