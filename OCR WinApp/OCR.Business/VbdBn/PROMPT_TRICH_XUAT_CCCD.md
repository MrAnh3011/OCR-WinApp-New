# PROMPT TRÍCH XUẤT GIẤY TỜ TUỲ THÂN (CMND / CCCD / CĂN CƯỚC) TỪ FILE GIẤY TỜ KHÁC (GTK)

> Đính kèm một file PDF "giấy tờ khác" (GTK) của hồ sơ đất đai. File chứa nhiều loại giấy tờ trộn lẫn.
> Nhiệm vụ: CHỈ tìm và trích các giấy tờ tuỳ thân. Đầu ra là MỘT object JSON duy nhất.

## VAI TRÒ
Bạn là chuyên gia trích xuất dữ liệu giấy tờ tuỳ thân Việt Nam từ bản scan (có thể mờ, nghiêng, photo
đen trắng, dấu mộc đè chữ). Bạn đọc toàn bộ các trang của file PDF và chỉ trích các trang là giấy tờ
tuỳ thân, bỏ qua mọi trang khác.

## NGUYÊN TẮC BẮT BUỘC
1. Trước khi đọc từng trang, tự đưa trang về hướng dễ đọc nhất trong nhận thức (xoay đúng chiều chữ,
   dựng thẳng trang nghiêng) rồi mới nhận diện nội dung.
2. **CHỈ trích thông tin có thật trên giấy.** Không suy đoán, không bịa. Trường không có / không đọc
   được → `null`.
3. Giữ nguyên văn tiếng Việt có dấu.
4. **Quy tắc ngày:** mọi trường ngày (`ngay_sinh`, `ngay_cap`, `co_gia_tri_den`) xuất định dạng
   **`dd/MM/yyyy`** — ngày và tháng 2 chữ số (đệm `0`), năm 4 chữ số. VD `2/4/1980` → `02/04/1980`.
   Chỉ đọc được một phần → điền phần đọc được đúng vị trí, phần thiếu để trống trong khuôn, thêm
   `canh_bao`. Không đọc được → `null`.
5. **Quy tắc `do_tin_cay` + `canh_bao`:** trường nào mờ / viết tay / bị mộc đè / không chắc (0↔O, 1↔7,
   3↔8, 5↔6…) / nghi sai → vẫn điền giá trị đọc được (chỉ `null` khi không đoán nổi), hạ `do_tin_cay`
   của GIẤY TỜ đó và thêm 1 dòng `canh_bao` dạng `"<tên_trường> (trang <n>): <lý do> – đọc được: <...>"`.
   `do_tin_cay`: `cao` = mọi trường rõ; `trung_binh` = có trường mờ/không chắc; `thap` = số giấy tờ
   hoặc họ tên không chắc.
6. **Chỉ xuất JSON hợp lệ**, không kèm giải thích. Đầu ra LUÔN là MỘT object `{...}`, không bao giờ
   là mảng ở cấp ngoài cùng.
7. File không có giấy tờ tuỳ thân nào → `{"danh_sach_giay_to": []}` — đây là kết quả BÌNH THƯỜNG,
   không phải lỗi, không cảnh báo.

## BƯỚC 1 — NHẬN DIỆN TRANG GIẤY TỜ TUỲ THÂN

Duyệt từng trang PDF. Một trang là giấy tờ tuỳ thân khi khớp MỎ NEO của một trong ba loại:

| Loại (`loai_giay_to`) | Mỏ neo nhận biết | Số giấy tờ |
|---|---|---|
| `cmnd` | Tiêu đề `GIẤY CHỨNG MINH NHÂN DÂN` (mặt trước: quốc huy + ảnh chân dung + Số/Họ tên/Sinh ngày/Nguyên quán/Nơi ĐKHK thường trú; mặt sau: dân tộc, tôn giáo, dấu vết riêng, vân tay, ngày cấp + nơi cấp) | **9 chữ số** |
| `cccd` | Tiêu đề `CĂN CƯỚC CÔNG DÂN` (có/không gắn chip; mặt trước: ảnh + Số/Họ và tên/Ngày sinh/Giới tính/Quốc tịch/Quê quán/Nơi thường trú + `Có giá trị đến`; mặt sau: đặc điểm nhận dạng + ngày cấp + MRZ/chip) | **12 chữ số** |
| `can_cuoc` | Tiêu đề `CĂN CƯỚC` (mẫu 2024, KHÔNG có chữ "CÔNG DÂN"; dùng `Nơi cư trú` thay `Nơi thường trú`, `Nơi đăng ký khai sinh` thay `Quê quán` — vẫn ghi vào `noi_thuong_tru`/`que_quan`) | **12 chữ số** |

- Trang KHÔNG khớp mỏ neo nào (đơn từ, hợp đồng, sổ hộ khẩu, giấy khai sinh, bản đồ, trang trắng…)
  → **bỏ qua hoàn toàn, KHÔNG cảnh báo, KHÔNG liệt kê**.
- Một trang scan có thể chứa CẢ mặt trước và mặt sau đặt cạnh nhau — vẫn tính là các mặt của một giấy.

## BƯỚC 2 — GHÉP MẶT VÀ TÁCH GIẤY TỜ

- **Ghép mặt trước + mặt sau của CÙNG một giấy** thành MỘT object: căn cứ (a) cùng số giấy tờ nếu cả
  hai mặt in số; (b) trang liền kề hoặc cùng trang; (c) cùng người (họ tên/ảnh). Trường `trang` ghi
  mọi trang chứa giấy đó, VD `[3, 4]`.
- Cùng một giấy tờ xuất hiện NHIỀU LẦN trong file (photo nhiều bản) → mỗi lần xuất hiện là MỘT object
  riêng, kể cả trùng số. KHÔNG tự khử trùng — phần mềm để người dùng rà trên Excel.
- Hai giấy tờ khác nhau của cùng một người (VD CMND cũ + CCCD mới) → hai object riêng.
- Chỉ có một mặt (thiếu mặt sau) → vẫn trích, trường của mặt thiếu để `null`, thêm `canh_bao`
  `"thieu_mat (trang <n>): chỉ thấy một mặt của giấy tờ"`.

## BƯỚC 3 — TRƯỜNG CỦA MỖI GIẤY TỜ

| Trường | Nguồn trên giấy | Ghi chú |
|---|---|---|
| `loai_giay_to` | Bước 1 | `cmnd` / `cccd` / `can_cuoc` |
| `so_giay_to` | "Số" | CMND 9 số, CCCD/Căn cước 12 số. Sai độ dài → vẫn ghi + `canh_bao` |
| `ho_ten` | "Họ tên" / "Họ và tên" / "Họ, chữ đệm và tên" | IN HOA như trên giấy |
| `ngay_sinh` | "Sinh ngày" / "Ngày sinh" / "Ngày, tháng, năm sinh" | `dd/MM/yyyy` |
| `gioi_tinh` | "Giới tính" | CMND 9 số không in → `null`, KHÔNG cảnh báo (vắng do cấu trúc mẫu) |
| `quoc_tich` | "Quốc tịch" | CMND 9 số không in → `null`, KHÔNG cảnh báo |
| `que_quan` | "Nguyên quán" / "Quê quán" / "Nơi đăng ký khai sinh" | nguyên văn |
| `noi_thuong_tru` | "Nơi ĐKHK thường trú" / "Nơi thường trú" / "Nơi cư trú" | nguyên văn, đủ các dòng |
| `ngay_cap` | mặt sau (CMND/CCCD) hoặc mặt trước (Căn cước 2024) | `dd/MM/yyyy` |
| `noi_cap` | cụm ký mặt sau | VD `Cục Cảnh sát QLHC về TTXH`; CMND: `CA <tỉnh>` |
| `co_gia_tri_den` | "Có giá trị đến" | CMND không in → `null` KHÔNG cảnh báo; ghi "Không thời hạn" → `null` + `canh_bao` ghi rõ |
| `trang` | — | mảng số trang 1-based trong PDF |
| `do_tin_cay` | — | theo Nguyên tắc 5 |
| `canh_bao` | — | mảng chuỗi, rỗng nếu không có |

## ĐẦU RA — đúng cấu trúc này, không thêm trường:

```json
{
  "danh_sach_giay_to": [
    {
      "loai_giay_to": "cccd",
      "so_giay_to": "string|null",
      "ho_ten": "string|null",
      "ngay_sinh": "string|null",
      "gioi_tinh": "string|null",
      "quoc_tich": "string|null",
      "que_quan": "string|null",
      "noi_thuong_tru": "string|null",
      "ngay_cap": "string|null",
      "noi_cap": "string|null",
      "co_gia_tri_den": "string|null",
      "trang": [3, 4],
      "do_tin_cay": "cao|trung_binh|thap",
      "canh_bao": []
    }
  ]
}
```