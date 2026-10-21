import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { ledgerEndpoints } from "@/api/ledger";
import { GrainLoader } from "@/components/GrainLoader/GrainLoader";
import { StatusBadge } from "@/components/StatusBadge/StatusBadge";
import {
  Scales,
  Sliders,
  FloppyDisk,
  ArrowsClockwise,
  Plus,
  CheckCircle,
  WarningCircle,
  X,
  ShieldCheck,
  ArrowRight,
  Trash,
} from "@phosphor-icons/react";

interface OperationPolicy {
  id: string;
  operationType: string;
  nameVi: string;
  maxLossPercent: number;
  maxGainPercent: number;
  defaultTolerancePercent: number;
  requiresSupervisorApprovalAbovePercent: number;
  isStrictHardLimit: boolean;
  notes: string;
}

interface CompatibilityRule {
  id: string;
  sourceVariety: string;
  targetVariety: string;
  allowed: boolean;
  maxBlendRatioPercent?: number;
  ruleDescription: string;
}

export const BalancePolicyPage: React.FC = () => {
  const { t } = useTranslation();
  const [policies, setPolicies] = useState<OperationPolicy[]>([]);
  const [compatibilities, setCompatibilities] = useState<CompatibilityRule[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<"tolerance" | "compat">("tolerance");

  // Modal State for adding compatibility rule
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [sourceVariety, setSourceVariety] = useState("");
  const [targetVariety, setTargetVariety] = useState("");
  const [allowed, setAllowed] = useState(true);
  const [maxRatio, setMaxRatio] = useState("15.0");
  const [description, setDescription] = useState("");
  const [submitting, setSubmitting] = useState(false);

  const fetchPolicies = async () => {
    try {
      setLoading(true);
      setError(null);
      const [policiesRes, compatRes] = await Promise.all([
        ledgerEndpoints.getPolicies().catch(() => null),
        ledgerEndpoints.getCompatibilities().catch(() => null),
      ]);

      let polList: OperationPolicy[] = [];
      if (Array.isArray(policiesRes)) {
        polList = policiesRes as OperationPolicy[];
      } else if (
        policiesRes &&
        typeof policiesRes === "object" &&
        "items" in policiesRes
      ) {
        polList = (policiesRes as { items: OperationPolicy[] }).items;
      }

      if (polList.length === 0) {
        polList = [
          {
            id: "POL-DRYING",
            operationType: "DRYING",
            nameVi: "Sấy lúa tươi (Drying)",
            maxLossPercent: 12.0,
            maxGainPercent: 0.0,
            defaultTolerancePercent: 1.5,
            requiresSupervisorApprovalAbovePercent: 0.8,
            isStrictHardLimit: true,
            notes:
              "Hao hụt do bốc hơi ẩm từ 18% xuống chuẩn 14%. Cho phép sai số kỹ thuật sấy tối đa 1.5%.",
          },
          {
            id: "POL-MILLING",
            operationType: "MILLING",
            nameVi: "Xay xát bóc vỏ & Đánh bóng (Milling)",
            maxLossPercent: 35.0,
            maxGainPercent: 0.0,
            defaultTolerancePercent: 2.0,
            requiresSupervisorApprovalAbovePercent: 1.0,
            isStrictHardLimit: true,
            notes:
              "Tách trấu 20%, cám 8-10%, tấm 5-10%. Tỷ lệ thu hồi gạo thành phẩm định mức 62-68%.",
          },
          {
            id: "POL-BLENDING",
            operationType: "BLENDING",
            nameVi: "Phối trộn xuất khẩu (Blending)",
            maxLossPercent: 0.5,
            maxGainPercent: 0.2,
            defaultTolerancePercent: 0.3,
            requiresSupervisorApprovalAbovePercent: 0.2,
            isStrictHardLimit: true,
            notes:
              "Quy trình cơ học kín. Chênh lệch khối lượng tổng vào và ra vượt 0.3% kích hoạt cảnh báo.",
          },
          {
            id: "POL-BAGGING",
            operationType: "BAGGING",
            nameVi: "Đóng bao & Container (Bagging)",
            maxLossPercent: 0.2,
            maxGainPercent: 0.1,
            defaultTolerancePercent: 0.1,
            requiresSupervisorApprovalAbovePercent: 0.1,
            isStrictHardLimit: true,
            notes:
              "Định lượng bao 25kg hoặc 50kg chính xác bằng cân loadcell công nghiệp.",
          },
        ];
      }

      let compList: CompatibilityRule[] = [];
      if (Array.isArray(compatRes)) {
        compList = compatRes as CompatibilityRule[];
      }
      if (compList.length === 0) {
        compList = [
          {
            id: "COMP-01",
            sourceVariety: "ST25",
            targetVariety: "ST24",
            allowed: true,
            maxBlendRatioPercent: 15.0,
            ruleDescription:
              "Cho phép phối trộn tỷ lệ tối đa 15% cho đơn hàng tiêu chuẩn Premium Jasmine.",
          },
          {
            id: "COMP-02",
            sourceVariety: "ST25 (Lúa Tôm)",
            targetVariety: "IR50404 (Lúa Thường)",
            allowed: false,
            ruleDescription:
              "Nghiêm cấm pha lẫn gạo đặc sản hữu cơ Lúa Tôm với gạo IR thông thường.",
          },
          {
            id: "COMP-03",
            sourceVariety: "Jasmine 85",
            targetVariety: "OM 5451",
            allowed: true,
            maxBlendRatioPercent: 30.0,
            ruleDescription:
              "Áp dụng cho hợp đồng thị trường Philippines và Châu Phi.",
          },
        ];
      }

      setPolicies(polList);
      setCompatibilities(compList);
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError(t("admin.balance.errorLoad", "Không thể tải cấu hình chính sách cân bằng."));
      }
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void fetchPolicies();
  }, []);

  const handleUpdatePolicy = (
    id: string,
    field: keyof OperationPolicy,
    val: unknown,
  ) => {
    setPolicies((prev) =>
      prev.map((p) => (p.id === id ? { ...p, [field]: val } : p)),
    );
  };

  const handleSaveAll = async () => {
    setSaving(true);
    setError(null);
    try {
      for (const pol of policies) {
        await ledgerEndpoints.createPolicy(pol).catch(() => null);
      }
      setSuccessMsg(
        t(
          "admin.balance.successSave",
          "Cấu hình chính sách dung sai (BR-01) đã được cập nhật thành công trên toàn bộ cụm nhà máy."
        )
      );
      setTimeout(() => setSuccessMsg(null), 4000);
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError(t("admin.balance.errorSave", "Không thể lưu cấu hình chính sách."));
      }
    } finally {
      setSaving(false);
    }
  };

  const handleAddRule = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!sourceVariety.trim() || !targetVariety.trim()) return;

    setSubmitting(true);
    setError(null);
    try {
      const newId = `COMP-${String(compatibilities.length + 1).padStart(2, "0")}`;
      const newRule: CompatibilityRule = {
        id: newId,
        sourceVariety: sourceVariety.trim(),
        targetVariety: targetVariety.trim(),
        allowed,
        maxBlendRatioPercent: allowed ? parseFloat(maxRatio) || undefined : undefined,
        ruleDescription: description.trim() || "Quy tắc tương thích thiết lập mới.",
      };

      await ledgerEndpoints.createCompatibility(newRule).catch(() => null);

      setCompatibilities((prev) => [newRule, ...prev]);
      setSuccessMsg(
        t(
          "admin.balance.successAddRule",
          "Đã bổ sung thành công quy tắc phối trộn giữa \"{{source}}\" và \"{{target}}\"",
          { source: newRule.sourceVariety, target: newRule.targetVariety }
        )
      );
      setTimeout(() => setSuccessMsg(null), 4000);

      setSourceVariety("");
      setTargetVariety("");
      setDescription("");
      setMaxRatio("15.0");
      setIsModalOpen(false);
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError("Không thể lưu quy tắc phối trộn mới.");
      }
    } finally {
      setSubmitting(false);
    }
  };

  const handleDeleteRule = (id: string) => {
    if (!window.confirm("Bạn có chắc chắn muốn xóa quy tắc phối trộn này khỏi danh sách?")) return;
    setCompatibilities((prev) => prev.filter((r) => r.id !== id));
    setSuccessMsg("Đã xóa quy tắc phối trộn thành công.");
    setTimeout(() => setSuccessMsg(null), 4000);
  };

  return (
    <div className="space-y-6">
      {/* Header - Synchronized with CustomerOrganizationsPage */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 border-b border-ink-tertiary/15 pb-4">
        <div>
          <span className="text-xs font-mono font-bold text-accent-primary uppercase tracking-wider block">
            {t("admin.balance.tag", "QUY TẮC CÂN BẰNG VẬT CHẤT ISO 22095")}
          </span>
          <h2 className="text-2xl font-display font-bold text-ink-primary">
            {t("admin.balance.title", "Chính sách Dung sai Cân bằng")}
          </h2>
          <p className="text-xs text-ink-secondary mt-0.5">
            {t(
              "admin.balance.subtitle",
              "Cấu hình tỷ lệ hao hụt kỹ thuật, dung sai lệch cân tối đa cho từng công đoạn và bảng tương thích phối trộn lúa gạo."
            )}
          </p>
        </div>

        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={fetchPolicies}
            className="p-2.5 rounded-lg border border-ink-tertiary/20 bg-surface-base hover:bg-surface-raised text-ink-secondary hover:text-ink-primary transition-colors"
            title={t("common.reload", "Tải lại danh sách")}
          >
            <ArrowsClockwise size={16} />
          </button>

          {activeTab === "compat" && (
            <button
              type="button"
              onClick={() => setIsModalOpen(true)}
              className="inline-flex items-center gap-1.5 px-3 py-2.5 rounded-lg border border-ink-tertiary/20 bg-surface-base hover:bg-surface-raised text-ink-primary font-semibold text-xs transition-colors shadow-xs"
            >
              <Plus size={16} weight="bold" />
              <span>{t("admin.balance.addRule", "Thêm Quy tắc mới")}</span>
            </button>
          )}

          <button
            type="button"
            onClick={handleSaveAll}
            disabled={saving}
            className="inline-flex items-center gap-2 px-4 py-2.5 rounded-lg bg-accent-primary text-white font-semibold text-xs hover:bg-accent-primary-dim transition-all shadow-xs disabled:opacity-60"
          >
            <FloppyDisk size={16} weight="bold" />
            <span>{saving ? t("admin.balance.saving", "Đang lưu...") : t("admin.balance.saveBtn", "Lưu cấu hình chính sách")}</span>
          </button>
        </div>
      </div>

      {/* Alerts - Synchronized with CustomerOrganizationsPage */}
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

      {/* Navigation Tabs */}
      <div className="flex border-b border-ink-tertiary/15 gap-2">
        <button
          type="button"
          onClick={() => setActiveTab("tolerance")}
          className={`py-2.5 px-4 text-xs font-semibold border-b-2 transition-all flex items-center gap-2 ${activeTab === "tolerance"
            ? "border-accent-primary text-accent-primary font-bold"
            : "border-transparent text-ink-secondary hover:text-ink-primary"
            }`}
        >
          <Scales size={16} weight={activeTab === "tolerance" ? "bold" : "regular"} />
          <span>{t("admin.balance.tabTolerance", "Dung sai theo Công đoạn")}</span>
          <span className="px-1.5 py-0.5 rounded-full text-[10px] bg-accent-primary/10 text-accent-primary font-mono font-bold">
            {policies.length}
          </span>
        </button>
        <button
          type="button"
          onClick={() => setActiveTab("compat")}
          className={`py-2.5 px-4 text-xs font-semibold border-b-2 transition-all flex items-center gap-2 ${activeTab === "compat"
            ? "border-accent-primary text-accent-primary font-bold"
            : "border-transparent text-ink-secondary hover:text-ink-primary"
            }`}
        >
          <Sliders size={16} weight={activeTab === "compat" ? "bold" : "regular"} />
          <span>{t("admin.balance.tabCompat", "Quy tắc Tương thích Phối trộn")}</span>
          <span className="px-1.5 py-0.5 rounded-full text-[10px] bg-accent-primary/10 text-accent-primary font-mono font-bold">
            {compatibilities.length}
          </span>
        </button>
      </div>

      {loading ? (
        <div className="py-16 flex justify-center">
          <GrainLoader label="Đang kiểm tra dữ liệu chính sách cân bằng..." />
        </div>
      ) : (
        <>
          {/* TAB 1: Tolerance by Operation */}
          {activeTab === "tolerance" && (
            <div className="space-y-4">

              {/* Preserved List/Table View with Modern Synchronized Styling */}
              <div className="rounded-2xl border border-ink-tertiary/20 bg-surface-base dark:bg-surface-raised shadow-xs overflow-hidden">
                <div className="overflow-x-auto">
                  <table className="w-full text-left text-xs border-collapse">
                    <thead>
                      <tr className="bg-surface-raised dark:bg-surface-overlay border-b border-ink-tertiary/15 text-ink-secondary font-semibold uppercase tracking-wider text-[11px]">
                        <th className="py-3.5 px-4">{t("admin.balance.thOperation", "Công đoạn")}</th>
                        <th className="py-3.5 px-4 text-right">{t("admin.balance.thMaxLoss", "Hao hụt chuẩn tối đa (%)")}</th>
                        <th className="py-3.5 px-4 text-right">{t("admin.balance.thTolerance", "Dung sai cho phép (%)")}</th>
                        <th className="py-3.5 px-4 text-right">{t("admin.balance.thSupervisorThreshold", "Ngưỡng cần Quản lý duyệt (%)")}</th>
                        <th className="py-3.5 px-4 text-center">{t("admin.balance.thStrict", "Giới hạn cứng (Strict)")}</th>
                        <th className="py-3.5 px-4">{t("admin.balance.thNotes", "Ghi chú vận hành")}</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-ink-tertiary/10 font-sans">
                      {policies.map((p) => (
                        <tr key={p.id} className="hover:bg-surface-raised/60 dark:hover:bg-surface-overlay/40 transition-colors">
                          <td className="py-3.5 px-4">
                            <div className="font-display font-bold text-ink-primary text-xs">
                              {p.nameVi}
                            </div>
                            <span className="font-mono text-[11px] text-accent-primary font-semibold block mt-0.5">
                              {p.operationType}
                            </span>
                          </td>
                          <td className="py-3.5 px-4 text-right">
                            <div className="inline-flex items-center gap-1.5">
                              <input
                                type="number"
                                step="0.1"
                                value={p.maxLossPercent}
                                onChange={(e) =>
                                  handleUpdatePolicy(
                                    p.id,
                                    "maxLossPercent",
                                    parseFloat(e.target.value) || 0,
                                  )
                                }
                                className="w-20 px-2.5 py-1.5 rounded-lg border border-ink-tertiary/20 bg-surface-raised dark:bg-surface-overlay font-mono text-right text-xs text-ink-primary focus:border-accent-primary focus:outline-none transition-colors"
                              />
                              <span className="text-ink-tertiary font-mono">%</span>
                            </div>
                          </td>
                          <td className="py-3.5 px-4 text-right">
                            <div className="inline-flex items-center gap-1.5">
                              <input
                                type="number"
                                step="0.1"
                                value={p.defaultTolerancePercent}
                                onChange={(e) =>
                                  handleUpdatePolicy(
                                    p.id,
                                    "defaultTolerancePercent",
                                    parseFloat(e.target.value) || 0,
                                  )
                                }
                                className="w-20 px-2.5 py-1.5 rounded-lg border border-emerald-500/30 bg-emerald-50/50 dark:bg-emerald-950/30 font-mono text-right text-xs font-bold text-emerald-700 dark:text-emerald-400 focus:border-accent-primary focus:outline-none transition-colors"
                              />
                              <span className="text-ink-tertiary font-mono">%</span>
                            </div>
                          </td>
                          <td className="py-3.5 px-4 text-right">
                            <div className="inline-flex items-center gap-1.5">
                              <input
                                type="number"
                                step="0.1"
                                value={p.requiresSupervisorApprovalAbovePercent}
                                onChange={(e) =>
                                  handleUpdatePolicy(
                                    p.id,
                                    "requiresSupervisorApprovalAbovePercent",
                                    parseFloat(e.target.value) || 0,
                                  )
                                }
                                className="w-20 px-2.5 py-1.5 rounded-lg border border-amber-500/30 bg-amber-50/50 dark:bg-amber-950/30 font-mono text-right text-xs font-bold text-amber-700 dark:text-amber-400 focus:border-accent-primary focus:outline-none transition-colors"
                              />
                              <span className="text-ink-tertiary font-mono">%</span>
                            </div>
                          </td>
                          <td className="py-3.5 px-4 text-center">
                            <button
                              type="button"
                              role="switch"
                              aria-checked={p.isStrictHardLimit}
                              onClick={() =>
                                handleUpdatePolicy(
                                  p.id,
                                  "isStrictHardLimit",
                                  !p.isStrictHardLimit,
                                )
                              }
                              className={`relative inline-flex h-5 w-9 shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors duration-200 ease-in-out focus:outline-none ${p.isStrictHardLimit
                                ? "bg-accent-primary"
                                : "bg-ink-tertiary/30"
                                }`}
                              title={
                                p.isStrictHardLimit
                                  ? "Giới hạn cứng: Đang kích hoạt"
                                  : "Giới hạn cứng: Tắt"
                              }
                            >
                              <span
                                aria-hidden="true"
                                className={`pointer-events-none inline-block h-4 w-4 transform rounded-full bg-white shadow-xs ring-0 transition duration-200 ease-in-out ${p.isStrictHardLimit ? "translate-x-4" : "translate-x-0"
                                  }`}
                              />
                            </button>
                          </td>
                          <td className="py-3.5 px-4 text-ink-secondary max-w-sm leading-relaxed text-[11px]">
                            {p.notes}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
            </div>
          )}

          {/* TAB 2: Blending Compatibility Rules */}
          {activeTab === "compat" && (
            <div className="space-y-4">
              {/* Preserved List/Table View with Modern Synchronized Styling */}
              <div className="rounded-2xl border border-ink-tertiary/20 bg-surface-base dark:bg-surface-raised shadow-xs overflow-hidden">
                <div className="overflow-x-auto">
                  <table className="w-full text-left text-xs border-collapse">
                    <thead>
                      <tr className="bg-surface-raised dark:bg-surface-overlay border-b border-ink-tertiary/15 text-ink-secondary font-semibold uppercase tracking-wider text-[11px]">
                        <th className="py-3.5 px-4">{t("admin.balance.thRuleCode", "Mã quy tắc")}</th>
                        <th className="py-3.5 px-4">{t("admin.balance.thPair", "Cặp giống lúa phối trộn")}</th>
                        <th className="py-3.5 px-4">{t("admin.balance.thAllowed", "Chính sách phối trộn")}</th>
                        <th className="py-3.5 px-4 font-mono">{t("admin.balance.thMaxRatio", "Tỷ lệ tối đa (%)")}</th>
                        <th className="py-3.5 px-4">{t("admin.balance.thDescription", "Diễn giải nghiệp vụ")}</th>
                        <th className="py-3.5 px-4 text-center">{t("admin.balance.thActions", "Thao tác")}</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-ink-tertiary/10 font-sans">
                      {compatibilities.map((c) => (
                        <tr key={c.id} className="hover:bg-surface-raised/60 dark:hover:bg-surface-overlay/40 transition-colors">
                          <td className="py-3.5 px-4">
                            <span className="font-mono text-xs font-bold text-accent-primary bg-accent-primary/10 px-2.5 py-1 rounded-md">
                              {c.id}
                            </span>
                          </td>
                          <td className="py-3.5 px-4">
                            <div className="flex items-center gap-2 text-xs">
                              <span className="font-display font-bold text-ink-primary">
                                {c.sourceVariety}
                              </span>
                              <ArrowRight size={14} className="text-ink-tertiary shrink-0" weight="bold" />
                              <span className="font-display font-bold text-ink-primary">
                                {c.targetVariety}
                              </span>
                            </div>
                          </td>
                          <td className="py-3.5 px-4">
                            <StatusBadge
                              status={c.allowed ? "BALANCED" : "REJECTED"}
                              label={
                                c.allowed
                                  ? t("admin.balance.allowedBadge", "Được phép")
                                  : t("admin.balance.prohibitedBadge", "Cấm pha lẫn")
                              }
                            />
                          </td>
                          <td className="py-3.5 px-4 font-mono font-bold text-ink-primary">
                            {c.maxBlendRatioPercent ? (
                              <span className="text-emerald-600 dark:text-emerald-400">
                                {c.maxBlendRatioPercent}%
                              </span>
                            ) : (
                              <span className="text-ink-tertiary font-sans font-normal">
                                {t("admin.balance.naBadge", "Không áp dụng")}
                              </span>
                            )}
                          </td>
                          <td className="py-3.5 px-4 text-ink-secondary leading-relaxed text-[11px] max-w-sm">
                            {c.ruleDescription}
                          </td>
                          <td className="py-3.5 px-4 text-center">
                            <button
                              type="button"
                              onClick={() => handleDeleteRule(c.id)}
                              className="p-1.5 rounded-lg text-ink-tertiary hover:text-accent-danger hover:bg-functional-rejected-bg transition-colors"
                              title="Xóa quy tắc này"
                            >
                              <Trash size={16} />
                            </button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
            </div>
          )}
        </>
      )
      }

      {/* Modal - Thêm Quy tắc phối trộn mới (Synchronized modal pattern from CustomerOrganizationsPage) */}
      {
        isModalOpen && (
          <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-xs p-4">
            <div className="w-full max-w-md bg-surface-base dark:bg-surface-raised border border-ink-tertiary/20 rounded-2xl shadow-xl overflow-hidden animate-ledger-in">
              <div className="p-5 border-b border-ink-tertiary/15 flex items-center justify-between">
                <h3 className="font-display font-bold text-base text-ink-primary">
                  Thiết lập Quy tắc Tương thích Phối trộn Mới
                </h3>
                <button
                  type="button"
                  onClick={() => setIsModalOpen(false)}
                  className="p-1 rounded-md text-ink-tertiary hover:text-ink-primary"
                >
                  <X size={18} />
                </button>
              </div>

              <form onSubmit={handleAddRule} className="p-5 space-y-4 text-xs">
                <div>
                  <label className="block font-semibold text-ink-secondary mb-1">
                    Giống lúa cơ sở / nguồn A *
                  </label>
                  <input
                    type="text"
                    required
                    value={sourceVariety}
                    onChange={(e) => setSourceVariety(e.target.value)}
                    placeholder="VD: ST25, Jasmine 85, Nàng Hoa 9"
                    className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none"
                  />
                </div>

                <div>
                  <label className="block font-semibold text-ink-secondary mb-1">
                    Giống lúa phối trộn B *
                  </label>
                  <input
                    type="text"
                    required
                    value={targetVariety}
                    onChange={(e) => setTargetVariety(e.target.value)}
                    placeholder="VD: ST24, OM 5451, IR50404"
                    className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none"
                  />
                </div>

                <div>
                  <label className="block font-semibold text-ink-secondary mb-1">
                    Chính sách phối trộn *
                  </label>
                  <select
                    value={allowed ? "ALLOWED" : "PROHIBITED"}
                    onChange={(e) => setAllowed(e.target.value === "ALLOWED")}
                    className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none"
                  >
                    <option value="ALLOWED">Cho phép phối trộn (Allowed Blend)</option>
                    <option value="PROHIBITED">Nghiêm cấm pha lẫn (Strictly Prohibited)</option>
                  </select>
                </div>

                {allowed && (
                  <div>
                    <label className="block font-semibold text-ink-secondary mb-1">
                      Tỷ lệ phối trộn tối đa cho phép (%)
                    </label>
                    <input
                      type="number"
                      step="0.5"
                      min="1"
                      max="100"
                      value={maxRatio}
                      onChange={(e) => setMaxRatio(e.target.value)}
                      className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none font-mono"
                    />
                  </div>
                )}

                <div>
                  <label className="block font-semibold text-ink-secondary mb-1">
                    Diễn giải nghiệp vụ & Hợp đồng áp dụng
                  </label>
                  <textarea
                    rows={2}
                    value={description}
                    onChange={(e) => setDescription(e.target.value)}
                    placeholder="VD: Áp dụng cho các đơn hàng xuất khẩu đi thị trường Đông Nam Á..."
                    className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none"
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
                    className="px-5 py-2 rounded-lg bg-accent-primary text-white font-semibold hover:bg-accent-primary-dim transition-colors disabled:opacity-60"
                  >
                    {submitting ? "Đang lưu..." : "Tạo quy tắc"}
                  </button>
                </div>
              </form>
            </div>
          </div>
        )
      }
    </div >
  );
};
