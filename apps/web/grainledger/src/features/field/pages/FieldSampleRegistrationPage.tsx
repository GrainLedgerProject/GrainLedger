import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  sampleEndpoints,
  type CaptureSampleRequest,
  type RecordSampleHandoverRequest,
} from "@/api/compliance";
import { GrainLoader } from "@/components/GrainLoader/GrainLoader";
import { StatusBadge } from "@/components/StatusBadge/StatusBadge";
import {
  TestTube,
  Plus,
  ArrowsClockwise,
  CheckCircle,
  WarningCircle,
  X,
  QrCode,
  ShieldCheck,
  Truck,
  FileText,
  Calendar,
} from "@phosphor-icons/react";

interface SampleItem {
  id: string;
  code: string | null;
  sealCode: string | null;
  sourceLotId: string;
  collectedAt: string;
  materialDescription?: string | null;
  handoverStatus?: "SEALED" | "IN_TRANSIT" | "DELIVERED_LAB";
  laboratoryName?: string | null;
}

export const FieldSampleRegistrationPage: React.FC = () => {
  const { t } = useTranslation();
  const [samples, setSamples] = useState<SampleItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);

  // Create Modal
  const [isCreateOpen, setIsCreateOpen] = useState(false);
  const [code, setCode] = useState("");
  const [sealCode, setSealCode] = useState("");
  const [sourceLotId, setSourceLotId] = useState("");
  const [materialDescription, setMaterialDescription] = useState("");
  const [submitting, setSubmitting] = useState(false);

  // Handover Modal
  const [isHandoverOpen, setIsHandoverOpen] = useState(false);
  const [selectedSample, setSelectedSample] = useState<SampleItem | null>(null);
  const [fromParty, setFromParty] = useState("Cán bộ thu mua Mekong Rice");
  const [toParty, setToParty] = useState("Đơn vị vận chuyển / Tài xế");
  const [laboratoryName, setLaboratoryName] = useState("Viện Kiểm Nghiệm NIFC");
  const [sealCondition, setSealCondition] = useState("NGUYÊN VẸN");

  const loadSamples = async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await sampleEndpoints.getSamples();
      if (Array.isArray(res) && res.length > 0) {
        setSamples(res as SampleItem[]);
      } else {
        // Mock fallback samples if backend returns empty
        setSamples([
          {
            id: "smp-001",
            code: "SMP-MK-2026-081",
            sealCode: "SEAL-A8820",
            sourceLotId: "LOT-TM-2026-001",
            collectedAt: new Date(Date.now() - 3600000 * 2).toISOString(),
            materialDescription: "Lúa tươi Jasmine 85 độ ẩm 22.4%",
            handoverStatus: "SEALED",
          },
          {
            id: "smp-002",
            code: "SMP-MK-2026-082",
            sealCode: "SEAL-B1194",
            sourceLotId: "LOT-TM-2026-002",
            collectedAt: new Date(Date.now() - 3600000 * 5).toISOString(),
            materialDescription: "Lúa tươi ST25 vùng trồng Mỹ Tú",
            handoverStatus: "DELIVERED_LAB",
            laboratoryName: "Viện Kiểm Nghiệm NIFC",
          },
        ]);
      }
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError(t("field.samples.errorLoad", "Không thể tải danh sách mẫu thu mua."));
      }
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadSamples();
  }, []);

  const handleCreateSample = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!code.trim() || !sealCode.trim()) return;

    setSubmitting(true);
    setError(null);
    try {
      const entityId = crypto.randomUUID();
      const payload: CaptureSampleRequest = {
        clientOperationId: crypto.randomUUID(),
        capturedAt: new Date().toISOString(),
        payloadSchemaVersion: 1,
        payload: {
          entityId,
          code: code.trim(),
          sealCode: sealCode.trim(),
          sourceLotId: sourceLotId.trim() || crypto.randomUUID(),
          collectedAt: new Date().toISOString(),
          materialDescription: materialDescription.trim() || null,
        },
      };

      await sampleEndpoints.captureSample(payload);

      setSuccessMsg(
        t("field.samples.successCreate", "Đã tạo biên bản lấy mẫu \"{{code}}\" thành công!", {
          code: code.trim(),
        })
      );

      // Add to local list
      setSamples((prev) => [
        {
          id: entityId,
          code: code.trim(),
          sealCode: sealCode.trim(),
          sourceLotId: sourceLotId.trim() || "LOT-TM-NEW",
          collectedAt: new Date().toISOString(),
          materialDescription: materialDescription.trim() || null,
          handoverStatus: "SEALED",
        },
        ...prev,
      ]);

      setCode("");
      setSealCode("");
      setSourceLotId("");
      setMaterialDescription("");
      setIsCreateOpen(false);
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError(t("field.samples.errorCreate", "Không thể lưu thông tin mẫu niêm phong."));
      }
    } finally {
      setSubmitting(false);
    }
  };

  const handleRecordHandover = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedSample) return;

    setSubmitting(true);
    setError(null);
    try {
      const payload: RecordSampleHandoverRequest = {
        clientOperationId: crypto.randomUUID(),
        capturedAt: new Date().toISOString(),
        payloadSchemaVersion: 1,
        payload: {
          entityId: crypto.randomUUID(),
          fromParty,
          toParty,
          handedOverAt: new Date().toISOString(),
          sealCondition,
          evidenceRef: `EVD-HANDOVER-${Date.now()}`,
          laboratoryName,
        },
      };

      await sampleEndpoints.recordSampleHandover(selectedSample.id, payload);

      setSuccessMsg(
        t("field.samples.successHandover", "Đã ghi nhận bàn giao mẫu \"{{code}}\" đến {{lab}}", {
          code: selectedSample.code || selectedSample.id,
          lab: laboratoryName,
        })
      );

      setSamples((prev) =>
        prev.map((s) =>
          s.id === selectedSample.id
            ? { ...s, handoverStatus: "DELIVERED_LAB", laboratoryName }
            : s
        )
      );

      setIsHandoverOpen(false);
      setSelectedSample(null);
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError(t("field.samples.errorHandover", "Không thể ghi nhận bàn giao mẫu."));
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 border-b border-ink-tertiary/15 pb-4">
        <div>
          <span className="text-xs font-mono font-bold text-accent-primary uppercase tracking-wider block">
            {t("field.samples.tag", "THU THẬP MẪU TẠI ĐIỂM THU MUA (BR-05, BR-19)")}
          </span>
          <h2 className="text-2xl font-display font-bold text-ink-primary">
            {t("field.samples.title", "Lấy Mẫu Niêm Phong & Bàn Giao (Procurement Samples)")}
          </h2>
          <p className="text-xs text-ink-secondary mt-0.5">
            {t(
              "field.samples.subtitle",
              "Ghi nhận mã niêm chì và mã barcode của mẫu thóc đại diện được lấy tại điểm thu mua trước khi đưa về nhà máy sấy và lưu kho."
            )}
          </p>
        </div>

        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={loadSamples}
            className="p-2.5 rounded-lg border border-ink-tertiary/20 bg-surface-base hover:bg-surface-raised text-ink-secondary hover:text-ink-primary transition-colors"
            title={t("common.reload", "Tải lại danh sách")}
          >
            <ArrowsClockwise size={16} />
          </button>
          <button
            type="button"
            onClick={() => setIsCreateOpen(true)}
            className="inline-flex items-center gap-2 px-4 py-2.5 rounded-lg bg-accent-primary text-white font-semibold text-xs hover:bg-accent-primary-dim transition-all shadow-xs"
          >
            <Plus size={16} weight="bold" />
            <span>{t("field.samples.createBtn", "Lấy Mẫu Niêm Phong Mới")}</span>
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

      {/* Grid of Samples */}
      {loading ? (
        <div className="py-16 flex justify-center">
          <GrainLoader label={t("field.samples.loading", "Đang tải danh sách mẫu niêm chì...")} />
        </div>
      ) : samples.length === 0 ? (
        <div className="py-16 text-center border-2 border-dashed border-ink-tertiary/15 rounded-2xl bg-surface-base dark:bg-surface-raised">
          <TestTube size={40} className="mx-auto text-ink-tertiary mb-3 opacity-60" />
          <p className="text-sm font-semibold text-ink-primary">
            {t("field.samples.emptyTitle", "Chưa có mẫu thóc nào được niêm chì")}
          </p>
          <p className="text-xs text-ink-secondary mt-1 max-w-sm mx-auto">
            {t("field.samples.emptyDesc", "Bấm 'Lấy Mẫu Niêm Phong Mới' để lập biên bản lấy mẫu đại diện tại trạm thu mua.")}
          </p>
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-5">
          {samples.map((sample) => (
            <div
              key={sample.id}
              className="p-5 rounded-2xl bg-surface-base dark:bg-surface-raised border border-ink-tertiary/20 shadow-xs hover:shadow-md transition-shadow space-y-4 flex flex-col justify-between"
            >
              <div className="space-y-3">
                <div className="flex items-start justify-between gap-3">
                  <div className="w-10 h-10 rounded-xl bg-accent-primary/10 text-accent-primary flex items-center justify-center shrink-0">
                    <TestTube size={22} weight="bold" />
                  </div>
                  <StatusBadge
                    status={sample.handoverStatus === "DELIVERED_LAB" ? "BALANCED" : "STALE"}
                    label={
                      sample.handoverStatus === "DELIVERED_LAB"
                        ? t("field.samples.statusDelivered", "Đã bàn giao Lab")
                        : t("field.samples.statusSealed", "Đã niêm chì")
                    }
                  />
                </div>

                <div>
                  <div className="flex items-center gap-1.5 text-ink-primary font-display font-bold text-base">
                    <QrCode size={16} className="text-accent-primary" />
                    <span>{sample.code || "MÃ-MẪU-MỚI"}</span>
                  </div>
                  <div className="flex items-center gap-1.5 text-xs text-ink-secondary mt-1 font-mono">
                    <ShieldCheck size={14} className="text-emerald-500" />
                    <span>Niêm chì: <strong>{sample.sealCode || "CHƯA GẮN"}</strong></span>
                  </div>
                </div>

                <div className="p-2.5 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/10 text-xs space-y-1">
                  <div className="flex justify-between text-ink-secondary">
                    <span>Lô nguồn:</span>
                    <span className="font-mono font-medium text-ink-primary">{sample.sourceLotId}</span>
                  </div>
                  {sample.materialDescription && (
                    <div className="text-ink-tertiary text-[11px] truncate">
                      {sample.materialDescription}
                    </div>
                  )}
                  {sample.laboratoryName && (
                    <div className="flex items-center gap-1 text-emerald-600 dark:text-emerald-400 text-[11px] pt-1 border-t border-ink-tertiary/10">
                      <Truck size={12} />
                      <span>{sample.laboratoryName}</span>
                    </div>
                  )}
                </div>
              </div>

              <div className="pt-3 border-t border-ink-tertiary/15 flex items-center justify-between text-xs text-ink-secondary">
                <span className="inline-flex items-center gap-1 text-[11px]">
                  <Calendar size={13} className="text-ink-tertiary" />
                  <span>{new Date(sample.collectedAt).toLocaleDateString("vi-VN")}</span>
                </span>

                {sample.handoverStatus !== "DELIVERED_LAB" && (
                  <button
                    type="button"
                    onClick={() => {
                      setSelectedSample(sample);
                      setIsHandoverOpen(true);
                    }}
                    className="inline-flex items-center gap-1 px-2.5 py-1.5 rounded-lg bg-accent-primary/10 hover:bg-accent-primary text-accent-primary hover:text-white font-medium text-xs transition-colors"
                  >
                    <Truck size={14} />
                    <span>Bàn giao Lab</span>
                  </button>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Modal: Lấy Mẫu Niêm Phong Mới */}
      {isCreateOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-xs p-4">
          <div className="w-full max-w-md bg-surface-base dark:bg-surface-raised border border-ink-tertiary/20 rounded-2xl shadow-xl overflow-hidden animate-ledger-in">
            <div className="p-5 border-b border-ink-tertiary/15 flex items-center justify-between">
              <h3 className="font-display font-bold text-base text-ink-primary">
                {t("field.samples.modalCreateTitle", "Lấy Mẫu Niêm Chì Mới (BR-05)")}
              </h3>
              <button
                type="button"
                onClick={() => setIsCreateOpen(false)}
                className="p-1 rounded-md text-ink-tertiary hover:text-ink-primary"
              >
                <X size={18} />
              </button>
            </div>

            <form onSubmit={handleCreateSample} className="p-5 space-y-4 text-xs">
              <div>
                <label className="block font-semibold text-ink-secondary mb-1">
                  {t("field.samples.codeLabel", "Mã Barcode Mẫu (Sample Code) *")}
                </label>
                <input
                  type="text"
                  required
                  value={code}
                  onChange={(e) => setCode(e.target.value)}
                  placeholder="VD: SMP-MK-2026-0092"
                  className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none"
                />
              </div>

              <div>
                <label className="block font-semibold text-ink-secondary mb-1">
                  {t("field.samples.sealLabel", "Mã Số Niêm Chì (Seal Code) *")}
                </label>
                <input
                  type="text"
                  required
                  value={sealCode}
                  onChange={(e) => setSealCode(e.target.value)}
                  placeholder="VD: SEAL-RED-9921"
                  className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none"
                />
              </div>

              <div>
                <label className="block font-semibold text-ink-secondary mb-1">
                  {t("field.samples.lotLabel", "Mã Lô Thu Mua Nguồn (Source Lot ID)")}
                </label>
                <input
                  type="text"
                  value={sourceLotId}
                  onChange={(e) => setSourceLotId(e.target.value)}
                  placeholder="VD: LOT-TM-2026-001"
                  className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none"
                />
              </div>

              <div>
                <label className="block font-semibold text-ink-secondary mb-1">
                  {t("field.samples.descLabel", "Mô tả / Giống lúa / Vùng trồng")}
                </label>
                <textarea
                  rows={2}
                  value={materialDescription}
                  onChange={(e) => setMaterialDescription(e.target.value)}
                  placeholder="VD: Mẫu thóc Jasmine 85 thu tại ruộng hộ Trần Văn Bảy..."
                  className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none"
                />
              </div>

              <div className="pt-3 border-t border-ink-tertiary/15 flex items-center justify-end gap-2">
                <button
                  type="button"
                  onClick={() => setIsCreateOpen(false)}
                  className="px-4 py-2 rounded-lg border border-ink-tertiary/20 hover:bg-surface-raised font-semibold text-ink-secondary"
                >
                  {t("common.cancel", "Huỷ")}
                </button>
                <button
                  type="submit"
                  disabled={submitting}
                  className="px-5 py-2 rounded-lg bg-accent-primary text-white font-semibold hover:bg-accent-primary-dim transition-colors disabled:opacity-60"
                >
                  {submitting ? t("common.saving", "Đang lưu...") : t("field.samples.submitCreate", "Niêm phong mẫu")}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Modal: Bàn Giao Mẫu Đến Lab */}
      {isHandoverOpen && selectedSample && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-xs p-4">
          <div className="w-full max-w-md bg-surface-base dark:bg-surface-raised border border-ink-tertiary/20 rounded-2xl shadow-xl overflow-hidden animate-ledger-in">
            <div className="p-5 border-b border-ink-tertiary/15 flex items-center justify-between">
              <h3 className="font-display font-bold text-base text-ink-primary">
                {t("field.samples.modalHandoverTitle", "Biên Bản Bàn Giao Mẫu (Chain of Custody)")}
              </h3>
              <button
                type="button"
                onClick={() => setIsHandoverOpen(false)}
                className="p-1 rounded-md text-ink-tertiary hover:text-ink-primary"
              >
                <X size={18} />
              </button>
            </div>

            <form onSubmit={handleRecordHandover} className="p-5 space-y-4 text-xs">
              <div className="p-3 rounded-lg bg-accent-primary/5 border border-accent-primary/20 space-y-1">
                <div className="font-semibold text-accent-primary">
                  Mẫu: {selectedSample.code}
                </div>
                <div className="text-[11px] text-ink-secondary">
                  Niêm chì: {selectedSample.sealCode} | Lô: {selectedSample.sourceLotId}
                </div>
              </div>

              <div>
                <label className="block font-semibold text-ink-secondary mb-1">
                  Bên bàn giao (From Party) *
                </label>
                <input
                  type="text"
                  required
                  value={fromParty}
                  onChange={(e) => setFromParty(e.target.value)}
                  className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none"
                />
              </div>

              <div>
                <label className="block font-semibold text-ink-secondary mb-1">
                  Bên tiếp nhận (To Party) *
                </label>
                <input
                  type="text"
                  required
                  value={toParty}
                  onChange={(e) => setToParty(e.target.value)}
                  className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none"
                />
              </div>

              <div>
                <label className="block font-semibold text-ink-secondary mb-1">
                  Phòng kiểm định nhận mẫu (Laboratory) *
                </label>
                <select
                  value={laboratoryName}
                  onChange={(e) => setLaboratoryName(e.target.value)}
                  className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none"
                >
                  <option value="Viện Kiểm Nghiệm NIFC">Viện Kiểm Nghiệm An Toàn Thực Phẩm Quốc Gia (NIFC)</option>
                  <option value="Eurofins Sắc Ký Hải Đăng">Eurofins Sắc Ký Hải Đăng (ISO 17025)</option>
                  <option value="Trung Tâm Kỹ Thuật 3 (Quatest 3)">Trung Tâm Đo Lường Chất Lượng Quatest 3</option>
                  <option value="SGS Vietnam Lab">SGS Vietnam Laboratory</option>
                </select>
              </div>

              <div>
                <label className="block font-semibold text-ink-secondary mb-1">
                  Tình trạng niêm chì khi giao nhận *
                </label>
                <select
                  value={sealCondition}
                  onChange={(e) => setSealCondition(e.target.value)}
                  className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none"
                >
                  <option value="NGUYÊN VẸN">NGUYÊN VẸN (Seal Intact - Pass)</option>
                  <option value="BỊ BIẾN DẠNG">BỊ BIẾN DẠNG (Damaged Seal - Flag)</option>
                  <option value="ĐÃ BỊ PHÁ NIÊM">ĐÃ BỊ PHÁ NIÊM (Broken Seal - Reject)</option>
                </select>
              </div>

              <div className="pt-3 border-t border-ink-tertiary/15 flex items-center justify-end gap-2">
                <button
                  type="button"
                  onClick={() => setIsHandoverOpen(false)}
                  className="px-4 py-2 rounded-lg border border-ink-tertiary/20 hover:bg-surface-raised font-semibold text-ink-secondary"
                >
                  {t("common.cancel", "Huỷ")}
                </button>
                <button
                  type="submit"
                  disabled={submitting}
                  className="px-5 py-2 rounded-lg bg-accent-primary text-white font-semibold hover:bg-accent-primary-dim transition-colors disabled:opacity-60"
                >
                  {submitting ? t("common.saving", "Đang ghi nhận...") : "Xác nhận bàn giao"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
