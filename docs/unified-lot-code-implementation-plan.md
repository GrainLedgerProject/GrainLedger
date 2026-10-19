# Implementation plan: mã lô thống nhất theo loại sản phẩm

Ngày: 17/10/2026. Trạng thái: đã triển khai trong mã nguồn; chờ kiểm thử toàn bộ và phê duyệt migration lên database chung.

## 1. Quyết định được chọn

Áp dụng hướng 3 trong đề xuất người dùng, với các điều chỉnh đã thống nhất trong trao đổi:

```text
HDR-20261017-000123
BGH-20261017-000124
```

- Format: `TOKEN-YYYYMMDD-SERIAL`.
- Ngày là ngày backend cấp mã theo `Asia/Ho_Chi_Minh`, không phải ngày nhận hàng hoặc ngày sản xuất do người dùng nhập.
- Serial là PostgreSQL bigint sequence toàn hệ thống, tối thiểu 6 chữ số; không reset, không tái sử dụng, cho phép khoảng trống và tăng quá 6 chữ số. Không dùng cách padding làm cắt số dài.
- Backend tự cấp mã; UUID tiếp tục là định danh liên kết dữ liệu.
- Không thêm hậu tố `-R1/-R2`. Correction giữ quy tắc tái sử dụng mã trong đúng lineage hiện có.
- Không đưa facility, farmer, owner, moisture, quantity hoặc trạng thái vào mã.
- Mã không thay thế trường thời gian nghiệp vụ để FIFO và không dùng làm bằng chứng hàng đã nhập kho.
- Mã đã cấp là bất biến; không đổi khi chuyển kho, đổi tên danh mục, HOLD hoặc thay đổi thời gian của draft.

| Token | Stage | Product identity |
| --- | --- | --- |
| RAW | RAW_PADDY | RAW_PADDY |
| DRY | DRIED_PADDY | DRIED_PADDY |
| HDR | HEAD_RICE | HEAD_RICE |
| BRK | BROKEN_RICE | BROKEN_RICE |
| BRN | BRAN | BRAN |
| HSK | HUSK | HUSK |
| BLD | BLENDED_RICE | BLENDED_RICE |
| BGH | BAGGED_RICE | HEAD_RICE |
| BGB | BAGGED_RICE | BROKEN_RICE |
| BGL | BAGGED_RICE | BLENDED_RICE |

Chỉ cấp mã cho cặp stage/product identity hợp lệ trong ma trận nghiệp vụ hiện tại. Không mở thêm loại sản phẩm chỉ vì có token.

## 2. Phạm vi và hiện trạng cần thay

- Capture tạo mã `LL-{lot UUID}` trong `ProcurementEndpoints.cs`, lưu trong response snapshot.
- Ledger cũng ghép `LL-…` trong các routine đăng ký nguồn, nhận hàng và read DTO. Phải cập nhật đồng bộ, không chỉ sửa FE.
- Processing hiện nhận `outputs[].code` do người dùng nhập trong `LedgerOperations.tsx`.
- `ledger.lot_codes` là registry bất biến có FK tới `ledger.lots`. Chưa thể giữ mã tại thời điểm intake/draft chưa tạo lô tồn kho.
- `ledger.register_code` hiện chỉ cho tái sử dụng mã trong một correction lineage rõ ràng. Không đổi cơ chế này bằng cách thêm suffix.
- FE chính nằm ở `apps/web/src`; kiểm tra thêm caller trong `apps/web/grainledger` đã được đưa từ dev để tránh bỏ sót contract.

Giữ nguyên mã lịch sử, chứng từ đã commit, snapshot idempotency, quan hệ sample/source lot, khối lượng và lịch sử hạch toán. Offline F07 và tạo QR/label mới không thuộc đợt này; các nơi đang hiển thị/xuất mã phải dùng giá trị API trả về.

## 3. Giai đoạn A — Contract và nơi giữ mã

1. Rà toàn bộ caller: procurement, receiving, operation draft create/update/detail, commit, correction replacement, seed/test fixtures, tìm kiếm, tracing và export.
2. Thêm migration Ledger mới; không sửa migration/SQL lịch sử đã áp dụng.
3. Tạo sequence và bảng giữ mã dự kiến `ledger.lot_code_reservations` gồm code, lot UUID, owner/context, stage, product identity, issued_at và trạng thái cần thiết để bảo toàn mã đã cấp. Thiết kế không yêu cầu có bản ghi `ledger.lots` trước khi nhận hàng/commit.
4. Code và lot UUID phải unique trong nơi giữ mã; kiểm tra collision với registry lịch sử `ledger.lot_codes` dưới cùng lock. Mã lịch sử nhập tay có thể trùng format mới; generator phải bỏ qua collision, không đổi mã cũ.
5. Tạo routine nội bộ cấp/đọc lại mã theo lot UUID và context. Gọi lại cùng lot/context trả cùng mã; không cấp thêm serial. Sai owner/context hoặc stage/identity thì trả lỗi rõ ràng.
6. Runtime không có quyền tự ghi bảng giữ mã hay tự lấy sequence. Cấp mã qua command/routine được bảo vệ, có kiểm tra actor và scope; thu hồi quyền thực thi PUBLIC thích hợp.
7. Giữ transaction/connection của caller, `READ COMMITTED`, advisory lock `714928352` trước source/stock rows; helper không tự commit. Sequence bảo đảm cấp số đồng thời; lock duy trì quy tắc hiện có.
8. Chạy validation và kiểm tra idempotency trước khi cấp mã. Transaction rollback có thể bỏ số nhưng không tạo mã hoặc stock giả.

Đầu ra: migration và generator có kiểm thử; chốt DTO trước khi nối FE.

## 4. Giai đoạn B — Procurement và Receiving

1. Mở rộng `IProcurementLedgerPort` bằng thao tác cấp mã theo lô ID/context trên connection + transaction hiện tại; không truy cập EF entity của Ledger từ Capture.
2. Intake cấp một mã RAW cho mỗi dòng source lot. Một receipt hai dòng phải có hai mã riêng.
3. Lưu mã trong response snapshot/nguồn đọc bền vững ngay trong transaction đăng ký. Retry cùng client operation trả đúng UUID/mã/snapshot đã có, kể cả retry sau nửa đêm.
4. `RegisterPendingAsync` và các routine xác minh snapshot đọc mã đã cấp thay vì tự ghép `LL-…`.
5. Receiving dùng đúng mã đã giữ khi tạo `ledger.lots` và gọi cơ chế đăng ký lineage. Không cấp lại mã khi cân lại, review shortage hay chấp nhận arrival.
6. Cập nhật receipt/source-lot/receiving read DTO và các nơi tìm kiếm để trả mã bền vững thống nhất.
7. Intake lịch sử đang PENDING_ARRIVAL vẫn dùng mã LL cũ khi nhận hàng sau cutover. Fallback chỉ dành cho bản ghi legacy xác định được; bản ghi mới thiếu reservation phải báo lỗi, không âm thầm ghép LL.

Nghiệm thu: intake mới có mã RAW và stock = 0; arrival được chấp nhận mới có stock, mã không đổi; sample vẫn liên kết đúng source lot.

## 5. Giai đoạn C — Processing, draft và correction

1. Khi lưu draft lần đầu, backend cấp mã cho từng output UUID hợp lệ và trả lại trong draft detail/response.
2. Output chưa lưu hiển thị “Assigned when saved”; không tạo mã giả trên FE. Retry create draft dùng định danh/idempotency hiện có, không sinh thêm output/mã.
3. Update draft giữ mã khi output UUID, stage và product identity không đổi. Caller gửi lại mã hiện tại được chấp nhận nếu khớp; gửi mã tùy ý bị từ chối.
4. Nếu đổi stage/product identity của output đã có mã mới: xử lý thành output UUID mới, cấp mã mới và giữ mã cũ đã dành. UI phải thể hiện việc thay output; không sửa token của mã cũ.
5. Output bị bỏ khỏi draft hoặc draft bị hủy không giải phóng mã cho lô khác.
6. Commit kiểm tra mã output khớp reservation/context rồi mới đăng ký code/lot. DRYING, MILLING, MERGE, BLENDING và BAGGING dùng cùng generator; không tự suy loại từ tên commodity.
7. Correction: backend xác định output gốc theo liên kết correction được review. Output thay thế có cùng stage/product identity và đáp ứng quy tắc lineage được giữ mã gốc; UUID bản thay thế vẫn khác. Không tin mã/ID nguồn do client khai báo thiếu xác minh.
8. Output correction mới hoặc đổi loại nhận mã mới và vẫn liên kết tới operation correction; không dùng mã cũ mang token sai loại. Trường hợp không xác định được output tương ứng phải được xử lý rõ trước khi commit, không đoán theo vị trí dòng.
9. Bảo toàn draft cũ có mã nhập tay đã lưu trước cutover. Ghi nhận mốc/format version để phân biệt legacy với draft mới; không suy legacy chỉ bằng regex của code. Không đổi mã legacy khi commit sau triển khai.

Nghiệm thu: đầu ra hợp lệ tự có mã; commit/retry không đổi mã; correction không mở đường dùng lại mã của lineage khác và không làm thay đổi kiểm soát HOLD/mass balance.

## 6. Giai đoạn D — Frontend và tài liệu

- Bỏ input nhập mã output mới; hiển thị mã read-only sau khi lưu draft. Update form gửi đúng contract mới và giữ output UUID qua các lần lưu.
- Hiển thị mã từ API tại intake, source lot, sample origin, receiving, operation output, stock picker, history, trace và export hiện có; không tự dựng mã bằng UUID.
- Cho phép tìm cả mã cũ và mới. Link vẫn dùng UUID; không giả định code luôn duy nhất giữa các bản correction trong cùng lineage.
- Giữ tên giống, farmer, facility, vị trí kho và thời gian nghiệp vụ cạnh mã khi cần; không yêu cầu người dùng giải mã để thao tác.
- Cập nhật procurement API, Ledger API/contract liên quan, hướng dẫn vận hành và dữ liệu test. Không đổi mã sample, seal, farmer, plot hoặc receipt trong đợt này.

## 7. Kiểm thử và nghiệm thu

### Database/API trên Testcontainers

- Đúng token cho toàn bộ cặp stage/identity; cặp không hợp lệ bị chặn.
- Nhiều request đồng thời không trùng mã; serial vượt 999999 không bị cắt; rollback/cancel không tái sử dụng mã.
- Ngày cấp mã đúng biên UTC+7; không phụ thuộc received_at/occurred_at do client nhập; retry qua ngày vẫn giữ mã.
- Request bị từ chối không tạo reservation bền vững; retry idempotent không tạo thêm lô/serial đã lưu.
- RAW reservation không tạo stock; receiving/review giữ mã; procurement và output khác không chiếm mã của nhau.
- Draft create/update/remove/retry và correction nhiều lần giữ đúng identity, version và lineage.
- Mã legacy LL/mã nhập tay, pending receipt/draft cũ, snapshot đã commit và mã legacy trùng format mới vẫn hoạt động.
- Kiểm tra runtime role, owner/facility scope, command grants và atomicity; không có đường gọi helper để vượt quyền.
- Regression mass balance, lineage, receiving, transfer, correction và HOLD hiện có.

### FE và thao tác thực tế

- Build/test FE; kiểm tra saved draft trả mã, không còn nhập mã output mới và không mất mã khi mở lại.
- Trên môi trường cô lập: intake hai dòng → nhận hàng → drying → milling nhiều đầu ra → blending/bagging → transfer → correction.
- Đối chiếu cùng một mã ở receipt, source lot, stock, sample origin và lịch sử; kiểm tra tìm mã cũ/mới.
- Test hai tab sửa draft, retry mất phản hồi và các lỗi validation/concurrency.

## 8. Triển khai

1. Hoàn tất A → B → C → D và kiểm thử cô lập.
2. Review migration, quyền runtime và kế hoạch tương thích trước cutover. Migration mới bổ sung reservation/helper và phiên bản routine hiện hành; không sửa script đã chạy.
3. Triển khai theo cửa sổ bảo trì ngắn cho các thao tác tạo/nhận/xử lý lô để không có instance cũ tiếp tục dựng LL trong lúc instance mới cấp RAW. Khởi động lại API và phát hành FE tương ứng.
4. Shared database chỉ được migrate khi có phê duyệt rõ ràng cho migration này. Yêu cầu lập plan hiện tại chưa phải phê duyệt chạy migration.
5. Sau cutover, không rollback sang phiên bản chỉ hiểu LL nếu đã phát sinh mã mới. Ưu tiên bản sửa tiến; mọi rollback phải giữ khả năng đọc và nhận hàng cho mã đã cấp.

## 9. Checklist bàn giao

- [x] A: contract, reservation, sequence, protected generator và migration.
- [x] B: intake/receiving/read DTO dùng mã bền vững, hỗ trợ legacy.
- [x] C: processing draft/commit/correction tự cấp mã và giữ lineage.
- [x] D: FE read-only code, search/trace/export và tài liệu.
- [ ] Integration tests và kiểm thử FE toàn luồng.
- [ ] Review migration và phê duyệt triển khai shared database.

Tham chiếu code hiện tại: `ProcurementEndpoints.cs`, `IProcurementLedgerPort`, `ProcurementLedgerPort.cs`, `Sql/CodeIdentity.sql`, `Sql/OfficialCapture.sql`, `Sql/PhysicalReceiving.sql`, `Sql/ManageActiveResources.sql`, `Sql/Operations.sql`, `LedgerOperations.tsx`. Khi triển khai phải kiểm tra thứ tự migration để thay đúng routine đang có hiệu lực, không sửa nhầm định nghĩa cũ bị migration sau ghi đè.
