/* ==========================================================================
   TÌM KIẾM "THÔNG MINH" DÙNG CHUNG (09/2026) — ô tìm chung trang "Dữ Liệu" của Module 1 và Module 2.
   Cần nạp TRƯỚC: js/size-label.js (window.KLSizePC).

   Cách tìm GIỐNG ô "Tìm trong toàn kho" trang chính (js/global-search.js › matcher) và điện thoại (makeLotMatch):
     • Không phân biệt dấu, hoa/thường, không tính khoảng trắng ("btp50" = "BTP 50").
     • Mỗi từ tìm RIÊNG, chỉ cần có ở 1 cột bất kỳ của dòng (VÀ giữa các từ):
       "BTP 50 MKS Nau" ra cả dòng Đặc tính "MKS Nau" lẫn dòng Đặc tính "MKS" + Hợp đồng "Nau".
     • Phần đầu là đúng 1 nhãn Size ("BTP 50 …") → chỉ dòng có ĐÚNG nhãn đó (không lẫn BTP 51/60, BTP 500).
     • Cả ô là dạng nhãn ("BTP 13") → nhãn BẮT ĐẦU bằng BTP13; có dấu cách cuối ("BTP 13 ") → đúng nhãn.
     • Ô chỉ có 1 con số ("50", "1.5") → giữ cách cũ của Module 1: ô nào ĐÚNG BẰNG số đó.
   Nhãn Size của dòng: đọc từ mã 21 ký tự (KLSizePC, từ điển Module 8 › "8. Size"); không đọc được thì lấy cột Size
   nếu nó đã là nhãn có chữ đứng trước (VD Kho gửi "NL85") — GIỐNG main.js › buildGlobalSearchData().
   ========================================================================== */
(function(root){
  'use strict';
  function norm(v){ return String(v == null ? '' : v).normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/g, 'd').replace(/Đ/g, 'D').toLowerCase(); }
  function cmp(v){ return String(v == null ? '' : v).replace(/\s+/g, ''); }
  var PURE_NUM = /^\d+([.,]\d+)?$/;

  // ---- Nhãn Size: bản chụp chuẩn ngay lập tức, rồi đọc từ điển Module 8 (1 lần / trang) ----
  var sizeFn = root.KLSizePC ? root.KLSizePC.create([]) : function(){ return { tag: '' }; };
  var version = 0, loading = null, waiters = [];
  function loadDict(){
    if(loading) return loading;
    loading = (async function(){
      try{
        if(!root.KLSizePC || !root.dbAPI || typeof root.dbAPI.readSheet !== 'function') return;
        var res = await root.dbAPI.readSheet('tong-hop', 'MaHoa');
        var list = (res && res.ok && Array.isArray(res.data)) ? res.data.filter(function(r){ return Number(r['Tổ hợp']) === 8; })
          .map(function(r){ return { code: r['Mã'], name: r['Tên'] }; }) : [];
        // PC 1.08: bảng Ánh xạ tên hàng (tên viết tắt BTP / TGC… sửa trong Tổng hợp) — đọc cùng lúc với từ điển Size
        var ax = null;
        if(root.KLAnhXa){ ax = await root.KLAnhXa.load(async function(n){ var r = await root.dbAPI.readSheet('tong-hop', n); return (r && r.ok && Array.isArray(r.data)) ? r.data : []; }); }
        if(list.length || ax){ sizeFn = root.KLSizePC.create(list, ax); version++; waiters.forEach(function(f){ try{ f(); }catch(e){ window.klLog && klLog('smart-search.js:35', e); } }); }
      }catch(e){ console.error('Tìm kiếm: không đọc được từ điển Size Module 8 (dùng bản chụp):', e); }
    })();
    return loading;
  }
  // Nhãn của 1 dòng: từ mã (+ tên hàng) trước, không có thì từ cột Size dạng "NL85".
  function tagOf(ma, ten, sizeCol){
    var t = ma ? sizeFn(ma, ten).tag : '';
    if(t) return t;
    var sz = String(sizeCol == null ? '' : sizeCol).trim();
    return /^[A-Za-zÀ-ỹĐđ]+\s*\d/.test(sz) ? sz : '';
  }

  // ---- Chỉ mục tìm kiếm cho 1 bảng ----
  // n: số dòng; cellsOf(i) → mảng giá trị ô của dòng i; tagInfo(i) → [maHang, tenHang, size] của dòng i.
  function buildIndex(n, cellsOf, tagInfo){
    var cells = new Array(n), s = new Array(n), sc = new Array(n), tags = new Array(n);
    var tagSet = new Set(), prefixes = new Set();
    for(var i = 0; i < n; i++){
      var c = (cellsOf(i) || []).map(function(v){ return norm(v).trim(); });
      var ti = tagInfo ? tagInfo(i) || [] : [];
      var tag = tagOf(ti[0], ti[1], ti[2]);
      var t = tag ? cmp(norm(tag)) : '';
      cells[i] = c; tags[i] = t;
      s[i] = c.join(' ') + (tag ? ' ' + norm(tag) : '');
      sc[i] = null;
      if(t){ tagSet.add(t); var m = /^[a-z]+/.exec(t); if(m) prefixes.add(m[0]); }
    }
    return { n: n, cells: cells, s: s, sc: sc, tags: tags, tagSet: tagSet, prefixes: prefixes, version: version };
  }
  function wordsHit(idx, i, qs){
    var s = idx.s[i];
    if(qs.every(function(w){ return s.indexOf(w) >= 0; })) return true;
    if(idx.sc[i] === null) idx.sc[i] = cmp(s);
    return idx.sc[i].indexOf(qs.join('')) >= 0;
  }
  // → null khi ô trống (không lọc), ngược lại hàm (i) → true/false.
  function matcher(idx, raw){
    raw = String(raw == null ? '' : raw);
    var qs = norm(raw).trim().split(/\s+/).filter(Boolean);
    if(!qs.length) return null;
    if(qs.length === 1 && PURE_NUM.test(raw.trim())){
      var q = norm(raw.trim());
      return function(i){ return idx.cells[i].some(function(c){ return c === q; }); };
    }
    for(var k = qs.length - 1; k >= 1; k--){
      var tag = qs.slice(0, k).join('');
      if(!idx.tagSet.has(tag)) continue;
      var rest = qs.slice(k);
      return (function(tag, rest){ return function(i){ return idx.tags[i] === tag && wordsHit(idx, i, rest); }; })(tag, rest);
    }
    var all = qs.join('');
    var m = /^([a-z]+)\d/.exec(all);
    if(m && idx.prefixes.has(m[1])){
      if(/\s$/.test(raw)) return function(i){ return idx.tags[i] === all; };
      return function(i){ return !!idx.tags[i] && idx.tags[i].indexOf(all) === 0; };
    }
    return function(i){ return wordsHit(idx, i, qs); };
  }

  root.KLSmartSearch = {
    buildIndex: buildIndex, matcher: matcher, tagOf: tagOf,
    loadDict: loadDict,
    version: function(){ return version; },
    // Gọi lại khi từ điển Size Module 8 nạp xong (để vẽ lại nếu đang tìm).
    onDictReady: function(fn){ if(typeof fn === 'function') waiters.push(fn); },
    placeholder: 'Tìm: BTP 50 MKS Nau, mã hàng, tên hàng, lô…'
  };
})(typeof window !== 'undefined' ? window : this);
