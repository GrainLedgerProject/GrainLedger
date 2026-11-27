# F14 manual challenge — 27/11/2026

Đã tạo dữ liệu và bốn export order/draft trên DB local. **Chưa gọi Calculate plan, F14 preview, residue preview của plan, propose hoặc approve.** Các kết quả bên dưới là kỳ vọng có căn cứ toán học, không phải kết quả quan sát từ solver.

## Môi trường và phạm vi

- DB: `127.0.0.1:5433/grainledger`; không migrate hoặc ghi DB chung.
- Facility: **GrainLedger Rice Demo Mill (synthetic)**, cùng facility demo đang dùng. Giữ dữ liệu cũ, chỉ thêm nguồn, lô, QC, panel dư lượng và đơn mới.
- Market: **F14 Manual Challenge Market (synthetic)**; commodity của đơn: RICE; giống bắt buộc: ST25; sản phẩm: BLENDED_RICE.
- Dữ liệu mô phỏng, tên giống là giống thật. X/Y và ngưỡng MRL là dữ liệu test, không đại diện quy định pháp lý của một thị trường thực.
- Có 2 vị trí gạo nguyên ST25 bổ sung B/C. Cấu trúc hiện tại: 22 vị trí; cám/trấu tiếp tục dùng hai kho chung, không tự MERGE.

## Bốn đơn để tự Calculate

| Ca | Export order | Khối lượng | Ẩm tối đa | Tấm tối đa | Kỳ vọng độc lập |
| --- | --- | ---: | ---: | ---: | --- |
| MC-01 | `EXP-20261127-000041` | 1500 kg | 13.7% | 4.5% | Có ít nhất một phương án đạt |
| MC-02 | `EXP-20261127-000042` | 1750 kg | 13.75% | 4.5% | Có ít nhất một phương án đạt; sử dụng tồn chặt hơn |
| MC-03 | `EXP-20261127-000043` | 1900 kg | 13.6% | 5% | Không có phương án đạt do độ ẩm |
| MC-04 | `EXP-20261127-000044` | 2100 kg | 14% | 5% | Không có phương án đạt do thiếu tồn đủ điều kiện |

Mỗi đơn đã có một container và draft trống ở version 1. Không có tỷ lệ phối được lưu trước. Các draft:

| Ca | Order ID | Draft ID |
| --- | --- | --- |
| MC-01 | `4e833fcb-7ede-4e57-8fe4-086fcd20fdf1` | `41df23ac-fba9-4e77-9660-85efd0e411fc` |
| MC-02 | `bf25bdfb-885b-44b6-80f2-9096b0903003` | `a162d980-9c50-4d97-912a-9e7ba12efe31` |
| MC-03 | `fb43e788-3086-4e0e-8e78-2f23853daf3b` | `a17012a5-1ff7-4226-8dbf-40803b5bfcb9` |
| MC-04 | `75eb586d-a445-495f-b32f-bba24a6476db` | `16710f75-ea21-47d9-8318-f04a602fcce3` |

## Dữ liệu gạo nguyên

Các giá trị X/Y là cận trên F12 sau sấy và xay xát, tính theo mg/kg as received. MRL pháp lý giả lập: X ≤ 0,100; Y ≤ 0,050. Policy có margin 10%, nên khi đánh giá phương án cần X ≤ 0,090 và Y ≤ 0,045.

| Nhãn | Lot code | Giống | Khả dụng | Ẩm | Tấm | X cận trên | Y cận trên | Vị trí / tình trạng |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | --- |
| A | `HDR-20261127-000512` | ST25 | 540.0 kg | 15.2% | 1% | 0.088000 | 0.011000 | Head Rice Store; Đủ bằng chứng; cần xét đồng thời ẩm/tấm/tồn |
| B | `HDR-20261127-000518` | ST25 | 432.0 kg | 12.0% | 9% | 0.013750 | 0.038500 | Head Rice Store B; Đủ bằng chứng; cần xét đồng thời ẩm/tấm/tồn |
| C | `HDR-20261127-000524` | ST25 | 378.0 kg | 13.4% | 4% | 0.049500 | 0.022000 | Head Rice Store C; Đủ bằng chứng; cần xét đồng thời ẩm/tấm/tồn |
| D | `HDR-20261127-000530` | ST25 | 324.0 kg | 14.7% | 2% | 0.022000 | 0.027500 | Head Rice Store; Đủ bằng chứng; cần xét đồng thời ẩm/tấm/tồn |
| E | `HDR-20261127-000536` | ST25 | 270.0 kg | 12.8% | 7% | 0.066000 | 0.013750 | Head Rice Store B; Đủ bằng chứng; cần xét đồng thời ẩm/tấm/tồn |
| F-MRL-FAIL | `HDR-20261127-000542` | ST25 | 540.0 kg | 11.8% | 0.5% | 0.121000 | 0.008250 | Head Rice Store C; Loại: X vượt MRL; không được pha loãng để né ngưỡng |
| G-NO-LAB | `HDR-20261127-000548` | ST25 | 540.0 kg | 11.9% | 1% | — | — | Head Rice Store; Loại: có sample nhưng chưa có report/selection |
| W-WRONG-VARIETY | `HDR-20261127-000554` | OM5451 | 540.0 kg | 11.7% | 0.8% | 0.013750 | 0.008250 | Head Rice Store; Loại: OM5451, khác giống đơn ST25 |

Chỉ C tự đạt cả ẩm và tấm của MC-01, nhưng C chỉ có 378 kg, không đủ cho đơn. A/D ít tấm nhưng ẩm cao; B/E khô hơn nhưng nhiều tấm. Không thể chỉ xếp lô theo một chỉ số rồi lấy cho đủ kg.

## Các lô tấm đi kèm

Mỗi nguồn được xay xát đầy đủ, gồm head rice, broken rice, bran và husk. Tấm có 98% broken kernels, độ ẩm theo nguồn. Các lô tấm cũng phải qua kiểm MRL từng lô, không chỉ kiểm sau khi phối.

| Nhãn nguồn | Lot tấm | Kg | Ẩm | X cận trên | Y cận trên | Đủ điều kiện giống/lab/MRL đầu vào? |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| A | `BRK-20261127-000513` | 80.0 | 15.2% | 0.140800 | 0.017600 | Không |
| B | `BRK-20261127-000519` | 64.0 | 12.0% | 0.022000 | 0.061600 | Không |
| C | `BRK-20261127-000525` | 56.0 | 13.4% | 0.079200 | 0.035200 | Có |
| D | `BRK-20261127-000531` | 48.0 | 14.7% | 0.035200 | 0.044000 | Có |
| E | `BRK-20261127-000537` | 40.0 | 12.8% | 0.105600 | 0.022000 | Không |
| F-MRL-FAIL | `BRK-20261127-000543` | 80.0 | 11.8% | 0.193600 | 0.013200 | Không |
| G-NO-LAB | `BRK-20261127-000549` | 80.0 | 11.9% | — | — | Không |
| W-WRONG-VARIETY | `BRK-20261127-000555` | 80.0 | 11.7% | 0.022000 | 0.013200 | Không |

**Tồn đủ điều kiện trước khi xét QC phối:** 1.944 kg head rice A–E + 56 kg tấm C + 48 kg tấm D = **2.048 kg**. Cám, trấu và lúa không nằm trong input products được duyệt của plan này.

Các lô ST25 của bộ demo cũ vẫn giữ nguyên tồn nhưng không có đầy đủ panel X/Y cho market mới, nên không đủ bằng chứng tham gia bộ đơn này. Không dùng tên kho, lot code hoặc ghi chú để thay cho kiểm giống/bằng chứng.

## Nguồn dữ liệu, lab và khối lượng

Đã thêm 8 farmer/plot/plot-season và 8 procurement/source/sample, nhận hàng qua API. Mỗi nguồn có raw mass lần lượt A=1.000, B=800, C=700, D=600, E=500, F=1.000, G=1.000, W=1.000 kg. W là OM5451; các nguồn khác ST25.

Sấy: 90% raw mass thành dried paddy, 10% hao hụt. Xay xát: head 54%, tấm 8%, cám 8%, trấu 18%, hao hụt 2% tính trên raw mass; tổng bằng 90% raw mass đầu vào xay xát. Các con số là giả định mô phỏng bảo toàn khối lượng, không phải hiệu suất được xác nhận của nhà máy thực.

Mỗi nguồn trừ G có report chứa hai dòng X/Y; đã xác nhận sample/seal và lưu reviewed selection với absolute uncertainty rõ ràng. G cố ý chỉ có sample, chưa có lab. Đã tạo 32 QC observations: 2 chỉ tiêu × 2 dòng gạo × 8 nguồn.

Profile test: concentration factor sấy 1,1; head 0,5; tấm 0,8; cám 1,5; trấu 0,2; homogeneous blend 1. Vì vậy head upper = raw upper × 0,55; broken upper = raw upper × 0,88. Yield phối là 1. Đây là giả định profile của bộ test, không phải hệ số khoa học mặc định cho mọi giống hoặc mọi điều kiện.

## Chứng cứ kỳ vọng, không chạy F14

Các chứng cứ dưới đây dùng số thập phân và số liệu F12 thực đọc từ DB, không gọi `BlendMilpSolver`, preview hay API Calculate. Không lưu allocation vào draft. Nếu thuật toán tìm tỷ lệ khác mà vẫn đạt mọi điều kiện thì vẫn đúng.

<details><summary>Mở ví dụ khả thi để đối chiếu sau khi tự chạy</summary>

| Ca | A/B/C/D/E (kg) | Ẩm tính tay | Tấm tính tay | X upper | Y upper |
| --- | --- | ---: | ---: | ---: | ---: |
| MC-01 | 288/306/378/306/222 | 13.6364% | 4.48% | 0.046431 | 0.023155 |
| MC-02 | 432/414/378/306/220 | 13.66502857142857142857142857% | 4.469714285714285714285714286% | 0.04781228571428571428571428571 | 0.02311257142857142857142857143 |

Công thức với yield=1 và homogeneous factor=1: chỉ tiêu phối = Σ(kg × chỉ tiêu lô) / tổng kg. X/Y dùng cận trên sau F12. Tổng kg đúng target, mỗi lượng lấy không vượt tồn.

</details>

**MC-03:** Bỏ qua giới hạn tấm và dư lượng để có cận dưới thuận lợi nhất cho độ ẩm. Lấy hết nguồn khô nhất: B 432, E 270, C+tấm C 434, D+tấm D 372, sau đó A 392 kg. Độ ẩm nhỏ nhất vẫn là **13,622315789…% > 13,6%**. Thêm giới hạn tấm không thể làm tình hình tốt hơn. Vì vậy không có phương án PASS.

**MC-04:** 2.048 kg input đủ điều kiện × yield 1 < 2.100 kg target, thiếu ít nhất 52 kg ngay trước khi xét QC. Không được bù bằng F quá MRL, G chưa lab, W khác giống hoặc lô cũ thiếu panel.

**Khi chọn Fewest lots:** MC-01 cần ít nhất 4 lô vì ba lô lớn nhất chỉ có 1.350 kg; có chứng cứ 4 lô A=540, B=312, C=378, E=270 cho ẩm 13,6488%, tấm 4,5%. MC-02 cần ít nhất 5 lô vì bốn vị trí lô lớn nhất chỉ có 1.674 kg; ví dụ 5 lô ở trên chứng minh đạt được. Đây là kỳ vọng tối ưu theo số lô, không buộc tỷ lệ duy nhất.

## Tự chạy trên FE

1. Mở Docker/DB local, chạy BE/FE như bình thường. Đăng nhập **export@grainledger.local** bằng mật khẩu local đang dùng.
2. Vào **Export orders**, tìm đúng một trong các mã 000041–000044 ở bảng trên.
3. Mở container rồi mở draft v1 có **Product to ship = Blended rice**, **Required rice variety = ST25**.
4. Chọn **GrainLedger Rice Demo Mill (synthetic)**. Có thể bật Show excluded để xem thêm lô bị loại. Chọn ưu tiên tùy ý; với Lowest moisture chọn đúng attribute Moisture - variety test.
5. Tự bấm **Calculate plan**. Kiểm tổng kg, giống của từng allocation, ẩm/tấm, từng dư lượng và các input exclusions. So kết quả với kỳ vọng; không bắt buộc trùng tỷ lệ minh họa.
6. Chưa cần Save proposal hoặc Approve nếu chỉ đang kiểm thuật toán.

## Kiểm tra đã thực hiện

- Đọc lại 32 output lots mới: tồn và giống đúng; 16 lô head/broken có bounds đúng theo lab/profile; G thiếu bounds đúng chủ đích.
- Tồn đủ điều kiện được đối soát thành 2.048 kg. Đã kiểm các lô ST25 cũ không có đủ panel X/Y.
- Phân bố head ST25: A/D/G ở kho head chính; B/E ở kho B; C/F ở kho C. W ở kho head OM5451. Các kho tấm theo giống, cám/trấu ở hai kho chung.
- Mỗi order mới có đúng draft DRAFT, **0 preview và 0 proposal**. Tổng preview toàn DB vẫn 18 như trước khi thêm bộ dữ liệu. Không reserve hoặc tiêu thụ tồn do tính phương án.
- Không sửa code thuật toán, không reset dữ liệu cũ, không chạy test/benchmark solver trên các scenario này.


## Extension to other varieties

The additive [OM5451/Jasmine 85 challenge](f14-multivariety-20261127.md) now adds orders `EXP-20261127-000045`–`000048` and complete X/Y source evidence for those varieties. The ST25 expectations above remain unchanged because different-variety stock is excluded. Facility total is now 24 locations. Earlier ST25 drafts may have user-generated previews; only the four newly added orders were verified uncalculated during the extension.


## Whole-kg planning update — 27/11/2026

F14 now selects integer kg per lot/location, minimum 1 kg, by integer solver variables rather than display rounding. The independent feasible witnesses in this document already use whole kg, so those feasibility expectations remain valid. Continuous infeasibility proofs also exclude integer solutions. Local store labels no longer have the suffix `(challenge)`; their UUIDs and stock are unchanged. No user demo order was recalculated for this update. See [the whole-kg planning contract and verification](../api/f14-whole-kg-planning.md).
