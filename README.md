# Kho Lạnh C#

Phần mềm quản lý kho lạnh chạy trên Windows, viết bằng C# (.NET 8, giao diện Avalonia), dữ liệu SQLite cục bộ.
Đây là phần mềm **mới, tách riêng**: không dùng chung dữ liệu và không ảnh hưởng tới phần mềm kho cũ.

![Màn hình chính](docs/anh/03-man-hinh-chinh.png)

## Module hiện có

App đang được dựng lại theo từng module (từ bản 1.7.0 đã gỡ các module phiếu / tồn / sổ kho / báo cáo / danh mục của bản 1.0).

| Nhóm | Chức năng |
|---|---|
| Tồn kho An An | Nhúng nguyên module của app cũ (WebView2): nạp file tồn gốc, phân loại, báo cáo Tổng Tồn / HLSO. Xem `web/NGUON.md`. |
| Tồn kho gửi | Nhúng nguyên module của app cũ: nạp file tồn kho gửi, phân loại, phiếu xuất, Đặc tính. |
| Tổng hợp | Viết bằng C# (không WebView2), dùng chung dữ liệu với app cũ (db `tong-hop`). Mã hóa sản phẩm 21 ký tự (10 tổ hợp, danh sách chuẩn), danh sách mã hàng (11.603 mã mặc định, tô đỏ mã sai độ dài, tô vàng mã trùng / tổ hợp chưa khai báo, ghép mã), Bảng 1 nhập từ Excel → Bảng 2 gộp theo Ngày + Tên sản phẩm + Size, ánh xạ tên hàng (quy tắc đơn, kết hợp). Chưa làm: Tra cứu kháng sinh. |
| Nhập liệu | Viết bằng C# (không WebView2). Các trang dạng lưới Excel: gõ / dán thẳng từ Excel (Ctrl+C / Ctrl+V), hoàn tác, điền xuống, tự lưu, mẫu cột (Nhập kho, Xuất kho, Kiểm kê, mẫu tự lưu), nhập / xuất Excel, thùng rác. Lưới dùng chung: `src/KhoLanh.App/Controls/SheetGrid.cs` + `src/KhoLanh.Core/Grid/GridModel.cs`. |
| Tài khoản | Đăng nhập (nhớ mật khẩu, tự đăng nhập), 3 vai trò + phân quyền tới từng module. Dùng chung cho mọi máy qua Google Sheet: `docs/TAI-KHOAN-DUNG-CHUNG.md`. |
| Cập nhật | Tự kiểm tra GitHub Releases lúc mở app và mỗi 30 phút, tải ngầm, mở lại là dùng bản mới; quay lại bản trước. |
| An toàn | Tự sao lưu mỗi ngày (giữ 30 bản), sao lưu ra file, khôi phục, nhật ký thao tác. |

**Thêm module mới:** thêm `NavItem` trong `MainWindow.axaml.cs` và mục quyền trong `Perm.Items` (`src/KhoLanh.Core/Models/Permissions.cs`). Dữ liệu module đọc / ghi qua `ISheetSource` (`App.Services.Lan`) để tự đồng bộ LAN; thêm tên database vào `ModuleBridge.PermKey`.

![Nhập liệu](docs/anh/20b-nhap-lieu-chon-vung.png)
![Tổng hợp](docs/anh/21b-tong-hop-bang.png)

## Dùng chung qua mạng LAN

Nhiều máy trong mạng nội bộ dùng chung dữ liệu module: máy chủ chính, máy dự phòng tự thay khi máy chủ tắt, máy con.
Mất máy chủ vẫn làm việc được, có máy chủ lại thì tự gộp. Xem [docs/MANG-LAN.md](docs/MANG-LAN.md).

## Cài đặt và chạy

1. Máy đầu tiên: tải `KhoLanh.exe` ở **Releases**. Máy tiếp theo: dùng nút **Tạo bộ cài cho máy mới** trong app.
2. Chép vào một thư mục bất kỳ, nhấp đúp để chạy. Không cần cài .NET. Sau đó app tự cập nhật.

Windows SmartScreen có thể cảnh báo vì file chưa ký số: bấm **More info › Run anyway**.

## Dữ liệu nằm ở đâu

`%LOCALAPPDATA%\KhoLanhCS\`
- `khodata.db` — toàn bộ dữ liệu (SQLite)
- `SaoLuu\` — bản sao lưu tự động hằng ngày

Muốn đặt dữ liệu ở chỗ khác (ví dụ ổ D), đặt biến môi trường `KHOLANH_DATA=D:\KhoLanhData` trước khi chạy.

## Dành cho lập trình viên

```
src/KhoLanh.Core      nghiệp vụ + SQLite (Dapper), xuất Excel (ClosedXML), mẫu in
src/KhoLanh.App       giao diện Avalonia
tests/KhoLanh.Core.Tests  kiểm thử xUnit cho nghiệp vụ
tools/Screenshots     tạo dữ liệu mẫu và chụp ảnh các màn hình (chạy không cần màn hình)
```

```bash
dotnet test tests/KhoLanh.Core.Tests
dotnet run --project src/KhoLanh.App
dotnet publish src/KhoLanh.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/win-x64
dotnet run --project tools/Screenshots -- docs/anh
```

Mỗi lần đẩy lên nhánh `main`, GitHub Actions tự chạy kiểm thử và đóng gói `KhoLanh.exe`. Gắn tag `v1.0.0` để tạo bản phát hành.
