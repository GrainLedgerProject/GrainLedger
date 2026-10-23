# Residue profiles and rules — field guide with synthetic examples

Routine workflow is short: use an existing reviewed system profile when applicable; Customer Manager explicitly adopts it under **Residue profiles** before finite calculation. Create a company override only when company review approves a complete alternative rule with same Code series. Return to inheritance by publishing a new `SYSTEM_DEFAULT` setting. No profile is adopted automatically.

Companion documents: [residue-bounds plan](residue-bounds-plan.md) and [end-to-end manual test](residue-bounds-manual-test-guide.md).

**Test safety:** every concentration, uncertainty, factor, definition, method, basis and review identity in examples is synthetic. They demonstrate application behavior, not scientific validity, realistic yields or legal compliance. Use isolated database with matching migrations and restricted runtime role. Never publish examples as operational policy or write to shared Supabase without explicit database-owner approval. See [setup and deployment](setup-and-deployment.md).

## 1. Routine company/profile workflow

| Need | Workflow |
| --- | --- |
| Adopt reviewed system policy | As Customer Manager, open **Residue profiles**, select the applicable profiles, inspect their scientific summaries, and publish one explicit reviewed adoption with a shared effective period and review. Each profile keeps its own immutable setting/version. |
| Return from override to inheritance | Select the profile series and system-default mode, then publish a reviewed return. This is versioned, not deletion. |
| Use company-specific scientific policy | Clone an eligible system profile as a company rule. Inherited applicability appears as a summary; change the reviewed percentage or factor, use advanced applicability only for real exceptions, and complete fresh scientific review. Then explicitly select the reviewed company override. |
| Add system policy | Administrator opens expert scientific-profile setup through **Administration → Catalogs → Commodities**. Full applicability and actual scientific review remain required; no numerical defaults are invented. Rule publication alone does not adopt it for any company. |
| Review calculation | Confirm sample identity/transcription once with **Confirm identity and bind compatible results**. Compatible adopted profiles bind automatically; returned exceptions stay unbound. Open exception review only for missing policy, normalization or conflicting evidence. |

No company setting means no adopted profile and finite calculation stays `UNRESOLVED`. Settings and rules are append-only/versioned; supersession stays within the same scope. An active override that does not match blocks corresponding inherited system applicability. Distinct matching series or same-scope ambiguity fail closed. Never assume default profile, coefficient, scientific rule, or successful calculation.

Normal report work does not require entering scientific rules again or a second routine binding confirmation. A uniquely compatible adopted profile supplies its reviewed basis, residue definition and supporting policy review. Report binding confirmation is recorded separately from scientific review. An applicable profile is not permission to replace laboratory-provided uncertainty, ignore a missing ND threshold, or choose between conflicting reports.

### Matrix editing and live bounds

The substance matrix summarizes existing reviewed profiles, not one universal policy per substance. Inspect each profile's commodity, method, stage/basis, fraction, conditions and effective dates before editing. Change reviewed uncertainty/ND settings or processing factors, retain explicit factor ranges where needed, then publish changed rules with one actual scientific review. Unchanged profiles are not rewritten; stale publication retains the draft and requires explicit refreshed review. Missing profiles use expert setup; blank cells never imply factor 1.

Company default/override choice remains explicit and independently versioned. Publication of a system rule does not adopt it. No method wildcard or automatic scientific approval exists.

Opening lot detail loads live bounds without writing history. **Refresh live preview** captures current evidence/policy again. **Lock snapshot for audit** appends an immutable historical evaluation; earlier snapshots never become current merely because they are the latest saved record.

When resolving a report exception, enter reviewed absolute uncertainty or reporting threshold in the original selected report row's unit. For example, a reviewed `50 µg/kg` uncertainty is entered as `50`, then normalized to `0.05 mg/kg`; do not pre-convert and scale it twice. Raw uncertainty wording remains unchanged and is never parsed automatically.

## 2. Which records do you need?

| Record | Who records it | Purpose |
| --- | --- | --- |
| Interpretation/process rule | Administrator for system scope; Customer Manager for own-company full-rule override | Full reviewed scientific rule series; an override uses same Code and compatible identity as its system series. |
| Company policy setting | Customer Manager, own company only | Explicit, effective-dated adoption/return to system inheritance or selection of a company override. |
| Confirmed laboratory report | Customer Manager | Records laboratory facts and confirms sample identity and transcription. |
| Reviewed report selection | Customer Manager | Selects applicable confirmed result and records normalized basis, residue definition and, when required, absolute uncertainty or reporting threshold. |
| Source-group fallback | Customer Manager | Separate reviewed evidence for explicitly grouped sources; not an interpretation or processing rule. |

For baseline A/B synthetic exercise, create one numeric interpretation system rule and three per-step process rules: DRYING, MILLING and BLENDING. Explicitly adopt every system profile used by test company before calculating. Optional mixed-storage continuation needs two additional process rules and explicit adoption. Company settings are separate from rule publication.

Lower sections retain detailed rule-entry examples for exceptional scientific-rule setup, overrides, review and troubleshooting. They are **synthetic rehearsals only**, never operational values or recommendations. Examples are alternatives where noted; do not publish every overlapping example into one baseline.

### Before entering rule values

1. Confirm approved scientific source, applicability and review authority; do not use this guide's synthetic values operationally.
2. For system policy, sign in as **Administrator** and open **Administration → Catalogs → Commodities**. For own-company override, use **Residue profiles** as Customer Manager; select matching system rule and **Clone as company rule** to retain its Code and applicable series identity.
3. Select actual existing Catalog **Substance** and **Commodity**. A display name does not replace persisted identity.
4. Record exact method, residue definition, analytical basis, material stages and output product identity applicable to actual evidence.
5. Complete every scientific and review field. Publish the reviewed rule, then explicitly publish relevant company profile setting before expecting it to apply.

The UI generates rule UUID/version data. For company override, Code must match selected system series; full rule remains separate company scope and version history. A matching profile name or rule publication is not adoption.

Published rules appear in a table; use each row's **Details** disclosure to inspect full scientific fields, exact effective times and review/evidence provenance. Revise only the row marked **Latest version** in its scope. **Superseded** rows remain visible and read-only; use the latest row for a new revision.

### Field meanings



| Field | How to fill it |
| --- | --- |
| Substance | Exact Catalog substance measured by the selected laboratory result. Rules are substance-specific. |
| Commodity | Exact Catalog commodity. Interpretation matches source lot commodity; processing matches output lot commodity. Do not assume every commodity or stage shares a rule. |
| Residue definition | Reviewed meaning of the analyte: for example, parent substance only versus a specified sum including metabolites. Must match reviewed evidence and compatible process rules exactly. |
| Material stage | Interpretation only: source material stage, such as `RAW_PADDY`. Choose from the supported stage dropdown; UI uses this as both input and output stage. |
| Analytical basis | Interpretation only: reviewed material/moisture basis of reported concentration. UI uses this as both input and output basis. Not a unit or stage. |
| Input / output stage | Processing only: choose physical stages before and after this transformation from the supported stage dropdowns. |
| Input / output analytical basis | Processing only: input must match parent interval basis; output becomes resulting interval basis and must match the next rule's input basis. |
| Concentration unit | Scientific profiles use `mg/kg`. Supported report units `µg/kg`, `μg/kg` and `ug/kg` normalize exactly by `0.001`; values, LOD, LOQ and reviewed concentration-valued selection fields scale together. Bare `ppb` remains unresolved without explicit mass-fraction semantics. |
| Relative uncertainty percent | Only for `NUMERIC_RELATIVE_UNCERTAINTY`; exact nonnegative decimal percentage, not fraction (`10` = 10%). |
| Reviewed relative uncertainty at numeric zero | Explicit opt-in required to use relative uncertainty at zero; otherwise numeric zero remains unresolved under relative policy. |
| Analytical method | Interpretation only: exact text recorded in laboratory result. No automatic method-name equivalence. |
| Output fraction / product identity | Processing only: exact output lot product identity, not its display name. Product identity can differ from stage, especially after bagging. |
| Process | Exact physical kind, such as `BLENDING`, not `BLEND`. Process stays free text with suggestions; mixed-storage kinds may need typing. |
| Conditions or UNCONDITIONAL applicability | Exact matching process-condition value, or `UNCONDITIONAL` only when review explicitly covers condition-independent applicability. Conditions stay free text with suggestions. This is not a free-form review note. |
| Factor lower / upper bound (decimal) | Nonnegative concentration-factor interval with lower ≤ upper. Enter plain decimals such as `0.5`, not `50%`, `0,5`, `±0.5` or scientific notation. Factor is dimensionless; result concentration remains `mg/kg`. |
| Factor semantics | **Per-step concentration factor** for one transformation; **Cumulative concentration factor** for a supported longer path anchored at its stated input stage. |
| Reviewed homogeneous mixing applicability | Check only when reviewed model authorizes homogeneous mass-weighted mixing. Required for multi-input processing, including relevant pool ingress. Not an automatic property of every process. |

No automatic conversion between moisture bases or residue definitions exists. Matching identifiers and text does not itself establish scientific validity.

### Review fields required for every rule

| Field | Meaning |
| --- | --- |
| Effective from | Start of approved applicability. Interpretation uses source received time; processing uses physical operation time or custody event time. |
| Effective until (optional) | End of applicability, exclusive. Must follow start. Blank means no stated end. |
| Evidence reference | Traceable policy, study or review record supporting this specific rule. A citation alone does not establish applicability. |
| Applicability statement | Scope, material, method/process, conditions and exclusions. |
| Scientific reviewer | Person responsible for completed review. Administrator login alone is not scientific approval. |
| Review authority | Responsible reviewing organization or role. |
| Reviewed at | Completed review timestamp, not a future time. |

Rules must also have been published by the evaluation snapshot. Publishing a rule does not rewrite an earlier saved evaluation.

## 3. Shared setup for the complete examples

Each example is complete when its example-specific table is combined with **all shared fields in this section**. Fields hidden for the chosen task are not filled.

Use a deliberately synthetic test substance and one existing test commodity. For the baseline, both sources and all output lots use that same selected commodity; real blending may use a different output commodity and therefore needs a rule selecting that actual output commodity.

| Shared field | Concrete test value |
| --- | --- |
| Substance | Select the synthetic test analyte used in both laboratory results. Keep the same persisted substance throughout. |
| Commodity | Select the existing test commodity used by this example's source/output lot. Keep the same persisted commodity in the baseline. |
| Residue definition | `SYNTHETIC_ANALYTE` |
| Concentration unit | `mg/kg` |
| Reviewed outward precision (decimal places) | `12` |
| Effective from | `2026-10-01T00:00` |
| Effective until (optional) | Leave blank |
| Scientific reviewer | `Synthetic reviewer — test identity only` |
| Review authority | `Isolated demonstration — no operational scientific approval` |
| Reviewed at | `2026-10-22T09:00` |

**Dates and identities above are fictional rehearsal metadata.** Use them only for synthetic records, with source/operation events on or after 2026-10-01 and publication after the stated review time. For a real review, enter actual reviewer, authority, evidence and timestamps; never backdate or fabricate approval.

Use `SYNTHETIC_AS_RECEIVED` as analytical basis and `SYNTHETIC_METHOD` as laboratory method for these examples. These strings are also used in existing isolated integration fixtures. They are not standardized production analytical definitions. This synthetic exercise explicitly retains the same basis through each transformation.

For every example, enter its full **Evidence reference** and **Applicability statement** below. Do not replace evidence with a real-looking invented publication.

## 4. Interpretation examples

Choose **Interpret laboratory results**. Every example in this section uses:

| Field | Value |
| --- | --- |
| Material stage | `RAW_PADDY` |
| Analytical basis | `SYNTHETIC_AS_RECEIVED` |
| Analytical method | `SYNTHETIC_METHOD` |

Also fill every shared field from section 3 and the two review text fields in the applicable example below.


### I1. Numeric result with reviewed absolute uncertainty

| Field | Value |
| --- | --- |
| Interpretation | **Numeric result with absolute uncertainty** |
| Evidence reference | `SYNTHETIC TEST ONLY — I1 numeric interpretation rehearsal` |
| Applicability statement | `Synthetic RAW_PADDY results for SYNTHETIC_ANALYTE, SYNTHETIC_METHOD and SYNTHETIC_AS_RECEIVED only. Use reviewed absolute uncertainty to form [max(0, value - uncertainty), value + uncertainty]. No operational scientific approval.` |

| Evidence field | Source A | Source B |
| --- | --- | --- |
| Result qualifier | Numeric | Numeric |
| Numeric value | `0.15` | `0.55` |
| Unit | `mg/kg` | `mg/kg` |
| Method | `SYNTHETIC_METHOD` | `SYNTHETIC_METHOD` |
| Original uncertainty text in synthetic report | `±0.05 mg/kg` | `±0.25 mg/kg` |
| Reviewed selection: absolute uncertainty | `0.05` | `0.25` |
| Reviewed selection: reporting threshold | Leave blank | Leave blank |
| Expected source interval | `[0.10, 0.20] mg/kg` | `[0.30, 0.80] mg/kg` |

Report review can bind multiple compatible results with one explicit confirmation while retaining separate immutable selections. When reported uncertainty needs normalization, record its reviewed absolute amount and supporting scientific review in the exception workflow; raw uncertainty text is never normalized automatically.

Absolute uncertainty is a concentration amount, **not a percentage**. Missing normalized uncertainty stays unresolved. The original laboratory text remains evidence; the application does not automatically parse it into an approved uncertainty convention. A synthetic value `0.03` with uncertainty `0.05` gives `[0, 0.08]`, not a negative lower endpoint.

### I1R. Numeric result with reviewed relative uncertainty

This is an alternative to I1, not an extra overlapping rule. Choose **Numeric result with relative uncertainty (%)**; enter a reviewed nonnegative exact percentage such as `10` for 10%, not `0.10`. Leave **Reviewed relative uncertainty at numeric zero** unchecked unless evidence explicitly approves that boundary.

Synthetic test only: value `0.15 mg/kg`, 10%, no reported uncertainty, and no normalized absolute uncertainty gives `[0.135, 0.165] mg/kg`. At numeric zero, same setting remains unresolved unless the explicit zero checkbox is reviewed true. A supplied reviewed normalized absolute uncertainty (for example `0.05`) is authoritative instead of relative calculation. If original lab uncertainty text is nonblank and no normalized absolute uncertainty was reviewed, result is unresolved; never parse raw text automatically. These values are synthetic arithmetic examples, not accepted defaults.

Remaining below-limit, ND and process examples retain synthetic-only status and existing applicability warnings.

### I2. Below analytical limit

| Field | Value |
| --- | --- |
| Interpretation | **Below analytical limit** |
| Evidence reference | `SYNTHETIC TEST ONLY — I2 below-limit interpretation rehearsal` |
| Applicability statement | `Synthetic compatible RAW_PADDY below-limit reports only. Interpret the explicitly reported positive threshold t conservatively as [0, t] mg/kg. No operational scientific approval.` |

Use a separate synthetic report/source for this example:

- Original result text: `<0.02 mg/kg`.
- Result qualifier: below limit; structured numeric threshold: `0.02`.
- Unit: `mg/kg`; method: `SYNTHETIC_METHOD`.
- Confirm report and publish reviewed selection with matching basis/definition; leave absolute uncertainty and reporting threshold blank.
- Expected interval: **`[0, 0.02] mg/kg`**.

In compact manual report entry, type `<0.02` in **Reported result**; the app preserves that text and derives its below-limit qualifier and exact threshold. Expert structured fields and API `numericValue` still require plain decimals without the comparison sign. This interpretation uses the below-limit result's numeric threshold, not an automatic choice of LOD or LOQ.

### I3. ND using an explicit reporting threshold

| Field | Value |
| --- | --- |
| Interpretation | **Not detected (ND)** |
| ND threshold policy | **Reporting threshold** |
| Evidence reference | `SYNTHETIC TEST ONLY — I3 ND reporting-threshold rehearsal` |
| Applicability statement | `Synthetic compatible RAW_PADDY ND reports only. Use the positive reporting threshold explicitly normalized in reviewed result selection; interval [0, threshold] mg/kg. No LOD/LOQ fallback or operational scientific approval.` |

Separate synthetic report: ND, unit `mg/kg`, method `SYNTHETIC_METHOD`; no numeric detected concentration. Confirm report. Publish reviewed selection with matching basis/definition and **reporting threshold `0.03`**; leave absolute uncertainty blank.

Expected interval: **`[0, 0.03] mg/kg`**. Missing selection threshold remains unresolved even if report includes an LOD or LOQ.

### I4. ND using LOD

| Field | Value |
| --- | --- |
| Interpretation | **Not detected (ND)** |
| ND threshold policy | **Limit of detection (LOD)** |
| Evidence reference | `SYNTHETIC TEST ONLY — I4 ND LOD rehearsal` |
| Applicability statement | `Synthetic compatible RAW_PADDY ND reports only. Reviewed policy uses the selected laboratory result's positive LOD as interval upper endpoint. No LOQ/reporting-threshold fallback or operational scientific approval.` |

Separate synthetic report: ND, **LOD `0.01`**, LOQ `0.02`, unit `mg/kg`, method `SYNTHETIC_METHOD`. Confirm report and publish reviewed selection with matching basis/definition; leave uncertainty and reporting threshold blank.

Expected interval: **`[0, 0.01] mg/kg`**. Missing LOD stays unresolved; available LOQ does not take its place.

### I5. ND using LOQ

| Field | Value |
| --- | --- |
| Interpretation | **Not detected (ND)** |
| ND threshold policy | **Limit of quantification (LOQ)** |
| Evidence reference | `SYNTHETIC TEST ONLY — I5 ND LOQ rehearsal` |
| Applicability statement | `Synthetic compatible RAW_PADDY ND reports only. Reviewed policy uses the selected laboratory result's positive LOQ as interval upper endpoint. No LOD/reporting-threshold fallback or operational scientific approval.` |

Separate synthetic report: ND, LOD `0.01`, **LOQ `0.02`**, unit `mg/kg`, method `SYNTHETIC_METHOD`. Confirm report and publish reviewed selection with matching basis/definition; leave uncertainty and reporting threshold blank.

Expected interval: **`[0, 0.02] mg/kg`**. ND is never automatically `[0,0]`.

**I3–I5 are alternative policies.** Separate source records alone do not distinguish which policy applies. Test one at a time in separate isolated fixtures, or use distinct actual applicability (for example a different synthetic method recorded consistently in both rule and report). Publishing all three with identical applicability creates conflicting interpretation policies.

## 5. Per-step processing examples

Choose **Define processing factors**. For every P1–P8 example, fill all shared fields from section 3 plus:

| Field | Value |
| --- | --- |
| Input analytical basis | `SYNTHETIC_AS_RECEIVED` |
| Output analytical basis | `SYNTHETIC_AS_RECEIVED` |
| Conditions or UNCONDITIONAL applicability | `UNCONDITIONAL` |
| Factor semantics | **Per-step concentration factor** |

`UNCONDITIONAL` here is an explicitly synthetic assumption: it does not approve real factory conditions. For operational conditional rules, enter the exact condition value carried by the physical processing record; explain the scientific scope in the separate applicability statement.

Each row completes a separate form. Checkbox **checked** means this synthetic example explicitly authorizes homogeneous mixing; unchecked means it does not authorize a multi-input variant.

| Example | Process | Input stage | Output stage | Output fraction / product identity | Lower | Upper | Reviewed homogeneous mixing applicability |
| --- | --- | --- | --- | --- | --- | --- | --- |
| P1 Drying | `DRYING` | `RAW_PADDY` | `DRIED_PADDY` | `DRIED_PADDY` | `1` | `1` | Unchecked; single input |
| P2 Head-rice milling | `MILLING` | `DRIED_PADDY` | `HEAD_RICE` | `HEAD_RICE` | `0.5` | `0.5` | Unchecked; single input |
| P3 Blending | `BLENDING` | `HEAD_RICE` | `BLENDED_RICE` | `BLENDED_RICE` | `1` | `1` | Checked |
| P4 Same-product merge | `MERGE` | `HEAD_RICE` | `HEAD_RICE` | `HEAD_RICE` | `1` | `1` | Checked |
| P5 Bagging head rice | `BAGGING` | `HEAD_RICE` | `BAGGED_RICE` | `HEAD_RICE` | `1` | `1` | Unchecked; single input |
| P6 Mixed-storage ingress | `CUSTODY_INGRESS` | `BLENDED_RICE` | `BLENDED_RICE` | `BLENDED_RICE` | `1` | `1` | Checked; incoming material plus existing pool |
| P7 Mixed-storage withdrawal | `CUSTODY_WITHDRAW` | `BLENDED_RICE` | `BLENDED_RICE` | `BLENDED_RICE` | `1` | `1` | Unchecked; single parent pool |
| P8 Milling factor interval, alternative to P2 | `MILLING` | `DRIED_PADDY` | `HEAD_RICE` | `HEAD_RICE` | `0.5` | `0.6` | Unchecked; single input |

Complete the review text for each form:

| Example | Evidence reference | Applicability statement |
| --- | --- | --- |
| P1 | `SYNTHETIC TEST ONLY — P1 drying` | `Single-input RAW_PADDY to DRIED_PADDY rehearsal. Concentration retained with explicit factor [1,1] on synthetic basis, independent of conditions for this test only. Not a production drying rule.` |
| P2 | `SYNTHETIC TEST ONLY — P2 head-rice milling` | `Single-input DRIED_PADDY to HEAD_RICE rehearsal. Apply step concentration factor [0.5,0.5] once; no extra yield adjustment. Synthetic basis and condition-independent test only.` |
| P3 | `SYNTHETIC TEST ONLY — P3 blending` | `Homogeneous HEAD_RICE inputs to BLENDED_RICE rehearsal, weighted by actual committed input masses. Factor [1,1]; synthetic basis and condition-independent test only. Does not replace blend compatibility approval.` |
| P4 | `SYNTHETIC TEST ONLY — P4 merge` | `Homogeneous HEAD_RICE inputs with matching commodity and product identity. Preserve stage and identity, combine by committed masses, factor [1,1]. Synthetic basis and condition-independent test only.` |
| P5 | `SYNTHETIC TEST ONLY — P5 bagging` | `Single-input HEAD_RICE bagging rehearsal. Output stage BAGGED_RICE retains HEAD_RICE product identity; factor [1,1]. Synthetic basis and condition-independent test only.` |
| P6 | `SYNTHETIC TEST ONLY — P6 custody ingress` | `Homogeneous BLENDED_RICE pool ingress rehearsal. Include incoming material and prior pool where present; factor [1,1]. Synthetic basis and condition-independent test only.` |
| P7 | `SYNTHETIC TEST ONLY — P7 custody withdrawal` | `Single-parent BLENDED_RICE pool split rehearsal. Aliquot and remainder retain parent concentration with explicit factor [1,1]. Synthetic basis and condition-independent test only.` |
| P8 | `SYNTHETIC TEST ONLY — P8 milling interval` | `Alternative single-input DRIED_PADDY to HEAD_RICE rehearsal using reviewed step-factor interval [0.5,0.6]. No midpoint substitution or extra yield adjustment. Synthetic basis and condition-independent test only.` |

### Expected calculations and applicability limits

- P1: source A `[0.10,0.20]` remains `[0.10,0.20]` after synthetic drying.
- P2: dried A `[0.10,0.20]` becomes `[0.05,0.10]` in head rice. Dried B `[0.30,0.80]` becomes `[0.15,0.40]`.
- P3: 60 kg head rice A plus 40 kg head rice B gives **`[0.09,0.22] mg/kg`**. These are committed input masses, not present remaining balances.
- P4: merging the same compatible head-rice inputs at the same masses gives the same interval, but retains `HEAD_RICE` stage/identity. Merge is not permission to blend incompatible products.
- P5: bagging A head rice retains `[0.05,0.10]`. **Output fraction is `HEAD_RICE`, not `BAGGED_RICE`.** To bag the P3 blend instead, use input stage `BLENDED_RICE`, output stage `BAGGED_RICE`, output identity `BLENDED_RICE`, and that actual output commodity.
- P6/P7: an empty pool receiving the baseline blend, followed by withdrawal, preserves `[0.09,0.22]` for pool, aliquot and remainder. Ingress into a nonempty pool also depends on that pool's existing evidence and masses; it need not retain the incoming blend interval.
- P8: dried A `[0.10,0.20]` becomes `[0.05,0.12]`. Bounds use lower × lower factor and upper × upper factor, not average factor `0.55`.

P8 replaces P2 for its separate exercise; publishing both as unrelated overlapping rules leaves conflicting process rules. Use **Revise rule** for an intended replacement with reviewed applicability.

P1/P2/P5 describe single-input operations. If your actual operation has multiple inputs, a compatible rule must explicitly authorize homogeneous mixing; do not check the box merely to clear an unresolved result.

Milling `HEAD_RICE` rules do not cover `BROKEN_RICE`, `BRAN` or `HUSK`. Those outputs require their own applicable fraction-specific rules and reviewed coefficients. No default factor is supplied.

Factor `1` is an explicitly entered synthetic concentration assumption, not an implicit default. No physical output mass, water loss or yield alone authorizes pesticide removal. Existing stock, balance, HOLD, facility and compatibility checks still apply.

## 6. Cumulative processing example

### P9. Paddy-to-head-rice cumulative milling factor

Test this separately from P2/P8, or explicitly supersede the applicable milling rule through reviewed revision. Otherwise both step and cumulative milling rules may match and conflict.

Physical path: one source A → single-input drying → single-input milling. Keep positive input masses and an unbranched compatible path for this exercise. Source A already resolves to `[0.10,0.20]` under I1.

Choose **Define processing factors**, fill every shared field from section 3, then:

| Field | Value |
| --- | --- |
| Input stage | `RAW_PADDY` |
| Output stage | `HEAD_RICE` |
| Input analytical basis | `SYNTHETIC_AS_RECEIVED` |
| Output analytical basis | `SYNTHETIC_AS_RECEIVED` |
| Output fraction / product identity | `HEAD_RICE` |
| Process | `MILLING` |
| Conditions or UNCONDITIONAL applicability | `UNCONDITIONAL` |
| Factor lower bound (decimal) | `0.5` |
| Factor upper bound (decimal) | `0.5` |
| Factor semantics | **Cumulative concentration factor** |
| Reviewed homogeneous mixing applicability | Unchecked; single input and unbranched anchor path |
| Evidence reference | `SYNTHETIC TEST ONLY — P9 cumulative paddy-to-head-rice` |
| Applicability statement | `Single-source RAW_PADDY through single-input drying to HEAD_RICE at milling. Factor [0.5,0.5] describes total concentration change from original paddy anchor, not an additional multiplier on dried concentration. Compatible synthetic basis and condition-independent test only; no branching or mixed historical mass-basis rewind.` |

Expected head-rice interval: **`[0.05,0.10] mg/kg`**, calculated from original paddy anchor.

For a separate demonstration, a synthetic drying rule with factor `[1.2,1.2]` would yield dried A `[0.12,0.24]`; P9 still yields `[0.05,0.10]`, not `[0.06,0.12]`. Its cumulative factor already describes the complete paddy-to-head-rice change. If demonstrating this, revise P1 explicitly in a separate fixture rather than publishing another overlapping drying rule.

Cumulative milling can use a compatible source anchor across a single-parent drying node even when that intermediate drying interval is unresolved because its factor is absent. That does **not** make a standalone evaluation of the dried lot complete: dried-lot evaluation still needs its own compatible rule.

Cumulative factors cannot generally rewind a branching mixture or changed historical mass basis. Such unsupported ancestry remains `UNRESOLVED`; do not substitute current masses for original weights.

## 7. Publish, revise and calculate

1. Check selected substance/commodity, exact text matches, effective dates, coefficients and review evidence.
2. Publish reviewed rule. This does not confirm reports, publish selections, adopt profiles or calculate bounds.
3. As Customer Manager, open **Residue profiles**. For each system Code used by calculation, publish a reviewed `SYSTEM_DEFAULT` setting. For a company override, publish company-scoped full rule with same Code first, then publish `COMPANY_OVERRIDE`. To return to inheritance, publish a new `SYSTEM_DEFAULT` setting; do not delete history.
4. Complete report confirmation and reviewed selection. Choose an adopted profile if supported by selector; selection pre-fills reviewed basis/definition/reference only. Keep reviewer/date explicit; no selection bypasses ambiguity or auto-selects lower result.
5. Use **Preview server assessment**; server resolves policy and evidence. A finite preview requires empty reasons and non-null endpoints; unresolved stays unresolved. Client calculations are not authoritative.
6. As authorized lot user, open **Ledger → Stock → lot detail**, select substance, then **Calculate bounds** or **Recalculate bounds**. Inspect interval, reasons, provenance, rule/policy setting references and dependencies.
7. Compare historical snapshots. Fresh evidence affects only new evaluations.

System rule revision preserves the system series Code and links same-scope predecessor. Company override revision uses same Code within company scope and supersedes only a company version, not system rule. Settings have their own version history and supersede same-company/same-Code setting. Do not treat identity-compatible override as automatic adoption.

On concurrent publication conflict, draft remains and history refreshes. Explicitly revise latest eligible version or setting and review again; application does not silently rebase or retry your scientific decision.

## 8. Troubleshooting and completion checks

| Symptom | Check / expected behavior |
| --- | --- |
| Cannot publish | Required unit, precision, applicability and review fields; ordered nonnegative factor endpoints; completed review time; existing substance and commodity. |
| Missing interpretation | Exact method, result unit, source stage/commodity, selection basis/definition, effective source time, confirmed result and reviewed selection. |
| Missing uncertainty/threshold | Numeric needs reviewed absolute uncertainty; below-limit needs positive numeric threshold; ND needs value selected by its explicit threshold policy. |
| Missing process match | Actual process kind, output stage/commodity/product identity, input basis/stage, unit/definition, conditions and effective process time. |
| Conflicting interpretation/process rules | Overlapping unsuperseded policies. Separate alternative exercises, or publish explicit reviewed revision; never choose lowest coefficient/result. |
| Multi-input calculation unresolved | Every positive parent needs usable evidence and compatible rules, including reviewed homogeneous mixing. Do not drop an unknown parent. |
| More than one conflicting confirmed result | Reviewed result selection/supersession must explicitly resolve evidence; latest report does not automatically win. |
| Missing process rule | `UNRESOLVED`, never implicit factor `1`. |
| Untested substance without approved fallback | `UNRESOLVED`, never zero or MRL used as concentration. |
| Earlier evaluation unchanged | Expected immutable history. Recalculate to create a new snapshot. |

For the baseline manual flow, B pending means **`UNRESOLVED`**. After B confirmation and reviewed selection, I1 + P1/P2/P3 with 60/40 kg committed inputs should yield **`[0.090000000000, 0.220000000000] mg/kg`**. `COMPLETE` means finite interval under referenced reviewed inputs, **not legal PASS**, HOLD release or export approval.

## 9. Implementation references

These examples reflect the current task-based UI and contracts, not proposed scientific defaults:

- [Rule entry UI](../apps/web/src/ResidueRuleMaintenance.tsx): task-specific fields, generated versions, review and publication.
- [Catalog rule contract and validation](../backend/src/GrainLedger.Modules.Catalogs/ResidueRules.cs): required fields, semantics, precision, review dates and publication authorization.
- [Evidence interpretation](../backend/src/GrainLedger.Modules.Ledger/ResidueEvidenceResolver.cs): exact matching, uncertainty and ND thresholds.
- [Residue calculator](../backend/src/GrainLedger.Modules.Ledger/ResidueCalculator.cs): process matching, mass weighting and cumulative anchors.
- [Physical output contracts](../backend/src/GrainLedger.Modules.Ledger/Sql/Operations.sql): stage and product identity, including bagging and merge.
- [Synthetic fixture inputs](../backend/tests/GrainLedger.Api.IntegrationTests/ResidueScenario.cs) and [qualifier/evaluation regressions](../backend/tests/GrainLedger.Api.IntegrationTests/ResidueEvaluationTests.cs): existing example basis, method, values and supported cases. These references are not a claim that tests were rerun for this documentation change.
