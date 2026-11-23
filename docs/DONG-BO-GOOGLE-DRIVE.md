# Đồng bộ qua Google Drive (máy ở mạng khác, không cần VPS)

**Mô hình:** dữ liệu chính làm việc và sao lưu trên máy tính bình thường (**Máy chính**, có thể đang là máy chủ LAN hoặc máy đơn).
Máy ở mạng khác (**Máy xem từ xa**) tải dữ liệu về xem; nếu sửa thì thay đổi được gửi ngược lên Máy chính để gộp.
App không đăng nhập Google; nó chỉ đọc / ghi một **thư mục của Google Drive for Desktop** trên mỗi máy.

## Cài đặt
1. Cài **Google Drive for Desktop** trên cả hai máy, cùng một tài khoản Google (hoặc chia sẻ thư mục cho nhau). Chọn một thư mục, ví dụ `G:\My Drive\KhoLanh`.
2. **Máy chính:** menu *Hệ thống › Đồng bộ Google Drive* → vai **Máy chính** → chọn thư mục → **Lưu** → **Chép mã đồng bộ** (`KLDRV1-…`).
3. **Máy xa:** *Đồng bộ Google Drive* → vai **Máy xem từ xa** → chọn thư mục (cùng thư mục đã đồng bộ về máy) → dán mã đồng bộ → **Lưu**.
   Máy xa đang dùng LAN thì chuyển về “Máy đơn” trước.

## Cách hoạt động
- Máy chính, mỗi ~45 giây: nếu dữ liệu đổi thì chụp toàn bộ bảng dữ liệu → `KhoLanh-Sync/snap/s-NNNNNN.klsync` + `latest.json` (giữ 4 bản gần nhất).
- Máy xa, mỗi ~45 giây: thấy bản mới → tải về và thay các bảng (bảng nào máy xa đang sửa dở thì giữ nguyên).
- Máy xa sửa dữ liệu → vào **sổ chờ** → gửi `in/<máy>-….klsync`. Máy chính gộp bằng đúng cơ chế của máy con LAN: không ai đụng nhau thì lấy thẳng,
  khác dòng thì tự gộp, trùng dòng thì ghi vào **Xung đột** để Quản trị chọn (Hệ thống › Mạng LAN). Kết quả trả về `out/<máy>.klsync`; máy chính chụp lại và máy xa nhận bản đã gộp.
- Máy xa mất mạng vẫn làm việc với bản đã tải; nối lại tự gửi.
- Tốc độ phụ thuộc Google Drive đồng bộ (vài giây đến vài phút) — **không tức thời**.

## Bảo mật
- Mọi tệp (trừ `latest.json` chỉ có số bản + dấu vân tay) **mã hóa AES-256-GCM** bằng “mã đồng bộ”: Google hay người khác trong thư mục chung không đọc / sửa được.
- Mã đồng bộ chứa khóa bí mật — chỉ đưa cho máy được phép. Muốn thu hồi: trên máy chính tắt rồi bật lại vai *Máy chính* sau khi xóa khóa (đặt `drive_key` rỗng) — hoặc liên hệ để thêm nút đổi khóa.
- Thay đổi từ máy xa được kiểm quyền theo **tài khoản đăng nhập** trên máy chính, nên cần tên tài khoản trùng (dùng Tài khoản dùng chung qua Google Sheet).

## Giới hạn
- Chỉ Máy chính giữ dữ liệu thật; máy xa là bản sao + sổ chờ. Hai máy xa sửa cùng một bảng thì máy chính gộp lần lượt.
- Không dùng thư mục này cho nhiều Máy chính cùng lúc.
- Thay đổi lớn (nạp file tồn nhiều MB) làm bản chụp nặng hơn; vẫn nén + mã hóa.
