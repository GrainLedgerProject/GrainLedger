#!/usr/bin/env python3
"""Generate realistic synthetic ISO/IEC 17025 lab report HTML and CSV for GrainLedger testing."""
import argparse
import csv
from datetime import date
import html
import io
from pathlib import Path
import sys

HEADERS = ('testLabel', 'substanceId', 'originalResultText', 'resultKind', 'numericValue', 'unit', 'lod', 'loq', 'method', 'uncertainty', 'reference')
KINDS = ('NUMERIC', 'BELOW_LIMIT', 'NOT_DETECTED', 'QUALITATIVE', 'OTHER')

# 14 canonical Mekong Delta rice active substances from GL-A-4 (pesticide_substances-v1.1.csv)
REALISTIC_PANEL = (
    ('TCZ001', 'Tricyclazole (CAS 41814-78-2)', 'LC-MS/MS (AOAC 2007.01 / EN 15662)', '0.002', '0.005'),
    ('IPT002', 'Isoprothiolane (CAS 50512-35-1)', 'GC-MS/MS (AOAC 2007.01 / EN 15662)', '0.003', '0.010'),
    ('HXC003', 'Hexaconazole (CAS 79983-71-4)', 'LC-MS/MS (AOAC 2007.01 / EN 15662)', '0.002', '0.005'),
    ('PPC004', 'Propiconazole (CAS 60207-90-1)', 'LC-MS/MS (AOAC 2007.01 / EN 15662)', '0.002', '0.005'),
    ('CBD005', 'Carbendazim (sum as carbendazim, CAS 10605-21-7)', 'LC-MS/MS (AOAC 2007.01 / EN 15662)', '0.003', '0.010'),
    ('CPF006', 'Chlorpyrifos-ethyl (CAS 2921-88-2)', 'GC-MS/MS (AOAC 2007.01 / EN 15662)', '0.002', '0.005'),
    ('IMD007', 'Imidacloprid (CAS 138261-41-3)', 'LC-MS/MS (AOAC 2007.01 / EN 15662)', '0.002', '0.005'),
    ('ABA008', 'Abamectin (Avermectin B1a+B1b, CAS 71751-41-2)', 'LC-MS/MS (AOAC 2007.01 / EN 15662)', '0.001', '0.002'),
    ('BUP009', 'Buprofezin (CAS 69327-76-0)', 'GC-MS/MS (AOAC 2007.01 / EN 15662)', '0.002', '0.005'),
    ('LCH010', 'Lambda-cyhalothrin (incl. Cyhalothrin, CAS 91465-08-6)', 'GC-MS/MS (AOAC 2007.01 / EN 15662)', '0.002', '0.005'),
    ('THX011', 'Thiamethoxam (CAS 153719-23-4)', 'LC-MS/MS (AOAC 2007.01 / EN 15662)', '0.002', '0.005'),
    ('FBN012', 'Fenobucarb / BPMC (CAS 3766-81-2)', 'LC-MS/MS (AOAC 2007.01 / EN 15662)', '0.002', '0.005'),
    ('PCZ013', 'Prochloraz (sum incl. BTS 44595/44596, CAS 67747-09-5)', 'LC-MS/MS (AOAC 2007.01 / EN 15662)', '0.003', '0.010'),
    ('TBZ014', 'Tebuconazole (CAS 107534-96-3)', 'LC-MS/MS (AOAC 2007.01 / EN 15662)', '0.002', '0.005'),
)

SCENARIO_VALUES = {
    'compliant': {
        'TCZ001': ('NOT_DETECTED', 'ND (< LOD)', '', ''),
        'IPT002': ('BELOW_LIMIT', '<0.010 mg/kg', '0.010', ''),
        'HXC003': ('NOT_DETECTED', 'ND (< LOD)', '', ''),
        'PPC004': ('NOT_DETECTED', 'ND (< LOD)', '', ''),
        'CBD005': ('BELOW_LIMIT', '<0.010 mg/kg', '0.010', ''),
        'CPF006': ('NUMERIC', '0.042 mg/kg', '0.042', '±0.008'),
        'IMD007': ('NOT_DETECTED', 'ND (< LOD)', '', ''),
        'ABA008': ('NUMERIC', '0.015 mg/kg', '0.015', '±0.003'),
        'BUP009': ('NOT_DETECTED', 'ND (< LOD)', '', ''),
        'LCH010': ('NUMERIC', '0.085 mg/kg', '0.085', '±0.017'),
        'THX011': ('NOT_DETECTED', 'ND (< LOD)', '', ''),
        'FBN012': ('NOT_DETECTED', 'ND (< LOD)', '', ''),
        'PCZ013': ('BELOW_LIMIT', '<0.010 mg/kg', '0.010', ''),
        'TBZ014': ('NUMERIC', '0.120 mg/kg', '0.120', '±0.024'),
    },
    'vn-pass-export-fail': {
        'TCZ001': ('NUMERIC', '0.045 mg/kg', '0.045', '±0.009'),
        'IPT002': ('NUMERIC', '1.800 mg/kg', '1.800', '±0.360'),
        'HXC003': ('NOT_DETECTED', 'ND (< LOD)', '', ''),
        'PPC004': ('NUMERIC', '0.060 mg/kg', '0.060', '±0.012'),
        'CBD005': ('NUMERIC', '0.420 mg/kg', '0.420', '±0.084'),
        'CPF006': ('NUMERIC', '0.180 mg/kg', '0.180', '±0.036'),
        'IMD007': ('NOT_DETECTED', 'ND (< LOD)', '', ''),
        'ABA008': ('NUMERIC', '0.040 mg/kg', '0.040', '±0.008'),
        'BUP009': ('NUMERIC', '0.110 mg/kg', '0.110', '±0.022'),
        'LCH010': ('NUMERIC', '0.310 mg/kg', '0.310', '±0.062'),
        'THX011': ('NOT_DETECTED', 'ND (< LOD)', '', ''),
        'FBN012': ('NOT_DETECTED', 'ND (< LOD)', '', ''),
        'PCZ013': ('BELOW_LIMIT', '<0.010 mg/kg', '0.010', ''),
        'TBZ014': ('NUMERIC', '0.850 mg/kg', '0.850', '±0.170'),
    },
    'vn-exceed': {
        'TCZ001': ('NUMERIC', '0.120 mg/kg', '0.120', '±0.024'),
        'IPT002': ('NUMERIC', '2.100 mg/kg', '2.100', '±0.420'),
        'HXC003': ('NOT_DETECTED', 'ND (< LOD)', '', ''),
        'PPC004': ('NOT_DETECTED', 'ND (< LOD)', '', ''),
        'CBD005': ('NUMERIC', '2.450 mg/kg', '2.450', '±0.490'),
        'CPF006': ('NUMERIC', '0.680 mg/kg', '0.680', '±0.136'),
        'IMD007': ('NOT_DETECTED', 'ND (< LOD)', '', ''),
        'ABA008': ('NUMERIC', '0.150 mg/kg', '0.150', '±0.030'),
        'BUP009': ('NOT_DETECTED', 'ND (< LOD)', '', ''),
        'LCH010': ('NUMERIC', '1.250 mg/kg', '1.250', '±0.250'),
        'THX011': ('NOT_DETECTED', 'ND (< LOD)', '', ''),
        'FBN012': ('NOT_DETECTED', 'ND (< LOD)', '', ''),
        'PCZ013': ('BELOW_LIMIT', '<0.010 mg/kg', '0.010', ''),
        'TBZ014': ('NUMERIC', '1.850 mg/kg', '1.850', '±0.370'),
    },
}


def realistic_row(index, scenario='compliant'):
    code, label, method, lod, loq = REALISTIC_PANEL[index % len(REALISTIC_PANEL)]
    kind, text, numeric, uncertainty = SCENARIO_VALUES[scenario][code]
    suffix = '' if index < len(REALISTIC_PANEL) else f' (replicate {index // len(REALISTIC_PANEL) + 1})'
    return {
        'testLabel': label + suffix,
        'substanceId': code if index < len(REALISTIC_PANEL) else '',
        'originalResultText': text,
        'resultKind': kind,
        'numericValue': numeric,
        'unit': 'mg/kg',
        'lod': lod,
        'loq': loq,
        'method': method,
        'uncertainty': uncertainty,
        'reference': f'Section 2, Table 1, Item {index + 1}',
    }


def synthetic_row(index):
    kind = KINDS[index % len(KINDS)]
    numeric = ('000.0100' if kind == 'NUMERIC' else '0.020') if kind in ('NUMERIC', 'BELOW_LIMIT') else ''
    text = {'NUMERIC': '0.0100 mg/kg', 'BELOW_LIMIT': '<0.020 mg/kg', 'NOT_DETECTED': 'ND', 'QUALITATIVE': 'Detected', 'OTHER': 'See note, "retain original"'}[kind]
    return {'testLabel': f'Synthetic test {index + 1}', 'substanceId': '', 'originalResultText': text, 'resultKind': kind, 'numericValue': numeric, 'unit': 'mg/kg', 'lod': '0.0010', 'loq': '0.0050', 'method': 'Synthetic method', 'uncertainty': '', 'reference': f'Synthetic row {index + 1}'}


def render_html(args, issued, report_number, rows):
    esc = lambda value: html.escape(str(value), quote=True)
    lab_name = args.laboratory_name
    lab_ref = args.lab_sample_reference or f'VLS-{issued.replace("-", "")}-01'
    seal_code = args.seal_code or 'SEAL-UNSPECIFIED'
    metadata = (
        ('Testing Laboratory', lab_name),
        ('Accreditation', 'ISO/IEC 17025:2017 — VILAS 004 (Synthetic Simulation)'),
        ('Certificate / Report No.', report_number),
        ('Report Kind', 'FINAL'),
        ('Issue Date', issued),
        ('Sample Matrix', 'Raw Paddy / Lúa tươi (Oryza sativa L.)'),
        ('Client Sample Code', args.sample_code),
        ('Seal Code & Condition', f'{seal_code} (Intact on receipt)'),
        ('Laboratory Sample Reference', lab_ref),
        ('Analytical Standard', 'AOAC 2007.01 / EN 15662 (QuEChERS) — LC-MS/MS & GC-MS/MS'),
    )
    details = ''.join(f'<div class="meta-item"><dt>{esc(label)}</dt><dd>{esc(value)}</dd></div>' for label, value in metadata)
    body = ''.join(
        '<tr>'
        + f'<td>{idx + 1}</td>'
        + ''.join(f'<td>{esc(row[key])}</td>' for key in ('testLabel', 'substanceId', 'originalResultText', 'resultKind', 'numericValue', 'unit', 'lod', 'loq', 'method', 'uncertainty', 'reference'))
        + '</tr>'
        for idx, row in enumerate(rows)
    )
    return f'''<!doctype html>
<html lang="en"><head><meta charset="utf-8"><title>Certificate of Analysis {esc(report_number)}</title>
<style>
body {{ font: 13px/1.45 "Segoe UI", Arial, sans-serif; color: #1b2420; margin: 2rem auto; max-width: 1100px; padding: 0 1rem; }}
header {{ display: flex; justify-content: space-between; align-items: flex-start; border-bottom: 2px solid #1b4332; padding-bottom: .75rem; margin-bottom: 1rem; }}
h1 {{ font-size: 20px; margin: 0 0 .25rem; color: #1b4332; text-transform: uppercase; letter-spacing: .03em; }}
.subtitle {{ margin: 0; color: #4a5c55; font-size: 12px; }}
.warning {{ background: #fff5f5; border: 2px solid #9b1c1c; color: #7f1d1d; padding: .65rem .9rem; font-weight: 700; margin-bottom: 1rem; font-size: 12px; }}
dl.meta-grid {{ display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: .5rem 1.5rem; background: #f8faf9; border: 1px solid #cbd5e1; padding: .85rem 1rem; margin: 0 0 1.25rem; }}
.meta-item {{ display: grid; grid-template-columns: 190px 1fr; gap: .4rem; }}
dt {{ font-weight: 600; color: #334155; }} dd {{ margin: 0; color: #0f172a; font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 12px; }}
table {{ border-collapse: collapse; width: 100%; font-size: 11px; margin-bottom: 1.25rem; }}
th, td {{ border: 1px solid #94a3b8; padding: .4rem .45rem; text-align: left; overflow-wrap: anywhere; }}
th {{ background: #e2e8f0; color: #0f172a; font-weight: 700; }}
tbody tr:nth-child(even) {{ background: #f8fafc; }}
thead {{ display: table-header-group; }} tr {{ break-inside: avoid; }}
.notes {{ font-size: 11px; color: #334155; border-top: 1px solid #cbd5e1; padding-top: .75rem; display: flex; justify-content: space-between; gap: 2rem; }}
.sig {{ text-align: center; min-width: 220px; }}
@media print {{ body {{ margin: 10mm; max-width: none; padding: 0; }} .warning {{ color: #900; }} }}
</style></head><body>
<header>
  <div>
    <h1>Certificate of Analysis / Phiếu Kết Quả Thử Nghiệm</h1>
    <p class="subtitle">{esc(lab_name)} · Multi-Residue Pesticide Panel in Rice &amp; Cereal Grains</p>
  </div>
  <div style="text-align:right;font-size:12px;">
    <strong>Report No.:</strong> {esc(report_number)}<br>
    <strong>Issued:</strong> {esc(issued)}
  </div>
</header>
<p class="warning">SYNTHETIC TEST DATA ONLY — NOT OFFICIAL LABORATORY EVIDENCE. No compliance assessment.</p>
<dl class="meta-grid">{details}</dl>
<table><thead><tr><th>#</th><th>Test parameter (CAS)</th><th>Catalog code</th><th>Reported result</th><th>Qualifier</th><th>Numeric</th><th>Unit</th><th>LOD</th><th>LOQ</th><th>Analytical method</th><th>Uncertainty (k=2)</th><th>Section ref</th></tr></thead><tbody>{body}</tbody></table>
<div class="notes">
  <div>
    <strong>Notes:</strong> Results apply only to the sample as received. <code>ND (&lt; LOD)</code>: Not detected below Limit of Detection. <code>&lt; LOQ</code>: Trace detected below Limit of Quantification. Expanded uncertainty U is reported at 95% confidence (k=2). Reference comparison against Thông tư 50/2016/TT-BYT or export MRL tables is performed separately in GrainLedger.
  </div>
  <div class="sig">
    <strong>Authorized Signatory</strong><br><br><br>
    <em>(Synthetic QA/QC Reviewer)</em>
  </div>
</div>
</body></html>'''


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--sample-code', required=True)
    parser.add_argument('--seal-code')
    parser.add_argument('--lab-sample-reference')
    parser.add_argument('--report-number')
    parser.add_argument('--laboratory-name', default='QUATEST 3 — Food & Agricultural Testing Lab (Synthetic)')
    parser.add_argument('--scenario', choices=('compliant', 'vn-pass-export-fail', 'vn-exceed', 'synthetic'), default='compliant')
    parser.add_argument('--rows', type=int, default=14)
    parser.add_argument('--no-overwrite', action='store_true', help='fail if output directory already exists')
    default_out_dir = Path(__file__).resolve().parent / 'mock_lab_reports'
    parser.add_argument('--out-dir', type=Path, default=default_out_dir)
    args = parser.parse_args(argv)
    if not args.sample_code.strip():
        parser.error('--sample-code must not be blank')
    if not 1 <= args.rows <= 10000:
        parser.error('--rows must be between 1 and 10000')
    issued = date.today().isoformat()
    report_number = args.report_number or f'COA-{issued.replace("-", "")}-001'
    rows = [
        synthetic_row(index) if args.scenario == 'synthetic' else realistic_row(index, args.scenario)
        for index in range(args.rows)
    ]
    try:
        args.out_dir.mkdir(parents=True, exist_ok=not args.no_overwrite)
        (args.out_dir / 'report.html').write_text(render_html(args, issued, report_number, rows), encoding='utf-8')
        output = io.StringIO(newline='')
        writer = csv.DictWriter(output, fieldnames=HEADERS, lineterminator='\n')
        writer.writeheader()
        writer.writerows(rows)
        (args.out_dir / 'results.csv').write_text(output.getvalue(), encoding='utf-8', newline='')
    except OSError as error:
        parser.error(f'cannot create output: {error}')
    print(f'HTML: {args.out_dir / "report.html"}')
    print(f'CSV: {args.out_dir / "results.csv"}')
    print('For PDF: open report.html in a browser, then Print → Save as PDF.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
