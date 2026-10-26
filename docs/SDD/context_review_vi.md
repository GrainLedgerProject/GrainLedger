# Đối chiếu context bổ sung trong thư mục SDD

Đã đọc `all-in-one.svg`, xem `UseCase.png` và kiểm tra `DBDiagram.png` ở mức tổng thể cùng các vùng chi tiết về stock, conversion, sample, lab, reservation và assessment. Ba file gốc được giữ nguyên.

- **ERD:** trích xuất trực tiếp được 74 tên entity từ SVG; tập tên khớp đủ 74 entity trong sáu file ERD chi tiết ở `review/erd_v2_1`. Các trường quan trọng xác nhận gồm signed measurement variance, correction compensation/replacement, reverse reservation consumption, snapshot thành viên cohort và assessment CLOSE theo container.
- **Use Case:** hình chứa UC-01–UC-38 và sáu vai trò nội bộ; hai actor trừu tượng không được biến thành vai trò mới. Mô hình thiết kế giữ phân biệt người mua hàng/thu mua, người xuất khẩu, manager phía khách và nhân sự nhà máy.
- **DB diagram:** dùng tên bảng vật lý và có thêm khóa company ghép, projection, JSONB/snapshot/hash. Bản SDD bổ sung bảng ánh xạ logical class → physical representation; không yêu cầu tên lớp phải trùng tên bảng.
- **Số học:** ảnh DB ghi `NUMERIC(20,6)` cho khối lượng kg và `NUMERIC(24,12)` ở các trường kết quả/đo lường. SDD ghi nhận đây là lựa chọn physical schema hiện có; quy tắc nhập, làm tròn và đổi đơn vị vẫn phải rõ ràng.
- **Snapshot:** `offline_operations.payload/result_payload`, assessment `basis_snapshot/basis_hash` và dossier `snapshot/snapshot_hash` cho phép lưu bằng chứng nguyên tử trong cơ sở dữ liệu. Hình storage trong architecture là năng lực logic, không bắt buộc dựng thêm object storage hoặc microservice.

Hai điểm cần xác minh trước khi triển khai adapter:

1. ERD logical ghi `Sample.paddy_lot_id`, còn DB diagram ghi `samples.source_lot_id`. Cần xem DDL/FK chính xác để xác định target. Không suy từ tên `source_lot_id` rằng đã hỗ trợ lấy mẫu cho mọi lot thành phẩm.
2. Logical safety margin có `unit` cho ABSOLUTE; ảnh physical margin table chưa thấy trường unit riêng. Cần chốt vị trí lưu unit trong bản ghi/basis để so sánh đúng nồng độ. Bản thiết kế không tự giả định giá trị hay đơn vị còn thiếu.

Ảnh DB không thay thế DDL để kiểm chứng CHECK, index, transaction isolation hoặc trigger. Bản SDD ghi hợp đồng phải được cưỡng chế; chưa tuyên bố cơ sở dữ liệu đã thực thi các hợp đồng đó.

Điểm lệch proposal về loss/signed variance đã ghi trong SDD mục 4.2. Theo context ERD và use case mới, bản thiết kế chi tiết tiếp tục giữ signed variance có xác nhận của Mill Manager.
