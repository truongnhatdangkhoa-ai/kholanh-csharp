/* ============================================================
   PC 5.26 — KLSortHead: BẤM TIÊU ĐỀ CỘT ĐỂ SẮP XẾP A→Z / Z→A CHO CÁC BẢNG PHIẾU XUẤT
   ------------------------------------------------------------
   Cách dùng: gắn thuộc tính  data-kl-sort  lên <table> (vẽ hết dòng vào trang, kiểu DOM):
       <table data-kcf data-kcf-key="m1:phieudt-today" data-kl-sort="m1:phieudt-today">
     • data-kl-sort="khóa"  → NHỚ cách sắp khi bảng bị vẽ lại (innerHTML) — cùng khóa = cùng bảng.
     • data-kl-sort=""      → không nhớ (bảng trong hộp thoại, mở lại là về thứ tự gốc).
   Bấm tiêu đề cột: A→Z (▲) → Z→A (▼) → bỏ sắp xếp (về thứ tự gốc).
   Tự nhận: số (1, 2, 10 và 4,470.5), ngày (dd/mm/yyyy, "hh:mm:ss d/m/yyyy"), chữ tiếng Việt (có số bên trong).
   Ô trống luôn xuống cuối. Ô chọn (select) sắp theo chữ đang chọn; ô nhập (input) sắp theo giá trị đang nhập.
   Không đụng tới bộ lọc cột KLColFilter: dòng bị lọc ẩn vẫn ẩn, chỉ đổi thứ tự dòng.
   ============================================================ */
(function () {
  'use strict';
  if (window.KLSortHead) return;

  const SEL = 'table[data-kl-sort]';
  const keyStates = new Map();      // khóa → { k: khóa cột, dir: 'asc' | 'desc' }
  const tableStates = new WeakMap(); // bảng không có khóa
  const NUM = /^[+-]?(\d{1,3}(,\d{3})+|\d+)(\.\d+)?$/;
  const DMY = /^(\d{1,2})\/(\d{1,2})\/(\d{4})(?:\s+(\d{1,2}):(\d{2})(?::(\d{2}))?)?/;
  const HMS_DMY = /^(\d{1,2}):(\d{2})(?::(\d{2}))?\s+(\d{1,2})\/(\d{1,2})\/(\d{4})/;

  const stateOf = (t) => { const k = t.getAttribute('data-kl-sort'); return k ? keyStates.get(k) : tableStates.get(t); };
  const setState = (t, st) => {
    const k = t.getAttribute('data-kl-sort');
    if (k) { if (st) keyStates.set(k, st); else keyStates.delete(k); }
    else if (st) tableStates.set(t, st); else tableStates.delete(t);
  };

  function headRow(t) {
    if (!t.tHead) return null;
    for (let i = 0; i < t.tHead.rows.length; i++) if (!t.tHead.rows[i].classList.contains('kcf-row')) return t.tHead.rows[i];
    return null;
  }
  const thLabel = (th) => (th.textContent || '').replace(/\s+/g, ' ').trim();
  const colKey = (th) => th.getAttribute('data-kcf-col') || thLabel(th) || String(th.cellIndex);
  const sortable = (th) => !th.classList.contains('chk-col') && !th.hasAttribute('data-kl-nosort') && thLabel(th) !== '' && th.colSpan <= 1;

  function cellText(td) {
    if (!td) return '';
    if (td.hasAttribute('data-sort-v')) return String(td.getAttribute('data-sort-v')).trim();
    const sel = td.querySelector('select');
    if (sel) { const o = sel.options[sel.selectedIndex]; return o && o.value !== '' ? String(o.text).trim() : ''; }
    const inp = td.querySelector('input:not([type=checkbox]):not([type=radio]), textarea');
    if (inp) return String(inp.value || '').trim();
    return (td.textContent || '').replace(/\s+/g, ' ').trim();
  }
  // Khóa so sánh: { t: 'e' (trống) | 'd' (ngày) | 'n' (số) | 's' (chữ), v }
  function keyOf(raw) {
    const s = String(raw == null ? '' : raw).trim();
    if (s === '' || s === '—' || s === '-') return { t: 'e', v: '' };
    let m = HMS_DMY.exec(s);
    if (m) return { t: 'd', v: Date.UTC(+m[6], +m[5] - 1, +m[4], +m[1], +m[2], +(m[3] || 0)) };
    m = DMY.exec(s);
    if (m) return { t: 'd', v: Date.UTC(+m[3], +m[2] - 1, +m[1], +(m[4] || 0), +(m[5] || 0), +(m[6] || 0)) };
    if (NUM.test(s)) return { t: 'n', v: parseFloat(s.replace(/,/g, '')) };
    return { t: 's', v: s.replace(/^[^\p{L}\p{N}]+/u, '') }; // bỏ biểu tượng đầu chữ (🖥 PC, 🔒 Đã khóa…)
  }
  function cmpKeys(a, b) {
    if (a.t === b.t && a.t !== 's') return a.v < b.v ? -1 : a.v > b.v ? 1 : 0;
    return String(a.v).localeCompare(String(b.v), 'vi', { numeric: true, sensitivity: 'base' });
  }

  function dataRows(t) {
    const tb = t.tBodies[0]; if (!tb) return [];
    return Array.from(tb.rows);
  }
  function stampOrder(rows) { rows.forEach((r, i) => { if (r._klOrd === undefined) r._klOrd = i; }); }

  function expectedOrder(t, st) {
    const rows = dataRows(t);
    if (!rows.length) return rows;
    stampOrder(rows);
    if (!st) return rows.slice().sort((a, b) => a._klOrd - b._klOrd);
    const hr = headRow(t); if (!hr) return rows;
    let ci = -1;
    for (let i = 0; i < hr.cells.length; i++) if (colKey(hr.cells[i]) === st.k) { ci = i; break; }
    if (ci < 0) return rows;
    if (rows.some((r) => r.cells.length !== hr.cells.length)) return rows; // có dòng gộp ô / dòng thông báo → không sắp
    const dir = st.dir === 'desc' ? -1 : 1;
    return rows.map((r) => ({ r, k: keyOf(cellText(r.cells[ci])) }))
      .sort((x, y) => {
        if (x.k.t === 'e' || y.k.t === 'e') { if (x.k.t === y.k.t) return x.r._klOrd - y.r._klOrd; return x.k.t === 'e' ? 1 : -1; } // trống luôn cuối
        return (cmpKeys(x.k, y.k) * dir) || (x.r._klOrd - y.r._klOrd);
      }).map((x) => x.r);
  }

  const busy = new WeakSet();
  function refresh(t) {
    if (busy.has(t)) return;
    busy.add(t);
    try {
      const hr = headRow(t);
      const st = stateOf(t);
      if (hr) {
        Array.from(hr.cells).forEach((th) => {
          if (!sortable(th)) return;
          if (!th.classList.contains('kl-sh')) {
            th.classList.add('kl-sh');
            th.title = (th.title ? th.title + ' — ' : '') + 'Bấm để sắp xếp A→Z / Z→A / bỏ sắp xếp';
            const ind = document.createElement('span'); ind.className = 'kl-sh-ind'; ind.setAttribute('aria-hidden', 'true');
            th.appendChild(ind);
          }
          const on = !!st && colKey(th) === st.k;
          if (on) th.setAttribute('data-kl-dir', st.dir); else th.removeAttribute('data-kl-dir');
        });
      }
      const rows = dataRows(t), want = expectedOrder(t, st);
      if (rows.length && rows.some((r, i) => r !== want[i])) {
        const tb = t.tBodies[0], frag = document.createDocumentFragment();
        want.forEach((r) => frag.appendChild(r));
        tb.appendChild(frag);
      }
    } finally {
      if (t._klMo) t._klMo.takeRecords(); // bỏ qua các thay đổi do chính mình gây ra
      busy.delete(t);
    }
  }

  function onHeadClick(t, e) {
    if (e.target.closest('input, button, select, textarea, a, label, .col-resizer, .row-resizer, .kcf-row')) return;
    const th = e.target.closest('th'); const hr = headRow(t);
    if (!th || !hr || th.parentElement !== hr || !sortable(th)) return;
    const k = colKey(th), cur = stateOf(t);
    let next = null;
    if (!cur || cur.k !== k) next = { k, dir: 'asc' };
    else if (cur.dir === 'asc') next = { k, dir: 'desc' };   // lần 3 → next = null (về thứ tự gốc)
    setState(t, next);
    refresh(t);
  }

  function attach(t) {
    if (t._klSh) { refresh(t); return; }
    t._klSh = true;
    t.addEventListener('click', (e) => { if (e.target.closest('thead')) onHeadClick(t, e); });
    let raf = 0;
    t._klMo = new MutationObserver(() => { if (raf) return; raf = requestAnimationFrame(() => { raf = 0; refresh(t); }); });
    t._klMo.observe(t, { childList: true, subtree: true });
    refresh(t);
  }
  function scan(root) {
    const r = root && root.querySelectorAll ? root : document;
    if (r.matches && r.matches(SEL)) attach(r);
    r.querySelectorAll(SEL).forEach(attach);
  }

  let scanRaf = 0;
  const bodyMo = new MutationObserver(() => { if (scanRaf) return; scanRaf = requestAnimationFrame(() => { scanRaf = 0; scan(document); }); });
  function boot() { scan(document); bodyMo.observe(document.body, { childList: true, subtree: true }); }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot); else boot();

  window.KLSortHead = { attach, scan, refresh, getState: stateOf, cmpKeys, keyOf };
})();
