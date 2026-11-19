# Báo cáo hướng dẫn test web toàn bộ luồng container hiện có

Ngày: **16/11/2026**. Nhánh: `feat/container`, nền container `be34fde`, đã nhận code từ `origin/dev` đến `7b8a4a2` trong working tree; merge chưa được commit tại thời điểm viết.

**Trạng thái tài liệu: test specification/manual checklist, chưa chạy các ca web trong báo cáo này.** Tất cả ca bên dưới bắt đầu ở `NOT RUN`. Kết quả automation hoặc `/health` thành công không thay thế bằng chứng đã thao tác web và đối chiếu số liệu.

Đã có [hướng dẫn ngắn](../guides/container-execution-web-test.md). Báo cáo này bổ sung dữ liệu đầu vào, đường đi trên giao diện, kết quả từng bước, test lỗi, cách ghi bằng chứng và giới hạn chức năng. Đối chiếu với [API hiện hành](../api/container-execution-api.md), [plan container](../plans/container-execution-plan-2026-11-13.md), code giao diện và kiểm thử tích hợp ở cuối tài liệu.

Nếu cần hiểu luồng chính từ export order tới đóng container và tracing trước khi test, đọc [luồng container end-to-end và phân công phối hợp](container-end-to-end-business-flow-2026-11-16.md).

## 1. Hiểu luồng trước khi test

Luồng chuẩn đang mở:

```text
Thành phẩm đã có trong Ledger
  → Export order + planned container target
  → Calculate plan → Save proposal
  → Customer Manager approve: RESERVE tồn
  → Export Staff ghi ACTUAL LOADING: trừ tồn + tiêu thụ reservation
  → Kiểm đủ target/number/seal/current assessment/HOLD
  → CLOSE: khóa manifest + lưu closure dossier, KHÔNG trừ tồn lần hai
  → Đọc hồ sơ/lịch sử và trace nguồn ↔ container
```

| Khái niệm | Ý nghĩa khi test |
| --- | --- |
| Planned container / target | Container trong order, có mã nội bộ và khối lượng dự kiến. Chưa có nghĩa hàng đã được xếp. |
| Proposal / approval | Phương án do Export Staff đề xuất, Customer Manager duyệt. Approve giữ quota hàng cho phương án. |
| Reservation | Khối lượng được giữ; giảm available/free stock, chưa làm giảm physical stock. |
| Metadata | Số vỏ container và số seal. `Save container metadata` chỉ lưu thông tin, không ghi loading hoặc trừ gạo. |
| Actual loading | Xác nhận khối lượng thực xếp từ lot/location đã được duyệt; ghi manifest và trừ physical stock đồng thời với consume reservation. |
| CLOSE | Chốt container khi actual loaded bằng target và các kiểm tra còn hợp lệ. Không phải thời điểm trừ stock lần đầu. |
| Dossier | Snapshot JSON bất biến được tạo khi đóng. Thông tin hiển thị phụ thuộc quyền của người đọc. |
| Version | Số kiểm soát sửa đồng thời. UI tự gửi; không nhập tay và không nhầm với phiên bản quyết định duyệt. |
| DIRECT_STOCK | Hàng thành phẩm đã sản xuất xong, có sẵn tại vị trí được duyệt; có thể là gạo đã blend trước khi lập phương án. |
| BLEND_INPUT_STOCK | Phương án cần sản xuất blend từ nguyên liệu sau duyệt. Chưa được loading khi chưa có bàn giao actual output/reservation. |

### Phép đối chiếu bắt buộc

Với **một vị trí có đúng 10 kg, reserve đúng 10 kg, không có reservation khác**:

| Mốc | Physical stock tại vị trí | Reservation còn lại | Reservation consumed | Available/free stock | Actual loaded | Còn phải xếp |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Trước approve | 10 | 0 | 0 | 10 | 0 | 10 |
| Sau approve | 10 | 10 | 0 | 0 | 0 | 10 |
| Sau lưu metadata | 10 | 10 | 0 | 0 | 0 | 10 |
| Load 4 kg | 6 | 6 | 4 | 0 | 4 | 6 |
| Load thêm 6 kg | 0 | 0 | 10 | 0 | 10 | 0 |
| Sau CLOSE | 0 | 0 | 10 | 0 | 10 | 0 |

Available bằng 0 sau approve là đúng trong ví dụ này. Container vẫn được dùng reservation của chính approval đó. Không lấy original output/quantity when created làm physical stock hiện tại. Nếu stock ban đầu lớn hơn 10 kg, số dư khác bảng này nhưng lượng giảm do loading vẫn phải đúng actual kg.

## 2. Chuẩn bị môi trường và dữ liệu

### 2.1. Khởi động

Chạy tại thư mục dự án trong hai terminal:

```bash
dotnet run --project backend/src/GrainLedger.Api
```

```bash
npm run dev
```

Mở **http://localhost:5173**. Backend dùng **http://localhost:5080**; `/health` phải hoạt động. Nếu Vite dùng cổng khác, dùng URL terminal in ra.

Backend, frontend và database phải tương thích cùng phiên bản. Database cần các migration của bản build, bao gồm `20261113000000_ContainerPreparation`, `20261113000100_ContainerExecution`, cùng whole-flow residue profiles/retirement đã tích hợp từ dev. Runtime phải có bộ quyền hợp lệ. **Không chạy migration/seed/cấp quyền chỉ để làm theo test guide**; xử lý deployment theo [setup](../setup-and-deployment.md#runtime-grants-after-branch-or-deployment-changes).

Dùng database test cô lập hoặc môi trường được phép ghi. Các bước create/approve/load/HOLD/CLOSE thực sự ghi dữ liệu; trên Supabase chung cần được chủ môi trường cho phép. Không reset stock hay dùng owner connection để chạy API.

### 2.2. Tài khoản

| Persona | Thao tác dùng trong báo cáo | Điều kiện |
| --- | --- | --- |
| Export Staff công ty A | Tạo order/plan/proposal; metadata; loading; CLOSE; đọc dossier đã lọc thông tin | Active role, cùng công ty; các quyền đọc order, propose, `containers.write`, `residue.evaluate` tương ứng. |
| Customer Manager công ty A | Approve; chuẩn bị policy/quality; đặt container HOLD; backward trace; đọc dossier đầy đủ | Active role và các quyền `blend.approve`, `holds.manage`, trace, policy/quality tương ứng. |
| Người dùng công ty B | Kiểm tra không xem/sửa container A | Tài khoản hợp lệ nhưng khác công ty. |
| Mill Operator / Purchasing Staff | Kiểm tra không có quyền thực thi container | Không mượn quyền của Export Staff. |
| Administrator | Kiểm tra quyền trace toàn cục khi có permission tương ứng | Không mặc định có quyền approve/load/CLOSE của business role. UI container không mặc định mở cho Admin. |

Nếu dùng account Development đã được provision, xem [danh sách tài khoản](../guides/development-accounts.md#4-đăng-nhập). Không giả định những account này đã tồn tại hoặc có mật khẩu mặc định. Không ghi mật khẩu/cookie/token vào report.

Dùng profile trình duyệt riêng cho Export và CM để tránh đổi session giữa hai vai trò. Hai tab cùng profile chia sẻ cookie nên không dùng chúng làm hai người khác nhau.

### 2.3. Fixture tối thiểu cho luồng thành công

Có thể dùng fixture đã chuẩn bị sẵn. Nếu chưa có, nhờ người phụ trách chuẩn bị và ghi lại các ID/mã dưới đây; tạo riêng order không tạo ra thành phẩm hoặc evidence.

| Thành phần | Dữ liệu cần có |
| --- | --- |
| Company/facility/location | Công ty A, facility được phép dùng, vị trí thường không mixed bulk/custody-derived. |
| Finished lot | Ví dụ `HEAD_RICE`, có đúng 10 kg free tại vị trí thường, source attribution đầy đủ; không HOLD hoặc restriction. |
| Nguồn và plot | Official procurement/receiving và nguồn Capture truy được tới plot/season, nếu muốn nghiệm thu backward trace đầy đủ. |
| Lab/residue | Confirmed report, substance coverage, reviewed source binding và whole-flow `FLOW` schema 2 profile/adoption phù hợp đường sản xuất và thời gian. Dữ liệu legacy không tự chuyển thành evidence mới. |
| Market/MRL | Market/commodity khớp order; một MRL version PUBLISHED đang áp dụng, đủ substances của assessment policy. |
| Assessment policy | Policy đúng công ty/market/commodity, effective, comparable convention `AS_RECEIVED`, required substances, reviewed margin và quality rules. |
| Quality evidence | Quan sát đã xác nhận đúng finished lot/attribute/unit, có evidence/report/reviewer/thời điểm hợp lệ. |
| Demand | Order 10 kg, một target 10 kg, product to ship khớp finished lot. |

Ví dụ **chỉ cho fixture synthetic**: quality attribute đơn vị `%`, kết quả đo `13`, order maximum `14`; market limit `1 mg/kg`, safety margin `0.1`, residue bounds đủ thấp để assessment PASS. Đây không phải ngưỡng/chính sách mặc định cho hàng thật. Không hạ ngưỡng kiểm tra hoặc chọn evidence sai để ép PASS.

### 2.4. Chuẩn bị policy và quality bằng web khi còn thiếu

Đăng nhập CM → **Export assessment**:

1. Trong **Market assessment policy**, chọn đúng market/commodity; dùng version kế tiếp theo dữ liệu hiện có.
2. Nhập reviewed effective time, margin và **Comparable residue convention → As received**; chọn required active substances, quality calculation rules và thông tin review/evidence.
3. Tick xác nhận và **Publish policy**. Chỉ làm trong fixture được phép; việc publish mới có thể làm approval đang dùng trở nên stale.
4. Trong **Finished-lot quality evidence**, chọn facility và đúng finished lot; chọn attribute, nhập measured value và measured time, upload supporting report synthetic, điền report reference/reviewer/authority/reviewed time.
5. Bảo đảm measured time ≤ reviewed time ≤ hiện tại; chọn thời điểm đã qua để tránh kiểm tra tương lai. Tick xác nhận → **Confirm quality observation**.
6. Xem bảng confirmed measurements đúng lot/attribute/version. Catalogue Quality attributes chỉ là danh mục, không thay thế observation này.

Phần lab/profile/MRL cần hoàn thành theo [lab guide](../guides/lab-flow-guide.md), [profile guide](../guides/residue-rules-entry-guide.md) và [residue manual tests](../guides/residue-bounds-manual-test-guide.md). Báo cáo này không tự đặt scientific policy cho team.

### 2.5. Chia fixture để không tự chặn các ca sau

| Fixture | Mục đích | Lưu ý |
| --- | --- | --- |
| A — chuẩn | 10 kg, một nguồn, partial load rồi CLOSE | Làm trước để hiểu đường chuẩn. |
| B — thiếu number/seal | Đủ hàng và approval, metadata để trống | Dùng để kiểm CLOSE bị chặn rồi bổ sung metadata. |
| C — HOLD | Container hoặc nguồn riêng | Không dùng lại A trước khi hoàn tất A; chưa có luồng release container HOLD. |
| D — stale evidence | Container riêng, dành cho publish quality/policy mới | Sau partial execution chưa có reviewed revalidation để tiếp tục. |
| E — nhiều nguồn | Lot thành phẩm 20 kg đã blend trước proposal, gồm hai nguồn mỗi nguồn 10 kg; reserve 10 kg | Một positive ordinary position và chứng minh được source complement. |
| F — các nhánh chưa hỗ trợ | Approval chọn blend-input hoặc pool/custody position | Kết quả mong đợi là bị chặn, không phải load thành công. |
| G — concurrency/retry | Container riêng, 10 kg | Cần hai profile/session phù hợp; không dùng container đã CLOSED. |

Ghi mã thật do server cấp; các chữ A–G là nhãn trong tài liệu, không phải ID có sẵn trong hệ thống.

## 3. Đường chuẩn: làm lần lượt CT-01 đến CT-10

Nếu đã có approved fixture, bắt đầu tại CT-04. Lần test đầu nên đi từ CT-01 để hiểu khác biệt giữa plan, reserve và actual loading.

### CT-01 — Tạo order và container target

**Vai trò:** Export Staff A. **Trạng thái:** NOT RUN.

1. Vào **Export orders → + Create export order**.
2. Chọn Export market/Commodity đúng fixture; Required mass `10`, Unit `KG`.
3. Chọn shipping window hợp lệ của đợt test; không sao chép ngày cũ cố định.
4. Trong **Quality specification**, chọn attribute của fixture và maximum `14` nếu dùng ví dụ synthetic ở trên.
5. Trong **Container targets**, để một dòng target mass `10`, Unit `KG` → **Create export order**.
6. Ghi order code/ID. Trong danh sách container targets, bấm **Open** và ghi container code/ID/URL.

**Mong đợi:** có planned container target 10 kg; **Loading and closure** hiển thị Actual loaded `0.000000`, Remaining to target `10.000000`, status `PLANNED`. Chưa có approval thì loading bị chặn; stock không đổi.

### CT-02 — Lập phương án DIRECT_STOCK

**Vai trò:** Export Staff A. **Trạng thái:** NOT RUN.

1. Trên container, tìm **Stock and blend plans**.
2. **Product to ship → Head rice** nếu fixture là HEAD_RICE → **+ Create draft**.
3. Trong workspace plan, chọn **Facility** đúng kho có thành phẩm.
4. Kiểm tra candidates: lot/location/product/available; dùng **Show other products**, **Show excluded stock**, **Sources** khi cần điều tra. Không coi raw paddy là thành phẩm chỉ vì đang có tồn.
5. Bấm **Calculate plan**. Xem **Selected stock**, mode **Direct stock**, đúng lot/location, tổng 10 kg và Assessment **PASS**.
6. Bấm **Save proposal** khi nút được mở. Preview có thời hạn; quá hạn thì calculate lại.
7. Ghi plan/proposal ID và URL workspace để CM mở.

**Mong đợi:** proposal được lưu, chưa reserve và chưa giảm physical stock. Nếu solver chọn lot khác vì có nhiều candidate hợp lệ, ghi lại selection thực tế và tính lại oracle; muốn số liệu đúng ví dụ phải chuẩn bị fixture không có candidate cạnh tranh.

Nếu assessment là INCOMPLETE/FAIL, dừng đường chuẩn và xử lý bảng blocker ở mục 7. Không mặc định có hàng là đủ điều kiện xuất.

### CT-03 — Duyệt và chứng minh chỉ reserve

**Vai trò:** CM A rồi Export Staff A. **Trạng thái:** NOT RUN.

1. CM mở workspace cùng plan/proposal, bấm **Refresh**; xem Saved proposal và **Proposal decision**.
2. Chỉ duyệt proposal còn current, selection đúng. Bấm **Approve proposal**, đọc xác nhận và bấm **Approve and reserve stock** trong form quyết định.
3. Xem Decision **Approved**, Reserved `10`, Still held `10`, Consumed `0`, Released `0`.
4. Mở Ledger của lot ở tab khác; refresh và ghi **physical stock** cùng **available stock**.
5. Export mở lại container hoặc bấm **Refresh container checks**.

**Mong đợi:** physical stock vẫn 10 kg; available có thể còn 0 vì cả 10 kg đã giữ. **Approved reserved stock** có reservation remaining 10; actual loaded vẫn 0, status vẫn PLANNED. Việc approve không tự tạo load.

### CT-04 — Lưu số container và seal

**Vai trò:** Export Staff A. **Trạng thái:** NOT RUN.

1. Mở **Export orders → order → container → Loading and closure**.
2. Trong **Physical container and seal**, nhập Container number `TEST-CONT-A-01`, Seal number `TEST-SEAL-A-01` hoặc mã synthetic riêng của đợt test.
3. Bấm **Save container metadata**. Refresh trang và kiểm tra giá trị còn lưu.
4. Bấm **Refresh container checks**.

**Mong đợi:** metadata thuộc chính planned container hiện tại; không sinh container khác, không có load mới, stock/reservation/loaded giữ nguyên. Khi đủ mọi điều kiện, **Loading checks: PASS**.

Biến thể validation: mỗi số container/seal tối đa 80 ký tự; không được lưu chuỗi vượt giới hạn khi gửi thẳng request. Trường trống được phép lưu ở giai đoạn chuẩn bị nhưng sẽ chặn CLOSE như CT-12.

### CT-05 — Actual loading lần đầu 4 kg

**Vai trò:** Export Staff A. **Trạng thái:** NOT RUN.

1. **Record actual loading → Approved position**: chọn lot đã duyệt.
2. **Actual loaded quantity (kg)** nhập `4` bằng dấu chấm nếu có phần thập phân.
3. Trước khi tick, kiểm tra nút commit chưa được mở. Tick **I confirm this is the actual loading to record.**
4. Bấm **Commit actual loading** một lần, đợi kết quả.
5. Xem **Committed manifest**; mở lot từ link trong manifest để đối chiếu physical stock, rồi xem quyết định proposal để đối chiếu reservation.

**Mong đợi:** status `LOADING`; actual loaded `4.000000`, remaining `6.000000`; manifest có đúng một lần load 4 kg. Physical stock còn 6; reservation remaining còn 6, consumed là 4. Không chỉ tạo draft hoặc chỉ giảm reservation.

### CT-06 — Refresh và tiếp tục load 6 kg

**Vai trò:** Export Staff A. **Trạng thái:** NOT RUN.

1. Reload trình duyệt: vẫn đúng 4 kg, không sinh load lần hai.
2. Chọn lại approved position; nhập `6`, tick xác nhận và commit.
3. Chờ UI tải lại; nếu trang còn hiển thị trạng thái cũ thì **Refresh container checks**.

**Mong đợi:** actual loaded `10.000000`, remaining `0.000000`, status vẫn `LOADING` cho tới khi close; hai lần load cộng đúng 10. Physical stock 0; reservation consumed 10 và remaining 0. Form load mới không còn khi đã đủ target; checks chuyển sang **Closure**.

### CT-07 — CLOSE và xác minh không trừ tồn lần hai

**Vai trò:** Export Staff A. **Trạng thái:** NOT RUN.

1. Ghi lại stock, consumed/remaining reservation và manifest ngay trước close.
2. **Refresh container checks** → **Closure checks: PASS**.
3. Bấm **Close container**. Lần đầu chọn Cancel trên hộp xác nhận: status và dữ liệu phải không đổi.
4. Bấm lại, xác nhận **Close this container and freeze its manifest?**
5. Refresh container, lot và proposal decision.

**Mong đợi:** status `CLOSED`; actual loaded vẫn 10, manifest vẫn đúng hai lần. Stock và reservation **bằng ngay trước close**, không có lần trừ 10 kg mới. Không còn form metadata/load thông thường; closure dossier xuất hiện. Quyền revoke approval không được mở lại sau execution.

### CT-08 — Đọc và tải closure dossier theo vai trò

**Vai trò:** Export Staff A và CM A. **Trạng thái:** NOT RUN.

1. Export bấm **Read closure dossier → Download dossier JSON**.
2. Kiểm tra file chứa container/manifest/khối lượng thực tế và thông tin chốt; không có source composition/evidence hoặc assessment chi tiết từng load thuộc view CM.
3. CM mở cùng container, đọc và tải dossier. Kiểm tra bản đầy đủ có nguồn/assessment references theo contract, tổng actual kg đúng 10 và các physical identifiers đã nhập.
4. Refresh/đọc lại: snapshot đóng phải giữ nguyên. Không so hai file Export/CM như thể chúng phải giống nhau; đó là hai view quyền khác nhau.

**Mong đợi:** hồ sơ lấy từ snapshot khi close; không lấy stock hiện tại thay khối lượng đã xếp. JSON này chưa phải PDF/Excel hoặc chứng thư pháp lý được duyệt.

### CT-09 — Đọc history

**Vai trò:** Export Staff A hoặc CM A. **Trạng thái:** NOT RUN.

1. Bấm **Read container history**; đối chiếu metadata, hai load và close của fixture A.
2. Nếu có nhiều hơn 20 event, dùng **Next history page / Previous history page**. Nếu ít hơn, nút Next phải bị khóa; không cần tạo hàng loạt loading chỉ để đủ trang.
3. Ghi event kind/thời điểm; kiểm event không trùng do refresh/retry.

**Mong đợi:** UI history hiện **kind + time**. Khi cần actor/request chi tiết, kiểm response History trong Network hoặc dossier; UI danh sách hiện tại không hiển thị mọi trường audit. Thao tác trace có audit riêng, không mặc định phải xuất hiện như một event loading trong history này.

### CT-10 — Trace từ container và từ nguồn

**Vai trò:** CM A; Export Staff A dùng để kiểm giới hạn đọc. **Trạng thái:** NOT RUN.

1. CM bấm **Trace container sources**. Xem bảng **Actual container sources**.
2. Fixture một nguồn: actual source kg 10; share 100%; có plot/provenance nếu nguồn Capture đầy đủ.
3. CM mở **Ledger → Traceability**, chọn lot nguồn tương ứng → trace **forward**. Xem **Actual container targets** có đúng container và khối lượng load liên quan.
4. Mở lot thành phẩm từ manifest để đối chiếu lineage. Current stock bằng 0 sau load không xóa lô hoặc lịch sử.
5. Export mở container: không có nút backward nguồn dành cho CM; có thể dùng forward trace theo quyền của mình, với source detail được redaction.

**Mong đợi:** chỉ actual loading tạo liên kết vật chất tới container. `linkedLoadedKg` trong forward là lượng load từ reachable lots, **không mặc định là tỷ lệ riêng của nguồn đang chọn** nếu lot có nhiều nguồn. Dùng backward container để xem source-attributed kg. Trace không thay stock/reservation hoặc tự tạo HOLD.

## 4. Test lỗi và những kiểm soát bắt buộc

Mỗi ca dưới đây dùng fixture riêng hoặc bắt đầu đúng precondition. Với mọi request bị từ chối, đối chiếu loaded, physical stock, reservation và manifest trước/sau; không chỉ nhìn thông báo lỗi.

### CT-11 — Chưa approve, proposal bị reject hoặc approval đã revoke

**Fixture:** chưa loading. **Vai trò:** Export + CM. **Trạng thái:** NOT RUN.

1. Mở container chỉ có draft/proposal chưa duyệt; refresh checks: không được load, thường có `ACTIVE_APPROVAL_REQUIRED`.
2. Dùng fixture khác, CM **Reject proposal** kèm lý do; Export không dùng proposal đó để load.
3. Dùng approval hợp lệ nhưng chưa execution, CM **Revoke approval** kèm lý do; kiểm quota được release theo F15, physical stock không bị trừ; Export refresh thấy không còn active approval để load.

**Mong đợi:** cả ba trạng thái không cho load. Revoke trước execution không phải workflow unload hoặc release container HOLD. Khi đã load một phần, việc chưa có nút revoke là đúng phạm vi hiện tại.

### CT-12 — Chưa đủ target và thiếu container number/seal

**Fixture:** B. **Vai trò:** Export. **Trạng thái:** NOT RUN.

1. Chỉ load 4/10 kg: chưa được CLOSE; nút close có thể disabled trong phase LOADING. Kiểm backend close sớm ở mục 6 nếu cần chứng minh không bypass được UI.
2. Load đủ 10 kg nhưng chưa lưu number/seal: Closure checks không PASS; thấy `SEAL_METADATA_MISSING`.
3. Chỉ nhập container number, seal còn trống: vẫn bị chặn. Nhập đủ và save, refresh.

**Mong đợi:** khi các điều kiện khác hợp lệ, nhập đủ metadata mới mở CLOSE. Thiếu target có thể thấy `EXACT_TARGET_NOT_LOADED` ở kiểm tra CLOSE/API; UI bình thường vẫn đang hiển thị phase LOADING nên không nhất thiết hiện code này.

### CT-13 — Vượt target hoặc quota của vị trí

**Fixture:** approved 10 kg, chưa load. **Vai trò:** Export. **Trạng thái:** NOT RUN.

1. Nhập `11`, tick, commit: backend từ chối.
2. Load hợp lệ 4; sau đó thử `7` trong khi chỉ còn 6: từ chối, loaded vẫn 4, stock/reservation vẫn còn 6.
3. Nếu approval có nhiều positions, thử kg lớn hơn quota của position đã chọn dù tổng container còn đủ: vẫn từ chối. Form hiện tại chọn **một position mỗi request**; muốn kiểm nhiều lines trong một request dùng ca API bổ sung.

**Mong đợi:** không rút hàng từ vị trí không được duyệt hoặc mượn quota của position khác; không ghi manifest một phần khi validation fail.

### CT-14 — Format và độ chính xác khối lượng; xác nhận thực xếp

**Fixture:** G chưa load. **Vai trò:** Export. **Trạng thái:** NOT RUN.

| Dữ liệu / thao tác | Mong đợi |
| --- | --- |
| Để trống quantity hoặc chưa chọn position | Không gửi load hợp lệ. |
| Nhập `4` nhưng chưa tick xác nhận | Commit không được mở. |
| Tick rồi đổi quantity hoặc position | Checkbox xác nhận bị reset; phải xác nhận lại. |
| `-1`, `abc`, `1,5`, `1e3`, `0.0000001` | Browser pattern hoặc backend từ chối; không phát sinh load. Dùng `1.5` cho 1,5 kg. |
| `0` hoặc `0.000000` | Backend từ chối vì không phải lượng dương; pattern chữ số riêng không chứng minh hợp lệ. |
| `4.000001` | Được giữ chính xác sáu chữ số khi đủ quota/checks; không làm tròn thành 4. |

Browser chặn không gửi request và server trả 400 là hai bằng chứng khác nhau. Không ghi đã kiểm backend nếu mới chỉ thấy browser validation.

### CT-15 — Container HOLD trước hoặc giữa loading

**Fixture:** C riêng. **Vai trò:** CM + Export. **Trạng thái:** NOT RUN.

1. CM mở container → **Place container HOLD**, nhập lý do synthetic rõ ràng → **Place container HOLD**.
2. Export **Refresh container checks**: thấy banner **Container HOLD** và blocker `CONTAINER_HOLD`; không được load/CLOSE.
3. Lặp trên container khác đã load 4/10 kg: đặt HOLD rồi thử tiếp 6; actual vẫn 4, stock/quota còn 6.
4. Export không được có form đặt HOLD dành cho CM; kiểm backend quyền ở mục 6.

**Mong đợi:** HOLD không làm mất lịch sử hoặc đảo load 4 kg cũ. Chưa có workflow release container HOLD; không sửa DB để xóa HOLD và tiếp tục ca chuẩn.

Biến thể validation: reason bắt buộc, không chỉ khoảng trắng và tối đa 1.000 ký tự. Form/endpoint phải từ chối reason không hợp lệ; không tạo HOLD từ request bị từ chối.

### CT-16 — Source/lot HOLD phát sinh sau partial loading

**Fixture:** C khác, đã load 4/10 kg. **Vai trò:** CM + Export. **Trạng thái:** NOT RUN.

1. CM dùng chức năng HOLD của Ledger, đặt HOLD lên lot thành phẩm hoặc nguồn đóng góp dương của lot đã chọn, đúng scope và lý do.
2. Export refresh container, thử load tiếp 6 hoặc CLOSE khi đủ hàng ở fixture tương ứng.
3. Đối chiếu manifest và reservation.

**Mong đợi:** thấy `LOT_RESTRICTED` hoặc lý do restriction tương ứng; continuation/CLOSE bị chặn. Loaded cũ không đổi. Có thể **không có bản ghi container HOLD mới** vì nguồn bị chặn không tự tạo HOLD trên container; không coi thiếu banner container HOLD là bỏ sót kiểm tra nguồn.

### CT-17 — Evidence thay đổi sau approve, trước loading

**Fixture:** D chưa load. **Vai trò:** CM + Export. **Trạng thái:** NOT RUN.

1. Sau approval, CM vào **Export assessment → Finished-lot quality evidence**, thêm observation version mới, dùng evidence synthetic đúng lot.
2. Trường hợp đầu dùng giá trị vượt maximum của fixture, ví dụ `15` so với max `14`.
3. Export refresh: phải bị chặn với `QUALITY_LIMIT` và/hoặc finding về evidence/freshness tùy kết quả assessment.
4. Với fixture độc lập, thêm observation mới vẫn trong giới hạn để kiểm fingerprint thay đổi: approval cũ không tự được xem là approval của evidence mới; có thể thấy `APPROVAL_EVIDENCE_CHANGED`.

**Mong đợi:** không load bằng căn cứ đã thay đổi chỉ vì trước đó UI từng PASS. Trước execution, chuẩn bị/review lại proposal theo F14/F15 được hỗ trợ; không chỉnh ID/fingerprint thủ công.

### CT-18 — Evidence thay đổi khi đã load một phần; MRL/policy mất hiệu lực

**Fixture:** D khác, đã load 4/10 kg. **Vai trò:** CM + Export. **Trạng thái:** NOT RUN.

1. Thêm reviewed quality observation mới hoặc dùng fixture có approved dependency bị thay đổi hợp lệ; refresh container.
2. Thử load phần còn lại; kiểm actual vẫn 4 và stock/quota không đổi khi bị từ chối.
3. Trên fixture riêng, sau khi đủ target nhưng trước close, làm current evidence thay đổi: CLOSE cũng phải bị chặn.
4. Các ca MRL/policy effective-time boundary phải dùng dữ liệu test đã lên lịch; chỉ thay một yếu tố mỗi ca và ghi thời gian trước/sau. Không publish policy thật hoặc chờ giờ tùy ý rồi kết luận đã bao phủ race ở commit.

**Mong đợi:** missing/expired/changed mandatory inputs không thành PASS. **Reviewed revalidation sau partial execution chưa có**, nên đây là test block đúng; không yêu cầu hệ thống tự thay approval và tiếp tục load.

### CT-19 — Dữ liệu lab/MRL/quality/policy thiếu

**Fixture:** từng fixture thiếu một đầu vào, chưa approve. **Vai trò:** Export + người phụ trách dữ liệu. **Trạng thái:** NOT RUN.

1. Trong plan workspace bấm **Calculate plan**.
2. Lần lượt kiểm thiếu assessment policy, applicable MRL, required lab/profile/evidence coverage hoặc confirmed quality observation.
3. Xem **Checks and evidence → Next step** và container readiness nếu đã có approval cũ không còn dùng được.

**Mong đợi:** INCOMPLETE/FAIL tương ứng, không có approval/load/CLOSE thành công dựa vào dữ liệu thiếu. Catalogue attribute, MRL draft hoặc report chưa confirmed không thay thế dữ liệu thực cần thiết. Các code thường gặp xem mục 7.

### CT-20 — Sau CLOSE: khóa thao tác nhưng vẫn đọc và đặt HOLD được

**Fixture:** A đã CLOSED. **Vai trò:** Export + CM. **Trạng thái:** NOT RUN.

1. Export reload: không có form load/metadata; không mở lại container hoặc đổi target bằng thao tác thông thường.
2. Đọc history/dossier/trace vẫn theo quyền.
3. CM lưu bản dossier trước, sau đó đặt container HOLD với lý do phát hiện muộn; refresh và đọc lại dossier.

**Mong đợi:** current detail có HOLD mới; status vẫn CLOSED; dossier chốt cũ không bị viết lại. Không có nghiệp vụ unload/reopen/reseal/cancel loaded container tự động. Kiểm API từ chối load/metadata sau CLOSED tại mục 6.

## 5. Retry, concurrent sessions, nhiều nguồn và phân quyền

### CT-21 — Mất mạng trước request và retry nguyên thao tác

**Fixture:** G riêng. **Vai trò:** Export. **Trạng thái:** NOT RUN.

1. Mở DevTools → Network; bật Preserve log. Chuẩn bị loading 4 kg, đã tick.
2. Chuyển Network sang **Offline** trước khi commit, rồi commit.
3. Kiểm thông báo **A previous operation has an uncertain result** và nút **Retry container operation**; form thay đổi/refresh thao tác bị khóa để tránh tạo request khác.
4. Chuyển về **Online**, bấm retry. Có thể reload cùng tab trước retry để kiểm request còn trong sessionStorage.
5. So payload request gốc và retry nếu DevTools thu được: cùng operationId, version, proposal/decision, loadedAt, quantity; actual cuối chỉ 4 kg.

**Mong đợi:** một load, không double debit. Đừng xóa sessionStorage hoặc đóng mất tab pending để làm nút hết khóa. Storage được tách theo user và container; không chép pending request sang người/container khác.

**Giới hạn:** Offline trước request chứng minh retry khi chưa ghi, chưa chứng minh trường hợp server đã commit nhưng response bị mất. Muốn xác nhận nhánh đó cần fault injection trên môi trường cô lập hoặc test tích hợp ở mục 6, không giả định mọi network failure đều đã commit.

### CT-22 — Server đã ghi nhưng response bị mất; changed replay

**Phương thức:** nâng cao, không thể tạo chắc chắn chỉ bằng nút web thông thường. **Trạng thái:** NOT RUN.

1. Trên môi trường cô lập, dùng công cụ fault injection/test harness để bỏ response sau khi server chấp nhận load; giữ nguyên pending request trong UI.
2. Bấm **Retry container operation**; kiểm receipt/manifest, stock và reservation chỉ có một lần thực thi.
3. Gửi lại chính request đã thành công với operationId và payload giữ nguyên: nhận receipt cũ, không tạo load khác. Đổi quantity nhưng giữ operationId: phải conflict.

Đổi mạng ngay sau click không phải bằng chứng chắc chắn server đã commit. Automation đã có coverage replay; manual test chỉ ghi PASS khi có bằng chứng đúng điểm lỗi. Không giả lập bằng cách sửa dữ liệu/receipt trong database chung.

### CT-23 — Hai session dùng version cũ

**Fixture:** G mới, 10 kg. **Vai trò:** hai session Export hợp lệ cùng công ty. **Trạng thái:** NOT RUN.

1. Hai profile cùng mở container và thấy cùng trạng thái/version hiện tại.
2. Session A chuẩn bị load 4; session B chuẩn bị load 7, chưa refresh.
3. A commit thành công; B commit với dữ liệu cũ.
4. B phải thấy conflict và reload; đối chiếu loaded/stock/reservation theo request thành công.

**Mong đợi:** B không ghi bằng version cũ; không thành 11 kg. Nếu muốn test cạnh tranh đúng đồng thời, gửi qua harness/HTTP và ghi hai kết quả; thao tác liên tiếp chỉ chứng minh stale-version protection. Trường hợp metadata/HOLD làm version tăng cũng yêu cầu session cũ refresh.

### CT-24 — Thành phẩm đã blend trước proposal, nhiều nguồn và micro-kg

**Fixture:** E, physical 20 kg, nguồn S1/S2 mỗi nguồn 10 kg, target/reserve 10 kg. **Vai trò:** Export + CM. **Trạng thái:** NOT RUN.

1. Lập proposal **Product to ship → Blended rice**. Xác minh selection mode là **Direct stock**, không phải Blend input stock; hàng đã được blend vật lý trước khi lập phương án.
2. Approve 10 kg. Export load `4.000001`, rồi `5.999999`.
3. CM **Trace container sources**: có hai nguồn; tổng source kg chính xác `10.000000`; fixture cân bằng này mỗi nguồn cuối cùng `5.000000`, share 50%.
4. Ledger finished lot còn physical 10 kg; reservation 10 kg đã consumed. Original quantity created vẫn 20 kg là đúng.
5. Close nếu đủ metadata/current checks. Stock vẫn 10 kg sau close.

**Mong đợi:** allocations giữ đúng micro-kg và source complement qua nhiều load; không cộng tỷ lệ bằng JavaScript float. Sau load đầu, một nguồn có thể nhận thêm 0.000001 kg do quy tắc largest remainder/UUID tie-break; không ép cả hai dòng bằng số thập phân vô hạn. Số kg là căn cứ accounting, phần trăm là số hiển thị đã làm tròn.

### CT-25 — Trace chưa có actual hoặc thiếu plot provenance

**Fixture:** container có proposal/approval nhưng loaded 0; fixture khác có actual nhưng nguồn thiếu Capture provenance. **Vai trò:** CM. **Trạng thái:** NOT RUN.

1. Trước loading, bấm **Trace container sources**: không tạo source kg từ target/reservation; cảnh báo incomplete. Response có `NO_ACTUAL_LOADING`.
2. Trace forward từ nguồn trước actual loading: container mới chỉ dự kiến không được xuất hiện như actual target của nó.
3. Với actual có lineage nguồn nhưng thiếu original Capture plot, xem incomplete/provenance status; không được tự invent plot hoặc báo complete.

**Mong đợi:** UI hiện cảnh báo **Source provenance is incomplete; review the findings.** Một số finding chi tiết chỉ có trong Network response, UI hiện tại không render toàn bộ mảng findings. Partial loading 4 kg thì source total phải là 4, không phải target 10.

### CT-26 — Phân quyền, company scope và thông tin bị ẩn

**Fixture:** container A đã load/close và account B. **Trạng thái:** NOT RUN.

| Người dùng | Thao tác web | Mong đợi |
| --- | --- | --- |
| Export A | Container A | Có thao tác metadata/load/CLOSE khi điều kiện hợp lệ; không có form CM HOLD/backward sources. |
| CM A | Container A | Có HOLD và Trace container sources; không có form Export thực xếp hoặc đóng. |
| Export/CM B | Dán URL container A đã ghi ở CT-01 | Không đọc được container/manifest/dossier/nguồn công ty A; direct request tương ứng bị 403. Không chỉ kiểm danh sách B không có mã A. |
| Purchasing/Mill Operator | Dán URL A hoặc tìm menu Export | Không có nghiệp vụ thực thi container. UI có thể không mount workspace; kiểm direct API 403 để chứng minh backend. |
| Export A so với CM A | Read closure dossier | Export được redaction source/evidence, CM được bản đầy đủ theo quyền; không dùng UI ẩn nút làm bằng chứng dữ liệu đã lọc. |
| Administrator | Đọc trace bằng quyền trace được cấp | Không suy ra có quyền approve/load/CLOSE; quyền nghiệp vụ cần kiểm riêng. |

Nếu quyền/active role vừa đổi, refresh hoặc đăng nhập lại theo session flow. Backend vẫn phải chặn request không còn quyền; ca thu hồi role trong khi đang chờ lock thuộc automation ở mục 6.

## 6. Nhánh bị chặn và kiểm thử bổ sung ngoài thao tác web thông thường

### CT-27 — BLEND_INPUT_STOCK cần đầu ra sản xuất sau duyệt

**Fixture:** F có approved selection mode Blend input stock. **Trạng thái:** NOT RUN.

1. Mở container, refresh checks.
2. Kiểm `BLEND_OUTPUT_HANDOFF_REQUIRED` và thông điệp cần actual blend execution/reserved finished outputs.
3. Kiểm không được dùng nguyên liệu đầu vào để load giả thành phẩm hoặc tự cho Export Staff quyền sản xuất.

**PASS của ca này là chặn đúng.** Đường sản xuất blend sau duyệt và bàn giao output reservation chưa hoàn chỉnh; không ghi luồng ấy đã test thành công. Nếu chưa tạo được approved fixture ở upstream, ghi BLOCKED tại upstream, không ghi PASS cho container.

### CT-28 — Mixed pool/custody position hoặc source complement không chứng minh được

**Fixture:** F riêng, có approved position thuộc nhánh chưa hỗ trợ. **Trạng thái:** NOT RUN.

1. Mở container, nhìn **Approved reserved stock → Restriction** và refresh checks.
2. Khi handoff chưa đủ, thấy **Handoff needed** / `POSITION_OR_SOURCE_HANDOFF_REQUIRED` hoặc lý do reconciliation phù hợp.
3. Không cho loading bằng cách thay lot/location sang successor pool hoặc vị trí khác ngoài approval.

**Mong đợi:** không có load/consume/posting giả. Pool là current và có stock không đủ để chứng minh container đã có reservation/source handoff. Không chuyển hàng trong DB chung để cố tái hiện ca này; dùng fixture chuẩn bị trước trong test env.

### CT-29 — Legacy Ledger → Containers là draft preparation

**Trạng thái:** NOT RUN.

1. Nếu role được phép, mở **Ledger → Containers** và đọc hướng dẫn trên màn hình.
2. Lưu metadata/loading draft của legacy preparation theo contract riêng nếu cần regression.
3. Quay lại planned container trong Export orders: actual loaded/manifest không được tự tăng từ draft legacy.

**Mong đợi:** actual loading/CLOSE thật thực hiện trong **Export orders → planned container → Loading and closure**. Nút legacy **Commit loading/Close container** vẫn không phải đường thực thi đã tích hợp. Không match hai container chỉ vì cùng text code/order reference.

### CT-30 — Các kiểm tra API/harness bổ sung bắt buộc khi nghiệm thu kỹ thuật

Đây là các ca không thể chứng minh đầy đủ chỉ bằng form hiện tại. Tester kỹ thuật có thể dùng Network để quan sát response; việc chỉnh/gửi request phải trên môi trường test được phép, giữ cơ chế auth/CSRF thực. Không đưa cookie/token/secret vào bằng chứng.

| Ca bổ sung | Cách kiểm | Mong đợi |
| --- | --- | --- |
| Close sớm; load/metadata mới sau CLOSED | Gửi request mới với operationId/version đúng; phân biệt với replay request cũ hợp lệ | Domain conflict, không có stock/manifest change. |
| Same-operation replay / changed replay | Cùng actor, operationId và payload; sau đó đổi payload | Lần giống trả receipt cũ; lần đổi conflict, không double consume. |
| DTO/mass boundaries | Numeric JSON token thay decimal string; hơn 6 decimals; unknown `approved`/`ready`/actor; duplicate positions; lot/location ngoài approval | 400 hoặc domain refusal đúng loại; không có quyền/stock mới từ payload. UI một position không chứng minh hết các ca này. |
| Quyền trực tiếp | Export POST HOLD/backward trace; foreign-company đọc/load/close/history/dossier | 403; không lộ nội dung container A. UI hidden-only chưa đủ. |
| History bounds | `history?pageSize=101` | 400; UI dùng pageSize 20. |
| Atomic rollback | Disposable PostgreSQL + fault injection sau consume/trước posting hoàn tất | Stock, reservation, actual load, status cùng rollback; không tạo trigger trên shared DB. |
| Role bị thu hồi trong lúc chờ stock lock | Integration harness khóa stock writer, gửi load, thu hồi role, nhả lock | 403 và không ghi; manual hai tab không chứng minh timing này. |
| Private helper permissions | Runtime role gọi trực tiếp allocation/helper/table | Bị từ chối; public API vẫn hoạt động. Không cấp helper EXECUTE để làm test xanh. |
| Allocation lớn UT-14 | Harness: load 25,000 kg từ 70,000 kg gồm source L3 11,600 kg | L3 `4142.857143` kg, phần còn lại `20857.142857` kg; tổng đúng 25,000. Không cần tạo khối lượng lớn trên web dùng chung. |

Trạng thái của từng dòng: **NOT RUN trong đợt manual này**. Coverage automation đã có không đồng nghĩa mọi biến thể ở bảng đều đã được automated; đối chiếu test method cụ thể bên dưới trước khi ghi kết quả.

## 7. Tra cứu khi nút bị khóa hoặc thấy lỗi

| Hiện tượng / code | Ý nghĩa | Việc cần làm |
| --- | --- | --- |
| Runtime grant missing / unsafe runtime role lúc startup | Code, installed objects hoặc grants không khớp | Xem [chẩn đoán runtime](runtime-startup-diagnosis-2026-11-16.md); dùng quy trình owner đã duyệt, không dùng owner để chạy app. Restart máy không tự thu hồi grants. |
| Feature not available / 503 | Migration/feature deployment chưa có hoặc code không khớp | Nhờ người phụ trách deployment kiểm; không coi là lỗi nhập form. |
| Không có nút load | Sai role/permission; CLOSED; đủ target; readiness chưa PASS hoặc đang pending | Xem role, status, checks và pending request. |
| `ACTIVE_APPROVAL_REQUIRED` | Chưa có active approval hợp lệ | CM review/approve proposal current. |
| `ASSESSMENT_POLICY_MISSING` | Thiếu policy đúng scope | CM publish reviewed market assessment policy. |
| `APPLICABLE_MRL_MISSING` | Không có MRL version áp dụng | Người phụ trách MRL kiểm published/effective market/commodity/version. |
| `QUALITY_EVIDENCE_MISSING` / `QUALITY_RULE_MISSING` | Thiếu đo thực tế hoặc quality propagation rule | Hoàn thiện observation/rule theo reviewed evidence. |
| `RESIDUE_COVERAGE_INCOMPLETE` | Lab/source binding/whole-flow profile/adoption/substance/time coverage chưa đủ | Xem lab/profile và evidence findings; report confirmed riêng chưa đủ. |
| `QUALITY_LIMIT` / `RESIDUE_LIMIT` | Assessment không đạt giới hạn | Xem measured/bounds và policy/order scope; không bỏ qua. |
| `APPROVAL_EVIDENCE_CHANGED` | Căn cứ hiện tại khác căn cứ đã duyệt | Review lại trước execution; partial execution đang chờ workflow revalidation. |
| `CONTAINER_HOLD` / `LOT_RESTRICTED` | Container hoặc hàng/nguồn đang bị hạn chế | Xem current restriction và actor xử lý; không tự release bằng trace hoặc lab confirmation. |
| `SEAL_METADATA_MISSING` | Thiếu number/seal khi CLOSE | Save đủ metadata, refresh checks. |
| `EXACT_TARGET_NOT_LOADED` | Actual chưa bằng target khi kiểm CLOSE | Đối chiếu actual manifest, không sửa target để hợp thức hóa hàng đã xếp. |
| `TARGET_ALREADY_LOADED` | Đã đủ target | Review closure checks; không load thêm. |
| Version changed / 409 | Session dùng version cũ hoặc dữ liệu/checks đã đổi | Refresh rồi kiểm lại; không retry như một request mới nếu outcome trước còn uncertain. |
| `BLEND_OUTPUT_HANDOFF_REQUIRED` | Cần actual output/reservation từ blend workflow | Chờ handoff upstream, không load nguyên liệu thay đầu ra. |
| `POSITION_OR_SOURCE_HANDOFF_REQUIRED` | Position/source complement hoặc pool/custody reservation chưa chứng minh được | Cần contract/reconciliation; không tự đổi lot trong request. |
| `NO_ACTUAL_LOADING` / incomplete trace | Chưa có load hoặc thiếu provenance | Xem actual và nguồn Capture; không lấy planned target làm source kg. |
| Có số kg còn lại nhưng free stock 0 | Có thể cả phần còn lại đang reserve cho chính approval | Xem Approved reserved stock/Still held, phân biệt với physical stock. |

Một số finding được UI đổi sang câu tiếng Anh thân thiện; code gốc có trong Network response. Không bắt buộc màn hình phải hiển thị nguyên code để ca test đạt.

## 8. Mẫu ghi kết quả và điều kiện nghiệm thu

Tạo một bản kết quả riêng cho từng đợt; không sửa checklist này thành PASS khi chưa chạy. Không chỉnh Report5/BM04 local để ghi đã nghiệm thu chỉ từ việc tài liệu/code tồn tại.

| Trường | Nội dung cần ghi |
| --- | --- |
| Build | Backend/web commit hoặc working-tree baseline, migration version, ngày giờ test và timezone. |
| Môi trường | Test environment, browser/version/viewport; không ghi connection string hoặc credentials. |
| Fixture | Company/facility/location, order/container/plan/proposal/lot/source ID hoặc mã synthetic. |
| Case ID | CT-xx và biến thể; precondition thực tế. |
| Actor | Role và định danh test đã được phép ghi; không đưa dữ liệu cá nhân thật vào report công khai. |
| Before / after | Loaded/remaining/status, physical/available stock, reserved/consumed/remaining quota, số load/manifest. |
| Request | Route/action, operationId, HTTP status và finding cần thiết; loại bỏ token/cookie/evidence thật. |
| Evidence | Screenshot trước/sau, JSON dossier đã kiểm quyền; trace/source sum và file hash khi cần chứng minh snapshot không đổi. |
| Kết quả | PASS, FAIL, BLOCKED hoặc NOT RUN; expected/actual và ticket lý do nếu không pass. |

Ví dụ ghi **sau khi đã thực hiện thật**: “CT-05 — Actual 0→4; physical 10→6; reservation consumed 0→4, remaining 10→6; một load mới; status PLANNED→LOADING; operationId …; PASS”. Đây là mẫu, không phải execution record.

Phân loại kết quả:

- **PASS:** đúng expected behavior; ca HOLD/missing evidence/unsupported handoff bị chặn đúng cũng là PASS của ca kiểm chặn.
- **FAIL:** quan sát được sai lệch với contract, ví dụ close trừ tồn lần hai, retry tạo load trùng, lộ source cho Export hoặc foreign company.
- **BLOCKED:** không có fixture, role, migration, policy hoặc upstream setup để chạy tới đúng precondition. Không gán PASS cho nhánh chưa chạm tới.
- **NOT RUN:** chưa thực hiện; đây là trạng thái ban đầu của toàn báo cáo.

Điều kiện nghiệm thu bản hiện tại: đường chuẩn CT-01–10 đạt với reconciliation stock/reservation/manifest; kiểm các blocker, permission và retry theo fixture; có trace source kg/provenance hoặc incomplete đúng; dossier giữ snapshot và redaction; ghi rõ ca nâng cao chưa chạy. Mobile layout, staging/witnessed acceptance và benchmark full-season ≤30 giây cần execution evidence riêng, không suy ra từ một container 10 kg.

## 9. Phạm vi chưa làm để không nhầm với lỗi của bài test

| Chưa được mở đầy đủ | Ảnh hưởng đến test |
| --- | --- |
| Blend sau duyệt → actual production/output reservation | CT-27 chỉ kiểm chặn, chưa có E2E loading từ nhánh này. |
| Pool/top-up/withdraw/bagging/transfer làm đổi lot/location/quota sau duyệt | CT-28 kiểm handoff blocker; không tự rebind reservation. |
| Revalidation sau partial loading khi evidence/policy thay đổi | CT-18 chặn tiếp tục đúng; chưa có nút tự review thay thế để tiếp tục. |
| Release container HOLD; unload/reopen/reseal/cancel hàng đã load; close thiếu/dư target | Không có đường ngoại lệ được cấp phép. Dùng fixture riêng để không mắc kẹt happy path. |
| PDF/Excel chứng từ chính thức, late supplements, shipment dispatch | Dossier JSON hiện tại không phải các chức năng này. |
| Container notification inbox/acknowledgment | Banner current HOLD và history chưa đồng nghĩa có inbox. |
| Tự chuyển legacy preparation thành planned container/actual | Hai luồng tách biệt; chưa có mapping được duyệt. |

Backlog/quyết định còn thiếu giữ tại [plan hiện hành](../plans/container-execution-plan-2026-11-13.md#để-lại-lần-sau--dependenciesquyết-định-chưa-có). Báo cáo không mở rộng sang scope khác hoặc tự ký acceptance thay team.

## 10. Căn cứ code và automation để đối chiếu

- [ExportOrders.tsx](../../apps/web/src/ExportOrders.tsx): route order/container, target và điểm mount Loading and closure.
- [BlendPlanning.tsx](../../apps/web/src/BlendPlanning.tsx): Calculate plan, proposal, decision/reservation và nút approve/revoke.
- [ExportAssessment.tsx](../../apps/web/src/ExportAssessment.tsx): reviewed market policy và finished-lot quality observations.
- [ContainerExecution.tsx](../../apps/web/src/ContainerExecution.tsx): tên field/button, confirmation, sessionStorage retry, manifest, history, dossier và source view.
- [ContainerExecutionEndpoints.cs](../../backend/src/GrainLedger.Api/Features/Exports/Shipments/ContainerExecutionEndpoints.cs), [ContainerTraceEndpoints.cs](../../backend/src/GrainLedger.Api/Features/Exports/Shipments/ContainerTraceEndpoints.cs), [ContainerExecution.sql](../../backend/src/GrainLedger.Api/Data/Sql/ContainerExecution.sql): authorization, assessment, locking, accounting, immutable load/close/trace.
- [ContainerExecutionScenario.cs](../../backend/tests/GrainLedger.Api.IntegrationTests/ContainerExecutionScenario.cs): fixture synthetic dùng whole-flow schema 2 và DIRECT_STOCK.
- [ContainerExecutionTests.cs](../../backend/tests/GrainLedger.Api.IntegrationTests/ContainerExecutionTests.cs): tám integration tests về partial load/close/replay/dossier, scope/trace, HOLD/evidence, concurrency/rollback, revocation khi chờ lock, UT-14/private helper, multi-source complement và late source HOLD.
- [ContainerExecution.test.tsx](../../apps/web/src/ContainerExecution.test.tsx): UI confirmation, request strings, permission visibility và retry.
- [Verification record 13/11](container-execution-verification-2026-11-13.md) và [runtime/development sync 16/11](runtime-startup-diagnosis-2026-11-16.md): bằng chứng kỹ thuật ở các đợt trước; không dùng làm log đã chạy checklist manual hiện tại.

Đợt viết báo cáo này chỉ đọc code/tài liệu và kiểm tra liên kết/format tài liệu; không chạy seed/migration/grants, không tạo order/load/HOLD/CLOSE trên database và không chạy browser E2E.
