import { useAuthStore } from "@/app/store/authStore";
import { useUiStore } from "@/app/store/uiStore";
import { RoleChip } from "@/components/RoleChip/RoleChip";
import type { UserRole } from "@/shared/types/roles";
import {
  ArrowsClockwise,
  BookOpen,
  Buildings,
  CaretDoubleLeft,
  CaretDoubleRight,
  ChartPieSlice,
  CheckSquare,
  CheckSquareIcon,
  ClockCounterClockwise,
  ClockCounterClockwiseIcon,
  Crosshair,
  Drop,
  Export,
  Factory,
  FileText,
  Flask,
  HandCoins,
  House,
  ListBullets,
  LockSimple,
  MagnifyingGlass,
  MapPin,
  Package,
  Scales,
  ShieldCheck,
  Tag,
  TestTube,
  Truck,
  Users,
} from "@phosphor-icons/react";
import React from "react";
import { useTranslation } from "react-i18next";
import { Link, useLocation } from "react-router-dom";

interface NavItem {
  label: string;
  path: string;
  icon: React.ComponentType<{
    size?: number;
    weight?: "bold" | "fill" | "regular";
    className?: string;
  }>;
  badge?: number | string;
  badgeTone?: "danger" | "warning" | "primary";
  isPrimary?: boolean;
}

export const AppSidebar: React.FC = () => {
  const { t } = useTranslation();
  const user = useAuthStore((state) => state.user);
  const { isSidebarCollapsed, toggleSidebar, pendingOfflineCount } =
    useUiStore();
  const location = useLocation();

  const role: UserRole = user?.role || "MILL_OPERATOR";

  // Role-based Nav Structure defined precisely by SRS §3.1.3
  const getNavItems = (): NavItem[] => {
    switch (role) {
      case "MILL_ADMIN":
        return [
          { label: t("nav.home", "Tổng quan hệ thống"), path: "/home", icon: House },
          { label: t("nav.admin.customers", "Khách hàng"), path: "/admin/customers", icon: Buildings },
          { label: t("nav.admin.access", "Tài khoản & Quyền"), path: "/admin/access", icon: Users },
          {
            label: t("nav.admin.facilities", "Cơ sở & Thiết bị"),
            path: "/admin/facilities",
            icon: Factory,
          },
          {
            label: t("nav.admin.balancePolicy", "Chính sách cân bằng"),
            path: "/admin/balance-policy",
            icon: Scales,
          },
          {
            label: t("nav.admin.receivingThresholds", "Ngưỡng tiếp nhận"),
            path: "/admin/receiving-thresholds",
            icon: Scales,
          },
          {
            label: t("nav.admin.masterData", "Danh mục vận hành"),
            path: "/admin/master-data",
            icon: ListBullets,
          },
          {
            label: t("nav.compliance.mrlVersions", "Phiên bản MRL"),
            path: "/compliance/mrl-versions",
            icon: BookOpen,
          },
          { label: t("nav.events", "Tìm sự kiện"), path: "/events", icon: MagnifyingGlass },
        ];

      case "MILL_OPERATOR":
        return [
          { label: t("nav.mill.today", "Hôm nay tại dây chuyền"), path: "/home", icon: House },
          {
            label: t("nav.mill.receiving", "Tiếp nhận & Cân đến"),
            path: "/mill/receiving",
            icon: Truck,
          },
          {
            label: t("nav.mill.operations", "Ghi bước chế biến"),
            path: "/mill/operations/new",
            icon: Scales,
            isPrimary: true,
          },
          {
            label: t("nav.mill.corrections", "Yêu cầu sửa"),
            path: "/mill/corrections/new",
            icon: ClockCounterClockwiseIcon,
          },
          {
            label: t("nav.mill.commitCorrection", "Ghi bút toán bù"),
            path: "/mill/corrections/pending",
            icon: CheckSquareIcon,
            badge: 2,
            badgeTone: "warning",
          },
          {
            label: t("nav.mill.planExecutions", "Thực thi plan"),
            path: "/mill/plan-executions/PLAN-001",
            icon: ArrowsClockwise,
            badge: 1,
            badgeTone: "primary",
          },
          { label: t("nav.events", "Tìm sự kiện"), path: "/events", icon: MagnifyingGlass },
        ];

      case "MILL_MANAGER":
        return [
          { label: t("nav.manager.home", "Bàn điều hành Quản lý"), path: "/home", icon: House },
          {
            label: t("nav.manager.receiving", "Tiếp nhận & Đối soát cân"),
            path: "/mill/receiving",
            icon: Truck,
          },
          {
            label: t("nav.mill.reconciliations", "Đối soát & Xác nhận"),
            path: "/mill/reconciliations",
            icon: CheckSquare,
            badge: 1,
            badgeTone: "warning",
          },
          {
            label: t("nav.mill.commitCorrection", "Ghi bút toán bù"),
            path: "/mill/corrections/pending",
            icon: ClockCounterClockwise,
          },
          {
            label: t("nav.risk.holdAlerts", "Cảnh báo HOLD"),
            path: "/risk/holds",
            icon: LockSimple,
            badge: 1,
            badgeTone: "danger",
          },
          {
            label: t("nav.admin.balancePolicy", "Chính sách cân bằng"),
            path: "/admin/balance-policy",
            icon: Scales,
          },
          {
            label: t("nav.mill.operations", "Ghi bước chế biến"),
            path: "/mill/operations/new",
            icon: Scales,
          },
          { label: t("nav.events", "Tìm sự kiện"), path: "/events", icon: MagnifyingGlass },
        ];

      case "CUSTOMER_PROCUREMENT_OFFICER":
        return [
          { label: t("nav.field.home", "Bàn làm việc thu mua"), path: "/home", icon: House },
          {
            label: t("nav.field.sync", "Đồng bộ / Hàng đợi"),
            path: "/field/sync",
            icon: ArrowsClockwise,
            badge: pendingOfflineCount,
            badgeTone: pendingOfflineCount > 0 ? "warning" : undefined,
          },
          { label: t("nav.field.farmers", "Nông hộ"), path: "/field/farmers/new", icon: Users },
          { label: t("nav.field.plots", "Thửa ruộng"), path: "/field/plots/new", icon: MapPin },
          {
            label: t("nav.field.declarations", "Khai báo thuốc BVTV"),
            path: "/field/declarations/new",
            icon: Drop,
          },
          {
            label: t("nav.field.receipts", "Thu mua lúa"),
            path: "/field/receipts/new",
            icon: HandCoins,
          },
          {
            label: t("nav.field.samples", "Lấy mẫu niêm phong"),
            path: "/field/samples/new",
            icon: TestTube,
          },
          { label: t("nav.events", "Tìm sự kiện"), path: "/events", icon: MagnifyingGlass },
        ];

      case "CUSTOMER_MANAGER":
        return [
          {
            label: t("nav.customerManager.home", "Điều phối Chất lượng & Rủi ro"),
            path: "/home",
            icon: House,
          },
          {
            label: t("nav.risk.traceback", "Truy vết hai chiều"),
            path: "/risk/traceback",
            icon: Crosshair,
            isPrimary: true,
          },
          {
            label: t("nav.risk.holds", "Lệnh giữ HOLD"),
            path: "/risk/holds",
            icon: LockSimple,
            badge: 1,
            badgeTone: "danger",
          },
          {
            label: t("nav.compliance.labSamples", "Mẫu xét nghiệm"),
            path: "/compliance/lab-samples",
            icon: Flask,
          },
          {
            label: t("nav.compliance.labResults", "Kết quả xét nghiệm"),
            path: "/compliance/lab-results",
            icon: TestTube,
          },
          {
            label: t("nav.compliance.safetyMargin", "Biên an toàn MRL"),
            path: "/compliance/safety-margin",
            icon: ShieldCheck,
          },
          {
            label: t("nav.export.plansPending", "Duyệt phương án"),
            path: "/export/plans/pending",
            icon: CheckSquare,
            badge: 1,
            badgeTone: "warning",
          },
          {
            label: t("nav.compliance.mrlVersions", "Phiên bản MRL"),
            path: "/compliance/mrl-versions",
            icon: BookOpen,
          },
          { label: t("nav.events", "Tìm sự kiện"), path: "/events", icon: MagnifyingGlass },
        ];

      case "CUSTOMER_EXPORT_OFFICER":
        return [
          {
            label: t("nav.exportOfficer.home", "Điều phối Đơn hàng & Xuất khẩu"),
            path: "/home",
            icon: House,
          },
          {
            label: t("nav.export.orders", "Đơn xuất khẩu"),
            path: "/export/orders",
            icon: Export,
            isPrimary: true,
          },
          {
            label: t("nav.export.plans", "Yêu cầu phương án"),
            path: "/export/plans",
            icon: ChartPieSlice,
          },
          { label: t("nav.export.containers", "Xếp container"), path: "/export/containers", icon: Truck },
          {
            label: t("nav.export.dossiers", "Hồ sơ & Chứng từ"),
            path: "/export/dossiers",
            icon: FileText,
          },
          { label: t("nav.risk.holdAlerts", "Cảnh báo HOLD"), path: "/risk/holds", icon: LockSimple },
          {
            label: t("nav.compliance.mrlVersions", "Phiên bản MRL"),
            path: "/compliance/mrl-versions",
            icon: BookOpen,
          },
          { label: t("nav.events", "Tìm sự kiện"), path: "/events", icon: MagnifyingGlass },
        ];

      case "AUTHORIZED_STOCK_VIEWER":
        return [
          { label: t("nav.stockViewer.home", "Tổng quan tồn kho"), path: "/home", icon: House },
          {
            label: t("nav.stock.overview", "Tồn kho theo vị trí"),
            path: "/stock",
            icon: Package,
            isPrimary: true,
          },
          {
            label: t("nav.stock.lots", "Chi tiết lô lúa/gạo"),
            path: "/stock/lots/LOT-2026-S1",
            icon: Tag,
          },
        ];

      default:
        return [
          { label: t("nav.home", "Tổng quan"), path: "/home", icon: House },
          { label: t("nav.events", "Tìm sự kiện"), path: "/events", icon: MagnifyingGlass },
        ];
    }
  };

  const navItems = getNavItems();

  return (
    <aside
      className={`h-screen sticky top-0 backdrop-blur-2xl bg-white/75 dark:bg-[#0E0E11]/85 border-r border-black/[0.06] dark:border-white/[0.08] flex flex-col justify-between transition-all duration-300 z-40 shadow-[1px_0_3px_0_rgba(0,0,0,0.02)] ${
        isSidebarCollapsed ? "w-[68px]" : "w-[260px]"
      }`}
      aria-label="Sidebar Navigation"
    >
      {/* Top Branding Section */}
      <div>
        <div className="h-16 px-4 flex items-center justify-between border-b border-black/[0.06] dark:border-white/[0.08]">
          <Link
            to="/home"
            className={`flex items-center gap-3 overflow-hidden transition-all group ${
              isSidebarCollapsed ? "justify-center w-full" : ""
            }`}
            title="GrainLedger — Về trang chủ"
          >
            <div className="w-8 h-8 rounded-xl flex items-center justify-center shrink-0 shadow-xs transition-transform duration-200 group-hover:scale-105">
              <img src="/favicon.svg" alt="GrainLedger" className="w-8 h-8" />
            </div>
            {!isSidebarCollapsed && (
              <div className="flex flex-col min-w-0">
                <span className="font-sans font-extrabold text-base text-ink-primary dark:text-white tracking-tight leading-tight">
                  GrainLedger
                </span>
                <span className="text-[10px] text-ink-tertiary uppercase tracking-wider truncate font-mono">
                  ISO 22095 Custody
                </span>
              </div>
            )}
          </Link>
        </div>

        {/* Navigation Links */}
        <nav className="p-3 space-y-1.5 overflow-y-auto max-h-[calc(100vh-140px)]">
          {navItems.map((item) => {
            const Icon = item.icon;
            const isActive =
              location.pathname === item.path ||
              (item.path !== "/home" &&
                location.pathname.startsWith(item.path));

            return (
              <Link
                key={item.path}
                to={item.path}
                className={`group relative flex items-center gap-3 px-3.5 py-2.5 rounded-2xl text-xs font-medium transition-all duration-200 ${
                  isActive
                    ? "bg-emerald-500/10 dark:bg-emerald-400/10 text-[#2F6B3A] dark:text-emerald-400 font-semibold shadow-xs border border-emerald-500/20 dark:border-emerald-400/20"
                    : "text-ink-secondary dark:text-ink-tertiary hover:text-ink-primary dark:hover:text-white hover:bg-black/[0.03] dark:hover:bg-white/[0.05]"
                } ${isSidebarCollapsed ? "justify-center px-0" : ""}`}
                title={isSidebarCollapsed ? item.label : undefined}
              >
                <Icon
                  size={19}
                  weight={isActive ? "fill" : "regular"}
                  className={`shrink-0 transition-transform duration-200 group-hover:scale-105 ${
                    isActive
                      ? "text-[#2F6B3A] dark:text-emerald-400"
                      : "text-ink-tertiary group-hover:text-ink-primary dark:group-hover:text-white"
                  }`}
                />

                {!isSidebarCollapsed && (
                  <span className="truncate flex-1 text-left">
                    {item.label}
                  </span>
                )}

                {/* Badge indicator */}
                {!isSidebarCollapsed && item.badge !== undefined && (
                  <span
                    className={`text-[10px] font-bold px-2 py-0.5 rounded-full font-mono shrink-0 shadow-2xs ${
                      item.badgeTone === "danger"
                        ? "bg-accent-danger text-white"
                        : item.badgeTone === "warning"
                          ? "bg-accent-warning text-ink-primary"
                          : "bg-[#2F6B3A] text-white"
                    }`}
                  >
                    {item.badge}
                  </span>
                )}

                {/* Tooltip on collapsed state */}
                {isSidebarCollapsed && item.badge !== undefined && (
                  <span
                    className={`absolute top-1.5 right-1.5 w-2.5 h-2.5 rounded-full ring-2 ring-white dark:ring-black ${
                      item.badgeTone === "danger"
                        ? "bg-accent-danger"
                        : item.badgeTone === "warning"
                          ? "bg-accent-warning"
                          : "bg-[#2F6B3A]"
                    }`}
                  />
                )}
              </Link>
            );
          })}
        </nav>
      </div>

      {/* Bottom Section: Role Chip & Collapse Toggle */}
      <div className="p-3 border-t border-black/[0.06] dark:border-white/[0.08] space-y-2">
        {!isSidebarCollapsed && (
          <div className="px-1 py-1">
            <RoleChip
              role={role}
              size="sm"
              showCode
              className="w-full justify-center"
            />
          </div>
        )}

        <button
          type="button"
          onClick={toggleSidebar}
          className="w-full flex items-center justify-center p-2.5 rounded-2xl text-ink-tertiary hover:text-ink-primary dark:hover:text-white hover:bg-black/[0.04] dark:hover:bg-white/[0.08] active:scale-95 transition-all text-xs focus:outline-none"
          aria-label={
            isSidebarCollapsed
              ? t("common.expand", "Mở rộng thanh điều hướng")
              : t("common.collapse", "Thu gọn thanh điều hướng")
          }
          title={
            isSidebarCollapsed
              ? t("common.expand", "Mở rộng thanh điều hướng")
              : t("common.collapse", "Thu gọn thanh điều hướng")
          }
        >
          {isSidebarCollapsed ? (
            <CaretDoubleRight size={16} weight="bold" />
          ) : (
            <CaretDoubleLeft size={16} weight="bold" />
          )}
          {!isSidebarCollapsed && (
            <span className="ml-2 text-xs font-semibold">{t("common.collapse", "Thu gọn")}</span>
          )}
        </button>
      </div>
    </aside>
  );
};
