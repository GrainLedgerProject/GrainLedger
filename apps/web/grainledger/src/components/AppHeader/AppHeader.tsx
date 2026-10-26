import { useAuthStore } from "@/app/store/authStore";
import { useUiStore } from "@/app/store/uiStore";
import { ConfirmPopover } from "@/components/ConfirmPopover/ConfirmPopover";
import { NetworkStatusIndicator } from "@/components/NetworkStatusIndicator/NetworkStatusIndicator";
import { RoleChip } from "@/components/RoleChip/RoleChip";
import { isCustomerSide } from "@/shared/types/roles";
import {
  Bell,
  CaretDown,
  Globe,
  LockSimple,
  MagnifyingGlass,
  Moon,
  Question,
  SignOut,
  Sun,
  User,
  WarningCircle,
} from "@phosphor-icons/react";
import { AnimatePresence, motion } from "framer-motion";
import React, { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useNavigate } from "react-router-dom";

export interface AppHeaderProps {
  pageTitle?: string;
  breadcrumbs?: Array<{ label: string; path?: string }>;
}

export const AppHeader: React.FC<AppHeaderProps> = ({
  pageTitle,
  breadcrumbs,
}) => {
  const { t } = useTranslation();
  const { user, logout, hasUnsavedDrafts } = useAuthStore();
  const {
    theme,
    toggleTheme,
    language,
    setLanguage,
    setSearchOpen,
    isNotificationsOpen,
    setNotificationsOpen,
  } = useUiStore();

  const [isUserMenuOpen, setIsUserMenuOpen] = useState(false);
  const [isLogoutModalOpen, setIsLogoutModalOpen] = useState(false);
  const userMenuRef = useRef<HTMLDivElement>(null);
  const navigate = useNavigate();

  // Determine Initials for Avatar
  const getInitials = (name?: string) => {
    if (!name) return "GL";
    const parts = name.trim().split(/\s+/);
    if (parts.length === 1) return parts[0].substring(0, 2).toUpperCase();
    return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
  };

  const handleLogoutConfirm = () => {
    setIsLogoutModalOpen(false);
    logout();
    navigate("/sign-in");
  };

  return (
    <>
      <header className="h-16 backdrop-blur-xl bg-white/70 dark:bg-black/70 border-b border-black/[0.06] dark:border-white/[0.08] px-5 flex items-center justify-between sticky top-0 z-30 transition-all duration-300 shadow-[0_1px_3px_0_rgba(0,0,0,0.02)]">
        {/* Left: Dynamic Title & Breadcrumbs */}
        <div className="flex items-center gap-3 min-w-0">
          <div className="flex flex-col justify-center">
            {breadcrumbs && breadcrumbs.length > 0 ? (
              <nav className="flex items-center gap-1.5 text-xs text-ink-tertiary">
                <Link
                  to="/home"
                  className="hover:text-ink-primary dark:hover:text-white transition-colors"
                >
                  Home
                </Link>
                {breadcrumbs.map((bc, idx) => (
                  <React.Fragment key={idx}>
                    <span className="opacity-40">/</span>
                    {bc.path ? (
                      <Link
                        to={bc.path}
                        className="hover:text-ink-primary dark:hover:text-white transition-colors"
                      >
                        {bc.label}
                      </Link>
                    ) : (
                      <span className="text-ink-primary dark:text-white font-medium">
                        {bc.label}
                      </span>
                    )}
                  </React.Fragment>
                ))}
              </nav>
            ) : null}
          </div>
        </div>

        {/* Center: Global Search Trigger Button (Apple Pill style) */}
        <div className="hidden md:flex items-center justify-center max-w-sm w-full mx-4">
          <button
            type="button"
            onClick={() => setSearchOpen(true)}
            className="w-full flex items-center justify-between px-4 py-2 rounded-full bg-black/[0.03] dark:bg-white/[0.05] border border-black/[0.06] dark:border-white/[0.08] text-xs text-ink-tertiary hover:text-ink-primary dark:hover:text-white hover:bg-black/[0.05] dark:hover:bg-white/[0.08] transition-all duration-200 shadow-xs focus:outline-none"
            aria-label={t(
              "header.searchTitle",
              "Tìm kiếm trong hệ thống (Cmd/Ctrl+K)",
            )}
          >
            <div className="flex items-center gap-2.5">
              <MagnifyingGlass size={15} weight="bold" />
              <span>
                {t(
                  "common.searchPlaceholder",
                  "Tìm kiếm lô, container, nông hộ...",
                )}
              </span>
            </div>
            <kbd className="font-mono text-[10px] px-2 py-0.5 rounded-full bg-white dark:bg-white/10 border border-black/[0.08] dark:border-white/[0.12] text-ink-secondary dark:text-ink-tertiary shadow-2xs">
              ⌘K
            </kbd>
          </button>
        </div>

        {/* Right side controls (Ergonomic Circular Buttons) */}
        <div className="flex items-center gap-1.5 sm:gap-2">
          {/* Network Indicator (for field users or test) */}
          <div className="hidden lg:block">
            <NetworkStatusIndicator />
          </div>

          {/* Search Trigger for Mobile */}
          <button
            type="button"
            onClick={() => setSearchOpen(true)}
            className="md:hidden w-10 h-10 rounded-full flex items-center justify-center text-ink-secondary hover:text-ink-primary dark:hover:text-white hover:bg-black/[0.04] dark:hover:bg-white/[0.08] active:scale-95 transition-all"
            aria-label="Tìm kiếm"
          >
            <MagnifyingGlass size={18} weight="bold" />
          </button>

          {/* Notification Bell with Badge */}
          <div className="relative">
            <button
              type="button"
              onClick={() => setNotificationsOpen(!isNotificationsOpen)}
              className="w-10 h-10 rounded-full flex items-center justify-center relative text-ink-secondary hover:text-ink-primary dark:hover:text-white hover:bg-black/[0.04] dark:hover:bg-white/[0.08] active:scale-95 transition-all"
              aria-label="Thông báo và cảnh báo HOLD"
            >
              <Bell size={18} weight="bold" />
              {/* Badge: 2 notifications */}
              <span className="absolute top-2 right-2 w-2 h-2 rounded-full bg-accent-danger ring-2 ring-white dark:ring-black animate-pulse" />
            </button>

            {/* Notifications Dropdown (Apple Liquid Glass) */}
            <AnimatePresence>
              {isNotificationsOpen && (
                <motion.div
                  initial={{ opacity: 0, scale: 0.96, y: 8 }}
                  animate={{ opacity: 1, scale: 1, y: 0 }}
                  exit={{ opacity: 0, scale: 0.96, y: 8 }}
                  transition={{ type: "spring", stiffness: 400, damping: 28 }}
                  className="absolute right-0 mt-2 w-80 bg-white/90 dark:bg-[#121215]/95 backdrop-blur-2xl border border-black/[0.08] dark:border-white/[0.1] rounded-3xl shadow-apple-card p-4 z-50 text-xs space-y-2.5"
                >
                  <div className="flex items-center justify-between pb-2.5 border-b border-black/[0.06] dark:border-white/[0.08] font-bold text-ink-primary dark:text-white">
                    <span>
                      {t("header.notificationsTitle", "Cảnh báo & Duyệt")}
                    </span>
                    <span className="font-mono text-[10px] bg-red-500/10 text-red-600 dark:text-red-400 font-bold px-2 py-0.5 rounded-full">
                      {t("header.newCount", "2 mới")}
                    </span>
                  </div>

                  <div className="space-y-2 max-h-60 overflow-y-auto">
                    <Link
                      to="/risk/holds"
                      onClick={() => setNotificationsOpen(false)}
                      className="block p-2 rounded bg-functional-hold-bg dark:bg-functional-hold-bg/30 border border-functional-hold-border/40 hover:border-functional-hold-border transition-colors"
                    >
                      <div className="flex items-center gap-1.5 font-bold text-accent-danger">
                        <LockSimple size={14} weight="fill" />
                        <span>
                          {t(
                            "header.holdAlert",
                            "Cảnh báo HOLD mới: Lô LOT-2026-S1",
                          )}
                        </span>
                      </div>
                      <p className="text-[11px] text-ink-secondary mt-0.5">
                        {t(
                          "header.holdAlertDesc",
                          "Tricyclazole tiệm cận ngưỡng MRL EU (0.009 mg/kg / 0.010 mg/kg).",
                        )}
                      </p>
                    </Link>

                    <Link
                      to="/export/plans/pending"
                      onClick={() => setNotificationsOpen(false)}
                      className="block p-2 rounded bg-amber-50 dark:bg-amber-950/30 border border-amber-300 dark:border-amber-700/40 hover:border-accent-warning transition-colors"
                    >
                      <div className="flex items-center gap-1.5 font-bold text-amber-800 dark:text-amber-200">
                        <WarningCircle size={14} weight="fill" />
                        <span>
                          {t(
                            "header.planAlert",
                            "Phương án PLAN-001 chờ duyệt",
                          )}
                        </span>
                      </div>
                      <p className="text-[11px] text-ink-secondary mt-0.5">
                        {t(
                          "header.planAlertDesc",
                          "Container C-101 (24,000 kg Gạo trắng 5% tấm) — Khách hàng Exporter A.",
                        )}
                      </p>
                    </Link>
                  </div>
                </motion.div>
              )}
            </AnimatePresence>
          </div>

          {/* Theme Toggle (Light / Dark) */}
          <button
            type="button"
            onClick={toggleTheme}
            className="w-10 h-10 rounded-full flex items-center justify-center text-ink-secondary hover:text-ink-primary dark:hover:text-white hover:bg-black/[0.04] dark:hover:bg-white/[0.08] active:scale-95 transition-all"
            title={
              theme === "dark"
                ? t("header.switchToLight", "Chuyển sang chế độ sáng")
                : t("header.switchToDark", "Chuyển sang chế độ tối")
            }
            aria-label="Toggle dark mode"
          >
            {theme === "dark" ? (
              <Sun size={18} weight="bold" />
            ) : (
              <Moon size={18} weight="bold" />
            )}
          </button>

          {/* Language Toggle (vi / en) */}
          <button
            type="button"
            onClick={() => setLanguage(language === "vi" ? "en" : "vi")}
            className="h-10 px-3 rounded-full flex items-center gap-1.5 text-ink-secondary hover:text-ink-primary dark:hover:text-white hover:bg-black/[0.04] dark:hover:bg-white/[0.08] active:scale-95 transition-all text-xs font-semibold"
            title={t("header.switchLang", "Chuyển ngôn ngữ / Switch language")}
            aria-label="Toggle language"
          >
            <Globe size={16} weight="bold" />
            <span className="uppercase font-mono text-[11px]">{language}</span>
          </button>

          {/* Help link */}
          <Link
            to="/"
            className="w-10 h-10 rounded-full flex items-center justify-center text-ink-secondary hover:text-ink-primary dark:hover:text-white hover:bg-black/[0.04] dark:hover:bg-white/[0.08] active:scale-95 transition-all"
            title={t("header.helpTitle", "Giới thiệu & Hướng dẫn hệ thống")}
            aria-label="Documentation"
          >
            <Question size={18} weight="bold" />
          </Link>

          {/* User Menu Dropdown (Apple Capsule Pill) */}
          <div className="relative" ref={userMenuRef}>
            <button
              type="button"
              onClick={() => setIsUserMenuOpen(!isUserMenuOpen)}
              className="flex items-center gap-2 p-1.5 pl-2 pr-3 rounded-full hover:bg-black/[0.04] dark:hover:bg-white/[0.08] border border-black/[0.08] dark:border-white/[0.1] active:scale-95 transition-all"
              aria-label={t("header.userMenu", "Menu hồ sơ người dùng")}
            >
              <div className="w-7 h-7 rounded-full bg-[#1B2420] dark:bg-white text-white dark:text-black flex items-center justify-center font-bold text-xs shadow-xs">
                {getInitials(user?.displayName)}
              </div>
              <span className="hidden sm:block text-xs font-semibold text-ink-primary dark:text-white max-w-[120px] truncate">
                {user?.displayName || t("header.notLoggedIn", "Người dùng")}
              </span>
              <CaretDown
                size={12}
                weight="bold"
                className="text-ink-tertiary"
              />
            </button>

            <AnimatePresence>
              {isUserMenuOpen && (
                <motion.div
                  initial={{ opacity: 0, scale: 0.96, y: 8 }}
                  animate={{ opacity: 1, scale: 1, y: 0 }}
                  exit={{ opacity: 0, scale: 0.96, y: 8 }}
                  transition={{ type: "spring", stiffness: 400, damping: 28 }}
                  className="absolute right-0 mt-2 w-72 bg-white/90 dark:bg-[#121215]/95 backdrop-blur-2xl border border-black/[0.08] dark:border-white/[0.1] rounded-3xl shadow-apple-card p-4 z-50 text-xs space-y-3"
                >
                  {/* User Profile Header in Menu */}
                  <div className="space-y-1.5 pb-3 border-b border-black/[0.06] dark:border-white/[0.08]">
                    <p className="font-bold text-sm text-ink-primary dark:text-white truncate">
                      {user?.displayName ||
                        t("header.notLoggedIn", "Chưa đăng nhập")}
                    </p>
                    <p className="text-ink-tertiary truncate font-mono text-[11px]">
                      {user?.email ||
                        t("header.guestEmail", "guest@grainledger.local")}
                    </p>
                    <div className="flex flex-wrap items-center gap-1.5 pt-1">
                      {user && <RoleChip role={user.role} size="sm" />}
                      {user && isCustomerSide(user.role) && (
                        <span className="font-medium text-[10px] px-2 py-0.5 rounded-full bg-blue-500/10 text-blue-600 dark:text-blue-400">
                          {user.organizationName || "Rice"}
                        </span>
                      )}
                    </div>
                  </div>

                  {/* Navigation Links */}
                  <div className="space-y-1">
                    <Link
                      to="/settings/profile"
                      onClick={() => setIsUserMenuOpen(false)}
                      className="flex items-center gap-2.5 px-3 py-2.5 rounded-2xl hover:bg-black/[0.04] dark:hover:bg-white/[0.08] text-ink-primary dark:text-white font-medium transition-colors"
                    >
                      <User
                        size={16}
                        weight="bold"
                        className="text-ink-secondary dark:text-ink-tertiary"
                      />
                      <span>{t("nav.profile", "Thông tin cá nhân")}</span>
                    </Link>

                    <Link
                      to="/settings/security"
                      onClick={() => setIsUserMenuOpen(false)}
                      className="flex items-center gap-2.5 px-3 py-2.5 rounded-2xl hover:bg-black/[0.04] dark:hover:bg-white/[0.08] text-ink-primary dark:text-white font-medium transition-colors"
                    >
                      <LockSimple
                        size={16}
                        weight="bold"
                        className="text-ink-secondary dark:text-ink-tertiary"
                      />
                      <span>{t("nav.security", "Bảo mật tài khoản")}</span>
                    </Link>
                  </div>

                  <div className="border-t border-black/[0.06] dark:border-white/[0.08] pt-2">
                    <button
                      type="button"
                      onClick={() => {
                        setIsUserMenuOpen(false);
                        setIsLogoutModalOpen(true);
                      }}
                      className="w-full flex items-center gap-2.5 px-3 py-2.5 rounded-2xl text-accent-danger hover:bg-red-500/10 font-semibold transition-colors"
                    >
                      <SignOut size={16} weight="bold" />
                      <span>{t("nav.signOut", "Đăng xuất")}</span>
                    </button>
                  </div>
                </motion.div>
              )}
            </AnimatePresence>
          </div>
        </div>
      </header>

      {/* Logout Confirmation Modal */}
      <ConfirmPopover
        isOpen={isLogoutModalOpen}
        title={t("auth.signOutConfirmTitle", "Đăng xuất khỏi GrainLedger?")}
        description={t(
          "auth.signOutConfirmDesc",
          "Phiên làm việc hiện tại của bạn sẽ kết thúc an toàn. Mọi dữ liệu đã ghi nhận vào sổ cái luôn được bảo toàn.",
        )}
        warningNote={
          hasUnsavedDrafts
            ? t(
                "auth.signOutConfirmDraftWarning",
                "Bạn có thay đổi biểu mẫu chưa lưu. Đăng xuất sẽ xoá các nháp chưa commit.",
              )
            : undefined
        }
        confirmLabel={t("auth.confirmSignOut", "Đăng xuất")}
        cancelLabel={t("auth.cancel", "Huỷ")}
        variant="danger"
        onConfirm={handleLogoutConfirm}
        onCancel={() => setIsLogoutModalOpen(false)}
      />
    </>
  );
};
