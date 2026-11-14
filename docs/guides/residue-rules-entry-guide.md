# Whole-flow residue profiles — field guide

A profile covers one substance and commodity across laboratory interpretation and configured processing paths. All concentrations are `mg/kg` **as received** at each stage. No moisture-basis conversion is performed. A profile is reviewed scientific policy, not a legal assessment or a laboratory result.

**Safety:** Values below are synthetic calculation examples, not scientific approvals, realistic yields or legal limits. Use an isolated environment with matching migrations and restricted runtime credentials. Never publish synthetic values as operational policy or write to shared Supabase without explicit database-owner approval. See [setup and deployment](../setup-and-deployment.md).

## Create and review a profile

1. As Administrator, open residue profile maintenance and choose **Create profile**. For an own-company alternative, use **Clone as company profile** on an eligible system profile. To revise a series, choose **Revise profile**; review the complete new version, not only changed rows.
2. Select the exact substance and commodity, enter the reviewed residue definition, and confirm the as-received convention. Enter the effective dates, evidence, applicability, reviewer, authority and review time. Set reviewed outward precision in the advanced profile settings. No scientific value is supplied automatically.
3. In **Laboratory results**, add each exact analytical method. Enable only the result forms actually reviewed: numeric absolute or relative uncertainty, below-limit, or ND with its explicit reporting threshold, LOD or LOQ choice. A method cannot be duplicated. Relative percentage `10` means 10%; numeric zero requires explicit relative-at-zero approval. Nonblank reported uncertainty still requires separately reviewed normalized absolute uncertainty.
4. In **Processing steps**, add each supported operation and physical material path, including separate milling fractions. Enter the reviewed lower factor and, when using a range, upper factor. Select explicitly unconditional applicability or exact reviewed condition text. Review cumulative semantics and its ancestor anchor, homogeneous mixing and precision where applicable. A cumulative step's input remains its immediate physical parent; its anchor controls calculation rewind. No implicit factor or assumed mixing is allowed.
5. Before publishing, review the always-visible **Live profile preview** against the source evidence. It reflects current editor entries: identity, laboratory methods, processing factors and paths, applicability, effective period and review. Blank values appear as **Not specified**. Return to the relevant fields to correct entries and review again. This is a configuration preview, not a residue calculation, scientific approval or compliance assessment.
6. Confirm scientific review and publish. Laboratory and step arrays cannot both be empty. A profile may have no steps for source-only coverage, or no laboratory entries for reviewed source-group fallback. Missing components do not inherit from another profile. Invalid or overlapping paths and stale revisions are rejected; retain and review the draft after a conflict.

After publication, open **Details** to review the published configuration, publication metadata and any retirement information. Compare the full profile with the approved evidence before adoption.

Profile Code identifies the whole series. Revisions preserve step IDs for unchanged logical rows; new paths get new IDs. Effective windows are start-inclusive and end-exclusive. One complete company override replaces the same-Code system profile for its effective window; missing override components do not fall back to system policy.

## Retire a profile series

In profile maintenance, an authorized Administrator can retire a system series and its Customer Manager can retire an own-company series. On the latest profile, choose **Retire profile**, enter a reason (up to 1,000 characters), and confirm retirement of the entire series. Retirement takes effect immediately; the server records the authenticated actor and time. Open **Details** to inspect the reason, actor and time, or filter by **Retired**. Published versions and existing snapshots remain available as history.

New evaluations cannot use a retired series, including older versions; retirement does not restore an earlier version. Review and publish a new profile series and any necessary company adoption rather than relying on fallback. Retirement does not delete or alter historical evaluations.

## Adopt and bind evidence

As Customer Manager, open **Residue profiles**, inspect the full profile, then publish a reviewed `SYSTEM_DEFAULT` setting for its Code and effective period. For an approved same-Code company profile, publish `COMPANY_OVERRIDE`; publish a new `SYSTEM_DEFAULT` setting to return to inheritance. Profile publication alone is not adoption. Search, scope/status filters, sorting and expandable history help find profile series and reviewed decisions.

For a laboratory report, check identity and transcription against the original. Check **Concentrations are as received (required for residue bounds)** only when supported by evidence. Identity confirmation still succeeds without this attestation, but automatic residue binding remains unresolved. After adoption or normalization review, confirmed reports can be explicitly reviewed and rebound with the as-received attestation. Exact method, result form, commodity and an unambiguous adopted profile must match. Do not infer a dry-weight conversion, uncertainty or missing ND limit from narrative text.

For reviewed source-group fallback, explicitly identify every member and select one adopted profile Code with as-received confirmation. The system records each member's effective profile and setting at receipt; shared farmer or plot alone does not establish representativeness.

## Synthetic example

For the [manual test](residue-bounds-manual-test-guide.md), create **one** profile with a synthetic substance and commodity, definition `SYNTHETIC_ANALYTE`, precision 12, laboratory method `SYNTHETIC_METHOD`, numeric absolute uncertainty and reviewed supported below-limit/ND forms. Configure all applicable rows in that same profile:

| Operation | Physical path | Factor |
| --- | --- | --- |
| Drying | `RAW_PADDY → DRIED_PADDY` | 1 |
| Milling, head-rice fraction | `DRIED_PADDY → HEAD_RICE` | 0.5 |
| Blending | `HEAD_RICE → BLENDED_RICE` | 1 |
| Mixed-storage ingress | `BLENDED_RICE → BLENDED_RICE` | 1 |
| Mixed-storage withdrawal | `BLENDED_RICE → BLENDED_RICE` | 1 |

Choose explicit `UNCONDITIONAL` conditions only after review; attest homogeneous mixing for applicable mixing/custody steps. Use reviewed windows covering receipts and operations, then adopt this one Code. The example's two source intervals `[0.10, 0.20]` at 60 kg and `[0.30, 0.80]` at 40 kg produce `[0.09, 0.22] mg/kg` after the listed factors. These values are test inputs only.

## Cutover and history

The schema-2 cutover does not convert old per-stage rules, settings, selections or groups. Those immutable records remain stored but are not exposed or consumed by active residue readers. Create new profile Codes, adoption and evidence with fresh review; do not supersede legacy records or infer their material convention. Original laboratory reports remain business evidence and can be rebound after explicit review. Old-engine evaluation snapshots remain stored but are absent from new history; their detail is unavailable and their IDs cannot be replayed by the new engine. New snapshots remain immutable when later evidence or profiles change.

Export market policy is separate from the profile: select and review `AS_RECEIVED` as its `residueBasis`. Unsupported or absent market conventions remain incomplete; no automatic conversion occurs. Live residue preview does not append history; **Lock snapshot for audit** does. Neither action changes stock, HOLDs or legal limits.
