# Giải thích 61 bảng PostgreSQL hiện hành

Nguồn: catalog PostgreSQL live chụp ngày 19/10/2026; sơ đồ sở hữu và [README](./README.md). Danh sách gồm 60 bảng nghiệp vụ/auth và bảng kỹ thuật `ledger.__EFMigrationsHistory`. Ý nghĩa dưới đây mô tả vai trò thể hiện bởi cấu trúc hiện có, không khẳng định thêm workflow hoặc validation ngoài schema.

## Identity

| Bảng | Vai trò theo dữ liệu lưu |
| --- | --- |
| `identity.audit_events` | Nhật ký thao tác Identity: actor, subject, action, chi tiết JSON, thời điểm và IP. |
| `identity.organizations` | Tổ chức với tên, loại và thời điểm tạo. |
| `identity.password_reset_codes` | Mã reset đã băm, số lần thử, hạn dùng, gửi gần nhất và thời điểm tiêu thụ cho user. |
| `identity.registration_attempts` | Thông tin đăng ký tạm gồm email, tên hiển thị, password/code hash, số lần thử và các mốc hiệu lực/tiêu thụ. |
| `identity.sessions` | Phiên user, thời hạn/thu hồi, refresh-token hash, session thay thế, IP và user agent. |
| `identity.users` | Tài khoản, định danh provider, email/tên/liên hệ, role, organization và trạng thái đăng nhập. |

## Capture

| Bảng | Vai trò theo dữ liệu lưu |
| --- | --- |
| `capture.business_events` | Event nghiệp vụ với công ty/actor, loại, thời gian, correlation ID và payload. |
| `capture.code_counters` | Bộ đếm mã theo company, kind và scope. |
| `capture.crop_seasons` | Vụ mùa theo company, tên, ngày bắt đầu/kết thúc và trạng thái xóa. |
| `capture.farmers` | Hồ sơ nông hộ: mã, tên, liên hệ, người tạo và trạng thái. |
| `capture.offline_operations` | Thao tác offline đã gửi, payload/hash, thời điểm thiết bị, trạng thái áp dụng và kết quả/xung đột. |
| `capture.plot_seasons` | Gắn thửa với vụ mùa và giá trị giống lúa được lưu cho liên kết đó. |
| `capture.plots` | Thửa theo nông hộ, mã, ranh giới GeoJSON và diện tích. |
| `capture.procurement_items` | Dòng thu mua: số dòng, thửa-vụ, khối lượng tịnh, số đo/đơn vị và độ ẩm. |
| `capture.procurements` | Phiếu thu mua: mã, nông hộ, điểm nhận, người/thời điểm nhận, trạng thái và event. |
| `capture.sample_handovers` | Bản ghi bàn giao mẫu: bên giao/nhận, người ghi, thời gian, tình trạng seal và tham chiếu bằng chứng. |
| `capture.samples` | Mẫu có mã, source lot, seal, người lấy và thời gian lấy. |
| `capture.source_lots` | Liên kết lot nguồn với dòng thu mua đã tạo ra lot đó. |
| `capture.spray_declarations` | Khai báo phun gắn thửa-vụ, ngày phun, người ghi và bằng chứng. |
| `capture.spray_items` | Sản phẩm/hoạt chất trong một khai báo phun; có các cột rate và rate_unit nullable theo catalog hiện hành. |

## Catalog

| Bảng | Vai trò theo dữ liệu lưu |
| --- | --- |
| `catalog.active_substances` | Mã và tên hoạt chất. |
| `catalog.catalog_events` | Nhật ký thay đổi danh mục với actor, resource, thời gian, correlation ID và trạng thái trước/sau. |
| `catalog.commodities` | Danh mục commodity, mã/tên, người tạo và cờ archive. |
| `catalog.markets` | Danh mục thị trường bằng mã và tên. |
| `catalog.measurement_units` | Đơn vị đo với mã, tên, loại đại lượng, người tạo và cờ xóa. |
| `catalog.mrl_limits` | Giá trị/đơn vị giới hạn cho một hoạt chất trong một phiên bản MRL. |
| `catalog.mrl_versions` | Phiên bản MRL theo market/commodity, trạng thái, ngày hiệu lực, người/ngày công bố và nguồn. |
| `catalog.quality_attributes` | Danh mục thuộc tính chất lượng, mã/tên và unit dạng lưu trữ. |
| `catalog.rice_varieties` | Danh mục giống lúa bằng mã và tên. |

## Ledger

| Bảng | Vai trò theo dữ liệu lưu |
| --- | --- |
| `ledger.__EFMigrationsHistory` | Lịch sử migration EF Core: MigrationId và ProductVersion; bảng kỹ thuật, không phải bảng nghiệp vụ. |
| `ledger.arrival_measurement_sets` | Bộ số đo lúc nhận: procurement, operation, operator, thời điểm, observations, version và threshold đã chọn. |
| `ledger.arrival_measurements` | Các số đo theo procurement item trong measurement set, gồm số đo/đơn vị và actual kg. |
| `ledger.arrival_operations` | Payload và kết quả thao tác receiving theo procurement, actor, loại và thời điểm. |
| `ledger.arrival_reviews` | Review receiving theo procurement/measurement set: hành động, lý do, evidence, manager và version. |
| `ledger.assignments` | Gán user vào facility, kèm người tạo, version và cờ xóa. |
| `ledger.catalog_commodities` | Ánh xạ giữa commodity catalog và commodity nội bộ ledger. |
| `ledger.commodities` | Commodity ledger có tên. |
| `ledger.compatibilities` | Quy tắc tương thích đầu vào/đầu ra theo facility, ngày hiệu lực và evidence. |
| `ledger.contributions` | Quantity đóng góp từ source lot vào lot khác. |
| `ledger.corrections` | Bản ghi correction cho operation: lý do/trạng thái, evidence, reconciliation và dữ liệu operation thay thế. |
| `ledger.equipment` | Thiết bị thuộc facility với tên, người tạo, version và cờ xóa. |
| `ledger.events` | Event ledger có kind, actor, operation/lot liên quan, details và thời điểm. |
| `ledger.evidence` | Nội dung evidence/file với owner, facility, tên file, MIME type, hash, actor và thời điểm. |
| `ledger.facilities` | Cơ sở với tên, người tạo, version và cờ xóa. |
| `ledger.holds` | Hold lot với lý do, trạng thái, evidence, phản hồi và thời điểm tạo. |
| `ledger.locations` | Vị trí thuộc facility, loại, owner, cờ cleared, người tạo, version và cờ xóa. |
| `ledger.lot_code_reservations` | Mã lot đã cấp trước theo owner/context, stage, product identity và thời điểm. |
| `ledger.lot_codes` | Mã lot và origin lot được gắn với mã đó. |
| `ledger.lots` | Lot với mã/origin, owner, commodity, stage, product identity, receipt/line, operation và thời điểm tạo. |
| `ledger.notifications` | Thông báo gắn user và hold/lot, nội dung, thời điểm tạo/đọc. |
| `ledger.operations` | Operation theo owner/facility: kind, thời điểm, equipment, content, status/version, xác nhận, variance, correction và người tạo. |
| `ledger.policies` | Policy theo facility/kind với tolerance, ngày hiệu lực và evidence. |
| `ledger.positions` | Số lượng của lot tại location. |
| `ledger.postings` | Bút toán số lượng theo event, lot và location; có thể trỏ posting bị đảo. |
| `ledger.receipt_correction_blocks` | Trạng thái block receipt theo correction/receipt/owner. |
| `ledger.receipt_reversals` | Liên kết receipt bị đảo với correction, owner và evidence. |
| `ledger.receiving` | Trạng thái/version receiving cho procurement, measurement set mới nhất/được chấp nhận và review được chấp nhận. |
| `ledger.receiving_blocks` | Discrepancy và cờ block theo lot, evidence, lý do và chênh lệch thực tế. |
| `ledger.receiving_threshold_default` | Cấu hình threshold mặc định dạng singleton, percentage và revision. |
| `ledger.receiving_threshold_history` | Lịch sử revision threshold theo company/scope/action, giá trị cũ/mới, actor, lý do và thời gian. |
| `ledger.receiving_threshold_overrides` | Threshold percentage/revision override theo company. |

## Phân biệt nguồn dữ liệu

`identity.audit_events`, `capture.business_events` và `ledger.events` là ba bảng khác nhau, thuộc ba schema và ba chủ sở hữu khác nhau. `catalog.commodities` khác `ledger.commodities`; `capture.source_lots` khác `ledger.lots`. Chỉ quan hệ FK trong catalog được vẽ thành đường nối; trường JSON, identifier không có FK và kiểm tra runtime không được hiểu là quan hệ vật lý.
