import React from 'react';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useUiStore } from '@/app/store/uiStore';
import {
  ShieldCheck,
  ArrowRight,
  Scales,
  Certificate,
  CheckCircle,
  Compass,
  Sparkle,
} from '@phosphor-icons/react';
import { motion, useReducedMotion } from 'framer-motion';

export const HeroSection: React.FC = () => {
  const { t } = useTranslation();
  const { language } = useUiStore();
  const shouldReduceMotion = useReducedMotion();

  return (
    <section className="relative overflow-hidden pt-12 pb-20 lg:pt-24 lg:pb-32">
      {/* Apple-style Cinematic Ambient Radial Glows */}
      <div className="absolute top-1/3 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[800px] h-[450px] bg-gradient-to-tr from-emerald-500/15 via-[#2F6B3A]/10 to-transparent blur-3xl pointer-events-none rounded-full" />
      <div className="absolute top-1/4 right-1/4 w-[400px] h-[300px] bg-blue-500/10 blur-3xl pointer-events-none rounded-full" />

      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 relative z-10">
        <div className="max-w-4xl mx-auto text-center space-y-7">
          {/* Top Apple-style Pill Tag */}
          <motion.div
            initial={shouldReduceMotion ? {} : { opacity: 0, y: -10 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ type: 'spring', stiffness: 300, damping: 25 }}
            className="inline-flex items-center gap-2 px-4 py-1.5 rounded-full text-xs font-semibold bg-emerald-500/10 dark:bg-emerald-950/50 text-[#2F6B3A] dark:text-emerald-300 border border-emerald-500/20 backdrop-blur-md shadow-xs"
          >
            <ShieldCheck size={16} weight="bold" />
            <span>
              {language === 'vi'
                ? 'Dự án Thí điểm Nhà máy Tiền Giang · Chuẩn Quốc tế ISO 22095:2020'
                : 'Tiền Giang Mill Pilot Project · ISO 22095:2020 Standard'}
            </span>
          </motion.div>

          {/* Large Headline with Negative Tracking */}
          <motion.h1
            initial={shouldReduceMotion ? {} : { opacity: 0, y: 16 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ type: 'spring', stiffness: 280, damping: 26, delay: 0.1 }}
            className="font-sans text-4xl sm:text-6xl lg:text-7xl font-extrabold tracking-[-0.035em] text-ink-primary dark:text-white leading-[1.08]"
          >
            {t(
              'landing.heroHeadline',
              'Hạ tầng số cho xuất khẩu lúa gạo tương lai.'
            )}
          </motion.h1>

          {/* Subtitle */}
          <motion.p
            initial={shouldReduceMotion ? {} : { opacity: 0, y: 14 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ type: 'spring', stiffness: 280, damping: 26, delay: 0.2 }}
            className="text-base sm:text-xl text-ink-secondary dark:text-ink-tertiary leading-relaxed max-w-2xl mx-auto font-normal"
          >
            {t(
              'landing.heroSubheadline',
              'GrainLedger giải quyết bài toán cốt lõi của chuỗi lúa gạo: Ghi nhận chính xác tỷ lệ đóng góp của từng nông hộ nhỏ vào silo chung và kiểm soát dư lượng thuốc bảo vệ thực vật trước khi container cập cảng EU, Mỹ, Nhật Bản.'
            )}
          </motion.p>

          {/* CTAs */}
          <motion.div
            initial={shouldReduceMotion ? {} : { opacity: 0, y: 14 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ type: 'spring', stiffness: 280, damping: 26, delay: 0.3 }}
            className="pt-2 flex flex-wrap items-center justify-center gap-4"
          >
            <motion.div whileHover={{ scale: 1.02 }} whileTap={{ scale: 0.98 }}>
              <Link
                to="/sign-in"
                className="inline-flex items-center gap-2.5 px-8 py-3.5 rounded-full text-sm font-semibold bg-[#2F6B3A] text-white hover:bg-[#25562e] transition-all shadow-[0_4px_14px_0_rgba(47,107,58,0.39)]"
              >
                <span>{t('landing.ctaSignIn', 'Vào bàn làm việc vận hành')}</span>
                <ArrowRight size={16} weight="bold" />
              </Link>
            </motion.div>

            <motion.div whileHover={{ scale: 1.02 }} whileTap={{ scale: 0.98 }}>
              <a
                href="#simulators"
                className="inline-flex items-center gap-2.5 px-7 py-3.5 rounded-full text-sm font-semibold bg-white/80 dark:bg-white/10 hover:bg-white dark:hover:bg-white/15 border border-black/[0.08] dark:border-white/[0.12] text-ink-primary dark:text-white backdrop-blur-md transition-all shadow-xs"
              >
                <Scales size={18} className="text-[#2F6B3A] dark:text-emerald-400" />
                <span>
                  {language === 'vi'
                    ? 'Trải nghiệm mô phỏng Sổ cái'
                    : 'Experience Ledger Simulator'}
                </span>
              </a>
            </motion.div>
          </motion.div>
        </div>

        {/* Regulatory & Standards Bar (Apple Glass Chips) */}
        <div className="mt-16 pt-10 border-t border-black/[0.06] dark:border-white/[0.08] text-center">
          <span className="text-[11px] font-mono uppercase tracking-wider text-ink-tertiary dark:text-ink-tertiary block mb-6 font-semibold">
            Tuân thủ các quy định & tiêu chuẩn kiểm định quốc tế hàng đầu
          </span>

          <div className="flex flex-wrap items-center justify-center gap-3 sm:gap-4 text-xs font-semibold">
            <div className="flex items-center gap-2 px-4 py-2 rounded-xl bg-white/60 dark:bg-white/[0.05] border border-black/[0.05] dark:border-white/[0.08] backdrop-blur-md shadow-xs text-ink-secondary dark:text-ink-secondary">
              <Certificate size={18} className="text-[#2F6B3A] dark:text-emerald-400" />
              <span>ISO 22095:2020 Mass Balance</span>
            </div>
            <div className="flex items-center gap-2 px-4 py-2 rounded-xl bg-white/60 dark:bg-white/[0.05] border border-black/[0.05] dark:border-white/[0.08] backdrop-blur-md shadow-xs text-ink-secondary dark:text-ink-secondary">
              <ShieldCheck size={18} className="text-blue-500" />
              <span>EU Regulation (EC) No 396/2005</span>
            </div>
            <div className="flex items-center gap-2 px-4 py-2 rounded-xl bg-white/60 dark:bg-white/[0.05] border border-black/[0.05] dark:border-white/[0.08] backdrop-blur-md shadow-xs text-ink-secondary dark:text-ink-secondary">
              <CheckCircle size={18} className="text-emerald-500" />
              <span>USA EPA 40 CFR Part 180</span>
            </div>
            <div className="flex items-center gap-2 px-4 py-2 rounded-xl bg-white/60 dark:bg-white/[0.05] border border-black/[0.05] dark:border-white/[0.08] backdrop-blur-md shadow-xs text-ink-secondary dark:text-ink-secondary">
              <Compass size={18} className="text-amber-500" />
              <span>Japan MHLW/CAA Positive List</span>
            </div>
            <div className="flex items-center gap-2 px-4 py-2 rounded-xl bg-white/60 dark:bg-white/[0.05] border border-black/[0.05] dark:border-white/[0.08] backdrop-blur-md shadow-xs text-ink-secondary dark:text-ink-secondary">
              <Sparkle size={18} className="text-purple-500" />
              <span>TCVN 11888:2017 & TT 50/2016</span>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
};
