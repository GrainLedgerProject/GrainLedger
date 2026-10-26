# Thiết lập và vận hành CI

Workflow [ci.yml](../.github/workflows/ci.yml) phục vụ luồng feature → `dev` → `main`. CI chỉ build/test, không deploy, push image hoặc thay đổi shared database. Không cần secrets Supabase, Google hay SMTP.

## Trigger và chọn job

PR vào `dev` mặc định chọn job theo toàn bộ diff của PR:

| Thay đổi | Backend | Web | Docker image |
| --- | --- | --- | --- |
| Chỉ README.md, AGENTS.md, Markdown trong docs/ | Skip | Skip | Skip |
| Backend `.cs`, kể cả migrations và tests | Chạy | Skip | Skip |
| File trong apps/web/src/ | Skip | Chạy | Skip |
| Backend project/config hoặc global.json, solution, NuGet/MSBuild config | Chạy | Skip | Chạy |
| npm manifests/lockfile hoặc web config ngoài src/ | Skip | Chạy | Chạy |
| Dockerfile hoặc .dockerignore | Skip | Skip | Chạy |
| Workflow/scripts CI, file chưa nhận diện, diff không đọc được | Chạy | Chạy | Chạy |

Các nhóm được hợp lại nếu PR thay đổi nhiều phần. Rename xét cả đường dẫn cũ và mới. Backend vẫn chạy toàn bộ integration suite khi được chọn; web vẫn chạy toàn bộ web suite.

- PR vào `main` hoặc nhánh khác `dev`: chạy đầy đủ, kể cả docs-only.
- Push `main`, `merge_group`, `workflow_dispatch`: chạy đầy đủ.
- Push `dev`: không chạy lần thứ hai sau merge. **Phải bảo vệ dev, yêu cầu PR và check `required`, giới hạn direct push/bypass.** Nếu nhóm vẫn cho phép direct push dev, thêm lại dev vào `on.push.branches`.
- Push feature branch chưa có PR: không chạy. Push thêm commit vào PR đang mở kích hoạt kiểm tra PR.
- Run cũ cùng PR/event được hủy khi có commit mới. Các sự kiện PR/push/merge queue vẫn là run riêng.

Không cần vào Settings để bật tối ưu. Repository variable `CI_ENABLE_PATH_FILTERS` chưa đặt hoặc bằng `true` bật routing trên PR vào dev; đặt `false` để ép full-run. Giá trị không nhận diện cũng chạy đầy đủ để tránh bỏ sót kiểm tra. Biến này không thêm lại push trigger cho dev.

Commit thay đổi `.github/**` vẫn chạy đầy đủ. Vì xét toàn bộ diff PR, PR setup CI tiếp tục full-run cho tới khi merge; các PR tính năng sau đó mới hưởng routing nhỏ hơn.

Docker không build lại cho mọi chỉnh sửa source trên PR vào dev. Lỗi chỉ xuất hiện khi publish/đóng gói có thể được phát hiện muộn hơn ở PR vào main. Khi cần kiểm tra image ngay, đặt variable thành `false` rồi cập nhật/reopen PR, hoặc chạy thủ công sau khi workflow có trên default branch.

## Jobs và cache

1. `plan and validate`: test routing/gate, actionlint, kiểm tra link tài liệu và xuất job cần chạy.
2. `backend`, `web`, `container`: chạy song song khi được chọn.
3. `required`: luôn chạy; planning lỗi, job bắt buộc fail/cancel/skip hoặc output không hợp lệ đều chặn check.

Backend dùng .NET từ global.json, Release build và PostgreSQL Testcontainers cô lập. Web dùng Node 24, npm ci, TypeScript/Vite build và Vitest. Giữ xUnit `MaxParallelThreads=4` và Vitest `maxWorkers=2`; không retry/skip test để làm xanh CI.

- npm cache đã có; vẫn chạy npm ci để cài theo lockfile.
- NuGet cache chỉ chứa `~/.nuget/packages`, key theo OS/architecture và project/SDK/dependency config. Restore luôn chạy, kể cả cache hit. Đây không phải locked restore; NuGet lockfiles chưa được thêm.
- Docker dùng Buildx và GitHub Actions layer cache, `mode=max`, scope riêng `grainledger-ci`. Không push image và không load image vào daemon vì job chỉ kiểm tra build. Cache miss vẫn build bình thường. Export cache giới hạn 2 phút và lỗi export không làm hỏng một build đã thành công; lỗi build vẫn fail.
- Không cache kết quả test, database, bin/obj, .env hoặc credentials. Backend TRX giữ 7 ngày. Cache lạnh vẫn tốn thời gian tải/build; cache chịu giới hạn dung lượng và quyền đọc theo branch của GitHub.

Actions pin SHA, actionlint pin version/checksum; permissions là contents: read. Docker layer cache theo [Docker documentation](https://docs.docker.com/build/ci/github-actions/cache/), package cache dùng [actions/cache](https://github.com/actions/cache).

## Kiểm tra tại máy

```bash
node --test .github/scripts/ci.test.mjs
dotnet restore GrainLedger.slnx
dotnet build GrainLedger.slnx --configuration Release --no-restore
dotnet test GrainLedger.slnx --configuration Release --no-build --no-restore --logger trx --results-directory artifacts/test-results/backend -- xUnit.MaxParallelThreads=4
npm ci
npm run build
npm run test --workspace @grainledger/web -- --maxWorkers=2
docker build --tag grainledger:ci .
```

Có thể chạy actionlint tại root. Thay đổi CI routing/docs không cần chạy lại backend/frontend nếu không sửa code ứng dụng; chạy CI script tests, actionlint và kiểm tra links. Cache GitHub chỉ xác minh được trên GitHub, không suy ra cache hit từ local build.

## Bước 9 — Bạn commit/push và cập nhật PR

Review `git diff`, stage đúng file đã sửa, commit Conventional Commit rồi push `feat/ci`. PR hiện tại nhắm vào `dev` tự nhận commit mới. Không cần tạo PR thứ hai.

## Bước 10 — Bạn kiểm tra trên GitHub

- PR setup CI phải chạy đầy đủ và `required` xanh.
- Xem restore cache hit/miss, Docker build layers và thời gian từng job; lần đầu có thể là cache lạnh.
- Sau khi merge CI, kiểm tra routing qua các PR docs-only, web source, backend source, packaging/config; các job không cần phải skipped và required vẫn xanh.
- Lỗi job bắt buộc phải làm required đỏ. Không chọn từng job có thể skip làm required checks.
- Theo dõi tổng runner-minutes để đo quota, và thời gian đến check required để đo thời gian chờ. Chạy song song không có nghĩa chỉ tính phút của job dài nhất.
- `Run workflow` cần workflow tồn tại trên default branch (main); chỉ merge vào dev chưa bảo đảm nút xuất hiện.

## Bước 11 — Bạn bảo vệ nhánh

Trong Settings → Rules → Rulesets (hoặc Branch protection), bảo vệ dev trước khi dùng lâu dài chính sách không chạy push dev:

1. Require pull request before merging, approval theo chính sách nhóm.
2. Require status checks: chọn job `required` thuộc workflow `CI` từ run thật.
3. Giới hạn bypass/direct push. Bật branch up-to-date hoặc merge queue nếu cần kiểm tra với base mới nhất.
4. Áp dụng tương tự cho main sau khi workflow/check tồn tại ở đó.

Nếu không thể bảo vệ dev, khôi phục push dev trong workflow để kiểm tra cả commit đẩy trực tiếp. CI không tự thay repository settings.

## Giới hạn

Docs check chỉ xác minh file đích của inline Markdown links, không kiểm tra anchors/URL ngoài. Chưa có browser E2E, shared-database smoke, NuGet locked restore hoặc deployment. Việc giảm Docker/source runs và bỏ push dev là đánh đổi có chủ đích; giữ full-run ở main và merge queue. Kế hoạch rollout ban đầu được lưu tại [ci-plan.md](ci-plan.md).
