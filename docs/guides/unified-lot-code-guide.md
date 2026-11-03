# Lot codes after the unified-code cutover

New source and processing lots receive a code from the backend. The format is
`TOKEN-YYYYMMDD-SERIAL`, for example `RAW-20261017-000123`. The date is the
date the backend issues the code in `Asia/Ho_Chi_Minh`. The serial is one
PostgreSQL sequence for all lot types. It never resets. Gaps are normal after
failed or rolled-back requests. UUIDs remain the record identifiers in links
and API requests.

| Token | Stage | Product identity |
| --- | --- | --- |
| RAW | RAW_PADDY | RAW_PADDY |
| DRY | DRIED_PADDY | DRIED_PADDY |
| HDR | HEAD_RICE | HEAD_RICE |
| BRK | BROKEN_RICE | BROKEN_RICE |
| BRN | BRAN | BRAN |
| HSK | HUSK | HUSK |
| BLD | BLENDED_RICE | BLENDED_RICE |
| BGH | BAGGED_RICE | HEAD_RICE |
| BGB | BAGGED_RICE | BROKEN_RICE |
| BGL | BAGGED_RICE | BLENDED_RICE |

`POST /api/procurements` reserves one RAW code for each source line and returns
it as `items[].lotCode`. The receipt remains `PENDING_ARRIVAL` with zero usable
stock. The code is retained when mill staff accept the arrival and is also
returned from receipt, source-lot, and receiving reads. Retrying the same
client operation returns its stored response and codes.

Processing draft create/update assigns each new output code when the draft is
saved. The client sends a stable output UUID, stage, product identity, location,
and quantity, but leaves `code` empty. The response and subsequent detail
reads return the assigned code. An edit can retain the UUID and code only if
the output stage and product identity are unchanged. Changing either requires
a new output UUID. Removing a draft output does not release its code.

For a correction replacement of the same product, an output can explicitly
carry `originalLotId` referring to the output being replaced. The backend
checks the correction, owner, facility, stage, and product identity, then uses
the original code. A new or changed product gets a new code. Correction
lineage is checked again at commit.

Historical source and processing codes remain unchanged, as do committed
receipts, operations and stock. Historic receipt verification compares the
saved code with the recorded lot rather than reconstructing a code. Existing
legacy processing drafts cannot be edited or committed: create a new draft
with server-assigned codes. Lot codes are not proof of accepted stock; check
receiving status and available quantity.

Deployment requires database-owner review and explicit shared-database
approval before applying forward migration `20261103000200_LedgerLotCodeCutover`.
Pause intake and processing writes during cutover and deploy compatible API
instances together. Inventory pending source receipts and legacy drafts before
approval. A source lot without a matching reservation fails closed, including
pending historical receipts; resolve each affected case through an explicitly
approved data-preserving procedure before accepting it. The migration does not
create reservations or alter historical data. Review runtime grants using the
existing role-configuration procedure before restarting the API. Rollback to
older code is unsupported.

## Mixed-bulk custody generations

Mixed-bulk ingress and partial withdrawal create new immutable lot generations with new reserved lot codes; they do not reuse a prior pool code as a correction. Current generation and revision are exposed by Ledger lot/location reads; source composition and custody links are lineage, not separate physical stock. A full drain clears current generation pointer, but identity remains until recorded clearance. Mixed-custody correction and downstream compensation are unsupported and fail closed; original untouched-package correction is not enabled without approved policy. Migration `20261020000000_MixedBulkStorage` is forward-only and implemented locally, not deployed. No reset/backfill/conversion; get explicit database-owner approval before shared migration, grants or seeds.
