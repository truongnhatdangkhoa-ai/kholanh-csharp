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
- Phát hành: PR từ nhánh làm việc vào `main` (đẩy thẳng `main` bị chặn ở môi trường cloud), gộp khi CI xanh (build + `tu-kiem-windows` chạy cập nhật thật) →
  workflow `.github/workflows/build.yml` đóng gói `KhoLanh.exe` (một file) và tạo Release `v<Version>`; app các máy tự cập nhật.
- Môi trường cloud chưa có .NET: `apt-get install -y dotnet-sdk-8.0`.

## Ghi chú kỹ thuật cần nhớ
- Đối chiếu với app cũ: chạy CHÍNH lõi JS cũ bằng node để sinh đáp án (`tests/KhoLanh.Core.Tests/Data/*-golden.gen.js` → `*.json`), test C# so từng giá trị.
- Màu: dùng `AppTheme.Brush("#...")` (tự đổi theo Sáng dịu / Tối), không `new SolidColorBrush(Color.Parse(..))`. Cỡ chữ: `LayoutTransformControl` ở MainWindow + `Ui.ApplyZoom` cho hộp thoại.
- SDK mới: mảng `.Reverse()` lỗi → dùng `Enumerable.Reverse(...)`.
- Lưới: `SheetGrid` + `GridModel`; cột tính toán dùng `SheetGrid.Display`, giữ mã dòng bằng cột ẩn rộng 0 (xem `DanhMucMaView`).
- Exe một file: thư viện nạp từ chính file exe khi cần lần đầu → `AppUpdater.Warmup()` nạp sẵn lúc mở app; không thay exe khi còn cửa sổ KhoLanh khác chạy (`UpdateScript`).
- Lỗi chưa bắt: `Program.cs` (AppDomain / Task) + `App` (`Dispatcher.UIThread.UnhandledException` → ghi `logs/loi.log`, báo người dùng, KHÔNG sập app). Trợ lý hệ thống đọc lại log.
- Công cụ chụp màn hình chạy đủ mất > 10 phút: chạy nền, hoặc `SHOTS_ONLY=dm|ck|tools` để chụp riêng.
- Repo app cũ chỉ đọc; không chép token cố định trong `core/app-config` của app cũ.

## Các bản đã phát hành (tóm tắt — chi tiết ở README và lịch sử commit)
| Bản | Nội dung chính | File / lõi chính |
|---|---|---|
| 1.25–1.28 | Trợ lý hệ thống · Tổng quan + Lịch xuất (email) · List sản xuất · Drive máy ở nhà · khởi động cùng Windows | `TroLy/`, `LichXuat/`, `Lan/DriveSync.cs` |
| 1.29 | NXT Bột/Sốt (sổ kho theo lô, FIFO, định mức, kiểm kê, hoàn tác) | `SoKho/`, test `sokho-oracle.json` |
| 1.30 | Số liệu cuối kỳ (đối chiếu kế toán – kho, nguyên nhân, lịch sử) | `CuoiKy/` |
| 1.31 | Vị Trí Bột (đẩy từ NXT, phiếu trừ tồn) | `SoKho/VtbService.cs` |
| 1.32–1.33 | Xử lý PDF & Excel · So sánh & Tra cứu bảng | `Bang/CcBang.cs`, `Controls/PdfOps.cs` |
| 1.34 | Giao diện Sáng dịu / Tối + cỡ chữ | `App/AppTheme.cs` |
| 1.35–1.36 | Tìm nhanh toàn app (Ctrl+K, phiếu / LSX / cont, giải mã 21 ký tự) + việc còn lại | `TimNhanh/` |
| 1.37–1.38 | Vị Trí Bột + đồng bộ điện thoại mọi module · icon / logo mới | `PhoneSync/`, `Services/PhoneWebApp.cs`, `docs/DONG-BO-DIEN-THOAI.md` |
| 1.39 | Tự tạo List / phiếu theo Lịch xuất (`PNX_AutoLog` dùng chung app cũ) | `LichXuat/LichXuatAuto.cs`, `PhieuKho/PhieuKhoAuto.cs` |
| 1.40 | Danh mục mã hàng (quyền `m3dm`) | `DanhMuc/` |
| 1.41 | Kiểm kê nhanh (tab đầu Đối chiếu kiểm kê) | `KiemKe/KkNhanh.cs`, `Views/KiemKeView.Nhanh.cs` |
| 1.42 | Sửa lỗi cập nhật FileNotFoundException (nhiều cửa sổ) · bắt lỗi giao diện không sập app · MailKit 4.18.2 · dọn tài liệu / ảnh cũ · sắp lại nhóm menu (Chức năng, Chứng từ) | `AppUpdater.cs`, `Services/UpdateScript.cs` |

## Việc tạm hoãn / còn mở
- Drive cá nhân / Lưu trữ cá nhân / Dọn rác (app cũ: `core/drive-ca-nhan.js`, `core/luu-tru.js`, `core/kho-ca-nhan.js`, `core/don-rac.js`) — người dùng hoãn 24/11: "khi ổn định sẽ làm sau".
- Danh mục mã hàng: số "Cần xử lý" trên thanh menu (app cũ có).
- Cảnh báo build còn lại: API cũ của Avalonia (Clipboard.GetTextAsync, DragEventArgs.Data) — đổi khi thay áo giao diện.

## KẾ HOẠCH TIẾP: THAY ÁO TOÀN BỘ GIAO DIỆN (yêu cầu 24/11/2026 — CHỜ NGƯỜI DÙNG CHỐT PHONG CÁCH)
- Đề xuất phong cách "Băng sạch" (bản dựng thử: https://claude.ai/artifact/N7BE31VXtV6cH4TgFzXfGk): nền xám lạnh rất nhạt, 1 màu nhấn xanh băng theo logo,
  màu chỉ để báo trạng thái (nền nhạt + chữ đậm), chữ Inter, số căn phải tabular, bỏ emoji trong nút / tiêu đề, Sáng dịu + Tối thiết kế riêng, mật độ bảng Gọn / Vừa / Thoáng.
- **Thanh menu bên trái** thay menu thả xuống; **thu gọn được** (chỉ còn biểu tượng) — khi thu gọn, rê chuột vào mục nào thì **hiện tên mục đó tức thì** (không độ trễ tooltip);
  nhớ trạng thái thu gọn theo tài khoản; số việc cần xử lý cạnh tên module.
- Thứ tự nhóm menu (đã áp ở 1.42.0): Tổng quan · Nhập liệu · Tồn kho · Chức năng (Báo cáo rã đông, Đối chiếu hàng ngày, Đối chiếu kiểm kê, Số liệu cuối kỳ,
  Tổng hợp, Danh mục mã hàng, Kết quả kháng sinh) · Chứng từ (Phiếu xuất kho, In chứng từ kho, Phiếu xuất hệ thống) · Công cụ · Hệ thống.
- Mỗi trang cùng khuôn: tiêu đề + mô tả 1 dòng, nút chính góc phải → thẻ số → thanh lọc → bảng → khung chi tiết bên phải; 1 nút chính / màn hình, nút xóa viền đỏ.
- Các bước (mỗi bước 1 bản): 1 nền móng (bộ màu / kiểu control / biểu tượng) · 2 khung app (thanh bên thu gọn, đầu trang, Ctrl+K, trạng thái LAN / Drive) ·
  3 khuôn trang dùng chung + Tổng quan / Tồn kho · 4 các module còn lại + chụp lại ảnh. Sửa luôn API Avalonia cũ (Clipboard.GetTextAsync, DragEventArgs.Data).

## Nhật ký tiến độ
(Ghi theo mẫu: ngày · việc · bản · đã làm · còn thiếu / lưu ý · file chính.)
- 24/11/2026 · Quét sau phát hành · 1.42.0 · `Dispatcher.UIThread.UnhandledException` (lỗi thao tác không sập app), sửa lỗi Trợ lý báo `FileNotFoundException System.Diagnostics.Process` ở `AppUpdater.RunHelper` (Warmup + chặn cập nhật khi còn cửa sổ khác + script chờ mọi cửa sổ, test `UpdateScriptTests`), MailKit 4.8.0 → 4.18.2 (lỗ hổng GHSA-9j88-vvj5-vhgr), `SoKhoView.ApplyTemplate` → `ApplyMau` (trùng tên hàm Avalonia), xóa `docs/BAN-GIAO.md` (việc WebView2 đã xong) + 60 ảnh không dùng, rút gọn CLAUDE.md.
