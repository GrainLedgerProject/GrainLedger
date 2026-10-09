# Đăng nhập email/password, OTP và test API bằng Scalar

## Phạm vi thay đổi

Backend .NET 10 vẫn là modular monolith, dùng PostgreSQL. Module Identity bổ sung đăng ký email/password, xác minh email bằng OTP, đăng nhập trả JWT, refresh token, logout, quên mật khẩu và đặt lại mật khẩu. Luồng Google và các API profile/phân quyền hiện có được giữ lại.

Scalar là giao diện test trực tiếp API backend. Giao diện React hỗ trợ đầy đủ đăng ký, xác minh email, gửi lại OTP, đăng nhập email/password, quên mật khẩu, đặt lại mật khẩu và đăng xuất. Luồng Google/cookie vẫn hoạt động song song.

## Cấu hình `.env`

Các thông tin cũ về database và Google giữ nguyên. Bổ sung:

```dotenv
Authentication__Jwt__SigningKey='CHUOI_NGAU_NHIEN_BI_MAT_IT_NHAT_32_KY_TU'
Authentication__Jwt__Issuer='GrainLedger'
Authentication__Jwt__Audience='GrainLedger.Api'

Mail__FromAddress='no-reply@your-domain.example'
Mail__FromName='GrainLedger'
Mail__Smtp__Host='smtp.your-provider.example'
Mail__Smtp__Port='587'
Mail__Smtp__Username='your-smtp-username'
Mail__Smtp__Password='your-smtp-password'
Mail__Smtp__UseStartTls='true'
```

Đây là giá trị minh họa, không phải máy chủ gửi mail có sẵn. Phải dùng SMTP thật của bạn để OTP đến hộp thư. `FromAddress` phải là địa chỉ được nhà cung cấp cho phép gửi. Tên hiển thị người gửi là **GrainLedger**; chỉ cấu hình tên hiển thị không tạo được địa chỉ email hoặc quyền gửi từ tên miền GrainLedger.

Bạn hiện chưa có SMTP, nên phần này được chuẩn bị sẵn để cấu hình sau. Khi thiếu SMTP, các API cần gửi email trả `503`; không có mã OTP mặc định hay đường tắt xác minh trong ứng dụng. Có thể chạy integration tests với mailbox giả lập mà không cần tài khoản SMTP.

`UseStartTls=true` thường dùng với cổng 587. `UseStartTls=false` dùng TLS ngay khi kết nối (thường cổng 465); không có chế độ gửi mật khẩu SMTP qua kết nối rõ.

Trong `.env` local, đã thêm các key còn thiếu và tạo khóa JWT ngẫu nhiên nếu trước đó chưa có key. Không ghi đè thông tin đã tồn tại. SMTP vẫn cần bạn điền. Không đưa khóa JWT hoặc SMTP password vào `VITE_`, source code hay tài liệu. Backend cần restart sau khi sửa `.env`.

Khóa JWT phải giữ ổn định giữa các lần restart. Đổi khóa làm token cũ mất hiệu lực. Các biến môi trường của process vẫn có độ ưu tiên cao hơn `.env`.

## Khởi động

```bash
docker compose up -d postgres
dotnet run --project backend/src/GrainLedger.Api -- --migrate
dotnet run --project backend/src/GrainLedger.Api
```

Phải chạy migration mới trước khi test các bảng/cột xác thực bổ sung. Không xóa volume hoặc database cũ để áp dụng thay đổi này.

Mở **http://localhost:5080/scalar/v1**. OpenAPI JSON ở **http://localhost:5080/openapi/v1.json**. Các trang này chỉ được bật ở Development và môi trường integration test, không tự công khai ở Production.

Để dùng giao diện React:

```bash
npm run dev --workspace @grainledger/web
```

Mở địa chỉ Vite in ra (mặc định **http://localhost:5173**). Form đăng nhập cho phép chọn email/password hoặc Google. Chọn **Đăng ký** để tạo tài khoản; giao diện tự chuyển sang bước nhập OTP và khóa nút gửi lại trong 60 giây. Chọn **Quên mật khẩu?** để yêu cầu mã và đặt mật khẩu mới. Các form dùng thuộc tính autocomplete phù hợp cho trình quản lý mật khẩu và OTP.

Nếu backend trả `503` khi đăng ký hoặc quên mật khẩu, giao diện báo dịch vụ email chưa sẵn sàng. Cấu hình SMTP theo phần trên và restart backend; ứng dụng không hiển thị hoặc ghi log OTP.

## 1. Đăng ký và xác minh OTP

Gọi `POST /api/auth/register`:

```json
{
  "email": "your-real-email@example.com",
  "password": "Use-A-Long-Unique-Password!2026",
  "displayName": "Nguyen Van A"
}
```

Lưu `registrationId` từ response, sau đó lấy OTP từ email GrainLedger gửi. Gọi `POST /api/auth/verify-email`:

```json
{
  "email": "your-real-email@example.com",
  "registrationId": "UUID_NHAN_TU_REGISTER",
  "otp": "123456"
}
```

`123456` chỉ minh họa; phải nhập mã thật trong email. API không trả OTP trong response. Chưa xác minh email thì chưa được login.

`registrationId` gắn OTP với đúng lần đăng ký. Không lấy mã từ một lần đăng ký rồi dùng cho lần khác. Nếu đăng ký lại, phải dùng registrationId và email OTP mới nhất.

Để gửi lại mã, gọi `POST /api/auth/resend-verification` với `email` và `registrationId`. Có thời gian chờ giữa hai lần gửi; mã cũ không còn dùng được sau khi mã mới được cấp.

## 2. Login lấy token

Gọi `POST /api/auth/login`:

```json
{
  "email": "your-real-email@example.com",
  "password": "Use-A-Long-Unique-Password!2026"
}
```

Response thành công gồm:

```json
{
  "accessToken": "JWT_ACCESS_TOKEN",
  "refreshToken": "OPAQUE_REFRESH_TOKEN",
  "tokenType": "Bearer",
  "expiresIn": 900,
  "user": {
    "id": "USER_ID",
    "email": "your-real-email@example.com",
    "displayName": "Nguyen Van A",
    "role": null,
    "permissions": []
  }
}
```

Ví dụ rút gọn phần `user`; response thực tế còn thông tin profile và tổ chức.

Trong mục **Authentication** của Scalar, chọn **Bearer**, dán riêng giá trị `accessToken`. Không dán JSON, không dán refreshToken. Scalar sẽ thêm:

```http
Authorization: Bearer JWT_ACCESS_TOKEN
```

Test `GET /api/auth/session` hoặc `GET /api/profile`: phải trả thông tin của tài khoản vừa đăng nhập. Có thể sửa profile bằng `PUT /api/profile`:

```json
{
  "displayName": "Nguyen Van A Updated",
  "phoneNumber": "+84901234567"
}
```

Request xác thực thành công bằng Bearer **không cần CSRF token**. Nếu dùng Google cookie như giao diện React thì các request thay đổi dữ liệu vẫn phải có CSRF. Chỉ thêm một header Bearer sai không giúp bỏ qua kiểm tra bảo mật hoặc dùng ké cookie.

## 3. Refresh token

Khi access token hết hạn, gọi `POST /api/auth/refresh`:

```json
{ "refreshToken": "REFRESH_TOKEN_MOI_NHAT" }
```

Lưu **cả accessToken và refreshToken mới** từ response, cập nhật Bearer trong Scalar. Refresh token được đổi sau mỗi lần sử dụng; không gửi lại token cũ. Việc dùng lại token đã đổi sẽ thu hồi session liên quan. Đừng gọi refresh đồng thời từ nhiều tab bằng cùng token.

Access token có hạn 15 phút. Refresh token mới có hạn 30 ngày tính từ lúc cấp. Refresh thành công không thu hồi ngay access token trước đó; token đó còn dùng được đến khi hết hạn hoặc cả chuỗi session bị thu hồi. Logout từ bất kỳ access token còn hiệu lực nào trong chuỗi sẽ thu hồi cả chuỗi. Logout, reset password, khóa tài khoản hoặc đổi quyền có thể làm token mất hiệu lực sớm hơn thời hạn đó.

Giao diện React giữ access token và refresh token của đăng nhập email/password trong bộ nhớ của tab, không ghi refresh token vào `localStorage` hay `sessionStorage`. Khi nhiều request cùng nhận `401`, giao diện chỉ thực hiện một lần refresh, lưu cả cặp token mới rồi thử lại request đúng một lần. Refresh thất bại sẽ xóa phiên local và đưa người dùng về màn hình đăng nhập; không có vòng lặp retry. Request Bearer không lấy hoặc gửi CSRF token. Request dùng cookie Google vẫn lấy CSRF mới trước các thao tác thay đổi dữ liệu.

Đổi lại, tải lại trang sẽ mất phiên email/password trong bộ nhớ và người dùng cần đăng nhập lại. Phiên Google dùng cookie vẫn có thể được khôi phục bằng `GET /api/auth/session`. Đây là giới hạn chủ ý để refresh token không tồn tại lâu trong browser storage; nếu sau này cần giữ đăng nhập qua reload, nên chuyển refresh token sang cookie `HttpOnly`, `Secure`, `SameSite` do backend quản lý thay vì lưu bằng JavaScript.

## 4. Logout

- `POST /api/auth/logout` với Bearer: kết thúc session hiện tại, thu hồi access/refresh token của session đó.
- `POST /api/auth/logout-all` với Bearer: kết thúc mọi session của tài khoản.

Sau logout, gọi `GET /api/profile` bằng token cũ phải nhận `401`. Backend kiểm tra trạng thái session trong database, không chỉ chữ ký và hạn dùng của JWT.

## 5. Forgot password và reset password

Gọi `POST /api/auth/forgot-password`:

```json
{ "email": "your-real-email@example.com" }
```

Response dùng thông báo chung để không tiết lộ email nào có tài khoản. Nếu đây là tài khoản local đủ điều kiện, GrainLedger gửi OTP đặt lại mật khẩu. Sau đó gọi `POST /api/auth/reset-password`:

```json
{
  "email": "your-real-email@example.com",
  "otp": "123456",
  "newPassword": "Another-Long-Unique-Password!2026"
}
```

OTP reset password và OTP xác minh đăng ký là hai mục đích riêng, không dùng thay nhau. Reset thành công không tự đăng nhập: hãy gọi login bằng mật khẩu mới. Tất cả session cũ và các mã còn hiệu lực của tài khoản bị thu hồi.

## Admin và phân quyền

Đăng ký không cho phép tự chọn role hoặc organization. Tài khoản đã xác minh có thể login và quản lý profile, nhưng vẫn chờ admin cấp quyền nghiệp vụ.

`Bootstrap__AdminEmail` chỉ dùng tạo admin đầu tiên. Đối với email/password, quyền admin bootstrap chỉ được cấp **sau khi xác minh email**, nếu chưa có admin. Nếu hệ thống đã có admin, người đó cấp quyền cho các thành viên khác; có thể có nhiều admin.

Khi gọi `PUT /api/admin/users/{id}/access`, ngoài Bearer admin còn phải có header `If-Match` bằng `etag` mới nhất của user lấy từ `GET /api/admin/users`. Scalar có ô nhập header này. Đổi quyền làm session của tài khoản được sửa mất hiệu lực.

Google và email/password không tự gộp tài khoản chỉ vì cùng email. Forgot password không được dùng để thêm mật khẩu vào tài khoản chỉ đăng nhập Google. Cần một luồng liên kết tài khoản có xác thực riêng nếu sau này muốn dùng chung một profile cho hai cách login.

## Các kiểm tra bảo mật và lỗi thường gặp

- Mật khẩu dài 12–128 ký tự, có chữ hoa, chữ thường, chữ số và ký tự đặc biệt. Mật khẩu được hash; OTP được bảo vệ bằng hash có khóa; database không lưu refresh token dạng rõ.
- OTP hết hạn sau 10 phút, tối đa 5 lần nhập sai; gửi lại có thời gian chờ 60 giây.
- Login sai 5 lần bị khóa tạm 15 phút; có thêm giới hạn tốc độ theo IP.
- `400`: dữ liệu, OTP hoặc CSRF không hợp lệ. Đọc ProblemDetails trong response.
- `401`: sai thông tin đăng nhập hoặc token hết hạn/đã thu hồi/không hợp lệ.
- `403`: chưa được cấp quyền gọi endpoint đó.
- `412`: `If-Match` cũ, cần lấy lại user/etag.
- `429`: vượt giới hạn thử/gửi mã, cần chờ.
- `503`: thiếu cấu hình JWT/SMTP hoặc không gửi được email; kiểm tra cấu hình và log backend.

## Kiểm thử

```bash
dotnet test GrainLedger.slnx
npm test --workspace @grainledger/web
npm run build --workspace @grainledger/web
```

Integration tests dùng PostgreSQL riêng qua Testcontainers và mailbox giả lập trong test; không gửi email đến người thật. Scalar/OpenAPI được kiểm tra có Bearer security scheme và đúng yêu cầu xác thực cho từng endpoint. SMTP thật và khả năng thư vào Inbox còn phụ thuộc nhà cung cấp, thông tin tài khoản gửi, cấu hình domain và bộ lọc spam.

Kết quả kiểm tra ngày 09/10/2026: toàn bộ **31 integration tests passed**, gồm test hồi quy logout không ảnh hưởng tài khoản khác khi trùng family ID. Quét package NuGet của backend (gồm dependency gián tiếp) không phát hiện lỗ hổng đã biết tại thời điểm kiểm tra. Đã kiểm tra Scalar hiển thị ô Bearer token trên trình duyệt. Chưa kiểm thử gửi mail thật vì chưa có SMTP.

Frontend có test cho đăng ký và chuyển bước OTP, liên kết `registrationId`, cooldown gửi lại, response `202` không body của forgot password, thông báo `503`, cookie/CSRF, Bearer refresh singleflight, retry bằng token đã xoay và cô lập response đến muộn từ phiên cũ. Build TypeScript/Vite cũng được kiểm tra.

Migration mới cấp family ID riêng cho từng session Google cũ; không gom chung các phiên cũ khi logout. Chưa áp dụng migration lên database làm việc của bạn trong đợt này; chạy lệnh `--migrate` ở trên trước khi dùng API mới.

Không thực hiện thao tác Git trong đợt triển khai này.
