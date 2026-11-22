/* ============================================================
   HỘP THOẠI "NGÀY BÁO CÁO" DÙNG CHUNG (Module 1 + Module 2)
   ============================================================
   window.showReportDateDialog() → Promise<string|null>
     - Trả về "dd/mm/yyyy" nếu người dùng bấm "Xuất Excel" (hoặc Enter).
     - Trả về null nếu bấm Hủy / Esc / click ra ngoài hộp (huỷ xuất file).

   Vì sao viết lại: bản cũ dựng bằng style inline với var(--panel) — biến này KHÔNG tồn tại
   trong ton-kho-an-an.css (module đó đặt tên là --surface) nên nền hộp bị "trong suốt", số liệu
   của bảng bên dưới lọt qua chồng lên chữ. Bản mới:
     • toàn bộ giao diện nằm ở class .rd-* trong css/theme-shared.css (nền đặc, không inline);
     • màu lấy từ --rd-* — mỗi module CSS tự map --rd-* về biến theme riêng của nó (cả sáng/tối);
     • vẫn dùng 3 ô riêng dd / mm / yyyy (không dùng input type="date" vì hiển thị theo ngôn ngữ
       hệ điều hành, có máy ra mm/dd/yyyy).
   ============================================================ */
(function(){
  'use strict';

  function pad2(n){ return String(n).padStart(2, '0'); }

  // Kiểm tra ngày có thật trên lịch (VD 31/02 bị loại, 29/02 chỉ hợp lệ năm nhuận).
  function isRealDate(d, m, y){
    const dt = new Date(y, m - 1, d);
    return dt.getFullYear() === y && dt.getMonth() === m - 1 && dt.getDate() === d;
  }

  // initial (tuỳ chọn): "dd/mm/yyyy" để điền sẵn — Module 1 truyền "Ngày số liệu" của Load Data.
  window.showReportDateDialog = function showReportDateDialog(initial){
    return new Promise(resolve => {
      const prevFocus = document.activeElement;
      const today = new Date();

      const overlay = document.createElement('div');
      overlay.className = 'rd-overlay';
      overlay.innerHTML = `
        <div class="rd-dialog" role="dialog" aria-modal="true" aria-labelledby="rdTitle">
          <div class="rd-head">
            <div class="rd-icon" aria-hidden="true">
              <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
                <rect x="3" y="4.5" width="18" height="16" rx="2.5"/><path d="M3 9.5h18M8 2.5v4M16 2.5v4"/>
              </svg>
            </div>
            <div>
              <h3 id="rdTitle" class="rd-title">Ngày báo cáo</h3>
              <p class="rd-sub">Chọn đúng ngày của số liệu báo cáo. File Excel xuất ra sẽ ghi ngày này, không tự lấy ngày hiện tại.</p>
            </div>
          </div>

          <div class="rd-fields">
            <label class="rd-field"><span>Ngày</span>
              <input type="number" class="rd-input" id="rdDD" min="1" max="31" inputmode="numeric" placeholder="dd" autocomplete="off"></label>
            <span class="rd-sep">/</span>
            <label class="rd-field"><span>Tháng</span>
              <input type="number" class="rd-input" id="rdMM" min="1" max="12" inputmode="numeric" placeholder="mm" autocomplete="off"></label>
            <span class="rd-sep">/</span>
            <label class="rd-field rd-field-year"><span>Năm</span>
              <input type="number" class="rd-input" id="rdYYYY" min="2000" max="2100" inputmode="numeric" placeholder="yyyy" autocomplete="off"></label>
          </div>

          <div class="rd-meta">
            <span class="rd-preview">Ghi vào file: <b id="rdPreview">—</b></span>
            <button type="button" class="rd-link" id="rdToday">Hôm nay</button>
          </div>
          <div class="rd-error" id="rdError" role="alert" hidden></div>

          <div class="rd-actions">
            <button type="button" class="rd-btn" id="rdCancel">Hủy</button>
            <button type="button" class="rd-btn primary" id="rdOk">Xuất Excel</button>
          </div>
        </div>`;
      document.body.appendChild(overlay);

      const $ = id => overlay.querySelector(id);
      const dd = $('#rdDD'), mm = $('#rdMM'), yyyy = $('#rdYYYY');
      const preview = $('#rdPreview'), errBox = $('#rdError');

      function setToday(){
        dd.value = pad2(today.getDate());
        mm.value = pad2(today.getMonth() + 1);
        yyyy.value = String(today.getFullYear());
        refresh();
      }
      function readParts(){
        const ds = String(dd.value || '').trim();
        const ms = String(mm.value || '').trim();
        const ys = String(yyyy.value || '').trim();
        return { ds, ms, ys, d: Number(ds), m: Number(ms), y: Number(ys) };
      }
      function validate(){
        const { ds, ms, ys, d, m, y } = readParts();
        if(!ds || !ms || !ys) return 'Vui lòng nhập đủ ngày, tháng và năm.';
        if(![d, m, y].every(Number.isInteger)) return 'Ngày, tháng, năm phải là số nguyên.';
        if(ys.length !== 4 || y < 2000 || y > 2100) return 'Năm phải gồm 4 chữ số (2000–2100).';
        if(m < 1 || m > 12) return 'Tháng phải từ 1 đến 12.';
        if(!isRealDate(d, m, y)) return `Tháng ${m}/${y} không có ngày ${d}.`;
        return '';
      }
      function refresh(){
        const { d, m, y, ds, ms, ys } = readParts();
        const ok = ds && ms && ys && Number.isInteger(d) && Number.isInteger(m) && Number.isInteger(y);
        preview.textContent = ok ? `${pad2(d)}/${pad2(m)}/${ys.padStart(4, '0')}` : '—';
        errBox.hidden = true;
        [dd, mm, yyyy].forEach(i => i.classList.remove('invalid'));
      }

      function close(result){
        document.removeEventListener('keydown', onKey, true);
        overlay.remove();
        if(prevFocus && typeof prevFocus.focus === 'function'){ try{ prevFocus.focus(); }catch(_){} }
        resolve(result);
      }
      function submit(){
        const msg = validate();
        if(msg){
          errBox.textContent = msg;
          errBox.hidden = false;
          [dd, mm, yyyy].forEach(i => i.classList.add('invalid'));
          const bad = !dd.value ? dd : (!mm.value ? mm : (!yyyy.value ? yyyy : null));
          (bad || dd).focus();
          return;
        }
        const { d, m, y } = readParts();
        close(`${pad2(d)}/${pad2(m)}/${y}`);
      }
      function onKey(e){
        if(e.key === 'Escape'){ e.preventDefault(); e.stopPropagation(); close(null); }
        else if(e.key === 'Enter' && !e.target.closest('#rdCancel, #rdToday')){ e.preventDefault(); e.stopPropagation(); submit(); }
        else if(e.key === 'Tab'){
          // Giữ focus trong hộp thoại.
          const f = overlay.querySelectorAll('input, button');
          const first = f[0], last = f[f.length - 1];
          if(e.shiftKey && document.activeElement === first){ e.preventDefault(); last.focus(); }
          else if(!e.shiftKey && document.activeElement === last){ e.preventDefault(); first.focus(); }
        }
      }
      document.addEventListener('keydown', onKey, true);

      // Tự nhảy sang ô kế tiếp khi gõ đủ số chữ số.
      dd.addEventListener('input', () => { refresh(); if(dd.value.length >= 2){ mm.focus(); mm.select(); } });
      mm.addEventListener('input', () => { refresh(); if(mm.value.length >= 2){ yyyy.focus(); yyyy.select(); } });
      yyyy.addEventListener('input', refresh);
      [dd, mm, yyyy].forEach(i => i.addEventListener('focus', () => i.select()));

      $('#rdToday').addEventListener('click', setToday);
      $('#rdCancel').addEventListener('click', () => close(null));
      $('#rdOk').addEventListener('click', submit);
      overlay.addEventListener('mousedown', e => { if(e.target === overlay) close(null); });

      const init = String(initial || '').match(/^(\d{1,2})\/(\d{1,2})\/(\d{4})$/);
      if(init && isRealDate(Number(init[1]), Number(init[2]), Number(init[3]))){
        dd.value = pad2(init[1]); mm.value = pad2(init[2]); yyyy.value = init[3];
        refresh();
      } else setToday();
      dd.focus();
    });
  };
})();
