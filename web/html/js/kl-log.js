/* PC 1.90 — NHẬT KÝ LỖI DÙNG CHUNG.
   Trước đây ~130 khối `catch (e) {}` nuốt lỗi im lặng nên khi dữ liệu sai rất khó truy ra nguyên nhân.
   Dùng:  catch (e) { klLog('Tên việc đang làm', e); }
   • Ghi vào <thư mục dữ liệu app>/logs/loi.log (qua window.dbAPI.logError); không có dbAPI thì chỉ console.warn.
   • Tự bắt luôn lỗi chưa xử lý (window error / unhandledrejection) của trang.
   • Chống ngập: cùng 1 lỗi lặp lại trong 60 giây chỉ ghi 1 lần; tối đa 50 dòng/phút/trang.
   • klLog KHÔNG BAO GIỜ ném lỗi và không đổi hành vi của chỗ gọi (chỉ thêm 1 dòng ghi log). */
(function () {
  'use strict';
  if (window.klLog) return;
  var page = '';
  try { page = (location.pathname || '').split('/').pop() || 'index'; } catch (e) { page = '?'; }
  var seen = {}, minuteStart = 0, minuteCount = 0;
  function klLog(ctx, err) {
    try {
      var msg = err && err.message ? err.message : String(err == null ? '' : err);
      var stack = err && err.stack ? String(err.stack).split('\n').slice(1, 4).join(' ← ').replace(/\s+/g, ' ') : '';
      var key = ctx + '|' + msg, now = Date.now();
      if (seen[key] && now - seen[key] < 60000) return;
      seen[key] = now;
      if (now - minuteStart > 60000) { minuteStart = now; minuteCount = 0; }
      if (++minuteCount > 50) return;
      try { console.warn('[klLog] ' + ctx + ': ' + msg); } catch (e1) { /* bỏ qua */ }
      if (window.dbAPI && typeof window.dbAPI.logError === 'function') {
        var p = window.dbAPI.logError({ page: page, ctx: String(ctx || ''), msg: msg, stack: stack });
        if (p && p.catch) p.catch(function () { /* bỏ qua */ });
      }
    } catch (e2) { /* ghi log không được phép gây lỗi */ }
  }
  window.klLog = klLog;
  // PC 1.30: GHI LẠI CÁC LẦN GIAO DIỆN BỊ KHỰNG (≥ 400 ms) vào logs/loi.log (ctx "Khựng") kèm tên trang — để biết trang nào / lúc nào bị đơ khi báo lỗi lag.
  try {
    if (window.PerformanceObserver && !window.__klLongTask) {
      window.__klLongTask = true;
      new PerformanceObserver(function (list) { list.getEntries().forEach(function (en) { if (en.duration >= 400) klLog('Khựng ' + Math.round(en.duration) + ' ms (tại ' + Math.round(en.startTime / 1000) + ' s sau khi mở trang)', 'giao diện đứng ' + Math.round(en.duration) + ' ms'); }); }).observe({ type: 'longtask', buffered: true });
    }
  } catch (e0) { /* trình duyệt không hỗ trợ */ }
  window.addEventListener('error', function (ev) {
    try { klLog('window.error', { message: (ev.message || '') + (ev.filename ? ' @' + String(ev.filename).split('/').pop() + ':' + ev.lineno : ''), stack: ev.error && ev.error.stack }); } catch (e) { /* bỏ qua */ }
  });
  window.addEventListener('unhandledrejection', function (ev) {
    try { klLog('promise', ev.reason || 'unhandledrejection'); } catch (e) { /* bỏ qua */ }
  });
})();
