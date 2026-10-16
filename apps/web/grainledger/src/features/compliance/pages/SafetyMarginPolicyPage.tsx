import {
  ArrowRight,
  BookOpen,
  Calculator,
  CheckCircle,
  ClockCounterClockwise,
  Flask,
  Info,
  Scales,
  ShieldCheck,
  ShieldWarning,
  Sliders,
  TestTube,
  WarningOctagon,
} from "@phosphor-icons/react";
import React, { useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { useTranslation } from "react-i18next";

interface SubstanceThreshold {
  id: string;
  code: string;
  name: string;
  commercialName: string;
  category: "FUNGICIDE" | "INSECTICIDE" | "HERBICIDE" | "HEAVY_METAL";
  marketLimits: {
    marketCode: string;
    marketName: string;
    mrlMgKg: number;
  }[];
}

const SUBSTANCE_RULES: SubstanceThreshold[] = [
  {
    id: "SUB-01",
    code: "TRICYCLAZOLE",
    name: "Tricyclazole",
    commercialName: "Beam 75WP, Filia 525SE (Trừ đạo ôn)",
    category: "FUNGICIDE",
    marketLimits: [
      {
        marketCode: "EU",
        marketName: "Liên minh Châu Âu (EC 396/2005)",
        mrlMgKg: 0.01,
      },
      { marketCode: "USA", marketName: "Hoa Kỳ (US FDA)", mrlMgKg: 0.05 },
      { marketCode: "JP", marketName: "Nhật Bản (MHLW)", mrlMgKg: 0.02 },
      { marketCode: "CN", marketName: "Trung Quốc (GB 2763)", mrlMgKg: 0.1 },
    ],
  },
  {
    id: "SUB-02",
    code: "HEXACONAZOLE",
    name: "Hexaconazole",
    commercialName: "Anvil 5SC (Trừ nấm khô vằn, lem lép hạt)",
    category: "FUNGICIDE",
    marketLimits: [
      {
        marketCode: "EU",
        marketName: "Liên minh Châu Âu (EC 396/2005)",
        mrlMgKg: 0.02,
      },
      { marketCode: "USA", marketName: "Hoa Kỳ (US FDA)", mrlMgKg: 0.1 },
      { marketCode: "JP", marketName: "Nhật Bản (MHLW)", mrlMgKg: 0.05 },
      { marketCode: "CN", marketName: "Trung Quốc (GB 2763)", mrlMgKg: 0.05 },
    ],
  },
  {
    id: "SUB-03",
    code: "CHLORPYRIFOS",
    name: "Chlorpyrifos-ethyl",
    commercialName: "Hoạt chất cấm lưu hành tại EU",
    category: "INSECTICIDE",
    marketLimits: [
      {
        marketCode: "EU",
        marketName: "Liên minh Châu Âu (EC 396/2005)",
        mrlMgKg: 0.01,
      },
      { marketCode: "USA", marketName: "Hoa Kỳ (US FDA)", mrlMgKg: 0.1 },
      { marketCode: "JP", marketName: "Nhật Bản (MHLW)", mrlMgKg: 0.01 },
      { marketCode: "CN", marketName: "Trung Quốc (GB 2763)", mrlMgKg: 0.1 },
    ],
  },
  {
    id: "SUB-04",
    code: "ISOPROTHIOLANE",
    name: "Isoprothiolane",
    commercialName: "Fuji-One 40EC (Trừ đạo ôn cổ bông)",
    category: "FUNGICIDE",
    marketLimits: [
      {
        marketCode: "EU",
        marketName: "Liên minh Châu Âu (EC 396/2005)",
        mrlMgKg: 0.1,
      },
      { marketCode: "USA", marketName: "Hoa Kỳ (US FDA)", mrlMgKg: 0.5 },
      { marketCode: "JP", marketName: "Nhật Bản (MHLW)", mrlMgKg: 0.2 },
      { marketCode: "CN", marketName: "Trung Quốc (GB 2763)", mrlMgKg: 0.5 },
    ],
  },
];

export const SafetyMarginPolicyPage: React.FC = () => {
  const { t } = useTranslation();
  // Safety margin buffer percentage (Rule BR-07: default 20%)
  const [safetyBufferPercent, setSafetyBufferPercent] = useState<number>(20);
  const [selectedMarket, setSelectedMarket] = useState<string>("EU");

  // Simulator test inputs
  const [testSubstance, setTestSubstance] = useState<string>("TRICYCLAZOLE");
  const [testMeasuredValue, setTestMeasuredValue] = useState<number>(0.0075);

  const activeSubstance = useMemo(() => {
    return (
      SUBSTANCE_RULES.find((s) => s.code === testSubstance) ||
      SUBSTANCE_RULES[0]
    );
  }, [testSubstance]);

  const activeLimit = useMemo(() => {
    return (
      activeSubstance.marketLimits.find(
        (m) => m.marketCode === selectedMarket,
      ) || activeSubstance.marketLimits[0]
    );
  }, [activeSubstance, selectedMarket]);

  const safeThreshold = useMemo(() => {
    return activeLimit.mrlMgKg * (1 - safetyBufferPercent / 100);
  }, [activeLimit, safetyBufferPercent]);

  const simulationVerdict = useMemo(() => {
    if (testMeasuredValue <= safeThreshold) {
      return {
        status: "SAFE_PASS",
        label: t("compliance.safetyMargin.safePassLabel", "ĐẠT CHUẨN AN TOÀN (SAFE PASS)"),
        description: t(
          "compliance.safetyMargin.safePassDesc",
          "Nồng độ đo thực tế nằm trong biên an toàn 20%. Đủ điều kiện phê duyệt phương án phối trộn xuất khẩu."
        ),
        color:
          "text-accent-success bg-accent-success/10 border-accent-success/20",
      };
    } else if (testMeasuredValue <= activeLimit.mrlMgKg) {
      return {
        status: "WARNING_BUFFER",
        label: t(
          "compliance.safetyMargin.warningBufferLabel",
          "CẢNH BÁO BIÊN AN TOÀN (SAFETY MARGIN WARNING)"
        ),
        description: t(
          "compliance.safetyMargin.warningBufferDesc",
          "Nồng độ đo đã vượt qua biên an toàn 20% dù chưa vượt MRL chính thức. Có nguy cơ bị từ chối nếu có sai số đo kiểm tại cảng đích."
        ),
        color:
          "text-accent-warning bg-accent-warning/10 border-accent-warning/20",
      };
    } else {
      return {
        status: "EXCEEDED_MRL",
        label: t("compliance.safetyMargin.exceededLabel", "VI PHẠM MRL (MRL EXCEEDED - HOLD)"),
        description: t(
          "compliance.safetyMargin.exceededDesc",
          "Vượt quá ngưỡng MRL tối đa của thị trường nhập khẩu! Hệ thống tự động kích hoạt Rule BR-15: Phong tỏa lệnh HOLD khẩn cấp."
        ),
        color: "text-accent-danger bg-accent-danger/10 border-accent-danger/20",
      };
    }
  }, [testMeasuredValue, safeThreshold, activeLimit, t]);

  return (
    <div className="space-y-6 pb-12">
      {/* Top Breadcrumbs */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
        <div className="flex items-center gap-2 text-xs text-ink-secondary">
          <Link to="/home" className="hover:text-ink-primary transition">
            {t("common.managerDashboard", "Bàn điều hành Quản lý")}
          </Link>
          <span>/</span>
          <span className="text-ink-primary font-semibold">
            {t("compliance.safetyMargin.breadcrumb", "Biên an toàn MRL (SC-25)")}
          </span>
        </div>

        <div className="flex items-center gap-2">
          <Link
            to="/compliance/lab-results"
            className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold rounded-lg border border-ink-tertiary/20 bg-surface-base hover:bg-surface-raised text-ink-primary transition shadow-xs"
          >
            <TestTube size={14} className="text-accent-primary" />
            <span>{t("compliance.safetyMargin.viewLabResults", "Xem Kết quả xét nghiệm (SC-24)")}</span>
            <ArrowRight size={13} />
          </Link>
        </div>
      </div>

      {/* Main Header Banner */}
      <div className="bg-surface-base border border-ink-tertiary/20 rounded-xl p-5 sm:p-6 shadow-xs relative overflow-hidden">
        <div className="flex items-start gap-4">
          <div className="w-12 h-12 rounded-xl bg-accent-success/10 text-accent-success flex items-center justify-center shrink-0 border border-accent-success/20">
            <ShieldCheck size={28} weight="duotone" />
          </div>
          <div className="space-y-1">
            <h1 className="text-xl sm:text-2xl font-bold font-serif text-ink-primary">
              {t("compliance.safetyMargin.pageTitle", "Chính Sách Biên An Toàn MRL (MRL Safety Margin Policy)")}
            </h1>
            <p className="text-xs sm:text-sm text-ink-secondary leading-relaxed max-w-3xl">
              {t(
                "compliance.safetyMargin.pageSubtitle",
                "Quy tắc BR-07 yêu cầu hệ thống tự động trừ dự phòng -20% vào giới hạn dư lượng tối đa (MRL) chính thức của thị trường đích khi tính toán công thức phối trộn xuất khẩu, nhằm triệt tiêu sai số phân tích sắc ký và biến thiên chất lượng trong hành trình vận tải biển."
              )}
            </p>
          </div>
        </div>
      </div>

      {/* Interactive Simulator & Policy Setup */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-6 items-start">
        {/* Left: Calculation Simulator (7 cols) */}
        <div className="lg:col-span-7 bg-surface-base border border-ink-tertiary/20 rounded-xl p-5 shadow-xs space-y-5">
          <div className="flex items-center justify-between border-b border-ink-tertiary/15 pb-3">
            <h2 className="text-sm font-bold font-serif text-ink-primary flex items-center gap-2">
              <Calculator size={18} className="text-accent-primary" />
              <span>{t("compliance.safetyMargin.simulatorTitle", "Công Cụ Mô Phỏng Đánh Giá Biên An Toàn Thực Tế")}</span>
            </h2>
            <span className="text-[11px] font-mono px-2 py-0.5 rounded bg-surface-raised text-ink-secondary">
              Simulation Mode
            </span>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div>
              <label className="block text-xs font-semibold text-ink-primary mb-1">
                {t("compliance.safetyMargin.targetMarketLabel", "Thị trường xuất khẩu đích:")}
              </label>
              <select
                value={selectedMarket}
                onChange={(e) => setSelectedMarket(e.target.value)}
                className="w-full px-3 py-2 text-xs rounded-lg border border-ink-tertiary/30 bg-surface-base text-ink-primary focus:outline-none focus:ring-1 focus:ring-accent-primary"
              >
                <option value="EU">Liên minh Châu Âu (EU)</option>
                <option value="USA">Hoa Kỳ (US FDA)</option>
                <option value="JP">Nhật Bản (MHLW)</option>
                <option value="CN">Trung Quốc (GB 2763)</option>
              </select>
            </div>

            <div>
              <label className="block text-xs font-semibold text-ink-primary mb-1">
                {t("compliance.safetyMargin.substanceLabel", "Hoạt chất thuốc BVTV cần thử nghiệm:")}
              </label>
              <select
                value={testSubstance}
                onChange={(e) => setTestSubstance(e.target.value)}
                className="w-full px-3 py-2 text-xs rounded-lg border border-ink-tertiary/30 bg-surface-base text-ink-primary focus:outline-none focus:ring-1 focus:ring-accent-primary"
              >
                {SUBSTANCE_RULES.map((s) => (
                  <option key={s.code} value={s.code}>
                    {s.name} ({s.commercialName.split("(")[0]})
                  </option>
                ))}
              </select>
            </div>
          </div>

          {/* Configurable Buffer Slider */}
          <div className="p-4 rounded-xl bg-surface-raised border border-ink-tertiary/10 space-y-2">
            <div className="flex items-center justify-between text-xs">
              <span className="font-semibold text-ink-primary flex items-center gap-1.5">
                <Sliders size={15} />
                <span>{t("compliance.safetyMargin.bufferRatioLabel", "Hệ số Biên an toàn cấu hình (Rule BR-07):")}</span>
              </span>
              <span className="font-mono font-bold text-accent-success text-sm">
                -{safetyBufferPercent}% Buffer
              </span>
            </div>
            <input
              type="range"
              min="10"
              max="30"
              step="5"
              value={safetyBufferPercent}
              onChange={(e) =>
                setSafetyBufferPercent(parseInt(e.target.value) || 20)
              }
              className="w-full accent-accent-success cursor-pointer"
            />
            <div className="flex justify-between text-[10px] text-ink-tertiary font-mono">
              <span>{t("compliance.safetyMargin.bufferBasic", "10% (Cơ bản)")}</span>
              <span className="font-bold text-accent-success">
                {t("compliance.safetyMargin.bufferIsoStandard", "20% (Tiêu chuẩn ISO GrainLedger)")}
              </span>
              <span>{t("compliance.safetyMargin.bufferStrict", "30% (Thắt chặt tối đa)")}</span>
            </div>
          </div>

          {/* Measured value slider/input */}
          <div className="space-y-1.5">
            <div className="flex justify-between text-xs">
              <label className="font-semibold text-ink-primary">
                {t(
                  "compliance.safetyMargin.measuredConcLabel",
                  "Nồng độ hoạt chất đo thực tế từ phòng Lab (mg/kg ppm):"
                )}
              </label>
              <span className="font-mono font-bold text-accent-primary">
                {testMeasuredValue.toFixed(4)} mg/kg
              </span>
            </div>
            <input
              type="number"
              step="0.001"
              min="0"
              max={activeLimit.mrlMgKg * 1.5}
              value={testMeasuredValue}
              onChange={(e) =>
                setTestMeasuredValue(parseFloat(e.target.value) || 0)
              }
              className="w-full px-3 py-2 text-xs rounded-lg border border-ink-tertiary/30 bg-surface-base text-ink-primary font-mono focus:outline-none focus:ring-1 focus:ring-accent-primary"
            />
          </div>

          {/* Simulation Output Card */}
          <div
            className={`p-4 rounded-xl border ${simulationVerdict.color} space-y-2`}
          >
            <div className="flex items-center justify-between">
              <span className="font-mono text-xs font-bold uppercase tracking-wider">
                {simulationVerdict.label}
              </span>
              <span className="font-mono text-xs font-semibold">
                {t("compliance.safetyMargin.measuredVsSafe", "Thực đo: {{measured}} / An toàn: {{safe}}", {
                  measured: testMeasuredValue.toFixed(4),
                  safe: safeThreshold.toFixed(4),
                })}
              </span>
            </div>
            <p className="text-xs leading-relaxed">
              {simulationVerdict.description}
            </p>
          </div>
        </div>

        {/* Right: Policy Rule Details (5 cols) */}
        <div className="lg:col-span-5 space-y-4">
          <div className="bg-surface-base border border-ink-tertiary/20 rounded-xl p-5 shadow-xs space-y-4">
            <h3 className="text-xs font-bold uppercase tracking-wider text-ink-primary flex items-center gap-1.5">
              <BookOpen size={16} className="text-accent-primary" />
              <span>{t("compliance.safetyMargin.ruleTitle", "Quy Tắc Kỹ Thuật ISO 22095 (BR-07)")}</span>
            </h3>

            <div className="space-y-3 text-xs text-ink-secondary leading-relaxed">
              <div className="p-3 rounded-lg bg-surface-raised border border-ink-tertiary/10 space-y-1">
                <strong className="text-ink-primary block">
                  {t("compliance.safetyMargin.formulaLabel", "Công thức tính ngưỡng an toàn:")}
                </strong>
                <p className="font-mono text-[11px] text-accent-success font-semibold">
                  MRL_Effective = MRL_Official × (1 - 0.20)
                </p>
                <p className="text-[11px] text-ink-tertiary">
                  {t(
                    "compliance.safetyMargin.formulaExample",
                    "Ví dụ: Tricyclazole tại EU có MRL = 0.0100 mg/kg. Ngưỡng cho phép tối đa của GrainLedger là 0.0080 mg/kg."
                  )}
                </p>
              </div>

              <div className="p-3 rounded-lg bg-surface-raised border border-ink-tertiary/10 space-y-1">
                <strong className="text-ink-primary block">
                  {t("compliance.safetyMargin.automationLabel", "Cơ chế tự động hóa:")}
                </strong>
                <ul className="list-disc pl-4 space-y-1 text-[11px]">
                  <li>
                    {t(
                      "compliance.safetyMargin.bulletWarning",
                      "Nếu vượt ngưỡng 0.0080 mg/kg: Hệ thống cảnh báo màu vàng khi lập phương án phối trộn."
                    )}
                  </li>
                  <li>
                    {t(
                      "compliance.safetyMargin.bulletHold",
                      "Nếu vượt ngưỡng 0.0100 mg/kg: Kích hoạt ngay lệnh phong tỏa HOLD (Rule BR-15), không cho phép xuất kho hoặc đóng container."
                    )}
                  </li>
                </ul>
              </div>
            </div>

            <div className="pt-2">
              <Link
                to="/compliance/mrl-versions"
                className="w-full inline-flex items-center justify-center gap-2 px-4 py-2 text-xs font-semibold rounded-lg border border-ink-tertiary/20 hover:bg-surface-raised text-ink-primary transition"
              >
                <span>{t("compliance.safetyMargin.viewMrlVersions", "Xem danh mục phiên bản MRL thị trường")}</span>
                <ArrowRight size={13} />
              </Link>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};
