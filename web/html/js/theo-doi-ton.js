/* ================= TAB "THEO DÕI TỒN" (PC 3.92) =================
   Chỉ áp dụng cho module Thành Phẩm Chính (tpc). Gộp 1 bảng duy nhất 2 nhóm cần theo dõi:
     • GẦN HẾT  : Tồn cuối (gộp Mã hàng + Hợp đồng) > 0 và ≤ ngưỡng "Sắp hết" (dùng CHUNG ô ngưỡng với tab Thành Phẩm Chính)
     • TỒN LÂU  : số tháng liên tiếp còn tồn ≥ ngưỡng tháng (mặc định 2)
   Mỗi dòng có 1 ô "Lý do còn tồn / ghi chú" gõ tay (quên xuất, rớt cont…) — ghi chú gắn theo khoá Mã hàng + Hợp đồng
   và KHÔNG mất khi nạp dữ liệu tháng mới: tháng sau mã còn tồn thì ghi chú cũ hiện lại ngay.
   "Số tháng tồn": mỗi lần nạp Load Data, app chụp 1 ảnh tồn của Thành Phẩm Chính theo THÁNG của ngày số liệu
   (nạp lại cùng tháng thì ghi đè). Đếm số tháng liên tiếp có mặt tới tháng hiện tại; chưa có lịch sử thì ước tính từ
   tháng trong số hợp đồng (VD 26.09.089 → 09/2026), hiện dấu "~".
   Chỉ chạy trên PC. Lưu: sheet "TheoDoiTon" (database Excel qua window.dbAPI), dự phòng localStorage.
   Dùng các biến/hàm toàn cục của ton-kho-an-an.js (state, MODULES, filterModuleBaseRowsCached, getFilterModuleColumns,
   escapeHtml, toast, hasDbAPI, renderAll, saveTpcThreshold, writeWorkbookWithFreezeTopRow). Nạp SAU file đó. */
(function(){
  'use strict';

  const DB_SHEET = 'TheoDoiTon';
  const LS_KEY = 'coldstock:theodoiton_v1';
  const MAX_SNAP_MONTHS = 24;
  const CHUNK = 30000; // ô Excel tối đa 32767 ký tự → cắt JSON thành nhiều dòng
  const DEFAULT_MONTHS = 2;

  // dữ liệu lưu: notes[key] = {ma,hd,ten,size,text,at,confirmedAt,lastQty}; snaps['yyyy-mm'] = [[key, qty], ...]
  const store = { notes: {}, snaps: {}, months: DEFAULT_MONTHS, lastSig: '' };
  const ui = { filter: 'all', q: '' };
  let loaded = false;

  /* ---------- tiện ích ---------- */
  const esc = function (v) { return window.KLUtil.escLite(v); };
  const fmt = n => Number(n).toLocaleString('en-US', { maximumFractionDigits: 2 });
  const keyOf = (ma, hd) => String(ma ?? '').trim() + '|' + String(hd ?? '').trim();
  const ymIdx = ym => { const m = /^(\d{4})-(\d{2})$/.exec(ym || ''); return m ? (+m[1]) * 12 + (+m[2]) - 1 : null; };
  const idxToYm = i => `${Math.floor(i / 12)}-${String(i % 12 + 1).padStart(2, '0')}`;
  const ymLabel = ym => { const m = /^(\d{4})-(\d{2})$/.exec(ym || ''); return m ? `${m[2]}/${m[1]}` : '—'; };
  const nowIso = () => new Date().toISOString();
  const fmtDT = iso => {
    if(!iso) return '';
    const d = new Date(iso); if(isNaN(d)) return '';
    const p = n => String(n).padStart(2, '0');
    return `${p(d.getDate())}/${p(d.getMonth() + 1)}/${d.getFullYear()}`;
  };

  // Tháng của số liệu đang nạp: ngày số liệu dd/mm/yyyy → "yyyy-mm"; thiếu thì lấy tháng nạp file.
  function currentYm(){
    const m = /^(\d{1,2})\/(\d{1,2})\/(\d{4})$/.exec(String(state.dataDate || '').trim());
    if(m) return `${m[3]}-${String(+m[2]).padStart(2, '0')}`;
    const d = state.importedAt ? new Date(state.importedAt) : new Date();
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
  }
  // Số hợp đồng "26.09.089-23/09" → tháng 09/2026 (yy.mm) — chỉ là ước tính khi chưa có lịch sử.
  function contractYm(hd){
    const m = /^\s*(\d{2})\.(\d{2})\./.exec(String(hd || ''));
    if(!m) return null;
    const mo = +m[2]; if(mo < 1 || mo > 12) return null;
    return `20${m[1]}-${m[2]}`;
  }

  /* ---------- lưu / đọc ---------- */
  function payload(){ return { v: 1, notes: store.notes, snaps: store.snaps, months: store.months, lastSig: store.lastSig }; }
  function applyPayload(d){
    if(!d || typeof d !== 'object') return;
    store.notes = (d.notes && typeof d.notes === 'object') ? d.notes : {};
    store.snaps = (d.snaps && typeof d.snaps === 'object') ? d.snaps : {};
    const m = Number(d.months);
    store.months = (Number.isFinite(m) && m >= 1) ? Math.floor(m) : DEFAULT_MONTHS;
    store.lastSig = typeof d.lastSig === 'string' ? d.lastSig : '';
  }
  let saveTimer = null;
  function scheduleSave(){ clearTimeout(saveTimer); saveTimer = setTimeout(persist, 400); }
  async function persist(){
    const json = JSON.stringify(payload());
    if(typeof hasDbAPI === 'function' && hasDbAPI()){
      try{
        const rows = [];
        for(let i = 0; i < json.length; i += CHUNK) rows.push({ data: (i === 0 ? '__JSON__' : '') + json.slice(i, i + CHUNK) });
        const res = await window.dbAPI.writeSheet('ton-kho-an-an', DB_SHEET, rows);
        if(res && res.ok) return true;
        console.error('Lỗi lưu Theo dõi tồn vào database Excel:', res && res.error);
      }catch(e){ console.error('Lỗi lưu Theo dõi tồn vào database Excel:', e); }
    }
    try{ localStorage.setItem(LS_KEY, json); }catch(e){ console.error('Lỗi lưu Theo dõi tồn (dự phòng localStorage):', e); }
    return false;
  }
  async function restore(){
    if(loaded) return;
    loaded = true;
    if(typeof hasDbAPI === 'function' && hasDbAPI()){
      try{
        const res = await window.dbAPI.readSheet('ton-kho-an-an', DB_SHEET);
        if(res && res.ok && Array.isArray(res.data) && res.data.length){
          const s = res.data.map(r => String((r && r.data) ?? '')).join('');
          if(s.startsWith('__JSON__')){ applyPayload(JSON.parse(s.slice(8))); return; }
        }
        if(res && res.ok) return;
      }catch(e){ /* thử dự phòng */ }
    }
    try{ const raw = localStorage.getItem(LS_KEY); if(raw) applyPayload(JSON.parse(raw)); }catch(e){ /* chưa có */ }
  }

  /* ---------- dữ liệu hiện tại của Thành Phẩm Chính (gộp Mã hàng + Hợp đồng) ---------- */
  function buildGroups(){
    if(!(state.rows && state.rows.length && state.headers && state.headers.length)) return null;
    const cols = getFilterModuleColumns('tpc');
    const ci = lbl => cols.find(c => c.label === lbl)?.ci;
    const cMa = ci('Mã hàng'), cHd = ci('Hợp đồng'), cTon = ci('Tồn cuối');
    if(cMa === undefined || cHd === undefined || cTon === undefined) return null;
    const cTen = ci('Tên hàng'), cSize = ci('Size'), cDvt = ci('ĐVT'), cDt = ci('Đặc tính');
    const map = new Map();
    filterModuleBaseRowsCached('tpc').forEach(({ row }) => {
      const ma = String(row[cMa] ?? '').trim();
      if(!ma) return;
      const hd = String(row[cHd] ?? '').trim();
      const k = ma + '|' + hd;
      let g = map.get(k);
      if(!g){
        g = { key: k, ma, hd, ten: '', size: '', dvt: '', dts: new Set(), qty: 0 };
        map.set(k, g);
      }
      g.qty += Number(row[cTon]) || 0;
      if(!g.ten && cTen !== undefined) g.ten = String(row[cTen] ?? '').trim();
      if(!g.size && cSize !== undefined) g.size = String(row[cSize] ?? '').trim();
      if(!g.dvt && cDvt !== undefined) g.dvt = String(row[cDvt] ?? '').trim();
      if(cDt !== undefined){ const d = String(row[cDt] ?? '').trim(); if(d) g.dts.add(d); }
    });
    return map;
  }

  /* ---------- ảnh chụp tồn theo tháng ---------- */
  function ensureSnapshot(groups){
    groups = groups || buildGroups();
    if(!groups) return false;
    let tot = 0; groups.forEach(g => { tot += g.qty; });
    const sig = [state.fileName || '', state.importedAt || '', state.dataDate || '', groups.size, Math.round(tot * 100)].join('|');
    if(sig === store.lastSig) return false;
    const ym = currentYm();
    const arr = [];
    groups.forEach(g => { if(g.qty > 0) arr.push([g.key, Math.round(g.qty * 1000) / 1000]); });
    store.snaps[ym] = arr;
    const yms = Object.keys(store.snaps).sort();
    while(yms.length > MAX_SNAP_MONTHS) delete store.snaps[yms.shift()];
    store.lastSig = sig;
    scheduleSave();
    return true;
  }

  // Tính số tháng liên tiếp còn tồn của 1 khoá, tính tới tháng hiện tại.
  function ageOf(key, curYm, snapSets){
    const cur = ymIdx(curYm);
    const yms = Object.keys(snapSets).filter(y => ymIdx(y) <= cur).sort();
    let start = null;
    // đi lùi từ tháng hiện tại: dừng ở ảnh chụp gần nhất KHÔNG có khoá
    for(let i = yms.length - 1; i >= 0; i--){
      if(snapSets[yms[i]].has(key)) start = ymIdx(yms[i]); else break;
    }
    const earliest = yms.length ? ymIdx(yms[0]) : cur;
    // run chạm tới ảnh chụp sớm nhất (hoặc chưa có ảnh nào) → chưa biết đã tồn từ trước → thử ước tính theo hợp đồng
    let estimated = false;
    if(start === null) start = cur;
    if(start === earliest){
      const hdIdx = ymIdx(contractYm(key.split('|')[1]));
      if(hdIdx !== null && hdIdx < start && hdIdx <= cur){ start = hdIdx; estimated = true; }
    }
    return { months: cur - start + (estimated ? 0 : 1), startYm: idxToYm(start), estimated };
  }

  /* ---------- danh sách hiển thị ---------- */
  function computeList(){
    const groups = buildGroups();
    if(!groups) return null;
    ensureSnapshot(groups);
    const curYm = currentYm();
    const snapSets = {};
    Object.keys(store.snaps).forEach(y => { snapSets[y] = new Set(store.snaps[y].map(a => a[0])); });
    // ảnh chụp tháng hiện tại phải phản ánh dữ liệu đang nạp
    snapSets[curYm] = new Set([...groups.values()].filter(g => g.qty > 0).map(g => g.key));

    const low = Number(state.tpcLowStockThreshold);
    const th = Number.isFinite(low) && low >= 0 ? low : 8;
    const items = [];
    groups.forEach(g => {
      if(g.qty <= 0) return;
      const isLow = g.qty <= th;
      const age = ageOf(g.key, curYm, snapSets);
      const isOld = age.months >= store.months;
      if(!isLow && !isOld) return;
      const note = store.notes[g.key] || null;
      items.push({ g, isLow, isOld, age, note, dt: [...g.dts].join(' / ') });
    });
    // đã hết: có ghi chú nhưng không còn tồn
    const done = [];
    Object.keys(store.notes).forEach(k => {
      const gr = groups.get(k);
      if(!gr || gr.qty <= 0) done.push({ key: k, note: store.notes[k] });
    });
    items.sort((a, b) => (b.age.months - a.age.months) || a.g.ma.localeCompare(b.g.ma) || a.g.hd.localeCompare(b.g.hd));
    return { items, done, th, curYm };
  }

  function matchFilter(it){
    switch(ui.filter){
      case 'low': return it.isLow;
      case 'old': return it.isOld;
      case 'both': return it.isLow && it.isOld;
      case 'nonote': return !(it.note && it.note.text);
      case 'note': return !!(it.note && it.note.text);
      default: return true;
    }
  }
  function norm(s){
    const f = (typeof fuzzyNormalize === 'function') ? fuzzyNormalize : (x => String(x ?? '').toLowerCase());
    return f(s);
  }

  function counts(L){
    const c = { all: L.items.length, low: 0, old: 0, both: 0, nonote: 0, note: 0, done: L.done.length };
    L.items.forEach(it => {
      if(it.isLow) c.low++;
      if(it.isOld) c.old++;
      if(it.isLow && it.isOld) c.both++;
      if(it.note && it.note.text) c.note++; else c.nonote++;
    });
    return c;
  }

  const CHIPS = [
    ['all', 'Tất cả'], ['low', 'Gần hết'], ['old', 'Tồn lâu'], ['both', 'Cả hai'],
    ['nonote', 'Chưa ghi chú'], ['note', 'Đã ghi chú'], ['done', 'Đã hết (ghi chú cũ)']
  ];

  function chipsHtml(c){
    return CHIPS.map(([id, lbl]) =>
      `<button type="button" class="tdt-chip ${ui.filter === id ? 'active' : ''}" data-tdt-filter="${id}">${lbl} <span class="n" data-tdt-n="${id}">${c[id]}</span></button>`
    ).join('');
  }

  function badgesHtml(it){
    return (it.isLow ? '<span class="tdt-badge low">Gần hết</span>' : '')
      + (it.isOld ? '<span class="tdt-badge old">Tồn lâu</span>' : '');
  }

  function rowHtml(it, i){
    const n = it.note || {};
    const tip = it.age.estimated ? 'Ước tính theo tháng trong số hợp đồng (chưa có lịch sử nạp đủ)' : 'Số tháng liên tiếp còn tồn theo các lần nạp dữ liệu';
    return `<tr data-tdt-key="${esc(it.g.key)}">
      <td class="c">${i + 1}</td>
      <td class="mono">${esc(it.g.ma)}</td>
      <td>${esc(it.g.ten)}</td>
      <td class="c">${esc(it.g.size)}</td>
      <td class="mono">${esc(it.g.hd)}</td>
      <td class="c">${esc(it.g.dvt)}</td>
      <td class="num">${fmt(it.g.qty)}</td>
      <td class="c">${badgesHtml(it)}</td>
      <td class="c" title="${esc(tip)}"><b>${it.age.estimated ? '~' : ''}${it.age.months}</b> th · từ ${ymLabel(it.age.startYm)}</td>
      <td class="tdt-note-cell"><textarea class="tdt-note" rows="1" placeholder="Gõ lý do còn tồn… (VD: quên xuất / rớt cont …)" data-tdt-note="${esc(it.g.key)}">${esc(n.text || '')}</textarea></td>
      <td class="c tdt-when" data-tdt-when="${esc(it.g.key)}">${esc(whenText(n))}</td>
      <td class="c"><button type="button" class="tdt-ok" data-tdt-ok="${esc(it.g.key)}" title="Xác nhận lý do vẫn đúng trong tháng này" ${n.text ? '' : 'disabled'}>✓ Vẫn đúng</button></td>
    </tr>`;
  }
  function whenText(n){
    if(!n || !n.text) return '';
    const a = fmtDT(n.confirmedAt || n.at);
    return (n.confirmedAt ? 'Xác nhận ' : 'Ghi ') + a;
  }

  function doneRowsHtml(L){
    if(!L.done.length) return `<tr><td colspan="12" class="tdt-empty">Chưa có mã nào đã hết hàng sau khi ghi chú.</td></tr>`;
    return L.done.map(({ key, note: n }, i) => `<tr class="tdt-done">
      <td class="c">${i + 1}</td>
      <td class="mono">${esc(n.ma || key.split('|')[0])}</td>
      <td>${esc(n.ten || '')}</td>
      <td class="c">${esc(n.size || '')}</td>
      <td class="mono">${esc(n.hd || key.split('|')[1])}</td>
      <td class="c"></td>
      <td class="num">0</td>
      <td class="c"><span class="tdt-badge ok">Đã hết</span></td>
      <td class="c">—</td>
      <td>${esc(n.text || '')}</td>
      <td class="c">${esc(whenText(n))}</td>
      <td class="c"><button type="button" class="tdt-ok" data-tdt-del="${esc(key)}" title="Xoá ghi chú cũ này">Xoá</button></td>
    </tr>`).join('');
  }

  function visibleItems(L){
    const q = norm(ui.q).trim();
    return L.items.filter(it => {
      if(!matchFilter(it)) return false;
      if(!q) return true;
      return norm([it.g.ma, it.g.ten, it.g.size, it.g.hd, it.dt, it.note && it.note.text].join(' ')).includes(q);
    });
  }

  /* ---------- vẽ tab ---------- */
  let lastList = null;
  window.renderTheoDoiTon = async function renderTheoDoiTon(){
    await restore();
    const c = document.getElementById('content');
    if(!c) return;
    const L = computeList();
    lastList = L;
    document.getElementById('pageSub').textContent = L
      ? `Thành Phẩm Chính — gần hết & tồn lâu · nạp từ ${state.fileName || 'dữ liệu đã lưu'}` + (state.dataDate ? ` · Số liệu ngày ${state.dataDate}` : '')
      : 'Chưa có dữ liệu — vào mục Load Data để nạp file tồn kho';
    const stats = document.getElementById('topbarStats'); if(stats) stats.innerHTML = '';
    if(!L){
      c.innerHTML = `<div class="placeholder"><div class="ph-ic">${icon('search', 'ic')}</div><h2>Chưa có dữ liệu tồn kho</h2>
        <p>Vào mục <b style="color:var(--frost)">Load Data</b> để nạp file Excel tồn kho trước.</p>
        <div class="tag" id="tdtGoLoad" style="cursor:pointer;">ĐI TỚI LOAD DATA</div></div>`;
      const b = document.getElementById('tdtGoLoad');
      if(b) b.addEventListener('click', () => { state.activeModule = 'loaddata'; renderAll(); });
      return;
    }
    const cnt = counts(L);
    const showDone = ui.filter === 'done';
    const list = showDone ? [] : visibleItems(L);
    const snapMonths = Object.keys(store.snaps).length;
    c.innerHTML = `<div class="tdt-wrap">
      <div class="tdt-bar">
        <div class="tdt-chips" id="tdtChips">${chipsHtml(cnt)}</div>
        <div class="tdt-tools">
          <input type="search" id="tdtSearch" class="tdt-search" placeholder="Tìm mã hàng, tên, hợp đồng, ghi chú…" value="${esc(ui.q)}">
          <span class="tdt-th" title="Dùng chung với ô Ngưỡng Sắp hết ở tab Thành Phẩm Chính">Gần hết ≤ <input type="number" id="tdtLowTh" min="0" value="${L.th}"></span>
          <span class="tdt-th" title="Số tháng liên tiếp còn tồn để coi là Tồn lâu">Tồn lâu ≥ <input type="number" id="tdtMonths" min="1" value="${store.months}"> tháng</span>
          <button type="button" class="tdt-xl" id="tdtExport">Xuất Excel</button>
        </div>
      </div>
      <div class="tdt-hint">Lịch sử đang có: <b>${snapMonths}</b> tháng nạp dữ liệu${snapMonths < 2 ? ' — dấu <b>~</b> là ước tính theo tháng của số hợp đồng, sẽ chính xác dần sau mỗi tháng nạp.' : '.'} Ghi chú gắn theo <b>Mã hàng + Hợp đồng</b>, giữ nguyên khi nạp tháng mới.</div>
      <div class="table-shell"><div class="table-scroll tdt-scroll"><table class="tdt-table">
        <thead><tr><th>#</th><th>Mã hàng</th><th>Tên hàng</th><th>Size</th><th>Hợp đồng</th><th>ĐVT</th><th class="num">Tồn cuối</th><th>Loại</th><th>Số tháng tồn</th><th>Lý do còn tồn / ghi chú</th><th>Ngày ghi</th><th></th></tr></thead>
        <tbody id="tdtBody">${showDone ? doneRowsHtml(L) : (list.length ? list.map(rowHtml).join('') : `<tr><td colspan="12" class="tdt-empty">Không có mã nào khớp bộ lọc.</td></tr>`)}</tbody>
      </table></div></div>
    </div>`;
    bind(c);
    c.querySelectorAll('.tdt-note').forEach(autoGrow);
  };

  function autoGrow(t){ t.style.height = 'auto'; t.style.height = Math.min(120, t.scrollHeight) + 'px'; }

  function refreshChipCounts(){
    if(!lastList) return;
    const cnt = counts(lastList);
    Object.keys(cnt).forEach(k => {
      const el = document.querySelector(`[data-tdt-n="${k}"]`);
      if(el) el.textContent = cnt[k];
    });
  }

  function findItem(key){ return lastList && lastList.items.find(it => it.g.key === key); }

  function saveNote(key, text){
    text = String(text || '').trim();
    const it = findItem(key);
    const old = store.notes[key];
    if(!text){
      if(old){ delete store.notes[key]; if(it) it.note = null; scheduleSave(); }
    } else if(!old || old.text !== text){
      const g = it ? it.g : null;
      const n = Object.assign({}, old || {}, {
        ma: g ? g.ma : key.split('|')[0], hd: g ? g.hd : key.split('|')[1],
        ten: g ? g.ten : (old && old.ten) || '', size: g ? g.size : (old && old.size) || '',
        text, at: nowIso(), confirmedAt: null, lastQty: g ? g.qty : (old && old.lastQty) || 0
      });
      store.notes[key] = n;
      if(it) it.note = n;
      scheduleSave();
    } else return;
    const w = document.querySelector(`[data-tdt-when="${CSS.escape(key)}"]`);
    if(w) w.textContent = whenText(store.notes[key]);
    const ok = document.querySelector(`[data-tdt-ok="${CSS.escape(key)}"]`);
    if(ok) ok.disabled = !store.notes[key];
    refreshChipCounts();
  }

  function bind(c){
    c.querySelectorAll('[data-tdt-filter]').forEach(b => b.addEventListener('click', () => { ui.filter = b.dataset.tdtFilter; window.renderTheoDoiTon(); }));
    const s = c.querySelector('#tdtSearch');
    if(s){
      let t = null;
      s.addEventListener('input', () => { clearTimeout(t); t = setTimeout(() => { ui.q = s.value; const pos = s.selectionStart; window.renderTheoDoiTon().then(() => { const n = document.getElementById('tdtSearch'); if(n){ n.focus(); try{ n.setSelectionRange(pos, pos); }catch(e){} } }); }, 250); });
    }
    const lowTh = c.querySelector('#tdtLowTh');
    if(lowTh) lowTh.addEventListener('change', () => {
      const n = Number(lowTh.value);
      state.tpcLowStockThreshold = (Number.isFinite(n) && n >= 0) ? n : state.tpcLowStockThreshold;
      if(typeof saveTpcThreshold === 'function') saveTpcThreshold(state.tpcLowStockThreshold);
      renderAll();
    });
    const mo = c.querySelector('#tdtMonths');
    if(mo) mo.addEventListener('change', () => {
      const n = Math.floor(Number(mo.value));
      store.months = (Number.isFinite(n) && n >= 1) ? n : DEFAULT_MONTHS;
      scheduleSave(); window.renderTheoDoiTon();
    });
    const ex = c.querySelector('#tdtExport');
    if(ex) ex.addEventListener('click', exportExcel);
    const body = c.querySelector('#tdtBody');
    if(body){
      body.addEventListener('change', e => { const t = e.target.closest('.tdt-note'); if(t) saveNote(t.dataset.tdtNote, t.value); });
      body.addEventListener('input', e => { const t = e.target.closest('.tdt-note'); if(t) autoGrow(t); });
      body.addEventListener('keydown', e => {
        const t = e.target.closest('.tdt-note');
        if(t && e.key === 'Enter' && !e.shiftKey){ e.preventDefault(); t.blur(); }
      });
      body.addEventListener('click', e => {
        const ok = e.target.closest('[data-tdt-ok]');
        if(ok && !ok.disabled){
          const n = store.notes[ok.dataset.tdtOk];
          if(n){
            n.confirmedAt = nowIso(); scheduleSave();
            const w = body.querySelector(`[data-tdt-when="${CSS.escape(ok.dataset.tdtOk)}"]`);
            if(w) w.textContent = whenText(n);
            if(typeof toast === 'function') toast('Đã xác nhận lý do vẫn đúng');
          }
          return;
        }
        const del = e.target.closest('[data-tdt-del]');
        if(del){
          if(confirm('Xoá ghi chú cũ của mã này?')){
            delete store.notes[del.dataset.tdtDel]; scheduleSave(); window.renderTheoDoiTon();
          }
        }
      });
    }
  }

  /* ---------- xuất Excel ---------- */
  async function exportExcel(){
    if(!lastList){ return; }
    if(window.klNapXlsx) await window.klNapXlsx(); // PC 1.85
    if(typeof XLSX === 'undefined'){ alert('Chưa nạp được thư viện Excel.'); return; }
    const head = ['STT', 'Mã hàng', 'Tên hàng', 'Size', 'Đặc tính', 'Hợp đồng', 'ĐVT', 'Tồn cuối', 'Loại', 'Số tháng tồn', 'Từ tháng', 'Lý do còn tồn / ghi chú', 'Ngày ghi'];
    const items = ui.filter === 'done' ? [] : visibleItems(lastList);
    const aoa = [head].concat(items.map((it, i) => [
      i + 1, it.g.ma, it.g.ten, it.g.size, it.dt, it.g.hd, it.g.dvt, it.g.qty,
      [it.isLow ? 'Gần hết' : '', it.isOld ? 'Tồn lâu' : ''].filter(Boolean).join(' + '),
      (it.age.estimated ? '~' : '') + it.age.months, ymLabel(it.age.startYm),
      (it.note && it.note.text) || '', whenText(it.note)
    ]));
    const ws = XLSX.utils.aoa_to_sheet(aoa);
    ws['!cols'] = [6, 26, 48, 9, 14, 18, 7, 11, 18, 12, 10, 46, 20].map(w => ({ wch: w }));
    const doneAoa = [['STT', 'Mã hàng', 'Tên hàng', 'Size', 'Hợp đồng', 'Lý do (ghi chú cũ)', 'Ngày ghi']].concat(
      lastList.done.map(({ key, note: n }, i) => [i + 1, n.ma || key.split('|')[0], n.ten || '', n.size || '', n.hd || key.split('|')[1], n.text || '', whenText(n)]));
    const ws2 = XLSX.utils.aoa_to_sheet(doneAoa);
    ws2['!cols'] = [6, 26, 48, 9, 18, 46, 20].map(w => ({ wch: w }));
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, 'Theo dõi tồn');
    XLSX.utils.book_append_sheet(wb, ws2, 'Đã hết');
    const d = String(state.dataDate || '').replace(/\//g, '-') || 'ngay';
    const fn = `TheoDoiTon_TPC_${d}.xlsx`;
    try{
      if(typeof writeWorkbookWithFreezeTopRow === 'function') await writeWorkbookWithFreezeTopRow(wb, fn);
      else XLSX.writeFile(wb, fn);
      if(typeof toast === 'function') toast('Đã xuất ' + fn);
    }catch(e){ console.error(e); alert('Không xuất được Excel: ' + e.message); }
  }

  /* ---------- móc vào app chính ---------- */
  window.tdtRestore = restore;
  // gọi cuối renderAll(): chụp ảnh tồn tháng ngay khi nạp dữ liệu mới, không cần mở tab này
  let afterTimer = null;
  window.tdtAfterRender = function(){
    if(state.activeModule === 'theodoi') return; // tab này tự chụp khi vẽ
    clearTimeout(afterTimer);
    afterTimer = setTimeout(async () => { try{ await restore(); ensureSnapshot(); }catch(e){ console.error(e); } }, 300);
  };
})();
