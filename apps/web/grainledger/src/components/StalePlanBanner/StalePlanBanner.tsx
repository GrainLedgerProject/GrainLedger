import { formatMessage } from '@/app/i18n/messages';
import { ArrowClockwise, WarningCircle } from '@phosphor-icons/react';
import React from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';

export interface StalePlanBannerProps {
  planId: string;
  orderId?: string;
  containerId?: string;
  reason?: string;
  className?: string;
}

/**
 * StalePlanBanner
 * Warning banner for blend plans in STALE state (BR-14 / MSG08).
 * A stale plan cannot authorize processing or loading without a fresh passing review.
 */
export const StalePlanBanner: React.FC<StalePlanBannerProps> = ({
  planId,
  orderId = 'ORD-2026-EU-01',
  containerId = 'CONT-01',
  reason,
  className = '',
}) => {
  const { t, i18n } = useTranslation();
  const defaultReason = t(
    'export.stalePlanDefaultReason',
    'Có kết quả kiểm nghiệm mới hoặc thay đổi tồn kho của các lô nguyên liệu thành phần.'
  );

  const message = formatMessage(
    'MSG08',
    {
      plan_id: planId,
    },
    i18n.language
  );

  return (
    <div
      className={`p-4 rounded-md border-2 border-functional-stale-border bg-functional-stale-bg dark:bg-functional-stale-bg/40 text-ink-primary shadow-xs ${className}`}
      role="alert"
    >
      <div className="flex items-start gap-3">
        <div className="p-1.5 rounded bg-accent-warning text-ink-primary shrink-0 mt-0.5">
          <WarningCircle size={20} weight="fill" />
        </div>

        <div className="flex-1 space-y-1">
          <div className="flex items-center justify-between flex-wrap gap-2">
            <span className="font-bold text-amber-900 dark:text-amber-200 text-sm">
              {message}
            </span>
            <span className="text-xs font-mono text-ink-secondary bg-surface-base px-2 py-0.5 rounded border border-functional-stale-border/40">
              {t('export.planCode', 'Mã Plan')}: {planId}
            </span>
          </div>

          <p className="text-xs text-ink-secondary leading-normal">
            <strong>{t('export.staleReason', 'Nguyên nhân chuyển trạng thái STALE')}:</strong> {reason || defaultReason}
          </p>

          <div className="pt-2 flex items-center gap-3">
            <Link
              to={`/export/orders/${orderId}/containers/${containerId}/plan`}
              className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md text-xs font-semibold bg-accent-warning text-ink-primary hover:bg-amber-400 transition-colors shadow-xs"
            >
              <ArrowClockwise size={14} weight="bold" />
              {t('export.reRequestPlan', 'Yêu cầu đánh giá lại phương án')}
            </Link>
            <span className="text-xs text-ink-tertiary">
              {t('export.staleRecheckHint', 'Cần chạy lại bài toán kiểm tra tính khả thi và MRL mới nhất.')}
            </span>
          </div>
        </div>
      </div>
    </div>
  );
};

