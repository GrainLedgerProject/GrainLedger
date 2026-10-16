import { MassValue } from '@/components/MassValue/MassValue';
import type { Decimal } from '@/shared/utils/decimalMass';
import { ArrowsCounterClockwise, ShieldWarning, XCircle } from '@phosphor-icons/react';
import React from 'react';
import { useTranslation } from 'react-i18next';

export interface LedgerRejectionCardProps {
  msgCode: 'MSG01' | 'MSG02' | string;
  varianceKg?: Decimal | string | number;
  allowedKg?: Decimal | string | number;
  lotId?: string;
  availableKg?: Decimal | string | number;
  inputMassKg?: Decimal | string | number;
  outputMassKg?: Decimal | string | number;
  lossKg?: Decimal | string | number;
  suggestedFix?: string;
  onRetry?: () => void;
  className?: string;
}

/**
 * LedgerRejectionCard
 * The sacred "TỪ CHỐI" (REJECTION) moment for ledger mass-balance failure.
 * Never silently warn or toast. Shows exact fired-clay border, shake animation,
 * arithmetic breakdown, and suggested action.
 */
export const LedgerRejectionCard: React.FC<LedgerRejectionCardProps> = ({
  msgCode,
  varianceKg,
  allowedKg,
  lotId,
  availableKg,
  inputMassKg,
  outputMassKg,
  lossKg,
  suggestedFix,
  onRetry,
  className = '',
}) => {
  const { t } = useTranslation();
  const defaultSuggestedFix = t(
    'mill.rejection.defaultSuggestedFix',
    'Kiểm tra lại số cân bàn cân thực tế hoặc đối soát lại lượng hao hụt ẩm đã khai báo trước khi nộp lại giao dịch.'
  );

  return (
    <div
      className={`animate-ledger-shake border-2 border-accent-danger bg-functional-rejected-bg dark:bg-functional-rejected-bg/40 rounded-lg p-5 shadow-md ${className}`}
      role="alert"
      aria-live="assertive"
    >
      <div className="flex items-start gap-3.5">
        <div className="p-2 rounded-md bg-accent-danger text-white shrink-0 shadow-xs">
          <XCircle size={24} weight="fill" />
        </div>

        <div className="flex-1 space-y-3">
          <div className="flex items-center justify-between flex-wrap gap-2">
            <div className="flex items-center gap-2">
              <span className="font-mono text-xs font-bold px-2 py-0.5 rounded bg-accent-danger text-white">
                {msgCode}
              </span>
              <h3 className="font-semibold text-accent-danger text-base">
                {msgCode === 'MSG01'
                  ? t('mill.rejection.titleBR01', 'Giao dịch bị từ chối — Vi phạm Cân bằng Khối lượng (BR-01)')
                  : msgCode === 'MSG02'
                  ? t('mill.rejection.titleBR02', 'Giao dịch bị từ chối — Không đủ số dư khả dụng (BR-02)')
                  : t('mill.rejection.titleDefault', 'Giao dịch bị sổ cái từ chối')}
              </h3>
            </div>
            <span className="text-xs text-ink-secondary italic">
              {t('mill.rejection.isoLaw', 'Định luật bảo toàn khối lượng ISO 22095')}
            </span>
          </div>

          <p className="text-sm text-ink-primary leading-relaxed">
            {msgCode === 'MSG01' ? (
              <>
                {t(
                  'mill.rejection.msg01Desc',
                  'Sổ cái từ chối ghi nhận bước chế biến này vì tổng khối lượng đầu ra cộng hao hụt không khớp với tổng đầu vào. Lệch quá mức dung sai cho phép.'
                )}
              </>
            ) : msgCode === 'MSG02' ? (
              <>
                {t('mill.rejection.msg02Prefix', 'Thao tác bị từ chối. Lô')}{' '}
                <span className="font-mono font-bold">{lotId}</span>{' '}
                {t('mill.rejection.msg02Mid', 'chỉ còn khả dụng')}{' '}
                <MassValue value={availableKg} decimals={3} />{' '}
                {t('mill.rejection.msg02Suffix', 'tại vị trí kho này.')}
              </>
            ) : (
              t('mill.rejection.defaultDesc', 'Dữ liệu vi phạm quy tắc kế toán lưu giữ của hệ thống.')
            )}
          </p>

          {/* Precision Numbers Table */}
          {msgCode === 'MSG01' && (
            <div className="grid grid-cols-2 sm:grid-cols-4 gap-2 p-3 rounded-md bg-surface-base border border-accent-danger/30 text-xs">
              <div>
                <span className="text-ink-tertiary block">{t('mill.rejection.totalIn', 'Tổng đầu vào')}:</span>
                <MassValue value={inputMassKg} decimals={3} className="text-sm font-semibold" />
              </div>
              <div>
                <span className="text-ink-tertiary block">{t('mill.rejection.totalOutLoss', 'Đầu ra + Hao hụt')}:</span>
                <MassValue
                  value={
                    outputMassKg !== undefined && lossKg !== undefined
                      ? Number(outputMassKg) + Number(lossKg)
                      : undefined
                  }
                  decimals={3}
                  className="text-sm font-semibold"
                />
              </div>
              <div>
                <span className="text-ink-tertiary block">{t('mill.rejection.variance', 'Độ lệch (Variance)')}:</span>
                <MassValue
                  value={varianceKg}
                  decimals={3}
                  className="text-sm font-bold text-accent-danger"
                />
              </div>
              <div>
                <span className="text-ink-tertiary block">{t('mill.rejection.allowed', 'Dung sai tối đa')}:</span>
                <MassValue
                  value={allowedKg}
                  decimals={3}
                  className="text-sm font-semibold text-accent-primary"
                />
              </div>
            </div>
          )}

          {/* Suggested Fix */}
          <div className="flex items-start gap-2 pt-1 text-xs text-ink-secondary">
            <ShieldWarning size={16} className="text-accent-warning shrink-0 mt-0.5" />
            <p>
              <strong className="text-ink-primary">{t('mill.rejection.suggestedFixLabel', 'Hướng giải quyết')}:</strong>{' '}
              {suggestedFix || defaultSuggestedFix}
            </p>
          </div>

          {onRetry && (
            <div className="pt-1">
              <button
                type="button"
                onClick={onRetry}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md text-xs font-semibold bg-accent-danger text-white hover:bg-red-700 transition-colors shadow-xs"
              >
                <ArrowsCounterClockwise size={14} />
                {t('mill.rejection.retryButton', 'Hiệu chỉnh lại thông số')}
              </button>
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

