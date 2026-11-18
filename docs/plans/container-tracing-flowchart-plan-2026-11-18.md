# Plan: bổ sung container vào Flowchart tracing

**Trạng thái: chỉ lập kế hoạch, CHƯA triển khai.** Người dùng yêu cầu dừng ở plan. Không sửa code, chạy migration/grant/seed, tạo fixture hoặc thực hiện trace có audit trên DB dùng chung trong lượt lập plan này. Không commit/push khi chưa được yêu cầu.

## 1. Mục tiêu và căn cứ

Cho người dùng theo dõi liên tục **nguồn → processing/custody → lot thành phẩm → container đã thực nhận hàng**, rồi từ trang container xem ngược các lot và nguồn đã đóng góp.

- [SRS UC-24](</home/phucpinku/Desktop/Week1_report/review/Report3_Software Requirement Specification.docx.md:144>) yêu cầu trace nguồn/results xuôi tới container và container ngược về source contributions/plot-level mass shares.
- [SRS FR-TRC-001](</home/phucpinku/Desktop/Week1_report/review/Report3_Software Requirement Specification.docx.md:445>) yêu cầu đủ kết quả được phép, giữ source kg, company scope và phân biệt tracing với HOLD.
- **Node container trên Flowchart, thao tác bấm mở hồ sơ và bố trí các màn hình là giải pháp UI đề xuất**, không phải giao diện bắt buộc nguyên văn của SRS.

Đây là bổ sung trình bày và contract đọc cho tracing đã có, không mở nghiệp vụ shipment mới. Không kéo vấn đề mapping commodity PADDY/RICE hay các sửa UI container khác vào plan này.

## 2. Hiện trạng đã đối chiếu

| Thành phần | Đã có | Còn thiếu cho mục tiêu |
|---|---|---|
| Ledger → Traceability | Chọn lot, forward/backward, Table/Flowchart, lot/processing/custody và source details | Flowchart chưa có container node/loading edge. |
| Actual container targets | Forward trả danh sách container thực nhận hàng, trạng thái/restriction và linkedLoadedKg; có link mở hồ sơ | Danh sách aggregate chưa đủ để dựng chính xác mọi cạnh lot → container. |
| Trang container của CM | Trace container sources trả actual source kg; dossier và actual loads đã lưu | Chưa có Flowchart backward với container làm điểm bắt đầu. |
| Flowchart renderer | Layout và namespace cho lot/operation/custody, zoom/fit/focus, giới hạn graph và fallback Table | Thêm loại node/edge container/loading và focus container. |

Source chính: [LotTraceability](../../backend/src/GrainLedger.Api/Features/Ledger/Traceability/LotTraceability.cs), [container execution](../../backend/src/GrainLedger.Api/Features/Exports/Shipments/ContainerExecutionEndpoints.cs), [LedgerTraceability UI](../../apps/web/src/LedgerTraceability.tsx), [trace-flow](../../apps/web/src/trace-flow.ts), [TraceFlowchart](../../apps/web/src/TraceFlowchart.tsx), [ContainerExecution UI](../../apps/web/src/ContainerExecution.tsx).

## 3. Phạm vi sẽ triển khai sau khi được yêu cầu

### T01 — Forward từ lot: container là đích cuối

- Mở rộng dữ liệu đọc để xác định các actual load lines liên kết lot đã reached với container. Không suy cạnh từ reservation, draft, plan hoặc chỉ từ danh sách aggregate targets.
- Thêm node container: internal code, trạng thái hiện hành, HOLD/restriction theo quyền. Dùng namespace riêng, không trùng UUID của lot/event.
- Nối lot thực load → container bằng loại quan hệ **Actual loading**, phân biệt với processing/custody.
- Gom cạnh theo lot/container với tổng actual kg chính xác và giữ references từng load line/location để mở chi tiết. Nhiều load lines không tạo nhiều node trùng cho cùng container.
- Chỉ vẽ các cạnh load có căn cứ trong trace scope; không nối nguồn thẳng vào container như thể nguồn thô đã được xếp trực tiếp.
- Giữ bảng Actual container targets hiện có để đối chiếu và làm fallback.

### T02 — Từ trang container: backward Flowchart

- Giữ nút **Trace container sources** trong trang container. Sau khi trace, cho chuyển **Table / Flowchart**; Table nguồn hiện có vẫn dùng được.
- Dùng container đang mở làm điểm bắt đầu; nối từ tất cả actual-loaded lot/position của container đó, rồi lấy hợp nhất ancestry được phép qua processing/custody tới nguồn.
- Bao phủ mọi committed load và proposal/plan version liên quan, không chỉ active plan hoặc load mới nhất.
- Không đưa container khác cùng dùng chung một lot vào backward graph của container đang chọn. Không lọc mất input hợp lệ của một operation chỉ để làm hình gọn.
- Giữ hướng vật liệu nhất quán với renderer hiện tại: nguồn ở trái, processing/lot ở giữa, container ở phải. Badge/focus chỉ ra container là điểm bắt đầu backward; không đảo chiều mũi tên vật liệu.
- Container chưa có actual loading: hiện node container riêng và thông báo chưa có actual loading, không dựng ancestry giả từ plan/reservation.
- Lot hết tồn vẫn được truy xuất; thiếu ancestry/provenance phải báo incomplete, không tự gọi graph là complete.

### T03 — Điều hướng trên node container

- Bấm node container trên forward graph mở `/export/containers/{id}` theo cơ chế navigation hiện có.
- Trang container vẫn áp dụng quyền/scoping backend như trước; khả năng nhìn một trace target không tự cấp quyền đọc full dossier hoặc sources.
- Từ trang container, CM dùng Trace container sources để xem ngược. Giữ hành vi chọn lot/inspection trong graph hiện tại.
- Hỗ trợ keyboard, focus và accessible labels cho node/link. Không bắt người dùng lấy UUID để đi từ graph tới hồ sơ.

**Chưa thêm bộ chọn container riêng vào Find a tracing starting point của Ledger.** Picker đó tiếp tục chọn lot; backward từ container đi qua trang container. Backward bắt đầu từ lot vẫn chỉ về upstream ancestry, không chèn container downstream vào kết quả.

## 4. Contract số liệu và phân quyền — điều kiện bắt buộc

### Không trộn các đại lượng

| Đại lượng | Ý nghĩa | Ví dụ |
|---|---|---|
| Lot produced kg | Khối lượng lot được tạo trong lịch sử | Lot blend được tạo 20 kg. |
| Loading edge kg | Actual kg từ đúng lot/position load vào container | Container C1 nhận 4+6 = 10 kg từ lot đó. |
| linkedLoadedKg của forward target | Actual kg từ các lot trên đường trace vào container; có thể chỉ là một phần manifest | Không mặc định bằng toàn bộ target hoặc toàn bộ container loaded kg. |
| Source-attributed kg | Phần nguồn đóng góp vào actual loading của container | Hai nguồn chia 50/50 → mỗi nguồn 5 kg trong C1. |

Không dùng full-lot composition 10+10 kg để trình bày rằng C1 nhận 20 kg. Giữ source shares từ allocation đã lưu theo từng actual load line; không phân bổ lại theo tỷ lệ lịch sử sau khi có partial loading/custody rounding. Không dùng JavaScript Number cho tính toán mass; API trả decimal strings và backend dùng exact numeric/micro-kg như contract hiện hành.

- CM backward chỉ trong owner-company scope được phép. Export giữ quyền forward hiện tại, không nhận thêm source composition/lab/private assessment từ graph payload mới.
- Backend kiểm persisted role/permission/company/facility khi áp dụng, kể cả request trực tiếp; không dựa vào việc ẩn nút.
- Material graph, actual loads, source shares và target headers phải thuộc snapshot đọc nhất quán; giữ fresh authorization recheck và freshness của restriction. Không response cache nếu chưa có yêu cầu invalidation.
- Trace không trừ stock, đổi reservation, sửa CLOSE/dossier hoặc tạo/release HOLD. Giữ audit TRACEBACK đúng contract hiện có; không tạo thêm một audit cho mỗi lot do gọi lặp endpoint, hoặc mỗi lần đổi Table/Flowchart.
- Graph thể hiện dữ liệu hiện hành theo trace view; dossier là snapshot lịch sử lúc CLOSE. Badge HOLD/restriction mới không sửa dossier.

## 5. Thiết kế kỹ thuật cần chốt trước khi code

- [ ] Kiểm kê response forward hiện tại và các bảng actual load/line/source; xác định field tối thiểu còn thiếu cho typed container/loading nodes, references và root target.
- [ ] Chốt contract mở rộng tương thích: phân biệt root lot/container, node type và quantity basis; giữ route/payload lot trace hiện có và table callers. Không tái sử dụng targetLotId để chứa container ID.
- [ ] Chốt cách lấy ancestry cho nhiều actual-loaded lots bằng bulk query/read contract; không gọi một HTTP request/trace audit cho từng load line. Dùng in-process/module contracts, không chuyển EF entities qua module boundary.
- [ ] Chốt semantics current/history với corrected/superseded lots và retained loads; không mất committed manifest hoặc lẫn hai thế hệ stock vì gom UUID không đúng.
- [ ] Nếu cần đổi protected SQL routine/grant: chuẩn bị forward migration đúng module và chỉ quyền cần thiết. Không mặc định task này không cần migration; cũng không thay toàn bộ grant set. Shared deployment cần phê duyệt riêng.

Không cần đổi storage model hay backfill business history chỉ để vẽ graph. Nếu dữ liệu cũ thiếu quan hệ cần thiết, trình bày thiếu dữ liệu đúng sự thật; không tạo load/lineage mới để lấp graph.

## 6. Thứ tự thực hiện khi được yêu cầu triển khai

1. Chốt contract T01/T02, role redaction và oracle số liệu.
2. Hoàn tất phần đọc backend/bulk composition và boundary authorization cần thiết.
3. Mở rộng graph builder/renderer cho container/loading, navigation và backward graph tại trang container.
4. Cập nhật callers, label giải thích kg và table fallback; cập nhật API/test guide.
5. Gom xong lượt triển khai rồi chạy **một pha kiểm thử tập trung**; lỗi phát hiện mới nhận targeted rerun. Không chạy toàn bộ backend giữa các đoạn code.

Không gán thêm sprint hoặc deadline khi chưa có capacity/ngày bàn giao. Các checkbox plan hiện đều chưa hoàn thành, không phải kết quả nghiệm thu.

## 7. Các ca kiểm chứng và nghiệm thu

| Ca | Kết quả phải chứng minh |
|---|---|
| Forward từ source A qua nhiều operations tới container | Đúng các lot/processing/custody và container thực nhận hàng; source A không bị gắn trực tiếp vào C1 như một physical loading edge. |
| Forward từ finished lot | Có node C1, cạnh load đúng 10 kg, click mở đúng hồ sơ. |
| Backward từ finished lot | Giữ upstream graph; không thêm container downstream. |
| Backward từ C1, partial load 4 kg | Đúng actual scope 4 kg và source 2+2 kg trên fixture 50/50. |
| Backward từ C1, hai load đủ 10 kg | Một container node, đầy đủ load refs, source 5+5 kg; lot produced 20 kg vẫn có nhãn lịch sử riêng. |
| Một lot vào C1 và C2 | Forward thấy cả hai, không đếm trùng; backward C1 chỉ có manifest/sources của C1. |
| Một container nhận nhiều lot/locations/P1-P2 | Graph/table gồm mọi committed load đúng lot và proposal gốc, không gắn hết vào active plan. |
| Source attribution không bằng whole-container linked kg | Fixture có mixed input/lot khác; graph/table không gọi linkedLoadedKg là kg riêng của source hoặc là full target. |
| Micro-kg/partial/custody | `4.000001` + `5.999999` đúng 10 kg; source-wise complement khớp persisted load allocations, không tự tính lại bằng float. |
| Chưa load / chỉ reserve / exhausted lot | Không tạo actual edge từ draft/reservation; exhausted historical lot vẫn truy được. |
| CLOSED rồi có HOLD/evidence mới | History/dossier bất biến, current badges/finding đúng; không suy PASS hoặc quyền load từ graph. |
| Thiếu lineage/provenance / failed request | Hiện incomplete/error rõ, không giữ graph cũ như một kết quả mới thành công. |
| Tenant B / sai role / revoked authority | Không lộ target, edge, source, file/evidence; direct route cũng bị chặn đúng. Export không có backward graph nguồn. |
| Graph lớn | Đếm cả node/edge mới trong limit hiện có 250 nodes/500 relationships; vượt limit chuyển Table với thông báo, không cắt kết quả rồi gọi complete. |
| API/query workload | Bulk đọc, không N+1 theo lot/container; kiểm đầy đủ targets so với oracle và tách warm handler/page timings nếu có đo. |
| Desktop/mobile/keyboard | Các node không chồng nhãn nghiêm trọng, zoom/fit/focus cả container root, click/keyboard navigation đúng. |

Khi triển khai, bổ sung focused regression cho authorization/accounting/graph semantics; không viết test chỉ lặp lại implementation. Chạy frontend graph/trace/container tests, build, các backend trace/container tests trực tiếp liên quan và actual API/browser path trên fixture cô lập. Không tạo lại dataset full-season hoặc chạy toàn backend mặc định khi thay đổi chưa ảnh hưởng truy vấn; chỉ mở rộng benchmark nếu query/performance concern thực tế đòi hỏi.

## 8. Tiêu chí bàn giao

- [ ] T01/T02/T03 đạt các ca có liên quan; code/API/UI/docs cùng contract, số liệu không nhầm lot mass với container/source mass.
- [ ] Có bằng chứng web: source → container node → hồ sơ → backward graph nguồn; giữ Table đối chiếu.
- [ ] Kết quả ghi rõ PASS, chặn đúng, PARTIAL/NOT RUN và giới hạn; không suy mọi CT cũ đều đã chạy lại.
- [ ] Không sửa business stock/history/dossier, không mở thêm quyền và không kéo các nghiệp vụ đã loại sau HOLD vào scope.
- [ ] Nếu có migration/grant, package và kế hoạch deployment được review riêng; implementation/isolated verification/shared deployment báo thành các mốc riêng.

Kịch bản demo hiện tại vẫn dùng [hướng dẫn container/tracing](../guides/container-tracing-lecturer-demo.md) và bảng Actual container targets. Cho tới khi plan này được triển khai và nghiệm thu, không hướng dẫn giảng viên rằng container đã là node trên Flowchart.
