# Companion Capture codes

New Capture records created by the current web client receive display codes from
the server. UUIDs remain the stable record identifiers used by API routes and
relationships.

| Record | New code example | Counter scope |
| --- | --- | --- |
| Farmer | `FRM-0042` | Company; never resets |
| Plot | `PLT-0042-01` | Parent farmer; never resets |
| Procurement receipt | `PRC-20261017-0015` | Company and record type; never resets |
| Sample | `SMP-20261017-0008` | Company and record type; never resets |

Dates use `YYYYMMDD` in Asia/Ho_Chi_Minh: `receivedAt` for procurement and
`collectedAt` for samples. Serial numbers have minimum widths of four digits
(two for plot number), and expand when necessary. Receipt and sample counters
are independent, continue across dates, and may have gaps. A code is not reused
after archival. The physical `sealCode` remains the value printed on the seal
and is entered by the collector.

Plot ownership is fixed after registration. `PUT /api/plots/{id}` rejects a
different `farmerId`; the web editor displays the original farmer. For a
mistaken unused registration, archive the plot and register a new one.

Historical farmer display codes remain unchanged. Each historical farmer has
a stable internal number so new plots can receive a `PLT` code. Always use the
`farmerId` relationship for ownership; never parse it from the code.

Create requests use `payloadSchemaVersion: 2` and omit `payload.code` for
farmer, plot and sample. Procurement version 2 also receives a server-generated
code. Version 1 creation requests for these four record types are no longer
accepted; other Capture operations retain their own version contracts. The
client keeps a pending request unchanged while retrying after an uncertain
outcome. Allocation and the new record are in the same database transaction
under Capture's shared write lock.

Ledger source and processing lot codes continue to follow
[`unified-lot-code-guide.md`](unified-lot-code-guide.md). A correction
replacement of the same product retains the original lot code; it does not
gain an `-R1` suffix.

Deploy the Capture migration before starting the new API. It seeds each
company/type counter from the highest existing dated counter and recognizable
historical code serial, without changing stored codes. Do not run the migration
as part of ordinary startup. Test with an isolated PostgreSQL database first.
During the API/web cutover, pause Capture writes and stop old API instances;
otherwise old date-scoped counters can advance after the migration.
