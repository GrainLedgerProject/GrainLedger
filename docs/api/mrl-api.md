# F03: Bảng MRL có phiên bản

## Tạo bản nháp

`POST /api/mrl-versions` dành cho Administrator có quyền `platform.configure`. API tạo bản `DRAFT`, chưa có dòng giới hạn hoạt chất và **chưa được dùng để đánh giá xuất khẩu**.

Body mẫu (thay hai UUID bằng `id` thật lấy từ `GET /api/markets` và `GET /api/commodities`):

```json
{
  "marketId": "11111111-1111-1111-1111-111111111111",
  "commodityId": "22222222-2222-2222-2222-222222222222",
  "effectiveFrom": "2027-01-01T00:00:00Z",
  "effectiveTo": "2028-01-01T00:00:00Z",
  "sourceRef": "Tên văn bản, ngày công bố và điều khoản áp dụng"
}
```

`effectiveTo` có thể bỏ qua hoặc đặt `null` để chưa xác định ngày kết thúc. Ngày kết thúc, nếu có, phải sau ngày bắt đầu. API lưu thời điểm theo UTC. `sourceRef` không được rỗng. Market và commodity phải tồn tại; UUID không tồn tại trả 404.

Kết quả `201 Created` gồm `id`, `marketId`, `commodityId`, `versionNo`, `status: "DRAFT"`, khoảng hiệu lực, `sourceRef`, `publishedBy: null`, `publishedAt: null`, header `Location` và `ETag`. Server cấp `versionNo` riêng cho từng cặp market–commodity, bắt đầu từ 1. Hai yêu cầu tạo cùng lúc được tuần tự hóa trong giao dịch để không nhận cùng số phiên bản. Mỗi lần tạo thành công ghi `catalog_events` cùng giao dịch; dữ liệu sai không tạo draft hay event.

## Liệt kê phiên bản

`GET /api/mrl-versions` trả `{ items, page, pageSize, hasMore }`. Có thể lọc bằng `marketId`, `commodityId`, `status` (`DRAFT` hoặc `PUBLISHED`); các bộ lọc đều tùy chọn. Ví dụ: `GET /api/mrl-versions?marketId=5147e69c-625c-4675-b8a9-0e2dc322ddc3&status=DRAFT&page=1&pageSize=20`. Mặc định `page=1`, `pageSize=20`; kích thước trang tối đa 100. Danh sách xếp theo ngày bắt đầu hiệu lực mới nhất, rồi số phiên bản giảm dần. Query sai trả 400.

Administrator xem cả bản nháp và bản đã công bố. Người có `mrl.read` chỉ thấy bản `PUBLISHED`, kể cả khi yêu cầu `status=DRAFT` (kết quả rỗng). Tài khoản không có một trong hai quyền nhận 403. Mỗi phần tử có cùng các trường với API chi tiết; danh sách không trả ETag riêng cho từng phần tử.

## Xem chi tiết và lấy ETag

`GET /api/mrl-versions/{id}` trả toàn bộ thông tin header và `ETag` hiện tại của phiên bản. Ví dụ, với ID đã tạo: `GET /api/mrl-versions/7a3a031a-3889-4281-ba0e-8f8a1a032feb`. Administrator được xem `DRAFT` và `PUBLISHED`; người có quyền `mrl.read` chỉ xem `PUBLISHED`. Với người có `mrl.read`, một ID draft được trả 404 để không lộ bản chưa công bố. Tài khoản khác bị từ chối 403. Bản không tồn tại trả 404. ETag sẽ được dùng cho API sửa draft tiếp theo.

## Sửa thông tin bản nháp

`PATCH /api/mrl-versions/{id}` dành cho Administrator có `platform.configure`. Trước tiên gọi GET chi tiết, sao chép header `ETag` sang header `If-Match` của PATCH. Body chỉ gồm trường cần sửa: `effectiveFrom`, `effectiveTo`, `sourceRef`. Ví dụ:

```json
{
  "sourceRef": "Tên văn bản và điều khoản đã kiểm tra",
  "effectiveTo": null
}
```

`effectiveTo: null` nghĩa là chưa định ngày kết thúc; bỏ trường này thì giữ ngày cũ. Ngày kết thúc, nếu có, phải sau ngày bắt đầu. Không cho sửa `marketId`, `commodityId`, `versionNo`, `status` hoặc thông tin công bố qua API này. Thành công trả bản ghi và ETag mới, đồng thời ghi `MRL_VERSION_UPDATED` vào audit. Thiếu `If-Match` trả 428; ETag cũ trả 412; bản đã `PUBLISHED` trả 409; body sai trả 400.

## Xem các giới hạn trong một phiên bản

`GET /api/mrl-versions/{id}/limits` trả `{ items, page, pageSize, hasMore }`. Có thể lọc theo `substanceId`; mặc định `page=1`, `pageSize=20`, tối đa 100. Ví dụ: `GET /api/mrl-versions/7a3a031a-3889-4281-ba0e-8f8a1a032feb/limits`. Mỗi dòng gồm `id`, `mrlVersionId`, `substanceId`, `limitValue`, `unit`. Response có `ETag` của phiên bản cha, dùng cho thao tác sửa dòng ở bước sau.

Administrator xem dòng của cả `DRAFT` và `PUBLISHED`; người có `mrl.read` chỉ xem phiên bản `PUBLISHED`. Người có `mrl.read` hỏi ID bản nháp nhận 404; không có quyền nhận 403. Bộ lọc hoặc phân trang sai trả 400. Bản nháp vừa tạo chưa có giới hạn sẽ trả `items: []`; đây không phải là tuyên bố rằng không có giới hạn pháp lý.

## Thêm hoặc cập nhật một giới hạn

`PUT /api/mrl-versions/{id}/limits/{substanceId}` dành cho Administrator, chỉ áp dụng cho `DRAFT`. `substanceId` lấy từ `GET /api/active-substances`. Trước khi dùng, danh mục `GET /api/measurement-units?q=MG/KG` phải có đơn vị code `MG/KG`, `quantityKind: RESIDUE_CONCENTRATION`; nếu chưa có, Administrator tạo bằng `POST /api/measurement-units`.

Gọi GET chi tiết phiên bản hoặc GET limits trước, rồi sao chép nguyên **ETag có dấu ngoặc kép** vào header `If-Match`. Body dùng đúng hai trường:

```json
{
  "limitValue": 0.01,
  "unit": "mg/kg"
}
```

Giá trị `0.01` ở đây **chỉ minh họa cú pháp**, không phải giới hạn pháp lý. Chỉ nhập giới hạn thật sau khi kiểm tra nguồn và `sourceRef`. `limitValue` phải không âm và vừa `numeric(24,12)`; MVP chỉ chấp nhận mg/kg, không tự quy đổi ppm hay đơn vị khác. API tạo dòng mới trả 201, cập nhật dòng cũ trả 200. Cả hai trả ETag mới của phiên bản; gửi lại đúng nội dung không tạo audit mới. Mỗi thay đổi ghi event vào `catalog_events` cùng giao dịch.

Thiếu `If-Match` trả 428; ETag cũ trả 412; bản `PUBLISHED` trả 409; hoạt chất không tồn tại trả 404; giá trị hoặc đơn vị không hợp lệ trả 400. Tài khoản không có `platform.configure` nhận 403.

## Xóa một giới hạn nhập sai khỏi bản nháp

`DELETE /api/mrl-versions/{id}/limits/{substanceId}` chỉ dành cho Administrator và phiên bản `DRAFT`. Gửi `If-Match` bằng ETag mới nhất của phiên bản, lấy từ GET chi tiết hoặc GET limits. Không có request body. Thành công trả `204 No Content` và ETag mới. Dòng bị xóa khỏi `mrl_limits`, nhưng sự kiện `MRL_LIMIT_DELETED` giữ giá trị trước khi xóa trong `catalog_events`.

Thiếu ETag trả 428; ETag cũ trả 412; phiên bản đã `PUBLISHED` trả 409; dòng không tồn tại trả 404. API này chỉ dùng để sửa dữ liệu nháp nhập sai, không xóa nội dung MRL đã công bố.

## Công bố phiên bản

`POST /api/mrl-versions/{id}/publish` dành cho Administrator có `platform.configure`. Không có request body. Gọi GET chi tiết hoặc GET limits để lấy `ETag` mới nhất, rồi gửi nguyên giá trị có dấu ngoặc kép trong header `If-Match`. API chỉ công bố `DRAFT` có nguồn, ngày hiệu lực không lùi trước thời điểm công bố, ít nhất một giới hạn hợp lệ theo mg/kg và đơn vị `MG/KG` được cấu hình là `RESIDUE_CONCENTRATION`.

Cùng thị trường và commodity, các khoảng hiệu lực `PUBLISHED` không được chồng nhau. Khoảng được hiểu là `[effectiveFrom, effectiveTo)`: có hiệu lực từ đầu, hết hiệu lực ngay tại cuối. Khi công bố phiên bản kế tiếp của một bản đang không có `effectiveTo`, hệ thống đóng bản cũ tại `effectiveFrom` của bản mới trong **cùng giao dịch**. Nội dung giới hạn và nguồn của bản cũ không đổi; hai sự kiện audit liên kết bằng `correlationId`. Thành công trả phiên bản `PUBLISHED`, `publishedBy`, `publishedAt` và ETag mới. Thiếu ETag trả 428; ETag cũ trả 412; bản đã công bố, ngày lùi, thiếu giới hạn hoặc khoảng chồng nhau trả 409.

**Không công bố bản nháp đang chứa `sourceRef` hoặc giá trị MRL chỉ để thử API trên Supabase chung.** Sau công bố, không được sửa/xóa nội dung; muốn thay phải tạo phiên bản mới. Kiểm thử publish trên PostgreSQL Testcontainers hoặc database dev cô lập với dữ liệu nghiệp vụ.

## Tìm phiên bản áp dụng tại thời điểm quyết định

`GET /api/mrl-versions/resolve?marketId={uuid}&commodityId={uuid}&decisionAt={ISO-8601}` dành cho Administrator hoặc người có `mrl.read`. Ví dụ `decisionAt=2027-01-01T00:00:00Z`. API chỉ trả bản `PUBLISHED` có `publishedAt <= decisionAt` và `effectiveFrom <= decisionAt < effectiveTo` (hoặc không có `effectiveTo`). Trả phiên bản và ETag, không tự đánh giá một lô gạo hay đơn xuất khẩu.

Thiếu hoặc sai query trả 400; không có bản áp dụng trả 404 với mã `MRL_VERSION_NOT_AVAILABLE`; nếu dữ liệu bất thường khiến nhiều bản cùng áp dụng trả 409 `MRL_VERSION_AMBIGUOUS`. Người không có quyền nhận 403. Khi lưu assessment, hệ thống phải giữ `mrlVersionId` đã chọn và snapshot bằng chứng; không resolve lại để thay đổi quyết định lịch sử.

Migration `20261013074717_AddMrlVersions` tạo `catalog.mrl_versions`; migration `20261013113324_AddMrlLimits` tạo `catalog.mrl_limits` với hai FK RESTRICT, unique theo phiên bản/hoạt chất và CHECK số không âm. Migration Catalogs thứ hai đã áp dụng lên Supabase dev ngày 13/10/2026; không chạy migration Identity/Capture. Khởi động lại API để Scalar hiển thị route mới.

Kiểm thử bằng PostgreSQL Testcontainers, không ghi fixture vào Supabase chung:

```powershell
dotnet test backend/tests/GrainLedger.Api.IntegrationTests/GrainLedger.Api.IntegrationTests.csproj -c Release --filter FullyQualifiedName~MrlDraftCreationTests
```
