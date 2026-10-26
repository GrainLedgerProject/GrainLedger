# Auth QA — 10/10/2026

## Kết luận

Không phát hiện lỗi chặn PR trong phạm vi kiểm thử bên dưới. Đây là kết quả kiểm tra chức năng, không phải cam kết hệ thống không còn lỗi hoặc đã sẵn sàng production.

Baseline: branch `enhance/authen`, commit `6f3f397`. Bổ sung `AuthAcceptanceTests.cs` với 9 trường hợp hồi quy; không đổi code nghiệp vụ trong lượt QA này.

## Kết quả tự động

| Kiểm tra | Kết quả |
|---|---|
| `dotnet test GrainLedger.slnx --no-restore --verbosity minimal` | 42/42 pass |
| `npm test --workspace @grainledger/web` | 18/18 pass |
| `npm run build --workspace @grainledger/web` | TypeScript + Vite pass |
| `git diff --check` | Pass |

Backend chạy PostgreSQL Testcontainers riêng, SMTP giả lập và Google backchannel giả lập. Frontend component tests dùng mock fetch.

Các ca đã kiểm tra:

- Register, OTP gắn đúng registrationId, xác minh, hết hạn và giới hạn 5 lần sai; lỗi gửi mail rollback dữ liệu.
- Login trả token/user, sai password và khóa tạm; Google/local cùng email không tự gộp.
- Refresh rotation, reuse thu hồi chuỗi token, race giữa refresh và logout; JWT sai/hết hạn không dùng ké cookie.
- Logout và logout-all; reset password thu hồi session cũ.
- Ca bổ sung: reset code hết hạn, 5 lần sai và replay sau khi thành công đều bị từ chối.
- Ca bổ sung: vô hiệu hóa local account chặn cả access token, refresh token và login mới.
- Ca bổ sung: đủ 6 role login/read own profile thành công; chỉ Administrator được truy cập users/organizations/roles administration, 5 role khác nhận 403.
- Ca bổ sung: logout-all thu hồi access/refresh của hai phiên local độc lập.
- Profile không sửa được quyền; cookie mutation cần CSRF, Bearer mutation không cần CSRF.
- Admin đổi quyền có If-Match, kiểm tra loại tổ chức, thu hồi phiên và bảo vệ admin cuối cùng; kiểm tra tenant scope ở service.
- Google middleware callback/state/verified email; Scalar/OpenAPI Bearer metadata.
- Seed tạo 6 tài khoản, đúng nhà máy/công ty, login được và chạy lại không ghi đè; chặn Production/password yếu.
- Frontend: register/verify, cooldown, login/profile/logout, forgot/reset, SMTP503, refresh singleflight và phản hồi cũ không ghi đè phiên mới.

## Kiểm tra trình duyệt với backend thật

Dùng bản frontend production build phục vụ bởi .NET ở cổng 5088, database PostgreSQL Docker tạm riêng. Không sử dụng connection string, SMTP hay Google credentials của người dùng.

1. Chạy migration và seed CLI thành công: 6 user, 1 mill, 1 demo customer.
2. Login `admin@grainledger.local` bằng form thành công.
3. Đổi tên profile; sidebar và màn hình quản trị hiển thị tên mới.
4. Administration hiển thị đủ 6 tài khoản và tổ chức tương ứng.
5. Logout quay về form login. Đối chiếu database: không còn session chưa thu hồi.
6. Mobile width 390px: trang login không tràn ngang; form register có thể thao tác.
7. Register khi SMTP bị tắt có chủ đích: API503 và thông báo dịch vụ email chưa sẵn sàng. Database vẫn có 6 user và 0 registration attempts.

## Giới hạn và lưu ý cho PR

- Chưa gửi email SMTP thật hoặc đăng nhập Google thật trong lượt này; cần smoke test bằng cấu hình môi trường triển khai trước release.
- Không ghi dữ liệu vào Supabase của người dùng. Chưa kiểm chứng connection pooling/network của Supabase deployment.
- Token email/password giữ trong bộ nhớ: reload cần login lại (hành vi hiện tại đã được thông báo trong UI).
- Auth UI tiếng Việt, profile/admin tiếng Anh: chưa đồng nhất ngôn ngữ.
- Địa chỉ `@grainledger.local` không có hộp thư thật; dùng seed/login, không dùng kiểm tra email delivery.
- Các module nghiệp vụ tương lai phải áp dụng tenant filtering riêng; test service scope không chứng minh mọi business endpoint tương lai đã cách ly dữ liệu.
- Chưa làm load test, penetration test, kiểm thử đa trình duyệt đầy đủ hoặc toàn bộ luồng OTP qua trình duyệt với SMTP thật.

Database/container và backend tạm dùng cho browser smoke được dọn sau kiểm tra. Không commit, push hoặc tạo PR trong lượt QA này.
