import { formatMessage } from '@/app/i18n/messages';
import { LockSimple, ShieldWarning } from '@phosphor-icons/react';
import React from 'react';

export interface HoldBannerProps {
  targetType: 'Lô hàng' | 'Container' | 'Thửa ruộng';
  targetId: string;
  reason?: string;
  issuedAt?: string;
  className?: string;
}

/**
 * HoldBanner
 * Displays authoritative warning when a lot or container is under quarantine HOLD (BR-15).
 * Note: MVP has NO stock RELEASE action.
 */
export const HoldBanner: React.FC<HoldBannerProps> = ({
  targetType,
  targetId,
  reason = 'Phát hiện dư lượng hoạt chất Tricyclazole vượt cận an toàn theo kết quả phân tích phòng lab.',
  issuedAt = '15/10/2026 14:30',
  className = '',
}) => {
  const message = formatMessage('MSG09', {
    target_type: targetType,
    target_id: targetId,
  });

  return (
    <div
      className={`p-4 rounded-md border-2 border-functional-hold-border bg-functional-hold-bg dark:bg-functional-hold-bg/40 text-ink-primary shadow-xs ${className}`}
      role="alert"
    >
      <div className="flex items-start gap-3">
        <div className="p-1.5 rounded bg-accent-danger text-white shrink-0 mt-0.5">
          <LockSimple size={20} weight="fill" />
        </div>

        <div className="flex-1 space-y-1">
          <div className="flex items-center justify-between flex-wrap gap-2">
            <span className="font-bold text-accent-danger text-sm flex items-center gap-1.5">
              <span>{message}</span>
            </span>
            <span className="text-xs font-mono text-ink-secondary bg-surface-base px-2 py-0.5 rounded border border-functional-hold-border/30">
              Thời điểm ban hành: {issuedAt}
            </span>
          </div>

          <p className="text-xs text-ink-secondary leading-normal">
            <strong>Lý do phong toả:</strong> {reason}
          </p>

          <div className="flex items-center gap-2 pt-1 text-xs text-accent-danger font-medium">
            <ShieldWarning size={14} />
            <span>
              Mọi hoạt động sấy, xay xát, phối trộn hoặc xếp hàng lên container liên quan đến đối tượng này đều bị chặn tự động ở tầng dữ liệu.
            </span>
          </div>
        </div>
      </div>
    </div>
  );
};
