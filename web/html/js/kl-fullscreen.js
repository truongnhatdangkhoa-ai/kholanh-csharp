// PC 2.11 — NÚT "TOÀN MÀN HÌNH" DÙNG CHUNG. PC 1.41: ĐỔI CÁCH HOẠT ĐỘNG TOÀN APP —
//   (1) CHỈ BUNG BẢNG TRONG KHUNG MODULE: không còn gọi Fullscreen API (trước đây phóng cả màn hình máy tính, ẩn luôn menu app).
//       Khung được chọn phủ kín vùng module bằng position:fixed (lớp kl-fs-cover) — menu trái / thanh trên của app vẫn giữ nguyên.
//   (2) NÚT CHỈ CÓ BIỂU TƯỢNG, KHÔNG CÓ CHỮ (rê chuột mới hiện gợi ý "Mở rộng bảng" / "Thu nhỏ"). Mọi nút data-kl-full, kể cả nút
//       vẽ lại (innerHTML) về sau, tự được đổi sang biểu tượng — không cần sửa từng trang.
// Cách dùng: nút bất kỳ có thuộc tính data-kl-full="<bộ chọn CSS của khung cần phóng to>". Bấm lại nút, hoặc phím Esc, để thu nhỏ.
// data-kl-full-mode="cover" giữ để tương thích (mọi nút nay đều chạy kiểu phủ kín khung module).
// Hộp thoại (xem trước phiếu, lọc cột…) có z-index ≥ 50 nên vẫn hiện TRÊN khung đang phóng (khung z-index 45).
// Khung bị dựng lại (innerHTML) thì tự phủ lại; khung biến mất / bị ẩn (đổi tab, đổi menu con) thì tự thu nhỏ.
(function(){
  'use strict';
  var COVER = 'kl-fs-cover', FLEX = 'kl-fs-flex', coverSel = '', coverFlex = false;
  var IC_ON = '<svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M9 3H3v6M15 3h6v6M9 21H3v-6M15 21h6v-6"/></svg>';
  var IC_OFF = '<svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 9h6V3M21 9h-6V3M3 15h6v6M21 15h-6v6"/></svg>';
  function coverEl(){ return document.querySelector('.' + COVER); }
  function sync(){
    var cur = coverEl();
    document.querySelectorAll('[data-kl-full]').forEach(function(btn){
      var t = null; try{ t = document.querySelector(btn.getAttribute('data-kl-full')); }catch(e){ window.klLog && klLog('kl-fullscreen.js:19', e); }
      var on = !!(cur && t && cur === t), st = on ? '1' : '0';
      btn.classList.toggle('on', on);
      if(btn.getAttribute('data-kl-ic') !== st){ // chỉ vẽ lại biểu tượng khi đổi trạng thái → không kích hoạt vòng lặp quan sát
        btn.setAttribute('data-kl-ic', st); btn.classList.add('kl-fs-ico'); btn.innerHTML = on ? IC_OFF : IC_ON;
      }
      var tip = on ? 'Thu nhỏ về như cũ (Esc)' : 'Mở rộng bảng ra toàn màn hình';
      if(btn.title !== tip) btn.title = tip;
      btn.setAttribute('aria-label', tip);
    });
    var ex = document.querySelector('.kl-tf-exit');
    if(ex && !ex.getAttribute('data-kl-ic')){ ex.setAttribute('data-kl-ic', '1'); ex.innerHTML = IC_OFF; ex.title = 'Thu nhỏ (Esc)'; ex.setAttribute('aria-label', 'Thu nhỏ bảng về như cũ'); }
  }
  function coverOff(){
    document.querySelectorAll('.' + COVER).forEach(function(e){ e.classList.remove(COVER); e.classList.remove(FLEX); });
    document.documentElement.classList.remove('kl-cover-on'); coverSel = ''; coverFlex = false;
  }
  function enterCover(el, sel, flex){
    coverSel = sel; coverFlex = !!flex; el.classList.add(COVER); if(flex) el.classList.add(FLEX);
    document.documentElement.classList.add('kl-cover-on'); sync();
  }
  var coverTick = 0;
  function coverGuard(){
    if(!coverSel || coverTick) return;
    coverTick = requestAnimationFrame(function(){
      coverTick = 0; if(!coverSel) return;
      var t = null; try{ t = document.querySelector(coverSel); }catch(e){ window.klLog && klLog('kl-fullscreen.js:45', e); }
      if(!t || !t.getClientRects().length){ coverOff(); sync(); return; } // khung biến mất / bị ẩn → thu nhỏ
      if(!t.classList.contains(COVER)){ document.querySelectorAll('.' + COVER).forEach(function(e){ e.classList.remove(COVER); e.classList.remove(FLEX); }); t.classList.add(COVER); if(coverFlex) t.classList.add(FLEX); sync(); } // khung vừa được vẽ lại → phủ lại
    });
  }
  function exit(){ coverOff(); sync(); }
  function toggle(el, mode, sel){
    if(!el) return;
    if(coverEl() === el){ exit(); return; }
    if(coverEl()) coverOff();
    enterCover(el, sel || '', mode !== 'cover'); // nút kiểu cũ (không khai mode) cần ép display:flex để bảng chiếm hết khung
  }
  document.addEventListener('click', function(e){
    var btn = e.target.closest && e.target.closest('[data-kl-full]'); if(!btn) return;
    e.preventDefault();
    var sel = btn.getAttribute('data-kl-full'), t = null; try{ t = document.querySelector(sel); }catch(err){ window.klLog && klLog('kl-fullscreen.js:60', err); }
    toggle(t, btn.getAttribute('data-kl-full-mode'), sel);
  });
  document.addEventListener('keydown', function(e){ if(e.key === 'Escape' && coverEl()) exit(); });
  var syncTick = 0;
  function syncSoon(){ if(syncTick) return; syncTick = requestAnimationFrame(function(){ syncTick = 0; sync(); }); }
  function start(){
    sync();
    try{ new MutationObserver(function(){ coverGuard(); syncSoon(); }).observe(document.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ['style', 'class', 'hidden'] }); }catch(e){ window.klLog && klLog('kl-fullscreen.js:68', e); }
  }
  if(document.readyState !== 'loading') start(); else document.addEventListener('DOMContentLoaded', start);
  window.klFullscreen = { toggle: toggle, exit: exit, sync: sync };
})();
