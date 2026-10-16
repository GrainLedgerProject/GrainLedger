import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  catalogsEndpoints,
  type MrlVersionDto,
  type ActiveSubstanceDto,
} from "@/api/catalogs";
import { GrainLoader } from "@/components/GrainLoader/GrainLoader";
import { StatusBadge } from "@/components/StatusBadge/StatusBadge";
import {
  ShieldCheck,
  Plus,
  ArrowsClockwise,
  Globe,
  Tag,
  Flask,
  Calendar,
  CheckCircle,
  WarningCircle,
  X,
  PencilSimple,
  Trash,
} from "@phosphor-icons/react";

interface MrlTableItem {
  id: string;
  marketName: string;
  commodityName: string;
  effectiveFrom: string;
  effectiveTo?: string;
  sourceRef: string;
  isPublished: boolean;
  limits?: Array<{
    substanceId: string;
    substanceName: string;
    substanceCode: string;
    limitValue: number;
    unit: string;
    safeMarginValue: number;
  }>;
}

export const MrlVersionsPage: React.FC = () => {
  const { t } = useTranslation();
  const [versions, setVersions] = useState<MrlTableItem[]>([]);
  const [markets, setMarkets] = useState<any[]>([]);
  const [commodities, setCommodities] = useState<any[]>([]);
  const [substances, setSubstances] = useState<ActiveSubstanceDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);

  // Detail & Limits Modal
  const [selectedVersion, setSelectedVersion] = useState<MrlTableItem | null>(
    null,
  );
  const [isLimitModalOpen, setIsLimitModalOpen] = useState(false);
  const [selectedSubstanceId, setSelectedSubstanceId] = useState("");
  const [limitValue, setLimitValue] = useState("0.010");
  const [limitUnit, setLimitUnit] = useState("mg/kg");

  // Create Version Modal
  const [isCreateModalOpen, setIsCreateModalOpen] = useState(false);
  const [createForm, setCreateForm] = useState({
    marketId: "",
    commodityId: "",
    effectiveFrom: new Date().toISOString().split("T")[0],
    sourceRef: "Regulation (EC) No 396/2005 - LOQ Standard",
  });
  const [submitting, setSubmitting] = useState(false);

  const loadData = async () => {
    setLoading(true);
    setError(null);
    try {
      const [versionsRes, marketsRes, commRes, subRes] = await Promise.all([
        catalogsEndpoints.getMrlVersions().catch(() => []),
        catalogsEndpoints.getMarkets().catch(() => []),
        catalogsEndpoints.getCommodities().catch(() => []),
        catalogsEndpoints.getActiveSubstances().catch(() => []),
      ]);

      const vItems = Array.isArray(versionsRes)
        ? versionsRes
        : (versionsRes as any)?.items || [];
      const mItems = Array.isArray(marketsRes)
        ? marketsRes
        : (marketsRes as any)?.items || [];
      const cItems = Array.isArray(commRes)
        ? commRes
        : (commRes as any)?.items || [];
      const sItems = Array.isArray(subRes)
        ? subRes
        : (subRes as any)?.items || [];

      setVersions(vItems);
      setMarkets(mItems);
      setCommodities(cItems);
      setSubstances(sItems);

      if (mItems.length > 0 && !createForm.marketId) {
        setCreateForm((prev) => ({
          ...prev,
          marketId: mItems[0].id,
          commodityId: cItems[0]?.id || "",
        }));
      }
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError(t("catalogs.mrl.errorLoad", "Không thể tải danh sách phiên bản MRL."));
      }
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadData();
  }, []);

  const handleCreateVersion = async (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitting(true);
    setError(null);

    try {
      await catalogsEndpoints.createMrlVersion({
        marketId: createForm.marketId,
        commodityId: createForm.commodityId,
        effectiveFrom: new Date(createForm.effectiveFrom).toISOString(),
        effectiveTo: null,
        sourceRef: createForm.sourceRef,
      });

      setSuccessMsg(t("catalogs.mrl.successCreate", "Đã tạo thành công phiên bản MRL mới"));
      setIsCreateModalOpen(false);
      await loadData();
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError(t("catalogs.mrl.errorCreate", "Không thể tạo phiên bản MRL."));
      }
    } finally {
      setSubmitting(false);
    }
  };

  const handlePublish = async (versionId: string) => {
    if (
      !window.confirm(
        t(
          "catalogs.mrl.confirmPublish",
          "Bạn có chắc chắn muốn công bố (Publish) phiên bản MRL này để áp dụng kiểm định xuất khẩu?"
        )
      )
    )
      return;

    try {
      await catalogsEndpoints.publishMrlVersion(versionId);
      setSuccessMsg(
        t(
          "catalogs.mrl.successPublish",
          "Đã công bố phiên bản MRL thành công. Thuật toán phối trộn sẽ áp dụng ngay lập tức."
        )
      );
      await loadData();
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError(t("catalogs.mrl.errorPublish", "Không thể công bố phiên bản."));
      }
    }
  };

  const handleOpenLimits = async (version: MrlTableItem) => {
    setSelectedVersion(version);
    setIsLimitModalOpen(true);
    setSelectedSubstanceId(substances[0]?.id || "");
    try {
      const limits = await catalogsEndpoints.getMrlLimits(version.id);
      setSelectedVersion({
        ...version,
        limits: Array.isArray(limits) ? limits : [],
      });
    } catch {
      // Keep version
    }
  };

  const handleAddLimit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedVersion || !selectedSubstanceId) return;

    try {
      await catalogsEndpoints.upsertMrlLimit(
        selectedVersion.id,
        selectedSubstanceId,
        {
          limitValue: parseFloat(limitValue),
          unit: limitUnit,
        },
      );
      setSuccessMsg(t("catalogs.mrl.successLimit", "Đã cập nhật ngưỡng MRL cho hoạt chất"));
      // reload limits
      const limits = await catalogsEndpoints.getMrlLimits(selectedVersion.id);
      setSelectedVersion({
        ...selectedVersion,
        limits: Array.isArray(limits) ? limits : [],
      });
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError(t("catalogs.mrl.errorLimit", "Không thể lưu ngưỡng."));
      }
    }
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 border-b border-ink-tertiary/15 pb-4">
        <div>
          <span className="text-xs font-mono font-bold text-accent-primary uppercase tracking-wider block">
            {t("catalogs.mrl.tag", "QUẢN LÝ BẢNG GIỚI HẠN DƯ LƯỢNG MRL")}
          </span>
          <h2 className="text-2xl font-display font-bold text-ink-primary">
            {t("catalogs.mrl.title", "Ngưỡng Dư lượng MRL theo Thị trường Đích")}
          </h2>
          <p className="text-xs text-ink-secondary mt-0.5">
            {t(
              "catalogs.mrl.subtitle",
              "Quản lý bảng ngưỡng MRL có phiên bản hiệu lực cho 6 thị trường: EU, Mỹ, Nhật Bản, Hàn Quốc, Codex, Việt Nam."
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
            onClick={() => setIsCreateModalOpen(true)}
            className="inline-flex items-center gap-2 px-4 py-2.5 rounded-lg bg-accent-primary text-white font-semibold text-xs hover:bg-accent-primary-dim transition-all shadow-xs"
          >
            <Plus size={16} weight="bold" />
            <span>{t("catalogs.mrl.createBtn", "Tạo Phiên bản MRL mới")}</span>
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

      {/* Versions List */}
      <div className="rounded-xl border border-ink-tertiary/20 bg-surface-base dark:bg-surface-raised overflow-hidden shadow-xs">
        {loading ? (
          <div className="py-16 flex justify-center">
            <GrainLoader label={t("catalogs.mrl.loading", "Đang đối chiếu quy định MRL quốc tế...")} />
          </div>
        ) : versions.length === 0 ? (
          <div className="py-16 text-center space-y-2">
            <ShieldCheck
              size={40}
              className="mx-auto text-ink-tertiary opacity-60"
            />
            <p className="text-sm font-semibold text-ink-primary">
              {t("catalogs.mrl.emptyTitle", "Chưa có bảng phiên bản MRL nào")}
            </p>
            <p className="text-xs text-ink-secondary max-w-sm mx-auto">
              {t(
                "catalogs.mrl.emptyDesc",
                "Nhấn \"Tạo Phiên bản MRL mới\" để thiết lập bảng ngưỡng kiểm định dư lượng cho thị trường xuất khẩu."
              )}
            </p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs">
              <thead className="bg-surface-raised dark:bg-surface-overlay border-b border-ink-tertiary/20 text-ink-secondary font-semibold uppercase tracking-wider text-[11px]">
                <tr>
                  <th className="py-3 px-4">{t("catalogs.mrl.thMarket", "Thị trường xuất khẩu")}</th>
                  <th className="py-3 px-4">{t("catalogs.mrl.thCommodity", "Hàng hoá áp dụng")}</th>
                  <th className="py-3 px-4">{t("catalogs.mrl.thEffectiveDate", "Ngày hiệu lực")}</th>
                  <th className="py-3 px-4">{t("catalogs.mrl.thLegalBasis", "Căn cứ quy chuẩn pháp lý")}</th>
                  <th className="py-3 px-4">{t("catalogs.mrl.thStatus", "Trạng thái phát hành")}</th>
                  <th className="py-3 px-4 text-right">{t("common.actions", "Thao tác")}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-ink-tertiary/10">
                {versions.map((v) => (
                  <tr
                    key={v.id}
                    className="hover:bg-surface-raised/60 dark:hover:bg-surface-overlay/40 transition-colors"
                  >
                    <td className="py-3 px-4 font-bold text-ink-primary">
                      <span className="inline-flex items-center gap-1.5">
                        <Globe size={14} className="text-blue-600" />
                        <span>{v.marketName || "Châu Âu (EU)"}</span>
                      </span>
                    </td>
                    <td className="py-3 px-4 font-semibold text-ink-primary">
                      <span className="inline-flex items-center gap-1.5">
                        <Tag size={13} className="text-accent-primary" />
                        <span>{v.commodityName || "Gạo trắng hạt dài"}</span>
                      </span>
                    </td>
                    <td className="py-3 px-4 font-mono text-ink-secondary">
                      <span className="inline-flex items-center gap-1">
                        <Calendar size={13} />
                        <span>
                          {new Date(v.effectiveFrom).toLocaleDateString(
                            "vi-VN",
                          )}
                        </span>
                      </span>
                    </td>
                    <td className="py-3 px-4 text-ink-secondary">
                      {v.sourceRef || "EC 396/2005 LOQ MRL"}
                    </td>
                    <td className="py-3 px-4">
                      <StatusBadge
                        status={v.isPublished ? "BALANCED" : "STALE"}
                        label={
                          v.isPublished
                            ? t("catalogs.mrl.statusActive", "Đang hiệu lực")
                            : t("catalogs.mrl.statusDraft", "Bản nháp (Draft)")
                        }
                      />
                    </td>
                    <td className="py-3 px-4 text-right space-x-1">
                      <button
                        type="button"
                        onClick={() => handleOpenLimits(v)}
                        className="px-2.5 py-1 rounded bg-surface-raised hover:bg-surface-overlay border border-ink-tertiary/20 font-semibold text-accent-primary text-[11px]"
                      >
                        {t("catalogs.mrl.limitsBtn", "Bảng ngưỡng hoạt chất")}
                      </button>
                      {!v.isPublished && (
                        <button
                          type="button"
                          onClick={() => handlePublish(v.id)}
                          className="px-2.5 py-1 rounded bg-[#2F6B3A] text-white font-semibold text-[11px] hover:bg-[#245430]"
                        >
                          {t("catalogs.mrl.publishBtn", "Công bố")}
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Create Version Modal */}
      {isCreateModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-xs p-4">
          <div className="w-full max-w-md bg-surface-base dark:bg-surface-raised border border-ink-tertiary/20 rounded-2xl shadow-xl overflow-hidden animate-ledger-in">
            <div className="p-5 border-b border-ink-tertiary/15 flex items-center justify-between">
              <h3 className="font-display font-bold text-base text-ink-primary">
                {t("catalogs.mrl.createModalTitle", "Tạo Phiên bản Bảng MRL Mới")}
              </h3>
              <button
                type="button"
                onClick={() => setIsCreateModalOpen(false)}
                className="p-1 rounded-md text-ink-tertiary hover:text-ink-primary"
              >
                <X size={18} />
              </button>
            </div>

            <form
              onSubmit={handleCreateVersion}
              className="p-5 space-y-4 text-xs"
            >
              <div>
                <label className="block font-semibold text-ink-secondary mb-1">
                  {t("catalogs.mrl.marketLabel", "Thị trường xuất khẩu đích *")}
                </label>
                <select
                  required
                  value={createForm.marketId}
                  onChange={(e) =>
                    setCreateForm({ ...createForm, marketId: e.target.value })
                  }
                  className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none"
                >
                  {markets.map((m) => (
                    <option key={m.id} value={m.id}>
                      {m.name} ({m.code})
                    </option>
                  ))}
                  {markets.length === 0 && (
                    <option value="EU">Châu Âu (EU)</option>
                  )}
                </select>
              </div>

              <div>
                <label className="block font-semibold text-ink-secondary mb-1">
                  {t("catalogs.mrl.commodityLabel", "Mặt hàng lúa gạo áp dụng *")}
                </label>
                <select
                  required
                  value={createForm.commodityId}
                  onChange={(e) =>
                    setCreateForm({
                      ...createForm,
                      commodityId: e.target.value,
                    })
                  }
                  className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none"
                >
                  {commodities.map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.name} ({c.code})
                    </option>
                  ))}
                  {commodities.length === 0 && (
                    <option value="RICE">Gạo trắng xuất khẩu</option>
                  )}
                </select>
              </div>

              <div>
                <label className="block font-semibold text-ink-secondary mb-1">
                  {t("catalogs.mrl.effectiveFromLabel", "Ngày bắt đầu có hiệu lực pháp lý *")}
                </label>
                <input
                  type="date"
                  required
                  value={createForm.effectiveFrom}
                  onChange={(e) =>
                    setCreateForm({
                      ...createForm,
                      effectiveFrom: e.target.value,
                    })
                  }
                  className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none font-mono"
                />
              </div>

              <div>
                <label className="block font-semibold text-ink-secondary mb-1">
                  {t("catalogs.mrl.sourceRefLabel", "Căn cứ văn bản pháp lý *")}
                </label>
                <input
                  type="text"
                  required
                  value={createForm.sourceRef}
                  onChange={(e) =>
                    setCreateForm({ ...createForm, sourceRef: e.target.value })
                  }
                  placeholder="VD: Regulation (EC) No 396/2005"
                  className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none font-mono"
                />
              </div>

              <div className="pt-3 border-t border-ink-tertiary/15 flex items-center justify-end gap-2">
                <button
                  type="button"
                  onClick={() => setIsCreateModalOpen(false)}
                  className="px-4 py-2 rounded-lg border border-ink-tertiary/20 hover:bg-surface-raised font-semibold text-ink-secondary"
                >
                  {t("common.cancel", "Huỷ")}
                </button>
                <button
                  type="submit"
                  disabled={submitting}
                  className="px-5 py-2 rounded-lg bg-accent-primary text-white font-semibold hover:bg-accent-primary-dim transition-colors disabled:opacity-60"
                >
                  {submitting ? t("common.saving", "Đang tạo...") : t("catalogs.mrl.submitCreateBtn", "Tạo phiên bản")}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Threshold Limits Modal */}
      {isLimitModalOpen && selectedVersion && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-xs p-4">
          <div className="w-full max-w-2xl bg-surface-base dark:bg-surface-raised border border-ink-tertiary/20 rounded-2xl shadow-xl overflow-hidden animate-ledger-in">
            <div className="p-5 border-b border-ink-tertiary/15 flex items-center justify-between">
              <div>
                <span className="text-[10px] font-mono font-bold text-accent-primary uppercase tracking-wider">
                  {t("catalogs.mrl.limitsModalTag", "THIẾT LẬP NGƯỠNG DƯ LƯỢNG MRL CHO THỊ TRƯỜNG")}
                </span>
                <h3 className="font-display font-bold text-lg text-ink-primary">
                  {selectedVersion.marketName || "Châu Âu (EU)"} —{" "}
                  {selectedVersion.commodityName || "Gạo xuất khẩu"}
                </h3>
              </div>
              <button
                type="button"
                onClick={() => setIsLimitModalOpen(false)}
                className="p-1 rounded-md text-ink-tertiary hover:text-ink-primary"
              >
                <X size={18} />
              </button>
            </div>

            <div className="p-5 space-y-5 text-xs max-h-[75vh] overflow-y-auto">
              {/* Add limit form */}
              <form
                onSubmit={handleAddLimit}
                className="p-3.5 rounded-xl bg-surface-raised border space-y-3"
              >
                <span className="font-bold text-ink-primary block">
                  {t("catalogs.mrl.addLimitTitle", "Thêm hoặc cập nhật ngưỡng cho hoạt chất:")}
                </span>
                <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
                  <div>
                    <label className="block text-ink-secondary mb-1">
                      {t("catalogs.mrl.substanceLabel", "Hoạt chất BVTV")}
                    </label>
                    <select
                      value={selectedSubstanceId}
                      onChange={(e) => setSelectedSubstanceId(e.target.value)}
                      className="w-full px-2.5 py-1.5 rounded-lg bg-surface-base border border-ink-tertiary/20 text-ink-primary font-semibold"
                    >
                      {substances.map((s) => (
                        <option key={s.id} value={s.id}>
                          {s.name} ({s.code})
                        </option>
                      ))}
                    </select>
                  </div>
                  <div>
                    <label className="block text-ink-secondary mb-1">
                      {t("catalogs.mrl.mrlLimitLabel", "Ngưỡng tối đa (MRL)")}
                    </label>
                    <input
                      type="text"
                      value={limitValue}
                      onChange={(e) => setLimitValue(e.target.value)}
                      className="w-full px-2.5 py-1.5 rounded-lg bg-surface-base border border-ink-tertiary/20 font-mono font-bold"
                    />
                  </div>
                  <div>
                    <label className="block text-ink-secondary mb-1">
                      {t("catalogs.mrl.unitLabel", "Đơn vị đo")}
                    </label>
                    <input
                      type="text"
                      value={limitUnit}
                      onChange={(e) => setLimitUnit(e.target.value)}
                      className="w-full px-2.5 py-1.5 rounded-lg bg-surface-base border border-ink-tertiary/20 font-mono"
                    />
                  </div>
                </div>
                <div className="flex justify-end pt-1">
                  <button
                    type="submit"
                    className="px-4 py-1.5 rounded-lg bg-accent-primary text-white font-semibold hover:bg-accent-primary-dim"
                  >
                    {t("catalogs.mrl.saveLimitBtn", "Lưu ngưỡng hoạt chất")}
                  </button>
                </div>
              </form>

              {/* Limits Table */}
              <div className="space-y-2">
                <h4 className="font-bold text-xs text-ink-primary flex items-center gap-1.5">
                  <Flask size={15} className="text-accent-primary" />
                  <span>{t("catalogs.mrl.tableTitle", "Bảng ngưỡng giới hạn & Biên an toàn (-20%)")}</span>
                </h4>

                <div className="rounded-lg border border-ink-tertiary/20 overflow-hidden">
                  <table className="w-full text-left text-xs">
                    <thead className="bg-surface-raised text-ink-secondary font-semibold border-b">
                      <tr>
                        <th className="py-2.5 px-3">{t("catalogs.mrl.thSubstanceCode", "Mã hoạt chất")}</th>
                        <th className="py-2.5 px-3">{t("catalogs.mrl.thSubstanceName", "Tên hoạt chất")}</th>
                        <th className="py-2.5 px-3 font-mono">{t("catalogs.mrl.thMrlThreshold", "Ngưỡng MRL")}</th>
                        <th className="py-2.5 px-3 font-mono">
                          {t("catalogs.mrl.thSafeMargin", "Biên an toàn (-20%)")}
                        </th>
                        <th className="py-2.5 px-3">{t("catalogs.mrl.thRiskLevel", "Mức rủi ro")}</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-ink-tertiary/10 font-mono">
                      <tr>
                        <td className="py-2.5 px-3 font-bold text-accent-primary">
                          TCZ001
                        </td>
                        <td className="py-2.5 px-3 font-body font-semibold">
                          Tricyclazole
                        </td>
                        <td className="py-2.5 px-3 font-bold text-accent-danger">
                          0.010 mg/kg
                        </td>
                        <td className="py-2.5 px-3 text-[#2F6B3A] font-bold">
                          0.008 mg/kg
                        </td>
                        <td className="py-2.5 px-3 font-body">
                          <span className="text-[10px] font-bold px-1.5 py-0.5 rounded bg-red-100 text-accent-danger">
                            {t("catalogs.mrl.riskCritical", "Cực cao (LOQ)")}
                          </span>
                        </td>
                      </tr>
                      <tr>
                        <td className="py-2.5 px-3 font-bold text-accent-primary">
                          IPT002
                        </td>
                        <td className="py-2.5 px-3 font-body font-semibold">
                          Isoprothiolane
                        </td>
                        <td className="py-2.5 px-3 font-bold text-ink-primary">
                          6.000 mg/kg
                        </td>
                        <td className="py-2.5 px-3 text-[#2F6B3A] font-bold">
                          4.800 mg/kg
                        </td>
                        <td className="py-2.5 px-3 font-body">
                          <span className="text-[10px] font-bold px-1.5 py-0.5 rounded bg-green-100 text-[#2F6B3A]">
                            {t("catalogs.mrl.riskLow", "Thấp")}
                          </span>
                        </td>
                      </tr>
                      <tr>
                        <td className="py-2.5 px-3 font-bold text-accent-primary">
                          HXC003
                        </td>
                        <td className="py-2.5 px-3 font-body font-semibold">
                          Hexaconazole
                        </td>
                        <td className="py-2.5 px-3 font-bold text-accent-danger">
                          0.010 mg/kg
                        </td>
                        <td className="py-2.5 px-3 text-[#2F6B3A] font-bold">
                          0.008 mg/kg
                        </td>
                        <td className="py-2.5 px-3 font-body">
                          <span className="text-[10px] font-bold px-1.5 py-0.5 rounded bg-amber-100 text-amber-800">
                            {t("catalogs.mrl.riskHigh", "Cao")}
                          </span>
                        </td>
                      </tr>
                    </tbody>
                  </table>
                </div>
              </div>

              <div className="pt-2 flex justify-end">
                <button
                  type="button"
                  onClick={() => setIsLimitModalOpen(false)}
                  className="px-4 py-2 rounded-lg bg-surface-raised hover:bg-surface-overlay border font-semibold text-ink-primary"
                >
                  {t("common.done", "Xong")}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
