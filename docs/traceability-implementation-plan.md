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
- [ ] B — Lab/evidence dependencies; đã có sample/handover sau merge dev, chưa có lab result/compliance; xem mục 7.
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

### 6.5. Mở rộng hiển thị Flowchart

Theo yêu cầu bổ sung graph cho Trace result và Processing relationships, giao diện có lựa chọn **Table / Flowchart** trên cùng kết quả. Sơ đồ kết hợp lô → công đoạn → lô, hỗ trợ nhánh tách/gộp, chọn lô, zoom/fit và focus lô bắt đầu. Mũi tên giữ chiều vật liệu cho cả forward/backward. Table cũng đưa Starting lot lên đầu thay vì theo UUID.

Không đổi API, số học khối lượng hoặc quyền; không thêm dependency/migration. Không tự vẽ correction thành cạnh vật liệu. Flowchart lớn hơn 250 node hoặc 500 quan hệ API chuyển sang thông báo dùng Table cho toàn bộ kết quả; không cắt bớt graph âm thầm. Chi tiết sử dụng tại [API/UI contract](traceability-api.md#table--flowchart-trên-web).

Kiểm chứng bổ sung đã chạy:

- `npm test`: PASS 65/65 (8 regression tests mới cho topology, bảng/flowchart, lựa chọn node, zoom, scope nguồn và fallback sơ đồ lớn).
- `npm run build`: PASS TypeScript và Vite production build; không thêm dependency hoặc thay lockfile.
- T3 browser + BrowserWorkflowHost/Testcontainers cô lập: tạo MILLING hợp lệ từ 8 kg lúa khô thành bốn đầu ra và loss 0,5 kg; forward hiện 5 node lô + 1 node công đoạn với 5 mũi tên. Backward từ cám hiện 3 lô + 2 công đoạn, mũi tên vẫn theo chiều vật liệu, lô bắt đầu nằm bên phải và được đánh dấu.
- Chọn node cám cập nhật chi tiết stock/source; chuyển Table/Flowchart giữ nguyên số request trace (không audit mới). Table đưa Starting lot lên đầu. Mobile 390 px cuộn graph trong khung, không tràn ngang toàn trang; không có console error hoặc nội dung tràn card trong fixture.
- `git diff --check` và link tài liệu: PASS. Không chạy lại toàn bộ backend suite vì phần mở rộng chỉ thay frontend/docs; browser dùng API thật của host test cô lập.

Không chạy trace, seed hoặc migration trên Supabase dùng chung trong đợt mở rộng UI này. Môi trường browser/test bridge tạm được dừng sau smoke. Phần mở rộng Flowchart được tách thành commit riêng trước khi bổ sung chi tiết nguồn ruộng/vụ; chưa push từ phiên làm việc này.


### 6.6. Chi tiết nguồn ruộng/vụ

Đã bổ sung **View field / season origin** trong Source composition cho từng nguồn của lô đang chọn, dùng được từ Table và Flowchart. Chuỗi trình bày nông dân → thửa/vụ, giống → phiếu/dòng thu mua → lô nguồn; giữ allocated kg/tỷ lệ riêng với source expectation. Không dựng thao tác đăng ký thửa thành công đoạn vật liệu.

Capture bổ sung `currentReferenceLabels` theo tập (mã/tên nông dân, mã thửa, tên vụ), join theo ID từ immutable snapshot và cùng owner, giữ reference đã lưu trữ. ID/giống/khối lượng nguồn không bị thay bằng giá trị danh mục; UI ghi rõ nhãn hiện tại và báo unavailable khi thiếu. Quyền nguồn vẫn là Customer Manager đúng owner hoặc Administrator; Export Staff không nhận nguồn. Không thêm endpoint, dependency, schema, migration hoặc grant.

Kiểm chứng đã chạy:

- `npm test`: PASS 67/67; thêm trường hợp mở nguồn từ Table/Flowchart không fetch lại, giữ exact zero allocation riêng với kỳ vọng và fallback ID khi thiếu nhãn.
- `npm run build`: PASS TypeScript/Vite.
- `dotnet test backend/tests/GrainLedger.Api.IntegrationTests --filter FullyQualifiedName~LotTraceabilityTests`: PASS 9/9 trên Testcontainers; xác minh nhãn hiện tại khi đổi tên/lưu trữ vẫn giữ nguyên nguồn snapshot, quyền/scope, không lộ contact/geometry và legacy rõ ràng. Lần đầu fixture cập nhật nhiều lệnh dùng positional parameters bị PostgreSQL từ chối; đã sửa thành một statement CTE và chạy lại toàn bộ nhóm pass. Backend build kèm test pass; các warning Pkcs NU1902/NU1903 và CS9113 có sẵn vẫn xuất hiện.
- Browser với API thật qua BrowserWorkflowHost/Testcontainers: Customer Manager backward từ lô khô 8 kg, mở nguồn thấy nông dân/mã thửa/vụ/giống/phiếu/source lot; source expectation 10 kg tách biệt allocation 8.000000 / 8.000000 kg. Chuyển Flowchart giữ chi tiết mở, 3 node vật liệu/công đoạn, vẫn một trace request.
- Browser test host hỗ trợ `GRAINLEDGER_BROWSER_API_PORT` / `GRAINLEDGER_BROWSER_WEB_PORT` tùy chọn (mặc định 5080/5173), chỉ bind localhost. Smoke dùng 5081/5174 vì cổng mặc định đang được sử dụng; không dừng server của người dùng. Config Vite tạm không đưa vào repository.

Không migrate/seed hoặc chạy data-changing smoke trên Supabase dùng chung. Phần ruộng/vụ và kiểm tra runtime grant được đưa vào commit tiếp theo; commit Flowchart trước đó là `7023cf4` (`feat(web): add traceability flowchart view`). Chưa push từ phiên này.

Phụ thuộc để làm sau: lịch sử đăng ký/sửa hồ sơ và historical label snapshot chưa có hợp đồng đọc audit dành cho trace; không suy diễn thời điểm/người đăng ký. Lab, compliance, plan/reservation/loading/container vẫn theo phần phụ thuộc trước đó.


### 6.7. Phát hiện runtime grant trace bị thiếu

Sau báo lỗi `permission denied for function trace_lot`, kiểm tra read-only trên database cấu hình xác nhận hàm và migration trace tồn tại, runtime login đúng role cấu hình, nhưng không có EXECUTE trace; command EXECUTE vẫn có. Chưa xác định tác nhân/thời điểm thay ACL. Đây không phải thay đổi quyền nghiệp vụ Customer Manager.

Đã bổ sung startup validation cho hàm/grant trace, không tự sửa database. Regression trong `CaptureCutoverMigrationTests` thu hồi riêng EXECUTE trace trên runtime test cô lập, xác minh startup từ chối với lỗi rõ ràng, cấu hình lại runtime grants rồi xác minh validation pass. Chạy nhóm này: PASS 1/1 (backend build kèm test pass; warning có sẵn vẫn còn). `git diff --check`: PASS.

Sửa database dùng chung cần phê duyệt riêng: chỉ GRANT EXECUTE trên `ledger.trace_lot(uuid,uuid,text,text)` cho runtime role cấu hình, không migration/seed hoặc business data smoke. Sau phê duyệt rõ ràng của người dùng, đã áp dụng đúng một GRANT EXECUTE trên signature này cho runtime role cấu hình. Preflight xác nhận cùng host/port/database, migration login riêng và effective PostgreSQL runtime role khớp cấu hình (pooler login có thể kèm suffix). Postflight qua runtime connection: `has_function_privilege(..., EXECUTE)=true`, `ValidateRuntimeAsync` PASS. Không chạy migration, seed hoặc trace POST ghi audit trên shared database.


## 7. Đánh giá điều kiện tiếp tục sau merge dev — 20/10/2026

Đối chiếu tại merge `dc2b31b` (cha trace `3e8039c`, dev gồm field capture/sample custody, MRL import, unified lot codes và receiving thresholds). Kết luận: có thể thiết kế/triển khai một phần mở rộng nhỏ nối nguồn với sample/handover sau khi ổn định merge; chưa đủ dependency cho toàn bộ B1–B3/C1–C3. Đây là đánh giá source và test cô lập, chưa xác nhận migration/data tại shared Supabase.

### Phần có thể bắt đầu tiếp

- **B0 — Nguồn → mẫu và bàn giao:** đã có `capture.samples`, `capture.sample_handovers`, API sample theo company, sample/source FK, business events và captured/recorded time. Trace đã trả `SourceRecordId`, chính là Capture SourceLot.Id; sample dùng ID này, không dùng Ledger lot ID. Có thể thêm thông tin mẫu, seal, thời điểm lấy và lịch sử bàn giao vào chi tiết nguồn, qua Capture-owned bulk read contract cùng scope/transaction. Không thêm cạnh vật liệu giả hoặc tự kết luận xét nghiệm/compliance. Chỉ hiển thị theo quyền trace nguồn hiện có; không mở dữ liệu nguồn cho ExportStaff. Admin global trace cần contract có kiểm tra authority riêng, không bỏ company filter của sample API hiện có.
- **Source/plot search:** đã có Capture search/reference và mã lot thống nhất; có thể triển khai điểm bắt đầu theo nguồn/thửa-vụ sau khi chốt mapping và scope. Source pending có thể có sample nhưng chưa có Ledger stock graph; phải trình bày rõ trạng thái.
- Sample API kiểm tra source đã commit trong Capture và procurement event; không kiểm tra Ledger arrival accepted. Không được hiểu chữ “accepted source” là đã có usable stock.

### Phần vẫn chờ

- **B1 đầy đủ:** chưa có lab report/result model/API và immutable official report/supersession contract. Sample handover có `toParty` không chứng minh phòng lab đã trả kết quả.
- **B2/B3:** MRL versions/limits, publish, resolve và import đã có nhưng chỉ là ngưỡng tham chiếu. Chưa có cohort membership snapshot, evidence dependency, compliance assessment, propagation hoặc atomic STALE/risk invalidation. Không suy dư lượng từ khối lượng đóng góp, tự HOLD hay kết luận đạt MRL.
- **C1–C3:** chưa có execution/versioned export plan, reservation, loading/allocation/container, closing assessment/dossier/supplement và container HOLD contract. Permission constants không chứng minh workflow đã triển khai.
- Arbitrary knowledge-time trace và benchmark toàn mùa vụ vẫn chưa đủ contract/dataset.

### Các việc phải xử lý trước khi mở rộng

1. Sót conflict marker đã được commit ở `README.md:5`, `apps/web/src/LedgerWorkspace.tsx:7` và `apps/web/src/styles.css:401`. Import LedgerWorkspace phải giữ cả Traceability lẫn OwnReceivingThreshold; CSS phải giữ cả trace-origin lẫn receiving-detail. Web hiện chưa build được. Đợt đánh giá này chỉ ghi nhận, chưa sửa code merge.
2. Ba `SampleTests` đều lỗi trong `SeedSourceAsync` ở `SaveChangesAsync` dòng 37: SQLSTATE 23514, `Committed procurement requires complete immutable provenance and Ledger stock`. Fixture dựng event/source rời rạc không còn thỏa boundary hiện tại. Cần dùng procurement/receiving workflow thật trong Testcontainers thay vì làm lỏng constraint; lỗi xảy ra trước khi test gọi sample API nên chưa kết luận API sample hỏng.
3. Sau khi sửa, cần kiểm tra integration source → sample → handover → backward/forward trace và browser thực tế, cả scope/legacy/pending source. Các test riêng đang pass chưa chứng minh phần tích hợp mới.
4. Kiểm tra pending migrations/runtime grants bằng read-only preflight trước deployment. Các migration unified code/receiving threshold mới trong source không có nghĩa đã được áp dụng trên shared database; mọi migration/grant vẫn cần phê duyệt riêng.

### Kiểm chứng thực sự đã chạy trong lần đánh giá

- `npm run build`: FAIL với TS1185 ở LedgerWorkspace, dòng 7/9/11 (conflict markers).
- `dotnet test backend/tests/GrainLedger.Api.IntegrationTests --filter 'FullyQualifiedName~LotTraceabilityTests|FullyQualifiedName~SampleTests|FullyQualifiedName~CaptureCutoverMigrationTests'`: build backend PASS (còn warning CS9113); 13 test: 10 PASS (9 trace + 1 migration/runtime), 3 FAIL (sample fixture như trên), 0 skipped. Database PostgreSQL Testcontainers cô lập.
- Không chạy toàn bộ test suite, không chạy browser với web build lỗi, không migrate/seed/trace POST hoặc sửa grants trên shared Supabase. Không thay đổi code tính năng và không commit trong lần đánh giá này.

Thứ tự đề xuất: sửa merge → sửa fixture và kiểm chứng sample → B0 sample/custody trong trace → chờ lab/compliance để mở B1/B2/B3 → chờ export/loading để mở C. Những điều kiện ở mục 4 vẫn giữ, chỉ phần sample/handover đã có nền tảng trong source.


### 7.1. Khôi phục runtime EXECUTE sau merge

Kiểm tra chỉ đọc ngày 20/10/2026 xác nhận trace function/migration vẫn tồn tại, effective runtime role đúng cấu hình, schema USAGE và command EXECUTE vẫn có nhưng trace ACL chỉ còn owner EXECUTE. Các migration UnifiedLotCodes/ReceivingLossThreshold/RepairReceivingLotCodeBoundary đã có trong history tại lần kiểm tra này. Bản dev trước merge thu hồi toàn bộ function grants rồi cấp lại danh sách chưa có trace; đây là cơ chế có thể gây mất quyền, chưa có audit chứng minh tác nhân đã chạy nó. Code sau merge đã có trace trong danh sách grant.

Theo yêu cầu khôi phục của người dùng và phê duyệt EXECUTE trước đó, đã cấp lại riêng `GRANT EXECUTE ON FUNCTION ledger.trace_lot(uuid,uuid,text,text)` cho runtime role đang cấu hình bằng migration-owner connection. Không migrate/seed, không gọi trace POST hoặc thay stock/history. Postflight runtime EXECUTE=true, `ValidateRuntimeAsync` PASS. Đã chạy backend bằng runtime connection, kiểm tra `GET http://localhost:5080/openapi/v1.json` trả 200 và route `/api/ledger/trace` có trong tài liệu. Backend được giữ chạy cho người dùng. Lỗi merge web ở mục 7 vẫn cần xử lý riêng.


### 7.2. Sửa conflict marker sau merge

Theo yêu cầu người dùng, đã gỡ ba khối marker ở README, LedgerWorkspace và styles.css. Giữ cả import/component Traceability và OwnReceivingThreshold, cả CSS trace-origin và receiving-detail; README giữ phần mô tả tracing cùng bố cục dev mới. Các lỗi marker/build web tại mục 7 đã được xử lý; lỗi fixture SampleTests vẫn là việc riêng chưa sửa.

Kiểm chứng: quét toàn bộ file tracked bằng `git grep` không còn conflict marker; `git diff --check` PASS; `npm run build` PASS (còn cảnh báo chunk trên 500 kB); `npm test` PASS 107/107 trên 20 file. Browser thật tại cổng test 5175 tải được màn hình đăng nhập và các module, không còn lỗi parse/import; session API trả 401 do chưa đăng nhập, chưa kiểm chứng màn Ledger sau đăng nhập trong smoke này. Không chạy trace POST hoặc thay dữ liệu shared database. Không chỉnh sửa package-lock đang có thay đổi của người dùng; chưa commit.
