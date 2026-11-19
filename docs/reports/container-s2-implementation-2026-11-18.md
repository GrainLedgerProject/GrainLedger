# Container S2 — triển khai và kiểm chứng, 18/11/2026

Đã triển khai các luồng S2 có căn cứ trong [plan S2](../plans/container-s2-implementation-plan-2026-11-18.md), trên nhánh `feat/container`, tiếp sau S1 `8fd96ac`. Code, migration và web đang nằm trong working tree, chưa commit/push. Sau lượt kiểm chứng cô lập, S2 đã được áp dụng lên Supabase cấu hình hiện tại theo phê duyệt riêng; xem [biên bản triển khai](container-s2-deployment-2026-11-18.md). Phần sau HOLD vẫn ngoài phạm vi theo quyết định của người dùng.

## Các luồng đã bổ sung

| Nhóm | Kết quả và ranh giới |
|---|---|
| Review sau partial | ALL_REMAINING/OUTPUT_ONLY giữ review/basis cũ; thay đổi evidence làm stale và giải phóng quota chưa dùng; review mới xác lập lại đúng scope, không tiêu lại inputs. |
| P1 → P2 và CLOSE | Draft P2 chỉ nhận demand chưa sản xuất/load; activation thu hồi reservation chưa dùng của plan trước. Load/output giữ proposal gốc; CLOSE kiểm mọi scope đã load và toàn manifest theo yêu cầu hiện tại. |
| Phần còn lại của phương án | Nhiều mẻ theo đúng recipe và quota duyệt, BigInt micro-kg ở web, exact decimal tại API/DB. Mỗi actual output cần evidence và review; không suy surplus/shortage policy. |
| Kho trộn, transfer, bagging | Operator lưu handoff, CM duyệt, Operator thực hiện bằng Ledger hiện có; release/INPUT authorization/consume/output claim/reserve cùng transaction. Extraction giữ source complement; ordinary transfer giữ identity; bagging giữ lineage và cần actual-output review. |
| HOLD | Notice đúng CM/Export/MM, retained read state, manager ACKNOWLEDGE/RECORD_RESPONSE và evidence theo facility. Isolation hàng còn lại dùng luồng Ledger HOLD hiện có. Không có release/resume, auto-HOLD hoặc unload/reopen/reseal. |
| Chứng từ/dossier | File shipment có version/hash/user/reason/time; supplements dùng evidence đúng company/facility sau CLOSED, không sửa snapshot CLOSE. Không tự tạo mẫu chứng thư/PDF/Excel. |
| Nghiệm thu | Focused integration/frontend checks, API/browser thực, dataset/oracle benchmark. Phân biệt rõ luồng thành công và chặn đúng. |

Các mutation giữ READ COMMITTED, advisory stock lock trước company/rows, kiểm lại actor/permission/company/facility sau chờ lock, replay đúng actor/action/payload. Kiểm lại scientific dependencies sau write, trước commit. Runtime không được quyền ghi trực tiếp các bảng mới; lịch sử load/evidence/dossier không bị sửa.

## Kiểm tra đã chạy

Hoàn tất code một lượt rồi mới vào pha kiểm thử; các lỗi phát hiện trong pha này được sửa và chạy lại nhóm liên quan. Không chạy toàn bộ backend.

| Check | Kết quả |
|---|---|
| Backend build, integration-test project | PASS; còn warning có sẵn CS9113, xUnit2031, xUnit2020. |
| Container + plan/order/candidate boundaries và ba nhánh stock liên quan | **65/65 PASS**, `/tmp/container-s2-test-results/container-s2-final.trx`. |
| Sau fix policy-expiry readiness: Workflow + Production | **25/25 PASS**, gồm hai regression mới cho expiry sau partial/full loading; `/tmp/container-s2-test-results/container-s2-expiry-final.trx`. |
| Sau fix trên: ContainerExecution | **8/8 PASS**, `/tmp/container-s2-test-results/container-s2-loading-final.trx`. |
| Specification mới sau partial production | **1/1 PASS**; thu hồi input/output quota chưa dùng, OUTPUT_ONLY review theo specification mới và load tiếp mà demand gốc không đổi; `/tmp/container-s2-test-results/container-s2-spec-final.trx`. |
| Bảy file frontend liên quan | **76/76 PASS**; Workflow, Production, Execution, ledger-ui, ExportOrders, BlendPlanning, LedgerWorkspace. |
| Web production build | PASS; còn cảnh báo chunk size hiện có. |
| Dataset benchmark riêng | **1/1 PASS**, `/tmp/container-s2-test-results/container-s2-season.trx`. |

Có 68 ca chức năng backend riêng biệt qua các lượt final/recheck trên, cộng một benchmark riêng. Không cộng các lượt chạy lại thành số ca mới. Regression gồm rollback sau input consume/posting/output claim, concurrent execution, revoked actor sau lock wait, tenant/facility/role denial, immutable history, reservation scope và source conservation. Fault hook expiry chỉ được cài trên DB Testcontainers, tạm bỏ immutable trigger của bảng fixture để thực sự làm window hết hạn tại write cuối; toàn mutation phải rollback. Browser dùng natural expiry, không sửa window đang test.

## Luồng web đã thực hiện

Dùng production web assets và BrowserWorkflowHost trên DB PostgreSQL dùng một lần; đăng nhập bằng cookie session thật qua cầu nối test loopback, vẫn chạy session validation, CSRF và API handler thật. Không dùng owner credentials làm runtime, không seed Supabase. Scientific fixtures đều ghi rõ SYNTHETIC, không phải phê duyệt khoa học vận hành.

- DIRECT_STOCK: load 4/10 kg, CM ghi quality observation mới trong limit; authority stale nhưng 4 kg giữ nguyên. CM duyệt OUTPUT_ONLY, Export load 6 kg còn lại, lưu seal/number và CLOSE thành công.
- Production: Export request; Operator thực hiện hai mẻ 10 kg với inputs 5+5 kg/mẻ. CM duyệt ALL_REMAINING sau mẻ đầu; load `4.000001`, thực hiện mẻ sau theo quota còn lại; CM duyệt actual outputs; load `5.999999` và `10`, CLOSE 20 kg thành công. Export dossier redacted giữ đủ ba loads. Một request production bị gián đoạn khi đổi phiên đã được phục hồi bằng Retry cùng operation.
- Pool/handoff: Operator lưu TRANSFER 10 kg từ pool 20 kg, CM duyệt, Operator commit actual aliquot; CM ghi quality/review rồi Export load `4.000001`. BAGGING `5.999999` còn lại theo đúng input/output, CM duyệt trước commit và review actual bagged output sau commit; Export load đủ 10 kg. Trace đúng hai nguồn, mỗi nguồn `5.000000`. Fixture này sau đó nhận HOLD ở LOADING để test notifications/handling; **lượt đầu không tính là browser PASS cho CLOSE của nhánh pool**. Lượt bổ sung với fixture mới không HOLD đã CLOSE thành công; xem walkthrough bổ sung bên dưới.
- Chứng từ/supplement trên container CLOSED: upload PACKING_LIST v1/v2, download đúng từng nội dung; upload evidence và append supplement. Dossier đọc trước/sau bằng nhau, trạng thái vẫn CLOSED.
- HOLD: CM đặt container HOLD; assigned MM thấy notice, đánh dấu read, upload evidence và ACKNOWLEDGE qua web. HOLD vẫn retained. Không thử release hoặc sửa fixture để chạy lại.
- CT-16: picker HOLD thực tế chọn được original RAW source đã hết tồn (`0.000000 kg`), tạo HOLD và notification; actual trace vẫn giữ 10 kg. Continuation blocking có API regression; không coi container CLOSED là một ca continuation đang mở.
- CT-25: retained-legacy fixture độc lập không có Capture mapping; browser trace báo incomplete/SOURCE_PROVENANCE_UNAVAILABLE, vẫn đủ 10 kg. Không xóa provenance hợp lệ để tạo lỗi.
- CT-26: tenant B Export paste trực tiếp execution/workflow/document file của A nhận 403; tenant B CM workflow/backward trace nhận 403. Dùng tài khoản được provision độc lập, không sửa actor ID trong request.
- CT-18: pre-expiry PASS và natural post-expiry MRL/policy blocking được kiểm qua browser; policy-expiry ban đầu trả lỗi thay vì readiness, đã sửa và có hai regression mới. Lượt browser cuối trên build đã sửa: trước expiry `05:04:58.6436317Z`, CLOSE PASS lúc `05:03:00.9547553Z`; refresh lúc `05:05:06.496Z` trả HTTP 200/readiness INCOMPLETE với ASSESSMENT_POLICY_MISSING và CLOSE_SCOPE_REVIEW_REQUIRED, authority null, vẫn LOADING/10 kg, không có CLOSE mới. MRL fixture trước đó PASS lúc `05:03:00.5862123Z`, sau expiry browser hiện APPLICABLE_MRL_MISSING, vẫn giữ 4 kg đã load. Các giờ trên là UTC.

Lượt bổ sung sau đó đã chạy đầy đủ P1→P2→CLOSE và pool→transfer→partial loading→bagging→CLOSE không HOLD trên web, cả hai PASS; xem [walkthrough bổ sung và bằng chứng](container-s2-web-walkthroughs-2026-11-18.md). Spec revision có API integration coverage. Danh sách trên bổ sung bằng chứng cho các ca PARTIAL cũ, không tự đổi cả CT-01–CT-30 thành PASS hoặc chạy lại CT-04–CT-10.

## Benchmark tracing

Profile `SYNTHETIC_SEASON_50_SOURCES_1000_CONTAINERS`: 50 official synthetic Capture sources qua receiving, 101 conversions thực bằng API (dry/mill mỗi nguồn, rồi blend), tổng load 25.000.000 kg, 1.000 actual container histories × 25.000 kg, 50.000 source rows. History lớn được bulk tạo trong DB cô lập từ một proposal/basis đã assess thật; đây không phải 1.000 lần load thủ công bằng UI.

Oracle kiểm đủ đúng tập 1.000 container khi forward trace từ một source, backward trả đủ đúng 50 source, mỗi source 500 kg/container, tổng đúng 25.000 kg. Không sampling hoặc cắt mất targets để đạt timing.

| Lượt warm | Forward HTTP + JSON (ms) | Backward HTTP + JSON (ms) |
|---|---:|---:|
| 1 | 661.5751 | 628.2982 |
| 2 | 716.9785 | 582.2199 |
| 3 | 720.6767 | 584.0073 |

Đạt ngưỡng 30 giây trên profile này. Đo bằng authenticated in-process HTTP handlers và JSON parse, PostgreSQL cô lập, ứng dụng/DB warm, máy 12 logical processors. Không bao gồm network/TLS, cold connection hoặc thời gian tải trang; chưa thay thế nghiệm thu tải đồng thời trên production. Truy vấn target đã được bulk tính restriction theo distinct actual lot để tránh lặp lại ancestry cho từng container; không cache hoặc bỏ kiểm scope/freshness. Metrics gốc: `/tmp/container-s2-season-benchmark.json`.

## Các giới hạn còn giữ và bàn giao

- Migration `20261118000000_ContainerWorkflow` và quyền EXECUTE mới đã deploy lên Supabase cấu hình hiện tại sau phê duyệt S2 riêng; [biên bản](container-s2-deployment-2026-11-18.md) ghi kết quả read-only/runtime checks. Không dùng lại phê duyệt S1, không chạy broad role reconfiguration hoặc đưa fixtures local lên DB chung.
- Direct Export loading từ current mixed pool vẫn bị chặn: phải qua reviewed Operator extraction. Other-plan pool reservations, generation/top-up làm đổi pinned input, hoặc attribution không chứng minh được vẫn fail closed; không tự rebind quota của người khác.
- Handoff BAGGING chỉ bằng nhau actual input/output và zero declared loss; cross-facility, ownership transfer, surplus/shortage shipment policy chưa có authority trong phạm vi này. Không tự tạo default tolerance hoặc compatibility.
- Dossier/phê duyệt không phải chứng nhận pháp lý; policy, MRL, lab và quality evidence hợp lệ vẫn phải do đúng bên cung cấp/phê duyệt.
- Sau HOLD, release/resume và unload/reopen/reseal nằm ngoài đợt này theo quyết định người dùng. Không có thêm S3 do developer tự đặt ra.


Lượt browser đã kết thúc; tab do agent mở và hai host test đã được đóng, file chứa session test tạm đã được xóa. Không lưu cookie/token trong repository. Các file TRX/metrics tạm nêu trên chỉ chứa bằng chứng chạy local; báo cáo này giữ kết quả để không phụ thuộc tuổi thọ `/tmp`.
