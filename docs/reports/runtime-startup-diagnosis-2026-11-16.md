# Kiểm tra lỗi runtime sau khi khởi động máy — 16/11/2026

Nhánh kiểm tra: `feat/container`, HEAD trước xử lý `be34fde`. Khởi động bằng runtime connection đang cấu hình trong `.env`; không đổi connection settings.

## Kết luận

Quyền PostgreSQL được lưu trên database, không tự mất khi khởi động lại máy. Đã xác minh hai vấn đề đồng thời:

1. Database dùng chung đã áp dụng whole-flow residue profiles và profile retirement của dev mới; code container cũ chưa nhận biết đầy đủ function/table được phép truy cập. Validator từ chối quyền EXECUTE của `catalog_retire_residue_rule` và SELECT của `catalog_residue_rule_retirements` vì chúng chưa nằm trong danh sách quyền của nhánh cũ.
2. Runtime role thiếu EXECUTE trên bốn entry point container, dù function và hai migration container đều tồn tại:
   - `grainledger.ledger_container_action(uuid,text,jsonb)`
   - `grainledger.ledger_export_container_read(uuid,uuid,text,integer,integer)`
   - `grainledger.ledger_container_targets(uuid,uuid,uuid[])`
   - `grainledger.ledger_export_container_command(uuid,uuid,text,jsonb,jsonb)`

Role đăng nhập khớp `Ledger:RuntimeRole`, không có quyền superuser/CREATE DATABASE/CREATE ROLE/BYPASSRLS và không sở hữu table/function ứng dụng. Không có pending migration.

Cơ chế cấu hình runtime thu hồi quyền cũ rồi cấp lại theo danh sách của code đang chạy. Chạy cơ chế đó từ nhánh dev chưa có container có thể thu hồi bốn grant container. Kết quả kiểm tra phù hợp với tình huống này; chưa có audit để kết luận ai đã chạy hoặc chạy khi nào.

## Xử lý

- Đồng bộ hai commit từ `origin/dev`: `24d7e53` (whole-flow residue profiles) và `7b8a4a2` (Capture source traceability tests). Merge tự động không có conflict; **chưa tạo merge commit hoặc push**.
- Giữ bộ quyền container và nhận danh sách quyền retirement mới từ dev. Không nới validator, không dùng owner credential để chạy API và không cấp EXECUTE cho helper private.
- Cập nhật fixture kiểm thử container theo whole-flow profile schema 2, basis `AS_RECEIVED`; dùng thời điểm quan sát/review trong quá khứ để tránh ranh giới kiểm tra timestamp PostgreSQL.
- Chạy duy nhất chế độ cấu hình quyền đã được cho phép, không chạy migration/seed:

  ```bash
  ASPNETCORE_ENVIRONMENT=Development DOTNET_ENVIRONMENT=Development \
  dotnet run --project backend/src/GrainLedger.Api --configuration Release \
    --no-build --no-launch-profile -- --configure-runtime-role
  ```

  Lệnh này cần migration-owner connection để cấu hình quyền; không dùng connection đó cho normal startup.

## Kiểm chứng

- Sau cấu hình: `RuntimeValidation=PASS`; không thiếu function/table/EXECUTE, không có EXECUTE thừa hoặc table/sequence grant sai.
- Database vẫn có 10 migration đã áp dụng và không có pending migration.
- Chạy đúng lệnh `dotnet run --project backend/src/GrainLedger.Api`: ứng dụng lắng nghe `http://localhost:5080`; `GET /health` trả HTTP 200, `{"status":"ok"}`. Đã dừng instance kiểm tra để không chiếm cổng khi người dùng chạy lại.
- Backend Release build và toàn bộ **590/590 test PASS**, gồm **8/8 ContainerExecutionTests**, sử dụng PostgreSQL Testcontainers cô lập; không skip. Còn hai warning analyzer trong test nhận từ dev (`xUnit2031`, `xUnit2020`).
- Frontend **238/238 test PASS** (33 files), TypeScript/Vite build PASS; còn warning chunk trên 500 kB.
- Docker image build PASS; **7/7** CI routing tests PASS; liên kết local trong các Markdown thay đổi và `git diff --cached --check` PASS. Đây là kiểm chứng local, chưa tạo/push commit hoặc chạy GitHub Actions cho merge này.
- Không thực hiện smoke test ghi dữ liệu nghiệp vụ trên database chung. Không đổi `.env`; các file local untracked có trước được giữ nguyên và không add vào index.

## Tránh lặp lại

Dùng code migration/runtime configuration đã tích hợp đầy đủ các tính năng đang cài trên database dùng chung. Nếu một thành viên chạy lại cấu hình quyền từ nhánh chưa có container, bốn grant này có thể tiếp tục bị thu hồi; khởi động lại API không sửa được tình trạng đó. Cần đưa phần container và danh sách runtime quyền tương ứng vào nhánh triển khai chung qua quy trình review của team. Không tự động cấp quyền bằng owner khi API khởi động.

Hướng dẫn vận hành: [setup và deployment](../setup-and-deployment.md). Bối cảnh container: [kết quả kiểm chứng 13/11](container-execution-verification-2026-11-13.md).
