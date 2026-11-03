export type LabCsvPreview = { rows: LabCsvRow[]; errors: string[] };
export type LabCsvRow = {
  testLabel: string;
  substanceId?: string;
  originalResultText: string;
  resultKind: 'NUMERIC' | 'BELOW_LIMIT' | 'NOT_DETECTED' | 'QUALITATIVE' | 'OTHER';
  numericValue?: string;
  unit?: string;
  lod?: string;
  loq?: string;
  method?: string;
  uncertainty?: string;
  reference?: string;
};

export const labCsvHeaders = ['testLabel', 'substanceId', 'originalResultText', 'resultKind', 'numericValue', 'unit', 'lod', 'loq', 'method', 'uncertainty', 'reference'] as const;

export function parseLabCsv(input: string): LabCsvPreview {
  let delimiter: ',' | '\t' = ',', headerCommas = 0, headerTabs = 0, inHeaderQuote = false;
  for (let i = 0; i < input.length && input[i] !== '\n' && input[i] !== '\r'; i++) {
    if (input[i] === '"' && input[i + 1] === '"' && inHeaderQuote) { i++; continue; }
    if (input[i] === '"') inHeaderQuote = !inHeaderQuote;
    else if (!inHeaderQuote && input[i] === ',') headerCommas++;
    else if (!inHeaderQuote && input[i] === '\t') headerTabs++;
  }
  if (headerTabs > headerCommas) delimiter = '\t';
  const records: string[][] = [];
  let row: string[] = [], cell = '', quoted = false;
  for (let i = 0; i < input.length; i++) {
    const char = input[i];
    if (quoted) {
      if (char === '"' && input[i + 1] === '"') { cell += '"'; i++; }
      else if (char === '"') quoted = false;
      else cell += char;
    } else if (char === '"' && cell.length === 0) quoted = true;
    else if (char === delimiter) { row.push(cell); cell = ''; }
    else if (char === '\n' || char === '\r') {
      if (char === '\r' && input[i + 1] === '\n') i++;
      row.push(cell); records.push(row); row = []; cell = '';
    } else cell += char;
  }
  if (quoted) return { rows: [], errors: ['Input has an unclosed quoted field.'] };
  if (cell.length || row.length) { row.push(cell); records.push(row); }
  const nonempty = records.filter(record => record.some(value => value.trim()));
  if (!nonempty.length) return { rows: [], errors: ['Add CSV or tab-separated rows.'] };
  const headers = nonempty[0].map(value => value.trim());
  const missing = ['testLabel', 'originalResultText', 'resultKind'].filter(name => !headers.includes(name));
  if (missing.length) return { rows: [], errors: [`Header must include: ${[...labCsvHeaders.slice(0, 3)].join(', ')}. Missing ${missing.join(', ')}.`] };
  const errors: string[] = [];
  const rows = nonempty.slice(1).map((values, index) => {
    const at = index + 2;
    if (values.length !== headers.length) errors.push(`Row ${at}: expected ${headers.length} columns, found ${values.length}.`);
    const value = (key: string) => { const column = headers.indexOf(key); return column < 0 ? '' : (values[column] ?? ''); };
    const testLabel = value('testLabel'), originalResultText = value('originalResultText');
    const kind = value('resultKind').trim().toUpperCase();
    if (!testLabel.trim()) errors.push(`Row ${at}: testLabel is required.`);
    if (!originalResultText.trim()) errors.push(`Row ${at}: originalResultText is required.`);
    if (!['NUMERIC', 'BELOW_LIMIT', 'NOT_DETECTED', 'QUALITATIVE', 'OTHER'].includes(kind)) errors.push(`Row ${at}: unsupported resultKind “${kind}”.`);
    const numericValue = value('numericValue');
    if (['NUMERIC', 'BELOW_LIMIT'].includes(kind) && !numericValue) errors.push(`Row ${at}: numericValue is required for ${kind}.`);
    for (const key of ['numericValue', 'lod', 'loq']) {
      const numeric = value(key);
      if (numeric.trim() && !/^[0-9]{1,12}(?:\.[0-9]{1,12})?$/.test(numeric)) errors.push(`Row ${at}: ${key} must be a nonnegative exact decimal string with at most 12 digits on each side.`);
    }
    const output: LabCsvRow = { testLabel, originalResultText, resultKind: kind as LabCsvRow['resultKind'] };
    for (const key of ['substanceId', 'numericValue', 'unit', 'lod', 'loq', 'method', 'uncertainty', 'reference']) {
      const item = value(key); if (item) Object.assign(output, { [key]: item });
    }
    return output;
  });
  return { rows, errors };
}
