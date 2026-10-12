# Giải thích 77 bảng trong DB diagram GrainLedger

Ngày cập nhật: 12/10/2026.

Các bảng bổ sung thuộc F03, được đặt trong nhóm 05:

| # | Bảng | Lưu gì và để làm gì? |
| --- | --- | --- |
| 75 | `rice_varieties` | Danh mục giống lúa dùng chung: mã và tên giống. Admin quản lý; nhân viên chọn khi đăng ký thửa–vụ. Giá trị giống đã ghi trong thửa–vụ vẫn là text lịch sử. |
| 76 | `measurement_units` | Danh mục đơn vị dùng chung: mã, tên và loại đại lượng. Giúp chọn đúng đơn vị theo trường dữ liệu; không tự thực hiện quy đổi. |
| 77 | `catalog_events` | Nhật ký thay đổi danh mục và bảng MRL: ai sửa, lúc nào, tài nguyên nào và dữ liệu trước/sau. Khi đóng khoảng hiệu lực của bản MRL cũ, event ghi lại thay đổi và ID bản kế tiếp. |

Hai bảng danh mục là nguồn tra cứu, chưa có FK bắt buộc đến các trường text hiện tại. Vì vậy diagram không vẽ đường quan hệ giả đến plot_seasons hoặc các cột unit.

Nguồn đối chiếu: [grainledger.dbml](./grainledger.dbml).

Các số thứ tự giữ theo bản giải thích cũ để dễ đối chiếu. Sau khi đổi phần Identity, vị trí bảng trên dbdiagram.io có thể khác ảnh cũ; đây không còn là hướng dẫn vị trí chính xác. Bốn bảng `identity.*` phản ánh code hiện tại, còn các bảng nghiệp vụ là thiết kế V2.

## Thuật ngữ

- **Lot:** một lô lúa, gạo hoặc phụ phẩm.
- **Conversion:** một công đoạn biến đầu vào thành đầu ra, như sấy, xay hoặc trộn.
- **Reservation:** giữ trước một lượng hàng cho kế hoạch, để kế hoạch khác không dùng lượng đó.
- **Bound:** cận trên dư lượng dùng để đánh giá thận trọng.
- **Assessment:** lần đánh giá của hệ thống.
- **Review:** quyết định duyệt hoặc từ chối của Customer Manager.

## Cột 1 — Ngoài cùng bên trái

| # | Bảng | Lưu gì và để làm gì? |
|---|---|---|
| 1 | `identity.organizations` | **Tổ chức:** một nhà máy (`Mill`) hoặc một công ty khách hàng (`Customer`). Hàng của khách hàng tham chiếu tổ chức loại `Customer`. |
| 2 | `identity.sessions` | **Phiên đăng nhập:** liên kết user, thời hạn và trạng thái thu hồi phiên. Đây là bảng auth đang chạy, không phải vai trò. |
| 3 | `facilities` | **Các cơ sở vận hành:** nhà máy hoặc điểm thu mua, cùng địa chỉ và trạng thái hoạt động. Dùng để xác định nghiệp vụ diễn ra tại cơ sở nào. |
| 4 | `active_substances` | **Danh mục hoạt chất thuốc bảo vệ thực vật.** Giúp khai báo phun thuốc, kết quả xét nghiệm và bảng MRL cùng tham chiếu một hoạt chất, tránh ghi tên không thống nhất. |
| 5 | `commodities` | **Danh mục hàng hóa/sản phẩm.** Dùng để xác định loại hàng của lot, đơn xuất khẩu và bảng giới hạn áp dụng. Công đoạn cụ thể của hàng được lưu thêm ở `lots.stage`. |
| 6 | `markets` | **Danh mục thị trường đích**, như EU hoặc Nhật. Dùng để chọn giới hạn dư lượng phù hợp cho đơn xuất khẩu. |
| 7 | `quality_attributes` | **Danh mục chỉ tiêu chất lượng và đơn vị**, như độ ẩm hoặc tỷ lệ tấm. Là danh mục chung để đối chiếu chất lượng thực tế với yêu cầu của đơn hàng. |

## Cột 2

| # | Bảng | Lưu gì và để làm gì? |
|---|---|---|
| 8 | `identity.users` | **Tài khoản người sử dụng:** định danh từ Google, email, tên hiển thị, một `Role`, tổ chức và trạng thái hoạt động. Dùng cho đăng nhập, phân quyền và xác định người thực hiện thao tác. |
| 9 | `farmers` | **Thông tin nông hộ trong dữ liệu của từng công ty:** mã, tên và liên hệ. Dùng để biết công ty đã thu mua lúa từ ai. Đây là hồ sơ nông hộ, không tự tạo tài khoản đăng nhập cho nông dân. |
| 10 | `locations` | **Vị trí cụ thể trong một cơ sở:** khu tiếp nhận, sấy, silo, xay, đóng bao hoặc cách ly. Dùng để theo dõi hàng đang nằm ở đâu. |
| 11 | `silos` | **Danh sách silo thuộc cơ sở nào.** Đại diện cho silo vật lý; các ngăn chứa cụ thể được lưu tại `silo_compartments`. |
| 12 | `crop_seasons` | **Vụ mùa:** tên vụ, ngày bắt đầu và kết thúc. Dùng để phân biệt lúa thu hoạch ở các vụ khác nhau và hỗ trợ phân nhóm nguồn. |
| 13 | `lots` | **Hồ sơ chung của mọi lô hàng:** mã lot, công ty sở hữu, loại hàng và giai đoạn như lúa tươi, lúa khô, gạo nguyên, tấm hoặc gạo đóng bao. Khối lượng hiện còn ở từng vị trí được lưu tại `stock_positions`. |

## Cột 3

| # | Bảng | Lưu gì và để làm gì? |
|---|---|---|
| 14 | `identity.audit_events` | **Nhật ký thao tác Identity:** ghi thay đổi liên quan đến tài khoản và phân quyền. Bảng này khác với `business_events` của nghiệp vụ. |
| 15 | `balance_policies` | **Phiên bản quy tắc dung sai cân bằng khối lượng cho từng loại công đoạn.** Dùng để quyết định sai lệch đầu vào − đầu ra − hao hụt có nằm trong mức cho phép hay không, đồng thời giữ lại quy tắc đã dùng. |
| 16 | `business_events` | **Nhật ký sự kiện nghiệp vụ:** loại sự kiện, người thực hiện, thời gian, công ty và nội dung. Dùng để giải trình ai đã làm gì; liên kết các thao tác cùng một quy trình bằng `correlation_id`. |
| 17 | `user_facilities` | **Thiết kế phân công người dùng vào cơ sở vận hành.** Ví dụ Mill Operator U01 được gán nhà máy M01. Bảng này chưa có trong migration Identity hiện tại; không được xem là quyền đã triển khai. |
| 18 | `plots` | **Thông tin thửa ruộng:** thuộc nông hộ nào, mã thửa, tọa độ/ranh giới và diện tích. Là điểm nguồn gốc cuối cùng khi truy ngược container về ruộng. |
| 19 | `stock_positions` | **Tồn kho của một lot tại một vị trí:** khối lượng hiện có và lượng đang giữ cho kế hoạch. Ví dụ Lot L01 có 600 kg tại Silo 1 và 400 kg tại Silo 2 thì có hai dòng. Dùng để biết lượng hàng thực sự còn có thể sử dụng. |
| 20 | `equipment` | **Thiết bị và công suất:** máy sấy, dây chuyền xay, đóng bao hoặc cân. Dùng để ghi công đoạn đã chạy trên thiết bị nào và hỗ trợ theo dõi vận hành, hao hụt theo thiết bị. |
| 21 | `silo_compartments` | **Các ngăn chứa của silo:** vị trí và sức chứa. Nếu silo không có ngăn riêng thì dùng một ngăn đại diện. Đây là đơn vị kiểm soát sức chứa và việc chứa hàng của từng công ty. |
| 22 | `safety_margin_policies` | **Phiên bản chính sách biên an toàn theo công ty, thị trường và hàng hóa.** Dùng để đặt ngưỡng đánh giá thận trọng hơn MRL và biết tại thời điểm duyệt đã dùng biên an toàn nào. |
| 23 | `cohort_policies` | **Quy tắc lập nhóm nguồn và lấy mẫu**, có phiên bản và người xác nhận. Dùng để xác định các lot nào được xem là cùng cohort và chính sách lấy mẫu áp dụng cho nhóm đó. |
| 24 | `lot_quality_values` | **Các giá trị chất lượng thực tế của lot:** chỉ tiêu, giá trị, đơn vị, thời gian và bằng chứng. Ví dụ độ ẩm đo được của L01. Dùng để chọn hàng phù hợp và đánh giá kế hoạch theo yêu cầu chất lượng. |
| 25 | `export_orders` | **Đơn xuất khẩu:** công ty, thị trường đích, hàng hóa, khối lượng yêu cầu và khoảng thời gian giao hàng. Là yêu cầu đầu vào cho việc lập kế hoạch và tổ chức container. |
| 26 | `mrl_versions` | **Phiên bản bảng giới hạn dư lượng:** thị trường, hàng hóa, ngày hiệu lực, nguồn và người công bố. Dùng để chọn đúng bảng tại thời điểm quyết định và tái dựng quyết định cũ. |
| 27 | `propagation_policies` | **Quy tắc tính dư lượng qua công đoạn chế biến**, theo sản phẩm, giai đoạn đầu vào/đầu ra và phiên bản. Dùng để tính bound của đầu ra; không tự mặc định rằng sấy hoặc xay làm giảm dư lượng. |

## Cột 4

| # | Bảng | Lưu gì và để làm gì? |
|---|---|---|
| 28 | `compartment_occupancies` | **Lịch sử công ty sử dụng một ngăn silo:** bắt đầu khi nào, đã làm trống khi nào và ai xác nhận. Giúp kiểm soát một ngăn chỉ chứa stock đang hoạt động của một công ty tại một thời điểm. |
| 29 | `plot_seasons` | **Một thửa ruộng trong một vụ mùa**, kèm giống lúa. Ví dụ Thửa P01 – vụ Đông Xuân – giống ST25. Giúp phân biệt lịch sử canh tác và lúa thu mua của cùng thửa qua nhiều vụ. |
| 30 | `procurements` | **Thông tin chung của một lần thu mua:** nông hộ giao hàng, công ty mua, điểm thu mua, người nhận và thời gian. Chi tiết từng phần lúa giao nằm ở `procurement_items`. |
| 31 | `offline_operations` | **Các thao tác gửi từ thiết bị sau khi làm việc offline:** mã thao tác, dữ liệu, thời gian gốc và kết quả đồng bộ. Dùng để chống tạo trùng thu mua/tồn kho và báo các bản ghi bị từ chối hoặc xung đột. |
| 32 | `cohorts` | **Nhóm nguồn cụ thể được tạo theo một policy:** cùng công ty, điểm thu mua, vụ mùa và khoảng thời gian. Dùng làm phạm vi áp dụng bound bảo thủ cho các lot thuộc nhóm. |
| 33 | `order_specifications` | **Phiên bản yêu cầu chất lượng của đơn xuất khẩu.** Giúp biết một kế hoạch được lập theo yêu cầu phiên bản nào, kể cả khi yêu cầu đơn hàng thay đổi sau đó. |
| 34 | `mrl_limits` | **Giới hạn của từng hoạt chất trong một phiên bản bảng MRL:** giá trị và đơn vị. `mrl_versions` là thông tin chung của bảng; bảng này là từng dòng giới hạn cụ thể. |
| 35 | `containers` | **Một container phục vụ một đơn xuất khẩu:** mã nội bộ, số container vật lý, seal, khối lượng mục tiêu và trạng thái. Dùng để lập kế hoạch, ghi loading và xác nhận đóng container. |

## Cột 5

| # | Bảng | Lưu gì và để làm gì? |
|---|---|---|
| 36 | `spray_declarations` | **Thông tin chung của một lần khai báo phun thuốc:** thửa–vụ, ngày phun, người ghi và bằng chứng. Giúp liên kết lịch sử phun thuốc với nguồn lúa thu mua. |
| 37 | `procurement_items` | **Từng dòng của phiếu thu mua:** thửa–vụ nguồn, khối lượng tịnh, số đo/đơn vị gốc và độ ẩm. Một lần giao từ nhiều thửa được tách thành nhiều dòng; mỗi dòng được chấp nhận tạo một source lot. |
| 38 | `cohort_bounds` | **Cận trên dư lượng của từng hoạt chất cho một cohort**, có phiên bản, bằng chứng và danh sách thành viên được bao phủ. Dùng cho lot chưa có xét nghiệm trực tiếp; tránh coi thiếu xét nghiệm là an toàn. |
| 39 | `specification_limits` | **Từng yêu cầu chất lượng cụ thể trong một phiên bản specification:** chỉ tiêu và giới hạn nhỏ nhất/lớn nhất. Ví dụ độ ẩm tối đa hoặc tỷ lệ tấm tối đa. |
| 40 | `blend_plans` | **Kế hoạch phối trộn cho một container:** phiên bản, specification, trạng thái, kết quả tìm kiếm và phiên bản thuật toán. Dùng để quản lý từ đề xuất đến duyệt, thực hiện hoặc mất hiệu lực. |
| 41 | `shipment_documents` | **Các chứng từ được xuất cho container:** loại, phiên bản, file, người xuất và thời gian. Dùng để quản lý tài liệu và xác định chính xác bản chứng từ đã phát hành. |

## Cột 6

| # | Bảng | Lưu gì và để làm gì? |
|---|---|---|
| 42 | `spray_items` | **Chi tiết thuốc hoặc hoạt chất của một lần phun:** tên sản phẩm, hoạt chất, liều lượng và đơn vị. Tách riêng vì một lần phun có thể khai báo nhiều thuốc/hoạt chất. |
| 43 | `source_lots` | **Liên kết lot lúa đầu vào với dòng thu mua đã tạo ra nó.** Từ đây truy được về nông hộ, thửa và vụ. Đây là thông tin nguồn bổ sung cho `lots`, không phải một kho tồn thứ hai. |
| 44 | `conversions` | **Thông tin chung của công đoạn:** sấy, xay, gộp, phối trộn, đóng bao hoặc bút toán bù; cùng thiết bị, hao hụt, người ghi và thời gian. Dùng để ghi lịch sử xử lý và kiểm tra cân bằng đầu vào–đầu ra. |
| 45 | `cohort_members` | **Danh sách source lot thuộc từng cohort.** Dùng để xác định một lot có được áp dụng bound của nhóm đó hay không. |
| 46 | `plan_items` | **Các phần stock mà kế hoạch dự định sử dụng**, kèm số kg từ mỗi vị trí. Ví dụ P1 dự định lấy 600 kg L01 tại Silo 1 và 400 kg L02 tại Silo 2. Đây là nội dung chọn hàng của kế hoạch. |
| 47 | `loadings` | **Thông tin chung của một lần xếp hàng:** container, kế hoạch, người ghi, thời gian và trạng thái. Một container có thể nhận hàng qua nhiều lần loading; chi tiết hàng nhận nằm ở `loading_items`. |

## Cột 7

| # | Bảng | Lưu gì và để làm gì? |
|---|---|---|
| 48 | `conversion_inputs` | **Các lot đầu vào và khối lượng dùng trong một công đoạn.** Ví dụ xay 1.000 kg lúa khô. Dùng để xác định hàng đã bị tiêu thụ và kiểm tra tổng đầu vào. |
| 49 | `samples` | **Mẫu vật lý lấy từ source lot:** mã mẫu, mã niêm phong, người và thời gian lấy mẫu. Dùng để gắn kết quả lab với đúng lô lúa nguồn; MVP mỗi mẫu đại diện một source lot. |
| 50 | `conversion_outputs` | **Các lot đầu ra và khối lượng được tạo bởi công đoạn.** Ví dụ xay tạo gạo nguyên, tấm, cám và trấu. Dùng để ghi tăng tồn kho và giữ liên kết với công đoạn tạo ra hàng. |
| 51 | `state_changes` | **Lịch sử chuyển trạng thái của plan, conversion hoặc container:** trạng thái trước/sau, sự kiện và lý do. Dùng để biết một đối tượng được duyệt, committed, stale hoặc đóng vào thời điểm nào. |
| 52 | `correction_requests` | **Yêu cầu sửa conversion đã ghi:** bản gốc, lý do, người yêu cầu, đối soát và conversion bù/thay thế. Dùng để sửa sổ có kiểm soát, giữ lịch sử và tránh tạo lại lượng hàng không còn tồn tại. |
| 53 | `residue_bounds` | **Cận trên dư lượng của từng hoạt chất trên từng lot**, có phiên bản và căn cứ tính. Đây là dữ liệu đánh giá rủi ro dùng cho planner và assessment; có thể xuất phát từ lab, cohort hoặc công đoạn trước. |
| 54 | `assessments` | **Kết quả đánh giá tại lúc đề xuất, review, loading hoặc đóng container:** PASS/FAIL/INCOMPLETE, MRL, biên an toàn và dữ liệu đã dùng. Dùng để giải thích vì sao hệ thống cho phép hoặc chặn một quyết định. |

## Cột 8

| # | Bảng | Lưu gì và để làm gì? |
|---|---|---|
| 55 | `mass_contributions` | **Khối lượng đầu ra được quy cho từng đầu vào của conversion.** Ví dụ đầu vào A/B chiếm 60%/40%, thì 800 kg đầu ra được phân bổ nguồn 480/320 kg theo quy tắc đã chốt. Đây là dữ liệu lõi để truy vết tỷ lệ sau trộn. |
| 56 | `sample_handovers` | **Lịch sử bàn giao mẫu:** ai/bên nào giao, bên nào nhận, thời gian và tình trạng seal. Dùng để giải trình quá trình quản lý mẫu từ lúc lấy đến lúc chuyển xét nghiệm. |
| 57 | `lab_reports` | **Thông tin chung của báo cáo xét nghiệm:** mẫu, phòng lab, số chứng thư, ngày phát hành, ngày đăng ký và file bằng chứng. Có liên kết báo cáo thay thế để giữ lịch sử sửa chính thức. |
| 58 | `assessment_bounds` | **Các phiên bản bound cụ thể và khối lượng được dùng trong assessment.** Dùng để tái dựng phép đánh giá, thay vì lấy bound mới nhất rồi làm thay đổi kết luận lịch sử. |
| 59 | `plan_findings` | **Các ràng buộc hoặc vấn đề quan sát được khi tìm/đánh giá kế hoạch:** mã vấn đề và giải thích. Dùng để giải thích thiếu stock, thiếu evidence hoặc chưa đáp ứng yêu cầu; không tự chứng minh mọi phương án đều bất khả thi. |
| 60 | `assessment_quality` | **Dữ liệu chất lượng dùng trong assessment**, gồm giá trị được đánh giá, nguồn đo và căn cứ tính. Dùng để giải thích kế hoạch/container có đáp ứng specification hay không. |
| 61 | `plan_reviews` | **Quyết định của Customer Manager:** approve/reject, phạm vi review, assessment, người và thời gian. Tách khỏi assessment để phân biệt kết quả hệ thống tính với quyết định có thẩm quyền của con người. |
| 62 | `plan_output_allocations` | **Phân bổ thành phẩm đã sản xuất cho kế hoạch:** đầu ra nào, tại vị trí nào, được giao bao nhiêu kg cho plan. Giúp nối hàng thực tế sau chế biến với kế hoạch và kiểm soát lượng được loading. |
| 63 | `dossiers` | **Hồ sơ tuân thủ được chốt khi đóng container:** snapshot loading, kế hoạch, evidence, MRL và assessment đóng. Dùng để tái dựng căn cứ quyết định xuất hàng tại thời điểm đó. |

## Cột 9

| # | Bảng | Lưu gì và để làm gì? |
|---|---|---|
| 64 | `lab_results` | **Từng kết quả hoạt chất trong một báo cáo lab:** giá trị, đơn vị, EQ/LT/ND và LOD/LOQ. Dùng để xây dựng bound; “không phát hiện” không tự được hiểu là dư lượng bằng 0. |
| 65 | `finding_lots` | **Các lot liên quan đến một finding.** Ví dụ finding “thiếu evidence” liên quan L01 và L03. Dùng để chỉ rõ người dùng cần kiểm tra những lô nào. |
| 66 | `stock_reservations` | **Lượng hàng giữ cho từng kế hoạch tại từng stock position**, gồm lượng cấp giữ chỗ, đã dùng và đã giải phóng. Có INPUT cho nguyên liệu và OUTPUT cho thành phẩm. Dùng để ngăn các kế hoạch cùng sử dụng một lượng hàng. |
| 67 | `dossier_supplements` | **Bằng chứng bổ sung sau khi dossier đã chốt**, cùng lý do, người và thời gian. Ví dụ lab trả kết quả mới sau khi container đóng. Giúp bổ sung thông tin mà vẫn giữ nguyên hồ sơ quyết định ban đầu. |

## Cột 10

| # | Bảng | Lưu gì và để làm gì? |
|---|---|---|
| 68 | `loading_items` | **Từng phần hàng được xếp trong một lần loading:** lấy từ position nào, reservation nào, bao nhiêu kg và số bao nếu có. Dùng để giảm đúng tồn kho và tính container nhận bao nhiêu từ mỗi lot. |
| 69 | `holds` | **Lệnh giữ một lot hoặc container:** người ra lệnh, thời gian, lý do và kết quả lab liên quan nếu có. Dùng để chặn các thao tác sử dụng/xuất hàng theo phạm vi của lệnh giữ. |
| 70 | `bound_evidence` | **Các nguồn bằng chứng tạo ra một residue bound:** lab result, cohort bound hoặc bound của lot đầu vào. Dùng để trả lời “cận dư lượng này được tính từ đâu?” và tìm các đánh giá bị ảnh hưởng khi evidence thay đổi. |

## Cột 11

| # | Bảng | Lưu gì và để làm gì? |
|---|---|---|
| 71 | `stock_postings` | **Các bút toán tăng/giảm khối lượng tại stock position.** Ví dụ chuyển 100 kg từ Silo 1 sang Silo 2 tạo dòng −100 và +100. Đây là lịch sử để dựng lại tồn kho; `stock_positions` giữ số dư hiện tại. |
| 72 | `hold_actions` | **Các hành động xử lý HOLD của nhà máy:** xác nhận nhận lệnh, cách ly hoặc ghi nhận phản hồi. Dùng để biết nhà máy đã xử lý ra sao; các hành động này không tự gỡ HOLD trong MVP. |
| 73 | `notifications` | **Thông báo HOLD trong ứng dụng:** người nhận, nội dung, thời gian tạo/đọc. Dùng để đưa lệnh giữ đến đúng người có trách nhiệm. Đã đọc thông báo không đồng nghĩa đã xử lý hoặc gỡ HOLD. |

## Cột 12 — Ngoài cùng bên phải

| # | Bảng | Lưu gì và để làm gì? |
|---|---|---|
| 74 | `reservation_events` | **Lịch sử thay đổi lượng giữ chỗ:** RESERVE, CONSUME, RELEASE và REVERSE_CONSUME. Dùng để giải trình số dư reservation và sửa consumption mà không tính trùng. Ví dụ consume 100 kg, đảo 100 kg rồi replacement consume 100 kg thì consumption ròng vẫn là 100 kg. `RELEASE` ở đây chỉ giải phóng giữ chỗ, không gỡ HOLD. |

## Phân biệt các bảng dễ nhầm

| Các bảng | Cách phân biệt |
|---|---|
| `lots` → `source_lots` → `stock_positions` | **Lot là lô nào** → **lô nguồn đến từ đâu** → **lô đang còn bao nhiêu tại mỗi vị trí**. |
| `conversions` → `conversion_inputs` / `conversion_outputs` → `mass_contributions` | **Đã làm công đoạn gì** → **dùng gì, tạo gì** → **mỗi nguồn đóng góp bao nhiêu vào đầu ra**. |
| `plan_items` → `stock_reservations` → `reservation_events` | **Dự định dùng hàng nào** → **đã giữ lượng hàng nào cho plan** → **lượng giữ chỗ đã thay đổi ra sao**. |
| `lab_reports` → `lab_results` → `residue_bounds` → `assessments` → `plan_reviews` | **Báo cáo lab** → **kết quả từng chất** → **cận dư lượng dùng để tính** → **kết luận đánh giá** → **quyết định duyệt của Manager**. |
| `loadings` → `loading_items` → `dossiers` | **Lần xếp hàng** → **chi tiết hàng thực tế đã xếp** → **hồ sơ bằng chứng khi đóng container**. |
