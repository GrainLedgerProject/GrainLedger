import React from 'react';

/**
 * Apple-inspired shimmer skeleton loader for React.lazy Suspense fallbacks.
 * Uses CSS-only animation — zero JS overhead during chunk loading.
 */
export const AppleSuspenseLoader: React.FC = () => {
  return (
    <div className="flex-1 flex items-center justify-center min-h-[60vh]">
      <div className="flex flex-col items-center gap-5">
        {/* Pulsing logo mark */}
        <div className="relative">
          <div className="w-10 h-10 rounded-2xl bg-gradient-to-br from-emerald-500/20 to-emerald-600/10 dark:from-emerald-400/15 dark:to-emerald-500/5 animate-pulse" />
          <div
            className="absolute inset-0 rounded-2xl opacity-60"
            style={{
              background: 'linear-gradient(135deg, transparent 40%, rgba(255,255,255,0.4) 50%, transparent 60%)',
              backgroundSize: '200% 200%',
              animation: 'apple-shimmer 1.8s ease-in-out infinite',
            }}
          />
        </div>

        {/* Skeleton content blocks */}
        <div className="flex flex-col items-center gap-3 w-64">
          <div className="h-3 w-40 rounded-full bg-ink-tertiary/10 dark:bg-ink-tertiary/8 relative overflow-hidden">
            <div
              className="absolute inset-0"
              style={{
                background: 'linear-gradient(90deg, transparent, rgba(255,255,255,0.3), transparent)',
                animation: 'apple-shimmer-slide 1.6s ease-in-out infinite',
              }}
            />
          </div>
          <div className="h-2.5 w-56 rounded-full bg-ink-tertiary/8 dark:bg-ink-tertiary/5 relative overflow-hidden">
            <div
              className="absolute inset-0"
              style={{
                background: 'linear-gradient(90deg, transparent, rgba(255,255,255,0.2), transparent)',
                animation: 'apple-shimmer-slide 1.6s ease-in-out 0.15s infinite',
              }}
            />
          </div>
          <div className="h-2.5 w-32 rounded-full bg-ink-tertiary/6 dark:bg-ink-tertiary/4 relative overflow-hidden">
            <div
              className="absolute inset-0"
              style={{
                background: 'linear-gradient(90deg, transparent, rgba(255,255,255,0.15), transparent)',
                animation: 'apple-shimmer-slide 1.6s ease-in-out 0.3s infinite',
              }}
            />
          </div>
        </div>
      </div>
    </div>
  );
};
