# Test web: container thực xếp và đóng — DIRECT_STOCK

Dùng [plan hiện hành](../plans/container-execution-plan-2026-11-13.md) và [API contract](../api/container-execution-api.md). Đây là hướng dẫn thực hiện, không tự chứng minh đã chạy manual browser E2E hoặc nghiệm thu staging.

## Điều kiện

- Backend/web cùng phiên bản feature; database đã được chủ môi trường phê duyệt và cài migration monolith qua `20261113000100_ContainerExecution` cùng finite runtime grants. Không tự chạy migration/seed trên Supabase để thử theo hướng dẫn.
- Dùng database thử nghiệm cô lập hoặc môi trường test được phép ghi. Một ExportStaff và một CustomerManager cùng công ty; công ty B để kiểm quyền.
- Có order/target **10 kg**, phương án **DIRECT_STOCK** và CM đã approve/reserve đúng 10 kg thành phẩm. Lab/rules/adoption/MRL/margin/quality phải đủ căn cứ để preview/approve PASS; không nhập PASS giả.
- DIRECT_STOCK có thể là thành phẩm đã blend **trước** khi lập phương án. Plan chọn BLEND_INPUT_STOCK cần luồng thực thi của bên blend bàn giao, hiện sẽ thấy blocker rõ.
- Chọn vị trí thường, không mixed pool/custody generation và có source attribution đầy đủ. Một lot nhiều nguồn cần một positive position và source complement chứng minh được.

## Luồng 1 — Loading từng phần và CLOSE

1. Đăng nhập **ExportStaff**, mở **Export orders → order → container target → Open**. Sử dụng phần **Loading and closure**, không dùng Ledger → Containers legacy preparation.
2. Xem Actual loaded = **0.000000 kg**, Remaining = **10.000000 kg**, trạng thái PLANNED. Approved reserved stock hiển thị reservation của chính phương án; free stock có thể bằng 0 vì hàng đã giữ, không có nghĩa không được loading quota của mình.
3. Trong **Physical container and seal**, nhập số vỏ/seal synthetic rồi Save. Thông tin được lưu vào cùng planned container, không tạo một preparation/container khác.
4. Bấm **Refresh container checks**. Loading checks phải PASS. Nếu có blocker, xử lý theo lý do; không bỏ qua bằng nút/flag. Việc kiểm lại sẽ diễn ra khi commit.
5. Chọn **Approved position**, nhập **4** kg, tick xác nhận actual loading và bấm **Commit actual loading**. Đây là ghi nhận hàng thực xếp, không phải chỉ giữ chỗ.
6. Xem Actual loaded = **4.000000**, Remaining = **6.000000**, trạng thái LOADING, manifest có một load. Stock tại vị trí và quota reservation cùng giảm 4 kg. Refresh trang không làm loading lần hai.
7. Loading tiếp **6** kg bằng version mới. Actual loaded = **10.000000**, Remaining = **0.000000**, reservation hết quota. Hai dòng/lần trong manifest không bị cộng cùng original output/draft.
8. Refresh closure checks. Đủ target + number/seal + current assessment/restrictions thì PASS. Bấm **Close container** và xác nhận. Status thành CLOSED, manifest cố định; stock không bị trừ thêm.
9. Mở **Read closure dossier**, tải JSON. ExportStaff nhận view đã bỏ source composition/evidence; CM nhận bản đầy đủ. Không coi JSON này là chứng thư pháp lý/PDF đã được duyệt.
10. Mở **Read container history**, kiểm actor/time và các sự kiện; phân trang nếu có nhiều dòng. Container đã đóng không còn thao tác loading/metadata thông thường.

## Luồng 2 — Truy xuất actual source ↔ container

1. Đăng nhập **CustomerManager** cùng công ty, mở container đã load, bấm **Trace container sources**. Xem source kg, tỷ lệ và plot provenance; tổng kg bằng actual loaded, không phải target chưa xếp.
2. Mở **Ledger → Traceability**, chọn nguồn/confirmed lab entry tương ứng, trace forward. **Actual container targets** phải có container vừa load; linked loaded kg là kg từ các lô liên quan, không tự được coi là phần của riêng nguồn bắt đầu. Dùng container backward trace để xem source-attributed kg.
3. Nếu lot thành phẩm có hai nguồn, xem hai phần nguồn và tổng. Ví dụ lot 20 kg gồm 10+10 kg, load tổng 10 kg qua hai lần 4.000001 và 5.999999: các allocations theo micro-kg phải cộng đúng 10 kg và complement còn lại đúng stock; không tính bằng float trên trình duyệt.
4. Không có load thì container trace báo NO_ACTUAL_LOADING/incomplete; draft/approved allocation không xuất hiện như hàng đã xếp. Tracing không tạo HOLD hoặc thay đổi stock.
5. ExportStaff trace forward được, nhưng request backward nguồn của container bị 403. Công ty B không đọc/trace/load/close container A bằng direct API.

## Luồng 3 — Blockers và retry

- **HOLD:** CM đặt container HOLD với lý do. ExportStaff refresh: thấy HOLD và loading/CLOSE bị chặn. HOLD sau CLOSE hiển thị hiện hành nhưng dossier cũ không đổi. Đặt HOLD ở nguồn đóng góp dương cũng chặn hàng bị ảnh hưởng; hệ thống không tự tạo container HOLD mới.
- **Evidence:** sửa bằng reviewed observation/version mới. Proposal fingerprint cũ không còn được dùng để load/close; thấy QUALITY_LIMIT/APPROVAL_EVIDENCE_CHANGED hoặc lý do tương ứng. Không tự reapprove sau partial loading; cần workflow revalidation được bàn giao.
- **Vượt quota/target:** gửi kg lớn hơn remaining, duplicate positions, numeric token thay string hoặc precision >6. Backend từ chối và toàn bộ stock/consume/manifest giữ nguyên.
- **Mất response:** sau POST bị mất mạng/408/5xx hoặc response không hợp lệ, UI khóa thay đổi và hiện **Retry container operation**. Retry cùng request, operation ID và thời điểm; không tạo thêm load. Đối chiếu manifest sau khi có receipt.
- **Hai người:** cùng version gửi hai loading cạnh tranh; một lần thắng, lần còn lại conflict và phải reload. Không overdraw quota/stock.
- **Nhánh chưa hỗ trợ:** BLEND_INPUT_STOCK hoặc pool/custody position hiển thị handoff blocker, không có fake successful load.

Ghi lại build/commit, fixture, actor/role, request IDs, actual kg, before/after stock/quota và manifest/dossier. Không sửa Report5/BM04 local sang PASS chỉ vì đã có code hoặc test fixture. ST-18 manual workflow và ST-21 full-season benchmark cần bằng chứng riêng.
