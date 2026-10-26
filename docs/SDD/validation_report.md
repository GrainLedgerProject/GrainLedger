# Kiểm tra SDD revision 0.3

- PASS — 32 figure captions liên tiếp, 12 table captions liên tiếp.
- PASS — 32 SVG và 32 PNG hợp lệ; 9 class, 19 sequence, 3 package và 1 architecture.
- PASS — 52 bản render Mermaid của 26 nguồn sau lần sửa context, cộng 4 bản render của 2 sequence target bổ sung.
- PASS — Đủ UC-01–UC-38, chín nhóm feature có Design status; nguồn hình trong Markdown và index tồn tại.
- PASS — DOCX ZIP/XML, đủ 32 ảnh nhúng và 12 bảng.
- PASS — Sources.docx, all-in-one.svg, DBDiagram.png và UseCase.png giữ nguyên byte.
- PASS — 258 file source/docs/config của repository không đổi qua SHA-256. Không ghi vào repository.

Repository hash snapshot bỏ qua .git, node_modules, bin/obj, .idea và .env. Không đọc secrets; không chạy ứng dụng/build/test/migration/seed hoặc kết nối database.

Đã xem trực quan architecture và package từ context source; bản HTML cho phép phóng to sơ đồ. DOCX theo style/structure của Sources.docx nhưng chưa được xác minh toàn bộ phân trang trong Word/Google Docs. Không tuyên bố toàn bộ target design đã triển khai hoặc đã kiểm thử phần mềm.

## Package diagrams chuyển sang PlantUML

- PASS — Ba nguồn .puml, render bằng PlantUML 1.2026.8 + Graphviz dot ra SVG/PNG; không có lỗi cú pháp.
- PASS — Ba PNG PlantUML mới có trong DOCX; HTML/Markdown dùng đúng SVG mới và có link nguồn .puml.
- PASS — DOCX vẫn đủ 32 hình và 12 bảng.
- PASS — System Architecture và toàn bộ Class/Sequence source/ảnh giữ nguyên SHA-256; chỉ ba Package Diagram được thay renderer.
- PASS — Architecture.drawio chứa riêng một trang; bản package vẽ tay cũ được archive.
- PASS — 258 file repository và bốn file tham chiếu của người dùng không đổi.

Đã xem trực quan các bản export. Phân trang Word/Google Docs vẫn chưa được kiểm tra toàn bộ.

## Chỉnh lại theo bản trước khi chuyển PlantUML

- PASS — Khôi phục các khối, tên và nhóm chứa theo bản archive trước chuyển đổi; bỏ các nhóm và quan hệ đã thêm.
- PASS — Backend giữ 6 quan hệ, Website không có quan hệ, Field giữ 2 quan hệ; hidden links chỉ dùng cho bố cục.
- PASS — Giữ thứ tự các hàng và màu nhóm của bản cũ; PlantUML tự quyết định kích thước khối và đường đi của mũi tên.
- PASS — Xuất lại SVG/PNG và DOCX/HTML; 32 hình, 12 bảng; ba PNG mới có trong Word.
- PASS — Các sơ đồ khác và bốn file tham chiếu giữ nguyên SHA-256.
