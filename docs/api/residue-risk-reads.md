# Residue change and risk read APIs

Implemented locally on 2026-11-23. These APIs read recorded F12/assessment history;
they do not execute propagation, approve a plan or certify market compliance.

## Routes and permissions

| Method and path | Response |
| --- | --- |
| `GET /api/residue-changes/{changeId}` | Company-scoped cause, origin references and processing progress |
| `GET /api/residue-changes/{changeId}/targets` | Paged target contexts and their evaluation/latest risk references |
| `GET /api/residue-risk-events` | Paged, filtered immutable risk-event projections |
| `GET /api/residue-risk-events/{eventId}` | One recorded assessment snapshot |

All four require an authenticated, active Customer Manager or Export Staff in a
Customer organization, with the existing `residue.evaluate`, `trace.read.tenant`
and `export-orders.read` permissions. Authorization rechecks persisted role and
company. Caller-supplied actor/company query parameters are not accepted.
Administrator and mill roles do not gain commercial history access through these
routes. Their existing lot/residue APIs are unchanged.

An owned cause is visible to its company. A global profile cause is visible only
after discovery records a target for the current company. Its response is a
company projection: `companyId` is the viewer's company, `globalCause` is true and
progress counts include only owned targets. It does not expose another company's
targets, error text or processing counts. An unrelated company receives 404.

## Queries

Detail routes accept no query parameters. List routes accept each parameter once;
unknown, repeated, empty or invalid values return 400.

| Query | Targets list | Risk list | Allowed values |
| --- | --- | --- | --- |
| `page` | yes | yes | 1..1,000,000; default 1 |
| `pageSize` | yes | yes | 1..100; default 25 |
| `targetKind` | yes | yes | `LOT`, `PLAN`, `CONTAINER` |
| `targetId` | yes | yes | Nonempty UUID in canonical dashed format |
| `state` | yes | no | `PENDING`, `PROCESSING`, `COMPLETED`, `FAILED_RETRYABLE` |
| `assessmentStatus` | no | yes | `COMPLETE`, `UNRESOLVED`, `PASS`, `FAIL`, `INCOMPLETE` |
| `from`, `to` | no | yes | ISO 8601 timestamp with explicit Z/offset |

Date filters use `evaluatedAt`: from is inclusive, to exclusive; from must precede
to. For example, `from=2026-11-23T00:00:00Z&to=2026-11-24T00:00:00Z`.
Encode the plus sign in non-UTC offsets, such as `07:00:00%2B07:00`.

Both lists return `items`, `page`, `pageSize`, `total` (after filtering) and
`hasNext`. Targets sort by UUID ascending. Risks sort by evaluated time descending,
then UUID ascending. Count and page share one MVCC read snapshot. Concurrent new
events can move offset pages between separate requests; the API does not promise a
frozen cross-request dataset.

## Response fields and redaction

Change detail has `id`, `companyId`, `globalCause`, `changeKind`, `subjectId`,
`subjectVersion`, `phase`, `knownAt`, `dueAt`, `state`, `discoveryComplete`,
`progress`, `errorCode` and `originReferences`. The references allow policy code,
report/event/operation IDs and effective/confirmation timestamps. They exclude
raw origin JSON, source arrays, root lots, SQL table names, lease tokens,
generation and frontier. Processing errors return generic codes, never raw
exception text. A global response's state is scoped progress, not a statement
that every company completed the parent job.

Target items contain `id`, `changeId`, `targetType`, `targetId`,
`relationshipBasis`, `state`, `evaluationId`, `dependencyRevision`, `substanceIds`,
`discoveredAt`, `updatedAt`, `errorCode` and `latestRiskEventId`.

Risk items/detail contain `id`, `changeId`, `targetType`, `targetId`,
`relationshipBasis`, `assessmentStatus`, `snapshotAt`, `evaluatedAt`,
`evaluationId`, `dependencyRevision` and `assessment`. Assessment includes the
engine version, available market/commodity/basis/policy/history references,
findings and per-substance results. Fields that have not been recorded remain
null; the reader does not invent an MRL version or material basis.

Projection is field- and scalar-type-checked, including nested results/rules and
findings. Raw lab payloads, original numeric/uncertainty text, farmer contacts,
private evidence references and arbitrary extra metadata are excluded. Customer
Managers receive whitelisted scientific provenance IDs; Export Staff do not
receive source/result-set provenance. Numeric bounds, reason strings and rule
version references remain available to both. Unknown bounds remain explicit
`lower: null` / `upper: null`, rather than 0.

A LOT event's `COMPLETE` means F12 has finite scientific bounds; it is not MRL
PASS. `latestRiskEventId` identifies the last recorded event. Reassessing it for
current order/market conditions and plan/container freshness belongs to the
Export integration stage, not to a GET.

## Read-only behavior and errors

Reads run in an explicit read-only REPEATABLE READ transaction. They do not create
jobs, evaluations, events, business audit entries or reservations, and do not
invoke the existing stateful container GET workflow. Normal authentication
session timestamp maintenance remains part of authentication, outside this
business read contract. Authorized responses use `Cache-Control: no-store`.

| Status | Meaning |
| --- | --- |
| 400 | Invalid, unsupported, repeated or empty query; empty resource UUID |
| 401 | No authenticated active session |
| 403 | Current role/permission/organization cannot use the API |
| 404 | Resource does not exist or is outside the current company scope |
| 503 | Public projection schema has not been deployed |

Malformed UUID path segments fail route matching (404). The schema guard refuses
to fall back to the old foundation reader, which returned private rows. Startup
and GETs do not migrate or repair grants.

## Migration and verification

`20261123030000_ResidueRiskReads` embeds `ResidueRiskReads.sql`, replaces the
protected reader with explicit public projections and adds a risk-time index.
The reader keeps its existing restricted EXECUTE grant. Projection helpers are
private and have no PUBLIC/runtime EXECUTE permission.

`ResidueRiskApiTests.cs` calls all four routes through the HTTP test host. It
checks company/role/session changes, global scoped progress, nested redaction,
filtered totals, deterministic ties, UTC-offset equivalence, malformed queries,
no business/audit/reservation changes, old-schema fail-closed behavior and a real
confirmation → propagation → GET sequence. Projection fixtures deliberately
include private and malformed extra JSON fields; these are synthetic privacy
regressions, not operational QC evidence.

The existing backend CI discovers this suite through its solution-wide test
command. No remote CI or shared-database deployment is implied by local tests.
See [the journal and worker contract](residue-change-journal.md) for propagation
and opt-in configuration. Commercial context mapping remains a later stage.

Final local verification: **147/147** integration tests passed, no skips, including
27 API cases and the prior 120 F12/publication/worker regressions. The run used
isolated PostgreSQL Testcontainers and a separate build output. Log evidence is
recorded in the implementation plan; no shared migration or remote CI was run.
