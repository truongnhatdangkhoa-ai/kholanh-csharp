/* ============================================================
   CỐ ĐỊNH (position:sticky) HÀNG "TÌM THEO CỘT" khi cuộn dọc bảng — dùng CHUNG cho mọi module.

   Mỗi bảng có 2 hàng đầu trong <thead>: hàng tiêu đề (đã sticky ở top:0 từ trước) và hàng thứ 2
   chứa ô tìm/lọc theo từng cột (class "col-search-row" ở hầu hết module, hoặc "filter-row" ở
   module "Số Liệu Kho/Kế Toán"). Hàng thứ 2 cần "top" = ĐÚNG chiều cao hàng tiêu đề để dán liền
   ngay bên dưới, nhưng chiều cao đó khác nhau tuỳ module (font/khoảng đệm tiêu đề khác nhau) và
   có thể đổi theo cỡ chữ trình duyệt/hệ điều hành — nên KHÔNG đoán cứng 1 số px trong CSS, mà đo
   THẬT chiều cao đã render rồi gán vào biến CSS --thead-row-h ngay trên <table> (kế thừa xuống
   các ô của hàng tìm-theo-cột, nơi CSS dùng "top: var(--thead-row-h, 42px)").

   File này được nạp ở MỌI trang có bảng dùng 1 trong 2 class trên — tự quét toàn trang, không
   cần gọi tay ở từng nơi vẽ bảng.
   ============================================================ */
(function(){
  const SEARCH_ROW_SELECTOR = 'thead tr.col-search-row, thead tr.filter-row, thead tr.search-row';

  function syncStickyOffset(table){
    const theadRow = table.querySelector('thead tr:first-child');
    const searchRow = table.querySelector(SEARCH_ROW_SELECTOR);
    if(!theadRow || !searchRow) return;
    const h = theadRow.getBoundingClientRect().height;
    if(h) table.style.setProperty('--thead-row-h', h + 'px');
  }

  function watchTable(table){
    if(table.dataset.stickySearchWatched) return;
    if(!table.querySelector(SEARCH_ROW_SELECTOR)) return; // bảng không có hàng tìm-theo-cột — bỏ qua
    table.dataset.stickySearchWatched = '1';
    syncStickyOffset(table);
    const theadRow = table.querySelector('thead tr:first-child');
    if(theadRow && window.ResizeObserver){
      // Theo dõi LIÊN TỤC (không chỉ đo 1 lần lúc vẽ) — chiều cao hàng tiêu đề có thể đổi khi
      // co giãn cột (bọc dòng dài xuống 2 dòng), đổi cỡ chữ hệ điều hành, hoặc đổi theme.
      new ResizeObserver(()=>syncStickyOffset(table)).observe(theadRow);
    }
  }

  function scan(){
    document.querySelectorAll('table').forEach(watchTable);
  }

  let scheduled = false;
  function scheduleScan(){
    if(scheduled) return;
    scheduled = true;
    requestAnimationFrame(()=>{ scheduled = false; scan(); });
  }

  const target = document.querySelector('main') || document.body;
  const observer = new MutationObserver(scheduleScan);
  observer.observe(target, { childList: true, subtree: true });
  scan();
})();
