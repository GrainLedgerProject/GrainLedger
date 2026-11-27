# F14 manual challenge: OM5451 and Jasmine 85

Added on 27/11/2026 after the initial ST25-only challenge. This is additive local test data. The seed did not call Calculate, preview, propose or approve. Existing ST25 data and user-generated previews were preserved.

## Orders

All four new orders use **F14 Manual Challenge Market (synthetic)**, commodity RICE, target BLENDED_RICE and the same facility **GrainLedger Rice Demo Mill (synthetic)**. Each specification stores the required variety UUID, not a free-text note.

| Scenario | Export order | Required variety | Kg | Max moisture | Max broken | Independent expectation |
| --- | --- | --- | ---: | ---: | ---: | --- |
| OM-01 | `EXP-20261127-000045` | OM5451 | 1650 | 13% | 3.5% | PASS allocation exists |
| OM-02 | `EXP-20261127-000046` | OM5451 | 1800 | 12.6% | 4.5% | No PASS: moisture cannot reach 12.6% |
| JAS-01 | `EXP-20261127-000047` | Jasmine 85 | 1000 | 13.5% | 4.5% | PASS allocation exists |
| JAS-02 | `EXP-20261127-000048` | Jasmine 85 | 1200 | 13.3% | 4.5% | No PASS: moisture/broken requirements conflict |

These are mathematical expectations, not solver results. The ST25 manual orders remain `EXP-20261127-000041`–`000044`; see [their separate scenario](f14-manual-challenge-20261127.md).

## Qualified head-rice data

X/Y values below are the verified F12 upper bounds in mg/kg as received. Synthetic MRLs: X=0.100 and Y=0.050; assessment margin 10%, giving output thresholds X=0.090 and Y=0.045. These are test thresholds, not actual market law.

| Source | Lot | Variety | Available kg | Moisture | Broken | X upper | Y upper | Location |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | --- |
| OM-A | `HDR-20261127-000560` | OM5451 | 486.0 | 15% | 1.2% | 0.060500 | 0.008250 | Head Rice Store |
| OM-B | `HDR-20261127-000566` | OM5451 | 432.0 | 12.2% | 8% | 0.013750 | 0.038500 | Head Rice Store B |
| OM-C | `HDR-20261127-000572` | OM5451 | 378.0 | 13.2% | 3.5% | 0.044000 | 0.019250 | Head Rice Store |
| JAS-A | `HDR-20261127-000578` | JASMINE85 | 486.0 | 14.8% | 1% | 0.055000 | 0.008250 | Head Rice Store |
| JAS-B | `HDR-20261127-000584` | JASMINE85 | 432.0 | 11.8% | 10% | 0.016500 | 0.038500 | Head Rice Store B |
| JAS-C | `HDR-20261127-000590` | JASMINE85 | 378.0 | 13.3% | 3% | 0.044000 | 0.022000 | Head Rice Store |
| W-WRONG-VARIETY | `HDR-20261127-000554` | OM5451 | 540.0 | 11.7% | 0.8% | 0.013750 | 0.008250 | Head Rice Store |

The existing OM5451 lot W (`HDR-20261127-000554`) was the wrong-variety control for ST25 orders. It is a valid positive input for an OM5451 order because its genotype, QC and X/Y evidence match that order. An exclusion depends on the order; it is not a global ban on W.

The six new source chains each have a farmer, plot, variety-linked plot season, procurement, measured arrival, sample, confirmed two-analyte report, reviewed selections, drying and milling. Raw masses are 900/800/700 kg for A/B/C of each variety. The same balanced simulated fractions and reviewed X/Y profiles as the ST25 challenge are used. No scientific factor or production algorithm was changed.

## Broken rice and qualified stock

Each source also has a 98%-broken-kernel byproduct. Input MRL screening still applies to each lot:

| Source | Broken lot | Kg | X upper | Y upper | Qualified input? |
| --- | --- | ---: | ---: | ---: | --- |
| OM-A | `BRK-20261127-000561` | 72.0 | 0.096800 | 0.013200 | Yes |
| OM-B | `BRK-20261127-000567` | 64.0 | 0.022000 | 0.061600 | No: Y exceeds legal test MRL |
| OM-C | `BRK-20261127-000573` | 56.0 | 0.070400 | 0.030800 | Yes |
| JAS-A | `BRK-20261127-000579` | 72.0 | 0.088000 | 0.013200 | Yes |
| JAS-B | `BRK-20261127-000585` | 64.0 | 0.026400 | 0.061600 | No: Y exceeds legal test MRL |
| JAS-C | `BRK-20261127-000591` | 56.0 | 0.070400 | 0.035200 | Yes |
| W-WRONG-VARIETY | `BRK-20261127-000555` | 80.0 | 0.022000 | 0.013200 | Yes |

Qualified OM5451 stock: 1,836 kg head rice + 208 kg broken rice = **2,044 kg**. Qualified Jasmine 85 stock: 1,296 kg head rice + 128 kg broken rice = **1,424 kg**. Old basic-demo stocks lack the new X/Y panel and are not included. Stock of another variety must never fill a shortfall.

OM5451 and Jasmine 85 each now have a second head-rice store B for the dry/high-broken batch. Facility total: **24 locations** (the earlier 22 plus these two), with shared Bran and Husk stores unchanged. Each rice variety retains separate locations and lot identities.

## Independent feasibility evidence

<details><summary>Witness allocations for comparison after you run Calculate</summary>

These allocations were checked with decimal arithmetic against the actual stored QC/F12 bounds. They are not stored in a plan and are not required solver answers.

| Scenario | Example kg | Moisture | Broken | X upper | Y upper |
| --- | --- | ---: | ---: | ---: | ---: |
| OM-01 | W-WRONG-VARIETY=540, OM-A=300, OM-B=432, OM-C=378 | 12.77454545454545454545454545% | 3.376363636363636363636363636% | 0.02918 | 0.01869 |
| JAS-01 | JAS-A=322, JAS-B=300, JAS-C=378 | 13.333% | 4.456% | 0.039292 | 0.0225225 |

</details>

**OM-02:** Even ignoring broken kernels and output residue limits, the driest available 1,800 kg is W head+broken 620 kg at 11.7%, B head 432 kg at 12.2%, C head+broken 434 kg at 13.2%, and 314 kg from A at 15%. Its moisture is **12.757333…%**, already above 12.6%. Extra constraints cannot create a passing solution.

**JAS-02:** For head A/B, `3 × moisture + broken = 45.4`; for C it is 42.9, and at most 378 kg of C exists. Qualified broken-rice lots have a larger left side. For any 1,200 kg allocation with broken ≤4.5%, this gives `moisture ≥ (45.4×1200 − 2.5×378 − 4.5×1200)/(3×1200) = 13.370833…%`, above 13.3%. There is enough eligible mass, but the two quality limits cannot both pass.

Because all three varieties now have complete X/Y panels and usable QC in the same facility, wrong-variety lots cannot be dismissed merely as missing evidence. This permits bidirectional checks: ST25 orders must reject OM/Jasmine; OM orders must reject ST25/Jasmine; Jasmine orders must reject ST25/OM.

## FE steps

Log in as Export Staff, open one of orders 000045–000048, open its existing version-1 draft, and select the same demo facility. Check the required variety displayed in the draft, then run Calculate yourself. When checking exclusions, distinguish the required variety from product identity (Head rice/Broken rice). Do not require the solver to return the exact witness quantities above.

## Verification performed without Calculate

- Read back 24 new output lots plus existing W head/broken stock; checked exact kg, catalog variety UUIDs and F12 bounds for 14 grain lots.
- Verified all 24 new stored QC observations and their % units.
- Verified the qualified-stock totals and both feasible witnesses independently.
- Four new drafts are DRAFT with **0 previews and 0 proposals**. Total DB previews stayed at 19 before/after this seed, preserving a prior user calculation.
- No production code, migration, shared DB or existing ST25 preview was changed.


## Whole-kg planning update — 27/11/2026

F14 now selects integer kg per lot/location, minimum 1 kg, by integer solver variables rather than display rounding. The independent feasible witnesses in this document already use whole kg, so those feasibility expectations remain valid. Continuous infeasibility proofs also exclude integer solutions. Local store labels no longer have the suffix `(challenge)`; their UUIDs and stock are unchanged. No user demo order was recalculated for this update. See [the whole-kg planning contract and verification](../api/f14-whole-kg-planning.md).
