import { useUiStore } from '@/app/store/uiStore';
import type { Decimal } from '@/shared/utils/decimalMass';
import { formatMass, toDecimal } from '@/shared/utils/decimalMass';
import React from 'react';

export interface MassValueProps {
  value: Decimal | string | number | null | undefined;
  decimals?: 3 | 6;
  unit?: string;
  className?: string;
  showPositiveSign?: boolean;
}

/**
 * MassValue Component
 * Enforces sacred mass data display rules:
 * 1. Exact arithmetic via decimal.js
 * 2. Tabular numbers with monospace font
 * 3. Right-aligned layout
 * 4. Explicit units ('kg')
 * 5. Exactly 3 or 6 decimal places
 */
export const MassValue: React.FC<MassValueProps> = ({
  value,
  decimals = 3,
  unit = 'kg',
  className = '',
  showPositiveSign = false,
}) => {
  const language = useUiStore((state) => state.language);
  const d = toDecimal(value);
  const formatted = formatMass(d, decimals, language);
  const isPositive = d.greaterThan(0);

  return (
    <span
      className={`font-mono tabular-nums text-right inline-flex items-baseline justify-end gap-1 ${className}`}
      title={`${d.toString()} ${unit}`}
    >
      <span className="font-semibold text-ink-primary">
        {showPositiveSign && isPositive ? `+${formatted}` : formatted}
      </span>
      {unit && (
        <span className="text-xs font-normal text-ink-tertiary select-none">
          {unit}
        </span>
      )}
    </span>
  );
};
