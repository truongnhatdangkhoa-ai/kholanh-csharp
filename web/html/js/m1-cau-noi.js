/* ============================================================
   CẦU NỐI DỮ LIỆU MODULE 1 (Tồn Kho An An) — PC 1.63
   ============================================================
   Các trang khác (Lịch xuất container, Phiếu xuất kho, Cargo) hỏi Module 1 qua postMessage để dò:
   lookupContractTpc · suggestContractTpc · listTpcLsx · stockIndex · listTpcNames.
   Trước đây nằm chung với Trợ lý AI — Trợ lý AI đã xóa khỏi app, phần cầu nối này được giữ riêng.
   Nạp SAU js/ton-kho-an-an.js. Chỉ ĐỌC dữ liệu; chỉ trả lời tin nhắn từ CỬA SỔ CHA (trang chính).
   Giao thức: { type:'kla-m1q-req', id, op:'call', name, args } → { type:'kla-m1q-res', id, ok, result|error }.
============================================================ */
(function(){

  function colIdx(label){ return state.headers.findIndex(h=>headersFuzzyMatch(h, label)); }

  function rowObj(row, cols){
    const o = {};
    cols.forEach(label=>{
      const ci = colIdx(label);
      o[label] = ci===-1 ? null : (row[ci] ?? '');
    });
    return o;
  }

  function num(v){ const n = Number(v); return isNaN(n) ? 0 : n; }
  // PC 8.47: mã LSX chuẩn hóa — "26.09.028A-16/09" → năm 26, tháng 9, số 28, hậu tố "A". Bỏ số 0 đầu từng phần; hậu tố 1 chữ (A/B) giữ nguyên.
  const LSX_CODE_RE = /(\d{1,4})\.(\d{1,2})\.(\d{1,4})([A-Z]?)/;
  function lsxParts(s){ const m = String(s ?? '').toUpperCase().match(LSX_CODE_RE); return m ? { y:+m[1], m:+m[2], n:+m[3], suf:m[4] } : null; }
  function lsxKey(s){ const p = lsxParts(s); return p ? p.y+'.'+p.m+'.'+p.n+p.suf : ''; }
  function lev(a, b){ const m = a.length, n = b.length; if(!m) return n; if(!n) return m; let prev = Array.from({length:n+1}, (_,j)=>j);
    for(let i=1;i<=m;i++){ const cur=[i]; for(let j=1;j<=n;j++) cur[j]=Math.min(prev[j]+1, cur[j-1]+1, prev[j-1]+(a[i-1]===b[j-1]?0:1)); prev=cur; } return prev[n]; }

  const TOOLS = ['lookupContractTpc', 'suggestContractTpc', 'listTpcLsx', 'stockIndex', 'listTpcNames'];

  function run(name, args){
    args = args || {};
    if(!state.rows || !state.rows.length) return { found:false, message:'Chưa có dữ liệu Load Data nào được nạp trong ứng dụng.' };
    switch(name){

      case 'lookupContractTpc': {
        // Dùng cho "Lịch xuất container" (trang chính, js/lich-xuat-cont.js): dò các dòng bảng Thành Phẩm Chính
        // (Phân loại) có cột "Hợp đồng" = LSX. Không khai báo trong functionDeclarations (không dành cho AI).
        const hdCi = colIdx('Hợp đồng');
        if(hdCi===-1) return { found:false, message:'Không thấy cột "Hợp đồng" trong Load Data.' };
        const sq = v => String(v ?? '').replace(/\s+/g,'').toUpperCase();
        const q = sq(args.lsx);
        if(!q) return { found:false, message:'Chưa nhập LSX.' };
        const base = filterModuleBaseRowsCached('tpc');
        let hits = base.filter(({row})=>sq(row[hdCi])===q);
        // PC 8.47: lớp 1 — so theo MÃ LSX CHUẨN HÓA (bỏ số 0 đầu: "26.09.028A" = "26.9.028A" = "26.9.28A"; hậu tố A/B vẫn phân biệt) trước khi dò "chứa chuỗi".
        const qk = lsxKey(args.lsx);
        if(!hits.length && qk) hits = base.filter(({row})=>lsxKey(row[hdCi])===qk);
        if(!hits.length) hits = base.filter(({row})=>sq(row[hdCi]).includes(q));
        if(!hits.length){
          const other = state.rows.filter(row=>sq(row[hdCi])===q).length;
          return { found:false, message: other ? `Có ${other} dòng Hợp đồng "${args.lsx}" nhưng không thuộc bảng Thành Phẩm Chính.` : `Không thấy Hợp đồng "${args.lsx}" trong bảng Thành Phẩm Chính.` };
        }
        const basis = getWeightBasisAllRows();
        const lines = hits.map(({row})=>{
          const o = rowObj(row, ["Mã hàng","Tên hàng","Size","Đặc tính","Hợp đồng","Khách hàng","Thị trường","ĐVT","Tồn cuối","Trọng lượng","NSX","Ngày SX"]);
          let kg = null;
          if(basis){ const a = parseLooseNumber(row[basis.qtyCi] ?? ''), w = parseLooseNumber(row[basis.weightCi] ?? ''); if(!isNaN(a) && !isNaN(w)) kg = a*w; }
          o.kg = kg; return o;
        });
        return { found:true, count: lines.length, dataDate: state.dataDate || '', lines };
      }

      case 'suggestContractTpc': {
        // PC 8.47 — lớp 2: LSX trong lịch không dò được → liệt kê các LSX GẦN GIỐNG trong Thành Phẩm Chính để NGƯỜI DÙNG chọn (không tự chọn). Chỉ ĐỌC; không dành cho AI.
        const hdCi = colIdx('Hợp đồng');
        if(hdCi===-1) return { found:false, message:'Không thấy cột "Hợp đồng" trong Load Data.' };
        const qRaw = String(args.lsx ?? '').trim();
        if(!qRaw) return { found:false, message:'Chưa nhập LSX.' };
        const qp = lsxParts(qRaw), qk = lsxKey(qRaw), qd = qRaw.replace(/\D/g,'');
        const tenCi = colIdx('Tên hàng'), khCi = colIdx('Khách hàng'), tonCi = colIdx('Tồn cuối');
        const groups = new Map();
        filterModuleBaseRowsCached('tpc').forEach(({row})=>{
          const raw = String(row[hdCi] ?? '').trim(), k = lsxKey(raw); if(!k) return;
          let g = groups.get(k);
          if(!g){ g = { key:k, parts:lsxParts(raw), code:(raw.toUpperCase().match(LSX_CODE_RE)||[raw])[0], hopDong:new Set(), n:0, ton:0, khach:new Set(), ten:new Set() }; groups.set(k, g); }
          g.n++; if(g.hopDong.size<4) g.hopDong.add(raw);
          if(tonCi!==-1) g.ton += num(row[tonCi]);
          if(khCi!==-1){ const v = String(row[khCi] ?? '').trim(); if(v && g.khach.size<3) g.khach.add(v); }
          if(tenCi!==-1){ const v = String(row[tenCi] ?? '').trim(); if(v && g.ten.size<3) g.ten.add(v); }
        });
        const out = [];
        groups.forEach(g=>{
          let sc = 0;
          if(qp && g.parts){
            if(qp.n===g.parts.n) sc += 50;
            if(qp.y===g.parts.y) sc += 15;
            if(qp.m===g.parts.m) sc += 15;
            if(qp.suf===g.parts.suf) sc += 10;
            const d = lev(qk, g.key); if(d<=2) sc += (3-d)*5;
            if(qp.n!==g.parts.n && d>2) sc = 0; // không trùng số thứ tự và cũng không sai ≤ 2 ký tự → bỏ
          }else if(qd.length>=2 && g.key.replace(/\D/g,'').includes(String(+qd))){
            sc = 40; // người dùng chỉ gõ một phần ("028") → số có trong mã
          }
          if(sc>0 && g.key!==qk) out.push({ g, sc });
          else if(sc>0) out.push({ g, sc: sc+5 });
        });
        out.sort((a,b)=>b.sc-a.sc || a.g.key.localeCompare(b.g.key));
        if(!out.length) return { found:false, message:`Không có LSX nào gần giống "${qRaw}" trong bảng Thành Phẩm Chính.` };
        return { found:true, candidates: out.slice(0,8).map(({g,sc})=>({ lsx:g.code, hopDong:Array.from(g.hopDong).join(' | '), soDong:g.n, tonCuoi:g.ton, khach:Array.from(g.khach).join(' · '), tenHang:Array.from(g.ten).join(' · '), diem:sc })) };
      }

      case 'listTpcLsx': {
        // PC 4.22 — dùng cho module "Phiếu xuất kho" (List sản xuất tự tạo từ tồn kho): MỖI LSX trong bảng Thành Phẩm Chính (cột Hợp đồng) + số dòng + TỔNG tồn (kiện) hiện tại.
        // Nhận cả mã có hậu tố ngày ("26.8.068-09/10A" → LSX 26.8.068). Chỉ ĐỌC; không khai báo trong functionDeclarations (không dành cho AI).
        const hdCi = colIdx('Hợp đồng');
        if(hdCi===-1) return { found:false, message:'Không thấy cột "Hợp đồng" trong Load Data.' };
        const tonCi = colIdx('Tồn cuối'), khCi = colIdx('Khách hàng'), dvtCi = colIdx('ĐVT'), m = new Map();
        filterModuleBaseRowsCached('tpc').forEach(({row})=>{
          const raw = String(row[hdCi] ?? '').trim(), mt = raw.toUpperCase().match(LSX_CODE_RE); if(!mt) return;
          const dvt = dvtCi===-1 ? '' : String(row[dvtCi] ?? '').trim();
          const k = lsxKey(raw); let g = m.get(k); if(!g){ g = { code: mt[0], n:0, ton:0, khach:new Set() }; m.set(k, g); }
          g.n++; if(tonCi!==-1 && (!dvt || /ki[eệ]n|th[uù]ng|carton|ctn/i.test(dvt))) g.ton += num(row[tonCi]);
          if(khCi!==-1){ const v = String(row[khCi] ?? '').trim(); if(v && g.khach.size<3) g.khach.add(v); }
        });
        return { found:true, count:m.size, dataDate: state.dataDate || '', items: Array.from(m.values()).map(g=>({ code:g.code, n:g.n, ton:Math.round(g.ton*1000)/1000, khach:Array.from(g.khach).join(' · ') })) };
      }

      case 'stockIndex': {
        // PC 1.53 — dùng cho Cargo (Phiếu xuất kho): tồn kho An An (Load Data của Module 1) GỘP THEO MÃ HÀNG → { ma, ton, n, ten, size, dt }. Chỉ ĐỌC; không dành cho AI.
        const maCi = colIdx('Mã hàng'), tonCi = colIdx('Tồn cuối'), tenCi = colIdx('Tên hàng'), szCi = colIdx('Size'), dtCi = colIdx('Đặc tính');
        if(maCi===-1 || tonCi===-1) return { found:false, message:'Không đọc được cột Mã hàng / Tồn cuối trong Load Data.' };
        const m = new Map();
        state.rows.forEach(row=>{
          const ma = String(row[maCi] ?? '').replace(/\s+/g,'').toUpperCase().replace(/^TPC-/,''); if(!ma) return;
          let v = parseLooseNumber(row[tonCi] ?? ''); if(isNaN(v)) v = 0;
          const g = m.get(ma) || { ma, ton:0, n:0, ten:'', size:'', dt:'' }; g.ton += v; g.n++;
          if(!g.ten && tenCi!==-1) g.ten = String(row[tenCi] ?? '').trim(); if(!g.size && szCi!==-1) g.size = String(row[szCi] ?? '').trim(); if(!g.dt && dtCi!==-1) g.dt = String(row[dtCi] ?? '').trim();
          m.set(ma, g);
        });
        if(!m.size) return { found:false, message:'Load Data chưa có dòng nào có Mã hàng.' };
        return { found:true, dataDate: state.dataDate || '', count:m.size, items: Array.from(m.values()).map(g=>({ ma:g.ma, ton:Math.round(g.ton*100)/100, n:g.n, ten:g.ten, size:g.size, dt:g.dt })) };
      }

      case 'listTpcNames': {
        // Dùng cho module "Phiếu xuất kho" (PC 7.98): các TÊN HÀNG đầy đủ (kèm số dòng + khách) của bảng Thành Phẩm Chính (Phân loại). Module đó tự rút gọn tên
        // (phần trước dấu "(") nên logic rút gọn nằm ở phieu-nhap-xuat.js (kiểm thử được). Không khai báo trong functionDeclarations (không dành cho AI). Chỉ ĐỌC.
        const tenCi = colIdx('Tên hàng');
        if(tenCi===-1) return { found:false, message:'Không thấy cột "Tên hàng" trong Load Data.' };
        const khCi = colIdx('Khách hàng'), m = new Map();
        filterModuleBaseRowsCached('tpc').forEach(({row})=>{
          const raw = String(row[tenCi] ?? '').replace(/\s+/g,' ').trim(); if(!raw) return;
          let e = m.get(raw); if(!e){ e = { raw, n:0, khach:new Set() }; m.set(raw, e); }
          e.n++; if(khCi!==-1){ const k = String(row[khCi] ?? '').trim(); if(k) e.khach.add(k); }
        });
        return { found:true, count:m.size, dataDate: state.dataDate || '', names: Array.from(m.values()).map(e=>({ raw:e.raw, n:e.n, khach:Array.from(e.khach).slice(0,10) })) };
      }

      default:
        return { error:true, message:`Công cụ "${name}" không tồn tại trong module này.` };
    }
  }

  window.addEventListener('message', async (ev) => {
    const d = ev.data;
    if(!d || d.type !== 'kla-m1q-req' || ev.source !== window.parent || window.parent === window) return;
    const reply = (p) => { try{ ev.source.postMessage(Object.assign({ type:'kla-m1q-res', id:d.id }, p), '*'); }catch(e){} };
    try{
      if(d.op === 'list') reply({ ok:true, tools: TOOLS });
      else if(d.op === 'call'){
        if(TOOLS.indexOf(d.name) < 0) throw new Error('Công cụ không được phép.');
        const r = await Promise.resolve(run(d.name, d.args || {}));
        reply({ ok:true, result: JSON.parse(JSON.stringify(r === undefined ? null : r)) });
      }
    }catch(e){ reply({ ok:false, error: String((e && e.message) || e) }); }
  });

})();
