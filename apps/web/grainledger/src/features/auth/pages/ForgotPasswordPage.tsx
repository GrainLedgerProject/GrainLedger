import { GrainLoader } from '@/components/GrainLoader/GrainLoader';
import { ArrowLeft, CheckCircle, EnvelopeSimple, LockSimple, ShieldCheck } from '@phosphor-icons/react';
import React, { useState } from 'react';
import { Link } from 'react-router-dom';

export const ForgotPasswordPage: React.FC = () => {
  const [email, setEmail] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isSubmitted, setIsSubmitted] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!email) return;

    setIsSubmitting(true);
    // Mimic POST /api/auth/forgot-password (returns 202 without revealing account existence)
    await new Promise((res) => setTimeout(res, 600));
    setIsSubmitting(false);
    setIsSubmitted(true);
  };

  return (
    <div className="min-h-screen bg-parchment-grain flex flex-col justify-center items-center p-4 sm:p-6 selection:bg-accent-primary/20 selection:text-accent-primary">
      <div className="w-full max-w-md bg-surface-base dark:bg-surface-raised border border-ink-tertiary/20 rounded-xl shadow-lg p-6 sm:p-8 space-y-6">
        <div className="flex items-center gap-3 border-b border-ink-tertiary/15 pb-4">
          <div className="w-10 h-10 rounded-lg bg-accent-primary text-white flex items-center justify-center shrink-0">
            <img src="/favicon.svgsvg" alt="GrainLedger" className="w-8 h-8" />
          </div>
          <div>
            <h1 className="text-xl font-serif font-bold text-ink-primary">
              Khôi phục mật khẩu
            </h1>
            <p className="text-xs text-ink-secondary">
              Yêu cầu đặt lại mật khẩu an toàn qua email
            </p>
          </div>
        </div>

        {isSubmitted ? (
          <div className="space-y-4">
            <div className="p-4 rounded-md bg-functional-balanced-bg border border-functional-balanced-border text-accent-primary text-sm space-y-2">
              <div className="flex items-center gap-2 font-bold">
                <CheckCircle size={20} weight="fill" />
                <span>Yêu cầu đã được tiếp nhận</span>
              </div>
              <p className="text-xs text-ink-secondary leading-relaxed">
                Nếu địa chỉ email <strong className="font-mono text-ink-primary">{email}</strong> đã được đăng ký trong hệ thống GrainLedger, mã xác nhận khôi phục sẽ được gửi tới hộp thư trong giây lát.
              </p>
            </div>

            <div className="p-3 rounded-md bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 text-xs text-ink-tertiary space-y-1">
              <span className="font-semibold text-ink-secondary flex items-center gap-1">
                <ShieldCheck size={14} />
                Chính sách bảo vệ tài khoản
              </span>
              <p className="text-[11px]">
                Để bảo vệ quyền riêng tư của khách hàng và nhà máy, hệ thống không tiết lộ email có tồn tại hay không.
              </p>
            </div>

            <Link
              to="/sign-in"
              className="w-full inline-flex items-center justify-center gap-2 px-4 py-2.5 rounded-md bg-surface-raised dark:bg-surface-overlay hover:bg-surface-overlay text-ink-primary font-semibold text-xs border border-ink-tertiary/20 transition-colors"
            >
              <ArrowLeft size={14} />
              Quay lại trang Đăng nhập
            </Link>
          </div>
        ) : (
          <form onSubmit={handleSubmit} className="space-y-4">
            <p className="text-xs text-ink-secondary leading-relaxed">
              Nhập địa chỉ email tài khoản đã được cấp quyền. Chúng tôi sẽ gửi mã OTP để bạn tiến hành tạo mật khẩu mới.
            </p>

            <div>
              <label className="block text-xs font-semibold text-ink-secondary mb-1">
                Địa chỉ email
              </label>
              <div className="relative">
                <input
                  type="email"
                  required
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  placeholder="ten.ban@congty.com"
                  className="w-full pl-9 pr-3 py-2 text-sm rounded-md bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/30 focus:border-accent-primary focus:outline-none transition-colors"
                />
                <EnvelopeSimple size={16} className="absolute left-3 top-2.5 text-ink-tertiary" />
              </div>
            </div>

            <button
              type="submit"
              disabled={isSubmitting}
              className="w-full flex items-center justify-center gap-2 px-4 py-2.5 rounded-md bg-accent-primary text-white font-semibold text-sm hover:bg-accent-primary-dim transition-colors shadow-xs disabled:opacity-60"
            >
              {isSubmitting ? (
                <GrainLoader size="sm" label="Đang gửi mã..." />
              ) : (
                <span>Gửi mã khôi phục</span>
              )}
            </button>

            <div className="pt-2 text-center">
              <Link
                to="/sign-in"
                className="inline-flex items-center gap-1.5 text-xs text-ink-secondary hover:text-ink-primary font-medium"
              >
                <ArrowLeft size={14} />
                Quay lại đăng nhập
              </Link>
            </div>
          </form>
        )}
      </div>
    </div>
  );
};
