import { catalogEndpoints } from "@/api/catalogs";
import { useAuthStore } from "@/app/store/authStore";
import {
  ArrowClockwise,
  ArrowRight,
  Barcode,
  Calendar,
  CheckCircle,
  Clock,
  ClockCounterClockwise,
  FilePdf,
  FileText,
  Flask,
  MagnifyingGlass,
  Plus,
  Printer,
  QrCode,
  Scales,
  SealCheck,
  ShieldCheck,
  Tag,
  TestTube,
  Truck,
  User,
  WarningCircle,
  X,
} from "@phosphor-icons/react";
import React, { useEffect, useMemo, useState } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { useTranslation } from "react-i18next";

export interface LabSampleRecord {
  id: string;
  sampleCode: string;
  sealCode: string;
  lotId: string;
  lotName: string;
  productType: string;
  sampleWeightKg: number;
  samplingStandard: string;
  samplingLocation: string;
  labName: string;
  labAddress: string;
  targetMarket: string;
  samplingDate: string;
  dispatchedDate?: string;
  estimatedResultDate: string;
  samplerName: string;
  status: "DRAFT" | "SENT_TO_LAB" | "ANALYZING" | "RESULT_RECEIVED";
  requestedSubstances: string[];
  notes?: string;
  linkedResultId?: string;
}

const SAMPLE_DATA: LabSampleRecord[] = [
  {
    id: "SMP-2026-001",
    sampleCode: "NIFC-2026-XN-8821",
    sealCode: "SEAL-VN-2026-0941",
    lotId: "LOT-TG-2026-001",
    lotName: "Lúa OM5451 Dây chuyền Sấy 01",
    productType: "Lúa sấy khô (Paddy Rice)",
    sampleWeightKg: 1.5,
    samplingStandard: "TCVN 9027:2011 / ISO 24333:2009",
    samplingLocation: "Nhà máy Tiền Giang - Cửa xả silo lưu tạm số 2",
    labName: "Viện Kiểm Nghiệm An Toàn Thực Phẩm Quốc Gia (NIFC)",
    labAddress: "65 Phạm Thận Duật, Cầu Giấy, Hà Nội",
    targetMarket: "Liên minh Châu Âu (EU) - EC 396/2005",
    samplingDate: "2026-10-14",
    dispatchedDate: "2026-10-14",
    estimatedResultDate: "2026-10-16",
    samplerName: "Nguyễn Văn KCS (Kỹ thuật viên phòng KCS)",
    status: "RESULT_RECEIVED",
    requestedSubstances: [
      "Tricyclazole",
      "Hexaconazole",
      "Chlorpyrifos-ethyl",
      "Isoprothiolane",
      "Cadimi (Cd)",
    ],
    notes:
      "Mẫu lưu đối chứng 3 túi niêm phong riêng biệt (1 gửi Lab, 1 lưu KCS, 1 giao khách hàng).",
    linkedResultId: "RES-2026-001",
  },
  {
    id: "SMP-2026-002",
    sampleCode: "EUROFINS-2026-4412",
    sealCode: "SEAL-VN-2026-0942",
    lotId: "LOT-TG-2026-002",
    lotName: "Gạo Lứt ST25 Thơm Cao Cấp",
    productType: "Gạo lứt (Brown Rice)",
    sampleWeightKg: 2.0,
    samplingStandard: "TCVN 9027:2011 (Lấy mẫu tự động phân đoạn)",
    samplingLocation: "Nhà máy Tiền Giang - Băng tải cấp sau sàng tạp chất",
    labName: "Eurofins Sắc Ký Hải Đăng",
    labAddress: "Lô E2b-3, Đường D1, Khu Công nghệ cao, TP. Thủ Đức, TP. HCM",
    targetMarket: "Hoa Kỳ (US FDA MRL CFR Title 21)",
    samplingDate: "2026-10-15",
    dispatchedDate: "2026-10-15",
    estimatedResultDate: "2026-10-17",
    samplerName: "Trần Thị Lan (KCS Trưởng)",
    status: "RESULT_RECEIVED",
    requestedSubstances: [
      "Tricyclazole",
      "Isoprothiolane",
      "Difenoconazole",
      "Carbendazim",
    ],
    notes:
      "Mẫu có nguy cơ tồn dư do nông hộ khai báo xịt đợt cuối giáp ngày thu hoạch.",
    linkedResultId: "RES-2026-002",
  },
  {
    id: "SMP-2026-003",
    sampleCode: "SGS-2026-VN-1109",
    sealCode: "SEAL-VN-2026-0945",
    lotId: "LOT-TG-2026-003",
    lotName: "Gạo trắng 5% tấm ST24 Xuất khẩu",
    productType: "Gạo trắng (Milled White Rice)",
    sampleWeightKg: 1.5,
    samplingStandard: "ISO 24333:2009",
    samplingLocation: "Nhà máy Tiền Giang - Cửa cấp máy đánh bóng số 1",
    labName: "SGS Vietnam Ltd - Trung tâm Kiểm nghiệm Nông sản",
    labAddress: "198 Nguyễn Thị Minh Khai, Quận 3, TP. HCM",
    targetMarket: "Nhật Bản (MHLW Positive List)",
    samplingDate: "2026-10-16",
    dispatchedDate: "2026-10-16",
    estimatedResultDate: "2026-10-18",
    samplerName: "Nguyễn Văn KCS (Kỹ thuật viên)",
    status: "ANALYZING",
    requestedSubstances: [
      "Tricyclazole",
      "Hexaconazole",
      "Buprofezin",
      "Fipronil",
      "Thiamethoxam",
    ],
    notes: "Đơn vị vận chuyển EMS chuyển phát nhanh hoả tốc trong ngày.",
  },
  {
    id: "SMP-2026-004",
    sampleCode: "QUATEST3-2026-8801",
    sealCode: "SEAL-VN-2026-0948",
    lotId: "LOT-TG-2026-004",
    lotName: "Gạo Jasmine 85 Chờ Đóng Bao",
    productType: "Gạo trắng (White Rice)",
    sampleWeightKg: 1.5,
    samplingStandard: "TCVN 9027:2011",
    samplingLocation: "Nhà máy Tiền Giang - Cửa xả Silo 4",
    labName: "Trung tâm Kỹ thuật Tiêu chuẩn Đo lường Chất lượng 3 (QUATEST 3)",
    labAddress: "Khu Công nghiệp Biên Hòa 1, Đồng Nai",
    targetMarket: "Liên minh Châu Âu (EU) - EC 396/2005",
    samplingDate: "2026-10-16",
    estimatedResultDate: "2026-10-19",
    samplerName: "Trần Thị Lan (KCS Trưởng)",
    status: "SENT_TO_LAB",
    requestedSubstances: [
      "Tricyclazole",
      "Azoxystrobin",
      "Propiconazole",
      "Chì (Pb)",
      "Cadimi (Cd)",
    ],
    notes: "Đang làm thủ tục bàn giao niêm phong tại trạm chuyển phát.",
  },
];

interface LabSampleRegistrationPageProps {
  defaultTab?: "LIST" | "CREATE";
}

export const LabSampleRegistrationPage: React.FC<
  LabSampleRegistrationPageProps
> = ({ defaultTab = "LIST" }) => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const location = useLocation();
  const { user } = useAuthStore();

  const isNewRoute =
    location.pathname.endsWith("/new") || defaultTab === "CREATE";
  const [activeTab, setActiveTab] = useState<"LIST" | "CREATE">(
    isNewRoute ? "CREATE" : "LIST",
  );

  const [samples, setSamples] = useState<LabSampleRecord[]>(SAMPLE_DATA);
  const [searchQuery, setSearchQuery] = useState("");
  const [statusFilter, setStatusFilter] = useState<string>("ALL");
  const [selectedSample, setSelectedSample] = useState<LabSampleRecord | null>(
    null,
  );

  // Form registration state
  const [formData, setFormData] = useState({
    lotId: "LOT-TG-2026-005",
    lotName: "Gạo Nàng Hoa 9 Thành phẩm",
    productType: "Gạo trắng (Milled White Rice)",
    sampleWeightKg: 1.5,
    samplingStandard: "TCVN 9027:2011 / ISO 24333:2009",
    samplingLocation: "Nhà máy Tiền Giang - Cửa xả Silo 5",
    labName: "Viện Kiểm Nghiệm An Toàn Thực Phẩm Quốc Gia (NIFC)",
    labAddress: "65 Phạm Thận Duật, Cầu Giấy, Hà Nội",
    targetMarket: "Liên minh Châu Âu (EU) - EC 396/2005",
    samplingDate: new Date().toISOString().split("T")[0],
    estimatedDays: 2,
    sealCode: `SEAL-VN-${new Date().getFullYear()}-${Math.floor(1000 + Math.random() * 9000)}`,
    samplerName: user?.displayName || "Nguyễn Văn KCS",
    notes: "Mẫu đại diện 1.5 kg chia làm 3 túi niêm phong đồng kiểm tra.",
    substances: [
      "Tricyclazole",
      "Hexaconazole",
      "Chlorpyrifos-ethyl",
      "Isoprothiolane",
    ],
  });

  const [isSubmitting, setIsSubmitting] = useState(false);
  const [successBanner, setSuccessBanner] = useState<string | null>(null);

  // Sync tab with URL if user clicks /compliance/lab-samples/new
  useEffect(() => {
    if (location.pathname.endsWith("/new")) {
      setActiveTab("CREATE");
    }
  }, [location.pathname]);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setIsSubmitting(true);

    setTimeout(() => {
      const newId = `SMP-2026-${String(samples.length + 1).padStart(3, "0")}`;
      const newSampleCode = `NIFC-2026-XN-${Math.floor(1000 + Math.random() * 9000)}`;

      const estDate = new Date();
      estDate.setDate(estDate.getDate() + formData.estimatedDays);

      const createdItem: LabSampleRecord = {
        id: newId,
        sampleCode: newSampleCode,
        sealCode: formData.sealCode,
        lotId: formData.lotId,
        lotName: formData.lotName,
        productType: formData.productType,
        sampleWeightKg: formData.sampleWeightKg,
        samplingStandard: formData.samplingStandard,
        samplingLocation: formData.samplingLocation,
        labName: formData.labName,
        labAddress: formData.labAddress,
        targetMarket: formData.targetMarket,
        samplingDate: formData.samplingDate,
        dispatchedDate: formData.samplingDate,
        estimatedResultDate: estDate.toISOString().split("T")[0],
        samplerName: formData.samplerName,
        status: "SENT_TO_LAB",
        requestedSubstances: formData.substances,
        notes: formData.notes,
      };

      setSamples([createdItem, ...samples]);
      setIsSubmitting(false);
      setSuccessBanner(
        t(
          "compliance.labSample.createSuccessBanner",
          "Đã đăng ký thành công Mẫu xét nghiệm {{id}} (Mã niêm phong: {{seal}}). Hồ sơ đã đưa vào chuỗi hành trình gửi phòng Lab.",
          { id: newId, seal: formData.sealCode }
        )
      );
      setActiveTab("LIST");
    }, 600);
  };

  const handlePrintHandover = (sample: LabSampleRecord) => {
    alert(
      `[${t("compliance.labSample.printHandoverTitle", "IN BIÊN BẢN BÀN GIAO MẪU NIÊM PHONG")}]\n${t("compliance.labSample.thCodeSeal", "Mã mẫu")}: ${sample.sampleCode}\n${t("compliance.labSample.leadSealLabel", "Mã niêm phong")}: ${sample.sealCode}\n${t("compliance.labSample.thLot", "Lô hàng")}: ${sample.lotId}\n${t("compliance.labSample.receivingLab", "Đơn vị tiếp nhận")}: ${sample.labName}\n${t("compliance.labSample.samplerNameLabel", "Người lấy mẫu")}: ${sample.samplerName}\n\n${t("compliance.labSample.printHandoverReady", "Đã sẵn sàng gửi kèm kiện mẫu gửi Lab.")}`
    );
  };

  const filteredSamples = useMemo(() => {
    return samples.filter((s) => {
      const matchStatus = statusFilter === "ALL" || s.status === statusFilter;
      const q = searchQuery.toLowerCase().trim();
      const matchQuery =
        !q ||
        s.id.toLowerCase().includes(q) ||
        s.sampleCode.toLowerCase().includes(q) ||
        s.sealCode.toLowerCase().includes(q) ||
        s.lotId.toLowerCase().includes(q) ||
        s.labName.toLowerCase().includes(q) ||
        s.productType.toLowerCase().includes(q);
      return matchStatus && matchQuery;
    });
  }, [samples, statusFilter, searchQuery]);

  const stats = useMemo(() => {
    const sent = samples.filter((s) => s.status === "SENT_TO_LAB").length;
    const analyzing = samples.filter((s) => s.status === "ANALYZING").length;
    const completed = samples.filter(
      (s) => s.status === "RESULT_RECEIVED",
    ).length;
    return {
      total: samples.length,
      sent,
      analyzing,
      completed,
    };
  }, [samples]);

  const renderStatusBadge = (status: LabSampleRecord["status"]) => {
    switch (status) {
      case "DRAFT":
        return (
          <span className="px-2 py-0.5 rounded-full text-[11px] font-semibold bg-surface-raised text-ink-secondary border border-ink-tertiary/20">
            {t("compliance.labSample.statusDraft", "Bản nháp")}
          </span>
        );
      case "SENT_TO_LAB":
        return (
          <span className="px-2.5 py-0.5 rounded-full text-[11px] font-semibold bg-blue-500/10 text-blue-600 dark:text-blue-400 border border-blue-500/20 inline-flex items-center gap-1">
            <Truck size={12} weight="bold" />
            {t("compliance.labSample.statusSent", "Đang gửi Lab")}
          </span>
        );
      case "ANALYZING":
        return (
          <span className="px-2.5 py-0.5 rounded-full text-[11px] font-semibold bg-amber-500/10 text-amber-600 dark:text-amber-400 border border-amber-500/20 inline-flex items-center gap-1">
            <Clock size={12} weight="bold" className="animate-spin" />
            {t("compliance.labSample.statusAnalyzing", "Đang thử nghiệm")}
          </span>
        );
      case "RESULT_RECEIVED":
        return (
          <span className="px-2.5 py-0.5 rounded-full text-[11px] font-semibold bg-accent-success/10 text-accent-success border border-accent-success/20 inline-flex items-center gap-1">
            <CheckCircle size={12} weight="fill" />
            {t("compliance.labSample.statusCompleted", "Đã có kết quả")}
          </span>
        );
    }
  };

  return (
    <div className="space-y-6 pb-12">
      {/* Top Breadcrumb & Workflow Switcher */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
        <div className="flex items-center gap-2 text-xs text-ink-secondary">
          <Link to="/home" className="hover:text-ink-primary transition">
            {t("common.managerDashboard", "Bàn điều hành Quản lý")}
          </Link>
          <span>/</span>
          <span className="text-ink-primary font-semibold">
            {t("compliance.labSample.breadcrumb", "Mẫu xét nghiệm (SC-23)")}
          </span>
        </div>

        <div className="flex items-center gap-2">
          <Link
            to="/compliance/lab-results"
            className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold rounded-lg border border-accent-primary/30 bg-accent-primary/10 hover:bg-accent-primary/15 text-accent-primary transition shadow-xs"
            title={t("compliance.labSample.switchResults", "Chuyển sang Kết quả xét nghiệm (SC-24)")}
          >
            <TestTube size={14} weight="bold" />
            <span>{t("compliance.labSample.switchResults", "Chuyển sang Kết quả xét nghiệm (SC-24)")}</span>
            <ArrowRight size={13} weight="bold" />
          </Link>
        </div>
      </div>

      {/* Main Header Banner */}
      <div className="bg-surface-base border border-ink-tertiary/20 rounded-xl p-5 sm:p-6 shadow-xs relative overflow-hidden">
        <div className="flex flex-col lg:flex-row lg:items-center lg:justify-between gap-4">
          <div className="flex items-start gap-4">
            <div className="w-12 h-12 rounded-xl bg-accent-primary/10 text-accent-primary flex items-center justify-center shrink-0 border border-accent-primary/20">
              <Flask size={28} weight="duotone" />
            </div>
            <div className="space-y-1">
              <h1 className="text-xl sm:text-2xl font-bold font-serif text-ink-primary">
                {t(
                  "compliance.labSample.pageTitle",
                  "Lấy Mẫu Niêm Phong & Gửi Phòng Lab (Lab Sample Chain of Custody)"
                )}
              </h1>
              <p className="text-xs sm:text-sm text-ink-secondary leading-relaxed max-w-3xl">
                {t(
                  "compliance.labSample.pageSubtitle",
                  "Khởi tạo hồ sơ mẫu ngũ cốc đại diện theo tiêu chuẩn ISO 24333, niêm phong chì điện tử và theo dõi chuỗi hành trình bàn giao đến phòng xét nghiệm chỉ định ISO/IEC 17025."
                )}
              </p>
            </div>
          </div>

          <div className="shrink-0 flex items-center gap-2">
            <button
              onClick={() =>
                setActiveTab(activeTab === "LIST" ? "CREATE" : "LIST")
              }
              className={`inline-flex items-center gap-1.5 px-3.5 py-2 text-xs font-bold rounded-lg transition shadow-xs ${
                activeTab === "CREATE"
                  ? "border border-ink-tertiary/20 bg-surface-raised hover:bg-surface-overlay text-ink-primary"
                  : "bg-accent-primary hover:bg-accent-primary-dim text-white"
              }`}
            >
              {activeTab === "CREATE" ? (
                <>
                  <FileText size={15} />
                  <span>{t("compliance.labSample.btnViewList", "Xem danh sách mẫu")}</span>
                </>
              ) : (
                <>
                  <Plus size={15} weight="bold" />
                  <span>{t("compliance.labSample.btnRegisterNew", "Đăng ký mẫu mới (SC-23)")}</span>
                </>
              )}
            </button>
          </div>
        </div>
      </div>

      {/* Success Banner */}
      {successBanner && (
        <div className="p-3.5 rounded-xl bg-accent-success/10 border border-accent-success/20 text-accent-success text-xs font-semibold flex items-center justify-between">
          <div className="flex items-center gap-2">
            <CheckCircle size={16} weight="fill" />
            <span>{successBanner}</span>
          </div>
          <button
            onClick={() => setSuccessBanner(null)}
            className="p-1 hover:bg-accent-success/20 rounded-md transition"
          >
            <X size={14} />
          </button>
        </div>
      )}

      {/* KPI Stats Bar */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 sm:gap-4">
        <div className="bg-surface-base border border-ink-tertiary/20 rounded-xl p-4 shadow-xs">
          <div className="flex items-center justify-between text-xs text-ink-secondary">
            <span>{t("compliance.labSample.kpiTotal", "Tổng số mẫu gửi")}</span>
            <Flask size={16} className="text-accent-primary" />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className="text-2xl font-bold font-mono text-ink-primary">
              {stats.total}
            </span>
            <span className="text-xs text-ink-tertiary">
              {t("compliance.labSample.sealedSamplesUnit", "mẫu niêm phong")}
            </span>
          </div>
        </div>

        <div className="bg-surface-base border border-ink-tertiary/20 rounded-xl p-4 shadow-xs">
          <div className="flex items-center justify-between text-xs text-ink-secondary">
            <span>{t("compliance.labSample.kpiSent", "Đang chuyển phát Lab")}</span>
            <Truck size={16} className="text-blue-500" />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className="text-2xl font-bold font-mono text-blue-600 dark:text-blue-400">
              {stats.sent}
            </span>
            <span className="text-xs text-ink-tertiary">
              {t("compliance.labSample.awaitingReceiptUnit", "chờ tiếp nhận")}
            </span>
          </div>
        </div>

        <div className="bg-surface-base border border-ink-tertiary/20 rounded-xl p-4 shadow-xs">
          <div className="flex items-center justify-between text-xs text-ink-secondary">
            <span>{t("compliance.labSample.kpiAnalyzing", "Đang thử nghiệm Lab")}</span>
            <Clock size={16} className="text-amber-500" />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className="text-2xl font-bold font-mono text-amber-600 dark:text-amber-400">
              {stats.analyzing}
            </span>
            <span className="text-xs text-ink-tertiary">
              {t("compliance.labSample.awaitingResultUnit", "chờ kết quả")}
            </span>
          </div>
        </div>

        <div className="bg-surface-base border border-ink-tertiary/20 rounded-xl p-4 shadow-xs">
          <div className="flex items-center justify-between text-xs text-ink-secondary">
            <span>{t("compliance.labSample.kpiCompleted", "Đã có kết quả")}</span>
            <CheckCircle
              size={16}
              className="text-accent-success"
              weight="fill"
            />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className="text-2xl font-bold font-mono text-accent-success">
              {stats.completed}
            </span>
            <span className="text-xs text-ink-tertiary">
              {t("compliance.labSample.reviewedUnit", "đã thẩm định")}
            </span>
          </div>
        </div>
      </div>

      {/* Main View Switcher */}
      {activeTab === "CREATE" ? (
        /* Create Sample Registration Form (SC-23) */
        <div className="bg-surface-base border border-ink-tertiary/20 rounded-xl shadow-xs overflow-hidden">
          <div className="p-5 border-b border-ink-tertiary/15 bg-surface-raised/40 flex items-center justify-between">
            <div className="space-y-0.5">
              <h2 className="text-base font-bold font-serif text-ink-primary flex items-center gap-2">
                <SealCheck size={20} className="text-accent-primary" />
                <span>{t("compliance.labSample.formTitle", "Đăng Ký Mẫu Niêm Phong Mới Gửi Phòng Lab (SC-23)")}</span>
              </h2>
              <p className="text-xs text-ink-secondary">
                {t(
                  "compliance.labSample.formSubtitle",
                  "Tuân thủ quy trình lấy mẫu ngẫu nhiên đại diện ISO 24333 và niêm phong chì chống tráo đổi."
                )}
              </p>
            </div>

            <button
              type="button"
              onClick={() => setActiveTab("LIST")}
              className="px-3 py-1.5 text-xs font-semibold rounded-lg border border-ink-tertiary/20 text-ink-secondary hover:text-ink-primary transition"
            >
              {t("common.cancel", "Hủy thao tác")}
            </button>
          </div>

          <form onSubmit={handleSubmit} className="p-6 space-y-6">
            {/* 3 Column Grid Info */}
            <div className="grid grid-cols-1 md:grid-cols-3 gap-5">
              {/* Col 1: Lô hàng & Khối lượng */}
              <div className="space-y-4">
                <h3 className="text-xs font-bold uppercase tracking-wider text-accent-primary pb-1 border-b border-ink-tertiary/10">
                  {t("compliance.labSample.sectionLotInfo", "1. Thông tin Lô hàng lấy mẫu")}
                </h3>

                <div>
                  <label className="block text-xs font-semibold text-ink-primary mb-1">
                    {t("compliance.labSample.lotIdLabel", "Mã Lô hàng đại diện (Lot ID) *")}
                  </label>
                  <select
                    value={formData.lotId}
                    onChange={(e) =>
                      setFormData({ ...formData, lotId: e.target.value })
                    }
                    className="w-full px-3 py-2 text-xs rounded-lg border border-ink-tertiary/30 bg-surface-base text-ink-primary focus:outline-none focus:ring-1 focus:ring-accent-primary font-mono"
                  >
                    <option value="LOT-TG-2026-001">
                      LOT-TG-2026-001 (Lúa OM5451)
                    </option>
                    <option value="LOT-TG-2026-002">
                      LOT-TG-2026-002 (Gạo Lứt ST25)
                    </option>
                    <option value="LOT-TG-2026-003">
                      LOT-TG-2026-003 (Gạo trắng ST24 5%)
                    </option>
                    <option value="LOT-TG-2026-004">
                      LOT-TG-2026-004 (Gạo Jasmine 85)
                    </option>
                    <option value="LOT-TG-2026-005">
                      LOT-TG-2026-005 (Gạo Nàng Hoa 9)
                    </option>
                  </select>
                </div>

                <div>
                  <label className="block text-xs font-semibold text-ink-primary mb-1">
                    {t("compliance.labSample.productTypeLabel", "Loại sản phẩm")}
                  </label>
                  <input
                    type="text"
                    value={formData.productType}
                    onChange={(e) =>
                      setFormData({ ...formData, productType: e.target.value })
                    }
                    className="w-full px-3 py-2 text-xs rounded-lg border border-ink-tertiary/30 bg-surface-base text-ink-primary focus:outline-none focus:ring-1 focus:ring-accent-primary"
                  />
                </div>

                <div>
                  <label className="block text-xs font-semibold text-ink-primary mb-1">
                    {t("compliance.labSample.sampleWeightLabel", "Khối lượng mẫu gửi (kg) *")}
                  </label>
                  <div className="relative">
                    <input
                      type="number"
                      step="0.1"
                      min="0.5"
                      value={formData.sampleWeightKg}
                      onChange={(e) =>
                        setFormData({
                          ...formData,
                          sampleWeightKg: parseFloat(e.target.value) || 1.5,
                        })
                      }
                      className="w-full pl-3 pr-10 py-2 text-xs rounded-lg border border-ink-tertiary/30 bg-surface-base text-ink-primary focus:outline-none focus:ring-1 focus:ring-accent-primary font-mono"
                    />
                    <span className="absolute right-3 top-1/2 -translate-y-1/2 text-xs text-ink-tertiary">
                      kg
                    </span>
                  </div>
                  <span className="text-[10px] text-ink-tertiary mt-1 block">
                    {t(
                      "compliance.labSample.weightIsoNote",
                      "Khuyến nghị ISO 24333: Tối thiểu 1.5 kg cho thử nghiệm dư lượng & vi sinh."
                    )}
                  </span>
                </div>

                <div>
                  <label className="block text-xs font-semibold text-ink-primary mb-1">
                    {t("compliance.labSample.samplingLocationLabel", "Điểm lấy mẫu thực tế")}
                  </label>
                  <input
                    type="text"
                    value={formData.samplingLocation}
                    onChange={(e) =>
                      setFormData({
                        ...formData,
                        samplingLocation: e.target.value,
                      })
                    }
                    className="w-full px-3 py-2 text-xs rounded-lg border border-ink-tertiary/30 bg-surface-base text-ink-primary focus:outline-none focus:ring-1 focus:ring-accent-primary"
                  />
                </div>
              </div>

              {/* Col 2: Phòng Lab & Niêm phong */}
              <div className="space-y-4">
                <h3 className="text-xs font-bold uppercase tracking-wider text-accent-primary pb-1 border-b border-ink-tertiary/10">
                  {t("compliance.labSample.sectionLabSeal", "2. Phòng Lab & Niêm phong chì")}
                </h3>

                <div>
                  <label className="block text-xs font-semibold text-ink-primary mb-1">
                    {t("compliance.labSample.labNameLabel", "Đơn vị phòng kiểm nghiệm ISO/IEC 17025 *")}
                  </label>
                  <select
                    value={formData.labName}
                    onChange={(e) =>
                      setFormData({ ...formData, labName: e.target.value })
                    }
                    className="w-full px-3 py-2 text-xs rounded-lg border border-ink-tertiary/30 bg-surface-base text-ink-primary focus:outline-none focus:ring-1 focus:ring-accent-primary"
                  >
                    <option value="Viện Kiểm Nghiệm An Toàn Thực Phẩm Quốc Gia (NIFC)">
                      Viện Kiểm Nghiệm Vệ Sinh An Toàn Thực Phẩm Quốc Gia (NIFC)
                    </option>
                    <option value="Eurofins Sắc Ký Hải Đăng">
                      Eurofins Sắc Ký Hải Đăng
                    </option>
                    <option value="SGS Vietnam Ltd - Trung tâm Kiểm nghiệm Nông sản">
                      SGS Vietnam Ltd
                    </option>
                    <option value="Trung tâm Kỹ thuật Tiêu chuẩn Đo lường Chất lượng 3 (QUATEST 3)">
                      QUATEST 3
                    </option>
                  </select>
                </div>

                <div>
                  <label className="block text-xs font-semibold text-ink-primary mb-1">
                    {t("compliance.labSample.leadSealLabel", "Mã số niêm phong chì bảo mật (Lead Seal Code) *")}
                  </label>
                  <div className="flex items-center gap-2">
                    <input
                      type="text"
                      required
                      value={formData.sealCode}
                      onChange={(e) =>
                        setFormData({ ...formData, sealCode: e.target.value })
                      }
                      className="flex-1 px-3 py-2 text-xs rounded-lg border border-ink-tertiary/30 bg-surface-base text-ink-primary font-mono focus:outline-none focus:ring-1 focus:ring-accent-primary font-bold"
                    />
                    <button
                      type="button"
                      onClick={() =>
                        setFormData({
                          ...formData,
                          sealCode: `SEAL-VN-${new Date().getFullYear()}-${Math.floor(
                            1000 + Math.random() * 9000,
                          )}`,
                        })
                      }
                      className="px-2.5 py-2 text-xs rounded-lg border border-ink-tertiary/20 hover:bg-surface-raised text-ink-secondary"
                      title={t("compliance.labSample.generateRandomSeal", "Sinh mã niêm phong ngẫu nhiên")}
                    >
                      <Barcode size={16} />
                    </button>
                  </div>
                </div>

                <div>
                  <label className="block text-xs font-semibold text-ink-primary mb-1">
                    {t("compliance.labSample.targetMarketLabel", "Thị trường xuất khẩu mục tiêu (Tiêu chuẩn MRL) *")}
                  </label>
                  <select
                    value={formData.targetMarket}
                    onChange={(e) =>
                      setFormData({ ...formData, targetMarket: e.target.value })
                    }
                    className="w-full px-3 py-2 text-xs rounded-lg border border-ink-tertiary/30 bg-surface-base text-ink-primary focus:outline-none focus:ring-1 focus:ring-accent-primary"
                  >
                    <option value="Liên minh Châu Âu (EU) - EC 396/2005">
                      Liên minh Châu Âu (EU) - EC 396/2005 (Safety Margin -20%)
                    </option>
                    <option value="Hoa Kỳ (US FDA MRL CFR Title 21)">
                      Hoa Kỳ (US FDA MRL CFR Title 21)
                    </option>
                    <option value="Nhật Bản (MHLW Positive List)">
                      Nhật Bản (MHLW Positive List)
                    </option>
                    <option value="Trung Quốc (GB 2763-2021)">
                      Trung Quốc (GB 2763-2021)
                    </option>
                  </select>
                </div>

                <div className="grid grid-cols-2 gap-2">
                  <div>
                    <label className="block text-xs font-semibold text-ink-primary mb-1">
                      {t("compliance.labSample.samplingDateLabel", "Ngày lấy mẫu")}
                    </label>
                    <input
                      type="date"
                      value={formData.samplingDate}
                      onChange={(e) =>
                        setFormData({
                          ...formData,
                          samplingDate: e.target.value,
                        })
                      }
                      className="w-full px-2.5 py-2 text-xs rounded-lg border border-ink-tertiary/30 bg-surface-base text-ink-primary focus:outline-none"
                    />
                  </div>
                  <div>
                    <label className="block text-xs font-semibold text-ink-primary mb-1">
                      {t("compliance.labSample.estDaysLabel", "Thời gian Lab dự kiến")}
                    </label>
                    <select
                      value={formData.estimatedDays}
                      onChange={(e) =>
                        setFormData({
                          ...formData,
                          estimatedDays: parseInt(e.target.value) || 2,
                        })
                      }
                      className="w-full px-2.5 py-2 text-xs rounded-lg border border-ink-tertiary/30 bg-surface-base text-ink-primary focus:outline-none"
                    >
                      <option value="1">24 giờ (Hỏa tốc)</option>
                      <option value="2">48 giờ (Tiêu chuẩn)</option>
                      <option value="3">72 giờ (Chuyên sâu)</option>
                    </select>
                  </div>
                </div>
              </div>

              {/* Col 3: Chỉ tiêu phân tích & Ghi chú */}
              <div className="space-y-4">
                <h3 className="text-xs font-bold uppercase tracking-wider text-accent-primary pb-1 border-b border-ink-tertiary/10">
                  {t("compliance.labSample.sectionSubstances", "3. Chỉ tiêu kiểm nghiệm yêu cầu")}
                </h3>

                <div className="space-y-2 p-3 rounded-lg bg-surface-raised border border-ink-tertiary/10 text-xs">
                  <span className="font-semibold text-ink-primary block mb-1">
                    {t("compliance.labSample.substancesListLabel", "Hoạt chất thuốc BVTV cần định lượng:")}
                  </span>
                  {[
                    "Tricyclazole (Thuốc trị đạo ôn)",
                    "Hexaconazole (Thuốc trừ nấm)",
                    "Chlorpyrifos-ethyl (Thuốc trừ sâu)",
                    "Isoprothiolane (Trừ đạo ôn cổ bông)",
                    "Difenoconazole (Trừ nấm hạt)",
                    "Cadimi (Cd) & Kim loại nặng",
                  ].map((sub, i) => (
                    <label
                      key={i}
                      className="flex items-center gap-2 text-ink-secondary cursor-pointer"
                    >
                      <input
                        type="checkbox"
                        defaultChecked={i < 4}
                        className="rounded text-accent-primary focus:ring-accent-primary"
                      />
                      <span>{sub}</span>
                    </label>
                  ))}
                </div>

                <div>
                  <label className="block text-xs font-semibold text-ink-primary mb-1">
                    {t("compliance.labSample.chainNotesLabel", "Ghi chú niêm phong & Chuỗi bàn giao (Chain of Custody)")}
                  </label>
                  <textarea
                    rows={3}
                    value={formData.notes}
                    onChange={(e) =>
                      setFormData({ ...formData, notes: e.target.value })
                    }
                    className="w-full p-2.5 text-xs rounded-lg border border-ink-tertiary/30 bg-surface-base text-ink-primary focus:outline-none focus:ring-1 focus:ring-accent-primary"
                  />
                </div>
              </div>
            </div>

            {/* Submit Action */}
            <div className="pt-4 border-t border-ink-tertiary/15 flex flex-col sm:flex-row items-center justify-between gap-3">
              <div className="text-xs text-ink-secondary flex items-center gap-1.5">
                <ShieldCheck size={16} className="text-accent-success" />
                <span>
                  {t(
                    "compliance.labSample.auditStandardNote",
                    "Mẫu sẽ được mã hóa Barcode và lưu vết kiểm toán bất biến theo chuẩn BR-19."
                  )}
                </span>
              </div>

              <div className="flex items-center gap-2 w-full sm:w-auto">
                <button
                  type="button"
                  onClick={() => setActiveTab("LIST")}
                  className="px-4 py-2 rounded-lg border border-ink-tertiary/30 text-ink-secondary hover:text-ink-primary text-xs font-semibold transition"
                >
                  {t("common.cancel", "Hủy bỏ")}
                </button>

                <button
                  type="submit"
                  disabled={isSubmitting}
                  className="px-5 py-2 rounded-lg bg-accent-primary hover:bg-accent-primary-dim text-white text-xs font-bold transition shadow-xs flex items-center gap-1.5"
                >
                  <SealCheck size={16} weight="bold" />
                  <span>
                    {isSubmitting
                      ? t("common.processing", "Đang xử lý...")
                      : t("compliance.labSample.btnSubmitSeal", "Niêm phong & Tạo hồ sơ gửi Lab (SC-23)")}
                  </span>
                </button>
              </div>
            </div>
          </form>
        </div>
      ) : (
        /* List Samples View (SC-23) */
        <div className="space-y-4">
          {/* Filter Bar */}
          <div className="bg-surface-base border border-ink-tertiary/20 rounded-xl p-3 sm:p-4 shadow-xs flex flex-col sm:flex-row sm:items-center justify-between gap-3">
            <div className="flex items-center gap-1 overflow-x-auto pb-1 sm:pb-0 text-xs">
              <button
                onClick={() => setStatusFilter("ALL")}
                className={`px-3 py-1.5 rounded-lg font-semibold transition whitespace-nowrap ${
                  statusFilter === "ALL"
                    ? "bg-accent-primary text-white shadow-xs"
                    : "text-ink-secondary hover:text-ink-primary hover:bg-surface-raised"
                }`}
              >
                {t("common.all", "Tất cả")} ({samples.length})
              </button>
              <button
                onClick={() => setStatusFilter("SENT_TO_LAB")}
                className={`px-3 py-1.5 rounded-lg font-semibold transition whitespace-nowrap ${
                  statusFilter === "SENT_TO_LAB"
                    ? "bg-blue-600 text-white shadow-xs"
                    : "text-ink-secondary hover:text-ink-primary hover:bg-surface-raised"
                }`}
              >
                {t("compliance.labSample.filterSent", "Đang gửi")} ({stats.sent})
              </button>
              <button
                onClick={() => setStatusFilter("ANALYZING")}
                className={`px-3 py-1.5 rounded-lg font-semibold transition whitespace-nowrap ${
                  statusFilter === "ANALYZING"
                    ? "bg-amber-600 text-white shadow-xs"
                    : "text-ink-secondary hover:text-ink-primary hover:bg-surface-raised"
                }`}
              >
                {t("compliance.labSample.filterAnalyzing", "Đang thử nghiệm")} ({stats.analyzing})
              </button>
              <button
                onClick={() => setStatusFilter("RESULT_RECEIVED")}
                className={`px-3 py-1.5 rounded-lg font-semibold transition whitespace-nowrap ${
                  statusFilter === "RESULT_RECEIVED"
                    ? "bg-accent-success text-white shadow-xs"
                    : "text-ink-secondary hover:text-ink-primary hover:bg-surface-raised"
                }`}
              >
                {t("compliance.labSample.filterCompleted", "Đã có kết quả")} ({stats.completed})
              </button>
            </div>

            <div className="relative w-full sm:w-72">
              <MagnifyingGlass
                size={14}
                className="absolute left-3 top-1/2 -translate-y-1/2 text-ink-tertiary"
              />
              <input
                type="text"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                placeholder={t(
                  "compliance.labSample.searchPlaceholder",
                  "Tìm mã mẫu, số niêm phong, lô hàng..."
                )}
                className="w-full pl-9 pr-3 py-1.5 text-xs rounded-lg border border-ink-tertiary/20 bg-surface-base text-ink-primary placeholder:text-ink-tertiary focus:outline-none focus:ring-1 focus:ring-accent-primary"
              />
            </div>
          </div>

          {/* Samples Table */}
          <div className="bg-surface-base border border-ink-tertiary/20 rounded-xl shadow-xs overflow-hidden">
            <div className="p-4 border-b border-ink-tertiary/15 flex items-center justify-between">
              <h2 className="text-sm font-bold font-serif text-ink-primary flex items-center gap-2">
                <Flask size={16} className="text-accent-primary" />
                <span>
                  {t(
                    "compliance.labSample.chainLogTitle",
                    "Sổ Theo Dõi Mẫu & Chuỗi Bàn Giao Niêm Phong ({{count}})",
                    { count: filteredSamples.length }
                  )}
                </span>
              </h2>
            </div>

            <div className="overflow-x-auto">
              <table className="w-full text-left text-xs border-collapse">
                <thead>
                  <tr className="bg-surface-raised border-b border-ink-tertiary/15 text-ink-tertiary uppercase tracking-wider font-semibold">
                    <th className="py-3 px-4">{t("compliance.labSample.thCodeSeal", "Mã Mẫu / Niêm Phong")}</th>
                    <th className="py-3 px-4">{t("compliance.labSample.thLot", "Lô Hàng Lấy Mẫu")}</th>
                    <th className="py-3 px-4">{t("compliance.labSample.thLab", "Đơn Vị Kiểm Nghiệm")}</th>
                    <th className="py-3 px-4">{t("compliance.labSample.thMarket", "Thị Trường Đích")}</th>
                    <th className="py-3 px-4">{t("compliance.labSample.thDates", "Ngày Gửi / Dự Kiến")}</th>
                    <th className="py-3 px-4">{t("compliance.labSample.thStatus", "Trạng Thái")}</th>
                    <th className="py-3 px-4 text-right">{t("compliance.labSample.thActions", "Thao Tác")}</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-ink-tertiary/10">
                  {filteredSamples.map((sample) => (
                    <tr
                      key={sample.id}
                      className="hover:bg-surface-raised/50 transition cursor-pointer"
                      onClick={() => setSelectedSample(sample)}
                    >
                      <td className="py-3 px-4">
                        <div className="space-y-0.5">
                          <span className="font-mono font-bold text-ink-primary block">
                            {sample.sampleCode}
                          </span>
                          <span className="font-mono text-[10px] text-accent-primary flex items-center gap-1">
                            <SealCheck size={12} weight="bold" />
                            {sample.sealCode}
                          </span>
                        </div>
                      </td>

                      <td className="py-3 px-4">
                        <div className="space-y-0.5">
                          <span className="font-mono font-semibold text-accent-info block">
                            {sample.lotId}
                          </span>
                          <span className="text-[11px] text-ink-secondary">
                            {sample.productType} ({sample.sampleWeightKg} kg)
                          </span>
                        </div>
                      </td>

                      <td className="py-3 px-4">
                        <span
                          className="text-ink-primary font-medium block max-w-xs truncate"
                          title={sample.labName}
                        >
                          {sample.labName}
                        </span>
                        <span className="text-[10px] text-ink-tertiary font-mono">
                          ISO/IEC 17025
                        </span>
                      </td>

                      <td className="py-3 px-4">
                        <span className="text-ink-secondary text-[11px]">
                          {sample.targetMarket}
                        </span>
                      </td>

                      <td className="py-3 px-4 font-mono text-[11px] text-ink-secondary">
                        <div>{t("compliance.labSample.sentPrefix", "Gửi:")} {sample.samplingDate}</div>
                        <div className="text-ink-tertiary text-[10px]">
                          {t("compliance.labSample.estPrefix", "Dự kiến:")} {sample.estimatedResultDate}
                        </div>
                      </td>

                      <td className="py-3 px-4">
                        {renderStatusBadge(sample.status)}
                      </td>

                      <td
                        className="py-3 px-4 text-right"
                        onClick={(e) => e.stopPropagation()}
                      >
                        <div className="flex items-center justify-end gap-1.5">
                          <button
                            onClick={() => handlePrintHandover(sample)}
                            className="p-1.5 rounded-lg border border-ink-tertiary/20 hover:bg-surface-raised text-ink-secondary hover:text-ink-primary transition"
                            title={t("compliance.labSample.printHandoverTooltip", "In biên bản bàn giao mẫu kèm mã vạch")}
                          >
                            <Printer size={14} />
                          </button>

                          {sample.status === "RESULT_RECEIVED" ? (
                            <Link
                              to="/compliance/lab-results"
                              className="px-2.5 py-1 rounded-lg bg-accent-primary hover:bg-accent-primary-dim text-white font-semibold text-[11px] transition inline-flex items-center gap-1 shadow-xs"
                            >
                              <span>{t("compliance.labSample.viewCoa", "Xem CoA")}</span>
                              <ArrowRight size={11} weight="bold" />
                            </Link>
                          ) : (
                            <button
                              onClick={() => setSelectedSample(sample)}
                              className="px-2.5 py-1 rounded-lg border border-ink-tertiary/20 hover:bg-surface-raised text-ink-secondary hover:text-ink-primary text-[11px] font-semibold transition"
                            >
                              {t("common.details", "Chi tiết")}
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}

                  {filteredSamples.length === 0 && (
                    <tr>
                      <td
                        colSpan={7}
                        className="py-10 text-center text-xs text-ink-secondary"
                      >
                        {t(
                          "compliance.labSample.noSamplesFound",
                          "Không tìm thấy mẫu xét nghiệm nào phù hợp với điều kiện lọc."
                        )}
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      )}

      {/* Sample Detail Drawer / Modal */}
      {selectedSample && (
        <div className="fixed inset-0 bg-black/50 backdrop-blur-xs flex items-center justify-center p-4 z-50">
          <div className="bg-surface-base rounded-2xl shadow-xl border border-ink-tertiary/20 max-w-xl w-full overflow-hidden animate-in fade-in zoom-in-95 duration-150">
            <div className="p-4 border-b border-ink-tertiary/15 flex items-center justify-between bg-surface-raised">
              <div className="flex items-center gap-2">
                <SealCheck size={20} className="text-accent-primary" />
                <h3 className="font-bold font-serif text-sm text-ink-primary">
                  {t(
                    "compliance.labSample.detailModalTitle",
                    "Chi Tiết Mẫu Niêm Phong {{code}}",
                    { code: selectedSample.sampleCode }
                  )}
                </h3>
              </div>
              <button
                onClick={() => setSelectedSample(null)}
                className="text-ink-tertiary hover:text-ink-primary p-1 rounded-lg hover:bg-surface-base transition"
              >
                <X size={16} />
              </button>
            </div>

            <div className="p-5 space-y-4 text-xs">
              {/* Barcode & Seal badge card */}
              <div className="p-4 rounded-xl bg-surface-raised border border-ink-tertiary/15 flex items-center justify-between">
                <div>
                  <span className="text-[10px] text-ink-tertiary uppercase font-mono block">
                    {t("compliance.labSample.leadSealLabel", "Mã niêm phong chì (Lead Seal)")}
                  </span>
                  <span className="text-sm font-bold font-mono text-accent-primary">
                    {selectedSample.sealCode}
                  </span>
                  <span className="text-[11px] text-ink-secondary block mt-0.5">
                    {t("compliance.labSample.thLot", "Lô hàng")}: {selectedSample.lotId} (
                    {selectedSample.sampleWeightKg} kg)
                  </span>
                </div>

                <div className="w-16 h-16 bg-white p-1 rounded-lg border border-ink-tertiary/20 flex items-center justify-center">
                  <QrCode size={48} className="text-ink-primary" />
                </div>
              </div>

              {/* Lab & Custody Info */}
              <div className="space-y-2">
                <div className="flex justify-between py-1 border-b border-ink-tertiary/10">
                  <span className="text-ink-secondary">
                    {t("compliance.labSample.receivingLab", "Phòng kiểm nghiệm tiếp nhận:")}
                  </span>
                  <span className="font-semibold text-ink-primary text-right max-w-xs">
                    {selectedSample.labName}
                  </span>
                </div>
                <div className="flex justify-between py-1 border-b border-ink-tertiary/10">
                  <span className="text-ink-secondary">{t("compliance.labSample.labAddress", "Địa chỉ gửi mẫu:")}</span>
                  <span className="text-ink-secondary text-right max-w-xs">
                    {selectedSample.labAddress}
                  </span>
                </div>
                <div className="flex justify-between py-1 border-b border-ink-tertiary/10">
                  <span className="text-ink-secondary">
                    {t("compliance.labSample.targetMarketLabel", "Thị trường xuất khẩu mục tiêu:")}
                  </span>
                  <span className="font-semibold text-ink-primary">
                    {selectedSample.targetMarket}
                  </span>
                </div>
                <div className="flex justify-between py-1 border-b border-ink-tertiary/10">
                  <span className="text-ink-secondary">
                    {t("compliance.labSample.samplingStandard", "Phương pháp lấy mẫu:")}
                  </span>
                  <span className="font-mono text-ink-primary">
                    {selectedSample.samplingStandard}
                  </span>
                </div>
                <div className="flex justify-between py-1 border-b border-ink-tertiary/10">
                  <span className="text-ink-secondary">
                    {t("compliance.labSample.samplerNameLabel", "Cán bộ thực hiện lấy mẫu:")}
                  </span>
                  <span className="font-medium text-ink-primary">
                    {selectedSample.samplerName}
                  </span>
                </div>
              </div>

              {/* Requested Substances */}
              <div>
                <span className="font-semibold text-ink-primary block mb-1.5">
                  {t("compliance.labSample.requestedSubstances", "Chỉ tiêu yêu cầu định lượng:")}
                </span>
                <div className="flex flex-wrap gap-1.5">
                  {selectedSample.requestedSubstances.map((s, idx) => (
                    <span
                      key={idx}
                      className="px-2 py-0.5 rounded-md bg-surface-raised border border-ink-tertiary/15 text-[11px] font-mono text-ink-secondary"
                    >
                      {s}
                    </span>
                  ))}
                </div>
              </div>

              {/* Notes */}
              {selectedSample.notes && (
                <div className="p-3 rounded-lg bg-surface-raised/50 border border-ink-tertiary/10 text-ink-secondary">
                  <strong className="text-ink-primary">{t("common.notes", "Ghi chú:")}</strong>{" "}
                  {selectedSample.notes}
                </div>
              )}
            </div>

            <div className="p-4 border-t border-ink-tertiary/15 flex items-center justify-between bg-surface-raised">
              <button
                onClick={() => handlePrintHandover(selectedSample)}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg border border-ink-tertiary/20 hover:bg-surface-base text-xs font-semibold text-ink-primary transition"
              >
                <Printer size={14} />
                <span>{t("compliance.labSample.btnPrintHandover", "In biên bản bàn giao")}</span>
              </button>

              <div className="flex items-center gap-2">
                <button
                  onClick={() => setSelectedSample(null)}
                  className="px-3.5 py-1.5 rounded-lg border border-ink-tertiary/20 text-ink-secondary hover:text-ink-primary text-xs font-semibold transition"
                >
                  {t("common.close", "Đóng")}
                </button>

                {selectedSample.status === "RESULT_RECEIVED" && (
                  <Link
                    to="/compliance/lab-results"
                    className="inline-flex items-center gap-1.5 px-3.5 py-1.5 rounded-lg bg-accent-primary hover:bg-accent-primary-dim text-white text-xs font-bold transition shadow-xs"
                  >
                    <span>{t("compliance.labSample.viewCoaResult", "Xem kết quả CoA")}</span>
                    <ArrowRight size={13} weight="bold" />
                  </Link>
                )}
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
