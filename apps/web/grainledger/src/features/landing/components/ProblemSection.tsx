import React from 'react';
import { motion, useReducedMotion } from 'framer-motion';

export const ProblemSection: React.FC = () => {
  const shouldReduceMotion = useReducedMotion();

  const cards = [
    {
      metric: '8,06M',
      title: 'Tấn gạo xuất khẩu / năm',
      desc: 'Giá trị kim ngạch hơn 4,1 tỷ USD của Việt Nam đòi hỏi chứng minh nguồn gốc minh bạch với đối tác toàn cầu.',
      color: 'text-ink-primary dark:text-white',
      accent: 'border-black/[0.06] dark:border-white/[0.08]',
      badge: 'Sản lượng',
    },
    {
      metric: '0,01',
      unit: 'mg/kg',
      title: 'Ngưỡng MRL EU (LOQ)',
      desc: 'Quy định khắt khe của EU với hoạt chất Tricyclazole — chỉ một mẻ nhiễm bẩn có thể khiến toàn bộ container bị từ chối cập cảng.',
      color: 'text-[#B5412B] dark:text-[#E05338]',
      accent: 'border-red-500/20 bg-red-500/[0.02]',
      badge: 'Rủi ro MRL',
    },
    {
      metric: '100%',
      title: 'Khối lượng được bảo toàn',
      desc: 'Sổ cái cân bằng kép từ chối dứt khoát mọi mẻ chế biến sai lệch ngoài dung sai 0,5% — không cho phép gian lận hoặc double-spending.',
      color: 'text-[#2F6B3A] dark:text-emerald-400',
      accent: 'border-emerald-500/20 bg-emerald-500/[0.02]',
      badge: 'Mass Balance',
    },
    {
      metric: '< 30s',
      title: 'Truy vết đồ thị hai chiều',
      desc: 'Định vị chính xác từ lọ mẫu lab đến container xuất khẩu, và từ container ngược về danh sách nông hộ kèm tỷ lệ % khối lượng.',
      color: 'text-blue-600 dark:text-blue-400',
      accent: 'border-blue-500/20 bg-blue-500/[0.02]',
      badge: 'Đồ thị truy vết',
    },
  ];

  return (
    <section
      id="problem"
      className="py-24 bg-surface-subtle dark:bg-[#0A0A0C] border-y border-black/[0.06] dark:border-white/[0.08]"
    >
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="max-w-3xl mb-16 space-y-3">
          <span className="text-xs font-mono font-bold uppercase tracking-widest text-[#B5412B] dark:text-[#E05338]">
            Bối cảnh thực tế & Thách thức
          </span>
          <h2 className="font-sans text-3xl sm:text-5xl font-extrabold tracking-[-0.03em] text-ink-primary dark:text-white leading-tight">
            Xuất khẩu gạo là bài toán truy vết & rủi ro dư lượng lớn nhất ĐBSCL.
          </h2>
          <p className="text-base text-ink-secondary dark:text-ink-tertiary leading-relaxed pt-1">
            Một container gạo xuất khẩu là sự hòa trộn của hàng trăm thửa ruộng nhỏ sau khi đưa vào lò sấy và silo công nghiệp. Khi một kết quả kiểm nghiệm xấu về muộn, các phương pháp quản lý bằng giấy hoặc Excel buộc doanh nghiệp phải dừng toàn bộ kho hoặc đối mặt nguy cơ trả hàng triệu USD.
          </p>
        </div>

        {/* 4 Apple Bento Metric Cards */}
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-6">
          {cards.map((card, idx) => (
            <motion.div
              key={card.title}
              initial={shouldReduceMotion ? {} : { opacity: 0, y: 16 }}
              whileInView={{ opacity: 1, y: 0 }}
              viewport={{ once: true, margin: '-50px' }}
              transition={{ type: 'spring', stiffness: 300, damping: 25, delay: idx * 0.08 }}
              whileHover={{ y: -4, transition: { duration: 0.2 } }}
              className={`p-8 rounded-3xl bg-white dark:bg-[#121215] border ${card.accent} shadow-apple-card backdrop-blur-md space-y-4 flex flex-col justify-between transition-all duration-300`}
            >
              <div className="space-y-2">
                <div className="flex items-center justify-between">
                  <span className="text-[11px] font-mono uppercase font-semibold text-ink-tertiary dark:text-ink-tertiary">
                    {card.badge}
                  </span>
                </div>
                <div className="flex items-baseline gap-1">
                  <span className={`font-sans text-5xl font-extrabold tracking-[-0.04em] ${card.color}`}>
                    {card.metric}
                  </span>
                  {card.unit && (
                    <span className="text-xs font-mono font-semibold text-ink-tertiary">
                      {card.unit}
                    </span>
                  )}
                </div>
              </div>

              <div className="space-y-1.5 pt-4 border-t border-black/[0.04] dark:border-white/[0.06]">
                <h4 className="font-semibold text-sm text-ink-primary dark:text-white">
                  {card.title}
                </h4>
                <p className="text-xs text-ink-tertiary leading-relaxed">
                  {card.desc}
                </p>
              </div>
            </motion.div>
          ))}
        </div>
      </div>
    </section>
  );
};
