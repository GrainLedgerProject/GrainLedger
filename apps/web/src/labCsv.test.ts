import { expect, it } from 'vitest';
import { parseLabCsv } from './labCsv';

it('preserves quoted text and decimal strings exactly', () => {
  const result = parseLabCsv('testLabel,substanceId,originalResultText,resultKind,numericValue,method\n"Residue, total",,0.0100,NUMERIC,0.0100,"Method ""A"""');
  expect(result.errors).toEqual([]);
  expect(result.rows[0]).toMatchObject({ testLabel: 'Residue, total', originalResultText: '0.0100', numericValue: '0.0100', method: 'Method "A"' });
});

it('validates row labels, qualifiers, and exact decimal limits', () => {
  const result = parseLabCsv('testLabel,originalResultText,resultKind,numericValue\n,,PASS,1e-3\nX,<0.01,BELOW_LIMIT,0.01');
  expect(result.rows).toHaveLength(2);
  expect(result.errors).toEqual(expect.arrayContaining(['Row 2: testLabel is required.', 'Row 2: originalResultText is required.', 'Row 2: unsupported resultKind “PASS”.', 'Row 2: numericValue must be a nonnegative exact decimal string with at most 12 digits on each side.']));
  expect(result.rows[1].numericValue).toBe('0.01');
});

it('preserves commas inside tab-separated fields and validates column counts', () => {
  const result = parseLabCsv('testLabel\toriginalResultText\tresultKind\treference\nChlorpyrifos\tND\tNOT_DETECTED\tpage 2, row 1');
  expect(result.errors).toEqual([]);
  expect(result.rows[0].reference).toBe('page 2, row 1');
  const malformed = parseLabCsv('testLabel,originalResultText,resultKind\nChlorpyrifos,ND,NOT_DETECTED,unexpected');
  expect(malformed.errors).toEqual(['Row 2: expected 3 columns, found 4.']);
});
