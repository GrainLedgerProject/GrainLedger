import React from 'react';
import { Link } from 'react-router-dom';
import { ArrowRight } from '@phosphor-icons/react';
import { motion } from 'framer-motion';

export const LandingCtaFooter: React.FC = () => {
  return (
    <>
      {/* Apple-grade CTA Banner */}
      <section className="py-24 bg-[#1B2420] dark:bg-black text-white relative overflow-hidden border-t border-black/[0.06] dark:border-white/[0.08]">
        {/* Subtle Ambient Glow */}
        <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[700px] h-[350px] bg-[#2F6B3A]/25 blur-3xl pointer-events-none rounded-full" />

        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 text-center space-y-6 relative z-10">
          <h2 className="font-sans text-3xl sm:text-5xl font-extrabold tracking-[-0.035em] max-w-2xl mx-auto leading-tight">
            Sẵn sàng chuẩn hóa chuỗi cung ứng lúa gạo của bạn?
          </h2>
          <p className="text-base text-white/70 max-w-xl mx-auto leading-relaxed font-normal">
            Tham gia chương trình thí điểm tại Tiền Giang và trải nghiệm nền tảng sổ cái cân bằng khối lượng đầu tiên tại Việt Nam.
          </p>
          <div className="pt-3 flex flex-wrap items-center justify-center gap-4">
            <motion.div whileHover={{ scale: 1.02 }} whileTap={{ scale: 0.98 }}>
              <Link
                to="/sign-in"
                className="inline-flex items-center gap-2 px-8 py-3.5 rounded-full text-sm font-semibold bg-white text-[#1B2420] hover:bg-white/95 shadow-lg transition-all"
              >
                <span>Đăng nhập hệ thống ngay</span>
                <ArrowRight size={15} weight="bold" />
              </Link>
            </motion.div>
          </div>
        </div>
      </section>

      {/* Modern Apple Minimal Footer */}
      <footer className="bg-white dark:bg-[#0A0A0C] text-ink-secondary dark:text-ink-tertiary text-xs py-16 border-t border-black/[0.06] dark:border-white/[0.08] transition-colors">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="grid grid-cols-1 md:grid-cols-4 gap-10 pb-12 border-b border-black/[0.06] dark:border-white/[0.08]">
            {/* Col 1: Brand */}
            <div className="space-y-4 md:col-span-1">
              <div className="flex items-center h-8">
                <img
                  src="/icons.svg"
                  alt="GrainLedger"
                  className="h-8 w-auto object-contain"
                />
              </div>
              <p className="text-xs text-ink-tertiary leading-relaxed">
                Nền tảng sổ cái lưu giữ theo cân bằng khối lượng (Mass-Balance Custody Ledger) phục vụ xuất khẩu lúa gạo sang các thị trường cao cấp.
              </p>
              <div className="text-[11px] font-mono text-ink-tertiary">
                Đề tài FA26SE272 · Nhóm GFA26SE137
              </div>
            </div>

            {/* Col 2: Nền tảng */}
            <div className="space-y-3">
              <h4 className="font-bold text-xs uppercase tracking-wider text-ink-primary dark:text-white">
                Công nghệ
              </h4>
              <ul className="space-y-2 text-xs">
                <li>
                  <a
                    href="#tech"
                    className="hover:text-ink-primary dark:hover:text-white transition-colors"
                  >
                    Sổ cái Cân bằng Khối lượng
                  </a>
                </li>
                <li>
                  <a
                    href="#tech"
                    className="hover:text-ink-primary dark:hover:text-white transition-colors"
                  >
                    Động cơ MRL & An toàn
                  </a>
                </li>
                <li>
                  <a
                    href="#tech"
                    className="hover:text-ink-primary dark:hover:text-white transition-colors"
                  >
                    Thu mua Ngoại tuyến Mobile
                  </a>
                </li>
                <li>
                  <a
                    href="#tech"
                    className="hover:text-ink-primary dark:hover:text-white transition-colors"
                  >
                    Truy vết Hai chiều Đồ thị
                  </a>
                </li>
              </ul>
            </div>

            {/* Col 3: Chuẩn mực */}
            <div className="space-y-3">
              <h4 className="font-bold text-xs uppercase tracking-wider text-ink-primary dark:text-white">
                Tiêu chuẩn Xuất khẩu
              </h4>
              <ul className="space-y-2 text-xs">
                <li>
                  <span className="text-ink-tertiary">
                    ISO 22095:2020 Mass Balance
                  </span>
                </li>
                <li>
                  <span className="text-ink-tertiary">
                    EU EC 396/2005 (Tricyclazole)
                  </span>
                </li>
                <li>
                  <span className="text-ink-tertiary">
                    US EPA 40 CFR Part 180
                  </span>
                </li>
                <li>
                  <span className="text-ink-tertiary">
                    Japan MHLW Positive List
                  </span>
                </li>
              </ul>
            </div>

            {/* Col 4: Cơ sở */}
            <div className="space-y-3">
              <h4 className="font-bold text-xs uppercase tracking-wider text-ink-primary dark:text-white">
                Cơ sở Thí điểm
              </h4>
              <p className="text-xs text-ink-tertiary leading-relaxed">
                Nhà máy Xay xát Lúa gạo Tiền Giang, Đồng bằng sông Cửu Long, Việt Nam.
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

          <div className="pt-8 flex flex-col sm:flex-row items-center justify-between gap-4 text-[11px] text-ink-tertiary">
            <div>GrainLedger © 2026 GFA26SE137. Mọi quyền được bảo lưu.</div>
            <div>
              Tuân thủ nghiêm ngặt tiêu chuẩn bảo toàn khối lượng ISO 22095:2020.
            </div>
          </div>
        </div>
      </footer>
    </>
  );
};
