# Container tracing — deployment và web nghiệm thu, 18/11/2026

**Đã áp dụng migration `20261118010000_ContainerTracingFlowchart` trên Supabase đang cấu hình và test tracing qua web với tài khoản CM/Export.** Người dùng duyệt: “Làm migration đi tôi duyệt”. Không dùng DB local, không chạy toàn bộ backend tests, không seed hoặc tạo/load/close/HOLD container mới. Không commit/push.

## Deployment thực tế

- Preflight xác nhận migration head cũ `20261118000000_ContainerWorkflow`, owner/runtime cùng DB, migration login sở hữu hai hàm được thay definition, script khớp bản đã chuẩn bị.
- Runtime ban đầu thiếu EXECUTE đúng sáu entry container đã được duyệt ở các lượt trước: `ledger_container_action`, `ledger_export_container_read`, `ledger_container_targets`, `ledger_export_container_command`, `ledger_container_production`, `ledger_container_workflow`. Các kiểm tra role/schema/table/sequence/private-function khác đều đạt. Khôi phục đúng sáu chữ ký này theo phê duyệt trước, cấp thêm **một** entry mới `ledger_container_trace_graph(uuid,uuid)` theo phê duyệt tracing hiện tại.
- Backup custom-format schema `grainledger` và data trước thay đổi: **1.416.808 bytes, 1.356 archive entries**, kiểm được archive listing. Đây không phải backup toàn Supabase project; chưa diễn tập restore.
- Áp dụng đúng EF forward script trong transaction READ COMMITTED, advisory lock `714928352`, lock timeout10s/statement timeout120s, kèm các EXECUTE nêu trên. Không gọi broad `--migrate`/`--configure-runtime-role`, không đổi role/connection settings/`.env`.
- Trước/sau migration trong cùng transaction: **124 bảng nghiệp vụ** có count/hash giữ nguyên; **262 đối tượng cũ** giữ owner, ACL chỉ đổi ở sáu container signatures được khôi phục. Hai function mới thêm đúng scope; private bulk helper không được runtime EXECUTE. EF history thêm đúng một migration row.
- Normal API startup bằng restricted runtime PASS; `/health`200. Không dùng migration credential để chạy ứng dụng.

EF script SHA256: `95130cf6d581a4e585b7f8930bfd3736a6d7a5c0d602fa6f6a01be4fb753c1b4`.

Backup SHA256: `5d5f3186ade10bc410320775cb84bbc7f7ea5d1f940c87550098e3a89de0e6f1`.

Artifacts ngoài Git: `/home/phucpinku/.local/share/grainledger-backups/container-tracing-20261118/`, directory0700/files0600. Không publish archive, session hoặc credentials.

## Web/API đã kiểm chứng

Dùng shared fixture đã có: source A `RAW-20261117-000018`, source B `RAW-20261117-000024`, lot `BLD-20261117-000064`. Container demo mới **CTN-20261118-000024**, UUID `983e96fa-c233-4792-8d8e-f74c0f44cadf`, physical metadata `DEMO-CONTAINER-20261118-01`, CLOSED, load4+6kg. Container cũ **CTN-20261117-000014** có load4.000001+5.999999kg từ cùng lot.

Đăng nhập và đăng xuất qua form web thật bằng hai tài khoản hiện hữu. Truy cập tại localhost5173 nhưng backend dùng Supabase hiện có; không có DB fixture local.

| Ca | Kết quả thực tế |
|---|---|
| CM: tìm source A bằng Procurement sources picker → Run forward trace | PASS200, complete true. Graph material5lots trên nhánh forward; targets gồm đúng hai container014/024, CLOSED, linked10kg mỗi container, không restriction. |
| Forward Flowchart và navigation | PASS: hai node container riêng, cạnh BLD→container10kg, không nối RAW trực tiếp vào container. Click container024 mở đúng hồ sơ. |
| CM: manifest container024 | PASS: actual10kg/remaining0/CLOSED, đúng hai load4/6kg có sẵn. |
| Container024 → Trace container sources → Table/Flowchart | PASS200, complete true, không findings; hai source5kg +5kg,50%/50%; ancestry7lots, chỉ một container024, actual edge10kg. |
| Historical kg vs actual kg | PASS: chọn BLD hiển thị historical created20kg, stock0; bảng actual container sources vẫn5+5kg. Line4kg có allocations2+2, line6kg có3+3, giữ proposal/location refs. |
| Table/Flowchart/focus/lot selection | PASS: cùng snapshot, trace request count giữ1 qua các thao tác display; Focus starting container đặt focus đúng link. |
| Backward từ exhausted BLD qua All recorded lots picker | PASS200,7lots ancestry, không actualContainers hoặc container node downstream. |
| Micro-kg trên container014 | API smoke qua authenticated browser session PASS: load4.000001/5.999999 giữ allocations2.000001+2.000000 và2.999999+3.000000; totals5+5/10kg. Không tạo load mới. |
| Export forward/redaction | PASS200 sau khôi phục ACL: hai node container; sourceContributionsVisible false, sources rỗng, loadingEdges không có sourceContributions. |
| Export backward container | PASS403 từ direct endpoint bằng session Export; không có Backward option hoặc Trace container sources control. |
| Keyboard link | PASS: focus node container024 +Enter mở đúng URL. Không dùng quan sát tức thời này làm bằng chứng manifest của Export đã tải xong. |
| Mobile375px | Phát hiện và sửa overflow của grid child chứa graph. Kiểm lại PASS: document width360≤viewport375, graph viewport284px và scrollWidth1046px khi zoom40%; vùng graph cuộn riêng. Desktop document không tràn. |
| Sau lượt web | Đã sign out cả hai tài khoản và đóng tab tự mở. Dừng API/Vite do lượt này mở để giải phóng connections. |

Bằng chứng lọc, chỉ dữ liệu fixture/không secrets: [web evidence JSON](container-tracing-web-evidence-2026-11-18.json). [Kịch bản từng bước](../guides/container-tracing-flowchart-web-test.md).

## Sự cố quyền trong lượt kiểm tra — chưa xác định nguồn thu hồi

Sau migration, runtime validation và các ca CM đều PASS. Trong lúc chuyển sang kiểm Export, đối chiếu DB thấy **cả bảy container EXECUTE đã biến mất trở lại**. Request forward khi đó403 với thông báo PostgreSQL `permission denied for function ledger_container_targets`; đây không phải kết quả từ chối theo business role Export.

So sánh metadata với snapshot sau deployment: đúng bảy function ACL thay đổi, không phát hiện owner/object khác thay đổi; `cron.job` không có trên DB này. Chưa xác định được actor/process thu hồi; không có bằng chứng để quy cho logout, một teammate hay một instance cụ thể. Không sửa trigger/job/role hoặc cấu hình quyền toàn hệ thống dựa trên suy đoán.

Đã khôi phục lại **đúng bảy EXECUTE đã được duyệt**, không chạy lại migration. Runtime validation PASS, Export forward200/backward403 như matrix. **Kiểm tra cuối sau khi đóng host test: migration head/runtime validation/entry EXECUTE/private-helper denial đều PASS.** Việc quyền có thể tiếp tục bị thu hồi ngoài lượt kiểm tra vẫn cần database owner kiểm tra deployment/permission-management đang chạy; chưa kết luận nguyên nhân đã được xử lý.

Một lần final-check cũng gặp pooler `EMAXCONNSESSION` (session pool limit15); sau khi dừng API/Vite của lượt test để giải phóng connections, kiểm tra lại PASS. Không đổi pooler/database/app connection configuration trong scope này. Không suy transient pool error là lỗi số liệu tracing.

## Phạm vi dữ liệu và giới hạn nghiệm thu

- Hash sau toàn lượt smoke chỉ thay đổi **identity_sessions, identity_audit_events, ledger_events**, phù hợp login/logout và **5 TRACEBACK audit thành công**. Mọi bảng nghiệp vụ khác, gồm stock/postings, actual manifests, reservations, closures/dossiers và HOLD, giữ hash sau migration. Các request lỗi không được báo thành success audit.
- Frontend trước deployment44/44 focused tests PASS. Sau sửa CSS/class overflow: targeted trace-flow +ContainerSourceTrace **11/11 PASS**, web production build PASS. Không chạy lại toàn BE. Backend/test project đã compile ở pha implementation, không có thay đổi backend sau đó.
- Các ca runtime tenantB/revocation race, container chưa load/incomplete legacy, nhiều actual lots/locations/P1-P2, custody/handoff, graph vượt limit và season benchmark **NOT RUN** trong lượt Supabase này vì không tạo fixture hoặc mở rộng quyền/dữ liệu. Có frontend regression cho graph limit/empty/multi-target, nhưng không coi đó là backend runtime proof.
- Kết quả là nghiệm thu luồng tracing chính trên fixtures nêu trên; không phải toàn bộ acceptance matrix hay toàn bộ CT đã chạy lại. Chưa công bố đạt NFR30s toàn mùa vụ.
