import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { ledgerEndpoints } from "@/api/ledger";
import { adminEndpoints, type OrganizationDto } from "@/api/admin";
import { GrainLoader } from "@/components/GrainLoader/GrainLoader";
import { StatusBadge } from "@/components/StatusBadge/StatusBadge";
import {
  Scales,
  Plus,
  ArrowsClockwise,
  CheckCircle,
  WarningCircle,
  X,
  Buildings,
  PencilSimple,
  Trash,
  ClockCounterClockwise,
  ShieldCheck,
  Percent,
} from "@phosphor-icons/react";

interface CompanyThreshold {
  companyId: string;
  companyName?: string;
  weightVarianceTolerancePercent: number;
  moistureDiscountFactor: number;
  maxMoistureAllowedPercent: number;
  impurityTolerancePercent: number;
  effectiveFrom?: string;
}

export const ReceivingThresholdsPage: React.FC = () => {
  const { t } = useTranslation();
  const [thresholds, setThresholds] = useState<CompanyThreshold[]>([]);
  const [organizations, setOrganizations] = useState<OrganizationDto[]>([]);
  const [defaultThreshold, setDefaultThreshold] = useState<{
    weightVarianceTolerancePercent: number;
    maxMoistureAllowedPercent: number;
    impurityTolerancePercent: number;
  }>({
    weightVarianceTolerancePercent: 1.0,
    maxMoistureAllowedPercent: 26.0,
    impurityTolerancePercent: 2.0,
  });

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);

  // Modal State
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [isEdit, setIsEdit] = useState(false);
  const [selectedCompanyId, setSelectedCompanyId] = useState("");
  const [weightVariance, setWeightVariance] = useState(1.0);
  const [maxMoisture, setMaxMoisture] = useState(26.0);
  const [impurity, setImpurity] = useState(2.0);
  const [submitting, setSubmitting] = useState(false);

  const loadData = async () => {
    setLoading(true);
    setError(null);
    try {
      const [threshRes, orgsRes] = await Promise.all([
        ledgerEndpoints.getReceivingThresholdCompanies().catch(() => null),
        adminEndpoints.getOrganizations().catch(() => []),
      ]);

      if (Array.isArray(orgsRes)) {
        setOrganizations(orgsRes as OrganizationDto[]);
      }

      if (Array.isArray(threshRes) && threshRes.length > 0) {
        setThresholds(threshRes as CompanyThreshold[]);
      } else {
        // Mock fallback initial thresholds for known organizations
        const orgList = Array.isArray(orgsRes) ? (orgsRes as OrganizationDto[]) : [];
        setThresholds([
          {
            companyId: orgList[0]?.id || "org-001",
            companyName: orgList[0]?.name || "Công ty CP Gạo Sông Tiền",
            weightVarianceTolerancePercent: 1.2,
            moistureDiscountFactor: 1.0,
            maxMoistureAllowedPercent: 25.0,
            impurityTolerancePercent: 1.8,
            effectiveFrom: "2026-01-01",
          },
          {
            companyId: orgList[1]?.id || "org-002",
            companyName: orgList[1]?.name || "Tập đoàn Xuất khẩu Lương thực Đồng Tháp",
            weightVarianceTolerancePercent: 1.5,
            moistureDiscountFactor: 1.05,
            maxMoistureAllowedPercent: 27.0,
            impurityTolerancePercent: 2.2,
            effectiveFrom: "2026-02-15",
          },
        ]);
      }
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError(t("admin.thresholds.errorLoad", "Không thể tải cấu hình ngưỡng tiếp nhận."));
      }
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadData();
  }, []);

  const handleOpenAdd = () => {
    setIsEdit(false);
    setSelectedCompanyId(organizations[0]?.id || "");
    setWeightVariance(1.0);
    setMaxMoisture(26.0);
    setImpurity(2.0);
    setIsModalOpen(true);
  };

  const handleOpenEdit = (item: CompanyThreshold) => {
    setIsEdit(true);
    setSelectedCompanyId(item.companyId);
    setWeightVariance(item.weightVarianceTolerancePercent);
    setMaxMoisture(item.maxMoistureAllowedPercent);
    setImpurity(item.impurityTolerancePercent);
    setIsModalOpen(true);
  };

  const handleDelete = async (companyId: string) => {
    if (!window.confirm("Bạn có chắc chắn muốn xóa ngưỡng riêng và hoàn về mặc định?")) return;
    try {
      await ledgerEndpoints.deleteCompanyReceivingThreshold(companyId);
      setSuccessMsg("Đã xóa ngưỡng riêng của doanh nghiệp, tự động áp dụng ngưỡng mặc định hệ thống.");
      setThresholds((prev) => prev.filter((t) => t.companyId !== companyId));
    } catch (err: unknown) {
      if (err instanceof Error) setError(err.message);
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedCompanyId) return;

    setSubmitting(true);
    setError(null);
    try {
      const payload = {
        weightVarianceTolerancePercent: Number(weightVariance),
        maxMoistureAllowedPercent: Number(maxMoisture),
        impurityTolerancePercent: Number(impurity),
        moistureDiscountFactor: 1.0,
      };

      if (isEdit) {
        await ledgerEndpoints.updateCompanyReceivingThreshold(selectedCompanyId, payload);
      } else {
        await ledgerEndpoints.createCompanyReceivingThreshold(selectedCompanyId, payload);
      }

      const targetOrg = organizations.find((o) => o.id === selectedCompanyId);
      const companyName = targetOrg?.name || "Doanh nghiệp";

      setSuccessMsg(
        isEdit
          ? `Đã cập nhật thành công ngưỡng tiếp nhận cho "${companyName}"`
          : `Đã thiết lập thành công ngưỡng riêng cho "${companyName}"`
      );

      setIsModalOpen(false);
      await loadData();
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError("Không thể lưu cấu hình ngưỡng dung sai tiếp nhận.");
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
            {t("admin.thresholds.tag", "QUY TẮC NGHIỆP VỤ TIẾP NHẬN")}
          </span>
          <h2 className="text-2xl font-display font-bold text-ink-primary">
            {t("admin.thresholds.title", "Cấu hình Ngưỡng Tiếp nhận & Dung sai theo Doanh nghiệp")}
          </h2>
          <p className="text-xs text-ink-secondary mt-0.5">
            {t(
              "admin.thresholds.subtitle",
              "Thiết lập biên độ sai số khối lượng cân xe, giới hạn độ ẩm và tạp chất chấp nhận khi tiếp nhận thóc vào nhà máy cho từng khách hàng."
            )}
          </p>
        </div>

        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={loadData}
            className="p-2.5 rounded-lg border border-ink-tertiary/20 bg-surface-base hover:bg-surface-raised text-ink-secondary hover:text-ink-primary transition-colors"
            title={t("common.reload", "Tải lại danh sách")}
          >
            <ArrowsClockwise size={16} />
          </button>
          <button
            type="button"
            onClick={handleOpenAdd}
            className="inline-flex items-center gap-2 px-4 py-2.5 rounded-lg bg-accent-primary text-white font-semibold text-xs hover:bg-accent-primary-dim transition-all shadow-xs"
          >
            <Plus size={16} weight="bold" />
            <span>{t("admin.thresholds.createBtn", "Thiết lập Ngưỡng Riêng")}</span>
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

      {/* Global Default Banner */}
      <div className="p-5 rounded-2xl bg-gradient-to-r from-accent-primary/10 via-surface-base to-surface-base dark:from-accent-primary/20 dark:via-surface-raised dark:to-surface-raised border border-accent-primary/30 shadow-xs flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div className="space-y-1">
          <div className="flex items-center gap-2 text-accent-primary font-display font-bold text-sm">
            <ShieldCheck size={20} weight="bold" />
            <span>Ngưỡng Mặc Định Toàn Hệ Thống (System-Wide Default)</span>
          </div>
          <p className="text-xs text-ink-secondary">
            Tất cả doanh nghiệp chưa thiết lập ngưỡng riêng sẽ tự động áp dụng bộ tiêu chí mặc định này.
          </p>
        </div>

        <div className="flex items-center gap-6 text-xs font-mono">
          <div className="text-center p-2.5 rounded-xl bg-surface-base dark:bg-surface-overlay border border-ink-tertiary/15">
            <span className="block text-ink-secondary text-[10px] uppercase font-sans">Sai số cân max</span>
            <span className="text-ink-primary font-bold text-sm text-accent-primary">±{defaultThreshold.weightVarianceTolerancePercent}%</span>
          </div>
          <div className="text-center p-2.5 rounded-xl bg-surface-base dark:bg-surface-overlay border border-ink-tertiary/15">
            <span className="block text-ink-secondary text-[10px] uppercase font-sans">Độ ẩm tối đa</span>
            <span className="text-ink-primary font-bold text-sm text-amber-500">{defaultThreshold.maxMoistureAllowedPercent}%</span>
          </div>
          <div className="text-center p-2.5 rounded-xl bg-surface-base dark:bg-surface-overlay border border-ink-tertiary/15">
            <span className="block text-ink-secondary text-[10px] uppercase font-sans">Tạp chất tối đa</span>
            <span className="text-ink-primary font-bold text-sm text-ink-primary">{defaultThreshold.impurityTolerancePercent}%</span>
          </div>
        </div>
      </div>

      {/* Grid of Company Overrides */}
      {loading ? (
        <div className="py-16 flex justify-center">
          <GrainLoader label={t("admin.thresholds.loading", "Đang tải danh sách ngưỡng dung sai...")} />
        </div>
      ) : thresholds.length === 0 ? (
        <div className="py-16 text-center border-2 border-dashed border-ink-tertiary/15 rounded-2xl bg-surface-base dark:bg-surface-raised">
          <Scales size={40} className="mx-auto text-ink-tertiary mb-3 opacity-60" />
          <p className="text-sm font-semibold text-ink-primary">
            Chưa có doanh nghiệp nào cài đặt ngưỡng tiếp nhận riêng
          </p>
          <p className="text-xs text-ink-secondary mt-1 max-w-sm mx-auto">
            Hệ thống đang áp dụng ngưỡng mặc định cho toàn bộ các lô lúa nhập kho.
          </p>
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-5">
          {thresholds.map((item) => (
            <div
              key={item.companyId}
              className="p-5 rounded-2xl bg-surface-base dark:bg-surface-raised border border-ink-tertiary/20 shadow-xs hover:shadow-md transition-shadow space-y-4 flex flex-col justify-between"
            >
              <div className="space-y-3">
                <div className="flex items-start justify-between gap-3">
                  <div className="w-10 h-10 rounded-xl bg-accent-primary/10 text-accent-primary flex items-center justify-center shrink-0">
                    <Buildings size={22} weight="bold" />
                  </div>
                  <StatusBadge
                    status="BALANCED"
                    label="Ngưỡng Riêng"
                  />
                </div>

                <div>
                  <h3 className="font-display font-bold text-base text-ink-primary">
                    {item.companyName || "Doanh nghiệp đối tác"}
                  </h3>
                  <span className="font-mono text-[11px] text-ink-tertiary block mt-0.5">
                    ID: {item.companyId.slice(0, 13)}...
                  </span>
                </div>

                <div className="grid grid-cols-3 gap-2 p-2.5 rounded-xl bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/10 text-center font-mono text-xs">
                  <div>
                    <span className="block text-[10px] text-ink-secondary font-sans">Sai số cân</span>
                    <strong className="text-accent-primary">±{item.weightVarianceTolerancePercent}%</strong>
                  </div>
                  <div>
                    <span className="block text-[10px] text-ink-secondary font-sans">Độ ẩm max</span>
                    <strong className="text-amber-500">{item.maxMoistureAllowedPercent}%</strong>
                  </div>
                  <div>
                    <span className="block text-[10px] text-ink-secondary font-sans">Tạp chất</span>
                    <strong className="text-ink-primary">{item.impurityTolerancePercent}%</strong>
                  </div>
                </div>
              </div>

              <div className="pt-3 border-t border-ink-tertiary/15 flex items-center justify-between text-xs text-ink-secondary">
                <span className="text-[11px] text-ink-tertiary">
                  ISO 22095 Custody
                </span>
                <div className="flex items-center gap-1">
                  <button
                    type="button"
                    onClick={() => handleOpenEdit(item)}
                    className="p-1.5 rounded-lg hover:bg-surface-raised text-ink-secondary hover:text-accent-primary transition-colors"
                    title="Chỉnh sửa ngưỡng"
                  >
                    <PencilSimple size={15} />
                  </button>
                  <button
                    type="button"
                    onClick={() => handleDelete(item.companyId)}
                    className="p-1.5 rounded-lg hover:bg-surface-raised text-ink-secondary hover:text-accent-danger transition-colors"
                    title="Xóa về mặc định"
                  >
                    <Trash size={15} />
                  </button>
                </div>
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Modal */}
      {isModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-xs p-4">
          <div className="w-full max-w-md bg-surface-base dark:bg-surface-raised border border-ink-tertiary/20 rounded-2xl shadow-xl overflow-hidden animate-ledger-in">
            <div className="p-5 border-b border-ink-tertiary/15 flex items-center justify-between">
              <h3 className="font-display font-bold text-base text-ink-primary">
                {isEdit ? "Chỉnh sửa Ngưỡng Doanh nghiệp" : "Thiết lập Ngưỡng Riêng Mới"}
              </h3>
              <button
                type="button"
                onClick={() => setIsModalOpen(false)}
                className="p-1 rounded-md text-ink-tertiary hover:text-ink-primary"
              >
                <X size={18} />
              </button>
            </div>

            <form onSubmit={handleSubmit} className="p-5 space-y-4 text-xs">
              <div>
                <label className="block font-semibold text-ink-secondary mb-1">
                  Doanh nghiệp Áp dụng *
                </label>
                <select
                  disabled={isEdit}
                  value={selectedCompanyId}
                  onChange={(e) => setSelectedCompanyId(e.target.value)}
                  className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none disabled:opacity-70"
                >
                  {organizations.map((org) => (
                    <option key={org.id} value={org.id}>
                      {org.name} ({org.kind})
                    </option>
                  ))}
                </select>
              </div>

              <div>
                <label className="block font-semibold text-ink-secondary mb-1">
                  Biên độ sai số khối lượng cân chấp nhận (±%) *
                </label>
                <input
                  type="number"
                  step="0.1"
                  min="0"
                  max="10"
                  required
                  value={weightVariance}
                  onChange={(e) => setWeightVariance(parseFloat(e.target.value) || 0)}
                  className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none font-mono"
                />
                <span className="text-[10px] text-ink-tertiary block mt-1">
                  Vượt quá biên độ này sẽ yêu cầu duyệt đối soát tại bước cân xe đến.
                </span>
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block font-semibold text-ink-secondary mb-1">
                    Độ ẩm tối đa (%) *
                  </label>
                  <input
                    type="number"
                    step="0.5"
                    min="10"
                    max="35"
                    required
                    value={maxMoisture}
                    onChange={(e) => setMaxMoisture(parseFloat(e.target.value) || 0)}
                    className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none font-mono"
                  />
                </div>
                <div>
                  <label className="block font-semibold text-ink-secondary mb-1">
                    Tạp chất tối đa (%) *
                  </label>
                  <input
                    type="number"
                    step="0.1"
                    min="0"
                    max="10"
                    required
                    value={impurity}
                    onChange={(e) => setImpurity(parseFloat(e.target.value) || 0)}
                    className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none font-mono"
                  />
                </div>
              </div>

              <div className="pt-3 border-t border-ink-tertiary/15 flex items-center justify-end gap-2">
                <button
                  type="button"
                  onClick={() => setIsModalOpen(false)}
                  className="px-4 py-2 rounded-lg border border-ink-tertiary/20 hover:bg-surface-raised font-semibold text-ink-secondary"
                >
                  Huỷ
                </button>
                <button
                  type="submit"
                  disabled={submitting}
                  className="px-5 py-2 rounded-lg bg-accent-primary text-white font-semibold hover:bg-accent-primary-dim transition-colors disabled:opacity-60"
                >
                  {submitting ? "Đang lưu..." : isEdit ? "Lưu thay đổi" : "Áp dụng ngưỡng"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
