// ================= LỌC THEO CỘT KIỂU EXCEL, DÙNG CHUNG (icon kính lúp trên tiêu đề cột) =================
// Trước đây 5 module (Tồn Kho An An, NXT Bột/Sốt, NXT-TNK/TGC, Đối Chiếu Kho Hàng, Chứng Từ Xuất
// Kho) đều tự viết lại gần như Y HỆT bộ hàm này (ensureColSearchPopoverEl/closeColSearchPopover/
// colFilterActive/withScrollCloseSuppressed/openColSearchPopoverGeneric) — bốn trong năm module
// giống nhau tới từng dòng, chỉ riêng NXT Bột/Sốt có thêm 2 điểm khác: (1) tách rows/colValueFn
// riêng thay vì nhận thẳng mảng giá trị, để tự lọc-liên-kết-theo-tầng (cascading) NGAY TRONG hàm;
// (2) khi ghi nhận bộ lọc, tô đỏ TẤT CẢ <th data-col-key> khớp (không chỉ ô đầu tiên tìm thấy) —
// dùng cho các trang có 2 bảng dùng chung 1 bộ lọc. Bản dùng chung này giữ lại CẢ hai cải tiến đó
// (xem cascadedColValues() bên dưới cho phần lọc-liên-kết-theo-tầng), nên không module nào bị mất
// tính năng khi chuyển sang dùng chung.
//
// CÁCH DÙNG trong 1 module:
//   openColSearchPopoverGeneric(btn, key, label, getValues, filtersObj, onApply)
//     - getValues: () => mảng giá trị THÔ (chưa dedupe/sort) của cột đang lọc.
//     - filtersObj: object riêng của bảng đó, dùng làm nơi lưu Set giá trị đang chọn theo từng
//       "key" (mỗi bảng/module tự giữ 1 filtersObj riêng, không dùng chung giữa các bảng khác
//       nhau, để lọc bảng này không ảnh hưởng bảng khác).
//     - onApply: gọi lại mỗi khi người dùng đổi lựa chọn, để bảng vẽ lại theo bộ lọc mới.
//   Nếu cần lọc-liên-kết-theo-tầng (giá trị gợi ý chỉ còn những gì còn khớp CÁC CỘT KHÁC đang
//   lọc, giống module NXT Bột/Sốt) — dùng cascadedColValues(getRows, colValueFn, key, filtersObj)
//   làm getValues, xem ví dụ trong nxt-bot-sot.js.
let suppressScrollClose = false;
function withScrollCloseSuppressed(fn){
  suppressScrollClose = true;
  fn();
  requestAnimationFrame(()=>{
    requestAnimationFrame(()=>{ suppressScrollClose = false; });
  });
}
function ensureColSearchPopoverEl(){
  let el = document.getElementById('colSearchPopover');
  if(!el){
    el = document.createElement('div');
    el.id = 'colSearchPopover';
    el.className = 'col-search-popover';
    document.body.appendChild(el);
  }
  return el;
}
function closeColSearchPopover(){
  const el = document.getElementById('colSearchPopover');
  if(el) el.classList.remove('open');
}
function colFilterActive(v){ return v instanceof Set; }

// Tìm phần tử CUỘN được gần nhất bao quanh "el" (đi ngược lên DOM tới khi gặp phần tử có
// overflow-y:auto/scroll VÀ thật sự đang cuộn được — scrollHeight > clientHeight). Dùng để giữ
// nguyên vị trí cuộn khi lọc khiến số dòng hiển thị đổi đột ngột (xem preserveScrollAround bên
// dưới) — mỗi module có thể cuộn ở cấp khác nhau (trang .main, hoặc 1 khung .table-wrap/
// .table-scroll riêng có chiều cao cố định), nên dò theo DOM thật thay vì đoán trước 1 selector
// cố định, để dùng đúng cho mọi module mà không cần biết trước cấu trúc của module đó.
function findScrollableAncestor(el){
  let node = el;
  while(node && node !== document.body && node !== document.documentElement){
    const style = getComputedStyle(node);
    if((style.overflowY === 'auto' || style.overflowY === 'scroll') && node.scrollHeight > node.clientHeight){
      return node;
    }
    node = node.parentElement;
  }
  return document.scrollingElement || document.documentElement;
}
// Giữ nguyên vị trí cuộn trong lúc chạy "fn" (thường là re-render bảng sau khi đổi bộ lọc) —
// tránh cảm giác "giật" do số dòng hiển thị đổi đột ngột làm chiều cao trang/khung thay đổi,
// khiến trình duyệt tự kéo lại vị trí cuộn.
// LƯU Ý QUAN TRỌNG: 1 vài module (ví dụ "Tồn Kho An An") vẽ lại re-render KIỂU THAY THẾ TOÀN BỘ
// nội dung (document.getElementById('content').innerHTML = ...), nghĩa là khung cuộn riêng của
// bảng (.table-scroll) bị HỦY và tạo mới hoàn toàn mỗi lần — nếu chỉ giữ lại đúng 1 tham chiếu
// phần tử tìm được TRƯỚC khi render, tham chiếu đó sẽ thành "zombie" (không còn gắn vào trang)
// ngay sau khi render xong, khiến việc gán lại scrollTop không có tác dụng gì (bug thật đã gặp).
// Để chắc chắn luôn có tác dụng dù module render kiểu nào, hàm này giữ lại CẢ HAI:
// (1) khung .main (khung ngoài cùng, KHÔNG BAO GIỜ bị thay thế bởi bất kỳ module nào, chỉ có
//     phần con bên trong nó bị vẽ lại) — luôn khôi phục được, xử lý đúng trường hợp cả trang bị
//     đẩy lên/xuống do đổi chiều cao nội dung;
// (2) khung cuộn gần nhất quanh nút bấm — chỉ khôi phục NẾU nó còn thật sự gắn trong trang sau
//     khi render xong (document.contains(...)) — bỏ qua an toàn nếu đã bị hủy, không báo lỗi gì.
function preserveScrollAround(anchorEl, fn){
  const mainEl = document.querySelector('main');
  const mainScrollTop = mainEl ? mainEl.scrollTop : null;
  const nestedEl = findScrollableAncestor(anchorEl);
  const nestedScrollTop = nestedEl.scrollTop;
  fn();
  if(mainEl && document.contains(mainEl)) mainEl.scrollTop = mainScrollTop;
  if(nestedEl !== mainEl && document.contains(nestedEl)) nestedEl.scrollTop = nestedScrollTop;
}

function colSearchEscapeHtml(s){
  return String(s??'').replace(/[&<>"']/g, c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
}

// Lọc liên kết theo tầng (cascading filter): trước khi lấy danh sách giá trị để hiển thị trong
// dropdown của 1 cột, loại bỏ khỏi "rows" những dòng không khớp bộ lọc của các CỘT KHÁC đang bật
// (bỏ qua bộ lọc của chính cột "key" đang mở) — nhờ vậy danh sách chọn chỉ còn giá trị còn khả
// dụng, không hiện giá trị đã không còn dòng nào khớp. Dùng làm getValues khi cần tính năng này
// (xem chú thích đầu file).
function cascadedColValues(getRows, colValueFn, key, filtersObj){
  const otherKeys = Object.keys(filtersObj).filter(k => k !== String(key) && colFilterActive(filtersObj[k]));
  const rows = otherKeys.length
    ? getRows().filter(r => otherKeys.every(k => filtersObj[k].has(colValueFn(r,k))))
    : getRows();
  return rows.map(r=>colValueFn(r,key));
}

// PC 1.18 — HỘP LỌC CHUẨN DÙNG CHUNG TOÀN APP (chuẩn M04, gộp thêm các điểm hay của hộp lọc Số liệu cuối kỳ /
// Nhập liệu kiểm kê để KHÔNG module nào mất tính năng khi chuyển sang dùng chung):
//   • Đếm số dòng của từng giá trị (getValues trả mảng THÔ, có lặp → đếm được).
//   • Sắp xếp tự nhận số (1, 2, 10 thay vì 1, 10, 2) + nút A→Z / Z→A trong hộp.
//   • (Tùy chọn) giá trị "(Trống)" — opts.blank = true.
//   • Gõ tìm = chọn ĐÚNG các giá trị khớp (kiểu Excel); khi cột đang lọc sẵn có thêm ô "Thêm vào bộ lọc hiện tại".
//   • "(Tất cả)" áp cho các giá trị ĐANG HIỆN theo ô tìm.
//   • Lọc NGAY khi tick / gõ (không có nút Áp dụng) — nút chỉ để đóng.
//   • Hộp tự lật lên trên nút khi thiếu chỗ phía dưới; danh sách dài chỉ vẽ tối đa 500 giá trị (gõ để tìm thêm).
// opts (tham số thứ 7, không bắt buộc): { blank:false, labelOf:(v)=>chữ hiển thị }
const COL_SEARCH_MAX_ITEMS = 500;
function colSearchIsNum(s){ if(window.KLNum){ const n = window.KLNum.parse(s, { unit:true }); if(n != null && !isNaN(n)) return true; } return /^[+-]?\d[\d,]*(\.\d+)?$/.test(s) || /^[+-]?\d[\d.]*(,\d+)?$/.test(s); }
function colSearchNumOf(s){
  if(window.KLNum){ const n = window.KLNum.parse(s, { unit:true }); return n == null ? NaN : n; } // PC 2.32: quy chuẩn số chung
  // "1,500.50" (kiểu app) hoặc "1.500,50" (kiểu VN) → số
  if(/,\d{1,2}$/.test(s) && !/\.\d+$/.test(s)) return parseFloat(s.replace(/\./g,'').replace(',','.'));
  return parseFloat(s.replace(/,/g,''));
}
function colSearchCompare(a, b){
  if(a === '' || b === '') return a === b ? 0 : (a === '' ? 1 : -1); // (Trống) luôn cuối
  if(colSearchIsNum(a) && colSearchIsNum(b)){
    const d = colSearchNumOf(a) - colSearchNumOf(b);
    if(!isNaN(d) && d !== 0) return d;
  }
  return a.localeCompare(b, 'vi', { numeric:true, sensitivity:'base' });
}
function openColSearchPopoverGeneric(btn, key, label, getValues, filtersObj, onApply, opts){
  opts = opts || {};
  const el = ensureColSearchPopoverEl();
  const width = 260;
  const labelOf = (typeof opts.labelOf === 'function') ? opts.labelOf : null;
  const showOf = (v) => v === '' ? '(Trống)' : (labelOf ? String(labelOf(v)) : v);

  // Đếm số dòng theo giá trị (mảng THÔ có lặp)
  const counts = new Map();
  (getValues() || []).forEach(v => {
    const s = String(v ?? '').trim();
    if(s === '' && !opts.blank) return;
    counts.set(s, (counts.get(s) || 0) + 1);
  });
  let values = Array.from(counts.keys()).sort(colSearchCompare);
  let dir = 1;

  el.innerHTML = `
    <div class="col-search-head"><b>${colSearchEscapeHtml(label||'')}</b><span class="col-search-sort"><button type="button" data-col-sort="1" title="Sắp A→Z / nhỏ→lớn">A→Z</button><button type="button" data-col-sort="-1" title="Sắp Z→A / lớn→nhỏ">Z→A</button></span></div>
    <input type="text" class="col-search-input" placeholder="Tìm ${colSearchEscapeHtml(String(label||'').toLowerCase())}..." autocomplete="off">
    <label class="col-search-selectall"><input type="checkbox" class="col-cb" data-col-cb-all><b>(Tất cả)</b></label>
    <label class="col-search-selectall col-search-add" hidden><input type="checkbox" class="col-cb" data-col-cb-add><span>Thêm vào bộ lọc hiện tại</span></label>
    <div class="col-search-list"></div>
    <div class="col-search-note"></div>
    <div class="col-search-footer">
      <span class="col-search-clear" data-col-clear>Xóa lọc</span>
      <span class="col-search-close" data-col-close>Đóng</span>
    </div>
  `;
  const inputEl = el.querySelector('.col-search-input');
  const listEl = el.querySelector('.col-search-list');
  const noteEl = el.querySelector('.col-search-note');
  const allCb = el.querySelector('[data-col-cb-all]');
  const addRow = el.querySelector('.col-search-add');
  const addCb = el.querySelector('[data-col-cb-add]');

  // Chỉ giữ giá trị đã chọn CÒN có trong danh sách hiện tại (xem chú thích PC 2.4 cũ): không còn giá trị nào → bỏ lọc cột.
  let selected;
  if(filtersObj[key] instanceof Set){
    selected = new Set(values.filter(v => filtersObj[key].has(v)));
    if(!selected.size){ selected = new Set(values); delete filtersObj[key]; }
    else if(selected.size !== filtersObj[key].size){
      if(selected.size === values.length) delete filtersObj[key];
      else filtersObj[key] = new Set(selected);
    }
  } else {
    selected = new Set(values);
  }
  const wasFiltered = filtersObj[key] instanceof Set;
  const before = new Set(selected); // lựa chọn lúc mở hộp (dùng cho "Thêm vào bộ lọc hiện tại" / xóa trắng ô tìm)

  const markHeader = (active)=>{
    btn.classList.toggle('active', active);
    const th = btn.closest('th');
    if(th) th.classList.toggle('has-filter', active);
    document.querySelectorAll(`th[data-col-key="${String(key).replace(/"/g,'')}"]`).forEach(otherTh=>{
      otherTh.classList.toggle('has-filter', active);
    });
  };
  const commit = ()=>{
    if(selected.size===values.length) delete filtersObj[key];
    else filtersObj[key] = new Set(selected);
    withScrollCloseSuppressed(()=>preserveScrollAround(btn, onApply));
    markHeader(colFilterActive(filtersObj[key]));
    updateNote();
  };
  const visibleValues = ()=>{
    const q = inputEl.value.trim().toLowerCase();
    return q ? values.filter(v=>showOf(v).toLowerCase().includes(q)) : values;
  };
  const updateSelectAllState = ()=>{
    const vis = visibleValues();
    const n = vis.filter(v=>selected.has(v)).length;
    allCb.checked = vis.length>0 && n===vis.length;
    allCb.indeterminate = n>0 && n<vis.length;
  };
  function updateNote(){
    const vis = visibleValues();
    noteEl.textContent = !selected.size ? 'Chưa chọn giá trị nào — bảng đang trống.'
      : (vis.length > COL_SEARCH_MAX_ITEMS ? `Hiển thị ${COL_SEARCH_MAX_ITEMS}/${vis.length.toLocaleString('en-US')} giá trị — gõ để tìm thêm` : '');
  }
  const renderList = ()=>{
    const vis = visibleValues().slice(0, COL_SEARCH_MAX_ITEMS);
    listEl.innerHTML = vis.length
      ? vis.map(v=>`<label class="col-search-item checkbox-item"><input type="checkbox" class="col-cb" data-col-val="${colSearchEscapeHtml(v)}" ${selected.has(v)?'checked':''}><span>${colSearchEscapeHtml(showOf(v))}</span><em class="col-search-count">${(counts.get(v)||0).toLocaleString('en-US')}</em></label>`).join('')
      : `<div class="col-search-empty">Không có dữ liệu phù hợp.</div>`;
    updateSelectAllState();
    updateNote();
  };
  renderList();

  // Gõ tìm (trì hoãn 120 ms): chọn ĐÚNG các giá trị khớp; tick "Thêm vào bộ lọc hiện tại" → gộp với lựa chọn lúc mở.
  let searchDebounceTimer = null;
  const applySearch = ()=>{
    const q = inputEl.value.trim().toLowerCase();
    addRow.hidden = !q || !wasFiltered;
    if(!q){ selected = wasFiltered ? new Set(before) : new Set(values); }
    else {
      const m = values.filter(v=>showOf(v).toLowerCase().includes(q));
      selected = new Set(addCb.checked && wasFiltered ? [...before, ...m] : m);
    }
    commit();
    renderList();
  };
  inputEl.addEventListener('input', ()=>{
    clearTimeout(searchDebounceTimer);
    searchDebounceTimer = setTimeout(applySearch, 120);
  });
  addCb.addEventListener('change', applySearch);
  inputEl.addEventListener('keydown', e=>{
    if(e.key==='Enter'){ e.preventDefault(); clearTimeout(searchDebounceTimer); if(inputEl.value.trim()) applySearch(); closeColSearchPopover(); }
    else if(e.key==='Escape'){ e.preventDefault(); closeColSearchPopover(); }
  });
  listEl.addEventListener('change', e=>{
    const cb = e.target.closest('[data-col-val]');
    if(!cb) return;
    if(cb.checked) selected.add(cb.dataset.colVal); else selected.delete(cb.dataset.colVal);
    commit();
    updateSelectAllState();
  });
  allCb.addEventListener('change', ()=>{
    const vis = visibleValues();
    vis.forEach(v=>{ if(allCb.checked) selected.add(v); else selected.delete(v); });
    commit();
    listEl.querySelectorAll('.col-cb').forEach(cb=>{ cb.checked = allCb.checked; });
    updateSelectAllState();
  });
  el.querySelector('.col-search-sort').addEventListener('click', e=>{
    const b = e.target.closest('[data-col-sort]'); if(!b) return;
    dir = Number(b.dataset.colSort) || 1;
    values = Array.from(counts.keys()).sort((a,b2)=>dir*colSearchCompare(a,b2));
    if(dir < 0){ // (Trống) vẫn để cuối
      const iBlank = values.indexOf(''); if(iBlank >= 0){ values.splice(iBlank,1); values.push(''); }
    }
    renderList();
  });
  el.querySelector('[data-col-clear]').addEventListener('click', ()=>{
    inputEl.value = '';
    addRow.hidden = true;
    selected = new Set(values);
    delete filtersObj[key];
    withScrollCloseSuppressed(()=>preserveScrollAround(btn, onApply));
    markHeader(false);
    renderList();
  });
  el.querySelector('[data-col-close]').addEventListener('click', closeColSearchPopover);

  // Đặt hộp ngay dưới nút; thiếu chỗ phía dưới thì lật lên trên nút.
  const rect = btn.getBoundingClientRect();
  el.style.left = Math.max(8, Math.min(window.innerWidth-width-8, Math.round(rect.left))) + 'px';
  el.style.top = Math.round(rect.bottom + 6) + 'px';
  el.style.bottom = 'auto';
  el.classList.add('open');
  const h = el.offsetHeight, below = window.innerHeight - rect.bottom - 12, above = rect.top - 12;
  if(h > below && above > below){
    el.style.top = 'auto';
    el.style.bottom = Math.round(window.innerHeight - rect.top + 6) + 'px';
  }
  inputEl.focus();
}

// Đóng popup khi bấm ra ngoài / cuộn trang / đổi kích thước cửa sổ — gắn Ở CẤP TOÀN TRANG, ĐÚNG
// 1 LẦN lúc file này được nạp (không phải mỗi lần vẽ bảng), nên không cần thêm cờ "đã gắn hay
// chưa" như 1 vài module từng phải tự làm trước đây.
document.addEventListener('click', e=>{
  const el = document.getElementById('colSearchPopover');
  if(!el || !el.classList.contains('open')) return;
  // Danh sách class/thuộc tính nhận diện "nút bấm mở popup lọc" của TỪNG module — click vào
  // đúng nút vừa mở popup không được coi là "click ra ngoài" (nếu không, popup sẽ tự đóng NGAY
  // sau khi vừa mở, do cùng 1 sự kiện click vừa mở popup lại tiếp tục nổi bọt lên tới document).
  // ĐỒNG BỘ (2026-09): thêm '.col-filter-btn' — nút lọc của module "Tồn Kho Gửi" sau khi
  // chuyển sang dùng popup lọc chung (trước đó module này tự đóng/mở popup riêng nên không cần
  // khai báo ở đây).
  if(el.contains(e.target) || e.target.closest('[data-filter-col]') || e.target.closest('[data-col-search]') || e.target.closest('.th-filter-btn') || e.target.closest('.col-filter-btn') || e.target.closest('.filter-btn')) return;
  el.classList.remove('open');
});
document.addEventListener('scroll', e=>{
  if(suppressScrollClose) return;
  const el = document.getElementById('colSearchPopover');
  if(el && el.contains(e.target)) return;
  closeColSearchPopover();
}, true);
window.addEventListener('resize', ()=>closeColSearchPopover());
