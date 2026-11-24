# Đồng bộ điện thoại — giao thức PC ↔ Google Sheet ↔ điện thoại

Tài liệu cho **bản mobile** sau này và cho ai sửa phần đồng bộ. PC (bản C#, `Core/PhoneSync/PhoneSyncService.cs`, `Core/Services/PhoneWebApp.cs`)
giữ **đúng** giao thức của app cũ (Electron `main.js`, `core/sheets-webapp-client.js`) nên app điện thoại hiện có dùng được ngay.

## 1. Vận chuyển

- Kho trung gian: 1 Google Sheet + **Apps Script Web App** (`Code.gs` của app điện thoại), đường dẫn `https://script.google.com/macros/s/…/exec`.
- Mọi lệnh: `POST` thân `text/plain` = JSON `{ "token": "<PC_TOKEN>", "action": "<lệnh>", …tham số }`. Google trả 302 sang
  `script.googleusercontent.com`, client đi theo (GET). Trả JSON `{ "ok": true, … }` hoặc `{ "ok": false, "error": "…" }`.
- Lệnh PC dùng:

| Lệnh | Tham số | Trả |
|---|---|---|
| `pc.ping` | — | `title`, `sheetNames[]` |
| `pc.ensureTabs` | `tabs: { tên: [tiêu đề…] \| null }` | tạo tab còn thiếu |
| `pc.readTable` | `name` | `headers[]`, `rows[][]`, `rowNumbers[]` (số dòng thật) |
| `pc.writeTable` | `name`, `columns[]`, `rows[][]` | GHI ĐÈ cả tab (ghi mới trước, xóa phần thừa sau) |
| `pc.markRows` | `name`, `updates: [{ rowNumber, id, values: { cột: giá trị } }]` | sửa vài ô của các dòng |

- Cấu hình PC: settings `phone_webapp_url`, `phone_pc_token` (mã cất bằng `Secret`), `phone_sync_pick` (bảng đã tick),
  `phone_sync_status` (trạng thái từng bảng), `phone_sync_interval` (phút, 0 = tắt).
- Giới hạn 1 ô Google Sheet 50.000 ký tự → cột JSON (`chiTiet`, `data`) dài quá 40.000 ký tự thì bỏ.

## 2. PC → điện thoại (đẩy tồn, điện thoại chỉ đọc)

Mã bảng (cũng là giá trị cột `module` của phiếu điện thoại): `M01` · `M01VT` · `M01VTB` · `M02` · `M03` · `M08` · `LX` · `C3D`.

| Bảng | Tab | Cột | Khóa dòng `_key` (để phiếu chỉ đúng dòng) |
|---|---|---|---|
| M01 Tồn kho An An | `TonKho_M01` | cột thật của Load Data (trống → `Cột n`, trùng → `Tên (2)`), mã bỏ `TPC-`, + `_key`, `_row` | `id:<Item ID>` nếu có cột Item ID, không thì `r:<thứ tự dòng>` |
| M01VT Tồn theo vị trí | `TonKho_M01_ViTri` | `Mã hàng, Tên hàng, ĐVT, Lô (Batch), Vị trí, SL Tồn, T.Lượng, Đã xuất, Xuất hôm nay, Nhóm đặc tính, _key` | `Mã|Lô|Vị trí#n` (n = thứ tự trùng; dòng không mã bỏ) |
| M01VTB Vị Trí Bột | `TonKho_M01_ViTriBot` | `Vị trí, Mã hàng, SL, NSX, HSD, Nhập, Ghi chú, Vị trí mới, _key` (giữ dòng SL = 0) | `Mã|Vị trí|NSX|HSD#n` |
| M02 Tồn kho gửi | `TonKho_M02` | `Mã hàng, Tên hàng, Đặc tính, Số lô, Size, Quy cách, Ngày nhập kho, Phiếu nhập/Voucher, Số kiện, Trọng Lượng (KG)` | phiếu khớp theo `Mã hàng|Số lô|Size|Phiếu nhập` |
| M03 NXT Bột/Sốt | `TonKho_M03` + `DanhMuc_M03_LSX` | mỗi LÔ còn tồn 1 dòng: `Mã phiếu, Ngày nhập, Vị trí, Loại Hàng (= mã), Mã Hàng (= size), LSX / Hợp đồng, Khách hàng, Trọng lượng (kg) (kg/bao), Kiện/Bao, Tổng (kg), Ghi chú, _key`; LSX: `id, code, name, khachHang` | `encodeURIComponent` của `Vị trí|Mã|Size|Hạn dùng|Ngày nhập` |
| M08 Tổng hợp | `M08_MaHoa` (`seg, code, name`), `M08_MaDaTao` (`stt, code, note`), `M08_TongHop` (cột của bảng tổng hợp), `M08_AnhXa` (`pv, ma, full, short`: dòng `SEG` = loại quy tắc, `CB` = kết hợp, `CFG` = tuỳ chọn) | — | — |
| LX Lịch xuất | `LichXuat_Cont` (từ đầu tháng hiện tại − 2: `id, loai, ngay, trangThai, lsx, khach, thiTruong, matHang, size, tonKien, kg, chiTiet, canXuat, doLuc, cont`), `LichXuat_Thang` (`thang, soLich, daXuat, kg, kgDaXuat, soNhap, daNhap, kgNhap, kgDaNhap, capNhat`) | — | — |
| C3D Container 3D | `C3D_Data` (`kind` = cont / plan / thung, `id, name, capNhat, contName, loaded, requested, data`) | — | — |

Kèm theo khi đẩy bất kỳ bảng tồn nào: **phiếu xuất tạo trên PC** → `PhieuXuat_PC` (`id, maPhieu, ngayTao, trangThai, nguoiTao, ghiChu, xuLyLuc, module,
lsxXuat, nguoiDung, nguon`) + `PhieuXuatItems_PC` (như dòng hàng phiếu điện thoại + `tl`); `trangThai` = `Tạo trên PC` / `Tạo trên PC - Đã hủy trừ tồn`;
mỗi nguồn 200 phiếu mới nhất, tối đa 12.000 dòng hàng.

**Meta** (tab `Meta`, `key | value`, ghi SAU CÙNG): `lastPushAt_<bảng>` (ISO), `lastPushAt` (= mốc sớm nhất khi đủ 8 bảng), `m01FileName`, `m01Rows`,
`m01ImportedAt`, `m01DataDate`, `m01ViTriRows`, `m01ViTriDataDate`, `m01ViTriImportedAt`, `m01ViTriBotRows`, `m01ViTriBotDataDate`, `m01ViTriBotImportedAt`,
`m02Rows`, `m02DataDate`, `m02ImportedAt`, `m03Rows`, `m08Tab2CodeCol`, `m08Tab2DateCol`, `lxRows`, `c3dRows`, `pxPcAt`, `pxPcRows`, `pxPcLoi`.
Điện thoại dựa vào `lastPushAt_<bảng>` để biết phiếu đã xử lý nằm trong số tồn chưa, `…ImportedAt` để ẩn phiếu thuộc đợt tồn cũ.

## 3. Điện thoại → PC (phiếu xuất)

- Điện thoại ghi **dòng hàng trước** (`PhieuXuatItems`: `phieuId, maHang, tenHang, phieuNhap, soLo, size, soKien, soLuongXuat, itemKey, rowIndex, hopDong, viTri`)
  rồi **đầu phiếu** (`PhieuXuat`: `id, maPhieu, ngayTao, trangThai, nguoiTao, ghiChu, xuLyLuc, module, lsxXuat`).
- PC kéo phiếu có `id` và `trangThai` trống / `Chưa xử lý` (không phải `Đã xử lý`, không bắt đầu bằng `Lỗi`), nhóm theo `module` (trống → `M02`), nhập từng module riêng:

| Module | Cách nhập (giống app cũ) |
|---|---|
| M01 | phiếu vào "Xuất Hàng" (sheet `PhieuXuat` 1 ô JSON, id `PX-nnnn`, `moduleId: dienthoai`), trừ ngay `Tồn cuối` dòng Load Data theo `itemKey` (`id:` / `r:`) |
| M01VT | trừ `SL Tồn` theo khóa, phiếu vào `ViTriPhieuDienThoai` (id `VT-nnnn`) |
| M01VTB | trừ `SL` theo khóa, phiếu vào `ViTriBotPhieuDienThoai` (id `VTB-nnnn`) |
| M02 | trừ `Số kiện` + `Trọng Lượng (KG)` theo tỉ lệ, đánh dấu `Đã trừ theo phiếu xuất`, đầu phiếu `nguon: Điện thoại` vào `PhieuXuat` / `PhieuXuatItems` |
| M03 | thêm DÒNG XUẤT vào sổ cái (`nguon: dt`, `phieuId` = id phiếu, LSX = `lsxXuat`); vượt tồn lô / mất lô → không nhận |

- Chống trùng: phiếu đã có (`phoneId` / `id` / `phieuId`) → chỉ đánh dấu, **không trừ lần 2**.
- Nhận xong: `trangThai = Đã xử lý`, `xuLyLuc = <ISO>`. Phiếu không khớp dòng / vượt tồn giữ `Chưa xử lý` (PC báo lỗi). Phiếu **không có dòng hàng quá 10 phút** →
  `Lỗi - không có dòng hàng`.
- "Đồng bộ ngay" = nhận phiếu → đẩy lại bảng đã tick + bảng vừa nhận phiếu (điện thoại thấy tồn mới ngay).

## 4. Chưa làm ở bản C# (app cũ có)

- Tab báo cáo Excel cho điện thoại (`BaoCao_M01`, `BaoCao_M02` — file dựng sẵn cắt base64).
- Kháng sinh "chỉ phần mới" (`M08_KhangSinh`, lệnh `pc.ksUpsert`).
- Hộp thư yêu cầu lịch xuất từ điện thoại (`LichXuat_YeuCau`), lưu trữ phiếu cũ (`pc.archive`), tài khoản / phân quyền điện thoại.
