/**
 * @screen SC-00 (Public Landing Page)
 * @feature Public
 * @roles ALL (Unauthenticated)
 * @description Enterprise-grade, modern landing page for GrainLedger inspired by Rize.farm.
 * Explains the Mass-Balance Custody Ledger (ISO 22095:2020), MRL compliance, offline field capture,
 * and bidirectional traceback for the rice export supply chain.
 */
import { useUiStore } from "@/app/store/uiStore";
import { MassValue } from "@/components/MassValue/MassValue";
import { StatusBadge } from "@/components/StatusBadge/StatusBadge";
import {
  ArrowRight,
  ArrowsClockwise,
  BookOpen,
  Buildings,
  CaretRight,
  ChartBar,
  ChartPieSlice,
  CheckCircle,
  Clock,
  Compass,
  Crosshair,
  Drop,
  Factory,
  Certificate,
  FileText,
  Flask,
  Globe,
  HandCoins,
  LockSimple,
  MagnifyingGlass,
  MapPin,
  Moon,
  Package,
  Scales,
  ShieldCheck,
  ShieldWarning,
  Sparkle,
  Sun,
  TestTube,
  Truck,
  Users,
  WarningCircle,
  XCircle,
} from "@phosphor-icons/react";
import { motion, useReducedMotion } from "framer-motion";
import React, { useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";

export const LandingPage: React.FC = () => {
  const { t } = useTranslation();
  const { theme, toggleTheme, language, setLanguage } = useUiStore();
  const shouldReduceMotion = useReducedMotion();

  // Stakeholder tab state
  const [activeStakeholder, setActiveStakeholder] = useState<
    "MILL" | "EXPORTER" | "FARMER" | "AUDIT"
  >("MILL");

  // Interactive Live Ledger Simulator State (Tab 1)
  const [simInputKg, setSimInputKg] = useState(100000);
  const [simOutputKg, setSimOutputKg] = useState(88000);
  const [simLossKg, setSimLossKg] = useState(12000);

  // Interactive MRL Simulator State (Tab 2)
  const [selectedMarket, setSelectedMarket] = useState<
    "EU" | "USA" | "JAPAN" | "KOREA"
  >("EU");
  const [selectedSubstance, setSelectedSubstance] = useState<
    "TCZ" | "IPT" | "HXC"
  >("TCZ");
  const [activeSimTab, setActiveSimTab] = useState<"LEDGER" | "MRL">("LEDGER");

  // Math for Ledger Simulator
  const simTotalOutAndLoss = simOutputKg + simLossKg;
  const simVariance = Math.abs(simInputKg - simTotalOutAndLoss);
  const simAllowed = simInputKg * 0.005; // 0.5% tolerance
  const isSimBalanced = simVariance <= simAllowed;

  // MRL Data Matrix (from Table 5-3 in SRS)
  const MRL_DATABASE = {
    TCZ: {
      name: "Tricyclazole (Đặc trị đạo ôn)",
      code: "TCZ001",
      cas: "41814-78-2",
      risk: "EXTREME",
      limits: {
        EU: {
          limit: 0.01,
          unit: "mg/kg",
          basis: "LOQ MRL (EC 396/2005)",
          safeMargin: 0.008,
        },
        USA: {
          limit: 3.0,
          unit: "mg/kg",
          basis: "EPA 40 CFR 180",
          safeMargin: 2.4,
        },
        JAPAN: {
          limit: 3.0,
          unit: "mg/kg",
          basis: "MHLW Positive List (Brown)",
          safeMargin: 2.4,
        },
        KOREA: {
          limit: 0.7,
          unit: "mg/kg",
          basis: "MFDS Food Code",
          safeMargin: 0.56,
        },
      },
      currentMeasured: 0.0075,
    },
    IPT: {
      name: "Isoprothiolane (Trừ đạo ôn & rầy nâu)",
      code: "IPT002",
      cas: "50512-35-1",
      risk: "LOW",
      limits: {
        EU: {
          limit: 6.0,
          unit: "mg/kg",
          basis: "EC 396/2005",
          safeMargin: 4.8,
        },
        USA: {
          limit: 6.0,
          unit: "mg/kg",
          basis: "EPA 40 CFR (Husked)",
          safeMargin: 4.8,
        },
        JAPAN: {
          limit: 7.0,
          unit: "mg/kg",
          basis: "MHLW Positive List",
          safeMargin: 5.6,
        },
        KOREA: {
          limit: 2.0,
          unit: "mg/kg",
          basis: "MFDS Food Code",
          safeMargin: 1.6,
        },
      },
      currentMeasured: 1.25,
    },
    HXC: {
      name: "Hexaconazole (Trừ nấm & khô vằn)",
      code: "HXC003",
      cas: "79983-71-4",
      risk: "HIGH",
      limits: {
        EU: { limit: 0.01, unit: "mg/kg", basis: "LOQ MRL", safeMargin: 0.008 },
        USA: {
          limit: 0.05,
          unit: "mg/kg",
          basis: "EPA Default",
          safeMargin: 0.04,
        },
        JAPAN: {
          limit: 0.01,
          unit: "mg/kg",
          basis: "Uniform Standard",
          safeMargin: 0.008,
        },
        KOREA: {
          limit: 0.3,
          unit: "mg/kg",
          basis: "MFDS Rice Limit",
          safeMargin: 0.24,
        },
      },
      currentMeasured: 0.0095,
    },
  };

  const currentMrl = MRL_DATABASE[selectedSubstance].limits[selectedMarket];
  const currentMeasured = MRL_DATABASE[selectedSubstance].currentMeasured;
  const isMrlCompliant = currentMeasured <= currentMrl.safeMargin;

  return (
    <div className="min-h-screen bg-[#F9FAF7] dark:bg-[#0E1411] text-[#1B2420] dark:text-[#F4EFE6] selection:bg-[#2F6B3A]/20 selection:text-[#2F6B3A] font-body transition-colors">
      {/* =========================================================================
          1. STICKY GLASSMORPHIC NAVBAR (Rize.farm style)
          ========================================================================= */}
      <header className="sticky top-0 z-50 backdrop-blur-md bg-[#F9FAF7]/90 dark:bg-[#0E1411]/90 border-b border-emerald-950/10 dark:border-emerald-500/15 transition-all">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 h-20 flex items-center justify-between">
          {/* Logo & Platform Tag */}
          <Link to="/" className="flex items-center h-full gap-3 group">
            <div className="flex items-center justify-center h-full">
              <img
                src="/icons.svg"
                alt="GrainLedger"
                className="h-full w-auto object-contain"
              />
            </div>
          </Link>

          {/* Desktop Nav Links */}
          <nav className="hidden lg:flex items-center gap-8 text-sm font-medium text-[#4A5C55] dark:text-[#A3B8AF]">
            <a
              href="#problem"
              className="hover:text-[#2F6B3A] dark:hover:text-emerald-400 transition-colors"
            >
              Thách thức
            </a>
            <a
              href="#tech"
              className="hover:text-[#2F6B3A] dark:hover:text-emerald-400 transition-colors"
            >
              Hệ thống lõi
            </a>
            <a
              href="#how-it-works"
              className="hover:text-[#2F6B3A] dark:hover:text-emerald-400 transition-colors"
            >
              Quy trình 5 bước
            </a>
            <a
              href="#simulators"
              className="hover:text-[#2F6B3A] dark:hover:text-emerald-400 transition-colors"
            >
              Mô phỏng trực tiếp
            </a>
            <a
              href="#ecosystem"
              className="hover:text-[#2F6B3A] dark:hover:text-emerald-400 transition-colors"
            >
              Đối tác chuỗi
            </a>
          </nav>

          {/* Right Action Controls */}
          <div className="flex items-center gap-3">
            {/* Dark Mode Toggle */}
            <button
              type="button"
              onClick={toggleTheme}
              className="p-2 rounded-lg text-[#4A5C55] dark:text-[#A3B8AF] hover:text-[#1B2420] dark:hover:text-[#F4EFE6] hover:bg-emerald-950/5 dark:hover:bg-white/5 transition-colors"
              aria-label="Toggle theme"
            >
              {theme === "dark" ? <Sun size={19} /> : <Moon size={19} />}
            </button>

            {/* Language Switcher */}
            <button
              type="button"
              onClick={() => setLanguage(language === "vi" ? "en" : "vi")}
              className="hidden sm:flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-emerald-950/5 dark:bg-white/5 hover:bg-emerald-950/10 dark:hover:bg-white/10 text-[#1B2420] dark:text-[#F4EFE6] transition-colors"
            >
              <Globe size={15} />
              <span className="font-mono uppercase">{language}</span>
            </button>

            {/* Direct Login CTA */}
            <Link
              to="/sign-in"
              className="inline-flex items-center gap-2 px-5 py-2.5 rounded-lg text-xs font-semibold bg-[#2F6B3A] text-white hover:bg-[#245430] transition-all shadow-sm hover:shadow-md"
            >
              <span>{t("landing.ctaSignIn", "Đăng nhập hệ thống")}</span>
              <ArrowRight size={14} weight="bold" />
            </Link>
          </div>
        </div>
      </header>

      {/* =========================================================================
          2. HERO SECTION (Rize.farm inspired with bold typography & trust tags)
          ========================================================================= */}
      <section className="relative overflow-hidden pt-12 pb-20 lg:pt-20 lg:pb-28">
        {/* Ambient Gradient Glows */}
        <div className="absolute top-1/4 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[700px] h-[400px] bg-gradient-to-tr from-emerald-500/15 via-[#2F6B3A]/10 to-transparent blur-3xl pointer-events-none rounded-full" />

        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 relative z-10">
          <div className="max-w-4xl mx-auto text-center space-y-6">
            {/* Top Project Pill Badge */}
            <motion.div
              initial={shouldReduceMotion ? {} : { opacity: 0, y: -10 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.4 }}
              className="inline-flex items-center gap-2 px-4 py-1.5 rounded-full text-xs font-semibold bg-emerald-800/10 dark:bg-emerald-950/60 text-[#2F6B3A] dark:text-emerald-300 border border-emerald-700/20 shadow-xs"
            >
              <ShieldCheck size={16} weight="bold" />
              <span>
                {language === "vi"
                  ? "Dự án Thí điểm Nhà máy Tiền Giang · Chuẩn Quốc tế ISO 22095:2020"
                  : "Tiền Giang Mill Pilot Project · ISO 22095:2020 International Standard"}
              </span>
            </motion.div>

            {/* Display Headline (Rize bold voice) */}
            <motion.h1
              initial={shouldReduceMotion ? {} : { opacity: 0, y: 14 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.5, delay: 0.1 }}
              className="font-display text-4xl sm:text-6xl lg:text-7xl font-bold tracking-tight text-[#1B2420] dark:text-[#F4EFE6] leading-[1.12]"
            >
              {t(
                "landing.heroHeadline",
                "Hạ tầng số cho xuất khẩu lúa gạo tương lai.",
              )}
            </motion.h1>

            {/* Subtitle */}
            <motion.p
              initial={shouldReduceMotion ? {} : { opacity: 0, y: 14 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.5, delay: 0.2 }}
              className="text-lg sm:text-xl text-[#4A5C55] dark:text-[#A3B8AF] leading-relaxed max-w-3xl mx-auto"
            >
              {t(
                "landing.heroSubheadline",
                "GrainLedger giải quyết bài toán cốt lõi của chuỗi lúa gạo: Ghi nhận chính xác tỷ lệ đóng góp của từng nông hộ nhỏ vào silo chung và kiểm soát dư lượng thuốc bảo vệ thực vật trước khi container cập cảng EU, Mỹ, Nhật Bản.",
              )}
            </motion.p>

            {/* CTAs */}
            <motion.div
              initial={shouldReduceMotion ? {} : { opacity: 0, y: 14 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.5, delay: 0.3 }}
              className="pt-4 flex flex-wrap items-center justify-center gap-4"
            >
              <Link
                to="/sign-in"
                className="inline-flex items-center gap-2 px-8 py-3.5 rounded-lg text-sm font-semibold bg-[#2F6B3A] text-white hover:bg-[#245430] transition-all shadow-md hover:shadow-lg hover:-translate-y-0.5"
              >
                <span>
                  {t("landing.ctaSignIn", "Vào bàn làm việc vận hành")}
                </span>
                <ArrowRight size={16} weight="bold" />
              </Link>
              <a
                href="#simulators"
                className="inline-flex items-center gap-2 px-7 py-3.5 rounded-lg text-sm font-semibold bg-white dark:bg-emerald-950/40 border border-emerald-950/15 dark:border-emerald-500/25 text-[#1B2420] dark:text-[#F4EFE6] hover:bg-emerald-50/50 dark:hover:bg-emerald-900/30 transition-all shadow-xs"
              >
                <Scales
                  size={18}
                  className="text-[#2F6B3A] dark:text-emerald-400"
                />
                <span>
                  {language === "vi"
                    ? "Trải nghiệm mô phỏng Sổ cái"
                    : "Experience Ledger Simulator"}
                </span>
              </a>
            </motion.div>
          </div>

          {/* Regulatory & Standards Bar (Rize.farm Partner Logos Style) */}
          <div className="mt-16 pt-8 border-t border-emerald-950/10 dark:border-emerald-500/15 text-center">
            <span className="text-[11px] font-mono uppercase tracking-wider text-[#7A8F87] dark:text-emerald-400/60 block mb-6 font-semibold">
              Tuân thủ các quy định & tiêu chuẩn kiểm định quốc tế hàng đầu
            </span>

            <div className="flex flex-wrap items-center justify-center gap-6 sm:gap-10 text-xs font-semibold text-[#4A5C55] dark:text-[#A3B8AF]">
              <div className="flex items-center gap-2 px-3 py-1.5 rounded-md bg-white/70 dark:bg-emerald-950/30 border border-emerald-950/10 dark:border-emerald-500/15 shadow-xs">
                <Certificate size={18} className="text-[#2F6B3A]" />
                <span>ISO 22095:2020 Mass Balance</span>
              </div>
              <div className="flex items-center gap-2 px-3 py-1.5 rounded-md bg-white/70 dark:bg-emerald-950/30 border border-emerald-950/10 dark:border-emerald-500/15 shadow-xs">
                <ShieldCheck size={18} className="text-blue-600" />
                <span>EU Regulation (EC) No 396/2005</span>
              </div>
              <div className="flex items-center gap-2 px-3 py-1.5 rounded-md bg-white/70 dark:bg-emerald-950/30 border border-emerald-950/10 dark:border-emerald-500/15 shadow-xs">
                <CheckCircle size={18} className="text-emerald-600" />
                <span>USA EPA 40 CFR Part 180</span>
              </div>
              <div className="flex items-center gap-2 px-3 py-1.5 rounded-md bg-white/70 dark:bg-emerald-950/30 border border-emerald-950/10 dark:border-emerald-500/15 shadow-xs">
                <Compass size={18} className="text-amber-600" />
                <span>Japan MHLW/CAA Positive List</span>
              </div>
              <div className="flex items-center gap-2 px-3 py-1.5 rounded-md bg-white/70 dark:bg-emerald-950/30 border border-emerald-950/10 dark:border-emerald-500/15 shadow-xs">
                <Sparkle size={18} className="text-purple-600" />
                <span>TCVN 11888:2017 & TT 50/2016</span>
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* =========================================================================
          3. THE BIG CHALLENGE (Directly inspired by Rize's "Rice is the greatest climate challenge")
          ========================================================================= */}
      <section
        id="problem"
        className="py-20 bg-white dark:bg-[#131D17] border-y border-emerald-950/10 dark:border-emerald-500/15"
      >
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="max-w-3xl mb-14 space-y-3">
            <span className="text-xs font-mono font-bold uppercase tracking-widest text-[#B5412B]">
              Bối cảnh thực tế & Thách thức
            </span>
            <h2 className="font-display text-3xl sm:text-5xl font-bold tracking-tight text-[#1B2420] dark:text-[#F4EFE6] leading-tight">
              Xuất khẩu gạo là bài toán truy vết & rủi ro dư lượng lớn nhất
              ĐBSCL.
            </h2>
            <p className="text-base text-[#4A5C55] dark:text-[#A3B8AF] leading-relaxed">
              Một container gạo xuất khẩu là sự hòa trộn của hàng trăm thửa
              ruộng nhỏ sau khi đưa vào lò sấy và silo công nghiệp. Khi một kết
              quả kiểm nghiệm xấu về muộn, các phương pháp quản lý bằng giấy
              hoặc Excel buộc doanh nghiệp phải dừng toàn bộ kho hoặc đối mặt
              nguy cơ trả hàng triệu USD.
            </p>
          </div>

          {/* 4 Impact Stat Cards (Matching Rize big metrics) */}
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-6">
            <div className="p-8 rounded-2xl bg-[#F9FAF7] dark:bg-[#0E1411] border border-emerald-950/10 dark:border-emerald-500/20 space-y-3 shadow-xs">
              <span className="font-display text-5xl font-bold text-[#1B2420] dark:text-[#F4EFE6] block">
                8,06M
              </span>
              <h4 className="font-semibold text-sm text-[#1B2420] dark:text-[#F4EFE6]">
                Tấn gạo xuất khẩu / năm
              </h4>
              <p className="text-xs text-[#7A8F87] dark:text-[#A3B8AF] leading-normal">
                Giá trị kim ngạch hơn 4,1 tỷ USD của Việt Nam đòi hỏi chứng minh
                nguồn gốc minh bạch với đối tác toàn cầu.
              </p>
            </div>

            <div className="p-8 rounded-2xl bg-[#F9FAF7] dark:bg-[#0E1411] border border-red-500/20 space-y-3 shadow-xs">
              <span className="font-display text-5xl font-bold text-[#B5412B] block">
                0,01
              </span>
              <h4 className="font-semibold text-sm text-[#B5412B]">
                mg/kg ngưỡng MRL EU (LOQ)
              </h4>
              <p className="text-xs text-[#7A8F87] dark:text-[#A3B8AF] leading-normal">
                Quy định khắt khe của EU với hoạt chất Tricyclazole — chỉ một mẻ
                nhiễm bẩn có thể khiến toàn bộ container bị từ chối cập cảng.
              </p>
            </div>

            <div className="p-8 rounded-2xl bg-[#F9FAF7] dark:bg-[#0E1411] border border-emerald-950/10 dark:border-emerald-500/20 space-y-3 shadow-xs">
              <span className="font-display text-5xl font-bold text-[#2F6B3A] dark:text-emerald-400 block">
                100%
              </span>
              <h4 className="font-semibold text-sm text-[#2F6B3A] dark:text-emerald-400">
                Khối lượng được bảo toàn
              </h4>
              <p className="text-xs text-[#7A8F87] dark:text-[#A3B8AF] leading-normal">
                Sổ cái cân bằng kép từ chối dứt khoát mọi mẻ chế biến sai lệch
                ngoài dung sai 0,5% — không cho phép gian lận hoặc
                double-spending.
              </p>
            </div>

            <div className="p-8 rounded-2xl bg-[#F9FAF7] dark:bg-[#0E1411] border border-emerald-950/10 dark:border-emerald-500/20 space-y-3 shadow-xs">
              <span className="font-display text-5xl font-bold text-blue-600 dark:text-blue-400 block">
                &lt; 30s
              </span>
              <h4 className="font-semibold text-sm text-blue-600 dark:text-blue-400">
                Truy vết đồ thị hai chiều
              </h4>
              <p className="text-xs text-[#7A8F87] dark:text-[#A3B8AF] leading-normal">
                Định vị chính xác từ lọ mẫu lab đến container xuất khẩu, và từ
                container ngược về danh sách nông hộ kèm tỷ lệ % khối lượng.
              </p>
            </div>
          </div>
        </div>
      </section>

      {/* =========================================================================
          4. HOW DO WE SOLVE IT? (4 Tech Pillars)
          ========================================================================= */}
      <section id="tech" className="py-20 lg:py-28">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="text-center max-w-3xl mx-auto mb-16 space-y-3">
            <span className="text-xs font-mono font-bold uppercase tracking-widest text-[#2F6B3A] dark:text-emerald-400">
              Công nghệ độc quyền
            </span>
            <h2 className="font-display text-3xl sm:text-5xl font-bold tracking-tight text-[#1B2420] dark:text-[#F4EFE6]">
              Cách GrainLedger giải quyết bài toán
            </h2>
            <p className="text-base text-[#4A5C55] dark:text-[#A3B8AF]">
              Nền tảng số hoá khép kín từ mảnh ruộng của nông dân đến hồ sơ hải
              quan của container xuất khẩu.
            </p>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-8">
            {/* Pillar 1 */}
            <div className="bg-white dark:bg-[#131D17] border border-emerald-950/10 dark:border-emerald-500/20 rounded-2xl p-7 space-y-4 hover:shadow-lg transition-all group">
              <div className="w-12 h-12 rounded-xl bg-emerald-100 dark:bg-emerald-950/60 text-[#2F6B3A] dark:text-emerald-400 flex items-center justify-center group-hover:scale-110 transition-transform">
                <Scales size={26} weight="bold" />
              </div>
              <h3 className="font-display font-bold text-xl text-[#1B2420] dark:text-[#F4EFE6]">
                Sổ cái Cân bằng Khối lượng
              </h3>
              <p className="text-xs text-[#4A5C55] dark:text-[#A3B8AF] leading-relaxed">
                Áp dụng mô hình Mass Balance ISO 22095. Tổng đầu ra cộng hao hụt
                phải bằng tổng đầu vào trong phạm vi dung sai (0,5%). Mọi vi
                phạm bị từ chối ở tầng dữ liệu.
              </p>
              <div className="pt-2 text-[11px] font-mono text-[#2F6B3A] font-bold">
                Quy tắc BR-01 & BR-02
              </div>
            </div>

            {/* Pillar 2 */}
            <div className="bg-white dark:bg-[#131D17] border border-emerald-950/10 dark:border-emerald-500/20 rounded-2xl p-7 space-y-4 hover:shadow-lg transition-all group">
              <div className="w-12 h-12 rounded-xl bg-blue-100 dark:bg-blue-950/60 text-blue-600 dark:text-blue-400 flex items-center justify-center group-hover:scale-110 transition-transform">
                <ShieldCheck size={26} weight="bold" />
              </div>
              <h3 className="font-display font-bold text-xl text-[#1B2420] dark:text-[#F4EFE6]">
                Động cơ Dư lượng MRL
              </h3>
              <p className="text-xs text-[#4A5C55] dark:text-[#A3B8AF] leading-relaxed">
                Tự động gán cận bảo thủ 95% cho các lô chưa xét nghiệm, áp dụng
                hệ số chế biến phân đoạn (gạo nguyên, tấm, cám) và trừ biên an
                toàn 20% khi duyệt phương án.
              </p>
              <div className="pt-2 text-[11px] font-mono text-blue-600 font-bold">
                Quy tắc BR-06 & BR-07
              </div>
            </div>

            {/* Pillar 3 */}
            <div className="bg-white dark:bg-[#131D17] border border-emerald-950/10 dark:border-emerald-500/20 rounded-2xl p-7 space-y-4 hover:shadow-lg transition-all group">
              <div className="w-12 h-12 rounded-xl bg-amber-100 dark:bg-amber-950/60 text-amber-700 dark:text-amber-400 flex items-center justify-center group-hover:scale-110 transition-transform">
                <MapPin size={26} weight="bold" />
              </div>
              <h3 className="font-display font-bold text-xl text-[#1B2420] dark:text-[#F4EFE6]">
                Thu mua Ngoại tuyến Hiện trường
              </h3>
              <p className="text-xs text-[#4A5C55] dark:text-[#A3B8AF] leading-relaxed">
                Ứng dụng web hoạt động hoàn hảo khi mất mạng tại bờ ruộng. Ghi
                toạ độ thửa đất, khai báo phun thuốc và đồng bộ chống trùng lặp
                (Idempotent) khi có sóng.
              </p>
              <div className="pt-2 text-[11px] font-mono text-amber-700 font-bold">
                Quy tắc BR-09 & BR-11
              </div>
            </div>

            {/* Pillar 4 */}
            <div className="bg-white dark:bg-[#131D17] border border-emerald-950/10 dark:border-emerald-500/20 rounded-2xl p-7 space-y-4 hover:shadow-lg transition-all group">
              <div className="w-12 h-12 rounded-xl bg-red-100 dark:bg-red-950/60 text-[#B5412B] flex items-center justify-center group-hover:scale-110 transition-transform">
                <Crosshair size={26} weight="bold" />
              </div>
              <h3 className="font-display font-bold text-xl text-[#1B2420] dark:text-[#F4EFE6]">
                Truy vết & Khóa HOLD
              </h3>
              <p className="text-xs text-[#4A5C55] dark:text-[#A3B8AF] leading-relaxed">
                Ban hành lệnh HOLD tức thì để ngăn chặn xuất xưởng khi phát hiện
                nguy cơ. Truy vết đồ thị hai chiều và sinh Hồ sơ tuân thủ
                (Digital Dossier) tự động.
              </p>
              <div className="pt-2 text-[11px] font-mono text-[#B5412B] font-bold">
                Quy tắc BR-15 & BR-17
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* =========================================================================
          5. COMPARISON MATRIX (Old Way vs GrainLedger Way)
          ========================================================================= */}
      <section
        id="comparison"
        className="py-20 bg-white dark:bg-[#131D17] border-y border-emerald-950/10 dark:border-emerald-500/15"
      >
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="text-center max-w-3xl mx-auto mb-14 space-y-3">
            <span className="text-xs font-mono font-bold uppercase tracking-widest text-[#2F6B3A] dark:text-emerald-400">
              Đối chiếu giải pháp
            </span>
            <h2 className="font-display text-3xl sm:text-4xl font-bold tracking-tight text-[#1B2420] dark:text-[#F4EFE6]">
              Mô hình truyền thống so với Sổ cái GrainLedger
            </h2>
          </div>

          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm border-collapse">
              <thead>
                <tr className="border-b-2 border-emerald-950/15 dark:border-emerald-500/25">
                  <th className="py-4 px-6 font-display font-bold text-base text-[#1B2420] dark:text-[#F4EFE6] w-1/4">
                    Tiêu chí vận hành
                  </th>
                  <th className="py-4 px-6 font-semibold text-sm text-[#7A8F87] w-3/8 bg-red-50/30 dark:bg-red-950/10">
                    Cách làm cũ (Sổ sách & Excel)
                  </th>
                  <th className="py-4 px-6 font-semibold text-sm text-[#2F6B3A] dark:text-emerald-400 w-3/8 bg-emerald-50/40 dark:bg-emerald-950/30">
                    Với GrainLedger (Mass-Balance Ledger)
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-emerald-950/10 dark:divide-emerald-500/15">
                <tr>
                  <td className="py-4 px-6 font-semibold text-[#1B2420] dark:text-[#F4EFE6]">
                    Trộn lúa trong Silo
                  </td>
                  <td className="py-4 px-6 text-xs text-[#4A5C55] dark:text-[#A3B8AF] bg-red-50/30 dark:bg-red-950/10">
                    Mất hoàn toàn dấu vết nguồn gốc sau khi đổ lúa của nhiều
                    nông dân vào silo.
                  </td>
                  <td className="py-4 px-6 text-xs font-medium text-[#1B2420] dark:text-[#F4EFE6] bg-emerald-50/40 dark:bg-emerald-950/30">
                    <strong className="text-[#2F6B3A] dark:text-emerald-400">
                      Bảo toàn tỷ lệ khối lượng:
                    </strong>{" "}
                    Xác định chính xác từng thửa ruộng đóng góp bao nhiêu % vào
                    mẻ gạo xuất khẩu.
                  </td>
                </tr>

                <tr>
                  <td className="py-4 px-6 font-semibold text-[#1B2420] dark:text-[#F4EFE6]">
                    Khi có kết quả Lab xấu
                  </td>
                  <td className="py-4 px-6 text-xs text-[#4A5C55] dark:text-[#A3B8AF] bg-red-50/30 dark:bg-red-950/10">
                    Không biết container nào bị nhiễm; phải phong toả toàn bộ
                    kho hoặc đối mặt rủi ro bị nước nhập khẩu phạt.
                  </td>
                  <td className="py-4 px-6 text-xs font-medium text-[#1B2420] dark:text-[#F4EFE6] bg-emerald-50/40 dark:bg-emerald-950/30">
                    <strong className="text-[#2F6B3A] dark:text-emerald-400">
                      Truy vết đồ thị trong 30s:
                    </strong>{" "}
                    Chỉ ra chính xác các container bị ảnh hưởng và khối lượng kg
                    nhiễm để cách ly.
                  </td>
                </tr>

                <tr>
                  <td className="py-4 px-6 font-semibold text-[#1B2420] dark:text-[#F4EFE6]">
                    Kiểm soát hao hụt & Thất thoát
                  </td>
                  <td className="py-4 px-6 text-xs text-[#4A5C55] dark:text-[#A3B8AF] bg-red-50/30 dark:bg-red-950/10">
                    Dễ khai khống, gõ nhầm hoặc tạo số liệu ảo mà hệ thống không
                    ngăn chặn được.
                  </td>
                  <td className="py-4 px-6 text-xs font-medium text-[#1B2420] dark:text-[#F4EFE6] bg-emerald-50/40 dark:bg-emerald-950/30">
                    <strong className="text-[#2F6B3A] dark:text-emerald-400">
                      Từ chối tuyệt đối:
                    </strong>{" "}
                    Giao dịch không cân bằng theo dung sai bị huỷ ngay lập tức,
                    không cho phép lưu nháp sai.
                  </td>
                </tr>

                <tr>
                  <td className="py-4 px-6 font-semibold text-[#1B2420] dark:text-[#F4EFE6]">
                    Duyệt xuất khẩu MRL
                  </td>
                  <td className="py-4 px-6 text-xs text-[#4A5C55] dark:text-[#A3B8AF] bg-red-50/30 dark:bg-red-950/10">
                    Phỏng đoán cảm tính, phụ thuộc vào kinh nghiệm người phối
                    trộn gạo.
                  </td>
                  <td className="py-4 px-6 text-xs font-medium text-[#1B2420] dark:text-[#F4EFE6] bg-emerald-50/40 dark:bg-emerald-950/30">
                    <strong className="text-[#2F6B3A] dark:text-emerald-400">
                      Tính toán toán học bảo thủ:
                    </strong>{" "}
                    Kiểm tra nồng độ theo phiên bản MRL của thị trường đích kèm
                    biên an toàn 20%.
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </section>

      {/* =========================================================================
          6. INTERACTIVE SIMULATORS (Dual-Tab Demo)
          ========================================================================= */}
      <section
        id="simulators"
        className="py-20 lg:py-28 bg-[#F4EFE6] dark:bg-[#0E1411]"
      >
        <div className="max-w-5xl mx-auto px-4 sm:px-6">
          <div className="text-center max-w-2xl mx-auto mb-10 space-y-3">
            <span className="text-xs font-mono font-bold uppercase tracking-widest text-[#2F6B3A] dark:text-emerald-400">
              Trải nghiệm thời gian thực
            </span>
            <h2 className="font-display text-3xl sm:text-4xl font-bold tracking-tight text-[#1B2420] dark:text-[#F4EFE6]">
              Trình mô phỏng Sổ cái & Kiểm định MRL
            </h2>
            <p className="text-xs text-[#4A5C55] dark:text-[#A3B8AF]">
              Khám phá cách thuật toán của GrainLedger xử lý khối lượng và tiêu
              chuẩn MRL.
            </p>
          </div>

          {/* Simulator Tabs */}
          <div className="flex items-center justify-center gap-2 mb-8">
            <button
              type="button"
              onClick={() => setActiveSimTab("LEDGER")}
              className={`px-5 py-2.5 rounded-lg text-xs font-semibold transition-all ${
                activeSimTab === "LEDGER"
                  ? "bg-[#2F6B3A] text-white shadow-sm"
                  : "bg-white dark:bg-emerald-950/40 text-[#4A5C55] dark:text-[#A3B8AF] hover:text-[#1B2420] border border-emerald-950/10"
              }`}
            >
              1. Sổ cái Cân bằng Khối lượng (BR-01)
            </button>
            <button
              type="button"
              onClick={() => setActiveSimTab("MRL")}
              className={`px-5 py-2.5 rounded-lg text-xs font-semibold transition-all ${
                activeSimTab === "MRL"
                  ? "bg-[#2F6B3A] text-white shadow-sm"
                  : "bg-white dark:bg-emerald-950/40 text-[#4A5C55] dark:text-[#A3B8AF] hover:text-[#1B2420] border border-emerald-950/10"
              }`}
            >
              2. Đánh giá MRL & Biên An Toàn (BR-07)
            </button>
          </div>

          {/* SIMULATOR TAB 1: MASS BALANCE */}
          {activeSimTab === "LEDGER" && (
            <div className="bg-white dark:bg-[#131D17] border border-emerald-950/15 dark:border-emerald-500/25 rounded-2xl p-6 sm:p-8 shadow-md space-y-6">
              <div className="flex items-center justify-between border-b border-emerald-950/10 pb-4">
                <div>
                  <h3 className="font-display font-bold text-lg text-[#1B2420] dark:text-[#F4EFE6]">
                    Công thức: Tổng Vào = Tổng Ra + Hao Hụt Khai Báo (Dung sai ≤
                    0,5%)
                  </h3>
                  <p className="text-xs text-[#7A8F87]">
                    Nhập số liệu hoặc chọn mẻ thử nghiệm để kiểm tra tính toàn
                    vẹn của sổ cái.
                  </p>
                </div>
                <StatusBadge
                  status={isSimBalanced ? "BALANCED" : "REJECTED"}
                  label={
                    isSimBalanced
                      ? "Sổ cái Chấp nhận"
                      : "Sổ cái Từ chối (MSG01)"
                  }
                />
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-3 gap-5">
                <div>
                  <label className="text-xs font-semibold text-[#4A5C55] dark:text-[#A3B8AF] block mb-1.5">
                    Đầu vào (Lúa tươi đưa vào máy sấy)
                  </label>
                  <div className="relative">
                    <input
                      type="number"
                      value={simInputKg}
                      onChange={(e) => setSimInputKg(Number(e.target.value))}
                      className="w-full px-3 py-2 text-sm font-mono font-bold text-right rounded-lg bg-[#F9FAF7] dark:bg-[#0E1411] border border-emerald-950/15 dark:border-emerald-500/20 focus:border-[#2F6B3A] focus:outline-none"
                    />
                    <span className="absolute right-3 top-2.5 text-xs text-[#7A8F87]">
                      kg
                    </span>
                  </div>
                </div>

                <div>
                  <label className="text-xs font-semibold text-[#4A5C55] dark:text-[#A3B8AF] block mb-1.5">
                    Đầu ra (Thóc khô S1 thu được)
                  </label>
                  <div className="relative">
                    <input
                      type="number"
                      value={simOutputKg}
                      onChange={(e) => setSimOutputKg(Number(e.target.value))}
                      className="w-full px-3 py-2 text-sm font-mono font-bold text-right rounded-lg bg-[#F9FAF7] dark:bg-[#0E1411] border border-emerald-950/15 dark:border-emerald-500/20 focus:border-[#2F6B3A] focus:outline-none"
                    />
                    <span className="absolute right-3 top-2.5 text-xs text-[#7A8F87]">
                      kg
                    </span>
                  </div>
                </div>

                <div>
                  <label className="text-xs font-semibold text-[#4A5C55] dark:text-[#A3B8AF] block mb-1.5">
                    Hao hụt ẩm khai báo
                  </label>
                  <div className="relative">
                    <input
                      type="number"
                      value={simLossKg}
                      onChange={(e) => setSimLossKg(Number(e.target.value))}
                      className="w-full px-3 py-2 text-sm font-mono font-bold text-right rounded-lg bg-[#F9FAF7] dark:bg-[#0E1411] border border-emerald-950/15 dark:border-emerald-500/20 focus:border-[#2F6B3A] focus:outline-none"
                    />
                    <span className="absolute right-3 top-2.5 text-xs text-[#7A8F87]">
                      kg
                    </span>
                  </div>
                </div>
              </div>

              {/* Status Outcome Banner */}
              <div
                className={`p-5 rounded-xl border flex items-center justify-between flex-wrap gap-4 ${
                  isSimBalanced
                    ? "bg-emerald-500/10 border-[#2F6B3A]/30 text-[#2F6B3A] dark:text-emerald-400"
                    : "bg-red-500/10 border-[#B5412B]/40 text-[#B5412B]"
                }`}
              >
                <div className="flex items-center gap-3">
                  <div
                    className={`p-2.5 rounded-full ${
                      isSimBalanced
                        ? "bg-[#2F6B3A] text-white"
                        : "bg-[#B5412B] text-white"
                    }`}
                  >
                    {isSimBalanced ? (
                      <CheckCircle size={22} weight="bold" />
                    ) : (
                      <LockSimple size={22} weight="fill" />
                    )}
                  </div>
                  <div>
                    <h4 className="font-bold text-sm">
                      {isSimBalanced
                        ? "BÚT TOÁN CÂN BẰNG — ĐÃ GHI NHẬN VÀO SỔ CÁI BẤT BIẾN"
                        : "MSG01: BƯỚC CHẾ BIẾN BỊ TỪ CHỐI (VƯỢT DUNG SAI)"}
                    </h4>
                    <p className="text-xs opacity-90 mt-0.5">
                      Độ lệch:{" "}
                      <strong className="font-mono">
                        {simVariance.toLocaleString("vi-VN")} kg
                      </strong>{" "}
                      · Dung sai tối đa cho phép (0,5%):{" "}
                      <span className="font-mono font-bold">
                        {simAllowed.toLocaleString("vi-VN")} kg
                      </span>
                    </p>
                  </div>
                </div>

                <div className="flex items-center gap-2">
                  <button
                    type="button"
                    onClick={() => {
                      setSimInputKg(100000);
                      setSimOutputKg(88000);
                      setSimLossKg(12000);
                    }}
                    className="px-3 py-1.5 rounded-md text-xs font-semibold bg-white dark:bg-[#0E1411] border border-emerald-950/15 text-[#1B2420] dark:text-[#F4EFE6]"
                  >
                    Mẫu chuẩn (0 kg lệch)
                  </button>
                  <button
                    type="button"
                    onClick={() => {
                      setSimInputKg(100000);
                      setSimOutputKg(92000);
                      setSimLossKg(12000);
                    }}
                    className="px-3 py-1.5 rounded-md text-xs font-semibold bg-white dark:bg-[#0E1411] border border-red-500/40 text-[#B5412B]"
                  >
                    Mẫu lỗi gõ nhầm (+4.000 kg)
                  </button>
                </div>
              </div>
            </div>
          )}

          {/* SIMULATOR TAB 2: MRL COMPLIANCE */}
          {activeSimTab === "MRL" && (
            <div className="bg-white dark:bg-[#131D17] border border-emerald-950/15 dark:border-emerald-500/25 rounded-2xl p-6 sm:p-8 shadow-md space-y-6">
              <div className="flex items-center justify-between border-b border-emerald-950/10 pb-4">
                <div>
                  <h3 className="font-display font-bold text-lg text-[#1B2420] dark:text-[#F4EFE6]">
                    Kiểm định Giới hạn Dư lượng MRL Đích
                  </h3>
                  <p className="text-xs text-[#7A8F87]">
                    Áp dụng ngưỡng trần quy định của thị trường nhập khẩu trừ
                    biên an toàn 20%.
                  </p>
                </div>
                <StatusBadge
                  status={isMrlCompliant ? "COMPLIANT" : "NON_COMPLIANT"}
                  label={
                    isMrlCompliant
                      ? "Đạt chuẩn xuất khẩu"
                      : "Nguy cơ vượt ngưỡng"
                  }
                />
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-5">
                <div>
                  <label className="text-xs font-semibold text-[#4A5C55] dark:text-[#A3B8AF] block mb-1.5">
                    Thị trường đích xuất khẩu:
                  </label>
                  <div className="grid grid-cols-2 gap-2">
                    {[
                      { id: "EU", label: "Châu Âu (EU)" },
                      { id: "USA", label: "Hoa Kỳ (USA)" },
                      { id: "JAPAN", label: "Nhật Bản (MHLW)" },
                      { id: "KOREA", label: "Hàn Quốc (MFDS)" },
                    ].map((m) => (
                      <button
                        key={m.id}
                        type="button"
                        onClick={() => setSelectedMarket(m.id as any)}
                        className={`p-2.5 rounded-lg text-xs font-semibold border transition-all ${
                          selectedMarket === m.id
                            ? "bg-[#2F6B3A] text-white border-[#2F6B3A]"
                            : "bg-[#F9FAF7] dark:bg-[#0E1411] text-[#4A5C55] dark:text-[#A3B8AF] border-emerald-950/10"
                        }`}
                      >
                        {m.label}
                      </button>
                    ))}
                  </div>
                </div>

                <div>
                  <label className="text-xs font-semibold text-[#4A5C55] dark:text-[#A3B8AF] block mb-1.5">
                    Hoạt chất thuốc BVTV kiểm định:
                  </label>
                  <div className="space-y-1.5">
                    {[
                      {
                        id: "TCZ",
                        name: "Tricyclazole (Rủi ro cực cao tại EU)",
                      },
                      { id: "IPT", name: "Isoprothiolane (Trừ đạo ôn)" },
                      { id: "HXC", name: "Hexaconazole (Trừ nấm)" },
                    ].map((s) => (
                      <button
                        key={s.id}
                        type="button"
                        onClick={() => setSelectedSubstance(s.id as any)}
                        className={`w-full text-left px-3 py-2 rounded-lg text-xs font-medium border transition-all ${
                          selectedSubstance === s.id
                            ? "bg-emerald-50 dark:bg-emerald-950/60 border-[#2F6B3A] text-[#2F6B3A] dark:text-emerald-300 font-bold"
                            : "bg-[#F9FAF7] dark:bg-[#0E1411] text-[#4A5C55] dark:text-[#A3B8AF] border-emerald-950/10"
                        }`}
                      >
                        {s.name}
                      </button>
                    ))}
                  </div>
                </div>
              </div>

              {/* MRL Threshold Comparison Card */}
              <div className="p-4 rounded-xl bg-[#F9FAF7] dark:bg-[#0E1411] border border-emerald-950/10 space-y-3 text-xs">
                <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
                  <div>
                    <span className="text-[#7A8F87] block">
                      Căn cứ pháp lý:
                    </span>
                    <span className="font-semibold text-[#1B2420] dark:text-[#F4EFE6]">
                      {currentMrl.basis}
                    </span>
                  </div>
                  <div>
                    <span className="text-[#7A8F87] block">
                      Ngưỡng MRL tối đa:
                    </span>
                    <span className="font-mono font-bold text-[#1B2420] dark:text-[#F4EFE6]">
                      {currentMrl.limit} {currentMrl.unit}
                    </span>
                  </div>
                  <div>
                    <span className="text-[#7A8F87] block">
                      Ngưỡng an toàn (-20%):
                    </span>
                    <span className="font-mono font-bold text-[#2F6B3A]">
                      {currentMrl.safeMargin} {currentMrl.unit}
                    </span>
                  </div>
                  <div>
                    <span className="text-[#7A8F87] block">
                      Dư lượng mẻ thử nghiệm:
                    </span>
                    <span
                      className={`font-mono font-bold ${
                        isMrlCompliant ? "text-[#2F6B3A]" : "text-[#B5412B]"
                      }`}
                    >
                      {currentMeasured} {currentMrl.unit}
                    </span>
                  </div>
                </div>

                <div className="pt-2 flex items-center gap-2">
                  {isMrlCompliant ? (
                    <span className="text-xs text-[#2F6B3A] font-semibold flex items-center gap-1.5">
                      <CheckCircle size={16} weight="bold" />
                      Mẻ gạo đạt chuẩn xuất khẩu sang thị trường{" "}
                      {selectedMarket}. Sẵn sàng cấp phép xếp container.
                    </span>
                  ) : (
                    <span className="text-xs text-[#B5412B] font-semibold flex items-center gap-1.5">
                      <WarningCircle size={16} weight="bold" />
                      Dư lượng tiếp cận hoặc vượt biên an toàn. Thuật toán phối
                      trộn sẽ đề xuất bổ sung lô gạo sạch để hạ nồng độ.
                    </span>
                  )}
                </div>
              </div>
            </div>
          )}
        </div>
      </section>

      {/* =========================================================================
          7. ECOSYSTEM & PARTNERS (Rize.farm Partners Carousel/Tabs)
          ========================================================================= */}
      <section
        id="ecosystem"
        className="py-20 lg:py-28 bg-white dark:bg-[#131D17]"
      >
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="text-center max-w-3xl mx-auto mb-16 space-y-3">
            <span className="text-xs font-mono font-bold uppercase tracking-widest text-[#2F6B3A] dark:text-emerald-400">
              Hệ sinh thái liên kết
            </span>
            <h2 className="font-display text-3xl sm:text-5xl font-bold tracking-tight text-[#1B2420] dark:text-[#F4EFE6]">
              Phục vụ toàn diện các bên trong chuỗi giá trị
            </h2>
          </div>

          {/* Stakeholder Category Tabs */}
          <div className="flex flex-wrap items-center justify-center gap-2 mb-10">
            {[
              { id: "MILL", label: "Nhà máy Xay xát & Sấy lúa", icon: Factory },
              { id: "EXPORTER", label: "Doanh nghiệp Xuất khẩu", icon: Truck },
              { id: "FARMER", label: "Nông hộ & Tổ hợp tác", icon: Users },
              {
                id: "AUDIT",
                label: "Cơ quan Kiểm định & Hải quan",
                icon: Certificate,
              },
            ].map((tab) => {
              const Icon = tab.icon;
              return (
                <button
                  key={tab.id}
                  type="button"
                  onClick={() => setActiveStakeholder(tab.id as any)}
                  className={`flex items-center gap-2 px-5 py-2.5 rounded-full text-xs font-semibold transition-all ${
                    activeStakeholder === tab.id
                      ? "bg-[#2F6B3A] text-white shadow-sm"
                      : "bg-[#F9FAF7] dark:bg-[#0E1411] text-[#4A5C55] dark:text-[#A3B8AF] hover:text-[#1B2420] border border-emerald-950/10"
                  }`}
                >
                  <Icon size={16} />
                  <span>{tab.label}</span>
                </button>
              );
            })}
          </div>

          {/* Stakeholder Details Card */}
          <div className="bg-[#F9FAF7] dark:bg-[#0E1411] border border-emerald-950/10 dark:border-emerald-500/20 rounded-3xl p-8 sm:p-12 shadow-sm max-w-5xl mx-auto">
            {activeStakeholder === "MILL" && (
              <div className="grid grid-cols-1 md:grid-cols-2 gap-8 items-center">
                <div className="space-y-4">
                  <span className="text-xs font-mono font-bold uppercase text-[#2F6B3A] tracking-wider">
                    Dành cho Ban Điều hành Nhà máy
                  </span>
                  <h3 className="font-display text-2xl sm:text-3xl font-bold text-[#1B2420] dark:text-[#F4EFE6]">
                    Minh bạch sản lượng sấy, xay xát và lưu trữ silo
                  </h3>
                  <p className="text-xs text-[#4A5C55] dark:text-[#A3B8AF] leading-relaxed">
                    Cung cấp dịch vụ sấy, silo và xay xát cho nhiều công ty xuất
                    khẩu mà không lo tranh chấp về hao hụt ẩm hay tỷ lệ thu hồi
                    thành phẩm, tấm và cám.
                  </p>
                  <ul className="space-y-2 text-xs text-[#1B2420] dark:text-[#F4EFE6]">
                    <li className="flex items-center gap-2">
                      <CheckCircle
                        size={16}
                        className="text-[#2F6B3A] shrink-0"
                        weight="fill"
                      />
                      <span>
                        Hạch toán hao hụt theo từng mẻ máy có số đo cân đối
                        chiếu
                      </span>
                    </li>
                    <li className="flex items-center gap-2">
                      <CheckCircle
                        size={16}
                        className="text-[#2F6B3A] shrink-0"
                        weight="fill"
                      />
                      <span>
                        Cách ly tuyệt đối lô hàng của từng công ty khách hàng
                      </span>
                    </li>
                    <li className="flex items-center gap-2">
                      <CheckCircle
                        size={16}
                        className="text-[#2F6B3A] shrink-0"
                        weight="fill"
                      />
                      <span>
                        Xử lý bút toán bù chuẩn mực khi có hiệu chỉnh số cân
                      </span>
                    </li>
                  </ul>
                </div>

                <div className="p-6 rounded-2xl bg-white dark:bg-[#131D17] border border-emerald-950/10 space-y-3">
                  <div className="flex items-center justify-between text-xs font-bold border-b pb-2">
                    <span>Mẫu báo cáo mẻ xay xát S1</span>
                    <span className="text-[#2F6B3A] font-mono">
                      100% Cân bằng
                    </span>
                  </div>
                  <div className="space-y-2 text-xs">
                    <div className="flex justify-between">
                      <span className="text-[#7A8F87]">Thóc khô đưa vào:</span>
                      <span className="font-mono font-bold">88.000,000 kg</span>
                    </div>
                    <div className="flex justify-between">
                      <span className="text-[#7A8F87]">
                        Gạo nguyên thu hồi:
                      </span>
                      <span className="font-mono font-bold text-[#2F6B3A]">
                        58.000,000 kg (65.9%)
                      </span>
                    </div>
                    <div className="flex justify-between">
                      <span className="text-[#7A8F87]">Tấm + Cám + Trấu:</span>
                      <span className="font-mono">29.700,000 kg (33.8%)</span>
                    </div>
                    <div className="flex justify-between border-t pt-1">
                      <span className="text-[#7A8F87]">Hao hụt máy:</span>
                      <span className="font-mono">300,000 kg (0.3%)</span>
                    </div>
                  </div>
                </div>
              </div>
            )}

            {activeStakeholder === "EXPORTER" && (
              <div className="grid grid-cols-1 md:grid-cols-2 gap-8 items-center">
                <div className="space-y-4">
                  <span className="text-xs font-mono font-bold uppercase text-blue-600 tracking-wider">
                    Dành cho Doanh nghiệp Xuất khẩu
                  </span>
                  <h3 className="font-display text-2xl sm:text-3xl font-bold text-[#1B2420] dark:text-[#F4EFE6]">
                    Bảo đảm từng container đạt chuẩn thị trường đích
                  </h3>
                  <p className="text-xs text-[#4A5C55] dark:text-[#A3B8AF] leading-relaxed">
                    Tối ưu hóa các mẻ phối trộn đạt tiêu chuẩn tỷ lệ tấm, độ ẩm
                    và dư lượng hoạt chất dưới ngưỡng MRL mà không lo bị từ chối
                    đơn hàng.
                  </p>
                  <ul className="space-y-2 text-xs text-[#1B2420] dark:text-[#F4EFE6]">
                    <li className="flex items-center gap-2">
                      <CheckCircle
                        size={16}
                        className="text-blue-600 shrink-0"
                        weight="fill"
                      />
                      <span>
                        Thuật toán đề xuất mẻ trộn tối ưu lợi nhuận và an toàn
                        MRL
                      </span>
                    </li>
                    <li className="flex items-center gap-2">
                      <CheckCircle
                        size={16}
                        className="text-blue-600 shrink-0"
                        weight="fill"
                      />
                      <span>
                        Xuất hồ sơ tuân thủ kèm QR truy xuất nguồn gốc số hoá
                      </span>
                    </li>
                    <li className="flex items-center gap-2">
                      <CheckCircle
                        size={16}
                        className="text-blue-600 shrink-0"
                        weight="fill"
                      />
                      <span>
                        Giám sát tiến độ đóng niêm container trực tiếp thời gian
                        thực
                      </span>
                    </li>
                  </ul>
                </div>

                <div className="p-6 rounded-2xl bg-white dark:bg-[#131D17] border border-emerald-950/10 space-y-3">
                  <div className="flex items-center justify-between text-xs font-bold border-b pb-2">
                    <span>Hồ sơ xuất xưởng Container C1</span>
                    <span className="text-blue-600 font-mono">
                      Đạt chuẩn EU
                    </span>
                  </div>
                  <div className="space-y-2 text-xs">
                    <div className="flex justify-between">
                      <span className="text-[#7A8F87]">Thị trường đích:</span>
                      <span className="font-bold">
                        Châu Âu (Regulation EC 396)
                      </span>
                    </div>
                    <div className="flex justify-between">
                      <span className="text-[#7A8F87]">Khối lượng tịnh:</span>
                      <span className="font-mono font-bold">25.000,000 kg</span>
                    </div>
                    <div className="flex justify-between">
                      <span className="text-[#7A8F87]">Số niêm chì:</span>
                      <span className="font-mono">GL-SEAL-8891</span>
                    </div>
                  </div>
                </div>
              </div>
            )}

            {activeStakeholder === "FARMER" && (
              <div className="grid grid-cols-1 md:grid-cols-2 gap-8 items-center">
                <div className="space-y-4">
                  <span className="text-xs font-mono font-bold uppercase text-amber-700 tracking-wider">
                    Dành cho Nông hộ & Hợp tác xã
                  </span>
                  <h3 className="font-display text-2xl sm:text-3xl font-bold text-[#1B2420] dark:text-[#F4EFE6]">
                    Ghi nhận công sức canh tác bền vững
                  </h3>
                  <p className="text-xs text-[#4A5C55] dark:text-[#A3B8AF] leading-relaxed">
                    Nông dân được định danh thửa ruộng trên bản đồ GPS, lưu lại
                    lịch sử canh tác an toàn và nhận đúng giá trị lúa chất lượng
                    cao.
                  </p>
                  <ul className="space-y-2 text-xs text-[#1B2420] dark:text-[#F4EFE6]">
                    <li className="flex items-center gap-2">
                      <CheckCircle
                        size={16}
                        className="text-amber-600 shrink-0"
                        weight="fill"
                      />
                      <span>
                        Phiếu cân điện tử tại bờ ruộng có toạ độ định vị
                      </span>
                    </li>
                    <li className="flex items-center gap-2">
                      <CheckCircle
                        size={16}
                        className="text-amber-600 shrink-0"
                        weight="fill"
                      />
                      <span>
                        Lấy mẫu niêm phong đại diện bảo đảm quyền lợi đôi bên
                      </span>
                    </li>
                  </ul>
                </div>

                <div className="p-6 rounded-2xl bg-white dark:bg-[#131D17] border border-emerald-950/10 space-y-3">
                  <div className="flex items-center justify-between text-xs font-bold border-b pb-2">
                    <span>Thửa ruộng L1 — Cái Bè</span>
                    <span className="text-amber-700 font-mono">
                      1.8 ha · OM5451
                    </span>
                  </div>
                  <p className="text-xs text-[#7A8F87]">
                    Nông hộ Nguyễn Văn An · Đã thu hoạch 50.000 kg lúa tươi đạt
                    chuẩn độ ẩm 26,5%.
                  </p>
                </div>
              </div>
            )}

            {activeStakeholder === "AUDIT" && (
              <div className="grid grid-cols-1 md:grid-cols-2 gap-8 items-center">
                <div className="space-y-4">
                  <span className="text-xs font-mono font-bold uppercase text-purple-700 tracking-wider">
                    Dành cho Cơ quan Kiểm định & Hải quan
                  </span>
                  <h3 className="font-display text-2xl sm:text-3xl font-bold text-[#1B2420] dark:text-[#F4EFE6]">
                    Truy xuất tức thời không thể làm giả
                  </h3>
                  <p className="text-xs text-[#4A5C55] dark:text-[#A3B8AF] leading-relaxed">
                    Dữ liệu lưu giữ bất biến trên sổ cái kỹ thuật số, kèm kết
                    quả thử nghiệm từ phòng thí nghiệm ISO/IEC 17025.
                  </p>
                  <ul className="space-y-2 text-xs text-[#1B2420] dark:text-[#F4EFE6]">
                    <li className="flex items-center gap-2">
                      <CheckCircle
                        size={16}
                        className="text-purple-600 shrink-0"
                        weight="fill"
                      />
                      <span>
                        Liên kết trực tiếp tới chứng thư kiểm nghiệm phân tích
                      </span>
                    </li>
                    <li className="flex items-center gap-2">
                      <CheckCircle
                        size={16}
                        className="text-purple-600 shrink-0"
                        weight="fill"
                      />
                      <span>
                        Truy vết toán học tỷ lệ phần trăm khối lượng nguồn gốc
                      </span>
                    </li>
                  </ul>
                </div>

                <div className="p-6 rounded-2xl bg-white dark:bg-[#131D17] border border-emerald-950/10 space-y-3">
                  <div className="flex items-center justify-between text-xs font-bold border-b pb-2">
                    <span>Chứng thư tuân thủ xuất khẩu</span>
                    <span className="text-purple-700 font-mono">ISO 17025</span>
                  </div>
                  <p className="text-xs text-[#7A8F87]">
                    Chứng thực lô hàng đạt chuẩn kiểm dịch thực vật và kiểm tra
                    dư lượng thuốc trừ sâu theo tiêu chuẩn quốc tế.
                  </p>
                </div>
              </div>
            )}
          </div>
        </div>
      </section>

      {/* =========================================================================
          8. CALL TO ACTION FOOTER BANNER (Rize.farm style)
          ========================================================================= */}
      <section className="py-20 bg-[#2F6B3A] text-white relative overflow-hidden">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 text-center space-y-6 relative z-10">
          <h2 className="font-display text-3xl sm:text-5xl font-bold tracking-tight max-w-2xl mx-auto leading-tight">
            Sẵn sàng chuẩn hóa chuỗi cung ứng lúa gạo của bạn?
          </h2>
          <p className="text-base text-emerald-100 max-w-xl mx-auto leading-relaxed">
            Tham gia chương trình thí điểm tại Tiền Giang và trải nghiệm nền
            tảng sổ cái cân bằng khối lượng đầu tiên tại Việt Nam.
          </p>
          <div className="pt-2 flex flex-wrap items-center justify-center gap-4">
            <Link
              to="/sign-in"
              className="px-8 py-3.5 rounded-lg text-sm font-semibold bg-white text-[#2F6B3A] hover:bg-[#F4EFE6] transition-all shadow-md hover:shadow-lg"
            >
              Đăng nhập hệ thống ngay
            </Link>
          </div>
        </div>
      </section>

      {/* =========================================================================
          9. MODERN ENTERPRISE FOOTER (Rize.farm layout)
          ========================================================================= */}
      <footer className="bg-[#F0F4F1] dark:bg-[#0E1411] text-[#4A5C55] dark:text-[#A3B8AF] text-xs py-16 border-t border-emerald-950/10 dark:border-emerald-900/40 transition-colors">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="grid grid-cols-1 md:grid-cols-4 gap-10 pb-12 border-b border-emerald-950/10 dark:border-emerald-900/30">
            {/* Col 1: Brand */}
            <div className="space-y-4 md:col-span-1">
              <div className="flex items-center">
                <img
                  src="/icons.svg"
                  alt="GrainLedger"
                  className="w-full object-contain"
                />
              </div>
              <p className="text-xs text-[#5A6E66] dark:text-[#7A8F87] leading-relaxed">
                Nền tảng sổ cái lưu giữ theo cân bằng khối lượng (Mass-Balance
                Custody Ledger) phục vụ xuất khẩu lúa gạo sang các thị trường
                cao cấp.
              </p>
              <div className="text-[11px] text-[#5A6E66] dark:text-[#7A8F87]">
                Đề tài FA26SE272 · Nhóm GFA26SE137
              </div>
            </div>

            {/* Col 2: Nền tảng */}
            <div className="space-y-3">
              <h4 className="font-bold text-xs uppercase tracking-wider text-[#1B2420] dark:text-white">
                Công nghệ
              </h4>
              <ul className="space-y-2 text-xs">
                <li>
                  <a
                    href="#tech"
                    className="text-[#4A5C55] dark:text-[#A3B8AF] hover:text-[#2F6B3A] dark:hover:text-white transition-colors"
                  >
                    Sổ cái Cân bằng Khối lượng
                  </a>
                </li>
                <li>
                  <a
                    href="#tech"
                    className="text-[#4A5C55] dark:text-[#A3B8AF] hover:text-[#2F6B3A] dark:hover:text-white transition-colors"
                  >
                    Động cơ MRL & An toàn
                  </a>
                </li>
                <li>
                  <a
                    href="#tech"
                    className="text-[#4A5C55] dark:text-[#A3B8AF] hover:text-[#2F6B3A] dark:hover:text-white transition-colors"
                  >
                    Thu mua Ngoại tuyến Mobile
                  </a>
                </li>
                <li>
                  <a
                    href="#tech"
                    className="text-[#4A5C55] dark:text-[#A3B8AF] hover:text-[#2F6B3A] dark:hover:text-white transition-colors"
                  >
                    Truy vết Hai chiều Đồ thị
                  </a>
                </li>
              </ul>
            </div>

            {/* Col 3: Chuẩn mực */}
            <div className="space-y-3">
              <h4 className="font-bold text-xs uppercase tracking-wider text-[#1B2420] dark:text-white">
                Tiêu chuẩn Xuất khẩu
              </h4>
              <ul className="space-y-2 text-xs">
                <li>
                  <span className="text-[#5A6E66] dark:text-[#7A8F87]">
                    ISO 22095:2020 Mass Balance
                  </span>
                </li>
                <li>
                  <span className="text-[#5A6E66] dark:text-[#7A8F87]">
                    EU EC 396/2005 (Tricyclazole)
                  </span>
                </li>
                <li>
                  <span className="text-[#5A6E66] dark:text-[#7A8F87]">
                    US EPA 40 CFR Part 180
                  </span>
                </li>
                <li>
                  <span className="text-[#5A6E66] dark:text-[#7A8F87]">
                    Japan MHLW Positive List
                  </span>
                </li>
              </ul>
            </div>

            {/* Col 4: Liên hệ & Thử nghiệm */}
            <div className="space-y-3">
              <h4 className="font-bold text-xs uppercase tracking-wider text-[#1B2420] dark:text-white">
                Cơ sở Thí điểm
              </h4>
              <p className="text-xs text-[#5A6E66] dark:text-[#7A8F87] leading-relaxed">
                Nhà máy Xay xát Lúa gạo Tiền Giang, Đồng bằng sông Cửu Long,
                Việt Nam.
              </p>
              <div className="pt-2">
                <Link
                  to="/sign-in"
                  className="inline-flex items-center gap-1.5 text-xs text-[#2F6B3A] dark:text-emerald-400 font-semibold hover:underline"
                >
                  <span>Truy cập Cổng Tài khoản</span>
                  <ArrowRight size={13} />
                </Link>
              </div>
            </div>
          </div>

          <div className="pt-8 flex flex-col sm:flex-row items-center justify-between gap-4 text-[11px] text-[#5A6E66] dark:text-[#7A8F87]">
            <div>GrainLedger © 2026 GFA26SE137. Mọi quyền được bảo lưu.</div>
            <div>
              Tuân thủ nghiêm ngặt tiêu chuẩn bảo toàn khối lượng ISO
              22095:2020.
            </div>
          </div>
        </div>
      </footer>
    </div>
  );
};
