# Một luồng demo container và tracing cho giảng viên — dùng dữ liệu Supabase đã chọn

**Thực hiện đúng luồng dưới đây, không cần chọn giữa nhiều kịch bản.** Dùng thành phẩm đã blend sẵn, tạo order/container mới 10 kg, duyệt DIRECT_STOCK, load **4 + 6 kg**, CLOSE rồi tracing hai chiều. Không có bước production mới trong luồng này; lịch sử blend sẽ được trình bày qua tracing.

**Bộ dữ liệu sử dụng: seed/demo hiện có trên Supabase**, không phải DB Testcontainers cục bộ. Dùng `DEMO-CONTAINER / PADDY` xuyên suốt order, stock, assessment policy và MRL để không đi qua điểm vướng PADDY/RICE. Các tên synthetic được giữ nguyên để giảng viên biết đây là dữ liệu phần mềm giả lập.

Kiểm tra gần nhất: `2026-11-18T06:35:14.957334+00:00` (UTC). Lượt chỉnh plan chỉ kiểm tra DB bằng SELECT/READ ONLY, không chạy lại seed, tạo stock bù, reset fixture hoặc tạo order/approval/load mới. Bộ demo cùng loại đã được dùng thành công ở CT-24 trước đây; 10 kg được chọn dưới đây là phần stock còn lại. Preview/approval vẫn kiểm lại dữ liệu hiện hành khi bạn bấm trên web; chưa có một lượt demo mới được thực hiện bởi việc viết tài liệu này.

## 0. Một bộ dữ liệu thống nhất cho toàn bộ luồng

| Thành phần | Giá trị đã chọn và cách sử dụng |
|---|---|
| Market | **DEMO-CONTAINER — GrainLedger Demo Container Market (synthetic)**. Không dùng EU/VN/US trong lượt này. |
| Commodity | **PADDY — Paddy** cho order, khớp commodity hiện lưu trên stock và policy/MRL demo. |
| Product identity | **BLENDED_RICE — Blended rice**. Hàng là gạo đã blend; tên commodity PADDY giữ theo nguồn ban đầu. |
| MRL | Bộ **DEMO-CONTAINER/PADDY version 1**, PUBLISHED, limit demo **1 mg/kg** cho IPT002. Đây là số liệu synthetic, không phải giới hạn pháp lý để chứng nhận xuất khẩu. |
| Assessment policy | Policy **DEMO-CONTAINER/PADDY version 1** hiện có của GrainLedger Demo Customer, yêu cầu IPT002; giữ nguyên margin và quality rule đang lưu. |
| Tài khoản/công ty | Hai tài khoản Export/CM hiện có, cùng **GrainLedger Demo Customer**, không tạo mới. |
| Facility/kho | **GrainLedger Demo Container Test Mill (synthetic)** / **Demo Container Blend Store**. Không chuyển hàng hoặc tạo kho mới. |
| Unit/quality | **KG**; **Moisture / %**, observation **13.2%**, specification max **14%**, minimum trống. |
| Lot | **BLD-20261117-000064**, 10 kg còn lại, chọn ordinary position của Blend Store. |
| Nguồn/evidence | Hai source lots A/B, procurement, plot, samples và lineage đã có — xem mục 8. Không tạo farmer/plot/lab/profile mới. |
| Record mới trong buổi demo | Chỉ order/container/plan/proposal và các giao dịch approve/load/CLOSE của lượt mới. Mã hệ thống tự sinh; number/seal nhập đúng mục 1. |

Luồng này không phụ thuộc việc sửa mapping của các commodity chuẩn còn vướng. Không kết luận dữ liệu gốc EU/Rice bị hỏng: đó là vấn đề contract/code cần kiểm chứng riêng, ngoài đường demo đã chọn.

## 1. Các giá trị đã chốt — nhập đúng bảng này

| Mục trên web | Giá trị sử dụng |
|---|---|
| Địa chỉ web | `http://localhost:5173` — web/API phải đang chạy matching build S2. |
| Database | Supabase cấu hình hiện có; không dùng UUID từ các báo cáo DB test cô lập. |
| Tài khoản Export | `export@grainledger.local` |
| Tài khoản Customer Manager | `customer.manager@grainledger.local` |
| Công ty của cả hai | **GrainLedger Demo Customer** |
| Export market | **GrainLedger Demo Container Market (synthetic)** — code **DEMO-CONTAINER** |
| Commodity | **Paddy** — code **PADDY** |
| Required mass | **10** |
| Unit | **KG** |
| Shipping from | **2026-11-19** |
| Shipping to | **2026-11-25** |
| Quality attribute | **Moisture** — code **MOISTURE**, unit **%** |
| Minimum | Để trống. |
| Maximum | **14** |
| Số container targets | **1** |
| Target mass / Unit | **10 / KG** |
| Product to ship | **Blended rice** — `BLENDED_RICE` |
| Facility | **GrainLedger Demo Container Test Mill (synthetic)** |
| Approved position / lot cần chọn | **BLD-20261117-000064**, tại **Demo Container Blend Store** |
| Container number | **DEMO-CONTAINER-20261118-01** |
| Seal number | **SEAL-DEMO-CONTAINER-20261118-01** |
| Actual loaded quantity lần 1 | **4** kg |
| Actual loaded quantity lần 2 | **6** kg |
| Trace view | **Current — effective operations** |

Đăng nhập bằng mật khẩu hiện có của hai tài khoản; không lưu mật khẩu vào slide/Git hoặc chạy lại seed. Mở Export ở cửa sổ trình duyệt thường, CM ở cửa sổ ẩn danh để hai phiên không đè nhau.

Order code, container internal code, plan/proposal ID **do hệ thống sinh**. Sau khi tạo, giữ đúng đối tượng vừa tạo và copy URL qua cửa sổ CM; không tự nhập mã `EXP-...`/`CTN-...` hoặc mở lại container cũ để ghi loading. Những giá trị tự sinh không phải lựa chọn nghiệp vụ bạn cần quyết định.

### Thông tin xác định đúng dữ liệu khi tên bị trùng

| Đối tượng | UUID trên Supabase |
|---|---|
| Market DEMO-CONTAINER | `04d9e540-045b-4174-a97b-2f7537ea00a9` |
| Catalog commodity PADDY cho order và stock/nguồn | `47c120b2-0704-4211-9f5d-ed37039af279` |
| Moisture | `e9c2a772-d010-43dc-8c81-6bd41c10bfb7` |
| Facility | `3e7c6c0c-2936-5a5c-9495-18e4ac027920` |
| Demo Container Blend Store | `05fee1f0-67a0-5cf4-94a7-72d27204beb2` |
| Finished Ledger lot BLD-20261117-000064 | `b3cf913a-f6bd-5916-956c-6bce85392aed` |

**Không chọn** EU/VN/US, DEMO-CONTAINER-NOPOLICY, DEMO-CONTAINER-NOMRL, facility “Quality Mill”, current mixed pool hoặc lot HEAD_RICE khác. Chọn đúng **DEMO-CONTAINER / PADDY / Blended rice**. Không cần đổi commodity của lot hoặc sửa dữ liệu lịch sử.

## 2. Giới thiệu trước giảng viên — khoảng 30 giây

> “Em demo một container 10 kg từ thành phẩm đã blend. Luồng gồm lập nhu cầu, đánh giá phương án, duyệt và giữ hàng, ghi nhận loading hai lần, đóng container và truy xuất hai chiều. Dữ liệu là synthetic phục vụ kiểm thử, không phải chứng nhận xuất khẩu.”

> “Stock đã chọn hiện còn đúng 10 kg. Hai nguồn là hai Ledger source lots; chúng cùng thuộc procurement PRC-20261117-0004 và thửa PLT-0001-01, giống ST25. Hai source lots không đồng nghĩa hai nông dân hoặc hai thửa khác nhau.”

## 3. Tạo order và container — cửa sổ Export

1. Đăng nhập **export@grainledger.local** → **Export orders** → **+ Create export order**.
2. Điền **Export market = GrainLedger Demo Container Market (synthetic)**, **Commodity = Paddy (PADDY)**, **Required mass = 10**, **Unit = KG**.
3. Điền **Shipping from = 2026-11-19**, **Shipping to = 2026-11-25**.
4. Trong quality specification, chọn **Moisture**, **Minimum trống**, **Maximum = 14**. Unit phải là **%**. Lot đã có quality observation **13.2%**, version 1; không cần tạo measurement mới để demo.
5. Trong container targets, giữ **một target 10 KG**. Bấm **Create export order**.
6. Ghi/copy mã order vừa sinh. Ở **Container targets**, bấm **Open** trên container vừa tạo; copy URL container vào ghi chú và gửi sang cửa sổ CM.
7. Chỉ cho giảng viên thấy: **PLANNED**, Actual loaded **0.000000 kg**, Remaining **10.000000 kg**. Chưa approve thì chưa có quyền load, dù stock vật lý đang có.

Lời trình bày: **“Order và target mới chỉ là nhu cầu, chưa ghi nhận hàng thực xếp.”**

## 4. Lập phương án — vẫn cửa sổ Export

8. Ở **Stock and blend plans**, chọn **Product to ship = Blended rice** → **+ Create draft**.
9. Kiểm **Target = 10 kg**. Ở **Facility**, chọn đúng **GrainLedger Demo Container Test Mill (synthetic)**, đợi bảng stock tải xong.
10. Tìm lot **BLD-20261117-000064** tại **Demo Container Blend Store**, available **10 kg**. Bấm **Calculate plan**.
11. Kiểm cả ba điều kiện trước khi đi tiếp: **Assessment PASS**, selection **Direct stock**, selected lot đúng **BLD-20261117-000064**, selected quantity **10 kg**. Solver tự chọn stock; không bấm tiếp nếu nó chọn khác lot đã chốt.
12. Mở **Checks and evidence** / chi tiết evidence: chỉ ra quality **13.2% ≤ 14%**, residue được đánh giá theo policy/MRL. Bộ DEMO-CONTAINER/PADDY dùng MRL version 1, limit demo **1 mg/kg** cho IPT002; policy có margin riêng. Đọc ngưỡng assessment sau margin từ evidence trên màn hình, không đồng nhất nó với raw MRL. Đây là dữ liệu demo, không trình bày là chứng nhận đạt chuẩn EU.
13. Bấm **Save proposal**. Copy URL plan sang cửa sổ CM.

Lời trình bày: **“Plan này dùng DIRECT_STOCK vì hàng đã blend trước đó. Lập phương án chưa blend thêm và cũng chưa trừ stock.”**

## 5. Duyệt và reserve — cửa sổ CM

14. Đăng nhập **customer.manager@grainledger.local**, mở URL plan vừa copy. Kiểm đúng container, lot và quantity **10 kg**.
15. Ở **Proposal decision**, bấm **Approve proposal**, sau đó **Approve and reserve stock**.
16. Đợi lưu xong: Decision **Approved**, reservation **10 kg**, consumed **0 kg**. Physical vẫn **10 kg**, available thành **0 kg** vì toàn bộ hàng đã được giữ cho plan này.

Lời trình bày: **“Available bằng 0 vì hàng đã reserved, không phải hàng đã biến mất. Chính approval này vẫn được load đủ quota của nó.”**

## 6. Metadata và actual loading — cửa sổ Export

17. Quay lại URL container mới → **Refresh container checks**. Kiểm có active approved stock đúng lot và **Loading checks: PASS**.
18. Trong **Physical container and seal**, nhập **Container number = DEMO-CONTAINER-20261118-01**, **Seal number = SEAL-DEMO-CONTAINER-20261118-01** → **Save container metadata**. Đợi lưu xong; refresh xác nhận hai giá trị vẫn còn.
19. Trong **Record actual loading**, chọn **Approved position = BLD-20261117-000064**. Nhập **Actual loaded quantity (kg) = 4**.
20. Chỉ ra nút commit chưa mở khi chưa xác nhận. Tick **I confirm this is the actual loading to record.** → **Commit actual loading**.
21. Đợi hoàn tất: status **LOADING**, Actual loaded **4.000000**, Remaining **6.000000**, manifest có một lần load 4 kg. Refresh trang: vẫn một lần load, không bị ghi hai lần.
22. Ở cửa sổ CM, mở container mới → **Trace container sources**. Mong đợi hai nguồn **2 kg + 2 kg**, tổng **4 kg actual**, không phải target 10 kg. Đối chiếu hai source IDs ở mục 8.
23. Quay lại Export, chọn lại approved position đúng lot, nhập **6**, tick lại xác nhận → **Commit actual loading**.
24. Đợi hoàn tất: Actual loaded **10.000000**, Remaining **0.000000**, manifest có đúng hai lần **4 và 6 kg**. Quota đã dùng hết; không tạo load thứ ba.

### Số liệu cần trình bày — theo stock hiện có, không phải lot 20 kg ban đầu

| Mốc | Physical của lot | Reservation còn lại của plan mới | Available | Actual loaded của container mới | Nguồn A/B trong container mới |
|---|---:|---:|---:|---:|---|
| Trước approve | 10 kg | 0 | 10 kg | 0 | Chưa có actual source loading. |
| Sau approve | 10 kg | 10 kg | 0 | 0 | Chưa có actual source loading. |
| Sau load 4 | 6 kg | 6 kg | 0 | 4 kg | 2 + 2 kg |
| Sau load thêm 6 | 0 | 0 | 0 | 10 kg | 5 + 5 kg |
| Sau CLOSE | 0 | 0 | 0 | 10 kg | 5 + 5 kg, dossier cố định. |

Lot này **ban đầu từng được tạo 20 kg**, và 10 kg đã được load trong lượt CT-24 trước đây. Luồng demo này dùng **10 kg còn lại**, không dùng lại phần đã load. Hai source contributions gốc của lot là 10+10 kg, nhưng phần nguồn của container mới chỉ là 5+5 kg.

## 7. CLOSE và dossier — Export rồi CM

25. Export bấm **Refresh container checks**. Kiểm **Closure checks: PASS**, đủ target, đã có number/seal và không restriction.
26. Bấm **Close container** → chấp nhận hộp xác nhận. Đợi status **CLOSED**.
27. Bấm **Read closure dossier** → **Download dossier JSON**, rồi **Read container history**. Kiểm đúng hai loads 4+6 và sự kiện CLOSE; không có debit stock thêm 10 kg khi đóng.
28. Sang cửa sổ CM, refresh container mới → **Read closure dossier**. CM thấy source composition/evidence đầy đủ; view Export đã lọc các thông tin đó.

Lời trình bày: **“CLOSE đóng băng hồ sơ actual đã ghi, không phải thêm một lần loading. Quyền xem hồ sơ khác nhau theo vai trò.”**

## 8. Trace ngược — container → hai source lots → nguồn gốc

Hai nguồn **đã chọn sẵn**, không cần tự tìm source khác:

| Nguồn | Source Ledger code | Ledger UUID để copy vào Traceability | Procurement / plot / sample |
|---|---|---|---|
| **A** | **RAW-20261117-000018** | `10770c77-aac8-5592-838d-5280bbfd0bfb` | PRC-20261117-0004 / PLT-0001-01 / SMP-20261117-0007 |
| **B** | **RAW-20261117-000024** | `0abdcfe4-5d5a-5224-8a95-e10012ef6a3f` | PRC-20261117-0004 / PLT-0001-01 / SMP-20261117-0013 |

29. CM mở container **vừa tạo trong buổi demo** → **Trace container sources**. Kiểm bảng **Actual container sources** có A/B đúng UUID trên, mỗi nguồn **5.000000 kg**, mỗi nguồn **50%**, tổng **10 kg**, provenance đầy đủ.
30. Vào **Ledger → Traceability**. Nhập **Ledger lot ID = `b3cf913a-f6bd-5916-956c-6bce85392aed`** — đây là finished lot BLD-20261117-000064.
31. Chọn **Trace direction = Backward — contributing sources**, **Trace view = Current — effective operations** → **Run trace**.
32. Bấm **Flowchart** để trình bày lịch sử processing, sau đó **Table** để xem số liệu. Chọn lot thành phẩm trong kết quả để xem **Traced source contributions**.
33. Mở **View field / season origin**. Chỉ ra procurement **PRC-20261117-0004**, plot **PLT-0001-01**, giống **ST25** và record mùa vụ đã lưu. Với quyền lab hiện có, bấm **View samples and laboratory evidence** để thấy các sample A/B đã nêu.

**Phân biệt khi giải thích:** backward của **finished lot** có thể hiển thị contribution lịch sử **10+10 kg** của lot tạo 20 kg. Backward của **container mới** ở bước 29 là **5+5 kg actual loaded**. Không gọi hai bảng này là số kg của cùng một đối tượng.

> “Tỷ lệ nguồn là phân bổ kế toán khối lượng dựa trên lineage đã commit; không nhận diện từng hạt gạo và không thay thế chứng nhận pháp lý.”

## 9. Trace xuôi — source A → container đã nhận hàng

34. Vẫn cửa sổ CM, ở **Ledger → Traceability**, nhập **Ledger lot ID = `10770c77-aac8-5592-838d-5280bbfd0bfb`** — source A đã chốt.
35. Chọn **Forward — recorded descendants**, giữ **Current — effective operations** → **Run trace**.
36. Trong **Actual container targets**, tìm **mã container mới vừa demo**: Linked loaded kg **10.000000**, status **CLOSED**, không HOLD/restriction ở luồng chuẩn.
37. Bảng có thể hiện thêm **CTN-20261117-000014**, là container cũ đã load phần 10 kg trước đó của cùng lot. Đây là kết quả hợp lệ, không phải nhân đôi loading. Mỗi dòng là một container với actual riêng; chỉ vào đúng mã mới đang trình bày.
38. Bấm link mã **container mới** để quay lại hồ sơ của nó. Chốt vòng truy xuất **container → source → container**.

> “Linked loaded kg là actual load của lot trên đường truy xuất, nên dòng container mới hiện 10 kg. Nguồn A đóng góp riêng 5 kg; số đó xem ở backward của container. Một nguồn có thể liên quan nhiều container qua các lần load thật khác nhau.”

## 10. Các điểm chặn nên chỉ ra ngay trong luồng, không tạo nhánh mới

- Bước 7: chưa approve thì chưa load được.
- Bước 20: chưa tick actual confirmation thì chưa commit được.
- Sau load 4 kg: chưa đủ target để CLOSE.
- Bước 21: refresh không nhân đôi lần load.
- Bước 28: Export/CM nhận thông tin hồ sơ khác nhau.
- Sau CLOSE: không còn form load mới/đổi metadata thông thường.

**Không đặt HOLD, đổi evidence/MRL, revoke hoặc test mạng lỗi vào fixture này giữa buổi demo.** Các thao tác đó có thể chặn luồng; chúng không cần thiết để trình bày đường chuẩn đã chốt.

## 11. Nếu dữ liệu thực tế không khớp

Nếu stock không còn 10 kg, preview không PASS, solver chọn khác lot hoặc có HOLD: **dừng tại bước đó và ghi đúng lỗi**. Không tự đổi market, commodity, limit hay chọn lot khác. Gửi lại mã container/plan, bước bị chặn và finding hiển thị để xử lý đúng fixture đã chốt.

Luồng này tiêu thụ **toàn bộ 10 kg còn lại**. Không chạy rehearsal đến commit/load rồi kỳ vọng lặp lại y hệt cho giảng viên. Muốn có một lượt thực hành và một lượt trình bày độc lập cần thêm fixture riêng; tài liệu này chưa tạo hoặc refill dữ liệu đó. Container CLOSED cũ không được reset để chạy lại.

## 12. Phiếu kết quả sau buổi demo

| Nội dung | Kết quả ghi sau khi thực hiện |
|---|---|
| Order code / container code tự sinh | … |
| Ngày giờ thực hiện | … |
| Preview đúng BLD-20261117-000064 và PASS | PASS / FAIL / NOT RUN |
| Approval reserve 10 kg, actual ban đầu 0 | PASS / FAIL / NOT RUN |
| Hai actual loads 4 và 6 kg | PASS / FAIL / NOT RUN |
| CLOSED, không debit thêm, dossier/history đúng | PASS / FAIL / NOT RUN |
| Container backward A/B = 5+5 kg | PASS / FAIL / NOT RUN |
| Source A forward có đúng container mới | PASS / FAIL / NOT RUN |

Các dòng này chỉ để ghi kết quả, không phải các tham số bạn cần tự chọn. Tài liệu tham chiếu: [tài khoản](development-accounts.md), [API S2](../api/container-workflow-api.md), [triển khai Supabase và phân biệt dữ liệu](../reports/container-s2-deployment-2026-11-18.md). Lượt chỉnh tài liệu chỉ đọc DB và sửa hướng dẫn, chưa chạy luồng demo mới, chưa commit/push.
