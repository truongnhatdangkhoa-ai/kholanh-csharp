# Dùng chung dữ liệu qua mạng LAN

Từ bản 1.8.0, nhiều máy trong cùng mạng nội bộ dùng chung dữ liệu hai module Tồn kho An An và Tồn kho gửi.
Cài ở **Hệ thống › Dữ liệu & sao lưu › Đồng bộ › Máy chủ LAN** (chỉ Quản trị).

![Mạng LAN](anh/15-mang-lan.png)

## Các vai

| Vai | Làm gì |
|---|---|
| Máy đơn | Không dùng LAN (mặc định). |
| Máy chủ chính | Giữ database chính, mở cổng 47820 cho các máy khác. |
| Máy dự phòng | Chép mọi thay đổi của máy chủ mỗi giây. Máy chủ tắt khoảng 10 giây thì tự lên làm máy chủ. |
| Máy con | Đọc và ghi thẳng vào máy đang làm máy chủ, giữ một bản sao trong máy. |

## Phân vai theo tài khoản (từ bản 1.18.0, khuyên dùng)

Quản trị gán vai cho **tài khoản**, mọi máy tự nhận vai khi đăng nhập. Người dùng không phải cài gì, không phải chép mã LAN.

![Phân vai theo tài khoản](anh/15b-mang-lan-theo-tai-khoan.png)

1. Bật **Tài khoản dùng chung (Google Sheet)** trước (xem [TAI-KHOAN-DUNG-CHUNG.md](TAI-KHOAN-DUNG-CHUNG.md)). Apps Script đang dùng **không phải dán lại**: phân vai được lưu ở một dòng riêng `#mang-lan` trong tab TaiKhoan (đừng sửa tay dòng này).
2. Trên máy quản trị (máy có mã quản trị), mở **Hệ thống › Dữ liệu & sao lưu › Đồng bộ › Máy chủ LAN**, tick **Bật: máy tự nhận vai theo tài khoản đăng nhập**, chọn vai cho từng tài khoản rồi bấm **Lưu phân vai**:
   - **Máy chủ chính**: thường là tài khoản Quản trị trên máy luôn bật.
   - **Máy dự phòng**: ví dụ tài khoản Quản lý A.
   - Không gán (**Máy con**): mọi tài khoản còn lại.
3. Đăng nhập tài khoản được gán Máy chủ chính trên máy chủ: máy đó thành máy chủ và tự ghi địa chỉ + khóa LAN lên Google Sheet (máy chủ cần có mã quản trị để ghi). Các máy khác đăng nhập là tự nối, không cần mã LAN.

Quy tắc:

- **Vai gắn theo máy.** Máy đã là máy chủ / dự phòng thì giữ vai dù ai đăng nhập trên máy đó. Chỉ có **một** máy chủ: tài khoản Quản trị đăng nhập ở máy khác thì máy đó vẫn là máy con.
- **Đổi máy chủ:** đăng nhập Quản trị trên máy mới, bấm **Chuyển máy chủ về máy này**. Máy chủ cũ trả vai sau vài giây (khi máy mới đã chép đủ dữ liệu) và thành máy con. Không mất dữ liệu.
- **Đổi máy dự phòng:** bấm **Bỏ máy dự phòng**; lần đăng nhập tới của tài khoản được gán dự phòng (trên máy mới) sẽ nhận vai.
- **Không nối được máy chủ và máy dự phòng:** máy con làm việc như máy đơn trên dữ liệu trong máy, mọi lần lưu vào sổ chờ; nối lại thì tự gửi lên và gộp (xem phần Khi máy chủ tắt). Mất Internet thì dùng địa chỉ máy chủ đã lưu từ lần trước.
- Máy đơn có dữ liệu riêng khi chuyển thành máy con / dự phòng: phần mềm tự sao lưu database trước (thư mục SaoLuu, file `truoc-doi-vai-…`).
- Máy chủ nên là máy luôn bật, đặt IP cố định nếu được (máy chủ đổi IP thì tự ghi lại địa chỉ mới lên Google Sheet trong vòng 2 phút; máy con nhận ở lần đăng nhập sau).

Khi đang bật phân vai theo tài khoản, phần **Vai của máy này (cài tay)** bị khóa.

## Cài đặt tay (khi không dùng phân vai theo tài khoản)

1. Mọi máy bật **Tài khoản dùng chung (Google Sheet)** để máy chủ nhận ra người dùng (xem [TAI-KHOAN-DUNG-CHUNG.md](TAI-KHOAN-DUNG-CHUNG.md)).
2. Máy chủ chính: chọn **Máy chủ chính**, bấm **Lưu và mở lại phần mềm**. Nếu Windows hỏi tường lửa, chọn **Cho phép** (mạng riêng). Bấm **Chép mã LAN**.
3. Máy dự phòng (nên có một máy luôn bật): chọn **Máy dự phòng**, dán mã LAN, lưu.
4. Các máy còn lại: chọn **Máy con**, dán mã LAN, lưu. Bộ cài cho máy mới (Sao lưu và cài đặt) tạo trên máy chủ chính đã kèm mã LAN, máy mới tự làm máy con.

Thanh menu hiện trạng thái LAN: chấm **xanh** là đang nối, chấm **cam** là đang làm riêng (mất máy chủ) hoặc đang chuyển máy chủ.

## Hai người cùng sửa một bảng

Mỗi bảng có số phiên bản. Người lưu sau, nếu bảng đã bị máy khác sửa từ lúc mở, sẽ được báo bấm **Làm mới (F5)** rồi làm lại. Không ai ghi đè dữ liệu của người khác.

## Khi máy chủ tắt

- **Có máy dự phòng:** sau khoảng 10 giây máy dự phòng lên làm máy chủ (khi quá nửa số máy con cũng báo mất máy chủ). Các máy con tự chuyển sang. Máy chủ chính bật lại thì chép phần thiếu rồi nhận lại vai.
- **Không máy nào làm chủ được:** mỗi máy làm tiếp trên dữ liệu của mình, mọi lần lưu ghi vào **sổ chờ** (kèm bản gốc lúc bắt đầu sửa). Có máy chủ lại thì tự gửi lên:
  - chỉ một bên sửa bảng: lấy luôn bản đã sửa;
  - hai bên sửa khác dòng: tự gộp (vẫn ghi lại để Quản trị xem, có nút **Hoàn tác gộp**);
  - hai bên sửa cùng một dòng, hoặc một bên nạp lại cả bảng: giữ bản máy chủ, bản của máy con nằm trong danh sách **Trùng sửa** để Quản trị chọn **Dùng bản máy con** hoặc **Giữ bản máy chủ**.
- **Hai máy cùng làm máy chủ** (đứt mạng giữa hai nhóm): khi thấy nhau, máy có nhiệm kỳ thấp hơn nhường vai và gửi phần đã sửa lên để gộp như trên.

Lời khuyên: trong lúc mất máy chủ, mỗi module chỉ nên để một máy nạp file.

## Lưu ý

- Khôi phục từ file sao lưu chỉ làm được ở Máy đơn (chuyển về Máy đơn, khôi phục, rồi cài lại vai).
- Đổi địa chỉ IP của máy chủ chính thì chép lại mã LAN cho các máy (máy con vẫn nhớ địa chỉ máy dự phòng).
