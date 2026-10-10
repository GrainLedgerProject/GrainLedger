# GrainLedger V2 — DB diagram

Cập nhật 09/10/2026: phần Identity được vẽ theo code và migration đang có trong `GrainLedger.Modules.Identity` (`identity.organizations`, `identity.users`, `identity.sessions`, `identity.audit_events`). Không thay đổi code auth. Các bảng nghiệp vụ còn lại vẫn là thiết kế V2, không có nghĩa tất cả đã được triển khai. Xem [đối chiếu Identity](./IDENTITY_ALIGNMENT_2026-10-09.md).

Bổ sung ngày 06/10/2026: [kết quả xử lý review A01–A05](./REVIEW_ALIGNMENT_A01_A05.md) và [hợp đồng lưu nội dung bất biến](./IMMUTABLE_CONTENT.md). Plot code unique theo farmer; propagation policy unique theo commodity/input stage/output stage/conversion type/version. Các khóa đầy đủ nằm trong DBML và dictionary.

Cập nhật: 05/10/2026. Mức thiết kế: physical database diagram cho PostgreSQL; chưa phải migration hoặc hệ thống đã triển khai.

Thiết kế nghiệp vụ được lập lại từ [GrainLedger_V2.pdf](../../GrainLedger_V2.pdf) và [các quyết định nghiệp vụ đã chốt](../ERD_Design_Decisions.md). Các bảng nghiệp vụ giữ những sửa đổi ERD RV01–RV05 và R2-01; xem [bảng thay đổi](./SYNC_ERD_2026-10-05.md). Phần Identity nay phản ánh migration đang chạy, nên không còn giống hoàn toàn ERD Identity cũ. PostgreSQL là lựa chọn kỹ thuật cho bản này, không phải yêu cầu của V2.

## Mở file nào?

## Chuẩn đặt tên theo GrainLedger V2

Tên chuẩn của **nhóm nghiệp vụ** trong bộ sơ đồ này lấy từ GrainLedger V2, Report 1 mục 6.1 và Report 3 mục 3.2--3.7. Các file nhỏ được tách theo quan hệ dữ liệu để dễ đọc; chúng không tạo thêm chức năng ngoài sáu nhóm dưới đây.

| Nhóm nghiệp vụ chuẩn | Chức năng V2 | File DB diagram tương ứng |
| --- | --- | --- |
| Offline Field Capture | F04--F07 | `02-offline-field-capture.mmd` |
| Mass-Balance Custody Ledger and Processing | F08--F10 | `03-mass-balance-custody-ledger-and-processing.mmd`; phần cấu hình silo/thiết bị ở `01-platform-and-tenant-administration.mmd` |
| Residue Evidence and Compliance Control | F11--F12 | `04-residue-evidence-laboratory.mmd`, `05-residue-evidence-limits-and-risk.mmd`; catalog, MRL, cohort và safety margin ở `05-residue-evidence-limits-and-risk.mmd` |
| Export Order, Feasible Blend Planning, and Shipment | F13--F17 | `06-export-order-blend-planning.mmd`, `07-shipment-loading-and-hold.mmd` |
| Traceback, HOLD, and Event History | F18--F20 | `03-mass-balance-custody-ledger-and-processing.mmd` (đồ thị nguồn gốc), `07-shipment-loading-and-hold.mmd`, `08-traceback-and-event-history.mmd` |
| Platform and Tenant Administration | F01--F03 | `01-platform-and-tenant-administration.mmd`, `05-residue-evidence-limits-and-risk.mmd` |

`00-overview.mmd` là sơ đồ liên kết toàn bộ sáu nhóm, không phải một nhóm nghiệp vụ thứ bảy.

| Bạn muốn làm gì? | File |
| --- | --- |
| Xem toàn bộ bảng trên dbdiagram.io | [grainledger.dbml](./grainledger.dbml) — nhập toàn bộ nội dung vào DBML editor. |
| Hiểu quan hệ tổng quát | [00-overview.mmd](./00-overview.mmd) |
| Xem tài khoản, công ty, silo, thiết bị | [01-platform-and-tenant-administration.mmd](./01-platform-and-tenant-administration.mmd) |
| Xem nông hộ, plot, thu mua, đồng bộ | [02-offline-field-capture.mmd](./02-offline-field-capture.mmd) |
| Xem ledger, stock, conversion, correction | [03-mass-balance-custody-ledger-and-processing.mmd](./03-mass-balance-custody-ledger-and-processing.mmd) |
| Xem sample, bàn giao và lab result | [04-residue-evidence-laboratory.mmd](./04-residue-evidence-laboratory.mmd) |
| Xem MRL, cohort và residue bound | [05-residue-evidence-limits-and-risk.mmd](./05-residue-evidence-limits-and-risk.mmd) |
| Xem export order, plan, review, reservation | [06-export-order-blend-planning.mmd](./06-export-order-blend-planning.mmd) |
| Xem loading, dossier, HOLD, thông báo | [07-shipment-loading-and-hold.mmd](./07-shipment-loading-and-hold.mmd) |
| Xem audit và lịch sử trạng thái | [08-traceback-and-event-history.mmd](./08-traceback-and-event-history.mmd) |
| Tra kiểu dữ liệu, NULL, FK và CHECK từng bảng | [DATA_DICTIONARY.md](./DATA_DICTIONARY.md) |

Mỗi `.mmd` mở độc lập trong Mermaid editor. File `.dbml` dành cho DBML editor, không dán vào Mermaid. Các entity cùng tên trong nhiều hình là cùng một bảng. Những bảng có ghi `Reference table` chỉ là bảng tham chiếu qua phân hệ; toàn bộ thuộc tính của chúng nằm trong sơ đồ sở hữu bảng và file DBML tổng hợp.

## Quy ước database

- 74 bảng trong 8 nhóm; sơ đồ 00 chỉ là tổng quan. Bốn bảng `identity.*` phản ánh migration đang chạy; các bảng còn lại là thiết kế, không tự khẳng định đã triển khai. Số bảng không bằng số chức năng: các dòng chi tiết, bảng nối, lịch sử và phiên bản có bảng riêng.
- `id`: UUID PK, mặc định `gen_random_uuid()`. Các FK đều chỉ rõ bảng đích; mặc định `ON DELETE RESTRICT`, không cascade xoá lịch sử nghiệp vụ.
- Trong Mermaid, `NN` là NOT NULL và `NULL` là có thể để trống. `PK`, `FK`, `UK` lần lượt là khóa chính, khóa ngoại và unique đơn cột. Unique ghép xem DBML/dictionary.
- Khối lượng dùng `numeric(20,6)` kg. Giá trị gốc và đơn vị khi cân được giữ ở dòng thu mua và các dòng input/output conversion; độ phân giải thiết bị không được suy ra từ số chữ số lưu trữ.
- Nồng độ, hệ số, tỷ lệ và chất lượng dùng `numeric(24,12)` trong bản vật lý này, với đơn vị rõ ràng. Thời điểm dùng `timestamptz`; mùa vụ và shipping window dùng `date`.
- GeoJSON WGS84 lưu bằng `jsonb`, phải kiểm tra hình học hợp lệ. Đây là lựa chọn lưu trữ, không giả định đã có PostGIS.
- Mỗi bảng nghiệp vụ tenant có `company_id` tham chiếu `identity.organizations.Id` và unique `(company_id, id)`. Chỉ organization có `Kind = Customer` mới được dùng làm công ty sở hữu dữ liệu nghiệp vụ. FK tự nó không kiểm tra được `Kind`; API hiện tại kiểm tra, các module sau cũng phải kiểm tra. Các quan hệ giữa hai bảng tenant dùng FK ghép `(company_id, parent_id)` để chặn tham chiếu chéo công ty ngay tại database.
- Actor FK đến `identity.users.Id` và event FK đến `business_events` cần kiểm tra quyền/scope riêng vì tài khoản nhà máy/Admin không phải chủ sở hữu customer data. FK cùng tenant không thay thế phân quyền SELECT.
- Identity hiện dùng Google OAuth; `users.Role` chứa một vai trò, danh sách quyền nằm trong code Identity. Không có bảng `roles`, `user_roles`, `password_hash` hay `users.scope` trong migration hiện tại. `identity.audit_events` (thao tác Identity) khác với `business_events` (sự kiện nghiệp vụ).
- `user_facilities` và các bảng facility là thiết kế nghiệp vụ tương lai; hiện chưa thuộc migration Identity. Không suy ra rằng auth hiện đã giới hạn người dùng theo facility.

## Các quyết định vật lý chính

| Vấn đề | Cách thể hiện trong bản mới |
| --- | --- |
| Một lot, nhiều vị trí | `stock_positions` chính là LotAllocation đã chốt: một dòng cho `(lot_id, location_id)`. |
| Không giữ hàng bằng trạng thái lot | `on_hand_kg` và `reserved_kg` là projection số lượng. HOLD là bảng riêng. Một lot có thể giữ chỗ một phần. |
| Giữ hàng trước và sau phối trộn | `stock_reservations` gắn trực tiếp với plan và stock position. INPUT reservation tham chiếu plan item; OUTPUT reservation tham chiếu `plan_output_allocations`, nối output, plan và position. |
| Không tính trùng kho và container | Loading tạo posting giảm vị trí kho và reservation consume. Khối lượng trong container lấy từ loading items, không tạo thêm số dư kho. |
| Chia vị trí và trộn vật lý | Chuyển vị trí ghi hai postings cùng lot. Đổ các lot cùng chủ vào một hỗn hợp tạo conversion MERGE và lot mới. |
| Truy vết sau xay/trộn | `mass_contributions` nối từng input/output và lưu kg nguồn được phân bổ vào output. |
| Sample đúng lot nguồn | `samples.source_lot_id` tham chiếu `source_lots`, không cho gắn nhầm processed lot theo phạm vi lấy mẫu đã chốt. |
| Lưu quyết định lịch sử | `assessments` tham chiếu phiên bản MRL và các bound/quality rows cụ thể; dossier giữ snapshot khi đóng container. |
| Safety margin | `safety_margin_policies` được version theo company/market/commodity; assessment lưu policy và giá trị margin đã dùng. |
| Có thể reject khi thiếu evidence | Assessment INCOMPLETE cho phép ghi lý do từ chối; chỉ PASS mới đủ điều kiện approve. |
| Một order nhiều container | Container thuộc một order. Mỗi plan thuộc một container, và specification phải thuộc đúng order đó. |
| HOLD không bị gỡ ngầm | Không có `released_at`, `archived` hoặc trạng thái handling cho phép dùng hàng trở lại. HOLD của MVP tiếp tục chặn hàng. |
| Thiết bị và công suất | `equipment` thể hiện dryer/milling/packing/scale, với giá trị công suất và đơn vị; conversion có thể chỉ rõ thiết bị sử dụng. |
| F19 có thông báo | `notifications` ghi thông báo trong hệ thống theo HOLD và người nhận có quyền; không thêm tích hợp email/SMS. |

## Nguồn nghiệp vụ và độ bao phủ F01–F20

Các dẫn chiếu sau là diễn giải nội dung, không phải trích nguyên văn. Số trang theo PDF GrainLedger V2 gồm 12 trang.

| Chức năng | Nhóm bảng hoặc cơ chế | Căn cứ V2 |
| --- | --- | --- |
| F01 Công ty, tài khoản | identity.organizations, identity.users, identity.sessions, identity.audit_events; user_facilities là thiết kế chưa triển khai | Mục 4–5, trang 7 |
| F02 Nhà máy, silo, thiết bị, công suất | facilities, locations, silos, silo_compartments, equipment, compartment_occupancies | Mục 5, trang 8 |
| F03 Danh mục và MRL phiên bản | active_substances, commodities, markets, quality_attributes, mrl_versions, mrl_limits | Trang 8; quy tắc 7 trang 9 |
| F04 Nông hộ và thửa có tọa độ | farmers, plots, crop_seasons, plot_seasons | Trang 8 |
| F05 Khai báo phun thuốc | spray_declarations, spray_items | Trang 8 |
| F06 Thu mua, lot, mẫu | procurements, procurement_items, lots, source_lots, samples | Trang 8; ví dụ trang 4 |
| F07 Offline, đồng bộ có đối soát | offline_operations, idempotency key/hash và event áp dụng | Trang 8 |
| F08 Công đoạn và sửa sai | conversions, inputs/outputs, postings, correction_requests | Trang 8; nguyên tắc trang 1, 4 |
| F09 Kiểm tra và từ chối sai | balance_policies, stock_positions, commit transaction và checks | Trang 3–4, 8 |
| F10 Tồn kho và thành phần theo khách | positions, occupancies, contribution graph và tenant FK | Trang 8 |
| F11 Mẫu và xét nghiệm | samples, sample_handovers, lab_reports, lab_results | Trang 8 |
| F12 Kế thừa và truyền bound | cohort tables, residue_bounds, bound_evidence, propagation_policies | Trang 8–9; processing factors trang 5–6 |
| F13 Đơn xuất khẩu | export_orders, order_specifications, specification_limits, containers | Trang 8 |
| F14 Đề xuất và giải thích | blend_plans, plan_items, plan_findings, finding_lots, assessments | Trang 8; khả thi đơn giản trang 11 |
| F15 Duyệt hoặc từ chối | plan_reviews, assessments, safety_margin_policies, reservations, state_changes | Trang 8–9 |
| F16 Xếp/đóng container | loadings, loading_items, output reservations, container status | Trang 8; ví dụ trang 5 |
| F17 Chứng từ và hồ sơ | shipment_documents, dossiers, dossier_supplements | Trang 9 |
| F18 Truy vết hai chiều | mass_contributions, conversion graph, source lot, loading graph; không cần bảng lineage thứ hai | Trang 9; ví dụ trang 5–7 |
| F19 HOLD và thông báo | holds, hold_actions, notifications | Trang 9 |
| F20 Lịch sử, nhật ký | business_events, state_changes, review/correction/handling records | Trang 9 |

## Ràng buộc cần cưỡng chế ở database

DBML đã mô tả PK/FK, nullable, default, unique và index thường. CHECK được ghi cụ thể trong Note và dictionary để chuyển thành migration. Các partial/exclusion index và kiểm tra nhiều dòng dưới đây không được hiểu là đã tự chạy chỉ vì có sơ đồ.

| Nhóm | Ràng buộc | Cơ chế triển khai |
| --- | --- | --- |
| Identity | CUSTOMER user có company; MILL/PLATFORM không có company. Role phù hợp scope. | CHECK và role-assignment guard |
| Tenant | Các dòng nghiệp vụ cùng company; người xem phải được cấp quyền cho company/facility. | FK ghép đã có trong DBML; RLS và authorization cần triển khai |
| Silo | Tối đa một occupancy chưa cleared; không chuyển chủ khi còn stock; không vượt dung lượng; compartment phải là location loại SILO cùng facility với silo. Equipment có location phải cùng facility. | Partial unique index trên compartment với cleared_at IS NULL; FK ghép hoặc trigger/transaction guard, khóa compartment và kiểm tra tổng stock |
| Thu mua | Commit có ít nhất một dòng; đúng farmer/plot/season; mỗi dòng tạo một source lot và posting tăng duy nhất. | Commit procedure với FK và idempotency |
| Conversion | Input/output dương; cùng chủ; đúng stage; input không HOLD; đủ stock; balance trong tolerance; giữ giá trị/đơn vị đo gốc nếu có. | Procedure/trigger kiểm tra trong cùng transaction, khóa các positions theo thứ tự ổn định |
| Variance | Lưu sai lệch thực tế; không tự tăng declared loss. | measurement_variance_kg = input − output − declared loss; chỉ PERCENT_INPUT. Sai lệch khác 0 cần variance_reason và variance_confirmed_by là Mill Manager |
| Lineage | Input/output contribution cùng conversion; mỗi contribution = output mass × input mass / tổng input mass, có làm tròn xác định và tổng theo output đúng kg output; không có cycle. | Commit guard và phép phân bổ decimal với quy tắc làm tròn bảo toàn tổng |
| Correction | Output chưa dùng/loading; đối soát vật lý trước, compensation và replacement phải commit cùng nhau. Request pending/blocked chặn sử dụng output. | Khóa stock/request; transaction chung cho postings, reservation reversal và replacement. Không lộ input ảo, không sửa lịch sử; kiểm tra đúng original/compensation/replacement |
| MRL | Khoảng hiệu lực PUBLISHED không chồng cho market/commodity. | Exclusion constraint theo khoảng thời gian hoặc publication procedure có khóa; giữ lịch sử và source |
| Evidence | Bound links đúng source kind, cùng chất và đơn vị; LAB phải truy về sample của đúng source lot, COHORT phải chứa lot theo policy, CONVERSION phải dùng đúng input lot; evidence đã dùng bất biến. | CHECK XOR, FK và guards; không dùng 0 thay bằng chứng thiếu |
| Safety margin | Policy version theo company/market/commodity, ngày hiệu lực và mode; assessment snapshot policy, margin, value đánh giá và calculation basis. | Unique version, CHECK range/ngày và selection guard tại assessment |
| Plan | Specification thuộc order của container; chỉ một plan có execution_authorized=true, kể cả EXECUTED đang chờ loading; đổi order/specification/input/evidence/rule làm plan STALE. | Partial unique index trên container_id WHERE execution_authorized; guard trạng thái và event chuyển quyền; plan item đã commit bất biến |
| Approval | Chỉ Customer Manager đúng company; PASS đủ bằng chứng; assessment phải cùng plan và purpose REVIEW; reserve không vượt stock. | Review + reservation events + projection + state change nguyên tử. OUTPUT_ONLY duyệt lại output thực tế, không reserve/chạy lại input; giữ provenance P1 |
| Reservation | consumed = tổng CONSUME − tổng REVERSE_CONSUME; outstanding = reserved − consumed − released không âm; tổng outstanding bằng reserved_kg tại position. | CHECK đơn dòng + transaction. Đảo đúng một INPUT CONSUME và posting gốc, không đảo một phần/lặp. Quota tính net consumed + outstanding của mọi reservation cùng plan item; không DML trực tiếp vào projection |
| Output | Output cần xuất được reserve cho plan cùng lúc commit sản xuất. | Qua plan_output_allocations; tổng net consumed + outstanding không vượt assigned_kg. Không reserve phụ phẩm ngoài đơn; position/lot/plan khớp output |
| Loading | Chỉ Export Officer được phép; plan đúng container; OUTPUT reservation khớp position/plan; không HOLD. CLOSE chỉ khi tổng loading bằng target container; tổng container đóng không vượt required mass của order. | Re-assessment rồi loading + posting giảm + consume nguyên tử; shipment tolerance là rule version riêng, không dùng tolerance chế biến |
| Dossier | CLOSE PASS đánh giá toàn bộ actual loading, đúng company/container sau kiểm target mass; evidence snapshots bất biến. | CLOSE assessment có container_id và plan_id/loading_id NULL; snapshot tất cả loading qua mọi plan version. Close transaction; supplement bổ sung bằng chứng về sau |
| HOLD | Đúng một target lot/container. Không nhận production consumption/loading; vẫn cho di chuyển cách ly có ghi sổ. | CHECK XOR, stock guard và quyền Mill Manager xử lý; không RELEASE trong MVP |
| Audit | Committed events, postings, bound versions và quyết định đã dùng không sửa/xóa. | Quyền DB hạn chế và immutable-row guards; draft xử lý riêng |

Xử lý trạng thái đã chốt:

- Plan: DRAFT → PROPOSED → APPROVED/REJECTED; APPROVED → IN_EXECUTION → EXECUTED. CANCELLED/STALE dừng quyền thực hiện/loading mới và giữ lịch sử. Kế hoạch không đổi được re-review với assessment mới; OUTPUT_ONLY chỉ duyệt output, không sản xuất lần nữa. Kế hoạch mới chỉ xử lý phần chưa sản xuất; mọi thay đổi reservation được ghi event.
- Conversion: DRAFT → COMMITTED. Validation thất bại không đưa sai dữ liệu vào ledger; ghi audit thất bại và giữ draft có thể sửa.
- Container: PLANNED → LOADING → CLOSED. HOLD là chiều kiểm soát riêng, có thể xuất hiện sau khi container đã đóng.

## Luồng kiểm tra bằng ví dụ

1. Thu mua 1.000 kg từ một plot: một procurement item → một source lot → một lot → posting tăng một stock position.
2. Chia 600 kg và 400 kg sang hai silo: giữ lot ID; chuyển postings giữa positions. Tổng tồn vẫn 1.000 kg.
3. Xay hoặc gộp với lot khác cùng company: conversion tiêu thụ kg input và tạo output lots; mass_contributions giữ tỷ trọng nguồn.
4. Approve plan: reserve lượng cần dùng ở các positions. Người khác chỉ dùng phần free stock, không dùng lượng đã reserve.
5. Thực hiện phối trộn: consume input reservations, giảm input stock, tạo output và OUTPUT reservation trong cùng transaction.
6. Loading: consume OUTPUT reservation và giảm stock position. Container nhận kg qua loading_items, không còn tính lượng này trong tồn kho silo.
7. Lab về muộn: evidence mới làm quyết định phụ thuộc cần đánh giá lại; không tự dùng approval cũ. Sau đó truy từ source lot qua contribution graph đến stock còn lại và container đã nhận. Customer Manager ghi HOLD đúng target, thông báo đến người có quyền.

## Kết quả kiểm tra

- DBML đã được parser `@dbml/core` chấp nhận và thử xuất sang PostgreSQL DDL thành công.
- Cả 9 file `.mmd` đã được parser Mermaid kiểm tra thành công.
- Mọi FK có target tồn tại. 94 FK giữa các bảng tenant dùng cặp company/key.
- Chưa chạy migration vào PostgreSQL và chưa xác nhận bố cục ảnh render. Không coi xuất DDL thành công là đã triển khai các guards nêu trên.

Các giá trị tolerance vận hành, conservative bound và hệ số chế biến vẫn cần dữ liệu có căn cứ. Schema đã có chỗ lưu phiên bản, nguồn và người xác nhận; bản này không tự gán các giá trị đó.
