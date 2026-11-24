# Lưu trữ cá nhân (1.59)

Công cụ › **Lưu trữ cá nhân** (quyền `m19`): nơi cất ghi chú, số liệu, liên kết, ảnh, tệp của **riêng từng tài khoản**.
Làm lại từ M19 của app cũ (khung chat) thành dạng **bảng + ngăn + thẻ** cho dễ quản lý.

## Dùng
- **Ngăn** (cột trái): chia nhóm (vd Hợp đồng, Số liệu kho, Cá nhân). Chuột phải để đổi tên / xóa (xóa ngăn thì mục về ngăn Chung, không mất).
- **Ghi chú mới** (Ctrl + N): tiêu đề + nội dung; có đường dẫn `https://…` thì thành **Liên kết** (nút mở ngay).
- **Thêm tệp / ảnh** hoặc kéo thả tệp vào trang (≤ 500 MB / tệp). Ảnh xem trước ở khung phải; Mở / Thư mục / Lưu bản sao.
- Tìm không cần dấu (Ctrl + F), lọc theo loại / thẻ, ghim lên đầu, chọn nhiều dòng để Chuyển ngăn / Xóa. Xóa → **Thùng rác** (khôi phục được); Xóa hẳn mới mất.
- Thẻ số đầu trang: số mục, ghi chú · liên kết, ảnh · tệp, dung lượng, trạng thái sao lưu Drive.

## Đăng nhập Google + sao lưu (tab "Sao lưu & Google")
- Mỗi tài khoản phần mềm đăng nhập **Gmail của riêng mình** (mở trình duyệt, cho phép, quay lại app). Quyền xin: `drive.file`
  (app chỉ thấy thư mục do nó tạo) + email. Mã đăng nhập cất mã hóa theo tài khoản Windows (DPAPI).
- Drive: `KhoLanh_LuuTruCaNhan/<tài khoản>/` gồm `data.json` (danh sách mục mới nhất), `tep/` (mỗi tệp tải 1 lần), `ban-theo-ngay/data-YYYY-MM-DD.json` (giữ 7–365 ngày).
- **Tự sao lưu**: khi có thay đổi (chờ 2 phút) · mỗi giờ · mỗi 6 giờ · mỗi ngày (chọn giờ, máy tắt lúc đó thì bù sau 24 giờ) · tắt. Chạy ngầm khi app đang mở.
- **Khôi phục từ Drive**: chọn bản mới nhất hoặc bản theo ngày → gộp (thêm mục thiếu, bản sửa sau thắng, tải tệp thiếu). Máy mới: đăng nhập Google xong, kho trống → app hỏi khôi phục luôn.
- **Trên máy này**: xuất / nạp `.zip` (toàn bộ ghi chú + tệp), **Nạp từ app cũ** (chọn thư mục `PM-Kho-Lanh`; đọc `app/config/luu-tru/<tài khoản>/`, chỉ đọc).

## Việc cần làm 1 lần trên Google (OAuth client)
Google bắt mỗi phần mềm máy tính có 1 "OAuth client" riêng (miễn phí):
1. Vào https://console.cloud.google.com → tạo dự án (vd "Kho Lanh").
2. **APIs & Services › Library** → bật **Google Drive API**.
3. **OAuth consent screen**: User type **External**, điền tên app + email; ở **Test users** thêm Gmail của từng người dùng
   (hoặc bấm **Publish app** để mọi Gmail đăng nhập được — Google sẽ báo "chưa xác minh", bấm Nâng cao › Tiếp tục).
4. **Credentials › Create credentials › OAuth client ID** → loại **Desktop app** → chép **Client ID** + **Client secret**.
5. Trong app: Lưu trữ cá nhân › Sao lưu & Google › *Cài đặt kết nối Google* → dán 2 mã → **Lưu** (mỗi máy 1 lần).
   Máy đã dùng **Drive cá nhân của app cũ**: bấm **Lấy từ app cũ…** để lấy Client ID (secret app cũ bị mã hóa → dán lại secret).

Mã nguồn: `src/KhoLanh.Core/LuuTru/` (`LtStore` kho + định dạng data.json app cũ, `LtGoogle` OAuth loopback PKCE + Drive REST, `LtConfig` lịch, `LuuTruService`),
giao diện `src/KhoLanh.App/Views/LuuTruView.cs`, test `tests/KhoLanh.Core.Tests/LuuTruTests.cs` (Google Drive giả lập).
