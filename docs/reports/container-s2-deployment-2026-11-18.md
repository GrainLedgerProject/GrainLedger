# Container S2 — triển khai Supabase và phân biệt dữ liệu, 18/11/2026

Đã áp dụng migration `20261118000000_ContainerWorkflow` và cấp riêng EXECUTE trên `grainledger.ledger_container_workflow(uuid,uuid,text,jsonb,jsonb)` cho runtime hiện có, theo yêu cầu rõ ràng của người dùng: “chạy migration đi, cấp quyền luôn”. Kiểm tra sau triển khai hoàn tất lúc `2026-11-18T05:25:29.9603361Z` (12:25:29, Việt Nam).

## Phạm vi đã thực hiện

- Preflight bằng owner/runtime hiện có xác nhận cùng Supabase database/schema, role runtime không elevated, migration mới duy nhất đang chờ là S2. Trước đó latest là `20261117000000_ContainerProduction`.
- Tạo backup custom-format của schema `grainledger` và dữ liệu trước migration: **1.304.322 bytes, 1.264 entries**, kiểm tra archive listing và SHA-256. Đây là backup application schema; không phải backup toàn Supabase project và chưa diễn tập restore.
- Sinh EF script chỉ từ S1 đến S2, áp dụng cùng **một** EXECUTE grant trong transaction READ COMMITTED giữ stock advisory lock `714928352` trước DDL; lock timeout 10 giây, statement timeout 120 giây.
- Không chạy application `--migrate` hoặc `--configure-runtime-role`, vì các lệnh đó cấu hình lại toàn bộ grant set. Không tạo role mới, đổi `.env`, connection settings, reset/seed/copy dữ liệu hoặc chạy business mutation smoke.
- Đối chiếu hash dữ liệu **114 bảng nghiệp vụ cũ** trước/sau trong cùng transaction: không thay đổi. Với `exports_blend_plan_decisions`, hai cột generated `previous_version`/`previous_action` được loại khỏi phép hash ở cả hai phía vì migration bỏ chúng theo thiết kế; dữ liệu decision gốc vẫn được đối chiếu.
- Owner/ACL của **117 đối tượng table/sequence/view và 128 function cũ** giữ nguyên. Chỉ entry workflow mới nhận EXECUTE; runtime không có quyền trực tiếp trên 10 bảng mới hoặc EXECUTE trên 6 helper mới.
- Cả 10 bảng mở rộng S2 đều rỗng sau deployment, phù hợp với việc chưa có actual S1 production output/review để backfill trên database này. Không có fixture local S2 được chuyển sang Supabase.

Script EF SHA-256: `b4e55357445811289a932100d6bdd560f196e9756e5bd751166efd679ad4ab4c`.

Backup SHA-256: `382a68bfa0bc499f09af4098fbc11ae83077265c8475857faad919c0f68d02fb`.

## Kiểm tra sau triển khai

| Kiểm tra | Kết quả thực tế |
|---|---|
| Migration history | Chỉ thêm S2; giữ toàn bộ history trước đó. |
| Runtime validation | PASS bằng restricted runtime connection, không dùng owner để chạy app. |
| Khởi động matching API | PASS tại localhost:5080; không có seed/migrate flag. |
| `GET /health` | HTTP 200, `{"status":"ok"}`. |
| Anonymous notifications/work-items | Hai endpoint đều HTTP 401, đã map và yêu cầu authentication. Không coi đây là authenticated full-flow test. |
| Read-only SQL smoke theo scope CM của fixture cũ | Workflow detail, execution detail, production detail và dossier đều đọc được bằng runtime. |
| Fixture `CTN-20261117-000004` | Vẫn CLOSED, loaded `10.000000` kg, production `NOT_REQUESTED`, dossier có một source contribution. |
| `.env` | Hash trước/sau bằng nhau. |

Một probe trace trong READ ONLY bị PostgreSQL chặn `25006` vì trace có ghi audit TRACEBACK; transaction đó đã rollback. Không nới read-only để chạy lại. Smoke thành công dùng detail/dossier; không tuyên bố đã chạy trace audit hoặc full S2 mutation trên Supabase.

Không chạy lại backend suite cho deployment. [68 regression chức năng và benchmark](container-s2-implementation-2026-11-18.md) cùng [hai walkthrough web bổ sung](container-s2-web-walkthroughs-2026-11-18.md) là kết quả trên DB test cô lập. Khởi động/ACL/read smoke sau migration không đồng nghĩa mọi dữ liệu Supabase đều đủ điều kiện thực hiện luồng S2. API local matching build đã khởi động; không xác nhận rollout hay phiên bản của các API instance từ máy khác.

## Dữ liệu trước đây nằm ở đâu?

**Bộ demo container được yêu cầu thêm trước đây nằm trên Supabase**, không chỉ trên local DB. [Báo cáo web ngày 17/11](container-web-test-execution-2026-11-17.md) ghi rõ API local dùng restricted runtime connection tới Supabase, cùng các mã `PRC-20261117-0003`, `SMP-20261117-0002`, `EXP-20261117-000004`, `CTN-20261117-000004`. [Lượt CT-11–CT-30](container-web-test-remaining-2026-11-17.md) tiếp tục tạo fixture qua API trong cùng môi trường được người dùng cho phép.

Preflight lần này tìm thấy trực tiếp container `14d858b8-97df-4ee1-b175-b327819a55d3` / `CTN-20261117-000004` và sample `SMP-20261117-0002` trên Supabase cấu hình hiện tại. Tổng database lúc preflight có **23 containers, 13 loads, 2 closures**. Đây là kiểm tra một số dấu mốc chứng minh dữ liệu demo cũ đang ở DB chung, không phải đối chiếu toàn bộ mọi fixture cũ.

**Dữ liệu regression/walkthrough S1/S2 gần đây là fixture riêng trong PostgreSQL Testcontainers**, được tạo theo từng ca, không clone Supabase. Các container hai plan, pool/bagging, expiry/tenant-B và dataset 1.000 container được kiểm trên DB cô lập; các DB/session tạm đã đóng sau test. Không có cơ chế tự sync sang Supabase.

`localhost:5173`/`:5080` chỉ là địa chỉ chạy web/API trên máy. Nó không xác định database: ứng dụng thông thường vẫn dùng Supabase trong cấu hình; test host mới thay bằng DB Testcontainers riêng, không sửa `.env` của ứng dụng.

Sau migration, hai môi trường có cùng phiên bản schema/logic container tương ứng build, **không có cùng business data**. Pass local xác nhận behavior trên fixture có đủ điều kiện; Supabase vẫn phụ thuộc stock, approvals, evidence, policy/MRL, HOLD và assignment thật đang lưu. Migration không tự tạo fixture mới, phục hồi fixture đã HOLD/CLOSED, hoặc cấp business-role/facility assignment. Nếu cần nghiệm thu ghi dữ liệu S2 trên DB chung, phải dùng fixture/phạm vi được phép riêng; lượt này chỉ triển khai migration/grant và kiểm tra đọc.

## Artifacts và recovery

Artifacts bảo vệ ngoài Git tại `/home/phucpinku/.local/share/grainledger-backups/container-workflow-20261118/`: archive, checksum metadata, reviewed script, snapshots ACL/hash trước/sau và verification. Thư mục 0700, file 0600; không publish backup hoặc connection/session credentials. Không restore, down migration, commit/push hay chuyển dữ liệu giữa hai database. Recovery theo [deployment package](../guides/container-s2-deployment.md); không rollback code đơn lẻ sau khi đã phát sinh lịch sử S2.
