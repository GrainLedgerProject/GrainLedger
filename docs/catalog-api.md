# F03: API danh mục hoạt chất, thị trường và commodity

Module `GrainLedger.Modules.Catalogs` sở hữu `catalog.active_substances` và `catalog.catalog_events`. Đây là danh mục dùng chung để Capture chọn hoạt chất khi khai báo phun thuốc; giới hạn MRL theo thị trường sẽ nằm ở các API F03 tiếp theo.

## Loại/dạng sản phẩm (`commodities`)

Commodity là loại hoặc dạng sản phẩm được áp dụng giới hạn MRL; không phải giống lúa. Danh mục này dùng chung cho các công ty.

| Method | Route | Quyền | Kết quả |
| --- | --- | --- | --- |
| POST | `/api/commodities` | `platform.configure` (Administrator) | Tạo; trả 201, Location và ETag |
| GET | `/api/commodities?q=&page=&pageSize=` | Tài khoản đang hoạt động, role và tổ chức hợp lệ | Tìm mã/tên; mặc định 20 dòng/trang, tối đa 100 |
| GET | `/api/commodities/{id}` | Như GET danh sách | Chi tiết và ETag; không có trả 404 |
| PATCH | `/api/commodities/{id}` | `platform.configure`, `If-Match` | Chỉ sửa tên; trả ETag mới |

POST body: `{"code":"RICE","name":"Milled rice"}`. PATCH body: `{"name":"Milled white rice"}`. API trim, viết hoa mã và giới hạn 40 ký tự; mã không đổi sau khi tạo. Mã trùng trả 409. PATCH thiếu `If-Match` trả 428, ETag cũ trả 412. Tạo/sửa thành công ghi `catalog_events` cùng giao dịch. Chưa có DELETE cho commodities.

Migration `20261012074031_AddCommodities` chỉ tạo `catalog.commodities` và chỉ mục unique cho `code`; không thay Identity hoặc Capture. Migration đã áp dụng lên Supabase dev ngày 12/10/2026. Khởi động lại API để Scalar hiển thị route mới.

## Thị trường xuất khẩu (`markets`)

| Method | Route | Quyền | Kết quả |
| --- | --- | --- | --- |
| POST | `/api/markets` | `platform.configure` (Administrator) | Tạo thị trường; trả 201, Location và ETag |
| GET | `/api/markets?q=&page=&pageSize=` | Tài khoản đang hoạt động, role và tổ chức hợp lệ | Tìm mã/tên; mặc định 20 dòng/trang, tối đa 100 |
| GET | `/api/markets/{id}` | Như GET danh sách | Chi tiết và ETag; không có trả 404 |
| PATCH | `/api/markets/{id}` | `platform.configure`, `If-Match` | Chỉ sửa tên; trả ETag mới |

POST body: `{"code":"EU","name":"European Union"}`. PATCH body: `{"name":"European Union (EU)"}`. API trim, viết hoa mã và giới hạn 40 ký tự; mã không đổi sau khi tạo. Mã trùng trả 409. PATCH thiếu `If-Match` trả 428, ETag cũ trả 412. Tạo/sửa thành công ghi `catalog_events` cùng giao dịch. Chưa có DELETE cho markets.

Migration `20261012071731_AddMarkets` chỉ tạo `catalog.markets` và chỉ mục unique cho `code`; không thay Identity hoặc Capture. Migration đã áp dụng lên Supabase dev ngày 12/10/2026. Khởi động lại API để Scalar hiển thị route mới.

## Hoạt chất (`active-substances`)

| Method | Route | Quyền | Kết quả |
| --- | --- | --- | --- |
| POST | `/api/active-substances` | `platform.configure` (Administrator) | Tạo hoạt chất, trả 201, Location và ETag |
| GET | `/api/active-substances?q=&page=&pageSize=` | Tài khoản đang hoạt động, có role và tổ chức hợp lệ | Tìm theo mã/tên; mặc định trang 1, 20 dòng; tối đa 100 dòng |
| GET | `/api/active-substances/{id}` | Như GET danh sách | Chi tiết và ETag; không có trả 404 |
| PATCH | `/api/active-substances/{id}` | `platform.configure`; yêu cầu `If-Match` | Sửa tên, giữ nguyên mã; trả ETag mới |
| DELETE | `/api/active-substances/{id}` | `platform.configure`; yêu cầu `If-Match` | Xóa hoạt chất chưa được tham chiếu; trả 204 |

POST body:

```json
{
  "code": "SUBSTANCE-001",
  "name": "Tên hoạt chất"
}
```

PATCH body:

```json
{
  "name": "Tên đã hiệu chỉnh"
}
```

API bỏ khoảng trắng đầu/cuối và chuẩn hóa `code` thành chữ hoa trước khi lưu. Mã là duy nhất trong danh mục chung, không đổi sau khi tạo. Tên không được để trống. Danh sách trả `items`, `page`, `pageSize`, `hasMore`.

Khi sửa hoặc xóa, trước tiên GET chi tiết và sao chép header `ETag`, ví dụ `"123"`, vào header `If-Match` của PATCH/DELETE. Thiếu header trả 428; ETag cũ trả 412. Mã trùng trả 409. DELETE không có body; nếu hoạt chất đã được bản ghi khác tham chiếu qua FK, API trả 409 `ACTIVE_SUBSTANCE_IN_USE`. Mọi lần tạo/sửa/xóa thành công đều ghi `catalog_events` trong cùng transaction; request thất bại không để lại event. Nhật ký đã ghi không bị xóa theo hoạt chất.

Trong Scalar, đăng nhập bằng tài khoản Administrator rồi chọn Authentication → Bearer và nhập `accessToken`. Dùng tài khoản PurchasingStaff để thử GET; tài khoản này không được POST/PATCH/DELETE. Bearer token không cần CSRF. Nếu dùng cookie trong trình duyệt để ghi, cần `X-CSRF-TOKEN` từ `GET /api/auth/csrf` theo cơ chế Identity hiện có.

Chạy migration riêng cho Catalog trên database mục tiêu đã được xác nhận:

```powershell
dotnet run --project backend/src/GrainLedger.Api -- --migrate-catalog
```

Sau đó khởi động lại API rồi mở `http://localhost:5080/scalar/v1`. Lệnh này chỉ gọi `CatalogDbContext.Database.MigrateAsync()`; nó không chạy migration Identity hoặc Capture. Migration bước 1 tạo hai bảng trên và FK từ `catalog_events.actor_id` đến `identity.users.Id`, nên database cần có schema Identity trước. Migration `20261012062315_InitialCatalog` đã được áp dụng lên Supabase dev cấu hình trong `.env` ngày 12/10/2026; lệnh trên dùng khi thiết lập database khác.

Kiểm thử tích hợp dùng PostgreSQL Testcontainers:

```powershell
dotnet test backend/tests/GrainLedger.Api.IntegrationTests/GrainLedger.Api.IntegrationTests.csproj -c Release --filter FullyQualifiedName~ActiveSubstanceTests
```

Docker Desktop cần đang chạy. Bốn test `ActiveSubstanceTests`, hai test `MarketTests` và hai test `CommodityTests` đã đạt trên PostgreSQL Testcontainers ngày 12/10/2026. Các API danh mục khác và bảng MRL sẽ được làm ở các bước sau; chưa cần cấp thêm `mrl.read` cho CustomerManager ở bước này.
