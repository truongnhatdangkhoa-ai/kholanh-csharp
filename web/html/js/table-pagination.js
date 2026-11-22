// ================= PHÂN TRANG DÙNG CHUNG (áp dụng cho MỌI bảng dữ liệu trong toàn app) =================
// Trước đây mỗi module tự viết lại renderPaginationControls() với chữ ký hàm khác nhau (1 bên nhận
// page/totalItems riêng, 1 bên gộp vào object info) — dễ lệch nhau khi sửa. Giờ chỉ còn ĐÚNG 1 bản ở
// đây, dùng chung cho toàn app. Muốn đổi cách hiển thị phân trang (số dòng/trang, kiểu nút...) sau
// này chỉ cần sửa file này, không phải lặp lại ở từng module.
//
// CÁCH DÙNG trong 1 module:
//   1. Có sẵn TABLE_PAGE_SIZE (mặc định 25) và renderPaginationControls(containerId, page, totalItems, onPageChange).
//   2. Giữ 1 biến "page" riêng cho từng bảng (module tự khai báo, ví dụ "let tonPage = 1;").
//   3. Trong hàm vẽ bảng: cắt dữ liệu theo trang bằng TABLE_PAGE_SIZE, vẽ xong thì gọi
//      renderPaginationControls('idKhungPhanTrang', page, tổngSốDòng, (p)=>{ page = p; veLaiBang(); });
//
// File này KHÔNG phụ thuộc vào bất kỳ biến/hàm nào khác của module — an toàn để dùng ở mọi nơi.
const TABLE_PAGE_SIZE = 25;
function renderPaginationControls(containerId, page, totalItems, onPageChange){
  const el = document.getElementById(containerId);
  if(!el) return;
  const totalPages = Math.max(1, Math.ceil(totalItems / TABLE_PAGE_SIZE));
  if(totalItems <= TABLE_PAGE_SIZE){ el.innerHTML = ''; return; }
  el.innerHTML = `
    <button type="button" class="btn btn-ghost btn-sm" id="${containerId}Prev" ${page<=1?'disabled':''}>← Trước</button>
    <span class="table-pagination-info">Trang ${page.toLocaleString('en-US')} / ${totalPages.toLocaleString('en-US')} (${totalItems.toLocaleString('en-US')} dòng)</span>
    <button type="button" class="btn btn-ghost btn-sm" id="${containerId}Next" ${page>=totalPages?'disabled':''}>Sau →</button>
  `;
  document.getElementById(containerId+'Prev').addEventListener('click', ()=> onPageChange(page-1));
  document.getElementById(containerId+'Next').addEventListener('click', ()=> onPageChange(page+1));
}
