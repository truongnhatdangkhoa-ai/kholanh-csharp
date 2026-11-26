# Máy chủ ở nhà, máy ở xa nối qua Internet (Cloudflare Tunnel)

Từ bản 1.113: một PC (ví dụ PC cá nhân ở nhà) làm **máy chủ chính** giữ database; các máy ở kho / văn phòng / nơi khác **chỉ cần Internet và app
KhoLanh, đăng nhập tài khoản như bình thường** — không cài thêm gì, không dán mã (địa chỉ máy chủ đi theo Google Sheet chung). Chỉ PC máy chủ cài `cloudflared` (đường hầm của Cloudflare, miễn phí).
App vẫn tự cập nhật từ GitHub như cũ.

```
Máy ở xa ──HTTPS──> kho.kcenter.xyz (Cloudflare) ──đường hầm──> cloudflared trên PC máy chủ ──> KhoLanh (localhost:47821)
Máy cùng mạng nhà ─────────────────────────────── nối thẳng TCP 47820 như trước ──────────────────────────> KhoLanh
```

## An toàn
- Gói tin vẫn **mã hóa AES-256-GCM bằng khóa LAN** (lưu trong Google Sheet chung, chỉ máy đăng nhập được mới có) — Cloudflare / mạng giữa đường không đọc, không sửa được; gửi lại gói cũ bị chặn.
- PC máy chủ **không mở cổng** trên modem; cổng 47821 chỉ nghe trong máy (127.0.0.1).
- Giờ trên các máy phải đúng (lệch quá 5 phút bị từ chối) — Windows mặc định tự chỉnh giờ.

## Bước 1 — Chuyển DNS kcenter.xyz từ Mắt Bão sang Cloudflare (làm 1 lần)
Tên miền vẫn mua ở Mắt Bão, chỉ đổi nơi quản lý DNS. Trang giới thiệu app vẫn chạy.
1. Vào https://dash.cloudflare.com → tạo tài khoản (miễn phí) → **Add a domain** → nhập `kcenter.xyz` → chọn gói **Free**.
2. Cloudflare quét và chép các bản ghi DNS đang có. **So lại với bản ghi ở Mắt Bão** (trang quản lý tên miền › Quản lý DNS): bản ghi của
   trang giới thiệu (thường A `185.199.108–111.153` hoặc CNAME `truongnhatdangkhoa-ai.github.io`), bản ghi email (MX, TXT) nếu có — thiếu thì thêm tay.
   Trang chạy trên GitHub Pages: để các bản ghi đó **DNS only** (đám mây xám).
3. Cloudflare đưa 2 nameserver (dạng `xxx.ns.cloudflare.com`). Vào Mắt Bão › quản lý tên miền › **Đổi Nameserver / DNS** → chọn dùng
   nameserver riêng → nhập 2 nameserver đó → lưu.
4. Chờ Cloudflare báo **Active** (thường vài giờ, chậm nhất ~24 giờ). Mở lại trang giới thiệu kiểm tra vẫn chạy.

## Bước 2 — Đặt PC cá nhân làm máy chủ chính
1. App KhoLanh trên PC (đăng nhập Quản trị): **Hệ thống › Đồng bộ & sao lưu › Giữa các máy › Mạng LAN trong kho › Máy chủ LAN** → **Bật máy chủ trên máy này**
   (máy đang làm máy chủ ở kho thì bấm *Chuyển máy chủ về máy này* — app tự chép đủ dữ liệu rồi đổi).
2. Windows: tắt chế độ ngủ (Settings › System › Power › Sleep = Never), bật "khởi động cùng Windows" trong app, nên cắm dây mạng + UPS.
3. Nên bật **Sao lưu Drive cá nhân** tự động trên máy này (Đồng bộ & sao lưu › Sao lưu).

## Bước 3 — Tạo đường hầm (sau khi DNS đã Active)
1. https://one.dash.cloudflare.com → (lần đầu chọn tên nhóm + gói **Free**; nếu được hỏi phương thức thanh toán thì gói Free vẫn 0 đồng)
   → **Networks › Tunnels** → **Create a tunnel** → loại **Cloudflared** → đặt tên `kholanh`.
2. Chọn hệ điều hành **Windows** → làm theo hướng dẫn: tải và cài `cloudflared`, rồi mở **Command Prompt (Run as administrator)** và dán
   lệnh `cloudflared.exe service install <mã>` mà trang đưa (mã này bí mật). Đường hầm thành **Healthy** là được; nó chạy như dịch vụ Windows, tự bật cùng máy.
3. **Public hostname** (Route traffic): Subdomain `kho` · Domain `kcenter.xyz` · Service **HTTP** · URL `localhost:47821`
   (số cổng xem ở app, khung *Máy chủ từ xa*, thường là 47821) → Save.
4. Kiểm tra: mở trình duyệt `https://kho.kcenter.xyz` → thấy chữ **KhoLanh OK**.

## Bước 4 — Khai báo trong app
1. PC máy chủ: Đồng bộ & sao lưu › Giữa các máy › Máy chủ LAN › khung **Máy chủ từ xa (qua Internet)** → ô *Địa chỉ từ xa* nhập `kho.kcenter.xyz`
   → **Lưu địa chỉ** (app ghi địa chỉ lên Google Sheet chung) → **Kiểm tra nối từ xa** (phải hiện ✓ … đang làm máy chủ).
2. Máy ở xa: mở app, **đăng nhập** như bình thường → tự nhận địa chỉ, tự nối qua Internet. Máy trong nhà vẫn nối thẳng qua mạng nhà.
3. Nên bật máy chủ dự phòng trên 1 máy trong kho (*Bật máy chủ trên máy này* với tài khoản được làm dự phòng): PC ở nhà tắt / mất mạng thì kho vẫn
   làm việc, PC bật lại tự gộp.

## Khi có sự cố
- Trợ lý hệ thống (máy chủ) tự thử nối qua địa chỉ từ xa mỗi lần quét; không được → cảnh báo **Nặng** "Máy ở xa không nối được máy chủ qua Internet".
- "đường hầm chạy nhưng máy chủ ở nhà không trả lời": PC đang mở nhưng app KhoLanh chưa mở / chưa làm máy chủ.
- "máy chủ từ chối": khóa LAN đã đổi (đăng nhập lại để nhận khóa mới) hoặc giờ máy lệch > 5 phút.
- Không mở được `https://kho.kcenter.xyz`: dịch vụ cloudflared tắt (Services › Cloudflared agent), DNS chưa Active, hoặc Public hostname sai cổng.
