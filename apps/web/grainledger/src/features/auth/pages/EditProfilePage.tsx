/**
 * @screen SC-01e (Edit Profile)
 * @feature Auth
 * @roles ALL Authenticated Users
 * @route /settings/profile
 * @description Updates personal display profile and language preferences.
 */
import { useAuthStore } from '@/app/store/authStore';
import { useUiStore } from '@/app/store/uiStore';
import { GrainLoader } from '@/components/GrainLoader/GrainLoader';
import { RoleChip } from '@/components/RoleChip/RoleChip';
import { SettingsLayout } from '@/components/SettingsLayout/SettingsLayout';
import { authApi } from '@/features/auth/api/authApi';
import {
  editProfileSchema,
  type EditProfileFormValues,
} from '@/features/auth/model/authSchemas';
import { isCustomerSide, isMillSide } from '@/shared/types/roles';
import { zodResolver } from '@hookform/resolvers/zod';
import {
  Buildings,
  CheckCircle,
  Factory,
  Globe,
  LockSimple,
  Phone,
  User,
  WarningCircle,
} from '@phosphor-icons/react';
import React, { useState } from 'react';
import { useForm } from 'react-hook-form';

export const EditProfilePage: React.FC = () => {
  const { user, updateProfile } = useAuthStore();
  const { language, setLanguage } = useUiStore();

  const [isSubmitting, setIsSubmitting] = useState(false);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<EditProfileFormValues>({
    resolver: zodResolver(editProfileSchema),
    defaultValues: {
      displayName: user?.displayName || '',
      language: language,
      phoneNumber: user?.phoneNumber || '',
    },
  });

  const getInitials = (name?: string) => {
    if (!name) return 'GL';
    const parts = name.trim().split(/\s+/);
    if (parts.length === 1) return parts[0].substring(0, 2).toUpperCase();
    return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
  };

  const onSubmit = async (values: EditProfileFormValues) => {
    setSuccessMsg(null);
    setErrorMsg(null);
    setIsSubmitting(true);

    try {
      await authApi.updateProfile({
        displayName: values.displayName,
        phoneNumber: values.phoneNumber,
      });

      // Optimistic state update in Zustand
      updateProfile({
        displayName: values.displayName,
        phoneNumber: values.phoneNumber,
      });

      // Apply language change
      setLanguage(values.language);

      setSuccessMsg('Thông tin cá nhân đã được lưu thành công.');
    } catch {
      setErrorMsg('Không thể lưu thông tin cá nhân. Vui lòng thử lại.');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <SettingsLayout>
      <div className="space-y-6">
        <div className="border-b border-ink-tertiary/15 pb-4">
          <h2 className="text-lg font-bold text-ink-primary font-serif">
            Hồ sơ cá nhân
          </h2>
          <p className="text-xs text-ink-secondary mt-0.5">
            Quản lý tên hiển thị, số điện thoại liên hệ và ngôn ngữ sử dụng trên hệ thống.
          </p>
        </div>

        {/* Success / Error Banners */}
        {successMsg && (
          <div className="p-3.5 rounded-md bg-functional-balanced-bg border border-functional-balanced-border text-accent-primary text-xs flex items-center gap-2">
            <CheckCircle size={18} weight="fill" />
            <span className="font-semibold">{successMsg}</span>
          </div>
        )}
        {errorMsg && (
          <div className="p-3.5 rounded-md bg-functional-rejected-bg border border-functional-rejected-border text-accent-danger text-xs flex items-center gap-2">
            <WarningCircle size={18} weight="fill" />
            <span className="font-semibold">{errorMsg}</span>
          </div>
        )}

        <form onSubmit={handleSubmit(onSubmit)} className="space-y-6 max-w-xl">
          {/* Avatar Section (Initials-based generated avatar) */}
          <div className="flex items-center gap-4 p-4 rounded-lg bg-surface-base border border-ink-tertiary/15">
            <div className="w-16 h-16 rounded-full bg-accent-primary text-white flex items-center justify-center font-bold text-xl shadow-sm shrink-0">
              {getInitials(user?.displayName)}
            </div>
            <div className="space-y-1">
              <h3 className="font-semibold text-sm text-ink-primary">
                Ảnh đại diện định danh
              </h3>
              <p className="text-xs text-ink-secondary">
                Ảnh đại diện tự động tạo theo chữ cái tên chính thức để bảo đảm đồng bộ hồ sơ kiểm toán.
              </p>
            </div>
          </div>

          {/* Editable Fields */}
          <div className="space-y-4">
            <div>
              <label className="block text-xs font-semibold text-ink-secondary mb-1">
                Họ và tên hiển thị *
              </label>
              <div className="relative">
                <input
                  type="text"
                  {...register('displayName')}
                  className={`w-full pl-9 pr-3 py-2 text-sm rounded-md bg-surface-base dark:bg-surface-raised border ${
                    errors.displayName ? 'border-accent-danger' : 'border-ink-tertiary/30'
                  } focus:border-accent-primary focus:outline-none transition-colors`}
                />
                <User size={16} className="absolute left-3 top-2.5 text-ink-tertiary" />
              </div>
              {errors.displayName && (
                <p className="text-xs text-accent-danger mt-1">{errors.displayName.message}</p>
              )}
            </div>

            <div>
              <label className="block text-xs font-semibold text-ink-secondary mb-1">
                Ngôn ngữ giao diện (Display Language)
              </label>
              <div className="relative">
                <select
                  {...register('language')}
                  className="w-full pl-9 pr-3 py-2 text-sm rounded-md bg-surface-base dark:bg-surface-raised border border-ink-tertiary/30 focus:border-accent-primary focus:outline-none transition-colors"
                >
                  <option value="vi">Tiếng Việt (Mặc định)</option>
                  <option value="en">English (Export Compliance)</option>
                </select>
                <Globe size={16} className="absolute left-3 top-2.5 text-ink-tertiary" />
              </div>
            </div>

            <div>
              <label className="block text-xs font-semibold text-ink-secondary mb-1">
                Số điện thoại liên lạc
              </label>
              <div className="relative">
                <input
                  type="tel"
                  {...register('phoneNumber')}
                  placeholder="09xx xxx xxx"
                  className={`w-full pl-9 pr-3 py-2 text-sm rounded-md bg-surface-base dark:bg-surface-raised border ${
                    errors.phoneNumber ? 'border-accent-danger' : 'border-ink-tertiary/30'
                  } focus:border-accent-primary focus:outline-none transition-colors font-mono`}
                />
                <Phone size={16} className="absolute left-3 top-2.5 text-ink-tertiary" />
              </div>
              {errors.phoneNumber && (
                <p className="text-xs text-accent-danger mt-1">{errors.phoneNumber.message}</p>
              )}
            </div>
          </div>

          {/* Read-Only Admin Managed Scope */}
          <div className="p-4 rounded-lg bg-surface-base border border-ink-tertiary/20 space-y-3">
            <div className="flex items-center gap-1.5 text-xs font-bold text-ink-tertiary uppercase tracking-wider">
              <LockSimple size={14} />
              <span>Phân quyền và phạm vi quản lý bởi Quản trị nhà máy</span>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 text-xs">
              <div>
                <span className="text-ink-tertiary block">Email tài khoản:</span>
                <span className="font-mono font-semibold text-ink-primary">
                  {user?.email || 'N/A'}
                </span>
              </div>

              <div>
                <span className="text-ink-tertiary block">Vai trò nghiệp vụ:</span>
                <div className="mt-1">
                  {user && <RoleChip role={user.role} size="sm" showCode />}
                </div>
              </div>

              {user && isCustomerSide(user.role) && (
                <div className="sm:col-span-2">
                  <span className="text-ink-tertiary block">Công ty khách hàng sở hữu:</span>
                  <div className="flex items-center gap-1.5 font-medium text-ink-primary mt-0.5">
                    <Buildings size={14} className="text-accent-info" />
                    <span>{user.organizationName}</span>
                  </div>
                </div>
              )}

              {user && isMillSide(user.role) && (
                <div className="sm:col-span-2">
                  <span className="text-ink-tertiary block">Cơ sở vận hành được cấp phép:</span>
                  <div className="flex items-center gap-1.5 font-medium text-ink-primary mt-0.5">
                    <Factory size={14} className="text-accent-primary" />
                    <span>
                      {user.assignedFacilities?.join(', ') || 'Cụm Silo & Dây chuyền A - Tiền Giang'}
                    </span>
                  </div>
                </div>
              )}
            </div>
          </div>

          <div>
            <button
              type="submit"
              disabled={isSubmitting}
              className="inline-flex items-center justify-center gap-2 px-5 py-2.5 rounded-md bg-accent-primary text-white font-semibold text-xs hover:bg-accent-primary-dim transition-colors shadow-xs disabled:opacity-60"
            >
              {isSubmitting ? (
                <GrainLoader size="sm" label="Đang lưu..." />
              ) : (
                <span>Lưu thay đổi</span>
              )}
            </button>
          </div>
        </form>
      </div>
    </SettingsLayout>
  );
};
