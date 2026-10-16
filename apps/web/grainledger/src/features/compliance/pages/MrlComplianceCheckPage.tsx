import { catalogEndpoints } from "@/api/catalogs";
import {
  ArrowRight,
  CheckCircle,
  ClockCounterClockwise,
  Flask,
  MagnifyingGlass,
  Plus,
  ShieldCheck,
  ShieldWarning,
  TestTube,
  WarningOctagon,
  X,
  XCircle,
} from "@phosphor-icons/react";
import React, { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useTranslation } from "react-i18next";

interface LabSample {
  id: string;
  sampleCode: string;
  lotId: string;
  labName: string;
  samplingDate: string;
  status: "PENDING_LAB" | "COMPLIANT" | "EXCEEDED_MRL" | "SUPERSEDED";
  substancesTested: Array<{
    substanceName: string;
    measuredValueMgKg: number;
    marketLimitMgKg: number;
    safeMarginMgKg: number;
    status: "PASS" | "WARN" | "FAIL";
  }>;
  supersededBy?: string;
  certificateRef?: string;
}

export const MrlComplianceCheckPage: React.FC = () => {
  const { t } = useTranslation();
  const [samples, setSamples] = useState<LabSample[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // New Sample modal
  const [showAddModal, setShowAddModal] = useState(false);
  const [sampleForm, setSampleForm] = useState({
    sampleCode: "",
    lotId: "",
    labName: "Viện Kiểm Nghiệm Vệ Sinh An Toàn Thực Phẩm (NIFC)",
    samplingDate: new Date().toISOString().split("T")[0],
    certificateRef: "",
  });

  // Selected sample for detail
  const [selectedSample, setSelectedSample] = useState<LabSample | null>(null);

  useEffect(() => {
    fetchComplianceData();
  }, []);

  const fetchComplianceData = async () => {
    try {
      setLoading(true);
      setError(null);

      // In dev/mock, set up realistic lab sample inspections
      setSamples([
        {
          id: "SMP-2026-001",
          sampleCode: "NIFC-2026-XN-8821",
          lotId: "LOT-TG-2026-001",
          labName: "Viện Kiểm Nghiệm An Toàn Thực Phẩm Quốc Gia (NIFC)",
          samplingDate: "2026-10-14",
          status: "COMPLIANT",
          certificateRef: "COA-EU-2026-0982",
          substancesTested: [
            {
              substanceName: "Tricyclazole (Thuốc đạo ôn)",
              measuredValueMgKg: 0.006,
              marketLimitMgKg: 0.01,
              safeMarginMgKg: 0.008,
              status: "PASS",
            },
            {
              substanceName: "Hexaconazole (Thuốc trừ nấm)",
              measuredValueMgKg: 0.004,
              marketLimitMgKg: 0.02,
              safeMarginMgKg: 0.016,
              status: "PASS",
            },
            {
              substanceName: "Chlorpyrifos-ethyl",
              measuredValueMgKg: 0.0,
              marketLimitMgKg: 0.01,
              safeMarginMgKg: 0.008,
              status: "PASS",
            },
          ],
        },
        {
          id: "SMP-2026-002",
          sampleCode: "EUROFINS-2026-4412",
          lotId: "LOT-TG-2026-002",
          labName: "Eurofins Sắc Ký Hải Đăng",
          samplingDate: "2026-10-15",
          status: "EXCEEDED_MRL",
          certificateRef: "COA-REJ-2026-1102",
          substancesTested: [
            {
              substanceName: "Tricyclazole (Thuốc đạo ôn)",
              measuredValueMgKg: 0.012,
              marketLimitMgKg: 0.01,
              safeMarginMgKg: 0.008,
              status: "FAIL",
            },
            {
              substanceName: "Isoprothiolane",
              measuredValueMgKg: 0.05,
              marketLimitMgKg: 0.1,
              safeMarginMgKg: 0.08,
              status: "PASS",
            },
          ],
        },
      ]);
    } catch (err: unknown) {
      console.error(err);
      setError(
        (err as Error).message ||
          t("compliance.mrlCheck.errLoad", "Không thể tải dữ liệu kiểm định phòng lab.")
      );
    } finally {
      setLoading(false);
    }
  };

  const handleCreateSample = (e: React.FormEvent) => {
    e.preventDefault();
    if (!sampleForm.sampleCode || !sampleForm.lotId) {
      alert(t("compliance.mrlCheck.alertFillRequired", "Vui lòng nhập đầy đủ mã mẫu và mã lô."));
      return;
    }

    const newSample: LabSample = {
      id: `SMP-2026-00${samples.length + 1}`,
      sampleCode: sampleForm.sampleCode,
      lotId: sampleForm.lotId,
      labName: sampleForm.labName,
      samplingDate: sampleForm.samplingDate,
      certificateRef: sampleForm.certificateRef,
      status: "PENDING_LAB",
      substancesTested: [],
    };

    setSamples([newSample, ...samples]);
    setShowAddModal(false);
    setSampleForm({
      sampleCode: "",
      lotId: "",
      labName: "Viện Kiểm Nghiệm Vệ Sinh An Toàn Thực Phẩm (NIFC)",
      samplingDate: new Date().toISOString().split("T")[0],
      certificateRef: "",
    });
    alert(t("compliance.mrlCheck.alertCreateSuccess", "Đã gửi đăng ký mẫu xét nghiệm thành công."));
  };

  const getStatusBadge = (status: LabSample["status"]) => {
    switch (status) {
      case "COMPLIANT":
        return (
          <span className="px-2 py-0.5 rounded-full text-xs font-semibold bg-emerald-50 text-emerald-700 border border-emerald-200 inline-flex items-center gap-1">
            <CheckCircle size={14} weight="fill" />
            {t("compliance.mrlCheck.statusCompliant", "Đạt chuẩn xuất khẩu")}
          </span>
        );
      case "EXCEEDED_MRL":
        return (
          <span className="px-2 py-0.5 rounded-full text-xs font-semibold bg-rose-50 text-rose-700 border border-rose-200 inline-flex items-center gap-1">
            <WarningOctagon size={14} weight="fill" />
            {t("compliance.mrlCheck.statusExceeded", "Vượt ngưỡng MRL (HOLD)")}
          </span>
        );
      case "PENDING_LAB":
        return (
          <span className="px-2 py-0.5 rounded-full text-xs font-semibold bg-amber-50 text-amber-700 border border-amber-200">
            {t("compliance.mrlCheck.statusPending", "Đang chờ kết quả lab")}
          </span>
        );
      case "SUPERSEDED":
        return (
          <span className="px-2 py-0.5 rounded-full text-xs font-semibold bg-slate-100 text-slate-600 border border-slate-200">
            {t("compliance.mrlCheck.statusSuperseded", "Đã bị thay thế")}
          </span>
        );
    }
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-col md:flex-row md:items-center md:justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold font-serif text-slate-900 mt-1">
            {t(
              "compliance.mrlCheck.pageTitle",
              "Kết Quả Thử Nghiệm MRL & Thẩm Định CoA (Lab Test Results & MRL Compliance)"
            )}
          </h1>
          <p className="text-sm text-slate-700 mt-0.5">
            {t(
              "compliance.mrlCheck.pageSubtitle",
              "Thẩm định phiếu kết quả thử nghiệm (Certificate of Analysis) từ phòng lab ISO/IEC 17025, đối chiếu nồng độ hoạt chất với ngưỡng MRL xuất khẩu và kích hoạt lệnh HOLD nếu phát hiện dư lượng vượt ngưỡng."
            )}
          </p>
        </div>

        <div className="flex items-center gap-2 shrink-0">
          <Link
            to="/compliance/lab-samples/new"
            className="px-3.5 py-2 bg-emerald-600 hover:bg-emerald-700 text-white rounded-lg text-xs font-bold flex items-center gap-1.5 shadow-sm transition"
          >
            <Plus size={15} weight="bold" />
            <span>{t("compliance.mrlCheck.btnRegisterSample", "Đăng ký mẫu mới")}</span>
          </Link>
        </div>
      </div>

      {error && (
        <div className="p-4 bg-rose-50 border border-rose-200 rounded-lg text-rose-800 text-sm flex items-start gap-2">
          <X className="shrink-0 mt-0.5" size={18} />
          <div>
            <strong>{t("common.error", "Lỗi")}:</strong> {error}
          </div>
        </div>
      )}

      {/* Safety Margin Banner */}
      <div className="p-4 bg-emerald-50/70 border border-emerald-200 rounded-xl text-emerald-950 text-xs flex items-center justify-between">
        <div className="flex items-center gap-3">
          <ShieldCheck
            size={24}
            weight="fill"
            className="text-emerald-600 shrink-0"
          />
          <div>
            <div className="font-semibold text-emerald-900">
              {t("compliance.mrlCheck.safetyBannerTitle", "Chính sách Biên an toàn 20% đang kích hoạt (BR-07)")}
            </div>
            <p className="text-emerald-800 mt-0.5">
              {t(
                "compliance.mrlCheck.safetyBannerDesc",
                "Hệ thống tự động trừ 20% vào giới hạn MRL chính thức của thị trường đích để đảm bảo an toàn tuyệt đối khi kiểm định tại cảng nhập khẩu."
              )}
            </p>
          </div>
        </div>
        <div className="font-mono font-bold text-sm bg-white px-3 py-1.5 rounded-lg border border-emerald-300 text-emerald-700 shrink-0 shadow-xs">
          Safety Buffer: -20%
        </div>
      </div>

      {/* Samples Table */}
      <div className="bg-white rounded-xl border border-slate-200 shadow-sm overflow-hidden">
        <div className="p-4 border-b border-slate-200 flex items-center justify-between">
          <h2 className="font-semibold text-slate-800 text-sm flex items-center gap-2">
            <TestTube size={16} />
            {t("compliance.mrlCheck.tableTitle", "Danh sách Mẫu Kiểm định Dư lượng ({{count}})", {
              count: samples.length,
            })}
          </h2>
          <button
            onClick={fetchComplianceData}
            className="text-xs text-slate-600 hover:text-emerald-700 flex items-center gap-1"
          >
            <ClockCounterClockwise size={14} />
            {t("common.refresh", "Làm mới")}
          </button>
        </div>

        {loading ? (
          <div className="p-12 text-center text-slate-700 text-sm">
            {t("compliance.mrlCheck.loadingSamples", "Đang tải danh sách mẫu...")}
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs border-collapse">
              <thead>
                <tr className="bg-slate-50 border-b border-slate-200 text-slate-700 font-semibold uppercase tracking-wider">
                  <th className="py-3 px-4">{t("compliance.mrlCheck.thSampleCode", "Mã kiểm định")}</th>
                  <th className="py-3 px-4">{t("compliance.mrlCheck.thLotId", "Lô hàng mục tiêu")}</th>
                  <th className="py-3 px-4">{t("compliance.mrlCheck.thLabName", "Đơn vị phân tích (Lab)")}</th>
                  <th className="py-3 px-4">{t("compliance.mrlCheck.thSamplingDate", "Ngày lấy mẫu")}</th>
                  <th className="py-3 px-4">{t("compliance.mrlCheck.thCoaRef", "Số chứng thư COA")}</th>
                  <th className="py-3 px-4">{t("compliance.mrlCheck.thSafetyRating", "Đánh giá an toàn")}</th>
                  <th className="py-3 px-4 text-right">{t("compliance.mrlCheck.thDetails", "Chi tiết")}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {samples.map((s) => (
                  <tr key={s.id} className="hover:bg-slate-50/80 transition">
                    <td className="py-3 px-4 font-mono font-medium text-slate-900">
                      {s.sampleCode}
                    </td>
                    <td className="py-3 px-4 font-mono text-emerald-700 font-semibold">
                      {s.lotId}
                    </td>
                    <td className="py-3 px-4 text-slate-800">{s.labName}</td>
                    <td className="py-3 px-4 text-slate-600">
                      {s.samplingDate}
                    </td>
                    <td className="py-3 px-4 font-mono text-slate-700">
                      {s.certificateRef || t("compliance.mrlCheck.notIssuedYet", "Chưa cấp")}
                    </td>
                    <td className="py-3 px-4">{getStatusBadge(s.status)}</td>
                    <td className="py-3 px-4 text-right">
                      <button
                        onClick={() => setSelectedSample(s)}
                        className="px-2 py-1 bg-slate-100 hover:bg-emerald-50 hover:text-emerald-700 text-slate-700 rounded font-medium transition"
                      >
                        {t("compliance.mrlCheck.btnViewMetrics", "Xem chỉ số")}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Detail Modal (SC-24) */}
      {selectedSample && (
        <div className="fixed inset-0 bg-slate-900/50 backdrop-blur-xs flex items-center justify-center p-4 z-50">
          <div className="bg-white rounded-xl shadow-xl border border-slate-200 max-w-2xl w-full overflow-hidden animate-in fade-in zoom-in-95 duration-150">
            <div className="p-4 border-b border-slate-200 flex items-center justify-between bg-slate-50">
              <div className="flex items-center gap-2">
                <Flask size={20} className="text-emerald-600" />
                <h3 className="font-bold text-slate-900">
                  {t("compliance.mrlCheck.modalDetailTitle", "Chi tiết Kết quả Phân tích Phòng Lab")}
                </h3>
              </div>
              <button
                onClick={() => setSelectedSample(null)}
                className="text-slate-700 hover:text-slate-600 p-1 rounded-lg hover:bg-slate-200 transition"
              >
                <X size={18} />
              </button>
            </div>

            <div className="p-6 space-y-4 text-sm">
              <div className="grid grid-cols-2 gap-4 bg-slate-50 p-4 rounded-lg border border-slate-200 text-xs">
                <div>
                  <span className="text-slate-700 uppercase font-semibold">
                    {t("compliance.mrlCheck.modalSampleCode", "Mã mẫu:")}
                  </span>
                  <p className="font-mono font-bold text-slate-900 text-sm mt-0.5">
                    {selectedSample.sampleCode}
                  </p>
                </div>
                <div>
                  <span className="text-slate-700 uppercase font-semibold">
                    {t("compliance.mrlCheck.modalLotId", "Lô hàng:")}
                  </span>
                  <p className="font-mono font-bold text-emerald-800 text-sm mt-0.5">
                    {selectedSample.lotId}
                  </p>
                </div>
                <div className="col-span-2">
                  <span className="text-slate-700 uppercase font-semibold">
                    {t("compliance.mrlCheck.modalLabName", "Phòng phân tích:")}
                  </span>
                  <p className="font-medium text-slate-900 mt-0.5">
                    {selectedSample.labName}
                  </p>
                </div>
              </div>

              <div>
                <h4 className="font-semibold text-slate-800 text-xs uppercase tracking-wider mb-2">
                  {t("compliance.mrlCheck.substanceTableTitle", "Bảng định lượng hoạt chất (mg/kg ppm)")}
                </h4>
                {selectedSample.substancesTested.length === 0 ? (
                  <div className="p-6 text-center text-slate-700 text-xs bg-slate-50 rounded-lg border border-slate-200">
                    {t("compliance.mrlCheck.noSubstancesYet", "Chưa có số liệu nạp từ phòng xét nghiệm.")}
                  </div>
                ) : (
                  <table className="w-full text-left text-xs border border-slate-200 rounded-lg overflow-hidden">
                    <thead>
                      <tr className="bg-slate-100 text-slate-700 font-semibold">
                        <th className="p-2.5">{t("compliance.mrlCheck.thSubstance", "Hoạt chất")}</th>
                        <th className="p-2.5 text-right">{t("compliance.mrlCheck.thMeasured", "Thực đo (mg/kg)")}</th>
                        <th className="p-2.5 text-right">{t("compliance.mrlCheck.thLimit", "Giới hạn MRL")}</th>
                        <th className="p-2.5 text-right">
                          {t("compliance.mrlCheck.thSafetyMargin", "Biên an toàn (-20%)")}
                        </th>
                        <th className="p-2.5 text-center">{t("compliance.mrlCheck.thVerdict", "Kết luận")}</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-200">
                      {selectedSample.substancesTested.map((sub, idx) => (
                        <tr key={idx} className="hover:bg-slate-50">
                          <td className="p-2.5 font-medium text-slate-900">
                            {sub.substanceName}
                          </td>
                          <td className="p-2.5 text-right font-mono font-bold">
                            {sub.measuredValueMgKg.toFixed(4)}
                          </td>
                          <td className="p-2.5 text-right font-mono text-slate-600">
                            {sub.marketLimitMgKg.toFixed(4)}
                          </td>
                          <td className="p-2.5 text-right font-mono text-emerald-700 font-medium">
                            {sub.safeMarginMgKg.toFixed(4)}
                          </td>
                          <td className="p-2.5 text-center">
                            {sub.status === "PASS" ? (
                              <span className="px-2 py-0.5 rounded text-[11px] font-semibold bg-emerald-50 text-emerald-700 border border-emerald-200">
                                PASS
                              </span>
                            ) : (
                              <span className="px-2 py-0.5 rounded text-[11px] font-semibold bg-rose-50 text-rose-700 border border-rose-200">
                                FAIL (MRL EXCEEDED)
                              </span>
                            )}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
              </div>

              {selectedSample.status === "EXCEEDED_MRL" && (
                <div className="p-3 bg-rose-50 border border-rose-200 rounded-lg text-xs text-rose-900 flex items-center gap-2">
                  <ShieldWarning size={18} className="text-rose-600 shrink-0" />
                  <span>
                    {t(
                      "compliance.mrlCheck.holdAlertDesc",
                      "Lô hàng này đã vượt ngưỡng an toàn MRL! Quy tắc BR-15 yêu cầu phát lệnh HOLD khẩn cấp để ngăn chặn phối trộn hoặc xếp container xuất khẩu."
                    )}
                  </span>
                </div>
              )}
            </div>

            <div className="p-4 border-t border-slate-200 flex justify-end bg-slate-50">
              <button
                onClick={() => setSelectedSample(null)}
                className="px-4 py-2 bg-slate-200 hover:bg-slate-300 text-slate-800 rounded-lg text-xs font-medium transition"
              >
                {t("common.close", "Đóng")}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Add Sample Modal (SC-23) */}
      {showAddModal && (
        <div className="fixed inset-0 bg-slate-900/50 backdrop-blur-xs flex items-center justify-center p-4 z-50">
          <div className="bg-white rounded-xl shadow-xl border border-slate-200 max-w-lg w-full overflow-hidden animate-in fade-in zoom-in-95 duration-150">
            <div className="p-4 border-b border-slate-200 flex items-center justify-between bg-slate-50">
              <div className="flex items-center gap-2">
                <Flask size={20} className="text-emerald-600" />
                <h3 className="font-bold text-slate-900">
                  {t("compliance.mrlCheck.addModalTitle", "Đăng ký Mẫu Xét nghiệm Phòng Lab")}
                </h3>
              </div>
              <button
                onClick={() => setShowAddModal(false)}
                className="text-slate-700 hover:text-slate-600 p-1 rounded-lg hover:bg-slate-200 transition"
              >
                <X size={18} />
              </button>
            </div>

            <form
              onSubmit={handleCreateSample}
              className="p-6 space-y-4 text-sm"
            >
              <div>
                <label className="block text-xs font-semibold text-slate-700 uppercase mb-1">
                  {t("compliance.mrlCheck.sealCodeLabel", "Mã niêm phong mẫu (Sample Seal Code) *")}
                </label>
                <input
                  type="text"
                  required
                  placeholder="Ví dụ: SMP-2026-NIFC-01"
                  value={sampleForm.sampleCode}
                  onChange={(e) =>
                    setSampleForm({ ...sampleForm, sampleCode: e.target.value })
                  }
                  className="w-full px-3 py-2 border border-slate-300 rounded-lg text-sm font-mono focus:ring-2 focus:ring-emerald-500 focus:outline-none"
                />
              </div>

              <div>
                <label className="block text-xs font-semibold text-slate-700 uppercase mb-1">
                  {t("compliance.mrlCheck.lotIdLabel", "Mã Lô hàng đại diện (Lot ID) *")}
                </label>
                <input
                  type="text"
                  required
                  placeholder="Ví dụ: LOT-TG-2026-001"
                  value={sampleForm.lotId}
                  onChange={(e) =>
                    setSampleForm({ ...sampleForm, lotId: e.target.value })
                  }
                  className="w-full px-3 py-2 border border-slate-300 rounded-lg text-sm font-mono focus:ring-2 focus:ring-emerald-500 focus:outline-none"
                />
              </div>

              <div>
                <label className="block text-xs font-semibold text-slate-700 uppercase mb-1">
                  {t("compliance.mrlCheck.isoLabLabel", "Đơn vị phòng kiểm nghiệm ISO 17025 *")}
                </label>
                <select
                  value={sampleForm.labName}
                  onChange={(e) =>
                    setSampleForm({ ...sampleForm, labName: e.target.value })
                  }
                  className="w-full px-3 py-2 border border-slate-300 rounded-lg text-sm focus:ring-2 focus:ring-emerald-500 focus:outline-none bg-white"
                >
                  <option value="Viện Kiểm Nghiệm Vệ Sinh An Toàn Thực Phẩm (NIFC)">
                    Viện Kiểm Nghiệm An Toàn Thực Phẩm Quốc Gia (NIFC)
                  </option>
                  <option value="Eurofins Sắc Ký Hải Đăng">
                    Eurofins Sắc Ký Hải Đăng
                  </option>
                  <option value="SGS Vietnam Co., Ltd">
                    SGS Vietnam Co., Ltd
                  </option>
                  <option value="Trung tâm Kỹ thuật Tiêu chuẩn Đo lường Chất lượng 3 (QUATEST 3)">
                    QUATEST 3
                  </option>
                </select>
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-xs font-semibold text-slate-700 uppercase mb-1">
                    {t("compliance.mrlCheck.samplingDateLabel", "Ngày lấy mẫu *")}
                  </label>
                  <input
                    type="date"
                    required
                    value={sampleForm.samplingDate}
                    onChange={(e) =>
                      setSampleForm({
                        ...sampleForm,
                        samplingDate: e.target.value,
                      })
                    }
                    className="w-full px-3 py-2 border border-slate-300 rounded-lg text-sm focus:ring-2 focus:ring-emerald-500 focus:outline-none"
                  />
                </div>
                <div>
                  <label className="block text-xs font-semibold text-slate-700 uppercase mb-1">
                    {t("compliance.mrlCheck.coaRefLabel", "Số phiếu tiếp nhận/COA")}
                  </label>
                  <input
                    type="text"
                    placeholder="Mã tham chiếu..."
                    value={sampleForm.certificateRef}
                    onChange={(e) =>
                      setSampleForm({
                        ...sampleForm,
                        certificateRef: e.target.value,
                      })
                    }
                    className="w-full px-3 py-2 border border-slate-300 rounded-lg text-sm focus:ring-2 focus:ring-emerald-500 focus:outline-none"
                  />
                </div>
              </div>

              <div className="flex items-center justify-end gap-2 pt-2 border-t border-slate-100">
                <button
                  type="button"
                  onClick={() => setShowAddModal(false)}
                  className="px-4 py-2 border border-slate-300 rounded-lg text-xs font-medium text-slate-700 hover:bg-slate-50 transition"
                >
                  {t("common.cancel", "Hủy bỏ")}
                </button>
                <button
                  type="submit"
                  className="px-4 py-2 bg-emerald-600 hover:bg-emerald-700 text-white rounded-lg text-xs font-medium transition"
                >
                  {t("compliance.mrlCheck.btnSubmitSample", "Gửi đăng ký mẫu")}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
