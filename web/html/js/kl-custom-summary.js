/* PC 5.33 — "Bảng Tổng Hợp Tùy Chọn": bảng RIÊNG, chỉ lọc lại từ bảng tổng hợp có sẵn
   (Size × nhóm Đặc Tính đã gộp). KHÔNG đọc dữ liệu tồn kho chi tiết, KHÔNG ghi ngược, KHÔNG đụng
   bộ lọc / trạng thái của bảng gốc. Dùng chung cho Tồn kho An An (Bảng Tổng Hợp HLSO) và
   Tồn kho gửi (Bảng Tổng Hợp).

   window.KLCustomSummary.mount(el, { key, title, getData, onExport? })
     getData() → { cols:[tên cột đặc tính], rows:[{ key, label, kind:'title'|'size', cells:[số|null…], total:số|null }] }
     - 'title' = dòng tiêu đề nhóm (VD "TÔM GIA CÔNG"): hiện in đậm, KHÔNG cộng vào dòng Tổng chọn (giống bảng gốc).
     - key phải ổn định giữa các lần vẽ (dùng để nhớ lựa chọn). */
(function(){
  'use strict';
  if(window.KLCustomSummary) return;

  const LS_PREFIX = 'klcs_v1_';
  const OPEN = {}; // nhớ trạng thái mở / đóng theo key trong phiên

  function esc(s){ return String(s==null?'':s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;'); }
  function isNum(v){ return typeof v === 'number' && isFinite(v); }
  function fmt(v){ return isNum(v) ? v.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2}) : null; }
  function stripAcc(s){ return String(s||'').normalize('NFD').replace(/[\u0300-\u036f]/g,'').replace(/đ/g,'d').replace(/Đ/g,'D').toLowerCase(); }

  function loadStore(key){
    try{
      const o = JSON.parse(localStorage.getItem(LS_PREFIX + key) || 'null');
      if(o && typeof o === 'object') return { sizes:o.sizes||null, cols:o.cols||null, hideEmpty:o.hideEmpty!==false, views:o.views||{} };
    }catch(e){}
    return { sizes:null, cols:null, hideEmpty:true, views:{} };
  }
  function saveStore(key, st){
    try{ localStorage.setItem(LS_PREFIX + key, JSON.stringify(st)); }catch(e){ console.error('Lỗi lưu Bảng Tổng Hợp Tùy Chọn:', e); }
  }

  function injectCss(){
    if(document.getElementById('klcs-css')) return;
    const s = document.createElement('style'); s.id = 'klcs-css';
    s.textContent = `
.klcs{margin:14px 0;border:1px solid var(--line,rgba(128,128,128,.35));border-radius:10px;background:var(--surface,transparent);}
.klcs>summary{cursor:pointer;padding:9px 14px;font-weight:600;font-size:14px;list-style:none;display:flex;align-items:center;gap:8px;}
.klcs>summary::-webkit-details-marker{display:none;}
.klcs>summary::before{content:'▸';display:inline-block;transition:transform .15s;}
.klcs[open]>summary::before{transform:rotate(90deg);}
.klcs-sub{font-weight:400;font-size:12px;opacity:.7;}
.klcs-body{padding:0 14px 14px;}
.klcs-bar{display:flex;flex-wrap:wrap;gap:8px;align-items:center;margin:4px 0 10px;}
.klcs-btn{font:inherit;font-size:12.5px;padding:5px 10px;border-radius:7px;cursor:pointer;color:inherit;background:transparent;border:1px solid var(--line,rgba(128,128,128,.45));}
.klcs-btn:hover{background:rgba(128,128,128,.14);}
.klcs-btn.on{border-color:#3B82F6;color:#3B82F6;}
.klcs-pick{position:relative;}
.klcs-pick>summary{list-style:none;cursor:pointer;}
.klcs-pick>summary::-webkit-details-marker{display:none;}
.klcs-pop{position:absolute;z-index:40;top:100%;left:0;margin-top:4px;min-width:250px;max-width:360px;max-height:340px;display:flex;flex-direction:column;
  background:var(--surface-2,var(--kl-surface-2,#fff));color:var(--text,inherit);border:1px solid var(--line,rgba(128,128,128,.5));border-radius:9px;box-shadow:0 8px 24px rgba(0,0,0,.28);padding:8px;}
.klcs-pop input[type=search]{font:inherit;font-size:12.5px;padding:5px 8px;margin-bottom:6px;border-radius:6px;border:1px solid var(--line,rgba(128,128,128,.45));background:transparent;color:inherit;}
.klcs-pop .klcs-act{display:flex;gap:6px;margin-bottom:6px;}
.klcs-list{overflow:auto;flex:1;}
.klcs-it{display:flex;align-items:center;gap:7px;padding:3px 4px;font-size:12.5px;cursor:pointer;border-radius:5px;}
.klcs-it:hover{background:rgba(128,128,128,.14);}
.klcs-it.ttl{font-weight:700;margin-top:4px;}
.klcs-it.sub{padding-left:18px;}
.klcs-view{font:inherit;font-size:12.5px;padding:5px 8px;border-radius:7px;border:1px solid var(--line,rgba(128,128,128,.45));background:transparent;color:inherit;max-width:190px;}
.klcs-view option{color:#000;}
.klcs-wrap{overflow:auto;max-height:520px;border:1px solid var(--line,rgba(128,128,128,.35));border-radius:8px;}
.klcs-tbl{border-collapse:collapse;width:100%;font-size:13px;}
.klcs-tbl th,.klcs-tbl td{padding:5px 10px;border-bottom:1px solid var(--line,rgba(128,128,128,.25));white-space:nowrap;text-align:right;}
.klcs-tbl th:first-child,.klcs-tbl td:first-child{text-align:left;position:sticky;left:0;background:var(--surface-2,var(--kl-surface-2,#fff));}
.klcs-tbl thead th{position:sticky;top:0;z-index:2;background:var(--surface-2,var(--kl-surface-2,#fff));font-weight:600;}
.klcs-tbl thead th:first-child{z-index:3;}
.klcs-tbl tr.ttl td{font-weight:700;background:rgba(59,130,246,.10);}
.klcs-tbl tr.ttl td:first-child{background:rgba(59,130,246,.16);}
.klcs-tbl tfoot td{font-weight:700;position:sticky;bottom:0;background:var(--surface-2,var(--kl-surface-2,#fff));border-top:2px solid var(--line,rgba(128,128,128,.5));}
.klcs-tbl td.tot{font-weight:700;}
.klcs-e{opacity:.4;}
.klcs-note{font-size:12px;opacity:.75;margin-top:8px;}
.klcs-warn{color:#D97706;opacity:1;}
.klcs-empty{padding:18px;text-align:center;opacity:.7;font-size:13px;}`;
    document.head.appendChild(s);
  }

  function mount(el, opt){
    if(!el || !opt || typeof opt.getData !== 'function') return;
    injectCss();
    const key = opt.key || 'default';
    const title = opt.title || 'Bảng Tổng Hợp Tùy Chọn';
    const st = loadStore(key);

    let data;
    try{ data = opt.getData() || { cols:[], rows:[] }; }catch(e){ console.error('Bảng Tổng Hợp Tùy Chọn — lỗi lấy dữ liệu:', e); data = { cols:[], rows:[] }; }
    const cols = data.cols || [], rows = data.rows || [];

    // lựa chọn: null = tất cả; chỉ giữ những khóa còn tồn tại trong dữ liệu hiện tại
    const rowKeys = rows.map(r=>r.key);
    const selRows = new Set(st.sizes ? st.sizes.filter(k=>rowKeys.includes(k)) : rowKeys);
    const selCols = new Set(st.cols ? st.cols.filter(c=>cols.includes(c)) : cols);

    function persist(){
      st.sizes = selRows.size === rowKeys.length ? null : Array.from(selRows);
      st.cols = selCols.size === cols.length ? null : Array.from(selCols);
      saveStore(key, st);
    }

    function compute(){
      const shownRows = rows.filter(r=>selRows.has(r.key));
      let colIdx = cols.map((c,i)=>i).filter(i=>selCols.has(cols[i]));
      if(st.hideEmpty){
        const has = colIdx.filter(i=>shownRows.some(r=>isNum(r.cells[i]) && Math.abs(r.cells[i])>0));
        if(has.length) colIdx = has;
      }
      const sums = colIdx.map(()=>0), hasSum = colIdx.map(()=>false);
      let grand = 0, grandHas = false;
      const body = shownRows.map(r=>{
        let rt = 0, rh = false;
        colIdx.forEach((ci,k)=>{
          const v = r.cells[ci];
          if(isNum(v)){ rt += v; rh = true; if(r.kind !== 'title'){ sums[k]+=v; hasSum[k]=true; } }
        });
        if(rh && r.kind !== 'title'){ grand += rt; grandHas = true; }
        return { r, rt: rh ? rt : null };
      });
      // chênh lệch so với cột "Tổng" của bảng gốc khi chọn đủ mọi cột (hàng chưa gán nhóm đặc tính)
      let diff = 0;
      if(selCols.size === cols.length){
        shownRows.forEach(r=>{
          if(r.kind==='title' || !isNum(r.total)) return;
          const s = r.cells.reduce((a,v)=>a+(isNum(v)?v:0),0);
          diff += (r.total - s);
        });
      }
      return { shownRows, colIdx, sums, hasSum, grand, grandHas, body, diff };
    }

    function tableHtml(c){
      if(!c.shownRows.length) return '<div class="klcs-empty">Chưa chọn Size nào — bấm "Chọn Size" để chọn.</div>';
      if(!c.colIdx.length) return '<div class="klcs-empty">Chưa chọn Đặc Tính nào — bấm "Chọn Đặc Tính" để chọn.</div>';
      const cell = v => { const f = fmt(v); return f===null ? '<span class="klcs-e">-</span>' : f; };
      let h = '<div class="klcs-wrap"><table class="klcs-tbl"><thead><tr><th>Size</th>'
        + c.colIdx.map(i=>`<th>${esc(cols[i])}</th>`).join('') + '<th>Tổng chọn</th></tr></thead><tbody>';
      c.body.forEach(b=>{
        h += `<tr class="${b.r.kind==='title'?'ttl':''}"><td>${esc(b.r.label)}</td>`
          + c.colIdx.map(i=>`<td>${cell(b.r.cells[i])}</td>`).join('')
          + `<td class="tot">${cell(b.rt)}</td></tr>`;
      });
      h += '</tbody><tfoot><tr><td>TỔNG CHỌN</td>'
        + c.colIdx.map((i,k)=>`<td>${c.hasSum[k]?fmt(c.sums[k]):'<span class="klcs-e">-</span>'}</td>`).join('')
        + `<td>${c.grandHas?fmt(c.grand):'<span class="klcs-e">-</span>'}</td></tr></tfoot></table></div>`;
      return h;
    }

    function noteHtml(c){
      let n = 'Số liệu lấy thẳng từ bảng tổng hợp có sẵn; "Tổng chọn" = cộng các cột đang chọn. Dòng tiêu đề nhóm (nền xanh) không cộng vào TỔNG CHỌN.';
      if(Math.abs(c.diff) > 0.005) n += ` <span class="klcs-warn">Lưu ý: cột "Tổng" của bảng gốc lớn hơn tổng các cột đặc tính ${fmt(c.diff)} kg (hàng chưa gán nhóm đặc tính).</span>`;
      return `<div class="klcs-note">${n}</div>`;
    }

    function pickHtml(id, label, items, selected){
      // items: [{k, label, cls}]
      const cnt = items.filter(i=>selected.has(i.k)).length;
      return `<details class="klcs-pick" data-pick="${id}"><summary class="klcs-btn">${label} (${cnt}/${items.length}) ▾</summary>
        <div class="klcs-pop"><input type="search" placeholder="Tìm…" data-q="${id}">
          <div class="klcs-act"><button type="button" class="klcs-btn" data-all="${id}">Chọn tất cả</button><button type="button" class="klcs-btn" data-none="${id}">Bỏ hết</button></div>
          <div class="klcs-list" data-list="${id}">${items.map((it,ix)=>`<label class="klcs-it ${it.cls||''}" data-s="${esc(stripAcc(it.label))}"><input type="checkbox" data-ck="${id}" data-i="${ix}" ${selected.has(it.k)?'checked':''}> ${esc(it.label)}</label>`).join('')}</div>
        </div></details>`;
    }

    // danh sách Size: dòng tiêu đề nhóm đứng đầu, các size thụt vào bên dưới; tick tiêu đề = tick cả nhóm
    const rowItems = rows.map(r=>({ k:r.key, label:r.label, cls: r.kind==='title' ? 'ttl' : 'sub' }));
    const colItems = cols.map(c=>({ k:c, label:c, cls:'' }));

    function render(){
      const wasOpenPick = {};
      el.querySelectorAll('.klcs-pick[open]').forEach(d=>{ wasOpenPick[d.dataset.pick] = true; });
      const c = compute();
      const viewNames = Object.keys(st.views);
      el.innerHTML = `<details class="klcs" ${OPEN[key]?'open':''}>
        <summary>${esc(title)} <span class="klcs-sub">· lọc Size và Đặc Tính đã gộp từ bảng có sẵn</span></summary>
        <div class="klcs-body">
          <div class="klcs-bar">
            ${pickHtml('rows','Chọn Size',rowItems,selRows)}
            ${pickHtml('cols','Chọn Đặc Tính',colItems,selCols)}
            <button type="button" class="klcs-btn ${st.hideEmpty?'on':''}" data-act="hideEmpty">${st.hideEmpty?'Đang ẩn cột trống':'Hiện cả cột trống'}</button>
            <select class="klcs-view" data-act="viewSel"><option value="">— Mẫu đã lưu —</option>${viewNames.map(n=>`<option value="${esc(n)}">${esc(n)}</option>`).join('')}</select>
            <button type="button" class="klcs-btn" data-act="saveView">Lưu mẫu</button>
            <button type="button" class="klcs-btn" data-act="delView" ${viewNames.length?'':'disabled'}>Xóa mẫu</button>
            <button type="button" class="klcs-btn" data-act="export">Xuất Excel</button>
            <button type="button" class="klcs-btn" data-act="reset">Chọn lại tất cả</button>
          </div>
          <div class="klcs-out">${tableHtml(c)}${noteHtml(c)}</div>
        </div></details>`;
      Object.keys(wasOpenPick).forEach(id=>{ const d = el.querySelector(`.klcs-pick[data-pick="${id}"]`); if(d) d.open = true; });
      bind();
    }

    function refreshOut(){ // chỉ vẽ lại phần bảng + số đếm, giữ nguyên ô tìm / danh sách đang mở
      const c = compute();
      const out = el.querySelector('.klcs-out');
      if(out) out.innerHTML = tableHtml(c) + noteHtml(c);
      [['rows',rowItems,selRows,'Chọn Size'],['cols',colItems,selCols,'Chọn Đặc Tính']].forEach(([id,items,sel,label])=>{
        const sm = el.querySelector(`.klcs-pick[data-pick="${id}"]>summary`);
        if(sm) sm.textContent = `${label} (${items.filter(i=>sel.has(i.k)).length}/${items.length}) ▾`;
      });
    }

    function syncChecks(id){
      const items = id==='rows' ? rowItems : colItems, sel = id==='rows' ? selRows : selCols;
      el.querySelectorAll(`input[data-ck="${id}"]`).forEach(inp=>{ inp.checked = sel.has(items[+inp.dataset.i].k); });
    }

    function exportXlsx(){
      const c = compute();
      if(!c.shownRows.length || !c.colIdx.length){ alert('Chưa có gì để xuất — hãy chọn Size và Đặc Tính trước.'); return; }
      // PC 5.42: module nào có hàm onExport (Tồn kho An An) thì xuất file theo ĐÚNG MẪU bảng tổng hợp gốc của module đó.
      if(typeof opt.onExport === 'function'){
        try{
          const r = opt.onExport({
            columns: c.colIdx.map(i=>cols[i]),
            rows: c.body.map(b=>({ label:b.r.label, kind:b.r.xKind || (b.r.kind==='title' ? 'group-title' : 'group-size'), cells:c.colIdx.map(i=>b.r.cells[i]), total:b.rt }))
          });
          if(r && typeof r.catch === 'function') r.catch(e=>{ console.error('Lỗi xuất Excel Bảng Tổng Hợp Tùy Chọn:', e); alert('Không xuất được file Excel.'); });
        }catch(e){ console.error('Lỗi xuất Excel Bảng Tổng Hợp Tùy Chọn:', e); alert('Không xuất được file Excel.'); }
        return;
      }
      if(!window.XLSX){ alert('Thiếu thư viện Excel (XLSX).'); return; }
      const aoa = [['Size'].concat(c.colIdx.map(i=>cols[i]), ['Tổng chọn'])];
      c.body.forEach(b=>aoa.push([b.r.label].concat(c.colIdx.map(i=>isNum(b.r.cells[i])?b.r.cells[i]:''), [b.rt===null?'':b.rt])));
      aoa.push(['TỔNG CHỌN'].concat(c.colIdx.map((i,k)=>c.hasSum[k]?c.sums[k]:''), [c.grandHas?c.grand:'']));
      const ws = XLSX.utils.aoa_to_sheet(aoa);
      ws['!cols'] = [{wch:22}].concat(c.colIdx.map(()=>({wch:16})), [{wch:16}]);
      const wb = XLSX.utils.book_new(); XLSX.utils.book_append_sheet(wb, ws, 'Tuy chon');
      const d = new Date(), p = n=>String(n).padStart(2,'0');
      const name = `TongHopTuyChon_${d.getFullYear()}${p(d.getMonth()+1)}${p(d.getDate())}_${p(d.getHours())}${p(d.getMinutes())}.xlsx`;
      try{
        XLSX.writeFile(wb, name);   // PC 3.36: writeFile đã được bọc (lib/kl-xlsx-style.js) → file có viền + tiêu đề rõ + cột tự căn
      }catch(e){ try{ XLSX.writeFile(wb, name); }catch(e2){ console.error('Lỗi xuất Excel Bảng Tổng Hợp Tùy Chọn:', e2); alert('Không xuất được file Excel.'); } }
    }

    function bind(){
      const det = el.querySelector('details.klcs');
      det.addEventListener('toggle', e=>{ if(e.target === det) OPEN[key] = det.open; });

      ['rows','cols'].forEach(id=>{
        const items = id==='rows' ? rowItems : colItems, sel = id==='rows' ? selRows : selCols;
        el.querySelectorAll(`input[data-ck="${id}"]`).forEach(inp=>{
          inp.addEventListener('change', ()=>{
            const i = +inp.dataset.i, it = items[i];
            const apply = (k, on)=>{ on ? sel.add(k) : sel.delete(k); };
            apply(it.k, inp.checked);
            if(id==='rows' && it.cls==='ttl'){ // tick tiêu đề nhóm = tick cả nhóm bên dưới
              for(let j=i+1; j<items.length && items[j].cls!=='ttl'; j++) apply(items[j].k, inp.checked);
              syncChecks('rows');
            }
            persist(); refreshOut();
          });
        });
        const q = el.querySelector(`input[data-q="${id}"]`);
        if(q) q.addEventListener('input', ()=>{
          const t = stripAcc(q.value.trim());
          el.querySelectorAll(`[data-list="${id}"] .klcs-it`).forEach(l=>{ l.style.display = (!t || l.dataset.s.includes(t)) ? '' : 'none'; });
        });
        const bAll = el.querySelector(`[data-all="${id}"]`), bNone = el.querySelector(`[data-none="${id}"]`);
        const visibleKeys = ()=>Array.from(el.querySelectorAll(`[data-list="${id}"] .klcs-it`)).filter(l=>l.style.display!=='none').map(l=>items[+l.querySelector('input').dataset.i].k);
        if(bAll) bAll.addEventListener('click', ()=>{ visibleKeys().forEach(k=>sel.add(k)); syncChecks(id); persist(); refreshOut(); });
        if(bNone) bNone.addEventListener('click', ()=>{ visibleKeys().forEach(k=>sel.delete(k)); syncChecks(id); persist(); refreshOut(); });
      });

      // đóng hộp chọn khi bấm ra ngoài
      const onDoc = e=>{
        if(!document.body.contains(el)){ document.removeEventListener('mousedown', onDoc); return; }
        el.querySelectorAll('.klcs-pick[open]').forEach(d=>{ if(!d.contains(e.target)) d.open = false; });
      };
      document.removeEventListener('mousedown', el.__klcsDoc || (()=>{}));
      el.__klcsDoc = onDoc; document.addEventListener('mousedown', onDoc);

      el.querySelectorAll('[data-act]').forEach(b=>{
        const act = b.dataset.act;
        if(act === 'viewSel'){
          b.addEventListener('change', ()=>{
            const v = st.views[b.value]; if(!v) return;
            selRows.clear(); (v.sizes || rowKeys).filter(k=>rowKeys.includes(k)).forEach(k=>selRows.add(k));
            selCols.clear(); (v.cols || cols).filter(c=>cols.includes(c)).forEach(c=>selCols.add(c));
            persist(); render();
            const sel2 = el.querySelector('[data-act="viewSel"]'); if(sel2) sel2.value = b.value;
          });
          return;
        }
        b.addEventListener('click', ()=>{
          if(act === 'hideEmpty'){ st.hideEmpty = !st.hideEmpty; saveStore(key, st); render(); }
          else if(act === 'reset'){ rowKeys.forEach(k=>selRows.add(k)); cols.forEach(c=>selCols.add(c)); persist(); render(); }
          else if(act === 'export'){ exportXlsx(); }
          else if(act === 'saveView'){
            const name = (window.prompt('Đặt tên mẫu (VD: TGC xuất Nhật):') || '').trim();
            if(!name) return;
            if(st.views[name] && !window.confirm(`Mẫu "${name}" đã có — ghi đè?`)) return;
            st.views[name] = { sizes: selRows.size===rowKeys.length ? null : Array.from(selRows), cols: selCols.size===cols.length ? null : Array.from(selCols) };
            saveStore(key, st); render();
            const s2 = el.querySelector('[data-act="viewSel"]'); if(s2) s2.value = name;
          }
          else if(act === 'delView'){
            const s2 = el.querySelector('[data-act="viewSel"]'); const name = s2 && s2.value;
            if(!name){ alert('Chọn mẫu cần xóa ở ô "Mẫu đã lưu" trước.'); return; }
            if(!window.confirm(`Xóa mẫu "${name}"?`)) return;
            delete st.views[name]; saveStore(key, st); render();
          }
        });
      });
    }

    render();
  }

  window.KLCustomSummary = { mount };
})();
