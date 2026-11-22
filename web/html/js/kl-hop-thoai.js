/* PC 3.06 — HỘP THÔNG BÁO CHUNG THAY alert() GỐC.
   alert() gốc chặn cả app, không theo giao diện Sáng / Tối, không chép được nội dung. Nạp ngay sau kl-log.js ở mọi trang:
   • window.alert(msg) → hộp theo màu giao diện, KHÔNG chặn, chép được (nút «Sao chép»), nhiều thông báo liên tiếp xếp hàng trong 1 hộp.
   • Enter / Esc / bấm nền = đóng. Bản gốc vẫn dùng được qua window.klAlertNative(msg).
   • confirm() giữ nguyên (phải trả kết quả ngay) — chỗ nào cần xác nhận dùng showConfirmPopup của từng trang.
   Lõi tạo nội dung (klHopThoaFormat) tách riêng để kiểm thử trong Node. */
(function (root) {
  'use strict';
  function fmt(m) { return String(m == null ? '' : m).replace(/\r\n?/g, '\n').trim(); }
  root.klHopThoaFormat = fmt;
  if (typeof document === 'undefined' || root.klAlertNative || !document.createElement) return;
  root.klAlertNative = root.alert ? root.alert.bind(root) : function () {};
  var queue = [], ov = null;
  function esc(s) { return String(s).replace(/[&<>"]/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]; }); }
  function close() { if (ov && ov.parentNode) ov.parentNode.removeChild(ov); ov = null; document.removeEventListener('keydown', key, true); }
  function key(e) { if (e.key === 'Escape' || e.key === 'Enter') { e.preventDefault(); e.stopPropagation(); close(); } }
  function show() {
    if (!queue.length) return;
    var body = document.body || document.documentElement; if (!body) return;
    if (!ov) {
      ov = document.createElement('div'); ov.setAttribute('data-kl-hop', '1');
      ov.style.cssText = 'position:fixed;inset:0;z-index:2147483000;background:rgba(0,0,0,.42);display:flex;align-items:center;justify-content:center;font-family:Segoe UI,Arial,sans-serif;';
      ov.addEventListener('mousedown', function (e) { if (e.target === ov) close(); });
      body.appendChild(ov); document.addEventListener('keydown', key, true);
    }
    var items = queue.slice(), txt = items.join('\n\n— — —\n\n');
    ov.innerHTML = '<div style="background:var(--panel,var(--surface,#fff));color:var(--text,#111827);border:1px solid var(--border,#C9CDD3);border-radius:10px;padding:16px 18px;min-width:300px;max-width:min(560px,92vw);max-height:80vh;display:flex;flex-direction:column;box-shadow:0 12px 40px rgba(0,0,0,.35)">'
      + '<div style="font-weight:700;margin-bottom:8px">Thông báo' + (items.length > 1 ? ' (' + items.length + ')' : '') + '</div>'
      + '<div style="white-space:pre-wrap;overflow:auto;user-select:text;font-size:14px;line-height:1.5">' + esc(txt) + '</div>'
      + '<div style="display:flex;gap:8px;justify-content:flex-end;margin-top:14px"><button type="button" data-a="cp" style="padding:6px 12px;border-radius:6px;border:1px solid var(--border,#C9CDD3);background:transparent;color:inherit;cursor:pointer">Sao chép</button>'
      + '<button type="button" data-a="ok" style="padding:6px 16px;border-radius:6px;border:0;background:#2563eb;color:#fff;cursor:pointer">Đóng</button></div></div>';
    ov.querySelector('[data-a="ok"]').onclick = close;
    ov.querySelector('[data-a="cp"]').onclick = function () { try { navigator.clipboard.writeText(txt); this.textContent = 'Đã chép'; } catch (e) { /* bỏ qua */ } };
    try { ov.querySelector('[data-a="ok"]').focus(); } catch (e) { /* bỏ qua */ }
  }
  root.alert = function (msg) {
    try {
      var m = fmt(msg); if (!m) return;
      if (queue.length && queue[queue.length - 1] === m) return;   // cùng 1 thông báo lặp liền nhau
      if (!ov) queue = []; queue.push(m); if (queue.length > 6) queue.shift();
      if (document.body) show(); else document.addEventListener('DOMContentLoaded', show, { once: true });
    } catch (e) { try { root.klAlertNative(msg); } catch (e2) { /* bỏ qua */ } }
  };
})(typeof window !== 'undefined' ? window : globalThis);
