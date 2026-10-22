/**
 * @screen SC-02
 * @feature Dashboard
 * @roles ALL
 * @description Role-based operational entry point. Apple Bento Box architecture customized per role.
 */
import { useAuthStore } from '@/app/store/authStore';
import { useUiStore } from '@/app/store/uiStore';
import { MassValue } from '@/components/MassValue/MassValue';
import { NetworkStatusIndicator } from '@/components/NetworkStatusIndicator/NetworkStatusIndicator';
import { RoleChip } from '@/components/RoleChip/RoleChip';
import { StatusBadge } from '@/components/StatusBadge/StatusBadge';
import type { UserRole } from '@/shared/types/roles';
import {
  ArrowRight,
  ArrowsClockwise,
  CheckCircle,
  ClockCounterClockwise,
  HandCoins,
  MapPin,
  Plus,
  Scales,
  ShieldWarning,
  TestTube,
  Users,
} from '@phosphor-icons/react';
import { motion } from 'framer-motion';
import React from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import {
  Bar,
  BarChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';

export const RoleHomePage: React.FC = () => {
  const { t } = useTranslation();
  const { user } = useAuthStore();
  const { pendingOfflineCount } = useUiStore();

  const currentRole: UserRole = user?.role || 'MILL_OPERATOR';

  // Demo line processing variance & loss data for Mill Manager Recharts
  const millLossData = [
    { name: 'Sấy 01', haoHutKg: 12000, dungSaiKg: 500, lossType: 'Hao hụt ẩm' },
    { name: 'Xay Xát A', haoHutKg: 300, dungSaiKg: 440, lossType: 'Hao hụt máy' },
    { name: 'Tách Cám', haoHutKg: 150, dungSaiKg: 200, lossType: 'Hao hụt cơ học' },
    { name: 'Phối Trộn B1', haoHutKg: 40, dungSaiKg: 350, lossType: 'Hao hụt trộn' },
    { name: 'Đóng Bao', haoHutKg: 25, dungSaiKg: 120, lossType: 'Bao bì' },
  ];

  return (
    <div className="space-y-8 pb-12">
      {/* Active Workspace Status Banner — Apple Glass Pill */}
      <div className="p-4 bg-white/70 dark:bg-[#121215]/80 backdrop-blur-xl border border-black/[0.06] dark:border-white/[0.08] rounded-3xl shadow-apple-card flex items-center justify-between gap-3 text-xs">
        <div className="flex items-center gap-3">
          <div className="w-8 h-8 rounded-xl bg-black/[0.03] dark:bg-white/[0.06] flex items-center justify-center shrink-0">
            <img src="/favicon.svg" alt="GrainLedger" className="w-6 h-6" />
          </div>
          <span className="font-semibold text-ink-primary dark:text-white">{t('common.workspace', 'Bàn làm việc')}:</span>
          {user && <RoleChip role={user.role} showCode />}
          {user?.organizationName && (
            <span className="text-ink-secondary dark:text-ink-tertiary font-medium">({user.organizationName})</span>
          )}
        </div>
      </div>

      {/* =========================================================================
          ROLE 1: MILL OPERATOR HOME (SC-15 Focus)
          ========================================================================= */}
      {currentRole === 'MILL_OPERATOR' && (
        <div className="space-y-6">
          <div className="border-b border-black/[0.06] dark:border-white/[0.08] pb-5 flex flex-col sm:flex-row sm:items-center justify-between gap-4">
            <div>
              <h2 className="text-2xl sm:text-3xl font-sans font-extrabold text-ink-primary dark:text-white tracking-tight">
                {t('dashboard.operatorTitle', 'Hôm nay tại dây chuyền chế biến')}
              </h2>
              <p className="text-xs text-ink-tertiary mt-1">
                Cơ sở: Nhà máy Xay xát Lúa gạo Tiền Giang — Dây chuyền Sấy & Xay Xát A
              </p>
            </div>
            <motion.div whileHover={{ scale: 1.02 }} whileTap={{ scale: 0.98 }}>
              <Link
                to="/mill/operations/new"
                className="inline-flex items-center gap-2 px-6 py-2.5 rounded-full bg-[#1B2420] dark:bg-white text-white dark:text-black font-semibold text-xs shadow-apple-card hover:shadow-apple-hover transition-all"
              >
                <Plus size={16} weight="bold" />
                <span>{t('dashboard.operatorNewStep', 'Ghi bước chế biến mới')}</span>
              </Link>
            </motion.div>
          </div>

          {/* Latest Operation Highlight — Apple Bento Card */}
          <div className="bg-white dark:bg-[#121215] border border-black/[0.06] dark:border-white/[0.08] rounded-3xl p-6 sm:p-7 shadow-apple-card space-y-4">
            <div className="flex items-center justify-between flex-wrap gap-2">
              <span className="text-xs font-bold text-ink-tertiary uppercase tracking-wider flex items-center gap-2">
                <Scales size={18} weight="bold" className="text-[#2F6B3A] dark:text-emerald-400" />
                Bước chế biến vừa ghi nhận gần nhất
              </span>
              <StatusBadge status="BALANCED" label="Đã cân bằng tuyệt đối (0 kg lệch)" />
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-4 gap-4 p-5 rounded-2xl bg-black/[0.02] dark:bg-white/[0.04] border border-black/[0.06] dark:border-white/[0.08] text-xs">
              <div>
                <span className="text-ink-tertiary block mb-1">Loại công đoạn:</span>
                <span className="font-semibold text-sm text-ink-primary dark:text-white">Xay xát (Milling)</span>
              </div>
              <div>
                <span className="text-ink-tertiary block mb-1">Lô đầu vào:</span>
                <span className="font-mono text-sm font-bold text-ink-primary dark:text-white">S1 (88.000 kg)</span>
              </div>
              <div>
                <span className="text-ink-tertiary block mb-1">Thành phẩm tạo ra:</span>
                <span className="font-bold text-sm text-[#2F6B3A] dark:text-emerald-400">
                  58.000 kg Gạo nguyên
                </span>
              </div>
              <div>
                <span className="text-ink-tertiary block mb-1">Phân đoạn phụ & Hao hụt:</span>
                <span className="font-mono text-xs text-ink-secondary dark:text-ink-tertiary">
                  8.000 kg tấm + 6.000 kg cám + 300 kg hao
                </span>
              </div>
            </div>
          </div>

          {/* Task Oriented Attention Items — Apple Dual Bento */}
          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            {/* Correction requests pending */}
            <div className="bg-white dark:bg-[#121215] border border-black/[0.06] dark:border-white/[0.08] rounded-3xl p-6 sm:p-7 shadow-apple-card space-y-4 flex flex-col justify-between">
              <div className="space-y-4">
                <div className="flex items-center justify-between">
                  <h3 className="font-bold text-sm text-ink-primary dark:text-white flex items-center gap-2">
                    <ClockCounterClockwise size={18} weight="bold" className="text-amber-500" />
                    Yêu cầu sửa đang chờ xác nhận
                  </h3>
                  <span className="font-mono text-[10px] font-bold px-2.5 py-0.5 rounded-full bg-amber-500/10 text-amber-600 dark:text-amber-400">
                    2 chờ
                  </span>
                </div>

                <div className="space-y-2 text-xs">
                  <div className="p-3.5 rounded-2xl bg-black/[0.02] dark:bg-white/[0.04] border border-black/[0.06] dark:border-white/[0.08] flex items-center justify-between">
                    <div>
                      <span className="font-mono font-bold block text-ink-primary dark:text-white">REQ-CORR-001</span>
                      <span className="text-ink-secondary dark:text-ink-tertiary text-[11px]">
                        Điều chỉnh hao hụt mẻ Sấy 02 (Cần bù 50 kg)
                      </span>
                    </div>
                    <Link
                      to="/mill/corrections/pending"
                      className="px-3 py-1.5 rounded-full bg-black/[0.04] dark:bg-white/[0.08] hover:bg-black/[0.08] text-ink-primary dark:text-white font-semibold text-[11px] transition-colors"
                    >
                      Xem
                    </Link>
                  </div>

                  <div className="p-3.5 rounded-2xl bg-black/[0.02] dark:bg-white/[0.04] border border-black/[0.06] dark:border-white/[0.08] flex items-center justify-between">
                    <div>
                      <span className="font-mono font-bold block text-ink-primary dark:text-white">REQ-CORR-002</span>
                      <span className="text-ink-secondary dark:text-ink-tertiary text-[11px]">
                        Cân lại độ ẩm lô thóc tươi L3 trước khi sấy
                      </span>
                    </div>
                    <Link
                      to="/mill/corrections/pending"
                      className="px-3 py-1.5 rounded-full bg-black/[0.04] dark:bg-white/[0.08] hover:bg-black/[0.08] text-ink-primary dark:text-white font-semibold text-[11px] transition-colors"
                    >
                      Xem
                    </Link>
                  </div>
                </div>
              </div>
            </div>

            {/* Approved blend plans ready for execution */}
            <div className="bg-white dark:bg-[#121215] border border-black/[0.06] dark:border-white/[0.08] rounded-3xl p-6 sm:p-7 shadow-apple-card space-y-4 flex flex-col justify-between">
              <div className="space-y-4">
                <div className="flex items-center justify-between">
                  <h3 className="font-bold text-sm text-ink-primary dark:text-white flex items-center gap-2">
                    <ArrowsClockwise size={18} weight="bold" className="text-[#2F6B3A] dark:text-emerald-400" />
                    Plan đã duyệt — Sẵn sàng thực thi
                  </h3>
                  <span className="font-mono text-[10px] font-bold px-2.5 py-0.5 rounded-full bg-emerald-500/10 text-[#2F6B3A] dark:text-emerald-400">
                    1 sẵn sàng
                  </span>
                </div>

                <div className="p-4 rounded-2xl bg-emerald-500/[0.04] border border-emerald-500/20 space-y-3 text-xs">
                  <div className="flex items-center justify-between">
                    <span className="font-mono font-bold text-[#2F6B3A] dark:text-emerald-400 text-sm">
                      PLAN-2026-EU-01
                    </span>
                    <span className="text-ink-tertiary">Container C1 (25.000 kg)</span>
                  </div>
                  <p className="text-ink-secondary dark:text-ink-tertiary">
                    Trộn 21.000 kg lô B1 + 4.000 kg lô gạo nguyên 5451 để đạt tiêu chuẩn MRL EU Tricyclazole.
                  </p>
                  <Link
                    to="/mill/plan-executions/PLAN-2026-EU-01"
                    className="w-full inline-flex items-center justify-center gap-2 py-2.5 rounded-full bg-[#2F6B3A] text-white font-semibold hover:bg-[#25562e] shadow-sm transition-all text-xs"
                  >
                    <span>Thực thi mẻ phối trộn này</span>
                    <ArrowRight size={14} weight="bold" />
                  </Link>
                </div>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* =========================================================================
          ROLE 2: MILL MANAGER HOME
          ========================================================================= */}
      {currentRole === 'MILL_MANAGER' && (
        <div className="space-y-6">
          <div className="border-b border-black/[0.06] dark:border-white/[0.08] pb-5">
            <h2 className="text-2xl sm:text-3xl font-sans font-extrabold text-ink-primary dark:text-white tracking-tight">
              {t('dashboard.managerTitle', 'Bàn điều hành Quản lý nhà máy')}
            </h2>
            <p className="text-xs text-ink-tertiary mt-1">
              Giám sát cân bằng dây chuyền, xác nhận đối soát bằng chứng và xử lý cách ly lệnh HOLD.
            </p>
          </div>

          {/* Action Items List */}
          <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
            <div className="p-6 rounded-3xl bg-white dark:bg-[#121215] border border-amber-500/20 shadow-apple-card space-y-3">
              <div className="flex items-center justify-between">
                <span className="text-xs font-bold text-amber-600 dark:text-amber-400">
                  Đối soát bằng chứng
                </span>
                <span className="font-mono font-bold px-2 py-0.5 rounded-full bg-amber-500/10 text-amber-600 dark:text-amber-400 text-xs">
                  1 chờ
                </span>
              </div>
              <p className="text-xs text-ink-secondary dark:text-ink-tertiary leading-relaxed">
                1 yêu cầu đối soát bằng chứng mẻ sấy sai lệch 120 kg trong dung sai.
              </p>
              <Link
                to="/mill/reconciliations/REC-001"
                className="inline-flex items-center gap-1.5 text-xs text-[#2F6B3A] dark:text-emerald-400 font-semibold hover:underline pt-1"
              >
                <span>Xem và xác nhận</span>
                <ArrowRight size={13} weight="bold" />
              </Link>
            </div>

            <div className="p-6 rounded-3xl bg-white dark:bg-[#121215] border border-red-500/20 shadow-apple-card space-y-3">
              <div className="flex items-center justify-between">
                <span className="text-xs font-bold text-red-600 dark:text-red-400">Lệnh giữ HOLD</span>
                <span className="font-mono font-bold px-2 py-0.5 rounded-full bg-red-500/10 text-red-600 dark:text-red-400 text-xs">
                  1 chờ
                </span>
              </div>
              <p className="text-xs text-ink-secondary dark:text-ink-tertiary leading-relaxed">
                1 lệnh HOLD từ Customer Manager cần nhà máy ghi nhận cách ly vật lý.
              </p>
              <Link
                to="/risk/holds"
                className="inline-flex items-center gap-1.5 text-xs text-red-600 dark:text-red-400 font-semibold hover:underline pt-1"
              >
                <span>Xử lý cách ly ngay</span>
                <ArrowRight size={13} weight="bold" />
              </Link>
            </div>

            <div className="p-6 rounded-3xl bg-white dark:bg-[#121215] border border-emerald-500/20 shadow-apple-card space-y-3">
              <div className="flex items-center justify-between">
                <span className="text-xs font-bold text-[#2F6B3A] dark:text-emerald-400">Dung sai 7 ngày qua</span>
                <CheckCircle size={18} weight="fill" className="text-[#2F6B3A] dark:text-emerald-400" />
              </div>
              <p className="text-xs text-ink-secondary dark:text-ink-tertiary leading-relaxed">
                0 bút toán lệch ngoài dung sai. Toàn bộ dây chuyền hoạt động chuẩn xác.
              </p>
              <span className="text-[11px] text-ink-tertiary block font-mono">
                100% Transactions Balanced
              </span>
            </div>
          </div>

          {/* Simple Recharts Line Loss Overview — Apple Card */}
          <div className="bg-white dark:bg-[#121215] border border-black/[0.06] dark:border-white/[0.08] rounded-3xl p-7 shadow-apple-card space-y-5">
            <div className="flex items-center justify-between">
              <div>
                <h3 className="font-bold text-base text-ink-primary dark:text-white">
                  Tổng quan hao hụt theo dây chuyền hôm nay (kg)
                </h3>
                <p className="text-xs text-ink-tertiary mt-0.5">
                  So sánh hao hụt thực tế ghi nhận với ngưỡng dung sai cho phép theo chính sách nhà máy
                </p>
              </div>
              <span className="text-xs font-mono text-ink-tertiary">Unit: kg</span>
            </div>

            <div className="h-64 w-full pt-2">
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={millLossData} margin={{ top: 10, right: 10, left: -20, bottom: 0 }}>
                  <CartesianGrid strokeDasharray="3 3" opacity={0.08} />
                  <XAxis dataKey="name" tick={{ fontSize: 11 }} />
                  <YAxis tick={{ fontSize: 11 }} />
                  <Tooltip
                    contentStyle={{
                      backgroundColor: 'rgba(18, 18, 21, 0.9)',
                      color: '#ffffff',
                      borderColor: 'rgba(255, 255, 255, 0.1)',
                      borderRadius: 12,
                      fontSize: 12,
                      backdropFilter: 'blur(10px)',
                    }}
                  />
                  <Bar dataKey="haoHutKg" name="Hao hụt thực tế (kg)" fill="#D9A441" radius={[6, 6, 0, 0]} />
                  <Bar dataKey="dungSaiKg" name="Dung sai tối đa (kg)" fill="#2F6B3A" radius={[6, 6, 0, 0]} />
                </BarChart>
              </ResponsiveContainer>
            </div>
          </div>
        </div>
      )}

      {/* =========================================================================
          ROLE 3: CUSTOMER MANAGER HOME (Quality & Traceback)
          ========================================================================= */}
      {currentRole === 'CUSTOMER_MANAGER' && (
        <div className="space-y-6">
          <div className="border-b border-black/[0.06] dark:border-white/[0.08] pb-5">
            <h2 className="text-2xl sm:text-3xl font-sans font-extrabold text-ink-primary dark:text-white tracking-tight">
              {t('dashboard.customerManagerTitle', 'Điều phối Chất lượng & Rủi ro')} — {user?.organizationName}
            </h2>
            <p className="text-xs text-ink-tertiary mt-1">
              Phê duyệt phương án phối trộn, theo dõi xét nghiệm dư lượng MRL và kích hoạt truy vết hai chiều.
            </p>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
            <div className="p-6 rounded-3xl bg-white dark:bg-[#121215] border border-amber-500/20 shadow-apple-card space-y-3">
              <div className="flex items-center justify-between">
                <span className="text-xs font-bold text-amber-600 dark:text-amber-400">
                  Phương án phối trộn
                </span>
                <span className="font-mono font-bold px-2 py-0.5 rounded-full bg-amber-500/10 text-amber-600 dark:text-amber-400 text-xs">
                  1 chờ duyệt
                </span>
              </div>
              <p className="text-xs text-ink-secondary dark:text-ink-tertiary leading-relaxed">
                Phương án PLAN-001 cho Container C1 đang chờ duyệt để xếp hàng.
              </p>
              <Link
                to="/export/plans/pending"
                className="inline-flex items-center gap-1.5 text-xs text-[#2F6B3A] dark:text-emerald-400 font-semibold hover:underline pt-1"
              >
                <span>Xem và duyệt ngay</span>
                <ArrowRight size={13} weight="bold" />
              </Link>
            </div>

            <div className="p-6 rounded-3xl bg-white dark:bg-[#121215] border border-red-500/20 shadow-apple-card space-y-3">
              <div className="flex items-center justify-between">
                <span className="text-xs font-bold text-red-600 dark:text-red-400">Lô hàng đang HOLD</span>
                <span className="font-mono font-bold px-2 py-0.5 rounded-full bg-red-500/10 text-red-600 dark:text-red-400 text-xs">
                  1 lô
                </span>
              </div>
              <p className="text-xs text-ink-secondary dark:text-ink-tertiary leading-relaxed">
                Lô LOT-2026-S1 đang trong diện phong toả do nghi ngờ dư lượng Tricyclazole.
              </p>
              <Link
                to="/risk/traceback"
                className="inline-flex items-center gap-1.5 text-xs text-red-600 dark:text-red-400 font-semibold hover:underline pt-1"
              >
                <span>Truy vết nguồn gốc lô</span>
                <ArrowRight size={13} weight="bold" />
              </Link>
            </div>

            <div className="p-6 rounded-3xl bg-white dark:bg-[#121215] border border-black/[0.06] dark:border-white/[0.08] shadow-apple-card space-y-3">
              <div className="flex items-center justify-between">
                <span className="text-xs font-bold text-ink-primary dark:text-white">Mẫu gửi phòng lab</span>
                <span className="font-mono font-bold px-2 py-0.5 rounded-full bg-black/[0.04] dark:bg-white/[0.08] text-ink-secondary dark:text-ink-tertiary text-xs">
                  2 chờ kết quả
                </span>
              </div>
              <p className="text-xs text-ink-secondary dark:text-ink-tertiary leading-relaxed">
                Mẫu LAB-TG-088 và LAB-TG-089 đã gửi viện kiểm nghiệm ISO 17025.
              </p>
              <Link
                to="/compliance/lab-samples"
                className="inline-flex items-center gap-1.5 text-xs text-blue-600 dark:text-blue-400 font-semibold hover:underline pt-1"
              >
                <span>Xem danh sách mẫu</span>
                <ArrowRight size={13} weight="bold" />
              </Link>
            </div>
          </div>

          {/* Residue Near-Limit Warnings */}
          <div className="bg-white dark:bg-[#121215] border border-black/[0.06] dark:border-white/[0.08] rounded-3xl p-7 shadow-apple-card space-y-4">
            <div className="flex items-center justify-between">
              <h3 className="font-bold text-base text-ink-primary dark:text-white flex items-center gap-2">
                <ShieldWarning size={20} weight="bold" className="text-amber-500" />
                Cảnh báo dư lượng tiếp cận ngưỡng MRL (Safety Margin Warning)
              </h3>
              <span className="text-xs text-ink-tertiary">Biên an toàn cấu hình: 20%</span>
            </div>

            <div className="overflow-x-auto">
              <table className="w-full text-xs text-left">
                <thead className="bg-black/[0.02] dark:bg-white/[0.04] text-ink-tertiary border-b border-black/[0.06] dark:border-white/[0.08]">
                  <tr>
                    <th className="py-3 px-4 font-semibold">Mã Lô</th>
                    <th className="py-3 px-4 font-semibold">Hoạt chất</th>
                    <th className="py-3 px-4 font-semibold">Thị trường</th>
                    <th className="py-3 px-4 font-semibold">Cận dư lượng 95%</th>
                    <th className="py-3 px-4 font-semibold">Ngưỡng MRL</th>
                    <th className="py-3 px-4 font-semibold">Đánh giá rủi ro</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-black/[0.04] dark:border-white/[0.06]">
                  <tr>
                    <td className="py-3 px-4 font-mono font-bold text-ink-primary dark:text-white">LOT-2026-S1</td>
                    <td className="py-3 px-4">Tricyclazole (TCZ001)</td>
                    <td className="py-3 px-4">EU (EC 396/2005)</td>
                    <td className="py-3 px-4 font-mono font-bold text-red-600 dark:text-red-400">0,009 mg/kg</td>
                    <td className="py-3 px-4 font-mono">0,010 mg/kg</td>
                    <td className="py-3 px-4">
                      <span className="px-2.5 py-1 rounded-full bg-red-500/10 text-red-600 dark:text-red-400 font-bold text-[11px]">
                        VƯỢT BIÊN AN TOÀN
                      </span>
                    </td>
                  </tr>
                  <tr>
                    <td className="py-3 px-4 font-mono font-bold text-ink-primary dark:text-white">LOT-2026-P01</td>
                    <td className="py-3 px-4">Isoprothiolane (IPT002)</td>
                    <td className="py-3 px-4">Nhật Bản (CAA)</td>
                    <td className="py-3 px-4 font-mono font-bold text-[#2F6B3A] dark:text-emerald-400">2,100 mg/kg</td>
                    <td className="py-3 px-4 font-mono">7,000 mg/kg</td>
                    <td className="py-3 px-4">
                      <span className="px-2.5 py-1 rounded-full bg-emerald-500/10 text-[#2F6B3A] dark:text-emerald-400 font-bold text-[11px]">
                        AN TOÀN
                      </span>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </div>
      )}

      {/* =========================================================================
          ROLE 4: CUSTOMER EXPORT OFFICER HOME
          ========================================================================= */}
      {currentRole === 'CUSTOMER_EXPORT_OFFICER' && (
        <div className="space-y-6">
          <div className="border-b border-black/[0.06] dark:border-white/[0.08] pb-5 flex flex-col sm:flex-row sm:items-center justify-between gap-4">
            <div>
              <h2 className="text-2xl sm:text-3xl font-sans font-extrabold text-ink-primary dark:text-white tracking-tight">
                {t('dashboard.exportOfficerTitle', 'Đơn hàng & Xếp Container')} — {user?.organizationName}
              </h2>
              <p className="text-xs text-ink-tertiary mt-1">
                Quản lý tiến độ đơn xuất khẩu, đóng niêm container và trích xuất hồ sơ tuân thủ.
              </p>
            </div>
            <motion.div whileHover={{ scale: 1.02 }} whileTap={{ scale: 0.98 }}>
              <Link
                to="/export/orders"
                className="inline-flex items-center gap-2 px-6 py-2.5 rounded-full bg-[#1B2420] dark:bg-white text-white dark:text-black font-semibold text-xs shadow-apple-card hover:shadow-apple-hover transition-all"
              >
                <Plus size={16} weight="bold" />
                <span>Tạo đơn xuất khẩu mới</span>
              </Link>
            </motion.div>
          </div>

          {/* Active Orders Table */}
          <div className="bg-white dark:bg-[#121215] border border-black/[0.06] dark:border-white/[0.08] rounded-3xl p-7 shadow-apple-card space-y-4">
            <h3 className="font-bold text-base text-ink-primary dark:text-white">
              Đơn hàng đang xử lý
            </h3>
            <div className="overflow-x-auto">
              <table className="w-full text-xs text-left">
                <thead className="bg-black/[0.02] dark:bg-white/[0.04] text-ink-tertiary border-b border-black/[0.06] dark:border-white/[0.08]">
                  <tr>
                    <th className="py-3 px-4 font-semibold">Mã Đơn</th>
                    <th className="py-3 px-4 font-semibold">Thị trường</th>
                    <th className="py-3 px-4 font-semibold">Khối lượng</th>
                    <th className="py-3 px-4 font-semibold">Container</th>
                    <th className="py-3 px-4 font-semibold">Trạng thái</th>
                    <th className="py-3 px-4 text-right font-semibold">Hành động</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-black/[0.04] dark:border-white/[0.06]">
                  <tr>
                    <td className="py-3 px-4 font-mono font-bold text-ink-primary dark:text-white">ORD-2026-EU-01</td>
                    <td className="py-3 px-4 font-medium">Châu Âu (EU)</td>
                    <td className="py-3 px-4 font-mono font-bold">
                      <MassValue value={50000} decimals={3} />
                    </td>
                    <td className="py-3 px-4">1/2 đã đóng</td>
                    <td className="py-3 px-4">
                      <StatusBadge status="IN_PROGRESS" label="Đang xếp Cont 2" />
                    </td>
                    <td className="py-3 px-4 text-right">
                      <Link
                        to="/export/containers/CONT-02/loading"
                        className="px-3.5 py-1.5 rounded-full bg-[#1B2420] dark:bg-white text-white dark:text-black font-semibold hover:opacity-90 transition-opacity"
                      >
                        Ghi nhận xếp hàng
                      </Link>
                    </td>
                  </tr>
                  <tr>
                    <td className="py-3 px-4 font-mono font-bold text-ink-primary dark:text-white">ORD-2026-JP-02</td>
                    <td className="py-3 px-4 font-medium">Nhật Bản (JP)</td>
                    <td className="py-3 px-4 font-mono font-bold">
                      <MassValue value={25000} decimals={3} />
                    </td>
                    <td className="py-3 px-4">0/1 đã đóng</td>
                    <td className="py-3 px-4">
                      <StatusBadge status="PENDING" label="Chờ yêu cầu Plan" />
                    </td>
                    <td className="py-3 px-4 text-right">
                      <Link
                        to="/export/plans"
                        className="px-3.5 py-1.5 rounded-full bg-black/[0.04] dark:bg-white/[0.08] hover:bg-black/[0.08] text-ink-primary dark:text-white font-semibold transition-colors"
                      >
                        Yêu cầu phối trộn
                      </Link>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </div>
      )}

      {/* =========================================================================
          ROLE 5: PROCUREMENT OFFICER HOME (Field Offline)
          ========================================================================= */}
      {currentRole === 'CUSTOMER_PROCUREMENT_OFFICER' && (
        <div className="space-y-6">
          <div className="border-b border-black/[0.06] dark:border-white/[0.08] pb-5">
            <h2 className="text-2xl sm:text-3xl font-sans font-extrabold text-ink-primary dark:text-white tracking-tight">
              {t('dashboard.procurementOfficerTitle', 'Bàn làm việc Thu mua Hiện trường (Field Client)')}
            </h2>
            <p className="text-xs text-ink-tertiary mt-1">
              Hỗ trợ làm việc hoàn toàn ngoại tuyến tại ruộng lúa và đồng bộ khi có kết nối internet.
            </p>
          </div>

          <div className="p-5 rounded-3xl bg-white dark:bg-[#121215] border border-black/[0.06] dark:border-white/[0.08] shadow-apple-card flex flex-col sm:flex-row items-center justify-between gap-4">
            <div className="flex items-center gap-3">
              <NetworkStatusIndicator />
              <span className="text-xs text-ink-secondary dark:text-ink-tertiary">
                Dữ liệu thu mua lưu an toàn trong bộ nhớ trình duyệt cục bộ.
              </span>
            </div>

            <motion.div whileHover={{ scale: 1.02 }} whileTap={{ scale: 0.98 }}>
              <Link
                to="/field/sync"
                className="inline-flex items-center gap-2 px-5 py-2.5 rounded-full bg-[#2F6B3A] text-white font-semibold text-xs shadow-apple-card hover:bg-[#25562e] transition-all"
              >
                <ArrowsClockwise size={15} weight="bold" />
                <span>Đồng bộ ngay ({pendingOfflineCount} bản ghi)</span>
              </Link>
            </motion.div>
          </div>

          <div className="grid grid-cols-2 sm:grid-cols-4 gap-6">
            <motion.div whileHover={{ y: -4 }} transition={{ duration: 0.2 }}>
              <Link
                to="/field/farmers/new"
                className="p-6 rounded-3xl bg-white dark:bg-[#121215] border border-black/[0.06] dark:border-white/[0.08] shadow-apple-card hover:shadow-apple-hover transition-all text-center space-y-3 block group"
              >
                <div className="w-12 h-12 rounded-2xl bg-black/[0.03] dark:bg-white/[0.06] text-[#2F6B3A] dark:text-emerald-400 flex items-center justify-center mx-auto group-hover:scale-110 transition-transform">
                  <Users size={24} weight="bold" />
                </div>
                <span className="font-bold text-xs text-ink-primary dark:text-white block">
                  Đăng ký Nông hộ
                </span>
              </Link>
            </motion.div>

            <motion.div whileHover={{ y: -4 }} transition={{ duration: 0.2 }}>
              <Link
                to="/field/plots/new"
                className="p-6 rounded-3xl bg-white dark:bg-[#121215] border border-black/[0.06] dark:border-white/[0.08] shadow-apple-card hover:shadow-apple-hover transition-all text-center space-y-3 block group"
              >
                <div className="w-12 h-12 rounded-2xl bg-black/[0.03] dark:bg-white/[0.06] text-blue-600 dark:text-blue-400 flex items-center justify-center mx-auto group-hover:scale-110 transition-transform">
                  <MapPin size={24} weight="bold" />
                </div>
                <span className="font-bold text-xs text-ink-primary dark:text-white block">
                  Thửa ruộng & Toạ độ
                </span>
              </Link>
            </motion.div>

            <motion.div whileHover={{ y: -4 }} transition={{ duration: 0.2 }}>
              <Link
                to="/field/receipts/new"
                className="p-6 rounded-3xl bg-white dark:bg-[#121215] border border-black/[0.06] dark:border-white/[0.08] shadow-apple-card hover:shadow-apple-hover transition-all text-center space-y-3 block group"
              >
                <div className="w-12 h-12 rounded-2xl bg-black/[0.03] dark:bg-white/[0.06] text-emerald-600 dark:text-emerald-400 flex items-center justify-center mx-auto group-hover:scale-110 transition-transform">
                  <HandCoins size={24} weight="bold" />
                </div>
                <span className="font-bold text-xs text-ink-primary dark:text-white block">
                  Thu mua lúa tươi
                </span>
              </Link>
            </motion.div>

            <motion.div whileHover={{ y: -4 }} transition={{ duration: 0.2 }}>
              <Link
                to="/field/samples/new"
                className="p-6 rounded-3xl bg-white dark:bg-[#121215] border border-black/[0.06] dark:border-white/[0.08] shadow-apple-card hover:shadow-apple-hover transition-all text-center space-y-3 block group"
              >
                <div className="w-12 h-12 rounded-2xl bg-black/[0.03] dark:bg-white/[0.06] text-amber-600 dark:text-amber-400 flex items-center justify-center mx-auto group-hover:scale-110 transition-transform">
                  <TestTube size={24} weight="bold" />
                </div>
                <span className="font-bold text-xs text-ink-primary dark:text-white block">
                  Lấy mẫu niêm phong
                </span>
              </Link>
            </motion.div>
          </div>
        </div>
      )}

      {/* =========================================================================
          ROLE 6: MILL ADMINISTRATOR HOME
          ========================================================================= */}
      {currentRole === 'MILL_ADMIN' && (
        <div className="space-y-6">
          <div className="border-b border-black/[0.06] dark:border-white/[0.08] pb-5">
            <h2 className="text-2xl sm:text-3xl font-sans font-extrabold text-ink-primary dark:text-white tracking-tight">
              {t('dashboard.adminTitle', 'Trạng thái Hệ thống Quản trị Nhà máy')}
            </h2>
            <p className="text-xs text-ink-tertiary mt-1">
              Cấu hình cơ sở vật chất, tài khoản phân quyền và quản lý phiên bản bảng giới hạn MRL.
            </p>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-3 gap-6">
            <div className="p-6 rounded-3xl bg-white dark:bg-[#121215] border border-black/[0.06] dark:border-white/[0.08] shadow-apple-card space-y-3">
              <span className="text-xs text-ink-tertiary">Công ty khách hàng đang hoạt động</span>
              <div className="flex items-baseline gap-2">
                <span className="font-mono text-4xl font-extrabold text-[#2F6B3A] dark:text-emerald-400">4</span>
                <span className="text-xs text-ink-secondary dark:text-ink-tertiary">công ty đối tác</span>
              </div>
              <Link to="/admin/customers" className="text-xs text-[#2F6B3A] dark:text-emerald-400 font-semibold hover:underline block pt-1">
                Quản lý công ty đối tác →
              </Link>
            </div>

            <div className="p-6 rounded-3xl bg-white dark:bg-[#121215] border border-black/[0.06] dark:border-white/[0.08] shadow-apple-card space-y-3">
              <span className="text-xs text-ink-tertiary">Tài khoản người dùng</span>
              <div className="flex items-baseline gap-2">
                <span className="font-mono text-4xl font-extrabold text-blue-600 dark:text-blue-400">28</span>
                <span className="text-xs text-ink-secondary dark:text-ink-tertiary">tài khoản hoạt động</span>
              </div>
              <Link to="/admin/access" className="text-xs text-blue-600 dark:text-blue-400 font-semibold hover:underline block pt-1">
                Phân quyền tài khoản →
              </Link>
            </div>

            <div className="p-6 rounded-3xl bg-white dark:bg-[#121215] border border-black/[0.06] dark:border-white/[0.08] shadow-apple-card space-y-3">
              <span className="text-xs text-ink-tertiary">Phiên bản MRL quốc tế</span>
              <div className="flex items-baseline gap-2">
                <span className="font-mono text-2xl font-bold text-amber-600 dark:text-amber-400">
                  EU 2026-Q3
                </span>
              </div>
              <Link to="/compliance/mrl-versions" className="text-xs text-amber-600 dark:text-amber-400 font-semibold hover:underline block pt-1">
                Xem bảng giới hạn MRL →
              </Link>
            </div>
          </div>
        </div>
      )}

      {/* =========================================================================
          ROLE 7: AUTHORIZED STOCK VIEWER HOME
          ========================================================================= */}
      {currentRole === 'AUTHORIZED_STOCK_VIEWER' && (
        <div className="space-y-6">
          <div className="border-b border-black/[0.06] dark:border-white/[0.08] pb-5">
            <h2 className="text-2xl sm:text-3xl font-sans font-extrabold text-ink-primary dark:text-white tracking-tight">
              {t('dashboard.stockViewerTitle', 'Vị trí & Cơ cấu Tồn kho Khách hàng')}
            </h2>
            <p className="text-xs text-ink-tertiary mt-1">
              Quyền xem độc lập dữ liệu tồn kho theo vị trí kho, silo và cơ cấu nguồn gốc thửa ruộng.
            </p>
          </div>

          <div className="p-7 rounded-3xl bg-white dark:bg-[#121215] border border-black/[0.06] dark:border-white/[0.08] shadow-apple-card space-y-4">
            <div className="flex items-center justify-between">
              <h3 className="font-bold text-base text-ink-primary dark:text-white">Tồn kho theo vị trí</h3>
              <Link
                to="/stock"
                className="px-5 py-2.5 rounded-full bg-[#1B2420] dark:bg-white text-white dark:text-black text-xs font-semibold hover:opacity-90 transition-opacity"
              >
                Mở bảng tồn kho chi tiết
              </Link>
            </div>
            <p className="text-xs text-ink-secondary dark:text-ink-tertiary leading-relaxed">
              Bạn có quyền truy cập chỉ-đọc (read-only) để kiểm tra số dư khả dụng, số dư đang bảo lưu cho phương án phối trộn, và tỷ lệ phần trăm khối lượng đóng góp từ từng thửa ruộng.
            </p>
          </div>
        </div>
      )}
    </div>
  );
};
