# Plan hoàn thiện luồng chính container — 17/11/2026

## 1. Mục tiêu và trạng thái

**Cập nhật phạm vi 18/11/2026 theo yêu cầu người dùng: chỉ thực hiện yêu cầu có căn cứ trong proposal/SRS/business rules; bỏ các phần mở rộng hoặc đề xuất chưa có yêu cầu. Plan gồm hai đợt: S1 đã triển khai, S2 hoàn thiện phần yêu cầu còn lại. Không có đợt S3 trong phạm vi hiện hành.**

S1 đã có code, kiểm thử cô lập/web và đã áp dụng migration cùng quyền runtime mới lên Supabase cấu hình hiện tại theo phê duyệt riêng. Commit bàn giao: `8fd96ac` trên `feat/container`. Xem [verification record](../reports/container-production-implementation-2026-11-18.md), [biên bản triển khai DB](../reports/container-production-deployment-2026-11-18.md) và [API/state contract](../api/container-production-api.md). Chưa chạy lại toàn bộ checklist web trên DB dùng chung sau migration. Phê duyệt DB vừa thực hiện chỉ áp dụng migration/quyền trong biên bản, không tự cho phép các thay đổi DB tiếp theo.

Mục tiêu sprint gần nhất: chạy được trên web **order → proposal BLEND_INPUT_STOCK → CM approve/reserve inputs → nhà máy blend thật → giữ và review actual output → actual loading từng phần → CLOSE → dossier/trace**. Duy trì đường DIRECT_STOCK đã chạy được.

Chưa có ngày deadline, độ dài sprint hoặc capacity cụ thể; các sprint dưới đây là đề xuất phân phạm vi và thứ tự, không phải cam kết thời gian. Không mở rộng sprint hiện tại chỉ để đánh dấu đủ mọi chức năng SRS.

Người dùng đã yêu cầu bắt đầu S2 theo bảy nhóm việc xác nhận lại, xem [plan triển khai S2](container-s2-implementation-plan-2026-11-18.md). Các checkbox chưa đánh dấu là công việc chưa hoàn tất. Việc hoàn tất kiểm thử thuộc hai đợt trên, không phải một đợt phát triển thứ ba. Gom xong implementation rồi mới chạy đợt kiểm thử container liên quan; không chạy toàn bộ backend giữa các đoạn sửa.

## 2. Căn cứ và baseline

- [Kết quả CT-01–CT-03, CT-11–CT-30](../reports/container-web-test-remaining-2026-11-17.md): DIRECT_STOCK, accounting, retry, dossier và trace đã kiểm; 16 nhóm PASS theo phạm vi, 4 nhóm PARTIAL. Không suy ra mọi biến thể đã nghiệm thu.
- [Kết quả CT-04–CT-10](../reports/container-web-test-execution-2026-11-17.md): đường loading/close thông thường.
- [Luồng nghiệp vụ](../reports/container-end-to-end-business-flow-2026-11-16.md), đặc biệt bước 4 về actual production và output reservation.
- [Plan ngày 13/11](container-execution-plan-2026-11-13.md): baseline đã triển khai và giới hạn; tài liệu này bổ sung thứ tự công việc tiếp theo, không sửa kết quả lịch sử.
- [SRS FR-EXP-003–005](</home/phucpinku/Desktop/Week1_report/review/Report3_Software Requirement Specification.docx.md:419>), [Business Rules R03/RV02/RV03](</home/phucpinku/Desktop/Week1_report/review/erd_v2_1/BUSINESS_RULES.md:64>): input/output reservations, execution, OUTPUT_ONLY/ALL_REMAINING, review mới, CLOSE và dossier. Đây là yêu cầu đã có, không ghi chung là “chờ nghĩ nghiệp vụ”.
- [Proposal v1.1](</home/phucpinku/Desktop/Week1_report/review/FA26SE272_GrainLedger_Proposal_v1.1.docx.pdf>): trang 4 và 11 xác định planning/approval/loading là core. Tài liệu local là căn cứ đọc, không copy vào Git hoặc sửa trạng thái nghiệm thu của team.
- [GrainLedger V2](</home/phucpinku/Desktop/Week1_report/GrainLedger_V2.pdf>): trang 6 mô tả gom lúa nhiều nông hộ cùng khách hàng vào silo. Nghiệp vụ này là căn cứ kho trộn; pool generation/aliquot/successor là lựa chọn triển khai, không phải một bộ chức năng độc lập được proposal yêu cầu nguyên văn.
- [SRS FR-LED-001/002](</home/phucpinku/Desktop/Week1_report/review/Report3_Software Requirement Specification.docx.md:359>), [FR-TRC-002](</home/phucpinku/Desktop/Week1_report/review/Report3_Software Requirement Specification.docx.md:453>), [NFR-REL-01–06 và NFR-PER-01](</home/phucpinku/Desktop/Week1_report/review/Report3_Software Requirement Specification.docx.md:513>): processing/transfer/bagging, exact mass, HOLD và retained notices, kiểm chứng và benchmark tracing theo dataset xác định.
- [Architecture](../architecture.md), [Ledger plan](ledger-implementation-plan.md), [setup/deployment](../setup-and-deployment.md): transaction, exact mass, quyền, code issuance và triển khai.

Baseline lúc lập plan gốc: `feat/container`, `1fc8efd`; S1 đã được commit tại `8fd96ac`. Khi triển khai S2 phải kiểm lại diff/schema/contracts của nhánh đích; không dùng mô tả trước S1 để ghi đè implementation đã hoàn tất. Tài liệu plan/report kỹ thuật tự viết không tự tạo thêm yêu cầu nghiệp vụ; mọi đầu việc S2 phải có căn cứ gốc ở mục 9.

## 3. Đợt 1 — S1 đã hoàn thành trong phạm vi sau

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

## 4. Baseline trước S1 — các nút thắt đã xử lý trong đợt 1

Bảng này ghi nhận code khi lập plan ngày 17/11, không phải danh sách lỗi còn tồn tại sau commit S1. Cách giải quyết thực tế và kiểm thử nằm trong verification record; không yêu cầu làm lại hoặc refactor theo dự kiến cũ.

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

## 8. Các gate của S1 lúc lập plan — lưu để đối chiếu

S1 đã vượt các gate để bàn giao phạm vi một mẻ, output đúng target. Bảng sau giữ lại lý do và ranh giới ban đầu; phạm vi S2 hiện hành chỉ lấy theo mục 9, không tự chuyển mọi ngoại lệ cũ thành đầu việc bắt buộc.

| Gate | Cản trở | Cách xử lý / điều kiện dừng |
| --- | --- | --- |
| G1 trước C02 | Chưa thống nhất actor/state/purpose/output review với mô hình hiện tại | Chốt C01 trong một bước review contract; ghi rõ quyết định. Nếu không có contract khả thi thì đưa cả nhánh blend sang S2, không chỉ gỡ blocker để kịp demo. |
| G2 trước C04 | Ledger command mở connection riêng hoặc reservation guard không phân biệt authorized input withdrawal | Cần đường nội bộ dùng chung transaction và protected consume. Nếu phải rewrite diện rộng mà không bảo đảm regression trong sprint, dừng mở production. |
| G3 trước C06 | Actual output assessment/review không thể tách khỏi fingerprint input proposal | Implement output review reference và actual scope đúng nghĩa. Không sửa saved snapshot, tạo PASS provider, giả CM hoặc coi planning bound là approval output. |
| G4 trước release | Thiếu evidence/policy hợp lệ hoặc actual target không phù hợp phạm vi một mẻ | Thiếu dữ liệu khoa học không tự điền default. Dùng fixture synthetic có khai báo cho test; hàng thật tiếp tục blocked. Ngoại lệ quantity/yield không có quy tắc nguồn rõ được loại khỏi phạm vi hiện hành. |
| G5 trước shared deployment | Migration/grants chưa được duyệt hoặc chưa có build tương thích DB | Hoàn tất code, tests cô lập, SQL diff/preflight/rollback-backup plan để review. Chưa được triển khai không báo đã live. Approval cũ cho 4 container grants không tự áp dụng cho schema/grants mới. |

G5 đã được xử lý cho migration `20261117000000_ContainerProduction` và một quyền EXECUTE mới trên DB cấu hình hiện tại ngày 18/11: người dùng duyệt riêng, backup và triển khai nguyên tử thành công, runtime local được kiểm tra. Không suy ra đã triển khai mọi API instance từ xa; xem biên bản triển khai ở trên.

S1 đã thực hiện được đường blend sau duyệt; không còn áp dụng phương án bàn giao thu hẹp chỉ có DIRECT_STOCK từng dự kiến ở plan gốc. Các gate dữ liệu/quyền/accounting vẫn phải được bảo toàn khi mở rộng S2.

## 9. Đợt 2 — S2: hoàn thiện các yêu cầu còn lại

Các mục dưới đây là phạm vi thực hiện tiếp theo, không phải kết quả đã hoàn tất. Mỗi mục ghi nguồn yêu cầu và giới hạn để không mở rộng theo suy đoán. Rà implementation hiện có trước khi sửa; phần Ledger/HOLD đã có và đạt yêu cầu chỉ cần tích hợp, không làm lại toàn bộ module.

### 9.1 Đầu việc và căn cứ

| ID | Đầu việc cần làm | Nguồn yêu cầu | Tiêu chí đạt / ranh giới |
| --- | --- | --- | --- |
| N01 | Duyệt lại sau khi đã có production/loading | [FR-EXP-003](</home/phucpinku/Desktop/Week1_report/review/Report3_Software Requirement Specification.docx.md:419>), [R03/RV03](</home/phucpinku/Desktop/Week1_report/review/erd_v2_1/BUSINESS_RULES.md:110>) | Assessment/review mới theo ALL_REMAINING hoặc OUTPUT_ONLY, scope output IDs/masses và phần chưa thực hiện chính xác; STALE giữ lịch sử, release/re-reserve phần chưa consume theo quyền mới. Không chạy lại input đã dùng; HOLD còn hiệu lực vẫn chặn dù review mới PASS. |
| N02 | Đổi plan P1→P2 và CLOSE qua nhiều plan | [FR-EXP-003/004](</home/phucpinku/Desktop/Week1_report/review/Report3_Software Requirement Specification.docx.md:425>), [RV03/RV04](</home/phucpinku/Desktop/Week1_report/review/erd_v2_1/BUSINESS_RULES.md:118>) | Chỉ một plan có quyền execution/loading mới tại mỗi thời điểm. P2 chỉ lập cho phần chưa sản xuất; output/loading P1 giữ đúng identity và review. CLOSE đánh giá mọi committed loads theo yêu cầu hiện hành, exact target và order limit. |
| N03 | Dùng stock kho trộn trong luồng container theo phạm vi đã được duyệt | [V2 trang 6](</home/phucpinku/Desktop/Week1_report/GrainLedger_V2.pdf>), [FR-LED-001](</home/phucpinku/Desktop/Week1_report/review/Report3_Software Requirement Specification.docx.md:365>), [R03](</home/phucpinku/Desktop/Week1_report/review/erd_v2_1/BUSINESS_RULES.md:64>) | Nối mô hình mixed storage đã có với stock selection, reservation và actual loading khi chứng minh được đúng source mass/identity/quota/company. Reuse custody hiện có; pool revision/aliquot là chi tiết triển khai. Không tạo thêm một hệ thống pool hoặc tự cho reservation đi theo mọi top-up. Quy tắc chưa có căn cứ tiếp tục bị chặn, ghi rõ trường hợp chưa hỗ trợ. |
| N04 | Nối transfer/bagging hợp lệ với reservation của plan | [FR-LED-001](</home/phucpinku/Desktop/Week1_report/review/Report3_Software Requirement Specification.docx.md:365>), [RV02](</home/phucpinku/Desktop/Week1_report/review/erd_v2_1/BUSINESS_RULES.md:70>) | Dùng Ledger transfer/bagging hiện có; ordinary transfer giữ lot identity và stock postings bằng nhau, conversion giữ lineage. Khi công đoạn tiêu thụ OUTPUT reservation phải release rồi xác lập INPUT reservation được review đúng scope. Output mới muốn load cần đúng output mapping/reservation/review; không coi successor tự có quyền của output cũ. Phạm vi mixed storage phụ thuộc N03. |
| N05 | Thực hiện phần còn lại theo scope được duyệt | [FR-EXP-003](</home/phucpinku/Desktop/Week1_report/review/Report3_Software Requirement Specification.docx.md:425>), [RV03](</home/phucpinku/Desktop/Week1_report/review/erd_v2_1/BUSINESS_RULES.md:114>), [FR-LED-001/002](</home/phucpinku/Desktop/Week1_report/review/Report3_Software Requirement Specification.docx.md:365>) | Hỗ trợ execution từng phần trong quota inputs/outputs được review, lưu actuals và phân biệt IN_EXECUTION/EXECUTED; phần đã thực hiện không chạy lại. Kiểm declared loss/variance theo policy hiện có. Không tự thêm batch-size policy, sản lượng dư/thiếu target, tăng target hoặc chia dư sang container khác. Partial execution có căn cứ; mọi biến thể nhiều mẻ không mặc định được yêu cầu. |
| N06 | Hoàn tất kiểm thử container và bằng chứng web | [NFR-REL-01–06](</home/phucpinku/Desktop/Week1_report/review/Report3_Software Requirement Specification.docx.md:513>), [FR-TRC-001](</home/phucpinku/Desktop/Week1_report/review/Report3_Software Requirement Specification.docx.md:449>) | Chốt CT-16 picker HOLD, CT-18 time boundaries, CT-25 provenance thiếu và CT-26 tenant B bằng fixture phù hợp. CT-27 thêm expected cho production thành công; CT-28 kiểm riêng nhánh hỗ trợ và nhánh phải chặn. Thêm regression cho N01–N05/N07/N08, exact mass, wrong-plan loading, replay/race/rollback, quyền và immutable history. Không nâng PASS chỉ vì đã sửa code. |
| N07 | Thông báo HOLD và xử lý theo đúng vai trò | [FR-TRC-002](</home/phucpinku/Desktop/Week1_report/review/Report3_Software Requirement Specification.docx.md:453>), [R09/R10](</home/phucpinku/Desktop/Week1_report/review/erd_v2_1/BUSINESS_RULES.md:164>) | In-app notices đúng recipients, unique hold/recipient, ghi cùng HOLD, giữ khi offline và read state. Mill Manager xử lý ACKNOWLEDGE/ISOLATE/RECORD_RESPONSE trong scope; tái dùng phần đã có. Read/handling không release HOLD. Thiếu recipients hợp lệ là lỗi cấu hình; không thêm email/SMS hoặc broadcast. |
| N08 | Chứng từ có phiên bản và dossier supplements | [FR-EXP-005; UC-33/35](</home/phucpinku/Desktop/Week1_report/review/Report3_Software Requirement Specification.docx.md:435>) | Export xuất shipment document có container/type/version/file reference; supplement append evidence cùng reason/user/time, giữ snapshot CLOSE bất biến và phân quyền. Không tự bắt buộc PDF/Excel cụ thể, mẫu thương mại/hải quan hoặc chứng nhận pháp lý. Nếu nội dung/mẫu chứng từ chưa được cung cấp, ghi chính xác phần thiếu; không tự chế mẫu rồi báo hoàn tất. |
| N09 | Kiểm chứng tracing trên dataset toàn mùa vụ | [NFR-PER-01, NFR-CAP-01](</home/phucpinku/Desktop/Week1_report/review/Report3_Software Requirement Specification.docx.md:527>), [proposal v1.1 trang 9](</home/phucpinku/Desktop/Week1_report/review/FA26SE272_GrainLedger_Proposal_v1.1.docx.pdf>) | Dataset synthetic có khai báo workload, nguồn/operations/container counts, source-mass oracle và tài nguyên máy. Đo forward/backward theo quyền, đối chiếu đầy đủ targets/shares và mục tiêu tracing 30 giây của SRS. Báo cold/warm/API/page đúng phạm vi; không benchmark mọi module BE hoặc tối ưu suy đoán. |

### 9.2 Thứ tự thực hiện và phụ thuộc

1. Rà lại implementation và chốt contract chỉ trong yêu cầu nguồn; bổ sung từng phần N06 còn thiếu bằng fixture độc lập.
2. N01 review mới → N05 execution phần còn lại → N02 chuyển plan/CLOSE toàn container. Không gộp lại quyền của hai plan để tránh phải xử lý scope.
3. N03 tích hợp stock kho trộn → N04 nhánh transfer/bagging liên quan. Ordinary transfer/bagging không có custody chỉ phụ thuộc contract review/reservation cần thiết.
4. N07 thông báo/xử lý HOLD và N08 chứng từ/supplements có thể thực hiện độc lập với nhánh pool; đều nằm trong S2, không bị đẩy thành yêu cầu tùy chọn của S3.
5. Hoàn tất N06 cho toàn bộ thay đổi, rồi N09 và hồ sơ bàn giao. Các bước kiểm thử/benchmark là phần nghiệm thu S2, không tạo thêm đợt phát triển.

N03–N05 cần chi tiết triển khai phù hợp mô hình hiện có. Đó không phải quyền tự đặt quy tắc nghiệp vụ: thiếu quy tắc compatibility, quantity exception hoặc authority thì giữ chặn riêng tình huống đó, không tự bổ sung default và không báo toàn bộ phạm vi đã đạt. Không cần xin duyệt lại những lựa chọn kỹ thuật thường lệ đã nằm trong yêu cầu; thay đổi chính sách nghiệp vụ/DB dùng chung vẫn theo ranh giới hiện có.

### 9.3 Điều kiện chốt đợt 2

- [x] N01/N02/N05: review và execution phần còn lại, chuyển plan, CLOSE nhiều plan chạy đúng bằng API/web; không tiêu lại inputs, vượt quota hoặc mất lịch sử.
- [x] N03/N04: các nhánh kho trộn/transfer/bagging có căn cứ đã tích hợp và chứng minh conservation/reservation; phần quy tắc còn thiếu được chỉ rõ, không mặc nhiên đánh dấu xong.
- [x] N07/N08: retained HOLD notices/handling và versioned documents/supplements đạt contract nguồn và phân quyền.
- [x] N06: ghi kết quả test thực tế; giữ phân biệt PASS chức năng thành công, PASS chặn đúng và PARTIAL/NOT RUN.
- [x] N09: có dataset/oracle và kết quả đo; không dùng kết quả fixture nhỏ thay nghiệm thu full-season.
- [x] Tài liệu, migration/grant package và bản bàn giao cùng phiên bản; chỉ triển khai DB theo phê duyệt cho đúng thay đổi.

Kết quả triển khai S2 ngày 18/11/2026 được ghi tại [báo cáo](../reports/container-s2-implementation-2026-11-18.md), [plan chi tiết](container-s2-implementation-plan-2026-11-18.md) và [gói triển khai](../guides/container-s2-deployment.md). Các dấu hoàn tất trên áp dụng cho luồng hỗ trợ và kiểm thử đã ghi rõ; không có nghĩa mọi biến thể pool/top-up có policy hoặc mọi CT cũ đều được đổi thành PASS. P1→P2 và pool/transfer/bagging đã có cả API coverage và walkthrough web đầy đủ đến CLOSE; xem [lượt nghiệm thu bổ sung](../reports/container-s2-web-walkthroughs-2026-11-18.md). Benchmark đạt trên dataset xác định, chưa thay nghiệm thu production concurrent load. Migration/quyền S2 đã áp dụng trên Supabase cấu hình hiện tại theo phê duyệt riêng; xem [biên bản triển khai](../reports/container-s2-deployment-2026-11-18.md). Dữ liệu test cô lập không được copy/sync lên Supabase.

## 10. Loại khỏi phạm vi triển khai hiện hành

Danh sách này chỉ ghi quyết định loại phạm vi để tránh tái đưa vào backlog; không phải các đầu việc sẽ tự làm sau S2. Các plan/report cũ mô tả giới hạn hoặc câu hỏi mở không thay đổi quyết định này.

- Legacy preparation → order/container mapping hoặc migration chuyển dữ liệu cũ: chưa có yêu cầu chuyển đổi đã xác nhận. Giữ lịch sử cũ theo hành vi hiện có.
- Tự xử lý output dư/thiếu target, tăng/giảm target sau loading, chia output dư sang container khác, đặt số mẻ/batch size tùy ý: chưa có policy cụ thể. Giữ kiểm actual mass/loss/variance đã được yêu cầu; không suy ngoại lệ shipment từ processing tolerance.
- Multi-container optimization, buyer/auditor portal, EPCIS export, automated regulatory-data ingestion: các phần mở rộng được proposal/SRS tách khỏi core; không thực hiện trong plan này.
- Mobile riêng cho container; thêm định dạng/mẫu PDF/Excel do developer tự chọn; dashboard/polish/read optimization ngoài yêu cầu hoặc thiếu đo lường. Mobile field capture và yêu cầu xuất shipment documents vốn có không bị loại khỏi phạm vi dự án; ở đây chỉ loại phần bổ sung cho container không có yêu cầu.
- HOLD release/cancellation, auto-HOLD, unload/reopen/reseal, đóng thiếu, customs/dispatch, ownership transfer: không có authority/policy cho phép trong phạm vi đã đọc hoặc được tài liệu loại rõ; không triển khai như ngoại lệ tự suy diễn.
- Tạo module custody mới, tự mở mọi successor/top-up/rebinding hoặc refactor diện rộng: không phải yêu cầu nghiệp vụ. Chỉ sửa tích hợp cần thiết cho N03/N04 và giữ protected contracts.

Không xóa khả năng đã được triển khai chỉ vì phần mở rộng bị loại khỏi kế hoạch tiếp theo. Không sửa proposal/SRS gốc để hợp thức hóa phạm vi.

## 11. Quy tắc thực hiện và báo cáo deadline

- Chỉ có hai đợt: **S1 đã bàn giao; S2 là các yêu cầu còn lại ở mục 9**. Mục 10 không nằm trong backlog thực hiện.
- Báo theo ba mốc riêng: **implemented → verified cô lập/web → deployed môi trường đích**. Checklist của plan không phải kết quả test. S1 đạt trong phạm vi một approval/mẻ/output; chưa phải toàn bộ SRS container hoàn tất.
- Nêu rõ requirement ID, hành vi, API/web và bằng chứng cho từng mục S2. Nếu thiếu dữ liệu hoặc quy tắc nguồn, ghi đúng phần bị chặn và phần vẫn làm được; không thêm một nghiệp vụ mới để giải quyết bằng suy đoán.
- Chỉ chạy backend tests liên quan container và các boundary trực tiếp bị thay đổi; frontend checks/build khi sửa web. Actual API/browser path dùng DB test cô lập, không dùng full backend suite để thay chứng cứ luồng web. Documentation-only chỉ kiểm file/link/whitespace.
- Mọi migration/grants/shared test data mới tuân theo scope phê duyệt hiện có và AGENTS.md; không dùng migration owner để chạy app, không reset/seed tổng quát hoặc sửa dữ liệu luồng khác.
- Người dùng đã cho phép triển khai S2 và xác nhận lại bảy nhóm việc; ACKNOWLEDGE/ISOLATE/RECORD_RESPONSE vẫn giữ, phần gỡ HOLD để tiếp tục vận hành ngoài phạm vi. Chưa có deadline/capacity để cam kết ngày hoàn thành. Không tự commit/push nếu chưa được yêu cầu.
