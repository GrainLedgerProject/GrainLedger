# Plan thực thi container — 13/11/2026

Nhánh `feat/container`, nền `bcf2c3e` đã merge dev `57f247d`. Đây là plan hiện hành thay thứ tự ưu tiên trong các plan tháng 10; [báo cáo sau merge](../reports/container-completion-review-2026-11-13.md) giữ căn cứ đánh giá.

## Căn cứ và phạm vi

Đã đọc bản local Report5_Test Documentation.docx và SWP490_R2_BM04_Review2_Commitment_1.docx, cùng các ghi chú rút gọn/chốt cam kết ngày 09/11. Chỉ đọc; không đưa tài liệu local vào Git hoặc sửa trạng thái NOT RUN trong DOCX. BM04 là cam kết bàn giao, không phải bằng chứng test PASS. Report5 có câu ST-16 “Load a closed container” mâu thuẫn lifecycle; thực hiện load trước close, closed từ chối load, không dùng câu đó làm quyền mở lại.

WF-05/F18 phải nối **actual loading**, source kg/shares và plot provenance; planned allocation/draft không phải material movement. Tracing không tự HOLD. UT-14 exact source allocation, IT-13 scope và IT-14 missing-data được automation; IT-11/12/ST-18 cần bằng chứng actual container. ST-21 performance/full-season và staging/witnessed acceptance là đợt riêng, không lấy timeout hoặc test nhỏ làm chứng minh ≤30 giây. Không sửa owner/test count/confirmation của team.

Order/proposal/approve và **blend sau duyệt thuộc team khác**. Container tái sử dụng F13/F15 và F12/F14 assessment; không viết lại solver, lab/calculator hoặc tự nhận sản xuất blend.

## Đợt này — triển khai được

- [x] A. Dùng planned container F13 làm ID và nguồn order/company/target/status/version. Thêm extension metadata/facility, immutable execution/loads/allocations/holds/close/request records bằng forward migration. Legacy preparation giữ nguyên, không tự map lịch sử.
- [x] B. DIRECT_STOCK loading qua approved proposal: recheck current active decision, exact allocation/quota, product, scope, stock/restrictions và current assessment. First execution marker cùng lần load đầu; partial load/retry/concurrency cùng caller READ COMMITTED transaction, existing stock lock trước rows/company lock. Protected consume và stock debit không tách transaction.
- [x] C. Exact source allocation/custody: actual load + source shares + reservation consume + posting/audit atomic; repeat loads giữ source complement, không sửa creation contributions. Từ chối ambiguous position attribution, mixed/custody generation chưa có reservation handoff, stale external movement không chứng minh được complement; lý do explicit, không rebind tự động.
- [x] D. Container HOLD cơ bản của CM own-company, append-only và alert/history; không implicit release. Lot/source HOLD tiếp tục block. Scoped read, active authority rechecked sau chờ lock.
- [x] E. CLOSE đường chuẩn: toàn actual manifest bằng target, order cap, number/seal, current assessment và restrictions. CLOSED + immutable dossier/audit cùng transaction, retry không double-debit; load/edit sau close bị chặn. Scoped read/download JSON là hồ sơ tối thiểu; không tự thiết kế chứng từ pháp lý.
- [x] F. UI dưới Export container: approved allocations và reserved remaining riêng free stock, actual loaded/remaining, confirm load, metadata, HOLD, close/read dossier, current blockers và history. Giữ original preparation là legacy, không báo đã xếp từ draft.
- [x] G. Container → actual source/plot kg và source/lot → actual containers; phân biệt planned/actual, current restrictions, role redaction, complete result/fail explicit khi quá giới hạn; trace ghi audit, không stock/HOLD mutation.
- [x] H. Focused regression + Report5 mapping; Release build, backend xUnit/Testcontainers có coverage, npm ci/build/Vitest coverage theo workflow, CI routing/docs links/actionlint và Docker build nếu được chọn. Actual HTTP smoke bằng restricted runtime/cookie/CSRF trong disposable DB; không shared migration/seed/grants. Chỉ báo CI thực khi có GitHub run, local tương đương không gọi là remote CI PASS.

Không mở thành công một write còn thiếu mandatory check. Readiness là current read hướng dẫn; commit luôn recheck. Missing policy/evidence không mặc định PASS. Các publishers lab/rules/MRL/quality hiện có dùng stock advisory lock; assessment ở bên trong cùng boundary trước write và recheck current actor.

## Để lại lần sau — dependencies/quyết định chưa có

| Hạng mục | Vì sao chưa bật | Điều kiện tiếp tục |
| --- | --- | --- |
| BLEND_INPUT_STOCK → actual blend/output | Bên order/blend sở hữu execution; current internal start helper chưa ghi processing/output reservation | Team bàn giao production execution + current output identity/quantity + reservation/consume và evidence, cùng connection/transaction. Container tích hợp, không xếp nguyên liệu thay đầu ra. |
| Mixed silo/top-up/withdraw/bagging/transfer sau duyệt | Reservation đang pin lot/location; generation đổi cần chuyển quota/attribution đúng, không tự rebind | Contract chuyển reservation/actual aliquot, role thực thi và regression conservation. Đường DIRECT_STOCK chỉ mở position attribution đã chứng minh. |
| Output dư/thiếu, nhiều mẻ/đổi phương án sau partial execution | Chưa có quota/approval revision rule cho actual production | Team chốt expected/actual deviation và output allocation; không tự tăng target. |
| Close thiếu, unload/reopen/reseal/cancel loaded container | Chưa có authority/evidence/compensation policy | Có quy tắc reviewed riêng trước khi mở ngoại lệ. Đường chuẩn strict-target vẫn làm. |
| Auto HOLD/release, giải phóng HOLD | Không có policy; tracing/lab confirmation không tự quyết định disposition | Chốt actor, evidence và multi-HOLD behavior; hiện chỉ manual HOLD và block. |
| Revalidation sau partial loading khi lab/MRL/policy thay đổi | Current fingerprint không còn khớp approval; F15 hiện chỉ revoke trước execution | Owner bổ sung reviewed revalidation/revision cho actual manifest và remaining quota; không tự thay approval bằng PASS mới. |
| Container notification inbox/acknowledgment | Đã có manual HOLD, current alerts và history; inbox hiện hữu của Ledger target lot, không phải container | Chốt actor nhận/phạm vi và handoff với notification owner; không báo đã có inbox/ack chỉ từ alert trên detail. |
| PDF/Excel chứng từ chính thức, late supplements | Chưa có mẫu/ownership/định dạng được chốt | Duyệt nội dung theo immutable dossier; bản JSON không tự được gọi là chứng từ pháp lý. |
| Legacy preparation → planned container | Chưa có mapping người dùng xác nhận | Read-only inventory và explicit mapping plan; không match tự động theo code/orderId. |
| Legacy data migration / môi trường khác | DB đang cấu hình đã được user duyệt và cài hai migration container; mapping dữ liệu preparation cũ và môi trường khác chưa được duyệt | Preflight/mapping và approval riêng; không tự reset/cutover hoặc dùng owner để chạy app. |
| ST-21 full-season performance, staging demo/sign-off | Chưa có declared season dataset/oracle/máy/acceptance witness | Chốt dataset, đo completeness/cold/warm và ≤30s riêng; không tự ký BM04 hoặc đổi DOCX sang PASS. |

## Quy tắc kiểm chứng

Mass qua API là string, numeric(20,6); source shares dùng integer micro-kg/largest remainder với UUID tie-break, tổng đúng actual load. Race tests: retry/conflict, over-target/quota, same-stock, role revocation, HOLD/fresh evidence, partial close, closed load, rollback sau consume/posting; direct stock conservative assessment, không bình quân nồng độ các lô rời. Authorization nằm backend, Administrator không wildcard business.

## Kết quả thực hiện

Đã thực hiện A–G trong đường DIRECT_STOCK được mô tả; H đã chạy các kiểm tra local theo workflow và HTTP thật. Chi tiết tại [verification record](../reports/container-execution-verification-2026-11-13.md).

- Backend Release + coverage: **573/573 pass**, không skip; frontend production build + full coverage: **260/260 pass**; CI routing 7/7, actionlint, docs links và Docker image build đạt.
- HTTP loopback/cookie/CSRF trên Testcontainers: actual 4.000001 + 5.999999 = 10 kg, hai nguồn mỗi nguồn 5 kg; quota/stock reconcile; source forward tìm container, container backward ra plot; close/retry không double debit, late HOLD không sửa dossier. Export backward/cross-company bị 403 và dossier Export được redact.
- UI tự động kiểm confirmation, exact strings, role visibility và retry nguyên request khi network/408/5xx/malformed success. Working web đã có màn thực thi và [hướng dẫn manual test](../guides/container-execution-web-test.md); chưa tuyên bố manual browser/mobile/staging ST-18 hoặc ST-21 benchmark.
- 159 file local có sẵn vẫn unchanged/untracked; không sửa DOCX Report5/BM04, .env, shared DB, không commit/push. Migration mới chỉ được thử trong disposable DB. Remote GitHub CI chưa chạy vì chưa push/PR.


### Follow-up deployment được người dùng phê duyệt

Đã apply `20261113000000_ContainerPreparation` + `20261113000100_ContainerExecution` và cấu hình finite runtime EXECUTE trên DB đang cấu hình. Không còn pending migration; runtime validation và normal startup `/health` 200 đạt. Trace entries vẫn được gọi, helper private vẫn bị từ chối. Không seed/ghi dữ liệu nghiệp vụ hoặc thay connection settings. Xem [deployment record](../reports/container-execution-verification-2026-11-13.md#follow-up-approved-configured-database-deployment). Kết quả test cô lập phía trên giữ nguyên; không coi migration thành bằng chứng web nghiệp vụ đã được test trên DB chung.
