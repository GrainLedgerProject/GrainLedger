# Kế hoạch triển khai truy xuất nguồn gốc

Ngày bắt đầu: 17/10/2026. Căn cứ: [đánh giá khả thi](../reports/traceability-feasibility-review-2026-10-17.md), [kiến trúc](../architecture.md), [hợp đồng Ledger](ledger-implementation-plan.md), SRS UC-24/25 và SDD mục 3.8.

## 1. Mục tiêu và trạng thái

Triển khai phần độc lập: **Ledger lot ↔ công đoạn đã commit ↔ nguồn thu mua/ruộng/vụ**. Bước A đã hoàn thành cho baseline trước mixed-bulk; chưa nghiệm thu toàn bộ truy xuất qua custody/plan/container.

**Cập nhật 21/10/2026:** đã merge dev mixed-bulk `7f7cb73` vào feat/trace, merge commit `2dfe2d3`. Có thể tiếp tục tracing thuần túy ngay, ưu tiên nối custody và actual processing input; không cần chờ lab/compliance hoặc export. Trace hiện tại chưa đầy đủ cho mixed-bulk dù routine cũ trả complete=true. Mục 9 là đánh giá trước triển khai; mục 10 ghi kết quả custody trace đã làm. **Mục 11 là plan sau merge dev residue `9bd3e8d` ngày 24/10/2026**; **mục 12 chốt ràng buộc hiện hành: chỉ thực hiện MVP/in scope**, không làm extended/out of scope. Mục 6–10 giữ căn cứ và nhật ký theo từng baseline. **Mục 13 ghi thực thi và deployment đã xong; mục 14 là kết luận bàn giao trước merge và backlog cho lần tiếp theo.**

- [x] Đọc và xác minh yêu cầu, source và giới hạn phụ thuộc.
- [x] Ghi kế hoạch trước khi thực hiện.
- [x] A1 — Protected API, đồ thị đầy đủ trong giới hạn công bố, persisted permission/owner, current/history, audit và bulk source contract.
- [x] A2 — Giao diện chọn lô/truy hai chiều, khối lượng nguồn và restriction.
- [x] A3 — Regression tests PostgreSQL cô lập, frontend tests, build và actual API/browser smoke.
- [x] A4 — Tài liệu API, kết quả kiểm chứng và hướng dẫn deployment.
- [x] B — Bằng chứng nguồn tùy chọn đã có trên UI: mẫu, confirmed report sections, original file và related history qua Capture API có sẵn. Full compliance/propagation vẫn để sau; xem mục 10.
- [ ] C — Chỉ chờ export/actual loading nếu truy đến plan/container; không chặn custody/lot tracing. Container preparation ở feat/container chưa phải loading đã commit.

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

Bảng dưới ghi đánh giá ban đầu ngày 17/10; phần lab/custody đã thay đổi. Mục 8 cập nhật dependency và ưu tiên, thay cho việc coi lab/compliance là điều kiện của mọi tracing.

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

Đã hoàn thành bước A trong source. Hợp đồng đầy đủ: [traceability-api.md](../api/traceability-api.md). B/C và các mở rộng ở mục 4 vẫn để sau, chưa coi là đã hoàn thành UC-24/25 toàn bộ.

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

Không đổi API, số học khối lượng hoặc quyền; không thêm dependency/migration. Không tự vẽ correction thành cạnh vật liệu. Flowchart lớn hơn 250 node hoặc 500 quan hệ API chuyển sang thông báo dùng Table cho toàn bộ kết quả; không cắt bớt graph âm thầm. Chi tiết sử dụng tại [API/UI contract](../api/traceability-api.md#table--flowchart-trên-web).

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


## 8. Plan tiếp tục tracing sau mixed-bulk — 21/10/2026

### 8.1. Baseline và kết luận

Đã đọc dev `7f7cb73` — `Implement mixed-bulk storage accounting and policy restrictions (#11)`, sau lab `b387942`. Merge vào feat/trace qua commit `2dfe2d3`; conflict LedgerWorkspace được gộp để giữ cả tab Traceability và `initialLotId` navigation từ nguồn/mẫu/lab sang stock. Không merge container feature vào trace; feat/container vẫn là nhánh riêng.

**Có đủ nền để làm tiếp tracing nguồn gốc và đường đi ngay.** Mixed-bulk đã lưu custody events/edges, exact source allocations, pool generations/revisions, actual processing allocations và effective HOLD ancestry. Không cần tạo lại nghiệp vụ lưu kho hoặc đợi order/blend/reservation/compliance để đọc các quan hệ này. Cần mở rộng trace reader/API/UI, không thay stock hoặc quy tắc pooling.

Nhưng **merge xong không có nghĩa trace đã tương thích đầy đủ với mixed-bulk**. Hai lỗ hổng ở 8.3 được xác minh bằng actual API cô lập. Không nghiệm thu graph cũ cho custody và không lấy complete=true hiện tại làm bằng chứng đủ quan hệ lưu kho.

### 8.2. Những gì mới có và cách tận dụng

- `ledger.custody_events` và `custody_edges`: ingress/withdraw tạo lô generation, aliquot và remainder; có event/location/revision/actor/time. Đây là quan hệ custody, không phải công đoạn sấy/xay. Pool cùng location ở các revision là các lot ID khác nhau.
- `ledger.custody_allocations`: source-wise allocation dương trên từng custody edge; exact micro-kg và remainder complement đã được upstream bảo toàn. Không tính lại attribution từ phần trăm đã làm tròn hoặc từ tổng nhập lịch sử.
- `ledger.custody_processing_allocations`: actual input/output/source shares cho processing; mutation mới vẫn giữ `operation.content` ở dạng draft/logical input. Immutable event có `resolvedInputs` chứa logicalLotId/actualLotId. Truy thực tế phải nối pool → aliquot thực rút → output thực → output pool nếu có.
- `MixedBulkReads.sql`: stock/detail có sourceComposition, custodyLinks, mixedBulk, poolLocationId, isCurrentPool, poolRevision, custodyDerived; source lookup có procurement_stock_bulk và procurement_source_lineage. Reuse contracts/semantics khi thích hợp, nhưng helper phục vụ source-stock không tự thay thế graph full forward/backward và không được coi một trang stock là toàn bộ graph.
- `MixedBulkSafety.sql`: effective HOLD theo source paths có phân bổ dương; later top-up không thay nguồn của aliquot đã rút trước đó. Không copy direct-HOLD logic cũ sang custody và không tự mở rộng HOLD authority.
- Lab intake/result/confirmation/related reports hiện có trên dev. Có thể gắn source evidence đọc-only sau custody, nhưng transcription confirmation và numeric VN comparison không phải market compliance PASS.

Nguồn code: [mixed-bulk plan](mixed-bulk-storage-plan.md), [storage schema](../../backend/src/GrainLedger.Modules.Ledger/Sql/MixedBulkStorage.sql), [commands](../../backend/src/GrainLedger.Modules.Ledger/Sql/MixedBulkCommands.sql), [reads](../../backend/src/GrainLedger.Modules.Ledger/Sql/MixedBulkReads.sql), [safety](../../backend/src/GrainLedger.Modules.Ledger/Sql/MixedBulkSafety.sql), [trace routine cũ](../../backend/src/GrainLedger.Modules.Ledger/Sql/Traceability.sql).

### 8.3. Gap đã xác minh và hệ quả

Diagnostic tạo source 10 kg, accepted arrival vào mixed location với policy/evidence synthetic hợp lệ, qua HTTP cookie/CSRF trên PostgreSQL Testcontainers. Kết quả:

1. Forward từ source không có pool descendant trong graph; backward từ pool không có root source trong danh sách traced lots. Source metadata có thể vẫn hiện qua flattened contributions, nhưng không chứng minh graph đã nối đường đi. Routine vẫn trả complete=true vì chỉ tính cạnh processing.
2. Sau HOLD nguồn, pool availableKg là `0.000000` theo effective blocking mới, nhưng restrictions của trace không có HOLD vì routine chỉ tìm hold trực tiếp trên pool lot.
3. Đối chiếu source: routine cũ lấy edges từ operation.content.inputs/outputs; mixed processing giữ logical input ở content và actual aliquot trong resolvedInputs/allocation events. Nếu chỉ thêm custody_edges mà không sửa actual processing join, có thể còn thiếu hoặc vẽ tắt sai nhánh processing.

Diagnostic tạm PASS vì assert đúng gap đang quan sát; không phải acceptance test xác nhận mixed trace đúng. File probe được xóa sau kiểm tra, không commit một test cố định đòi hệ thống phải thiếu dữ liệu. Khi triển khai, chuyển các tình huống này thành regression yêu cầu graph đúng; không sửa migration SQL cũ để giả đã nâng cấp DB.

### 8.4. Thứ tự làm tiếp

- [x] **T0 — Contract và regression cho mixed trace (ưu tiên đầu tiên):** tạo failing cases cho nguồn → pool, pool → nguồn, withdrawal/remainder, processing resolved inputs và inherited HOLD. Chốt semantics ở 8.5. Trước khi công bố hỗ trợ mixed-bulk, bổ sung guard từ chối phạm vi chưa hỗ trợ hoặc hoàn thành T1; không tiếp tục trình bày graph thiếu như full custody trace.
- [x] **T1 — Backend traversal gồm custody (làm ngay, không chờ lab/export):** forward migration mới thay protected trace routine, giữ migration/audit cũ. Union typed custody edges với actual processing edges, owner-scoped ở mọi hop; full traversal bao gồm lô hết tồn và generation lịch sử, không chỉ current pool pointer. Reads/weights/restrictions/graph dùng cùng snapshot; audit TRACEBACK giữ basis/version. Legacy processing không có allocation events dùng fallback đã xác minh từ immutable operation/event; không suy cạnh từ chung root.
- [x] **T2 — API/Table/Flowchart đồng bộ (sau T1):** typed relation/event fields; custody ingress/withdraw/remainder khác processing. Hiện current pool, revision/location, direct/effective restrictions và exact source shares. Starting lot vẫn rõ, physical arrows giữ input → output. Không bỏ custody node chỉ vì UI hiện chỉ biết operationId; cập nhật guards/counts/layout/fallback và callers/tests cùng API. Source expectation, source share còn lại, whole pool stock và original output không trộn thành một cột kg.
- [ ] **T3 — Điểm bắt đầu và chuyển trang dễ dùng (Stock baseline đã làm; scoped search rộng còn lại):** chọn current pool/source lot; link từ procurement/source/sample sang đúng Ledger lot hoặc current pool; cho xem generation cũ. Source search/plot-season search chỉ dùng scoped Capture read contract. Capture SourceLot.Id, Ledger source lot ID và current pool ID là ba identity khác nhau; pending procurement chưa có Ledger graph vẫn báo trạng thái, không tạo stock để trace.
- [x] **T4 — Sample/report evidence tùy chọn:** triển khai bằng Capture-owned paged APIs hiện có cho một nguồn được chọn, thay vì thêm bulk enrichment vào mỗi trace; gắn nguồn → sample → confirmed association/report và related history; quyền/source visibility giữ nguyên, ExportStaff không mặc nhiên được source-contribution trace chỉ vì được đọc lab. Ghi riêng sự liên quan và kết quả báo cáo, không truyền nồng độ xuống pool hoặc kết luận compliance. Có thể để sau mà T1–T3 vẫn nghiệm thu tracing thuần túy được.
- [ ] **T5 — Container/plan integration khi có actual execution:** chỉ nối committed loading/allocation từ feature sở hữu; không coi draft/container reference ở feat/container là hàng đã xếp. Reservation/assessment thiếu không chặn lot/custody trace; chúng chỉ chặn các nghiệp vụ tương ứng và việc truy thực tế chưa tồn tại.
- [ ] **T6 — Performance và bàn giao:** benchmark fan-in nhiều nguồn, nhiều lần top-up/withdraw/processing, depth/history và concurrent writes trên dataset cô lập đã thống nhất. Giữ freshness/no-cache và không per-record API calls; kiểm graph đầy đủ trong guard trước pagination trình bày. Không lấy build/test timing làm NFR 30 giây.

### 8.5. Semantics phải giữ khi thực hiện

- **Ba loại thông tin khác nhau:** stock tại thời điểm query; quan hệ vật liệu/custody đã ghi; attribution theo nguồn. Giữ lô lịch sử đã hết stock. Không cộng kg của mọi generation làm tổng hàng ảnh hưởng.
- **Chronology:** source D nạp sau khi đã rút aliquot Q không được xuất hiện trong backward của Q; forward từ D không quay về Q. Đi theo event edges/actual allocations, không nối hai lô chỉ vì cùng kho/root.
- **Zero và positive allocation:** zero-rounded root của processing thường vẫn là recorded relationship theo contract A; custody/effective HOLD mới dựa trên allocation dương. Không dùng một global `quantity>0` filter làm mất quan hệ ordinary đã cam kết; cũng không áp dụng graph reachability mù để gắn nguồn zero-share vào mọi aliquot. T0 phải chốt/kiểm chứng cách API phân biệt quan hệ cấu trúc với phần đóng góp nguồn dương và tập liên quan theo từng basis. Không đồng nhất “có cạnh” với “bị nhiễm” hoặc “cần HOLD”.
- **Actual processing:** dùng resolvedInputs hoặc positive processing allocations cho custody hops, đồng thời giữ recorded zero-root relation của ordinary scope; không dựng cả logical shortcut lẫn actual aliquot edge rồi cộng kg hai lần. Pin source of each edge trong API/audit.
- **Current/history:** current loại processing bị supersede đúng baseline; custody append-only không tự biến mất vì pool đã có revision mới. History không đồng nghĩa stock as-of tùy ý. Mixed-custody correction/unmix hiện bị chặn theo nghiệp vụ upstream; trace không tự tạo đường đảo vật liệu.
- **Restrictions:** hiển thị nguồn/direct/effective HOLD đủ giải thích available=0, reuse helper hiện có trong protected owner routine; không grant runtime đọc custody private tables hoặc execute effective_hold_ids/blocked helpers riêng. Không tạo HOLD mới từ thao tác trace.
- **Access:** CustomerManager own-company và Administrator theo global trace riêng; ExportStaff forward-only không lộ source composition. Quyền gọi trace không cấp quyền general stock cho Admin, không mở các custody helpers cho PUBLIC.
- **Limits:** 5.000 lots/20.000 edges backend và 250 nodes/500 relations Flowchart phải tính cả custody nodes/edges theo contract mới, không silently truncate. Complete chỉ true khi toàn bộ phạm vi được hỗ trợ đã duyệt xong; timeout/unsupported/over-limit phải từ chối rõ.

### 8.6. Acceptance/regression cần thêm

1. Nguồn A/B/C vào một pool; hai chiều đi qua đủ generation. Xác minh số kg/source lấy từ persisted allocation, không recompute bằng float.
2. Rút một phần tạo aliquot và remainder; từng source và tổng mass bảo toàn. D top-up sau đó không làm thay đổi tổ tiên aliquot cũ.
3. Pool → aliquot → DRYING/MILLING → output pool/stock; đúng actual input, không duplicate logical edge/attribution; ordinary source/draft vẫn hoạt động.
4. HOLD source/intermediate → effective block đúng pool/descendant; zero-share/top-up boundary đúng upstream safety, trace badge và available không mâu thuẫn.
5. Current/history, corrections ordinary, lịch sử custody immutable; exhausted pool vẫn có trong graph; ordinary transfer không bị vẽ thành processing.
6. Revoked role, cross-owner edges, Export source denial, Admin trace-only, new private custody grants không bị mở rộng.
7. Graph lớn hơn một trang; bao gồm cả custody/event nodes trong guards; no partial/audit commit khi lỗi; concurrent snapshot; table/flowchart cùng dữ liệu và không audit lại khi đổi view.
8. Actual API/browser cô lập: source → mixed pool → withdraw → top-up → process → forward/backward/history và giải thích HOLD. Source/sample/report chỉ là evidence refs khi chưa làm compliance.

### 8.7. Kiểm chứng lần merge/đánh giá này

- `npm test`: PASS **126/126** trên 22 file; `npm run build`: PASS, còn warning chunk trên 500 kB.
- `dotnet test ... --filter 'FullyQualifiedName~LotTraceabilityTests|FullyQualifiedName~MixedBulkTests|FullyQualifiedName~MixedBulkMigrationTests|FullyQualifiedName~CaptureCutoverMigrationTests'`: backend build PASS, **39/39** regression PASS trên PostgreSQL Testcontainers. Đây là các suite riêng, không chứng minh trace mixed đã tích hợp.
- Diagnostic actual HTTP riêng như 8.3: **1/1 PASS với gap được xác nhận**, forwardContainsPool=false, backwardContainsRoot=false, completeReported=true; pool holdBadge=false trong khi availability=0.000000. Probe tạm đã xóa khỏi source; không seed/write lên shared DB.
- Quét marker toàn repo và `git diff --cached --check`: PASS trước merge commit. Không chạy toàn bộ backend suite, không chạy browser mới trong lượt đánh giá vì chưa triển khai UI/traversal mới; actual API diagnostic là runtime path đã thực hiện.
- Không migration, seed, data cleanup hoặc grant thay đổi trên Supabase. Dev docs ghi mixed-bulk là local implementation, không suy đã deploy từ migration source hoặc các lần phê duyệt tính năng khác.

**Bước được khuyến nghị bắt đầu tiếp: T0 → T1 → T2.** User đang yêu cầu plan nên lượt này chưa sửa trace routine/API/UI cho custody. Plan cập nhật sau merge giữ ngoài commit; chỉ merge commit được tạo, chưa push.

## 9. Rà soát lại toàn luồng và plan hiện hành — 21/10/2026

### 9.1. Căn cứ và phạm vi kết luận

Rà soát checkout `feat/trace` tại `2dfe2d3`, gồm host/API composition, Identity permissions, Catalog/MRL, Capture nguồn/ruộng/vụ/khai báo phun/mẫu/lab, Ledger receiving/stock/processing/corrections/HOLD/mixed-bulk, trace API/Table/Flowchart và regression tests liên quan. Đối chiếu README, AGENTS, setup/deployment, architecture, Ledger/lab/mixed-bulk/API plans. Đây là đánh giá code hiện có, không xác nhận migration đã được cài hoặc dữ liệu thực đã đủ trên shared database.

Kiểm tra thêm bằng `git show` trên các refs đang có: `feat/container` tại `7ad419f` chỉ có preparation/drafts; `origin/feature/implement-export` tại `6864fc8` có module export order và planned container targets nhưng chưa nằm trong checkout này. Không fetch/pull hoặc merge thêm trong lượt rà soát; refs local không chứng minh trạng thái remote mới nhất. Không coi code ở nhánh khác là dependency đã tích hợp.

**Kết luận:** làm tiếp được ngay. Mục tiêu đợt kế tiếp là truy đúng **nguồn → nhập kho/trộn → rút/tách → chế biến → lô đầu ra**, kèm giải thích restriction thực có. Sau đó nối bằng chứng lab của nguồn. Không cần làm optimizer, reservation hoặc compliance engine trước để hoàn thành phạm vi này.

### 9.2. Readiness theo nghiệp vụ

| Phần | Bằng chứng trong code hiện tại | Việc tracing có thể làm tiếp |
| --- | --- | --- |
| Nguồn, ruộng, vụ | Capture immutable procurement snapshot và `ISourceProvenanceReader`; trace đã hiển thị origin | Giữ identity/snapshot hiện có; thêm điều hướng từ nguồn đến trace. Không dựng đăng ký thửa thành cạnh vật liệu. |
| Nhận hàng | Ledger arrival/measurement/review, measured stock; procurement đăng ký chưa tạo stock | Nối lịch sử nhận và evidence khi cần; phân biệt expected/accepted/remaining kg. Pending source chỉ có hồ sơ, không có Ledger graph. |
| Kho thường/chế biến | Committed operations, contributions, corrections; trace hiện có | Duy trì hai chiều, current/history, retained zero-rounded relations và quyền hiện hành. |
| Kho trộn/rút hàng | `custody_events`, `custody_edges`, `custody_allocations`, `custody_processing_allocations`, event `resolvedInputs` | **Ưu tiên sửa:** truy đủ generation/aliquot/remainder và actual processing inputs; không tạo lại nghiệp vụ lưu kho. |
| HOLD | `effective_hold_ids` và `blocked`; stock reader đã dùng effective HOLD, trace còn direct-only | Đồng bộ badge/explanation với restriction thực; không đặt hoặc giải phóng HOLD từ trace. |
| Mẫu và lab | Sample/handovers, original reports/results, confirmed associations, amendment/supplement/retest | **Đủ dữ liệu để bổ sung evidence nguồn ngay**, sau khi làm bulk read và quyền riêng. Không cần chờ compliance đầy đủ. |
| MRL/compliance | `AssessVn` đã so sánh báo cáo confirmed với MRL VN published, có numeric/qualifier/LOD/LOQ | Có thể dẫn đến kết quả tham chiếu của báo cáo. Chưa có quyết định market/commodity/time/version đầy đủ để suy PASS cho hậu duệ. |
| Khai báo phun | Immutable declaration theo plot-season; khai báo khác xét nghiệm | Tùy chọn dẫn đến hồ sơ canh tác đúng scope; không coi hoạt chất khai báo là kết quả đo hoặc bằng chứng lô bị nhiễm. |
| Order/plan/container | Order code ở ref khác; container branch chỉ preparation và loading drafts, commit/CLOSE bị chặn | Chờ hợp đồng actual committed loading/allocation. Planned target/draft không phải bằng chứng hàng đã xếp. |

Source implementation anchors: [Capture provenance](../../backend/src/GrainLedger.Modules.Capture/Traceability/SourceProvenanceReader.cs), [lab API/assessment](../../backend/src/GrainLedger.Modules.Capture/LabReportEndpoints.cs), [role permissions](../../backend/src/GrainLedger.Modules.Identity/Authorization/Permissions.cs), [trace backend](../../backend/src/GrainLedger.Modules.Ledger/Traceability/LotTraceability.cs), [trace SQL](../../backend/src/GrainLedger.Modules.Ledger/Sql/Traceability.sql), [mixed commands](../../backend/src/GrainLedger.Modules.Ledger/Sql/MixedBulkCommands.sql), [mixed safety](../../backend/src/GrainLedger.Modules.Ledger/Sql/MixedBulkSafety.sql), [trace UI](../../apps/web/src/LedgerTraceability.tsx), [graph builder](../../apps/web/src/trace-flow.ts).

### 9.3. Những điểm phải chốt bằng code/contract trước nghiệm thu

1. **Graph đầy đủ:** routine hiện chỉ duyệt `operation.content`, nên bỏ custody và có thể dùng logical input thay aliquot thực. `complete=true` hiện chỉ phản ánh graph cũ. T0/T1 phải sửa hoặc từ chối rõ trường hợp chưa hỗ trợ, không chỉ đổi câu chữ UI.
2. **Source-specific paths:** pool có nhiều nguồn không có nghĩa mọi nhánh sau đều nhận mọi nguồn, nhất là ở micro-kg. Graph structural và source-attributed reachability phải có basis riêng. Giữ ordinary zero relationship để đọc lịch sử; khi diễn giải liên quan theo nguồn qua custody phải theo persisted source allocations dương. Không dùng chung một BFS rồi tuyên bố toàn bộ tập là hàng nhiễm/ảnh hưởng chất lượng.
3. **Actual input:** dùng immutable processing event `resolvedInputs` để biết input đã tiêu thụ; positive `custody_processing_allocations` phục vụ source shares/ancestry. Chỉ lấy bảng allocation dương có thể làm mất structural processing relation được làm tròn về zero. Không giữ cả logical shortcut và actual edge như hai lần tiêu thụ.
4. **Giải thích HOLD:** dùng helper hiện có trong protected routine. Tách direct/effective HOLD; nếu Export không được thấy origin, chỉ trả trạng thái và giải thích đã rút gọn, không lộ source IDs qua HOLD details. Admin global trace không kéo theo quyền stock/lab chung.
5. **Ba identity:** trace contribution `sourceLotId` là Ledger source-lot UUID; `capture.source_lots.id` chính là `sourceRecordId` trong provenance; `sample.sourceLotId` tham chiếu ID Capture này. Pool/aliquot là Ledger lot khác. Lab join phải qua mapping, không so trực tiếp hai trường cùng tên `sourceLotId`.
6. **Quyền lab độc lập:** hiện CustomerManager có `lab.write`, ExportStaff có `lab.read`, Administrator không có cả hai; lab API còn yêu cầu customer-company scope. Vì vậy `trace.read.all` không được dùng để kéo báo cáo lab cho Admin. Đợt evidence đầu chỉ enrich actor có quyền lab và quyền source tương ứng (CustomerManager); actor khác vẫn trace bình thường và có trạng thái evidence không được cấp quyền. Export có thể tiếp tục dùng lab UI riêng theo quyền hiện có, không nhận thêm source composition qua trace.
7. **Độ hạt kết quả lab:** `AssessVn` gom rows của mọi result set trong một báo cáo; một report có thể chứa nhiều mẫu. Summary cấp report không được gán thành verdict cho từng source hoặc từng output pool. Trace chỉ hiển thị rows đúng confirmed association và liên kết report; summary nếu hiển thị phải ghi rõ là toàn báo cáo.
8. **Thời điểm assessment:** `ResponsesAsync` tính lại VN comparison khi đọc, chọn published version mới nhất theo substance/commodity; query hiện không pin decision date/version và không chọn riêng commodity của lô. Đây không phải immutable assessment snapshot lúc confirm hoặc lúc trace. Không lưu summary này như closing verdict; không tự sửa interpretation/MRL rules trong feature tracing.
9. **Bổ sung/sửa xét nghiệm:** related-report links đã có nhưng không tự đủ quy tắc chọn một kết quả pháp lý hiện hành. Hiện lịch sử và quan hệ đúng loại; không tự xóa report cũ, chọn report mới nhất làm PASS hoặc giải phóng HOLD.
10. **Snapshot và dung lượng:** graph/stock/restrictions cần cùng SQL statement snapshot. Bulk lab enrichment ở READ COMMITTED có thể là statement khác; phải ghi rõ thời điểm đọc evidence, không tuyên bố cùng snapshot lịch sử với graph nếu chưa bảo đảm được. Cap/pagination evidence độc lập và khai báo rõ; không cắt lab records rồi báo toàn bộ evidence đầy đủ. Không kéo file bytes hoặc N requests theo từng source vào trace response.

Một số câu trong architecture/lab/mixed-bulk docs còn mô tả mọi report là `NOT_ASSESSED`. Code `AssessVn` và test `Confirmed_report_assesses_against_published_VN_MRL_limits_and_flags_unregulated_or_insufficient_rows` chứng minh đã có numeric baseline comparison. Phân biệt **baseline comparison đã có** với **full compliance decision chưa có**; không dùng câu mô tả cũ để chặn evidence tracing.

### 9.4. Thứ tự triển khai và đầu ra nghiệm thu

- [x] **Đợt 1 — T0 + T1: sửa backend custody trace.** Viết regression mong muốn cho các gap mục 8.3; chốt typed edge contract/structural-vs-source basis; forward migration mới thay routine, snapshot/audit schema phù hợp và finite runtime grants. Nghiệm thu source ↔ pool ↔ withdrawal ↔ actual processing ↔ output pool, đúng inherited HOLD và chronology. Không đổi postings/allocation/policy upstream; không sửa migration lịch sử.
- [x] **Đợt 2 — T2: Table/Flowchart đọc đủ kết quả mới.** Cập nhật DTO/callers, custody event nodes và labels; chỉ rõ pool hiện hành/generation cũ, vị trí, quantity semantics, direct/effective restriction. Table và graph cùng snapshot; switching display không gọi trace lại; guards tính cả custody. Kết thúc đợt 2 có thể bàn giao tracing nguồn gốc qua lưu kho và sản xuất, độc lập lab/export.
- [x] **Đợt 3 — T3 baseline: mở trace từ các màn đang có.** Reuse scoped source/stock projections để chọn đúng accepted source/current pool và mở generation cũ; thêm action trace từ Stock/Source theo quyền, không chỉ nhập UUID. Giữ pending expectation rõ ràng. Plot/season search diện rộng chỉ thêm sau khi có scoped Capture bulk resolver; không dùng một trang search làm toàn bộ tập truy xuất.
- [x] **Đợt 4 — T4: nối mẫu/báo cáo như bằng chứng nguồn.** Đã chọn tái sử dụng Capture paged APIs theo yêu cầu xem một nguồn, thời điểm đọc evidence riêng với graph (xem mục 10); không thêm enrichment/port mới; source → sample/dispatch → confirmed result sets/report refs và related history. Mở panel riêng của nguồn trong Table/Flowchart, giữ concentration/qualifiers/unit/LOD/LOQ nguyên văn; không sửa graph vật liệu thành graph lab. Report chưa xác định identity phải ở trạng thái chưa xác nhận, không gán authoritative evidence cho source. Có thể làm sau đợt 2/3, không phụ thuộc compliance engine.
- [ ] **Đợt 5 — T6: kiểm chứng và hiệu năng theo phạm vi vừa triển khai.** Mỗi đợt chạy regression và actual API/browser cô lập trước khi bàn giao; cuối cùng đo workload mixed/lab lớn đã thống nhất, graph completeness, concurrent reads/writes và response/page riêng. Không đợi đến đợt 5 mới kiểm chức năng; không tuyên bố NFR từ timeout hoặc test timing.
- [ ] **Để sau — T5: order/plan/container actuals.** Chỉ mở khi upstream có committed loading + actual lot/location/kg, authority/version/reservation references và correction/cancellation semantics. Khi có CLOSE mới nối closing dossier snapshot/supplements; draft/reference đơn thuần không tạo material edge.
- [ ] **Để sau — quality/compliance impact.** Lan truyền residue/bounds, market decision/safety margin, evidence staleness, automatic restriction hoặc container HOLD cần nghiệp vụ và contract riêng. Không phải điều kiện trước của đợt 1–4; không triển khai ngầm dưới tên tracing.

### 9.5. Kiểm chứng cần bổ sung cho lab và điều hướng

- Mapping Ledger root → Capture SourceLot.Id → sample/report đúng company; legacy thiếu mapping, pending arrival, exhausted lot và pool generation cũ có trạng thái rõ.
- Một report chứa hai mẫu thuộc hai nguồn: panel nguồn A không hiển thị rows nguồn B như kết quả của A; summary toàn báo cáo không biến thành verdict của A.
- CustomerManager đúng company đọc được evidence; foreign company/revoked role bị chặn; Admin trace vẫn chạy nhưng không đọc lab; Export forward/source redaction không bị vượt qua qua IDs, HOLD explanation hoặc lab payload.
- Late report và amendment/retest giữ nguyên original association; không đổi stock, concentration hậu duệ, HOLD hoặc old trace thành kết quả mới. Nguồn chưa có report khác với không có quyền đọc report.
- Bulk read không materialize original file bytes; pagination/limits evidence minh bạch; chuyển Table/Flowchart không tạo thêm audit hoặc gọi lại từng source.
- T0–T2 tiếp tục đầy đủ acceptance mục 8.6, đặc biệt zero allocation, late top-up, fan-in/rejoin, runtime helper denial và rollback success audit khi graph thất bại.

### 9.6. Phạm vi thực hiện của lượt rà soát này

Chỉ đọc source/tests/tài liệu và refs đã có, cập nhật plan và các mô tả tài liệu liên quan. Không sửa feature code, không fetch/merge nhánh khác, không chạy test/build/API/browser mới, không truy vấn hoặc ghi shared database, không migration/seed/grant, không commit/push. Kết quả test và diagnostic ở mục 6–8 là kết quả các lượt trước, không phải lần chạy mới. Kiểm tra lần này giới hạn ở diff/format và liên kết tài liệu được sửa.

**Bước nên triển khai kế tiếp: đợt 1 (T0 + T1), sau đó đợt 2 (T2).** Lab evidence đã đủ nền cho đợt 4; full compliance và actual container trace vẫn là phần riêng có điều kiện.


## 10. Kết quả triển khai tiếp — 21/10/2026

### 10.1. Phần đã thực hiện trong source

Checkout vẫn là `feat/trace` trên `2dfe2d3`; không cần chuyển nhánh vì đã ở đúng nhánh. Giữ nguyên các thay đổi tài liệu chưa commit từ lượt đánh giá trước, không merge/push/commit trong lượt triển khai này.

- **T0–T1:** migration forward `20261021010000_CustodyTraceability` và [routine mới](../../backend/src/GrainLedger.Modules.Ledger/Sql/CustodyTraceability.sql) thay trace routine; không sửa SQL/migration lịch sử. Graph gồm typed custody ingress/withdraw/aliquot/remainder và processing actual inputs từ immutable commit event `resolvedInputs`, với fallback recorded content cho legacy. Current/history vẫn giữ correction semantics, ordinary transfer không thành processing edge.
- Schema version 2 khai báo `RECORDED_MATERIAL_RELATIONSHIPS`. Structural zero relationships được giữ; `sourceAllocations` và `positivePathSourceIds` theo persisted positive source allocations là thông tin riêng. Coverage `PARTIAL_LEGACY` không chứng minh nguồn vắng mặt. Không dùng shared pool reachability để gán nguồn zero-share vào mọi aliquot; không suy nhiễm/MRL từ graph.
- Graph/stock/composition/effective HOLD chung một SQL statement snapshot. Badge HOLD dùng `effective_hold_ids`; thêm direct/effective flags để giải thích availability zero. Không lộ source IDs/allocations qua Export response và không mở quyền đọc private helpers/tables. Không sửa stock/HOLD nghiệp vụ.
- Backend và web từ chối trace response trước schema version 2. Nếu DB còn routine cũ, backend trả 503, transaction rollback cả success audit vừa tạo. Không để web mới báo complete custody từ routine cũ.
- **T2:** Table và Flowchart dùng cùng typed relations; custody event là node kho riêng, có location/revision. Quantities giữ exact strings, chart lấy `inputEventKg` từ backend cho split input thay vì cộng float hoặc đếm lại. Giữ starting lot, chọn node, zoom/fit, limits và Table fallback. Quantity khi tạo lot, stock hiện tại và source composition vẫn khác nhau.
- **T3 baseline:** Stock detail có **Trace this lot**, chuyển tab/điền đúng ID nhưng không tự tạo audit. Source/sample đi qua mapping current stock có sẵn rồi mở trace. Trace picker bao gồm lô hết tồn/generation cũ; cho nhập UUID trực tiếp dù option chưa tải. Quy tắc historical lot của processing/transfer không đổi.
- **T4 baseline:** [source evidence panel](../../apps/web/src/TraceSourceEvidence.tsx) mở theo nhu cầu cho CustomerManager có quyền lab, dùng Capture `sourceRecordId` từ provenance để đọc sample/report. Report sections chỉ lấy confirmed association đúng nguồn; giữ nguyên result text, unit, LOD/LOQ/method. Có sample/handover links, original download và related history. Whole-report VN comparison có nhãn scope riêng; không gán cho từng source/descendant.

### 10.2. Điều chỉnh cách tích hợp evidence và giới hạn

Tái sử dụng hai Capture APIs đã phân trang, mỗi trang 20 records, cho **một nguồn do người dùng chọn**. Đây là các truy vấn theo tập trong Capture, không loop query từng source của graph hoặc từng result row. Không thêm cross-module EF access, grants hoặc service abstraction. Trình bày mẫu và báo cáo có pagination độc lập; không tuyên bố một trang là toàn bộ evidence và không tải original bytes vào list/trace response.

Evidence được đọc sau graph và ghi thời điểm đọc riêng; không tạo cảm giác report comparison là immutable snapshot tại thời điểm trace. Chuyển Table/Flowchart không remount/refetch evidence; đổi lô/request đóng panel. Existing Capture APIs kiểm tra permission/company trên từng request; Admin trace-only và Export source redaction không bị thay đổi. Lỗi quyền hoặc tải dữ liệu không được đổi thành “không có báo cáo”.

T3 còn lại: plot/season/source search diện rộng và điểm bắt đầu từ pending source cần scoped resolver/UX riêng; chưa làm. Không tạo Ledger lot/stock cho procurement chưa nhận. T4 không phải compliance engine, không tự giải quyết supersession pháp lý, truyền nồng độ, auto-HOLD hay container impact.

### 10.3. Kiểm chứng thực tế

- Regression mới ban đầu tái hiện thiếu custody/actual-input graph trên routine cũ; sau nâng cấp đã pass.
- Backend focused suite **62/62 PASS** trên PostgreSQL Testcontainers: `LotTraceabilityTests`, `MixedBulkTests`, `MixedBulkMigrationTests`, `CaptureCutoverMigrationTests`, `LabReportTests`, `SampleTests`. Bao gồm chronology/top-up, exact zero-share boundary, actual processing/output ingress, inherited HOLD, scope/Export redaction/Admin, ordinary zero relations/corrections, large result/guard, concurrent snapshots, prior-schema upgrade/grants và legacy-routine 503 không commit misleading audit.
- `npm test`: **135/135 PASS** trên 23 files. Thêm custody split topology/limits, table/graph cùng snapshot, effective HOLD, Stock→Trace không tự query, historical generation/manual ID, multi-sample report filtering, qualifier precision, evidence paging/error, permission boundary, xóa result khi đổi role/company và old-schema response rejection.
- Một lượt lặp frontend có 134 assertions pass nhưng exit 1 vì unhandled error trong fixture lab cũ: GET detail sau create trả page envelope. Đã áp dụng đúng hai dòng sửa fixture từ feat/container (detail mock và await heading), không đổi lab nghiệp vụ; lượt cuối 135/135 pass, exit 0 và không còn lỗi bất đồng bộ.
- `npm run build`: **PASS** TypeScript/Vite; còn warning chunk trên 500 kB. Backend build qua các lượt `dotnet test` pass, còn CS9113 unread parameter có sẵn. Không chạy toàn bộ backend solution suite hoặc benchmark toàn mùa vụ.
- Browser/API thật tại `5086/5176`, cookie/CSRF và database Testcontainers: source 10 kg → mixed pool, source HOLD → pool available zero; backward có hai lot + một storage node. Table→Flowchart giữ một trace request trong lượt smoke custody; không có browser error ở fixture đã kiểm tra.
- Stock deep link → **Trace this lot** điền đúng pool UUID, chưa có POST trace trước khi bấm Run trace. CustomerManager mở evidence nguồn, thấy sample và confirmed synthetic report, `<0.0100`, `mg/kg`, LOD `0.0050`, LOQ `0.0100`; dispatch/sample history link đi đến đúng route. Sample/report/confirmation/dispatch synthetic chỉ tạo trên Testcontainers. Lần thử handover dùng schemaVersion 2 nhận 400; gọi đúng contract version 1 đã thành công 201.
- Màn trace/evidence ở viewport 390 px có document width 375 px (scrollbar), không overflow toàn trang; bảng/graph cuộn trong khung. Browser Development StrictMode replay request khi mount evidence, không tính là production request/performance benchmark. Chuyển display không làm tăng số report requests đã ghi nhận.

### 10.4. Deployment và phần tiếp theo

Tại thời điểm bàn giao code ban đầu, migration mới **chỉ được áp dụng trong test containers**, chưa trên Supabase dùng chung; trạng thái sau phê duyệt cập nhật ở mục 10.5. `CREATE OR REPLACE` giữ signature/ACL; tiếp tục finite EXECUTE grant trong runtime-role configuration hiện có, không cấp riêng private helpers hay owner access cho runtime. Shared deployment vẫn cần phê duyệt riêng theo AGENTS; normal startup không migrate/seed/grant. Không chạy trace audit, mẫu/report synthetic hay stock mutation trên shared DB.

Phần chưa hoàn thành: T3 resolver/search rộng; T6 benchmark dataset/concurrency và NFR; T5 actual loading/container/order-plan integration; quality/compliance propagation/decisions. Những phần này không được đánh dấu hoàn thành từ kết quả kiểm thử chức năng trên.

Đã dừng browser bridge/Vite test riêng, đóng tab test và xóa config/session fixture tạm. `git diff --check`, quét conflict marker và kiểm tra 21 liên kết tài liệu mới/thay đổi pass; không commit/push.


### 10.5. Cập nhật database theo phê duyệt — 21/10/2026

Sau yêu cầu trực tiếp “vậy chạy update database đi”, đã cập nhật database trong cấu hình Development hiện hành, giữ nguyên `.env` và runtime/migration connections.

- Preflight chỉ đọc: runtime/migration cùng target, login riêng; effective runtime database role khớp cấu hình (Supabase connection username có routing suffix nên không so trực tiếp username với role). Identity/Catalog/Capture không pending; Ledger chỉ pending `20261021010000_CustodyTraceability`. Routine trace còn bản cũ và thiếu runtime EXECUTE.
- Database còn có `ledger.container_action(uuid,text,jsonb)` từ nhánh container. Để tránh quy trình cấp lại toàn bộ quyền ở nhánh trace tác động các objects của nhánh khác, dùng EF `LedgerDbContext.Database.MigrateAsync()` với migration-owner, sau đó chỉ GRANT EXECUTE trên `ledger.trace_lot(uuid,uuid,text,text)` cho configured runtime role đã xác minh. Không chạy host-wide grant reset.
- Process exit 0. Chỉ migration CustodyTraceability được cài; postflight không còn Ledger pending, trace schema v2=true, runtime trace EXECUTE=true và `ValidateRuntimeAsync` PASS. Các module khác không có pending tại preflight.
- Container routine vẫn tồn tại, runtime EXECUTE vẫn false như preflight; không tự sửa quyền container trong yêu cầu cập nhật tracing này. Không thu hồi hay mở rộng các quyền khác.
- Không chạy seed, trace POST/audit hoặc data-changing smoke trên database dùng chung. Không thay stock, HOLD, báo cáo xét nghiệm hoặc container data. Console preflight/deployment tạm nằm ngoài repository, không chứa credentials trong source/output.


## 11. Plan cải thiện tracing theo silo sau dev mới — 24/10/2026

### 11.1. Baseline đã xác minh

Đã fetch origin và kiểm tra `dev`/`origin/dev` tại `9bd3e8d` (PR #14, 23/10/2026): **Enhance residue management and streamline workflows across ledger and web**. Đây là một squash commit gồm residue bounds/rules, company adoption, reviewed report selection/uncertainty, concentration normalization, live preview/audit snapshots, unconfirmed lab draft revisions/deletion, equipment retirement và cập nhật npm packages. Không có commit dev mới hơn tại thời điểm fetch.

Đã merge dev vào `feat/trace` qua **`73935b0 chore(ledger): merge residue workflows into trace feature`**. Giải quyết conflict ở LabReports, lab tests, BrowserWorkflowHost, architecture và sample API: giữ export Report type cho trace evidence đồng thời giữ result IDs/draft lifecycle của dev; giữ cả trace/residue browser fixtures và loopback origin validation, hỗ trợ cả URL và port env vars. Runtime grants vẫn có trace entry cùng các residue routines; không mở private custody helpers.

Thay đổi `9bd3e8d` **không sửa** `MixedBulkStorage.sql`, `MixedBulkCommands.sql`, `PhysicalReceiving.sql` hoặc `ProcurementLedgerPort.cs`. Cơ chế nhiều nguồn nhập thành một pool đã có từ **`7f7cb73` (#11)**; custody trace đã làm tại `7d0565b`. Nhận định “Ledger hiện chỉ giữ mỗi thu mua thành một lô riêng, chưa gộp được” không mô tả đầy đủ baseline hiện nay.

### 11.2. Proposal nói gì và tracing phải biểu diễn ra sao

Căn cứ `GrainLedger_V2.pdf` trong Week1_report (30/09/2026), trang 1, 6 và 9: nhiều nguồn bị trộn ở silo; hàng nhiều nông hộ **cùng khách hàng** được gom, không trộn giữa khách; thu mua vẫn tạo nguồn có chủ sở hữu; F18 cần truy từ bằng chứng xấu đến container và từ container về thửa/tỷ lệ. Ví dụ trang 6 gồm ba nguồn 50/30/20 tấn trong 100 tấn silo. Chuỗi minh họa cũng có nhận/sấy trước lưu silo; không suy rằng mọi đăng ký thu mua phải lập tức tạo tồn hoặc tự động vào cùng một silo.

Cần phân biệt:

- **Nguồn thu mua:** hồ sơ nguồn/thửa/vụ/mẫu và Ledger root sau khi nhận, theo dòng thu mua. Một procurement có thể có nhiều dòng; không phải luôn một procurement = đúng một Ledger source lot. Giữ các identity này để đọc sample/lab và audit.
- **Lô tồn trong silo:** current pool của một location/owner/commodity/stage/product identity hợp lệ. Khi nhiều nguồn vào kho trộn, tồn thực nằm ở pool chung; nguồn giữ attribution chứ không còn là các tồn riêng có thể rút thêm.
- **Generation lịch sử:** mỗi ingress/top-up/withdraw có thể tạo UUID và revision mới; location silo ổn định nhưng pool hiện hành đổi. Không sửa pool cũ tại chỗ hoặc gộp/xóa các nguồn để giả chỉ còn một UUID suốt đời silo.

Ví dụ dễ kiểm chứng: A 600 kg vào silo → pool P1 600 kg; B 200 kg vào cùng silo → pool P2 800 kg. A/B/P1 hết tồn tại vị trí đó, P2 còn 800 kg; composition P2 giữ A 600 và B 200. Truy ngược từ P2 phải thấy cả hai nguồn và lần nhập qua P1. Truy xuôi từ A phải đến các pool/output thực liên quan. Không cộng lượng tạo ra của A/B/P1/P2 thành tồn kho.

**Quyết định cho UX:** ưu tiên bắt đầu từ silo/pool hiện hành cho câu hỏi “hàng đang nằm ở đâu, từ nguồn nào”, vẫn cho bắt đầu từ nguồn hoặc lô lịch sử cho điều tra. Không bỏ phần trước silo khỏi lineage; không giới hạn traversal vào current pool pointer.

### 11.3. Khi thấy nhiều lô riêng, cần kiểm tra gì trước

Không sửa tracing bằng cách ẩn các nguồn để che lỗi cấu hình/ghi nhận vật liệu. Cần kiểm tra theo đúng scope:

1. Đang nhìn **danh sách nguồn**, **toàn bộ trace/history**, hay **tồn vật lý hiện hành**? Có nhiều source/history rows là đúng dù chỉ một pool còn stock.
2. Arrival thực tế được nhận vào location nào? Nếu vào kho thường rồi mới chuyển silo, pooling chỉ xảy ra ở bước nhập silo đã ghi; đăng ký procurement chưa tạo stock.
3. Location đó có `mixedBulk=true` và evidence-backed policy đúng owner/commodity/stage/product identity/effective time không? Chỉ đặt kind/name là SILO không tự bật pooling. `pool_ingress` trả lot đầu vào khi location không mixed; mixed thiếu policy thì fail closed.
4. Có cùng silo, cùng owner và identity phù hợp không? Khác silo/customer/stage có thể là nhiều pool hợp lệ, không phải lỗi “chưa gộp”. Không trộn giữa công ty để làm giao diện giống ví dụ.
5. Khi nhập thành công, `mixed_bulk_pools.lot_id/revision`, positions, custody edges và source stock projection có nhất quán không? Tỷ lệ khi tạo và attribution còn lại sau rút phải phân biệt.

Lượt rà soát này chưa đọc cấu hình/dữ liệu Supabase hiện tại nên **chưa kết luận silo của team đã opt-in/policy đúng hoặc migrations dev mới đã được áp dụng**. Không tự bật silo, tạo policy hay sửa hàng đã ghi.

### 11.4. Dev residue mới mở thêm được gì

Đã có [residue graph](../../backend/src/GrainLedger.Modules.Ledger/Sql/ResidueBounds.sql), [calculator](../../backend/src/GrainLedger.Modules.Ledger/ResidueCalculator.cs), [preview/evaluation service](../../backend/src/GrainLedger.Modules.Ledger/ResidueEvaluations.cs), [report review/binding](../../backend/src/GrainLedger.Modules.Ledger/ResidueReportReview.cs) và Stock live preview. Có thể nối trace lot → preview/historical evaluation và explanation về reports/results/rules/settings/dependencies mà không viết lại calculator.

Ba lớp phải giữ riêng:

- Recorded material/custody graph: quan hệ vật liệu đã ghi.
- Kg/source composition và positive accounting paths: phân bổ kế toán exact micro-kg đang có trong trace.
- Residue intervals/dependencies: dùng physical mass, exact rational và reviewed scientific applicability. Dev mới giữ cả exposure dương nhỏ hơn ngưỡng làm tròn kế toán; **không lọc chemical dependencies bằng `positivePathSourceIds`, `custody_processing_allocations.quantity>0` hoặc percentage đã làm tròn của trace**.

Residue trả finite bounds hoặc `UNRESOLVED` theo rule/evidence, không phải legal market PASS. Missing adoption, method, uncertainty, basis, factor hoặc evidence không được thay bằng 0/factor 1/MRL. Lab identity confirmation và residue binding là facts riêng; report confirmed vẫn có thể chưa đủ điều kiện tính bounds. Draft sửa/xóa appends revision/tombstone; không biến draft mới thành replacement pháp lý cho confirmed evidence.

### 11.5. Thứ tự triển khai đề xuất

Cập nhật trạng thái sau mục 13: P1–P3 đã triển khai ở mức MVP; P4 đã có lab-result → forward material trace và residue theo node, còn hoàn thiện liên kết evidence/diễn giải ảnh hưởng; P5 đã có regression/browser cho phạm vi hiện tại, chưa nghiệm thu benchmark. Chi tiết bàn giao mới nhất tại mục 14. Mô tả dưới đây giữ yêu cầu gốc, không dùng checkbox P4/P5 để suy rằng mọi phần của chúng đều chưa làm.

- [x] **P0 — Merge và kiểm chứng baseline:** giữ cả custody trace và residue mới, sửa conflict, kiểm tra grants/source role, migration compatibility và test thực tế. Kết quả ở 11.7.
- [x] **P1 — Ưu tiên silo/current pool trong điểm bắt đầu (làm tiếp ngay):** thêm lựa chọn rõ “Hàng trong silo/kho”, “Nguồn thu mua”, “Lô/generation lịch sử”; scoped server filter theo facility/location và current pool. Hiện tên vị trí, owner theo quyền, stage, quantity/available và revision; không suy loại lô từ prefix RAW. Không chỉ lọc một trang 25 dòng ở browser rồi coi là đủ pools. Giữ manual Ledger ID và quyền riêng Admin/Export. Kiểm tra setup theo 11.3 bằng đường đọc; chỉ đề nghị thay policy/ghi nhận hàng khi có căn cứ và authorization riêng.
- [x] **P2 — Làm graph/table nói đúng ngôn ngữ nghiệp vụ (sau P1):** nhãn riêng “Nguồn”, “Pool hiện hành”, “Generation cũ”, “Phần rút ra”, “Phần còn lại”; vị trí/silo dễ đọc. Nguồn tồn 0 ghi rõ “đã nhập pool” khi có custody evidence. Tách quantity khi tạo, tồn hiện tại và kg nguồn còn lại; Starting lot khác node đang chọn. View Current vẫn là effective operation relationships, không bỏ hết generation cũ. Giữ full result/IDs/edge counts và mở được đường chi tiết; không thêm một chế độ compact/analytics riêng trong đợt MVP này.
- [x] **P3 — Nối residue evidence vào trace (đủ nền code, sau P1/P2):** theo quyền `residue.evaluate`, cho mở preview hoặc evaluation đã lưu của node. Reuse existing service/routes và rule/result/dependency refs; không gọi preview từng node khi tải graph. Hiện status, khoảng/unit/basis, lý do UNRESOLVED và thời điểm/snapshot riêng; graph snapshot và preview không mặc nhiên cùng thời điểm. `GET .../residue-preview` không ghi snapshot; POST evaluation ghi audit chỉ khi người dùng chủ động yêu cầu. Không mở original lab cho Admin hoặc mở backward/source composition cho Export chỉ vì được evaluate.
- [ ] **P4 — Truy xuôi từ kết quả lab xấu (core tracing, sau P3):** resolve confirmed result → source Ledger identity đúng company, forward material trace, rồi đọc chemical dependencies/provenance để giải thích phần liên quan. Source bị nghi ngờ không đồng nghĩa mọi structural descendant đã được chứng minh nhiễm. Không tự đánh FAIL, áp HOLD hay release; giữ late evidence và evaluation snapshots cũ bất biến.
- [ ] **P5 — Nghiệm thu, hiệu năng:** mỗi bước có regression và actual API/browser cô lập; đo fan-in/top-up/withdraw, dataset/source count và thời gian DB/API/page riêng. Không tuyên bố đạt NFR toàn mùa vụ từ timeout hoặc build timing.
- [ ] **Để sau — actual order/loading/container:** nối khi có committed loading/allocation và closing dossier contracts. Container draft/planned target không phải hàng đã xuất; residue bounds chưa thay assessment dùng để cho phép CLOSE.

**Thứ tự lúc lập plan: P1 → P2.** Các bước này đã triển khai tại mục 13; lần sau tiếp tục theo mục 14, không làm lại từ đầu.

### 11.6. Acceptance cho phần cải thiện

1. Hai nguồn cùng công ty vào silo mixed hợp lệ: current selector chỉ ra pool hiện hành với tổng còn lại, backward vẫn đủ sources/generations, không còn coi source balance đã tiêu thụ là hàng có thể rút thêm.
2. Kho thường giữ source lots; silo chưa mixed hoặc thiếu policy được giải thích đúng, không fake pooling trên trace. Hai silo/khác customer không bị gộp bởi filter/UI.
3. A/B → pool → withdrawal Q → top-up C: Q cũ không nhận C; current pool composition dùng remainder + C. Historical pool vẫn mở được dù tồn 0.
4. Sấy/xay trước hoặc sau silo theo actual committed input/stage: không hardcode chỉ RAW_PADDY hoặc coi nhiều procurement luôn nhập cùng một location.
5. Root share kế toán về 0 nhưng residue dependency còn dương: source composition, material graph và chemical interval/explanation không được đồng nhất; UNRESOLVED được giữ nếu thiếu khoa học/policy.
6. CustomerManager own-company hai chiều; Export forward-only không nhận nguồn qua residue links; Admin trace không tự được original lab/evaluate. Scope/revocation và helper-grant denial tiếp tục được kiểm backend.
7. Draft report edit/delete, confirmed result selection/supersession, late evidence: trace evidence đúng association; evaluation cũ không bị rewrite, preview có thời điểm riêng, không tự thay HOLD.
8. Full Table/Flowchart nhất quán, current/history không lẫn inventory-as-of; guards giữ đủ custody/event nodes, không silent truncate hoặc client-side filter bỏ pool nằm ở trang sau.

### 11.7. Kiểm chứng merge và giới hạn kết luận

- `npm ci` theo lockfile dev mới; không tự nâng package ngoài merge. `npm run build`: PASS TypeScript/Vite, còn chunk warning trên 500 kB.
- Backend nhóm liên quan: lượt cuối **176/176 PASS** trên PostgreSQL Testcontainers, gồm lot trace, mixed-bulk, migration/cutover, lab, residue evaluation/review/company policies, equipment/ledger management và permission contracts. Actual HTTP paths nằm trong integration suite; không chạy toàn bộ backend solution suite.
- Lượt đầu backend có 66 fail/110 pass: một assertion migration của trace giả định không có migration tương lai, đã sửa thành kiểm bước upgrade riêng đến CustodyTraceability rồi nâng tiếp latest, vẫn giữ assertion history và legacy marker. Các lỗi lab 500/residue rule ở cùng lượt không tái hiện khi chạy hai ca riêng trên cả dev `9bd3e8d` và merge; lượt cuối đủ 176 pass. Chưa xác định nguyên nhân lỗi tạm thời đó, không kết luận đã sửa lỗi lab nghiệp vụ hay chứng minh mọi lần chạy đều ổn định.
- Frontend lượt cuối **208/208 PASS**; chạy `npm run test --workspace @grainledger/web -- --maxWorkers=2`. Lượt full mặc định trước đó có 206 pass/2 fail ở ResidueRuleMaintenance (timeout/async assertion); không đổi timeout hoặc logic residue để che lỗi. Các trace/source-evidence tests pass. Cần giữ theo dõi độ ổn định suite nặng ở P0/P5.
- Browser dùng test bridge/Vite riêng `5087/5177`, không dùng Supabase: Stock detail có cả Trace this lot và live residue preview; pool 10 kg HOLD hiện available 0, trace backward 2 lots/1 relation, Flowchart 3 nodes với 1 custody event, đúng direct/inherited HOLD. Đổi display vẫn 1 trace request. Preview riêng trả UNRESOLVED và reasons khi không có applicable rules/evidence, không tự tạo snapshot/HOLD. Focused click/snapshot tools có timeout; smoke tiếp tục bằng DOM events/evaluation trong cùng T3 preview, không báo đã kiểm pointer interaction hay performance.
- Không chạy migration, seed, grant reset, trace audit hoặc data-changing smoke trên database dùng chung. Các migrations/routines residue mới có trong code không chứng minh đã được cài trên DB của team. Merge không tự deploy; muốn chạy backend với DB đó cần kiểm pending migrations/runtime grants theo deployment guide và approval riêng.
- Merge commit đã tạo theo yêu cầu merge; plan mục 11 giữ ngoài merge commit để review, chưa push. Đây là plan cải thiện, chưa triển khai P1–P4 trong lượt này.


## 12. Ràng buộc chỉ triển khai in scope/MVP — 24/10/2026

Theo yêu cầu người dùng: **chỉ làm in scope; không làm extended scope hoặc out of scope**. Áp dụng cho các bước tiếp theo; code hoặc nền kỹ thuật đã tồn tại không tự biến một extension thành yêu cầu MVP.

Đã đối chiếu `review/FA26SE272_GrainLedger_Proposal_v1.1.docx.pdf`: core traceback trang 4, deliverables/extension trang 9, phân tách minimum/extended/excluded trang 11. `GrainLedger_V2.pdf` trang 8–9 là mapping F01–F20 để tham chiếu. Nếu một phần Theory gợi ý EPCIS/solver rộng hơn, không lấy đó để vượt giới hạn minimum scope ghi rõ ở deliverables/comments.

### 12.1. In scope toàn dự án

- Tài khoản/quyền/company scope, cấu hình nhà máy/kho và danh mục/MRL có phiên bản.
- Farmer/plot/season, khai báo thuốc, procurement/intake, sample/lab; mobile-friendly web capture và offline/idempotent sync.
- Ledger/stock, core conversions/custody, exact mass balance, chống double consumption, corrections append-only, nguồn đóng góp.
- Conservative residue bounds và evidence/rules, destination limits; blend feasibility và lý do từ chối cơ bản; Manager approval.
- Loading/CLOSE container, hồ sơ tuân thủ/truy xuất có thể xuất ra.
- Tracing hai chiều source/plot/lot/adverse lab result → stock/container và container → contributing plots/percentages/events/evidence; authorized HOLD/response, history/audit.
- Test/simulation/synthetic-season fixtures, correctness/concurrency/sync checks và traceback benchmarks thuộc deliverables cốt lõi, không phải extension nghiệp vụ.

Đây là phân loại phạm vi dự án, không phải tuyên bố tất cả đã triển khai hoặc thuộc trách nhiệm riêng của nhánh trace.

### 12.2. Extended scope — không triển khai tiếp

- Tối ưu nhiều container/orders, mixed-integer/advanced optimisation thay vì feasibility tối thiểu.
- Advanced mathematical infeasibility diagnostics; không yêu cầu IIS/chứng minh tối ưu/bất khả thi hình thức cho MVP.
- Portal hoặc shipment view/link dành cho buyer/auditor bên ngoài.
- GS1 EPCIS 2.0-compatible export/integration.
- Automated regulatory/market-limit ingestion và bulk import mở rộng; quản lý/versioned lookup MRL cơ bản vẫn là core.
- Dedicated native Android app; mobile-friendly/offline web client vẫn là core.

### 12.3. Out of scope — không triển khai

- Dự đoán dư lượng từ vệ tinh/thời tiết.
- Tính carbon/methane credits.
- Quản lý thanh toán/giá mua/quyết toán cho nông dân.
- Tích hợp ERP hoặc hải quan. Shipment dossier/export chứng từ nội bộ là core và không đồng nghĩa tích hợp hải quan.

### 12.4. Hệ quả cho plan tracing

- Cần sửa cách gọi trước đây: **truy từ kết quả lab xấu đến lô/pool/container là in scope (core traceback/F18), không phải extended scope**. Việc chưa triển khai hoặc còn chờ actual loading là trạng thái dependency, không phải phân loại ngoài MVP. Không mở thêm một module điều tra/phân tích nâng cao.
- P1/P2 chỉ cải thiện chọn đúng silo/current pool, nhận diện nguồn/generation và đọc graph hiện có cho core tracing; không đưa compact/analytics mới vào điều kiện hoàn thành.
- P3 chỉ nối evidence/bounds/rule references cần để hiểu nguồn và tính toán, reuse residue module; không tạo engine hoặc dự đoán mới.
- P4 chỉ thực hiện entry từ confirmed adverse result đến đường truy xuất và các outputs thực có; không thêm suy luận điều tra nâng cao hoặc tự đặt/release HOLD.
- Container tracing/HOLD/dossier vẫn trong scope dự án, nhưng chỉ tích hợp khi có actual committed loading và đúng contract/authority từ phần sở hữu. Không tạo container giả để coi core trace hoàn tất.
- Không triển khai các extension liệt kê ở 12.2, không dùng chúng làm prerequisite cho tracing; không sửa/xóa một tính năng đã có ở nhánh khác chỉ để đổi phân loại scope trong lượt cập nhật plan này.

## 13. Plan thực thi MVP: silo, bounds và lab-to-trace — 24/10/2026

Phạm vi được yêu cầu: thực hiện P1/P2/P3/P4 của mục 11 trong giới hạn mục 12. Không actual container/loading mới, auto-HOLD, optimizer, EPCIS, portal hoặc dự đoán.

- [x] **S1 — Điểm bắt đầu và loại lô:** protected paged GET `/api/ledger/trace/starts`, filter pools/sources/all, facility/location/search áp dụng server trước pagination. Scope actor lấy từ persisted role/company; Admin chỉ trace projection, không general stock grant. Dùng forward migration mới cho finite selector routine/private metadata helper và bổ sung kind/tên vị trí vào trace schema v2 (additive). UI mặc định current pools, vẫn cho nguồn/history/manual ID. Phân loại từ receipt/custody/pool facts, không prefix mã lô.
- [x] **S2 — Lab entry:** Capture-owned confirmed-result resolver cùng connection/transaction do trace caller sở hữu; POST `/api/ledger/trace/from-lab` nhận report/result IDs và view, server resolve đúng sample → Capture source → Ledger root. Chỉ actor có cả trace và lab scope; forced forward, không tin request source/owner. Pending/no accepted Ledger lot báo rõ, không tạo stock. Reuse trace traversal/audit; response giữ lab origin refs riêng, không tự đưa lab result thành chemical verdict hoặc saved residue assessment.
- [x] **S3 — UI liên kết:** Table/Flowchart cùng labels source/current pool/old pool/aliquot/remainder/output và vị trí. Report confirmed/trace source evidence có action mở forward trace từ đúng result; vào Traceability theo context report/result, không tự POST trace khi mount. Chọn node có thể mở ResidueBounds hiện có theo `residue.evaluate`, chỉ một node khi người dùng yêu cầu, không N calls cho toàn graph; preview/history/snapshot giữ mốc riêng và hành động ghi audit tường minh.
- [x] **S4 — Verification:** isolated PostgreSQL HTTP cho server filtering >1 page, owner/role/revocation/private helper denial, exact quantities, labels/chronology, confirmed row mapping và mismatch/unconfirmed/pending/foreign denial; UI request lifecycle, filter pagination, manual/history entry, selected-node bounds và lab navigation. Actual browser test host cô lập, không dùng shared DB. Build và affected regressions; ghi kết quả thật.
- [x] **S5 — Bàn giao:** docs request/response + web guide + migration boundary; không migrate/seed/grant trên shared DB và không commit/push khi chưa yêu cầu.

Lab origin là liên kết confirmed evidence được resolve trong lần trace, không phải assessment pháp lý đã lưu. Khoảng dư lượng lấy từ service hiện có với snapshot riêng; positive accounting paths không được dùng làm chemical dependencies. Nếu selector migration chưa cài, API trả lỗi feature deployment rõ; không lùi về một trang stock rồi báo là đủ current pools.


### 13.1. Kết quả thực hiện và kiểm chứng

- S1/S2/S3 đã có trong source trên `feat/trace`: selector server-side có pagination/filter và current labels, node kinds lấy từ receipt/custody facts, Capture-owned resolver cho từng confirmed lab result, HTTP entry forced-forward, Table/Flowchart cùng labels, lab navigation và residue panel theo node. Không thêm dependency npm, không sửa lịch sử migration đã phát hành.
- Đổi role/company xóa request/result/lab context cũ và remount selector để không giữ danh sách của scope trước. Stock → Trace this lot thay thế context lab. Lab resolver recheck confirmed association và company/role trước commit; từ chối không có graph thay vì tạo stock.
- Backend **106/106 PASS** trên PostgreSQL Testcontainers: LotTraceabilityTests, MixedBulkTests, MixedBulkMigrationTests, LabReportTests, ResidueEvaluationTests, PermissionContractTests. Sau bổ sung recheck lab ở cuối transaction, chạy lại nhóm lab-entry + LotTraceability: **12/12 PASS**. Không cộng hai lượt thành số test distinct. Bộ 106 gồm regression migrations nâng từ baseline trước custody đến latest và bảo toàn history/legacy marker.
- Frontend full suite lượt cuối **215/215 PASS**, 29 files, `--maxWorkers=2`; sau thay đổi clear scope và sửa test phải nhập lại lot khi đổi role, nhóm trực tiếp Traceability/TraceStartPicker/Workspace/LabReports **76/76 PASS**. Full suite chưa chạy lại sau thay đổi scope cuối; nhóm affected đã pass. Lượt full trước chạy đồng thời backend/browser có **210 pass/2 fail** tại ResidueRuleMaintenance (timeout/async assertion); lượt full chạy lại khi backend đã xong pass. Không đổi timeout hay code residue để che lỗi; chưa xác minh nguyên nhân duy nhất là contention.
- Web build `tsc --noEmit && vite build` PASS sau thay đổi cuối; còn warning chunk >500 kB. Backend compile qua `dotnet test`, còn CS9113 `LedgerService.access` đã có. `git diff --check` PASS. Không chạy toàn bộ backend solution suite hoặc benchmark production.
- Actual browser: test bridge + Vite riêng **5088/5178**, disposable PostgreSQL. CustomerManager dùng native selector tìm pool RAW_PADDY 10 kg → Backward Current trả 2 lots/1 relation, tên Trace mixed store, source direct HOLD/pool inherited HOLD và available 0. Table/Flowchart cùng trace reference, đổi display không thêm POST.
- Browser mở residue cho node đang chọn: history/preview hiện UNRESOLVED với reasons vì fixture này thiếu applicable evidence/rules; không tạo evaluation/HOLD. Dev StrictMode replay GET history khi mount, không coi đó là N calls cho mọi node; chỉ mở một selected lot. Không đo performance production.
- Browser mở confirmed report → đúng result action → trace context; mount và reload URL không POST. Bấm Run trả **8 lots/7 relationships**, từ nguồn 60 kg đã hết tồn qua DRYING/MILLING/BLENDING và pool/withdrawal/remainder; kinds và vị trí hiển thị đúng, Forward bị khóa. Focused navigate/click/select tools hoạt động; DOM evaluation dùng để đọc kết quả/network. Lần login đầu ERR_CONNECTION_REFUSED do host chưa listen, retry sau khi fixture sẵn sàng thành công.
- Browser host đã dừng; session file chứa test cookies và Vite config tạm được xóa. Không thay `.env`, không truy vấn ghi shared Supabase, không chạy shared migration/seed/grant, không commit/push trong lượt này. Hướng dẫn test mới ở [web guide mục 8](../guides/traceability-web-test-guide.md#8-test-tính-năng-mới-silo-dư-lượng-và-lab--trace-24102026); hợp đồng tại [trace API](../api/traceability-api.md).

### 13.2. Phần để sau và điều kiện tiếp tục

1. **Deployment bản này:** shared DB cần approved owner migration `20261024000000_TraceEntryPoints` cùng runtime-role configuration trước khi backend mới chạy. Đây chưa phải đã triển khai; startup chủ động dừng nếu thiếu routine/grant hoặc helper bị lộ EXECUTE. Giữ restricted runtime, không dùng owner làm application connection.
2. **Core trace đến container:** chờ actual committed loading/output contracts (order/approved blend/reservation, loaded lots + exact quantities, container/CLOSE identity và evidence authority). Khi có mới thêm material/custody links và container dossier; preparation metadata không phải actual loading. Không tạo shipment/stock giả.
3. **Kết luận ảnh hưởng lab:** entry hiện truy material descendants của sampled source. Không khẳng định một kết quả áp dụng trực tiếp cho mọi hậu duệ, không tự HOLD/release hoặc kết luận đạt thị trường. Dùng residue bounds/rules/evidence service hiện có; missing policy/coverage phải unresolved. Khi cần immutable lab-to-trace evidence reference trong query audit, phải bổ sung hợp đồng snapshot rõ; hiện labOrigin chỉ là response context, TRACEBACK audit vẫn ghi root/graph.
4. **Full MVP chưa được tuyên bố hoàn tất:** actual container traceback/dossier, action HOLD theo authority và export assessment còn phụ thuộc phần sở hữu. Extended/out of scope mục 12.2–12.3 tiếp tục không triển khai. Benchmark theo deliverables cần dataset/acceptance target riêng; không dùng smoke này làm bằng chứng tốc độ hoặc coverage toàn dự án.


### 13.3. Sửa deployment/runtime grants theo yêu cầu — 24/10/2026

Người dùng yêu cầu “là sửa chưa, sửa đi” sau khi được giải thích thiếu migration và quyền EXECUTE trên DB dùng chung. Đã chạy preflight chỉ đọc bằng cấu hình hiện hành: runtime login khớp `Ledger:RuntimeRole`; `trace_lot` có nhưng thiếu EXECUTE; `trace_starts` và metadata helper chưa tồn tại. Identity/Catalog/Capture không có pending migration; Ledger chỉ thiếu `20261024000000_TraceEntryPoints`.

- Đã chạy workflow `dotnet run --project backend/src/GrainLedger.Api -- --migrate`: áp dụng đúng migration trên, sau đó `ConfigureRuntimeRoleAsync` cấp lại finite runtime grants. Không seed, không đổi connection settings, không dùng owner cho normal application startup. Migration thay routine, không tạo stock/receipt/sample hoặc viết lại lineage.
- Postflight: cả bốn contexts không pending; runtime EXECUTE trace_lot và trace_starts đều true; metadata helper tồn tại nhưng runtime EXECUTE false; ledger.command vẫn EXECUTE true. `LedgerDatabase.ValidateRuntimeAsync` bằng restricted connection PASS.
- Actual normal startup `dotnet run --project backend/src/GrainLedger.Api --no-build` thành công; `GET http://localhost:5080/health` trả HTTP 200 `{"status":"ok"}`. Không chạy authenticated trace trên shared DB vì nó ghi TRACEBACK audit. Backend dùng để kiểm tra đã dừng sau smoke để không giữ port 5080 của người dùng.
- Nguy cơ còn lại: local dev hiện có blanket revoke/regrant với allowlist chưa chứa tracing. Chạy owner migration/grant workflow từ bản đó có thể thu hồi lại quyền trace. Không xác định người/process đã sửa ACL trước đó. Source feat/trace đã có finite grants mới; cần tích hợp migrations và grants cùng nhau vào nhánh deploy chung. Chưa merge vào dev, commit hoặc push trong lượt sửa DB này. Chuyển branch/restart bình thường không gọi workflow thu hồi quyền.

Mục 13.1–13.2 ghi trạng thái trước phê duyệt deployment; ghi nhận này cập nhật trạng thái shared DB mới nhất. Actual loading/container dependencies vẫn giữ nguyên.


## 14. Bàn giao trước merge dev và plan lần sau — 24/10/2026

### 14.1. Kết luận trạng thái

**Đã hoàn thành phạm vi đợt hiện tại: tracing các Ledger lots/pools và processing/custody đã commit, cùng lab entry/evidence. Có thể đưa phần này vào review và merge dev như một mốc chức năng độc lập; chưa đánh dấu toàn bộ tracing MVP của dự án hoàn tất.** Việc merge không yêu cầu chờ đội order/blend/loading làm xong, nhưng full source/lab ↔ actual container traceback và dossier chưa thể nghiệm thu.

Đã có:

- [x] Forward/backward; Current effective operations/History giữ superseded operations; nguồn đã hết tồn và historical generations vẫn truy được.
- [x] Mixed-bulk ingress/withdrawal/remainder, actual resolved processing input và persisted source allocation; không bỏ provenance trước silo. Shares kế toán và chemical dependencies được giải thích riêng.
- [x] Table/Flowchart cùng response/snapshot; starting lot khác selected node; selector current pools/sources/all, server-side location/facility/search/pagination, manual Ledger ID.
- [x] Nguồn thu mua → farmer/plot/season metadata; samples/confirmed report sections theo đúng nguồn và quyền. Đây chưa phải toàn bộ audit thay đổi đăng ký thửa ruộng.
- [x] Confirmed lab result → server resolve đúng accepted source → forward material graph. Không tự coi kết quả đo nguồn là nồng độ mọi hậu duệ.
- [x] Mở residue preview/history theo selected node, reuse module hiện có, explicit snapshot action; không gọi preview cho mọi node khi mount graph.
- [x] Hiển thị direct/effective HOLD và available stock. API đặt/respond HOLD đã tồn tại trong Ledger; phần trace không tự đặt hoặc release HOLD.
- [x] Persisted role/company scope, Export forward/redaction, Admin không có lab wildcard, query audit và guards công bố. Lab origin refs hiện chỉ nằm trong response, xem R2.
- [x] Migration `20261024000000_TraceEntryPoints` và finite runtime grants đã cài trên DB chung theo phê duyệt, runtime validation/health pass (13.3). Không cần chạy lại chỉ vì restart. Không suy deployment sang môi trường khác từ kết quả này.

Bằng chứng kiểm tra dùng kết quả thật tại 13.1/13.3: backend 106/106, sau thay đổi recheck lab nhóm 12/12; frontend full 215/215, sau thay đổi scope nhóm affected 76/76; web build PASS, browser Testcontainers PASS. Không cộng các lượt thành số test distinct; chưa có full suite cuối trên merge commit tương lai, chưa benchmark NFR. User đã gửi ảnh pool 800 kg trên web; không coi đó là nghiệm thu tất cả ca trong walkthrough.

### 14.2. Các việc còn lại — chỉ in scope

- [ ] **R1 — Dễ đọc UI (độc lập, ưu tiên sớm):** tách dòng/spacing nhãn Type/storage và Stage/revision; ảnh hiện có `Current poolDemo Dry Store`, `RAW_PADDYCurrent pool`. Dùng custody evidence có sẵn để giải thích nguồn/generation tồn 0 đã chuyển vào pool; không suy mọi stock 0 là do pooling. Giữ số chính xác và bảng/graph thống nhất. Đạt khi ảnh desktop/mobile không dính nhãn, starting lot dễ nhận diện, không thay totals hoặc accounting.
- [ ] **R2 — Hoàn thiện lab-to-trace evidence/audit (độc lập với container):** hiện TRACEBACK lưu query root/scope/view/basis/lot IDs, không lưu `reportId/resultId/sampleId` của lab entry và không giữ toàn bộ graph/evaluation response. Thiết kế minimal immutable origin refs cùng association/version và link evaluation đã lưu khi người dùng chọn, trong cùng audit transaction/đúng quyền; không copy report bytes hoặc tự lưu evaluation ở mount. Phân biệt rerun theo current evidence với xem căn cứ lúc chạy. Reuse residue replay/history để giải thích chemical dependencies theo chất/basis, kể cả accounting share về 0, thay vì suy từ structural descendants. Đạt khi có regression cho multi-sample report, late/replacement evidence và reauthorization, old refs không bị rewrite; UI không tuyên bố trace reference tự tái dựng chứng nhận cũ.
- [ ] **R3 — Entry trực tiếp từ plot/vụ/source record (đối chiếu acceptance F18 trước khi chốt đầu vào):** hiện đã xem được plot/vụ trong source composition, nhưng các entry trace chỉ có Ledger lot và confirmed lab result; chưa tìm thửa/vụ để truy đồng thời mọi source lot của thửa/vụ đó. Khi làm, Capture cung cấp scoped paged/bulk mapping sang accepted Ledger roots; hỗ trợ nhiều procurements và nguồn chưa nhận mà không tạo stock. Không gọi N trace requests rồi cộng trùng generations hoặc tiết lộ source composition cho Export. Đạt khi đúng company/season, không thiếu nguồn qua pagination, phân biệt hiển thị origin đã có với entry tìm theo plot chưa có. Không mở module audit đăng ký ruộng mới trong nhánh trace.
- [ ] **R4 — Actual loading/container traceback (chờ contract của luồng sở hữu):** owner order/blend/reservation/loading cung cấp actual committed load IDs, consumed lots/aliquots, exact kg, reversals/corrections, container identity và CLOSE/dossier refs. Sau đó nối source/confirmed lab → affected stock/actual containers và container → tất cả contributing sources/plots/percentages/events. Planned targets/drafts/preparation metadata không tạo material edge. Đạt khi partial/multiple loading, mixed-bulk resolved inputs, correction và owner scope không double count; closed evidence giữ bất biến và late evidence được đọc tách.
- [ ] **R5 — Hồ sơ xuất và response theo quyền (sau contract R4, phối hợp compliance/HOLD):** gắn truy xuất với closing dossier/document export hiện thực tế; phân biệt material relationship, kết quả residue/assessment và hành động xử lý. HOLD/response đi qua API/authority hiện có, có lý do/evidence/audit; không auto-HOLD, auto-release hay dùng trace thay điều kiện CLOSE. Đây là tích hợp các phần của MVP, không yêu cầu trace tự viết engine compliance, order approval hoặc reservation. File export/API cần cùng owner/redaction scope và exact mass với UI.
- [ ] **R6 — Nghiệm thu hiệu năng và coverage cuối (có thể chuẩn bị lot/pool trước R4):** chốt dataset/acceptance target từ proposal với team, đo full-season fan-in/fan-out/depth/top-up/withdraw, role/concurrency và correctness; tách cold/warm DB/API/page, dùng production web build cho loading measurement. Backend hiện guard 5.000 lots/20.000 edges và graph UI 250 nodes/500 relationships; chưa chứng minh đáp ứng dataset toàn mùa vụ. Đạt khi công bố đủ dataset/counts/môi trường/kết quả và hành vi quá giới hạn; không silent truncate hoặc lấy timeout làm bằng chứng đạt NFR. Khi có R4 bổ sung actual container cases.

Thứ tự lần sau: **R1 → chốt R2/R3 theo acceptance → benchmark lot/pool R6** có thể làm ngay; **R4 → R5 → nghiệm thu container R6** khi team có actual loading/closing contracts. Không dùng dependency container để trì hoãn sửa UI hoặc benchmark lot hiện có. R2/R3 không phải lý do đánh phần lot/pool đã triển khai là chưa dùng được; là các gap cần chốt khi nghiệm thu đầy đủ tracing MVP.

Tiếp tục loại trừ toàn bộ extended/out of scope ở mục 12: không optimizer nhiều container, portal ngoài, EPCIS, native Android, dự đoán vệ tinh/thời tiết, carbon credits, thanh toán hay ERP/customs.

### 14.3. Lưu ý khi merge vào dev

- [ ] Commit toàn bộ implementation/contracts/tests/docs và **hai file migration/resource mới** `TraceEntryPointsMigration.cs` + `TraceEntryPoints.sql`, cùng grant changes trong `LedgerDatabase.cs`. Những file untracked hiện chưa nằm trong Git history; không chỉ merge commit cũ rồi nghĩ code mới đã đi theo.
- [ ] Review target dev mới nhất, xử lý conflict rồi chạy affected regressions/build trên kết quả tích hợp. Bản local dev tại thời điểm rà không có commit đi trước feat/trace (`dev...feat/trace = 0/10`), nhưng chưa fetch xác minh remote trong lượt cập nhật docs này. Không suy remote/PR CI đã xanh.
- [ ] Giữ cả finite EXECUTE grants `trace_lot` và `trace_starts`, deny direct metadata helper. Không lấy allowlist dev cũ ghi đè: đó là cơ chế có thể thu hồi quyền trace trên DB chung. Shared deployment cần một revision tích hợp thống nhất và người/job sở hữu; không cấp EXECUTE tất cả functions để né lỗi.
- [ ] CI/Testcontainers kiểm migration mới từ clean DB và upgrade; khi deploy môi trường khác chạy approved migration workflow + runtime validation, không seed/reset hoặc dùng owner cho normal startup. DB chung hiện đã apply thì workflow không cần áp dụng lại migration cũ.
- [ ] Smoke web theo [4 luồng đã chuẩn bị](../guides/traceability-web-walkthrough.md): backward pool → source composition, forward source, confirmed lab entry, residue selected node; kiểm thêm effective HOLD và role redaction trên fixture cô lập. Ghi kết quả thật; không tạo data-changing shared scenarios khi chưa được phép.

**Không coi R1–R6 là đã hoàn thành chỉ vì merge dev.** Mốc merge nên mô tả “lot/pool tracing, confirmed lab entry và residue evidence links”; không ghi “full end-to-end container tracing completed”. Lượt này chỉ cập nhật plan, không commit/push/merge hoặc thay database.
