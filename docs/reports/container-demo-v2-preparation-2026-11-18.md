# Demo container/tracing mới — chuẩn bị và rehearsal, 18/11/2026

Đã tạo commit **`4152dcf feat(ledger): add container flowcharts to tracing`** trên nhánh `feat/container` trước khi làm demo mới, theo yêu cầu. Chưa push. Tài liệu/demo mới trong lượt này được để riêng cho người dùng xem, chưa tạo commit tiếp.

**Bàn giao:** [một luồng demo đã chốt mọi lựa chọn](../guides/container-tracing-lecturer-demo-2026-11-18-v2.md). Fixture mới trên Supabase, lot **BLD-20261118-000079 còn physical10kg và available10kg**, kiểm bằng actual API lúc **2026-11-18T12:13:17.889Z (19:13:17 giờ Việt Nam)**. Chưa tạo order/container của lượt trình bày; người dùng sẽ tạo đối tượng mới theo guide. Container rehearsal025 đã CLOSE, không reset/load lại.

## Dữ liệu và phạm vi thực hiện

- Thực hiện bằng API có login/CSRF của các tài khoản PurchasingStaff, MillOperator, CustomerManager; rehearsal qua form web thật với ExportStaff/CustomerManager. Tất cả dùng Supabase cấu hình sẵn, không tạo DB local, không chạy full BE tests hoặc broad seed/migration/grant commands.
- Tái sử dụng farmer/plot/season, marketDEMO-CONTAINER, catalogPADDY, facility/kho/equipment và các policy/compatibility/MRL/profile đã có. Không đổi commodity hoặc history của dữ liệu cũ, không tạo policy khoa học mới để làm PASS.
- Các stock HEAD cũ phần lớn đã reserved; **không lấy stock đó**. Tạo procurement mới **PRC-20261118-0007** với hai source expectation20kg, operator nhận đúng20+20kg.
- SourceA **RAW-20261118-000071**, Ledger ID `9a2da471-cb8c-474a-aeb8-e220dc3bafc0`; sourceB **RAW-20261118-000072**, Ledger ID `048d5b61-949d-417e-955c-ea36435182aa`. Hai source lots của cùng procurement/plot, không phải hai nông dân/thửa khác nhau.
- Tạo samples **SMP-20261118-0018** và **SMP-20261118-0019**; report **LAB-DEMO-CONTAINER-20261118-02**, ID `709eb7f8-5761-475d-8ba2-6ea0b7d3ded6`. Nội dung gốc là text synthetic có sample/seal đúng, không phải file hoặc chứng nhận lab thực tế. IPT002 `<0.02mg/kg`, method khớp profile demo hiện hành. Confirmation trả cả hai residue bindings published=true, không findings.
- Hai drying20kg, hai milling20→head10+broken10, một blendinghead10+10→**BLD-20261118-00007920kg**, ID `69f2035a-5dfe-4ffc-a7ff-504a22a138a2`, tại Demo Container Blend Store. Mã RAW/DRY/HDR/BRK/BLD/SMP/PRC đều do API sinh theo counter hiện tại.
- Quality evidence mới chỉ cho fixture này, moisture observation13.2% v1. Residue evaluation của blend COMPLETE, interval0–0.02mg/kg theo profile hiện có `DEMO-CONTAINER-REGRESSION-IPT002-PADDY-FLOW`.
- Sau rehearsal10kg, giữ **10kg blend chưa reserved/chưa load** cho một lượt trình bày mới. Hai byproduct lots **BRK-20261118-000075/000078**, mỗi10kg, vẫn là stock mới hợp lệ; không xóa/dispose để che khối lượng. Mass reconciliation: raw40kg = rehearsal load10 + remaining blend10 + broken20, loss0.

## Rehearsal thực tế trên web

Dùng Export và CM ở hai browser profiles khác nhau, login thật bằng tài khoản hiện có. Tạo **EXP-20261118-000025** (`372e14b9-7640-46d4-ad33-6c86b0556dbe`) và **CTN-20261118-000025** (`bf38e257-b045-49a9-8b4b-4946dc7b7415`), target10kg. Plan `1ff10286-a75c-4fd2-9828-c8a81bdcab2a`, proposal `dbe5312b-5825-4542-83de-620d907894ed`.

| Bước | Bằng chứng thực tế |
|---|---|
| Export tạo order qua UI | MarketDEMO-CONTAINER, PADDY,10KG, shipping19–25/11/2026, Moisture max14, một target10KG. Order/container được tạo một lần. |
| Create draft → facility → Calculate plan | PASS, DIRECT_STOCK, đúng BLD07910kg. Moisture13.2≤14; residue0.02≤0.9mg/kg. |
| Save proposal → CM Approve proposal → Approve and reserve stock | FreshnessCURRENT, Approved, reserved/stillheld10, consumed0. |
| Metadata | `DEMO-CONTAINER-20261118-02` / `SEAL-DEMO-CONTAINER-20261118-02` lưu và đọc lại đúng. Guide người dùng chọn suffix03 cho lượt mới. |
| Load4 | Commit disabled trước checkbox; sau xác nhận, loaded4/remaining6, đúng một load. |
| Refresh sau load4 | Vẫn loaded4/remaining6, manifest một load, Close disabled. Không submit load để kiểm idempotency một cách nhân tạo. |
| CM trace partial qua nút UI | Hai source2+2kg,50/50; Flowchart một container và loading edge4kg. |
| Load6 | Loaded10/remaining0, đúng hai loads4/6. CLOSE readiness PASS với whole-manifest assessment. |
| CLOSE qua UI + xác nhận popup | CLOSED, dossierAvailable=true, không còn form loading. Không debit thêm mass. |
| CM dossier | Dossier actual10, hai loads, source contributions5+5kg,50/50. |
| CM Trace container sources → Flowchart | Hai nguồn5+5;7ancestor lots, một container025 CLOSED, cạnh actual10kg. |
| CM Ledger → Traceability, Procurement sources picker → sourceA → Forward | PASS, Actual container targets chỉ container02510kg ở thời điểm rehearsal. Graph5lots trên nhánh sourceA, có thêm nhánh BRK đúng lịch sử milling; container là node có link mở đúng hồ sơ. |
| Kiểm stock bàn giao | BLD079 vẫn10kg physical/available, contribution lịch sử10+10kg của lot20kg. Guide tách rõ historical whole-lot composition và actual container attribution. |

[Bằng chứng web đã lọc](container-demo-v2-web-evidence-2026-11-18.json). Các request HTTP từ web dùng normal restricted runtime; không lấy kết quả trực tiếp từ migration-owner để coi là UI PASS.

Rehearsal bắt đầu với lot20kg và dùng10kg. Buổi trình bày bắt đầu với10kg còn lại; sau approve expected available0 nhưng quota riêng approval10. Preview và approval của buổi trình bày phải tính lại trên current stock, không tái dùng preview/proposal rehearsal đã consume.

## Kiểm tra không ảnh hưởng stock cũ

Sau khi loại **đúng9 UUID lot mới** (hai raw, hai dried, hai head, hai broken, một blend) khỏi tập stock positions, **69 position rows có từ trước** giữ nguyên count/hash `ad13f748bc0a2b76c02c29eaf264f078`. Kiểm lúc `2026-11-18T12:13:32.8719443Z`. Không dùng owner SQL để sửa row stock, refill hoặc reset history.

Hồ sơ request/response phục vụ replay có kiểm soát nằm ngoài Git tại `/home/phucpinku/.local/share/grainledger-backups/container-demo-20261118/`, directory0700/files0600. Không lưu password/token/cookie vào tài liệu hoặc artifacts công khai. Idempotency envelopes được giữ riêng để không tạo lại các bước chuẩn bị khi cần xem kết quả.

## Lỗi hạ tầng và giới hạn của lời cam kết

- Khi mở order mới lần đầu, một GET container list trả500 do Supabase session pool báo `EMAXCONNSESSION`, limit15. **Order tạo đã thành công**; refresh đúng order URL tải lại được list. Không tạo order trùng, không mất transaction loading. Toàn bộ bước approval/loading/CLOSE/tracing phía sau PASS.
- Không thay `.env`, pooler, database connection settings, role/grants hoặc application source để che lỗi hạ tầng này. Guide có preflight đọc stock và trace rehearsal trước buổi trình bày, hướng dẫn giữ hai phiên cần dùng và cách xử lý kết quả đọc thất bại mà không submit mutation mới.
- Sự cố EXECUTE từng bị thu hồi ở lượt triển khai trước vẫn chưa có kết luận nguyên nhân; không suy từ rehearsal PASS rằng một actor/process khác không thể đổi quyền sau đó. Kiểm tra cuối sau khi dừng host test: **migration head, restricted-runtime validation, tracing entry EXECUTE và private-helper denial đều PASS**. Lượt chuẩn bị demo không chạy migration hoặc đổi grants.
- Không chạy lại toàn bộ CT/backend, không test deliberateHOLD/revoke/evidence changes hoặc unload/reopen. Không consume phần10kg để dành nhằm chạy thêm rehearsal. Không thể bảo đảm không lỗi tuyệt đối khi DB/quyền/data dùng chung thay đổi; đã xác nhận luồng nghiệp vụ chính trên web và để phần stock còn khả dụng ở thời điểm bàn giao.
- Đã đăng xuất các tài khoản browser của lượt test, đóng hai tab tự mở, dừng API/Vite do lượt này mở để giải phóng connections. Guide có hai lệnh khởi động thông thường, không kèm migration/seed.

Tài liệu cũ được gắn cảnh báo hết stock và dẫn sang guide mới; không xóa báo cáo/dossier của các buổi trước.
