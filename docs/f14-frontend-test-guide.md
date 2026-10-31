# Test F14 blend planning in the web app

The F14 UI is in the working web app under **Export orders**. These steps require an authenticated Export Staff account and an API connected to a database with the reviewed F14 owner migrations and runtime grants. The configured shared database received the F14 migrations and grants on 27 October 2026. Use an isolated test database for write scenarios; no data-changing smoke scenario was run against the shared database during deployment.

## Required test data

- One F13 export order with market, commodity, buyer quality specification and a planned container target.
- One facility with finished owner stock. For a blend test, use two compatible input lots and a published Ledger compatibility and yield rule.
- Published market MRL and company assessment policy, complete F12 residue evidence, and confirmed finished-lot quality observations for required attributes. A missing item should produce `INCOMPLETE` instead of PASS.

Customer Manager can now prepare the company assessment policy and confirmed finished-lot quality in **Export assessment**. Ledger compatibility and yield-rule setup remains a separate Mill Manager task. Use genuine reviewed values and evidence; the UI does not invent a passing result.

## Customer Manager setup in the web app

1. Sign in as `customer.manager@grainledger.local` and open **Export assessment** from the sidebar.
2. Under **Market assessment policy**, choose the same market and commodity as the order. Enter the reviewed version, effective period, required active substances, MRL margin fraction, residue basis, quality calculation rules and review reference. Confirm the review and publish. An existing applicable version is shown when available.
3. Under **Finished-lot quality evidence**, choose the mill facility and search for the finished Ledger lot code (for example, `HDR-20261027-000029`). Choose the required quality attribute, enter the measured value and time, upload the supporting Ledger evidence, enter its report reference and review details, then confirm. The page shows the current confirmed version and evidence link. Repeat for each required attribute.
4. Return to the Export Staff plan and select **Calculate plan** again. Other missing residue, MRL or process evidence may still leave the assessment `INCOMPLETE`.

## Browser path

1. Sign in as Export Staff. Open **Export orders**, an order, then one container. The **Stock and blend plans** section appears below the target mass. If storage is unavailable, the section explains the missing deployment.
2. Choose **Product to ship** and **Create draft**. For a `RICE` order the selector offers only Head rice, Broken rice and Blended rice. The plan page shows the pinned target, product, market, commodity, shipping window and buyer specification. A commodity without an F14 product mapping shows a message instead of the draft action.
3. Choose **Facility**. Inspect eligible stock. Select **Show excluded stock** to see HOLDs or incomplete lineage with reasons. Select **Sources** for one position to inspect its Capture source composition and processing/custody graph. Page forward and back; changing facility resets candidates and preview.
4. Select **Calculate plan**. Inspect selected input kg, predicted output kg, assessment and findings. `INCOMPLETE` and `FAIL` cannot be proposed. `SEARCH_EXHAUSTED` means only that the bounded search found no PASS within its budget.
5. With a PASS preview, select **Save proposal** within ten minutes. Reopen the plan to see the immutable assessment and `CURRENT` freshness. F14 does not reserve or deduct stock.
6. Change a test stock position, add a HOLD, publish new relevant evidence/policy or edit the F13 demand in the isolated database. Refresh the proposal. It should show `STALE` and reasons while retaining the saved assessment. Create a new draft from the container for a new attempt.
7. Verify a Customer Manager can read drafts and proposals but cannot create a draft, calculate or propose. Check the layout at desktop and narrow mobile widths.

## Failure recovery

- If a create/cancel/propose response is lost, the page shows **Retry same request**. Reload the page and retry. The same operation ID and request body must be sent; the server should return the stored result without creating a duplicate.
- If candidates change between pages or before preview, reload candidates from page one. If the preview expires or proposal reports stale inputs, calculate a new preview before proposing.
- A failed proposal freshness read must not be displayed as `CURRENT`.

## Automated checks

From the repository root:

```powershell
npm exec --workspace=@grainledger/web vitest -- run src/BlendPlanning.test.tsx src/ExportOrders.test.tsx
npm exec --workspace=@grainledger/web vitest -- run src/ExportAssessment.test.tsx
npm run build --workspace=@grainledger/web
dotnet build backend/src/GrainLedger.Modules.Exports/GrainLedger.Modules.Exports.csproj --no-restore
```

The focused F13/F14 UI tests passed 14/14 and the F14 proposal integration test passed on isolated PostgreSQL. The full FE suite currently has 13 failures/timeouts in residue rule and company-policy tests outside the F14 files; investigate those before merging the branch. Browser acceptance with authenticated isolated data remains open.
