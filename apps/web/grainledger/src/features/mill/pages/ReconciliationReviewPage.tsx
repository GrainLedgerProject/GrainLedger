import { ledgerEndpoints } from "@/api/ledger";
import { useAuthStore } from "@/app/store/authStore";
import { StatusBadge } from "@/components/StatusBadge/StatusBadge";
import {
  ArrowRight,
  ArrowsClockwise,
  CheckCircle,
  ClipboardText,
  ClockCounterClockwise,
  FilePdf,
  FileText,
  MagnifyingGlass,
  Scales,
  ShieldCheck,
  ShieldWarning,
  WarningCircle,
  XCircle,
} from "@phosphor-icons/react";
import React, { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";

export interface ReconciliationItem {
  id: string;
  operationId: string;
  facilityId: string;
  facilityName: string;
  operationType: "DRYING" | "MILLING" | "BLENDING" | "RECEIVING";
  category: string;
  description: string;
  status: "PENDING_REVIEW" | "RECONCILED" | "REJECTED";
  createdAt: string;
  submittedBy: string;

  // Mass Variance & Tolerance
  sourceQuantityKg: number;
  actualQuantityKg: number;
  varianceKg: number;
  variancePercent: number;
  tolerancePercent: number;
  withinTolerance: boolean;

  // Weighbridge Evidence
  scaleTicketNumber: string;
  scaleDeviceName: string;
  grossWeightKg: number;
  tareWeightKg: number;
  netWeightKg: number;
  weighTimestamp: string;

  // Lab & KCS Evidence
  kcsSlipNumber: string;
  inspectorName: string;
  moistureDeclared: number;
  moistureMeasured: number;
  foreignMatterPercent: number;

  // Mill Manager Review Data
  managerNotes?: string;
  reviewedAt?: string;
  reviewedBy?: string;
  linkedCorrectionId?: string;
}

const SAMPLE_RECONCILIATIONS: ReconciliationItem[] = [
  {
    id: "REC-2026-001",
    operationId: "OP-2026-DRY-01",
    facilityId: "FAC-TG-01",
    facilityName: "Nhà máy Sấy & Chế biến Tiền Giang",
    operationType: "DRYING",
    category: "Đo sai độ ẩm / Hao hụt mẻ sấy",
    description:
      "Nhầm lẫn phiếu cân độ ẩm đầu vào từ 14.8% thành 14.2%, dẫn đến sai lệch lượng hao hụt tính toán 120 kg so với thực tế.",
    status: "PENDING_REVIEW",
    createdAt: new Date(Date.now() - 1000 * 60 * 180).toISOString(),
    submittedBy: "Nguyễn Tấn Vận Hành (Vận hành viên ca 1)",
    sourceQuantityKg: 25400,
    actualQuantityKg: 25520,
    varianceKg: 120,
    variancePercent: 0.47,
    tolerancePercent: 0.5,
    withinTolerance: true,
    scaleTicketNumber: "CAN-2026-0428",
    scaleDeviceName: "Cân bàn điện tử Mettler Toledo 80T (CAN-BAN-01)",
    grossWeightKg: 37920,
    tareWeightKg: 12400,
    netWeightKg: 25520,
    weighTimestamp: "16/10/2026 09:15",
    kcsSlipNumber: "KCS-2026-092",
    inspectorName: "Trần Thị Lan (KCS Trưởng)",
    moistureDeclared: 14.8,
    moistureMeasured: 14.2,
    foreignMatterPercent: 1.1,
    linkedCorrectionId: "CORR-2026-001",
  },
  {
    id: "REC-2026-002",
    operationId: "OP-2026-MILL-03",
    facilityId: "FAC-TG-01",
    facilityName: "Nhà máy Sấy & Chế biến Tiền Giang",
    operationType: "MILLING",
    category: "Tỷ lệ thu hồi cám/tấm lệch",
    description:
      "Tỷ lệ thu hồi cám thực tế lệch 85 kg so với định mức do máy bóc vỏ gặp sự cố trục rulo đầu ca. Đã thay rulo cao su số 3.",
    status: "RECONCILED",
    createdAt: new Date(Date.now() - 1000 * 60 * 540).toISOString(),
    submittedBy: "Nguyễn Tấn Vận Hành (Vận hành viên ca 1)",
    sourceQuantityKg: 18200,
    actualQuantityKg: 18285,
    varianceKg: 85,
    variancePercent: 0.46,
    tolerancePercent: 0.5,
    withinTolerance: true,
    scaleTicketNumber: "CAN-2026-0435",
    scaleDeviceName: "Cân phễu định lượng tự động Silo Cám (CAN-SILO-02)",
    grossWeightKg: 5200,
    tareWeightKg: 1420,
    netWeightKg: 3780,
    weighTimestamp: "16/10/2026 14:00",
    kcsSlipNumber: "KCS-2026-098",
    inspectorName: "Lê Quốc Bảo (Kỹ thuật cơ điện)",
    moistureDeclared: 14.0,
    moistureMeasured: 13.9,
    foreignMatterPercent: 0.8,
    managerNotes:
      "Đã kiểm tra biên bản sự cố rulo bóc vỏ. Số liệu đo lường cân băng tải silo cám chuẩn xác, độ lệch 85 kg (0.46%) nằm trong ngưỡng dung sai 0.5%. Đã duyệt đối soát chuyển chốt bút toán bù.",
    reviewedAt: new Date(Date.now() - 1000 * 60 * 120).toISOString(),
    reviewedBy: "Trần Văn Quản Lý (Mill Manager)",
    linkedCorrectionId: "CORR-2026-002",
  },
  {
    id: "REC-2026-003",
    operationId: "OP-2026-RCV-07",
    facilityId: "FAC-TG-01",
    facilityName: "Nhà máy Sấy & Chế biến Tiền Giang",
    operationType: "RECEIVING",
    category: "Sai lệch cân xe tải vượt ngưỡng",
    description:
      "Phiếu cân xe tải 63C-182.91 lệch 380 kg (-1.52%) so với phiếu thu mua tại ruộng từ nông hộ. Vượt ngưỡng dung sai cho phép 0.5%.",
    status: "REJECTED",
    createdAt: new Date(Date.now() - 1000 * 60 * 960).toISOString(),
    submittedBy: "Phạm Văn Cân (Nhân viên trạm cân)",
    sourceQuantityKg: 25000,
    actualQuantityKg: 24620,
    varianceKg: -380,
    variancePercent: -1.52,
    tolerancePercent: 0.5,
    withinTolerance: false,
    scaleTicketNumber: "CAN-2026-0412",
    scaleDeviceName: "Cân cầu xe tải 80T (CAN-CAU-01)",
    grossWeightKg: 38240,
    tareWeightKg: 13620,
    netWeightKg: 24620,
    weighTimestamp: "15/10/2026 16:45",
    kcsSlipNumber: "KCS-2026-081",
    inspectorName: "Trần Thị Lan (KCS Trưởng)",
    moistureDeclared: 25.0,
    moistureMeasured: 25.2,
    foreignMatterPercent: 2.4,
    managerNotes:
      "Sai lệch 380 kg vượt ngưỡng dung sai 0.5%. Yêu cầu tài xế cân đối chứng lại tại trạm cân độc lập thứ hai và kiểm tra xem có thất thoát trên đường vận chuyển hay không.",
    reviewedAt: new Date(Date.now() - 1000 * 60 * 700).toISOString(),
    reviewedBy: "Trần Văn Quản Lý (Mill Manager)",
  },
  {
    id: "REC-2026-004",
    operationId: "OP-2026-BLEND-01",
    facilityId: "FAC-TG-01",
    facilityName: "Nhà máy Sấy & Chế biến Tiền Giang",
    operationType: "BLENDING",
    category: "Sai lệch bì bao đóng gói",
    description:
      "Trừ sai khối lượng bao bì xe trung chuyển 40 kg khi đóng bao thành phẩm gạo thơm 5% tấm.",
    status: "RECONCILED",
    createdAt: new Date(Date.now() - 1000 * 60 * 1440 * 2).toISOString(),
    submittedBy: "Lê Văn Quản Trị (Admin)",
    sourceQuantityKg: 10000,
    actualQuantityKg: 10040,
    varianceKg: 40,
    variancePercent: 0.4,
    tolerancePercent: 0.5,
    withinTolerance: true,
    scaleTicketNumber: "CAN-2026-0390",
    scaleDeviceName: "Cân bàn đóng bao tự động (CAN-BAO-01)",
    grossWeightKg: 10540,
    tareWeightKg: 500,
    netWeightKg: 10040,
    weighTimestamp: "14/10/2026 10:20",
    kcsSlipNumber: "KCS-2026-065",
    inspectorName: "Ngô Văn Kiểm (KCS)",
    moistureDeclared: 14.0,
    moistureMeasured: 14.0,
    foreignMatterPercent: 0.5,
    managerNotes:
      "Đã đối chiếu phiếu cân xe và phiếu cân bàn đầu ra. Chuẩn y số liệu lệch 40 kg.",
    reviewedAt: new Date(
      Date.now() - 1000 * 60 * 1440 * 2 + 1000 * 60 * 60,
    ).toISOString(),
    reviewedBy: "Trần Văn Quản Lý (Mill Manager)",
    linkedCorrectionId: "CORR-2026-003",
  },
];

export const ReconciliationReviewPage: React.FC = () => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { id: routeId } = useParams<{ id?: string }>();
  const { user } = useAuthStore();

  const [items, setItems] = useState<ReconciliationItem[]>(
    SAMPLE_RECONCILIATIONS,
  );
  const [loading, setLoading] = useState(false);
  const [selectedItem, setSelectedItem] = useState<ReconciliationItem | null>(
    null,
  );
  const [activeTab, setActiveTab] = useState<
    "EVIDENCE" | "TOLERANCE" | "SIGN_OFF"
  >("EVIDENCE");

  // Filter & Search
  const [statusFilter, setStatusFilter] = useState<
    "ALL" | ReconciliationItem["status"]
  >("ALL");
  const [searchQuery, setSearchQuery] = useState("");

  // Review Form state
  const [reviewNotes, setReviewNotes] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [actionMessage, setActionMessage] = useState<{
    type: "success" | "error";
    text: string;
  } | null>(null);

  // Sync route param (e.g. /mill/reconciliations/REC-001 or /mill/reconciliations/REC-2026-001)
  useEffect(() => {
    if (routeId && items.length > 0) {
      const normalizedRouteId = routeId.toLowerCase().replace(/[^a-z0-9]/g, "");
      const found = items.find((it) => {
        const itNormId = it.id.toLowerCase().replace(/[^a-z0-9]/g, "");
        const itNormOp = it.operationId.toLowerCase().replace(/[^a-z0-9]/g, "");
        const itNormCorr =
          it.linkedCorrectionId?.toLowerCase().replace(/[^a-z0-9]/g, "") || "";
        return (
          itNormId.includes(normalizedRouteId) ||
          normalizedRouteId.includes(itNormId) ||
          itNormOp.includes(normalizedRouteId) ||
          itNormCorr.includes(normalizedRouteId)
        );
      });

      if (found) {
        setSelectedItem(found);
        setReviewNotes(found.managerNotes || "");
      } else {
        setSelectedItem(items[0]);
        setReviewNotes(items[0].managerNotes || "");
      }
    } else if (!selectedItem && items.length > 0) {
      setSelectedItem(items[0]);
      setReviewNotes(items[0].managerNotes || "");
    }
  }, [routeId, items]);

  const handleSelectItem = (item: ReconciliationItem) => {
    setSelectedItem(item);
    setReviewNotes(item.managerNotes || "");
    setActionMessage(null);
  };

  const handleApproveReconciliation = async () => {
    if (!selectedItem) return;
    if (!reviewNotes.trim()) {
      alert("Vui lòng ghi rõ kết luận đối soát vào ô Biên bản đối soát.");
      return;
    }

    try {
      setIsSubmitting(true);
      setActionMessage(null);

      // Call API if linked to correction
      if (selectedItem.linkedCorrectionId) {
        try {
          await ledgerEndpoints.reconcileCorrection(
            selectedItem.linkedCorrectionId,
            {
              notes: reviewNotes.trim(),
            },
          );
        } catch {
          // Dev fallback
        }
      }

      const updatedNotes = reviewNotes.trim();
      const updatedTimestamp = new Date().toISOString();
      const reviewerName =
        user?.displayName || "Trần Văn Quản Lý (Mill Manager)";

      setItems((prev) =>
        prev.map((it) =>
          it.id === selectedItem.id
            ? {
              ...it,
              status: "RECONCILED",
              managerNotes: updatedNotes,
              reviewedAt: updatedTimestamp,
              reviewedBy: reviewerName,
            }
            : it,
        ),
      );

      setSelectedItem((prev) =>
        prev
          ? {
            ...prev,
            status: "RECONCILED",
            managerNotes: updatedNotes,
            reviewedAt: updatedTimestamp,
            reviewedBy: reviewerName,
          }
          : null,
      );

      setActionMessage({
        type: "success",
        text: `Đã xác nhận đối soát thành công hồ sơ ${selectedItem.id}. Hồ sơ đã sẵn sàng để chuyển sang bước Ghi bút toán bù sổ cái.`,
      });
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleRejectReconciliation = async () => {
    if (!selectedItem) return;
    if (!reviewNotes.trim()) {
      alert("Vui lòng nêu rõ lý do từ chối hoặc yêu cầu cân lại đối chứng.");
      return;
    }

    try {
      setIsSubmitting(true);
      setActionMessage(null);

      const updatedNotes = reviewNotes.trim();
      const updatedTimestamp = new Date().toISOString();
      const reviewerName =
        user?.displayName || "Trần Văn Quản Lý (Mill Manager)";

      setItems((prev) =>
        prev.map((it) =>
          it.id === selectedItem.id
            ? {
              ...it,
              status: "REJECTED",
              managerNotes: updatedNotes,
              reviewedAt: updatedTimestamp,
              reviewedBy: reviewerName,
            }
            : it,
        ),
      );

      setSelectedItem((prev) =>
        prev
          ? {
            ...prev,
            status: "REJECTED",
            managerNotes: updatedNotes,
            reviewedAt: updatedTimestamp,
            reviewedBy: reviewerName,
          }
          : null,
      );

      setActionMessage({
        type: "error",
        text: `Đã từ chối xác nhận đối soát cho hồ sơ ${selectedItem.id}. Hồ sơ đã gửi phản hồi yêu cầu cân lại.`,
      });
    } finally {
      setIsSubmitting(false);
    }
  };

  // Filtered List
  const filteredItems = useMemo(() => {
    return items.filter((item) => {
      const matchStatus =
        statusFilter === "ALL" || item.status === statusFilter;
      const q = searchQuery.toLowerCase().trim();
      const matchQuery =
        !q ||
        item.id.toLowerCase().includes(q) ||
        item.operationId.toLowerCase().includes(q) ||
        item.category.toLowerCase().includes(q) ||
        item.description.toLowerCase().includes(q) ||
        item.submittedBy.toLowerCase().includes(q);
      return matchStatus && matchQuery;
    });
  }, [items, statusFilter, searchQuery]);

  // Statistics
  const stats = useMemo(() => {
    const pending = items.filter((it) => it.status === "PENDING_REVIEW").length;
    const reconciled = items.filter((it) => it.status === "RECONCILED").length;
    const rejected = items.filter((it) => it.status === "REJECTED").length;
    const withinToleranceCount = items.filter(
      (it) => it.withinTolerance,
    ).length;
    return {
      pending,
      reconciled,
      rejected,
      withinToleranceCount,
      total: items.length,
    };
  }, [items]);

  return (
    <div className="space-y-6 pb-12">
      {/* Page Header - Synchronized with CustomerOrganizationsPage */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 border-b border-ink-tertiary/15 pb-4">
        <div>
          <span className="text-xs font-mono font-bold text-accent-primary uppercase tracking-wider block">
            {t("mill.reconciliation.tag", "ĐỐI SOÁT & XÁC NHẬN BẰNG CHỨNG")}
          </span>
          <h2 className="text-2xl font-display font-bold text-ink-primary">
            {t(
              "mill.reconciliation.pageTitle",
              "Đối Soát & Xác Nhận Bằng Chứng"
            )}
          </h2>
          <p className="text-xs text-ink-secondary mt-0.5">
            {t(
              "mill.reconciliation.pageSubtitle",
              "Thẩm định bằng chứng đo lường thực tế (phiếu cân bàn, biên bản KCS kiểm tra độ ẩm/tạp chất), so sánh mức độ sai lệch với ngưỡng dung sai chính sách và ký biên bản đối soát trước khi chuyển giao sang quy trình ghi bút toán bù sổ cái."
            )}
          </p>
        </div>

        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() => {
              setLoading(true);
              setTimeout(() => setLoading(false), 500);
            }}
            disabled={loading}
            className="p-2.5 rounded-lg border border-ink-tertiary/20 bg-surface-base hover:bg-surface-raised text-ink-secondary hover:text-ink-primary transition-colors"
            title={t("common.reload", "Tải lại danh sách")}
          >
            <ArrowsClockwise
              size={16}
              className={loading ? "animate-spin" : ""}
            />
          </button>

          <Link
            to="/mill/corrections/pending"
            className="inline-flex items-center gap-2 px-4 py-2.5 rounded-lg bg-accent-primary text-white font-semibold text-xs hover:bg-accent-primary-dim transition-all shadow-xs"
          >
            <ClockCounterClockwise size={16} weight="bold" />
            <span>{t("mill.reconciliation.switchCommitLink", "Chuyển sang Ghi bút toán bù (SC-18)")}</span>
            <ArrowRight size={14} weight="bold" />
          </Link>
        </div>
      </div>

      {/* KPI Stats Bar - Synchronized with Surface Card Design */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
        <div className="p-4 sm:p-5 rounded-2xl bg-surface-base dark:bg-surface-raised border border-ink-tertiary/20 shadow-xs hover:shadow-md transition-shadow">
          <div className="flex items-center justify-between text-xs text-ink-secondary">
            <span>{t("mill.reconciliation.kpiPending", "Chờ thẩm định đối soát")}</span>
            <span className="w-2.5 h-2.5 rounded-full bg-amber-500 animate-pulse" />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className="text-2xl font-bold font-mono text-amber-600 dark:text-amber-400">
              {stats.pending}
            </span>
            <span className="text-xs text-ink-tertiary font-mono">
              {t("mill.reconciliation.dossiersUnit", "hồ sơ")}
            </span>
          </div>
        </div>

        <div className="p-4 sm:p-5 rounded-2xl bg-surface-base dark:bg-surface-raised border border-ink-tertiary/20 shadow-xs hover:shadow-md transition-shadow">
          <div className="flex items-center justify-between text-xs text-ink-secondary">
            <span>{t("mill.reconciliation.kpiWithinTol", "Trong dung sai cho phép")}</span>
            <ShieldCheck
              size={18}
              className="text-emerald-600 dark:text-emerald-400"
              weight="fill"
            />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className="text-2xl font-bold font-mono text-emerald-600 dark:text-emerald-400">
              {stats.withinToleranceCount} / {stats.total}
            </span>
            <span className="text-xs text-ink-tertiary font-mono">(≤ 0.5%)</span>
          </div>
        </div>

        <div className="p-4 sm:p-5 rounded-2xl bg-surface-base dark:bg-surface-raised border border-ink-tertiary/20 shadow-xs hover:shadow-md transition-shadow">
          <div className="flex items-center justify-between text-xs text-ink-secondary">
            <span>{t("mill.reconciliation.kpiReconciled", "Đã đối soát & chuẩn y")}</span>
            <CheckCircle size={18} className="text-accent-primary" weight="fill" />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className="text-2xl font-bold font-mono text-accent-primary">
              {stats.reconciled}
            </span>
            <span className="text-xs text-ink-tertiary font-mono">
              {t("mill.reconciliation.dossiersUnit", "hồ sơ")}
            </span>
          </div>
        </div>

        <div className="p-4 sm:p-5 rounded-2xl bg-surface-base dark:bg-surface-raised border border-ink-tertiary/20 shadow-xs hover:shadow-md transition-shadow">
          <div className="flex items-center justify-between text-xs text-ink-secondary">
            <span>{t("mill.reconciliation.kpiRejected", "Yêu cầu cân lại / Từ chối")}</span>
            <XCircle size={18} className="text-accent-danger" weight="fill" />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className="text-2xl font-bold font-mono text-accent-danger">
              {stats.rejected}
            </span>
            <span className="text-xs text-ink-tertiary font-mono">
              {t("mill.reconciliation.dossiersUnit", "hồ sơ")}
            </span>
          </div>
        </div>
      </div>

      {/* Main 2-Column Workbench Layout */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-6 items-start">
        {/* Left Column: List of Reconciliation Requests (5 cols) */}
        <div className="lg:col-span-5 space-y-4">
          <div className="p-4 rounded-2xl bg-surface-base dark:bg-surface-raised border border-ink-tertiary/20 shadow-xs space-y-3">
            <div className="flex items-center justify-between">
              <h2 className="text-sm font-bold text-ink-primary font-display flex items-center gap-2">
                <FileText size={18} className="text-accent-primary" />
                <span>{t("mill.reconciliation.listTitle", "Danh mục hồ sơ đối soát")}</span>
                <span className="font-mono text-xs px-2 py-0.5 rounded-full bg-accent-primary/10 text-accent-primary font-bold">
                  {filteredItems.length}
                </span>
              </h2>
            </div>

            {/* Filter Tabs */}
            <div className="flex items-center gap-1 p-1 bg-surface-raised dark:bg-surface-overlay rounded-xl border border-ink-tertiary/15 text-xs">
              <button
                type="button"
                onClick={() => setStatusFilter("ALL")}
                className={`flex-1 py-1.5 rounded-lg text-xs font-semibold transition-all ${statusFilter === "ALL"
                    ? "bg-surface-base dark:bg-surface-raised text-ink-primary shadow-xs font-bold"
                    : "text-ink-secondary hover:text-ink-primary"
                  }`}
              >
                {t("common.all", "Tất cả")} ({items.length})
              </button>
              <button
                type="button"
                onClick={() => setStatusFilter("PENDING_REVIEW")}
                className={`flex-1 py-1.5 rounded-lg text-xs font-semibold transition-all ${statusFilter === "PENDING_REVIEW"
                    ? "bg-surface-base dark:bg-surface-raised text-amber-600 dark:text-amber-400 shadow-xs font-bold"
                    : "text-ink-secondary hover:text-ink-primary"
                  }`}
              >
                {t("mill.reconciliation.tabPending", "Chờ duyệt")} ({stats.pending})
              </button>
              <button
                type="button"
                onClick={() => setStatusFilter("RECONCILED")}
                className={`flex-1 py-1.5 rounded-lg text-xs font-semibold transition-all ${statusFilter === "RECONCILED"
                    ? "bg-surface-base dark:bg-surface-raised text-accent-primary shadow-xs font-bold"
                    : "text-ink-secondary hover:text-ink-primary"
                  }`}
              >
                {t("mill.reconciliation.tabReconciled", "Đã duyệt")} ({stats.reconciled})
              </button>
            </div>

            {/* Search Input */}
            <div className="relative">
              <MagnifyingGlass
                size={16}
                className="absolute left-3 top-1/2 -translate-y-1/2 text-ink-tertiary"
              />
              <input
                type="text"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                placeholder={t(
                  "mill.reconciliation.searchPlaceholder",
                  "Tìm mã hồ sơ, mẻ vận hành, người gửi..."
                )}
                className="w-full pl-9 pr-3 py-2 text-xs rounded-lg border border-ink-tertiary/20 bg-surface-raised dark:bg-surface-overlay text-ink-primary placeholder:text-ink-tertiary focus:border-accent-primary focus:outline-none transition-colors"
              />
            </div>
          </div>

          {/* List items */}
          <div className="space-y-3 max-h-[640px] overflow-y-auto pr-1">
            {filteredItems.map((item) => {
              const isSelected = selectedItem?.id === item.id;
              return (
                <div
                  key={item.id}
                  onClick={() => handleSelectItem(item)}
                  className={`p-4 rounded-2xl border cursor-pointer transition-all text-left space-y-2.5 relative shadow-xs ${isSelected
                      ? "bg-accent-primary/5 dark:bg-accent-primary/10 border-accent-primary ring-1 ring-accent-primary"
                      : "bg-surface-base dark:bg-surface-raised border-ink-tertiary/20 hover:border-ink-tertiary/40 hover:bg-surface-raised/60"
                    }`}
                >
                  <div className="flex items-center justify-between gap-2">
                    <div className="flex items-center gap-2">
                      <span className="font-mono text-xs font-bold text-accent-primary bg-accent-primary/10 px-2 py-0.5 rounded-md">
                        {item.id}
                      </span>
                      <span className="text-[11px] font-mono px-2 py-0.5 rounded-md bg-surface-raised dark:bg-surface-overlay text-ink-secondary border border-ink-tertiary/15">
                        {item.operationId}
                      </span>
                    </div>

                    {item.status === "PENDING_REVIEW" && (
                      <span className="inline-flex items-center gap-1.5 text-[11px] font-bold px-2.5 py-0.5 rounded-full bg-amber-50 dark:bg-amber-950/40 text-amber-700 dark:text-amber-300 border border-amber-500/30">
                        <span className="w-1.5 h-1.5 rounded-full bg-amber-500 animate-pulse" />
                        {t("mill.reconciliation.statusPending", "Chờ đối soát")}
                      </span>
                    )}
                    {item.status === "RECONCILED" && (
                      <StatusBadge
                        status="BALANCED"
                        label={t("mill.reconciliation.statusReconciled", "Đã đối soát")}
                      />
                    )}
                    {item.status === "REJECTED" && (
                      <StatusBadge
                        status="REJECTED"
                        label={t("mill.reconciliation.statusRejected", "Cần cân lại")}
                      />
                    )}
                  </div>

                  <p className="text-xs font-display font-bold text-ink-primary line-clamp-1">
                    {item.category}
                  </p>

                  <p className="text-[11px] text-ink-secondary line-clamp-2 leading-relaxed">
                    {item.description}
                  </p>

                  <div className="pt-2 border-t border-ink-tertiary/15 flex items-center justify-between text-[11px]">
                    <div className="flex items-center gap-1 font-mono">
                      <span className="text-ink-tertiary">{t("mill.reconciliation.varianceLabel", "Lệch:")}</span>
                      <span
                        className={`font-bold ${item.varianceKg > 0
                            ? "text-amber-600 dark:text-amber-400"
                            : "text-accent-danger"
                          }`}
                      >
                        {item.varianceKg > 0
                          ? `+${item.varianceKg}`
                          : item.varianceKg}{" "}
                        kg
                      </span>
                      <span className="text-ink-tertiary font-sans">
                        ({Math.abs(item.variancePercent)}%)
                      </span>
                    </div>

                    <span
                      className={`font-semibold px-2 py-0.5 rounded-md text-[10px] ${item.withinTolerance
                          ? "bg-emerald-50 dark:bg-emerald-950/40 text-emerald-700 dark:text-emerald-300 border border-emerald-500/20"
                          : "bg-functional-rejected-bg text-accent-danger border border-functional-rejected-border"
                        }`}
                    >
                      {item.withinTolerance
                        ? t("mill.reconciliation.withinTolTag", "Trong dung sai")
                        : t("mill.reconciliation.outOfTolTag", "Vượt dung sai")}
                    </span>
                  </div>
                </div>
              );
            })}

            {filteredItems.length === 0 && (
              <div className="p-8 text-center bg-surface-base dark:bg-surface-raised rounded-2xl border border-ink-tertiary/20 text-xs text-ink-secondary">
                {t(
                  "mill.reconciliation.noItemsFound",
                  "Không tìm thấy hồ sơ đối soát nào phù hợp với bộ lọc."
                )}
              </div>
            )}
          </div>
        </div>

        {/* Right Column: Detailed Evidence Inspector & Manager Sign-off (7 cols) */}
        <div className="lg:col-span-7 space-y-4">
          {selectedItem ? (
            <div className="bg-surface-base dark:bg-surface-raised border border-ink-tertiary/20 rounded-2xl shadow-xs overflow-hidden">
              {/* Header Panel */}
              <div className="p-5 border-b border-ink-tertiary/15 bg-surface-raised/40 dark:bg-surface-overlay/30 space-y-3">
                <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
                  <div>
                    <div className="flex items-center gap-2">
                      <h2 className="text-lg font-bold font-display text-ink-primary">
                        {t("mill.reconciliation.dossierDetailTitle", "Hồ Sơ Đối Soát {{id}}", {
                          id: selectedItem.id,
                        })}
                      </h2>
                      <span className="text-xs font-mono px-2 py-0.5 rounded-lg bg-surface-base dark:bg-surface-raised border border-ink-tertiary/20 text-ink-secondary font-bold">
                        {t("mill.reconciliation.batchPrefix", "Mẻ: {{id}}", {
                          id: selectedItem.operationId,
                        })}
                      </span>
                    </div>
                    <p className="text-xs text-ink-secondary mt-0.5">
                      {selectedItem.facilityName} • {t("mill.reconciliation.submittedBy", "Người nộp:")}{" "}
                      {selectedItem.submittedBy}
                    </p>
                  </div>

                  <div className="flex items-center gap-2">
                    {selectedItem.status === "RECONCILED" && (
                      <Link
                        to="/mill/corrections/pending"
                        className="inline-flex items-center gap-1.5 px-3.5 py-2 text-xs font-bold rounded-lg bg-accent-primary hover:bg-accent-primary-dim text-white transition-all shadow-xs"
                      >
                        <span>{t("mill.reconciliation.btnPostCompensation", "Ghi bút toán bù (SC-18)")}</span>
                        <ArrowRight size={14} weight="bold" />
                      </Link>
                    )}
                  </div>
                </div>

                {/* Status alert message */}
                {actionMessage && (
                  <div
                    className={`p-3.5 rounded-xl text-xs font-semibold flex items-center gap-2.5 ${actionMessage.type === "success"
                        ? "bg-emerald-50 dark:bg-emerald-950/40 text-emerald-700 dark:text-emerald-300 border border-emerald-500/30"
                        : "bg-functional-rejected-bg text-accent-danger border border-functional-rejected-border"
                      }`}
                  >
                    {actionMessage.type === "success" ? (
                      <CheckCircle size={18} weight="fill" className="shrink-0" />
                    ) : (
                      <WarningCircle size={18} weight="fill" className="shrink-0" />
                    )}
                    <span>{actionMessage.text}</span>
                  </div>
                )}

                {/* Inspector Tabs */}
                <div className="flex items-center gap-3 border-b border-ink-tertiary/15 pt-2 text-xs">
                  <button
                    type="button"
                    onClick={() => setActiveTab("EVIDENCE")}
                    className={`pb-2.5 px-1 font-semibold border-b-2 transition-all flex items-center gap-1.5 ${activeTab === "EVIDENCE"
                        ? "border-accent-primary text-accent-primary font-bold"
                        : "border-transparent text-ink-secondary hover:text-ink-primary"
                      }`}
                  >
                    <Scales size={16} />
                    <span>{t("mill.reconciliation.tabEvidence", "1. Bằng chứng phiếu cân & KCS")}</span>
                  </button>
                  <button
                    type="button"
                    onClick={() => setActiveTab("TOLERANCE")}
                    className={`pb-2.5 px-1 font-semibold border-b-2 transition-all flex items-center gap-1.5 ${activeTab === "TOLERANCE"
                        ? "border-accent-primary text-accent-primary font-bold"
                        : "border-transparent text-ink-secondary hover:text-ink-primary"
                      }`}
                  >
                    <ShieldCheck size={16} />
                    <span>{t("mill.reconciliation.tabTolerance", "2. Đánh giá dung sai & Cân bằng")}</span>
                  </button>
                  <button
                    type="button"
                    onClick={() => setActiveTab("SIGN_OFF")}
                    className={`pb-2.5 px-1 font-semibold border-b-2 transition-all flex items-center gap-1.5 ${activeTab === "SIGN_OFF"
                        ? "border-accent-primary text-accent-primary font-bold"
                        : "border-transparent text-ink-secondary hover:text-ink-primary"
                      }`}
                  >
                    <ClipboardText size={16} />
                    <span>{t("mill.reconciliation.tabSignOff", "3. Biên bản & Ký duyệt Quản lý")}</span>
                  </button>
                </div>
              </div>

              {/* Tab 1: Physical Evidence Inspector */}
              {activeTab === "EVIDENCE" && (
                <div className="p-5 space-y-4">
                  {/* Category & Reason Description */}
                  <div className="p-4 rounded-2xl bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/15 space-y-1.5">
                    <span className="text-[11px] font-mono font-bold text-accent-primary uppercase tracking-wider block">
                      {t("mill.reconciliation.reasonTitle", "Lý do & Nội dung khai báo sai lệch")}
                    </span>
                    <p className="text-xs font-display font-bold text-ink-primary">
                      {selectedItem.category}
                    </p>
                    <p className="text-xs text-ink-secondary leading-relaxed">
                      {selectedItem.description}
                    </p>
                  </div>

                  {/* Weighbridge Evidence Slip */}
                  <div className="p-4 sm:p-5 rounded-2xl border border-ink-tertiary/20 bg-surface-base dark:bg-surface-raised space-y-3 shadow-xs">
                    <div className="flex items-center justify-between border-b border-ink-tertiary/15 pb-2.5">
                      <div className="flex items-center gap-3">
                        <div className="w-9 h-9 rounded-xl bg-accent-primary/10 text-accent-primary flex items-center justify-center shrink-0">
                          <Scales size={18} weight="bold" />
                        </div>
                        <div>
                          <h3 className="text-xs font-display font-bold text-ink-primary">
                            {t("mill.reconciliation.weighSlipTitle", "Bằng chứng Phiếu Cân Điện Tử")}
                          </h3>
                          <span className="text-[11px] text-ink-tertiary font-mono">
                            {t("mill.reconciliation.ticketNumber", "Số phiếu:")} {selectedItem.scaleTicketNumber}
                          </span>
                        </div>
                      </div>
                      <span className="text-[11px] font-mono text-ink-tertiary">
                        {selectedItem.weighTimestamp}
                      </span>
                    </div>

                    <div className="grid grid-cols-3 gap-3 text-center py-2.5 bg-surface-raised dark:bg-surface-overlay rounded-xl border border-ink-tertiary/15">
                      <div className="p-2 border-r border-ink-tertiary/15">
                        <span className="text-[10px] text-ink-tertiary block font-medium">
                          {t("mill.reconciliation.grossWeight", "Tổng bì (Gross)")}
                        </span>
                        <span className="text-xs font-bold font-mono text-ink-primary">
                          {selectedItem.grossWeightKg.toLocaleString()} kg
                        </span>
                      </div>
                      <div className="p-2 border-r border-ink-tertiary/15">
                        <span className="text-[10px] text-ink-tertiary block font-medium">
                          {t("mill.reconciliation.tareWeight", "Bì xe / vỏ (Tare)")}
                        </span>
                        <span className="text-xs font-bold font-mono text-ink-secondary">
                          {selectedItem.tareWeightKg.toLocaleString()} kg
                        </span>
                      </div>
                      <div className="p-2">
                        <span className="text-[10px] text-ink-tertiary block font-medium">
                          {t("mill.reconciliation.netWeight", "Khối lượng tịnh (Net)")}
                        </span>
                        <span className="text-xs font-bold font-mono text-accent-primary">
                          {selectedItem.netWeightKg.toLocaleString()} kg
                        </span>
                      </div>
                    </div>

                    <div className="text-[11px] text-ink-secondary flex items-center justify-between pt-1">
                      <span>{t("mill.reconciliation.scaleDevice", "Thiết bị cân:")}</span>
                      <span className="font-mono font-medium text-ink-primary">
                        {selectedItem.scaleDeviceName}
                      </span>
                    </div>
                  </div>

                  {/* KCS Lab Evidence Slip */}
                  <div className="p-4 sm:p-5 rounded-2xl border border-ink-tertiary/20 bg-surface-base dark:bg-surface-raised space-y-3 shadow-xs">
                    <div className="flex items-center justify-between border-b border-ink-tertiary/15 pb-2.5">
                      <div className="flex items-center gap-3">
                        <div className="w-9 h-9 rounded-xl bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 flex items-center justify-center shrink-0">
                          <FilePdf size={18} weight="bold" />
                        </div>
                        <div>
                          <h3 className="text-xs font-display font-bold text-ink-primary">
                            {t(
                              "mill.reconciliation.labSlipTitle",
                              "Phiếu Kiểm Tra Chất Lượng Phòng Lab (KCS)"
                            )}
                          </h3>
                          <span className="text-[11px] text-ink-tertiary font-mono">
                            {t(
                              "mill.reconciliation.kcsReportInspector",
                              "Biên bản: {{slip}} • Giám định viên: {{inspector}}",
                              {
                                slip: selectedItem.kcsSlipNumber,
                                inspector: selectedItem.inspectorName,
                              }
                            )}
                          </span>
                        </div>
                      </div>
                      <span className="text-[10px] font-bold px-2 py-0.5 rounded-full bg-emerald-50 dark:bg-emerald-950/40 text-emerald-700 dark:text-emerald-300 border border-emerald-500/20">
                        {t("mill.reconciliation.techStandardPassed", "Đạt chuẩn kỹ thuật")}
                      </span>
                    </div>

                    <div className="grid grid-cols-3 gap-3 text-xs">
                      <div className="p-3 rounded-xl bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/15">
                        <span className="text-[10px] text-ink-tertiary block font-medium">
                          {t("mill.reconciliation.declaredMoisture", "Độ ẩm khai báo")}
                        </span>
                        <span className="font-mono font-bold text-ink-primary text-xs mt-0.5 block">
                          {selectedItem.moistureDeclared}%
                        </span>
                      </div>
                      <div className="p-3 rounded-xl bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/15">
                        <span className="text-[10px] text-ink-tertiary block font-medium">
                          {t("mill.reconciliation.measuredMoisture", "Độ ẩm đo thực tế KCS")}
                        </span>
                        <span className="font-mono font-bold text-accent-primary text-xs mt-0.5 block">
                          {selectedItem.moistureMeasured}%
                        </span>
                      </div>
                      <div className="p-3 rounded-xl bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/15">
                        <span className="text-[10px] text-ink-tertiary block font-medium">
                          {t("mill.reconciliation.foreignMatter", "Tạp chất đo được")}
                        </span>
                        <span className="font-mono font-bold text-ink-primary text-xs mt-0.5 block">
                          {selectedItem.foreignMatterPercent}%
                        </span>
                      </div>
                    </div>
                  </div>
                </div>
              )}

              {/* Tab 2: Tolerance & Balance Check */}
              {activeTab === "TOLERANCE" && (
                <div className="p-5 space-y-4">
                  {/* Gauge Card */}
                  <div
                    className={`p-5 rounded-2xl border ${selectedItem.withinTolerance
                        ? "bg-emerald-50/50 dark:bg-emerald-950/20 border-emerald-500/30"
                        : "bg-functional-rejected-bg border-functional-rejected-border"
                      } space-y-3.5 shadow-xs`}
                  >
                    <div className="flex items-center justify-between">
                      <div className="flex items-center gap-3">
                        {selectedItem.withinTolerance ? (
                          <div className="w-10 h-10 rounded-xl bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 flex items-center justify-center shrink-0">
                            <ShieldCheck size={22} weight="bold" />
                          </div>
                        ) : (
                          <div className="w-10 h-10 rounded-xl bg-accent-danger/10 text-accent-danger flex items-center justify-center shrink-0">
                            <ShieldWarning size={22} weight="bold" />
                          </div>
                        )}
                        <div>
                          <h3 className="text-sm font-display font-bold text-ink-primary">
                            {selectedItem.withinTolerance
                              ? t(
                                "mill.reconciliation.withinToleranceAlert",
                                "Sai lệch NẰM TRONG Dung sai quy định"
                              )
                              : t(
                                "mill.reconciliation.outOfToleranceAlert",
                                "CẢNH BÁO: Sai lệch VƯỢT QUÁ Dung sai tối đa"
                              )}
                          </h3>
                          <p className="text-xs text-ink-secondary mt-0.5">
                            {t(
                              "mill.reconciliation.tolerancePolicyDesc",
                              "Ngưỡng cho phép theo Chính sách cân bằng nhà máy: ±{{percent}}%",
                              { percent: selectedItem.tolerancePercent }
                            )}
                          </p>
                        </div>
                      </div>

                      <span
                        className={`text-xs font-mono font-bold px-3 py-1 rounded-full ${selectedItem.withinTolerance
                            ? "bg-emerald-600 text-white"
                            : "bg-accent-danger text-white"
                          }`}
                      >
                        {selectedItem.variancePercent > 0
                          ? `+${selectedItem.variancePercent}%`
                          : `${selectedItem.variancePercent}%`}
                      </span>
                    </div>

                    {/* Visual Progress / Tolerance Range */}
                    <div className="space-y-1.5 pt-1">
                      <div className="flex justify-between text-[11px] text-ink-secondary font-mono">
                        <span>
                          {t(
                            "mill.reconciliation.actualVariance",
                            "Sai lệch thực tế: {{variance}} kg",
                            {
                              variance:
                                selectedItem.varianceKg > 0
                                  ? `+${selectedItem.varianceKg}`
                                  : selectedItem.varianceKg,
                            }
                          )}
                        </span>
                        <span>
                          {t(
                            "mill.reconciliation.maxToleranceLimit",
                            "Giới hạn tối đa: ±{{limit}} kg",
                            {
                              limit: (
                                selectedItem.sourceQuantityKg *
                                (selectedItem.tolerancePercent / 100)
                              ).toFixed(1),
                            }
                          )}
                        </span>
                      </div>
                      <div className="w-full h-3 rounded-full bg-surface-raised dark:bg-surface-overlay overflow-hidden flex relative border border-ink-tertiary/15">
                        <div
                          className={`h-full ${selectedItem.withinTolerance
                              ? "bg-emerald-600"
                              : "bg-accent-danger"
                            }`}
                          style={{
                            width: `${Math.min(100, (Math.abs(selectedItem.variancePercent) / selectedItem.tolerancePercent) * 70)}%`,
                          }}
                        />
                      </div>
                    </div>
                  </div>

                  {/* Mass Balance Matrix */}
                  <div className="p-4 sm:p-5 rounded-2xl border border-ink-tertiary/20 bg-surface-base dark:bg-surface-raised space-y-3 shadow-xs">
                    <h4 className="text-xs font-display font-bold text-ink-primary uppercase tracking-wider">
                      {t("mill.reconciliation.massBalanceTitle", "Cân bằng khối lượng công đoạn")}
                    </h4>

                    <div className="space-y-2 text-xs">
                      <div className="flex justify-between py-2 border-b border-ink-tertiary/10">
                        <span className="text-ink-secondary">
                          {t("mill.reconciliation.declaredInputWeight", "Khối lượng đầu vào khai báo:")}
                        </span>
                        <span className="font-mono font-bold text-ink-primary">
                          {selectedItem.sourceQuantityKg.toLocaleString()} kg
                        </span>
                      </div>
                      <div className="flex justify-between py-2 border-b border-ink-tertiary/10">
                        <span className="text-ink-secondary">
                          {t(
                            "mill.reconciliation.actualMeasuredWeight",
                            "Khối lượng cân thực tế (sau đối soát):"
                          )}
                        </span>
                        <span className="font-mono font-bold text-ink-primary">
                          {selectedItem.actualQuantityKg.toLocaleString()} kg
                        </span>
                      </div>
                      <div className="flex justify-between py-2 border-b border-ink-tertiary/10">
                        <span className="text-ink-secondary">
                          {t("mill.reconciliation.deltaNetWeight", "Chênh lệch khối lượng ròng (Delta):")}
                        </span>
                        <span
                          className={`font-mono font-bold ${selectedItem.varianceKg > 0
                              ? "text-amber-600 dark:text-amber-400"
                              : "text-accent-danger"
                            }`}
                        >
                          {selectedItem.varianceKg > 0
                            ? `+${selectedItem.varianceKg}`
                            : selectedItem.varianceKg}{" "}
                          kg
                        </span>
                      </div>
                    </div>
                  </div>
                </div>
              )}

              {/* Tab 3: Manager Sign-off & Verdict */}
              {activeTab === "SIGN_OFF" && (
                <div className="p-5 space-y-4">
                  <div className="p-4 sm:p-5 rounded-2xl bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/15 space-y-3">
                    <div className="flex items-center gap-2">
                      <ShieldCheck size={20} className="text-accent-primary" />
                      <h3 className="text-xs font-display font-bold text-ink-primary uppercase tracking-wider">
                        {t(
                          "mill.reconciliation.signOffTitle",
                          "Biên Bản & Quyết Định Đối Soát Của Quản Lý Nhà Máy"
                        )}
                      </h3>
                    </div>

                    <p className="text-xs text-ink-secondary leading-relaxed">
                      {t(
                        "mill.reconciliation.signOffRuleDesc",
                        "Theo quy định Rule BR-01 và BR-12, Quản lý nhà máy có trách nhiệm xác minh toàn bộ chứng từ đo lường. Sau khi ký xác nhận đối soát, hồ sơ sẽ được chuyển sang trạng thái sẵn sàng để phát hành Bút Toán Bù (SC-18) vào Sổ cái bất biến."
                      )}
                    </p>

                    <div className="space-y-2 pt-2">
                      <label className="text-xs font-semibold text-ink-primary block">
                        {t("mill.reconciliation.notesLabel", "Kết luận & Ghi chú đối soát:")}
                      </label>
                      <textarea
                        rows={4}
                        value={reviewNotes}
                        onChange={(e) => setReviewNotes(e.target.value)}
                        placeholder={t(
                          "mill.reconciliation.notesPlaceholder",
                          "Ghi rõ: Đã kiểm tra phiếu cân, mẫu lưu KCS số... Chuẩn y khối lượng điều chỉnh hoặc yêu cầu cân lại..."
                        )}
                        className="w-full p-3 text-xs rounded-xl border border-ink-tertiary/20 bg-surface-base dark:bg-surface-raised text-ink-primary placeholder:text-ink-tertiary focus:border-accent-primary focus:outline-none leading-relaxed transition-colors"
                      />
                    </div>

                    {selectedItem.reviewedAt && (
                      <div className="p-3 rounded-xl bg-surface-base dark:bg-surface-raised border border-ink-tertiary/15 text-[11px] text-ink-secondary space-y-1">
                        <div>
                          <strong className="text-ink-primary">
                            {t("mill.reconciliation.reviewedByLabel", "Ký duyệt bởi:")}
                          </strong>{" "}
                          {selectedItem.reviewedBy}
                        </div>
                        <div>
                          <strong className="text-ink-primary">
                            {t("mill.reconciliation.reviewTimeLabel", "Thời gian:")}
                          </strong>{" "}
                          {new Date(selectedItem.reviewedAt).toLocaleString(
                            "vi-VN"
                          )}
                        </div>
                      </div>
                    )}

                    {/* Action buttons */}
                    <div className="pt-3 border-t border-ink-tertiary/15 flex flex-col sm:flex-row items-center gap-3">
                      <button
                        type="button"
                        onClick={handleApproveReconciliation}
                        disabled={isSubmitting}
                        className="w-full sm:w-auto flex-1 inline-flex items-center justify-center gap-2 px-5 py-2.5 rounded-lg bg-emerald-600 hover:bg-emerald-700 text-white text-xs font-bold transition-all shadow-xs disabled:opacity-60"
                      >
                        <CheckCircle size={16} weight="bold" />
                        <span>{t("mill.reconciliation.btnApprove", "Xác nhận đối soát hợp lệ (SC-17)")}</span>
                      </button>

                      <button
                        type="button"
                        onClick={handleRejectReconciliation}
                        disabled={isSubmitting}
                        className="w-full sm:w-auto inline-flex items-center justify-center gap-2 px-5 py-2.5 rounded-lg border border-accent-danger/30 text-accent-danger hover:bg-functional-rejected-bg text-xs font-bold transition-colors disabled:opacity-60"
                      >
                        <XCircle size={16} weight="bold" />
                        <span>{t("mill.reconciliation.btnReject", "Từ chối / Yêu cầu cân lại")}</span>
                      </button>
                    </div>

                    {/* Next step prompt */}
                    {selectedItem.status === "RECONCILED" && (
                      <div className="mt-4 p-4 rounded-xl bg-accent-primary/10 border border-accent-primary/20 flex flex-col sm:flex-row sm:items-center justify-between gap-3">
                        <div className="space-y-0.5">
                          <span className="text-xs font-bold text-accent-primary block">
                            {t(
                              "mill.reconciliation.step1Completed",
                              "Hồ sơ đã hoàn tất bước 1: Đối soát bằng chứng"
                            )}
                          </span>
                          <span className="text-[11px] text-ink-secondary">
                            {t(
                              "mill.reconciliation.step2Prompt",
                              "Tiếp tục sang bước 2 để chốt bút toán bù đảo ngược vào Sổ cái bất biến (SC-18)."
                            )}
                          </span>
                        </div>
                        <Link
                          to="/mill/corrections/pending"
                          className="shrink-0 inline-flex items-center gap-2 px-4 py-2 text-xs font-bold rounded-lg bg-accent-primary hover:bg-accent-primary-dim text-white transition-all shadow-xs"
                        >
                          <span>{t("mill.reconciliation.btnGoToCompensation", "Chuyển sang Ghi bút toán bù")}</span>
                          <ArrowRight size={14} weight="bold" />
                        </Link>
                      </div>
                    )}
                  </div>
                </div>
              )}
            </div>
          ) : (
            <div className="p-12 text-center bg-surface-base dark:bg-surface-raised border border-ink-tertiary/20 rounded-2xl space-y-2">
              <ClipboardText size={40} className="mx-auto text-ink-tertiary opacity-60" />
              <p className="text-sm font-semibold text-ink-primary">
                {t(
                  "mill.reconciliation.selectDossierPrompt",
                  "Chọn một hồ sơ đối soát bên trái để thẩm định bằng chứng"
                )}
              </p>
              <p className="text-xs text-ink-secondary">
                {t(
                  "mill.reconciliation.selectDossierDesc",
                  "Bạn có thể xem phiếu cân, chứng từ kiểm tra KCS và ký phê duyệt đối soát."
                )}
              </p>
            </div>
          )}
        </div>
      </div>
    </div>
  );
};
