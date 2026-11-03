import { useUiStore } from '@/app/store/uiStore';
import type { UserRole } from '@/shared/types/roles';
import { ROLE_DEFINITIONS } from '@/shared/types/roles';
import React from 'react';

export interface RoleChipProps {
  role: UserRole;
  showCode?: boolean;
  size?: 'sm' | 'md';
  className?: string;
}

export const RoleChip: React.FC<RoleChipProps> = ({
  role,
  showCode = false,
  size = 'md',
  className = '',
}) => {
  const language = useUiStore((state) => state.language);
  const def = ROLE_DEFINITIONS[role] || ROLE_DEFINITIONS.MILL_OPERATOR;
  const label = language === 'vi' ? def.labelVi : def.labelEn;

  // Side-based color themes
  const sideStyles = {
    MILL: 'bg-emerald-900/10 text-accent-primary border-accent-primary/30 dark:bg-emerald-950/40 dark:text-accent-primary dark:border-accent-primary/40',
    CUSTOMER: 'bg-indigo-900/10 text-accent-info border-accent-info/30 dark:bg-indigo-950/40 dark:text-blue-300 dark:border-accent-info/40',
    QUALIFIED_ACCESS: 'bg-amber-900/10 text-amber-800 border-amber-600/30 dark:bg-amber-950/40 dark:text-amber-300 dark:border-amber-500/40',
  };

  const currentSideStyle = sideStyles[def.side];
  const sizeClasses = size === 'sm' ? 'px-2 py-0.5 text-xs' : 'px-2.5 py-1 text-xs font-medium';

  return (
    <span
      className={`inline-flex items-center gap-1.5 rounded-full border ${currentSideStyle} ${sizeClasses} select-none ${className}`}
      title={`${def.side}: ${label}`}
    >
      <span
        className={`w-1.5 h-1.5 rounded-full ${
          def.side === 'MILL'
            ? 'bg-accent-primary'
            : def.side === 'CUSTOMER'
            ? 'bg-accent-info'
            : 'bg-amber-500'
        }`}
      />
      {showCode && <span className="font-mono font-bold">[{def.code}]</span>}
      <span>{label}</span>
    </span>
  );
};
