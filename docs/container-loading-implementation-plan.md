# Kế hoạch loading và đóng container

**Plan thực thi hiện hành:** [từ loading draft đến loading thật](container-loading-next-plan.md), cập nhật 25/10/2026. Tách LOADING khỏi CLOSE; mục bên dưới giữ căn cứ và nhật ký trước đó.

Ngày đánh giá: **20/10/2026**. Baseline source: nhánh **`dev`**, commit **`b387942`** — `Enhance laboratory workflow and MRL reference management (#10)`.

Trạng thái cập nhật: **đã triển khai preparation/draft và readiness gates trên feat/container từ feat/trace `f399aa7`**; xem mục 14 và [API contract](container-api.md). Loading commit/CLOSE thực chưa bật vì order/blend/reservation do team khác triển khai, theo xác nhận của người dùng. Sau triển khai/test cô lập, người dùng đã phê duyệt migration database đang cấu hình; kết quả tại mục 15. Không seed hoặc commit/push. Source có migration không chứng minh deployment ở database khác.

Cập nhật 25/10/2026: đã đọc dev `eecde7e` và tích hợp vào nhánh container; **mục 16** là đánh giá dependency/plan hiện hành. Mục 3 và roadmap gốc giữ bối cảnh lúc lập plan, không phải tuyên bố dev mới chưa có tracing/residue.

## 1. Kết luận và phạm vi

Có thể bắt đầu xây phần quản lý order/target/container và draft loading. Chưa thể bật **commit loading hoặc CLOSE có giá trị nghiệp vụ** chỉ với stock và lab hiện tại: cần quyền dùng hàng từ phương án đã duyệt, reservation tồn kho, assessment đúng phạm vi và cơ chế freshness/invalidation.

Mục tiêu cuối: Export Staff chọn đúng hàng được duyệt, ghi thực xếp, hệ thống giảm tồn và tiêu thụ giữ chỗ nguyên tử; khi đủ target, đóng container cùng assessment và hồ sơ bất biến. Loading, ghi số container/seal và CLOSE là ba thao tác riêng. CLOSE không đồng nghĩa hàng đã dispatch hoặc đã ra khỏi quyền kiểm soát.

Phạm vi container gồm UC-31/32 và phần hồ sơ tối thiểu của UC-34/35. UC-29 order/target là dependency trực tiếp; UC-23/15/30/37 cung cấp plan/review/execution/assessment. UC-33 xuất chứng từ là bước sau snapshot, không thay việc tạo dossier khi CLOSE. Không mở rộng tracing trong đợt triển khai này, nhưng phải lưu đủ provenance để tích hợp sau.

Không yêu cầu làm solver tối ưu phối trộn trước mọi màn container. Có thể làm thủ công phần đề xuất/chọn allocation nếu hợp đồng nghiệp vụ được chốt; vẫn phải có review, reservation, actual execution và assessment thật. Không tạo plan APPROVED giả, assessment PASS mặc định hoặc gắn hồi tố production cũ vào plan để vượt gate.

## 2. Căn cứ và thứ tự ưu tiên

Nguồn yêu cầu bên ngoài repo, đã đọc trong lần lập plan:

- `/home/phucpinku/Desktop/Week1_report/review/Report3_Software Requirement Specification.docx.md`: FR-EXP-001–005, UC-23/29–35/37; đặc biệt FR-EXP-004/005.
- `/home/phucpinku/Desktop/Week1_report/review/erd_v2_1/BUSINESS_RULES.md`: R01 về transaction/accounting; R03 và R2-01/RV03 về reservation/review; R04/R05 về evidence; R08/RV04 về whole-container CLOSE; R09 về HOLD/audit.
- `/home/phucpinku/Desktop/Week1_report/review/erd_v2_1/05-orders-and-planning.mmd` và `06-loading-hold-and-audit.mmd`: logical entities và các khóa liên kết.
- `/home/phucpinku/Desktop/Week1_report/review/SDD/GrainLedger_SDD.html`, mục 3.7, và `diagrams/11_sequence_load.mmd`, `11_sequence_close.mmd`, `11_sequence_documents.mmd`: loading transaction, CLOSE xuyên phiên bản plan, dossier/supplement.
- `/home/phucpinku/Desktop/Week1_report/GLM/03_Nguoi_dung_va_personas.md`, `05_Luong_nghiep_vu_khong_AI.md` mục Luồng 7, `12_Mass_balance_custody_ledger.md`, `13_Luong_nghiep_vu_nha_may.md` mục L8: trách nhiệm hai bên, actual mass, ví dụ loading và custody.

Hợp đồng code cần giữ: [architecture](architecture.md), [Ledger](ledger-implementation-plan.md), [setup/deployment](setup-and-deployment.md), [lot-code guide](unified-lot-code-guide.md), [lab policy boundaries](lab-flow-plan.md), [sample/lab API](sample-api.md), [MRL](mrl-api.md).

SRS/ERD chi tiết dùng làm target nghiệp vụ; AGENTS/architecture và code hiện tại quyết định cách triển khai. SDD là thiết kế dự kiến, không chứng minh đã có service/table. Tài liệu GLM cũ mô tả `Loaded → Dispatched`, nhưng SRS hiện hành quy định CLOSE và dossier: không tự bổ sung dispatch vào CLOSE. Bộ [dbdiagram](dbdiagram/README.md) là snapshot ngày 19/10, không phải nguồn migration hoặc toàn bộ lab schema mới.

Ảnh context người dùng cung cấp phù hợp với chuỗi dependency: lab/quality → compliance → plan/review/reservation → execution → loading/CLOSE → dossier. Cập nhật quan trọng: dev mới đã có intake/result/confirmation lab; không còn đúng nếu nói chưa có kết quả xét nghiệm.

## 3. Hiện trạng đã xác minh trên dev

| Thành phần | Đã có | Còn thiếu cho container |
| --- | --- | --- |
| Identity | `containers.write` cho ExportStaff; `containers.operate` cho MillOperator; `blend.approve` cho CustomerManager; facility assignments | Protected routes/routines theo hành động và scope; permission constant chưa phải workflow |
| Stock/Ledger | `ledger.lots`, positions theo `(lot_id, location_id)`, postings, contributions, processing/transfer/correction, lot HOLD | Loading withdrawal và liên kết reservation; chặn sửa processing có output đã loading |
| Available stock | `lot_json` trả available bằng physical quantity nếu không bị blocked | Trừ outstanding reservations ở mọi read/write path; chưa có giữ tồn thực |
| Lot codes | `ledger.lot_code_reservations` giữ mã RAW/processing output | Bảng này không giữ kg và không được tái sử dụng làm stock reservation |
| Capture/lab | Sample/dispatch, report file/hash, structured result sets/results, confirmation/associations, báo cáo liên quan | Contract evidence dùng cho export assessment và invalidation; không suy confirmed report = export PASS |
| MRL/quality | MRL version/limits/publish/resolve/import; QualityAttribute catalog; so sánh số liệu VN trong lab response | Lot quality measurements; required coverage, interpretation/propagation, versioned margin và immutable decision-time assessment |
| Export/container | Chưa thấy model/route/routine thực thi order, plan/review, stock reservations, loading/container/dossier trong source nghiệp vụ | Cần thêm các phần này, không dựa vào ERD tương lai để coi là đã có |
| Tracing | Permission có trong dev; phần trace từng làm nằm trên `feat/trace`, không phải baseline dev này | Không merge/triển khai lại tracing trong plan container; chỉ lưu IDs và actual quantities để tích hợp sau |

Điểm đối chiếu code: [Permissions.cs](../backend/src/GrainLedger.Modules.Identity/Authorization/Permissions.cs), [Schema.sql](../backend/src/GrainLedger.Modules.Ledger/Sql/Schema.sql), [Reads.sql](../backend/src/GrainLedger.Modules.Ledger/Sql/Reads.sql), [UnifiedLotCodes.sql](../backend/src/GrainLedger.Modules.Ledger/Sql/UnifiedLotCodes.sql), [LabReport.cs](../backend/src/GrainLedger.Modules.Capture/LabReport.cs), [LabReportEndpoints.cs](../backend/src/GrainLedger.Modules.Capture/LabReportEndpoints.cs).

`AssessVn` hiện có numeric baseline comparison, không phải UC-37 với order market/commodity, decision-time MRL, safety margin, required substances, chất lượng và actual loaded scope. Không dùng kết quả hiển thị đó làm quyền commit container.

## 4. Luồng người dùng dự kiến

1. **Export Staff tạo đơn/target:** công ty lấy từ actor; chọn market/commodity, required kg, shipping window, specification version. Tạo container với internal code và target kg; số vỏ container/seal có thể chưa biết khi tạo.
2. **Chuẩn bị hàng:** xem phương án/review hiện hành cho container, actual outputs và OUTPUT reservations được phép dùng. Chỉ hiện hàng đúng owner, facility/location và product/specification; cho biết physical/free/reserved separately.
3. **Draft loading:** chọn lot + source location + reservation, nhập kg thực, thời điểm và bag count nếu áp dụng. Draft không trừ stock, không consume reservation và không chứng minh hàng đã xếp.
4. **Precheck:** backend trả kết quả stock, remaining target, authority, restriction và evidence readiness. Kết quả chỉ để hướng dẫn, không bảo đảm commit sau đó; mọi điều kiện kiểm lại trong transaction.
5. **Commit actual loading:** ghi append-only actual lines; cùng transaction ghi withdrawal âm, OUTPUT CONSUME, assessment LOADING, event và projections. Trả receipt loading với actual kg và số còn thiếu target.
6. **Ghi metadata:** cập nhật container number/seal trước CLOSE qua version check/audit. Không sửa dòng loading đã commit để thay metadata.
7. **CLOSE:** hiển thị toàn bộ committed loadings, kể cả plan cũ; kiểm exact target, order cap, metadata, restrictions và current CLOSE PASS. Thành công sinh một closing dossier bất biến cùng transaction.
8. **Sau CLOSE:** chỉ đọc snapshot/manifest, xuất chứng từ theo version hoặc append supplement có lý do. Không thêm loading, không chỉnh lại snapshot, không tự mở lại container.

Nếu thực tế đã xếp nhưng commit bị từ chối do thay đổi authority/evidence, phải ghi nhận sự cố/đối soát vật lý theo policy được chốt; không đổi thời gian, tạo PASS giả hoặc bấm lại để giấu sai lệch. Không giữ DB transaction mở trong lúc công nhân đang xếp hàng.

UI tối thiểu: danh sách container có tìm kiếm/paging; chi tiết theo các phần Overview, Loading, Close check, Dossier/history. Tách rõ actual loaded mass, remaining target và availability. Lỗi có reason code và hướng xử lý như thiếu OUTPUT reservation, plan STALE, thiếu assessment, HOLD hoặc version cũ. Không đưa chi tiết SQL/grants vào luồng người dùng.

## 5. Quyền và trạng thái

ExportStaff đúng company là actor nghiệp vụ tạo container, ghi metadata/loading và CLOSE theo SRS. CustomerManager review plan và ra lệnh HOLD trong company; không tự được loading/CLOSE chỉ vì là manager. MillOperator có `containers.operate` và assignment được tham gia phần vận hành đúng facility; cần chốt hành động cụ thể trước khi mở endpoint. Không tự biến đây thành quyền duyệt thương mại hoặc bắt thêm một vòng ký duyệt chưa có yêu cầu. MillManager xử lý cách ly/đối soát theo assignment; Administrator quản trị cấu hình, không là wildcard loading/CLOSE.

Vòng đời container đề xuất: `OPEN` → `LOADING` sau loading đầu tiên → `CLOSED` sau CLOSE hợp lệ. Đây là enum cần thống nhất ở bước C0, không khẳng định enum đang có trong DB. Risk/HOLD là restriction độc lập, không ghi đè lifecycle. Precheck PASS không tự đổi trạng thái. Order completion chỉ khi tổng closed mass đúng required mass.

State/history phải lưu actor, occurred time và recorded time. `loadedAt` có thể là thời điểm thực tế nhưng việc cho phép commit luôn dùng authority/evidence hiện tại; không backdate để dùng approval đã hết hiệu lực. Metadata draft có version/ETag, committed loading không update/delete.

## 6. Những invariant bắt buộc

### 6.1. Stock và reservation

- Một loading line rút đúng position `(lot_id, location_id)` của đúng owner/facility; không chọn mỗi lot rồi ngầm rút từ location khác. Kiểm tra stage/product identity, HOLD, receiving/correction/risk restrictions và quota.
- Exact kg là decimal string qua API và `numeric(20,6)` trong DB; không dùng JavaScript float để cộng, so sánh hay phân bổ mass. Bag count không tự thay thế số kg thực; không mặc định bag weight/tare.
- Loading là stock withdrawal, không phải processing tạo lô hàng mới. Không tạo kho đích dương giả để cân bằng; container loaded balance đọc từ committed loading. Ledger contributions của source lots giữ nguyên.
- `outstanding = reserved − netConsumed − released`, `free = onHand − sum(outstanding)`. HOLD trả usable=0 theo restriction, không tạo thêm một lần trừ kg. CONSUME đã giảm onHand, không trừ lại consumed khỏi free.
- Khi consume chính reservation của plan, kiểm `q <= reservation.outstanding` và đủ onHand, bảo vệ các reservation khác; không yêu cầu q phải nhỏ hơn free stock đã loại chính reservation đó. Ví dụ onHand 100, giữ cho P1 60, free 40: P1 được loading 60 nếu mọi gate hợp lệ; P2 không được dùng 60 đó.
- OUTPUT reservation phải khớp chính plan, output allocation, lot/location, review scope và container. INPUT reservation không được dùng cho loading. Có stock nhưng reservation thuộc plan khác vẫn từ chối.
- Stock reservation phải được tôn trọng bởi cả processing, transfer và correction hiện có. Không chỉ thêm kiểm tra ở loading; nếu không, luồng cũ vẫn lấy mất hàng đã giữ. Chặn transfer phần đang giữ, trừ khi có policy chuyển reservation nguyên tử được chốt riêng; transfer cách ly theo HOLD vẫn cần đối soát đúng scope.
- Mỗi CONSUME liên kết duy nhất với withdrawal cùng amount/event/scope; retry không được rút thêm. Không sửa projection để chữa lệch accounting.

### 6.2. Loading và CLOSE

- Một container chỉ có một authority thực hiện/loading mới tại một thời điểm. Không suy quyền chỉ từ plan `APPROVED`/`EXECUTED`: phải có review hiện hành đủ `ALL_REMAINING`/`OUTPUT_ONLY`, đúng actual output và reservations.
- Có thể loading nhiều đợt từ nhiều vị trí/lô hợp lệ. Không cho vượt remaining container target; tổng CLOSE bằng chính xác target, không dùng receiving threshold hoặc processing tolerance cho shipment.
- Khóa order khi kiểm cap để hai container không cùng đóng vượt required kg. Xác định quota khi tạo targets ở C0; tối thiểu mọi CLOSE phải bảo đảm closed mass không vượt order.
- CLOSE dùng mọi committed loading của container xuyên mọi plan version. Ví dụ 10 tấn theo P1, 15 tấn theo P2: đánh giá đủ 25 tấn; không bỏ 10 tấn P1 khi plan đó ngừng được loading mới.
- CLOSE PASS, state CLOSED, unique dossier, metadata snapshot và event cùng commit hoặc cùng rollback. CLOSE không giảm stock lần nữa. Sau CLOSED, loading mới bị từ chối.
- Lab confirmation/supplement, MRL/spec/margin/quality change và HOLD phải tham gia freshness protocol. Đọc trước transaction hoặc dùng assessment cũ không đủ. Thiếu evidence/policy trả INCOMPLETE/BLOCKED; không tự tạo HOLD hoặc coi thiếu dữ liệu là PASS.
- Container HOLD cần target riêng, đúng owner; không giả lập bằng HOLD tất cả lot nguồn vì cùng lot có thể cấp cho container khác. HOLD/response không tự release stock hoặc mở CLOSE.
- Correction processing phải kiểm output đã loading và chặn đảo lịch sử downstream. Unload/reopen/loading correction không có hợp đồng hiện tại: chưa hỗ trợ, cần quy trình đối soát append-only riêng trước khi cho sửa. Không tái dùng processing REVERSE_CONSUME để đảo OUTPUT loading consumption.

## 7. Thiết kế dữ liệu và ranh giới module đề xuất

Đề xuất đặt feature theo layout `Shipments/` trong **Ledger** trước, vì loading/postings/reservations/CLOSE cần chung protected connection/transaction. Không tách service hoặc DB riêng. Tên table/route dưới đây là hợp đồng dự kiến, chưa tạo migration. Nếu team triển khai Export/Compliance ở module khác, dùng in-process ports, module nào sở hữu table thì module đó sở hữu migration; không copy EF entities giữa module.

- **Order/container:** ExportOrder, immutable SpecificationVersion/limits, Container, state transitions/version. Market/commodity/attributes dùng Catalog IDs hiện có; pin specification version khi quyết định.
- **Plan boundary:** BlendPlan/version, PlanReview/authorization, actual execution references, PlanOutputAllocation. Đây là dependency của loading; không dựng một approval boolean thay cho hợp đồng này.
- **Reservation:** stock reservations và immutable reservation events RESERVE/CONSUME/RELEASE; net accounting có đường correction INPUT theo baseline. Không reuse lot-code reservations. Logical `allocation_id` phải map rõ tới position hiện có hoặc allocation entity mới có constraint, không giả có table LotAllocation trong DB.
- **ContainerLoading:** owner/container/plan/review IDs, version/content hash, recordedBy, loadedAt/recordedAt, status DRAFT/COMMITTED, event ID. LoadingAllocation giữ lot/location/actual-output reference, OUTPUT reservation, exact loaded kg, bag count nếu áp dụng và withdrawal/consume links.
- **Assessment:** immutable purpose LOADING/CLOSE, owner/container, decision time, MRL/spec/margin/quality/evidence versions và digest/snapshot actual scope. LOADING gắn loading + plan; CLOSE không lọc theo một plan, plan/loading target fields để null theo RV04.
- **Dossier:** một successful closing dossier/container, unique closing assessment, canonical versioned snapshot + digest. Có thể lưu JSON/bytes trong cùng PostgreSQL transaction theo pattern evidence hiện có; không yêu cầu object store mới. Gồm actual loadings, owner, target/actual mass, container/seal, authorizations, source references, decision versions và actors/times.
- **Document/supplement:** versioned immutable shipment document; dossier supplement giữ evidence ref/hash, reason, actor/time. Tải lại dossier đọc bản lưu, không dựng lại từ danh mục hoặc MRL mới.

Ràng buộc: composite scope FKs/validation cho owner/container/plan/reservation; unique event/idempotency identities; positive mass; consumed/released/outstanding không âm; unique consumption-posting link; unique dossier/container và closing assessment. Không cấp runtime quyền sửa trực tiếp các bảng Ledger mới.

Source contributions cho loading cần được giữ theo thực xếp để tích hợp tracing sau. Khi rút nhiều phần nhỏ từ một lot, không làm tròn từng lần độc lập gây vượt đóng góp nguồn. Phải chốt thuật toán phân bổ/cumulative rounding có version, bảo toàn từng loading và không vượt nguồn qua các lần rút; lưu basis và regression micro-kg. Đây là gate trước khi tuyên bố có container-to-source proportions; không lấy phần trăm làm tròn trên UI làm accounting.

## 8. Transaction và API dự kiến

Mọi mutation dùng persisted actor/scope, CSRF với cookie hoặc bearer theo pattern repo; expectedVersion/ETag cho draft/metadata. Không nhận actor/confirmer ID làm authority. Có idempotency key và canonical content hash: cùng key/nội dung trả receipt cũ, nội dung khác conflict; version stale không được ghi đè.

Commit loading: mở caller-owned transaction READ COMMITTED → advisory lock **714928352** trước các row locks → khóa scope theo thứ tự thống nhất order → container → plan/review → reservations → lot/location → đọc/revalidate evidence basis → assess LOADING → append loading/posting/CONSUME/assessment/event → cập nhật projections/version → commit. Module routines không tự commit. Các publisher evidence, MRL/margin/spec và restrictions phải tham gia protocol hoặc cung cấp revision fence kiểm cuối cùng trong transaction; chưa có fence thì không bật live commit.

CLOSE: cùng lock protocol → lấy toàn bộ committed loadings và metadata hiện tại → kiểm target/order/restrictions → assess CLOSE với current basis → kiểm revision còn nguyên → lưu assessment + CLOSED + dossier + event một transaction. Không giữ transaction qua thao tác người dùng hoặc gọi dịch vụ mạng ngoài; evaluator nên dùng dữ liệu/version contracts nội bộ đã được bảo vệ.

Read APIs dùng bulk queries, search/paging theo owner/facility từ trước paging; không fetch từng lot/report một và không thêm response cache chưa có invalidation contract. Danh sách loading có paging để xem, nhưng CLOSE lấy toàn bộ tập authoritative ở backend, không lấy page đang hiện làm scope.

Routes dự kiến, cần chốt ở C0: `/api/export-orders`, `/api/containers`, `/api/containers/{id}`, `/{id}/loadings`, `/api/container-loadings/{id}/precheck`, `/{id}/commit`, `/api/containers/{id}/metadata`, `/{id}/close-check`, `/{id}/close`, `/{id}/dossier`, `/{id}/documents`, `/api/dossiers/{id}/supplements`. Mọi mutation POST/PATCH; precheck có lưu assessment/audit phải là POST. Dossier/file GET vẫn kiểm permission/owner; không lộ báo cáo công ty khác.

## 9. Thứ tự thực hiện và điều kiện mở từng bước

- [ ] **C0 — Chốt hợp đồng (bắt đầu ngay):** review plan này, thống nhất enums/roles, net mass/bag count, container identity, draft/cancel/correction policy, manual plan/existing stock route và dependency DTOs. Viết API contract/invariants trước migrations; mapping allocation với position hiện tại.
- [ ] **C1 — Order/target/container shell (làm ngay sau C0):** migration riêng cho order/spec/target/container/draft, scoped list/detail/create/metadata/history và web UI. Không thay stock. Khi thiếu authority/assessment, hiển thị pending dependency và disable commit/CLOSE; API cũng từ chối. Done khi scope/version/retry và browser draft workflow pass trên Testcontainers.
- [ ] **D1 — Plan/review/execution + stock reservation (dependency trước C3):** phân công team hoặc làm trong cùng roadmap. Duyệt giữ tồn nguyên tử; execution consume INPUT/earmark OUTPUT đúng plan; patch mọi stock writer/available read/transfer/correction; re-review/invalidation/release phần chưa dùng. Chưa có D1 thì container chỉ dừng ở draft. Có thể phát triển song song C1 bằng contract test cô lập, không seed approval giả vào shared DB.
- [ ] **D2 — Export assessment + freshness + container restrictions (dependency trước C3/C4):** confirmed lab evidence và reported qualifiers chỉ là đầu vào; thêm required coverage, interpretation/propagation/quality/margin rules, decision-time MRL, basis versions và restriction revision. Chốt xử lý late evidence với cả publishers mới/cũ. Missing rule phải block. Không mặc định factor=1, margin=0 hoặc dùng lab numeric baseline làm PASS.
- [ ] **C2 — Loading draft/precheck (sau C1, song song hoàn thiện D1/D2):** đọc actual outputs/reservations theo contract, exact kg, capacity/target check nếu policy có, lý do từ chối và optimistic concurrency. Kiểm UI không nhầm draft với hàng đã commit; chưa bật live mutation nếu dependency chưa sẵn.
- [ ] **C3 — Commit loading (chỉ mở khi D1/D2 thật pass):** protected routine, withdrawal + OUTPUT consumption + retained assessment/event; concurrency/idempotency/partial-loading coverage. Không trừ tồn lần nữa ở CLOSE. Done khi actual API/browser path và accounting reconciliation đều pass, không chỉ test mock.
- [ ] **C4 — CLOSE + immutable dossier:** kiểm whole-container across P1/P2, exact target/order cap, metadata/restrictions/current PASS; lưu snapshot nguyên tử. Done khi CLOSE/evidence/HOLD races và retry chỉ có một dossier pass.
- [ ] **C5 — Dossier reads/documents/supplements:** retrieval/hash checks, scoped download, manifest/export format theo yêu cầu đã chốt; supplement không reopen. Chưa có mẫu chứng từ thật thì không gọi bản manifest kỹ thuật là chứng từ pháp lý đầy đủ.
- [ ] **C6 — Handover/deployment:** hướng dẫn test, demo Testcontainers, review migration/runtime grants, backup/cutover và smoke chỉ đọc sau deploy. Shared migration cần phê duyệt riêng. Theo dõi mọi stock writer dùng cùng bản phần mềm; không để instance cũ không biết reservation cùng ghi kho.

Trong các bước trên, C0–C2 là phần có thể tiến hành trước mà không tự làm toàn bộ lab/compliance/export optimizer. **Không coi UI container hoặc test stub evaluator là hoàn tất loading/CLOSE**. Khi team chưa cung cấp D1/D2, ghi BLOCKED DEPENDENCY tại C3/C4, tiếp tục phần độc lập; không lách gate bằng feature flag cho phép PASS mặc định.

## 10. Quyết định cần chốt, không tự đặt giá trị nghiệp vụ

1. **Vật lý và actor:** MillOperator chỉ đọc/ghi xác nhận vận hành nào? ExportStaff là người commit/CLOSE theo SRS; có yêu cầu bàn giao/đối soát hai bên không? Chưa mặc định bắt hai chữ ký.
2. **Hàng được xếp:** chỉ BAGGED_RICE hay có bulk? Quy tắc product/specification, đo actual net kg, bag count, tare/gross và evidence nào bắt buộc? Không hardcode 25 kg/bao, 25 tấn/container hoặc tolerance 2% receiving.
3. **Plan tối thiểu:** cho phép plan earmark thành phẩm tồn sẵn như thế nào, hay chỉ output của execution thuộc plan? Phải có hợp đồng rõ; không tự gắn production lịch sử hoặc tạo conversion ảo.
4. **Identity/metadata:** internal code uniqueness, container number format/uniqueness trong lịch sử, thay container/seal trước đóng và required sealing evidence. Không tự áp dụng thuật toán/checksum chưa được xác nhận. Number/seal cần đủ trước successful CLOSE; quy tắc chuẩn hóa phải thống nhất.
5. **Target/specification thay đổi:** trước review/draft có thể version-edit; sau loading phải qua policy explicit và re-review, không tự tăng target hoặc đổi order/owner để khớp số đã xếp. Chốt target quota across open containers.
6. **Loading sai/đã diễn ra vật lý:** lỡ xếp sai, nhầm kg/lô, unload, cancel sau partial loading, đổi seal sau CLOSE xử lý ra sao? Chưa có reviewed append-only reconciliation thì chặn sửa/đảo; không dùng soft delete hoặc reopen tự động.
7. **Assessment rules:** required substances, ND/LOQ/LOD/conflicting reports, propagation theo stage, quality aggregation và versioned safety margin. Cần nguồn/policy phê duyệt, không tự lấy giá trị ví dụ trong tài liệu.
8. **Dossier/docs:** bộ field/chứng từ/file format cần xuất, người được xem evidence gốc, retention và policy supplement. Tránh làm lộ lab file nhiều công ty qua dossier.
9. **Rounding provenance:** thuật toán cho partial loading nhiều lần và phân bổ nguồn qua mọi withdrawal; tiêu chí bảo toàn và phiên bản algorithm trước khi lưu snapshot sử dụng cho trace.

Các mục này không cần chốt hết để viết C1, nhưng mục tương ứng phải được chốt trước khi mở write path chịu ảnh hưởng.

## 11. Kiểm chứng bắt buộc khi triển khai

- **Authorization:** cross-company IDs, changed/disabled persisted role/session; ExportStaff khác owner; operator ngoài facility; Administrator không được business bypass; không tin confirmer/owner trên request. Dossier/file không rò dữ liệu.
- **Accounting:** đủ/thiếu onHand, free vs own reserved quantity, sai INPUT/OUTPUT reservation, reservation khác plan/container/location; sum repeated lot lines trước kiểm; atomic withdrawal/consume; retry không double-load; exception giữa các bước không partial stock.
- **Concurrency:** hai loading rút cùng position; loading đối đầu processing/transfer/correction/reserve; loading vs CLOSE/target update/HOLD; hai CLOSE cạnh tranh order cap; invalidation/release không làm reservation bị dùng hai lần.
- **Evidence:** confirmed report nhưng thiếu policy không PASS; late lab/result confirmation/supplement, MRL/spec/margin/quality đổi giữa precheck và commit/CLOSE; current basis bị stale phải reassess/refuse. Lưu history cũ, không rewrite dossier khi evidence mới tới.
- **Lifecycle:** partial loading nhiều đợt, target chưa đủ/vượt; 10 tấn P1 + 15 tấn P2 đều trong CLOSE; plan cũ ngừng quyền không xóa loadings; OUTPUT_ONLY không cho chạy lại input; CLOSE retry unique dossier; sau CLOSED cấm loading; container HOLD chặn CLOSE.
- **Precision/provenance:** micro-kg, maximum numeric range, normalize units exact, bag count không thay kg; partial allocations không tạo/mất source micro-kg; tổng warehouse withdrawals = tổng committed loaded mass; snapshot hash ổn định và truy nguyên đầy đủ IDs.
- **Actual API/browser:** cô lập: source received → processing → review/reserve/execution thật → load hai đợt → đủ target → CLOSE → download snapshot → supplement. Thử request bị từ chối và refresh/mất kết nối/retry. Desktop/mobile không che reason hoặc nhầm available/loaded.

Test backend dùng PostgreSQL Testcontainers; không ghi fixture vào Supabase. Khi đã code, chạy nhóm phù hợp cộng receiving/processing/transfer/correction/reservation regression, frontend tests/build và runtime path. Chỉ mở rộng chạy full suite nếu thay đổi/phạm vi rủi ro cần thiết; ghi đúng checks đã chạy. C0 tài liệu chỉ cần kiểm file/link.

## 12. Điều kiện nghiệm thu và bàn giao

Không gọi flow hoàn thành khi chỉ có CRUD container. C3/C4 chỉ nghiệm thu khi gate nghiệp vụ, stock reservation, current assessment và freshness chạy thật; mọi invariant có regression và UI/API path bằng dữ liệu cô lập. Tồn kho không âm/không double-consume, exact target, same-owner/facility, dossier immutable và audit phải đối soát được sau retry/concurrent writes.

Hồ sơ bàn giao gồm API contract, migrations/grants finite, permission matrix, test/demo steps, dependency status, policy versions và hướng xử lý lỗi. Tracing sâu/container HOLD release/dispatch/customs/payment/optimizer nâng cao ngoài phạm vi này trừ khi có yêu cầu riêng; riêng container restrictions cần để bảo vệ loading/CLOSE vẫn nằm trong dependency tối thiểu.

Khi deploy: giữ `.env`, preflight migration/runtime role, review SQL với đúng owner; không thêm grant PUBLIC, không dùng elevated runtime. Migration mới thêm table/function cần cập nhật `ConfigureRuntimeRoleAsync`/`ValidateRuntimeAsync` và regression grants trong cùng change. Không chạy migrate/seed khi startup, không chỉnh migration đã áp dụng, không reset lịch sử stock. Đồng bộ code/grants giữa các branch và release để tránh thu hồi mất quyền entry routines của feature khác.

## 13. Kết quả lần lập plan

- Đã xác nhận branch dev `b387942`, working tree sạch trước khi tạo tài liệu.
- Đã đọc code/schema/contracts hiện tại, SRS/SDD/ERD rules và domain flows được liệt kê ở mục 2; cập nhật sự tồn tại của lab workflow mới, không dùng đánh giá dev cũ làm kết luận hiện tại.
- Chỉ tạo plan này; chưa thêm bảng/endpoint, chưa chạy build/test ứng dụng hoặc browser vì đây là thay đổi tài liệu. Chưa xác minh schema/grants/deployment trên shared DB trong lần này.
- File/link validation: PASS 14 link nội bộ và 12 file nguồn ngoài repo; không có conflict marker hoặc trailing whitespace, Markdown fences cân bằng. Trạng thái thực hiện sẽ cập nhật khi bắt đầu C0/C1. Chưa commit/push.


## 14. Thực hiện theo phạm vi đã xác nhận

Người dùng yêu cầu tạo `feat/container` từ `feat/trace` và xác nhận **chỉ tập trung container, tích hợp đầu ra order/blend sau**. Đã tạo nhánh từ `f399aa7`. Vì vậy không triển khai duplicate order/plan/reservation/assessment hoặc solver trong nhánh này.

- Hoàn thành phần container của C1: protected scoped create/list/detail/metadata/history, persisted role/company/facility checks, aggregate version, retry receipt và immutable audit. Order chỉ là reference nullable chưa xác minh; không dựng đơn xuất giả. C0 còn các policy thuộc order/blend/physical exceptions cần chốt trước mở live transitions.
- Hoàn thành phần độc lập C2: loading draft nhiều dòng, exact kg, owner/position/facility validation, giới hạn payload, check physical shortage gộp các dòng trùng và lot restrictions hiện hành. Draft là các phương án chuẩn bị riêng, không cộng thành actual loaded mass và không giữ tồn.
- Commit loading/CLOSE có endpoint từ chối thực sự ở SQL/API (HTTP 409), UI disabled với findings rõ ràng; không có posting/CONSUME/CLOSED/dossier giả. Đây là chuẩn bị loading, **chưa nghiệm thu C3/C4**.
- D1/D2, C3/C4/C5 còn chờ các hợp đồng ở [container API](container-api.md#handover-required-from-orderblend-team). Không tạo interface/mock PASS sản xuất hoặc cấp authority qua request flags. C1 không bao gồm order/spec CRUD vì ownership đã được giao team khác.
- Migration mới `20261021000000_ContainerDrafts`, chỉ Ledger; thêm finite runtime execute trong ConfigureRuntimeRoleAsync, giữ nguyên trace và các grant hiện có. Runtime cũ vẫn có thể startup khi chưa cài migration; feature báo 503 nếu thiếu entry routine. C6 deployment còn chờ phê duyệt migration riêng.

Kiểm chứng thực sự đã chạy:

- Backend build kèm `dotnet test ... --filter 'FullyQualifiedName~ContainerTests|FullyQualifiedName~LotTraceabilityTests|FullyQualifiedName~CaptureCutoverMigrationTests'`: PASS **14/14** (4 container, 9 trace, 1 migration/runtime) trên Testcontainers; còn warning CS9113 có sẵn. Bao gồm actual authenticated HTTP, scope/spoof denial, replay, exact micro-kg, stale/concurrent writes, fresh HOLD/stock checks, không thay stock và direct runtime UPDATE bị từ chối.
- `npm test`: PASS **127/127**, 23 file; 5 container regressions gồm exact strings/CSRF, read-only role, ambiguous network retry, stale metadata và bỏ qua response sau khi rời section. Lượt giữa phát hiện test lab cũ trả page shape cho GET report vừa tạo, gây unhandled error; sửa riêng fixture GET và chờ detail render, không đổi code lab production. Lượt cuối không còn unhandled errors.
- `npm run build`: PASS TypeScript/Vite; còn warning chunk lớn hơn 500 kB, không tuyên bố đo loading performance.
- Browser thật với BrowserWorkflowHost/Testcontainers/cookie CSRF: Export Staff tạo container target 8.000000 kg, lưu container/seal, lưu hai dòng 4.000001 + 3.999999 kg; saved total 8.000000, history create/metadata/draft hiện đủ. Loading/CLOSE check trả dependency findings, committedLoadedKg vẫn 0.000000; cả hai nút commit/close disabled. Browser không xác nhận có blend authority vì fixture chỉ dùng stock có sẵn để chuẩn bị draft.
- Mobile 390 px: document width 375, fieldset/select/input không tràn viewport. Test host/dev server được dừng và session fixture riêng được xóa sau smoke.
- File/link/fence validation, source whitespace/marker checks và `git diff --check`: PASS. Không chạy toàn bộ backend suite hoặc đo production performance.

Không ghi lên Supabase, không chạy migration/seed chung, không commit/push. Migration mới hiện chỉ được áp dụng trên Testcontainers; cần deployment có phê duyệt riêng trước khi dùng tính năng trên DB chung.


## 15. Migration theo phê duyệt của người dùng

Sau yêu cầu “chạy migration luôn đi, nhớ cấp quyền execute”, đã thực hiện workflow owner `ASPNETCORE_ENVIRONMENT=Development dotnet run --no-build --no-launch-profile --project backend/src/GrainLedger.Api -- --migrate`, giữ nguyên `.env` và connection settings.

- Preflight chỉ đọc: runtime/migration cùng host/port/database, login riêng; effective runtime role khớp cấu hình và runtime validation PASS. Chỉ Ledger có pending `20261021000000_ContainerDrafts`; Identity/Catalog/Capture không có pending migration. Container table/function chưa tồn tại, trace EXECUTE vẫn có.
- Migration exit 0; chỉ áp dụng `20261021000000_ContainerDrafts`, kèm ConfigureRuntimeRoleAsync theo code hiện tại. Không chạy seed, không tạo container/draft mẫu, không gọi trace POST hoặc data-changing smoke trên database chung.
- Postflight chỉ đọc: cả bốn module không còn pending migration; container table/function tồn tại; runtime EXECUTE container_action=true, trace_lot=true và ValidateRuntimeAsync PASS. PUBLIC không có EXECUTE entry container; runtime không có quyền INSERT/UPDATE/DELETE trực tiếp lên containers/drafts và không được EXECUTE helper container_json.
- Có thể tải lại trang Containers để test preparation. Commit loading/CLOSE vẫn bị chặn bởi dependency như mục 14; migration không tự cung cấp order/blend/reservation/compliance authority. Chưa commit/push.


## 16. Sau merge dev ngày 25/10/2026: làm tiếp được gì?

### 16.1. Baseline và kết luận

Đã fetch origin, chuyển sang dev và fast-forward từ `9bd3e8d` lên `eecde7e` (#15), sau đó merge dev vào `feat/container` (trước merge: `7ad419f`). Không merge container ngược vào dev, không push, không chạy shared migration/seed/grant.

**Có thể tiếp tục phần chuẩn bị loading dựa trên mixed-bulk, lab/residue và tracing mới. Chưa đủ điều kiện bật commit loading hoặc CLOSE thật.** Order → approved blend/review → reservation/execution vẫn thuộc team khác theo phân công đã chốt; không làm thay nền này trong nhánh container.

Dev #15 là tracing được tích hợp từ feat/trace, không phải một tracing engine khác. Phần core trace được lấy nguyên bản mới từ dev; container chỉ thêm workspace section/routes/entry grant bên cạnh nó. Không giữ một fork tracing cũ hoặc triển khai lại trace trong lượt merge.

### 16.2. Những nền mới có thể tái sử dụng

- **Mixed-bulk từ `7f7cb73` (#11):** ingress/current pool generations, withdrawal/aliquot/remainder, exact persisted source allocations, actual resolved processing inputs, pool revision và effective HOLD. Stock picker mới đã có nhãn current pool revision/historical generation và không chọn generation cũ hết tồn cho nghiệp vụ. Container precheck vốn gọi `ledger.blocked`, sau merge sử dụng semantics effective HOLD hiện hành; đó vẫn là lot restriction, chưa có container-level risk provider. Chưa có server fence pool revision riêng trong loading draft/commit.
- **Residue/lab từ `9bd3e8d` (#14):** confirmed report binding/review, versioned rules và company adoption/overrides, exact residue bounds/provenance/dependencies, live preview và immutable evaluations/replay/history. Có thể đọc evidence và lý do UNRESOLVED cho lot dự định xếp. Finite interval/COMPLETE không phải PASS thị trường; service này chưa cung cấp LOADING/CLOSE decision contract hay freshness fence trong transaction stock write.
- **Tracing từ `eecde7e` (#15):** lot/pool graph, Current/History, source field/season, confirmed lab-result entry, selected-node bounds và scoped/redacted views. Có thể mở trace của một loading candidate để kiểm tra nguồn theo quyền; hiện material graph chưa có actual loading/container edges.
- **Bảo trì phụ trợ:** retire equipment và lab draft revision/tombstone đã theo dev; không làm mất immutable operation/report history. Chúng không tạo output authority/reservation cho container.

Các API đọc sẵn dùng: `GET /api/ledger/lots` + safe lookup/detail (pool positions/revision), `GET /api/ledger/lots/{lotId}/residue-preview`, evaluation history/detail, audited trace POST hiện có. Reuse module contracts, không truyền EF entities hoặc copy calculator vào container.

### 16.3. Những dependency chưa có — vẫn chặn C3/C4

- **D1 chưa có:** không tìm thấy persistence/API/in-process contract cho actual export order/spec version, approved blend plan/review/execution và giữ/tiêu thụ OUTPUT stock reservation. `blend.approve`/`containers.*` permission và physical BLENDING operation không thay cho các hợp đồng này. `lot_code_reservations` chỉ giữ mã, không giữ kg. Order reference trong container vẫn là UUID chưa xác minh.
- **D2 có thêm đầu vào nhưng chưa hoàn tất:** đã có residue engine/evidence/rules, nhưng thiếu contract đánh giá theo destination/commodity/specification, required analyte/quality coverage, MRL/margin versions và freshness/revocation fence dùng cho từng LOADING và whole-container CLOSE. Không suy missing margin=0, COMPLETE=PASS hoặc dùng whole-report VN summary làm quyết định xuất hàng.
- **Container restriction:** existing lot HOLD/effective ancestry đã có; chưa có container-level HOLD/risk/status contract hoặc luồng late evidence ảnh hưởng container đã loading/CLOSED. Không coi `ledger.blocked(lot)` là kiểm hết container restrictions.
- **Actual loading/CLOSE:** hiện SQL `containers.status` chỉ cho OPEN; detail trả committedLoadedKg=0; mọi readiness vẫn có bốn unconditional integration findings, endpoints commit/close trả 409. Không có committed loading/withdrawal/reservation-consumption records hoặc closing dossier/document exporter ở nhánh này.

Vì vậy không bỏ các findings `ORDER_PLAN_AUTHORITY_UNAVAILABLE`, `OUTPUT_RESERVATION_UNAVAILABLE`, `EXPORT_ASSESSMENT_UNAVAILABLE`, `CONTAINER_RESTRICTIONS_UNAVAILABLE` chỉ do dev đã có lab/residue/tracing. Đây không phải feature flag có thể bật trước bằng UI.

### 16.4. Plan tiếp theo trong phạm vi container

- [x] **N1 — Chuẩn bị loading hiểu mixed-bulk (làm ngay):** audit picker/position theo owner/facility/current positive stock; hiện picker đã có phần nhãn, cần hoàn thiện draft contract giữ observed pool revision/lot/location, freshness findings khi generation thay đổi và nút reload/reselect. Không âm thầm remap draft cũ sang pool mới. Forward migration nếu đổi persisted draft shape; preserve historical drafts/audit. Test pool top-up/withdraw giữa chọn–save–check, mixed/ordinary positions và scope; mọi test vẫn chứng minh không trừ tồn/reserve.
- [x] **N2 — Evidence panel cho candidate loading (làm ngay sau N1, hoặc song song):** theo `residue.evaluate`, mở existing lot preview/history khi người dùng chọn line; nút trace theo đúng permission. Hiện basis/unit/lower-upper/reasons + evidence/rule refs và timestamp riêng. Không N calls cho toàn bộ danh sách khi mount; batch/bulk nếu thật sự cần nhiều lot. Cùng lot nhiều dòng không tính lại dư lượng theo số dòng. Không tự lấy trung bình nồng độ của hàng xếp chung container như thể đã trộn vật lý; aggregate/sampling semantics cần policy được duyệt. Không mở source/lab ngoài scope cho Export/Mill roles.
- [ ] **N3 — Chốt integration contracts cùng team (làm ngay, không tạo fake provider):** thống nhất order/target/spec version; approved plan/review authority và ALL_REMAINING/OUTPUT_ONLY; actual output/aliquot mapping; OUTPUT reservation ID/outstanding kg; atomic consume port dùng cùng connection/READ COMMITTED transaction/lock; LOADING/CLOSE assessment input/output/basis/revision fence; container restriction version. Bổ sung concrete DTO/ports khi module sở hữu thực sự có contracts. Không nhận ready/approved/assessment ID từ client làm authority, không tạo reservation giả hoặc làm order/blend thay team.
- [ ] **N4 — Commit loading (chờ D1 + D2 + restriction contract):** dưới existing stock advisory lock trước stock rows, revalidate owner/facility, current pool/revision, authoritative actual output và reservation; rút đúng lot/aliquot, consume đúng OUTPUT quota, retain actual load + assessment/evidence basis/audit atomically và idempotently. Phối hợp tất cả stock writer/transfer/correction để tôn trọng reservation. Test double load/overconsumption/retry/concurrent HOLD/review revocation, exact partial loading. Chỉ lúc đạt mới mở button/API success; CLOSE không trừ tồn lần hai.
- [ ] **N5 — CLOSE và dossier (sau N4):** lấy toàn bộ committed loadings xuyên draft/plan version; kiểm target/order cap, metadata/seal, current restrictions và fresh assessment, lưu CLOSED+dossier trong cùng transaction. Draft không tính là loaded. Preserve late evidence/supplement riêng, không rewrite closing snapshot. Document export dựa dossier thật, chưa làm portal/EPCIS/ERP/customs hoặc dispatch lifecycle mới.
- [ ] **N6 — Nối tracing actual container và nghiệm thu (sau N4/N5, phối hợp trace):** container → actual loads → consumed lots/aliquots → sources/plots/percentages, và adverse source/lab → actual containers đúng snapshot/authority. Không nối planned drafts vào material graph. Kiểm nhiều loading/partial/multiple plan versions, zero accounting shares vs chemical dependencies, role redaction và correction scope. Benchmarks riêng theo dataset/target, không suy từ build/test duration.

**Ưu tiên khuyến nghị: N1 → N2 và chốt N3 với team.** N4/N5 chỉ triển khai để bật thật khi các dependency tồn tại và pass contract tests; không cần chờ container để chuẩn bị read UI/evidence. Chỉ in scope/MVP; không solver nâng cao, portal, EPCIS hoặc integration ngoài.

### 16.5. Merge và verification

Đã resolve 16 conflicted files, phần lớn là tracing add/add do squash merge trên dev; giữ container tab/routes/grants và toàn bộ tracing/residue mới. `ledger.container_action`, `trace_lot`, `trace_starts` cùng finite EXECUTE list; metadata helpers vẫn private. Không sửa applied migration SQL để né conflict.

Regression migration cũ giả định chỉ có CustodyTraceability giữa mixed-bulk và trace; khi merge có ContainerDrafts xen giữa, assertion history fail. Đã thêm bước migrate đúng ContainerDrafts, kiểm prior history và legacy marker giữ nguyên, rồi nâng CustodyTraceability và latest. Không bỏ assertion preservation hoặc reset history.

Đã chạy `dotnet build GrainLedger.slnx`: PASS, warning CS9113 đã có. Frontend full suite `--maxWorkers=2`: **221/221 PASS**, 30 files; `npm run build`: PASS TypeScript/Vite, còn warning chunk >500 kB. Backend lượt đầu **131 pass/1 fail** (132 cases), đúng ca history migration kể trên; sau sửa, migration retest **1/1 PASS**. Lượt cuối sau sửa: **132/132 PASS** trên PostgreSQL Testcontainers, gồm container/trace/mixed-bulk/migration/residue evaluation/company policy/report review/permission/cutover HTTP contracts. Không chạy toàn bộ backend solution suite hoặc benchmark production.

Browser smoke dùng test bridge/Vite riêng **5091/5181** + PostgreSQL Testcontainers: Export Staff tạo synthetic preparation target 8.000000 kg, chọn actual available lot/position, save loading draft 8.000000 kg; audit create/draft hiện đủ, committed loaded vẫn 0.000000. Check loading và closure trả dependency findings; Commit loading/Close container disabled. Chuyển sang Traceability thấy current-pool selector và chỉ Forward theo Export role, quay lại Containers vẫn thấy draft đã lưu. Không tạo thêm business feature hoặc giả approved blend; đây chỉ là smoke preparation. Focused click ban đầu dùng exact label `Check loading` thiếu draft UUID nên timeout; sửa locator theo label hiện tại thành công. Không dùng timeout này để kết luận app lỗi. Không đo mobile/loading performance trong lượt này.

Đã dừng browser/dev bridge riêng và xóa session file/config tạm. Đây không phải phê duyệt triển khai shared DB. Migration code có sẵn không chứng minh target DB khác đã đủ migration/grants. Không tự chạy lại migration chỉ vì merge.


## 17. Thực hiện A1–A3 theo plan mới — 25/10/2026

Đã triển khai preparation revision/stale checks, selected-lot residue/trace actions và grouped readiness. Chi tiết source/contracts, tests, giới hạn browser và trạng thái deployment tại [plan hiện hành mục 9](container-loading-next-plan.md#9-kết-quả-triển-khai-a1a3--25102026). N1/N2 đã có; N3 mới có checklist đề xuất, chưa có agreement/provider từ owners. Không mở actual loading hoặc CLOSE; B1–B4/C/D còn phụ thuộc như plan mới. Migration mới chỉ được apply trong Testcontainers, shared deployment cần phê duyệt riêng. Chưa commit/push.
