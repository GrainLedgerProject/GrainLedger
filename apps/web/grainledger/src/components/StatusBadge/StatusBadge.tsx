import {
  ArrowsClockwise,
  CheckCircle,
  Clock,
  ClockCounterClockwise,
  LockSimple,
  ShieldCheck,
  WarningCircle,
  XCircle,
} from '@phosphor-icons/react';
import React from 'react';

export type StatusType =
  | 'BALANCED'
  | 'REJECTED'
  | 'HOLD'
  | 'STALE'
  | 'COMPLIANT'
  | 'NON_COMPLIANT'
  | 'PENDING'
  | 'IN_PROGRESS'
  | 'CLOSED'
  | 'ACTIVE';

export interface StatusBadgeProps {
  status: StatusType;
  label?: string;
  size?: 'sm' | 'md';
  className?: string;
}

const STATUS_CONFIG: Record<
  StatusType,
  {
    bg: string;
    border: string;
    text: string;
    icon: React.ComponentType<{ size?: number; className?: string; weight?: 'bold' | 'fill' | 'regular' }>;
    defaultLabelVi: string;
    defaultLabelEn: string;
  }
> = {
  BALANCED: {
    bg: 'bg-functional-balanced-bg dark:bg-functional-balanced-bg/30',
    border: 'border-functional-balanced-border',
    text: 'text-accent-primary dark:text-accent-primary',
    icon: CheckCircle,
    defaultLabelVi: 'Cân bằng',
    defaultLabelEn: 'Balanced',
  },
  REJECTED: {
    bg: 'bg-functional-rejected-bg dark:bg-functional-rejected-bg/30',
    border: 'border-functional-rejected-border',
    text: 'text-accent-danger',
    icon: XCircle,
    defaultLabelVi: 'Từ chối',
    defaultLabelEn: 'Rejected',
  },
  HOLD: {
    bg: 'bg-functional-hold-bg dark:bg-functional-hold-bg/30',
    border: 'border-functional-hold-border',
    text: 'text-accent-danger font-semibold',
    icon: LockSimple,
    defaultLabelVi: 'Lệnh HOLD',
    defaultLabelEn: 'HOLD Active',
  },
  STALE: {
    bg: 'bg-functional-stale-bg dark:bg-functional-stale-bg/30',
    border: 'border-functional-stale-border',
    text: 'text-accent-warning',
    icon: ClockCounterClockwise,
    defaultLabelVi: 'Lỗi thời (STALE)',
    defaultLabelEn: 'Stale Plan',
  },
  COMPLIANT: {
    bg: 'bg-functional-balanced-bg dark:bg-functional-balanced-bg/30',
    border: 'border-functional-balanced-border',
    text: 'text-accent-success',
    icon: ShieldCheck,
    defaultLabelVi: 'Đạt chuẩn MRL',
    defaultLabelEn: 'MRL Compliant',
  },
  NON_COMPLIANT: {
    bg: 'bg-functional-rejected-bg dark:bg-functional-rejected-bg/30',
    border: 'border-functional-rejected-border',
    text: 'text-accent-danger',
    icon: WarningCircle,
    defaultLabelVi: 'Không đạt MRL',
    defaultLabelEn: 'Non-Compliant',
  },
  PENDING: {
    bg: 'bg-functional-stale-bg dark:bg-functional-stale-bg/30',
    border: 'border-functional-stale-border',
    text: 'text-accent-warning',
    icon: Clock,
    defaultLabelVi: 'Chờ duyệt',
    defaultLabelEn: 'Pending Review',
  },
  IN_PROGRESS: {
    bg: 'bg-blue-50 dark:bg-blue-950/40',
    border: 'border-accent-info',
    text: 'text-accent-info',
    icon: ArrowsClockwise,
    defaultLabelVi: 'Đang xử lý',
    defaultLabelEn: 'In Progress',
  },
  CLOSED: {
    bg: 'bg-surface-raised dark:bg-surface-overlay',
    border: 'border-ink-tertiary/40',
    text: 'text-ink-secondary',
    icon: CheckCircle,
    defaultLabelVi: 'Đã đóng niêm',
    defaultLabelEn: 'Closed & Sealed',
  },
  ACTIVE: {
    bg: 'bg-emerald-50 dark:bg-emerald-950/40',
    border: 'border-emerald-500',
    text: 'text-emerald-700 dark:text-emerald-400',
    icon: CheckCircle,
    defaultLabelVi: 'Hoạt động',
    defaultLabelEn: 'Active',
  },
};

export const StatusBadge: React.FC<StatusBadgeProps> = ({
  status,
  label,
  size = 'md',
  className = '',
}) => {
  const config = STATUS_CONFIG[status] || STATUS_CONFIG.PENDING;
  const Icon = config.icon;
  const displayLabel = label || config.defaultLabelVi;

  const sizeClasses = size === 'sm' ? 'px-2 py-0.5 text-xs gap-1' : 'px-2.5 py-1 text-xs gap-1.5 font-medium';
  const iconSize = size === 'sm' ? 12 : 14;

  return (
    <span
      className={`inline-flex items-center rounded-full border ${config.bg} ${config.border} ${config.text} ${sizeClasses} select-none ${className}`}
    >
      <Icon size={iconSize} weight="bold" className="shrink-0" />
      <span>{displayLabel}</span>
    </span>
  );
};
