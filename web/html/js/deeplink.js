/* ==========================================================================
   DEEPLINK — nhận lệnh "kla-open-view" từ index.html (do trang "Phiếu Xuất Toàn Hệ Thống" — Module 09 —
   gửi lên qua postMessage) để chuyển module này tới đúng phần PHIẾU XUẤT, rồi làm nổi phiếu được chọn.
   Nạp ở cuối <body> của: ton-kho-an-an.html (M01), ton-kho-gui.html (M02), nxt-bot-sot.html (M03),
   (M04 đã gỡ). Mỗi trang tự biết chuyển tab bằng cách nào:
     - M02: bấm nút menu có sẵn (DOM) — làm ngay trong file này.
     - M01, M03: hàm window.__klaOpenView(view) do chính ton-kho-an-an.js / nxt-bot-sot.js đăng ký
       (vì state của 2 module đó nằm trong phạm vi file JS, không truy cập được từ ngoài).
   Không đổi hành vi nào khác của module — chỉ thêm 1 trình lắng nghe message.
   ========================================================================== */
(function(){
  'use strict';
  var done = {};   // chống xử lý lặp: index.html gửi cùng 1 yêu cầu vài lần để chờ iframe/dữ liệu sẵn sàng

  function file(){ return (location.pathname.split('/').pop() || '').toLowerCase(); }

  function clickTab(sel){
    var el = document.querySelector(sel);
    if(!el) return false;
    el.click();
    return true;
  }

  // Đi tới view. Trả về Promise<boolean> (true = đã chuyển được).
  function openView(view){
    var f = file();
    if(f === 'ton-kho-gui.html'){                       // M02 — tab "Phiếu Xuất"
      return Promise.resolve(clickTab('nav.menu button[data-tab="px"]'));
    }
    return waitFor(function(){ return typeof window.__klaOpenView === 'function'; }, 15000).then(function(ok){
      if(!ok) return false;
      return Promise.resolve(window.__klaOpenView(view)).then(function(){ return true; });
    });
  }

  function waitFor(cond, ms){
    return new Promise(function(resolve){
      var t0 = Date.now();
      (function tick(){
        if(cond()) return resolve(true);
        if(Date.now() - t0 > ms) return resolve(false);
        setTimeout(tick, 150);
      })();
    });
  }

  // Làm nổi dòng/thẻ chứa mã phiếu (nếu tìm thấy) và cuộn tới đó.
  function highlight(text){
    if(!text) return;
    var tries = 0;
    (function attempt(){
      var root = document.getElementById('content') || document.body;
      var walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, null);
      var hit = null, n;
      while((n = walker.nextNode())){
        if(n.nodeValue && n.nodeValue.indexOf(text) !== -1){
          var p = n.parentElement;
          if(p && !/^(SCRIPT|STYLE|OPTION)$/.test(p.tagName) && p.offsetParent !== null){ hit = p; break; }
        }
      }
      if(!hit){ if(++tries < 8) setTimeout(attempt, 300); return; }
      var box = hit.closest('tr, .card, li, .slip, [class*="slip"], [class*="phieu"]') || hit;
      try{ box.scrollIntoView({ block:'center', behavior:'smooth' }); }catch(e){}
      var oldOutline = box.style.outline, oldBg = box.style.backgroundColor, oldTr = box.style.transition;
      box.style.transition = 'outline-color .4s, background-color .4s';
      box.style.outline = '2px solid #ff9f43';
      box.style.backgroundColor = 'rgba(255,159,67,.18)';
      setTimeout(function(){ box.style.outline = oldOutline; box.style.backgroundColor = oldBg; box.style.transition = oldTr; }, 3500);
    })();
  }

  window.addEventListener('message', function(e){
    var d = e.data;
    if(!d || d.type !== 'kla-open-view') return;
    if(d.reqId){ if(done[d.reqId]) return; }
    if(d.reqId) done[d.reqId] = true;
    openView(d.view).then(function(ok){
      if(!ok && d.reqId) done[d.reqId] = false;         // chưa sẵn sàng → cho phép lần gửi lặp sau thử lại
      if(ok) setTimeout(function(){ highlight(d.slipId); }, 500);
    });
  });
})();
