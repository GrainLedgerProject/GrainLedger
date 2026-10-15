# Tài khoản local mẫu cho Development

`--seed-dev` seeds six local Identity accounts without OTP; `--seed-ledger` also creates demo mill setup and Ledger stock through official APIs. Use only a development/test database you own and control; Development environment alone does not prove database is isolated. Confirm `ConnectionStrings__GrainLedger` before running. Accounts are in `identity`, not Supabase Auth.

## 1. Cấu hình

Trong `.env` ở thư mục gốc, giữ connection string PostgreSQL của Supabase và thêm:

```dotenv
DevelopmentSeed__Password='THAY_BANG_MAT_KHAU_TEST_CUA_BAN'
```

Giá trị trên là placeholder. Chọn mật khẩu 12–128 ký tự, có chữ hoa, chữ thường, số và ký tự đặc biệt. Sáu tài khoản dùng cùng mật khẩu bạn chọn lúc tạo; database chỉ lưu hash. Không cần SMTP cho seed hoặc login. Login vẫn cần `Authentication__Jwt__SigningKey` đã cấu hình.

## 2. Chạy từ thư mục gốc project

```bash
dotnet run --project backend/src/GrainLedger.Api --no-launch-profile -- --environment Development --migrate
dotnet run --project backend/src/GrainLedger.Api --no-launch-profile -- --environment Development --seed-dev
```

Migration updates schema; either seed command creates accounts then exits. Choose `--seed-dev` for accounts only or `--seed-ledger` for the complete demo below; do not combine flags or run migration implicitly. Use only a database you own/control. Process environment overrides `.env`. Do not silently start a replacement local database or alter existing connection strings.

## 3. Capture và Ledger

For demo mill setup and stock, after an explicit migration:

```bash
dotnet run --project backend/src/GrainLedger.Api --no-launch-profile -- --environment Development --seed-ledger
```

Requires the restricted runtime connection and `DevelopmentSeed__Password`. It creates missing accounts automatically; running `--seed-dev` first is unnecessary. Existing demo accounts must retain their expected roles, organization, active/unlocked state and the configured password. Conflicting accounts cause an error, never a password/role reset.

Seed contents:
- One `GrainLedger Demo Mill (synthetic)` facility, receiving/dry stores and isolation location.
- Dryer and mill equipment, with demo MillManager/MillOperator assigned to this facility.
- Facility-scoped, evidence-backed **synthetic 0% DRYING policy**, explicitly not factory-approved. Other processing policies and blend compatibility remain unconfigured and fail closed.
- Catalog `PADDY` and `KG` MASS prerequisites, plus a synthetic farmer, plot and 2026 crop season/plot-season.
- One official 600 kg procurement followed by an exact complete operator arrival: `RECEIVED_MATCHED`, 600 kg `RAW_PADDY`, immutable receipt posting and source lineage.

The command briefly hosts the normal authenticated APIs on an ephemeral loopback-only port, then shuts down. Its temporary signing key is never persisted; normal browser/API login still needs configured JWT settings. No migration-owner credential, direct Ledger table writes, retired MockCapture or historical conversion is used.

Reruns use stable demo identifiers, preserve setup edits and existing receipt/arrival decisions, and never refill transferred or consumed stock. Retired/conflicting demo records are not reset or restored automatically; resolve any reported prerequisite conflict before rerunning. Each request commits normally, so interrupted seeds can leave partial setup; rerun resumes without wiping it.


Official Capture supports farmer, plot, crop season, plot-season and procurement APIs. Procurement registration records source expectation only and creates no usable Ledger stock. Assigned-facility operator records one complete arrival measurement set; exact match posts actual mass, while mismatch stays without stock until manager evidence review. Managers may approve eligible shortages at actual mass, append a remeasurement request, or keep unresolved shortage/surplus blocked. See [procurement API](procurement-api.md) for endpoints and payloads.

One procurement means one complete arrival; register separate procurement per load. No truck model, customer settlement, tolerance, partial arrival, or surplus acceptance. Zero/full-loss arrival is unsupported and fails closed. Existing historical source evidence and stock are grandfathered unchanged; migration `20261014190000_PhysicalReceiving` does not retroactively freeze or reinterpret legacy stock. Do not use retired synthetic capture workflows or `--seed-mock`.

Trước mọi migration, seed, thay đổi schema/role/grant hoặc kiểm tra có ghi dữ liệu trên Supabase dùng chung, cần phê duyệt rõ ràng từ chủ database. Ưu tiên Testcontainers cô lập; không thay đổi `.env` hay connection string hiện có.

## 4. Đăng nhập

| Email | Role | Tổ chức |
|---|---|---|
| admin@grainledger.local | Administrator | Nhà máy hệ thống |
| mill.manager@grainledger.local | MillManager | Nhà máy hệ thống |
| mill.operator@grainledger.local | MillOperator | Nhà máy hệ thống |
| customer.manager@grainledger.local | CustomerManager | GrainLedger Demo Customer |
| export@grainledger.local | ExportStaff | GrainLedger Demo Customer |
| purchasing@grainledger.local | PurchasingStaff | GrainLedger Demo Customer |

Seed dùng nhà máy đang có; nếu chưa có thì tạo `GrainLedger Mill`. Chỉ tạo một công ty khách hàng mẫu. Không xóa tài khoản/tổ chức có sẵn.

```bash
dotnet run --project backend/src/GrainLedger.Api
```

Mở Scalar tại `http://localhost:5080/scalar/v1`, gọi `POST /api/auth/login` với email trong bảng và mật khẩu đã chọn. Dán `accessToken` vào Bearer để test endpoint khác. Hoặc chạy `npm run dev` và dùng form đăng nhập frontend.

## Chạy lại và giới hạn

- Dữ liệu được lưu lâu dài trong Supabase; không seed lại khi restart backend.
- Chạy lại bỏ qua email local đã tồn tại, không đổi password, role hoặc trạng thái của tài khoản đó. Vì vậy đổi `DevelopmentSeed__Password` không reset mật khẩu đã seed.
- Nếu email đã tồn tại trước lần seed đầu, tài khoản đó cũng được giữ nguyên. Con số tài khoản mới tạo có thể ít hơn 6.
- Lệnh bị chặn ngoài môi trường Development. Không có endpoint HTTP tạo tài khoản mẫu.
- Địa chỉ `@grainledger.local` là định danh test, không phải hộp thư thật. Không dùng forgot-password/OTP qua email cho các tài khoản này.
- Không commit `.env` hoặc chia sẻ mật khẩu seed công khai.

Historical report only: earlier seed/browser checks used a previous implementation and do not establish current Capture, arrival or correction behavior. Documentation owner ran no tests or seed and made no shared Supabase change.
