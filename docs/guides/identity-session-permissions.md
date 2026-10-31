# Sửa phiên web sau F5 và tách permission

## Mục tiêu và nguyên nhân

Trước thay đổi, web đăng nhập email/password qua `POST /api/auth/login`, giữ access token và refresh token trong biến của `api.ts`. F5 xóa các biến này. `GET /api/auth/session` sau reload không còn Bearer nên nhận 401 dù phiên trong database chưa bị thu hồi. Google đã dùng cookie và không có nguyên nhân này.

Mục tiêu: reload khi phiên còn hiệu lực vẫn đăng nhập; logout, logout-all, tài khoản disable, reset mật khẩu và thay đổi quyền vẫn thu hồi phiên như trước. Không đổi database, migration, dữ liệu nghiệp vụ hoặc cấu hình kết nối.

## Quyết định lưu phiên

Dùng lại cơ chế cookie session ASP.NET Core đang phục vụ Google cho cả email/password trên web. Không cần thêm refresh-cookie protocol, không lưu JWT/refresh token vào localStorage hoặc sessionStorage. API JWT cho Scalar và các client API vẫn hoạt động riêng như trước.

localStorage không tự làm ứng dụng bị hack. Tuy nhiên mọi JavaScript cùng origin đều có thể đọc nó; một lỗ hổng XSS hoặc script dependency bị chèn có thể lấy token dài hạn. Mã hóa token bằng khóa cũng nằm trong JavaScript không giải quyết được vấn đề này. HttpOnly ngăn JavaScript đọc cookie trực tiếp, Secure giới hạn gửi qua HTTPS, SameSite=Lax hạn chế gửi cookie chéo site. HttpOnly không ngăn XSS thực hiện thao tác thay người dùng: vẫn phải phòng XSS và kiểm tra CSRF.

Tham khảo: [OWASP HTML5 Security](https://cheatsheetseries.owasp.org/cheatsheets/HTML5_Security_Cheat_Sheet.html), [ASP.NET Core antiforgery](https://learn.microsoft.com/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0).

## Luồng backend và frontend

1. Web lấy `GET /api/auth/csrf` với credentials include. Endpoint đã hỗ trợ người chưa đăng nhập; token CSRF chỉ giữ trong request, không lưu lâu dài.
2. Web gửi `POST /api/auth/web-login` với email/password và `X-CSRF-TOKEN`. Route này không mang metadata miễn CSRF như API JWT. Tất cả kiểm tra mật khẩu, giới hạn thử sai, advisory lock, trạng thái active và audit dùng chung với login API.
3. Backend tạo một `LoginSession` cookie, không tạo refresh token/JWT cho web. Chỉ sau khi transaction thành công mới phát cookie ứng dụng và trả profile. Cookie có HttpOnly, SameSite=Lax, Path=/; production dùng Secure và tiền tố __Host-. Development cho phép HTTP localhost.
4. Web không lưu token. Các API gửi cookie bằng credentials include; trước mutation lấy CSRF token mới để tránh token gắn với danh tính cũ.
5. Khi F5, App ở trạng thái loading và gọi `GET /api/auth/session`; cookie được browser tự gửi, backend xác nhận phiên và trả user. Chỉ 401 mới kết thúc phiên; lỗi mạng/5xx hiển thị lỗi kết nối và cho thử lại.
6. Logout và logout-all dùng API hiện có: thu hồi session trong DB, xóa cookie, xóa user phía UI. Reload sau logout không khôi phục được phiên.

Thời hạn giữ nguyên chính sách Google hiện có: 15 phút không hoạt động, gia hạn khi request hợp lệ. Đây không phải tùy chọn ghi nhớ 30 ngày. Reload sau thời hạn hoặc sau thu hồi phải đăng nhập lại. Các tab chia sẻ cookie, không có refresh-token rotation race giữa tab. Không thêm background polling để kéo dài phiên vô hạn.

Production cần HTTPS và Data Protection keys ổn định dùng chung giữa các instance (cấu hình `DataProtection:KeysPath` hiện có); mất key có thể khiến cookie không đọc được. Không tự sửa cấu hình triển khai trong thay đổi này.

## Tách quyền

- `Domain/Roles.cs`: giữ tên 6 role, organization kind và các role definition; lấy quyền từ `Permissions.ForRole`.
- `Authorization/Permissions.cs`: danh mục hằng số permission, danh sách `All` để đăng ký policy và bảng gán quyền `ForRole`, không phải JSON.
- Policy registration dùng `Permissions.All`. Các endpoint Identity dùng cùng hằng số. Bảng gán role–permission nằm trong `Permissions.ForRole`; thêm quyền mới phải thêm danh mục, gán role phù hợp, rồi dùng policy tại endpoint.
- Handler, CurrentAccess, session DTO và API danh sách role tiếp tục dùng `Roles.All` làm nguồn gán quyền duy nhất. Không sao chép bảng gán sang handler.
- Giữ nguyên mọi chuỗi permission công khai để Catalog/Capture/Ledger và UI tương thích. Không cấp thêm quyền cho Administrator, không đổi organization/facility scope.

Tách file là tổ chức code, không phải hệ thống role động. Đổi permission vẫn cần build/deploy; đổi role của user vẫn qua database và quy trình quản trị hiện có.

## Kiểm chứng cần thực hiện

- Backend integration trên PostgreSQL Testcontainers: web-login cần CSRF; cookie flags; không trả JWT; session đọc lại không cần Bearer; mutation cần CSRF; logout/logout-all và disable/reset/đổi quyền thu hồi; phiên hết hạn bị từ chối; chính sách role cũ giữ nguyên.
- Frontend: login gửi CSRF/cookie và không lưu token; reload khôi phục profile; lỗi mạng không tự xóa phiên; đăng xuất rồi reload về login.
- Build backend, test liên quan, test frontend và build production.
- Smoke bằng browser với host/database cô lập; không chạy login/seed/data-changing smoke trên Supabase dùng chung.

Kết quả thực tế được ghi cuối tài liệu sau khi chạy; danh sách trên là kế hoạch, chưa phải kết quả.

## Các file thay đổi và cách bảo trì

- `Features/Authentication/LocalAuthEndpoints.cs`: thêm `/web-login`, tách phần xác minh credential dùng chung. Nhánh browser tạo session không có RefreshTokenHash, commit transaction rồi SignInAsync; nhánh API vẫn IssueAsync như trước. Web-login dùng cùng rate limiter authentication.
- `apps/web/src/api.ts`: bỏ bộ nhớ JWT/refresh và retry rotation khỏi web; login lấy CSRF trước, gửi credentials include và nhận User. Giữ authGeneration để response API của phiên cũ không ghi đè phiên mới.
- `apps/web/src/LocalAuth.tsx`, `App.tsx`: cập nhật mô tả phiên; dùng luồng loading/GET session hiện có để khôi phục sau reload. Không cố đọc cookie HttpOnly từ JS để đoán người dùng đã đăng nhập.
- `Authorization/Permissions.cs`, `Domain/Roles.cs`, `DependencyInjection.cs`: tách permission và role grants; đăng ký policy từ danh mục. Endpoint admin dùng `Permissions.IdentityManage`.
- Hướng dẫn Scalar, README và architecture được cập nhật theo luồng mới; không sửa migration hoặc tên permission của module khác.

Ví dụ đổi quyền: chỉnh nhánh `Roles.MillOperator` trong `Permissions.ForRole`, review và build/deploy. Ví dụ thêm quyền mới: khai báo hằng số trong `Permissions`, đưa vào `All`, gán trong `ForRole`, khai báo `.RequireAuthorization(Permissions.TenQuyenMoi)` tại endpoint liên quan và bổ sung kiểm tra phạm vi dữ liệu. Thêm permission không tự tạo logic nghiệp vụ. Quyền không nằm trong role hiện tại bị từ chối; role không biết không nhận quyền. API `/api/admin/roles`, `/api/auth/session`, PermissionAuthorizationHandler và CurrentAccess cùng đọc các role definition đã lấy grant từ ForRole.

### Contract web

```http
GET /api/auth/csrf
# Browser lưu antiforgery cookie; response { "token": "..." }.

POST /api/auth/web-login
Content-Type: application/json
X-CSRF-TOKEN: <token từ response trên>

{ "email": "user@example.com", "password": "<mật khẩu người dùng>" }
```

Browser cần gửi cookie trên cả hai request (`credentials: 'include'`). Thành công trả 200 + SessionUserDto và Set-Cookie; không có accessToken/refreshToken. Thiếu/sai CSRF trả 400; sai credential, disable hoặc lockout trả 401; rate limit trả 429. Sau đăng nhập lấy CSRF token mới cho mutation, vì antiforgery token gắn với danh tính. GET session trả 200 khi cookie còn hợp lệ, 401 khi không còn phiên.

Không tự chuyển các phiên JWT trước thay đổi thành cookie: người đang dùng web cũ có thể cần đăng nhập một lần sau cập nhật. Các lần reload sau đó dùng cookie mới. Client Scalar không cần thay đổi.

## Kết quả kiểm chứng ngày 16/10/2026

- `dotnet restore GrainLedger.slnx -v minimal`: thành công; cần restore vì assets test cũ sau pull chưa phản ánh module references hiện tại.
- `dotnet build GrainLedger.slnx --no-restore -v quiet`: thành công.
- `dotnet test GrainLedger.slnx --no-restore -v minimal`: **244/244 passed**, không skip. Gồm 8 ca browser password session mới và 6 ca kiểm tra toàn bộ permission policy theo từng role. Test chạy trên Testcontainers, không dùng Supabase.
- `npm test`: **53/53 passed**. Đã thay các test Bearer web cũ bằng cookie/CSRF, khôi phục sau remount và logout; kiểm tra response đến muộn vẫn được giữ.
- `npm run build`: TypeScript và production Vite build thành công.
- Browser smoke qua BrowserWorkflowHost + PostgreSQL dùng một lần: đăng nhập email/password 200; reload thật (`performance.navigation` loại `reload`) → GET session 200 và vẫn ở profile; sửa tên profile 200 với CSRF rồi reload vẫn thấy tên đã lưu; logout 204 → reload GET session 401, trở về login. localStorage/sessionStorage rỗng, session cookie không đọc được qua document.cookie.
- Browser smoke dùng Development Vite với test bridge, không đo performance và không xác minh cấu hình Google Cloud/HTTPS production thực tế. Secure cookie production được kiểm tra trong integration test HTTPS.
- Build còn cảnh báo dependency có sẵn NU1902/NU1903 cho `System.Security.Cryptography.Pkcs` 10.0.0; thay đổi này không cập nhật package đó.
- Không chỉnh `.env`, không migration/seed/mutate database dùng chung; không commit/push.
