/* kla-pick-memory.js — PC 3.73: ô chọn file (<input type="file">) của MỖI MODULE nhớ thư mục riêng.
   Chromium dùng chung 1 vị trí "gần nhất" cho cả máy → chọn ổ C ở module 1 thì module 2 cũng mở ổ C. Ở đây ta chặn việc mở hộp chọn
   mặc định, nhờ tiến trình chính mở hộp chọn của Windows tại thư mục đã nhớ CỦA TRANG NÀY (core/pick-memory.js), đọc file rồi trả lại
   đúng vào ô như người dùng vừa chọn (đặt input.files + bắn input/change) → mã của từng module KHÔNG phải sửa.
   Không áp dụng (giữ hộp mặc định): chọn thư mục (webkitdirectory), chụp camera (capture), ô bị khóa, hoặc khi không có cầu nối (điện thoại / trình duyệt). */
(function () {
  'use strict';
  if (window.__klaPickMem) return; window.__klaPickMem = true;
  var origClick = HTMLInputElement.prototype.click;
  var origShow = HTMLInputElement.prototype.showPicker;
  var busy = new WeakSet(), pass = new WeakSet();

  function bridge() { return window.klaPickMem && typeof window.klaPickMem.open === 'function' ? window.klaPickMem : null; }
  function eligible(el) {
    return !!el && el.tagName === 'INPUT' && el.type === 'file' && !el.disabled && !el.webkitdirectory && !el.hasAttribute('capture') && !!bridge();
  }
  function native(el) { pass.add(el); try { origClick.call(el); } catch (e) { pass.delete(el); } }

  function pick(el) {
    if (busy.has(el)) return;
    busy.add(el);
    var sub = el.getAttribute('data-pick-key') || el.id || '';
    bridge().open({ accept: el.accept || '', multiple: !!el.multiple, sub: sub }).then(function (r) {
      if (!r) { native(el); return; }
      if (r.canceled) { try { el.dispatchEvent(new Event('cancel', { bubbles: true })); } catch (e) { /* bỏ qua */ } return; }
      if (r.ok === false) { alert(r.error || 'Không chọn được file.'); return; }
      var dt = new DataTransfer();
      (r.files || []).forEach(function (f) { dt.items.add(new File([f.bytes], f.name, { type: f.type || '', lastModified: f.mtime || Date.now() })); });
      if (!dt.files.length) return;
      el.files = dt.files;
      el.dispatchEvent(new Event('input', { bubbles: true }));
      el.dispatchEvent(new Event('change', { bubbles: true }));
    }).catch(function () { native(el); }).then(function () { busy.delete(el); });
  }

  // Bấm vào ô / nhãn <label for> (sự kiện click lan tới document). Dùng pha BUBBLE (không phải capture) để chạy SAU bộ chặn theo quyền của kla-role.js:
  // nếu tài khoản bị khóa nhập file, kla-role đã chặn click ở pha capture nên sự kiện không bao giờ tới đây. Hộp mặc định chỉ mở sau khi dispatch xong → preventDefault vẫn kịp.
  document.addEventListener('click', function (ev) {
    var el = ev.target;
    if (!el || el.tagName !== 'INPUT' || el.type !== 'file') return;
    if (pass.has(el)) { pass.delete(el); return; }
    if (ev.defaultPrevented || !eligible(el)) return;
    ev.preventDefault();
    pick(el);
  }, false);

  // input.click() / showPicker() gọi bằng mã — gồm cả ô tạo động chưa gắn vào trang (so-kho-ui: pickFiles…): sự kiện click không lan tới document nên chặn ở hàm.
  HTMLInputElement.prototype.click = function () {
    if (eligible(this) && !pass.has(this) && !this.isConnected) { pick(this); return; }
    return origClick.apply(this, arguments);
  };
  if (typeof origShow === 'function') {
    HTMLInputElement.prototype.showPicker = function () {
      if (eligible(this) && !pass.has(this)) { pick(this); return; }
      return origShow.apply(this, arguments);
    };
  }
})();
