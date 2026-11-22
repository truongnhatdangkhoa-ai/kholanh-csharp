// nap-thu-vien.js — NẠP LƯỜI thư viện nặng (PC 0.19).
// ExcelJS (~0,9 MB), jsPDF (~0,36 MB), html2canvas (~0,2 MB) trước đây nạp NGAY khi mở module dù chỉ dùng lúc bấm
// Xuất Excel / Xuất PDF. Mỗi module là 1 iframe riêng nên mỗi module tự giữ 1 bản trong RAM → máy yếu mở vài module là nặng.
// Nay chỉ nạp khi cần:   await klNap('exceljs');   await klNap('jspdf', 'html2canvas');   await klNap('pdfjs');
// Gọi nhiều lần không sao: lần sau trả ngay (thư viện đã có). Chỉ nạp từ thư mục lib/ trên máy — KHÔNG dùng mạng.
(function () {
  'use strict';
  if (window.klNap) return;
  var cur = document.currentScript && document.currentScript.src;
  var BASE = cur ? cur.replace(/[^\/]*$/, '') : '../lib/';
  var LIBS = {
    exceljs:     { file: 'exceljs.min.js',     ten: 'ExcelJS (xuất Excel)', ok: function () { return typeof window.ExcelJS !== 'undefined'; } },
    jspdf:       { file: 'jspdf.umd.min.js',   ten: 'jsPDF (xuất PDF)',     ok: function () { return !!(window.jspdf && window.jspdf.jsPDF); } },
    html2canvas: { file: 'html2canvas.min.js', ten: 'html2canvas (chụp phiếu)', ok: function () { return typeof window.html2canvas === 'function'; } },
    jszip:       { file: 'jszip.min.js',       ten: 'JSZip (nén ZIP)', ok: function () { return typeof window.JSZip !== 'undefined'; } },
    // PC 0.21 — pdf.js cho Scan tài liệu (đọc PDF). Worker nằm cùng thư mục lib/.
    pdfjs:       { file: 'pdf.min.js',         ten: 'pdf.js (đọc PDF)', ok: function () { return !!window.pdfjsLib; },
                   sau: function () { try { window.pdfjsLib.GlobalWorkerOptions.workerSrc = BASE + 'pdf.worker.min.js'; } catch (e) {} } }
  };
  var dangNap = {};
  function napMot(name) {
    var lib = LIBS[name];
    if (!lib) return Promise.reject(new Error('Không biết thư viện: ' + name));
    if (lib.ok()) return Promise.resolve();
    if (dangNap[name]) return dangNap[name];
    dangNap[name] = new Promise(function (resolve, reject) {
      var s = document.createElement('script');
      s.src = BASE + lib.file;
      s.onload = function () {
        if (lib.ok()) { if (lib.sau) lib.sau(); try { if (window.KLXS) window.KLXS.patchAll(); } catch (e) { /* PC 3.36 */ } resolve(); }
        else { delete dangNap[name]; reject(new Error('Nạp xong nhưng không dùng được thư viện ' + lib.ten + '.')); }
      };
      s.onerror = function () {
        delete dangNap[name]; s.remove();
        reject(new Error('Thiếu thư viện ' + lib.ten + ' (lib/' + lib.file + '). Hãy chạy lại bản cập nhật đầy đủ.'));
      };
      document.head.appendChild(s);
    });
    return dangNap[name];
  }
  window.klNap = function () {
    var names = Array.prototype.slice.call(arguments);
    return Promise.all(names.map(napMot)).then(function () { return true; });
  };

  // PC 1.85 — NẠP LƯỜI bộ đọc / ghi Excel của module Tồn kho An An (SheetJS 0,9 MB + xlsx-js-style 0,4 MB + JSZip): trước đây chặn việc hiện màn hình ~0,2 s (máy yếu ~0,5 s) mỗi lần mở module.
  // Giữ ĐÚNG thứ tự cũ: SheetJS chính thức (đọc file → window.XLSX_DOC) rồi xlsx-js-style (ghi có màu → window.XLSX ghi đè) rồi JSZip. Gọi nhiều lần không sao.
  function napTep(src, optional) {
    return new Promise(function (resolve, reject) {
      var s = document.createElement('script'); s.src = src;
      s.onload = function () { resolve(); };
      s.onerror = function () { s.remove(); if (optional) resolve(); else reject(new Error('Thiếu thư viện Excel: ' + src)); };
      document.head.appendChild(s);
    });
  }
  window.klNapXlsx = function () {
    if (!window.__klXlsxLazy) return Promise.resolve(true); // trang khác vẫn nạp thư viện Excel theo cách cũ
    if (window.__klXlsxP) return window.__klXlsxP;
    window.__klXlsxP = (window.XLSX && window.XLSX.utils && window.XLSX_DOC !== undefined && window.JSZip ? Promise.resolve() :
      napTep(BASE + '../node_modules/xlsx/dist/xlsx.full.min.js', true)
        .then(function () { window.XLSX_DOC = window.XLSX || null; return napTep(BASE + 'xlsx.bundle.js', false); })
        .then(function () { return window.klNap('jszip'); }))
      .then(function () { try { if (window.KLXS) window.KLXS.patchAll(); } catch (e) { /* PC 3.36: bọc XLSX.writeFile → file có viền */ } return true; }, function (e) { window.__klXlsxP = null; throw e; });
    return window.__klXlsxP;
  };
  window.klNapXlsxSauKhiHien = function () { // gọi 1 lần: nạp ngầm ngay sau khi màn hình đã hiện, trước khi người dùng kịp bấm Xuất / Nạp file
    var go = function () { setTimeout(function () { window.klNapXlsx().catch(function (e) { console.error(e); }); }, 60); };
    if (document.readyState === 'complete') go(); else window.addEventListener('load', go, { once: true });
  };
})();

// PC 2.74 — DỰNG FILE EXCEL TỪNG ĐOẠN NHỎ: các bước nặng (XLSX.write, nén ZIP, ghi file) cách nhau bằng klNhuong() để giao diện kịp vẽ + nhận chuột / phím,
// và klBang(msg) hiện thanh «Đang tạo file…» ở góc dưới để người dùng thấy app đang làm việc (không tưởng bị đứng). Không dùng mạng, không đổi nội dung file.
(function () {
  window.klNhuong = function () { return new Promise(function (r) { var d = false, f = function () { if (!d) { d = true; r(); } }; try { requestAnimationFrame(function () { setTimeout(f, 0); }); } catch (e) { /* khung ẩn */ } setTimeout(f, 80); }); };
  window.klBang = function (msg) {
    var el = null;
    try { el = document.createElement('div'); el.setAttribute('role', 'status'); el.textContent = msg || 'Đang xử lý…';
      el.style.cssText = 'position:fixed;right:16px;bottom:16px;z-index:2147483000;padding:10px 16px;border-radius:10px;font:600 14px system-ui,sans-serif;background:#0B57D0;color:#fff;box-shadow:0 4px 16px rgba(0,0,0,.3);pointer-events:none';
      document.body.appendChild(el); } catch (e) { el = null; }
    return function () { try { if (el && el.parentNode) el.parentNode.removeChild(el); } catch (e) { /* bỏ qua */ } };
  };
})();
