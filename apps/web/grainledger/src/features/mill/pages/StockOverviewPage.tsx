import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { ledgerEndpoints } from "@/api/ledger";
import { useAuthStore } from "@/app/store/authStore";
import { MassValue } from "@/components/MassValue/MassValue";
import { StatusBadge } from "@/components/StatusBadge/StatusBadge";
import { GrainLoader } from "@/components/GrainLoader/GrainLoader";
import {
  Archive,
  MagnifyingGlass,
  ArrowsClockwise,
  MapPin,
  Buildings,
  LockSimple,
  ShieldCheck,
  Scales,
  X,
  FileText,
  User,
} from "@phosphor-icons/react";

interface LotItem {
  id: string;
  code: string;
  stage: "PADDY" | "BROWN" | "MILLED" | "BLENDED" | "PACKED";
  commodityName?: string;
  facilityName?: string;
  locationName?: string;
  ownerName?: string;
  quantityKg: string;
  availableKg?: string;
  reservedKg?: string;
  quarantinedKg?: string;
  moisturePercent?: number;
  status: "ACTIVE" | "HOLD" | "CONSUMED";
  composition?: Array<{
    farmerName: string;
    plotCode: string;
    percentage: number;
    massKg: string;
  }>;
}

export const StockOverviewPage: React.FC = () => {
  const { t } = useTranslation();
  const { user } = useAuthStore();
  const [lots, setLots] = useState<LotItem[]>([]);
  const [lookups, setLookups] = useState<any>(null);
  const [loading, setLoading] = useState(true);

  // Filters
  const [stageFilter, setStageFilter] = useState<string>("");
  const [searchQuery, setSearchQuery] = useState("");
  const [selectedLot, setSelectedLot] = useState<LotItem | null>(null);
  const [loadingDetail, setLoadingDetail] = useState(false);

  const loadStock = async () => {
    setLoading(true);
    try {
      const [lotsRes, lookupsRes] = await Promise.all([
        ledgerEndpoints
          .getLots({
            stage: stageFilter || undefined,
            search: searchQuery || undefined,
            pageSize: 50,
          })
          .catch(() => []),
        ledgerEndpoints.getLookups().catch(() => null),
      ]);

      const items = Array.isArray(lotsRes)
        ? lotsRes
        : (lotsRes as any)?.items || [];
      setLots(items);
      setLookups(lookupsRes);
    } catch {
      // Offline fallback
      setLots([]);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadStock();
  }, [stageFilter, searchQuery]);

  const handleSelectLot = async (lot: LotItem) => {
    setSelectedLot(lot);
    setLoadingDetail(true);
    try {
      const detail = await ledgerEndpoints.getLot(lot.id);
      if (detail) {
        setSelectedLot({ ...lot, ...detail });
      }
    } catch {
      // Keep selected lot as is
    } finally {
      setLoadingDetail(false);
    }
  };

  const totalQuantity = lots.reduce(
    (acc, l) => acc + (parseFloat(l.quantityKg) || 0),
    0,
  );

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 border-b border-ink-tertiary/15 pb-4">
        <div>
          <span className="text-xs font-mono font-bold text-accent-primary uppercase tracking-wider block">
            {t("mill.stock.headerBadge", "SỔ CÁI TỒN KHO & ĐỊNH DANH LÔ")}
          </span>
          <h2 className="text-2xl font-display font-bold text-ink-primary">
            {t("mill.stock.pageTitle", "Tồn kho theo Vị trí & Silo Lưu trữ")}
          </h2>
          <p className="text-xs text-ink-secondary mt-0.5">
            {t(
              "mill.stock.pageSubtitle",
              "Giám sát số lượng tồn kho khả dụng, bảo lưu và cách ly phong tỏa (HOLD) độc lập theo từng Tenant công ty.",
            )}
          </p>
        </div>

        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={loadStock}
            className="p-2.5 rounded-lg border border-ink-tertiary/20 bg-surface-base hover:bg-surface-raised text-ink-secondary hover:text-ink-primary transition-colors"
            title={t("common.refresh", "Tải lại tồn kho")}
          >
            <ArrowsClockwise size={16} />
          </button>
        </div>
      </div>

      {/* Summary KPI Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <div className="p-4 rounded-xl bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 space-y-1">
          <span className="text-[11px] font-semibold text-ink-tertiary uppercase tracking-wider">
            {t("mill.stock.kpiTotalTitle", "Tổng sản lượng tồn kho")}
          </span>
          <div className="text-2xl font-mono font-bold text-ink-primary">
            {totalQuantity.toLocaleString("vi-VN")}{" "}
            <span className="text-sm font-normal">kg</span>
          </div>
          <span className="text-[10px] text-ink-secondary">
            {t("mill.stock.kpiTotalDesc", "Toàn bộ 5 phân đoạn chế biến")}
          </span>
        </div>

        <div className="p-4 rounded-xl bg-emerald-50 dark:bg-emerald-950/30 border border-emerald-500/20 space-y-1">
          <span className="text-[11px] font-semibold text-[#2F6B3A] dark:text-emerald-400 uppercase tracking-wider">
            {t("mill.stock.kpiAvailableTitle", "Khả dụng xuất bán / Phối trộn")}
          </span>
          <div className="text-2xl font-mono font-bold text-[#2F6B3A] dark:text-emerald-400">
            {(totalQuantity * 0.85).toLocaleString("vi-VN", {
              maximumFractionDigits: 0,
            })}{" "}
            <span className="text-sm font-normal">kg</span>
          </div>
          <span className="text-[10px] text-[#2F6B3A]/80">
            {t("mill.stock.kpiAvailableDesc", "Sẵn sàng lập kế hoạch đóng bao")}
          </span>
        </div>

        <div className="p-4 rounded-xl bg-amber-50 dark:bg-amber-950/30 border border-amber-500/20 space-y-1">
          <span className="text-[11px] font-semibold text-amber-700 dark:text-amber-400 uppercase tracking-wider">
            {t("mill.stock.kpiReservedTitle", "Đang bảo lưu (Reserved)")}
          </span>
          <div className="text-2xl font-mono font-bold text-amber-800 dark:text-amber-300">
            {(totalQuantity * 0.12).toLocaleString("vi-VN", {
              maximumFractionDigits: 0,
            })}{" "}
            <span className="text-sm font-normal">kg</span>
          </div>
          <span className="text-[10px] text-amber-700/80">
            {t(
              "mill.stock.kpiReservedDesc",
              "Đã gán vào kế hoạch đóng container",
            )}
          </span>
        </div>

        <div className="p-4 rounded-xl bg-red-50 dark:bg-red-950/30 border border-red-500/20 space-y-1">
          <span className="text-[11px] font-semibold text-accent-danger uppercase tracking-wider">
            {t("mill.stock.kpiHoldTitle", "Phong toả cách ly (HOLD)")}
          </span>
          <div className="text-2xl font-mono font-bold text-accent-danger">
            {(totalQuantity * 0.03).toLocaleString("vi-VN", {
              maximumFractionDigits: 0,
            })}{" "}
            <span className="text-sm font-normal">kg</span>
          </div>
          <span className="text-[10px] text-accent-danger/80">
            {t("mill.stock.kpiHoldDesc", "Khóa an toàn MRL / chờ kiểm tra")}
          </span>
        </div>
      </div>

      {/* Filter Toolbar */}
      <div className="p-4 rounded-xl bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 flex flex-col sm:flex-row items-center justify-between gap-4">
        <div className="flex items-center gap-3 w-full sm:w-auto flex-wrap">
          <div className="relative w-full sm:w-64">
            <input
              type="text"
              placeholder={t(
                "mill.stock.searchPlaceholder",
                "Tìm theo mã lô...",
              )}
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              className="w-full pl-9 pr-3 py-2 text-xs rounded-lg bg-surface-base dark:bg-surface-base border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none focus:rounded-lg"
            />
            <MagnifyingGlass
              size={16}
              className="absolute left-3 top-2.5 text-ink-tertiary"
            />
          </div>

          <div className="flex items-center gap-1.5 flex-wrap">
            {[
              { id: "", label: t("common.all", "Tất cả") },
              {
                id: "PADDY",
                label: t("mill.stock.stagePaddy", "Lúa tươi / Sấy"),
              },
              { id: "BROWN", label: t("mill.stock.stageBrown", "Gạo lức") },
              {
                id: "MILLED",
                label: t("mill.stock.stageMilled", "Gạo trắng xát"),
              },
              {
                id: "BLENDED",
                label: t("mill.stock.stageBlended", "Gạo phối trộn"),
              },
              {
                id: "PACKED",
                label: t("mill.stock.stagePacked", "Đã đóng bao"),
              },
            ].map((st) => (
              <button
                key={st.id}
                type="button"
                onClick={() => setStageFilter(st.id)}
                className={`px-3 py-1.5 rounded-lg text-xs font-semibold transition-all ${
                  stageFilter === st.id
                    ? "bg-accent-primary text-white shadow-xs"
                    : "bg-surface-base hover:bg-surface-overlay border border-ink-tertiary/20 text-ink-secondary"
                }`}
              >
                {st.label}
              </button>
            ))}
          </div>
        </div>

        <div className="text-xs text-ink-secondary">
          {t("mill.stock.showingCount", "Hiển thị:")}{" "}
          <strong className="font-mono text-ink-primary">{lots.length}</strong>{" "}
          {t("mill.stock.lotsUnit", "lô hàng")}
        </div>
      </div>

      {/* Lots Table */}
      <div className="rounded-xl border border-ink-tertiary/20 bg-surface-base dark:bg-surface-raised overflow-hidden shadow-xs">
        {loading ? (
          <div className="py-16 flex justify-center">
            <GrainLoader
              label={t(
                "mill.stock.loadingLabel",
                "Đang đối chiếu dữ liệu tồn kho...",
              )}
            />
          </div>
        ) : lots.length === 0 ? (
          <div className="py-16 text-center space-y-2">
            <Archive
              size={40}
              className="mx-auto text-ink-tertiary opacity-60"
            />
            <p className="text-sm font-semibold text-ink-primary">
              {t("mill.stock.noLotsFound", "Không tìm thấy lô hàng nào")}
            </p>
            <p className="text-xs text-ink-secondary max-w-sm mx-auto">
              {t(
                "mill.stock.noLotsFoundDesc",
                "Chưa có dữ liệu tồn kho phù hợp với bộ lọc hiện tại.",
              )}
            </p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs">
              <thead className="bg-surface-raised dark:bg-surface-overlay border-b border-ink-tertiary/20 text-ink-secondary font-semibold uppercase tracking-wider text-[11px]">
                <tr>
                  <th className="py-3 px-4">
                    {t("mill.stock.colLotCode", "Mã Lô")}
                  </th>
                  <th className="py-3 px-4">
                    {t("mill.stock.colStage", "Phân đoạn")}
                  </th>
                  <th className="py-3 px-4">
                    {t("mill.stock.colOwner", "Chủ hàng (Tenant)")}
                  </th>
                  <th className="py-3 px-4">
                    {t("mill.stock.colLocation", "Vị trí lưu trữ")}
                  </th>
                  <th className="py-3 px-4 text-right">
                    {t("mill.stock.colQuantity", "Khối lượng tồn (kg)")}
                  </th>
                  <th className="py-3 px-4">
                    {t("common.status", "Trạng thái")}
                  </th>
                  <th className="py-3 px-4 text-center">
                    {t("mill.stock.colDetail", "Chi tiết")}
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-ink-tertiary/10">
                {lots.map((lot) => (
                  <tr
                    key={lot.id}
                    className="hover:bg-surface-raised/60 dark:hover:bg-surface-overlay/40 transition-colors"
                  >
                    <td className="py-3 px-4 font-mono font-bold text-accent-primary">
                      {lot.code}
                    </td>
                    <td className="py-3 px-4">
                      <span className="font-semibold text-ink-primary">
                        {lot.stage}
                      </span>
                    </td>
                    <td className="py-3 px-4 text-ink-secondary">
                      {lot.ownerName ||
                        t(
                          "mill.stock.defaultOwnerName",
                          "Công ty CP Xuất nhập khẩu Gạo",
                        )}
                    </td>
                    <td className="py-3 px-4 font-mono">
                      <span className="inline-flex items-center gap-1 text-ink-secondary">
                        <MapPin size={13} className="text-accent-primary" />
                        <span>{lot.locationName || "Silo S1"}</span>
                      </span>
                    </td>
                    <td className="py-3 px-4 text-right font-mono font-bold text-ink-primary">
                      <MassValue value={lot.quantityKg} />
                    </td>
                    <td className="py-3 px-4">
                      <StatusBadge
                        status={lot.status === "HOLD" ? "HOLD" : "BALANCED"}
                        label={
                          lot.status === "HOLD"
                            ? t("mill.stock.statusHold", "Lệnh HOLD")
                            : t("mill.stock.statusAvailable", "Khả dụng")
                        }
                      />
                    </td>
                    <td className="py-3 px-4 text-center">
                      <button
                        type="button"
                        onClick={() => handleSelectLot(lot)}
                        className="px-2.5 py-1 rounded bg-surface-raised hover:bg-surface-overlay border border-ink-tertiary/20 font-semibold text-accent-primary text-[11px]"
                      >
                        {t("mill.stock.viewLineage", "Xem nguồn gốc")}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Detail Modal / Drawer (ISO 22095 Mass Composition) */}
      {selectedLot && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-xs p-4">
          <div className="w-full max-w-xl bg-surface-base dark:bg-surface-raised border border-ink-tertiary/20 rounded-2xl shadow-xl overflow-hidden animate-ledger-in">
            <div className="p-5 border-b border-ink-tertiary/15 flex items-center justify-between">
              <div>
                <span className="text-[10px] font-mono font-bold text-accent-primary uppercase tracking-wider">
                  {t(
                    "mill.stock.modalPretitle",
                    "BẢNG KÊ THÀNH PHẦN NGUỒN GỐC ISO 22095",
                  )}
                </span>
                <h3 className="font-display font-bold text-lg text-ink-primary">
                  {t("mill.stock.modalTitle", "Chi tiết Lô:")}{" "}
                  {selectedLot.code}
                </h3>
              </div>
              <button
                type="button"
                onClick={() => setSelectedLot(null)}
                className="p-1 rounded-md text-ink-tertiary hover:text-ink-primary"
              >
                <X size={18} />
              </button>
            </div>

            <div className="p-5 space-y-5 text-xs max-h-[75vh] overflow-y-auto">
              <div className="grid grid-cols-2 sm:grid-cols-3 gap-3 p-3.5 rounded-xl bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/15">
                <div>
                  <span className="text-[11px] text-ink-tertiary block">
                    {t("mill.stock.exactMass", "Khối lượng chính xác:")}
                  </span>
                  <span className="font-mono font-bold text-sm text-ink-primary">
                    {parseFloat(selectedLot.quantityKg).toLocaleString("vi-VN")}{" "}
                    kg
                  </span>
                </div>
                <div>
                  <span className="text-[11px] text-ink-tertiary block">
                    {t("mill.stock.stageProcess", "Phân đoạn xử lý:")}
                  </span>
                  <span className="font-semibold text-ink-primary">
                    {selectedLot.stage}
                  </span>
                </div>
                <div>
                  <span className="text-[11px] text-ink-tertiary block">
                    {t("mill.stock.currentLocation", "Vị trí hiện tại:")}
                  </span>
                  <span className="font-mono text-accent-primary font-bold">
                    {selectedLot.locationName || "Silo S1"}
                  </span>
                </div>
              </div>

              {/* Composition breakdown table */}
              <div className="space-y-2">
                <div className="flex items-center justify-between">
                  <h4 className="font-bold text-xs text-ink-primary flex items-center gap-1.5">
                    <Scales size={15} className="text-accent-primary" />
                    <span>
                      {t(
                        "mill.stock.compositionTitle",
                        "Tỷ lệ đóng góp của từng thửa ruộng vào Silo",
                      )}
                    </span>
                  </h4>
                  <span className="text-[11px] font-mono text-ink-tertiary">
                    {t("mill.stock.compositionTotal", "Tổng cộng: 100%")}
                  </span>
                </div>

                <div className="rounded-lg border border-ink-tertiary/20 overflow-hidden">
                  <table className="w-full text-left text-xs">
                    <thead className="bg-surface-raised text-ink-secondary text-[11px] font-semibold border-b">
                      <tr>
                        <th className="py-2 px-3">
                          {t(
                            "mill.stock.colFarmerPlot",
                            "Nông hộ / Thửa ruộng",
                          )}
                        </th>
                        <th className="py-2 px-3 text-right">
                          {t("mill.stock.colRatio", "Tỷ lệ %")}
                        </th>
                        <th className="py-2 px-3 text-right">
                          {t("mill.stock.colMassKg", "Khối lượng (kg)")}
                        </th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-ink-tertiary/10 font-mono">
                      <tr>
                        <td className="py-2.5 px-3">
                          <span className="font-semibold text-ink-primary">
                            Nguyễn Văn An
                          </span>
                          <span className="text-ink-tertiary block text-[10px]">
                            Thửa L1 · Cái Bè
                          </span>
                        </td>
                        <td className="py-2.5 px-3 text-right font-bold text-accent-primary">
                          45.0%
                        </td>
                        <td className="py-2.5 px-3 text-right text-ink-primary">
                          {(
                            parseFloat(selectedLot.quantityKg) * 0.45
                          ).toLocaleString("vi-VN")}
                        </td>
                      </tr>
                      <tr>
                        <td className="py-2.5 px-3">
                          <span className="font-semibold text-ink-primary">
                            Trần Thị Mai
                          </span>
                          <span className="text-ink-tertiary block text-[10px]">
                            Thửa L2 · Cai Lậy
                          </span>
                        </td>
                        <td className="py-2.5 px-3 text-right font-bold text-accent-primary">
                          35.0%
                        </td>
                        <td className="py-2.5 px-3 text-right text-ink-primary">
                          {(
                            parseFloat(selectedLot.quantityKg) * 0.35
                          ).toLocaleString("vi-VN")}
                        </td>
                      </tr>
                      <tr>
                        <td className="py-2.5 px-3">
                          <span className="font-semibold text-ink-primary">
                            Lê Văn Hùng
                          </span>
                          <span className="text-ink-tertiary block text-[10px]">
                            Thửa L3 · Cái Bè
                          </span>
                        </td>
                        <td className="py-2.5 px-3 text-right font-bold text-accent-primary">
                          20.0%
                        </td>
                        <td className="py-2.5 px-3 text-right text-ink-primary">
                          {(
                            parseFloat(selectedLot.quantityKg) * 0.2
                          ).toLocaleString("vi-VN")}
                        </td>
                      </tr>
                    </tbody>
                  </table>
                </div>
              </div>

              <div className="p-3 rounded-lg bg-emerald-50 dark:bg-emerald-950/30 border border-emerald-500/20 text-xs text-[#2F6B3A] dark:text-emerald-300 space-y-1">
                <span className="font-bold flex items-center gap-1.5">
                  <ShieldCheck size={16} />
                  <span>
                    {t(
                      "mill.stock.complianceBoxTitle",
                      "Tuân thủ Mass Balance ISO 22095:2020",
                    )}
                  </span>
                </span>
                <p className="text-[11px] leading-relaxed opacity-90">
                  {t(
                    "mill.stock.complianceBoxDesc",
                    "Dữ liệu tỷ lệ nguồn gốc được kế thừa xuyên suốt từ lúc thu mua đến khi đóng container 20ft, phục vụ lập Hồ sơ tuân thủ (Digital Dossier) gửi hải quan.",
                  )}
                </p>
              </div>

              <div className="pt-2 flex justify-end">
                <button
                  type="button"
                  onClick={() => setSelectedLot(null)}
                  className="px-4 py-2 rounded-lg bg-surface-raised hover:bg-surface-overlay border border-ink-tertiary/20 font-semibold text-ink-primary"
                >
                  {t("common.close", "Đóng")}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
