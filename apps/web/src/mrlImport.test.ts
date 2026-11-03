import { expect, it } from 'vitest';
import { parseCsv, previewMrlRows } from './mrlImport';

const header = 'mrl_id,substance_id,substance_name,commodity_code,commodity_name_en,market,regulation_reference,mrl_value_mg_kg,mrl_unit,effective_date,version,status';

it('reads quoted CSV cells and previews only numeric ACTIVE rows without inventing an end date', () => {
  const csv = `${header}\r\nMRL-1,TCZ001,"Tricyclazole, technical",0500060,Rice,EU,"Regulation, section 2",0.01,mg/kg,2027-01-01,EU-v1,ACTIVE\r\nMRL-2,IPT002,Isoprothiolane,0500060,Rice,EU,Regulation,,mg/kg,2027-01-01,EU-v1,NO_MRL\r\n`;
  const preview = previewMrlRows(parseCsv(csv));
  expect(preview.excludedRows).toBe(1);
  expect(preview.groups).toHaveLength(1);
  expect(preview.groups[0]).toMatchObject({ market: 'EU', commodityCode: '0500060', effectiveDate: '2027-01-01', effectiveTo: '', excludedRows: [{ row: 3, substanceCode: 'IPT002', status: 'NO_MRL' }] });
  expect(preview.groups[0].limits).toEqual([{ row: 2, substanceCode: 'TCZ001', substanceName: 'Tricyclazole, technical', limitValue: '0.01', unit: 'mg/kg' }]);
});

it('keeps different regulatory versions separate and rejects duplicate substance limits', () => {
  const rows = parseCsv(`${header}\n1,A,One,RICE,Rice,EU,Rule,1,mg/kg,2027-01-01,v1,ACTIVE\n2,A,One,RICE,Rice,EU,Rule,2,mg/kg,2027-01-01,v1,ACTIVE\n3,B,Two,RICE,Rice,EU,New rule,3,mg/kg,2028-01-01,v2,ACTIVE`);
  const preview = previewMrlRows(rows);
  expect(preview.groups).toHaveLength(2);
  expect(preview.groups[0].issues).toContain('Row 3: substance A appears more than once in this version.');
  expect(preview.groups[1].issues).toHaveLength(0);
});

it('accepts a missing file effective date for explicit admin entry', () => {
  const preview = previewMrlRows(parseCsv(`${header}\n1,A,One,RICE,Rice,JAPAN,Rule,0.5,mg/kg,,JP-v1,ACTIVE`));
  expect(preview.groups[0]).toMatchObject({ effectiveDate: '', limits: [{ substanceCode: 'A', limitValue: '0.5' }] });
});
