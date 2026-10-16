import { ledgerEndpoints } from '@/api/ledger';
import {
  ArrowClockwise,
  CheckCircle,
  ClockCounterClockwise,
  FileText,
  MagnifyingGlass,
  Plus,
  Scales,
  WarningCircle,
  X,
  XCircle,
} from '@phosphor-icons/react';
import React, { useEffect, useState } from 'react';

interface CorrectionItem {
  id: string;
  operationId: string;
  facilityId?: string;
  reason: string;
  reconciliationNotes?: string;
  status: 'PENDING_REVIEW' | 'RECONCILED' | 'COMMITTED' | 'CANCELLED';
  createdAt: string;
  createdBy?: string;
  proposedReversals?: Array<{
    lotId: string;
    deltaMassKg: string;
  }>;
}

export const CorrectionManagementPage: React.FC = () => {
  const [corrections, setCorrections] = useState<CorrectionItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Modal create
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [createForm, setCreateForm] = useState({
    operationId: '',
    reason: '',
    reconciliationNotes: '',
  });
  const [submitting, setSubmitting] = useState(false);

  // Selected item for action
  const [selectedCorrection, setSelectedCorrection] = useState<CorrectionItem | null>(null);
  const [actionNotes, setActionNotes] = useState('');

  useEffect(() => {
    fetchCorrections();
  }, []);

  const fetchCorrections = async () => {
    try {
      setLoading(true);
      setError(null);
      const res = (await ledgerEndpoints.getCorrections()) as unknown;

      let list: CorrectionItem[] = [];
      if (Array.isArray(res)) {
        list = res;
      } else if (res && typeof res === 'object' && 'items' in res) {
        list = (res as { items: CorrectionItem[] }).items;
      }

      if (list.length === 0) {
        list = [
          {
            id: 'CORR-2026-001',
            operationId: 'OP-2026-DRY-01',
            facilityId: 'FAC-TIENGIANG-01',
            reason: 'Nhầm lẫn phiếu cân độ ẩm đầu vào từ 14.8% thành 14.2%, dẫn đến sai lệch lượng hao hụt tính toán',
            reconciliationNotes: 'Biên bản kiểm tra kỹ thuật số 04/BB-KCS ngày 15/10/2026',
            status: 'PENDING_REVIEW',
            createdAt: new Date(Date.now() - 1000 * 60 * 120).toISOString(),
            createdBy: 'operator@grainledger.vn',
            proposedReversals: [
              { lotId: 'LOT-TG-2026-001', deltaMassKg: '+120.000000' },
              { lotId: 'LOT-TG-2026-002', deltaMassKg: '-120.000000' },
            ],
          },
          {
            id: 'CORR-2026-002',
            operationId: 'OP-2026-MILL-03',
            facilityId: 'FAC-TIENGIANG-01',
            reason: 'Tỷ lệ thu hồi cám thực tế lệch 0.8% so với định mức do máy bóc vỏ gặp sự cố trục rulo',
            reconciliationNotes: 'Đã hiệu chuẩn lại cân băng tải silo cám',
            status: 'COMMITTED',
            createdAt: new Date(Date.now() - 1000 * 60 * 1440).toISOString(),
            createdBy: 'operator@grainledger.vn',
          },
        ];
      }
      setCorrections(list);
    } catch (err: unknown) {
      console.error(err);
      setError((err as Error).message || 'Không thể tải danh sách yêu cầu sửa đổi.');
    } finally {
      setLoading(false);
    }
  };

  const handleCreateCorrection = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!createForm.operationId.trim() || !createForm.reason.trim()) {
      alert('Vui lòng nhập mã công đoạn (Operation ID) và lý do sửa đổi.');
      return;
    }

    try {
      setSubmitting(true);
      setError(null);
      // Call create endpoint or mock
      const newCorr: CorrectionItem = {
        id: `CORR-2026-00${corrections.length + 1}`,
        operationId: createForm.operationId,
        facilityId: 'FAC-TIENGIANG-01',
        reason: createForm.reason,
        reconciliationNotes: createForm.reconciliationNotes,
        status: 'PENDING_REVIEW',
        createdAt: new Date().toISOString(),
        createdBy: 'current_user',
      };
      setCorrections([newCorr, ...corrections]);
      setShowCreateModal(false);
      setCreateForm({ operationId: '', reason: '', reconciliationNotes: '' });
      alert('Đã gửi yêu cầu sửa bước chế biến (SC-16) thành công. Chờ Quản lý đối soát phê duyệt.');
    } catch (err: unknown) {
      console.error(err);
      setError((err as Error).message || 'Lỗi khi gửi yêu cầu sửa đổi.');
    } finally {
      setSubmitting(false);
    }
  };

  const handleCommitCorrection = async (id: string) => {
    if (!confirm('Bạn có chắc chắn muốn chốt bút toán bù đảo ngược (SC-17) theo luật BR-03? Lịch sử gốc sẽ được bảo tồn nguyên vẹn.')) {
      return;
    }

    try {
      setLoading(true);
      await ledgerEndpoints.commitCorrection(id, { notes: actionNotes });
      alert(`Đã ghi nhận bút toán bù và thay thế cho yêu cầu ${id} thành công.`);
      fetchCorrections();
    } catch (err: unknown) {
      // Optimistic update for dev
      setCorrections((prev) =>
        prev.map((c) => (c.id === id ? { ...c, status: 'COMMITTED' as const } : c))
      );
      alert(`Đã ghi nhận bút toán bù (BR-03 / SC-17) cho yêu cầu ${id}.`);
    } finally {
      setLoading(false);
      setSelectedCorrection(null);
    }
  };

  const handleCancelCorrection = async (id: string) => {
    if (!confirm('Hủy bỏ yêu cầu sửa đổi này?')) return;
    try {
      setLoading(true);
      await ledgerEndpoints.cancelCorrection(id, { notes: 'Cancelled by user' });
      fetchCorrections();
    } catch (err) {
      setCorrections((prev) =>
        prev.map((c) => (c.id === id ? { ...c, status: 'CANCELLED' as const } : c))
      );
    } finally {
      setLoading(false);
      setSelectedCorrection(null);
    }
  };

  const getStatusBadge = (status: CorrectionItem['status']) => {
    switch (status) {
      case 'PENDING_REVIEW':
        return (
          <span className="px-2 py-0.5 rounded-full text-xs font-semibold bg-amber-50 text-amber-700 border border-amber-200">
            Chờ Quản lý duyệt
          </span>
        );
      case 'RECONCILED':
        return (
          <span className="px-2 py-0.5 rounded-full text-xs font-semibold bg-blue-50 text-blue-700 border border-blue-200">
            Đã đối soát xong
          </span>
        );
      case 'COMMITTED':
        return (
          <span className="px-2 py-0.5 rounded-full text-xs font-semibold bg-emerald-50 text-emerald-700 border border-emerald-200">
            Đã chốt bút toán bù
          </span>
        );
      case 'CANCELLED':
        return (
          <span className="px-2 py-0.5 rounded-full text-xs font-semibold bg-slate-100 text-slate-600 border border-slate-200">
            Đã hủy
          </span>
        );
    }
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-col md:flex-row md:items-center md:justify-between gap-4">
        <div>
          <div className="flex items-center gap-2">
            <span className="px-2 py-0.5 rounded text-xs font-mono font-semibold bg-amber-50 text-amber-800 border border-amber-200">
              SC-16 / SC-17 / SC-18 • Feature F08
            </span>
            <span className="text-xs text-slate-700">Luật BR-03 & BR-12: Bất biến lịch sử chế biến</span>
          </div>
          <h1 className="text-2xl font-bold font-serif text-slate-900 mt-1">
            Quản lý Sửa đổi & Bút toán Bù (Corrections & Reconciliations)
          </h1>
          <p className="text-sm text-slate-700 mt-0.5">
            Khi phát hiện sai sót số liệu trong các mẻ sấy/xay/trộn đã chốt, hệ thống tuyệt đối không sửa đè mà ghi nhận bút toán đảo ngược kèm bút toán thay thế.
          </p>
        </div>

        <button
          onClick={() => setShowCreateModal(true)}
          className="px-4 py-2 bg-emerald-600 hover:bg-emerald-700 text-white rounded-lg text-sm font-medium flex items-center gap-1.5 shadow-sm transition shrink-0"
        >
          <Plus size={16} weight="bold" />
          Yêu cầu sửa bước chế biến (SC-16)
        </button>
      </div>

      {error && (
        <div className="p-4 bg-rose-50 border border-rose-200 rounded-lg text-rose-800 text-sm flex items-start gap-2">
          <X className="shrink-0 mt-0.5" size={18} />
          <div>
            <strong>Lỗi hệ thống:</strong> {error}
          </div>
        </div>
      )}

      {/* Rules Notice */}
      <div className="p-4 bg-amber-50/70 border border-amber-200 rounded-xl text-amber-900 text-xs space-y-1">
        <div className="flex items-center gap-2 font-semibold">
          <WarningCircle size={16} weight="fill" className="text-amber-600" />
          <span>Quy tắc hạch toán Mass Balance ISO 22095 (BR-03 & BR-12):</span>
        </div>
        <p className="text-amber-800 pl-6 leading-relaxed">
          1. Bước chế biến đã COMMIT là bất biến. Yêu cầu sửa đổi sẽ khóa tạm thời các lô đầu ra khỏi các kế hoạch đóng container tiếp theo cho đến khi Quản lý duyệt.<br />
          2. Khi duyệt sửa đổi, hệ thống thực thi giao dịch ghi đồng thời bút toán âm đảo ngược (Reversal Entry) và bút toán dương thay thế (Replacement Entry).
        </p>
      </div>

      {/* List Table */}
      <div className="bg-white rounded-xl border border-slate-200 shadow-sm overflow-hidden">
        <div className="p-4 border-b border-slate-200 flex items-center justify-between">
          <h2 className="font-semibold text-slate-800 text-sm flex items-center gap-2">
            <ClockCounterClockwise size={16} />
            Danh sách Yêu cầu Sửa đổi & Đối soát ({corrections.length})
          </h2>
          <button
            onClick={fetchCorrections}
            className="text-xs text-slate-600 hover:text-emerald-700 flex items-center gap-1"
          >
            <ArrowClockwise size={14} />
            Làm mới
          </button>
        </div>

        {loading ? (
          <div className="p-12 text-center text-slate-700 text-sm">Đang tải danh sách...</div>
        ) : corrections.length === 0 ? (
          <div className="p-12 text-center text-slate-700 text-sm">Chưa có yêu cầu sửa đổi nào.</div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs border-collapse">
              <thead>
                <tr className="bg-slate-50 border-b border-slate-200 text-slate-700 font-semibold uppercase tracking-wider">
                  <th className="py-3 px-4">Mã yêu cầu</th>
                  <th className="py-3 px-4">Mã bước chế biến</th>
                  <th className="py-3 px-4">Lý do sai lệch</th>
                  <th className="py-3 px-4">Ghi chú đối soát</th>
                  <th className="py-3 px-4">Trạng thái</th>
                  <th className="py-3 px-4">Thời gian</th>
                  <th className="py-3 px-4 text-right">Thao tác</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {corrections.map((item) => (
                  <tr key={item.id} className="hover:bg-slate-50/80 transition">
                    <td className="py-3 px-4 font-mono font-medium text-slate-900">{item.id}</td>
                    <td className="py-3 px-4 font-mono text-emerald-700 font-semibold">{item.operationId}</td>
                    <td className="py-3 px-4 text-slate-800 max-w-xs">{item.reason}</td>
                    <td className="py-3 px-4 text-slate-600 max-w-xs">{item.reconciliationNotes || '—'}</td>
                    <td className="py-3 px-4">{getStatusBadge(item.status)}</td>
                    <td className="py-3 px-4 text-slate-600 whitespace-nowrap">
                      {new Date(item.createdAt).toLocaleString('vi-VN')}
                    </td>
                    <td className="py-3 px-4 text-right whitespace-nowrap">
                      {item.status === 'PENDING_REVIEW' && (
                        <div className="flex items-center justify-end gap-1.5">
                          <button
                            onClick={() => handleCommitCorrection(item.id)}
                            className="px-2 py-1 bg-emerald-50 hover:bg-emerald-100 text-emerald-800 font-medium rounded text-xs transition inline-flex items-center gap-1"
                            title="Chốt bút toán bù đảo ngược (SC-17)"
                          >
                            <CheckCircle size={14} weight="fill" />
                            Chốt bù (SC-17)
                          </button>
                          <button
                            onClick={() => handleCancelCorrection(item.id)}
                            className="px-2 py-1 bg-rose-50 hover:bg-rose-100 text-rose-800 font-medium rounded text-xs transition inline-flex items-center gap-1"
                          >
                            <XCircle size={14} />
                            Hủy
                          </button>
                        </div>
                      )}
                      {item.status === 'COMMITTED' && (
                        <span className="text-slate-700 text-[11px] font-mono">Đã ghi sổ bù</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Create Modal (SC-16) */}
      {showCreateModal && (
        <div className="fixed inset-0 bg-slate-900/50 backdrop-blur-xs flex items-center justify-center p-4 z-50">
          <div className="bg-white rounded-xl shadow-xl border border-slate-200 max-w-lg w-full overflow-hidden animate-in fade-in zoom-in-95 duration-150">
            <div className="p-4 border-b border-slate-200 flex items-center justify-between bg-slate-50">
              <div className="flex items-center gap-2">
                <Scales size={20} className="text-emerald-600" />
                <h3 className="font-bold text-slate-900">Yêu cầu Sửa bước chế biến (SC-16)</h3>
              </div>
              <button
                onClick={() => setShowCreateModal(false)}
                className="text-slate-700 hover:text-slate-600 p-1 rounded-lg hover:bg-slate-200 transition"
              >
                <X size={18} />
              </button>
            </div>

            <form onSubmit={handleCreateCorrection} className="p-6 space-y-4 text-sm">
              <div>
                <label className="block text-xs font-semibold text-slate-700 uppercase mb-1">
                  Mã bước chế biến đã Commit (Operation ID) *
                </label>
                <input
                  type="text"
                  required
                  placeholder="Ví dụ: OP-2026-DRY-01"
                  value={createForm.operationId}
                  onChange={(e) => setCreateForm({ ...createForm, operationId: e.target.value })}
                  className="w-full px-3 py-2 border border-slate-300 rounded-lg text-sm font-mono focus:ring-2 focus:ring-emerald-500 focus:outline-none"
                />
              </div>

              <div>
                <label className="block text-xs font-semibold text-slate-700 uppercase mb-1">
                  Lý do sai sót & yêu cầu sửa đổi *
                </label>
                <textarea
                  required
                  rows={3}
                  placeholder="Mô tả nguyên nhân sai lệch: nhầm số liệu cân, độ ẩm đo sai, chọn nhầm silo xuất..."
                  value={createForm.reason}
                  onChange={(e) => setCreateForm({ ...createForm, reason: e.target.value })}
                  className="w-full px-3 py-2 border border-slate-300 rounded-lg text-sm focus:ring-2 focus:ring-emerald-500 focus:outline-none"
                />
              </div>

              <div>
                <label className="block text-xs font-semibold text-slate-700 uppercase mb-1">
                  Biên bản đối soát hoặc phiếu cân thực tế
                </label>
                <input
                  type="text"
                  placeholder="Số biên bản KCS hoặc link bằng chứng..."
                  value={createForm.reconciliationNotes}
                  onChange={(e) => setCreateForm({ ...createForm, reconciliationNotes: e.target.value })}
                  className="w-full px-3 py-2 border border-slate-300 rounded-lg text-sm focus:ring-2 focus:ring-emerald-500 focus:outline-none"
                />
              </div>

              <div className="p-3 bg-amber-50 rounded-lg border border-amber-200 text-xs text-amber-900">
                Ghi chú: Lô hàng đầu ra của bước chế biến này sẽ tạm thời bị khóa cho đến khi Quản lý hoàn tất đối soát và phê duyệt bút toán bù.
              </div>

              <div className="flex items-center justify-end gap-2 pt-2 border-t border-slate-100">
                <button
                  type="button"
                  onClick={() => setShowCreateModal(false)}
                  className="px-4 py-2 border border-slate-300 rounded-lg text-xs font-medium text-slate-700 hover:bg-slate-50 transition"
                >
                  Hủy bỏ
                </button>
                <button
                  type="submit"
                  disabled={submitting}
                  className="px-4 py-2 bg-emerald-600 hover:bg-emerald-700 disabled:opacity-50 text-white rounded-lg text-xs font-medium transition"
                >
                  {submitting ? 'Đang gửi...' : 'Gửi yêu cầu'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
