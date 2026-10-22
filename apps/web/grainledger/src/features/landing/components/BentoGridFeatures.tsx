import React from 'react';
import { Scales, ShieldCheck, MapPin, Crosshair } from '@phosphor-icons/react';
import { motion, useReducedMotion } from 'framer-motion';

export const BentoGridFeatures: React.FC = () => {
  const shouldReduceMotion = useReducedMotion();

  const pillars = [
    {
      icon: Scales,
      title: 'Sổ cái Cân bằng Khối lượng',
      desc: 'Áp dụng mô hình Mass Balance ISO 22095. Tổng đầu ra cộng hao hụt phải bằng tổng đầu vào trong phạm vi dung sai (0,5%). Mọi vi phạm bị từ chối ở tầng dữ liệu.',
      rule: 'Quy tắc BR-01 & BR-02',
      color: 'text-[#2F6B3A] dark:text-emerald-400',
      bgGlow: 'from-emerald-500/10 to-transparent',
      tag: 'Bất biến',
    },
    {
      icon: ShieldCheck,
      title: 'Động cơ Dư lượng MRL',
      desc: 'Tự động gán cận bảo thủ 95% cho các lô chưa xét nghiệm, áp dụng hệ số chế biến phân đoạn (gạo nguyên, tấm, cám) và trừ biên an toàn 20% khi duyệt phương án.',
      rule: 'Quy tắc BR-06 & BR-07',
      color: 'text-blue-600 dark:text-blue-400',
      bgGlow: 'from-blue-500/10 to-transparent',
      tag: 'Toán học',
    },
    {
      icon: MapPin,
      title: 'Thu mua Ngoại tuyến Hiện trường',
      desc: 'Ứng dụng web hoạt động hoàn hảo khi mất mạng tại bờ ruộng. Ghi toạ độ thửa đất, khai báo phun thuốc và đồng bộ chống trùng lặp (Idempotent) khi có sóng.',
      rule: 'Quy tắc BR-09 & BR-11',
      color: 'text-amber-600 dark:text-amber-400',
      bgGlow: 'from-amber-500/10 to-transparent',
      tag: 'Offline-First',
    },
    {
      icon: Crosshair,
      title: 'Truy vết & Khóa HOLD',
      desc: 'Ban hành lệnh HOLD tức thì để ngăn chặn xuất xưởng khi phát hiện nguy cơ. Truy vết đồ thị hai chiều và sinh Hồ sơ tuân thủ (Digital Dossier) tự động.',
      rule: 'Quy tắc BR-15 & BR-17',
      color: 'text-[#B5412B] dark:text-[#E05338]',
      bgGlow: 'from-red-500/10 to-transparent',
      tag: 'Cô lập rủi ro',
    },
  ];

  return (
    <section id="tech" className="py-24 lg:py-32 relative">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="text-center max-w-3xl mx-auto mb-20 space-y-3">
          <span className="text-xs font-mono font-bold uppercase tracking-widest text-[#2F6B3A] dark:text-emerald-400">
            Công nghệ độc quyền
          </span>
          <h2 className="font-sans text-3xl sm:text-5xl font-extrabold tracking-[-0.03em] text-ink-primary dark:text-white">
            Cách GrainLedger giải quyết bài toán
          </h2>
          <p className="text-base text-ink-secondary dark:text-ink-tertiary">
            Nền tảng số hoá khép kín từ mảnh ruộng của nông dân đến hồ sơ hải quan của container xuất khẩu.
          </p>
        </div>

        {/* Apple Bento Grid */}
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6">
          {pillars.map((pillar, idx) => {
            const Icon = pillar.icon;
            return (
              <motion.div
                key={pillar.title}
                initial={shouldReduceMotion ? {} : { opacity: 0, y: 16 }}
                whileInView={{ opacity: 1, y: 0 }}
                viewport={{ once: true, margin: '-50px' }}
                transition={{ type: 'spring', stiffness: 300, damping: 25, delay: idx * 0.08 }}
                whileHover={{ y: -6, transition: { duration: 0.2 } }}
                className="relative group p-8 rounded-3xl bg-white dark:bg-[#121215] border border-black/[0.06] dark:border-white/[0.08] shadow-apple-card hover:shadow-apple-hover flex flex-col justify-between overflow-hidden transition-all duration-300"
              >
                {/* Subtle Radial Glow on Top Corner */}
                <div
                  className={`absolute -top-12 -right-12 w-36 h-36 bg-gradient-to-br ${pillar.bgGlow} rounded-full blur-2xl opacity-60 group-hover:opacity-100 transition-opacity duration-300 pointer-events-none`}
                />

                <div className="space-y-6 relative z-10">
                  <div className="flex items-center justify-between">
                    <div className="w-12 h-12 rounded-2xl bg-black/[0.03] dark:bg-white/[0.06] flex items-center justify-center transition-transform duration-300 group-hover:scale-110">
                      <Icon size={26} weight="bold" className={pillar.color} />
                    </div>
                    <span className="text-[10px] font-mono uppercase tracking-wider font-bold px-2.5 py-1 rounded-full bg-black/[0.03] dark:bg-white/[0.06] text-ink-tertiary">
                      {pillar.tag}
                    </span>
                  </div>

                  <div className="space-y-2">
                    <h3 className="font-sans font-bold text-xl text-ink-primary dark:text-white tracking-[-0.015em]">
                      {pillar.title}
                    </h3>
                    <p className="text-xs text-ink-secondary dark:text-ink-tertiary leading-relaxed">
                      {pillar.desc}
                    </p>
                  </div>
                </div>

                <div className="pt-6 mt-6 border-t border-black/[0.04] dark:border-white/[0.06] relative z-10">
                  <span className={`text-[11px] font-mono font-bold ${pillar.color}`}>
                    {pillar.rule}
                  </span>
                </div>
              </motion.div>
            );
          })}
        </div>
      </div>
    </section>
  );
};
