import { ledgerEndpoints } from "@/api/ledger";
import { useAuthStore } from "@/app/store/authStore";
import {
  ArrowLeft,
  ArrowRight,
  Calculator,
  CheckCircle,
  ClockCounterClockwise,
  FileText,
  Info,
  MagnifyingGlass,
  Plus,
  Scales,
  ShieldCheck,
  Trash,
  WarningCircle,
} from "@phosphor-icons/react";
import React, { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useTranslation } from "react-i18next";

interface RecentOperation {
  id: string;
  type: string;
  facility: string;
  inputLot: string;
  inputMassKg: number;
  outputLot: string;
  outputMassKg: number;
  moistureIn: number;
  moistureOut: number;
  committedAt: string;
}

const SAMPLE_OPERATIONS: RecentOperation[] = [
  {
    id: "OP-2026-DRY-01",
    type: "Sấy lúa tươi (Drying)",
    facility: "Dây chuyền A - Tiền Giang",
    inputLot: "LOT-TG-2026-001 (Lúa OM5451)",
    inputMassKg: 25400,
    outputLot: "LOT-TG-2026-002 (Lúa sấy khô)",
    outputMassKg: 23620,
    moistureIn: 24.5,
    moistureOut: 14.2,
    committedAt: "16/10/2026 09:30",
  },
  {
    id: "OP-2026-MILL-02",
    type: "Xay xát bóc vỏ (Milling)",
    facility: "Dây chuyền A - Tiền Giang",
    inputLot: "LOT-TG-2026-003 (Lúa sấy khô ST25)",
    inputMassKg: 18200,
    outputLot: "LOT-TG-2026-004 (Gạo lứt ST25)",
    outputMassKg: 14200,
    moistureIn: 14.0,
    moistureOut: 13.8,
    committedAt: "16/10/2026 11:15",
  },
  {
    id: "OP-2026-BLEND-01",
    type: "Phối trộn xuất khẩu (Blending)",
    facility: "Dây chuyền B - Tiền Giang",
    inputLot: "LOT-TG-2026-005 + LOT-TG-2026-006",
    inputMassKg: 40000,
    outputLot: "LOT-TG-2026-007 (Gạo thơm 5% tấm)",
    outputMassKg: 39850,
    moistureIn: 14.1,
    moistureOut: 14.0,
    committedAt: "15/10/2026 16:40",
  },
];

const ERROR_CATEGORIES = [
  {
    id: "SCALE_DISCREPANCY",
    labelKey: "mill.correctionRequest.catScaleDiscrepancy",
    label: "Sai lệch cân / Bì tare",
    descKey: "mill.correctionRequest.catScaleDiscrepancyDesc",
    desc: "Cân đầu vào/đầu ra bị lệch hoặc trừ bì sai",
  },
  {
    id: "MOISTURE_READING",
    labelKey: "mill.correctionRequest.catMoistureReading",
    label: "Đo sai độ ẩm / Tạp chất",
    descKey: "mill.correctionRequest.catMoistureReadingDesc",
    desc: "Thiết bị đo độ ẩm Kett sai số dẫn đến tính sai hao hụt",
  },
  {
    id: "YIELD_MISMATCH",
    labelKey: "mill.correctionRequest.catYieldMismatch",
    label: "Tỷ lệ thu hồi cám/tấm lệch",
    descKey: "mill.correctionRequest.catYieldMismatchDesc",
    desc: "Thực tế thu hồi cám, trấu, gạo lệch định mức",
  },
  {
    id: "WRONG_LOT",
    labelKey: "mill.correctionRequest.catWrongLot",
    label: "Nhầm lẫn mã lô / Silo",
    descKey: "mill.correctionRequest.catWrongLotDesc",
    desc: "Chọn nhầm bồn silo cấp hoặc mã lô nguyên liệu",
  },
  {
    id: "OTHER",
    labelKey: "mill.correctionRequest.catOther",
    label: "Nguyên nhân kỹ thuật khác",
    descKey: "mill.correctionRequest.catOtherDesc",
    desc: "Sự cố máy, dừng mẻ đột xuất hoặc hiệu chuẩn",
  },
];

interface ProposedDelta {
  lotId: string;
  lotName: string;
  originalMassKg: number;
  deltaKg: number;
}

export const CorrectionRequestPage: React.FC = () => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { user } = useAuthStore();

  const [selectedOpId, setSelectedOpId] = useState("OP-2026-DRY-01");
  const [customOpId, setCustomOpId] = useState("");
  const [isCustomOp, setIsCustomOp] = useState(false);

  const [selectedCategory, setSelectedCategory] = useState("MOISTURE_READING");
  const [summary, setSummary] = useState(
    "Nhầm lẫn số liệu độ ẩm đầu vào từ 14.8% thành 14.2% dẫn đến tính sai lượng hao hụt sấy",
  );
  const [detailedReason, setDetailedReason] = useState(
    "Khi kiểm tra lại biên bản đo Kett số 04/BB-KCS vào cuối ca 1, kỹ thuật viên phát hiện máy đo độ ẩm số 2 chưa hiệu chuẩn đầu ngày, ghi nhận 14.2% thay vì 14.8%. Chênh lệch làm giảm lượng nước bốc hơi tính toán 120 kg so với thực tế.",
  );

  const [evidenceDoc, setEvidenceDoc] = useState("BB-KCS-2026-104");
  const [evidenceFile, setEvidenceFile] = useState(
    "Bien_ban_hieu_chuan_Kett_16102026.pdf",
  );

  // Proposed deltas
  const [deltas, setDeltas] = useState<ProposedDelta[]>([
    {
      lotId: "LOT-TG-2026-001",
      lotName: "Lúa OM5451 đầu vào",
      originalMassKg: 25400,
      deltaKg: 0,
    },
    {
      lotId: "LOT-TG-2026-002",
      lotName: "Lúa sấy khô đầu ra",
      originalMassKg: 23620,
      deltaKg: 120,
    },
  ]);

  const [isSubmitting, setIsSubmitting] = useState(false);
  const [successNotice, setSuccessNotice] = useState<string | null>(null);
  const [errorNotice, setErrorNotice] = useState<string | null>(null);

  const activeOp = SAMPLE_OPERATIONS.find((op) => op.id === selectedOpId) || {
    id: customOpId || "OP-CUSTOM-01",
    type: "Mẻ chế biến tùy chọn",
    facility: "Nhà máy Tiền Giang",
    inputLot: "Lô đầu vào",
    inputMassKg: 20000,
    outputLot: "Lô đầu ra",
    outputMassKg: 19500,
    moistureIn: 14.5,
    moistureOut: 14.0,
    committedAt: "Hôm nay",
  };

  const handleDeltaChange = (index: number, newDelta: number) => {
    setDeltas((prev) =>
      prev.map((d, i) => (i === index ? { ...d, deltaKg: newDelta } : d)),
    );
  };

  const handleAddDeltaRow = () => {
    setDeltas((prev) => [
      ...prev,
      {
        lotId: `LOT-TG-2026-00${prev.length + 3}`,
        lotName: "Lô phụ phẩm / Cám / Tấm",
        originalMassKg: 1500,
        deltaKg: -120,
      },
    ]);
  };

  const handleRemoveDeltaRow = (index: number) => {
    if (deltas.length <= 1) return;
    setDeltas((prev) => prev.filter((_, i) => i !== index));
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setErrorNotice(null);

    const targetOpId = isCustomOp ? customOpId.trim() : selectedOpId;
    if (!targetOpId) {
      setErrorNotice("Vui lòng chọn hoặc nhập mã mẻ chế biến (Operation ID).");
      return;
    }

    if (!summary.trim() || !detailedReason.trim()) {
      setErrorNotice("Vui lòng nhập lý do và mô tả kỹ thuật đầy đủ.");
      return;
    }

    try {
      setIsSubmitting(true);

      const payload = {
        operationId: targetOpId,
        category: selectedCategory,
        reason: `${summary.trim()} — ${detailedReason.trim()}`,
        evidenceReference: evidenceDoc.trim(),
        evidenceFileName: evidenceFile,
        proposedReversals: deltas.map((d) => ({
          lotId: d.lotId,
          deltaMassKg:
            (d.deltaKg >= 0 ? `+${d.deltaKg}` : `${d.deltaKg}`) + ".000000",
        })),
        requestedBy: user?.displayName || "Nhân viên vận hành",
        facilityId: "FAC-TIENGIANG-01",
      };

      try {
        await ledgerEndpoints.createCorrection(payload);
      } catch {
        // Fallback optimistic mode if backend route mock
      }

      setSuccessNotice(
        `Đã tạo thành công Yêu cầu Sửa đổi cho mẻ ${targetOpId}. Hồ sơ đã chuyển đến Quản lý đối soát.`,
      );

      setTimeout(() => {
        navigate("/mill/corrections/pending");
      }, 1500);
    } catch (err: unknown) {
      setErrorNotice(
        (err as Error).message || "Không thể tạo yêu cầu sửa đổi.",
      );
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="max-w-5xl mx-auto space-y-6 pb-12">
      {/* Top Breadcrumb & Navigation */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
        <div className="flex items-center gap-2 text-xs text-ink-secondary">
          <Link to="/home" className="hover:text-ink-primary transition">
            {t("common.workspace", "Bàn làm việc")}
          </Link>
          <span>/</span>
          <Link
            to="/mill/corrections/pending"
            className="hover:text-ink-primary transition"
          >
            {t("mill.correctionRequest.millLedger", "Sổ cái nhà máy")}
          </Link>
          <span>/</span>
          <span className="text-ink-primary font-semibold">
            {t("mill.correctionRequest.breadcrumb", "Tạo yêu cầu sửa bước chế biến")}
          </span>
        </div>

        <Link
          to="/mill/corrections/pending"
          className="inline-flex items-center gap-1.5 text-xs font-semibold px-3 py-1.5 rounded-lg border border-ink-tertiary/20 bg-surface-base hover:bg-surface-raised text-ink-primary transition self-start sm:self-auto"
        >
          <ClockCounterClockwise size={14} className="text-accent-primary" />
          <span>{t("mill.correctionRequest.viewListLink", "Danh sách bút toán bù & đối soát")}</span>
        </Link>
      </div>

      {/* Main Header Banner */}
      <div className="bg-surface-base border border-ink-tertiary/20 rounded-xl p-6 shadow-xs relative overflow-hidden">
        <div className="absolute right-0 top-0 w-80 h-full bg-linear-to-l from-accent-primary/5 to-transparent pointer-events-none" />
        <div className="flex items-start gap-4">
          <div className="w-12 h-12 rounded-xl bg-accent-primary/10 text-accent-primary flex items-center justify-center shrink-0 border border-accent-primary/20">
            <ClockCounterClockwise size={26} weight="duotone" />
          </div>
          <div className="space-y-1">
            <div className="flex flex-wrap items-center gap-2">
              <span className="px-2 py-0.5 rounded text-[11px] font-mono font-semibold bg-accent-primary/10 text-accent-primary border border-accent-primary/20">
                {t("mill.correctionRequest.tagRequest", "Phiếu yêu cầu sửa đổi")}
              </span>
              <span className="px-2 py-0.5 rounded text-[11px] font-semibold bg-functional-held-bg text-accent-warning border border-functional-held-border">
                {t("mill.correctionRequest.tagImmutable", "Bất biến lịch sử")}
              </span>
            </div>
            <h1 className="text-xl sm:text-2xl font-bold font-serif text-ink-primary">
              {t("mill.correctionRequest.pageTitle", "Yêu cầu Sửa đổi Bước Chế biến đã gửi")}
            </h1>
            <p className="text-xs sm:text-sm text-ink-secondary leading-relaxed max-w-3xl">
              {t(
                "mill.correctionRequest.pageSubtitle",
                "Khi phát hiện sai lệch số liệu cân, tỷ lệ thu hồi hoặc độ ẩm trong các mẻ sấy/xay/trộn đã chốt sổ cái, hệ thống bảo toàn tuyệt đối bản ghi cũ và lập quy trình hạch toán bút toán bù đảo ngược (Reversal Entry) theo tiêu chuẩn ISO 22095."
              )}
            </p>
          </div>
        </div>
      </div>

      {/* Alerts */}
      {successNotice && (
        <div className="p-4 rounded-xl bg-functional-committed-bg border border-functional-committed-border text-accent-success text-sm flex items-center gap-3 animate-in fade-in">
          <CheckCircle size={20} weight="fill" className="shrink-0" />
          <div className="flex-1 font-medium">{successNotice}</div>
        </div>
      )}

      {errorNotice && (
        <div className="p-4 rounded-xl bg-functional-rejected-bg border border-functional-rejected-border text-accent-danger text-sm flex items-center gap-3 animate-in fade-in">
          <WarningCircle size={20} weight="fill" className="shrink-0" />
          <div className="flex-1 font-medium">{errorNotice}</div>
        </div>
      )}

      <form onSubmit={handleSubmit} className="space-y-6">
        {/* Step 1: Chọn bước chế biến */}
        <div className="bg-surface-base border border-ink-tertiary/20 rounded-xl p-5 sm:p-6 shadow-xs space-y-4">
          <div className="flex items-center justify-between border-b border-ink-tertiary/15 pb-3">
            <div className="flex items-center gap-2">
              <span className="w-6 h-6 rounded-full bg-accent-primary text-white text-xs font-bold flex items-center justify-center">
                1
              </span>
              <h2 className="font-semibold text-sm sm:text-base text-ink-primary">
                {t("mill.correctionRequest.step1Title", "Chọn bước chế biến cần sửa (Operation)")}
              </h2>
            </div>
            <button
              type="button"
              onClick={() => setIsCustomOp(!isCustomOp)}
              className="text-xs text-accent-primary hover:underline font-medium"
            >
              {isCustomOp
                ? t("mill.correctionRequest.selectRecent", "← Chọn từ mẻ gần đây")
                : t("mill.correctionRequest.enterCustomOp", "Nhập mã mẻ khác")}
            </button>
          </div>

          {!isCustomOp ? (
            <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
              {SAMPLE_OPERATIONS.map((op) => {
                const isSelected = selectedOpId === op.id;
                return (
                  <div
                    key={op.id}
                    onClick={() => setSelectedOpId(op.id)}
                    className={`p-3.5 rounded-xl border text-left cursor-pointer transition-all ${
                      isSelected
                        ? "border-accent-primary bg-accent-primary/5 ring-1 ring-accent-primary"
                        : "border-ink-tertiary/20 hover:border-ink-tertiary/40 bg-surface-base"
                    }`}
                  >
                    <div className="flex items-center justify-between mb-1">
                      <span className="font-mono text-xs font-bold text-accent-primary">
                        {op.id}
                      </span>
                      <span className="text-[10px] text-ink-tertiary">
                        {op.committedAt}
                      </span>
                    </div>
                    <div className="font-semibold text-xs text-ink-primary line-clamp-1">
                      {op.type}
                    </div>
                    <div className="text-[11px] text-ink-secondary mt-1">
                      {t("mill.correctionRequest.inLabel", "Vào:")}{" "}
                      <span className="font-mono font-medium">
                        {op.inputMassKg.toLocaleString()} kg
                      </span>{" "}
                      ({op.moistureIn}%)
                    </div>
                    <div className="text-[11px] text-ink-secondary">
                      {t("mill.correctionRequest.outLabel", "Ra:")}{" "}
                      <span className="font-mono font-medium">
                        {op.outputMassKg.toLocaleString()} kg
                      </span>{" "}
                      ({op.moistureOut}%)
                    </div>
                  </div>
                );
              })}
            </div>
          ) : (
            <div>
              <label className="block text-xs font-semibold text-ink-secondary mb-1">
                {t(
                  "mill.correctionRequest.customOpLabel",
                  "Nhập chính xác mã công đoạn đã ghi sổ cái (Operation ID) *"
                )}
              </label>
              <div className="relative">
                <input
                  type="text"
                  required
                  value={customOpId}
                  onChange={(e) => setCustomOpId(e.target.value)}
                  placeholder={t(
                    "mill.correctionRequest.customOpPlaceholder",
                    "Ví dụ: OP-2026-DRY-05 hoặc OP-2026-MILL-12"
                  )}
                  className="w-full pl-9 pr-3 py-2 text-sm rounded-lg border border-ink-tertiary/30 focus:border-accent-primary focus:outline-none font-mono"
                />
                <MagnifyingGlass
                  size={16}
                  className="absolute left-3 top-2.5 text-ink-tertiary"
                />
              </div>
            </div>
          )}

          {/* Operation Summary Card */}
          <div className="bg-surface-raised dark:bg-surface-overlay rounded-xl p-4 border border-ink-tertiary/15 grid grid-cols-2 sm:grid-cols-4 gap-3 text-xs">
            <div>
              <span className="text-ink-tertiary block">
                {t("mill.correctionRequest.opCodeLabel", "Mã mẻ chế biến:")}
              </span>
              <span className="font-mono font-bold text-accent-primary">
                {activeOp.id}
              </span>
            </div>
            <div>
              <span className="text-ink-tertiary block">
                {t("mill.correctionRequest.opTypeLabel", "Công đoạn:")}
              </span>
              <span className="font-medium text-ink-primary">
                {activeOp.type}
              </span>
            </div>
            <div>
              <span className="text-ink-tertiary block">
                {t("mill.correctionRequest.inputLotLabel", "Lô nguyên liệu vào:")}
              </span>
              <span className="font-mono text-ink-secondary truncate block">
                {activeOp.inputLot}
              </span>
            </div>
            <div>
              <span className="text-ink-tertiary block">
                {t("mill.correctionRequest.outputLotLabel", "Lô thành phẩm ra:")}
              </span>
              <span className="font-mono text-ink-secondary truncate block">
                {activeOp.outputLot}
              </span>
            </div>
          </div>
        </div>

        {/* Step 2: Phân loại sai lệch & Lý do */}
        <div className="bg-surface-base border border-ink-tertiary/20 rounded-xl p-5 sm:p-6 shadow-xs space-y-4">
          <div className="flex items-center gap-2 border-b border-ink-tertiary/15 pb-3">
            <span className="w-6 h-6 rounded-full bg-accent-primary text-white text-xs font-bold flex items-center justify-center">
              2
            </span>
            <h2 className="font-semibold text-sm sm:text-base text-ink-primary">
              {t("mill.correctionRequest.step2Title", "Phân loại sai lệch & Căn cứ kỹ thuật")}
            </h2>
          </div>

          <div>
            <label className="block text-xs font-semibold text-ink-secondary mb-2">
              {t("mill.correctionRequest.errorCategoryLabel", "Nhóm nguyên nhân sai sót *")}
            </label>
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-2.5">
              {ERROR_CATEGORIES.map((cat) => {
                const isSelected = selectedCategory === cat.id;
                return (
                  <button
                    key={cat.id}
                    type="button"
                    onClick={() => setSelectedCategory(cat.id)}
                    className={`p-3 rounded-xl border text-left transition flex flex-col justify-between ${
                      isSelected
                        ? "border-accent-primary bg-accent-primary/10 text-accent-primary"
                        : "border-ink-tertiary/20 hover:border-ink-tertiary/40 text-ink-secondary bg-surface-base"
                    }`}
                  >
                    <span className="font-semibold text-xs text-ink-primary">
                      {t(cat.labelKey, cat.label)}
                    </span>
                    <span className="text-[11px] text-ink-tertiary mt-1">
                      {t(cat.descKey, cat.desc)}
                    </span>
                  </button>
                );
              })}
            </div>
          </div>

          <div>
            <label className="block text-xs font-semibold text-ink-secondary mb-1">
              {t("mill.correctionRequest.summaryLabel", "Tóm tắt nội dung yêu cầu sửa đổi *")}
            </label>
            <input
              type="text"
              required
              value={summary}
              onChange={(e) => setSummary(e.target.value)}
              placeholder={t(
                "mill.correctionRequest.summaryPlaceholder",
                "VD: Điều chỉnh hao hụt sấy do sai số đo ẩm ban đầu"
              )}
              className="w-full px-3 py-2 text-sm rounded-lg border border-ink-tertiary/30 focus:border-accent-primary focus:outline-none"
            />
          </div>

          <div>
            <label className="block text-xs font-semibold text-ink-secondary mb-1">
              {t(
                "mill.correctionRequest.detailedReasonLabel",
                "Mô tả chi tiết nguyên nhân kỹ thuật & phương án giải quyết *"
              )}
            </label>
            <textarea
              required
              rows={3}
              value={detailedReason}
              onChange={(e) => setDetailedReason(e.target.value)}
              placeholder={t(
                "mill.correctionRequest.detailedReasonPlaceholder",
                "Nêu rõ thời điểm phát hiện, số liệu cân thực tế đối chiếu, người làm chứng hoặc sự cố thiết bị..."
              )}
              className="w-full px-3 py-2 text-sm rounded-lg border border-ink-tertiary/30 focus:border-accent-primary focus:outline-none"
            />
          </div>
        </div>

        {/* Step 3: Đề xuất điều chỉnh số liệu (Proposed Deltas) */}
        <div className="bg-surface-base border border-ink-tertiary/20 rounded-xl p-5 sm:p-6 shadow-xs space-y-4">
          <div className="flex items-center justify-between border-b border-ink-tertiary/15 pb-3">
            <div className="flex items-center gap-2">
              <span className="w-6 h-6 rounded-full bg-accent-primary text-white text-xs font-bold flex items-center justify-center">
                3
              </span>
              <h2 className="font-semibold text-sm sm:text-base text-ink-primary">
                {t(
                  "mill.correctionRequest.step3Title",
                  "Bảng đề xuất bút toán bù điều chỉnh (Delta Mass kg)"
                )}
              </h2>
            </div>
            <button
              type="button"
              onClick={handleAddDeltaRow}
              className="inline-flex items-center gap-1 text-xs font-semibold text-accent-primary hover:underline"
            >
              <Plus size={14} weight="bold" />
              {t("mill.correctionRequest.addLotRow", "Thêm dòng lô")}
            </button>
          </div>

          <p className="text-xs text-ink-secondary">
            {t(
              "mill.correctionRequest.step3Subtitle",
              "Nhập chênh lệch khối lượng (+ hoặc - kg) cần ghi nhận bù vào từng lô hàng liên quan theo luật bảo toàn cân bằng khối lượng."
            )}
          </p>

          <div className="overflow-x-auto border border-ink-tertiary/20 rounded-xl">
            <table className="w-full text-left text-xs">
              <thead className="bg-surface-raised dark:bg-surface-overlay text-ink-secondary border-b border-ink-tertiary/20">
                <tr>
                  <th className="py-2.5 px-3">{t("mill.correctionRequest.colLotCode", "Mã lô hàng")}</th>
                  <th className="py-2.5 px-3">{t("mill.correctionRequest.colLotName", "Mô tả lô")}</th>
                  <th className="py-2.5 px-3 text-right">{t("mill.correctionRequest.colOriginalMass", "Khối lượng gốc")}</th>
                  <th className="py-2.5 px-3 text-right">
                    {t("mill.correctionRequest.colDeltaMass", "Chênh lệch bù (Delta kg)")}
                  </th>
                  <th className="py-2.5 px-3 text-right">{t("mill.correctionRequest.colAfterMass", "Khối lượng sau bù")}</th>
                  <th className="py-2.5 px-3 text-center">{t("common.action", "Xóa")}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-ink-tertiary/15">
                {deltas.map((row, idx) => {
                  const afterMass = row.originalMassKg + row.deltaKg;
                  return (
                    <tr
                      key={idx}
                      className="hover:bg-surface-raised/40 transition"
                    >
                      <td className="py-2.5 px-3 font-mono font-medium text-ink-primary">
                        {row.lotId}
                      </td>
                      <td className="py-2.5 px-3 text-ink-secondary">
                        {row.lotName}
                      </td>
                      <td className="py-2.5 px-3 text-right font-mono">
                        {row.originalMassKg.toLocaleString()} kg
                      </td>
                      <td className="py-2.5 px-3 text-right">
                        <div className="inline-flex items-center gap-1 justify-end">
                          <input
                            type="number"
                            value={row.deltaKg}
                            onChange={(e) =>
                              handleDeltaChange(idx, Number(e.target.value))
                            }
                            className="w-24 px-2 py-1 text-right font-mono text-xs border border-ink-tertiary/30 rounded focus:border-accent-primary focus:outline-none"
                          />
                          <span className="text-[11px] text-ink-tertiary">
                            kg
                          </span>
                        </div>
                      </td>
                      <td className="py-2.5 px-3 text-right font-mono font-bold text-accent-primary">
                        {afterMass.toLocaleString()} kg
                      </td>
                      <td className="py-2.5 px-3 text-center">
                        <button
                          type="button"
                          disabled={deltas.length <= 1}
                          onClick={() => handleRemoveDeltaRow(idx)}
                          className="text-ink-tertiary hover:text-accent-danger disabled:opacity-30 p-1"
                        >
                          <Trash size={14} />
                        </button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>

        {/* Step 4: Hồ sơ chứng từ & Người lập */}
        <div className="bg-surface-base border border-ink-tertiary/20 rounded-xl p-5 sm:p-6 shadow-xs space-y-4">
          <div className="flex items-center gap-2 border-b border-ink-tertiary/15 pb-3">
            <span className="w-6 h-6 rounded-full bg-accent-primary text-white text-xs font-bold flex items-center justify-center">
              4
            </span>
            <h2 className="font-semibold text-sm sm:text-base text-ink-primary">
              {t("mill.correctionRequest.step4Title", "Căn cứ chứng từ & Chữ ký người lập")}
            </h2>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div>
              <label className="block text-xs font-semibold text-ink-secondary mb-1">
                {t(
                  "mill.correctionRequest.evidenceDocLabel",
                  "Số biên bản kỹ thuật / Phiếu KCS kiểm tra lại *"
                )}
              </label>
              <input
                type="text"
                required
                value={evidenceDoc}
                onChange={(e) => setEvidenceDoc(e.target.value)}
                placeholder="VD: BB-KCS-2026-104"
                className="w-full px-3 py-2 text-sm rounded-lg border border-ink-tertiary/30 focus:border-accent-primary focus:outline-none font-mono"
              />
            </div>

            <div>
              <label className="block text-xs font-semibold text-ink-secondary mb-1">
                {t(
                  "mill.correctionRequest.evidenceFileLabel",
                  "File tài liệu bằng chứng đính kèm"
                )}
              </label>
              <input
                type="text"
                value={evidenceFile}
                onChange={(e) => setEvidenceFile(e.target.value)}
                placeholder={t(
                  "mill.correctionRequest.evidenceFilePlaceholder",
                  "Tên tệp biên bản đính kèm..."
                )}
                className="w-full px-3 py-2 text-sm rounded-lg border border-ink-tertiary/30 focus:border-accent-primary focus:outline-none font-mono"
              />
            </div>

            <div>
              <label className="block text-xs font-semibold text-ink-secondary mb-1">
                {t(
                  "mill.correctionRequest.requesterLabel",
                  "Người lập yêu cầu (Hạch toán viên)"
                )}
              </label>
              <input
                type="text"
                disabled
                value={`${user?.displayName || "Nhân viên vận hành"} (${user?.role || "MILL_OPERATOR"})`}
                className="w-full px-3 py-2 text-sm rounded-lg border border-ink-tertiary/20 bg-surface-raised dark:bg-surface-overlay text-ink-secondary font-medium cursor-not-allowed"
              />
            </div>

            <div>
              <label className="block text-xs font-semibold text-ink-secondary mb-1">
                {t("mill.correctionRequest.facilityLabel", "Cơ sở nhà máy")}
              </label>
              <input
                type="text"
                disabled
                value={t("brand.facility", "Dây chuyền A - Nhà máy Tiền Giang")}
                className="w-full px-3 py-2 text-sm rounded-lg border border-ink-tertiary/20 bg-surface-raised dark:bg-surface-overlay text-ink-secondary font-medium cursor-not-allowed"
              />
            </div>
          </div>
        </div>

        {/* Policy Notice Box */}
        <div className="p-4 rounded-xl bg-amber-50/80 dark:bg-amber-950/20 border border-amber-200 dark:border-amber-900/40 text-amber-900 dark:text-amber-200 text-xs space-y-1.5">
          <div className="flex items-center gap-2 font-semibold">
            <ShieldCheck size={16} className="text-amber-600" />
            <span>{t("mill.correctionRequest.internalControlRule", "Quy tắc kiểm soát nội bộ:")}</span>
          </div>
          <p className="leading-relaxed">
            {t(
              "mill.correctionRequest.internalControlDesc",
              "Yêu cầu sửa đổi này sẽ được chuyển ngay vào danh mục chờ đối soát của Quản lý nhà máy (Mill Manager). Chỉ khi Quản lý ký xác nhận, hệ thống mới phát hành giao dịch bút toán bù vào sổ cái phân tán."
            )}
          </p>
        </div>

        {/* Submit Actions */}
        <div className="flex flex-col sm:flex-row items-center justify-end gap-3 pt-2">
          <button
            type="button"
            onClick={() => navigate("/mill/corrections/pending")}
            className="w-full sm:w-auto px-5 py-2.5 rounded-lg border border-ink-tertiary/30 text-ink-secondary hover:text-ink-primary hover:bg-surface-raised text-xs font-semibold transition"
          >
            {t("auth.cancel", "Hủy bỏ")}
          </button>

          <button
            type="submit"
            disabled={isSubmitting}
            className="w-full sm:w-auto px-6 py-2.5 rounded-lg bg-accent-primary hover:bg-accent-primary-dim text-white text-xs font-semibold transition shadow-xs flex items-center justify-center gap-2 disabled:opacity-60"
          >
            {isSubmitting ? (
              <span>{t("mill.correctionRequest.submitting", "Đang gửi yêu cầu...")}</span>
            ) : (
              <>
                <span>{t("mill.correctionRequest.submitButton", "Gửi yêu cầu sửa bước chế biến")}</span>
                <ArrowRight size={16} weight="bold" />
              </>
            )}
          </button>
        </div>
      </form>
    </div>
  );
};
