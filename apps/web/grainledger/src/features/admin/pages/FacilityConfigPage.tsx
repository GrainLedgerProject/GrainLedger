import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { ledgerEndpoints } from "@/api/ledger";
import { GrainLoader } from "@/components/GrainLoader/GrainLoader";
import { StatusBadge } from "@/components/StatusBadge/StatusBadge";
import {
  Warehouse,
  Plus,
  ArrowsClockwise,
  MapPin,
  Wrench,
  CheckCircle,
  WarningCircle,
  X,
  Buildings,
  PencilSimple,
  Trash,
  Broom,
} from "@phosphor-icons/react";

interface FacilityItem {
  id: string;
  name: string;
  code: string;
  address?: string;
  locations?: Array<{
    id: string;
    code: string;
    name: string;
    type: "SILO" | "BIN" | "WAREHOUSE";
    capacityKg: number;
    currentFillKg?: number;
  }>;
  equipment?: Array<{
    id: string;
    code: string;
    name: string;
    kind: "DRYER" | "MILL" | "BLENDER" | "PACKER";
  }>;
}

export const FacilityConfigPage: React.FC = () => {
  const { t } = useTranslation();
  const [, setSetupData] = useState<unknown>(null);
  const [facilities, setFacilities] = useState<FacilityItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);

  // Modal State
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [modalType, setModalType] = useState<"FACILITY" | "LOCATION" | "EQUIPMENT">("FACILITY");
  const [isEdit, setIsEdit] = useState(false);
  const [editId, setEditId] = useState<string | null>(null);
  const [targetFacilityId, setTargetFacilityId] = useState<string>("");
  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [extra, setExtra] = useState("");
  const [submitting, setSubmitting] = useState(false);

  const loadData = async () => {
    setLoading(true);
    setError(null);
    try {
      const setup = await ledgerEndpoints.getSetup().catch(() => null);
      setSetupData(setup);

      // Map facilities
      const facs: FacilityItem[] = (setup as { facilities?: FacilityItem[] })?.facilities || [
        {
          id: "fac-1",
          code: "FAC-TIENGIANG-01",
          name: "Nhà máy Xay xát & Sấy lúa Tiền Giang",
          address: "Huyện Cái Bè, Tỉnh Tiền Giang",
          locations: [
            {
              id: "loc-1",
              code: "SILO-01",
              name: "Silo lưu trữ lúa khô S1",
              type: "SILO",
              capacityKg: 500000,
              currentFillKg: 120000,
            },
            {
              id: "loc-2",
              code: "SILO-02",
              name: "Silo lưu trữ lúa khô S2",
              type: "SILO",
              capacityKg: 500000,
              currentFillKg: 0,
            },
            {
              id: "loc-3",
              code: "BIN-01",
              name: "Thùng trung chuyển gạo trắng",
              type: "BIN",
              capacityKg: 80000,
              currentFillKg: 45000,
            },
          ],
          equipment: [
            {
              id: "eq-1",
              code: "DRYER-LINE-A",
              name: "Lò sấy vỉ ngang 50 tấn/mẻ",
              kind: "DRYER",
            },
            {
              id: "eq-2",
              code: "MILL-LINE-01",
              name: "Dây chuyền bóc vỏ & xát trắng A",
              kind: "MILL",
            },
            {
              id: "eq-3",
              code: "BLEND-LINE-01",
              name: "Máy trộn tỷ lệ tấm tự động",
              kind: "BLENDER",
            },
          ],
        },
      ];
      setFacilities(facs);
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError(t("admin.facility.errorLoad", "Không thể tải cấu hình cơ sở."));
      }
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadData();
  }, []);

  const handleOpenAdd = (type: "FACILITY" | "LOCATION" | "EQUIPMENT", facId?: string) => {
    setModalType(type);
    setIsEdit(false);
    setEditId(null);
    setTargetFacilityId(facId || facilities[0]?.id || "");
    setCode("");
    setName("");
    setExtra(type === "LOCATION" ? "500000" : "");
    setIsModalOpen(true);
  };

  const handleOpenEdit = (
    type: "FACILITY" | "LOCATION" | "EQUIPMENT",
    item: { id: string; code: string; name: string; extraVal?: string; facId?: string }
  ) => {
    setModalType(type);
    setIsEdit(true);
    setEditId(item.id);
    setTargetFacilityId(item.facId || facilities[0]?.id || "");
    setCode(item.code);
    setName(item.name);
    setExtra(item.extraVal || "");
    setIsModalOpen(true);
  };

  const handleDelete = async (type: "FACILITY" | "LOCATION" | "EQUIPMENT", id: string, label: string) => {
    if (!window.confirm(`Bạn có chắc chắn muốn xóa "${label}"?`)) return;
    try {
      if (type === "FACILITY") {
        await ledgerEndpoints.deleteFacility(id);
      } else if (type === "LOCATION") {
        await ledgerEndpoints.deleteLocation(id);
      } else {
        await ledgerEndpoints.deleteEquipment(id);
      }
      setSuccessMsg(`Đã xóa thành công ${label}.`);
      await loadData();
    } catch (err: unknown) {
      if (err instanceof Error) setError(err.message);
    }
  };

  const handleClearLocation = async (id: string, codeStr: string) => {
    if (!window.confirm(`Xác nhận giải phóng và vệ sinh mặt bằng Silo ${codeStr}?`)) return;
    try {
      await ledgerEndpoints.clearLocation(id, { cleanedAt: new Date().toISOString() });
      setSuccessMsg(`Đã xác nhận vệ sinh & giải phóng Silo ${codeStr} thành công.`);
      await loadData();
    } catch (err: unknown) {
      if (err instanceof Error) setError(err.message);
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitting(true);
    setError(null);

    try {
      if (isEdit && editId) {
        if (modalType === "FACILITY") {
          await ledgerEndpoints.updateFacility(editId, { code, name, address: extra });
        } else if (modalType === "LOCATION") {
          await ledgerEndpoints.updateLocation(editId, {
            facilityId: targetFacilityId,
            code,
            name,
            type: "SILO",
            capacityKg: parseFloat(extra) || 500000,
          });
        } else {
          await ledgerEndpoints.updateEquipment(editId, {
            facilityId: targetFacilityId,
            code,
            name,
            kind: "MILL",
          });
        }
        setSuccessMsg(`Đã cập nhật thành công "${name}".`);
      } else {
        if (modalType === "FACILITY") {
          await ledgerEndpoints.createFacility({ code, name, address: extra });
        } else if (modalType === "LOCATION") {
          await ledgerEndpoints.createLocation({
            facilityId: targetFacilityId || facilities[0]?.id || crypto.randomUUID(),
            code,
            name,
            type: "SILO",
            capacityKg: parseFloat(extra) || 500000,
          });
        } else {
          await ledgerEndpoints.createEquipment({
            facilityId: targetFacilityId || facilities[0]?.id || crypto.randomUUID(),
            code,
            name,
            kind: "MILL",
          });
        }
        setSuccessMsg(
          t("admin.facility.successCreate", "Đã tạo thành công {{name}}", { name })
        );
      }

      setIsModalOpen(false);
      await loadData();
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError(t("admin.facility.errorCreate", "Thao tác không thành công."));
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
            {t("admin.facility.tag", "CẤU HÌNH NHÀ MÁY, SILO & THIẾT BỊ")}
          </span>
          <h2 className="text-2xl font-display font-bold text-ink-primary">
            {t("admin.facility.title", "Cơ sở Vật chất & Vị trí Lưu trữ Silo")}
          </h2>
          <p className="text-xs text-ink-secondary mt-0.5">
            {t(
              "admin.facility.subtitle",
              "Cấu hình các silo lưu trữ, máy sấy và dây chuyền chế biến để hệ thống hạch toán dòng chảy cân bằng khối lượng."
            )}
          </p>
        </div>

        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={loadData}
            className="p-2.5 rounded-lg border border-ink-tertiary/20 bg-surface-base hover:bg-surface-raised text-ink-secondary hover:text-ink-primary transition-colors"
            title={t("common.reload", "Tải lại dữ liệu")}
          >
            <ArrowsClockwise size={16} />
          </button>
          <button
            type="button"
            onClick={() => handleOpenAdd("FACILITY")}
            className="inline-flex items-center gap-1.5 px-3 py-2.5 rounded-lg border border-ink-tertiary/20 bg-surface-base hover:bg-surface-raised text-ink-primary font-semibold text-xs transition-colors"
          >
            <Buildings size={15} />
            <span>Thêm Nhà máy</span>
          </button>
          <button
            type="button"
            onClick={() => handleOpenAdd("LOCATION")}
            className="inline-flex items-center gap-2 px-4 py-2.5 rounded-lg bg-accent-primary text-white font-semibold text-xs hover:bg-accent-primary-dim transition-all shadow-xs"
          >
            <Plus size={16} weight="bold" />
            <span>{t("admin.facility.addLocationBtn", "Thêm Silo / Kho chứa")}</span>
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

      {/* Facilities Cards */}
      {loading ? (
        <div className="py-16 flex justify-center">
          <GrainLoader label={t("admin.facility.loading", "Đang kiểm tra sơ đồ cơ sở nhà máy...")} />
        </div>
      ) : (
        <div className="space-y-6">
          {facilities.map((fac) => (
            <div
              key={fac.id}
              className="p-6 rounded-2xl bg-surface-base dark:bg-surface-raised border border-ink-tertiary/20 shadow-xs space-y-6"
            >
              <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 border-b border-ink-tertiary/15 pb-4">
                <div>
                  <div className="flex items-center gap-2">
                    <h3 className="font-display font-bold text-lg text-ink-primary">
                      {fac.name}
                    </h3>
                    <span className="font-mono text-xs font-bold text-accent-primary bg-emerald-100 dark:bg-emerald-950/60 px-2 py-0.5 rounded">
                      {fac.code}
                    </span>
                  </div>
                  <span className="text-xs text-ink-secondary mt-0.5 block flex items-center gap-1">
                    <MapPin size={13} className="text-ink-tertiary" />
                    <span>{fac.address || "Cái Bè, Tiền Giang, Việt Nam"}</span>
                  </span>
                </div>

                <div className="flex items-center gap-2">
                  <button
                    type="button"
                    onClick={() =>
                      handleOpenEdit("FACILITY", {
                        id: fac.id,
                        code: fac.code,
                        name: fac.name,
                        extraVal: fac.address,
                      })
                    }
                    className="p-1.5 rounded-lg hover:bg-surface-raised text-ink-secondary hover:text-accent-primary transition-colors text-xs inline-flex items-center gap-1 border border-ink-tertiary/15"
                  >
                    <PencilSimple size={14} />
                    <span>Sửa</span>
                  </button>
                  <button
                    type="button"
                    onClick={() => handleDelete("FACILITY", fac.id, fac.name)}
                    className="p-1.5 rounded-lg hover:bg-surface-raised text-ink-secondary hover:text-accent-danger transition-colors text-xs inline-flex items-center gap-1 border border-ink-tertiary/15"
                  >
                    <Trash size={14} />
                    <span>Xóa</span>
                  </button>
                  <button
                    type="button"
                    onClick={() => handleOpenAdd("EQUIPMENT", fac.id)}
                    className="px-2.5 py-1.5 rounded-lg bg-surface-raised hover:bg-surface-overlay text-ink-primary border border-ink-tertiary/20 text-xs font-medium inline-flex items-center gap-1"
                  >
                    <Plus size={13} />
                    <span>Thêm Thiết bị</span>
                  </button>
                </div>
              </div>

              {/* Silos / Locations Grid */}
              <div className="space-y-3">
                <div className="flex items-center justify-between">
                  <h4 className="font-bold text-xs uppercase tracking-wider text-ink-primary flex items-center gap-1.5">
                    <Warehouse size={15} className="text-accent-primary" />
                    <span>{t("admin.facility.siloSystem", "Hệ thống Silo & Kho chứa lúa")}</span>
                  </h4>
                  <span className="text-xs text-ink-secondary">
                    {t("admin.facility.locationCount", "{{count}} vị trí", {
                      count: fac.locations?.length || 0,
                    })}
                  </span>
                </div>

                <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
                  {fac.locations?.map((loc) => {
                    const fillPercent = loc.currentFillKg
                      ? Math.round((loc.currentFillKg / loc.capacityKg) * 100)
                      : 0;
                    return (
                      <div
                        key={loc.id}
                        className="p-3.5 rounded-xl bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/15 space-y-2 flex flex-col justify-between"
                      >
                        <div className="space-y-2">
                          <div className="flex items-start justify-between">
                            <div>
                              <span className="font-mono font-bold text-accent-primary text-xs block">
                                {loc.code}
                              </span>
                              <span className="font-semibold text-ink-primary text-xs">
                                {loc.name}
                              </span>
                            </div>
                            <StatusBadge
                              status={fillPercent > 0 ? "BALANCED" : "STALE"}
                              label={
                                fillPercent > 0
                                  ? t("admin.facility.percentFull", "{{percent}}% đầy", { percent: fillPercent })
                                  : t("admin.facility.emptyBadge", "Trống")
                              }
                            />
                          </div>

                          <div className="space-y-1 pt-1">
                            <div className="w-full h-1.5 rounded-full bg-ink-tertiary/20 overflow-hidden">
                              <div
                                className="h-full bg-accent-primary rounded-full transition-all"
                                style={{
                                  width: `${Math.min(fillPercent, 100)}%`,
                                }}
                              />
                            </div>
                            <div className="flex justify-between text-[11px] font-mono text-ink-secondary">
                              <span>
                                {loc.currentFillKg?.toLocaleString("vi-VN") || 0}{" "}
                                kg
                              </span>
                              <span>
                                {t("admin.facility.capacityLabel", "Sức chứa: {{val}} kg", {
                                  val: loc.capacityKg.toLocaleString("vi-VN"),
                                })}
                              </span>
                            </div>
                          </div>
                        </div>

                        {/* Location Actions */}
                        <div className="pt-2 border-t border-ink-tertiary/10 flex items-center justify-between text-xs">
                          <button
                            type="button"
                            onClick={() => handleClearLocation(loc.id, loc.code)}
                            className="p-1 text-ink-tertiary hover:text-accent-primary inline-flex items-center gap-1 text-[11px]"
                            title="Vệ sinh & giải phóng Silo"
                          >
                            <Broom size={13} />
                            <span>Vệ sinh Silo</span>
                          </button>
                          <div className="flex items-center gap-1">
                            <button
                              type="button"
                              onClick={() =>
                                handleOpenEdit("LOCATION", {
                                  id: loc.id,
                                  code: loc.code,
                                  name: loc.name,
                                  extraVal: String(loc.capacityKg),
                                  facId: fac.id,
                                })
                              }
                              className="p-1 text-ink-tertiary hover:text-ink-primary"
                              title="Sửa Silo"
                            >
                              <PencilSimple size={14} />
                            </button>
                            <button
                              type="button"
                              onClick={() => handleDelete("LOCATION", loc.id, loc.name)}
                              className="p-1 text-ink-tertiary hover:text-accent-danger"
                              title="Xóa Silo"
                            >
                              <Trash size={14} />
                            </button>
                          </div>
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>

              {/* Equipment Grid */}
              <div className="space-y-3 pt-2">
                <div className="flex items-center justify-between">
                  <h4 className="font-bold text-xs uppercase tracking-wider text-ink-primary flex items-center gap-1.5">
                    <Wrench size={15} className="text-accent-primary" />
                    <span>{t("admin.facility.equipmentSystem", "Dây chuyền máy móc & Thiết bị vận hành")}</span>
                  </h4>
                  <span className="text-xs text-ink-secondary">
                    {t("admin.facility.equipmentCount", "{{count}} thiết bị", {
                      count: fac.equipment?.length || 0,
                    })}
                  </span>
                </div>

                <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
                  {fac.equipment?.map((eq) => (
                    <div
                      key={eq.id}
                      className="p-3 rounded-xl bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/15 flex items-center justify-between"
                    >
                      <div>
                        <span className="font-mono text-xs font-bold text-accent-primary block">
                          {eq.code}
                        </span>
                        <span className="text-xs font-semibold text-ink-primary">
                          {eq.name}
                        </span>
                      </div>
                      <div className="flex items-center gap-2">
                        <span className="text-[10px] font-mono uppercase font-bold text-ink-tertiary px-1.5 py-0.5 rounded bg-surface-base border">
                          {eq.kind}
                        </span>
                        <button
                          type="button"
                          onClick={() =>
                            handleOpenEdit("EQUIPMENT", {
                              id: eq.id,
                              code: eq.code,
                              name: eq.name,
                              facId: fac.id,
                            })
                          }
                          className="p-1 text-ink-tertiary hover:text-ink-primary"
                          title="Sửa thiết bị"
                        >
                          <PencilSimple size={13} />
                        </button>
                        <button
                          type="button"
                          onClick={() => handleDelete("EQUIPMENT", eq.id, eq.name)}
                          className="p-1 text-ink-tertiary hover:text-accent-danger"
                          title="Xóa thiết bị"
                        >
                          <Trash size={13} />
                        </button>
                      </div>
                    </div>
                  ))}
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
                {isEdit
                  ? `Chỉnh sửa ${modalType === "LOCATION" ? "Silo / Kho chứa" : modalType === "EQUIPMENT" ? "Thiết bị" : "Cơ sở Nhà máy"}`
                  : modalType === "LOCATION"
                    ? t("admin.facility.modalLocationTitle", "Thêm Silo / Kho chứa Mới")
                    : modalType === "EQUIPMENT"
                      ? t("admin.facility.modalEquipmentTitle", "Thêm Thiết bị / Dây chuyền Mới")
                      : t("admin.facility.modalFacilityTitle", "Thêm Cơ sở Nhà máy Mới")}
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
                  {t("admin.facility.codeLabel", "Mã Code định danh *")}
                </label>
                <input
                  type="text"
                  required
                  value={code}
                  onChange={(e) => setCode(e.target.value)}
                  placeholder="VD: SILO-03, DRYER-LINE-B"
                  className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none font-mono uppercase"
                />
              </div>

              <div>
                <label className="block font-semibold text-ink-secondary mb-1">
                  {t("admin.facility.nameLabel", "Tên hiển thị *")}
                </label>
                <input
                  type="text"
                  required
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder="VD: Silo lưu trữ lúa khô S3"
                  className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none"
                />
              </div>

              <div>
                <label className="block font-semibold text-ink-secondary mb-1">
                  {modalType === "LOCATION"
                    ? t("admin.facility.capacityInputLabel", "Sức chứa thiết kế (kg) *")
                    : t("admin.facility.noteInputLabel", "Địa chỉ / Ghi chú kỹ thuật")}
                </label>
                <input
                  type="text"
                  value={extra}
                  onChange={(e) => setExtra(e.target.value)}
                  placeholder={
                    modalType === "LOCATION"
                      ? "500000"
                      : "Huyện Cái Bè, Tiền Giang"
                  }
                  className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none font-mono"
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
                  {submitting ? t("common.saving", "Đang lưu...") : isEdit ? "Lưu thay đổi" : t("admin.facility.submitBtn", "Tạo mới")}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
