#!/usr/bin/env python3
"""Generate synthetic lab evidence for GrainLedger import testing."""
import argparse
import csv
from datetime import date
import html
import io
from pathlib import Path
import sys

HEADERS = ('testLabel', 'substanceId', 'originalResultText', 'resultKind', 'numericValue', 'unit', 'lod', 'loq', 'method', 'uncertainty', 'reference')
KINDS = ('NUMERIC', 'BELOW_LIMIT', 'NOT_DETECTED', 'QUALITATIVE', 'OTHER')


def row_for(index):
    kind = KINDS[index % len(KINDS)]
    numeric = ('000.0100' if kind == 'NUMERIC' else '0.020') if kind in ('NUMERIC', 'BELOW_LIMIT') else ''
    text = {'NUMERIC': '0.0100 mg/kg', 'BELOW_LIMIT': '<0.020 mg/kg', 'NOT_DETECTED': 'ND', 'QUALITATIVE': 'Detected', 'OTHER': 'See note, "retain original"'}[kind]
    return {'testLabel': f'Synthetic test {index + 1}', 'substanceId': '', 'originalResultText': text, 'resultKind': kind, 'numericValue': numeric, 'unit': 'mg/kg', 'lod': '0.0010', 'loq': '0.0050', 'method': 'Synthetic method', 'uncertainty': '', 'reference': f'Synthetic row {index + 1}'}


def render_html(args, issued, report_number, rows):
    esc = lambda value: html.escape(str(value), quote=True)
    metadata = (('Laboratory', 'Synthetic Laboratory'), ('Report number', report_number), ('Report kind', 'FINAL'), ('Issue date', issued), ('Sample code', args.sample_code), ('Seal code', args.seal_code or ''), ('Laboratory sample reference', args.lab_sample_reference or ''))
    details = ''.join(f'<dt>{esc(label)}</dt><dd>{esc(value)}</dd>' for label, value in metadata)
    body = ''.join('<tr>' + ''.join(f'<td>{esc(row[key])}</td>' for key in ('testLabel', 'originalResultText', 'resultKind', 'numericValue', 'unit', 'lod', 'loq', 'method', 'uncertainty', 'reference')) + '</tr>' for row in rows)
    return f'''<!doctype html>
<html lang="en"><head><meta charset="utf-8"><title>Synthetic lab report {esc(report_number)}</title>
<style>
body {{ font: 14px Arial, sans-serif; color: #111; margin: 2rem; }}
h1 {{ margin-bottom: .25rem; }} .warning {{ border: 2px solid #9b1c1c; padding: .8rem; font-weight: bold; }}
dt {{ font-weight: bold; }} dd {{ margin: 0 0 .5rem; }} dl {{ display: grid; grid-template-columns: max-content 1fr; gap: .2rem 1rem; }}
table {{ border-collapse: collapse; width: 100%; font-size: 11px; }} th, td {{ border: 1px solid #777; padding: .35rem; text-align: left; overflow-wrap: anywhere; }}
thead {{ display: table-header-group; }} tr {{ break-inside: avoid; }}
@media print {{ body {{ margin: 12mm; }} .warning {{ color: #900; }} }}
</style></head><body>
<h1>Synthetic laboratory report</h1><p class="warning">SYNTHETIC TEST DATA ONLY — NOT OFFICIAL LABORATORY EVIDENCE. No compliance assessment.</p>
<dl>{details}</dl><table><thead><tr><th>Test label</th><th>Original result</th><th>Kind</th><th>Numeric value</th><th>Unit</th><th>LOD</th><th>LOQ</th><th>Method</th><th>Uncertainty</th><th>Reference</th></tr></thead><tbody>{body}</tbody></table>
</body></html>'''


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--sample-code', required=True)
    parser.add_argument('--seal-code')
    parser.add_argument('--lab-sample-reference')
    parser.add_argument('--report-number')
    parser.add_argument('--rows', type=int, default=5)
    default_out_dir = Path(__file__).resolve().parent / 'mock_lab_reports'
    parser.add_argument('--out-dir', type=Path, default=default_out_dir)
    args = parser.parse_args(argv)
    if not args.sample_code.strip():
        parser.error('--sample-code must not be blank')
    if not 1 <= args.rows <= 10000:
        parser.error('--rows must be between 1 and 10000')
    issued = date.today().isoformat()
    report_number = args.report_number or f'SYN-{issued.replace("-", "")}'
    rows = [row_for(index) for index in range(args.rows)]
    try:
        args.out_dir.mkdir(parents=True, exist_ok=False)
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
