# GrainLedger SDD — revision 0.3

Thiết kế đầy đủ theo yêu cầu GrainLedger, trình bày theo phần SDD trong `Sources.docx`, bổ sung context kỹ thuật từ repository `/home/phucpinku/Desktop/Git/GrainLedger` bằng thao tác chỉ đọc. Các luồng chưa hoàn thành vẫn có thiết kế và được ghi planned.

- `GrainLedger_SDD.docx`: bản tiếng Anh chính, dùng Word hoặc import Google Docs.
- `GrainLedger_SDD.html`: bản xem độc lập, bấm vào ảnh để phóng to.
- `GrainLedger_SDD.md`: nội dung nguồn, heading/caption tương ứng DOCX.
- `GrainLedger_Architecture.drawio`: nguồn chỉnh sửa riêng của System Architecture.
- `diagrams/`: chứa các file nguồn chỉnh sửa gồm 28 file Mermaid (`.mmd`) và 3 file PlantUML (`.puml`: `02_packages_field.puml`, `03_packages_workspace.puml`, `04_packages_core.puml`).
- `diagrams/rendered/`: chứa **32 SVG** được lưu trong Git để link ảnh ở bản Markdown hoạt động; PNG có thể render cục bộ và không được lưu trong Git. Gồm 1 architecture, 3 package, 9 class và 19 sequence. Xem hướng dẫn render tại `docs/diagram-rendering.md`.
- `repository_context_vi.md`: căn cứ code/docs đã đọc, mapping tên, các phần existing/planned và khác biệt thu mua/nhận thực tế.
- `design_basis_and_traceability.md`: căn cứ thiết kế và đối chiếu đủ UC-01–UC-38; các ghi chú revision cũ được giữ làm lịch sử.
- `validation_report.md`: kiểm tra đầu ra và đối chiếu repository không đổi.

Phần chính giữ thứ tự System Architecture + Component Explanations → Backend/Website/Field Web Client Package Diagrams + No/Package/Description → Database Design dẫn chiếu → Detailed Design. Mỗi nhóm có Class Diagram, Class Specifications và từng sequence đánh số riêng. Có 32 caption Diagram và 12 caption Table.

Backend thực tế là ASP.NET Core 10 modular monolith với GrainLedger.Api và bốn module Identity, Catalogs, Capture, Ledger. Frontend là React/TypeScript/Vite. SDD không gán một cây Controller/Service/Repository chung cho mã hiện tại. Capture/Identity/Catalogs dùng endpoint handlers và DbContext; Ledger dùng LedgerService/Npgsql/protected SQL. Module/capability tương lai được ghi rõ, không giả nhận project đã tồn tại.

Procurement source registration được tách khỏi physical arrival acceptance. Đăng ký APPLIED/PENDING_ARRIVAL chưa tạo usable stock; receiving exact match hoặc shortage approval đủ điều kiện mới post actuals. Thiết kế tương lai cho lab/cohort/residue, export plan/reservations, loading/CLOSE/dossier, full container traceback và offline vẫn giữ đủ phạm vi.

Không vẽ lại DB diagram; giữ nguyên Sources.docx, all-in-one.svg, DBDiagram.png, UseCase.png và repository. Không chạy ứng dụng/build/test/migration/seed hoặc gọi database. Không đọc secrets. Bản trước được lưu trong `review/archive/SDD_before_repository_context_2026-10-16`; revision trước Sources nằm trong archive tương ứng.

## Chỉnh sửa và xuất lại

Sửa Markdown/nguồn hình rồi dùng `tools/export_sdd.py` để xuất DOCX/HTML từ nội dung hiện tại; cần python-docx, Markdown, Pillow. Script đọc style từ Sources.docx, không sao chép nội dung mẫu. Mermaid CLI dùng cấu hình `tools/mermaid-config.json` để render `.mmd`; SVG native cần CairoSVG khi sinh PNG.

Các script dựng revision 0.2 là tài nguyên lịch sử, không chạy lại nếu muốn giữ bản hiện tại. `align_repository.py`, `repository_sequences.py`, `layout_repository.py` ghi lại phần dựng context mới; một số hiệu chỉnh cuối nằm trực tiếp trong Markdown/Mermaid, nên cần giữ khi tái dựng. DOCX dùng A4 dọc và hình/caption nối theo nội dung mẫu; chưa xác minh toàn bộ phân trang bằng Word/Google Docs.

## Package Diagram bằng PlantUML

Ba file `.puml` là nguồn chính thức cho package diagrams, được render bằng PlantUML 1.2026.8 với Graphviz dot. Đã cập nhật ảnh trong DOCX/HTML. Các trang package vẽ tay trước đó được lưu ở `review/archive/SDD_before_PlantUML_packages_2026-10-16`; dùng bản này để đối chiếu cấu trúc trước chuyển đổi. Bản PlantUML đã được chỉnh lại để giữ nguyên tên khối, nhóm chứa và quan hệ của bản cũ (Backend: 6 quan hệ; Website: 0; Field: 2). Các đường hidden chỉ điều khiển bố cục, không phải quan hệ mới.

Xuất lại (cần Java, PlantUML JAR và Graphviz):

```bash
# Xuất SVG/PNG vào diagrams/rendered/
python tools/render_packages_plantuml.py
# Hoặc chỉ định jar/java/dot nếu cần:
# python tools/render_packages_plantuml.py --jar /path/to/plantuml.jar
```

Nếu Java hoặc dot không nằm trên PATH, truyền thêm `--java /path/to/java --dot /path/to/dot`. Sau khi render, chạy `tools/export_sdd.py` để đưa hình mới vào DOCX/HTML. Các script layout native cũ thuộc lịch sử; không chạy để ghi đè package diagrams hiện tại.
