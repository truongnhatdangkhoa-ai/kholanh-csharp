/* PC 4.19 — HÀM DÙNG CHUNG (chỉ những hàm mà các bản chép rời trong app có HÀNH VI GIỐNG HỆT nhau).
   Khảo sát 46 định nghĩa esc / escapeHtml rải ở các file: chia 2 nhóm giống hệt (đã so sánh kết quả trên cùng bộ chuỗi thử) → gom về đây; các bản còn lại KHÁC hành vi
   (vd null → "null" thay vì rỗng, hoặc dựng bằng DOM) nên KHÔNG gom — gom sẽ đổi kết quả hiển thị. Các hàm toast / fmt / num cũng không gom: mỗi bản gắn với 1 phần tử trang
   hoặc 1 quy tắc làm tròn khác nhau.
   Trong các file đã chuyển, bản cũ được giữ DƯỚI CÙNG TÊN nhưng chỉ gọi sang đây (giữ nguyên tên, phạm vi, thứ tự khai báo) — nên PHẢI nạp file này TRƯỚC các file đó
   (tests/kl-util.test.js kiểm từng trang). Thêm hàm mới vào đây chỉ khi đã kiểm hành vi các bản cũ giống nhau. */
(function (root) {
  'use strict';
  // NHÓM 1 — thoát đủ & < > " '  ; null / undefined → rỗng. (19 bản cũ)
  function esc(v) { return String(v == null ? '' : v).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); }
  // NHÓM 2 — thoát & < > " (KHÔNG thoát dấu nháy đơn) ; null / undefined → rỗng. (15 bản cũ)
  function escLite(v) { return String(v == null ? '' : v).replace(/[&<>"]/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]; }); }
  var api = { esc: esc, escLite: escLite };
  if (typeof module !== 'undefined' && module.exports) module.exports = api; else root.KLUtil = api;
})(typeof window !== 'undefined' ? window : globalThis);
