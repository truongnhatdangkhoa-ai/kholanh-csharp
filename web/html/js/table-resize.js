// ================= CO DÃN CỘT / DÒNG KIỂU EXCEL (áp dụng cho MỌI bảng .table-wrap) =================
// Viết độc lập, không đụng tới logic bôi đen ô tính tổng (initExcelLikeSelection) đã có sẵn
// trong chung-tu-xuat-kho.js — chỉ thêm 2 tay kéo nhỏ ở mép phải mỗi <th> và mép dưới mỗi <tr>.
//
// SỬA LỖI "kéo hẹp cột không được, như bị cố định tối thiểu": TRƯỚC ĐÂY dùng chung 1 mức tối
// thiểu cứng MIN_COL=40 cho MỌI cột, nhưng CSS nền của các bảng (tbody td) lại đặt sẵn
// "min-width" riêng + "white-space:nowrap" KHÔNG cho chữ rút gọn "..." — nên dù JS cho phép kéo
// xuống 40px, ô vẫn bị chính CSS đó ép rộng ra lại để hiện đủ chữ không xuống dòng, tạo cảm giác
// "cố định, không kéo hẹp được". Nay đo ĐỘ RỘNG TỰ NHIÊN thật của từng cột (giống hệt cách module
// "NXT Bột/Sốt" — nxt-bot-sot.js/initColResize — đã làm): cột thuần văn bản được phép co nhỏ tới
// mức rất hẹp (phần chữ dư tự rút gọn "..." — xem CSS text-overflow:ellipsis đã thêm ở tbody td),
// còn cột có chứa ô nhập/nút (input/button/select) thì không cho co hẹp hơn kích thước THẬT của
// control đó, tránh kéo che mất nút bấm hoặc cắt xén ô nhập. Thêm nhấp đúp vào tay kéo để tự bung
// cột vừa khít nội dung, giống Excel.
(function(){
  "use strict";
  var MIN_COL_FALLBACK = 24, MIN_ROW = 22;
  var DRAG = null; // {type:'col'|'row', el, startX/startY, startW/startH}

  function ensurePositioned(el){
    if(getComputedStyle(el).position === "static"){ el.style.position = "relative"; }
  }

  // SỬA LỖI "kéo hẹp/rộng cột trái phải không ăn, như bị khoá cứng": với table-layout:fixed,
  // độ rộng THẬT của <table> phải là 1 con số px CỐ ĐỊNH đúng bằng tổng độ rộng mọi cột (ở đây
  // là mọi <th> dòng tiêu đề đầu tiên, vì bảng này không dùng <colgroup>) thì trình duyệt mới
  // tôn trọng đúng từng cột đã đặt. Trước đây dùng table.style.width = "auto" — với auto, Chrome
  // tự tính lại độ rộng dựa trên NỘI DUNG bên trong ô (cột có chữ dài không xuống dòng sẽ tự
  // phình to hơn hẳn con số vừa gán, các cột khác bị co lại bù), khiến độ rộng thực tế trên màn
  // hình lệch hẳn so với con số vừa đặt/kéo. Gọi hàm này SAU MỖI LẦN đổi độ rộng bất kỳ cột nào.
  function syncTableWidthFromThs(table){
    var headRow = table.querySelector("thead tr:first-child");
    if(!headRow) return;
    var ths = headRow.querySelectorAll("th");
    if(!ths.length) return;
    var sum = 0;
    ths.forEach(function(th){ sum += parseFloat(th.style.width) || 0; });
    if(sum > 0) table.style.width = Math.round(sum) + "px";
  }

  // Đo độ rộng "tự nhiên" cần thiết của 1 cột: rộng nhất giữa tiêu đề và mọi ô dữ liệu đang có
  // trong CỘT đó (không phụ thuộc việc cột đang bị co hẹp hay không, vì đo bằng canvas — không
  // đụng tới layout thật của bảng). Nếu cột có chứa control (input/button/select) ở BẤT KỲ dòng
  // nào, trả thêm cờ hasControl để áp mức tối thiểu bằng đúng kích thước control đó (không cho
  // co hẹp hơn, tránh che mất nút/ô nhập).
  var __canvasCtx = null;
  function textWidth(text, font){
    if(!__canvasCtx) __canvasCtx = document.createElement("canvas").getContext("2d");
    __canvasCtx.font = font;
    return __canvasCtx.measureText(text || "").width;
  }
  function naturalColumnWidth(table, th, colIndex){
    var headFont = getComputedStyle(th).font || "600 12px Inter, sans-serif";
    var maxWidth = textWidth((th.textContent || "").trim(), headFont) + 40; // + đệm padding/tay kéo
    var hasControl = false;
    var controlWidth = 0;
    var rows = table.querySelectorAll(":scope > tbody > tr");
    // Bảng có thể rất nhiều dòng — chỉ cần quét tối đa ~80 dòng đầu để ước lượng, đủ đại diện mà
    // không làm chậm bảng lớn (giống tinh thần tối ưu hiệu năng đã áp dụng ở module 3).
    var limit = Math.min(rows.length, 80);
    var cellFont = null, cellPad = 32;
    for(var i=0; i<limit; i++){
      var td = rows[i].children[colIndex];
      if(!td) continue;
      // Ô gộp nhiều cột (VD dòng "Không có dữ liệu để hiển thị" colspan=N) không thuộc riêng cột
      // nào — trước đây bị đo như dữ liệu của cột đầu, làm cột đó phình to vô lý.
      if((td.colSpan || 1) > 1) continue;
      var control = td.querySelector("input, button, select");
      if(control){
        hasControl = true;
        var w = control.getBoundingClientRect().width || parseFloat(getComputedStyle(control).width) || 0;
        var cs = getComputedStyle(td);
        var total = w + (parseFloat(cs.paddingLeft)||0) + (parseFloat(cs.paddingRight)||0) + 6;
        if(total > controlWidth) controlWidth = total;
        continue;
      }
      if(cellFont === null){
        var tcs = getComputedStyle(td);
        cellFont = tcs.font || headFont;
        cellPad = (parseFloat(tcs.paddingLeft)||16) + (parseFloat(tcs.paddingRight)||16);
      }
      var w2 = textWidth((td.textContent || "").trim(), cellFont) + cellPad + 4;
      if(w2 > maxWidth) maxWidth = w2;
    }
    return { natural: Math.ceil(maxWidth), hasControl: hasControl, controlWidth: Math.ceil(controlWidth) };
  }

  // Tự động canh độ rộng MỌI cột theo đúng nội dung thật, NGAY KHI bảng vừa được vẽ/nạp dữ liệu —
  // không cần đợi người dùng kéo tay trước (trước đây chỉ tính độ rộng "tự nhiên" lúc bắt đầu kéo,
  // nên trước khi kéo bảng vẫn bị trình duyệt tự ép hẹp — xem SỬA LỖI bên dưới). Chỉ chạy 1 lần
  // cho mỗi lần bảng được vẽ mới (dataset __widthsLocked) — nếu người dùng tự kéo tay sau đó, lần
  // vẽ TIẾP THEO (dữ liệu mới nạp vào / đổi trang / lọc...) vẫn tính lại theo dữ liệu MỚI, đúng
  // tinh thần "tự động căn chỉnh theo kích thước giá trị bảng nạp vào".
  function autoFitAllColumns(table){
    if(table.__widthsLocked) return;
    var headRow = table.querySelector("thead tr:first-child");
    if(!headRow) return;
    var ths = headRow.querySelectorAll("th");
    if(!ths.length) return;
    var widths = [];
    ths.forEach(function(th, idx){
      var info = applyMinWidth(table, th, idx);
      var fitW = info.hasControl ? info.controlWidth : info.natural;
      widths.push(Math.max(fitW, MIN_COL_FALLBACK));
    });
    // Nếu tổng độ rộng "vừa khít dữ liệu" NHỎ HƠN khung hiển thị thật (khung cuộn ngang chứa
    // bảng), chia đều phần dư cho mọi cột để bảng luôn lấp đầy màn hình — tránh để trống 1 mảng
    // lớn bên phải như trước (chỉ nhỏ gọn theo dữ liệu mà không lấp đầy khung). Nếu dữ liệu cần
    // NHIỀU hơn khung hiển thị thì giữ nguyên độ rộng tự nhiên, khung tự cuộn ngang (không co ép).
    var scrollEl = table.closest(".table-scroll") || table.closest(".table-wrap");
    var available = scrollEl ? scrollEl.clientWidth : 0;
    var sum = widths.reduce(function(a,b){ return a+b; }, 0);
    if(available > sum && sum > 0){
      // Chia phần dư THEO TỈ LỆ độ rộng tự nhiên của từng cột (cột vốn đã rộng — VD "Tên hàng"
      // — được chia nhiều hơn), thay vì chia đều — tránh làm cột vốn rất hẹp (checkbox, STT)
      // bị kéo dãn bất hợp lý, ô nhỏ nằm lọt thỏm giữa 1 khoảng trống lớn.
      var extraTotal = available - sum;
      widths = widths.map(function(w){ return w + extraTotal * (w / sum); });
    }
    ths.forEach(function(th, idx){ th.style.width = Math.round(widths[idx]) + "px"; });
    table.style.tableLayout = "fixed";
    // QUAN TRỌNG: một số bảng có sẵn CSS "table{width:100%}" — nếu để nguyên, table-layout:fixed
    // sẽ co TỈ LỆ mọi cột lại cho vừa đúng 100% khung chứa thay vì tôn trọng độ rộng PIXEL vừa
    // tính. Trước đây ép về "auto" để né CSS width:100% đó — nhưng "auto" lại gây ra lỗi khác
    // (xem chú thích ở syncTableWidthFromThs() phía trên): Chrome tính lại độ rộng "auto" theo
    // NỘI DUNG ô thay vì theo đúng các <th> vừa đặt. Đặt CỨNG 1 con số px đúng bằng tổng các
    // <th> (luôn thắng CSS ngoài vì là inline style) vừa né được CSS width:100%, vừa không bị
    // Chrome tính lại theo nội dung — .table-wrap/.table-scroll đã có overflow-x:auto nên thừa
    // ra thì tự cuộn ngang, không co ép.
    syncTableWidthFromThs(table);
    table.__widthsLocked = true;
  }

  // Khoá độ rộng HIỆN TẠI của tất cả cột (chuyển table-layout -> fixed) để kéo dãn 1 cột không
  // làm xô lệch các cột khác. Bình thường KHÔNG cần gọi hàm này nữa vì autoFitAllColumns() ở trên
  // đã tự khoá + đặt đúng độ rộng ngay khi bảng vừa vẽ xong rồi — chỉ còn dùng làm lưới an toàn
  // (phòng trường hợp bảng nào đó vì lý do gì chưa kịp auto-fit trước khi người dùng bấm kéo).
  function lockColumnWidths(table){
    if(table.__widthsLocked) return;
    var headRow = table.querySelector("thead tr:first-child");
    if(!headRow) return;
    var ths = headRow.querySelectorAll("th");
    if(!ths.length) return;
    ths.forEach(function(th, idx){
      th.style.width = th.getBoundingClientRect().width + "px";
      applyMinWidth(table, th, idx);
    });
    table.style.tableLayout = "fixed";
    syncTableWidthFromThs(table);
    table.__widthsLocked = true;
  }

  function applyMinWidth(table, th, idx){
    var info = naturalColumnWidth(table, th, idx);
    var minW = info.hasControl ? Math.max(info.controlWidth, MIN_COL_FALLBACK) : MIN_COL_FALLBACK;
    th.dataset.minWidth = minW;
    return info;
  }

  // Bung 1 cột vừa khít nội dung (nhấp đúp viền cột — kiểu Excel). Luôn khoá lại độ rộng <table>
  // theo tổng các <th> sau khi đổi (xem syncTableWidthFromThs), và báo sự kiện cho module chủ.
  function autoFitColumn(table, targetTh){
    lockColumnWidths(table);
    table.__manualResize = true;
    var liveIdx = Array.prototype.indexOf.call(targetTh.parentElement.children, targetTh);
    var info = naturalColumnWidth(table, targetTh, liveIdx);
    var fitW = Math.max(info.hasControl ? info.controlWidth : info.natural, MIN_COL_FALLBACK);
    targetTh.style.width = fitW + "px";
    syncTableWidthFromThs(table);
    notifyColWidth(table, "tablecolautofit", liveIdx, fitW);
    return fitW;
  }
  function notifyColWidth(table, type, index, width){
    try{
      table.dispatchEvent(new CustomEvent(type, {bubbles:true, detail:{index:index, width:width}}));
    }catch(_){}
  }

  // SỬA LỖI "bảng không tự căn chỉnh theo kích thước dữ liệu": nhiều module dựng <table> MỘT LẦN
  // (tiêu đề + <tbody> rỗng) rồi các lần tìm kiếm / lọc / đổi trang / nạp dữ liệu mới CHỈ thay
  // nội dung <tbody>. autoFitAllColumns() lại chỉ chạy 1 lần cho mỗi phần tử <table> (cờ
  // __widthsLocked) — nếu lần đầu bảng còn rỗng/ít dữ liệu, độ rộng bị khoá ở mức hẹp VĨNH VIỄN
  // (VD cột Size hiện "BTP..." dù dữ liệu dài hơn). Nay mỗi khi dữ liệu trong <tbody> đổi, NỚI
  // RỘNG những cột có dữ liệu mới dài hơn độ rộng hiện tại. Chỉ nới rộng, KHÔNG co hẹp — tránh
  // bảng "nhảy cột" liên tục khi gõ tìm kiếm/đổi trang (giật màn hình). Bỏ qua khi người dùng
  // đã tự kéo/nhấp đúp cột (tôn trọng lựa chọn của họ), và bảng có <colgroup> (do hệ thống kéo
  // cột riêng của module quản lý).
  var growQueue = [], growTimer = null;
  function scheduleGrowToFit(table){
    if(!table || !table.__widthsLocked || table.__manualResize) return;
    if(table.querySelector(":scope > colgroup")) return;
    if(growQueue.indexOf(table) === -1) growQueue.push(table);
    if(growTimer) return;
    growTimer = setTimeout(function(){
      growTimer = null;
      var list = growQueue; growQueue = [];
      list.forEach(growColumnsToFit);
    }, 60);
  }
  function growColumnsToFit(table){
    if(!table.isConnected || table.__manualResize) return;
    var headRow = table.querySelector("thead tr:first-child");
    if(!headRow) return;
    var changed = false;
    headRow.querySelectorAll("th").forEach(function(th, idx){
      var info = naturalColumnWidth(table, th, idx);
      var fitW = Math.max(info.hasControl ? info.controlWidth : info.natural, MIN_COL_FALLBACK);
      var cur = parseFloat(th.style.width) || 0;
      if(fitW > cur + 1){ th.style.width = Math.ceil(fitW) + "px"; changed = true; }
    });
    if(changed) syncTableWidthFromThs(table);
  }

  function addColResizers(table){
    var headRow = table.querySelector("thead tr:first-child");
    if(!headRow) return;
    var ths = Array.prototype.slice.call(headRow.querySelectorAll("th"));
    if(!ths.length) return;
    // LOI DA GAP (giu lai lich su de khong sua lai lan nua): <th> dung position:sticky +
    // z-index rieng (de dinh lai khi cuon doc) -- moi <th> co z-index do tu lap 1 "khoi xep
    // lop" (stacking context) RIENG cho TOAN BO noi dung ben trong no, ke ca con co z-index cao
    // hon. Khi 2 <th> lien ke dung CUNG muc z-index, <th> dung SAU trong DOM luon duoc ve chong
    // len <th> dung truoc tai duong bien chung -- z-index cao cua tay keo (span con) ben trong
    // <th> dung truoc KHONG BAO GIO thoat ra ngoai khoi xep lop cua chinh no de noi len tren
    // duoc. Ket qua: tay keo nam o mep phai moi <th> luon bi <th> ke tiep che mat, chuot khong
    // bao gio cham toi duoc -- dung hien tuong "chi keo tren duoi (dong) duoc, khong keo trai
    // phai (cot) duoc" (tay keo DONG nam o <td> dau dong, khong co <th> nao khac che nen van on).
    // Cach sua DA THU nhung KHONG an: nang tam z-index cua <th> khi hover/keo vao tay keo -- khong
    // hoat dong vi chinh viec tay keo bi che khien su kien "hover vao tay keo" khong bao gio ban
    // ra de kich hoat viec nang z-index do (ga-va-trung).
    // CACH SUA THAT: thay vi gan tay keo cua cot i vao MEP PHAI cua <th> cot i (ben "thua" o
    // duong bien chung), gan no vao MEP TRAI cua <th> cot i+1 -- chinh la <th> dang "THANG" o
    // duong bien do theo dung quy tac xep lop tren. Tay keo van hien thi o dung vi tri duong bien
    // (dung left:-3px de lan nhe qua ben trai), van dieu khien dung do rong cua cot i khi keo --
    // chi khac noi no duoc GAN VAO trong DOM. Cot CUOI cung khong co <th> ke tiep de gan nho nen
    // van giu tay keo o mep phai cua chinh no (khong bi ai che vi khong co cot nao sau no).
    ths.forEach(function(targetTh, idx){
      var mountTh = ths[idx+1] || targetTh;
      var isTrailing = (mountTh === targetTh);
      if(mountTh.querySelector(":scope > .col-resizer[data-for-col='" + idx + "']")) return;
      ensurePositioned(mountTh);
      var grip = document.createElement("span");
      grip.className = "col-resizer" + (isTrailing ? "" : " col-resizer--leading");
      grip.dataset.forCol = String(idx);
      grip.title = "Kéo để đổi độ rộng cột · nhấp đúp để tự bung theo nội dung";
      grip.addEventListener("mousedown", function(e){
        e.preventDefault(); e.stopPropagation();
        lockColumnWidths(table);
        var liveIdx = Array.prototype.indexOf.call(targetTh.parentElement.children, targetTh);
        DRAG = {type:"col", el: targetTh, idx: liveIdx, table: table, startX: e.clientX, startW: targetTh.getBoundingClientRect().width, moved: false};
        document.body.classList.add("resizing-col");
      });
      grip.addEventListener("dblclick", function(e){
        e.preventDefault(); e.stopPropagation();
        autoFitColumn(table, targetTh);
      });
      mountTh.appendChild(grip);
    });
  }

  function addRowResizer(tr){
    var firstCell = tr.querySelector("td, th");
    if(!firstCell || firstCell.querySelector(":scope > .row-resizer")) return;
    ensurePositioned(firstCell);
    var grip = document.createElement("span");
    grip.className = "row-resizer";
    grip.title = "Kéo để đổi chiều cao dòng";
    grip.addEventListener("mousedown", function(e){
      e.preventDefault(); e.stopPropagation();
      DRAG = {type:"row", el: tr, startY: e.clientY, startH: tr.getBoundingClientRect().height};
      document.body.classList.add("resizing-row");
    });
    firstCell.appendChild(grip);
  }

  document.addEventListener("mouseover", function(e){ // gắn tay kéo dòng lười cho dòng dữ liệu đang rê chuột vào
    var t = e.target; if(DRAG || !t || !t.closest) return;
    var tr = t.closest("tbody tr"); if(!tr) return;
    var table = tr.closest("table"); if(table && table.__trEnhanced) addRowResizer(tr);
  }, true);

  document.addEventListener("mousemove", function(e){
    if(!DRAG) return;
    if(DRAG.type === "col"){
      if(!DRAG.moved && Math.abs(e.clientX - DRAG.startX) >= 2){
        DRAG.moved = true;
        DRAG.table.__manualResize = true;
      }
      var minW = Number(DRAG.el.dataset.minWidth) || MIN_COL_FALLBACK;
      var newW = Math.max(minW, DRAG.startW + (e.clientX - DRAG.startX));
      DRAG.el.style.width = newW + "px";
      // Xem chú thích ở syncTableWidthFromThs(): phải khoá lại độ rộng thật của <table> NGAY
      // trong lúc kéo, nếu không bảng vẫn ở "auto" nên bị tính lại theo nội dung ô.
      syncTableWidthFromThs(DRAG.table);
    } else {
      var newH = Math.max(MIN_ROW, DRAG.startH + (e.clientY - DRAG.startY));
      DRAG.el.style.height = newH + "px";
    }
  });
  document.addEventListener("mouseup", function(){
    if(!DRAG) return;
    if(DRAG.type === "col" && DRAG.el) DRAG.el.style.zIndex = "";
    if(DRAG.type === "col" && DRAG.moved){
      // Báo cho module chủ bảng (VD Tồn Kho Gửi lưu độ rộng riêng để áp lại khi vẽ lại)
      notifyColWidth(DRAG.table, "tablecolresize", DRAG.idx, parseFloat(DRAG.el.style.width));
    }
    DRAG = null;
    document.body.classList.remove("resizing-col");
    document.body.classList.remove("resizing-row");
  });

  function enhanceTable(table){
    if(!table.closest(".table-wrap")) return;
    // Bỏ qua bảng "Bảng Số Liệu" (module Số liệu kiểm kê) — bảng đó đã có SẴN hệ thống kéo-thả
    // + đổi thứ tự cột RIÊNG (xem .bsl-col-resizer/.bsl-row-resizer trong doi-chieu-kho-hang.js),
    // thêm tay kéo generic ở đây vào sẽ chồng lên/xung đột với tay kéo riêng đó.
    if(table.closest(".bsl-table-wrap")) return;
    table.__trEnhanced = true;
    addColResizers(table);
    // PC 1.49: chỉ hàng tiêu đề gắn tay kéo dòng ngay; dòng dữ liệu gắn LƯỜI khi rê chuột vào (xem lazyRowResizer) — trước đây
    // mỗi lần vẽ bảng gắn 1 span + 1 hàm + 1 getComputedStyle cho TỪNG dòng (bảng nghìn dòng → trễ vài giây).
    table.querySelectorAll("thead tr:first-child").forEach(addRowResizer);
    // Tự canh độ rộng cột NGAY, không đợi người dùng kéo — đây chính là phần còn thiếu khiến
    // bảng "chưa tự động căn chỉnh theo kích thước giá trị bảng nạp vào" trước đây.
    autoFitAllColumns(table);
  }

  // Các bảng trong module này liên tục được vẽ lại (innerHTML) khi tải dữ liệu, lọc theo ngày,
  // gộp phiếu... -> theo dõi bằng MutationObserver để tự thêm tay kéo cho dòng/bảng mới.
  // HIỆU NĂNG (rà soát 09/2026): 1 lần vẽ bảng sinh RẤT NHIỀU bản ghi thay đổi (mỗi dòng/ô/tay kéo 1 bản ghi) —
  // trước đây MỖI bản ghi gọi enhanceTable() (đo lại độ rộng MỌI ô = ép trình duyệt tính lại bố cục) → bảng lớn
  // bị giật / treo vài giây. Nay gom các bảng bị ảnh hưởng trong 1 lượt vào Set rồi xử lý MỖI BẢNG 1 LẦN.
  var mo = new MutationObserver(function(mutations){
    var touched = new Set(), grow = new Set();
    mutations.forEach(function(m){
      // PC 1.49: bỏ qua thay đổi do CHÍNH tay kéo (span .row-resizer / .col-resizer) vừa gắn — tránh đo lại độ rộng cột vô ích
      if(m.addedNodes.length === 1 && !m.removedNodes.length){
        var an = m.addedNodes[0];
        if(an.nodeType === 1 && an.classList && (an.classList.contains("row-resizer") || an.classList.contains("col-resizer"))) return;
      }
      m.addedNodes.forEach(function(node){
        if(node.nodeType !== 1) return;
        if(node.matches && node.matches("table")){ touched.add(node); }
        if(node.querySelectorAll && node.firstElementChild){ node.querySelectorAll("table").forEach(function(t){ touched.add(t); }); }
      });
      // Dòng có thể được thêm trực tiếp vào <tbody> đã tồn tại (không kèm bảng mới)
      if(m.target && m.target.closest){
        var table = m.target.closest("table");
        if(table){
          touched.add(table);
          if(m.target.closest("tbody")) grow.add(table);
        }
      }
    });
    touched.forEach(function(t){ enhanceTable(t); });
    grow.forEach(scheduleGrowToFit);
  });
  mo.observe(document.body, {childList:true, subtree:true});
  document.querySelectorAll(".table-wrap table").forEach(enhanceTable);

  // SỬA LỖI "bảng không tự co giãn theo kích thước màn hình": bản trước CHỈ nghe sự kiện
  // window "resize", nhưng sự kiện đó KHÔNG bắn ra khi 1 bảng đang nằm trong tab ẨN
  // (display:none) rồi được CHUYỂN SANG HIỆN (bấm đổi tab) — mà cửa sổ Electron desktop
  // thường mở cố định 1 kích thước, người dùng hiếm khi tự kéo giãn cửa sổ, nên trong thực tế
  // sự kiện resize gần như KHÔNG BAO GIỜ xảy ra. Kết quả: nếu autoFitAllColumns() chạy lần đầu
  // lúc khung .table-wrap còn ẩn (clientWidth = 0, vì đang ở tab khác), độ rộng bị khoá vĩnh
  // viễn ở mức hẹp đó — đúng triệu chứng "cột bị cắt chữ, có khoảng trống thừa bên phải,
  // không tự co giãn theo kích thước màn hình" đã gặp thực tế.
  //
  // Thay bằng ResizeObserver gắn trực tiếp lên TỪNG .table-wrap — API này bắn sự kiện mỗi khi
  // kích thước THẬT của phần tử đổi, KỂ CẢ khi đổi từ 0 (ẩn) sang có kích thước thật (vừa hiện
  // ra do chuyển tab) — bắt được đúng mọi trường hợp: đổi cỡ cửa sổ, mở/thu sidebar, chuyển tab,
  // đổi theme làm layout dịch chuyển... mà không cần biết trước lý do gì làm đổi kích thước.
  var roDebounce = null;
  var ro = ("ResizeObserver" in window) ? new ResizeObserver(function(entries){
    if(roDebounce) clearTimeout(roDebounce);
    roDebounce = setTimeout(function(){
      entries.forEach(function(entry){
        if(entry.contentRect.width <= 0) return; // vẫn đang ẩn / vừa mới ẩn đi — bỏ qua
        var table = entry.target.querySelector("table");
        if(!table || table.__manualResize) return;
        table.__widthsLocked = false;
        autoFitAllColumns(table);
      });
    }, 120);
  }) : null;
  function observeTableWrap(wrap){
    if(!ro || wrap.__roObserved) return;
    wrap.__roObserved = true;
    ro.observe(wrap);
  }
  document.querySelectorAll(".table-wrap").forEach(observeTableWrap);
  // Bảng mới có thể được thêm vào DOM sau (module tải dữ liệu xong mới dựng bảng) — theo dõi
  // luôn bằng chính MutationObserver đã có sẵn ở trên thay vì mở thêm 1 observer riêng.
  var origEnhanceTable = enhanceTable;
  enhanceTable = function(table){
    origEnhanceTable(table);
    var wrap = table.closest(".table-wrap");
    if(wrap) observeTableWrap(wrap);
  };

  // SỬA LỖI "nhấp đúp vào viền cột không tự căn": trước đây CHỈ tay cầm nhỏ ở mép tiêu đề mới
  // có nhấp đúp — nhấp đúp vào viền giữa 2 ô dữ liệu (thao tác tự nhiên nhất, giống Excel) không
  // có tác dụng gì. Nay nhấp đúp trong vùng 6px quanh viền trái/phải của BẤT KỲ ô tiêu đề/dữ
  // liệu nào → bung cột đó vừa khít nội dung. Bỏ qua bảng không do hệ thống này quản lý, bảng có
  // <colgroup> (module có hệ thống kéo cột riêng) và khi module khác đã xử lý (defaultPrevented).
  var DBL_EDGE = 6;
  function logicalIndexOf(cell){
    var idx = 0, c = cell.previousElementSibling;
    while(c){ idx += (c.colSpan || 1); c = c.previousElementSibling; }
    return idx;
  }
  document.addEventListener("dblclick", function(e){
    if(e.defaultPrevented) return;
    var cell = e.target.closest && e.target.closest("td, th");
    if(!cell || (cell.colSpan || 1) > 1) return;
    if(e.target.closest(".col-resizer")) return; // tay cầm đã tự xử lý
    var table = cell.closest("table");
    if(!table || !table.__trEnhanced || table.querySelector(":scope > colgroup")) return;
    if(cell.closest("tfoot")) return;
    var rect = cell.getBoundingClientRect();
    var li = logicalIndexOf(cell);
    var nearRight = rect.right - e.clientX <= DBL_EDGE;
    var nearLeft = li > 0 && (e.clientX - rect.left) <= DBL_EDGE;
    if(!nearRight && !nearLeft) return;
    var colIdx = (nearLeft && !nearRight) ? li - 1 : li;
    var headRow = table.querySelector("thead tr:first-child");
    var th = headRow && headRow.children[colIdx];
    if(!th) return;
    e.preventDefault();
    autoFitColumn(table, th);
  });

  // API cho module khác dùng lại (VD hệ thống kéo viền riêng của Tồn Kho Gửi).
  window.TableResize = {
    autoFitColumn: autoFitColumn,
    syncTableWidth: syncTableWidthFromThs
  };

  // Vẫn giữ lại "resize" cửa sổ làm lưới an toàn cho các khung KHÔNG NẰM trong .table-wrap
  // chuẩn (nếu có) — ResizeObserver ở trên đã lo phần lớn trường hợp thực tế.
  var resizeTimer = null;
  window.addEventListener("resize", function(){
    if(resizeTimer) clearTimeout(resizeTimer);
    resizeTimer = setTimeout(function(){
      document.querySelectorAll(".table-wrap table").forEach(function(table){
        if(table.__manualResize) return;
        table.__widthsLocked = false;
        autoFitAllColumns(table);
      });
    }, 150);
  });
})();
