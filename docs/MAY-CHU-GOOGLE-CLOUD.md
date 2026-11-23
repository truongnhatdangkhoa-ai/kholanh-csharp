# Máy chủ LAN trên Google Cloud (VPS)

Cho các kho ở nhiều nơi dùng chung dữ liệu qua Internet. Máy chủ chạy **không giao diện** (`kholanh-server`, Linux), dùng lại đúng phần
LAN của app; các máy trong kho vẫn là app Windows bình thường, chỉ đổi địa chỉ máy chủ.

## An toàn đường truyền
- Kênh LAN **mã hóa AES-256-GCM** bằng khóa LAN (bắt tay `KLE1`, khóa phiên riêng mỗi kết nối, chống đọc lén / sửa / phát lại). Không cần chứng chỉ.
- `kholanh-server` mặc định **bắt buộc mã hóa** (từ chối máy con bản cũ chưa mã hóa). Cần tạm cho máy cũ nối thì thêm `--allow-plain`, cập nhật xong thì bỏ.
- Khóa LAN là bí mật chung: ai có mã LAN đều nối được. Đừng gửi mã qua kênh công khai; đổi mã nếu lộ.
- Nên giới hạn firewall chỉ cho IP các kho (hoặc đặt sau VPN WireGuard / Tailscale).

## 1. Tạo VM (Google Compute Engine)
- Máy `e2-small` (2 GB RAM), Ubuntu 22.04/24.04, đĩa 20 GB, vùng **asia-southeast1 (Singapore)**.
- **Địa chỉ IP tĩnh** (VPC network → IP addresses → Reserve), gán cho VM.
- Firewall (VPC network → Firewall): cho phép TCP **47820**, nguồn = IP công khai các kho (không để 0.0.0.0/0 nếu tránh được).

## 2. Cài đặt
Trên máy dev (có .NET 8 SDK):
```bash
dotnet publish src/KhoLanh.Server -c Release -r linux-x64 --self-contained -o publish
scp -r publish/* USER@IP-VPS:/tmp/kholanh/
```
Trên VPS:
```bash
sudo useradd -r -s /usr/sbin/nologin kholanh
sudo mkdir -p /opt/kholanh /var/lib/kholanh /etc/kholanh
sudo cp -r /tmp/kholanh/* /opt/kholanh/ && sudo chmod +x /opt/kholanh/kholanh-server
sudo chown -R kholanh:kholanh /var/lib/kholanh
sudo cp deploy/server.env.example /etc/kholanh/server.env && sudo chmod 600 /etc/kholanh/server.env
sudo nano /etc/kholanh/server.env          # đặt KHOLANH_PUBLIC = IP-tĩnh:47820
sudo cp deploy/kholanh-server.service /etc/systemd/system/
sudo systemctl daemon-reload && sudo systemctl enable --now kholanh-server
journalctl -u kholanh-server -f             # phải thấy "đang phục vụ"
```
(Hoặc dùng Docker: `deploy/Dockerfile`, chạy `docker run -d -p 47820:47820 -v kholanh:/data -e KHOLANH_PUBLIC=IP:47820 kholanh-server`.)

## 3. Lấy mã LAN cho máy trong kho
```bash
sudo -u kholanh env $(cat /etc/kholanh/server.env | xargs) /opt/kholanh/kholanh-server --show-code
```
In ra một dòng `KLLAN1-…` (chứa khóa bí mật). Trên máy kho: app → **Mạng LAN** → vai **Máy con** → dán mã. Hoặc đặt vào `ket-noi.txt` cạnh file cài.

## 4. Chuyển dữ liệu hiện có lên VPS
Máy chủ ở kho đang giữ dữ liệu. Cách an toàn:
1. Trên máy chủ kho: dùng **Sao lưu** của app → file `.db`.
2. Trên VPS: dừng `kholanh-server`, chép file đó thành `/var/lib/kholanh/kho.db` (chủ sở hữu `kholanh`), chạy lại. Khóa LAN cũ nằm trong file nên **mã LAN giữ nguyên khóa**, chỉ địa chỉ đổi thành địa chỉ VPS (`--show-code` in mã mới).
3. Các máy kho dán mã LAN mới (vai **Máy con**). Máy kho từng là chủ chuyển sang **Máy con** hoặc **Máy dự phòng**.
Thử với một máy trước, kiểm tra dữ liệu rồi mới chuyển hết.

## 5. Máy dự phòng
Có thể chạy thêm một `kholanh-server --role duphong --code KLLAN1-…` (VM thứ hai, hoặc một máy trong kho). Máy chủ chính chết thì máy dự phòng tự lên thay.

## 6. Sao lưu và theo dõi
- `deploy/backup.sh` (cron mỗi đêm, giữ 14 bản, tùy chọn đẩy lên Cloud Storage). **Thử khôi phục thật một lần** (dừng dịch vụ → chép lại `kho.db` → chạy).
- Log: `journalctl -u kholanh-server`. Dịch vụ tự khởi động lại khi chết (`Restart=always`).
- Cảnh báo chết máy: Cloud Monitoring → Uptime check TCP cổng 47820, gửi email.

## 7. Cập nhật phiên bản
1. Build lại, chép đè `/opt/kholanh`, `sudo systemctl restart kholanh-server`.
2. Cập nhật app trên các máy kho. Máy con bản cũ nối vào máy chủ yêu cầu mã hóa sẽ bị từ chối — cập nhật máy con trước, hoặc tạm `--allow-plain`.

## Giới hạn hiện biết
- Tài khoản đăng nhập vẫn dùng đồng bộ qua Google Sheet như trước; máy chủ không giao diện **không** đăng nhập, chỉ giữ và phân phối dữ liệu.
- Quyền Quản trị xử lý xung đột ghi (Resolve) phải làm từ app của Quản trị, không phải từ máy chủ không giao diện.
