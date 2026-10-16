import React, { useEffect, useState } from 'react';
import { procurementEndpoints, type ProcurementInput } from '@/api/procurement';
import { GrainLoader } from '@/components/GrainLoader/GrainLoader';
import { StatusBadge } from '@/components/StatusBadge/StatusBadge';
import {
  Receipt,
  Plus,
  ArrowsClockwise,
  MagnifyingGlass,
  CheckCircle,
  WarningCircle,
  X,
  Scales,
  Drop,
  User,
  Calendar,
  Warehouse,
} from '@phosphor-icons/react';

interface ProcurementReceiptItem {
  id: string;
  code?: string;
  farmerId: string;
  farmerName?: string;
  commodityName?: string;
  receivedAt: string;
  totalQuantityKg?: number;
  items?: Array<{
    lineNo: number;
    measuredValue: string;
    measuredUnit: string;
    moisturePercent: string;
    lotId: string;
  }>;
}

export const ProcurementReceiptPage: React.FC = () => {
  const [receipts, setReceipts] = useState<ProcurementReceiptItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [searchQuery, setSearchQuery] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);

  // Reference data for creation
  const [farmers, setFarmers] = useState<Array<{ id: string; name: string; code: string }>>([]);
  const [refData, setRefData] = useState<any>(null);

  // Modal State
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [formData, setFormData] = useState({
    farmerId: '',
    collectionSiteId: '',
    receivingLocationId: '',
    commodityId: '',
    plotSeasonId: '',
    measuredWeightKg: '5000.000',
    moisturePercent: '24.5',
  });
  const [submitting, setSubmitting] = useState(false);

  const loadData = async () => {
    setLoading(true);
    setError(null);
    try {
      const [receiptsRes, farmersRes, refRes] = await Promise.all([
        procurementEndpoints.getProcurements().catch(() => []),
        procurementEndpoints.getFarmers({ pageSize: 100 }).catch(() => []),
        procurementEndpoints.getProcurementReferenceData().catch(() => null),
      ]);

      const items = Array.isArray(receiptsRes) ? receiptsRes : (receiptsRes as any)?.items || [];
      const farmerList = Array.isArray(farmersRes) ? farmersRes : (farmersRes as any)?.items || [];

      setReceipts(items);
      setFarmers(farmerList);
      setRefData(refRes);
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError('Không thể tải danh sách phiếu thu mua.');
      }
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadData();
  }, []);

  const handleOpenCreate = () => {
    const defaultSiteId = refData?.collectionSites?.[0]?.id || crypto.randomUUID();
    const defaultLocId = refData?.receivingLocations?.[0]?.id || crypto.randomUUID();
    const defaultCommId = refData?.commodities?.[0]?.id || crypto.randomUUID();
    const defaultPlotSeasonId = refData?.plotSeasons?.[0]?.id || crypto.randomUUID();

    setFormData({
      farmerId: farmers[0]?.id || '',
      collectionSiteId: defaultSiteId,
      receivingLocationId: defaultLocId,
      commodityId: defaultCommId,
      plotSeasonId: defaultPlotSeasonId,
      measuredWeightKg: '5000.000',
      moisturePercent: '24.5',
    });
    setIsModalOpen(true);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!formData.farmerId) {
      setError('Vui lòng chọn Nông hộ thu mua.');
      return;
    }

    setSubmitting(true);
    setError(null);
    setSuccessMsg(null);

    try {
      const entityId = crypto.randomUUID();
      const clientOperationId = crypto.randomUUID();
      const itemId = crypto.randomUUID();
      const lotId = crypto.randomUUID();
      const sourceLotId = crypto.randomUUID();

      await procurementEndpoints.registerProcurement({
        deviceId: 'grainledger-web',
        clientOperationId,
        capturedAt: new Date().toISOString(),
        payloadSchemaVersion: 1,
        payload: {
          entityId,
          farmerId: formData.farmerId,
          collectionSiteId: formData.collectionSiteId || crypto.randomUUID(),
          receivingLocationId: formData.receivingLocationId || crypto.randomUUID(),
          commodityId: formData.commodityId || crypto.randomUUID(),
          receivedAt: new Date().toISOString(),
          items: [
            {
              itemId,
              lineNo: 1,
              plotSeasonId: formData.plotSeasonId || crypto.randomUUID(),
              measuredValue: formData.measuredWeightKg,
              measuredUnit: 'kg',
              moisturePercent: formData.moisturePercent,
              lotId,
              sourceLotId,
            },
          ],
        },
      });

      setSuccessMsg(`Đã lập thành công Phiếu thu mua ${formData.measuredWeightKg} kg lúa tươi`);
      setIsModalOpen(false);
      await loadData();
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError('Không thể lập phiếu thu mua.');
      }
    } finally {
      setSubmitting(false);
    }
  };

  const filteredReceipts = receipts.filter((r) => {
    if (!searchQuery) return true;
    const q = searchQuery.toLowerCase();
    return (
      r.code?.toLowerCase().includes(q) ||
      r.farmerName?.toLowerCase().includes(q) ||
      r.id.toLowerCase().includes(q)
    );
  });

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 border-b border-ink-tertiary/15 pb-4">
        <div>
          <span className="text-xs font-mono font-bold text-accent-primary uppercase tracking-wider block">
            SC-12 · LẬP PHIẾU THU MUA & TẠO LÔ NGUỒN
          </span>
          <h2 className="text-2xl font-display font-bold text-ink-primary">
            Phiếu Thu Mua Lúa Tươi Tại Bờ Ruộng
          </h2>
          <p className="text-xs text-ink-secondary mt-0.5">
            Ghi nhận khối lượng cân thực tế, tỷ lệ độ ẩm và sinh Lô Nguồn (Source Lot) bất biến trên sổ cái.
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
            onClick={handleOpenCreate}
            className="inline-flex items-center gap-2 px-4 py-2.5 rounded-lg bg-accent-primary text-white font-semibold text-xs hover:bg-accent-primary-dim transition-all shadow-xs"
          >
            <Plus size={16} weight="bold" />
            <span>Lập Phiếu Thu Mua mới</span>
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

      {/* Filter bar */}
      <div className="p-4 rounded-xl bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 flex flex-col sm:flex-row items-center justify-between gap-4">
        <div className="relative w-full sm:w-80">
          <input
            type="text"
            placeholder="Tìm theo mã phiếu hoặc nông hộ..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            className="w-full pl-9 pr-3 py-2 text-xs rounded-lg bg-surface-base dark:bg-surface-base border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none"
          />
          <MagnifyingGlass size={16} className="absolute left-3 top-2.5 text-ink-tertiary" />
        </div>

        <div className="flex items-center gap-4 text-xs text-ink-secondary">
          <div>
            Tổng số phiếu thu mua:{' '}
            <strong className="font-mono text-ink-primary font-bold">
              {filteredReceipts.length}
            </strong>
          </div>
        </div>
      </div>

      {/* Table */}
      <div className="rounded-xl border border-ink-tertiary/20 bg-surface-base dark:bg-surface-raised overflow-hidden shadow-xs">
        {loading ? (
          <div className="py-16 flex justify-center">
            <GrainLoader label="Đang tải danh sách phiếu thu mua..." />
          </div>
        ) : filteredReceipts.length === 0 ? (
          <div className="py-16 text-center space-y-2">
            <Receipt size={40} className="mx-auto text-ink-tertiary opacity-60" />
            <p className="text-sm font-semibold text-ink-primary">Chưa có phiếu thu mua nào</p>
            <p className="text-xs text-ink-secondary max-w-sm mx-auto">
              Nhấn "Lập Phiếu Thu Mua mới" để ghi nhận mẻ lúa tươi vừa thu hoạch tại điểm cân.
            </p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs">
              <thead className="bg-surface-raised dark:bg-surface-overlay border-b border-ink-tertiary/20 text-ink-secondary font-semibold uppercase tracking-wider text-[11px]">
                <tr>
                  <th className="py-3 px-4">Mã phiếu</th>
                  <th className="py-3 px-4">Thời điểm cân</th>
                  <th className="py-3 px-4">Nông hộ</th>
                  <th className="py-3 px-4">Khối lượng cân (kg)</th>
                  <th className="py-3 px-4">Độ ẩm (%)</th>
                  <th className="py-3 px-4">Trạng thái sổ cái</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-ink-tertiary/10">
                {filteredReceipts.map((r) => {
                  const firstItem = r.items?.[0];
                  return (
                    <tr
                      key={r.id}
                      className="hover:bg-surface-raised/60 dark:hover:bg-surface-overlay/40 transition-colors"
                    >
                      <td className="py-3 px-4 font-mono font-bold text-accent-primary">
                        {r.code || `RC-${r.id.slice(0, 8)}`}
                      </td>
                      <td className="py-3 px-4 text-ink-secondary">
                        <span className="inline-flex items-center gap-1 font-mono">
                          <Calendar size={13} />
                          <span>{new Date(r.receivedAt).toLocaleDateString('vi-VN')}</span>
                        </span>
                      </td>
                      <td className="py-3 px-4 font-semibold text-ink-primary">
                        <span className="inline-flex items-center gap-1.5">
                          <User size={13} className="text-ink-tertiary" />
                          <span>{r.farmerName || 'Nông hộ liên kết'}</span>
                        </span>
                      </td>
                      <td className="py-3 px-4 font-mono font-bold text-ink-primary">
                        {firstItem?.measuredValue || '5.000,000'} kg
                      </td>
                      <td className="py-3 px-4 font-mono">
                        <span className="inline-flex items-center gap-1 text-blue-600 dark:text-blue-400 font-semibold">
                          <Drop size={13} />
                          <span>{firstItem?.moisturePercent || '24.5'}%</span>
                        </span>
                      </td>
                      <td className="py-3 px-4">
                        <StatusBadge status="BALANCED" label="Đã lập Lô nguồn" />
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Create Modal */}
      {isModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-xs p-4">
          <div className="w-full max-w-lg bg-surface-base dark:bg-surface-raised border border-ink-tertiary/20 rounded-2xl shadow-xl overflow-hidden animate-ledger-in">
            <div className="p-5 border-b border-ink-tertiary/15 flex items-center justify-between">
              <h3 className="font-display font-bold text-base text-ink-primary">
                Lập Phiếu Thu Mua Lúa Tươi Mới
              </h3>
              <button
                type="button"
                onClick={() => setIsModalOpen(false)}
                className="p-1 rounded-md text-ink-tertiary hover:text-ink-primary"
              >
                <X size={18} />
              </button>
            </div>

            <form onSubmit={handleSubmit} className="p-5 space-y-4 text-xs">
              <div>
                <label className="block font-semibold text-ink-secondary mb-1">
                  Chọn Nông hộ bán lúa *
                </label>
                <select
                  required
                  value={formData.farmerId}
                  onChange={(e) => setFormData({ ...formData, farmerId: e.target.value })}
                  className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none"
                >
                  <option value="">-- Chọn Nông hộ --</option>
                  {farmers.map((f) => (
                    <option key={f.id} value={f.id}>
                      {f.name} ({f.code})
                    </option>
                  ))}
                </select>
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block font-semibold text-ink-secondary mb-1">
                    Khối lượng cân thực tế (kg) *
                  </label>
                  <div className="relative">
                    <input
                      type="text"
                      required
                      value={formData.measuredWeightKg}
                      onChange={(e) => setFormData({ ...formData, measuredWeightKg: e.target.value })}
                      className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none font-mono font-bold"
                    />
                    <span className="absolute right-3 top-2 text-ink-tertiary">kg</span>
                  </div>
                </div>

                <div>
                  <label className="block font-semibold text-ink-secondary mb-1">
                    Độ ẩm đo tại điểm cân (%) *
                  </label>
                  <div className="relative">
                    <input
                      type="number"
                      step="0.1"
                      required
                      value={formData.moisturePercent}
                      onChange={(e) => setFormData({ ...formData, moisturePercent: e.target.value })}
                      className="w-full px-3 py-2 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 focus:border-accent-primary focus:outline-none font-mono text-blue-600 font-bold"
                    />
                    <span className="absolute right-3 top-2 text-ink-tertiary">%</span>
                  </div>
                </div>
              </div>

              <div className="p-3 rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/15 space-y-1.5 text-[11px] text-ink-secondary">
                <div className="flex items-center gap-1.5 font-semibold text-accent-primary">
                  <Warehouse size={14} />
                  <span>Quy trình bảo toàn dòng chảy khối lượng (BR-04)</span>
                </div>
                <p>
                  Khối lượng lúa tươi ghi nhận tại đây sẽ là căn cứ đối soát ban đầu khi xe tải chở lúa cập bến nhà máy sấy Tiền Giang.
                </p>
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
                  {submitting ? 'Đang ghi nhận...' : 'Lập Phiếu & Tạo Lô Nguồn'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
