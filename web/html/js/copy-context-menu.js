/* ============================================================
   COPY CONTEXT MENU — ĐỘNG CƠ DÙNG CHUNG CHO TOÀN BỘ APP
   ============================================================
   Vì sao cần file này: Electron KHÔNG tự vẽ menu chuột phải "Copy/Cut/Paste"
   như trình duyệt Chrome bình thường (đây là hành vi mặc định của Electron,
   không phải lỗi) — nên trước đây bấm chuột phải trong toàn bộ app không có
   gì hiện ra. Ngoài ra, các bảng dữ liệu (table.excel-select-enabled) đã cố
   ý tắt user-select để hỗ trợ kéo-chọn-ô kiểu Excel (xem cell-selected +
   lastRange trong từng file js/*.js), nên bôi đen chữ kiểu trình duyệt bình
   thường KHÔNG hoạt động trong các bảng đó — chỉ Ctrl+C mới copy được.

   File này (nạp 1 lần duy nhất, dùng chung mọi trang) thêm:
   1) Menu chuột phải thật cho MỌI nơi trong app.
   2) Trong bảng kiểu Excel: nút "Copy vùng đã chọn" chỉ đơn giản gọi
      document.execCommand('copy') — TÁI SỬ DỤNG NGUYÊN XI cơ chế Ctrl+C đã
      có sẵn trong từng module (mỗi module tự nghe sự kiện 'copy' và đọc
      lastRange của chính nó) — file này KHÔNG cần biết gì về lastRange hay
      cấu trúc bảng của từng module, nên không sửa file nào khác cả.
      Nếu chưa bôi đen ô nào, cho phép copy nhanh 1 ô hoặc 1 dòng dưới chuột.
   3) Trong khung chat AI (.ai-msg): copy đoạn đã bôi đen hoặc cả tin nhắn.
   4) Trong ô nhập liệu (input/textarea): Cắt/Copy/Dán/Chọn tất cả.
   5) Mọi chữ khác có thể bôi đen bình thường: copy đúng đoạn đã bôi đen.

   Không phụ thuộc file nào khác nạp trước/sau — có thể đặt thẻ <script> ở
   bất kỳ đâu trong <body>, miễn load 1 lần mỗi trang.
============================================================ */
(function(){

  /* ===== PC 3.28 — SAO CHÉP ĐƯỢC MỌI CHỮ TRONG APP (trừ token / mật khẩu) =====
     (a) Bôi đen LUÔN nhìn thấy: màu chọn xanh – chữ trắng, ép lên mọi theme (trước đây vài trang dùng trắng 25% → không thấy ở giao diện sáng).
     (b) Mọi chữ bôi đen được: user-select:text với độ ưu tiên 0 (:where) — chỗ nào CỐ Ý tắt (kéo chọn ô Excel, thanh kéo cột…) vẫn giữ nguyên.
     (c) Chuột phải vào bất kỳ chữ nào (tiêu đề, menu, nút, giá trị tĩnh / động) → "Sao chép chữ này" / "Sao chép cả mục".
     (d) Ctrl+C: nếu 1 module tự xử lý sự kiện copy rồi chặn mà không đưa gì vào clipboard → bù lại bằng đoạn đang bôi đen.
     (e) TOKEN / MẬT KHẨU không sao chép được: ô type=password, mọi thứ có id / name / class chứa token · pass · matkhau · secret · api-key, hoặc gắn data-no-copy. */
  (function injectBase(){
    try{
      if(document.getElementById('kl-copy-base')) return;
      const st = document.createElement('style'); st.id = 'kl-copy-base';
      st.textContent = ':where(html,body,body *){-webkit-user-select:text;user-select:text}\n'
        + '::selection,*::selection,input::selection,textarea::selection{background:#2f6df6 !important;color:#fff !important}\n';
      (document.head || document.documentElement).appendChild(st);
    }catch(e){ /* trang chưa có head → bỏ qua, các CSS riêng của trang vẫn chạy */ }
  })();

  const SECRET_RE = /token|pass|matkhau|mat_khau|secret|api[-_]?key/i;
  function isSecret(el){
    for(let n = el; n && n.nodeType === 1 && n !== document.documentElement; n = n.parentElement){
      if(n.hasAttribute && n.hasAttribute('data-no-copy')) return true;
      if(n.tagName === 'INPUT' && String(n.type || '').toLowerCase() === 'password') return true;
      const tag = (n.id || '') + ' ' + ((n.getAttribute && n.getAttribute('name')) || '') + ' ' + (typeof n.className === 'string' ? n.className : '');
      if(tag.trim() && SECRET_RE.test(tag)) return true;
    }
    return false;
  }
  const elOfNode = (nd) => nd ? (nd.nodeType === 1 ? nd : nd.parentElement) : null;
  document.addEventListener('copy', function(e){
    try{
      const sel = window.getSelection(), ae = document.activeElement;
      const inSecret = isSecret(elOfNode(sel && sel.anchorNode)) || (ae && (ae.tagName === 'INPUT' || ae.tagName === 'TEXTAREA') && isSecret(ae));
      if(inSecret){ e.preventDefault(); if(e.clipboardData) e.clipboardData.setData('text/plain', ''); }
    }catch(_e){ /* bỏ qua */ }
  }, true);
  window.addEventListener('copy', function(e){   // chạy SAU các handler của từng module
    try{
      if(!e.defaultPrevented || !e.clipboardData || e.clipboardData.getData('text/plain')) return;
      const sel = window.getSelection(), s = sel ? sel.toString() : '';
      if(!s.trim() || isSecret(elOfNode(sel.anchorNode))) return;
      e.clipboardData.setData('text/plain', s);
    }catch(_e){ /* bỏ qua */ }
  }, false);
  /** Chữ nằm đúng dưới con trỏ (đoạn chữ chứa con trỏ) + cả mục chứa nó. */
  function textUnder(ev){
    const t = ev.target; if(!(t instanceof Element) || isSecret(t)) return { word: '', block: '' };
    let word = '';
    try{ const r = document.caretRangeFromPoint ? document.caretRangeFromPoint(ev.clientX, ev.clientY) : null; if(r && r.startContainer && r.startContainer.nodeType === 3) word = String(r.startContainer.data || '').trim(); }catch(_e){ /* bỏ qua */ }
    if(!word){ const x = String(t.innerText || t.textContent || '').trim(); word = x.length <= 1000 ? x : ''; }
    const b = t.closest('li,tr,p,label,button,a,summary,h1,h2,h3,h4,h5,h6,td,th,div'); let block = b ? String(b.innerText || '').trim() : '';
    if(block.length > 4000) block = '';
    return { word: word.slice(0, 2000), block: block };
  }
  window.KLCopy = { isSecret, textUnder, copy: (s) => writeClipboardSafe(s) };
  function writeClipboardSafe(s){ return writeClipboard(s); }

  let menuEl = null;

  function closeMenu(){
    if(menuEl){ menuEl.remove(); menuEl = null; }
    document.removeEventListener('mousedown', onOutsideClick, true);
    document.removeEventListener('scroll', closeMenu, true);
    window.removeEventListener('resize', closeMenu);
    document.removeEventListener('keydown', onKeydown, true);
  }
  function onOutsideClick(e){ if(menuEl && !menuEl.contains(e.target)) closeMenu(); }
  function onKeydown(e){ if(e.key === 'Escape') closeMenu(); }

  async function writeClipboard(text){
    if(text == null) return;
    try{
      await navigator.clipboard.writeText(text);
      return;
    }catch(e){ /* rơi xuống cách dự phòng bên dưới */ }
    try{
      const ta = document.createElement('textarea');
      ta.value = text;
      ta.style.position = 'fixed';
      ta.style.top = '-1000px';
      ta.style.opacity = '0';
      document.body.appendChild(ta);
      ta.focus(); ta.select();
      document.execCommand('copy');
      ta.remove();
    }catch(e2){ console.error('Copy context menu: không copy được clipboard.', e2); }
  }

  function showMenu(x, y, items){
    closeMenu();
    if(!items || !items.length) return;
    menuEl = document.createElement('div');
    menuEl.className = 'kla-ctx-menu';
    items.forEach(it=>{
      if(it.separator){
        const sep = document.createElement('div');
        sep.className = 'kla-ctx-sep';
        menuEl.appendChild(sep);
        return;
      }
      const btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'kla-ctx-item';
      btn.disabled = !!it.disabled;
      btn.innerHTML = `<span class="kla-ctx-ico">${it.icon || '📋'}</span><span>${it.label}</span>`;
      btn.addEventListener('click', ()=>{
        closeMenu();
        if(!it.disabled && typeof it.action === 'function') it.action();
      });
      menuEl.appendChild(btn);
    });
    document.body.appendChild(menuEl);

    // Định vị theo con trỏ chuột, tự co lại nếu tràn mép màn hình.
    const vw = window.innerWidth, vh = window.innerHeight;
    const rect = menuEl.getBoundingClientRect();
    let left = x, top = y;
    if(left + rect.width > vw - 8) left = Math.max(8, vw - rect.width - 8);
    if(top + rect.height > vh - 8) top = Math.max(8, vh - rect.height - 8);
    menuEl.style.left = left + 'px';
    menuEl.style.top = top + 'px';

    // Trì hoãn 1 tick trước khi gắn listener đóng menu, tránh chính sự kiện
    // contextmenu/mousedown hiện tại kích hoạt đóng ngay lập tức.
    setTimeout(()=>{
      document.addEventListener('mousedown', onOutsideClick, true);
      document.addEventListener('scroll', closeMenu, true);
      window.addEventListener('resize', closeMenu);
      document.addEventListener('keydown', onKeydown, true);
    }, 0);
  }

  function currentSelectionText(){
    const sel = window.getSelection();
    const s = sel ? sel.toString() : '';
    return s.trim() ? s : '';
  }

  document.addEventListener('contextmenu', function(e){
    const target = e.target;
    if(!(target instanceof Element)) return;

    /* ---------- 0) Vùng tự có menu chuột phải riêng (PC 0.42) ----------
       Bảng Nhập Liệu (data-kla-own-ctx) tự vẽ menu của nó (nhân đôi dòng, chèn / di chuyển dòng-cột…).
       Trước đây handler này (gắn ở document, giai đoạn capture) chạy TRƯỚC handler của bảng nên hiện
       thêm menu Cắt/Copy/Dán → 2 popup chồng nhau. Nay bỏ qua hẳn: chỉ còn đúng 1 menu của bảng. */
    if(target.closest('[data-kla-own-ctx]')) return;

    /* ---------- 1) Bảng kiểu Excel (kéo-chọn-ô, xem cell-selected/lastRange) ---------- */
    const table = target.closest('table.excel-select-enabled');
    if(table){
      e.preventDefault();
      const items = [];
      const hasRangeSelected = !!table.querySelector('td.cell-selected');
      const td = target.closest('td');

      if(hasRangeSelected){
        items.push({
          label: 'Copy vùng đã chọn', icon: '📋',
          action: ()=> document.execCommand('copy')
        });
      }
      if(td && !td.querySelector('input, button, a, textarea, select')){
        items.push({
          label: hasRangeSelected ? 'Copy ô này (chỉ 1 ô)' : 'Copy ô này', icon: '📄',
          action: ()=> writeClipboard(td.textContent.trim())
        });
        const tr = td.closest('tr');
        if(tr){
          items.push({
            label: 'Copy cả dòng', icon: '📑',
            action: ()=> writeClipboard(Array.from(tr.cells).map(c=>c.textContent.trim()).join('\t'))
          });
        }
      }
      const th = target.closest('th');
      if(th && !th.querySelector('input, select, textarea') && !isSecret(th)){ items.push({ label: 'Copy tiêu đề cột', icon: '📄', action: ()=> writeClipboard(th.textContent.trim()) }); }
      if(!items.length){
        const u = textUnder(e);
        if(u.word) items.push({ label: 'Sao chép chữ này', icon: '📄', action: ()=> writeClipboard(u.word) });
      }
      if(!items.length){
        items.push({ label: 'Không có gì để copy', icon: '—', disabled: true, action(){} });
      }
      showMenu(e.clientX, e.clientY, items);
      return;
    }

    /* ---------- 2) Tin nhắn trong khung chat AI ---------- */
    const aiMsg = target.closest('.ai-msg');
    if(aiMsg){
      e.preventDefault();
      const selText = currentSelectionText();
      const items = [];
      if(selText && aiMsg.contains(window.getSelection().anchorNode)){
        items.push({ label: 'Copy đoạn đã chọn', icon: '📋', action: ()=> writeClipboard(selText) });
      }
      items.push({ label: 'Copy toàn bộ tin nhắn', icon: '💬', action: ()=> writeClipboard(aiMsg.innerText.trim()) });
      showMenu(e.clientX, e.clientY, items);
      return;
    }

    /* ---------- 3) Ô nhập liệu ---------- */
    if(target.tagName === 'INPUT' || target.tagName === 'TEXTAREA'){
      e.preventDefault();
      const el = target;
      const hasSel = el.selectionStart !== el.selectionEnd;
      const secret = isSecret(el);   // token / mật khẩu: không cho Cắt / Copy
      const items = [
        { label: 'Cắt', icon: '✂️', disabled: secret || !hasSel || el.readOnly || el.disabled, action: ()=> document.execCommand('cut') },
        { label: hasSel ? 'Copy' : 'Copy cả giá trị', icon: '📋', disabled: secret || (!hasSel && !String(el.value || '').length), action: ()=>{ if(hasSel) document.execCommand('copy'); else writeClipboard(String(el.value || '')); } },
        { label: 'Dán', icon: '📥', disabled: el.readOnly || el.disabled, action: ()=> document.execCommand('paste') },
        { separator: true },
        { label: 'Chọn tất cả', icon: '🔲', action: ()=> el.select() }
      ];
      showMenu(e.clientX, e.clientY, items);
      return;
    }

    /* ---------- 4) Mọi chữ khác ---------- */
    const selText = currentSelectionText();
    if(selText && !isSecret(elOfNode(window.getSelection().anchorNode))){
      e.preventDefault();
      showMenu(e.clientX, e.clientY, [
        { label: 'Copy', icon: '📋', action: ()=> writeClipboard(selText) }
      ]);
      return;
    }
    // PC 3.28: chưa bôi đen gì → vẫn sao chép được chữ dưới con trỏ (tiêu đề, menu, nút, giá trị tĩnh / động). Token / mật khẩu: không hiện menu.
    const u = textUnder(e);
    if(u.word){
      e.preventDefault();
      const items = [{ label: 'Sao chép chữ này', icon: '📄', action: ()=> writeClipboard(u.word) }];
      if(u.block && u.block !== u.word) items.push({ label: 'Sao chép cả mục', icon: '📑', action: ()=> writeClipboard(u.block) });
      showMenu(e.clientX, e.clientY, items);
      return;
    }

    // Click phải vào khoảng trống / nơi không có gì để copy -> không hiện menu.
  }, true);

})();
