/* NAP-EXCEL — NẠP EXCEL AN TOÀN cho Tồn kho An An (module1) và Tồn kho gửi (module2) (PC 3.12). Phần giao diện + nối dbAPI / napAPI (core/nap-excel.js). Kiểm tra dữ liệu: nap-excel-core.js.
   Quy trình 4 bước cho mỗi lần nạp:  1) chọn file  2) kiểm tra cấu trúc + dữ liệu  3) XEM TRƯỚC & XÁC NHẬN (3 mức: hợp lệ / cần kiểm tra / lỗi — lỗi nặng thì KHÔNG cho nạp)  4) nạp + ghi lịch sử.
   • Trước khi ghi: chụp lại dữ liệu cũ của các sheet bị thay (lưu trên máy này N ngày — mặc định 30, chọn 7 / 15 / 30) + cất BẢN GỐC file Excel (cả vào Quản lý file › Kho dữ liệu cá nhân).
   • Nhật ký mỗi đợt nạp (ai · máy nào · file nào · lúc nào · số dòng · tổng kg · dấu vân tay file) ghi ở sheet NapLog của chính database module → mọi máy cùng thấy; nạp lại đúng file đã nạp thì cảnh báo.
   • HOÀN TÁC: chỉ đợt nạp MỚI NHẤT chưa hoàn tác (đưa dữ liệu về đúng như trước khi nạp), làm được trên máy đã nạp đợt đó. */
(function (root) {
  'use strict';
  const Core = root.NapExcelCore, LOG_SHEET = 'NapLog', LS_DAYS = 'kla_nap_days', MAX_LOG = 300;
  const $ = (id) => document.getElementById(id);
  const esc = function (v) { return window.KLUtil.escLite(v); };
  const fmt = (n, d) => Core.fmt(n, d);
  const keepDays = () => { const n = +localStorage.getItem(LS_DAYS); return [7, 15, 30].indexOf(n) >= 0 ? n : 30; };
  const mb = (b) => (b / 1048576 < 0.1 ? Math.max(1, Math.round(b / 1024)) + ' KB' : (Math.round(b / 104857.6) / 10) + ' MB');
  const when = (iso) => { const d = new Date(iso); if (isNaN(d)) return ''; const p = (n) => String(n).padStart(2, '0'); return p(d.getDate()) + '/' + p(d.getMonth() + 1) + '/' + d.getFullYear() + ' ' + p(d.getHours()) + ':' + p(d.getMinutes()); };
  const hasDb = () => !!(root.dbAPI && root.dbAPI.readSheet && root.dbAPI.writeSheet);
  const hasNap = () => !!(root.napAPI && root.napAPI.save);
  const ngayIso = (s) => { s = String(s || '').trim(); let m = /^(\d{1,2})[/.-](\d{1,2})[/.-](\d{4})$/.exec(s); if (m) return m[3] + '-' + m[2].padStart(2, '0') + '-' + m[1].padStart(2, '0'); m = /^(\d{4})-(\d{2})-(\d{2})/.exec(s); return m ? m[1] + '-' + m[2] + '-' + m[3] : ''; };

  async function sha256(buf) { // dấu vân tay file (SHA-256); không có crypto.subtle thì dùng băm FNV-1a 2 vòng
    try { if (root.crypto && root.crypto.subtle) { const h = await root.crypto.subtle.digest('SHA-256', buf); return Array.from(new Uint8Array(h)).map((b) => b.toString(16).padStart(2, '0')).join(''); } } catch (e) { /* dùng bản dự phòng */ }
    const u = new Uint8Array(buf); let h1 = 0x811c9dc5, h2 = 0x01000193; for (let i = 0; i < u.length; i++) { h1 = Math.imul(h1 ^ u[i], 16777619) >>> 0; h2 = Math.imul(h2 + u[i], 2246822519) >>> 0; } return 'fnv' + h1.toString(16).padStart(8, '0') + h2.toString(16).padStart(8, '0') + u.length.toString(16);
  }
  async function readLog(mod) { try { const r = await root.dbAPI.readSheet(mod, LOG_SHEET); return r && r.ok && Array.isArray(r.data) ? r.data.filter((x) => x && x.id) : []; } catch (e) { return []; } }
  async function writeLog(mod, rows) { rows = rows.slice(-MAX_LOG); const r = await root.dbAPI.writeSheet(mod, LOG_SHEET, rows); if (!r || r.ok === false) throw new Error((r && r.error) || 'Không ghi được nhật ký nạp'); }
  async function who() { try { const r = await root.napAPI.who(); return r && r.ok ? r.value : { user: '', host: '' }; } catch (e) { return { user: '', host: '' }; } }

  // ---------- hộp thoại ----------
  function modal(html, cls) {
    const ov = document.createElement('div'); ov.className = 'nx-ov'; ov.innerHTML = '<div class="nx-dlg ' + (cls || '') + '" role="dialog" aria-modal="true">' + html + '</div>'; const host = document.fullscreenElement || document.body; host.appendChild(ov);
    const close = () => { ov.remove(); document.removeEventListener('keydown', onKey, true); };
    const onKey = (e) => { if (e.key === 'Escape') { e.stopPropagation(); ov.dispatchEvent(new CustomEvent('nx-esc')); } };
    document.addEventListener('keydown', onKey, true); return { ov, close, q: (s) => ov.querySelector(s) };
  }
  const pill = (cls, n, label) => '<div class="nx-k ' + cls + '"><b>' + fmt(n) + '</b><span>' + label + '</span></div>';

  /** XEM TRƯỚC & XÁC NHẬN. Trả về ticket (đã chụp bản hoàn tác) hoặc null nếu người dùng hủy / có lỗi chặn. */
  async function review(opt) {
    if (!hasDb()) return { id: '', module: opt.module, noLog: true };
    const C = opt.check, mod = opt.module, log = await readLog(mod), sha = opt.buf ? await sha256(opt.buf) : '';
    const dd = ngayIso(opt.dataDate), dup = Core.findDuplicate(log, sha, dd), cmp = Core.compare(opt.current ? Object.assign({}, opt.current, { date: ngayIso(opt.current.date) }) : null, { rows: C.stats.total, items: C.stats.items, kg: C.stats.kg, date: dd });
    const blocking = C.errors.length > 0, hardRows = C.stats.err > 0;
    const warns = [].concat(C.warns, cmp.warns, opt.notes || []);
    if (dup.sameFile) warns.unshift('File này ĐÃ ĐƯỢC NẠP lúc ' + when(dup.sameFile.time) + ' (' + (dup.sameFile.user || '?') + (dup.sameFile.host ? ' · máy ' + dup.sameFile.host : '') + '). Nạp lại sẽ thay thế đúng số liệu đó — chắc chắn chưa?');
    else if (dup.sameDate) warns.unshift('Ngày số liệu ' + opt.dataDate + ' đã được nạp lúc ' + when(dup.sameDate.time) + ' bằng file khác ("' + (dup.sameDate.fileName || '?') + '").');
    const sample = C.problems.slice(0, 40);
    const html = '<div class="nx-h"><h2>Xem trước khi nạp · ' + esc(opt.title) + '</h2><button type="button" class="nx-x" data-a="cancel" aria-label="Đóng">✕</button></div>'
      + '<div class="nx-b"><div class="nx-file"><b>' + esc(opt.file.name) + '</b> · ' + mb(opt.file.size || 0) + (opt.sheetCount ? ' · ' + opt.sheetCount + ' sheet' : '') + (opt.sheetName ? ' (đang nạp sheet “' + esc(opt.sheetName) + '”)' : '') + (opt.dataDate ? ' · ngày số liệu <b>' + esc(opt.dataDate) + '</b>' : ' · <span class="nx-w">chưa có ngày số liệu</span>') + '</div>'
      + '<div class="nx-ks">' + pill('ok', C.stats.ok, 'Dòng hợp lệ') + pill('warn', C.stats.warn, 'Dòng cần kiểm tra') + pill('err', C.stats.err, 'Dòng lỗi') + '</div>'
      + (C.infos.length ? '<div class="nx-inf">' + C.infos.map(esc).join('<br>') + '</div>' : '')
      + (C.errors.length ? '<div class="nx-box err"><b>Lỗi nặng — không nạp được:</b><ul>' + C.errors.map((m) => '<li>' + esc(m) + '</li>').join('') + '</ul></div>' : '')
      + (warns.length ? '<div class="nx-box warn"><b>Cần lưu ý (' + warns.length + '):</b><ul>' + warns.slice(0, 8).map((m) => '<li>' + esc(m) + '</li>').join('') + '</ul></div>' : '')
      + (cmp.lines.length ? '<table class="nx-t"><thead><tr><th>So với dữ liệu đang có</th><th class="r">Đang có</th><th class="r">File mới</th><th class="r">Chênh</th></tr></thead><tbody>' + cmp.lines.map((l) => '<tr><td>' + esc(l[0]) + '</td><td class="r">' + fmt(l[1], l[0].indexOf('kg') > 0 ? 0 : 0) + '</td><td class="r">' + fmt(l[2], 0) + '</td><td class="r ' + (l[3] != null && Math.abs(l[3]) >= 40 ? 'nx-w' : '') + '">' + (l[3] == null ? '' : (l[3] > 0 ? '+' : '') + l[3] + '%') + '</td></tr>').join('') + '</tbody></table>' : '')
      + (sample.length ? '<details class="nx-det"' + (hardRows ? ' open' : '') + '><summary>Các dòng cần xem (' + fmt(C.problems.length) + ')</summary><div class="nx-sc"><table class="nx-t"><thead><tr><th class="r">Dòng</th><th>Mức</th><th>Nội dung</th></tr></thead><tbody>' + sample.map((p) => '<tr><td class="r">' + p.row + '</td><td><span class="nx-lv ' + p.level + '">' + (p.level === 'err' ? 'Lỗi' : 'Kiểm tra') + '</span></td><td>' + esc(p.msg) + '</td></tr>').join('') + '</tbody></table></div>' + (C.problems.length > sample.length ? '<small>…và ' + fmt(C.problems.length - sample.length) + ' dòng khác.</small>' : '') + '</details>' : '')
      + (hardRows ? '<label class="nx-ack"><input type="checkbox" id="nxAck"> Tôi đã xem ' + fmt(C.stats.err) + ' dòng lỗi ở trên và vẫn muốn nạp.</label>' : '')
      + '<p class="nx-foot">Dữ liệu cũ được chụp lại trước khi nạp — hoàn tác được trong ' + keepDays() + ' ngày (nút “Lịch sử nạp”). Bản gốc file Excel cũng được cất vào Quản lý file.</p></div>'
      + '<div class="nx-f"><button type="button" class="nx-btn" data-a="cancel">Hủy — giữ dữ liệu cũ</button><button type="button" class="nx-btn pri" data-a="ok"' + (blocking || hardRows ? ' disabled' : '') + '>' + esc(blocking ? 'Không nạp được' : (hardRows || warns.length || C.stats.warn ? 'Vẫn nạp' : (opt.confirmText || 'Nạp dữ liệu'))) + '</button></div>';
    const M = modal(html); const ack = M.q('#nxAck'); if(ack) ack.addEventListener('change', () => { const ok = M.q('[data-a=ok]'); if(ok) ok.disabled = !ack.checked; });
    const choice = await new Promise((res) => { M.ov.addEventListener('click', (e) => { const b = e.target.closest('[data-a]'); if (b) res(b.dataset.a); }); M.ov.addEventListener('nx-esc', () => res('cancel')); }); M.close();
    if (choice !== 'ok') return null;
    // chụp bản hoàn tác + cất bản gốc
    const me = await who(), id = Core.newId(), ticket = { id, module: mod, title: opt.title, sha, fileName: opt.file.name, size: opt.file.size || 0, dataDate: opt.dataDate || '', hasUndo: false, sheets: opt.sheets || [], user: me.user, host: me.host, t0: new Date().toISOString(), warn: C.stats.warn, err: C.stats.err || '', dupFile: !!dup.sameFile, buf: opt.buf };
    if (hasNap() && ticket.sheets.length) {
      try {
        const before = {}; for (const sn of ticket.sheets) { const r = await root.dbAPI.readSheet(mod, sn); before[sn] = r && r.ok && Array.isArray(r.data) ? r.data : []; }
        const r = await root.napAPI.save({ module: mod, id, before, fileBuf: opt.buf, fileName: opt.file.name, meta: { title: opt.title, dataDate: opt.dataDate || '', rows: C.stats.total }, keepDays: keepDays(), maxMB: 600 });
        if (r && r.ok) ticket.hasUndo = true; else throw new Error((r && r.error && r.error.message) || 'không ghi được');
      } catch (e) {
        if (!confirm('KHÔNG lưu được bản hoàn tác (' + (e && e.message || e) + ').\n\nVẫn nạp dữ liệu? (đợt nạp này sẽ KHÔNG hoàn tác được)')) return null;
      }
    }
    return ticket;
  }
  /** Gọi SAU KHI dữ liệu đã ghi xong: ghi nhật ký + cất bản gốc vào Kho dữ liệu cá nhân. info = { rows, kg, items, sheet } */
  async function commit(ticket, info) {
    if (!ticket || !ticket.id || !hasDb()) return;
    info = info || {}; ticket.info = info;
    try {
      const log = await readLog(ticket.module); log.forEach((x) => { if (x.status === 'applied') x.status = 'replaced'; });
      log.push({ id: ticket.id, time: ticket.t0, user: ticket.user || '', host: ticket.host || '', fileName: ticket.fileName, size: ticket.size, sha: ticket.sha, rows: info.rows == null ? '' : info.rows, items: info.items == null ? '' : info.items, kg: info.kg == null ? '' : Math.round(info.kg * 100) / 100, dataDate: ticket.dataDate, sheet: info.sheet || '', status: 'applied', hasUndo: ticket.hasUndo ? 1 : 0, warn: ticket.warn || 0, err: ticket.err || 0 });
      await writeLog(ticket.module, log);
    } catch (e) { console.error('Không ghi được nhật ký nạp:', e); }
    ticket.buf = null; refreshBtn(ticket.module);
  }
  /** Đổi sheet / sửa thông tin của đợt nạp đang mở (VD người dùng chọn sheet khác của cùng file). */
  async function update(ticket, patch) {
    if (!ticket || !ticket.id || !hasDb()) return; try { const log = await readLog(ticket.module), x = log.find((r) => r.id === ticket.id); if (!x) return; Object.assign(x, patch || {}); await writeLog(ticket.module, log); } catch (e) { /* bỏ qua */ }
  }

  // ---------- lịch sử + hoàn tác ----------
  async function history(opt) {
    const mod = opt.module; if (!hasDb()) return;
    const M = modal('<div class="nx-h"><h2>Lịch sử nạp Excel · ' + esc(opt.title) + '</h2><button type="button" class="nx-x" data-a="close" aria-label="Đóng">✕</button></div><div class="nx-b" id="nxHb"><p class="nx-inf">Đang đọc…</p></div><div class="nx-f" id="nxHf"></div>', 'wide');
    const state = { busy: false }; M.ov.addEventListener('nx-esc', () => M.close());
    const paint = async () => {
      const log = (await readLog(mod)).sort((a, b) => String(b.time).localeCompare(String(a.time))), local = hasNap() ? await root.napAPI.list({ module: mod }).then((r) => (r && r.ok ? r.value : [])).catch(() => []) : [], have = {}; local.forEach((x) => { have[x.id] = x; });
      const usage = hasNap() ? await root.napAPI.usage().then((r) => (r && r.ok ? r.value : null)).catch(() => null) : null;
      const lastApplied = log.find((x) => x.status === 'applied'), tb = log.slice(0, 60).map((x) => {
        const st = x.status === 'applied' ? '<span class="nx-st ok">Đang dùng</span>' : (x.status === 'undone' ? '<span class="nx-st undone">Đã hoàn tác</span>' : '<span class="nx-st rep">Đã bị thay</span>');
        const can = lastApplied && lastApplied.id === x.id && have[x.id] && have[x.id].hasBefore;
        const why = x.status === 'applied' && !can ? (have[x.id] ? '' : '<small>' + (x.hasUndo ? 'bản hoàn tác nằm ở máy ' + esc(x.host || '?') + ' (hoặc đã quá hạn)' : 'không có bản hoàn tác') + '</small>') : '';
        return '<tr><td>' + esc(when(x.time)) + '</td><td>' + esc(x.fileName) + (x.sheet ? '<small>sheet ' + esc(x.sheet) + '</small>' : '') + '</td><td>' + esc(x.dataDate || '') + '</td><td class="r">' + fmt(x.rows) + '</td><td class="r">' + (x.kg === '' || x.kg == null ? '' : fmt(x.kg, 0)) + '</td><td>' + esc(x.user || '') + (x.host ? '<small>' + esc(x.host) + '</small>' : '') + '</td><td>' + st + '</td><td>' + (can ? '<button type="button" class="nx-btn sm" data-undo="' + esc(x.id) + '">Hoàn tác</button>' : why) + '</td></tr>';
      }).join('');
      $('nxHb') && ($('nxHb').innerHTML = (log.length ? '<div class="nx-sc tall"><table class="nx-t"><thead><tr><th>Lúc nạp</th><th>File</th><th>Ngày SL</th><th class="r">Dòng</th><th class="r">Tổng (kg)</th><th>Người · máy</th><th>Trạng thái</th><th></th></tr></thead><tbody>' + tb + '</tbody></table></div>' : '<p class="nx-inf">Chưa có đợt nạp nào được ghi (lịch sử bắt đầu từ lần nạp đầu tiên sau khi cập nhật bản này).</p>') + '<p class="nx-foot">Chỉ hoàn tác được đợt nạp <b>mới nhất đang dùng</b>, trên đúng máy đã nạp. Hoàn tác đưa dữ liệu về như TRƯỚC khi nạp file đó; bản gốc file Excel vẫn còn trong Quản lý file để nạp lại.</p>');
      $('nxHf') && ($('nxHf').innerHTML = '<span class="nx-us">Giữ bản hoàn tác: <select id="nxDays"><option value="7">7 ngày</option><option value="15">15 ngày</option><option value="30">30 ngày</option></select>' + (usage ? ' · đang dùng ' + mb(usage.bytes) + ' (' + usage.count + ' đợt)' : '') + '</span><button type="button" class="nx-btn sm" data-a="purge">Dọn bản cũ ngay</button><button type="button" class="nx-btn sm" data-a="open">Mở thư mục</button><button type="button" class="nx-btn" data-a="close">Đóng</button>'); const sel = $('nxDays'); if (sel) sel.value = String(keepDays());
    };
    await paint();
    M.ov.addEventListener('change', (e) => { if (e.target.id === 'nxDays') { try { localStorage.setItem(LS_DAYS, e.target.value); } catch (x) { /* bỏ qua */ } if (hasNap()) root.napAPI.purge({ days: +e.target.value, maxMB: 600 }).then(paint); } });
    M.ov.addEventListener('click', async (e) => {
      const b = e.target.closest('[data-a],[data-undo]'); if (!b || state.busy) return;
      if (b.dataset.a === 'close') return M.close(); if (b.dataset.a === 'open') { root.napAPI.open(); return; }
      if (b.dataset.a === 'purge') { await root.napAPI.purge({ days: keepDays(), maxMB: 600 }); return paint(); }
      if (b.dataset.undo) {
        const id = b.dataset.undo, log = await readLog(mod), x = log.find((r) => r.id === id); if (!x) return;
        if (!confirm('HOÀN TÁC đợt nạp lúc ' + when(x.time) + '?\n\nFile: ' + x.fileName + '\nDữ liệu sẽ trở về như TRƯỚC khi nạp file này.\n(Bản gốc file Excel vẫn còn trong Quản lý file để nạp lại khi cần.)')) return;
        state.busy = true; b.disabled = true; b.textContent = 'Đang hoàn tác…';
        try {
          const r = await root.napAPI.before({ module: mod, id }); if (!r || !r.ok) throw new Error((r && r.error && r.error.message) || 'Không đọc được bản hoàn tác');
          const data = r.value || {}, names = Object.keys(data); if (!names.length) throw new Error('Bản hoàn tác trống.');
          if (typeof root.dbAPI.writeSheets === 'function') { const w = await root.dbAPI.writeSheets(mod, data); if (!w || w.ok === false) throw new Error((w && w.error) || 'Không ghi được dữ liệu'); }
          else for (const sn of names) { const w = await root.dbAPI.writeSheet(mod, sn, data[sn]); if (!w || w.ok === false) throw new Error((w && w.error) || 'Không ghi được dữ liệu'); }
          const me = await who(), lg = await readLog(mod), cur = lg.find((q) => q.id === id); if (cur) { cur.status = 'undone'; cur.undoneAt = new Date().toISOString(); cur.undoneBy = me.user || ''; }
          const prev = lg.filter((q) => q.status === 'replaced' && String(q.time) < String(cur && cur.time)).sort((a, c) => String(c.time).localeCompare(String(a.time)))[0]; if (prev) prev.status = 'applied';
          await writeLog(mod, lg); M.close();
          if (typeof opt.onUndone === 'function') opt.onUndone(x); else root.location.reload();
        } catch (err) { state.busy = false; alert('Không hoàn tác được: ' + (err && err.message || err)); paint(); }
      }
    });
  }
  // nút nhỏ cố định ở góc trang để mở lịch sử (không đụng bố cục sẵn có của module)
  const btns = {};
  function refreshBtn(mod) { const b = btns[mod]; if (!b) return; readLog(mod).then((l) => { const x = l.filter((r) => r.status === 'applied').sort((a, c) => String(c.time).localeCompare(String(a.time)))[0]; b.title = x ? 'Lần nạp gần nhất: ' + when(x.time) + ' — ' + x.fileName : 'Lịch sử nạp Excel + hoàn tác'; }); }
  function mountButton(opt) {
    if (btns[opt.module]) return; const b = document.createElement('button'); b.type = 'button'; b.className = 'nx-fab'; b.textContent = '⟲ Lịch sử nạp'; b.onclick = () => history(opt); document.body.appendChild(b); btns[opt.module] = b; refreshBtn(opt.module);
  }
  root.NapExcel = { review, commit, update, history, mountButton, sha256, readLog };
})(typeof window !== 'undefined' ? window : globalThis);
