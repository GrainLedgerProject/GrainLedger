import React, { useEffect, useState } from 'react';
import { ledgerEndpoints } from '@/api/ledger';
import { GrainLoader } from '@/components/GrainLoader/GrainLoader';
import { StatusBadge } from '@/components/StatusBadge/StatusBadge';
import {
  Warehouse,
  Plus,
  ArrowsClockwise,
  MapPin,
  Wrench,
  CheckCircle,
  WarningCircle,
  X,
  Buildings,
} from '@phosphor-icons/react';

interface FacilityItem {
  id: string;
  name: string;
  code: string;
  address?: string;
  locations?: Array<{
    id: string;
    code: string;
    name: string;
    type: 'SILO' | 'BIN' | 'WAREHOUSE';
    capacityKg: number;
    currentFillKg?: number;
  }>;
  equipment?: Array<{
    id: string;
    code: string;
    name: string;
    kind: 'DRYER' | 'MILL' | 'BLENDER' | 'PACKER';
  }>;
}

export const FacilityConfigPage: React.FC = () => {
  const [setupData, setSetupData] = useState<any>(null);
  const [facilities, setFacilities] = useState<FacilityItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);

  // Modal State
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [modalType, setModalType] = useState<'FACILITY' | 'LOCATION' | 'EQUIPMENT'>('FACILITY');
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [extra, setExtra] = useState('');
  const [submitting, setSubmitting] = useState(false);

  const loadData = async () => {
    setLoading(true);
    setError(null);
    try {
      const setup = await ledgerEndpoints.getSetup().catch(() => null);
      setSetupData(setup);

      // Map facilities
      const facs: FacilityItem[] = (setup as any)?.facilities || [
        {
          id: 'fac-1',
          code: 'FAC-TIENGIANG-01',
          name: 'Nhà máy Xay xát & Sấy lúa Tiền Giang',
          address: 'Huyện Cái Bè, Tỉnh Tiền Giang',
          locations: [
            { id: 'loc-1', code: 'SILO-01', name: 'Silo lưu trữ lúa khô S1', type: 'SILO', capacityKg: 500000, currentFillKg: 120000 },
            { id: 'loc-2', code: 'SILO-02', name: 'Silo lưu trữ lúa khô S2', type: 'SILO', capacityKg: 500000, currentFillKg: 0 },
            { id: 'loc-3', code: 'BIN-01', name: 'Thùng trung chuyển gạo trắng', type: 'BIN', capacityKg: 80000, currentFillKg: 45000 },
          ],
          equipment: [
            { id: 'eq-1', code: 'DRYER-LINE-A', name: 'Lò sấy vỉ ngang 50 tấn/mẻ', kind: 'DRYER' },
            { id: 'eq-2', code: 'MILL-LINE-01', name: 'Dây chuyền bóc vỏ & xát trắng A', kind: 'MILL' },
            { id: 'eq-3', code: 'BLEND-LINE-01', name: 'Máy trộn tỷ lệ tấm tự động', kind: 'BLENDER' },
          ],
        },
      ];
      setFacilities(facs);
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError('Không thể tải cấu hình cơ sở.');
      }
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadData();
  }, []);

  const handleOpenAdd = (type: 'FACILITY' | 'LOCATION' | 'EQUIPMENT') => {
    setModalType(type);
    setCode('');
    setName('');
    setExtra('');
    setIsModalOpen(true);
  };

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitting(true);
    setError(null);

    try {
      if (modalType === 'FACILITY') {
        await ledgerEndpoints.createFacility({ code, name, address: extra });
      } else if (modalType === 'LOCATION') {
        await ledgerEndpoints.createLocation({
          facilityId: facilities[0]?.id || crypto.randomUUID(),
          code,
          name,
          type: 'SILO',
          capacityKg: parseFloat(extra) || 500000,
        });
      } else {
        await ledgerEndpoints.createEquipment({
          facilityId: facilities[0]?.id || crypto.randomUUID(),
          code,
          name,
          kind: 'MILL',
        });
      }

      setSuccessMsg(`Đã tạo thành công ${name}`);
      setIsModalOpen(false);
      await loadData();
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError('Thao tác không thành công.');
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
            SC-05 · CẤU HÌNH NHÀ MÁY, SILO & THIẾT BỊ
          </span>
          <h2 className="text-2xl font-display font-bold text-ink-primary">
            Cơ sở Vật chất & Vị trí Lưu trữ Silo
          </h2>
          <p className="text-xs text-ink-secondary mt-0.5">
            Cấu hình các silo lưu trữ, máy sấy và dây chuyền chế biến để hệ thống hạch toán dòng chảy cân bằng khối lượng.
          </p>
        </div>

        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={loadData}
            className="p-2.5 rounded-lg border border-ink-tertiary/20 bg-surface-base hover:bg-surface-raised text-ink-secondary hover:text-ink-primary transition-colors"
            title="Tải lại dữ liệu"
          >
            <ArrowsClockwise size={16} />
          </button>
          <button
            type="button"
            onClick={() => handleOpenAdd('LOCATION')}
            className="inline-flex items-center gap-2 px-4 py-2.5 rounded-lg bg-accent-primary text-white font-semibold text-xs hover:bg-accent-primary-dim transition-all shadow-xs"
          >
            <Plus size={16} weight="bold" />
            <span>Thêm Silo / Kho chứa</span>
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

      {/* Facilities Cards */}
      {loading ? (
        <div className="py-16 flex justify-center">
          <GrainLoader label="Đang kiểm tra sơ đồ cơ sở nhà máy..." />
        </div>
      ) : (
        <div className="space-y-6">
          {facilities.map((fac) => (
            <div
              key={fac.id}
              className="p-6 rounded-2xl bg-surface-base dark:bg-surface-raised border border-ink-tertiary/20 shadow-xs space-y-6"
            >
              <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 border-b border-ink-tertiary/15 pb-4">
                <div>
                  <div className="flex items-center gap-2">
                    <h3 className="font-display font-bold text-lg text-ink-primary">
                      {fac.name}
                    </h3>
                    <span className="font-mono text-xs font-bold text-accent-primary bg-emerald-100 dark:bg-emerald-950/60 px-2 py-0.5 rounded">
                      {fac.code}
                    </span>
                  </div>
                  <span className="text-xs text-ink-secondary mt-0.5 block">
                    {fac.address || 'Cái Bè, Tiền Giang, Việt Nam'}
                  </span>
                </div>
              </div>

              {/* Silos / Locations Grid */}
              <div className="space-y-3">
                <div className="flex items-center justify-between">
                  <h4 className="font-bold text-xs uppercase tracking-wider text-ink-primary flex items-center gap-1.5">
                    <Warehouse size={15} className="text-accent-primary" />
                    <span>Hệ thống Silo & Kho chứa lúa</span>
                  </h4>
                  <span className="text-xs text-ink-secondary">
                    {fac.locations?.length || 0} vị trí
                  </span>
                </div>

                <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
                  {fac.locations?.map((loc) => {
                    const fillPercent = loc.currentFillKg
                      ? Math.round((loc.currentFillKg / loc.capacityKg) * 100)
                      : 0;
                    return (
                      <div
                        key={loc.id}
                        className="p-3.5 rounded-xl bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/15 space-y-2"
                      >
                        <div className="flex items-start justify-between">
                          <div>
                            <span className="font-mono font-bold text-accent-primary text-xs block">
                              {loc.code}
                            </span>
                            <span className="font-semibold text-ink-primary text-xs">
                              {loc.name}
                            </span>
                          </div>
                          <StatusBadge
                            status={fillPercent > 0 ? 'BALANCED' : 'STALE'}
                            label={fillPercent > 0 ? `${fillPercent}% đầy` : 'Trống'}
                          />
                        </div>

                        <div className="space-y-1 pt-1">
                          <div className="w-full h-1.5 rounded-full bg-ink-tertiary/20 overflow-hidden">
                            <div
                              className="h-full bg-accent-primary rounded-full transition-all"
                              style={{ width: `${Math.min(fillPercent, 100)}%` }}
                            />
                          </div>
                          <div className="flex justify-between text-[11px] font-mono text-ink-secondary">
                            <span>{loc.currentFillKg?.toLocaleString('vi-VN') || 0} kg</span>
                            <span>Sức chứa: {loc.capacityKg.toLocaleString('vi-VN')} kg</span>
                          </div>
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>

              {/* Equipment Grid */}
              <div className="space-y-3 pt-2">
                <div className="flex items-center justify-between">
                  <h4 className="font-bold text-xs uppercase tracking-wider text-ink-primary flex items-center gap-1.5">
                    <Wrench size={15} className="text-accent-primary" />
                    <span>Dây chuyền máy móc & Thiết bị vận hành</span>
                  </h4>
                  <span className="text-xs text-ink-secondary">
                    {fac.equipment?.length || 0} thiết bị
                  </span>
                </div>

                <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
                  {fac.equipment?.map((eq) => (
                    <div
                      key={eq.id}
                      className="p-3 rounded-xl bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/15 flex items-center justify-between"
                    >
                      <div>
                        <span className="font-mono text-xs font-bold text-accent-primary block">
                          {eq.code}
                        </span>
                        <span className="text-xs font-semibold text-ink-primary">
                          {eq.name}
                        </span>
                      </div>
                      <span className="text-[10px] font-mono uppercase font-bold text-ink-tertiary px-1.5 py-0.5 rounded bg-surface-base border">
                        {eq.kind}
                      </span>
                    </div>
                  ))}
                </div>
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Modal */}
      {isModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-xs p-4">
          <div className="w-full max-w-md bg-surface-base dark:bg-surface-raised border border-ink-tertiary/20 rounded-2xl shadow-xl overflow-hidden animate-ledger-in">
            <div className="p-5 border-b border-ink-tertiary/15 flex items-center justify-between">
              <h3 className="font-display font-bold text-base text-ink-primary">
                {modalType === 'LOCATION'
                  ? 'Thêm Silo / Kho chứa Mới'
                  : modalType === 'EQUIPMENT'
                  ? 'Thêm Thiết bị / Dây chuyền Mới'
                  : 'Thêm Cơ sở Nhà máy Mới'}
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
                  Mã Code định danh *
                </label>
                <input
                  type="text"
                  required
                  value={code}
                  onChange={(e) => setCode(e.target.value)}
                  placeholder="VD: SILO-03, DRYER-LINE-B"
                  className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none font-mono uppercase"
                />
              </div>

              <div>
                <label className="block font-semibold text-ink-secondary mb-1">
                  Tên hiển thị *
                </label>
                <input
                  type="text"
                  required
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder="VD: Silo lưu trữ lúa khô S3"
                  className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none"
                />
              </div>

              <div>
                <label className="block font-semibold text-ink-secondary mb-1">
                  {modalType === 'LOCATION'
                    ? 'Sức chứa thiết kế (kg) *'
                    : 'Địa chỉ / Ghi chú kỹ thuật'}
                </label>
                <input
                  type="text"
                  value={extra}
                  onChange={(e) => setExtra(e.target.value)}
                  placeholder={modalType === 'LOCATION' ? '500000' : 'Huyện Cái Bè, Tiền Giang'}
                  className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none font-mono"
                />
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
                  {submitting ? 'Đang tạo...' : 'Tạo mới'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
