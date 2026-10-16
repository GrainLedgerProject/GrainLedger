/**
 * @screen SC-02
 * @feature Dashboard
 * @roles ALL
 * @description Role-based operational entry point. Task-oriented layout customized per role.
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
  BookOpen,
  Buildings,
  CheckCircle,
  ClockCounterClockwise,
  Crosshair,
  Drop,
  Export,
  Factory,
  FileText,
  HandCoins,
  LockSimple,
  MapPin,
  Package,
  Plus,
  Scales,
  ShieldCheck,
  ShieldWarning,
  TestTube,
  Truck,
  Users,
  WarningCircle,
} from '@phosphor-icons/react';
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
    <div className="space-y-6">
      {/* Active Workspace Status Banner */}
      <div className="p-3 bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 rounded-lg flex items-center justify-between gap-3 text-xs">
        <div className="flex items-center gap-2">
          <img src="/favicon.svg" alt="GrainLedger" className="w-8 h-8" />
          <span className="font-semibold text-ink-primary">{t('common.workspace', 'Bàn làm việc')}:</span>
          {user && <RoleChip role={user.role} showCode />}
          {user?.organizationName && (
            <span className="text-ink-secondary">({user.organizationName})</span>
          )}
        </div>
      </div>

      {/* =========================================================================
          ROLE 1: MILL OPERATOR HOME (SC-15 Focus)
          ========================================================================= */}
      {currentRole === 'MILL_OPERATOR' && (
        <div className="space-y-6">
          <div className="border-b border-ink-tertiary/15 pb-4 flex flex-col sm:flex-row sm:items-center justify-between gap-4">
            <div>
              <h2 className="text-2xl font-serif font-bold text-ink-primary">
                {t('dashboard.operatorTitle', 'Hôm nay tại dây chuyền chế biến')}
              </h2>
              <p className="text-xs text-ink-secondary mt-0.5">
                Cơ sở: Nhà máy Xay xát Lúa gạo Tiền Giang — Dây chuyền Sấy & Xay Xát A
              </p>
            </div>
            <Link
              to="/mill/operations/new"
              className="inline-flex items-center gap-2 px-4 py-2.5 rounded-md bg-accent-primary text-white font-semibold text-xs hover:bg-accent-primary-dim transition-all shadow-xs"
            >
              <Plus size={16} weight="bold" />
              <span>{t('dashboard.operatorNewStep', 'Ghi bước chế biến mới')}</span>
            </Link>
          </div>

          {/* Latest Operation Highlight */}
          <div className="bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 rounded-lg p-5 space-y-4">
            <div className="flex items-center justify-between flex-wrap gap-2">
              <span className="text-xs font-bold text-ink-tertiary uppercase tracking-wider flex items-center gap-1.5">
                <Scales size={16} className="text-accent-primary" />
                Bước chế biến vừa ghi nhận gần nhất
              </span>
              <StatusBadge status="BALANCED" label="Đã cân bằng tuyệt đối (0 kg lệch)" />
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-4 gap-4 p-4 rounded-md bg-surface-base border border-ink-tertiary/15 text-xs">
              <div>
                <span className="text-ink-tertiary block">Loại công đoạn:</span>
                <span className="font-semibold text-sm text-ink-primary">Xay xát (Milling)</span>
              </div>
              <div>
                <span className="text-ink-tertiary block">Lô đầu vào:</span>
                <span className="font-mono text-sm font-semibold text-ink-primary">S1 (88.000 kg)</span>
              </div>
              <div>
                <span className="text-ink-tertiary block">Thành phẩm tạo ra:</span>
                <span className="font-semibold text-sm text-accent-primary">
                  58.000 kg Gạo nguyên
                </span>
              </div>
              <div>
                <span className="text-ink-tertiary block">Phân đoạn phụ & Hao hụt:</span>
                <span className="font-mono text-xs text-ink-secondary">
                  8.000 kg tấm + 6.000 kg cám + 300 kg hao
                </span>
              </div>
            </div>
          </div>

          {/* Task Oriented Attention Items */}
          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            {/* Correction requests pending */}
            <div className="bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 rounded-lg p-5 space-y-4">
              <div className="flex items-center justify-between">
                <h3 className="font-semibold text-sm text-ink-primary flex items-center gap-2">
                  <ClockCounterClockwise size={18} className="text-accent-warning" />
                  Yêu cầu sửa đang chờ xác nhận
                </h3>
                <span className="font-mono text-xs font-bold px-2 py-0.5 rounded-full bg-accent-warning/20 text-amber-800 dark:text-amber-300">
                  2 chờ
                </span>
              </div>

              <div className="space-y-2 text-xs">
                <div className="p-3 rounded-md bg-surface-base border border-ink-tertiary/15 flex items-center justify-between">
                  <div>
                    <span className="font-mono font-bold block text-ink-primary">REQ-CORR-001</span>
                    <span className="text-ink-secondary text-[11px]">
                      Điều chỉnh hao hụt mẻ Sấy 02 (Cần bù 50 kg)
                    </span>
                  </div>
                  <Link
                    to="/mill/corrections/pending"
                    className="px-2.5 py-1 rounded bg-surface-raised hover:bg-surface-overlay text-ink-primary font-medium"
                  >
                    Xem
                  </Link>
                </div>

                <div className="p-3 rounded-md bg-surface-base border border-ink-tertiary/15 flex items-center justify-between">
                  <div>
                    <span className="font-mono font-bold block text-ink-primary">REQ-CORR-002</span>
                    <span className="text-ink-secondary text-[11px]">
                      Cân lại độ ẩm lô thóc tươi L3 trước khi sấy
                    </span>
                  </div>
                  <Link
                    to="/mill/corrections/pending"
                    className="px-2.5 py-1 rounded bg-surface-raised hover:bg-surface-overlay text-ink-primary font-medium"
                  >
                    Xem
                  </Link>
                </div>
              </div>
            </div>

            {/* Approved blend plans ready for execution */}
            <div className="bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 rounded-lg p-5 space-y-4">
              <div className="flex items-center justify-between">
                <h3 className="font-semibold text-sm text-ink-primary flex items-center gap-2">
                  <ArrowsClockwise size={18} className="text-accent-primary" />
                  Plan đã duyệt — Sẵn sàng thực thi
                </h3>
                <span className="font-mono text-xs font-bold px-2 py-0.5 rounded-full bg-accent-primary/20 text-accent-primary">
                  1 sẵn sàng
                </span>
              </div>

              <div className="p-4 rounded-md bg-surface-base border border-accent-primary/25 space-y-3 text-xs">
                <div className="flex items-center justify-between">
                  <span className="font-mono font-bold text-accent-primary text-sm">
                    PLAN-2026-EU-01
                  </span>
                  <span className="text-ink-tertiary">Container C1 (25.000 kg)</span>
                </div>
                <p className="text-ink-secondary">
                  Trộn 21.000 kg lô B1 + 4.000 kg lô gạo nguyên 5451 để đạt tiêu chuẩn MRL EU Tricyclazole.
                </p>
                <Link
                  to="/mill/plan-executions/PLAN-2026-EU-01"
                  className="w-full inline-flex items-center justify-center gap-1.5 py-2 rounded bg-accent-primary text-white font-semibold hover:bg-accent-primary-dim transition-colors"
                >
                  <span>Thực thi mẻ phối trộn này</span>
                  <ArrowRight size={14} />
                </Link>
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
          <div className="border-b border-ink-tertiary/15 pb-4">
            <h2 className="text-2xl font-serif font-bold text-ink-primary">
              {t('dashboard.managerTitle', 'Bàn điều hành Quản lý nhà máy')}
            </h2>
            <p className="text-xs text-ink-secondary mt-0.5">
              Giám sát cân bằng dây chuyền, xác nhận đối soát bằng chứng và xử lý cách ly lệnh HOLD.
            </p>
          </div>

          {/* Action Items List */}
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
            <div className="p-4 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-amber-300 dark:border-amber-700/50 space-y-2">
              <div className="flex items-center justify-between">
                <span className="text-xs font-bold text-amber-800 dark:text-amber-200">
                  Đối soát bằng chứng
                </span>
                <span className="font-mono font-bold px-1.5 py-0.5 rounded bg-accent-warning text-ink-primary text-xs">
                  1 chờ
                </span>
              </div>
              <p className="text-xs text-ink-secondary">
                1 yêu cầu đối soát bằng chứng mẻ sấy sai lệch 120 kg trong dung sai.
              </p>
              <Link
                to="/mill/reconciliations/REC-001"
                className="inline-flex items-center gap-1 text-xs text-accent-primary font-semibold hover:underline pt-1"
              >
                <span>Xem và xác nhận</span>
                <ArrowRight size={13} />
              </Link>
            </div>

            <div className="p-4 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-accent-danger/40 space-y-2">
              <div className="flex items-center justify-between">
                <span className="text-xs font-bold text-accent-danger">Lệnh giữ HOLD</span>
                <span className="font-mono font-bold px-1.5 py-0.5 rounded bg-accent-danger text-white text-xs">
                  1 chờ
                </span>
              </div>
              <p className="text-xs text-ink-secondary">
                1 lệnh HOLD từ Customer Manager cần nhà máy ghi nhận cách ly vật lý.
              </p>
              <Link
                to="/risk/holds"
                className="inline-flex items-center gap-1 text-xs text-accent-danger font-semibold hover:underline pt-1"
              >
                <span>Xử lý cách ly ngay</span>
                <ArrowRight size={13} />
              </Link>
            </div>

            <div className="p-4 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-accent-primary/30 space-y-2">
              <div className="flex items-center justify-between">
                <span className="text-xs font-bold text-accent-primary">Dung sai 7 ngày qua</span>
                <CheckCircle size={18} weight="fill" className="text-accent-primary" />
              </div>
              <p className="text-xs text-ink-secondary">
                0 bút toán lệch ngoài dung sai. Toàn bộ dây chuyền hoạt động chuẩn xác.
              </p>
              <span className="text-[11px] text-ink-tertiary block font-mono">
                100% Transactions Balanced
              </span>
            </div>
          </div>

          {/* Simple Recharts Line Loss Overview */}
          <div className="bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 rounded-lg p-6 space-y-4">
            <div className="flex items-center justify-between">
              <div>
                <h3 className="font-semibold text-sm text-ink-primary font-serif">
                  Tổng quan hao hụt theo dây chuyền hôm nay (kg)
                </h3>
                <p className="text-xs text-ink-secondary">
                  So sánh hao hụt thực tế ghi nhận với ngưỡng dung sai cho phép theo chính sách nhà máy
                </p>
              </div>
              <span className="text-xs font-mono text-ink-tertiary">Unit: kg</span>
            </div>

            <div className="h-64 w-full pt-2">
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={millLossData} margin={{ top: 10, right: 10, left: -20, bottom: 0 }}>
                  <CartesianGrid strokeDasharray="3 3" opacity={0.15} />
                  <XAxis dataKey="name" tick={{ fontSize: 11 }} />
                  <YAxis tick={{ fontSize: 11 }} />
                  <Tooltip
                    contentStyle={{
                      backgroundColor: 'var(--color-surface-base)',
                      borderColor: 'var(--color-ink-tertiary)',
                      borderRadius: 6,
                      fontSize: 12,
                    }}
                  />
                  <Bar dataKey="haoHutKg" name="Hao hụt thực tế (kg)" fill="#D9A441" radius={[4, 4, 0, 0]} />
                  <Bar dataKey="dungSaiKg" name="Dung sai tối đa (kg)" fill="#2F6B3A" radius={[4, 4, 0, 0]} />
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
          <div className="border-b border-ink-tertiary/15 pb-4">
            <h2 className="text-2xl font-serif font-bold text-ink-primary">
              {t('dashboard.customerManagerTitle', 'Điều phối Chất lượng & Rủi ro')} — {user?.organizationName}
            </h2>
            <p className="text-xs text-ink-secondary mt-0.5">
              Phê duyệt phương án phối trộn, theo dõi xét nghiệm dư lượng MRL và kích hoạt truy vết hai chiều.
            </p>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
            <div className="p-4 rounded-lg bg-amber-50 dark:bg-amber-950/30 border border-amber-300 dark:border-amber-700/50 space-y-2">
              <div className="flex items-center justify-between">
                <span className="text-xs font-bold text-amber-900 dark:text-amber-200">
                  Phương án phối trộn
                </span>
                <span className="font-mono font-bold px-2 py-0.5 rounded bg-accent-warning text-ink-primary text-xs">
                  1 chờ duyệt
                </span>
              </div>
              <p className="text-xs text-ink-secondary">
                Phương án PLAN-001 cho Container C1 đang chờ duyệt để xếp hàng.
              </p>
              <Link
                to="/export/plans/pending"
                className="inline-flex items-center gap-1 text-xs text-accent-primary font-semibold hover:underline pt-1"
              >
                <span>Xem và duyệt ngay</span>
                <ArrowRight size={13} />
              </Link>
            </div>

            <div className="p-4 rounded-lg bg-red-50 dark:bg-red-950/30 border border-accent-danger/40 space-y-2">
              <div className="flex items-center justify-between">
                <span className="text-xs font-bold text-accent-danger">Lô hàng đang HOLD</span>
                <span className="font-mono font-bold px-2 py-0.5 rounded bg-accent-danger text-white text-xs">
                  1 lô
                </span>
              </div>
              <p className="text-xs text-ink-secondary">
                Lô LOT-2026-S1 đang trong diện phong toả do nghi ngờ dư lượng Tricyclazole.
              </p>
              <Link
                to="/risk/traceback"
                className="inline-flex items-center gap-1 text-xs text-accent-danger font-semibold hover:underline pt-1"
              >
                <span>Truy vết nguồn gốc lô</span>
                <ArrowRight size={13} />
              </Link>
            </div>

            <div className="p-4 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 space-y-2">
              <div className="flex items-center justify-between">
                <span className="text-xs font-bold text-ink-primary">Mẫu gửi phòng lab</span>
                <span className="font-mono font-bold px-2 py-0.5 rounded bg-surface-base text-ink-secondary text-xs">
                  2 chờ kết quả
                </span>
              </div>
              <p className="text-xs text-ink-secondary">
                Mẫu LAB-TG-088 và LAB-TG-089 đã gửi viện kiểm nghiệm ISO 17025.
              </p>
              <Link
                to="/compliance/lab-samples"
                className="inline-flex items-center gap-1 text-xs text-accent-info font-semibold hover:underline pt-1"
              >
                <span>Xem danh sách mẫu</span>
                <ArrowRight size={13} />
              </Link>
            </div>
          </div>

          {/* Residue Near-Limit Warnings */}
          <div className="bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 rounded-lg p-5 space-y-3">
            <div className="flex items-center justify-between">
              <h3 className="font-semibold text-sm text-ink-primary flex items-center gap-2">
                <ShieldWarning size={18} className="text-accent-warning" />
                Cảnh báo dư lượng tiếp cận ngưỡng MRL (Safety Margin Warning)
              </h3>
              <span className="text-xs text-ink-tertiary">Biên an toàn cấu hình: 20%</span>
            </div>

            <div className="overflow-x-auto">
              <table className="w-full text-xs text-left">
                <thead className="bg-surface-base text-ink-tertiary border-b border-ink-tertiary/15">
                  <tr>
                    <th className="py-2 px-3">Mã Lô</th>
                    <th className="py-2 px-3">Hoạt chất</th>
                    <th className="py-2 px-3">Thị trường</th>
                    <th className="py-2 px-3">Cận dư lượng 95%</th>
                    <th className="py-2 px-3">Ngưỡng MRL</th>
                    <th className="py-2 px-3">Đánh giá rủi ro</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-ink-tertiary/10">
                  <tr>
                    <td className="py-2.5 px-3 font-mono font-bold">LOT-2026-S1</td>
                    <td className="py-2.5 px-3">Tricyclazole (TCZ001)</td>
                    <td className="py-2.5 px-3">EU (EC 396/2005)</td>
                    <td className="py-2.5 px-3 font-mono font-semibold text-accent-danger">0,009 mg/kg</td>
                    <td className="py-2.5 px-3 font-mono">0,010 mg/kg</td>
                    <td className="py-2.5 px-3">
                      <span className="px-2 py-0.5 rounded bg-red-100 text-accent-danger font-bold">
                        VƯỢT BIÊN AN TOÀN
                      </span>
                    </td>
                  </tr>
                  <tr>
                    <td className="py-2.5 px-3 font-mono font-bold">LOT-2026-P01</td>
                    <td className="py-2.5 px-3">Isoprothiolane (IPT002)</td>
                    <td className="py-2.5 px-3">Nhật Bản (CAA)</td>
                    <td className="py-2.5 px-3 font-mono font-semibold text-accent-primary">2,100 mg/kg</td>
                    <td className="py-2.5 px-3 font-mono">7,000 mg/kg</td>
                    <td className="py-2.5 px-3">
                      <span className="px-2 py-0.5 rounded bg-emerald-100 text-accent-primary font-bold">
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
          <div className="border-b border-ink-tertiary/15 pb-4 flex flex-col sm:flex-row sm:items-center justify-between gap-4">
            <div>
              <h2 className="text-2xl font-serif font-bold text-ink-primary">
                {t('dashboard.exportOfficerTitle', 'Đơn hàng & Xếp Container')} — {user?.organizationName}
              </h2>
              <p className="text-xs text-ink-secondary mt-0.5">
                Quản lý tiến độ đơn xuất khẩu, đóng niêm container và trích xuất hồ sơ tuân thủ.
              </p>
            </div>
            <Link
              to="/export/orders"
              className="inline-flex items-center gap-2 px-4 py-2 rounded-md bg-accent-primary text-white font-semibold text-xs hover:bg-accent-primary-dim transition-colors shadow-xs"
            >
              <Plus size={16} />
              <span>Tạo đơn xuất khẩu mới</span>
            </Link>
          </div>

          {/* Active Orders Table */}
          <div className="bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 rounded-lg p-5 space-y-4">
            <h3 className="font-semibold text-sm text-ink-primary">
              Đơn hàng đang xử lý
            </h3>
            <div className="overflow-x-auto">
              <table className="w-full text-xs text-left">
                <thead className="bg-surface-base text-ink-tertiary border-b border-ink-tertiary/15">
                  <tr>
                    <th className="py-2 px-3">Mã Đơn</th>
                    <th className="py-2 px-3">Thị trường</th>
                    <th className="py-2 px-3">Khối lượng</th>
                    <th className="py-2 px-3">Container</th>
                    <th className="py-2 px-3">Trạng thái</th>
                    <th className="py-2 px-3 text-right">Hành động</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-ink-tertiary/10">
                  <tr>
                    <td className="py-3 px-3 font-mono font-bold text-ink-primary">ORD-2026-EU-01</td>
                    <td className="py-3 px-3 font-medium">Châu Âu (EU)</td>
                    <td className="py-3 px-3 font-mono">
                      <MassValue value={50000} decimals={3} />
                    </td>
                    <td className="py-3 px-3">1/2 đã đóng</td>
                    <td className="py-3 px-3">
                      <StatusBadge status="IN_PROGRESS" label="Đang xếp Cont 2" />
                    </td>
                    <td className="py-3 px-3 text-right">
                      <Link
                        to="/export/containers/CONT-02/loading"
                        className="px-2.5 py-1 rounded bg-accent-primary text-white font-medium hover:bg-accent-primary-dim"
                      >
                        Ghi nhận xếp hàng
                      </Link>
                    </td>
                  </tr>
                  <tr>
                    <td className="py-3 px-3 font-mono font-bold text-ink-primary">ORD-2026-JP-02</td>
                    <td className="py-3 px-3 font-medium">Nhật Bản (JP)</td>
                    <td className="py-3 px-3 font-mono">
                      <MassValue value={25000} decimals={3} />
                    </td>
                    <td className="py-3 px-3">0/1 đã đóng</td>
                    <td className="py-3 px-3">
                      <StatusBadge status="PENDING" label="Chờ yêu cầu Plan" />
                    </td>
                    <td className="py-3 px-3 text-right">
                      <Link
                        to="/export/plans"
                        className="px-2.5 py-1 rounded bg-surface-base hover:bg-surface-raised border border-ink-tertiary/20 text-ink-primary font-medium"
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
          <div className="border-b border-ink-tertiary/15 pb-4">
            <h2 className="text-2xl font-serif font-bold text-ink-primary">
              {t('dashboard.procurementOfficerTitle', 'Bàn làm việc Thu mua Hiện trường (Field Client)')}
            </h2>
            <p className="text-xs text-ink-secondary mt-0.5">
              Hỗ trợ làm việc hoàn toàn ngoại tuyến tại ruộng lúa và đồng bộ khi có kết nối internet.
            </p>
          </div>

          <div className="p-4 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 flex flex-col sm:flex-row items-center justify-between gap-4">
            <div className="flex items-center gap-3">
              <NetworkStatusIndicator />
              <span className="text-xs text-ink-secondary">
                Dữ liệu thu mua lưu an toàn trong bộ nhớ trình duyệt cục bộ.
              </span>
            </div>

            <Link
              to="/field/sync"
              className="inline-flex items-center gap-2 px-4 py-2 rounded-md bg-accent-primary text-white font-semibold text-xs hover:bg-accent-primary-dim transition-colors"
            >
              <ArrowsClockwise size={15} />
              <span>Đồng bộ ngay ({pendingOfflineCount} bản ghi)</span>
            </Link>
          </div>

          <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
            <Link
              to="/field/farmers/new"
              className="p-4 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 hover:border-accent-primary transition-all text-center space-y-2 group"
            >
              <div className="w-10 h-10 rounded-full bg-accent-primary/10 text-accent-primary flex items-center justify-center mx-auto group-hover:scale-110 transition-transform">
                <Users size={22} weight="bold" />
              </div>
              <span className="font-semibold text-xs text-ink-primary block">
                Đăng ký Nông hộ
              </span>
            </Link>

            <Link
              to="/field/plots/new"
              className="p-4 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 hover:border-accent-primary transition-all text-center space-y-2 group"
            >
              <div className="w-10 h-10 rounded-full bg-accent-info/10 text-accent-info flex items-center justify-center mx-auto group-hover:scale-110 transition-transform">
                <MapPin size={22} weight="bold" />
              </div>
              <span className="font-semibold text-xs text-ink-primary block">
                Thửa ruộng & Toạ độ
              </span>
            </Link>

            <Link
              to="/field/receipts/new"
              className="p-4 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 hover:border-accent-primary transition-all text-center space-y-2 group"
            >
              <div className="w-10 h-10 rounded-full bg-emerald-500/10 text-accent-primary flex items-center justify-center mx-auto group-hover:scale-110 transition-transform">
                <HandCoins size={22} weight="bold" />
              </div>
              <span className="font-semibold text-xs text-ink-primary block">
                Thu mua lúa tươi
              </span>
            </Link>

            <Link
              to="/field/samples/new"
              className="p-4 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 hover:border-accent-primary transition-all text-center space-y-2 group"
            >
              <div className="w-10 h-10 rounded-full bg-amber-500/10 text-amber-700 dark:text-amber-400 flex items-center justify-center mx-auto group-hover:scale-110 transition-transform">
                <TestTube size={22} weight="bold" />
              </div>
              <span className="font-semibold text-xs text-ink-primary block">
                Lấy mẫu niêm phong
              </span>
            </Link>
          </div>
        </div>
      )}

      {/* =========================================================================
          ROLE 6: MILL ADMINISTRATOR HOME
          ========================================================================= */}
      {currentRole === 'MILL_ADMIN' && (
        <div className="space-y-6">
          <div className="border-b border-ink-tertiary/15 pb-4">
            <h2 className="text-2xl font-serif font-bold text-ink-primary">
              {t('dashboard.adminTitle', 'Trạng thái Hệ thống Quản trị Nhà máy')}
            </h2>
            <p className="text-xs text-ink-secondary mt-0.5">
              Cấu hình cơ sở vật chất, tài khoản phân quyền và quản lý phiên bản bảng giới hạn MRL.
            </p>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
            <div className="p-5 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 space-y-2">
              <span className="text-xs text-ink-tertiary">Công ty khách hàng đang hoạt động</span>
              <div className="flex items-baseline gap-2">
                <span className="font-mono text-3xl font-bold text-accent-primary">4</span>
                <span className="text-xs text-ink-secondary">công ty đối tác</span>
              </div>
              <Link to="/admin/customers" className="text-xs text-accent-primary font-semibold hover:underline block pt-1">
                Quản lý công ty đối tác →
              </Link>
            </div>

            <div className="p-5 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 space-y-2">
              <span className="text-xs text-ink-tertiary">Tài khoản người dùng</span>
              <div className="flex items-baseline gap-2">
                <span className="font-mono text-3xl font-bold text-accent-info">28</span>
                <span className="text-xs text-ink-secondary">tài khoản hoạt động</span>
              </div>
              <Link to="/admin/access" className="text-xs text-accent-info font-semibold hover:underline block pt-1">
                Phân quyền tài khoản →
              </Link>
            </div>

            <div className="p-5 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 space-y-2">
              <span className="text-xs text-ink-tertiary">Phiên bản MRL quốc tế</span>
              <div className="flex items-baseline gap-2">
                <span className="font-mono text-xl font-bold text-amber-700 dark:text-amber-400">
                  EU 2026-Q3
                </span>
              </div>
              <Link to="/compliance/mrl-versions" className="text-xs text-amber-700 dark:text-amber-400 font-semibold hover:underline block pt-1">
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
          <div className="border-b border-ink-tertiary/15 pb-4">
            <h2 className="text-2xl font-serif font-bold text-ink-primary">
              {t('dashboard.stockViewerTitle', 'Vị trí & Cơ cấu Tồn kho Khách hàng')}
            </h2>
            <p className="text-xs text-ink-secondary mt-0.5">
              Quyền xem độc lập dữ liệu tồn kho theo vị trí kho, silo và cơ cấu nguồn gốc thửa ruộng.
            </p>
          </div>

          <div className="p-6 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 space-y-4">
            <div className="flex items-center justify-between">
              <h3 className="font-semibold text-base text-ink-primary">Tồn kho theo vị trí</h3>
              <Link
                to="/stock"
                className="px-4 py-2 rounded-md bg-accent-primary text-white text-xs font-semibold hover:bg-accent-primary-dim"
              >
                Mở bảng tồn kho chi tiết
              </Link>
            </div>
            <p className="text-xs text-ink-secondary leading-relaxed">
              Bạn có quyền truy cập chỉ-đọc (read-only) để kiểm tra số dư khả dụng, số dư đang bảo lưu cho phương án phối trộn, và tỷ lệ phần trăm khối lượng đóng góp từ từng thửa ruộng.
            </p>
          </div>
        </div>
      )}
    </div>
  );
};
