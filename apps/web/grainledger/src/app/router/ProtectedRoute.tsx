import { useAuthStore } from "@/app/store/authStore";
import { RoleChip } from "@/components/RoleChip/RoleChip";
import type { UserRole } from "@/shared/types/roles";
import { ArrowLeft, ShieldWarning } from "@phosphor-icons/react";
import React, { type ReactNode } from "react";
import { Navigate, useLocation, useNavigate } from "react-router-dom";

interface ProtectedRouteProps {
  children: ReactNode;
  allowedRoles?: UserRole[];
}

export const ProtectedRoute: React.FC<ProtectedRouteProps> = ({
  children,
  allowedRoles,
}) => {
  const { isAuthenticated, user } = useAuthStore();
  const location = useLocation();
  const navigate = useNavigate();

  // If not authenticated, redirect to sign-in with returnUrl
  if (!isAuthenticated || !user) {
    return (
      <Navigate
        to={`/sign-in?returnUrl=${encodeURIComponent(location.pathname)}`}
        replace
      />
    );
  }

  // If specific roles are required, verify access
  if (allowedRoles && !allowedRoles.includes(user.role)) {
    return (
      <div className="max-w-xl mx-auto py-16 px-4 text-center space-y-4">
        <div className="w-16 h-16 rounded-full bg-red-100 dark:bg-red-950/40 text-accent-danger flex items-center justify-center mx-auto">
          <ShieldWarning size={32} weight="fill" />
        </div>
        <h2 className="text-xl font-bold text-ink-primary">
          Không có quyền truy cập màn hình này
        </h2>
        <p className="text-xs text-ink-secondary leading-relaxed">
          Tài khoản của bạn hiện có vai trò{" "}
          <span className="inline-block align-middle mx-1">
            <RoleChip role={user.role} size="sm" />
          </span>
          . Màn hình này chỉ dành cho:{" "}
          {allowedRoles.map((r) => (
            <span key={r} className="inline-block align-middle mx-0.5">
              <RoleChip role={r} size="sm" />
            </span>
          ))}
          .
        </p>

        <div className="pt-4 flex items-center justify-center gap-3">
          <button
            type="button"
            onClick={() => navigate("/home")}
            className="inline-flex items-center gap-1.5 px-4 py-2 rounded-md bg-accent-primary text-white text-xs font-semibold hover:bg-accent-primary-dim transition-colors"
          >
            <ArrowLeft size={14} />
            Về bàn làm việc của tôi
          </button>
        </div>
      </div>
    );
  }

  return <>{children}</>;
};
