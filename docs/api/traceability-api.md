# Lot traceability — storage and processing

Trạng thái source: API/Table/Flowchart hỗ trợ quan hệ processing và mixed-bulk custody, actual resolved inputs và effective HOLD. Source evidence mở theo yêu cầu qua API Capture riêng, không nằm trong material graph response. Plan/reservation/actual loading/container chưa tích hợp. Kết quả kiểm chứng và phần còn lại nằm ở [plan mục 10](../plans/traceability-implementation-plan.md#10-kết-quả-triển-khai-tiếp--21102026).

Cập nhật 24/10: có selector current pool, phân loại lô, lab-result entry và mở residue theo node. **Cần migration mới `20261024000000_TraceEntryPoints` và runtime-role configuration trước khi chạy backend mới**; đã áp dụng lên database dùng chung ngày 24/10 theo yêu cầu trực tiếp của người dùng; postflight runtime validation và backend health pass (plan mục 13.3). Các ghi nhận deployment bên dưới là lịch sử của những migration trước, không chứng minh bản mới đã được cài.

Deployment cần migration mới `20261021010000_CustodyTraceability` sau mixed-bulk và runtime grants hiện có cho `ledger.trace_lot(uuid,uuid,text,text)`. Không sửa migration cũ; startup không tự apply. Backend/web mới từ chối response trước schemaVersion 2 thay vì hiện graph cũ thiếu custody như kết quả đầy đủ. Sau yêu cầu cập nhật database của người dùng, migration này đã áp dụng lên database dùng chung đang cấu hình và runtime trace EXECUTE đã được khôi phục; postflight schema v2/runtime validation pass. Xem [deployment record](../plans/traceability-implementation-plan.md#105-cập-nhật-database-theo-phê-duyệt--21102026).

## Silo và nguồn thu mua sau dev mới

Nhiều nguồn cùng owner/identity vào location mixed hợp lệ tạo current pool; source lots và pool generations cũ vẫn là provenance, không phải tồn có thể rút thêm. Selector mặc định current pools, vẫn giữ nguồn trước silo và history. Dev residue `9bd3e8d` bổ sung calculation/preview riêng; accounting positive paths của trace không thay chemical dependencies. Xem [plan hiện hành](../plans/traceability-implementation-plan.md#13-plan-thực-thi-mvp-silo-bounds-và-lab-to-trace--24102026).

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

- `schemaVersion=2`, `graphBasis=RECORDED_MATERIAL_RELATIONSHIPS`: complete nói về graph quan hệ đã ghi, không phải tập lô nhiễm/đạt chuẩn.
- `positivePathCoverage`: `PERSISTED_ALLOCATIONS` hoặc `PARTIAL_LEGACY` khi có processing cũ thiếu allocation detail.
- `traceId`: UUID event audit; dùng làm reference của kết quả.
- `targetLotId`, `ownerId`, `direction`, `view`.
- `asOf`, `snapshot`: mốc câu truy vấn graph và PostgreSQL snapshot basis; đây không phải token để yêu cầu một lần đọc as-of tùy ý.
- `complete`, `lotCount`, `edgeCount`: `complete=true` chỉ khi toàn bộ tập trong scope đã xử lý thành công.
- `lots[]`: `id`, `code`, `stage`, `operationId`, `producedKg`, `positions[]`, `restrictions[]`, `contributions[]` hoặc null; thêm `directHold`, `effectiveHold`, `isCurrentPool`, `poolRevision` (revision khi tạo custody lot, có thể null) và `positivePathSourceIds` (null cho Export).
- `positions[]`: `locationId`, `facilityId`, `quantityKg`, `availableKg` tại snapshot graph. Không lọc bỏ lô có số dư bằng 0.
- `contributions[]`: `sourceLotId` là **Ledger source-lot UUID**, `quantityKg`, `denominatorKg`, `percentage`. Giữ cả dòng được phân bổ bằng 0.
- `edges[]`: `relationType` (`PROCESSING`/`CUSTODY`), `relationId` (operation/event UUID), nullable `operationId`, nullable `eventId`, `basis` (`RESOLVED_INPUTS`/`RECORDED_CONTENT`/`CUSTODY_ALLOCATION`), `inputLotId`, `outputLotId`, `inputConsumedKg`, `outputProducedKg`, `inputEventKg`, `sourceAllocations`.
- Processing quantities là input consumed/output produced của công đoạn, không phải kg attribution trên từng pair. Custody `inputConsumedKg` là mass trên cạnh; `outputProducedKg` là tổng lô được tạo; `inputEventKg` là tổng input ấy qua event (bao gồm nhánh không xuất hiện trong hướng truy hiện tại). Graph dùng `inputEventKg` để không đếm một input split hai lần. Không cộng repeated output amounts qua fan-in.
- `sourceAllocations[]`: persisted positive `{ sourceLotId, quantityKg }` theo cạnh. Null khi Export hoặc legacy thiếu detail; empty array khác null và không xóa structural zero relationship. `positivePathSourceIds` mang cùng source identity dọc các cạnh có allocation dương từ target, theo hướng query. Không dùng flattened lot composition để chứng minh đường đi, không dùng graph này để suy nồng độ/HOLD mới. Với `PARTIAL_LEGACY`, không có positive path không phải bằng chứng không có nguồn.
- `custodyEvents[]`: `id`, `action` (`INGRESS`/`WITHDRAW`), `locationId`, `revision`, `occurredAt`, `primaryOutputLotId`. WITHDRAW có thể có aliquot và remainder; remainder không được vẽ thành sấy/xay.
- `operations[]`: `id`, `kind`, `status`, `occurredAt`, `declaredLossKg`, `varianceKg`.
- `corrections[]`: `id`, `originalOperationId`, `replacementOperationId`, `status`; chỉ là liên kết kế toán, không tạo cạnh sản xuất đảo.
- `sources[]`: `lotId`, `status` và `provenance`. Status `RECORDED` có source record/procurement/item, farmer, plot, season, plot-season IDs, rice variety và giá trị/đơn vị/kg kỳ vọng gốc; `LEGACY_SOURCE_UNAVAILABLE` có provenance null.

Khối lượng là decimal string, kg giữ precision của dữ liệu đã ghi. `producedKg` là tổng attribution lúc tạo lô; không cộng qua các tầng để ra tổng tồn/ảnh hưởng. `percentage` được backend làm tròn sáu chữ số để hiển thị; số liệu đối chiếu chính là `quantityKg / denominatorKg`. Không dùng JavaScript float cho khối lượng. Không suy dư lượng hoặc compliance verdict từ tỷ lệ.

Source metadata được Capture cung cấp theo tập từ immutable procurement event snapshot, cùng connection/transaction do caller quản lý. Không đọc mutable master-data labels để ghi đè nguồn lịch sử, không xuất contacts/geometry/evidence files. `provenance.currentReferenceLabels` bổ sung `farmerCode`, `farmerName`, `plotCode`, `seasonName` (nullable), tra theo ID trong snapshot và cùng owner, kể cả reference đã soft-delete. Các nhãn này là giá trị hiện tại khi enrichment, không phải historical snapshot và không thay thế các ID nguồn đã ghi nhận. Source expectation vẫn giữ nguyên ngay cả khi accepted shortage tạo stock theo actual mass khác kỳ vọng.

## Hành vi đồ thị

- `forward`: đi theo recorded processing/custody edges sử dụng chính lô đang xét, không nối hai lô chỉ vì cùng root hoặc cùng kho. Structural descendants có thể chứa nhánh với source share bằng zero; xem persisted source paths riêng, không gọi toàn bộ tập là contamination impact.
- `backward`: đi về actual inputs của processing/custody tạo lô, không lấy mọi sibling output. Processing event có `resolvedInputs` dùng aliquot thực, không vẽ thêm logical pool shortcut. Legacy thiếu event này dùng recorded content và báo coverage tương ứng.
- `current`: chỉ dùng operation COMMITTED; operation CORRECTED không tạo cạnh hiện hành. Khi bắt đầu từ lô đã superseded, lô đó vẫn được trả và có restriction; source attribution của chính lô vẫn là lịch sử tạo ra nó, không tự gán sang replacement.
- `history`: dùng cả COMMITTED/CORRECTED; hiện trạng stock và restrictions vẫn là tại snapshot hiện tại. Đây **không phải** historical inventory balance tại một thời điểm quá khứ.
- Ordinary transfer giữ lot ID, không thêm production edge; mixed-bulk ingress/withdraw có typed custody edge. Các generation cũ vẫn giữ trong graph dù không còn stock hoặc không là current pointer; top-up sau không trở thành tổ tiên aliquot cũ. Effective HOLD reuse helper của Ledger, không cấp runtime đọc private custody tables/helpers.
- Không tự đưa replacement thành hậu duệ vật lý của original; xem `corrections` để hiểu thay thế.
- Source đang pending arrival chưa có Ledger lot: bị từ chối như một identity chưa khả dụng trong scope. Không tạo stock, không tự cân/nhận hàng.

Graph, positions, restrictions và attribution được đọc theo tập trong một câu SQL ở READ COMMITTED. Audit cùng transaction với graph và source enrichment; enrichment lỗi thì không commit success audit. Không dùng global stock-write lock hoặc response cache. Kết quả không phải snapshot ổn định qua nhiều lần chạy; cần chạy lại khi stock/evidence thay đổi.

## Lỗi và giới hạn

- `400`: input/direction/view không hợp lệ; cookie thiếu CSRF cũng theo middleware hiện tại.
- `401`: thiếu/expired/revoked authentication.
- `403`: thiếu quyền, sai use case/owner hoặc lot không tồn tại/chưa nhận; không phân biệt foreign ID với missing ID.
- `409`: quá 5.000 reachable lots hoặc 20.000 edges. Không trả partial graph hoặc commit success audit.
- `503`: routine chưa nâng cấp hoặc query không hoàn tất trong budget. Server statement timeout 25 giây, client command timeout 30 giây; đây là guard tài nguyên, chưa phải chứng minh NFR 30 giây toàn mùa vụ.

API trả toàn bộ graph trong giới hạn; UI phân trang cục bộ phần trình bày (25 dòng) sau khi nhận tập hoàn chỉnh. Không lấy pagination inventory làm giới hạn traversal. Version đầu chưa có export/batch traversal lớn hơn guard.

## Table / Flowchart trên web

Sau **Run trace**, chọn **Table** hoặc **Flowchart** tại phần Trace result. Flowchart kết hợp danh sách lô và Processing relationships thành sơ đồ **lô đầu vào → công đoạn/sự kiện kho → lô đầu ra**, dùng đúng snapshot đã tải. Chuyển cách hiển thị, chọn node và zoom không gọi lại trace API hoặc tạo thêm audit.

- Mũi tên luôn theo chiều vật liệu từ trái sang phải. Với Backward, các tổ tiên nằm bên trái lô bắt đầu; không đảo cạnh thành quá trình vật lý ngược.
- Lô bắt đầu có nhãn **Starting lot**, khác với node đang được chọn. **Focus starting lot** cuộn và đặt focus về node đó; có zoom, Fit graph và cuộn khi sơ đồ lớn.
- Chọn một node lô để xem stock/restrictions và phần source composition được API cho phép. Export Staff vẫn không có source composition.
- Mỗi công đoạn chỉ có một node dùng chung cho các nhánh vào/ra. Quan hệ chỉ hiển thị trong phạm vi trace được trả; một công đoạn có thể còn inputs/outputs khác nằm ngoài hướng truy hiện tại. Các kg input/output xem trong **Operation quantities and exact relationships**, giữ nguyên decimal string, không cộng lặp các cặp input–output.
- Correction references vẫn tách riêng, không dựng cạnh vật liệu ngược hay cạnh tự động original → replacement. Bản CORRECTED trong History giữ đúng status trên node công đoạn.
- Để giữ thao tác tương tác gọn, Flowchart hỗ trợ tối đa **250 node lô/công đoạn/sự kiện kho và 500 quan hệ API**. Quá giới hạn thì thông báo rõ và cho **Show table**; không vẽ một phần rồi coi là đầy đủ. Giới hạn API 5.000 lô/20.000 cạnh vẫn giữ nguyên.
- Table hiện xếp lô bắt đầu trước, sau đó theo từng tầng truy xuất và mã lô trong cùng nhánh. Thứ tự hiển thị không còn phụ thuộc UUID và không thay đổi dữ liệu API.

Flowchart được vẽ bằng HTML/SVG với React đang có; không thêm dependency, không cần migration mới và không thay đổi authorization/accounting backend.

## Chi tiết nguồn ruộng/vụ

Trong **Source composition** của lô đang chọn, mở **View field / season origin** ở từng nguồn đóng góp. Có thể mở từ cả Table và Flowchart; không fetch lại, không tạo audit mới. Phần này hiển thị nông dân → thửa/vụ (giống đã ghi nhận) → phiếu và dòng thu mua → source Ledger lot, kèm allocated kg/tỷ lệ của lô đang chọn.

Đây là chuỗi nguồn gốc, không phải công đoạn chuyển đổi vật liệu hoặc lịch sử đăng ký thửa. Source expectation được ghi riêng với allocated kg, không coi là arrival measurement. Tên/mã danh mục được ghi rõ là current reference labels; khi thiếu nhãn vẫn hiện ID bất biến và báo unavailable, không suy diễn dữ liệu. Legacy không có hồ sơ nguồn vẫn báo thiếu. Chỉ Customer Manager đúng owner và Administrator theo quyền trace hiện có được đọc; Export Staff không được bổ sung dữ liệu nguồn.

Không cần migration hoặc grant mới. Capture đọc metadata theo tập trên connection/transaction hiện có; UI không gọi lần lượt API nông dân/thửa/vụ.

## Deployment

Forward migration Ledger `20261017000000_AddLotTraceability` thêm `ledger.trace_lot(uuid,uuid,text,text)`. Runtime chỉ được EXECUTE entry routine; các grant được cập nhật trong `LedgerDatabase.ConfigureRuntimeRoleAsync` khi chạy migration workflow hiện có. Không sửa migration cũ hay grant PUBLIC. Trước khi dùng trên shared Supabase, owner cần phê duyệt và thực hiện migration/runtime grant theo quy trình repository; normal startup không tự migrate.

Kiểm thử và browser smoke của đợt phát triển dùng database Testcontainers cô lập. Sau yêu cầu trực tiếp của người dùng, migration đã được áp dụng vào database được cấu hình, cùng runtime EXECUTE grant; postflight xác nhận không còn pending migration và runtime validation pass. Không chạy seed hoặc data-changing smoke trên shared DB. Chi tiết tại mục 6.4 của [implementation plan](../plans/traceability-implementation-plan.md).

## Chẩn đoán `permission denied for function trace_lot`

Đây là thiếu quyền PostgreSQL `EXECUTE` của backend runtime login, khác với lỗi business role/owner bên trong routine. Customer Manager vẫn có quyền trace theo owner; đổi role của tài khoản người dùng không khắc phục lỗi database này.

Kiểm tra chỉ đọc bằng runtime connection:

```sql
SELECT to_regprocedure('ledger.trace_lot(uuid,uuid,text,text)') IS NOT NULL AS routine_exists,
       has_function_privilege(current_user, 'ledger.trace_lot(uuid,uuid,text,text)', 'EXECUTE') AS can_execute;
```

Nếu routine tồn tại và migration trace đã áp dụng nhưng `can_execute=false`, owner có thể cấp riêng EXECUTE trên đúng signature cho runtime role đang cấu hình, sau phê duyệt của database owner. Không cần seed hoặc sửa stock. Workflow `--migrate` hiện có cũng cấu hình lại toàn bộ runtime grants và có thể áp dụng migration còn thiếu; không dùng nó như lệnh chẩn đoán chỉ đọc.

`ValidateRuntimeAsync` nay kiểm tra routine trace tồn tại và runtime có EXECUTE khi khởi động. Không tự cấp quyền hay dùng migration credential để chạy ứng dụng. Kiểm tra tại database cấu hình trong phiên này xác nhận routine/migration tồn tại, runtime login khớp `Ledger:RuntimeRole`, nhưng EXECUTE trace bị thiếu trong khi EXECUTE `ledger.command` vẫn có; chưa xác định tác nhân đã đổi ACL.

## Mở trace và xem bằng chứng nguồn

Có thể thực hành theo [kịch bản test web với pool 800 kg hiện có](../guides/traceability-web-test-guide.md).

- Stock → mở chi tiết lô → **Trace this lot** chuyển sang Traceability và điền Ledger lot ID; người dùng bấm Run trace để tạo snapshot/audit. Source/sample hiện đã có link đến current stock theo mapping; từ đó mở trace. Không tự chạy trace khi đổi tab.
- Starting lot picker của trace đọc cả lô hết tồn và cho chọn historical mixed generation. Quy tắc chọn stock cho processing/transfer giữ nguyên. Nhập Ledger ID trực tiếp không bị dropdown bắt buộc chặn khi trang selector chưa chứa ID đó.
- CustomerManager có quyền `lab.read`/`lab.write` mở **View samples and laboratory evidence** trong Source composition. Dùng `provenance.sourceRecordId` (Capture SourceLot.Id), không dùng contribution `sourceLotId` (Ledger UUID) làm sample filter.
- Tái sử dụng Capture `GET /api/samples?sourceLotId=...` và `GET /api/lab-reports?sourceLotId=...`, mỗi API phân trang độc lập 20 records. Chỉ đọc một nguồn do người dùng chọn, không loop mọi root, không coi một trang là toàn bộ evidence. API Capture vẫn kiểm tra persisted permission/customer-company scope trên từng request.
- Kết quả report chỉ hiện result sets có confirmed sample association đúng nguồn; original result text/unit/LOD/LOQ/method giữ nguyên. Cho mở sample/handover history, tải original theo endpoint có quyền và xem related report refs. Unresolved report không được gán thành evidence nguồn đã xác nhận.
- Whole-report VN comparison được ghi rõ là cấp toàn báo cáo; report có thể chứa nhiều mẫu. Không gán summary này thành verdict nguồn/pool/output, không lấy số liệu nguồn suy nồng độ hậu duệ, không tự thay HOLD.
- Evidence đọc sau trace có thời điểm riêng; không phải snapshot cùng graph hoặc assessment pinned tại thời điểm trace. Đổi Table/Flowchart giữ panel đã mở, không gọi lại graph hoặc report. Chọn lô khác/đổi request đóng panel để tránh lẫn nguồn.
- Admin global trace và Export forward trace không có panel source lab này; không mở quyền lab/source composition mới. Không có quyền khác với không có report; lỗi đọc hiện rõ, không chuyển thành danh sách rỗng giả.

## Starting points và lab entry — 24/10/2026

`GET /api/ledger/trace/starts?mode=pools&page=1&pageSize=25`

- `mode`: `pools` mặc định (current pool còn lượng vật lý), `sources` (procurement receipt roots), `all` (cả historical/exhausted lots). HOLD không loại pool khỏi selector: trace cần thấy hàng đang bị giữ.
- Optional `search` tối đa 200 ký tự tìm code hoặc tên location; `facilityId`, `locationId` UUID. Filter được áp dụng trước pagination, sắp code/UUID ổn định; page ≥1, tối đa 1.000.000, pageSize 1–100. Query key lạ bị 400, không nhận owner/actor.
- Response `items`, `page`, `pageSize`, `totalCount`, `hasMore`, `facilities`, `locations`. Facets là current labels của vị trí trong scope, không chỉ trang đang xem. Một nguồn đã hết tồn có thể không còn vị trí phù hợp filter location; chọn sources/all và bỏ filter để tìm nó.
- Item có `id`, `code`, `ownerId`, `stage`, `kind`, `enteredPool`, `isCurrentPool`, `poolRevision`, `quantityKg` string và `positions`. Đây là trace projection có scope, không cấp general inventory API cho Admin. GET không ghi TRACEBACK audit.
- Trace `lots[]` bổ sung cùng `kind` và `enteredPool`; `positions` bổ sung `locationName`, `locationKind`, `facilityName`. Names là nhãn hiện tại, không phải tên bất biến ở thời điểm ingress. Kind: `SOURCE`, `CURRENT_POOL`, `POOL_GENERATION`, `ALIQUOT`, `REMAINDER`, `PROCESSING_OUTPUT`, `LOT`; current pool ưu tiên hơn loại generation gốc. Không suy loại từ RAW_PADDY hay prefix code.

`POST /api/ledger/trace/from-lab`

```json
{
  "reportId": "00000000-0000-0000-0000-000000000001",
  "resultId": "00000000-0000-0000-0000-000000000002",
  "view": "current"
}
```

Actor cần trace permission và `lab.read` hoặc `lab.write`, đồng thời persisted CustomerManager/ExportStaff của company chứa report. Admin không có lab wildcard. Capture resolver xác minh result thuộc report và section có confirmed sample association, report chưa bị retire, sample/source cùng company. Server map đến accepted Ledger root; luôn forward. Không chấp nhận source/lot/owner/direction giả mạo trong body. Result mismatch/unconfirmed/foreign: 404; chưa có accepted Ledger lot: 409; invalid input: 400; thiếu quyền: 403. Recheck trace authority và confirmed lab association trước commit.

Response giữ trace schema v2 và thêm `labOrigin`: `ownerId`, `lotId`, `reportId`, `resultId`, `sampleId`, nullable `substanceId`. Export vẫn `sources=[]`, contributions/positive source paths redacted; không gửi dữ liệu nguồn qua composition. Resolver chỉ trả identity evidence tối thiểu, không report bytes hoặc farmer/plot metadata. `TRACEBACK` audit hiện có lưu root/graph query; **labOrigin là response context, chưa được lưu thành evidence/assessment snapshot bất biến trong audit đó**. Muốn tái lập evidence dùng history/snapshot của residue service riêng; không coi trace reference là assessment pháp lý.

Trên web, confirmed result có **Trace forward from result**. Chỉ chuẩn bị URL/context, người dùng bấm Run trace; reload URL giữ report/result context mà không tự POST. Khi đổi actor/role/company, kết quả, lab context và selector cũ bị xóa. Stock → Trace this lot thay thế context lab trước đó.

Panel **Residue evidence — selected lot** chỉ cho actor có `residue.evaluate` (không Admin). Bấm **View residue bounds for selected lot** mới mount component hiện có: live preview/history đọc riêng, không tải toàn graph; đổi node đóng panel. Table/Flowchart không chạy lại trace. Lock snapshot là thao tác ghi tường minh của residue module; không tự HOLD/release, không xem accounting shares là nồng độ.

Forward migration `20261024000000_TraceEntryPoints` thêm selector và private metadata helper, thay trace routine bằng bản additive v2; không rewrite stock/history. Approved migration workflow cấu hình EXECUTE cho `ledger.trace_starts(uuid,text,text,uuid,uuid,integer,integer)` cùng trace routine hiện có. Runtime không được gọi trực tiếp `ledger.trace_lot_metadata(uuid,uuid[])`, không grant PUBLIC. Startup xác minh cả entry/helper ACL; nếu thiếu thì dừng với lỗi deployment rõ, không tự sửa quyền hoặc đổi sang owner connection. Đã áp dụng migration/grants mới lên shared DB theo yêu cầu sửa lỗi tiếp theo của người dùng; xem plan mục 13.3.
