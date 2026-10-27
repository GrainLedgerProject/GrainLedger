# CI plan ? historical baseline

The plan below records the original full-run rollout. The current optimized policy supersedes its trigger, routing and cache decisions: see [CI setup and operation](ci-guide.md) and [workflow](../.github/workflows/ci.yml). PRs into dev filter changes by default, pushes to dev no longer run CI, and NuGet/Docker layer caches are enabled. Protect dev with required PR checks before relying on this policy.

# Kế hoạch CI cho GrainLedger bằng GitHub Actions

Ngày khảo sát: 26/10/2026. Phạm vi: kiểm tra chất lượng và khả năng build khi tích hợp code; chưa triển khai CD. Đây là kế hoạch, chưa phải workflow đã chạy thành công trên GitHub.

Bản setup đầu tiên đã được chuẩn bị trên `feat/ci`: [workflow](../.github/workflows/ci.yml), [routing/gate](../.github/scripts/ci.mjs), [regression tests](../.github/scripts/ci.test.mjs) và [hướng dẫn bước 9–11](ci-guide.md). Full-run là mặc định; tối ưu đường dẫn chỉ bật khi repository variable `CI_ENABLE_PATH_FILTERS=true` sau baseline. Chưa commit/push hoặc thay đổi GitHub Settings. Các hạng mục PR 2/3 bên dưới vẫn là công việc tiếp theo.

Luồng branch đã được người dùng xác nhận: feature → `dev` → `main`. PR setup CI nhắm vào `dev`; workflow chạy PR và push trên cả hai nhánh tích hợp `dev`/`main`. Default branch `main` không đồng nghĩa feature phải mở PR trực tiếp vào `main`.

Điều chỉnh khi kiểm tra local: workflow giới hạn xUnit `MaxParallelThreads=2` và Vitest `maxWorkers=2` sau khi lần chạy mặc định gặp timeout, Vitest tạo 29 workers. Các lệnh ở phần thiết kế bên dưới là baseline; lệnh CI chính xác nằm trong workflow và hướng dẫn vận hành. Không thay timeout, bỏ test hoặc dùng lại kết quả test để làm gate xanh.

## 1. Đề xuất chính

Dùng **GitHub Actions với GitHub-hosted Ubuntu runner**, tận dụng bộ test hiện có. Luồng đề xuất sau tối ưu: `changes` xác định phạm vi → backend, web và Docker build chạy độc lập khi cần → `CI / required` tổng hợp kết quả. PR chỉ sửa tài liệu bỏ qua build/test; PR backend chạy toàn bộ backend suite và Docker; PR web chạy web suite/build và Docker. Push lên nhánh tích hợp và chạy thủ công mặc định chạy đầy đủ để giữ một mốc kiểm tra chung.

Triển khai bản full-run trước để xác nhận môi trường và đo baseline, rồi bật phân loại thay đổi sau khi đã kiểm tra các trường hợp skip/failure. Tối ưu này chủ yếu giảm số job và runner-minutes cho PR; chưa có số đo chứng minh giảm bao nhiêu thời gian.

Với modular monolith này, chia pipeline theo loại kiểm tra hợp lý hơn tạo một pipeline riêng cho từng module: Identity, Catalogs, Capture và Ledger có hợp đồng và migration liên quan nhau.

CI không cần credentials Supabase, Google OAuth hoặc SMTP thật. Không chạy migration/seed lên database dùng chung, không push image, không deploy, không tạo release.

## 2. Những gì đã xác nhận từ repository

- [README](../README.md), [setup](setup-and-deployment.md) và [architecture](architecture.md) xác định .NET 10, Node.js 24, PostgreSQL, React/TypeScript/Vite; normal startup không migrate hoặc seed.
- [global.json](../global.json) chọn SDK `10.0.100`, `rollForward: latestFeature`. Khi triển khai, dùng `actions/setup-dotnet` đọc file này thay vì mặc định dựa vào SDK cài sẵn trên runner.
- [Solution](../GrainLedger.slnx) có API, bốn module và một test project. Project test dùng xUnit, `Microsoft.AspNetCore.Mvc.Testing` và Testcontainers; có 50 file tên `*Tests.cs` tại thời điểm khảo sát. Đây không phải số test case hoặc kết quả pass.
- [ApiFactory](../backend/tests/GrainLedger.Api.IntegrationTests/ApiFactory.cs) tự tạo `postgres:17-alpine`, migrate theo thứ tự Identity → Catalogs → Capture → Ledger, tạo runtime role giới hạn, cấu hình grants và kiểm tra runtime role. Google/email được thay bằng fake. Không cần thêm `services: postgres` cho suite này.
- [Compose](../compose.yaml) dùng `postgres:18-alpine`, khác major version PostgreSQL trong test. Cần xác nhận major version database mục tiêu trước khi chọn baseline; CI ban đầu giữ nguyên test hiện tại và ghi nhận khoảng trống tương thích này.
- [Root package.json](../package.json) điều phối npm workspaces; `npm test` và `npm run build` sử dụng `@grainledger/web` ở `apps/web`. Có 29 file `.test.ts`/`.test.tsx` trong `apps/web/src`; không phải số test case.
- [Web package.json](../apps/web/package.json): test là `vitest run`, build là `tsc --noEmit && vite build`. TypeScript đã bật strict. Chưa có script lint và coverage trong workspace đang hoạt động.
- `apps/web/grainledger` là một cây frontend có package, lockfile và ESLint riêng, nhưng không phải workspace được root scripts chọn. Không lấy cấu hình lint tại đây làm bằng chứng app chính đã có lint. Chưa xóa hay chuyển đổi cây này trong phạm vi CI.
- [Dockerfile](../Dockerfile) build web, publish API và ghép web assets vào `wwwroot`; Docker build là kiểm tra đóng gói có giá trị, kể cả chưa làm CD.
- Chưa có file GitHub Actions được Git theo dõi; chưa thấy NuGet `packages.lock.json`, cấu hình coverage hay Playwright suite. Chưa xác minh visibility, gói GitHub, default branch và ruleset từ GitHub.

## 3. Thiết kế workflow sau tối ưu

File dự kiến: `.github/workflows/ci.yml`.

### Sự kiện và môi trường

- `pull_request`: kiểm tra PR nhắm đến nhánh tích hợp được nhóm chọn. Có thể bắt đầu với mọi base branch để không bỏ sót; thu hẹp sau khi xác nhận quy trình branch.
- `push`: chỉ nhánh tích hợp/default branch đã xác nhận, để kiểm tra trạng thái sau merge. Không chạy push trên mọi feature branch khi đã có PR, tránh chạy trùng.
- `workflow_dispatch`: hỗ trợ chạy lại chủ động. Nếu sau này dùng merge queue, thêm `merge_group` trước khi yêu cầu check cho queue.
- Runner đề xuất `ubuntu-24.04`; cài .NET theo `global.json`, Node.js 24. Ghi lại phiên bản SDK/Node/npm và `docker info` để chẩn đoán, không in environment chứa secrets.
- `permissions: contents: read`, checkout với `persist-credentials: false`. Không dùng `pull_request_target` để chạy code PR.
- Đặt concurrency theo workflow + PR number hoặc ref, `cancel-in-progress: true` để hủy run cũ khi có commit mới.
- Timeout khởi điểm: backend 30 phút, web 15 phút, Docker 20 phút. Đây là giới hạn đề xuất, không phải thời gian đã đo.

### Job changes — phân loại thay đổi và kiểm tra nhẹ

Workflow luôn được tạo cho sự kiện được hỗ trợ, không dùng `paths`/`paths-ignore` ở mức workflow. Job đầu tiên chạy kiểm tra cấu hình workflow khi cần và xuất ba boolean tường minh: `run_backend`, `run_web`, `run_container`.

Quy tắc PR:

- Chỉ thay đổi Markdown trong `docs/`, `README.md` hoặc `AGENTS.md`: kiểm tra tài liệu/link theo phạm vi, ba boolean là `false`.
- Thay đổi `backend/**`: backend và container là `true`. Chạy toàn bộ backend suite, không lọc test theo tên module.
- Thay đổi `apps/web/**`: web và container là `true`. Bao gồm cây lồng `apps/web/grainledger` ở lần đầu để không bỏ sót tác động Docker context; chỉ thu hẹp khi nhóm quyết định phạm vi cây này.
- Thay đổi root `package.json`, `package-lock.json` hoặc `apps/mobile/package.json`: web và container là `true`, vì Docker/npm install phụ thuộc các manifest này.
- Thay đổi `global.json`, `GrainLedger.slnx`, NuGet/MSBuild config: backend và container là `true`. Cấu hình dùng chung chưa xác định rõ tác động thì chạy cả ba.
- Thay đổi `Dockerfile` hoặc `.dockerignore`: container là `true`; các flag khác theo những file còn lại trong PR.
- Thay đổi `.github/**`, script CI hoặc file chưa có quy tắc: cả ba là `true`. Quy tắc mặc định chạy đầy đủ bảo vệ khi repository phát triển thêm.
- Một PR có nhiều loại thay đổi lấy hợp của các yêu cầu, không chọn một loại duy nhất.

Quy tắc sự kiện khác: push nhánh tích hợp, `workflow_dispatch` và `merge_group` nếu dùng đều xuất cả ba flag `true`. Chưa thêm chế độ chạy thủ công cho phép bỏ gate bắt buộc.

Cách triển khai cần kiểm tra kỹ:

- Lấy danh sách file Git đầy đủ, không dựa trên danh sách bị giới hạn hoặc API pagination chưa xử lý hết. Với PR, so sánh merge-base của base SHA và head SHA để bao phủ toàn bộ PR; giữ commit được build/test là merge commit PR theo checkout mặc định. Với sự kiện full-run không cần suy luận diff.
- Xét cả tên cũ và mới khi rename, và file bị xóa. Không dùng shell `eval` hoặc nội suy tên file PR vào command text; dùng parser/list API an toàn, hỗ trợ tên chứa khoảng trắng.
- Diff không xác định được thì chạy đầy đủ; lỗi xác thực/configuration của job vẫn phải hiện rõ, không giả thành docs-only. Output thiếu, rỗng hoặc sai định dạng khiến gate thất bại.
- Có thể dùng action phân loại đường dẫn đã được review, pin SHA và cấu hình đủ quyền đọc PR; hoặc script Git nhỏ được kiểm tra bằng các tình huống bên dưới. Không thêm quyền write chỉ để đọc danh sách file.
- Job này không cài Node/.NET hoặc khởi động Docker cho PR docs-only. Timeout đề xuất 5 phút; tránh biến bước phân loại thành nút thắt mới.

### Job backend — restore, Release build và test API

Chạy tại repository root, trên cùng một runner/job để dùng lại output build:

```bash
dotnet restore GrainLedger.slnx
dotnet build GrainLedger.slnx --configuration Release --no-restore
dotnet test GrainLedger.slnx --configuration Release --no-build --no-restore --logger trx --results-directory artifacts/test-results/backend
```

Lưu ý triển khai:

- Dùng `Testing` cho test host; không nạp `.env` của developer và không khai báo connection string dùng chung trong workflow. Giữ migration/grant setup trong fixture trên container tạm.
- Để `GRAINLEDGER_TEST_HOST_NETWORK` không được đặt ở runner Ubuntu thông thường; dùng cơ chế mapped port mặc định của Testcontainers trước.
- Không đặt `GRAINLEDGER_BROWSER_SESSION_FILE` trong suite thông thường: `BrowserWorkflowHost` sẽ khởi động host chờ shutdown khi biến này có giá trị.
- Fixture được dùng theo class và tạo container riêng. Đo CPU/RAM/số container ở lần chạy đầu; nếu cạn tài nguyên, giới hạn song song của xUnit bằng cấu hình riêng, không bỏ test để làm pipeline xanh.
- Upload TRX bằng `actions/upload-artifact` với `if: always()`, retention khởi điểm 7 ngày. Nếu restore/build hỏng thì chưa có TRX; artifact step không được che lỗi gốc.
- Không thêm SQLite/in-memory thay PostgreSQL: các function SQL, quyền DB, transaction và mass accounting cần PostgreSQL thật.

### Job web — cài dependency, test và production build

Chạy từ repository root:

```bash
npm ci
npm test
npm run build
```

- Dùng root `package-lock.json`; không chạy install/build trong `apps/web/grainledger`.
- Cache npm download cache bằng `actions/setup-node`, `cache: npm`, `cache-dependency-path: package-lock.json`; không cache `node_modules`.
- `npm run build` đã kiểm tra TypeScript và tạo `apps/web/dist`; không cần một job typecheck trùng lặp ngay từ đầu.
- Có thể thêm Vitest JUnit reporter vào lệnh CI và upload XML khi đã thử đúng phiên bản Vitest hiện tại. Console output đủ để khởi động CI, không gọi đó là báo cáo coverage.
- `dist` không bắt buộc lưu ở MVP vì chưa có CD; có thể lưu ngắn hạn để reviewer tải kiểm tra khi cần.
- Để lỗi compile được phát hiện sớm, thứ tự trong job có thể là `npm ci` → `npm run build` → `npm test`. Không tách build/test thành hai job cùng cài npm trừ khi số liệu cho thấy lợi ích lớn hơn chi phí runner và install lặp.

### Job container — kiểm tra Dockerfile

```bash
docker build --tag grainledger:ci .
```

- Build image trên runner tạm và không push registry. Đây vẫn là CI.
- Chạy khi `run_container` là `true`, song song với backend/web. Không đặt `needs` backend/web chỉ để đợi test xanh: điều đó kéo dài critical path mà chưa tái sử dụng được output của chúng. Nếu nhóm muốn tiết kiệm compute bằng cách chờ, cần chấp nhận độ trễ tăng và đo trước khi đổi.
- Giữ kiểm tra Docker trên PR thay đổi code backend/web: Dockerfile ghép hai phần này vào một image. Chuyển mọi Docker build sang sau merge sẽ giảm chi phí PR nhưng để lỗi đóng gói lọt qua gate, nên chưa chọn cách đó.
- Sau baseline, dùng `docker/setup-buildx-action` và `docker/build-push-action` với `push: false`, cache GHA `cache-from`/`cache-to`, scope riêng cho image này. Dùng Buildx/BuildKit hỗ trợ cache API hiện hành, kiểm tra cache miss và giới hạn dung lượng. Không đăng nhập registry chỉ để build.
- npm cache của job web và NuGet cache của job backend không tự trở thành Docker layer cache. Không truyền `dist`/publish artifact sang Dockerfile bằng cách đổi cách đóng gói trong PR CI đầu tiên; giữ Dockerfile hiện tại là thứ được kiểm tra.
- Build thành công chưa chứng minh container khởi động thành công hoặc kết nối DB đúng; runtime smoke là hạng mục riêng ở giai đoạn sau.

Tham khảo [Docker cache trên GitHub Actions](https://docs.docker.com/build/ci/github-actions/cache/) cho cấu hình và yêu cầu phiên bản. Cache giúp giảm công việc lặp khi trúng cache, không thay thế việc chạy test hoặc xác minh đóng gói.

### Job tổng hợp — `CI / required`

- `needs` changes, backend, web và container; đặt điều kiện chạy `always()`.
- `changes` phải thành công và cả ba output phải là boolean chuỗi hợp lệ. Với từng flag `true`, job tương ứng bắt buộc có kết quả `success`; `failure`, `cancelled` hoặc `skipped` đều làm gate thất bại. Với flag `false`, chỉ chấp nhận `skipped` hoặc `success`, không bỏ qua một job đã chạy nhưng thất bại.
- Không chỉ kiểm tra “không có failure”: một job bị skip ngoài ý muốn cũng phải làm gate đỏ nếu nó được yêu cầu. Không dùng `if: success()` mặc định cho gate vì dependency fail/skip có thể khiến gate không chạy.
- Dùng tên check ổn định, duy nhất giữa các workflow. Cấu hình branch ruleset yêu cầu chính xác tên check GitHub hiển thị sau lần chạy đầu.
- Không đặt path filter ở mức workflow required: workflow không được tạo có thể khiến required check pending. Docs-only vẫn có gate tổng hợp thành công sau khi kiểm tra nhẹ; không require trực tiếp những job được phép skip.

GitHub giải thích các trường hợp skipped/pending và yêu cầu `always()` cho job phụ thuộc trong [required status checks](https://docs.github.com/en/enterprise-cloud@latest/pull-requests/how-tos/merge-and-close-pull-requests/troubleshooting-required-status-checks).

## 4. Công cụ nên chọn

### Dùng ngay

- **GitHub Actions**: điều phối và hiển thị kết quả ngay trên PR; tận dụng các lệnh repository đã có. [Hướng dẫn .NET chính thức](https://docs.github.com/en/actions/tutorials/build-and-test-code/net).
- **xUnit + Testcontainers + PostgreSQL**: giữ nguyên nền tảng backend, ưu tiên bảo vệ migration, role/grant, authorization, ledger invariants và API contracts đã có.
- **Vitest + React Testing Library + TypeScript**: giữ nguyên nền tảng web, không thêm một test runner khác chỉ để chạy CI.
- **Docker CLI**: kiểm tra image build từ Dockerfile hiện có.
- **actionlint**: kiểm tra cú pháp workflow, expressions và shell liên quan; pin phiên bản/checksum hoặc action SHA khi đưa vào workflow. [Dự án chính thức](https://github.com/rhysd/actionlint).
- **Dependabot**: cập nhật dependency bằng PR, lịch tuần, giới hạn số PR đang mở; theo dõi `github-actions` tại `/`, npm workspace ở `/`, NuGet tại các thư mục project thực tế. Chưa auto-merge. [Cấu hình chính thức](https://docs.github.com/en/code-security/reference/supply-chain-security/dependabot-options-reference).

### Bổ sung sau khi pipeline cơ bản ổn định

- **ESLint cho app chính**: thêm cấu hình và script tại `apps/web`, ưu tiên React Hooks và lỗi thực tế; xử lý baseline trước khi biến thành required check. Không dùng nhầm cấu hình cây frontend lồng bên trong.
- **Coverage**: thêm collector .NET tương thích test SDK và coverage provider tương thích Vitest. Thu baseline trước, rồi chọn ngưỡng cho phần code nghiệp vụ quan trọng; chưa áp một con số 80% tùy ý. Báo cáo artifact trước khi cân nhắc Codecov hay dịch vụ khác.
- **Playwright**: thêm browser smoke nhỏ, ổn định, chạy Chromium trước; tận dụng test host/DB tạm thay vì tài khoản và database thật. [Tài liệu CI chính thức](https://playwright.dev/docs/ci).
- **Dependency review / CodeQL**: chỉ bật sau khi xác nhận repo và gói GitHub hỗ trợ. Dependency review khả dụng cho public repo và private repo có Code Security/Advanced Security; code scanning trên private repo cũng cần entitlement phù hợp. [Dependency review](https://docs.github.com/en/code-security/concepts/supply-chain-security/dependency-review), [CodeQL private repo](https://docs.github.com/en/code-security/reference/code-scanning/troubleshoot-analysis-errors/private-repository-enablement).
- **Gitleaks**: cân nhắc làm secret scan độc lập nếu tính năng GitHub chưa đáp ứng; chọn phạm vi quét, baseline và pin phiên bản trước khi required. Không để scanner log nội dung secret. [Dự án chính thức](https://github.com/gitleaks/gitleaks).

Giai đoạn này chưa cần tự vận hành Jenkins, thêm SonarQube server, Kubernetes hoặc registry. Đánh giá thêm công cụ khi nhóm có nhu cầu cụ thể mà các kiểm tra trên chưa đáp ứng.

## 5. Dependency, bảo mật và database

### Tính tái lập và cache

- npm dùng `npm ci` để tôn trọng lockfile. Không tự sửa lockfile trong CI.
- Backend hiện chưa có NuGet lockfile: MVP restore bình thường, chưa bật `setup-dotnet cache: true`. Action này yêu cầu `packages.lock.json`; bật cache ngay có thể làm workflow lỗi. [Tài liệu setup-dotnet](https://github.com/actions/setup-dotnet#caching-nuget-packages).
- Bước tiếp theo có thể tạo và commit NuGet lockfiles cho project liên quan, kiểm tra restore rồi chuyển sang `dotnet restore --locked-mode` và cache theo lockfiles. Đây là thay đổi riêng cần review.
- `latestFeature` và image tag hiện tại có thể thay đổi SDK/base image được resolve. Ban đầu giữ policy repository; sau baseline, thống nhất SDK patch/feature band và image digest nếu cần tái lập chặt hơn. Không âm thầm đổi SDK policy trong PR CI.

### Quyền và secrets

- Pin actions bằng full commit SHA đã kiểm tra, kèm comment version; dùng Dependabot để cập nhật. Không lấy SHA đoán hoặc tag cũ từ một mẫu workflow.
- Không cấp quyền write, deploy keys, Supabase credentials hoặc migration-owner credentials cho job chạy code PR. Không cần khai báo secrets thật cho MVP.
- Không đưa `.env`, browser session JSON, cookie, access token hoặc bằng chứng thật vào artifact/cache; artifact chỉ gồm kết quả test/build đã chọn.
- Nếu thêm browser artifact, dùng dữ liệu giả và kiểm tra screenshot/trace để không lộ test session. Nếu thêm secret scan có quyền riêng, tách job và chỉ cấp quyền tối thiểu cần thiết.
- Hạn chế runner tự quản cho code PR chưa tin cậy; ưu tiên runner tạm của GitHub lúc bắt đầu.

Các lựa chọn SHA pinning, token tối thiểu và xử lý code PR theo [GitHub secure use reference](https://docs.github.com/en/actions/reference/security/secure-use).

### Migration và runtime smoke

- Suite hiện có kiểm tra migrate từ database rỗng; các test migration riêng cần tiếp tục chạy. Không xem migrate fresh là bằng chứng mọi upgrade từ dữ liệu cũ đều an toàn.
- Lập thêm upgrade regression bằng fixture schema/dữ liệu tổng hợp của phiên bản trước trên container tạm khi migration thay đổi cần kiểm tra lịch sử. Không dùng dump dữ liệu thật.
- Xác nhận PostgreSQL major ở môi trường mục tiêu; sau đó quyết định đổi test baseline hoặc cho fixture nhận image từ cấu hình test để chạy matrix có chủ đích. Không chỉ thêm service PostgreSQL 18 rồi tưởng suite đã dùng nó.
- Runtime container smoke sau này cần DB tạm, bước migrate/provision chỉ trỏ container đó và app chạy bằng runtime role hạn chế. Không bỏ `ValidateRuntimeAsync`, không chạy app bằng owner để làm `/health` xanh.

## 6. Lộ trình triển khai theo PR

### PR 1 — CI cơ bản

Đầu ra: `.github/workflows/ci.yml`, actionlint verification, hướng dẫn đọc kết quả CI trong README hoặc tài liệu liên quan.

Thực hiện ba job và job tổng hợp ở phần 3; pin action SHA, thiết lập timeouts/concurrency, npm cache, TRX artifacts. Đầu tiên cho mọi flag là `true` để đo baseline, rồi thêm job changes và quy tắc skip vào cùng giai đoạn CI cơ bản sau khi kiểm tra gate. Chạy trên GitHub để kiểm tra thực tế Docker/Testcontainers và Linux, không kết luận từ local build.

Điều kiện hoàn thành:

- Một PR bình thường chạy thành công cả backend, web và Docker trên GitHub.
- Cố ý tạo một test thất bại trên branch tạm: job tương ứng và `CI / required` cùng thất bại; bỏ thay đổi thử sau khi xác minh.
- Build web lỗi TypeScript hoặc Dockerfile lỗi phải làm gate thất bại; không dùng `continue-on-error` cho kiểm tra bắt buộc.
- Commit mới hủy run cũ đúng PR; run lỗi vẫn có log và TRX nếu test runner đã tạo file.
- Không truy cập shared DB, không dùng secrets thật, không push/deploy.
- Docs-only PR bỏ qua ba job nặng và gate vẫn thành công; backend-only chạy backend/container; web-only chạy web/container; thay đổi cả hai chạy đầy đủ.
- File chưa có quy tắc, workflow thay đổi, diff không xác định được và sự kiện full-run đều không được trở thành docs-only. Kiểm tra rename, xóa file, PR nhiều commit và danh sách thay đổi lớn.
- Cố ý làm job bắt buộc bị skip hoặc output changes không hợp lệ: gate phải thất bại. Job không được yêu cầu nhưng thực tế đã chạy và fail cũng không được bỏ qua.

### PR 2 — Quy trình merge và dependency

Đầu ra: `.github/dependabot.yml`, cấu hình branch ruleset sau khi check đã xuất hiện, hướng dẫn xử lý CI đỏ.

- Require pull request, ít nhất một approval theo quy mô nhóm, resolve conversations và `CI / required`; giới hạn direct push/bypass theo quyền repo thực tế.
- Xác nhận ruleset/branch protection được gói GitHub hiện tại hỗ trợ. Đây là cấu hình GitHub, không tự xuất hiện chỉ vì đã thêm workflow YAML.
- Dependabot cập nhật theo tuần, nhóm cập nhật nhỏ khi phù hợp, tách major upgrade để review. Nếu cây `apps/web/grainledger` tiếp tục được bảo trì, quyết định lịch cập nhật riêng; không ngầm coi root lockfile đã quản lý cây này.
- Ghi baseline vài lần chạy: tổng thời gian từ enqueue tới hoàn tất, thời gian từng job, cache hit/miss và tài nguyên backend. Mục tiêu ban đầu là pipeline PR khoảng dưới 15 phút nếu runner/suite cho phép; đây là mục tiêu cần đo, chưa phải cam kết.

### PR 3 — Lint, coverage và browser smoke

Đầu ra: ESLint cho app chính, coverage reporting và browser smoke tự động có chủ đích.

- Bổ sung regression cho hành vi thiếu coverage: persisted-role/facility/owner boundaries, session/CSRF, mass conservation, immutable corrections và fail-closed policy.
- Browser smoke: đăng nhập bằng synthetic local account, kiểm tra session và điều hướng protected screen; thêm một workflow receiving/traceability trọng yếu khi fixture ổn định.
- `BrowserWorkflowHost` hiện là helper có điều kiện, không phải browser suite đã chạy trong `dotnet test`: thiếu `GRAINLEDGER_BROWSER_SESSION_FILE` thì test return ngay. Cần runner Playwright, quản lý process start/ready/stop, loopback origins, timeout và cleanup; không được báo “đã có E2E” chỉ vì backend suite xanh.
- Dùng production web build cho smoke nếu muốn phản ánh hành vi production. Cấu hình proxy `/api` và `/signin-google` rõ ràng cho preview/test server; Vite dev proxy hiện tại không tự chứng minh cấu hình preview đã đúng.
- Thử thực tế luồng browser trên DB tạm trước khi nâng thành required. Google consent và SMTP thật vẫn là kiểm tra môi trường riêng, không được chứng minh bởi fake providers.

### Đo hiệu quả và tối ưu tiếp theo

So sánh các run trước/sau theo nhóm docs-only, backend-only, web-only và full-stack; phân biệt cache lạnh/ấm. Ghi median và p95 khi đủ mẫu cho thời gian chờ runner, thời gian đến gate, từng job và tổng runner-minutes. Không lấy tổng duration các job song song làm thời gian người dùng đợi.

Ưu tiên tiếp theo dựa trên bottleneck:

- Restore chậm: thêm NuGet lockfiles/cache theo phần 5; npm cache vẫn chạy `npm ci` để xác minh lockfile.
- Docker chậm: đo cache-hit từng stage và kích thước context trước khi đổi Dockerfile. Cây frontend lồng trong `apps/web` được COPY vào context hiện tại; chỉ loại bỏ sau khi xác nhận nó không cần cho build.
- Backend thiếu RAM hoặc khởi động nhiều container: đo mức song song xUnit trước, giữ database cô lập theo fixture. Không dùng chung một DB giữa các test class hoặc cache DB volume để đổi tốc độ lấy nguy cơ nhiễm trạng thái.
- Integration suite thực sự chiếm phần lớn thời gian: cân nhắc chia nhóm test cân bằng theo timing, giữ toàn bộ test được chạy đúng một lần và kiểm tra không thiếu nhóm. Chưa shard ngay vì các job mới lặp restore/build và tăng runner-minutes.
- E2E/coverage/security: chạy tập quan trọng trong PR khi ổn định; tác vụ nặng bổ sung có thể chạy theo lịch. Không chuyển các test authorization/accounting hiện có ra khỏi gate PR.

Giữ workflow đơn giản: chưa thêm matrix nhiều OS/Node/PostgreSQL hoặc Redis/cache server khi chưa có nhu cầu tương thích cụ thể. Không dùng cache kết quả test hoặc tự retry toàn suite để che flaky test; ghi nhận nguyên nhân và xử lý fixture/test tương ứng.

## 7. Những quyết định cần xác nhận khi bắt tay triển khai

1. Đã chốt nhánh tích hợp là `dev`, sau đó đưa vào `main`; còn cần xác nhận có dùng merge queue không.
2. Repo public/private, gói GitHub và quyền quản trị ruleset hiện có?
3. PostgreSQL major được nhóm hỗ trợ là 17, 18 hay cả hai?
4. Ai nhận trách nhiệm xử lý CI đỏ và review Dependabot PR?

Các điểm này không ngăn viết workflow cơ bản với giả định được ghi rõ; chúng quyết định trigger cuối cùng, branch protection và công cụ bảo mật tùy chọn. Không cần xin credentials database để thực hiện CI này.

## 8. Phạm vi xác minh của bản kế hoạch

Phần khảo sát ban đầu dựa trên tài liệu nền tảng, cấu hình solution/workspaces, test fixture, browser host và Dockerfile; các hạn chế quan trọng đã được đối chiếu hướng dẫn GitHub chính thức. Workflow đã được chuẩn bị để kiểm tra local, nhưng chưa có lần chạy trên GitHub. Kết quả local được báo riêng khi bàn giao; không coi đó là bằng chứng runner GitHub đã đạt hoặc số đo hiệu năng production. Không thay đổi GitHub settings, runtime configuration hoặc shared database.
