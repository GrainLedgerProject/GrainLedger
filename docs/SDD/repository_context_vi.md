# Context repository dùng cho SDD revision 0.3

Repository được đọc: `/home/phucpinku/Desktop/Git/GrainLedger`. Chỉ đọc source/docs; không sửa file, không chạy ứng dụng/build/test/migration/seed, không gọi API hoặc kết nối database. Không đọc `.env` hoặc in credentials.

Theo xác nhận của bạn, mã nguồn cung cấp context kỹ thuật; **SDD vẫn thiết kế đầy đủ các luồng chưa hoàn thành**, không bị thu hẹp thành báo cáo những gì đã code.

## Căn cứ trực tiếp

| Nguồn trong repository | Context lấy cho SDD |
| --- | --- |
| `AGENTS.md`, `README.md`, `docs/architecture.md` | Modular monolith, stack, quyền, module/schema, numeric/transaction contracts và các phần chưa triển khai. README còn ghi migration dùng chung cần phê duyệt; SDD không tự coi source migration là đã deploy. |
| `GrainLedger.slnx`, `backend/src/GrainLedger.Api/Program.cs` | Một host ASP.NET Core 10; module Identity, Catalogs, Capture, Ledger; Minimal API mappings; auth, antiforgery, same-origin frontend. |
| `apps/web/package.json`, `src/api.ts`, các file `.tsx` | React/TypeScript/Vite; src hiện phẳng; wrapper cookie/CSRF/session và ProblemDetails; không có cây Controllers/Services/Repositories hoặc frontend package giả định trong bản cũ. |
| `apps/mobile/package.json` | Package placeholder, không phải native app đã hoạt động. |
| `Identity/Domain`, `Features`, `Authorization`, `Infrastructure` | User/Organization/Session, role và persisted grants, email-password/Google, SMTP, EF Core IdentityDbContext. Không coi Administrator là quyền wildcard. |
| `CatalogDbContext.cs`, `MrlVersionEndpoints.cs`, `docs/api/mrl-api.md` | Các class MrlVersion/MrlLimit hiện hữu, source/effectivity/ETag và publish; predecessor interval được đóng có audit tương quan. Resolve MRL chưa phải đánh giá residue hoặc approve plan. |
| `Capture/Procurement.cs`, `ProcurementEndpoints.cs`, `CaptureDbContext.cs` | Procurement/ProcurementItem/SourceLot/CaptureOperation; source expectation và idempotency; source Status COMMITTED khác outward receiving state. |
| Hai file `ProcurementLedgerPort.cs` ở Capture và Ledger | Capture sở hữu interface; Ledger implement. Runtime qua port dùng cùng connection và caller transaction; EF entity không truyền qua boundary. |
| `LedgerEndpoints.cs`, `LedgerService.cs` | Minimal API → CommandAsync / ReadAsync / ReceivingAsync → Npgsql → protected SQL. Không dựng một tầng MVC Controller/Repository có sẵn giả tạo. |
| `Ledger/Sql/Schema.sql`, `Operations.sql`, `Commands.sql`, `PhysicalReceiving.sql`, `CorrectionBinding.sql` và các forward SQL resources | Logical SQL records, nhận thực tế, stock/postings/lineage, signed variance, HOLD, evidence, correction/replacement và protected routines. Phải đọc cùng forward changes, không dùng Initial Schema riêng làm schema cuối. |
| `Dockerfile` | Build frontend + .NET host thành container. Không tự suy ra pipeline GitHub Actions hoặc vendor hosting từ mẫu HomeCycle. |

## Những thay đổi quan trọng

1. **Kiến trúc/package:** bỏ cấu trúc API/Application/Domain/Infrastructure chung được gán như project thật. SDD dùng đúng host/module hiện có; các logical capability Compliance/Export được ghi planned và chưa chốt project/namespace. Identity có các thư mục phân lớp nội bộ, không được suy ra mọi module có cùng cây thư mục.
2. **Frontend:** gọi đúng App, api, LocalAuth, Administration, CatalogScreen, LedgerWorkspace/Receiving/Operations/Stock/Setup và shared ledger-ui. Local durable offline/sync/reconciliation và compliance/export screens vẫn có thiết kế planned.
3. **Class:** User/Organization, MrlVersion/MrlLimit và các Capture class dùng tên/thuộc tính thực. Existing property-only EF entity không bị gán phương thức nghiệp vụ đã implement giả. Các logical SQL/target class được ghi riêng; không tuyên bố Conversion, StockReservation hay mọi entity trong ERD đều là CLR class hiện có.
4. **Sequence:** các phần hiện có dùng tên Endpoint mappings, DbContext, LedgerService, in-process port, Npgsql và protected SQL; các phương thức của phần tương lai được ghi planned. Vẫn giữ đánh số, alt/opt và caption theo Sources.docx.
5. **Numeric/transaction:** decimal string ở API; kg numeric(20,6); giữ số đo/đơn vị gốc; không JS float cho khối lượng. Source/stock dùng READ COMMITTED, advisory lock 714928352 trước đọc dòng, cùng connection/transaction qua module port. MRL publication có lock/ETag riêng; không gán stock-write contract cho mọi read-only query.

## Đăng ký thu mua không đồng nghĩa có hàng tồn

Đây là khác biệt hành vi đáng kể với SRS/ERD conceptual trước đó, không chỉ đổi tên lớp. Revision 0.3 mô tả rõ context nhận thực tế của repository, đồng thời giữ ghi nhận rằng SRS/use case narrative cần đồng bộ ở lần sửa nguồn riêng; lần này không sửa SRS, proposal hoặc ERD.

- PurchasingStaff đăng ký Procurement, các item và source identity bằng một CaptureOperation có key/hash. Source Status là COMMITTED; kết quả outward là PENDING_ARRIVAL, usable stock bằng 0.
- Capture gọi `RegisterPendingAsync` qua Ledger port trong transaction đang có; không tạo ledger lot/position/posting usable ở bước đăng ký.
- MillOperator được phân công nhập **một bộ actual measurement đầy đủ cho tất cả dòng**. Exact match mới post actuals và trả RECEIVED_MATCHED.
- Có sai lệch thì AWAITING_MANAGER_REVIEW, vẫn chưa có stock. MillManager có reason/evidence đúng company/facility có thể approve shortage khi mọi actual không vượt expected và có ít nhất một dòng thiếu; chỉ actual dương được post. Có thể yêu cầu cân lại hoặc keep blocked.
- Không thêm surplus approval, partial arrival, transport settlement, receiving tolerance hoặc truck model. Mỗi procurement là một complete arrival; legacy marker NULL và stock lịch sử không bị diễn giải lại.
- Idempotency APPLIED là đăng ký source đã nhận; không phải phép xác nhận đã có stock. Reconnect không được tự post số dự kiến.

## Phần chưa hoàn thành vẫn nằm trong SDD

Lab/sample/cohort, conservative residue bounds và propagation; assessment PASS/FAIL/INCOMPLETE; export order/plan/review; INPUT/OUTPUT reservations và correction accounting; loading/CLOSE/dossier; full lab-to-container và container-to-plot trace; container HOLD và ảnh hưởng downstream; full offline lifecycle — tất cả vẫn là target design.

Current `ledger.evidence` là bằng chứng operational tổng quát, không chứng minh module xét nghiệm đã có. Physical BLENDING không chứng minh export compliance. Root-source contribution/history không đồng nghĩa full container traceback. Current HOLD target là lot và không tự cascade descendants; thiết kế target có container/authority effects phải triển khai thêm.

Các dependency/namespace planned là lựa chọn thiết kế trong cùng modular-monolith boundary, không giả nhận project `.csproj` hoặc endpoint đó đã tồn tại. Không bỏ nghiệp vụ chỉ vì chưa tìm thấy code.
