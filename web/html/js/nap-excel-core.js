/* NAP-EXCEL-CORE — KIỂM TRA FILE EXCEL TRƯỚC KHI NẠP (PC 3.12). Không dùng DOM → kiểm thử được trong Node (module.exports / window.NapExcelCore).
   Dùng cho Tồn kho An An (Load Data: tiêu đề + dòng dạng mảng) và Tồn kho gửi (dòng dạng object theo TARGET_COLS).
   Mỗi dòng được xếp 1 trong 3 mức: ok (hợp lệ) · warn (cần kiểm tra) · err (lỗi). Lỗi MỨC FILE (không có dòng nào, không nhận ra cột then chốt…) chặn không cho nạp.
   Quy ước đọc số chung của app: "1,234" = 1234 · "(100)" = −100 · dấu chấm = thập phân. */
(function (root) {
  'use strict';
  const noAcc = (s) => String(s == null ? '' : s).normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/g, 'd').replace(/Đ/g, 'D').toLowerCase().replace(/\s+/g, ' ').trim();
  function num(v) {
    let s = String(v == null ? '' : v).trim(); if (s === '' || s === '.' || s === '-') return null;
    let neg = false; if (/^\(.*\)$/.test(s)) { neg = true; s = s.slice(1, -1); } s = s.replace(/\s/g, '').replace(/,/g, '');
    if (/\d\.$/.test(s)) s = s.slice(0, -1); // PC 3.69: ô định dạng #,###.## hiển thị số nguyên là "30." (dấu chấm cuối) → coi là 30
    if (!/^[-+]?\d*\.?\d+(e[-+]?\d+)?$/i.test(s)) return NaN; const n = Number(s); return neg ? -n : n;
  }
  const fmt = (n, d) => (n == null || isNaN(n) ? '' : Number(n).toLocaleString('en-US', { minimumFractionDigits: d || 0, maximumFractionDigits: d == null ? 2 : d }));
  const colIdx = (headers, name) => { const t = noAcc(name); return headers.findIndex((h) => noAcc(h) === t); };
  const mk = () => ({ errors: [], warns: [], infos: [], problems: [], stats: { total: 0, ok: 0, warn: 0, err: 0, kg: 0, items: 0 } });
  function addProblem(R, row, level, msg) { R.problems.push({ row, level, msg }); }
  function finish(R, levels) { // levels: Map dòng → mức nặng nhất
    let w = 0, e = 0; levels.forEach((lv) => { if (lv === 'err') e++; else if (lv === 'warn') w++; });
    R.stats.err = e; R.stats.warn = w; R.stats.ok = Math.max(0, R.stats.total - w - e); R.problems.sort((a, b) => (a.level === b.level ? a.row - b.row : (a.level === 'err' ? -1 : 1))); return R;
  }
  function levelOf(map, row, lv) { const cur = map.get(row); if (!cur || (lv === 'err' && cur !== 'err')) map.set(row, lv); }

  /** Tồn kho An An: headers = tên cột (đã dò dòng tiêu đề); rows = mảng các dòng (mảng ô, đã bỏ dòng "Tồn cuối" trống); opt = { requiredColumns, headerScore, headerRowNumber, dropped, firstRowNumber, dataDate } */
  function checkM1(headers, rows, opt) {
    opt = opt || {}; const R = mk(), req = opt.requiredColumns || [], lv = new Map(); headers = headers || []; rows = rows || [];
    const found = req.filter((c) => colIdx(headers, c) >= 0).length; R.stats.total = rows.length;
    const iMa = colIdx(headers, 'Mã hàng'), iTen = colIdx(headers, 'Tên hàng'), iTc = colIdx(headers, 'Tồn cuối'), iNet = colIdx(headers, 'Tồn cuối (NET)'), iHd = colIdx(headers, 'Hợp đồng'), iDt = colIdx(headers, 'Đặc tính');
    R.infos.push('Dòng tiêu đề nằm ở dòng ' + (opt.headerRowNumber || '?') + ' · khớp ' + found + '/' + (req.length || '?') + ' cột chuẩn.');
    if (!rows.length) R.errors.push('Không có dòng dữ liệu nào (sau khi bỏ các dòng "Tồn cuối" trống). Kiểm tra lại file / sheet.');
    if (req.length && found < Math.min(6, req.length)) R.errors.push('File không giống báo cáo tồn kho (chỉ khớp ' + found + '/' + req.length + ' cột chuẩn) — có thể chọn nhầm file hoặc sai sheet.');
    ['Mã hàng', 'Tồn cuối'].forEach((c) => { if (colIdx(headers, c) < 0) R.errors.push('Thiếu cột bắt buộc "' + c + '".'); });
    if (iTen < 0) R.warns.push('Thiếu cột "Tên hàng".'); if (iNet < 0) R.warns.push('Thiếu cột "Tồn cuối (NET)" — tổng kg sẽ không tính được.');
    if (opt.dropped > 0) R.infos.push('Đã bỏ ' + fmt(opt.dropped) + ' dòng hết tồn (cột "Tồn cuối" trống).');
    const seen = new Map(); let kg = 0; const items = new Set(); const base = opt.firstRowNumber || 1;
    rows.forEach((r, i) => {
      const n = opt.rowNums ? opt.rowNums[i] : base + i, ma = iMa >= 0 ? String(r[iMa] == null ? '' : r[iMa]).trim() : '';
      if (iMa >= 0) { if (!ma) { addProblem(R, n, 'warn', 'Mã hàng để trống'); levelOf(lv, n, 'warn'); } else items.add(ma); }
      if (iTc >= 0) { const t = num(r[iTc]); if (t !== null && isNaN(t)) { addProblem(R, n, 'err', 'Tồn cuối không phải số: "' + r[iTc] + '"'); levelOf(lv, n, 'err'); } else if (t !== null && t < 0) { addProblem(R, n, 'warn', 'Tồn cuối âm: ' + t); levelOf(lv, n, 'warn'); } }
      if (iNet >= 0) { const t = num(r[iNet]); if (t !== null && isNaN(t)) { addProblem(R, n, 'err', 'Tồn cuối (NET) không phải số: "' + r[iNet] + '"'); levelOf(lv, n, 'err'); } else if (t !== null) kg += t; }
      const key = [ma, iHd >= 0 ? r[iHd] : '', iDt >= 0 ? r[iDt] : ''].join('|').toLowerCase(); if (ma) { if (seen.has(key)) { addProblem(R, n, 'warn', 'Trùng mã hàng + hợp đồng + đặc tính với dòng ' + seen.get(key)); levelOf(lv, n, 'warn'); } else seen.set(key, n); }
    });
    R.stats.kg = kg; R.stats.items = items.size; return finish(R, lv);
  }

  /** Tồn kho gửi: rows = mảng object {maHang, tenHang, dacTinh, soLo, size, quyCach, ngayNhapKho, phieuNhap, soKien, trongLuong} (filteredRows); opt = { today:'yyyy-mm-dd' } */
  function checkM2(rows, opt) {
    opt = opt || {}; const R = mk(), lv = new Map(); rows = rows || []; R.stats.total = rows.length;
    if (!rows.length) R.errors.push('Không có dòng nào để nạp (chưa có "Dữ liệu đã lọc" khớp mẫu).');
    const seen = new Map(), items = new Set(); let kg = 0, kien = 0, zero = 0;
    rows.forEach((r, i) => {
      const n = i + 1, ma = String(r.maHang == null ? '' : r.maHang).trim();
      if (!ma) { addProblem(R, n, 'warn', 'Mã hàng để trống'); levelOf(lv, n, 'warn'); } else items.add(ma);
      const sk = num(r.soKien), tl = num(r.trongLuong);
      if (sk !== null && isNaN(sk)) { addProblem(R, n, 'err', 'Số kiện không phải số: "' + r.soKien + '"'); levelOf(lv, n, 'err'); } else if (sk !== null && sk < 0) { addProblem(R, n, 'warn', 'Số kiện âm: ' + sk); levelOf(lv, n, 'warn'); } else if (sk !== null) kien += sk;
      if (tl !== null && isNaN(tl)) { addProblem(R, n, 'err', 'Trọng lượng không phải số: "' + r.trongLuong + '"'); levelOf(lv, n, 'err'); } else if (tl !== null) { kg += tl; if (tl < 0) { addProblem(R, n, 'warn', 'Trọng lượng âm: ' + tl); levelOf(lv, n, 'warn'); } }
      if ((sk === null || sk === 0) && (tl === null || tl === 0)) zero++; // PC 4.97: dòng hết tồn cuối kỳ (0 kiện / 0 kg) là bình thường trong báo cáo Nhập-Xuất-Tồn → chỉ ghi chú, không tính 'cần kiểm tra'
      const d = String(r.ngayNhapKho == null ? '' : r.ngayNhapKho).trim(); if (d && opt.today) { const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(d) || (/^(\d{1,2})[/.-](\d{1,2})[/.-](\d{4})/.exec(d) ? ((x) => [0, x[3], x[2].padStart(2, '0'), x[1].padStart(2, '0')])(/^(\d{1,2})[/.-](\d{1,2})[/.-](\d{4})/.exec(d)) : null); if (m && (m[1] + '-' + m[2] + '-' + m[3]) > opt.today) { addProblem(R, n, 'warn', 'Ngày nhập kho ở tương lai: ' + d); levelOf(lv, n, 'warn'); } }
      const key = [ma, r.soLo, r.phieuNhap, r.size, r.quyCach, r.ngayNhapKho].map((x) => String(x == null ? '' : x).toLowerCase()).join('|'); if (ma) { if (seen.has(key)) { addProblem(R, n, 'warn', 'Trùng mã hàng + số lô + phiếu nhập với dòng ' + seen.get(key)); levelOf(lv, n, 'warn'); } else seen.set(key, n); }
    });
    if (zero > 0) R.infos.push(fmt(zero) + ' dòng đã hết tồn cuối kỳ (0 kiện / 0 kg) — bình thường, vẫn được nạp.');
    R.stats.kg = kg; R.stats.kien = kien; R.stats.items = items.size; return finish(R, lv);
  }

  /** So với dữ liệu đang có: cảnh báo khi số dòng / tổng kg thay đổi quá nhiều (dấu hiệu nạp nhầm file / nhầm ngày). cur / next = { rows, kg, items, date } */
  function compare(cur, next) {
    const out = { lines: [], warns: [] }; if (!cur || !(cur.rows > 0)) return out;
    const pct = (a, b) => (a > 0 ? Math.round((b - a) / a * 1000) / 10 : null);
    const dRows = pct(cur.rows, next.rows), dKg = pct(cur.kg, next.kg);
    out.lines = [['Số dòng', cur.rows, next.rows, dRows], ['Số mã hàng', cur.items, next.items, pct(cur.items, next.items)], ['Tổng số liệu (kg)', cur.kg, next.kg, dKg]];
    if (dRows != null && dRows <= -40) out.warns.push('Số dòng GIẢM ' + Math.abs(dRows) + '% so với dữ liệu đang có (' + fmt(cur.rows) + ' → ' + fmt(next.rows) + ') — kiểm tra có chọn nhầm file / sheet không.');
    if (dKg != null && Math.abs(dKg) >= 50) out.warns.push('Tổng số liệu ' + (dKg > 0 ? 'TĂNG ' : 'GIẢM ') + Math.abs(dKg) + '% so với dữ liệu đang có (' + fmt(cur.kg, 0) + ' → ' + fmt(next.kg, 0) + ' kg) — bất thường, nên kiểm tra trước khi nạp.');
    if (cur.date && next.date && cur.date === next.date) out.warns.push('Ngày số liệu trùng với dữ liệu đang có (' + next.date + ') — nạp file này sẽ THAY THẾ số liệu cùng ngày.');
    return out;
  }
  /** Nhật ký đã nạp: tìm lần nạp trước cùng file (trùng dấu vân tay) hoặc cùng ngày số liệu. log = [{id,time,user,host,fileName,sha,dataDate,status}] */
  function findDuplicate(log, sha, dataDate) {
    const live = (log || []).filter((x) => x && x.status !== 'undone').sort((a, b) => String(b.time).localeCompare(String(a.time)));
    const same = sha ? live.find((x) => x.sha === sha) : null, day = dataDate ? live.find((x) => x.dataDate && x.dataDate === dataDate && (!same || x.id !== same.id)) : null;
    return { sameFile: same || null, sameDate: day || null };
  }
  const newId = (d) => { d = d || new Date(); const p = (n) => String(n).padStart(2, '0'); return 'n' + d.getFullYear() + p(d.getMonth() + 1) + p(d.getDate()) + p(d.getHours()) + p(d.getMinutes()) + p(d.getSeconds()) + Math.random().toString(36).slice(2, 5); };
  const api = { num, noAcc, fmt, checkM1, checkM2, compare, findDuplicate, newId };
  if (typeof module !== 'undefined' && module.exports) module.exports = api; else root.NapExcelCore = api;
})(typeof window !== 'undefined' ? window : globalThis);
