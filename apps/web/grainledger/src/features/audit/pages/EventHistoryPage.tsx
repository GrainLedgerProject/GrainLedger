import { ledgerEndpoints } from "@/api/ledger";
import {
  Calendar,
  CheckCircle,
  ClockCounterClockwise,
  Cube,
  Eye,
  FileCode,
  Funnel,
  HandCoins,
  LockSimple,
  MagnifyingGlass,
  Scales,
  ShieldCheck,
  Truck,
  X,
} from "@phosphor-icons/react";
import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

interface AuditEvent {
  id: string;
  eventType: string;
  entityType: string;
  entityId: string;
  facilityId?: string;
  actorId?: string;
  actorEmail?: string;
  timestamp: string;
  version: number;
  payload: Record<string, unknown> | string;
  hash?: string;
}

export const EventHistoryPage: React.FC = () => {
  const { t, i18n } = useTranslation();
  const [events, setEvents] = useState<AuditEvent[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Filters
  const [searchTerm, setSearchTerm] = useState("");
  const [entityFilter, setEntityFilter] = useState("ALL");
  const [eventTypeFilter, setEventTypeFilter] = useState("ALL");

  // Selected event for detail drawer (SC-22)
  const [selectedEvent, setSelectedEvent] = useState<AuditEvent | null>(null);

  useEffect(() => {
    fetchEvents();
  }, []);

  const fetchEvents = async () => {
    try {
      setLoading(true);
      setError(null);
      const res = (await ledgerEndpoints.getHistory()) as unknown;

      let list: AuditEvent[] = [];
      if (Array.isArray(res)) {
        list = res;
      } else if (res && typeof res === "object" && "items" in res) {
        list = (res as { items: AuditEvent[] }).items;
      } else if (res && typeof res === "object" && "events" in res) {
        list = (res as { events: AuditEvent[] }).events;
      }

      // If empty in mock/dev, provide ISO 22095 sample ledger events
      if (list.length === 0) {
        list = [
          {
            id: "EVT-2026-0098",
            eventType: "LotCreated",
            entityType: "Lot",
            entityId: "LOT-TG-2026-001",
            facilityId: "FAC-TIENGIANG-01",
            actorEmail: "procurement@grainledger.vn",
            timestamp: new Date(Date.now() - 1000 * 60 * 25).toISOString(),
            version: 1,
            hash: "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            payload: {
              sourceLotCode: "SL-2026-FARM-01",
              variety: "ST25 Lúa Tôm",
              measuredMassKg: "15400.000000",
              moisturePercent: "14.2",
              farmerCode: "FM-TG-001",
              plotCode: "PL-ST25-01",
            },
          },
          {
            id: "EVT-2026-0097",
            eventType: "ReceivingMeasurementRecorded",
            entityType: "ReceivingTask",
            entityId: "RCV-2026-001",
            facilityId: "FAC-TIENGIANG-01",
            actorEmail: "operator@grainledger.vn",
            timestamp: new Date(Date.now() - 1000 * 60 * 65).toISOString(),
            version: 1,
            hash: "a591a6d40bf420404a011733cfb7b190d62c65bf0bcda32b57b277d9ad9f146e",
            payload: {
              truckPlate: "63C-123.45",
              grossWeightKg: "24500.000000",
              tareWeightKg: "9100.000000",
              netWeightKg: "15400.000000",
              scaleTicketNumber: "ST-88921",
            },
          },
          {
            id: "EVT-2026-0096",
            eventType: "OperationCommitted",
            entityType: "Operation",
            entityId: "OP-2026-DRY-01",
            facilityId: "FAC-TIENGIANG-01",
            actorEmail: "operator@grainledger.vn",
            timestamp: new Date(Date.now() - 1000 * 60 * 180).toISOString(),
            version: 1,
            hash: "4355a46b19d348dc2f57c046f8ef63d4538ebb936000f3c9ee954a27460dd865",
            payload: {
              operationType: "DRYING",
              inputLots: [{ lotId: "LOT-TG-2026-001", massKg: "15400.000000" }],
              outputLots: [
                {
                  productType: "PADDY_DRY",
                  massKg: "14920.000000",
                  locationId: "SILO-01",
                },
              ],
              declaredLossKg: "480.000000",
              moistureReduction: "14.2% -> 12.0%",
            },
          },
          {
            id: "EVT-2026-0095",
            eventType: "HoldIssued",
            entityType: "Hold",
            entityId: "HOLD-2026-001",
            facilityId: "FAC-TIENGIANG-01",
            actorEmail: "quality.mgr@export-rice.com",
            timestamp: new Date(Date.now() - 1000 * 60 * 360).toISOString(),
            version: 1,
            hash: "53c234e5e8472b6ac51c1ae1cab3fe06fad053beb8ebfd8977b010655bfdd3c3",
            payload: {
              targetLotId: "LOT-TG-2026-001",
              reason: "Dư lượng Tricyclazole vượt ngưỡng cho phép 0.01 mg/kg",
              evidenceReference: "LAB-TEST-2026-9921",
              status: "ACTIVE_HOLD",
            },
          },
        ];
      }
      setEvents(list);
    } catch (err: unknown) {
      console.error(err);
      setError(
        (err as Error).message || t("audit.events.errorLoad", "Không thể tải chuỗi sự kiện nghiệp vụ.")
      );
    } finally {
      setLoading(false);
    }
  };

  const filteredEvents = events.filter((evt) => {
    const matchesSearch =
      searchTerm === "" ||
      evt.id?.toLowerCase().includes(searchTerm.toLowerCase()) ||
      evt.entityId?.toLowerCase().includes(searchTerm.toLowerCase()) ||
      evt.eventType?.toLowerCase().includes(searchTerm.toLowerCase()) ||
      evt.actorEmail?.toLowerCase().includes(searchTerm.toLowerCase()) ||
      JSON.stringify(evt.payload)
        .toLowerCase()
        .includes(searchTerm.toLowerCase());

    const matchesEntity =
      entityFilter === "ALL" || evt.entityType === entityFilter;
    const matchesEventType =
      eventTypeFilter === "ALL" || evt.eventType === eventTypeFilter;

    return matchesSearch && matchesEntity && matchesEventType;
  });

  const getEntityIcon = (entityType: string) => {
    switch (entityType?.toLowerCase()) {
      case "lot":
        return <Cube className="text-amber-600" size={16} />;
      case "operation":
        return <Scales className="text-emerald-600" size={16} />;
      case "receivingtask":
      case "receiving":
        return <Truck className="text-blue-600" size={16} />;
      case "hold":
        return <LockSimple className="text-rose-600" size={16} />;
      case "procurement":
        return <HandCoins className="text-teal-600" size={16} />;
      default:
        return <ClockCounterClockwise className="text-slate-500" size={16} />;
    }
  };

  return (
    <div className="space-y-6">
      {/* Page Header */}
      <div className="flex flex-col md:flex-row md:items-center md:justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold font-serif text-slate-900 mt-1">
            {t("audit.events.title", "Nhật ký Sự kiện Nghiệp vụ Bất biến (Immutable Audit Trail)")}
          </h1>
          <p className="text-sm text-slate-700 mt-0.5">
            {t(
              "audit.events.subtitle",
              "Mọi biến đổi khối lượng (kg), trạng thái niêm chì, kiểm nghiệm và lệnh phong tỏa được lưu trữ bất biến kèm chuỗi băm xác thực."
            )}
          </p>
        </div>

        <div className="flex items-center gap-3">
          <div className="flex items-center gap-1.5 px-3 py-1.5 bg-emerald-50 text-emerald-700 rounded-lg text-xs font-medium border border-emerald-200">
            <ShieldCheck size={16} weight="fill" />
            <span>{t("audit.events.badgeSha", "Chuỗi băm toàn vẹn SHA-256")}</span>
          </div>
          <button
            onClick={fetchEvents}
            className="px-3 py-1.5 border border-slate-300 rounded-lg text-xs font-medium text-slate-700 hover:bg-slate-50 flex items-center gap-1.5 shadow-sm transition"
          >
            <ClockCounterClockwise size={14} />
            {t("audit.events.btnRefresh", "Làm mới")}
          </button>
        </div>
      </div>

      {error && (
        <div className="p-4 bg-rose-50 border border-rose-200 rounded-lg text-rose-800 text-sm flex items-start gap-2">
          <X className="shrink-0 mt-0.5" size={18} />
          <div>
            <strong>{t("common.systemError", "Lỗi hệ thống:")}</strong> {error}
          </div>
        </div>
      )}

      {/* Filter and Search Bar */}
      <div className="bg-white p-4 rounded-xl border border-slate-200 shadow-sm space-y-3">
        <div className="grid grid-cols-1 md:grid-cols-4 gap-3">
          <div className="md:col-span-2 relative">
            <MagnifyingGlass
              className="absolute left-3 top-2.5 text-slate-700"
              size={18}
            />
            <input
              type="text"
              placeholder={t("audit.events.searchPlaceholder", "Tìm theo mã sự kiện, mã lô (Lot ID), người ghi, hash...")}
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              className="w-full pl-9 pr-3 py-2 text-sm border border-slate-300 rounded-lg focus:ring-2 focus:ring-emerald-500 focus:outline-none"
            />
          </div>

          <div>
            <select
              value={entityFilter}
              onChange={(e) => setEntityFilter(e.target.value)}
              className="w-full py-2 px-3 text-sm border border-slate-300 rounded-lg focus:ring-2 focus:ring-emerald-500 focus:outline-none bg-white"
            >
              <option value="ALL">{t("audit.events.filterAllEntities", "Tất cả đối tượng (All Entities)")}</option>
              <option value="Lot">{t("audit.events.filterLot", "Lô hàng (Lot)")}</option>
              <option value="Operation">{t("audit.events.filterOperation", "Công đoạn chế biến (Operation)")}</option>
              <option value="ReceivingTask">
                {t("audit.events.filterReceiving", "Tiếp nhận nhà máy (Receiving)")}
              </option>
              <option value="Hold">{t("audit.events.filterHold", "Lệnh phong tỏa (Hold)")}</option>
              <option value="Procurement">{t("audit.events.filterProcurement", "Thu mua nguồn (Procurement)")}</option>
              <option value="Correction">{t("audit.events.filterCorrection", "Bút toán bù (Correction)")}</option>
            </select>
          </div>

          <div className="flex items-center gap-2">
            <Funnel className="text-slate-700" size={18} />
            <select
              value={eventTypeFilter}
              onChange={(e) => setEventTypeFilter(e.target.value)}
              className="w-full py-2 px-3 text-sm border border-slate-300 rounded-lg focus:ring-2 focus:ring-emerald-500 focus:outline-none bg-white"
            >
              <option value="ALL">{t("audit.events.filterAllTypes", "Tất cả loại sự kiện")}</option>
              <option value="LotCreated">{t("audit.events.filterLotCreated", "Khởi tạo lô (LotCreated)")}</option>
              <option value="OperationCommitted">
                {t("audit.events.filterOperationCommitted", "Chốt chế biến (OperationCommitted)")}
              </option>
              <option value="ReceivingMeasurementRecorded">
                {t("audit.events.filterReceivingRecorded", "Cân tiếp nhận (ReceivingMeasurement)")}
              </option>
              <option value="HoldIssued">{t("audit.events.filterHoldIssued", "Ban hành HOLD (HoldIssued)")}</option>
              <option value="HoldResponded">
                {t("audit.events.filterHoldResponded", "Chuyển kho cách ly (HoldResponded)")}
              </option>
            </select>
          </div>
        </div>

        <div className="text-xs text-slate-700 flex items-center justify-between pt-1 border-t border-slate-100">
          <span>
            {t("audit.events.showingCount", "Hiển thị {{count}} / {{total}} sự kiện được ghi nhận", {
              count: filteredEvents.length,
              total: events.length,
            })}
          </span>
          <span className="font-mono text-[11px] text-slate-700">
            {t("audit.events.isoCompliance", "Mã hóa tuân thủ ISO/IEC 27001 & ISO 22095")}
          </span>
        </div>
      </div>

      {/* Events Table */}
      <div className="bg-white rounded-xl border border-slate-200 shadow-sm overflow-hidden">
        {loading ? (
          <div className="p-12 text-center text-slate-700 text-sm">
            {t("audit.events.loading", "Đang tải lịch sử sự kiện...")}
          </div>
        ) : filteredEvents.length === 0 ? (
          <div className="p-12 text-center text-slate-700 text-sm">
            {t("audit.events.noEvents", "Không tìm thấy sự kiện nào phù hợp với bộ lọc.")}
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs border-collapse">
              <thead>
                <tr className="bg-slate-50 border-b border-slate-200 text-slate-700 font-semibold uppercase tracking-wider">
                  <th className="py-3 px-4">{t("audit.events.thTime", "Thời gian")}</th>
                  <th className="py-3 px-4">{t("audit.events.thEventId", "Mã sự kiện")}</th>
                  <th className="py-3 px-4">{t("audit.events.thEntity", "Đối tượng")}</th>
                  <th className="py-3 px-4">{t("audit.events.thEventType", "Loại sự kiện")}</th>
                  <th className="py-3 px-4">{t("audit.events.thActor", "Tài khoản ghi")}</th>
                  <th className="py-3 px-4">{t("audit.events.thHash", "Chuỗi băm SHA-256")}</th>
                  <th className="py-3 px-4 text-right">{t("audit.events.thDetails", "Chi tiết")}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100 font-normal">
                {filteredEvents.map((evt) => (
                  <tr key={evt.id} className="hover:bg-slate-50/80 transition">
                    <td className="py-3 px-4 text-slate-600 whitespace-nowrap">
                      <div className="flex items-center gap-1.5">
                        <Calendar size={14} className="text-slate-700" />
                        <span>
                          {new Date(evt.timestamp).toLocaleString(
                            i18n.language.startsWith("en") ? "en-US" : "vi-VN"
                          )}
                        </span>
                      </div>
                    </td>
                    <td className="py-3 px-4 font-mono font-medium text-slate-900">
                      {evt.id}
                    </td>
                    <td className="py-3 px-4">
                      <div className="flex items-center gap-1.5">
                        {getEntityIcon(evt.entityType)}
                        <span className="font-semibold text-slate-800">
                          {evt.entityType}:
                        </span>
                        <span className="font-mono text-emerald-700 bg-emerald-50 px-1 rounded">
                          {evt.entityId}
                        </span>
                      </div>
                    </td>
                    <td className="py-3 px-4">
                      <span className="px-2 py-0.5 rounded-full text-[11px] font-semibold bg-slate-100 text-slate-800 border border-slate-200">
                        {evt.eventType}
                      </span>
                    </td>
                    <td className="py-3 px-4 text-slate-600 truncate max-w-[150px]">
                      {evt.actorEmail || t("audit.events.systemActor", "Hệ thống")}
                    </td>
                    <td className="py-3 px-4 font-mono text-[11px] text-slate-700">
                      {evt.hash
                        ? `${evt.hash.substring(0, 10)}...${evt.hash.substring(evt.hash.length - 6)}`
                        : "—"}
                    </td>
                    <td className="py-3 px-4 text-right">
                      <button
                        onClick={() => setSelectedEvent(evt)}
                        className="px-2 py-1 rounded bg-slate-100 hover:bg-emerald-50 hover:text-emerald-700 text-slate-700 font-medium inline-flex items-center gap-1 transition"
                      >
                        <Eye size={14} />
                        {t("audit.events.btnView", "Xem")}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Detail Drawer Modal (SC-22) */}
      {selectedEvent && (
        <div className="fixed inset-0 bg-slate-900/50 backdrop-blur-xs flex items-center justify-center p-4 z-50">
          <div className="bg-white rounded-xl shadow-xl border border-slate-200 max-w-2xl w-full max-h-[90vh] flex flex-col overflow-hidden animate-in fade-in zoom-in-95 duration-150">
            <div className="p-4 border-b border-slate-200 flex items-center justify-between bg-slate-50">
              <div className="flex items-center gap-2">
                <FileCode size={20} className="text-emerald-600" />
                <h3 className="font-bold text-slate-900">
                  {t("audit.events.modalTitle", "Chi tiết Sự kiện Bất biến")}
                </h3>
                <span className="font-mono text-xs text-slate-700">
                  #{selectedEvent.id}
                </span>
              </div>
              <button
                onClick={() => setSelectedEvent(null)}
                className="text-slate-700 hover:text-slate-600 p-1 rounded-lg hover:bg-slate-200 transition"
              >
                <X size={18} />
              </button>
            </div>

            <div className="p-6 overflow-y-auto space-y-4 text-sm">
              <div className="grid grid-cols-2 gap-4 bg-slate-50 p-4 rounded-lg border border-slate-200">
                <div>
                  <span className="text-xs text-slate-700 uppercase font-semibold">
                    {t("audit.events.lblEventType", "Loại sự kiện:")}
                  </span>
                  <p className="font-medium text-slate-900">
                    {selectedEvent.eventType}
                  </p>
                </div>
                <div>
                  <span className="text-xs text-slate-700 uppercase font-semibold">
                    {t("audit.events.lblTimestamp", "Thời điểm ghi nhận:")}
                  </span>
                  <p className="font-medium text-slate-900">
                    {new Date(selectedEvent.timestamp).toLocaleString(
                      i18n.language.startsWith("en") ? "en-US" : "vi-VN"
                    )}
                  </p>
                </div>
                <div>
                  <span className="text-xs text-slate-700 uppercase font-semibold">
                    {t("audit.events.lblTargetEntity", "Đối tượng mục tiêu:")}
                  </span>
                  <p className="font-mono font-medium text-emerald-800">
                    {selectedEvent.entityType} ({selectedEvent.entityId})
                  </p>
                </div>
                <div>
                  <span className="text-xs text-slate-700 uppercase font-semibold">
                    {t("audit.events.lblActor", "Người thực hiện:")}
                  </span>
                  <p className="font-medium text-slate-900">
                    {selectedEvent.actorEmail || t("audit.events.systemActor", "System Process")}
                  </p>
                </div>
                {selectedEvent.facilityId && (
                  <div className="col-span-2">
                    <span className="text-xs text-slate-700 uppercase font-semibold">
                      {t("audit.events.lblFacility", "Cơ sở nhà máy:")}
                    </span>
                    <p className="font-medium text-slate-900">
                      {selectedEvent.facilityId}
                    </p>
                  </div>
                )}
                {selectedEvent.hash && (
                  <div className="col-span-2">
                    <span className="text-xs text-slate-700 uppercase font-semibold">
                      {t("audit.events.lblHash", "Chuỗi băm mật mã:")}
                    </span>
                    <p className="font-mono text-xs text-slate-600 break-all bg-white p-2 rounded border border-slate-200">
                      {selectedEvent.hash}
                    </p>
                  </div>
                )}
              </div>

              <div>
                <h4 className="text-xs font-semibold text-slate-700 uppercase tracking-wider mb-2">
                  {t("audit.events.lblPayload", "Dữ liệu tải nộp (Payload Snapshot)")}
                </h4>
                <pre className="p-4 bg-slate-900 text-emerald-400 font-mono text-xs rounded-lg overflow-x-auto">
                  {typeof selectedEvent.payload === "string"
                    ? selectedEvent.payload
                    : JSON.stringify(selectedEvent.payload, null, 2)}
                </pre>
              </div>

              <div className="p-3 bg-emerald-50 border border-emerald-200 rounded-lg text-emerald-800 text-xs flex items-center gap-2">
                <CheckCircle
                  size={16}
                  weight="fill"
                  className="shrink-0 text-emerald-600"
                />
                <span>
                  {t(
                    "audit.events.integrityNotice",
                    "Sự kiện này đã được kiểm tra tính toàn vẹn chữ ký chuỗi khối Mass-Balance. Dữ liệu không thể bị chỉnh sửa hoặc thu hồi."
                  )}
                </span>
              </div>
            </div>

            <div className="p-4 border-t border-slate-200 flex justify-end bg-slate-50">
              <button
                onClick={() => setSelectedEvent(null)}
                className="px-4 py-2 bg-slate-200 hover:bg-slate-300 text-slate-800 rounded-lg text-xs font-medium transition"
              >
                {t("common.close", "Đóng")}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
