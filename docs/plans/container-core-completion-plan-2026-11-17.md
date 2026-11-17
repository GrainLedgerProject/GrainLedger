# Plan hoàn thiện luồng chính container — 17/11/2026

## 1. Mục tiêu và trạng thái

**Cập nhật 18/11/2026: S1 đã có code, kiểm thử cô lập/web và đã áp dụng migration cùng quyền runtime mới lên Supabase cấu hình hiện tại theo phê duyệt riêng của người dùng.** Xem [verification record](../reports/container-production-implementation-2026-11-18.md), [biên bản triển khai DB](../reports/container-production-deployment-2026-11-18.md) và [API/state contract](../api/container-production-api.md). S2/S3 vẫn là backlog. Plan gốc được lập ngày 17/11 trước khi triển khai. Phê duyệt vừa thực hiện chỉ áp dụng migration/quyền trong biên bản, không tự cho phép các thay đổi DB hay dữ liệu tiếp theo. Người dùng yêu cầu tập trung luồng chính trước deadline và chuyển phần nhiều ràng buộc sang sprint sau.

Mục tiêu sprint gần nhất: chạy được trên web **order → proposal BLEND_INPUT_STOCK → CM approve/reserve inputs → nhà máy blend thật → giữ và review actual output → actual loading từng phần → CLOSE → dossier/trace**. Duy trì đường DIRECT_STOCK đã chạy được.

Chưa có ngày deadline, độ dài sprint hoặc capacity cụ thể; các sprint dưới đây là đề xuất phân phạm vi và thứ tự, không phải cam kết thời gian. Không mở rộng sprint hiện tại chỉ để đánh dấu đủ mọi chức năng SRS.

Đầu ra của lượt lập plan ban đầu (17/11): duy nhất tài liệu này và liên kết trong documentation index. Không sửa code, không khởi động ứng dụng, không thay đổi DB, không commit/push. Các checkbox chưa đánh dấu là công việc tương lai.

## 2. Căn cứ và baseline

- [Kết quả CT-01–CT-03, CT-11–CT-30](../reports/container-web-test-remaining-2026-11-17.md): DIRECT_STOCK, accounting, retry, dossier và trace đã kiểm; 16 nhóm PASS theo phạm vi, 4 nhóm PARTIAL. Không suy ra mọi biến thể đã nghiệm thu.
- [Kết quả CT-04–CT-10](../reports/container-web-test-execution-2026-11-17.md): đường loading/close thông thường.
- [Luồng nghiệp vụ](../reports/container-end-to-end-business-flow-2026-11-16.md), đặc biệt bước 4 về actual production và output reservation.
- [Plan ngày 13/11](container-execution-plan-2026-11-13.md): baseline đã triển khai và giới hạn; tài liệu này bổ sung thứ tự công việc tiếp theo, không sửa kết quả lịch sử.
- [SRS FR-EXP-003–005](</home/phucpinku/Desktop/Week1_report/review/Report3_Software Requirement Specification.docx.md:419>), [Business Rules R03/RV02/RV03](</home/phucpinku/Desktop/Week1_report/review/erd_v2_1/BUSINESS_RULES.md:64>): input/output reservations, execution, OUTPUT_ONLY/ALL_REMAINING, review mới, CLOSE và dossier. Đây là yêu cầu đã có, không ghi chung là “chờ nghĩ nghiệp vụ”.
- [Proposal v1.1](</home/phucpinku/Desktop/Week1_report/review/FA26SE272_GrainLedger_Proposal_v1.1.docx.pdf>): trang 4 và 11 xác định planning/approval/loading là core. Tài liệu local là căn cứ đọc, không copy vào Git hoặc sửa trạng thái nghiệm thu của team.
- [Architecture](../architecture.md), [Ledger plan](ledger-implementation-plan.md), [setup/deployment](../setup-and-deployment.md): transaction, exact mass, quyền, code issuance và triển khai.

Baseline code được đọc: `feat/container`, `1fc8efd`. Khi bắt đầu triển khai phải kiểm lại diff/schema/contracts của nhánh đích; không dựa vào baseline này để ghi đè thay đổi mới của người khác.

## 3. Phạm vi sprint gần nhất — S1

### 3.1 Đường chính phải hoàn tất

1. Export lập order/container và phương án; CM approve input selection bằng workflow hiện có.
2. Export tạo yêu cầu sản xuất từ approval; Mill Operator có assignment đúng facility thực hiện. Không cấp quyền sản xuất cho Export hoặc quyền kinh doanh của khách cho Operator.
3. Một mẻ BLENDING tiêu thụ đúng input reservations, ghi actual outputs/loss/variance bằng Ledger hiện có.
4. Trong cùng transaction sản xuất, ghi liên kết approval/execution/output và giữ output cho đúng container. Output được giữ chưa đồng nghĩa đủ điều kiện loading.
5. Đánh giá actual output bằng lab/residue/quality/MRL/policy hiện hành; CM review output trước loading, ghi quyết định mới theo phạm vi OUTPUT_ONLY đã có trong SRS. Không sao chép PASS/fingerprint giả lập của input sang output.
6. Export load output đã được phép qua nhiều lần; consume output reservation, debit stock và lưu actual source kg nguyên tử.
7. CLOSE đúng target, dossier bất biến, trace hai chiều từ actual loads. CLOSE không debit stock lần hai.

### 3.2 Giới hạn có chủ đích

| Nội dung | S1 hỗ trợ | Chưa mở trong S1 |
| --- | --- | --- |
| Plan/execution | Một plan có quyền hiện hành, một mẻ hoàn tất cho một container | Nhiều mẻ/partial production; đổi công thức hoặc chuyển plan sau execution |
| Stock | Inputs/finished output ở ordinary positions, source complement chứng minh được | Mixed pool/custody generation; bagging/transfer output đã reserve sang vị trí mới |
| Output | Một output giao hàng; actual eligible quantity đúng target và đúng product/compatibility được duyệt | Dư/thiếu output so với target; chia output sang nhiều container; tự chuyển phần dư thành free stock |
| Processing | Dùng policy có evidence, actual loss/variance và manager confirmation hiện có khi cần | Tolerance/loss/yield mặc định; coi predicted output là actual output |
| Review | Review actual output trước lần load đầu; có thể review lại output đó trước loading với current stock/evidence | Full revalidation sau partial loading, ALL_REMAINING nhiều giai đoạn và CLOSE qua nhiều plan |
| Loading | Nhiều lần loading đúng output quota, exact kg, stock và source accounting | Unload, reopen/reseal, đóng thiếu hoặc tự đổi target |

Đúng target không đồng nghĩa luôn zero loss/variance. S1 vẫn cần processing policy hợp lệ và confirmation theo Ledger; fixture demo có thể dùng cân bằng chính xác nhưng không biến con số fixture thành policy hệ thống. Nếu actual output không đúng target, từ chối commit của đường S1 trước khi có bất kỳ posting/consume nào; không ghi production một nửa rồi để output không có người sở hữu.

Thành phẩm đã ghi đúng nhưng thiếu quality hoặc assessment không PASS được giữ ở bước chờ review, không load. CM reject/invalidate trước loading phải có chuyển trạng thái và release phần output chưa consume trong cùng transaction, giữ production history; release reservation không gỡ HOLD. Review lại trước loading chỉ reserve lại output còn thực sự tồn tại/đủ điều kiện. Nếu contract xử lý này chưa triển khai được thì dừng ở gate thiết kế, không đưa nhánh production ra sử dụng.

## 4. Những nút thắt đã xác định trong code

| Điểm | Hiện trạng | Việc phải làm |
| --- | --- | --- |
| [BlendExecutionAuthorization](../../backend/src/GrainLedger.Api/Features/Exports/Planning/BlendExecutionAuthorization.cs) | Helper bắt ExportStaff và đổi container sang LOADING khi start | Tách quyền yêu cầu/phê duyệt/sản xuất; có execution marker riêng. Không gọi helper dưới actor giả, không coi sản xuất là actual loading. Chặn revoke/edit target sau production dù loaded còn 0. |
| [LedgerService.CommandAsync](../../backend/src/GrainLedger.Api/Features/Ledger/LedgerService.cs) | Mở connection riêng cho command thông thường | Bổ sung đường nội bộ nhận caller connection/transaction cho orchestration; không gọi HTTP/gọi command tự commit rồi mới cập nhật reservation. Giữ hành vi callers khác. |
| [BlendApprovalReservation.sql](../../backend/src/GrainLedger.Api/Data/Sql/BlendApprovalReservation.sql) | Reservation theo proposal/lot/location, chưa thể hiện đầy đủ input→production→output handoff | Bổ sung identity/purpose/reference và event cho authorized input consume/output reserve; giữ tương thích DIRECT_STOCK. |
| [ContainerExecutionEndpoints](../../backend/src/GrainLedger.Api/Features/Exports/Shipments/ContainerExecutionEndpoints.cs) | Readiness chặn mode khác DIRECT_STOCK; đánh giá selection/fingerprint gốc | Đọc output authorization thực, assessment trên output thực và full manifest khi close. Không chỉ bỏ điều kiện mode hoặc sửa snapshot gốc thành DIRECT_STOCK. |
| [ContainerExecution.sql](../../backend/src/GrainLedger.Api/Data/Sql/ContainerExecution.sql) | Consume/loading hiện gắn approved selection trực tiếp | Hỗ trợ đúng output reservation, giữ source complement và immutable receipts; chặn nhầm INPUT reservation ở loading. |
| UI và quyền nhà máy | Có processing độc lập, chưa có đầy đủ work item từ approval tới output | Nối yêu cầu sản xuất và execution thật; Operator chỉ thấy dữ liệu đủ cho vận hành được giao, không mở toàn bộ hồ sơ/lab của khách. |

Đây là việc tích hợp trong modular monolith hiện có, không mặc định yêu cầu một hệ thống bên thứ ba mới. Trách nhiệm thiết kế theo Exports/Ledger/assessment; chưa gán tên thành viên hoặc coi contract đã được owner xác nhận. Những thay đổi chạm module khác phải được review cùng, nhưng không dùng phân công team làm lý do bỏ yêu cầu core.

## 5. Backlog S1 theo thứ tự phụ thuộc

| ID | Đầu việc | Phụ thuộc | Đầu ra/điều kiện đạt |
| --- | --- | --- | --- |
| C01 | Chốt contract lát cắt S1 | Không | Ghi rõ state transitions, actor/assignment, input/output reservation purpose, review pre-load, quota, lock order và xử lý reject/rollback. Một plan có quyền; không đổi business status container chỉ vì start production. |
| C02 | Thiết kế schema và forward migration | C01 | Execution/request/output mappings, authorization/review refs, reservation events và replay identity có constraints. Dữ liệu đã commit không bị sửa; schema cũ DIRECT_STOCK vẫn đọc được. Có finite runtime entry grants, private helpers không được mở trực tiếp. |
| C03 | Tạo yêu cầu sản xuất và draft từ approval | C01–C02 | Export chỉ yêu cầu cho own company; Operator chỉ xử lý task thuộc facility được giao. Inputs lấy từ approval, không tùy chọn lot ngoài quota. Nhập actual output/loss/evidence; ràng buộc đúng một mẻ/output S1 ở backend. |
| C04 | Commit approved BLENDING nguyên tử | C02–C03 | Fresh approval + actor/assignment + stock/HOLD/policy checks; INPUT consume + negative input postings + actual output + lineage + output reserve + execution marker + audit/receipt cùng transaction. Recheck sau lock wait. Không mở đường consume tùy ý cho processing thường. |
| C05 | Review actual output trước loading | C04 | CM đọc actual output, quality/residue/current policy, review OUTPUT_ONLY mới. PASS mới cấp loading authority; missing/FAIL giữ blocked. Reject/invalidation release theo scope trước loading, retain history; re-review không chạy lại input. |
| C06 | Loading/CLOSE/dossier/trace từ output | C04–C05 | Backend đọc output authorization, exact remaining; loading consume OUTPUT, giữ source complement; CLOSE đánh giá toàn actual manifest và current evidence. Readiness giải thích production pending/review pending/blocked. DIRECT_STOCK không đổi hành vi. |
| C07 | Nối web theo vai trò | C03–C06 | Export request/progress/output → Operator production → CM output review → Export load/close/dossier. Hiển thị plan/execution/output IDs, quota và lý do chặn đủ dùng; không bắt người dùng nhập UUID thủ công để nối record. |
| C08 | Sửa UI ảnh hưởng demo | Độc lập, ghép trước C09 | HOLD picker chọn được lot reserved hết/nguồn hết tồn đúng scope; status refresh sau mutation; phân biệt demand revision với execution version; position quota 0 không bị gán nhãn handoff sai; key React cho event nhiều output duy nhất. |
| C09 | Regression + actual web/API smoke | C04–C08 | Tests bảo vệ invariants, race/rollback/quyền; chạy web thật trên DB test cô lập có auth/CSRF. Có accounting trước/sau và bằng chứng luồng S1 + DIRECT_STOCK cũ. |
| C10 | Docs và package bàn giao | C09 | API/state/role docs, walkthrough, test record mới, migration/grant preflight và known limitations. Ghi kết quả thực, không đổi DOCX/BM04 thành PASS thay người nghiệm thu. |

Đường găng: **C01 → C02 → C03/C04 → C05 → C06 → C07 → C09 → C10**. C08 có thể làm độc lập khi triển khai, nhưng không coi vài fix UI là hoàn tất luồng blend. Checklist thực hiện:

- [x] C01–C02: contract/schema đã đối chiếu và kiểm trên PostgreSQL cô lập; không tuyên bố có external owner sign-off.
- [x] C03–C05: chạy được production và review output với persisted authority thật.
- [x] C06–C08: người dùng thực hiện hết lát cắt S1 trên web.
- [x] C09–C10: checks đạt, có bằng chứng và tài liệu bàn giao.

## 6. Transaction, authorization và accounting bắt buộc

- Dùng cùng Npgsql connection, caller-owned READ COMMITTED transaction và advisory lock `714928352` trước rows. Module routine không tự commit. Không giữ transaction trong lúc chờ thao tác người dùng/upload hoặc gọi dịch vụ ngoài.
- Input reservation consume chỉ đi qua execution được cấp quyền của đúng plan/company/facility. Output reserve và mọi output postings cùng commit; không release input trước rồi gọi processing ở request khác.
- Production/lab observation/review/loading là các thao tác khác nhau. Không chờ CM review trong transaction sản xuất; output đang chờ review phải có trạng thái/claim được bảo vệ.
- Operation identity gồm actor/scope/action/payload/version theo local pattern. Same replay trả receipt cũ; changed payload conflict; replay không tạo execution/output/reservation mới.
- Client không truyền actor/confirmer hoặc cờ approved/ready để cấp quyền. Administrator không wildcard. Permission/assignment/approval phải còn hợp lệ sau khi chờ lock.
- Mass là decimal string, kg `numeric(20,6)`, source allocation dùng integer micro-kg. Reuse existing processing lineage và container complement; không lấy tổng tỷ lệ đã làm tròn để hạch toán.
- Giữ immutable proposal/review/execution/evidence/load/dossier. Output loading phải thuộc đúng production/plan/container, không gắn hồi tố một production bất kỳ bằng UUID.
- Các writer processing/transfer/correction khác tiếp tục tôn trọng reserved stock. Output chưa load của S1 bị transfer ngoài đường hỗ trợ phải bị chặn; không phát sinh successor mapping ngầm.
- Execution đã sản xuất không thể revoke như approval chưa làm gì. Nếu evidence/HOLD đổi sau partial loading, giữ chặn hiện tại và history cho tới S2, không tự cấp reapproval.

## 7. Tiêu chí nghiệm thu S1

### 7.1 Kịch bản web bắt buộc

Fixture synthetic có source/lab/profile/current MRL/quality/compatibility/processing policy hợp lệ. Một plan từ ít nhất hai input lots, ordinary positions; declared actuals có căn cứ. Ví dụ exact balance target 10 kg từ 6+4 kg chỉ là fixture, không phải yield/tolerance mặc định.

1. Export tạo order/plan; CM approve. Input physical chưa giảm, outstanding input reserve đúng lượng.
2. Export request production; Operator đúng facility tạo draft/commit. Physical input giảm đúng một lần, INPUT consumed đúng lượng; actual output có lineage và reserved quota cho container. Loaded vẫn 0, không ghi LOADING chỉ để báo production started.
3. Thiếu quality/review thì form và API loading đều bị chặn. CM xác nhận quality theo workflow hiện có, review actual output PASS trước loading; không rerun input.
4. Export lưu metadata rồi load `4.000001` và `5.999999`. Tổng actual/source kg đúng `10.000000`; OUTPUT consumed đúng 10, input không bị consume lại.
5. CLOSE đúng target, immutable dossier chứa production/output/review/assessment references; stock không giảm lần nữa. Trace container về nguồn/plot và source forward về actual container đúng scope.
6. Một fixture DIRECT_STOCK riêng vẫn đi hết load → close như baseline. Fixture test âm độc lập, không dùng HOLD/stale để chặn chéo đường chuẩn.

### 7.2 Nhóm kiểm thử kỹ thuật tối thiểu

| Nhóm | Bằng chứng cần có |
| --- | --- |
| Atomicity | Fault sau INPUT consume, sau tạo output/trước reserve, và sau OUTPUT consume/trước manifest: tất cả writes trong thao tác cùng rollback. Không có output mất claim hoặc stock giả. |
| Retry/concurrency | Retry production tạo đúng một execution/output; hai operators/requests không tiêu cùng approval hai lần; replay output review/loading không reserve/debit trùng; stale version trả conflict. |
| Authority | Export gọi commit production 403; Operator review/output load kinh doanh 403; sai company/facility denied; role/assignment bị thu hồi lúc chờ lock denied; task read không lộ dữ liệu ngoài scope. |
| Scope/quantity | Lot/vị trí ngoài approval, INPUT reservation đem load, output ngoài execution, >quota/target, duplicate positions, số không dương/numeric token/>6 decimals đều bị từ chối không partial writes. |
| Evidence/restriction | Stale approval trước production; missing/failed actual output evidence; HOLD xuất hiện trước commit/load/close; boundary effective-time MRL/policy dùng clock điều khiển được, không chờ giờ tùy ý. |
| Review pre-load | Output PASS/REJECT/re-review, release/reserve đúng outstanding, không sửa production hoặc chạy lại input. Sau partial load vẫn chặn theo giới hạn S1. |
| Regression | DIRECT_STOCK, multi-source micro-kg, immutable dossier/redaction, correction/downstream guards, old stock writers tôn trọng reservation, private helpers không có runtime access trực tiếp. |
| UI | Hai vai khách/nhà máy và CM thực hiện task; pending/retry nguyên request; status/remaining/current blockers đồng bộ; không còn duplicate-key ở fixture nhiều outputs. |

Khi triển khai: chạy build/test backend liên quan bằng Docker/Testcontainers; frontend tests/build theo thay đổi; actual browser/API path trên môi trường cô lập. Trước merge chạy required checks của repo/CI theo scope. Không lấy 8 integration tests đã PASS ở lượt trước làm bằng chứng cho chức năng mới chưa viết. Bổ sung tenant B test account bằng harness cô lập, không gửi email hoặc tạo account trên DB chung ngoài scope.

## 8. Cản trở và quyết định cắt phạm vi

| Gate | Cản trở | Cách xử lý / điều kiện dừng |
| --- | --- | --- |
| G1 trước C02 | Chưa thống nhất actor/state/purpose/output review với mô hình hiện tại | Chốt C01 trong một bước review contract; ghi rõ quyết định. Nếu không có contract khả thi thì đưa cả nhánh blend sang S2, không chỉ gỡ blocker để kịp demo. |
| G2 trước C04 | Ledger command mở connection riêng hoặc reservation guard không phân biệt authorized input withdrawal | Cần đường nội bộ dùng chung transaction và protected consume. Nếu phải rewrite diện rộng mà không bảo đảm regression trong sprint, dừng mở production. |
| G3 trước C06 | Actual output assessment/review không thể tách khỏi fingerprint input proposal | Implement output review reference và actual scope đúng nghĩa. Không sửa saved snapshot, tạo PASS provider, giả CM hoặc coi planning bound là approval output. |
| G4 trước release | Thiếu evidence/policy hợp lệ hoặc actual target không phù hợp phạm vi một mẻ | Thiếu dữ liệu khoa học không tự điền default. Dùng fixture synthetic có khai báo cho test; hàng thật tiếp tục blocked. Ngoại lệ quantity/yield sang S2. |
| G5 trước shared deployment | Migration/grants chưa được duyệt hoặc chưa có build tương thích DB | Hoàn tất code, tests cô lập, SQL diff/preflight/rollback-backup plan để review. Chưa được triển khai không báo đã live. Approval cũ cho 4 container grants không tự áp dụng cho schema/grants mới. |

G5 đã được xử lý cho migration `20261117000000_ContainerProduction` và một quyền EXECUTE mới trên DB cấu hình hiện tại ngày 18/11: người dùng duyệt riêng, backup và triển khai nguyên tử thành công, runtime local được kiểm tra. Không suy ra đã triển khai mọi API instance từ xa; xem biên bản triển khai ở trên.

Nếu S1 không vượt được G1–G3 trong thời gian sprint thực tế: đóng gói DIRECT_STOCK + C08 + kiểm thử/tài liệu làm mốc bàn giao thu hẹp; báo rõ **chưa hoàn tất order → blend → container**, chuyển toàn bộ lát cắt blend còn thiếu sang sprint sau. Không hạ điều kiện transaction/quyền/accounting để giữ nhãn “done”.

## 9. Sprint sau — S2: các luồng chính phức tạp

Đây vẫn là backlog nghiệp vụ, không mặc định loại khỏi yêu cầu dự án. Mỗi dòng chỉ bắt đầu khi prerequisites tương ứng đủ và nghiệm thu độc lập.

| ID | Phần việc | Điều kiện mở / tiêu chí hoàn tất |
| --- | --- | --- |
| N01 | Full revalidation sau partial production/loading | Mở rộng RV03: ALL_REMAINING/OUTPUT_ONLY, current assessment + review mới, scope chính xác và release/re-reserve chưa consume. Giữ actual history; không chạy lại input; một plan có quyền mới tại mỗi thời điểm. |
| N02 | Chuyển plan P1→P2 và CLOSE nhiều plan | Version/review/output identity giữ nguyên, không gán output P1 thành P2. Whole-container CLOSE đánh giá mọi committed loads/current requirements, giữ old authorization và new CLOSE basis. |
| N03 | Pool/custody/aliquot reservation handoff | Contract source-wise complement + pool revisions + reservation purpose/transfer, lock/replay và HOLD ancestry. Test top-up/withdrawal/race; không tự rebind theo current stock. |
| N04 | Transfer/bagging sau sản xuất, successor positions | Build trên N03 khi có custody; chuyển quota đúng output identity/product, không nhân stock hoặc mất source shares. Chỉ mở từng operation đã chứng minh. |
| N05 | Nhiều mẻ, partial production, dư/thiếu output và chia container | Quy tắc actual yield/variance/output allocation được chốt, scope review/reserve theo actuals; conservation qua mọi mẻ. Không dùng predicted yield làm actual, không tự tăng target. |
| N06 | Mở rộng regression/acceptance | Actual missing plot provenance bằng fixture cô lập hợp lệ, web tenant B, policy time boundaries, hành vi N01–N05; source-attribution/performance datasets và oracle đủ scope. |

N01/N02 có thiết kế nghiệp vụ trong SRS/RV03/RV04; cản trở là độ rộng thay đổi approval, reservation, assessment và manifest hiện tại. N03–N05 cần thêm contract thực thi chi tiết cho generation/quantity exceptions; không ghi chung là tất cả đều chưa có yêu cầu.

## 10. Sau core — S3 / backlog phụ

| Phần việc | Trạng thái ưu tiên và điều kiện |
| --- | --- |
| Container HOLD notification inbox/read/handling | Sau S1; nối đúng recipients và retained read state theo R09/R10, không tự release. Alert detail hiện có không đủ để báo inbox hoàn chỉnh. |
| Versioned shipment documents và dossier evidence supplements | Sau S1; FR-EXP-005 vẫn còn. Giữ immutable closing snapshot, supplement có reason/user/time, không reopen. Chốt mẫu/định dạng trước khi làm PDF/Excel cụ thể. |
| Performance full-season, production measurements, staging/witness sign-off | Chốt dataset/counts/oracle/môi trường; đo cold/warm/API/page riêng. Cần nghiệm thu riêng trước khi tuyên bố đạt NFR toàn mùa vụ. |
| Legacy preparation mapping | Chỉ khi có nhu cầu chuyển đổi đã xác nhận; explicit mapping/migration riêng, không match code hoặc tự chuyển draft thành actual. |
| Mobile polish, định dạng xuất thêm và tối ưu đọc | Theo nhu cầu sau core; không cache bỏ freshness hoặc tối ưu trước khi có đo lường. |

Không tự đưa HOLD release, unload/reopen/reseal, đóng thiếu, auto-HOLD, customs/dispatch hoặc ownership transfer vào backlog bắt buộc của S1/S2. Những ngoại lệ này cần yêu cầu/policy riêng; HOLD release không thuộc MVP đã đọc. Đưa sang sprint sau không đồng nghĩa cho phép triển khai khi chưa có nghiệp vụ.

## 11. Quy tắc bàn giao và báo cáo deadline

- Báo theo ba mốc riêng: **implemented → verified cô lập/web → deployed môi trường đích**. Checklist của plan không phải kết quả test.
- Khi S1 đạt có thể báo: “Hoàn tất đường chính một approval–một mẻ blend–ordinary output review–partial loading–CLOSE–trace; giữ tương thích DIRECT_STOCK.” Phải kèm giới hạn S2/S3; không báo toàn bộ SRS container hoàn tất.
- Nếu chỉ bàn giao mốc thu hẹp vì gate chưa đạt, báo đúng DIRECT_STOCK và các fix/test thực tế, cùng ID công việc chuyển sprint. Không tạo báo cáo PASS cho luồng chưa có.
- Mọi migration/grants/shared test data được trình duyệt riêng sau khi có SQL diff, code và kiểm thử review được. Không dùng owner để chạy app; bảo toàn `.env`, dữ liệu/historical records và quyền private helper.
- Người dùng đã yêu cầu triển khai S1; chưa cung cấp deadline/capacity cụ thể để gắn ngày cho sprint sau. Không tự mở rộng N01–N06/S3. Shared deployment cần approval riêng theo checklist, dù implementation đã được cho phép.
