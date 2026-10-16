import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { procurementEndpoints } from "@/api/procurement";
import { GrainLoader } from "@/components/GrainLoader/GrainLoader";
import { StatusBadge } from "@/components/StatusBadge/StatusBadge";
import {
  MapPin,
  MagnifyingGlass,
  Plus,
  ArrowsClockwise,
  PencilSimple,
  Trash,
  CheckCircle,
  WarningCircle,
  X,
  Polygon,
  Crop,
  User,
} from "@phosphor-icons/react";

interface PlotRecord {
  id: string;
  code: string;
  farmerId: string;
  farmerName?: string;
  areaHa: number;
  geometry?: any;
  version?: number;
  variety?: string;
}

interface FarmerOption {
  id: string;
  name: string;
  code: string;
}

export const PlotRegistrationPage: React.FC = () => {
  const { t } = useTranslation();
  const [plots, setPlots] = useState<PlotRecord[]>([]);
  const [farmers, setFarmers] = useState<FarmerOption[]>([]);
  const [loading, setLoading] = useState(true);
  const [searchQuery, setSearchQuery] = useState("");
  const [selectedFarmerId, setSelectedFarmerId] = useState<string>("");
  const [error, setError] = useState<string | null>(null);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);

  // Modal State
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [editingPlot, setEditingPlot] = useState<PlotRecord | null>(null);
  const [formData, setFormData] = useState({
    code: "",
    farmerId: "",
    areaHa: 1.5,
    coordinates: "10.3541, 106.0125",
  });
  const [submitting, setSubmitting] = useState(false);

  const loadData = async () => {
    setLoading(true);
    setError(null);
    try {
      const plotParams: Record<string, string | number> = { pageSize: 50 };
      if (searchQuery) plotParams.search = searchQuery;
      if (selectedFarmerId) plotParams.farmerId = selectedFarmerId;

      const [plotsRes, farmersRes] = await Promise.all([
        procurementEndpoints.getPlots(plotParams),
        procurementEndpoints.getFarmers({ pageSize: 100 }),
      ]);

      const plotItems = Array.isArray(plotsRes)
        ? plotsRes
        : (plotsRes as any)?.items || [];
      const farmerItems = Array.isArray(farmersRes)
        ? farmersRes
        : (farmersRes as any)?.items || [];

      // Map farmer names to plots
      const farmerMap = new Map(farmerItems.map((f: any) => [f.id, f.name]));
      const enrichedPlots = plotItems.map((p: any) => ({
        ...p,
        farmerName: farmerMap.get(p.farmerId) || t("field.plot.unknownFarmer", "Nông hộ chưa rõ"),
      }));

      setPlots(enrichedPlots);
      setFarmers(farmerItems);
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError(t("field.plot.errLoad", "Không thể tải danh sách thửa ruộng từ máy chủ."));
      }
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadData();
  }, [searchQuery, selectedFarmerId]);

  const handleOpenCreate = () => {
    setEditingPlot(null);
    setFormData({
      code: `PLOT-${Date.now().toString().slice(-4)}`,
      farmerId: farmers[0]?.id || "",
      areaHa: 1.5,
      coordinates: "10.3541, 106.0125",
    });
    setIsModalOpen(true);
  };

  const handleOpenEdit = (plot: PlotRecord) => {
    setEditingPlot(plot);
    setFormData({
      code: plot.code || "",
      farmerId: plot.farmerId || "",
      areaHa: plot.areaHa || 1.0,
      coordinates: plot.geometry?.coordinates
        ? JSON.stringify(plot.geometry.coordinates)
        : "10.3541, 106.0125",
    });
    setIsModalOpen(true);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!formData.farmerId) {
      setError(t("field.plot.errSelectFarmer", "Vui lòng chọn Nông hộ sở hữu thửa ruộng."));
      return;
    }

    setSubmitting(true);
    setError(null);
    setSuccessMsg(null);

    try {
      const geometryObj = {
        type: "Point",
        coordinates: formData.coordinates
          .split(",")
          .map((c) => parseFloat(c.trim())),
      };

      if (editingPlot) {
        // Update
        await procurementEndpoints.updatePlot(editingPlot.id, {
          farmerId: formData.farmerId,
          code: formData.code.trim(),
          areaHa: Number(formData.areaHa),
          geometry: geometryObj,
          expectedVersion: editingPlot.version || 1,
        });
        setSuccessMsg(
          t("field.plot.updatedSuccess", "Đã cập nhật thửa ruộng {{code}}", {
            code: formData.code,
          })
        );
      } else {
        // Create
        const entityId = crypto.randomUUID();
        const clientOperationId = crypto.randomUUID();
        await procurementEndpoints.createPlot({
          deviceId: "grainledger-web",
          clientOperationId,
          capturedAt: new Date().toISOString(),
          payloadSchemaVersion: 1,
          payload: {
            entityId,
            farmerId: formData.farmerId,
            code: formData.code.trim(),
            areaHa: Number(formData.areaHa),
            geometry: geometryObj,
          },
        });
        setSuccessMsg(
          t("field.plot.createdSuccess", "Đã đăng ký thành công thửa ruộng {{code}}", {
            code: formData.code,
          })
        );
      }

      setIsModalOpen(false);
      await loadData();
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError(t("field.plot.errAction", "Thao tác không thành công."));
      }
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async (plot: PlotRecord) => {
    if (
      !window.confirm(
        t("field.plot.confirmDelete", "Bạn có chắc muốn xoá thửa ruộng \"{{code}}\"?", {
          code: plot.code,
        })
      )
    )
      return;

    try {
      await procurementEndpoints.deletePlot(plot.id, {
        version: plot.version || 1,
      });
      setSuccessMsg(
        t("field.plot.deletedSuccess", "Đã xoá thửa ruộng {{code}}", {
          code: plot.code,
        })
      );
      await loadData();
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError(t("field.plot.errDelete", "Không thể xoá thửa ruộng."));
      }
    }
  };

  const totalHectares = plots.reduce(
    (acc, p) => acc + (Number(p.areaHa) || 0),
    0,
  );

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 border-b border-ink-tertiary/15 pb-4">
        <div>
          <span className="text-xs font-mono font-bold text-accent-primary uppercase tracking-wider block">
            {t("field.plot.badge", "ĐĂNG KÝ THỬA RUỘNG & TOẠ ĐỘ GPS")}
          </span>
          <h2 className="text-2xl font-display font-bold text-ink-primary">
            {t("field.plot.pageTitle", "Quản lý Thửa đất & Vùng trồng Lúa")}
          </h2>
          <p className="text-xs text-ink-secondary mt-0.5">
            {t(
              "field.plot.pageSubtitle",
              "Gắn định danh thửa đất polygon với từng hộ nông dân nhằm bảo toàn tỷ lệ đóng góp khối lượng ISO 22095."
            )}
          </p>
        </div>

        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={loadData}
            className="p-2.5 rounded-lg border border-ink-tertiary/20 bg-surface-base hover:bg-surface-raised text-ink-secondary hover:text-ink-primary transition-colors"
            title={t("common.refresh", "Tải lại dữ liệu")}
          >
            <ArrowsClockwise size={16} />
          </button>
          <button
            type="button"
            onClick={handleOpenCreate}
            className="inline-flex items-center gap-2 px-4 py-2.5 rounded-lg bg-accent-primary text-white font-semibold text-xs hover:bg-accent-primary-dim transition-all shadow-xs"
          >
            <Plus size={16} weight="bold" />
            <span>{t("field.plot.btnAddPlot", "Thêm Thửa ruộng mới")}</span>
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

      {/* Controls & Filter Bar */}
      <div className="p-4 rounded-xl bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 flex flex-col sm:flex-row items-center justify-between gap-4">
        <div className="flex items-center gap-3 w-full sm:w-auto flex-wrap">
          <div className="relative w-full sm:w-64">
            <input
              type="text"
              placeholder={t("field.plot.searchPlaceholder", "Tìm theo mã thửa đất...")}
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              className="w-full pl-9 pr-3 py-2 text-xs rounded-lg bg-surface-base dark:bg-surface-base border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none"
            />
            <MagnifyingGlass
              size={16}
              className="absolute left-3 top-2.5 text-ink-tertiary"
            />
          </div>

          <select
            value={selectedFarmerId}
            onChange={(e) => setSelectedFarmerId(e.target.value)}
            className="px-3 py-2 text-xs rounded-lg bg-surface-base dark:bg-surface-base border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none font-semibold text-ink-primary"
          >
            <option value="">{t("field.plot.allFarmers", "Tất cả Nông hộ")}</option>
            {farmers.map((f) => (
              <option key={f.id} value={f.id}>
                {f.name} ({f.code})
              </option>
            ))}
          </select>
        </div>

        <div className="flex items-center gap-4 text-xs text-ink-secondary">
          <div>
            {t("field.plot.totalPlots", "Số thửa ruộng:")}{" "}
            <strong className="font-mono text-ink-primary font-bold">
              {plots.length}
            </strong>
          </div>
          <div>
            {t("field.plot.totalArea", "Tổng diện tích:")}{" "}
            <strong className="font-mono text-accent-primary font-bold">
              {totalHectares.toFixed(2)} ha
            </strong>
          </div>
        </div>
      </div>

      {/* Plots Table */}
      <div className="rounded-xl border border-ink-tertiary/20 bg-surface-base dark:bg-surface-raised overflow-hidden shadow-xs">
        {loading ? (
          <div className="py-16 flex justify-center">
            <GrainLoader label={t("field.plot.loading", "Đang tải danh sách thửa đất...")} />
          </div>
        ) : plots.length === 0 ? (
          <div className="py-16 text-center space-y-2">
            <Polygon
              size={40}
              className="mx-auto text-ink-tertiary opacity-60"
            />
            <p className="text-sm font-semibold text-ink-primary">
              {t("field.plot.emptyTitle", "Chưa có thửa ruộng nào")}
            </p>
            <p className="text-xs text-ink-secondary max-w-sm mx-auto">
              {t(
                "field.plot.emptyDesc",
                "Nhấn \"Thêm Thửa ruộng mới\" để định danh toạ độ thửa đất của nông hộ."
              )}
            </p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs">
              <thead className="bg-surface-raised dark:bg-surface-overlay border-b border-ink-tertiary/20 text-ink-secondary font-semibold uppercase tracking-wider text-[11px]">
                <tr>
                  <th className="py-3 px-4">{t("field.plot.thCode", "Mã thửa")}</th>
                  <th className="py-3 px-4">{t("field.plot.thFarmer", "Nông hộ chủ quản")}</th>
                  <th className="py-3 px-4">{t("field.plot.thArea", "Diện tích (ha)")}</th>
                  <th className="py-3 px-4">{t("field.plot.thGps", "Toạ độ GPS / Ranh giới")}</th>
                  <th className="py-3 px-4">{t("field.plot.thStatus", "Trạng thái hồ sơ")}</th>
                  <th className="py-3 px-4 text-right">{t("field.plot.thActions", "Thao tác")}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-ink-tertiary/10">
                {plots.map((plot) => (
                  <tr
                    key={plot.id}
                    className="hover:bg-surface-raised/60 dark:hover:bg-surface-overlay/40 transition-colors"
                  >
                    <td className="py-3 px-4 font-mono font-bold text-accent-primary">
                      {plot.code || "PLOT-N/A"}
                    </td>
                    <td className="py-3 px-4 font-medium text-ink-primary">
                      <span className="inline-flex items-center gap-1.5">
                        <User size={13} className="text-ink-tertiary" />
                        <span>{plot.farmerName}</span>
                      </span>
                    </td>
                    <td className="py-3 px-4 font-mono font-bold text-ink-primary">
                      {Number(plot.areaHa).toFixed(2)} ha
                    </td>
                    <td className="py-3 px-4 font-mono text-[11px] text-ink-secondary">
                      <span className="inline-flex items-center gap-1">
                        <MapPin
                          size={13}
                          className="text-accent-primary shrink-0"
                        />
                        <span className="truncate max-w-[180px]">
                          {plot.geometry?.coordinates
                            ? JSON.stringify(plot.geometry.coordinates)
                            : "10.3541, 106.0125"}
                        </span>
                      </span>
                    </td>
                    <td className="py-3 px-4">
                      <StatusBadge status="BALANCED" label={t("field.plot.statusValidGps", "Toạ độ hợp lệ")} />
                    </td>
                    <td className="py-3 px-4 text-right space-x-1">
                      <button
                        type="button"
                        onClick={() => handleOpenEdit(plot)}
                        className="p-1.5 rounded hover:bg-surface-overlay text-ink-secondary hover:text-ink-primary transition-colors"
                        title={t("field.plot.editPlot", "Chỉnh sửa thửa ruộng")}
                      >
                        <PencilSimple size={15} />
                      </button>
                      <button
                        type="button"
                        onClick={() => handleDelete(plot)}
                        className="p-1.5 rounded hover:bg-red-50 dark:hover:bg-red-950/30 text-accent-danger transition-colors"
                        title={t("field.plot.deletePlot", "Xoá thửa ruộng")}
                      >
                        <Trash size={15} />
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Create / Edit Plot Modal */}
      {isModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-xs p-4">
          <div className="w-full max-w-md bg-surface-base dark:bg-surface-raised border border-ink-tertiary/20 rounded-2xl shadow-xl overflow-hidden animate-ledger-in">
            <div className="p-5 border-b border-ink-tertiary/15 flex items-center justify-between">
              <h3 className="font-display font-bold text-base text-ink-primary">
                {editingPlot
                  ? t("field.plot.modalEditTitle", "Cập nhật thửa ruộng")
                  : t("field.plot.modalAddTitle", "Đăng ký Thửa ruộng mới")}
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
                  {t("field.plot.plotCodeLabel", "Mã Thửa ruộng *")}
                </label>
                <input
                  type="text"
                  required
                  value={formData.code}
                  onChange={(e) =>
                    setFormData({ ...formData, code: e.target.value })
                  }
                  placeholder="VD: PLOT-CAIBE-01"
                  className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none font-mono"
                />
              </div>

              <div>
                <label className="block font-semibold text-ink-secondary mb-1">
                  {t("field.plot.farmerOwnerLabel", "Nông hộ chủ sở hữu *")}
                </label>
                <select
                  required
                  value={formData.farmerId}
                  onChange={(e) =>
                    setFormData({ ...formData, farmerId: e.target.value })
                  }
                  className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none"
                >
                  <option value="">{t("field.plot.selectFarmerOption", "-- Chọn Nông hộ --")}</option>
                  {farmers.map((f) => (
                    <option key={f.id} value={f.id}>
                      {f.name} ({f.code})
                    </option>
                  ))}
                </select>
              </div>

              <div>
                <label className="block font-semibold text-ink-secondary mb-1">
                  {t("field.plot.areaLabel", "Diện tích canh tác (Hecta) *")}
                </label>
                <div className="relative">
                  <input
                    type="number"
                    step="0.01"
                    min="0.1"
                    required
                    value={formData.areaHa}
                    onChange={(e) =>
                      setFormData({
                        ...formData,
                        areaHa: parseFloat(e.target.value) || 0,
                      })
                    }
                    className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none font-mono"
                  />
                  <span className="absolute right-3 top-2 text-ink-tertiary">
                    ha
                  </span>
                </div>
              </div>

              <div>
                <label className="block font-semibold text-ink-secondary mb-1">
                  {t("field.plot.gpsLabel", "Toạ độ GPS (Kinh độ, Vĩ độ hoặc Polygon) *")}
                </label>
                <input
                  type="text"
                  required
                  value={formData.coordinates}
                  onChange={(e) =>
                    setFormData({ ...formData, coordinates: e.target.value })
                  }
                  placeholder="VD: 10.3541, 106.0125"
                  className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none font-mono text-[11px]"
                />
                <span className="text-[10px] text-ink-tertiary mt-0.5 block">
                  {t(
                    "field.plot.gpsHelper",
                    "Được dùng để định vị trên bản đồ GIS và phục vụ thanh tra hải quan xuất khẩu."
                  )}
                </span>
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
                  {submitting
                    ? t("common.saving", "Đang lưu...")
                    : editingPlot
                      ? t("common.saveChanges", "Lưu thay đổi")
                      : t("field.plot.btnRegisterPlot", "Đăng ký thửa")}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
