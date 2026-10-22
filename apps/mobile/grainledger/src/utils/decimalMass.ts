import { Decimal } from 'decimal.js';

Decimal.set({ precision: 28, rounding: Decimal.ROUND_HALF_UP });

export type MassInput = Decimal | string | number;

export function toDecimal(val: MassInput | null | undefined): Decimal {
  if (val === null || val === undefined) return new Decimal(0);
  if (val instanceof Decimal) return val;
  if (typeof val === 'number') {
    return new Decimal(val.toString());
  }
  const clean = val.toString().trim().replace(/,/g, '');
  if (!clean || isNaN(Number(clean))) return new Decimal(0);
  return new Decimal(clean);
}

export function formatMass(
  val: MassInput | null | undefined,
  decimals: 0 | 1 | 2 | 3 | 6 = 2,
  locale: 'vi' | 'en' = 'vi'
): string {
  const d = toDecimal(val);
  const fixedStr = d.toFixed(decimals);
  const [integerPart, fractionPart] = fixedStr.split('.');

  const separator = locale === 'vi' ? '.' : ',';
  const decimalPoint = locale === 'vi' ? ',' : '.';

  const formattedInteger = integerPart.replace(/\B(?=(\d{3})+(?!\d))/g, separator);
  return fractionPart ? `${formattedInteger}${decimalPoint}${fractionPart}` : formattedInteger;
}

export function checkMassBalance(
  totalInput: MassInput,
  totalOutput: MassInput,
  declaredLoss: MassInput,
  tolerancePercent: number = 0.5
): {
  isBalanced: boolean;
  varianceKg: Decimal;
  allowedKg: Decimal;
  percentageDiff: Decimal;
} {
  const inp = toDecimal(totalInput);
  const out = toDecimal(totalOutput);
  const loss = toDecimal(declaredLoss);

  const sumOutAndLoss = out.plus(loss);
  const varianceKg = sumOutAndLoss.minus(inp).abs();
  const allowedKg = inp.times(tolerancePercent).dividedBy(100);

  const percentageDiff = inp.isZero() ? new Decimal(0) : varianceKg.dividedBy(inp).times(100);
  const isBalanced = varianceKg.lessThanOrEqualTo(allowedKg);

  return {
    isBalanced,
    varianceKg,
    allowedKg,
    percentageDiff,
  };
}

export { Decimal };
