# Đồng bộ điện thoại — giao thức PC ↔ Google Sheet ↔ điện thoại

Tài liệu cho **bản mobile** sau này và cho ai sửa phần đồng bộ. PC (bản C#, `Core/PhoneSync/PhoneSyncService.cs`, `Core/Services/PhoneWebApp.cs`)
giữ **đúng** giao thức của app cũ (Electron `main.js`, `core/sheets-webapp-client.js`) nên app điện thoại hiện có dùng được ngay.

> **Từ 1.58:** hệ thống mới dùng **Google Sheet riêng** + app điện thoại mới `truongnhatdangkhoa-ai/kholanh-csharp-mobile` ("Kho Lạnh (mới)").
> Đường dẫn Web App của Sheet mới cài sẵn ở cả 2 bên (`PhoneWebApp.DefaultUrl` · `www/app-config.js`) — PC chỉ nhập `PC_TOKEN`,
> điện thoại chỉ nhập tài khoản + mật khẩu (`APP_TOKEN` = quản trị). Hệ thống cũ (app PC Electron + `kholanh-anan-mobile` + Sheet cũ) giữ nguyên.

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
| M01VTB Vị Trí Bột | `TonKho_M01_ViTriBot` | `Vị trí, Mã hàng, SL, NSX, HSD, Nhập, Ghi chú, Vị trí mới, _key, Nguồn` (giữ dòng SL = 0; từ 1.177 cột `Nguồn` = `NXT` / `Excel`, ngăn bị tắt công tắc không được đẩy) | `Mã|Vị trí|NSX|HSD#n`, ngăn Excel thêm tiền tố `X:` |
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

## 4. Phần thêm từ bản 1.55 (chép main.js của app cũ)

| Việc | Tab / lệnh | Cách làm |
|---|---|---|
| Báo cáo Excel cho điện thoại tải | `BaoCao_M01` (đẩy cùng M01): `tongton` Báo cáo tổng tồn 7 module, `hlso` Bảng tổng hợp HLSO, `phanloai` JSON Phân loại 8 bảng · `BaoCao_M02` (đẩy cùng M02): `tonghop` Bảng Tổng Hợp, `phanloai` 6 nhóm kho gửi | cột `key, title, name, reportDate, generatedAt, part, parts, data`; mỗi file base64 cắt 40.000 ký tự / dòng, `data` có tiền tố `~`. Meta `m01BaoCaoAt / m01BaoCaoCu / m01BaoCaoLoi` (và `m02…`). Bản C# dựng báo cáo thẳng từ dữ liệu (không cần mở trang module như app cũ) |
| Kết quả kháng sinh "chỉ phần mới" | `pc.ksUpsert { rows: [[key, "", "~" + JSON rút gọn]], dels: [[key, ""]], full }` (đẩy cùng M08, quyền `m10`) | dấu nội dung FNV-1a (`ksHash`) lưu settings `phone_ks_state`; lần đầu gửi cả bảng (đợt 2.000 dòng). Code.gs cũ chưa có lệnh → ghi cả bảng `M08_KhangSinh (key, value)` |
| Yêu cầu lịch xuất / nhập từ điện thoại | `LichXuat_YeuCau` (`id, hanhDong add|move, loai xuat|nhap, targetId, ngay, lsx, trangThaiLich, …, trangThai, xuLyLuc`) | mỗi lần "Đồng bộ ngay": thêm nhanh (nhiều LSX trong 1 yêu cầu = chung cont), lịch nhập, chuyển ngày (+ trạng thái); đẩy lại `LichXuat_Cont` rồi đánh dấu `Đã xử lý` / `Lỗi - …`; lỗi cũ hơn 24 giờ → `Đã xử lý (bỏ lỗi cũ)` |
| Lưu trữ phiếu cũ | `pc.archive { days: 90 }` | tối đa 1 lần / ngày (settings `phone_archive_day`), phiếu đã xong > 90 ngày sang `PhieuXuat_LuuTru` / `PhieuXuatItems_LuuTru` |

Không đẩy `M08_RaDong`, `M08_SizeLabel` (app cũ cũng bỏ từ PC 1.38). Tài khoản / phân quyền điện thoại (`pq.*`) nằm trong Code.gs, không cần PC.

**Tài khoản PC trên điện thoại (PC 1.76):** PC ghi đè tab `PhanQuyen` (cột `PQ_COLS` của Code.gs) bằng `pc.writeTable` — Code.gs tự sao lưu bản cũ (`PhanQuyen_BanLuu_…`). Dòng có `ten` trùng tài khoản PC do PC quyết định: `tokenHash` = sha256 hex mật khẩu PC (PC ghi khi người đó đăng nhập / đổi mật khẩu), `quyen` / `kho` / `trangThai` theo Hệ thống › Tài khoản › Điện thoại, `maTruyCap` rỗng (không lưu mật khẩu dạng chữ), `pcModules` giữ nguyên. Dòng tạo riêng trên điện thoại (tên không trùng tài khoản PC) giữ nguyên. Không đổi gì thì không ghi. Hai dòng đang bật cùng mật khẩu → Code.gs `resolveUser_` chỉ nhận dòng đầu, nên PC cảnh báo.

## Kết quả kháng sinh TÔM NHẬP KHẨU (1.175)

Cùng đợt đẩy kháng sinh (`pc.ksUpsert`, tab kháng sinh của Sheet): thêm các dòng có khóa bắt đầu bằng **`i:nk:`** (bản hiện hành) và **`i:nkh:`** (bản đã bị phiếu "Cập nhật" thay, lịch sử).
Mỗi dòng = **một Size của một phiếu**. Giá trị (cột `value`) là JSON bắt đầu bằng `{` (không có tiền tố `~` như dòng lô `s:batch:`); các dòng khác giữ nguyên.

| Trường | Ý nghĩa |
|---|---|
| `v` | phiên bản định dạng (1) |
| `so` | số phiếu ("19/KS 11") · `d` ngày phân tích (ISO) |
| `c` | nước nhập ("Ấn Độ") |
| `inv` · `invd` | số INVOICE · ngày INV (ISO, có thể rỗng) |
| `ct` · `tp` | Contract ("TP-143/24-MAA") · số TP ("143", rỗng nếu Contract không có TP) |
| `h` · `sz` | hàng ("Tôm HLSO") · Size ("31/35", có thể "60") |
| `n` · `kien` · `dn` | số mẫu · tổng kiện · ngày nhập (ISO) |
| `g` | nhóm theo đặc tính: `[[đặc tính, kết quả, số mẫu, [số thứ tự mẫu…]]]`, vd `[["23.MKS","ĐẠT MKS",28,[1,2,…]],["10.N-AS2-M-H","ĐẠT NHẬT, ASC-A,B, MỸ, HQ",2,[29,30]]]` |
| `x` | **mẫu nhiễm** (có chỉ tiêu phát hiện, không phải ND): `[[số mẫu, kiện, [[chỉ tiêu, giá trị]…], đặc tính, kết quả, ghi chú]]` |
| `lod` | LOD từng chỉ tiêu (`enro cipro oxy doxy sulfo aoz cap`) |
| `old`·`by` | chỉ ở `i:nkh:`: `old=1`, `by` = số phiếu thay thế |

**Tra cứu trên điện thoại (giống PC):** tách câu gõ thành TP (`tp\s*-?\s*(\d+)`), Size (`\d{2,3}/\d{2,3}` hoặc `size N`) và các từ còn lại (INV / Contract / nước / số phiếu, bỏ dấu + bỏ ký tự không phải chữ số; số trần như `143` khớp TP 143). Chỉ xét dòng `i:nk:` (bỏ `i:nkh:` trừ khi người dùng chọn xem lịch sử). Hiển thị mỗi dòng khớp một thẻ: "N mẫu: a mẫu <đặc tính> · b mẫu nhiễm <đặc tính>" (xanh khi `g` chỉ có 1 nhóm và `x` rỗng, vàng khi có `x`), liệt kê `x` (mẫu, chỉ tiêu, giá trị), ngày nhập, tổng kiện, Contract. Không có kết quả: nếu có TP khớp mà khác Size thì báo "TP-143 chỉ có Size …".
