/* PC 7.14 — CHẾ ĐỘ THEO VAI TRÒ trong từng module (iframe). Nạp ở CUỐI mỗi trang module.
   Vai trò (lấy từ main.js qua window.authAPI.state() → user.vaiTro):
     xem     = Người dùng cấp 1 — chỉ xem: chặn nút Thêm / Sửa / Xóa / Lưu / Nhập… và nút xuất file (Excel / PDF / PNG / In).
     xuat    = Người dùng cấp 2 — xem + xuất phiếu: chặn nút Thêm / Sửa / Xóa / Lưu / Nhập… (nút xuất / in vẫn dùng được).
     quanly / quantri = không chặn gì ở đây.
   ĐÂY CHỈ LÀ LỚP GIAO DIỆN (ẩn / chặn nút cho gọn, báo lý do). CHẶN THẬT việc GHI dữ liệu nằm ở main.js (PC_GUARD: db:write-sheet,
   db:write-sheets, phone:mark-deleted, sheets:pull-now) — cấp 1 / 2 gọi thẳng window.dbAPI.writeSheet cũng bị từ chối. */
(function(){
  'use strict';
  var api = window.authAPI;
  if(!api || typeof api.state !== 'function') return;
  var role = '';
  var NAME = { xem:'Người dùng cấp 1 — chỉ xem', xuat:'Người dùng cấp 2 — xem + xuất phiếu' };
  // Chữ trên nút → thao tác GHI (đầu chuỗi, không phân biệt hoa/thường). Cố ý chỉ bắt các động từ ghi rõ ràng.
  var WRITE_RE = /^[\s+＋✎✏🗑️🗑💾]*(thêm|them|sửa|sua|xóa|xoá|xoa|lưu|luu|nhập|nhap|import|cập nhật|cap nhat|chốt|chot|hoàn tác|hoan tac|khôi phục|khoi phuc|đổi tên|doi ten|dán|dan|áp dụng|ap dung|duyệt|duyet|gộp|gop|tách|tach|ghi |tạo mới|tao moi|đồng bộ|dong bo|kéo phiếu|keo phieu|save|add|edit|delete|remove|update)/i;
  // Cấp 1: thêm nút xuất file (Excel / PDF / PNG / In…) bị chặn.
  var EXPORT_RE = /(excel|\.xlsx|pdf|png|xuất file|xuat file|xuất excel|export|tải xuống|tai xuong|download|in phiếu|in phieu|in chứng từ|in chung tu|in báo cáo|in bao cao|^in$)/i;
  function label(el){
    var t = (el.value && (el.type === 'button' || el.type === 'submit') ? el.value : el.textContent) || '';
    t = String(t).replace(/\s+/g, ' ').trim();
    var extra = (el.getAttribute('title') || '') + ' ' + (el.getAttribute('aria-label') || '');
    return { text:t, all:(t + ' ' + extra).trim() };
  }
  function blocked(el){
    if(!role || role === 'quanly' || role === 'quantri') return '';
    var l = label(el);
    if(!l.text && !l.all) return '';
    if(l.text.length > 40) return ''; // vùng lớn bấm được, không phải nút
    if(WRITE_RE.test(l.text) || WRITE_RE.test(l.all)) return 'Bạn không được cấp quyền';
    if(role === 'xem' && (EXPORT_RE.test(l.text) || EXPORT_RE.test(l.all))) return 'Bạn không được cấp quyền';
    return '';
  }
  var SEL = 'button, input[type=button], input[type=submit], [role=button], a.btn, .btn';
  // PC 7.50: ô chọn file (nhập Excel), ô dán dữ liệu và kéo-thả file KHÔNG phải nút nên chữ-trên-nút ở trên không bắt được → cấp 1 / cấp 2 vẫn nhập được ở màn hình
  // (main.js chặn ghi thật nhưng dữ liệu vẫn hiện trên màn hình + localStorage). Cấp 2 chỉ được nhập ở Chứng từ xuất kho (module 6).
  var fileExempt = false;
  function isFileInput(el){ return !!el && el.tagName === 'INPUT' && String(el.type).toLowerCase() === 'file'; }
  function fileTargetOf(t){
    if(!t || !t.closest) return null;
    if(isFileInput(t)) return t;
    var lb = t.closest('label');
    if(lb){ var f = lb.control || lb.querySelector('input[type=file]'); if(isFileInput(f)) return f; }
    return null;
  }
  function isManualBox(el){ return !!el && el.tagName === 'TEXTAREA' && (/manual/i.test(el.id || '') || /manual/i.test(el.className || '')); }
  function fileWhy(){ return 'Bạn không được cấp quyền'; }
  function toast(t){
    var e = document.createElement('div');
    e.textContent = t;
    e.style.cssText = 'position:fixed;left:50%;bottom:28px;transform:translateX(-50%);z-index:2147483000;background:#1f2937;color:#fff;padding:10px 16px;border-radius:10px;font:600 13px/1.4 system-ui,sans-serif;box-shadow:0 8px 24px rgba(0,0,0,.35);max-width:80vw;text-align:center';
    document.body.appendChild(e); setTimeout(function(){ if(e.parentNode) e.parentNode.removeChild(e); }, 2800);
  }
  function onClick(ev){
    if(!role || role === 'quanly' || role === 'quantri') return;
    if(!fileExempt && fileTargetOf(ev.target)){ ev.preventDefault(); ev.stopPropagation(); ev.stopImmediatePropagation(); toast(fileWhy()); return; }
    var t = ev.target && ev.target.closest ? ev.target.closest(SEL) : null;
    if(!t) return;
    var why = blocked(t);
    if(!why) return;
    ev.preventDefault(); ev.stopPropagation(); ev.stopImmediatePropagation();
    toast(why);
  }
  var pending = false;
  function mark(){
    pending = false;
    var list = document.querySelectorAll(SEL);
    for(var i = 0; i < list.length; i++){
      var b = list[i];
      if(b.getAttribute('data-kla-ro-chk') === '1') continue;
      b.setAttribute('data-kla-ro-chk', '1');
      if(blocked(b)){ b.setAttribute('data-kla-ro', '1'); b.title = (b.title ? b.title + ' — ' : '') + 'Bạn không được cấp quyền'; }
    }
    if(!fileExempt){
      var fi = document.querySelectorAll('input[type=file]');
      for(var k = 0; k < fi.length; k++){ if(!fi[k].disabled){ fi[k].disabled = true; fi[k].title = 'Bạn không được cấp quyền'; } }
      var ta = document.querySelectorAll('textarea');
      for(var m = 0; m < ta.length; m++){ if(isManualBox(ta[m]) && !ta[m].readOnly){ ta[m].readOnly = true; ta[m].setAttribute('data-kla-ro', '1'); } }
    }
    // ô sửa trực tiếp (contenteditable) → khóa
    var ce = document.querySelectorAll('[contenteditable="true"]');
    for(var j = 0; j < ce.length; j++) ce[j].setAttribute('contenteditable', 'false');
  }
  function schedule(){ if(pending) return; pending = true; (window.requestAnimationFrame || setTimeout)(mark); }
  function start(r){
    role = r;
    if(role !== 'xem' && role !== 'xuat') return;
    var st = document.createElement('style');
    st.textContent = '[data-kla-ro="1"]{opacity:.45!important;cursor:not-allowed!important}'
      + '.kla-ro-badge{position:fixed;right:10px;bottom:8px;z-index:2147482000;padding:3px 10px;border-radius:999px;font:700 11px/1.5 system-ui,sans-serif;pointer-events:none;'
      + 'color:' + (role === 'xem' ? '#fde68a' : '#86efac') + ';background:rgba(0,0,0,.55);border:1px solid ' + (role === 'xem' ? 'rgba(253,224,71,.5)' : 'rgba(110,231,183,.55)') + ';box-shadow:0 0 9px ' + (role === 'xem' ? 'rgba(253,224,71,.35)' : 'rgba(74,222,128,.40)') + '}';
    document.head.appendChild(st);
    // PC 7.45: bỏ nhãn "Người dùng cấp N — …" ở góc dưới phải (đè lên góc dưới phải); vai trò đã hiện ở menu tài khoản.
    fileExempt = (role === 'xuat' && /chung-tu-xuat-kho/i.test(location.pathname || ''));
    if(!fileExempt){
      try{
        var origClick = HTMLInputElement.prototype.click;
        HTMLInputElement.prototype.click = function(){ if(isFileInput(this)){ toast(fileWhy()); return; } return origClick.apply(this, arguments); };
      }catch(e){ window.klLog && klLog('kla-role.js:98', e); }
      var hasFiles = function(ev){ try{ var ty = ev.dataTransfer && ev.dataTransfer.types; return !!ty && Array.prototype.indexOf.call(ty, 'Files') >= 0; }catch(e){ return false; } };
      ['dragover', 'drop'].forEach(function(n){
        document.addEventListener(n, function(ev){ if(!hasFiles(ev)) return; ev.preventDefault(); ev.stopPropagation(); ev.stopImmediatePropagation(); if(n === 'drop') toast(fileWhy()); }, true);
      });
      document.addEventListener('paste', function(ev){ if(isManualBox(ev.target)){ ev.preventDefault(); ev.stopImmediatePropagation(); toast(fileWhy()); } }, true);
    }
    document.addEventListener('click', onClick, true);
    try{ new MutationObserver(schedule).observe(document.documentElement, { childList:true, subtree:true }); }catch(e){ window.klLog && klLog('kla-role.js:106', e); }
    schedule();
  }
  api.state().then(function(r){
    var u = r && r.ok && r.user;
    if(u) start(u.vaiTro || u.quyen || '');
  }, function(){});
})();
