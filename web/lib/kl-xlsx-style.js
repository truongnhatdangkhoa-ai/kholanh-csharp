/* kl-xlsx-style.js — PC 3.36: MỌI FILE EXCEL XUẤT RA đều có VIỀN + TIÊU ĐỀ RÕ + CỘT TỰ CĂN THEO DỮ LIỆU như một báo cáo (áp dụng toàn app).
   CÁCH LÀM — một lớp dùng chung, không phải sửa từng nơi xuất:
   · ExcelJS: bọc `Workbook.xlsx.writeBuffer / write / writeFile` → trước khi ghi, `styleWorkbook(wb)` tự định dạng từng sheet CHƯA có định dạng:
       – tìm DÒNG TIÊU ĐỀ (dòng đầu có ≥ 2 ô chữ và ≥ nửa số cột), các dòng tiêu đề / tên báo cáo phía trên (nếu có) giữ nguyên;
       – kẻ viền mảnh toàn bảng, tiêu đề đậm + nền xanh nhạt + căn giữa + tự xuống dòng, dòng "Tổng / Cộng" đậm nền xám;
       – độ rộng cột theo nội dung dài nhất (8–60), ô quá dài tự xuống dòng; số căn phải; cố định dòng tiêu đề; bộ lọc tiêu đề; in ngang vừa 1 trang rộng + lặp tiêu đề.
   · SheetJS (`XLSX.writeFile(wb, "….xlsx")`): chuyển sang ExcelJS (giữ giá trị / công thức / định dạng số / ô gộp / độ rộng) rồi tô như trên và tải xuống (bất đồng bộ — nơi gọi không phụ thuộc kết quả trả về).
   KHÔNG đụng vào: (1) sheet / file ĐÃ có định dạng riêng (≥ 25% ô có viền / nền, hoặc tiêu đề đã tô) — tức các module có MẪU RIÊNG (Chứng từ xuất kho, Báo cáo rã đông, Phiếu nhập xuất, Mẫu in…) tự giữ mẫu của mình; (2) các trang có cờ `window.KL_XLSX_NOSTYLE` hoặc nằm trong NOSTYLE_PAGES; (3) workbook có `wb.__klSkipStyle = true`; (4) cột đã đặt độ rộng riêng.
   Lõi (`styleWorkbook`, `fromSheetJS`) không phụ thuộc DOM → chạy được trong Node (tests/kl-xlsx-style.test.js). */
(function (root) {
  'use strict';
  const NOSTYLE_PAGES = /(chung-tu-xuat-kho|bao-cao-ra-dong|phieu-nhap-xuat|phieu-xuat-he-thong|mau-in|nhap-lieu-module|scan-tai-lieu|container-3d)/i;
  const SIDE = { style: 'thin', color: { argb: 'FF808080' } }, BORDER = { top: SIDE, left: SIDE, bottom: SIDE, right: SIDE };
  const HEAD_FILL = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FFD9E1F2' } }, TOT_FILL = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FFF2F2F2' } };
  const FONT = 'Times New Roman';
  const isNum = (v) => typeof v === 'number' && isFinite(v);

  /** giá trị ô → { text: chữ hiển thị (để đo độ rộng), kind: 'n' | 's' | 'd' | '' } */
  function cellInfo(cell) {
    let v = cell.value; if (v == null) return { text: '', kind: '' };
    if (typeof v === 'object' && !(v instanceof Date)) {
      if (v.richText) v = v.richText.map((x) => x.text).join(''); else if ('result' in v) v = v.result; else if (v.formula || v.sharedFormula) v = ''; else if (v.text != null) v = v.text; else if (v.error) v = String(v.error); else v = '';
    }
    if (v == null) return { text: '', kind: '' };
    if (v instanceof Date) return { text: '00/00/0000', kind: 'd' };
    if (isNum(v)) { const f = cell.numFmt || ''; const dec = /0\.(0+)/.exec(f), nd = dec ? dec[1].length : (Number.isInteger(v) ? 0 : Math.min(4, (String(v).split('.')[1] || '').length)); return { text: v.toLocaleString('en-US', { minimumFractionDigits: nd, maximumFractionDigits: nd }), kind: 'n' }; }
    return { text: String(v), kind: typeof v === 'boolean' ? 's' : 's' };
  }
  const maxLine = (t) => String(t).split(/\r?\n/).reduce((m, l) => Math.max(m, l.length), 0);

  function hasStyle(cell) { const b = cell.border; return !!(b && (b.top || b.left || b.bottom || b.right)) || !!(cell.fill && cell.fill.pattern && cell.fill.pattern !== 'none'); }
  /** Sheet đã được người viết module định dạng sẵn (có mẫu riêng) → không đụng. */
  function alreadyStyled(ws, h0, r1, c1) {
    let n = 0, s = 0;
    for (let r = h0; r <= r1 && n < 400; r++) { const row = ws.getRow(r); for (let c = 1; c <= c1; c++) { const cell = row.getCell(c); if (cell.value == null || cell.value === '') continue; n++; if (hasStyle(cell)) s++; } }
    return n > 0 && s / n >= 0.25;
  }
  function mergedSet(ws) { const set = new Set(); try { const m = ws.model && ws.model.merges ? ws.model.merges : []; m.forEach((a) => { const mm = /^([A-Z]+)(\d+):([A-Z]+)(\d+)$/.exec(a); if (mm) set.add(+mm[2] + '-' + +mm[4]); }); } catch (e) { /* bỏ qua */ } return set; }

  /** Định dạng 1 sheet. Trả { styled, why }. */
  function styleSheet(ws, opts) {
    opts = opts || {};
    const r1 = ws.actualRowCount ? ws.rowCount : 0, c1 = ws.actualColumnCount || ws.columnCount; if (!r1 || !c1) return { styled: false, why: 'trống' };
    if (ws.__klSkipStyle) return { styled: false, why: 'đã đánh dấu bỏ qua' };
    // dòng tiêu đề: dòng đầu (≤ 12) có ≥ 2 ô chữ và ≥ nửa số cột đang dùng
    let h = 0;
    for (let r = 1; r <= Math.min(r1, 12); r++) {
      const row = ws.getRow(r); let ns = 0, nn = 0; for (let c = 1; c <= c1; c++) { const i = cellInfo(row.getCell(c)); if (i.kind === 's' && i.text.trim()) ns++; else if (i.kind) nn++; }
      if (ns >= 2 && ns >= Math.ceil(c1 / 2) && ns >= nn) { h = r; break; }
    }
    if (!h) h = 1;
    if (alreadyStyled(ws, h, r1, c1)) return { styled: false, why: 'đã có định dạng riêng (mẫu riêng)' };
    const merged = mergedSet(ws);
    // ---- độ rộng cột ----
    const wide = new Array(c1 + 1).fill(0), longCol = new Array(c1 + 1).fill(false);
    for (let r = h; r <= r1; r++) { const row = ws.getRow(r); for (let c = 1; c <= c1; c++) { const cell = row.getCell(c); if (cell.isMerged && cell.master !== cell) continue; const i = cellInfo(cell); if (!i.text) continue; let L = maxLine(i.text); if (r === h) L = Math.min(L, 34); else if (L > 60) longCol[c] = true; wide[c] = Math.max(wide[c], L); } }
    for (let c = 1; c <= c1; c++) { const col = ws.getColumn(c); if (col.width && col.width > 0 && !opts.forceWidth) continue; col.width = Math.max(8, Math.min(60, Math.round(wide[c] * 1.18 + 2.5))); }
    // ---- viền + tiêu đề + nội dung ----
    for (let r = h; r <= r1; r++) {
      const row = ws.getRow(r); let first = ''; for (let c = 1; c <= c1; c++) { const i = cellInfo(row.getCell(c)); if (i.text) { first = i.text; break; } }
      const isTot = r > h && /^\s*(tổng|tong|cộng|cong|total|sum)\b/i.test(first);
      for (let c = 1; c <= c1; c++) {
        const cell = row.getCell(c), info = cellInfo(cell);
        cell.border = BORDER;
        if (r === h) { cell.font = Object.assign({}, cell.font || {}, { name: FONT, size: 11, bold: true }); cell.fill = HEAD_FILL; cell.alignment = { horizontal: 'center', vertical: 'middle', wrapText: true }; }
        else {
          cell.font = Object.assign({ name: FONT, size: 11 }, cell.font || {}, isTot ? { bold: true } : {}); if (!cell.font.name) cell.font.name = FONT;
          if (isTot) cell.fill = TOT_FILL;
          cell.alignment = Object.assign({}, cell.alignment || {}, { vertical: 'middle', horizontal: (cell.alignment && cell.alignment.horizontal) || (info.kind === 'n' ? 'right' : info.kind === 'd' ? 'center' : 'left'), wrapText: longCol[c] || /\n/.test(info.text) || (cell.alignment && cell.alignment.wrapText) || false });
        }
      }
      if (r === h) row.height = Math.max(row.height || 0, 24);
    }
    // phần trên dòng tiêu đề (tên báo cáo…): đậm + căn giữa nếu là 1 ô duy nhất
    for (let r = 1; r < h; r++) { const row = ws.getRow(r); for (let c = 1; c <= c1; c++) { const cell = row.getCell(c), i = cellInfo(cell); if (i.text && !(cell.font && cell.font.bold)) cell.font = Object.assign({ name: FONT, size: r === 1 ? 14 : 11 }, cell.font || {}, r === 1 ? { bold: true, size: 14 } : {}); } }
    // ---- cố định tiêu đề, bộ lọc, in ----
    try {
      if (!ws.views || !ws.views.length || !ws.views[0].state) ws.views = [{ state: 'frozen', ySplit: h }];
      let hasMergeInTable = false; for (let r = h; r <= r1; r++) if (merged.has(r + '-' + r) || Array.from(merged).some((k) => { const [a, b] = k.split('-').map(Number); return r >= a && r <= b; })) { hasMergeInTable = true; break; }
      if (!ws.autoFilter && !hasMergeInTable && r1 > h) ws.autoFilter = { from: { row: h, column: 1 }, to: { row: h, column: c1 } };
      if (!ws.pageSetup || !ws.pageSetup.fitToPage) { ws.pageSetup = Object.assign({ orientation: c1 > 6 ? 'landscape' : 'portrait', paperSize: 9 }, ws.pageSetup || {}, { fitToPage: true, fitToWidth: 1, fitToHeight: 0 }); ws.pageSetup.printTitlesRow = h + ':' + h; }
    } catch (e) { /* bỏ qua phần phụ */ }
    return { styled: true, header: h, rows: r1, cols: c1 };
  }

  function skipPage() { try { return !!(root.KL_XLSX_NOSTYLE || (root.location && NOSTYLE_PAGES.test(root.location.pathname || ''))); } catch (e) { return false; } }
  /** Định dạng mọi sheet của workbook ExcelJS (chỉ sheet chưa có định dạng riêng). */
  function styleWorkbook(wb, opts) {
    if (!wb || wb.__klSkipStyle || wb.__klStyled || (!opts && skipPage())) return [];
    wb.__klStyled = true; const out = [];
    wb.eachSheet((ws) => { try { out.push(Object.assign({ name: ws.name }, styleSheet(ws, opts))); } catch (e) { out.push({ name: ws.name, styled: false, why: String(e && e.message || e) }); } });
    return out;
  }

  // ---------------- SheetJS → ExcelJS ----------------
  const colName = (n) => { let s = ''; n++; while (n > 0) { const m = (n - 1) % 26; s = String.fromCharCode(65 + m) + s; n = Math.floor((n - 1) / 26); } return s; };
  const decode = (a) => { const m = /^([A-Z]+)(\d+)$/.exec(a); if (!m) return null; let c = 0; for (const ch of m[1]) c = c * 26 + ch.charCodeAt(0) - 64; return { r: +m[2] - 1, c: c - 1 }; };
  function fromSheetJS(wbj, ExcelJS) {
    const wb = new ExcelJS.Workbook();
    (wbj.SheetNames || []).forEach((name) => {
      const sj = wbj.Sheets[name]; if (!sj) return; const ws = wb.addWorksheet(String(name).replace(/[\\\/?*\[\]:]/g, ' ').slice(0, 31) || 'Sheet');
      Object.keys(sj).forEach((k) => {
        if (k[0] === '!') return; const p = decode(k); if (!p) return; const c = sj[k]; if (!c || (c.v == null && !c.f)) return; const cell = ws.getCell(p.r + 1, p.c + 1);
        let v = c.v; if (c.t === 'e') v = String(c.w || '#ERR'); if (c.t === 'd' && !(v instanceof Date)) v = new Date(v);
        if (c.f) cell.value = { formula: String(c.f).replace(/^=/, ''), result: v }; else cell.value = v;
        if (c.z && c.z !== 'General') cell.numFmt = c.z;
      });
      (sj['!merges'] || []).forEach((m) => { try { ws.mergeCells(m.s.r + 1, m.s.c + 1, m.e.r + 1, m.e.c + 1); } catch (e) { /* bỏ qua ô gộp trùng */ } });
      (sj['!cols'] || []).forEach((c, i) => { if (c && (c.wch || c.width || c.wpx)) ws.getColumn(i + 1).width = c.wch || c.width || Math.round(c.wpx / 7); if (c && c.hidden) ws.getColumn(i + 1).hidden = true; });
    });
    return wb;
  }

  /** Workbook SheetJS đã có kiểu ô riêng (xlsx-js-style: ô có thuộc tính `s`) → module tự định dạng, KHÔNG chuyển đổi (khỏi mất màu / mẫu). */
  function hasCellStyles(wbj) {
    for (const n of (wbj && wbj.SheetNames) || []) { const sj = wbj.Sheets[n]; if (!sj) continue; let k = 0; for (const a in sj) { if (a[0] === '!') continue; if (sj[a] && sj[a].s && typeof sj[a].s === 'object' && Object.keys(sj[a].s).length) return true; if (++k > 3000) break; } }
    return false;
  }
  const api = { hasCellStyles, styleSheet, styleWorkbook, fromSheetJS, cellInfo, skipPage, NOSTYLE_PAGES };
  root.KLXS = api; if (typeof module !== 'undefined' && module.exports) module.exports = api;
  if (typeof document === 'undefined') return;

  // =====================================================================================================================
  // GẮN VÀO ExcelJS / SheetJS (trình duyệt)
  // =====================================================================================================================
  function download(buf, name) {
    const a = document.createElement('a'); a.href = URL.createObjectURL(new Blob([buf], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' })); a.download = name; document.body.appendChild(a); a.click(); setTimeout(() => { URL.revokeObjectURL(a.href); a.remove(); }, 1500);
  }
  function patchExcelJS(E) {
    if (!E || !E.Workbook || E.Workbook.__klp) return; E.Workbook.__klp = true;
    const d = Object.getOwnPropertyDescriptor(E.Workbook.prototype, 'xlsx'); if (!d || !d.get) return;
    Object.defineProperty(E.Workbook.prototype, 'xlsx', { configurable: true, enumerable: d.enumerable, set: d.set, get() {
      const x = d.get.call(this), wb = this; if (!x || x.__klw) return x; x.__klw = true;
      ['writeBuffer', 'write', 'writeFile'].forEach((k) => { const o = x[k]; if (typeof o !== 'function') return; x[k] = function () { try { styleWorkbook(wb); } catch (e) { if (root.console) console.error('KLXS:', e); } return o.apply(this, arguments); }; });
      return x;
    } });
  }
  let _E = root.ExcelJS;
  if (_E) patchExcelJS(_E); else { try { Object.defineProperty(root, 'ExcelJS', { configurable: true, get() { return _E; }, set(v) { _E = v; patchExcelJS(v); } }); } catch (e) { /* bỏ qua */ } }

  function patchSheetJS(X) {
    if (!X || !X.writeFile || X.__klp) return; X.__klp = true; const orig = X.writeFile;
    X.writeFile = function (wb, filename, opts) {
      const args = arguments, isX = /\.xlsx$/i.test(String(filename || '')) && (!opts || !opts.bookType || opts.bookType === 'xlsx');
      if (!isX || skipPage() || (wb && wb.__klSkipStyle) || hasCellStyles(wb)) return orig.apply(this, args);   // có kiểu ô riêng → giữ nguyên
      const run = async () => {
        if (!root.ExcelJS && typeof root.klNap === 'function') await root.klNap('exceljs');
        if (!root.ExcelJS) throw new Error('chưa có ExcelJS');
        const ew = fromSheetJS(wb, root.ExcelJS); styleWorkbook(ew, { force: true }); download(await ew.xlsx.writeBuffer(), filename);
      };
      run().catch((e) => { if (root.console) console.error('KLXS: xuất có định dạng lỗi, dùng bản thường —', e); try { orig.apply(X, args); } catch (e2) { /* bỏ qua */ } });
    };
  }
  /** Gọi khi trang nạp lười SheetJS / ExcelJS xong. */
  api.patchAll = function () { if (root.ExcelJS) patchExcelJS(root.ExcelJS); if (root.XLSX) patchSheetJS(root.XLSX); };
  api.patchAll();
  let tries = 0; const tm = setInterval(() => { api.patchAll(); if ((root.XLSX && root.XLSX.__klp && (!root.ExcelJS || root.ExcelJS.Workbook.__klp)) || ++tries > 60) clearInterval(tm); }, 500);   // SheetJS nạp sau (xlsx.full.min.js / xlsx-js-style) vẫn được bọc
})(typeof window !== 'undefined' ? window : globalThis);
