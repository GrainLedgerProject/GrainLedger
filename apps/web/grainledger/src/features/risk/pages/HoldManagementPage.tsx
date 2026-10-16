import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { ledgerEndpoints } from "@/api/ledger";
import { GrainLoader } from "@/components/GrainLoader/GrainLoader";
import { StatusBadge } from "@/components/StatusBadge/StatusBadge";
import {
  LockSimple,
  ShieldWarning,
  Plus,
  ArrowsClockwise,
  CheckCircle,
  WarningCircle,
  X,
  FileText,
  Calendar,
  Warehouse,
  Flask,
} from "@phosphor-icons/react";

interface HoldOrder {
  id: string;
  holdCode?: string;
  lotCode?: string;
  reasonCode: string;
  notes?: string;
  issuedAt: string;
  status: "ACTIVE_HOLD" | "QUARANTINED" | "UNDER_RETEST";
  lotQuantityKg?: string;
  ownerName?: string;
}

export const HoldManagementPage: React.FC = () => {
  const { t } = useTranslation();
  const [holds, setHolds] = useState<HoldOrder[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);

  // Issue Hold Modal
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [targetLotCode, setTargetLotCode] = useState("LOT-2026-S1");
  const [reasonCode, setReasonCode] = useState("MRL_TRICYCLAZOLE_EXCEEDED");
  const [notes, setNotes] = useState(
    "Kết quả thử nghiệm phòng lab LOQ vượt ngưỡng 0.010 mg/kg của thị trường EU.",
  );
  const [submitting, setSubmitting] = useState(false);

  const loadHolds = async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await ledgerEndpoints.getHolds().catch(() => []);
      const items = Array.isArray(res) ? res : (res as any)?.items || [];
      setHolds(items);
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError(t("risk.hold.errLoad", "Không thể tải danh sách lệnh HOLD."));
      }
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadHolds();
  }, []);

  const handleIssueHold = async (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitting(true);
    setError(null);

    try {
      await ledgerEndpoints.createHold({
        lotId: crypto.randomUUID(),
        reasonCode,
        notes,
        issuedAt: new Date().toISOString(),
      });

      setSuccessMsg(
        t(
          "risk.hold.issuedSuccess",
          "Đã ban hành Lệnh HOLD phong toả khẩn cấp cho lô {{lot}}",
          {
            lot: targetLotCode,
          },
        ),
      );
      setIsModalOpen(false);
      await loadHolds();
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError(t("risk.hold.errCreate", "Không thể ban hành lệnh HOLD."));
      }
    } finally {
      setSubmitting(false);
    }
  };

  const handleRespondHold = async (hold: HoldOrder, responseKind: string) => {
    try {
      await ledgerEndpoints.respondToHold(hold.id, {
        responseKind,
        respondedAt: new Date().toISOString(),
        notes: `Ghi nhận chuyển kho cách ly vật lý theo quy định BR-15`,
      });
      setSuccessMsg(
        t(
          "risk.hold.updatedSuccess",
          "Đã cập nhật xử lý lệnh HOLD cho lô {{lot}}",
          {
            lot: hold.lotCode || hold.id,
          },
        ),
      );
      await loadHolds();
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError(
          t("risk.hold.errRespond", "Không thể cập nhật phản hồi HOLD."),
        );
      }
    }
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 border-b border-ink-tertiary/15 pb-4">
        <div>
          <span className="text-xs font-mono font-bold text-accent-danger uppercase tracking-wider block">
            {t("risk.hold.badge", "QUẢN LÝ PHONG TỎA & CẢNH BÁO RỦI RO")}
          </span>
          <h2 className="text-2xl font-display font-bold text-ink-primary">
            {t("risk.hold.title", "Lệnh Giữ Phong Toả An Toàn (HOLD Orders)")}
          </h2>
          <p className="text-xs text-ink-secondary mt-0.5">
            {t(
              "risk.hold.subtitle",
              "Ngăn chặn xuất xưởng khi phát hiện vi phạm MRL hoặc sai lệch chất lượng. Tuyệt đối không cho phép RELEASE tự do mà phải có quy trình cách ly vật lý.",
            )}
          </p>
        </div>

        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={loadHolds}
            className="p-2.5 rounded-lg border border-ink-tertiary/20 bg-surface-base hover:bg-surface-raised text-ink-secondary hover:text-ink-primary transition-colors"
            title={t("common.refresh", "Tải lại dữ liệu")}
          >
            <ArrowsClockwise size={16} />
          </button>
          <button
            type="button"
            onClick={() => setIsModalOpen(true)}
            className="inline-flex items-center gap-2 px-4 py-2.5 rounded-lg bg-accent-danger text-white font-semibold text-xs hover:bg-red-700 transition-all shadow-xs"
          >
            <LockSimple size={16} weight="bold" />
            <span>{t("risk.hold.newHold", "Ban hành Lệnh HOLD mới")}</span>
          </button>
        </div>
      </div>

      {/* Alerts */}
      {error && (
        <div className="p-3.5 rounded-lg bg-functional-rejected-bg border border-functional-rejected-border text-accent-danger text-xs flex items-center gap-2">
          <WarningCircle size={18} weight="fill" className="shrink-0" />
          <span>{error}</span>
        </div>
      )}
      {successMsg && (
        <div className="p-3.5 rounded-lg bg-emerald-50 dark:bg-emerald-950/40 border border-emerald-500/30 text-[#2F6B3A] dark:text-emerald-300 text-xs flex items-center gap-2">
          <CheckCircle size={18} weight="fill" className="shrink-0" />
          <span>{successMsg}</span>
        </div>
      )}

      {/* Warning Policy Box */}
      <div className="p-4 rounded-xl bg-functional-hold-bg dark:bg-functional-hold-bg/20 border border-functional-hold-border/40 flex items-start gap-3">
        <ShieldWarning
          size={22}
          className="text-accent-danger shrink-0 mt-0.5"
          weight="fill"
        />
        <div className="text-xs space-y-1">
          <h4 className="font-bold text-accent-danger">
            {t("risk.hold.policyTitle", "Phong toả và Cô lập Dứt khoát")}
          </h4>
          <p className="text-ink-secondary leading-relaxed">
            {t(
              "risk.hold.policyDesc",
              "Khi phát lệnh HOLD, lô hàng hoặc container bị khóa lập tức ở tầng sổ cái và không thể đưa vào bất kỳ phương án phối trộn hay xuất xưởng nào. Lệnh chỉ kết thúc khi có kết quả xét nghiệm lại đạt chuẩn hoặc lập phiếu chuyển phân loại thành phẩm.",
            )}
          </p>
        </div>
      </div>

      {/* Table */}
      <div className="rounded-xl border border-ink-tertiary/20 bg-surface-base dark:bg-surface-raised overflow-hidden shadow-xs">
        {loading ? (
          <div className="py-16 flex justify-center">
            <GrainLoader
              label={t(
                "risk.hold.loading",
                "Đang đối soát danh sách phong toả...",
              )}
            />
          </div>
        ) : holds.length === 0 ? (
          <div className="py-16 text-center space-y-2">
            <CheckCircle
              size={40}
              className="mx-auto text-accent-primary opacity-60"
            />
            <p className="text-sm font-semibold text-ink-primary">
              {t(
                "risk.hold.emptyTitle",
                "Không có lô hàng nào bị phong toả HOLD",
              )}
            </p>
            <p className="text-xs text-ink-secondary max-w-sm mx-auto">
              {t(
                "risk.hold.emptyDesc",
                "Toàn bộ tồn kho hiện tại đều an toàn và đạt tiêu chuẩn MRL theo các thị trường xuất khẩu.",
              )}
            </p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs">
              <thead className="bg-surface-raised dark:bg-surface-overlay border-b border-ink-tertiary/20 text-ink-secondary font-semibold uppercase tracking-wider text-[11px]">
                <tr>
                  <th className="py-3 px-4">
                    {t("risk.hold.thLot", "Lô hàng bị phong toả")}
                  </th>
                  <th className="py-3 px-4">
                    {t("risk.hold.thReason", "Lý do phát lệnh HOLD")}
                  </th>
                  <th className="py-3 px-4">
                    {t("risk.hold.thIssuedAt", "Thời điểm ban hành")}
                  </th>
                  <th className="py-3 px-4">
                    {t("risk.hold.thNotes", "Ghi chú bằng chứng")}
                  </th>
                  <th className="py-3 px-4">
                    {t("risk.hold.thStatus", "Trạng thái")}
                  </th>
                  <th className="py-3 px-4 text-right">
                    {t("risk.hold.thActions", "Thao tác xử lý")}
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-ink-tertiary/10">
                {holds.map((h) => (
                  <tr
                    key={h.id}
                    className="hover:bg-surface-raised/60 dark:hover:bg-surface-overlay/40 transition-colors"
                  >
                    <td className="py-3 px-4 font-mono font-bold text-accent-danger">
                      <span className="inline-flex items-center gap-1.5">
                        <LockSimple size={14} weight="fill" />
                        <span>{h.lotCode || `LOT-${h.id.slice(0, 8)}`}</span>
                      </span>
                    </td>
                    <td className="py-3 px-4 font-semibold text-ink-primary">
                      {h.reasonCode}
                    </td>
                    <td className="py-3 px-4 font-mono text-ink-secondary">
                      <span className="inline-flex items-center gap-1">
                        <Calendar size={13} />
                        <span>
                          {new Date(h.issuedAt).toLocaleDateString("vi-VN")}
                        </span>
                      </span>
                    </td>
                    <td className="py-3 px-4 text-ink-secondary max-w-xs truncate">
                      {h.notes ||
                        t(
                          "risk.hold.defaultNotes",
                          "Vượt ngưỡng an toàn phòng thí nghiệm",
                        )}
                    </td>
                    <td className="py-3 px-4">
                      <StatusBadge
                        status="HOLD"
                        label={t("risk.hold.statusActive", "ĐANG PHONG TOẢ")}
                      />
                    </td>
                    <td className="py-3 px-4 text-right space-x-1">
                      <button
                        type="button"
                        onClick={() =>
                          handleRespondHold(h, "TRANSFER_QUARANTINE")
                        }
                        className="px-2.5 py-1 rounded bg-amber-600 text-white font-semibold text-[11px] hover:bg-amber-700"
                        title={t(
                          "risk.hold.btnTransferQuarantine",
                          "Xác nhận chuyển kho cách ly vật lý",
                        )}
                      >
                        {t(
                          "risk.hold.btnTransferQuarantine",
                          "Chuyển kho cách ly",
                        )}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Modal */}
      {isModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-xs p-4">
          <div className="w-full max-w-md bg-surface-base dark:bg-surface-raised border border-ink-tertiary/20 rounded-2xl shadow-xl overflow-hidden animate-ledger-in">
            <div className="p-5 border-b border-ink-tertiary/15 flex items-center justify-between">
              <h3 className="font-display font-bold text-base text-accent-danger flex items-center gap-2">
                <LockSimple size={18} weight="fill" />
                <span>
                  {t("risk.hold.modalTitle", "Ban hành Lệnh HOLD Khẩn Cấp")}
                </span>
              </h3>
              <button
                type="button"
                onClick={() => setIsModalOpen(false)}
                className="p-1 rounded-md text-ink-tertiary hover:text-ink-primary"
              >
                <X size={18} />
              </button>
            </div>

            <form onSubmit={handleIssueHold} className="p-5 space-y-4 text-xs">
              <div>
                <label className="block font-semibold text-ink-secondary mb-1">
                  {t(
                    "risk.hold.lotCodeLabel",
                    "Mã Lô hàng hoặc Container cần phong toả *",
                  )}
                </label>
                <input
                  type="text"
                  required
                  value={targetLotCode}
                  onChange={(e) => setTargetLotCode(e.target.value)}
                  placeholder="VD: LOT-2026-S1, CONT-20FT-001"
                  className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-danger focus:outline-none font-mono font-bold"
                />
              </div>

              <div>
                <label className="block font-semibold text-ink-secondary mb-1">
                  {t(
                    "risk.hold.reasonCodeLabel",
                    "Mã Lý do phong toả (Reason Code) *",
                  )}
                </label>
                <select
                  value={reasonCode}
                  onChange={(e) => setReasonCode(e.target.value)}
                  className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-danger focus:outline-none"
                >
                  <option value="MRL_TRICYCLAZOLE_EXCEEDED">
                    {t(
                      "risk.hold.reasonMrlTricyclazole",
                      "Vượt ngưỡng MRL Tricyclazole (EU LOQ 0.010)",
                    )}
                  </option>
                  <option value="MRL_HEXACONAZOLE_EXCEEDED">
                    {t(
                      "risk.hold.reasonMrlHexaconazole",
                      "Vượt ngưỡng MRL Hexaconazole",
                    )}
                  </option>
                  <option value="MOISTURE_EXCESSIVE">
                    {t(
                      "risk.hold.reasonMoistureExcessive",
                      "Độ ẩm vượt quá tiêu chuẩn bảo quản (>15%)",
                    )}
                  </option>
                  <option value="BROKEN_PERCENT_MISMATCH">
                    {t(
                      "risk.hold.reasonBrokenMismatch",
                      "Tỷ lệ hạt tấm không đồng nhất",
                    )}
                  </option>
                  <option value="CONTAMINATION_SUSPECTED">
                    {t(
                      "risk.hold.reasonContamination",
                      "Nghi ngờ nhiễm bẩn cơ học hoặc sâu mọt",
                    )}
                  </option>
                </select>
              </div>

              <div>
                <label className="block font-semibold text-ink-secondary mb-1">
                  {t(
                    "risk.hold.notesLabel",
                    "Ghi chú chi tiết & Căn cứ phiếu thử nghiệm *",
                  )}
                </label>
                <textarea
                  required
                  rows={3}
                  value={notes}
                  onChange={(e) => setNotes(e.target.value)}
                  className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-danger focus:outline-none"
                />
              </div>

              <div className="pt-3 border-t border-ink-tertiary/15 flex items-center justify-end gap-2">
                <button
                  type="button"
                  onClick={() => setIsModalOpen(false)}
                  className="px-4 py-2 rounded-lg border border-ink-tertiary/20 hover:bg-surface-raised font-semibold text-ink-secondary"
                >
                  {t("common.cancel", "Huỷ")}
                </button>
                <button
                  type="submit"
                  disabled={submitting}
                  className="px-5 py-2 rounded-lg bg-accent-danger text-white font-semibold hover:bg-red-700 transition-colors disabled:opacity-60"
                >
                  {submitting
                    ? t("risk.hold.submitting", "Đang phát lệnh...")
                    : t("risk.hold.btnSubmit", "Ban hành Lệnh HOLD")}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
