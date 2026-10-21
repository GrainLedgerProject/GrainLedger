# Kịch bản test tracing trên web với dữ liệu hiện có

Đối chiếu code và đọc database ngày 21/10/2026, nhánh `feat/trace`. Không tạo/seed hoặc thay dữ liệu nghiệp vụ để chuẩn bị hướng dẫn. Số tồn và current generation có thể thay đổi nếu người khác nhập/rút hàng; các kỳ vọng dưới đây theo thời điểm kiểm tra.

Mục tiêu: **pool 800 kg → truy ngược về hai nguồn → xem mẫu/báo cáo nguồn → truy xuôi một nguồn về pool**.

## 1. Chuẩn bị

Khởi động lại backend sau cập nhật code/database; chạy frontend ở terminal khác, từ thư mục gốc repository:

```bash
dotnet run --project backend/src/GrainLedger.Api
```

```bash
npm run dev
```

Mở **http://localhost:5173**. Đăng nhập **Customer Manager của GrainLedger Demo Customer**. Không dùng Administrator cho luồng test này vì Admin không có quyền đọc lab. Không cần chạy migration hoặc seed thêm.

## 2. Dữ liệu sẽ dùng

| Vai trò trong luồng | Mã lô | Ledger lot ID | Lượng ghi nhận |
| --- | --- | --- | --- |
| Nguồn A | RAW-20261021-000014 | `7ba0d447-383a-419f-a1cf-c3e2ae97cf09` | 600 kg |
| Nguồn B | RAW-20261021-000015 | `41ad2054-3c9d-4186-8f4b-42d80b32089f` | 200 kg |
| Pool đầu từ nguồn B | RAW-20261021-000016 | `a0ded53f-ff9c-4ad5-8f5c-c754308b9415` | 200 kg khi tạo; hiện hết tồn |
| Pool hiện hành | RAW-20261021-000017 | `7db63747-66d0-41fd-b5ea-d0dddbe58636` | 800 kg; revision 2 |

Đường đi đã ghi: nguồn B → pool 000016; sau đó pool 000016 + nguồn A → pool 000017. Các node này đều là RAW_PADDY; dữ liệu hiện tại chưa có bước rút/tách hoặc chế biến cho chuỗi này.

## 3. Truy ngược pool 800 kg

1. Vào **Ledger → Stock & HOLDs**, tìm `RAW-20261021-000017`.
2. Bấm **Open lot detail → Trace this lot**. Tab Traceability mở và Ledger lot ID được điền sẵn. Chưa có trace result cho đến khi bấm Run trace.
3. Chọn **Backward — contributing sources** và **Current — effective operations**.
4. Bấm **Run trace**.

Kết quả mong đợi:

- **4 lots, 3 relationships**: 000017, 000016 và hai nguồn 000014/000015. Starting lot là 000017, đứng đầu Table.
- Chọn 000017: **Quantity when created = 800.000000**, stock/available **800.000000 / 800.000000**, không HOLD tại thời điểm đối chiếu.
- **Source composition** của 000017: nguồn 000014 là **600 / 800 kg, 75%**; nguồn 000015 là **200 / 800 kg, 25%**.
- Các lô cũ có stock 0 vẫn xuất hiện vì đang xem đường đi lịch sử đã ghi. Không cộng Quantity when created của mọi generation thành tổng tồn.

Nếu không tìm thấy pool trong Stock, vào thẳng **Ledger → Traceability**, dán ID pool ở mục 2 rồi chạy cùng lựa chọn. Nếu sai company, backend sẽ từ chối; không đổi sang Admin để bỏ qua bước kiểm tra quyền lab.

## 4. Kiểm tra Flowchart

1. Bấm **Flowchart → Fit graph**.
2. Kiểm tra nguồn 000015 đi qua pool 000016, rồi cùng nguồn 000014 vào pool 000017.
3. Phải thấy **hai node Pool ingress**; không có node DRYING/MILLING vì chuỗi này chưa chế biến.
4. Chọn node pool 000017: Source composition vẫn là 600/200 kg. Chọn lô khác: Starting lot vẫn là 000017, chỉ phần chi tiết đổi theo node đang chọn.
5. Bấm **Table**: vẫn cùng kết quả/snapshot/reference, không chạy trace mới chỉ vì đổi cách hiển thị.

Graph có **4 node lô + 2 node sự kiện = 6 node**. Con số 3 relationships ở API đếm cặp lô đầu vào/đầu ra; graph chèn node sự kiện nên số mũi tên không nhất thiết bằng 3. Mũi tên vẫn theo chiều vật liệu ngay cả khi đang Backward.

## 5. Kiểm tra mẫu và báo cáo nguồn

1. Chọn lại pool 000017, kéo xuống **Source composition**.
2. Ở dòng nguồn **RAW-20261021-000014**, bấm **View samples and laboratory evidence**.
3. Phải thấy mẫu **SMP-261021-0001** và báo cáo đã xác nhận liên kết nguồn này (tại thời điểm đối chiếu có một confirmed association).
4. Kiểm tra dòng kết quả, đơn vị, LOD/LOQ nếu báo cáo có ghi. Chúng phải giữ đúng dữ liệu báo cáo, không biến thành kết quả đo trực tiếp của pool 800 kg.
5. Mở **Whole-report VN comparison**: đây là so sánh cấp toàn báo cáo, không phải quyết định pool đạt chuẩn xuất khẩu.
6. Bấm **View sample and handover history** để mở đúng hồ sơ mẫu. Nếu muốn giữ trace đang xem, mở link trong tab mới.

Mẫu và báo cáo phân trang độc lập; dùng Next nếu còn trang. Không có báo cáo khác với lỗi không có quyền. Tải evidence có thời điểm đọc riêng sau trace; nó không thay stock/HOLD hoặc tạo trace snapshot mới.

## 6. Truy xuôi nguồn B để đối chiếu

1. Quay lại **Ledger → Traceability**.
2. Dán Ledger lot ID nguồn B: `41ad2054-3c9d-4186-8f4b-42d80b32089f`.
3. Chọn **Forward — recorded descendants**, **Current**, bấm **Run trace**.

Kết quả mong đợi: **3 lots, 2 relationships** — 000015 → 000016 → 000017. Nguồn A (000014) không nằm trong danh sách hậu duệ của B vì A là đầu vào cùng tham gia ở bước nhập pool sau. Tuy nhiên, Source composition của pool 000017 vẫn có cả A và B.

Chuyển **Trace view → History**, rồi bấm Run trace lại. Chuỗi này chưa có correction nên tập lô/quan hệ dự kiến giống Current; History không tái dựng tồn kho tại một ngày quá khứ.

## 7. Cách kết luận lần test

Đạt luồng này khi: backward đủ bốn lô; graph đúng hai lần nhập pool; shares pool là 600/200; source 000014 mở đúng mẫu/báo cáo; forward nguồn B đi đúng ba lô; đổi Table/Flowchart giữ snapshot.

Nếu dữ liệu đã thay đổi trong lúc test, kiểm tra generation/tồn mới trước khi kết luận bug. Muốn kiểm thêm withdrawal, top-up sau withdrawal, DRYING/MILLING hoặc inherited HOLD cần chuỗi dữ liệu có các thao tác đó; không tạo các thao tác trên DB chung chỉ để khớp kỳ vọng của hướng dẫn này.

Các thao tác Run trace ghi audit truy xuất theo thiết kế, không trừ tồn. Lượt chuẩn bị tài liệu này chỉ đọc database, chưa thực hiện kịch bản bằng tài khoản web trên database dùng chung. Backend/frontend regression và browser cô lập đã chạy được ghi tại [kết quả triển khai](traceability-implementation-plan.md#103-kiểm-chứng-thực-tế).
