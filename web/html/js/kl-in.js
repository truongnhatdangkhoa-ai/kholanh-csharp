/* kl-in.js — PC 1.98: XEM TRƯỚC KHI IN kiểu Excel cho mọi nút "In" trong app.
   Dùng:  KLIn.open({ html, title, key, orient, paper, margin, name })  → Promise { printed }
     html   : chuỗi HTML đầy đủ cần in, HOẶC hàm (cfg) => HTML khi nội dung phụ thuộc khổ / hướng giấy (vd Scan tài liệu).
              cfg = { orient: 'portrait'|'landscape', paper, w, h, m:{t,r,b,l}, cw, ch }  (mm; w×h đã tính hướng; cw×ch = vùng in)
     key    : tên nhớ cài đặt (hướng / khổ / lề / tỷ lệ) riêng từng loại phiếu, lưu trên máy này
     orient / paper / margin : mặc định lần đầu ('portrait' | 'landscape', 'A4'…, số mm hoặc {t,r,b,l})
   Tuỳ chọn thêm: docMargin (tài liệu tự lo lề: @page lề 0, builder dùng cfg.m) · noScale · noCopies · toggles:[{key,label,value}] → cfg.x[key] · marginFromCaller (lề luôn lấy từ margin truyền vào) · onSettings(st).
   Trang in có thể đặt window.__KL_IN_INFO__ (chuỗi) → hiện dưới phần cài đặt (vd số phiếu phải in ≥ 2 trang).
   Bên trái: In · số bản · máy in · hướng giấy · khổ giấy · lề · tỷ lệ · khoảng trang · Lưu PDF. Bên phải: trang xem trước đúng như sẽ in, chuyển trang ‹ ›.
   Xem trước = PDF dựng ở tiến trình chính (core/in-xem-truoc.js) rồi vẽ bằng pdf.js; bản in dùng cùng HTML + cài đặt.
   Không có window.inAPI (bản web / điện thoại) → in thẳng bằng hộp thoại của trình duyệt như cũ. */
(function () {
  'use strict';
  if (window.KLIn) return;
  var SCRIPT = document.currentScript && document.currentScript.src;
  var LIB = SCRIPT ? SCRIPT.replace(/[^\/]*$/, '') + '../../lib/' : '../../lib/';
  var PAPER = { A4: [210, 297], A3: [297, 420], A5: [148, 210], Letter: [215.9, 279.4], Legal: [215.9, 355.6] };
  var MARGINS = { normal: { n: 'Bình thường', v: 12 }, narrow: { n: 'Hẹp', v: 6 }, wide: { n: 'Rộng', v: 20 }, none: { n: 'Không lề', v: 0 } };
  var esc = function (t) { return String(t == null ? '' : t).replace(/[&<>"]/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]; }); };

  var CSS = '.kin{position:fixed;inset:0;z-index:100000;display:flex;background:#e6e8eb;color:#1f2933;font:14px/1.4 "Segoe UI",Arial,sans-serif}' +
    '.kin *{box-sizing:border-box}.kin [hidden]{display:none!important}.kin-s{width:340px;flex:none;background:#fff;border-right:1px solid #d5d9de;padding:18px 20px 20px;overflow:auto;display:flex;flex-direction:column;gap:12px}' +
    '.kin-hd{display:flex;align-items:center;gap:10px}.kin-hd h2{margin:0;font-size:24px;font-weight:600}.kin-back{width:34px;height:34px;border-radius:50%;border:1px solid #c3c9d0;background:#fff;cursor:pointer;font-size:18px;line-height:1}' +
    '.kin-back:hover{background:#eef1f4}.kin-top{display:flex;align-items:stretch;gap:14px;margin:4px 0 2px}' +
    '.kin-go{width:96px;height:96px;border:1px solid #1f6f43;border-radius:6px;background:#217346;color:#fff;font:600 15px inherit;font-family:inherit;cursor:pointer;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:6px}' +
    '.kin-go:hover:not(:disabled){background:#1a5c38}.kin-go:disabled{opacity:.55;cursor:wait}.kin-go svg{width:30px;height:30px}' +
    '.kin-cp{display:flex;flex-direction:column;gap:6px;justify-content:center}.kin-cp span{font-size:13px;color:#52606d}' +
    '.kin-s h3{margin:6px 0 0;font-size:17px;font-weight:600;color:#217346}.kin-f{display:flex;flex-direction:column;gap:4px}.kin-f>span{font-size:12.5px;color:#52606d}' +
    '.kin-s select,.kin-s input[type=number]{height:36px;border:1px solid #c3c9d0;border-radius:6px;padding:0 8px;background:#fff;color:inherit;font:inherit;width:100%}' +
    '.kin-s select:focus,.kin-s input:focus,.kin-s button:focus-visible{outline:2px solid #217346;outline-offset:1px}.kin-row{display:flex;gap:8px;align-items:center}.kin-row input{width:100%}.kin-row em{font-style:normal;color:#52606d;font-size:12.5px}' +
    '.kin-mg{display:grid;grid-template-columns:1fr 1fr;gap:8px}.kin-mg label{display:flex;flex-direction:column;gap:2px;font-size:12px;color:#52606d}' +
    '.kin-pdf{height:36px;border:1px solid #c3c9d0;border-radius:6px;background:#fff;cursor:pointer;font:inherit;color:inherit}.kin-pdf:hover:not(:disabled){background:#eef1f4}.kin-pdf:disabled{opacity:.55}' +
    '.kin-note{font-size:12.5px;color:#52606d;margin-top:auto}.kin-info{font-size:12.5px;color:#7a4b00;background:#fff6e0;border:1px solid #f1d9a2;border-radius:6px;padding:8px 10px;white-space:pre-wrap}.kin-chk{display:flex;gap:8px;align-items:flex-start;font-size:13px}.kin-chk input{margin-top:3px}.kin-err{color:#b42318;font-size:13px}' +
    '.kin-v{flex:1;min-width:0;display:flex;flex-direction:column}.kin-st{flex:1;min-height:0;overflow:auto;display:flex;align-items:flex-start;justify-content:center;padding:22px}' +
    '.kin-pg{background:#fff;box-shadow:0 1px 6px rgba(0,0,0,.35);position:relative;margin:auto}.kin-pg canvas{display:block}' +
    '.kin-ld{position:absolute;inset:0;display:flex;align-items:center;justify-content:center;color:#52606d;background:rgba(230,232,235,.6)}.kin-ld[hidden]{display:none}' +
    '.kin-nv{flex:none;height:44px;display:flex;align-items:center;justify-content:center;gap:10px;border-top:1px solid #d5d9de;background:#f4f5f7}' +
    '.kin-nv button{width:32px;height:30px;border:1px solid #c3c9d0;border-radius:5px;background:#fff;cursor:pointer;font-size:16px}.kin-nv button:disabled{opacity:.4;cursor:default}.kin-nv input{width:54px;height:30px;text-align:center;border:1px solid #c3c9d0;border-radius:5px;font:inherit}' +
    /* PC 3.18: trang In luôn SÁNG — ép màu chữ / nền cho nút, ô nhập, ô chọn (nếu không, giao diện tối của app làm chữ trắng trên nền trắng) */
    '.kin button:not(.kin-go),.kin input,.kin select{color:#1f2933!important;-webkit-text-fill-color:#1f2933!important;background-color:#fff!important}.kin button:not(.kin-go):hover:not(:disabled){background-color:#eef1f4!important}' +
    '.kin input::placeholder{color:#7b8794!important;-webkit-text-fill-color:#7b8794!important;opacity:1}.kin .kin-go{color:#fff!important;-webkit-text-fill-color:#fff!important}.kin input[type=checkbox]{accent-color:#217346}' +
    '@media (max-width:760px){.kin{flex-direction:column}.kin-s{width:100%;max-height:48%;border-right:0;border-bottom:1px solid #d5d9de}}';

  var ICON = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M7 8V3h10v5"/><rect x="3" y="8" width="18" height="9" rx="2"/><path d="M7 14h10v7H7z"/></svg>';

  function cfgOf(st) {
    var pg = PAPER[st.paper] || PAPER.A4, land = st.orient === 'landscape', w = land ? pg[1] : pg[0], h = land ? pg[0] : pg[1], m = st.m;
    if (st.size) { w = st.size.w; h = st.size.h; }
    return { orient: land ? 'landscape' : 'portrait', paper: st.paper, w: w, h: h, x: Object.assign({}, st.x || {}), m: { t: m.t, r: m.r, b: m.b, l: m.l }, cw: Math.max(20, w - m.l - m.r), ch: Math.max(20, h - m.t - m.b) };
  }
  function htmlOf(o, st) { return typeof o.html === 'function' ? String(o.html(cfgOf(st))) : String(o.html == null ? '' : o.html); }
  function optOf(st) {
    return { pageW: st.size ? st.size.w : 0, pageH: st.size ? st.size.h : 0, paper: st.paper, orient: st.orient, margin: st.m, scale: st.noScale ? 100 : (st.scale === 'fit' ? 'fit' : st.scaleN), copies: st.noCopies ? 1 : st.copies, printer: st.printer, from: st.from, to: st.to, docMargin: !!st.docMargin };
  }
  function loadLib() {
    if (window.pdfjsLib) return Promise.resolve();
    if (window.klNap) return window.klNap('pdfjs');
    return new Promise(function (ok, no) {
      var s = document.createElement('script'); s.src = LIB + 'pdf.min.js';
      s.onload = function () { try { window.pdfjsLib.GlobalWorkerOptions.workerSrc = LIB + 'pdf.worker.min.js'; } catch (e) { /* bỏ qua */ } ok(); };
      s.onerror = function () { no(new Error('Thiếu thư viện xem PDF (lib/pdf.min.js).')); }; document.head.appendChild(s);
    });
  }
  function fallbackPrint(o) {
    return new Promise(function (resolve) {
      var fr = document.createElement('iframe'); fr.setAttribute('aria-hidden', 'true'); fr.style.cssText = 'position:fixed;right:0;bottom:0;width:0;height:0;border:0'; document.body.appendChild(fr);
      var d = fr.contentWindow.document; d.open(); d.write(htmlOf(o, { orient: o.orient || 'portrait', paper: o.paper || 'A4', m: marginObj(o.margin == null ? 12 : o.margin) })); d.close();
      var imgs = Array.prototype.slice.call(d.images || []);
      Promise.all(imgs.map(function (im) { return im.decode ? im.decode().catch(function () {}) : Promise.resolve(); })).then(function () {
        setTimeout(function () { try { fr.contentWindow.focus(); fr.contentWindow.print(); } catch (e) { /* bỏ qua */ } setTimeout(function () { if (fr.parentNode) fr.parentNode.removeChild(fr); }, 3000); resolve({ printed: true }); }, 250);
      });
    });
  }
  function marginObj(v) { if (v && typeof v === 'object') return { t: +v.t || 0, r: +v.r || 0, b: +v.b || 0, l: +v.l || 0 }; var n = isFinite(+v) ? +v : 12; return { t: n, r: n, b: n, l: n }; }
  var num = function (v, a, b, d) { v = Number(v); return isFinite(v) ? Math.min(b, Math.max(a, v)) : d; };

  function open(o) {
    o = o || {};
    if (!window.inAPI) return fallbackPrint(o);
    return new Promise(function (resolveAll) {
      var key = 'kl.in.v1.' + String(o.key || o.title || 'chung'), saved = null;
      try { saved = JSON.parse(localStorage.getItem(key) || 'null'); } catch (e) { saved = null; }
      var st = { orient: o.orient === 'landscape' ? 'landscape' : 'portrait', paper: PAPER[o.paper] ? o.paper : 'A4', m: marginObj(o.margin == null ? 12 : o.margin), mt: 'normal', scale: 'n', scaleN: 100, copies: 1, printer: '', from: 0, to: 0, x: {}, docMargin: !!o.docMargin, size: (o.pageSize && o.pageSize.w > 0 && o.pageSize.h > 0) ? { w: +o.pageSize.w, h: +o.pageSize.h } : null, noScale: !!o.noScale, noCopies: !!o.noCopies };
      if (st.size) o.lockPaper = true;
      if (o.scale === 'fit') st.scale = 'fit';
      (o.toggles || []).forEach(function (t) { st.x[t.key] = !!t.value; });
      if (saved && typeof saved === 'object') {
        if (saved.orient === 'landscape' || saved.orient === 'portrait') st.orient = saved.orient;
        if (PAPER[saved.paper]) st.paper = saved.paper;
        if (saved.m && !o.marginFromCaller) { st.m = marginObj(saved.m); if (saved.mt) st.mt = saved.mt; }
        if (saved.x && typeof saved.x === 'object' && !o.toggleFromCaller) (o.toggles || []).forEach(function (t) { if (typeof saved.x[t.key] === 'boolean') st.x[t.key] = saved.x[t.key]; });
        if (false) st.mt = saved.mt; if (saved.scale === 'fit') st.scale = 'fit'; st.scaleN = num(saved.scaleN, 20, 200, 100); if (saved.printer) st.printer = String(saved.printer);
      }
      if (!saved || o.marginFromCaller) st.mt = (function () { var v = st.m.t; for (var k in MARGINS) if (MARGINS[k].v === v && st.m.r === v && st.m.b === v && st.m.l === v) return k; return 'custom'; })();
      if (!document.getElementById('klInCss')) { var sy = document.createElement('style'); sy.id = 'klInCss'; sy.textContent = CSS; document.head.appendChild(sy); }
      var prevFocus = document.activeElement;
      var root = document.createElement('div'); root.className = 'kin'; root.setAttribute('role', 'dialog'); root.setAttribute('aria-modal', 'true'); root.setAttribute('aria-label', 'Xem trước khi in');
      root.innerHTML = '<aside class="kin-s"><div class="kin-hd"><button type="button" class="kin-back" data-k="close" aria-label="Đóng (Esc)" title="Đóng (Esc)">←</button><h2>In</h2></div>' +
        '<div class="kin-top"><button type="button" class="kin-go" data-k="go">' + ICON + 'In</button><label class="kin-cp"' + (o.noCopies ? ' hidden' : '') + '><span>Số bản</span><input type="number" min="1" max="99" value="1" data-k="copies"></label></div>' +
        '<h3>Máy in</h3><div class="kin-f"><select data-k="printer" aria-label="Máy in"><option value="">Đang tìm máy in…</option></select></div>' +
        '<h3>Cài đặt</h3>' +
        '<label class="kin-f"' + (o.lockPaper ? ' hidden' : '') + '><span>Hướng giấy</span><select data-k="orient"><option value="portrait">Giấy dọc</option><option value="landscape">Giấy ngang</option></select></label>' +
        '<label class="kin-f"' + (o.lockPaper ? ' hidden' : '') + '><span>Khổ giấy</span><select data-k="paper">' + Object.keys(PAPER).map(function (k) { return '<option value="' + k + '">' + k + ' (' + PAPER[k][0] + ' × ' + PAPER[k][1] + ' mm)</option>'; }).join('') + '</select></label>' +
        '<label class="kin-f"' + (o.noMargin ? ' hidden' : '') + '><span>Lề</span><select data-k="mt">' + Object.keys(MARGINS).map(function (k) { return '<option value="' + k + '">' + MARGINS[k].n + ' (' + MARGINS[k].v + ' mm)</option>'; }).join('') + '<option value="custom">Tùy chỉnh…</option></select></label>' +
        '<div class="kin-mg" data-k="mgbox" hidden><label>Trên (mm)<input type="number" min="0" max="60" data-k="mT"></label><label>Dưới (mm)<input type="number" min="0" max="60" data-k="mB"></label><label>Trái (mm)<input type="number" min="0" max="60" data-k="mL"></label><label>Phải (mm)<input type="number" min="0" max="60" data-k="mR"></label></div>' +
        (o.toggles || []).map(function (t) { return '<label class="kin-chk"' + (t.title ? ' title="' + esc(t.title) + '"' : '') + '><input type="checkbox" data-k="x:' + esc(t.key) + '"> <span>' + esc(t.label) + '</span></label>'; }).join('') +
        '<label class="kin-f"' + (o.noScale ? ' hidden' : '') + '><span>Tỷ lệ</span><select data-k="scale"><option value="n">Tỷ lệ thật / tùy chỉnh</option><option value="fit">Vừa khít chiều ngang (thu nhỏ nếu rộng hơn trang)</option></select></label>' +
        '<div class="kin-row" data-k="scbox"' + (o.noScale ? ' hidden' : '') + '><input type="number" min="20" max="200" step="5" data-k="scaleN" aria-label="Tỷ lệ phần trăm"><em>%</em></div>' +
        '<div class="kin-f"><span>Trang</span><div class="kin-row"><input type="number" min="0" data-k="from" placeholder="Từ"><em>đến</em><input type="number" min="0" data-k="to" placeholder="Đến"></div></div>' +
        '<div class="kin-info" data-k="info" hidden></div><button type="button" class="kin-pdf" data-k="pdf">Lưu thành file PDF…</button><div class="kin-err" data-k="err" role="alert"></div>' +
        '<div class="kin-note">Để trống “Trang” = in tất cả. Bản xem trước bên phải là đúng như sẽ in.</div></aside>' +
        '<main class="kin-v"><div class="kin-st" data-k="stage"><div class="kin-pg" data-k="pg"><canvas data-k="cv"></canvas><div class="kin-ld" data-k="ld">Đang tạo bản xem trước…</div></div></div>' +
        '<footer class="kin-nv"><button type="button" data-k="prev" aria-label="Trang trước">‹</button><span>Trang</span><input type="number" min="1" value="1" data-k="cur" aria-label="Số trang"><span data-k="tot">/ 1</span><button type="button" data-k="next" aria-label="Trang sau">›</button></footer></main>';
      document.body.appendChild(root);
      var q = function (k) { return root.querySelector('[data-k="' + k + '"]'); }; // (k có dấu ':' vẫn hợp lệ trong selector thuộc tính có nháy)
      var pdf = null, pageNo = 1, token = 0, timer = 0, closed = false, busy = false, printed = false;

      function save() { try { localStorage.setItem(key, JSON.stringify({ orient: st.orient, paper: st.paper, m: st.m, mt: st.mt, scale: st.scale, scaleN: st.scaleN, printer: st.printer, x: st.x }));
        if (typeof o.onSettings === 'function') { try { o.onSettings({ orient: st.orient, paper: st.paper, m: st.m, x: Object.assign({}, st.x) }); } catch (e) { /* bỏ qua */ } } } catch (e) { /* bỏ qua */ } }
      function sync() {
        q('orient').value = st.orient; q('paper').value = st.paper; q('mt').value = st.mt; q('scale').value = st.scale; q('scaleN').value = st.scaleN; q('copies').value = st.copies;
        q('mgbox').hidden = st.mt !== 'custom'; q('mT').value = st.m.t; q('mB').value = st.m.b; q('mL').value = st.m.l; q('mR').value = st.m.r;
        q('scbox').style.display = (st.noScale || st.scale === 'fit') ? 'none' : 'flex'; (o.toggles || []).forEach(function (t) { var c = q('x:' + t.key); if (c) c.checked = !!st.x[t.key]; }); q('from').value = st.from || ''; q('to').value = st.to || '';
      }
      function setErr(t) { q('err').textContent = t || ''; }
      function setBusy(b) { busy = b; q('go').disabled = b; q('pdf').disabled = b; }

      function drawPage() {
        if (!pdf) return;
        var n = Math.min(Math.max(1, pageNo), pdf.numPages); pageNo = n; q('cur').value = n; q('cur').max = pdf.numPages; q('tot').textContent = '/ ' + pdf.numPages;
        q('prev').disabled = n <= 1; q('next').disabled = n >= pdf.numPages;
        var my = ++token;
        pdf.getPage(n).then(function (page) {
          if (my !== token || closed) return;
          var vp1 = page.getViewport({ scale: 1 }), stg = q('stage'), aw = stg.clientWidth - 44, ah = stg.clientHeight - 44, sc = Math.max(0.2, aw / vp1.width), dpr = Math.max(2, window.devicePixelRatio || 1);   // PC 2.07: vừa chiều ngang + vẽ gấp đôi độ phân giải → chữ nét, không mờ
          var vp = page.getViewport({ scale: sc * dpr }), cv = q('cv'); cv.width = Math.floor(vp.width); cv.height = Math.floor(vp.height); cv.style.width = Math.floor(vp.width / dpr) + 'px'; cv.style.height = Math.floor(vp.height / dpr) + 'px';
          page.render({ canvasContext: cv.getContext('2d'), viewport: vp }).promise.then(function () { if (my === token) q('ld').hidden = true; }, function () {});
        });
      }
      function refresh() { // dựng lại bản xem trước sau mỗi lần đổi cài đặt (gộp các lần đổi liên tiếp)
        clearTimeout(timer); q('ld').hidden = false; setErr('');
        timer = setTimeout(function () {
          var my = ++token;
          Promise.all([loadLib(), window.inAPI.pdf(htmlOf(o, st), optOf(st))]).then(function (rs) {
            if (my !== token || closed) return; var r = rs[1];
            if (!r || !r.ok) throw new Error((r && r.error) || 'Không tạo được bản xem trước.');
            var data = new Uint8Array(r.pdf), inf = q('info'); inf.textContent = r.info || ''; inf.hidden = !r.info;
            return window.pdfjsLib.getDocument({ data: data }).promise.then(function (d) { if (my !== token || closed) return; if (pdf) { try { pdf.destroy(); } catch (e) { /* bỏ qua */ } } pdf = d; if (pageNo > d.numPages) pageNo = d.numPages; drawPage(); });
          }).catch(function (e) { if (my !== token) return; q('ld').hidden = true; setErr((e && e.message) || String(e)); });
        }, 220);
      }
      function close() {
        if (closed) return; closed = true; clearTimeout(timer); token++;
        document.removeEventListener('keydown', onKey, true); window.removeEventListener('resize', onResize);
        try { if (pdf) pdf.destroy(); } catch (e) { /* bỏ qua */ } if (root.parentNode) root.parentNode.removeChild(root);
        try { if (prevFocus && prevFocus.focus) prevFocus.focus(); } catch (e) { /* bỏ qua */ } resolveAll({ printed: printed });
      }
      function onKey(e) {
        if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); close(); return; }
        var tg = e.target && e.target.tagName; if (/^(INPUT|SELECT|TEXTAREA)$/.test(tg || '')) return;
        if (e.key === 'PageDown' || e.key === 'ArrowRight') { pageNo++; drawPage(); } else if (e.key === 'PageUp' || e.key === 'ArrowLeft') { pageNo--; drawPage(); }
      }
      var rt = 0; function onResize() { clearTimeout(rt); rt = setTimeout(drawPage, 150); }
      document.addEventListener('keydown', onKey, true); window.addEventListener('resize', onResize);

      function readRange() { st.from = Math.floor(num(q('from').value, 0, 9999, 0)); st.to = Math.floor(num(q('to').value, 0, 9999, 0)); if (st.from && st.to && st.to < st.from) st.to = st.from; }
      root.addEventListener('change', function (e) {
        var k = e.target && e.target.getAttribute && e.target.getAttribute('data-k'); if (!k) return;
        if (k === 'orient') st.orient = e.target.value === 'landscape' ? 'landscape' : 'portrait';
        else if (k === 'paper') st.paper = PAPER[e.target.value] ? e.target.value : 'A4';
        else if (k === 'mt') { st.mt = e.target.value; if (MARGINS[st.mt]) st.m = marginObj(MARGINS[st.mt].v); }
        else if (k === 'mT' || k === 'mB' || k === 'mL' || k === 'mR') { st.mt = 'custom'; st.m = { t: num(q('mT').value, 0, 60, 12), b: num(q('mB').value, 0, 60, 12), l: num(q('mL').value, 0, 60, 12), r: num(q('mR').value, 0, 60, 12) }; }
        else if (k === 'scale') st.scale = e.target.value === 'fit' ? 'fit' : 'n';
        else if (k === 'scaleN') st.scaleN = num(e.target.value, 20, 200, 100);
        else if (k === 'copies') st.copies = Math.floor(num(e.target.value, 1, 99, 1));
        else if (k === 'printer') st.printer = e.target.value;
        else if (k === 'from' || k === 'to') { readRange(); sync(); return; }
        else if (k.indexOf('x:') === 0) st.x[k.slice(2)] = !!e.target.checked;
        else if (k === 'cur') { pageNo = Math.floor(num(e.target.value, 1, pdf ? pdf.numPages : 1, 1)); drawPage(); return; }
        save(); sync(); if (k !== 'copies' && k !== 'printer') refresh();
      });
      root.addEventListener('click', function (e) {
        var b = e.target.closest && e.target.closest('[data-k]'); if (!b) return; var k = b.getAttribute('data-k');
        if (k === 'close') close();
        else if (k === 'prev') { pageNo--; drawPage(); } else if (k === 'next') { pageNo++; drawPage(); }
        else if (k === 'go') {
          readRange(); setBusy(true); setErr(''); q('go').lastChild.textContent = 'Đang in…';
          window.inAPI.print(htmlOf(o, st), optOf(st)).then(function (r) {
            if (r && r.ok) { printed = true; close(); return; }
            setBusy(false); q('go').lastChild.textContent = 'In'; if (r && !r.cancelled) setErr((r && r.error) || 'Không in được.');
          }).catch(function (er) { setBusy(false); q('go').lastChild.textContent = 'In'; setErr((er && er.message) || String(er)); });
        } else if (k === 'pdf') {
          setBusy(true); setErr('');
          window.inAPI.savePdf(htmlOf(o, st), optOf(st), o.name || o.title || 'In').then(function (r) { setBusy(false); if (r && r.ok) setErr(''); else if (r && !r.cancelled) setErr((r && r.error) || 'Không lưu được PDF.'); })
            .catch(function (er) { setBusy(false); setErr((er && er.message) || String(er)); });
        }
      });
      sync(); refresh();
      // danh sách máy in (lỗi / không có máy in → vẫn in được bằng hộp thoại hệ thống)
      window.inAPI.printers().then(function (r) {
        var sel = q('printer'), list = r && r.ok ? r.printers : [];
        if (!list.length) { sel.innerHTML = '<option value="">Hộp thoại in của hệ thống (chưa thấy máy in)</option>'; return; }
        var def = list.filter(function (p) { return p.isDefault; })[0];
        if (!st.printer || !list.some(function (p) { return p.name === st.printer; })) st.printer = (def || list[0]).name;
        sel.innerHTML = list.map(function (p) { return '<option value="' + esc(p.name) + '">' + esc(p.displayName) + (p.isDefault ? ' (mặc định)' : '') + '</option>'; }).join(''); sel.value = st.printer;
      }).catch(function () { q('printer').innerHTML = '<option value="">Hộp thoại in của hệ thống</option>'; });
      setTimeout(function () { try { q('go').focus(); } catch (e) { /* bỏ qua */ } }, 50);
    });
  }
  /* PC 2.00: dựng HTML in từ CHÍNH trang đang mở (bản sao DOM + CSS, bỏ script) — dùng cho nút In có sẵn của trang.
     opts.only: [selector…] chỉ lấy các phần tử này · opts.drop: [selector…] bỏ đi · opts.light (mặc định true): ép giao diện sáng khi in. */
  function pageHtml(opts) {
    opts = opts || {};
    var root = document.documentElement.cloneNode(true);
    root.querySelectorAll('script,iframe,meta[http-equiv],noscript').forEach(function (n) { n.remove(); });
    (opts.drop || []).forEach(function (s) { root.querySelectorAll(s).forEach(function (n) { n.remove(); }); });
    var body = root.querySelector('body');
    if (opts.only && opts.only.length) {
      var parts = [];
      opts.only.forEach(function (s) { var n = document.querySelector(s); if (n) parts.push(n.outerHTML.replace(/<script[\s\S]*?<\/script>/gi, '')); });
      body.innerHTML = parts.join('');
    }
    if (opts.light !== false) root.setAttribute('data-theme', 'light');
    var head = root.querySelector('head');
    var base = document.createElement('base'); base.href = location.href; head.insertBefore(base, head.firstChild);
    root.querySelectorAll('link[rel="stylesheet"]').forEach(function (l) { try { l.setAttribute('href', new URL(l.getAttribute('href'), location.href).href); } catch (e) { /* bỏ qua */ } });
    var css = opts.css ? '<style>' + opts.css + '</style>' : '';
    return '<!doctype html>' + root.outerHTML.replace(/<\/head>/i, css + '</head>');
  }
  window.KLIn = { open: open, PAPER: PAPER, pageHtml: pageHtml };
})();
