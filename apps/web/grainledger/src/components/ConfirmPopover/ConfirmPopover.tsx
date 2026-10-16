import { WarningCircle, X } from '@phosphor-icons/react';
import { AnimatePresence, motion } from 'framer-motion';
import React, { useEffect, useRef } from 'react';

export interface ConfirmPopoverProps {
  isOpen: boolean;
  title: string;
  description?: string;
  warningNote?: string;
  confirmLabel?: string;
  cancelLabel?: string;
  variant?: 'danger' | 'warning' | 'primary';
  onConfirm: () => void;
  onCancel: () => void;
}

export const ConfirmPopover: React.FC<ConfirmPopoverProps> = ({
  isOpen,
  title,
  description,
  warningNote,
  confirmLabel = 'Xác nhận',
  cancelLabel = 'Huỷ',
  variant = 'danger',
  onConfirm,
  onCancel,
}) => {
  const dialogRef = useRef<HTMLDivElement>(null);

  // Close on Escape key
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && isOpen) {
        onCancel();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isOpen, onCancel]);

  const confirmBtnStyles = {
    danger: 'bg-accent-danger hover:bg-red-700 text-white',
    warning: 'bg-accent-warning hover:bg-amber-500 text-ink-primary',
    primary: 'bg-accent-primary hover:bg-accent-primary-dim text-white',
  }[variant];

  return (
    <AnimatePresence>
      {isOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
          {/* Backdrop */}
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-ink-primary/40 backdrop-blur-xs"
            onClick={onCancel}
          />

          {/* Dialog Modal */}
          <motion.div
            ref={dialogRef}
            initial={{ scale: 0.95, opacity: 0, y: 8 }}
            animate={{ scale: 1, opacity: 1, y: 0 }}
            exit={{ scale: 0.95, opacity: 0, y: 8 }}
            transition={{ duration: 0.2, ease: [0.22, 1, 0.36, 1] }}
            className="relative z-10 w-full max-w-md rounded-lg bg-surface-base dark:bg-surface-raised border border-ink-tertiary/20 p-6 shadow-xl"
            role="alertdialog"
            aria-modal="true"
            aria-labelledby="confirm-dialog-title"
          >
            <button
              type="button"
              onClick={onCancel}
              className="absolute top-4 right-4 text-ink-tertiary hover:text-ink-primary p-1 rounded-sm transition-colors"
              aria-label="Đóng"
            >
              <X size={18} />
            </button>

            <div className="flex items-start gap-4">
              <div
                className={`p-2.5 rounded-full shrink-0 ${
                  variant === 'danger'
                    ? 'bg-red-100 text-accent-danger dark:bg-red-950/50'
                    : variant === 'warning'
                    ? 'bg-amber-100 text-accent-warning dark:bg-amber-950/50'
                    : 'bg-emerald-100 text-accent-primary dark:bg-emerald-950/50'
                }`}
              >
                <WarningCircle size={24} weight="fill" />
              </div>

              <div className="flex-1 space-y-2">
                <h3 id="confirm-dialog-title" className="text-base font-semibold text-ink-primary">
                  {title}
                </h3>
                {description && (
                  <p className="text-sm text-ink-secondary leading-normal">
                    {description}
                  </p>
                )}
                {warningNote && (
                  <div className="p-2.5 rounded bg-amber-50 dark:bg-amber-950/40 border border-amber-300 dark:border-amber-700/50 text-xs text-amber-900 dark:text-amber-200">
                    <strong>Lưu ý:</strong> {warningNote}
                  </div>
                )}
              </div>
            </div>

            <div className="mt-6 flex items-center justify-end gap-3">
              <button
                type="button"
                onClick={onCancel}
                className="px-4 py-2 text-sm font-medium text-ink-secondary hover:text-ink-primary hover:bg-surface-raised dark:hover:bg-surface-overlay rounded-md transition-colors"
              >
                {cancelLabel}
              </button>
              <button
                type="button"
                onClick={onConfirm}
                className={`px-4 py-2 text-sm font-semibold rounded-md shadow-xs transition-colors ${confirmBtnStyles}`}
              >
                {confirmLabel}
              </button>
            </div>
          </motion.div>
        </div>
      )}
    </AnimatePresence>
  );
};
