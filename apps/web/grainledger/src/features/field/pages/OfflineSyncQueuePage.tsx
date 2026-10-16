/**
 * @screen SC-14
 * @feature F07 (Offline Capture & Sync)
 * @roles CUSTOMER_PROCUREMENT_OFFICER
 * @description Review records captured offline and synchronize idempotently with server.
 */
import { useUiStore } from "@/app/store/uiStore";
import { useTranslation } from "react-i18next";
import { GrainLoader } from "@/components/GrainLoader/GrainLoader";
import { MassValue } from "@/components/MassValue/MassValue";
import { StatusBadge } from "@/components/StatusBadge/StatusBadge";
import {
  ArrowsClockwise,
  CheckCircle,
  CloudCheck,
  CloudSlash,
  Drop,
  HandCoins,
  MapPin,
  TestTube,
  User,
  WarningCircle,
} from "@phosphor-icons/react";
import React, { useState } from "react";

interface QueuedRecord {
  id: string;
  type: "FARMER" | "PLOT" | "DECLARATION" | "INTAKE" | "SAMPLE";
  title: string;
  details: string;
  massKg?: number;
  capturedAt: string;
  status: "PENDING" | "ACCEPTED" | "DUPLICATE" | "CONFLICT";
}

export const OfflineSyncQueuePage: React.FC = () => {
  const { t } = useTranslation();
  const { isOnline, pendingOfflineCount, setPendingOfflineCount } =
    useUiStore();
  const [isSyncing, setIsSyncing] = useState(false);
  const [syncSummary, setSyncSummary] = useState<string | null>(null);

  const [records, setRecords] = useState<QueuedRecord[]>([
    {
      id: "REC-OFF-01",
      type: "INTAKE",
      title: "Phiếu thu mua lúa tươi — Thửa L1 (50.000 kg)",
      details: "Nông hộ: Nguyễn Văn An · Độ ẩm: 26,5% · Giống lúa: OM5451",
      massKg: 50000,
      capturedAt: "16/10/2026 08:30:15",
      status: "PENDING",
    },
    {
      id: "REC-OFF-02",
      type: "SAMPLE",
      title: "Mẫu niêm phong đại diện SMP-TG-01",
      details:
        "Mã niêm chì: SEAL-8812 · Lô nguồn: L1 · Người lấy mẫu: Võ Minh Thu Mua",
      capturedAt: "16/10/2026 08:45:00",
      status: "PENDING",
    },
    {
      id: "REC-OFF-03",
      type: "DECLARATION",
      title: "Khai báo phun thuốc BVTV — Thửa L2",
      details:
        "Hoạt chất: Isoprothiolane (Fuji-One) · Tỷ lệ: 1.2 L/ha · Cách ly: 14 ngày",
      capturedAt: "16/10/2026 09:10:22",
      status: "PENDING",
    },
  ]);

  const handleSyncAll = async () => {
    if (!isOnline) return;
    setIsSyncing(true);
    setSyncSummary(null);

    // Simulate server synchronization roundtrip
    await new Promise((res) => setTimeout(res, 900));

    setRecords((prev) =>
      prev.map((r) => ({
        ...r,
        status: "ACCEPTED",
      })),
    );

    setPendingOfflineCount(0);
    setIsSyncing(false);
    setSyncSummary(
      t(
        "field.sync.syncSummaryText",
        "Đồng bộ hoàn tất: 3 đã nhận, 0 trùng lặp, 0 bị từ chối, 0 xung đột."
      )
    );
  };

  const getIcon = (type: QueuedRecord["type"]) => {
    switch (type) {
      case "INTAKE":
        return <HandCoins size={18} className="text-accent-primary" />;
      case "SAMPLE":
        return <TestTube size={18} className="text-amber-600" />;
      case "DECLARATION":
        return <Drop size={18} className="text-accent-info" />;
      case "PLOT":
        return <MapPin size={18} className="text-purple-600" />;
      case "FARMER":
        return <User size={18} className="text-emerald-600" />;
    }
  };

  return (
    <div className="max-w-4xl mx-auto space-y-6">
      <div className="border-b border-ink-tertiary/15 pb-4 flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl font-serif font-bold text-ink-primary">
            {t("field.sync.title", "Hàng đợi & Đồng bộ ngoại tuyến (Offline Queue)")}
          </h1>
          <p className="text-xs text-ink-secondary mt-0.5">
            {t(
              "field.sync.subtitle",
              "Duyệt và đẩy các bản ghi thu mua hiện trường lên máy chủ sổ cái một cách an toàn và chống trùng lặp (Idempotent)."
            )}
          </p>
        </div>

        <button
          type="button"
          disabled={!isOnline || isSyncing || pendingOfflineCount === 0}
          onClick={handleSyncAll}
          className="inline-flex items-center gap-2 px-4 py-2.5 rounded-md bg-accent-primary text-white font-semibold text-xs hover:bg-accent-primary-dim transition-all shadow-xs disabled:opacity-50"
        >
          {isSyncing ? (
            <GrainLoader size="sm" label={t("field.sync.syncing", "Đang đẩy dữ liệu...")} />
          ) : (
            <>
              <ArrowsClockwise size={16} />
              <span>
                {t("field.sync.syncAllBtn", "Đồng bộ toàn bộ ({{count}})", {
                  count: pendingOfflineCount,
                })}
              </span>
            </>
          )}
        </button>
      </div>

      {/* Network Status Header Bar */}
      <div
        className={`p-4 rounded-lg border flex items-center justify-between flex-wrap gap-3 ${
          isOnline
            ? "bg-emerald-50 dark:bg-emerald-950/30 border-accent-primary/25 text-ink-primary"
            : "bg-red-50 dark:bg-red-950/30 border-accent-danger/30 text-accent-danger"
        }`}
      >
        <div className="flex items-center gap-3 text-xs">
          {isOnline ? (
            <CloudCheck size={24} className="text-accent-primary" />
          ) : (
            <CloudSlash size={24} className="text-accent-danger" />
          )}
          <div>
            <span className="font-bold block">
              {isOnline
                ? t("field.sync.onlineStatus", "Thiết bị đang có kết nối Internet (Online)")
                : t("field.sync.offlineStatus", "Thiết bị đang mất mạng (Offline)")}
            </span>
            <span className="text-ink-secondary">
              {isOnline
                ? t(
                    "field.sync.onlineDesc",
                    "Sẵn sàng gửi các bản ghi thu mua và mẫu niêm phong lên máy chủ nhà máy."
                  )
                : t(
                    "field.sync.offlineDesc",
                    "Mọi dữ liệu vẫn được lưu trữ bền vững trong IndexedDB / LocalStorage."
                  )}
            </span>
          </div>
        </div>

        <span className="font-mono text-xs font-bold px-2.5 py-1 rounded-full bg-surface-base border border-ink-tertiary/20">
          {t("field.sync.pendingRecords", "{{count}} bản ghi chờ", {
            count: pendingOfflineCount,
          })}
        </span>
      </div>

      {/* Sync Success Summary (MSG03) */}
      {syncSummary && (
        <div className="p-4 rounded-lg bg-functional-balanced-bg border border-functional-balanced-border text-accent-primary text-xs flex items-center gap-2.5">
          <CheckCircle size={20} weight="fill" />
          <span className="font-semibold">MSG03: {syncSummary}</span>
        </div>
      )}

      {/* List of Queued Records */}
      <div className="bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 rounded-lg overflow-hidden shadow-xs">
        <div className="px-5 py-3 border-b border-ink-tertiary/15 font-semibold text-xs text-ink-primary flex items-center justify-between">
          <span>{t("field.sync.queueListTitle", "Danh sách bản ghi hiện trường")}</span>
          <span className="text-ink-tertiary">{t("field.sync.operationKey", "Khóa vận hành: OP-KEY-2026")}</span>
        </div>

        <div className="divide-y divide-ink-tertiary/10">
          {records.map((item) => (
            <div
              key={item.id}
              className="p-4 flex items-start gap-3.5 hover:bg-surface-base/50 transition-colors"
            >
              <div className="p-2 rounded-md bg-surface-base border border-ink-tertiary/15 shrink-0 mt-0.5">
                {getIcon(item.type)}
              </div>

              <div className="flex-1 space-y-1">
                <div className="flex items-center justify-between flex-wrap gap-2">
                  <div className="flex items-center gap-2">
                    <span className="font-mono text-xs font-bold text-accent-primary">
                      {item.id}
                    </span>
                    <h3 className="font-semibold text-sm text-ink-primary">
                      {item.title}
                    </h3>
                  </div>

                  <StatusBadge
                    status={
                      item.status === "ACCEPTED" ? "COMPLIANT" : "PENDING"
                    }
                    label={
                      item.status === "ACCEPTED"
                        ? t("field.sync.statusAccepted", "Đã nhận vào sổ cái")
                        : t("field.sync.statusPending", "Chờ đồng bộ")
                    }
                    size="sm"
                  />
                </div>

                <p className="text-xs text-ink-secondary">{item.details}</p>

                <div className="flex items-center gap-4 pt-1 text-[11px] text-ink-tertiary font-mono">
                  {item.massKg && (
                    <span>
                      {t("field.sync.massLabel", "Khối lượng:")}{" "}
                      <MassValue value={item.massKg} decimals={3} />
                    </span>
                  )}
                  <span>
                    {t("field.sync.capturedAtLabel", "Thời điểm ghi nhận tại ruộng:")}{" "}
                    {item.capturedAt}
                  </span>
                </div>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
};
