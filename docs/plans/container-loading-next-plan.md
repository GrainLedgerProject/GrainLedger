# Plan hiện hành: từ loading draft đến loading thật

> Current execution work and remaining dependencies: [13 November execution plan](container-execution-plan-2026-11-13.md). This document preserves its earlier implementation baseline.

> Cập nhật 13/11/2026: đã chuyển theo monolith trên dev. Đánh giá và thứ tự triển khai hiện hành nằm trong [báo cáo sau merge](../reports/container-completion-review-2026-11-13.md). Các mốc 10/2026 dưới đây là lịch sử, không dùng làm hướng dẫn migration hiện tại.

Ngày: **25/10/2026**. Nhánh: `feat/container`, baseline `3295cb6`, đã merge dev `eecde7e`. Plan này thay **thứ tự ưu tiên** ở các roadmap cũ; [plan gốc](container-loading-implementation-plan.md) giữ yêu cầu, invariants, lịch sử và kết quả kiểm chứng.

## 1. Mục tiêu và ranh giới

Mở **commit loading thật**: ghi nhận đúng hàng thực xếp, rút đúng tồn, tiêu thụ đúng OUTPUT reservation và giữ căn cứ kiểm tra/audit trong cùng transaction. **Chưa cần làm xong CLOSE, dossier hoặc document export để mở loading.** Nhưng điều kiện của chính LOADING phải đủ.

Giữ phân công người dùng đã chốt: nhánh container tập trung container; order → approved blend/review → reservation/execution do team khác cung cấp. Không tự mở rộng sang làm toàn bộ các module đó. Tất cả công việc dưới đây nằm trong MVP; không thêm solver nâng cao, portal, EPCIS, dự đoán rủi ro AI, ERP/customs.

Lát cắt đầu để tích hợp/test: **một order, một container, một approved plan, một actual output**, có thể loading một phần rồi loading tiếp. Đây là fixture/đợt kiểm chứng đầu, không phải tự ban hành policy chỉ được có một plan/output. Whole-container CLOSE qua nhiều plan/loading vẫn thuộc giai đoạn sau.

## 2. Hiện đã có

- [x] Container preparation, metadata, loading draft, exact kg, version/idempotency/history và scoped access.
- [x] Kiểm tra vật lý/lot restriction hiện hành; mọi commit-loading/CLOSE vẫn bị backend từ chối, không chỉ khóa nút.
- [x] Nền dev: mixed-bulk pool/revision/aliquot, effective lot HOLD; lab/residue rules, company adoption, preview và evaluation history; tracing lot/pool và confirmed lab entry.
- [x] Merge đã kiểm build, frontend 221/221, backend liên quan 132/132 và browser cô lập. Đây là kết quả baseline đã chạy, chưa phải kiểm chứng tính năng ở plan mới.

Chưa có: verified order/plan/output authority, stock reservation/atomic consume contract, LOADING assessment/freshness và container-level restriction provider. `orderId` trong metadata vẫn là reference chưa xác minh; draft chưa phải hàng được giữ hoặc actual loaded mass.

## 3. Giai đoạn A — Làm ngay trên nhánh container

### A1. Draft nhận biết đúng tồn và pool revision

- [x] Kiểm selector/position theo company + facility, phân biệt current positive stock với nguồn/generation hết tồn; giữ lịch sử draft cũ để đọc, không biến history thành hàng đủ điều kiện loading.
- [x] Bổ sung observed pool identity/revision vào contract draft khi chọn mixed-bulk; ordinary position không tự có pool revision. Draft revision chỉ là trạng thái đã quan sát, không phải giấy phép tiêu thụ hàng.
- [x] Save/check phát hiện pool đã đổi do top-up/withdraw, lô hết tồn hoặc restriction mới; hiện lý do và yêu cầu reload/reselect/resave, không tự đổi draft sang generation mới.
- [x] Nếu đổi SQL/persisted draft shape: forward migration mới, giữ historical payload/audit; không sửa migration đã apply. Đổi UI/DTO/backend/docs cùng nhau.

**Đạt khi:** test hai người chọn/sửa pool, stale revision, ordinary lot, mixed lot, foreign owner/facility; draft không trừ tồn, không giữ hàng, committedLoadedKg vẫn 0. Hoàn thành A1 chưa mở commit loading.

### A2. Xem evidence cho lô dự kiến xếp

- [x] Chọn line/lot rồi mới mở residue preview/history theo `residue.evaluate`; hiện chất, khoảng/unit/basis, reasons UNRESOLVED, rule/evidence refs và timestamp riêng.
- [x] Có action mở trace lô theo quyền hiện hành; reuse tracing dev, không fork hoặc viết lại calculator.
- [x] Không tự tải residue cho mọi lot trong danh sách; cùng lot nhiều line không gây gọi lặp. Giữ Export/Mill source/lab redaction; không tự lấy trung bình nồng độ các lô xếp chung container như thể hàng đã trộn vật lý.

**Đạt khi:** đổi selected lot/role không giữ evidence sai, lỗi khác với không có dữ liệu, preview/history không bị gọi thành export PASS. Snapshot ghi chỉ qua hành động tường minh hiện có.

### A3. Readiness dễ hiểu và contract bàn giao

- [x] Phân nhóm lý do trên UI: draft/stock/pool đã kiểm; chưa có order/plan/reservation; thiếu assessment/policy/evidence; container restriction chưa kết nối. Hiện việc cần xử lý tiếp, không báo “ready” khi còn thiếu một điều kiện bắt buộc.
- [x] Viết [checklist bàn giao](container-integration-handover.md), ghi implementation nào chưa có và ownership nào chưa xác nhận; không tạo production provider trả PASS giả.
- [ ] Team owners xác nhận checklist và bàn giao concrete contracts B1–B4. Phần này chưa thực hiện thay team, không coi tài liệu là agreement.

**Đạt khi:** người dùng biết draft vướng bước nào; backend vẫn chặn direct commit request. A1 → A2 là thứ tự khuyến nghị; A3 có thể làm cùng lúc. Chưa nhận thêm trách nhiệm xây order/blend/reservation trong nhánh này.

## 4. Giai đoạn B — Dependency phải bàn giao trước loading thật

- [ ] **B1 — Order/approved output authority:** persisted order/spec/market/target version, approved plan/review còn hiệu lực, review scope, actual output/aliquot/location mapping và exact kg được phép dùng. Server xác minh owner/container/facility; client không tự khai `approved:true` hoặc đổi target để hợp thức hóa hàng đã xếp. Order/blend team cung cấp, container tích hợp.
- [ ] **B2 — OUTPUT reservation:** persisted reservation gắn đúng plan/output/lot/location/container, outstanding kg, nguyên tắc release/invalidation và consume idempotent. Processing/transfer/correction cùng tôn trọng reservation. Không dùng reservation mã lot làm giữ kg. Không bỏ qua việc sửa các stock writer khác chỉ để loading chạy được.
- [ ] **B3 — LOADING assessment/freshness:** đầu vào và quyết định theo đúng hàng chuẩn bị xếp, destination/spec, analyte/quality coverage, lab/rules/MRL/margin versions. Missing policy/evidence trả thiếu/chặn. Có protocol chống evidence/policy/review đổi từ khi kiểm đến lúc commit; current read-only preview/evaluation cũ chưa đủ authority. Publishers và writers liên quan phải cùng tuân thủ revision/freshness protocol, không chỉ khóa bảng loading.
- [ ] **B4 — Container restrictions:** đọc authoritative container HOLD/blocking state, scope, restriction revision và ảnh hưởng tới loading mới. Lot HOLD đã có nhưng không thay container HOLD. “Risk” ở đây là lý do nghiệp vụ có căn cứ, không làm scoring/prediction engine. Không tự phát hành/release HOLD từ lab preview.

**Điều kiện qua B:** có implementation thật, concrete in-process contracts và integration tests chứng minh authorize/deny/revoke/freshness; không chỉ có tên DTO, permission constant hoặc status APPROVED. Consume/withdraw tham gia cùng connection + READ COMMITTED transaction + existing stock advisory lock; module routine không commit hộ caller, không giữ transaction qua thao tác người dùng hoặc gọi mạng ngoài.

B3/B4 cần phối hợp bên sở hữu compliance/HOLD; chưa mặc định team order/blend làm luôn hai phần này. Nếu chưa có người phụ trách phải phân công trước triển khai, không coi việc thiếu người là permission cho container tự đặt policy.

## 5. Giai đoạn C — Triển khai và mở commit loading

Chỉ bắt đầu bật đường ghi thành công khi B1–B4 sẵn sàng. Có thể chuẩn bị acceptance/schema đề xuất trước, nhưng không expose bypass hoặc successful mock loading trên DB vận hành.

- [ ] **C1 — Actual loading persistence:** forward migration cho immutable actual load/lines và references tới container, plan/review/output/reservation, consumed lot/aliquot, exact kg, basis/revisions/actor/time/idempotency. Snapshot không được thay bằng draft mutable.
- [ ] **C2 — Atomic write path:** existing advisory lock trước stock/source rows → recheck role/company/facility, container OPEN, target remaining, authority, position/pool revision, reservation và LOADING assessment/restrictions → exact withdrawal + consume reservation + actual load/basis/audit cùng transaction. Own reservation được consume đúng quota, không nhầm với free stock đã loại chính reservation đó. Mixed withdrawal giữ source allocations và actual aliquot identity.
- [ ] **C3 — Chống thao tác lặp/sai:** một request thành công có thể replay nhưng không rút lần hai; hai người loading cùng quota không vượt outstanding; stale review/pool/HOLD/evidence từ chối và rollback đủ. Không sửa/xóa committed loading; unload/correction chỉ mở khi có reviewed policy riêng, không invent workflow hồi tố.
- [ ] **C4 — UI loading thật:** explicit confirmation và server result; hiển thị actual committed loaded kg/remaining thay số 0 cố định. Drafts không cộng vào loaded mass. Chỉ mở Commit loading sau acceptance; CLOSE vẫn disabled và backend từ chối khi chưa làm D.

**Đạt khi:** isolated HTTP/browser + accounting reconciliation pass: loading một phần và tiếp tục, duplicate retry, double consumption, exceeded target/reservation, stale pool, cross-company, HOLD/review/evidence races. Chỉ test một happy path hoặc mock service không đủ. Deployment migration/grants trên DB chung cần phê duyệt riêng; normal startup không tự migrate, không chạy seed.

## 6. Giai đoạn D — CLOSE và hồ sơ, sau loading

- [ ] **D1 — CLOSE assessment:** lấy toàn bộ actual committed loading xuyên mọi plan version, kiểm exact container target/order cap, metadata/seal, current evidence/assessment và restrictions. Không chỉ dùng page đang hiển thị hoặc lần loading cuối.
- [ ] **D2 — Atomic CLOSE/dossier:** lưu assessment + CLOSED + immutable dossier + audit cùng transaction, idempotent; không trừ tồn lần hai, không cho loading mới sau CLOSED.
- [ ] **D3 — Documents/supplements/trace:** scoped dossier retrieval và export format đã chốt; late evidence bổ sung riêng; nối actual container ↔ consumed lots/aliquots ↔ sources/plots theo contracts tracing hiện có. Không đưa loading draft thành cạnh vật liệu.

**Đạt khi:** nhiều loading/plan versions, partial loads, concurrent CLOSE/order cap/HOLD, retry một dossier, role redaction và immutable history đều pass. D không phải dependency để mở LOADING ở C; fresh LOADING checks tại B/C vẫn bắt buộc.

## 7. Quyết định cần chốt trước từng đường ghi

- Actual loading dùng lô thành phẩm output nào, product/stage/spec nào, có bag count/tare/gross/evidence bắt buộc không? Không tự cho phép mọi stage chỉ vì draft lưu được.
- Ai ghi nhận hàng thực xếp và ai commit theo role hiện hành; có cần xác nhận vận hành riêng không? Không tự thêm hai chữ ký.
- Target/order thay đổi hoặc hủy sau partial loading xử lý thế nào? Không tự tăng target, đổi owner/order, unload hoặc reopen.
- Nguồn policy/authority duyệt analyte coverage, chất lượng, margins và freshness; thiếu rule chặn phần chịu ảnh hưởng.

Chỉ chốt quyết định liên quan trước giai đoạn chịu ảnh hưởng; không yêu cầu mẫu chứng từ cuối hoặc mọi policy CLOSE xong rồi mới cải thiện draft/evidence.

## 8. Việc có thể giao làm ngay

1. **A1:** draft/pool revision và stale-stock checks.
2. **A2:** residue/evidence + trace action cho selected loading lot.
3. **A3:** readiness messages và handoff checklist với team.

Sau đó nhận B1–B4 để làm C; giữ D cho đợt CLOSE. Sau yêu cầu tiếp theo “thực hiện theo plan”, đã triển khai A1/A2 và phần readiness/checklist của A3. B1–B4 vẫn chờ owners/implementation; C/D chưa bật. Kết quả tại mục 9. Chưa chạy shared migration/grants hoặc commit/push.


## 9. Kết quả triển khai A1–A3 — 25/10/2026

- **A1 source:** forward migration `20261025000000_ContainerDraftReadiness` bổ sung preparationVersion 2, observed poolRevision string; save từ chối mixed missing/stale revision, empty/restricted positions và ordinary revision giả. Check đọc legacy không đổi nội dung, báo POOL_REVISION_NOT_RECORDED/POOL_CHANGED/POSITION_UNAVAILABLE. Không tự remap và không backfill revision. Positive-position draft vẫn có thể đề xuất thiếu kg để precheck giải thích; tổng vượt target vẫn từ chối. Existing idempotent receipt giữ nguyên; web reload detail nếu receipt thuộc phiên bản cũ.
- Sửa `availableOnly=true` của stock reader ở server trước count/paging: loại ordinary zero stock, blocked lots và noncurrent mixed positions; không lấy một trang rồi lọc ở browser. UI source positions chỉ chọn positive stock; saved historical positions vẫn đọc được. Không thay policy reservation hoặc phép tính exact kg.
- **A2 source:** panel Loading lot evidence chọn một distinct lot từ form/saved drafts; residue history/preview chỉ mount khi người dùng bấm, reuse ResidueBounds. Trace selected loading lot mở đúng Ledger lot trong dev tracing và xóa ngữ cảnh lab cũ; không POST trace tự động. Rời tab/đổi scope đóng bounds, remount container khi scope đổi; không đổi source/download authority.
- **A3 source:** Readiness checks riêng, có check scope/timestamp và stockChecksPerformed, chia findings theo nhóm và next steps. Không có stock finding không phải export PASS. Backend vẫn ready=false/complete=false, commit-loading/CLOSE vẫn 409 và buttons disabled. [Handoff checklist](container-integration-handover.md) là bản đề xuất; chưa gửi cho hoặc được các owner chấp thuận. Không có fake implementation/approval flag mới.
- **Kiểm chứng cuối:** backend **133/133 PASS** trên PostgreSQL Testcontainers, gồm container/mixed-bulk/stale and legacy pool/available pagination, trace, migration/cutover/permissions và stock list/lookup/smoke/safety/acceptance/read scaling. Frontend full suite `--maxWorkers=2` **225/225 PASS**, 30 files; build TypeScript/Vite PASS, còn chunk >500 kB warning. Backend compile qua dotnet test còn CS9113 LedgerService.access đã có. Không chạy toàn bộ backend solution suite, không benchmark production.
- Lượt frontend trước có 224 pass/1 fail do test mới tìm nhãn observed revision trùng ở saved draft và edit form sau khi thêm chi tiết saved lines; đã scope assertion vào Loading line 1. Không đổi timeout hay nghiệp vụ để che lỗi. Lượt cuối full 225 pass.
- **Browser cô lập 5092/5182:** Export Staff tạo preparation target 8 kg, chọn current BLENDED_RICE pool 75 kg revision 2, save draft 8 kg; observed revision hiện ở form và saved draft, readiness có đúng nhóm missing integrations, committedLoadedKg vẫn 0. Mở một lot evidence, history/live preview trả UNRESOLVED với reasons/rule refs phù hợp fixture thiếu evidence; trace action đi đúng pool, Forward-only, chưa có POST trace. GET stock sau smoke vẫn 75.000000/75.000000 kg, revision 2. Không lock evaluation snapshot hoặc tạo actual load.
- Browser focused click báo `Preview automation click timed out after 5000ms.`; snapshot báo `Preview automation snapshot timed out after 15000ms.`. DOM evaluation/events trong cùng owned T3 preview hoạt động và dùng cho smoke; không báo đã kiểm pointer interaction. Resize request 390 px nhưng measured innerWidth vẫn 1280, nên chưa xác nhận mobile layout. Dev StrictMode replay hai GET history cho một selected lot, một GET preview; không coi đây là N calls cho toàn graph.
- Browser host/dev server riêng đã dừng; session file và Vite config tạm đã xóa. Chưa chạy migration/seed/grants hoặc data-changing smoke lên shared Supabase, không đổi `.env`, không commit/push. Feature upgrade chưa cài sẽ hiện thông báo rõ và chặn Save draft; normal startup không tự repair hay mới chặn backend vì optional container upgrade. Muốn test bản mới trên shared DB cần approved deployment riêng theo [API/setup](../api/container-api.md#draft-preparation-v2--implementation-25102026).

**Phần tiếp theo:** owners xác nhận/bàn giao B1–B4 → C1–C4 cho loading thật → D cho CLOSE/dossier. Không đánh dấu full loading hoàn tất từ kết quả draft/evidence này.

### 9.1. Rà soát lại sau yêu cầu kiểm tra lỗi

Đã phát hiện và sửa hai lỗi, không kết luận phần mềm tuyệt đối không còn lỗi:

1. UI có thể giữ evidence của một unsaved loading lot sau khi clear/reselect lot/location, edit/remove line hoặc New draft. Đã đóng panel và unmount bounds khi đổi ngữ cảnh; thêm regression và kiểm lại trên browser cô lập: mở bounds của lô chưa lưu → New draft → panel/bounds đóng, không có lỗi hiển thị.
2. Container mutation đọc role trước lúc chờ advisory lock; nếu role bị thu hồi trong lúc chờ, request cũ vẫn có thể ghi. Test PostgreSQL/HTTP cố định thứ tự lock → request waiting → revoke role → unlock tái hiện trước sửa (expected 403, actual 200). Forward preparation migration nay recheck active Export role và company sau khi lấy lock, trước container rows/idempotent replay/mutation. Sau sửa, request bị 403, container version/metadata/history không thay đổi. Không sửa migration đã apply hoặc dùng elevated runtime để né kiểm tra.

Kết quả rà cuối: **134/134 backend affected tests PASS**, **226/226 frontend tests PASS** (30 files), web build PASS, `git diff --check` PASS. Backend compile từ lượt ContainerTests sau sửa PASS; còn warning CS9113 đã có và web chunk >500 kB. Không chạy toàn bộ backend solution suite hoặc benchmark. Browser regression dùng 5093/5183/Testcontainers và DOM events trong owned T3 preview; test lock/revocation dùng actual HTTP với restricted runtime trên DB cô lập. Browser host đã tạo trước bản recheck SQL cuối, nên browser chỉ xác nhận UI fix; test HTTP/PostgreSQL cuối xác nhận recheck SQL trên migration mới. Host/config/session tạm đã được dọn. Không deploy/migrate/seed/grant shared DB, không commit/push. Loading/CLOSE thật vẫn chưa bật; owner contracts và mobile layout vẫn chưa được nghiệm thu.
