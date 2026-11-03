export type ImportLimit = { substanceCode: string; substanceName: string; limitValue: string; unit: string; row: number };
export type ImportGroup = {
  key: string; market: string; commodityCode: string; commodityName: string;
  version: string; effectiveDate: string; effectiveTo: string;
  sources: string[]; limits: ImportLimit[]; issues: string[];
  excludedRows: { row: number; substanceCode: string; status: string }[];
};
export type ImportPreview = { groups: ImportGroup[]; totalRows: number; excludedRows: number };

const required = ['substance_id', 'commodity_name_en', 'market', 'mrl_value_mg_kg', 'mrl_unit', 'effective_date', 'version', 'status'];
const limitNumber = /^(?:0|[1-9]\d{0,11})(?:\.\d{1,12})?$/;
const cell = (value: unknown) => value == null ? '' : value instanceof Date
  ? `${value.getUTCFullYear()}-${String(value.getUTCMonth() + 1).padStart(2, '0')}-${String(value.getUTCDate()).padStart(2, '0')}`
  : String(value).trim();

export function parseCsv(source: string): string[][] {
  const rows: string[][] = []; let row: string[] = []; let value = ''; let quoted = false;
  const text = source.replace(/^\uFEFF/, '');
  for (let i = 0; i < text.length; i++) {
    const char = text[i];
    if (char === '"') {
      if (quoted && text[i + 1] === '"') { value += '"'; i++; }
      else if (!quoted && value) throw new Error(`Invalid CSV quote near character ${i + 1}.`);
      else quoted = !quoted;
    } else if (char === ',' && !quoted) { row.push(value); value = ''; }
    else if ((char === '\n' || char === '\r') && !quoted) {
      if (char === '\r' && text[i + 1] === '\n') i++;
      row.push(value); if (row.some(item => item.trim())) rows.push(row);
      row = []; value = '';
    } else value += char;
  }
  if (quoted) throw new Error('CSV has an unclosed quoted field.');
  row.push(value); if (row.some(item => item.trim())) rows.push(row);
  return rows;
}

export function previewMrlRows(rows: unknown[][]): ImportPreview {
  if (rows.length < 2) throw new Error('The file has no MRL data rows.');
  const headers = rows[0].map(value => cell(value).replace(/^\uFEFF/, '').toLowerCase());
  const missing = required.filter(header => !headers.includes(header));
  if (missing.length) throw new Error(`Missing columns: ${missing.join(', ')}.`);
  const read = (row: unknown[], name: string) => cell(row[headers.indexOf(name)]);
  const groups = new Map<string, ImportGroup>(); let excludedRows = 0;
  for (let index = 1; index < rows.length; index++) {
    const row = rows[index]; if (!row.some(value => cell(value))) continue;
    const market = read(row, 'market').toUpperCase();
    const commodityCode = read(row, 'commodity_code');
    const commodityName = read(row, 'commodity_name_en');
    const version = read(row, 'version');
    const effectiveDate = read(row, 'effective_date');
    const effectiveTo = headers.includes('effective_to') ? read(row, 'effective_to') : '';
    if (!market || !commodityName || !version || (effectiveDate && !/^\d{4}-\d{2}-\d{2}$/.test(effectiveDate)) ||
        (effectiveTo && !/^\d{4}-\d{2}-\d{2}$/.test(effectiveTo)))
      throw new Error(`Row ${index + 1}: market, commodity and version are required; dates must use YYYY-MM-DD when supplied.`);
    const key = JSON.stringify([market, commodityCode || commodityName, version, effectiveDate, effectiveTo]);
    let group = groups.get(key);
    if (!group) {
      group = { key, market, commodityCode, commodityName, version, effectiveDate,
        effectiveTo, sources: [], limits: [], issues: [], excludedRows: [] };
      groups.set(key, group);
    }
    const source = read(row, 'regulation_reference');
    if (source && !group.sources.includes(source)) group.sources.push(source);
    const status = read(row, 'status').toUpperCase();
    if (status !== 'ACTIVE') {
      group.excludedRows.push({ row: index + 1, substanceCode: read(row, 'substance_id'), status });
      excludedRows++; continue;
    }
    const substanceCode = read(row, 'substance_id').toUpperCase();
    const limitValue = read(row, 'mrl_value_mg_kg');
    const unit = read(row, 'mrl_unit');
    if (!substanceCode || !limitNumber.test(limitValue) || unit.toLowerCase() !== 'mg/kg') {
      group.issues.push(`Row ${index + 1}: ACTIVE requires a substance code and a nonnegative mg/kg limit (up to 12 decimal places).`);
      continue;
    }
    if (group.limits.some(limit => limit.substanceCode === substanceCode)) {
      group.issues.push(`Row ${index + 1}: substance ${substanceCode} appears more than once in this version.`);
      continue;
    }
    group.limits.push({ substanceCode, substanceName: read(row, 'substance_name'), limitValue, unit: 'mg/kg', row: index + 1 });
  }
  for (const group of groups.values())
    if (group.limits.length > 500) group.issues.push('This group exceeds the 500-limit import maximum. Split and review the source table before importing.');
  return { groups: [...groups.values()], totalRows: rows.length - 1, excludedRows };
}

export async function readMrlFile(file: File): Promise<ImportPreview> {
  if (file.size > 2_000_000) throw new Error('Choose a CSV or XLSX file smaller than 2 MB.');
  const extension = file.name.split('.').pop()?.toLowerCase();
  if (extension === 'csv') return previewMrlRows(parseCsv(await file.text()));
  if (extension === 'xlsx') {
    const { readSheet } = await import('read-excel-file/browser');
    return previewMrlRows(await readSheet(file));
  }
  throw new Error('Choose a .csv or .xlsx file. Legacy .xls files are not supported.');
}
