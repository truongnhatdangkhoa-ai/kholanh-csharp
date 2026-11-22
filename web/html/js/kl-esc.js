/* PC 4.21 — PHÍM ESC = QUAY LẠI MỘT BƯỚC, BƯỚC CUỐI LÀ TRANG TỔNG QUAN. Dùng chung cho MỌI trang (nạp ngay sau kl-log.js).
   Thứ tự mỗi lần bấm Esc (không kèm Ctrl / Alt / Shift):
   1) Trang đang có việc riêng cho Esc (đóng hộp thoại, menu chuột phải, thu nhỏ bảng toàn màn hình, hủy sửa ô…) → để trang tự xử lý, KHÔNG quay lại.
      Cách nhận biết chung (không cần sửa từng trang): sau khi bấm, trong 40 ms trang có đổi giao diện thật (thêm / bớt phần tử, đổi thuộc tính) hoặc đã gọi preventDefault → coi là Esc đã được dùng.
      Thay đổi chỉ là đổi chữ (đồng hồ, bộ đếm) không tính.
   2) Đang gõ trong ô nhập mà Esc không làm gì → bỏ con trỏ khỏi ô (lần Esc sau mới quay lại).
   3) PC 2.53 — đóng BẢNG PHỤ trước: hộp thoại / cửa sổ nổi / panel chi tiết đang mở (lớp phủ position:fixed) → bấm nút Đóng / Hủy của nó.
   4) Trang có "bước lùi" riêng (KLEsc.register: chi tiết → danh sách…) → lùi 1 bước đó; nút «← Quay lại danh sách» đang hiện cũng được bấm giúp.
   5) PC 2.53 — lùi TAB: tab phụ → tab đầu của thanh tab đó (thanh tab con trước, thanh tab chính sau). Danh sách thanh tab = TAB_BARS bên dưới.
   6) Hết bước riêng → báo lên khung cha (trang lồng trong trang → module → trang chính). Trang chính (index.html) đưa về Tổng quan; đang ở Tổng quan thì không làm gì.
   Dùng:  KLEsc.register(function(){ if (đang ở chi tiết) { quayVeDanhSach(); return true; } return false; }); */
(function () {
  'use strict';
  if (window.KLEsc) return;
  var handlers = [], isTop = window.parent === window, WAIT = 40;

  function isEditable(el) { return !!el && (/^(INPUT|TEXTAREA|SELECT)$/.test(el.tagName) || el.isContentEditable === true); }
  // có thay đổi giao diện thật không (bỏ qua việc chỉ đổi chữ như đồng hồ)
  function real(recs) {
    for (var i = 0; i < recs.length; i++) {
      var r = recs[i];
      if (r.type === 'attributes') { if (r.target.getAttribute(r.attributeName) !== r.oldValue) return true; continue; } // ghi lại đúng giá trị cũ (vd. setAttribute('aria-expanded','false') khi đang false) không tính
      var n = [].slice.call(r.addedNodes || []).concat([].slice.call(r.removedNodes || []));
      for (var j = 0; j < n.length; j++) if (n[j].nodeType !== 3 && n[j].nodeType !== 8) return true;
    }
    return false;
  }
  function runHandlers() {
    for (var i = handlers.length - 1; i >= 0; i--) { try { if (handlers[i]() === true) return true; } catch (e) { if (window.klLog) window.klLog('kl-esc.js', e); } }
    return false;
  }
  // ---------- PC 2.53: đóng bảng phụ (lớp phủ đang mở) ----------
  var LAYER_PRE = '[class*="modal"],[class*="overlay"],[class*="dlg"],[class*="dialog"],[class*="mask"],[class*="-ov"],[class*="-win"],[class*="popup"],[class*="drawer"],[id*="Panel"],[id*="Dlg"],[id*="Modal"],[role="dialog"],dialog[open]';
  var LAYER_SKIP = /(toast|tooltip|\bkl-tip\b|status|fab|selsum|sug\b|xbar|offline|progress|\bau-|login|ctx|context|popover|slt-menu|menu|banner|-exit\b|sc-live|sc-crop)/i;
  var CLOSE_SEL = '[data-close],[data-dismiss],[data-act="close"],[data-act="cancel"],.modal-close,.px-modal-close,.close,.btn-close,[aria-label*="óng" i],[aria-label*="close" i],[title="Đóng"]';
  var CLOSE_TXT = /^(✕|✖|×|x|đóng( lại)?|hủy( bỏ)?|huỷ|thoát|bỏ qua|không|để sau|đã hiểu|cancel|close|quay lại)$/i;
  function shown(el) {
    if (!el || !el.getClientRects || !el.getClientRects().length) return false;
    var cs = window.getComputedStyle(el); return cs.visibility !== 'hidden' && cs.display !== 'none' && parseFloat(cs.opacity || '1') > 0.05;
  }
  function topLayer() {
    var all = document.querySelectorAll(LAYER_PRE), cand = [], i, el, cs, r, z;
    for (i = 0; i < all.length; i++) {
      el = all[i]; if (LAYER_SKIP.test((el.className && el.className.baseVal === undefined ? String(el.className) : '') + ' ' + (el.id || ''))) continue;
      if (!shown(el)) continue; cs = window.getComputedStyle(el); if (cs.position !== 'fixed') continue;
      r = el.getBoundingClientRect(); if (r.width < 240 || r.height < 120) continue;
      z = parseInt(cs.zIndex, 10); cand.push({ el: el, z: isNaN(z) ? 0 : z, i: i });
    }
    if (!cand.length) return null;
    cand = cand.filter(function (c) { return !cand.some(function (o) { return o !== c && o.el.contains(c.el); }); }); // chỉ lấy lớp ngoài cùng
    cand.sort(function (a, b) { return b.z - a.z || b.i - a.i; });
    return cand[0].el;
  }
  function closeControl(layer) {
    var q = layer.querySelectorAll(CLOSE_SEL), i, b;
    for (i = 0; i < q.length; i++) if (shown(q[i])) return q[i];
    var bs = layer.querySelectorAll('button,[role="button"],a'), vis = [], txt;
    for (i = 0; i < bs.length; i++) if (shown(bs[i])) vis.push(bs[i]);
    for (i = 0; i < vis.length; i++) { txt = (vis[i].textContent || '').replace(/\s+/g, ' ').trim().replace(/^[^\wÀ-ỹ✕✖×]+\s*/, function (m) { return /[✕✖×]/.test(m) ? m : ''; }); if (CLOSE_TXT.test(txt)) return vis[i]; }
    if (vis.length === 1) return vis[0];   // hộp thông báo chỉ có 1 nút (OK / Đã hiểu)
    return null;
  }
  function closeLayer() {
    var L = topLayer(); if (!L) return false;
    var c = closeControl(L);
    if (c) { c.click(); return true; }
    return false;   // không biết đóng thế nào → không chặn, để Esc đi tiếp
  }
  // ---------- PC 2.53: nút «← Quay lại danh sách» đang hiện ----------
  var BACK_TXT = /^(←|⬅|‹)?\s*(quay lại danh sách|về danh sách|danh sách( lsx| tài khoản| phiếu)?)\b/i;
  function clickBackButton() {
    var bs = document.querySelectorAll('button,a,[role="button"]'), i, t;
    for (i = 0; i < bs.length; i++) {
      t = (bs[i].textContent || '').replace(/\s+/g, ' ').trim(); if (t.length > 40 || !/^(←|⬅|‹|quay lại|về danh sách)/i.test(t) || !BACK_TXT.test(t) || !shown(bs[i]) || bs[i].disabled) continue;
      bs[i].click(); return true;
    }
    return false;
  }
  // ---------- PC 2.53: lùi tab phụ → tab chính ----------
  var TAB_BARS = '.tl-tabs,.tl-sub,.km-tabs:not(#tKho):not(#tKhu),.sheet-tabs,.dcx-ptabs,.pnx-tabs,.sk-tabs,nav.tabs,.tab-nav,.cd-subnav,.module-nav,nav.menu,nav.nav,.subnav,.kl-subtabs:not(.px-grp-bar),.pl-subnav,.dt-subnav';
  var ACT_CLS = /^(on|active|is-active|selected|current|sel|act)$/;
  function barItems(bar) {
    var kids = [].slice.call(bar.children).filter(function (k) { return shown(k) && (k.textContent || '').trim() !== ''; });
    if (kids.length < 2) kids = [].slice.call(bar.querySelectorAll('button,[role="tab"],a')).filter(function (k) { return shown(k) && (k.textContent || '').trim() !== ''; });
    return kids;
  }
  function isActive(el) {
    if (el.getAttribute('aria-selected') === 'true' || el.getAttribute('aria-current') === 'true' || el.getAttribute('aria-current') === 'page') return true;
    for (var i = 0; i < el.classList.length; i++) if (ACT_CLS.test(el.classList[i])) return true;
    return false;
  }
  function tabBack() {
    var bars = [].slice.call(document.querySelectorAll(TAB_BARS)).filter(shown), i, items, k, act;
    for (i = bars.length - 1; i >= 0; i--) {   // thanh tab con (đứng sau trong trang) trước, thanh tab chính sau
      items = barItems(bars[i]); if (items.length < 2) continue;
      act = -1; for (k = 0; k < items.length; k++) if (isActive(items[k])) { act = k; break; }
      if (act > 0) { items[0].click(); return true; }
    }
    return false;
  }
  function goBack() {
    if (closeLayer()) return;
    if (runHandlers()) return;
    if (clickBackButton()) return;
    if (tabBack()) return;
    if (isTop) { if (typeof window.__klaEscTop === 'function') window.__klaEscTop(); }
    else { try { window.parent.postMessage({ type: 'kla-esc-back' }, '*'); } catch (e) { /* bỏ qua */ } }
  }

  window.addEventListener('keydown', function (ev) {
    if (ev.key !== 'Escape' || ev.repeat || ev.isComposing || ev.ctrlKey || ev.altKey || ev.metaKey || ev.shiftKey) return;
    if (document.fullscreenElement) return; // Esc đang dùng để thoát toàn màn hình của hệ điều hành
    var ae = document.activeElement, ed = isEditable(ae), mo = null, seen = false;
    try { mo = new MutationObserver(function (recs) { if (real(recs)) seen = true; }); mo.observe(document.documentElement, { attributes: true, attributeOldValue: true, childList: true, subtree: true }); } catch (e) { mo = null; }
    setTimeout(function () {
      var recs = []; if (mo) { try { recs = mo.takeRecords(); } catch (e) { /* bỏ qua */ } mo.disconnect(); }
      if (seen || real(recs) || ev.defaultPrevented) return; // trang đã dùng Esc cho việc riêng
      if (ed) { try { ae.blur(); } catch (e) { /* bỏ qua */ } return; }
      goBack();
    }, WAIT);
  }, true);

  // trang con (iframe) báo "Esc chưa ai dùng" → trang cha cũng thử bước lùi của mình rồi báo tiếp lên trên (trang chính tự xử lý ở index.js)
  window.addEventListener('message', function (e) {
    var d = e.data;
    if (isTop || !d || d.type !== 'kla-esc-back' || (e.source && (e.source === window || e.source === window.parent))) return; // chỉ nhận từ trang CON (không nhận từ trang cha)
    goBack();
  });

  window.KLEsc = { register: function (fn) { if (typeof fn === 'function') handlers.push(fn); }, back: goBack, _t: { topLayer: topLayer, closeControl: closeControl, tabBack: tabBack, clickBackButton: clickBackButton } };
})();
