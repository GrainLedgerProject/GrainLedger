# Tài khoản local mẫu cho Development

Lệnh seed tạo 6 tài khoản local, không cần gửi OTP, dùng backend .NET để login. Đây là dữ liệu trong schema `identity`, không phải Supabase Auth. Chỉ chạy lệnh trên database dành cho development/test. Môi trường Development không tự chứng minh database là database test: hãy kiểm tra `ConnectionStrings__GrainLedger` trước khi chạy.

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

Migration cập nhật schema; seed tạo dữ liệu rồi thoát. Lệnh dùng database trong cấu hình backend. Biến môi trường của process có ưu tiên cao hơn `.env`. Không tự khởi động database local nếu bạn đang dùng Supabase.

## 3. Đăng nhập

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

Đã kiểm thử seed và đăng nhập trên database Testcontainers riêng; chưa chạy seed vào Supabase của bạn.
