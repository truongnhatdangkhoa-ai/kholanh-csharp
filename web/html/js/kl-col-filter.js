/* ============================================================
   PC 1.18 — KLColFilter: Ô TÌM DƯỚI TIÊU ĐỀ CỘT + LỌC PHÂN TẦNG — 1 CHUẨN DUY NHẤT CHO TOÀN APP
   ------------------------------------------------------------
   Chuẩn lấy từ module M04 (NXT TNK/TGC), trước đây bị CHÉP RIÊNG ở M03, M04, M05, M06, M08 (mỗi bản lệch
   nhau: bản M05/M06/M08 không phân tầng, bản M05/M06 chỉ lọc trong TRANG đang xem, bản M04 không gắn cho
   bảng vẽ sau). Nay gom về ĐÚNG 1 file này; các module chỉ khai báo, không tự viết lại.

   Mỗi bảng có:
     • 1 hàng ngay dưới tiêu đề (tr.col-search-row.kcf-row): mỗi cột 1 ô "Tìm …" + nút kính lúp.
       – Gõ → hiện gợi ý (chỉ giá trị còn khớp các cột KHÁC đang lọc); bấm gợi ý / Enter = lọc ĐÚNG giá trị đó;
         xóa trắng ô = bỏ lọc cột.
       – Nút kính lúp → hộp tick nhiều giá trị (openColSearchPopoverGeneric trong table-col-search.js),
         lọc ngay, có đếm số dòng, A→Z / Z→A, (Tất cả), Thêm vào bộ lọc hiện tại.
     • Lọc PHÂN TẦNG: danh sách giá trị của 1 cột chỉ lấy từ các dòng còn lại sau khi lọc ở các cột khác.
     • Lọc trên TOÀN BỘ dữ liệu (kể cả bảng chia trang) — xem "Nguồn dữ liệu" bên dưới.

   CÁCH DÙNG
     1. Bảng nhỏ / vẽ hết dòng vào trang (kiểu DOM): chỉ cần gắn thuộc tính lên <table>:
          <table data-kcf data-kcf-key="ten-bang-duy-nhat">
        – data-kcf-key (nên có): nhớ bộ lọc khi bảng bị vẽ lại toàn bộ (innerHTML).
        – Bảng tự được gắn hàng tìm (theo dõi trang), tự lọc lại sau mỗi lần vẽ lại tbody.
     2. Trang muốn gắn cho MỌI bảng (M03, M04, M05, M06 — như trước): KLColFilter.autoAll();
        Bảng KHÔNG muốn gắn: thêm data-no-col-search (trên <table> hoặc khung bao ngoài).
     3. Bảng CHIA TRANG / vẽ lại từ mảng dữ liệu (kiểu NGUỒN): trong hàm vẽ, thay vì tự cắt trang:
          const info = KLColFilter.paginate(tableOrKey, items, {
            cell: (item, ci) => 'chữ hiển thị ở cột ci',   // ci = thứ tự cột theo tiêu đề (0,1,2…)
            page, pageSize, rerender: () => { page = 1; veLai(); } });
          → info.pageItems / info.totalFiltered / info.page / info.totalPages
        Hoặc chỉ lọc (không chia trang): KLColFilter.filterItems(tableOrKey, items, cell)
        + KLColFilter.source(tableOrKey, { items, cell, rerender }).
     4. Module có SẴN bộ lọc dạng { cột: Set } (vd Số liệu cuối kỳ): KLColFilter.useFilters(key, obj) để hàng
        tìm ghi thẳng vào object đó (module vẫn tự lọc như cũ).
   ============================================================ */
(function () {
  'use strict';
  if (window.KLColFilter) return;

  const FOREIGN_ROW_SEL = 'tr.col-search-row:not(.kcf-row), tr.search-row, tr.filter-row, tr.history-search-row';
  const SKIP_LABELS = new Set(['#', 'STT']); // cột nút bấm (Sửa/Xóa…): module gắn data-kcf-skip lên <th>
  const tableFilters = new WeakMap();
  const keyFilters = new Map();
  const tableSources = new WeakMap();
  const keySources = new Map();
  let autoAllOn = false;

  const debounce = (fn, ms) => {
    if (window.KLAType && typeof KLAType.debounce === 'function') return KLAType.debounce(fn, ms);
    let t = null; const d = function () { const a = arguments; clearTimeout(t); t = setTimeout(() => { t = null; fn.apply(null, a); }, ms); };
    d.cancel = () => { clearTimeout(t); t = null; }; d.flush = () => { if (t) { clearTimeout(t); t = null; fn(); } };
    return d;
  };
  const norm = (s) => String(s == null ? '' : s).replace(/\s+/g, ' ').trim();
  const isSet = (v) => v instanceof Set;
  const escHtml = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

  function getCellValue(row, colIndex) {
    const cell = row.children[colIndex];
    if (!cell) return '';
    const field = cell.querySelector('input:not([type=checkbox]):not([type=radio]), select, textarea');
    if (field) return (field.value || '').toString();
    return (cell.textContent || '').toString();
  }
  function headRow(table) {
    const th = table.tHead; if (!th) return null;
    for (let i = 0; i < th.rows.length; i++) if (!th.rows[i].classList.contains('kcf-row')) return th.rows[i];
    return null;
  }
  function ownRow(table) { return table.tHead ? table.tHead.querySelector(':scope > tr.kcf-row') : null; }
  function keyOf(t) { return typeof t === 'string' ? t : (t && t.dataset && t.dataset.kcfKey) || null; }
  function tablesOf(t) {
    if (typeof t !== 'string') return t ? [t] : [];
    return Array.from(document.querySelectorAll('table[data-kcf-key]')).filter((x) => x.dataset.kcfKey === t);
  }

  // ---------- bộ lọc { chỉSốCột: Set(giá trị) } ----------
  function filtersFor(t) {
    const k = keyOf(t);
    if (k) { let f = keyFilters.get(k); if (!f) { f = {}; keyFilters.set(k, f); } return f; }
    let f = tableFilters.get(t); if (!f) { f = {}; tableFilters.set(t, f); } return f;
  }
  function useFilters(key, obj) { if (key && obj) keyFilters.set(key, obj); return obj; }
  function activeEntries(f, exceptKey) {
    return Object.keys(f).filter((k) => k !== String(exceptKey) && isSet(f[k])).map((k) => [k, f[k]]);
  }

  // ---------- nguồn dữ liệu (bảng chia trang / vẽ từ mảng) ----------
  function source(t, src) {
    const k = keyOf(t);
    if (k) keySources.set(k, src); else if (t) tableSources.set(t, src);
    return src;
  }
  function sourceFor(table) {
    const k = keyOf(table);
    if (k && keySources.has(k)) return keySources.get(k);
    if (tableSources.has(table)) return tableSources.get(table);
    // Tương thích M04: filterAndPaginate() đăng ký nguồn kiểu dựng dòng HTML "ảo"
    const legacy = window.__paginatedTableSources && window.__paginatedTableSources.get(table);
    if (legacy) {
      const rr = window.__paginatedTableRenders && window.__paginatedTableRenders.get(table);
      return { legacy: true, items: legacy.items, rowHtmlFn: legacy.rowHtmlFn, colCount: legacy.templateColCount, rerender: rr || null };
    }
    return null;
  }
  function offsetFor(table, src) {
    const hr = table ? headRow(table) : null;
    if (!hr || !src || src.colCount == null) return 0;
    return hr.cells.length - src.colCount;
  }
  // Trả hàm đọc giá trị ô (đã chuẩn hóa) theo CHỈ SỐ CỘT SỐNG
  function srcCellReader(src, offset) {
    if (src.cell && src.byKey) return (item, k) => norm(src.cell(item, k)); // cột đặt tên riêng (th[data-kcf-col])
    if (src.cell) return (item, liveIdx) => norm(src.cell(item, Number(liveIdx) - offset));
    // Nguồn chỉ có hàm dựng HTML 1 dòng: dựng dòng "ảo" (không gắn vào trang) để đọc chữ từng ô — nhớ lại theo từng
    // phần tử để mỗi dòng chỉ dựng 1 lần cho mỗi lần vẽ.
    const wrapper = document.createElement('tbody');
    const cache = src._cache || (src._cache = new Map());
    return (item, liveIdx) => {
      let arr = cache.get(item);
      if (!arr) {
        wrapper.innerHTML = src.rowHtmlFn(item);
        const tr = wrapper.firstElementChild;
        arr = tr ? Array.from(tr.children).map((c, i) => norm(getCellValue(tr, i))) : [];
        cache.set(item, arr);
      }
      return arr[Number(liveIdx) - offset] || '';
    };
  }
  function filterItemsWith(t, items, src) {
    const act = activeEntries(filtersFor(t));
    if (!act.length || !items || !items.length || !src) return items || [];
    const table = typeof t === 'string' ? tablesOf(t)[0] : t;
    const read = srcCellReader(src, offsetFor(table, src));
    return items.filter((it) => act.every(([k, set]) => set.has(read(it, k))));
  }
  function filterItems(t, items, cell) {
    const table = typeof t === 'string' ? tablesOf(t)[0] : t;
    const src = cell ? { cell } : (table ? sourceFor(table) : keySources.get(keyOf(t)));
    return filterItemsWith(t, items, src);
  }
  function paginate(t, items, o) {
    o = o || {};
    const src = source(t, { items, cell: o.cell, rowHtmlFn: o.rowHtml, colCount: o.colCount, rerender: o.rerender, blank: !!o.blank, byKey: !!o.byKey });
    const passing = sortItemsWith(t, filterItemsWith(t, items, src), src);
    const pageSize = o.pageSize || (typeof TABLE_PAGE_SIZE !== 'undefined' ? TABLE_PAGE_SIZE : 25);
    const totalPages = Math.max(1, Math.ceil(passing.length / pageSize));
    const page = Math.min(Math.max(1, o.page || 1), totalPages);
    const start = (page - 1) * pageSize;
    return { pageItems: passing.slice(start, start + pageSize), filtered: passing, totalFiltered: passing.length, totalPages, page };
  }

  // ---------- SẮP XẾP trên TOÀN BỘ dữ liệu (PC 1.19) ----------
  // Bảng chia trang: bấm tiêu đề để sắp xếp trước đây chỉ đảo các dòng của TRANG đang xem. Nay module gọi
  // KLColFilter.setSort(table, cột, 'asc'|'desc') → nhớ cách sắp, vẽ lại; paginate() sắp toàn bộ dòng đã lọc rồi mới cắt trang.
  const tableSorts = new WeakMap(), keySorts = new Map();
  function sortOf(t) { const k = keyOf(t); return k ? keySorts.get(k) : (t && typeof t === 'object' ? tableSorts.get(t) : null); }
  function setSortState(t, st) { const k = keyOf(t); if (k) { if (st) keySorts.set(k, st); else keySorts.delete(k); } else if (t) { if (st) tableSorts.set(t, st); else tableSorts.delete(t); } }
  const DMY = /^(\d{1,2})\/(\d{1,2})\/(\d{4})(?:\s+(\d{1,2}):(\d{2})(?::(\d{2}))?)?$/;
  function sortCmp(a, b) {
    if (a === b) return 0;
    if (a === '' || a === '—') return 1;          // ô trống luôn xuống cuối
    if (b === '' || b === '—') return -1;
    const da = DMY.exec(a), db = DMY.exec(b);    // ngày dd/mm/yyyy (kèm giờ) → so theo thời gian
    if (da && db) {
      const ka = da[3] + da[2].padStart(2, '0') + da[1].padStart(2, '0') + (da[4] || '0').padStart(2, '0') + (da[5] || '00') + (da[6] || '00');
      const kb = db[3] + db[2].padStart(2, '0') + db[1].padStart(2, '0') + (db[4] || '0').padStart(2, '0') + (db[5] || '00') + (db[6] || '00');
      return ka < kb ? -1 : ka > kb ? 1 : 0;
    }
    if (typeof colSearchCompare === 'function') return colSearchCompare(a, b);
    return a.localeCompare(b, 'vi', { numeric: true, sensitivity: 'base' });
  }
  function sortItemsWith(t, items, src) {
    const st = sortOf(t);
    if (!st || !src || !items || items.length < 2) return items || [];
    const table = typeof t === 'string' ? tablesOf(t)[0] : t;
    const read = srcCellReader(src, offsetFor(table, src));
    const dir = st.dir === 'desc' ? -1 : 1;
    return items.map((it, i) => ({ it, i, v: read(it, st.k) }))
      .sort((x, y) => (sortCmp(x.v, y.v) * (x.v === '' || y.v === '' ? 1 : dir)) || (x.i - y.i))
      .map((x) => x.it);
  }
  // Module gọi khi người dùng bấm tiêu đề cột. Trả true = bảng kiểu NGUỒN đã được sắp lại trên toàn bộ dữ liệu
  // (module KHÔNG cần tự đảo dòng DOM nữa); false = bảng vẽ hết dòng → module tự sắp DOM như cũ.
  function setSort(table, liveIdx, dir) {
    if (!table) return false;
    const src = sourceFor(table);
    if (!src || typeof src.rerender !== 'function') return false;
    const hr = headRow(table), th = hr && hr.cells[liveIdx];
    setSortState(table, th ? { k: colKeyOf(th), dir: dir === 'desc' ? 'desc' : 'asc' } : null);
    src.rerender();
    return true;
  }
  function sortItems(t, items) { const table = typeof t === 'string' ? tablesOf(t)[0] : t; return sortItemsWith(t, items, table ? sourceFor(table) : keySources.get(keyOf(t))); }
  // Dòng để XUẤT EXCEL: bảng kiểu nguồn → TOÀN BỘ dòng đang lọc (đã sắp), dựng thành <tr> rời (không gắn vào trang);
  // bảng vẽ hết dòng → các dòng không bị ẩn bởi bộ lọc cột. Trả null nếu không phải bảng do KLColFilter quản lý.
  function exportRows(table) {
    if (!table) return null;
    const src = sourceFor(table);
    if (src && Array.isArray(src.items)) {
      const items = sortItemsWith(table, filterItemsWith(table, src.items, src), src);
      const tb = document.createElement('tbody');
      const hr = headRow(table), n = hr ? hr.cells.length - offsetFor(table, src) : 0;
      return items.map((it) => {
        if (src.rowHtmlFn) { tb.innerHTML = src.rowHtmlFn(it); return tb.firstElementChild; }
        const tr = document.createElement('tr');
        for (let c = 0; c < n; c++) { const td = document.createElement('td'); td.textContent = src.cell ? String(src.cell(it, src.byKey && hr ? colKeyOf(hr.cells[c]) : c) ?? '') : ''; tr.appendChild(td); }
        return tr;
      }).filter(Boolean);
    }
    if (!table._kcf) return null;
    return dataRows(table).filter((r) => r.dataset.colFilterHidden !== '1');
  }

  // ---------- giá trị của 1 cột (THÔ, có lặp — để hộp lọc đếm số dòng), đã lọc phân tầng ----------
  // Khóa bộ lọc của 1 cột: th[data-kcf-col] (module tự đặt tên cột) hoặc thứ tự cột hiện tại
  function colKeyOf(th) { return th.dataset.kcfCol || String(th.cellIndex); }
  function idxOfKey(table, k) {
    if (/^\d+$/.test(String(k))) return Number(k);
    const hr = headRow(table); if (!hr) return -1;
    for (let i = 0; i < hr.cells.length; i++) if (hr.cells[i].dataset.kcfCol === String(k)) return i;
    return -1;
  }
  function columnValues(table, liveIdx) {
    const f = filtersFor(table), others = activeEntries(f, liveIdx);
    const src = sourceFor(table), out = [];
    if (src) {
      const read = srcCellReader(src, offsetFor(table, src));
      (src.items || []).forEach((it) => {
        for (const [k, set] of others) if (!set.has(read(it, k))) return;
        out.push(read(it, liveIdx));
      });
      return out;
    }
    dataRows(table).forEach((row) => {
      for (const [k, set] of others) if (!set.has(norm(getCellValue(row, idxOfKey(table, k))))) return;
      out.push(norm(getCellValue(row, idxOfKey(table, liveIdx))));
    });
    return out;
  }
  function dataRows(table) {
    const out = [];
    for (let b = 0; b < table.tBodies.length; b++) {
      const rows = table.tBodies[b].rows;
      for (let i = 0; i < rows.length; i++) {
        const r = rows[i];
        if (r.children.length === 1 && r.children[0].hasAttribute('colspan')) continue; // dòng "chưa có dữ liệu"
        if (r.classList.contains('row-total') || r.classList.contains('kcf-skip')) continue;  // dòng tổng
        let merged = false; for (let c = 0; c < r.cells.length; c++) if (r.cells[c].colSpan > 1) { merged = true; break; }
        if (merged) continue; // dòng có ô gộp (tổng ngày, tiêu đề nhóm…) — không phải dòng dữ liệu, luôn để hiện
        out.push(r);
      }
    }
    return out;
  }

  // ---------- áp bộ lọc ----------
  function markHeaders(table) {
    const hr = headRow(table), own = ownRow(table); if (!hr) return;
    const f = filtersFor(table);
    Array.from(hr.cells).forEach((th, i) => {
      const on = isSet(f[colKeyOf(th)]);
      th.classList.toggle('has-filter', on);
      const td = own && own.cells[i];
      const b = td && td.querySelector('.th-filter-btn'); if (b) b.classList.toggle('active', on);
    });
  }
  // Bảng bị vẽ lại TOÀN BỘ (innerHTML) sau mỗi lần lọc → phần tử cũ đã rời trang; tìm bảng mới cùng data-kcf-key
  function live(table) {
    if (!table || table.isConnected) return table;
    const k = keyOf(table); const t2 = k ? tablesOf(k)[0] : null;
    return t2 || table;
  }
  function applyFilters(table) {
    table = live(table);
    if (!table) return;
    if (!table.isConnected) { const s0 = sourceFor(table); if (s0 && typeof s0.rerender === 'function') s0.rerender(); return; }
    const src = sourceFor(table);
    if (src && typeof src.rerender === 'function') { // bảng vẽ từ mảng dữ liệu: vẽ lại để lọc trên TOÀN BỘ dữ liệu, không chỉ trang đang hiện
      src.rerender();
      tablesOf(keyOf(table) || table).forEach(markHeaders);
      return;
    }
    const act = activeEntries(filtersFor(table));
    dataRows(table).forEach((row) => {
      let visible = true;
      for (const [k, set] of act) { if (!set.has(norm(getCellValue(row, idxOfKey(table, k))))) { visible = false; break; } }
      // Ghi cờ (không chỉ style) — tính năng "Ẩn dòng/Ẩn cột" ở M03/M04 đọc cờ này để không hiện lại dòng đang bị lọc.
      row.dataset.colFilterHidden = visible ? '' : '1';
      row.style.display = (visible && row.dataset.expCollapsed !== '1') ? '' : 'none';
    });
    markHeaders(table);
    try { table.dispatchEvent(new CustomEvent('kcf:filtered', { bubbles: true })); } catch (e) { /* bỏ qua */ }
  }
  function refresh(t) { tablesOf(t).forEach((tb) => { syncInputs(tb); markHeaders(tb); }); }
  function clear(t) {
    const f = filtersFor(t); Object.keys(f).forEach((k) => delete f[k]);
    const tbs = tablesOf(t);
    tbs.forEach(syncInputs);
    if (tbs.length) applyFilters(tbs[0]); else { const s = keySources.get(keyOf(t)); if (s && s.rerender) s.rerender(); }
    tbs.slice(1).forEach(markHeaders);
  }
  function activeCount(t) { return activeEntries(filtersFor(t)).length; }

  // ---------- gợi ý thả xuống dưới ô tìm (đặt cố định theo màn hình để không bị khung cuộn cắt mất) ----------
  let dd = null;
  function closeDropdown() { if (dd) { dd.remove(); dd = null; } }
  window.__kcfCloseDropdown = closeDropdown;
  document.addEventListener('mousedown', (e) => {
    if (dd && !dd.contains(e.target) && !(e.target.closest && e.target.closest('.kcf-input'))) closeDropdown();
  }, true);
  document.addEventListener('scroll', (e) => { if (dd && !dd.contains(e.target)) closeDropdown(); }, true);
  window.addEventListener('resize', closeDropdown);

  function syncInputs(table) {
    const own = ownRow(table); if (!own) return;
    const f = filtersFor(table);
    own.querySelectorAll('input.kcf-input').forEach((inp) => {
      const td = inp.closest('td'); const hr = headRow(table);
      const th = td && hr ? hr.cells[td.cellIndex] : null;
      const s = th ? f[colKeyOf(th)] : undefined;
      if (document.activeElement === inp) return;
      inp.value = (isSet(s) && s.size === 1 && s._kcfText != null) ? s._kcfText : '';
    });
    markHeaders(table);
  }

  // ---------- gắn hàng tìm ----------
  function labelOf(th) {
    if (th.hasAttribute('data-kcf-skip')) return '';
    const sp = th.querySelector('.th-label');
    const raw = sp ? sp.textContent : (th.dataset.kcfLabel || th.textContent);
    const s = norm(raw).replace(/[\s▾▸⇅↑↓▲▼]+$/, '').trim();
    return SKIP_LABELS.has(s) ? '' : s;
  }
  function eligible(table, byCall) {
    if (!table || table.tagName !== 'TABLE' || !table.isConnected) return false;
    if (!table.tHead || !table.tBodies.length) return false;
    if (table.closest('[data-no-col-search]') || table.closest('.hidemgr-toolbar')) return false;
    const explicit = byCall || table.hasAttribute('data-kcf');
    if (!explicit) {
      if (!autoAllOn) return false;
      if (table.dataset.searchReady && !table._kcf) return false; // bảng tự khai báo không cần (data-search-ready)
      // Bảng đã có bộ lọc riêng (icon [data-col-search], M03) — không chồng thêm
      const scope = table.closest('.card') || table.closest('.data-wrap') || table;
      if (scope.querySelector('[data-col-search]')) return false;
    }
    if (table.tHead.querySelector(FOREIGN_ROW_SEL)) return false;
    const hr = headRow(table); if (!hr || !hr.cells.length) return false;
    for (let i = 0; i < table.tHead.rows.length; i++) { const r = table.tHead.rows[i]; if (r !== hr && !r.classList.contains('kcf-row')) return false; } // tiêu đề nhiều tầng
    for (let i = 0; i < hr.cells.length; i++) if (hr.cells[i].colSpan > 1) return false;
    return Array.from(hr.cells).some((th) => !!labelOf(th));
  }

  /* ---------- PC 1.96 — NÚT ẨN / HIỆN CỘT TRỐNG (chỉ trang đặt data-kcf-emptycols trên <body>: nhóm Đối chiếu số liệu) ----------
     Nút chữ "⊟ Ẩn cột trống" nằm chung dòng nút phía trên bảng (PC 1.97; trước đó là icon nhỏ ở hàng tìm). Bật → ẩn mọi cột KHÔNG có dữ liệu ở bất kỳ dòng nào (ô trống hoặc chỉ "—").
     • Tính trên TOÀN BỘ dữ liệu của bảng (kể cả bảng chia trang, qua nguồn KLColFilter.source), không chỉ trang đang xem; không phụ thuộc bộ lọc cột.
     • Không bao giờ ẩn cột đầu, cột không có tên tiêu đề (cột nút bấm) và cột #/STT. Số 0 là có dữ liệu (không ẩn).
     • Ô trải nhiều cột (dòng TỔNG…) tự co lại theo số cột còn hiện. Chỉ ẩn trên màn hình — Excel xuất ra không đổi.
     • Nhớ lựa chọn theo từng bảng (localStorage kl_ec:<trang>:<bảng>) và áp lại mỗi khi bảng vẽ lại. Bảng không muốn có nút: data-no-empty-cols. */
  const ecOnPage = () => !!(document.body && document.body.hasAttribute('data-kcf-emptycols'));
  const ecAllowed = (table) => ecOnPage() && !table.hasAttribute('data-no-empty-cols') && !table.closest('[data-no-empty-cols]');
  const ecEmptyVal = (v) => v === '' || /^[\u2014\u2013-]+$/.test(v);
  function ecKey(table) {
    const hr = headRow(table), sig = hr ? Array.from(hr.cells).map(labelOf).join('|') : '';
    return 'kl_ec:' + (location.pathname.split('/').pop() || 'index') + ':' + (keyOf(table) || sig);
  }
  function ecState(table) { try { return localStorage.getItem(ecKey(table)) === '1'; } catch (e) { return false; } }
  function ecSave(table, on) { try { if (on) localStorage.setItem(ecKey(table), '1'); else localStorage.removeItem(ecKey(table)); } catch (e) { /* bỏ qua */ } }
  function ecColumns(table) { // → Set chỉ số cột (theo hàng tiêu đề) không có dữ liệu
    const hr = headRow(table), out = new Set(); if (!hr) return out;
    const cand = [];
    for (let i = 1; i < hr.cells.length; i++) { const th = hr.cells[i], lab = labelOf(th); if (!lab || SKIP_LABELS.has(lab) || th.hasAttribute('data-kcf-skip')) continue; cand.push(i); }
    if (!cand.length) return out;
    const src = sourceFor(table);
    if (src && src.items) {
      if (!src.items.length) return out;
      const read = srcCellReader(src, offsetFor(table, src));
      cand.forEach((i) => { const key = colKeyOf(hr.cells[i]); let empty = true; for (const it of src.items) { if (!ecEmptyVal(read(it, key))) { empty = false; break; } } if (empty) out.add(i); });
      return out;
    }
    const rows = dataRows(table); if (!rows.length) return out;
    cand.forEach((i) => {
      let empty = true;
      for (const r of rows) { const c = r.cells[i]; if (!c) continue; if (c.querySelector('button, a, input[type=checkbox], input[type=radio], select, img, svg') || !ecEmptyVal(norm(getCellValue(r, i)))) { empty = false; break; } }
      if (empty) out.add(i);
    });
    return out;
  }
  function ecPaint(table) {
    const b = table._kecBtn; if (!b) return;
    const on = ecState(table), n = table._kecN || 0;
    b.classList.toggle('on', on);
    b.textContent = on ? '\u229E Hiện cột trống' + (n ? ' (' + n + ')' : '') : '\u229F Ẩn cột trống';
    b.title = on ? (n ? 'Đang ẩn ' + n + ' cột không có dữ liệu — bấm để hiện lại' : 'Chế độ ẩn cột trống đang BẬT (bảng này hiện không có cột nào trống) — bấm để tắt') : 'Ẩn các cột không có dữ liệu ở bảng này';
  }
  function ecApply(table) {
    if (!table || !table.isConnected || !ecAllowed(table)) return;
    table.querySelectorAll('.kec-hide').forEach((c) => c.classList.remove('kec-hide'));
    table.querySelectorAll('[data-kec-span]').forEach((c) => { c.colSpan = +c.dataset.kecSpan; delete c.dataset.kecSpan; });
    const hidden = ecState(table) ? ecColumns(table) : new Set();
    table._kecN = hidden.size;
    if (hidden.size) {
      for (let r = 0; r < table.rows.length; r++) {
        const row = table.rows[r]; let pos = 0;
        for (let c = 0; c < row.cells.length; c++) {
          const cell = row.cells[c], span = cell.colSpan || 1; let cnt = 0;
          for (let k = pos; k < pos + span; k++) if (hidden.has(k)) cnt++;
          if (cnt >= span) cell.classList.add('kec-hide'); else if (cnt) { cell.dataset.kecSpan = span; cell.colSpan = span - cnt; }
          pos += span;
        }
      }
      const cg = table.querySelector(':scope > colgroup');
      if (cg) { let p = 0; Array.from(cg.children).forEach((col) => { const sp = +col.getAttribute('span') || 1; let cnt = 0; for (let k = p; k < p + sp; k++) if (hidden.has(k)) cnt++; if (cnt >= sp) col.classList.add('kec-hide'); p += sp; }); }
    }
    ecPaint(table);
  }
  // Nút đặt CHUNG DÒNG NÚT của bảng (thanh nút / tiêu đề phía trên bảng: Mở / sửa, Xuất Excel, Thu gọn…). Không thấy thanh nút nào → tự tạo 1 dòng mỏng ngay trên bảng.
  function ecIsToolbar(el) {
    if (!el || el.nodeType !== 1 || el.tagName === 'TABLE' || /^(SCRIPT|STYLE|TEMPLATE)$/.test(el.tagName)) return false;
    if (el.querySelector('table')) return false;
    if (!el.querySelector('button, select, input:not([type=hidden]):not([type=checkbox]):not([type=radio])')) return false;
    return (el.textContent || '').length <= 900;
  }
  function ecFindToolbar(table) {
    let node = table;
    for (let up = 0; up < 4 && node && node !== document.body; up++) {
      let sib = node.previousElementSibling, n = 0;
      while (sib && n < 3) { if (ecIsToolbar(sib)) return sib; sib = sib.previousElementSibling; n++; }
      const par = node.parentElement;
      if (!par || par.querySelectorAll('table').length > 1) break; // lên tiếp sẽ sang khu vực của bảng khác → dừng (dùng dòng riêng)
      node = par;
    }
    return null;
  }
  function ecEnsureButton(table) {
    if (!ecAllowed(table) || !table.isConnected) return;
    if (table._kecBtn && table._kecBtn.isConnected) return;
    const b = document.createElement('button'); b.type = 'button'; b.className = 'kec-btn'; b._kecTable = table;
    b.addEventListener('click', (e) => { e.preventDefault(); e.stopPropagation(); ecSave(table, !ecState(table)); ecApply(table); });
    ['mousedown', 'dblclick', 'contextmenu'].forEach((ev) => b.addEventListener(ev, (e) => e.stopPropagation()));
    const bar = ecFindToolbar(table);
    if (bar) {
      bar.querySelectorAll('.kec-btn').forEach((x) => { if (!x._kecTable || !x._kecTable.isConnected) x.remove(); }); // nút của bảng cũ đã bị vẽ lại
      const btns = bar.querySelectorAll('button:not(.kec-btn)'), last = btns[btns.length - 1], before = bar.querySelector('[data-kec-before]');
      if (before) before.insertAdjacentElement('beforebegin', b); // PC 1.99: nút nguy hiểm (Xóa) luôn đứng cuối
      else if (last) last.insertAdjacentElement('afterend', b); else bar.appendChild(b);
    } else {
      const host = table.closest('.table-wrap, .data-wrap, .dc-tw, .dcx-tw') || table;
      const prev = host.previousElementSibling;
      if (prev && prev.classList.contains('kec-bar') && !(prev.firstElementChild && prev.firstElementChild._kecTable && prev.firstElementChild._kecTable.isConnected)) prev.remove();
      const line = document.createElement('div'); line.className = 'kec-bar'; line.appendChild(b);
      host.insertAdjacentElement('beforebegin', line);
    }
    table._kecBtn = b; ecPaint(table);
  }

  function attach(table, force, byCall) {
    if (!table) return false;
    const existing = ownRow(table);
    if (existing && !force) { if (ecAllowed(table)) ecEnsureButton(table); return true; }
    if (existing) existing.remove();
    if (!eligible(table, byCall || table._kcfByCall)) return false;
    if (byCall) table._kcfByCall = true;
    const hr = headRow(table);
    const row = document.createElement('tr');
    row.className = 'col-search-row kcf-row';
    let any = false;
    Array.from(hr.cells).forEach((th) => {
      const td = document.createElement('td');
      const label = labelOf(th);
      row.appendChild(td);
      if (!label) return;
      any = true;
      const cell = document.createElement('div'); cell.className = 'col-search-cell';
      const input = document.createElement('input');
      input.type = 'text'; input.className = 'col-search-input kcf-input'; input.placeholder = 'Tìm ' + label + '...'; input.autocomplete = 'off'; input.spellcheck = false;
      const btn = document.createElement('button');
      btn.type = 'button'; btn.className = 'th-filter-btn kcf-btn'; btn.title = 'Lọc theo ' + label;
      btn.innerHTML = '<svg viewBox="0 0 24 24"><circle cx="11" cy="11" r="7"/><path d="M21 21l-4.3-4.3" stroke-linecap="round"/></svg>';
      cell.appendChild(input); cell.appendChild(btn); td.appendChild(cell);

      const liveIdx = () => colKeyOf(th);
      btn.addEventListener('mousedown', (e) => e.stopPropagation());
      btn.addEventListener('click', (e) => {
        e.stopPropagation(); e.preventDefault();
        closeDropdown();
        const src = sourceFor(table);
        openColSearchPopoverGeneric(btn, liveIdx(), label, () => columnValues(table, liveIdx()), filtersFor(table),
          () => { const t = live(table); syncInputs(t); applyFilters(t); }, { blank: !!(src && src.blank) });
      });
      function applySingle(val) {
        const f = filtersFor(table), i = liveIdx();
        if (val) { const s = new Set([val]); s._kcfText = val; f[i] = s; } else delete f[i];
        applyFilters(live(table));
      }
      function openDropdown() {
        closeDropdown();
        const all = Array.from(new Set(columnValues(table, liveIdx()).filter(Boolean)))
          .sort((a, b) => a.localeCompare(b, 'vi', { numeric: true, sensitivity: 'base' }));
        const q = input.value.trim().toLowerCase();
        const matches = (q ? all.filter((v) => v.toLowerCase().includes(q)) : all).slice(0, 100);
        if (!matches.length) return;
        dd = document.createElement('div');
        dd.className = 'col-search-dropdown kcf-dd';
        dd.innerHTML = matches.map((v) => '<div class="col-search-opt">' + escHtml(v) + '</div>').join('');
        const r = input.getBoundingClientRect();
        Object.assign(dd.style, { position: 'fixed', left: Math.round(r.left) + 'px', top: Math.round(r.bottom + 3) + 'px', right: 'auto',
          width: Math.max(180, Math.round(r.width + 24)) + 'px', zIndex: 2147483000, marginTop: '0' });
        document.body.appendChild(dd);
        const h = dd.offsetHeight;
        if (r.bottom + 3 + h > window.innerHeight - 8 && r.top - h - 3 > 8) dd.style.top = Math.round(r.top - h - 3) + 'px';
        dd.addEventListener('mousedown', (e) => {
          const opt = e.target.closest('.col-search-opt'); if (!opt) return;
          e.preventDefault();
          const v = matches[Array.prototype.indexOf.call(dd.children, opt)];
          input.value = v; typed.cancel(); applySingle(v); closeDropdown();
        });
      }
      input.addEventListener('focus', openDropdown);
      // Chờ ngừng gõ ~150 ms mới dựng gợi ý; xóa hết chữ → tự BỎ lọc cột này.
      const typed = debounce(() => {
        if (!input.isConnected) return;
        if (input.value.trim() === '') { const f = filtersFor(table); if (isSet(f[liveIdx()])) applySingle(''); }
        if (document.activeElement === input) openDropdown();
      }, 150);
      input.addEventListener('input', typed);
      input.addEventListener('keydown', (e) => {
        if (!e.ctrlKey && !e.metaKey && !e.altKey) e.stopPropagation(); // không để phím tắt của bảng (di chuyển ô, xóa dòng…) bắt phím khi đang gõ tìm
        if (e.key === 'Enter') { e.preventDefault(); typed.cancel(); applySingle(input.value.trim()); closeDropdown(); input.blur(); }
        else if (e.key === 'Escape') { closeDropdown(); input.blur(); }
      });
      ['click', 'dblclick', 'contextmenu'].forEach((ev) => td.addEventListener(ev, (e) => e.stopPropagation()));
    });
    if (!any) return false;
    // Tiêu đề đổi (đổi file / thêm bớt cột) → bộ lọc đã nhớ theo vị trí cột không còn đúng → bỏ
    const sig = Array.from(hr.cells).map(labelOf).join('|');
    const fNow = filtersFor(table);
    if (fNow.__kcfSig !== undefined && fNow.__kcfSig !== sig) Object.keys(fNow).forEach((k) => { if (k !== '__kcfSig') delete fNow[k]; });
    try { Object.defineProperty(fNow, '__kcfSig', { value: sig, writable: true, configurable: true, enumerable: false }); } catch (e) { /* bỏ qua */ }
    hr.insertAdjacentElement('afterend', row);
    table._kcf = true;
    table.dataset.searchReady = '1';
    table.classList.add('kcf-table');
    watchSticky(table, hr);
    syncInputs(table);
    if (ecAllowed(table)) { ecEnsureButton(table); ecApply(table); } // PC 1.96: nút ẩn / hiện cột trống (PC 1.97: chung dòng nút của bảng)
    // Bảng bị vẽ lại toàn bộ (cùng data-kcf-key) → áp lại bộ lọc đã nhớ. Bảng kiểu NGUỒN đã tự lọc khi vẽ.
    const s0 = sourceFor(table);
    if (!(s0 && s0.rerender) && activeEntries(filtersFor(table)).length) applyFilters(table);
    return true;
  }

  // Hàng tìm dính ngay dưới tiêu đề khi tiêu đề đang dính (position:sticky)
  const stickyObs = window.ResizeObserver ? new ResizeObserver((ents) => ents.forEach((en) => {
    const t = en.target.closest('table'); if (t) t.style.setProperty('--thead-row-h', en.target.getBoundingClientRect().height + 'px');
  })) : null;
  function watchSticky(table, hr) {
    const th0 = hr.cells[0]; if (!th0) return;
    const pos = getComputedStyle(th0).position, posTr = getComputedStyle(hr).position, posHead = getComputedStyle(table.tHead).position;
    if (posHead === 'sticky') return; // cả thead dính → hàng tìm tự dính theo
    if (pos === 'sticky' || posTr === 'sticky') {
      table.classList.add('kcf-sticky');
      table.style.setProperty('--thead-row-h', hr.getBoundingClientRect().height + 'px');
      if (stickyObs) stickyObs.observe(hr);
    }
  }

  // ---------- theo dõi trang: tự gắn bảng mới, tự lọc lại khi tbody vẽ lại ----------
  function scan(root) {
    const base = root || document;
    const list = autoAllOn ? base.querySelectorAll('table') : base.querySelectorAll('table[data-kcf]');
    list.forEach((t) => { if (!ownRow(t)) attach(t); });
  }
  const STRUCT = { TABLE: 1, THEAD: 1, TBODY: 1, TFOOT: 1 };
  const mo = new MutationObserver((recs) => {
    let scanNeeded = false;
    const touched = new Set(), reheader = new Set();
    for (const r of recs) {
      const tg = r.target; if (!tg || tg.nodeType !== 1) continue;
      if (tg.closest && (tg.closest('.kcf-row') || tg.closest('.kcf-dd') || tg.closest('#colSearchPopover'))) continue;
      let relevant = false;
      r.addedNodes.forEach((n) => {
        if (n.nodeType !== 1) return;
        if (n.classList && (n.classList.contains('kcf-row') || n.classList.contains('kcf-dd'))) return;
        if (n.tagName === 'TABLE' || (n.querySelector && n.querySelector('table'))) scanNeeded = true;
        if (STRUCT[n.tagName] || n.tagName === 'TR') relevant = true;
      });
      if (r.removedNodes.length && STRUCT[tg.tagName]) relevant = true;
      if (relevant || STRUCT[tg.tagName]) { const tb = tg.tagName === 'TABLE' ? tg : tg.closest('table'); if (tb) touched.add(tb); }
      // Hàng tiêu đề bị vẽ lại (theadRow.innerHTML = …) → gắn lại hàng tìm cho khớp cột mới
      if (tg.tagName === 'TR' && tg.parentElement && tg.parentElement.tagName === 'THEAD' && !tg.classList.contains('kcf-row')) {
        const tb = tg.closest('table'); if (tb && tb._kcf) reheader.add(tb);
      }
    }
    if (scanNeeded) scan(document);
    reheader.forEach((tb) => { if (tb.isConnected) attach(tb, true); });
    touched.forEach((tb) => {
      if (!tb.isConnected) return;
      if (tb._kcf) {
        if (!ownRow(tb)) attach(tb);               // thead bị vẽ lại → gắn lại hàng tìm
        else { const s1 = sourceFor(tb); if (!(s1 && s1.rerender) && activeEntries(filtersFor(tb)).length) applyFilters(tb); if (ecAllowed(tb)) { ecEnsureButton(tb); if (tb._kecN || ecState(tb)) ecApply(tb); } } // tbody vẽ lại → lọc lại; PC 1.96: áp lại ẩn cột trống
      } else if (autoAllOn || tb.hasAttribute('data-kcf')) attach(tb);
    });
  });
  function start() {
    if (!document.body) return;
    mo.observe(document.body, { childList: true, subtree: true });
    scan(document);
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();

  // Ô tìm kiểu chuẩn cho bảng TỰ QUẢN (vd lưới Nhập liệu — ô tìm đặt trong <th>): gõ → gợi ý (o.values() = danh sách chữ đã
  // lọc phân tầng), bấm gợi ý / Enter → o.pick(giá trị), xóa trắng → o.clear().
  function bindInput(input, o) {
    function open() {
      closeDropdown();
      const all = Array.from(new Set((o.values() || []).map(norm).filter(Boolean))).sort((a, b) => a.localeCompare(b, 'vi', { numeric: true, sensitivity: 'base' }));
      const q = input.value.trim().toLowerCase();
      const matches = (q ? all.filter((v) => v.toLowerCase().includes(q)) : all).slice(0, 100);
      if (!matches.length) return;
      dd = document.createElement('div');
      dd.className = 'col-search-dropdown kcf-dd';
      dd.innerHTML = matches.map((v) => '<div class="col-search-opt">' + escHtml(v) + '</div>').join('');
      const r = input.getBoundingClientRect();
      Object.assign(dd.style, { position: 'fixed', left: Math.round(r.left) + 'px', top: Math.round(r.bottom + 3) + 'px', right: 'auto',
        width: Math.max(180, Math.round(r.width + 24)) + 'px', zIndex: 2147483000, marginTop: '0' });
      document.body.appendChild(dd);
      const h = dd.offsetHeight;
      if (r.bottom + 3 + h > window.innerHeight - 8 && r.top - h - 3 > 8) dd.style.top = Math.round(r.top - h - 3) + 'px';
      dd.addEventListener('mousedown', (e) => {
        const opt = e.target.closest('.col-search-opt'); if (!opt) return;
        e.preventDefault();
        const v = matches[Array.prototype.indexOf.call(dd.children, opt)];
        input.value = v; typed.cancel(); o.pick(v); closeDropdown();
      });
    }
    const typed = debounce(() => {
      if (!input.isConnected) return;
      if (input.value.trim() === '' && o.clear) o.clear();
      if (document.activeElement === input) open();
    }, 150);
    input.classList.add('kcf-input');
    input.addEventListener('focus', open);
    input.addEventListener('input', typed);
    input.addEventListener('keydown', (e) => {
      e.stopPropagation();
      if (e.key === 'Enter') { e.preventDefault(); typed.cancel(); const v = input.value.trim(); if (v) o.pick(v); else if (o.clear) o.clear(); closeDropdown(); input.blur(); }
      else if (e.key === 'Escape') { closeDropdown(); input.blur(); }
    });
  }

  function autoAll() { autoAllOn = true; if (document.readyState !== 'loading') scan(document); }

  window.KLColFilter = {
    autoAll, attach: (t) => attach(t, true, true), scan, refresh, clear, activeCount,
    filters: filtersFor, useFilters, source, filterItems, paginate, apply: applyFilters,
    values: columnValues, bindInput, setSort, sortItems, exportRows, emptyCols: ecApply
  };
  // Tương thích các đoạn code cũ đang gọi tên cũ
  window.__colSearchGetFilters = filtersFor;          // M04 filterAndPaginate()
  window.__colSearchGetCellValue = getCellValue;      // M04 filterAndPaginate()
  window.__reapplyTableFilter = (t) => attach(t, true); // M05 bảng Gộp
})();
