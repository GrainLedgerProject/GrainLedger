# Demo mới: container 10 kg và tracing hai chiều

**Bắt đầu ở menu Export orders, không phải Ledger → Containers.** Mở [Export orders](http://localhost:5173/export/orders) → **+ Create export order**. Container thực thi nằm trong **Container targets** của order. Trang Ledger → Containers chỉ liệt kê preparation legacy; bản ghi `DEMO-CONTAINER-LEGACY-20261117-01` và thông báo “awaiting integration” ở đó không phải luồng loading/CLOSE đang demo. Không tạo preparation ở trang legacy cho kịch bản này.

**Dùng tài liệu này thay cho luồng cũ.** Dữ liệu nằm trên **Supabase hiện có**. Lot mới **BLD-20261118-000079 còn 10 kg khả dụng**, kiểm lúc **19:13 ngày18/11/2026** sau một lượt chạy thử độc lập đã hoàn tất trên web. Phần 10 kg này chưa load vào container của buổi trình bày; bạn sẽ tạo order/container mới theo đúng các bước dưới đây.

Lượt chạy thử đã hoàn tất: **EXP-20261118-000025 / CTN-20261118-000025**, đã ghi hai lần loading 4 kg và 6 kg, trạng thái **CLOSED**. Tracing sau lần loading đầu hiển thị 2 + 2 kg; sau khi đủ 10 kg hiển thị 5 + 5 kg. **Không dùng container **CTN-20261118-000025** để load lại.** Container CLOSED vẫn mở dossier/tracing được nhiều lần; muốn thực hiện lại approval/loading/CLOSE cần container mới và stock khả dụng mới.

## 1. Tên trường và giá trị cần chọn trên web

| Mục | Nhập/chọn đúng giá trị |
|---|---|
| Web | `http://localhost:5173` |
| Export | `export@grainledger.local`, cửa sổ trình duyệt thường |
| Customer Manager (gọi tắt là CM trong hướng dẫn) | `customer.manager@grainledger.local`, cửa sổ ẩn danh riêng |
| Công ty | **GrainLedger Demo Customer** |
| Export market | **GrainLedger Demo Container Market (synthetic)** — `DEMO-CONTAINER` |
| Commodity | **Paddy — PADDY** |
| Required mass / Unit | **10 / KG** |
| Shipping from / to | **2026-11-19 / 2026-11-25** |
| Attribute 1 / Minimum (optional) / Maximum (optional) | **Moisture / để trống / 14**, đơn vị **%** |
| Container targets | Một dòng **Container 1**: **Target mass = 10**, **Unit = KG** |
| Product to ship | **Blended rice — BLENDED_RICE** |
| Facility | **GrainLedger Demo Container Test Mill (synthetic)** |
| Lot / Location | **BLD-20261118-000079 / Demo Container Blend Store** |
| Actual loaded quantity (kg), lần 1 / lần 2 | **4 / 6 kg** |
| Container number | **DEMO-CONTAINER-20261118-03** |
| Seal number | **SEAL-DEMO-CONTAINER-20261118-03** |
| Trace view | **Current — effective operations** |

Market/PADDY và BLENDED_RICE là bộ kết hợp demo đã kiểm chứng; giữ đúng, không đổi sang EU/RICE hoặc lot cũ. Các số liệu là **synthetic phục vụ demo, không phải chứng nhận xuất khẩu**. Mật khẩu dùng tài khoản hiện có, không ghi vào tài liệu/slide.

Mã `EXP-...`, `CTN-...`, plan/proposal do hệ thống sinh: ghi lại mã/URL **mới vừa tạo**, không đoán số thứ tự. Container number/seal trong bảng là số vật lý giả định của demo, cần nhập tay; chúng khác internal code CTN. Trong thực tế dùng số ghi trên container/seal thực tế.

Nếu API/web chưa chạy, mở hai terminal ở repository root:

```bash
dotnet run --project backend/src/GrainLedger.Api
```

```bash
npm run dev
```

Startup không migrate hoặc seed. Giữ nguyên DB Supabase.

**Quy tắc tracing trong hướng dẫn này:** tại **Ledger → Traceability**, luôn chọn lô qua **Find a tracing starting point → Starting lot**. Ô **Ledger lot ID** được hệ thống tự điền sau khi chọn; không gõ hoặc dán UUID vào ô đó. Hướng dẫn không giả định lỗi nhập ID trực tiếp đã được sửa. Khi truy xuất từ hồ sơ container, dùng nút **Trace container sources**; thao tác này không cần nhập ID hoặc dùng bộ chọn lô.

## 2. Kiểm tra trước buổi trình bày — không tiêu thụ hàng

1. CM → **Ledger → Stock & HOLDs → Stock**. Tìm **BLD-20261118-000079**. Kiểm đúng **Demo Container Blend Store**, physical **10 kg**, available **10 kg**, không HOLD/correction. Nếu số liệu không khớp thì dừng trước khi tạo order, không tự lấy stock của plan khác.
2. CM mở [CTN-20261118-000025 — container đã chạy thử](http://localhost:5173/export/containers/bf38e257-b045-49a9-8b4b-4946dc7b7415) → **Trace container sources**. Mong đợi hai nguồn **RAW-20261118-000071** và **RAW-20261118-000072**, mỗi nguồn **5.000000 kg**, rồi thử **Flowchart** có node **CTN-20261118-000025**. Đây là kiểm tra đọc, không dùng thêm 10 kg để dành. Sau kiểm tra, quay lại tạo đối tượng mới ở phần 3.
3. Đóng các tab thử cũ không cần dùng, giữ hai cửa sổ Export/CM. Đảm bảo API/web đang phục vụ đúng bản tracing. Lượt chạy thử từng gặp một lỗi Supabase session pool hết kết nối 15; refresh cùng order đã phục hồi và các bước nghiệp vụ sau đó PASS. Nếu gặp lỗi đọc này, refresh **đúng URL đã có**, không tạo order hoặc submit loading lần nữa để thay thế một kết quả chưa rõ.

Nếu có `permission denied for function ...`, cần kiểm lại quyền runtime; đừng đổi market, nới limit hoặc reset container. Trước đây container EXECUTE từng bị thu hồi lại, nguồn thu hồi chưa xác định. Đây là điều kiện hạ tầng phải kiểm trước demo, không thể bảo đảm bất biến trên DB dùng chung.

**Không chạy thử bằng cách commit/load phần 10 kg còn lại trước buổi trình bày.** Preview/read/trace không lấy thêm hàng; loading thật sẽ tiêu thụ nó. Khi hoàn tất một lượt demo này, toàn bộ 20 kg của lot mới đã được dùng hết, gồm 10 kg chạy thử và 10 kg trình bày.

Lời mở đầu: “Em demo một container 10 kg từ thành phẩm đã blend: tạo nhu cầu, đánh giá phương án, CM duyệt và giữ hàng, loading hai lần, CLOSE rồi truy xuất nguồn và container đích.”

## 3. Export — tạo order và container mới

4. **Export orders → + Create export order**. Chọn market/commodity, required mass/unit, shipping dates đúng bảng phần 1.
5. **Quality specification**: **Attribute 1 = Moisture**, **Minimum (optional)** để trống, **Maximum (optional) = 14**. **Container targets** giữ một target **10 KG**. Bấm **Create export order** đúng một lần và đợi chuyển sang trang order đã có mã.
6. Trong **Container targets**, bấm **Open** trên container vừa sinh. Ghi lại **mã CTN mới** và **URL container mới**; copy URL qua cửa sổ CM. Container ban đầu **PLANNED**, **Actual loaded = 0.000000 kg**, **Remaining to target = 10.000000 kg**; **Committed manifest** chưa có lần loading nào.

Nói: “Order và target là nhu cầu; chưa phải hàng đã xếp.”

## 4. Export — tính và lưu phương án

7. Trong **Stock and blend plans**, chọn **Product to ship = Blended rice** → **+ Create draft**. Hệ thống chuyển tới **Plan v1**; nếu đang ở danh sách thì mở đúng plan vừa tạo.
8. Chọn **Facility = GrainLedger Demo Container Test Mill (synthetic)**, đợi stock tải. Tìm **BLD-20261118-000079** ở **Demo Container Blend Store**, available **10 kg**, **Direct match**. Không cần tick Show other products hay Show excluded stock.
9. Bấm **Calculate plan**. Chỉ đi tiếp khi đủ tất cả:

   - Assessment **PASS**, mode **Direct stock**.
   - Selected lot **BLD-20261118-000079**, input/output **10 kg**.
   - Moisture actual **13.2%**, limit **≤14%**.
   - Residue upper **0.02 mg/kg**, assessment limit **0.9 mg/kg**.

   Raw demo MRL là1 mg/kg, policy giữ margin10%, nên assessment dùng0.9 mg/kg. Không trình bày đây là giới hạn EU hoặc nói `<0.02` là giá trị đo đúng0.02.
10. Bấm **Save proposal**, đợi Saved proposal có freshness **CURRENT**, PASS. Copy **URL plan mới** sang CM. Đừng để preview quá 10 phút mới bấm Save proposal; nếu hết hạn thì Calculate plan lại trước khi lưu.

Nói: “Đường này là DIRECT_STOCK vì thành phẩm đã có sẵn. Lịch sử drying/milling/blending sẽ xem trong tracing, không blend thêm trong buổi trình bày.”

## 5. CM — duyệt và giữ đúng hàng

11. CM mở URL plan mới. Kiểm đúng container mới, lot **BLD-20261118-000079**, quantity **10 kg**, PASS/CURRENT.
12. **Proposal decision → Approve proposal → Approve and reserve stock**. Đợi **Decision = Approved**, **Reserved = 10 kg**, **Still held = 10 kg**, **Consumed = 0 kg**.
13. Nếu xem lại stock: physical vẫn **10 kg**, available **0 kg** vì đã reserved. Quota loading của chính approval này vẫn 10 kg. Đừng chọn stock khác chỉ vì available trở về 0.

## 6. Export — metadata, partial loading và trace partial

14. Export quay lại **URL container mới** → **Refresh container checks**. Phải có Loading checks **PASS**, bảng **Approved reserved stock** có đúng **BLD-20261118-000079**, **Reservation remaining (kg) = 10.000000** và **Restriction = Current position**.
15. Trong **Physical container and seal**, nhập **Container number = DEMO-CONTAINER-20261118-03**, **Seal number = SEAL-DEMO-CONTAINER-20261118-03** → **Save container metadata**. Đợi lưu xong rồi refresh kiểm hai giá trị vẫn còn.
16. **Record actual loading**: **Approved position = BLD-20261118-000079**, **Actual loaded quantity (kg) = 4**. Chỉ ra nút **Commit actual loading** còn disabled khi chưa tick. Tick **I confirm this is the actual loading to record.** → **Commit actual loading**.
17. Đợi LOADING, **Actual loaded = 4.000000 kg**, **Remaining to target = 6.000000 kg**. Refresh: manifest vẫn đúng **một load 4 kg**. Close container vẫn disabled vì chưa đủ target.
18. CM mở **container mới** → **Trace container sources**: bảng nguồn **RAW-20261118-000071 = 2.000000 kg**, **RAW-20261118-000072 = 2.000000 kg**, mỗi nguồn **50%**. Chọn **Flowchart**: một node có nhãn **STARTING CONTAINER**, cạnh actual loading **4 kg**, ancestry đầy đủ. Không nói đã truy ra target 10 kg khi mới load 4 kg.

## 7. Export — load phần còn lại và CLOSE

19. Export chọn lại **Approved position = BLD-20261118-000079**, nhập **Actual loaded quantity (kg) = 6**, tick lại **I confirm this is the actual loading to record.** → **Commit actual loading**.
20. Đợi **Actual loaded = 10.000000 kg**, **Remaining to target = 0.000000 kg**, **Committed manifest** đúng **hai lần loading 4 kg và 6 kg**. Không có lần thứ ba. Closure checks phải **PASS** trước khi đóng.
21. Bấm **Close container**; popup **“Close this container and freeze its manifest?”** → xác nhận. Đợi status **CLOSED**, **Actual loaded = 10.000000 kg**, **Remaining to target = 0.000000 kg**, có nút **Read closure dossier**. CLOSE không trừ thêm 10 kg.
22. Refresh: status vẫn CLOSED, không còn form loading mới. Nếu phần Loading checks hiện INCOMPLETE kèm **The container is closed / The target is loaded**, đó là vì container đã đóng và không được load tiếp; không suy CLOSE thất bại. Điều này chỉ áp dụng sau khi đã xác nhận CLOSED/manifest 10 kg/dossier có sẵn. INCOMPLETE trước CLOSE vẫn phải xử lý đúng finding.

## 8. CM — dossier và backward Flowchart từ container

23. CM refresh **container mới** → **Read closure dossier**. Chỉ ra snapshot actual **10 kg**, đúng hai lần loading **4 kg / 6 kg**, source contributions **5 kg + 5 kg**. **Read container history** sẽ có METADATA, LOAD, LOAD, CLOSE; danh sách tóm tắt chỉ kind/time, số liệu xem ở manifest/dossier.
24. Bấm **Trace container sources**. Hai nguồn phải là:

| Source | Actual kg | Share (%) |
|---|---|---|
| **RAW-20261118-000071** | **5.000000** | **50** |
| **RAW-20261118-000072** | **5.000000** | **50** |

25. Chọn **Flowchart**. Nguồn ở trái → drying → milling → **BLD-20261118-000079** → **STARTING CONTAINER mới** ở phải. Chỉ một node container trong backward này; không kéo container đã chạy thử **CTN-20261118-000025** vào graph của container mới. **Focus starting container**, **Fit graph**, chọn **BLD-20261118-000079** để xem lot detail.
26. Đối chiếu các đại lượng: lot **BLD-20261118-000079** **historical created 20 kg**, whole-lot source composition **10+10 kg**; actual container mới chỉ **10 kg**, nguồn **5+5 kg**. Sau buổi demo physical lot còn 0 nhưng historical created vẫn 20. Chuyển Table/Flowchart hoặc chọn node không tạo thêm loading hoặc chạy lại trace API.

## 9. CM — backward từ lot và forward từ source

27. CM mở **Ledger → Traceability**. Trong **Find a tracing starting point**, thao tác đúng từng trường:

| Trường trên web | Giá trị cho backward từ lot thành phẩm |
|---|---|
| Starting point type | **All recorded lots, including history** |
| Trace facility | **All permitted facilities** |
| Trace silo / location | **All permitted locations** |
| Search Starting lot | Nhập mã **BLD-20261118-000079** |
| Starting lot | Đợi danh sách tải, chọn option bắt đầu bằng **BLD-20261118-000079 · Processing output · BLENDED_RICE**, tại **Demo Container Blend Store** |
| Trace direction | **Backward — contributing sources** |
| Trace view | **Current — effective operations** |

Sau khi chọn **Starting lot**, giữ nguyên ô **Ledger lot ID** được tự điền. Bấm **Run trace**, đợi kết quả rồi chọn **Flowchart**. Nếu chưa thấy lô, kiểm lại **Starting point type**, hai bộ lọc và mã tìm kiếm; không chuyển sang nhập ID thủ công.

28. Trình bày ancestry gồm 7 lots; backward từ lot không có container downstream. Chọn node **BLD-20261118-000079** để xem phần **Source composition — BLD-20261118-000079**. Mở **View field / season origin — PRC-20261118-0007** để thấy **PLT-0001-01**, giống **ST25** và season record. Bấm **View samples and laboratory evidence** của nguồn để xem **SMP-20261118-0018** và **SMP-20261118-0019**, report synthetic đã confirm. Hai source lots cùng procurement/plot không đồng nghĩa hai thửa hoặc hai nông dân.

29. Vẫn ở **Ledger → Traceability**, dùng lại **Find a tracing starting point**, không sửa trực tiếp ô **Ledger lot ID**:

| Trường trên web | Giá trị cho forward từ nguồn |
|---|---|
| Starting point type | **Procurement sources** |
| Trace facility | **All permitted facilities** |
| Trace silo / location | **All permitted locations** |
| Search Starting lot | Thay mã tìm kiếm bằng **RAW-20261118-000071** |
| Starting lot | Đợi danh sách tải, chọn option bắt đầu bằng **RAW-20261118-000071 · Procurement source · RAW_PADDY**, tại **Demo Container Receiving Store** |
| Trace direction | **Forward — recorded descendants** |
| Trace view | **Current — effective operations** |

Bấm **Run trace**. Bảng **Actual container targets** sẽ có **mã container mới bạn vừa tạo, 10.000000 kg** và **CTN-20261118-000025, 10.000000 kg**. CTN-20261118-000025 đã dùng 10 kg đầu trong lượt chạy thử; đây là hai container khác nhau, không phải loading bị nhân đôi.

30. Chọn **Flowchart**: các container là đích ở phải, cạnh từ **BLD-20261118-000079** tới mỗi container là **10.000000 kg loaded**. Bấm **đúng node mã container mới vừa trình bày** để mở hồ sơ, rồi CM bấm **Trace container sources** để khép vòng **container → source → container**. Forward có thể còn nhánh **BRK-20261118-000075** vì milling có byproduct; nhánh đó không có loading edge vào container này.

> “Linked loaded kg là actual loading của lot trên đường truy xuất. Nguồn A đóng góp5 kg vào từng container 10 kg ở đây; xem attribution đó trong backward của từng container. Tỷ lệ này là phân bổ kế toán khối lượng từ lineage đã commit, không nhận diện từng hạt gạo.”

## 10. Các mã cần đối chiếu — không nhập UUID

| Đối tượng | Tên/mã dùng trên web |
|---|---|
| Lot thành phẩm | **BLD-20261118-000079** |
| Facility | **GrainLedger Demo Container Test Mill (synthetic)** |
| Location của lot thành phẩm | **Demo Container Blend Store** |
| Source thứ nhất / thứ hai | **RAW-20261118-000071 / RAW-20261118-000072** |
| Procurement | **PRC-20261118-0007** |
| Sample của RAW-20261118-000071 | **SMP-20261118-0018** |
| Sample của RAW-20261118-000072 | **SMP-20261118-0019** |
| Lab report | **LAB-DEMO-CONTAINER-20261118-02** |
| Container đã dùng chạy thử, chỉ mở để đọc | [**CTN-20261118-000025**](http://localhost:5173/export/containers/bf38e257-b045-49a9-8b4b-4946dc7b7415), trạng thái **CLOSED** |
| Container dùng trình bày | **Mã CTN mới do hệ thống sinh ở bước 6**, dùng xuyên suốt các bước sau; không thay bằng CTN-20261118-000025 |

Không đặt HOLD, đổi evidence/MRL/policy, revoke, hoặc dùng stock của lot này cho luồng khác trước buổi trình bày. Không seed/refill/reset container cũ. Luồng này chỉ có **một lượt loading mới 10 kg** còn sẵn, còn tracing/dossier đã commit có thể đọc lại nhiều lần.

[Báo cáo chuẩn bị và chạy thử](../reports/container-demo-v2-preparation-2026-11-18.md) ghi rõ những bước đã chạy và giới hạn hạ tầng; không cam kết không thể phát sinh lỗi do DB/quyền/dữ liệu dùng chung thay đổi sau thời điểm kiểm tra.
