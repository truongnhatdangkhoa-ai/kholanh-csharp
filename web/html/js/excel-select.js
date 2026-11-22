/* ============================================================
   BÔI ĐEN Ô KIỂU EXCEL — FILE DÙNG CHUNG (PC 3.0)
   Trước đây chép y hệt vào 7 file (M02, M03, M04, M05, M06, M08, Cài Đặt) — sửa 1 lỗi phải sửa 7 chỗ. Gom về đây;
   mỗi trang nạp <script src="js/excel-select.js"> SAU script riêng của trang. (Module 1 vẫn có bản riêng trong
   ton-kho-an-an.js vì gắn với các bảng của nó theo cách khác.)
   Kéo chuột từ 1 ô sang các ô khác trong CÙNG 1 bảng để chọn vùng chữ nhật; thanh trạng thái #excelStatusBar hiện
   Tổng / Trung bình / Nhỏ nhất / Lớn nhất của các ô có số; Ctrl+C chép vùng đang chọn dạng TSV (dán thẳng vào Excel).
   Tự nhận bảng mới qua MutationObserver; bắt sự kiện ủy quyền 1 lần / bảng (table.__xlDeleg).
   Bảng không muốn bôi đen: thêm thuộc tính data-no-excel.
   PC 1.33: (1) trang nào chưa có thanh #excelStatusBar thì file này TỰ tạo thanh + tự nạp CSS (chỉ cần thêm 1 dòng <script> vào trang —
   không phải chép CSS / markup như 7 trang cũ); (2) parseNum đọc SỐ chặt hơn: trước đây mọi chữ-có-số đều bị lọc lấy chữ số
   (VD mã hàng "VRPDITYEKMF203140JRA1" → 2031401, số xe "50E88725" → 5088725) nên Tổng bị cộng nhầm khi bôi đen lẫn cột mã.
   Nay chỉ nhận ô là SỐ thuần (có thể kèm dấu +/−, dấu phẩy nghìn, %, hoặc đơn vị chữ ngắn như "kg").
   ============================================================ */
(function(){
  function parseNum(raw){
    const s = String(raw||'').replace(/[\u00a0\s]+/g,' ').trim();
    if(!s) return NaN;
    if(window.KLNum){ const n = window.KLNum.parse(s.replace(/^[\u25B2\u25BC\u2191\u2193]\s?/,''), { unit:true }); return n == null ? NaN : n; }   // PC 2.32: quy chuẩn số chung (js/kl-num.js); phần dưới chỉ chạy khi trang chưa nạp kl-num.js
    const m = /^[\u25B2\u25BC\u2191\u2193]?\s?([-+\u2212]?)\s?(\d[\d,]*(?:\.\d+)?|\.\d+)\s?(?:%|[A-Za-z\u00C0-\u1EF9][A-Za-z\u00C0-\u1EF9\/.]{0,7})?$/.exec(s);
    if(!m) return NaN;
    const n = parseFloat(m[2].replace(/,/g,''));
    if(isNaN(n)) return NaN;
    return (m[1] === '-' || m[1] === '\u2212') ? -n : n;
  }

  // Trang chưa có thanh trạng thái (+ CSS) → tự tạo. Trang cũ đã có markup + CSS riêng thì giữ nguyên, không đụng.
  if(!document.getElementById('excelStatusBar')){
    const st = document.createElement('style');
    st.id = 'klExcelSelCss';
    st.textContent =
      'table.excel-select-enabled td.cell-selected{ background:rgba(255,255,255,.22) !important; box-shadow:inset 0 0 0 1px var(--accent, var(--theme-accent, #3b82f6)); }' +
      'table.excel-select-enabled tbody td{ user-select:none; -webkit-user-select:none; }' +
      '.excel-status-bar{ display:none; position:fixed; left:0; right:0; bottom:0; z-index:200; align-items:center; gap:22px; padding:8px 22px;' +
      ' background:var(--bg-elevated-solid, var(--theme-panel, #1b2430)); border-top:1px solid var(--border-soft, var(--theme-border, #334155)); box-shadow:0 -6px 18px rgba(0,0,0,.45);' +
      " font-family:'JetBrains Mono',monospace; font-size:12.5px; color:var(--text-muted, var(--theme-text-dim, #94a3b8)); }" +
      '.excel-status-bar b{ color:var(--accent, var(--theme-accent, #3b82f6)); font-weight:700; margin-left:5px; }' +
      ".excel-status-bar .esb-clear{ margin-left:auto; cursor:pointer; color:var(--text-muted, var(--theme-text-dim, #94a3b8)); font-family:'Inter',sans-serif; font-size:11.5px; border:1px solid var(--border, var(--theme-border, #334155)); border-radius:6px; padding:3px 9px; background:transparent; }" +
      '.excel-status-bar .esb-clear:hover{ color:var(--accent, var(--theme-accent, #3b82f6)); border-color:var(--accent, var(--theme-accent, #3b82f6)); }';
    (document.head || document.documentElement).appendChild(st);
    const bar = document.createElement('div');
    bar.id = 'excelStatusBar'; bar.className = 'excel-status-bar';
    (document.body || document.documentElement).appendChild(bar);
  }

  let isSelecting = false;
  let anchorCell = null;
  let activeTable = null;

  function getCellPos(td){
    const tr = td.closest('tr');
    if(!tr) return null;
    return { row: tr.rowIndex, col: td.cellIndex };
  }
  function clearHighlight(table){
    table.querySelectorAll('td.cell-selected').forEach(td=>td.classList.remove('cell-selected'));
  }
  let lastRange = null; // {r1,r2,c1,c2} của vùng đang bôi đen — dùng để copy khi bấm Ctrl+C
  function clearSelection(){
    if(activeTable) clearHighlight(activeTable);
    lastRange = null;
    const bar = document.getElementById('excelStatusBar');
    if(bar) bar.style.display = 'none';
  }
  function highlightRange(table, a, b){
    clearHighlight(table);
    const r1 = Math.min(a.row,b.row), r2 = Math.max(a.row,b.row);
    const c1 = Math.min(a.col,b.col), c2 = Math.max(a.col,b.col);
    lastRange = { r1, r2, c1, c2 };
    const cells = [];
    for(let r=r1;r<=r2;r++){
      const tr = table.rows[r]; if(!tr) continue;
      for(let c=c1;c<=c2;c++){
        const td = tr.cells[c]; if(!td) continue;
        td.classList.add('cell-selected');
        cells.push(td);
      }
    }
    updateStatusBar(cells);
  }

  // Bấm Ctrl+C (hoặc Cmd+C trên Mac) khi đang có vùng ô bôi đen kiểu Excel -> copy nội dung các
  // ô đó vào clipboard (dạng TSV, giống hệt copy trong Excel: mỗi dòng cách nhau xuống dòng, mỗi
  // ô trong dòng cách nhau bằng Tab, để dán được thẳng vào Excel/Google Sheets). Bảng dùng
  // user-select:none để việc kéo chọn vùng ô không bị lẫn với bôi đen chữ của trình duyệt (xem
  // class "excel-select-enabled" ở CSS) — nên bôi đen ô XONG mà bấm Ctrl+C như bình thường TRƯỚC
  // ĐÂY không copy được gì cả (không có vùng chọn chữ thật nào để trình duyệt copy). Nay bắt sự
  // kiện "copy" và tự đọc nội dung từ đúng vùng đang bôi đen (lastRange) để đưa vào clipboard.
  // PC 1.28: đang đứng trong ô nhập (input / textarea / select) → Ctrl+C là chép CHỮ TRONG Ô ĐÓ, không chép vùng bôi đen cũ của bảng
  // (lỗi: bôi đen 1 ô ở bảng danh sách phiếu rồi copy ở ô khác → dán ra giá trị ô cũ, vd "Ngày xuất").
  const inEditable = ()=>{ const a = document.activeElement; return !!(a && (a.tagName === 'INPUT' || a.tagName === 'TEXTAREA' || a.tagName === 'SELECT' || a.isContentEditable)); };
  document.addEventListener('focusin', (e)=>{ const t = e.target; if(t && t.tagName && /^(INPUT|TEXTAREA|SELECT)$/.test(t.tagName) && activeTable && !activeTable.contains(t)) clearSelection(); });
  document.addEventListener('copy', (e)=>{
    if(!activeTable || !lastRange) return;
    if(inEditable()) return;
    if(!activeTable.querySelector('td.cell-selected')) return;
    const rows = [];
    for(let r=lastRange.r1; r<=lastRange.r2; r++){
      const tr = activeTable.rows[r]; if(!tr) continue;
      const cols = [];
      for(let c=lastRange.c1; c<=lastRange.c2; c++){
        const td = tr.cells[c];
        cols.push(td ? td.textContent.trim() : '');
      }
      rows.push(cols.join('\t'));
    }
    e.clipboardData.setData('text/plain', rows.join('\n'));
    e.preventDefault();
  });
  function updateStatusBar(cells){
    const bar = document.getElementById('excelStatusBar');
    if(!bar) return;
    if(cells.length < 2){ bar.style.display = 'none'; return; }
    const nums = [];
    cells.forEach(td=>{
      const n = parseNum(td.textContent);
      if(!isNaN(n)) nums.push(n);
    });
    if(nums.length === 0){ bar.style.display = 'none'; return; }
    const sum = nums.reduce((a,b)=>a+b,0);
    const avg = sum/nums.length;
    const fmt = n => n.toLocaleString('en-US', { maximumFractionDigits: 2 });
    bar.innerHTML =
      `<span>Đã chọn: <b>${cells.length.toLocaleString('en-US')}</b> ô (${nums.length.toLocaleString('en-US')} số)</span>` +
      `<span>Tổng: <b>${fmt(sum)}</b></span>` +
      `<span>Trung bình: <b>${fmt(avg)}</b></span>` +
      `<span>Nhỏ nhất: <b>${fmt(Math.min(...nums))}</b></span>` +
      `<span>Lớn nhất: <b>${fmt(Math.max(...nums))}</b></span>` +
      `<button type="button" class="esb-clear" id="esbClearBtn">Bỏ chọn ✕</button>`;
    bar.style.display = 'flex';
    const clearBtn = document.getElementById('esbClearBtn');
    if(clearBtn) clearBtn.addEventListener('click', clearSelection);
  }

  function initExcelLikeSelection(table){
    if(!table || table.hasAttribute('data-no-excel')) return; // VD bảng phân quyền ở Cài Đặt: bấm dòng = mở popup
    table.classList.add('excel-select-enabled');
    // HIỆU NĂNG (rà soát PC 2.9): bắt sự kiện kiểu ủy quyền trên CHÍNH bảng, gắn 1 lần / bảng — trước đây gắn 2
    // listener cho TỪNG ô sau mỗi lần vẽ (bảng 4.000 dòng × 12 cột ≈ 96.000 listener mỗi lần lọc / chuyển trang).
    if(table.__xlDeleg) return;
    table.__xlDeleg = true;
    const cellOf = (e)=>{
      const td = e.target.closest && e.target.closest('td');
      return td && td.closest('table') === table && td.parentElement && td.parentElement.parentElement
        && td.parentElement.parentElement.tagName === 'TBODY' ? td : null;
    };
    table.addEventListener('mousedown', (e)=>{
      const td = cellOf(e); if(!td) return;
      if(e.button !== 0 || e.target.closest('input, button, a, textarea, select')) return;
      const pos = getCellPos(td);
      if(!pos) return;
      isSelecting = true;
      anchorCell = pos;
      activeTable = table;
      highlightRange(table, anchorCell, anchorCell);
      e.preventDefault();
    });
    table.addEventListener('mouseover', (e)=>{
      if(!isSelecting || !anchorCell || activeTable !== table) return;
      const td = cellOf(e); if(!td) return;
      const pos = getCellPos(td);
      if(pos) highlightRange(table, anchorCell, pos);
    });
  }
  document.addEventListener('mouseup', ()=>{ isSelecting = false; });

  function scanAndInit(){
    document.querySelectorAll('table').forEach(initExcelLikeSelection);
  }

  let scheduled = false;
  function scheduleScan(){
    if(scheduled) return;
    scheduled = true;
    requestAnimationFrame(()=>{ scheduled = false; scanAndInit(); });
  }

  const target = document.querySelector('main') || document.body;
  const observer = new MutationObserver(scheduleScan);
  observer.observe(target, { childList: true, subtree: true });
  scanAndInit();
})();
