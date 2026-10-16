# Lot traceability — bước A

Trạng thái: API/UI truy xuất lô, công đoạn và nguồn đã ghi nhận. Phạm vi phụ thuộc và kết quả kiểm chứng nằm trong [implementation plan](traceability-implementation-plan.md). Lab, plan, reservation, loading và container chưa nằm trong response.

## Request

`POST /api/ledger/trace`

```json
{
  "lotId": "00000000-0000-0000-0000-000000000001",
  "direction": "backward",
  "view": "current"
}
```

UUID trong ví dụ chỉ minh họa. `lotId` là Ledger lot UUID đã nhận, không phải Capture SourceLot.Id; không nhận owner/actor từ client. `direction` mặc định `forward`, chỉ nhận `forward`/`backward`; `view` mặc định `current`, chỉ nhận `current`/`history`. Invalid/null/empty input bị từ chối. Có thể bắt đầu từ lô đã dùng hết.

POST vì truy vấn thành công lưu audit `TRACEBACK`. Cookie request cần `X-CSRF-TOKEN`; Bearer dùng hợp đồng auth hiện có. Response có `Cache-Control: no-store`. Không gọi từ mount effect: người dùng bấm Run trace, mỗi lần chạy là snapshot/audit mới.

## Quyền

| Vai | Forward affected targets | Backward/source composition | Scope |
| --- | --- | --- | --- |
| CustomerManager | Có | Có | Company hiện hành của actor |
| ExportStaff | Có | Không | Company hiện hành; `contributions=null`, `sources=[]` |
| Administrator | Có | Có | Global trace grant riêng; không mở stock/HOLD/processing wildcard |
| MillManager/MillOperator/PurchasingStaff/pending | Không | Không | Không cấp trace chỉ vì xem được stock/history |

Kiểm tra role/permission từ trạng thái persisted, đồng thời kiểm tra owner trong protected routine. Sau enrichment có kiểm tra lại authority trước commit. Mã nguồn không cấp một quyền business mới cho Administrator ngoài `trace.read.all` đã có.

## Response

- `traceId`: UUID event audit; dùng làm reference của kết quả.
- `targetLotId`, `ownerId`, `direction`, `view`.
- `asOf`, `snapshot`: mốc câu truy vấn graph và PostgreSQL snapshot basis; đây không phải token để yêu cầu một lần đọc as-of tùy ý.
- `complete`, `lotCount`, `edgeCount`: `complete=true` chỉ khi toàn bộ tập trong scope đã xử lý thành công.
- `lots[]`: `id`, `code`, `stage`, `operationId`, `producedKg`, `positions[]`, `restrictions[]`, `contributions[]` hoặc null.
- `positions[]`: `locationId`, `facilityId`, `quantityKg`, `availableKg` tại snapshot graph. Không lọc bỏ lô có số dư bằng 0.
- `contributions[]`: `sourceLotId` là **Ledger source-lot UUID**, `quantityKg`, `denominatorKg`, `percentage`. Giữ cả dòng được phân bổ bằng 0.
- `edges[]`: `operationId`, `inputLotId`, `outputLotId`, `inputConsumedKg`, `outputProducedKg`. Input cùng lô ở nhiều vị trí được nhóm kg. Hai lượng này là lượng công đoạn, **không phải kg attribution trực tiếp trên cạnh**. Một output xuất hiện trên nhiều cạnh không có nghĩa được sản xuất nhiều lần.
- `operations[]`: `id`, `kind`, `status`, `occurredAt`, `declaredLossKg`, `varianceKg`.
- `corrections[]`: `id`, `originalOperationId`, `replacementOperationId`, `status`; chỉ là liên kết kế toán, không tạo cạnh sản xuất đảo.
- `sources[]`: `lotId`, `status` và `provenance`. Status `RECORDED` có source record/procurement/item, farmer, plot, season, plot-season IDs, rice variety và giá trị/đơn vị/kg kỳ vọng gốc; `LEGACY_SOURCE_UNAVAILABLE` có provenance null.

Khối lượng là decimal string, kg giữ precision của dữ liệu đã ghi. `producedKg` là tổng attribution lúc tạo lô; không cộng qua các tầng để ra tổng tồn/ảnh hưởng. `percentage` được backend làm tròn sáu chữ số để hiển thị; số liệu đối chiếu chính là `quantityKg / denominatorKg`. Không dùng JavaScript float cho khối lượng. Không suy dư lượng hoặc compliance verdict từ tỷ lệ.

Source metadata được Capture cung cấp theo tập từ immutable procurement event snapshot, cùng connection/transaction do caller quản lý. Không đọc mutable master-data labels để ghi đè nguồn lịch sử, không xuất contacts/geometry/evidence files. Source expectation vẫn giữ nguyên ngay cả khi accepted shortage tạo stock theo actual mass khác kỳ vọng.

## Hành vi đồ thị

- `forward`: đi theo công đoạn sử dụng chính lô đang xét, không mở rộng qua các lô chỉ có chung root source.
- `backward`: đi về inputs của công đoạn tạo lô, không lấy mọi sibling output.
- `current`: chỉ dùng operation COMMITTED; operation CORRECTED không tạo cạnh hiện hành. Khi bắt đầu từ lô đã superseded, lô đó vẫn được trả và có restriction; source attribution của chính lô vẫn là lịch sử tạo ra nó, không tự gán sang replacement.
- `history`: dùng cả COMMITTED/CORRECTED; hiện trạng stock và restrictions vẫn là tại snapshot hiện tại. Đây **không phải** historical inventory balance tại một thời điểm quá khứ.
- Transfer giữ lot ID, không thêm production edge; positions thể hiện vị trí/số dư.
- Không tự đưa replacement thành hậu duệ vật lý của original; xem `corrections` để hiểu thay thế.
- Source đang pending arrival chưa có Ledger lot: bị từ chối như một identity chưa khả dụng trong scope. Không tạo stock, không tự cân/nhận hàng.

Graph, positions, restrictions và attribution được đọc theo tập trong một câu SQL ở READ COMMITTED. Audit cùng transaction với graph và source enrichment; enrichment lỗi thì không commit success audit. Không dùng global stock-write lock hoặc response cache. Kết quả không phải snapshot ổn định qua nhiều lần chạy; cần chạy lại khi stock/evidence thay đổi.

## Lỗi và giới hạn

- `400`: input/direction/view không hợp lệ; cookie thiếu CSRF cũng theo middleware hiện tại.
- `401`: thiếu/expired/revoked authentication.
- `403`: thiếu quyền, sai use case/owner hoặc lot không tồn tại/chưa nhận; không phân biệt foreign ID với missing ID.
- `409`: quá 5.000 reachable lots hoặc 20.000 edges. Không trả partial graph hoặc commit success audit.
- `503`: query không hoàn tất trong budget. Server statement timeout 25 giây, client command timeout 30 giây; đây là guard tài nguyên, chưa phải chứng minh NFR 30 giây toàn mùa vụ.

API trả toàn bộ graph trong giới hạn; UI phân trang cục bộ phần trình bày (25 dòng) sau khi nhận tập hoàn chỉnh. Không lấy pagination inventory làm giới hạn traversal. Version đầu chưa có export/batch traversal lớn hơn guard.

## Deployment

Forward migration Ledger `20261017000000_AddLotTraceability` thêm `ledger.trace_lot(uuid,uuid,text,text)`. Runtime chỉ được EXECUTE entry routine; các grant được cập nhật trong `LedgerDatabase.ConfigureRuntimeRoleAsync` khi chạy migration workflow hiện có. Không sửa migration cũ hay grant PUBLIC. Trước khi dùng trên shared Supabase, owner cần phê duyệt và thực hiện migration/runtime grant theo quy trình repository; normal startup không tự migrate.

Kiểm thử và browser smoke của đợt phát triển dùng database Testcontainers cô lập. Sau yêu cầu trực tiếp của người dùng, migration đã được áp dụng vào database được cấu hình, cùng runtime EXECUTE grant; postflight xác nhận không còn pending migration và runtime validation pass. Không chạy seed hoặc data-changing smoke trên shared DB. Chi tiết tại mục 6.4 của [implementation plan](traceability-implementation-plan.md).
