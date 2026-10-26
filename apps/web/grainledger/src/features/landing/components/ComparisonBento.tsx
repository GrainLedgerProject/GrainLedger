import React from 'react';
import { motion, useReducedMotion } from 'framer-motion';
import { XCircle, CheckCircle } from '@phosphor-icons/react';

export const ComparisonBento: React.FC = () => {
  const shouldReduceMotion = useReducedMotion();

  const comparisons = [
    {
      criteria: 'Trộn lúa trong Silo',
      oldWay: 'Mất hoàn toàn dấu vết nguồn gốc sau khi đổ lúa của nhiều nông dân vào silo chung.',
      newWay: 'Bảo toàn tỷ lệ khối lượng: Xác định chính xác từng thửa ruộng đóng góp bao nhiêu % vào mẻ gạo xuất khẩu.',
    },
    {
      criteria: 'Khi có kết quả Lab xấu',
      oldWay: 'Không biết container nào bị nhiễm; phải phong toả toàn bộ kho hoặc đối mặt rủi ro bị nước nhập khẩu phạt.',
      newWay: 'Truy vết đồ thị trong 30s: Chỉ ra chính xác các container bị ảnh hưởng và khối lượng kg nhiễm để cách ly tức thời.',
    },
    {
      criteria: 'Kiểm soát hao hụt & Thất thoát',
      oldWay: 'Dễ khai khống, gõ nhầm hoặc tạo số liệu ảo mà hệ thống sổ sách không ngăn chặn được.',
      newWay: 'Từ chối tuyệt đối: Giao dịch không cân bằng theo dung sai 0,5% bị huỷ ngay lập tức ở tầng dữ liệu.',
    },
    {
      criteria: 'Duyệt xuất khẩu MRL',
      oldWay: 'Phỏng đoán cảm tính, phụ thuộc vào kinh nghiệm người phối trộn gạo.',
      newWay: 'Tính toán toán học bảo thủ: Kiểm tra nồng độ theo phiên bản MRL của thị trường đích kèm biên an toàn 20%.',
    },
  ];

  return (
    <section
      id="comparison"
      className="py-24 bg-surface-subtle dark:bg-[#0A0A0C] border-y border-black/[0.06] dark:border-white/[0.08]"
    >
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="text-center max-w-3xl mx-auto mb-16 space-y-3">
          <span className="text-xs font-mono font-bold uppercase tracking-widest text-[#2F6B3A] dark:text-emerald-400">
            Đối chiếu giải pháp
          </span>
          <h2 className="font-sans text-3xl sm:text-4xl font-extrabold tracking-[-0.03em] text-ink-primary dark:text-white">
            Mô hình truyền thống so với Sổ cái GrainLedger
          </h2>
          <p className="text-sm text-ink-secondary dark:text-ink-tertiary">
            Sự khác biệt cốt lõi giữa quản lý thủ công truyền thống và kiến trúc sổ cái kế toán bảo toàn khối lượng.
          </p>
        </div>

        {/* Apple-grade Comparison Cards */}
        <div className="space-y-4 max-w-5xl mx-auto">
          {comparisons.map((item, idx) => (
            <motion.div
              key={item.criteria}
              initial={shouldReduceMotion ? {} : { opacity: 0, y: 12 }}
              whileInView={{ opacity: 1, y: 0 }}
              viewport={{ once: true, margin: '-40px' }}
              transition={{ type: 'spring', stiffness: 320, damping: 26, delay: idx * 0.06 }}
              className="p-6 sm:p-7 rounded-3xl bg-white dark:bg-[#121215] border border-black/[0.06] dark:border-white/[0.08] shadow-apple-card grid grid-cols-1 md:grid-cols-12 gap-5 items-center"
            >
              <div className="md:col-span-3">
                <span className="text-[11px] font-mono uppercase tracking-wider text-ink-tertiary block mb-1">
                  Tiêu chí
                </span>
                <h4 className="font-sans font-bold text-base text-ink-primary dark:text-white">
                  {item.criteria}
                </h4>
              </div>

              {/* Old Way */}
              <div className="md:col-span-4 p-4 rounded-2xl bg-red-500/[0.04] border border-red-500/10 space-y-1.5">
                <div className="flex items-center gap-1.5 text-xs font-bold text-[#B5412B] dark:text-[#E05338]">
                  <XCircle size={15} weight="fill" />
                  <span>Cách làm cũ (Sổ & Excel)</span>
                </div>
                <p className="text-xs text-ink-secondary dark:text-ink-tertiary leading-relaxed">
                  {item.oldWay}
                </p>
              </div>

              {/* GrainLedger Way */}
              <div className="md:col-span-5 p-4 rounded-2xl bg-emerald-500/[0.04] border border-emerald-500/20 space-y-1.5 shadow-[0_0_0_1px_rgba(47,107,58,0.06)_inset]">
                <div className="flex items-center gap-1.5 text-xs font-bold text-[#2F6B3A] dark:text-emerald-400">
                  <CheckCircle size={15} weight="fill" />
                  <span>GrainLedger (Mass Balance)</span>
                </div>
                <p className="text-xs text-ink-primary dark:text-white font-medium leading-relaxed">
                  {item.newWay}
                </p>
              </div>
            </motion.div>
          ))}
        </div>
      </div>
    </section>
  );
};
