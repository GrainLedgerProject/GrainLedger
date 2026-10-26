import React from "react";
import "@/index.css";

export interface GrainLoaderProps {
  size?: "sm" | "md" | "lg" | "xl";
  label?: string;
  fullPage?: boolean;
  className?: string;
}

const sizeMap = {
  sm: 24,
  md: 36,
  lg: 52,
  xl: 68,
};

export const GrainLoader: React.FC<GrainLoaderProps> = ({
  size = "md",
  label,
  fullPage = false,
  className = "",
}) => {
  const dimension = sizeMap[size];

  const loader = (
    <div
      role="status"
      aria-live="polite"
      className={`flex flex-col items-center justify-center gap-4 ${className}`}
    >
      <div
        className="relative flex items-center justify-center"
        style={{
          width: dimension + 24,
          height: dimension + 24,
        }}
      >
        {/* Soft green glow */}
        <div
          className="
            absolute rounded-full bg-emerald-500/10
            blur-xl animate-pulse
            motion-reduce:animate-none
          "
          style={{
            width: dimension,
            height: dimension,
          }}
          aria-hidden="true"
        />

        {/* Rotating progress arc */}
        <svg
          className="absolute inset-0 h-full w-full animate-spin motion-reduce:animate-none"
          style={{ animationDuration: "2.4s" }}
          viewBox="0 0 100 100"
          fill="none"
          aria-hidden="true"
        >
          <circle
            cx="50"
            cy="50"
            r="46"
            stroke="currentColor"
            strokeWidth="1.5"
            className="text-emerald-700/10"
          />
          <circle
            cx="50"
            cy="50"
            r="46"
            stroke="#D5AD43"
            strokeWidth="2.5"
            strokeLinecap="round"
            strokeDasharray="64 225"
          />
        </svg>

        {/* Your original GrainLedger logo */}
        <img
          src="/favicon.svg"
          alt=""
          draggable={false}
          className="
            relative z-10 object-contain
            animate-grain-breathe
            motion-reduce:animate-none
          "
          style={{
            width: dimension,
            height: dimension,
          }}
        />
      </div>

      {label && (
        <span className="text-xs font-semibold tracking-widest text-ink-secondary">
          {label}
        </span>
      )}

      <span className="sr-only">
        {label ? `${label}. ` : ""}Đang tải dữ liệu...
      </span>
    </div>
  );

  if (!fullPage) return loader;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-surface-base/80 backdrop-blur-sm">
      {loader}
    </div>
  );
};
