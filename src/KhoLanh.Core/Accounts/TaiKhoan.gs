/**
 * Kho Lạnh (app C#) — DANH SÁCH TÀI KHOẢN DÙNG CHUNG cho mọi máy.
 * Dán TOÀN BỘ file này vào: Google Sheet › Tiện ích mở rộng › Apps Script (xóa hết mã cũ trước khi dán), rồi Lưu.
 * Triển khai: Triển khai › Tùy chọn triển khai mới › Ứng dụng web; Thực thi với tư cách: Tôi; Ai có quyền truy cập: Bất kỳ ai.
 *
 * Mật khẩu KHÔNG lưu dạng chữ: chỉ có mã băm PBKDF2-SHA256 (100.000 vòng) + muối ngẫu nhiên do phần mềm tạo.
 * Đừng sửa tay tab TaiKhoan — mọi thay đổi làm trong phần mềm (Hệ thống › Tài khoản).
 * Hai mã dưới đây do phần mềm tạo riêng cho bạn; đừng gửi file này cho người ngoài.
 */
var MA_DOC = '__MA_DOC__';           // máy có mã này: đọc danh sách tài khoản để đăng nhập
var MA_QUAN_TRI = '__MA_QUAN_TRI__'; // chỉ máy quản trị: được thêm / sửa tài khoản, phân quyền
var TAB = 'TaiKhoan';
var COT = ['username', 'ho_ten', 'vai_tro', 'dang_dung', 'quyen', 'mat_khau_bam', 'muoi', 'cap_nhat_luc', 'cap_nhat_boi', 'email_google']; // email_google: 1.70 (đăng nhập Google)

function doGet() {
  return out_({ ok: true, app: 'kholanh-taikhoan', v: 1 });
}

function doPost(e) {
  try {
    var req = JSON.parse((e && e.postData && e.postData.contents) || '{}');
    if (!req.token || req.token !== MA_DOC) return out_({ ok: false, error: 'Sai mã kết nối.' });
    var sh = tab_();
    if (req.action === 'list') return out_({ ok: true, users: read_(sh) });
    if (!req.adminKey || req.adminKey !== MA_QUAN_TRI)
      return out_({ ok: false, error: 'Máy này không có mã quản trị nên không được sửa tài khoản.' });
    var lock = LockService.getScriptLock();
    lock.waitLock(20000);
    try {
      if (req.action === 'save') { save_(sh, req.user || {}); return out_({ ok: true }); }
      if (req.action === 'replaceAll') {
        var list = req.users || [];
        if (!list.length) return out_({ ok: false, error: 'Danh sách trống.' });
        if (sh.getLastRow() > 1) sh.getRange(2, 1, sh.getLastRow() - 1, COT.length).clearContent();
        for (var i = 0; i < list.length; i++) save_(sh, list[i]);
        return out_({ ok: true });
      }
    } finally {
      lock.releaseLock();
    }
    return out_({ ok: false, error: 'Không có chức năng ' + req.action });
  } catch (err) {
    return out_({ ok: false, error: String(err && err.message || err) });
  }
}

function tab_() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var sh = ss.getSheetByName(TAB);
  if (!sh) {
    sh = ss.insertSheet(TAB);
    sh.getRange(1, 1, 1, COT.length).setValues([COT]).setFontWeight('bold');
    sh.getRange(1, 1, 1000, COT.length).setNumberFormat('@');
    sh.setFrozenRows(1);
  } else if (sh.getLastColumn() < COT.length) {
    // Sheet tạo bằng bản cũ: thêm tiêu đề cột mới (vd email_google) — dữ liệu cũ giữ nguyên.
    sh.getRange(1, 1, 1, COT.length).setValues([COT]).setFontWeight('bold');
    sh.getRange(1, 1, 1000, COT.length).setNumberFormat('@');
  }
  return sh;
}

function read_(sh) {
  var n = sh.getLastRow();
  if (n < 2) return [];
  var head = sh.getRange(1, 1, 1, COT.length).getValues()[0].map(String);
  var rows = sh.getRange(2, 1, n - 1, COT.length).getValues();
  var res = [];
  for (var i = 0; i < rows.length; i++) {
    var o = {};
    for (var j = 0; j < head.length; j++) o[head[j]] = rows[i][j] === null ? '' : String(rows[i][j]);
    if (o.username) res.push(o);
  }
  return res;
}

function save_(sh, u) {
  var name = String(u.username || '').trim();
  if (!name) throw new Error('Thiếu tên đăng nhập.');
  var row = COT.map(function (c) { return safe_(u[c]); });
  var n = sh.getLastRow();
  var at = -1;
  if (n >= 2) {
    var names = sh.getRange(2, 1, n - 1, 1).getValues();
    for (var i = 0; i < names.length; i++)
      if (String(names[i][0]).toLowerCase() === name.toLowerCase()) { at = i + 2; break; }
  }
  if (at < 0) at = Math.max(n, 1) + 1;
  var r = sh.getRange(at, 1, 1, COT.length);
  r.setNumberFormat('@');
  r.setValues([row]);
}

// Chữ bắt đầu bằng = + - @ sẽ bị Google Sheet hiểu là công thức → thêm dấu ' để giữ nguyên là chữ.
function safe_(v) {
  var s = v === undefined || v === null ? '' : String(v);
  return /^[=+\-@]/.test(s) ? "'" + s : s;
}

function out_(o) {
  return ContentService.createTextOutput(JSON.stringify(o)).setMimeType(ContentService.MimeType.JSON);
}
