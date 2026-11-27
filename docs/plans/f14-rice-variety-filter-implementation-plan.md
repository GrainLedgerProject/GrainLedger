# Kế hoạch lọc giống gạo khi lập phương án F14

Ngày lập: 26/11/2026. Nhánh: `feat/f14-rice-variety-filter`.

F14 phải chọn lô đúng giống được yêu cầu trên specification của đơn xuất. Đơn yêu cầu ST25 chỉ được dùng lô có `varietyStatus=SINGLE` và `riceVarietyId` đúng mã ST25. Tập lô phù hợp này tiếp tục phải đáp ứng tồn khả dụng, sản phẩm, compatibility, yield, QC, bằng chứng lab nguồn và MRL. Thiếu ST25 không được tự bù bằng giống khác.

Trạng thái: đã triển khai và nghiệm thu local ngày 26/11/2026. Hồi quy: 97/97 test BE và 28/28 test FE PASS; build FE PASS. Chưa chạy CI remote và chưa deploy DB chung.

## Baseline và các commit liên quan

Nhánh bắt đầu từ `dev` tại `75f534c`, working tree sạch tại thời điểm lập kế hoạch. Đã fetch `origin` để đối chiếu.

| Commit | Nội dung đã có | Phần cần giữ khi triển khai |
| --- | --- | --- |
| `75f534c` | Phân giống Ledger và xem tồn theo vị trí, PR 28 | Mã giống từ Catalog, trạng thái SINGLE/UNKNOWN/MIXED, kế thừa giống qua xử lý và custody |
| `9588e8c` | Lan truyền thay đổi F12 và bắt buộc lab nguồn trong F14, PR 27 | Selection hợp lệ, freshness, recheck và quyền runtime tương thích |
| `a364ad1` | Hoàn thiện container và tracing, PR 26 | HOLD, kiểm lại nhu cầu và các luồng downstream hiện có |
| `5ca7fdc` | MILP và sàng lọc MRL từng input, PR 24 | Bộ giải tổng quát, mục tiêu tối ưu và kiểm định phương án sau giải |

Tại baseline, `CreateExportOrderRequest` và `AddExportSpecificationRequest` chưa có yêu cầu giống. `PlanningCandidate` chưa mang mã giống. `AssessedMilpSearch.Groups` chọn theo trạng thái tồn, sản phẩm và compatibility; khóa nhóm hiện là `LedgerCommodityId:ProductIdentity`. FE ghi rõ Product to ship là loại sản phẩm, không phải giống.

Ledger đã lưu `variety_status`, `rice_variety_id`, `rice_variety_name` trên `ledger_lots`. Không tạo thêm hệ thống giống hoặc bảng tồn riêng cho Exports.

## Quy tắc nghiệp vụ cho bản triển khai này

1. Mỗi specification yêu cầu tối đa một giống. Với sản phẩm ngũ cốc cần lập phương án theo giống, mã giống phải được xác định trước khi tính hoặc lưu proposal. Không triển khai danh sách nhiều giống, tỷ lệ pha giống hay chế độ tự chọn giống bất kỳ.
2. So sánh bằng UUID Catalog. Tên ST25 trên kho, facility, lot code, mã đơn hoặc ghi chú không phải căn cứ chọn giống.
3. Chỉ `SINGLE` và đúng UUID mới đủ điều kiện giống. Loại `UNKNOWN`, `MIXED`, thiếu UUID và khác giống. Không suy giống còn thiếu từ tên tự do hoặc từ duy nhất một nguồn trong một lô nhiều nguồn.
4. Cùng một giống vẫn có thể phối nhiều lô hoặc nhiều vị trí. Head rice và broken rice phải đồng thời khớp giống và compatibility. Lô BLENDED_RICE xuất trực tiếp cũng phải được kiểm giống.
5. Đối với HEAD_RICE, BROKEN_RICE, BLENDED_RICE, yêu cầu giống là ràng buộc bắt buộc. Áp dụng cùng guard cho RAW_PADDY/DRIED_PADDY khi lập phương án trực tiếp theo các target mà hệ thống đang hỗ trợ. BRAN/HUSK giữ quy tắc phụ phẩm hiện tại; không áp dụng nhầm yêu cầu thuần giống gạo cho phụ phẩm.
6. Các điều kiện F12, QC, MRL và tồn vẫn bắt buộc. Đúng giống không được bù cho thiếu lab hoặc vượt MRL.
7. Một mã giống được phép tạo đơn mới phải là mục danh mục hợp lệ theo trạng thái catalog thực tế đang hỗ trợ. Không mặc định Catalog có tính năng archive nếu contract hiện tại chưa có. Khi đổi tên danh mục, nhận diện vẫn dựa vào UUID; snapshot giữ tên tại thời điểm ghi nhận.

## Lưu yêu cầu giống và quản lý phiên bản

Đề xuất thêm `required_rice_variety_id` vào `exports_order_specifications`, tham chiếu `catalog_rice_varieties`. Lưu thêm tên giống tại thời điểm ghi specification để đọc lịch sử, nhưng không dùng tên đó để kiểm điều kiện.

Cột UUID nullable ở tầng lưu trữ để giữ specification cũ và đơn phụ phẩm. Với đơn mới thuộc commodity RICE/POLISHED-RICE và các alias đang được hỗ trợ, API yêu cầu chọn giống. Với PADDY có thể có target phụ phẩm, kiểm bắt buộc theo target ở bước tạo draft/preview; không suy giống ngầm khi null.

Đổi giống bằng cách tạo specification version mới qua endpoint đang có. Không UPDATE specification, demand snapshot, proposal hoặc lịch sử đã chốt. Sửa riêng các giới hạn QC không được vô tình xóa yêu cầu giống hiện tại; contract phải phân biệt giữ nguyên giống với thay giống rõ ràng.

Đơn/specification cũ không được tự gán ST25. Trước khi lập phương án cho target cần giống, người dùng phải tạo specification mới có giống. Draft/preview cũ thiếu trường giống không đủ căn cứ để lưu hoặc duyệt mới. Proposal và lịch sử cũ giữ nguyên để tra cứu; không tự thu hồi reservation hay sửa snapshot đã duyệt. Các luồng đang tiêu thụ assessment chung phải trả rõ thiếu yêu cầu giống thay vì tiếp tục thực thi bằng giả định.

## Luồng xử lý đích

```text
Specification có requiredRiceVarietyId và version
    → Draft chụp yêu cầu giống vào demand snapshot
    → Ledger đọc hàng loạt các lot-position và nhận diện giống
    → Kiểm owner, facility, tồn, HOLD và product compatibility
    → Lọc SINGLE và đúng riceVarietyId
    → Kiểm lab nguồn, F12, QC, MRL, compatibility và yield
    → MILP giải trên tập lô hợp lệ
    → Kiểm lại giống của mọi allocation có số kg dương
    → F14 đánh giá PASS/FAIL/INCOMPLETE
    → Propose/approve đọc lại và tái kiểm trước khi lưu/giữ tồn
```

Giống là điều kiện đủ tư cách của input, không phải hàm mục tiêu tối ưu. Bộ giải không cần biến quyết định giống mới khi đơn đã yêu cầu đúng một giống. Các ưu tiên độ ẩm, ít lô, ít vị trí và dư lượng tiếp tục chạy trên tập lô đã lọc. Bộ giải không được chứa UUID ST25, tên fixture hoặc đáp án của bốn ca local.

## Các bước triển khai và nghiệm thu

| Bước | Công việc | Kết quả phải đạt trước khi chuyển bước | Trạng thái |
| --- | --- | --- | --- |
| 0 | Chốt contract một giống, specification version, cách xử lý đơn cũ và bộ ca kiểm | Danh sách trạng thái và kỳ vọng rõ, không có fallback sang giống khác | Hoàn thành |
| 1 | Thêm migration forward, EF mapping và contract tạo/đọc/revise specification | Lưu đúng UUID; FK hợp lệ; giữ nguyên lịch sử cũ; sửa limits không làm mất giống | Hoàn thành, đã kiểm local |
| 2 | Bổ sung giống trong projection Ledger và DTO candidate | Đọc UUID/status từ dữ liệu Ledger thật, theo cùng snapshot với tồn; không gọi một API cho mỗi lô | Hoàn thành, đã kiểm local |
| 3 | Thêm bộ kiểm eligibility dùng chung và nối vào các đường tính F14 | Sai giống/UNKNOWN/MIXED bị loại trước MILP, không chỉ loại sau khi solver đã chọn | Hoàn thành, đã kiểm local |
| 4 | Đưa giống vào demand/dependency snapshots, fingerprint và recheck | Propose/approve/adjustment không dùng được snapshot cũ hoặc allocation bị chèn sai giống | Hoàn thành, đã kiểm local |
| 5 | Cập nhật FE tạo/sửa đơn và màn hình phương án | Chọn giống riêng với loại sản phẩm; xem được giống thực của lô và lý do bị loại | Hoàn thành, đã kiểm local |
| 6 | Bổ sung unit/integration/FE tests và cập nhật fixtures cũ | Có ca dương, âm, legacy, stale, authorization; bảo toàn các ràng buộc F12/F15 cũ | Hoàn thành, đã kiểm local |
| 7 | Chạy nghiệm thu API và FE trên bộ kho local đã tạo | Ba giống đúng UUID; thiếu giống không lấy giống khác; không reserve khi preview | API 20/20 lượt; FE component 28/28; chưa nghiệm thu trình duyệt thủ công |
| 8 | Double check plan, tài liệu API và CI | Mọi yêu cầu có code/test/ghi chú trạng thái; không tuyên bố PASS cho mục chưa chạy | Đã double check và cập nhật tài liệu; CI remote chờ push |

### Bước 1 về dữ liệu và API

- Sửa `ExportModel.cs`, `ExportRules.cs`, `ExportEndpoints.cs` và EF mapping hiện có. Bổ sung trường trong cả DTO chi tiết, specification history, request tạo đơn và request revision.
- Dùng `POST /api/export-orders` và `POST /api/export-orders/{id}/specifications` hiện có; không tạo endpoint cập nhật giống trực tiếp cho một specification bất biến.
- Giữ ETag, idempotency hash và các khóa hiện có. Đổi giống với cùng OperationId đã dùng cho payload khác phải conflict.
- Khi có active approval, giữ quy tắc hiện tại về sửa specification/invalidate authority; không tự giải phóng tồn.
- Thêm một migration forward có tên mới sau baseline hiện tại. Không chỉnh các migration đã triển khai. Không đổi commodity Paddy/Rice trong bước này.
- Nếu projection `ledger_planning_candidates` chưa có trường giống, sửa nó qua migration forward, giữ chữ ký và scope hiện có. Không cấp runtime quyền SELECT/UPDATE rộng hơn để bù cho projection thiếu trường.

### Bước 2 và 3 về Ledger candidate và MILP

- `ledger_planning_candidates` phải trả `riceVarietyId`, `riceVariety`, `varietyStatus` cho từng vị trí và lot liên quan, từ cùng bulk read `REPEATABLE READ` đang dùng.
- Bổ sung trường tương ứng vào `PlanningCandidate`, `PlanningCandidateSnapshot`, JSON response và candidate revision. Bao gồm UUID/status và dữ liệu nhu cầu giống trong phạm vi freshness.
- Tách `inventoryEligible` khỏi điều kiện giống. Lô đúng tồn nhưng sai giống vẫn có thể hiện ở Show excluded với lý do rõ; không cộng vào tổng tồn phù hợp yêu cầu giống.
- Tạo một helper nghiệp vụ dùng chung, chẳng hạn `RiceVarietyEligibility`, để phục vụ candidate, group validation và recheck. Không nhân bản ba cách so giống ở UI, preview và approve.
- `AssessedMilpSearch` chỉ nhận các candidate đạt guard. Nhánh direct, blend, các nhóm compatibility và bất kỳ đường dự phòng nào còn tồn tại đều phải dùng cùng tập hợp đã lọc.
- `CandidateGroupValidator` xác minh lại mọi allocation dương bằng snapshot hiện tại. Bộ kiểm sau giải không chỉ tin cờ eligibility đã lưu trong preview.
- API thiếu projection/schema giống phải báo dependency chưa sẵn sàng; không coi trường giống thiếu là tương thích mọi giống.

### Bước 4 về snapshot và tái kiểm tra

- `BlendPlanningEndpoints.cs`: chụp UUID, tên và specification version vào demand snapshot; kiểm lại khi đọc draft và tạo preview.
- `ExportCandidates.cs`: đưa yêu cầu giống vào scope revision. Snapshot thiếu giống phải bị từ chối theo chính sách legacy đã chốt.
- `BlendPlanPreviewEndpoints.cs`: lọc trước các lần đọc bằng chứng tốn kém nếu có thể; vẫn kiểm đúng owner/facility trước khi phân loại lô bị loại. Ghi yêu cầu giống và nhận diện giống từng allocation vào kết quả giải thích.
- Thử tỷ lệ, plan residue preview, saved-selection recheck, propose và approve cùng dùng guard. Không để đường điều chỉnh tỷ lệ hoặc đọc lại proposal bỏ qua giống.
- Rà `BlendPlanProposalEndpoints.cs`, luồng quyết định F15 và `ExportResiduePropagation.cs` để tính đủ điều kiện giống khi recompute. Khi downstream gọi assessment chung, giữ cùng guard; không mở rộng thành tính năng container mới.
- Giữ khóa Ledger trước, khóa công ty Export sau và `READ COMMITTED` cho các luồng ghi. Không giữ khóa ghi trong lúc solver tìm nghiệm.
- Một thay đổi nhu cầu giống phải làm cũ draft/preview như thay đổi specification khác. Khi bị chặn, không phát sinh reservation/posting dở dang.

### Trạng thái và thông báo

Tên mã dưới đây là đề xuất contract; khi triển khai phải thống nhất giữa BE, FE và tests.

| Tình huống | Xử lý dự kiến |
| --- | --- |
| Đơn/draft cũ chưa có yêu cầu giống cho target cần giống | `VARIETY_REQUIREMENT_MISSING`; chưa đủ điều kiện tính/lưu proposal, hướng dẫn tạo specification mới |
| Lô SINGLE nhưng khác UUID yêu cầu | `VARIETY_MISMATCH`; loại lô |
| Lô chưa xác định giống hoặc thiếu mã giống | `VARIETY_UNKNOWN`; loại lô, không tự suy đoán |
| Lô MIXED đối với yêu cầu một giống | `VARIETY_MIXED`; loại lô |
| Không đủ kg của giống phù hợp, dù tồn toàn facility đủ | `VARIETY_STOCK_INSUFFICIENT`; không có phương án cho tập tồn đủ tư cách tại thời điểm đọc |
| Có đủ tồn cùng giống nhưng thiếu QC/lab/profile | Giữ trạng thái thiếu bằng chứng F12/F14, không đổi thành lỗi thiếu giống |
| Solver hết thời gian nhưng chưa chứng minh infeasible | Ghi chưa tìm được phương án trong budget; không khẳng định không có nghiệm |
| Yêu cầu giống, candidate identity hoặc specification thay đổi sau preview | Snapshot stale; tính lại trước khi propose/approve |

### Bước 5 về giao diện

- `ExportOrders.tsx`: thêm Rice variety tại form tạo đơn, detail và form tạo specification mới. Sử dụng catalog UUID; giữ giống khi người dùng chỉ sửa giới hạn QC.
- `BlendPlanning.tsx`: hiển thị riêng Required rice variety và Product to ship. Không ghép ST25 vào HEAD_RICE hoặc tạo một commodity ST25.
- Candidate list hiển thị giống/status, tồn phù hợp và lý do loại. Show excluded phải giữ kiểm soát phạm vi công ty/cơ sở hiện tại.
- Preview hiển thị kiểm đúng giống; chỉ cho Save proposal khi toàn bộ kiểm định đạt. Người xem phân biệt được PASS về giống với PASS độ ẩm/tấm/dư lượng.
- Dropdown hoặc nhãn trên FE không phải biện pháp bảo vệ duy nhất; API phải xử lý request giả mạo giống hệt request từ giao diện.

## Bộ kiểm thử cần triển khai

Các nhóm dưới đây có thể được tham số hóa; không cố định số hàm Fact theo số dòng bảng.

| Nhóm | Các ca phải có | Nơi dự kiến |
| --- | --- | --- |
| Eligibility theo UUID | Đúng giống; khác giống cùng tên; cùng UUID đổi tên; UNKNOWN; MIXED; thiếu UUID; kg bằng 0 | Unit tests cho helper giống và group validator |
| Nhu cầu đơn | Tạo đơn có giống; mã không tồn tại; thiếu giống bắt buộc; revise giống; cập nhật QC giữ giống; ETag/idempotency | `ExportRiceVarietyTests.cs`, kiểm API thật |
| Migration legacy | DB có tồn/specification/proposal cũ; không đổi kg hoặc snapshot; null giống không thành ANY | Test cutover dùng PostgreSQL tạm |
| Candidate projection | Trả giống đúng; nhiều vị trí cùng lô; custody/pool hiện hành; scope riêng công ty/facility; Show excluded không leak | Candidate integration tests |
| Direct | HEAD_RICE/BROKEN_RICE/BLENDED_RICE đã có tồn nhưng khác giống không được dùng | F14 integration tests |
| Blend | Nhiều lô ST25 cùng giống đạt; head ST25 + broken OM5451 bị loại; cùng giống nhưng khác product vẫn cần compatibility | F14 integration tests |
| Thiếu tồn phù hợp | Facility đủ tổng kg, riêng ST25 thiếu; không tự bù OM5451/Jasmine | F14 integration tests |
| Không làm yếu compliance | Đúng giống nhưng thiếu lab, selection, QC hoặc vượt MRL vẫn bị chặn; input sai giống không làm hỏng phương án đủ bằng chứng ở giống đúng | Kế thừa và bổ sung tests F12/F14 |
| Các mục tiêu MILP | Các ưu tiên hiện có đều chỉ lấy giống đúng; đổi thứ tự lô, UUID và số lô không đổi quy tắc | Parameterized unit/integration tests |
| Adjustment và API giả mạo | Nhét allocation sai giống; đổi tỷ lệ; gọi residue-preview/propose bỏ qua FE | API integration tests |
| Freshness | Đổi specification sau preview; candidate identity/pool revision thay đổi; approve proposal cũ không reserve | Proposal/decision integration tests |
| F15 và phụ phẩm | Giữ tồn đúng khối lượng một lần; revoke không hồi quy; BRAN/HUSK giữ chính sách phụ phẩm | Regression tests hiện có |
| FE | Chọn giống, detail/history, revise QC, hiển thị excluded, thiếu giống và không cho lưu phương án không hợp lệ | `ExportOrders.test.tsx`, `BlendPlanning.test.tsx` |

Chạy test sau mỗi bước có thay đổi hành vi. Integration test dùng Testcontainers; test migration phải tạo schema thật và cấu hình runtime hạn chế. Sau các nhóm trọng tâm đạt, chạy kiểm tra backend/FE theo workflow CI hiện có. Không thêm một pipeline riêng chỉ để seed fixture local.

## Nghiệm thu với bộ kho local hiện có

Facility: `GrainLedger Rice Variety Demo Mill (synthetic)`. Cả ba giống ở cùng facility, khác vị trí kho. Mỗi giống có 1.200 kg head rice và 200 kg broken rice. Các lô có mã giống SINGLE, QC và lab nguồn hợp lệ. Những dữ liệu này là mô phỏng phục vụ test phần mềm.

| Ca | Đơn hiện có | Trước sửa | Sau triển khai phải đạt |
| --- | --- | --- | --- |
| ST25 900 kg | `EXP-20261126-000029` | Chọn OM5451 và đánh giá PASS | Specification mới yêu cầu ST25; chỉ chọn ST25; PASS các kiểm định còn lại |
| OM5451 900 kg | `EXP-20261126-000030` | Chọn OM5451 do mục tiêu độ ẩm | Specification yêu cầu OM5451; chỉ OM5451, kể cả khi đổi ưu tiên |
| Jasmine 85 900 kg | `EXP-20261126-000031` | Chọn OM5451 và đánh giá PASS | Specification yêu cầu Jasmine 85; chỉ Jasmine 85 |
| ST25 1.300 kg | `EXP-20261126-000032` | Chọn OM5451 và Jasmine 85, vẫn PASS | Không có phương án thuần ST25 đạt yêu cầu 5% tấm; không lấy giống khác bù |

Ở ca ST25 1.300 kg, nếu dùng đủ 1.200 kg head rice ST25 rồi bù tối thiểu 100 kg broken rice 98% tấm, tỷ lệ tấm là `(600×3,5 + 600×4,5 + 100×98) / 1300 = 11,230769%`, vượt 5%. Đây là căn cứ độc lập cho kết quả mong đợi, không phải đáp án hardcode trong solver.

Tại baseline, các đơn trên chưa lưu yêu cầu giống. Khi nghiệm thu đã tạo specification version 2 có UUID đúng và draft mới cho từng đơn; bốn draft cũ được giữ nguyên và bị chặn khi tính lại. Không sửa trực tiếp snapshot cũ.

Nghiệm thu qua API và FE, ghi lại order/specification/plan ID, giống yêu cầu và giống thực của từng allocation. Preview không đổi tồn. Chưa approve hoặc commit phối vật lý khi chỉ đang kiểm việc chọn giống.

## Double check trước bàn giao

- Mọi đường tạo/đọc/revise đơn, draft, candidates, preview, adjustment, propose và approve dùng cùng yêu cầu giống có phiên bản.
- Không còn đường direct, blend hay dự phòng đưa sai giống vào solver hoặc bỏ qua hậu kiểm.
- Đơn cũ, UNKNOWN/MIXED và thiếu bằng chứng có thông báo đúng; không tự sửa lịch sử hoặc reservation.
- Tên giống/kho/facility không thay thế UUID. Không có tên fixture hoặc tỷ lệ mẫu trong thuật toán sản xuất.
- Các thay đổi commodity, giá thành, FIFO/FEFO, phối nhiều giống và tối ưu nhiều container nằm ngoài phạm vi.
- Bộ kiểm mới, hồi quy F12/F14/F15 và FE/build có kết quả được ghi riêng; CI phải được kiểm sau khi push, không suy từ test local.
- Migration và smoke ghi dữ liệu chỉ chạy local theo phạm vi hiện tại. Không migrate/seed DB chung nếu chưa có yêu cầu riêng.
- Cập nhật tài liệu API, sơ đồ schema đang dùng và trạng thái từng bước trong kế hoạch. Giữ phiên bản migration đã áp dụng.

## File tham chiếu chính

- `backend/src/GrainLedger.Api/Features/Exports/ExportModel.cs`
- `backend/src/GrainLedger.Api/Features/Exports/ExportRules.cs`
- `backend/src/GrainLedger.Api/Features/Exports/ExportTargetProducts.cs`
- `backend/src/GrainLedger.Api/Features/Exports/Orders/ExportEndpoints.cs`
- `backend/src/GrainLedger.Api/Features/Exports/Planning/ExportCandidates.cs`
- `backend/src/GrainLedger.Api/Features/Exports/Planning/CandidateGroupValidator.cs`
- `backend/src/GrainLedger.Api/Features/Exports/Planning/AssessedMilpSearch.cs`
- `backend/src/GrainLedger.Api/Features/Exports/Planning/BlendMilpSolver.cs`
- `backend/src/GrainLedger.Api/Features/Exports/Planning/BlendPlanningEndpoints.cs`
- `backend/src/GrainLedger.Api/Features/Exports/Planning/BlendPlanPreviewEndpoints.cs`
- `backend/src/GrainLedger.Api/Features/Exports/Planning/BlendPlanProposalEndpoints.cs`
- `backend/src/GrainLedger.Api/Features/Exports/Planning/BlendPlanDecisionEndpoints.cs`
- `backend/src/GrainLedger.Api/Features/Exports/Planning/ExportResiduePropagation.cs`
- `backend/src/GrainLedger.Api/Features/Ledger/Planning/PlanningCandidateRead.cs`
- `apps/web/src/ExportOrders.tsx`
- `apps/web/src/BlendPlanning.tsx`

## Kết quả triển khai và double check ngày 26/11/2026

- Migration `20261128000000_ExportRiceVarietyRequirement` đã áp dụng trên `127.0.0.1:5433/grainledger`; tổng 24 migration, runtime guard PASS, tài khoản runtime vẫn hạn chế quyền. Không migrate/seed DB chung.
- Ledger candidate bulk read đã có giống từ baseline; chỉ mở rộng DTO và scope fingerprint. Không thêm truy vấn cho từng lô. Các kiểm production/workflow đọc giống bằng một bulk read có kiểm quyền và read-only repeatable-read.
- `RiceVarietyEligibility` dùng UUID/status, được áp dụng trước MILP, trước fallback và khi hậu kiểm selection. Bước sàng lọc dư lượng tiếp tục dùng tập đã lọc giống, không tái đưa lô bị loại vào tìm kiếm.
- Dependency fingerprint dùng cùng basis ở preview, production, workflow và worker. Bằng chứng không đổi vẫn giữ CURRENT; dữ liệu thay đổi vẫn bị stale theo cơ chế cũ. Không sửa snapshot đã chốt.
- Cả sáu chế độ mục tiêu (không thêm ưu tiên và năm ưu tiên hiện có) có test loại khác giống/MIXED/UNKNOWN. API có ca thiếu giống, UUID không tồn tại, giữ giống khi revise limits, đổi giống và lịch sử version, legacy, stale draft, payload ép chọn sai giống và bảo toàn tồn.
- Hồi quy BE: 97/97 PASS, không skip; gồm `RiceVarietyEligibilityTests`, `F14RiceVarietyApiTests`, `CandidateGroupValidatorTests`, `BlendMilpSearchTests`, `BlendPlanProposalTests`, `ExportOrderTests`, `ContainerWorkflowTests`, `ResidueExportPropagationTests`. Các fixture ghi tải hàng dùng giờ DB; test lịch sử HOLD bảo đảm thời điểm DB đã qua thời điểm proposal, không nới guard production.
- Hồi quy FE: 28/28 PASS qua `ExportOrders.test.tsx` và `BlendPlanning.test.tsx`; `npm run build` PASS (cảnh báo chunk size hiện có). Chưa chạy nghiệm thu trình duyệt thủ công.
- Bốn đơn local chạy cả năm ưu tiên: 3 đơn 900 kg PASS đúng giống; ST25 1.300 kg không có phương án PASS và không thay bằng giống khác. Cả 24 output lots giữ nguyên physical/available stock, 12 grain inputs giữ F12 COMPLETE. Không approve proposal hoặc commit phối vật lý.
- CI hiện có chạy toàn bộ test BE trên Docker và test/build FE, nên bộ kiểm mới được phát hiện tự động. Chưa push/chạy remote CI trong đợt này; không tuyên bố CI PASS.
- Contract và sơ đồ schema bổ sung tại [F14 rice variety API](../api/f14-rice-variety-filter.md). Không thay đổi thuật toán dư lượng F12, commodity mapping, quy tắc nhiều giống, chi phí hay FIFO.

| Đơn local | Specification | Draft mới |
| --- | --- | --- |
| EXP-20261126-000029 | v2, ST25 | e9aa91ea-a50d-45e0-988b-96c094a77c23 |
| EXP-20261126-000030 | v2, OM5451 | 4ad6a51d-8aa3-4e41-a134-3a2638d10398 |
| EXP-20261126-000031 | v2, Jasmine 85 | ffc67442-3482-4e3e-88ea-b53e01e5c273 |
| EXP-20261126-000032 | v2, ST25 | df48b130-cf6e-4dc9-a03e-07722a40ff5b |

Chạy riêng bộ kiểm giống:

```powershell
dotnet test backend/tests/GrainLedger.Api.IntegrationTests/GrainLedger.Api.IntegrationTests.csproj --filter "FullyQualifiedName~RiceVarietyEligibilityTests|FullyQualifiedName~F14RiceVarietyApiTests|FullyQualifiedName~Every_objective_excludes_other_mixed_and_unknown_varieties"
npm run test --workspace @grainledger/web -- src/ExportOrders.test.tsx src/BlendPlanning.test.tsx
```

Docker phải chạy để test API tạo PostgreSQL tạm. Các test không ghi lên DB dùng chung. Phiên API kiểm chứng riêng cổng 5081 được dừng sau nghiệm thu; khởi động lại backend đang chạy để FE dùng code mới.

## Cập nhật sau reset kho local

Theo yêu cầu reset dữ liệu demo ngày 26/11/2026, các đơn và draft local cũ trong bảng nghiệm thu trên đã được xóa sau khi sao lưu. Bộ kho hiện tại gồm một facility, 24 vị trí, ba giống; dùng các đơn EXP-20261126-000033 đến 000036. Xem [hướng dẫn dữ liệu local hiện tại](../guides/local-rice-warehouse-demo.md). Đã kiểm lại 20 lượt F14, tồn kho và F12 qua API; không thay đổi code thuật toán.


## Điều chỉnh kho phụ phẩm ngày 27/11/2026

Bộ demo đã đổi từ 24 sang 20 vị trí: các khu gạo tách theo giống, cám dùng Shared Bran Store và trấu dùng Shared Husk Store. Kho chung không tự MERGE các lô. Policy hiện có mặc định cho phép gộp khác giống riêng BRAN/HUSK, Customer Manager có thể override từng loại; không sửa thuật toán để thay quy tắc này. Các đơn local hiện tại là EXP-20261127-000037 đến 000040; xem [hướng dẫn demo hiện tại](../guides/local-rice-warehouse-demo.md). Kiểm policy/stock/segregation: 6 test BE và 1 test FE PASS.



## Yêu cầu cân kg nguyên ngày 27/11/2026

Đã chuyển biến kg của solver thành số nguyên, floor tồn theo từng vị trí, tối thiểu 1 kg mỗi allocation. Nhánh dự phòng, ratio range và hậu kiểm dùng cùng quy tắc; không làm tròn tỷ lệ liên tục trên FE. Ledger vẫn giữ precision đo thực tế. Đã kiểm 112/112 test BE, 28/28 test FE và build FE; API local công bố quantityStepKg=1. Chưa Calculate lại đơn demo hoặc chạy remote CI. Bốn tên vị trí đã bỏ nhãn `(challenge)` qua API setup, giữ nguyên UUID và tồn. Xem [contract kg nguyên](../api/f14-whole-kg-planning.md).
