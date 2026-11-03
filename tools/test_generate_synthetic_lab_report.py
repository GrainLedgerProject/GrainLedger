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

    def test_realistic_default_generates_14_substances_and_overwrites_in_place(self):
        with tempfile.TemporaryDirectory() as temp:
            out = Path(temp) / 'report'
            first = self.run_cli(out)
            self.assertEqual(first.returncode, 0, first.stderr)
            with (out / 'results.csv').open(newline='', encoding='utf-8') as file:
                reader = csv.DictReader(file)
                self.assertEqual(reader.fieldnames, HEADERS)
                rows = list(reader)
            self.assertEqual(len(rows), 14)
            self.assertEqual(rows[0]['substanceId'], 'TCZ001')
            self.assertIn('Tricyclazole', rows[0]['testLabel'])
            self.assertEqual(rows[5]['substanceId'], 'CPF006')
            self.assertEqual(rows[5]['numericValue'], '0.042')

            # Re-running with a different scenario overwrites report.html and results.csv cleanly
            second = self.run_cli(out, '--scenario', 'vn-exceed', '--report-number', 'COA-OVERRIDE-2')
            self.assertEqual(second.returncode, 0, second.stderr)
            with (out / 'results.csv').open(newline='', encoding='utf-8') as file:
                rows_after = list(csv.DictReader(file))
            self.assertEqual(rows_after[5]['numericValue'], '0.680')
            page = (out / 'report.html').read_text(encoding='utf-8')
            self.assertIn('SYNTHETIC TEST DATA ONLY', page)
            self.assertIn('COA-OVERRIDE-2', page)
            self.assertIn('SAMPLE&lt;&amp;&quot;1', page)
            self.assertNotIn('SAMPLE<&"1', page)

    def test_synthetic_scenario_and_csv_escaping(self):
        with tempfile.TemporaryDirectory() as temp:
            out = Path(temp) / 'report'
            result = self.run_cli(out, '--scenario', 'synthetic', '--rows', '5')
            self.assertEqual(result.returncode, 0, result.stderr)
            csv_text = (out / 'results.csv').read_text(encoding='utf-8')
            with (out / 'results.csv').open(newline='', encoding='utf-8') as file:
                rows = list(csv.DictReader(file))
            self.assertIn('"See note, ""retain original"""', csv_text)
            self.assertEqual(len(rows), 5)
            self.assertEqual([r['resultKind'] for r in rows], ['NUMERIC', 'BELOW_LIMIT', 'NOT_DETECTED', 'QUALITATIVE', 'OTHER'])

    def test_default_output_is_next_to_copied_script_and_overwrites_by_default(self):
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

            # Overwrites by default
            rerun = subprocess.run([*command, '--report-number', 'COA-NEW'], cwd=cwd, text=True, capture_output=True)
            self.assertEqual(rerun.returncode, 0, rerun.stderr)
            self.assertIn('COA-NEW', (output / 'report.html').read_text(encoding='utf-8'))

            # --no-overwrite refuses when directory already exists
            refused = subprocess.run([*command, '--no-overwrite'], cwd=cwd, text=True, capture_output=True)
            self.assertNotEqual(refused.returncode, 0)

    def test_bounds_blank_sample_and_row_limits(self):
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
    def test_accepts_maximum_row_count(self):
        with tempfile.TemporaryDirectory() as temp:
            out = Path(temp) / 'large'
            result = self.run_cli(out, '--rows', '10000')
            self.assertEqual(result.returncode, 0, result.stderr)
            with (out / 'results.csv').open(newline='', encoding='utf-8') as file:
                self.assertEqual(sum(1 for _ in csv.DictReader(file)), 10000)


if __name__ == '__main__':
    unittest.main()
