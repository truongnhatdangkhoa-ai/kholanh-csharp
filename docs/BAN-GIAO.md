# Biên bản bàn giao — app Kho Lạnh C# (23/11/2026)

Phiên trước dừng giữa chừng vì hết token. Tài liệu này giúp phiên sau làm tiếp mà không phải hỏi lại người dùng.

## 1. Mục tiêu

Người dùng giao: "Viết lại Tồn kho An An và Tồn kho gửi bằng C# để bỏ hẳn WebView2, đây là việc lớn nhất."

Yêu cầu khi làm:
- Giao diện Avalonia, bảng kiểu Excel (SheetGrid).
- Giữ nguyên toàn bộ nghiệp vụ và số liệu của 2 module.
- Dùng chung database của app cũ, giữ phân quyền và dữ liệu.
- So kết quả với bản WebView2 trên dữ liệu thật.
- Phát hành từng module: An An trước (bản 1.20.0), rồi Tồn kho gửi.
- Chỉ bỏ WebView2 khi cả 2 module đã khớp.

## 2. Nhánh và commit

| Nơi | Commit | Nội dung |
|---|---|---|
| `main` | `684745a` | Lõi An An bằng C#. Đã build, test đạt, đã so với bản cũ. Chưa phát hành. |
| `wip/ton-kho-an-an-csharp` | commit cuối của nhánh | Giao diện An An (`AnAnView`). Đang dở, chưa đưa vào `main`. |

- Phiên bản hiện tại: `1.19.1` (Directory.Build.props). Bản kế tiếp: `1.20.0`.
- Phát hành: tăng `<Version>` rồi push lên `main`; job CI `phat-hanh` tự tạo release.

## 3. Trạng thái từng việc

### Đã xong
- Sửa lỗi tạo tài khoản ("máy này chưa mở được khóa ghi"), phát hành 1.19.1. Người dùng đang thử trên máy thật.
- Lõi Tồn kho An An, thư mục `src/KhoLanh.Core/TonKho/`:
  - `JsText.cs`: các hàm chữ và số chép đúng từ JS. Gồm toLocaleString en-US, Number(), so khớp tên cột, sắp xếp kiểu `vi`.
  - `SheetText.cs`, `XlsBiff.cs`: đọc file xlsx, xls (ExcelDataReader) và csv, ra chữ giống SheetJS.
  - `XlsxOut.cs`: ghi Excel giống SheetJS về font, độ rộng cột và SUBTOTAL.
  - `AnAnData.cs`: prepareSheet, phân loại 8 bảng, điều kiện tự thêm, trọng lượng, Báo Cáo, HLSO, đối soát, Đặc Tính.
  - `AnAnStore.cs`: đọc và ghi các sheet của database `ton-kho-an-an`, đúng định dạng app cũ.
  - `AnAnExport.cs`: 15 loại file Excel xuất ra.
  - `TheoDoiTon.cs`: tab Theo Dõi Tồn (ảnh chụp tồn theo tháng, ghi chú).
  - `SoSanhNap.cs`: so sánh 2 lần nạp, sheet `LanNapTruoc`.
  - `NapExcel.cs`:
    - Ngày số liệu (`DataDate`).
    - Kiểm tra file trước khi nạp (`CheckM1`; có sẵn `Num` dùng cho M2).
    - Nhật ký nạp, sheet `NapLog`.
    - `NapUndoStore`: lưu bản hoàn tác trên máy, trong thư mục `nap` cạnh `kho.db`. App cũ dùng `napAPI`, phía C# trước đây chưa có.
  - `AnAnService.cs`, đăng ký thành `App.Services.AnAn`:
    - Nạp file: `Preview`, `Commit`, `SwitchSheet`.
    - Lịch sử và hoàn tác: `History`, `CanUndo`, `UndoNap`.
    - Sửa dữ liệu: ngày số liệu, chuyển dòng thủ công, điều kiện lọc, Đặc Tính.
    - Kho Gửi tự lấy từ Tồn kho gửi (`SyncKhoGui`).
    - Phiếu Xuất Hàng: tạo, trừ, hủy trừ.
    - Quyền: `m1`. Mức Sửa mới được ghi.
  - Test: `tests/KhoLanh.Core.Tests/AnAnTests.cs`, 6 test, đều đạt.
    - Test `Real_data_matches_old_app_when_available` chỉ chạy khi máy có file riêng (đường dẫn ở mục 6). Máy khác thì test tự bỏ qua.
- Đã so với bản WebView2 trên dữ liệu thật, tất cả khớp:
  - Load Data: 3845 dòng, 0 ô khác.
  - Các file xuất 01, 02, 04–15: khớp hết. File 13 chỉ khác numFmt rỗng do ClosedXML, Excel hiển thị như nhau.
  - Các sheet `LanNapTruoc`, `NapLog`, `DacTinh`, `TheoDoiTon`.
  - Kho Gửi = 983,086.6.
  - Dấu vân tay (sha256) của file nạp.

### Đang dở (nhánh `wip/ton-kho-an-an-csharp`)
- `src/KhoLanh.App/Views/AnAnView.cs` đã build được, có 6 tab:
  - Load Data: nạp file, xem trước, lịch sử nạp và hoàn tác, so sánh 2 lần nạp, ngày số liệu, đổi sheet.
  - Phân Loại: 8 bảng, ngưỡng của Thành Phẩm Chính, chuyển dòng, điều kiện lọc.
  - Báo Cáo: tổng tồn, đối soát, HLSO, các nút xuất.
  - Theo Dõi Tồn: gõ ghi chú, xác nhận ghi chú.
  - Đặc Tính: gộp và bỏ gộp nhóm.
  - Tổng Hợp: Xuất Hàng, Báo cáo ngoài sổ sách.
- Đã chụp màn hình không cần màn hình thật (headless) với dữ liệu thật: 6 tab hiện đúng số.
- `MainWindow.axaml.cs`:
  - Mục `m1` mở `AnAnView`.
  - Thêm mục tạm "Tồn kho An An (bản cũ)" (khóa `m1cu`, xét theo quyền `m1`) để đối chiếu.
- `Ui.OpenFile` nay nhận nhiều đuôi file, ví dụ `"xlsx;xls;csv"`.
- Chưa kiểm xong:
  - Thao tác gõ SL xuất rồi "Tạo phiếu xuất", và gõ ghi chú Theo Dõi Tồn, trên giao diện. Script chụp ảnh lỗi do tab con Tổng Hợp đang ở "Báo cáo ngoài sổ sách" nên không thấy nút. Đây là lỗi của script, không phải lỗi app. Phần lõi của 2 việc này đã có test đạt.
  - Hộp thoại (xem trước khi nạp, lịch sử nạp, so sánh 2 lần nạp) chưa chạy thử bằng tay.

### Chưa làm (theo thứ tự)
1. Hoàn thiện giao diện An An:
   - Chạy thử các thao tác ở trên.
   - Merge nhánh `wip` vào `main`.
   - Tăng lên 1.20.0, phát hành, báo người dùng thử trên máy thật.
2. Tồn kho gửi (m2) bằng C#:
   - Đọc module cũ `ton-kho-gui.js`. Database `ton-kho-gui` có các sheet DuLieuChinh, DuLieuMeta, FileNap, NapLog, PhieuXuat, PhieuXuatItems, DacTinh_*, DanhSach_Nhom.
   - Viết lõi, rồi so với bản cũ. Bộ chạy bản cũ ở `/home/user/oracle/run.js` (cách chạy ở mục 5), file thật là `TON KHO GUI 21.11.xls`.
   - Làm giao diện, rồi phát hành.
3. Bỏ WebView2: bỏ mục "bản cũ", bỏ `ModuleView` của m1/m2 và các trang html, khi cả 2 module đã khớp.
4. Nhận phiếu từ điện thoại (m7).
5. Đối chiếu kiểm kê (m9): thêm phần tồn kho gửi và cột phát sinh trong file Excel.
6. Đối chiếu hàng ngày (m5): Nạp tạm, đọc file .xls (dùng lại `XlsBiff`).
7. Phiếu nhập và phiếu xuất nội địa (m13): chờ người dùng gửi mẫu Excel.

## 4. Quyết định đã chốt với người dùng
- Không sửa, push hay mở PR vào các repo cũ (`kholanh-anan-pc`, `kholanh-anan-mobile`). Chỉ đọc.
- Module mới phải đọc được database của app cũ. Nếu chưa đọc trực tiếp được thì phải có cách nạp vào, và hướng dẫn người dùng.
- LAN: "Giữ vai theo máy".
- Đối chiếu hàng ngày: nạp hoặc dán file kế toán.
- Kháng sinh: có phần tra cứu kháng sinh.
- Được push thẳng lên `main`. Phát hành bằng cách tăng Version.
- File PDF và dữ liệu thật của người dùng chỉ dùng để thử, không đưa lên GitHub.
- Không đóng thread: người dùng tự thử trên máy thật.

## 5. Build, test, chạy
```
dotnet build src/KhoLanh.App -c Release
dotnet test tests/KhoLanh.Core.Tests -c Release --filter "FullyQualifiedName~AnAnTests"
dotnet run --project tools/Screenshots -- <thư mục ảnh>   # chụp màn hình headless (dữ liệu mẫu)
```
- Bộ chạy bản WebView2 để lấy kết quả mẫu: `node /home/user/oracle/run.js`. Kết quả nằm ở `/home/user/cmp/old-anan/` (các file 01–15 và `db.json`).
- Test `LanTests.Standby_takes_over` đôi khi lỗi ngẫu nhiên trên CI. Chạy lại job lỗi:
  `gh api -X POST repos/truongnhatdangkhoa-ai/kholanh-csharp/actions/runs/{id}/rerun-failed-jobs`.
- Không bỏ qua test để CI xanh.

## 6. Lưu ý và lỗi đã biết
- File riêng, KHÔNG commit:
  - `/home/user/olddb/khokhanh.db`
  - `/home/user/oracle/seed.js`
  - `/home/user/mau-goc/*`
  - `/home/user/cmp/*`
  - file PDF thật của người dùng.
- Công cụ Write có thể đổi `̀`, ` `… trong code thành ký tự thật. Viết xong kiểm lại, thoát lại bằng python nếu cần.
- `JsonValue` tạo trong bộ nhớ từ `int` không đọc được bằng `TryGetValue<double>`. Dùng `AnAnService.Num(node)`.
- ClosedXML tự cộng khoảng 0.71 vào độ rộng cột; `XlsxOut` đã bù.
- `FindDuplicate` (trùng ngày số liệu) ở app cũ so ngày ISO với ngày dd/mm/yyyy nên không bao giờ báo. Bản C# so đúng dạng dd/mm/yyyy, nên sẽ báo khi trùng ngày.
- Máy con LAN khi máy chủ tắt: app cũ có hàng chờ phiếu (mã tạm PXT-…). Bản C# chưa có; phiếu ghi qua sổ chờ của LAN như các sheet khác.
- Chưa có giao diện cho bảng HLSO tùy chọn (`kl-custom-summary.js`), dù `AnAnExport.HlsoSummary` đã nhận tham số `custom`.
