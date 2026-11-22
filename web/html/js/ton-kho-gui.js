  // ===== Kết nối CSDL Excel (window.dbAPI) — xem chi tiết cơ chế trong main.js/preload.js.
  // Mỗi module dùng 1 dbKey + 1 file .xlsx riêng trong thư mục /database/ (y hệt cách Module 3 -
  // nxt-bot-sot.html và Module 4 - nxt-hang-nhap-khau.html đã làm). dbKey của module này là
  // 'module2' -> file database/module2_tonkhogui.xlsx (xem DB_FILES trong main.js) — đúng
  // theo vị trí "Module 02" hiện tại trên Dashboard (xem index.html).
  // Khi mở thẳng file .html này bằng trình duyệt thường (không qua Electron), window.dbAPI sẽ
  // không tồn tại -> hasDbAPI() trả về false -> mọi thao tác đọc/ghi tự động dùng localStorage
  // như phương án dự phòng, ứng dụng vẫn chạy được bình thường.
  let m2CanWrite = true; // PC 7.54: khai báo ĐẦU file (tránh TDZ) — chỉ Quản lý trở lên được sửa Đặc Tính / điều kiện phân loại (xem applyDtLock)
  const DB_KEY = 'ton-kho-gui';
  function hasDbAPI(){
    return typeof window !== 'undefined' && !!window.dbAPI && typeof window.dbAPI.writeSheet === 'function';
  }

  // ==== Hộp thoại xác nhận dùng chung cho các thao tác Lưu / Sửa / Xóa ====
  // showConfirmDialog('Nội dung...') hoặc
  // showConfirmDialog({ title, message, confirmText, cancelText, danger:true })
  // => trả về Promise<boolean> (true nếu bấm "Xác nhận", false nếu "Hủy"/đóng/Esc)
  function showConfirmDialog(opts){
    const cfg = (typeof opts === 'string') ? { message: opts } : (opts || {});
    const title       = cfg.title       || 'Xác nhận thao tác';
    const message     = cfg.message     || 'Bạn có chắc chắn muốn thực hiện thao tác này?';
    const confirmText = cfg.confirmText || 'Xác nhận';
    const cancelText  = cfg.cancelText  || 'Hủy';
    const danger      = !!cfg.danger;

    return new Promise((resolve)=>{
      const overlay    = document.getElementById('gcOverlay');
      const titleEl    = document.getElementById('gcTitle');
      const messageEl  = document.getElementById('gcMessage');
      const confirmBtn = document.getElementById('gcConfirmBtn');
      const cancelBtn  = document.getElementById('gcCancelBtn');
      const closeBtn   = document.getElementById('gcCloseBtn');

      titleEl.textContent    = title;
      messageEl.textContent  = message;
      confirmBtn.textContent = confirmText;
      cancelBtn.textContent  = cancelText;
      confirmBtn.className = 'px-modal-btn ' + (danger ? 'danger' : 'primary');

      overlay.style.display = 'flex';

      function finish(result){
        overlay.style.display = 'none';
        confirmBtn.removeEventListener('click', onConfirm);
        cancelBtn.removeEventListener('click', onCancel);
        closeBtn.removeEventListener('click', onCancel);
        overlay.removeEventListener('mousedown', onOverlayClick);
        document.removeEventListener('keydown', onKeyDown);
        resolve(result);
      }
      function onConfirm(){ finish(true); }
      function onCancel(){ finish(false); }
      function onOverlayClick(e){ if(e.target === overlay) finish(false); }
      function onKeyDown(e){ if(e.key === 'Escape') finish(false); }

      confirmBtn.addEventListener('click', onConfirm);
      cancelBtn.addEventListener('click', onCancel);
      closeBtn.addEventListener('click', onCancel);
      overlay.addEventListener('mousedown', onOverlayClick);
      document.addEventListener('keydown', onKeyDown);
    });
  }
  // Đồng hồ sidebar đã được ẩn trong module này theo yêu cầu.

  // tabs
  document.querySelectorAll('nav.menu > button').forEach(btn=>{
    btn.addEventListener('click', ()=>{
      if(btn.classList.contains('disabled')) return;
      document.querySelectorAll('nav.menu > button').forEach(b=>b.classList.remove('active'));
      btn.classList.add('active');
      document.querySelectorAll('main > section').forEach(s=>s.style.display='none');
      document.getElementById('tab-'+btn.dataset.tab).style.display='flex';
      document.getElementById('tab-'+btn.dataset.tab).style.flexDirection='column';
      document.getElementById('tab-'+btn.dataset.tab).style.gap='20px';
      if(btn.dataset.tab === 'dl') refreshDuLieuTab();
      if(btn.dataset.tab === 'px') refreshPhieuXuatTab();
      if(btn.dataset.tab === 'pl'){
        // Bấm nút "Phân Loại" -> nhảy thẳng tới đúng module con đang xem gần nhất (mặc định
        // "Tôm Nguyên Liệu" nếu chưa từng xem module con nào) — ĐỒNG BỘ đúng kiểu Module "Báo
        // Cáo Tồn": menu con hiện dạng thanh ngang NGAY TRÊN bảng dữ liệu (xem .pl-subnav ở
        // đầu #tab-pl), không còn là dropdown xổ dưới nút nữa.
        const activeSub = document.querySelector('.pl-subtab-btn.active') || document.querySelector('.pl-subtab-btn[data-plsub="nl"]');
        if(activeSub) activeSub.click();
      }
      if(btn.dataset.tab === '6'){ renderDtManualPreview(); refreshDacTinhTab(); }
      if(btn.dataset.tab === '7'){ refreshBaoCaoTab(); refreshDoiSoatTab(); }
    });
  });

  // sub-tabs: PHÂN LOẠI (Tôm Nguyên Liệu / HLSO / Giám Sát / Gia Công / Ấn Độ / Ecuador + tab "Điều kiện lọc" PC 1.25)
  document.querySelectorAll('.pl-subtab-btn').forEach(btn=>{
    btn.addEventListener('click', ()=>{
      // PC 1.25: nhớ module đang xem để tab "Điều kiện lọc" mở sẵn đúng module đó
      const prevBtn = document.querySelector('.pl-subtab-btn.active');
      const prevSub = prevBtn ? prevBtn.dataset.plsub : null;
      document.querySelectorAll('.pl-subtab-btn').forEach(b=>b.classList.remove('active'));
      btn.classList.add('active');
      ['nl','5','4','3','ad','2','dk'].forEach(id=>{
        document.getElementById('tab-'+id).style.display='none';
      });
      const sec = document.getElementById('tab-'+btn.dataset.plsub);
      sec.style.display='flex';
      sec.style.flexDirection='column';
      sec.style.gap='20px';
      if(btn.dataset.plsub === '2') refreshEcuadorTab();
      if(btn.dataset.plsub === 'ad') refreshAnDoTab();
      if(btn.dataset.plsub === '3') refreshTGCTab();
      if(btn.dataset.plsub === '4') refreshTGSTab();
      if(btn.dataset.plsub === 'nl') refreshNguyenLieuTab();
      if(btn.dataset.plsub === '5') refreshHLSOTab();
      if(btn.dataset.plsub === 'dk'){
        if(prevSub && prevSub !== 'dk' && DKLOC_BY_ID[prevSub]) dkLocMod = prevSub;
        refreshDkLocTab();
      }
    });
  });

  // ---- Excel loading logic ----
  const dropZone   = document.getElementById('dropZone');
  const fileInput  = document.getElementById('fileInput');
  const statusEl   = document.getElementById('status');
  const fileInfo   = document.getElementById('fileInfo');
  const fName      = document.getElementById('fName');
  const fSize      = document.getElementById('fSize');
  const fSheets    = document.getElementById('fSheets');
  const sheetSelect= document.getElementById('sheetSelect');
  const clearDataBtn = document.getElementById('clearDataBtn');
  const tableScroll= document.getElementById('tableScroll');
  const rowCount   = document.getElementById('rowCount');
  const summaryStats  = document.getElementById('summaryStats');
  const pagination    = document.getElementById('pagination');
  const pageSizeSelect= document.getElementById('pageSizeSelect');
  const prevPageBtn   = document.getElementById('prevPage');
  const nextPageBtn   = document.getElementById('nextPage');
  const pageIndicator = document.getElementById('pageIndicator');
  const dzChooseBtn   = document.getElementById('dzChooseBtn');

  let workbook = null;
  let currentColumns = [];
  let currentDisplayRows = [];
  let baseDisplayRows = [];   // toàn bộ dòng của trang hiện tại, CHƯA áp dụng tìm kiếm theo tiêu đề
  let searchFilters = {};     // { colIndex: "chuỗi tìm kiếm" } — dòng tìm kiếm thông minh dưới tiêu đề
  let lastHeaderKey = null;   // dùng để biết khi nào cần vẽ lại tiêu đề + ô tìm kiếm (tránh mất focus khi gõ)
  let isFilteredMode = false;
  let currentPage = 1;
  let pageSize = 100;

  dropZone.addEventListener('click', ()=> fileInput.click());
  dropZone.addEventListener('dragover', e=>{ e.preventDefault(); dropZone.classList.add('drag'); });
  dropZone.addEventListener('dragleave', ()=> dropZone.classList.remove('drag'));
  dropZone.addEventListener('drop', e=>{
    e.preventDefault();
    dropZone.classList.remove('drag');
    if(e.dataTransfer.files.length) handleFile(e.dataTransfer.files[0]);
  });
  fileInput.addEventListener('change', e=>{
    if(e.target.files.length) handleFile(e.target.files[0]);
  });
  // Nút "Chọn file" (thêm cùng bố cục thanh ngang gọn, giống Module 1) — chặn nổi bọt để không
  // gọi fileInput.click() 2 lần do dropZone phía trên cũng đã có listener click riêng.
  if(dzChooseBtn) dzChooseBtn.addEventListener('click', e=>{ e.stopPropagation(); fileInput.click(); });

  function setStatus(msg, type){
    statusEl.textContent = msg;
    statusEl.className = 'status-line' + (type ? ' '+type : '');
  }

  function formatSize(bytes){
    if(bytes < 1024) return bytes + ' B';
    if(bytes < 1024*1024) return (bytes/1024).toFixed(1) + ' KB';
    return (bytes/1024/1024).toFixed(2) + ' MB';
  }

  let m2Src = null;
  function handleFile(file){
    setStatus('Đang đọc file...', null);
    const reader = new FileReader();
    reader.onload = (e)=>{
      try{
        const data = new Uint8Array(e.target.result);
        workbook = XLSX.read(data, {type:'array'});
        m2Src = { name: file.name, size: file.size, buf: e.target.result }; // PC 3.12: giữ bản gốc để cất + lấy dấu vân tay khi bấm Nạp

        fName.textContent = file.name;
        fSize.textContent = formatSize(file.size);
        m2PendingDate = guessDataDateFromFileName(file.name); // đoán từ tên file — sửa được trước khi Nạp
        renderM2DataDate();
        fSheets.textContent = workbook.SheetNames.length;

        sheetSelect.innerHTML = '';
        workbook.SheetNames.forEach(name=>{
          const opt = document.createElement('option');
          opt.value = name; opt.textContent = name;
          sheetSelect.appendChild(opt);
        });

        fileInfo.classList.add('show');
        renderSheet(workbook.SheetNames[0]);
        persistLoadedFile(workbook.SheetNames[0]); // PC 8.52: lưu file đã nạp để lần sau còn kiểm tra
      }catch(err){
        console.error(err);
        setStatus('Lỗi khi đọc file: ' + err.message, 'err');
      }
    };
    reader.onerror = ()=> setStatus('Không thể đọc file này.', 'err');
    reader.readAsArrayBuffer(file);
  }

  sheetSelect.addEventListener('change', ()=>{ renderSheet(sheetSelect.value); persistLoadedFile(sheetSelect.value); });

  // PC 8.52: LƯU FILE ĐÃ NẠP (tab Load data) — giống Module 1 (sheet LoadData + LoadDataMeta). Trước đây tắt app là mất bảng "Dữ liệu đã nạp"
  // (chỉ còn menu "Dữ Liệu" sau khi bấm Nạp) → không kiểm tra lại được file đã dùng. Nay: sheet đang xem của file vừa chọn ghi vào sheet FileNap
  // (mỗi DÒNG Excel = 1 dòng file, cột c0, c1… theo thứ tự cột gốc) + FileNap_Meta (1 ô JSON nhỏ: tên file, dung lượng, số sheet, sheet đang xem, lúc lưu).
  // Mở lại module → dựng lại workbook 1 sheet từ dữ liệu này rồi gọi renderSheet() như vừa chọn file. Chọn file khác / bấm Xóa → ghi đè / xóa.
  const FILE_SHEET = 'FileNap', FILE_META_SHEET = 'FileNap_Meta';
  async function writeFileNap(rows, meta){
    if(!hasDbAPI()) return;
    const payload = { [FILE_SHEET]: rows, [FILE_META_SHEET]: meta ? [{ data: '__JSON__' + JSON.stringify(meta) }] : [] };
    if(typeof window.dbAPI.writeSheets === 'function'){ const r = await window.dbAPI.writeSheets(DB_KEY, payload); if(!r || !r.ok) console.error('Lỗi lưu file đã nạp (module2):', r && r.error); return; }
    await window.dbAPI.writeSheet(DB_KEY, FILE_SHEET, payload[FILE_SHEET]); await window.dbAPI.writeSheet(DB_KEY, FILE_META_SHEET, payload[FILE_META_SHEET]);
  }
  async function persistLoadedFile(sheetName){
    if(!hasDbAPI() || !workbook || !workbook.Sheets[sheetName]) return;
    try{
      const json = XLSX.utils.sheet_to_json(workbook.Sheets[sheetName], {header:1, defval:''});
      const w = json.reduce((m, r)=> Math.max(m, r.length), 0);
      const rows = json.map(r=>{ const o = {}; for(let i = 0; i < w; i++) o['c' + i] = r[i] ?? ''; return o; });
      await writeFileNap(rows, { fileName: fName.textContent, size: fSize.textContent, sheetCount: workbook.SheetNames.length, sheet: sheetName, cols: w, savedAt: new Date().toISOString() });
    }catch(e){ console.error('Lỗi lưu file đã nạp (module2):', e); }
  }
  async function hydrateLoadedFileFromDb(){
    if(!hasDbAPI() || workbook) return false;
    try{
      const [mr, dr] = await Promise.all([window.dbAPI.readSheet(DB_KEY, FILE_META_SHEET), window.dbAPI.readSheet(DB_KEY, FILE_SHEET)]);
      const cell = mr && mr.ok && Array.isArray(mr.data) && mr.data[0] && mr.data[0].data;
      if(typeof cell !== 'string' || !cell.startsWith('__JSON__') || !dr || !dr.ok || !Array.isArray(dr.data)) return false;
      const meta = JSON.parse(cell.slice('__JSON__'.length)) || {}, w = Number(meta.cols) || 0;
      if(workbook || !w) return false; // người dùng vừa chọn file khác trong lúc đọc / file rỗng
      const aoa = dr.data.map(o=>{ const a = []; for(let i = 0; i < w; i++) a.push(o['c' + i] ?? ''); return a; });
      const nm = String(meta.sheet || 'Sheet1');
      const wb = XLSX.utils.book_new(); XLSX.utils.book_append_sheet(wb, XLSX.utils.aoa_to_sheet(aoa.length ? aoa : [[]]), nm);
      workbook = wb;
      fName.textContent = meta.fileName || '—'; fSize.textContent = meta.size || '—'; fSheets.textContent = meta.sheetCount || 1;
      m2PendingDate = guessDataDateFromFileName(meta.fileName || '') || ''; renderM2DataDate();
      sheetSelect.innerHTML = ''; const opt = document.createElement('option'); opt.value = nm; opt.textContent = nm; sheetSelect.appendChild(opt);
      fileInfo.classList.add('show');
      renderSheet(nm);
      let when = ''; try{ when = new Date(meta.savedAt).toLocaleString('vi-VN'); }catch(e){ if(window.klLog) klLog('ton-kho-gui.js:247',e); }
      setStatus('Đã mở lại file nạp lần trước: ' + (meta.fileName || '') + (when ? ' (lưu lúc ' + when + ')' : '') + '. Chọn file khác để thay thế.', 'ok');
      return true;
    }catch(e){ console.error('Lỗi đọc file đã nạp (module2):', e); return false; }
  }

  // Xóa toàn bộ dữ liệu đã nạp — đưa app về trạng thái ban đầu như chưa nạp file nào.
  function clearLoadedData(){
    workbook = null;
    currentColumns = [];
    currentDisplayRows = [];
    baseDisplayRows = [];
    searchFilters = {};
    lastHeaderKey = null;
    filteredRows = null;
    rawDacTinhValues = [];
    isFilteredMode = false;
    currentPage = 1;

    writeFileNap([], null).catch(()=>{}); // PC 8.52: xóa luôn bản lưu file đã nạp
    fileInput.value = '';
    fName.textContent = '—';
    fSize.textContent = '—';
    fSheets.textContent = '—';
    sheetSelect.innerHTML = '';
    fileInfo.classList.remove('show');
    m2PendingDate = null;
    renderM2DataDate();
    setStatus('Chưa có file nào được nạp.', null);

    tableScroll.innerHTML = '<div class="empty-table">Chưa có dữ liệu để hiển thị</div>';
    rowCount.textContent = '';
    exportBtn.style.display = 'none';
    pagination.style.display = 'none';
    summaryStats.innerHTML = '';
    renderLdStats();
    resetTableCheckboxFilters('main');
    toolbarLabel.textContent = 'Chưa có dữ liệu';

    refreshPhieuXuatTab();
    refreshEcuadorTab();
    refreshAnDoTab();
    refreshTGCTab();
    refreshTGSTab();
    refreshNguyenLieuTab();
    refreshHLSOTab();
    refreshDacTinhTab();
    refreshBaoCaoTab();
    refreshDoiSoatTab();
  }

  clearDataBtn.addEventListener('click', async ()=>{
    const ok = await showConfirmDialog({
      title: 'Xác nhận xóa dữ liệu',
      message: 'Xóa toàn bộ dữ liệu đã nạp ở "Load data"?\nThao tác này sẽ đặt lại tất cả các tab (Báo Cáo, Đối Soát, Ecuador, Ấn Độ, Gia Công, Giám Sát, Nguyên Liệu, HLSO).\nDữ liệu Đặc Tính đã gộp/lưu trước đó sẽ KHÔNG bị xóa.',
      confirmText: 'Xóa dữ liệu',
      danger: true
    });
    if(!ok) return;
    clearLoadedData();
  });

  const exportBtn    = document.getElementById('exportBtn');
  const toolbarLabel = document.getElementById('toolbarLabel');
  let filteredRows = null; // dữ liệu đã lọc TẠM THỜI của tab "Load data" — chỉ tồn tại trong phiên
                            // đang mở, MẤT ngay khi không có file nào được nạp (đóng app/tải lại
                            // trang mà chưa chọn lại file). Dùng cho phần xem trước + xuất Excel
                            // NGAY TẠI tab "Load data" mà thôi.
  let rawDacTinhValues = []; // đặc tính lấy trực tiếp từ file KHÔNG khớp mẫu tồn kho (chỉ cần có cột "Đặc tính")

  // ===== "Dữ Liệu" — bản sao LÂU DÀI của "Dữ liệu đã lọc", lưu trong localStorage (đúng cơ chế
  // đã dùng cho Phiếu Xuất/Đặc Tính ở file này) — KHÔNG mất khi không có file nạp ở "Load data".
  // Tất cả các tab còn lại (Phiếu Xuất, Ecuador, Ấn Độ, Gia Công, Giám Sát, Nguyên Liệu, HLSO,
  // Đặc Tính, Báo Cáo, Đối Soát) đọc dữ liệu từ ĐÂY (persistedRows) thay vì từ filteredRows —
  // nhấn nút "Nạp Dữ Liệu" ở tab "Load data" để chép filteredRows hiện tại vào đây (ghi đè bản cũ).
  const DULIEU_STORAGE_KEY = 'khogui_duLieuChinhThuc_v1';
  let persistedRows = [];

  // Chuyển 1 dòng persistedRows (object theo key nội bộ) <-> 1 dòng sheet "DuLieuChinh" của
  // database Excel (object theo nhãn cột tiếng Việt, TARGET_COLS) — dùng chung cho save/load DB.
  // Tên cột "ẩn" lưu cờ đã trừ tồn theo phiếu xuất — KHÔNG nằm trong TARGET_COLS nên không hiện
  // thành cột trên bảng, chỉ đi kèm trong file database Excel để cờ tô đỏ không bị mất khi mở lại app.
  const PX_DAXUAT_DB_LABEL = 'Đã trừ theo phiếu xuất';

  function duLieuRowToDb(r){
    const o = {};
    TARGET_COLS.forEach(c => { o[c.label] = (r[c.key] === undefined || r[c.key] === null) ? '' : r[c.key]; });
    o[PX_DAXUAT_DB_LABEL] = r._pxDaXuat ? 1 : 0;
    return o;
  }
  function dbRowToDuLieu(o){
    const r = {};
    TARGET_COLS.forEach(c=>{
      let v = o[c.label];
      if(v === undefined || v === null) v = '';
      if(c.key === 'soKien' || c.key === 'trongLuong'){
        v = (v === '') ? 0 : Number(v);
        if(isNaN(v)) v = 0;
      }
      r[c.key] = v;
    });
    r._pxDaXuat = !!(Number(o[PX_DAXUAT_DB_LABEL]) || o[PX_DAXUAT_DB_LABEL] === true);
    return r;
  }

  function loadPersistedRows(){
    try{
      const raw = localStorage.getItem(DULIEU_STORAGE_KEY);
      persistedRows = raw ? JSON.parse(raw) : [];
    }catch(e){ persistedRows = []; }
  }
  // savePersistedRows() GỘP LỆNH (rà soát 09/2026, giống persist() Module 1): mỗi lần lưu ghi lại toàn bộ sheet
  // DuLieuChinh → cả file Module 2. Đang ghi thì mọi lệnh mới gộp thành ĐÚNG 1 lần ghi kế tiếp (trạng thái mới nhất).
  let saveRowsRun = null, saveRowsNext = null;
  function savePersistedRows(){
    if(saveRowsNext) return saveRowsNext;
    if(saveRowsRun){
      saveRowsNext = saveRowsRun.catch(()=>{}).then(()=>{ saveRowsNext = null; return startSaveRows(); });
      return saveRowsNext;
    }
    return startSaveRows();
  }
  function startSaveRows(){
    saveRowsRun = savePersistedRowsNow().finally(()=>{ saveRowsRun = null; });
    return saveRowsRun;
  }
  async function savePersistedRowsNow(){
    // Bản sao localStorage chỉ là đệm hiển thị nhanh lúc mở (database Excel mới là nguồn chính). Dữ liệu lớn vượt
    // hạn mức ~5 MB của localStorage thì lệnh ghi lỗi và bản CŨ nằm lại → xoá luôn để không bao giờ hiện số cũ.
    try{
      const js = JSON.stringify(persistedRows);
      if(js.length < 2000000) localStorage.setItem(DULIEU_STORAGE_KEY, js); else localStorage.removeItem(DULIEU_STORAGE_KEY);
    }catch(e){ try{ localStorage.removeItem(DULIEU_STORAGE_KEY); }catch(_){ } }
    if(hasDbAPI()){
      try{
        const res = window.KLLuuDong ? await window.KLLuuDong.save(DB_KEY, 'DuLieuChinh', persistedRows.map(duLieuRowToDb)) : await window.dbAPI.writeSheet(DB_KEY, 'DuLieuChinh', persistedRows.map(duLieuRowToDb)); // PC 1.68: lưu theo dòng
        if(!res || !res.ok){ console.error('Lỗi lưu database Excel (module2 - Dữ Liệu):', res && res.error); return false; }
      }catch(e){ console.error('Lỗi lưu database Excel (module2 - Dữ Liệu):', e); return false; }
    }
    return true;
  }
  // Mốc kỹ thuật "lúc nạp file gần nhất" cho menu "Dữ Liệu" — CHỈ ghi khi người dùng bấm nút
  // "Nạp Dữ Liệu" (thay hẳn dữ liệu cũ bằng file mới), KHÔNG ghi lại mỗi khi sửa/xoá dòng lẻ, để
  // đúng cùng ý nghĩa với "importedAt" của Module 1 (thời điểm nạp file, không phải ngày nghiệp
  // vụ trong dữ liệu). Bảng "Tổng Quan Dữ Liệu" ở trang chủ (dashboard-overview.js) đọc sheet
  // này để hiển thị cột "Cập nhật gần nhất" — xem loadModule2Overview().
  const DULIEU_META_DB_SHEET = 'DuLieuMeta';
  const DULIEU_META_LS_KEY = 'module2.duLieuMeta';
  // "Ngày số liệu" (dataDate, dd/mm/yyyy) = ngày CỦA số tồn trong file, KHÁC importedAt (lúc nạp).
  // Lưu cùng ô JSON của sheet DuLieuMeta → main.js đẩy lên Sheet (Meta m02DataDate) cho điện thoại,
  // dashboard-overview.js hiện ở Tổng Quan, và hộp "Ngày báo cáo" điền sẵn ngày này.
  // m2PendingDate: ngày đoán/nhập cho file ĐANG CHỌN nhưng CHƯA bấm "Nạp Dữ Liệu" (null = không có file chờ).
  let m2Meta = { importedAt: null, dataDate: '' };
  let m2PendingDate = null;
  try{ const raw = localStorage.getItem(DULIEU_META_LS_KEY); if(raw) m2Meta = Object.assign(m2Meta, JSON.parse(raw)); }catch(e){}
  async function saveDuLieuMeta(){
    try{ localStorage.setItem(DULIEU_META_LS_KEY, JSON.stringify(m2Meta)); }catch(e){}
    if(!hasDbAPI()) return;
    try{
      const res = await window.dbAPI.writeSheet(DB_KEY, DULIEU_META_DB_SHEET, [{ data: '__JSON__' + JSON.stringify(m2Meta) }]);
      if(!res || !res.ok) console.error('Lỗi lưu DuLieuMeta (module2 - Dữ Liệu):', res && res.error);
    }catch(e){ console.error('Lỗi lưu DuLieuMeta (module2 - Dữ Liệu):', e); }
  }
  async function hydrateDuLieuMetaFromDb(){
    if(!hasDbAPI()) return false;
    try{
      const res = await window.dbAPI.readSheet(DB_KEY, DULIEU_META_DB_SHEET);
      const cell = res && res.ok && Array.isArray(res.data) && res.data[0] && res.data[0].data;
      if(typeof cell === 'string' && cell.startsWith('__JSON__')){
        const m = JSON.parse(cell.slice('__JSON__'.length)) || {};
        m2Meta = { importedAt: m.importedAt || null, dataDate: m.dataDate || '', napId: m.napId || '' };
        try{ localStorage.setItem(DULIEU_META_LS_KEY, JSON.stringify(m2Meta)); }catch(e){}
        return true;
      }
    }catch(e){ console.error('Lỗi đọc DuLieuMeta (module2 - Dữ Liệu):', e); }
    return false;
  }
  // Ô "Ngày số liệu" ở bảng "Dữ liệu đã nạp": có file đang chờ nạp → sửa ngày CHỜ (lưu khi bấm Nạp Dữ
  // Liệu); không có → sửa thẳng ngày của dữ liệu đã lưu.
  function renderM2DataDate(){
    const inp = document.getElementById('m2DataDate');
    const note = document.getElementById('m2DataDateNote');
    if(!inp) return;
    const pending = m2PendingDate !== null;
    const v = pending ? m2PendingDate : (m2Meta.dataDate || '');
    if(document.activeElement !== inp) inp.value = v;
    inp.classList.toggle('dd-missing', !v);
    inp.classList.remove('dd-bad');
    if(note){
      note.className = pending ? 'dd-pending' : '';
      note.textContent = pending
        ? (v ? 'Của file đang chọn — lưu khi bấm Nạp Dữ Liệu' : 'Nhập ngày cho file đang chọn')
        : (v ? '' : (m2Meta.importedAt ? 'Chưa nhập — đang dùng ngày nạp' : ''));
    }
  }
  function m2DataDateLabel(){
    if(m2Meta.dataDate) return m2Meta.dataDate;
    if(m2Meta.importedAt){ const d = new Date(m2Meta.importedAt); return ddmmFmt(d.getDate(), d.getMonth()+1, d.getFullYear()) + ' (ngày nạp)'; }
    return '';
  }
  (function bindM2DataDate(){
    const inp = document.getElementById('m2DataDate');
    if(!inp) return;
    const commit = async ()=>{
      const raw = inp.value.trim();
      const v = parseDataDate(raw);
      if(raw && !v){ inp.classList.add('dd-bad'); inp.title = 'Ngày không hợp lệ — gõ dạng dd/mm/yyyy'; return; }
      inp.title = '';
      if(m2PendingDate !== null) m2PendingDate = v;
      else if(v !== (m2Meta.dataDate || '')){ m2Meta.dataDate = v; await saveDuLieuMeta(); refreshDuLieuTab(); }
      renderM2DataDate();
    };
    inp.addEventListener('keydown', e=>{
      if(e.key === 'Enter'){ e.preventDefault(); inp.blur(); }
      else if(e.key === 'Escape'){ inp.value = m2PendingDate !== null ? m2PendingDate : (m2Meta.dataDate || ''); inp.blur(); }
    });
    inp.addEventListener('change', commit);
  })();
  renderM2DataDate();
  // Đọc lại "Dữ Liệu" từ database Excel (nếu có window.dbAPI) — GHI ĐÈ bản localStorage vừa nạp
  // tạm ở trên bằng dữ liệu THẬT trong file Excel, đúng cơ chế 2 pha (hiện ngay bản cũ trong lúc
  // chờ, rồi thay bằng bản mới nhất đọc từ đĩa) đã dùng ở Module 2/3. Xem lệnh gọi ở khối init()
  // cuối file.
  async function hydratePersistedRowsFromDb(){
    if(!hasDbAPI()) return false;
    try{
      const res = window.KLLuuDong ? await window.KLLuuDong.read(DB_KEY, 'DuLieuChinh') : await window.dbAPI.readSheet(DB_KEY, 'DuLieuChinh'); // PC 1.68: đọc + nhớ (lưu theo dòng)
      if(res && res.ok){
        persistedRows = (res.data || []).map(dbRowToDuLieu);
        return true;
      }
      console.error('Đọc database Excel (module6 - Dữ Liệu) không hợp lệ, giữ nguyên dữ liệu localStorage.');
    }catch(e){ console.error('Lỗi đọc database Excel (module6 - Dữ Liệu):', e); }
    return false;
  }
  loadPersistedRows(); // nạp ngay lúc mở trang (localStorage), để mọi tab khác có dữ liệu kể cả khi chưa chọn file

  // ===== TAB "DỮ LIỆU" — hiển thị persistedRows, y hệt cơ chế bảng/phân trang của tab Ecuador =====
  const toolbarLabelDL = document.getElementById('toolbarLabelDL');
  const tableScrollDL  = document.getElementById('tableScrollDL');
  const rowCountDL     = document.getElementById('rowCountDL');
  const summaryStatsDL = document.getElementById('summaryStatsDL');
  const paginationDL   = document.getElementById('paginationDL');
  const exportBtnDL    = document.getElementById('exportBtnDL');
  const clearBtnDL     = document.getElementById('clearBtnDL');
  const pageSizeSelectDL = document.getElementById('pageSizeSelectDL');
  const prevPageBtnDL = document.getElementById('prevPageDL');
  const nextPageBtnDL = document.getElementById('nextPageDL');
  const pageIndicatorDL = document.getElementById('pageIndicatorDL');

  let duLieuPage = 1;
  let duLieuPageSize = 100;
  let duLieuSearchState = { searchFilters:{}, lastHeaderKey:null, bound:false };
  // Ô tìm chung "BTP 50 MKS Nau" (js/smart-search.js, 09/2026): lọc persistedRows TRƯỚC các ô tìm / lọc theo cột.
  let duLieuQ = '';
  let duLieuIdx = null; // { rows, len, idx }
  function duLieuSmartRows(){
    const S = window.KLSmartSearch;
    if(!duLieuQ.trim() || !S) return persistedRows;
    if(!duLieuIdx || duLieuIdx.rows !== persistedRows || duLieuIdx.len !== persistedRows.length || duLieuIdx.idx.version !== S.version()){
      const cols = TARGET_COLS.map(c => c.key);
      duLieuIdx = { rows: persistedRows, len: persistedRows.length, idx: S.buildIndex(persistedRows.length,
        i => cols.map(k => objColValue(persistedRows[i], k)),
        i => [persistedRows[i].maHang, persistedRows[i].tenHang, persistedRows[i].size]) };
    }
    const hit = S.matcher(duLieuIdx.idx, duLieuQ);
    return hit ? persistedRows.filter((r, i) => hit(i)) : persistedRows;
  }
  const searchDLInput = document.getElementById('searchDL');
  if(searchDLInput){
    const run = KLAType.debounce(() => { duLieuPage = 1; renderDuLieuPage(); }, 160);
    searchDLInput.addEventListener('input', () => { duLieuQ = searchDLInput.value; run(); });
    if(window.KLSmartSearch){
      window.KLSmartSearch.loadDict();
      window.KLSmartSearch.onDictReady(() => { duLieuIdx = null; if(duLieuQ.trim() && persistedRows.length) renderDuLieuPage(); });
    }
  }

  function refreshDuLieuTab(){
    if(!persistedRows || !persistedRows.length){
      toolbarLabelDL.textContent = 'Chưa có dữ liệu';
      tableScrollDL.innerHTML = '<div class="empty-table">Chưa có dữ liệu — hãy nạp file ở "Load data" rồi bấm "Nạp Dữ Liệu".</div>';
      rowCountDL.textContent = '';
      summaryStatsDL.innerHTML = '';
      paginationDL.style.display = 'none';
      exportBtnDL.style.display = 'none';
      clearBtnDL.style.display = 'none';
      return;
    }
    duLieuPage = 1;
    duLieuIdx = null; // dữ liệu có thể đã đổi (nạp lại / trừ phiếu xuất) → dựng lại chỉ mục tìm kiếm
    duLieuSearchState.searchFilters = {};
    duLieuSearchState.lastHeaderKey = null;
    resetTableCheckboxFilters('duLieu');
    toolbarLabelDL.textContent = 'Dữ liệu đã lưu (dùng chung cho mọi tab)' + (m2DataDateLabel() ? ' · Số liệu ngày ' + m2DataDateLabel() : '');
    exportBtnDL.style.display = 'inline-block';
    clearBtnDL.style.display = 'inline-block';
    renderDuLieuPage();
  }

  function updateDuLieuSummary(rows){
    const data = rows || persistedRows;
    if(!data.length){
      summaryStatsDL.innerHTML = `<span class="stat">Số dòng<b>0</b></span>`;
      return;
    }
    let soKienSum = 0, trongLuongSum = 0;
    data.forEach(r=>{
      soKienSum += toNumber(r.soKien);
      trongLuongSum += toNumber(r.trongLuong);
    });
    summaryStatsDL.innerHTML =
      `<span class="stat">Số dòng<b>${formatNumber(data.length)}</b></span>` +
      `<span class="stat">Tồn kiện<b>${formatNumber(soKienSum)}</b></span>` +
      `<span class="stat">Tồn KG<b>${formatWeight(trongLuongSum)}</b></span>`;
  }

  function renderDuLieuPage(){
    const base = duLieuSmartRows();
    const filtered = applyColumnSearch(base, TARGET_COLS, duLieuSearchState.searchFilters, 'duLieu');
    updateDuLieuSummary(filtered);
    const total = filtered.length;
    const totalPages = Math.max(1, Math.ceil(total / duLieuPageSize));
    if(duLieuPage > totalPages) duLieuPage = totalPages;
    if(duLieuPage < 1) duLieuPage = 1;

    const start = (duLieuPage - 1) * duLieuPageSize;
    // Giữ lại các dòng GỐC (object) của trang hiện tại song song với mảng hiển thị -> dùng để
    // biết dòng nào đã được trừ theo phiếu xuất (_pxDaXuat) mà tô đỏ, không đổi thứ tự/']số lượng.
    const pageOriginalRows = filtered.slice(start, start + duLieuPageSize);
    const pageRows = pageOriginalRows.map(r => rowToDisplayArray(r));

    if(!persistedRows.length){
      tableScrollDL.innerHTML = '<div class="empty-table">Chưa có dữ liệu — hãy nạp file ở "Load data" rồi bấm "Nạp Dữ Liệu".</div>';
    } else {
      renderGenericTable(tableScrollDL, TARGET_COLS, pageRows, true, {
        state: duLieuSearchState,
        allRows: base, // lọc theo cột chỉ gợi ý giá trị của các dòng khớp ô tìm chung (phân tầng)
        tableId: 'duLieu',
        onSearchInput: (idx, val) => {
          duLieuSearchState.searchFilters[idx] = val;
          duLieuPage = 1;
          renderDuLieuPage();
        },
        onFilterApply: () => {
          duLieuPage = 1;
          renderDuLieuPage();
        },
        // Tô đỏ (class "row-daxuat") cho dòng đã được trừ tồn khi tạo phiếu xuất thành công.
        rowClassFn: (ri) => (pageOriginalRows[ri] && pageOriginalRows[ri]._pxDaXuat) ? 'row-daxuat' : ''
      });
    }

    if(total > 0){
      const from = start + 1;
      const to = Math.min(start + duLieuPageSize, total);
      rowCountDL.textContent = `${formatNumber(total)} dòng (${from}–${to})`;
    } else {
      rowCountDL.textContent = '';
    }

    pageIndicatorDL.textContent = 'Trang ' + duLieuPage + '/' + totalPages;
    prevPageBtnDL.disabled = duLieuPage <= 1;
    nextPageBtnDL.disabled = duLieuPage >= totalPages;
    paginationDL.style.display = total > 0 ? 'flex' : 'none';
  }

  pageSizeSelectDL.addEventListener('change', ()=>{
    duLieuPageSize = parseInt(pageSizeSelectDL.value, 10);
    duLieuPage = 1;
    renderDuLieuPage();
  });
  prevPageBtnDL.addEventListener('click', ()=>{ duLieuPage--; renderDuLieuPage(); });
  nextPageBtnDL.addEventListener('click', ()=>{ duLieuPage++; renderDuLieuPage(); });

  exportBtnDL.addEventListener('click', ()=>{
    if(!persistedRows.length) return;
    const data = persistedRows.map(r => rowToExportObj(r));
    const ws = XLSX.utils.json_to_sheet(data);
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, 'Du lieu');
    XLSX.writeFile(wb, 'du-lieu.xlsx');
  });

  // ===== Nút "Xóa bảng dữ liệu" — xóa toàn bộ persistedRows (bản dữ liệu lâu dài dùng chung
  // cho mọi tab). Cùng cơ chế reset như nút "Nạp Dữ Liệu", nhưng theo chiều ngược lại: làm rỗng
  // persistedRows rồi làm mới TẤT CẢ các tab phụ thuộc để chúng không còn hiển thị dữ liệu cũ. =====
  clearBtnDL.addEventListener('click', async ()=>{
    if(!persistedRows.length) return;
    const ok = await showConfirmDialog({
      title: 'Xác nhận xóa bảng dữ liệu',
      message: `Xóa toàn bộ ${formatNumber(persistedRows.length)} dòng dữ liệu đã lưu ở menu "Dữ Liệu"?\nMọi tab khác (Phiếu Xuất, Ecuador, Ấn Độ, Gia Công, Giám Sát, Nguyên Liệu, HLSO, Đặc Tính, Báo Cáo, Đối Soát) sẽ mất dữ liệu theo. Hành động này không thể hoàn tác.`,
      confirmText: 'Xóa toàn bộ',
      danger: true
    });
    if(!ok) return;
    persistedRows = [];
    savePersistedRows();
    m2Meta = { importedAt: null, dataDate: '' };
    saveDuLieuMeta();
    renderM2DataDate();
    refreshDuLieuTab();
    refreshPhieuXuatTab();
    refreshEcuadorTab();
    refreshAnDoTab();
    refreshTGCTab();
    refreshTGSTab();
    refreshNguyenLieuTab();
    refreshHLSOTab();
    refreshDacTinhTab();
    refreshBaoCaoTab();
    refreshDoiSoatTab();
    setStatus('Đã xóa toàn bộ dữ liệu ở menu "Dữ Liệu".', 'ok');
  });

  // ===== Nút "Nạp Dữ Liệu" (ở tab Load data) — chép filteredRows hiện tại vào persistedRows,
  // lưu lâu dài, rồi làm mới TẤT CẢ các tab phụ thuộc (vì chúng đều đọc từ persistedRows). =====
  const loadDuLieuBtn = document.getElementById('loadDuLieuBtn');
  loadDuLieuBtn.addEventListener('click', async ()=>{
    if(!filteredRows || !filteredRows.length){
      setStatus('Chưa có "Dữ liệu đã lọc" ở tab Load data để nạp — hãy chọn file khớp mẫu trước.', 'err');
      return;
    }
    const pxActiveN = pxPhieuList.filter(pxIsActive).length;
    // PC 3.12 — NẠP EXCEL AN TOÀN: xem trước (hợp lệ / cần kiểm tra / lỗi) + so với dữ liệu đang có + file đã nạp chưa; chụp bản hoàn tác trước khi thay.
    let ticket = null;
    if(window.NapExcel && window.NapExcelCore){
      const iso = (()=>{ const d = new Date(), p = n=>String(n).padStart(2,'0'); return d.getFullYear()+'-'+p(d.getMonth()+1)+'-'+p(d.getDate()); })();
      const chk = NapExcelCore.checkM2(filteredRows, { today: iso });
      if(m2ReadInfo) chk.infos.push(m2ReadInfo);
      const notes = [];
      if(pxActiveN) notes.push(pxActiveN + ' phiếu xuất đang trừ tồn theo số liệu cũ sẽ bị KHÓA (chỉ còn xem / xuất Excel / xóa), không trừ vào số liệu mới. Hoàn tác đợt nạp KHÔNG tự mở khóa lại các phiếu này.');
      if(m2PendingDate === null || m2PendingDate === '') chk.warns.push('Chưa có “Ngày số liệu” cho file này — nhập ở ô Ngày số liệu để báo cáo / điện thoại hiện đúng ngày.');
      const cur = persistedRows.length ? (()=>{ const it = new Set(); let kg = 0; persistedRows.forEach(r=>{ const m = String(r.maHang||'').trim(); if(m) it.add(m); const n = Number(r.trongLuong); if(!isNaN(n)) kg += n; }); return { rows: persistedRows.length, items: it.size, kg, date: m2Meta.dataDate || '' }; })() : null;
      chk.infos.push('Thay thế toàn bộ menu “Dữ Liệu”; mọi tab khác (Phiếu Xuất, Ecuador, Ấn Độ, Gia Công, Giám Sát, Nguyên Liệu, HLSO, Đặc Tính, Báo Cáo, Đối Soát) cập nhật theo số mới.');
      ticket = await NapExcel.review({ module: DB_KEY, title: 'Tồn kho gửi', file: { name: (m2Src && m2Src.name) || fName.textContent || 'file', size: (m2Src && m2Src.size) || 0 }, buf: m2Src && m2Src.buf, sheetName: sheetSelect.value, sheetCount: workbook ? workbook.SheetNames.length : 0, dataDate: m2PendingDate !== null ? m2PendingDate : '', check: chk, current: cur, sheets: ['DuLieuChinh', DULIEU_META_DB_SHEET], notes, confirmText: 'Nạp dữ liệu' });
      if(!ticket) return;
    } else {
      const ok = await showConfirmDialog({
        title: 'Xác nhận nạp dữ liệu',
        message: `Chép ${filteredRows.length} dòng hiện tại vào menu "Dữ Liệu"?\nDữ liệu cũ đã lưu ở đó (nếu có) sẽ bị THAY THẾ hoàn toàn, và mọi tab khác (Phiếu Xuất, Ecuador, Ấn Độ, Gia Công, Giám Sát, Nguyên Liệu, HLSO, Đặc Tính, Báo Cáo, Đối Soát) sẽ cập nhật theo dữ liệu mới này.` +
          (pxActiveN ? `\n\nLƯU Ý: ${pxActiveN} phiếu xuất đang trừ tồn theo số liệu cũ sẽ bị KHÓA (chỉ còn xem / xuất Excel / xóa), không trừ vào số liệu mới.` : ''),
        confirmText: 'Nạp dữ liệu',
        danger: false
      });
      if(!ok) return;
    }
    persistedRows = filteredRows.slice(); // bản sao độc lập, không dùng chung tham chiếu với filteredRows
    const savedRowsP = savePersistedRows();
    // Ghi mốc "lúc nạp file" + "Ngày số liệu" của file vừa nạp — CHỈ ở bước nạp thay thế toàn bộ này.
    m2Meta = { importedAt: Date.now(), dataDate: m2PendingDate !== null ? m2PendingDate : '', napId: ticket && ticket.id ? ticket.id : '' };
    m2PendingDate = null;
    const savedMetaP = saveDuLieuMeta();
    renderM2DataDate();
    refreshDuLieuTab();
    refreshPhieuXuatTab();
    refreshEcuadorTab();
    refreshAnDoTab();
    refreshTGCTab();
    refreshTGSTab();
    refreshNguyenLieuTab();
    refreshHLSOTab();
    refreshDacTinhTab();
    refreshBaoCaoTab();
    refreshDoiSoatTab();
    setStatus(`Đã nạp ${persistedRows.length} dòng vào menu "Dữ Liệu".`, 'ok');
    // PC 3.72: báo cho Module 1 (Tồn kho An An › Báo Cáo › Kho Gửi) cập nhật số mới — sau khi ghi database xong
    Promise.all([savedRowsP, savedMetaP]).then(()=>{
      try{ const bc = new BroadcastChannel('kl-m2-changed'); bc.postMessage({ t: Date.now() }); setTimeout(()=>{ try{ bc.close(); }catch(e){} }, 500); }catch(e){}
      try{ localStorage.setItem('kl:m2-changed', String(Date.now())); }catch(e){}
      try{ if(window.parent && window.parent !== window) window.parent.postMessage({ type: 'klg-data-changed' }, '*'); }catch(e){}
    }).catch(()=>{});
    if(ticket && window.NapExcel){ // ghi nhật ký SAU khi dữ liệu đã ghi xong
      const it = new Set(); let kg = 0; persistedRows.forEach(r=>{ const m = String(r.maHang||'').trim(); if(m) it.add(m); const n = Number(r.trongLuong); if(!isNaN(n)) kg += n; });
      Promise.all([savedRowsP, savedMetaP]).then(()=> NapExcel.commit(ticket, { rows: persistedRows.length, items: it.size, kg, sheet: sheetSelect.value })).catch(()=>{});
    }
  });

  // PC 3.12 — nút "⟲ Lịch sử nạp": xem các đợt nạp + HOÀN TÁC đợt mới nhất (sheet DuLieuChinh + DuLieuMeta về như trước khi nạp). Bộ đệm localStorage phải xóa để không hiện lại số cũ.
  window.addEventListener('load', ()=>{ try{ if(window.NapExcel) NapExcel.mountButton({ module: DB_KEY, title: 'Tồn kho gửi', sheets: ['DuLieuChinh', DULIEU_META_DB_SHEET], onUndone: ()=>{ try{ localStorage.removeItem(DULIEU_STORAGE_KEY); localStorage.removeItem(DULIEU_META_LS_KEY); }catch(e){} try{ if(window.parent && window.parent !== window) window.parent.postMessage({ type:'kla-loaddata-changed' }, '*'); }catch(e){} location.reload(); } }); }catch(e){ console.error(e); } });

  const TARGET_COLS = [
    { key:'maHang',      label:'Mã hàng' },
    { key:'tenHang',     label:'Tên hàng' },
    { key:'dacTinh',     label:'Đặc tính' },
    { key:'soLo',        label:'Số lô' },
    { key:'size',        label:'Size' },
    { key:'quyCach',     label:'Quy cách' },
    { key:'ngayNhapKho', label:'Ngày nhập kho' },
    { key:'phieuNhap',   label:'Phiếu nhập/Voucher' },
    { key:'soKien',      label:'Số kiện' },
    { key:'trongLuong',  label:'Trọng Lượng (KG)' },
  ];

  // ============================================================
  // ĐIỀU KIỆN LỌC TỰ THÊM — cho phép người dùng tự thêm 1 điều kiện phụ đơn giản (cột + toán tử
  // + giá trị) qua nút "+ Thêm điều kiện" ở mỗi tab "Phân Loại", KHÔNG cần sửa code, áp dụng CỘNG
  // DỒN (AND) với điều kiện lọc gốc của tab (isNguyenLieuSize/isHLSOLot/...) đã viết sẵn trong
  // code — không đụng/xóa được điều kiện gốc. Mỗi tab 1 mảng điều kiện riêng, khoá theo đúng
  // data-plsub của tab đó ('nl','5','4','3','ad','2'). Đồng bộ nguyên xi cơ chế đã dùng ở module
  // "Tồn Kho An An" (Module 1) — xem chú thích đầy đủ ở file js/ton-kho-an-an.js.
  // Lưu bền vào database Excel của module (sheet riêng "DieuKienTuThem", 1 ô JSON), dự phòng
  // localStorage khi không chạy trong Electron (window.dbAPI không tồn tại).
  // ============================================================
  const CUSTOM_FILTER_TABS = ['nl','5','4','3','ad','2'];
  const CUSTOM_FILTER_STORAGE_KEY = 'coldstock:customFilters_v1_m2'; // chỉ dùng cho phương án dự phòng localStorage
  const CUSTOM_FILTER_DB_SHEET = 'DieuKienTuThem';
  // PC 1.25: bộ toán tử cụ thể hơn — GIỐNG Tồn kho An An (js/ton-kho-an-an.js): bắt đầu / kết thúc bằng, thuộc / không
  // thuộc danh sách (ngăn bằng ; hoặc |), ký tự vị trí từ–đến thuộc / không thuộc danh sách, ≥ / ≤. Điều kiện cũ lưu
  // trước PC 1.25 (eq, neq, contains, notcontains, gt, lt, empty, notempty) đọc bình thường.
  const CUSTOM_FILTER_OPS = [
    ['eq','bằng'], ['neq','khác'], ['contains','chứa'], ['notcontains','không chứa'],
    ['startswith','bắt đầu bằng'], ['endswith','kết thúc bằng'],
    ['in','thuộc danh sách'], ['notin','không thuộc danh sách'],
    ['pos_in','ký tự vị trí … thuộc'], ['pos_notin','ký tự vị trí … không thuộc'],
    ['gt','lớn hơn'], ['gte','lớn hơn hoặc bằng'], ['lt','nhỏ hơn'], ['lte','nhỏ hơn hoặc bằng'],
    ['empty','để trống'], ['notempty','không để trống'],
  ];
  // Danh sách giá trị ngăn bằng ; hoặc | hoặc xuống dòng (VD "ECUADOR; AD")
  function customFilterList(v){ return String(v ?? '').split(/[;|\n]/).map(x=>x.trim()).filter(Boolean); }
  function customFilterOpIsPos(op){ return op==='pos_in' || op==='pos_notin'; }
  function customFilterOpLabel(op){ return (CUSTOM_FILTER_OPS.find(o=>o[0]===op)||[])[1] || op; }
  function customFilterOpNeedsValue(op){ return op!=='empty' && op!=='notempty'; }
  // Mô tả 1 điều kiện tự thêm bằng chữ (dùng ở tab "Điều kiện lọc")
  function customFilterDesc(cf){
    const op = cf.op;
    if(customFilterOpIsPos(op)){
      const f = parseInt(cf.from,10)||1, t = parseInt(cf.to,10)||f;
      return `${cf.colLabel}: ký tự vị trí ${f===t?f:f+'–'+t} ${op==='pos_in'?'thuộc':'không thuộc'} [${customFilterList(cf.value).join(', ')}]`;
    }
    if(op==='in' || op==='notin') return `${cf.colLabel} ${customFilterOpLabel(op)} [${customFilterList(cf.value).join(', ')}]`;
    return `${cf.colLabel} ${customFilterOpLabel(op)}${customFilterOpNeedsValue(op)?` "${cf.value}"`:''}`;
  }
  // Đọc số kiểu Việt / kiểu Mỹ (1.234,5 và 1,234.5) — NaN nếu không phải số (cùng quy tắc parseLooseNumber của Tồn kho An An)
  function parseLooseNumber(v){
    let s = String(v ?? '').trim();
    if(s === '') return NaN;
    s = s.replace(/[\u00A0\s]/g, '');
    const hasComma = s.includes(','), hasDot = s.includes('.');
    if(hasComma && hasDot){
      if(s.lastIndexOf(',') > s.lastIndexOf('.')) s = s.replace(/\./g, '').replace(',', '.');
      else s = s.replace(/,/g, '');
    } else if(hasComma){
      s = /^-?\d+,\d{1,2}$/.test(s) ? s.replace(',', '.') : s.replace(/,/g, '');
    }
    const m = s.match(/^-?\d+(\.\d+)?/);
    return m ? parseFloat(m[0]) : NaN;
  }
  // So khớp "bắt đầu / kết thúc bằng": không phân biệt hoa thường / dấu / khoảng trắng
  function dkNoSpaceNorm(v){ return normalize(v).replace(/đ/g,'d').replace(/\s+/g,''); }
  let customFiltersStore = {};
  // Nạp NGAY LẬP TỨC (đồng bộ) từ localStorage lúc khai báo — đúng cơ chế 2 pha đã dùng cho các
  // nguồn dữ liệu khác ở dưới (persistedRows/pxPhieuList/…): hiện dữ liệu tạm ngay, không phải
  // chờ IPC; nếu đang chạy trong Electron, initDbHydration() bên dưới sẽ đọc lại từ file Excel
  // database thật và GHI ĐÈ lên bản tạm này.
  try{
    const rawCfEarly = localStorage.getItem(CUSTOM_FILTER_STORAGE_KEY);
    if(rawCfEarly){
      const parsedEarly = JSON.parse(rawCfEarly);
      CUSTOM_FILTER_TABS.forEach(t=>{ customFiltersStore[t] = Array.isArray(parsedEarly[t]) ? parsedEarly[t] : []; });
    }
  }catch(e){ /* bỏ qua, dùng mặc định rỗng */ }
  function customFiltersFor(tabId){
    if(!customFiltersStore[tabId]) customFiltersStore[tabId] = [];
    return customFiltersStore[tabId];
  }
  function buildCustomFiltersPayload(){
    const out = {};
    CUSTOM_FILTER_TABS.forEach(t=>{ out[t] = customFiltersFor(t); });
    return out;
  }
  // Ghi ĐÈ nội dung từng mảng (length=0 rồi push lại) thay vì gán lại tham chiếu mới — để nơi
  // khác lỡ đang giữ tham chiếu cũ vẫn tự động thấy dữ liệu mới nhất.
  function applyCustomFiltersPayload(d){
    if(!d || typeof d !== 'object') return;
    CUSTOM_FILTER_TABS.forEach(t=>{
      const arr = customFiltersFor(t);
      arr.length = 0;
      if(Array.isArray(d[t])) d[t].forEach(cf=>arr.push(cf));
    });
  }
  async function persistCustomFilters(){
    if(!m2CanWrite) return;
    const payload = buildCustomFiltersPayload();
    if(hasDbAPI()){
      try{
        const res = await window.dbAPI.writeSheet(DB_KEY, CUSTOM_FILTER_DB_SHEET, [{ data: '__JSON__' + JSON.stringify(payload) }]);
        if(res && res.ok) return; // ghi database Excel thành công — xong, không cần fallback
        console.error('Lỗi lưu điều kiện lọc tự thêm vào database Excel:', res && res.error);
      }catch(e){ console.error('Lỗi lưu điều kiện lọc tự thêm vào database Excel:', e); }
    }
    try{ localStorage.setItem(CUSTOM_FILTER_STORAGE_KEY, JSON.stringify(payload)); }
    catch(e){ console.error('Lỗi lưu điều kiện lọc tự thêm (dự phòng localStorage):', e); }
  }
  async function restoreCustomFilters(){
    if(hasDbAPI()){
      try{
        const res = await window.dbAPI.readSheet(DB_KEY, CUSTOM_FILTER_DB_SHEET);
        if(res && res.ok && Array.isArray(res.data) && res.data.length){
          const cell = res.data[0] && res.data[0].data;
          if(typeof cell === 'string' && cell.startsWith('__JSON__')){
            applyCustomFiltersPayload(JSON.parse(cell.slice('__JSON__'.length)));
            return;
          }
        }
        if(res && res.ok) return; // đọc thành công nhưng chưa từng lưu điều kiện nào — không cần fallback
      }catch(e){ /* lỗi đọc (VD file đang bị khoá) — thử phương án dự phòng bên dưới */ }
    }
    try{
      const raw = localStorage.getItem(CUSTOM_FILTER_STORAGE_KEY);
      if(raw) applyCustomFiltersPayload(JSON.parse(raw));
    }catch(e){ /* chưa có dữ liệu lưu trước đó, hoặc dữ liệu lỗi — bỏ qua, dùng mặc định rỗng */ }
  }
  // So khớp 1 điều kiện tự thêm với 1 ô dữ liệu. normalize() (khai báo ngay phía trên) đã bỏ dấu
  // tiếng Việt + hạ chữ thường sẵn nên dùng chung cho cả "bằng/khác" LẪN "chứa/không chứa" (khác
  // Module 1 vốn tách riêng 2 hàm chuẩn hoá). Với "lớn hơn/nhỏ hơn": nếu ô dữ liệu hoặc giá trị
  // nhập không phải số hợp lệ thì coi như KHÔNG khớp (an toàn — không loại/giữ nhầm dòng vì lỗi
  // định dạng số).
  function testCustomFilter(cellVal, cf){
    switch(cf.op){
      case 'eq': return normalize(cellVal) === normalize(cf.value);
      case 'neq': return normalize(cellVal) !== normalize(cf.value);
      case 'contains': return normalize(cellVal).includes(normalize(cf.value));
      case 'notcontains': return !normalize(cellVal).includes(normalize(cf.value));
      // PC 1.25: so sánh số bằng parseLooseNumber (ô không phải số → KHÔNG khớp; trước đây toNumber coi ô chữ / ô trống là 0)
      case 'gt': { const a = parseLooseNumber(cellVal), b = parseLooseNumber(cf.value); return !isNaN(a) && !isNaN(b) && a > b; }
      case 'lt': { const a = parseLooseNumber(cellVal), b = parseLooseNumber(cf.value); return !isNaN(a) && !isNaN(b) && a < b; }
      case 'gte': { const a = parseLooseNumber(cellVal), b = parseLooseNumber(cf.value); return !isNaN(a) && !isNaN(b) && a >= b; }
      case 'lte': { const a = parseLooseNumber(cellVal), b = parseLooseNumber(cf.value); return !isNaN(a) && !isNaN(b) && a <= b; }
      case 'empty': return String(cellVal ?? '').trim() === '';
      case 'notempty': return String(cellVal ?? '').trim() !== '';
      case 'startswith': { const q = dkNoSpaceNorm(cf.value); return q !== '' && dkNoSpaceNorm(cellVal).startsWith(q); }
      case 'endswith': { const q = dkNoSpaceNorm(cf.value); return q !== '' && dkNoSpaceNorm(cellVal).endsWith(q); }
      case 'in': return customFilterList(cf.value).some(v => normalize(cellVal) === normalize(v));
      case 'notin': return !customFilterList(cf.value).some(v => normalize(cellVal) === normalize(v));
      case 'pos_in': case 'pos_notin': {
        const from = Math.max(1, parseInt(cf.from, 10) || 1), to = Math.max(from, parseInt(cf.to, 10) || from);
        const seg = String(cellVal ?? '').trim().substring(from - 1, to).toUpperCase();
        const hit = customFilterList(cf.value).some(v => v.trim().toUpperCase() === seg);
        return cf.op === 'pos_in' ? hit : !hit;
      }
      default: return true;
    }
  }
  // Áp TOÀN BỘ điều kiện tự thêm của 1 tab lên 1 mảng dòng — dùng TARGET_COLS để map "tên cột"
  // hiển thị (label) sang đúng field (key) trong object dòng (r.maHang, r.dacTinh…).
  function applyCustomFiltersToRows(rows, tabId){
    const cfs = customFiltersFor(tabId);
    if(!cfs.length) return rows;
    return rows.filter(r => cfs.every(cf=>{
      const col = TARGET_COLS.find(c=>c.label===cf.colLabel);
      if(!col) return true; // cột không tìm thấy (hiếm khi xảy ra) — bỏ qua an toàn, không loại hết dữ liệu
      return testCustomFilter(r[col.key], cf);
    }));
  }
  // ============================================================
  // TAB "ĐIỀU KIỆN LỌC" (PC 1.25) — trang riêng trong menu con Phân Loại, đồng bộ với tab cùng tên ở Tồn kho An An.
  // Với MỖI module trong 6 module Phân Loại (Nguyên Liệu · HLSO · Giám Sát · Gia Công · Ấn Độ · Ecuador) liệt kê đúng
  // từng bước lọc theo thứ tự áp dụng (điều kiện chính → phụ → điều kiện tự thêm) kèm số dòng CÒN LẠI / BỊ LOẠI ở từng bước.
  // Các bước "gốc" (Chính / Phụ) dùng CHÍNH các hàm lọc của tab (isEcuadorLot, isAnDoLot, isTGCLot, isTGSLot,
  // isNguyenLieuSize) nên không lệch với bảng — nếu đổi quy tắc ở các hàm đó thì cập nhật cả danh sách DKLOC_MODS bên dưới
  // (kết quả cuối của dkLocFunnel PHẢI bằng đúng số dòng của tab; đã có phép thử so từng module).
  // Module 2 không có "chuyển thủ công giữa các module" và không có ưu tiên theo Khách hàng như Module 1.
  // ============================================================
  const DKLOC_MODS = [
    { id:'nl', label:'Tôm Nguyên Liệu', color:'#F59E0B', refresh:()=>refreshNguyenLieuTab(), steps:[
      { kind:'main',  label:'Size chứa "NL" ở bất kỳ vị trí nào', col:'Size', keep:r=>isNguyenLieuSize(r.size) },
    ]},
    { id:'5', label:'Tôm HLSO', color:'#22D3EE', refresh:()=>refreshHLSOTab(), steps:[
      { kind:'main',  label:'Loại dòng có Số lô chứa "ECUADOR"', col:'Số lô', keep:r=>!isEcuadorLot(r.soLo) },
      { kind:'extra', label:'Loại dòng có Số lô chứa "AD"', col:'Số lô', keep:r=>!isAnDoLot(r.soLo) },
      { kind:'extra', label:'Loại dòng có Số lô chứa "TGC"', col:'Số lô', keep:r=>!isTGCLot(r.soLo) },
      { kind:'extra', label:'Loại dòng có Số lô chứa "TGS"', col:'Số lô', keep:r=>!isTGSLot(r.soLo) },
      { kind:'extra', label:'Loại dòng có Size chứa "NL"', col:'Size', keep:r=>!isNguyenLieuSize(r.size) },
    ]},
    { id:'4', label:'Tôm Giám Sát', color:'#94A3B8', refresh:()=>refreshTGSTab(), steps:[
      { kind:'main',  label:'Số lô chứa "TGS" ở bất kỳ vị trí nào', col:'Số lô', keep:r=>isTGSLot(r.soLo) },
    ]},
    { id:'3', label:'Tôm Gia Công', color:'#38BDF8', refresh:()=>refreshTGCTab(), steps:[
      { kind:'main',  label:'Số lô chứa "TGC" ở bất kỳ vị trí nào', col:'Số lô', keep:r=>isTGCLot(r.soLo) },
    ]},
    { id:'ad', label:'Tôm Ấn Độ', color:'#F472B6', refresh:()=>refreshAnDoTab(), steps:[
      { kind:'main',  label:'Số lô chứa "AD" ở bất kỳ vị trí nào', col:'Số lô', keep:r=>isAnDoLot(r.soLo) },
      { kind:'extra', label:'Loại dòng đã thuộc Tôm Ecuador (Số lô chứa "ECUADOR")', col:'Số lô', keep:r=>!isEcuadorLot(r.soLo) },
      { kind:'extra', label:'Loại dòng đã thuộc Tôm Gia Công (Số lô chứa "TGC")', col:'Số lô', keep:r=>!isTGCLot(r.soLo) },
      { kind:'extra', label:'Loại dòng đã thuộc Tôm Giám Sát (Số lô chứa "TGS")', col:'Số lô', keep:r=>!isTGSLot(r.soLo) },
      { kind:'extra', label:'Loại dòng đã thuộc Tôm Nguyên Liệu (Size chứa "NL")', col:'Size', keep:r=>!isNguyenLieuSize(r.size) },
    ]},
    { id:'2', label:'Tôm Ecuador', color:'#34D399', refresh:()=>refreshEcuadorTab(), steps:[
      { kind:'main',  label:'Số lô chứa "ECUADOR" ở bất kỳ vị trí nào', col:'Số lô', keep:r=>isEcuadorLot(r.soLo) },
    ]},
  ];
  const DKLOC_BY_ID = {};
  DKLOC_MODS.forEach(m => { DKLOC_BY_ID[m.id] = m; });
  let dkLocMod = 'nl';                                  // module đang xem ở tab "Điều kiện lọc"
  let dkLast = { col:'', op:'eq', from:2, to:4 };       // nhớ cột + toán tử vừa dùng ở form thêm điều kiện

  const DK_IC = {
    search: '<svg class="dk-ic" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="7"/><path d="M21 21l-4.3-4.3"/></svg>',
    lock:   '<svg class="dk-ic" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><rect x="5" y="11" width="14" height="9" rx="2"/><path d="M8 11V8a4 4 0 0 1 8 0v3"/></svg>',
    check:  '<svg class="dk-ic" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12l5 5 9-10"/></svg>',
    chevR:  '<svg class="dk-ic" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 6l6 6-6 6"/></svg>',
  };
  const DK_KIND_LABEL = { main:'Chính', extra:'Phụ', custom:'Tự thêm' };

  function dkLocColByLabel(label){ return TARGET_COLS.find(c => c.label === label) || null; }
  function dkLocCountConditions(modId){
    const mod = DKLOC_BY_ID[modId];
    return (mod ? mod.steps.length : 0) + customFiltersFor(modId).length;
  }
  // Điều kiện tự thêm KHÔNG áp dụng được (không tìm thấy cột) — bị bỏ qua khi lọc nên cần báo
  function dkLocWarnings(modId){
    const out = [];
    customFiltersFor(modId).forEach(cf => { if(!dkLocColByLabel(cf.colLabel)) out.push(`Điều kiện tự thêm: không thấy cột "${cf.colLabel}"`); });
    return out;
  }
  // Chạy từng bước lọc trên toàn bộ dữ liệu đã nạp ("Dữ Liệu"), ghi lại số dòng còn lại sau mỗi bước. Trả về cả mảng dòng
  // kết quả (rows) — đúng bằng dòng của tab sau khi áp điều kiện tự thêm.
  function dkLocFunnel(modId){
    const mod = DKLOC_BY_ID[modId];
    const total = persistedRows.length;
    const steps = [];
    let rows = persistedRows, prev = total, extraNo = 0;
    const add = (kind, label, now, extra)=>{
      steps.push(Object.assign({ kind, label, remaining: now.length, removed: prev - now.length }, extra || {}));
      prev = now.length;
    };
    mod.steps.forEach(sp=>{
      rows = rows.filter(sp.keep);
      add(sp.kind, sp.label, rows, { col: sp.col, locked: true, num: sp.kind === 'extra' ? ++extraNo : 0 });
    });
    customFiltersFor(modId).forEach(cf=>{
      const col = dkLocColByLabel(cf.colLabel);
      if(col) rows = rows.filter(r => testCustomFilter(r[col.key], cf));
      add('custom', customFilterDesc(cf), rows, { col: cf.colLabel, warn: !col, cfId: cf.id });
    });
    return { total, steps, final: rows.length, rows };
  }

  // Nút nhỏ ở đầu mỗi tab Phân Loại: nhảy sang tab "Điều kiện lọc" đúng module đó (thay cho khối "+ Thêm điều kiện" cũ)
  function renderCustomFilterArea(tabId, containerId){
    const container = document.getElementById(containerId);
    if(!container) return;
    const n = dkLocCountConditions(tabId);
    const w = dkLocWarnings(tabId).length;
    container.innerHTML = `<button type="button" class="dk-goto" title="Xem từng bước lọc, số dòng bị loại, thêm / xóa điều kiện tự thêm">
      ${DK_IC.search}<span>Điều kiện lọc (${n})</span>${w?`<span class="dk-goto-warn" title="${w} điều kiện không áp dụng được">⚠ ${w}</span>`:''}${DK_IC.chevR}
    </button>`;
    const btn = container.querySelector('.dk-goto');
    if(btn) btn.addEventListener('click', ()=>{
      dkLocMod = tabId;
      const chip = document.querySelector('.pl-subtab-btn[data-plsub="dk"]');
      if(chip) chip.click();
    });
  }

  const DK_OP_HINT = {
    in: 'Nhiều giá trị ngăn bằng dấu ; hoặc | — dòng khớp nếu ô bằng MỘT trong các giá trị (VD: ECUADOR; AD).',
    notin: 'Nhiều giá trị ngăn bằng dấu ; hoặc | — dòng bị loại nếu ô bằng một trong các giá trị.',
    pos_in: 'Lấy các ký tự từ vị trí … đến … (tính từ 1) của ô rồi so với danh sách (VD Mã hàng, vị trí 2–4, giá trị RHO; RHL).',
    pos_notin: 'Như trên nhưng LOẠI các dòng có đoạn ký tự đó (VD vị trí 2–4 không thuộc RBR; CBR).',
    startswith: 'Không phân biệt hoa thường / dấu / khoảng trắng.', endswith: 'Không phân biệt hoa thường / dấu / khoảng trắng.',
    contains: 'Không phân biệt hoa thường / dấu.', notcontains: 'Không phân biệt hoa thường / dấu.',
    gt: 'So sánh số (nhận 1.234,5 và 1,234.5).', gte: 'So sánh số (nhận 1.234,5 và 1,234.5).', lt: 'So sánh số.', lte: 'So sánh số.',
  };

  function refreshDkLocTab(){
    const root = document.getElementById('dkLocRoot');
    if(!root) return;
    if(!persistedRows || !persistedRows.length){
      root.innerHTML = `<div class="empty-table">Chưa có dữ liệu — hãy nạp file ở menu "Load data" trước, các bước lọc sẽ hiện tại đây.</div>
        <div><button type="button" class="btn-ghost-sm" id="dkGoLoad">Đi tới Load data</button></div>`;
      const go = document.getElementById('dkGoLoad');
      if(go) go.addEventListener('click', ()=>{ const b = document.querySelector('nav.menu > button[data-tab="1"]'); if(b) b.click(); });
      return;
    }
    if(!DKLOC_BY_ID[dkLocMod]) dkLocMod = DKLOC_MODS[0].id;
    const modId = dkLocMod;
    const mod = DKLOC_BY_ID[modId];
    const fn = dkLocFunnel(modId);
    const warns = dkLocWarnings(modId);
    const fmt = n => formatNumber(n);

    // Hàng chip chọn module (kèm số dòng của từng module)
    const chips = DKLOC_MODS.map(m=>{
      const n = dkLocFunnel(m.id).final;
      const w = dkLocWarnings(m.id).length;
      return `<button type="button" class="dk-mod ${m.id===modId?'active':''}" data-dkmod="${m.id}" style="--tc:${m.color}">
        <span class="dk-dot"></span><span class="dk-mod-name">${escapeHtml(m.label)}</span>
        <span class="dk-mod-n">${fmt(n)}</span>${w?`<span class="dk-mod-warn" title="${w} điều kiện không áp dụng được">⚠</span>`:''}
      </button>`;
    }).join('');

    const extraCount = fn.steps.filter(x => x.kind === 'extra').length;
    const stepRows = fn.steps.map((sp, idx)=>{
      const status = sp.warn
        ? `<span class="dk-st dk-st-warn" title="Không thấy cột — điều kiện này đang bị bỏ qua">⚠ Không áp dụng</span>`
        : (sp.locked ? `<span class="dk-st dk-st-lock">${DK_IC.lock} Cố định</span>` : `<span class="dk-st dk-st-ok">${DK_IC.check} Đang áp dụng</span>`);
      const del = sp.kind === 'custom' && sp.cfId ? `<button type="button" class="custom-filter-del dk-del" data-cf-id="${sp.cfId}" title="Xóa điều kiện này">×</button>` : '';
      const rem = sp.removed;
      const remTxt = rem === 0 ? '<span class="dk-zero">0</span>' : (rem > 0 ? `−${fmt(rem)}` : `+${fmt(-rem)}`);
      return `<tr class="dk-row dk-${sp.kind}">
        <td class="dk-n">${idx+1}</td>
        <td><span class="dk-kind dk-kind-${sp.kind}">${DK_KIND_LABEL[sp.kind]}${sp.num && extraCount > 1 ? ' #'+sp.num : ''}</span></td>
        <td class="dk-cond">${escapeHtml(sp.label)}</td>
        <td>${status}</td>
        <td class="num">${fmt(sp.remaining)}</td>
        <td class="num dk-rem">${remTxt}</td>
        <td class="dk-act">${del}</td>
      </tr>`;
    }).join('');

    const colOpts = TARGET_COLS.map(c=>`<option value="${escapeHtml(c.label)}" ${c.label===dkLast.col?'selected':''}>${escapeHtml(c.label)}</option>`).join('');
    const opOpts = CUSTOM_FILTER_OPS.map(([v,l])=>`<option value="${v}" ${v===dkLast.op?'selected':''}>${l}</option>`).join('');
    const dateLbl = (typeof m2DataDateLabel === 'function') ? m2DataDateLabel() : '';

    root.innerHTML = `
    <div class="dk-wrap" id="dkLocWrap">
      <div class="dk-note">${DK_IC.search} Dữ liệu lấy trực tiếp từ dữ liệu đã nạp ở <b>Load data</b> → <b>Dữ Liệu</b> (${fmt(persistedRows.length)} dòng${dateLbl?`, ngày số liệu <b>${escapeHtml(dateLbl)}</b>`:''}). Điều kiện áp dụng lần lượt từ trên xuống; mỗi dòng cho biết số dòng còn lại và số dòng bị loại ở bước đó.</div>
      <div class="dk-mods">${chips}</div>

      <div class="dk-card">
        <div class="dk-head">
          <div class="dk-title"><span class="dk-dot" style="--tc:${mod.color}"></span>${escapeHtml(mod.label)}</div>
          <div class="dk-sum"><b>${fmt(fn.final)}</b> / ${fmt(fn.total)} dòng dữ liệu đã nạp</div>
          <button type="button" class="btn-ghost-sm" id="dkOpenMod">Xem bảng dữ liệu ${DK_IC.chevR}</button>
        </div>
        ${warns.length ? `<div class="dk-note dk-warn-note">${warns.map(escapeHtml).join(' · ')} — các điều kiện này đang bị bỏ qua.</div>` : ''}
        <div class="dk-tablewrap"><table class="dk-table">
          <thead><tr><th>#</th><th>Loại</th><th>Điều kiện</th><th>Trạng thái</th><th class="num">Còn lại</th><th class="num">Loại bỏ</th><th></th></tr></thead>
          <tbody>
            <tr class="dk-row dk-start"><td class="dk-n">0</td><td><span class="dk-kind">Gốc</span></td><td class="dk-cond">Toàn bộ dữ liệu đã nạp (menu Dữ Liệu)</td><td></td><td class="num">${fmt(fn.total)}</td><td class="num"></td><td></td></tr>
            ${stepRows}
            <tr class="dk-row dk-final"><td></td><td></td><td class="dk-cond"><b>Kết quả của module</b></td><td></td><td class="num"><b>${fmt(fn.final)}</b></td><td class="num">−${fmt(fn.total - fn.final)}</td><td></td></tr>
          </tbody>
        </table></div>
        <div class="dk-hint">${DK_IC.lock} Điều kiện <b>Chính / Phụ</b> đã cố định trong phần mềm (không sửa qua giao diện). <b>Điều kiện tự thêm</b> cộng dồn (VÀ) với chúng, không xóa được các điều kiện gốc.</div>
      </div>

      <div class="dk-card">
        <div class="dk-head"><div class="dk-title">Thêm điều kiện cho ${escapeHtml(mod.label)}</div></div>
        <div class="dk-form" id="dkForm">
          <label>Cột <select id="dkCfCol">${colOpts}</select></label>
          <label>Điều kiện <select id="dkCfOp">${opOpts}</select></label>
          <label id="dkPosBox" style="display:none;">Ký tự từ <input type="number" id="dkCfFrom" min="1" value="${dkLast.from||2}" style="width:58px;"> đến <input type="number" id="dkCfTo" min="1" value="${dkLast.to||4}" style="width:58px;"></label>
          <label id="dkValBox">Giá trị <input type="text" id="dkCfVal" placeholder="Giá trị…" style="min-width:200px;"></label>
          <button type="button" class="btn-primary-sm" id="dkCfAdd">Thêm</button>
        </div>
        <div class="dk-hint" id="dkOpHint"></div>
        <div class="dk-hint dk-preview" id="dkPreview"></div>
      </div>
    </div>`;
    bindDkLocEvents(modId, fn);
  }

  function bindDkLocEvents(modId, fn){
    const mod = DKLOC_BY_ID[modId];
    document.querySelectorAll('#dkLocWrap .dk-mod').forEach(b => b.addEventListener('click', ()=>{ dkLocMod = b.dataset.dkmod; refreshDkLocTab(); }));
    const open = document.getElementById('dkOpenMod');
    if(open) open.addEventListener('click', ()=>{
      const b = document.querySelector(`.pl-subtab-btn[data-plsub="${modId}"]`);
      if(b) b.click();
    });

    const opSel = document.getElementById('dkCfOp');
    const colSel = document.getElementById('dkCfCol');
    const valIn = document.getElementById('dkCfVal');
    const fromIn = document.getElementById('dkCfFrom');
    const toIn = document.getElementById('dkCfTo');
    const posBox = document.getElementById('dkPosBox');
    const valBox = document.getElementById('dkValBox');
    const hint = document.getElementById('dkOpHint');
    const prev = document.getElementById('dkPreview');
    if(!opSel || !colSel) return;

    // Đọc form → đối tượng điều kiện (hoặc lý do lỗi)
    function readForm(){
      const op = opSel.value;
      const cf = { id: 'cf_'+Date.now()+'_'+Math.random().toString(36).slice(2,7), colLabel: colSel.value, op, value: valIn.value.trim() };
      if(!cf.colLabel) return { err: 'Chọn cột cần lọc' };
      if(customFilterOpNeedsValue(op) && !cf.value) return { err: 'Nhập giá trị cần so khớp' };
      if((op==='gt'||op==='lt'||op==='gte'||op==='lte') && isNaN(parseLooseNumber(cf.value))) return { err: 'Giá trị so sánh phải là số' };
      if(customFilterOpIsPos(op)){
        const f = parseInt(fromIn.value,10), t = parseInt(toIn.value,10);
        if(!(f >= 1) || !(t >= f)) return { err: 'Vị trí ký tự không hợp lệ (từ ≥ 1, đến ≥ từ)' };
        cf.from = f; cf.to = t;
        if(!customFilterList(cf.value).length) return { err: 'Nhập ít nhất 1 giá trị' };
      }
      if((op==='in'||op==='notin') && !customFilterList(cf.value).length) return { err: 'Nhập ít nhất 1 giá trị' };
      return { cf };
    }
    // Xem trước: thêm điều kiện này thì module còn bao nhiêu dòng (không ghi gì)
    function refreshPreview(){
      valBox.style.display = customFilterOpNeedsValue(opSel.value) ? '' : 'none';
      posBox.style.display = customFilterOpIsPos(opSel.value) ? '' : 'none';
      hint.textContent = DK_OP_HINT[opSel.value] || '';
      const r = readForm();
      if(r.err){ prev.textContent = ''; return; }
      const col = dkLocColByLabel(r.cf.colLabel);
      if(!col){ prev.textContent = ''; return; }
      const cur = fn.rows;
      const kept = cur.filter(row => testCustomFilter(row[col.key], r.cf)).length;
      prev.innerHTML = `Xem trước: thêm điều kiện này thì module còn <b>${formatNumber(kept)}</b> / ${formatNumber(cur.length)} dòng (loại ${formatNumber(cur.length - kept)}).`;
    }
    [opSel, colSel].forEach(el => el.addEventListener('change', refreshPreview));
    const previewSoon = (typeof KLAType !== 'undefined' && KLAType.debounce) ? KLAType.debounce(refreshPreview, 250) : refreshPreview;
    [valIn, fromIn, toIn].forEach(el => el && el.addEventListener('input', previewSoon));
    refreshPreview();

    const addBtn = document.getElementById('dkCfAdd');
    const doAdd = ()=>{
      if(m2DtDenied()) return; // PC 7.54: điều kiện phân loại làm đổi số liệu → chỉ Quản lý trở lên
      const r = readForm();
      if(r.err){ alert(r.err); return; }
      dkLast = { col: r.cf.colLabel, op: r.cf.op, from: r.cf.from || 2, to: r.cf.to || 4 };
      customFiltersFor(modId).push(r.cf);
      persistCustomFilters();
      mod.refresh();        // lọc lại mảng dòng của tab đó (Báo Cáo / Đối Soát đọc từ đây)
      refreshDkLocTab();
    };
    if(addBtn) addBtn.addEventListener('click', doAdd);
    if(valIn) valIn.addEventListener('keydown', e=>{ if(e.key === 'Enter'){ e.preventDefault(); doAdd(); } });

    document.querySelectorAll('#dkLocWrap .dk-del').forEach(btn=>{
      btn.addEventListener('click', ()=>{
        if(m2DtDenied()) return;
        const arr = customFiltersFor(modId);   // splice trực tiếp, không gán lại tham chiếu (xem applyCustomFiltersPayload)
        const idx = arr.findIndex(cf => cf.id === btn.dataset.cfId);
        if(idx !== -1) arr.splice(idx, 1);
        persistCustomFilters();
        mod.refresh();
        refreshDkLocTab();
      });
    });
  }

  // Chuyển số serial ngày của Excel (hoặc Date object do SheetJS trả về) thành chuỗi dd/mm/yyyy.
  function formatExcelDate(v){
    if(v === '' || v === null || v === undefined) return '';
    if(v instanceof Date){
      const dd = String(v.getDate()).padStart(2,'0');
      const mm = String(v.getMonth()+1).padStart(2,'0');
      return `${dd}/${mm}/${v.getFullYear()}`;
    }
    const n = Number(v);
    if(!isNaN(n) && n > 0 && String(v).trim() !== ''){
      // Công thức chuẩn của SheetJS: quy đổi serial ngày Excel (epoch 1899-12-30) sang UTC.
      const utcDays = Math.floor(n - 25569);
      const utcMs = utcDays * 86400 * 1000;
      const d = new Date(utcMs);
      const dd = String(d.getUTCDate()).padStart(2,'0');
      const mm = String(d.getUTCMonth()+1).padStart(2,'0');
      return `${dd}/${mm}/${d.getUTCFullYear()}`;
    }
    return String(v);
  }

  function normalize(v){
    return String(v ?? '')
      .normalize('NFD').replace(/[\u0300-\u036f]/g,'')
      .toLowerCase().trim();
  }

  // Ô có phải "số thuần" không — để tô màu/canh phải như số liệu (đồng bộ với Module "Báo Cáo
  // Tồn"). Chỉ khớp khi giá trị chỉ gồm chữ số + dấu phẩy/chấm phân cách + dấu âm + %, và có ít
  // nhất 1 chữ số. Mã hàng / hợp đồng / size... có chữ cái sẽ KHÔNG khớp nên vẫn là chữ thường.
  function looksNumericCell(v){
    const s = String(v ?? '').trim();
    if(s === '') return false;
    if(!/^-?[\d.,]+%?$/.test(s.replace(/\s+/g,''))) return false;
    return /\d/.test(s);
  }

  function findCol(row, keyword){
    if(!row) return -1;
    for(let c=0;c<row.length;c++){
      if(normalize(row[c]).includes(keyword)) return c;
    }
    return -1;
  }

  // Tìm dòng chứa "Mã hàng" (dòng nhãn cột đơn) và dòng chứa "Số kiện" (dòng nhãn nhóm số liệu)
  function locateHeaders(json){
    let mainRow = -1;
    for(let r=0;r<Math.min(json.length,30);r++){
      if(findCol(json[r], 'ma hang') !== -1){ mainRow = r; break; }
    }
    if(mainRow !== -1){
      let subRow = -1;
      for(let r=mainRow; r<=mainRow+2 && r<json.length; r++){
        if(findCol(json[r], 'so kien') !== -1){ subRow = r; break; }
      }
      if(subRow !== -1) return { mainRow, subRow, mode:'text' };
    }

    // Fallback: một số file xuất từ phần mềm kho KHÔNG có nhãn chữ "Mã hàng / Tên hàng / Số lô /
    // Size / Quy cách" — chỉ có nhãn nhóm "Số kiện" / "Trọng Lượng" lặp lại (Đầu kỳ, Nhập, Xuất, Cuối kỳ).
    // Trong trường hợp này, 5 cột NGAY TRƯỚC cột "Số kiện" đầu tiên luôn theo đúng thứ tự cố định:
    // Mã hàng, Tên hàng, Số lô, Size, Quy cách (đúng theo mẫu báo cáo NXT kho lạnh).
    for(let r=0;r<Math.min(json.length,30);r++){
      const row = json[r] || [];
      let firstSoKien = -1;
      for(let c=0;c<row.length;c++){
        if(normalize(row[c]).includes('so kien')){ firstSoKien = c; break; }
      }
      if(firstSoKien >= 5){
        return { mainRow:r, subRow:r, mode:'position', firstSoKien };
      }
    }

    return null;
  }

  let m2ReadInfo = '';
  function extractInventoryData(json){
    const pos = locateHeaders(json);
    if(!pos) return null;

    const row1   = json[pos.mainRow] || [];
    const subRow = json[pos.subRow]  || [];

    let colMaHang, colTenHang, colSoLo, colSize, colQuyCach, colDacTinh, colNgayNhapKho, colPhieuNhap;

    if(pos.mode === 'position'){
      // Không có nhãn chữ -> suy ra vị trí từ cột "Số kiện" đầu tiên (xem locateHeaders).
      colQuyCach = pos.firstSoKien - 1;
      colSize    = pos.firstSoKien - 2;
      colSoLo    = pos.firstSoKien - 3;
      colTenHang = pos.firstSoKien - 4;
      colMaHang  = pos.firstSoKien - 5;
      colDacTinh = -1; // không có cột đặc tính riêng trong mẫu này
      // Mẫu rút gọn (không nhãn chữ) không có sẵn 2 cột này -> để trống.
      colNgayNhapKho = -1;
      colPhieuNhap   = -1;
    } else {
      colMaHang  = findCol(row1, 'ma hang');
      colTenHang = findCol(row1, 'ten hang');
      colSoLo    = findCol(row1, 'so lo');
      colSize    = findCol(row1, 'size');
      colQuyCach = findCol(row1, 'quy cach');
      colDacTinh = findCol(row1, 'dac tinh'); // tùy chọn — không bắt buộc phải có
      colNgayNhapKho = findCol(row1, 'ngay nhap kho'); // tùy chọn
      colPhieuNhap   = findCol(row1, 'phieu nhap');
      if(colPhieuNhap === -1) colPhieuNhap = findCol(row1, 'voucher'); // tùy chọn, phòng khi chỉ có nhãn "Voucher"
    }

    if([colMaHang,colTenHang,colSoLo,colSize,colQuyCach].some(c=>c===-1 || c<0)){
      return null; // không khớp mẫu -> để hàm gọi rơi về chế độ xem thô
    }

    // Nhóm "Số kiện/Trọng lượng" lặp lại cho từng cột (Đầu kỳ, Nhập, Xuất, Cuối kỳ).
    // Không có nhãn tên nhóm trong dữ liệu thật -> lấy CẶP CUỐI CÙNG = Tồn cuối kỳ (đúng theo mẫu báo cáo).
    const soKienIdxs = [];
    for(let c=0;c<subRow.length;c++){
      if(normalize(subRow[c]).includes('so kien')) soKienIdxs.push(c);
    }
    if(!soKienIdxs.length) return null;

    let colSoKien = soKienIdxs[soKienIdxs.length - 1];
    m2ReadInfo = 'Không có nhãn nhóm "TỒN CUỐI KỲ" — lấy cặp Số kiện / Trọng lượng cuối cùng (cột ' + (colSoKien + 1) + ').';
    // PC 4.96: nếu file có nhãn nhóm "TỒN CUỐI KỲ" thì LẤY ĐÚNG NHÓM ĐÓ (file BC_NXT_THP chỉ ghi "Số kiện" ở nhóm XUẤT, các nhóm khác ghi "Số lượng" → trước đây app lấy nhầm cột XUẤT TRONG KỲ).
    for(let r=Math.max(0,pos.mainRow-1); r<=Math.max(pos.mainRow,pos.subRow); r++){
      const hr = json[r] || []; let g = -1;
      for(let c=0;c<hr.length;c++){ if(normalize(hr[c]).includes('ton cuoi')){ g = c; break; } }
      if(g !== -1){ colSoKien = g; m2ReadInfo = 'Đang lấy nhóm cột "TỒN CUỐI KỲ" (cột ' + (g + 1) + ' – ' + (g + 2) + ').'; break; }
    }
    let colTrongLuong = colSoKien + 1;
    if(!normalize(subRow[colTrongLuong]).includes('trong luong')){
      for(let c=colSoKien+1;c<subRow.length;c++){
        if(normalize(subRow[c]).includes('trong luong')){ colTrongLuong = c; break; }
      }
    }

    const dataStart = Math.max(pos.mainRow, pos.subRow) + 1;
    const out = [];
    for(let r=dataStart;r<json.length;r++){
      const row = json[r] || [];
      const isBlank = row.every(cell => String(cell ?? '').trim() === '');
      if(isBlank) break;
      const ma = row[colMaHang];
      if(String(ma ?? '').trim() === '') continue; // bỏ dòng tổng/ghi chú không có mã hàng
      out.push({
        maHang:      row[colMaHang]     ?? '',
        tenHang:     row[colTenHang]    ?? '',
        soLo:        row[colSoLo]       ?? '',
        size:        row[colSize]       ?? '',
        quyCach:     row[colQuyCach]    ?? '',
        ngayNhapKho: colNgayNhapKho !== -1 ? formatExcelDate(row[colNgayNhapKho]) : '',
        phieuNhap:   colPhieuNhap   !== -1 ? (row[colPhieuNhap] ?? '') : '',
        soKien:      row[colSoKien]     ?? '',
        trongLuong:  row[colTrongLuong] ?? '',
        dacTinh:     colDacTinh !== -1 ? (row[colDacTinh] ?? '') : '',
      });
    }
    return out;
  }

  // Lấy riêng cột "Đặc tính" cho những file KHÔNG khớp đủ mẫu tồn kho (thiếu Tên hàng/Số lô/Size/Quy cách/Số kiện...).
  // Chỉ cần dò được dòng có "Mã hàng" và có cột "Đặc tính" trong dòng đó là lấy dữ liệu, không đòi hỏi thêm điều kiện gì khác.
  function extractDacTinhRaw(json){
    let mainRow = -1;
    for(let r=0;r<Math.min(json.length,30);r++){
      if(findCol(json[r], 'ma hang') !== -1){ mainRow = r; break; }
    }
    if(mainRow === -1) return [];

    const row1 = json[mainRow] || [];
    const colDacTinh = findCol(row1, 'dac tinh');
    if(colDacTinh === -1) return [];

    const out = [];
    for(let r=mainRow+1; r<json.length; r++){
      const row = json[r] || [];
      const isBlank = row.every(cell => String(cell ?? '').trim() === '');
      if(isBlank) break;
      const v = row[colDacTinh];
      if(String(v ?? '').trim() === '') continue;
      out.push({ dacTinh: v });
    }
    return out;
  }

  function renderSheet(sheetName){
    const ws = workbook.Sheets[sheetName];
    const json = XLSX.utils.sheet_to_json(ws, {header:1, defval:''});
    if(!json.length){
      currentColumns = [];
      currentDisplayRows = [];
      baseDisplayRows = [];
      searchFilters = {};
      lastHeaderKey = null;
      filteredRows = null;
      rawDacTinhValues = [];
      isFilteredMode = false;
      currentPage = 1;
      tableScroll.innerHTML = '<div class="empty-table">Sheet này không có dữ liệu</div>';
      rowCount.textContent = '';
      exportBtn.style.display = 'none';
      pagination.style.display = 'none';
      summaryStats.innerHTML = '';
      renderLdStats();
      refreshPhieuXuatTab();
      refreshEcuadorTab();
      refreshAnDoTab();
      refreshTGCTab();
      refreshTGSTab();
      refreshNguyenLieuTab();
      refreshHLSOTab();
      refreshDacTinhTab();
      refreshBaoCaoTab();
      refreshDoiSoatTab();
      return;
    }

    const extracted = extractInventoryData(json);

    if(extracted && extracted.length){
      filteredRows = extracted;
      rawDacTinhValues = []; // đã có đặc tính trong filteredRows, khỏi cần lấy thêm ở đây
      isFilteredMode = true;
      currentColumns = TARGET_COLS;
      currentDisplayRows = extracted.map(r => rowToDisplayArray(r));
      toolbarLabel.textContent = 'Dữ liệu đã nạp';
      exportBtn.style.display = 'inline-block';
      setStatus('Nạp và lọc dữ liệu thành công — tìm thấy ' + extracted.length + ' dòng khớp mẫu.', 'ok');
    } else {
      filteredRows = null;
      rawDacTinhValues = extractDacTinhRaw(json); // vẫn lấy cột "Đặc tính" dù file không khớp mẫu tồn kho
      isFilteredMode = false;
      exportBtn.style.display = 'none';
      toolbarLabel.textContent = 'Dữ liệu đã nạp (chưa khớp mẫu cột)';
      const header = json[0];
      currentColumns = header.map(h => ({ label: String(h) }));
      currentDisplayRows = json.slice(1);
      setStatus('Không tìm thấy đủ cột theo mẫu (Mã hàng / Tên hàng / Số lô / Size / Quy cách / Ngày nhập kho / Phiếu nhập-Voucher / Tồn cuối kỳ) trong sheet "' + sheetName + '" — đang hiển thị dữ liệu thô.', 'err');
    }

    // Nạp sheet/file mới -> đặt lại bộ lọc tìm kiếm theo tiêu đề và buộc vẽ lại dòng tìm kiếm.
    baseDisplayRows = currentDisplayRows;
    searchFilters = {};
    resetTableCheckboxFilters('main');
    lastHeaderKey = null;

    currentPage = 1;
    updateSummaryBar();
    renderPage();
    refreshPhieuXuatTab();
    refreshEcuadorTab();
    refreshAnDoTab();
    refreshTGCTab();
    refreshTGSTab();
    refreshNguyenLieuTab();
    refreshHLSOTab();
    refreshDacTinhTab();
    refreshBaoCaoTab();
    refreshDoiSoatTab();
  }

  function toNumber(v){
    if(window.KLNum) return window.KLNum.parseOr(v, 0, { unit:true }); // PC 2.32: quy chuẩn số chung (trống / không đọc được = 0 như trước)
    if(v === '' || v === null || v === undefined) return 0;
    const n = parseFloat(String(v).replace(/,/g,''));
    return isNaN(n) ? 0 : n;
  }

  function formatNumber(n){
    return n.toLocaleString('en-US', { maximumFractionDigits:2 });
  }

  // Định dạng riêng cho TẤT CẢ số liệu liên quan đến Trọng lượng (net): luôn có dấu phẩy
  // ngăn cách phần ngàn và đúng 2 chữ số thập phân — ví dụ "1620" -> "1,620.00",
  // "2053.8" -> "2,053.80". Dùng cho mọi nơi hiển thị/xuất Trọng Lượng (KG) trong ứng dụng.
  function formatWeight(v){
    if(v === '' || v === null || v === undefined) return '';
    const n = toNumber(v);
    return n.toLocaleString('en-US', { minimumFractionDigits:2, maximumFractionDigits:2 });
  }

  // Chuyển 1 dòng dữ liệu (object) thành mảng giá trị hiển thị theo đúng thứ tự TARGET_COLS,
  // đồng thời áp định dạng Trọng Lượng (KG) ở trên cho cột trongLuong. Cột "Đặc tính" KHÔNG
  // phải field có sẵn trong dữ liệu gốc — được SUY RA từ "Tên hàng" bằng
  // deriveDacTinhFromTenHang() (dùng chung với tab "Đặc Tính"/"Báo Cáo" — xem chú thích tại
  // nơi khai báo hàm đó, đặc biệt trường hợp riêng "CKQ").
  function rowToDisplayArray(r){
    return TARGET_COLS.map(c => {
      if(c.key === 'trongLuong') return formatWeight(r[c.key]);
      if(c.key === 'dacTinh') return deriveDacTinhFromTenHang(r.tenHang, r.soLo);
      return r[c.key];
    });
  }

  // Chuyển 1 dòng dữ liệu (object) thành object để xuất Excel (json_to_sheet), áp cùng định dạng
  // Trọng Lượng (KG) như trên màn hình để đồng bộ giữa ứng dụng và file Excel xuất ra.
  function rowToExportObj(r){
    const o = {};
    TARGET_COLS.forEach(c => {
      if(c.key === 'trongLuong') o[c.label] = formatWeight(r[c.key]);
      else if(c.key === 'dacTinh') o[c.label] = deriveDacTinhFromTenHang(r.tenHang, r.soLo);
      else o[c.label] = r[c.key];
    });
    return o;
  }

  // Bảng phải "Dữ liệu đã nạp" của tab Load data (bố cục 2 bảng song song): các ô số tổng + nhãn
  // trạng thái. Tính trên TOÀN BỘ dữ liệu đã nạp (không theo trang/tìm kiếm).
  // (lấy phần tử NGAY TRONG hàm — hàm có thể được gọi sớm lúc khởi động qua clearLoadedData(),
  //  trước khi chạy tới dòng khai báo hằng ở đây)
  function renderLdStats(){
    const ldStatsEl = document.getElementById('ldStats');
    const ldBadgeEl = document.getElementById('ldBadge');
    if(!ldStatsEl) return;
    const tile = (v, l) => `<div class="ld-stat"><div class="v">${v}</div><div class="l">${l}</div></div>`;
    if(isFilteredMode && filteredRows && filteredRows.length){
      let soKien = 0, trongLuong = 0;
      const maHang = new Set();
      filteredRows.forEach(r=>{
        soKien += toNumber(r.soKien);
        trongLuong += toNumber(r.trongLuong);
        const m = String(r.maHang ?? '').trim();
        if(m) maHang.add(m);
      });
      ldStatsEl.innerHTML =
        tile(formatNumber(filteredRows.length), 'Dòng') +
        tile(formatNumber(maHang.size), 'Mã hàng') +
        tile(formatNumber(soKien), 'Tồn kiện') +
        tile(formatWeight(trongLuong), 'Tồn KG');
      if(ldBadgeEl){ ldBadgeEl.className = 'ph-badge ok'; ldBadgeEl.textContent = '● Khớp mẫu'; }
    } else if(currentDisplayRows.length){
      ldStatsEl.innerHTML = tile(formatNumber(currentDisplayRows.length), 'Dòng') +
        `<div class="ld-stat wide"><div class="l" style="margin:0;text-transform:none;letter-spacing:0;font-size:11.5px;">File chưa khớp mẫu cột Tồn cuối kỳ — chỉ đếm được số dòng. Kiểm tra lại tên cột trong file.</div></div>`;
      if(ldBadgeEl){ ldBadgeEl.className = 'ph-badge warn'; ldBadgeEl.textContent = 'Chưa khớp mẫu'; }
    } else {
      ldStatsEl.innerHTML = `<div class="ld-empty">Chưa có dữ liệu.<br>Chọn file ở bảng bên trái — các số tổng sẽ hiện ở đây.</div>`;
      if(ldBadgeEl){ ldBadgeEl.className = 'ph-badge none'; ldBadgeEl.textContent = 'Chưa có dữ liệu'; }
    }
  }

  function updateSummaryBar(){
    renderLdStats();
    if(isFilteredMode && filteredRows && filteredRows.length){
      let soKienSum = 0, trongLuongSum = 0;
      filteredRows.forEach(r=>{
        soKienSum += toNumber(r.soKien);
        trongLuongSum += toNumber(r.trongLuong);
      });
      summaryStats.innerHTML =
        `<span class="stat">Số dòng<b>${formatNumber(filteredRows.length)}</b></span>` +
        `<span class="stat">Tồn kiện<b>${formatNumber(soKienSum)}</b></span>` +
        `<span class="stat">Tồn KG<b>${formatWeight(trongLuongSum)}</b></span>`;
    } else if(currentDisplayRows.length){
      summaryStats.innerHTML = `<span class="stat">Số dòng<b>${formatNumber(currentDisplayRows.length)}</b></span>`;
    } else {
      summaryStats.innerHTML = '';
    }
  }

  function renderPage(){
    const total = currentDisplayRows.length;
    const totalPages = Math.max(1, Math.ceil(total / pageSize));
    if(currentPage > totalPages) currentPage = totalPages;
    if(currentPage < 1) currentPage = 1;

    const start = (currentPage - 1) * pageSize;
    const pageRows = currentDisplayRows.slice(start, start + pageSize);

    renderTable(currentColumns, pageRows, isFilteredMode);

    if(total > 0){
      const from = start + 1;
      const to = Math.min(start + pageSize, total);
      rowCount.textContent = `${formatNumber(total)} dòng (${from}–${to})`;
    } else {
      rowCount.textContent = '';
    }

    pageIndicator.textContent = 'Trang ' + currentPage + '/' + totalPages;
    prevPageBtn.disabled = currentPage <= 1;
    nextPageBtn.disabled = currentPage >= totalPages;
    pagination.style.display = total > 0 ? 'flex' : 'none';
  }

  pageSizeSelect.addEventListener('change', ()=>{
    pageSize = parseInt(pageSizeSelect.value, 10);
    currentPage = 1;
    renderPage();
  });
  prevPageBtn.addEventListener('click', ()=>{ currentPage--; renderPage(); });
  nextPageBtn.addEventListener('click', ()=>{ currentPage++; renderPage(); });

  function normalizeSearch(v){
    return normalize(v); // dùng chung hàm normalize (bỏ dấu, chữ thường) để tìm kiếm không phân biệt dấu tiếng Việt
  }

  // excludeIdx: nếu truyền vào, bộ lọc (tìm kiếm + checkbox) của CHÍNH cột đó sẽ bị bỏ qua khi tính kết quả.
  // Dùng để xây "lọc liên kết theo tầng" (cascading filter): khi mở dropdown chọn giá trị của 1 cột,
  // ta cần biết những giá trị nào còn "khả dụng" dựa trên các cột KHÁC đã lọc, không tính bộ lọc của
  // chính cột đang mở (nếu không thì đã chọn gì sẽ chỉ thấy lại đúng cái đã chọn).
  function getFilteredRows(excludeIdx){
    const activeFilters = Object.keys(searchFilters)
      .map(k => [parseInt(k,10), searchFilters[k]])
      .filter(([idx, v]) => idx !== excludeIdx && v && v.trim() !== '')
      .map(([idx, v]) => [idx, normalizeSearch(v)]); // chuẩn hoá từ khoá 1 LẦN, không lặp lại cho từng dòng
    const activeCheckbox = [];
    currentColumns.forEach((_, i)=>{
      if(i === excludeIdx) return;
      const set = checkboxFilters[cfKey('main', i)];
      if(set) activeCheckbox.push([i, set]);
    });
    const result = (!activeFilters.length && !activeCheckbox.length) ? baseDisplayRows :
      baseDisplayRows.filter(row =>
        activeFilters.every(([idx, val]) => normalizeSearch(String(row[idx] ?? '')).includes(val)) &&
        activeCheckbox.every(([idx, set]) => set.has(String(row[idx] ?? '').trim()))
      );
    return applySortArrayRows(result, 'main');
  }

  // Gõ ô tìm theo cột: chờ ngừng gõ mới lọc + vẽ lại (bảng lớn lọc mỗi phím làm khựng, ngắt quãng gõ Telex).
  const applySearchFiltersTyping = KLAType.debounce(()=>applySearchFilters(), 160);
  function applySearchFilters(){
    currentDisplayRows = getFilteredRows();
    currentPage = 1;
    renderPage();
    updateSummaryBar();
  }

  // Dòng tìm kiếm thông minh theo từng cột — dùng ủy quyền sự kiện trên tableScroll (phần tử này không
  // bị thay thế giữa các lần render) nên input không bao giờ mất focus khi người dùng đang gõ.
  tableScroll.addEventListener('input', (e)=>{
    const input = e.target;
    if(!input.classList || !input.classList.contains('col-search-input')) return;
    const idx = parseInt(input.getAttribute('data-col-index'), 10);
    searchFilters[idx] = input.value;
    const th = input.closest('th');
    if(th) th.classList.toggle('has-value', !!input.value.trim());
    // Tô đỏ luôn Ô TIÊU ĐỀ CHÍNH (không phải ô tìm kiếm) của đúng cột này ngay khi gõ — vì
    // tiêu đề không vẽ lại theo từng phím gõ (tránh mất focus, xem chú thích headerKey ở trên).
    const mainTh = tableScroll.querySelector(`thead tr:first-child th[data-col-index="${idx}"]`);
    if(mainTh) mainTh.classList.toggle('has-filter', !!input.value.trim() || isColumnFilterActive('main', idx));
    applySearchFiltersTyping();
  });

  // Nút lọc checkbox (▾) trên dòng tìm kiếm của bảng Load data
  tableScroll.addEventListener('click', (e)=>{
    // ĐỒNG BỘ (2026-09): chỉ bắt sự kiện sắp xếp khi bấm ĐÚNG phần chữ tiêu đề (.th-label),
    // không phải cả ô <th> — trước đây bấm bất kỳ đâu trong ô tiêu đề đều kích hoạt sắp xếp.
    const sortLabel = e.target.closest('th.sort-th .th-label');
    const sortTh = sortLabel ? sortLabel.closest('th.sort-th') : null;
    if(sortTh){
      const idx = parseInt(sortTh.getAttribute('data-col-index'), 10);
      toggleColumnSort('main', idx);
      lastHeaderKey = null; // buộc vẽ lại tiêu đề để cập nhật trạng thái sắp xếp
      applySearchFilters();
      return;
    }
    const btn = e.target.closest('.col-filter-btn');
    if(!btn) return;
    const idx = parseInt(btn.getAttribute('data-col-index'), 10);
    // Lọc liên kết theo tầng: chỉ lấy giá trị của những dòng đã khớp với các bộ lọc CỘT KHÁC
    // (không tính bộ lọc của chính cột "idx" này) để danh sách chọn không hiện giá trị của toàn bộ dữ liệu.
    const values = getFilteredRows(idx).map(row => row[idx]);
    openColumnFilterDropdown(btn, 'main', idx, currentColumns[idx].label, values, ()=>{
      lastHeaderKey = null; // buộc vẽ lại tiêu đề để cập nhật trạng thái "đang lọc" của nút
      applySearchFilters();
    });
  });

  enableCellResize(tableScroll, 'main');

  // Lấy danh sách giá trị duy nhất của 1 cột (từ toàn bộ dữ liệu, không bị ảnh hưởng bởi bộ lọc hiện tại)
  // để gợi ý tự động (datalist) cho ô tìm kiếm thông minh của cột đó.
  function buildColumnSuggestions(colIndex){
    const set = new Set();
    // Chỉ gợi ý những giá trị còn khả dụng dựa trên các cột KHÁC đang lọc (lọc liên kết theo tầng)
    getFilteredRows(colIndex).forEach(row=>{
      const v = row[colIndex];
      const s = String(v ?? '').trim();
      if(s !== '') set.add(s);
    });
    const MAX_SUGGESTIONS = 2000; // PC 0.32: 300 → 2000 (Mã hàng nhiều hơn 300 giá trị thì phần sau A→Z trước đây không có gợi ý)
    return Array.from(set)
      .sort((a,b)=> a.localeCompare(b, 'vi'))
      .slice(0, MAX_SUGGESTIONS);
  }

  function renderTable(columns, rows, filteredMode){
    const sortState = getColumnSort('main');
    const sortKeyStr = sortState ? sortState.idx + ':' + sortState.dir : '';
    const headerKey = (filteredMode ? '1' : '0') + '|' + sortKeyStr + '|' + columns.map(c=>c.label).join('||');
    if(headerKey !== lastHeaderKey){
      let html = `<table${filteredMode ? ' class="filtered-table"' : ''}><thead><tr>`;
      columns.forEach((c,i) => {
        const activeSort = sortState && sortState.idx === i ? (sortState.dir === 1 ? ' sort-asc' : ' sort-desc') : '';
        const hasFilter = (searchFilters[i] && searchFilters[i].trim() !== '') || isColumnFilterActive('main', i);
        html += `<th class="sort-th${activeSort}${hasFilter ? ' has-filter' : ''}" data-col-index="${i}"><span class="th-label" title="Nhấp để sắp xếp">${escapeHtml(c.label)}</span></th>`;
      });
      html += '</tr><tr class="search-row">';
      columns.forEach((c,i) => {
        const val = searchFilters[i] || '';
        const listId = `dl-col-${i}`;
        const options = buildColumnSuggestions(i).map(v => `<option value="${escapeHtml(v)}">`).join('');
        const filterActive = isColumnFilterActive('main', i);
        html += `<th class="${val.trim() ? 'has-value' : ''}">` +
          `<div class="col-search-cell">` +
            `<input type="text" class="col-search-input" list="${listId}" data-col-index="${i}" placeholder="Tìm ${escapeHtml(c.label)}…" value="${escapeHtml(val)}">` +
            `<button type="button" class="col-filter-btn${filterActive ? ' active' : ''}" data-col-index="${i}" title="Lọc theo giá trị">▾</button>` +
          `</div>` +
          `<datalist id="${listId}">${options}</datalist>` +
          `</th>`;
      });
      html += '</tr></thead><tbody id="tableBodyRows"></tbody></table>';
      tableScroll.innerHTML = html;
      lastHeaderKey = headerKey;
    }
    const tbody = document.getElementById('tableBodyRows');
    if(!tbody) return;
    if(rows.length){
      let body = '';
      rows.forEach(r=>{
        body += '<tr>';
        columns.forEach((_, i)=> { const cv = String(r[i] ?? ''); body += `<td${looksNumericCell(cv) ? ' class="num"' : ''}>${escapeHtml(cv)}</td>`; });
        body += '</tr>';
      });
      tbody.innerHTML = body;
    } else {
      const hasActiveFilter = Object.values(searchFilters).some(v => v && v.trim() !== '');
      tbody.innerHTML = `<tr><td colspan="${columns.length}" style="text-align:center;color:var(--text-low);padding:24px;font-family:'JetBrains Mono',monospace;font-size:12px;">` +
        (hasActiveFilter ? 'Không có dòng nào khớp với tìm kiếm.' : 'Không có dữ liệu để hiển thị') +
        '</td></tr>';
    }
    if(filteredMode){
      applyStoredSizes(tableScroll.querySelector('table'), 'main');
    }
  }

  exportBtn.addEventListener('click', ()=>{
    if(!filteredRows || !filteredRows.length) return;
    const data = filteredRows.map(r => rowToExportObj(r));
    const ws = XLSX.utils.json_to_sheet(data);
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, 'Du lieu loc');
    XLSX.writeFile(wb, 'du-lieu-da-loc.xlsx');
  });

  // ---- TAB: Phiếu Xuất ----
  // Bảng trên: lấy dữ liệu từ "Load data", tick chọn dòng + nhập số lượng xuất, rồi "Tạo phiếu xuất".
  // Bảng dưới: danh sách các phiếu xuất đã tạo (lưu trong localStorage) — xuất Excel / sửa / xóa từng phiếu tại đây.
  const toolbarLabelPX  = document.getElementById('toolbarLabelPX');
  const tableScrollPX   = document.getElementById('tableScrollPX');
  const rowCountPX      = document.getElementById('rowCountPX');
  const summaryStatsPX  = document.getElementById('summaryStatsPX');
  const paginationPX    = document.getElementById('paginationPX');
  const pageSizeSelectPX= document.getElementById('pageSizeSelectPX');
  const prevPageBtnPX   = document.getElementById('prevPagePX');
  const nextPageBtnPX   = document.getElementById('nextPagePX');
  const pageIndicatorPX = document.getElementById('pageIndicatorPX');
  const createPxBtn     = document.getElementById('createPxBtn');
  const pxMaPhieuInput  = document.getElementById('pxMaPhieuInput');
  // Đã bỏ ô "Tìm nhanh" riêng của tab Phiếu Xuất — thay bằng dòng tìm kiếm + lọc theo từng cột,
  // đồng bộ với các bảng khác trong module (xem renderPxPage()).
  const selectAllBtnPX  = document.getElementById('selectAllBtnPX');
  const selectNoneBtnPX = document.getElementById('selectNoneBtnPX');
  const pxListScroll    = document.getElementById('pxListScroll');
  const pxListCount     = document.getElementById('pxListCount');

  const PX_EXPORT_COLS = [
    { key:'maHang',    label:'Mã hàng' },
    { key:'tenHang',   label:'Tên hàng' },
    // PC 0.32: thêm cột "Đặc tính" (suy ra từ Tên hàng qua objColValue/deriveDacTinhFromTenHang, giống bảng Dữ Liệu / Phân Loại)
    { key:'dacTinh',   label:'Đặc tính', w:150 },
    { key:'phieuNhap', label:'Phiếu nhập' },
    { key:'soLo',      label:'Số lô' },
    { key:'size',      label:'Size' },
    { key:'soKien',    label:'Số kiện' },
  ];

  // Trọng lượng (KG) TƯƠNG ỨNG với số lượng đang định xuất của 1 dòng — tính THEO TỈ LỆ giống
  // hệt công thức trừ tồn thật khi tạo phiếu (xem trongLuongTru ở createPxPhieu bên dưới):
  // kg/kiện = trongLuong hiện có / soKien hiện có, nhân với số lượng đang nhập ở ô "SL xuất".
  // Cột này CHỈ hiển thị trên bảng để người dùng biết cần xuất bao nhiêu KG — KHÔNG đưa vào
  // PX_EXPORT_COLS nên không lọt vào file Excel phiếu xuất (buildPxPhieuWorkbook() có headers
  // riêng, không đụng tới PX_EXPORT_COLS hay hàm này).
  function pxRowKg(r, qtyRaw){
    if(!r) return 0;
    const soKienGoc = toNumber(r.soKien);
    if(soKienGoc <= 0) return toNumber(r.trongLuong) > 0 ? toNumber(qtyRaw) : 0; // PC 5.10: dòng chỉ có kg → ô SL xuất là số KG
    return toNumber(r.trongLuong) * (toNumber(qtyRaw) / soKienGoc);
  }
  function pxDefQty(r){ return (toNumber(r.soKien) <= 0 && toNumber(r.trongLuong) > 0) ? r.trongLuong : r.soKien; }

  let pxRows = [];              // tham chiếu tới filteredRows tại thời điểm nạp dữ liệu
  let pxIndexByRow = new Map(); // tra cứu nhanh: object dòng -> chỉ số gốc trong pxRows (dùng khi
                                 // bảng đã lọc/sắp xếp làm đổi vị trí hiển thị so với mảng gốc)
  let pxSelected = new Set();   // tập chỉ số (index trong pxRows) các dòng đang được tick chọn
  let pxQuantities = {};        // { idx trong pxRows: số lượng xuất đang nhập (chuỗi) }
  let pxPage = 1;
  let pxPageSize = 300; // PC 5.06: mặc định 300 (trước 500) — 300 / 400 / 500 dòng/trang
  // Trạng thái tìm kiếm + sắp xếp + kéo dãn theo từng cột — CÙNG CƠ CHẾ với "Dữ Liệu" và các tab
  // Phân Loại (renderGenericTable/applyColumnSearch/enableCellResize) để bảng "Phiếu Xuất" có đủ
  // dòng tìm kiếm, lọc, sắp xếp khi bấm tiêu đề, kéo dãn cột/dòng và tiêu đề cố định khi cuộn —
  // giống hệt các bảng khác trong module này. Không dùng chung renderGenericTable() trực tiếp vì
  // bảng này có thêm 3 cột đặc thù (checkbox chọn dòng, STT, ô nhập Số lượng xuất) mà hàm dùng
  // chung không hỗ trợ — renderPxPage() tự dựng bảng nhưng tái sử dụng lại đúng các hàm lõi
  // (applyColumnSearch, toggleColumnSort, enableCellResize, applyStoredSizes...).
  let pxSearchState = { searchFilters:{}, lastHeaderKey:null, bound:false, resizeBound:false };

  // PC 1.70 — Phiếu Xuất có 2 menu con (Tạo phiếu xuất / Danh sách phiếu xuất) + dải lọc nhanh theo nhóm đã phân loại.
  // Nhóm dùng ĐÚNG hàm lọc + điều kiện tự thêm của từng tab Phân Loại (tab Phân Loại không đổi gì).
  let pxSub = 'tao';   // menu con đang xem: 'tao' | 'ds'
  let pxGroup = 'all'; // nhóm đang lọc nhanh: 'all' hoặc id tab Phân Loại
  let pxGrpCache = null;
  // PC 2.66 — Tab Phiếu Xuất CHỈ liệt kê dòng CÒN TỒN. Dòng Số kiện = 0 và Trọng lượng = 0 (hết hàng từ file nạp,
  // hoặc đã bị phiếu xuất trừ về 0) không thể xuất nữa nên ẩn khỏi bảng chọn, số đếm nhóm, tổng và nút "Chọn tất cả".
  // pxRows vẫn là toàn bộ persistedRows (giữ nguyên chỉ số gốc cho pxSelected / pxQuantities / pxIndexByRow); chỉ lọc ở lúc
  // hiển thị — nên phiếu bị sửa / xóa hoàn lại tồn thì dòng tự hiện lại. Dòng tồn ÂM vẫn hiện (là số liệu bất thường cần thấy).
  // Ngưỡng 0.005 giống "còn tồn" của bảng báo cáo (PC 2.11). Các tab Dữ Liệu / Ecuador / … vẫn hiện đủ dòng như cũ.
  function pxInStock(r){ return Math.abs(toNumber(r.soKien)) >= 0.005 || Math.abs(toNumber(r.trongLuong)) >= 0.005; }
  function pxLiveRows(){ return pxRows.filter(pxInStock); }
  const PX_GROUPS = [
    { id:'all', label:'Tất cả' },
    { id:'nl',  label:'Nguyên Liệu', test:r=>isNguyenLieuSize(r.size) },
    { id:'5',   label:'HLSO',        test:r=>isHLSOLot(r) },
    { id:'4',   label:'Giám Sát',    test:r=>isTGSLot(r.soLo) },
    { id:'3',   label:'Gia Công',    test:r=>isTGCLot(r.soLo) },
    { id:'ad',  label:'Ấn Độ',       test:r=>isAnDoOnly(r) },
    { id:'2',   label:'Ecuador',     test:r=>isEcuadorLot(r.soLo) }
  ];
  // Tập dòng (object) thuộc từng nhóm — có bộ nhớ đệm, tính lại khi đổi dữ liệu / điều kiện tự thêm
  function pxGroupSets(){
    const sig = (pxRows ? pxRows.length : 0) + '|' + CUSTOM_FILTER_TABS.map(id=>JSON.stringify(customFiltersFor(id))).join('#');
    if(pxGrpCache && pxGrpCache.src === pxRows && pxGrpCache.sig === sig) return pxGrpCache.sets;
    const sets = {};
    PX_GROUPS.forEach(g=>{
      if(!g.test) return;
      sets[g.id] = new Set(applyCustomFiltersToRows(pxRows.filter(g.test), g.id));
    });
    pxGrpCache = { src:pxRows, sig, sets };
    return sets;
  }
  function renderPxGroupBar(){
    const bar = document.getElementById('pxGroupBar');
    if(!bar) return;
    if(!pxRows || !pxRows.length){ bar.innerHTML = ''; return; }
    const sets = pxGroupSets();
    if(pxGroup !== 'all' && !sets[pxGroup]) pxGroup = 'all';
    bar.innerHTML = PX_GROUPS.map(g=>{
      let n = 0;
      if(g.id === 'all') n = pxLiveRows().length;
      else if(sets[g.id]) sets[g.id].forEach(r => { if(pxInStock(r)) n++; });
      return '<button type="button" class="kl-subtab' + (g.id === pxGroup ? ' active' : '') + '" data-pxgrp="' + g.id + '">' +
             g.label + ' <span class="num">' + formatNumber(n) + '</span></button>';
    }).join('');
  }
  function setPxSub(sub){
    pxSub = (sub === 'ds') ? 'ds' : 'tao';
    document.querySelectorAll('#pxSubnav [data-pxsub]').forEach(b=> b.classList.toggle('active', b.dataset.pxsub === pxSub));
    const pick = document.getElementById('pxPickPanel');
    const list = document.getElementById('pxListPanel');
    const head = document.querySelector('#tab-px > .panel-head');
    if(pick) pick.style.display = (pxSub === 'tao') ? '' : 'none';
    if(list) list.style.display = (pxSub === 'ds') ? '' : 'none';
    if(head) head.style.display = (pxSub === 'tao') ? '' : 'none';
    if(pxSub === 'ds') renderPxList();
    fitPxLayout();
  }

  // ---- Danh sách phiếu xuất đã tạo (lưu localStorage để không mất khi tải lại trang) ----
  const PX_PHIEU_STORAGE_KEY = 'dataConsole_phieuXuatList_v1';
  let pxPhieuList = [];
  // PC 3.24 — MÁY CON LAN KHI MÁY CHỦ TẮT: tạo / xóa phiếu xuất Tồn kho gửi vẫn làm được (lệnh phiếu → main: bản sao + sổ chờ; máy chủ gộp lại, xem core/phieu-gop-m2.js). Sửa phiếu chỉ khi máy chủ bật.
  const m2Lan = { mode: '', offline: false };
  (function(){ try{ if(!window.lanAPI) return; const up = i => { if(i){ m2Lan.mode = i.mode || ''; m2Lan.offline = !!i.offline; } }; window.lanAPI.getInfo().then(r => up(r && r.info)).catch(()=>{}); window.lanAPI.onStatus(up); }catch(e){ /* máy đơn */ } })();
  function m2Offline(){ return m2Lan.mode === 'client' && m2Lan.offline && !!(window.lanAPI && window.lanAPI.phieuOffline); }
  async function m2QueuePhieu(op){
    try{ const r = await window.lanAPI.phieuOffline(Object.assign({ kind: 'm2-phieu' }, op)); if(r && r.ok) return true; alert((r && r.error) || 'Không lưu được thao tác phiếu lúc máy chủ tắt.'); }
    catch(e){ alert('Không lưu được thao tác phiếu lúc máy chủ tắt: ' + (e && e.message || e)); }
    return false;
  }

  // ===== PC 1.69 — PHIẾU XUẤT GẮN VỚI ĐỢT SỐ LIỆU TỒN MÀ NÓ ĐÃ TRỪ =====
  // Mỗi phiếu nhớ `loadAt` (= m2Meta.importedAt lúc tạo, tức đợt "Nạp Dữ Liệu" nó trừ vào) và `loadDate` (Ngày số liệu
  // của đợt đó). Khi nạp số liệu MỚI (importedAt đổi) thì phiếu của đợt cũ KHÔNG còn tác dụng: không trừ vào số mới,
  // bị KHÓA (chỉ xem / xuất Excel / xóa), xóa phiếu KHÔNG hoàn tồn (trước đây hoàn nhầm vào số liệu mới nếu trùng khóa dòng).
  // loadAt === undefined = phiếu cũ chưa được gắn đợt (tạm coi đang trừ cho tới khi pxMigrateLegacyBatch() gắn).
  function pxLoadStamp(){ return Number(m2Meta && m2Meta.importedAt) || 0; }
  function pxIsActive(p){
    if(!p) return false;
    if(p.loadAt === undefined || p.loadAt === null) return true;
    return (Number(p.loadAt) || 0) === pxLoadStamp();
  }
  function pxLoadDateText(p){
    if(p && p.loadDate) return p.loadDate;
    const t = Number(p && p.loadAt) || 0;
    if(t > 1){ const d = new Date(t); if(!isNaN(d.getTime())) return ddmmFmt(d.getDate(), d.getMonth() + 1, d.getFullYear()) + ' (ngày nạp)'; }
    return '';
  }
  // Phiếu tạo từ bản cũ (chưa có loadAt): so ngày tạo phiếu với lúc nạp số liệu hiện tại — tạo SAU khi nạp = đang trừ số liệu này;
  // tạo TRƯỚC = thuộc đợt cũ (loadAt = 1, bị khóa). Phiếu từ điện thoại / ngày tạo không đọc được: coi là đang trừ (không đoán bừa).
  function pxMigrateLegacyBatch(){
    let changed = false;
    const cur = pxLoadStamp();
    pxPhieuList.forEach(p=>{
      if(p.loadAt !== undefined && p.loadAt !== null) return;
      const t = Date.parse(p.ngayTao);
      if(!cur) p.loadAt = 0;
      else if(p.nguon || isNaN(t)) p.loadAt = cur;
      else p.loadAt = (t >= cur) ? cur : 1;
      p.loadDate = (p.loadAt === cur) ? (m2Meta.dataDate || '') : '';
      changed = true;
    });
    if(changed) savePxPhieuList();
  }

  function loadPxPhieuList(){
    try{
      const raw = localStorage.getItem(PX_PHIEU_STORAGE_KEY);
      pxPhieuList = raw ? JSON.parse(raw) : [];
      if(!Array.isArray(pxPhieuList)) pxPhieuList = [];
    }catch(e){ pxPhieuList = []; }
  }
  async function savePxPhieuList(){
    try{ localStorage.setItem(PX_PHIEU_STORAGE_KEY, JSON.stringify(pxPhieuList)); }catch(e){}
    if(hasDbAPI()){
      try{
        // Tách thành 2 sheet phẳng (giống cơ chế LSX/DinhMuc ở Module 2): "PhieuXuat" lưu
        // thông tin đầu phiếu, "PhieuXuatItems" lưu từng dòng hàng, liên kết qua "phieuId".
        // nguon/nguoiTao/ghiChu: phiếu do ĐIỆN THOẠI gửi về (xem khối "NHẬN PHIẾU XUẤT TỪ ĐIỆN
        // THOẠI" cuối file) — phiếu tạo trên PC để trống nguon.
        const headerRows = pxPhieuList.map(p => ({
          id: p.id || '', maPhieu: p.maPhieu || '', ngayTao: p.ngayTao || '',
          nguon: p.nguon || '', nguoiTao: p.nguoiTao || '', ghiChu: p.ghiChu || '',
          loadAt: Number(p.loadAt) || 0, loadDate: p.loadDate || ''
        }));
        const itemRows = [];
        pxPhieuList.forEach(p=>{
          (p.items || []).forEach(it=>{
            itemRows.push({
              phieuId: p.id || '', maHang: it.maHang ?? '', tenHang: it.tenHang ?? '',
              phieuNhap: it.phieuNhap ?? '', soLo: it.soLo ?? '', size: it.size ?? '',
              soKien: it.soKien ?? '', soLuongXuat: it.soLuongXuat ?? 0,
              occ: (typeof it.occ === 'number') ? it.occ : '', xuatKg: it.xuatKg ?? '',
              // Trọng lượng đã trừ cho đúng dòng này — PHẢI lưu vào Excel, nếu không mở lại app
              // rồi Xóa/Sửa phiếu sẽ chỉ hoàn lại Số kiện mà không hoàn lại Trọng lượng.
              trongLuongTru: it.trongLuongTru ?? 0
            });
          });
        });
        const [r1, r2] = await Promise.all([
          window.KLLuuDong ? window.KLLuuDong.save(DB_KEY, 'PhieuXuat', headerRows) : window.dbAPI.writeSheet(DB_KEY, 'PhieuXuat', headerRows), // PC 1.68: lưu theo dòng
          window.KLLuuDong ? window.KLLuuDong.save(DB_KEY, 'PhieuXuatItems', itemRows) : window.dbAPI.writeSheet(DB_KEY, 'PhieuXuatItems', itemRows),
        ]);
        if(!r1 || !r1.ok || !r2 || !r2.ok){ console.error('Lỗi lưu database Excel (module2 - phiếu xuất).'); return false; }
      }catch(e){ console.error('Lỗi lưu database Excel (module2 - phiếu xuất):', e); return false; }
    }
    return true;
  }
  function recomputePxPhieuCounter(){
    pxPhieuCounter = pxPhieuList.reduce((max, p)=>{
      const m = /^PX(\d+)$/.exec(p.maPhieu || '');
      return m ? Math.max(max, parseInt(m[1], 10)) : max;
    }, 0);
  }
  async function hydratePxPhieuListFromDb(){
    if(!hasDbAPI()) return false;
    try{
      const [hRes, iRes] = await Promise.all([
        window.KLLuuDong ? window.KLLuuDong.read(DB_KEY, 'PhieuXuat') : window.dbAPI.readSheet(DB_KEY, 'PhieuXuat'), // PC 1.68
        window.KLLuuDong ? window.KLLuuDong.read(DB_KEY, 'PhieuXuatItems') : window.dbAPI.readSheet(DB_KEY, 'PhieuXuatItems'),
      ]);
      if(hRes && hRes.ok && iRes && iRes.ok){
        const itemsByPhieu = {};
        (iRes.data || []).forEach(r=>{
          const pid = String(r.phieuId ?? '');
          if(!itemsByPhieu[pid]) itemsByPhieu[pid] = [];
          itemsByPhieu[pid].push({
            maHang: String(r.maHang ?? ''), tenHang: String(r.tenHang ?? ''),
            phieuNhap: String(r.phieuNhap ?? ''), soLo: String(r.soLo ?? ''),
            size: String(r.size ?? ''), soKien: r.soKien ?? '',
            soLuongXuat: Number(r.soLuongXuat) || 0,
            trongLuongTru: Number(r.trongLuongTru) || 0,
            ...((r.occ === undefined || r.occ === null || r.occ === '' || isNaN(Number(r.occ))) ? {} : { occ: Number(r.occ) }),
            ...((r.xuatKg === undefined || r.xuatKg === null || r.xuatKg === '' || isNaN(Number(r.xuatKg))) ? {} : { xuatKg: Number(r.xuatKg) })
          });
        });
        pxPhieuList = (hRes.data || []).map(r=>{
          const id = String(r.id ?? '');
          return {
            id, maPhieu: String(r.maPhieu ?? ''), ngayTao: String(r.ngayTao ?? ''),
            nguon: String(r.nguon ?? ''), nguoiTao: String(r.nguoiTao ?? ''), ghiChu: String(r.ghiChu ?? ''),
            loadAt: (r.loadAt === undefined || r.loadAt === null || r.loadAt === '') ? undefined : (Number(r.loadAt) || 0),
            loadDate: String(r.loadDate ?? ''),
            items: itemsByPhieu[id] || []
          };
        });
        return true;
      }
      console.error('Đọc database Excel (module6 - phiếu xuất) không hợp lệ, giữ nguyên dữ liệu localStorage.');
    }catch(e){ console.error('Lỗi đọc database Excel (module6 - phiếu xuất):', e); }
    return false;
  }
  loadPxPhieuList();

  let pxPhieuCounter = 0;
  recomputePxPhieuCounter();

  function genPxMaPhieu(){
    pxPhieuCounter++;
    return 'PX' + String(pxPhieuCounter).padStart(4, '0');
  }

  function formatPxDate(iso){
    try{
      const d = new Date(iso);
      if(isNaN(d.getTime())) return iso || '';
      return d.toLocaleString('vi-VN', { day:'2-digit', month:'2-digit', year:'numeric', hour:'2-digit', minute:'2-digit' });
    }catch(e){ return iso || ''; }
  }

  // PC 0.32 — Phiếu Xuất: bảng "Chọn dòng để xuất phiếu" cao ĐÚNG BẰNG vùng nhìn của khung nội dung (hiện được nhiều dòng);
  // "Danh sách phiếu xuất" nằm ngay dưới, cuộn xuống để xem. Đo chiều cao thật của <main> rồi đặt biến --px-h (xem css cuối file).
  function fitPxLayout(){
    const sec = document.getElementById('tab-px');
    const mainEl = sec ? sec.parentElement : null;
    if(!mainEl) return;
    const cs = getComputedStyle(mainEl);
    const h = mainEl.clientHeight - (parseFloat(cs.paddingTop) || 0) - (parseFloat(cs.paddingBottom) || 0);
    const nav = document.getElementById('pxSubnav');
    const navH = nav && nav.offsetHeight ? nav.offsetHeight + 6 : 36;
    if(h > 160) sec.style.setProperty('--px-h', Math.floor(h - navH) + 'px');
  }
  window.addEventListener('resize', fitPxLayout);
  if(window.ResizeObserver){
    const pxMainEl = document.querySelector('main');
    if(pxMainEl) new ResizeObserver(fitPxLayout).observe(pxMainEl);
  }
  const pxJumpListBtn = document.getElementById('pxJumpListBtn');
  const pxJumpTopBtn  = document.getElementById('pxJumpTopBtn');
  if(pxJumpListBtn) pxJumpListBtn.addEventListener('click', ()=> setPxSub('ds'));
  if(pxJumpTopBtn)  pxJumpTopBtn.addEventListener('click', ()=> setPxSub('tao'));
  document.querySelectorAll('#pxSubnav [data-pxsub]').forEach(b=> b.addEventListener('click', ()=> setPxSub(b.dataset.pxsub)));
  const pxGroupBarEl = document.getElementById('pxGroupBar');
  if(pxGroupBarEl) pxGroupBarEl.addEventListener('click', (e)=>{
    const b = e.target.closest('[data-pxgrp]');
    if(!b) return;
    pxGroup = b.dataset.pxgrp;
    pxPage = 1;
    renderPxGroupBar();
    renderPxPage();   // dòng đã tick (pxSelected) được giữ nguyên khi đổi nhóm
  });
  function updatePxJumpLabel(){
    const pxJumpListBtn = document.getElementById('pxJumpListBtn');
    if(!pxJumpListBtn) return;
    pxJumpListBtn.textContent = 'Danh sách phiếu' + (pxPhieuList && pxPhieuList.length ? ' (' + pxPhieuList.length + ')' : '') + ' →';
  }

  function refreshPhieuXuatTab(){
    fitPxLayout();
    pxGroup = 'all';
    if(!persistedRows || !persistedRows.length){
      pxRows = [];
      pxIndexByRow = new Map();
      pxSelected = new Set();
      pxQuantities = {};
      pxPage = 1;
      toolbarLabelPX.textContent = 'Chưa có dữ liệu';
      tableScrollPX.innerHTML = '<div class="empty-table">Chưa có dữ liệu — hãy nạp dữ liệu ở menu "Load data" (nút "Nạp Dữ Liệu") trước.</div>';
      rowCountPX.textContent = '';
      summaryStatsPX.innerHTML = '';
      paginationPX.style.display = 'none';
      createPxBtn.disabled = true;
      renderPxList();
      renderPxGroupBar();
      setPxSub(pxSub);
      return;
    }
    pxRows = persistedRows;
    pxIndexByRow = new Map();
    pxRows.forEach((r,i)=>pxIndexByRow.set(r,i));
    pxSelected = new Set(); // nạp file mới -> đặt lại lựa chọn
    pxQuantities = {};
    pxPage = 1;
    pxSearchState = { searchFilters:{}, lastHeaderKey:null, bound:pxSearchState.bound, resizeBound:pxSearchState.resizeBound };
    resetTableCheckboxFilters('px');
    if(pxMaPhieuInput) pxMaPhieuInput.value = '';
    toolbarLabelPX.textContent = 'Chọn dòng để xuất phiếu';
    updatePxSummary();
    renderPxGroupBar();
    renderPxPage();
    renderPxList();
    setPxSub(pxSub);
  }

  // Trả về mảng OBJECT các dòng (không phải chỉ số) đã qua tìm kiếm + lọc cột + sắp xếp hiện tại
  // — dùng applyColumnSearch() dùng chung với mọi bảng khác trong module.
  function getPxFilteredRows(){
    let base = pxLiveRows(); // PC 2.66: bỏ dòng tồn = 0
    if(pxGroup !== 'all'){
      const set = pxGroupSets()[pxGroup];
      if(set) base = base.filter(r => set.has(r));
    }
    const hit = pxSmartHit(); if(hit) base = base.filter(r => hit(pxIndexByRow.get(r)));
    return applyColumnSearch(base, PX_EXPORT_COLS, pxSearchState.searchFilters, 'px');
  }
  // PC 2.87 — Ô tìm THÔNG MINH ở Tạo phiếu xuất (cùng bộ js/smart-search.js với trang Dữ Liệu): "BTP 50 MKS Nau", mã hàng, lô…
  let pxQ = '', pxSmartIdx = null;
  function pxSmartHit(){
    const S = window.KLSmartSearch; if(!S || !pxQ.trim() || !pxRows || !pxRows.length) return null;
    if(!pxSmartIdx || pxSmartIdx.rows !== pxRows || pxSmartIdx.len !== pxRows.length || pxSmartIdx.idx.version !== S.version()){
      const cols = TARGET_COLS.map(c => c.key);
      pxSmartIdx = { rows: pxRows, len: pxRows.length, idx: S.buildIndex(pxRows.length, i => cols.map(k => objColValue(pxRows[i], k)), i => [pxRows[i].maHang, pxRows[i].tenHang, pxRows[i].size]) };
    }
    return S.matcher(pxSmartIdx.idx, pxQ) || null;
  }
  (function(){
    const inp = document.getElementById('pxSmartSearch'); if(!inp) return;
    const run = KLAType.debounce(() => { pxPage = 1; renderPxPage(); }, 160);
    inp.addEventListener('input', () => { pxQ = inp.value; run(); });
    if(window.KLSmartSearch) window.KLSmartSearch.onDictReady(() => { pxSmartIdx = null; if(pxQ.trim() && pxRows && pxRows.length) renderPxPage(); });
  })();
  // Chỉ số (trong pxRows gốc) của các dòng đang hiển thị ở TRANG hiện tại — tính lại mỗi lần gọi
  // (không cache) để luôn khớp đúng trang/bộ lọc mới nhất, tránh closure giữ dữ liệu cũ.
  function getPxPageIdxs(){
    const filtered = getPxFilteredRows();
    const start = (pxPage - 1) * pxPageSize;
    return filtered.slice(start, start + pxPageSize).map(r => pxIndexByRow.get(r));
  }

  // PC 0.33: tổng theo DỮ LIỆU ĐANG LỌC (không cần tick chọn) — renderPxPage() tính lại mỗi lần lọc / đổi trang,
  // updatePxSummary() chỉ đọc lại (tránh lọc lại 1.900+ dòng mỗi lần gõ ô SL xuất).
  let pxLocTotals = { rows:0, kien:0, net:0 };
  function calcPxLocTotals(filteredObjs){
    let kien = 0, net = 0;
    filteredObjs.forEach(r => { kien += toNumber(r.soKien); net += toNumber(r.trongLuong); });
    pxLocTotals = { rows: filteredObjs.length, kien, net };
  }

  // Dòng TỔNG cuối bảng (tfoot): cộng cho từng cột số — Số kiện, SL xuất, Trọng lượng (kg) — trên TẤT CẢ dòng đang lọc
  // (mọi trang). SL xuất / Trọng lượng lấy đúng giá trị đang nhập ở ô SL xuất nên đổi theo khi gõ.
  let pxFootRows = [];
  function updatePxFoot(){
    const foot = document.getElementById('tableScrollPX-foot');
    if(!foot) return;
    let kien = 0, sl = 0, kg = 0;
    pxFootRows.forEach(r=>{
      const idx = pxIndexByRow.get(r);
      const qtyRaw = Object.prototype.hasOwnProperty.call(pxQuantities, idx) ? pxQuantities[idx] : pxDefQty(r);
      kien += toNumber(r.soKien);
      if(!(toNumber(r.soKien) <= 0 && toNumber(r.trongLuong) > 0)) sl += toNumber(qtyRaw); // dòng chỉ có kg: SL xuất là kg → không cộng vào SL kiện
      kg += pxRowKg(r, qtyRaw);
    });
    foot.innerHTML = '<tr>' +
      `<td colspan="${PX_EXPORT_COLS.length + 1}" class="total-label">Tổng (${formatNumber(pxFootRows.length)} dòng)</td>` +
      `<td class="num">${formatNumber(kien)}</td>` +
      `<td class="num">${formatNumber(sl)}</td>` +
      `<td class="num">${formatWeight(kg)}</td></tr>`;
  }

  function updatePxSummary(){
    let soKienSum = 0, trongLuongSum = 0;
    const maHangSet = new Set();
    pxSelected.forEach(idx=>{
      const r = pxRows[idx];
      if(!r) return;
      const qtyRaw = Object.prototype.hasOwnProperty.call(pxQuantities, idx) ? pxQuantities[idx] : pxDefQty(r);
      if(!(toNumber(r.soKien) <= 0 && toNumber(r.trongLuong) > 0)) soKienSum += toNumber(qtyRaw); // dòng chỉ có kg không cộng vào số kiện
      trongLuongSum += pxRowKg(r, qtyRaw);
      maHangSet.add(String(r.maHang ?? '').trim());
    });
    summaryStatsPX.innerHTML =
      `<span class="stat">Tổng số dòng<b>${formatNumber(pxLiveRows().length)}</b></span>` +
      `<span class="stat stat-loc" title="Cộng theo các dòng đang hiển thị sau khi lọc / tìm kiếm (tất cả các trang), không cần tick chọn">Dòng đang lọc<b>${formatNumber(pxLocTotals.rows)}</b></span>` +
      `<span class="stat stat-loc" title="Cộng cột Số kiện của các dòng đang lọc (tất cả các trang)">Tổng kiện đang lọc<b>${formatNumber(pxLocTotals.kien)}</b></span>` +
      `<span class="stat stat-loc" title="Cộng trọng lượng net (kg) của các dòng đang lọc (tất cả các trang)">Tổng net đang lọc (kg)<b>${formatWeight(pxLocTotals.net)}</b></span>` +
      `<span class="stat">Tổng dòng đã chọn<b>${formatNumber(pxSelected.size)}</b></span>` +
      `<span class="stat">Tổng mã đã chọn<b>${formatNumber(maHangSet.size)}</b></span>` +
      `<span class="stat">Tổng số kiện đã chọn<b>${formatNumber(soKienSum)}</b></span>` +
      `<span class="stat">Tổng trọng lượng đã chọn (kg)<b>${formatWeight(trongLuongSum)}</b></span>`;
    createPxBtn.disabled = pxSelected.size === 0;
  }

  function renderPxPage(){
    const filteredObjs = getPxFilteredRows();
    calcPxLocTotals(filteredObjs); // PC 0.33
    pxFootRows = filteredObjs;
    const total = filteredObjs.length;
    const totalPages = Math.max(1, Math.ceil(total / pxPageSize));
    if(pxPage > totalPages) pxPage = totalPages;
    if(pxPage < 1) pxPage = 1;
    const start = (pxPage - 1) * pxPageSize;
    const pageObjs = filteredObjs.slice(start, start + pxPageSize);

    if(!pxRows.length){
      tableScrollPX.innerHTML = '<div class="empty-table">Chưa có dữ liệu — hãy nạp file ở menu "Load data" trước.</div>';
    } else {
      const sortState = getColumnSort('px');
      const sortKeyStr = sortState ? sortState.idx + ':' + sortState.dir : '';
      // SỬA LỖI MẤT FOCUS KHI GÕ LIÊN TỤC: headerKey TRƯỚC ĐÂY có JSON.stringify(searchFilters)
      // -> giá trị vừa gõ đổi mỗi phím -> headerKey đổi mỗi phím -> vẽ lại TOÀN BỘ <thead> (kể cả
      // các ô input tìm kiếm) mỗi phím -> input bị thay bằng phần tử DOM mới, mất focus, người
      // dùng phải bấm lại vào ô mới gõ được ký tự tiếp theo. Đúng ra header chỉ cần vẽ lại khi
      // đổi SẮP XẾP hoặc đổi CỘT (giống renderTable()/renderGenericTable() ở trên đã làm đúng) —
      // còn giá trị gõ trong ô tìm kiếm được cập nhật tại chỗ (class "has-value") ngay trong sự
      // kiện 'input' bên dưới, không cần vẽ lại header.
      const headerKey = sortKeyStr + '|' + PX_EXPORT_COLS.map(c=>c.label).join('||');
      if(headerKey !== pxSearchState.lastHeaderKey){
        let html = '<table class="filtered-table px-table"><thead><tr>' +
          '<th style="width:36px;text-align:center;"><input type="checkbox" id="pxCheckAll"></th>' +
          '<th style="width:56px;">STT</th>';
        PX_EXPORT_COLS.forEach((c,i)=>{
          const activeSort = sortState && sortState.idx === i ? (sortState.dir === 1 ? ' sort-asc' : ' sort-desc') : '';
          const hasFilter = (pxSearchState.searchFilters[i] && pxSearchState.searchFilters[i].trim() !== '') || isColumnFilterActive('px', i);
          html += `<th class="sort-th${activeSort}${hasFilter ? ' has-filter' : ''}" data-col-index="${i}"${c.w ? ` style="width:${c.w}px;"` : ''}><span class="th-label" title="Nhấp để sắp xếp">${escapeHtml(c.label)}</span></th>`;
        });
        html += '<th style="width:110px;">SL xuất</th><th style="width:140px;">Trọng lượng (kg)</th></tr><tr class="search-row"><th></th><th></th>';
        PX_EXPORT_COLS.forEach((c,i)=>{
          const val = pxSearchState.searchFilters[i] || '';
          const listId = `tableScrollPX-dl-${i}`;
          const cascaded = applyColumnSearch(pxLiveRows(), PX_EXPORT_COLS, pxSearchState.searchFilters, 'px', i);
          const options = buildSuggestionsForColumnObj(cascaded, c.key).map(v => `<option value="${escapeHtml(v)}">`).join('');
          const filterActive = isColumnFilterActive('px', i);
          html += `<th class="${val.trim() ? 'has-value' : ''}">` +
            `<div class="col-search-cell">` +
              `<input type="text" class="col-search-input" list="${listId}" data-col-index="${i}" placeholder="Tìm ${escapeHtml(c.label)}…" value="${escapeHtml(val)}">` +
              `<button type="button" class="col-filter-btn${filterActive ? ' active' : ''}" data-col-index="${i}" title="Lọc theo giá trị">▾</button>` +
            `</div><datalist id="${listId}">${options}</datalist></th>`;
        });
        html += '<th></th><th></th></tr></thead><tbody id="tableScrollPX-body"></tbody><tfoot id="tableScrollPX-foot"></tfoot></table>';
        tableScrollPX.innerHTML = html;
        pxSearchState.lastHeaderKey = headerKey;

        if(!pxSearchState.bound){
          tableScrollPX.addEventListener('input', (e)=>{
            const el = e.target;
            if(el.classList && el.classList.contains('col-search-input')){
              const idx = parseInt(el.getAttribute('data-col-index'), 10);
              const th = el.closest('th');
              if(th) th.classList.toggle('has-value', !!el.value.trim());
              pxSearchState.searchFilters[idx] = el.value;
              const mainTh = tableScrollPX.querySelector(`thead tr:first-child th[data-col-index="${idx}"]`);
              if(mainTh) mainTh.classList.toggle('has-filter', !!el.value.trim() || isColumnFilterActive('px', idx));
              pxPage = 1;
              // PC 0.19: chờ ngừng gõ 160ms mới vẽ lại (trước đây vẽ lại mỗi phím → giật trên máy yếu)
              (tableScrollPX.__klaPxRun || (tableScrollPX.__klaPxRun = KLAType.debounce(()=>KLAType.keepFocus(renderPxPage), 160)))();
              return;
            }
            if(el.classList && el.classList.contains('px-qty-input')){
              const idx = parseInt(el.getAttribute('data-idx'), 10);
              pxQuantities[idx] = el.value;
              updatePxSummary();
              const kgCell = tableScrollPX.querySelector(`.px-kg-cell[data-idx="${idx}"]`);
              if(kgCell) kgCell.textContent = formatWeight(pxRowKg(pxRows[idx], el.value));
              updatePxFoot();
            }
          });
          tableScrollPX.addEventListener('click', (e)=>{
            // ĐỒNG BỘ (2026-09): chỉ bắt sự kiện sắp xếp khi bấm ĐÚNG phần chữ tiêu đề (.th-label),
    // không phải cả ô <th> — trước đây bấm bất kỳ đâu trong ô tiêu đề đều kích hoạt sắp xếp.
    const sortLabel = e.target.closest('th.sort-th .th-label');
    const sortTh = sortLabel ? sortLabel.closest('th.sort-th') : null;
            if(sortTh){
              const idx = parseInt(sortTh.getAttribute('data-col-index'), 10);
              toggleColumnSort('px', idx);
              pxSearchState.lastHeaderKey = null;
              renderPxPage();
              return;
            }
            const filterBtn = e.target.closest('.col-filter-btn');
            if(filterBtn){
              const idx = parseInt(filterBtn.getAttribute('data-col-index'), 10);
              const colKey = PX_EXPORT_COLS[idx].key;
              const cascadedRows = applyColumnSearch(pxLiveRows(), PX_EXPORT_COLS, pxSearchState.searchFilters, 'px', idx);
              const values = cascadedRows.map(r => objColValue(r, colKey));
              openColumnFilterDropdown(filterBtn, 'px', idx, PX_EXPORT_COLS[idx].label, values, ()=>{
                pxSearchState.lastHeaderKey = null;
                renderPxPage();
              });
              return;
            }
          });
          tableScrollPX.addEventListener('change', (e)=>{
            const cb = e.target;
            if(!cb.classList || !cb.classList.contains('px-row-check')) return;
            const idx = parseInt(cb.getAttribute('data-idx'), 10);
            if(cb.checked) pxSelected.add(idx); else { pxSelected.delete(idx); delete pxQuantities[idx]; }
            updatePxSummary();
            const checkAll = document.getElementById('pxCheckAll');
            if(checkAll){
              const rowChecks = tableScrollPX.querySelectorAll('.px-row-check');
              const allChecked = Array.from(rowChecks).every(c => c.checked);
              const anyChecked = Array.from(rowChecks).some(c => c.checked);
              checkAll.checked = allChecked;
              checkAll.indeterminate = !allChecked && anyChecked;
            }
          });
          pxSearchState.bound = true;
        }
        if(!pxSearchState.resizeBound){
          enableCellResize(tableScrollPX, 'px');
          pxSearchState.resizeBound = true;
        }
      }

      const tbody = document.getElementById('tableScrollPX-body');
      if(tbody){
        if(pageObjs.length){
          let body = '';
          pageObjs.forEach((r, ri)=>{
            const idx = pxIndexByRow.get(r);
            const stt = start + ri + 1;
            const checked = pxSelected.has(idx) ? 'checked' : '';
            const qty = Object.prototype.hasOwnProperty.call(pxQuantities, idx) ? pxQuantities[idx] : String(pxDefQty(r) ?? '');
            const kg = pxRowKg(r, qty);
            body += '<tr>' +
              `<td style="text-align:center;"><input type="checkbox" class="px-row-check" data-idx="${idx}" ${checked}></td>` +
              `<td>${stt}</td>` +
              PX_EXPORT_COLS.map(c => `<td>${escapeHtml(String(objColValue(r, c.key) ?? ''))}</td>`).join('') +
              `<td><input type="text" inputmode="decimal" class="px-qty-input" data-idx="${idx}" value="${escapeHtml(qty)}"></td>` +
              `<td class="px-kg-cell" data-idx="${idx}" style="text-align:right;">${formatWeight(kg)}</td>` +
              '</tr>';
          });
          tbody.innerHTML = body;
        } else {
          const hasActiveFilter = Object.values(pxSearchState.searchFilters).some(v => v && v.trim() !== '');
          tbody.innerHTML = `<tr><td colspan="${PX_EXPORT_COLS.length + 4}" style="text-align:center;color:var(--text-low);padding:24px;font-family:'JetBrains Mono',monospace;font-size:12px;">` +
            (hasActiveFilter ? 'Không có dòng nào khớp với tìm kiếm.' : 'Không có dòng nào còn tồn để xuất.') +
            '</td></tr>';
        }
        applyStoredSizes(tableScrollPX.querySelector('table'), 'px');

        const checkAll = document.getElementById('pxCheckAll');
        if(checkAll){
          const pageIdxsNow = pageObjs.map(r => pxIndexByRow.get(r));
          const pageAllSelected = pageIdxsNow.length > 0 && pageIdxsNow.every(idx => pxSelected.has(idx));
          checkAll.checked = pageAllSelected;
          checkAll.indeterminate = !pageAllSelected && pageIdxsNow.some(idx => pxSelected.has(idx));
          if(!checkAll.dataset.bound){
            checkAll.dataset.bound = '1';
            checkAll.addEventListener('change', ()=>{
              const curPageIdxs = getPxPageIdxs(); // luôn lấy lại đúng trang hiện tại, tránh dữ liệu cũ
              if(checkAll.checked){ curPageIdxs.forEach(idx => pxSelected.add(idx)); }
              else { curPageIdxs.forEach(idx => { pxSelected.delete(idx); delete pxQuantities[idx]; }); }
              updatePxSummary();
              renderPxPage();
            });
          }
        }
      }
    }

    if(total > 0){
      const from = start + 1;
      const to = Math.min(start + pxPageSize, total);
      rowCountPX.textContent = `${formatNumber(total)} dòng (${from}–${to})`;
    } else {
      rowCountPX.textContent = '';
    }
    pageIndicatorPX.textContent = 'Trang ' + pxPage + '/' + totalPages;
    prevPageBtnPX.disabled = pxPage <= 1;
    nextPageBtnPX.disabled = pxPage >= totalPages;
    paginationPX.style.display = total > 0 ? 'flex' : 'none';
    updatePxFoot();
    if(pxRows.length) updatePxSummary(); // PC 0.33: cập nhật tổng kiện / net theo dữ liệu đang lọc
  }

  pageSizeSelectPX.addEventListener('change', ()=>{
    pxPageSize = parseInt(pageSizeSelectPX.value, 10);
    pxPage = 1;
    renderPxPage();
  });
  prevPageBtnPX.addEventListener('click', ()=>{ pxPage--; renderPxPage(); });
  nextPageBtnPX.addEventListener('click', ()=>{ pxPage++; renderPxPage(); });

  selectAllBtnPX.addEventListener('click', ()=>{
    getPxFilteredRows().forEach(r => pxSelected.add(pxIndexByRow.get(r)));
    updatePxSummary();
    renderPxPage();
  });
  selectNoneBtnPX.addEventListener('click', ()=>{
    pxSelected.clear();
    pxQuantities = {};
    updatePxSummary();
    renderPxPage();
  });

  // ==== Cơ chế TRỪ / HOÀN dữ liệu "Dữ Liệu" dùng chung cho Tạo / Sửa / Hủy phiếu xuất ====
  // Khớp 1 dòng persistedRows với 1 item trong phiếu xuất bằng "khóa" Mã hàng + Số lô + Size +
  // Phiếu nhập — đây là tổ hợp xác định 1 lô/dòng cụ thể trong "Dữ Liệu". Dùng khóa (thay vì chỉ
  // số index) vì sau khi Tạo/Sửa/Hủy nhiều phiếu, vị trí index trong mảng có thể không còn khớp
  // đúng dòng ban đầu như lúc tạo phiếu.
  function pxRowMatchKey(o){
    return [o.maHang, o.soLo, o.size, o.phieuNhap].map(v => String(v ?? '').trim()).join('|');
  }
  // PC 5.10: trước đây Map.set ghi đè nên 2 dòng TRÙNG KHÓA luôn bị trừ / hoàn vào dòng SAU. Nay mỗi khóa giữ MẢNG các dòng
  // theo thứ tự xuất hiện; item phiếu mới ghi thêm `occ` (thứ tự trong nhóm) để trừ / hoàn đúng dòng đã chọn.
  // Phiếu cũ không có occ → giữ cách cũ (dòng cuối nhóm) để hoàn lại đúng dòng đã bị trừ trước đây.
  function buildPersistedRowIndex(){
    const map = new Map();
    persistedRows.forEach(r => { const k = pxRowMatchKey(r); const a = map.get(k); if(a) a.push(r); else map.set(k, [r]); });
    return map;
  }
  function pxPickRow(idxMap, it){
    const arr = idxMap.get(pxRowMatchKey(it));
    if(!arr || !arr.length) return null;
    const o = it && it.occ;
    if(typeof o === 'number' && isFinite(o) && arr[o]) return arr[o];
    return arr[arr.length - 1];
  }
  function pxRowOcc(idxMap, r){
    const arr = idxMap.get(pxRowMatchKey(r)) || [];
    const i = arr.indexOf(r);
    return i < 0 ? 0 : i;
  }
  // PC 5.10: dòng chỉ có kg (số kiện = 0, kg > 0) — xuất THEO KG: item.xuatKg = số kg xuất, soLuongXuat = 0 (các báo cáo
  // tính kiện không bị lẫn đơn vị), trongLuongTru = xuatKg nên trừ / hoàn kg dùng đúng đường cũ.
  function pxIsKgOnly(r){ return toNumber(r.soKien) <= 0 && toNumber(r.trongLuong) > 0; }
  // sign = -1 khi TRỪ (tạo phiếu / áp lại số lượng mới khi sửa), +1 khi HOÀN (hủy phiếu / hoàn số
  // lượng cũ khi sửa). Dòng gốc không còn tìm thấy (VD Load Data đã bị nạp lại bằng file khác) thì
  // bỏ qua item đó, không báo lỗi cả phiếu.
  function applyPxItemsDelta(items, sign){
    if(!items || !items.length) return;
    const idxMap = buildPersistedRowIndex();
    items.forEach(it=>{
      const r = pxPickRow(idxMap, it);
      if(!r) return;
      r.soKien = toNumber(r.soKien) + sign * toNumber(it.soLuongXuat);
      r.trongLuong = toNumber(r.trongLuong) + sign * toNumber(it.trongLuongTru || 0);
    });
  }
  // Tính lại toàn bộ cờ "_pxDaXuat" (tô đỏ ở tab Dữ Liệu) dựa trên TOÀN BỘ danh sách phiếu xuất
  // hiện có — thay vì tự tăng/giảm dần cờ này theo từng thao tác, để luôn đúng dù Tạo/Sửa/Hủy
  // bao nhiêu lần cũng không bị lệch.
  function recomputePxDaXuatFlags(){
    persistedRows.forEach(r => { r._pxDaXuat = false; });
    const idxMap = buildPersistedRowIndex();
    pxPhieuList.forEach(p=>{
      if(!pxIsActive(p)) return; // PC 1.69: phiếu của đợt số liệu cũ không còn đánh dấu dòng của số liệu mới
      (p.items || []).forEach(it=>{
        if(!toNumber(it.soLuongXuat) && !toNumber(it.xuatKg)) return;
        const r = pxPickRow(idxMap, it);
        if(r) r._pxDaXuat = true;
      });
    });
  }
  // Làm mới TẤT CẢ các tab phụ thuộc "Dữ Liệu" (Ecuador/Ấn Độ/Gia Công/Giám Sát/Nguyên Liệu/HLSO,
  // Đặc Tính, Báo Cáo, Đối Soát) — dùng chung sau khi Tạo/Sửa/Hủy phiếu xuất làm thay đổi persistedRows.
  function refreshAllTabsAfterPxChange(){
    refreshDuLieuTab();
    refreshEcuadorTab();
    refreshAnDoTab();
    refreshTGCTab();
    refreshTGSTab();
    refreshNguyenLieuTab();
    refreshHLSOTab();
    refreshDacTinhTab();
    refreshBaoCaoTab();
    refreshDoiSoatTab();
    // Làm mới lại bảng trên của tab "Phiếu Xuất" (Số kiện hiển thị) mà KHÔNG reset trang/tìm kiếm.
    renderPxGroupBar(); // PC 2.66: số đếm nhóm theo dòng còn tồn đổi sau khi tạo / sửa / xóa phiếu
    renderPxPage();
    updatePxSummary();
  }

  // PC 5.09 — KIỂM SỐ LƯỢNG XUẤT trước khi tạo / sửa phiếu. Trước đây ô SL xuất là chữ tự do và không bị chặn: để trống / chữ → 0,
  // số ÂM → CỘNG thêm tồn, lớn hơn tồn → tồn bị ÂM. problems = [{ label, why }]; trả về chuỗi báo lỗi hoặc '' nếu hợp lệ.
  function pxQtyProblems(list){
    const bad = [];
    list.forEach(({ label, qtyRaw, avail }) => {
      const q = toNumber(qtyRaw), empty = String(qtyRaw ?? '').trim() === '';
      if(empty || !isFinite(q)) bad.push(`• ${label} — SL xuất đang TRỐNG / không phải số`);
      else if(q <= 0) bad.push(`• ${label} — SL xuất = ${formatNumber(q)} (phải > 0)`);
      else if(avail !== null && q > avail + 1e-9) bad.push(`• ${label} — SL xuất ${formatNumber(q)} VƯỢT tồn (${formatNumber(avail)})`);
    });
    if(!bad.length) return '';
    return `Có ${bad.length} dòng SL xuất chưa hợp lệ:\n\n` + bad.slice(0, 12).join('\n') + (bad.length > 12 ? `\n… và ${bad.length - 12} dòng khác` : '') + '\n\nHãy sửa rồi thử lại.';
  }

  // ---- Tạo phiếu xuất mới từ các dòng đang được tick chọn ----
  createPxBtn.addEventListener('click', async ()=>{
    if(!pxSelected.size) return;
    { // PC 5.09: chặn SL xuất trống / ≤ 0 / vượt số kiện đang tồn
      const msg = pxQtyProblems(Array.from(pxSelected).sort((a,b)=> a-b).map(idx => {
        const r = pxRows[idx]; if(!r) return { label: '(dòng không còn)', qtyRaw: '', avail: 0 };
        const kgOnly = pxIsKgOnly(r); // PC 5.10: dòng chỉ có kg → SL xuất tính theo KG
        return { label: `${r.maHang || ''}${r.soLo ? ' · Lô ' + r.soLo : ''}${r.size ? ' · ' + r.size : ''}`.trim() + (kgOnly ? ' (xuất theo kg)' : '') || `Dòng ${idx + 1}`,
          qtyRaw: Object.prototype.hasOwnProperty.call(pxQuantities, idx) ? pxQuantities[idx] : (kgOnly ? r.trongLuong : r.soKien),
          avail: kgOnly ? toNumber(r.trongLuong) : toNumber(r.soKien) };
      }));
      if(msg){ alert(msg); return; }
    }
    const ok = await showConfirmDialog({
      title: 'Xác nhận tạo phiếu xuất',
      message: `Tạo phiếu xuất mới với ${pxSelected.size} dòng hàng đã chọn?`,
      confirmText: 'Tạo phiếu'
    });
    if(!ok) return;
    const selectedIdxs = Array.from(pxSelected).sort((a,b)=> a-b); // giữ đúng thứ tự dữ liệu gốc
    const occIdxMap = buildPersistedRowIndex();
    const items = selectedIdxs.map(idx=>{
      const r = pxRows[idx];
      const kgOnly = pxIsKgOnly(r);
      const qtyRaw = Object.prototype.hasOwnProperty.call(pxQuantities, idx) ? pxQuantities[idx] : (kgOnly ? r.trongLuong : r.soKien);
      const qty = toNumber(qtyRaw);
      const soKienGoc = toNumber(r.soKien);
      const trongLuongGoc = toNumber(r.trongLuong);
      // Trọng lượng (KG) tương ứng bị trừ cho ĐÚNG dòng này — lưu lại trong item để HOÀN LẠI
      // chính xác khi hủy/sửa phiếu sau này (không cần tính lại theo tỉ lệ hiện tại, vốn có thể
      // đã đổi do các phiếu xuất khác).
      const trongLuongTru = kgOnly ? qty : (soKienGoc > 0 ? trongLuongGoc * (qty / soKienGoc) : 0);
      const it = {
        maHang:      r.maHang ?? '',
        tenHang:     r.tenHang ?? '',
        phieuNhap:   r.phieuNhap ?? '',
        soLo:        r.soLo ?? '',
        size:        r.size ?? '',
        soKien:      r.soKien ?? '',
        soLuongXuat: kgOnly ? 0 : qty,
        trongLuongTru: trongLuongTru,
        occ:         pxRowOcc(occIdxMap, r)
      };
      if(kgOnly) it.xuatKg = qty;
      return it;
    });

    let maPhieu = (pxMaPhieuInput.value || '').trim();
    const pxOff = m2Offline(); // PC 3.24
    if(!maPhieu && pxOff){
      maPhieu = 'PXT-' + Date.now().toString(36).toUpperCase() + Math.random().toString(36).slice(2, 5).toUpperCase(); // mã TẠM — máy chủ cấp mã PX… khi gộp
    } else if(!maPhieu){
      maPhieu = genPxMaPhieu();
    } else {
      const base = maPhieu;
      let n = 1;
      while(pxPhieuList.some(p => p.maPhieu === maPhieu)){
        n++; maPhieu = base + '-' + n;
      }
    }

    const newPx = {
      id: 'px_' + Date.now() + '_' + Math.random().toString(36).slice(2, 7),
      maPhieu,
      ngayTao: new Date().toISOString(),
      loadAt: pxLoadStamp(), loadDate: m2Meta.dataDate || '',
      items
    };
    pxPhieuList.unshift(newPx);
    if(pxOff){ // PC 3.24: máy chủ tắt → gửi lệnh tạo phiếu thay vì ghi cả sheet
      const okQ = await m2QueuePhieu({ act: 'create', slip: { id: newPx.id, maPhieu: newPx.maPhieu, ngayTao: newPx.ngayTao, nguoiTao: '', items: items.map(i => Object.assign({}, i)) } });
      if(!okQ){ pxPhieuList = pxPhieuList.filter(p => p !== newPx); return; }
    } else savePxPhieuList();

    // ---- Tạo phiếu xuất THÀNH CÔNG -> trừ ngay vào "Số kiện" VÀ "Trọng lượng (KG)" của đúng
    // dòng trong "Dữ Liệu", đánh dấu "_pxDaXuat" để tô ĐỎ, rồi làm mới toàn bộ các tab phụ thuộc.
    applyPxItemsDelta(items, -1);
    recomputePxDaXuatFlags();
    if(!pxOff) savePersistedRows(); // PC 3.24: máy chủ tắt → bản sao đã được main cập nhật theo lệnh phiếu
    refreshAllTabsAfterPxChange();

    // Sau khi tạo phiếu, đặt lại lựa chọn ở bảng trên (KHÔNG refresh toàn bộ tab "Phiếu Xuất" để
    // giữ nguyên trang/tìm kiếm hiện tại đang xem).
    pxSelected.clear();
    pxQuantities = {};
    pxMaPhieuInput.value = '';
    updatePxSummary();
    renderPxPage();
    renderPxList();
    setPxSub('ds'); // PC 1.70: phiếu vừa tạo hiện ở menu con "Danh sách phiếu xuất"
  });

  // ---- Danh sách phiếu xuất: hiển thị + xuất Excel / sửa / xóa từng phiếu ----
  function renderPxList(){
    updatePxJumpLabel();
    if(!pxListScroll) return;
    if(!pxPhieuList.length){
      pxListScroll.innerHTML = '<div class="empty-table">Chưa có phiếu xuất nào được tạo.</div>';
      if(pxListCount) pxListCount.textContent = '';
      return;
    }
    if(pxListCount) pxListCount.textContent = formatNumber(pxPhieuList.length) + ' phiếu';

    let html = '<table class="px-table" data-kcf data-kcf-key="m2:phieuxuat" data-kl-sort="m2:phieuxuat"><thead><tr>' +
      '<th style="width:50px;">STT</th>' +
      '<th>Mã phiếu</th>' +
      '<th>Ngày tạo</th>' +
      '<th style="width:150px;">Theo tồn ngày</th>' +
      '<th style="width:190px;">Trạng thái</th>' +
      '<th style="width:90px;">Số dòng</th>' +
      '<th style="width:120px;">Tổng SL xuất</th>' +
      '<th style="width:230px;" data-kl-nosort>Thao tác</th>' +
      '</tr></thead><tbody>';
    pxPhieuList.forEach((p, i)=>{
      const items = p.items || [];
      const tongSL = items.reduce((s, it)=> s + toNumber(it.soLuongXuat), 0);
      const act = pxIsActive(p);
      const ldTxt = pxLoadDateText(p);
      html += `<tr${act ? '' : ' class="px-locked"'}>` +
        `<td>${i + 1}</td>` +
        `<td>${escapeHtml(String(p.maPhieu || ''))}${p.nguon ? ` <span title="${escapeHtml('Gửi từ ' + p.nguon + (p.nguoiTao ? ' — ' + p.nguoiTao : '') + (p.ghiChu ? ' — ' + p.ghiChu : ''))}">📱</span>` : ''}</td>` +
        `<td>${escapeHtml(formatPxDate(p.ngayTao))}</td>` +
        `<td>${escapeHtml(ldTxt || '—')}</td>` +
        `<td>${act
          ? '<span class="px-st px-st-on" title="Phiếu đã trừ vào số liệu tồn đang dùng. Xóa / sửa phiếu sẽ hoàn / điều chỉnh lại tồn.">● Đang trừ tồn</span>'
          : `<span class="px-st px-st-off" title="${escapeHtml('Số liệu tồn đã được nạp lại' + (ldTxt ? ' (phiếu này tính theo số liệu ngày ' + ldTxt + ')' : '') + ' nên phiếu không còn tác dụng, không trừ vào số liệu mới.')}">🔒 Đã khóa — tồn đã cập nhật</span>`}</td>` +
        `<td>${formatNumber(items.length)}</td>` +
        `<td>${formatNumber(tongSL)}</td>` +
        `<td><div style="display:flex; gap:6px; flex-wrap:wrap;">` +
          `<button type="button" class="px-list-action" data-action="view" data-id="${p.id}">👁 Xem</button>` +
          `<button type="button" class="px-list-action" data-action="export" data-id="${p.id}">↓ Xuất Excel</button>` +
          (act
            ? `<button type="button" class="px-list-action" data-action="edit" data-id="${p.id}">✎ Sửa</button>`
            : `<button type="button" class="px-list-action" disabled title="Phiếu đã khóa vì số liệu tồn đã được cập nhật — không sửa được">✎ Sửa</button>`) +
          `<button type="button" class="px-list-action danger" data-action="delete" data-id="${p.id}">🗑 Xóa</button>` +
        `</div></td>` +
        '</tr>';
    });
    html += '</tbody></table>';
    pxListScroll.innerHTML = html;
  }

  pxListScroll.addEventListener('click', (e)=>{
    const btn = e.target.closest('.px-list-action');
    if(!btn) return;
    const id = btn.getAttribute('data-id');
    const action = btn.getAttribute('data-action');
    const phieu = pxPhieuList.find(p => p.id === id);
    if(!phieu) return;
    if(action === 'view') openPxViewModal(phieu);
    else if(action === 'export') exportPxPhieuToExcel(phieu);
    else if(action === 'edit') openPxEditModal(phieu);
    else if(action === 'delete') deletePxPhieu(id);
  });

  // Logo Long An Logistics dùng để chèn vào góc trên-trái file Excel phiếu xuất (base64 PNG)
  const PX_LOGO_BASE64 = "iVBORw0KGgoAAAANSUhEUgAAAosAAACxCAYAAACslPf1AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAAFxEAABcRAcom8z8AAP+lSURBVHhe7L0FYB3Xmf5tlMwQbprStrvdbv+L3f122922ScyyZZBkxjC2SZk5YEYx2WEyMzODLJZly8xs4b1XV9LzPc87d+RrRU4cbJrMsR8N3IFDM+c376EmdQA8efLkyZMnT548eWpMHix68uTJkydPnjx5uq48WPTkyZMnT548efJ0XXmw6MmTJ0+ePHny5Om68mDRkydPnjx58uTJ03XlwaInT548efLkyZOn68qDRU+ePHny5MmTJ0/XlQeLnjx58uTJkydPnq4rDxY9efLkyZMnT548XVceLHry5MmTJ0+ePHm6rjxY9OTJkydPnjx58nRdebDoyZMnT548efLk6bryYNGTJ0+ePHny5MnTdeXBoidPnjx58uTJk6fryoNFT548efLkyZMnT9eVB4uePHny5MmTJ0+erisPFj158uTJkydPnjxdVx4sevLkyZMnT548ebquPFj05MmTJ0+ePHnydF15sOjJkydPnjx58uTpuvJg0ZMnT548efLkydN15cGiJ0+ePHny5MmTp+vKg0VPnjx58uTJkydP15UHi548efLkyZMnT56uKw8WPX0gua6WW1Jjx3jy5MmTJ0+e/nblweJnTI25xo57J4WDYWOQqO2aulqT+3v4MbX848rd58mTJ0+ePHn6ZMqDxc+YGnPh8BYOcQ2dcK+mrg5BQqB0FQpdcARquKbftGwIiXLuevh9PHny5MmTJ0+fXHmw+FkT/zTcZ+DGP1KdwI/UV1sPhVzymCCPrCYCVtdVI1gbQLCGW7VCQgcIa/i3pjaImmAANfwtWCsFeZ4Djg5ECi5DkOjePOScTRcw3W1Pnjx58uTJ019bHix+SuT8cRY1lAN49bsdp40QiQnWAjWuhRCoNtAj2tXUoKaaChL2+FsVD67k76UEwEpCYnWtn/LBXxOAnxep4G9XeIVKHmfQSEisrvHDH6xCgOsBwmU1jw1wX3VdjV1Tlsha3lRQaqJfHEjk76ar/n+7BK/OceFWS0+ePHny5MnTRyMPFj8lcv44i+vCIreEWrLzydLnr6FIan7+UsUDfVxKFYQ6X00FfHUVqCTsldXW4Aoh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";

  function exportPxPhieuToExcel(phieu){
    buildPxPhieuWorkbook(phieu).then(buffer=>{
      const blob = new Blob([buffer], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = (String(phieu.maPhieu || '').replace(/[\\/:*?"<>|\r\n]+/g, ' ').replace(/\s+/g, ' ').trim() || 'phieu-xuat') + '.xlsx'; // PC 3.28: dùng ĐÚNG mã phiếu người dùng đặt, không thêm tiền tố
      document.body.appendChild(a);
      a.click();
      a.remove();
      setTimeout(()=> URL.revokeObjectURL(url), 1000);
    }).catch(err=>{
      console.error(err);
      alert('Có lỗi khi xuất file Excel: ' + err.message);
    });
  }

  // ---- Tạo file Excel "Phiếu yêu cầu xuất hàng" đúng theo mẫu PYC (font, viền, merge, công thức TỔNG...) ----
  async function buildPxPhieuWorkbook(phieu){
    await klNap('exceljs'); // PC 0.19: nạp lười
    const items = phieu.items || [];
    const wb = new ExcelJS.Workbook();
    const ws = wb.addWorksheet('PYC', {
      pageSetup: { orientation: 'portrait', margins: { left:0.7, right:0.7, top:0.75, bottom:0.75, header:0.3, footer:0.3 } }
    });

    // Độ rộng cột sẽ được TỰ TÍNH theo đúng dữ liệu thực tế của phiếu này (xem đoạn tính toán
    // ở cuối hàm, sau khi đã có đủ items/headers/dòng TỔNG) — không dùng độ rộng cố định như
    // mẫu cũ nữa (mỗi phiếu có mã hàng/tên hàng dài ngắn khác nhau, cố định 1 độ rộng chung dễ
    // bị dư thừa hoặc quá hẹp so với giá trị thật).

    const THIN = { style: 'thin' };
    const GRID_BORDER = { top: THIN, bottom: THIN, left: THIN, right: THIN };
    const FONT_BASE = { name: 'Times New Roman', size: 16 };
    const QTY_NUMFMT = '_(* #,##0_);_(* \\(#,##0\\);_(* "-"??_);_(@_)';

    // ---- Logo công ty (góc trên - trái, không đè lên tiêu đề) ----
    // SỬA: logo cũ quá nhỏ (150x41) so với bề rộng thật của bảng — phóng to lên (giữ đúng tỉ lệ
    // ảnh gốc ~3.66:1) và tăng chiều cao 2 dòng đầu tương ứng để logo không bị tràn ra ngoài.
    ws.getRow(1).height = 24;
    ws.getRow(2).height = 24;
    const logoImageId = wb.addImage({ base64: PX_LOGO_BASE64, extension: 'png' });
    ws.addImage(logoImageId, { tl: { col: 0, row: 0 }, ext: { width: 230, height: 63 } });

    // ---- Tiêu đề (dòng 3, giống mẫu) ----
    // SỬA LỖI CĂN GIỮA: trước đây gộp "B3:H3" trong khi bảng dữ liệu + dòng ghi chú (A6:G6)
    // đều chỉ trải từ cột A đến G — lệch mất 1 cột (thiếu cột A, thừa cột H rộng) khiến khung
    // tiêu đề không thẳng hàng với bảng, nhìn như bị lệch sang 1 bên. Nay gộp đúng "A3:G3" cho
    // khớp với đúng bề rộng thật của bảng.
    ws.mergeCells('A3:G3');
    const titleCell = ws.getCell('A3');
    titleCell.value = 'PHIẾU YÊU CẦU XUẤT HÀNG';
    titleCell.font = { ...FONT_BASE, bold: true };
    titleCell.alignment = { horizontal: 'center' };

    // ---- Ghi chú hướng dẫn (dòng 6, đỏ, đậm, giống mẫu) ----
    ws.mergeCells('A6:G6');
    const noteCell = ws.getCell('A6');
    noteCell.value = 'XUẤT HÀNG THEO THỨ TỰ PHIẾU YÊU CẦU - GHI RÕ THÔNG TIN SIZE, QUY CÁCH, GHI CHÚ TRÊN CARGO';
    noteCell.font = { ...FONT_BASE, bold: true, color: { argb: 'FFFF0000' } };
    noteCell.alignment = { horizontal: 'center', vertical: 'center' };

    // ---- Dòng tiêu đề bảng (dòng 7, giống mẫu) ----
    const headerRowIdx = 7;
    const headers = ['STT', 'Mã hàng', 'Tên hàng', 'Số phiếu nhập', 'GHI CHÚ - LÔ ( IN TRÊN THÙNG )', 'Kích cỡ/size', 'Số lượng \n(thùng/bao)'];
    const headerRow = ws.getRow(headerRowIdx);
    headerRow.height = 37.5;
    headers.forEach((h, i)=>{
      const cell = headerRow.getCell(i+1);
      cell.value = h;
      cell.font = { ...FONT_BASE, bold: true };
      cell.alignment = { horizontal: 'center', vertical: 'center', wrapText: true };
      cell.border = GRID_BORDER;
    });

    // ---- Các dòng dữ liệu ----
    const firstDataRow = headerRowIdx + 1;
    items.forEach((it, i)=>{
      const r = ws.getRow(firstDataRow + i);
      const vals = [i + 1, it.maHang ?? '', it.tenHang ?? '', it.phieuNhap ?? '', it.soLo ?? '', it.size ?? '', (it.xuatKg ? formatNumber(toNumber(it.xuatKg)) + ' kg' : toNumber(it.soLuongXuat))];
      vals.forEach((v, ci)=>{
        const cell = r.getCell(ci+1);
        cell.value = v;
        cell.font = { ...FONT_BASE };
        cell.border = GRID_BORDER;
        if(ci === 0){ cell.alignment = { horizontal: 'center' }; }
        else if(ci === 1 || ci === 2 || ci === 3 || ci === 5){ cell.alignment = { horizontal: 'left', wrapText: true }; }
        else if(ci === 4){ cell.alignment = { horizontal: 'left' }; }
        else if(ci === 6){ cell.numFmt = QTY_NUMFMT; }
      });
    });

    // ---- Dòng TỔNG ----
    const totalRowIdx = firstDataRow + items.length;
    const totalRow = ws.getRow(totalRowIdx);
    totalRow.height = 22.5;
    for(let ci=1; ci<=7; ci++){
      const cell = totalRow.getCell(ci);
      cell.border = GRID_BORDER;
      cell.font = { ...FONT_BASE, size: 18, bold: true };
    }
    const totalLabelCell = totalRow.getCell(3);
    totalLabelCell.value = 'TỔNG';
    totalLabelCell.alignment = { horizontal: 'left', wrapText: true };
    const totalQtyCell = totalRow.getCell(7);
    totalQtyCell.value = items.length ? { formula: `SUM(G${firstDataRow}:G${totalRowIdx - 1})` } : 0;
    totalQtyCell.numFmt = QTY_NUMFMT;
    totalQtyCell.alignment = { vertical: 'center' };

    // ---- Chữ ký (cách 1 dòng trống) ----
    const signRow1Idx = totalRowIdx + 2;
    const signRow2Idx = signRow1Idx + 1;
    ws.mergeCells(`A${signRow1Idx}:C${signRow1Idx}`);
    const signCell1 = ws.getCell(`A${signRow1Idx}`);
    signCell1.value = 'Người giao hàng';
    signCell1.font = { ...FONT_BASE, bold: true };
    signCell1.alignment = { horizontal: 'center', vertical: 'center', wrapText: true };

    ws.mergeCells(`A${signRow2Idx}:C${signRow2Idx}`);
    const signCell2 = ws.getCell(`A${signRow2Idx}`);
    signCell2.value = '(Ký và ghi rõ họ tên)';
    signCell2.font = { ...FONT_BASE, italic: true };
    signCell2.alignment = { horizontal: 'center', vertical: 'center' };

    // ---- Tự động canh độ rộng cột A-G theo đúng độ dài giá trị thực tế của phiếu này ----
    // Đo độ dài chữ DÀI NHẤT đang có trong từng cột (tiêu đề bảng + mọi dòng dữ liệu + dòng
    // TỔNG), rồi quy đổi ra đơn vị độ rộng cột Excel. Cỡ chữ đã tăng lên 16 (to hơn cỡ mặc
    // định Excel dùng để tính đơn vị độ rộng) nên nhân thêm hệ số bù ~1.35. Vẫn giữ khoảng
    // tối thiểu/tối đa hợp lý riêng cho từng cột — cột "Tên hàng"/"GHI CHÚ" có thể rất dài
    // (tên sản phẩm/ghi chú tự do) nên chặn trần để không kéo cả bảng quá khổ, phần dư vẫn
    // tự xuống dòng nhờ wrapText đã bật sẵn ở trên.
    const SIZE_FACTOR = FONT_BASE.size / 11; // 11 = cỡ chữ mặc định Excel dùng làm chuẩn đo cột
    const COL_MIN = [4.5, 10, 14, 10, 12, 8, 9];
    const COL_MAX = [6,   30, 55, 20, 40, 16, 16];
    const colMaxLen = headers.map(h => h.length);
    items.forEach((it, i) => {
      const vals = [
        String(i + 1),
        it.maHang ?? '',
        it.tenHang ?? '',
        it.phieuNhap ?? '',
        it.soLo ?? '',
        it.size ?? '',
        it.xuatKg ? formatNumber(toNumber(it.xuatKg)) + ' kg' : formatNumber(toNumber(it.soLuongXuat))
      ];
      vals.forEach((v, ci) => { colMaxLen[ci] = Math.max(colMaxLen[ci], String(v).length); });
    });
    colMaxLen[2] = Math.max(colMaxLen[2], totalLabelCell.value.length); // 'TỔNG' nằm chung cột Tên hàng
    colMaxLen[6] = Math.max(colMaxLen[6], 8); // chừa chỗ cho tổng số lượng (có dấu phẩy ngăn cách)
    colMaxLen.forEach((len, i) => {
      const raw = len * SIZE_FACTOR * 1.1 + 2;
      ws.getColumn(i + 1).width = Math.min(COL_MAX[i], Math.max(COL_MIN[i], raw));
    });

    return wb.xlsx.writeBuffer();
  }

  async function deletePxPhieu(id){
    const phieu = pxPhieuList.find(p => p.id === id);
    if(!phieu) return;
    const wasActive = pxIsActive(phieu);
    const ok = await showConfirmDialog({
      title: 'Xác nhận xóa phiếu xuất',
      message: wasActive
        ? `Xóa phiếu xuất "${phieu.maPhieu}"? Số kiện/Trọng lượng đã trừ trước đó sẽ được HOÀN LẠI vào "Dữ Liệu". Hành động này không thể hoàn tác.`
        : `Xóa phiếu xuất "${phieu.maPhieu}" khỏi danh sách?\nPhiếu này đã bị khóa (số liệu tồn đã được cập nhật lại) nên KHÔNG hoàn lại số kiện / trọng lượng vào "Dữ Liệu" hiện tại. Hành động này không thể hoàn tác.`,
      confirmText: 'Xóa phiếu',
      danger: true
    });
    if(!ok) return;
    pxPhieuList = pxPhieuList.filter(p => p.id !== id);
    const delOff = m2Offline(); // PC 3.24
    if(delOff) await m2QueuePhieu({ act: 'delete', ref: id }); else savePxPhieuList();

    // ---- Hủy phiếu xuất -> HOÀN LẠI đúng Số kiện + Trọng lượng đã trừ cho từng dòng, rồi làm
    // mới toàn bộ các tab phụ thuộc "Dữ Liệu" như lúc tạo phiếu.
    if(wasActive){
      applyPxItemsDelta(phieu.items, +1);
      recomputePxDaXuatFlags();
      if(!delOff) savePersistedRows();
    }
    refreshAllTabsAfterPxChange();

    renderPxList();
    // Phiếu do điện thoại gửi (nguon = 'Điện thoại'): báo lên Sheet để điện thoại chuyển sang "Đã hủy" — gọi SAU khi đã
    // hoàn lại số kiện/trọng lượng và lưu Excel, để lần đẩy tồn kèm theo mang số liệu đã hoàn.
    if(phieu.nguon && window.phoneImportBridge && window.phoneImportBridge.markDeleted){
      window.phoneImportBridge.markDeleted('M02', [phieu.id]).catch(e => console.error('Báo xóa phiếu lên Sheet lỗi:', e));
    }
  }

  // ---- Modal sửa phiếu xuất ----
  const pxModalOverlay     = document.getElementById('pxModalOverlay');
  const pxModalMaPhieu     = document.getElementById('pxModalMaPhieu');
  const pxModalItemsScroll = document.getElementById('pxModalItemsScroll');
  const pxModalSaveBtn     = document.getElementById('pxModalSaveBtn');
  const pxModalCancelBtn   = document.getElementById('pxModalCancelBtn');
  const pxModalCloseBtn    = document.getElementById('pxModalCloseBtn');

  let pxEditingId = null;
  let pxEditingItems = [];

  function openPxEditModal(phieu){
    if(m2Offline()){ alert('Máy chủ đang tắt — "Sửa phiếu xuất" chỉ làm được khi máy chủ bật. (Tạo phiếu và Xóa phiếu vẫn làm được.)'); return; }
    if(!pxIsActive(phieu)){
      alert('Phiếu này đã bị khóa vì số liệu tồn đã được cập nhật lại' + (pxLoadDateText(phieu) ? ' (phiếu tính theo số liệu ngày ' + pxLoadDateText(phieu) + ')' : '') + '. Không sửa được — chỉ xem / xuất Excel / xóa.');
      return;
    }
    pxEditingId = phieu.id;
    pxEditingItems = (phieu.items || []).map(it => ({ ...it }));
    pxModalMaPhieu.value = phieu.maPhieu || '';
    renderPxModalItems();
    pxModalOverlay.style.display = 'flex';
  }

  function closePxEditModal(){
    pxModalOverlay.style.display = 'none';
    pxEditingId = null;
    pxEditingItems = [];
  }

  function renderPxModalItems(){
    if(!pxEditingItems.length){
      pxModalItemsScroll.innerHTML = '<div class="empty-table">Phiếu không còn dòng nào.</div>';
      return;
    }
    let html = '<table class="px-table"><thead><tr>' +
      '<th style="width:40px;">STT</th>' +
      '<th>Mã hàng</th><th>Tên hàng</th><th>Phiếu nhập</th><th>Số lô</th><th>Size</th>' +
      '<th style="width:80px;">SL tồn</th>' +
      '<th style="width:110px;">SL xuất</th>' +
      '<th style="width:44px;"></th>' +
      '</tr></thead><tbody>';
    pxEditingItems.forEach((it, i)=>{
      html += '<tr>' +
        `<td>${i + 1}</td>` +
        `<td>${escapeHtml(String(it.maHang ?? ''))}</td>` +
        `<td>${escapeHtml(String(it.tenHang ?? ''))}</td>` +
        `<td>${escapeHtml(String(it.phieuNhap ?? ''))}</td>` +
        `<td>${escapeHtml(String(it.soLo ?? ''))}</td>` +
        `<td>${escapeHtml(String(it.size ?? ''))}</td>` +
        `<td>${escapeHtml(String(it.soKien ?? ''))}</td>` +
        `<td><input type="text" inputmode="decimal" class="px-modal-qty" data-i="${i}" value="${escapeHtml(String(it.xuatKg !== undefined ? it.xuatKg : (it.soLuongXuat ?? '')))}"${it.xuatKg !== undefined ? ' title="Dòng chỉ có kg: nhập số KG xuất"' : ''}></td>` +
        `<td style="text-align:center;"><button type="button" class="px-modal-remove" data-i="${i}" title="Xóa dòng này khỏi phiếu">✕</button></td>` +
        '</tr>';
    });
    html += '</tbody></table>';
    pxModalItemsScroll.innerHTML = html;
  }

  pxModalItemsScroll.addEventListener('input', (e)=>{
    const inp = e.target;
    if(!inp.classList || !inp.classList.contains('px-modal-qty')) return;
    const i = parseInt(inp.getAttribute('data-i'), 10);
    if(pxEditingItems[i]){ if(pxEditingItems[i].xuatKg !== undefined) pxEditingItems[i].xuatKg = inp.value; else pxEditingItems[i].soLuongXuat = inp.value; }
  });

  pxModalItemsScroll.addEventListener('click', async (e)=>{
    const btn = e.target.closest('.px-modal-remove');
    if(!btn) return;
    const i = parseInt(btn.getAttribute('data-i'), 10);
    const ok = await showConfirmDialog({
      title: 'Xác nhận xóa dòng',
      message: 'Xóa dòng hàng này khỏi phiếu xuất đang sửa?',
      confirmText: 'Xóa dòng',
      danger: true
    });
    if(!ok) return;
    pxEditingItems.splice(i, 1);
    renderPxModalItems();
  });

  pxModalSaveBtn.addEventListener('click', async ()=>{
    if(!pxEditingId) return;
    const phieu = pxPhieuList.find(p => p.id === pxEditingId);
    if(!phieu){ closePxEditModal(); return; }
    if(!pxIsActive(phieu)){ closePxEditModal(); alert('Phiếu này vừa bị khóa vì số liệu tồn đã được cập nhật lại — không lưu thay đổi.'); return; }
    let newMa = (pxModalMaPhieu.value || '').trim();
    if(!newMa) newMa = phieu.maPhieu;
    if(newMa !== phieu.maPhieu && pxPhieuList.some(p => p.id !== phieu.id && p.maPhieu === newMa)){
      alert('Mã phiếu này đã tồn tại, vui lòng chọn mã khác.');
      return;
    }
    { // PC 5.09: phiếu phải còn ít nhất 1 dòng; SL mới > 0 và không vượt (tồn hiện tại + SL cũ của chính dòng đó trên phiếu này)
      if(!pxEditingItems.length){ alert('Phiếu không còn dòng nào. Muốn bỏ phiếu hãy dùng nút Xóa phiếu.'); return; }
      const idxMapChk = buildPersistedRowIndex(), oldQty = new Map(); // PC 5.10: theo DÒNG (không theo khóa) để dòng trùng khóa không cộng lẫn
      (phieu.items || []).forEach(o => { const rr = pxPickRow(idxMapChk, o); if(rr) oldQty.set(rr, (oldQty.get(rr) || 0) + (o.xuatKg !== undefined ? toNumber(o.xuatKg) : toNumber(o.soLuongXuat))); });
      const msg = pxQtyProblems(pxEditingItems.map(it => {
        const r = pxPickRow(idxMapChk, it), kg = it.xuatKg !== undefined;
        return { label: `${it.maHang || ''}${it.soLo ? ' · Lô ' + it.soLo : ''}${it.size ? ' · ' + it.size : ''}`.trim() + (kg ? ' (xuất theo kg)' : ''),
          qtyRaw: kg ? it.xuatKg : it.soLuongXuat, avail: r ? (kg ? toNumber(r.trongLuong) : toNumber(r.soKien)) + (oldQty.get(r) || 0) : null };
      }));
      if(msg){ alert(msg); return; }
    }
    const ok = await showConfirmDialog({
      title: 'Xác nhận lưu thay đổi',
      message: `Lưu thay đổi cho phiếu xuất "${newMa}"? Số kiện/Trọng lượng ở "Dữ Liệu" sẽ được điều chỉnh lại đúng theo số lượng mới.`,
      confirmText: 'Lưu thay đổi'
    });
    if(!ok) return;

    const oldItems = phieu.items || [];
    const newItems = pxEditingItems.map(it => it.xuatKg !== undefined
      ? ({ ...it, xuatKg: toNumber(it.xuatKg), soLuongXuat: 0 })
      : ({ ...it, soLuongXuat: toNumber(it.soLuongXuat) }));

    // ---- Điều chỉnh "Dữ Liệu": HOÀN LẠI toàn bộ theo số lượng CŨ trước, rồi tính lại Trọng lượng
    // tương ứng và TRỪ lại theo số lượng MỚI — làm theo thứ tự này (hoàn trước, trừ sau, dựa trên
    // tồn kho SAU KHI đã hoàn) để sửa phiếu nhiều lần (tăng/giảm số lượng, xóa dòng...) luôn ra
    // đúng số liệu, không bị cộng dồn sai qua từng lần sửa.
    applyPxItemsDelta(oldItems, +1);
    const idxMapForEdit = buildPersistedRowIndex();
    newItems.forEach(it=>{
      const r = pxPickRow(idxMapForEdit, it);
      if(r){
        const soKienGoc = toNumber(r.soKien);
        const trongLuongGoc = toNumber(r.trongLuong);
        if(it.occ === undefined) it.occ = pxRowOcc(idxMapForEdit, r); // phiếu cũ: ghim vào đúng dòng vừa hoàn
        it.trongLuongTru = it.xuatKg !== undefined ? toNumber(it.xuatKg) : (soKienGoc > 0 ? trongLuongGoc * (toNumber(it.soLuongXuat) / soKienGoc) : 0);
      } else {
        it.trongLuongTru = 0; // dòng gốc không còn -> không trừ được Trọng lượng, chỉ giữ Số kiện xuất trên phiếu
      }
    });
    applyPxItemsDelta(newItems, -1);
    recomputePxDaXuatFlags();
    savePersistedRows();

    phieu.maPhieu = newMa;
    phieu.items = newItems;
    savePxPhieuList();

    refreshAllTabsAfterPxChange();
    renderPxList();
    closePxEditModal();
  });

  pxModalCancelBtn.addEventListener('click', closePxEditModal);
  pxModalCloseBtn.addEventListener('click', closePxEditModal);
  pxModalOverlay.addEventListener('mousedown', (e)=>{
    if(e.target === pxModalOverlay) closePxEditModal();
  });

  // ---- Modal xem chi tiết phiếu xuất (popup lớn, chỉ xem — không sửa được) ----
  const pxViewModalOverlay     = document.getElementById('pxViewModalOverlay');
  const pxViewModalTitle       = document.getElementById('pxViewModalTitle');
  const pxViewModalMeta        = document.getElementById('pxViewModalMeta');
  const pxViewModalItemsScroll = document.getElementById('pxViewModalItemsScroll');
  const pxViewModalCloseBtn    = document.getElementById('pxViewModalCloseBtn');
  const pxViewModalCloseBtn2   = document.getElementById('pxViewModalCloseBtn2');
  const pxViewModalEditBtn     = document.getElementById('pxViewModalEditBtn');
  const pxViewModalExportBtn   = document.getElementById('pxViewModalExportBtn');

  let pxViewingId = null;

  function openPxViewModal(phieu){
    pxViewingId = phieu.id;
    pxViewModalTitle.textContent = 'Chi tiết phiếu xuất — ' + (phieu.maPhieu || '');
    const items = phieu.items || [];
    const tongSL = items.reduce((s, it)=> s + toNumber(it.soLuongXuat), 0);
    const tongTon = items.reduce((s, it)=> s + toNumber(it.soKien), 0);
    pxViewModalMeta.innerHTML =
      `<div class="px-view-item"><span>Mã phiếu</span><b>${escapeHtml(String(phieu.maPhieu || ''))}</b></div>` +
      `<div class="px-view-item"><span>Ngày tạo</span><b>${escapeHtml(formatPxDate(phieu.ngayTao))}</b></div>` +
      `<div class="px-view-item"><span>Số dòng</span><b>${formatNumber(items.length)}</b></div>` +
      `<div class="px-view-item"><span>Tổng SL tồn</span><b>${formatNumber(tongTon)}</b></div>` +
      `<div class="px-view-item"><span>Tổng SL xuất</span><b>${formatNumber(tongSL)}</b></div>`;
    {
      const act = pxIsActive(phieu), ld = pxLoadDateText(phieu);
      pxViewModalMeta.innerHTML +=
        `<div class="px-view-item"><span>Theo tồn ngày</span><b>${escapeHtml(ld || '—')}</b></div>` +
        `<div class="px-view-item"><span>Trạng thái</span><b>${act ? 'Đang trừ tồn' : '🔒 Đã khóa — tồn đã cập nhật'}</b></div>`;
      pxViewModalEditBtn.style.display = act ? '' : 'none';
    }

    if(!items.length){
      pxViewModalItemsScroll.innerHTML = '<div class="empty-table">Phiếu không có dòng nào.</div>';
    } else {
      let html = '<table class="px-table"><thead><tr>' +
        '<th style="width:50px;">STT</th>' +
        '<th>Mã hàng</th><th>Tên hàng</th><th>Phiếu nhập</th><th>Số lô</th><th>Size</th>' +
        '<th style="width:90px;">SL tồn</th>' +
        '<th style="width:100px;">SL xuất</th>' +
        '</tr></thead><tbody>';
      items.forEach((it, i)=>{
        html += '<tr>' +
          `<td>${i + 1}</td>` +
          `<td>${escapeHtml(String(it.maHang ?? ''))}</td>` +
          `<td>${escapeHtml(String(it.tenHang ?? ''))}</td>` +
          `<td>${escapeHtml(String(it.phieuNhap ?? ''))}</td>` +
          `<td>${escapeHtml(String(it.soLo ?? ''))}</td>` +
          `<td>${escapeHtml(String(it.size ?? ''))}</td>` +
          `<td>${formatNumber(toNumber(it.soKien))}</td>` +
          `<td>${it.xuatKg ? formatNumber(toNumber(it.xuatKg)) + ' kg' : formatNumber(toNumber(it.soLuongXuat))}</td>` +
          '</tr>';
      });
      html += '</tbody></table>';
      pxViewModalItemsScroll.innerHTML = html;
    }
    pxViewModalOverlay.style.display = 'flex';
  }

  function closePxViewModal(){
    pxViewModalOverlay.style.display = 'none';
    pxViewingId = null;
  }

  pxViewModalCloseBtn.addEventListener('click', closePxViewModal);
  pxViewModalCloseBtn2.addEventListener('click', closePxViewModal);
  pxViewModalOverlay.addEventListener('mousedown', (e)=>{
    if(e.target === pxViewModalOverlay) closePxViewModal();
  });
  pxViewModalExportBtn.addEventListener('click', ()=>{
    const phieu = pxPhieuList.find(p => p.id === pxViewingId);
    if(phieu) exportPxPhieuToExcel(phieu);
  });
  pxViewModalEditBtn.addEventListener('click', ()=>{
    const phieu = pxPhieuList.find(p => p.id === pxViewingId);
    if(!phieu) return;
    closePxViewModal();
    openPxEditModal(phieu);
  });

  // ---- TAB 2: Tôm Ecuador (lọc từ dữ liệu đã nạp ở tab 1) ----
  const toolbarLabel2 = document.getElementById('toolbarLabel2');
  const tableScroll2  = document.getElementById('tableScroll2');
  const rowCount2     = document.getElementById('rowCount2');
  const summaryStats2 = document.getElementById('summaryStats2');
  const pagination2   = document.getElementById('pagination2');
  const pageSizeSelect2 = document.getElementById('pageSizeSelect2');
  const prevPageBtn2  = document.getElementById('prevPage2');
  const nextPageBtn2  = document.getElementById('nextPage2');
  const pageIndicator2= document.getElementById('pageIndicator2');
  const exportBtn2    = document.getElementById('exportBtn2');

  let ecuadorRows = [];
  let ecuadorPage = 1;
  let ecuadorPageSize = 100;
  let ecuadorSearchState = { searchFilters:{}, lastHeaderKey:null, bound:false };

  // Khớp "ECUADOR" xuất hiện ở bất kỳ vị trí nào trong Số lô (đầu, cuối, hoặc giữa chuỗi)
  function isEcuadorLot(soLo){
    return normalize(soLo).includes('ecuador');
  }

  function refreshEcuadorTab(){
    if(!persistedRows || !persistedRows.length){
      ecuadorRows = [];
      toolbarLabel2.textContent = 'Chưa có dữ liệu';
      tableScroll2.innerHTML = '<div class="empty-table">Chưa có dữ liệu — hãy nạp file ở menu "Load data" trước.</div>';
      rowCount2.textContent = '';
      summaryStats2.innerHTML = '';
      pagination2.style.display = 'none';
      exportBtn2.style.display = 'none';
      return;
    }

    ecuadorRows = persistedRows.filter(r => isEcuadorLot(r.soLo));
    ecuadorRows = applyCustomFiltersToRows(ecuadorRows, '2');
    ecuadorPage = 1;
    ecuadorSearchState.searchFilters = {};
    ecuadorSearchState.lastHeaderKey = null;
    resetTableCheckboxFilters('ecuador');
    toolbarLabel2.textContent = 'Số lô Ecuador';
    exportBtn2.style.display = ecuadorRows.length ? 'inline-block' : 'none';
    updateEcuadorSummary();
    renderCustomFilterArea('2', 'customFilterArea2', refreshEcuadorTab);
    renderEcuadorPage();
  }

  function updateEcuadorSummary(rows){
    const data = rows || ecuadorRows;
    if(!data.length){
      summaryStats2.innerHTML = `<span class="stat">Số dòng<b>0</b></span>`;
      return;
    }
    let soKienSum = 0, trongLuongSum = 0;
    data.forEach(r=>{
      soKienSum += toNumber(r.soKien);
      trongLuongSum += toNumber(r.trongLuong);
    });
    summaryStats2.innerHTML =
      `<span class="stat">Số dòng<b>${formatNumber(data.length)}</b></span>` +
      `<span class="stat">Tồn kiện<b>${formatNumber(soKienSum)}</b></span>` +
      `<span class="stat">Tồn KG<b>${formatWeight(trongLuongSum)}</b></span>`;
  }

  function renderEcuadorPage(){
    const filtered = applyColumnSearch(ecuadorRows, TARGET_COLS, ecuadorSearchState.searchFilters, 'ecuador');
    updateEcuadorSummary(filtered);
    const total = filtered.length;
    const totalPages = Math.max(1, Math.ceil(total / ecuadorPageSize));
    if(ecuadorPage > totalPages) ecuadorPage = totalPages;
    if(ecuadorPage < 1) ecuadorPage = 1;

    const start = (ecuadorPage - 1) * ecuadorPageSize;
    const pageRows = filtered.slice(start, start + ecuadorPageSize)
      .map(r => rowToDisplayArray(r));

    if(!ecuadorRows.length){
      tableScroll2.innerHTML = '<div class="empty-table">Không có dòng nào có Số lô chứa ECUADOR trong dữ liệu đã nạp.</div>';
    } else {
      renderGenericTable(tableScroll2, TARGET_COLS, pageRows, true, {
        state: ecuadorSearchState,
        allRows: ecuadorRows,
        tableId: 'ecuador',
        onSearchInput: (idx, val) => {
          ecuadorSearchState.searchFilters[idx] = val;
          ecuadorPage = 1;
          renderEcuadorPage();
        },
        onFilterApply: () => {
          ecuadorPage = 1;
          renderEcuadorPage();
        }
      });
    }

    if(total > 0){
      const from = start + 1;
      const to = Math.min(start + ecuadorPageSize, total);
      rowCount2.textContent = `${formatNumber(total)} dòng (${from}–${to})`;
    } else {
      rowCount2.textContent = '';
    }

    pageIndicator2.textContent = 'Trang ' + ecuadorPage + '/' + totalPages;
    prevPageBtn2.disabled = ecuadorPage <= 1;
    nextPageBtn2.disabled = ecuadorPage >= totalPages;
    pagination2.style.display = total > 0 ? 'flex' : 'none';
  }

  pageSizeSelect2.addEventListener('change', ()=>{
    ecuadorPageSize = parseInt(pageSizeSelect2.value, 10);
    ecuadorPage = 1;
    renderEcuadorPage();
  });
  prevPageBtn2.addEventListener('click', ()=>{ ecuadorPage--; renderEcuadorPage(); });
  nextPageBtn2.addEventListener('click', ()=>{ ecuadorPage++; renderEcuadorPage(); });

  exportBtn2.addEventListener('click', ()=>{
    if(!ecuadorRows.length) return;
    const data = ecuadorRows.map(r => rowToExportObj(r));
    const ws = XLSX.utils.json_to_sheet(data);
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, 'Tom Ecuador');
    XLSX.writeFile(wb, 'tom-ecuador.xlsx');
  });

  // ---- TAB: Tôm Ấn Độ (lọc từ dữ liệu đã nạp ở tab 1) ----
  const toolbarLabelAD = document.getElementById('toolbarLabelAD');
  const tableScrollAD  = document.getElementById('tableScrollAD');
  const rowCountAD     = document.getElementById('rowCountAD');
  const summaryStatsAD = document.getElementById('summaryStatsAD');
  const paginationAD   = document.getElementById('paginationAD');
  const pageSizeSelectAD = document.getElementById('pageSizeSelectAD');
  const prevPageBtnAD  = document.getElementById('prevPageAD');
  const nextPageBtnAD  = document.getElementById('nextPageAD');
  const pageIndicatorAD= document.getElementById('pageIndicatorAD');
  const exportBtnAD    = document.getElementById('exportBtnAD');

  let anDoRows = [];
  let anDoPage = 1;
  let anDoPageSize = 100;
  let anDoSearchState = { searchFilters:{}, lastHeaderKey:null, bound:false };

  // Khớp "AD" xuất hiện ở bất kỳ vị trí nào trong Số lô (đầu, cuối, hoặc giữa chuỗi)
  function isAnDoLot(soLo){
    return normalize(soLo).includes('ad');
  }

  // Chỉ nhận là "Tôm Ấn Độ" khi khớp số lô AD VÀ không thuộc về Ecuador/TGC/TGS/Nguyên Liệu —
  // tránh 1 dòng bị đếm trùng ở nhiều tab cùng lúc.
  function isAnDoOnly(r){
    return isAnDoLot(r.soLo) && !isEcuadorLot(r.soLo) && !isTGCLot(r.soLo) && !isTGSLot(r.soLo) && !isNguyenLieuSize(r.size);
  }

  function refreshAnDoTab(){
    if(!persistedRows || !persistedRows.length){
      anDoRows = [];
      toolbarLabelAD.textContent = 'Chưa có dữ liệu';
      tableScrollAD.innerHTML = '<div class="empty-table">Chưa có dữ liệu — hãy nạp file ở menu "Load data" trước.</div>';
      rowCountAD.textContent = '';
      summaryStatsAD.innerHTML = '';
      paginationAD.style.display = 'none';
      exportBtnAD.style.display = 'none';
      return;
    }

    anDoRows = persistedRows.filter(isAnDoOnly);
    anDoRows = applyCustomFiltersToRows(anDoRows, 'ad');
    anDoPage = 1;
    anDoSearchState.searchFilters = {};
    anDoSearchState.lastHeaderKey = null;
    resetTableCheckboxFilters('ad');
    toolbarLabelAD.textContent = 'Số lô Ấn Độ';
    exportBtnAD.style.display = anDoRows.length ? 'inline-block' : 'none';
    updateAnDoSummary();
    renderCustomFilterArea('ad', 'customFilterAreaAD', refreshAnDoTab);
    renderAnDoPage();
  }

  function updateAnDoSummary(rows){
    const data = rows || anDoRows;
    if(!data.length){
      summaryStatsAD.innerHTML = `<span class="stat">Số dòng<b>0</b></span>`;
      return;
    }
    let soKienSum = 0, trongLuongSum = 0;
    data.forEach(r=>{
      soKienSum += toNumber(r.soKien);
      trongLuongSum += toNumber(r.trongLuong);
    });
    summaryStatsAD.innerHTML =
      `<span class="stat">Số dòng<b>${formatNumber(data.length)}</b></span>` +
      `<span class="stat">Tồn kiện<b>${formatNumber(soKienSum)}</b></span>` +
      `<span class="stat">Tồn KG<b>${formatWeight(trongLuongSum)}</b></span>`;
  }

  function renderAnDoPage(){
    const filtered = applyColumnSearch(anDoRows, TARGET_COLS, anDoSearchState.searchFilters, 'ad');
    updateAnDoSummary(filtered);
    const total = filtered.length;
    const totalPages = Math.max(1, Math.ceil(total / anDoPageSize));
    if(anDoPage > totalPages) anDoPage = totalPages;
    if(anDoPage < 1) anDoPage = 1;

    const start = (anDoPage - 1) * anDoPageSize;
    const pageRows = filtered.slice(start, start + anDoPageSize)
      .map(r => rowToDisplayArray(r));

    if(!anDoRows.length){
      tableScrollAD.innerHTML = '<div class="empty-table">Không có dòng nào có Số lô chứa AD trong dữ liệu đã nạp.</div>';
    } else {
      renderGenericTable(tableScrollAD, TARGET_COLS, pageRows, true, {
        state: anDoSearchState,
        allRows: anDoRows,
        tableId: 'ad',
        onSearchInput: (idx, val) => {
          anDoSearchState.searchFilters[idx] = val;
          anDoPage = 1;
          renderAnDoPage();
        },
        onFilterApply: () => {
          anDoPage = 1;
          renderAnDoPage();
        }
      });
    }

    if(total > 0){
      const from = start + 1;
      const to = Math.min(start + anDoPageSize, total);
      rowCountAD.textContent = `${formatNumber(total)} dòng (${from}–${to})`;
    } else {
      rowCountAD.textContent = '';
    }

    pageIndicatorAD.textContent = 'Trang ' + anDoPage + '/' + totalPages;
    prevPageBtnAD.disabled = anDoPage <= 1;
    nextPageBtnAD.disabled = anDoPage >= totalPages;
    paginationAD.style.display = total > 0 ? 'flex' : 'none';
  }

  pageSizeSelectAD.addEventListener('change', ()=>{
    anDoPageSize = parseInt(pageSizeSelectAD.value, 10);
    anDoPage = 1;
    renderAnDoPage();
  });
  prevPageBtnAD.addEventListener('click', ()=>{ anDoPage--; renderAnDoPage(); });
  nextPageBtnAD.addEventListener('click', ()=>{ anDoPage++; renderAnDoPage(); });

  exportBtnAD.addEventListener('click', ()=>{
    if(!anDoRows.length) return;
    const data = anDoRows.map(r => rowToExportObj(r));
    const ws = XLSX.utils.json_to_sheet(data);
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, 'Tom An Do');
    XLSX.writeFile(wb, 'tom-an-do.xlsx');
  });

  // ---- TAB 3: Tôm Gia Công (lọc từ dữ liệu đã nạp ở tab 1) ----
  const toolbarLabel3 = document.getElementById('toolbarLabel3');
  const tableScroll3  = document.getElementById('tableScroll3');
  const rowCount3     = document.getElementById('rowCount3');
  const summaryStats3 = document.getElementById('summaryStats3');
  const pagination3   = document.getElementById('pagination3');
  const pageSizeSelect3 = document.getElementById('pageSizeSelect3');
  const prevPageBtn3  = document.getElementById('prevPage3');
  const nextPageBtn3  = document.getElementById('nextPage3');
  const pageIndicator3= document.getElementById('pageIndicator3');
  const exportBtn3    = document.getElementById('exportBtn3');

  let tgcRows = [];
  let tgcPage = 1;
  let tgcPageSize = 100;
  let tgcSearchState = { searchFilters:{}, lastHeaderKey:null, bound:false };

  // Khớp "TGC" xuất hiện ở bất kỳ vị trí nào trong Số lô (đầu, cuối, hoặc giữa chuỗi)
  function isTGCLot(soLo){
    return normalize(soLo).includes('tgc');
  }

  function refreshTGCTab(){
    if(!persistedRows || !persistedRows.length){
      tgcRows = [];
      toolbarLabel3.textContent = 'Chưa có dữ liệu';
      tableScroll3.innerHTML = '<div class="empty-table">Chưa có dữ liệu — hãy nạp file ở menu "Load data" trước.</div>';
      rowCount3.textContent = '';
      summaryStats3.innerHTML = '';
      pagination3.style.display = 'none';
      exportBtn3.style.display = 'none';
      return;
    }

    tgcRows = persistedRows.filter(r => isTGCLot(r.soLo));
    tgcRows = applyCustomFiltersToRows(tgcRows, '3');
    tgcPage = 1;
    tgcSearchState.searchFilters = {};
    tgcSearchState.lastHeaderKey = null;
    resetTableCheckboxFilters('tgc');
    toolbarLabel3.textContent = 'Số lô Gia Công (TGC)';
    exportBtn3.style.display = tgcRows.length ? 'inline-block' : 'none';
    updateTGCSummary();
    renderCustomFilterArea('3', 'customFilterArea3', refreshTGCTab);
    renderTGCPage();
  }

  function updateTGCSummary(rows){
    const data = rows || tgcRows;
    if(!data.length){
      summaryStats3.innerHTML = `<span class="stat">Số dòng<b>0</b></span>`;
      return;
    }
    let soKienSum = 0, trongLuongSum = 0;
    data.forEach(r=>{
      soKienSum += toNumber(r.soKien);
      trongLuongSum += toNumber(r.trongLuong);
    });
    summaryStats3.innerHTML =
      `<span class="stat">Số dòng<b>${formatNumber(data.length)}</b></span>` +
      `<span class="stat">Tồn kiện<b>${formatNumber(soKienSum)}</b></span>` +
      `<span class="stat">Tồn KG<b>${formatWeight(trongLuongSum)}</b></span>`;
  }

  function renderTGCPage(){
    const filtered = applyColumnSearch(tgcRows, TARGET_COLS, tgcSearchState.searchFilters, 'tgc');
    updateTGCSummary(filtered);
    const total = filtered.length;
    const totalPages = Math.max(1, Math.ceil(total / tgcPageSize));
    if(tgcPage > totalPages) tgcPage = totalPages;
    if(tgcPage < 1) tgcPage = 1;

    const start = (tgcPage - 1) * tgcPageSize;
    const pageRows = filtered.slice(start, start + tgcPageSize)
      .map(r => rowToDisplayArray(r));

    if(!tgcRows.length){
      tableScroll3.innerHTML = '<div class="empty-table">Không có dòng nào có Số lô chứa TGC trong dữ liệu đã nạp.</div>';
    } else {
      renderGenericTable(tableScroll3, TARGET_COLS, pageRows, true, {
        state: tgcSearchState,
        allRows: tgcRows,
        tableId: 'tgc',
        onSearchInput: (idx, val) => {
          tgcSearchState.searchFilters[idx] = val;
          tgcPage = 1;
          renderTGCPage();
        },
        onFilterApply: () => {
          tgcPage = 1;
          renderTGCPage();
        }
      });
    }

    if(total > 0){
      const from = start + 1;
      const to = Math.min(start + tgcPageSize, total);
      rowCount3.textContent = `${formatNumber(total)} dòng (${from}–${to})`;
    } else {
      rowCount3.textContent = '';
    }

    pageIndicator3.textContent = 'Trang ' + tgcPage + '/' + totalPages;
    prevPageBtn3.disabled = tgcPage <= 1;
    nextPageBtn3.disabled = tgcPage >= totalPages;
    pagination3.style.display = total > 0 ? 'flex' : 'none';
  }

  pageSizeSelect3.addEventListener('change', ()=>{
    tgcPageSize = parseInt(pageSizeSelect3.value, 10);
    tgcPage = 1;
    renderTGCPage();
  });
  prevPageBtn3.addEventListener('click', ()=>{ tgcPage--; renderTGCPage(); });
  nextPageBtn3.addEventListener('click', ()=>{ tgcPage++; renderTGCPage(); });

  exportBtn3.addEventListener('click', ()=>{
    if(!tgcRows.length) return;
    const data = tgcRows.map(r => rowToExportObj(r));
    const ws = XLSX.utils.json_to_sheet(data);
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, 'Tom Gia Cong');
    XLSX.writeFile(wb, 'tom-gia-cong.xlsx');
  });

  // ---- TAB 4: Tôm Giám Sát (lọc từ dữ liệu đã nạp ở tab 1) ----
  const toolbarLabel4 = document.getElementById('toolbarLabel4');
  const tableScroll4  = document.getElementById('tableScroll4');
  const rowCount4     = document.getElementById('rowCount4');
  const summaryStats4 = document.getElementById('summaryStats4');
  const pagination4   = document.getElementById('pagination4');
  const pageSizeSelect4 = document.getElementById('pageSizeSelect4');
  const prevPageBtn4  = document.getElementById('prevPage4');
  const nextPageBtn4  = document.getElementById('nextPage4');
  const pageIndicator4= document.getElementById('pageIndicator4');
  const exportBtn4    = document.getElementById('exportBtn4');

  let tgsRows = [];
  let tgsPage = 1;
  let tgsPageSize = 100;
  let tgsSearchState = { searchFilters:{}, lastHeaderKey:null, bound:false };

  // Khớp "TGS" xuất hiện ở bất kỳ vị trí nào trong Số lô (đầu, cuối, hoặc giữa chuỗi)
  // vd: "TGS", "TGSL", "06.05.26 L12 TGSL"
  function isTGSLot(soLo){
    return normalize(soLo).includes('tgs');
  }

  function refreshTGSTab(){
    if(!persistedRows || !persistedRows.length){
      tgsRows = [];
      toolbarLabel4.textContent = 'Chưa có dữ liệu';
      tableScroll4.innerHTML = '<div class="empty-table">Chưa có dữ liệu — hãy nạp file ở menu "Load data" trước.</div>';
      rowCount4.textContent = '';
      summaryStats4.innerHTML = '';
      pagination4.style.display = 'none';
      exportBtn4.style.display = 'none';
      return;
    }

    tgsRows = persistedRows.filter(r => isTGSLot(r.soLo));
    tgsRows = applyCustomFiltersToRows(tgsRows, '4');
    tgsPage = 1;
    tgsSearchState.searchFilters = {};
    tgsSearchState.lastHeaderKey = null;
    resetTableCheckboxFilters('tgs');
    toolbarLabel4.textContent = 'Số lô Giám Sát (TGS)';
    exportBtn4.style.display = tgsRows.length ? 'inline-block' : 'none';
    updateTGSSummary();
    renderCustomFilterArea('4', 'customFilterArea4', refreshTGSTab);
    renderTGSPage();
  }

  function updateTGSSummary(rows){
    const data = rows || tgsRows;
    if(!data.length){
      summaryStats4.innerHTML = `<span class="stat">Số dòng<b>0</b></span>`;
      return;
    }
    let soKienSum = 0, trongLuongSum = 0;
    data.forEach(r=>{
      soKienSum += toNumber(r.soKien);
      trongLuongSum += toNumber(r.trongLuong);
    });
    summaryStats4.innerHTML =
      `<span class="stat">Số dòng<b>${formatNumber(data.length)}</b></span>` +
      `<span class="stat">Tồn kiện<b>${formatNumber(soKienSum)}</b></span>` +
      `<span class="stat">Tồn KG<b>${formatWeight(trongLuongSum)}</b></span>`;
  }

  function renderTGSPage(){
    const filtered = applyColumnSearch(tgsRows, TARGET_COLS, tgsSearchState.searchFilters, 'tgs');
    updateTGSSummary(filtered);
    const total = filtered.length;
    const totalPages = Math.max(1, Math.ceil(total / tgsPageSize));
    if(tgsPage > totalPages) tgsPage = totalPages;
    if(tgsPage < 1) tgsPage = 1;

    const start = (tgsPage - 1) * tgsPageSize;
    const pageRows = filtered.slice(start, start + tgsPageSize)
      .map(r => rowToDisplayArray(r));

    if(!tgsRows.length){
      tableScroll4.innerHTML = '<div class="empty-table">Không có dòng nào có Số lô chứa TGS trong dữ liệu đã nạp.</div>';
    } else {
      renderGenericTable(tableScroll4, TARGET_COLS, pageRows, true, {
        state: tgsSearchState,
        allRows: tgsRows,
        tableId: 'tgs',
        onSearchInput: (idx, val) => {
          tgsSearchState.searchFilters[idx] = val;
          tgsPage = 1;
          renderTGSPage();
        },
        onFilterApply: () => {
          tgsPage = 1;
          renderTGSPage();
        }
      });
    }

    if(total > 0){
      const from = start + 1;
      const to = Math.min(start + tgsPageSize, total);
      rowCount4.textContent = `${formatNumber(total)} dòng (${from}–${to})`;
    } else {
      rowCount4.textContent = '';
    }

    pageIndicator4.textContent = 'Trang ' + tgsPage + '/' + totalPages;
    prevPageBtn4.disabled = tgsPage <= 1;
    nextPageBtn4.disabled = tgsPage >= totalPages;
    pagination4.style.display = total > 0 ? 'flex' : 'none';
  }

  pageSizeSelect4.addEventListener('change', ()=>{
    tgsPageSize = parseInt(pageSizeSelect4.value, 10);
    tgsPage = 1;
    renderTGSPage();
  });
  prevPageBtn4.addEventListener('click', ()=>{ tgsPage--; renderTGSPage(); });
  nextPageBtn4.addEventListener('click', ()=>{ tgsPage++; renderTGSPage(); });

  exportBtn4.addEventListener('click', ()=>{
    if(!tgsRows.length) return;
    const data = tgsRows.map(r => rowToExportObj(r));
    const ws = XLSX.utils.json_to_sheet(data);
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, 'Tom Giam Sat');
    XLSX.writeFile(wb, 'tom-giam-sat.xlsx');
  });

  // ---- TAB NL: Tôm Nguyên Liệu (lọc từ dữ liệu đã nạp ở tab 1) ----
  const toolbarLabelNL = document.getElementById('toolbarLabelNL');
  const tableScrollNL  = document.getElementById('tableScrollNL');
  const rowCountNL     = document.getElementById('rowCountNL');
  const summaryStatsNL = document.getElementById('summaryStatsNL');
  const paginationNL   = document.getElementById('paginationNL');
  const pageSizeSelectNL = document.getElementById('pageSizeSelectNL');
  const prevPageBtnNL  = document.getElementById('prevPageNL');
  const nextPageBtnNL  = document.getElementById('nextPageNL');
  const pageIndicatorNL= document.getElementById('pageIndicatorNL');
  const exportBtnNL    = document.getElementById('exportBtnNL');

  let nlRows = [];
  let nlPage = 1;
  let nlPageSize = 100;
  let nlSearchState = { searchFilters:{}, lastHeaderKey:null, bound:false };

  // Khớp "NL" xuất hiện ở bất kỳ vị trí nào trong Size (vd: "NL115")
  function isNguyenLieuSize(size){
    return normalize(size).includes('nl');
  }

  function refreshNguyenLieuTab(){
    if(!persistedRows || !persistedRows.length){
      nlRows = [];
      toolbarLabelNL.textContent = 'Chưa có dữ liệu';
      tableScrollNL.innerHTML = '<div class="empty-table">Chưa có dữ liệu — hãy nạp file ở menu "Load data" trước.</div>';
      rowCountNL.textContent = '';
      summaryStatsNL.innerHTML = '';
      paginationNL.style.display = 'none';
      exportBtnNL.style.display = 'none';
      return;
    }

    nlRows = persistedRows.filter(r => isNguyenLieuSize(r.size));
    nlRows = applyCustomFiltersToRows(nlRows, 'nl');
    nlPage = 1;
    nlSearchState.searchFilters = {};
    nlSearchState.lastHeaderKey = null;
    resetTableCheckboxFilters('nl');
    toolbarLabelNL.textContent = 'Size Nguyên Liệu (NL)';
    exportBtnNL.style.display = nlRows.length ? 'inline-block' : 'none';
    updateNguyenLieuSummary();
    renderCustomFilterArea('nl', 'customFilterAreaNL', refreshNguyenLieuTab);
    renderNguyenLieuPage();
  }

  function updateNguyenLieuSummary(rows){
    const data = rows || nlRows;
    if(!data.length){
      summaryStatsNL.innerHTML = `<span class="stat">Số dòng<b>0</b></span>`;
      return;
    }
    let soKienSum = 0, trongLuongSum = 0;
    data.forEach(r=>{
      soKienSum += toNumber(r.soKien);
      trongLuongSum += toNumber(r.trongLuong);
    });
    summaryStatsNL.innerHTML =
      `<span class="stat">Số dòng<b>${formatNumber(data.length)}</b></span>` +
      `<span class="stat">Tồn kiện<b>${formatNumber(soKienSum)}</b></span>` +
      `<span class="stat">Tồn KG<b>${formatWeight(trongLuongSum)}</b></span>`;
  }

  function renderNguyenLieuPage(){
    const filtered = applyColumnSearch(nlRows, TARGET_COLS, nlSearchState.searchFilters, 'nl');
    updateNguyenLieuSummary(filtered);
    const total = filtered.length;
    const totalPages = Math.max(1, Math.ceil(total / nlPageSize));
    if(nlPage > totalPages) nlPage = totalPages;
    if(nlPage < 1) nlPage = 1;

    const start = (nlPage - 1) * nlPageSize;
    const pageRows = filtered.slice(start, start + nlPageSize)
      .map(r => rowToDisplayArray(r));

    if(!nlRows.length){
      tableScrollNL.innerHTML = '<div class="empty-table">Không có dòng nào có Size chứa NL trong dữ liệu đã nạp.</div>';
    } else {
      renderGenericTable(tableScrollNL, TARGET_COLS, pageRows, true, {
        state: nlSearchState,
        allRows: nlRows,
        tableId: 'nl',
        onSearchInput: (idx, val) => {
          nlSearchState.searchFilters[idx] = val;
          nlPage = 1;
          renderNguyenLieuPage();
        },
        onFilterApply: () => {
          nlPage = 1;
          renderNguyenLieuPage();
        }
      });
    }

    if(total > 0){
      const from = start + 1;
      const to = Math.min(start + nlPageSize, total);
      rowCountNL.textContent = `${formatNumber(total)} dòng (${from}–${to})`;
    } else {
      rowCountNL.textContent = '';
    }

    pageIndicatorNL.textContent = 'Trang ' + nlPage + '/' + totalPages;
    prevPageBtnNL.disabled = nlPage <= 1;
    nextPageBtnNL.disabled = nlPage >= totalPages;
    paginationNL.style.display = total > 0 ? 'flex' : 'none';
  }

  pageSizeSelectNL.addEventListener('change', ()=>{
    nlPageSize = parseInt(pageSizeSelectNL.value, 10);
    nlPage = 1;
    renderNguyenLieuPage();
  });
  prevPageBtnNL.addEventListener('click', ()=>{ nlPage--; renderNguyenLieuPage(); });
  nextPageBtnNL.addEventListener('click', ()=>{ nlPage++; renderNguyenLieuPage(); });

  exportBtnNL.addEventListener('click', ()=>{
    if(!nlRows.length) return;
    const data = nlRows.map(r => rowToExportObj(r));
    const ws = XLSX.utils.json_to_sheet(data);
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, 'Tom Nguyen Lieu');
    XLSX.writeFile(wb, 'tom-nguyen-lieu.xlsx');
  });

  // ---- TAB 5: Tôm HLSO (loại trừ Ecuador, Ấn Độ, TGC, TGS, Nguyên Liệu khỏi dữ liệu đã nạp ở tab 1) ----
  const toolbarLabel5 = document.getElementById('toolbarLabel5');
  const tableScroll5  = document.getElementById('tableScroll5');
  const rowCount5     = document.getElementById('rowCount5');
  const summaryStats5 = document.getElementById('summaryStats5');
  const pagination5   = document.getElementById('pagination5');
  const pageSizeSelect5 = document.getElementById('pageSizeSelect5');
  const prevPageBtn5  = document.getElementById('prevPage5');
  const nextPageBtn5  = document.getElementById('nextPage5');
  const pageIndicator5= document.getElementById('pageIndicator5');
  const exportBtn5    = document.getElementById('exportBtn5');

  let hlsoRows = [];
  let hlsoPage = 1;
  let hlsoPageSize = 100;
  let hlsoSearchState = { searchFilters:{}, lastHeaderKey:null, bound:false };

  // HLSO = phần còn lại sau khi loại trừ Ecuador, Ấn Độ, TGC, TGS và Nguyên Liệu (Size chứa NL)
  function isHLSOLot(row){
    return !isEcuadorLot(row.soLo) && !isAnDoLot(row.soLo) && !isTGCLot(row.soLo) && !isTGSLot(row.soLo) && !isNguyenLieuSize(row.size);
  }

  function refreshHLSOTab(){
    if(!persistedRows || !persistedRows.length){
      hlsoRows = [];
      toolbarLabel5.textContent = 'Chưa có dữ liệu';
      tableScroll5.innerHTML = '<div class="empty-table">Chưa có dữ liệu — hãy nạp file ở menu "Load data" trước.</div>';
      rowCount5.textContent = '';
      summaryStats5.innerHTML = '';
      pagination5.style.display = 'none';
      exportBtn5.style.display = 'none';
      return;
    }

    hlsoRows = persistedRows.filter(r => isHLSOLot(r));
    hlsoRows = applyCustomFiltersToRows(hlsoRows, '5');
    hlsoPage = 1;
    hlsoSearchState.searchFilters = {};
    hlsoSearchState.lastHeaderKey = null;
    resetTableCheckboxFilters('hlso');
    toolbarLabel5.textContent = 'Số lô HLSO (đã loại trừ Ecuador/Ấn Độ/TGC/TGS/Nguyên Liệu)';
    exportBtn5.style.display = hlsoRows.length ? 'inline-block' : 'none';
    updateHLSOSummary();
    renderCustomFilterArea('5', 'customFilterArea5', refreshHLSOTab);
    renderHLSOPage();
  }

  function updateHLSOSummary(rows){
    const data = rows || hlsoRows;
    if(!data.length){
      summaryStats5.innerHTML = `<span class="stat">Số dòng<b>0</b></span>`;
      return;
    }
    let soKienSum = 0, trongLuongSum = 0;
    data.forEach(r=>{
      soKienSum += toNumber(r.soKien);
      trongLuongSum += toNumber(r.trongLuong);
    });
    summaryStats5.innerHTML =
      `<span class="stat">Số dòng<b>${formatNumber(data.length)}</b></span>` +
      `<span class="stat">Tồn kiện<b>${formatNumber(soKienSum)}</b></span>` +
      `<span class="stat">Tồn KG<b>${formatWeight(trongLuongSum)}</b></span>`;
  }

  function renderHLSOPage(){
    const filtered = applyColumnSearch(hlsoRows, TARGET_COLS, hlsoSearchState.searchFilters, 'hlso');
    updateHLSOSummary(filtered);
    const total = filtered.length;
    const totalPages = Math.max(1, Math.ceil(total / hlsoPageSize));
    if(hlsoPage > totalPages) hlsoPage = totalPages;
    if(hlsoPage < 1) hlsoPage = 1;

    const start = (hlsoPage - 1) * hlsoPageSize;
    const pageRows = filtered.slice(start, start + hlsoPageSize)
      .map(r => rowToDisplayArray(r));

    if(!hlsoRows.length){
      tableScroll5.innerHTML = '<div class="empty-table">Không có dòng nào thuộc HLSO trong dữ liệu đã nạp.</div>';
    } else {
      renderGenericTable(tableScroll5, TARGET_COLS, pageRows, true, {
        state: hlsoSearchState,
        allRows: hlsoRows,
        tableId: 'hlso',
        onSearchInput: (idx, val) => {
          hlsoSearchState.searchFilters[idx] = val;
          hlsoPage = 1;
          renderHLSOPage();
        },
        onFilterApply: () => {
          hlsoPage = 1;
          renderHLSOPage();
        }
      });
    }

    if(total > 0){
      const from = start + 1;
      const to = Math.min(start + hlsoPageSize, total);
      rowCount5.textContent = `${formatNumber(total)} dòng (${from}–${to})`;
    } else {
      rowCount5.textContent = '';
    }

    pageIndicator5.textContent = 'Trang ' + hlsoPage + '/' + totalPages;
    prevPageBtn5.disabled = hlsoPage <= 1;
    nextPageBtn5.disabled = hlsoPage >= totalPages;
    pagination5.style.display = total > 0 ? 'flex' : 'none';
  }

  pageSizeSelect5.addEventListener('change', ()=>{
    hlsoPageSize = parseInt(pageSizeSelect5.value, 10);
    hlsoPage = 1;
    renderHLSOPage();
  });
  prevPageBtn5.addEventListener('click', ()=>{ hlsoPage--; renderHLSOPage(); });
  nextPageBtn5.addEventListener('click', ()=>{ hlsoPage++; renderHLSOPage(); });

  exportBtn5.addEventListener('click', ()=>{
    if(!hlsoRows.length) return;
    const data = hlsoRows.map(r => rowToExportObj(r));
    const ws = XLSX.utils.json_to_sheet(data);
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, 'Tom HLSO');
    XLSX.writeFile(wb, 'tom-hlso.xlsx');
  });

  /* PC 7.54 — bảng "Đặc Tính" / "Gộp Đặc Tính", nạp tay, chuyển số lô sang đặc tính và điều kiện lọc tự thêm ở các tab Phân Loại làm đổi số liệu
     báo cáo → chỉ vai trò Quản lý trở lên (user.canWrite) được sửa; Người dùng cấp 1 / cấp 2 chỉ xem. Chặn ở giao diện (nút khóa) VÀ ở từng hàm
     lưu (saveDt*, persistCustomFilters) + từng handler (m2DtDenied) để không đổi được dữ liệu trong bộ nhớ. */
  const M2_DT_LOCK_MSG = 'Bạn không được cấp quyền: chỉ vai trò Quản lý trở lên mới được sửa Đặc Tính / điều kiện phân loại.';
  function m2DtDenied(){
    if(m2CanWrite) return false;
    try{ alert(M2_DT_LOCK_MSG); }catch(e){ window.klLog && klLog('ton-kho-gui.js:3795', e); }
    return true;
  }
  function applyDtLock(){
    if(m2CanWrite) return;
    const off = (el)=>{ if(el){ el.disabled = true; el.title = 'Chỉ vai trò Quản lý trở lên được sửa'; el.style.opacity = '.45'; el.style.cursor = 'not-allowed'; } };
    ['dtMergeBtn','dtClearSelBtn','dtClearAllBtn','dtGroupSelect','dtParsePasteBtn','dtClearManualBtn','dtFileInput'].forEach(id => off(document.getElementById(id)));
    const pa = document.getElementById('dtPasteArea'); if(pa){ pa.readOnly = true; pa.style.opacity = '.55'; }
    const dz = document.getElementById('dtDropZone'); if(dz){ dz.style.pointerEvents = 'none'; dz.style.opacity = '.45'; }
    ['dtSub-1','dtSub-2'].forEach(id=>{
      const p = document.getElementById(id);
      if(!p || p.querySelector('.kla-dt-lock-note')) return;
      const n = document.createElement('div');
      n.className = 'kla-dt-lock-note';
      n.style.cssText = 'padding:8px 12px;border-radius:8px;border:1px solid rgba(250,204,21,.45);background:rgba(250,204,21,.10);font-size:12.5px;';
      n.textContent = '🔒 Bạn chỉ được xem. Chỉ vai trò Quản lý trở lên mới được sửa Đặc Tính (nạp tay, gộp nhóm, bỏ gộp, chuyển số lô, xóa).';
      p.insertBefore(n, p.firstChild);
    });
  }
  (function loadM2Role(){
    try{
      const api = window.authAPI;
      if(!api || typeof api.state !== 'function') return;
      api.state().then(function(st){
        m2CanWrite = !(st && st.ok && st.user) || st.user.canWrite !== false;
        if(!m2CanWrite){
          applyDtLock();
          try{ refreshDacTinhTab(); }catch(e){ window.klLog && klLog('ton-kho-gui.js:3822', e); }
          try{ renderDtManualPreview(); }catch(e){ window.klLog && klLog('ton-kho-gui.js:3823', e); }
        }
      }, function(){});
    }catch(e){ window.klLog && klLog('ton-kho-gui.js:3826', e); }
  })();

  // ---- TAB 6 / SUB-MENU 1: Nạp dữ liệu Đặc Tính (file Excel 2 cột hoặc dán tay) ----
  const DT_MANUAL_STORAGE_KEY = 'dataConsole_dacTinhManualRows_v1';
  let dtManualRows = [];   // [{ maHang, dacTinh }]
  let dtManualWorkbook = null;

  function loadDtManualRows(){
    try{
      const raw = localStorage.getItem(DT_MANUAL_STORAGE_KEY);
      dtManualRows = raw ? JSON.parse(raw) : [];
    }catch(e){ dtManualRows = []; }
  }
  async function saveDtManualRows(){
    if(!m2CanWrite) return;
    try{ localStorage.setItem(DT_MANUAL_STORAGE_KEY, JSON.stringify(dtManualRows)); }catch(e){}
    if(hasDbAPI()){
      try{
        // Cùng 2 nhãn cột với nút "Xuất Excel" sẵn có của tab này (xem dtExportManual bên dưới),
        // để mở file database bằng Excel cũng đọc hiểu được ngay.
        const rows = dtManualRows.map(r => ({ 'Diễn giải đặc tính': r.maHang, 'Đặc tính': r.dacTinh }));
        const res = await window.dbAPI.writeSheet(DB_KEY, 'DacTinh_NhapTay', rows);
        if(!res || !res.ok) console.error('Lỗi lưu database Excel (module6 - đặc tính nhập tay):', res && res.error);
      }catch(e){ console.error('Lỗi lưu database Excel (module6 - đặc tính nhập tay):', e); }
    }
  }
  async function hydrateDtManualRowsFromDb(){
    if(!hasDbAPI()) return false;
    try{
      const res = await window.dbAPI.readSheet(DB_KEY, 'DacTinh_NhapTay');
      if(res && res.ok){
        dtManualRows = (res.data || [])
          .map(r => ({ maHang: String(r['Diễn giải đặc tính'] ?? ''), dacTinh: String(r['Đặc tính'] ?? '') }))
          .filter(r => r.dacTinh);
        return true;
      }
      console.error('Đọc database Excel (module6 - đặc tính nhập tay) không hợp lệ, giữ nguyên dữ liệu localStorage.');
    }catch(e){ console.error('Lỗi đọc database Excel (module6 - đặc tính nhập tay):', e); }
    return false;
  }
  loadDtManualRows();

  // ---- CHUYỂN TAY "Số lô" -> "Đặc tính" ----
  // Dòng có đặc tính = CKQ được lấy Số lô làm đặc tính (xem deriveDacTinhFromTenHang). Sau khi kiểm tra
  // thực tế, người dùng chuyển từng Số lô đó về 1 đặc tính thật (VD "04.04.26 L12 NÂU" -> "23.MKS") bằng
  // nút "⇄ Chuyển" ở tab Đặc Tính › Gộp đặc tính. Lưu kép: localStorage + sheet Excel "DacTinh_ChuyenLo"
  // (cột "Số lô", "Đặc tính"). main.js › m2WithDerivedDacTinh() đọc cùng sheet này — sửa 1 bên thì sửa bên kia.
  const DT_LOMAP_STORAGE_KEY = 'dataConsole_dacTinhLoMap_v1';
  let dtLoMap = {};   // { '<số lô chữ thường>': { lo, dacTinh } }
  function loKey(s){ return String(s ?? '').trim().toLowerCase(); }
  function loadDtLoMap(){
    try{
      const raw = localStorage.getItem(DT_LOMAP_STORAGE_KEY);
      dtLoMap = raw ? JSON.parse(raw) : {};
    }catch(e){ dtLoMap = {}; }
  }
  async function saveDtLoMap(){
    if(!m2CanWrite) return;
    try{ localStorage.setItem(DT_LOMAP_STORAGE_KEY, JSON.stringify(dtLoMap)); }catch(e){}
    if(hasDbAPI()){
      try{
        const rows = Object.values(dtLoMap).map(r => ({ 'Số lô': r.lo, 'Đặc tính': r.dacTinh }));
        const res = await window.dbAPI.writeSheet(DB_KEY, 'DacTinh_ChuyenLo', rows);
        if(!res || !res.ok) console.error('Lỗi lưu database Excel (module6 - chuyển số lô sang đặc tính):', res && res.error);
      }catch(e){ console.error('Lỗi lưu database Excel (module6 - chuyển số lô sang đặc tính):', e); }
    }
  }
  async function hydrateDtLoMapFromDb(){
    if(!hasDbAPI()) return false;
    try{
      const res = await window.dbAPI.readSheet(DB_KEY, 'DacTinh_ChuyenLo');
      if(res && res.ok){
        const map = {};
        (res.data || []).forEach(r=>{
          const lo = String(r['Số lô'] ?? '').trim();
          const dt = String(r['Đặc tính'] ?? '').trim();
          if(lo && dt) map[loKey(lo)] = { lo, dacTinh: dt };
        });
        dtLoMap = map;
        return true;
      }
    }catch(e){ /* sheet chưa có ở database cũ, bỏ qua */ }
    return false;
  }
  loadDtLoMap();

  const dtDropZone     = document.getElementById('dtDropZone');
  const dtFileInput    = document.getElementById('dtFileInput');
  const dtFileStatus   = document.getElementById('dtFileStatus');
  const dtFileInfo     = document.getElementById('dtFileInfo');
  const dtFName        = document.getElementById('dtFName');
  const dtFSheets      = document.getElementById('dtFSheets');
  const dtSheetSelect  = document.getElementById('dtSheetSelect');
  const dtPasteArea    = document.getElementById('dtPasteArea');
  const dtParsePasteBtn= document.getElementById('dtParsePasteBtn');
  const dtClearManualBtn = document.getElementById('dtClearManualBtn');
  const dtManualScroll = document.getElementById('dtManualScroll');
  const dtManualTotal  = document.getElementById('dtManualTotal');
  const dtExportManual = document.getElementById('dtExportManual');

  function setDtFileStatus(msg, type){
    dtFileStatus.textContent = msg;
    dtFileStatus.className = 'status-line' + (type ? ' '+type : '');
  }

  function addManualRows(newRows){
    if(!m2CanWrite) return 0;
    const seen = new Set(dtManualRows.map(r => normalize(r.maHang) + '|' + normalize(r.dacTinh)));
    let added = 0;
    newRows.forEach(r=>{
      if(!r.dacTinh) return;
      const key = normalize(r.maHang) + '|' + normalize(r.dacTinh);
      if(seen.has(key)) return;
      seen.add(key);
      dtManualRows.push(r);
      added++;
    });
    if(added) saveDtManualRows();
    return added;
  }

  function renderDtManualPreview(){
    dtManualTotal.textContent = 'Tổng: ' + formatNumber(dtManualRows.length);
    if(!dtManualRows.length){
      dtManualScroll.innerHTML = '<div class="empty-table">Chưa có dữ liệu nhập tay</div>';
      return;
    }
    let html = '<table data-kcf data-kcf-key="m2:dtnhaptay"><thead><tr><th>Diễn giải đặc tính</th><th>Đặc tính</th><th style="width:36px;"></th></tr></thead><tbody>';
    dtManualRows.forEach((r, i)=>{
      html += `<tr><td>${escapeHtml(r.maHang || '')}</td><td>${escapeHtml(r.dacTinh || '')}</td>` +
        `<td>${m2CanWrite ? `<button data-delrow="${i}" style="border:none; background:none; color:var(--danger); cursor:pointer; font-size:13px;" title="Xoá dòng này">✕</button>` : ''}</td></tr>`;
    });
    html += '</tbody></table>';
    dtManualScroll.innerHTML = html;
    dtManualScroll.querySelectorAll('[data-delrow]').forEach(btn=>{
      btn.addEventListener('click', async ()=>{
        if(m2DtDenied()) return;
        const ok = await showConfirmDialog({
          title: 'Xác nhận xóa dòng',
          message: 'Xóa dòng đặc tính này?',
          confirmText: 'Xóa dòng',
          danger: true
        });
        if(!ok) return;
        dtManualRows.splice(parseInt(btn.dataset.delrow, 10), 1);
        saveDtManualRows();
        renderDtManualPreview();
        refreshDacTinhTab();
      });
    });
  }

  function parsePastedText(text){
    const lines = text.split(/\r?\n/).map(l=>l.trim()).filter(l=>l.length);
    if(!lines.length) return [];
    let startIdx = 0;
    const firstNorm = normalize(lines[0]);
    if(firstNorm.includes('ma hang') || firstNorm.includes('dac tinh')) startIdx = 1;
    const rows = [];
    for(let i=startIdx;i<lines.length;i++){
      let maHang = '', dacTinh = '';
      const partsTab = lines[i].split(/\t/);
      if(partsTab.length >= 2){
        maHang = partsTab[0].trim();
        dacTinh = partsTab.slice(1).join(' ').trim();
      } else {
        const partsSp = lines[i].split(/\s{2,}/);
        if(partsSp.length >= 2){
          maHang = partsSp[0].trim();
          dacTinh = partsSp.slice(1).join(' ').trim();
        } else {
          dacTinh = lines[i].trim();
        }
      }
      if(dacTinh) rows.push({ maHang, dacTinh });
    }
    return rows;
  }

  dtDropZone.addEventListener('click', ()=>{ if(m2DtDenied()) return; dtFileInput.click(); });
  dtDropZone.addEventListener('dragover', e=>{ e.preventDefault(); dtDropZone.classList.add('drag'); });
  dtDropZone.addEventListener('dragleave', ()=> dtDropZone.classList.remove('drag'));
  dtDropZone.addEventListener('drop', e=>{
    e.preventDefault();
    dtDropZone.classList.remove('drag');
    if(m2DtDenied()) return;
    if(e.dataTransfer.files.length) handleDtFile(e.dataTransfer.files[0]);
  });
  dtFileInput.addEventListener('change', e=>{
    if(e.target.files.length) handleDtFile(e.target.files[0]);
  });

  function handleDtFile(file){
    setDtFileStatus('Đang đọc file...', null);
    const reader = new FileReader();
    reader.onload = (e)=>{
      try{
        const data = new Uint8Array(e.target.result);
        dtManualWorkbook = XLSX.read(data, {type:'array'});

        dtFName.textContent = file.name;
        dtFSheets.textContent = dtManualWorkbook.SheetNames.length;

        dtSheetSelect.innerHTML = '';
        dtManualWorkbook.SheetNames.forEach(name=>{
          const opt = document.createElement('option');
          opt.value = name; opt.textContent = name;
          dtSheetSelect.appendChild(opt);
        });

        dtFileInfo.classList.add('show');
        renderDtManualSheet(dtManualWorkbook.SheetNames[0]);
      }catch(err){
        console.error(err);
        setDtFileStatus('Lỗi khi đọc file: ' + err.message, 'err');
      }
    };
    reader.onerror = ()=> setDtFileStatus('Không thể đọc file này.', 'err');
    reader.readAsArrayBuffer(file);
  }

  dtSheetSelect.addEventListener('change', ()=> renderDtManualSheet(dtSheetSelect.value));

  function renderDtManualSheet(sheetName){
    const ws = dtManualWorkbook.Sheets[sheetName];
    const json = XLSX.utils.sheet_to_json(ws, {header:1, defval:''});
    if(!json.length){
      setDtFileStatus('Sheet này không có dữ liệu.', 'err');
      return;
    }
    const header = json[0];
    let colMa = findCol(header, 'ma hang');
    let colDt = findCol(header, 'dac tinh');
    let startRow = 1;
    if(colMa === -1 && colDt === -1){
      colMa = 0; colDt = 1; startRow = 0; // không nhận diện được nhãn cột -> mặc định cột A/B, không bỏ dòng đầu
    } else {
      if(colMa === -1) colMa = (colDt === 0 ? 1 : 0);
      if(colDt === -1) colDt = (colMa === 0 ? 1 : 0);
    }
    const rows = [];
    for(let r=startRow;r<json.length;r++){
      const row = json[r] || [];
      const dt = String(row[colDt] ?? '').trim();
      if(!dt) continue;
      rows.push({ maHang: String(row[colMa] ?? '').trim(), dacTinh: dt });
    }
    const added = addManualRows(rows);
    renderDtManualPreview();
    refreshDacTinhTab();
    setDtFileStatus('Đã nạp ' + added + ' dòng mới từ sheet "' + sheetName + '" (bỏ qua trùng lặp).', 'ok');
  }

  dtParsePasteBtn.addEventListener('click', ()=>{
    if(m2DtDenied()) return;
    const text = dtPasteArea.value;
    if(!text.trim()){ setDtFileStatus('Vui lòng dán dữ liệu trước khi nạp.', 'err'); return; }
    const rows = parsePastedText(text);
    if(!rows.length){ setDtFileStatus('Không đọc được dữ liệu hợp lệ từ nội dung đã dán.', 'err'); return; }
    const added = addManualRows(rows);
    dtPasteArea.value = '';
    renderDtManualPreview();
    refreshDacTinhTab();
    setDtFileStatus('Đã nạp ' + added + ' dòng mới từ dữ liệu dán (bỏ qua trùng lặp).', 'ok');
  });

  dtClearManualBtn.addEventListener('click', async ()=>{
    if(m2DtDenied()) return;
    const ok = await showConfirmDialog({
      title: 'Xác nhận xóa dữ liệu',
      message: 'Xoá toàn bộ dữ liệu đặc tính đã nhập tay/nạp file? Hành động này không thể hoàn tác.',
      confirmText: 'Xóa toàn bộ',
      danger: true
    });
    if(!ok) return;
    dtManualRows = [];
    saveDtManualRows();
    renderDtManualPreview();
    refreshDacTinhTab();
  });

  dtExportManual.addEventListener('click', ()=>{
    if(!dtManualRows.length) return;
    const data = dtManualRows.map(r => ({ 'Diễn giải đặc tính': r.maHang, 'Đặc tính': r.dacTinh }));
    const ws = XLSX.utils.json_to_sheet(data);
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, 'Dac tinh nhap tay');
    XLSX.writeFile(wb, 'dac-tinh-nhap-tay.xlsx');
  });

  // ---- TAB 6 sub-nav (Đặc tính / Gộp đặc tính) ----
  document.querySelectorAll('.dt-subtab-btn').forEach(btn=>{
    btn.addEventListener('click', ()=>{
      document.querySelectorAll('.dt-subtab-btn').forEach(b=>b.classList.remove('active'));
      btn.classList.add('active');
      document.getElementById('dtSub-1').style.display = btn.dataset.dtsub === '1' ? 'flex' : 'none';
      document.getElementById('dtSub-2').style.display = btn.dataset.dtsub === '2' ? 'flex' : 'none';
      if(btn.dataset.dtsub === '1') renderDtManualPreview();
      if(btn.dataset.dtsub === '2') refreshDacTinhTab();
    });
  });

  // ---- TAB 6 / SUB-MENU 2: Đặc Tính (gộp giá trị cột "Đặc tính" thành nhóm cố định) ----
  const DT_GROUPS = [
    'MKS',
    'NHẬT, ASC, HQ, MỸ',
    'NHẬT, ASC-A,B, HQ, MỸ',
    'NHẬT, ASC-B, HQ, MỸ',
    'NHẬT, ASC, HQ',
    'NHẬT, EU, HQ',
    'NHẬT, ASC-A,B, HQ',
    'NHẬT, ASC-B, HQ',
    'NHẬT, ASC',
    'NHẬT, HQ',
    'NHẬT, EU',
    'ASC, HQ, MỸ',
    'ASC-A, B, HQ, MỸ',
    'ASC-B, HQ, MỸ',
    'ASC, HQ',
    'ASC-A,B, HQ',
    'ASC-B, HQ',
    'ASC',
    'EU, HQ, MỸ',
    'EU, HQ',
    'EU',
    'HQ',
    'KHÔNG ĐẠT',
  ];
  const DT_STORAGE_KEY = 'dataConsole_dacTinhGroupMap_v1';
  // Lưu trữ lâu dài TẤT CẢ giá trị "Đặc tính" TỪNG xuất hiện (kể cả đã gộp hay CHƯA gộp) — để
  // khi anh bấm "Xoá dữ liệu đã lưu" ở tab "Dữ Liệu", các đặc tính CHƯA GỘP đã từng thấy trước
  // đó (suy ra từ dữ liệu cũ) vẫn còn nguyên trong danh sách để gộp sau này, không bị mất theo
  // dữ liệu nguồn. Khác với dtGroupMap (chỉ lưu đặc tính ĐÃ gộp) — đây lưu CẢ đặc tính CHƯA gộp.
  const DT_SEEN_STORAGE_KEY = 'dataConsole_dacTinhSeenValues_v1';
  let dtSeenValues = new Set();

  let dtGroupMap = {};      // { [giá trị đặc tính]: tên nhóm }
  let dtCheckedSet = new Set();
  let dtSearchTerm = '';
  let dtFilterStatus = 'all'; // 'all' | 'unmerged' | 'merged' — lọc theo trạng thái gộp ở bảng "Đặc Tính"

  function loadDtGroupMap(){
    try{
      const raw = localStorage.getItem(DT_STORAGE_KEY);
      dtGroupMap = raw ? JSON.parse(raw) : {};
    }catch(e){ dtGroupMap = {}; }
  }
  async function saveDtGroupMap(){
    if(!m2CanWrite) return;
    try{ localStorage.setItem(DT_STORAGE_KEY, JSON.stringify(dtGroupMap)); }catch(e){}
    if(hasDbAPI()){
      try{
        const rows = Object.keys(dtGroupMap).map(k => ({ 'Đặc tính': k, 'Nhóm đã gộp': dtGroupMap[k] }));
        const res = await window.dbAPI.writeSheet(DB_KEY, 'DacTinh_NhomGop', rows);
        if(!res || !res.ok) console.error('Lỗi lưu database Excel (module6 - nhóm đặc tính):', res && res.error);
      }catch(e){ console.error('Lỗi lưu database Excel (module6 - nhóm đặc tính):', e); }
    }
  }
  async function hydrateDtGroupMapFromDb(){
    if(!hasDbAPI()) return false;
    try{
      const res = await window.dbAPI.readSheet(DB_KEY, 'DacTinh_NhomGop');
      if(res && res.ok){
        const map = {};
        (res.data || []).forEach(r=>{
          const k = String(r['Đặc tính'] ?? '').trim();
          if(k) map[k] = String(r['Nhóm đã gộp'] ?? '');
        });
        dtGroupMap = map;
        return true;
      }
      console.error('Đọc database Excel (module6 - nhóm đặc tính) không hợp lệ, giữ nguyên dữ liệu localStorage.');
    }catch(e){ console.error('Lỗi đọc database Excel (module6 - nhóm đặc tính):', e); }
    return false;
  }
  loadDtGroupMap();

  function loadDtSeenValues(){
    try{
      const raw = localStorage.getItem(DT_SEEN_STORAGE_KEY);
      dtSeenValues = raw ? new Set(JSON.parse(raw)) : new Set();
    }catch(e){ dtSeenValues = new Set(); }
  }
  async function saveDtSeenValues(){
    if(!m2CanWrite) return;
    const arr = Array.from(dtSeenValues);
    try{ localStorage.setItem(DT_SEEN_STORAGE_KEY, JSON.stringify(arr)); }catch(e){}
    if(hasDbAPI()){
      try{
        const rows = arr.map(v => ({ 'Đặc tính': v }));
        const res = await window.dbAPI.writeSheet(DB_KEY, 'DacTinh_DaGhiNhan', rows);
        if(!res || !res.ok) console.error('Lỗi lưu database Excel (module6 - đặc tính đã ghi nhận):', res && res.error);
      }catch(e){ console.error('Lỗi lưu database Excel (module6 - đặc tính đã ghi nhận):', e); }
    }
  }
  async function hydrateDtSeenValuesFromDb(){
    if(!hasDbAPI()) return false;
    try{
      const res = await window.dbAPI.readSheet(DB_KEY, 'DacTinh_DaGhiNhan');
      if(res && res.ok){
        const set = new Set();
        (res.data || []).forEach(r=>{
          const v = String(r['Đặc tính'] ?? '').trim();
          if(v) set.add(v);
        });
        dtSeenValues = set;
        return true;
      }
      // Sheet có thể chưa tồn tại ở database cũ (mới thêm tính năng) — không phải lỗi, bỏ qua.
    }catch(e){ /* sheet chưa có ở database cũ, bỏ qua */ }
    return false;
  }
  loadDtSeenValues();

  const dtGroupSelect = document.getElementById('dtGroupSelect');
  const dtMergeBtn    = document.getElementById('dtMergeBtn');
  const dtClearSelBtn = document.getElementById('dtClearSelBtn');
  const dtClearAllBtn = document.getElementById('dtClearAllBtn');
  const dtSearch       = document.getElementById('dtSearch');
  const dtFilterSelect = document.getElementById('dtFilterStatus');
  const dtExportLeft   = document.getElementById('dtExportLeft');
  const dtExportRight  = document.getElementById('dtExportRight');
  const dtLeftScroll   = document.getElementById('dtLeftScroll');
  const dtRightScroll  = document.getElementById('dtRightScroll');
  const dtLeftTotal    = document.getElementById('dtLeftTotal');

  DT_GROUPS.forEach(g=>{
    const opt = document.createElement('option');
    opt.value = g; opt.textContent = g;
    dtGroupSelect.appendChild(opt);
  });

  // ---- Suy ra "Đặc tính" từ cột "Tên hàng" của bảng "Dữ liệu đã lọc" ----
  // Điều kiện 1: lấy phần nằm sau dấu "," cuối cùng của Tên hàng (đã trim khoảng trắng).
  //   VD: "TÔM TCT BTP50, BLOCK, 1.8Kg x 5, 0083" -> "0083"
  //       "TÔM TCT 41/50, BLOCK, 1.8Kg x 7, ASCN" -> "ASCN"
  //       "TÔM TCT BTP35, IQF, 9Kg x 1, CKQ"      -> "CKQ"
  function lastCommaSegment(tenHang){
    const s = String(tenHang ?? '').trim();
    if(!s) return '';
    if(!s.includes(',')) return s;
    const parts = s.split(',');
    return parts[parts.length - 1].trim();
  }

  // Điều kiện 2: đối chiếu giá trị lấy được ở điều kiện 1 với bảng "Dữ liệu Đặc Tính đã nhập"
  //   (cột "Đặc tính", dạng "<mã số>.<đặc tính>", VD "83.EU-H-M"). Nếu phần đứng trước dấu "."
  //   đầu tiên trùng với giá trị điều kiện 1 (so cả dạng chuỗi và dạng số, bỏ qua số 0 ở đầu),
  //   trả về TOÀN BỘ giá trị "Đặc tính" đã nhập đó; nếu không tìm thấy trùng, giữ nguyên kết quả điều kiện 1.
  function matchManualDacTinhPrefix(v){
    if(!v) return null;
    const vTrim = String(v).trim();
    if(!vTrim) return null;
    const vNum = /^\d+$/.test(vTrim) ? String(parseInt(vTrim,10)) : null;
    for(const r of dtManualRows){
      const dt = String(r.dacTinh ?? '').trim();
      if(!dt) continue;
      const m = dt.match(/^([^.]+)\./);
      if(!m) continue;
      const prefix = m[1].trim();
      if(prefix === vTrim) return dt;
      if(vNum !== null && /^\d+$/.test(prefix) && String(parseInt(prefix,10)) === vNum) return dt;
    }
    return null;
  }

  // Hàm dùng chung: suy ra giá trị "đặc tính" cuối cùng cho 1 dòng dữ liệu (từ Tên hàng),
  // áp dụng đúng điều kiện 1 + điều kiện 2 ở trên. Dùng lại ở TẤT CẢ nơi cần đặc tính (Dữ Liệu,
  // Đặc Tính, Báo Cáo) để đảm bảo mọi nơi luôn tính ra cùng một giá trị cho cùng một dòng dữ liệu.
  // TRƯỜNG HỢP RIÊNG "CKQ": không khớp được với bảng "Diễn giải đặc tính" nào (không theo đúng
  // quy ước "<số>.<mã>") — theo yêu cầu, dùng luôn giá trị cột "Số lô" của DÒNG ĐÓ làm "đặc
  // tính" thay vì gộp chung 1 chữ "CKQ", để có thể TÁCH RIÊNG từng lô ra gộp nhóm khác nhau ở
  // tab "Đặc Tính" (mỗi lô là 1 dòng riêng trong danh sách đó) — CHỈ áp dụng riêng cho "CKQ",
  // mọi giá trị đặc tính khác vẫn hoạt động y hệt như cũ.
  function deriveDacTinhFromTenHang(tenHang, soLo){
    const seg = lastCommaSegment(tenHang);
    if(!seg) return '';
    const matched = matchManualDacTinhPrefix(seg);
    const result = matched || seg;
    if(result === 'CKQ'){
      const lo = String(soLo ?? '').trim();
      const mapped = lo ? dtLoMap[loKey(lo)] : null;
      if(mapped && mapped.dacTinh) return mapped.dacTinh;   // đã chuyển tay Số lô -> Đặc tính
      return lo;
    }
    return result;
  }

  // Tập các Số lô của những dòng có đặc tính = CKQ (những giá trị có thể chuyển tay sang đặc tính thật).
  function getCkqLotSet(){
    const set = new Set();
    (persistedRows || []).forEach(r=>{
      const seg = lastCommaSegment(r.tenHang);
      if(!seg) return;
      if((matchManualDacTinhPrefix(seg) || seg) !== 'CKQ') return;
      const lo = String(r.soLo ?? '').trim();
      if(lo) set.add(lo);
    });
    return set;
  }

  // Quy đổi 1 giá trị đặc tính "dạng số thô" (VD "0023", "23") về đúng dạng đầy đủ trong bảng
  // "Dữ liệu Đặc Tính đã nhập" (VD "23.MKS"). Cũng xử lý "0023.MKS" -> "23.MKS" (bỏ số 0 đầu,
  // không phân biệt hoa/thường). Không tìm thấy trong bảng nhập tay thì giữ nguyên.
  function canonDacTinhValue(v){
    const s = String(v ?? '').trim();
    if(!s || !dtManualRows.length) return s;
    if(/^\d+$/.test(s)){
      const m = matchManualDacTinhPrefix(s);
      return m || s;
    }
    const mm = s.match(/^0*(\d+)\.(.+)$/);
    if(mm){
      const cand = (mm[1] + '.' + mm[2].trim()).toLowerCase();
      for(const r of dtManualRows){
        const dt = String(r.dacTinh ?? '').trim();
        if(dt && dt.toLowerCase() === cand) return dt;
      }
    }
    return s;
  }

  // Dọn các giá trị cũ đã lưu (dtSeenValues / dtGroupMap) có dạng số thô như "0023":
  // đổi sang dạng "23.MKS" và chuyển luôn nhóm đã gộp sang khoá mới (nếu khoá mới chưa có nhóm).
  function migrateDtAliases(){
    if(!dtManualRows.length) return;
    let seenChanged = false, mapChanged = false;
    Array.from(dtSeenValues).forEach(v=>{
      const c = canonDacTinhValue(v);
      if(c !== v){ dtSeenValues.delete(v); dtSeenValues.add(c); seenChanged = true; }
    });
    Object.keys(dtGroupMap).forEach(k=>{
      const c = canonDacTinhValue(k);
      if(c !== k){
        if(!dtGroupMap[c]) dtGroupMap[c] = dtGroupMap[k];
        delete dtGroupMap[k]; mapChanged = true;
      }
    });
    if(seenChanged) saveDtSeenValues();
    if(mapChanged) saveDtGroupMap();
  }

  function getUniqueDacTinhValues(){
    migrateDtAliases();
    const set = new Set();
    if(persistedRows && persistedRows.length){
      persistedRows.forEach(r=>{
        const v = deriveDacTinhFromTenHang(r.tenHang, r.soLo);
        if(v) set.add(v);
      });
    }
    if(rawDacTinhValues && rawDacTinhValues.length){
      rawDacTinhValues.forEach(r=>{
        const v = canonDacTinhValue(r.dacTinh);
        if(v) set.add(v);
      });
    }
    dtManualRows.forEach(r=>{
      const v = String(r.dacTinh ?? '').trim();
      if(v) set.add(v);
    });

    // Ghi nhận LÂU DÀI mọi giá trị vừa thấy được ở trên vào dtSeenValues (localStorage + Excel)
    // — để nếu sau này anh xoá dữ liệu ở tab "Dữ Liệu" (persistedRows rỗng), các đặc tính CHƯA
    // GỘP đã từng thấy trước đó (đặc biệt các "Số lô" tách ra từ "CKQ") KHÔNG bị mất khỏi danh
    // sách bên dưới, vẫn còn đó để gộp sau. Chỉ lưu lại khi thực sự có giá trị MỚI (tránh ghi
    // đĩa liên tục mỗi lần bảng vẽ lại).
    let hasNewSeen = false;
    set.forEach(v=>{
      if(!dtSeenValues.has(v)){ dtSeenValues.add(v); hasNewSeen = true; }
    });
    if(hasNewSeen) saveDtSeenValues();

    // Kết quả cuối = giá trị đang thấy trong dữ liệu hiện tại HỢP với mọi giá trị đã từng ghi
    // nhận trước đó (dtSeenValues) — đảm bảo không có gì biến mất khỏi danh sách.
    dtSeenValues.forEach(v=> set.add(v));
    // Số lô đã chuyển tay sang đặc tính thật thì không còn là 1 đặc tính riêng nữa -> ẩn khỏi danh sách.
    Array.from(set).forEach(v=>{ if(dtLoMap[loKey(v)]) set.delete(v); });
    return Array.from(set).sort((a,b)=> a.localeCompare(b, 'vi', {numeric:true}));
  }

  // Tra "Diễn giải đặc tính" (cột 1 của bảng "Dữ liệu Đặc Tính đã nhập") theo giá trị đặc tính
  // (so khớp không phân biệt hoa/thường). Không có thì trả về chuỗi rỗng.
  function getDienGiaiDacTinh(v){
    const key = String(v ?? '').trim().toLowerCase();
    if(!key) return '';
    for(const r of dtManualRows){
      if(String(r.dacTinh ?? '').trim().toLowerCase() === key) return String(r.maHang ?? '').trim();
    }
    return '';
  }

  function refreshDacTinhTab(){
    renderDtLeft();
    renderDtRight();
  }

  function renderDtLeft(){
    // Giữ nguyên vị trí cuộn hiện tại — tránh màn hình bị giật/nhảy lên đầu bảng
    // mỗi khi tick chọn, gộp nhóm, bỏ khỏi nhóm... (các thao tác gọi lại renderDtLeft()).
    const scrollTop = dtLeftScroll.scrollTop;

    const all = getUniqueDacTinhValues();
    // Kèm số đặc tính CHƯA gộp vào nhóm nào (toàn bộ danh sách, không phụ thuộc ô tìm kiếm / bộ lọc).
    dtLeftTotal.textContent = 'Tổng: ' + formatNumber(all.length) + ' · Chưa gộp: ' + formatNumber(all.filter(v => !dtGroupMap[v]).length);

    const term = normalize(dtSearchTerm);
    let rows = term ? all.filter(v => normalize(v).includes(term)) : all.slice();

    if(dtFilterStatus === 'unmerged') rows = rows.filter(v => !dtGroupMap[v]);
    else if(dtFilterStatus === 'merged') rows = rows.filter(v => !!dtGroupMap[v]);

    // Đẩy các đặc tính đã gộp (đã "ẩn" khỏi danh sách chọn) xuống cuối bảng; các đặc tính
    // chưa gộp nằm lên trên. Dùng sort ổn định (stable sort) nên trong từng nhóm (chưa gộp /
    // đã gộp) thứ tự tương đối theo số 1 > 83 (đã có sẵn từ getUniqueDacTinhValues) được giữ nguyên.
    rows = rows
      .map((v, i) => ({ v, i, merged: !!dtGroupMap[v] }))
      .sort((a, b) => (a.merged === b.merged) ? (a.i - b.i) : (a.merged ? 1 : -1))
      .map(r => r.v);

    if(!all.length){
      dtLeftScroll.innerHTML = '<div class="empty-table">Chưa có dữ liệu — hãy nạp file ở menu "Load data" trước.</div>';
      dtLeftScroll.scrollTop = scrollTop;
      return;
    }
    if(!rows.length){
      dtLeftScroll.innerHTML = '<div class="empty-table">Không tìm thấy đặc tính khớp tìm kiếm / bộ lọc.</div>';
      dtLeftScroll.scrollTop = scrollTop;
      return;
    }

    // Đặc tính đã có nhóm (đã gộp) thì khóa lại — không cho tick chọn để gộp thêm lần nữa
    // cho tới khi bị bỏ khỏi nhóm (qua nút × ở đây hoặc ở bảng "Đặc tính đã gộp").
    rows.forEach(v=>{ if(dtGroupMap[v]) dtCheckedSet.delete(v); });

    const selectableRows = rows.filter(v => !dtGroupMap[v]);
    const allChecked = selectableRows.length > 0 && selectableRows.every(v => dtCheckedSet.has(v));
    let html = '<table class="dt-fit-table"><thead><tr>' +
      `<th style="width:36px;"><input type="checkbox" id="dtSelectAll"${allChecked ? ' checked' : ''}${(selectableRows.length && m2CanWrite) ? '' : ' disabled'}></th>` +
      '<th>ĐẶC TÍNH</th><th>DIỄN GIẢI ĐẶC TÍNH</th></tr></thead><tbody>';
    const ckqLots = getCkqLotSet();
    rows.forEach(v=>{
      const grp = dtGroupMap[v];
      const locked = !!grp;
      const checked = (!locked && dtCheckedSet.has(v)) ? ' checked' : '';
      const pill = grp
        ? `<span class="dt-pill">${escapeHtml(grp)}${m2CanWrite ? `<button data-untag="${escapeHtml(v)}" title="Bỏ khỏi nhóm">×</button>` : ''}</span>`
        : '';
      const rowTitle = locked ? ` title="Đã gộp vào nhóm \\"${escapeHtml(grp)}\\" — bỏ khỏi nhóm trước khi gộp lại"` : '';
      html += `<tr class="${locked ? 'dt-row-locked' : ''}"${rowTitle}>` +
        `<td><input type="checkbox" class="dt-row-check" data-value="${escapeHtml(v)}"${checked}${(locked || !m2CanWrite) ? ' disabled' : ''}></td>` +
        `<td class="dt-cell-nowrap"><b>${escapeHtml(v)}</b> ${pill}` +
        lotsIntoHtml(v) +
        ((m2CanWrite && !locked && ckqLots.has(v)) ? ` <button type="button" class="dt-btn" data-lo2dt="${escapeHtml(v)}" style="padding:1px 8px; font-size:11px; margin-left:8px;" title="Số lô này thực tế thuộc đặc tính khác — chuyển tay">⇄ Chuyển</button>` : '') +
        `</td>` +
        `<td class="dt-cell-wrap dt-cell-explain">${escapeHtml(getDienGiaiDacTinh(v))}</td></tr>`;
    });
    html += '</tbody></table>';
    dtLeftScroll.innerHTML = html;

    document.getElementById('dtSelectAll').addEventListener('change', (e)=>{
      selectableRows.forEach(v => e.target.checked ? dtCheckedSet.add(v) : dtCheckedSet.delete(v));
      renderDtLeft();
    });
    dtLeftScroll.querySelectorAll('.dt-row-check').forEach(cb=>{
      cb.addEventListener('change', ()=>{
        if(cb.disabled) return;
        const v = cb.dataset.value;
        if(cb.checked) dtCheckedSet.add(v); else dtCheckedSet.delete(v);
      });
    });
    dtLeftScroll.querySelectorAll('[data-untag]').forEach(btn=>{
      btn.addEventListener('click', async ()=>{
        if(m2DtDenied()) return;
        const ok = await showConfirmDialog({
          title: 'Xác nhận bỏ khỏi nhóm',
          message: `Bỏ "${btn.dataset.untag}" khỏi nhóm đặc tính đã gộp?`,
          confirmText: 'Bỏ khỏi nhóm',
          danger: true
        });
        if(!ok) return;
        delete dtGroupMap[btn.dataset.untag];
        saveDtGroupMap();
        refreshDacTinhTab();
        refreshBaoCaoTab();
      });
    });

    dtLeftScroll.querySelectorAll('[data-undolo]').forEach(btn=>{
      btn.addEventListener('click', async ()=>{
        if(m2DtDenied()) return;
        const ok = await showConfirmDialog({
          title: 'Hoàn tác chuyển số lô',
          message: `Trả số lô "${btn.dataset.undolo}" về lại dạng Số lô (không còn thuộc đặc tính này)?`,
          confirmText: 'Hoàn tác',
          danger: true
        });
        if(!ok) return;
        await undoLoMapping(btn.dataset.undolo);
      });
    });
    dtLeftScroll.querySelectorAll('[data-lo2dt]').forEach(btn=>{
      btn.addEventListener('click', async ()=>{
        if(m2DtDenied()) return;
        const lo = btn.dataset.lo2dt;
        const target = await showLoToDacTinhDialog(lo);
        if(!target) return;
        dtLoMap[loKey(lo)] = { lo, dacTinh: target };
        dtCheckedSet.delete(lo);
        await saveDtLoMap();
        refreshAfterLoMapChange();
      });
    });

    dtLeftScroll.scrollTop = scrollTop;
  }

  // Các Số lô đã được chuyển tay VỀ đặc tính v — hiện ngay dưới đặc tính để đối chiếu/kiểm tra.
  function lotsIntoHtml(v){
    const key = loKey(v);
    const lots = Object.values(dtLoMap).filter(m => loKey(m.dacTinh) === key)
      .sort((a,b)=> a.lo.localeCompare(b.lo, 'vi', {numeric:true}));
    if(!lots.length) return '';
    const stats = getLoStats();
    const pills = lots.map(m=>{
      const st = stats[loKey(m.lo)];
      const tip = st ? `${st.rows} dòng · ${formatWeight(st.kg)} KG` : 'không còn dòng dữ liệu';
      return `<span class="dt-pill" title="${escapeHtml(tip)}">${escapeHtml(m.lo)}<button data-undolo="${escapeHtml(m.lo)}" title="Hoàn tác: trả về Số lô">×</button></span>`;
    }).join('');
    return `<div style="margin-top:4px; font-size:11.5px; color:var(--text-low);">gồm ${lots.length} lô chuyển vào: ${pills}</div>`;
  }

  // Thống kê theo Số lô (chỉ các dòng CKQ): số dòng, tổng KG, các mã hàng — để kiểm tra khi chuyển.
  function getLoStats(){
    const out = {};
    (persistedRows || []).forEach(r=>{
      const seg = lastCommaSegment(r.tenHang);
      if(!seg || (matchManualDacTinhPrefix(seg) || seg) !== 'CKQ') return;
      const k = loKey(r.soLo);
      if(!k) return;
      const o = out[k] || (out[k] = { rows:0, kg:0, maHang:new Set() });
      o.rows++; o.kg += toNumber(r.trongLuong);
      if(r.maHang) o.maHang.add(String(r.maHang).trim());
    });
    return out;
  }

  async function undoLoMapping(lo){
    if(m2DtDenied()) return;
    delete dtLoMap[loKey(lo)];
    await saveDtLoMap();
    refreshAfterLoMapChange();
  }

  function refreshAfterLoMapChange(){
    updateDtLoMapBtn();
    refreshDuLieuTab();
    refreshDacTinhTab();
    refreshBaoCaoTab();
    refreshDoiSoatTab();
  }

  // Chuẩn hoá đặc tính người dùng gõ: khớp không phân biệt hoa/thường với đặc tính đã có
  // (VD gõ "23.mks" -> "23.MKS"); chưa có thì viết HOA cho đồng nhất với các đặc tính khác.
  function resolveTypedDacTinh(typed, existing){
    const t = String(typed ?? '').trim();
    if(!t) return '';
    const low = t.toLowerCase();
    for(const v of existing){ if(String(v).trim().toLowerCase() === low) return String(v).trim(); }
    const canon = canonDacTinhValue(t);
    if(canon !== t) return canon;
    return t.toUpperCase();
  }

  // Hộp nhập: chuyển 1 Số lô sang 1 đặc tính. Trả về đặc tính đã chuẩn hoá, hoặc null nếu huỷ.
  function showLoToDacTinhDialog(lo){
    return new Promise((resolve)=>{
      const ckq = getCkqLotSet();
      const existing = getUniqueDacTinhValues().filter(v => !ckq.has(v));
      const ov = document.createElement('div');
      ov.className = 'px-modal-overlay';
      ov.style.cssText = 'display:flex; z-index:85;';
      ov.innerHTML =
        '<div class="px-modal" style="width:min(480px, 100%);">' +
          '<div class="px-modal-head"><span>Chuyển Số lô → Đặc tính</span><button type="button" class="px-modal-close" data-x>✕</button></div>' +
          '<div class="px-modal-body">' +
            '<div style="font-size:13px; color:var(--text-mid);">Số lô: <b style="color:var(--text-hi);" data-lo></b></div>' +
            '<div class="px-modal-field"><label>Đặc tính</label><input type="text" autocomplete="off" style="flex:1;" placeholder="VD: 23.MKS — gõ để lọc danh sách" data-input></div>' +
            '<div data-list style="max-height:220px; overflow-y:auto; border:1px solid var(--line); border-radius:6px; background:var(--bg-void);"></div>' +
            '<div style="font-size:12px; color:var(--text-low);">Bấm chọn 1 đặc tính trong danh sách hoặc gõ tay. Muốn hoàn tác: nút "Lô đã chuyển" ở thanh công cụ.</div>' +
          '</div>' +
          '<div class="px-modal-footer"><button type="button" class="px-modal-btn" data-cancel>Hủy</button><button type="button" class="px-modal-btn primary" data-ok>Chuyển</button></div>' +
        '</div>';
      ov.querySelector('[data-lo]').textContent = lo;
      document.body.appendChild(ov);
      const input = ov.querySelector('[data-input]');
      const listEl = ov.querySelector('[data-list]');
      function renderList(){
        const q = input.value.trim().toLowerCase();
        const items = q ? existing.filter(v => String(v).toLowerCase().includes(q)) : existing;
        listEl.textContent = '';
        if(!items.length){
          const empty = document.createElement('div');
          empty.style.cssText = 'padding:8px 12px; font-size:12.5px; color:var(--text-low);';
          empty.textContent = 'Không có đặc tính khớp — sẽ dùng đúng chữ bạn gõ.';
          listEl.appendChild(empty);
          return;
        }
        items.forEach(v=>{
          const it = document.createElement('div');
          it.textContent = v;
          it.style.cssText = 'padding:6px 12px; font-size:12.5px; font-weight:600; color:var(--text-hi); cursor:pointer;';
          it.addEventListener('mouseenter', ()=>{ it.style.background = 'rgba(255,255,255,0.10)'; });
          it.addEventListener('mouseleave', ()=>{ it.style.background = ''; });
          it.addEventListener('click', ()=>{ input.value = v; renderList(); input.focus(); });
          listEl.appendChild(it);
        });
      }
      input.addEventListener('input', renderList);
      renderList();
      setTimeout(()=> input.focus(), 0);
      function finish(val){
        document.removeEventListener('keydown', onKey);
        ov.remove();
        resolve(val);
      }
      function submit(){
        const v = resolveTypedDacTinh(input.value, existing);
        if(!v){ input.focus(); return; }
        finish(v);
      }
      function onKey(e){
        if(e.key === 'Escape') finish(null);
        else if(e.key === 'Enter' && document.activeElement === input) submit();
      }
      document.addEventListener('keydown', onKey);
      ov.querySelector('[data-ok]').addEventListener('click', submit);
      ov.querySelector('[data-cancel]').addEventListener('click', ()=> finish(null));
      ov.querySelector('[data-x]').addEventListener('click', ()=> finish(null));
      ov.addEventListener('mousedown', (e)=>{ if(e.target === ov) finish(null); });
    });
  }

  // Hộp xem/hoàn tác các Số lô đã chuyển tay.
  function showLoMapListDialog(){
    const ov = document.createElement('div');
    ov.className = 'px-modal-overlay';
    ov.style.cssText = 'display:flex; z-index:85;';
    ov.innerHTML =
      '<div class="px-modal" style="width:min(860px, 100%);">' +
        '<div class="px-modal-head"><span>Số lô đã chuyển sang đặc tính</span><button type="button" class="px-modal-close" data-x>✕</button></div>' +
        '<div class="px-modal-body" data-body></div>' +
        '<div class="px-modal-footer"><button type="button" class="px-modal-btn" data-close>Đóng</button></div>' +
      '</div>';
    document.body.appendChild(ov);
    const body = ov.querySelector('[data-body]');
    function close(){ document.removeEventListener('keydown', onKey); ov.remove(); }
    function onKey(e){ if(e.key === 'Escape') close(); }
    document.addEventListener('keydown', onKey);
    ov.querySelector('[data-x]').addEventListener('click', close);
    ov.querySelector('[data-close]').addEventListener('click', close);
    ov.addEventListener('mousedown', (e)=>{ if(e.target === ov) close(); });

    function render(){
      const items = Object.keys(dtLoMap).map(k => dtLoMap[k])
        .sort((a,b)=> a.lo.localeCompare(b.lo, 'vi', {numeric:true}));
      if(!items.length){
        body.innerHTML = '<div class="empty-table">Chưa chuyển số lô nào.</div>';
        return;
      }
      const stats = getLoStats();
      let html = '<table data-kcf data-kcf-key="m2:lomap"><thead><tr><th>Số lô</th><th>→ Đặc tính</th><th>Số dòng</th><th>KG</th><th>Mã hàng</th><th style="width:36px;"></th></tr></thead><tbody>';
      items.forEach(r=>{
        const st = stats[loKey(r.lo)];
        const mh = st ? Array.from(st.maHang).join(', ') : '';
        html += `<tr><td>${escapeHtml(r.lo)}</td><td><b>${escapeHtml(r.dacTinh)}</b></td>` +
          `<td>${st ? formatNumber(st.rows) : '—'}</td><td>${st ? formatWeight(st.kg) : '—'}</td>` +
          `<td class="dt-cell-wrap">${escapeHtml(mh)}</td>` +
          `<td>${m2CanWrite ? `<button data-undo="${escapeHtml(r.lo)}" style="border:none; background:none; color:var(--danger); cursor:pointer; font-size:13px;" title="Hoàn tác: trả về Số lô">✕</button>` : ''}</td></tr>`;
      });
      html += '</tbody></table>';
      body.innerHTML = html;
      body.querySelectorAll('[data-undo]').forEach(btn=>{
        btn.addEventListener('click', async ()=>{
          if(m2DtDenied()) return;
          delete dtLoMap[loKey(btn.dataset.undo)];
          await saveDtLoMap();
          refreshAfterLoMapChange();
          render();
        });
      });
    }
    render();
  }

  function updateDtLoMapBtn(){
    const b = document.getElementById('dtLoMapBtn');
    if(b) b.textContent = 'Lô đã chuyển (' + formatNumber(Object.keys(dtLoMap).length) + ')';
  }
  (function initDtLoMapBtn(){
    const b = document.getElementById('dtLoMapBtn');
    if(!b) return;
    b.addEventListener('click', showLoMapListDialog);
    updateDtLoMapBtn();
  })();

  function renderDtRight(){
    // Giữ nguyên vị trí cuộn của bảng "Gộp Đặc Tính" khi render lại, tránh bị giật lên đầu.
    const scrollTop = dtRightScroll.scrollTop;

    let html = '<table class="dt-group-table"><thead><tr>' +
      '<th>NHÓM</th><th>ĐẶC TÍNH ĐÃ GỘP</th></tr></thead><tbody>';
    DT_GROUPS.forEach(g=>{
      const items = Object.keys(dtGroupMap)
        .filter(v => dtGroupMap[v] === g)
        .sort((a,b)=> a.localeCompare(b, 'vi', {numeric:true}));
      const cell = items.length
        ? items.map(v => `<span class="dt-pill">${escapeHtml(v)}${m2CanWrite ? `<button data-untag2="${escapeHtml(v)}" title="Bỏ khỏi nhóm">×</button>` : ''}</span>`).join('')
        : '<span style="color:var(--text-low)">—</span>';
      html += `<tr><td><b>${escapeHtml(g)}</b></td><td class="dt-cell-wrap">${cell}</td></tr>`;
    });
    html += '</tbody></table>';
    dtRightScroll.innerHTML = html;

    dtRightScroll.querySelectorAll('[data-untag2]').forEach(btn=>{
      btn.addEventListener('click', async ()=>{
        if(m2DtDenied()) return;
        const ok = await showConfirmDialog({
          title: 'Xác nhận bỏ khỏi nhóm',
          message: `Bỏ "${btn.dataset.untag2}" khỏi nhóm đặc tính đã gộp?`,
          confirmText: 'Bỏ khỏi nhóm',
          danger: true
        });
        if(!ok) return;
        delete dtGroupMap[btn.dataset.untag2];
        saveDtGroupMap();
        refreshDacTinhTab();
        refreshBaoCaoTab();
      });
    });

    dtRightScroll.scrollTop = scrollTop;
  }

  dtMergeBtn.addEventListener('click', async ()=>{
    if(m2DtDenied()) return;
    const g = dtGroupSelect.value;
    if(!g) return;
    // Loại bỏ phòng hờ các giá trị đã có nhóm (đã bị khóa) lỡ còn sót trong dtCheckedSet
    Array.from(dtCheckedSet).forEach(v => { if(dtGroupMap[v]) dtCheckedSet.delete(v); });
    if(!dtCheckedSet.size){ alert('Vui lòng tick chọn ít nhất 1 đặc tính chưa được gộp để gộp.'); return; }
    // Gộp ngay, không hỏi xác nhận (đã bỏ popup theo yêu cầu). Muốn hoàn tác: bấm × trên nhãn nhóm.
    dtCheckedSet.forEach(v => { dtGroupMap[v] = g; });
    saveDtGroupMap();
    dtCheckedSet.clear();
    refreshDacTinhTab();
    refreshBaoCaoTab();
  });

  dtClearSelBtn.addEventListener('click', ()=>{
    dtCheckedSet.clear();
    renderDtLeft();
  });

  dtClearAllBtn.addEventListener('click', async ()=>{
    if(m2DtDenied()) return;
    const ok = await showConfirmDialog({
      title: 'Xác nhận xóa dữ liệu',
      message: 'Xoá toàn bộ dữ liệu gộp đặc tính đã lưu? Hành động này không thể hoàn tác.',
      confirmText: 'Xóa toàn bộ',
      danger: true
    });
    if(!ok) return;
    dtGroupMap = {};
    saveDtGroupMap();
    refreshDacTinhTab();
    refreshBaoCaoTab();
  });

  const renderDtLeftTyping = KLAType.debounce(()=>KLAType.keepFocus(renderDtLeft), 140);
  dtSearch.addEventListener('input', ()=>{
    dtSearchTerm = dtSearch.value;
    renderDtLeftTyping();
  });

  dtFilterSelect.addEventListener('change', ()=>{
    dtFilterStatus = dtFilterSelect.value;
    renderDtLeft();
  });

  dtExportLeft.addEventListener('click', ()=>{
    const all = getUniqueDacTinhValues();
    if(!all.length) return;
    const data = all.map(v => ({ 'Đặc tính': v, 'Diễn giải đặc tính': getDienGiaiDacTinh(v), 'Nhóm': dtGroupMap[v] || '' }));
    const ws = XLSX.utils.json_to_sheet(data);
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, 'Dac tinh');
    XLSX.writeFile(wb, 'dac-tinh.xlsx');
  });

  dtExportRight.addEventListener('click', ()=>{
    const data = DT_GROUPS.map(g => {
      const items = Object.keys(dtGroupMap).filter(v => dtGroupMap[v] === g)
        .sort((a,b)=> a.localeCompare(b, 'vi', {numeric:true}));
      return { 'Nhóm': g, 'Đặc tính đã gộp': items.join(', ') };
    });
    const ws = XLSX.utils.json_to_sheet(data);
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, 'Gop dac tinh');
    XLSX.writeFile(wb, 'gop-dac-tinh.xlsx');
  });

  // ---- TAB 7: BÁO CÁO — "Bảng Tổng Hợp" (pivot Size × Nhóm đặc tính, tính trên Trọng lượng KG) ----
  const toolbarLabel7 = document.getElementById('toolbarLabel7');
  const tableScroll7  = document.getElementById('tableScroll7');
  const rowCount7     = document.getElementById('rowCount7');
  const summaryStats7 = document.getElementById('summaryStats7');
  const exportBtn7    = document.getElementById('exportBtn7');
  const gotoDacTinhBtn7 = document.getElementById('gotoDacTinhBtn7');

  // 6 nhóm hàng, theo đúng tên và thứ tự các menu thực tế trong ứng dụng (khớp với nav.menu)
  const BAOCAO_CATEGORIES = [
    { label:'TÔM NGUYÊN LIỆU', getRows:()=> nlRows },
    { label:'TÔM HLSO',        getRows:()=> hlsoRows },
    { label:'TÔM GIÁM SÁT',    getRows:()=> tgsRows },
    { label:'TÔM GIA CÔNG',    getRows:()=> tgcRows },
    { label:'TÔM ẤN ĐỘ',       getRows:()=> anDoRows },
    { label:'TÔM ECUADOR',     getRows:()=> ecuadorRows },
  ];

  let baoCaoPivot = null; // { grand:{byGroup,tong}, categories:[{label,byGroup,tong,sizes:[{size,byGroup,tong}]}] }

  // Tiền tố hiển thị của size theo từng phân loại — dùng để chuẩn hoá nhãn size
  // (VD: "TGS BTP45" / "BTP45" -> đều hiển thị thống nhất là "TGS 45").
  const BAOCAO_SIZE_PREFIX = {
    'TÔM NGUYÊN LIỆU': 'NL',
    'TÔM HLSO':        'BTP',
    'TÔM GIÁM SÁT':    'TGS',
    'TÔM GIA CÔNG':    'TGC',
    'TÔM ẤN ĐỘ':       'AĐ',
    'TÔM ECUADOR':     'ECU',
  };
  // Mọi tiền tố có thể xuất hiện (lồng nhau) trong dữ liệu gốc của cột Size, cần bóc hết
  // trước khi gắn lại đúng 1 tiền tố chuẩn theo phân loại hiện tại.
  const BAOCAO_SIZE_PREFIX_RE = /^(TGS|TGC|BTP|ECU|NL|AĐ|AD)\s*/i;

  function stripKnownSizePrefixes(raw){
    let s = String(raw ?? '').trim();
    let m;
    while((m = s.match(BAOCAO_SIZE_PREFIX_RE))){
      s = s.slice(m[0].length).trim();
    }
    return s;
  }

  // Khoá sắp xếp size — chia thành 3 nhóm riêng biệt, mỗi nhóm sắp xếp tăng dần trong nội bộ:
  //   nhóm 0: size số thuần (VD 45, 50)
  //   nhóm 1: size dạng khoảng "xx/yy" (VD 16/20, 41/50, 51/60) — xếp theo số đầu
  //   nhóm 2: size dạng chữ+XX, có thể có số đứng trước (VD MXX, 1LXX, 2LXX, 6LXX)
  //   nhóm 3: còn lại (VD "(Trống)") — xếp cuối cùng
  function baoCaoSizeSortKey(core){
    const s = String(core ?? '').trim();
    let m = s.match(/^(\d+(?:\.\d+)?)$/);
    if(m) return [0, parseFloat(m[1])];
    m = s.match(/^(\d+(?:\.\d+)?)\s*\/\s*\d+(?:\.\d+)?$/);
    if(m) return [1, parseFloat(m[1])];
    m = s.match(/^(\d*)([A-ZÀ-Ỹ]+)XX$/i);
    if(m) return [2, m[1] ? parseFloat(m[1]) : 0, m[2].toUpperCase()];
    return [3, Number.MAX_SAFE_INTEGER];
  }

  function emptyBucket(){ return { byGroup:{}, tong:0 }; }

  function addToBucket(bucket, group, w){
    // Chỉ cộng vào "Tổng" khi dòng này thực sự có số liệu hiện trên bảng (tức đã gộp vào
    // 1 trong các cột nhóm đặc tính). Dòng có đặc tính nhưng CHƯA gộp nhóm nào thì bỏ qua
    // hoàn toàn, không cộng vào cột nhóm nào và cũng không cộng vào "Tổng".
    if(!group) return;
    bucket.tong += w;
    bucket.byGroup[group] = (bucket.byGroup[group] || 0) + w;
  }

  // Tổng hợp Trọng lượng (KG) theo Size, trong từng nhóm hàng (6 module), và theo từng
  // nhóm đặc tính đã gộp (DT_GROUPS). CHỈ tính những dòng suy ra được đặc tính (từ Tên hàng)
  // — dòng nào KHÔNG có đặc tính thì bỏ qua hoàn toàn, không cộng vào bất kỳ cột nào kể cả "Tổng".
  // Trong các dòng có đặc tính, cột "Tổng" chỉ cộng phần đã gộp được vào 1 nhóm đặc tính
  // (đúng bằng tổng các cột nhóm hiển thị trên bảng); đặc tính chưa gộp nhóm không cộng vào đâu cả.
  function computeBaoCaoPivot(){
    const grand = emptyBucket();
    const categories = BAOCAO_CATEGORIES.map(cat=>{
      const sizeOrder = [];
      const sizeMap = new Map();
      const catBucket = emptyBucket();

      cat.getRows().forEach(r=>{
        const dacTinh = deriveDacTinhFromTenHang(r.tenHang, r.soLo);
        if(!dacTinh) return; // không có số liệu đặc tính -> không tính vào Bảng Tổng Hợp

        const rawSize = String(r.size ?? '').trim();
        // Bóc hết tiền tố (NL/BTP/TGS/TGC/AĐ/ECU) khỏi size gốc để gộp các biến thể
        // (VD "TGS BTP45" và "BTP45") về cùng 1 dòng theo core số/khoảng size.
        const core = rawSize ? (stripKnownSizePrefixes(rawSize) || rawSize) : '(Trống)';
        if(!sizeMap.has(core)){ sizeMap.set(core, emptyBucket()); sizeOrder.push(core); }
        const w = toNumber(r.trongLuong);
        const group = dtGroupMap[dacTinh] || null;

        addToBucket(sizeMap.get(core), group, w);
        addToBucket(catBucket, group, w);
        addToBucket(grand, group, w);
      });

      // Sắp xếp size theo thứ tự tăng dần (không nhảy lộn xộn theo thứ tự nạp dữ liệu).
      sizeOrder.sort((a,b)=>{
        const ka = baoCaoSizeSortKey(a), kb = baoCaoSizeSortKey(b);
        if(ka[0] !== kb[0]) return ka[0] - kb[0];
        if(ka[1] !== kb[1]) return ka[1] - kb[1];
        return a.localeCompare(b);
      });

      const prefix = BAOCAO_SIZE_PREFIX[cat.label] || '';
      return {
        label: cat.label,
        byGroup: catBucket.byGroup,
        tong: catBucket.tong,
        sizes: sizeOrder.map(core => ({
          // Gắn lại đúng 1 tiền tố chuẩn theo phân loại (VD "TGS 45", "NL 105"); dòng
          // trống giữ nguyên nhãn "(Trống)".
          size: (core === '(Trống)' || !prefix) ? core : `${prefix} ${core}`,
          ...sizeMap.get(core),
        })),
      };
    });

    // PC 2.11: phân loại / size KHÔNG CÒN TỒN (Tổng và mọi cột nhóm đều 0) thì ẨN — vì vẽ bảng, xuất Excel và file báo cáo gửi
    // điện thoại đều dùng computeBaoCaoPivot() nên 3 nơi luôn khớp. Tổng TỒN không đổi (hàng bị bỏ đều bằng 0).
    const bucketInStock = (b) => Math.abs(b.tong || 0) >= 0.005 || Object.keys(b.byGroup || {}).some(g => Math.abs(b.byGroup[g] || 0) >= 0.005);
    const liveCategories = categories
      .map(cat => Object.assign({}, cat, { sizes: cat.sizes.filter(bucketInStock) }))
      .filter(cat => bucketInStock(cat) || cat.sizes.length);
    return { grand, categories: liveCategories };
  }

  function fmtCell(v){
    if(!v) return '<span class="dash">-</span>';
    return formatWeight(v);
  }

  function baoCaoRowCells(bucket, visibleGroups){
    let html = '';
    visibleGroups.forEach(g=> html += `<td>${fmtCell(bucket.byGroup[g])}</td>`);
    html += `<td>${fmtCell(bucket.tong)}</td>`;
    return html;
  }

  // Mặc định ẨN các cột nhóm đặc tính hoàn toàn không có số liệu ở bất kỳ dòng nào (giống hệt
  // cách làm ở module "Tồn Kho An An" — "Bảng Tổng Hợp HLSO"), có nút bật lại khi cần xem đủ.
  let baoCaoHideEmptyCols = true;
  const toggleEmptyColsBtn7 = document.getElementById('toggleEmptyColsBtn7');
  if(toggleEmptyColsBtn7) toggleEmptyColsBtn7.addEventListener('click', ()=>{
    baoCaoHideEmptyCols = !baoCaoHideEmptyCols;
    refreshBaoCaoTab();
  });

  // PC 5.33 — "Bảng Tổng Hợp Tùy Chọn": bảng RIÊNG lọc Size + Đặc Tính đã gộp, chỉ ĐỌC lại baoCaoPivot đang hiển thị
  // (không tính lại từ tồn kho, không đụng bảng gốc). Khung đặt ngay dưới dòng chứa "Bảng Tổng Hợp".
  function mountBaoCaoCustom(){
    const wrap = document.getElementById('bcTableWrap7');
    if(!wrap || !wrap.parentNode) return;
    let el = document.getElementById('baoCaoCustomSummary');
    if(!el){
      el = document.createElement('div'); el.id = 'baoCaoCustomSummary';
      wrap.parentNode.insertAdjacentElement('afterend', el);
    }
    if(!baoCaoPivot || !window.KLCustomSummary){ el.innerHTML = ''; return; }
    window.KLCustomSummary.mount(el, { key:'m2-baocao', title:'Bảng Tổng Hợp Tùy Chọn', getData: ()=>{
      const rows = [];
      const cellsOf = b => DT_GROUPS.map(g => { const v = b.byGroup[g]; return (typeof v === 'number' && isFinite(v)) ? v : null; });
      baoCaoPivot.categories.forEach(cat=>{
        rows.push({ key:'T|'+cat.label, label:cat.label, kind:'title', cells:cellsOf(cat), total:cat.tong });
        cat.sizes.forEach(sz=> rows.push({ key:'S|'+cat.label+'|'+sz.size, label:sz.size, kind:'size', cells:cellsOf(sz), total:sz.tong }));
      });
      return { cols: DT_GROUPS.slice(), rows };
    }});
  }

  function refreshBaoCaoTab(){
    const hasAnyData = [hlsoRows, tgcRows, ecuadorRows, anDoRows, nlRows, tgsRows].some(a => a && a.length);

    if(!hasAnyData){
      baoCaoPivot = null;
      tableScroll7.innerHTML = '<div class="empty-table">Chưa có dữ liệu — hãy nạp file ở menu "Load data" trước.</div>';
      rowCount7.textContent = '';
      summaryStats7.innerHTML = '';
      exportBtn7.style.display = 'none';
      if(toggleEmptyColsBtn7) toggleEmptyColsBtn7.style.display = 'none';
      mountBaoCaoCustom();
      refreshBaoCaoPctTables();
      return;
    }

    baoCaoPivot = computeBaoCaoPivot();
    exportBtn7.style.display = 'inline-block';

    const totalSizeRows = baoCaoPivot.categories.reduce((s,c)=> s + c.sizes.length, 0);
    rowCount7.textContent = formatNumber(totalSizeRows) + ' size · ' + formatNumber(baoCaoPivot.categories.length) + ' nhóm';

    // Cột nhóm đặc tính nào có ít nhất 1 giá trị khác 0 ở dòng TỔNG TỒN hoặc bất kỳ dòng nào
    // bên dưới — quét TỔNG TỒN trước cho nhanh (đã cộng dồn tất cả), chỉ cần quét thêm dòng
    // con nếu TỔNG TỒN chưa thấy (trường hợp cộng dồn ra đúng 0 dù có dữ liệu âm/dương triệt
    // tiêu nhau — hiếm nhưng vẫn quét đủ cho chắc).
    const groupHasData = {};
    DT_GROUPS.forEach(g=>{ groupHasData[g] = !!baoCaoPivot.grand.byGroup[g]; });
    baoCaoPivot.categories.forEach(cat=>{
      DT_GROUPS.forEach(g=>{ if(cat.byGroup[g]) groupHasData[g] = true; });
      cat.sizes.forEach(sz=> DT_GROUPS.forEach(g=>{ if(sz.byGroup[g]) groupHasData[g] = true; }));
    });
    const emptyColCount = DT_GROUPS.filter(g=>!groupHasData[g]).length;
    const visibleGroups = baoCaoHideEmptyCols ? DT_GROUPS.filter(g=>groupHasData[g]) : DT_GROUPS;

    if(toggleEmptyColsBtn7){
      if(emptyColCount > 0){
        toggleEmptyColsBtn7.style.display = 'inline-block';
        toggleEmptyColsBtn7.textContent = baoCaoHideEmptyCols ? `Hiện cột trống (${emptyColCount})` : 'Ẩn cột trống';
      } else {
        toggleEmptyColsBtn7.style.display = 'none';
      }
    }

    let html = '<table class="baocao-table"><thead><tr><th>SIZE</th>';
    visibleGroups.forEach(g => html += `<th>${escapeHtml(g)}</th>`);
    html += '<th>TỔNG</th></tr></thead><tbody>';

    html += `<tr class="baocao-total-row"><td>TỔNG TỒN</td>${baoCaoRowCells(baoCaoPivot.grand, visibleGroups)}</tr>`;

    baoCaoPivot.categories.forEach(cat=>{
      html += `<tr class="baocao-group-row"><td>${escapeHtml(cat.label)}</td>${baoCaoRowCells(cat, visibleGroups)}</tr>`;
      cat.sizes.forEach(sz=>{
        html += `<tr class="baocao-size-row"><td>${escapeHtml(sz.size)}</td>${baoCaoRowCells(sz, visibleGroups)}</tr>`;
      });
    });

    html += `<tr class="baocao-total-row baocao-total-row-bottom"><td>TỔNG TỒN</td>${baoCaoRowCells(baoCaoPivot.grand, visibleGroups)}</tr>`;
    html += '</tbody></table>';
    // Giữ nguyên vị trí cuộn ngang/dọc đang xem trước khi vẽ lại — tránh giật/nhảy về góc trên-
    // trái mỗi khi bấm nút "Ẩn/Hiện cột trống" (đổi số cột không nên làm mất chỗ đang xem).
    const prevScrollLeft = tableScroll7.scrollLeft, prevScrollTop = tableScroll7.scrollTop;
    tableScroll7.innerHTML = html;
    tableScroll7.scrollLeft = prevScrollLeft;
    tableScroll7.scrollTop = prevScrollTop;

    summaryStats7.innerHTML =
      `<span class="stat">Số nhóm<b>${formatNumber(baoCaoPivot.categories.length)}</b></span>` +
      `<span class="stat">Số dòng size<b>${formatNumber(totalSizeRows)}</b></span>` +
      `<span class="stat">Tổng KG<b>${formatWeight(baoCaoPivot.grand.tong)}</b></span>`;

    mountBaoCaoCustom();
    refreshBaoCaoPctTables();
  }

  // ---- 2 bảng "Tỉ Lệ % Tồn Kho" (theo 6 nhóm hàng) và "Tỉ Lệ % Theo Size" (từng dòng size,
  // gộp cả 6 nhóm) — dùng lại ĐÚNG baoCaoPivot đã tính ở computeBaoCaoPivot() nên số liệu luôn
  // khớp với "Bảng Tổng Hợp" ở trên. Tương tự cách làm ở module "Tồn Kho An An". ----------
  const tableScrollPctNhom = document.getElementById('tableScrollPctNhom');
  const rowCountPctNhom    = document.getElementById('rowCountPctNhom');
  const tableScrollPctSize = document.getElementById('tableScrollPctSize');
  const rowCountPctSize    = document.getElementById('rowCountPctSize');

  function pctBarHtml(pct){
    if(pct===null || pct===undefined || isNaN(pct)) return '<span class="dash">-</span>';
    return `<div style="display:flex;align-items:center;gap:8px;justify-content:flex-end;">
        <div style="flex:1;max-width:56px;height:6px;border-radius:3px;background:var(--line);overflow:hidden;">
          <div style="width:${Math.min(100,pct).toFixed(1)}%;height:100%;background:var(--blue-glow);"></div>
        </div>
        <span style="min-width:40px;text-align:right;">${pct.toFixed(1)}%</span>
      </div>`;
  }

  function refreshBaoCaoPctTables(){
    if(!tableScrollPctNhom || !tableScrollPctSize) return;
    if(!baoCaoPivot || !baoCaoPivot.grand.tong){
      tableScrollPctNhom.innerHTML = '<div class="empty-table">Chưa có dữ liệu.</div>';
      tableScrollPctSize.innerHTML = '<div class="empty-table">Chưa có dữ liệu.</div>';
      rowCountPctNhom.textContent = '';
      rowCountPctSize.textContent = '';
      sizePctAllRows = [];
      closeSizePctPopover();
      return;
    }
    const grandTotal = baoCaoPivot.grand.tong;
    sizePctGrandTotal = grandTotal;

    // ---- Tỉ Lệ % Tồn Kho theo 6 nhóm hàng — sắp xếp giảm dần theo trọng lượng ----
    const nhomRows = baoCaoPivot.categories
      .map(cat => ({ label: cat.label, weight: cat.tong }))
      .filter(r => r.weight)
      .sort((a,b)=> b.weight - a.weight);
    rowCountPctNhom.textContent = formatNumber(nhomRows.length) + ' nhóm';
    let htmlNhom = '<table class="baocao-table"><thead><tr><th>NHÓM</th><th>TRỌNG LƯỢNG (KG)</th><th>% CHIẾM DỤNG</th></tr></thead><tbody>';
    nhomRows.forEach(r=>{
      htmlNhom += `<tr><td>${escapeHtml(r.label)}</td><td>${formatWeight(r.weight)}</td><td>${pctBarHtml(r.weight/grandTotal*100)}</td></tr>`;
    });
    htmlNhom += `<tr class="baocao-total-row"><td>TỔNG</td><td>${formatWeight(grandTotal)}</td><td style="text-align:right;">100.0%</td></tr>`;
    htmlNhom += '</tbody></table>';
    tableScrollPctNhom.innerHTML = htmlNhom;

    // ---- Tỉ Lệ % Theo Size — gộp TẤT CẢ dòng size của 6 nhóm vào 1 danh sách, sắp xếp giảm
    // dần theo trọng lượng (nhãn size đã gắn tiền tố theo nhóm ở computeBaoCaoPivot() nên
    // không bị lẫn khi 2 nhóm khác nhau cùng có 1 giá trị size gốc giống nhau). Có thêm lọc
    // kiểu Excel (chọn nhiều Size cùng lúc) — chọn xong sẽ cộng dồn TỔNG % của các Size đã chọn
    // ở dòng "TỔNG ĐÃ CHỌN" riêng, xem renderSizePctTableBody()/toggleSizePctPopover(). ----
    sizePctAllRows.length = 0;
    baoCaoPivot.categories.forEach(cat=>{
      cat.sizes.forEach(sz=>{
        if(sz.tong) sizePctAllRows.push({ label: sz.size, weight: sz.tong });
      });
    });
    sizePctAllRows.sort((a,b)=> b.weight - a.weight);
    // Bỏ khỏi tập đã chọn những Size không còn tồn tại trong dữ liệu mới (VD sau khi nạp lại
    // file khác) — tránh giữ lại lựa chọn "ma" không còn ứng với dòng nào.
    if(sizePctSelectedSet){
      const validLabels = new Set(sizePctAllRows.map(r=>r.label));
      Array.from(sizePctSelectedSet).forEach(l => { if(!validLabels.has(l)) sizePctSelectedSet.delete(l); });
      if(sizePctSelectedSet.size === 0) sizePctSelectedSet = null;
    }

    let htmlSize = `<table class="baocao-table"><thead><tr>
        <th>
          <span>SIZE</span>
          <button type="button" id="btnSizePctFilter" title="Lọc theo Size" style="margin-left:6px;cursor:pointer;background:none;border:none;color:inherit;font-size:12px;vertical-align:middle;">⏷</button>
        </th>
        <th>TRỌNG LƯỢNG (KG)</th><th>% CHIẾM DỤNG</th>
      </tr></thead><tbody id="sizePctTbody"></tbody></table>`;
    tableScrollPctSize.innerHTML = htmlSize;

    renderSizePctTableBody();

    const filterBtn = document.getElementById('btnSizePctFilter');
    if(filterBtn) filterBtn.addEventListener('click', (e)=>{ e.stopPropagation(); toggleSizePctPopover(filterBtn); });
  }

  // Danh sách ĐẦY ĐỦ (chưa lọc) các dòng Size — dùng chung cho cả vẽ bảng lẫn vẽ popup lọc.
  let sizePctAllRows = [];
  let sizePctGrandTotal = 0;
  // null = không lọc (hiện tất cả); Set các label = chỉ hiện đúng những Size có trong Set.
  let sizePctSelectedSet = null;

  function renderSizePctTableBody(){
    const tbody = document.getElementById('sizePctTbody');
    if(!tbody) return;
    const isFiltered = sizePctSelectedSet !== null;
    const visibleRows = isFiltered ? sizePctAllRows.filter(r => sizePctSelectedSet.has(r.label)) : sizePctAllRows;
    rowCountPctSize.textContent = isFiltered
      ? `${formatNumber(visibleRows.length)}/${formatNumber(sizePctAllRows.length)} size (đã lọc)`
      : formatNumber(sizePctAllRows.length) + ' size';

    let html = '';
    visibleRows.forEach(r=>{
      html += `<tr><td>${escapeHtml(r.label)}</td><td>${formatWeight(r.weight)}</td><td>${pctBarHtml(r.weight/sizePctGrandTotal*100)}</td></tr>`;
    });
    // Chỉ hiện dòng "TỔNG ĐÃ CHỌN" khi đang lọc (chọn ít hơn toàn bộ) — cộng dồn đúng % của các
    // Size đang được chọn/hiển thị, để biết nhanh nhóm size đã chọn chiếm bao nhiêu % tổng kho.
    if(isFiltered){
      const selectedWeight = visibleRows.reduce((s,r)=> s + r.weight, 0);
      html += `<tr class="baocao-group-row"><td>TỔNG ĐÃ CHỌN (${formatNumber(visibleRows.length)} size)</td><td>${formatWeight(selectedWeight)}</td><td style="text-align:right;">${(selectedWeight/sizePctGrandTotal*100).toFixed(1)}%</td></tr>`;
    }
    html += `<tr class="baocao-total-row"><td>TỔNG</td><td>${formatWeight(sizePctGrandTotal)}</td><td style="text-align:right;">100.0%</td></tr>`;
    tbody.innerHTML = html;
  }

  function closeSizePctPopover(){
    const el = document.getElementById('sizePctPopover');
    if(el) el.remove();
    document.removeEventListener('click', sizePctPopoverOutsideClick, true);
  }
  function sizePctPopoverOutsideClick(e){
    const el = document.getElementById('sizePctPopover');
    if(el && !el.contains(e.target) && e.target.id !== 'btnSizePctFilter') closeSizePctPopover();
  }

  function toggleSizePctPopover(anchorBtn){
    const existing = document.getElementById('sizePctPopover');
    if(existing){ closeSizePctPopover(); return; }

    const rect = anchorBtn.getBoundingClientRect();
    const pop = document.createElement('div');
    pop.id = 'sizePctPopover';
    pop.style.cssText = `position:fixed; top:${rect.bottom+4}px; left:${rect.left}px; z-index:500; width:220px;
      background:var(--bg-panel); border:1px solid var(--line); border-radius:8px; box-shadow:0 10px 30px rgba(0,0,0,0.5);
      padding:8px; font-size:12.5px; color:var(--text-hi); max-height:320px; display:flex; flex-direction:column;`;

    const searchInput = document.createElement('input');
    searchInput.type = 'text';
    searchInput.placeholder = 'Gõ để tìm / chọn nhanh Size…';
    searchInput.style.cssText = 'width:100%; box-sizing:border-box; margin-bottom:6px; padding:6px 8px; font-size:12px; border-radius:6px; border:1px solid var(--line); background:var(--bg-panel-2); color:var(--text-hi); outline:none;';
    pop.appendChild(searchInput);

    const actionsRow = document.createElement('div');
    actionsRow.style.cssText = 'display:flex; gap:8px; margin-bottom:6px; padding-bottom:6px; border-bottom:1px solid var(--line);';
    const btnAll = document.createElement('button');
    btnAll.textContent = 'Chọn tất cả';
    btnAll.type = 'button';
    btnAll.style.cssText = 'flex:1; font-size:11.5px; padding:4px 6px; cursor:pointer; background:var(--bg-panel-2); border:1px solid var(--line); border-radius:5px; color:inherit;';
    const btnNone = document.createElement('button');
    btnNone.textContent = 'Bỏ chọn hết';
    btnNone.type = 'button';
    btnNone.style.cssText = btnAll.style.cssText;
    actionsRow.appendChild(btnAll);
    actionsRow.appendChild(btnNone);
    pop.appendChild(actionsRow);

    const listWrap = document.createElement('div');
    listWrap.style.cssText = 'overflow-y:auto; overflow-x:hidden; flex:1; display:flex; flex-direction:column;';
    sizePctAllRows.forEach(r=>{
      const isChecked = sizePctSelectedSet === null || sizePctSelectedSet.has(r.label);
      const row = document.createElement('label');
      row.style.cssText = 'display:flex; align-items:center; gap:6px; padding:3px 2px; cursor:pointer; width:100%; box-sizing:border-box; flex-shrink:0;';
      row.dataset.searchText = r.label.toLowerCase();
      row.innerHTML = `<input type="checkbox" ${isChecked?'checked':''} data-size-label="${escapeHtml(r.label)}" style="cursor:pointer;flex-shrink:0;"><span style="flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">${escapeHtml(r.label)}</span>`;
      listWrap.appendChild(row);
    });
    pop.appendChild(listWrap);

    document.body.appendChild(pop);
    searchInput.focus();

    const applyFromCheckboxes = ()=>{
      const checked = Array.from(listWrap.querySelectorAll('input[type=checkbox]:checked')).map(cb=>cb.dataset.sizeLabel);
      sizePctSelectedSet = (checked.length === sizePctAllRows.length) ? null : new Set(checked);
      renderSizePctTableBody();
    };
    // Gõ chữ -> lọc nhanh danh sách bên dưới (ẩn dòng không khớp), không đổi lựa chọn đang tick.
    // Bấm Enter -> CHỈ chọn đúng các dòng đang hiện ra sau khi lọc (bỏ chọn dòng khác) (đánh tay chọn nhanh
    // 1 hoặc nhiều size cùng lúc mà không cần cuộn/tìm bằng mắt).
    searchInput.addEventListener('input', ()=>{
      const kw = searchInput.value.trim().toLowerCase();
      Array.from(listWrap.children).forEach(row=>{
        row.style.display = (!kw || row.dataset.searchText.includes(kw)) ? '' : 'none';
      });
    });
    searchInput.addEventListener('keydown', (e)=>{
      if(e.key !== 'Enter') return;
      e.preventDefault();
      const visibleRows = Array.from(listWrap.children).filter(row => row.style.display !== 'none');
      if(visibleRows.length === 0) return;
      // Enter = lấy ĐÚNG các dòng đang hiện (kiểu Excel): tick dòng khớp từ khoá, BỎ tick mọi dòng không khớp — trước
    // đây giữ nguyên các size đã tick từ trước (không khớp từ khoá) nên phải bấm "Bỏ chọn hết" rồi chọn lại (PC 2.4).
    Array.from(listWrap.children).forEach(row => { row.querySelector('input[type=checkbox]').checked = row.style.display !== 'none'; });
      applyFromCheckboxes();
    });
    listWrap.addEventListener('change', applyFromCheckboxes);
    btnAll.addEventListener('click', ()=>{
      listWrap.querySelectorAll('input[type=checkbox]').forEach(cb=>cb.checked=true);
      applyFromCheckboxes();
    });
    btnNone.addEventListener('click', ()=>{
      listWrap.querySelectorAll('input[type=checkbox]').forEach(cb=>cb.checked=false);
      applyFromCheckboxes();
    });

    // Chờ 1 tick trước khi lắng nghe click-ra-ngoài, tránh bắt luôn chính cú click vừa mở popup.
    setTimeout(()=> document.addEventListener('click', sizePctPopoverOutsideClick, true), 0);
  }

  gotoDacTinhBtn7.addEventListener('click', ()=>{
    document.querySelector('nav.menu button[data-tab="6"]').click();
  });

  // ---- Style dùng chung khi xuất "Bảng Tổng Hợp" — sao chép 100% theo file mẫu BangTongHopHLSO.xlsx ----
  const BC_FONT = { name:'Times New Roman', size:14 };
  const BC_THIN = { style:'thin', color:{ argb:'FF000000' } };
  const BC_MEDIUM = { style:'medium', color:{ argb:'FF000000' } };
  const BC_BORDER_ALL_THIN = { top:BC_THIN, bottom:BC_THIN, left:BC_THIN, right:BC_THIN };
  const BC_BORDER_TOTAL = { top:BC_MEDIUM, bottom:BC_THIN, left:BC_THIN, right:BC_THIN };
  const BC_FILL_HEADER = { type:'pattern', pattern:'solid', fgColor:{ argb:'FF1F4E78' } };
  const BC_FILL_TOTAL  = { type:'pattern', pattern:'solid', fgColor:{ argb:'FFDDEBF7' } };
  const BC_FILL_GROUP  = { type:'pattern', pattern:'solid', fgColor:{ argb:'FFD6EAD3' } };
  const BC_NUMFMT = '#,##0.00';

  // ---- Đo độ rộng chữ thật để tự căn cột/dòng theo đúng font+cỡ chữ đang dùng (Times New Roman 14),
  // thay vì đoán theo số ký tự — canvas đo đúng bằng font hiển thị thực tế của Electron/Chromium.
  const BC_MEASURE_CTX = document.createElement('canvas').getContext('2d');
  const BC_MDW_PX = 7; // độ rộng 1 đơn vị "độ rộng cột" Excel (≈ bề ngang ký tự '0' của font mặc định, 96dpi)

  function bcCellDisplayText(v, isNumber){
    if(v === '' || v === null || v === undefined) return '';
    if(isNumber) return Number(v).toLocaleString('en-US', { minimumFractionDigits:2, maximumFractionDigits:2 });
    return String(v);
  }

  function bcMeasureTextPx(text, bold){
    BC_MEASURE_CTX.font = `${bold ? 'bold ' : ''}${BC_FONT.size}pt "${BC_FONT.name}"`;
    return BC_MEASURE_CTX.measureText(text).width;
  }

  // Đổ 1 hàng dữ liệu vào worksheet đúng theo style của từng loại hàng (header/tổng/nhóm/size).
  // values: mảng theo đúng thứ tự cột: [Size, ...usedGroups, 'Tổng', Size(lặp lại)]
  // colPx (tuỳ chọn): mảng tích luỹ độ rộng pixel lớn nhất từng cột, dùng để tự căn cột sau khi ghi hết dữ liệu.
  function bcWriteRow(ws, values, kind, colPx){
    const row = ws.addRow(values);
    // Không set row.height cố định -> để Excel tự tính chiều cao dòng theo đúng cỡ chữ thật khi mở file
    // (auto-fit dòng thay vì áp 1 chiều cao cứng cho mọi dòng).
    const lastCol = values.length;
    row.eachCell({ includeEmpty:true }, (cell, colNumber)=>{
      const isLabelCol = (colNumber === 1 || colNumber === lastCol);
      let bold = kind !== 'size';
      cell.font = { ...BC_FONT, bold, color: kind === 'header' ? { argb:'FFFFFFFF' } : undefined };
      cell.alignment = { vertical:'center', horizontal: kind === 'header' ? 'center' : (kind === 'size' ? 'right' : (isLabelCol ? 'left' : 'right')) };
      if(kind === 'header'){
        cell.fill = BC_FILL_HEADER;
        cell.border = BC_BORDER_ALL_THIN;
      } else if(kind === 'total'){
        cell.fill = BC_FILL_TOTAL;
        cell.border = BC_BORDER_TOTAL;
      } else if(kind === 'group'){
        cell.fill = BC_FILL_GROUP;
        cell.border = BC_BORDER_ALL_THIN;
      } else {
        cell.border = BC_BORDER_ALL_THIN;
        // Trong dòng size, chỉ cột "Tổng" (áp chót) in đậm — các cột còn lại thường
        if(colNumber === lastCol - 1){ cell.font = { ...BC_FONT, bold:true }; bold = true; }
      }
      const isNumber = (!isLabelCol && colNumber !== lastCol && typeof cell.value === 'number');
      if(isNumber) cell.numFmt = BC_NUMFMT;

      if(colPx){
        const text = bcCellDisplayText(cell.value, isNumber);
        const px = text ? bcMeasureTextPx(text, bold) : 0;
        if(px > (colPx[colNumber-1] || 0)) colPx[colNumber-1] = px;
      }
    });
    return row;
  }

  // Hộp thoại yêu cầu nhập NGÀY BÁO CÁO trước khi xuất Excel "Bảng Tổng Hợp" — không tự lấy
  // ngày thực tế lúc bấm xuất. Trả về chuỗi "dd/mm/yyyy" nếu bấm "Xuất Excel", null nếu Hủy.
  // Dùng 3 ô nhập riêng (không dùng input type="date") vì input type="date" hiển thị theo
  // ngôn ngữ hệ điều hành (có máy ra mm/dd/yyyy) — 3 ô riêng luôn đúng thứ tự dd/mm/yyyy.
  function promptForReportDateBc(){
    // Giao diện + kiểm tra hợp lệ nằm ở js/report-date-dialog.js (dùng chung Module 1 + 2).
    // Điền sẵn "Ngày số liệu" của dữ liệu đã lưu (không có thì dialog tự lấy hôm nay).
    return window.showReportDateDialog(m2Meta.dataDate || '');
  }

  exportBtn7.addEventListener('click', async ()=>{
    if(!baoCaoPivot) return;
    const reportDateStr = await promptForReportDateBc();
    if(!reportDateStr) return; // bấm Hủy — không xuất file
    const buffer = await buildBangTongHopBuffer(baoCaoPivot, reportDateStr);
    const blob = new Blob([buffer], { type:'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
    const fname = `BangTongHop_${bcFileStamp()}.xlsx`;
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url; a.download = fname;
    document.body.appendChild(a); a.click(); document.body.removeChild(a);
    URL.revokeObjectURL(url);
  });

  function bcFileStamp(){
    const stamp = new Date();
    const pad = n => String(n).padStart(2,'0');
    return `${stamp.getFullYear()}${pad(stamp.getMonth()+1)}${pad(stamp.getDate())}_${pad(stamp.getHours())}${pad(stamp.getMinutes())}`;
  }

  // Dựng file Excel "Bảng Tổng Hợp" (ArrayBuffer .xlsx) từ 1 pivot (computeBaoCaoPivot) — dùng chung cho nút
  // Xuất Excel trên PC và file báo cáo gửi lên điện thoại (buildM2PhoneReportSnapshot), để ra ĐÚNG 1 file.
  async function buildBangTongHopBuffer(baoCaoPivot, reportDateStr){
    await klNap('exceljs'); // PC 0.19: nạp lười (cả nút Xuất Excel lẫn báo cáo gửi điện thoại)
    // Chỉ xuất những cột nhóm đặc tính có ít nhất 1 giá trị khác 0, để file Excel gọn hơn
    // (bảng hiển thị trên màn hình vẫn luôn đủ toàn bộ nhóm cố định).
    const usedGroups = DT_GROUPS.filter(g =>
      baoCaoPivot.grand.byGroup[g] || baoCaoPivot.categories.some(c => c.byGroup[g])
    );

    const toValues = (label, bucket, withMirror) => {
      const arr = [label];
      usedGroups.forEach(g => arr.push(bucket.byGroup[g] || ''));
      arr.push(bucket.tong || '');
      arr.push(withMirror ? label : '');
      return arr;
    };

    const wb = new ExcelJS.Workbook();
    const ws = wb.addWorksheet('Bang Tong Hop');

    const colCount = usedGroups.length + 3; // Size + nhóm... + Tổng + Size
    const colPx = []; // độ rộng pixel lớn nhất theo từng cột, đo theo đúng font/độ đậm của từng ô

    // Dòng tiêu đề "Ngày báo cáo" — nằm TRÊN dòng tiêu đề cột, gộp ô ngang hết bề rộng bảng.
    const titleRow = ws.addRow([`Ngày báo cáo: ${reportDateStr}`]);
    ws.mergeCells(1, 1, 1, colCount);
    titleRow.getCell(1).font = { ...BC_FONT, bold:true, size: BC_FONT.size + 2 };
    titleRow.getCell(1).alignment = { vertical:'center', horizontal:'left' };

    bcWriteRow(ws, ['Size', ...usedGroups, 'Tổng', 'Size'], 'header', colPx);
    bcWriteRow(ws, toValues('TỔNG TỒN', baoCaoPivot.grand, false), 'total', colPx);
    baoCaoPivot.categories.forEach(cat=>{
      bcWriteRow(ws, toValues(cat.label, cat, true), 'group', colPx);
      cat.sizes.forEach(sz => bcWriteRow(ws, toValues(sz.size, sz, true), 'size', colPx));
    });
    bcWriteRow(ws, toValues('TỔNG TỒN', baoCaoPivot.grand, false), 'total', colPx);

    // Tự căn chỉnh độ rộng cột: quy đổi độ rộng pixel đo thật (đúng font Times New Roman 14, có/không đậm)
    // sang đơn vị "độ rộng cột" của Excel, cộng thêm chút đệm cho viền/canh lề.
    for(let i=1;i<=colCount;i++){
      const px = colPx[i-1] || 0;
      const width = px ? (px / BC_MDW_PX) + 2.5 : 10;
      ws.getColumn(i).width = Math.min(Math.max(width, 8), 45);
    }

    // Tự căn chỉnh chiều cao dòng theo đúng cỡ chữ thật (đo bằng canvas), thay vì áp cứng 1 giá trị
    // không khớp với font Times New Roman 14 đang dùng. Bỏ qua dòng tiêu đề "Ngày báo cáo" (dòng 1)
    // vì cỡ chữ dòng đó lớn hơn, không dùng chung 1 chiều cao với các dòng dữ liệu bên dưới.
    BC_MEASURE_CTX.font = `${BC_FONT.size}pt "${BC_FONT.name}"`;
    const bcMetrics = BC_MEASURE_CTX.measureText('Mg0123456789');
    const bcLinePx = (bcMetrics.fontBoundingBoxAscent && bcMetrics.fontBoundingBoxDescent)
      ? (bcMetrics.fontBoundingBoxAscent + bcMetrics.fontBoundingBoxDescent)
      : BC_FONT.size * 1.2;
    const bcRowHeightPt = Math.ceil((bcLinePx * (72/96)) * 1.15);
    ws.eachRow((row, rowNumber) => { if(rowNumber > 1) row.height = bcRowHeightPt; });

    // Tạo filter cho dòng tiêu đề cột (dòng 2, vì dòng 1 giờ là "Ngày báo cáo").
    ws.autoFilter = { from: { row:2, column:1 }, to: { row:2, column: colCount } };

    // Đóng băng dòng tiêu đề khi cuộn, giống file mẫu.
    ws.views = [{ state:'frozen', ySplit:2 }];

    return await wb.xlsx.writeBuffer();
  }

  // ===== BÁO CÁO EXCEL CHO ĐIỆN THOẠI ("Bảng Tổng Hợp") =====
  // main.js (khi đẩy bảng M02 lên Google Sheet) nhờ Module 02 đang mở dựng sẵn file "Bảng Tổng Hợp" bằng
  // ĐÚNG buildBangTongHopBuffer() của nút Xuất Excel, gửi dạng base64 lên tab BaoCao_M02 — điện thoại chỉ
  // tải về lưu / chia sẻ. Pivot tính MỚI (không dùng baoCaoPivot đang vẽ, có thể cũ nếu chưa mở tab Báo Cáo).
  // Ngày báo cáo = "Ngày số liệu" (m2Meta.dataDate) — đúng giá trị hộp thoại PC điền sẵn; thiếu thì hôm nay.
  /* ================= PHÂN LOẠI CHO ĐIỆN THOẠI (PC 1.56 / ĐT 1.12) =================
     6 loại như các tab con của "Phân Loại" (cùng điều kiện lọc + điều kiện tự thêm): tổng KG + từng Size (số dòng, KG) + tối đa 25 mã hàng
     lớn nhất. Đi chung đường với file báo cáo (tab BaoCao_M02, file key 'phanloai'). */
  function plUtf8B64(obj){
    const bytes = new TextEncoder().encode(JSON.stringify(obj));
    let bin = '';
    for(let i = 0; i < bytes.length; i += 0x8000) bin += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
    return btoa(bin);
  }
  function plSizeCmp(a, b){
    const na = parseFloat(String(a).match(/\d+(?:[.,]\d+)?/) ? String(a).match(/\d+(?:[.,]\d+)?/)[0].replace(',', '.') : ''), nb = parseFloat(String(b).match(/\d+(?:[.,]\d+)?/) ? String(b).match(/\d+(?:[.,]\d+)?/)[0].replace(',', '.') : '');
    if(!isNaN(na) && !isNaN(nb) && na !== nb) return na - nb;
    if(!isNaN(na) && isNaN(nb)) return -1;
    if(isNaN(na) && !isNaN(nb)) return 1;
    return String(a).localeCompare(String(b), 'vi', { numeric: true });
  }
  function plBuildCross(sizeList, cell, dtTot, maxCols){
    // PC 1.64 — bảng chéo Size × Đặc tính (kg) cho điện thoại: tối đa maxCols đặc tính lớn nhất, phần còn lại gộp cột "Khác"
    const r1 = (n) => Math.round(n * 10) / 10;
    const order = Array.from(dtTot.entries()).sort((a, b) => b[1] - a[1]).map(x => x[0]);
    const main = order.slice(0, maxCols), rest = order.slice(maxCols);
    const cols = main.map(l => ({ label: l })).concat(rest.length ? [{ label: 'Khác', other: rest.length }] : []);
    const rows = sizeList.map(sz => {
      const m = cell.get(sz.label) || new Map();
      const cells = main.map(l => r1(m.get(l) || 0));
      if(rest.length) cells.push(r1(rest.reduce((a, l) => a + (m.get(l) || 0), 0)));
      let tot = 0; m.forEach(v => { tot += v; });
      return { label: sz.label, cells, total: r1(tot) };
    });
    return { cols, rows, totals: cols.map((_, i) => r1(rows.reduce((a, r) => a + r.cells[i], 0))), total: r1(rows.reduce((a, r) => a + r.total, 0)) };
  }
  function buildM2PhanLoai(){
    const out = { v:1, nguon:'m02', loai:[] };
    if(!persistedRows || !persistedRows.length) return out;
    const r1 = (n) => Math.round(n * 10) / 10;
    const defs = [
      { id:'nl', name:'Tôm Nguyên Liệu', color:'#A3E635', rows: () => applyCustomFiltersToRows(persistedRows.filter(r => isNguyenLieuSize(r.size)), 'nl') },
      { id:'5',  name:'Tôm HLSO',        color:'#FB7185', rows: () => applyCustomFiltersToRows(persistedRows.filter(r => isHLSOLot(r)), '5') },
      { id:'4',  name:'Tôm Giám Sát',    color:'#FB923C', rows: () => applyCustomFiltersToRows(persistedRows.filter(r => isTGSLot(r.soLo)), '4') },
      { id:'3',  name:'Tôm Gia Công',    color:'#38BDF8', rows: () => applyCustomFiltersToRows(persistedRows.filter(r => isTGCLot(r.soLo)), '3') },
      { id:'ad', name:'Ấn Độ',           color:'#818CF8', rows: () => applyCustomFiltersToRows(persistedRows.filter(isAnDoOnly), 'ad') },
      { id:'2',  name:'Ecuador',         color:'#F472B6', rows: () => applyCustomFiltersToRows(persistedRows.filter(r => isEcuadorLot(r.soLo)), '2') }
    ];
    defs.forEach(d => {
      const rows = d.rows() || [], bySize = new Map(), crossCell = new Map(), dtTot = new Map(); let total = 0;
      rows.forEach(r => {
        const w = toNumber(r.trongLuong), label = String(r.size == null ? '' : r.size).trim() || '(Không có size)';
        total += w;
        { const dtl = String(r.dacTinh == null ? '' : r.dacTinh).trim() || '(Trống)'; let cm = crossCell.get(label); if(!cm){ cm = new Map(); crossCell.set(label, cm); } cm.set(dtl, (cm.get(dtl) || 0) + w); dtTot.set(dtl, (dtTot.get(dtl) || 0) + w); } // bảng chéo Size × Đặc tính
        let sz = bySize.get(label); if(!sz){ sz = { label, net:0, rows:0, items:new Map() }; bySize.set(label, sz); }
        sz.net += w; sz.rows++;
        const ma = String(r.maHang == null ? '' : r.maHang).trim(), lo = String(r.soLo == null ? '' : r.soLo).trim(), key = ma + '|' + lo;
        let it = sz.items.get(key); if(!it){ it = { ma, sub: lo ? 'Lô ' + lo : '', dt: String(r.dacTinh == null ? '' : r.dacTinh).trim(), net:0 }; sz.items.set(key, it); }
        it.net += w;
      });
      const sizes = Array.from(bySize.values()).map(sz => {
        const all = Array.from(sz.items.values()).sort((a, b) => b.net - a.net);
        return { label: sz.label, net: r1(sz.net), rows: sz.rows, low: 0, more: Math.max(0, all.length - 25), items: all.slice(0, 25).map(i => ({ ma:i.ma, sub:i.sub, dt:i.dt, net:r1(i.net), low:0 })) };
      }).sort((a, b) => plSizeCmp(a.label, b.label));
      const lo = { id: d.id, name: d.name, color: d.color, net: r1(total), rows: rows.length, sizes };
      if(dtTot.size) lo.cross = plBuildCross(sizes, crossCell, dtTot, 6);
      out.loai.push(lo);
    });
    return out;
  }
  async function buildM2PhoneReportSnapshot(){
    try{ await pxHydrationDone; }catch(e){ if(window.klLog) klLog('ton-kho-gui.js:5342',e); }
    const now = new Date();
    const pad = n => String(n).padStart(2,'0');
    const dateStr = m2Meta.dataDate || `${pad(now.getDate())}/${pad(now.getMonth()+1)}/${now.getFullYear()}`;
    // importedAt: để main.js so với DuLieuMeta trong database — khác nhau = trang này đang giữ số liệu CŨ (PC 2.6)
    const snap = { ok:true, generatedAt: now.toISOString(), dataDate: m2Meta.dataDate || '', importedAt: m2Meta.importedAt || null, reportDate: dateStr, files: [], errors: [] };
    if(!persistedRows.length) return snap; // chưa nạp dữ liệu — không có báo cáo
    try{
      const pivot = computeBaoCaoPivot();
      const buffer = await buildBangTongHopBuffer(pivot, dateStr);
      const bytes = new Uint8Array(buffer);
      let bin = '';
      for(let i = 0; i < bytes.length; i += 0x8000) bin += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
      snap.files.push({ key:'tonghop', title:'Báo cáo kho gửi', name:`BangTongHop_${bcFileStamp()}.xlsx`, b64: btoa(bin) });
    }catch(e){
      console.error('Lỗi dựng file báo cáo cho điện thoại (Bảng Tổng Hợp):', e);
      snap.errors.push('Bảng Tổng Hợp: ' + String(e && e.message || e));
    }
    // PC 1.56: dữ liệu "Phân loại" cho điện thoại (để SAU file Excel; lỗi ở đây không ảnh hưởng báo cáo Excel)
    try{
      const pl = buildM2PhanLoai();
      if(pl.loai.length) snap.files.push({ key:'phanloai', title:'Phân loại (Kho gửi)', name:'PhanLoai_M02.json', b64: plUtf8B64(pl) });
    }catch(e){
      console.error('Lỗi dựng dữ liệu Phân loại cho điện thoại (Module 02):', e);
      snap.errors.push('Phân loại: ' + String(e && e.message || e));
    }
    return snap;
  }


  // ---- TAB 8: ĐỐI SOÁT (Load data ⇄ Tổng 6 module ⇄ Bảng Tổng Hợp) ----
  const doiSoatContainer = document.getElementById('doiSoatContainer');

  function ds_fmtKg(n){ return formatWeight(n) + ' kg'; }

  // Tính tổng Trọng lượng (KG) của toàn bộ dữ liệu đã nạp ở "Load data"
  function ds_loadDataTotal(){
    if(!persistedRows || !persistedRows.length) return 0;
    return persistedRows.reduce((s,r)=> s + toNumber(r.trongLuong), 0);
  }

  // Với mỗi dòng, đếm xem nó rơi vào bao nhiêu trong 6 module (Ecuador/Ấn Độ/TGC/TGS/NL/HLSO).
  // count = 0  -> không thuộc module nào (thiếu)
  // count = 1  -> bình thường
  // count >= 2 -> bị đếm trùng ở nhiều module (Ecuador/Ấn Độ/TGC/TGS/NL không loại trừ lẫn nhau)
  // Đồng thời tính riêng phần KHÔNG suy ra được đặc tính (Bảng Tổng Hợp không tính các dòng này).
  function ds_computeOverlap(){
    let dupSum = 0, dupCount = 0, missingSum = 0, missingCount = 0, moduleTotal = 0;
    let noDacTinhSum = 0, noDacTinhCount = 0;
    let ungroupedSum = 0, ungroupedCount = 0; // có đặc tính nhưng CHƯA gộp vào nhóm nào (không hiện số liệu ở Bảng Tổng Hợp)
    // Danh sách chi tiết từng dòng (mã hàng) gây ra mỗi loại chênh lệch — để hiển thị khi người dùng bấm xem chi tiết.
    const dupList = [], missingList = [], noDacTinhList = [], ungroupedList = [];
    if(persistedRows && persistedRows.length){
      persistedRows.forEach(r=>{
        const w = toNumber(r.trongLuong);
        const modules = [];
        if(isEcuadorLot(r.soLo)) modules.push('Ecuador');
        if(isAnDoOnly(r)) modules.push('Ấn Độ');
        if(isTGCLot(r.soLo)) modules.push('Gia Công');
        if(isTGSLot(r.soLo)) modules.push('Giám Sát');
        if(isNguyenLieuSize(r.size)) modules.push('Nguyên Liệu');
        const k = modules.length;
        if(k === 0) modules.push('HLSO');
        const total = k + (k === 0 ? 1 : 0); // HLSO chỉ tính khi k===0 (phần bù)
        moduleTotal += w * total;
        if(total >= 2){
          dupSum += w * (total - 1); dupCount++;
          dupList.push({ maHang: r.maHang, tenHang: r.tenHang, soLo: r.soLo, size: r.size, trongLuong: w, modulesStr: modules.join(', ') });
        }
        if(total === 0){
          missingSum += w; missingCount++;
          missingList.push({ maHang: r.maHang, tenHang: r.tenHang, soLo: r.soLo, size: r.size, trongLuong: w });
        }
        const dacTinh = deriveDacTinhFromTenHang(r.tenHang, r.soLo);
        if(!dacTinh){
          noDacTinhSum += w * total; noDacTinhCount++;
          noDacTinhList.push({ maHang: r.maHang, tenHang: r.tenHang, soLo: r.soLo, size: r.size, trongLuong: w * total });
        } else if(!dtGroupMap[dacTinh]){
          ungroupedSum += w * total; ungroupedCount++;
          ungroupedList.push({ maHang: r.maHang, tenHang: r.tenHang, soLo: r.soLo, size: r.size, trongLuong: w * total, dacTinh });
        }
      });
      // Ưu tiên hiện các dòng có trọng lượng lớn trước — dễ tìm ra mã hàng gây lệch nhiều nhất.
      const byWeightDesc = (a,b)=> Math.abs(b.trongLuong) - Math.abs(a.trongLuong);
      dupList.sort(byWeightDesc);
      missingList.sort(byWeightDesc);
      noDacTinhList.sort(byWeightDesc);
      ungroupedList.sort(byWeightDesc);
    }
    return { moduleTotal, dupSum, dupCount, missingSum, missingCount, noDacTinhSum, noDacTinhCount, ungroupedSum, ungroupedCount, dupList, missingList, noDacTinhList, ungroupedList };
  }

  // ---- Tổng số kiện đã bị TRỪ khỏi "Dữ Liệu" do tạo phiếu xuất — lấy từ toàn bộ pxPhieuList
  // (không chỉ những dòng persistedRows hiện còn "_pxDaXuat", vì nguồn phiếu xuất vẫn giữ đúng
  // lịch sử kể cả khi dòng gốc trong Load Data đã bị nạp lại/thay đổi sau đó).
  function ds_computePxDeducted(){
    let totalSoLuong = 0;
    const list = [];
    (pxPhieuList || []).forEach(p=>{
      if(!pxIsActive(p)) return; // PC 1.69: chỉ tính phiếu đang trừ vào số liệu hiện tại
      (p.items || []).forEach(it=>{
        const qty = toNumber(it.soLuongXuat);
        totalSoLuong += qty;
        list.push({
          maHang: it.maHang, tenHang: it.tenHang, soLo: it.soLo, size: it.size,
          soLuongXuat: qty, maPhieu: p.maPhieu || '', ngayTao: formatPxDate(p.ngayTao)
        });
      });
    });
    list.sort((a,b)=> Math.abs(b.soLuongXuat) - Math.abs(a.soLuongXuat));
    return { totalSoLuong, count: list.length, list };
  }

  // ---- Bấm vào 1 dòng chênh lệch trong Đối Soát để mở/đóng bảng chi tiết mã hàng ----
  const doiSoatOpenSections = {}; // { sectionKey: true/false }
  function ds_toggleSection(key){
    doiSoatOpenSections[key] = !doiSoatOpenSections[key];
    refreshDoiSoatTab();
  }
  const DS_DETAIL_MAX_ROWS = 300; // tránh render quá nhiều dòng nếu chênh lệch quá lớn
  function ds_renderDetailRow(sectionKey, list, columns, colspan){
    colspan = colspan || 2;
    if(!doiSoatOpenSections[sectionKey]) return '';
    if(!list.length){
      return `<tr class="ds-detail-row"><td colspan="${colspan}"><div class="ds-detail-empty">Không có dòng nào.</div></td></tr>`;
    }
    const shown = list.slice(0, DS_DETAIL_MAX_ROWS);
    let inner = '<table class="ds-detail-table"><thead><tr>';
    columns.forEach(c => inner += `<th>${escapeHtml(c.label)}</th>`);
    inner += '</tr></thead><tbody>';
    shown.forEach(item=>{
      inner += '<tr>';
      columns.forEach(c=>{
        const raw = item[c.key];
        const val = c.key === 'trongLuong' ? formatWeight(raw) : (c.key === 'soLuongXuat' ? formatNumber(raw) : String(raw ?? ''));
        inner += `<td>${escapeHtml(val)}</td>`;
      });
      inner += '</tr>';
    });
    inner += '</tbody></table>';
    if(list.length > DS_DETAIL_MAX_ROWS){
      inner += `<div class="ds-detail-more">… và ${formatNumber(list.length - DS_DETAIL_MAX_ROWS)} dòng khác (đã ẩn bớt để bảng không quá dài).</div>`;
    }
    return `<tr class="ds-detail-row"><td colspan="${colspan}">${inner}</td></tr>`;
  }

  function ds_diffRowClass(diff){
    return 'ds-diff' + (Math.abs(diff) < 0.005 ? ' ds-ok' : '');
  }

  function refreshDoiSoatTab(){
    const hasData = persistedRows && persistedRows.length;
    const hasModuleData = [hlsoRows, tgcRows, ecuadorRows, anDoRows, nlRows, tgsRows].some(a => a && a.length);

    if(!hasData && !hasModuleData){
      doiSoatContainer.innerHTML = '<div class="empty-table" style="height:120px;">Chưa có dữ liệu — hãy nạp file ở menu "Load data" trước.</div>';
      const pxDaTruContainerElEmpty = document.getElementById('pxDaTruContainer');
      if(pxDaTruContainerElEmpty) pxDaTruContainerElEmpty.innerHTML = '<div class="empty-table" style="height:120px;">Chưa có dữ liệu.</div>';
      return;
    }

    const loadTotal = ds_loadDataTotal();
    const overlap = ds_computeOverlap();
    const moduleTotal = overlap.moduleTotal;
    const diff1 = moduleTotal - loadTotal;

    const pivot = computeBaoCaoPivot();
    const baoCaoTotal = pivot.grand.tong;
    const diff2 = baoCaoTotal - moduleTotal;
    const diff2Unexplained = diff2 + overlap.noDacTinhSum + overlap.ungroupedSum; // phần lệch KHÔNG giải thích được bởi "chưa có đặc tính" / "chưa gộp nhóm"

    let html = '';

    // PC 5.35 — BẢNG NGANG: trước đây bảng đối soát xếp DỌC (9 dòng, hẹp 340–420px) nên rất dài; nay đổi thành 1 dòng
    // dữ liệu chia 3 cụm (Tổng trọng lượng · Chênh lệch 6 Module − Load Data · Chênh lệch Bảng Tổng Hợp − 6 Module),
    // rộng hết chiều ngang. Số liệu + công thức GIỮ NGUYÊN; bấm vào ô "Do dòng …" vẫn mở bảng chi tiết (hiện ngay dưới bảng).
    const dupCols = [
      { key:'maHang', label:'Mã hàng' }, { key:'tenHang', label:'Tên hàng' },
      { key:'soLo', label:'Số lô' }, { key:'size', label:'Size' },
      { key:'trongLuong', label:'Trọng lượng (kg)' }, { key:'modulesStr', label:'Thuộc module' }
    ];
    const missingCols = [
      { key:'maHang', label:'Mã hàng' }, { key:'tenHang', label:'Tên hàng' },
      { key:'soLo', label:'Số lô' }, { key:'size', label:'Size' },
      { key:'trongLuong', label:'Trọng lượng (kg)' }
    ];
    const noDacTinhCols = missingCols;
    const ungroupedCols = [
      { key:'maHang', label:'Mã hàng' }, { key:'tenHang', label:'Tên hàng' },
      { key:'soLo', label:'Số lô' }, { key:'size', label:'Size' },
      { key:'trongLuong', label:'Trọng lượng (kg)' }, { key:'dacTinh', label:'Đặc tính' }
    ];

    const dsDiffCls = (d) => 'ds-hv-diff' + (Math.abs(d) < 0.005 ? ' ds-ok' : '');
    const dsSubCell = (key, label, count, signedText, lastInGroup) => {
      const open = doiSoatOpenSections[key] ? ' ds-open' : '';
      const clk = ` onclick="ds_toggleSection('${key}')"`;
      const sep = lastInGroup ? ' ds-gsep-r' : '';
      return {
        lbl: `<td class="ds-hl ds-hsub${open}${sep}"${clk}>└ ${label}${count ? ' <span class="ds-hint">(bấm xem chi tiết)</span>' : ''}</td>`,
        val: `<td class="ds-hv ds-hsub${open}${sep}"${clk}>${signedText}<span class="ds-sub">${formatNumber(count)} dòng</span></td>`
      };
    };
    const cDup = dsSubCell('dup', 'Do dòng đếm ở ≥2 module (trùng)', overlap.dupCount, '+' + formatWeight(overlap.dupSum), false);
    const cMiss = dsSubCell('missing', 'Do dòng không thuộc module nào', overlap.missingCount, '-' + formatWeight(overlap.missingSum), true);
    const cNoDt = dsSubCell('noDacTinh', 'Do dòng chưa suy ra được đặc tính (Bảng Tổng Hợp không tính)', overlap.noDacTinhCount, '-' + formatWeight(overlap.noDacTinhSum), false);
    const cUngr = dsSubCell('ungrouped', 'Do dòng có đặc tính nhưng chưa gộp vào nhóm nào', overlap.ungroupedCount, '-' + formatWeight(overlap.ungroupedSum), false);

    html += '<div class="doisoat-card">';
    html += '<div class="ds-title">Đối Soát Số Liệu</div>';
    html += '<div class="ds-h-scroll"><table class="ds-table ds-h"><thead><tr>' +
      '<th colspan="3" class="ds-grp ds-gsep-r">Tổng trọng lượng (kg)</th>' +
      '<th colspan="3" class="ds-grp ds-gsep-r">Chênh lệch (6 Module − Load Data)</th>' +
      '<th colspan="3" class="ds-grp">Chênh lệch (Bảng Tổng Hợp − 6 Module)</th>' +
      '</tr></thead><tbody>';
    // hàng nhãn
    html += '<tr class="ds-h-labels">' +
      '<td class="ds-hl">Tổng toàn bộ dữ liệu Load Data</td>' +
      '<td class="ds-hl">Tổng cộng 6 module <span class="ds-hint">(Ecuador, Ấn Độ, Gia Công, Giám Sát, Nguyên Liệu, HLSO)</span></td>' +
      '<td class="ds-hl ds-gsep-r">Tổng theo Bảng Tổng Hợp <span class="ds-hint">(dòng TỔNG TỒN)</span></td>' +
      '<td class="ds-hl ds-hdiff">Chênh lệch</td>' + cDup.lbl + cMiss.lbl +
      '<td class="ds-hl ds-hdiff">Chênh lệch</td>' + cNoDt.lbl + cUngr.lbl +
      '</tr>';
    // hàng giá trị
    html += '<tr class="ds-h-vals">' +
      `<td class="ds-hv">${ds_fmtKg(loadTotal)}</td>` +
      `<td class="ds-hv">${ds_fmtKg(moduleTotal)}</td>` +
      `<td class="ds-hv ds-gsep-r">${ds_fmtKg(baoCaoTotal)}</td>` +
      `<td class="ds-hv ${dsDiffCls(diff1)}">${ds_fmtKg(diff1)}</td>` + cDup.val + cMiss.val +
      `<td class="ds-hv ${dsDiffCls(diff2Unexplained)}">${ds_fmtKg(diff2)}</td>` + cNoDt.val + cUngr.val +
      '</tr>';
    html += '</tbody></table></div>';
    // chi tiết (mở khi bấm ô "Do dòng …") — hiện ngay dưới bảng ngang, rộng hết chiều ngang
    const dsDetail =
      ds_renderDetailRow('dup', overlap.dupList, dupCols, 1) +
      ds_renderDetailRow('missing', overlap.missingList, missingCols, 1) +
      ds_renderDetailRow('noDacTinh', overlap.noDacTinhList, noDacTinhCols, 1) +
      ds_renderDetailRow('ungrouped', overlap.ungroupedList, ungroupedCols, 1);
    if(dsDetail) html += '<table class="ds-table ds-h-detail"><tbody>' + dsDetail + '</tbody></table>';
    html += '</div>';

    doiSoatContainer.innerHTML = html;

    // ---- Card riêng: Dữ Liệu đã bị TRỪ do tạo phiếu xuất (đơn vị: Số kiện, khác đơn vị Trọng
    // lượng ở trên) — render vào khung RIÊNG (pxDaTruContainer, đặt ở DÒNG 2 cùng 2 bảng "Tỉ Lệ
    // %"), tách khỏi thẻ "Đối Soát Số Liệu" ở DÒNG 1 theo đúng bố cục yêu cầu.
    const pxDaTruContainerEl = document.getElementById('pxDaTruContainer');
    if(pxDaTruContainerEl){
      const pxDeducted = ds_computePxDeducted();
      const pxDaTruCols = [
        { key:'maHang', label:'Mã hàng' }, { key:'tenHang', label:'Tên hàng' },
        { key:'soLo', label:'Số lô' }, { key:'size', label:'Size' },
        { key:'soLuongXuat', label:'Số kiện đã xuất' },
        { key:'maPhieu', label:'Mã phiếu xuất' }, { key:'ngayTao', label:'Ngày tạo phiếu' }
      ];
      let pxHtml = '<div class="doisoat-card">';
      pxHtml += '<div class="ds-title">Dữ Liệu Đã Trừ Theo Phiếu Xuất</div>';
      pxHtml += '<table class="ds-table"><thead><tr><th></th><th>SỐ KIỆN</th></tr></thead><tbody>';
      pxHtml += `<tr><td class="ds-label">Tổng số phiếu xuất đã tạo</td><td class="ds-value">${formatNumber((pxPhieuList||[]).filter(pxIsActive).length)}${(pxPhieuList||[]).some(p => !pxIsActive(p)) ? `<span class="ds-sub">+ ${formatNumber((pxPhieuList||[]).filter(p => !pxIsActive(p)).length)} phiếu đã khóa (tồn cũ)</span>` : ''}</td></tr>`;
      pxHtml += `<tr class="ds-sub-row${doiSoatOpenSections.pxDaTru ? ' ds-open' : ''}" onclick="ds_toggleSection('pxDaTru')"><td class="ds-label">└ Tổng số kiện đã trừ khỏi "Dữ Liệu" theo tất cả phiếu xuất <span class="ds-hint">${pxDeducted.count ? '(bấm để xem chi tiết)' : ''}</span></td>` +
        `<td class="ds-value">-${formatNumber(pxDeducted.totalSoLuong)}<span class="ds-sub">${formatNumber(pxDeducted.count)} dòng</span></td></tr>`;
      pxHtml += ds_renderDetailRow('pxDaTru', pxDeducted.list, pxDaTruCols);
      pxHtml += '</tbody></table></div>';
      pxDaTruContainerEl.innerHTML = pxHtml;
    }
  }

  // ---- Tìm kiếm thông minh dùng chung cho các bảng "object rows" (Ecuador/TGC/TGS/Nguyên Liệu/HLSO) ----
  // excludeIdx: nếu truyền vào, bộ lọc (tìm kiếm + checkbox) của CHÍNH cột đó bị bỏ qua — dùng để xây
  // "lọc liên kết theo tầng" (cascading filter): danh sách giá trị/gợi ý của 1 cột chỉ phụ thuộc các
  // cột KHÁC đã lọc, không phụ thuộc bộ lọc hiện có của chính cột đó.
  // Cột "Đặc tính" (dacTinh) KHÔNG phải field có sẵn trong dữ liệu gốc của các tab Phân Loại
  // (Ecuador/Ấn Độ/TGC/TGS/Nguyên Liệu/HLSO) — nó được SUY RA từ "Tên hàng" bằng
  // deriveDacTinhFromTenHang(), giống hệt cách rowToDisplayArray()/rowToExportObj() ở trên đã
  // làm cho tab "Dữ Liệu" chính. Các bảng dạng OBJECT dùng chung (applyColumnSearch/
  // buildSuggestionsForColumnObj/applySortObjRows/popup lọc) trước đây đọc thẳng r['dacTinh']
  // (luôn rỗng vì field này không tồn tại trên object gốc) -> popup lọc "Đặc tính" không có giá
  // trị nào để chọn, gõ tìm kiếm/sắp xếp theo cột này cũng không hoạt động — dù cột vẫn HIỂN THỊ
  // đúng giá trị (vì phần vẽ bảng đã gọi rowToDisplayArray() tính đúng riêng). Hàm này thay thế
  // mọi chỗ đọc "r[key]" trực tiếp để luôn lấy đúng giá trị đã suy ra cho cột "dacTinh".
  function objColValue(r, key){
    if(key === 'dacTinh') return deriveDacTinhFromTenHang(r.tenHang, r.soLo);
    return r[key];
  }

  function applyColumnSearch(objRows, columns, searchFilters, tableId, excludeIdx){
    const active = Object.keys(searchFilters)
      .map(k => [parseInt(k,10), searchFilters[k]])
      .filter(([idx, v]) => idx !== excludeIdx && v && v.trim() !== '');
    const activeCheckbox = [];
    if(tableId){
      columns.forEach((c,i)=>{
        if(i === excludeIdx) return;
        const set = checkboxFilters[cfKey(tableId, i)];
        if(set) activeCheckbox.push([i, set]);
      });
    }
    // PC 0.32 — SỬA LỖI lọc Mã hàng: (1) popup lọc ▾ (js/table-col-search.js) lưu giá trị đã .trim(), còn ở đây trước đây so
    // NGUYÊN giá trị ô (mã hàng nạp từ Excel hay thừa dấu cách / xuống dòng ở đầu-cuối) → tick đúng mã mà bảng không ra dòng
    // nào hoặc thiếu dòng; nay so cả hai vế đã .trim(). (2) Ô gõ tìm không phân biệt khoảng trắng giữa các chữ
    // (dán "VRHL BTN 0050" vẫn ra "VRHLBTN0050…"), giống ô tìm chung trang Dữ Liệu.
    const compactSearch = (v) => normalizeSearch(v).replace(/\s+/g, '');
    const activeCompact = active.map(([idx, v]) => [idx, compactSearch(v)]).filter(([, v]) => v !== '');
    const result = (!activeCompact.length && !activeCheckbox.length) ? objRows :
      objRows.filter(r =>
        activeCompact.every(([idx, val]) => compactSearch(String(objColValue(r, columns[idx].key) ?? '')).includes(val)) &&
        activeCheckbox.every(([idx, set]) => set.has(String(objColValue(r, columns[idx].key) ?? '').trim()))
      );
    return tableId ? applySortObjRows(result, columns, tableId) : result;
  }

  function buildSuggestionsForColumnObj(objRows, colKey){
    const set = new Set();
    objRows.forEach(r=>{
      const s = String(objColValue(r, colKey) ?? '').trim();
      if(s !== '') set.add(s);
    });
    const MAX_SUGGESTIONS = 2000; // PC 0.32: 300 → 2000 (Mã hàng nhiều hơn 300 giá trị thì phần sau A→Z trước đây không có gợi ý)
    return Array.from(set).sort((a,b)=> a.localeCompare(b, 'vi')).slice(0, MAX_SUGGESTIONS);
  }

  // Bảng dùng chung cho 5 tab (Ecuador/TGC/TGS/Nguyên Liệu/HLSO), có dòng tìm kiếm thông minh + gợi ý.
  // searchCtx = { state:{searchFilters,lastHeaderKey,bound}, allRows: dữ liệu gốc (object) để tạo gợi ý, onSearchInput(idx,val) }
  // ---- Kéo dãn cột / dòng cho bảng (dùng chung cho tất cả bảng .filtered-table) ----
  // Rê chuột gần viền PHẢI của bất kỳ ô nào (tiêu đề, dòng tìm kiếm, hay ô dữ liệu) để đổi ĐỘ RỘNG CỘT.
  // Rê chuột gần viền DƯỚI của một ô dữ liệu (tbody) để đổi CHIỀU CAO của dòng đó.
  // (Không cho đổi chiều cao dòng tiêu đề vì 2 dòng tiêu đề đang dùng position:sticky với top cố định.)
  const RESIZE_EDGE = 6; // px — khoảng cách tính từ viền để coi là đang rê viền
  const colWidthOverrides  = {}; // "tableId:colIndex" -> độ rộng (px) người dùng đã kéo
  const rowHeightOverrides = {}; // "tableId:rowKey"   -> chiều cao (px) người dùng đã kéo
  function sizeKey(tableId, part){ return tableId + ':' + part; }

  // Khoá nhận diện 1 dòng dữ liệu dựa trên nội dung vài ô đầu tiên — để chiều cao đã chỉnh
  // không bị mất khi bảng vẽ lại do lọc/tìm kiếm/đổi trang (chỉ mất nếu chính dòng đó không còn hiển thị).
  function rowKeyFromCells(cellEls){
    return Array.from(cellEls).slice(0, 3).map(td => (td.textContent || '').trim()).join('|');
  }

  // Áp lại các độ rộng/chiều cao đã lưu mỗi khi bảng được vẽ (hoặc vẽ lại) — gọi sau khi đã có đủ
  // dòng tiêu đề + dòng dữ liệu trong DOM.
  function applyStoredSizes(tableEl, tableId){
    if(!tableEl || !tableId) return;
    const headRow = tableEl.querySelector('thead tr'); // dòng đầu tiên quyết định độ rộng cột (table-layout:fixed)
    if(headRow){
      let any = false;
      Array.from(headRow.children).forEach((th, i)=>{
        const w = colWidthOverrides[sizeKey(tableId, i)];
        if(w){ th.style.width = w + 'px'; any = true; }
      });
      // Có áp độ rộng đã lưu → khoá lại độ rộng <table> theo tổng các cột (xem chú thích ở
      // mousemove kéo cột), tránh trình duyệt chia lại tỉ lệ làm lệch cột.
      // Chỉ khoá khi MỌI cột đều đã có độ rộng (bảng mới dựng chưa được tự căn thì để
      // js/table-resize.js tự căn + khoá, tránh khoá theo tổng thiếu).
      if(any && window.TableResize && Array.from(headRow.children).every(th => th.style.width)){
        window.TableResize.syncTableWidth(tableEl);
      }
    }
    tableEl.querySelectorAll('tbody tr').forEach(tr=>{
      if(!tr.children.length) return;
      const h = rowHeightOverrides[sizeKey(tableId, rowKeyFromCells(tr.children))];
      if(h) tr.style.height = h + 'px';
    });
  }

  // Gắn 1 lần cho mỗi container bảng (container không bị thay thế giữa các lần vẽ lại).
  function enableCellResize(container, tableId){
    let dragMode = null; // 'col' | 'row'
    let dragTh = null, dragColIndex = -1, startX = 0, startWidth = 0, dragMoved = false;
    let dragTr = null, startY = 0, startHeight = 0;

    container.addEventListener('mousemove', (e)=>{
      if(dragMode) return;
      const cell = e.target.closest && e.target.closest('td, th');
      if(!cell){ return; }
      const rect = cell.getBoundingClientRect();
      const posIdx = Array.from(cell.parentElement.children).indexOf(cell);
      const nearRight = rect.right - e.clientX <= RESIZE_EDGE;
      // <th> dùng position:sticky nên tự lập 1 khối xếp lớp riêng — <th> đứng sau trong DOM
      // luôn đè lên đúng vị trí viền phải của <th> đứng trước (dù ĐANG rê chuột ngay tại đó),
      // khiến e.target trả về nhầm sang ô kế tiếp. Dò thêm viền TRÁI của ô hiện tại — nếu đang ở
      // gần viền trái, thực chất người dùng đang muốn kéo giãn CỘT TRƯỚC (viền phải của nó),
      // không phải ô hiện tại. Xem chú thích đầy đủ ở js/table-resize.js (hàm addColResizers).
      const nearLeft = posIdx > 0 && (e.clientX - rect.left) <= RESIZE_EDGE;
      const nearBottom = rect.bottom - e.clientY <= RESIZE_EDGE && cell.parentElement.parentElement.tagName === 'TBODY';
      cell.style.cursor = (nearRight || nearLeft) ? 'col-resize' : (nearBottom ? 'row-resize' : '');
    });

    container.addEventListener('mousedown', (e)=>{
      const cell = e.target.closest && e.target.closest('td, th');
      if(!cell) return;
      const rect = cell.getBoundingClientRect();
      const tr = cell.parentElement;
      const posIdx = Array.from(tr.children).indexOf(cell);
      const nearRight = rect.right - e.clientX <= RESIZE_EDGE;
      const nearLeft = posIdx > 0 && (e.clientX - rect.left) <= RESIZE_EDGE;
      const nearBottom = rect.bottom - e.clientY <= RESIZE_EDGE;
      if(nearRight || nearLeft){
        // nearLeft => viền phải của CỘT TRƯỚC (idx-1) mới là viền người dùng thực sự muốn kéo.
        const idx = posIdx - (nearLeft && !nearRight ? 1 : 0);
        const table = cell.closest('table');
        const headRow = table.querySelector('thead tr');
        const headTh = headRow && headRow.children[idx];
        if(!headTh) return;
        dragMode = 'col';
        dragTh = headTh;
        dragColIndex = idx;
        dragMoved = false;
        startX = e.clientX;
        startWidth = headTh.getBoundingClientRect().width;
        headTh.style.zIndex = 20;
        e.preventDefault();
      } else if(nearBottom && tr.parentElement.tagName === 'TBODY'){
        dragMode = 'row';
        dragTr = tr;
        startY = e.clientY;
        startHeight = tr.getBoundingClientRect().height;
        e.preventDefault();
      }
    });

    // Nhấp đúp vào viền trái/phải của BẤT KỲ ô nào (tiêu đề, dòng tìm kiếm, dữ liệu) → bung cột
    // đó vừa khít nội dung dài nhất (giống Excel). Trước đây chỉ có kéo, không có nhấp đúp.
    container.addEventListener('dblclick', (e)=>{
      if(e.target.closest && e.target.closest('.col-resizer')) return; // tay cầm tự xử lý
      const cell = e.target.closest && e.target.closest('td, th');
      if(!cell || (cell.colSpan || 1) > 1 || !window.TableResize) return;
      const rect = cell.getBoundingClientRect();
      const posIdx = Array.from(cell.parentElement.children).indexOf(cell);
      const nearRight = rect.right - e.clientX <= RESIZE_EDGE;
      const nearLeft = posIdx > 0 && (e.clientX - rect.left) <= RESIZE_EDGE;
      if(!nearRight && !nearLeft) return;
      const idx = posIdx - (nearLeft && !nearRight ? 1 : 0);
      const table = cell.closest('table');
      const headRow = table && table.querySelector('thead tr');
      const headTh = headRow && headRow.children[idx];
      if(!headTh) return;
      e.preventDefault();
      window.TableResize.autoFitColumn(table, headTh); // -> bắn "tablecolautofit" (lưu ở dưới)
    });
    // Độ rộng do js/table-resize.js đổi (nhấp đúp / kéo tay cầm ở tiêu đề) cũng phải lưu vào
    // colWidthOverrides — nếu không, lần vẽ lại kế tiếp applyStoredSizes() sẽ áp lại độ rộng CŨ.
    const rememberWidth = (e)=>{
      const d = e.detail || {};
      if(typeof d.index === 'number' && d.width > 0) colWidthOverrides[sizeKey(tableId, d.index)] = d.width;
    };
    container.addEventListener('tablecolautofit', rememberWidth);
    container.addEventListener('tablecolresize', rememberWidth);

    document.addEventListener('mousemove', (e)=>{
      if(dragMode === 'col' && dragTh){
        if(!dragMoved && Math.abs(e.clientX - startX) >= 2){
          dragMoved = true;
          // Báo cho js/table-resize.js: người dùng đã tự chỉnh — không tự nới rộng đè lên nữa.
          const t = dragTh.closest('table');
          if(t) t.__manualResize = true;
        }
        dragTh.style.width = Math.max(40, startWidth + (e.clientX - startX)) + 'px';
        // Khoá lại độ rộng <table> theo tổng các cột NGAY khi kéo — <table> đang bị khoá cứng px
        // (table-layout:fixed) nên nếu không cập nhật, trình duyệt chia lại tỉ lệ cho MỌI cột và
        // việc kéo 1 cột trông như "không ăn"/làm lệch các cột khác.
        if(window.TableResize) window.TableResize.syncTableWidth(dragTh.closest('table'));
      } else if(dragMode === 'row' && dragTr){
        dragTr.style.height = Math.max(24, startHeight + (e.clientY - startY)) + 'px';
      }
    });

    document.addEventListener('mouseup', ()=>{
      if(dragMode === 'col' && dragTh){
        // Chỉ lưu khi THẬT SỰ có kéo — trước đây 1 cú nhấp (kể cả cú nhấp đầu của nhấp đúp) vào
        // viền cũng ghim cứng độ rộng hiện tại, khiến cột không còn tự căn theo dữ liệu.
        if(dragMoved){
          colWidthOverrides[sizeKey(tableId, dragColIndex)] = parseFloat(dragTh.style.width);
          justResizedCell = true;
        }
        dragTh.style.zIndex = '';
      } else if(dragMode === 'row' && dragTr){
        rowHeightOverrides[sizeKey(tableId, rowKeyFromCells(dragTr.children))] = parseFloat(dragTr.style.height);
        justResizedCell = true;
      }
      dragMode = null; dragTh = null; dragTr = null;
    });
  }

  function renderGenericTable(container, columns, rows, filteredMode, searchCtx){
    if(!searchCtx){
      // Không có ngữ cảnh tìm kiếm -> vẽ bảng đơn giản (không dùng tới trong app này, giữ lại để an toàn)
      let html = `<table${filteredMode ? ' class="filtered-table"' : ''}><thead><tr>`;
      columns.forEach(c => html += `<th>${escapeHtml(c.label)}</th>`);
      html += '</tr></thead><tbody>';
      rows.forEach(r=>{
        html += '<tr>';
        columns.forEach((_, i)=> html += `<td>${escapeHtml(String(r[i] ?? ''))}</td>`);
        html += '</tr>';
      });
      html += '</tbody></table>';
      container.innerHTML = html;
      return;
    }

    const state = searchCtx.state;
    // Phân trang (js/table-pagination.js, dùng chung toàn app) — vì hàm này DÙNG CHUNG cho mọi
    // bảng (Load Data, Ecuador, TGC, TGS, NL, HLSO...), chỉ cần thêm 1 chỗ này là có phân trang
    // cho TẤT CẢ các bảng đó cùng lúc. Mỗi bảng tự giữ số trang riêng trong state.page (state đã
    // là object RIÊNG của từng bảng, truyền vào qua searchCtx.state).
    if(!state.page) state.page = 1;
    const sortState = getColumnSort(searchCtx.tableId);
    const sortKeyStr = sortState ? sortState.idx + ':' + sortState.dir : '';
    const headerKey = (filteredMode ? '1' : '0') + '|' + sortKeyStr + '|' + columns.map(c=>c.label).join('||');
    if(headerKey !== state.lastHeaderKey){
      let html = `<table${filteredMode ? ' class="filtered-table"' : ''}><thead><tr>`;
      columns.forEach((c,i) => {
        const activeSort = sortState && sortState.idx === i ? (sortState.dir === 1 ? ' sort-asc' : ' sort-desc') : '';
        const hasFilter = (state.searchFilters[i] && state.searchFilters[i].trim() !== '') || (searchCtx.tableId && isColumnFilterActive(searchCtx.tableId, i));
        html += `<th class="sort-th${activeSort}${hasFilter ? ' has-filter' : ''}" data-col-index="${i}"><span class="th-label" title="Nhấp để sắp xếp">${escapeHtml(c.label)}</span></th>`;
      });
      html += '</tr><tr class="search-row">';
      columns.forEach((c,i) => {
        const val = state.searchFilters[i] || '';
        const listId = `${container.id}-dl-${i}`;
        // Lọc liên kết theo tầng: chỉ gợi ý giá trị còn khả dụng dựa trên các cột KHÁC đang lọc
        const cascadedForSuggest = applyColumnSearch(searchCtx.allRows, columns, state.searchFilters, searchCtx.tableId, i);
        const options = buildSuggestionsForColumnObj(cascadedForSuggest, c.key).map(v => `<option value="${escapeHtml(v)}">`).join('');
        const filterActive = searchCtx.tableId && isColumnFilterActive(searchCtx.tableId, i);
        html += `<th class="${val.trim() ? 'has-value' : ''}">` +
          `<div class="col-search-cell">` +
            `<input type="text" class="col-search-input" list="${listId}" data-col-index="${i}" placeholder="Tìm ${escapeHtml(c.label)}…" value="${escapeHtml(val)}">` +
            `<button type="button" class="col-filter-btn${filterActive ? ' active' : ''}" data-col-index="${i}" title="Lọc theo giá trị">▾</button>` +
          `</div>` +
          `<datalist id="${listId}">${options}</datalist>` +
          `</th>`;
      });
      html += `</tr></thead><tbody id="${container.id}-body"></tbody></table><div class="table-pagination" id="${container.id}-pagination"></div>`;
      container.innerHTML = html;
      state.lastHeaderKey = headerKey;

      if(!state.bound){
        container.addEventListener('input', (e)=>{
          const input = e.target;
          if(!input.classList || !input.classList.contains('col-search-input')) return;
          const idx = parseInt(input.getAttribute('data-col-index'), 10);
          const th = input.closest('th');
          if(th) th.classList.toggle('has-value', !!input.value.trim());
          const mainTh = container.querySelector(`thead tr:first-child th[data-col-index="${idx}"]`);
          if(mainTh) mainTh.classList.toggle('has-filter', !!input.value.trim() || (searchCtx.tableId && isColumnFilterActive(searchCtx.tableId, idx)));
          // Chờ ngừng gõ mới lọc + vẽ lại; gom theo từng cột (gõ cột A rồi sang cột B vẫn áp đủ cả 2).
          const pend = container.__klaPendingSearch || (container.__klaPendingSearch = {});
          pend[idx] = input.value;
          if(!container.__klaSearchRun){
            container.__klaSearchRun = KLAType.debounce(()=>{
              const todo = container.__klaPendingSearch; container.__klaPendingSearch = {};
              Object.keys(todo).forEach(k => searchCtx.onSearchInput(parseInt(k,10), todo[k]));
            }, 160);
          }
          container.__klaSearchRun();
        });
        container.addEventListener('click', (e)=>{
          // ĐỒNG BỘ (2026-09): chỉ bắt sự kiện sắp xếp khi bấm ĐÚNG phần chữ tiêu đề (.th-label),
    // không phải cả ô <th> — trước đây bấm bất kỳ đâu trong ô tiêu đề đều kích hoạt sắp xếp.
    const sortLabel = e.target.closest('th.sort-th .th-label');
    const sortTh = sortLabel ? sortLabel.closest('th.sort-th') : null;
          if(sortTh){
            const idx = parseInt(sortTh.getAttribute('data-col-index'), 10);
            toggleColumnSort(searchCtx.tableId, idx);
            state.lastHeaderKey = null; // buộc vẽ lại tiêu đề để cập nhật trạng thái sắp xếp
            state.page = 1; // đổi cách sắp xếp thì về lại trang 1
            searchCtx.onFilterApply();
            return;
          }
          const btn = e.target.closest('.col-filter-btn');
          if(!btn || !searchCtx.tableId) return;
          const idx = parseInt(btn.getAttribute('data-col-index'), 10);
          const colKey = columns[idx].key;
          // Lọc liên kết theo tầng: chỉ lấy giá trị của những dòng đã khớp các bộ lọc CỘT KHÁC
          // (không tính bộ lọc của chính cột "idx"), để danh sách chọn không hiện giá trị của toàn bộ dữ liệu.
          const cascadedRows = applyColumnSearch(searchCtx.allRows, columns, state.searchFilters, searchCtx.tableId, idx);
          const values = cascadedRows.map(r => objColValue(r, colKey));
          openColumnFilterDropdown(btn, searchCtx.tableId, idx, columns[idx].label, values, ()=>{
            state.lastHeaderKey = null; // buộc vẽ lại tiêu đề để cập nhật trạng thái "đang lọc" của nút
            state.page = 1; // đổi bộ lọc thì về lại trang 1
            searchCtx.onFilterApply();
          });
        });
        state.bound = true;
      }
      if(filteredMode && searchCtx.tableId && !state.resizeBound){
        enableCellResize(container, searchCtx.tableId);
        state.resizeBound = true;
      }
    }

    const tbody = document.getElementById(`${container.id}-body`);
    if(!tbody) return;
    // Chỉ vẽ TỐI ĐA TABLE_PAGE_SIZE dòng/trang — "Tổng"/thống kê ở nơi khác vẫn tính trên "rows"
    // ĐẦY ĐỦ (KHÔNG theo trang), chỉ phần vẽ ra bảng mới cắt theo trang.
    const totalRows = rows.length;
    const totalPages = Math.max(1, Math.ceil(totalRows / TABLE_PAGE_SIZE));
    if(state.page > totalPages) state.page = totalPages;
    if(state.page < 1) state.page = 1;
    const pageRows = rows.slice((state.page - 1) * TABLE_PAGE_SIZE, state.page * TABLE_PAGE_SIZE);
    if(pageRows.length){
      let body = '';
      pageRows.forEach((r, riOnPage)=>{
        const ri = (state.page - 1) * TABLE_PAGE_SIZE + riOnPage;
        // rowClassFn (nếu có, do bảng "Dữ Liệu" truyền vào) -> tô đỏ dòng đã trừ theo phiếu xuất.
        const rowClass = searchCtx.rowClassFn ? (searchCtx.rowClassFn(ri) || '') : '';
        body += `<tr${rowClass ? ` class="${rowClass}"` : ''}>`;
        columns.forEach((_, i)=> { const cv = String(r[i] ?? ''); body += `<td${looksNumericCell(cv) ? ' class="num"' : ''}>${escapeHtml(cv)}</td>`; });
        body += '</tr>';
      });
      tbody.innerHTML = body;
    } else {
      const hasActiveFilter = Object.values(state.searchFilters).some(v => v && v.trim() !== '');
      tbody.innerHTML = `<tr><td colspan="${columns.length}" style="text-align:center;color:var(--text-low);padding:24px;font-family:'JetBrains Mono',monospace;font-size:12px;">` +
        (hasActiveFilter ? 'Không có dòng nào khớp với tìm kiếm.' : 'Không có dữ liệu để hiển thị') +
        '</td></tr>';
    }
    renderPaginationControls(`${container.id}-pagination`, state.page, totalRows, (p)=>{
      state.page = p;
      renderGenericTable(container, columns, rows, filteredMode, searchCtx);
    });
    if(filteredMode && searchCtx.tableId){
      applyStoredSizes(container.querySelector('table'), searchCtx.tableId);
    }
  }

  function escapeHtml(str){
    return str.replace(/[&<>"']/g, m=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[m]));
  }

  // ---- Bộ lọc checkbox theo cột (dùng chung cho tất cả các bảng: Load data, Ecuador, TGC, TGS, NL, HLSO) ----
  // checkboxFilters: { "tableId:colIndex": Set(['giá trị đã chọn', ...]) }
  // Không có key nghĩa là đang chọn tất cả (không lọc). Set rỗng nghĩa là không dòng nào khớp.
  let checkboxFilters = {};

  function cfKey(tableId, colIndex){ return tableId + ':' + colIndex; }

  // ---- Sắp xếp theo cột: nhấp vào tiêu đề cột để sắp xếp A→Z, nhấp lại để Z→A, nhấp lần 3 để bỏ sắp xếp ----
  // columnSortState: { tableId: { idx: chỉ số cột đang sắp xếp, dir: 1 (A→Z) | -1 (Z→A) } }
  let columnSortState = {};

  function getColumnSort(tableId){ return columnSortState[tableId]; }

  function toggleColumnSort(tableId, colIndex){
    const cur = columnSortState[tableId];
    if(cur && cur.idx === colIndex){
      if(cur.dir === 1){ cur.dir = -1; }
      else { delete columnSortState[tableId]; } // nhấp lần 3 -> bỏ sắp xếp, về thứ tự gốc
    } else {
      columnSortState[tableId] = { idx: colIndex, dir: 1 };
    }
  }

  // So sánh 2 giá trị ô: nếu cả hai đều là số thì so sánh theo giá trị số, ngược lại so sánh theo chuỗi
  // (có hỗ trợ dấu tiếng Việt) để sắp xếp A→Z / Z→A đúng thứ tự chữ cái.
  function compareCellValues(a, b){
    const sa = String(a ?? '').trim();
    const sb = String(b ?? '').trim();
    const fa = parseFloat(sa.replace(/,/g,''));
    const fb = parseFloat(sb.replace(/,/g,''));
    const numA = sa !== '' && /^-?[\d.,]+$/.test(sa) && !isNaN(fa);
    const numB = sb !== '' && /^-?[\d.,]+$/.test(sb) && !isNaN(fb);
    if(numA && numB) return fa - fb;
    return sa.localeCompare(sb, 'vi', { numeric:true, sensitivity:'base' });
  }

  // Sắp xếp mảng "dòng dạng object" (r[col.key]) theo columnSortState[tableId], nếu có thiết lập sắp xếp
  function applySortObjRows(rows, columns, tableId){
    const s = columnSortState[tableId];
    if(!s) return rows;
    const key = columns[s.idx].key;
    return rows.slice().sort((r1, r2) => compareCellValues(objColValue(r1, key), objColValue(r2, key)) * s.dir);
  }

  // Sắp xếp mảng "dòng dạng mảng" (r[idx]) theo columnSortState[tableId], nếu có thiết lập sắp xếp
  function applySortArrayRows(rows, tableId){
    const s = columnSortState[tableId];
    if(!s) return rows;
    return rows.slice().sort((r1, r2) => compareCellValues(r1[s.idx], r2[s.idx]) * s.dir);
  }

  // ---- Nhấp vào ô để sao chép nội dung, không cần bôi đen (chọn chữ) thủ công ----
  // Dùng chung cho TẤT CẢ các bảng trong app: chỉ cần nhấp 1 lần vào ô là nội dung được copy
  // vào clipboard, có nháy màu nhẹ để xác nhận đã copy.
  let justResizedCell = false; // true ngay sau khi vừa kéo dãn cột/dòng, để không bị copy nhầm

  function copyCellText(text, cellEl){
    const value = (text ?? '').toString().trim();
    if(!value) return;
    const fallbackCopy = () => {
      const ta = document.createElement('textarea');
      ta.value = value;
      ta.style.position = 'fixed';
      ta.style.left = '-9999px';
      ta.style.top = '0';
      document.body.appendChild(ta);
      ta.focus();
      ta.select();
      try{ document.execCommand('copy'); }catch(e){}
      document.body.removeChild(ta);
    };
    if(navigator.clipboard && navigator.clipboard.writeText){
      navigator.clipboard.writeText(value).catch(fallbackCopy);
    } else {
      fallbackCopy();
    }
    if(cellEl){
      cellEl.classList.add('cell-copied');
      setTimeout(()=> cellEl.classList.remove('cell-copied'), 350);
    }
  }

  // Ủy quyền sự kiện trên toàn trang: hoạt động cho mọi bảng, kể cả bảng được vẽ lại nhiều lần.
  document.addEventListener('click', (e)=>{
    if(justResizedCell){ justResizedCell = false; return; } // vừa kéo dãn xong -> bỏ qua lần nhấp này
    if(e.target.closest('button, a, input, select, textarea, label')) return; // không copy khi bấm nút/ô nhập liệu bên trong ô
    const td = e.target.closest('td');
    if(!td) return;
    copyCellText(td.textContent, td);
  });

  function isColumnFilterActive(tableId, colIndex){
    return !!checkboxFilters[cfKey(tableId, colIndex)];
  }

  function resetTableCheckboxFilters(tableId){
    Object.keys(checkboxFilters).forEach(k=>{
      if(k.indexOf(tableId + ':') === 0) delete checkboxFilters[k];
    });
    delete columnSortState[tableId]; // nạp dữ liệu mới -> bỏ luôn sắp xếp cột cũ (nếu có)
  }

  // ĐỒNG BỘ (2026-09): bộ dựng popup lọc riêng của module này (panel .col-filter-dropdown/.cf-*,
  // 2 nút "Chọn tất cả"/"Bỏ chọn tất cả") đã được GỠ BỎ — dùng THẲNG popup dùng chung
  // js/table-col-search.js#openColSearchPopoverGeneric (class .col-search-popover, 1 dòng
  // checkbox "(Tất cả)") để giao diện lọc giống hệt mọi module khác trong app. checkboxFilters
  // vẫn giữ nguyên cấu trúc { "tableId:colIndex": Set(...) } như trước — truyền thẳng làm
  // filtersObj cho hàm dùng chung, không cần đổi gì ở isColumnFilterActive/resetTableCheckboxFilters
  // hay bất kỳ chỗ nào khác đang đọc checkboxFilters.
  function closeColumnFilterDropdown(){ closeColSearchPopover(); }

  // btnEl: nút ▾ vừa bấm | tableId/colIndex: định danh cột | label: tên cột hiển thị (đặt trong ô
  // tìm của popup) | rawValues: mảng giá trị thô của TOÀN BỘ dữ liệu cột đó | onApply: gọi lại mỗi
  // khi người dùng đổi lựa chọn, để bảng vẽ lại theo bộ lọc mới NGAY LẬP TỨC.
  function openColumnFilterDropdown(btnEl, tableId, colIndex, label, rawValues, onApply){
    openColSearchPopoverGeneric(btnEl, cfKey(tableId, colIndex), label, ()=>rawValues, checkboxFilters, onApply);
  }

  // ===== KHỞI TẠO: đọc database Excel thật (module2_tonkhogui.xlsx) khi mở trang =====
  // Toàn bộ 4 nguồn dữ liệu ở trên (persistedRows/pxPhieuList/dtManualRows/dtGroupMap) đã được
  // nạp NGAY LẬP TỨC từ localStorage lúc khai báo (để trang hiện dữ liệu tức thì, không phải
  // chờ IPC). Khối init() này chạy SAU CÙNG (đúng cơ chế 2 pha đã dùng ở Module 2/3): nếu đang
  // chạy trong Electron (window.dbAPI tồn tại) thì đọc lại 4 nguồn đó từ file Excel database
  // thật và GHI ĐÈ lên bản localStorage vừa hiện tạm, rồi vẽ lại toàn bộ các tab phụ thuộc — để
  // dữ liệu luôn khớp với file Excel trên đĩa (dùng chung được giữa nhiều máy/nhiều lần mở app),
  // đúng yêu cầu "tự động đọc/ghi vào đúng file database, giống cơ chế các module khác".
  // ===== NHẬN PHIẾU XUẤT TỪ ĐIỆN THOẠI (xem pullPhieuXuatVeLocal() trong main.js) =====
  // Module này giữ TOÀN BỘ danh sách phiếu + "Dữ Liệu" trong bộ nhớ và ghi đè cả sheet mỗi lần lưu,
  // nên khi đang mở thì main.js KHÔNG được tự ghi phiếu điện thoại vào Excel (sẽ bị ghi đè mất) —
  // thay vào đó main.js gửi phiếu sang đây, module tự nhập y như bấm "Tạo phiếu" trên PC (trừ Số
  // kiện + Trọng lượng, tô đỏ, lưu Excel) rồi báo lại. Đăng ký PHẢI chạy trước khi đọc database
  // (initDbHydration bên dưới) để main.js không còn ghi thẳng vào Excel trong lúc đang đọc.
  let pxHydrationDone = Promise.resolve();
  async function importPhonePhieuList(list){
    try{ await pxHydrationDone; }catch(e){ if(window.klLog) klLog('ton-kho-gui.js:6079',e); }
    const existingIds = new Set(pxPhieuList.map(p => String(p.id)));
    const importedIds = [];
    let newCount = 0;
    (list || []).forEach(ph=>{
      const id = String(ph.id || '');
      if(!id) return;
      if(existingIds.has(id)){ importedIds.push(id); return; } // đã nhập ở lần trước (VD mất mạng lúc đánh dấu)
      const idxMap = buildPersistedRowIndex();
      const items = (ph.items || []).map(it=>{
        const qty = toNumber(it.soLuongXuat);
        const r = pxPickRow(idxMap, it);
        const soKienGoc = r ? toNumber(r.soKien) : toNumber(it.soKien);
        const trongLuongGoc = r ? toNumber(r.trongLuong) : 0;
        return {
          maHang: String(it.maHang ?? ''), tenHang: String(it.tenHang ?? (r ? r.tenHang : '')),
          phieuNhap: String(it.phieuNhap ?? ''), soLo: String(it.soLo ?? ''), size: String(it.size ?? ''),
          soKien: soKienGoc, soLuongXuat: qty,
          trongLuongTru: (r && soKienGoc > 0) ? trongLuongGoc * (qty / soKienGoc) : 0
        };
      }).filter(it => it.soLuongXuat > 0);
      if(!items.length) return;
      let maPhieu = String(ph.maPhieu || '').trim() || id;
      const base = maPhieu; let n = 1;
      while(pxPhieuList.some(p => p.maPhieu === maPhieu)){ n++; maPhieu = base + '-' + n; }
      pxPhieuList.unshift({
        id, maPhieu, ngayTao: ph.ngayTao || new Date().toISOString(),
        nguon: 'Điện thoại', nguoiTao: String(ph.nguoiTao || ''), ghiChu: String(ph.ghiChu || ''),
        loadAt: pxLoadStamp(), loadDate: m2Meta.dataDate || '',
        items
      });
      applyPxItemsDelta(items, -1);
      existingIds.add(id);
      importedIds.push(id);
      newCount++;
    });
    if(newCount){
      recomputePxDaXuatFlags();
      const [okPx, okDl] = await Promise.all([savePxPhieuList(), savePersistedRows()]);
      refreshAllTabsAfterPxChange();
      renderPxList();
      if(!okPx || !okDl) return { ok:false, error:'Tồn kho gửi không lưu được database Excel (file đang mở trong Excel?).' };
    }
    return { ok:true, importedIds, newCount };
  }
  // Nút "🔄 Đồng bộ điện thoại" ở danh sách phiếu xuất — chỉ hiện khi đã cấu hình đồng bộ trong
  // Cài Đặt → Điện Thoại. Phiếu mới về được nhập qua đúng cơ chế importPhonePhieuList() ở trên
  // (main.js gửi sang), nên bảng tự cập nhật; ở đây chỉ cần báo kết quả.
  (function setupPhoneSyncButton(){
    const btn = document.getElementById('pxPhoneSyncBtn');
    const statusEl = document.getElementById('pxPhoneSyncStatus');
    const api = (typeof window !== 'undefined') ? window.sheetsSyncAPI : null;
    if(!btn || !api) return;
    const fmt = (iso)=>{ try{ return iso ? new Date(iso).toLocaleTimeString('vi-VN', { hour:'2-digit', minute:'2-digit' }) : ''; }catch(e){ return ''; } };
    function showStatus(cfg){
      if(!statusEl || !cfg) return;
      statusEl.textContent = cfg.lastPullAt ? `Lấy phiếu lần cuối ${fmt(cfg.lastPullAt)}${cfg.lastPullOk === false ? ' ⚠️' : ''}` : '';
    }
    async function refreshVisibility(){
      try{
        const res = await api.getConfig();
        const cfg = res && res.ok ? res.config : null;
        btn.hidden = !(cfg && cfg.webAppUrl && cfg.pcToken);
        showStatus(cfg);
      }catch(e){ btn.hidden = true; }
    }
    btn.addEventListener('click', async ()=>{
      btn.disabled = true;
      const old = btn.textContent;
      btn.textContent = 'Đang đồng bộ…';
      try{
        // Chỉ lấy phiếu điện thoại + đẩy lại bảng Tồn kho gửi (M02) — không đụng bảng của module khác.
        const res = await api.syncNow({ pull: true, tables: ['M02'] });
        showStatus(res && res.config);
        if(res && res.ok){
          btn.textContent = res.soPhieuMoi ? `✓ Nhận ${res.soPhieuMoi} phiếu mới` : '✓ Không có phiếu mới';
        }else{
          btn.textContent = '⚠️ Lỗi đồng bộ';
          const errs = [res && res.pullError && ('Lấy phiếu: ' + res.pullError), res && res.pushError && ('Đẩy tồn: ' + res.pushError), res && res.error].filter(Boolean);
          alert('Đồng bộ điện thoại chưa trọn vẹn:\n' + (errs.join('\n') || 'không rõ lỗi'));
        }
      }catch(e){
        btn.textContent = '⚠️ Lỗi đồng bộ';
        console.error('Lỗi đồng bộ điện thoại:', e);
      }finally{
        setTimeout(()=>{ btn.textContent = old; btn.disabled = false; }, 2500);
      }
    });
    refreshVisibility();
    // Cấu hình có thể vừa được nhập ở Cài Đặt (iframe khác) — kiểm tra lại mỗi khi quay lại module.
    document.addEventListener('visibilitychange', ()=>{ if(!document.hidden) refreshVisibility(); });
    setInterval(refreshVisibility, 60000);
  })();

  if(typeof window !== 'undefined' && window.phoneImportBridge){
    window.phoneImportBridge.onImportRequest(async (payload)=>{
      if(payload && payload.kind === 'baocao'){
        let snap;
        try{ snap = await buildM2PhoneReportSnapshot(); }
        catch(e){ console.error('Lỗi dựng báo cáo điện thoại (Module 02):', e); snap = { ok:false, error:String(e && e.message || e) }; }
        try{ await window.phoneImportBridge.sendResult(payload.reqId, snap); }catch(e){ if(window.klLog) klLog('ton-kho-gui.js:6179',e); }
        return;
      }
      let result;
      try{ result = await importPhonePhieuList(payload && payload.list); }
      catch(e){ console.error('Lỗi nhập phiếu xuất từ điện thoại:', e); result = { ok:false, error: String(e && e.message || e) }; }
      try{ await window.phoneImportBridge.sendResult(payload && payload.reqId, result); }catch(e){ if(window.klLog) klLog('ton-kho-gui.js:6185',e); }
    });
    window.phoneImportBridge.register().catch(()=>{});
  }

  pxHydrationDone = (async function initDbHydration(){
    if(!hasDbAPI()){ pxMigrateLegacyBatch(); return; } // mở trực tiếp bằng trình duyệt (không qua Electron) -> giữ nguyên localStorage
    const [chinhOk, metaOk, pxOk, dtManualOk, dtGroupOk, dtSeenOk, dtLoMapOk] = await Promise.all([
      hydratePersistedRowsFromDb(),
      hydrateDuLieuMetaFromDb().then(ok => { renderM2DataDate(); return ok; }),
      hydratePxPhieuListFromDb(),
      hydrateDtManualRowsFromDb(),
      hydrateDtGroupMapFromDb(),
      hydrateDtSeenValuesFromDb(),
      hydrateDtLoMapFromDb(),
      restoreCustomFilters(),
    ]);
    // 1.15: database CHƯA có dữ liệu đặc tính nào (máy mới) → nạp "Đặc tính gốc" (js/dactinh-goc-m2.js) đúng 1 lần rồi ghi vào database.
    // Cờ localStorage giữ để sau này người dùng chủ động xoá hết thì KHÔNG tự nạp lại.
    let dtSeeded = false;
    try{
      const G = window.KL_DACTINH_GOC_M2, FLAG = 'dataConsole_dacTinhGoc_done';
      const empty = !dtManualRows.length && !Object.keys(dtGroupMap).length && !dtSeenValues.size && !Object.keys(dtLoMap).length;
      if(G && empty && localStorage.getItem(FLAG) !== '1'){
        dtManualRows = (G.nhapTay || []).map(r => ({ maHang: r[0], dacTinh: r[1] })).filter(r => r.dacTinh);
        dtGroupMap = {}; (G.nhomGop || []).forEach(r => { if(r[0]) dtGroupMap[r[0]] = r[1]; });
        dtSeenValues = new Set((G.daGhiNhan || []).map(r => r[0]).filter(Boolean));
        dtLoMap = {}; (G.chuyenLo || []).forEach(r => { if(r[0] && r[1]) dtLoMap[loKey(r[0])] = { lo: r[0], dacTinh: r[1] }; });
        localStorage.setItem(FLAG, '1'); dtSeeded = true;
        await Promise.all([saveDtManualRows(), saveDtGroupMap(), saveDtSeenValues(), saveDtLoMap()]);
      }
    }catch(e){ console.error('Lỗi nạp đặc tính gốc (Tồn kho gửi):', e); }
    pxMigrateLegacyBatch(); // PC 1.69: gắn đợt số liệu cho phiếu cũ chưa có
    await hydrateLoadedFileFromDb(); // PC 8.52: mở lại file đã nạp lần trước
    if(pxOk) recomputePxPhieuCounter();
    if(chinhOk || metaOk || pxOk || dtManualOk || dtGroupOk || dtSeenOk || dtLoMapOk || dtSeeded){
      updateDtLoMapBtn();
      refreshDuLieuTab();
      refreshPhieuXuatTab();
      refreshEcuadorTab();
      refreshAnDoTab();
      refreshTGCTab();
      refreshTGSTab();
      refreshNguyenLieuTab();
      refreshHLSOTab();
      renderDtManualPreview();
      refreshDacTinhTab();
      refreshBaoCaoTab();
      refreshDoiSoatTab();
    } else {
      // Dù 5 nguồn kia không đổi, điều kiện lọc tự thêm vẫn có thể vừa được đọc lại từ Excel
      // (khác với bản localStorage tạm) — vẽ lại riêng 6 tab Phân Loại để áp dụng đúng.
      refreshEcuadorTab();
      refreshAnDoTab();
      refreshTGCTab();
      refreshTGSTab();
      refreshNguyenLieuTab();
      refreshHLSOTab();
    }
  })();

/* ============================================================
   THU GỌN / MỞ RỘNG SIDEBAR — để mở rộng không gian làm việc cho các bảng nhiều cột. Trạng
   thái lưu vào localStorage, giữ nguyên giữa các lần mở lại module. Đồng bộ hành vi với module
   "NXT Bột/Sốt" / "Tồn Kho An An". */
(function initSidebarToggle(){
  var SIDEBAR_COLLAPSE_KEY = 'btkg:sidebar_collapsed';
  var sidebarEl = document.getElementById('sidebar');
  var btn = document.getElementById('sidebarToggleBtn');
  if(!sidebarEl || !btn) return;
  function apply(collapsed){
    sidebarEl.classList.toggle('collapsed', collapsed);
    btn.title = collapsed ? 'Mở rộng menu' : 'Thu gọn menu';
    btn.setAttribute('aria-label', btn.title);
  }
  var saved = false;
  try{ saved = localStorage.getItem(SIDEBAR_COLLAPSE_KEY) === '1'; }catch(e){ /* bỏ qua lỗi đọc */ }
  apply(saved);
  btn.addEventListener('click', function(){
    var collapsed = !sidebarEl.classList.contains('collapsed');
    apply(collapsed);
    try{ localStorage.setItem(SIDEBAR_COLLAPSE_KEY, collapsed ? '1' : '0'); }catch(e){ /* bỏ qua lỗi lưu */ }
  });
})();

/* ============================================================
   BÔI ĐEN Ô ĐỂ TÍNH TỔNG KIỂU EXCEL (áp dụng cho MỌI bảng trong module) — viết độc lập, KHÔNG
   phụ thuộc biến/hàm nội bộ ở trên (module này không có 1 hàm "render tất cả" duy nhất mà có
   rất nhiều hàm refresh riêng cho từng tab, nên dùng MutationObserver để tự phát hiện bảng mới/
   thay đổi thay vì phải sửa từng hàm refresh). Kéo chuột từ 1 ô sang các ô khác trong CÙNG 1
   bảng để chọn vùng chữ nhật; thanh trạng thái cố định dưới màn hình (#excelStatusBar) hiện
   Tổng/Trung bình/Nhỏ nhất/Lớn nhất của các ô có giá trị số trong vùng đã chọn.
   ============================================================ */
// (Bôi đen ô kiểu Excel: đã chuyển sang file dùng chung js/excel-select.js — PC 3.0)
