# Current local rice warehouse demo

Updated on 27 November 2026 to match the existing byproduct variety policy. This is simulated data, not operational warehouse data.

Database: `127.0.0.1:5433/grainledger`. API: `http://localhost:5080`. Shared database configuration and data were not changed. Identity accounts, catalogs, MRL, residue profiles, company settings and numbering counters were retained during the authorized local demo rebuild.

The latest pre-rebuild recovery dump is `%LOCALAPPDATA%\GrainLedger\backups\grainledger-before-shared-byproducts-20261127-120358.dump`. The dump archive list was readable; no restore was performed. Local maintenance restored all truncate guards before commit. Normal runtime permissions remain restricted.

## Facility and layout

The only active facility is **GrainLedger Rice Demo Mill (synthetic)**. ST25, OM5451 and Jasmine 85 each have these six locations:

| Location | Purpose | Kind |
| --- | --- | --- |
| Receiving Store | Incoming paddy | STORE |
| Dried Paddy Store | Dried paddy awaiting milling | STORE |
| Head Rice Store | Head rice of that variety | STORE |
| Broken Rice Store | Broken rice of that variety | STORE |
| Blended Rice Store | Finished rice blends of that variety | STORE |
| Isolation Store | Segregated stock pending review | ISOLATION |

The facility also has two shared ordinary stores:

| Location | Current contents |
| --- | --- |
| Shared Bran Store | 480 kg bran, 6 lots from ST25, OM5451 and Jasmine 85 |
| Shared Husk Store | 600 kg husk, 6 lots from ST25, OM5451 and Jasmine 85 |

Total: **20 locations**, comprising 17 ordinary stores and 3 isolation locations. Bran and husk are separate product identities; they are not combined with one another.

An ordinary shared store retains separate lot identities. Sharing a location does not perform a physical merge. No mixed-bulk pools or physical byproduct merges were created by this seed. A later explicit MERGE checks the current company policy at commit.

## Mixing rules and Customer Manager override

| Same product, different registered varieties | Effective rule |
| --- | --- |
| HEAD_RICE | Cross-variety merge rejected |
| BROKEN_RICE | Cross-variety merge rejected |
| BRAN | Allowed by system default; Customer Manager may forbid it |
| HUSK | Allowed by system default; Customer Manager may forbid it |

Same-variety lots may be merged when the other Ledger conditions pass. Mixing head rice with broken rice is a separate BLENDING operation requiring approved compatibility; it is not a MERGE between different product identities.

Log in as `customer.manager@grainledger.local`, open **Byproduct mixing** (`/byproduct-mixing`). Bran and Husk have independent settings:

- **Use system default (Allow)** uses the default `allowCrossVariety=true`.
- **Allow different varieties** records a company override allowing mixing.
- **Keep varieties separate** records a company override forbidding cross-variety mixing.

Enter a reason and save. The setting is company-scoped and checked at commit, including drafts created before the change. It governs future mixing; it does not unmix committed stock or rewrite its lineage. A completed cross-variety byproduct merge has variety status MIXED, not UNKNOWN, with the source varieties retained through lineage.

The local demo currently uses SYSTEM_DEFAULT/Allow for both BRAN and HUSK. No company override was changed during verification.

## Stock and source evidence

Each variety has two received source lots of 1,000 kg each. Each is dried to 900 kg and milled into 600 kg head rice, 100 kg broken rice, 80 kg bran and 100 kg husk, with 20 kg milling loss. Source samples have confirmed synthetic reports and reviewed residue selections. Head and broken rice have reviewed quality observations.

| Variety | Head rice, separate store | Broken rice, separate store | Bran in shared store | Husk in shared store |
| --- | ---: | ---: | ---: | ---: |
| ST25 | 1,200 kg | 200 kg | 160 kg | 200 kg |
| OM5451 | 1,200 kg | 200 kg | 160 kg | 200 kg |
| Jasmine 85 | 1,200 kg | 200 kg | 160 kg | 200 kg |

There are 36 Ledger lots including spent raw/dried intermediates; 24 output lots have positive stock. All 36 are SINGLE because no physical merge has been executed. UNKNOWN and MIXED counts are zero. Receiving, dried-paddy, blended-rice and isolation locations are initially empty. There are no blend reservations or approved export proposals.

## Current F14 orders

The previous local demo order numbers were removed by the authorized rebuild. Use these current orders:

| Order | Required variety | Mass | Result |
| --- | --- | ---: | --- |
| EXP-20261127-000037 | ST25 | 900 kg | PASS; ST25 only |
| EXP-20261127-000038 | OM5451 | 900 kg | PASS; OM5451 only |
| EXP-20261127-000039 | Jasmine 85 | 900 kg | PASS; Jasmine 85 only |
| EXP-20261127-000040 | ST25 | 1,300 kg | INCOMPLETE; no passing plan, no substitution |

Specifications store registered variety UUIDs from version 1. Head-rice-only blend compatibility and output yield 1 are configured. Orders require moisture at most 14% and broken kernels at most 5%. Synthetic residue MRL is 0.5 mg/kg with a 10% fixture assessment margin. These values are test configuration, not actual legal market limits.

## Verification

- Existing backend policy/stock/segregation tests: 6/6 PASS on disposable PostgreSQL, including default byproduct merges, separate Bran/Husk overrides, company and role scope, stale revisions, blocked commit preserving stock, and rejection of cross-variety rice processing.
- Existing Customer Manager policy component test: 1/1 PASS.
- Actual local API: both byproducts read SYSTEM_DEFAULT/Allow, 20 locations visible, all 24 positive stock lots retain mass, and all 12 grain inputs retain COMPLETE F12 bounds.
- F14 acceptance checks run each current order through all five priorities, without approving proposals or consuming stock.

Refresh the browser after rebuild to discard selections referencing removed demo records. Use the assigned Mill Operator for stock and processing, Customer Manager for company mixing policy, and Export Staff for the orders above.


## Additional uncalculated challenge dataset — 27 November 2026

An additive manual challenge now uses the same facility with two extra ST25 head-rice locations B/C (22 locations total). The 36-lot basic dataset described above is retained; there are now 84 Ledger lots in total. Eight new source chains add 32 positive output lots, two synthetic residue substances and a separate challenge market. Orders `EXP-20261127-000041`–`000044` each have an empty draft; no Calculate/preview/proposal was run for them. See [the full scenario, lot data, expected results and FE steps](../scenarios/f14-manual-challenge-20261127.md).


## Multi-variety extension

OM5451 and Jasmine 85 now each have an additional head-rice store B and three new source chains with complete challenge X/Y evidence. Current total: 24 locations and 120 Ledger lots. Orders `EXP-20261127-000045`–`000048` add one feasible and one quality-infeasible case for each variety. They were created with empty drafts and no calculation. Existing ST25 data/results were retained. See [the multi-variety scenario](../scenarios/f14-multivariety-20261127.md).
