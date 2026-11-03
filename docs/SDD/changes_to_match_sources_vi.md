# Những chỗ đã sửa theo mẫu Sources.docx

Đã đọc cấu trúc và trích ảnh trong phần IV. Software Design Description để đối chiếu architecture, backend/frontend package, class diagram và sequence diagram. Mốc trang Word có thể đổi theo trình đọc; việc trích nội dung bám heading của phần SDD trong file.

1. **Cấu trúc:** phần chính bắt đầu bằng `IV. Software Design Description`; giữ thứ tự 1. System Design, 2. Database Design, 3. Detailed Design. Chuyển các bảng nguồn, quyết định và review/traceability dài sang file riêng.
2. **System Architecture:** vẽ lại dạng phân lớp Frontend / API / Application / Domain / Infrastructure / Persistence, có Build and Deployment và external information. Sau hình có caption và Component Explanations thành các đoạn như mẫu. Vendor, framework và API của HomeCycle không bị đưa vào GrainLedger.
3. **Package Diagram:** vẽ ký hiệu UML có tab thư mục và dependency, thay cho flowchart. Thứ tự Backend → Website → Field Web Client. Backend thể hiện package con Controllers, Middlewares, DTOs, Interfaces, Services, Validations, Entities, Policies, Repositories, Persistence...; tất cả có bảng No / Package / Description.
4. **Class Diagram:** bỏ kiểu hình trộn service/policy với vài entity rời rạc. Tập trung vào lớp nghiệp vụ của từng chức năng, gồm kiểu thuộc tính, phương thức và quan hệ có multiplicity. Đối chiếu tên/trường với ERD GrainLedger. Phương thức là hợp đồng thiết kế đề xuất, không tuyên bố đã có mã nguồn.
5. **Sequence Diagram:** bổ sung UI, Controller, Application Service, Repository, Database và collaborator liên quan; đánh số thông điệp, dùng activation và alt/opt/loop. Các luồng lõi được viết lại ở mức lời gọi phương thức để thể hiện ranh giới transaction và nhánh từ chối rõ ràng.
6. **Heading/caption:** mỗi nhóm có `3.x.1 Class Diagram`, rồi `3.x.2 <Sequence name>`, `3.x.3 <Sequence name>`...; không gộp tất cả vào một mục Sequence Diagrams nữa. Có caption Diagram 1–29 và Table 1–12. Không lặp số subsection như một số chỗ trong mẫu.
7. **DOCX:** đọc style heading/caption và font mặc định từ Sources.docx, dùng A4 dọc, lề tương ứng mẫu. Chèn hình trực tiếp theo mạch nội dung thay vì ép mỗi sơ đồ sang một section/trang riêng.
8. **DB:** chỉ dẫn chiếu hai hình có sẵn, theo yêu cầu trước đó không vẽ lại DB.

Bộ tài liệu vẫn giữ các quy tắc đã chốt: exact decimal; signed variance có xác nhận; reservation đúng plan; compensation + replacement nguyên tử; thiếu evidence là INCOMPLETE; review/loading/CLOSE dùng bằng chứng hiện hành; CLOSE toàn container; HOLD không tự gỡ hoặc tự phát từ kết quả lab.

Có 29 sơ đồ, gồm 1 architecture, 3 package, 9 class và 16 sequence; bảng đối chiếu riêng giữ đủ 38 use case. Class Specifications vẫn có trong phần Detailed Design vì đây là yêu cầu ban đầu của bạn, dù hình minh họa trong Sources.docx chủ yếu trình bày các diagram.

## Bổ sung revision 0.3 theo repository

- Vẫn giữ cấu trúc/typography/caption theo Sources.docx, nhưng thay tên kỹ thuật giả định bằng host và các module thực tế.
- API hiện hữu là Minimal API endpoints; LedgerService gọi protected SQL qua Npgsql. Identity/Catalogs/Capture dùng các DbContext và feature handler hiện có.
- Giữ đầy đủ target design cho phần chưa hoàn thành, có Design status ở cả chín nhóm; không suy ra thiếu code là bỏ scope.
- Các CLR class có sẵn dùng đúng tên và property. Logical SQL model và planned target class được phân biệt rõ.
- Tách source registration và physical arrival; thêm sequence riêng để mô tả actual measurement / manager shortage review / remeasurement.
- Tổng hiện tại 30 diagram và 12 bảng, vẫn phủ UC-01–UC-38. Bốn file tham chiếu của người dùng và repository được giữ nguyên.
