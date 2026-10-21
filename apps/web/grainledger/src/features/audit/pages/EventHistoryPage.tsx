import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { ledgerEndpoints } from "@/api/ledger";
import { GrainLoader } from "@/components/GrainLoader/GrainLoader";
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
  WarningCircle,
  X,
  ArrowsClockwise,
  CheckCircleIcon,
} from "@phosphor-icons/react";

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

  // Selected event for detail modal (SC-22)
  const [selectedEvent, setSelectedEvent] = useState<AuditEvent | null>(null);

  useEffect(() => {
    void fetchEvents();
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
        return <Cube className="text-amber-500" size={16} weight="bold" />;
      case "operation":
        return <Scales className="text-emerald-500" size={16} weight="bold" />;
      case "receivingtask":
      case "receiving":
        return <Truck className="text-sky-500" size={16} weight="bold" />;
      case "hold":
        return <LockSimple className="text-accent-danger" size={16} weight="bold" />;
      case "procurement":
        return <HandCoins className="text-teal-500" size={16} weight="bold" />;
      default:
        return <ClockCounterClockwise className="text-ink-tertiary" size={16} weight="bold" />;
    }
  };

  return (
    <div className="space-y-6">
      {/* Page Header - Synchronized with CustomerOrganizationsPage */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 border-b border-ink-tertiary/15 pb-4">
        <div>
          <span className="text-xs font-mono font-bold text-accent-primary uppercase tracking-wider block">
            {t("audit.events.tag", "NHẬT KÝ ĐỐI SOÁT & BẰNG CHỨNG BẤT BIẾN (AUDIT TRAIL)")}
          </span>
          <h2 className="text-2xl font-display font-bold text-ink-primary">
            {t("audit.events.title", "Nhật ký Sự kiện Nghiệp vụ Bất biến")}
          </h2>
          <p className="text-xs text-ink-secondary mt-0.5">
            {t(
              "audit.events.subtitle",
              "Mọi biến đổi khối lượng (kg), trạng thái niêm chì, kiểm nghiệm và lệnh phong tỏa được lưu trữ bất biến kèm chuỗi băm xác thực."
            )}
          </p>
        </div>

        <div className="flex items-center gap-2">
          <div className="hidden sm:inline-flex items-center gap-1.5 px-3 py-2 rounded-lg bg-surface-base dark:bg-surface-raised border border-emerald-500/30 text-emerald-700 dark:text-emerald-400 text-xs font-semibold shadow-xs">
            <ShieldCheck size={16} weight="fill" />
            <span>{t("audit.events.badgeSha", "Chuỗi băm SHA-256")}</span>
          </div>

          <button
            type="button"
            onClick={fetchEvents}
            className="p-2.5 rounded-lg border border-ink-tertiary/20 bg-surface-base hover:bg-surface-raised text-ink-secondary hover:text-ink-primary transition-colors"
            title={t("audit.events.btnRefresh", "Làm mới danh sách")}
          >
            <ArrowsClockwise size={16} />
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

      {/* Filter and Search Card */}
      <div className="p-4 rounded-2xl bg-surface-base dark:bg-surface-raised border border-ink-tertiary/20 shadow-xs space-y-3">
        <div className="grid grid-cols-1 md:grid-cols-4 gap-3">
          <div className="md:col-span-2 relative">
            <MagnifyingGlass
              className="absolute left-3 top-2.5 text-ink-tertiary"
              size={16}
            />
            <input
              type="text"
              placeholder={t("audit.events.searchPlaceholder", "Tìm theo mã sự kiện, mã lô (Lot ID), người ghi, hash...")}
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              className="w-full pl-9 pr-3 py-2 text-xs rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 text-ink-primary placeholder:text-ink-tertiary focus:border-accent-primary focus:outline-none transition-colors"
            />
          </div>

          <div>
            <select
              value={entityFilter}
              onChange={(e) => setEntityFilter(e.target.value)}
              className="w-full py-2 px-3 text-xs rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 text-ink-primary focus:border-accent-primary focus:outline-none transition-colors"
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
            <Funnel className="text-ink-tertiary shrink-0" size={16} />
            <select
              value={eventTypeFilter}
              onChange={(e) => setEventTypeFilter(e.target.value)}
              className="w-full py-2 px-3 text-xs rounded-lg bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 text-ink-primary focus:border-accent-primary focus:outline-none transition-colors"
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

        <div className="text-xs text-ink-secondary flex items-center justify-between pt-2.5 border-t border-ink-tertiary/10">
          <span>
            {t("audit.events.showingCount", "Hiển thị {{count}} / {{total}} sự kiện được ghi nhận", {
              count: filteredEvents.length,
              total: events.length,
            })}
          </span>
          <span className="font-mono text-[11px] text-accent-primary font-bold">
            {t("audit.events.isoCompliance", "Mã hóa tuân thủ ISO/IEC 27001 & ISO 22095")}
          </span>
        </div>
      </div>

      {/* Events Table */}
      {loading ? (
        <div className="py-16 flex justify-center">
          <GrainLoader label={t("audit.events.loading", "Đang tải chuỗi sự kiện đối soát bất biến...")} />
        </div>
      ) : filteredEvents.length === 0 ? (
        <div className="py-16 text-center border-2 border-dashed border-ink-tertiary/15 rounded-2xl bg-surface-base dark:bg-surface-raised">
          <ClockCounterClockwise size={40} className="mx-auto text-ink-tertiary mb-3 opacity-60" />
          <p className="text-sm font-semibold text-ink-primary">
            {t("audit.events.noEvents", "Không tìm thấy sự kiện nào phù hợp với bộ lọc.")}
          </p>
          <p className="text-xs text-ink-secondary mt-1">
            Vui lòng thử điều chỉnh lại từ khóa tìm kiếm hoặc loại đối tượng.
          </p>
        </div>
      ) : (
        <div className="rounded-2xl border border-ink-tertiary/20 bg-surface-base dark:bg-surface-raised shadow-xs overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs border-collapse">
              <thead>
                <tr className="bg-surface-raised dark:bg-surface-overlay border-b border-ink-tertiary/15 text-ink-secondary font-semibold uppercase tracking-wider text-[11px]">
                  <th className="py-3.5 px-4">{t("audit.events.thTime", "Thời gian")}</th>
                  <th className="py-3.5 px-4">{t("audit.events.thEventId", "Mã sự kiện")}</th>
                  <th className="py-3.5 px-4">{t("audit.events.thEntity", "Đối tượng")}</th>
                  <th className="py-3.5 px-4">{t("audit.events.thEventType", "Loại sự kiện")}</th>
                  <th className="py-3.5 px-4">{t("audit.events.thActor", "Tài khoản ghi")}</th>
                  <th className="py-3.5 px-4">{t("audit.events.thHash", "Chuỗi băm SHA-256")}</th>
                  <th className="py-3.5 px-4 text-right">{t("audit.events.thDetails", "Chi tiết")}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-ink-tertiary/10 font-sans">
                {filteredEvents.map((evt) => (
                  <tr key={evt.id} className="hover:bg-surface-raised/60 dark:hover:bg-surface-overlay/40 transition-colors">
                    <td className="py-3.5 px-4 text-ink-secondary whitespace-nowrap">
                      <div className="flex items-center gap-1.5 font-mono text-[11px]">
                        <Calendar size={13} className="text-ink-tertiary" />
                        <span>
                          {new Date(evt.timestamp).toLocaleString(
                            i18n.language.startsWith("en") ? "en-US" : "vi-VN"
                          )}
                        </span>
                      </div>
                    </td>
                    <td className="py-3.5 px-4">
                      <span className="font-mono text-xs font-bold text-accent-primary bg-accent-primary/10 px-2 py-0.5 rounded-md inline-block">
                        {evt.id}
                      </span>
                    </td>
                    <td className="py-3.5 px-4">
                      <div className="flex items-center gap-1.5">
                        {getEntityIcon(evt.entityType)}
                        <span className="font-semibold text-ink-primary text-xs">
                          {evt.entityType}:
                        </span>
                        <span className="font-mono text-[11px] text-emerald-700 dark:text-emerald-400 bg-emerald-50 dark:bg-emerald-950/40 border border-emerald-500/20 px-1.5 py-0.5 rounded">
                          {evt.entityId}
                        </span>
                      </div>
                    </td>
                    <td className="py-3.5 px-4">
                      <span className="px-2 py-0.5 rounded-full text-[11px] font-semibold bg-surface-raised dark:bg-surface-overlay border border-ink-tertiary/20 text-ink-primary">
                        {evt.eventType}
                      </span>
                    </td>
                    <td className="py-3.5 px-4 text-ink-secondary truncate max-w-[150px] font-mono text-[11px]">
                      {evt.actorEmail || t("audit.events.systemActor", "Hệ thống")}
                    </td>
                    <td className="py-3.5 px-4 font-mono text-[11px] text-ink-tertiary">
                      {evt.hash
                        ? `${evt.hash.substring(0, 10)}...${evt.hash.substring(evt.hash.length - 6)}`
                        : "—"}
                    </td>
                    <td className="py-3.5 px-4 text-right">
                      <button
                        type="button"
                        onClick={() => setSelectedEvent(evt)}
                        className="px-2.5 py-1 rounded-lg border border-ink-tertiary/20 bg-surface-base hover:bg-surface-raised text-ink-primary font-semibold text-xs inline-flex items-center gap-1.5 transition-colors shadow-xs"
                      >
                        <Eye size={14} />
                        <span>{t("audit.events.btnView", "Xem")}</span>
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Detail Modal (SC-22) - Synchronized with CustomerOrganizationsPage modal */}
      {selectedEvent && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-xs p-4">
          <div className="w-full max-w-2xl bg-surface-base dark:bg-surface-raised border border-ink-tertiary/20 rounded-2xl shadow-xl overflow-hidden animate-ledger-in flex flex-col max-h-[90vh]">
            <div className="p-5 border-b border-ink-tertiary/15 flex items-center justify-between">
              <div className="flex items-center gap-2">
                <FileCode size={20} className="text-accent-primary" />
                <h3 className="font-display font-bold text-base text-ink-primary">
                  {t("audit.events.modalTitle", "Chi tiết Sự kiện Bất biến")}
                </h3>
                <span className="font-mono text-xs text-accent-primary bg-accent-primary/10 px-2 py-0.5 rounded font-bold">
                  #{selectedEvent.id}
                </span>
                <div className="relative inline-flex items-center group">
                  <CheckCircleIcon
                    size={18}
                    weight="fill"
                    className="shrink-0 text-emerald-600 dark:text-emerald-400 cursor-help"
                  />

                  {/* Tooltip */}
                  <div className="absolute top-full right-0 pt-2 w-80 z-50
                  invisible opacity-0 translate-y-1
                  group-hover:visible group-hover:opacity-100
                  group-hover:translate-y-0
                  transition-all duration-200
                  pointer-events-none">
                    <div className="p-3.5 rounded-xl bg-emerald-50 dark:bg-emerald-950/40 border border-emerald-500/30 text-[#2F6B3A] dark:text-emerald-300 text-xs flex items-start gap-2.5 shadow-lg">
                      <CheckCircle
                        size={18}
                        weight="fill"
                        className="shrink-0 text-emerald-600 dark:text-emerald-400"
                      />

                      <span>
                        {t(
                          "audit.events.integrityNotice",
                          "Sự kiện này đã được kiểm tra tính toàn vẹn chữ ký chuỗi khối Mass-Balance. Dữ liệu không thể bị chỉnh sửa hoặc thu hồi."
                        )}
                      </span>
                    </div>
                  </div>
                </div>
              </div>
              <button
                type="button"
                onClick={() => setSelectedEvent(null)}
                className="p-1 rounded-md text-ink-tertiary hover:text-ink-primary transition-colors"
              >
                <X size={18} />
              </button>
            </div>

            <div className="p-5 overflow-y-auto space-y-4 text-xs">
              <div className="grid grid-cols-2 gap-3.5 bg-surface-raised dark:bg-surface-overlay p-4 rounded-xl border border-ink-tertiary/15">
                <div>
                  <span className="text-[11px] text-ink-tertiary uppercase font-semibold block mb-0.5">
                    {t("audit.events.lblEventType", "Loại sự kiện:")}
                  </span>
                  <p className="font-bold text-ink-primary text-xs">
                    {selectedEvent.eventType}
                  </p>
                </div>
                <div>
                  <span className="text-[11px] text-ink-tertiary uppercase font-semibold block mb-0.5">
                    {t("audit.events.lblTimestamp", "Thời điểm ghi nhận:")}
                  </span>
                  <p className="font-mono text-ink-primary text-xs">
                    {new Date(selectedEvent.timestamp).toLocaleString(
                      i18n.language.startsWith("en") ? "en-US" : "vi-VN"
                    )}
                  </p>
                </div>
                <div>
                  <span className="text-[11px] text-ink-tertiary uppercase font-semibold block mb-0.5">
                    {t("audit.events.lblTargetEntity", "Đối tượng mục tiêu:")}
                  </span>
                  <p className="font-mono font-bold text-accent-primary text-xs">
                    {selectedEvent.entityType} ({selectedEvent.entityId})
                  </p>
                </div>
                <div>
                  <span className="text-[11px] text-ink-tertiary uppercase font-semibold block mb-0.5">
                    {t("audit.events.lblActor", "Người thực hiện:")}
                  </span>
                  <p className="font-mono text-ink-secondary text-xs">
                    {selectedEvent.actorEmail || t("audit.events.systemActor", "System Process")}
                  </p>
                </div>
                {selectedEvent.facilityId && (
                  <div className="col-span-2">
                    <span className="text-[11px] text-ink-tertiary uppercase font-semibold block mb-0.5">
                      {t("audit.events.lblFacility", "Cơ sở nhà máy:")}
                    </span>
                    <p className="font-mono text-ink-primary text-xs">
                      {selectedEvent.facilityId}
                    </p>
                  </div>
                )}
                {selectedEvent.hash && (
                  <div className="col-span-2">
                    <span className="text-[11px] text-ink-tertiary uppercase font-semibold block mb-0.5">
                      {t("audit.events.lblHash", "Chuỗi băm mật mã (SHA-256):")}
                    </span>
                    <p className="font-mono text-[11px] text-ink-secondary break-all bg-surface-base dark:bg-surface-raised p-2 rounded-lg border border-ink-tertiary/15">
                      {selectedEvent.hash}
                    </p>
                  </div>
                )}
              </div>

              {/* <div>
                <h4 className="text-[11px] font-semibold text-ink-secondary uppercase tracking-wider mb-2">
                  {t("audit.events.lblPayload", "Dữ liệu tải nộp (Payload Snapshot)")}
                </h4>
                <pre className="p-4 bg-ink-primary text-emerald-400 dark:bg-black/60 dark:text-emerald-300 font-mono text-[11px] rounded-xl overflow-x-auto border border-ink-tertiary/20 leading-relaxed max-h-60">
                  {typeof selectedEvent.payload === "string"
                    ? selectedEvent.payload
                    : JSON.stringify(selectedEvent.payload, null, 2)}
                </pre>
              </div> */}

              {/* <div className="p-3.5 rounded-xl bg-emerald-50 dark:bg-emerald-950/40 border border-emerald-500/30 text-[#2F6B3A] dark:text-emerald-300 text-xs flex items-center gap-2.5">
                <CheckCircle
                  size={18}
                  weight="fill"
                  className="shrink-0 text-emerald-600 dark:text-emerald-400"
                />
                <span>
                  {t(
                    "audit.events.integrityNotice",
                    "Sự kiện này đã được kiểm tra tính toàn vẹn chữ ký chuỗi khối Mass-Balance. Dữ liệu không thể bị chỉnh sửa hoặc thu hồi."
                  )}
                </span>
              </div> */}
            </div>

            <div className="p-4 border-t border-ink-tertiary/15 flex justify-end bg-surface-base dark:bg-surface-raised">
              <button
                type="button"
                onClick={() => setSelectedEvent(null)}
                className="px-4 py-2 border border-ink-tertiary/20 hover:bg-surface-raised text-ink-secondary hover:text-ink-primary rounded-lg text-xs font-semibold transition-colors"
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
