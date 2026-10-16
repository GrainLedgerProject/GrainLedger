# Kế hoạch triển khai truy xuất nguồn gốc

Ngày bắt đầu: 17/10/2026. Căn cứ: [đánh giá khả thi](traceability-feasibility-review-2026-10-17.md), [kiến trúc](architecture.md), [hợp đồng Ledger](ledger-implementation-plan.md), SRS UC-24/25 và SDD mục 3.8.

## 1. Mục tiêu và trạng thái

Triển khai phần độc lập có thể làm ngay: **Ledger lot ↔ công đoạn đã commit ↔ nguồn thu mua/ruộng/vụ**. Đây là bước A của F18, chưa nghiệm thu toàn bộ truy xuất lab/plan/container.

- [x] Đọc và xác minh yêu cầu, source và giới hạn phụ thuộc.
- [x] Ghi kế hoạch trước khi thực hiện.
- [x] A1 — Protected API, đồ thị đầy đủ trong giới hạn công bố, persisted permission/owner, current/history, audit và bulk source contract.
- [x] A2 — Giao diện chọn lô/truy hai chiều, khối lượng nguồn và restriction.
- [x] A3 — Regression tests PostgreSQL cô lập, frontend tests, build và actual API/browser smoke.
- [x] A4 — Tài liệu API, kết quả kiểm chứng và hướng dẫn deployment.
- [ ] B — Lab/evidence dependencies; chờ nghiệp vụ lab/compliance.
- [ ] C — Plan/reservation/loading/container/dossier; chờ nghiệp vụ export.

## 2. Phạm vi bước A thực hiện trong đợt này

1. Input là **Ledger lot ID đã tồn tại** (bao gồm official source lot đã được nhận). SourceLot.Id của Capture là ID khác; chưa hỗ trợ plot/source-record search độc lập. Nguồn mới đăng ký nhưng chưa nhận chưa có Ledger lot, nên chưa có đồ thị stock để truy; không tạo stock hoặc tự nhận hàng để phục vụ trace.
2. Forward tìm hậu duệ thực của lô; backward tìm tổ tiên thực. Dùng input/output của operation, không nối mọi lô có cùng nguồn gốc. Transfer giữ lot identity, thể hiện qua positions và không tạo cạnh sản xuất.
3. `current` bỏ công đoạn bị thay thế; `history` giữ công đoạn COMMITTED/CORRECTED. Correction links là metadata riêng, không phải cạnh vật liệu ngược. Lô đã tiêu thụ vẫn có trong đồ thị.
4. Customer Manager và Administrator (grant global trace riêng) được hai chiều và nguồn đóng góp. ExportStaff chỉ forward affected-target trace; không cấp backward/source-contribution trace hoặc HOLD. Vai khác bị từ chối. Không sửa quyền đọc stock chung để cho Admin business wildcard.
5. Stock/quan hệ/attribution lấy từ một câu truy vấn theo tập; dùng READ COMMITTED. Metadata Capture chỉ lấy từ immutable procurement snapshot qua Capture-owned bulk contract, cùng connection/transaction. Audit thành công ghi TRACEBACK trong protected routine; lỗi enrichment rollback audit. Không cache hoặc lấy từng trang inventory làm tập truy xuất.
6. Khối lượng qua API là decimal string; attribution dùng số đã lưu. Tỷ lệ backend tính từ numerator/denominator của mỗi lô, chỉ là hiển thị; không cộng kg của mọi tầng làm tổng ảnh hưởng. Cạnh chỉ báo input consumed/output produced, không hứa kg attribution trực tiếp trên từng cạnh trung gian.
7. Giữ nguồn có contribution 0 sau làm tròn. Legacy thiếu Capture metadata báo rõ; không bịa nguồn. Không lấy farmer contact/plot geometry hoặc evidence file vào response.
8. Trả tập đầy đủ với thứ tự ổn định; guard tối đa 5.000 lô và 20.000 cạnh, vượt guard hoặc timeout phải báo lỗi, không trả kết quả thiếu với complete=true. Đây là giới hạn phiên bản đầu, chưa đạt benchmark toàn mùa vụ. Không có pagination của graph ở đợt này.
9. Có forward migration mới cho protected routine và runtime EXECUTE grant. Giai đoạn kiểm thử chỉ áp dụng vào Testcontainers. Migration shared DB cần phê duyệt riêng; sau đó người dùng đã yêu cầu chạy migration, kết quả tại mục 6.4. Không chạy seed.

## 3. Thứ tự thực hiện

- A1: thêm SQL routine/migration và finite runtime grant; Capture bulk snapshot port; API POST vì có audit, dùng cookie CSRF và bearer như pattern hiện tại.
- A2: thêm Traceability trong Ledger workspace cho đúng grant; dùng lot search có sẵn cho customer và nhập Ledger ID cho global Admin; trình bày graph bằng bảng quan hệ dễ kiểm tra, source kg/tỷ lệ, stock và correction riêng.
- A3: test branching/rejoin/same-root siblings, exact attribution/tiny mass, correction, full sets >100, scopes/Export backward denial/persisted revocation, legacy/archived metadata; kiểm actual HTTP/browser trên host test cô lập.
- A4: cập nhật plan theo kết quả thật và ghi rõ phần chưa chạy/chưa deploy.

## 4. Phần để sau và điều kiện mở lại

| Phần | Dependency còn thiếu | Điều kiện bắt đầu/nghiệm thu |
| --- | --- | --- |
| B1: lab result → nguồn | Sample, report/results, official supersession, knowledge time | Có contract sample/source-lot và immutable report IDs; không coi operational evidence là lab. |
| B2: evidence/cohort impact | Cohort membership snapshots, bound dependencies, propagation/assessment | Compliance cung cấp graph dependency evidence riêng; không giả cạnh vật liệu hoặc suy nồng độ từ mass share. |
| B3: risk invalidation | Plan authority và current assessment | Late evidence có STALE/risk blocks nguyên tử theo business rules; không tự tạo HOLD. |
| C1: affected plans | Export order/plan, INPUT/OUTPUT reservation | Có actual execution/provenance và versioned plan/review links. |
| C2: source ↔ container | Committed loading/allocation, actual loaded kg, container | Truy mọi loading qua mọi plan version; chốt partial loading allocation và rounding bảo toàn tổng. |
| C3: historical dossier/container HOLD | CLOSE assessment/snapshot, supplements, container restriction | Giữ closing snapshot, current risk tách riêng; HOLD target/authority đúng scope. |
| Source/plot search và pending source detail | Search contract riêng của Capture | Chốt UX và quyền, phân biệt SourceLot.Id/Ledger lot ID; không dùng pending expectation như stock. |
| Full-season benchmark/paged graph | Dataset và concurrency profile thống nhất | Đo tính đầy đủ, độ sâu/fan-in/out, DB/API/page và warm/cold riêng; chưa tuyên bố đạt 30 giây. |
| Truy tại knowledge time tùy ý | Recorded-time/version contract xuyên module | Có đủ dữ liệu tái dựng; current/history trong A không tương đương arbitrary as-of query. |

## 5. Tiêu chí hoàn thành bước A

API/UI dùng được trên dữ liệu đã nhận, đúng scope và phân biệt current/history; đi đúng nhánh kể cả contribution 0; attribution và stock không bị thay đổi. Source metadata lấy theo tập từ snapshot, legacy có thông báo thiếu. Test/build và smoke được ghi kết quả thật ở dưới. Không có lab/container giả, tự HOLD, đổi policy hay sửa lịch sử để demo.

## 6. Kết quả thực hiện

Đã hoàn thành bước A trong source. Hợp đồng đầy đủ: [traceability-api.md](traceability-api.md). B/C và các mở rộng ở mục 4 vẫn để sau, chưa coi là đã hoàn thành UC-24/25 toàn bộ.

### 6.1. Thay đổi đã làm

- `POST /api/ledger/trace` gọi protected routine mới; cùng transaction với audit và Capture-owned bulk immutable source read. Không thay đổi stock, postings cũ, policy hoặc HOLD.
- Migration `20261017000000_AddLotTraceability`, finite runtime EXECUTE grant; graph guard 5.000 lots / 20.000 edges và statement timeout 25 giây. Không có benchmark toàn mùa vụ trong đợt này.
- Ledger → Traceability: chọn lô, forward/backward theo quyền, current/history; phân trang trình bày sau khi nhận đủ graph; stock hiện tại, source expectation, source kg/tỷ lệ và correction refs tách rõ.
- Thêm 9 backend regression tests và 4 frontend tests. Browser test host có một chuỗi synthetic nhận 10 kg → sấy 8 kg, loss 2 kg để smoke luồng thật trên database cô lập.
- Một test Identity cũ được sửa kỳ vọng race: request thua khi hai Admin hạ quyền đồng thời có thể nhận 409 tại transaction guard hoặc 401/403 nếu session/role bị thu hồi trước auth. Siết assertion còn đúng một active Administrator và đúng một request thành công; không đổi logic auth ứng dụng.

### 6.2. Kiểm chứng thực sự đã chạy

| Kiểm tra | Kết quả |
| --- | --- |
| `dotnet build GrainLedger.slnx --no-restore` | PASS. |
| `dotnet test ... --filter FullyQualifiedName~LotTraceabilityTests` | PASS 9/9: scope, graph branches/rejoin, zero-rounded source, transfer, correction, >100 targets, legacy/archived origin, guard không trả partial/audit, concurrent stock snapshot. |
| `dotnet test GrainLedger.slnx` lượt cuối | PASS 253/253 trên PostgreSQL Testcontainers. Hai lượt trước gặp kỳ vọng 409 của Identity race test; test đã được sửa như mô tả trên trước lượt cuối. |
| `npm test` | PASS 57/57, gồm 4 test Traceability mới. |
| `npm run build` | PASS TypeScript và Vite production build. |
| T3 collaborative browser + isolated BrowserWorkflowHost | PASS chọn lô sau sấy → backward bằng UI; forward qua actual cookie/CSRF HTTP trả 2 lots, 1 edge, output/allocated 8.000000 kg, source expectation 10 kg; response no-store. |
| Browser authorization smoke | Foreign company trace trả 403; Administrator global backward trace trả 200 và ordinary lot detail vẫn 403. |
| Mobile viewport 390 px | Có bảng source/relationships và wrapper cuộn bảng, không tràn ngang toàn trang trong fixture smoke. Đây là kiểm tra bố cục, không phải benchmark page loading. |
| Liên kết tài liệu và `git diff --check` | PASS; không đưa session/cookie fixture vào repository. |

Build còn báo NU1902/NU1903 về dependency có sẵn; không nâng cấp dependency trong phạm vi tính năng này. Không đưa các timing build/test hoặc browser Development thành kết quả đạt NFR traceback 30 giây.

### 6.3. Trạng thái bàn giao và triển khai

Tại thời điểm hoàn tất bước A, migration/grant chỉ được áp dụng vào Testcontainers; browser/test bridge tạm đã dừng sau smoke. Sau đó người dùng đã phê duyệt migration database được cấu hình; xem mục 6.4. Chưa deploy ứng dụng, chưa commit hoặc push.

Giới hạn còn lại của bước A: accepted Ledger lot ID là điểm bắt đầu; chưa có source/plot search riêng, query as-of tùy ý, large/batch traversal vượt guard hay benchmark toàn mùa vụ. Metadata hiển thị là immutable IDs/rice variety/source measurements, không bổ sung mutable tên nông dân hoặc thông tin liên hệ. Những giới hạn này không được diễn giải thành hỗ trợ lab/container hoặc chứng nhận compliance.

### 6.4. Migration theo phê duyệt của người dùng

Sau yêu cầu trực tiếp “chạy migration đi”, đã thực hiện quy trình `--migrate` hiện có của host trong Development, dùng migration-owner connection được cấu hình, giữ nguyên `.env`/connection settings và không chạy seed.

- Preflight: runtime/migration trỏ cùng database, là hai login riêng; runtime validation pass. Identity/Catalogs/Capture không có pending migration; Ledger chỉ có `20261017000000_AddLotTraceability`.
- Migration process exit code 0; chỉ migration Ledger trên được áp dụng, kèm runtime grants theo `LedgerDatabase.ConfigureRuntimeRoleAsync`.
- Postflight chỉ đọc: cả bốn module không còn pending migration; routine `ledger.trace_lot(uuid,uuid,text,text)` tồn tại, restricted runtime có EXECUTE, runtime validation vẫn pass.
- Không gọi trace thực tế trên shared DB vì thao tác đó ghi audit, không chạy data-changing smoke hoặc tự tạo dữ liệu mẫu. Browser/API smoke trước đó vẫn thuộc database cô lập.
