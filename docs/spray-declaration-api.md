# F05: API khai báo phun thuốc

Nhân viên thu mua ghi một lần phun trên một thửa–vụ. Một khai báo có một hoặc nhiều dòng. Tên thuốc hoặc hoạt chất là thông tin được khai báo, không phải kết quả xét nghiệm và không chứng minh đạt MRL.

## API và quyền

| API | Quyền | Kết quả |
| --- | --- | --- |
| `POST /api/spray-declarations` | `farms.write`, customer company | Tạo khai báo và các dòng. Trả 201 cùng `Location`. |
| `GET /api/spray-declarations?plotSeasonId={id}&page=1&pageSize=20` | `farms.read` hoặc `farms.write` | Lịch sử trong company, có `itemCount`, `hasMore`. |
| `GET /api/spray-declarations/{id}` | `farms.read` hoặc `farms.write` | Header, các dòng, nguồn thông tin và thời gian ghi. |

Các GET chỉ trả dữ liệu của company hiện hành. ID khác company trả 404 ở GET chi tiết hoặc danh sách rỗng khi lọc. `PurchasingStaff` có thể ghi. `CustomerManager` có thể đọc.

## Chuẩn bị để thử trên Scalar

1. Đăng nhập bằng tài khoản `PurchasingStaff` và nhập access token ở mục Bearer của Scalar.
2. Lấy một `plotSeasonId` từ `GET /api/plot-seasons`.
3. Nếu ghi hoạt chất, lấy `substanceId` từ `GET /api/active-substances`.
4. Chọn một mã từ `GET /api/measurement-units` có `quantityKind` là `APPLICATION_RATE`. Nếu danh mục chưa có mã phù hợp, Admin tạo bằng API danh mục.

Ví dụ request. Thay các UUID, thời gian và mã đơn vị bằng dữ liệu test thật. `rate` chỉ là giá trị thử, không phải khuyến nghị sử dụng thuốc.

```json
{
  "deviceId": "web-test-01",
  "clientOperationId": "03872ee2-c9fc-4852-95da-8cbd3b72ab90",
  "capturedAt": "2026-10-13T08:00:00Z",
  "payloadSchemaVersion": 1,
  "payload": {
    "entityId": "29c1b962-d283-4ae2-9ad8-efec6aee7146",
    "plotSeasonId": "6178312b-7c52-43e1-972d-944e4e5c31d1",
    "appliedOn": "2026-10-12",
    "informationSource": "Nông dân cung cấp thông tin từ sổ ghi chép",
    "evidenceRef": null,
    "items": [
      {
        "productName": "Sản phẩm thử nghiệm A",
        "substanceId": null,
        "rate": 1.25,
        "rateUnit": "L/HA"
      }
    ]
  }
}
```

`entityId`, `clientOperationId`, `deviceId` và `capturedAt` do ứng dụng tạo. Nhân viên chọn thửa–vụ và nhập ngày phun (`appliedOn`, `YYYY-MM-DD`), nguồn thông tin, tên thuốc hoặc hoạt chất, liều và đơn vị. Không nhập hoặc tự tạo giờ phun. `informationSource` bắt buộc. Mỗi dòng cần `productName` hoặc `substanceId`, `rate > 0` và đơn vị thuộc nhóm `APPLICATION_RATE`. Ngày phun không được sau ngày `capturedAt` theo giờ Việt Nam.

Gửi lại **nguyên request** với cùng `clientOperationId` sau khi mất response sẽ trả 200 và ID dòng cũ. Cùng operation ID nhưng đổi nội dung trả 409 `OPERATION_CONFLICT`. Tạo khai báo mới phải dùng `entityId` và `clientOperationId` mới.

GET danh sách mặc định page 1, pageSize 20, tối đa 100. Sắp xếp theo ngày phun mới nhất. GET chi tiết trả thêm `informationSource`, `capturedAt` và `items` có ID từng dòng. Query rỗng/sai trả 400. ID không thuộc company trả 404. API không có sửa hoặc xóa khai báo.

## Database và kiểm thử

Migration Capture `20261013124259_AddCaptureSprayDeclarations` tạo `capture.spray_declarations` và `capture.spray_items` cùng các FK đến `capture.plot_seasons`, `identity.users`, `identity.organizations` và `catalog.active_substances`. Migration tiếp theo `20261013134831_ChangeSprayApplicationToDate` đổi `applied_at` thành `applied_on` kiểu `date`. Dòng thuốc giữ `rate_unit` dưới dạng text lịch sử. Nguồn thông tin nằm trong event `SPRAY_DECLARATION_RECORDED`.

Khi tạo database mới, chạy migration theo thứ tự Identity → Catalogs → Capture. Lệnh `--migrate` và `ApiFactory` đã dùng thứ tự này. Migration Capture đã được áp dụng lên Supabase dev ngày 13/10/2026. API đang chạy cần restart để nạp route mới.

Kiểm thử tự động dùng PostgreSQL Testcontainers. Không ghi fixture vào Supabase dev:

```powershell
dotnet test backend/tests/GrainLedger.Api.IntegrationTests/GrainLedger.Api.IntegrationTests.csproj -c Release --filter FullyQualifiedName~SprayDeclarationTests
```
