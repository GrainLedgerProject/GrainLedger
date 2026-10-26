# Báo cáo bổ sung Admin và Capture

Ngày: 16/10/2026. Đối chiếu theo [implementation plan](../plans/admin-capture-enhancements-plan.md).

## Đã làm

| Mục | Thay đổi có thể thấy trên FE | API/contract |
| --- | --- | --- |
| Crop seasons Admin | Tab Crop seasons: chọn customer company, tìm, sort, paging, thêm, sửa, archive | API hiện có; cập nhật read capabilities theo dependency |
| Farmer/plot archive | Nút Archive và hộp xác nhận trong farmer/plot detail; điều hướng về parent sau khi thành công | DELETE với `version`; read `canEdit/canDelete` ẩn thao tác nếu record đang được dùng |
| Plot season edit/archive | Nút ở detail, sửa season/giống và archive tại chỗ | PUT `expectedVersion`, DELETE `version`; capability xét spray/intake |
| Intake search/filter | Tìm mã phiếu, tìm/chọn farmer, lọc khoảng ngày, sort thời gian/mã/trạng thái; giữ filter/page khi mở detail rồi quay lại | Query list hiện có |
| Applicable MRL | Market + commodity + decision time → phiên bản Published áp dụng, nguồn luật, limits | GET resolve và list limits; xử lý 404/409; read-only cho vai trò `mrl.read` |
| Spray toàn công ty | Mục Spray declarations, paging, mở detail/Plot season, quay lại đúng trang | GET list không có `plotSeasonId`; read DTO trả thêm plot code + season name |
| Giống lúa | Gợi ý từ catalog tại form tạo/sửa Plot season, vẫn nhập được tên giống cũ/ngoài danh mục | GET rice varieties; gửi string theo contract hiện hành |
| Nguồn gốc | Intake/source lot/sample detail hiển thị farmer, plot, season, receipt và đường dẫn tương ứng | Procurement/source-lot read DTO bổ sung nhãn; sample detail đọc source-lot theo quyền |

Read DTO mới chỉ thêm trường tùy chọn; không đổi schema và không cần migration. Các truy vấn nhãn ở detail kiểm tra company scope và đọc được tên của reference đã archive để giữ thông tin lịch sử. List capability kiểm tra dependency theo lô ID của trang hiện tại; PUT/DELETE tiếp tục kiểm tra lại trong transaction. Nếu dữ liệu thay đổi giữa lúc mở và bấm Save/Archive, FE hiển thị lỗi 409/412 tại chỗ.

## Kiểm tra đã chạy

- `npm run build`: đạt.
- `npx vitest run --maxWorkers=4`: 16 file, 81 test đạt. Các test mới kiểm tra Crop seasons theo công ty/version, MRL resolve theo thời điểm, intake filter gửi server và khôi phục sau khi mở lại list.
- `dotnet build GrainLedger.slnx -c Release --no-restore`: đạt. Build Debug API bị Windows khóa vì tiến trình API hiện đang chạy.
- Backend integration tests: chưa chạy được. Testcontainers không kết nối được Docker pipe `npipe://./pipe/docker_engine` trong môi trường hiện tại; fixture thất bại trước khi vào API. Không dùng database chung để thay thế test.
- Chưa thực hiện browser walkthrough với tài khoản thật. Cần khởi động lại backend để nạp DLL mới trước khi test FE; Vite có thể hot reload nhưng backend Debug đang chạy vẫn dùng code cũ.

## Cách test trên frontend

Chuẩn bị: chạy backend và frontend tại `http://localhost:5173`; đăng nhập một Administrator của mill và một PurchasingStaff của customer company. Khởi động lại backend sau lần build này. Dùng bản ghi test riêng; archive là soft delete và không có nút Restore.

1. **Admin → Crop seasons:** chọn customer company. Bấm Add crop season, nhập `Demo Đông Xuân 2027`, từ `2027-01-01` đến `2027-05-01`. Xem mùa vụ trong list, tìm theo tên, đổi sort, mở Edit và sửa tên. Chọn một mùa vụ chưa có plot season để thử Archive. Mùa vụ đã dùng sẽ không hiện nút Edit/Archive sau khi tải lại.
2. **Farmers → thêm Farmer/Plot:** tạo farmer test, vào detail tạo plot. Với farmer đã có plot, nút Archive farmer không hiện. Tạo một farmer test khác chưa có plot để thử Archive; sau khi xác nhận sẽ quay về list và record không còn trong active list. Tương tự, một plot chưa có plot season có thể archive; plot đã đăng ký mùa vụ sẽ không hiện Archive.
3. **Plot → Register season:** chọn mùa vụ Admin vừa tạo. Trường Rice variety gợi ý giá trị từ catalog; chọn `ST25` nếu có hoặc nhập một tên test. Vào Plot season detail, thử Edit registration đổi giống hoặc season và lưu. Nếu Plot season đã có spray/intake, nút sửa/archive sẽ ẩn sau khi tải lại. Với bản ghi test chưa dùng, thử Archive rồi kiểm tra quay về Plot.
4. **Spray declarations:** tạo khai báo phun từ Plot season. Mở mục Spray declarations ở sidebar, thấy bản ghi trong danh sách toàn công ty; bấm tên Plot season để về hồ sơ hoặc Open để xem chi tiết, rồi Back to declarations. Kiểm tra page trước/sau nếu có nhiều bản ghi.
5. **Paddy intake:** tạo một receipt có 2 dòng từ 2 plot seasons thuộc cùng farmer. Trên danh sách, tìm theo mã `TM-…`, chọn farmer, chọn from/to và sort; Apply. Mở receipt rồi Back, bộ lọc và trang vẫn giữ. Thử from > to: FE báo lỗi ngay. Trong receipt, kiểm tra từng dòng có mã plot + tên season riêng, các link đến farmer/plot/Plot season/source lot.
6. **Source lot → Sample:** mở từng source lot từ receipt; kiểm tra receipt, farmer, plot, season đúng dòng. Tạo sample ở một source lot, vào Sample details; kiểm tra source lot và chuỗi nguồn gốc, rồi bấm link để về hồ sơ. Sample chỉ gắn source lot đã chọn.
7. **Applicable MRL:** Admin mở tab Applicable MRL; người có quyền `mrl.read` dùng mục cùng tên ở sidebar. Chọn market, commodity và thời điểm nằm trong khoảng hiệu lực của một version Published, bấm Find version. Kiểm tra version, nguồn luật và substance limits. Chọn thời điểm ngoài khoảng để thấy thông báo không có version. Tra cứu không kết luận mẫu/lô đạt chuẩn.
8. **Quyền và lỗi cạnh tranh:** đăng nhập user cùng công ty nhưng không phải người tạo và kiểm tra không thấy Edit/Archive. Mở cùng một record trong hai tab, thay đổi ở tab A rồi thao tác bằng version cũ ở tab B: FE phải báo record đã thay đổi, yêu cầu tải lại. Không làm bước archive bằng record đang có lịch sử thật.

## Giới hạn còn lại

- Bộ lọc nâng cao của Spray theo farmer/ngày/substance cần API mới; hiện list toàn công ty có paging và liên kết hồ sơ.
- Rice variety vẫn lưu tên string. Liên kết cứng với catalog bằng ID cần migration và chính sách dữ liệu cũ riêng.
- FE chưa chạy browser walkthrough vì backend Debug hiện được một tiến trình khác giữ DLL và Docker Testcontainers không truy cập được từ môi trường này. Build/FE tests đã đạt; cần kiểm tra thao tác thật theo các bước trên sau khi khởi động lại backend.
- Không commit/push hoặc áp dụng migration trong đợt này.
