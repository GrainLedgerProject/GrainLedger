# F14 whole-kilogram planning

Updated 27 November 2026 after the user required quantities that can be weighed in whole kg. This rule applies to proposed input allocations; Ledger measurements, mass accounting, laboratory values and QC retain their existing precision.

## Rule

Each selected lot/location contributes an integer number of kilograms, with minimum 1 kg. Unselected positions contribute zero and are omitted. This does not imply 50 kg bags or any packaging-unit model.

SCIP receives integer decision variables `kg_i` with `0 <= kg_i <= floor(availableKg_i)`. Product-presence constraints require at least 1 kg of each product declared by the selected compatibility. Quantity is not optimized as a continuous number and rounded for display afterward.

The returned solution is checked for integrality, exact summed mass, stock capacity and exact decimal row constraints, then assessed through the existing F12/F14 checks. Solver integrality noise within 0.000001 kg may normalize to the integer the model already selected; no post-solve redistribution is performed. A fractional-only feasible LP has no feasible whole-kg plan.

The existing fallback also uses floored stock and integer anchors. Saved selections sent through candidate-group validation are rejected with `QUANTITY_WHOLE_KG_REQUIRED` when any allocation is fractional. A fractional direct-stock target cannot be covered exactly by integer allocations; the direct solver reports `WHOLE_KG_TARGET_REQUIRED`.

For blending, the input requirement is the least integer kg that covers target/yield and keeps predicted output representable at Ledger's 6-decimal precision. For example, target 1000 kg at yield 0.9 requires 1112 integer kg input, predicting 1000.8 kg output. Predicted output and actual measured output are distinct; this change does not round actual Ledger facts. Fractional stock remainders are retained in inventory but cannot be selected as fractional allocations.

The two-lot adjustment range now has integer endpoints and 1 kg steps. Every adjusted ratio still requires server recheck. Planning-context and calculated-preview responses expose `quantityStepKg: "1"`.

## Implementation

- `BlendMilpSolver.cs`: integer variables, floored capacities, exact validation; removed micro-kg repair redistribution.
- `AssessedMilpSearch.cs`: 1 kg product minimum, integer ratio ranges and capacity lower bounds, rational whole-kg input calculation for yield.
- `GreedyStockSelection.cs`: same rule for fallback.
- `CandidateGroupValidator.cs`: fractional plan allocations rejected before scientific assessment.
- `BlendPlanDraft.cs` / `BlendPlanPreviewEndpoints.cs`: response step metadata.
- `BlendPlanning.tsx`: explains the 1 kg input step and fractional-allocation validation result.

No database migration or change to Ledger precision was required. Existing preview history was not rewritten; calculate a new preview to apply the rule, and do not use a saved fractional preview as a new proposal.

## Verification

- Focused solver/API/selection tests: 64/64 PASS.
- Expanded F12/F14/F15/container regression: 112/112 PASS, no skips, against disposable PostgreSQL Testcontainers.
- FE planning/order tests: 28/28 PASS; production web build PASS (existing chunk-size warning).
- New tests cover all six objectives, fractional remnants, a model whose only continuous solution is 0.5/0.5 kg, fractional direct target, integer fallback anchors, yield/output precision and API rejection of fractional input before F12.
- Existing BL-01–BL-15 and proposal/ratio rechecks run on the same general model. Retain-head optimum at 5% broken becomes 959 kg head + 41 kg broken; at 4% it becomes 970 + 30 kg.
- Actual local API on port 5080 reports `quantityStepKg="1"` through read-only planning contexts. The user's demo orders were not recalculated during verification.
- Four local location labels dropped the suffix `(challenge)`. Location UUIDs, stock and source identities were unchanged. The term remains in scenario/market names as a test-set label, not an inventory type or special algorithm path.

Local DB remains `127.0.0.1:5433/grainledger`. Shared DB, runtime grants and immutable stock history were not changed. Remote CI has not been run for this change.
