import React from 'react';

export interface GrainLoaderProps {
  size?: 'sm' | 'md' | 'lg' | 'xl';
  label?: string;
  fullPage?: boolean;
  className?: string;
}

export const GrainLoader: React.FC<GrainLoaderProps> = ({
  size = 'md',
  label,
  fullPage = false,
  className = '',
}) => {
  const sizeMap = {
    sm: 20,
    md: 32,
    lg: 48,
    xl: 64,
  };

  const content = (
    <div
      className={`inline-flex flex-col items-center justify-center gap-3 transition-opacity duration-300 ${className}`}
      role="status"
      aria-live="polite"
    >
      <div className="relative flex items-center justify-center">
        {/* Subtle rotating glow ring */}
        <div
          className="absolute inset-0 rounded-full border border-accent-primary/20 border-t-accent-primary animate-spin"
          style={{ width: sizeMap[size] + 16, height: sizeMap[size] + 16, animationDuration: '1.2s' }}
        />
        {/* Rotating Grain */}
        <div className="animate-bounce" style={{ animationDuration: '1.4s' }}>
          <img src="/favicon.svg" alt="GrainLedger" className="w-12 h-12" />
        </div>
      </div>
      {label && (
        <span className="text-xs uppercase tracking-wider font-semibold text-ink-secondary">
          {label}
        </span>
      )}
      <span className="sr-only">Đang tải dữ liệu...</span>
    </div>
  );

  if (fullPage) {
    return (
      <div className="fixed inset-0 z-50 flex items-center justify-center bg-surface-base/80 backdrop-blur-sm">
        {content}
      </div>
    );
  }

  return content;
};
