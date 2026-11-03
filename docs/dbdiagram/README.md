# Sơ đồ PostgreSQL hiện hành

Bộ sơ đồ phản ánh catalog PostgreSQL live được chụp ngày **19/10/2026**: **61 bảng** (60 bảng nghiệp vụ/auth và `ledger.__EFMigrationsHistory`), 424 cột, 120 khóa ngoại. Đây là tài liệu đối chiếu schema đã triển khai, không phải thiết kế tương lai hay nguồn tạo migration.

## Chọn sơ đồ

| Phạm vi | File |
| --- | --- |
| Phụ thuộc FK vật lý giữa bốn schema | [00-overview.mmd](./00-overview.mmd) |
| Identity, cơ sở, vị trí, thiết bị, phân công | [01-platform-and-tenant-administration.mmd](./01-platform-and-tenant-administration.mmd) |
| Nông hộ, thửa-vụ, thu mua, khai báo phun, offline | [02-offline-field-capture.mmd](./02-offline-field-capture.mmd) |
| Lot, mã lot, operation, tồn kho, posting, contribution | [03-mass-balance-custody-ledger-and-processing.mmd](./03-mass-balance-custody-ledger-and-processing.mmd) |
| Sample và bàn giao mẫu | [04-samples-and-handovers.mmd](./04-samples-and-handovers.mmd) |
| Danh mục và MRL | [05-catalog-and-mrl.mmd](./05-catalog-and-mrl.mmd) |
| Receiving, đo lường, review, threshold | [06-receiving-and-review.mmd](./06-receiving-and-review.mmd) |
| HOLD và corrections | [07-hold-and-corrections.mmd](./07-hold-and-corrections.mmd) |
| Business events, ledger events, audit history | [08-traceback-and-event-history.mmd](./08-traceback-and-event-history.mmd) |
| Giải thích đủ 61 bảng | [TABLE_EXPLANATIONS.md](./TABLE_EXPLANATIONS.md) |
| Kiểu, NULL, constraints, indexes | [DATA_DICTIONARY.md](./DATA_DICTIONARY.md) |
| DBML tổng hợp | [grainledger.dbml](./grainledger.dbml) |

## Ranh giới và quy ước

- Snapshot catalog PostgreSQL là nguồn chính xác; migration chưa áp dụng không làm phát sinh bảng/cột trong các hình này. `ledger.__EFMigrationsHistory` được giữ vì là bảng có thật trong schema.
- Mỗi bảng có đủ cột trong đúng sơ đồ sở hữu. Entity tham chiếu ở sơ đồ khác chỉ có khóa cần thiết để đọc quan hệ; comment chỉ rõ schema và nơi có đủ cột. Tên entity gồm schema để phân biệt bảng cùng tên.
- Đường nối chỉ là FK vật lý trong catalog, gắn nhãn tên constraint thật. Cardinality dựa trên FK nullability và PK/unique constraint hoặc unique index không partial của bảng con. Không vẽ quan hệ workflow suy diễn, cũng không tạo FK giả cho JSON hoặc ID actor chỉ được kiểm tra lúc chạy.
- `NN` = NOT NULL; `NULL` = nullable; `PK` và `FK` = thành viên khóa thực. Mermaid dùng `varchar(n)` thay cho PostgreSQL `character varying(n)` và `timestamptz` thay cho `timestamp with time zone`; các tên rút gọn này có chú thích trong sơ đồ. Kiểu còn lại giữ nguyên.
- Mermaid thể hiện cột, PK/FK và quan hệ. CHECK, UNIQUE ghép, index, default và sequence cần xem [DATA_DICTIONARY.md](./DATA_DICTIONARY.md) hoặc [grainledger.dbml](./grainledger.dbml); không suy ra chúng từ đường ER.
- Physical FK không đồng nghĩa với authorization hoặc business validation. FK chỉ xác nhận tham chiếu; scope tenant, quyền actor, tính hợp lệ của JSON và quy tắc nhiều dòng có thể cần kiểm tra runtime riêng. Không có xác nhận runtime nào được vẽ thành FK.

Sơ đồ `00` là flowchart tổng hợp phụ thuộc giữa schema; các sơ đồ `01`–`08` là ERD chi tiết. DBML chỉ dùng trong DBML editor, không dán vào Mermaid editor.
