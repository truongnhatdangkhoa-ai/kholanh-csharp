// 1.110 — Chạy CHÍNH Code.gs (bản chép từ repo kholanh-csharp-mobile › apps-script/Code.gs) như Web App Apps Script, để test C#
// (CodeGsThatTests) nói chuyện với mã thật trên Google thay vì Web App giả viết tay. Giả lập Properties / Cache / Lock / Spreadsheet trong bộ nhớ.
// Chạy: node gia-lap-web-app.js <Code.gs> <cổng|0>  → in "gas on <cổng>". POST / = doPost.
const http=require('http'),fs=require('fs');
const P={PC_TOKEN:'pc-token-that'}, C={}, tabs={};
function tab(name){ if(!tabs[name]) tabs[name]={name,rows:[]}; const t=tabs[name]; return {
  getName:()=>name, getLastRow:()=>t.rows.length, getLastColumn:()=>t.rows.reduce((m,r)=>Math.max(m,r.length),0), getMaxRows:()=>Math.max(1000,t.rows.length),
  getRange:(r,c,nr=1,nc=1)=>{const R={ getValues:()=>{const o=[];for(let i=0;i<nr;i++){const row=t.rows[r-1+i]||[];o.push(Array.from({length:nc},(_,j)=>row[c-1+j]??''));}return o;},
    setValues:v=>{v.forEach((x,i)=>{const row=t.rows[r-1+i]||(t.rows[r-1+i]=[]);x.forEach((y,j)=>row[c-1+j]=y);});return R;},
    clearContent(){for(let i=0;i<nr;i++){const row=t.rows[r-1+i];if(row)for(let j=0;j<nc;j++)row[c-1+j]='';}return R;},
    setNumberFormat(){return this},setFontWeight(){return this},getValue:()=>(t.rows[r-1]||[])[c-1]??'',setValue:v=>{(t.rows[r-1]||(t.rows[r-1]=[]))[c-1]=v;return R;}};return R;},
  setFrozenRows(){}, appendRow:x=>t.rows.push(x), clear(){t.rows=[]} }; }
const ss={getSheetByName:n=>tabs[n]?tab(n):null, insertSheet:n=>{tabs[n]={name:n,rows:[]};return tab(n)}, getName:()=>'Kho Lạnh (giả lập)', getSheets:()=>Object.keys(tabs).map(tab), getId:()=>'x'};
global.PropertiesService={getScriptProperties:()=>({getProperty:k=>P[k]??null,setProperty:(k,v)=>{P[k]=String(v)},setProperties:o=>{for(const k in o)P[k]=String(o[k])},deleteProperty:k=>{delete P[k]},getProperties:()=>({...P})})};
global.CacheService={getScriptCache:()=>({get:k=>C[k]??null,put:(k,v)=>{C[k]=String(v)},remove:k=>{delete C[k]}})};
global.LockService={getScriptLock:()=>({waitLock(){},tryLock(){return true},releaseLock(){}})};
global.SpreadsheetApp={getActiveSpreadsheet:()=>ss,openById:()=>ss,flush(){}};
global.ContentService={createTextOutput:t=>({_t:t,setMimeType(){return this}}),MimeType:{JSON:1}};
global.Utilities={computeDigest:(a,s)=>Array.from(require('crypto').createHash('sha256').update(Buffer.from(String(s),'utf8')).digest()).map(b=>b>127?b-256:b),DigestAlgorithm:{SHA_256:1},Charset:{UTF_8:1},sleep(){}};
global.Logger={log(){}};
eval(fs.readFileSync(process.argv[2],'utf8')+';global.__doPost=doPost;');
const srv=http.createServer((req,res)=>{let b='';req.on('data',d=>b+=d);req.on('end',()=>{
  if(req.url==='/__state'){res.end(JSON.stringify({P,tk:(tabs.TaiKhoan||{rows:[]}).rows.map(r=>r[0])}));return;}
  if(req.url==='/__setprop'){Object.assign(P,JSON.parse(b));res.end('{}');return;}
  let out; try{ out=__doPost({postData:{contents:b}})._t; }catch(e){ out=JSON.stringify({ok:false,error:'LỖI GIẢ LẬP: '+e.message}); }
  res.setHeader('Content-Type','application/json'); res.end(out);});});
srv.listen(+(process.argv[3]||0),'127.0.0.1',()=>console.log('gas on '+srv.address().port));
