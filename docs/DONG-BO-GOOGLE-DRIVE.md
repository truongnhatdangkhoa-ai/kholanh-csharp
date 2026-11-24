# Đồng bộ qua Google Drive (máy ở mạng khác, không cần VPS)

**Mô hình:** dữ liệu chính làm việc và sao lưu trên máy tính bình thường (**Máy chính**, có thể đang là máy chủ LAN hoặc máy đơn).
Máy ở mạng khác (**Máy xem từ xa**) tải dữ liệu về xem; nếu sửa thì thay đổi được gửi ngược lên Máy chính để gộp.
App không đăng nhập Google; nó chỉ đọc / ghi một **thư mục của Google Drive for Desktop** trên mỗi máy.

## Cài đặt
1. Cài **Google Drive for Desktop** trên cả hai máy, cùng một tài khoản Google (hoặc chia sẻ thư mục cho nhau). Chọn một thư mục, ví dụ `G:\My Drive\KhoLanh`.
2. **Máy chính:** menu *Hệ thống › Dữ liệu & sao lưu › Đồng bộ › Thư mục / Google Drive dùng chung* → vai **Máy chính** → chọn thư mục → **Lưu** → **Chép mã đồng bộ** (`KLDRV1-…`).
3. **Máy xa:** *Đồng bộ Google Drive* → vai **Máy xem từ xa** → chọn thư mục (cùng thư mục đã đồng bộ về máy) → dán mã đồng bộ → **Lưu**.
   Máy xa đang dùng LAN thì chuyển về “Máy đơn” trước.

## Gắn vai theo tài khoản (khuyên dùng)
Thay vì đặt vai từng máy bằng tay: *Đồng bộ Google Drive › Phân vai theo tài khoản* → Quản trị bật, tick các tài khoản **máy chính** (thường là Quản trị), **Lưu phân vai** (lưu trên Google Sheet tài khoản dùng chung, kèm mã đồng bộ nên không phải dán mã). Mỗi máy chỉ cần **chọn thư mục Drive một lần**.
- Tài khoản máy chính đăng nhập ở máy nào **trước** thì máy đó là **Máy chính**. Máy đăng nhập sau (kể cả cùng tài khoản) và mọi tài khoản khác là **Máy xem từ xa**.
- Chỉ một máy chính tại một thời điểm, nhờ tệp khóa `main.lock` trong thư mục Drive (mã hóa). Máy chính cập nhật khóa mỗi lượt đồng bộ; máy thứ hai thấy khóa còn hạn thì tự làm máy xa và báo ở màn hình đăng nhập.
- Máy chính **thoát phần mềm**: đưa bản cuối lên Drive rồi nhả khóa. Máy kia đăng nhập lại → thành máy chính: tải bản mới nhất, **gộp các thay đổi chờ gửi** đã làm ở máy xa (3 bên, như xung đột LAN) rồi làm chủ.
- Máy chính treo / mất điện: khóa hết hạn sau **15 phút**, hoặc Quản trị bấm **Chiếm quyền máy chính** (chỉ khi chắc máy cũ đã tắt).
- Giới hạn: hai máy đăng nhập đúng cùng lúc (trong lúc Google Drive chưa kịp đồng bộ khóa) có thể cùng nhận làm máy chính; máy chính thứ hai phát hiện khóa của máy kia ở lượt đồng bộ sau thì tạm dừng đưa dữ liệu lên (báo “cũng đang là máy chính”) — lúc đó đăng xuất ở một máy.

## Dùng ở công ty ban ngày, ở nhà ban đêm (bản 1.27)
Ban ngày 3 máy công ty dùng **Mạng LAN** (máy chủ + máy chủ dự phòng + máy con); máy chủ công ty bật đồng bộ Drive vai **Máy chính**.
Ban đêm máy công ty tắt; các **máy ở nhà** làm việc **độc lập** qua Drive.
- Mỗi máy ở nhà: *Đồng bộ Google Drive › Máy ở nhà & chu kỳ đồng bộ* → tick **Máy này ở NHÀ** → Lưu. Máy ở nhà luôn là máy xem từ xa, **không bao giờ tự nhận làm máy chính** (kể cả đăng nhập tài khoản máy chính). Chỉ khi máy công ty hỏng mới bấm “Chiếm quyền máy chính”.
- Chiều tắt app ở công ty: app đưa bản cuối lên Drive. Tối máy ở nhà mở app: tải bản đó về, sửa bình thường; thay đổi được gửi lên Drive theo chu kỳ và lúc tắt app.
- Sáng máy chủ công ty mở app: tự gộp việc làm ở nhà (trùng dòng → Quản trị chọn ở *Mạng LAN › Xung đột*), rồi máy ở nhà nhận bản đã gộp ở lần mở sau.
- Giới hạn (chấp nhận được vì ít khi nhiều máy nhà làm cùng đêm): các máy ở nhà **chưa thấy việc của nhau** cho tới khi máy công ty gộp vào sáng hôm sau.
- **Chu kỳ đồng bộ** chọn theo máy: 45 giây / 5 / 15 / 30 / 60 phút, hoặc chỉ khi mở app / bấm “Đồng bộ ngay” / thoát app. Mặc định 5 phút.
- **Quyền “Gửi dữ liệu từ máy ở nhà (Drive)”**: tài khoản không có quyền này thì máy ở nhà chỉ xem, không sửa được (để không ai gõ cả buổi mà không gửi đi được).

## Sao lưu & khôi phục qua Drive
- Máy chính **tự sao lưu toàn bộ dữ liệu lên Drive mỗi ngày** (`KhoLanh-Sync/backup/b-yyyy-mm-dd.kldb`, nén + mã hóa bằng mã đồng bộ). Giữ 14 ngày gần nhất + bản đầu mỗi tháng trong 12 tháng. Có nút “Sao lưu lên Drive ngay”.
- **Máy công ty (LAN):** không khôi phục trên máy con — luôn lấy dữ liệu của máy chủ. Khôi phục máy chủ: tải bản đã chọn về file `.db` (nút “Tải bản đã chọn về file .db…”) rồi khôi phục ở *Sao lưu và cài đặt* (Quản trị).
- **Máy ở nhà:** “Khôi phục máy này từ bản đã chọn” (quyền *Sao lưu và cài đặt*). Chỉ ảnh hưởng máy đó: giữ nguyên cài đặt máy (thư mục, mã đồng bộ…), bỏ thay đổi chưa gửi (bản hiện tại vẫn được lưu trong SaoLuu), lần đồng bộ tới tải bản mới nhất của máy chính. Không kéo dữ liệu chung về bản cũ.
- Khi chuyển lên máy chủ online 24/7: máy ở nhà chỉ đổi nguồn sang máy chủ online; sao lưu vẫn giữ trên Drive làm dự phòng.

## Cách hoạt động
- Máy chính, theo chu kỳ (mặc định 5 phút): nếu dữ liệu đổi thì chụp toàn bộ bảng dữ liệu → `KhoLanh-Sync/snap/s-NNNNNN.klsync` + `latest.json` (giữ 4 bản gần nhất).
- Máy xa, theo chu kỳ: thấy bản mới → tải về và thay các bảng (bảng nào máy xa đang sửa dở thì giữ nguyên).
- Máy xa sửa dữ liệu → vào **sổ chờ** → gửi `in/<máy>-….klsync`. Máy chính gộp bằng đúng cơ chế của máy con LAN: không ai đụng nhau thì lấy thẳng,
  khác dòng thì tự gộp, trùng dòng thì ghi vào **Xung đột** để Quản trị chọn (Hệ thống › Dữ liệu & sao lưu › Đồng bộ › Máy chủ LAN). Kết quả trả về `out/<máy>.klsync`; máy chính chụp lại và máy xa nhận bản đã gộp.
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
