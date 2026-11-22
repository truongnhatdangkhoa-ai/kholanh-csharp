/* PC 8.86 — PANEL CHI TIẾT DÒNG (chuẩn UI/UX kho lạnh). Bấm đúp (hoặc Enter khi đang chọn) 1 dòng trong bảng dữ liệu
   → panel bên phải hiện đủ các cột của dòng đó + nhãn trạng thái suy ra từ số liệu (Còn tồn / Đã xuất hết / Tồn âm / có nhập / có xuất).
   Chỉ ĐỌC từ DOM bảng đang hiển thị — không đụng state, không ghi database. ↑/↓ chuyển dòng, Esc đóng.
   Gắn cho: Module 1 (table.resizable-table) và Module 2 (filtered-table, px-table). Gỡ: bỏ <script> + khối CSS "PANEL CHI TIẾT" trong ui-chuan.css. */
(function(){
  'use strict';
  var SEL = 'table.resizable-table, table.filtered-table, table.px-table';
  var panel = null, cur = null;
  function norm(s){ return String(s||'').normalize('NFD').replace(/[\u0300-\u036f]/g,'').replace(/đ/g,'d').toLowerCase().replace(/\s+/g,' ').trim(); }
  function num(s){ if(window.KLNum){ var k=window.KLNum.parse(s); return k==null?NaN:k; } // PC 2.32: quy chuẩn số chung
    var t=String(s||'').replace(/[\s\u00a0]/g,''); if(!/^-?[\d.,]+$/.test(t)) return NaN;
    if(/,\d{1,2}$/.test(t) && t.indexOf('.')>-1) t=t.replace(/\./g,'').replace(',','.'); else t=t.replace(/,/g,''); return parseFloat(t); }
  function headers(table){ var tr=table.querySelector('thead tr'); if(!tr) return [];
    return Array.prototype.map.call(tr.children,function(th){ var c=th.cloneNode(true); Array.prototype.forEach.call(c.querySelectorAll('input,button,select,.col-resizer,.resize-handle'),function(x){x.remove();}); return c.textContent.replace(/\s+/g,' ').trim(); }); }
  function build(){
    panel=document.createElement('aside'); panel.id='ctdPanel'; panel.setAttribute('role','complementary'); panel.setAttribute('aria-label','Chi tiết dòng'); panel.hidden=true;
    panel.innerHTML='<div class="ctd-h"><b id="ctdTitle">Chi tiết dòng</b><button type="button" id="ctdClose" aria-label="Đóng">×</button></div><div class="ctd-st" id="ctdSt"></div><dl class="ctd-l" id="ctdL"></dl><div class="ctd-f"><button type="button" id="ctdPrev">↑ Dòng trước</button><button type="button" id="ctdNext">↓ Dòng sau</button><button type="button" id="ctdCopy">Sao chép dòng</button></div>';
    document.body.appendChild(panel);
    panel.querySelector('#ctdClose').onclick=close;
    panel.querySelector('#ctdPrev').onclick=function(){ step(-1); };
    panel.querySelector('#ctdNext').onclick=function(){ step(1); };
    panel.querySelector('#ctdCopy').onclick=function(){ var t=Array.prototype.map.call(panel.querySelectorAll('.ctd-l div'),function(d){return d.firstChild.textContent+': '+d.lastChild.textContent;}).join('\n');
      try{ navigator.clipboard.writeText(t); }catch(e){ window.klLog && klLog('chi-tiet-dong.js:23', e); } };
  }
  function chip(cls,t){ return '<span class="st '+cls+'">'+t+'</span>'; }
  function show(tr){
    if(!panel) build(); if(cur) cur.classList.remove('ctd-row'); cur=tr; tr.classList.add('ctd-row');
    var table=tr.closest('table'), hs=headers(table), cells=Array.prototype.slice.call(tr.children), rows='', st='', vals={};
    cells.forEach(function(td,i){ if(td.querySelector('input[type=checkbox]')) return; var h=hs[i]||('Cột '+(i+1)); var v=td.textContent.replace(/\s+/g,' ').trim(); if(!v||v==='—') v='—';
      vals[norm(h)]=v; rows+='<div><dt></dt><dd></dd></div>'; });
    var dl=panel.querySelector('#ctdL'); dl.innerHTML=rows; var k=0;
    cells.forEach(function(td,i){ if(td.querySelector('input[type=checkbox]')) return; var d=dl.children[k++]; d.firstChild.textContent=hs[i]||('Cột '+(i+1)); d.lastChild.textContent=td.textContent.replace(/\s+/g,' ').trim()||'—'; });
    var cuoi=NaN,nhap=NaN,xuat=NaN; Object.keys(vals).forEach(function(h){ if(h.indexOf('ton cuoi')===0) cuoi=num(vals[h]); else if(h==='nhap') nhap=num(vals[h]); else if(h==='xuat') xuat=num(vals[h]); });
    if(!isNaN(cuoi)){ st+= cuoi<0?chip('bad','Tồn âm — cần kiểm tra'):cuoi===0?chip('info','Đã xuất hết'):chip('ok','Còn tồn'); }
    if(nhap>0) st+=chip('info','Có nhập trong kỳ'); if(xuat>0) st+=chip('info','Có xuất trong kỳ');
    panel.querySelector('#ctdSt').innerHTML=st;
    var first=cells.filter(function(td){return !td.querySelector('input[type=checkbox]');})[0]; panel.querySelector('#ctdTitle').textContent=(first&&first.textContent.trim())||'Chi tiết dòng';
    panel.hidden=false; document.documentElement.classList.add('ctd-open');
  }
  function close(){ if(panel) panel.hidden=true; if(cur) cur.classList.remove('ctd-row'); cur=null; document.documentElement.classList.remove('ctd-open'); }
  function step(d){ if(!cur) return; var n=d<0?cur.previousElementSibling:cur.nextElementSibling; while(n&&n.tagName!=='TR') n=d<0?n.previousElementSibling:n.nextElementSibling; if(n&&n.children.length>1){ show(n); n.scrollIntoView({block:'nearest'}); } }
  document.addEventListener('dblclick',function(e){ var tr=e.target.closest&&e.target.closest('tbody tr'); if(!tr||!tr.closest(SEL)) return; if(e.target.closest('input,button,a,select,textarea')) return; if(tr.children.length<2) return; show(tr); });
  document.addEventListener('keydown',function(e){ if(!panel||panel.hidden) return; if(e.key==='Escape'){ close(); } else if((e.key==='ArrowDown'||e.key==='ArrowUp')&&e.altKey){ e.preventDefault(); step(e.key==='ArrowDown'?1:-1); } });
})();
