# Manual test flow — residue bounds

Flow: receive paddy → collect samples → record laboratory evidence → process stock → calculate bounds → receive late evidence → recalculate → compare history.

Use an isolated test environment only. Numbers below are synthetic calculation inputs, not scientific approvals or realistic production yields. Do not run these write-based scenarios against shared Supabase without explicit database-owner approval. Do not publish synthetic rules as operational policy.

This guide covers the business flow from new source records. The existing browser-test scenario already creates the source, processing and sample records; when using that prebuilt scenario, inspect those records rather than creating them again.

Environment prerequisites: matching application and module migrations installed in an isolated database, configured runtime role, and test accounts for each role below. See [Setup and deployment](../setup-and-deployment.md). Do not run migrations or seeds against shared data for this test.

## 1. Prepare test data and rules

For field-by-field instructions and synthetic rule examples, see [Residue rules entry guide](residue-rules-entry-guide.md). Use numeric interpretation and baseline drying/milling/blending rules for this flow; alternative ND and cumulative examples are separate exercises.

Keep both source lots under the same customer company, with operators assigned to the processing facility.

| Source | Received mass | Laboratory value | Reviewed absolute uncertainty | Source interval |
| --- | --- | --- | --- | --- |
| A | 60 kg | 0.15 mg/kg | 0.05 mg/kg | `[0.10, 0.20]` |
| B | 40 kg | 0.55 mg/kg | 0.25 mg/kg | `[0.30, 0.80]` |

Use the same substance, residue definition and analytical basis throughout. The laboratory method must match the interpretation rule.

As **Administrator**, open **Administration → Catalogs → Commodities** and configure synthetic reviewed rules before calculating:

| Process | Input → output | Concentration factor |
| --- | --- | --- |
| Drying | RAW_PADDY → DRIED_PADDY | 1 |
| Milling | DRIED_PADDY → HEAD_RICE | 0.5 |
| Blending | HEAD_RICE → BLENDED_RICE | 1 |
| Mixed-storage ingress, if tested | BLENDED_RICE → BLENDED_RICE | 1 |
| Mixed-storage withdrawal, if tested | BLENDED_RICE → BLENDED_RICE | 1 |

Then, as **Customer Manager**, open **Residue profiles**. Select the interpretation profile and every process profile used here, choose system-default adoption, and publish with one shared reviewed effective period covering test events. Check each inherited evidence/applicability summary and record the actual setting reviewer/authority/review time once for the batch. Separate setting IDs/versions remain available for audit. System rule publication is not adoption; no seed or automatic adoption exists.

Profile management checks: **Create rule** opens an expert dialog with no scientific values supplied. **Edit selected profiles** opens the batch dialog; explicitly select exact profiles and complete shared review. Check rule search/task/scope/status filters and sorting; current scoped series and historical-version counts must remain distinct. Earlier versions stay collapsed for broad filters; a historical-only match reveals history while retaining the latest tip. Confirm adoption/override history search and mode/effective-status filters can find earlier decisions without changing them. Neither grouping nor batch review authorizes fewer scientific applicability requirements.

Also required:

- Reviewed numeric interpretation matching method, substance, commodity and material basis.
- Explicit outward precision, for example **12 decimal places**.
- Applicable process rules with explicit fraction/product identity, basis, conditions and factor semantics. Use per-step factors for this test. `UNCONDITIONAL` only for explicitly reviewed condition-independent synthetic rule.
- Reviewed homogeneous mixing for multi-input operations.
- Existing facility balance policies and blend compatibility.
- Effective dates covering test events.
- Clearly labelled synthetic evidence references, applicability statements and reviewer metadata on rules and settings.

Do not weaken validation when setup is missing. Missing adoption or applicable rule must produce `UNRESOLVED`.

For separate relative-uncertainty rehearsal, use explicitly reviewed `NUMERIC_RELATIVE_UNCERTAINTY` with exact percentage (synthetic `10` means 10%, not a fraction). Reviewed normalized absolute uncertainty takes priority. Nonblank raw uncertainty without normalized absolute value must remain unresolved; do not parse text. Relative use at numeric zero requires explicit reviewed `RelativeUncertaintyAtZero: true`. These are behavior checks only; synthetic values are not operational.

## 2. Register and receive two source lots

### Purchasing Staff

1. Open **Paddy intake**.
2. Register source A: **60 kg**.
3. Register source B: **40 kg**.
4. Record the generated source-lot codes.

**Expected:** procurement source records exist, but registration alone creates no usable stock.

### Mill Operator

1. Open **Ledger → Receive stock**.
2. Record arrival measurements:
   - A: **60 kg**.
   - B: **40 kg**.
3. Complete receiving acceptance.

Use matching expected and measured masses to avoid an unrelated shortage-review scenario.

**Expected:**

- Two usable `RAW_PADDY` lots.
- Stock quantities **60 kg** and **40 kg**.
- Correct company ownership and facility scope.

## 3. Collect samples before processing

As **Purchasing Staff**, repeat for A and B:

1. Open **Paddy intake → receipt → source-lot code**.
2. Click **+ Take sample**.
3. Enter the material sampled, collection time and synthetic physical seal code.
4. Save and record the generated sample code.
5. Click **Send to laboratory** and record dispatch.

**Expected:**

- Each sample belongs to exactly one original procurement source.
- Sample and dispatch records do not change stock.
- Sample code and seal remain distinct identifiers.

See [Laboratory workflow guide](lab-flow-guide.md) for sample identity and custody requirements.

## 4. Confirm source A’s report; leave B pending

As **Customer Manager**:

### Report A

1. Open **Samples → sample A → + Receive report**.
2. Upload a clearly labelled synthetic report belonging only to the test company.
3. Enter matching printed sample and seal identifiers.
4. Type **0.15** in **Reported result**, set unit **mg/kg** and laboratory method, and retain original uncertainty text under **Additional lab details**. Do not duplicate the numeric value in expert fields.
5. Save report evidence.
6. Open **Review and confirm**.
7. Verify sample identity and transcription against the synthetic original.
8. Choose **Confirm identity and bind compatible results**.
9. Inspect returned residue-binding outcomes. This baseline's nonblank uncertainty text requires exception review, so that row stays unbound:
   - Open **Resolve exceptions or bind after policy adoption**.
   - Server matches the adopted profile against confirmed source, substance, commodity, method, stage, canonical unit and effective applicability.
   - Enter reviewed absolute uncertainty **0.05** with supporting review; never infer it by parsing raw text.
   - Preview interval/reasons and explicitly publish the reviewed exception selection.
   - In the separate relative-uncertainty rehearsal with no reported uncertainty, compatible adopted policy binds during step 8; no second routine confirmation is required.

### Report B

Record report B with **0.55 mg/kg** and its original uncertainty text, but **do not confirm or publish its reviewed selection yet**.

**Expected:** A has usable reviewed evidence; B does not. Identity confirmation and interval interpretation retain separate audit facts, even when the routine action records both.

## 5. Process both lots while B’s evidence remains pending

As **Mill Operator**, create drafts and use **Commit processing**:

1. Dry A → dried lot A.
2. Dry B → dried lot B.
3. Mill dried A → head-rice lot A.
4. Mill dried B → head-rice lot B.
5. Blend both head-rice lots → blended lot.

For this synthetic arithmetic test, retain:

- A path: **60 kg**.
- B path: **40 kg**.
- Blend output: **100 kg**.
- Declared loss: **0**.

These equal-mass transitions isolate residue calculation; they are **not realistic milling-yield assumptions**. Record only the positive output fraction used by this synthetic example; do not fabricate additional byproducts.

**Expected:**

- Operations become committed.
- Inputs are consumed and outputs created.
- The blend retains ancestry through both paths.
- Missing laboratory evidence does not independently invent a stock or HOLD block.

If nonzero measurement variance occurs, follow existing Manager review rather than bypassing it. Existing receiving, HOLD, balance and compatibility restrictions still apply.

## 6. Calculate before late evidence

As **Customer Manager**:

1. Open **Ledger → Stock → blended lot detail**; live bounds load automatically.
2. Inspect the test substance's live unresolved result and captured time.
3. Select the test substance for historical audit.
4. Click **Lock snapshot for audit** and record its timestamp.
5. Expand reasons and lineage dependencies.

**Expected:**

- Result: **`UNRESOLVED`**.
- No finite interval; unknown endpoints are null in the API response.
- Reasons identify unresolved evidence or a positive parent dependency.
- Both source paths remain represented.
- Calculation changes neither stock nor HOLD state.

**Failure:** the system drops B and calculates from A alone.

## 7. Confirm B’s evidence and recalculate

As **Customer Manager**:

1. Open report B.
2. Review sample identity and transcription.
3. Choose **Confirm identity and bind compatible results**. This baseline needs normalized uncertainty, so inspect its explicit exception.
4. Open exception review and draft B's reviewed result selection:
   - Numeric result: **0.55 mg/kg**.
   - Reviewed absolute uncertainty: **0.25 mg/kg**, entered in this report's original unit.
   - Same compatible basis and residue definition as A.
5. Preview the server assessment; retain profile scientific review and record the actual normalization review note.
6. Publish the reviewed exception selection, then return to blended lot.
7. Click **Refresh live preview**. Refresh must not append evaluation history.
8. Click **Lock snapshot for audit** to retain the new finite result alongside the earlier unresolved snapshot.

**Expected result:**

```text
Status: COMPLETE
Lower:  0.090000000000
Upper:  0.220000000000
Unit:   mg/kg
```

Inspect:

- Provenance references A and B.
- Applied interpretation and process rules.
- Physical dependencies.

`COMPLETE` means a finite interval was established under the referenced reviewed inputs, not legal PASS.

## 8. Compare historical snapshots

1. Open the earlier snapshot created before B confirmation.
2. Confirm it remains **`UNRESOLVED`**.
3. Open the newest snapshot.
4. Confirm `[0.09, 0.22] mg/kg`.
5. Reload the page and reopen both snapshots.

**Expected:** history persists; later evidence does not rewrite earlier evaluations.

## 9. Optional mixed-storage continuation

If the mixed-bulk location and policies are configured:

1. Transfer **100 kg blend** into mixed-bulk storage.
2. Withdraw **25 kg** to an ordinary location.
3. Inspect:
   - Withdrawn aliquot: **25 kg**.
   - Remaining pool: **75 kg**.
4. Calculate bounds for both.
5. Open the original exhausted blend through its saved lot link and calculate again.

**Expected:** all three yield `[0.09, 0.22] mg/kg`. Withdrawal does not scale concentration by remaining quantity.

## 10. Negative checks

Use separate test records for conflicting-evidence scenarios so they do not contaminate the successful baseline.

| Test | Expected |
| --- | --- |
| Select a substance absent from reports, without approved fallback | `UNRESOLVED`, never zero |
| Evaluate a path without a compatible process rule | `UNRESOLVED`, never implicit factor 1 |
| Create a conflicting confirmed retest without reviewed resolution | `UNRESOLVED`; latest or lowest must not win automatically |
| Assigned operator opens derived bounds | Allowed |
| Operator requests the original laboratory report | Denied |
| Different company opens the saved lot URL | Access denied; no result data |
| Confirm a supported `150 µg/kg` result under adopted synthetic relative 10% profile, without reported uncertainty | Same `[0.135, 0.165] mg/kg` source interval as `0.15 mg/kg`; original unit/value and scale retained |
| Confirm a report containing one compatible row and one missing-policy substance | Identity remains confirmed; compatible row binds, missing-policy row remains an explicit exception |
| Open or refresh live preview repeatedly | No evaluation history rows appended |
| Publish focused batch-editor change against stale predecessor | 409; no partial rule/settings publication, draft retained |
| Filter a scoped series or search its Code when both current and earlier versions match | Earlier versions remain collapsed until explicitly opened |
| Search metadata found only in a historical version | Matching history opens, latest published tip remains visible, no automatic revision |
| Open Create rule / close either editor by keyboard | Required scientific inputs remain blank; focus stays inside dialog and returns to its trigger on close |

## Pass condition

Baseline pass criteria: late evidence updates only new calculations, history stays immutable, missing scientific inputs never become guessed bounds or PASS, and residue evaluation does not alter stock, original laboratory evidence or HOLD state. Extension acceptance additionally requires explicit profile adoption, fail-closed override behavior, correct uncertainty handling/provenance and real UI/API verification.

Numeric examples above are expected synthetic test data, not operational scientific policy. Observed checks and isolated API/browser scenarios are recorded separately in the [focused-profile verification log](../plans/residue-bounds-plan.md#16-focused-profile-management-and-rule-volume-audit); that log, rather than this walkthrough, identifies which flows were exercised.
