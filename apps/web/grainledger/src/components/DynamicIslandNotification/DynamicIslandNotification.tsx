import React, { useState } from 'react';
import { useUiStore } from '@/app/store/uiStore';
import { motion, AnimatePresence } from 'framer-motion';
import { WifiSlash, CloudArrowUp, ArrowRight } from '@phosphor-icons/react';
import { Link } from 'react-router-dom';

/**
 * Apple Dynamic Island Floating Pill Notification
 * Displays dynamic network status and offline synchronization alerts.
 */
export const DynamicIslandNotification: React.FC = () => {
  const { isOnline, pendingOfflineCount } = useUiStore();
  const [isExpanded, setIsExpanded] = useState(false);

  // If online and no pending items, hide island
  if (isOnline && pendingOfflineCount === 0) {
    return null;
  }

  return (
    <div className="fixed top-3 left-1/2 -translate-x-1/2 z-50 pointer-events-auto">
      <motion.div
        layout
        transition={{ type: 'spring', stiffness: 450, damping: 30 }}
        onClick={() => setIsExpanded(!isExpanded)}
        className="cursor-pointer bg-black/90 dark:bg-black/95 text-white backdrop-blur-xl border border-white/15 shadow-[0_8px_32px_rgba(0,0,0,0.36)] rounded-full px-4 py-2 flex items-center gap-3 overflow-hidden"
        style={{ borderRadius: isExpanded ? '20px' : '9999px' }}
      >
        <AnimatePresence mode="wait">
          {!isExpanded ? (
            /* Compact Pill Mode */
            <motion.div
              key="compact"
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              className="flex items-center gap-2.5 text-xs font-semibold"
            >
              {!isOnline ? (
                <>
                  <span className="w-2 h-2 rounded-full bg-amber-400 animate-pulse" />
                  <WifiSlash size={15} className="text-amber-400" weight="bold" />
                  <span className="font-mono text-[11px] tracking-wide">NGOẠI TUYẾN</span>
                </>
              ) : (
                <>
                  <span className="w-2 h-2 rounded-full bg-emerald-400 animate-pulse" />
                  <CloudArrowUp size={15} className="text-emerald-400" weight="bold" />
                  <span className="font-mono text-[11px] tracking-wide">{pendingOfflineCount} ĐỢI ĐỒNG BỘ</span>
                </>
              )}
            </motion.div>
          ) : (
            /* Expanded Island Mode */
            <motion.div
              key="expanded"
              initial={{ opacity: 0, scale: 0.95 }}
              animate={{ opacity: 1, scale: 1 }}
              exit={{ opacity: 0, scale: 0.95 }}
              className="py-1 px-1 space-y-3 min-w-[280px]"
            >
              <div className="flex items-center justify-between text-xs">
                <div className="flex items-center gap-2">
                  <span className={`w-2.5 h-2.5 rounded-full ${isOnline ? 'bg-emerald-400' : 'bg-amber-400'} animate-ping`} />
                  <span className="font-bold text-sm tracking-tight">
                    {isOnline ? 'Sẵn sàng đồng bộ' : 'Chế độ Hiện trường Ngoại tuyến'}
                  </span>
                </div>
                <span className="text-[10px] font-mono px-2 py-0.5 rounded-full bg-white/10 text-white/80">
                  Idempotent
                </span>
              </div>

              <p className="text-[11px] text-white/70 leading-relaxed">
                {!isOnline
                  ? 'Dữ liệu cân và tọa độ GPS vẫn được lưu cục bộ an toàn. Hệ thống sẽ tự động đồng bộ khi có kết nối.'
                  : `${pendingOfflineCount} phiếu thu mua đang chờ xác nhận ghi sổ cái tại Tiền Giang.`}
              </p>

              <div className="pt-1 flex items-center justify-between border-t border-white/10">
                <Link
                  to="/field/sync-queue"
                  onClick={(e) => e.stopPropagation()}
                  className="inline-flex items-center gap-1.5 text-xs text-emerald-400 hover:text-emerald-300 font-semibold"
                >
                  <span>Mở hàng đợi đồng bộ</span>
                  <ArrowRight size={13} weight="bold" />
                </Link>

                <span className="text-[10px] text-white/40">Nhấn để thu gọn</span>
              </div>
            </motion.div>
          )}
        </AnimatePresence>
      </motion.div>
    </div>
  );
};
