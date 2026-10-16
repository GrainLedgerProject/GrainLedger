import React, { useEffect, useState } from 'react';
import { adminEndpoints, type OrganizationDto } from '@/api/admin';
import { GrainLoader } from '@/components/GrainLoader/GrainLoader';
import { StatusBadge } from '@/components/StatusBadge/StatusBadge';
import {
  Buildings,
  Plus,
  ArrowsClockwise,
  CheckCircle,
  WarningCircle,
  X,
  Factory,
  Briefcase,
  Users,
} from '@phosphor-icons/react';

export const CustomerOrganizationsPage: React.FC = () => {
  const [organizations, setOrganizations] = useState<OrganizationDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);

  // Modal State
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [name, setName] = useState('');
  const [kind, setKind] = useState<'Mill' | 'Customer'>('Customer');
  const [submitting, setSubmitting] = useState(false);

  const loadOrgs = async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await adminEndpoints.getOrganizations();
      setOrganizations(Array.isArray(res) ? res : []);
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError('Không thể tải danh sách tổ chức/doanh nghiệp.');
      }
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadOrgs();
  }, []);

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!name.trim()) return;

    setSubmitting(true);
    setError(null);
    try {
      await adminEndpoints.createOrganization({
        name: name.trim(),
        kind,
      });
      setSuccessMsg(`Đã thiết lập thành công doanh nghiệp "${name.trim()}"`);
      setName('');
      setIsModalOpen(false);
      await loadOrgs();
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError('Không thể tạo doanh nghiệp.');
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 border-b border-ink-tertiary/15 pb-4">
        <div>
          <span className="text-xs font-mono font-bold text-accent-primary uppercase tracking-wider block">
            SC-03 · THIẾT LẬP RANH GIỚI KHÁCH HÀNG (TENANT ISOLATION)
          </span>
          <h2 className="text-2xl font-display font-bold text-ink-primary">
            Quản trị Doanh nghiệp & Khách hàng Xuất khẩu
          </h2>
          <p className="text-xs text-ink-secondary mt-0.5">
            Quản lý các công ty gửi lúa vào nhà máy sấy, lưu trữ silo và chế biến xuất khẩu. Dữ liệu lô hàng được cô lập 100% giữa các công ty.
          </p>
        </div>

        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={loadOrgs}
            className="p-2.5 rounded-lg border border-ink-tertiary/20 bg-surface-base hover:bg-surface-raised text-ink-secondary hover:text-ink-primary transition-colors"
            title="Tải lại danh sách"
          >
            <ArrowsClockwise size={16} />
          </button>
          <button
            type="button"
            onClick={() => setIsModalOpen(true)}
            className="inline-flex items-center gap-2 px-4 py-2.5 rounded-lg bg-accent-primary text-white font-semibold text-xs hover:bg-accent-primary-dim transition-all shadow-xs"
          >
            <Plus size={16} weight="bold" />
            <span>Thêm Doanh nghiệp mới</span>
          </button>
        </div>
      </div>

      {/* Alerts */}
      {error && (
        <div className="p-3.5 rounded-lg bg-functional-rejected-bg border border-functional-rejected-border text-accent-danger text-xs flex items-center gap-2">
          <WarningCircle size={18} weight="fill" className="shrink-0" />
          <span>{error}</span>
        </div>
      )}
      {successMsg && (
        <div className="p-3.5 rounded-lg bg-emerald-50 dark:bg-emerald-950/40 border border-emerald-500/30 text-[#2F6B3A] dark:text-emerald-300 text-xs flex items-center gap-2">
          <CheckCircle size={18} weight="fill" className="shrink-0" />
          <span>{successMsg}</span>
        </div>
      )}

      {/* Grid of Organizations */}
      {loading ? (
        <div className="py-16 flex justify-center">
          <GrainLoader label="Đang kiểm tra ranh giới dữ liệu doanh nghiệp..." />
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-5">
          {organizations.map((org) => (
            <div
              key={org.id}
              className="p-5 rounded-2xl bg-surface-base dark:bg-surface-raised border border-ink-tertiary/20 shadow-xs hover:shadow-md transition-shadow space-y-4"
            >
              <div className="flex items-start justify-between gap-3">
                <div className="w-10 h-10 rounded-xl bg-accent-primary/10 text-accent-primary flex items-center justify-center shrink-0">
                  {org.kind === 'Mill' ? <Factory size={22} weight="bold" /> : <Briefcase size={22} weight="bold" />}
                </div>
                <StatusBadge
                  status={org.kind === 'Mill' ? 'BALANCED' : 'STALE'}
                  label={org.kind === 'Mill' ? 'Đơn vị Vận hành Nhà máy' : 'Doanh nghiệp Xuất khẩu'}
                />
              </div>

              <div>
                <h3 className="font-display font-bold text-base text-ink-primary">
                  {org.name}
                </h3>
                <span className="font-mono text-[11px] text-ink-tertiary block mt-0.5">
                  ID: {org.id.slice(0, 13)}...
                </span>
              </div>

              <div className="pt-3 border-t border-ink-tertiary/15 flex items-center justify-between text-xs text-ink-secondary">
                <span className="inline-flex items-center gap-1">
                  <Users size={14} className="text-ink-tertiary" />
                  <span>Cô lập Tenant riêng biệt</span>
                </span>
                <span className="font-bold text-accent-primary">ISO 22095</span>
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Create Modal */}
      {isModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-xs p-4">
          <div className="w-full max-w-md bg-surface-base dark:bg-surface-raised border border-ink-tertiary/20 rounded-2xl shadow-xl overflow-hidden animate-ledger-in">
            <div className="p-5 border-b border-ink-tertiary/15 flex items-center justify-between">
              <h3 className="font-display font-bold text-base text-ink-primary">
                Thiết lập Doanh nghiệp Khách hàng Mới
              </h3>
              <button
                type="button"
                onClick={() => setIsModalOpen(false)}
                className="p-1 rounded-md text-ink-tertiary hover:text-ink-primary"
              >
                <X size={18} />
              </button>
            </div>

            <form onSubmit={handleCreate} className="p-5 space-y-4 text-xs">
              <div>
                <label className="block font-semibold text-ink-secondary mb-1">
                  Tên Công ty / Đơn vị *
                </label>
                <input
                  type="text"
                  required
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder="VD: Công ty Cổ phần Xuất nhập khẩu Lúa Gạo Sông Tiền"
                  className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none"
                />
              </div>

              <div>
                <label className="block font-semibold text-ink-secondary mb-1">
                  Phân loại tổ chức *
                </label>
                <select
                  value={kind}
                  onChange={(e) => setKind(e.target.value as 'Mill' | 'Customer')}
                  className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none"
                >
                  <option value="Customer">Khách hàng Xuất khẩu (Customer Exporter)</option>
                  <option value="Mill">Ban Điều hành Nhà máy (Mill Operator)</option>
                </select>
              </div>

              <div className="pt-3 border-t border-ink-tertiary/15 flex items-center justify-end gap-2">
                <button
                  type="button"
                  onClick={() => setIsModalOpen(false)}
                  className="px-4 py-2 rounded-lg border border-ink-tertiary/20 hover:bg-surface-raised font-semibold text-ink-secondary"
                >
                  Huỷ
                </button>
                <button
                  type="submit"
                  disabled={submitting}
                  className="px-5 py-2 rounded-lg bg-accent-primary text-white font-semibold hover:bg-accent-primary-dim transition-colors disabled:opacity-60"
                >
                  {submitting ? 'Đang tạo...' : 'Tạo doanh nghiệp'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
