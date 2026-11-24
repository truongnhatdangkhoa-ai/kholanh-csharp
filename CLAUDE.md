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
| 1 | NXT Bột/Sốt (Sổ kho) | ✅ xong — bản 1.29.0 |
| 2 | Số liệu cuối kỳ | ✅ xong — bản 1.30.0 |
| 3 | Vị Trí Bột | ✅ xong — bản 1.31.0 |
| 4 | Xử lý PDF & Excel | ✅ xong — bản 1.32.0 |
| 5 | So sánh & Tra cứu bảng | ✅ xong — bản 1.33.0 |
| 6 | Giao diện Sáng / Tối + cỡ chữ | ✅ xong — bản 1.34.0 |
| 7 | Tìm nhanh toàn app | ✅ xong — bản 1.35.0 |

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
- 23/11/2026 · NXT Bột/Sốt · (chưa nâng bản) · lõi + service + 6 test đạt + giao diện 8 tab, build đạt.
- 23/11/2026 · NXT Bột/Sốt · 1.29.0 · chụp màn hình (35–35d, chép 35/35b/35c vào docs/anh), sửa nhãn "Từ" thừa ở tab Tồn kho, thêm test lưu phiếu vượt định mức (chỉ cảnh báo) + kiểm kê + hoàn tác kiểm kê (7 test SoKho đạt), README · còn: mẫu cột `SoKhoMau`; đẩy tồn lên Vị Trí Bột để dành module 3 · `SoKho/*.cs`, `Views/SoKhoView.cs`.
- 23/11/2026 · Số liệu cuối kỳ · 1.30.0 · lõi `CuoiKy/` (CkBase cấu hình + chuẩn hoá, CkImport đọc An An / Kế toán + CT + DMVT / Kho gửi / file điều chỉnh, CkSuggest gợi ý mã + CkTrace cờ chứng từ, CkEngine đối chiếu + nguyên nhân + KPI + kiểm tra dữ liệu, CkHistory xử lý / hoàn tác / gộp 2 máy, CkService lưu DC2_* + xuất Excel 10 sheet); test `CuoiKyTests` đối chiếu lõi JS (Data/ck-golden.json, sinh lại bằng `node Data/ck-golden.gen.js`) + test service; giao diện `Views/CuoiKyView.cs` (nguồn, KPI, lọc, bảng, chi tiết nguyên nhân / gợi ý / chứng từ, xử lý, lịch sử, kiểm tra dữ liệu, tự đồng bộ 8 giây); quyền `m7ck` · còn: lấy nguồn thẳng từ module Tồn An An / Tồn kho gửi (app cũ có), sửa ô dữ liệu nguồn trên lưới (lõi đã có ApplyEdits), chứng từ nhập / xuất An An.
- 23/11/2026 · Số liệu cuối kỳ · 1.30.0 đã phát hành (lần đầu CI lỗi vì `.Reverse()` của mảng trên SDK mới → dùng `Enumerable.Reverse(...)`; lưu ý khi viết code mới).
- 23/11/2026 · Vị Trí Bột · 1.31.0 · `SoKho/VtbService.cs` (đọc / ghi ViTriBotMeta + ViTriBot + ViTriBotPhieuDienThoai đúng định dạng app cũ, FromLedger + Push từ NXT Bột/Sốt, tạo phiếu trừ ngay, hủy trừ / trừ lại / xóa, khóa phiếu đợt cũ, định dạng ngày giống vtbFmtDate), nút "📱 Đưa lên Vị Trí Bột…" ở NXT Bột/Sốt › Tồn kho, `Views/VtbView.cs`, quyền `m1vtb` (MissingDefault = Xem), test `VtbTests` · còn: nhận phiếu từ điện thoại (khi làm bản điện thoại), nạp file "Vị trí Bột" kiểu cũ (app cũ đã bỏ từ PC 1.62).
- 23/11/2026 · Xử lý PDF & Excel · 1.32.0 · lõi `Bang/CcBang.cs` (làm sạch, gộp, tách, so sánh, tra cứu — test `CcBangTests` đối chiếu lõi JS, Data/cc-golden.json sinh bằng `node Data/cc-golden.gen.js`), `Bang/ExcelOps.cs` (đọc / ghi nhiều sheet), `App/Controls/PdfOps.cs` (PDFium: gộp, tách, xoay, xóa, đóng dấu số trang / chữ chìm / hình, tìm trang trắng, vẽ theo dpi), `JpegPdf.Page` thêm cỡ trang (giảm dung lượng), `Views/XuLyTepView.cs` (8 chức năng PDF + 3 Excel), quyền `m21`; chạy thử thật các thao tác trong tools/Screenshots (dòng [PDF] / [XLS]) · còn: làm sạch tùy chọn theo từng cột (lõi đã có `Cols`), xem trước trang PDF.
- 23/11/2026 · So sánh & Tra cứu bảng · 1.33.0 · `Views/SoSanhBangView.cs` (2 bảng A / B, cột khóa tự chọn theo tên trùng, so sánh song song kiểu buildSbs của app cũ + lọc Giống / Khác / Chỉ A / Chỉ B / Mã trùng, tra cứu kéo cột B → A với 5 cách xử lý mã nhiều dòng, xuất Excel), dùng lõi `CcBang.CompareTables / LookupTables` (đã có test golden); quyền `m22`; `MissingDefault` bật sẵn m21 + m22 (công cụ chỉ tạo file mới) · còn: ngày trong Excel hiện dạng số ngày (OADate) khi xem.
- 23/11/2026 · Giao diện Sáng / Tối + cỡ chữ · 1.34.0 · `App/AppTheme.cs` (tên lớp AppTheme vì Control đã có thuộc tính `Theme`): `AppTheme.Brush(hex)` brush dùng chung được giữ lại và đổi màu tại chỗ khi đổi giao diện (cả brush static trong View), `AppTheme.Color(hex)`, Sáng dịu = trắng tinh → #FBFAF6, Tối = đổi HSL (nền sáng → tối, chữ đậm → sáng), tài nguyên App.axaml thay bằng brush AppTheme (`Install`, XAML chuyển DynamicResource, thêm NavBg / GridLine / GridHead), FluentTheme đổi ThemeVariant; đã thay toàn bộ `new SolidColorBrush(Color.Parse(..))` / `Color.Parse` / `Brushes.White` nền / `Brushes.Black` chữ trong App (trừ PdfOps / PdfRender / ScanImaging và các bộ vẽ ảnh xuất file RaDong / ViTri). Thu phóng = LayoutTransformControl bọc cửa sổ chính; menu tài khoản › "Giao diện & cỡ chữ…" + Ctrl + / − / 0; lưu settings `ui_theme:<user>`, `ui_scale:<user>`. **Code mới: dùng `AppTheme.Brush("#...")` thay cho `new SolidColorBrush(Color.Parse(..))`.** · còn: ô vẽ tay (SheetGrid, sơ đồ kho) lấy màu mới khi dựng lại trang (tự dựng lại khi lưu); hộp thoại không thu phóng.
- 23/11/2026 · Tìm nhanh toàn app · 1.35.0 · lõi `Core/TimNhanh/TimNhanh.cs`: `Gs` (Norm / MaKey / WordsHit / Match nhãn size / Search / Highlight — chép global-search.js) + `GsService.Build()` gom tồn hiện tại M01VT (Tồn theo vị trí), M01 (Load Data An An, mã chưa có ở vị trí), M01VTB (Vị Trí Bột), M02 (Tồn kho gửi, nhãn từ mã hoặc cột Size dạng chữ+số), M03 (NXT Bột/Sốt: lô còn tồn) theo quyền xem; size / nhãn lấy qua `ViTriService.SizeOf / GopLabel`; test `TimNhanhTests`. Giao diện `Views/TimNhanhWindow.cs` (ô tìm, nút module khớp tên, bảng kết quả 400 dòng / trang, chi tiết mã + mở module), nút 🔍 trên thanh menu + Ctrl + K (`MainWindow.OpenSearch`) · còn: tìm Phiếu / LSX / Số cont (app cũ core/tim-nhanh.js), giải mã 10 đoạn mã 21 ký tự trong chi tiết, lọc theo cột. **Hết kế hoạch 7 module.**
- 24/11/2026 · Hoàn thành việc còn lại · 1.36.0 · **Tìm nhanh**: `GsDocs` (chép core/tim-nhanh.js: Phiếu xuất kho PNX_Phieu + phiếu vị trí, mở thẳng phiếu qua `PhieuKhoView(0, id)` / `ViTriView.OpenSlipId`), lọc cột phân tầng, `Gs.Decode` + `GsService.ReadMaHoa` (giải mã 10 đoạn). **Công cụ**: `CkService.ReadExcel(keepDates)` → ExcelOps giữ DateTime, `ExcelOps.Show` (dd/MM/yyyy); làm sạch theo từng cột (tùy chọn chung đổi thành tùy chọn từng cột để lựa chọn riêng luôn ưu tiên); xem trước trang PDF (`OnePdf`, `LoadPdfForTest`); `Ui.ApplyZoom` cho mọi hộp thoại qua `Ui.Show` + cửa sổ Tìm nhanh. **Số liệu cuối kỳ**: `CkMovement` (readMovementAnAn + phần đọc báo cáo nhập / xuất của dc-engine.js; test `CkMovementTests` với Data/cm-golden.json sinh bằng `node Data/cm-golden.gen.js`), nguồn anan_nhap / anan_xuat lưu `DC2_Nguon_*` dạng chứng từ, `CkService.LoadFromModule` / `ModuleInfo` (LoadData+LoadDataMeta, FileNap+FileNap_Meta), `CkHistory.EditCells / RevertCell` + `CkService.EditCell / RevertCell / CurrentValue / IsEdited`, tab "Dữ liệu nguồn". **NXT Bột/Sốt**: `SoKho/SkTemplate.cs` (defaultTemplate / normTemplate / templateFor / store — test `SkTemplateTests` với Data/sk-tpl-golden.json, `node Data/sk-tpl-golden.gen.js`), lưới phiếu theo mẫu + cột phụ x_ (ghi `extra`), Sổ cái hiện cột phụ, Cấu hình › Mẫu cột phiếu, công thức cột `SkTemplate.Eval` (evalFormula: ô để trống thì tự tính khi lưu phiếu). · Còn để dành có chủ đích: nhận phiếu điện thoại cho Vị Trí Bột (chờ bản điện thoại).
- 24/11/2026 · Vị Trí Bột — 2 việc cuối · 1.37.0 · **Điện thoại**: `Services/PhoneWebApp.cs` (chép core/sheets-webapp-client.js: pc.ping / pc.ensureTabs / pc.readTable / pc.writeTable / pc.markRows, POST text/plain theo 302; cấu hình `phone_webapp_url` + `phone_pc_token` (Secret) — dùng chung Web App với app điện thoại, `AppServices.Phone`), `VtbService.PushRows / ApplyPhone / SyncPhoneAsync` (kéo phiếu `PhieuXuat`/`PhieuXuatItems` module "M01VTB" chưa xử lý → trừ SL theo `_key`, đánh dấu "Đã xử lý" / "Lỗi - không có dòng hàng" sau 10 phút; đẩy `TonKho_M01_ViTriBot` + Meta `lastPushAt_M01VTB`, `m01ViTriBot*`; chỉ đụng phiếu Vị Trí Bột) — test `VtbPhoneTests` với Web App giả `FakeWebApp`. Nút "📱 Đồng bộ điện thoại" + "Kết nối điện thoại…" ở Vị Trí Bột; "Đưa lên Vị Trí Bột" ở NXT Bột/Sốt tự đẩy lên điện thoại khi đã kết nối. **Nạp file kiểu cũ**: `VtbService.ParseSheet / ParseBook / LoadFile` (dòng "NGÀY:", nhận cột theo VTB_COL_ALIASES, ngày qua `FmtDate`), nút "📄 Nạp file Vị trí Bột…" + "Tải file mẫu…". · Lưu ý: đồng bộ điện thoại của các module khác (Tồn An An, Tồn vị trí, Tồn kho gửi, NXT, Tổng hợp…) CHƯA có ở bản C# — app cũ vẫn làm phần đó; `PhoneWebApp` dùng lại được khi làm tiếp.

## KẾ HOẠCH 24/11/2026 (đợt 2)
| # | Việc | Trạng thái |
|---|---|---|
| 1 | Đổi icon + logo theo ảnh | ✅ 1.38.0 |
| 2 | Đồng bộ điện thoại mọi module (hạ tầng cho bản mobile) | ✅ 1.38.0 |
| 3 | Tự tạo List / phiếu theo Lịch xuất | ✅ 1.39.0 |
| 4 | Danh mục mã hàng | ⏳ |
| 5 | Drive cá nhân / Lưu trữ, Dọn rác | ⏳ |
| 6 | Kiểm kê nhanh | ⏳ |

- 24/11/2026 · Icon + Đồng bộ điện thoại · 1.38.0 · icon / logo mới (`Assets/app.ico` 16–256, `Assets/logo.png`, bản 1024 ở `docs/brand/icon-1024.png` cho bản mobile). `Core/PhoneSync/PhoneSyncService.cs`: đẩy M01 / M01VT (+ cột phụ vt-push-extra) / M01VTB / M02 (đặc tính suy ra) / M03 (pushRows + LSX) / M08 (`AnhXa.ToPhoneTable`) / LX / C3D + phiếu PC (`CollectPcSlips` = phieu-pc-push.js) + Meta; nhận phiếu M01 / M01VT / M01VTB / M02 / M03 (`ApplyPhonePhieu` = so-kho-core.js) đọc / ghi THẲNG sheet dùng chung rồi nạp lại service; chọn bảng `phone_sync_pick`, trạng thái `phone_sync_status`, tự đồng bộ `PhoneAutoSync` (`phone_sync_interval`). Giao diện `Views/PhoneSyncView.cs` (quyền `phone`), hộp thoại dùng chung `Views/PhoneConnect.cs`. Test `PhoneSyncTests` (Web App giả). Giao thức: `docs/DONG-BO-DIEN-THOAI.md`. · còn: tab báo cáo Excel BaoCao_M01/M02, kháng sinh chỉ phần mới, yêu cầu lịch xuất từ điện thoại, lưu trữ phiếu cũ, tài khoản điện thoại.
- 24/11/2026 · Tự tạo List / phiếu theo Lịch xuất · 1.39.0 · `LichXuat/LichXuatAuto.cs` (LichXuatService thành partial): `Mutate` chụp LSX + loại phiếu từng lịch trước khi sửa → `AfterWrite` tạo List cho lịch XUẤT sắp tới mới / đổi LSX / đổi loại (từ tồn kho, không có hàng → List trống `(tự tạo từ lịch xuất)`), `Reconcile` (gọi trong `Load`, 2 phút / lần) rà lịch 14 ngày tới thiếu khóa `__list`; `AutoMode` (settings `lx_auto_mode`: list mặc định · phieu = thêm Phiếu xuất khẩu theo nhóm cùng ngày + cont · off); `Auto` chỉ bật ở app (`MainWindow`), test bật tay. `PhieuKho/PhieuKhoAuto.cs` (PhieuKhoService thành partial): nhật ký `PNX_AutoLog` đúng cột app cũ (LogKey = lsxNorm|loaiPhieu, giữ 2000 dòng), nhãn `tuDong` {at, ngayLich, loaiLich, lichIds} trong `Extra`, chưa xem (listNew / phieuNew + listSeen / phieuSeen), `MarkSeen`, `AutoItems`, xóa hết List + Phiếu của LSX → quên nhật ký. Nút Tạo List / Tạo phiếu cũng gắn nhãn + ghi nhật ký (không tính "mới"). Giao diện `Views/PnxAutoView.cs` (tab "⚡ Tự động đã tạo" của Phiếu xuất kho), ⚡ trên danh sách phiếu / List, nút "⚡ Tự tạo: n" trên thanh menu. Test `Tu_tao_list_theo_lich_xuat_va_nhat_ky`. · còn: tự tạo phiếu loại khác xuất khẩu (app cũ cũng chỉ tạo List), cập nhật List khi Load Data mới (đã có "Quét tồn kho").
