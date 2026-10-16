import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { procurementEndpoints, type FarmerInput } from "@/api/procurement";
import { useAuthStore } from "@/app/store/authStore";
import { StatusBadge } from "@/components/StatusBadge/StatusBadge";
import { GrainLoader } from "@/components/GrainLoader/GrainLoader";
import {
  UserPlus,
  MagnifyingGlass,
  Phone,
  IdentificationCard,
  MapPin,
  ArrowsClockwise,
  PencilSimple,
  Trash,
  CheckCircle,
  WarningCircle,
  X,
} from "@phosphor-icons/react";

interface FarmerRecord {
  id: string;
  code: string;
  name: string;
  contact?: string;
  version?: number;
  totalPlots?: number;
  totalAreaHa?: number;
}

export const FarmerRegistrationPage: React.FC = () => {
  const { t } = useTranslation();
  const { user } = useAuthStore();
  const [farmers, setFarmers] = useState<FarmerRecord[]>([]);
  const [loading, setLoading] = useState(true);
  const [searchQuery, setSearchQuery] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);

  // Modal State
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [editingFarmer, setEditingFarmer] = useState<FarmerRecord | null>(null);
  const [formData, setFormData] = useState({
    code: "",
    name: "",
    contact: "",
  });
  const [submitting, setSubmitting] = useState(false);

  const loadFarmers = async () => {
    setLoading(true);
    setError(null);
    try {
      const params: Record<string, string | number> = { pageSize: 50 };
      if (searchQuery) params.search = searchQuery;
      const res = await procurementEndpoints.getFarmers(params);
      // The API may return an array directly or an object with items
      const items = Array.isArray(res) ? res : (res as any)?.items || [];
      setFarmers(items);
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError(t("field.farmer.errLoad", "Không thể tải danh sách nông hộ từ máy chủ."));
      }
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadFarmers();
  }, [searchQuery]);

  const handleOpenCreate = () => {
    setEditingFarmer(null);
    setFormData({
      code: `FARMER-${Date.now().toString().slice(-4)}`,
      name: "",
      contact: "",
    });
    setIsModalOpen(true);
  };

  const handleOpenEdit = (farmer: FarmerRecord) => {
    setEditingFarmer(farmer);
    setFormData({
      code: farmer.code || "",
      name: farmer.name || "",
      contact: farmer.contact || "",
    });
    setIsModalOpen(true);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!formData.name.trim()) return;

    setSubmitting(true);
    setError(null);
    setSuccessMsg(null);

    try {
      if (editingFarmer) {
        // Update
        await procurementEndpoints.updateFarmer(editingFarmer.id, {
          code: formData.code.trim(),
          name: formData.name.trim(),
          contact: formData.contact.trim() || null,
          expectedVersion: editingFarmer.version || 1,
        });
        setSuccessMsg(
          t("field.farmer.updatedSuccess", "Đã cập nhật thông tin nông hộ {{name}}", {
            name: formData.name,
          })
        );
      } else {
        // Create
        const entityId = crypto.randomUUID();
        const clientOperationId = crypto.randomUUID();
        await procurementEndpoints.createFarmer({
          deviceId: "grainledger-web",
          clientOperationId,
          capturedAt: new Date().toISOString(),
          payloadSchemaVersion: 1,
          payload: {
            entityId,
            code: formData.code.trim(),
            name: formData.name.trim(),
            contact: formData.contact.trim() || "",
          },
        });
        setSuccessMsg(
          t("field.farmer.registeredSuccess", "Đã đăng ký thành công nông hộ {{name}}", {
            name: formData.name,
          })
        );
      }

      setIsModalOpen(false);
      await loadFarmers();
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError(t("field.farmer.errAction", "Thao tác không thành công."));
      }
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async (farmer: FarmerRecord) => {
    if (
      !window.confirm(
        t("field.farmer.confirmDelete", "Bạn có chắc muốn xoá hồ sơ nông hộ \"{{name}}\"?", {
          name: farmer.name,
        })
      )
    )
      return;

    try {
      await procurementEndpoints.deleteFarmer(farmer.id, {
        version: farmer.version || 1,
      });
      setSuccessMsg(
        t("field.farmer.deletedSuccess", "Đã xoá hồ sơ nông hộ {{name}}", {
          name: farmer.name,
        })
      );
      await loadFarmers();
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError(t("field.farmer.errDelete", "Không thể xoá nông hộ."));
      }
    }
  };

  return (
    <div className="space-y-6">
      {/* Page Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 border-b border-ink-tertiary/15 pb-4">
        <div>
          <span className="text-xs font-mono font-bold text-accent-primary uppercase tracking-wider block">
            {t("field.farmer.badge", "QUẢN LÝ NGUỒN CUNG ỨNG NÔNG HỘ")}
          </span>
          <h2 className="text-2xl font-display font-bold text-ink-primary">
            {t("field.farmer.pageTitle", "Đăng ký Nông hộ & Hồ sơ Canh tác")}
          </h2>
          <p className="text-xs text-ink-secondary mt-0.5">
            {t(
              "field.farmer.pageSubtitle",
              "Quản lý định danh các nông hộ nhỏ liên kết chuỗi cung ứng lúa gạo tại Đồng bằng sông Cửu Long."
            )}
          </p>
        </div>

        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={loadFarmers}
            className="p-2.5 rounded-lg border border-ink-tertiary/20 bg-surface-base hover:bg-surface-raised text-ink-secondary hover:text-ink-primary transition-colors"
            title={t("common.refresh", "Tải lại danh sách")}
          >
            <ArrowsClockwise size={16} />
          </button>
          <button
            type="button"
            onClick={handleOpenCreate}
            className="inline-flex items-center gap-2 px-4 py-2.5 rounded-lg bg-accent-primary text-white font-semibold text-xs hover:bg-accent-primary-dim transition-all shadow-xs"
          >
            <UserPlus size={16} weight="bold" />
            <span>{t("field.farmer.btnAddFarmer", "Thêm Nông hộ mới")}</span>
          </button>
        </div>
      </div>

      {/* Alert Notices */}
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

      {/* Search & Statistics Filter Bar */}
      <div className="p-4 rounded-xl bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 flex flex-col sm:flex-row items-center justify-between gap-4">
        <div className="relative w-full sm:w-80">
          <input
            type="text"
            placeholder={t("field.farmer.searchPlaceholder", "Tìm theo tên nông hộ hoặc mã số...")}
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            className="w-full pl-9 pr-3 py-2 text-xs rounded-lg bg-surface-base dark:bg-surface-base border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none font-body"
          />
          <MagnifyingGlass
            size={16}
            className="absolute left-3 top-2.5 text-ink-tertiary"
          />
        </div>

        <div className="flex items-center gap-4 text-xs text-ink-secondary">
          <div>
            {t("field.farmer.totalFarmers", "Tổng số nông hộ:")}{" "}
            <strong className="font-mono text-ink-primary font-bold">
              {farmers.length}
            </strong>
          </div>
          <div className="hidden md:block">
            {t("field.farmer.orgInCharge", "Doanh nghiệp phụ trách:")}{" "}
            <span className="font-semibold text-accent-primary">
              {user?.organizationName || t("field.farmer.allFarmers", "Tất cả nông hộ")}
            </span>
          </div>
        </div>
      </div>

      {/* Farmers Data Table */}
      <div className="rounded-xl border border-ink-tertiary/20 bg-surface-base dark:bg-surface-raised overflow-hidden shadow-xs">
        {loading ? (
          <div className="py-16 flex justify-center">
            <GrainLoader label={t("field.farmer.loading", "Đang tải danh sách nông hộ...")} />
          </div>
        ) : farmers.length === 0 ? (
          <div className="py-16 text-center space-y-2">
            <IdentificationCard
              size={40}
              className="mx-auto text-ink-tertiary opacity-60"
            />
            <p className="text-sm font-semibold text-ink-primary">
              {t("field.farmer.emptyTitle", "Chưa có nông hộ nào")}
            </p>
            <p className="text-xs text-ink-secondary max-w-sm mx-auto">
              {t(
                "field.farmer.emptyDesc",
                "Nhấn \"Thêm Nông hộ mới\" để đăng ký hồ sơ nông dân liên kết chuỗi thu mua lúa gạo."
              )}
            </p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs">
              <thead className="bg-surface-raised dark:bg-surface-overlay border-b border-ink-tertiary/20 text-ink-secondary font-semibold uppercase tracking-wider text-[11px]">
                <tr>
                  <th className="py-3 px-4">{t("field.farmer.thCode", "Mã số")}</th>
                  <th className="py-3 px-4">{t("field.farmer.thName", "Họ và Tên nông dân")}</th>
                  <th className="py-3 px-4">{t("field.farmer.thContact", "Số điện thoại / Liên hệ")}</th>
                  <th className="py-3 px-4">{t("field.farmer.thPlots", "Thửa ruộng liên kết")}</th>
                  <th className="py-3 px-4">{t("field.farmer.thStatus", "Trạng thái")}</th>
                  <th className="py-3 px-4 text-right">{t("field.farmer.thActions", "Thao tác")}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-ink-tertiary/10">
                {farmers.map((farmer) => (
                  <tr
                    key={farmer.id}
                    className="hover:bg-surface-raised/60 dark:hover:bg-surface-overlay/40 transition-colors"
                  >
                    <td className="py-3 px-4 font-mono font-bold text-accent-primary">
                      {farmer.code || "N/A"}
                    </td>
                    <td className="py-3 px-4 font-semibold text-ink-primary">
                      {farmer.name}
                    </td>
                    <td className="py-3 px-4 font-mono text-ink-secondary">
                      {farmer.contact ? (
                        <span className="inline-flex items-center gap-1">
                          <Phone size={13} className="text-ink-tertiary" />
                          <span>{farmer.contact}</span>
                        </span>
                      ) : (
                        <span className="text-ink-tertiary italic">
                          {t("field.farmer.notRecorded", "Chưa ghi nhận")}
                        </span>
                      )}
                    </td>
                    <td className="py-3 px-4">
                      <span className="inline-flex items-center gap-1 font-mono text-xs">
                        <MapPin size={13} className="text-accent-primary" />
                        <span>
                          {t("field.farmer.plotsUnit", "{{count}} thửa", {
                            count: farmer.totalPlots ?? 1,
                          })}
                        </span>
                      </span>
                    </td>
                    <td className="py-3 px-4">
                      <StatusBadge status="BALANCED" label={t("field.farmer.statusVerified", "Đã xác thực")} />
                    </td>
                    <td className="py-3 px-4 text-right space-x-1">
                      <button
                        type="button"
                        onClick={() => handleOpenEdit(farmer)}
                        className="p-1.5 rounded hover:bg-surface-overlay text-ink-secondary hover:text-ink-primary transition-colors"
                        title={t("field.farmer.editFarmer", "Chỉnh sửa nông hộ")}
                      >
                        <PencilSimple size={15} />
                      </button>
                      <button
                        type="button"
                        onClick={() => handleDelete(farmer)}
                        className="p-1.5 rounded hover:bg-red-50 dark:hover:bg-red-950/30 text-accent-danger transition-colors"
                        title={t("field.farmer.deleteFarmer", "Xoá nông hộ")}
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

      {/* Create / Edit Farmer Modal */}
      {isModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-xs p-4">
          <div className="w-full max-w-md bg-surface-base dark:bg-surface-raised border border-ink-tertiary/20 rounded-2xl shadow-xl overflow-hidden animate-ledger-in">
            <div className="p-5 border-b border-ink-tertiary/15 flex items-center justify-between">
              <h3 className="font-display font-bold text-base text-ink-primary">
                {editingFarmer
                  ? t("field.farmer.modalEditTitle", "Cập nhật thông tin Nông hộ")
                  : t("field.farmer.modalAddTitle", "Đăng ký Nông hộ mới")}
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
                  {t("field.farmer.farmerCodeLabel", "Mã Nông hộ (Định danh hệ thống) *")}
                </label>
                <input
                  type="text"
                  required
                  value={formData.code}
                  onChange={(e) =>
                    setFormData({ ...formData, code: e.target.value })
                  }
                  placeholder="VD: FARMER-001"
                  className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none font-mono"
                />
              </div>

              <div>
                <label className="block font-semibold text-ink-secondary mb-1">
                  {t("field.farmer.farmerNameLabel", "Họ và tên Nông dân *")}
                </label>
                <input
                  type="text"
                  required
                  value={formData.name}
                  onChange={(e) =>
                    setFormData({ ...formData, name: e.target.value })
                  }
                  placeholder="VD: Nguyễn Văn An"
                  className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none"
                />
              </div>

              <div>
                <label className="block font-semibold text-ink-secondary mb-1">
                  {t("field.farmer.farmerContactLabel", "Số điện thoại / Thông tin liên hệ")}
                </label>
                <input
                  type="text"
                  value={formData.contact}
                  onChange={(e) =>
                    setFormData({ ...formData, contact: e.target.value })
                  }
                  placeholder="VD: 0912 345 678 · Huyện Cái Bè, Tiền Giang"
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
                  {submitting
                    ? t("common.saving", "Đang lưu...")
                    : editingFarmer
                      ? t("common.saveChanges", "Lưu thay đổi")
                      : t("common.register", "Đăng ký")}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
