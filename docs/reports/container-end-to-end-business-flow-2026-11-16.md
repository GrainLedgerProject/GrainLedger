# Luồng container — tóm tắt để bàn giao thực hiện

Cập nhật 16/11/2026, theo code `feat/container` đã nhận dev đến `7b8a4a2` trong working tree.

## 1. Bắt đầu từ đâu và mục tiêu là gì?

**Luồng bắt đầu khi Export Staff tạo export order và target container.** Mục tiêu là lấy đúng hàng theo phương án đã duyệt, ghi nhận lượng thực xếp, trừ tồn đúng một lần và đóng container kèm hồ sơ để truy xuất.

Luồng có blend sau duyệt cần đi như sau:

```text
Export Staff tạo order + target container
  → Export Staff lập và gửi phương án
  → Customer Manager duyệt, giữ tồn nguyên liệu
  → Nhà máy blend, tạo lô thành phẩm mới
  → Gắn lô thành phẩm và lượng được giữ với đúng phương án/container
  → Export Staff ghi actual loading, trừ tồn thành phẩm
  → Export Staff đóng container, chốt hồ sơ
  → Tracing hiển thị hàng trong container đến từ đâu
```

**Đây là luồng cần hoàn thiện. Code hiện còn thiếu đoạn nối blend sau duyệt với lô đầu ra/reservation để container sử dụng.** Nếu đã có thành phẩm phù hợp trong kho thì dùng đường DIRECT_STOCK: duyệt và giữ chính thành phẩm đó, rồi loading, không cần blend thêm.


**Blend là bước chuẩn bị thành phẩm cho container khi phương án yêu cầu phối trộn. Container không thực hiện blend:** nhà máy/Ledger thực hiện sản xuất; container nhận lô đầu ra và lượng được phép sử dụng để loading.

Cần phân biệt rõ hai đường:

- **Thành phẩm đã có sẵn:** order → phương án DIRECT_STOCK → CM duyệt/giữ thành phẩm → loading → CLOSE → hồ sơ/tracing. Code hiện hỗ trợ đường này trong phạm vi position/source được phép, kể cả thành phẩm đã blend trước khi lập phương án.
- **Phải blend sau duyệt:** order → phương án phối trộn → CM duyệt/giữ nguyên liệu → nhà máy blend tạo lô mới → gắn và giữ lô đầu ra cho container → loading → CLOSE → hồ sơ/tracing. Đoạn nối sản xuất theo approval với output/reservation còn cần hoàn thiện.

**Phần từng bước bên dưới tập trung vào đường thứ hai, là luồng đang cần bàn giao để làm tiếp.** Blend không bắt buộc cho mọi container; nhưng với đường này phải ghi rõ blend và bàn giao đầu ra, không rút gọn thành “thành phẩm được phép dùng” rồi coi như đã nối xong.

## 2. Từng bước, ai làm và kết quả là gì?

### Bước 1 — Tạo export order và container dự kiến

**Người làm: Export Staff.**

Nhập loại hàng, thị trường, tổng khối lượng, yêu cầu chất lượng và thời gian giao; chia lượng cần giao vào các container.

Ví dụ: order cần **10 kg gạo phối trộn**, tạo một container target **10 kg**.

**Kết quả:** có order và planned container để lập phương án. Chưa giữ tồn, chưa trừ hàng. **Đã có trong code.**

### Bước 2 — Lập phương án dùng hàng

**Người làm: Export Staff.**

Chọn sản phẩm cần giao và facility, bấm Calculate plan. Hệ thống chọn hàng và đánh giá theo yêu cầu. Khi đạt, Export lưu proposal để CM duyệt.

Ví dụ phương án cần blend **6 kg lô A + 4 kg lô B** thành 10 kg sản phẩm cần giao.

**Kết quả:** proposal ghi rõ hàng được chọn, lượng cần dùng và căn cứ đánh giá. Chưa giữ tồn hoặc trừ hàng. **Đã có trong code.**

### Bước 3 — Duyệt phương án và giữ tồn

**Người làm: Customer Manager của công ty sở hữu hàng.**

CM kiểm tra rồi approve. Hệ thống reserve lượng hàng của phương án để phương án khác không dùng trùng.

Ví dụ: giữ **6 kg A và 4 kg B**. Hàng vẫn nằm trong kho; physical stock chưa giảm, chỉ lượng tự do có thể dùng giảm.

**Kết quả:** active approval và reservation gắn với phương án/container. **Đã có trong code.**

### Bước 4 — Blend và bàn giao thành phẩm cho container

**Người làm: nhà máy — Mill Operator thực hiện, Mill Manager review khi workflow yêu cầu.**

Nhà máy blend theo phương án đã duyệt. Giả sử không hao hụt:

- Trừ **6 kg A và 4 kg B**, đồng thời xử lý phần reservation nguyên liệu tương ứng.
- Tạo **lô C có 10 kg thành phẩm** và lưu lineage C đến từ A/B.
- Ghi rõ **C là đầu ra của phương án này, dành cho container này, được phép loading 10 kg**.
- Giữ 10 kg C cho container để phương án khác không lấy mất.

“Bàn giao” ở đây là liên kết trong hệ thống giữa **phương án đã duyệt → lần sản xuất → lô đầu ra/vị trí → quota thành phẩm của container**. Container phải thấy lô C để xếp, thay vì tiếp tục lấy nguyên liệu A/B trong proposal cũ.

**Hiện trạng:** Ledger đã có BLENDING tạo thành phẩm. Nhưng đoạn thực hiện blend gắn approval và bàn giao output/reservation cho container **chưa nối hoàn chỉnh**. Mode `BLEND_INPUT_STOCK` hiện bị chặn loading. Đây là điểm chính người tiếp theo cần phối hợp với phần order/blend/reservation để hoàn thiện.

### Bước 5 — Actual loading vào container

**Người ghi nhận trên hệ thống: Export Staff.**

Tại **Export orders → container → Loading and closure**, ghi số vỏ container/seal, chọn thành phẩm được duyệt và nhập lượng đã thực sự xếp. Lưu số container/seal riêng không trừ hàng.

Ví dụ xếp lô C hai lần:

- Lần 1 xếp **4 kg**: tồn C giảm 4, reservation C được consume 4, manifest ghi một lần load 4 kg.
- Lần 2 xếp **6 kg**: tồn C giảm thêm 6, reservation được consume thêm 6; actual loaded đạt 10 kg.

Stock debit, reservation consume và actual load phải ghi đồng thời. Retry cùng thao tác không được trừ lần nữa.

**Kết quả:** biết chính xác lô nào đã xếp, bao nhiêu kg và nguồn đóng góp. Trạng thái chuyển PLANNED → LOADING từ lần load đầu.

**Hiện trạng:** đã có loading từng phần từ **DIRECT_STOCK — thành phẩm sẵn có được duyệt/reserve**, kể cả hàng đã blend trước khi lập proposal. Loading từ lô C của blend sau duyệt cần hoàn thiện bước 4 trước.

### Bước 6 — Đóng container

**Người làm: Export Staff.**

Khi actual loaded đủ target, hệ thống kiểm số container/seal, approval/căn cứ đánh giá hiện hành và HOLD/restrictions. Đủ điều kiện mới cho CLOSE.

**Kết quả:** container thành **CLOSED**, chốt manifest và lưu dossier JSON bất biến, có lịch sử thao tác. **Không trừ tồn lần nữa**, vì đã trừ khi actual loading.

**Đã có trong code cho đường được hỗ trợ.** Container đã đóng không cho loading/sửa metadata thông thường; chưa có luồng unload/reopen/reseal. CLOSED kết thúc phần đóng container hiện có, không tự chứng minh hàng đã vận chuyển đến người mua.

## 3. Sau đó tracing hiển thị gì?

Tracing đọc lại **actual loading và lineage đã ghi**. Có thể trace ngay sau khi load một phần, không cần chờ đóng.

**Từ container truy ngược:**

- Container đã xếp những lô thành phẩm nào, lượng bao nhiêu.
- Các lô đó đến từ những nguồn nào, mỗi nguồn đóng góp bao nhiêu kg và tỷ lệ.
- Provenance tới procurement/thửa ruộng/mùa vụ khi dữ liệu nguồn đầy đủ; thiếu thì báo incomplete.

Với ví dụ 6 kg A + 4 kg B → 10 kg C, nếu A và B mỗi lô có một nguồn và toàn bộ C được xếp, container thể hiện đóng góp 6 kg từ nguồn A và 4 kg từ nguồn B. Nếu mới xếp 4 kg thì source total chỉ là 4 kg, không lấy target 10 kg làm lượng đã giao.

**Từ nguồn/lô truy xuôi:** hiển thị những actual containers đã nhận hàng trên đường vật liệu đó. `Linked loaded kg` là lượng load từ các lô liên quan; muốn biết lượng riêng của từng nguồn trong lô nhiều nguồn thì xem backward container.

**Đã có actual tracing trong code.** CM được xem backward nguồn chi tiết; Export đọc theo quyền và nhận dossier đã lọc source/evidence. Trace không trừ tồn và không tự tạo/release HOLD; thao tác truy xuất có thể ghi audit.

## 4. Những điểm phải giữ đúng khi nối tiếp

- **Approve:** chỉ reserve, chưa trừ physical stock.
- **Blend:** trừ nguyên liệu đầu vào và tạo tồn thành phẩm mới.
- **Loading:** trừ thành phẩm thực xếp, đồng thời consume reservation của thành phẩm.
- **CLOSE:** kiểm và chốt hồ sơ, không debit lần hai.
- **Trace:** dùng actual đã ghi, không lấy draft/proposal làm hàng đã xếp.

Lab, quality, MRL và assessment policy là dữ liệu được các bước đánh giá dùng lại. Thiếu hoặc không đạt thì bị chặn; container không tự tạo dữ liệu thay các phần đó. Đổi evidence sau khi đã load một phần hiện có thể chặn tiếp tục và chưa có luồng reviewed revalidation hoàn chỉnh.

## 5. Tiếp theo cần làm tiếp gì?

**Ưu tiên nối bước 4 với bước 5**, phối hợp người phụ trách order/blend/reservation:

1. Gắn lần blend thực tế với đúng proposal/approval đã duyệt; chỉ dùng nguyên liệu trong quota được phép.
2. Ghi và bàn giao output lot, location, actual kg, lineage và reservation thành phẩm cho đúng container; xử lý nguyên liệu/đầu ra trong transaction phù hợp để không giữ/trừ trùng.
3. Cho container đọc và consume đúng quota đầu ra khi loading, kiểm lại current assessment/restrictions.
4. Kiểm xuyên suốt: **approve → blend → output reservation → partial loading → CLOSE → trace**, đối chiếu stock/quota/source và retry.

Nếu có hao hụt, dư/thiếu đầu ra, nhiều mẻ hoặc đổi phương án sau execution, phải thống nhất quy tắc xử lý trước, không tự nâng target hoặc bỏ quota. Hàng từ mixed pool/custody-derived position cũng còn cần reservation/source handoff riêng; chưa mặc định đi được chỉ vì Ledger đã có trace pool.

**Đường có thể dùng ngay để kiểm container:** thành phẩm phù hợp đã có trong kho → DIRECT_STOCK proposal → CM approve/reserve → actual loading → CLOSE → dossier/trace.

Chi tiết kỹ thuật và phần còn thiếu: [plan container](../plans/container-execution-plan-2026-11-13.md). Thao tác kiểm thử web: [test guide chi tiết](container-web-test-report-2026-11-16.md).
