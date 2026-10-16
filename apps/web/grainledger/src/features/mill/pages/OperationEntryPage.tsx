/**
 * @screen SC-15
 * @feature F08, F09 (Custody Ledger)
 * @roles MILL_OPERATOR, MILL_MANAGER (View)
 * @description Record drying, milling, physical blending or merge, bagging, or transfer of an unchanged lot between locations.
 * Validates mass balance conservation (BR-01) and double-spending prevention (BR-02).
 */
import { useAuthStore } from "@/app/store/authStore";
import { useDraftStore } from "@/app/store/draftStore";
import { HoldBanner } from "@/components/HoldBanner/HoldBanner";
import { LedgerRejectionCard } from "@/components/LedgerRejectionCard/LedgerRejectionCard";
import { MassValue } from "@/components/MassValue/MassValue";
import { StatusBadge } from "@/components/StatusBadge/StatusBadge";
import {
  checkMassBalance,
  toDecimal,
  type Decimal,
} from "@/shared/utils/decimalMass";
import {
  ArrowRight,
  ArrowsClockwise,
  CheckCircle,
  FloppyDisk,
  Info,
  Scales,
  Trash,
} from "@phosphor-icons/react";
import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

export const OperationEntryPage: React.FC = () => {
  const { t } = useTranslation();
  const { setHasUnsavedDrafts } = useAuthStore();
  const { saveDraft, getDraft, clearDraft } = useDraftStore();

  const DRAFT_KEY = "SC15_OPERATION_ENTRY";

  // Process type options
  const [processType, setProcessType] = useState<
    "DRYING" | "MILLING" | "BLENDING" | "BAGGING"
  >("MILLING");
  const [selectedInputLot, setSelectedInputLot] = useState("LOT-2026-S1");
  const [inputMassKg, setInputMassKg] = useState<string>("88000");

  // Outputs (for milling: head rice, broken, bran, husk, and declared loss)
  const [headRiceKg, setHeadRiceKg] = useState<string>("58000");
  const [brokenRiceKg, setBrokenRiceKg] = useState<string>("8000");
  const [branKg, setBranKg] = useState<string>("6000");
  const [huskKg, setHuskKg] = useState<string>("15700");
  const [declaredLossKg, setDeclaredLossKg] = useState<string>("300");
  const [lossReason, setLossReason] = useState<string>(
    "Hao hụt ẩm và bụi phát tán tự nhiên qua hệ thống quạt hút",
  );

  // Tolerance is 0.5% for milling (BR-01)
  const tolerancePercent = 0.5;

  const [submissionStatus, setSubmissionStatus] = useState<
    "IDLE" | "BALANCED_COMMITTED" | "REJECTED"
  >("IDLE");

  // Check if chosen lot is under HOLD
  const isInputLotOnHold = selectedInputLot === "LOT-2026-HOLD-TEST";

  // Calculate live mass balance
  const totalOut = toDecimal(headRiceKg)
    .plus(toDecimal(brokenRiceKg))
    .plus(toDecimal(branKg))
    .plus(toDecimal(huskKg));

  const balanceResult = checkMassBalance(
    toDecimal(inputMassKg),
    totalOut,
    toDecimal(declaredLossKg),
    tolerancePercent,
  );

  // Autosave draft management
  useEffect(() => {
    const draft = getDraft<{
      processType: "DRYING" | "MILLING" | "BLENDING" | "BAGGING";
      selectedInputLot: string;
      inputMassKg: string;
      headRiceKg: string;
      brokenRiceKg: string;
      branKg: string;
      huskKg: string;
      declaredLossKg: string;
      lossReason: string;
    }>(DRAFT_KEY);

    if (draft) {
      setProcessType(draft.processType);
      setSelectedInputLot(draft.selectedInputLot);
      setInputMassKg(draft.inputMassKg);
      setHeadRiceKg(draft.headRiceKg);
      setBrokenRiceKg(draft.brokenRiceKg);
      setBranKg(draft.branKg);
      setHuskKg(draft.huskKg);
      setDeclaredLossKg(draft.declaredLossKg);
      setLossReason(draft.lossReason);
    }
  }, []);

  const handleFieldChange = () => {
    setHasUnsavedDrafts(true);
    saveDraft(DRAFT_KEY, {
      processType,
      selectedInputLot,
      inputMassKg,
      headRiceKg,
      brokenRiceKg,
      branKg,
      huskKg,
      declaredLossKg,
      lossReason,
    });
  };

  const handleClearDraft = () => {
    clearDraft(DRAFT_KEY);
    setHasUnsavedDrafts(false);
    setInputMassKg("88000");
    setHeadRiceKg("58000");
    setBrokenRiceKg("8000");
    setBranKg("6000");
    setHuskKg("15700");
    setDeclaredLossKg("300");
    setSubmissionStatus("IDLE");
  };

  const handleCommitOperation = (e: React.FormEvent) => {
    e.preventDefault();

    if (isInputLotOnHold) {
      setSubmissionStatus("REJECTED");
      return;
    }

    if (!balanceResult.isBalanced) {
      setSubmissionStatus("REJECTED");
      return;
    }

    setSubmissionStatus("BALANCED_COMMITTED");
    clearDraft(DRAFT_KEY);
    setHasUnsavedDrafts(false);
  };

  return (
    <div className="max-w-4xl mx-auto space-y-6">
      {/* Page Header */}
      <div className="border-b border-ink-tertiary/15 pb-4 flex flex-col sm:flex-row sm:items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-serif font-bold text-ink-primary">
            {t("mill.operations.pageTitle", "Ghi nhận bước chế biến vật lý (Operation Entry)")}
          </h1>
          <p className="text-xs text-ink-secondary mt-0.5">
            {t(
              "mill.operations.pageSubtitle",
              "Mọi mẻ chế biến được đối chiếu bảo toàn khối lượng và ghi nhận vào sổ cái chỉ-ghi-thêm."
            )}
          </p>
        </div>

        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={handleClearDraft}
            className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md text-xs font-medium text-ink-tertiary hover:text-ink-primary hover:bg-surface-raised transition-colors"
          >
            <Trash size={14} />
            <span>{t("mill.operations.clearDraft", "Xoá nháp")}</span>
          </button>
        </div>
      </div>

      {/* HOLD Alert if testing lot on HOLD */}
      {isInputLotOnHold && (
        <HoldBanner
          targetType="Lô hàng"
          targetId="LOT-2026-HOLD-TEST"
          reason={t(
            "mill.operations.holdNotice",
            "Lô đang bị giữ theo lệnh phong toả kiểm dịch BR-15. Nghi ngờ nhiễm Tricyclazole vượt ngưỡng MRL EU."
          )}
        />
      )}

      {/* Rejection Card if unbalanced */}
      {submissionStatus === "REJECTED" && (
        <LedgerRejectionCard
          msgCode="MSG01"
          varianceKg={balanceResult.varianceKg}
          allowedKg={balanceResult.allowedKg}
          inputMassKg={inputMassKg}
          outputMassKg={totalOut}
          lossKg={declaredLossKg}
          suggestedFix={t(
            "mill.operations.rejectionSuggestedFix",
            "Kiểm tra lại số cân bàn cân phân đoạn hoặc đối soát lại khối lượng trấu và tấm trước khi nộp lại."
          )}
          onRetry={() => setSubmissionStatus("IDLE")}
        />
      )}

      {/* Success Committed State */}
      {submissionStatus === "BALANCED_COMMITTED" && (
        <div className="p-5 rounded-lg bg-functional-balanced-bg border-2 border-functional-balanced-border text-accent-primary space-y-3">
          <div className="flex items-center gap-2.5 font-bold text-base">
            <CheckCircle size={24} weight="fill" />
            <span>
              {t(
                "mill.operations.successTitle",
                "Giao dịch chế biến đã được ghi vào sổ cái thành công!"
              )}
            </span>
          </div>
          <p className="text-xs text-ink-secondary leading-relaxed">
            {t("mill.operations.immutableCode", "Mã bút toán bất biến: ")}
            <code className="font-mono font-bold text-ink-primary">
              EVT-CONV-2026-00492
            </code>
            . {t("mill.operations.inputDeductionPrefix", "Số dư lô đầu vào ")}
            <span className="font-mono font-bold">{selectedInputLot}</span>{" "}
            {t("mill.operations.inputDeducted", "đã khấu trừ ")}
            <MassValue value={inputMassKg} decimals={3} />{" "}
            {t(
              "mill.operations.outputInherited",
              "và 4 lô phân đoạn mới đã được sinh với đầy đủ hệ số kế thừa nguồn gốc."
            )}
          </p>
          <button
            type="button"
            onClick={() => {
              setSubmissionStatus("IDLE");
              handleClearDraft();
            }}
            className="px-3.5 py-1.5 rounded-md bg-accent-primary text-white text-xs font-semibold hover:bg-accent-primary-dim transition-colors"
          >
            {t("mill.operations.recordNextBatch", "Ghi nhận mẻ tiếp theo")}
          </button>
        </div>
      )}

      {/* Conversion Form */}
      <form onSubmit={handleCommitOperation} className="space-y-6">
        <div className="bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 rounded-lg p-6 space-y-6 shadow-xs">
          {/* Process Type Selector */}
          <div>
            <label className="block text-xs font-semibold text-ink-secondary mb-2">
              {t("mill.operations.selectProcessType", "Chọn công đoạn chế biến:")}
            </label>
            <div className="grid grid-cols-2 sm:grid-cols-4 gap-2">
              {[
                {
                  type: "MILLING",
                  label: t("mill.operations.typeMilling", "Xay xát gạo (Milling)"),
                  tolerance: "0,5%",
                },
                {
                  type: "DRYING",
                  label: t("mill.operations.typeDrying", "Sấy khô lúa tươi"),
                  tolerance: "0,5%",
                },
                {
                  type: "BLENDING",
                  label: t("mill.operations.typeBlending", "Phối trộn cơ học"),
                  tolerance: "0,2%",
                },
                {
                  type: "BAGGING",
                  label: t("mill.operations.typeBagging", "Đóng bao & Xuất kho"),
                  tolerance: "0,1%",
                },
              ].map((p) => (
                <button
                  key={p.type}
                  type="button"
                  onClick={() => {
                    setProcessType(p.type as any);
                    handleFieldChange();
                  }}
                  className={`p-3 rounded-md text-left border transition-all ${
                    processType === p.type
                      ? "bg-surface-base border-accent-primary text-accent-primary font-semibold shadow-xs"
                      : "bg-surface-base/60 border-ink-tertiary/20 text-ink-secondary hover:text-ink-primary"
                  }`}
                >
                  <span className="block text-xs">{p.label}</span>
                  <span className="text-[10px] text-ink-tertiary font-mono">
                    {t("mill.operations.tolerancePrefix", "Dung sai:")} {p.tolerance}
                  </span>
                </button>
              ))}
            </div>
          </div>

          {/* Section 1: Input Lot */}
          <div className="p-4 rounded-md bg-surface-base border border-ink-tertiary/20 space-y-4">
            <h3 className="text-xs font-bold uppercase tracking-wider text-ink-tertiary flex items-center justify-between">
              <span>{t("mill.operations.inputSectionTitle", "1. Lô nguyên liệu đầu vào (Input Lot)")}</span>
              <span className="font-mono text-accent-primary">
                {t("mill.operations.appliedTolerance", "Dung sai áp dụng:")} {tolerancePercent}%
              </span>
            </h3>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label className="block text-xs font-semibold text-ink-secondary mb-1">
                  {t("mill.operations.selectInputLot", "Chọn Lô trong Silo")}
                </label>
                <select
                  value={selectedInputLot}
                  onChange={(e) => {
                    setSelectedInputLot(e.target.value);
                    handleFieldChange();
                  }}
                  className="w-full px-3 py-2 text-xs rounded-md bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/30 focus:border-accent-primary focus:outline-none font-mono"
                >
                  <option value="LOT-2026-S1">
                    LOT-2026-S1 ({t("mill.operations.demoLotAvailable", "Thóc khô · Khả dụng 88.000 kg · Silo 1")})
                  </option>
                  <option value="LOT-2026-HOLD-TEST">
                    LOT-2026-HOLD-TEST ({t("mill.operations.demoLotHold", "Lô đang phong toả HOLD")})
                  </option>
                </select>
              </div>

              <div>
                <label className="block text-xs font-semibold text-ink-secondary mb-1">
                  {t("mill.operations.inputMassLabel", "Khối lượng đưa vào chế biến *")}
                </label>
                <div className="relative">
                  <input
                    type="number"
                    value={inputMassKg}
                    onChange={(e) => {
                      setInputMassKg(e.target.value);
                      handleFieldChange();
                    }}
                    className="w-full px-3 py-2 text-sm text-right rounded-md bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/30 focus:border-accent-primary focus:outline-none font-mono font-semibold"
                  />
                  <span className="absolute right-3 top-2.5 text-xs text-ink-tertiary pointer-events-none">
                    kg
                  </span>
                </div>
              </div>
            </div>
          </div>

          {/* Section 2: Output Fractions */}
          <div className="p-4 rounded-md bg-surface-base border border-ink-tertiary/20 space-y-4">
            <h3 className="text-xs font-bold uppercase tracking-wider text-ink-tertiary">
              {t("mill.operations.outputSectionTitle", "2. Các phân đoạn đầu ra & Hao hụt (Output Fractions & Loss)")}
            </h3>

            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
              <div>
                <label className="block text-xs font-medium text-ink-secondary mb-1">
                  {t("mill.operations.headRice", "Gạo nguyên (Head Rice)")}
                </label>
                <input
                  type="number"
                  value={headRiceKg}
                  onChange={(e) => {
                    setHeadRiceKg(e.target.value);
                    handleFieldChange();
                  }}
                  className="w-full px-3 py-1.5 text-xs text-right rounded-md bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/30 font-mono"
                />
              </div>

              <div>
                <label className="block text-xs font-medium text-ink-secondary mb-1">
                  {t("mill.operations.brokenRice", "Gạo tấm (Broken Rice)")}
                </label>
                <input
                  type="number"
                  value={brokenRiceKg}
                  onChange={(e) => {
                    setBrokenRiceKg(e.target.value);
                    handleFieldChange();
                  }}
                  className="w-full px-3 py-1.5 text-xs text-right rounded-md bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/30 font-mono"
                />
              </div>

              <div>
                <label className="block text-xs font-medium text-ink-secondary mb-1">
                  {t("mill.operations.bran", "Cám gạo (Bran)")}
                </label>
                <input
                  type="number"
                  value={branKg}
                  onChange={(e) => {
                    setBranKg(e.target.value);
                    handleFieldChange();
                  }}
                  className="w-full px-3 py-1.5 text-xs text-right rounded-md bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/30 font-mono"
                />
              </div>

              <div>
                <label className="block text-xs font-medium text-ink-secondary mb-1">
                  {t("mill.operations.husk", "Vỏ trấu (Husk)")}
                </label>
                <input
                  type="number"
                  value={huskKg}
                  onChange={(e) => {
                    setHuskKg(e.target.value);
                    handleFieldChange();
                  }}
                  className="w-full px-3 py-1.5 text-xs text-right rounded-md bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/30 font-mono"
                />
              </div>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-3 gap-3 pt-2">
              <div>
                <label className="block text-xs font-medium text-ink-secondary mb-1">
                  {t("mill.operations.declaredLoss", "Hao hụt khai báo *")}
                </label>
                <input
                  type="number"
                  value={declaredLossKg}
                  onChange={(e) => {
                    setDeclaredLossKg(e.target.value);
                    handleFieldChange();
                  }}
                  className="w-full px-3 py-1.5 text-xs text-right rounded-md bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/30 font-mono"
                />
              </div>

              <div className="sm:col-span-2">
                <label className="block text-xs font-medium text-ink-secondary mb-1">
                  {t("mill.operations.lossReason", "Lý do hao hụt vật lý")}
                </label>
                <input
                  type="text"
                  value={lossReason}
                  onChange={(e) => {
                    setLossReason(e.target.value);
                    handleFieldChange();
                  }}
                  className="w-full px-3 py-1.5 text-xs rounded-md bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/30"
                />
              </div>
            </div>
          </div>

          {/* Section 3: Live Conservation Check Widget */}
          <div
            className={`p-4 rounded-md border flex items-center justify-between flex-wrap gap-4 ${
              balanceResult.isBalanced
                ? "bg-functional-balanced-bg border-functional-balanced-border text-accent-primary"
                : "bg-functional-rejected-bg border-functional-rejected-border text-accent-danger"
            }`}
          >
            <div className="flex items-center gap-3">
              <Scales size={24} weight="bold" />
              <div>
                <h4 className="font-bold text-sm">
                  {balanceResult.isBalanced
                    ? t("mill.operations.balancePassed", "Đạt điều kiện cân bằng khối lượng (BR-01)")
                    : t("mill.operations.balanceFailed", "Không đạt cân bằng — Bút toán sẽ bị từ chối")}
                </h4>
                <div className="flex items-center gap-3 text-xs mt-0.5 opacity-90">
                  <span>
                    {t("mill.operations.inMass", "Vào:")}{" "}
                    <MassValue value={inputMassKg} decimals={3} />
                  </span>
                  <span>=</span>
                  <span>
                    {t("mill.operations.outPlusLoss", "Ra + Hao:")}{" "}
                    <MassValue
                      value={totalOut.plus(toDecimal(declaredLossKg))}
                      decimals={3}
                    />
                  </span>
                  <span>
                    ({t("mill.operations.variance", "Lệch:")}{" "}
                    <MassValue value={balanceResult.varianceKg} decimals={3} />)
                  </span>
                </div>
              </div>
            </div>

            <div className="text-right text-xs">
              <span className="block text-ink-tertiary">
                {t("mill.operations.allowedToleranceLabel", "Dung sai cho phép:")}
              </span>
              <MassValue
                value={balanceResult.allowedKg}
                decimals={3}
                className="font-bold text-ink-primary"
              />
            </div>
          </div>

          {/* Submit Actions */}
          <div className="flex items-center justify-between pt-2">
            <span className="text-xs text-ink-tertiary flex items-center gap-1.5">
              <FloppyDisk size={14} />
              {t("mill.operations.autosaveNote", "Bản nháp tự động lưu trên trình duyệt")}
            </span>

            <button
              type="submit"
              className="inline-flex items-center gap-2 px-6 py-2.5 rounded-md bg-accent-primary text-white font-semibold text-xs hover:bg-accent-primary-dim transition-all shadow-xs"
            >
              <span>{t("mill.operations.submitButton", "Kiểm tra & Ghi vào sổ cái")}</span>
              <ArrowRight size={15} weight="bold" />
            </button>
          </div>
        </div>
      </form>
    </div>
  );
};
