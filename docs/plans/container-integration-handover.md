# Container integration handoff — owner confirmation pending

> Cập nhật 13/11/2026: đã chuyển theo monolith trên dev. Đánh giá và thứ tự triển khai hiện hành nằm trong [báo cáo sau merge](../reports/container-completion-review-2026-11-13.md). Các mốc 10/2026 dưới đây là lịch sử, không dùng làm hướng dẫn migration hiện tại.

Ngày 25/10/2026, nhánh `feat/container`. Đây là checklist trao đổi cho giai đoạn B của [plan hiện hành](container-loading-next-plan.md), **không phải contract đã được các team phê duyệt hoặc provider đã triển khai**. Không liên hệ/gửi thông tin cho team qua công cụ trong lượt này.

## 1. Những phần container đã chuẩn bị

- Draft lưu Ledger lot/location, exact kg và observed pool revision dạng decimal string cho mixed position. Ordinary position không có revision. Revision không tự cập nhật khi pool đổi; legacy mixed draft không có revision cần chọn lại.
- Readiness hiện reasons theo nhóm stock/pool, order/approved outputs, OUTPUT reservation, assessment, container restrictions và details/target. Check là quan sát, không giữ hàng và không cấp quyền loading.
- Selected loading lot có thể mở existing residue preview/history hoặc tracing theo quyền; không biến interval COMPLETE thành export PASS.
- Mọi actual commit-loading/CLOSE vẫn trả 409, ready=false/complete=false; chưa có stock withdrawal hay reservation consumption.

## 2. Order/blend team — B1 và B2

Bên sở hữu được người dùng xác định là team đang làm order/approved blend/reservation. Cần bàn giao implementation thật và xác nhận:

- Order identity/company, destination/commodity/specification version, required kg và target/quota gắn container. Target change sau partial loading không được tự hợp thức hóa bằng request metadata.
- Plan identity/version, review identity/version/scope (ALL_REMAINING/OUTPUT_ONLY theo model đã chốt), trạng thái còn hiệu lực, cách revoke/re-review/invalidate; actor/owner được kiểm từ persisted identity.
- Actual execution/output allocation và lot/aliquot/location mapping. Logical pool input, physical BLENDING operation và output của plan là các identity khác nhau; không gắn hồi tố production cũ bằng client UUID.
- OUTPUT reservation identity, cùng company/plan/output/container/position, exact outstanding kg và replay identity. INPUT reservation và lot-code reservation không được dùng để loading.
- Consume/release/invalidation semantics do owner chốt; consume phải tham gia caller connection + READ COMMITTED transaction + stock advisory lock, không tự commit hay mở connection độc lập để consume.
- Mọi stock writer/transfer/correction đã tôn trọng reservation, cách xử lý pool revision/withdrawal và phần kg được giữ. Không chỉ cập nhật endpoint loading.

Acceptance cần thấy: valid own reservation; foreign/container mismatch; stale review/output; insufficient outstanding; two consumers; replay; rollback không tiêu thụ một nửa; old writers không lấy mất stock đã giữ.

## 3. Compliance/evidence owner — B3 (chưa có người phụ trách cụ thể)

Cần xác nhận người/team sở hữu trước khi tích hợp LOADING decision. Reuse lab/residue module hiện có, không yêu cầu viết engine tính dư lượng mới.

- Required analyte/quality coverage, đúng destination/commodity/spec version, concentration basis/unit/residue definition, current MRL/margin policies và treatment of missing/conflicting/late evidence.
- Decision scope phải chỉ rõ hàng thực định loading và exact kg; retain decision/basis references, engine/policy/evidence versions và reasons. Missing policy/evidence không PASS; current saved evaluation ID do client gửi không tự cấp authority.
- Freshness protocol: evidence/rule/MRL/spec/margin publishers, plan review và stock/container writer phối hợp kiểm revision/knowledge state đến lúc commit. Một GET preview hoặc việc chưa quá N phút không tự chứng minh freshness; không tự đặt TTL.
- Container chứa các lô không trộn vật lý không được mặc định là hỗn hợp đồng nhất để lấy trung bình concentration. Required sampling/aggregate semantics phải do policy xác nhận.
- LOADING assessment và whole-container CLOSE assessment là hai phạm vi khác; có thể bàn giao LOADING trước để mở giai đoạn C, CLOSE sau giai đoạn D.

Acceptance cần thấy: missing coverage/rule, incompatible basis/unit, superseded/late lab, changed MRL/margin/spec/review giữa check–commit và authority revocation; retain old evidence/basis immutable; failure rollback loading và reservation cùng nhau.

## 4. HOLD/container restriction owner — B4 (chưa có người phụ trách cụ thể)

- Persisted target container riêng, đúng company, current blocking state và restriction revision; lot/effective ancestral HOLD hiện có không thay container-level check.
- Scope/thẩm quyền phát hành, notices, acknowledgement/isolation/response và effect đối với loading mới; không tự phát hành hoặc release HOLD từ preview.
- Cách restriction changes tham gia cùng freshness/locking protocol. “Không có active HOLD” chỉ là kết quả hợp lệ khi provider thật đã đọc đủ scope; provider chưa có phải UNAVAILABLE.
- Không mở prediction/scoring engine, standalone loss settlement, auto-release/reopen hoặc dispatch lifecycle ngoài scope đã chốt.

Acceptance cần thấy: company mismatch, lot HOLD, container HOLD độc lập, concurrent issue/response và loading; response/read notice không tự xóa block; no cross-company evidence leak.

## 5. Shared transaction và lỗi/retry

Concrete in-process DTO/ports chỉ chốt khi owners có model thật; không truyền EF entities, không gọi mạng ngoài trong stock transaction. Container orchestrates revalidation/withdrawal/consume/persist/audit, từng module sở hữu schema và không commit caller transaction.

Phải phân biệt invalid input, forbidden scope, conflict/stale state và dependency unavailable. Retry cùng actor/request/payload không consume thêm; changed payload phải conflict. Idempotent receipt cũ không phải fresh eligibility: sau replay reload detail/check nếu cần đọc hiện tại.

Chưa áp đặt wire names, error enums mới hoặc fake PASS provider trong application code chỉ để đánh dấu checklist “done”. Cần contract tests qua implementation thật trước khi gỡ gate.

## 6. Điều kiện chuyển sang loading thật

B1–B4 có owners và implementation đã xác nhận → tests authorization/accounting/freshness/concurrency/replay pass → actual HTTP/browser loading trên Testcontainers → review migration/grants và triển khai được phê duyệt. Không yêu cầu CLOSE/dossier hoàn tất trước LOADING; không bỏ các điều kiện của chính LOADING.
