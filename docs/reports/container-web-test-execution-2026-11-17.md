# Kết quả chạy luồng container trên web — 17/11/2026

## Phạm vi và kết luận

Đã đăng nhập và thao tác trên web theo [hướng dẫn DIRECT_STOCK](../guides/container-execution-web-test.md), dùng các bước CT-04–CT-10 trong [checklist chi tiết](container-web-test-report-2026-11-16.md). Đường chuẩn từ fixture đã approve đến loading từng phần, CLOSE, dossier và trace hai chiều chạy thành công. Không gặp blocker nghiệp vụ trong đường này.

Đây là execution record riêng. Không đổi các ô NOT RUN trong checklist hoặc Report5/BM04; không tuyên bố hoàn tất toàn bộ 30 nhóm ca hay nghiệm thu ST-18/full-season ST-21.

Lượt tiếp theo đã chạy CT-01–CT-03 và CT-11–CT-30, ghi kết quả cùng các phần chưa kiểm đủ trong [báo cáo bổ sung ngày 17/11](container-web-test-remaining-2026-11-17.md). Những ghi chú NOT RUN bên dưới phản ánh phạm vi của riêng lượt CT-04–CT-10 này.

- Ngày chạy: 17/11/2026, khoảng 18:42–18:51, Asia/Ho_Chi_Minh.
- Source: `feat/container`, HEAD `1fc8efd`; không sửa code khi test.
- Web: Vite Development tại `http://localhost:5173`; API Release tại `http://localhost:5080`, restricted runtime connection đang cấu hình.
- Database: Supabase đang cấu hình, với quyền ghi test fixture được người dùng cho phép. Không migration, seed tổng quát hoặc đổi grants trong lượt test này.
- Trình duyệt: T3 shared preview cho login Export, metadata, hai lần load và reload; Playwright 1.57 với Brave 155.0.8059.85 cho phần CLOSE/dossier/history và hai phiên Export/CM riêng biệt. Viewport Playwright 1440 × 900; chưa xác nhận mobile.
- Password lấy từ cấu hình hiện có, không đưa vào báo cáo hoặc artifact. Không lưu cookie/token trong evidence.

## Fixture thực tế

- Công ty: `GrainLedger Demo Customer`.
- Order: `EXP-20261117-000004` / `03cc8d4a-3592-49bc-abb1-002473a5dc66`.
- Container: `CTN-20261117-000004` / `14d858b8-97df-4ee1-b175-b327819a55d3`.
- Target: `10.000000` kg; DIRECT_STOCK từ `HDR-20261117-000012` / `f0da14d7-fd02-5734-b8e1-f0e72d07a747`.
- Proposal: `53fcac60-67ea-4104-95e8-671927d6a95a`; approved plan v2: `33843317-2bc4-4521-a2d0-6a7bd2fa02ba`.
- Thị trường test: `DEMO-CONTAINER`; commodity giữ theo Ledger `PADDY`, sản phẩm giao `HEAD_RICE`. MRL/profile/quality là synthetic, không phải chứng nhận hàng thật.
- Container number: `DEMO-CONTAINER-20261117-01`; seal: `SEAL-DEMO-CONTAINER-20261117-01`.
- Nguồn: `PRC-20261117-0003`, sample `SMP-20261117-0002`, lab `LAB-DEMO-SMP-20261117-0002`.
- Source Ledger lot: `6aef567c-de87-5261-83a2-dad553970f63`; provenance về `PLT-0001-01`, mùa `2026`.

Order, proposal, approval, stock, lab/profile và metadata ban đầu được chuẩn bị bằng API ở lượt trước. CT-01–CT-03 không được chạy lại bằng trình duyệt trong đợt này.

## Các thao tác đã thực hiện

| Bước | Kết quả quan sát | Phạm vi xác nhận |
| --- | --- | --- |
| Đăng nhập Export Staff | PASS | Đăng nhập bằng form web; mở Export orders → order → container đúng fixture. |
| CT-04 metadata | PASS với giá trị đã điền sẵn | Bấm Save container metadata, reload trang; number/seal vẫn đúng, loading checks PASS, actual ban đầu 0. Không test biến thể giới hạn 80 ký tự. |
| CT-05 load lần đầu | PASS | Chọn approved position, nhập `4`; commit disabled trước checkbox. Tick xác nhận và click commit: HTTP 200, actual 4, remaining 6, một load. Mở lot từ manifest để kiểm stock; đối chiếu thêm bằng SELECT. |
| CT-06 reload và load tiếp | PASS | Reload vẫn một load 4 kg. Nhập `6`, xác nhận, commit qua web: HTTP 200; actual 10, remaining 0, hai load; closure checks PASS. |
| CT-07 CLOSE | PASS | Cancel hộp thoại không gửi close request; sau đó xác nhận CLOSE qua web, HTTP 200. CLOSED, hai load, một closure. Stock/quota không đổi so với ngay trước close. |
| CT-08 dossier | PASS cho đọc/tải và redaction | Export và CM đều bấm Read closure dossier rồi Download dossier JSON. Export nhận redacted view không có sourceContributions/evidence/per-load assessment; CM nhận đầy đủ nguồn và assessment evidence. Chưa test late HOLD/supplement trong ca này. |
| CT-09 history | PASS cho một trang | Năm event duy nhất: hai METADATA (lượt chuẩn bị và lượt web), hai LOAD, một CLOSE. Next history page disabled; không tạo thêm event để ép pagination. |
| CT-10 backward | PASS | CM bấm Trace container sources trên web: complete=true; một nguồn, `10.000000` kg, 100%, có plot provenance. Export không có nút backward. Chưa gửi direct API để kiểm 403 trong đợt này. |
| CT-10 forward | PASS | CM mở Ledger → Traceability, điền source lot, chạy Forward: complete=true, containerCoverage=ACTUAL_LOADING; đúng container CLOSED với linkedLoadedKg=`10.000000`. |
| Sau CLOSE | PASS cho visibility | Form metadata và actual loading không còn; vẫn đọc được dossier/history/trace. Không thử gửi mutation mới bằng API sau CLOSED. |

Các mutation metadata/load/CLOSE ở trên được phát sinh bởi form và nút web, không dùng HTTP script để thay thế thao tác UI. Truy vấn đối chiếu DB dùng transaction READ ONLY rồi rollback.

## Đối chiếu accounting

- Trước test: PLANNED; actual `0`; physical stock `10.000000`; consumed `0`; chưa có load.
- Sau load đầu: LOADING; actual `4.000000`; physical stock `6.000000`; consumed `4.000000`; một load.
- Trước CLOSE: LOADING; actual `10.000000`; physical stock `0.000000`; consumed `10.000000`; hai load.
- Sau CLOSE và trace: CLOSED; actual `10.000000`; physical stock `0.000000`; consumed `10.000000`; hai load, một closure; tổng source allocation `10.000000` kg.

Tồn và reservation cũ của `HDR-20261110-000006`/proposal cũ vẫn là `200.000000` kg. Không load, HOLD hoặc sửa các container cũ; không reset hoặc bù lại stock test đã tiêu thụ. Container demo này hiện đã CLOSED nên không còn là fixture chưa load cho lần test kế tiếp.

## Điểm hiển thị cần xem lại, không chặn đường chuẩn

1. Sau load đầu, Status ở đầu trang vẫn PLANNED trong khi Execution status đã LOADING. Reload cập nhật Status thành LOADING. Hai vùng hiển thị chưa đồng bộ ngay sau mutation.
2. Plan v2 đã được duyệt xuất hiện nhãn `Proposal saved · Demand changed` sau execution dù order/target không đổi trong lượt test; loading/CLOSE vẫn dùng đúng approval. Cần phân biệt thay đổi version do execution với demand thay đổi khi hiển thị.
3. Sau khi load hết quota, bảng Approved reserved stock hiện `Handoff needed` cho position còn 0 kg, dù closure checks PASS. Nhãn này dễ khiến người dùng hiểu nhầm rằng cần handoff để đóng.
4. Khi mở lot detail từ manifest, Vite ghi nhận React console error `Encountered two children with the same key`, key `6e76ab1e-f5f0-41b4-86c1-ff104fd7ef1b` (18:45:45). Đây là event của operation MILLING tạo hai output trong fixture. Chưa xác nhận dòng nào bị mất hoặc nhân đôi trên UI; accounting đọc từ DB vẫn đúng. Cần kiểm tra React keys ở phần detail/history.

Chỉ ghi nhận, không sửa code trong đợt kiểm thử này.

## Giới hạn và lỗi công cụ

- Sau khi dismiss dialog CLOSE, T3 preview báo stale element ref. Lệnh chờ dossier tiếp theo timeout vì lần click dùng ref cũ không thực hiện được. Chưa có close request thành công ở thời điểm đó. Chuyển sang Playwright, kiểm lại trạng thái và hoàn thành Cancel/CLOSE bằng UI; không bỏ qua confirmation hoặc giả response.
- Script Playwright đầu tiên timeout ở `getByLabel('Trace direction', {exact:true})`: label thực tế bao gồm cả text các option. Lượt chạy tiếp kiểm điều khiển thực, xác nhận giá trị Forward và dùng label matching phù hợp; forward trace thành công. Đây là lỗi locator của harness, không phải blocker của chức năng trace.
- Không có uncaught page error trong lượt Playwright forward hoàn tất. Các GET session 401 trước login và request mount bị abort trong Dev không được coi là lỗi nghiệp vụ.
- Chưa chạy CT-11–CT-30 đầy đủ: thiếu các fixture riêng cho HOLD/stale evidence/concurrency/nhiều nguồn/tenant B/blend-input/pool handoff; không tạo hoặc thay đổi chúng trong lượt này. Quan sát checkbox gate, close chưa đủ target bị disabled và form sau CLOSED chỉ là các kiểm tra UI phụ, không thay direct API security/race coverage.
- Không benchmark, không đo production load time, không remote CI hoặc staging/witnessed acceptance. Các lượt reload không được gọi là kiểm thử mất response/replay idempotency.

## Evidence local

Artifacts tạm ở `/tmp/grainledger-container-web-test-20261117/`; không commit credentials, evidence bytes hoặc dossier vào repository:

- `before.json`, `after-load4.json`, `before-close.json`, `after-close-trace.json`, `final-invariants.json`: đối chiếu READ ONLY.
- `afterFirstLoad.txt`, `afterSecondLoad.txt`, `stockAfter4.txt`: nội dung trang được quan sát trong T3 preview.
- `close-response.json`, `history.json`, `backward-trace.json`, `forward-trace.json`: response lấy từ request do UI phát sinh.
- `export-dossier.json`, `cm-dossier.json`: file thực tải bằng link Download dossier JSON của từng vai trò.
- `closed-export.png`, `backward-cm.png`, `forward-cm.png`: ảnh bằng chứng trình duyệt.
- `forward-browser-results.json`: log lượt forward cuối. Không dùng file `browser-results.json` làm tổng log mọi bước vì log của lượt tiếp tục đã thay file đó; các artifacts riêng và execution record này giữ kết quả tương ứng.

SHA-256 của file tải:

- Export: `8afa33032ea429c8c734260b6a896814f0084ace3f6b9aebe34321109004caf0`.
- CM: `78cc6dbc4286a8ea209c7d1c0a5abb29bac3e2f5943e06875fc4611bbc583383`.
