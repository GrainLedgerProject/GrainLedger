/**
 * @screen SC-01d (Account Security)
 * @feature Auth
 * @roles ALL Authenticated Users
 * @route /settings/security
 * @description Changes active account password with inline validation and security feedback.
 */
import { GrainLoader } from '@/components/GrainLoader/GrainLoader';
import { SettingsLayout } from '@/components/SettingsLayout/SettingsLayout';
import { authApi } from '@/features/auth/api/authApi';
import {
  changePasswordSchema,
  type ChangePasswordFormValues,
} from '@/features/auth/model/authSchemas';
import { zodResolver } from '@hookform/resolvers/zod';
import {
  CheckCircle,
  Eye,
  EyeSlash,
  LockSimple,
  ShieldCheck,
  WarningCircle,
} from '@phosphor-icons/react';
import React, { useMemo, useState } from 'react';
import { useForm } from 'react-hook-form';

export const ChangePasswordPage: React.FC = () => {
  const [showCurrentPass, setShowCurrentPass] = useState(false);
  const [showNewPass, setShowNewPass] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [customError, setCustomError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    watch,
    reset,
    formState: { errors },
  } = useForm<ChangePasswordFormValues>({
    resolver: zodResolver(changePasswordSchema),
  });

  const newPasswordValue = watch('newPassword', '');

  // Strength score
  const passwordStrength = useMemo(() => {
    if (!newPasswordValue) return { score: 0, label: 'Chưa nhập', color: 'bg-ink-tertiary/20' };
    let score = 0;
    if (newPasswordValue.length >= 12) score += 1;
    if (/[A-Z]/.test(newPasswordValue)) score += 1;
    if (/[a-z]/.test(newPasswordValue)) score += 1;
    if (/[0-9]/.test(newPasswordValue)) score += 1;
    if (/[^A-Za-z0-9]/.test(newPasswordValue)) score += 1;

    if (score <= 2) {
      return { score: 1, label: 'Yếu (Weak)', color: 'bg-accent-danger' };
    } else if (score <= 4) {
      return { score: 2, label: 'Khá (Fair)', color: 'bg-accent-warning' };
    } else {
      return { score: 3, label: 'Mạnh (Strong)', color: 'bg-accent-primary' };
    }
  }, [newPasswordValue]);

  const onSubmit = async (values: ChangePasswordFormValues) => {
    setSuccessMessage(null);
    setCustomError(null);
    setIsSubmitting(true);

    try {
      await authApi.changePassword(values.currentPassword, values.newPassword);
      setSuccessMessage('Mật khẩu đã được cập nhật thành công.');
      reset();
    } catch (err: unknown) {
      if (err instanceof Error) {
        setCustomError(err.message);
      } else {
        setCustomError('Đã có lỗi xảy ra khi đổi mật khẩu.');
      }
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <SettingsLayout>
      <div className="space-y-6">
        <div className="border-b border-ink-tertiary/15 pb-4">
          <h2 className="text-lg font-bold text-ink-primary font-serif">
            Bảo mật tài khoản
          </h2>
          <p className="text-xs text-ink-secondary mt-0.5">
            Mật khẩu mới phải có tối thiểu 12 ký tự và không trùng với mật khẩu hiện tại.
          </p>
        </div>

        {/* Inline Success Notice */}
        {successMessage && (
          <div
            className="p-3.5 rounded-md bg-functional-balanced-bg border border-functional-balanced-border text-accent-primary text-xs flex items-center gap-2.5 animate-fadeIn"
            role="status"
          >
            <CheckCircle size={20} weight="fill" className="shrink-0" />
            <span className="font-semibold">{successMessage}</span>
          </div>
        )}

        {/* Inline Error Notice */}
        {customError && (
          <div
            className="p-3.5 rounded-md bg-functional-rejected-bg border border-functional-rejected-border text-accent-danger text-xs flex items-center gap-2.5 animate-ledger-shake"
            role="alert"
          >
            <WarningCircle size={20} weight="fill" className="shrink-0" />
            <span className="font-semibold">{customError}</span>
          </div>
        )}

        <form onSubmit={handleSubmit(onSubmit)} className="space-y-5 max-w-md">
          {/* Current Password */}
          <div>
            <label className="block text-xs font-semibold text-ink-secondary mb-1">
              Mật khẩu hiện tại
            </label>
            <div className="relative">
              <input
                type={showCurrentPass ? 'text' : 'password'}
                autoComplete="current-password"
                {...register('currentPassword')}
                placeholder="••••••••••••"
                className={`w-full pl-9 pr-10 py-2 text-sm rounded-md bg-surface-base dark:bg-surface-raised border ${
                  errors.currentPassword ? 'border-accent-danger' : 'border-ink-tertiary/30'
                } focus:border-accent-primary focus:outline-none transition-colors font-mono`}
              />
              <LockSimple size={16} className="absolute left-3 top-2.5 text-ink-tertiary" />
              <button
                type="button"
                onClick={() => setShowCurrentPass(!showCurrentPass)}
                className="absolute right-3 top-2.5 text-ink-tertiary hover:text-ink-primary"
                aria-label="Toggle current password visibility"
              >
                {showCurrentPass ? <EyeSlash size={16} /> : <Eye size={16} />}
              </button>
            </div>
            {errors.currentPassword && (
              <p className="text-xs text-accent-danger mt-1">
                {errors.currentPassword.message}
              </p>
            )}
          </div>

          {/* New Password */}
          <div>
            <label className="block text-xs font-semibold text-ink-secondary mb-1">
              Mật khẩu mới
            </label>
            <div className="relative">
              <input
                type={showNewPass ? 'text' : 'password'}
                autoComplete="new-password"
                {...register('newPassword')}
                placeholder="••••••••••••"
                className={`w-full pl-9 pr-10 py-2 text-sm rounded-md bg-surface-base dark:bg-surface-raised border ${
                  errors.newPassword ? 'border-accent-danger' : 'border-ink-tertiary/30'
                } focus:border-accent-primary focus:outline-none transition-colors font-mono`}
              />
              <LockSimple size={16} className="absolute left-3 top-2.5 text-ink-tertiary" />
              <button
                type="button"
                onClick={() => setShowNewPass(!showNewPass)}
                className="absolute right-3 top-2.5 text-ink-tertiary hover:text-ink-primary"
                aria-label="Toggle new password visibility"
              >
                {showNewPass ? <EyeSlash size={16} /> : <Eye size={16} />}
              </button>
            </div>

            {/* Strength meter bar */}
            {newPasswordValue && (
              <div className="mt-2 space-y-1">
                <div className="flex items-center justify-between text-[11px]">
                  <span className="text-ink-tertiary">Độ phức tạp:</span>
                  <span className="font-semibold text-ink-primary">{passwordStrength.label}</span>
                </div>
                <div className="h-1.5 w-full bg-surface-base rounded-full overflow-hidden flex gap-1">
                  <div
                    className={`h-full flex-1 transition-all rounded-full ${
                      passwordStrength.score >= 1 ? passwordStrength.color : 'bg-transparent'
                    }`}
                  />
                  <div
                    className={`h-full flex-1 transition-all rounded-full ${
                      passwordStrength.score >= 2 ? passwordStrength.color : 'bg-transparent'
                    }`}
                  />
                  <div
                    className={`h-full flex-1 transition-all rounded-full ${
                      passwordStrength.score >= 3 ? passwordStrength.color : 'bg-transparent'
                    }`}
                  />
                </div>
              </div>
            )}

            {errors.newPassword && (
              <p className="text-xs text-accent-danger mt-1">{errors.newPassword.message}</p>
            )}
          </div>

          {/* Confirm New Password */}
          <div>
            <label className="block text-xs font-semibold text-ink-secondary mb-1">
              Xác nhận mật khẩu mới
            </label>
            <div className="relative">
              <input
                type={showNewPass ? 'text' : 'password'}
                autoComplete="new-password"
                {...register('confirmNewPassword')}
                placeholder="••••••••••••"
                className={`w-full pl-9 pr-3 py-2 text-sm rounded-md bg-surface-base dark:bg-surface-raised border ${
                  errors.confirmNewPassword ? 'border-accent-danger' : 'border-ink-tertiary/30'
                } focus:border-accent-primary focus:outline-none transition-colors font-mono`}
              />
              <LockSimple size={16} className="absolute left-3 top-2.5 text-ink-tertiary" />
            </div>
            {errors.confirmNewPassword && (
              <p className="text-xs text-accent-danger mt-1">
                {errors.confirmNewPassword.message}
              </p>
            )}
          </div>

          <div className="p-3 rounded-md bg-surface-base border border-ink-tertiary/20 text-xs text-ink-tertiary space-y-1">
            <span className="font-semibold text-ink-secondary flex items-center gap-1">
              <ShieldCheck size={14} className="text-accent-primary" />
              Tiêu chuẩn bảo mật (BR-18)
            </span>
            <p className="text-[11px]">
              Đổi mật khẩu thành công sẽ tự động thu hồi các phiên làm việc đang hoạt động khác trên các thiết bị khác.
            </p>
          </div>

          <div className="pt-2">
            <button
              type="submit"
              disabled={isSubmitting}
              className="inline-flex items-center justify-center gap-2 px-5 py-2.5 rounded-md bg-accent-primary text-white font-semibold text-xs hover:bg-accent-primary-dim transition-colors shadow-xs disabled:opacity-60"
            >
              {isSubmitting ? (
                <GrainLoader size="sm" label="Đang cập nhật..." />
              ) : (
                <span>Đổi mật khẩu</span>
              )}
            </button>
          </div>
        </form>
      </div>
    </SettingsLayout>
  );
};
