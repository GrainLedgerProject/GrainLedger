# Procurement samples and external laboratory API

Purchasing Staff records one physical sample against one committed procurement source lot, including before mill arrival. Handovers, laboratory dispatch, and laboratory acknowledgements are append-only. Customer Manager registers immutable original reports and sample-specific results and checks identity/transcription; Export Staff can read company-scoped laboratory evidence. Collection, report intake, confirmation, amendments, and retests do not change stock or HOLDs and never establish market compliance.

| Method | Path | Purpose |
| --- | --- | --- |
| POST | `/api/samples` | Record a sample. Requires `purchases.write`. |
| GET | `/api/samples?sourceLotId={id}&page=1&pageSize=20` | List company samples. Requires `purchases.write`, `farms.read`, `lab.write`, or `lab.read`. |
| GET | `/api/samples/{id}` | Read sample details and current handover count. Same read permissions. |
| POST | `/api/samples/{id}/handovers` | Append a handover. Requires `purchases.write`. |
| GET | `/api/samples/{id}/handovers?page=1&pageSize=20` | Read handover history. Same read permissions. |

All requests are scoped to the authenticated customer company. `sourceLotId` is the ID of `capture.source_lots`, not the Ledger `lotId`. The source must have committed procurement and its acceptance event; usable Ledger stock is not required. The app creates `entityId`, `clientOperationId`, `deviceId`, and `capturedAt`. Collection requires `payloadSchemaVersion: 2` and omits `code`; the server assigns an `SMP-YYYYMMDD-NNNN` code with a company-scoped sequence that does not reset daily. Record the physical seal code and write or print the returned sample code on the sample bag. One physical sample cannot represent multiple source lots.

## Create sample

```json
{
  "deviceId": "web-test-01",
  "clientOperationId": "ad5182d9-2188-45b2-bb52-9d1ab8e5bd30",
  "capturedAt": "2026-10-14T03:05:00Z",
  "payloadSchemaVersion": 2,
  "payload": {
    "entityId": "71f17c62-99c8-4f65-9016-6bf889095251",
    "sourceLotId": "88888888-8888-4888-8888-888888888888",
    "sealCode": "SEAL-001",
    "collectedAt": "2026-10-14T03:00:00Z"
  }
}
```

Replace the UUIDs with new IDs and an accepted source lot in the current environment. The seal code is required but is not unique by database rule. The server records `collectedBy` from the authenticated user. A successful POST returns 201, a `Location` header, the sample, and `handoverCount: 0`.

`materialDescription` is optional bounded text (200 characters) and nullable in detail/list responses. Current web collection requires staff to describe material sampled. Missing historical material remains `null`; it is never guessed.

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

Capture migration `20261020000000_ExternalLaboratoryWorkflow` extends existing sample/handover schema and adds original report, result-set, row, confirmation, and confirmed-association tables. Original bytes are durable Capture `bytea`, with no fabricated facility. Company-composite foreign keys, exact-decimal constraints, immutable-evidence triggers, same-transaction original-row guards, and deferred complete-confirmation checks protect evidence. Explicit migration installation grants restricted runtime access through existing module provisioning; triggers need no elevated runtime role. Routine startup never migrates. Do not apply migrations/seeds or write verification data to shared Supabase without approval; use isolated PostgreSQL. See [deployment safety](../setup-and-deployment.md).

```powershell
dotnet test backend/tests/GrainLedger.Api.IntegrationTests/GrainLedger.Api.IntegrationTests.csproj -c Release --filter FullyQualifiedName~SampleTests
```

## Laboratory report routes

This section is the HTTP contract referenced by [external laboratory workflow plan](../plans/lab-flow-plan.md). All reads require persisted `lab.read` or `lab.write`; all writes require `lab.write` (Customer Manager). Administrator is not a business-role wildcard. Active authenticated customer-company scope covers metadata, every proposed/confirmed sample ID, related report IDs, and complete original downloads. Purchasing Staff's sample permissions do not authorize report intake. Cookie mutations retain normal CSRF protection.

| Method | Path | Purpose |
| --- | --- | --- |
| POST | `/api/lab-reports` | Preserve original bytes, report metadata, sample-specific sections and result rows. |
| PUT | `/api/lab-reports/{id}` | Save a replacement revision of an active unconfirmed report; retain earlier original evidence. |
| DELETE | `/api/lab-reports/{id}` | Remove an active unconfirmed report from the queue; retain its original and deletion audit record. |
| GET | `/api/lab-reports?page=1&pageSize=20&sampleId={id}&sourceLotId={id}` | Company report queue; optional independent filters. Page size 1–100. |
| GET | `/api/lab-reports/{id}` | Immutable original transcription, confirmation, related report links and assessment caveat. |
| GET | `/api/lab-reports/{id}/file` | Scoped original download, attachment, `application/octet-stream`, no-sniff and sandbox headers. |
| POST | `/api/lab-reports/{id}/confirm` | Append authenticated identity/transcription confirmation and verified sample associations. |

Report metadata, bytes, proposed sample IDs, sections, rows, and confirmed associations are never overwritten or physically deleted. Draft editing appends a replacement report and retires its predecessor; draft deletion appends a tombstone. Confirmed reports cannot be edited, deleted, or reopened; correct them with a new linked report.

Lists return `{ "items": [LabReportResponse], "page": 1, "pageSize": 20, "hasMore": false }` and exclude superseded/deleted drafts. IDs are paged before bulk metadata/result projection; metadata responses never materialize original bytes. `sampleId` filtering includes proposed or confirmed associations so active unresolved evidence is still findable. `sourceLotId` filtering includes **confirmed associations only**. Unconfirmed or wrong identity never appears as confirmed source-lot evidence. Earlier draft versions and deleted originals remain available through scoped detail/file reads for audit.
The web sample chooser searches loaded samples only. Searching never clears the selected sample: the selected option remains visible even when it does not match the search text, and report submission preserves that sample ID. For the role-by-role application workflow, see the [laboratory workflow guide](../guides/lab-flow-guide.md).

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
`relatedReportId` is optional and must be `null` or a UUID in canonical hyphenated form (hex digits are case-insensitive); report numbers remain arbitrary labels and are not accepted as IDs. A malformed UUID in the JSON request body returns 400 with a safe validation problem. Oversized request bodies return 413.

In the web form, choose a related report from the scoped readable, paginated **Related report** selector; the UI submits its ID as `relatedReportId`. HTTP clients must still send a valid UUID as specified above. A server 400 is a definite rejection: the form remains editable and does not retain an uncertain-retry envelope. Genuine uncertain failures retain the exact original request for replay.

`reportKind` permits `PARTIAL`, `FINAL`, `SUPPLEMENTARY`, `AMENDMENT`, or `RETEST`. Supplements/amendments/retests require existing same-company `relatedReportId` and nonblank `relationshipReason`. A linked new report does not replace any earlier row, silently resolve conflicting measurements, select the lowest/latest result, change earlier interpretations, or release a HOLD.

At least one result set and one result row per set are required; limits are 1000 sets and 10000 total rows. Set IDs must be unique. Missing sample identity is allowed: omit `sampleId` and/or printed identity to preserve an unresolved original. Supplied `sampleId` is merely proposed until verified confirmation.

### Exact flexible result rows

`testLabel` and `originalResultText` are required and preserved verbatim. Clearing either required field in manual entry produces a validation alert before upload; an empty required value is not treated as an omitted optional field. `resultKind` permits `NUMERIC`, `BELOW_LIMIT`, `NOT_DETECTED`, `QUALITATIVE`, and `OTHER`. `NUMERIC`/`BELOW_LIMIT` require `numericValue`; unknown printed substances and unsupported qualitative findings remain raw evidence. Optional `substanceId` must exist in Catalog; mapping is never guessed. `laboratoryConclusion` and `statedStandard` preserve laboratory assertions separately from GrainLedger assessment.

`numericValue`, `lod`, and `loq` cross JSON as strings and use exact constrained text storage with this grammar:

```text
^[0-9]{1,12}(\.[0-9]{1,12})?$
```

Nonnegative, 1–12 integer digits, optionally 1–12 fractional digits; no whitespace, signs, exponent, floating-point parsing or rounding. `"000.0100"` remains `"000.0100"`; original result text such as `"ND"` or `"<0.01"` is retained independently. Units, method, uncertainty and row reference are preserved, not converted. ND is not zero, below-limit is not a measured value equal to threshold, and omitted substances are not tested/passed.

Compact web intake has one **Reported result** input. Supported numeric text, `<threshold` and `ND` populate existing qualifier/value fields without floating-point conversion; original text remains unchanged. Unsupported notation uses explicit structured overrides under **Additional lab details**, not a guessed interpretation. Optional section unit/method are entry conveniences expanded into each row before upload; supplied row values take precedence. API payload and CSV/TSV headers below are unchanged.

Web CSV import and pasted TSV use this exact header:

```csv
testLabel,substanceId,originalResultText,resultKind,numericValue,unit,lod,loq,method,uncertainty,reference
```

Optional empty fields become absent values; decimals remain strings. CSV supports quoted fields, embedded delimiters and escaped double quotes. TSV uses same column names separated by tabs. Import previews/validates before adding rows to the currently selected sample section; this internal template does not prescribe external laboratory report formats.

### Synthetic report generator

For the complete generate → print PDF → upload → import → confirm walkthrough, see the [synthetic laboratory report guide](../guides/synthetic-lab-report-guide.md).

Use the standalone Python 3 standard-library CLI to make importable test evidence. Replace `SAMPLE-REPLACE-WITH-ACTUAL-CODE` with a registered sample code; optional seal and lab reference should match the sample's recorded identity/handover when testing confirmation.

```sh
python3 tools/generate_synthetic_lab_report.py \
  --sample-code SAMPLE-REPLACE-WITH-ACTUAL-CODE \
  --seal-code SEAL-001 \
  --lab-sample-reference LAB-S-001 \
  --out-dir tools/mock_lab_reports/report-001
```

`--out-dir` is optional; omitting it writes to `tools/mock_lab_reports` beside the script. Repeat runs overwrite `report.html` and `results.csv`; pass `--no-overwrite` to reject an existing output directory.

Default output is a realistic-looking 14-substance synthetic panel with catalog codes, CAS labels, methods, and reporting limits. `--rows` accepts 1–10000; `--scenario synthetic` exercises all five supported result kinds. Other scenarios are `compliant` (default), `vn-pass-export-fail`, and `vn-exceed`. These names select fictional measurements, not legal verdicts. Import matching `results.csv`; open `report.html` in a browser and Print → Save as PDF if needed.

Every generated value is synthetic, visibly marked as test data, and is not official laboratory evidence. Never use it as a real report or for compliance decisions. Report kind is `FINAL`; no legal limit or pass/fail comparison is applied by the generator.

This standalone generator creates no report relationship: `relatedReportId` and `relationshipReason` do not apply to its unlinked `FINAL` output. For actual intake, amendments, supplements, or retests, relationship reason explains why new report links to its original; it does not replace earlier evidence or resolve conflicts.

### Edit or delete an unconfirmed report

`UNRESOLVED` and `PENDING_REVIEW` reports can be revised by an own-company Customer Manager with current `lab.write`. `PUT /api/lab-reports/{id}` accepts the same envelope and full payload as intake, with `payload.entityId` equal to the current route ID. Send `contentBase64: null` to retain the exact current original bytes, filename and media type, or supply a replacement file using the intake file rules. Supply every section and result row; an omitted row is not copied implicitly.

Successful edit returns `200` with the replacement `LabReportResponse`. Server generates a new report ID and fresh section/result IDs. Follow the returned `id` for subsequent review, confirmation or editing. Earlier report remains immutable, marked `SUPERSEDED` with `replacementReportId`. Editing is a draft-transcription revision, not a laboratory `AMENDMENT`, `SUPPLEMENTARY` or `RETEST`; those document relationships remain separate.

`DELETE /api/lab-reports/{id}` accepts a JSON body containing `deviceId`, fresh `clientOperationId`, `capturedAt` and `payloadSchemaVersion: 1`; no payload is required. Successful deletion returns `204`. A report referenced by another report cannot be deleted (`409 LAB_REPORT_REFERENCED`). Deletion removes the active queue entry, not stored original evidence, source records, stock, HOLDs or historical calculations.

Both commands use current permissions, company scope, CSRF, atomic audit writes and operation-id replay. A stale edit/delete/confirmation cannot act on an already retired draft; failed validation leaves the previous version active. Confirmed reports reject edit/delete and stay confirmed. After an uncertain response, retry the exact request with the same operation ID; do not create a new operation until the outcome is known.

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
    "asReceivedConfirmed": true,
    "matches": [{
      "resultSetId": "a1ec0410-a760-41d1-8719-8431755c93ea",
      "sampleId": "71f17c62-99c8-4f65-9016-6bf889095251",
      "identityBasis": "SAMPLE_CODE",
      "evidenceRef": null
    }]
  }
}
```

Each result set must occur exactly once. All nonblank printed sample and seal identifiers must agree with selected registered sample: one matching identifier does not override another mismatching identifier. Comparisons ignore leading/trailing whitespace but remain case-sensitive; legacy evidence is not rewritten. New intake trims sample/seal and laboratory-reference fields before storing them.

- `SAMPLE_CODE`: printed sample-code match after trimming.
- `SEAL_CODE`: printed seal-code match after trimming.
- `LAB_REFERENCE`: report laboratory sample reference matches a persisted `LAB_DISPATCH` or `LAB_ACKNOWLEDGEMENT` for selected sample and same laboratory; submitted nonblank `evidenceRef` must match that handover's supporting evidence. An arbitrary free-text reference is not enough.

Missing/contradictory identity returns 409 `REPORT_IDENTITY_UNRESOLVED`; it cannot auto-confirm by farmer/plot/lot similarity. If original printed identity is wrong, preserve unresolved original and append linked corrected evidence. Confirmation records authenticated actor/time, check attestations and immutable associations; it never edits original proposed IDs/results. New operation against an already confirmed report returns 409 `REPORT_ALREADY_CONFIRMED`; identical replay returns original response with 200.

`asReceivedConfirmed` explicitly attests that concentrations are as received for residue bounds. False or omitted is allowed for identity/transcription confirmation, but automatic residue binding returns unresolved reasons and publishes no binding. The flag participates in operation replay identity; do not change it when retrying the same operation ID. Already confirmed reports can be reviewed and bound separately with explicit attestation.

The web confirmation dialog compares printed and registered identifiers and proposes a basis from matching sample code, then matching seal code, then an available laboratory reference. Staff still verify evidence and explicitly attest identity/transcription. Rejections appear beside the submit action inside the open dialog. Definite rejections permit correction; uncertain failures retain the exact request for replay.

Legacy padded identities also require Capture migration `20261020120000_TrimLegacyLabIdentityComparisons`. It aligns the database association guard with .NET whitespace trimming for sample/seal, laboratory-reference, and supporting-evidence comparisons. Original report/sample evidence and immutability triggers remain unchanged. A backend restart alone does not update this guard; apply the migration through the approved migration-owner workflow, never routine runtime startup.

### Detail shape and meaning

`LabReportResponse` contains:

- `id`, `companyId`, `laboratoryName`, `reportNumber`, `issuedOn`, `receivedAt`, `reportKind`.
- `relatedReportId`, `relationshipReason`, `laboratoryConclusion`, `statedStandard`.
- `fileName`, `contentType` (original submitted media type), `fileSize`, `sha256`; never inline original bytes or unscoped storage references.
- Server `recordedBy`, `capturedAt`, `recordedAt`; nullable `confirmedBy`, `confirmedAt`.
- `status`: `UNRESOLVED`, `PENDING_REVIEW`, `CONFIRMED`, `SUPERSEDED`, or `DELETED`. Initial pending review requires all proposed samples to match printed identity without contradictory identifiers. Only explicit successful confirmation yields confirmed. Retired draft originals are read-only; nullable `replacementReportId` links a superseded draft to its successor.
- `resultSets`: `id`, original proposed `sampleId`, separate `confirmedSampleId`, **confirmed-only** `sourceLotId`, printed `laboratorySampleReference`, `statedSampleCode`, `statedSealCode`, `sectionReference`, `identityStatus` (`UNRESOLVED`, `MATCHED`, `CONFIRMED`), and `results`. Each result adds server `id` to its intake row fields.
- `relatedReports`: child `{id, reportKind, relationshipReason}` links; parent remains `relatedReportId`.
- `assessmentStatus`: `NOT_ASSESSED`, `WITHIN_ASSESSED_LIMITS`, `EXCEEDS_LIMIT`, `INSUFFICIENT_EVIDENCE`, or `NO_MRL_IN_VN`; `suggestedMarketCode: "VN"` and per-substance `assessmentReasons`.

Confirmed reports with mapped substances and published VN limits receive a server-computed numeric baseline comparison. Unconfirmed reports, missing published limits, or wholly unmapped panels remain `NOT_ASSESSED`. Numeric and below-limit results use exact decimal comparisons; ND requires a usable LOQ/LOD. Missing numeric limits are reported as `NO_MRL_IN_VN`, never zero or unrestricted. Exceedance takes precedence over insufficient evidence, then within-assessed findings. This read-time comparison currently uses the latest published VN version per substance/commodity; it is not a persisted, decision-time legal assessment or finished-product certification. The web displays server status/reasons without embedded statutory values or browser-side compliance calculations.

Confirmation is a transcription/identity check, not PASS or legal compliance. Late reports follow stable procurement sample/source identity, even after receiving/processing/exhaustion. Stock and existing HOLD decisions remain unchanged.

Lab writes reuse Capture's transaction/event/offline operation convention. Successful intake returns 201 plus `Location`; confirmation and revision return 200; deletion returns 204. Identical device/operation/content replay returns the recorded result; changed content returns 409 `OPERATION_CONFLICT`. Invalid syntax/decimal strings/attestations return 400; unavailable sample/related report/catalog references return 409 (`SAMPLE_NOT_FOUND`, `RELATED_REPORT_NOT_FOUND`, `SUBSTANCE_NOT_FOUND`); scoped GET/file/mutations for absent/foreign report return 404 `LAB_REPORT_NOT_FOUND`. Unauthorized roles receive 403. No report evidence can be obtained through an unscoped URL.

## Residue bounds for derived lots

Laboratory originals, rows and confirmed procurement associations remain immutable facts. Residue bounds use reviewed schema-2 selections or explicit source-group fallback and a whole-flow profile; missing scientific interpretation, uncertainty, applicability, coverage or process factor produces `UNRESOLVED`, never ND=0 or PASS. Capture source IDs differ from Ledger lot IDs. Report relationships alone do not resolve evidence conflicts.

| Method | Path | Purpose / access |
| --- | --- | --- |
| GET | `/api/residue-rules` | Return shared system rules plus caller's own company rules; Administrator sees system rules only. |
| GET | `/api/residue-rules/{id}` | Read a visible system or own-company rule; foreign company rules are not exposed. |
| POST | `/api/residue-rules` | Publish reviewed system rule (`residue.policy.manage`, Administrator) or own-company rule (`residue.company-policy.manage`, CustomerManager). Cookie mutations require CSRF. |
| GET | `/api/residue-policy-settings` | Return `{ "items": [...] }` for authenticated caller's own company only. No setting means no adopted profile. |
| GET | `/api/residue-policy-settings/{id}` | Read own-company setting; out-of-scope IDs return not found. |
| POST | `/api/residue-policy-settings` | Publish immutable, reviewed own-company profile setting (`residue.company-policy.manage`, CustomerManager); cookie mutations require CSRF. |
| POST | `/api/residue-policy-settings/batch` | `{ "items": [ResiduePolicySettingVersion, ...] }`; atomically publish reviewed own-company settings with the same permissions/CSRF and scientific gates as individual publication. |
| POST | `/api/residue-report-selections/preview` | Non-mutating, server-side `ResidueSourceBound` preview for confirmed own-company source/result; requires `residue.evidence.manage`. |
| POST | `/api/lab-reports/{reportId}/residue-review/preview` | Non-mutating report-level profile matching and interval preview; confirmed own-company evidence only, `residue.evidence.manage`. |
| POST | `/api/lab-reports/{reportId}/residue-review/confirm` | Explicitly bind reviewed compatible report rows to adopted profiles; revalidate and publish individual selections atomically, without changing originals, stock, HOLDs or saved evaluations. |

One profile Code covers a complete `kind: "FLOW"`, `schemaVersion: 2` profile for one substance and commodity. Shared fields include scope, version, review, effective window, `residueDefinition`, fixed `unit: "mg/kg"`, `decimalPlaces` and `asReceivedConfirmed: true`. Required `laboratory` and `steps` arrays cannot both be empty. Old per-stage and analytical-basis fields are not accepted.

Each laboratory entry identifies an exact `analyticalMethod` and explicitly enabled result forms: `numericInterpretationKind`, `relativeUncertaintyPercent`, `relativeUncertaintyAtZero`, `belowLimit` and `ndThresholdKind`. Each process step has a stable UUID `id`, `process`, immediate `inputStage`, `outputStage`, `outputFraction`, explicit `conditions`, `factorLower`, `factorUpper`, `factorSemantics`, `anchorStage` and `homogeneousMixing`. Cumulative factors use an explicit supported ancestor anchor; overlapping applicability and duplicate methods are rejected. Source stage is `RAW_PADDY`. Missing components remain unresolved, not filled from another Code.

Customer Manager explicitly publishes `SYSTEM_DEFAULT` to adopt a system profile or `COMPANY_OVERRIDE` for a compatible same-Code company profile. Settings retain `ruleCode` as the whole-profile Code, independent version/review/effective dates and scope. No effective setting means unresolved; an override never falls back to the system profile. Event-time resolution follows the recorded Code, and positive parents with different Codes remain unresolved. Provenance identifies profile version, setting and laboratory component or step UUID.

Relative uncertainty is an exact nonnegative percentage (`10` means 10%); use at zero requires explicit review. A reviewed normalized `absoluteUncertainty` takes precedence. Nonblank raw uncertainty without normalization remains unresolved. Selection uncertainty and reporting threshold use the report's original unit; supported microgram-per-kilogram units normalize exactly to `mg/kg`. Bare `ppb` stays unresolved. Preview is non-mutating and returns finite bounds only with empty reasons and non-null endpoints.

New selection/group evidence uses `schemaVersion: 2`, `asReceivedConfirmed: true` and `profileCode`. Selections record `profileRuleId`, `profileSettingId` and `laboratoryMethod`; groups record one `profileBindings` entry per source, with `sourceId`, `profileRuleId` and `profileSettingId`, resolved at each member's receipt. Publication requires `residue.evidence.manage`; `residue.evaluate` grants scoped derived results, not original reports. Existing lab originals may be freshly reviewed and bound without alteration.

Legacy profile, setting, selection and group records remain stored but are invisible to active readers; old IDs cannot be used as schema-2 predecessors. Old-engine evaluation history is excluded, detail returns 404 and replay with its ID returns 409; use a new evaluation ID. New engine is `residue-flow-as-received-3`. No old approvals are automatically converted. Separate export-market `residueBasis` must be explicitly reviewed as `AS_RECEIVED`; unsupported conventions remain incomplete.

### Report-level residue review

Start with `POST /api/lab-reports/{reportId}/residue-review/preview`:

```json
{
  "companyId": "OWNING-COMPANY-UUID",
  "effectiveFrom": null,
  "asReceivedConfirmed": true,
  "rows": []
}
```

Response contains `reportId`, server-chosen `effectiveFrom` and `rows`. Each row includes `input` (immutable selection UUID, result/source binding, optional normalization/threshold/supersession/note, server preview fingerprint), human-readable report/result `label`, `rawUncertainty`, matched `profile`/`setting`, `assessment`, unresolved `reasons`, named `conflictingResults`/`existingSelections`, and `published`.

To edit an exception, preserve its row UUID and source/result binding, change only explicit normalization, threshold, supersession or review note, then preview again. Matching uses source receipt time, exact method and the adopted whole profile; multiple matches and missing intended overrides fail closed. Any normalization, threshold or supersession requires a binding review note.

Confirm with the same `companyId`, returned `effectiveFrom`, `asReceivedConfirmed: true` and reviewed eligible row `input` objects at `POST /api/lab-reports/{reportId}/residue-review/confirm`. False attestation produces an unresolved preview and rejects publication without writes. Each row must carry its current preview fingerprint; stale or invalid rows reject the batch. Identical successful replay retains the original binding. Profile scientific reviewer and current binding confirmer remain distinct. No review rewrites originals or earlier evaluations.

Report review supports up to 10,000 input rows, matching report intake's total panel limit. Prearrival confirmed source rows whose reserved Ledger lot has not been posted remain unresolved without blocking preview of other rows. Atomic confirmation applies to the explicitly submitted compatible rows, not to every row in the original report; omitted unresolved rows remain unbound.

### Single-action laboratory confirmation

`POST /api/lab-reports/{id}/confirm` retains identity/transcription checks and idempotency. With `asReceivedConfirmed: true`, it also binds uniquely compatible confirmed source/substance results to applicable adopted policy within the caller-owned transaction. Response `residueBindings` includes per-row `resultId`, `sourceId`, `published` and `reasons`. False or omitted attestation, missing policy, unsupported uncertainty or conflicting evidence leaves rows unbound with explicit reasons without undoing valid identity confirmation. Manual review remains available after adoption and explicit attestation.

### Read-only live lot bounds

`GET /api/ledger/lots/{lotId}/residue-preview?substanceIds=<comma-separated-UUIDs>` requires `residue.evaluate` and current lot/company/facility scope. Supply 1–200 distinct nonempty substance UUIDs. Response contains `lotId`, `companyId`, `snapshotAt`, `engineVersion` and `results` with existing exact interval/provenance/reason contracts; no evaluation `id` or `persistedAt`.

Preview uses coherent read-only `REPEATABLE READ` materialization and the same calculator as evaluation, but neither replays nor appends an evaluation. `snapshotAt` describes its captured view, not guaranteed later stock eligibility. Lot detail loads preview automatically; **Refresh live preview** recomputes without history writes. **Lock snapshot for audit** retains the existing immutable evaluation POST and replay/history contracts.
