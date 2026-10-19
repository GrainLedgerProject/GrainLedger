# Companion Capture codes

New Capture records created by the current web client receive display codes from
the server. UUIDs remain the stable record identifiers used by API routes and
relationships.

| Record | New code example | Counter scope |
| --- | --- | --- |
| Farmer | `FRM-0042` | Company; never resets |
| Plot | `PLT-0042-01` | Parent farmer; never resets |
| Procurement receipt | `PRC-261017-0015` | Company and Vietnam local `receivedAt` date |
| Sample | `SMP-261017-0008` | Company and Vietnam local `collectedAt` date |

The numbers have minimum widths of four and two digits, respectively. They
expand when necessary. Receipt and sample counters are independent. A code is
not reused after archival. The physical `sealCode` remains the value printed
on the seal and is entered by the collector.

Plot ownership is fixed after registration. `PUT /api/plots/{id}` rejects a
different `farmerId`; the web editor displays the original farmer. For a
mistaken unused registration, archive the plot and register a new one.

Migration assigns each historical farmer a stable internal number so new plots
under that farmer can receive a `PLT` code. Historical `ND`, `TR`, `TM`,
`SMP-YYYYMMDD-hex` and manually entered codes remain unchanged. A historical
farmer may therefore keep an `ND` display code while a new child plot uses
the internal farmer number in its `PLT` code. Always use the `farmerId`
relationship for ownership; never parse it from the code.

Create requests with `payloadSchemaVersion: 2` omit `payload.code` for farmer,
plot and sample. Procurement version 2 has the same fields as version 1 and
receives a `PRC` code. Existing version 1 requests retain their original
canonical content and replay response, including any legacy code. The client
keeps a pending request unchanged while retrying after an uncertain outcome.
The server checks the operation record before allocating a number. Allocation
and the new record are in the same database transaction under Capture's
shared write lock.

Ledger source and processing lot codes continue to follow
[`unified-lot-code-guide.md`](unified-lot-code-guide.md). A correction
replacement of the same product retains the original lot code; it does not
gain an `-R1` suffix.

Deploy the Capture migration and reconfigure the restricted runtime role's
table grants before starting the new API. Do not run the migration as part of
ordinary startup. Test with an isolated PostgreSQL database first. During the
API/web cutover, pause new Capture writes so old API instances cannot issue
legacy codes for new requests.
