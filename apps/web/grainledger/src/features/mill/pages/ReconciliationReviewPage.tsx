import { ledgerEndpoints } from "@/api/ledger";
import { useAuthStore } from "@/app/store/authStore";
import {
  ArrowClockwise,
  ArrowRight,
  ArrowsLeftRight,
  CaretRight,
  CheckCircle,
  ClipboardText,
  ClockCounterClockwise,
  Eye,
  FilePdf,
  FileText,
  Funnel,
  Info,
  LockSimple,
  MagnifyingGlass,
  Scales,
  ShieldCheck,
  ShieldWarning,
  Warning,
  WarningCircle,
  X,
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

  const isManager =
    user?.role === "MILL_MANAGER" || user?.role === "MILL_ADMIN";

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
  const [reviewVerdict, setReviewVerdict] = useState<"APPROVE" | "REJECT">(
    "APPROVE",
  );
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
        // Fallback: pick the first item if specific ID not matched
        setSelectedItem(items[0]);
        setReviewNotes(items[0].managerNotes || "");
      }
    } else if (!selectedItem && items.length > 0) {
      // Select first item by default on desktop
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
      {/* Top Breadcrumb & Workflow Switcher */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
        <div className="flex items-center gap-2 text-xs text-ink-secondary">
          <Link to="/home" className="hover:text-ink-primary transition">
            {t("nav.manager.home", "Bàn điều hành Quản lý")}
          </Link>
          <span>/</span>
          <span className="text-ink-primary font-semibold">
            {t("mill.reconciliation.breadcrumb", "Đối soát & Xác nhận (SC-17)")}
          </span>
        </div>

        <div className="flex items-center gap-2">
          <Link
            to="/mill/corrections/pending"
            className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold rounded-lg border border-ink-tertiary/20 bg-surface-base hover:bg-surface-raised text-ink-primary transition shadow-xs"
            title={t("mill.reconciliation.switchCommitLink", "Chuyển sang Ghi bút toán bù (SC-18)")}
          >
            <ClockCounterClockwise size={14} className="text-accent-info" />
            <span>{t("mill.reconciliation.switchCommitLink", "Chuyển sang Ghi bút toán bù (SC-18)")}</span>
            <ArrowRight size={13} />
          </Link>
        </div>
      </div>

      {/* Main Header Banner */}
      <div className="bg-surface-base border border-ink-tertiary/20 rounded-xl p-5 sm:p-6 shadow-xs relative overflow-hidden">
        <div className="flex flex-col lg:flex-row lg:items-center lg:justify-between gap-4">
          <div className="flex items-start gap-4">
            <div className="w-12 h-12 rounded-xl bg-amber-500/10 text-amber-600 dark:text-amber-400 flex items-center justify-center shrink-0 border border-amber-500/20">
              <ClipboardText size={28} weight="duotone" />
            </div>
            <div className="space-y-1">
              <div className="flex items-center gap-2 flex-wrap">
                <span className="font-mono text-[11px] font-bold px-2 py-0.5 rounded bg-amber-100 dark:bg-amber-950/40 text-amber-800 dark:text-amber-200 border border-amber-300 dark:border-amber-700/50">
                  {t("mill.reconciliation.tagRule", "SC-17 • Feature F08 / Rule BR-01 & BR-12")}
                </span>
                <span className="text-xs text-ink-tertiary">
                  {t("mill.reconciliation.roleTag", "Quyền: Quản lý nhà máy (Mill Manager)")}
                </span>
              </div>
              <h1 className="text-xl sm:text-2xl font-bold font-serif text-ink-primary">
                {t(
                  "mill.reconciliation.pageTitle",
                  "Đối Soát & Xác Nhận Bằng Chứng (Reconciliation & Evidence Review)"
                )}
              </h1>
              <p className="text-xs sm:text-sm text-ink-secondary leading-relaxed max-w-3xl">
                {t(
                  "mill.reconciliation.pageSubtitle",
                  "Thẩm định bằng chứng đo lường thực tế (phiếu cân bàn, biên bản KCS kiểm tra độ ẩm/tạp chất), so sánh mức độ sai lệch với ngưỡng dung sai chính sách và ký biên bản đối soát trước khi chuyển giao sang quy trình ghi bút toán bù sổ cái."
                )}
              </p>
            </div>
          </div>

          <div className="shrink-0 flex items-center gap-2">
            <button
              onClick={() => {
                setLoading(true);
                setTimeout(() => setLoading(false), 500);
              }}
              disabled={loading}
              className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold rounded-lg border border-ink-tertiary/20 bg-surface-raised hover:bg-surface-overlay text-ink-secondary hover:text-ink-primary transition"
            >
              <ArrowClockwise
                size={14}
                className={loading ? "animate-spin" : ""}
              />
              <span>{t("common.update", "Cập nhật")}</span>
            </button>
          </div>
        </div>
      </div>

      {/* KPI Stats Bar */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 sm:gap-4">
        <div className="bg-surface-base border border-ink-tertiary/20 rounded-xl p-4 shadow-xs">
          <div className="flex items-center justify-between text-xs text-ink-secondary">
            <span>{t("mill.reconciliation.kpiPending", "Chờ thẩm định đối soát")}</span>
            <span className="w-2 h-2 rounded-full bg-amber-500 animate-pulse" />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className="text-2xl font-bold font-mono text-amber-700 dark:text-amber-400">
              {stats.pending}
            </span>
            <span className="text-xs text-ink-tertiary">{t("mill.reconciliation.dossiersUnit", "hồ sơ")}</span>
          </div>
        </div>

        <div className="bg-surface-base border border-ink-tertiary/20 rounded-xl p-4 shadow-xs">
          <div className="flex items-center justify-between text-xs text-ink-secondary">
            <span>{t("mill.reconciliation.kpiWithinTol", "Trong dung sai cho phép")}</span>
            <ShieldCheck
              size={16}
              className="text-accent-success"
              weight="fill"
            />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className="text-2xl font-bold font-mono text-accent-success">
              {stats.withinToleranceCount} / {stats.total}
            </span>
            <span className="text-xs text-ink-tertiary">(≤ 0.5%)</span>
          </div>
        </div>

        <div className="bg-surface-base border border-ink-tertiary/20 rounded-xl p-4 shadow-xs">
          <div className="flex items-center justify-between text-xs text-ink-secondary">
            <span>{t("mill.reconciliation.kpiReconciled", "Đã đối soát & chuẩn y")}</span>
            <CheckCircle size={16} className="text-accent-info" weight="fill" />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className="text-2xl font-bold font-mono text-accent-info">
              {stats.reconciled}
            </span>
            <span className="text-xs text-ink-tertiary">{t("mill.reconciliation.dossiersUnit", "hồ sơ")}</span>
          </div>
        </div>

        <div className="bg-surface-base border border-ink-tertiary/20 rounded-xl p-4 shadow-xs">
          <div className="flex items-center justify-between text-xs text-ink-secondary">
            <span>{t("mill.reconciliation.kpiRejected", "Yêu cầu cân lại / Từ chối")}</span>
            <XCircle size={16} className="text-accent-danger" weight="fill" />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className="text-2xl font-bold font-mono text-accent-danger">
              {stats.rejected}
            </span>
            <span className="text-xs text-ink-tertiary">{t("mill.reconciliation.dossiersUnit", "hồ sơ")}</span>
          </div>
        </div>
      </div>

      {/* Main 2-Column Workbench Layout */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-6 items-start">
        {/* Left Column: List of Reconciliation Requests (5 cols) */}
        <div className="lg:col-span-5 space-y-4">
          <div className="bg-surface-base border border-ink-tertiary/20 rounded-xl p-4 shadow-xs space-y-3">
            <div className="flex items-center justify-between">
              <h2 className="text-sm font-bold text-ink-primary font-serif flex items-center gap-1.5">
                <FileText size={16} className="text-accent-primary" />
                <span>{t("mill.reconciliation.listTitle", "Danh mục hồ sơ đối soát")} ({filteredItems.length})</span>
              </h2>
            </div>

            {/* Filter Tabs */}
            <div className="flex items-center gap-1.5 p-1 bg-surface-raised rounded-lg border border-ink-tertiary/10 text-xs">
              <button
                onClick={() => setStatusFilter("ALL")}
                className={`flex-1 py-1 rounded font-semibold transition ${
                  statusFilter === "ALL"
                    ? "bg-surface-base text-ink-primary shadow-xs"
                    : "text-ink-secondary hover:text-ink-primary"
                }`}
              >
                {t("common.all", "Tất cả")} ({items.length})
              </button>
              <button
                onClick={() => setStatusFilter("PENDING_REVIEW")}
                className={`flex-1 py-1 rounded font-semibold transition ${
                  statusFilter === "PENDING_REVIEW"
                    ? "bg-surface-base text-amber-700 dark:text-amber-400 shadow-xs"
                    : "text-ink-secondary hover:text-ink-primary"
                }`}
              >
                {t("mill.reconciliation.tabPending", "Chờ duyệt")} ({stats.pending})
              </button>
              <button
                onClick={() => setStatusFilter("RECONCILED")}
                className={`flex-1 py-1 rounded font-semibold transition ${
                  statusFilter === "RECONCILED"
                    ? "bg-surface-base text-accent-info shadow-xs"
                    : "text-ink-secondary hover:text-ink-primary"
                }`}
              >
                {t("mill.reconciliation.tabReconciled", "Đã duyệt")} ({stats.reconciled})
              </button>
            </div>

            {/* Search Input */}
            <div className="relative">
              <MagnifyingGlass
                size={14}
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
                className="w-full pl-8 pr-3 py-1.5 text-xs rounded-lg border border-ink-tertiary/20 bg-surface-raised dark:bg-surface-base text-ink-primary placeholder:text-ink-tertiary focus:outline-none focus:ring-1 focus:ring-accent-primary"
              />
            </div>
          </div>

          {/* List items */}
          <div className="space-y-2.5 max-h-[640px] overflow-y-auto pr-1">
            {filteredItems.map((item) => {
              const isSelected = selectedItem?.id === item.id;
              return (
                <div
                  key={item.id}
                  onClick={() => handleSelectItem(item)}
                  className={`p-3.5 rounded-xl border cursor-pointer transition text-left space-y-2 relative ${
                    isSelected
                      ? "bg-accent-primary/5 border-accent-primary ring-1 ring-accent-primary shadow-xs"
                      : "bg-surface-base border-ink-tertiary/20 hover:border-ink-tertiary/40 hover:bg-surface-raised/40"
                  }`}
                >
                  <div className="flex items-center justify-between gap-2">
                    <div className="flex items-center gap-1.5">
                      <span className="font-mono text-xs font-bold text-ink-primary">
                        {item.id}
                      </span>
                      <span className="text-[11px] font-mono px-1.5 py-0.5 rounded bg-surface-raised text-ink-secondary border border-ink-tertiary/10">
                        {item.operationId}
                      </span>
                    </div>

                    {item.status === "PENDING_REVIEW" && (
                      <span className="inline-flex items-center gap-1 text-[11px] font-bold px-2 py-0.5 rounded-full bg-amber-100 dark:bg-amber-950/50 text-amber-800 dark:text-amber-300 border border-amber-300 dark:border-amber-700/50">
                        <span className="w-1.5 h-1.5 rounded-full bg-amber-500 animate-pulse" />
                        {t("mill.reconciliation.statusPending", "Chờ đối soát")}
                      </span>
                    )}
                    {item.status === "RECONCILED" && (
                      <span className="inline-flex items-center gap-1 text-[11px] font-bold px-2 py-0.5 rounded-full bg-accent-info/10 text-accent-info border border-accent-info/20">
                        <CheckCircle size={12} weight="fill" />
                        {t("mill.reconciliation.statusReconciled", "Đã đối soát")}
                      </span>
                    )}
                    {item.status === "REJECTED" && (
                      <span className="inline-flex items-center gap-1 text-[11px] font-bold px-2 py-0.5 rounded-full bg-accent-danger/10 text-accent-danger border border-accent-danger/20">
                        <XCircle size={12} weight="fill" />
                        {t("mill.reconciliation.statusRejected", "Cần cân lại")}
                      </span>
                    )}
                  </div>

                  <p className="text-xs font-semibold text-ink-primary line-clamp-1">
                    {item.category}
                  </p>

                  <p className="text-[11px] text-ink-secondary line-clamp-2 leading-relaxed">
                    {item.description}
                  </p>

                  <div className="pt-1.5 border-t border-ink-tertiary/10 flex items-center justify-between text-[11px]">
                    <div className="flex items-center gap-1 font-mono">
                      <span className="text-ink-tertiary">{t("mill.reconciliation.varianceLabel", "Lệch:")}</span>
                      <span
                        className={`font-bold ${
                          item.varianceKg > 0
                            ? "text-accent-warning"
                            : "text-accent-danger"
                        }`}
                      >
                        {item.varianceKg > 0
                          ? `+${item.varianceKg}`
                          : item.varianceKg}{" "}
                        kg
                      </span>
                      <span className="text-ink-tertiary">
                        ({Math.abs(item.variancePercent)}%)
                      </span>
                    </div>

                    <span
                      className={`font-semibold px-1.5 py-0.5 rounded text-[10px] ${
                        item.withinTolerance
                          ? "bg-accent-success/10 text-accent-success"
                          : "bg-accent-danger/10 text-accent-danger"
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
              <div className="p-8 text-center bg-surface-base rounded-xl border border-ink-tertiary/20 text-xs text-ink-secondary">
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
            <div className="bg-surface-base border border-ink-tertiary/20 rounded-xl shadow-xs overflow-hidden">
              {/* Header Panel */}
              <div className="p-5 border-b border-ink-tertiary/20 bg-surface-raised/40 space-y-3">
                <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-2">
                  <div>
                    <div className="flex items-center gap-2">
                      <h2 className="text-lg font-bold font-serif text-ink-primary">
                        {t("mill.reconciliation.dossierDetailTitle", "Hồ Sơ Đối Soát {{id}}", {
                          id: selectedItem.id,
                        })}
                      </h2>
                      <span className="text-xs font-mono px-2 py-0.5 rounded bg-surface-base border border-ink-tertiary/20 text-ink-secondary">
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
                        className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-bold rounded-lg bg-accent-primary hover:bg-accent-primary-dim text-white transition shadow-xs"
                      >
                        <span>{t("mill.reconciliation.btnPostCompensation", "Ghi bút toán bù (SC-18)")}</span>
                        <ArrowRight size={13} weight="bold" />
                      </Link>
                    )}
                  </div>
                </div>

                {/* Status alert message */}
                {actionMessage && (
                  <div
                    className={`p-3 rounded-lg text-xs font-semibold flex items-center gap-2 ${
                      actionMessage.type === "success"
                        ? "bg-accent-success/10 text-accent-success border border-accent-success/20"
                        : "bg-accent-danger/10 text-accent-danger border border-accent-danger/20"
                    }`}
                  >
                    <CheckCircle size={16} weight="fill" />
                    <span>{actionMessage.text}</span>
                  </div>
                )}

                {/* Inspector Tabs */}
                <div className="flex items-center gap-2 border-b border-ink-tertiary/10 pt-2 text-xs">
                  <button
                    onClick={() => setActiveTab("EVIDENCE")}
                    className={`pb-2 px-1 font-bold border-b-2 transition flex items-center gap-1.5 ${
                      activeTab === "EVIDENCE"
                        ? "border-accent-primary text-accent-primary"
                        : "border-transparent text-ink-secondary hover:text-ink-primary"
                    }`}
                  >
                    <Scales size={15} />
                    <span>{t("mill.reconciliation.tabEvidence", "1. Bằng chứng phiếu cân & KCS")}</span>
                  </button>
                  <button
                    onClick={() => setActiveTab("TOLERANCE")}
                    className={`pb-2 px-1 font-bold border-b-2 transition flex items-center gap-1.5 ${
                      activeTab === "TOLERANCE"
                        ? "border-accent-primary text-accent-primary"
                        : "border-transparent text-ink-secondary hover:text-ink-primary"
                    }`}
                  >
                    <ShieldCheck size={15} />
                    <span>{t("mill.reconciliation.tabTolerance", "2. Đánh giá dung sai & Cân bằng")}</span>
                  </button>
                  <button
                    onClick={() => setActiveTab("SIGN_OFF")}
                    className={`pb-2 px-1 font-bold border-b-2 transition flex items-center gap-1.5 ${
                      activeTab === "SIGN_OFF"
                        ? "border-accent-primary text-accent-primary"
                        : "border-transparent text-ink-secondary hover:text-ink-primary"
                    }`}
                  >
                    <ClipboardText size={15} />
                    <span>{t("mill.reconciliation.tabSignOff", "3. Biên bản & Ký duyệt Quản lý")}</span>
                  </button>
                </div>
              </div>

              {/* Tab 1: Physical Evidence Inspector */}
              {activeTab === "EVIDENCE" && (
                <div className="p-5 space-y-5">
                  {/* Category & Reason Description */}
                  <div className="p-4 rounded-xl bg-surface-raised border border-ink-tertiary/10 space-y-1.5">
                    <span className="text-[11px] font-bold text-accent-primary uppercase tracking-wider">
                      {t("mill.reconciliation.reasonTitle", "Lý do & Nội dung khai báo sai lệch")}
                    </span>
                    <p className="text-xs font-semibold text-ink-primary">
                      {selectedItem.category}
                    </p>
                    <p className="text-xs text-ink-secondary leading-relaxed">
                      {selectedItem.description}
                    </p>
                  </div>

                  {/* Weighbridge Evidence Slip */}
                  <div className="p-4 rounded-xl border border-ink-tertiary/20 bg-surface-base space-y-3">
                    <div className="flex items-center justify-between border-b border-ink-tertiary/10 pb-2">
                      <div className="flex items-center gap-2">
                        <div className="w-7 h-7 rounded-lg bg-accent-info/10 text-accent-info flex items-center justify-center">
                          <Scales size={16} weight="duotone" />
                        </div>
                        <div>
                          <h3 className="text-xs font-bold text-ink-primary">
                            {t("mill.reconciliation.weighSlipTitle", "Bằng chứng Phiếu Cân Điện Tử")}
                          </h3>
                          <span className="text-[10px] text-ink-tertiary font-mono">
                            {t("mill.reconciliation.ticketNumber", "Số phiếu:")} {selectedItem.scaleTicketNumber}
                          </span>
                        </div>
                      </div>
                      <span className="text-[11px] font-mono text-ink-tertiary">
                        {selectedItem.weighTimestamp}
                      </span>
                    </div>

                    <div className="grid grid-cols-3 gap-3 text-center py-2 bg-surface-raised rounded-lg">
                      <div className="p-2 border-r border-ink-tertiary/10">
                        <span className="text-[10px] text-ink-tertiary block">
                          {t("mill.reconciliation.grossWeight", "Tổng bì (Gross)")}
                        </span>
                        <span className="text-xs font-bold font-mono text-ink-primary">
                          {selectedItem.grossWeightKg.toLocaleString()} kg
                        </span>
                      </div>
                      <div className="p-2 border-r border-ink-tertiary/10">
                        <span className="text-[10px] text-ink-tertiary block">
                          {t("mill.reconciliation.tareWeight", "Bì xe / vỏ (Tare)")}
                        </span>
                        <span className="text-xs font-bold font-mono text-ink-secondary">
                          {selectedItem.tareWeightKg.toLocaleString()} kg
                        </span>
                      </div>
                      <div className="p-2">
                        <span className="text-[10px] text-ink-tertiary block">
                          {t("mill.reconciliation.netWeight", "Khối lượng tịnh (Net)")}
                        </span>
                        <span className="text-xs font-bold font-mono text-accent-primary">
                          {selectedItem.netWeightKg.toLocaleString()} kg
                        </span>
                      </div>
                    </div>

                    <div className="text-[11px] text-ink-secondary flex items-center justify-between">
                      <span>{t("mill.reconciliation.scaleDevice", "Thiết bị cân:")}</span>
                      <span className="font-mono font-medium text-ink-primary">
                        {selectedItem.scaleDeviceName}
                      </span>
                    </div>
                  </div>

                  {/* KCS Lab Evidence Slip */}
                  <div className="p-4 rounded-xl border border-ink-tertiary/20 bg-surface-base space-y-3">
                    <div className="flex items-center justify-between border-b border-ink-tertiary/10 pb-2">
                      <div className="flex items-center gap-2">
                        <div className="w-7 h-7 rounded-lg bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 flex items-center justify-center">
                          <FilePdf size={16} weight="duotone" />
                        </div>
                        <div>
                          <h3 className="text-xs font-bold text-ink-primary">
                            {t(
                              "mill.reconciliation.labSlipTitle",
                              "Phiếu Kiểm Tra Chất Lượng Phòng Lab (KCS)"
                            )}
                          </h3>
                          <span className="text-[10px] text-ink-tertiary font-mono">
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
                      <span className="text-[10px] font-bold px-2 py-0.5 rounded bg-accent-success/10 text-accent-success">
                        {t("mill.reconciliation.techStandardPassed", "Đạt chuẩn kỹ thuật")}
                      </span>
                    </div>

                    <div className="grid grid-cols-3 gap-3 text-xs">
                      <div className="p-2.5 rounded-lg bg-surface-raised border border-ink-tertiary/10">
                        <span className="text-[10px] text-ink-tertiary block">
                          {t("mill.reconciliation.declaredMoisture", "Độ ẩm khai báo")}
                        </span>
                        <span className="font-mono font-bold text-ink-primary">
                          {selectedItem.moistureDeclared}%
                        </span>
                      </div>
                      <div className="p-2.5 rounded-lg bg-surface-raised border border-ink-tertiary/10">
                        <span className="text-[10px] text-ink-tertiary block">
                          {t("mill.reconciliation.measuredMoisture", "Độ ẩm đo thực tế KCS")}
                        </span>
                        <span className="font-mono font-bold text-accent-primary">
                          {selectedItem.moistureMeasured}%
                        </span>
                      </div>
                      <div className="p-2.5 rounded-lg bg-surface-raised border border-ink-tertiary/10">
                        <span className="text-[10px] text-ink-tertiary block">
                          {t("mill.reconciliation.foreignMatter", "Tạp chất đo được")}
                        </span>
                        <span className="font-mono font-bold text-ink-primary">
                          {selectedItem.foreignMatterPercent}%
                        </span>
                      </div>
                    </div>
                  </div>
                </div>
              )}

              {/* Tab 2: Tolerance & Balance Check */}
              {activeTab === "TOLERANCE" && (
                <div className="p-5 space-y-5">
                  {/* Gauge Card */}
                  <div
                    className={`p-5 rounded-xl border ${
                      selectedItem.withinTolerance
                        ? "bg-accent-success/5 border-accent-success/30"
                        : "bg-accent-danger/5 border-accent-danger/30"
                    } space-y-3`}
                  >
                    <div className="flex items-center justify-between">
                      <div className="flex items-center gap-2">
                        {selectedItem.withinTolerance ? (
                          <ShieldCheck
                            size={22}
                            weight="fill"
                            className="text-accent-success"
                          />
                        ) : (
                          <ShieldWarning
                            size={22}
                            weight="fill"
                            className="text-accent-danger"
                          />
                        )}
                        <div>
                          <h3 className="text-sm font-bold text-ink-primary">
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
                          <p className="text-xs text-ink-secondary">
                            {t(
                              "mill.reconciliation.tolerancePolicyDesc",
                              "Ngưỡng cho phép theo Chính sách cân bằng nhà máy: ±{{percent}}%",
                              { percent: selectedItem.tolerancePercent }
                            )}
                          </p>
                        </div>
                      </div>

                      <span
                        className={`text-xs font-mono font-bold px-2.5 py-1 rounded-full ${
                          selectedItem.withinTolerance
                            ? "bg-accent-success text-white"
                            : "bg-accent-danger text-white"
                        }`}
                      >
                        {selectedItem.variancePercent > 0
                          ? `+${selectedItem.variancePercent}%`
                          : `${selectedItem.variancePercent}%`}
                      </span>
                    </div>

                    {/* Visual Progress / Tolerance Range */}
                    <div className="space-y-1.5 pt-2">
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
                      <div className="w-full h-3 rounded-full bg-surface-raised overflow-hidden flex relative">
                        {/* 0.5% tolerance region marker */}
                        <div
                          className={`h-full ${
                            selectedItem.withinTolerance
                              ? "bg-accent-success"
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
                  <div className="p-4 rounded-xl border border-ink-tertiary/20 bg-surface-base space-y-3">
                    <h4 className="text-xs font-bold text-ink-primary font-serif uppercase tracking-wider">
                      {t("mill.reconciliation.massBalanceTitle", "Cân bằng khối lượng công đoạn")}
                    </h4>

                    <div className="space-y-2 text-xs">
                      <div className="flex justify-between py-1.5 border-b border-ink-tertiary/10">
                        <span className="text-ink-secondary">
                          {t("mill.reconciliation.declaredInputWeight", "Khối lượng đầu vào khai báo:")}
                        </span>
                        <span className="font-mono font-bold text-ink-primary">
                          {selectedItem.sourceQuantityKg.toLocaleString()} kg
                        </span>
                      </div>
                      <div className="flex justify-between py-1.5 border-b border-ink-tertiary/10">
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
                      <div className="flex justify-between py-1.5 border-b border-ink-tertiary/10">
                        <span className="text-ink-secondary">
                          {t("mill.reconciliation.deltaNetWeight", "Chênh lệch khối lượng ròng (Delta):")}
                        </span>
                        <span
                          className={`font-mono font-bold ${
                            selectedItem.varianceKg > 0
                              ? "text-accent-warning"
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
                <div className="p-5 space-y-5">
                  <div className="p-4 rounded-xl bg-surface-raised border border-ink-tertiary/10 space-y-3">
                    <div className="flex items-center gap-2">
                      <ShieldCheck size={20} className="text-accent-primary" />
                      <h3 className="text-xs font-bold text-ink-primary uppercase tracking-wider">
                        {t(
                          "mill.reconciliation.signOffTitle",
                          "Biên Bản & Quyết Định Đối Soát Của Quản Lý Nhà Máy"
                        )}
                      </h3>
                    </div>

                    <p className="text-xs text-ink-secondary">
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
                        className="w-full p-3 text-xs rounded-lg border border-ink-tertiary/30 bg-surface-base text-ink-primary placeholder:text-ink-tertiary focus:outline-none focus:ring-1 focus:ring-accent-primary leading-relaxed"
                      />
                    </div>

                    {selectedItem.reviewedAt && (
                      <div className="p-2.5 rounded-lg bg-surface-base border border-ink-tertiary/10 text-[11px] text-ink-secondary space-y-1">
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
                    <div className="pt-3 border-t border-ink-tertiary/10 flex flex-col sm:flex-row items-center gap-3">
                      <button
                        onClick={handleApproveReconciliation}
                        disabled={isSubmitting}
                        className="w-full sm:w-auto flex-1 inline-flex items-center justify-center gap-2 px-4 py-2.5 rounded-lg bg-accent-success hover:bg-emerald-600 text-white text-xs font-bold transition shadow-xs disabled:opacity-60"
                      >
                        <CheckCircle size={16} weight="bold" />
                        <span>{t("mill.reconciliation.btnApprove", "Xác nhận đối soát hợp lệ (SC-17)")}</span>
                      </button>

                      <button
                        onClick={handleRejectReconciliation}
                        disabled={isSubmitting}
                        className="w-full sm:w-auto inline-flex items-center justify-center gap-2 px-4 py-2.5 rounded-lg border border-accent-danger/30 text-accent-danger hover:bg-accent-danger/10 text-xs font-bold transition disabled:opacity-60"
                      >
                        <XCircle size={16} weight="bold" />
                        <span>{t("mill.reconciliation.btnReject", "Từ chối / Yêu cầu cân lại")}</span>
                      </button>
                    </div>

                    {/* Next step prompt */}
                    {selectedItem.status === "RECONCILED" && (
                      <div className="mt-4 p-3.5 rounded-xl bg-accent-info/10 border border-accent-info/20 flex items-center justify-between gap-3">
                        <div className="space-y-0.5">
                          <span className="text-xs font-bold text-accent-info block">
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
                          className="shrink-0 inline-flex items-center gap-1.5 px-3.5 py-1.5 text-xs font-bold rounded-lg bg-accent-info hover:bg-accent-info-dim text-white transition shadow-xs"
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
            <div className="p-12 text-center bg-surface-base border border-ink-tertiary/20 rounded-xl space-y-2">
              <ClipboardText size={40} className="mx-auto text-ink-tertiary" />
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
