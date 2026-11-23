# Kho Lạnh C# — hướng dẫn cho các phiên làm việc

App quản lý kho lạnh **viết lại bằng C#** (.NET 8, Avalonia, SQLite) từ app cũ Electron/JS `truongnhatdangkhoa-ai/kholanh-anan-pc`
(nếu có, clone ở `/home/user/kholanh-anan-pc`; mã giao diện cũ ở `app/html/js/`, lõi ở `app/core/`). Giao diện, chú thích, thông báo: **tiếng Việt**.

## Cấu trúc & quy ước
- `src/KhoLanh.Core/<Module>/` — lõi (hàm thuần + Service). Không phụ thuộc giao diện. Test ở `tests/KhoLanh.Core.Tests/`.
- `src/KhoLanh.App/Views/<Module>View.cs` — giao diện viết bằng code (không axaml), mẫu: `PxhtView.cs`, `TongQuanView.cs`, `ListSxView.cs`.
- Dữ liệu module: bảng `module_sheets` (db_key + sheet = mảng dòng JSON) **dùng chung tên database / sheet với app cũ** (vd `nxt-bot-sot` / `SoCai`).
  Đọc / ghi qua `Func<ISheetSource>` (= `LanService`, tự xử lý máy đơn / LAN / Drive). Ô JSON một dòng: `[{ "data": "__JSON__{...}" }]`.
- Đăng ký service ở `src/KhoLanh.Core/AppServices.cs`; menu ở `src/KhoLanh.App/MainWindow.axaml.cs` (`NavItem` + nhóm); quyền ở `src/KhoLanh.Core/Models/Permissions.cs`
  (`Perm.Items`; mục quyền mới mà tài khoản phân quyền riêng từ trước chưa có → thêm vào `Perm.MissingDefault` nếu cần bật sẵn).
- Mỗi lần xong một phần: `dotnet test tests/KhoLanh.Core.Tests -c Release` phải đạt hết; build `src/KhoLanh.App`; chụp màn hình bằng
  `dotnet run -c Release --project tools/Screenshots -- <thư mục>` (thêm cảnh chụp cho module mới ở cuối `tools/Screenshots/Program.cs`), chép ảnh cần thiết vào `docs/anh/`.
- Nâng `<Version>` trong `Directory.Build.props` mỗi bản; cập nhật bảng module trong `README.md`.
- Phát hành: code vào `main` (qua PR) → workflow `.github/workflows/build.yml` tự đóng gói `KhoLanh.exe` và tạo Release `v<Version>`; app các máy tự cập nhật.
- Môi trường cloud chưa có .NET: `apt-get install -y dotnet-sdk-8.0`.

## KẾ HOẠCH ĐANG LÀM (yêu cầu 23/11/2026)
Làm lần lượt; **xong mỗi module: ghi báo cáo vào mục "Nhật ký tiến độ" bên dưới + đổi trạng thái ở bảng này + commit + push**.

| # | Module | Trạng thái |
|---|---|---|
| 1 | NXT Bột/Sốt (Sổ kho) | 🔶 đang làm — lõi + service + test + giao diện xong; còn chụp màn hình, nâng bản, README (xem BÀN GIAO) |
| 2 | Số liệu cuối kỳ | ⏳ chưa làm |
| 3 | Vị Trí Bột | ⏳ chưa làm |
| 4 | Xử lý PDF & Excel | ⏳ chưa làm |
| 5 | So sánh & Tra cứu bảng | ⏳ chưa làm |
| 6 | Giao diện Sáng / Tối + cỡ chữ | ⏳ chưa làm |
| 7 | Tìm nhanh toàn app | ⏳ chưa làm |

### 1. NXT Bột/Sốt — Sổ kho (app cũ: `js/so-kho-core.js`, `js/so-kho-ui.js`; db `nxt-bot-sot`)
- Mô hình: MỌI nghiệp vụ là dòng trong **sổ cái** `SoCai` (loai: tondau · nhap · xuat · chuyen · dieuchinh). Lô = Vị trí · Mã hàng · Size · Hạn dùng · Ngày nhập.
  Xuất không ghi hạn dùng / ngày nhập → trừ **FIFO** (ngày nhập cũ trước, rồi hạn dùng gần trước). Tồn được âm (cảnh báo). Mốc **Tồn đầu** (tondau): dòng cũ hơn mốc bị bỏ qua.
- Sheet khác: `SoKhoMau` (mẫu cột), `SoKhoDinhMuc`, `SoKhoSanLuong`, `SoKhoCauHinh`, `_SoKhoLichSu` (lịch sử + hoàn tác theo thao tác).
- Làm: lõi `SoKhoCore` (normLine, compute tồn theo lô + FIFO, NXT theo kỳ, tồn tại 1 ngày, chốt tồn, định mức, cảnh báo hạn dùng, lịch sử / hoàn tác) + test đối chiếu kết quả lõi JS;
  `SoKhoService` (đọc / ghi sổ cái, kiểm tra dòng, chống trùng id); giao diện các tab: Phiếu nhập · Phiếu xuất · Tồn kho (NXT | tồn tại ngày | theo vị trí) · Kiểm kê · Định mức · Lịch sử · Cấu hình; nhập Excel; xuất Excel.
- Quyền mới `m3sk` "NXT Bột/Sốt". Menu nhóm TỒN KHO.

### 2. Số liệu cuối kỳ (app cũ: `html/quan-ly-kho/dc2-*.js`, `dc-*.js`; module M07)
- Đối chiếu TỒN CUỐI KỲ theo mã (kg): Tồn kho = Tồn An An + Tồn kho gửi; Chênh lệch = Tồn kho − Kế toán. Nhập / xuất / đầu kỳ chỉ để tham khảo + tự khớp của từng nguồn.
- Nguyên nhân gắn nhãn "xác định" / "nghi ngờ" + độ tin cậy; không đủ bằng chứng → "Chưa xác định". Lịch sử các kỳ, xuất Excel.
- Làm: đọc kỹ `dc2-engine.js`, `dc2-import.js`, `dc2-suggest.js`, `dc2-trace.js`, `dc2-history.js`, `dc2-config.js`; port lõi + test; giao diện. Xem chỗ dùng chung với `KiemKe` (Đối chiếu kiểm kê) để tái dùng.

### 3. Vị Trí Bột (app cũ: `ton-kho-an-an.js` từ dòng ~7770; sheet `ton-kho-an-an` / `ViTriBot`, `ViTriBotMeta`, `ViTriBotPhieuDienThoai`)
- Từ PC 1.62 tồn Vị Trí Bột được **đẩy từ NXT Bột/Sốt › Tồn theo vị trí** (`so-kho-ui.js › pushViTriBot`), phiếu điện thoại trừ tồn. Làm sau mục 1:
  màn hình tồn theo vị trí của bột (lọc, bỏ dòng SL = 0), nút "Đưa tồn theo vị trí lên Vị Trí Bột" từ Sổ kho, danh sách phiếu (PC / điện thoại) — hủy trừ / trừ lại / xóa.

### 4. Xử lý PDF & Excel (app cũ: `js/xu-ly-tep.js`, `js/cc-bang-engine.js`)
- PDF: gộp, tách, xoay / xóa trang, đóng dấu chữ, đánh số trang. Excel: gộp nhiều file / sheet, tách theo cột, làm sạch (khoảng trắng, dòng trùng, số / ngày dạng chữ).
- Thư viện C#: PDF dùng PdfSharp hoặc PdfPig + ghi (kiểm tra gói đã có: PdfPig, PDFium để vẽ); Excel dùng ClosedXML (đã có).

### 5. So sánh & Tra cứu bảng (app cũ: `js/so-sanh-bang.js`, `js/cc-bang-engine.js › compareTables / lookupTables`)
- Nạp 2 bảng (Excel / CSV), chọn cột khóa: (1) so sánh — dòng chỉ có ở A / B, khác số liệu theo cột; (2) tra cứu kiểu VLOOKUP — kéo cột từ B sang A. Xuất Excel.

### 6. Giao diện Sáng / Tối + cỡ chữ
- 2 bộ màu: **Sáng dịu** (nền trắng ngà / xám rất nhạt, không trắng tinh — đỡ chói) và **Tối** (xám đen cơ bản). Thu phóng cỡ chữ (90–130%).
- Hiện màu đang viết cứng trong nhiều View (`B("#...")`) → gom về bảng màu dùng chung (tài nguyên Avalonia `DynamicResource` / lớp `Theme`), đổi dần từng View. Lưu theo tài khoản trên máy.

### 7. Tìm nhanh toàn app (app cũ: `js/global-search.js`)
- Ô tìm / Ctrl + K trên thanh menu: tìm tên module (nhảy tới) + tìm mã hàng / tên hàng / lô / hợp đồng trong tồn hiện tại của mọi module
  (Tồn An An, Tồn vị trí, Tồn kho gửi, NXT Bột/Sốt, Vị Trí Bột) → bảng kết quả; bấm mã → chi tiết mã đó ở mọi module. Cách khớp từ giống app cũ (`wordsHit`).

## Nhật ký tiến độ
(Ghi theo mẫu: ngày · module · bản · đã làm · còn thiếu / lưu ý · file chính.)

- 23/11/2026 · lập kế hoạch 7 module ở trên. Bản hiện tại 1.28.0 (đã phát hành: Trợ lý hệ thống, Tổng quan + Lịch xuất, List sản xuất, Drive máy ở nhà, khởi động cùng Windows).
- 23/11/2026 · NXT Bột/Sốt · (chưa nâng bản) · lõi + service + 6 test đạt + giao diện 8 tab, build đạt · còn chụp màn hình / nâng 1.29.0 / README — xem mục BÀN GIAO · `SoKho/*.cs`, `Views/SoKhoView.cs`.

## BÀN GIAO (tạm dừng 23/11/2026, giữa module 1)
Đã commit lên nhánh `claude/blissful-brown-82s8ht` (chưa vào `main`, chưa phát hành; bản vẫn 1.28.0).

**Đã xong (module 1 — NXT Bột/Sốt):**
- `src/KhoLanh.Core/SoKho/SoKhoCore.cs` — port `so-kho-core.js`: NormLine, Validate, Compute (lô + FIFO + mốc tồn đầu), Stock, Nxt, Detail, Negatives, Expiring,
  Snapshot (chốt tồn), KiemKePlan, XuatLoCheck (tràn lô chỉ định), DinhMuc / DinhMucCheck, lịch sử (HistRows / HistOps / UndoPlan), Groups (gom phiếu), NextSo.
- `src/KhoLanh.Core/SoKho/SoKhoService.cs` — Load / Commit (ghi SoCai + _SoKhoLichSu cùng lúc), CheckDraft, SavePhieu, DeleteLines, Snapshot, ApplyKiemKe, Undo,
  SaveNorms / SaveOutputs / SaveConfig, BelowMin, ReadExcel / ImportExcel / ExportLedger / ExportTable, PhieuHtml (in A4).
- Đăng ký `AppServices.SoKho`; quyền `m3sk` trong `Permissions.cs`; menu `NavItem("m3sk", "NXT Bột/Sốt")` (nhóm TỒN KHO, trước Sơ đồ kho).
- Test: `SoKhoTests.cs` (4 test đối chiếu lõi JS qua `Data/sokho-oracle.json`, khớp tuyệt đối) + `SoKhoServiceTests.cs` (2 test) — đều ĐẠT.
  Oracle sinh bằng node chạy chính `so-kho-core.js` của app cũ (script cũ ở /tmp, không còn — muốn sinh lại thì viết lại theo cấu trúc JSON trong file oracle).
- `src/KhoLanh.App/Views/SoKhoView.cs` — 8 tab: Phiếu nhập · Phiếu xuất · Chuyển vị trí (danh sách phiếu trái, soạn phiếu trên lưới, Kiểm tra / Lưu / Xóa / In,
  bảng tồn theo lô + "Thêm lô đang chọn") · Tồn kho (theo mã+size / vị trí / lô / NXT kỳ / sắp hết hạn / tồn âm, Xuất Excel, Chốt tồn) · Kiểm kê (gõ số đếm → chênh → ghi điều chỉnh) ·
  Định mức (định mức + sản lượng + báo cáo + LSX thiếu định mức) · Sổ cái & lịch sử (lọc, xóa dòng, nạp Excel, xuất Excel, hoàn tác) · Cấu hình (dung sai, báo hạn, tồn tối thiểu).
  App **build đạt** (Release).

**Việc còn lại của module 1 (làm tiếp theo thứ tự):**
1. Cảnh chụp đã thêm ở cuối `tools/Screenshots/Program.cs` (khối "NXT Bột/Sốt (Sổ kho)", ảnh 35-…35d) nhưng **CHƯA chạy thử** — chạy
   `dotnet run -c Release --project tools/Screenshots -- <thư mục>`, sửa lỗi nếu có, xem ảnh, chỉnh giao diện; chép ảnh 35/35b vào `docs/anh/`.
2. Thử tay trong ảnh / logic: lưu phiếu xuất có cảnh báo định mức, hoàn tác, kiểm kê.
3. Chưa làm so với app cũ: mẫu cột `SoKhoMau`, sheet `SoKhoSanLuong` chỉ dùng như bảng sản lượng; nút "Đưa tồn theo vị trí lên Vị Trí Bột" để dành cho module 3.
4. Nâng `<Version>` lên 1.29.0, thêm dòng "NXT Bột/Sốt" vào bảng module `README.md`, đổi trạng thái bảng trên thành ✅, ghi Nhật ký tiến độ, commit + push.
5. Sau đó sang module 2 (Số liệu cuối kỳ) theo kế hoạch ở trên.

Lưu ý môi trường: cloud chưa có .NET → `apt-get install -y dotnet-sdk-8.0`; test: `dotnet test tests/KhoLanh.Core.Tests -c Release`.
