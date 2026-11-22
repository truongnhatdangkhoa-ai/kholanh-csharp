# Tài khoản và phân quyền dùng chung cho mọi máy (Google Sheet)

Từ bản 1.3.0, các máy có thể dùng **chung một danh sách tài khoản và phân quyền**, lưu trên một Google Sheet của bạn.
Cách làm giống app kho cũ (Apps Script Web App): **không cần Google Cloud, không cần service account hay file key**.

- Google Sheet là bản gốc. Mỗi máy tải danh sách về **lúc đăng nhập**. Nếu mất mạng, máy dùng bản lưu sẵn trong máy, nên vẫn đăng nhập được.
- Chỉ **máy quản trị** (có "mã quản trị") được thêm tài khoản, sửa tài khoản, phân quyền và đặt lại mật khẩu. Thay đổi được ghi lên Sheet trước. Ghi lên Sheet không được thì máy không đổi gì.
- **Mật khẩu không bao giờ lưu dạng chữ.** Sheet chỉ chứa mã băm PBKDF2-SHA256 (100.000 vòng) và muối ngẫu nhiên.
- Tài khoản bị xóa khỏi Sheet sẽ bị **khóa** trên mọi máy ở lần đăng nhập sau.

## A. Thiết lập trên máy quản trị (một lần, khoảng 5 phút)

1. Mở phần mềm và đăng nhập bằng tài khoản Quản trị. Vào **Hệ thống › Sao lưu và cài đặt › Tài khoản dùng chung cho mọi máy**.
2. Bấm **Tạo mã Apps Script**. Mã được tự chép vào bộ nhớ, và cũng được lưu ở thư mục dữ liệu, file `TaiKhoan-AppsScript.gs`.
3. Mở <https://sheets.new> để tạo một Google Sheet trống, rồi đặt tên, ví dụ *Kho Lạnh - Tài khoản*.
4. Trong Sheet: **Tiện ích mở rộng › Apps Script**. Xóa hết mã có sẵn, dán (Ctrl+V), rồi bấm **Lưu** (biểu tượng đĩa).
5. Bấm **Triển khai › Tùy chọn triển khai mới**:
   - Chọn loại (bánh răng): **Ứng dụng web**.
   - Thực thi với tư cách: **Tôi**.
   - Ai có quyền truy cập: **Bất kỳ ai**.
   - Bấm **Triển khai**, rồi **Cho phép truy cập**, chọn tài khoản Google, chọn **Nâng cao › Đi tới … (không an toàn)**, rồi **Cho phép**. Đây là mã của chính bạn, nên Google mới cảnh báo như vậy.
6. Chép **URL ứng dụng web** (kết thúc bằng `/exec`) và dán vào ô trong phần mềm. Bấm **Kết nối**.
   Phần mềm đưa các tài khoản đang có trên máy này lên Sheet (tab *TaiKhoan*).

## B. Nối các máy khác

1. Trên máy quản trị, bấm **Chép mã cho máy nhân viên**. Bạn được một dòng mã `KLTK1-…`; gửi mã đó sang máy kia, ví dụ qua Zalo.
2. Máy kia có hai trường hợp:
   - **Máy mới** (mở phần mềm lần đầu): bấm **Dùng chung tài khoản với máy khác…**, dán mã. Máy nhận toàn bộ tài khoản, rồi bạn đăng nhập bình thường.
   - **Máy đã dùng**: đăng nhập bằng tài khoản Quản trị của máy đó, vào **Sao lưu và cài đặt › Tài khoản dùng chung**, dán mã vào ô, bấm **Kết nối**. Danh sách tài khoản của máy đó được thay bằng danh sách trên Sheet.
3. Nếu muốn thêm một máy quản trị nữa, bấm **Chép mã cho máy quản trị**. Chỉ gửi mã này cho người quản trị.

## Lưu ý

- Đừng sửa tay tab *TaiKhoan* trên Google Sheet. Mọi thay đổi làm trong phần mềm, ở **Hệ thống › Tài khoản**.
- Đừng chia sẻ Google Sheet hay file Apps Script cho người ngoài, vì trong đó có mã kết nối.
- Nếu lộ mã, bấm **Tạo lại mã Apps Script**, dán lại vào Apps Script, rồi chọn **Triển khai › Quản lý triển khai › sửa › Phiên bản mới**. Sau đó kết nối lại các máy.
- Máy nhân viên không tự đổi được mật khẩu. Nhờ Quản trị đặt lại.
