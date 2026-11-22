/* ============================================================
   "NGÀY SỐ LIỆU" DÙNG CHUNG (Module 1 + Module 2)
   Ngày CỦA số tồn trong file (VD file "TON 23.9.xlsx" = tồn ngày 23/09) — khác "Nạp lúc" (lúc bấm nạp).
   Hàm toàn cục, lưu/hiển thị dạng "dd/mm/yyyy":
     parseDataDate("23/9" | "23.09.2026" | "23-9-26" | "23092026") → "23/09/2026" ('' nếu sai)
     guessDataDateFromFileName("TON 23.9.xlsx") → "23/09/2026" ('' nếu tên file không có ngày)
   ============================================================ */
function ddmmValid(d, m, y){
  const dt = new Date(y, m - 1, d);
  return dt.getFullYear() === y && dt.getMonth() === m - 1 && dt.getDate() === d;
}
function ddmmFmt(d, m, y){ return `${String(d).padStart(2,'0')}/${String(m).padStart(2,'0')}/${y}`; }
// Năm thiếu → năm nay; nếu ra ngày ở tương lai quá 7 ngày thì hiểu là năm trước (VD nạp file "28.12" vào đầu tháng 1).
function ddmmYear(d, m, yRaw){
  if(yRaw){ const y = Number(yRaw); return y < 100 ? 2000 + y : y; }
  const now = new Date(), y = now.getFullYear();
  return new Date(y, m - 1, d) - now > 7 * 86400000 ? y - 1 : y;
}
// Chuỗi người dùng gõ: "23/9", "23.09.2026", "23-9-26", "23092026" → "23/09/2026"; không hợp lệ → ''.
function parseDataDate(str){
  const t = String(str ?? '').trim();
  if(!t) return '';
  let m = t.match(/^(\d{1,2})\s*[./\-\s]\s*(\d{1,2})(?:\s*[./\-\s]\s*(\d{2}|\d{4}))?$/);
  if(!m) m = t.match(/^(\d{2})(\d{2})(\d{4})$/);
  if(!m) return '';
  const d = Number(m[1]), mo = Number(m[2]), y = ddmmYear(d, mo, m[3]);
  return ddmmValid(d, mo, y) ? ddmmFmt(d, mo, y) : '';
}
// Đoán ngày từ tên file: "TON 23.9.xlsx", "Ton_23-09-2026", "tonkho 23.9.26 (2).xlsx", "TON 23092026"…
function guessDataDateFromFileName(name){
  const base = String(name ?? '').replace(/\.[^.]+$/, '');
  const re = /(?:^|\D)(\d{1,2})[._\-\/ ](\d{1,2})(?:[._\-\/ ](\d{4}|\d{2}))?(?!\d)/g;
  let m, found = '';
  while((m = re.exec(base))){
    const d = Number(m[1]), mo = Number(m[2]);
    if(d < 1 || d > 31 || mo < 1 || mo > 12) continue;
    const y = ddmmYear(d, mo, m[3]);
    if(ddmmValid(d, mo, y)){ found = ddmmFmt(d, mo, y); break; }
  }
  if(!found){
    const m8 = base.match(/(?:^|\D)(\d{2})(\d{2})(\d{4})(?!\d)/);
    if(m8 && ddmmValid(Number(m8[1]), Number(m8[2]), Number(m8[3]))) found = ddmmFmt(Number(m8[1]), Number(m8[2]), Number(m8[3]));
  }
  return found;
}
