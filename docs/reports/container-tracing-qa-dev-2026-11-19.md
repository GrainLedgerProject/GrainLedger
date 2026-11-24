# Báo cáo Container và Tracing theo GrainLedger V2 và tài liệu R2

Kiểm thử ngày 19/11/2026. Đối chiếu yêu cầu ngày 23/11/2026. Mã được kiểm ở commit `464746e` (`Feat/container (#25)`), trùng HEAD của `dev` và `feat/container-fix` tại thời điểm đối chiếu.

Ở bản dev được rà soát, luồng chính chạy được từ phương án đã duyệt, nạp từng phần đến CLOSE và truy vết hai chiều qua manifest. Dossier giữ nguyên sau HOLD. Đợt đối chiếu đó ghi nhận **6 nhóm yêu cầu chưa đáp ứng đầy đủ** và **7 lỗi hoặc điểm bất nhất**. Các mục bên dưới giữ lại hiện trạng và bước tái hiện ở bản dev này.

## Bản sửa sau tái kiểm thử

Đã sửa cả sáu lỗi tái hiện trên nhánh `feat/container-fix`. Kết quả dưới đây dùng bản sửa trên PostgreSQL cô lập và web build production.

| Lỗi | Kết quả sau sửa |
|---|---|
| 1. Lô xuất hàng bị ảnh hưởng vẫn chuyển kho thường được và cách ly thất bại | Trace báo HOLD và `0` kg khả dụng. API chặn chuyển kho thường. Mill Manager mở cảnh báo của lô bị ảnh hưởng, cách ly đúng lô bằng mã HOLD gốc và ghi nhận phản hồi. |
| 2. Có thể chọn lại lô bị ảnh hưởng cho container khác | Preview hiển thị hạn chế `HOLD` và `0` kg khả dụng. Backend kiểm lại phạm vi HOLD khi giữ chỗ, kể cả khi lô đang được chọn lần đầu cho container. |
| 3. Mất phản hồi rồi đổi lô tạo hai HOLD | Form lưu yêu cầu đang gửi theo tài khoản, công ty và đối tượng. Chromium đã mô phỏng phản hồi mất sau khi API ghi thành công. Đổi lô rồi quay lại và retry dùng cùng mã yêu cầu. API chỉ lưu một HOLD. |
| 4. Mill Manager thiếu lịch sử HOLD trước lần nạp đầu | Mill Manager phụ trách cơ sở của phương án thấy sự kiện đặt HOLD và phản hồi `ACKNOWLEDGE` của container `PLANNED`. Các bài test xác nhận Mill Manager ngoài phạm vi và tài khoản công ty khác bị giới hạn. |
| 5. Hai form HOLD trên màn container | Màn chi tiết còn một form đặt HOLD yêu cầu evidence, kể cả khi bảng trace đang mở. |
| 6. Packing List thiếu nguồn tạo | Danh sách phân biệt `Generated from dossier` và `Uploaded file`. Các chứng từ đã lưu trước migration hiện `Origin not recorded`. Phiên bản và nội dung chứng từ cũ được giữ nguyên. |

Hai ca kiểm thử kho thường phát hiện phạm vi HOLD kế thừa ban đầu quá rộng. Phạm vi cuối giữ quy tắc HOLD trực tiếp cho lô kho thường ngoài luồng xuất hàng. Khi chọn lô cho container, preview và bước giữ chỗ kiểm tra HOLD theo nguồn đóng góp.

- `dotnet test GrainLedger.slnx` đạt **655/655** trên PostgreSQL Testcontainers. Vitest đạt **259/259** và `npm run build` thành công.
- HTTP và Chromium qua luồng loading, CLOSE, hoàn thành đơn, Packing List, trace hai chiều, truy xuôi từ lab đã xác nhận và quyền xem audit. Chromium kiểm thêm Fit graph trên desktop và mobile, giới hạn quyền Administrator, HOLD có evidence, retry sau khi mất phản hồi và lịch sử HOLD trước khi nạp.
- Migration [`20261123010000_ContainerTraceCorrections`](../../backend/src/GrainLedger.Api/Data/Migrations/20261123010000_ContainerTraceCorrections.cs) đã chạy trong môi trường test cô lập. Supabase vẫn dùng migration đã triển khai trước đó.

## Tái kiểm thử trước khi merge ngày 23/11/2026

Bản kiểm thử là commit `ad76034` trên `feat/container-fix`. Dùng PostgreSQL Testcontainers, API qua HTTP loopback và build production của web. Dữ liệu, evidence và phiên đăng nhập là dữ liệu tổng hợp. Lượt này chạy lại cả bộ test backend và web, sau đó kiểm riêng những tình huống nằm ngoài các ca hồi quy đã có.

**Kết luận cập nhật:** loading, CLOSE, tạo Packing List và truy vết trên luồng chính đạt. HOLD lô nguồn, retry HOLD và lịch sử của Mill Manager còn lỗi tái hiện được. Chưa đạt điều kiện merge.

### Lỗi đã tái hiện

1. **Cao · Lô phối trộn bị ảnh hưởng được chuyển kho thường nhưng không cách ly được (F19).** Nạp 4 kg từ một lô phối trộn rồi đặt HOLD lên lô nguồn có đóng góp. Mill Manager nhận cảnh báo cho lô phối trộn. Chuyển 1 kg vào khu cách ly với mã HOLD trả `409` vì lô thiếu effective HOLD. Mill Operator chuyển 1 kg của cùng lô giữa hai vị trí thường nhận `200`. Cùng một trace forward báo lô phối trộn còn kg khả dụng và thiếu cờ HOLD, trong khi container hiển thị hạn chế lô đã nạp. Phạm vi nhận diện lô ảnh hưởng cần nhất quán giữa cảnh báo, tồn khả dụng, chặn di chuyển và cách ly. [Tạo cảnh báo cho lô ảnh hưởng](../../backend/src/GrainLedger.Api/Data/Sql/ContainerTraceCompletion.sql#L82) · [Gác chuyển kho và cách ly](../../backend/src/GrainLedger.Api/Data/Sql/InitialSchema.sql#L1044)
2. **Cao · Có thể duyệt lại lô bị ảnh hưởng cho container khác (F19).** Sau khi đặt HOLD lô nguồn và thu hồi reservation chưa dùng của container đầu, tạo đơn 10 kg mới và chọn lô phối trộn đó. Preview trả `PASS`, API chấp nhận proposal và approval. Actual loading của container mới mới bị chặn với `409`. Kết quả mong đợi là lô bị ảnh hưởng bị loại trước khi được giữ chỗ. [Kiểm tra tồn khả dụng khi reserve](../../backend/src/GrainLedger.Api/Data/Sql/BlendApprovalReservation.sql#L65) · [Kiểm tra hạn chế ở loading](../../backend/src/GrainLedger.Api/Data/Sql/ContainerTraceCompletion.sql#L470)
3. **Cao · Mất phản hồi rồi đổi lô có thể tạo hai HOLD (F19).** Từ trace trên Chromium, đính kèm evidence và gửi HOLD. Mô phỏng mất phản hồi sau khi API đã trả `200`. Form hiện `Retry HOLD`. Chọn lô khác rồi quay lại lô ban đầu trong cùng kết quả trace và gửi lại. Trình duyệt tạo ID mới, API trả `200` lần nữa và danh sách HOLD xác nhận cả hai ID đã lưu. Cần duy trì cùng ID cho lần thử lại dù người dùng đổi lựa chọn hoặc mở lại màn hình. [Lưu retry trong form](../../apps/web/src/TraceHoldForm.tsx#L21) · [Form theo lô đang chọn](../../apps/web/src/LedgerTraceability.tsx#L138)
4. **Trung bình · Business History thiếu HOLD của container chưa nạp đối với Mill Manager được phân công (F20).** Đặt HOLD có evidence cho container `PLANNED` đã có phương án. Mill Manager nhận thông báo và ghi được `ACKNOWLEDGE`. Khi lọc Business History theo `HOLD` và mã container, tài khoản đó nhận 0 sự kiện. Customer Manager thấy đủ 2 sự kiện và tài khoản công ty khác nhận 0. Lịch sử đang xét facility của execution, còn quyền nhận và xử lý HOLD đã có từ facility của phương án. [Điều kiện facility trong Business History](../../backend/src/GrainLedger.Api/Data/Sql/ContainerTraceCompletion.sql#L656)
5. **Thấp · Hai điểm đặt HOLD trên cùng màn container.** Sau khi mở `Trace container sources`, Chromium hiển thị đồng thời nút ở form cũ chỉ nhập lý do và nút ở form trace yêu cầu evidence. Hai cách đặt cùng một HOLD khiến người dùng khó biết yêu cầu chứng từ nào áp dụng. [Các thao tác HOLD trên màn container](../../apps/web/src/ContainerExecution.tsx#L119)
6. **Thấp · Danh sách Packing List thiếu dấu hiệu nguồn tạo.** Sau CLOSE, API chấp nhận upload một tệp gắn loại `PACKING_LIST`, rồi sinh một Packing List từ dossier. Hai mục nằm chung dãy `PACKING_LIST v1/v2`. Giao diện hiển thị loại, phiên bản và tên tệp. Nên hiện thêm nguồn tạo để người dùng nhận biết bản do hệ thống sinh. [Đường nhận chứng từ](../../backend/src/GrainLedger.Api/Features/Exports/Shipments/ContainerWorkflowEndpoints.cs#L125) · [Danh sách chứng từ](../../apps/web/src/ContainerWorkflow.tsx#L106)

### Phần đã kiểm chứng đạt

| Phạm vi | Kết quả |
|---|---|
| Loading và CLOSE | Chặn thời điểm nạp trước ngày tạo lô và sau thời điểm hệ thống. Hai container 5 kg trên cùng đơn chuyển tiến độ từ 5 kg và `IN_PROGRESS` sang 10 kg và `COMPLETE` sau lần CLOSE thứ hai. |
| Production và số liệu | HTTP loopback thực hiện hai mẻ 10 kg, review chất lượng và retry commit bằng cùng ID. Nạp `4.000001 + 5.999999 + 10` kg, CLOSE 20 kg và trace ra đúng 10 kg cho mỗi nguồn. Bộ backend đi qua ca đổi phương án, handoff, bagging, mixed pool, ghi đồng thời, thu hồi quyền và rollback. |
| Truy vết và phân quyền | HTTP kiểm truy xuôi từ lô và kết quả lab đã xác nhận, truy ngược từ manifest, khớp `traceId` với audit và chặn công ty khác tìm audit. Bộ backend kiểm cả việc Export thấy forward theo quyền, backward bị từ chối, và truy vết dữ liệu cũ thiếu provenance. |
| Chứng từ và giao diện | API tải được Packing List có tổng khối lượng đúng. Chromium kiểm được tạo phiên bản, tổng hợp theo thửa, Fit graph tại 1280 và 375 px, trạng thái đơn hàng, HOLD có evidence, Business History và trang từ chối quyền của Administrator. |

`dotnet test GrainLedger.slnx` đạt **653/653**. Vitest đạt **257/257** với hai worker, và web build production thành công. Benchmark tổng hợp 50 nguồn và 1.000 container đạt ngưỡng 30 giây trong cả ba lượt. Thời gian warm HTTP và parse JSON đo được là **1,69–2,04 giây forward** và **2,86–3,14 giây backward**. Các ca bổ sung chạy ngoài repository. T3 Preview báo lỗi khung hình hai lần, nên kiểm thử giao diện dùng Chromium.

## Triển khai ngày 23/11/2026

Thay đổi nằm trên nhánh `feat/container-fix`. Các bước thực hiện và kết quả trong phạm vi đã chốt:

| Bước | Kết quả |
|---|---|
| Tính toàn vẹn của loading và audit | Chặn actual loading trước thời điểm tồn tại của lô hoặc sau giờ hệ thống. Một lần truy container ghi một audit có ID trùng với `traceId` trả về. |
| Truy vết và giao diện | Forward hiện phương án đã lưu và actual loading. Kg quy thuộc của lô nguồn lấy từ phân bổ đã lưu và hiển thị theo quyền đọc thành phần nguồn. Backward gom kg và tỷ lệ theo thửa, kèm nhãn provenance. Quyền mở container được kiểm tra ở bảng và sơ đồ. Fit graph tính theo vùng xem thực tế. |
| HOLD | Customer Manager chọn lô hoặc container từ trace và đính kèm evidence. HOLD lô gửi thông báo cho Export Officer cùng công ty và Mill Manager phụ trách các vị trí xuất hàng bị ảnh hưởng. HOLD có hiệu lực trong cùng transaction với việc thu hồi reservation chưa dùng. Giao diện phân biệt HOLD container và hạn chế lô đã nạp. |
| CLOSE và chứng từ | Đơn hàng tính tiến độ từ kg thực nạp trong container đã CLOSE. Export sinh Packing List dạng HTML in được, có phiên bản và hash. Mẫu sử dụng net kg và thông tin đã ghi nhận trong luồng xuất. |
| Lịch sử và tài liệu | Bổ sung Business history theo công ty, cơ sở và vai trò. Có bộ lọc loại sự kiện, ngày UTC và mã tham chiếu. Hợp đồng API chỉ rõ `version` cho `production/next` lấy từ production task. |

Khối lượng mục tiêu của container tiếp tục cấu hình theo đơn hàng. Kịch bản truy từ lab dùng kết quả đã xác nhận. Các đường đặt HOLD cũ tiếp tục nhận payload hiện hành để các khách hàng API có thể nâng cấp độc lập. Màn đặt HOLD từ trace yêu cầu evidence.

### Kiểm chứng

- Bộ backend qua 653 test trên PostgreSQL Testcontainers. Bản cuối của phần thông báo HOLD qua thêm lượt hồi quy 30 ca Container và Tracing. Web qua 257 test và build production thành công. Lượt web chạy đồng thời với backend có một số ca chạm timeout hoặc chờ giao diện quá hạn. Lượt chạy lại với 2 worker qua đủ 257 ca.
- Smoke qua HTTP loopback kiểm tra hai chiều trace, truy từ kết quả lab đã xác nhận, chặn ngày nạp bất khả thi, tiến độ đơn hàng, tải Packing List và quyền tìm audit.
- Chromium với dữ liệu cô lập xác nhận tổng kg theo thửa, Fit graph ở 1280 px và 375 px, tạo phiên bản Packing List, trạng thái đơn hoàn thành, đặt HOLD kèm evidence từ trace, Business history và thông báo quyền của Administrator.
- Profile tổng hợp 50 nguồn và 1.000 container đo ba lượt trên API đã khởi động. Forward trong khoảng 1,77–1,86 giây, backward trong khoảng 2,99–3,46 giây, so với ngưỡng 30 giây. Các số đo gồm thời gian xử lý HTTP nội bộ và parse JSON trong bài test.

### Áp dụng migration và nghiệm thu web trên Supabase

- Đã áp dụng `20261123000000_ContainerTraceCompletion` sau khi người dùng duyệt. Backup custom-format của schema và dữ liệu `grainledger` đọc được danh sách archive. Đối chiếu count và hash của 124 bảng trước và sau trong transaction. Cấp hai quyền `EXECUTE` cho role runtime, kiểm tra các entry hiện có và xác nhận API chạy bằng role runtime với `/health` trả 200.
- Đăng nhập và đăng xuất thực tế qua form web localhost với Customer Manager và Export Officer. Chrome headless dùng hồ sơ tạm. T3 Preview báo không tạo được screenshot frames ở hai lần thử.
- Customer Manager gọi forward từ lô nguồn, nhận 200 với 2 container và 3 phương án. Container đối chiếu có 10 kg actual loading, trong đó 5 kg từ lô đang truy. Backward nhận 200 với 2 lô nguồn gom thành 1 thửa. `traceId` khớp audit tìm được trong Business History. Fit graph vừa vùng xem 375 px. Chi tiết đơn hàng trả 10 kg đã CLOSE và trạng thái `COMPLETE`. Dossier giữ 2 dòng nạp hàng đã có.
- Export Officer gọi forward nhận 200 với thành phần nguồn được ẩn, gọi backward container nhận 403 và đọc dossier đã lược thông tin nguồn nhận 200. Các thao tác trên Supabase sử dụng hồ sơ có sẵn và phát sinh session cùng audit theo hành vi thông thường của ứng dụng.
- Trên PostgreSQL cô lập, đã chạy lại API chặn ngày nạp sai, ghi actual loading, CLOSE, tải Packing List và trace từ kết quả lab đã xác nhận. Chromium xác nhận sinh thêm phiên bản Packing List, đặt HOLD kèm evidence từ trace, màn Business History và quyền Administrator.

[Hợp đồng Container](../api/container-workflow-api.md) · [Hợp đồng Tracing](../api/traceability-api.md)

## Tài liệu yêu cầu

Các tài liệu PDF dưới đây được cung cấp riêng trong thư mục `Week1_report/New report`.

- **Report 3 – SRS R2** (`SWP490_FA26SE272_R2_Report3_SRS.pdf`) là căn cứ cho use case, yêu cầu chức năng và tiêu chí nghiệm thu. Số trang SRS dưới đây là số in trong PDF.
- **Report 4 – SDD R2** (`SWP490_FA26SE272_R2_Report4_SDD.pdf`) mô tả thiết kế đích. Các chi tiết chỉ xuất hiện trong SDD được ghi rõ tại mục liên quan.
- **GrainLedger V2** (`GrainLedger_V2.pdf`) mô tả luồng xuất khẩu và truy vết F16–F20, kèm ví dụ đối chiếu kg nguồn đóng góp vào container.

## Yêu cầu chưa đáp ứng đầy đủ ở bản dev trước khi triển khai

Các mục sau được xác định bằng đối chiếu PDF với mã trên commit nêu trên. Bằng chứng từ HTTP và trình duyệt ngày 19/11 được ghi tại các mục QA.

### 1. Sinh chứng từ xuất hàng từ hồ sơ đã CLOSE

**GrainLedger V2 F17, tr. 9 · SRS §3.6.5, tr. 39–40 / UC-33.** Export Officer mở container đã CLOSE và yêu cầu hệ thống sinh chứng từ từ hồ sơ đóng hàng. Dữ liệu đầu vào gồm manifest, tỷ lệ nguồn, phê duyệt phương án, đánh giá CLOSE, evidence lab và phiên bản quy tắc. SRS để mở định dạng chứng từ.

Hiện Export có thể tải dossier JSON đã lưu và tải lên hoặc tải xuống các tệp chứng từ theo phiên bản. Đường ghi chứng từ nhận tên và nội dung tệp từ người dùng. **Chưa có thao tác sinh chứng từ từ dữ liệu đóng hàng.** Dossier bất biến và evidence supplements của UC-34/35 đã có. [Lưu chứng từ](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/backend/src/GrainLedger.Api/Data/Sql/ContainerWorkflow.sql#L490) · [Tải dossier JSON](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/apps/web/src/ContainerExecution.tsx#L122)

### 2. Xác định đơn hàng đã hoàn thành từ các container đã CLOSE

**GrainLedger V2 F16, tr. 8 · SRS §3.6.4, tr. 39 / UC-31–32.** Đơn hàng hoàn thành khi tổng khối lượng của các container đã hoàn thành đạt khối lượng yêu cầu. Container CLOSE khi đủ target và đạt kiểm tra toàn bộ manifest.

Mã CLOSE cập nhật trạng thái của container. Chi tiết đơn hàng tính `allocatedMassKg` từ tổng **target đã phân bổ** và từ đó tính lượng chưa phân bổ. **Chưa có kết quả trạng thái hoàn thành đơn dựa trên khối lượng các container đã CLOSE.** Một đơn có thể đã phân bổ hết target trong khi các container vẫn đang PLANNED. [Tính khối lượng phân bổ](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/backend/src/GrainLedger.Api/Features/Exports/Orders/ExportEndpoints.cs#L131) · [Ghi nhận CLOSE](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/backend/src/GrainLedger.Api/Data/Sql/ContainerWorkflow.sql#L960)

### 3. Truy xuôi đến phương án bị ảnh hưởng và khối lượng quy thuộc

**GrainLedger V2 F18, tr. 5–7 và 9 · SRS UC-25, tr. 13 và §3.7.1, tr. 40.** UC-25 yêu cầu Customer Manager và Export Officer tìm được lô, phương án và container bị ảnh hưởng trong phạm vi công ty. Phần chức năng mô tả cùng các đích downstream và khối lượng quy thuộc.

Forward hiện trả các lô, vị trí và container có actual loading. `proposalId` xuất hiện theo load line đã commit. **Chưa có danh sách phương án bị ảnh hưởng khi hàng mới được chọn hoặc giữ chỗ.** Với lô blend nhiều nguồn, forward trả kg của dòng nạp có liên quan. Ví dụ, một dòng nạp 10 kg có thể chứa 5 kg từ nguồn đang truy. Số 5 kg có ở backward của container, còn forward chỉ hiện 10 kg linked loading. Ví dụ V2 dùng nguồn L3 chiếm 11.600 kg trong blend 70.000 kg và container nạp 25.000 kg. Test phân bổ hiện có xác nhận L3 đóng góp chính xác `4142.857143` kg, còn kết quả forward chưa hiện số này cho nguồn đang truy. [Ghép actual targets trong HTTP](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/backend/src/GrainLedger.Api/Features/Ledger/Traceability/LotTraceability.cs#L110) · [Tính linked loading](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/backend/src/GrainLedger.Api/Data/Sql/ContainerTracingFlowchart.sql#L213) · [Test phân bổ 25.000 kg](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/backend/tests/GrainLedger.Api.IntegrationTests/ContainerExecutionTests.cs#L164)

### 4. Tổng hợp khối lượng và tỷ lệ theo thửa

**GrainLedger V2 F18, tr. 9 · SRS UC-24, tr. 13 và §3.7.1, tr. 40.** Trace ngược phải cho thấy các lô nguồn, thửa và tỷ lệ đóng góp theo thửa.

API đang tính kg và tỷ lệ theo **lô nguồn** rồi bổ sung provenance. Khi hai lô cùng một thửa, API và UI hiện hai dòng riêng. **Chưa trả tổng kg và tỷ lệ của thửa trong container.** Bằng chứng QA CT-06 xác nhận hai dòng có chung UUID thửa. CT-06 còn ghi nhận vấn đề hiển thị nhãn thửa trên giao diện. [Tính tỷ lệ từng lô nguồn](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/backend/src/GrainLedger.Api/Features/Exports/Shipments/ContainerTraceEndpoints.cs#L46) · [Bảng nguồn trên web](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/apps/web/src/ContainerSourceTrace.tsx#L21)

### 5. Đặt HOLD từ truy vết và áp dụng đầy đủ hạn chế

**GrainLedger V2 F19, tr. 9 · SRS UC-26–28, §3.7.2, tr. 40–41 và BR-15, tr. 47 · SDD §3.8.4.** Customer Manager chọn lô hoặc container từ evidence hoặc traceback để đặt HOLD. Hệ thống ghi HOLD cùng thông báo cho các vai trò chịu trách nhiệm và chặn planning, sử dụng trong production, loading và CLOSE thuộc phạm vi bị giữ.

- **Đặt HOLD từ kết quả trace và lưu evidence:** hiện thao tác đặt HOLD nằm ở màn lô hoặc container, với trường lý do. Chưa có thao tác đặt HOLD từ đích đã chọn trên kết quả trace hoặc lưu tham chiếu evidence khi phát lệnh. Mill Manager đã có đường ghi evidence khi xử lý HOLD. [Form HOLD lô](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/apps/web/src/LedgerStock.tsx#L124) · [Yêu cầu API đặt HOLD container](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/backend/src/GrainLedger.Api/Features/Exports/Shipments/ContainerExecutionEndpoints.cs#L18)
- **Người nhận thông báo HOLD lô:** mã gửi cho Customer Manager cùng công ty và nhân sự mill có phân công, thiếu Export Officer cùng công ty. Luồng HOLD container đã có kiểm tra các nhóm người nhận bắt buộc. [Thông báo HOLD lô](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/backend/src/GrainLedger.Api/Data/Sql/InitialSchema.sql#L1557) · [Thông báo HOLD container](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/backend/src/GrainLedger.Api/Data/Sql/ContainerWorkflow.sql#L526)
- **Chặn planning và thu hồi reservation:** production, loading và CLOSE có kiểm tra HOLD container. Đường tạo draft còn thiếu kiểm tra HOLD của container. SDD mô tả thu hồi reservation chưa dùng trong transaction đặt HOLD. Mã hiện tại ghi HOLD và notice ở lệnh phát hành, còn bước vô hiệu hóa approval và thu hồi reservation nằm ở đường kiểm tra căn cứ sau đó. [Tạo draft](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/backend/src/GrainLedger.Api/Features/Exports/Planning/BlendPlanningEndpoints.cs#L191) · [Đặt HOLD](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/backend/src/GrainLedger.Api/Data/Sql/ContainerWorkflow.sql#L869) · [Thu hồi quota](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/backend/src/GrainLedger.Api/Data/Sql/ContainerWorkflow.sql#L333) · [Kiểm tra lại approval](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/backend/src/GrainLedger.Api/Features/Exports/Shipments/ContainerExecutionEndpoints.cs#L79)

### 6. Tìm kiếm lịch sử nghiệp vụ theo quyền

**GrainLedger V2 F20, tr. 9 · SRS §3.7.3, tr. 41 và BR-17, tr. 47 / UC-38 · SDD §3.9.2.** Các vai trò nội bộ được tìm và xem bản ghi nghiệp vụ trong phạm vi được cấp, có bộ lọc loại sự kiện và khoảng thời gian. SDD mô tả việc hợp nhất lịch sử xuất hàng và tuân thủ là phần thiết kế đích.

Hiện có Ledger History và các màn lịch sử riêng cho order, plan và container. **Chưa có đường tìm kiếm bao phủ các nhóm sự kiện nghiệp vụ mà SRS liệt kê**, gồm thu mua, lab, plan/reservation, loading/CLOSE, HOLD và quản trị. Ledger History nhận loại sự kiện nhưng chưa nhận bộ lọc khoảng ngày. [Bộ lọc HTTP](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/backend/src/GrainLedger.Api/Features/Ledger/Endpoints/LedgerEndpoints.cs#L21) · [Dữ liệu của Ledger History](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/backend/src/GrainLedger.Api/Data/Sql/InitialSchema.sql#L3962)

## Ranh giới nghiệm thu

Luồng nghiệm thu tiếp tục sau khi kết quả lab đã về. Truy vết từ **kết quả lab đã xác nhận** và đặt HOLD theo quyền thuộc F18–F19. Kịch bản và xử lý tự động khi kết quả lab đến muộn nằm ngoài phạm vi đợt này theo quyết định của người dùng. GrainLedger V2, tr. 6–9 · SRS R2, §3.5.2 và §3.7.1–3.7.2

SRS R2 §3.7.1 liệt kê điểm bắt đầu là container, lô hoặc kết quả lab. Tỷ lệ theo thửa là một đầu ra của backward. BR-15 giữ HOLD sau khi xử lý. Việc thu hồi reservation chưa dùng giải phóng quota của phương án, và HOLD vẫn có hiệu lực với đích được giữ. SRS R2, tr. 40–41 và 47 · SDD R2, §3.8.4

## Thứ tự hoàn thiện

1. **Sửa tính toàn vẹn của loading và audit.** Xử lý CT-01 để thời điểm actual loading hợp lệ so với thời điểm lô tồn tại và thời điểm ghi nhận. Xử lý CT-02 để `graph.traceId` của container đối chiếu được với đúng bản ghi `TRACEBACK`.
2. **Hoàn tất forward và backward.** Forward trả các phương án bị ảnh hưởng và phân biệt phần dự kiến với actual loading. Với nguồn đang truy, trả kg thực nạp được quy thuộc theo phân bổ đã lưu. Backward tổng hợp kg và tỷ lệ theo thửa, giữ chi tiết lô nguồn và hiển thị provenance đọc được. Xử lý CT-04, CT-05 và CT-06 cùng phần giao diện truy vết.
3. **Hoàn tất HOLD.** Customer Manager đặt HOLD từ đích đã chọn và lưu evidence. Bổ sung thông báo cho Export Officer khi HOLD lô, chặn planning thuộc phạm vi HOLD, thu hồi reservation chưa dùng theo SDD và sửa cờ restriction CT-03.
4. **Hoàn tất luồng sau CLOSE.** Hiển thị đơn hoàn thành dựa trên tổng kg của các container đã CLOSE. Sinh chứng từ từ hồ sơ đã đóng, lưu và tải theo phiên bản và quyền. Chốt nội dung và mẫu chứng từ cần xuất theo UC-33 trước khi triển khai phần sinh file.
5. **Hoàn tất tìm kiếm lịch sử.** Trả các sự kiện nghiệp vụ theo quyền với bộ lọc loại sự kiện và khoảng ngày. Làm rõ task version trong tài liệu `production/next` theo CT-07.

## Kiểm tra để chốt luồng

- Khi lab đã có kết quả xác nhận, chạy forward đến phương án chưa load và container đã load. Đối chiếu đúng kg của nguồn trên container. Chạy backward và đối chiếu tổng theo thửa với các dòng chi tiết, gồm hai lô có cùng thửa.
- Dùng Customer Manager đặt HOLD kèm evidence từ một đích trong trace. Kiểm thông báo đến đúng vai trò, phần chưa thực hiện bị chặn, quota chưa dùng được xử lý và dossier đã CLOSE giữ nguyên.
- Dùng một đơn có nhiều container. Kiểm đơn chỉ hoàn thành khi đủ kg từ các container đã CLOSE. Sinh và tải chứng từ, đối chiếu manifest, tỷ lệ nguồn và căn cứ phê duyệt theo quyền.
- Tìm lại các sự kiện trên theo loại, khoảng ngày và quyền. Đối chiếu `traceId` với audit, chạy lại các ca CT-01–CT-07 và kiểm tracing hai chiều trên profile mùa vụ được phê duyệt theo mục tiêu 30 giây của SRS §4.2.3. SRS R2, tr. 43

## Bằng chứng QA ngày 19/11/2026

Backend dùng `BrowserWorkflowHost` và PostgreSQL Testcontainers riêng, với dữ liệu tổng hợp. Web chạy qua Vite và API loopback. Phiên email/password được kiểm bằng form web với tài khoản pending assignment. Các ca theo vai trò dùng phiên fixture có xác thực, session cookie, CSRF và phân quyền backend thật. Audit được đối chiếu bằng truy vấn chỉ đọc trong database test.

### Lỗi và điểm bất nhất đã tái hiện

#### CT-01 · Cao · API ghi nhận actual loading vào thời điểm bất khả thi

**Tái hiện.** Dùng Export Staff, tạo order/container 2 kg, tính và lưu phương án, rồi cho Customer Manager duyệt. Gửi hai request hợp lệ đến `POST /api/export-containers/{id}/execution/loads`, mỗi request 1 kg và dùng version mới. Đặt `loadedAt` lần lượt là `2099-01-01T12:00:00Z` và `2000-01-01T12:00:00Z`. Lô blend được tạo năm 2026. Lưu container number/seal rồi gọi CLOSE.

**Thực tế.** Hai request đều trả `200` và làm tồn giảm ngay từ `10.000000` còn `8.000000` kg. Container `CTN-20261119-000003` CLOSE thành công lúc `2026-11-19T13:02:02Z`. Truy vết ngược trả `complete: true` và giữ cả hai mốc trong loading lines.

**Kỳ vọng.** Actual loading phản ánh một sự kiện vật lý đã xảy ra với lô đã tồn tại. Lịch sử loading, chứng từ và trace phải giữ thứ tự thời gian khả thi. Trường hợp này làm cả thời gian trước khi có lô và thời gian sau khi CLOSE đi vào manifest bất biến.

[Validation HTTP](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/backend/src/GrainLedger.Api/Features/Exports/Shipments/ContainerExecutionEndpoints.cs#L116) · [Ghi thời điểm đã load](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/backend/src/GrainLedger.Api/Data/Sql/ContainerWorkflow.sql#L904)

#### CT-02 · Cao · `graph.traceId` của container không trỏ đến audit đã ghi

**Tái hiện.** Dùng Customer Manager gọi `POST /api/export-containers/{id}/execution/trace` với body `{}` trên container đã CLOSE. Đối chiếu `graph.traceId` với bản ghi `TRACEBACK` mới của container trong database test. Gọi thêm `POST /api/ledger/trace` trên lô nguồn để đối chứng.

**Thực tế.** Container ghi đúng một audit, nhưng response trả `06353912-a8fb-407c-9500-84b80367042c` trong khi bản ghi mới có ID `7357cf00-9487-49ad-b3f3-f8c9ad1b9afe`. Với lot trace, ID `0ff9e74f-6c31-4eec-9f1d-05e3affcdaa5` khớp đúng một hàng audit.

**Kỳ vọng.** Khi response cung cấp `traceId` theo hợp đồng trace v2, ID phải tra được lần truy vết tương ứng. Sai ID làm mất khả năng đối chiếu chính xác một kết quả container với audit.

[Hợp đồng trace ID](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/docs/api/traceability-api.md#L44) · [ID được tạo trong graph](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/backend/src/GrainLedger.Api/Data/Sql/ContainerTracingFlowchart.sql#L11) · [Audit container](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/backend/src/GrainLedger.Api/Data/Sql/ContainerWorkflow.sql#L829)

#### CT-03 · Trung bình · HOLD của container làm cờ restriction lô tải chuyển thành true

**Tái hiện.** Chạy forward từ nguồn của một container đã CLOSE và chạy backward từ container. Đặt HOLD riêng trên container bằng Customer Manager, rồi chạy lại hai trace. Đối chiếu header và cờ restriction của từng load line.

**Thực tế.** Header đổi từ `held: false, restricted: false` sang `held: true, restricted: true` ở forward và backward. Từng load line vẫn có `restricted: false`. UI ưu tiên hiển thị “Container HOLD”, nên triệu chứng rõ nhất trên JSON. Dossier vẫn bất biến.

**Kỳ vọng.** Hai tín hiệu phải phân biệt HOLD của container với restriction của hàng đã load theo hợp đồng API. Tích hợp đọc JSON hiện có thể kết luận nhầm rằng các lô trong manifest bị hạn chế.

[Hợp đồng hai tín hiệu](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/docs/api/container-execution-api.md#L82) · [Forward projection](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/backend/src/GrainLedger.Api/Data/Sql/ContainerTracingFlowchart.sql#L233) · [Backward projection](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/backend/src/GrainLedger.Api/Data/Sql/ContainerTracingFlowchart.sql#L280)

#### CT-04 · Trung bình · Administrator mở target container rồi rơi vào My profile

**Tái hiện.** Đăng nhập Administrator, vào **Ledger → Traceability**, chạy Forward từ một lô có actual loading. Bấm node **Open container** trong Flowchart. Link trong bảng cũng mang cùng URL.

**Thực tế.** Trình duyệt đến `/export/containers/{id}`, topbar ghi **Export orders**, nhưng phần nội dung là **My profile** và không có thông báo về quyền. Phiên này gọi GET execution trả `403`, đúng ranh giới quyền nghiệp vụ. API backward container trace với quyền trace toàn cục vẫn trả `200`.

**Kỳ vọng.** Giao diện phản ánh đúng quyền của vai trò và không đưa người dùng tới màn hình không liên quan khi mở một kết quả trace hợp lệ.

[Link trên bảng](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/apps/web/src/LedgerTraceability.tsx#L100) · [Link trên Flowchart](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/apps/web/src/TraceFlowchart.tsx#L73) · [Màn hình fallback](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/apps/web/src/App.tsx#L242)

#### CT-05 · Trung bình · Fit graph vẫn để sơ đồ vượt vùng xem

**Tái hiện.** Chạy Forward từ nguồn qua sấy, xay, trộn và container. Chuyển sang **Flowchart → Fit graph**. Kiểm ở viewport desktop 1.280 px và mobile 375 px.

**Thực tế.** Cả hai lần nút dừng ở zoom 40%, với canvas rộng khoảng 1.046 px. Vùng xem chỉ rộng 868 px trên desktop và 296 px trên mobile. Phải cuộn ngang để xem chuỗi. Cuộn nằm trong vùng graph, và **Focus starting lot** vẫn đặt focus đúng node.

**Kỳ vọng.** Fit graph phải đưa sơ đồ vừa vùng xem, hoặc báo rõ khi giới hạn zoom không cho phép. Nút hiện cho cảm giác đã fit trong khi phần lớn graph vẫn ở ngoài vùng xem trên mobile.

[Điều khiển zoom và Fit graph](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/apps/web/src/TraceFlowchart.tsx#L38)

#### CT-06 · Thấp · Backward container chỉ hiển thị UUID ở cột provenance

**Tái hiện.** Customer Manager mở container đã CLOSE, bấm **Trace container sources**, rồi đối chiếu bảng **Actual container sources** với `sourceContributions[].provenance` trong response.

**Thực tế.** UI hiện đúng mã lô, số kg và tỷ lệ, nhưng cột **Plot / provenance** chỉ hiện UUID thửa. Hai dòng fixture cùng thửa có cùng UUID. API đã trả mã phiếu thu mua, giống, ID vụ và nhãn hiện tại của thửa/vụ/nông dân. Trang backward không cho mở các chi tiết nguồn đó từ bảng. Lot Traceability có thao tác **View field / season origin**.

**Kỳ vọng.** Người dùng có thể đối chiếu các nguồn với thửa, vụ và phiếu thu mua bằng thông tin nghiệp vụ đã có trong response. Đây là hạn chế hiển thị, dữ liệu provenance vẫn có trong API.

[Hiển thị provenance container](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/apps/web/src/ContainerSourceTrace.tsx#L21) · [Chi tiết nguồn ở lot trace](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/docs/api/traceability-api.md#L99)

#### CT-07 · Thấp · Tài liệu `production/next` chưa chỉ rõ version của production task

**Tái hiện.** Sau mẻ đầu 10/20 kg, review và load một phần, đọc GET production. Fixture trả `version: 3`, `containerVersion: 4`. Gọi `POST .../production/next` một lần với container version, rồi với task version.

**Thực tế.** `version: 4` trả `409 Production version changed; reload`. `version: 3` trả `200` và mở mẻ tiếp theo. UI gửi task version chính xác. Tài liệu S2 chỉ ghi `{operationId, version}` dù hai concurrency token cùng xuất hiện trong response.

**Kỳ vọng.** Hợp đồng nói rõ production task version là token của route này. Backend trả `409` đúng khi caller gửi token khác.

[Tài liệu S2](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/docs/api/container-workflow-api.md#L11) · [UI gửi task version](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/apps/web/src/ContainerProduction.tsx#L95) · [Kiểm tra version](https://github.com/GrainLedgerProject/GrainLedger/blob/464746e5c1b1ba7570be137751f65cefd505f3e4/backend/src/GrainLedger.Api/Data/Sql/ContainerWorkflow.sql#L633)

### Các đường chạy đã kiểm

| Đường chạy | Kết quả quan sát |
| --- | --- |
| Web · Đăng nhập / đăng xuất | Form email/password đăng nhập được tài khoản test. `GET /api/auth/session` xác nhận phiên. Các phiên fixture vai trò cho kết quả scope đúng trong những ca bên dưới. |
| Web · Container trực tiếp | Export lưu metadata, load 4 kg, thử `6.000001` kg khi còn 6 kg và thấy lỗi **Loading exceeds container target**, sau đó load 6 kg và xác nhận CLOSE. UI hiện CLOSED 10 kg. |
| HTTP · Order, proposal, actual loading và CLOSE | Tạo order/container, preview, save proposal, CM approve. Với fixture 10 kg, load `4.000001 + 5.999999`; replay cùng request giữ receipt, sửa payload cùng operation ID bị từ chối. Version cũ, trùng dòng, kg dạng số, quá sáu chữ số thập phân, trường actor thừa, CLOSE sớm và sửa metadata sau CLOSE đều bị từ chối. CLOSE không trừ tồn lần hai. |
| HTTP · Production hai mẻ | Export request, Operator chuẩn bị/commit 10 + 10 kg, CM xác nhận chất lượng và review scope. Output chưa review bị chặn. Load `4.000001 + 5.999999 + 10` kg từ hai lô thực, CLOSE 20 kg, trace hai nguồn 10 kg mỗi nguồn. Phát hiện CT-07 ở bước mở mẻ hai. |
| HTTP · Handoff từ pool | Load trực tiếp từ pool bị chặn. Operator lưu transfer, CM duyệt, Operator commit/replay. Có aliquot mới; load bị chặn trước khi kiểm chất lượng và review. Sau review, CLOSE 10 kg, trace có custody events và hai nguồn 5 kg. |
| HTTP / web · HOLD và chứng từ | HOLD sau CLOSE giữ nguyên dossier và 10 kg. Replay không nhân đôi thông báo. Đánh dấu đã đọc giữ nguyên HOLD. Tenant khác không thấy notice. Tạo hai version tài liệu qua API, tải xuống đúng bytes, tenant khác nhận `403`. Tải thêm tệp text synthetic qua form web tại 375 px, API trả lại đúng nội dung. |
| HTTP / web · Forward và backward | Reservation chưa load không tạo đích actual loading. Partial forward thấy `4.000001` kg; backward phân bổ `2.000000 + 2.000001`. Sau CLOSE, CM thấy hai nguồn `5.000000` kg, hai load refs và đúng một container root. UI phân biệt 20 kg lô tạo với 10 kg đã load, giữ chiều vật liệu từ trái sang phải và đặt focus đúng root, kể cả backward ở 375 px. Đổi Table/Flowchart không gọi thêm trace. Lot backward history không có container downstream. |
| HTTP · Trace rỗng / legacy | Container chưa load có root riêng, không có ancestry và `NO_ACTUAL_LOADING`. Dữ liệu legacy thiếu Capture vẫn giữ 10 kg actual và báo `SOURCE_PROVENANCE_UNAVAILABLE`. |
| HTTP · Quyền | Anonymous `401`, cookie thiếu CSRF `400`, tenant khác và Mill Operator bị từ chối khi truy vết ngược container. Export forward có target đã load nhưng response lược bỏ sources/contributions; backward bị `403`. Dossier Export lược bỏ source shares và assessment evidence. Administrator có quyền trace toàn cục, GET execution bị `403`. |

### Kiểm thử tự động và giới hạn

- `dotnet build GrainLedger.slnx` thành công. Hai cảnh báo analyzer xUnit nằm ngoài phần được kiểm.
- `npm test` đạt **257/257**. `npm run build` thành công, có cảnh báo bundle JavaScript chính vượt 500 kB sau minify.
- Hai nhóm integration về Container, Traceability, Capture source, browser session, Export orders, blend planning/assessment và benchmark đạt **62/62** và **37/37**. Nhóm MixedBulk đạt **37/37**, gồm bộ chọn điểm bắt đầu, custody và truy vết từ kết quả lab. Tổng cộng **136/136** backend integration tests. Các nhóm này bao gồm kiểm tra rollback, tranh chấp khi load, thu hồi quyền đang chờ lock, policy/MRL hết hiệu lực, P1/P2, bagging, HOLD handling và audit. Bảng trên chỉ liệt kê đường chạy HTTP/browser thực hiện riêng.
- Benchmark tổng hợp 50 nguồn, 101 công đoạn và 1.000 container trả đủ target cùng source shares. Ba lượt warm đo handler HTTP in-process và parse JSON: forward **0,75–0,79 s**, backward **1,63–1,72 s**. Đây là số đo fixture trên máy kiểm thử, không bao gồm network/TLS, tải trang hay môi trường triển khai.
- Phạm vi không bao gồm Google login, Supabase/deployment, thiết bị mobile vật lý hoặc toàn bộ test suite backend. Các ca bagging, đổi P1/P2 và xử lý HOLD của Mill Manager chạy trong integration suite; lượt web tập trung vào load/CLOSE, truy vết, phân quyền điều hướng và upload.
