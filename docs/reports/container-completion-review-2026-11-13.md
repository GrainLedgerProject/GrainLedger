# Báo cáo hoàn tất luồng container sau khi merge dev

> Post-merge review baseline. Subsequent implementation is recorded in the [execution plan](../plans/container-execution-plan-2026-11-13.md), [API contract](../api/container-execution-api.md) and [verification record](container-execution-verification-2026-11-13.md). The gap inventory below describes the state before that implementation.

Ngày đánh giá: **13/11/2026**. Nhánh thực hiện: `feat/container`. Nền container trước merge: `567297c`. Nền dev đã đọc và merge: **`57f247d`**, cùng toàn bộ thay đổi mới kể từ điểm chung `eecde7e`.

Đây là **báo cáo hiện hành và thứ tự công việc cho lần triển khai tiếp theo**. Các plan tháng 10 được giữ làm lịch sử. Báo cáo phân biệt code đang có, phần chưa nối và quyết định nghiệp vụ chưa xác nhận; không coi trạng thái source là bằng chứng DB dùng chung đã được triển khai.

## 1. Kết luận

**Có thể tiếp tục triển khai container trên nền dev mới.** Không còn lý do chờ team xây từ đầu order, phương án, đánh giá đề xuất, duyệt và giữ tồn: các phần đó đã có implementation. Tuy nhiên, **chưa thể mở loading/CLOSE thật chỉ bằng cách bỏ readiness gates**.

Khoảng trống chính là đường thực thi sau duyệt: liên kết đúng container kế hoạch; tiêu thụ reservation được phép; với phương án cần trộn thì ghi sản xuất thật và giữ đầu ra cho container; kiểm tra hàng thực xếp; ghi loading/custody nguyên tử; container HOLD; đóng và lưu manifest/hồ sơ bất biến. Các phần nối bắt buộc này cần nằm trong đợt hoàn thiện container, thay vì tiếp tục để “chờ contract” chung chung. Thay đổi policy/kết quả khoa học hoặc mở rộng nghiệp vụ chưa rõ vẫn cần chốt, không tự tạo PASS.

Lần này đã merge và thích nghi code container với refactor, giữ nguyên hành vi preparation/draft, rồi lập báo cáo. **Chưa triển khai thành công actual loading, CLOSE hoặc thay đổi dữ liệu vận hành.**

## 2. Đã đọc những commit dev nào

`git log --reverse feat/container..dev` tại thời điểm bắt đầu cho thấy **5 commit** mới, không chỉ commit cuối:

| Commit | Nội dung đã đối chiếu | Ảnh hưởng container |
| --- | --- | --- |
| `dce8fa8` | Tổ chức lại docs thành `api`, `guides`, `plans`, `reports` | Chuyển tài liệu container theo cùng cấu trúc, cập nhật liên kết. |
| `91fa83b` / #16 | GitHub Actions, phân luồng kiểm tra, coverage và cấu hình test/build | Dùng pipeline hiện tại; không đưa trở lại cấu hình/dependency cũ. |
| `58c6564` / #17 | F13 export orders/specification/container targets; F14 candidates, greedy search, evidence, assessment, preview/proposal và UI | Tái sử dụng demand/container/assessment thật; không xây thêm order hoặc solver riêng. |
| `2dd6650` / #18 | Gộp API và context/schema; tổ chức feature; tối ưu scoped reads; liên tục hóa mã Capture/cutover lot code | Container phải nằm trong API hiện tại, dùng AppDbContext/schema `grainledger`, không giữ project/schema cũ. |
| `57f247d` / #20 | F15 approve/reject/revoke, active approval, giữ tồn, trừ reservation trong available reads, guard demand edits và hàm bắt đầu execution | Đã có nguồn phê duyệt và giữ tồn thật. Còn phải nối execution/consume/output/loading; không đánh đồng reservation hiện có với OUTPUT reservation đã hoàn tất. |

Đã fetch `origin`; `dev` và `origin/dev` cùng trỏ `57f247d` khi đánh giá. Không suy diễn các commit nằm trên nhánh khác nhưng chưa vào dev là tính năng hiện có.

## 3. Merge và thích nghi refactor đã làm

- Backend container chuyển sang [Features/Ledger/Shipments](../../backend/src/GrainLedger.Api/Features/Ledger/Shipments/ContainerEndpoints.cs), cùng namespace `GrainLedger.Api.Features.Ledger`. Adapter gọi routine nằm trong partial LedgerService của feature.
- SQL chuyển sang [Data/Sql/ContainerPreparation.sql](../../backend/src/GrainLedger.Api/Data/Sql/ContainerPreparation.sql), đặt tất cả object dưới `grainledger` với tiền tố `ledger_`/`identity_` theo dev.
- Thêm forward AppDbContext migration [20261113000000_ContainerPreparation](../../backend/src/GrainLedger.Api/Data/Migrations/20261113000000_ContainerPreparation.cs). Không sửa baseline `InitialMonolith` hoặc migration F15. Migration của context Ledger cũ không được giữ làm migration chạy trong monolith.
- Giữ preparation v2: exact kg, optimistic version, request replay/conflict, observed pool revision, stale/empty/restricted position checks, role/company/facility scope, history và quyền truy xuất evidence/trace.
- [Runtime-role configuration](../../backend/src/GrainLedger.Api/Features/Ledger/LedgerDatabase.cs) cấp riêng EXECUTE cho entry `grainledger.ledger_container_action(uuid,text,jsonb)` nếu feature đã cài; không cấp quyền ghi trực tiếp bảng hoặc gọi helper private, không tự sửa grant khi startup.
- `availableOnly=true` lọc trước pagination và tôn trọng **tồn khả dụng sau F15 reservation**. Không thay `availableKg` trở lại số physical stock cũ. Picker free stock chưa phải picker reservation của riêng container.
- Giữ code tracing, order/planning/F15, context/model snapshot và cấu trúc build mới của dev; không phục hồi project module hoặc test lịch sử migration module mà dev đã bỏ.
- Các file local untracked có sẵn trước khi làm được giữ nguyên, không stage/track. Không sửa `.env`.

Giữ route preparation cũ để không phá caller hiện tại **không có nghĩa** đã nối nó với planned container của F13. Đó là hạng mục P0 bên dưới.

## 4. Những nền tảng đã có và giới hạn thực tế

| Thành phần | Đã có trong source | Còn thiếu đối với actual container |
| --- | --- | --- |
| Order và container kế hoạch | F13 company-scoped order, market/commodity, shipping window, specification, target, versions; UI `/export/containers/{id}` | Chọn container này làm định danh thực thi duy nhất; preparation cũ chưa được ràng buộc với nó. |
| Đề xuất và đánh giá | F14 `DIRECT_STOCK` / `BLEND_INPUT_STOCK`, snapshot allocations, residue/quality, applicable MRL, margin/required panel policy, preview PASS, proposal và dependency fingerprints | Đánh giá hàng thực xếp và toàn manifest; proposal mô phỏng blend không chứng minh output thật đã được sản xuất. |
| Duyệt và giữ tồn | CM approve/reject/revoke; một active approval/container; approve và reserve cùng transaction/lock; guard không tiêu quá stock đã giữ | Reservation hiện gắn **allocation đã duyệt**. Với BLEND_INPUT_STOCK đó là nguyên liệu; chưa có chuỗi consume input → actual output → reserve output. |
| Bắt đầu execution | [BeginApprovedBlendExecutionAsync](../../backend/src/GrainLedger.Api/Features/Exports/Planning/BlendExecutionAuthorization.cs) kiểm active approval/decision version, outstanding allocations, reassessment; nhận transaction của caller, khóa stock rồi company; đổi planned container sang LOADING | Là hàm internal, chỉ được gọi bởi tests hiện tại; chưa có production endpoint/caller nối thao tác đầu tiên. Không tự consume, tạo output hoặc ghi loaded mass. Không commit marker một mình. |
| Stock và processing | Exact posting, mixed-bulk revision/aliquot, deterministic source allocation, lot restriction, protected processing/transfer | Processing hiện tại không phải đường thực thi approved proposal; reservation trigger sẽ chặn rút phần đang giữ nếu chưa có authorized consume cùng transaction. |
| Lab/compliance nền | Confirmed evidence, F12 intervals, policy adoption, F14 assessment/yield/quality và dependency revisions | Không cần viết lại lab/calculator; cần adapter/check cho actual load/output và CLOSE, giữ freshness đến thời điểm ghi. |
| Container preparation | Metadata, draft, check stock/pool, residue/trace actions, audit/idempotency | OPEN-only; committed loaded kg vẫn 0; commit-loading/close vẫn HTTP 409; bốn gate chưa được tích hợp. |
| HOLD | Lot/source ancestry HOLD và restrictions do receiving/correction | Chưa có target/state container HOLD và đường xử lý/tra cứu ảnh hưởng qua actual loads. |
| Trace | Lot/custody/source/lab entry, forward/backward theo quyền | Chưa có actual container edges/manifest/source mass; draft không được trở thành material edge. |
| Hồ sơ đóng | Có dữ liệu nguồn và snapshot assessment có thể tái sử dụng | Chưa có close record/dossier bất biến và retrieval/export theo nội dung đã chốt. |

Căn cứ chính: [F13 endpoints](../../backend/src/GrainLedger.Api/Features/Exports/Orders/ExportEndpoints.cs), [planning routes](../../backend/src/GrainLedger.Api/Features/Exports/Planning/BlendPlanningEndpoints.cs), [approval](../../backend/src/GrainLedger.Api/Features/Exports/Planning/BlendPlanApprovalEndpoints.cs), [decision read](../../backend/src/GrainLedger.Api/Features/Exports/Planning/BlendPlanDecisionEndpoints.cs), [reservation SQL](../../backend/src/GrainLedger.Api/Data/Sql/BlendApprovalReservation.sql), [assessment](../../backend/src/GrainLedger.Api/Features/Exports/Planning/BlendAssessment.cs), [F15 HTTP tests](../../backend/tests/GrainLedger.Api.IntegrationTests/BlendPlanProposalTests.cs).

Một số tài liệu F14/plan tháng 10 vẫn ghi chưa có frontend/approval/reservation: đó là trạng thái lịch sử, đã bị source dev mới vượt qua. Không dùng chúng để kết luận team chưa làm.

## 5. Các vấn đề phải giải quyết trước khi mở loading

### 5.1. Hai định danh container chưa nối nhau

`grainledger.exports_containers` có order/company/target/version và lifecycle `PLANNED → LOADING → CLOSED`. `grainledger.ledger_containers` của preparation cũ tự có code/target/version UUID và status OPEN, chỉ giữ `orderId` chưa xác minh. Hiện không có FK/ràng buộc khiến một bản preparation chắc chắn là đúng target đã được duyệt.

**Đề xuất:** dùng ID của F13 làm định danh container chính; trạng thái/target/order/company do model đó quyết định. Loading-owned records gắn FK/scoped reference vào ID này; metadata và facility là extension nếu cần. Không tạo thêm order hoặc lifecycle cạnh tranh. Bản preparation cũ giữ được lịch sử; việc gắn dữ liệu cũ với planned container phải có thao tác xác nhận/đối chiếu rõ, không tự match bằng code/UUID do người dùng nhập.

### 5.2. Reservation input khác reservation output

- **DIRECT_STOCK:** hàng đã có đúng product/commodity và được duyệt; có thể tiêu trực tiếp allocation reservation khi loading, không ép tạo một operation BLENDING giả. Đây là lát cắt đầu nên làm.
- **BLEND_INPUT_STOCK:** phải rút đúng input reservation, ghi processing thực với actual outputs/loss/variance theo Ledger, lưu mapping proposal/execution/output; giữ phần output được phép cho container. Chỉ sau đó mới loading output. Không xếp raw/head input thay blended output chỉ vì cùng proposal.
- Schema reservation hiện có action `CONSUME`, nhưng **chưa có protected production consume command**. Runtime không được phép tự insert sự kiện consume. Cần command mới gắn quyền/approval, quota và posting; trigger bảo vệ stock vẫn giữ nguyên.
- Output đi vào mixed storage có thể biến thành successor pool. Cần quy tắc lưu actual identity/aliquot/revision và giữ quyền sử dụng output qua pool, hoặc từ chối tình huống chưa hỗ trợ. Không tự coi pool mới chính là output đã duyệt.

### 5.3. Authorization phải sống cùng transaction thực thi

Caller dùng AppDbContext/shared connection và READ COMMITTED; lấy stock advisory lock `714928352` trước row locks và giữ thứ tự stock → company của F15. Tái kiểm role/company/facility sau chờ lock; request không tự cấp actor/approval.

Hàm begin execution chỉ dùng khi **marker và lần ghi execution đầu tiên cùng commit hoặc cùng rollback**. Không gọi riêng để chuyển LOADING rồi mới gọi HTTP rút tồn. Sau khi container version tăng, snapshot demand cũ không tự trở thành snapshot execution mới: cần basis được pin cho execution và evaluator thích hợp cho lần loading tiếp theo.

### 5.4. Assessment phải kiểm đúng hàng và thời điểm

Tái sử dụng F12/F14 policy, MRL, quality và evidence readers. Lưu assessment phase LOADING/CLOSE, actual subject set/quantities, plan/decision và versions, source/pool/evidence references, calculation version, decision/reasons và timestamp. Recheck dưới protocol khóa/revision của publishers trước khi commit; không chỉ dựa vào preview đã thấy trên màn hình.

Container có nhiều lot không có nghĩa chúng đã trộn vật lý: không tự bình quân residue/quality toàn container để làm đạt ngưỡng. Với direct lots, giữ kiểm từng lot hoặc conservative bounds đúng policy hiện hành; chỉ dùng propagation khi có operation blend thật và rule phù hợp. Có policy khoa học đã được review vẫn phải có dữ liệu áp dụng cho actual output.

### 5.5. Stock ra khỏi kho phải còn được hạch toán trong container

Actual load phải có immutable header/lines và custody/attribution: exact kg, source position/aliquot, original unit nếu nhận nhiều đơn vị, actor/occurrence/recording time, approved allowance và assessment. Source stock giảm, loaded custody tăng; truy xuất và tổng source shares bằng đúng loaded kg. CLOSE không trừ tồn lần hai.

Tính cả downstream container consumption khi xét correction/reversal: không cho sửa như thể hàng đã xếp vẫn còn nguyên trong kho. Retry không phát sinh hai lần debit; lỗi giữa các bước rollback stock/reservation/manifest/audit cùng nhau.

### 5.6. Container restrictions và CLOSE chưa có

Cần container HOLD do đúng role tạo, phạm vi đọc/xử lý tương ứng và audit; lot HOLD vẫn chặn hàng bị ảnh hưởng. Không auto release khi lab đạt/đã acknowledge. Kết quả lab muộn phải tra ra container bị ảnh hưởng qua actual allocations; có thể ghi nhận restriction mới mà không sửa dossier lịch sử.

CLOSE đọc **toàn bộ committed loads**, không dựa vào draft hoặc page hiện tại: target/order cap, metadata/seal, authority, current assessment và restrictions; atomically lưu CLOSED + manifest/assessment/dossier/audit, idempotent. Không cho load thêm sau CLOSED. Đóng không đồng nghĩa dispatch/customs.

## 6. Plan triển khai tiếp theo

Các checkbox dưới đây là phần còn lại, không phải việc đã hoàn thành trong lượt merge.

| Bước | Công việc và đầu ra | Điều kiện nghiệm thu |
| --- | --- | --- |
| P0 | Chốt identity F13 container; extension/FK và mapping legacy; actual loading/execution DTOs, lock order, version/idempotency; chốt câu hỏi blocking ở mục 7 | Một container/order/target có nguồn sự thật duy nhất; không bypass bằng orderId tự nhập; scope và migration giữ lịch sử. |
| P1 | Lát cắt **DIRECT_STOCK**: nối active approval với container; begin execution cùng lần load đầu; protected consume + stock/custody/source allocation + immutable actual loading và audit; LOADING assessment/freshness + container blocking tối thiểu | Load một phần/rồi tiếp đủ target; exact source conservation; retry, concurrent overspend, stale/revoked/HOLD rollback; free vs own reserved tách rõ. Không mở khi một check bắt buộc còn thiếu. |
| P2 | **BLEND_INPUT_STOCK**: authorized production execution dưới quyền vận hành hiện có; consume input reservations cùng processing; map actual outputs; reserve eligible output cho container; làm rõ pool/bagging/transfer; sau đó dùng load path P1 | Không xếp input thay output, không tiêu input hai lần; variance/yield/loss không bị giả định; output không bị quy trình khác lấy mất; partial executions chỉ mở khi quota/lineage được định nghĩa. |
| P3 | Readiness/CLOSE trên toàn manifest; container HOLD/history; immutable close/dossier/retrieval, role redaction; chặn load/edit sau close | Nhiều lần load vẫn đúng target; close retry một hồ sơ, không double debit; race close/HOLD/evidence và order cap không vượt. Bản ghi đóng giữ nguyên khi có evidence muộn. |
| P4 | Nối UI từ Export order → planned container → decision → execution/loading → manifest → close; thay preparation nhập tay; nối actual container ↔ lots/sources/plots và chiều ảnh hưởng ngược | Người dùng làm hết luồng trên working web app qua API thật; trace chỉ dùng committed loads; không N+1, không cắt tập ảnh hưởng theo pagination. |
| P5 | Integration/regression + API/web walkthrough, migration/grant instructions, backup/cutover preflight; deployment riêng khi được duyệt | Tất cả invariants/test bên dưới đạt; môi trường đích được xác minh, runtime restricted khởi động; không coi test container là deploy DB chung. |

P1/P2 khác nhau ở nguồn hàng, không phải hai container model. Có thể mở DIRECT_STOCK trước nếu đủ policy/authority/assessment/restrictions; lúc đó UI phải nêu rõ blend execution chưa hỗ trợ. Muốn tuyên bố **hoàn tất order → blend → loading → đóng** thì P2 cũng bắt buộc. Nếu hệ thống thực tế chỉ dùng BLEND_INPUT_STOCK, đưa P2 trước lần bật production đầu tiên.

Các hạng mục tương ứng trong plan tháng 10 B1–B4 không còn đều “chưa có”: B1 đã có order/approval nhưng thiếu output binding; B2 đã có input/direct reservation nhưng thiếu consume/output; B3 đã có planning assessment nhưng chưa actual LOADING/CLOSE integration; B4 vẫn thiếu container target/state. Không chờ viết lại nền đã có.

## 7. Câu hỏi cần chốt và mốc chặn

Các câu trả lời đã có trong code/proposal như ExportStaff thao tác container, CM duyệt phương án, mass exact, không vượt stock, không tự release HOLD không cần hỏi lại. Các câu sau là khoảng trống còn ảnh hưởng thiết kế:

| Câu hỏi | Đề xuất để chốt | Cần trước |
| --- | --- | --- |
| Mốc demo/đợt đầu có chấp nhận DIRECT_STOCK trước, hay buộc đi qua blend thật? | Dùng DIRECT_STOCK kiểm đường tích hợp đầu tiên; vẫn làm P2 để hoàn tất toàn luồng. | Chọn thứ tự P1/P2; không tự loại blend khỏi scope. |
| Xác nhận dùng container F13 làm ID chính; preparation cũ cần giữ đọc hay cần nối sang order/target nào? | F13 authoritative, legacy giữ lịch sử và explicit mapping; không auto match. | P0 migration/API/UI. |
| Sau CM approve, ai khởi tạo yêu cầu thực thi và ai xác nhận actual blend? Handoff ExportStaff ↔ MillOperator qua task/operation nào? | Giữ ExportStaff authority yêu cầu, MillOperator authority sản xuất và MillManager variance review; không cấp quyền sản xuất cho ExportStaff chỉ để gọi helper hiện có. | P2 endpoint/authorization. |
| Output loading được phép là bulk blended rice, bagged rice hay cả hai? Có bắt buộc số bao/tare/gross/evidence cụ thể không? | Chỉ nhận identity/stage đã được approved và quy trình thực thi chứng minh; không thêm bag/tare bắt buộc nếu nghiệp vụ chưa yêu cầu. | P1/P2 line contract và validation. |
| Một approved blend được sản xuất nhiều mẻ không? Output dư/thiếu so với prediction được giữ/reserve/đánh giá thế nào? | Không dùng prospective yield thay actuals; quota và output mapping phải explicit. Không tăng target hoặc phân bổ dư sang container khác tự động. | P2 execution/consume/output reservation. |
| Output vào silo/top-up, hoặc transfer/bagging sau sản xuất: có cho phép trong execution đầu tiên không? | Chỉ mở khi reservation và source allocation chuyển được chính xác sang generation/aliquot/output mới; trường hợp chưa chứng minh thì báo chặn cụ thể. | P2 storage và output picker. |
| Khi không đủ exact target, có được đóng thiếu/đổi target sau load không? | Giữ exact-target hiện tại; không shortage tolerance. Nếu cho phép ngoại lệ cần rule, role, evidence và demand/approval revision riêng. | P3; không chặn chuẩn bị core strict path. |
| Kết quả lab/HOLD mới sau partial load: ai quyết định dừng, thay phương án hoặc xử lý hàng đã xếp? Có cho unload/reopen/reseal không? | Block load/close bị ảnh hưởng và giữ lịch sử; không tự unload/reopen/release. Workflow ngoại lệ phải được chốt riêng nếu cần sử dụng. | P1 blocking; ngoại lệ trước khi mở chính ngoại lệ đó. |
| CLOSE dùng những metadata/evidence bắt buộc nào; dossier tối thiểu và định dạng bàn giao là gì? Ai sở hữu phần xuất chứng từ? | Manifest + exact source shares + plan/decision/assessment/legal/evidence refs + container/seal + actor/time; scoped JSON/read trước, PDF/Excel theo mẫu đã thống nhất. Không tự làm customs/dispatch. | P3 schema và P4/P5 acceptance. |
| Database đích đã cutover monolith chưa; dữ liệu cũ/preparation cũ cần giữ ở đâu, ai duyệt triển khai/backup? | Kiểm tra read-only và lập cutover riêng trước migration; không chạy baseline/reset cũ lên DB chung theo suy đoán. | P5 deployment, không chặn coding/test cô lập. |

Đây là danh sách để thảo luận, chưa được team phê duyệt trong lượt này. Các chi tiết không ảnh hưởng đường chuẩn có thể chốt sau; không đòi làm workflow ngoại lệ mới để hoàn thiện đường chuẩn.

## 8. Kiểm thử bắt buộc trước khi tuyên bố hoàn tất

- Luồng qua HTTP thật: order/spec → draft/preview PASS → propose → CM approve+reserve → actual production nếu cần → load từng phần → close → read dossier/trace. Dữ liệu khoa học/policy trong fixture có nhãn synthetic; không giả approval/provider thành công.
- Authorization/CSRF/owner/facility; không chấp nhận actor, approval, compliance flags từ client; revoke/đổi role trong lúc chờ lock không được thực hiện write.
- Hai container cùng stock, hai load cùng quota; processing/transfer cạnh tranh; input/output reservation không overconsume; còn reservedKg + consumed/released bảo toàn quota.
- Pool revision/top-up/withdraw, duplicate rows, precision tới micro-kg, partial repeated allocations/source shares và output identity; rollback khi lỗi sau consume hoặc posting.
- Evidence/MRL/policy/quality/HOLD đổi giữa preview và commit; thiếu một required input trả blocked/incomplete, không nhận preview cũ làm PASS hiện hành.
- Correction/reversal không xóa hàng đã load; CLOSE không debit lại; retry không ghi double load/dossier; mọi late supplement tách khỏi original snapshot.
- Toàn manifest qua nhiều loads/plans nếu cho phép; current stock bằng 0 sau load không làm mất trace; reverse affected set đầy đủ dù UI phân trang; source amounts tổng đúng actual kg.
- UI actual totals và remaining tính exact string; chọn đúng approved output, stale-state reload; quyền evidence/trace và desktop/mobile. Không coi một unit test mock UI là bằng chứng runtime đã chạy.

## 9. Kiểm chứng lần merge này

Kết quả thực chạy trên bản merge monolith; không dùng kết quả test tháng 10 thay cho kiểm chứng hiện tại.

- Build backend: đã đạt; warnings hiện có CS9113 (unread `access`) và xUnit2020 trong ResidueEvaluationTests.
- Full backend suite sau port/grants: **565/565 pass**, không skip, dùng PostgreSQL Testcontainers cô lập.
- Full frontend: **251/251 pass**, 33 files, maxWorkers=2. Lệnh root chuyển tiếp `--maxWorkers` ban đầu bị npm từ chối; đã chạy đúng script workspace, không retry test lỗi để bỏ qua failure.
- Web TypeScript/Vite build: đạt; còn warning chunk lớn hơn 500 kB.
- Regression bổ sung F15 reserved stock không xuất hiện lại trong free-stock picker: **5/5 BlendPlanProposalTests pass** sau khi thêm assertions vào luồng approve thật. Đây là chạy lại một nhóm trong 565 tests, không cộng thành 570 tests khác nhau.
- API smoke qua loopback host/Testcontainers với cookie/CSRF thật: **đạt**. Tạo preparation v2, lưu draft **8.000000 kg**, replay giữ nguyên receipt, loading-check trả blockers; commit-loading/CLOSE **409**, foreign company đọc **403**; stock không đổi và history đúng hai events (create + save). Script smoke ban đầu chưa gửi đủ CSRF cookie nên bị 400; đã sửa client gửi cookie/token đúng rồi chạy đủ luồng. Không phải smoke DB vận hành hay browser verification.
- Không chạy migration/seed/grant/reset/data-changing smoke trên Supabase; không đổi connection settings. Migration mới chỉ chạy trong test DB. Không xác nhận production deployment, throughput hoặc kiểm thử trình duyệt/mobile trong lượt này.

## 10. Ranh giới scope

Trong MVP container: binding planned container, nối approved execution/reservation, actual loading, container HOLD/restrictions, CLOSE/manifest/assessment snapshot, audit và container trace. Nếu thiếu protected consume/output bridge hay actual assessment adapter, đó là việc tích hợp cần bổ sung để F16 chạy thật, không thể thay bằng DTO hay bỏ gate.

Không mở rộng sang solver nâng cao, buyer portal, EPCIS, AI risk prediction, native mobile, ERP/customs, dispatch logistics, payment hoặc carbon/weather. Lab/MRL/F12/F14 đã có được tái sử dụng; không viết lại thuật toán/policy của team. Phần chứng từ ngoài manifest/dossier tối thiểu cần xác nhận ownership ở mục 7.
