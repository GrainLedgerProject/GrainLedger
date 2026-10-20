# Procurement samples and external laboratory API

Purchasing Staff records one physical sample against one committed procurement source lot, including before mill arrival. Handovers, laboratory dispatch, and laboratory acknowledgements are append-only. Customer Manager registers immutable original reports and sample-specific results and checks identity/transcription; Export Staff can read company-scoped laboratory evidence. Collection, report intake, confirmation, amendments, and retests do not change stock or HOLDs and never establish market compliance.

| Method | Path | Purpose |
| --- | --- | --- |
| POST | `/api/samples` | Record a sample. Requires `purchases.write`. |
| GET | `/api/samples?sourceLotId={id}&page=1&pageSize=20` | List company samples. Requires `purchases.write`, `farms.read`, `lab.write`, or `lab.read`. |
| GET | `/api/samples/{id}` | Read sample details and current handover count. Same read permissions. |
| POST | `/api/samples/{id}/handovers` | Append a handover. Requires `purchases.write`. |
| GET | `/api/samples/{id}/handovers?page=1&pageSize=20` | Read handover history. Same read permissions. |

All requests are scoped to the authenticated customer company. `sourceLotId` is the ID of `capture.source_lots`, not the Ledger `lotId`. The source must have committed procurement and its acceptance event; usable Ledger stock is not required. The app creates `entityId`, `clientOperationId`, `deviceId`, and `capturedAt`. Current web collection sends `payloadSchemaVersion: 2`, omits `code`, and uses the server-assigned `SMP` code described in [companion codes](companion-capture-codes.md). Version 1 remains compatible for historical manual-code submissions, as illustrated below. Record the physical seal code and write or print the returned sample code on the sample bag. One physical sample cannot represent multiple source lots.

## Create sample

```json
{
  "deviceId": "web-test-01",
  "clientOperationId": "ad5182d9-2188-45b2-bb52-9d1ab8e5bd30",
  "capturedAt": "2026-10-14T03:05:00Z",
  "payloadSchemaVersion": 1,
  "payload": {
    "entityId": "71f17c62-99c8-4f65-9016-6bf889095251",
    "sourceLotId": "88888888-8888-4888-8888-888888888888",
    "code": "SAMPLE-001",
    "sealCode": "SEAL-001",
    "collectedAt": "2026-10-14T03:00:00Z"
  }
}
```

Replace the UUIDs with new IDs and an accepted source lot in the current environment. The sample code must be unique per company. The seal code is required but is not unique by database rule. The server records `collectedBy` from the authenticated user. A successful POST returns 201, a `Location` header, the sample, and `handoverCount: 0`.

`materialDescription` is optional bounded text (200 characters) on both payload versions and nullable in detail/list responses. Current web collection requires staff to describe material sampled. Missing historical material remains `null`; it is never guessed.

## Record handover

```json
{
  "deviceId": "web-test-01",
  "clientOperationId": "5620b24b-6fa6-45f0-af3e-c339205512c0",
  "capturedAt": "2026-10-14T07:05:00Z",
  "payloadSchemaVersion": 1,
  "payload": {
    "entityId": "9be9e71d-243d-4942-a95c-ed6db9dd0107",
    "fromParty": "Collection agent",
    "toParty": "Laboratory",
    "handedOverAt": "2026-10-14T07:00:00Z",
    "sealCondition": "Intact",
    "evidenceRef": null
  }
}
```

Send this body to `POST /api/samples/{sampleId}/handovers`. `recordedBy` comes from the authenticated user. The handover time cannot precede collection or follow capture time. A successful POST returns 201; later handovers are separate records.

Optional handover fields: `handoverKind` (`HANDOVER`, `LAB_DISPATCH`, `LAB_ACKNOWLEDGEMENT`), `laboratoryName`, `laboratoryReference`, and `laboratorySampleReference`. Omitted kind defaults to `HANDOVER` for historical clients. Laboratory name and references allow 200 characters. Dispatch/acknowledgement require laboratory name; generic handover does not imply receipt or laboratory processing. `evidenceRef` retains supporting evidence references. A separate acknowledgement records evidence of actual receipt; dispatch alone means staff recorded sending the sample.

Each POST stores the entity, a business event, and an offline operation in one transaction. Retrying the same device, operation ID, and content returns the prior result with 200. Reusing the operation ID with different content returns 409 `OPERATION_CONFLICT`. A source lot or parent sample that has not been accepted returns 409 `SOURCE_RECORD_NOT_READY`. A duplicate sample code returns 409 `SAMPLE_CODE_EXISTS`. GET on a sample outside the company returns 404.

Capture migration `20261020000000_ExternalLaboratoryWorkflow` extends existing sample/handover schema and adds original report, result-set, row, confirmation, and confirmed-association tables. Original bytes are durable Capture `bytea`, with no fabricated facility. Company-composite foreign keys, exact-decimal constraints, immutable-evidence triggers, same-transaction original-row guards, and deferred complete-confirmation checks protect evidence. Explicit migration installation grants restricted runtime access through existing module provisioning; triggers need no elevated runtime role. Routine startup never migrates. Do not apply migrations/seeds or write verification data to shared Supabase without approval; use isolated PostgreSQL. See [deployment safety](setup-and-deployment.md).

```powershell
dotnet test backend/tests/GrainLedger.Api.IntegrationTests/GrainLedger.Api.IntegrationTests.csproj -c Release --filter FullyQualifiedName~SampleTests
```

## Laboratory report routes

This section is the HTTP contract referenced by [external laboratory workflow plan](lab-flow-plan.md). All reads require persisted `lab.read` or `lab.write`; all writes require `lab.write` (Customer Manager). Administrator is not a business-role wildcard. Active authenticated customer-company scope covers metadata, every proposed/confirmed sample ID, related report IDs, and complete original downloads. Purchasing Staff's sample permissions do not authorize report intake. Cookie mutations retain normal CSRF protection.

| Method | Path | Purpose |
| --- | --- | --- |
| POST | `/api/lab-reports` | Preserve original bytes, report metadata, sample-specific sections and result rows. |
| GET | `/api/lab-reports?page=1&pageSize=20&sampleId={id}&sourceLotId={id}` | Company report queue; optional independent filters. Page size 1–100. |
| GET | `/api/lab-reports/{id}` | Immutable original transcription, confirmation, related report links and assessment caveat. |
| GET | `/api/lab-reports/{id}/file` | Scoped original download, attachment, `application/octet-stream`, no-sniff and sandbox headers. |
| POST | `/api/lab-reports/{id}/confirm` | Append authenticated identity/transcription confirmation and verified sample associations. |

No report/result/file PUT, PATCH or DELETE endpoint exists. Original report metadata, bytes, proposed sample IDs, sections, rows, and confirmed associations cannot be overwritten. Correct with a new linked report.

Lists return `{ "items": [LabReportResponse], "page": 1, "pageSize": 20, "hasMore": false }`. IDs are paged before bulk metadata/result projection; metadata responses never materialize original bytes. `sampleId` filtering includes proposed or confirmed associations so unresolved evidence is still findable. `sourceLotId` filtering includes **confirmed associations only**. Unconfirmed or wrong identity never appears as confirmed source-lot evidence.

### Receive an original report

```json
{
  "deviceId": "web-test-01",
  "clientOperationId": "fe0dbe5b-90e0-44ae-bae2-cc460c02a798",
  "capturedAt": "2026-10-20T10:00:00Z",
  "payloadSchemaVersion": 1,
  "payload": {
    "entityId": "94785b41-1873-4956-be1e-11923a6c516b",
    "laboratoryName": "External laboratory",
    "reportNumber": "R-001",
    "issuedOn": "2026-10-19",
    "receivedAt": "2026-10-20T09:00:00Z",
    "reportKind": "FINAL",
    "relatedReportId": null,
    "relationshipReason": null,
    "fileName": "report.txt",
    "contentType": "text/plain",
    "contentBase64": "TkQ=",
    "companyOnlyConfirmed": true,
    "laboratoryConclusion": null,
    "statedStandard": null,
    "resultSets": [{
      "entityId": "a1ec0410-a760-41d1-8719-8431755c93ea",
      "sampleId": "71f17c62-99c8-4f65-9016-6bf889095251",
      "laboratorySampleReference": "LAB-S-001",
      "statedSampleCode": "SAMPLE-001",
      "statedSealCode": "SEAL-001",
      "sectionReference": "Page 1",
      "results": [{
        "testLabel": "Laboratory printed substance label",
        "substanceId": null,
        "originalResultText": "<0.010",
        "resultKind": "BELOW_LIMIT",
        "numericValue": "0.010",
        "unit": "mg/kg",
        "lod": null,
        "loq": "0.010",
        "method": null,
        "uncertainty": null,
        "reference": "Page 1, row 1"
      }]
    }]
  }
}
```

Use real company sample IDs and fresh entity/operation IDs. Original file must be nonempty and at most 10 MiB; request cap is 20 MiB including base64 and structured results. File names allow no paths/control characters; media type must be valid. Server derives actor, recording time, file size and SHA-256. Whole original is stored independently of any mill facility.

`companyOnlyConfirmed` must explicitly be `true` and attests that **every part of the original** belongs to this company, not only transcribed rows. Split/redact mixed-company originals before intake. Every supplied sample/related-report ID must belong to current company; one multi-sample file has separate result sets, never a shared global result panel applied to each lot.

`reportKind` permits `PARTIAL`, `FINAL`, `SUPPLEMENTARY`, `AMENDMENT`, or `RETEST`. Supplements/amendments/retests require existing same-company `relatedReportId` and nonblank `relationshipReason`. A linked new report does not replace any earlier row, silently resolve conflicting measurements, select the lowest/latest result, change earlier interpretations, or release a HOLD.

At least one result set and one result row per set are required; limits are 1000 sets and 10000 total rows. Set IDs must be unique. Missing sample identity is allowed: omit `sampleId` and/or printed identity to preserve an unresolved original. Supplied `sampleId` is merely proposed until verified confirmation.

### Exact flexible result rows

`testLabel` and `originalResultText` are required and preserved verbatim. Clearing either required field in manual entry produces a validation alert before upload; an empty required value is not treated as an omitted optional field. `resultKind` permits `NUMERIC`, `BELOW_LIMIT`, `NOT_DETECTED`, `QUALITATIVE`, and `OTHER`. `NUMERIC`/`BELOW_LIMIT` require `numericValue`; unknown printed substances and unsupported qualitative findings remain raw evidence. Optional `substanceId` must exist in Catalog; mapping is never guessed. `laboratoryConclusion` and `statedStandard` preserve laboratory assertions separately from GrainLedger assessment.

`numericValue`, `lod`, and `loq` cross JSON as strings and use exact constrained text storage with this grammar:

```text
^[0-9]{1,12}(\.[0-9]{1,12})?$
```

Nonnegative, 1–12 integer digits, optionally 1–12 fractional digits; no whitespace, signs, exponent, floating-point parsing or rounding. `"000.0100"` remains `"000.0100"`; original result text such as `"ND"` or `"<0.01"` is retained independently. Units, method, uncertainty and row reference are preserved, not converted. ND is not zero, below-limit is not a measured value equal to threshold, and omitted substances are not tested/passed.

Web CSV import and pasted TSV use this exact header:

```csv
testLabel,substanceId,originalResultText,resultKind,numericValue,unit,lod,loq,method,uncertainty,reference
```

Optional empty fields become absent values; decimals remain strings. CSV supports quoted fields, embedded delimiters and escaped double quotes. TSV uses same column names separated by tabs. Import previews/validates before adding rows to the currently selected sample section; this internal template does not prescribe external laboratory report formats.

### Review and confirm

```json
{
  "deviceId": "web-test-01",
  "clientOperationId": "269cd03c-3e20-4d05-a23e-0b36cc91d18f",
  "capturedAt": "2026-10-20T11:00:00Z",
  "payloadSchemaVersion": 1,
  "payload": {
    "identityChecked": true,
    "transcriptionChecked": true,
    "matches": [{
      "resultSetId": "a1ec0410-a760-41d1-8719-8431755c93ea",
      "sampleId": "71f17c62-99c8-4f65-9016-6bf889095251",
      "identityBasis": "SAMPLE_CODE",
      "evidenceRef": null
    }]
  }
}
```

Each result set must occur exactly once. All nonblank printed sample and seal identifiers must agree with selected registered sample: one matching identifier does not override another mismatching identifier.

- `SAMPLE_CODE`: exact printed sample-code match.
- `SEAL_CODE`: exact printed seal-code match.
- `LAB_REFERENCE`: report laboratory sample reference matches a persisted `LAB_DISPATCH` or `LAB_ACKNOWLEDGEMENT` for selected sample and same laboratory; submitted nonblank `evidenceRef` must match that handover's supporting evidence. An arbitrary free-text reference is not enough.

Missing/contradictory identity returns 409 `REPORT_IDENTITY_UNRESOLVED`; it cannot auto-confirm by farmer/plot/lot similarity. If original printed identity is wrong, preserve unresolved original and append linked corrected evidence. Confirmation records authenticated actor/time, check attestations and immutable associations; it never edits original proposed IDs/results. New operation against an already confirmed report returns 409 `REPORT_ALREADY_CONFIRMED`; identical replay returns original response with 200.

### Detail shape and meaning

`LabReportResponse` contains:

- `id`, `companyId`, `laboratoryName`, `reportNumber`, `issuedOn`, `receivedAt`, `reportKind`.
- `relatedReportId`, `relationshipReason`, `laboratoryConclusion`, `statedStandard`.
- `fileName`, `contentType` (original submitted media type), `fileSize`, `sha256`; never inline original bytes or unscoped storage references.
- Server `recordedBy`, `capturedAt`, `recordedAt`; nullable `confirmedBy`, `confirmedAt`.
- `status`: `UNRESOLVED`, `PENDING_REVIEW`, or `CONFIRMED`. Initial pending review requires all proposed samples to match printed identity without contradictory identifiers. Only explicit successful confirmation yields confirmed.
- `resultSets`: `id`, original proposed `sampleId`, separate `confirmedSampleId`, **confirmed-only** `sourceLotId`, printed `laboratorySampleReference`, `statedSampleCode`, `statedSealCode`, `sectionReference`, `identityStatus` (`UNRESOLVED`, `MATCHED`, `CONFIRMED`), and `results`. Each result adds server `id` to its intake row fields.
- `relatedReports`: child `{id, reportKind, relationshipReason}` links; parent remains `relatedReportId`.
- `assessmentStatus: "NOT_ASSESSED"`, `suggestedMarketCode: "VN"`, and reasons `LEGAL_DATA_NOT_APPROVED`, `INTERPRETATION_POLICY_NOT_APPROVED`, `COVERAGE_POLICY_NOT_APPROVED`.

No assessment calculation/write route exists until applicable legal data, interpretation and coverage policy are approved. Confirmation is a transcription/identity check, not PASS, legal compliance or finished-product certification. Late reports follow stable procurement sample/source identity, even after receiving/processing/exhaustion. Stock and existing HOLD decisions remain unchanged.

Both lab writes reuse Capture's transaction/event/offline operation convention. Successful intake returns 201 plus `Location`; confirmation returns 200. Identical device/operation/content replay returns 200; changed content returns 409 `OPERATION_CONFLICT`. Invalid syntax/decimal strings/attestations return 400; unavailable sample/related report/catalog references return 409 (`SAMPLE_NOT_FOUND`, `RELATED_REPORT_NOT_FOUND`, `SUBSTANCE_NOT_FOUND`); scoped GET/file/confirmation for absent/foreign report returns 404 `LAB_REPORT_NOT_FOUND`. Unauthorized roles receive 403. No report evidence can be obtained through an unscoped URL.

