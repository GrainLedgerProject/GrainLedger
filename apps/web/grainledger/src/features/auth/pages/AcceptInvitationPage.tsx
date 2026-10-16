import { GrainLoader } from '@/components/GrainLoader/GrainLoader';
import { authApi } from '@/features/auth/api/authApi';
import {
  acceptInvitationSchema,
  type AcceptInvitationFormValues,
} from '@/features/auth/model/authSchemas';
import { zodResolver } from '@hookform/resolvers/zod';
import {
  ArrowLeft,
  CheckCircle,
  Eye,
  EyeSlash,
  LockSimple,
  ShieldCheck,
  WarningCircle,
} from '@phosphor-icons/react';
import React, { useEffect, useMemo, useState } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams } from 'react-router-dom';

export const AcceptInvitationPage: React.FC = () => {
  const { t } = useTranslation();
  const { token } = useParams<{ token: string }>();
  const navigate = useNavigate();

  const [isValidating, setIsValidating] = useState(true);
  const [tokenError, setTokenError] = useState<string | null>(null);
  const [invitedUser, setInvitedUser] = useState<{ email: string; name: string } | null>(null);
  const [showPassword, setShowPassword] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isSuccess, setIsSuccess] = useState(false);

  const {
    register,
    handleSubmit,
    watch,
    formState: { errors },
  } = useForm<AcceptInvitationFormValues>({
    resolver: zodResolver(acceptInvitationSchema),
  });

  const passwordValue = watch('password', '');

  // Calculate Password Strength
  const passwordStrength = useMemo(() => {
    if (!passwordValue)
      return { score: 0, label: t('auth.invitation.strengthNone', 'Chưa nhập'), color: 'bg-ink-tertiary/20' };
    let score = 0;
    if (passwordValue.length >= 12) score += 1;
    if (/[A-Z]/.test(passwordValue)) score += 1;
    if (/[a-z]/.test(passwordValue)) score += 1;
    if (/[0-9]/.test(passwordValue)) score += 1;
    if (/[^A-Za-z0-9]/.test(passwordValue)) score += 1;

    if (score <= 2) {
      return { score: 1, label: t('auth.invitation.strengthWeak', 'Yếu (Weak)'), color: 'bg-accent-danger' };
    } else if (score <= 4) {
      return { score: 2, label: t('auth.invitation.strengthFair', 'Khá (Fair)'), color: 'bg-accent-warning' };
    } else {
      return { score: 3, label: t('auth.invitation.strengthStrong', 'Mạnh (Strong)'), color: 'bg-accent-primary' };
    }
  }, [passwordValue, t]);

  useEffect(() => {
    let isMounted = true;
    async function validate() {
      try {
        const result = await authApi.validateInvitationToken(token || 'default');
        if (isMounted) {
          setInvitedUser(result);
          setIsValidating(false);
        }
      } catch (err: unknown) {
        if (isMounted) {
          setTokenError(
            err instanceof Error
              ? err.message
              : t('auth.invitation.invalidDefault', 'Lời mời không hợp lệ hoặc đã hết hạn.')
          );
          setIsValidating(false);
        }
      }
    }
    validate();
    return () => {
      isMounted = false;
    };
  }, [token, t]);

  const onSubmit = async (values: AcceptInvitationFormValues) => {
    setIsSubmitting(true);
    try {
      await authApi.acceptInvitation(token || '', values.password);
      setIsSuccess(true);
      setTimeout(() => {
        navigate('/sign-in');
      }, 2000);
    } catch {
      // Error handling
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="min-h-screen bg-parchment-grain flex flex-col justify-center items-center p-4 sm:p-6 selection:bg-accent-primary/20 selection:text-accent-primary">
      <div className="w-full max-w-lg bg-surface-base dark:bg-surface-raised border border-ink-tertiary/20 rounded-xl shadow-lg p-6 sm:p-8 space-y-6">
        {/* Header */}
        <div className="flex items-center gap-3 border-b border-ink-tertiary/15 pb-4">
          <div className="w-10 h-10 rounded-lg bg-accent-primary text-white flex items-center justify-center shrink-0">
            <img src="/favicon.svg" alt="GrainLedger" className="w-8 h-8" />
          </div>
          <div>
            <h1 className="text-xl font-serif font-bold text-ink-primary">
              {t('auth.invitation.title', 'Kích hoạt tài khoản GrainLedger')}
            </h1>
            <p className="text-xs text-ink-secondary">
              {t('auth.invitation.subtitle', 'Thiết lập mật khẩu an toàn theo thư mời từ Quản trị viên')}
            </p>
          </div>
        </div>

        {/* Loading Token state */}
        {isValidating && (
          <div className="py-12 flex flex-col items-center justify-center space-y-3">
            <GrainLoader size="md" label={t('auth.invitation.validating', 'Đang kiểm tra tính hợp lệ của thư mời...')} />
          </div>
        )}

        {/* Invalid Token state */}
        {!isValidating && tokenError && (
          <div className="space-y-4">
            <div className="p-4 rounded-md bg-functional-rejected-bg border border-functional-rejected-border text-accent-danger text-sm flex items-start gap-3">
              <WarningCircle size={22} weight="fill" className="shrink-0 mt-0.5" />
              <div>
                <h3 className="font-semibold text-base mb-1">
                  {t('auth.invitation.invalidTitle', 'Thư mời không còn hiệu lực')}
                </h3>
                <p className="text-xs">{tokenError}</p>
                <p className="text-xs mt-2 font-medium">
                  {t(
                    'auth.invitation.contactAdmin',
                    'Vui lòng liên hệ Quản trị nhà máy (Mill Administrator) để cấp lại lời mời kích hoạt mới.'
                  )}
                </p>
              </div>
            </div>

            <Link
              to="/sign-in"
              className="inline-flex items-center gap-2 text-xs font-semibold text-accent-primary hover:underline"
            >
              <ArrowLeft size={14} />
              {t('auth.backToSignIn', 'Quay lại màn hình đăng nhập')}
            </Link>
          </div>
        )}

        {/* Success Feedback */}
        {isSuccess && (
          <div className="p-4 rounded-md bg-functional-balanced-bg border border-functional-balanced-border text-accent-primary text-sm flex items-center gap-3">
            <CheckCircle size={24} weight="fill" />
            <div>
              <h3 className="font-semibold text-base">{t('auth.invitation.successTitle', 'Thiết lập mật khẩu thành công!')}</h3>
              <p className="text-xs opacity-90">{t('auth.invitation.redirecting', 'Đang chuyển hướng về trang đăng nhập...')}</p>
            </div>
          </div>
        )}

        {/* Valid Token Form */}
        {!isValidating && !tokenError && !isSuccess && invitedUser && (
          <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
            {/* Pre-filled readonly info */}
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 p-3 rounded-md bg-surface-raised dark:bg-surface-overlay text-xs">
              <div>
                <span className="text-ink-tertiary block font-medium">
                  {t('auth.invitation.nameLabel', 'Họ và tên')}:
                </span>
                <span className="font-semibold text-ink-primary">{invitedUser.name}</span>
              </div>
              <div>
                <span className="text-ink-tertiary block font-medium">
                  {t('auth.invitation.emailLabel', 'Email định danh')}:
                </span>
                <span className="font-mono text-ink-primary">{invitedUser.email}</span>
              </div>
            </div>

            {/* New Password */}
            <div>
              <label className="block text-xs font-semibold text-ink-secondary mb-1">
                {t('auth.invitation.newPasswordLabel', 'Mật khẩu mới')}
              </label>
              <div className="relative">
                <input
                  type={showPassword ? 'text' : 'password'}
                  {...register('password')}
                  placeholder="••••••••••••"
                  className={`w-full pl-9 pr-10 py-2 text-sm rounded-md bg-surface-raised dark:bg-surface-overlay border ${
                    errors.password ? 'border-accent-danger' : 'border-ink-tertiary/30'
                  } focus:border-accent-primary focus:outline-none transition-colors font-mono`}
                />
                <LockSimple size={16} className="absolute left-3 top-2.5 text-ink-tertiary" />
                <button
                  type="button"
                  onClick={() => setShowPassword(!showPassword)}
                  className="absolute right-3 top-2.5 text-ink-tertiary hover:text-ink-primary"
                  aria-label="Toggle password visibility"
                >
                  {showPassword ? <EyeSlash size={16} /> : <Eye size={16} />}
                </button>
              </div>

              {/* Password Strength Meter */}
              {passwordValue && (
                <div className="mt-2 space-y-1">
                  <div className="flex items-center justify-between text-[11px]">
                    <span className="text-ink-tertiary">{t('settings.complexity', 'Độ mạnh mật khẩu')}:</span>
                    <span className="font-semibold text-ink-primary">{passwordStrength.label}</span>
                  </div>
                  <div className="h-1.5 w-full bg-surface-raised dark:bg-surface-overlay rounded-full overflow-hidden flex gap-1">
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

              {errors.password && (
                <p className="text-xs text-accent-danger mt-1">{errors.password.message}</p>
              )}
            </div>

            {/* Confirm Password */}
            <div>
              <label className="block text-xs font-semibold text-ink-secondary mb-1">
                {t('auth.invitation.confirmPasswordLabel', 'Xác nhận mật khẩu mới')}
              </label>
              <div className="relative">
                <input
                  type={showPassword ? 'text' : 'password'}
                  {...register('confirmPassword')}
                  placeholder="••••••••••••"
                  className={`w-full pl-9 pr-3 py-2 text-sm rounded-md bg-surface-raised dark:bg-surface-overlay border ${
                    errors.confirmPassword ? 'border-accent-danger' : 'border-ink-tertiary/30'
                  } focus:border-accent-primary focus:outline-none transition-colors font-mono`}
                />
                <LockSimple size={16} className="absolute left-3 top-2.5 text-ink-tertiary" />
              </div>
              {errors.confirmPassword && (
                <p className="text-xs text-accent-danger mt-1">
                  {errors.confirmPassword.message}
                </p>
              )}
            </div>

            <div className="p-3 rounded-md bg-emerald-50 dark:bg-emerald-950/30 border border-accent-primary/20 text-xs text-ink-secondary space-y-1">
              <span className="font-semibold text-accent-primary flex items-center gap-1">
                <ShieldCheck size={14} />
                {t('auth.invitation.standardTitle', 'Tiêu chuẩn mật khẩu an toàn')}
              </span>
              <p className="text-[11px]">
                {t(
                  'auth.invitation.passwordHint',
                  'Tối thiểu 12 ký tự, bao gồm ít nhất một chữ hoa, chữ thường, chữ số và ký tự đặc biệt.'
                )}
              </p>
            </div>

            <button
              type="submit"
              disabled={isSubmitting}
              className="w-full flex items-center justify-center gap-2 px-4 py-2.5 rounded-md bg-accent-primary text-white font-semibold text-sm hover:bg-accent-primary-dim transition-colors shadow-xs disabled:opacity-60"
            >
              {isSubmitting ? (
                <GrainLoader size="sm" label={t('auth.invitation.saving', 'Đang lưu mật khẩu...')} />
              ) : (
                <span>{t('auth.invitation.submitButton', 'Kích hoạt tài khoản & Đăng nhập')}</span>
              )}
            </button>
          </form>
        )}
      </div>
    </div>
  );
};

