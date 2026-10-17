# Kế hoạch bổ sung Admin và Capture

Ngày đối chiếu code: 16/10/2026. Trạng thái: đã triển khai các hạng mục chính; xem [báo cáo triển khai](admin-capture-enhancements-report.md) để biết kết quả kiểm thử và giới hạn môi trường.

## 1. Mục tiêu và phạm vi

Hoàn thiện tám bổ sung đã thống nhất cho Admin và luồng Capture online, tận dụng API hiện có và chỉ bổ sung contract khi dữ liệu/quyền trả về chưa đủ.

- Admin quản lý Crop season theo công ty; Crop season là mùa vụ dùng chung của công ty, Plot season là đăng ký một thửa vào mùa vụ đó.
- PurchasingStaff quản lý hồ sơ trong Farmers, đăng ký thu mua và mẫu trong phạm vi công ty của mình.
- Giữ mô hình một procurement cho một nông hộ/một lần giao hàng, nhiều dòng source lot; mỗi sample gắn một source lot.
- Intake đăng ký nguồn dự kiến; tồn kho khả dụng chỉ xuất hiện sau quy trình nhận hàng của Ledger.
- F07 offline tiếp tục hoãn. Xét nghiệm/kết quả F11, mẫu gộp nhiều source lot, polygon và quy trình nhận hàng Ledger không thuộc đợt này.

## 2. Kết quả đối chiếu hiện trạng

| Hạng mục | Backend hiện có | Khoảng trống frontend / contract |
| --- | --- | --- |
| Crop seasons | GET list/detail, POST, PUT, DELETE `/api/crop-seasons`; list cần `companyId` đối với Admin | Admin chưa có tab riêng; type `cropSeasons.ts` chưa chứa version/capabilities |
| Farmer và plot archive | DELETE `/api/farmers/{id}`, `/api/plots/{id}`, body `{ version }` | Bổ sung thao tác tại màn hồ sơ; xử lý dependency và bản ghi đã thay đổi |
| Plot season edit/archive | PUT, DELETE `/api/plot-seasons/{id}` | `PlotSeasonDetail.tsx` chủ yếu đọc; cần form sửa, archive và cập nhật type |
| Intake search/filter | GET `/api/procurements`: `search`/`q`, `farmerId`, `from`, `to`, `sortBy`, `sortDirection`, paging | `Procurement.tsx` hiện gọi list với page/pageSize |
| MRL áp dụng | GET `/api/mrl-versions/resolve?marketId=...&commodityId=...&decisionAt=...`; GET limits | AdminMrl chưa có màn tra cứu theo thời điểm |
| Spray toàn công ty | GET `/api/spray-declarations` không bắt buộc `plotSeasonId`; có detail và paging | FE chỉ list theo Plot season; chưa có màn tổng hợp |
| Giống lúa danh mục | GET `/api/rice-varieties`; Plot season nhận `riceVariety` dạng string | Chưa chọn/gợi ý từ danh mục; chưa có `riceVarietyId` |
| Nguồn gốc | Intake/source lot/sample có các ID liên kết; intake summary có farmerName | Detail thiếu nhiều nhãn như tên farmer, mã plot, tên season; cần bổ sung read DTO hoặc lookup có giới hạn |

Các điểm cần giữ chính xác khi triển khai:

1. `canEdit/canDelete` hiện chủ yếu kiểm tra người tạo và quyền, chưa phản ánh đầy đủ dữ liệu phụ thuộc. Không coi `true` là cam kết thao tác chắc chắn thành công.
2. Backend chặn farmer đã có plot/procurement; plot đã có plot season; plot season đã có spray/intake; crop season đã được đăng ký. Archive là soft delete, không xóa dây chuyền và chưa có restore.
3. Reference update dùng PUT và `expectedVersion`; DELETE dùng `version`. MRL dùng ETag/If-Match, không dùng chung máy móc hai cơ chế.
4. Procurement hỗ trợ sort `receivedAt`, `code`, `status`; chưa xác nhận có lọc status, nên đợt này không thêm dropdown lọc status.
5. Khoảng ngày procurement được backend hiểu theo UTC+7, `from` bao gồm đầu ngày và `to` bao gồm hết ngày. MRL resolve nhận timestamp có UTC offset, hiệu lực theo khoảng `[effectiveFrom, effectiveTo)`.

## 3. Thứ tự triển khai và đầu ra

### Giai đoạn A — Chuẩn hóa contract và hành vi dùng chung

- Bổ sung type FE cho version, canEdit, canDelete, totalCount nơi endpoint có trả về; không ép tất cả list có totalCount.
- Dùng lại permission checks, API client, paging, modal và cách điều hướng hiện có. Tách component nhỏ khi có nhiều màn cùng dùng.
- Làm rõ khả năng thao tác ở backend: tính capability từ quyền + người tạo + dependency; có thể thêm `editBlockedReason`/`archiveBlockedReason` vào read DTO. Tính bằng bulk/EXISTS trong truy vấn, không một request/query cho mỗi dòng.
- Duy trì kiểm tra trong transaction khi PUT/DELETE để xử lý dependency mới phát sinh sau khi màn hình được tải.
- Nếu giữ nguyên backend ở bước đầu: ẩn thao tác khi capability false, xử lý 409 tại chỗ. Chỉ nghiệm thu yêu cầu “chỉ hiện khi backend cho phép” đầy đủ sau khi capability có dependency.
- Lỗi field hiển thị sát input; lỗi thao tác hiển thị trong modal/panel liên quan. 409 giải thích dữ liệu đang được sử dụng; 412 yêu cầu tải lại và xem thay đổi, không tự retry mutation với version mới.
- Modal quản lý focus, Escape và trả focus về nút mở; ngăn submit lặp khi pending. Giữ dữ liệu đã nhập khi API thất bại.

Đầu ra: contract rõ ràng, trạng thái loading/empty/error/retry và mẫu thao tác archive dùng nhất quán.

### Giai đoạn B — Nhóm ưu tiên 1: quản lý mùa vụ và hồ sơ

**B1. Crop seasons trong Admin**

- Thêm tab ngang hàng `Crop seasons` trong AdminWorkspace; cập nhật điều hướng ở App.
- Chọn công ty Customer từ lookup tổ chức đã được cấp quyền; chưa chọn công ty thì chưa gọi list mùa vụ. Kiểm tra tài khoản có `platform.configure` có quyền dùng lookup này; nếu không, cung cấp lookup được phép thay vì mở rộng quyền quản trị tài khoản.
- List: tên mùa vụ, ngày bắt đầu/kết thúc, thao tác; tìm theo tên, sort tên/ngày bắt đầu, paging phía server.
- Nút Add/Edit mở modal với company, name, startsOn, endsOn. Company cố định khi sửa; validate endsOn >= startsOn.
- Tạo thành công tải lại bản ghi để có version/capabilities; PUT gửi expectedVersion hiện tại; archive xác nhận tên mùa vụ và gửi version.
- Đổi công ty phải xóa selection, reset page và hủy/bỏ response cũ để tránh hiển thị dữ liệu công ty trước.
- Giữ màn Ledger hiện có hoạt động; không xóa chức năng ở đó trong đợt này.

**B2. Archive nông hộ và thửa trong Farmers**

- Đặt Archive tại farmer detail và plot detail, theo capability từ backend; dùng nhãn Archive thống nhất.
- Modal nêu đúng bản ghi và ảnh hưởng: ngừng xuất hiện trong danh sách/chọn mới, giữ dữ liệu lịch sử; không hứa có thể khôi phục.
- Thành công: quay về danh sách/parent, tải lại dữ liệu và giữ bộ lọc; nếu trang cuối hết bản ghi thì lùi một trang.
- Bản ghi có dependency: hiển thị lý do bị khóa; trường hợp phát sinh đồng thời trả 409 vẫn giữ hồ sơ để người dùng đọc.

**B3. Sửa/archive Plot season ngay tại detail**

- Form sửa chọn Crop season trong công ty và giống lúa; giữ plotId cố định theo trang đang mở.
- PUT payload `{ plotId, seasonId, riceVariety, expectedVersion }`; DELETE `{ version }`.
- Chặn/giải thích khi đã có khai báo phun thuốc hoặc intake; xử lý cặp plot/season trùng và season không còn khả dụng.
- Sau sửa cập nhật tiêu đề, tên mùa vụ và danh sách ở plot; sau archive quay về plot detail.

Nghiệm thu B: Admin tạo mùa vụ → PurchasingStaff chọn được khi đăng ký thửa; bản ghi chưa được dùng sửa/archive được nếu có quyền; khác người tạo/khác công ty/bản ghi đã dùng bị chặn đúng; không mất lịch sử.

### Giai đoạn C — Nhóm ưu tiên 2: tìm kiếm và tra cứu

**C1. Paddy intake search/filter**

- Toolbar: mã phiếu, farmer picker có tìm kiếm/phân trang, từ ngày/đến ngày, sort thời gian/mã/trạng thái và chiều sort; Apply và Clear.
- Gửi đúng query có sẵn; không lọc riêng trang đang tải trên client. Kiểm tra from <= to ngay tại input.
- Đổi bộ lọc reset về page 1; hủy/bỏ response cũ; giữ bộ lọc và page khi mở detail rồi Back.
- Hiển thị phân biệt “chưa có phiếu” và “không có kết quả theo bộ lọc”.

**C2. Tra cứu MRL áp dụng**

- Thêm chế độ `Applicable MRL` cạnh quản lý Versions trong khu vực MRL, với market, commodity, thời điểm và nút tra cứu.
- Chuyển giờ local sang ISO 8601 có offset/Z; gọi resolve, sau đó lấy limits có paging của version được trả về.
- Hiển thị phiên bản, trạng thái Published, thời gian hiệu lực, nguồn pháp lý và limits; không chọn version bằng logic tự suy luận ở FE.
- 404: “không có phiên bản công bố áp dụng tại thời điểm này”; 409 ambiguous: báo xung đột dữ liệu, không tự chọn một phiên bản.
- Cho phép UI read-only khi người dùng có `mrl.read` theo contract; quyền sửa vẫn là `platform.configure`. Nếu vị trí navigation chưa hỗ trợ vai trò đọc, thêm entry phù hợp thay vì đưa họ vào quản trị tài khoản.
- Tra cứu là xem quy tắc; không kết luận lô/mẫu đạt MRL khi chưa có kết quả xét nghiệm.

**C3. Danh sách phun thuốc toàn công ty**

- Thêm mục `Spray declarations` trong Capture cho vai trò có farms.read/farms.write.
- Gọi list không truyền plotSeasonId để lấy toàn công ty; bảng có ngày phun, đăng ký thửa/mùa vụ, số sản phẩm, ngày ghi nhận, link chi tiết.
- Dùng lại chi tiết Spray; điều hướng được về Plot season/plot và trở lại list đúng page.
- Phạm vi đợt đầu chỉ có bộ lọc plotSeasonId đã hỗ trợ. Bộ lọc farmer/ngày/substance và sort bổ sung là backlog riêng; không giả lập bằng lọc một trang ở FE.
- Nhãn nguồn gốc được bổ sung cùng D2; tạm thời không gọi chuỗi API cho từng dòng để dựng tên.

Nghiệm thu C: filter/sort/page hoạt động kết hợp; ngày biên đúng UTC+7; đổi filter nhanh không hiện kết quả cũ; MRL chọn đúng tại đầu/cuối hiệu lực; không lộ dữ liệu công ty khác.

### Giai đoạn D — Nhóm ưu tiên 3: giảm nhập sai và rõ nguồn gốc

**D1. Chọn giống lúa từ danh mục**

- Dùng picker/gợi ý từ GET `/api/rice-varieties` trong tạo và sửa Plot season; hiển thị code + name, gửi tên theo contract string hiện có.
- Cho phép giữ tên lịch sử không còn nằm trong danh mục; hỗ trợ nhập tên ngoài danh mục có nhãn rõ ràng vì backend hiện cho phép.
- Không âm thầm đổi tên giống trên lịch sử khi danh mục được sửa. Gợi ý từ danh mục không được mô tả là ràng buộc bằng ID.
- Liên kết bắt buộc bằng riceVarietyId là giai đoạn riêng: cần contract, migration, mapping dữ liệu cũ và chính sách tên snapshot; chưa đưa migration đó vào đợt này.

**D2. Nguồn gốc trong intake, source lot và sample**

- Hiển thị: Farmer → Plot → Crop season/Plot season → Procurement → Source lot → Sample; chỉ thể hiện quan hệ thật, từng receipt line có nguồn riêng.
- Bổ sung read DTO thích hợp với farmer code/name, plot code, season id/name và mã procurement/source lot còn thiếu. Với sample, trả source context hoặc lấy một source-lot detail trên trang detail; danh sách dùng bulk projection.
- Join theo company scope, xử lý thiếu nhãn bằng mã/ID và trạng thái rõ ràng; không fetch contact cá nhân không cần thiết.
- Giữ khả năng đọc nguồn lịch sử theo authorization khi reference đã archive; không dùng bộ lọc archive để làm biến mất bằng chứng lịch sử, không bỏ company scope khi đọc lịch sử.
- Link dẫn tới hồ sơ có quyền xem; mở trực tiếp hoặc refresh vẫn lấy đủ context. Trở lại màn trước giữ page/filter.
- Không cộng khối lượng bằng floating point; giữ decimal strings và đơn vị từ backend.

Nghiệm thu D: một intake có hai source lot từ hai thửa hiển thị đúng từng nguồn; sample lot A không mang nguồn lot B; tên dài, dữ liệu lịch sử thiếu nhãn và record ngoài quyền đều được xử lý.

## 4. File dự kiến thay đổi

| Khu vực | File hiện có / file dự kiến |
| --- | --- |
| Điều hướng | `apps/web/src/App.tsx`, `AdminWorkspace.tsx` |
| Crop seasons | `cropSeasons.ts`, thêm `AdminCropSeasons.tsx` và test hành vi |
| Hồ sơ | `Farmers.tsx`, `PlotDetail.tsx`, `PlotSeasonDetail.tsx`, `PlotSeasonCreate.tsx`; thêm editor/confirmation khi cần |
| Tra cứu | `Procurement.tsx`, `AdminMrl.tsx`, `Spray.tsx`; thêm component list/resolve có phạm vi rõ |
| Nguồn gốc | `procurementTypes.ts`, `Samples.tsx`, các read DTO Capture |
| Backend nhỏ | `FarmerEndpoints.cs`, `PlotEndpoints.cs`, `PlotSeasonEndpoints.cs`, `CropSeasonEndpoints.cs` cho capabilities; `ProcurementEndpoints.cs`, `SampleEndpoints.cs`, `SprayDeclarationEndpoints.cs` cho nhãn đọc |
| Regression | `F04Enhancements.test.tsx`, `CaptureFlow.test.tsx`, `AdminMrl.test.tsx`, test integration Capture/Catalog liên quan |

## 5. Kiểm thử và bàn giao

- FE: hành vi thành công/lỗi, quyền thao tác, 409/412, giữ filter khi Back, response cũ, phạm vi company, datetime MRL và ngày intake.
- BE khi thay DTO/capability: kiểm tra actor/creator/company, dependency và concurrency; dùng PostgreSQL test container, không dùng database chung cho test ghi.
- Browser: desktop/mobile, modal keyboard/focus, thông báo tại đúng nơi thao tác, không nhảy layout khi loading, tên dài, bảng rỗng và nhiều trang. Dùng test environment cô lập cho các bước tạo/sửa/archive.
- Chạy FE build và test liên quan sau từng nhóm; chạy suite frontend cuối đợt. Với BE thay đổi, build và integration tests của contract bị ảnh hưởng.
- Happy path cuối: Admin tạo mùa vụ → tạo farmer → tạo plot → đăng ký Plot season với giống → khai báo phun → intake nhiều dòng → source lot → sample → handover; tra lại bằng tìm kiếm và danh sách tổng hợp.
- Nhánh archive được test bằng dữ liệu chưa dùng riêng; không kỳ vọng archive toàn bộ chuỗi happy path đã có lịch sử.
- Không cần migration cho UI, DTO read và capability nếu không đổi schema. Nếu phát hiện cần schema/index, ghi migration riêng và xin phép trước khi áp dụng database chung.

## 6. Các mốc hoàn thành

- [x] A: Contract và capabilities xét dependency; lỗi 409/412 tại form/panel thao tác.
- [x] B1: Crop seasons Admin.
- [x] B2: Farmer/plot archive.
- [x] B3: Plot season edit/archive.
- [x] C1: Intake search/filter/sort.
- [x] C2: MRL resolve UI.
- [x] C3: Spray toàn công ty.
- [x] D1: Rice variety picker.
- [x] D2: Nguồn gốc trong intake/source lot/sample và liên kết hồ sơ trên detail.
- [ ] Backend integration và browser walkthrough trên môi trường có Docker/API mới; xem báo cáo.

Thứ tự thực hiện: A → B1 → B2 → B3 → C1 → C2 → C3 → D1 → D2 → kiểm thử toàn luồng. D1 có thể làm cùng B3 để dùng chung editor; phần nhãn cho C3 được hoàn thiện trong D2.

## 7. Nguồn đối chiếu

- [Architecture và quyền truy cập](architecture.md)
- [Crop season API](../backend/src/GrainLedger.Modules.Capture/CropSeasonEndpoints.cs)
- [Farmer API](../backend/src/GrainLedger.Modules.Capture/FarmerEndpoints.cs)
- [Plot API](../backend/src/GrainLedger.Modules.Capture/PlotEndpoints.cs)
- [Plot season API](../backend/src/GrainLedger.Modules.Capture/PlotSeasonEndpoints.cs)
- [Procurement API](../backend/src/GrainLedger.Modules.Capture/ProcurementEndpoints.cs)
- [Spray API](../backend/src/GrainLedger.Modules.Capture/SprayDeclarationEndpoints.cs)
- [Sample API](../backend/src/GrainLedger.Modules.Capture/SampleEndpoints.cs)
- [MRL API](../backend/src/GrainLedger.Modules.Catalogs/MrlVersionEndpoints.cs)

Đây là kế hoạch bổ sung dựa trên yêu cầu đã liệt kê và code đang có; không coi các mở rộng đề xuất là yêu cầu đã được xác nhận trong GrainLedger V2.
