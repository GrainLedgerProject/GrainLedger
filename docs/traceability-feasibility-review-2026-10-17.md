# Đánh giá khả thi luồng truy xuất nguồn gốc GrainLedger

Ngày đánh giá: 17/10/2026. Mã nguồn đối chiếu: commit `93b7c939e9c100a8c42857f2b2abc18ceb567516`, working tree sạch trước khi thêm báo cáo.

## 1. Kết luận và quyết định đề xuất

**Có thể bắt đầu triển khai truy xuất nguồn gốc hai chiều trên nền tảng hiện tại.** Phạm vi có thể làm ngay là nguồn thu mua ↔ lô ↔ công đoạn chế biến, kèm nguồn ruộng/vụ, khối lượng phân bổ, lịch sử sửa sai và trạng thái hạn chế. Đây là một phần của F18, chưa phải nghiệm thu đầy đủ UC-24/UC-25.

**Toàn bộ luồng kết quả xét nghiệm → lô/kế hoạch/container và container → ruộng còn phụ thuộc nghiệp vụ chưa triển khai.** Lab/sample, kế hoạch xuất khẩu, reservation, loading và container phải có dữ liệu bền vững, giao dịch và quyền tương ứng trước khi chứng minh được luồng hoàn chỉnh. Không thể thay thế các mắt xích đó bằng dữ liệu giả trên giao diện rồi gọi là chức năng hoàn tất.

Kiến trúc modular monolith với PostgreSQL hiện tại phù hợp để phát triển tiếp. Chưa có bằng chứng cần thêm microservice, graph database hoặc một sổ cái thứ hai. Đây là đánh giá thiết kế từ source, không phải bảo đảm hiệu năng khi triển khai thực tế.

Đề xuất: làm phần truy xuất lô trước; song song thống nhất hợp đồng với Capture, lab và export. Giữ nguyên mục tiêu cuối cùng của SRS, nghiệm thu từng phần bằng tiêu chí rõ ràng.

## 2. Phương pháp xác minh và giới hạn

Báo cáo phân biệt bốn loại căn cứ:

- **Yêu cầu:** mục tiêu được viết trong Proposal/SRS; chưa tự chứng minh đã có phần mềm.
- **Thiết kế:** ERD, business rules, SDD; chưa tự chứng minh đã có bảng, migration hoặc API đang chạy.
- **Hiện hữu trong source:** entity, route, SQL, UI và mã test tìm được trong repository.
- **Đề xuất triển khai:** lựa chọn kỹ thuật của báo cáo; cần được kiểm chứng khi thực hiện.

Đã đọc thêm Proposal v1.1 DOCX bằng trích xuất XML, template Report4, các review use case, ghi chú phiên bản, SDD hiện tại, sequence truy xuất, ERD/business rules và thiết kế DB trong repository. Đã đối chiếu các nhận định với routes, permission grants, migration resources, SQL được thay thế ở các migration sau, Capture API và mã regression tests. Không lấy riêng `Schema.sql` hoặc `Operations.sql` ban đầu làm toàn bộ trạng thái cuối.

Đã kiểm tra ZIP hiện có `SDD_v1.1.zip`: ba file `GrainLedger_SDD.md`, `README.md`, `repository_context_vi.md` trong ZIP giống byte với bản trong `review/SDD`. ZIP này không cung cấp một baseline mới cho ba nội dung đó. Không suy ra toàn bộ ZIP hoặc tất cả ảnh đều đã được so sánh.

Phiên này không chạy ứng dụng, build, test, benchmark, API smoke, migration hoặc seed; không truy cập Supabase và không đọc `.env`. Các kết luận “có mã test” chỉ xác nhận test source tồn tại, không có nghĩa test đã chạy thành công trong phiên này. Công việc chỉ thêm báo cáo, vì vậy kiểm tra bàn giao là kiểm tra nội dung, liên kết và diff tài liệu.

## 3. Chọn đúng nguồn và tránh trộn phiên bản

| Nguồn | Giá trị sử dụng và giới hạn |
| --- | --- |
| Proposal v1.1 DOCX, mục 3.2(b), (d), (f) | Xác nhận mục tiêu truy từ plot/lot/adverse result tới container và truy ngược về nguồn, benchmark mùa vụ giả lập. Nội dung proposal chưa đồng bộ hoàn toàn với SRS/SDD. [T1] |
| SRS, UC-24/25, FR-TRC-001–003, NFR-PER-01 | Căn cứ trực tiếp về phạm vi truy xuất, actor, HOLD và mục tiêu hiệu năng. [T2] |
| SDD revision 0.3, mục 3.8, 3.9 | Thiết kế truy xuất đầy đủ, đồng thời ghi rõ phần existing/planned. Tên participant trong sequence không chứng minh phương thức tương ứng đã được code. [T3] |
| ERD v2.1 và BUSINESS_RULES R01, RV01, R04–05, R08–10 | Quy tắc lineage, correction, evidence muộn, toàn bộ loading, HOLD và audit. Là hợp đồng thiết kế, không phải DDL đã deploy. [T4] [T5] [T6] [T7] |
| Review ngày 28/09, 05/10 và `noteV2.md` | Hữu ích để hiểu tranh luận và điểm mở ở thời điểm đó; phải đối chiếu với nguồn hiện tại trước khi áp dụng. [T8] [T9] [T10] [T11] |
| `Report4_Software Design Document.docx` ở gốc review | Là template chứa placeholder, không có đặc tả GrainLedger để bổ sung nghiệp vụ. |
| `SDD/Sources.docx` và PDF hướng dẫn use case | Tài liệu mẫu/trình bày; không dùng làm căn cứ cấp thêm quyền hoặc thêm tính năng GrainLedger. [T12] |
| `docs/dbdiagram` trong repository | Mô hình vật lý thiết kế; README tự nêu giới hạn triển khai. Một số mô tả hiện trạng đã cũ, chẳng hạn chỉ có Google OAuth hoặc facility assignment còn ở tương lai, khác source hiện tại. [C1] |

Các khác biệt đã xác minh:

1. `01_Hieu_dung_GrainLedger.md` từng đề xuất Farmer là actor mới. SRS hiện tại chọn sáu vai nội bộ và giao UC-08 cho Procurement; code `Permissions.ForRole` cũng chưa có Farmer. Không đưa Farmer thành actor truy xuất dựa trên ghi chú cũ.
2. `noteV2.md` còn yêu cầu làm rõ quyền Export Officer. SRS hiện tại đã ghi Export được UC-25, còn UC-24 thuộc Customer Manager. Điểm này đã rõ ở mức yêu cầu, vẫn cần thực thi trong API mới.
3. SRS UC-09/ERD còn diễn đạt receipt tạo stock; repository và SDD đã tách đăng ký nguồn với accepted physical arrival. Báo cáo theo hành vi source khi mô tả hiện trạng và ghi nhận việc cần đồng bộ tài liệu.
4. Proposal DOCX vẫn diễn đạt shortfall trong tolerance thành loss; SRS/ERD/code giữ signed variance riêng có xác nhận. Truy xuất cần giữ đúng dữ liệu đã ghi, không tự chuyển variance thành loss.
5. Phân công và mốc thời gian trong `proposalProject.md` khác Proposal DOCX. Báo cáo không sử dụng chúng để kết luận ai đang phụ trách hoặc cam kết ngày hoàn thành.

SDD là tài liệu dẫn đường tốt, nhưng ghi chú repository của chính SDD cũng là kết quả review source trước đó. Các nhận định hiện trạng bên dưới được kiểm tra lại với code thay vì chỉ lặp lại SDD.

## 4. Luồng nghiệp vụ thực sự phải cung cấp

### 4.1. Truy ngược và truy xuôi

Truy ngược từ container phải lấy **mọi committed loading**, tìm lô đã rút và lượng thực đóng, đi về các công đoạn và nguồn thu mua, rồi nhóm theo nguồn/ruộng/vụ. Không chỉ lấy kế hoạch mới nhất: thiết kế R08 cho phép lịch sử loading của nhiều plan version trong cùng container. [T2] [T6]

Truy xuôi từ plot hoặc source phải xác định các source lot thuộc phạm vi bắt đầu, đi qua các công đoạn thực tế, rồi nối tới stock, kế hoạch và container liên quan. Truy từ một lô trung gian cần đi theo hậu duệ của chính lô đó; các lô có chung nguồn gốc không tự động là hậu duệ của nhau.

Với lab result, điểm bắt đầu còn cần sample/source-lot link và ngữ cảnh bằng chứng. Tập đối tượng có quan hệ nguồn gốc không đồng nghĩa tất cả đều FAIL cho mọi thị trường. Việc đánh giá phải dùng evidence, commodity, MRL, policy và thời điểm phù hợp. [T4]

### 4.2. Quyền và HOLD

- UC-24: Customer Manager xem source contribution tới/từ container.
- UC-25: Customer Manager và Export Officer tìm các đối tượng bị ảnh hưởng trong company scope.
- UC-26: Customer Manager ra HOLD; quyền truy xuất không cấp quyền này cho Export Officer.
- Xử lý HOLD tại nhà máy theo facility assignment; đọc thông báo, phản hồi hoặc cách ly không gỡ HOLD.

**Không tự tạo HOLD không có nghĩa không tự chặn rủi ro.** R05 yêu cầu evidence mới làm mất căn cứ phải tác động tới plan authority, STALE và các hành động mới có liên quan; Customer Manager vẫn chọn từng target HOLD riêng. Đây là nghiệp vụ tương lai, chưa có chỉ vì hiện tại đã có lot HOLD. [T2] [T4]

### 4.3. Ý nghĩa khối lượng

`contributionKg` hiện tại là khối lượng đầu ra được phân bổ về nguồn, không phải số kg lúa gốc đã mua, lượng nguyên liệu đã tiêu thụ, hoặc lượng hiện còn trong kho. Sau sấy/xay, các đại lượng đó khác nhau.

Ví dụ giả lập: đầu vào 60 kg nguồn A và 40 kg nguồn B tạo 80 kg đầu ra. Theo mô hình tỷ trọng, đầu ra được phân bổ A=48 kg, B=32 kg. Nếu dùng 20 kg đầu ra cho công đoạn sau thì attribution cho phần dùng cần áp dụng quy tắc phân bổ và làm tròn tương ứng, không mang nguyên 48/32 kg sang phần 20 kg.

Không cộng đóng góp của tất cả tổ tiên và hậu duệ để báo “tổng kg bị ảnh hưởng”: cùng vật liệu có thể đi qua nhiều công đoạn và bị đếm nhiều lần. Tách lượng lịch sử qua công đoạn, lượng stock hiện tại và lượng đã loading theo đúng ranh giới báo cáo.

## 5. Những gì đã xác minh trong mã nguồn

| Thành phần | Bằng chứng và kết luận |
| --- | --- |
| Nguồn về ruộng/vụ | `SourceLot` có `Id`, `LotId`, `ProcurementItemId`; item có `PlotSeasonId`; source-lot API trả plot/season/farmer từ snapshot thu mua. `SourceLot.Id` khác khái niệm Ledger lot ID, không được dùng thay nhau. [C2] [C3] |
| Đăng ký chưa có stock | PhysicalReceiving lưu pending receiving, measurements/reviews; actual acceptance mới tạo Ledger lot/postings/contributions. Có thể có source identity trước khi có usable Ledger stock. [C4] |
| Quan hệ công đoạn | `ledger.operations.content` giữ inputs/outputs; lô tạo mới có `operation_id`. Event khi commit giữ bản content. Có cơ sở dựng quan hệ công đoạn từ dữ liệu đã ghi. [C5] [C6] |
| Attribution hiện tại | `ledger.contributions(lot_id, source_lot_id, quantity_kg)` là lô → nguồn gốc, không phải bảng input-edge → output-edge như `MassContribution` trong ERD. [C5] [T5] |
| Phân bổ chính xác | Bản `commit_operation` trong `ManageActiveResources.sql` phân bổ output → input rồi input → nguồn, dùng integer micro-kg, largest remainder và UUID tie-break; kiểm tra tổng bằng output mass. [C6] |
| Truy ngược nguồn đang có | `lot_json` trả `sourceLotId` và `quantityKg` trong detail cho vai được phép; UI Stock có phần Source contributions. Đây là detail của một lô, chưa phải API truy xuất đầy đủ. [C7] [C8] |
| Index truy từ nguồn | `ReadScaling.sql` có index `(source_lot_id, lot_id)`. Hỗ trợ tìm lô theo nguồn; không tự cung cấp đầy đủ đồ thị trung gian/container. [C9] |
| Correction | Nhánh correction trong `ledger.command`, gồm bản thay thế ở `ManageRecords.sql`, ghi đảo posting và replacement trong một transaction, sau đó original operation chuyển `CORRECTED`; `lot_json` có restriction `SUPERSEDED`. Cần diễn giải current/history khi truy xuất. [C10] [C17] [C7] |
| Quyền truy xuất | Identity có `trace.read.tenant` cho CustomerManager/ExportStaff, `trace.read.all` cho Administrator. `LedgerEndpoints` chưa có route trace; `can_read_lot` hiện tại cũng không cho Administrator đọc mọi lô. Không thể chỉ thêm UI rồi coi trace permission đã được thực thi. [C11] [C12] [C7] |
| Test có sẵn | Có smoke test chuỗi source → drying → milling, kiểm tra tỷ lệ 50/30/20; có tiny-lineage test giữ ba nguồn với hai nguồn được làm tròn về 0; có các test scope/correction. Chưa xác nhận kết quả chạy phiên này. [C13] [C14] |
| Thành phần tương lai | Không tìm thấy implementation lab/sample/cohort, export plan/reservation/loading/container tương ứng mục tiêu trong các project hiện tại. Các tên xuất hiện trong permission hoặc file thiết kế không chứng minh chức năng đã triển khai. [T3] [C12] |

Luồng đọc hiện hành cũng phải được đọc cùng migration sau: `PagedLists.sql` thay `ledger.read`, còn `ManageActiveResources.sql` thay `commit_operation`. Không dùng read function cũ để thiết kế hợp đồng list hiện tại. [C15] [C16] [C17]

## 6. Kiểm chứng và đính chính nhận định đã trao đổi

| Nhận định trước | Kết quả xác minh lại |
| --- | --- |
| Có thể làm phần truy xuất lô ngay | Giữ kết luận ở mức sẵn sàng bắt đầu phát triển. Cần thêm API/UI, truy vấn đầy đủ và regression coverage; chưa phải tính năng đã sẵn sàng vận hành. |
| Đã có lineage | Đúng, nhưng cần nói rõ hai biểu diễn: attribution về nguồn được lưu sẵn; quan hệ input/output từng công đoạn nằm trong JSON/snapshot. Chưa có bảng cạnh attribution đúng như ERD. |
| Có nền tảng phân quyền | Đúng với nghiệp vụ hiện có. Chưa có endpoint trace thực thi quyền riêng; trace toàn cục của Administrator cần nhánh được cấp rõ ràng, không mở wildcard business access. |
| Lab, export và container là phần thiếu | Được SDD và source xác nhận. MRL resolver và operational evidence không thay cho lab/assessment; physical BLENDING không thay cho approved export plan. |
| Cần xử lý contribution bằng 0 | Được code và tiny-lineage test xác nhận. Đây là cảnh báo thiết kế cho trace mới, chưa phải kết luận có lỗi trace đang chạy. |
| Có test chứng minh tỷ lệ | Có test source với assertion cụ thể. Cần tránh diễn đạt thành “đã kiểm thử thành công” khi phiên này chưa chạy. |
| Các mảnh đọc dữ liệu có hiệu năng tốt | Chỉ có số đo trước đây được ghi trong architecture docs. Chưa có benchmark đầy đủ trace; không dùng timing reverse-source SQL làm timing của F18. [C18] |
| Luồng hoàn chỉnh cần lab/planning/loading | Đúng cho nghiệm thu nghiệp vụ cuối cùng. Riêng phép nối loading → source attribution không phải chờ thuật toán planner hoàn chỉnh mới thiết kế/kiểm thử được; có thể phát triển bằng contract và fixture được ghi nhãn. |

Nhận định kiến trúc khả thi là suy luận kỹ thuật từ mô hình và source đã đọc. Nó chưa chứng minh độ đầy đủ của dữ liệu lịch sử, phiên bản schema đang chạy trên Supabase, độ tin cậy số đo thực tế hoặc khả năng chịu tải sản xuất.

## 7. Các vấn đề phải giải quyết trong implementation

### 7.1. Phân biệt quan hệ ảnh hưởng và khối lượng phân bổ

Không lọc `quantity_kg > 0` để quyết định toàn bộ phạm vi sự cố. Largest remainder có thể đưa một nhánh về 0 micro-kg trong attribution, trong khi input vẫn thực sự được ghi nhận trong công đoạn. Truy quan hệ và tính kg cần có ý nghĩa riêng; nhãn 0 kg phải nêu rõ độ chính xác phân bổ, không diễn giải thành không liên quan.

Không suy ra nồng độ bằng cách nhân tỷ lệ nguồn với lab result một cách tùy ý. Đó thuộc phạm vi residue propagation/evidence policy, khác mass attribution.

### 7.2. Giữ đúng thuật toán và tránh tính trùng

Dùng root contributions đã lưu để báo nguồn của một lô. Khi cần thể hiện kg ở từng cạnh trung gian, phải tái dựng đúng thuật toán lịch sử từ input/output đã commit và nguồn tương ứng, hoặc đề xuất lưu cạnh dẫn xuất có phiên bản và kiểm chứng. Không chỉ nhân tỷ lệ bằng số thực qua nhiều tầng vì kết quả làm tròn có thể khác với thuật toán đang dùng.

Duyệt đồ thị phải xử lý nhánh hợp lại: khử trùng target không có nghĩa bỏ các đường đóng góp hợp lệ; cộng mọi đường cũng có thể đếm trùng nếu attribution về nguồn đã được flatten. Test phải có đồ thị phân nhánh rồi hợp lại.

### 7.3. Correction, pending receiving và dữ liệu lịch sử

Current view phải nhận biết original đã bị thay thế và replacement hiện hành; history vẫn giữ cả hai, liên kết rõ correction. Bản đảo kế toán không phải cạnh chế biến vật lý ngược. Không tính hai bản như hai sản lượng hiện hành.

Nguồn đang pending arrival hiển thị là đã đăng ký/chưa có stock, không báo như đã nhận. Nếu approved shortage thì dùng actual accepted mass cho lượng stock/lineage, đồng thời giữ expected mass trong lịch sử thu mua.

Không tự chuyển dữ liệu synthetic cũ thành nguồn chính thức, không bịa plot/vụ cho dữ liệu thiếu. Giữ khả năng đọc historical snapshot khi master data đã archive/soft-delete; không dùng bộ lọc selector active để làm mất nguồn của lô lịch sử.

### 7.4. Scope và tính đầy đủ

API mới phải kiểm tra active persisted role, action permission, owner và facility theo mục tiêu, kể cả truy cập trực tiếp bằng UUID. Hai vai cùng có `trace.read.tenant` chưa đủ phân biệt UC-24 với UC-25: endpoint phải thực thi quyền hành động đúng yêu cầu.

Không dùng API danh sách stock hiện tại làm toàn bộ tập trace: có phân trang, scope và mục tiêu hiển thị khác nhau. Duyệt tập đầy đủ trong phạm vi được phép rồi mới phân trang kết quả. Không để số đếm, tên, evidence hoặc export file tiết lộ dữ liệu ngoài phạm vi. Nếu xử lý thất bại do giới hạn tài nguyên thì báo không hoàn tất, không trả một tập thiếu như kết quả đầy đủ.

Một nuance cần giữ: `can_read_lot` cho nhân sự nhà máy dựa vào positions và assignment hiện hành. Quyền global trace của Administrator không nằm trong hàm này. Không dùng nguyên helper đó cho mọi use case mới rồi vô tình loại historical targets, hoặc sửa helper chung để mở rộng toàn bộ quyền đọc nghiệp vụ. Thiết kế quyền trace phải có phạm vi riêng và test tương ứng.

### 7.5. Freshness, audit và hiệu năng

Tránh N+1 bằng truy vấn theo tập và bulk contract qua module; không gọi HTTP lại vào cùng host cho từng source. Không truyền EF entity qua boundary. Capture đang sở hữu source metadata; cần một read contract phù hợp thay vì mặc định interface `IProcurementLedgerPort` đã cung cấp bulk trace enrichment.

Giữ READ COMMITTED theo hợp đồng repository. Nhiều lệnh đọc độc lập có thể thấy các trạng thái khác nhau; thiết kế truy vấn/snapshot/version basis phải được kiểm chứng trên đường chạy thật. Một lời gọi hàm chứa nhiều truy vấn không tự chứng minh toàn bộ kết quả cùng snapshot; không tự nâng isolation hoặc khóa mọi trace bằng global write lock.

Thiết kế business rules yêu cầu audit traceback. Cần xác định payload audit gồm actor, scope, target, thời điểm/basis và correlation phù hợp. Không coi trace là hoàn toàn “không ghi gì” chỉ vì nó là tác vụ đọc; đồng thời không log mọi lần xem màn hình hoặc lưu dữ liệu nhạy cảm dư thừa.

## 8. Phạm vi triển khai đề xuất

### Giai đoạn A — Truy xuất lô trên dữ liệu hiện có

Đầu vào: Ledger lot hoặc source identity được phân loại rõ; mở rộng plot/plot-season qua Capture contract nếu cần. Hướng truy: forward/backward. Phạm vi quyền: các hành động cụ thể của CM/Export theo SRS; nếu cung cấp global trace cho Administrator thì đặc tả và kiểm thử riêng theo grant hiện có.

Đầu ra nên có identity, công đoạn và quan hệ, source metadata được phép xem, kg phân bổ dạng decimal string, mẫu số của tỷ lệ, stock hiện tại tách riêng, trạng thái correction/restriction, basis và trạng thái hoàn tất. Đây là đề xuất contract, chưa phải DTO hoặc route hiện có.

Giữ tỷ lệ chính xác bằng số học phù hợp ở backend; giao diện chỉ định dạng hiển thị. Không mặc định tỷ lệ đã làm tròn phải cộng đúng 100.00% nếu chưa định nghĩa quy tắc hiển thị; kg là dữ liệu đối chiếu chính.

### Giai đoạn B — Truy xuất liên quan đến lab và quyết định

Tích hợp sample/source-lot, report supersession, evidence knowledge time và các dependency của bound/assessment. Phân biệt ảnh hưởng vật liệu với ảnh hưởng bằng chứng: một kết quả còn có thể tác động tới các thành viên cohort mà không có đường vật liệu trực tiếp từ lô lấy mẫu sang chúng. Rà soát dependency này cùng nhóm compliance theo R04/R05; không mở rộng đồ thị vật liệu bằng cạnh giả.

Nối kế hoạch và risk invalidation khi các module cung cấp contract; HOLD vẫn là quyết định được cấp quyền riêng. Có thể phát triển bằng fixture/adapter theo contract trước khi tích hợp, nhưng ghi rõ phần chưa có dữ liệu nghiệp vụ thật.

### Giai đoạn C — Container và hồ sơ lịch sử

Nối committed loading cùng actual mass và plan/review version; tổng hợp mọi loading của container. Phân bổ phần loading theo cùng chính sách exact mass đã chốt, tránh chênh tổng khi rút một phần/nhiều lần. Giữ closing dossier bất biến, tách current evidence/risk và supplement về sau.

Các giai đoạn là thứ tự tích hợp/kiểm chứng, không phải đề xuất cắt bỏ lab/container khỏi phạm vi cuối cùng. Chưa ước lượng lịch hoàn thành vì chưa chốt khối lượng, người phụ trách và tiến độ các dependency.

## 9. Tiêu chí nghiệm thu cần có

1. Nguồn → sấy → xay với nhiều phụ phẩm: truy ngược đầy đủ, tổng source kg bằng output mass, không biến hao hụt thành stock.
2. Hai nhánh cùng nguồn nhưng độc lập: truy từ lô trung gian chỉ lấy đúng hậu duệ; đồ thị phân nhánh rồi hợp lại không tính trùng.
3. Partial consumption, nhiều location và transfer: giữ lot identity, phân biệt attribution lịch sử với tồn hiện tại.
4. Tiny mass: không mất quan hệ do contribution làm tròn bằng 0; tổng kg vẫn chính xác sau nhiều tầng.
5. Correction: current/history phân biệt rõ, không cộng original và replacement hai lần, không dựng reverse physical edge.
6. Pending receiving và shortage: chưa nhận không có usable stock; accepted actual khác expected được hiển thị đúng.
7. Cross-company, guessed ID, pending/disabled user, sai facility và sai use case: không rò dữ liệu qua detail, search, counts hoặc export.
8. Hơn 100 target hoặc nhiều trang: tập ảnh hưởng đầy đủ, phân trang không quyết định phạm vi duyệt.
9. Source metadata đã archive: vẫn giải thích được historical source trong quyền được phép; legacy thiếu dữ liệu được đánh dấu rõ.
10. Khi có lab: official supersession, independent conflicting reports, late evidence và cohort dependency được xử lý theo policy; không tự HOLD hoặc tự PASS.
11. Khi có container: nhiều loading/multiple plan versions, actual loading khác đề xuất, closing snapshot và evidence mới không làm mất lịch sử.
12. Concurrent correction/commit/loading/evidence: không ghép các phần kết quả thành một trạng thái chưa từng tồn tại theo basis đã công bố.

Sau implementation phải chạy integration tests bằng PostgreSQL cô lập và smoke đường API/browser thực tế. Không ghi dữ liệu test vào Supabase dùng chung. Không cần chạy application tests chỉ để thêm báo cáo này.

Benchmark F18 phải định nghĩa full-season dataset, số nguồn và fan-in/fan-out, độ sâu, số cạnh/loading/container, concurrency, tài nguyên máy và số lần chạy. Báo warm/cold, DB/API/whole-page riêng và kiểm tra đầy đủ/chính xác cùng với latency. Mốc 30 giây là mục tiêu cần đo; các timing cũ trong architecture không phải kết quả nghiệm thu F18.

## 10. Những quyết định cần chốt trước khi code

- Phạm vi bàn giao đầu tiên là Giai đoạn A hay end-to-end; giữ rõ phần nào chưa nghiệm thu UC-24/25.
- Đầu vào được hỗ trợ, current/history và có cần truy theo knowledge time hay không. Không hứa historical reconstruction tùy ý khi chưa có hợp đồng dữ liệu tương ứng.
- Contract quyền riêng cho source-contribution trace và affected-target trace; phạm vi global trace của Administrator.
- Ý nghĩa kg/tỷ lệ, nguồn gốc mẫu số, partial loading allocation và cách hiển thị nguồn có 0 kg sau làm tròn.
- Contract bulk source metadata và future lab/plan/loading links; chủ sở hữu schema và transaction cho từng phần.
- Quy tắc audit, nhất quán kết quả qua nhiều trang, workload và tiêu chí chứng minh kết quả đầy đủ.

Các giá trị tolerance, cohort bound, processing factor hoặc chính sách vận hành còn thiếu không chặn việc làm trace topology của dữ liệu đã commit. Chúng chặn việc tự đưa ra compliance verdict hoặc cấp quyền xuất hàng không có căn cứ.

## 11. Tài liệu tham chiếu

### Tài liệu ngoài repository

Các link T trỏ tới thư mục review trên máy hiện tại, không phải tài nguyên được commit cùng báo cáo. Khi chia sẻ repository, cần cung cấp bộ tài liệu này riêng hoặc thay bằng liên kết kho tài liệu đã được nhóm thống nhất. Những mục archive/mẫu chỉ được dùng cho lịch sử và hình thức, không thay nguồn nghiệp vụ.

[T1]: </home/phucpinku/Desktop/Week1_report/review/FA26SE272_GrainLedger_Proposal_v1.1.docx>
[T2]: </home/phucpinku/Desktop/Week1_report/review/Report3_Software Requirement Specification.docx.md>
[T3]: /home/phucpinku/Desktop/Week1_report/review/SDD/GrainLedger_SDD.md
[T4]: /home/phucpinku/Desktop/Week1_report/review/erd_v2_1/BUSINESS_RULES.md
[T5]: /home/phucpinku/Desktop/Week1_report/review/erd_v2_1/03-ledger-and-stock.mmd
[T6]: /home/phucpinku/Desktop/Week1_report/review/erd_v2_1/06-loading-hold-and-audit.mmd
[T7]: /home/phucpinku/Desktop/Week1_report/review/erd_v2_1/04-residue-and-evidence.mmd
[T8]: /home/phucpinku/Desktop/Week1_report/review/01_Hieu_dung_GrainLedger.md
[T9]: /home/phucpinku/Desktop/Week1_report/review/02_Kiem_tra_use_case_GrainLedger_V2.md
[T10]: /home/phucpinku/Desktop/Week1_report/review/03_Review_toan_dien_UseCase_V2.md
[T11]: /home/phucpinku/Desktop/Week1_report/review/noteV2.md
[T12]: /home/phucpinku/Desktop/Week1_report/review/SDD/README.md

### Mã nguồn và tài liệu trong repository

Các tham chiếu C dùng đường dẫn tương đối để tiếp tục hoạt động khi clone repository. Vị trí bằng chứng chính được mô tả trong bảng mục 5 và tên hàm dưới đây.

- [C1] DB design README, đặc biệt giới hạn triển khai.
- [C2] Capture source/receipt entities.
- [C3] `GetSourceLotAsync`, `RegisterAsync`, snapshot/result contracts.
- [C4] Physical receiving, measurements và posting actuals.
- [C5] Initial Ledger schema.
- [C6] Bản thay thế `commit_operation`, CTE `input_weights` tới `source_ranked`.
- [C7] `can_read_lot`, `lot_json`, restriction và source attribution.
- [C8] Stock UI và Source contributions.
- [C9] Reverse-source index.
- [C10] Correction transaction trong `ledger.command`.
- [C11] `Permissions.ForRole`.
- [C12] Toàn bộ Ledger route mappings hiện tại.
- [C13] `Official_capture_to_ledger_smoke_proves_stock_lineage_and_authorization`.
- [C14] `Tiny_lineage_largest_remainder_ties_preserve_exact_totals`.
- [C15] Paged `ledger.read`.
- [C16] Migration registration và runtime grants.
- [C17] Forward migration record management; [C21] chứa bản thay thế `ledger.command`.
- [C18] Architecture, mục Read scalability and tracing readiness.
- [C19] Capture-owned Ledger port hiện tại.
- [C20] Forward migration list pagination.

[C1]: dbdiagram/README.md
[C2]: ../backend/src/GrainLedger.Modules.Capture/Procurement.cs
[C3]: ../backend/src/GrainLedger.Modules.Capture/ProcurementEndpoints.cs
[C4]: ../backend/src/GrainLedger.Modules.Ledger/Sql/PhysicalReceiving.sql
[C5]: ../backend/src/GrainLedger.Modules.Ledger/Sql/Schema.sql
[C6]: ../backend/src/GrainLedger.Modules.Ledger/Sql/ManageActiveResources.sql
[C7]: ../backend/src/GrainLedger.Modules.Ledger/Sql/Reads.sql
[C8]: ../apps/web/src/LedgerStock.tsx
[C9]: ../backend/src/GrainLedger.Modules.Ledger/Sql/ReadScaling.sql
[C10]: ../backend/src/GrainLedger.Modules.Ledger/Sql/Commands.sql
[C11]: ../backend/src/GrainLedger.Modules.Identity/Authorization/Permissions.cs
[C12]: ../backend/src/GrainLedger.Modules.Ledger/LedgerEndpoints.cs
[C13]: ../backend/tests/GrainLedger.Api.IntegrationTests/LedgerSmokeTests.cs
[C14]: ../backend/tests/GrainLedger.Api.IntegrationTests/LedgerAcceptanceTests.cs
[C15]: ../backend/src/GrainLedger.Modules.Ledger/Sql/PagedLists.sql
[C16]: ../backend/src/GrainLedger.Modules.Ledger/LedgerDatabase.cs
[C17]: ../backend/src/GrainLedger.Modules.Ledger/ManageLedgerRecords.cs
[C18]: architecture.md
[C19]: ../backend/src/GrainLedger.Modules.Capture/ProcurementLedgerPort.cs
[C20]: ../backend/src/GrainLedger.Modules.Ledger/PaginateLedgerLists.cs
[C21]: ../backend/src/GrainLedger.Modules.Ledger/Sql/ManageRecords.sql
