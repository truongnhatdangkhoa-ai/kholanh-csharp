/* kl-bridge.js — Kho Lạnh C#: thay preload.js của app Electron cũ cho các module gốc chạy trong WebView2.
   Cấp window.dbAPI / window.authAPI với ĐÚNG hình dạng kết quả như app cũ ({ok, data} …); dữ liệu lưu trong SQLite của app C#.
   Đường truyền: WebView2 (window.chrome.webview.postMessage) — hoặc POST /__bridge khi chạy thử bằng máy chủ kiểm tra. */
(function () {
  'use strict';
  if (window.dbAPI) return;
  var wv = window.chrome && window.chrome.webview;
  var seq = 0, pend = {};
  if (wv) {
    wv.addEventListener('message', function (e) {
      var m = e.data;
      if (typeof m === 'string') { try { m = JSON.parse(m); } catch (x) { return; } }
      if (!m || m.__klBridge !== 1) return;
      var p = pend[m.id]; if (!p) return;
      delete pend[m.id];
      p(m.result);
    });
  }
  function call(op, args) {
    var body = JSON.stringify({ op: op, args: args });
    if (wv) {
      return new Promise(function (resolve) {
        var id = ++seq; pend[id] = resolve;
        wv.postMessage(JSON.stringify({ __klBridge: 1, id: id, req: body }));
      });
    }
    return fetch('/__bridge', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: body })
      .then(function (r) { return r.json(); })
      .catch(function (e) { return { ok: false, error: String(e && e.message || e) }; });
  }
  window.dbAPI = {
    readSheet: function (dbKey, sheetName) { return call('readSheet', [dbKey, sheetName]); },
    writeSheet: function (dbKey, sheetName, dataArray) { return call('writeSheet', [dbKey, sheetName, Array.isArray(dataArray) ? dataArray : []]); },
    writeSheets: function (dbKey, sheets) { return call('writeSheets', [dbKey, sheets || {}]); },
    // App cũ: lưu theo dòng chỉ khi bật SQLite cụm; không hỗ trợ → needFull để kl-luu-dong.js ghi cả sheet (cùng kết quả).
    saveRows: function () { return Promise.resolve({ ok: false, needFull: 'khong-ho-tro' }); },
    listSheets: function (dbKey) { return call('listSheets', [dbKey]); },
    logError: function (rec) { return call('logError', [rec || {}]); }
  };
  window.authAPI = { state: function () { return call('authState', []); } };
  window.__klCSharp = true;
})();
