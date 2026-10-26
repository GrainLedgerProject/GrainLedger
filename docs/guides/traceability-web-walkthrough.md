# Thực hành tracing trên web — 4 luồng dễ đối chiếu

Đã đọc lại dữ liệu DB đang cấu hình ngày 24/10/2026 bằng transaction READ ONLY. Không seed, không chạy trace có audit, không sửa stock/HOLD/evidence khi chuẩn bị hướng dẫn. Đây là kịch bản để người dùng thao tác, không phải biên bản đã test UI trên DB chung. Browser cô lập đã kiểm tại [plan mục 13](../plans/traceability-implementation-plan.md#13-plan-thực-thi-mvp-silo-bounds-và-lab-to-trace--24102026).

## Chuẩn bị một lần

Ở nhánh `feat/trace`, khởi động backend và frontend trong hai terminal từ repository root:

```bash
dotnet run --project backend/src/GrainLedger.Api
```

```bash
npm run dev
```

Mở http://localhost:5173, đăng nhập **Customer Manager thuộc GrainLedger Demo Customer**. Migration/grants tracing mới đã được cập nhật theo yêu cầu trước đó; không cần seed hoặc chạy migration lại chỉ để test. Nếu đang mở bản web cũ thì reload.

Bốn lô dùng để đối chiếu:

- Nguồn A: `RAW-20261021-000014`, 600 kg ban đầu, hiện stock 0.
- Nguồn B: `RAW-20261021-000015`, 200 kg ban đầu, hiện stock 0.
- Pool cũ: `RAW-20261021-000016`, 200 kg khi tạo, hiện stock 0.
- Pool hiện hành: `RAW-20261021-000017`, đang có 800 kg tại **Demo Dry Store**, chưa HOLD khi kiểm tra.

Đường đã ghi là **B → pool cũ; sau đó pool cũ + A → pool hiện hành**. Tất cả vẫn là RAW_PADDY. Đây là gộp trong kho, chưa phải chuỗi DRYING/MILLING.

## Luồng 1 — Pool 800 kg này đến từ đâu?

1. Vào **Ledger → Traceability**. Starting point type giữ **Current silo / warehouse pools**: danh sách ưu tiên pool đang giữ hàng, giúp không chọn nhầm nguồn đã hết tồn.
2. Trong **Search Starting lot**, nhập `RAW-20261021-000017`, chờ danh sách tải rồi chọn nó tại **Starting lot**. Có thể chọn location Demo Dry Store trước để thu hẹp.
3. Nếu muốn nhập thẳng, dán UUID này vào **Ledger lot ID**:

   ```text
   7db63747-66d0-41fd-b5ea-d0dddbe58636
   ```

4. Chọn **Backward — contributing sources** và **Current — effective operations**, bấm **Run trace**. Backward trả lời nguồn nào đã góp vào pool này.
5. Đọc dòng trạng thái và Table: dự kiến **4 lots, 3 relationships**, pool 000017 đứng đầu và có nhãn Starting lot. Type/storage phân biệt Current pool, Historical pool, Procurement source. Pool 000017 có stock/available `800.000000 / 800.000000`.
6. Bấm mã **RAW-20261021-000017** trong bảng để chọn node đó, kéo xuống **Source composition**: A góp `600 / 800 kg = 75%`, B góp `200 / 800 kg = 25%`.
7. Bấm **Flowchart → Fit graph**. Kiểm tra B đi qua pool 000016, rồi pool đó cùng A vào pool 000017. Có hai sự kiện Pool ingress. Bấm **Table** lại: snapshot/reference phải giữ nguyên.

**Đạt khi:** đủ bốn lô, đúng hai lần nhập pool và tỷ lệ 75%/25%. Source stock 0 vẫn xuất hiện là đúng: đó là nguồn gốc cần truy lại. Không cộng lượng lúc tạo của mọi generation thành tổng tồn; không kỳ vọng có công đoạn chế biến trong chuỗi này.

## Luồng 2 — Nguồn B đã đi vào những lô nào?

1. Quay lên form, xóa nội dung **Search Starting lot**, bỏ facility/location filter nếu có. Đổi Starting point type thành **Procurement sources**, chờ tải rồi chọn `RAW-20261021-000015`. Đổi bộ lọc sẽ xóa lựa chọn cũ.
2. Hoặc dán Ledger lot ID của B sau khi đã chỉnh bộ lọc:

   ```text
   41ad2054-3c9d-4186-8f4b-42d80b32089f
   ```

3. Chọn **Forward — recorded descendants**, **Current**, bấm **Run trace**. Forward trả lời những hậu duệ nào được tạo qua đường vật liệu có nguồn B tham gia.
4. Dự kiến **3 lots, 2 relationships**: B 000015 → pool 000016 → pool 000017. Trong Flowchart, mũi tên vẫn theo chiều vật liệu.
5. Chọn pool 000017 trong kết quả: Source composition vẫn phải có cả A 600 kg và B 200 kg.
6. Đổi Trace view sang **History**, bấm **Run trace** lại. Đây là lần trace/snapshot mới. Chuỗi này chưa có correction nên dự kiến tập lô/quan hệ giống Current.

**Đạt khi:** ba lô đúng đường trên. A không có trong danh sách hậu duệ của B vì A là đầu vào khác cùng nhập pool sau; A vẫn có trong thành phần nguồn của pool. History bao gồm operations đã bị thay thế, không phải tồn kho tại một ngày quá khứ.

## Luồng 3 — Từ một kết quả lab tìm các lô liên quan

Dùng báo cáo confirmed **COA-20261021-001**, mẫu **SMP-261021-0001**, thuộc nguồn A.

1. Vào **Laboratory reports**, tìm và mở `COA-20261021-001`. Kiểm tra trạng thái **CONFIRMED** và section của mẫu SMP-261021-0001.
2. Tìm dòng **Chlorpyrifos-ethyl (CAS 2921-88-2)**, dữ liệu báo cáo hiện ghi `0.042 mg/kg`. Bấm **Trace forward from result** ngay trên dòng đó. Có thể dùng dòng khác của cùng mẫu để kiểm mapping; không cần kết quả vượt ngưỡng mới được trace.
3. Màn Traceability mở và ghi Report/Result đang dùng, direction bị khóa **Forward**. Chưa có kết quả trace vì bạn chưa bấm Run. Ledger lot ID có thể còn trống; backend sẽ tự resolve đúng nguồn khi chạy.
4. Bấm **Run trace**. Dự kiến **2 lots, 1 relationship**: nguồn A `RAW-20261021-000014` → pool hiện hành `RAW-20261021-000017`. Starting lot phải là A, không phải Capture sample ID, mã report hoặc pool.
5. Bấm **Choose another starting point**: thông báo lab context biến mất, bạn chọn lô khác và direction lại được.

Đường thay thế nếu khó tìm report trong register: từ luồng 1, chọn pool 000017 → Source composition → dòng nguồn A → **View samples and laboratory evidence** → report COA-20261021-001 → cùng nút Trace forward from result. Đây là lab entry ngay trong trace, cùng quy tắc server resolve nguồn.

**Đạt khi:** chọn đúng dòng lab của A dẫn về A và pool 800 kg, không kéo B/pool 000016 vào danh sách hậu duệ của A. Kết quả `0.042 mg/kg` là phép đo của mẫu nguồn A; trace không nói pool có cùng nồng độ hoặc tự kết luận vượt MRL. Không thay HOLD chỉ vì bấm trace.

## Luồng 4 — Xem dư lượng của node đang chọn

1. Chạy lại luồng 1 hoặc giữ kết quả có pool 000017, bấm mã pool trong Table hoặc node pool trong Flowchart để chọn nó.
2. Kéo xuống **Residue evidence — RAW-20261021-000017**. Tên ở đây giúp xác nhận đang xem đúng lô.
3. Bấm **View residue bounds for selected lot**. Đọc **Live preview**, thời điểm tính, khoảng dư lượng nếu có và phần **Reasons**. Nếu thiếu evidence/policy/rule phù hợp, kết quả phải thể hiện **UNRESOLVED**, không tự PASS. Hướng dẫn này chưa gọi preview của DB chung nên không ấn định khoảng số cụ thể.
4. Xem **Immutable evaluation history** nếu đã có snapshots. Không có snapshot không đồng nghĩa không có preview: preview là tính hiện tại, history là kết quả đã được lưu trước đó.
5. Chọn node nguồn A: panel phải chuyển sang tên A và phần bounds của pool cũ đóng lại. Bấm View residue bounds một lần nữa để xem A.

**Đạt khi:** đúng lô được chọn, preview có mốc riêng và lý do thiếu dữ liệu hiện rõ. Không bấm **Lock snapshot for audit** trong luồng đọc này; nút đó ghi thêm snapshot, không cần để xem preview. Graph snapshot, lab evidence và residue preview là những lần đọc khác nhau; không coi chúng là một chứng nhận pháp lý.

## Ghi nhận khi test

Mỗi lần Run trace lưu TRACEBACK audit, không tiêu thụ tồn. Các thao tác xem Table/Flowchart, chọn node, đọc evidence/preview không phải nhập/rút hàng. Nếu đồng đội thay dữ liệu trong lúc test, counts/current pool có thể khác mốc 24/10; đối chiếu generation mới trước khi kết luận bug.

Nếu cần báo lỗi, ghi luồng/bước, mã starting lot, direction/view, thông báo lỗi hoặc trace reference. Không gửi mật khẩu, cookie hay connection string. Có thể xem thêm [hướng dẫn chi tiết](traceability-web-test-guide.md).
