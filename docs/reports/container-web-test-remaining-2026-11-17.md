# Container web test — CT-01–CT-03 và CT-11–CT-30, 17/11/2026

## Kết luận

Đã chạy lại từ đầu CT-01–CT-03 bằng web, bỏ qua việc chạy lại CT-04–CT-10 theo yêu cầu, rồi thực hiện CT-11–CT-30 theo [checklist](container-web-test-report-2026-11-16.md). Các lần load hợp lệ phía sau dùng để thiết lập điều kiện riêng của ca âm, retry hoặc nhiều nguồn.

Trong 20 nhóm CT-11–CT-30: **16 nhóm PASS trong phạm vi mô tả, 4 nhóm PARTIAL** (CT-16, CT-18, CT-25, CT-26). Không gọi đây là hoàn tất mọi biến thể hay nghiệm thu toàn bộ luồng container. PASS ở CT-27/CT-28 nghĩa là hệ thống **chặn đúng nhánh chưa có handoff**, không phải đã giao hàng thành công qua những nhánh đó.

DIRECT_STOCK tiếp tục giữ đúng actual mass, reservation, stock và immutable dossier trong các ca đã chạy. Phát hiện một hạn chế UI đặt HOLD: picker lọc `availableOnly=true`, nên không chọn được lot bị reservation giữ hết available hoặc nguồn đã hết tồn. Backend CM vẫn đặt source HOLD thành công và container chặn continuation đúng.

## Môi trường và phạm vi ghi dữ liệu

- Ngày chạy: 17/11/2026, giờ Việt Nam; phần CT-11–CT-30 khoảng 19:34–19:54.
- Source `feat/container`, HEAD `1fc8efd`; API Release tại port 5080, Vite Development tại port 5173, Supabase đang cấu hình với restricted runtime connection.
- T3 shared preview cho CT-01–CT-03 và phần đầu các ca âm; Playwright 1.57 với Brave cho phần tiếp theo. Đăng nhập form thật bằng các tài khoản demo hiện có, sử dụng auth/CSRF của ứng dụng. Không lưu credential/cookie/token vào evidence.
- Người dùng đã cho phép tạo fixture container và tiếp tục CT-11–CT-30. Fixture chuẩn bị qua API chính thức: procurement → arrival → sample/lab confirmation → processing → quality → proposal/approval. Không migration, seed tổng quát, sửa schema/grants, hay sửa code ứng dụng trong lượt này.
- Tạo hai facility synthetic: `GrainLedger Demo Container Test Mill (synthetic)` và `GrainLedger Demo Container Quality Mill (synthetic)`. Assignment mới chỉ vào facility mới; chính sách sản xuất/compatibility mới chỉ phục vụ facility demo. Profile có code/method riêng; các market thiếu policy/MRL có code `DEMO-CONTAINER-*`. Đây là dữ liệu phần mềm giả lập, không phải chứng cứ khoa học hoặc chứng nhận xuất khẩu.
- Có 14 nguồn mới, mỗi nguồn 20 kg. Các ca HOLD/stale evidence dùng nguồn riêng để không chặn chéo fixture. Không reset, bù stock hoặc xóa HOLD sau test.
- Kết quả CT-04–CT-10 của lượt trước ở [execution record riêng](container-web-test-execution-2026-11-17.md).

## CT-01–CT-03 trên web

Order `EXP-20261117-000005`, container `CTN-20261117-000005` (`6cf6452e-f78f-4b86-88d0-8c440ee3096a`), BROKEN_RICE, target 10 kg, market DEMO-CONTAINER/PADDY. Sử dụng phần BROKEN 10 kg còn lại của fixture demo trước; bổ sung reviewed quality bằng web cho đúng lot đó.

| Ca | Kết quả | Bằng chứng |
| --- | --- | --- |
| CT-01 | PASS | Tạo order/container trên web. PLANNED, loaded 0; chưa approve thì có `ACTIVE_APPROVAL_REQUIRED`. Physical 10, reserved 0 kg. |
| CT-02 | PASS | Calculate plan PASS, lưu proposal 201. Solver bỏ qua stock cũ không đủ bằng chứng và chọn đúng BROKEN demo 10 kg. Physical 10, reserved 0 kg. |
| CT-03 | PASS | CM approve bằng web, 200, decisionVersion 1. Physical vẫn 10, reserved 10, available 0, consumed 0 kg. |

Approval của container này sau đó được revoke có chủ đích trong CT-11: release 10 kg, consumed 0, actual 0. Container này hiện không còn active approval.

## Kết quả CT-11–CT-30

Các mã rút gọn dưới đây đều có tiền tố `CTN-20261117-`.

| Ca | Kết quả | Thao tác và kết quả thực tế |
| --- | --- | --- |
| CT-11 | PASS | Chưa approve ở CT-01 bị chặn. CM web revoke `000005`, release đúng 10 kg, không trừ physical. Fixture `000020` được CM web reject kèm lý do; Export thấy `ACTIVE_APPROVAL_REQUIRED`, actual 0. |
| CT-12 | PASS | `000006` load 4/10 kg; direct close trả 409 với `EXACT_TARGET_NOT_LOADED` và `SEAL_METADATA_MISSING`. Load thêm 6: vẫn thiếu metadata. Chỉ lưu number, seal trống: còn `SEAL_METADATA_MISSING`. Lưu đủ number/seal: closure PASS, nút close mở. Sau đó dùng fixture này cho CT-18 trước close. |
| CT-13 | PASS | Load 11 vào target 10 trả 409. `000008` load 4 rồi thử 7 trả 409, loaded vẫn 4, stock/quota còn 6. `000013` target 20 với hai positions mỗi vị trí 10: thử 11 ở một vị trí trả 409 dù tổng quota đủ. |
| CT-14 | PASS | Trống quantity/position hoặc chưa xác nhận: không mở commit hợp lệ. Đổi quantity/position reset xác nhận. `-1`, `abc`, `1,5`, `1e3`, `0.0000001` không qua browser validity. `0` gửi tới backend trả 400. `4.000001` được ghi chính xác ở CT-21/CT-24. |
| CT-15 | PASS | `000007` HOLD trước load, `000008` HOLD sau 4 kg. CM web đặt HOLD; Export bị `CONTAINER_HOLD`. Direct continuation 409, actual/stock/quota không đổi. Reason rỗng, khoảng trắng, 1.001 ký tự bị API từ chối 400; Export POST HOLD bị 403 ở CT-26. |
| CT-16 | PARTIAL | UI không tìm được lot reserved hết available/nguồn hết tồn. CM gọi API thật để HOLD nguồn riêng của `000009`, rồi kiểm trên web: `LOT_RESTRICTED`, load disabled, direct load tiếp 6 trả 409. Actual giữ 4, quota 6; không tự sinh container HOLD. Chức năng backend PASS, đường đặt HOLD hoàn toàn qua picker UI chưa hoàn tất. |
| CT-17 | PASS | `000010`: CM web thêm quality 13.1 trong max 14 → `APPROVAL_EVIDENCE_CHANGED`. Thêm version 15 → thêm `QUALITY_LIMIT`. Cả hai lần direct load bị 409, actual vẫn 0, stock/quota không bị consume. |
| CT-18 | PARTIAL | `000011` đã load 4: quality 13.1 mới làm stale approval, continuation 409, actual vẫn 4. `000006` đã đủ 10 và đủ metadata: quality mới khiến close 409, loaded vẫn 10. Chưa chạy biến thể MRL/policy hết hiệu lực tại ranh giới thời gian. |
| CT-19 | PASS | Web Calculate plan trên fixture riêng: `000017` thiếu lab → `RESIDUE_COVERAGE_INCOMPLETE`; `000018` thiếu policy → `ASSESSMENT_POLICY_MISSING`; `000019` thiếu MRL → `APPLICABLE_MRL_MISSING`; `000022` có lab nhưng thiếu confirmed finished quality → `QUALITY_EVIDENCE_MISSING`. Tất cả INCOMPLETE, không có preview để propose thành công. |
| CT-20 | PASS | Container demo đã CLOSED `000004` không có form load/metadata. CM đọc dossier rồi đặt late container HOLD bằng web. Status vẫn CLOSED, loaded 10; dossier đọc lại bằng CM giống nguyên bản trước HOLD. |
| CT-21 | PASS | `000012`: offline trước commit `4.000001`, UI giữ pending và nút Retry. Online trở lại, state trước retry chưa đổi; retry dùng nguyên body lưu trong sessionStorage, ghi đúng một load `4.000001`. |
| CT-22 | PASS | Chặn response ở trình duyệt sau khi `route.fetch()` đã nhận 200 từ server cho load 0.5 kg. Giữ pending; retry gửi đúng payload/operationId, trả receipt cũ. Same replay 200; đổi quantity cùng operationId trả 409. Manifest/stock/quota không đổi sau replay. Xem ghi chú harness bên dưới. |
| CT-23 | PASS | Hai context Export đăng nhập riêng cùng mở version hiện tại của `000012`. A load 1 thành công; B gửi version cũ trả 409 `Container version changed; reload`. Actual cuối 6.500001 kg, không ghi request B. Đây là kiểm stale session; race đồng thời được kiểm riêng bằng integration test. |
| CT-24 | PASS | `000014` DIRECT_STOCK từ BLENDED_RICE đã sản xuất trước proposal: physical 20, reserve/target 10, hai nguồn gốc mỗi nguồn 10. Web load `4.000001` + `5.999999`; CM trace ra hai nguồn mỗi nguồn `5.000000`, share 50%. CLOSE thành công, physical còn `10.000000`, không debit lần hai. |
| CT-25 | PARTIAL | `000007` trước load: CM web trace incomplete, findings `NO_ACTUAL_LOADING`, sourceContributions rỗng; UI hiện cảnh báo provenance incomplete. Forward từ nguồn không coi container dự kiến là actual target. Chưa có fixture actual thiếu original Capture plot; không sửa provenance để tạo ca này. |
| CT-26 | PARTIAL | Export không có form CM HOLD; direct HOLD/backward trace 403. CM có trace/HOLD, không có Export loading. Purchasing, Mill Operator và Administrator bị từ chối direct loading; các read/history/dossier đã kiểm theo quyền. Export dossier có `redacted=true`, không có sourceContributions/evidence; CM có đủ hai source rows. Chưa có tài khoản Export/CM tenant B đã xác thực để test dán URL trên web. Foreign-company backend denial được phủ bởi integration test, không thay kết quả web tenant B. |
| CT-27 | PASS chặn | `000015` có approved `BLEND_INPUT_STOCK`: `BLEND_OUTPUT_HANDOFF_REQUIRED`, loading disabled, direct load 409, actual 0. Chưa có E2E sản xuất blend sau duyệt → output reservation → loading. |
| CT-28 | PASS chặn | `000023` có approved DIRECT_STOCK từ current mixed HEAD_RICE pool, đủ lab/current quality. Web hiện Handoff needed và `POSITION_OR_SOURCE_HANDOFF_REQUIRED`; form disabled, direct load 409; stock/reservation/actual 0 không bị đổi bởi request từ chối. |
| CT-29 | PASS | Export web tạo `DEMO-CONTAINER-LEGACY-20261117-01`, lưu legacy loading draft 1 kg từ BRAN demo. Legacy committedLoadedKg vẫn `0.000000`; physical BRAN vẫn `20.000000`; planned container đối chiếu vẫn actual 0. Legacy Commit loading/Close container disabled. |
| CT-30 | PASS phạm vi kỹ thuật đã chạy | 11 direct API checks: numeric JSON mass, >6 decimals, unknown approved/ready/actor, duplicate positions, ngoài approval, history 101, metadata 81 ký tự, load/metadata mới sau CLOSED đều bị từ chối đúng, không đổi state. Close sớm/replay/quyền đã kiểm ở các ca trên. Chạy thêm 8 ContainerExecutionTests trên PostgreSQL Testcontainers cô lập: 8 PASS, 0 skipped. |

## Accounting và fixture còn lại

Đối chiếu cuối bằng API thực:

| Container | Actual kg | Trạng thái sau test |
| --- | --- | --- |
| 000005 | 0 | Approval revoked, 10 kg đã release. |
| 000006 | 10.000000 | LOADING, stale evidence chặn close. |
| 000007 | 0.000000 | Container HOLD, reservation còn 10. |
| 000008 | 4.000000 | Container HOLD, reservation còn 6. |
| 000009 | 4.000000 | Source HOLD, reservation còn 6; không có container HOLD. |
| 000010 | 0.000000 | Quality 15 và stale evidence, reservation còn 10. |
| 000011 | 4.000000 | Stale evidence, reservation còn 6. |
| 000012 | 6.500001 | LOADING, 4 loads, reservation/target remaining 3.499999. |
| 000013 | 0.000000 | Approved hai positions, reservation còn 20. |
| 000014 | 10.000000 | CLOSED, hai nguồn mỗi nguồn 5; physical finished lot còn 10. |
| 000015 | 0.000000 | Blend-output handoff blocker, reservation còn 20. |
| 000023 | 0.000000 | Pool/source handoff blocker, reservation còn 20. |

Các container 000016–000022 là fixture draft/rejected/missing-input, không có actual load hoặc active approval. Một số draft trung gian được giữ lại; chúng không đại diện cho fixture hoàn chỉnh để chạy lại. Sau khi test quality missing ở facility riêng, chính stock test đó được dùng chuẩn bị current pool cho CT-28; snapshot quality-missing trước đó không còn là trạng thái hiện tại của stock.

Tổng actual của các case tại hai facility mới: **38.500001 kg**. Các fixture âm cố ý giữ HOLD, stale approval và reservation; không tự giải phóng để làm chúng trở lại PASS. Container 000004 của lượt trước có late HOLD mới theo CT-20 nhưng dossier cũ không đổi.

So sánh hash `md5(row_to_json(row)::text)` với baseline trước khi tạo bộ demo: **250 dòng nghiệp vụ trong 57 bảng không có dòng bị đổi hoặc mất**. Đây là phạm vi baseline đã ghi nhận, không phải tuyên bố mọi bảng/counter/session trong database không thay đổi. Lot cũ `HDR-20261110-000006` (`4712d7e6-4fde-477e-95a9-a8280e694fee`) vẫn physical 200 kg; reservation cũ nằm trong baseline cũng giữ nguyên 200 kg.

## Các điểm chưa thể coi là hoàn tất

1. **UI đặt HOLD:** picker dùng free available stock khiến không chọn được chính lot đang reserved hoặc nguồn đã hết tồn. CT-16 chỉ hoàn tất phần backend + web quan sát chặn sau khi đặt bằng API CM. Không sửa code trong lượt này.
2. **CT-18:** chưa có kiểm thử ranh giới effective time của MRL/policy. Quality freshness trước/sau partial execution và trước close đã kiểm; không suy rộng thành mọi dependency race.
3. **CT-25:** chưa có fixture actual thiếu Capture plot provenance. Không phá hoặc xóa provenance của dữ liệu hợp lệ để tạo fixture.
4. **CT-26:** thiếu verified peer-company Export/CM account cho web. Không tạo account/session giả hoặc gửi email đăng ký ngoài phạm vi test.
5. Các handoff CT-27/CT-28 vẫn là giới hạn chức năng đã nêu trong docs. Kết quả chặn đúng không hoàn thành blend-after-approval, pool successor rebinding hoặc reviewed revalidation sau partial loading.

## Harness, automation và bằng chứng

- Preview chuyển sang Playwright sau hai lỗi helper: `Expected a string with no leading or trailing whitespace`, rồi `ct is not defined`. Đây là lỗi harness, không tính là lỗi ứng dụng. Các locator ban đầu dùng exact label có text option hoặc chưa tìm qua pagination đã được sửa và chạy lại; kết quả cuối theo artifact thành công.
- Lượt đầu CT-22: helper kiểm tra biến response quá sớm. Server đã ghi thêm 1 kg, được xác nhận bằng API trước khi tiếp tục; không tính lượt đó là bằng chứng đầy đủ CT-22. Lượt xác minh sau dùng 0.5 kg, lưu accepted response/payload trước khi bỏ response và retry. Vì vậy `000012` có bốn load hợp lệ: `4.000001`, `1`, `0.5`, `1` = `6.500001` kg. Không có ghi đôi do replay.
- Fault injection mất response chỉ áp dụng đúng request container demo trong context trình duyệt riêng trên môi trường được phép ghi fixture; không tạo trigger hoặc sửa receipt trên Supabase. Fault injection rollback sau consume chỉ chạy trong Testcontainers cô lập.
- Chuẩn bị CT-28 ban đầu thử chuyển phần còn lại của BLENDED nhiều nguồn sau actual consumption; API trả 409 `Multi-source stock withdrawal attribution is ambiguous`, không chuyển stock. Fixture cuối dùng nguồn HEAD_RICE riêng đi vào pool mới, với confirmed quality đúng current pool generation. Không dùng việc có pool/stock để tự nhận đã có container handoff.
- Fixture quality thiếu ban đầu dùng BRAN không đủ điều kiện quality API, rồi một draft HEAD_RICE chọn nguồn thiếu lab. Chúng không được dùng làm bằng chứng quality-missing. Ca cuối ở facility riêng `000022` chứng minh residue PASS và `QUALITY_EVIDENCE_MISSING` qua web trước khi stock được đưa vào pool.
- Không chạy production benchmark/mobile, CI hoặc witnessed acceptance. Không commit/push và không sửa source ứng dụng.

8 integration tests đã chạy thuộc `ContainerExecutionTests`: direct partial/close/immutable dossier; actual trace và foreign-role denial; HOLD/current quality; concurrent load/changed replay/rollback sau consume; thu hồi role trong lúc chờ lock; UT-14 phân bổ 25.000 kg và private helper denied; preproduced multi-source complement; late source HOLD. Đây là coverage PostgreSQL cô lập, được ghi riêng với kết quả browser.

Evidence local tại `/tmp/grainledger-container-web-start-20261117/`:

- `ct01-03-results.json`, `ct01-stock.json`, `ct02-stock.json`, `ct03-stock.json`, `ct11-revoke.json`.
- `preview-results.json`: CT-12/13/14 và các preconditions quan sát bằng shared preview.
- `CT-*.json`: response, payload và đối chiếu state của từng ca thành công. Các file `remaining-batch*.json` và `regression-results.json` giữ cả lỗi harness; không dùng mọi chữ BLOCKED ở lượt đầu làm kết quả cuối của ca đã chạy lại thành công.
- `ct22-accepted.json`, `CT-22-lost-response-and-replay.json`, `CT-23-stale-session.json`, `CT-24-multisource-micro-kg.json`, `CT-30-DTO-boundaries.json`.
- `ct28-pool-handoff.png`, `ct29-legacy.png`.
- `fixtures/state.json`, `fixtures/cases.json`: manifest/API setup của đúng fixture synthetic; không có access token.
- `final-state.json`, `final-preservation.json`: trạng thái cuối và đối chiếu baseline.
- `integration-results/container-regression-20261117.trx`: 8 PASS.

Các artifact ở `/tmp` là bằng chứng cục bộ tạm, chưa phải kho lưu trữ nghiệm thu lâu dài. Không đưa evidence bytes/dossier hoặc secret vào repository.
