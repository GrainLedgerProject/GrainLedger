# Container S2 — hai walkthrough web bổ sung, 18/11/2026

**Kết quả: cả hai PASS trên web**, bổ sung hai khoảng trống trong [báo cáo S2](container-s2-implementation-2026-11-18.md). Lượt này không sửa code ứng dụng, không chạy lại toàn bộ backend và không áp dụng migration/grant lên Supabase dùng chung.

Dùng production web assets qua BrowserWorkflowHost tại loopback port 5293 và PostgreSQL Testcontainers mới. Host áp dụng migration vào DB cô lập và tạo synthetic fixtures, hoàn toàn tách DB dùng chung. Các phiên Export/CM/Operator dùng cookie thật qua cầu nối đăng nhập test; session validation, CSRF, phân quyền và API handlers thật vẫn hoạt động. Không phải bài test nhập email/password vào form đăng nhập.

Các chuyển tiếp container dưới đây được thực hiện qua form/button và xác nhận trên giao diện. Quan sát chất lượng actual lot/evidence mới được chuẩn bị qua API CM hiện có để tập trung kiểm luồng container; không giả lập PASS hay chèn authority qua DB. Các GET/trace API trong phiên browser dùng để kiểm tra kết quả lưu sau thao tác, không thay thế mutation bằng UI.

## 1. P1 → P2 → CLOSE nhiều plan

Container synthetic `42ebd5c8-a36a-4f41-a9c6-5819d21ffb8d`, target 10 kg.

| Bước | Vai trò/thao tác trên web | Kết quả |
|---|---|---|
| 1 | Export load actual 4 kg theo P1 | LOADING, còn 6 kg; P1 giữ load gốc. |
| 2 | CM cập nhật quality observation hợp lệ, refresh container | Approval stale; actual 4 kg giữ nguyên. |
| 3 | Export chọn Head rice → Create draft → chọn facility → Calculate plan → Save proposal | UI Plan v2 có target đúng 6 kg; preview PASS. |
| 4 | CM chọn proposal P2, scope ALL_REMAINING, ghi reason và approve | P2 active; còn 6 kg để load. |
| 5 | Export load 6 kg theo P2 | Đủ 10 kg; CLOSE vẫn yêu cầu review lại scope P1 và metadata. |
| 6 | CM chọn P1, scope OUTPUT_ONLY, ghi reason và approve | Review mới cho actual đã load của P1; không gán load P1 sang P2. |
| 7 | Export lưu number/seal → Close container → xác nhận | CLOSED, không alert lỗi. |
| 8 | CM mở Read closure dossier | Dossier có hai loads thuộc hai proposal gốc và hai reviewedScopes; nguồn cộng đúng 10 kg. |

Kết quả lưu xác nhận lúc `2026-11-18T05:13:39.616Z` (UTC). P1 proposal `1a9ac9f9-9ea3-4977-8b16-52b4e95b5fd3`; P2 proposal `d03f1c58-e111-466a-8f1d-cdb89af1f9f4`. Cả hai có review reference riêng trong basis CLOSE.

## 2. Kho trộn → transfer → partial loading → bagging → CLOSE, không HOLD

Container synthetic `f3ef4bba-3747-4d7a-90e5-d4175fcedb13`, target 10 kg; pool ban đầu 20 kg từ hai nguồn.

| Bước | Vai trò/thao tác trên web | Kết quả |
|---|---|---|
| 1 | Operator mở Approved production/work item, lưu TRANSFER 10 kg từ pool sang ordinary store | Handoff DRAFT, không tự load pool. |
| 2 | CM ghi reason, Approve saved handoff | TRANSFER APPROVED. |
| 3 | Operator Commit reviewed handoff → xác nhận | TRANSFER COMMITTED, có aliquot thực 10 kg; pool successor giữ phần còn lại. |
| 4 | CM chuẩn bị quality actual aliquot và review OUTPUT_ONLY | Loading readiness PASS. |
| 5 | Export load `4.000001` kg | Giữ partial actual load. |
| 6 | Operator lưu BAGGING với input/output `5.999999` kg và zero loss; CM duyệt; Operator commit/xác nhận | BAGGING COMMITTED; output bagged có identity mới. |
| 7 | CM chuẩn bị quality output bagged và review actual outputs | Loading readiness PASS; không dùng lại PASS của aliquot cũ. |
| 8 | Export load `5.999999` kg, lưu number/seal → Close container → xác nhận | Đủ 10 kg, CLOSED, holds = 0. |
| 9 | CM mở dossier và Trace container sources | Hai loads giữ lot thực của từng bước; trace complete, hai source × `5.000000` kg. |
| 10 | Đọc stock scope facility/location trong phiên CM | Một current pool successor, revision 2, physical/available đều `10.000000` kg. |

Kết quả lưu xác nhận lúc `2026-11-18T05:15:51.886Z` (UTC). Không đặt HOLD, không release HOLD hoặc sửa fixture cũ để mở đường CLOSE.

## Bằng chứng và trạng thái bàn giao

[Bằng chứng JSON](container-s2-web-walkthrough-evidence-2026-11-18.json) lưu các ID synthetic, loads, scope refs, source shares, trạng thái handoff/CLOSE và pool remainder. Không chứa cookie/token/connection string. Các popup native là bước xác nhận Close container/Commit reviewed handoff của ứng dụng.

Hai walkthrough chưa chạy trước đây nay đã có kết quả web PASS. Đây không phải tuyên bố toàn bộ mọi biến thể CT-01–CT-30 đều được chạy lại. Sau lượt kiểm tra, tab và host do agent mở được đóng; file session test tạm được xóa.

**Tại thời điểm walkthrough, shared deployment chưa thực hiện.** Sau đó người dùng đã phê duyệt riêng migration/quyền S2 và chúng được áp dụng lên Supabase cấu hình hiện tại; xem [biên bản triển khai](container-s2-deployment-2026-11-18.md). Các kết quả walkthrough trên vẫn thuộc DB cô lập, không được gọi là full-flow test trên Supabase. Approval S1 không được dùng lại cho S2.
