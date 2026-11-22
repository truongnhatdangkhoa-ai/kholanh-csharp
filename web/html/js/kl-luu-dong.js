/* kl-luu-dong.js — LƯU THEO DÒNG (PC 1.67). Dùng cho bảng lớn mà module vẫn giữ CẢ bảng trong bộ nhớ (PC 1.67: Tồn kho An An; PC 1.68: Tồn kho gửi, Lịch xuất khẩu,
   Báo cáo rã đông, Tổng hợp — thêm read() đọc + nhớ, saveMany() thay cho writeSheets).
   Trước đây mỗi lần lưu (VD 1 phiếu xuất trừ 3 dòng) → gửi lại CẢ sheet (hàng nghìn dòng) qua IPC rồi tiến trình chính gộp + so lịch sử cả bảng.
   Nay: nhớ bản đã lưu / đã đọc gần nhất của từng sheet, so từng dòng theo VỊ TRÍ (so nông từng ô), chỉ gửi dòng đổi → window.dbAPI.saveRows.
   Mọi trường hợp không chắc (chưa có bản nhớ, đổi > 50% dòng, máy chủ / máy con LAN, chế độ Excel cũ, bảng vừa bị nơi khác ghi, lỗi) → lưu CẢ sheet như cũ
   (writeSheets, kèm các sheet nhỏ `also` trong cùng 1 lần ghi). Kết quả trả về giữ dạng cũ: { ok, error }.
     KLLuuDong.seed(dbKey, sheet, rowsDaDoc)                 — gọi ngay sau khi đọc sheet từ CSDL (bản nhớ = đúng dữ liệu vừa đọc)
     await KLLuuDong.save(dbKey, sheet, rows, alsoSheets)     — rows: mảng object như writeSheet; alsoSheets: { tenSheet: rows } ghi kèm
     KLLuuDong.forget(dbKey, sheet)                          — bỏ bản nhớ (lần lưu sau sẽ lưu cả sheet) */
(function (root) {
  'use strict';
  const snaps = new Map(); // `${dbKey}|${sheet}` → mảng bản sao nông của từng dòng
  const key = (d, s) => d + '|' + s;
  const copy = (rows) => rows.map((r) => Object.assign({}, r));
  function same(a, b) {
    if (a === b) return true; if (!a || !b) return false;
    let n = 0; for (const k in a) { const x = a[k], y = b[k]; if (x !== y && !((x === undefined || x === null || x === '') && (y === undefined || y === null || y === ''))) return false; n++; }
    for (const k in b) if (!(k in a) && b[k] !== undefined && b[k] !== null && b[k] !== '') return false;
    return true;
  }
  // → { len, set: [[i, dòng], …] } hoặc null nếu không nên lưu theo dòng
  function diff(prev, rows) {
    if (!prev) return null; const set = [];
    for (let i = 0; i < rows.length; i++) if (i >= prev.length || !same(rows[i], prev[i])) set.push([i, rows[i]]);
    if (rows.length && set.length > Math.max(50, rows.length * 0.5)) return null; // đổi quá nhiều (nạp file mới, xóa dòng giữa bảng…) → lưu cả sheet rẻ hơn
    return { len: rows.length, set };
  }
  async function full(api, dbKey, sheet, rows, also) {
    if (also && Object.keys(also).length && typeof api.writeSheets === 'function') return api.writeSheets(dbKey, Object.assign({}, also, { [sheet]: rows }));
    if (also) for (const [n, r] of Object.entries(also)) { const x = await api.writeSheet(dbKey, n, r); if (!x || !x.ok) return x; }
    return api.writeSheet(dbKey, sheet, rows);
  }
  async function save(dbKey, sheet, rows, also) {
    const api = root.dbAPI; if (!api) return { ok: false, error: 'Không có dbAPI' };
    const k = key(dbKey, sheet), p = typeof api.saveRows === 'function' ? diff(snaps.get(k), rows) : null;
    if (p) {
      if (!p.set.length && p.len === snaps.get(k).length && !(also && Object.keys(also).length)) return { ok: true, unchanged: true };
      let r = null; try { r = await api.saveRows(dbKey, sheet, p, also || null); } catch (e) { r = null; }
      if (r && r.ok) { const s = snaps.get(k); s.length = p.len; p.set.forEach(([i, row]) => { s[i] = Object.assign({}, row); }); keepAlso(dbKey, also); return { ok: true, rows: p.set.length }; }
      if (r && !r.needFull) return r; // lỗi thật (VD không có quyền) → báo như cũ, không thử lại
    }
    const r = await full(api, dbKey, sheet, rows, also);
    if (r && r.ok) { snaps.set(k, copy(rows)); keepAlso(dbKey, also); } else { snaps.delete(k); forgetAlso(dbKey, also); }
    return r;
  }
  // sheet ghi kèm cũng thành "bản nhớ" (lần sau sheet đó lớn lên vẫn lưu theo dòng được)
  function keepAlso(dbKey, also) { if (also) for (const [n, r] of Object.entries(also)) { if (Array.isArray(r)) snaps.set(key(dbKey, n), copy(r)); else snaps.delete(key(dbKey, n)); } }
  function forgetAlso(dbKey, also) { if (also) for (const n of Object.keys(also)) snaps.delete(key(dbKey, n)); }
  // PC 1.68 — Lưu NHIỀU sheet 1 lần (thay cho dbAPI.writeSheets): sheet LỚN NHẤT có bản nhớ → lưu theo dòng, các sheet còn lại ghi kèm cùng giao dịch.
  // Có sheet null (xóa sheet) / không có sheet nào lưu theo dòng được → writeSheets cả gói như cũ.
  async function saveMany(dbKey, sheets) {
    const api = root.dbAPI; if (!api) return { ok: false, error: 'Không có dbAPI' };
    const names = Object.keys(sheets || {}); if (!names.length) return { ok: true, unchanged: true };
    const hasNull = names.some((n) => !Array.isArray(sheets[n]));
    let pick = null;
    if (!hasNull && typeof api.saveRows === 'function') {
      names.filter((n) => snaps.has(key(dbKey, n))).sort((a, b) => sheets[b].length - sheets[a].length).some((n) => { if (diff(snaps.get(key(dbKey, n)), sheets[n])) { pick = n; return true; } return false; });
    }
    if (pick) { const also = {}; names.forEach((n) => { if (n !== pick) also[n] = sheets[n]; }); return save(dbKey, pick, sheets[pick], names.length > 1 ? also : undefined); }
    const r = typeof api.writeSheets === 'function' ? await api.writeSheets(dbKey, sheets) : await (async () => { for (const n of names) { if (!Array.isArray(sheets[n])) continue; const x = await api.writeSheet(dbKey, n, sheets[n]); if (!x || !x.ok) return x; } return { ok: true }; })();
    if (r && r.ok) keepAlso(dbKey, sheets); else forgetAlso(dbKey, sheets);
    return r;
  }
  // PC 1.68 — Đọc sheet + nhớ luôn làm bản nhớ (thay cho dbAPI.readSheet ở các chỗ đọc bảng lớn). Trả đúng kết quả của readSheet.
  async function read(dbKey, sheet) {
    const r = await root.dbAPI.readSheet(dbKey, sheet);
    if (r && r.ok && Array.isArray(r.data)) seed(dbKey, sheet, r.data); else forget(dbKey, sheet);
    return r;
  }
  function seed(dbKey, sheet, rows) { if (Array.isArray(rows)) snaps.set(key(dbKey, sheet), copy(rows)); else snaps.delete(key(dbKey, sheet)); }
  function forget(dbKey, sheet) { snaps.delete(key(dbKey, sheet)); }
  const api = { save, saveMany, read, seed, forget, _diff: diff, _same: same };
  root.KLLuuDong = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof window !== 'undefined' ? window : globalThis);
