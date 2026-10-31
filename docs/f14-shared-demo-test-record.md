# F14 shared demo database test record

Date: 2026-10-31. Scope: `GrainLedger Demo Customer` and `GrainLedger Demo Mill (synthetic)` only. All Lab, QC, scientific rules and review labels below are fictional functional test data. A `PASS` in this scenario does not establish EU regulatory compliance.

## Existing records used

- Export order `EXP-261027-000004`, EU/RICE, container target 200 kg; container ID `ffbe577b-9713-4189-bcf9-9672d06cf984`; blend draft ID `582410cc-56b6-451d-92cb-7c6831363bc7`.
- Finished lot `HDR-20261027-000029`, lot ID `67252fbd-ac38-4268-bb3e-a7974c7982e9`, 200 kg available at Demo Silo 2.
- Source samples `SMP-261023-0002` and `SMP-261023-0001` map to the two RAW lots in this HDR lot's physical ancestry.

## Demo data added through role protected APIs

- Ledger evidence file: `GrainLedger_Demo_Evidence_Pack.pdf`, evidence ID `d5204605-a105-47b7-9b8d-836083eb8ba0`.
- Confirmed finished-lot moisture observation: 13.4%, observation ID `e2bb882a-b0c6-4035-9e40-540c4854221a`, referenced to the demo evidence file. The buyer's maximum is 14%.
- Confirmed synthetic Lab report `b405845c-0614-4497-8832-9114334a72a9`: both existing samples have fictional Isoprothiolane (`IPT002`) results `ND <0.01 mg/kg`. Two company-scoped selections connect these results to their original source lots on material basis `AS_RECEIVED`.
- Company-scoped F12 rules and company policy settings: ND interpreted with LOQ, factor 1.0 for RAW custody ingress and withdrawal, DRIED custody withdrawal, drying, and milling. Each record is marked `SYNTHETIC FUNCTIONAL TEST ONLY` and references the demo rule pack or custody supplement.
- Published demo assessment policy `b7a50485-0fca-4ba4-89fe-7e33339edbb1` for EU/RICE, version 1, requires **only IPT002**, basis `AS_RECEIVED`, 10% MRL safety margin, and mass-weighted moisture. This limited substance scope was selected to verify the F14 software path with the supplied fictional LOQ; it does not cover the full 14-substance MRL table.

## Verified result

- F12 residue preview of the HDR lot for IPT002: `COMPLETE`, interval 0–0.01 mg/kg, basis `AS_RECEIVED`.
- F14 preview: `assessmentStatus=PASS`, predicted output 200 kg. The selected allocation is 200 kg of `HDR-20261027-000029` from Demo Silo 2.
- Saved proposal ID `0016a1bc-fdad-4a04-97e8-da0d5e38ac0d`: read-back `freshness=CURRENT`, `assessmentStatus=PASS`, `stockReserved=false`.
- The candidate scope revision stayed `f5c7acbb49e0bf24e4bfa1e88d3bfeebfe852d06a48773ac882e67b94e5cd7f9` before and after saving; HDR available quantity remained 200 kg. F14 did not reserve or deduct stock.

## Frontend check

Sign in as the demo Export Staff, open **Export orders** → `EXP-261027-000004` → its 200 kg container → **Blend plans** → **Plan v1**. Refresh the plan page. The saved proposal should show `PASS`, 200 kg selected from `HDR-20261027-000029`, the moisture and IPT002 checks, and no stock reservation. Continue with approval/reservation only when testing the later export flow.
