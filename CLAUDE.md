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
- Màu: dùng `AppTheme.Brush("#...")` (tự đổi theo Sáng dịu / Tối), không `new SolidColorBrush(Color.Parse(..))`. Bộ màu "Băng sạch": chữ #1A2433, phụ #5D6B78, nhấn #1F6FD6, nền nhạt trạng thái ok #E3F4EA · cảnh báo #FFF2D6 · lỗi #FDE8E6 · thông tin #EEEBFC (chữ #1E7B4A / #9A5B00 / #B42318 / #5B4BC4); màu cũ được `AppTheme.Unify` tự quy về bộ này. Nút: thường / `primary` (1 nút / màn hình) / `ghost` / `danger`; nhãn trạng thái `Border.pill.ok|warn|bad|info|accent`; biểu tượng `Controls/Icon` (emoji đầu chữ nút được tự đổi). Menu: `MainWindow.NavIcons` + `SetBadge`. Cỡ chữ: `LayoutTransformControl` ở MainWindow + `Ui.ApplyZoom` cho hộp thoại.
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
| 1.48 | Nút / tab / menu nổi bật hơn ở mọi module: chữ nút SemiBold 13.5 (nút chính Bold), tab SemiBold (tab đang chọn Bold), mục thanh bên SemiBold, viền nút `BorderStrong` #B9C3D0, chữ menu sáng hơn (NavFg #DCE5F0, NavMuted #8FA0B6), biểu tượng nét 2.1 | `Styles.axaml`, `AppTheme.cs`, `Controls/Icon.cs` |
| 1.47 | Menu xếp gọn: nhóm Chức năng / Chứng từ / Công cụ / Hệ thống tự thu lại, chỉ mở nhóm chứa màn hình đang dùng (bấm tên nhóm để mở / đóng, nhóm đang thu hiện tổng số việc cần xử lý); Phiếu xuất kho chuyển vào cuối nhóm Tồn kho | `MainWindow.axaml.cs` (`Accordion`, `ApplyAccordion`), `Styles.axaml` (Button.sidegrphead) |
| 1.46 | Sửa 3 lỗi tranh chấp LAN: nhân đôi dòng khi máy chủ áp sổ chờ nhưng trả lời bị mất rồi máy con sửa tiếp (`sent` / `prev_ops`, CSDL v5) · máy nhận bỏ lỡ thay đổi ghi chen giữa câu trả lời "changes" · tải lại toàn bộ xóa nhầm bảng vừa ghi · màn hình đăng nhập gọn 1 cột | `Lan/LanService.cs`, `Data/Database.cs`, `LoginWindow.axaml` |
| 1.45 | Giao diện mới bước 3–4 (toàn app): `AppTheme.Unify` quy màu cũ của mọi View về bộ Băng sạch (tô nền đậm → nền nhạt trạng thái, màu thương hiệu cũ → mới; không áp khi xuất ảnh / in), `Icon.InstallEmojiSwap` đổi emoji đầu chữ của nút / tab / menu thành biểu tượng nét mảnh, mật độ bảng Gọn / Vừa / Thoáng (`AppTheme.Density`, `SheetGrid.RowH`), trạng thái trống cho bảng chỉ đọc, thẻ số Tổng quan viền mảnh, màn hình đăng nhập theo màu mới, bỏ API cũ Avalonia (Clipboard / kéo thả) | `AppTheme.cs`, `Controls/Icon.cs`, `Controls/SheetGrid.cs` |
| 1.44 | Giao diện mới bước 2 — thanh bên trái thu gọn được (Ctrl + B, rê chuột hiện tên ngay, nhớ theo tài khoản `ui_nav_mini:<tk>`), biểu tượng từng module, số việc cần xử lý (`MainWindow.SetBadge`, Danh mục mã hàng tính ngầm sau 30 s), thanh đầu trang gộp tiêu đề + Tìm nhanh + tài khoản | `MainWindow.axaml(.cs)`, `Styles.axaml` (Button.side…) |
| 1.43 | Giao diện mới bước 1 — nền móng: bảng màu Tối thiết kế riêng (`AppTheme.DesignedDark`), tài nguyên mới (Text, Surface2, BorderStrong, Ok/Warn/Bad/Info + Soft, NavFg…), kiểu nút primary / thường / ghost / danger, ô nhập 32 px, tab gạch chân, `Border.pill.*`, bộ biểu tượng nét mảnh `Controls/Icon.cs` | `AppTheme.cs`, `Styles.axaml`, `Controls/Icon.cs` |
| 1.42 | Sửa lỗi cập nhật FileNotFoundException (nhiều cửa sổ) · bắt lỗi giao diện không sập app · MailKit 4.18.2 · dọn tài liệu / ảnh cũ · sắp lại nhóm menu (Chức năng, Chứng từ) | `AppUpdater.cs`, `Services/UpdateScript.cs` |

## Việc tạm hoãn / còn mở
- Drive cá nhân / Lưu trữ cá nhân / Dọn rác (app cũ: `core/drive-ca-nhan.js`, `core/luu-tru.js`, `core/kho-ca-nhan.js`, `core/don-rac.js`) — người dùng hoãn 24/11: "khi ổn định sẽ làm sau".
- Danh mục mã hàng: số "Cần xử lý" trên thanh menu (app cũ có).

## ĐÃ XONG: THAY ÁO TOÀN BỘ GIAO DIỆN (yêu cầu 24/11/2026 — người dùng ĐÃ CHỐT phong cách "Băng sạch")
- Đề xuất phong cách "Băng sạch" (bản dựng thử: https://claude.ai/artifact/N7BE31VXtV6cH4TgFzXfGk): nền xám lạnh rất nhạt, 1 màu nhấn xanh băng theo logo,
  màu chỉ để báo trạng thái (nền nhạt + chữ đậm), chữ Inter, số căn phải tabular, bỏ emoji trong nút / tiêu đề, Sáng dịu + Tối thiết kế riêng, mật độ bảng Gọn / Vừa / Thoáng.
- **Thanh menu bên trái** thay menu thả xuống; **thu gọn được** (chỉ còn biểu tượng) — khi thu gọn, rê chuột vào mục nào thì **hiện tên mục đó tức thì** (không độ trễ tooltip);
  nhớ trạng thái thu gọn theo tài khoản; số việc cần xử lý cạnh tên module.
- Thứ tự nhóm menu (1.42.0, Phiếu xuất kho vào cuối Tồn kho ở 1.47.0; Chức năng / Chứng từ / Công cụ / Hệ thống xếp gọn): Tổng quan · Nhập liệu · Tồn kho (… Sơ đồ kho, Phiếu xuất kho) · Chức năng (Báo cáo rã đông, Đối chiếu hàng ngày, Đối chiếu kiểm kê, Số liệu cuối kỳ,
  Tổng hợp, Danh mục mã hàng, Kết quả kháng sinh) · Chứng từ (In chứng từ kho, Phiếu xuất hệ thống) · Công cụ · Hệ thống.
- Mỗi trang cùng khuôn: tiêu đề + mô tả 1 dòng, nút chính góc phải → thẻ số → thanh lọc → bảng → khung chi tiết bên phải; 1 nút chính / màn hình, nút xóa viền đỏ.
- Trạng thái: bước 1 ✅ 1.43.0 · bước 2 ✅ 1.44.0 · bước 3 + 4 ✅ 1.45.0 — HOÀN THÀNH thay áo giao diện
- Các bước (mỗi bước 1 bản): 1 nền móng (bộ màu / kiểu control / biểu tượng) · 2 khung app (thanh bên thu gọn, đầu trang, Ctrl+K, trạng thái LAN / Drive) ·
  3 khuôn trang dùng chung + Tổng quan / Tồn kho · 4 các module còn lại + chụp lại ảnh. Sửa luôn API Avalonia cũ (Clipboard.GetTextAsync, DragEventArgs.Data).

## Nhật ký tiến độ
(Ghi theo mẫu: ngày · việc · bản · đã làm · còn thiếu / lưu ý · file chính.)
- 24/11/2026 · Nút nổi bật · 1.48.0 · người dùng: "các nút menu và các nút nạp ở tất cả module làm cho nổi bật hơn, chữ đậm hơn" → đổi ở bộ kiểu dùng chung (Styles.axaml) nên áp cho mọi module.
- 24/11/2026 · Menu xếp gọn · 1.47.0 · `Accordion` (4 nhóm), `_openGroup` đặt theo màn hình đang mở trong `Show` (màn hình ngoài 4 nhóm → thu hết), thanh bên thu gọn (chỉ biểu tượng) thì hiện mọi mục; Phiếu xuất kho (m13) chuyển sang nhóm Tồn kho cả ở menu lẫn `Perm.Items` (nhóm "Tồn kho gốc").
- 24/11/2026 · Sửa lỗi LAN + đăng nhập gọn · 1.46.0 · nguyên nhân lỗi chập chờn `LanTests.Offline_client…` ("a,b,c,a,b"): mục sổ chờ đã gửi, máy chủ áp xong nhưng trả lời mất → người dùng sửa tiếp → upsert đổi op_id / data nhưng giữ bản gốc cũ → máy chủ gộp 3 bên với bản gốc sai. Sửa: cột `sent` (đánh dấu trong `Flush` / `PendingItems`) + `prev_ops` (upsert đẩy lần đã gửi vào danh sách), mục gửi kèm `prev`; `ApplySyncItem` thấy lần trước đã áp (`lan_applied`) thì dùng nội dung đó làm bản gốc; `Flush` không tải đè bảng còn mục chờ mới hơn. Test tất định `Resent_pending_after_lost_reply_is_not_duplicated` (bỏ phần sửa thì ra đúng "a,b,c,a,b"). Cùng bản còn sửa 2 lỗi tranh chấp LAN khác (tìm bằng cách chạy 14 bộ `LanTests` song song): (a) trả lời "changes" đọc danh sách trước rồi mới đọc seq → lần ghi chen giữa bị máy nhận bỏ lỡ vĩnh viễn (làm `Standby_takes_over…` đỏ, chặn phát hành 1.45.0) — nay đọc seq trước; (b) `Pull` tải lại toàn bộ xóa nhầm bảng cục bộ vừa ghi sau mốc máy chủ báo → nay chỉ xóa bảng có phiên bản ≤ seq. Test `Offline_client…` chờ dữ liệu ban đầu lên máy chủ trước khi tắt máy chủ (tiền đề kịch bản). Kiểm: 14 bộ song song × 3 đợt đều đạt. Đăng nhập: 1 cột, bỏ khối nền, bỏ ghi chú (đường dẫn dữ liệu ở gợi ý số phiên bản; ghi chú chỉ hiện khi lần đầu / có lỗi).
- 24/11/2026 · Giao diện mới bước 3–4 · 1.45.0 · làm ở tầng dùng chung để phủ cả 44 màn hình: quy màu (`Unify`), đổi emoji nút → biểu tượng (`InstallEmojiSwap`, công cụ chụp tìm nút bằng `Icon.TextOf`), mật độ bảng (hộp "Giao diện & cỡ chữ"), trạng thái trống lưới, Tổng quan thẻ số, đăng nhập, API Avalonia mới (`DataTransfer`, `TryGetTextAsync`). Chụp lại ảnh. Còn có thể làm thêm (không bắt buộc): khung chi tiết bên phải cho các bảng lớn chưa có, viết lại từng View theo khuôn trang (hiện dùng khuôn cũ + kiểu mới).
- 24/11/2026 · Giao diện mới bước 2 · 1.44.0 · `MainWindow.axaml` viết lại: thanh bên (Border `Side` 232 / 60 px, `BuildSide`, `SetMini`, `NavIcons`, `SetBadge`), nút trạng thái trên đầu trang dùng `Icon.Label` (bỏ emoji), Tổng quan: đầu khung Lịch xuất trong ngày xếp dòng tự do (hết đè chữ ở 1366 px). Ảnh `03d-menu-thu-gon`. Bước 3 tiếp: khuôn trang dùng chung (đầu trang / thanh lọc / thẻ số / khung chi tiết / trạng thái trống) + Tổng quan, Tồn kho.
- 24/11/2026 · Giao diện mới bước 1 · 1.43.0 · màu mới "Băng sạch" (nền #F4F6F9, nhấn #1F6FD6, Sáng dịu: trắng tinh → #FBFCFD), Tối thiết kế riêng cho các màu chính (màu ngoài bảng vẫn đổi HSL); Styles.axaml viết lại; `Icon.Make(name)` / `Icon.Button(text, icon, kind)` (46 biểu tượng, nét theo màu chữ cha) — CHƯA thay emoji trong các View (làm ở bước 3–4). Bước 2 tiếp: thanh bên thu gọn (rê chuột hiện tên ngay) thay menu trên, `MainWindow.axaml(.cs)`.
- 24/11/2026 · Quét sau phát hành · 1.42.0 · `Dispatcher.UIThread.UnhandledException` (lỗi thao tác không sập app), sửa lỗi Trợ lý báo `FileNotFoundException System.Diagnostics.Process` ở `AppUpdater.RunHelper` (Warmup + chặn cập nhật khi còn cửa sổ khác + script chờ mọi cửa sổ, test `UpdateScriptTests`), MailKit 4.8.0 → 4.18.2 (lỗ hổng GHSA-9j88-vvj5-vhgr), `SoKhoView.ApplyTemplate` → `ApplyMau` (trùng tên hàm Avalonia), xóa `docs/BAN-GIAO.md` (việc WebView2 đã xong) + 60 ảnh không dùng, rút gọn CLAUDE.md.
