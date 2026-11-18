# Nghiệm thu tracing container trên web

Trạng thái: đã chạy luồng chính qua CM/Export trên Supabase ngày18/11/2026; xem [kết quả và các ca NOT RUN](../reports/container-tracing-deployment-2026-11-18.md). Chỉ kiểm tracing sau khi code hoàn tất và migration/grant được duyệt, không chạy toàn backend hoặc tạo DB local.

## Điều kiện deployment cần duyệt

- Migration: `20261118010000_ContainerTracingFlowchart` dùng [SQL](../../backend/src/GrainLedger.Api/Data/Sql/ContainerTracingFlowchart.sql). Hai hàm có sẵn được thay definition (`ledger_trace_lot`, `ledger_container_targets`), thêm private bulk helper `ledger_trace_graph` và public scoped read `ledger_container_trace_graph`.
- Cấp **một** EXECUTE mới cho runtime hiện có: `grainledger.ledger_container_trace_graph(uuid,uuid)`. Private helper không cấp quyền runtime/PUBLIC. Không chạy cấu hình lại toàn bộ grants.
- Không thay bảng, seed, backfill, stock, reservations, CLOSE/dossier, HOLD hoặc business history. Giữ ACL/owner hai hàm thay definition; snapshot/backup trước deployment. Migration ghi EF history như bình thường.
- Phạm vi web được yêu cầu: đăng nhập tài khoản test đã có, trace dữ liệu hiện hữu. Trace có ghi audit TRACEBACK và đăng nhập ghi session/audit bình thường; không tạo/load/close/HOLD container để dựng test case.
- Các regression backend dùng Testcontainers được bổ sung để chạy trong CI khi cần; lượt này không chạy chúng theo yêu cầu không dùng DB local. Build test project không khởi chạy fixtures.

## Bộ dữ liệu hiện hữu và oracle

Dùng source A `RAW-20261117-000018` (UUID `10770c77-aac8-5592-838d-5280bbfd0bfb`), source B `RAW-20261117-000024`, finished lot `BLD-20261117-000064` (UUID `b3cf913a-f6bd-5916-956c-6bce85392aed`). Lot được sản xuất 20 kg với 10+10 kg source composition lịch sử. Stock hiện tại có thể hết sau buổi demo; không cần tồn khả dụng để trace.

Dùng **CTN-20261118-000024**, UUID `983e96fa-c233-4792-8d8e-f74c0f44cadf`, người dùng đã load4+6kg và CLOSE trong buổi demo; đối chiếu metadata `DEMO-CONTAINER-20261118-01` nếu còn đúng. Không tự gán một UUID container chưa kiểm tra. Container cũ `CTN-20261117-000014` có thể cùng hiện, là manifest khác hợp lệ.

## Các bước web

1. Mở `http://localhost:5173`, đăng nhập CM `customer.manager@grainledger.local` bằng thông tin tài khoản đang có. Không đưa password/token/cookie vào tài liệu hoặc log.
2. Ledger → Traceability → Find a tracing starting lot. Chọn **Procurement sources**, All permitted facilities/locations, tìm và chọn `RAW-20261117-000018`. Giữ **Forward — recorded descendants**, **Current — effective operations**, Run trace.
3. Đối chiếu bảng **Actual container targets** với manifest hiện hữu: mã container, status, linkedLoadedKg và restrictions. Chỉ count actual loading; draft/reservation không trở thành target.
4. Chọn Flowchart. Kiểm chuỗi source → DRY → HDR → BLD → container; các node container có mã, status, HOLD/restriction. Không nối RAW trực tiếp vào container khi actual lot load là BLD. Cạnh BLD → container mới là **10.000000 kg loaded**. Một node cho mỗi container, hai load không tạo node lặp.
5. Chuyển Table rồi Flowchart, chọn lot, zoom, Fit graph, Focus starting lot. Không phát sinh thêm trace request/audit khi chỉ đổi display. Dùng Tab để tới link container, Enter để mở đúng `/export/containers/{id}`.
6. Tại đúng container mới, xác nhận committed manifest hai lần 4 và 6 kg, CLOSED/actual10. Không bấm các nút thay đổi metadata/HOLD/loading/close trong lượt này.
7. Bấm **Trace container sources**. Bảng **Actual container sources** phải đúng hai nguồn 5.000000 kg + 5.000000 kg (50% + 50%), total10; source/provenance rõ ràng hoặc incomplete có finding thật.
8. Ở **Table**, xem historical ancestry: BLD produced20 kg; actual loading relationships10 kg với hai load refs4/6 và proposal/location gốc. Không gọi20 kg là khối lượng đã load vào container mới.
9. Chọn **Flowchart** trong container trace. Node **STARTING CONTAINER** ở phía phải, nguồn bên trái; focus starting container đặt keyboard focus đúng node. Có tất cả actual-loaded lots/ancestry; không kéo container014 hoặc container khác vào backward graph này.
10. Chọn BLD trong graph để xem historical produced20 kg. Actual container sources vẫn giữ5+5 kg riêng. Đổi Table/Flowchart không gọi thêm trace API. Nếu graph quá lớn, Show table vẫn cho xem đầy đủ dữ liệu qua pagination.
11. Quay lại Ledger → Traceability, chọn **All recorded lots, including history**, tìm BLD đã hết tồn, chạy Backward. Chỉ thấy ancestry upstream, không có downstream container. Đây là hành vi đúng.
12. Đăng nhập Export `export@grainledger.local`: forward graph/actual targets vẫn dùng được; không lộ source allocations/lab/private evidence mới. Không có backward container source control; gọi trực tiếp endpoint backward bằng session Export phải403.
13. Nếu có tenant B/fixture partial/multi-plan/pool/no-load đã tồn tại và tài khoản sẵn có thì kiểm thêm; nếu không, ghi **NOT RUN**. Không tự tạo shared fixtures. Các ca micro-kg/P1-P2/multi-lot phải được phân biệt với fixture4+6 demo, không suy đã pass từ một container.
14. Kiểm desktop/mobile (375px), graph cuộn trong vùng riêng, focus và link dùng được. Ghi lỗi JS/network liên quan tracing nếu có; không đưa secrets vào bằng chứng.

## Ghi kết quả

Tách riêng: frontend focused tests/build; backend compile; migration/grant đã/chưa áp dụng; actual API/browser đã/chưa chạy. Mỗi ca ghi PASS/FAIL/NOT RUN và lý do. Chưa đo benchmark thì không nhận đạt NFR30s toàn mùa vụ.
