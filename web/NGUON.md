# Mã gốc 2 module Tồn kho An An và Tồn kho gửi

Thư mục này là bản chép NGUYÊN VẸN (không sửa dòng nào) các tệp cần cho 2 trang
`html/ton-kho-an-an.html` và `html/ton-kho-gui.html` của app cũ `kholanh-anan-pc`
(thư mục `app/`, commit 8f42a876, 22/11/2026). Giữ đúng cấu trúc thư mục để các đường dẫn
`../lib/…`, `../node_modules/…` trong trang vẫn đúng.

- `node_modules/xlsx/dist/xlsx.full.min.js`: SheetJS 0.20.3 (đọc file Excel) — đúng bản app cũ cài.
- `lib/xlsx.bundle.js`: xlsx-js-style (ghi file Excel có định dạng) — như app cũ.

App C# nhúng thư mục này vào `KhoLanh.exe`, khi chạy giải nén ra `%LOCALAPPDATA%\KhoLanhCS\web\…`
và mở trong Microsoft Edge WebView2 tại địa chỉ ảo `https://kholanh.local/html/…`.
`window.dbAPI` / `window.authAPI` (trước là preload.js của Electron) do
`src/KhoLanh.App/Web/kl-bridge.js` cấp, dữ liệu lưu SQLite của app mới (bảng `module_sheets`).

## Cập nhật khi app cũ đổi module

Chép đè lại đúng các tệp tương ứng từ `kholanh-anan-pc/app/` vào đây rồi build lại.
Danh sách tệp JS cần là các `<script src>` trong 2 trang html (và thư viện nạp lười trong `lib/nap-thu-vien.js`).

## Đã đối chiếu

Ngày 22/11/2026, nạp `TON KHO AN AN 21.11.xlsx` và `TON KHO GUI 21.11.xls` vào trang cũ
(giả lập preload/main của Electron) và vào app mới (cầu nối C#), cùng dữ liệu cấu hình chép từ
database app cũ, rồi bấm mọi nút Xuất Excel: 15/15 file của Tồn kho An An và 9/9 file của
Tồn kho gửi giống nhau từng byte; dữ liệu lưu sau khi nạp giống nhau (trừ mã lần nạp ngẫu nhiên).
