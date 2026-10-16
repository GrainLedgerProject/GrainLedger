import { useAuthStore } from '@/app/store/authStore';
import { useUiStore } from '@/app/store/uiStore';
import { ConfirmPopover } from '@/components/ConfirmPopover/ConfirmPopover';
import { NetworkStatusIndicator } from '@/components/NetworkStatusIndicator/NetworkStatusIndicator';
import { RoleChip } from '@/components/RoleChip/RoleChip';
import { isCustomerSide } from '@/shared/types/roles';
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
} from '@phosphor-icons/react';
import { AnimatePresence, motion } from 'framer-motion';
import React, { useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useLocation, useNavigate } from 'react-router-dom';

export interface AppHeaderProps {
  pageTitle?: string;
  breadcrumbs?: Array<{ label: string; path?: string }>;
}

export const AppHeader: React.FC<AppHeaderProps> = ({ pageTitle, breadcrumbs }) => {
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
  const location = useLocation();
  const navigate = useNavigate();

  // Determine Initials for Avatar
  const getInitials = (name?: string) => {
    if (!name) return 'GL';
    const parts = name.trim().split(/\s+/);
    if (parts.length === 1) return parts[0].substring(0, 2).toUpperCase();
    return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
  };

  const handleLogoutConfirm = () => {
    setIsLogoutModalOpen(false);
    logout();
    navigate('/sign-in');
  };

  return (
    <>
      <header className="h-14 bg-surface-base dark:bg-surface-raised border-b border-ink-tertiary/20 px-4 flex items-center justify-between sticky top-0 z-30 transition-colors">
        {/* Left: Dynamic Title & Breadcrumbs */}
        <div className="flex items-center gap-3 min-w-0">
          <div className="flex flex-col justify-center">
            {breadcrumbs && breadcrumbs.length > 0 ? (
              <nav className="flex items-center gap-1.5 text-xs text-ink-tertiary">
                <Link to="/home" className="hover:text-ink-primary">
                  Home
                </Link>
                {breadcrumbs.map((bc, idx) => (
                  <React.Fragment key={idx}>
                    <span>/</span>
                    {bc.path ? (
                      <Link to={bc.path} className="hover:text-ink-primary">
                        {bc.label}
                      </Link>
                    ) : (
                      <span className="text-ink-primary font-medium">{bc.label}</span>
                    )}
                  </React.Fragment>
                ))}
              </nav>
            ) : null}
            <h1 className="text-base font-semibold text-ink-primary truncate font-body">
              {pageTitle || t('header.defaultTitle', 'Bàn làm việc vận hành')}
            </h1>
          </div>
        </div>

        {/* Center: Global Search Trigger Button */}
        <div className="hidden md:flex items-center justify-center max-w-sm w-full mx-4">
          <button
            type="button"
            onClick={() => setSearchOpen(true)}
            className="w-full flex items-center justify-between px-3 py-1.5 rounded-md bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/25 text-xs text-ink-tertiary hover:text-ink-primary hover:border-ink-tertiary/40 transition-colors shadow-xs"
            aria-label={t('header.searchTitle', 'Tìm kiếm trong hệ thống (Cmd/Ctrl+K)')}
          >
            <div className="flex items-center gap-2">
              <MagnifyingGlass size={15} />
              <span>{t('common.searchPlaceholder', 'Tìm kiếm lô, container, nông hộ...')}</span>
            </div>
            <kbd className="font-mono text-[10px] px-1.5 py-0.5 rounded bg-surface-base border border-ink-tertiary/30">
              ⌘K
            </kbd>
          </button>
        </div>

        {/* Right side controls */}
        <div className="flex items-center gap-2">
          {/* Network Indicator (for field users or test) */}
          <div className="hidden lg:block">
            <NetworkStatusIndicator />
          </div>

          {/* Search Trigger for Mobile */}
          <button
            type="button"
            onClick={() => setSearchOpen(true)}
            className="md:hidden p-2 rounded-md text-ink-secondary hover:text-ink-primary hover:bg-surface-raised transition-colors"
            aria-label="Tìm kiếm"
          >
            <MagnifyingGlass size={18} />
          </button>

          {/* Notification Bell with Badge */}
          <div className="relative">
            <button
              type="button"
              onClick={() => setNotificationsOpen(!isNotificationsOpen)}
              className="relative p-2 rounded-md text-ink-secondary hover:text-ink-primary hover:bg-surface-raised transition-colors"
              aria-label="Thông báo và cảnh báo HOLD"
            >
              <Bell size={18} />
              {/* Badge: 2 notifications */}
              <span className="absolute top-1 right-1 w-2 h-2 rounded-full bg-accent-danger ring-2 ring-surface-base" />
            </button>

            {/* Notifications Dropdown */}
            <AnimatePresence>
              {isNotificationsOpen && (
                <motion.div
                  initial={{ opacity: 0, y: 6 }}
                  animate={{ opacity: 1, y: 0 }}
                  exit={{ opacity: 0, y: 6 }}
                  className="absolute right-0 mt-2 w-80 bg-surface-base dark:bg-surface-raised border border-ink-tertiary/25 rounded-lg shadow-lg p-3 z-50 text-xs space-y-2"
                >
                  <div className="flex items-center justify-between pb-2 border-b border-ink-tertiary/15 font-semibold text-ink-primary">
                    <span>{t('header.notificationsTitle', 'Cảnh báo & Hoạt động cần duyệt')}</span>
                    <span className="font-mono text-[10px] bg-red-100 text-accent-danger px-1.5 py-0.5 rounded">
                      {t('header.newCount', '2 mới')}
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
                        <span>{t('header.holdAlert', 'Cảnh báo HOLD mới: Lô LOT-2026-S1')}</span>
                      </div>
                      <p className="text-[11px] text-ink-secondary mt-0.5">
                        {t('header.holdAlertDesc', 'Tricyclazole tiệm cận ngưỡng MRL EU (0.009 mg/kg / 0.010 mg/kg).')}
                      </p>
                    </Link>

                    <Link
                      to="/export/plans/pending"
                      onClick={() => setNotificationsOpen(false)}
                      className="block p-2 rounded bg-amber-50 dark:bg-amber-950/30 border border-amber-300 dark:border-amber-700/40 hover:border-accent-warning transition-colors"
                    >
                      <div className="flex items-center gap-1.5 font-bold text-amber-800 dark:text-amber-200">
                        <WarningCircle size={14} weight="fill" />
                        <span>{t('header.planAlert', 'Phương án PLAN-001 chờ duyệt')}</span>
                      </div>
                      <p className="text-[11px] text-ink-secondary mt-0.5">
                        {t('header.planAlertDesc', 'Container C-101 (24,000 kg Gạo trắng 5% tấm) — Khách hàng Exporter A.')}
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
            className="p-2 rounded-md text-ink-secondary hover:text-ink-primary hover:bg-surface-raised transition-colors"
            title={theme === 'dark' ? t('header.switchToLight', 'Chuyển sang chế độ sáng') : t('header.switchToDark', 'Chuyển sang chế độ tối')}
            aria-label="Toggle dark mode"
          >
            {theme === 'dark' ? <Sun size={18} /> : <Moon size={18} />}
          </button>

          {/* Language Toggle (vi / en) */}
          <button
            type="button"
            onClick={() => setLanguage(language === 'vi' ? 'en' : 'vi')}
            className="flex items-center gap-1 p-2 rounded-md text-ink-secondary hover:text-ink-primary hover:bg-surface-raised transition-colors text-xs font-semibold"
            title={t('header.switchLang', 'Chuyển ngôn ngữ / Switch language')}
            aria-label="Toggle language"
          >
            <Globe size={16} />
            <span className="uppercase font-mono">{language}</span>
          </button>

          {/* Help link */}
          <Link
            to="/"
            className="p-2 rounded-md text-ink-secondary hover:text-ink-primary hover:bg-surface-raised transition-colors"
            title={t('header.helpTitle', 'Giới thiệu & Hướng dẫn hệ thống')}
            aria-label="Documentation"
          >
            <Question size={18} />
          </Link>

          {/* User Menu Dropdown */}
          <div className="relative" ref={userMenuRef}>
            <button
              type="button"
              onClick={() => setIsUserMenuOpen(!isUserMenuOpen)}
              className="flex items-center gap-2 p-1 pl-2 rounded-full hover:bg-surface-raised dark:hover:bg-surface-overlay border border-ink-tertiary/20 transition-colors"
              aria-label={t('header.userMenu', 'Menu hồ sơ người dùng')}
            >
              <div className="w-7 h-7 rounded-full bg-accent-primary text-white flex items-center justify-center font-bold text-xs shadow-xs">
                {getInitials(user?.displayName)}
              </div>
              <span className="hidden sm:block text-xs font-semibold text-ink-primary max-w-[120px] truncate">
                {user?.displayName || t('header.notLoggedIn', 'Người dùng')}
              </span>
              <CaretDown size={12} className="text-ink-tertiary" />
            </button>

            <AnimatePresence>
              {isUserMenuOpen && (
                <motion.div
                  initial={{ opacity: 0, scale: 0.96, y: 6 }}
                  animate={{ opacity: 1, scale: 1, y: 0 }}
                  exit={{ opacity: 0, scale: 0.96, y: 6 }}
                  transition={{ duration: 0.15 }}
                  className="absolute right-0 mt-2 w-72 bg-surface-base dark:bg-surface-raised border border-ink-tertiary/25 rounded-lg shadow-xl p-3 z-50 text-xs space-y-3"
                >
                  {/* User Profile Header in Menu */}
                  <div className="space-y-1.5 pb-2.5 border-b border-ink-tertiary/15">
                    <p className="font-bold text-sm text-ink-primary truncate">
                      {user?.displayName || t('header.notLoggedIn', 'Chưa đăng nhập')}
                    </p>
                    <p className="text-ink-tertiary truncate font-mono text-[11px]">
                      {user?.email || t('header.guestEmail', 'guest@grainledger.local')}
                    </p>
                    <div className="flex flex-wrap items-center gap-1.5 pt-1">
                      {user && <RoleChip role={user.role} size="sm" />}
                      {user && isCustomerSide(user.role) && (
                        <span className="font-medium text-[10px] px-1.5 py-0.5 rounded bg-blue-100 text-blue-800 dark:bg-blue-950/60 dark:text-blue-300">
                          {user.organizationName || 'Rice'}
                        </span>
                      )}
                    </div>
                  </div>

                  {/* Navigation Links */}
                  <div className="space-y-1">
                    <Link
                      to="/settings/profile"
                      onClick={() => setIsUserMenuOpen(false)}
                      className="flex items-center gap-2.5 px-2.5 py-2 rounded-md hover:bg-surface-raised dark:hover:bg-surface-overlay text-ink-primary transition-colors"
                    >
                      <User size={16} className="text-ink-secondary" />
                      <span>{t('nav.profile', 'Thông tin cá nhân')}</span>
                    </Link>

                    <Link
                      to="/settings/security"
                      onClick={() => setIsUserMenuOpen(false)}
                      className="flex items-center gap-2.5 px-2.5 py-2 rounded-md hover:bg-surface-raised dark:hover:bg-surface-overlay text-ink-primary transition-colors"
                    >
                      <LockSimple size={16} className="text-ink-secondary" />
                      <span>{t('nav.security', 'Bảo mật tài khoản')}</span>
                    </Link>
                  </div>

                  <div className="border-t border-ink-tertiary/15 pt-2">
                    <button
                      type="button"
                      onClick={() => {
                        setIsUserMenuOpen(false);
                        setIsLogoutModalOpen(true);
                      }}
                      className="w-full flex items-center gap-2.5 px-2.5 py-2 rounded-md text-accent-danger hover:bg-red-50 dark:hover:bg-red-950/30 font-semibold transition-colors"
                    >
                      <SignOut size={16} />
                      <span>{t('nav.signOut', 'Đăng xuất')}</span>
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
        title={t('auth.signOutConfirmTitle', 'Đăng xuất khỏi GrainLedger?')}
        description={t(
          'auth.signOutConfirmDesc',
          'Phiên làm việc hiện tại của bạn sẽ kết thúc an toàn. Mọi dữ liệu đã ghi nhận vào sổ cái luôn được bảo toàn.'
        )}
        warningNote={
          hasUnsavedDrafts
            ? t(
                'auth.signOutConfirmDraftWarning',
                'Bạn có thay đổi biểu mẫu chưa lưu. Đăng xuất sẽ xoá các nháp chưa commit.'
              )
            : undefined
        }
        confirmLabel={t('auth.confirmSignOut', 'Đăng xuất')}
        cancelLabel={t('auth.cancel', 'Huỷ')}
        variant="danger"
        onConfirm={handleLogoutConfirm}
        onCancel={() => setIsLogoutModalOpen(false)}
      />
    </>
  );
};
