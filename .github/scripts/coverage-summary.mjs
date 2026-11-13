import { appendFileSync, readFileSync } from 'node:fs';

// Istanbul/Vitest JSON summary; report aggregate numbers only, not PR-controlled filenames.
const total = JSON.parse(readFileSync(process.argv[2], 'utf8')).total;
const rows = ['lines', 'branches', 'functions', 'statements'].map(metric => {
  const value = total?.[metric];
  if (!value || !Number.isInteger(value.total) || !Number.isInteger(value.covered) ||
      value.covered < 0 || value.total < value.covered) {
    throw new Error(`Invalid coverage metric: ${metric}`);
  }
  const percent = value.total === 0 ? 'N/A' : `${(100 * value.covered / value.total).toFixed(2)}%`;
  return `| ${metric} | ${percent} | ${value.covered}/${value.total} |`;
});
const markdown = `### Web coverage\n\nTest outcome: ${process.env.COVERAGE_TEST_OUTCOME === 'success' ? 'passed' : 'not passed (coverage may be partial)'}\n\n` +
  `| Metric | Coverage | Covered/total |\n| --- | ---: | ---: |\n${rows.join('\n')}\n\n` +
  'Download **web-coverage**, extract it and open **index.html** for file/line details. No coverage threshold is enforced.\n';
if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, markdown);
else console.log(markdown);
