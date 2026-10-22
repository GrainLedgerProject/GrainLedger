import { useUiStore } from "@/app/store/uiStore";
import { ArrowsClockwise, CloudCheck, CloudSlash } from "@phosphor-icons/react";
import React from "react";
import { Link } from "react-router-dom";

export interface NetworkStatusIndicatorProps {
  className?: string;
  showSyncLink?: boolean;
}

export const NetworkStatusIndicator: React.FC<NetworkStatusIndicatorProps> = ({
  className = "",
  showSyncLink = true,
}) => {
  const { isOnline, pendingOfflineCount } = useUiStore();

  return (
    <div
      className={`inline-flex items-center gap-2 px-2.5 py-1 rounded-full text-xs font-medium border transition-colors ${
        isOnline
          ? pendingOfflineCount > 0
            ? "bg-amber-500/10 text-amber-700 border-amber-500/30 dark:bg-amber-950/30 dark:text-amber-400"
            : "bg-emerald-500/10 text-accent-primary border-accent-primary/20 dark:bg-emerald-950/30 dark:text-accent-primary"
          : "bg-red-500/10 text-accent-danger border-accent-danger/30 dark:bg-red-950/30 dark:text-accent-danger"
      } ${className}`}
      role="status"
    >
      <span className="relative flex h-2 w-2">
        {isOnline && (
          <span
            className={`animate-ping absolute inline-flex h-full w-full rounded-full opacity-75 ${
              pendingOfflineCount > 0 ? "bg-amber-400" : "bg-emerald-400"
            }`}
          />
        )}
        <span
          className={`relative inline-flex rounded-full h-2 w-2 ${
            !isOnline
              ? "bg-accent-danger"
              : pendingOfflineCount > 0
                ? "bg-amber-500"
                : "bg-accent-primary"
          }`}
        />
      </span>

      <span className="flex items-center gap-1">
        {isOnline ? (
          pendingOfflineCount > 0 ? (
            <>
              <CloudCheck size={14} />
              <span>
                Trực tuyến ({pendingOfflineCount} bản ghi chờ đồng bộ)
              </span>
            </>
          ) : (
            <>
              <CloudCheck size={14} />
              <span>Trực tuyến</span>
            </>
          )
        ) : (
          <>
            <CloudSlash size={14} />
            <span>Ngoại tuyến ({pendingOfflineCount} bản ghi cục bộ)</span>
          </>
        )}
      </span>

      {showSyncLink && pendingOfflineCount > 0 && isOnline && (
        <Link
          to="/field/sync"
          className="ml-1 hover:text-ink-primary font-semibold flex items-center gap-0.5"
        >
          <ArrowsClockwise
            size={14}
            className="animate-spin"
            style={{ animationDuration: "3s" }}
          />
          Đồng bộ
        </Link>
      )}
    </div>
  );
};
