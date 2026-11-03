import { useAuthStore } from '@/app/store/authStore';
import { isCustomerSide } from '@/shared/types/roles';
import { Buildings, ShieldCheck } from '@phosphor-icons/react';
import React from 'react';

export const CompanyContextBar: React.FC = () => {
  const user = useAuthStore((state) => state.user);

  if (!user || !isCustomerSide(user.role)) {
    return null;
  }

  const companyName = user.organizationName || 'Công ty CP Xuất nhập khẩu Mekong Rice';
  const companyColor = user.organizationColor || 'var(--color-company-a)';

  return (
    <div
      className="w-full px-4 py-1.5 border-b flex items-center justify-between text-xs transition-colors"
      style={{
        backgroundColor: 'rgba(37, 99, 235, 0.05)',
        borderColor: 'rgba(37, 99, 235, 0.15)',
      }}
    >
      <div className="flex items-center gap-2 max-w-screen-xl mx-auto w-full">
        <div className="flex items-center gap-1.5 font-medium text-ink-primary">
          <Buildings size={14} className="text-accent-info" />
          <span
            className="w-2.5 h-2.5 rounded-sm inline-block shadow-xs"
            style={{ backgroundColor: companyColor }}
            title={`Mã màu đại diện: ${companyName}`}
          />
          <span className="font-semibold text-accent-info">{companyName}</span>
          <span className="text-ink-secondary">|</span>
          <span className="text-ink-secondary">
            Phạm vi dữ liệu cách ly theo chuẩn ISO 22095 (Data Isolated Tenant)
          </span>
        </div>

        <div className="ml-auto flex items-center gap-1 text-accent-success font-medium">
          <ShieldCheck size={14} weight="bold" />
          <span>Tenant Isolated</span>
        </div>
      </div>
    </div>
  );
};
