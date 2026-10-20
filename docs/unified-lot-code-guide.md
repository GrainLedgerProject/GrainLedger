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

Historical `LL-…` source codes and pre-cutover manually coded processing
drafts remain unchanged. New code syntax is not used to infer whether a record
is historical. Lot codes are not proof of accepted stock; check receiving
status and available quantity for that.

Deployment requires review of the forward-only Ledger migration and an
explicit shared-database migration approval. After migration, reconfigure the
restricted runtime grants with the existing role-configuration procedure
before starting the new API. Pause intake and processing writes during the
API cutover; old API instances cannot issue or validate the new codes. Do not
roll back to an API version that only understands `LL-…` after new codes exist.
