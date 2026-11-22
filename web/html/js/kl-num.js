/* PC 2.32 — QUY CHUẨN ĐỌC SỐ + NGÀY DÙNG CHUNG TOÀN APP (anh duyệt 29/10/2026).
   Chỉ áp cho Ô CHỮ (ô Excel là số thật thì giữ nguyên số đó).
   SỐ — KLNum.parse(v, { unit }) → số | null (ô trống) | NaN (không đọc được):
     • Chuẩn của app: phẩy = nghìn, chấm = thập phân      "1,234" = 1234 · "1,500.50" = 1500.5 · "1.250" = 1.25
     • Kiểu Việt vẫn đọc được khi KHÔNG mơ hồ:            "1.234,5" = 1234.5 · "1.234.567" = 1234567 · "12,5" = 12.5
     • Âm kiểu kế toán: "(100)" = −100; dấu trừ "−" (U+2212) = "-"
     • "", "-", "—" = ô trống (null); chữ lẫn số ("50E88725", "abc", "5-") = không đọc được (NaN) — KHÔNG đoán
     • { unit: true }: cho phép đuôi đơn vị chữ ngắn hoặc % sau số ("12 kg", "15%") — dùng cho tính nhanh khi bôi đen / sắp xếp;
       các bước ĐỐI CHIẾU dùng mặc định (đuôi chữ = không đọc được).
   NGÀY — KLDate.parse(v) → "YYYY-MM-DD" | null:
     • dd/mm/yyyy · dd-mm-yyyy · dd.mm.yyyy · năm 2 chữ số = 20yy ("5/10/26" = 05/10/2026) · được kèm giờ phía sau
     • yyyy-mm-dd (yyyy/mm/dd) · số ngày Excel · đối tượng Date
     • ngày không có thật (31/02) = null; KHÔNG đọc kiểu tháng/ngày (mm/dd) */
(function (root) {
  'use strict';
  function parse(v, opt) {
    if (v == null) return null;
    if (typeof v === 'number') return isFinite(v) ? v : NaN;
    if (typeof v === 'boolean') return NaN;
    let s = String(v).replace(/[\s\u00a0\u2007\u202f]+/g, '');
    if (s === '' || s === '-' || s === '—' || s === '–') return null;
    s = s.replace(/\u2212/g, '-');
    let neg = false;
    if (/^\(.*\)$/.test(s)) { neg = true; s = s.slice(1, -1); }
    if (opt && opt.unit) { const m = /^([-+]?[\d.,]+)(%|[A-Za-z\u00C0-\u1EF9][A-Za-z\u00C0-\u1EF9\/.]{0,7})$/.exec(s); if (m) s = m[1]; }
    let sign = '';
    if (/^[-+]/.test(s)) { sign = s[0]; s = s.slice(1); }
    if (!/^[\d.,]+$/.test(s) || !/\d/.test(s) || /^[.,]|[.,]$/.test(s)) return NaN;
    const nC = (s.match(/,/g) || []).length, nD = (s.match(/\./g) || []).length;
    if (nC && nD) {
      const c = s.lastIndexOf(','), d = s.lastIndexOf('.');
      if (c > d) { if (nC > 1) return NaN; s = s.replace(/\./g, '').replace(',', '.'); }   // 1.234,5
      else { if (nD > 1) return NaN; s = s.replace(/,/g, ''); }                              // 1,234.5
    } else if (nC) {
      if (/^\d{1,3}(,\d{3})+$/.test(s)) s = s.replace(/,/g, '');                              // 1,234 · 12,345,678
      else if (nC === 1) s = s.replace(',', '.');                                             // 12,5
      else return NaN;
    } else if (nD > 1) {
      if (/^\d{1,3}(\.\d{3})+$/.test(s)) s = s.replace(/\./g, '');                            // 1.234.567
      else return NaN;
    }
    let n = Number(s);
    if (!isFinite(n)) return NaN;
    if (sign === '-') n = -n;
    return neg ? -n : n;
  }
  // Số hoặc giá trị mặc định khi trống / không đọc được
  const parseOr = (v, def, opt) => { const n = parse(v, opt); return n == null || isNaN(n) ? def : n; };

  const pad = (n) => (n < 10 ? '0' : '') + n;
  function ymd(y, m, d) {
    const t = new Date(y, m - 1, d);
    return t.getFullYear() === y && t.getMonth() === m - 1 && t.getDate() === d ? y + '-' + pad(m) + '-' + pad(d) : null;
  }
  function parseDate(v) {
    if (v == null || v === '') return null;
    if (v instanceof Date) return isNaN(v) ? null : ymd(v.getFullYear(), v.getMonth() + 1, v.getDate());
    if (typeof v === 'number') {
      if (!isFinite(v) || v < 20000 || v > 80000) return null;   // số ngày Excel (khoảng 1954–2119)
      const d = new Date(Math.round((v - 25569) * 86400000));
      return ymd(d.getUTCFullYear(), d.getUTCMonth() + 1, d.getUTCDate());
    }
    const s = String(v).trim();
    let m = /^(\d{1,2})[\/\-.](\d{1,2})[\/\-.](\d{4}|\d{2})(?:[ T]+\d{1,2}:\d{2}(?::\d{2})?(?:\s*[AaPp][Mm])?)?$/.exec(s);
    if (m) { let y = +m[3]; if (m[3].length === 2) y += 2000; return ymd(y, +m[2], +m[1]); }
    m = /^(\d{4})[\/\-.](\d{1,2})[\/\-.](\d{1,2})(?:[ T].*)?$/.exec(s);
    if (m) return ymd(+m[1], +m[2], +m[3]);
    return null;
  }
  root.KLNum = { parse, parseOr };
  root.KLDate = { parse: parseDate };
  if (typeof module !== 'undefined' && module.exports) module.exports = { KLNum: root.KLNum, KLDate: root.KLDate };
})(typeof window !== 'undefined' ? window : globalThis);
