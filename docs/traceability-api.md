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

Source metadata được Capture cung cấp theo tập từ immutable procurement event snapshot, cùng connection/transaction do caller quản lý. Không đọc mutable master-data labels để ghi đè nguồn lịch sử, không xuất contacts/geometry/evidence files. `provenance.currentReferenceLabels` bổ sung `farmerCode`, `farmerName`, `plotCode`, `seasonName` (nullable), tra theo ID trong snapshot và cùng owner, kể cả reference đã soft-delete. Các nhãn này là giá trị hiện tại khi enrichment, không phải historical snapshot và không thay thế các ID nguồn đã ghi nhận. Source expectation vẫn giữ nguyên ngay cả khi accepted shortage tạo stock theo actual mass khác kỳ vọng.

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

## Table / Flowchart trên web

Sau **Run trace**, chọn **Table** hoặc **Flowchart** tại phần Trace result. Flowchart kết hợp danh sách lô và Processing relationships thành sơ đồ **lô đầu vào → công đoạn → lô đầu ra**, dùng đúng snapshot đã tải. Chuyển cách hiển thị, chọn node và zoom không gọi lại trace API hoặc tạo thêm audit.

- Mũi tên luôn theo chiều vật liệu từ trái sang phải. Với Backward, các tổ tiên nằm bên trái lô bắt đầu; không đảo cạnh thành quá trình vật lý ngược.
- Lô bắt đầu có nhãn **Starting lot**, khác với node đang được chọn. **Focus starting lot** cuộn và đặt focus về node đó; có zoom, Fit graph và cuộn khi sơ đồ lớn.
- Chọn một node lô để xem stock/restrictions và phần source composition được API cho phép. Export Staff vẫn không có source composition.
- Mỗi công đoạn chỉ có một node dùng chung cho các nhánh vào/ra. Quan hệ chỉ hiển thị trong phạm vi trace được trả; một công đoạn có thể còn inputs/outputs khác nằm ngoài hướng truy hiện tại. Các kg input/output xem trong **Operation quantities and exact relationships**, giữ nguyên decimal string, không cộng lặp các cặp input–output.
- Correction references vẫn tách riêng, không dựng cạnh vật liệu ngược hay cạnh tự động original → replacement. Bản CORRECTED trong History giữ đúng status trên node công đoạn.
- Để giữ thao tác tương tác gọn, Flowchart hỗ trợ tối đa **250 node lô/công đoạn và 500 quan hệ API**. Quá giới hạn thì thông báo rõ và cho **Show table**; không vẽ một phần rồi coi là đầy đủ. Giới hạn API 5.000 lô/20.000 cạnh vẫn giữ nguyên.
- Table hiện xếp lô bắt đầu trước, sau đó theo từng tầng truy xuất và mã lô trong cùng nhánh. Thứ tự hiển thị không còn phụ thuộc UUID và không thay đổi dữ liệu API.

Flowchart được vẽ bằng HTML/SVG với React đang có; không thêm dependency, không cần migration mới và không thay đổi authorization/accounting backend.

## Chi tiết nguồn ruộng/vụ

Trong **Source composition** của lô đang chọn, mở **View field / season origin** ở từng nguồn đóng góp. Có thể mở từ cả Table và Flowchart; không fetch lại, không tạo audit mới. Phần này hiển thị nông dân → thửa/vụ (giống đã ghi nhận) → phiếu và dòng thu mua → source Ledger lot, kèm allocated kg/tỷ lệ của lô đang chọn.

Đây là chuỗi nguồn gốc, không phải công đoạn chuyển đổi vật liệu hoặc lịch sử đăng ký thửa. Source expectation được ghi riêng với allocated kg, không coi là arrival measurement. Tên/mã danh mục được ghi rõ là current reference labels; khi thiếu nhãn vẫn hiện ID bất biến và báo unavailable, không suy diễn dữ liệu. Legacy không có hồ sơ nguồn vẫn báo thiếu. Chỉ Customer Manager đúng owner và Administrator theo quyền trace hiện có được đọc; Export Staff không được bổ sung dữ liệu nguồn.

Không cần migration hoặc grant mới. Capture đọc metadata theo tập trên connection/transaction hiện có; UI không gọi lần lượt API nông dân/thửa/vụ.

## Deployment

Forward migration Ledger `20261017000000_AddLotTraceability` thêm `ledger.trace_lot(uuid,uuid,text,text)`. Runtime chỉ được EXECUTE entry routine; các grant được cập nhật trong `LedgerDatabase.ConfigureRuntimeRoleAsync` khi chạy migration workflow hiện có. Không sửa migration cũ hay grant PUBLIC. Trước khi dùng trên shared Supabase, owner cần phê duyệt và thực hiện migration/runtime grant theo quy trình repository; normal startup không tự migrate.

Kiểm thử và browser smoke của đợt phát triển dùng database Testcontainers cô lập. Sau yêu cầu trực tiếp của người dùng, migration đã được áp dụng vào database được cấu hình, cùng runtime EXECUTE grant; postflight xác nhận không còn pending migration và runtime validation pass. Không chạy seed hoặc data-changing smoke trên shared DB. Chi tiết tại mục 6.4 của [implementation plan](traceability-implementation-plan.md).

## Chẩn đoán `permission denied for function trace_lot`

Đây là thiếu quyền PostgreSQL `EXECUTE` của backend runtime login, khác với lỗi business role/owner bên trong routine. Customer Manager vẫn có quyền trace theo owner; đổi role của tài khoản người dùng không khắc phục lỗi database này.

Kiểm tra chỉ đọc bằng runtime connection:

```sql
SELECT to_regprocedure('ledger.trace_lot(uuid,uuid,text,text)') IS NOT NULL AS routine_exists,
       has_function_privilege(current_user, 'ledger.trace_lot(uuid,uuid,text,text)', 'EXECUTE') AS can_execute;
```

Nếu routine tồn tại và migration trace đã áp dụng nhưng `can_execute=false`, owner có thể cấp riêng EXECUTE trên đúng signature cho runtime role đang cấu hình, sau phê duyệt của database owner. Không cần seed hoặc sửa stock. Workflow `--migrate` hiện có cũng cấu hình lại toàn bộ runtime grants và có thể áp dụng migration còn thiếu; không dùng nó như lệnh chẩn đoán chỉ đọc.

`ValidateRuntimeAsync` nay kiểm tra routine trace tồn tại và runtime có EXECUTE khi khởi động. Không tự cấp quyền hay dùng migration credential để chạy ứng dụng. Kiểm tra tại database cấu hình trong phiên này xác nhận routine/migration tồn tại, runtime login khớp `Ledger:RuntimeRole`, nhưng EXECUTE trace bị thiếu trong khi EXECUTE `ledger.command` vẫn có; chưa xác định tác nhân đã đổi ACL.
