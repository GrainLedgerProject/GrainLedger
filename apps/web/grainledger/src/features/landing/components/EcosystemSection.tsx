import React, { useState } from 'react';
import { Factory, Truck, Users, Certificate, CheckCircle } from '@phosphor-icons/react';
import { motion, AnimatePresence } from 'framer-motion';

export const EcosystemSection: React.FC = () => {
  const [activeStakeholder, setActiveStakeholder] = useState<
    'MILL' | 'EXPORTER' | 'FARMER' | 'AUDIT'
  >('MILL');

  const tabs = [
    { id: 'MILL', label: 'Nhà máy Xay xát & Sấy lúa', icon: Factory },
    { id: 'EXPORTER', label: 'Doanh nghiệp Xuất khẩu', icon: Truck },
    { id: 'FARMER', label: 'Nông hộ & Tổ hợp tác', icon: Users },
    { id: 'AUDIT', label: 'Cơ quan Kiểm định & Hải quan', icon: Certificate },
  ];

  return (
    <section id="ecosystem" className="py-24 lg:py-32 bg-surface-subtle dark:bg-[#0A0A0C] border-y border-black/[0.06] dark:border-white/[0.08]">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="text-center max-w-3xl mx-auto mb-16 space-y-3">
          <span className="text-xs font-mono font-bold uppercase tracking-widest text-[#2F6B3A] dark:text-emerald-400">
            Hệ sinh thái liên kết
          </span>
          <h2 className="font-sans text-3xl sm:text-5xl font-extrabold tracking-[-0.03em] text-ink-primary dark:text-white">
            Phục vụ toàn diện các bên trong chuỗi giá trị
          </h2>
          <p className="text-sm text-ink-secondary dark:text-ink-tertiary">
            Mỗi đối tác trong chuỗi lúa gạo sở hữu một trải nghiệm số hóa tinh gọn, phù hợp đặc thù vận hành.
          </p>
        </div>

        {/* Stakeholder Category Pills */}
        <div className="flex flex-wrap items-center justify-center gap-2 mb-12">
          {tabs.map((tab) => {
            const Icon = tab.icon;
            const isActive = activeStakeholder === tab.id;
            return (
              <button
                key={tab.id}
                type="button"
                onClick={() => setActiveStakeholder(tab.id as 'MILL' | 'EXPORTER' | 'FARMER' | 'AUDIT')}
                className={`relative flex items-center gap-2 px-5 py-2.5 rounded-full text-xs font-semibold transition-all duration-200 ${
                  isActive
                    ? 'text-white dark:text-black'
                    : 'text-ink-secondary dark:text-ink-tertiary hover:text-ink-primary bg-white/70 dark:bg-white/[0.05] border border-black/[0.06] dark:border-white/[0.08]'
                }`}
              >
                {isActive && (
                  <motion.div
                    layoutId="stakeholderPillHighlight"
                    className="absolute inset-0 bg-[#1B2420] dark:bg-white rounded-full shadow-md"
                    transition={{ type: 'spring', stiffness: 400, damping: 30 }}
                  />
                )}
                <span className="relative z-10 flex items-center gap-2">
                  <Icon size={16} weight={isActive ? 'bold' : 'regular'} />
                  <span>{tab.label}</span>
                </span>
              </button>
            );
          })}
        </div>

        {/* Stakeholder Details Card */}
        <div className="bg-white dark:bg-[#121215] border border-black/[0.06] dark:border-white/[0.08] rounded-3xl p-8 sm:p-12 shadow-apple-card max-w-5xl mx-auto">
          <AnimatePresence mode="wait">
            {activeStakeholder === 'MILL' && (
              <motion.div
                key="mill"
                initial={{ opacity: 0, y: 10 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: -10 }}
                transition={{ duration: 0.2 }}
                className="grid grid-cols-1 md:grid-cols-2 gap-10 items-center"
              >
                <div className="space-y-5">
                  <span className="text-xs font-mono font-bold uppercase text-[#2F6B3A] dark:text-emerald-400 tracking-wider">
                    Dành cho Ban Điều hành Nhà máy
                  </span>
                  <h3 className="font-sans text-2xl sm:text-3xl font-bold text-ink-primary dark:text-white tracking-[-0.02em]">
                    Minh bạch sản lượng sấy, xay xát và lưu trữ silo
                  </h3>
                  <p className="text-xs text-ink-secondary dark:text-ink-tertiary leading-relaxed">
                    Cung cấp dịch vụ sấy, silo và xay xát cho nhiều công ty xuất khẩu mà không lo tranh chấp về hao hụt ẩm hay tỷ lệ thu hồi thành phẩm, tấm và cám.
                  </p>
                  <ul className="space-y-3 text-xs text-ink-primary dark:text-white">
                    <li className="flex items-center gap-2.5">
                      <CheckCircle size={17} className="text-[#2F6B3A] dark:text-emerald-400 shrink-0" weight="fill" />
                      <span>Hạch toán hao hụt theo từng mẻ máy có số đo cân đối chiếu</span>
                    </li>
                    <li className="flex items-center gap-2.5">
                      <CheckCircle size={17} className="text-[#2F6B3A] dark:text-emerald-400 shrink-0" weight="fill" />
                      <span>Cách ly tuyệt đối lô hàng của từng công ty khách hàng</span>
                    </li>
                    <li className="flex items-center gap-2.5">
                      <CheckCircle size={17} className="text-[#2F6B3A] dark:text-emerald-400 shrink-0" weight="fill" />
                      <span>Xử lý bút toán bù chuẩn mực khi có hiệu chỉnh số cân</span>
                    </li>
                  </ul>
                </div>

                <div className="p-7 rounded-2xl bg-black/[0.02] dark:bg-white/[0.04] border border-black/[0.06] dark:border-white/[0.08] space-y-4">
                  <div className="flex items-center justify-between text-xs font-bold border-b border-black/[0.06] dark:border-white/[0.08] pb-3">
                    <span className="text-ink-primary dark:text-white">Mẫu báo cáo mẻ xay xát S1</span>
                    <span className="text-[#2F6B3A] dark:text-emerald-400 font-mono">100% Cân bằng</span>
                  </div>
                  <div className="space-y-2.5 text-xs">
                    <div className="flex justify-between">
                      <span className="text-ink-tertiary">Thóc khô đưa vào:</span>
                      <span className="font-mono font-bold text-ink-primary dark:text-white">88.000,000 kg</span>
                    </div>
                    <div className="flex justify-between">
                      <span className="text-ink-tertiary">Gạo nguyên thu hồi:</span>
                      <span className="font-mono font-bold text-[#2F6B3A] dark:text-emerald-400">58.000,000 kg (65.9%)</span>
                    </div>
                    <div className="flex justify-between">
                      <span className="text-ink-tertiary">Tấm + Cám + Trấu:</span>
                      <span className="font-mono text-ink-secondary dark:text-ink-tertiary">29.700,000 kg (33.8%)</span>
                    </div>
                    <div className="flex justify-between border-t border-black/[0.06] dark:border-white/[0.08] pt-2">
                      <span className="text-ink-tertiary">Hao hụt máy:</span>
                      <span className="font-mono text-ink-secondary dark:text-ink-tertiary">300,000 kg (0.3%)</span>
                    </div>
                  </div>
                </div>
              </motion.div>
            )}

            {activeStakeholder === 'EXPORTER' && (
              <motion.div
                key="exporter"
                initial={{ opacity: 0, y: 10 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: -10 }}
                transition={{ duration: 0.2 }}
                className="grid grid-cols-1 md:grid-cols-2 gap-10 items-center"
              >
                <div className="space-y-5">
                  <span className="text-xs font-mono font-bold uppercase text-blue-600 dark:text-blue-400 tracking-wider">
                    Dành cho Doanh nghiệp Xuất khẩu
                  </span>
                  <h3 className="font-sans text-2xl sm:text-3xl font-bold text-ink-primary dark:text-white tracking-[-0.02em]">
                    Bảo đảm từng container đạt chuẩn thị trường đích
                  </h3>
                  <p className="text-xs text-ink-secondary dark:text-ink-tertiary leading-relaxed">
                    Tối ưu hóa các mẻ phối trộn đạt tiêu chuẩn tỷ lệ tấm, độ ẩm và dư lượng hoạt chất dưới ngưỡng MRL mà không lo bị từ chối đơn hàng.
                  </p>
                  <ul className="space-y-3 text-xs text-ink-primary dark:text-white">
                    <li className="flex items-center gap-2.5">
                      <CheckCircle size={17} className="text-blue-600 dark:text-blue-400 shrink-0" weight="fill" />
                      <span>Thuật toán đề xuất mẻ trộn tối ưu lợi nhuận và an toàn MRL</span>
                    </li>
                    <li className="flex items-center gap-2.5">
                      <CheckCircle size={17} className="text-blue-600 dark:text-blue-400 shrink-0" weight="fill" />
                      <span>Xuất hồ sơ tuân thủ kèm QR truy xuất nguồn gốc số hoá</span>
                    </li>
                    <li className="flex items-center gap-2.5">
                      <CheckCircle size={17} className="text-blue-600 dark:text-blue-400 shrink-0" weight="fill" />
                      <span>Giám sát tiến độ đóng niêm container trực tiếp thời gian thực</span>
                    </li>
                  </ul>
                </div>

                <div className="p-7 rounded-2xl bg-black/[0.02] dark:bg-white/[0.04] border border-black/[0.06] dark:border-white/[0.08] space-y-4">
                  <div className="flex items-center justify-between text-xs font-bold border-b border-black/[0.06] dark:border-white/[0.08] pb-3">
                    <span className="text-ink-primary dark:text-white">Hồ sơ xuất xưởng Container C1</span>
                    <span className="text-blue-600 dark:text-blue-400 font-mono">Đạt chuẩn EU</span>
                  </div>
                  <div className="space-y-2.5 text-xs">
                    <div className="flex justify-between">
                      <span className="text-ink-tertiary">Thị trường đích:</span>
                      <span className="font-bold text-ink-primary dark:text-white">Châu Âu (Regulation EC 396)</span>
                    </div>
                    <div className="flex justify-between">
                      <span className="text-ink-tertiary">Khối lượng tịnh:</span>
                      <span className="font-mono font-bold text-ink-primary dark:text-white">25.000,000 kg</span>
                    </div>
                    <div className="flex justify-between">
                      <span className="text-ink-tertiary">Số niêm chì:</span>
                      <span className="font-mono text-ink-secondary dark:text-ink-tertiary">GL-SEAL-8891</span>
                    </div>
                  </div>
                </div>
              </motion.div>
            )}

            {activeStakeholder === 'FARMER' && (
              <motion.div
                key="farmer"
                initial={{ opacity: 0, y: 10 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: -10 }}
                transition={{ duration: 0.2 }}
                className="grid grid-cols-1 md:grid-cols-2 gap-10 items-center"
              >
                <div className="space-y-5">
                  <span className="text-xs font-mono font-bold uppercase text-amber-600 dark:text-amber-400 tracking-wider">
                    Dành cho Nông hộ & Hợp tác xã
                  </span>
                  <h3 className="font-sans text-2xl sm:text-3xl font-bold text-ink-primary dark:text-white tracking-[-0.02em]">
                    Ghi nhận công sức canh tác bền vững
                  </h3>
                  <p className="text-xs text-ink-secondary dark:text-ink-tertiary leading-relaxed">
                    Nông dân được định danh thửa ruộng trên bản đồ GPS, lưu lại lịch sử canh tác an toàn và nhận đúng giá trị lúa chất lượng cao.
                  </p>
                  <ul className="space-y-3 text-xs text-ink-primary dark:text-white">
                    <li className="flex items-center gap-2.5">
                      <CheckCircle size={17} className="text-amber-600 dark:text-amber-400 shrink-0" weight="fill" />
                      <span>Phiếu cân điện tử tại bờ ruộng có toạ độ định vị</span>
                    </li>
                    <li className="flex items-center gap-2.5">
                      <CheckCircle size={17} className="text-amber-600 dark:text-amber-400 shrink-0" weight="fill" />
                      <span>Lấy mẫu niêm phong đại diện bảo đảm quyền lợi đôi bên</span>
                    </li>
                  </ul>
                </div>

                <div className="p-7 rounded-2xl bg-black/[0.02] dark:bg-white/[0.04] border border-black/[0.06] dark:border-white/[0.08] space-y-3">
                  <div className="flex items-center justify-between text-xs font-bold border-b border-black/[0.06] dark:border-white/[0.08] pb-3">
                    <span className="text-ink-primary dark:text-white">Thửa ruộng L1 — Cái Bè</span>
                    <span className="text-amber-600 dark:text-amber-400 font-mono">1.8 ha · OM5451</span>
                  </div>
                  <p className="text-xs text-ink-secondary dark:text-ink-tertiary leading-relaxed">
                    Nông hộ Nguyễn Văn An · Đã thu hoạch 50.000 kg lúa tươi đạt chuẩn độ ẩm 26,5%.
                  </p>
                </div>
              </motion.div>
            )}

            {activeStakeholder === 'AUDIT' && (
              <motion.div
                key="audit"
                initial={{ opacity: 0, y: 10 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: -10 }}
                transition={{ duration: 0.2 }}
                className="grid grid-cols-1 md:grid-cols-2 gap-10 items-center"
              >
                <div className="space-y-5">
                  <span className="text-xs font-mono font-bold uppercase text-purple-600 dark:text-purple-400 tracking-wider">
                    Dành cho Cơ quan Kiểm định & Hải quan
                  </span>
                  <h3 className="font-sans text-2xl sm:text-3xl font-bold text-ink-primary dark:text-white tracking-[-0.02em]">
                    Truy xuất tức thời không thể làm giả
                  </h3>
                  <p className="text-xs text-ink-secondary dark:text-ink-tertiary leading-relaxed">
                    Dữ liệu lưu giữ bất biến trên sổ cái kỹ thuật số, kèm kết quả thử nghiệm từ phòng thí nghiệm ISO/IEC 17025.
                  </p>
                  <ul className="space-y-3 text-xs text-ink-primary dark:text-white">
                    <li className="flex items-center gap-2.5">
                      <CheckCircle size={17} className="text-purple-600 dark:text-purple-400 shrink-0" weight="fill" />
                      <span>Liên kết trực tiếp tới chứng thư kiểm nghiệm phân tích</span>
                    </li>
                    <li className="flex items-center gap-2.5">
                      <CheckCircle size={17} className="text-purple-600 dark:text-purple-400 shrink-0" weight="fill" />
                      <span>Truy vết toán học tỷ lệ phần trăm khối lượng nguồn gốc</span>
                    </li>
                  </ul>
                </div>

                <div className="p-7 rounded-2xl bg-black/[0.02] dark:bg-white/[0.04] border border-black/[0.06] dark:border-white/[0.08] space-y-3">
                  <div className="flex items-center justify-between text-xs font-bold border-b border-black/[0.06] dark:border-white/[0.08] pb-3">
                    <span className="text-ink-primary dark:text-white">Chứng thư tuân thủ xuất khẩu</span>
                    <span className="text-purple-600 dark:text-purple-400 font-mono">ISO 17025</span>
                  </div>
                  <p className="text-xs text-ink-secondary dark:text-ink-tertiary leading-relaxed">
                    Chứng thực lô hàng đạt chuẩn kiểm dịch thực vật và kiểm tra dư lượng thuốc trừ sâu theo tiêu chuẩn quốc tế.
                  </p>
                </div>
              </motion.div>
            )}
          </AnimatePresence>
        </div>
      </div>
    </section>
  );
};
