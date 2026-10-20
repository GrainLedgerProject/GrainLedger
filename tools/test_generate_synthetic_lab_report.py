import csv
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).with_name('generate_synthetic_lab_report.py')
HEADERS = ['testLabel', 'substanceId', 'originalResultText', 'resultKind', 'numericValue', 'unit', 'lod', 'loq', 'method', 'uncertainty', 'reference']


class GeneratorTests(unittest.TestCase):
    def run_cli(self, out, *extra):
        return subprocess.run([sys.executable, str(SCRIPT), '--sample-code', 'SAMPLE<&"1', '--seal-code', 'SEAL-1', '--lab-sample-reference', 'LAB-1', '--report-number', 'REPORT-1', '--out-dir', str(out), *extra], text=True, capture_output=True)

    def test_report_and_csv_share_synthetic_rows(self):
        with tempfile.TemporaryDirectory() as temp:
            out = Path(temp) / 'report'
            result = self.run_cli(out, '--rows', '5')
            self.assertEqual(result.returncode, 0, result.stderr)
            csv_text = (out / 'results.csv').read_text(encoding='utf-8')
            with (out / 'results.csv').open(newline='', encoding='utf-8') as file:
                reader = csv.DictReader(file)
                self.assertEqual(reader.fieldnames, HEADERS)
                rows = list(reader)
            self.assertIn('"See note, ""retain original"""', csv_text)
            self.assertEqual(len(rows), 5)
            self.assertEqual([r['resultKind'] for r in rows], ['NUMERIC', 'BELOW_LIMIT', 'NOT_DETECTED', 'QUALITATIVE', 'OTHER'])
            self.assertEqual(rows[0]['numericValue'], '000.0100')
            self.assertEqual(rows[1]['numericValue'], '0.020')
            self.assertTrue(all(not r['substanceId'] and r['testLabel'].startswith('Synthetic') for r in rows))
            self.assertEqual(rows[-1]['originalResultText'], 'See note, "retain original"')
            page = (out / 'report.html').read_text(encoding='utf-8')
            self.assertIn('SYNTHETIC TEST DATA ONLY', page)
            self.assertIn('SAMPLE&lt;&amp;&quot;1', page)
            self.assertNotIn('SAMPLE<&"1', page)
            for row in rows:
                self.assertIn(row['testLabel'], page)
                self.assertIn(row['originalResultText'].replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;').replace('"', '&quot;'), page)
            self.assertIn('&quot;retain original&quot;', page)
            self.assertIn('REPORT-1', page)
            self.assertIn('SEAL-1', page)
            self.assertIn('LAB-1', page)


    def test_default_output_is_next_to_copied_script_not_working_directory(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            tools = root / 'tools'
            tools.mkdir()
            copied_script = tools / SCRIPT.name
            copied_script.write_bytes(SCRIPT.read_bytes())
            cwd = root / 'working-directory'
            cwd.mkdir()
            output = tools / 'mock_lab_reports'
            command = [sys.executable, str(copied_script), '--sample-code', 'SAMPLE-1']

            result = subprocess.run(command, cwd=cwd, text=True, capture_output=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertTrue((output / 'report.html').is_file())
            self.assertTrue((output / 'results.csv').is_file())
            self.assertFalse((cwd / 'mock_lab_reports').exists())
            self.assertFalse((cwd / 'report.html').exists())
            self.assertFalse((cwd / 'results.csv').exists())

            original_report = (output / 'report.html').read_text(encoding='utf-8')
            original_csv = (output / 'results.csv').read_text(encoding='utf-8')
            retry = subprocess.run(command, cwd=cwd, text=True, capture_output=True)
            self.assertNotEqual(retry.returncode, 0)
            self.assertEqual((output / 'report.html').read_text(encoding='utf-8'), original_report)
            self.assertEqual((output / 'results.csv').read_text(encoding='utf-8'), original_csv)

    def test_bounds_blank_sample_and_nonoverwrite_preserve_existing_file(self):
        with tempfile.TemporaryDirectory() as temp:
            blank_out = Path(temp) / 'blank'
            blank = subprocess.run([sys.executable, str(SCRIPT), '--sample-code', '  ', '--out-dir', str(blank_out)], text=True, capture_output=True)
            self.assertNotEqual(blank.returncode, 0)
            self.assertFalse(blank_out.exists())
            for count in ('0', '10001'):
                out = Path(temp) / f'bad-{count}'
                result = self.run_cli(out, '--rows', count)
                self.assertNotEqual(result.returncode, 0)
                self.assertFalse(out.exists())
            out = Path(temp) / 'existing'
            out.mkdir()
            marker = out / 'evidence.txt'
            marker.write_text('keep', encoding='utf-8')
            result = self.run_cli(out)
            self.assertNotEqual(result.returncode, 0)
            self.assertEqual(marker.read_text(encoding='utf-8'), 'keep')
            self.assertFalse((out / 'report.html').exists())

    def test_accepts_maximum_row_count(self):
        with tempfile.TemporaryDirectory() as temp:
            out = Path(temp) / 'large'
            result = self.run_cli(out, '--rows', '10000')
            self.assertEqual(result.returncode, 0, result.stderr)
            with (out / 'results.csv').open(newline='', encoding='utf-8') as file:
                self.assertEqual(sum(1 for _ in csv.DictReader(file)), 10000)


if __name__ == '__main__':
    unittest.main()
