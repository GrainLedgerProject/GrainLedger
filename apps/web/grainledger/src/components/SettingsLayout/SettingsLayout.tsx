import { LockSimple, ShieldCheck, User } from '@phosphor-icons/react';
import React, { type ReactNode } from 'react';
import { Link, useLocation } from 'react-router-dom';

export interface SettingsLayoutProps {
  children: ReactNode;
}

export const SettingsLayout: React.FC<SettingsLayoutProps> = ({ children }) => {
  const location = useLocation();

  const navLinks = [
    {
      label: 'Thông tin cá nhân',
      description: 'Họ tên, ngôn ngữ, liên hệ',
      path: '/settings/profile',
      icon: User,
    },
    {
      label: 'Bảo mật tài khoản',
      description: 'Đổi mật khẩu và phiên đăng nhập',
      path: '/settings/security',
      icon: LockSimple,
    },
  ];

  return (
    <div className="max-w-5xl mx-auto py-8 px-4 space-y-6">
      <div className="border-b border-ink-tertiary/15 pb-4">
        <h1 className="text-2xl font-serif font-bold text-ink-primary">
          Cài đặt tài khoản & Bảo mật
        </h1>
        <p className="text-sm text-ink-secondary mt-1">
          Quản lý danh tính cá nhân và chứng thực quyền truy cập trên hệ thống GrainLedger.
        </p>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-4 gap-6 items-start">
        {/* Left Settings Sidebar */}
        <aside className="space-y-1.5 md:col-span-1">
          {navLinks.map((item) => {
            const Icon = item.icon;
            const isActive = location.pathname === item.path;

            return (
              <Link
                key={item.path}
                to={item.path}
                className={`flex items-start gap-3 p-3 rounded-lg text-xs transition-colors ${
                  isActive
                    ? 'bg-surface-raised dark:bg-surface-overlay text-accent-primary font-semibold border-l-4 border-accent-primary shadow-xs'
                    : 'text-ink-secondary hover:text-ink-primary hover:bg-surface-raised/50'
                }`}
              >
                <Icon size={18} weight={isActive ? 'fill' : 'regular'} className="shrink-0 mt-0.5" />
                <div>
                  <div className="font-medium text-sm leading-tight text-ink-primary">
                    {item.label}
                  </div>
                  <div className="text-[11px] text-ink-tertiary mt-0.5">{item.description}</div>
                </div>
              </Link>
            );
          })}

          <div className="pt-4 px-3 text-[11px] text-ink-tertiary space-y-1 border-t border-ink-tertiary/10 mt-4">
            <div className="flex items-center gap-1.5 text-accent-primary font-semibold">
              <ShieldCheck size={14} />
              <span>Chứng thực phiên an toàn</span>
            </div>
            <p>Phiên đăng nhập được mã hoá JWT và giám sát thu hồi theo chuẩn bảo mật.</p>
          </div>
        </aside>

        {/* Right Content Area */}
        <main className="md:col-span-3 bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 rounded-lg p-6 sm:p-8 shadow-xs">
          {children}
        </main>
      </div>
    </div>
  );
};
