# F03: API danh mục hoạt chất, thị trường, commodity, chỉ tiêu chất lượng, giống lúa và đơn vị đo

Module `GrainLedger.Modules.Catalogs` sở hữu `catalog.active_substances` và `catalog.catalog_events`. Đây là danh mục dùng chung để Capture chọn hoạt chất khi khai báo phun thuốc; giới hạn MRL theo thị trường sẽ nằm ở các API F03 tiếp theo.

## Đơn vị đo (`measurement-units`)

Danh mục này định nghĩa đơn vị và loại đại lượng; không tự quy đổi số đo hoặc thêm FK vào các bản ghi nghiệp vụ cũ.

| Method | Route | Quyền | Kết quả |
| --- | --- | --- | --- |
| POST | `/api/measurement-units` | `platform.configure` (Administrator) | Tạo; trả 201, Location và ETag |
| GET | `/api/measurement-units?q=&page=&pageSize=&sortBy=&sortDirection=` | Tài khoản đang hoạt động, role và tổ chức hợp lệ | Tìm mã/tên; mặc định 25 dòng/trang, tối đa 100 |
| GET | `/api/measurement-units/{id}` | Như GET danh sách | Chi tiết gồm loại đại lượng và ETag; không có trả 404 |
| PATCH | `/api/measurement-units/{id}` | `platform.configure`, `If-Match` | Chỉ sửa tên; trả ETag mới |
| DELETE | `/api/measurement-units/{id}` | `platform.configure`, current creator, `If-Match` | Soft-delete unused unit; returns 204 |

POST body: `{"code":"KG","name":"Kilogram","quantityKind":"MASS"}`. PATCH body: `{"name":"Kilogram (kg)"}`. Mã tối đa 30 ký tự, được trim, viết hoa và phải duy nhất. `quantityKind` được chuẩn hóa chữ hoa và hiện chỉ nhận `MASS`, `AREA`, `RESIDUE_CONCENTRATION`, `APPLICATION_RATE`, `FRACTION`. Mã và loại đại lượng không đổi sau khi tạo; cần ý nghĩa khác thì tạo đơn vị mới. Mã trùng trả 409; PATCH/DELETE thiếu `If-Match` trả 428, ETag cũ trả 412. Sửa/xóa yêu cầu current creator; unit đang được procurement, spray hoặc MRL history tham chiếu trả 409 `MEASUREMENT_UNIT_IN_USE`. Delete chỉ đánh `is_deleted`; dữ liệu và mã vẫn được giữ, ẩn khỏi danh sách/selector, không có restore. Thành công ghi `catalog_events` cùng giao dịch.

Migration `20261012175054_AddMeasurementUnits` creates `catalog.measurement_units`; `20261014120000_AddCatalogAttributionAndCommodityArchive` adds creator and soft-delete state. Do not assume migration is applied to target. Shared Supabase migration requires explicit database-owner approval. Normal application startup uses runtime credentials and does not migrate or seed.

## Giống lúa (`rice-varieties`)

Danh mục giống lúa dùng chung để giao diện chọn giống khi ghi thông tin vụ mùa. `plot_seasons.rice_variety` vẫn là bản ghi tên giống dạng text tại thời điểm ghi nhận; API này chưa thêm FK hay viết lại dữ liệu Capture cũ.

| Method | Route | Quyền | Kết quả |
| --- | --- | --- | --- |
| POST | `/api/rice-varieties` | `platform.configure` (Administrator) | Tạo; trả 201, Location và ETag |
| GET | `/api/rice-varieties?q=&page=&pageSize=` | Tài khoản đang hoạt động, role và tổ chức hợp lệ | Tìm mã/tên; mặc định 20 dòng/trang, tối đa 100 |
| GET | `/api/rice-varieties/{id}` | Như GET danh sách | Chi tiết và ETag; không có trả 404 |
| PATCH | `/api/rice-varieties/{id}` | `platform.configure`, `If-Match` | Chỉ sửa tên; trả ETag mới |

POST body: `{"code":"ST25","name":"ST25"}`. PATCH body: `{"name":"Tên giống đã hiệu chỉnh"}`. Code được trim, viết hoa, giới hạn 40 ký tự, duy nhất và không đổi sau khi tạo. Mã trùng trả 409; PATCH thiếu `If-Match` trả 428, ETag cũ trả 412. Tạo/sửa thành công ghi `catalog_events` cùng giao dịch. Chưa có DELETE.

Migration `20261012163647_AddRiceVarieties` chỉ tạo `catalog.rice_varieties` và unique index cho `code`; không thay Identity hoặc Capture. Migration đã áp dụng lên Supabase dev ngày 12/10/2026. Khởi động lại API để Scalar hiển thị route mới.

## Chỉ tiêu chất lượng (`quality-attributes`)

Danh mục này định nghĩa tên và đơn vị của chỉ tiêu; không lưu giá trị đo của từng lô và không phải bảng giới hạn MRL.

| Method | Route | Quyền | Kết quả |
| --- | --- | --- | --- |
| POST | `/api/quality-attributes` | `platform.configure` (Administrator) | Tạo; trả 201, Location và ETag |
| GET | `/api/quality-attributes?q=&page=&pageSize=` | Tài khoản đang hoạt động, role và tổ chức hợp lệ | Tìm mã/tên; mặc định 20 dòng/trang, tối đa 100 |
| GET | `/api/quality-attributes/{id}` | Như GET danh sách | Chi tiết gồm đơn vị và ETag; không có trả 404 |
| PATCH | `/api/quality-attributes/{id}` | `platform.configure`, `If-Match` | Chỉ sửa tên; trả ETag mới |

POST body: `{"code":"MOISTURE","name":"Moisture content","unit":"%"}`. PATCH body: `{"name":"Moisture content (rice)"}`. Code tối đa 60 ký tự, unit tối đa 30 ký tự; cả hai được trim. Code được viết hoa và phải duy nhất. Code và unit không được sửa sau khi tạo: nếu cần đơn vị khác, tạo chỉ tiêu mới. Mã trùng trả 409; PATCH thiếu `If-Match` trả 428, ETag cũ trả 412. Tạo/sửa thành công ghi `catalog_events` cùng giao dịch. Chưa có DELETE.

Migration `20261012163146_AddQualityAttributes` chỉ tạo `catalog.quality_attributes` và unique index cho `code`; đã áp dụng lên Supabase dev ngày 12/10/2026. Khởi động lại API để Scalar hiển thị route mới.

## Loại/dạng sản phẩm (`commodities`)

Commodity là loại hoặc dạng sản phẩm được áp dụng giới hạn MRL; không phải giống lúa. Danh mục này dùng chung cho các công ty.

| Method | Route | Quyền | Kết quả |
| --- | --- | --- | --- |
| POST | `/api/commodities` | `platform.configure` (Administrator) | Tạo; trả 201, Location và ETag |
| GET | `/api/commodities?q=&page=&pageSize=&sortBy=&sortDirection=` | Tài khoản đang hoạt động, role và tổ chức hợp lệ | Tìm mã/tên; mặc định 25 dòng/trang, tối đa 100 |
| GET | `/api/commodities/{id}` | Như GET danh sách | Chi tiết và ETag; không có trả 404 |
| DELETE | `/api/commodities/{id}` | `platform.configure`, current creator, `If-Match` | Archive unused commodity; returns 204 |
| PATCH | `/api/commodities/{id}` | `platform.configure`, `If-Match` | Chỉ sửa tên; trả ETag mới |

POST body: `{"code":"RICE","name":"Milled rice"}`. PATCH body: `{"name":"Milled white rice"}`. API trims, uppercases code and limits it to 40 characters; code cannot change after creation. Duplicate code returns 409. PATCH/DELETE require `If-Match`: missing returns 428, stale ETag returns 412. Successful create/update/archive writes `catalog_events` in same transaction. Archive requires current creator; referenced commodity returns 409 `COMMODITY_IN_USE`. Archive is soft: row and code remain reserved, while active lists/details/selectors hide it. No restore endpoint.

Migration `20261012074031_AddCommodities` creates `catalog.commodities`; `20261014120000_AddCatalogAttributionAndCommodityArchive` adds commodity creator/archive state and measurement-unit creator/deleted state. Do not assume either migration is applied to a target database. Shared Supabase migration requires explicit database-owner approval. Normal application startup uses runtime credentials and does not migrate or seed.

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

Docker Desktop cần đang chạy. Mười bốn test của `ActiveSubstanceTests`, `MarketTests`, `CommodityTests`, `QualityAttributeTests`, `RiceVarietyTests` và `MeasurementUnitTests` đã đạt trên PostgreSQL Testcontainers ngày 13/10/2026. Bảng MRL sẽ được làm ở các bước sau; chưa cần cấp thêm `mrl.read` cho CustomerManager ở bước này.
