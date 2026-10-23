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
The web sample chooser searches loaded samples only. Searching never clears the selected sample: the selected option remains visible even when it does not match the search text, and report submission preserves that sample ID. For the role-by-role application workflow, see the [laboratory workflow guide](lab-flow-guide.md).

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

For the complete generate → print PDF → upload → import → confirm walkthrough, see the [synthetic laboratory report guide](synthetic-lab-report-guide.md).

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

Each result set must occur exactly once. All nonblank printed sample and seal identifiers must agree with selected registered sample: one matching identifier does not override another mismatching identifier. Comparisons ignore leading/trailing whitespace but remain case-sensitive; legacy evidence is not rewritten. New intake trims sample/seal and laboratory-reference fields before storing them.

- `SAMPLE_CODE`: printed sample-code match after trimming.
- `SEAL_CODE`: printed seal-code match after trimming.
- `LAB_REFERENCE`: report laboratory sample reference matches a persisted `LAB_DISPATCH` or `LAB_ACKNOWLEDGEMENT` for selected sample and same laboratory; submitted nonblank `evidenceRef` must match that handover's supporting evidence. An arbitrary free-text reference is not enough.

Missing/contradictory identity returns 409 `REPORT_IDENTITY_UNRESOLVED`; it cannot auto-confirm by farmer/plot/lot similarity. If original printed identity is wrong, preserve unresolved original and append linked corrected evidence. Confirmation records authenticated actor/time, check attestations and immutable associations; it never edits original proposed IDs/results. New operation against an already confirmed report returns 409 `REPORT_ALREADY_CONFIRMED`; identical replay returns original response with 200.

The web confirmation dialog compares printed and registered identifiers and proposes a basis from matching sample code, then matching seal code, then an available laboratory reference. Staff still verify evidence and explicitly attest identity/transcription. Rejections appear beside the submit action inside the open dialog. Definite rejections permit correction; uncertain failures retain the exact request for replay.

Legacy padded identities also require Capture migration `20261020120000_TrimLegacyLabIdentityComparisons`. It aligns the database association guard with .NET whitespace trimming for sample/seal, laboratory-reference, and supporting-evidence comparisons. Original report/sample evidence and immutability triggers remain unchanged. A backend restart alone does not update this guard; apply the migration through the approved migration-owner workflow, never routine runtime startup.

### Detail shape and meaning

`LabReportResponse` contains:

- `id`, `companyId`, `laboratoryName`, `reportNumber`, `issuedOn`, `receivedAt`, `reportKind`.
- `relatedReportId`, `relationshipReason`, `laboratoryConclusion`, `statedStandard`.
- `fileName`, `contentType` (original submitted media type), `fileSize`, `sha256`; never inline original bytes or unscoped storage references.
- Server `recordedBy`, `capturedAt`, `recordedAt`; nullable `confirmedBy`, `confirmedAt`.
- `status`: `UNRESOLVED`, `PENDING_REVIEW`, or `CONFIRMED`. Initial pending review requires all proposed samples to match printed identity without contradictory identifiers. Only explicit successful confirmation yields confirmed.
- `resultSets`: `id`, original proposed `sampleId`, separate `confirmedSampleId`, **confirmed-only** `sourceLotId`, printed `laboratorySampleReference`, `statedSampleCode`, `statedSealCode`, `sectionReference`, `identityStatus` (`UNRESOLVED`, `MATCHED`, `CONFIRMED`), and `results`. Each result adds server `id` to its intake row fields.
- `relatedReports`: child `{id, reportKind, relationshipReason}` links; parent remains `relatedReportId`.
- `assessmentStatus`: `NOT_ASSESSED`, `WITHIN_ASSESSED_LIMITS`, `EXCEEDS_LIMIT`, `INSUFFICIENT_EVIDENCE`, or `NO_MRL_IN_VN`; `suggestedMarketCode: "VN"` and per-substance `assessmentReasons`.

Confirmed reports with mapped substances and published VN limits receive a server-computed numeric baseline comparison. Unconfirmed reports, missing published limits, or wholly unmapped panels remain `NOT_ASSESSED`. Numeric and below-limit results use exact decimal comparisons; ND requires a usable LOQ/LOD. Missing numeric limits are reported as `NO_MRL_IN_VN`, never zero or unrestricted. Exceedance takes precedence over insufficient evidence, then within-assessed findings. This read-time comparison currently uses the latest published VN version per substance/commodity; it is not a persisted, decision-time legal assessment or finished-product certification. The web displays server status/reasons without embedded statutory values or browser-side compliance calculations.

Confirmation is a transcription/identity check, not PASS or legal compliance. Late reports follow stable procurement sample/source identity, even after receiving/processing/exhaustion. Stock and existing HOLD decisions remain unchanged.

Both lab writes reuse Capture's transaction/event/offline operation convention. Successful intake returns 201 plus `Location`; confirmation returns 200. Identical device/operation/content replay returns 200; changed content returns 409 `OPERATION_CONFLICT`. Invalid syntax/decimal strings/attestations return 400; unavailable sample/related report/catalog references return 409 (`SAMPLE_NOT_FOUND`, `RELATED_REPORT_NOT_FOUND`, `SUBSTANCE_NOT_FOUND`); scoped GET/file/confirmation for absent/foreign report returns 404 `LAB_REPORT_NOT_FOUND`. Unauthorized roles receive 403. No report evidence can be obtained through an unscoped URL.

## Residue bounds for derived lots

F11 report originals, rows and confirmed procurement associations remain immutable facts. Residue bounds use reviewed result selection/normalization plus explicit company-scoped source-group fallback; they do not modify lab routes or inherit the VN comparator's threshold choices. Missing scientific interpretation, uncertainty, method/material/unit applicability, coverage or process factor produces `UNRESOLVED`, never ND=0 or PASS.

Customer Manager uses **Laboratory reports → Residue fallback and report evidence** for explicit source-group membership; no report is required for reviewed external fallback evidence. Confirmed-report detail first offers server-matched residue review and one explicit confirmation for compatible rows. Expert review remains available for normalized uncertainty/thresholds, unmatched applicability and named report/result supersession choices. Capture source IDs differ from Ledger lot IDs. A report relationship alone does not select evidence or resolve conflicts. The selected rule reference records policy origin, setting ID/version and rule code alongside rule ID/version.

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

Reusable profile is an existing rule Code series. A Customer Manager explicitly publishes `SYSTEM_DEFAULT` to adopt the system series or return to inheritance, or `COMPANY_OVERRIDE` to select a company rule with the same Code and compatible substance/kind/commodity identity. Company rule versions supersede only within their own scope; settings are versioned independently. `COMPANY_OVERRIDE` never falls back to system rule if company rule is missing or inapplicable. There is no automatic adoption, setting seed, cross-company read, or Administrator wildcard for company settings. Setting/rule effective time, review evidence, applicability and supersession are retained; selected policy origin/setting and normalized uncertainty are recorded in calculation provenance.

For a relative numeric interpretation, `RelativeUncertaintyPercent` is exact nonnegative decimal percentage (`10` means 10%). `RelativeUncertaintyAtZero` is false unless explicitly reviewed true. A reviewed selection's normalized `absoluteUncertainty`, when supplied, takes precedence; if raw reported uncertainty is nonblank and normalized absolute uncertainty is absent, calculation is unresolved rather than parsing raw text. Selection `absoluteUncertainty` and `reportingThreshold` are entered in the selected report row's original concentration unit. Supported report units `mg/kg`, `µg/kg`, `μg/kg` and `ug/kg` normalize exactly to canonical `mg/kg`; microgram values, LOD, LOQ and reviewed absolute uncertainty/threshold are multiplied by `0.001` once. Bare `ppb` remains unresolved because the report model has no explicit mass-fraction semantic marker. Preview response has no status field: only empty `reasons` and non-null `lower`/`upper` represent a finite interval. Browser arithmetic is not authoritative.
Setting JSON is `ResiduePolicySettingVersion`: `id`, owning `companyId`, `ruleCode`, `versionNo`, `mode` (`SYSTEM_DEFAULT` or `COMPANY_OVERRIDE`), `effectiveFrom`, optional `effectiveTo`, `supersedesId`, `evidenceRef`, `applicabilityStatement`, `reviewedBy`, `reviewAuthority`, `reviewedAt`; publication metadata is server-owned. Settings are versioned per company and Code. Authenticated request company fields do not establish authority; role and company are verified from persisted identity. Company settings cannot be read by Administrator as wildcard.

Rule JSON retains existing fields and adds optional trailing `companyId` (`null` = system scope), `relativeUncertaintyPercent` and `relativeUncertaintyAtZero`. `NUMERIC_RELATIVE_UNCERTAINTY` requires an explicit nonnegative exact decimal percent; `10` means 10%, not a fraction. Other interpretation kinds and processing rules cannot carry relative settings. Missing fields in old JSON mean system scope and unchanged existing behavior.

Preview accepts proposed existing `ResidueReportSelection` JSON, but does not persist selection/evaluation or trust supplied review/published metadata. It requires confirmed own-company source/result and valid superseded-result references, and returns `ResidueSourceBound` (no `status` field). Finite result requires empty reasons plus non-null lower/upper; otherwise preserve `UNRESOLVED`. Provenance adds `uncertaintyMode`, applied relative percent and exact canonical `absoluteUncertainty`. Conversion provenance retains `originalUnit`, `canonicalUnit`, `concentrationScale`, `originalNumericValue`, `originalLod`, `originalLoq`, `originalAbsoluteUncertainty` and `originalReportingThreshold` where applicable. Raw uncertainty text is never automatically normalized.
`residue.evidence.manage` controls own-company evidence publication. `residue.evaluate` permits appropriately scoped customer/facility users to calculate or inspect derived Ledger snapshots; it does not grant original laboratory downloads. New rule/group/selection/settings versions and late reports affect a new calculation only; old snapshots and stock/HOLDs remain unchanged. Rule/settings record shapes and full behavior: [residue bounds delivered API](residue-bounds-plan.md#9-delivered-residue-bounds-contracts).

### Report-level residue review

Start with `POST /api/lab-reports/{reportId}/residue-review/preview`:

```json
{
  "companyId": "OWNING-COMPANY-UUID",
  "effectiveFrom": null,
  "rows": []
}
```

Response contains `reportId`, server-chosen `effectiveFrom` and `rows`. Each row includes `input` (immutable selection UUID, result/source binding, optional normalization/threshold/supersession/note, server preview fingerprint), human-readable report/result `label`, `rawUncertainty`, matched `profile`/`setting`, `assessment`, unresolved `reasons`, named `conflictingResults`/`existingSelections`, and `published`.

To edit an exception, preserve its row UUID and source/result binding, change only the explicit normalization, threshold, supersession or review note, then preview again. Raw laboratory uncertainty is never parsed automatically. Matching uses source receipt time, exact method/material identity, supported canonical concentration unit and the company's adopted whole-rule policy; multiple matches and missing intended overrides fail closed. Any normalization, threshold or supersession requires a binding review note.

Confirm with the same `companyId`, returned `effectiveFrom` and reviewed eligible row `input` objects at `POST /api/lab-reports/{reportId}/residue-review/confirm`. Every submitted row must still be compatible and carry its current preview fingerprint; stale or invalid rows reject the entire batch. Replaying an identical successful row set with the same UUIDs/current actor returns the original binding without appending duplicates. Do not mix new and already-published row UUIDs in one batch.

Selections retain `bindingReportId`, `profileRuleId`, `profileSettingId`, canonical `bindingRequest`, `bindingFingerprint` and optional `bindingNote`. Profile `reviewedBy`/`reviewAuthority`/`reviewedAt` identify the retained scientific review; persisted current `publishedBy`/`publishedAt` identify the report binding confirmer and time. A report-specific normalization note belongs to that confirmer, not to the profile's earlier scientific reviewer. No report-level review rewrites originals or earlier evaluations.

Report review supports up to 10,000 input rows, matching report intake's total panel limit. Prearrival confirmed source rows whose reserved Ledger lot has not been posted remain unresolved without blocking preview of other rows. Atomic confirmation applies to the explicitly submitted compatible rows, not to every row in the original report; omitted unresolved rows remain unbound.

### Substance matrix publication

`POST /api/residue-matrix/publish` publishes changed complete scientific rules and explicitly requested company settings in one transaction. Administrator publishes system rules; Customer Manager publishes only own-company overrides/settings. Company adoption is never implied by rule publication. Cookie mutations require CSRF.

Request fields:

- `companyId`: null for system scope, otherwise authenticated owning company UUID.
- `rules`: changed rows `{ baseId, sourceId, rule }`. `rule` is the complete scientific rule; `sourceId` identifies an existing visible source profile. `baseId` is the expected predecessor in the publication scope, or null for a new company override. New unmatched scientific series still use expert rule publication.
- `settings`: explicit `{ ruleCode, baseId, mode, evidenceRef, applicabilityStatement }` changes, or empty.
- Shared `effectiveFrom`, optional `effectiveTo`, `reviewedBy`, `reviewAuthority`, `reviewedAt`.

Server derives rule/setting UUIDs, versions, predecessors and publication metadata; shared review/effective dates apply to the changed records. Stale predecessors return 409 and publish nothing; unchanged records are not rewritten. Response contains `{ rules, settings }` for published versions. Applicability remains full-rule scoped; matrix does not introduce wildcard methods, guessed factors or automatic scientific approval.

### Single-action laboratory confirmation

Existing `POST /api/lab-reports/{id}/confirm` retains identity/transcription checks and idempotency. Within its caller-owned transaction, it also binds uniquely compatible confirmed source/substance results to applicable adopted policy. Response adds `residueBindings`: per-row `resultId`, `sourceId`, `published` and `reasons`.

Missing policy, unsupported units/uncertainty, unavailable source stock or evidence conflicts leave affected rows unbound with explicit reasons; they do not erase a valid F11 identity confirmation. Existing result selections/retests are never silently replaced. Identity failures still reject confirmation. Exception-only residue preview/confirm routes remain available after policy adoption or explicit reviewed normalization/supersession. Persisted authorization controls binding separately from lab confirmation.

### Read-only live lot bounds

`GET /api/ledger/lots/{lotId}/residue-preview?substanceIds=<comma-separated-UUIDs>` requires `residue.evaluate` and current lot/company/facility scope. Supply 1–200 distinct nonempty substance UUIDs. Response contains `lotId`, `companyId`, `snapshotAt`, `engineVersion` and `results` with existing exact interval/provenance/reason contracts; no evaluation `id` or `persistedAt`.

Preview uses coherent read-only `REPEATABLE READ` materialization and the same calculator as evaluation, but neither replays nor appends an evaluation. `snapshotAt` describes its captured view, not guaranteed later stock eligibility. Lot detail loads preview automatically; **Refresh live preview** recomputes without history writes. **Lock snapshot for audit** retains the existing immutable evaluation POST and replay/history contracts.
