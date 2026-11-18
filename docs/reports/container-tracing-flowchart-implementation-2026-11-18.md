# Container tracing Flowchart — implementation và bước chờ duyệt

Ngày 18/11/2026. **Code hoàn tất; focused frontend checks/build PASS. Migration đã áp dụng và luồng web chính đã nghiệm thu trên Supabase; xem [deployment/web report](container-tracing-deployment-2026-11-18.md), gồm sự cố ACL tái diễn chưa xác định nguồn.** Không commit/push.

## Đã triển khai

- Forward lot/source trace thêm node container theo actual load lines, giữ source-positive filtering; một cạnh lot/container tổng exact kg và references mọi load/location/proposal. Node mở đúng hồ sơ container. Giữ Actual container targets để đối chiếu; linked kg không được diễn giải là source-attributed kg.
- CM dùng Trace container sources trên hồ sơ để xem Table/Flowchart backward từ container. Bulk traversal hợp nhất ancestry toàn actual manifest, kể cả loads từ nhiều plans; không kéo container khác dùng cùng lot vào graph này.
- Container root có namespace/type riêng, focus/keyboard navigation. Backward từ lot không thêm downstream container; starting-lot picker giữ phạm vi lot.
- Khối lượng historical lot created, actual loading và actual source contribution có nhãn riêng. Table sources vẫn hiển thị cạnh graph để đối chiếu; dữ liệu mass giữ decimal strings và allocations đã persist. Không tính mass bằng JS Number.
- Giới hạn 250 nodes/500 relationships tính cả container/loading; kiểm thêm 500 connections vẽ sau khi gom processing/custody. Không cắt graph để báo complete; fallback Table có pagination và đầy đủ loading refs.
- Empty loading có standalone container và finding, missing source ancestry/provenance báo incomplete. Failed/stale inspection không giữ graph cũ như kết quả mới.
- Giữ role/company scope, Export forward redaction, cùng REPEATABLE READ cho graph/loads/provenance và fresh authority recheck. Một TRACEBACK audit mỗi lần container trace, không audit từng ancestry lot hoặc đổi Table/Flowchart.

## Kiểm tra thực tế đã chạy sau khi hoàn tất code

| Kiểm tra | Kết quả |
|---|---|
| Web focused tests: trace-flow, LedgerTraceability, ContainerSourceTrace, ContainerExecution, TraceStartPicker, TraceSourceEvidence | **44/44 PASS**, 6 files. |
| `npm run build --workspace @grainledger/web` | PASS. Cảnh báo chunk >500 kB; không đo hoặc nhận đạt page-performance/NFR. |
| Build backend integration-test project `--no-restore` | PASS, API và test project compile; 3 warnings sẵn có ở LedgerService/Residue tests. Đây **không** phải thực thi backend tests. |
| EF migration script S2 → ContainerTracingFlowchart | Sinh thành công bằng design-time connection giả, không mở DB. EF tools10.0.12 cảnh báo thấp hơn runtime10.0.13. |
| Links plan/web guide; `git diff --check` | PASS. |
| Backend integration/Testcontainers | NOT RUN, không tạo DB local theo yêu cầu. Hàm SQL mới đã chạy qua actual shared API/web theo deployment report. |
| Shared migration/grant, runtime validation/API smoke, actual browser login/test | Đã thực hiện sau phê duyệt; [kết quả và giới hạn](container-tracing-deployment-2026-11-18.md). |
| Shared partial/multi-plan/pool/tenant B/large graph runtime/benchmark | NOT RUN. Không seed hoặc mutate để dựng fixture. Frontend fixtures không chứng minh backend accounting/authorization. |

Các regression backend mới ở [ContainerTracingFlowchartTests](../../backend/tests/GrainLedger.Api.IntegrationTests/ContainerTracingFlowchartTests.cs) kiểm partial micro-kg vs historical20kg, actual allocations5+5, audit count, hai container, Export redaction và private helper denial. Chúng chỉ chạy với ApiFactory/Testcontainers khi được phép sử dụng CI/isolated test environment; không cấu hình chúng trỏ vào Supabase.

## Gói migration cần duyệt

[Migration 20261118010000_ContainerTracingFlowchart](../../backend/src/GrainLedger.Api/Data/Migrations/20261118010000_ContainerTracingFlowchart.cs) nạp [ContainerTracingFlowchart.sql](../../backend/src/GrainLedger.Api/Data/Sql/ContainerTracingFlowchart.sql).

| Đối tượng | Thay đổi |
|---|---|
| `ledger_trace_lot(uuid,uuid,text,text)` | CREATE OR REPLACE wrapper: bulk graph chung, giữ route/payload lot cũ và một audited event với lot_id. Giữ existing owner/ACL. |
| `ledger_container_targets(uuid,uuid,uuid[])` | CREATE OR REPLACE: thêm grouped actual loading edges/line refs, giữ aggregate targets/filter/restriction; giữ existing owner/ACL. |
| `ledger_trace_graph(uuid,uuid[],text,text)` | Private bulk helper dùng chung; runtime/PUBLIC không EXECUTE. |
| `ledger_container_trace_graph(uuid,uuid)` | Public scoped CM/Admin read cho graph container; cần **một** runtime EXECUTE mới. PUBLIC không EXECUTE. |
| `grainledger.__EFMigrationsHistory` | Một migration row theo EF script. |

Không thêm bảng, index, seed, backfill, thay row nghiệp vụ, stock/reservation, HOLD, CLOSE hoặc dossier. Không cần đổi `.env`, role hay cấu hình lại toàn bộ grants.

Review artifact đã sinh ngoài Git: `/tmp/grainledger-container-tracing-review/migration.sql`, SHA256 `95130cf6d581a4e585b7f8930bfd3736a6d7a5c0d602fa6f6a01be4fb753c1b4`. Script chỉ từ `20261118000000_ContainerWorkflow` tới migration tracing, chưa chứa environment-specific GRANT; chưa thực thi SQL bodies trên PostgreSQL.

Sau khi được duyệt: kiểm đúng migration head/owner/runtime hiện tại, backup bảo vệ và snapshot ACL/business hashes, áp dụng đúng script + một EXECUTE trong transaction READ COMMITTED có advisory lock714928352 và timeouts; không chạy broad migration/grant configuration. Đối chiếu history/ACL/data, validate restricted runtime, khởi động matching API/web rồi thực hiện [web walkthrough](../guides/container-tracing-flowchart-web-test.md) bằng fixture hiện hữu. Login/trace chỉ ghi session/audit thông thường, không tạo/load/close/HOLD container mới.

## Các bước deployment ban đầu — đã thực hiện sau phê duyệt

1. Người dùng duyệt đúng migration và single EXECUTE mới trên Supabase dùng chung. [AGENTS.md](../../AGENTS.md) yêu cầu: “Obtain explicit approval before running migrations, seeds, schema/role/grant changes, or data-changing smoke scenarios against them.”
2. Áp dụng và kiểm tra runtime bằng quyền hiện tại; xử lý lỗi SQL/runtime nếu xuất hiện bằng thay đổi có kiểm soát. Trạng thái ban đầu này đã được cập nhật bằng actual SQL/API/web proof trong deployment report.
3. Đăng nhập web và chứng minh source → container node → hồ sơ → container backward graph; đối chiếu source5+5 và historical20kg trên container demo hiện hữu. Ghi PASS/FAIL/NOT RUN cho các case khác theo dữ liệu/quyền thực có; không tự claim toàn bộ matrix đã pass.
