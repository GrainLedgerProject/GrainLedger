# Synthetic laboratory report generator guide

Use this tool to test GrainLedger's manual laboratory-report workflow without waiting for a real laboratory report. It generates a printable HTML report and matching result-row CSV from the same synthetic findings.

**Test data only.** Generated files are not official laboratory evidence. They contain no legal limits or compliance decision. The generator never connects to GrainLedger or writes to a database; uploading and confirming its output through the application does create records. Use a disposable test environment or an explicitly approved development database. Do not upload synthetic reports against real business lots or shared Supabase without approval.

## 1. Prepare a registered sample

Requirements: Python 3, a browser, and access to the repository. No additional Python packages are needed. Run commands from the repository root.

In the Purchasing Staff account:

1. Open **Paddy intake → receipt → source-lot code → Take sample**.
2. Record material sampled, physical seal code, and collection time. Save.
3. Copy the readable **sample code** and recorded **seal code** from sample details. The generator needs sample code, not source-lot code or sample UUID.
4. To exercise custody history, choose **Send to laboratory** and record only actual dispatch details. **Record laboratory receipt** separately only when actual receipt is known. Generic **+ Record handover** remains for other custody transfers.

A sample can be collected before mill arrival. Sample collection and report intake do not create or deduct stock.

For generator-only preview, an invented sample code works. For successful application confirmation, use a registered sample whose printed identifiers match the report.

## 2. Generate report and CSV

Replace sample/seal values below with the values copied from your test sample:

```bash
python3 tools/generate_synthetic_lab_report.py \
  --sample-code SMP-261020-0001 \
  --seal-code SEAL-001 \
  --lab-sample-reference LAB-S001 \
  --report-number SYN-MANUAL-001 \
  --out-dir tools/mock_lab_reports/report-001
```

`--out-dir` is optional; omitting it writes directly to `tools/mock_lab_reports` beside the script. Repeat runs overwrite `report.html` and `results.csv`. Use `--no-overwrite` to reject an existing output directory, or choose a fresh subdirectory to retain previous files.

Outputs:

- `report.html`: printable report with sample identity, metadata and results.
- `results.csv`: matching rows in GrainLedger's documented import format.

### Options

| Option | Required/default | Meaning |
| --- | --- | --- |
| `--sample-code` | Required; nonblank | Sample code printed in report. Use registered sample code for a matching-identity test. |
| `--seal-code` | Optional | Printed physical seal identifier. Omit if testing a report with no seal identifier. |
| `--lab-sample-reference` | Optional | Printed laboratory-assigned sample reference; not a GrainLedger UUID. |
| `--report-number` | Optional; `COA-YYYYMMDD-001` | Printed report identifier. Supply distinct identifiers when testing several reports on the same day. |
| `--rows` | Optional; `14` | Number of findings, from `1` to `10000`. |
| `--out-dir` | Optional; `tools/mock_lab_reports` beside script | Output directory; generated HTML/CSV are overwritten on repeat runs. |
| `--no-overwrite` | Optional | Reject an existing output directory. |
| `--laboratory-name` | Optional; `QUATEST 3 — Food & Agricultural Testing Lab (Synthetic)` | Synthetic laboratory label printed in report. Not proof of laboratory issuance or accreditation. |
| `--scenario` | Optional; `compliant` | Fictional measurements: `compliant`, `vn-pass-export-fail`, `vn-exceed`, or `synthetic`. Scenario names are not legal verdicts. |

Report kind is **FINAL**; issue date is the generator machine's current date. Copy actual metadata printed in the generated report.

Default panel uses 14 reference-substance codes, CAS labels, realistic-looking methods, LOD/LOQ, and uncertainty. CSV `substanceId` contains catalog codes resolved to database IDs at intake. `--scenario synthetic` exercises `NUMERIC`, `BELOW_LIMIT`, `NOT_DETECTED`, `QUALITATIVE`, and `OTHER` with unmapped synthetic labels. Decimal strings, qualifiers, and quoted text are preserved.

For a large panel:

```bash
python3 tools/generate_synthetic_lab_report.py \
  --sample-code SMP-261020-0001 \
  --seal-code SEAL-001 \
  --report-number SYN-PANEL-001 \
  --rows 300 \
  --out-dir tools/mock_lab_reports/panel-001
```

## 3. Print report to PDF

1. Open generated `report.html` in your browser.
2. Use **Print → Save as PDF**.
3. Choose landscape orientation if needed to keep the result table readable. Inspect print preview before saving.
4. Keep the visible **SYNTHETIC TEST DATA ONLY** warning. Save PDF beside generated files for convenience.

The CLI creates HTML and CSV only; PDF is produced by the browser. The HTML report remains useful for reviewing values before upload.

## 4. Register report as Customer Manager

1. Sign in as **Customer Manager** for sample's company.
2. Open **Samples**, choose sample, then **+ Receive report**. Sample is preselected; verify it against report and custody history.
3. Copy report metadata from HTML/PDF:
   - **Laboratory:** laboratory name printed in generated report.
   - **Report number:** generated identifier, such as `SYN-MANUAL-001`.
   - **Issue date:** date printed in report.
   - **Received at:** actual receipt time, distinct from collection and issue dates.
   - **Report kind:** `FINAL`.
4. Leave **Related report** unlinked for this independent original. For linked reports, select earlier report by its readable report number/lab/date/kind choice; do not enter a UUID or printed report number as an ID. Linked report requires a relationship reason.
5. Upload PDF as original report file. Review entire file before checking company-only declaration. Do not include another company's information.
6. In sample-specific sections, verify preselected registered sample. Copy printed sample code, seal code, and laboratory sample reference exactly; do not invent missing identifiers.
7. Enter a section reference such as `Results table`. Generated rows carry their own section/table/item references.
8. Choose CSV import before the results table and select `results.csv`.
9. Preview rows. Check row count, exact numeric strings, original result text and qualifiers. Resolve validation errors before continuing.
10. Add previewed rows, then save report evidence.

One CSV import belongs to the selected report section only. This generator represents one sample per report; it does not generate composite samples or automatically create application records.

## 5. Review and confirm

1. Open saved report and download original if needed.
2. Click **Review and confirm**.
3. Check sample identity against the registered sample and custody history; check every transcribed result against original file.
4. Select verified sample. Compare printed and registered identifiers; review suggested identity basis. **SAMPLE_CODE** works when printed sample code matches and any printed seal code also agrees. Leading/trailing whitespace is ignored; real contradictions require corrected evidence, not a different basis.
5. Check identity and transcription attestations, then click **Confirm checked record**.
6. Reopen the sample or original procurement source lot to review linked confirmed results.

`LAB_REFERENCE` is not an arbitrary alternative: it requires matching persisted dispatch/acknowledgement laboratory name, sample reference and supporting evidence reference. Merely passing `--lab-sample-reference` does not create that evidence.

Confirmation means identity/transcription were checked, not PASS or legal compliance. Server may provide a VN numeric baseline comparison where published limits and mapped results exist; otherwise assessment remains **NOT_ASSESSED**. Browser never substitutes hardcoded limits. Stock and HOLD state remain unchanged. Rejected confirmations show errors beside the submit action inside the dialog; correct rejected inputs before retrying. An uncertain outcome retains the exact original request for replay.

## Relationship reason: when to use it

**Relationship reason explains why a new report links to an earlier report.** It is not a sample identifier, result, compliance override or HOLD-release reason.

| Situation | Report kind | Example reason |
| --- | --- | --- |
| Independent original report | `FINAL` or `PARTIAL` | Leave related ID and reason blank. |
| Laboratory corrects earlier report | `AMENDMENT` | `Laboratory corrected the unit printed for row 4.` |
| Laboratory supplies additional findings | `SUPPLEMENTARY` | `Remaining panel results supplement the earlier partial report.` |
| Laboratory performs a new test | `RETEST` | `Separate retest requested after conflicting measurements.` |
| Final report explicitly linked to earlier partial report | `FINAL` | `Final report completes the earlier partial report.` |

Every linked report requires an existing same-company related report and nonblank reason. Select the earlier report using the readable **Related report** chooser; never type a raw UUID or confuse printed report number with a GrainLedger ID. Original report, rows and confirmation remain unchanged.

This generator always prints `FINAL` and does not generate amendment/supplement/retest metadata. Do not relabel its independent output as one of those report kinds unless your separate synthetic report document actually supports that scenario. Generating another file does not create an application relationship automatically.

## Troubleshooting

- **Output directory already exists:** repeat runs overwrite generated files by default; remove `--no-overwrite` only if replacing those files is intended, or choose a fresh directory.
- **Sample code rejected by CLI:** provide a nonblank value.
- **Row count rejected:** use an integer from `1` to `10000`.
- **Report stays unresolved:** compare printed sample/seal identifiers with the selected sample. Wrong or missing identity must not be guessed. Preserve the original; use verified evidence or a linked correction as applicable.
- **CSV preview reports errors:** select generated `results.csv`, not HTML/PDF. Spreadsheet edits can change decimal text or CSV structure; regenerate if unsure.
- **No matching sample in selector:** confirm company account, load more samples, and search by sample/seal code. Search does not change an existing selected sample.
- **Large report exceeds upload limit:** original file must be at most 10 MiB; total intake request is capped at 20 MiB. Use a smaller panel or smaller PDF without removing required evidence.

## Generator regression check

```bash
python3 -m unittest discover -s tools -p test_generate_synthetic_lab_report.py
```

See [sample and laboratory API](../api/sample-api.md#laboratory-report-routes) for exact authorization, identity and CSV contracts, and [laboratory workflow policy boundaries](../plans/lab-flow-plan.md) for assessment/HOLD exclusions.
