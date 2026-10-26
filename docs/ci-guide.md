# Thiết lập và vận hành CI

Workflow [ci.yml](../.github/workflows/ci.yml) kiểm tra PR, push vào `dev` và `main`, merge queue và thao tác chạy thủ công. Luồng tích hợp là feature → `dev` → `main`; PR setup CI này nhắm vào `dev`. Chỉ build/test; không deploy, push image, migrate hoặc seed lên shared database. Không cần tạo GitHub Secrets cho Supabase, Google hay SMTP.

## Luồng đang được cấu hình

1. `plan and validate`: kiểm tra routing/gate bằng Node test runner, kiểm tra workflow bằng actionlint và local Markdown links, rồi xuất danh sách job bắt buộc.
2. `backend`, `web`, `container`: chạy song song theo danh sách đó.
3. `required`: luôn đánh giá kết quả; job bắt buộc bị fail, cancelled hoặc skipped đều làm check thất bại.

Backend dùng .NET từ `global.json`, Release build và toàn bộ suite với PostgreSQL Testcontainers. Web dùng Node.js 24, root npm lockfile, TypeScript/Vite production build và Vitest. CI giới hạn xUnit `MaxParallelThreads=2` và Vitest `maxWorkers=2` để giảm tranh chấp tài nguyên; vẫn chạy toàn bộ test, giữ nguyên assertions và timeout. Container build Dockerfile hiện có và không publish image. Báo cáo backend TRX được giữ 7 ngày nếu test runner đã tạo file.

Actions được pin commit SHA; actionlint pin phiên bản và SHA-256. Workflow chỉ có quyền `contents: read`, không giữ Git credentials trong checkout. Runner cần tải được các action, SDK, npm/NuGet packages, actionlint và container images từ Internet.

Lần đầu **chạy đầy đủ** để lấy baseline. Biến repository `CI_ENABLE_PATH_FILTERS` chưa đặt hoặc khác chuỗi `true` đều giữ chế độ này. Không bật lọc trước khi hoàn thành bước 10 bên dưới. Docker layer cache, NuGet lock/cache, coverage, ESLint, E2E và Dependabot thuộc các bước tiếp theo trong [kế hoạch](ci-plan.md); chưa được bật trong bản CI đầu tiên.

## Kiểm tra tại máy trước khi push

Tại repository root:

```bash
node --test .github/scripts/ci.test.mjs
dotnet restore GrainLedger.slnx
dotnet build GrainLedger.slnx --configuration Release --no-restore
dotnet test GrainLedger.slnx --configuration Release --no-build --no-restore --logger trx --results-directory artifacts/test-results/backend -- xUnit.MaxParallelThreads=2
npm ci
npm run build
npm run test --workspace @grainledger/web -- --maxWorkers=2
docker build --tag grainledger:ci .
```

Node.js 24 và Docker daemon cần sẵn sàng cho kiểm tra ứng dụng. `actionlint` có thể chạy tại root nếu đã cài; workflow tự tải bản `1.7.12` và xác minh checksum. Kiểm tra local không thay thế lần chạy thật trên GitHub Ubuntu.

### Kết quả kiểm tra khi chuẩn bị branch

Ngày 26/10/2026:

- actionlint `1.7.12`, năm nhóm regression tests cho CI scripts, local links của tài liệu thay đổi và `git diff --check`: đạt.
- .NET SDK `10.0.401` trên Windows: restore, Release build, publish API và **477/477 backend tests** đạt; PostgreSQL 17 chạy trong container tạm, xUnit giới hạn song song 2.
- Node.js `24.22.1`: `npm ci` và production web build đạt trên Windows. Linux web stage của Dockerfile cũng build được; toàn bộ **216/216 web tests** đạt trong Linux container với `maxWorkers=2`.
- Đã sửa mock `duplicate-check` trong test đăng ký thửa đất: tránh trả nhầm DTO chi tiết plot khi debounce kịp chạy trên máy chậm. Test đợi request duplicate-check thật sự xảy ra; cả file `App.test.tsx` **19/19 tests** đạt khi chạy lại trong Linux với file sửa được mount vào container.
- Lần chạy mặc định chưa giới hạn song song có timeout và lỗi dây chuyền; kết quả đạt ở trên dùng cấu hình concurrency của workflow cuối cùng. Không tăng timeout hoặc bỏ assertions.
- **Full Docker image build chưa được xác nhận đạt**: đã dừng lượt local sau hơn 20 phút, vẫn ở bước tải layer .NET SDK từ MCR. Web stage đã đạt và API publish local đã đạt, nhưng chúng không thay thế xác minh full image build ở bước 10 trên GitHub.
- `npm audit` báo **1 high** ở dependency gián tiếp `undici`; chưa sửa dependency/lockfile trong phạm vi setup CI. Backend build có warning `CS9113` sẵn có và Vite cảnh báo chunk lớn; các warning này không bị che bằng cấu hình mới.

Chưa commit/push, mở PR hoặc chỉnh GitHub Settings. Chưa có số đo runner Ubuntu của GitHub; không dùng timing local này làm cam kết thời gian CI.

## Bước 9 — Bạn commit, push và mở PR

Xác nhận branch và chỉ stage các file của thay đổi này:

```bash
git branch --show-current
git status --short
git add .github/workflows/ci.yml .github/scripts/ci.mjs .github/scripts/ci.test.mjs apps/web/src/App.test.tsx README.md docs/ci-plan.md docs/ci-guide.md
git diff --cached --stat
git diff --cached
git commit -m "ci(api): add GitHub Actions validation"
git push -u origin feat/ci
```

Không dùng `git add .` nếu workspace có thay đổi khác chưa muốn commit. Kiểm tra diff không chứa `.env`, credentials hoặc evidence thật.

Mở repository GitHub → **Compare & pull request** → base `dev`, compare `feat/ci` → **Create pull request**. Khi nhóm đưa bản tích hợp từ `dev` vào `main`, PR đó cũng chạy CI.

Nếu Actions bị tắt, vào **Settings → Actions → General**, cho phép GitHub Actions và các action đang dùng trong workflow. Không cần bật quyền write cho `GITHUB_TOKEN`.

## Bước 10 — Bạn xác minh CI trên GitHub

### Lần chạy đầy đủ

- Mở PR → **Checks**, hoặc tab **Actions → CI → run**. Các job phải chạy với commit mới nhất của PR.
- `plan and validate` phải thành công; xem summary để xác nhận ba flag đều `true`.
- `backend`, `web`, `container` và `required` phải xanh. Chưa bật ruleset nếu baseline vẫn đỏ.
- Nếu đỏ, mở job → step lỗi để lấy log. Với backend tests, tải artifact `backend-test-results` nếu đã được tạo. Không gửi `.env`, tokens hoặc session JSON khi nhờ hỗ trợ.
- Ghi thời gian từng job, thời gian chờ runner và tổng thời gian tới `required`. Run đầu thường cần tải dependencies/images; không dùng nó làm số đo cache ấm.

### Kiểm tra CI thực sự chặn lỗi

Trên branch thử hoặc PR hiện tại, tạm làm một assertion trong test có sẵn thất bại, commit và push. Xác nhận job test và `required` đều đỏ. Sau đó hoàn tác đúng thay đổi thử bằng commit mới, push và đợi toàn bộ CI xanh. Không merge commit cố ý gây lỗi.

Nếu cần kiểm tra Docker build gate, thử thay đổi Dockerfile gây build lỗi rồi khôi phục bằng cùng cách. Khi push commit mới trong lúc run cũ đang chạy, kiểm tra run cũ được hủy đúng PR và run mới vẫn xuất hiện.

### Bật tối ưu sau baseline, khi bạn sẵn sàng

Vào **Settings → Secrets and variables → Actions → Variables → New repository variable**:

- Name: `CI_ENABLE_PATH_FILTERS`
- Value: `true`

Đây là variable không nhạy cảm, không phải secret. Thay đổi variable không tự tạo run mới; push commit mới hoặc re-run phù hợp để kiểm tra.

Trên các PR thử vào `dev`, xác minh:

- Chỉ sửa Markdown trong `docs/`, `README.md`, `AGENTS.md`: bỏ qua backend/web/container; `required` vẫn xanh sau kiểm tra nhẹ.
- Sửa `backend/**`: backend và container chạy; web được skip.
- Sửa `apps/web/**` hoặc root npm manifests/lockfile: web và container chạy.
- Sửa cả hai, `.github/**` hoặc file chưa nhận diện: chạy đầy đủ.
- Rename file code sang docs phải vẫn kích hoạt kiểm tra code; không chỉ xét tên mới. PR nhiều commit phải tính toàn bộ diff của PR.

Logic này có regression tests tại [ci.test.mjs](../.github/scripts/ci.test.mjs), nhưng trạng thái skipped/gate trên GitHub vẫn cần xác minh thực tế. Push `dev`/`main`, `merge_group` và `workflow_dispatch` luôn chạy đầy đủ dù variable đã bật. Có thể tắt tối ưu bất kỳ lúc nào bằng cách xóa variable hoặc đặt `false`.

Nút **Run workflow** cần workflow có trên default branch (hiện refs `origin/HEAD` trỏ `main`). Chỉ merge vào `dev` chưa bảo đảm nút này xuất hiện; dùng PR và push `dev` để kiểm tra trước. Sau khi workflow có trên default branch, có thể dùng **Actions → CI → Run workflow** và chọn branch cần chạy.

## Bước 11 — Bạn bật yêu cầu CI trước merge

Sau khi bước 10 đạt, vào **Settings → Rules → Rulesets → New branch ruleset**:

1. Đặt tên `Protect dev`, chọn trạng thái **Active**.
2. Target branch: `dev`. Sau khi workflow cũng có trên `main` và check đã xuất hiện, tạo rule tương ứng bảo vệ `main`.
3. Bật **Require a pull request before merging**; chọn số approval phù hợp nhóm.
4. Bật **Require status checks to pass** và chọn check của job **`required`** thuộc workflow **`CI`**. Giao diện có thể hiển thị `required` hoặc nhóm dưới `CI`; lấy đúng tên từ lần chạy thực tế, không tự nhập một tên chưa tồn tại.
5. Không chọn trực tiếp backend/web/container làm required checks vì chúng được phép skip khi bật tối ưu.
6. Bật yêu cầu resolve conversations nếu phù hợp; giới hạn bypass theo chính sách nhóm, rồi lưu ruleset.

Kiểm tra một PR có check đỏ bị chặn merge, PR xanh đáp ứng các yêu cầu còn lại thì merge được. Không cần merge PR lỗi để thử. Nếu nhóm muốn bắt buộc thử với phiên bản base mới nhất, bật yêu cầu branch up to date hoặc dùng merge queue; workflow đã hỗ trợ `merge_group`.

Nếu không có Rulesets, kiểm tra **Settings → Branches → Branch protection rules** hoặc quyền/gói GitHub của repository. CI YAML không tự tạo branch protection. Không bật yêu cầu deployment vì phạm vi hiện tại chỉ là CI.

## Giới hạn và cách xử lý lỗi

- Docker/PostgreSQL lỗi khởi động: kiểm tra step `Check SDK and Docker`, tài nguyên runner và test logs. Không sửa connection string sang Supabase hoặc bỏ runtime-role validation để làm CI xanh.
- Backend fixture tạo database riêng theo class; mức song song 2 đã được chọn sau khi lần chạy mặc định gặp timeout trong workload lớn. Đo tiếp trên GitHub trước khi tăng concurrency. Không cache test database hoặc dùng chung DB giữa fixture.
- Docs check chỉ xác minh sự tồn tại của đường dẫn trong inline Markdown links ở file thay đổi, bỏ qua tài liệu bị xóa; không kiểm tra URL ngoài, anchors hay mọi cú pháp Markdown.
- Diff PR không đọc được sẽ chạy đầy đủ; job planning/test/actionlint hỏng sẽ khiến gate đỏ. Không dùng `continue-on-error` để vượt lỗi.
- CI full suite hiện tại không tự động chạy browser E2E. Google consent thật, SMTP thật và runtime container smoke vẫn cần kiểm tra riêng theo kế hoạch.
