/* ================= CONFIG ================= */
// Mỗi module 1 màu riêng (jewel-tone, hài hoà với nền tối) — dùng cho hiệu ứng rê chuột nổi
// 3D ở thanh menu (renderNav). "Báo Cáo" giữ đúng màu ice/frost đã dùng làm accent chính của
// toàn giao diện, các mục còn lại mỗi mục 1 sắc riêng để dễ phân biệt nhanh bằng mắt.
const MODULES = [
  {id:'loaddata', label:'Load Data',        icon:'upload', color:'#A78BFA'},
  {id:'dulieu',   label:'Dữ Liệu',          icon:'stack',  color:'#22D3EE'},
  {id:'baocao',   label:'Báo Cáo',           icon:'chart', color:'#60A5FA'},   // PC 1.10: xanh dương (trước ice #63E2F0 gần như trùng màu Dữ Liệu)
  // 8 module lọc bên dưới được GỘP CHUNG vào 1 mục menu chính duy nhất "Phân Loại" (field
  // group:'phanloai') — giống mẫu menu "Phân Loại" ở module "Tồn Kho Gửi": trên
  // thanh menu chỉ hiện 1 nút "Phân Loại", bấm vào sẽ hiện thanh menu con ngay trên bảng dữ
  // liệu để chọn từng module con. KHÔNG đổi id/thứ tự các module này — mọi chỗ khác trong file
  // (FILTER_MODULES, state.filterModules, các hàm tổng hợp "Báo Cáo"/"Đặc Tính"…) vẫn tham
  // chiếu thẳng theo id như cũ, chỉ có cách hiển thị trên menu là thay đổi.
  {id:'tpc',      label:'Thành Phẩm Chính', icon:'stack', color:'#34D399', group:'phanloai'},
  {id:'tptam',    label:'Thành Phẩm Tạm',   icon:'stack', color:'#F0A94E', group:'phanloai'},
  // Module "BM": module lọc theo 1 tiêu chí trên dữ liệu Load Data, xây dựng tương tự các module
  // lọc còn lại (Thành Phẩm Chính, HLSO, các module Tôm…) — cấu hình điều kiện lọc khai báo ở
  // FILTER_MODULES.bm bên dưới. Đặt NGAY SAU "Thành Phẩm Tạm" để khớp đúng thứ tự hiển thị trên
  // bảng "Báo Cáo Tổng Tồn".
  {id:'bm',       label:'Tôm BM',           icon:'stack', color:'#2DD4BF', group:'phanloai'},
  {id:'hlso',     label:'Tôm HLSO',         icon:'stack', color:'#FB7185', group:'phanloai'},
  {id:'tomgc',    label:'Tôm Gia Công',     icon:'stack', color:'#38BDF8', group:'phanloai'},
  {id:'tomnk',    label:'Tôm Nhập Khẩu',    icon:'stack', color:'#E879F9', group:'phanloai'},
  {id:'tomnl',    label:'Tôm Nguyên Liệu',  icon:'stack', color:'#A3E635', group:'phanloai'},
  {id:'tomgs',    label:'Tôm Giám Sát',     icon:'stack', color:'#FB923C', group:'phanloai'},
  // PC 1.24: TAB "Điều kiện lọc" — KHÔNG phải module lọc (không có trong FILTER_MODULES, `tab:true`): trang riêng liệt kê
  // từng bước lọc của 8 module Phân Loại (chính · phụ · ưu tiên Khách hàng · tự thêm · chuyển thủ công) kèm số dòng còn lại / bị loại.
  {id:'dkloc',    label:'Điều kiện lọc',    icon:'search', color:'#94A3B8', group:'phanloai', tab:true},
  // PC 3.92: tab "Theo Dõi Tồn" — gộp mã Thành Phẩm Chính GẦN HẾT + TỒN LÂU, kèm ghi chú lý do (js/theo-doi-ton.js). Chỉ PC.
  {id:'theodoi',  label:'Theo Dõi Tồn',     icon:'search', color:'#F87171'},
  // PC 2.49: menu "Tồn theo vị trí" ĐÃ TÁCH thành module riêng "Tồn Vị Trí An An" (database ton-vi-tri-an-an).
  // PC 1.62: menu "Vị Trí Bột" + tab nạp file Vị Trí Bột ĐÃ BỎ — tồn đẩy thẳng từ NXT Bột/Sốt › Tồn theo vị trí lên điện thoại; phiếu từ điện thoại vẫn tự trừ tồn (importPhonePhieuVTB).
  // Module "Đặc Tính": bảng tổng hợp theo cột "Đặc tính" của toàn bộ dữ liệu Load Data.
  {id:'dactinh',  label:'Đặc Tính',         icon:'edit', color:'#818CF8'},
  // Module "Tổng Hợp": chức năng sẽ xây dựng sau — hiện chỉ thêm chỗ vào menu, chưa gán logic.
  {id:'tonghop',  label:'Tổng Hợp',         icon:'file', color:'#94A3B8'},
];

/* ===== PHÂN QUYỀN PC 7.2 — Module 01 có 3 quyền riêng (giống điện thoại) =====
   M01 = Tồn An An (Load Data chính, Dữ Liệu, Phân Loại, Báo Cáo, Đặc Tính, Tổng Hợp) · M01VT = Tồn theo vị trí · M01VTB = Vị Trí Bột.
   Quyền lấy từ window.authAPI.state() (preload cấp cho cả iframe). Không có authAPI / chưa đăng nhập → m1Pc = null → cho phép hết
   (việc chặn cả module 01 đã do js/auth-gate.js ở trang chính lo). Menu Load Data mở được khi có ít nhất 1 quyền, bên trong chỉ hiện tab được phép. */
let m1Pc = null;
let m1CanWrite = true; // PC 7.45: vai trò được ghi dữ liệu (Quản lý / Quản trị) — cấp 1 / cấp 2 chỉ xem nên không cần menu nạp file
function m1Can(code){ return !m1Pc || m1Pc.indexOf(code) !== -1; }
function m1CodeOf(id){ return 'M01'; }
function m1ModAllowed(id){
  // PC 7.45: menu Load Data = nơi NẠP file. Có quyền M01 (Tồn An An) → luôn hiện. Chỉ có Tồn theo vị trí / Vị Trí Bột thì
  // CHỈ hiện cho vai trò ghi được (Quản lý / Quản trị — cần nạp file vị trí); cấp 1 / cấp 2 chỉ xem → ẩn hẳn, chỉ còn menu xem.
  if(id === 'loaddata') return m1Can('M01'); // PC 2.49: Tồn theo vị trí có module riêng (M01VT)
  return m1Can(m1CodeOf(id));
}
function m1FirstAllowed(){ const f = MODULES.find(m => m1ModAllowed(m.id)); return f ? f.id : null; }
function m1EnsureAllowedModule(){
  if(m1ModAllowed(state.activeModule)) return true;
  const f = m1FirstAllowed(); if(!f) return false;
  state.activeModule = f; return true;
}
async function m1LoadPerm(){
  try{
    const st = window.authAPI && window.authAPI.state ? await window.authAPI.state() : null;
    m1Pc = (st && st.ok && st.user && Array.isArray(st.user.pc)) ? st.user.pc.slice() : null;
    m1CanWrite = !(st && st.ok && st.user) || st.user.canWrite !== false;
  }catch(e){ m1Pc = null; }
}
/* PC 7.54 — bảng "Đặc Tính" / "Gộp Đặc Tính" và việc CHUYỂN MÃ HÀNG THỦ CÔNG giữa các module (Phân Loại) làm đổi số liệu báo cáo
   → chỉ vai trò Quản lý trở lên (m1CanWrite) được sửa. Người dùng cấp 1 / cấp 2 chỉ xem. Chặn ở CẢ giao diện (nút bị khóa) lẫn hàm gốc
   (moveRowsToModule / revertRowsToAuto / persistDacTinh) để không còn đường nào đổi được dữ liệu trong bộ nhớ. */
const M1_DT_LOCK_MSG = 'Bạn không được cấp quyền: chỉ vai trò Quản lý trở lên mới được sửa Đặc Tính / chuyển mã hàng giữa các module.';
function m1DtDenied(){
  if(m1CanWrite) return false;
  try{ alert(M1_DT_LOCK_MSG); }catch(e){ window.klLog && klLog('ton-kho-an-an.js:73', e); }
  return true;
}

const REQUIRED_COLUMNS = [
  "Item ID","Mã hàng","Tên hàng","Đặc tính","Hợp đồng","ĐVT","Tồn đầu","Nhập","Xuất",
  "Tồn cuối","Tồn cuối (NET)","Trọng lượng","Nguyên liệu","Qui cách đông","Tình trạng",
  "Hóa chất","Thị trường","Khách hàng","Mạ băng","Size","Qui cách gói"
];
const NUMERIC_COLS = new Set([
  "tồn đầu","nhập","xuất","tồn cuối","tồn cuối (net)","trọng lượng"
]);
const STORAGE_KEY = 'coldstock:inventory'; // chỉ dùng cho phương án dự phòng localStorage
// Ngưỡng "Sắp hết" (cột "Trạng thái", tab Thành Phẩm Chính) — cho phép người dùng nhập tay
// thay vì cố định = 8, lưu lại vào localStorage để giữ nguyên giữa các lần mở app.
const TPC_THRESHOLD_KEY = 'coldstock:tpc_low_stock_threshold';
const TPC_THRESHOLD_DEFAULT = 8;
function loadTpcThreshold(){
  try{
    const raw = localStorage.getItem(TPC_THRESHOLD_KEY);
    const n = Number(raw);
    return (raw !== null && Number.isFinite(n) && n >= 0) ? n : TPC_THRESHOLD_DEFAULT;
  }catch(e){ return TPC_THRESHOLD_DEFAULT; }
}
function saveTpcThreshold(n){
  try{ localStorage.setItem(TPC_THRESHOLD_KEY, String(n)); }catch(e){ /* bỏ qua lỗi lưu */ }
}
const LOADDATA_DB_SHEET = 'LoadData'; // sheet trong M01_TonKhoAnAn.xlsx lưu DỮ LIỆU THẬT (mỗi dòng Excel = 1 dòng)
const LOADDATA_META_SHEET = 'LoadDataMeta'; // sheet lưu metadata nhỏ (tên cột, tên file…) — 1 ô JSON, luôn nhỏ gọn
// Nhãn "Bột + Sốt + Bếp" là hàng NHẬP TAY, dùng chung ở CẢ 2 nơi: dòng đầu bảng "Báo Cáo
// Tổng Tồn" và bảng "Bảng Tổng Hợp HLSO" (module Báo Cáo) — cùng đọc/ghi 1 giá trị trong
// state.hlsoManualValues nên sửa ở đâu cũng đồng bộ nơi còn lại.
const BAOCAO_MANUAL_LABEL = 'Bột + Sốt + Bếp';
// Nhãn bảng nhập tay riêng "Báo Cáo Kho Gửi" — TÁCH RIÊNG thành 1 bảng nhỏ độc lập, không cộng
// vào "TỔNG TỒN KHO" của bảng "Báo Cáo Tổng Tồn" (khác với "Bột + Sốt + Bếp" — dòng đó vẫn nằm
// TRONG bảng chính và CÓ cộng vào tổng). Giá trị nhập tay lưu chung trong state.hlsoManualValues
// (khoá đã chuẩn hoá qua normalizeLabelForMatch) để tái dùng luôn cơ chế lưu/tải sẵn có, không
// cần sửa persist()/applyLoadDataMeta().
const BAOCAO_KHOGUI_LABEL = 'Báo Cáo Kho Gửi';
const PAGE_SIZE = 25;

// Cột hiển thị ở các menu lọc theo dữ liệu Load Data ("Thành Phẩm Chính", "HLSO"…)
const FILTER_COLUMNS = [
  "Mã hàng","Tên hàng","Size","Đặc tính","Hợp đồng","ĐVT","Tồn đầu","Nhập","Xuất",
  "Tồn cuối","Trọng lượng","Khách hàng","Thị trường","Mạ băng","Qui cách gói","Tình trạng"
];
let FILTER_PAGE_SIZE = 25;

// Cột cố định dùng khi XUẤT EXCEL (7 module lọc + "Báo Cáo Tổng Tồn") — khác với cột hiển thị
// trên bảng (FILTER_COLUMNS). Áp dụng cho MỌI file .xlsx xuất ra, bất kể module nào.
// "Tổng KL (kg)" không nằm trong danh sách này vì đây là cột ẢO (virtual), được tự động chèn
// ngay sau "Trọng lượng" — xem getFilterModuleExportColumns() bên dưới.
const EXPORT_COLUMNS = [
  "Mã hàng","Tên hàng","Size","Đặc tính","Hợp đồng","Tồn cuối","Trọng lượng"
];

// Tiền tố gắn thêm phía trước giá trị cột "Size" — CHỈ áp dụng cho các module bên dưới. Dùng
// chung cho cả bảng hiển thị trên giao diện lẫn khi xuất Excel để 2 bên luôn khớp nhau.
// - Tôm Gia Công ("TGC"), Tôm Nguyên Liệu ("NL"), Tôm Giám Sát ("TGS"): gắn thẳng tiền tố,
//   ví dụ "71/90" → "TGC 71/90".
// - HLSO ("BTP"): có 2 điều kiện loại trừ — xem applyHlsoSizePrefix() bên dưới.
const SIZE_PREFIX_BY_MODULE = { tomgc: 'TGC', tomnl: 'NL', tomgs: 'TGS' };

// Quy tắc riêng cho tab "HLSO": gắn tiền tố "BTP" vào Size, TRỪ 2 trường hợp:
// (1) Size kết thúc bằng "XX" (VD "3LXX", "6LXX") — giữ nguyên, không gắn.
// (2) Size có chứa dấu "/" (VD "91/120", "41/50") — giữ nguyên, không gắn.
// Các Size còn lại đều gắn "BTP ", riêng Size có số 0 đệm phía trước (VD "0020") thì bỏ các số
// 0 đệm đó trước khi gắn tiền tố — VD "0020" → "BTP 20".
function applyHlsoSizePrefix(val){
  const s = String(val).trim();
  if(/XX$/i.test(s)) return s;
  if(s.includes('/')) return s;
  const stripped = s.replace(/^0+(?=\d)/, '');
  return `BTP ${stripped}`;
}

function applySizePrefix(modId, label, val){
  if(val === '' || val === null || val === undefined) return val;
  if(label !== 'Size') return val;
  if(modId === 'hlso') return applyHlsoSizePrefix(val);
  const prefix = SIZE_PREFIX_BY_MODULE[modId];
  return prefix ? `${prefix} ${val}` : val;
}

// Hàm dựng sẵn để chọn giá trị mặc định cho 1 module lọc: giá trị phải chứa TẤT CẢ
// includeHints và KHÔNG chứa bất kỳ excludeHint nào (so khớp mờ, không phân biệt dấu).
// Giúp việc thêm module lọc mới (mỗi module ứng với 1 giá trị/nhóm giá trị riêng) chỉ
// cần khai báo 1 dòng, không phải viết lại logic so khớp.
function makeHintGuess(includeHints, excludeHints){
  includeHints = includeHints || [];
  excludeHints = excludeHints || [];
  return function(vals){
    return vals.find(v=>{
      const fn = fuzzyNormalize(v);
      return includeHints.every(h=>fn.includes(h)) && !excludeHints.some(h=>fn.includes(h));
    });
  };
}

// Cấu hình riêng cho từng module lọc theo 1 tiêu chí trên dữ liệu Load Data:
// - defaultCol: tên cột dùng để lọc mặc định (fuzzy match với header thực tế)
// - guess: chọn giá trị mặc định trong số các giá trị hiện có của cột đó
//
// LƯU Ý MỞ RỘNG: "HLSO" hiện đang gộp mọi giá trị "tạm" (VD: "Thành phẩm tạm") bằng
// 1 hint chung. Theo kế hoạch, phần giá trị "tạm" này sẽ được TÁCH ra thành nhiều module
// riêng biệt (mỗi module ứng với 1 giá trị "tạm" cụ thể). Khi đó, thêm module mới chỉ
// cần: (1) thêm 1 dòng vào MODULES, (2) thêm 1 entry vào FILTER_MODULES bên dưới với
// guess: makeHintGuess(['<hint riêng của giá trị đó>']) — không cần sửa bất kỳ hàm render/
// filter/sort/resize nào khác, vì toàn bộ đã dùng chung theo modId.
const FILTER_MODULES = {
  tpc: {
    columns: FILTER_COLUMNS,
    defaultCol: "Tình trạng",
    guess(vals){
      // Ưu tiên giá trị chứa "thành phẩm" nhưng KHÔNG chứa "tạm" (VD: "Thành phẩm", không phải "Thành phẩm tạm")
      return makeHintGuess(['thanh pham'], ['tam'])(vals)
          || makeHintGuess(['thanh pham'])(vals);
    },
    // Điều kiện phụ: loại trừ Mã hàng có ký tự vị trí 2–4 là "RBR" hoặc "CBR" — đây là điều
    // kiện Mã hàng dùng để nhận diện module "BM", thêm loại trừ ở đây để đảm bảo 2 module
    // không bao giờ trùng dữ liệu (dù Tình trạng của "BM" — chứa "Tạm"/bằng "T" — vốn đã khác
    // với Tình trạng "Thành phẩm" (không tạm) của module này).
    extraFilters: [
      {
        col: "Mã hàng",
        label: 'Mã hàng: loại trừ ký tự vị trí 2–4 là "RBR" hoặc "CBR" (tránh trùng với module BM)',
        test(row, ci){
          const val = String(row[ci] ?? '').trim().substring(1, 4).toUpperCase();
          return val !== 'RBR' && val !== 'CBR';
        },
      },
    ],
    // Module đã ổn định — tạm khóa bộ chọn tiêu chí lọc để tránh đổi nhầm cột/giá trị.
    locked: true,
  },
  tptam: {
    columns: FILTER_COLUMNS,
    // Điều kiện chính: lấy TOÀN BỘ dữ liệu Load Data, sau đó loại trừ mọi dòng đã thuộc về
    // BẤT KỲ module lọc nào khác (Thành Phẩm Chính, HLSO, các module Tôm…). Phần dữ liệu còn
    // lại — không trùng với module nào — chính là dữ liệu của module này. Vì dựa trên "tất cả
    // module khác", module này sẽ tự động cập nhật khi có module mới được thêm vào sau này —
    // không cần sửa lại cấu hình ở đây.
    excludeAllOtherModules: true,
  },
  hlso: {
    columns: FILTER_COLUMNS,
    // Điều kiện chính: cột "Tình trạng" chứa "Tạm" (VD: "Thành phẩm tạm") HOẶC bằng đúng "T".
    // Điều kiện chính này áp dụng cố định (không chọn qua dropdown) vì gồm nhiều giá trị cùng lúc.
    primaryMatchCol: "Tình trạng",
    primaryMatchLabel: 'Tình trạng chứa "Tạm" hoặc bằng "T"',
    primaryMatch(value){
      const fn = fuzzyNormalize(value);
      return fn.includes('tam') || fn === 't';
    },
    // Các điều kiện phụ (AND với nhau và với điều kiện chính ở trên).
    extraFilters: [
      {
        col: "Mã hàng",
        label: 'Mã hàng có ký tự vị trí 2–4 là "RHL"',
        test(row, ci){
          const val = String(row[ci] ?? '').trim();
          return val.substring(1, 4).toUpperCase() === 'RHL';
        },
      },
      {
        col: "Khách hàng",
        label: 'Khách hàng: không có khách hàng (để trống)',
        test(row, ci){
          return String(row[ci] ?? '').trim() === '';
        },
      },
      {
        col: "Mạ băng",
        label: 'Mạ băng: loại trừ dòng chứa "Tôm giám sát" (tránh trùng với module Tôm Giám Sát)',
        test(row, ci){
          return !fuzzyNormalize(row[ci]).includes(fuzzyNormalize('Tôm giám sát'));
        },
      },
    ],
    // Module đã ổn định — khóa cứng 3 điều kiện lọc tự động (điều kiện chính + 2 điều kiện
    // phụ ở trên) để tránh đổi nhầm. Vì module này dùng điều kiện chính cố định (primaryMatch,
    // không chọn qua dropdown cột/giá trị), khóa ở đây chỉ hiển thị huy hiệu "Đã khóa" xác nhận
    // các điều kiện đã cố định — không có gì để mở khóa qua giao diện. Muốn thêm mã hàng phát
    // sinh không khớp 3 điều kiện trên, dùng thanh "Chuyển mã hàng" (⇄ chuyển thủ công) có sẵn
    // ở mọi module lọc — chọn dòng ở module đang chứa nó rồi chuyển sang "HLSO", không cần và
    // không được sửa lại 3 điều kiện lọc tự động này.
    locked: true,
  },
  tomgc: {
    columns: FILTER_COLUMNS,
    // Điều kiện chính: cột "Tình trạng" chứa "Tạm" (VD: "Thành phẩm tạm") HOẶC bằng đúng "T".
    primaryMatchCol: "Tình trạng",
    primaryMatchLabel: 'Tình trạng chứa "Tạm" hoặc bằng "T"',
    primaryMatch(value){
      const fn = fuzzyNormalize(value);
      return fn.includes('tam') || fn === 't';
    },
    // Điều kiện phụ: cột "Khách hàng" đúng "TÂN PHONG PHÚ" (so khớp không phân biệt hoa/thường, có/không dấu).
    extraFilters: [
      {
        col: "Khách hàng",
        label: 'Khách hàng là "TÂN PHONG PHÚ"',
        test(row, ci){
          return fuzzyNormalize(row[ci]) === fuzzyNormalize('TÂN PHONG PHÚ');
        },
      },
      {
        col: "Mã hàng",
        label: 'Mã hàng có ký tự vị trí 2–4 là "RHL"',
        test(row, ci){
          const val = String(row[ci] ?? '').trim();
          return val.substring(1, 4).toUpperCase() === 'RHL';
        },
      },
    ],
    // Module đã ổn định — khóa cứng điều kiện chính + điều kiện phụ ở trên (chỉ hiển thị huy
    // hiệu "Đã khóa" xác nhận, không có gì để mở khóa qua giao diện vì đây là điều kiện chính
    // cố định). Mã hàng phát sinh không khớp điều kiện, dùng thanh "Chuyển mã hàng" (⇄) sẵn có.
    locked: true,
  },
  tomnk: {
    columns: FILTER_COLUMNS,
    // Điều kiện chính: cột "Tình trạng" chứa "Tạm" (VD: "Thành phẩm tạm") HOẶC bằng đúng "T".
    primaryMatchCol: "Tình trạng",
    primaryMatchLabel: 'Tình trạng chứa "Tạm" hoặc bằng "T"',
    primaryMatch(value){
      const fn = fuzzyNormalize(value);
      return fn.includes('tam') || fn === 't';
    },
    // Điều kiện phụ: cột "Khách hàng" là "ECUADOR" HOẶC "ẤN ĐỘ" (so khớp không phân biệt hoa/thường, có/không dấu).
    extraFilters: [
      {
        col: "Khách hàng",
        label: 'Khách hàng là "ECUADOR" hoặc "ẤN ĐỘ"',
        test(row, ci){
          const fn = fuzzyNormalize(row[ci]);
          return fn === fuzzyNormalize('ECUADOR') || fn === fuzzyNormalize('ẤN ĐỘ');
        },
      },
    ],
    // Module đã ổn định — khóa cứng điều kiện chính + điều kiện phụ ở trên.
    locked: true,
  },
  tomnl: {
    columns: FILTER_COLUMNS,
    // Điều kiện chính: cột "Tình trạng" chứa "Tạm" (VD: "Thành phẩm tạm") HOẶC bằng đúng "T".
    primaryMatchCol: "Tình trạng",
    primaryMatchLabel: 'Tình trạng chứa "Tạm" hoặc bằng "T"',
    primaryMatch(value){
      const fn = fuzzyNormalize(value);
      return fn.includes('tam') || fn === 't';
    },
    // Điều kiện phụ: cột "Mã hàng" có ký tự ở vị trí 2–4 (3 ký tự, tính từ 1) là "RHO",
    // và cột "Khách hàng" phải trống (không có khách hàng).
    extraFilters: [
      {
        col: "Mã hàng",
        label: 'Mã hàng có ký tự vị trí 2–4 là "RHO"',
        test(row, ci){
          const val = String(row[ci] ?? '').trim();
          return val.substring(1, 4).toUpperCase() === 'RHO';
        },
      },
      {
        col: "Khách hàng",
        label: 'Khách hàng: không có khách hàng (để trống)',
        test(row, ci){
          return String(row[ci] ?? '').trim() === '';
        },
      },
    ],
    // Module đã ổn định — khóa cứng điều kiện chính + điều kiện phụ ở trên.
    locked: true,
  },
  tomgs: {
    columns: FILTER_COLUMNS,
    // Điều kiện chính: cột "Tình trạng" chứa "Tạm" (VD: "Thành phẩm tạm") HOẶC bằng đúng "T".
    // (Người dùng gọi tắt là "Trạng thái" — dùng chung cột "Tình trạng" như các module Tôm khác.)
    primaryMatchCol: "Tình trạng",
    primaryMatchLabel: 'Tình trạng chứa "Tạm" hoặc bằng "T"',
    primaryMatch(value){
      const fn = fuzzyNormalize(value);
      return fn.includes('tam') || fn === 't';
    },
    // Điều kiện phụ: cột "Mạ băng" CHỨA "Tôm giám sát" (so khớp kiểu chứa, không phân biệt hoa/thường, có/không dấu).
    extraFilters: [
      {
        col: "Mạ băng",
        label: 'Mạ băng chứa "Tôm giám sát"',
        test(row, ci){
          return fuzzyNormalize(row[ci]).includes(fuzzyNormalize('Tôm giám sát'));
        },
      },
    ],
    // Module đã ổn định — khóa cứng điều kiện chính + điều kiện phụ ở trên.
    locked: true,
  },
  bm: {
    columns: FILTER_COLUMNS,
    // Điều kiện chính: cột "Tình trạng" chứa "Tạm" (VD: "Thành phẩm tạm") HOẶC bằng đúng "T".
    primaryMatchCol: "Tình trạng",
    primaryMatchLabel: 'Tình trạng chứa "Tạm" hoặc bằng "T"',
    primaryMatch(value){
      const fn = fuzzyNormalize(value);
      return fn.includes('tam') || fn === 't';
    },
    // Điều kiện phụ: cột "Mã hàng" có ký tự ở vị trí 2–4 (3 ký tự, tính từ 1) là "RBR" HOẶC
    // "CBR" (VD "VRBR…" → lấy "RBR", "VCBR…" → lấy "CBR"), và cột "Khách hàng" phải trống
    // (không có khách hàng).
    extraFilters: [
      {
        col: "Mã hàng",
        label: 'Mã hàng có ký tự vị trí 2–4 là "RBR" hoặc "CBR"',
        test(row, ci){
          const val = String(row[ci] ?? '').trim().substring(1, 4).toUpperCase();
          return val === 'RBR' || val === 'CBR';
        },
      },
      {
        col: "Khách hàng",
        label: 'Khách hàng: không có khách hàng (để trống)',
        test(row, ci){
          return String(row[ci] ?? '').trim() === '';
        },
      },
    ],
    // Module đã ổn định — khóa cứng điều kiện chính + điều kiện phụ ở trên.
    locked: true,
  },
};


const ICONS = {
  print: '<path d="M7 8V3h10v5"/><rect x="3" y="8" width="18" height="9" rx="2"/><path d="M7 14h10v7H7z"/>',
  'upload':'<path d="M12 16V4M8 8l4-4 4 4"/><path d="M4 16v3a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-3"/>',
  'clock':'<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3.5 2"/>',
  'file':'<path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8l-5-5Z"/><path d="M14 3v5h5"/>',
  'search':'<circle cx="11" cy="11" r="6.5"/><path d="M20 20l-4-4"/>',
  'trash':'<path d="M4 7h16M9 7V5a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2m-8 0 1 13a1 1 0 0 0 1 1h6a1 1 0 0 0 1-1l1-13"/>',
  'download':'<path d="M12 4v11M8 11l4 4 4-4"/><path d="M4 18v1a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-1"/>',
  'chevronL':'<path d="M14 6l-6 6 6 6"/>',
  'chevronR':'<path d="M10 6l6 6-6 6"/>',
  'chevronD':'<path d="M6 10l6 6 6-6"/>',
  'stack':'<path d="M12 3 3 8l9 5 9-5-9-5Z"/><path d="M3 12l9 5 9-5"/><path d="M3 16l9 5 9-5"/>',
  'chart':'<path d="M4 20V10M10 20V4M16 20v-7M22 20H2"/>',
  'lock':'<rect x="5" y="11" width="14" height="9" rx="2"/><path d="M8 11V7a4 4 0 0 1 8 0v4"/>',
  'edit':'<path d="M12 20h9"/><path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4Z"/>',
  'check':'<path d="M20 6 9 17l-5-5"/>',
  'pin':'<path d="M12 21s-7-6.2-7-11.5a7 7 0 0 1 14 0C19 14.8 12 21 12 21Z"/><circle cx="12" cy="9.5" r="2.5"/>',
};
function icon(name, cls){
  return `<svg class="${cls||'ic'}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round">${ICONS[name]||''}</svg>`;
}

/* ================= STATE ================= */
const state = {
  activeModule: 'loaddata',
  // Module con được chọn gần nhất bên trong nhóm menu "Phân Loại" — dùng để khi bấm lại nút
  // menu chính "Phân Loại" thì quay về đúng module con đang xem dở, thay vì luôn nhảy về module
  // đầu tiên (Thành Phẩm Chính).
  phanLoaiSub: 'tpc',
  dkLocMod: 'tpc', // PC 1.24: module đang xem ở tab "Điều kiện lọc"
  dkLast: { col: '', op: 'eq', from: 2, to: 4 }, // PC 1.24: cột / toán tử vừa dùng ở form thêm điều kiện (thêm nhiều điều kiện liên tiếp khỏi chọn lại)
  // Nhóm menu con "Phân Loại" hiện lồng ngay trong menu dọc bên trái (thay vì thanh ngang
  // phía trên bảng) — cờ này chỉ trạng thái đang mở/thu gọn của danh sách con đó.
  phanLoaiOpen: true,
  workbook: null,
  sheets: [],
  currentSheet: null,
  headers: [],
  rows: [],
  fileName: '',
  importedAt: null,
  dataDate: '',       // "Ngày số liệu" dd/mm/yyyy — xem parseDataDate()
  headerRowInfo: null,
  search: '',
  page: 1,
  selected: new Set(),
  sortCol: null,
  sortDir: 'asc',
  colWidths: {},
  rowHeights: {}, // { [chỉ số dòng trong state.rows]: chiều cao (px) người dùng đã kéo }

  // ---- tìm kiếm riêng theo từng cột ở bảng Load Data (giống hệt cơ chế th-filter-btn đã
  // dùng ở 7 module lọc bên dưới — đồng bộ giao diện, không ảnh hưởng ô tìm kiếm chung
  // "searchInput" đã có sẵn, 2 cơ chế lọc CỘNG DỒN với nhau). ----
  colFilters: {},
  filterOpenCols: new Set(),
  // Ô tìm kiếm "chứa" riêng theo từng cột (dòng ngay dưới tiêu đề, cạnh nút lọc icon phễu) —
  // khoá = chỉ số cột (ci), giá trị = chuỗi đang gõ. CỘNG DỒN với colFilters ở trên và ô tìm
  // kiếm chung "searchInput", giống hệt cơ chế đã dùng ở bảng nguồn "Xuất Hàng".
  colSearch: {},

  // ---- các module lọc theo 1 tiêu chí trên dữ liệu Load Data (Thành Phẩm Chính, HLSO…) ----
  filterModules: {},

  // ---- chuyển mã hàng thủ công qua lại giữa 7 module: { [chỉ số dòng trong state.rows]: modId } ----
  // Khi 1 dòng có mặt trong bảng này, dòng đó CHỈ thuộc đúng module được chỉ định (bỏ qua mọi
  // điều kiện lọc tự động), dùng cho cả việc "kéo" 1 dòng vào module khác lẫn "loại" nó ra khỏi
  // module đang tự động khớp.
  manualAssign: {},

  // ---- giá trị nhập tay cho các hàng "cho nhập tay" trên Bảng Tổng Hợp HLSO (module Báo Cáo) ----
  // khoá = nhãn hàng đã chuẩn hoá (normalizeLabelForMatch), giá trị = số hoặc null.
  hlsoManualValues: {},

  // (Đã bỏ baocaoManualEditing/khoGuiManualEditing — dòng "Bột + Sốt + Bếp" và bảng "Báo Cáo
  // Kho Gửi" ở "Báo Cáo Tổng Tồn" giờ CHỈ HIỂN THỊ, không còn sửa trực tiếp tại đó. Chế độ sửa
  // giờ nằm ở state.ngoaiSoSachEditing, dùng cho tab "Tổng Hợp > Báo cáo ngoài sổ sách".)

  // ---- lọc theo "Size" ở bảng "Bảng Tổng Hợp HLSO" (module Báo Cáo) — chỉ lọc theo cột nhãn
  // dòng (cột duy nhất có nội dung chữ; các cột còn lại đều là số tính toán, không có ý nghĩa
  // để lọc theo giá trị) — dùng chung popup checkbox kiểu Excel AutoFilter (khoá cố định
  // "hlsoTitle") như Load Data và 7 module lọc, đồng bộ giao diện. ----
  hlsoSummaryColFilters: {},

  // ---- ẩn/hiện các cột thị trường KHÔNG có số liệu ở bất kỳ dòng nào trong "Bảng Tổng Hợp
  // HLSO" (mặc định ẨN cho gọn bảng, có nút "Hiện cột trống" để bật lại khi cần xem đủ). ----
  hlsoHideEmptyCols: true,

  // ---- chọn dòng (checkbox) ở bảng "Đặc Tính" — CHỈ tồn tại trong phiên làm việc hiện tại
  // (KHÔNG lưu vào localStorage), dùng chỉ số hàng trong danh sách getDacTinhBreakdown().
  dactinhSelected: new Set(),

  // ---- gán 1 giá trị "Đặc Tính" vào 1 trong 11 nhóm gộp cố định ở bảng "Gộp Đặc Tính" —
  // { [nhãn Đặc Tính đã chuẩn hoá]: tên nhóm } — CÓ lưu vào localStorage (persist) vì đây là
  // cấu hình gộp lâu dài, không phải trạng thái chọn tạm thời.
  dactinhGroupAssign: {},
  // { [nhãn đã chuẩn hoá]: nhãn gốc } — TOÀN BỘ nhãn "Đặc Tính" TỪNG xuất hiện trong dữ liệu,
  // CÓ lưu vào localStorage (persist), KHÔNG BAO GIỜ bị xoá dòng nào (chỉ được thêm mới) — kể
  // cả khi dữ liệu nguồn (Load Data) bị xoá hoặc thay đổi không còn nhãn đó nữa. Đây là cơ chế
  // đảm bảo module "Đặc Tính" (và các đặc tính đã gộp ở "Gộp Đặc Tính") không bao giờ mất dữ
  // liệu, chỉ có thể tích luỹ thêm theo thời gian.
  dactinhKnownLabels: {},

  // ---- tìm kiếm + sắp xếp ở bảng "Đặc Tính" — CHỈ tồn tại trong phiên làm việc hiện tại
  // (KHÔNG lưu vào localStorage).
  dactinhSearch: '',
  dactinhSortDir: 'asc',   // 'asc' = A→Z, 'desc' = Z→A

  // ---- ngưỡng "Sắp hết" cho cột "Trạng thái" (tab Thành Phẩm Chính) — người dùng nhập tay,
  // CÓ lưu vào localStorage (xem loadTpcThreshold/saveTpcThreshold) để giữ nguyên giữa các
  // lần mở app. Mặc định = TPC_THRESHOLD_DEFAULT (8) khi chưa từng chỉnh.
  tpcLowStockThreshold: loadTpcThreshold(),

  // ---- module "Tổng Hợp" > "Báo cáo ngoài sổ sách" — 2 bảng nhập tay LIÊN KẾT TRỰC TIẾP với
  // dòng "Bột + Sốt + Bếp"/"Kho Gửi" ở bảng "Báo Cáo Tổng Tồn" (module Báo Cáo): đọc/ghi CÙNG
  // 1 giá trị trong state.hlsoManualValues (khoá qua normalizeLabelForMatch(BAOCAO_MANUAL_LABEL)/
  // normalizeLabelForMatch(BAOCAO_KHOGUI_LABEL)) — sửa ở đây tự động cập nhật bên "Báo Cáo Tổng
  // Tồn" và ngược lại, vì đây là tab THAY THẾ cho việc nhập tay trực tiếp ở "Báo Cáo Tổng Tồn"
  // (xem renderBaoCao — 2 dòng đó giờ CHỈ HIỂN THỊ, không còn nút Sửa/Xác nhận tại đó nữa).
  // Chế độ sửa từng bảng — NẰM RIÊNG cho từng bảng, CHỈ tồn tại trong phiên làm việc hiện tại.
  ngoaiSoSachEditing: { botsotbep: false, khogui: false },

  // ---- module "Tổng Hợp" > menu con đang chọn ----
  // 'xuathang' = "Xuất Hàng" (đã có chức năng đầy đủ), 'ngoaisoSach' = "Báo cáo ngoài sổ sách"
  // (mới thêm chỗ vào menu, CHƯA gán logic — sẽ xây dựng sau). Reset mỗi lần mở lại app (không
  // lưu localStorage) — chỉ ảnh hưởng tab đang xem, không ảnh hưởng dữ liệu.
  tonghopSubTab: 'xuathang',
  // Module NGUỒN đang chọn để xem/tạo phiếu xuất (1 trong các module lọc: tpc, tptam, bm,
  // hlso, tomgc, tomnk, tomnl, tomgs) — CHỈ được chọn 1 module tại 1 thời điểm, không gộp
  // nhiều module lại. CHỈ tồn tại trong phiên làm việc hiện tại.
  xuatHangSourceModule: null,
  // Các dòng đang được tick chọn để đưa vào phiếu xuất mới: { [chỉ số dòng trong state.rows]: SL xuất }
  xuatHangSelected: {},
  // Tìm kiếm riêng theo TỪNG CỘT ở bảng nguồn "Xuất Hàng" (dòng ô nhập ngay dưới tiêu đề) —
  // khoá = tên cột (label), giá trị = chuỗi đang gõ. Lọc kiểu "chứa" (không phân biệt hoa/
  // thường/dấu), CỘNG DỒN giữa các cột. Reset về rỗng mỗi khi đổi module nguồn.
  xuatHangColSearch: {},
  // Bộ lọc theo cột kiểu Excel AutoFilter (icon phễu, checkbox nhiều giá trị) cho bảng nguồn
  // "Xuất Hàng" — CỘNG DỒN với ô tìm kiếm "chứa" (xuatHangColSearch) ở trên, khoá theo tên cột
  // (label). Reset về rỗng mỗi khi đổi module nguồn (giống xuatHangColSearch).
  xuatHangColFilters: {},
  // Trang hiện tại của bảng nguồn "Xuất Hàng" — phân trang giống 7 module lọc (FILTER_PAGE_SIZE
  // dòng/trang) để tránh vẽ hàng nghìn dòng cùng lúc gây giật/lag. Reset về 1 khi đổi module
  // nguồn hoặc khi gõ vào ô tìm kiếm theo cột.
  xuatHangPage: 1,
  xuatHangSearch: '',
  // Danh sách phiếu xuất đã tạo — CÓ lưu vào database Excel (persistXuatHang/restoreXuatHang).
  // Mỗi phiếu: { id, moduleId, moduleLabel, createdAt, lines:[{i, maHang, tenHang, size,
  // hopDong, qty}], deducted, deductedAt }. "i" là chỉ số dòng trong state.rows tại thời điểm
  // tạo phiếu — dùng đúng quy ước đã có sẵn của state.manualAssign.
  xuatSlips: [],
  xuatSlipSeq: 1,
};

// Khởi tạo state riêng cho từng module lọc, dựa theo FILTER_MODULES đã khai báo ở trên.
function makeFilterModuleState(modId){
  return {
    filterCol: null,   // chỉ số cột dùng để lọc (null = tự chọn theo defaultCol)
    filterVal: '',      // giá trị lọc đang chọn (rỗng = tự chọn theo guess())
    search: '',
    page: 1,
    sortCol: null,
    sortDir: 'asc',
    colFilters: {},
    // Ô tìm kiếm "chứa" riêng theo từng cột (dòng ngay dưới tiêu đề) — khoá = chỉ số cột thật
    // (ci) hoặc '__tpcStatus' cho cột ảo "Trạng thái". CỘNG DỒN với colFilters + search chung.
    colSearch: {},
    filterOpenCols: new Set(),
    colWidths: {},
    rowHeights: {}, // { [chỉ số dòng trong state.rows]: chiều cao (px) người dùng đã kéo }
    notesCollapsed: true, // ẩn/hiện khối ghi chú (nguồn dữ liệu, điều kiện chính/phụ) — mặc định ẩn để dành chỗ làm việc
    selected: new Set(),  // các dòng (chỉ số trong state.rows) đang được chọn để chuyển qua module khác
    // Bấm vào huy hiệu "⇄ N chuyển thủ công" để BẬT/TẮT chế độ chỉ hiện các dòng đã chuyển
    // thủ công vào module này — giúp tìm nhanh mà không cần cuộn/tìm thủ công trong bảng.
    // CHỈ tồn tại trong phiên làm việc hiện tại (KHÔNG lưu vào localStorage).
    showManualOnly: false,
    // Điều kiện lọc TỰ THÊM qua nút "+ Thêm điều kiện" (xem chú thích đầy đủ ở
    // persistCustomFilters()/restoreCustomFilters() bên dưới). THAM CHIẾU tới đúng mảng dùng
    // chung trong customFiltersStore (không phải bản sao) — để không bị mất khi nạp lại Load
    // Data (makeFilterModuleState() bị gọi lại, tạo state MỚI, nhưng mảng điều kiện vẫn giữ
    // nguyên vì cùng tham chiếu). Mỗi phần tử: {id, colLabel, op, value}.
    customFilters: modId ? customFiltersFor(modId) : [],
  };
}
// ============================================================
// ĐIỀU KIỆN LỌC TỰ THÊM — cho phép người dùng tự thêm 1 điều kiện phụ đơn giản (cột + toán tử +
// giá trị) qua giao diện "+ Thêm điều kiện", KHÔNG cần sửa code, áp dụng CỘNG DỒN (AND) với các
// điều kiện chính/phụ đã khóa sẵn của module — không đụng/xóa được các điều kiện gốc.
// Lưu bền vào database Excel của module (sheet riêng "DieuKienTuThem", 1 ô JSON — giống cách
// "Xuất Hàng"/state.xuatSlips đang lưu ở sheet PhieuXuat), dự phòng localStorage khi không chạy
// trong Electron (window.dbAPI không tồn tại) — xem persistCustomFilters()/restoreCustomFilters().
// ============================================================
const CUSTOM_FILTER_STORAGE_KEY = 'coldstock:customFilters_v1'; // chỉ dùng cho phương án dự phòng localStorage
const CUSTOM_FILTER_DB_SHEET = 'DieuKienTuThem';
const CUSTOM_FILTER_OPS = [
  ['eq','bằng'], ['neq','khác'], ['contains','chứa'], ['notcontains','không chứa'],
  ['startswith','bắt đầu bằng'], ['endswith','kết thúc bằng'],
  ['in','thuộc danh sách'], ['notin','không thuộc danh sách'],
  ['pos_in','ký tự vị trí … thuộc'], ['pos_notin','ký tự vị trí … không thuộc'],
  ['gt','lớn hơn'], ['gte','lớn hơn hoặc bằng'], ['lt','nhỏ hơn'], ['lte','nhỏ hơn hoặc bằng'],
  ['empty','để trống'], ['notempty','không để trống'],
];
// PC 1.24: danh sách giá trị ngăn bằng ; hoặc | (VD "RBR; CBR")
function customFilterList(v){ return String(v ?? '').split(/[;|\n]/).map(x=>x.trim()).filter(Boolean); }
function customFilterOpIsPos(op){ return op==='pos_in' || op==='pos_notin'; }
// Mô tả 1 điều kiện tự thêm bằng chữ (dùng ở chip + tab "Điều kiện lọc")
function customFilterDesc(cf){
  const op = cf.op;
  if(customFilterOpIsPos(op)){
    const f = parseInt(cf.from,10)||1, t = parseInt(cf.to,10)||f;
    return `${cf.colLabel}: ký tự vị trí ${f===t?f:f+'–'+t} ${op==='pos_in'?'thuộc':'không thuộc'} [${customFilterList(cf.value).join(', ')}]`;
  }
  if(op==='in' || op==='notin') return `${cf.colLabel} ${customFilterOpLabel(op)} [${customFilterList(cf.value).join(', ')}]`;
  return `${cf.colLabel} ${customFilterOpLabel(op)}${customFilterOpNeedsValue(op)?` "${cf.value}"`:''}`;
}
function customFilterOpLabel(op){ return (CUSTOM_FILTER_OPS.find(o=>o[0]===op)||[])[1] || op; }
function customFilterOpNeedsValue(op){ return op!=='empty' && op!=='notempty'; }
// Kho lưu điều kiện tự thêm theo TỪNG modId — TÁCH RIÊNG khỏi state.filterModules (vốn bị tạo
// lại HOÀN TOÀN mỗi khi nạp lại Load Data, xem makeFilterModuleState()) để không bị mất khi nạp
// lại file. state.filterModules[modId].customFilters chỉ THAM CHIẾU tới đúng mảng ở đây.
let customFiltersStore = {};
function customFiltersFor(modId){
  if(!customFiltersStore[modId]) customFiltersStore[modId] = [];
  return customFiltersStore[modId];
}
function buildCustomFiltersPayload(){
  const out = {};
  Object.keys(FILTER_MODULES).forEach(id=>{ out[id] = customFiltersFor(id); });
  return out;
}
// Ghi ĐÈ nội dung từng mảng (length=0 rồi push lại) thay vì gán lại tham chiếu mới — để mọi nơi
// đang giữ tham chiếu cũ (state.filterModules[id].customFilters) tự động thấy dữ liệu mới nhất.
function applyCustomFiltersPayload(d){
  if(!d || typeof d !== 'object') return;
  Object.keys(FILTER_MODULES).forEach(id=>{
    const arr = customFiltersFor(id);
    arr.length = 0;
    if(Array.isArray(d[id])) d[id].forEach(cf=>arr.push(cf));
  });
}
async function persistCustomFilters(){
  const payload = buildCustomFiltersPayload();
  if(hasDbAPI()){
    try{
      const res = await window.dbAPI.writeSheet('ton-kho-an-an', CUSTOM_FILTER_DB_SHEET, [{ data: '__JSON__' + JSON.stringify(payload) }]);
      if(res && res.ok) return; // ghi database Excel thành công — xong, không cần fallback
      console.error('Lỗi lưu điều kiện lọc tự thêm vào database Excel:', res && res.error);
    }catch(e){ console.error('Lỗi lưu điều kiện lọc tự thêm vào database Excel:', e); }
  }
  try{ localStorage.setItem(CUSTOM_FILTER_STORAGE_KEY, JSON.stringify(payload)); }
  catch(e){ console.error('Lỗi lưu điều kiện lọc tự thêm (dự phòng localStorage):', e); }
}
async function restoreCustomFilters(){
  if(hasDbAPI()){
    try{
      const res = await window.dbAPI.readSheet('ton-kho-an-an', CUSTOM_FILTER_DB_SHEET);
      if(res && res.ok && Array.isArray(res.data) && res.data.length){
        const cell = res.data[0] && res.data[0].data;
        if(typeof cell === 'string' && cell.startsWith('__JSON__')){
          applyCustomFiltersPayload(JSON.parse(cell.slice('__JSON__'.length)));
          return;
        }
      }
      if(res && res.ok) return; // đọc thành công nhưng chưa từng lưu điều kiện nào — không cần fallback
    }catch(e){ /* lỗi đọc (VD file đang bị khoá) — thử phương án dự phòng bên dưới */ }
  }
  try{
    const raw = localStorage.getItem(CUSTOM_FILTER_STORAGE_KEY);
    if(raw) applyCustomFiltersPayload(JSON.parse(raw));
  }catch(e){ /* chưa có dữ liệu lưu trước đó, hoặc dữ liệu lỗi — bỏ qua, dùng mặc định rỗng */ }
}
// So khớp 1 điều kiện tự thêm với 1 ô dữ liệu — dùng normalize() (so khớp "bằng"/"khác", không
// phân biệt hoa/thường + khoảng trắng), fuzzyNormalize() (so khớp "chứa", bỏ luôn dấu tiếng
// Việt) — ĐÚNG cơ chế đã dùng cho các điều kiện phụ khai báo sẵn (extraFilters) trong code, để
// hành vi nhất quán với các điều kiện gốc — hoặc parseLooseNumber() (so sánh số "lớn hơn/nhỏ
// hơn"). Với "lớn hơn"/"nhỏ hơn": nếu ô dữ liệu hoặc giá trị nhập không phải số hợp lệ thì coi
// như KHÔNG khớp (an toàn — không loại nhầm/giữ nhầm dòng vì lỗi định dạng số).
function testCustomFilter(cellVal, cf){
  switch(cf.op){
    case 'eq': return normalize(cellVal) === normalize(cf.value);
    case 'neq': return normalize(cellVal) !== normalize(cf.value);
    case 'contains': return fuzzyIncludes(cellVal, cf.value);
    case 'notcontains': return !fuzzyIncludes(cellVal, cf.value);
    case 'gt': { const a = parseLooseNumber(cellVal), b = parseLooseNumber(cf.value); return !isNaN(a) && !isNaN(b) && a > b; }
    case 'lt': { const a = parseLooseNumber(cellVal), b = parseLooseNumber(cf.value); return !isNaN(a) && !isNaN(b) && a < b; }
    case 'empty': return String(cellVal ?? '').trim() === '';
    case 'notempty': return String(cellVal ?? '').trim() !== '';
    // PC 1.24 — toán tử cụ thể hơn (cùng quy tắc với dc-phanloai-m1.js, giữ 2 file khớp nhau)
    case 'gte': { const a = parseLooseNumber(cellVal), b = parseLooseNumber(cf.value); return !isNaN(a) && !isNaN(b) && a >= b; }
    case 'lte': { const a = parseLooseNumber(cellVal), b = parseLooseNumber(cf.value); return !isNaN(a) && !isNaN(b) && a <= b; }
    case 'startswith': { const q = noSpace(fuzzyNormalize(cf.value)); return q !== '' && noSpace(fuzzyNormalize(cellVal)).startsWith(q); }
    case 'endswith': { const q = noSpace(fuzzyNormalize(cf.value)); return q !== '' && noSpace(fuzzyNormalize(cellVal)).endsWith(q); }
    case 'in': return customFilterList(cf.value).some(v => normalize(cellVal) === normalize(v));
    case 'notin': return !customFilterList(cf.value).some(v => normalize(cellVal) === normalize(v));
    case 'pos_in': case 'pos_notin': {
      const from = Math.max(1, parseInt(cf.from, 10) || 1), to = Math.max(from, parseInt(cf.to, 10) || from);
      const seg = String(cellVal ?? '').trim().substring(from - 1, to).toUpperCase();
      const hit = customFilterList(cf.value).some(v => v.trim().toUpperCase() === seg);
      return cf.op === 'pos_in' ? hit : !hit;
    }
    default: return true;
  }
}
Object.keys(FILTER_MODULES).forEach(id => { state.filterModules[id] = makeFilterModuleState(id); });

/* ================= NAV RENDER ================= */
// PC 1.10 — menu phụ dạng chip nhỏ (css/kl-tabs-g.css): mục ĐẦU mỗi nhóm có thêm khoảng cách bên trái (Load Data·Dữ Liệu | Báo Cáo·Phân Loại | Tồn theo vị trí·Vị Trí Bột | Đặc Tính·Tổng Hợp)
const NAV_GAP_BEFORE = new Set(['baocao','dactinh']);
function renderNav(){
  const nav = document.getElementById('navList');
  // Nhóm "Phân Loại" hiện 1 nút gộp trên thanh menu ngang phía trên — bấm vào sẽ mở module con
  // đang xem gần nhất (hoặc "Thành Phẩm Chính" theo mặc định). Việc CHUYỂN QUA LẠI giữa các
  // module con trong nhóm này nằm ở 1 dòng menu ngang RIÊNG, hiện ngay phía trên bảng dữ liệu
  // (xem renderPhanLoaiSubnav) — tiện chuyển bảng hơn so với dropdown lồng trong menu chính.
  const plItems = MODULES.filter(m=>m.group==='phanloai');
  const plActive = plItems.some(m=>m.id===state.activeModule);
  let plButtonAdded = false;
  const parts = [];
  MODULES.forEach((m)=>{
    if(!m1ModAllowed(m.id)) return; // PC 7.2: ẩn menu không có quyền
    if(m.group==='phanloai'){
      if(plButtonAdded) return;
      plButtonAdded = true;
      parts.push(`
        <button class="nav-item ${plActive?'active':''}" data-id="__phanloai__" style="--tc:#34D399">
          ${icon('stack','nav-icon')}
          <span>Phân Loại</span>
        </button>
      `);
      return;
    }
    parts.push(`
      <button class="nav-item ${NAV_GAP_BEFORE.has(m.id)?'g-gap':''} ${m.id===state.activeModule?'active':''}" data-id="${m.id}" style="--tc:${m.color}">
        ${icon(m.icon,'nav-icon')}
        <span>${m.label}</span>
        ${m.soon?'<span class="soon">SẮP CÓ</span>':''}
      </button>
    `);
  });
  nav.innerHTML = parts.join('');
  nav.querySelectorAll('.nav-item').forEach(btn=>{
    btn.addEventListener('click', ()=>{
      if(btn.dataset.id === '__phanloai__'){
        state.activeModule = state.phanLoaiSub || 'tpc';
        renderAll();
        return;
      }
      state.activeModule = btn.dataset.id;
      renderAll();
    });
  });
}

/* ================= HELPERS ================= */
function normalize(s){ return String(s??'').trim().toLowerCase().replace(/\s+/g,' '); }
// Chuẩn hoá "sâu" 1 giá trị ô khi so khớp CHÍNH XÁC cho bộ lọc icon phễu (khác normalize() ở
// trên vốn dùng cho tìm kiếm "chứa" và có đổi thường/viết hoa): ngoài trim khoảng trắng 2 đầu +
// gộp khoảng trắng liên tiếp, còn đưa về Unicode dạng chuẩn NFC + bỏ các ký tự "vô hình" hay gặp
// khi dữ liệu được copy từ nguồn khác (Word/PDF/web) dán vào Excel — dấu cách không ngắt (NBSP,
// U+00A0), khoảng trắng độ rộng-không (zero-width space/joiner, U+200B–U+200D, U+FEFF).
// LÝ DO CẦN HÀM NÀY: nếu không xử lý, 2 ô nhìn GIỐNG HỆT NHAU trên màn hình (VD "03.N-EU-H") có
// thể là 2 CHUỖI KHÁC NHAU về byte — khiến 1 giá trị xuất hiện đúng trong danh sách lọc (rút từ
// dữ liệu thật) nhưng khi tick chọn lại không khớp được dòng nào (dòng đó dùng biến thể ký tự
// khác), trông như "chọn 1 giá trị lọc ra 0 dòng" dù giá trị đó rõ ràng có trong dữ liệu. Dùng
// CHUNG cho cả nơi dựng danh sách checkbox (giá trị hiển thị) LẪN nơi so khớp lọc thật sự, để 2
// bên luôn đồng nhất tuyệt đối.
function normalizeCellForExactFilter(s){
  return String(s ?? '')
    .normalize('NFC')
    .replace(/[\u200B-\u200D\uFEFF]/g, '')
    .replace(/\u00A0/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}
function isNumericCol(h){ return NUMERIC_COLS.has(normalize(h)); }
// "Trọng lượng" là số (định dạng/canh phải như cột số khác), NHƯNG là trọng lượng ĐƠN VỊ của
// từng mã hàng (VD 1 kiện Size A nặng 9.00kg, Size B nặng 0.14kg...) — cộng dồn cột này qua
// nhiều mã hàng khác nhau ra 1 con số KHÔNG có ý nghĩa gì (khác với "Tồn cuối"/"Tổng KL (kg)"
// là số LƯỢNG/tổng khối lượng thực tế, cộng dồn được). Vì vậy dùng isSummableCol() (thay vì
// isNumericCol()) ở MỌI nơi tính dòng Tổng/SUBTOTAL, để "Trọng lượng" vẫn hiển thị canh phải +
// định dạng số như bình thường nhưng KHÔNG bị đưa vào dòng Tổng.
function isSummableCol(h){ return isNumericCol(h) && normalize(h) !== 'trọng lượng'; }

// Chuyển 1 giá trị ô Excel (đọc dạng CHUỖI do dùng {raw:false}) thành số, chấp nhận cả 2 kiểu
// định dạng hay gặp:
//  - Kiểu Mỹ/mặc định:      "1,234.56"  (dấu phẩy = hàng nghìn, dấu chấm = thập phân)
//  - Kiểu Việt (Excel VN):  "1.234,56"  (dấu chấm = hàng nghìn, dấu phẩy = thập phân)
// kể cả số không có phân cách hàng nghìn, hoặc có đơn vị đi kèm phía sau (VD "0.16 kg" vẫn lấy
// được phần số ở đầu). Trả về NaN nếu không tách được số hợp lệ. DÙNG CHUNG cho MỌI chỗ tính
// tổng/so sánh số trong file này — trước đây mỗi nơi tự viết "parseFloat(...replace(/,/g,''))"
// riêng lẻ, chỉ đúng với kiểu Mỹ; nếu cột số trong file nguồn dùng dấu phẩy làm dấu thập phân
// (kiểu Việt) thì bị parse SAI (hoặc ra NaN, khiến cột đó bị loại khỏi dòng "Tổng" — VD cột
// "Trọng lượng" không hiện tổng dù có dữ liệu).
function parseLooseNumber(v){
  let s = String(v ?? '').trim();
  if(s === '') return NaN;
  s = s.replace(/[\u00A0\s]/g, '');
  const hasComma = s.includes(',');
  const hasDot = s.includes('.');
  if(hasComma && hasDot){
    if(s.lastIndexOf(',') > s.lastIndexOf('.')) s = s.replace(/\./g, '').replace(',', '.');
    else s = s.replace(/,/g, '');
  } else if(hasComma){
    s = /^-?\d+,\d{1,2}$/.test(s) ? s.replace(',', '.') : s.replace(/,/g, '');
  }
  const m = s.match(/^-?\d+(\.\d+)?/);
  return m ? parseFloat(m[0]) : NaN;
}

// So khớp khi tìm kiếm: nếu từ khóa là số thuần (VD "30"), chỉ khớp khi Ô DỮ LIỆU bằng
// CHÍNH XÁC từ khóa đó (sau khi bỏ khoảng trắng thừa) — để tránh "30" khớp nhầm vào
// "26/30" hay các mã số khác chứa "30" như 1 phần bên trong. Với từ khóa dạng chữ (VD
// tên khách hàng), vẫn tìm kiểu chứa (substring) như trước để dễ tìm kiếm gần đúng.
function isPureNumericQuery(q){ return /^\d+([.,]\d+)?$/.test(String(q??'').trim()); }
// So khớp dựa trên giá trị Ô ĐÃ CHUẨN HÓA SẴN (xem getNormalizedRows bên dưới) — dùng trong
// vòng lặp tìm kiếm để tránh gọi lại normalize() (trim/lowercase/regex) trên từng ô mỗi lần
// gõ phím, vốn là điểm chậm nhất khi bảng có nhiều dòng.
// PC 7.38: chuẩn hóa từ khóa 1 LẦN rồi dùng cho cả nghìn dòng (bản cũ chuẩn hóa lại từ khóa cho từng ô).
function prepareNormQuery(query){
  const q = normalize(query);
  return { q, empty: q === '', numeric: isPureNumericQuery(query), qs: noSpace(q) };
}
function normMatchesPrepared(normCell, pq){
  if(pq.empty) return false;
  if(pq.numeric) return normCell === pq.q;
  return normCell.includes(pq.q) || noSpace(normCell).includes(pq.qs);
}
function normMatchesQuery(normCell, query){
  const q = normalize(query);
  if(q === '') return false;
  if(isPureNumericQuery(query)) return normCell === q;
  return normCell.includes(q) || noSpace(normCell).includes(noSpace(q));
}

// Bộ nhớ đệm: chuẩn hóa (normalize) sẵn toàn bộ ô dữ liệu 1 lần, dùng lại cho mọi lượt tìm
// kiếm cho tới khi state.rows được NẠP LẠI (gán mảng mới — so sánh bằng tham chiếu, không
// cần tự tay xóa cache). Giúp gõ tìm kiếm mượt hơn nhiều khi bảng có hàng nghìn dòng.
let _normCache = null;
function getNormalizedRows(){
  if(_normCache && _normCache.source === state.rows) return _normCache.data;
  const data = state.rows.map(row => row.map(c => normalize(c)));
  _normCache = { source: state.rows, data };
  return data;
}

/* ================= NGÀY SỐ LIỆU =================
   "Ngày số liệu" = ngày CỦA số tồn trong file (VD file "TON 23.9.xlsx" là tồn ngày 23/09), KHÁC với
   "Nạp lúc" (importedAt = lúc bấm nạp file). Người dùng nhập/sửa ở Load Data; khi nạp file mới app
   đoán sẵn từ tên file. Dùng làm chuẩn cho: dòng phụ đầu trang, hộp "Ngày báo cáo" khi xuất Excel
   (điền sẵn), Tổng Quan, và đẩy lên điện thoại (Meta m01DataDate / m01ViTriDataDate). Lưu dạng "dd/mm/yyyy". */
// ddmmValid/ddmmFmt/ddmmYear/parseDataDate/guessDataDateFromFileName: js/data-date.js (dùng chung Module 1 + 2).

// Thông báo nhỏ góc dưới màn hình. Bộ lọc tuỳ chỉnh ("Lưu" điều kiện lọc) gọi toast() nhưng trang
// Module 1 trước đây KHÔNG có hàm này → bấm Lưu thiếu cột/giá trị bị lỗi ngầm, không báo gì.
function toast(msg, isErr){
  let el = document.getElementById('bctToast');
  if(!el){
    el = document.createElement('div');
    el.id = 'bctToast';
    el.style.cssText = 'position:fixed;left:50%;bottom:24px;transform:translateX(-50%);z-index:99999;padding:9px 16px;border-radius:8px;font-size:13px;font-weight:600;box-shadow:0 6px 24px rgba(0,0,0,.35);transition:opacity .2s;pointer-events:none;';
    document.body.appendChild(el);
  }
  el.textContent = msg;
  el.style.background = isErr ? '#b42318' : '#1f6f3f';
  el.style.color = '#fff';
  el.style.opacity = '1';
  clearTimeout(el._t);
  el._t = setTimeout(()=>{ el.style.opacity = '0'; }, 2600);
}
// Ô nhập "Ngày số liệu" dùng chung cho 2 khu (Load Data + Tải dữ liệu vị trí).
function dataDateFieldHtml(id, value){
  return `<span>Ngày số liệu</span><span class="dd-field">
      <input type="text" id="${id}" class="dd-input${value ? '' : ' dd-missing'}" value="${escapeHtml(value || '')}" placeholder="dd/mm/yyyy" maxlength="10" autocomplete="off"
        title="Ngày của số tồn trong file (không phải ngày nạp). Gõ dd/mm/yyyy, Enter để lưu.">
      ${value ? '' : '<em class="dd-warn">Chưa nhập — đang dùng ngày nạp</em>'}</span>`;
}
function bindDataDateInput(id, getVal, setVal){
  const el = document.getElementById(id);
  if(!el) return;
  const commit = async ()=>{
    const raw = el.value.trim();
    const v = parseDataDate(raw);
    if(raw && !v){ el.classList.add('dd-bad'); el.title = 'Ngày không hợp lệ — gõ dạng dd/mm/yyyy'; return; }
    el.classList.remove('dd-bad');
    if(v === (getVal() || '')){ el.value = v; return; }
    await setVal(v);
    renderAll();
  };
  el.addEventListener('keydown', e=>{ if(e.key === 'Enter'){ e.preventDefault(); el.blur(); } else if(e.key === 'Escape'){ el.value = getVal() || ''; el.blur(); } });
  el.addEventListener('change', commit);
  el.addEventListener('click', e=> e.stopPropagation());
}
// Dòng "Ngày số liệu" ở đầu bảng "Dữ liệu đã nạp" (bên phải) của cả 3 khu: Dữ Liệu Chính / Vị Trí / Vị Trí Bột.
// Chỉ HIỂN THỊ (sửa ngày vẫn ở ô bên trái). Chưa nhập thì hiện ngày nạp file kèm chú thích màu cảnh báo.
function dataDateStatHtml(dataDate, importedAt){
  let val = dataDate || '', note = '';
  if(!val){
    if(importedAt){ const d = new Date(importedAt); val = ddmmFmt(d.getDate(), d.getMonth()+1, d.getFullYear()); }
    note = val ? 'Chưa nhập — đang dùng ngày nạp' : 'Chưa nhập';
  }
  return `<div class="ld-stat wide ld-stat-date${dataDate ? '' : ' dd-missing'}"><div class="ld-stat-row">
      <div><div class="v">${val ? escapeHtml(val) : '—'}</div><div class="l">Ngày số liệu</div></div>
      ${note ? `<span class="dd-warn">${note}</span>` : ''}
    </div></div>`;
}
// Ngày số liệu của Load Data, không có thì lấy ngày nạp file (để hiển thị ở dòng phụ).
function loadDataDateLabel(){
  if(state.dataDate) return state.dataDate;
  if(state.importedAt){ const d = new Date(state.importedAt); return ddmmFmt(d.getDate(), d.getMonth()+1, d.getFullYear()) + ' (ngày nạp)'; }
  return '';
}

// Tìm kiếm KHÔNG TÍNH KHOẢNG TRẮNG: "BTP30" = "BTP 30". So sánh 2 chuỗi đã chuẩn hoá sau khi bỏ hết
// dấu cách (fuzzyNormalize đổi . , ( ) / - _ : ; thành dấu cách nên các ký tự đó cũng được bỏ qua).
function noSpace(s){ return String(s??'').replace(/\s+/g,''); }
// PC 7.38: ghi nhớ kết quả chuẩn hóa của từng ô (NFD + nhiều regex rất tốn khi lọc hàng nghìn dòng mỗi lần gõ 1 phím).
const _fzCache = new Map();
function _fzHay(hay){
  const k = typeof hay === 'string' ? hay : String(hay??'');
  let v = _fzCache.get(k);
  if(v === undefined){
    if(_fzCache.size > 150000) _fzCache.clear();
    v = noSpace(fuzzyNormalize(k)); _fzCache.set(k, v);
  }
  return v;
}
const _fzQ = { raw:null, val:'' };
function fuzzyIncludes(hay, q){
  if(_fzQ.raw !== q){ _fzQ.raw = q; _fzQ.val = noSpace(fuzzyNormalize(q)); }
  return _fzHay(hay).includes(_fzQ.val);
}
function fuzzyNormalize(s){
  return String(s??'')
    .normalize('NFD').replace(/[\u0300-\u036f]/g,'')
    .replace(/đ/gi,'d')
    .toLowerCase()
    .replace(/[.,()\/\\\-_:;]/g,' ')
    .replace(/\s+/g,' ')
    .trim();
}
function headersFuzzyMatch(a, b){
  const fa = fuzzyNormalize(a), fb = fuzzyNormalize(b);
  if(!fa || !fb) return false;
  return fa === fb || fa.includes(fb) || fb.includes(fa);
}

function matchedSchema(){
  const matched = REQUIRED_COLUMNS.filter(rc => state.headers.some(h => headersFuzzyMatch(h, rc)));
  const missing = REQUIRED_COLUMNS.filter(rc => !state.headers.some(h => headersFuzzyMatch(h, rc)));
  return {matched, missing};
}

function detectHeaderRowIndex(rawRows){
  const scanLimit = Math.min(15, rawRows.length);
  let bestIdx = 0, bestScore = -1;
  for(let i=0; i<scanLimit; i++){
    const row = rawRows[i];
    if(!row || !row.some(c => String(c).trim() !== '')) continue;
    const score = row.filter(cell => REQUIRED_COLUMNS.some(rc => headersFuzzyMatch(cell, rc))).length;
    if(score > bestScore){ bestScore = score; bestIdx = i; }
  }
  return {idx: bestIdx, score: bestScore};
}

// Ô tìm chung trang "Dữ Liệu" — tìm kiểu "BTP 50 MKS Nau" (js/smart-search.js, 09/2026): nhãn Size đọc từ Mã hàng,
// mỗi từ tìm riêng ở mọi cột. Chỉ mục dựng lại khi nạp file mới / đổi cột / từ điển Size Module 8 nạp xong.
let _smartIdx = null;
function m1SmartIndex(){
  const S = window.KLSmartSearch;
  if(!S) return null;
  if(_smartIdx && _smartIdx.rows === state.rows && _smartIdx.headers === state.headers && _smartIdx.idx.version === S.version()) return _smartIdx.idx;
  const col = (name) => state.headers.findIndex(h => fuzzyNormalize(h) === fuzzyNormalize(name));
  const iMa = col('Mã hàng'), iTen = col('Tên hàng'), iSz = col('Size');
  const idx = S.buildIndex(state.rows.length, i => state.rows[i],
    i => [iMa >= 0 ? state.rows[i][iMa] : '', iTen >= 0 ? state.rows[i][iTen] : '', iSz >= 0 ? state.rows[i][iSz] : '']);
  _smartIdx = { rows: state.rows, headers: state.headers, idx };
  return idx;
}
if(window.KLSmartSearch){
  window.KLSmartSearch.loadDict();
  window.KLSmartSearch.onDictReady(() => { if(state.search && state.search.trim() && document.getElementById('ldTable')) renderLoadDataRows(); /* chỉ khi đang mở trang Dữ Liệu */ });
}

function filteredRows(){
  let fr;
  if(!state.search.trim()){
    fr = state.rows.map((row,i)=>({i, row}));
  } else {
    const idx = m1SmartIndex();
    const hit = idx && window.KLSmartSearch.matcher(idx, state.search);
    if(hit){
      fr = state.rows.map((row,i)=>({i, row})).filter(({i}) => hit(i));
    } else { // dự phòng (thiếu smart-search.js): cách tìm cũ — cả cụm trong 1 ô
      const normRows = getNormalizedRows();
      const q = state.search;
      { const pq = prepareNormQuery(q); fr = state.rows.map((row,i)=>({i, row})).filter(({i}) => normRows[i].some(nc => normMatchesPrepared(nc, pq))); }
    }
  }
  // Áp dụng thêm các ô lọc theo từng cột riêng (checkbox nhiều giá trị, icon phễu) — CỘNG DỒN
  // với ô tìm kiếm chung ở trên (dòng phải khớp CẢ ô tìm chung LẪN mọi cột đang lọc, giống hệt
  // cơ chế đã dùng ở 7 module lọc Thành Phẩm Chính/Tạm, HLSO, Tôm…).
  const activeColFilters = Object.entries(state.colFilters).filter(([,v]) => colFilterActive(v));
  if(activeColFilters.length){
    fr = fr.filter(({row}) => activeColFilters.every(([ci, set]) => set.has(normalizeCellForExactFilter(row[Number(ci)]))));
  }
  // Ô tìm kiếm "chứa" riêng theo từng cột (dòng ngay dưới tiêu đề) — CỘNG DỒN với bộ lọc icon
  // phễu ở trên và ô tìm kiếm chung "searchInput".
  const activeColSearch = Object.entries(state.colSearch).filter(([,v]) => String(v||'').trim() !== '');
  if(activeColSearch.length){
    fr = fr.filter(({row}) => activeColSearch.every(([ci, q]) => fuzzyIncludes(row[Number(ci)], q)));
  }
  if(state.sortCol !== null){
    const ci = state.sortCol;
    const numeric = isNumericCol(state.headers[ci]);
    const dir = state.sortDir === 'desc' ? -1 : 1;
    fr = fr.slice().sort((a,b)=>{
      const va = a.row[ci] ?? '', vb = b.row[ci] ?? '';
      if(va === '' && vb === '') return 0;
      if(va === '') return 1;
      if(vb === '') return -1;
      if(numeric){
        const na = parseLooseNumber(va), nb = parseLooseNumber(vb);
        if(!isNaN(na) && !isNaN(nb)) return (na-nb)*dir;
      }
      return String(va).localeCompare(String(vb), 'vi', {numeric:true, sensitivity:'base'}) * dir;
    });
  }
  return fr;
}

/* ================= MODULE LỌC DÙNG CHUNG (Thành Phẩm Chính, HLSO…) ================= */
// Ánh xạ cột hiển thị sang đúng cột thực tế trong dữ liệu đã nạp (dùng fuzzy match
// giống hệt Load Data), nên bảng luôn phản ánh đúng dữ liệu vừa import.
function getFilterModuleColumns(modId){
  const cfg = FILTER_MODULES[modId];
  return cfg.columns
    .map(label => ({ label, ci: state.headers.findIndex(h => headersFuzzyMatch(h, label)) }))
    .filter(c => c.ci !== -1);
}

// Cột dùng riêng khi XUẤT EXCEL — CỐ ĐỊNH theo EXPORT_COLUMNS, không phụ thuộc cột đang
// hiển thị/đã resize trên bảng của module. Dùng chung cho cả exportFilterModuleToExcel()
// (xuất từng module) và exportBaoCaoAllModulesToExcel() (xuất "Báo Cáo Tổng Tồn", 7 sheet).
function getFilterModuleExportColumns(modId){
  return EXPORT_COLUMNS
    .map(label => ({ label, ci: state.headers.findIndex(h => headersFuzzyMatch(h, label)) }))
    .filter(c => c.ci !== -1);
}

// Cột riêng cho bảng "Báo Cáo Tổng Hợp" (chèn cuối sheet "Tổng Tồn" khi xuất Excel — xem
// buildTongTonSummarySheet()) — 10 cột theo đúng yêu cầu, KHÁC với EXPORT_COLUMNS:
//   1. Tên hàng, 2. Mã hàng                — lấy thẳng giá trị cột cùng tên.
//   3. Loại hàng                            — 4 ký tự ĐẦU của giá trị cột "Mã hàng" (cột ẢO,
//                                              không có sẵn trong Load Data).
//   4. Size, 5. Khách hàng, 6. Đặc tính, 7. Hợp đồng — lấy thẳng giá trị cột cùng tên.
//   8. "Tồn cuối (kiện)"                    — lấy giá trị cột "Tồn cuối" (đổi tên hiển thị).
//   9. "Tồn cuối (net)"                     — lấy giá trị cột "Tồn cuối (NET)" (đổi tên).
//  10. "Bảng"                               — TÊN MODULE (Thành Phẩm Chính/HLSO/Tôm.../BM...)
//                                              mà dòng đó đang thuộc về trên bảng "Báo Cáo Tổng
//                                              Tồn" — cột ẢO, tính qua buildRowModuleNameMap()
//                                              bên dưới (KHÔNG lấy trực tiếp từ 1 cột có sẵn).
// "Tồn cuối" và "Tồn cuối (NET)" là 2 cột KHÁC NHAU nhưng headersFuzzyMatch coi "Tồn cuối (NET)"
// cũng "chứa" chuỗi "Tồn cuối" (so khớp kiểu substring) — nên phải tìm cột "(NET)" TRƯỚC, rồi
// loại trừ đúng vị trí cột đó khi tìm cột "Tồn cuối" thường, để không bị lẫn 2 cột vào nhau.
function getTongHopExportColumns(){
  const maHangCi = state.headers.findIndex(h => headersFuzzyMatch(h, 'Mã hàng'));
  // So khớp CHÍNH XÁC (sau khi chuẩn hoá) cho 2 cột "Tồn cuối" / "Tồn cuối (NET)" — headersFuzzyMatch
  // thường dùng (kiểu substring) sẽ nhận nhầm 2 cột này lẫn nhau vì "ton cuoi" là 1 chuỗi con của
  // "ton cuoi net", nên phải so khớp đúng tuyệt đối ở đây để không bị hoán đổi giá trị 2 cột.
  const netCi = state.headers.findIndex(h => fuzzyNormalize(h) === fuzzyNormalize('Tồn cuối (NET)'));
  const tonCuoiCi = state.headers.findIndex(h => fuzzyNormalize(h) === fuzzyNormalize('Tồn cuối'));
  const findCi = label => state.headers.findIndex(h => headersFuzzyMatch(h, label));

  const defs = [
    { label: 'Tên hàng',        ci: findCi('Tên hàng') },
    { label: 'Mã hàng',         ci: maHangCi },
    { label: 'Loại hàng',       ci: maHangCi, deriveLoaiHang: true },
    { label: 'Size',            ci: findCi('Size') },
    { label: 'Khách hàng',      ci: findCi('Khách hàng') },
    { label: 'Đặc tính',        ci: findCi('Đặc tính') },
    { label: 'Hợp đồng',        ci: findCi('Hợp đồng') },
    { label: 'Tồn cuối (kiện)', ci: tonCuoiCi, numeric: true, integer: true },
    { label: 'Tồn cuối (net)',  ci: netCi, numeric: true },
    { label: 'Bảng',            ci: maHangCi, deriveModuleName: true },
  ];
  return defs.filter(c => c.ci !== -1);
}

// Dò xem MỖI dòng Load Data (theo chỉ số dòng, không phải theo Mã hàng — vì Mã hàng có thể
// trùng nhau giữa nhiều dòng khác Size/Hợp đồng) đang thuộc về module lọc NÀO trên bảng "Báo
// Cáo Tổng Tồn" (Thành Phẩm Chính/Thành Phẩm Tạm/BM/HLSO/Tôm.../...) — dùng lại ĐÚNG danh sách
// dòng mà mỗi module đang tính (filterModuleBaseRowsCached, đã áp dụng cả điều kiện lọc tự động
// LẪN các dòng đã chuyển thủ công qua module khác), để luôn khớp 100% với dòng đang hiển thị
// trên bảng — không tính lại logic phân loại riêng ở đây, tránh sai lệch/trùng lặp code.
// CHỈ xét các module có trong FILTER_MODULES (7 module phân loại chính) — module "Đặc Tính" là
// bảng tổng hợp KHÁC (không thuộc FILTER_MODULES, không mang tính phân loại "1 dòng = 1 module"
// nên không đưa vào đây).
function buildRowModuleNameMap(){
  const map = {};
  Object.keys(FILTER_MODULES).forEach(modId=>{
    const label = (MODULES.find(m => m.id === modId) || {}).label || modId;
    filterModuleBaseRowsCached(modId).forEach(({i})=>{ map[i] = label; });
  });
  return map;
}

// Giá trị 1 ô trong bảng "Báo Cáo Tổng Hợp": số thì parse về number (để Excel tính tổng/định
// dạng nghìn được), chữ thì giữ nguyên; riêng cột "Loại hàng" lấy 4 ký tự đầu của "Mã hàng".
// Riêng "Tồn cuối (kiện)" (integer: true) LÀM TRÒN về số nguyên — cột này đếm SỐ KIỆN, không
// lấy phần thập phân (khác với "Tồn cuối (net)" — trọng lượng, vẫn giữ nguyên số thập phân).
function tongHopCellValue(row, col, rowIdx, rowModuleNameMap){
  if(col.deriveModuleName) return rowModuleNameMap ? (rowModuleNameMap[rowIdx] || '') : '';
  const raw = row[col.ci] ?? '';
  if(col.deriveLoaiHang) return String(raw).trim().slice(0, 4);
  if(col.numeric && raw !== ''){
    const n = parseLooseNumber(raw);
    if(isNaN(n)) return raw;
    return col.integer ? Math.round(n) : n;
  }
  return raw;
}

// Cột dùng để lọc — mặc định lấy theo cfg.defaultCol nếu có trong dữ liệu,
// nếu không thì dùng cột đầu tiên.
function resolveFilterCol(modId){
  const cfg = FILTER_MODULES[modId];
  const st = state.filterModules[modId];
  if(st.filterCol !== null && st.filterCol < state.headers.length) return st.filterCol;
  const idx = state.headers.findIndex(h => headersFuzzyMatch(h, cfg.defaultCol));
  if(idx !== -1) return idx;
  return state.headers.length ? 0 : null;
}

function distinctColumnValues(ci){
  if(ci === null) return [];
  const set = new Set();
  state.rows.forEach(row=>{ const v = String(row[ci] ?? '').trim(); if(v) set.add(v); });
  return Array.from(set).sort((a,b)=>a.localeCompare(b,'vi'));
}

// Giá trị lọc đang áp dụng — nếu người dùng chưa chọn (hoặc lựa chọn cũ không còn tồn tại
// trong dữ liệu mới), tự động chọn theo cfg.guess(), nếu không có thì lấy giá trị đầu tiên.
function resolveFilterVal(modId, ci){
  const cfg = FILTER_MODULES[modId];
  const st = state.filterModules[modId];
  const vals = distinctColumnValues(ci);
  if(st.filterVal && vals.includes(st.filterVal)) return st.filterVal;
  const guess = cfg.guess(vals);
  return guess || vals[0] || '';
}

// Tập dòng gốc của module: toàn bộ dữ liệu Load Data đã lọc theo tiêu chí chính, rồi áp
// thêm điều kiện phụ (extraFilter) nếu module có cấu hình.
// Tiêu chí chính có 2 dạng:
// - primaryMatch(value): hàm cố định, khớp NHIỀU giá trị cùng lúc (không chọn qua dropdown).
// - cột + 1 giá trị cụ thể chọn qua dropdown (mặc định theo guess()) — dùng khi không có primaryMatch.
//
// Dựng sẵn 1 hàm kiểm tra "dòng có thỏa module hay không" với vị trí cột đã XÁC ĐỊNH TRƯỚC
// (tính 1 lần duy nhất) — để dùng trong vòng lặp hàng nghìn dòng mà KHÔNG phải tìm lại vị trí
// cột cho từng dòng (rất tốn khi dữ liệu lớn). Dùng cho module loại trừ (excludeAllOtherModules).
function buildModuleMatcher(modId){
  const cfg = FILTER_MODULES[modId];
  let primaryCi = -1;
  if(cfg.primaryMatch){
    primaryCi = cfg.primaryMatchCol ? state.headers.findIndex(h => headersFuzzyMatch(h, cfg.primaryMatchCol)) : -1;
  }
  let dropdownCi = null, dropdownVal = '';
  if(!cfg.primaryMatch && !cfg.excludeAllOtherModules){
    const ci = resolveFilterCol(modId);
    if(ci !== null){
      dropdownCi = ci;
      dropdownVal = resolveFilterVal(modId, ci);
    }
  }
  const extraChecks = (cfg.extraFilters || [])
    .map(ef => ({ ci: ef.col ? state.headers.findIndex(h => headersFuzzyMatch(h, ef.col)) : -1, test: ef.test }))
    .filter(e => e.ci !== -1);

  return function(row){
    if(cfg.primaryMatch){
      if(primaryCi !== -1 && !cfg.primaryMatch(row[primaryCi])) return false;
    } else if(dropdownCi !== null && dropdownVal && normalize(row[dropdownCi]) !== normalize(dropdownVal)){
      return false;
    }
    for(let k=0; k<extraChecks.length; k++){
      if(!extraChecks[k].test(row, extraChecks[k].ci)) return false;
    }
    return true;
  };
}

// Module có điều kiện phụ theo cột "Khách hàng" hay không — dùng để ƯU TIÊN khi 1 dòng khớp
// điều kiện tự động của ≥2 module cùng lúc (VD: "Tôm Giám Sát" chỉ kiểm tra cột "Mạ băng",
// không xét Khách hàng — nên có thể trùng với "Tôm Gia Công" nếu dòng đó vừa có Mạ băng chứa
// "Tôm giám sát" vừa khớp điều kiện Khách hàng "TÂN PHONG PHÚ" của Tôm Gia Công). Quy ước:
// dòng bị trùng điều kiện như vậy CHỈ thuộc về module có điều kiện "Khách hàng", KHÔNG tính
// vào module không có điều kiện này — tránh đếm 2 lần vào tổng trọng lượng ở "Báo Cáo".
function moduleHasKhachHangCondition(modId){
  const cfg = FILTER_MODULES[modId];
  return !!(cfg.extraFilters || []).some(ef => ef.col === 'Khách hàng');
}

function filterModuleBaseRows(modId){
  const cfg = FILTER_MODULES[modId];
  let base;
  if(cfg.excludeAllOtherModules){
    // Module "phần còn lại": lấy toàn bộ dữ liệu Load Data, loại bỏ mọi dòng đã thuộc về
    // BẤT KỲ module lọc nào khác (tự động cập nhật khi có module mới được thêm vào sau này).
    // Dựng sẵn 1 hàm kiểm tra (matcher) cho mỗi module khác — CHỈ 1 LẦN trước vòng lặp — thay
    // vì tìm lại vị trí cột cho từng dòng, giúp nhanh hơn nhiều lần khi dữ liệu lớn.
    const otherIds = Object.keys(FILTER_MODULES).filter(id => id !== modId);
    const matchers = otherIds.map(id => buildModuleMatcher(id));
    base = state.rows
      .map((row,i)=>({i, row}))
      .filter(({row}) => !matchers.some(fn => fn(row)));
  } else if(cfg.primaryMatch){
    const ci = cfg.primaryMatchCol
      ? state.headers.findIndex(h => headersFuzzyMatch(h, cfg.primaryMatchCol))
      : -1;
    base = ci === -1
      ? state.rows.map((row,i)=>({i, row}))
      : state.rows.map((row,i)=>({i, row})).filter(({row}) => cfg.primaryMatch(row[ci]));
  } else {
    const ci = resolveFilterCol(modId);
    if(ci === null){
      base = state.rows.map((row,i)=>({i, row}));
    } else {
      const val = resolveFilterVal(modId, ci);
      base = !val
        ? state.rows.map((row,i)=>({i, row}))
        : state.rows.map((row,i)=>({i, row})).filter(({row}) => normalize(row[ci]) === normalize(val));
    }
  }
  if(cfg.extraFilters && cfg.extraFilters.length){
    cfg.extraFilters.forEach(ef=>{
      const extraCi = ef.col ? state.headers.findIndex(h => headersFuzzyMatch(h, ef.col)) : -1;
      if(extraCi !== -1){
        base = base.filter(({row}) => ef.test(row, extraCi));
      }
    });
  }
  // ƯU TIÊN THEO ĐIỀU KIỆN "KHÁCH HÀNG": nếu module này KHÔNG có điều kiện phụ theo cột
  // "Khách hàng" (xem moduleHasKhachHangCondition ở trên), loại khỏi module này mọi dòng ĐỒNG
  // THỜI khớp điều kiện tự động của 1 module KHÁC CÓ điều kiện "Khách hàng" — dòng đó được ưu
  // tiên tính vào module kia, tránh bị đếm trùng ở "Báo Cáo Tổng Tồn" (xem
  // computeModuleReconciliation()). Không áp dụng cho module "phần còn lại" (đã loại trừ mọi
  // module khác theo cách riêng ở trên).
  if(!cfg.excludeAllOtherModules && !moduleHasKhachHangCondition(modId)){
    const priorityIds = Object.keys(FILTER_MODULES).filter(id => id !== modId && moduleHasKhachHangCondition(id));
    if(priorityIds.length){
      const priorityMatchers = priorityIds.map(id => buildModuleMatcher(id));
      base = base.filter(({row}) => !priorityMatchers.some(fn => fn(row)));
    }
  }
  // Điều kiện lọc TỰ THÊM (bản thử nghiệm, xem loadCustomFiltersFor()) — CỘNG DỒN (AND) với các
  // điều kiện chính/phụ đã khóa ở trên, không thay thế. Cột không tìm thấy (VD đổi sang file
  // Excel khác không có cột đó) thì BỎ QUA AN TOÀN điều kiện đó, không báo lỗi/không loại hết dữ
  // liệu — người dùng vẫn thấy dữ liệu bình thường, chỉ thiếu đúng 1 điều kiện đó.
  const customFilters = (state.filterModules[modId] && state.filterModules[modId].customFilters) || [];
  if(customFilters.length){
    customFilters.forEach(cf=>{
      const cfCi = state.headers.findIndex(h => headersFuzzyMatch(h, cf.colLabel));
      if(cfCi !== -1){
        base = base.filter(({row}) => testCustomFilter(row[cfCi], cf));
      }
    });
  }

  // Áp dụng chuyển thủ công (state.manualAssign) — LUÔN tính SAU CÙNG, đè lên mọi điều kiện
  // lọc tự động ở trên: (1) loại khỏi module này bất kỳ dòng nào đã bị chuyển đi module khác,
  // (2) thêm vào module này các dòng đã được chuyển thủ công tới đây dù không tự khớp điều kiện.
  const overrides = state.manualAssign || {};
  base = base.filter(({i}) => {
    const ov = overrides[i];
    return ov === undefined || ov === modId;
  });
  const baseIdxSet = new Set(base.map(b => b.i));
  Object.keys(overrides).forEach(key=>{
    const i = Number(key);
    if(overrides[key] === modId && !baseIdxSet.has(i) && state.rows[i]){
      base.push({ i, row: state.rows[i] });
    }
  });

  return base;
}

// ---------------------------------------------------------------------------
// CACHE HIỆU NĂNG cho module "Báo Cáo" (bảng "Bảng Tổng Hợp HLSO"):
// filterModuleBaseRows(modId) quét TOÀN BỘ state.rows mỗi lần gọi. Trước khi có cache này,
// mỗi lần vẽ bảng "Bảng Tổng Hợp HLSO" (5 cụm module × nhiều Size × 11 cột thị trường) hàm
// này bị gọi lại hàng trăm lần CHO CÙNG 1 module trong CÙNG 1 lần render — với dữ liệu lớn
// (>4000 dòng) việc quét lặp lại như vậy cộng dồn thành hàng triệu phép so khớp, gây đơ khi
// chuyển sang tab "Báo Cáo". _fmBaseRowsCache lưu lại kết quả theo modId, chỉ tính 1 lần cho
// mỗi module trong 1 lần render — được xoá sạch ở đầu renderAll() nên luôn phản ánh đúng dữ
// liệu/trạng thái mới nhất ở lần render kế tiếp.
let _fmBaseRowsCache = {};
let _fmSizeStatsCache = {}; // trước đây thiếu khai báo (biến toàn cục ngầm) — xem getModuleSizeStats()
function clearRenderCaches(){
  _fmBaseRowsCache = {};
  _fmSizeStatsCache = {};
  _tpcStatusCache = null;
}
function filterModuleBaseRowsCached(modId){
  if(!(modId in _fmBaseRowsCache)) _fmBaseRowsCache[modId] = filterModuleBaseRows(modId);
  return _fmBaseRowsCache[modId];
}

// Nhóm dữ liệu tính "Trạng thái" (tab Thành Phẩm Chính) — TÁCH RIÊNG thành hàm dùng chung, để
// vừa dùng khi VẼ badge từng dòng (renderFilterModule), vừa dùng khi LỌC theo cột "Trạng thái"
// (filterModuleFilteredRows) — tránh tính trùng lặp 2 lần cho cùng 1 lần render.
let _tpcStatusCache = null;
function getTpcStatusInfo(){
  if(_tpcStatusCache) return _tpcStatusCache;
  const cols = getFilterModuleColumns('tpc');
  const maHangCi = cols.find(c => c.label === 'Mã hàng')?.ci;
  const hopDongCi = cols.find(c => c.label === 'Hợp đồng')?.ci;
  const tonCuoiCi = cols.find(c => c.label === 'Tồn cuối')?.ci;
  if(maHangCi === undefined || hopDongCi === undefined || tonCuoiCi === undefined){
    _tpcStatusCache = null;
    return null;
  }
  const totals = new Map();
  filterModuleBaseRowsCached('tpc').forEach(({row}) => {
    const key = String(row[maHangCi] ?? '').trim() + '|' + String(row[hopDongCi] ?? '').trim();
    const qty = Number(row[tonCuoiCi]) || 0;
    totals.set(key, (totals.get(key) || 0) + qty);
  });
  _tpcStatusCache = { maHangCi, hopDongCi, totals };
  return _tpcStatusCache;
}
function getTpcRowStatus(info, row){
  const key = String(row[info.maHangCi] ?? '').trim() + '|' + String(row[info.hopDongCi] ?? '').trim();
  const total = info.totals.get(key) || 0;
  const threshold = Number(state.tpcLowStockThreshold);
  const th = Number.isFinite(threshold) && threshold >= 0 ? threshold : TPC_THRESHOLD_DEFAULT;
  if(total <= 0) return 'Hết hàng';
  if(total <= th) return 'Sắp hết';
  return 'Còn hàng';
}

function filterModuleFilteredRows(modId){
  const st = state.filterModules[modId];
  let fr = filterModuleBaseRowsCached(modId);
  const weightBasis = getFilterModuleWeightBasis(modId); // dùng cho cột ẢO "Tổng KL (kg)" — xem bên dưới
  const activeColFilters = Object.entries(st.colFilters).filter(([k,v]) => k !== '__tpcStatus' && k !== '__tongKL' && colFilterActive(v));
  if(st.search.trim()){
    // PC 7.38: dùng CHUNG bộ tìm thông minh với trang "Dữ Liệu" / Module 2 (chỉ mục dựng 1 lần, mỗi phím chỉ so chuỗi) → gõ không giật.
    const idx = m1SmartIndex();
    const hit = idx && window.KLSmartSearch.matcher(idx, st.search);
    if(hit){
      fr = fr.filter(({i}) => hit(i));
    } else {
      const normRows = getNormalizedRows();
      const pq = prepareNormQuery(st.search);
      fr = fr.filter(({i}) => normRows[i].some(nc => normMatchesPrepared(nc, pq)));
    }
  }
  // Lọc riêng theo từng tiêu đề cột (icon phễu, checkbox nhiều giá trị)
  if(activeColFilters.length){
    fr = fr.filter(({row}) => activeColFilters.every(([ci,set]) => set.has(normalizeCellForExactFilter(row[Number(ci)]))));
  }
  // Ô tìm kiếm "chứa" riêng theo từng cột (dòng ngay dưới tiêu đề) — CỘNG DỒN với icon phễu +
  // ô tìm kiếm chung ở trên. Cột ảo "Trạng thái" (__tpcStatus) và "Tổng KL (kg)" (__tongKL) xử
  // lý riêng bên dưới.
  const activeColSearch = Object.entries(st.colSearch).filter(([k,v]) => k !== '__tpcStatus' && k !== '__tongKL' && String(v||'').trim() !== '');
  if(activeColSearch.length){
    fr = fr.filter(({row}) => activeColSearch.every(([ci,q]) => fuzzyIncludes(row[Number(ci)], q)));
  }
  // Lọc riêng theo cột ẢO "Trạng thái" (chỉ tab Thành Phẩm Chính) — không phải cột thật trong
  // dữ liệu (không có ci để đọc row[ci] trực tiếp như trên), nên phải tính lại trạng thái từng
  // dòng qua getTpcRowStatus() rồi mới so khớp với bộ lọc đã chọn.
  if(modId === 'tpc' && colFilterActive(st.colFilters.__tpcStatus)){
    const info = getTpcStatusInfo();
    if(info){
      const allowed = st.colFilters.__tpcStatus;
      fr = fr.filter(({row}) => allowed.has(getTpcRowStatus(info, row)));
    }
  }
  // Ô tìm kiếm "chứa" cho cột ẢO "Trạng thái" — cũng phải tính lại trạng thái từng dòng như trên.
  if(modId === 'tpc' && String(st.colSearch.__tpcStatus||'').trim() !== ''){
    const info = getTpcStatusInfo();
    if(info){
      const q = fuzzyNormalize(st.colSearch.__tpcStatus);
      fr = fr.filter(({row}) => noSpace(fuzzyNormalize(getTpcRowStatus(info, row))).includes(noSpace(q)));
    }
  }
  // Lọc + tìm kiếm cho cột ẢO "Tổng KL (kg)" (= Tồn cuối (NET) × Trọng lượng đơn vị) — chỉ áp
  // dụng ở các module CÓ cột này (weightBasis khác null, xem getFilterModuleWeightBasis()). So
  // khớp trên GIÁ TRỊ ĐÃ ĐỊNH DẠNG hiển thị trên màn hình (2 số thập phân, dùng
  // filterModuleTongKLDisplay()) để những gì gõ/tick trong popup luôn khớp đúng với những gì
  // đang nhìn thấy trên bảng.
  if(weightBasis){
    if(colFilterActive(st.colFilters.__tongKL)){
      const allowed = st.colFilters.__tongKL;
      fr = fr.filter(({row}) => allowed.has(filterModuleTongKLDisplay(row, weightBasis)));
    }
    if(String(st.colSearch.__tongKL||'').trim() !== ''){
      const q = fuzzyNormalize(st.colSearch.__tongKL);
      fr = fr.filter(({row}) => noSpace(fuzzyNormalize(filterModuleTongKLDisplay(row, weightBasis))).includes(noSpace(q)));
    }
  }
  if(st.sortCol !== null){
    const ci = st.sortCol;
    const numeric = isNumericCol(state.headers[ci]);
    const dir = st.sortDir === 'desc' ? -1 : 1;
    fr = fr.slice().sort((a,b)=>{
      const va = a.row[ci] ?? '', vb = b.row[ci] ?? '';
      if(va === '' && vb === '') return 0;
      if(va === '') return 1;
      if(vb === '') return -1;
      if(numeric){
        const na = parseLooseNumber(va), nb = parseLooseNumber(vb);
        if(!isNaN(na) && !isNaN(nb)) return (na-nb)*dir;
      }
      return String(va).localeCompare(String(vb), 'vi', {numeric:true, sensitivity:'base'}) * dir;
    });
  }
  return fr;
}

/* ---------- Chuyển mã hàng thủ công qua lại giữa 7 module ---------- */
// Số dòng đang thuộc modId do CHUYỂN THỦ CÔNG (không phải do khớp điều kiện lọc tự động).
function countManualInModule(modId){
  const overrides = state.manualAssign || {};
  let n = 0;
  Object.values(overrides).forEach(v => { if(v === modId) n++; });
  return n;
}

// Chuyển các dòng (chỉ số trong state.rows) sang module đích — ghi đè mọi điều kiện lọc tự
// động, dòng sẽ biến mất khỏi module cũ (dù trước đó thuộc module nào) và chỉ còn thuộc
// đúng targetModId. Lưu lại (persist) ngay để không mất khi tải lại trang.
function moveRowsToModule(rowIdxs, targetModId){
  if(!m1CanWrite) return; // PC 7.54: chỉ Quản lý trở lên
  if(!FILTER_MODULES[targetModId]) return;
  rowIdxs.forEach(i => { state.manualAssign[i] = targetModId; });
  persist();
}

// Trả các dòng về lại chế độ tự động (bỏ chuyển thủ công) — dòng sẽ tự khớp module theo đúng
// điều kiện lọc như ban đầu, thay vì cố định vào 1 module do người dùng chỉ định.
function revertRowsToAuto(rowIdxs){
  if(!m1CanWrite) return; // PC 7.54: chỉ Quản lý trở lên
  rowIdxs.forEach(i => { delete state.manualAssign[i]; });
  persist();
}

function sumFilterModuleColumn(modId, label){
  const col = getFilterModuleColumns(modId).find(c => c.label === label);
  if(!col) return null;
  let sum = 0;
  filterModuleBaseRowsCached(modId).forEach(({row})=>{
    const v = parseLooseNumber(row[col.ci] ?? '');
    if(!isNaN(v)) sum += v;
  });
  return sum;
}

function countDistinctFilterModuleColumn(modId, label){
  const col = getFilterModuleColumns(modId).find(c => c.label === label);
  if(!col) return null;
  const set = new Set(filterModuleBaseRowsCached(modId).map(({row}) => String(row[col.ci] ?? '').trim()).filter(v=>v!==''));
  return set.size;
}

// Cột "Trọng lượng" trong file thường là khối lượng ĐƠN VỊ (kg/đơn vị), không phải tổng khối
// lượng tồn kho. Ghép với Tồn cuối (NET) để tính ra tổng khối lượng thực tế.
function getFilterModuleWeightBasis(modId){
  const cols = getFilterModuleColumns(modId);
  const weightCol = cols.find(c => c.label === 'Trọng lượng');
  const qtyCol = cols.find(c => c.label === 'Tồn cuối (NET)') || cols.find(c => c.label === 'Tồn cuối');
  if(!weightCol || !qtyCol) return null;
  return { qtyCi: qtyCol.ci, weightCi: weightCol.ci };
}

function filterModuleRowTotalWeight(row, basis){
  const qty = parseLooseNumber(row[basis.qtyCi] ?? '');
  const w = parseLooseNumber(row[basis.weightCi] ?? '');
  if(isNaN(qty) || isNaN(w)) return null;
  return qty * w;
}
// Chuỗi hiển thị của "Tổng KL (kg)" — DÙNG CHUNG cho cả ô tìm kiếm/popup lọc lẫn ô hiển thị
// trong bảng (xem chỗ vẽ <td> ở renderFilterModuleRows), để những gì gõ/tick trong popup luôn
// khớp đúng 100% với những gì đang nhìn thấy trên màn hình (kể cả các dòng trống — hiển thị
// "—" cũng dùng chung chuỗi này để có thể lọc riêng "các dòng thiếu dữ liệu để tính Tổng KL").
function filterModuleTongKLDisplay(row, basis){
  const v = filterModuleRowTotalWeight(row, basis);
  return v===null ? '—' : v.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2});
}

function sumFilterModuleTotalWeight(modId){
  const basis = getFilterModuleWeightBasis(modId);
  if(!basis) return null;
  let sum = 0;
  filterModuleBaseRowsCached(modId).forEach(({row})=>{
    const v = filterModuleRowTotalWeight(row, basis);
    if(v !== null) sum += v;
  });
  return sum;
}

/* ---------- Thống kê nhanh cho TOÀN BỘ dữ liệu Load Data (không qua bộ lọc module nào) ---------- */
function countDistinctColumnAllRows(label){
  const ci = state.headers.findIndex(h => headersFuzzyMatch(h, label));
  if(ci === -1) return null;
  const set = new Set(state.rows.map(row => String(row[ci] ?? '').trim()).filter(v=>v!==''));
  return set.size;
}
function sumColumnAllRows(label){
  const ci = state.headers.findIndex(h => headersFuzzyMatch(h, label));
  if(ci === -1) return null;
  let sum = 0;
  state.rows.forEach(row=>{
    const v = parseLooseNumber(row[ci] ?? '');
    if(!isNaN(v)) sum += v;
  });
  return sum;
}
// "Tổng KL (kg)" = Tồn cuối × Trọng lượng (kg/đơn vị). "Tồn cuối (NET)" đã là KG net nên KHÔNG nhân với
// Trọng lượng. Trước 09/2026 hàm này tìm 'Tồn cuối (NET)' bằng so khớp mờ nên thực tế vẫn ra cột
// "Tồn cuối" (đứng trước) — nay ghi rõ, kết quả không đổi. Bản chép: dashboard-overview.js (ô Tổng KG) và
// ban-dien-thoai/www/app.js (rawKg của kho Tồn kho An An) — sửa 1 nơi thì sửa cả 3.
function getWeightBasisAllRows(){
  const weightCi = state.headers.findIndex(h => headersFuzzyMatch(h, 'Trọng lượng'));
  let qtyCi = state.headers.findIndex(h => fuzzyNormalize(h) === fuzzyNormalize('Tồn cuối'));
  if(qtyCi === -1) qtyCi = state.headers.findIndex(h => headersFuzzyMatch(h, 'Tồn cuối'));
  if(weightCi === -1 || qtyCi === -1) return null;
  return { qtyCi, weightCi };
}
function sumTotalWeightAllRows(){
  const basis = getWeightBasisAllRows();
  if(!basis) return null;
  let sum = 0;
  state.rows.forEach(row=>{
    const qty = parseLooseNumber(row[basis.qtyCi] ?? '');
    const w = parseLooseNumber(row[basis.weightCi] ?? '');
    if(!isNaN(qty) && !isNaN(w)) sum += qty * w;
  });
  return sum;
}

// Gộp tỉ lệ % theo Size — CHỈ lấy đúng các dòng Size đang hiển thị trong "Bảng Tổng Hợp HLSO"
// (kind 'group-size' trong getHlsoSummaryRows(), xem chú thích ở đó), KHÔNG lấy toàn bộ Load
// Data như trước — để bảng "Tỉ Lệ % Theo Size" luôn khớp đúng những gì đang hiển thị ở "Bảng
// Tổng Hợp HLSO". Dùng displayLabel (đã gắn tiền tố TGC/NL/TGS) làm nhãn hiển thị để không bị
// lẫn khi 2 cụm module khác nhau cùng có 1 giá trị Size giống nhau; TỔNG = cộng dồn CHỈ các
// dòng Size này (không cộng thêm dòng tiêu đề cụm/dòng "static" để tránh tính trùng).
function getBaoCaoSizeBreakdown(){
  const sizeRowDescs = getHlsoSummaryRows().filter(r => r.kind === 'group-size');
  if(sizeRowDescs.length === 0) return null;
  const rows = sizeRowDescs
    .map(r => ({ label: r.displayLabel, weight: getHlsoSummaryRowTotal(r) }))
    .filter(r => r.weight !== null && r.weight !== undefined);
  if(rows.length === 0) return null;
  const total = rows.reduce((sum, r) => sum + r.weight, 0);
  return { rows, total };
}

// ---- Lọc kiểu Excel (chọn nhiều Size cùng lúc) cho bảng "Tỉ Lệ % Theo Size" ở renderBaoCao()
// — chọn xong cộng dồn TỔNG % của các Size đã chọn vào dòng "TỔNG ĐÃ CHỌN" riêng. Giống hệt
// cách làm ở module "Tồn Kho Gửi" (refreshBaoCaoPctTables). ----
let baocaoSizePctAllRows = [];
let baocaoSizePctGrandTotal = 0;
// null = không lọc (hiện tất cả); Set các label = chỉ hiện đúng những Size có trong Set.
let baocaoSizePctSelectedSet = null;

function baocaoPctBarHtml(pct){
  if(pct===null || pct===undefined || isNaN(pct)) return '<span class="cell-empty">—</span>';
  return `<div style="display:flex;align-items:center;gap:8px;justify-content:flex-end;">
      <div style="flex:1;max-width:64px;height:6px;border-radius:3px;background:var(--border);overflow:hidden;">
        <div style="width:${Math.min(100, pct).toFixed(1)}%;height:100%;background:var(--ice, var(--accent));"></div>
      </div>
      <span style="min-width:42px;text-align:right;">${pct.toFixed(1)}%</span>
    </div>`;
}

function renderBaoCaoSizePctBody(){
  const tbody = document.getElementById('baocaoSizePctTbody');
  const tfoot = document.getElementById('baocaoSizePctTfoot');
  if(!tbody || !tfoot) return;
  const isFiltered = baocaoSizePctSelectedSet !== null;
  const visibleRows = isFiltered ? baocaoSizePctAllRows.filter(r => baocaoSizePctSelectedSet.has(r.label)) : baocaoSizePctAllRows;

  let html = '';
  visibleRows.forEach(r=>{
    const pct = baocaoSizePctGrandTotal ? (r.weight / baocaoSizePctGrandTotal * 100) : null;
    html += `<tr>
      <td style="font-weight:700;color:var(--text);">${escapeHtml(r.label)}</td>
      <td class="num">${r.weight.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2})}</td>
      <td class="num">${baocaoPctBarHtml(pct)}</td>
    </tr>`;
  });
  tbody.innerHTML = html;

  // Chỉ hiện dòng "TỔNG ĐÃ CHỌN" khi đang lọc (chọn ít hơn toàn bộ) — cộng dồn đúng % của các
  // Size đang được chọn/hiển thị. Đặt trong <tfoot> (như dòng "TỔNG") để giữ đúng kiểu chữ đậm/
  // màu nổi bật, dính khi cuộn bảng.
  let footHtml = '';
  if(isFiltered){
    const selectedWeight = visibleRows.reduce((s,r)=> s + r.weight, 0);
    const selectedPct = baocaoSizePctGrandTotal ? (selectedWeight / baocaoSizePctGrandTotal * 100) : 0;
    footHtml += `<tr>
      <td class="total-label">TỔNG ĐÃ CHỌN (${visibleRows.length} size)</td>
      <td class="num">${selectedWeight.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2})}</td>
      <td class="num">${selectedPct.toFixed(1)}%</td>
    </tr>`;
  }
  footHtml += `<tr>
    <td class="total-label">TỔNG</td>
    <td class="num">${baocaoSizePctGrandTotal.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2})}</td>
    <td class="num">100.0%</td>
  </tr>`;
  tfoot.innerHTML = footHtml;
  initExcelLikeSelection(tbody.closest('table'));
}

function closeBaoCaoSizePctPopover(){
  const el = document.getElementById('baocaoSizePctPopover');
  if(el) el.remove();
  document.removeEventListener('click', baocaoSizePctPopoverOutsideClick, true);
}
function baocaoSizePctPopoverOutsideClick(e){
  const el = document.getElementById('baocaoSizePctPopover');
  if(el && !el.contains(e.target) && e.target.id !== 'btnBaoCaoSizePctFilter') closeBaoCaoSizePctPopover();
}

function toggleBaoCaoSizePctPopover(anchorBtn){
  const existing = document.getElementById('baocaoSizePctPopover');
  if(existing){ closeBaoCaoSizePctPopover(); return; }

  const rect = anchorBtn.getBoundingClientRect();
  const pop = document.createElement('div');
  pop.id = 'baocaoSizePctPopover';
  pop.style.cssText = `position:fixed; top:${rect.bottom+4}px; left:${rect.left}px; z-index:500; width:220px;
    background:var(--surface); border:1px solid var(--border); border-radius:8px; box-shadow:0 10px 30px rgba(0,0,0,0.35);
    padding:8px; font-size:12.5px; color:var(--text); max-height:320px; display:flex; flex-direction:column;`;

  const searchInput = document.createElement('input');
  searchInput.type = 'text';
  searchInput.placeholder = 'Gõ để tìm / chọn nhanh Size…';
  searchInput.style.cssText = 'width:100%; box-sizing:border-box; margin-bottom:6px; padding:6px 8px; font-size:12px; border-radius:6px; border:1px solid var(--border); background:var(--panel-hover); color:var(--text); outline:none;';
  pop.appendChild(searchInput);

  const actionsRow = document.createElement('div');
  actionsRow.style.cssText = 'display:flex; gap:8px; margin-bottom:6px; padding-bottom:6px; border-bottom:1px solid var(--border);';
  const btnAll = document.createElement('button');
  btnAll.textContent = 'Chọn tất cả';
  btnAll.type = 'button';
  btnAll.style.cssText = 'flex:1; font-size:11.5px; padding:4px 6px; cursor:pointer; background:var(--panel-hover); border:1px solid var(--border); border-radius:5px; color:inherit;';
  const btnNone = document.createElement('button');
  btnNone.textContent = 'Bỏ chọn hết';
  btnNone.type = 'button';
  btnNone.style.cssText = btnAll.style.cssText;
  actionsRow.appendChild(btnAll);
  actionsRow.appendChild(btnNone);
  pop.appendChild(actionsRow);

  const listWrap = document.createElement('div');
  listWrap.style.cssText = 'overflow-y:auto; overflow-x:hidden; flex:1; display:flex; flex-direction:column;';
  baocaoSizePctAllRows.forEach(r=>{
    const isChecked = baocaoSizePctSelectedSet === null || baocaoSizePctSelectedSet.has(r.label);
    const row = document.createElement('label');
    row.style.cssText = 'display:flex; align-items:center; gap:6px; padding:3px 2px; cursor:pointer; width:100%; box-sizing:border-box; flex-shrink:0;';
    row.dataset.searchText = r.label.toLowerCase();
    row.innerHTML = `<input type="checkbox" ${isChecked?'checked':''} data-size-label="${escapeHtml(r.label)}" style="cursor:pointer;flex-shrink:0;"><span style="flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">${escapeHtml(r.label)}</span>`;
    listWrap.appendChild(row);
  });
  pop.appendChild(listWrap);

  document.body.appendChild(pop);
  searchInput.focus();

  const applyFromCheckboxes = ()=>{
    const checked = Array.from(listWrap.querySelectorAll('input[type=checkbox]:checked')).map(cb=>cb.dataset.sizeLabel);
    baocaoSizePctSelectedSet = (checked.length === baocaoSizePctAllRows.length) ? null : new Set(checked);
    renderBaoCaoSizePctBody();
  };
  // Gõ chữ -> lọc nhanh danh sách bên dưới (ẩn dòng không khớp). Bấm Enter -> CHỈ chọn đúng
  // các dòng đang hiện sau khi lọc (bỏ chọn dòng khác) (đánh tay chọn nhanh 1 hoặc nhiều size cùng lúc).
  searchInput.addEventListener('input', ()=>{
    const kw = searchInput.value.trim().toLowerCase();
    Array.from(listWrap.children).forEach(row=>{
      row.style.display = (!kw || row.dataset.searchText.includes(kw)) ? '' : 'none';
    });
  });
  searchInput.addEventListener('keydown', (e)=>{
    if(e.key !== 'Enter') return;
    e.preventDefault();
    const visibleRows = Array.from(listWrap.children).filter(row => row.style.display !== 'none');
    if(visibleRows.length === 0) return;
    // Enter = lấy ĐÚNG các dòng đang hiện (kiểu Excel): tick dòng khớp từ khoá, BỎ tick mọi dòng không khớp — trước
    // đây giữ nguyên các size đã tick từ trước (không khớp từ khoá) nên phải bấm "Bỏ chọn hết" rồi chọn lại (PC 2.4).
    Array.from(listWrap.children).forEach(row => { row.querySelector('input[type=checkbox]').checked = row.style.display !== 'none'; });
    applyFromCheckboxes();
  });
  listWrap.addEventListener('change', applyFromCheckboxes);
  btnAll.addEventListener('click', ()=>{
    listWrap.querySelectorAll('input[type=checkbox]').forEach(cb=>cb.checked=true);
    applyFromCheckboxes();
  });
  btnNone.addEventListener('click', ()=>{
    listWrap.querySelectorAll('input[type=checkbox]').forEach(cb=>cb.checked=false);
    applyFromCheckboxes();
  });

  setTimeout(()=> document.addEventListener('click', baocaoSizePctPopoverOutsideClick, true), 0);
}

// Tổng trọng lượng cho "TỔNG TỒN KHO" ở module Báo Cáo — CỘNG DỒN từ 7 module lọc, KHÔNG
// lấy trực tiếp từ toàn bộ Load Data — để nếu 1 module lọc có sai sót điều kiện (thiếu/dư
// dòng), số Tổng ở Báo Cáo vẫn phản ánh đúng NHỮNG GÌ ĐANG HIỂN THỊ trên các module, thay vì
// một con số tổng riêng có thể không khớp với tổng các module cộng lại.
function sumBaoCaoModuleWeights(){
  let sum = 0, has = false;
  Object.keys(FILTER_MODULES).forEach(modId=>{
    const w = sumFilterModuleTotalWeight(modId);
    if(w !== null){ sum += w; has = true; }
  });
  return has ? sum : null;
}

/* ================= ĐỐI SOÁT: tìm dòng bị đếm trùng nhiều module / bị bỏ sót =================
   Vì mỗi module (trừ "Thành Phẩm Tạm") tự lọc độc lập theo điều kiện riêng, 1 dòng dữ liệu
   HOÀN TOÀN CÓ THỂ thỏa điều kiện tự động của ≥2 module cùng lúc (VD: "Tôm Giám Sát" chỉ kiểm
   tra cột "Mạ băng", không xét Khách hàng — nên có thể khớp cả điều kiện của HLSO/Tôm Gia
   Công/Tôm Nhập Khẩu/Tôm Nguyên Liệu). Để tránh CỘNG 2 LẦN trở lên vào TỔNG TỒN KHO ở Báo Cáo,
   filterModuleBaseRows() đã tự ƯU TIÊN dòng đó về ĐÚNG 1 module — module nào CÓ điều kiện phụ
   theo cột "Khách hàng" thắng module không có (xem moduleHasKhachHangCondition()). Hàm này
   dùng lại chính danh sách dòng đã ưu tiên đó (filterModuleBaseRowsCached — giống hệt dữ liệu
   đang hiển thị/được cộng tổng ở mỗi module) để quét xem còn dòng nào vẫn thuộc ≥2 module sau
   khi ưu tiên (trường hợp lỗi cấu hình chưa lường tới) hoặc bị bỏ sót, không rơi vào module nào
   kể cả "Thành Phẩm Tạm" (không nên xảy ra nếu cấu hình đúng, nhưng vẫn kiểm tra để phòng lỗi
   cấu hình/thiếu cột).
*/
function computeModuleReconciliation(){
  if(!state.rows.length || !state.headers.length) return null;

  const allIds = Object.keys(FILTER_MODULES);
  const moduleRowSets = {};
  allIds.forEach(id => {
    moduleRowSets[id] = new Set(filterModuleBaseRowsCached(id).map(({i}) => i));
  });

  const basis = getWeightBasisAllRows();
  const maCi = state.headers.findIndex(h => headersFuzzyMatch(h, 'Mã hàng'));
  const tenCi = state.headers.findIndex(h => headersFuzzyMatch(h, 'Tên hàng'));
  const sizeCi = state.headers.findIndex(h => headersFuzzyMatch(h, 'Size'));
  const moduleLabel = id => (MODULES.find(m=>m.id===id)||{}).label || id;

  const overlapRows = [];
  const orphanRows = [];
  let overlapExcessWeight = 0;

  state.rows.forEach((row, i) => {
    const hit = allIds.filter(id => moduleRowSets[id].has(i));
    const totalHit = hit.length;
    const w = basis ? filterModuleRowTotalWeight(row, basis) : null;

    if(totalHit >= 2){
      const labels = hit.map(moduleLabel);
      const excess = w !== null ? w * (totalHit - 1) : null;
      if(excess !== null) overlapExcessWeight += excess;
      overlapRows.push({
        i,
        maHang: maCi!==-1 ? row[maCi] : '',
        tenHang: tenCi!==-1 ? row[tenCi] : '',
        size: sizeCi!==-1 ? row[sizeCi] : '',
        modules: labels,
        weight: w,
        excess,
      });
    } else if(totalHit === 0){
      orphanRows.push({
        i,
        maHang: maCi!==-1 ? row[maCi] : '',
        tenHang: tenCi!==-1 ? row[tenCi] : '',
        size: sizeCi!==-1 ? row[sizeCi] : '',
        weight: w,
      });
    }
  });

  overlapRows.sort((a,b)=> (b.excess||0) - (a.excess||0));

  // Cảnh báo cột thiếu (primaryMatchCol / extraFilters) — khi thiếu cột, điều kiện tương ứng
  // bị BỎ QUA HOÀN TOÀN (xem filterModuleBaseRows/buildModuleMatcher), khiến module đó lọc
  // lỏng hơn dự kiến và dễ trùng với module khác.
  const columnWarnings = [];
  Object.entries(FILTER_MODULES).forEach(([id, cfg])=>{
    const missing = [];
    if(cfg.primaryMatchCol && state.headers.findIndex(h=>headersFuzzyMatch(h, cfg.primaryMatchCol)) === -1){
      missing.push(cfg.primaryMatchCol);
    }
    (cfg.extraFilters||[]).forEach(ef=>{
      if(ef.col && state.headers.findIndex(h=>headersFuzzyMatch(h, ef.col)) === -1){
        missing.push(ef.col);
      }
    });
    if(missing.length){
      columnWarnings.push({ label: moduleLabel(id), missing });
    }
  });

  return {
    overlapRows,
    orphanRows,
    overlapExcessWeight,
    orphanWeight: orphanRows.reduce((s,r)=> s + (r.weight||0), 0),
    columnWarnings,
  };
}

/* ---- Đối Soát: 8 Module vs Load Data — bấm vào 1 dòng chênh lệch để mở/đóng bảng chi tiết
   mã hàng (giống 100% chức năng "Đối Soát Số Liệu" ở module Tồn Kho Gửi). ---- */
const doiSoatOpenSections = {}; // { sectionKey: true/false }
function ds_toggleSection(key){
  doiSoatOpenSections[key] = !doiSoatOpenSections[key];
  renderBaoCao();
}
const DS_DETAIL_MAX_ROWS = 300; // tránh render quá nhiều dòng nếu chênh lệch quá lớn
function ds_fmtKg(n){
  return (n!==null && n!==undefined && !isNaN(n)) ? n.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2}) + ' kg' : '<span class="cell-empty">—</span>';
}
function ds_renderDetailRow(sectionKey, list, columns){
  if(!doiSoatOpenSections[sectionKey]) return '';
  if(!list.length){
    return `<tr class="ds-detail-row"><td colspan="2"><div class="ds-detail-empty">Không có dòng nào.</div></td></tr>`;
  }
  const shown = list.slice(0, DS_DETAIL_MAX_ROWS);
  let inner = '<table class="ds-detail-table"><thead><tr>';
  columns.forEach(c => inner += `<th>${escapeHtml(c.label)}</th>`);
  inner += '</tr></thead><tbody>';
  shown.forEach(item=>{
    inner += '<tr>';
    columns.forEach(c=>{
      const raw = item[c.key];
      let val;
      if(c.key === 'weight' || c.key === 'excess'){
        val = (raw!==null && raw!==undefined && !isNaN(raw)) ? raw.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2}) : '—';
      } else if(c.key === 'modules'){
        val = Array.isArray(raw) ? raw.join(', ') : String(raw ?? '');
      } else {
        val = String(raw ?? '');
      }
      inner += `<td>${escapeHtml(val)}</td>`;
    });
    inner += '</tr>';
  });
  inner += '</tbody></table>';
  if(list.length > DS_DETAIL_MAX_ROWS){
    inner += `<div class="ds-detail-more">… và ${(list.length - DS_DETAIL_MAX_ROWS).toLocaleString('en-US')} dòng khác (đã ẩn bớt để bảng không quá dài).</div>`;
  }
  return `<tr class="ds-detail-row"><td colspan="2">${inner}</td></tr>`;
}
function ds_diffRowClass(diff){
  return 'ds-diff' + (diff!==null && Math.abs(diff) < 0.01 ? ' ds-ok' : '');
}

// Tính tổng cho từng cột số của bảng, dựa trên đúng tập dòng đang hiển thị
// sau khi lọc/tìm kiếm (fr) — không chỉ riêng trang hiện tại.
function filterModuleComputeTotals(frRows, displayCols){
  return displayCols.map(col=>{
    if(col.virtual){
      let sum = 0, has = false;
      frRows.forEach(({row})=>{
        const v = filterModuleRowTotalWeight(row, col.basis);
        if(v !== null){ sum += v; has = true; }
      });
      return has ? sum : null;
    }
    if(!isSummableCol(col.label)) return undefined;
    let sum = 0, has = false;
    frRows.forEach(({row})=>{
      const v = parseLooseNumber(row[col.ci] ?? '');
      if(!isNaN(v)){ sum += v; has = true; }
    });
    return has ? sum : null;
  });
}

/* ---------- Lấp đầy khung bảng khi dữ liệu chưa đủ rộng (bảng Load Data + 7 module lọc) ----------
   Độ rộng TỪNG CỘT vẫn được tính ưu tiên theo ĐÚNG DỮ LIỆU như cũ — không đổi gì ở
   ldDefaultColWidth/filterModuleDefaultColWidth hay độ rộng người dùng đã tự kéo tay (lưu ở
   state.colWidths / state.filterModules[modId].colWidths). Hàm này chỉ chạy SAU KHI bảng đã vẽ
   xong vào DOM, đo khung cuộn ngang thật (.table-scroll) đang hiển thị: nếu TỔNG độ rộng các
   cột (đã tính theo dữ liệu) NHỎ HƠN khung đó, chia đều phần dư THEO TỈ LỆ độ rộng hiện có của
   từng cột (cột vốn đã rộng — VD "Tên hàng" — được chia nhiều hơn; cột hẹp như checkbox/STT gần
   như không đổi) để bảng luôn lấp đầy khung, không còn để trống 1 mảng xám bên phải như trước.
   Nếu dữ liệu đã cần NHIỀU hơn khung hiển thị thì GIỮ NGUYÊN độ rộng theo dữ liệu — khung tự
   cuộn ngang, không co ép lại cột nào (đúng tinh thần "ưu tiên độ rộng theo đúng dữ liệu").
*/
// SỬA LỖI "kéo hẹp/rộng cột trái phải không ăn": với table-layout:fixed, độ rộng của <table>
// PHẢI là 1 con số px CỐ ĐỊNH đúng bằng tổng độ rộng mọi <col> thì trình duyệt mới tôn trọng
// chính xác từng <col> — nếu để "auto" (kể cả khi bị ép rộng thêm qua CSS min-width:100%),
// Chrome tính lại độ rộng "auto" đó dựa trên NỘI DUNG bên trong ô (cột chữ dài không xuống
// dòng sẽ tự phình to hơn hẳn con số vừa gán, các cột khác bị co lại bù) — xem giải thích đầy
// đủ ở css/ton-kho-an-an.css ngay tại rule "table.resizable-table". Gọi hàm này SAU MỖI LẦN
// thay đổi độ rộng bất kỳ <col> nào (vẽ bảng lần đầu, lấp đầy khung, kéo tay đổi 1 cột…) để
// luôn khoá đúng độ rộng thật của <table> theo tổng các <col> hiện tại.
function syncTableWidthToCols(table){
  if(!table) return;
  const colgroup = table.querySelector(':scope > colgroup');
  if(!colgroup) return;
  const cols = colgroup.querySelectorAll('col');
  if(!cols.length) return;
  let sum = 0;
  cols.forEach(c => { sum += parseFloat(c.style.width) || 0; });
  if(sum > 0) table.style.width = Math.round(sum) + 'px';
}

function fillResizableTableWidth(table){
  if(!table) return;
  const colgroup = table.querySelector(':scope > colgroup');
  if(!colgroup) return;
  const cols = colgroup.querySelectorAll('col');
  if(!cols.length) return;
  const scrollEl = table.closest('.table-scroll');
  if(cols.length >= 2 && scrollEl){
    // Cột đầu tiên luôn là cột checkbox (rộng cố định 38px) — không tham gia chia phần dư,
    // tránh kéo dãn bất hợp lý 1 cột vốn rất hẹp.
    const fixedW = parseFloat(cols[0].style.width) || 0;
    const flexCols = Array.prototype.slice.call(cols, 1);
    const widths = flexCols.map(c => parseFloat(c.style.width) || 0);
    const flexSum = widths.reduce((a,b)=>a+b, 0);
    const sum = fixedW + flexSum;
    const available = scrollEl.clientWidth;
    // Chỉ chia phần dư khi dữ liệu chưa đủ rộng khung; nếu đã rộng hơn/bằng khung thì GIỮ
    // NGUYÊN độ rộng theo dữ liệu — khung tự cuộn ngang, không co ép lại cột nào.
    if(flexSum > 0 && sum > 0 && available > sum){
      const extraTotal = available - sum;
      flexCols.forEach((c, idx) => {
        c.style.width = Math.floor(widths[idx] + extraTotal * (widths[idx] / flexSum)) + 'px';
      });
    }
  }
  // QUAN TRỌNG: luôn khoá lại độ rộng THẬT của <table> theo đúng tổng các <col> — dù nhánh
  // "lấp đầy" ở trên có chạy hay không (xem chú thích ở syncTableWidthToCols()).
  syncTableWidthToCols(table);
}
// Bảng đang hiển thị (Load Data hoặc module lọc hiện tại) có thể cần lấp đầy lại khi người dùng
// đổi kích thước cửa sổ ứng dụng (khung .table-scroll rộng/hẹp ra) — không đổi độ rộng đã lưu
// trong state, chỉ tính lại phần lấp đầy dựa trên khung mới.
let __fillTableResizeTimer = null;
window.addEventListener('resize', ()=>{
  clearTimeout(__fillTableResizeTimer);
  __fillTableResizeTimer = setTimeout(()=>{
    document.querySelectorAll('#dataWrap table.resizable-table, [id$="Wrap"] table.resizable-table').forEach(fillResizableTableWidth);
  }, 120);
});

/* ---------- Resizable columns (module lọc dùng chung) ---------- */
const FM_COL_MIN_WIDTH = 70;
const FM_COL_DEFAULT_WIDTH = {
  'ĐVT': 70, 'Size': 80, 'Mạ băng': 90, 'Tồn đầu': 100, 'Nhập': 90, 'Xuất': 90,
  'Tồn cuối': 100, 'Trọng lượng': 110, 'Tổng KL (kg)': 120, 'Tình trạng': 120,
};
// Phương án dự phòng (chưa có dữ liệu để đo) — không còn là nguồn chính, xem
// filterModuleAutoColWidth() bên dưới.
function filterModuleDefaultColWidth(col){
  if(FM_COL_DEFAULT_WIDTH[col.label]) return FM_COL_DEFAULT_WIDTH[col.label];
  return Math.max(100, Math.min(220, col.label.length * 10 + 50));
}

/* ---------- ĐỘ RỘNG CỘT THEO DỮ LIỆU THẬT (tự căn kiểu Excel) ----------
   SỬA LỖI "các bảng không tự căn chỉnh theo kích thước dữ liệu": trước đây 8 menu Phân Loại
   tính độ rộng cột CHỈ theo độ dài TIÊU ĐỀ (label.length*10+50, trần 220px) — không hề nhìn dữ
   liệu, nên cột "Mã hàng"/"Tên hàng" luôn bị cắt "..." dù dữ liệu dài hơn nhiều. Nay đo đúng chữ
   SẼ HIỂN THỊ trong ô (cùng font/cỡ chữ thật với CSS, đo bằng canvas — không đụng layout) trên
   TOÀN BỘ dòng gốc của menu đó (không chỉ trang đang xem), lấy ô rộng nhất.
   - Mặc định trần COL_AUTO_MAX (= LD_COL_MAX_WIDTH, 420px) để 1 ô siêu dài không chiếm hết khung.
   - Nhấp đúp viền cột dùng trần rộng hơn COL_AUTOFIT_MAX (900px) — đúng ý "bung vừa khít".
   Kết quả được cache theo THAM CHIẾU mảng dòng gốc (filterModuleBaseRowsCached) — chỉ tính lại
   khi dữ liệu thực sự đổi (nạp file, chuyển mã hàng...), không tính lại mỗi lần gõ tìm/lọc. */
const COL_AUTOFIT_MAX = 900;
const COL_FIT_HEAD_FONT = '500 11px Inter, sans-serif';   // = thead th (uppercase)
const COL_FIT_CELL_FONT = '400 12.5px Inter, sans-serif'; // = tbody td
const COL_FIT_HEAD_PAD = 28 + 26 + 16 + 8; // padding + nút lọc + mũi tên sắp xếp + tay kéo
const COL_FIT_CELL_PAD = 28 + 6;           // padding ô + đệm chống răng cưa chữ

// Đo độ rộng vừa khít cho 1 cột từ tiêu đề + danh sách chuỗi hiển thị. Bảng có thể vài nghìn
// dòng: gộp giá trị trùng, và nếu vẫn quá nhiều thì chỉ đo ~80 chuỗi DÀI NHẤT theo số ký tự
// (độ dài ký tự tương quan rất sát độ rộng thật) — nhanh mà gần như không sai.
function measureColFitWidth(label, values, maxW, extraCellPad){
  let w = ldTextWidth(String(label||'').toUpperCase(), COL_FIT_HEAD_FONT) + COL_FIT_HEAD_PAD;
  const uniq = Array.from(new Set(values.filter(v => v !== '' && v !== null && v !== undefined).map(v => String(v))));
  const sample = uniq.length > 300 ? uniq.sort((a,b)=>b.length-a.length).slice(0, 80) : uniq;
  const pad = COL_FIT_CELL_PAD + (extraCellPad || 0);
  for(const s of sample){
    const x = ldTextWidth(s, COL_FIT_CELL_FONT) + pad;
    if(x > w) w = x;
  }
  return Math.max(FM_COL_MIN_WIDTH, Math.min(maxW, Math.ceil(w)));
}

// Chuỗi hiển thị THẬT của 1 cột trong menu Phân Loại (khớp đúng cách vẽ ô ở renderFilterModule).
function filterModuleColDisplayValues(modId, col, base){
  if(col.statusBadge) return ['Còn hàng', 'Sắp hết', 'Hết hàng'];
  if(col.virtual){
    return base.map(({row}) => {
      const v = filterModuleRowTotalWeight(row, col.basis);
      return v === null ? '' : v.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2});
    });
  }
  return base.map(({row}) => {
    const val = row[col.ci] ?? '';
    return val === '' ? '' : applySizePrefix(modId, col.label, val);
  });
}

function filterModuleFitColWidth(modId, col, maxW){
  const base = filterModuleBaseRowsCached(modId);
  if(!base || !base.length) return filterModuleDefaultColWidth(col);
  // Cột đầu có thể kèm huy hiệu "⇄" (dòng chuyển thủ công); badge trạng thái có viền/đệm riêng.
  const extra = col.statusBadge ? 26 : 0;
  return measureColFitWidth(col.label, filterModuleColDisplayValues(modId, col, base), maxW, extra);
}

const _fmAutoWidthCache = {}; // modId -> { baseRef, widths: { label: px } }
function filterModuleAutoColWidth(modId, col){
  const base = filterModuleBaseRowsCached(modId);
  let c = _fmAutoWidthCache[modId];
  if(!c || c.baseRef !== base){ c = _fmAutoWidthCache[modId] = { baseRef: base, widths: {} }; }
  if(!(col.label in c.widths)) c.widths[col.label] = filterModuleFitColWidth(modId, col, LD_COL_MAX_WIDTH);
  return c.widths[col.label];
}

function filterModuleColWidth(modId, col){
  return state.filterModules[modId].colWidths[col.label] || filterModuleAutoColWidth(modId, col);
}

// Nhấp đúp viền cột (tay cầm HOẶC viền bất kỳ ô nào) → bung vừa khít dữ liệu của cột đó.
function autoFitFilterModuleColumn(modId, label){
  const st = state.filterModules[modId];
  if(!st) return;
  const view = computeFilterModuleView(modId);
  const col = view.displayCols.find(c => c.label === label);
  if(!col) return;
  st.colWidths[label] = filterModuleFitColWidth(modId, col, COL_AUTOFIT_MAX);
  renderFilterModule(modId);
}
// Trạng thái kéo cột dùng chung, gắn listener ở cấp document MỘT LẦN duy nhất
// (xem cuối file) để tránh chồng listener mỗi lần render lại bảng.
let filterModuleColDrag = null;
function bindFilterModuleColResize(modId){
  document.querySelectorAll(`#${modId}Wrap .col-resizer`).forEach(handle=>{
    // Tay kéo giờ được GẮN VÀO <th> kế tiếp (xem nơi tạo HTML ở renderFilterModule()) nên luôn
    // nổi trên đường biên chung — không cần "nâng z-index khi hover" nữa (xem js/table-resize.js
    // hàm addColResizers để biết lý do cách cũ không hoạt động).
    handle.addEventListener('mousedown', e=>{
      e.preventDefault();
      e.stopPropagation();
      const label = handle.dataset.resizeLabel;
      const colEl = document.querySelector(`#${modId}Wrap col[data-col-label="${CSS.escape(label)}"]`);
      if(!colEl) return;
      filterModuleColDrag = { modId, label, colEl, startX: e.clientX, startWidth: colEl.getBoundingClientRect().width };
      handle.classList.add('resizing');
      document.body.style.cursor = 'col-resize';
      document.body.style.userSelect = 'none';
    });
    handle.addEventListener('dblclick', e=>{
      e.preventDefault();
      e.stopPropagation();
      // Trước đây: xoá độ rộng đã kéo → quay về mặc định (tính theo tiêu đề) — KHÔNG phải tự căn.
      // Nay: bung đúng vừa khít dữ liệu dài nhất của cột, giống nhấp đúp viền cột trong Excel.
      autoFitFilterModuleColumn(modId, handle.dataset.resizeLabel);
    });
  });
}

// Xuất Excel riêng cho bảng "Báo Cáo Tổng Tồn": mỗi module (7 module) thành 1 sheet, lấy
// TOÀN BỘ dữ liệu gốc của module đó (theo đúng điều kiện lọc chính/phụ của module — không
// phải số liệu tổng hợp ở Báo Cáo, và KHÔNG bị ảnh hưởng bởi ô tìm kiếm/lọc cột/sắp xếp mà
// người dùng có thể đang để dở trên từng module).
// Hộp thoại yêu cầu nhập NGÀY BÁO CÁO trước khi xuất Excel tổng hợp — thay vì tự động lấy ngày
// thực tế lúc bấm xuất (VD: xuất file vào 17/09 nhưng báo cáo thực chất là số liệu của ngày
// 15/09 thì cần ghi đúng 15/09, không phải ngày xuất). Trả về chuỗi "dd/mm/yyyy" nếu người dùng
// bấm "Xuất Excel", hoặc null nếu bấm Hủy/đóng (huỷ xuất file).
function promptForReportDate(){
  // Giao diện + kiểm tra hợp lệ nằm ở js/report-date-dialog.js (dùng chung Module 1 + 2).
  // Điền sẵn "Ngày số liệu" của Load Data (không có thì dialog tự lấy hôm nay).
  return window.showReportDateDialog(state.dataDate || '');
}

async function exportBaoCaoAllModulesToExcel(){
  if(!state.rows.length || !state.headers.length){
    alert('Chưa có dữ liệu để xuất. Vào Load Data để nạp file trước.');
    return;
  }
  const reportDateStr = await promptForReportDate();
  if(!reportDateStr) return; // người dùng bấm Hủy — không xuất file
  const wb = buildBaoCaoAllModulesWorkbook(reportDateStr);
  if(!wb){
    alert('Không có module nào để xuất.');
    return;
  }
  await writeWorkbookWithFreezeTopRow(wb, `BaoCao_TongTon_7Module_${exportFileStamp()}.xlsx`);
}
// PC 4.25 — nút "Xuất excel HLSO": CÙNG cấu trúc file với "Xuất excel tổng tồn", chỉ gồm 5 loại Tôm HLSO, Tôm Gia Công, Tôm Nhập Khẩu, Tôm Nguyên Liệu, Tôm Giám Sát.
const BAOCAO_HLSO_EXPORT_IDS = ['hlso', 'tomgc', 'tomnk', 'tomnl', 'tomgs'];
// PC 1.40 — popup chọn sheet cho "Xuất excel HLSO": sheet "Tổng Tồn" + 5 sheet loại tôm (mỗi sheet 1 ô tick).
// Nhớ lựa chọn lần trước (localStorage klhlso_sheets_v1). Trả về { summary:boolean, ids:Set } hoặc null nếu Hủy.
const HLSO_SHEETS_LS = 'klhlso_sheets_v1';
function promptHlsoSheets(){
  return new Promise(resolve => {
    const prevFocus = document.activeElement;
    const items = [{ key:'summary', label:'Tổng Tồn', sub:'Bảng tổng hợp theo loại + Kho Gửi' }]
      .concat(BAOCAO_HLSO_EXPORT_IDS.map(id => {
        const mod = MODULES.find(m => m.id === id);
        let n = null;
        try{ n = filterModuleBaseRowsCached(id).length; }catch(_){ window.klLog && klLog('ton-kho-an-an.js:2115', _); }
        return { key:id, label:(mod && mod.label) || id, sub: n === null ? '' : `${n.toLocaleString('en-US')} dòng` };
      }));
    let saved = null;
    try{ saved = JSON.parse(localStorage.getItem(HLSO_SHEETS_LS) || 'null'); }catch(_){}
    const picked = new Set(Array.isArray(saved) && saved.length ? saved.filter(k => items.some(i => i.key === k)) : items.map(i => i.key));
    if(!picked.size) items.forEach(i => picked.add(i.key));

    const overlay = document.createElement('div');
    overlay.className = 'rd-overlay';
    overlay.innerHTML = `
      <div class="rd-dialog" role="dialog" aria-modal="true" aria-labelledby="hsTitle">
        <div class="rd-head">
          <div class="rd-icon" aria-hidden="true">
            <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="18" height="18" rx="2.5"/><path d="M3 9h18M9 3v18"/></svg>
          </div>
          <div>
            <h3 id="hsTitle" class="rd-title">Chọn sheet để xuất</h3>
            <p class="rd-sub">Tick các sheet muốn có trong file Excel HLSO. Lựa chọn được nhớ cho lần sau.</p>
          </div>
        </div>
        <div class="rd-sheets">
          ${items.map(i => `<label class="rd-sheet-row"><input type="checkbox" data-k="${i.key}"><span class="rd-sheet-name">${i.label}</span><span class="rd-sheet-sub">${i.sub}</span></label>`).join('')}
        </div>
        <div class="rd-meta">
          <span class="rd-preview">Đã chọn: <b id="hsCount">0</b>/${items.length} sheet</span>
          <span><button type="button" class="rd-link" id="hsAll">Chọn tất cả</button> · <button type="button" class="rd-link" id="hsNone">Bỏ hết</button></span>
        </div>
        <div class="rd-error" id="hsErr" role="alert" hidden></div>
        <div class="rd-actions">
          <button type="button" class="rd-btn" id="hsCancel">Hủy</button>
          <button type="button" class="rd-btn primary" id="hsOk">Tiếp tục</button>
        </div>
      </div>`;
    document.body.appendChild(overlay);
    const $ = sel => overlay.querySelector(sel);
    const boxes = Array.from(overlay.querySelectorAll('input[data-k]'));
    const refresh = () => { $('#hsCount').textContent = String(boxes.filter(b => b.checked).length); $('#hsErr').hidden = true; };
    boxes.forEach(b => { b.checked = picked.has(b.dataset.k); b.addEventListener('change', refresh); });
    refresh();
    $('#hsAll').addEventListener('click', () => { boxes.forEach(b => b.checked = true); refresh(); });
    $('#hsNone').addEventListener('click', () => { boxes.forEach(b => b.checked = false); refresh(); });
    function close(result){
      document.removeEventListener('keydown', onKey, true);
      overlay.remove();
      if(prevFocus && typeof prevFocus.focus === 'function'){ try{ prevFocus.focus(); }catch(_){} }
      resolve(result);
    }
    function submit(){
      const keys = boxes.filter(b => b.checked).map(b => b.dataset.k);
      if(!keys.length){ const e = $('#hsErr'); e.textContent = 'Chọn ít nhất 1 sheet để xuất.'; e.hidden = false; return; }
      try{ localStorage.setItem(HLSO_SHEETS_LS, JSON.stringify(keys)); }catch(_){}
      close({ summary: keys.includes('summary'), ids: new Set(keys.filter(k => k !== 'summary')) });
    }
    function onKey(e){
      if(e.key === 'Escape'){ e.preventDefault(); e.stopPropagation(); close(null); }
      else if(e.key === 'Enter' && !(e.target && e.target.tagName === 'BUTTON')){ e.preventDefault(); submit(); }
    }
    document.addEventListener('keydown', onKey, true);
    $('#hsCancel').addEventListener('click', () => close(null));
    $('#hsOk').addEventListener('click', submit);
    overlay.addEventListener('mousedown', e => { if(e.target === overlay) close(null); });
    $('#hsOk').focus();
  });
}
async function exportBaoCaoHlsoToExcel(){
  if(!state.rows.length || !state.headers.length){
    alert('Chưa có dữ liệu để xuất. Vào Load Data để nạp file trước.');
    return;
  }
  const pick = await promptHlsoSheets();
  if(!pick) return; // Hủy — không xuất file
  let reportDateStr = '';
  if(pick.summary){ // ngày báo cáo chỉ ghi ở sheet Tổng Tồn
    reportDateStr = await promptForReportDate();
    if(!reportDateStr) return; // người dùng bấm Hủy — không xuất file
  }
  const wb = buildBaoCaoAllModulesWorkbook(reportDateStr, { only: new Set(BAOCAO_HLSO_EXPORT_IDS), pickIds: pick.ids, skipSummary: !pick.summary });
  if(!wb){
    alert('Không có sheet nào để xuất.');
    return;
  }
  await writeWorkbookWithFreezeTopRow(wb, `BaoCao_TongTon_HLSO_${exportFileStamp()}.xlsx`);
}
// Tên file dạng yyyymmdd_hhmm (giờ lúc xuất) — dùng chung cho các file báo cáo.
function exportFileStamp(){
  const ts = new Date();
  return `${ts.getFullYear()}${String(ts.getMonth()+1).padStart(2,'0')}${String(ts.getDate()).padStart(2,'0')}_${String(ts.getHours()).padStart(2,'0')}${String(ts.getMinutes()).padStart(2,'0')}`;
}
// Dựng workbook "Báo Cáo Tổng Tồn" (sheet Tổng Tồn + 7 sheet module) — trả về null nếu không có
// module nào. Dùng chung cho nút Xuất Excel trên PC và file báo cáo gửi lên điện thoại.
function buildBaoCaoAllModulesWorkbook(reportDateStr, opt){ // PC 4.25: opt.only = Set id module (xuất HLSO)
  const onlyIds = opt && opt.only ? opt.only : null;
  const wb = XLSX.utils.book_new();
  let sheetCount = 0;

  // ---------- Sheet đầu tiên: "Tổng Tồn" — lấy đúng dữ liệu bảng "Báo Cáo Tổng Tồn" ----------
  if(!(opt && opt.skipSummary)){ // PC 1.40: có thể bỏ sheet "Tổng Tồn" khi người dùng không tick
    const summaryWs = buildTongTonSummarySheet(reportDateStr, opt);
    XLSX.utils.book_append_sheet(wb, summaryWs, 'Tổng Tồn');
    if(opt && opt.pickIds) sheetCount++; // chế độ chọn sheet: file chỉ có Tổng Tồn vẫn hợp lệ
  }
  const pickIds = opt && opt.pickIds ? opt.pickIds : null; // PC 1.40: chỉ các sheet module đã tick
  MODULES.filter(m => FILTER_MODULES[m.id] && (!onlyIds || onlyIds.has(m.id)) && (!pickIds || pickIds.has(m.id))).forEach(mod=>{
    const modId = mod.id;
    const cols = getFilterModuleExportColumns(modId);
    const weightBasis = getFilterModuleWeightBasis(modId);
    const displayCols = cols.map(c => ({ ...c, virtual: false }));
    if(weightBasis){
      const insertAt = displayCols.findIndex(c => c.label === 'Trọng lượng') + 1;
      displayCols.splice(insertAt, 0, { label: 'Tổng KL (kg)', virtual: true, basis: weightBasis });
    }
    // PC 1.27: CHỈ 2 sheet "Thành Phẩm Chính" (tpc) và "Thành Phẩm Tạm" (tptam) thêm 2 cột giống sheet
    // "Tổng Tồn": "Loại hàng" (4 ký tự đầu của Mã hàng — cột ảo) và "Khách hàng" (lấy từ Load Data).
    // Vị trí theo thứ tự sheet "Tổng Tồn": Loại hàng ngay sau Tên hàng; Khách hàng ngay sau Size. Sheet khác không đổi.
    if(modId === 'tpc' || modId === 'tptam'){
      const maCi = state.headers.findIndex(h => headersFuzzyMatch(h, 'Mã hàng'));
      const khCi = state.headers.findIndex(h => headersFuzzyMatch(h, 'Khách hàng'));
      if(khCi !== -1){
        const sizeAt = displayCols.findIndex(c => c.label === 'Size');
        displayCols.splice(sizeAt === -1 ? displayCols.length : sizeAt + 1, 0, { label: 'Khách hàng', ci: khCi, virtual: false });
      }
      if(maCi !== -1){
        const tenAt = displayCols.findIndex(c => c.label === 'Tên hàng');
        displayCols.splice(tenAt === -1 ? displayCols.length : tenAt + 1, 0, { label: 'Loại hàng', ci: maCi, virtual: false, deriveLoaiHang: true });
      }
    }
    // Toàn bộ dòng gốc của module — theo điều kiện chính + phụ, KHÔNG áp tìm kiếm/lọc cột/
    // sắp xếp hiện tại của người dùng trên module đó.
    const baseRows = filterModuleBaseRowsCached(modId);
    const totals = displayCols.length ? filterModuleComputeTotals(baseRows, displayCols) : [];

    // Dòng Tổng-theo-SUBTOTAL đặt NGAY TRÊN dòng tiêu đề (giống bảng gốc trên màn hình): giá
    // trị ban đầu = tổng toàn bộ dữ liệu xuất ra (giống hệt dòng "Tổng" tĩnh ở cuối sheet), NHƯNG
    // ghi dưới dạng CÔNG THỨC =SUBTOTAL(109,...) tham chiếu đúng vùng dữ liệu bên dưới — nhờ vậy
    // khi người dùng bật/đổi bộ lọc cột (autofilter) ngay trên sheet Excel này, số Tổng tự động
    // cập nhật lại theo đúng các dòng đang hiển thị (đã lọc), không cần quay lại app xuất lại file.
    // (109 = hàm SUM nhưng BỎ QUA các dòng đã bị autofilter lọc ẩn hoặc ẩn tay.)
    const subtotalRow = displayCols.map((col, idx)=>{
      const summable = isSummableCol(col.label) || col.virtual;
      if(!summable) return '';
      const t = totals[idx];
      return (t === undefined || t === null) ? 0 : t;
    });

    const headerRow = displayCols.length ? displayCols.map(c => c.label) : ['(Không tìm thấy cột dữ liệu)'];
    const aoa = displayCols.length ? [subtotalRow, headerRow] : [headerRow];
    if(displayCols.length){
      baseRows.forEach(({row})=>{
        aoa.push(displayCols.map(col=>{
          if(col.virtual){
            const v = filterModuleRowTotalWeight(row, col.basis);
            return v === null ? '' : v;
          }
          const val = row[col.ci] ?? '';
          if(col.deriveLoaiHang) return String(val).trim().slice(0, 4);
          if(isNumericCol(col.label) && val !== ''){
            const n = parseLooseNumber(val);
            return isNaN(n) ? val : n;
          }
          return applySizePrefix(modId, col.label, val);
        }));
      });

      let labelPlaced = false;
      const totalRow = displayCols.map((col, idx)=>{
        const t = totals[idx];
        if(t === undefined){
          if(!labelPlaced){ labelPlaced = true; return `Tổng (${baseRows.length.toLocaleString('en-US')} dòng)`; }
          return '';
        }
        return t === null ? '' : t;
      });
      aoa.push(totalRow);
    }

    const ws = XLSX.utils.aoa_to_sheet(aoa);
    if(displayCols.length){
      ws['!cols'] = computeAutoColWidths(aoa);
      const totalRowIdx = aoa.length - 1;
      const headerRowIdx = 1;
      const subtotalRowIdx = 0;
      styleExportedSheet(ws, displayCols, totalRowIdx, { headerRowIdx, subtotalRowIdx });

      // Ghi công thức SUBTOTAL(109,...) vào từng ô số của dòng Tổng-trên-đầu. Vùng tham chiếu
      // CHỈ gồm các dòng dữ liệu thật (không gồm dòng "Tổng (n dòng)" tĩnh ở cuối sheet, tránh
      // cộng dồn 2 lần).
      if(baseRows.length > 0){
        const firstDataExcelRow = headerRowIdx + 2; // header ở excel row (headerRowIdx+1), data ngay sau
        const lastDataExcelRow = firstDataExcelRow + baseRows.length - 1;
        displayCols.forEach((col, idx)=>{
          const summable = isSummableCol(col.label) || col.virtual;
          if(!summable) return;
          const colLetter = XLSX.utils.encode_col(idx);
          const addr = XLSX.utils.encode_cell({ r: subtotalRowIdx, c: idx });
          if(ws[addr]) ws[addr].f = `SUBTOTAL(109,${colLetter}${firstDataExcelRow}:${colLetter}${lastDataExcelRow})`;
        });
      }
    }
    const sheetName = (mod.label || modId).slice(0, 31);
    XLSX.utils.book_append_sheet(wb, ws, sheetName);
    sheetCount++;
  });

  return sheetCount === 0 ? null : wb;
}

// Xuất toàn bộ dữ liệu đang hiển thị (đã áp điều kiện chính/phụ + tìm kiếm + lọc từng cột +
// sắp xếp — không chỉ riêng trang hiện tại) của 1 module lọc ra file Excel (.xlsx).
async function exportFilterModuleToExcel(modId){ if(typeof window !== 'undefined' && window.klNapXlsx) await window.klNapXlsx(); /* PC 1.85: thư viện Excel nạp lười */
  const mod = MODULES.find(m=>m.id===modId);
  const cols = getFilterModuleExportColumns(modId);
  const weightBasis = getFilterModuleWeightBasis(modId);
  const displayCols = cols.map(c => ({ ...c, virtual: false }));
  if(weightBasis){
    const insertAt = displayCols.findIndex(c => c.label === 'Trọng lượng') + 1;
    displayCols.splice(insertAt, 0, { label: 'Tổng KL (kg)', virtual: true, basis: weightBasis });
  }
  const fr = filterModuleFilteredRows(modId);
  if(displayCols.length === 0 || fr.length === 0){
    alert('Không có dữ liệu để xuất.');
    return;
  }

  const aoa = [displayCols.map(c => c.label)];
  fr.forEach(({row})=>{
    aoa.push(displayCols.map(col=>{
      if(col.virtual){
        const v = filterModuleRowTotalWeight(row, col.basis);
        return v === null ? '' : v;
      }
      const val = row[col.ci] ?? '';
      if(isNumericCol(col.label) && val !== ''){
        const n = parseLooseNumber(val);
        return isNaN(n) ? val : n;
      }
      return applySizePrefix(modId, col.label, val);
    }));
  });

  // Dòng Tổng — cộng đúng các cột số trên tập dòng đã xuất (fr), giống dòng "Tổng" trên bảng.
  const totals = filterModuleComputeTotals(fr, displayCols);
  let labelPlaced = false;
  const totalRow = displayCols.map((col, idx)=>{
    const t = totals[idx];
    if(t === undefined){
      if(!labelPlaced){ labelPlaced = true; return `Tổng (${fr.length.toLocaleString('en-US')} dòng)`; }
      return '';
    }
    return t === null ? '' : t;
  });
  aoa.push(totalRow);

  const ws = XLSX.utils.aoa_to_sheet(aoa);
  ws['!cols'] = computeAutoColWidths(aoa);
  const totalRowIdx = aoa.length - 1; // dòng Tổng, tính theo chỉ số 0
  styleExportedSheet(ws, displayCols, totalRowIdx);

  const wb = XLSX.utils.book_new();
  const sheetName = (mod.label || modId).slice(0, 31);
  XLSX.utils.book_append_sheet(wb, ws, sheetName);

  const ts = new Date();
  const stamp = `${ts.getFullYear()}${String(ts.getMonth()+1).padStart(2,'0')}${String(ts.getDate()).padStart(2,'0')}_${String(ts.getHours()).padStart(2,'0')}${String(ts.getMinutes()).padStart(2,'0')}`;
  await writeWorkbookWithFreezeTopRow(wb, `${(mod.label||modId).replace(/\s+/g,'_')}_${stamp}.xlsx`);
}

// Xuất Excel cho menu "Dữ Liệu": xuất TOÀN BỘ dữ liệu đã lưu (theo state.headers/state.rows),
// áp đúng bộ tìm/lọc theo cột đang bật (filteredRows()) để khớp những gì đang thấy trên bảng.
// Dùng chung các helper định dạng (computeAutoColWidths/styleExportedSheet/freeze top row) với
// các hàm xuất khác để file ra đồng nhất (kẻ ô, tô tiêu đề, canh phải + định dạng số).
async function exportDuLieuToExcel(){ if(typeof window !== 'undefined' && window.klNapXlsx) await window.klNapXlsx(); /* PC 1.85: thư viện Excel nạp lười */
  if(!state.rows.length || !state.headers.length){ alert('Không có dữ liệu để xuất.'); return; }
  const fr = filteredRows();
  if(fr.length === 0){ alert('Không có dòng nào để xuất (kiểm tra lại bộ lọc theo cột).'); return; }
  const displayCols = state.headers.map((h, ci) => ({ label: h, ci }));
  const aoa = [displayCols.map(c => c.label)];
  fr.forEach(({row})=>{
    aoa.push(displayCols.map(col=>{
      const val = row[col.ci] ?? '';
      if(isNumericCol(col.label) && val !== ''){
        const n = parseLooseNumber(val);
        return isNaN(n) ? val : n;
      }
      return val;
    }));
  });
  // Dòng "Tổng" cuối bảng: cộng các cột cộng-dồn-được (isSummableCol — loại trừ "Trọng lượng"
  // vì là khối lượng ĐƠN VỊ/kiện, cộng dồn vô nghĩa), đặt nhãn "Tổng (N dòng)" ở ô chữ đầu tiên.
  let labelPlaced = false;
  const totalRow = displayCols.map(col=>{
    if(isSummableCol(col.label)){
      let s = 0, any = false;
      fr.forEach(({row})=>{ const n = parseLooseNumber(row[col.ci]); if(!isNaN(n)){ s += n; any = true; } });
      return any ? s : '';
    }
    if(!labelPlaced){ labelPlaced = true; return `Tổng (${fr.length.toLocaleString('en-US')} dòng)`; }
    return '';
  });
  aoa.push(totalRow);

  const ws = XLSX.utils.aoa_to_sheet(aoa);
  ws['!cols'] = computeAutoColWidths(aoa);
  styleExportedSheet(ws, displayCols, aoa.length - 1);
  const wb = XLSX.utils.book_new();
  XLSX.utils.book_append_sheet(wb, ws, 'Dữ Liệu');
  const ts = new Date();
  const stamp = `${ts.getFullYear()}${String(ts.getMonth()+1).padStart(2,'0')}${String(ts.getDate()).padStart(2,'0')}_${String(ts.getHours()).padStart(2,'0')}${String(ts.getMinutes()).padStart(2,'0')}`;
  await writeWorkbookWithFreezeTopRow(wb, `DuLieu_${stamp}.xlsx`);
}

// Kẻ ô (viền), tô màu tiêu đề/dòng Tổng, căn phải + định dạng số có dấu phân cách nghìn cho
// cột số, và bật bộ lọc tự động (autofilter) trên dòng tiêu đề — áp dụng cho mọi file Excel
// xuất ra từ các module lọc. Dùng thư viện xlsx-js-style (tương thích API với xlsx gốc) để
// có thể ghi được màu/viền/font — bản xlsx miễn phí gốc không hỗ trợ ghi style.
const XLSX_BORDER_COLOR = '000000';
const XLSX_THIN_BORDER = {
  top:    { style: 'thin', color: { rgb: XLSX_BORDER_COLOR } },
  bottom: { style: 'thin', color: { rgb: XLSX_BORDER_COLOR } },
  left:   { style: 'thin', color: { rgb: XLSX_BORDER_COLOR } },
  right:  { style: 'thin', color: { rgb: XLSX_BORDER_COLOR } },
};
// ---------- Độ rộng cột tự động theo NỘI DUNG THỰC TẾ trong file Excel ----------
// Dùng CHUNG cho MỌI hàm xuất Excel trong file này: quét toàn bộ giá trị của từng cột (kể cả
// dòng tiêu đề) trong mảng `aoa` (mảng 2 chiều dùng để dựng sheet qua XLSX.utils.aoa_to_sheet),
// lấy độ dài chuỗi hiển thị DÀI NHẤT của cột đó để quyết định độ rộng — thay vì chỉ dựa theo độ
// dài tên cột (cách cũ) hoặc 1 con số cố định, để cột luôn đủ rộng hiển thị hết nội dung dài
// nhất, không bị cắt/phải tự kéo giãn tay. minWch/maxWch chỉ để tránh cột quá hẹp (dưới ngưỡng
// đọc được) hoặc quá rộng bất thường (dữ liệu lỗi/rất dài) — số +2 là khoảng đệm 2 bên cho dễ
// đọc, giống thao tác "AutoFit Column Width" trong Excel.
// ---------- Ghi file Excel kèm "Freeze Top Row" (cố định dòng tiêu đề khi cuộn) ----------
// LƯU Ý QUAN TRỌNG: thuộc tính ws['!freeze'] chỉ có tác dụng ở bản Pro của thư viện SheetJS —
// bản miễn phí "xlsx-js-style" (CDN đang dùng) KHÔNG ghi thuộc tính này ra file .xlsx thật, nên
// dù có set ws['!freeze'] thì mở bằng Excel/Google Sheets vẫn KHÔNG thấy dòng tiêu đề được cố
// định. Để đảm bảo tính năng "Freeze Top Row" hoạt động thật khi mở file, hàm này can thiệp
// TRỰC TIẾP vào XML gốc của từng sheet (sau khi XLSX.write tạo ra file nhị phân), chèn đúng thẻ
// <sheetViews><sheetView><pane .../></sheetView></sheetViews> theo chuẩn OOXML — cách duy nhất
// đảm bảo Excel thật sự hiểu và bật "Freeze Top Row" cho MỌI sheet trong file.
function injectFreezeTopRowIntoSheetXml(xml, freezeRows){
  // freezeRows: số dòng cần cố định từ đầu sheet (mặc định 1 = chỉ dòng tiêu đề, như trước đây).
  // Sheet nào có thêm dòng Tổng-SUBTOTAL nằm trên header (xem styleExportedSheet) sẽ truyền 2,
  // để cả dòng Tổng lẫn dòng tiêu đề đều cố định khi cuộn xuống, giống cách hiển thị trên bảng.
  freezeRows = freezeRows || 1;
  const topLeftCell = `A${freezeRows + 1}`;
  const paneXml = `<pane ySplit="${freezeRows}" topLeftCell="${topLeftCell}" activePane="bottomLeft" state="frozen"/><selection pane="bottomLeft" activeCell="${topLeftCell}" sqref="${topLeftCell}"/>`;
  if(/<sheetViews>[\s\S]*?<\/sheetViews>/.test(xml)){
    return xml.replace(/<sheetViews>([\s\S]*?)<\/sheetViews>/, (full, inner)=>{
      const selfClosing = inner.match(/<sheetView([^>]*?)\/>/);
      if(selfClosing) return `<sheetViews><sheetView${selfClosing[1]}>${paneXml}</sheetView></sheetViews>`;
      return `<sheetViews>${inner.replace(/(<sheetView[^>]*>)/, `$1${paneXml}`)}</sheetViews>`;
    });
  }
  const sheetViewsBlock = `<sheetViews><sheetView workbookViewId="0">${paneXml}</sheetView></sheetViews>`;
  if(/<dimension[^>]*\/>/.test(xml)) return xml.replace(/(<dimension[^>]*\/>)/, `$1${sheetViewsBlock}`);
  return xml.replace(/(<sheetData)/, `${sheetViewsBlock}$1`);
}
// Thay thế cho XLSX.writeFile(wb, filename) ở MỌI nơi xuất Excel trong file này: vẫn dùng
// XLSX.write() để lấy dữ liệu nhị phân workbook như bình thường (giữ nguyên style/màu/viền/độ
// rộng cột đã set), sau đó mở file .xlsx đó ra dưới dạng zip (bằng JSZip) để chèn thêm
// "Freeze Top Row" vào XML của TỪNG sheet, rồi mới đóng gói lại thành file .xlsx cuối cùng và
// tự động tải xuống — người dùng không thấy khác biệt gì ngoài việc mở file lên sẽ thấy dòng
// tiêu đề luôn cố định khi cuộn xuống.
async function writeWorkbookWithFreezeTopRow(wb, filename){
  const nh = () => (typeof window !== 'undefined' && window.klNhuong ? window.klNhuong() : Promise.resolve());
  const done = (typeof window !== 'undefined' && window.klBang) ? window.klBang('Đang tạo file Excel…') : () => {}; // PC 2.74: dựng file từng đoạn, nhường giao diện giữa các bước
  let outBlob;
  try{ await nh(); const zip = await buildFrozenWorkbookZip(wb); await nh(); outBlob = await zip.generateAsync({ type: 'blob' }); await nh(); }
  finally{ done(); }
  const url = URL.createObjectURL(outBlob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  setTimeout(()=> URL.revokeObjectURL(url), 4000);
}
// PC 4.52: chèn "in vừa 1 trang ngang" (sheetPr/pageSetUpPr + pageSetup) vào XML sheet ĐÚNG THỨ TỰ phần tử của chuẩn OOXML.
// LỖI TRƯỚC ĐÂY (PC 3.x–4.51): file do xlsx-js-style ghi KHÔNG có <pageMargins> → <pageSetup> bị chèn cuối file, SAU <ignoredErrors>
// → sai thứ tự (pageMargins, pageSetup, headerFooter … phải đứng TRƯỚC ignoredErrors) → Excel báo "We found a problem with some content…
// Do you want us to try to recover" khi mở phiếu xuất Tồn theo vị trí / Vị trí Bột. Nay: có <pageMargins> thì đặt pageSetup ngay sau; không có thì
// tự tạo <pageMargins> + <pageSetup> rồi chèn TRƯỚC phần tử đầu tiên đứng sau chúng (headerFooter, rowBreaks, ignoredErrors, drawing, tableParts, extLst…).
function injectFitToPageIntoSheetXml(xml){
  const pr = '<pageSetUpPr fitToPage="1"/>';
  if(/<sheetPr\b[^>]*\/>/.test(xml)) xml = xml.replace(/<sheetPr\b([^>]*)\/>/, `<sheetPr$1>${pr}</sheetPr>`);
  else if(/<sheetPr\b/.test(xml)) xml = /<pageSetUpPr\b/.test(xml) ? xml.replace(/<pageSetUpPr\b[^>]*\/>/, pr) : xml.replace('</sheetPr>', pr + '</sheetPr>');
  else xml = xml.replace(/(<worksheet\b[^>]*>)/, `$1<sheetPr>${pr}</sheetPr>`);
  const ps = '<pageSetup orientation="portrait" fitToWidth="1" fitToHeight="0"/>';
  xml = xml.replace(/<pageSetup\b[^>]*\/>/, '');
  if(/<pageMargins\b[^>]*\/>/.test(xml)) return xml.replace(/(<pageMargins\b[^>]*\/>)/, '$1' + ps);
  const pm = '<pageMargins left="0.7" right="0.7" top="0.75" bottom="0.75" header="0.3" footer="0.3"/>';
  const m = /<(headerFooter|rowBreaks|colBreaks|customProperties|cellWatches|ignoredErrors|smartTags|drawing|legacyDrawing|legacyDrawingHF|drawingHF|picture|oleObjects|controls|webPublishItems|tableParts|extLst)\b/.exec(xml);
  const i = m ? m.index : xml.lastIndexOf('</worksheet>');
  return xml.slice(0, i) + pm + ps + xml.slice(i);
}
// Dựng file .xlsx HOÀN CHỈNH (đã chèn cố định dòng/in vừa trang vào XML) dưới dạng JSZip — dùng
// chung cho tải file trên PC (writeWorkbookWithFreezeTopRow) và gửi file báo cáo lên điện thoại
// (buildPhoneReportSnapshot → generateAsync base64), để 2 nơi ra ĐÚNG 1 file giống hệt nhau.
async function buildFrozenWorkbookZip(wb){ if(typeof window !== 'undefined' && window.klNapXlsx) await window.klNapXlsx(); /* PC 1.85: thư viện Excel nạp lười */
  const nh = () => (typeof window !== 'undefined' && window.klNhuong ? window.klNhuong() : Promise.resolve());
  await nh();
  const wbout = XLSX.write(wb, { bookType: 'xlsx', type: 'array' });
  await nh();
  const zip = await JSZip.loadAsync(wbout);
  const sheetPaths = Object.keys(zip.files).filter(p => /^xl\/worksheets\/sheet\d+\.xml$/.test(p));
  for(const path of sheetPaths){
    // Khớp "sheetN.xml" với đúng worksheet đã append vào wb theo thứ tự (sheet1.xml = sheet đầu
    // tiên...) để lấy lại số dòng cần cố định (ws['!freezeRows'], do styleExportedSheet() set) —
    // mỗi sheet có thể cần cố định số dòng khác nhau (1 dòng tiêu đề, hoặc 2 nếu có thêm dòng
    // Tổng-SUBTOTAL nằm trên header).
    const m = path.match(/sheet(\d+)\.xml$/);
    const sheetIdx = m ? parseInt(m[1], 10) - 1 : 0;
    const sheetName = wb.SheetNames[sheetIdx];
    const sheetObj = sheetName ? wb.Sheets[sheetName] : null;
    const freezeRows = (sheetObj && sheetObj['!freezeRows']) || 1;
    let xml = await zip.file(path).async('string');
    xml = injectFreezeTopRowIntoSheetXml(xml, freezeRows);
    // ws['!fitToPage']: in dọc, co vừa 1 trang ngang (chiều dọc tự nhiều trang) — VD phiếu xuất Tồn theo vị trí / Vị trí Bột.
    if(sheetObj && sheetObj['!fitToPage']) xml = injectFitToPageIntoSheetXml(xml);
    zip.file(path, xml);
    await nh();
  }
  return zip;
}

function computeAutoColWidths(aoa, minWch, maxWch){
  minWch = minWch || 8;
  // Nâng trần rất cao (mặc định 120) — CHỈ để chặn trường hợp dữ liệu lỗi dài bất thường,
  // KHÔNG được cắt bớt cột "Tên hàng"/mô tả dài như thực tế (trước đây trần 60 làm cột bị cắt,
  // nhìn như chồng lên cột kế bên vì nội dung dài hơn độ rộng cột).
  maxWch = maxWch || 120;
  const numCols = aoa.reduce((max, row) => Math.max(max, row.length), 0);
  const widths = [];
  for(let c = 0; c < numCols; c++){
    let maxLen = 0;
    aoa.forEach(row=>{
      const val = row[c];
      let s;
      if(val === null || val === undefined || val === '') s = '';
      else if(typeof val === 'number') s = val.toLocaleString('en-US', { maximumFractionDigits: 2 });
      else s = String(val);
      if(s.length > maxLen) maxLen = s.length;
    });
    // Nhân thêm hệ số 1.15 vì đơn vị "wch" của Excel ước lượng theo bề rộng ký tự số (font
    // mặc định) — chữ HOA, chữ có dấu tiếng Việt (font Times New Roman đang dùng để xuất) thực
    // tế RỘNG HƠN 1 đơn vị "wch"/ký tự, nên tính đúng bằng số ký tự dễ bị hẹp hơn thực tế,
    // khiến nội dung bị cắt/nhìn như chồng lên cột kế bên dù đã "auto-fit" theo số ký tự.
    const estWidth = Math.ceil(maxLen * 1.15) + 3;
    widths.push({ wch: Math.max(minWch, Math.min(maxWch, estWidth)) });
  }
  return widths;
}

function styleExportedSheet(ws, displayCols, totalRowIdx, opts){
  // opts.headerRowIdx: dòng tiêu đề nằm ở đâu (mặc định 0 — ngay dòng đầu sheet, như trước đây).
  // opts.subtotalRowIdx: nếu có, đây là dòng Tổng-theo-SUBTOTAL đặt NGAY TRÊN dòng tiêu đề (xem
  // exportBaoCaoAllModulesToExcel) — được tô CÙNG kiểu với dòng tiêu đề để tạo cảm giác 1 khối.
  opts = opts || {};
  const headerRowIdx = opts.headerRowIdx || 0;
  const subtotalRowIdx = opts.subtotalRowIdx;
  // opts.plain (04/10/2026, phiếu xuất): in bằng máy in TRẮNG ĐEN → tiêu đề cột và dòng Tổng KHÔNG tô nền,
  // chữ đen, chỉ in đậm (dòng Tổng giữ viền trên đậm). Các báo cáo khác không truyền opts.plain nên giữ nguyên màu.
  const plain = !!opts.plain;
  // Định dạng số CỐ ĐỊNH 2 chữ số thập phân (VD 7.565,40 · 7.565,00) — áp dụng đồng nhất cho
  // MỌI bảng xuất Excel trong file này, thay vì "#,##0.###" (số thập phân biến thiên, có
  // dòng hiện 1 số lẻ, có dòng hiện đủ 3 số lẻ) như trước đây.
  const numFmt = '#,##0.00';
  for(let r = 0; r <= totalRowIdx; r++){
    for(let c = 0; c < displayCols.length; c++){
      const addr = XLSX.utils.encode_cell({ r, c });
      if(!ws[addr]) ws[addr] = { t: 's', v: '' };
      const isNumericColumn = isNumericCol(displayCols[c].label) || displayCols[c].virtual;

      if(subtotalRowIdx !== undefined && r === subtotalRowIdx){
        // Dòng Tổng-theo-SUBTOTAL (nằm trên header): tô cùng màu/chữ với dòng tiêu đề.
        ws[addr].s = {
          font: { name: 'Times New Roman', sz: 14, bold: true, color: { rgb: 'FFFFFF' } },
          fill: { patternType: 'solid', fgColor: { rgb: '1F4E78' } },
          alignment: { horizontal: isNumericColumn ? 'right' : 'center', vertical: 'center' },
          border: XLSX_THIN_BORDER,
        };
        if(isNumericColumn && typeof ws[addr].v === 'number') ws[addr].z = numFmt;
      } else if(r === headerRowIdx){
        // Dòng tiêu đề: nền xanh đậm, chữ trắng đậm, căn giữa. Riêng ô tiêu đề đang chứa SẴN 1
        // con số (VD cột "Tổng" của Bảng Tổng Hợp HLSO hiển thị số TỔNG TỒN ngay trên đầu cột
        // thay vì chữ) vẫn cần định dạng nghìn "#,##0.00" giống các ô số liệu bên dưới.
        ws[addr].s = {
          font: plain ? { name: 'Times New Roman', sz: 14, bold: true, color: { rgb: '000000' } }
                      : { name: 'Times New Roman', sz: 14, bold: true, color: { rgb: 'FFFFFF' } },
          alignment: { horizontal: 'center', vertical: 'center', wrapText: true },
          border: XLSX_THIN_BORDER,
        };
        if(!plain) ws[addr].s.fill = { patternType: 'solid', fgColor: { rgb: '1F4E78' } };
        if(typeof ws[addr].v === 'number') ws[addr].z = numFmt;
      } else if(r === totalRowIdx){
        // Dòng Tổng: nền xanh nhạt, chữ đậm, viền trên đậm hơn để tách khỏi dữ liệu.
        ws[addr].s = {
          font: { name: 'Times New Roman', sz: 14, bold: true },
          alignment: { horizontal: isNumericColumn ? 'right' : 'left', vertical: 'center' },
          border: { ...XLSX_THIN_BORDER, top: { style: 'medium', color: { rgb: '000000' } } },
        };
        if(!plain) ws[addr].s.fill = { patternType: 'solid', fgColor: { rgb: 'DDEBF7' } };
        if(isNumericColumn && typeof ws[addr].v === 'number') ws[addr].z = numFmt;
      } else {
        // Dòng dữ liệu: kẻ viền mảnh toàn bộ ô, cột số căn phải + định dạng nghìn.
        ws[addr].s = {
          font: { name: 'Times New Roman', sz: 14 },
          alignment: { horizontal: isNumericColumn ? 'right' : 'left', vertical: 'center' },
          border: XLSX_THIN_BORDER,
        };
        if(isNumericColumn && typeof ws[addr].v === 'number') ws[addr].z = numFmt;
      }
    }
  }
  // Bật bộ lọc tự động trên dòng tiêu đề (kể cả khi có dòng Tổng-SUBTOTAL nằm trên), và cố
  // định các dòng từ đầu sheet đến hết dòng tiêu đề khi cuộn (freezeRows — đọc lại ở
  // writeWorkbookWithFreezeTopRow() để chèn đúng vào XML).
  const lastCol = XLSX.utils.encode_col(displayCols.length - 1);
  const filterExcelRow = headerRowIdx + 1;
  ws['!autofilter'] = { ref: `A${filterExcelRow}:${lastCol}${filterExcelRow}` };
  ws['!freeze'] = { xSplit: 0, ySplit: filterExcelRow };
  ws['!freezeRows'] = filterExcelRow;
}

// Danh sách hàng của bảng "Báo Cáo Tổng Tồn": dòng ĐẦU TIÊN là hàng nhập tay
// BAOCAO_MANUAL_LABEL ("Bột + Sốt + Bếp"), sau đó là 7 module lọc theo đúng thứ tự trên
// thanh điều hướng. Dùng chung cho cả renderBaoCao() (vẽ giao diện) và buildTongTonSummarySheet()
// (xuất Excel) để số liệu luôn khớp nhau.
function getBaoCaoTongTonRows(){
  const key = normalizeLabelForMatch(BAOCAO_MANUAL_LABEL);
  const manualVal = state.hlsoManualValues[key];
  const manualRow = { label: BAOCAO_MANUAL_LABEL, weight: (manualVal===undefined?null:manualVal), manual: true };
  const moduleRows = MODULES
    .filter(m => FILTER_MODULES[m.id])
    .map(m => ({ label: m.label, weight: sumFilterModuleTotalWeight(m.id), manual: false }));
  // PC 2.11: phân loại KHÔNG CÒN TỒN (trọng lượng rỗng / bằng 0) thì ẨN — cả trên bảng lẫn file Excel (cùng dùng hàm này).
  return [manualRow, ...moduleRows].filter(r => hasStockWeight(r.weight));
}
// PC 2.11: "còn tồn" = có số và khác 0 (sai số làm tròn < 0.005 kg coi như hết tồn).
function hasStockWeight(v){ return v !== null && v !== undefined && !isNaN(v) && Math.abs(v) >= 0.005; }
// TỔNG TỒN KHO = cộng dồn trọng lượng của 7 module lọc VÀ dòng nhập tay "Bột + Sốt + Bếp"
// (tất cả các dòng đang hiển thị trên bảng), để tổng luôn khớp với các dòng đang hiển thị.
function getBaoCaoTongTonTotal(rows){
  return rows.some(r => r.weight !== null)
    ? rows.reduce((sum, r) => sum + (r.weight || 0), 0)
    : null;
}

// Dựng sheet "Tổng Tồn" — LẤY ĐÚNG dữ liệu đang hiển thị trên bảng "Báo Cáo Tổng Tồn" (dòng
// nhập tay "Bột + Sốt + Bếp" ở vị trí đầu + N module 1 dòng + dòng "TỔNG TỒN KHO" ở cuối),
// dùng chung logic getBaoCaoTongTonRows() với phần vẽ bảng (renderBaoCao) để số liệu luôn
// khớp với những gì người dùng đang xem trên màn hình. NGOÀI RA, gộp thêm bảng nhỏ riêng
// "Tồn Kho Gửi" (cách 1 dòng trống) — bảng đó KHÔNG cộng vào "TỔNG TỒN KHO" ở trên (tách
// biệt hoàn toàn về số liệu). CUỐI CÙNG SHEET, gộp thêm bảng "Báo Cáo Tổng Hợp" (cách 1 dòng
// trống) — lấy TOÀN BỘ dữ liệu THÔ của "Load Data" (state.rows), CHỈ với 10 cột riêng (xem
// getTongHopExportColumns()), giữ NGUYÊN từng dòng, KHÔNG gộp nhóm. TRÊN CÙNG SHEET là 1 dòng
// tiêu đề lớn (mẫu do người dùng cung cấp) + ngày xuất file — CHỈ có trên file Excel xuất ra,
// KHÔNG có trên giao diện ứng dụng — nên toàn bộ logic dựng bảng nằm gọn trong hàm này, tách
// biệt hoàn toàn với renderBaoCao() (không đổi các nhãn "Module"/"Kho Gửi"/"Báo Cáo Kho Gửi"
// đang hiển thị trên màn hình, và không đổi BAOCAO_KHOGUI_LABEL — vẫn dùng để tra state.hlsoManualValues).
const TONGTON_EXPORT_TITLE = 'BÁO CÁO TỒN KHO AN AN ĐẾN';
const TONGTON_EXPORT_MAIN_HEADER = 'Tồn Kho An An'; // thay cho "Module" — CHỈ áp dụng khi xuất Excel
const TONGTON_EXPORT_MAIN_TOTAL_LABEL = 'TỔNG TỒN AN AN'; // nhãn dòng tổng bảng "Tồn Kho An An" — CHỈ áp dụng khi xuất Excel
const TONGTON_EXPORT_KHOGUI_TITLE = 'Tồn Kho Gửi'; // thay cho "Báo Cáo Kho Gửi" — CHỈ áp dụng khi xuất Excel
const TONGTON_EXPORT_KHOGUI_ROW_LABEL = 'Tồn kho gởi kho tại Kho Transimex Long An'; // thay cho "Kho Gửi"
const TONGTON_EXPORT_GRAND_TOTAL_LABEL = 'TỔNG TỒN KHO AN AN + KHO GỬI'; // nhãn dòng tổng CỘNG DỒN cả 2 bảng — CHỈ áp dụng khi xuất Excel
// PC 4.25: opt.only (Set id module) = chỉ gồm các loại đó: bảng chính chỉ còn các dòng module đó (bỏ "Bột + Sốt + Bếp"), TỔNG TỒN AN AN tính lại theo các dòng còn lại,
// bảng "Báo Cáo Tổng Hợp" chỉ còn các dòng Load Data thuộc các module đó. Cấu trúc file (2 bảng đầu, Tồn Kho Gửi, tổng cộng dồn, bảng Tổng Hợp) giữ nguyên.
function buildTongTonSummarySheet(dateStr, opt){
  const onlyIds = opt && opt.only ? opt.only : null;
  const onlyLabels = onlyIds ? new Set(MODULES.filter(m => onlyIds.has(m.id)).map(m => m.label)) : null;
  const rows = getBaoCaoTongTonRows().filter(r => !onlyLabels || (!r.manual && onlyLabels.has(r.label)));
  const totalWeight = getBaoCaoTongTonTotal(rows);

  const aoa = [[TONGTON_EXPORT_TITLE, `Ngày : ${dateStr}`]];
  const titleRowIdx = 0;
  aoa.push([TONGTON_EXPORT_MAIN_HEADER, 'Tổng trọng lượng (kg)']);
  const headerRowIdx = aoa.length - 1;
  rows.forEach(r => aoa.push([r.label, r.weight === null ? '' : r.weight]));
  aoa.push([TONGTON_EXPORT_MAIN_TOTAL_LABEL, totalWeight === null ? '' : totalWeight]);
  const mainTotalRowIdx = aoa.length - 1;

  const khoGuiVal = state.hlsoManualValues[normalizeLabelForMatch(BAOCAO_KHOGUI_LABEL)];
  const khoGuiValNum = (khoGuiVal===undefined) ? null : khoGuiVal;
  aoa.push(['', '']);
  const blankRowIdx = aoa.length - 1;
  aoa.push([TONGTON_EXPORT_KHOGUI_TITLE, '']);
  const khoGuiHeaderRowIdx = aoa.length - 1;
  aoa.push([TONGTON_EXPORT_KHOGUI_ROW_LABEL, khoGuiValNum === null ? '' : khoGuiValNum]);
  aoa.push(['', '']);
  const khoGuiBlankRowIdx = aoa.length - 1;
  // Tổng CỘNG DỒN cả "TỔNG TỒN AN AN" (bảng trên) VÀ số kho gửi (bảng dưới) — coi phần thiếu
  // (chưa nhập/null) là 0 khi cộng, nhưng nếu CẢ HAI đều chưa có số liệu thì để trống (không
  // hiện "0.00" gây hiểu nhầm là đã có số liệu).
  const grandTotal = (totalWeight === null && khoGuiValNum === null)
    ? null
    : (totalWeight || 0) + (khoGuiValNum || 0);
  aoa.push([TONGTON_EXPORT_GRAND_TOTAL_LABEL, grandTotal === null ? '' : grandTotal]);
  const khoGuiTotalRowIdx = aoa.length - 1;
  // Từ đây trở lên là 2 bảng "cũ" (Tổng Tồn + Kho Gửi), luôn đúng 2 cột — style riêng bên dưới.
  const legacyRowCount = aoa.length;

  // ---------- Bảng "Báo Cáo Tổng Hợp" — lấy nguyên dữ liệu Load Data, 10 cột riêng ----------
  const tongHopCols = getTongHopExportColumns();
  let tongHopTitleRowIdx = -1, tongHopSubtotalRowIdx = -1, tongHopHeaderRowIdx = -1, tongHopTotalRowIdx = -1;
  const rowMapOnly = onlyLabels ? buildRowModuleNameMap() : null;
  const thEntries = state.rows.map((row, rowIdx) => ({ row, rowIdx })).filter(e => !onlyLabels || onlyLabels.has(rowMapOnly[e.rowIdx]));
  if(tongHopCols.length && thEntries.length){
    aoa.push([]); // dòng trống phân cách với bảng "Kho Gửi" phía trên — không tô/viền.
    const tongHopBlankRowIdx = aoa.length - 1;

    const titleRow = tongHopCols.map(() => '');
    titleRow[0] = 'Báo Cáo Tổng Hợp';
    aoa.push(titleRow);
    tongHopTitleRowIdx = aoa.length - 1;

    // Tính tổng 1 cột số trên TOÀN BỘ dòng — dùng CHUNG cho cả dòng Tổng-theo-SUBTOTAL (trên
    // header) VÀ dòng Tổng tĩnh (cuối bảng) bên dưới, để 2 dòng luôn khớp số với nhau lúc mới
    // mở file (trước khi người dùng bật bộ lọc).
    function sumTongHopCol(col){
      let sum = 0, has = false;
      thEntries.forEach(({row})=>{
        const v = parseLooseNumber(row[col.ci] ?? '');
        if(!isNaN(v)){ sum += v; has = true; }
      });
      return has ? sum : null;
    }

    // Dòng Tổng-theo-SUBTOTAL đặt NGAY TRÊN dòng tiêu đề (giống bảng gốc trên màn hình, và
    // giống các sheet module trong exportBaoCaoAllModulesToExcel) — mỗi ô số là CÔNG THỨC
    // =SUBTOTAL(109,...) tham chiếu đúng vùng dữ liệu bên dưới (không gồm dòng Tổng tĩnh ở
    // cuối), tự cập nhật lại khi người dùng bật/đổi bộ lọc cột (autofilter) ngay trên sheet này.
    const subtotalRow = tongHopCols.map(col=>{
      if(!col.numeric) return '';
      const s = sumTongHopCol(col);
      return s === null ? 0 : (col.integer ? Math.round(s) : s);
    });
    aoa.push(subtotalRow);
    tongHopSubtotalRowIdx = aoa.length - 1;

    aoa.push(tongHopCols.map(c => c.label));
    tongHopHeaderRowIdx = aoa.length - 1;

    // Toàn bộ dòng THÔ của Load Data, giữ NGUYÊN thứ tự, KHÔNG gộp nhóm/lọc gì cả.
    const rowModuleNameMap = buildRowModuleNameMap();
    thEntries.forEach(({row, rowIdx})=>{
      aoa.push(tongHopCols.map(col=>tongHopCellValue(row, col, rowIdx, rowModuleNameMap)));
    });

    // Dòng Tổng — cộng các cột số ("Tồn cuối (kiện)", "Tồn cuối (net)") trên TOÀN BỘ dòng
    // đã đưa vào bảng, giữ nguyên các cột còn lại (chữ) trống.
    let labelPlaced = false;
    const totalRow = tongHopCols.map(col=>{
      if(!col.numeric){
        if(!labelPlaced){ labelPlaced = true; return `Tổng (${thEntries.length.toLocaleString('en-US')} dòng)`; }
        return '';
      }
      const s = sumTongHopCol(col);
      return s === null ? '' : (col.integer ? Math.round(s) : s);
    });
    aoa.push(totalRow);
    tongHopTotalRowIdx = aoa.length - 1;

    void tongHopBlankRowIdx; // dòng trống — không cần style riêng, giữ tên biến để dễ đọc.
  }

  const ws = XLSX.utils.aoa_to_sheet(aoa);
  ws['!cols'] = computeAutoColWidths(aoa);

  // Định dạng số CỐ ĐỊNH 2 chữ số thập phân, đồng nhất với styleExportedSheet() ở trên.
  const numFmt = '#,##0.00';
  // ---- Style 2 bảng "cũ" (Tổng Tồn + Kho Gửi) — luôn đúng 2 cột (A,B) ----
  for(let r = 0; r < legacyRowCount; r++){
    if(r === blankRowIdx || r === khoGuiBlankRowIdx) continue; // các dòng trống phân cách — không tô/viền.
    for(let c = 0; c < 2; c++){
      const addr = XLSX.utils.encode_cell({ r, c });
      if(!ws[addr]) ws[addr] = { t: 's', v: '' };
      const isNumericColumn = c === 1;

      if(r === titleRowIdx){
        // Dòng tiêu đề lớn trên cùng ("BÁO CÁO TỒN KHO AN AN ĐẾN" / "Ngày : …") — chữ đậm,
        // căn giữa, KHÔNG tô nền (khác với dòng tiêu đề cột bên dưới) để nổi bật riêng.
        ws[addr].s = {
          font: { name: 'Times New Roman', sz: 14, bold: true },
          alignment: { horizontal: 'center', vertical: 'center', wrapText: true },
          border: XLSX_THIN_BORDER,
        };
      } else if(r === headerRowIdx || r === khoGuiHeaderRowIdx){
        // Dòng tiêu đề cột — dùng chung style cho CẢ tiêu đề bảng "Tổng Tồn" LẪN tiêu đề
        // bảng nhỏ "Tồn Kho Gửi" chèn thêm bên dưới.
        ws[addr].s = {
          font: { name: 'Times New Roman', sz: 14, bold: true, color: { rgb: 'FFFFFF' } },
          fill: { patternType: 'solid', fgColor: { rgb: '1F4E78' } },
          alignment: { horizontal: 'center', vertical: 'center', wrapText: true },
          border: XLSX_THIN_BORDER,
        };
      } else if(r === mainTotalRowIdx || r === khoGuiTotalRowIdx){
        ws[addr].s = {
          font: { name: 'Times New Roman', sz: 14, bold: true },
          fill: { patternType: 'solid', fgColor: { rgb: 'DDEBF7' } },
          alignment: { horizontal: isNumericColumn ? 'right' : 'left', vertical: 'center' },
          border: { ...XLSX_THIN_BORDER, top: { style: 'medium', color: { rgb: '000000' } } },
        };
        if(isNumericColumn && typeof ws[addr].v === 'number') ws[addr].z = numFmt;
      } else {
        ws[addr].s = {
          font: { name: 'Times New Roman', sz: 14 },
          alignment: { horizontal: isNumericColumn ? 'right' : 'left', vertical: 'center' },
          border: XLSX_THIN_BORDER,
        };
        if(isNumericColumn && typeof ws[addr].v === 'number') ws[addr].z = numFmt;
      }
    }
  }

  // ---- Style riêng bảng "Báo Cáo Tổng Hợp" — số cột thay đổi theo số cột khớp được trong
  // dữ liệu Load Data đã nạp (10 cột nếu file nạp có đủ, ít hơn nếu file thiếu vài cột).
  // Riêng cột "Tồn cuối (kiện)" (integer: true) dùng định dạng SỐ NGUYÊN "#,##0" — không hiện
  // phần thập phân — khác với các cột số khác (vẫn "#,##0.00" như numFmt chung). ----
  const numFmtInt = '#,##0';
  if(tongHopHeaderRowIdx !== -1){
    for(let r = tongHopTitleRowIdx; r <= tongHopTotalRowIdx; r++){
      for(let c = 0; c < tongHopCols.length; c++){
        const addr = XLSX.utils.encode_cell({ r, c });
        if(!ws[addr]) ws[addr] = { t: 's', v: '' };
        const isNumericColumn = tongHopCols[c].numeric;
        const cellNumFmt = tongHopCols[c].integer ? numFmtInt : numFmt;

        if(r === tongHopTitleRowIdx || r === tongHopHeaderRowIdx){
          ws[addr].s = {
            font: { name: 'Times New Roman', sz: 14, bold: true, color: { rgb: 'FFFFFF' } },
            fill: { patternType: 'solid', fgColor: { rgb: '1F4E78' } },
            alignment: { horizontal: 'center', vertical: 'center', wrapText: true },
            border: XLSX_THIN_BORDER,
          };
        } else if(r === tongHopSubtotalRowIdx){
          // Dòng Tổng-theo-SUBTOTAL (nằm trên header) — tô cùng màu/chữ với dòng tiêu đề.
          ws[addr].s = {
            font: { name: 'Times New Roman', sz: 14, bold: true, color: { rgb: 'FFFFFF' } },
            fill: { patternType: 'solid', fgColor: { rgb: '1F4E78' } },
            alignment: { horizontal: isNumericColumn ? 'right' : 'center', vertical: 'center' },
            border: XLSX_THIN_BORDER,
          };
          if(isNumericColumn && typeof ws[addr].v === 'number') ws[addr].z = cellNumFmt;
        } else if(r === tongHopTotalRowIdx){
          ws[addr].s = {
            font: { name: 'Times New Roman', sz: 14, bold: true },
            fill: { patternType: 'solid', fgColor: { rgb: 'DDEBF7' } },
            alignment: { horizontal: isNumericColumn ? 'right' : 'left', vertical: 'center' },
            border: { ...XLSX_THIN_BORDER, top: { style: 'medium', color: { rgb: '000000' } } },
          };
          if(isNumericColumn && typeof ws[addr].v === 'number') ws[addr].z = cellNumFmt;
        } else {
          ws[addr].s = {
            font: { name: 'Times New Roman', sz: 14 },
            alignment: { horizontal: isNumericColumn ? 'right' : 'left', vertical: 'center' },
            border: XLSX_THIN_BORDER,
          };
          if(isNumericColumn && typeof ws[addr].v === 'number') ws[addr].z = cellNumFmt;
        }
      }
    }

    // Ghi công thức SUBTOTAL(109,...) vào các ô số của dòng Tổng-trên-đầu bảng "Báo Cáo Tổng
    // Hợp". Vùng tham chiếu CHỈ gồm các dòng dữ liệu thật (không gồm dòng "Tổng (n dòng)" tĩnh
    // ở cuối bảng, tránh cộng dồn 2 lần).
    if(tongHopSubtotalRowIdx !== -1 && thEntries.length > 0){
      const firstDataExcelRow = tongHopHeaderRowIdx + 2; // Excel 1-based, ngay sau dòng tiêu đề
      const lastDataExcelRow = firstDataExcelRow + thEntries.length - 1;
      tongHopCols.forEach((col, idx)=>{
        if(!col.numeric) return;
        const colLetter = XLSX.utils.encode_col(idx);
        const addr = XLSX.utils.encode_cell({ r: tongHopSubtotalRowIdx, c: idx });
        if(ws[addr]) ws[addr].f = `SUBTOTAL(109,${colLetter}${firstDataExcelRow}:${colLetter}${lastDataExcelRow})`;
      });
    }
  }

  // Bật AutoFilter (icon phễu lọc) trên DÒNG TIÊU ĐỀ của bảng "Báo Cáo Tổng Hợp" — bảng có
  // các cột Tên hàng/Mã hàng/Loại hàng/Size/Khách hàng/Đặc tính/Hợp đồng/Tồn cuối (Kiện)/
  // Tồn cuối (net)/Bằng — vì đây mới là bảng người dùng cần lọc theo cột. Nếu chưa có dữ liệu
  // Load Data (bảng "Báo Cáo Tổng Hợp" không được chèn) thì giữ nguyên filter cũ trên bảng
  // "Tổng Tồn" (2 cột A:B) để không làm hỏng file khi rỗng.
  if(tongHopHeaderRowIdx !== -1){
    const tongHopLastCol = XLSX.utils.encode_col(tongHopCols.length - 1);
    ws['!autofilter'] = { ref: `A${tongHopHeaderRowIdx + 1}:${tongHopLastCol}${tongHopHeaderRowIdx + 1}` };
  } else {
    ws['!autofilter'] = { ref: 'A2:B2' };
  }
  ws['!freeze'] = { xSplit: 0, ySplit: 2 };
  return ws;
}

/* ================= PERSISTENCE ================= */
// LƯU Ý (2 đời bug đã sửa ở đây):
// 1) Bản đầu tiên dùng window.storage.get/set — API CHỈ tồn tại trong môi trường xem trước
//    Artifact của Claude.ai, KHÔNG tồn tại trong Electron thật — mọi lệnh gọi lỗi âm thầm nên
//    dữ liệu Load Data không bao giờ được ghi ra đĩa.
// 2) Bản vá lần 1 chuyển sang lưu qua window.dbAPI — ĐÚNG HƯỚNG nhưng lại nhồi TOÀN BỘ dữ liệu
//    (hàng nghìn dòng × hơn 20 cột) vào chung 1 chuỗi JSON RỒI GHI VÀO 1 Ô EXCEL DUY NHẤT. Excel
//    giới hạn CỨNG 32.767 ký tự/ô — với vài nghìn dòng dữ liệu thật, chuỗi JSON này vượt xa giới
//    hạn đó, bị cắt cụt khi ghi file, nên lần mở sau JSON.parse() sẽ LỖI (JSON không còn hợp lệ)
//    → rơi xuống nhánh dự phòng (rỗng) → bảng biến mất, y hệt lỗi người dùng gặp phải.
// SỬA LẦN NÀY: tách làm 2 phần, không còn ô nào phải chứa dữ liệu lớn:
//  - Sheet "LoadData": dữ liệu THẬT, mỗi DÒNG Excel = 1 dòng dữ liệu (cột đặt tên trung tính
//    c0, c1, c2… theo THỨ TỰ cột đã nạp — không dùng thẳng tên cột gốc để tránh xung đột khi
//    tên cột trống/trùng nhau, vốn rất hay gặp ở các file Excel tồn kho thực tế).
//  - Sheet "LoadDataMeta": 1 ô JSON nhỏ chứa danh sách tên cột (headers) + các thông tin phụ
//    (tên file, thời điểm nạp, cột đang gán thủ công…) — luôn nhỏ, không phụ thuộc số dòng dữ
//    liệu nên không bao giờ chạm giới hạn 32.767 ký tự/ô dù file nguồn có bao nhiêu dòng.
// Dự phòng: localStorage của trình duyệt khi không có window.dbAPI hoặc ghi Excel lỗi.
function buildLoadDataMeta(){
  return {
    headers: state.headers, sheets: state.sheets, currentSheet: state.currentSheet,
    fileName: state.fileName, importedAt: state.importedAt, dataDate: state.dataDate || '', napId: state.napId || '', headerRowInfo: state.headerRowInfo,
    manualAssign: state.manualAssign,
    // KHÔNG còn gồm hlsoManualValues ở đây nữa — dữ liệu "Báo cáo ngoài sổ sách" giờ có sheet
    // Excel riêng "BaoCaoNgoaiSoSach", đọc/ghi qua persistNgoaiSoSach()/restoreNgoaiSoSach()
    // (xem ghi chú ở đó), để: (1) người dùng mở được Excel lên xem trực tiếp dạng bảng thay vì
    // giấu trong 1 ô JSON, và (2) không còn phụ thuộc/lẫn với vòng đời dữ liệu Load Data.
  };
}
function applyLoadDataMeta(d){
  state.headers = d.headers||[];
  state.sheets = d.sheets||[];
  state.currentSheet = d.currentSheet||null;
  state.fileName = d.fileName||'';
  state.importedAt = d.importedAt||null;
  state.dataDate = d.dataDate||'';
  state.napId = d.napId||'';   // PC 3.12: mã đợt nạp (khớp sheet NapLog)
  state.headerRowInfo = d.headerRowInfo||null;
  state.manualAssign = d.manualAssign||{};
  // Tương thích ngược: nếu file Excel này được ghi bởi bản CŨ (trước khi tách sheet riêng),
  // LoadDataMeta vẫn có thể còn chứa hlsoManualValues — đọc tạm vào state ở đây để
  // restoreNgoaiSoSach() (chạy sau, xem init()) có cái migrate sang sheet riêng; nếu
  // LoadDataMeta không còn trường này (file mới) thì giữ nguyên state hiện có, không ghi đè rỗng.
  if(d.hlsoManualValues) state.hlsoManualValues = d.hlsoManualValues;
}
// persist() GỘP LỆNH (rà soát 09/2026): mỗi lần lưu = ghi lại TOÀN BỘ sheet LoadData (hàng nghìn dòng) → cả file
// Excel Module 1 (~1–2 giây). Nhiều thao tác liên tiếp (chuyển dòng, trừ phiếu, sửa ô…) trước đây xếp hàng N lần ghi
// giống hệt nhau → ứng dụng ì dần. Nay: đang có 1 lần ghi chạy thì MỌI lệnh mới gộp thành ĐÚNG 1 lần ghi kế tiếp
// (chụp trạng thái mới nhất lúc bắt đầu ghi) — kết quả trả về ('excel' / 'localStorage' / 'error') là của lần ghi
// đã bao gồm thay đổi của người gọi.
let persistRun = null, persistNext = null;
function persist(){
  if(persistNext) return persistNext;
  if(persistRun){
    persistNext = persistRun.catch(()=>{}).then(()=>{ persistNext = null; return startPersist(); });
    return persistNext;
  }
  return startPersist();
}
function startPersist(){
  // PC 7.58: lưu xong Load Data (nạp file Excel mới…) → báo trang chính để "Lịch xuất nhập hàng" tự dò lại tồn (js/lich-xuat-cont.js › autoRescan)
  persistRun = persistNow().then((r)=>{ try{ if(window.parent && window.parent !== window) window.parent.postMessage({ type:'kla-loaddata-changed' }, '*'); }catch(e){} return r; }).finally(()=>{ persistRun = null; });
  return persistRun;
}
async function persistNow(){
  if(hasDbAPI()){
    try{
      const colCount = state.headers.length;
      // mỗi dòng dữ liệu -> 1 object {c0:.., c1:.., ...} -> writeSheet() ghi thành 1 DÒNG Excel
      // thật (không nhồi vào 1 ô), nên không có giới hạn số dòng/ký tự nào đáng lo ở đây.
      const dataRows = state.rows.map(r=>{
        const obj = {};
        for(let i=0;i<colCount;i++) obj['c'+i] = r[i] ?? '';
        return obj;
      });
      const metaJson = JSON.stringify(buildLoadDataMeta());
      // Ghi ĐÈ TOÀN BỘ cả 2 sheet — mỗi lần nạp file Excel mới, dữ liệu CŨ bị THAY THẾ HOÀN
      // TOÀN bằng dữ liệu vừa nạp (đúng ý muốn, không cộng dồn/giữ lại cả 2 lần nạp).
      // Có writeSheets: ghi CẢ 2 sheet trong 1 lần đọc/ghi file (nhanh gấp đôi, không bao giờ lệch Meta ↔ Data
      // nếu app bị tắt giữa chừng). Preload cũ chưa có thì ghi lần lượt như trước.
      // PC 1.67: lưu THEO DÒNG (js/kl-luu-dong.js) — chỉ gửi dòng đổi (VD phiếu xuất trừ 3 dòng → gửi 3 dòng), Meta ghi kèm cùng 1 giao dịch; không chắc chắn → tự lưu cả sheet như cũ.
      if(window.KLLuuDong){
        const r = await window.KLLuuDong.save('ton-kho-an-an', LOADDATA_DB_SHEET, dataRows, { [LOADDATA_META_SHEET]: [{ data: '__JSON__' + metaJson }] });
        if(r && r.ok) return 'excel';
        console.error('Lỗi lưu dữ liệu Load Data vào database Excel:', r && r.error);
      }else if(typeof window.dbAPI.writeSheets === 'function'){
        const r = await window.dbAPI.writeSheets('ton-kho-an-an', { [LOADDATA_META_SHEET]: [{ data: '__JSON__' + metaJson }], [LOADDATA_DB_SHEET]: dataRows });
        if(r && r.ok) return 'excel';
        console.error('Lỗi lưu dữ liệu Load Data vào database Excel:', r && r.error);
      }else{
      const rMeta = await window.dbAPI.writeSheet('ton-kho-an-an', LOADDATA_META_SHEET, [{ data: '__JSON__' + metaJson }]);
      const rData = await window.dbAPI.writeSheet('ton-kho-an-an', LOADDATA_DB_SHEET, dataRows);
      if(rMeta && rMeta.ok && rData && rData.ok) return 'excel'; // ghi thành công — xong, không cần localStorage nữa
      console.error('Lỗi lưu dữ liệu Load Data vào database Excel:', (rMeta && rMeta.error) || (rData && rData.error) || 'writeSheet trả về ok=false');
      }
    }catch(e){ console.error('Lỗi lưu dữ liệu Load Data vào database Excel:', e); }
  }
  // Không có window.dbAPI (VD mở file HTML trực tiếp bằng trình duyệt thường, không qua ứng
  // dụng Electron này) HOẶC ghi Excel lỗi — rơi xuống localStorage. Trả về 'excel'/'localStorage'
  // (hoặc 'error' nếu cả 2 đều lỗi) để nơi gọi (VD commitNgss ở module "Tổng Hợp") biết CHẮC
  // CHẮN dữ liệu đã thực sự ghi vào database Excel hay chỉ mới lưu tạm trong trình duyệt.
  try{
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ ...buildLoadDataMeta(), rows: state.rows }));
    return 'localStorage';
  }
  catch(e){ console.error('Lỗi lưu dữ liệu Load Data (dự phòng localStorage):', e); return 'error'; }
}
async function restore(){
  if(hasDbAPI()){
    try{
      const resMeta = await window.dbAPI.readSheet('ton-kho-an-an', LOADDATA_META_SHEET);
      if(resMeta && resMeta.ok && Array.isArray(resMeta.data) && resMeta.data.length){
        const cell = resMeta.data[0] && resMeta.data[0].data;
        if(typeof cell === 'string' && cell.startsWith('__JSON__')){
          applyLoadDataMeta(JSON.parse(cell.slice('__JSON__'.length)));
          const colCount = state.headers.length;
          const resData = await window.dbAPI.readSheet('ton-kho-an-an', LOADDATA_DB_SHEET);
          if(window.KLLuuDong) window.KLLuuDong.seed('ton-kho-an-an', LOADDATA_DB_SHEET, resData && resData.ok ? resData.data : null); // PC 1.67: bản nhớ = đúng dữ liệu vừa đọc (cùng thứ tự với CSDL)
          state.rows = (resData && resData.ok && Array.isArray(resData.data))
            ? resData.data.map(obj=>{
                const arr = [];
                for(let i=0;i<colCount;i++) arr.push(obj['c'+i] ?? '');
                return arr;
              })
            : [];
          return; // đọc database Excel thành công — xong, không cần fallback
        }
      }
      return; // đọc thành công nhưng chưa từng lưu dữ liệu nào — không cần rơi xuống localStorage cũ
    }catch(e){ /* lỗi đọc (VD file đang bị khoá) — thử phương án dự phòng bên dưới */ }
  }
  try{
    const raw = localStorage.getItem(STORAGE_KEY);
    if(raw){
      const d = JSON.parse(raw);
      applyLoadDataMeta(d);
      state.rows = d.rows||[];
      { const mi = (state.headers||[]).findIndex(h => headersFuzzyMatch(h, 'Mã hàng')); if(mi >= 0) state.rows.forEach(r => { if(r && typeof r[mi] === 'string') r[mi] = r[mi].replace(/^\s*TPC-/i, ''); }); } // PC 1.89
    }
  }catch(e){ /* chưa có dữ liệu lưu trước đó, hoặc dữ liệu lỗi — bỏ qua, dùng mặc định rỗng */ }
}

// ---------------------------------------------------------------------------
// LƯU RIÊNG cho module "Đặc Tính" (2 bảng "Đặc Tính" + "Gộp Đặc Tính"): persist()/restore()
// ở trên dựa vào window.storage.get/set, một API CHỈ tồn tại trong môi trường xem trước
// Artifact của Claude.ai; khi chạy trong Electron (ứng dụng thật) window.storage không tồn
// tại, mọi lệnh gọi đều lỗi âm thầm (bị try/catch nuốt mất) nên state.dactinhGroupAssign/
// dactinhKnownLabels KHÔNG BAO GIỜ thực sự được ghi ra đĩa dù logic gộp nhóm ở trên đã đúng.
//
// Nguồn lưu CHÍNH: sheet "DacTinh" trong database Excel cục bộ (database/database.xlsx) qua
// window.dbAPI.readSheet/writeSheet đã có sẵn từ main.js/preload.js — hoạt động HOÀN TOÀN
// OFFLINE (không cần Internet, không cần localStorage của trình duyệt), và cho phép mở trực
// tiếp database.xlsx bằng Excel để xem/sửa thủ công nếu cần. LƯU Ý: window.dbAPI chỉ tồn tại
// khi chạy đúng trong Electron VÀ main.js đã bật "nodeIntegrationInSubFrames: true" (bắt buộc
// vì module này được nhúng qua <iframe>, mặc định Electron không chạy preload.js trong iframe
// con) — nếu thiếu 1 trong 2 điều kiện đó, window.dbAPI sẽ là undefined.
//
// Nguồn lưu DỰ PHÒNG: localStorage của trình duyệt — CHỈ dùng khi window.dbAPI không tồn tại
// (VD lỡ mở trực tiếp file này bằng trình duyệt thường thay vì qua ứng dụng Electron), để
// tính năng gộp nhóm vẫn hoạt động thay vì mất trắng hoàn toàn trong tình huống đó.
const DACTINH_STORAGE_KEY = 'coldstock:dactinh_v1'; // chỉ dùng cho phương án dự phòng localStorage
const DACTINH_DB_SHEET = 'DacTinh';
const DACTINH_GOC_FLAG = 'coldstock:dactinh_goc_done';

function hasDbAPI(){
  return typeof window !== 'undefined' && !!window.dbAPI && typeof window.dbAPI.writeSheet === 'function';
}

// Gộp dactinhKnownLabels + dactinhGroupAssign thành 1 mảng object phẳng để ghi vào sheet Excel:
// mỗi dòng = 1 đặc tính, cột "Nhóm đã gộp" để trống nếu đặc tính đó chưa được gộp vào nhóm nào.
function buildDacTinhSheetRows(){
  return Object.keys(state.dactinhKnownLabels).map(key=>{
    const label = state.dactinhKnownLabels[key];
    const assign = state.dactinhGroupAssign[key];
    return { 'Đặc Tính': label, 'Nhóm đã gộp': assign ? assign.group : '' };
  });
}

// Ngược lại: từ mảng dòng đọc được trong sheet Excel, dựng lại 2 object state cần dùng.
function parseDacTinhSheetRows(dataRows){
  const known = {}, group = {};
  (dataRows||[]).forEach(row=>{
    const label = String(row['Đặc Tính'] ?? '').trim();
    if(!label) return;
    const key = normalizeLabelForMatch(label);
    known[key] = label;
    const g = String(row['Nhóm đã gộp'] ?? '').trim();
    if(g) group[key] = { group: g, label };
  });
  return { known, group };
}

async function persistDacTinh(){
  if(!m1CanWrite) return; // PC 7.54: người dùng cấp 1 / 2 không ghi sổ Đặc Tính (kể cả bản dự phòng localStorage)
  if(hasDbAPI()){
    try{
      const res = await window.dbAPI.writeSheet('ton-kho-an-an', DACTINH_DB_SHEET, buildDacTinhSheetRows());
      if(res && res.ok) return; // ghi database Excel thành công — xong, không cần fallback
      console.error('Lỗi lưu dữ liệu Đặc Tính vào database Excel:', res && res.error);
    }catch(e){ console.error('Lỗi lưu dữ liệu Đặc Tính vào database Excel:', e); }
  }
  // Dự phòng: không có dbAPI, hoặc ghi Excel thất bại — vẫn lưu tạm vào localStorage để không
  // mất trắng dữ liệu vừa gộp trong phiên làm việc hiện tại.
  try{
    localStorage.setItem(DACTINH_STORAGE_KEY, JSON.stringify({
      dactinhGroupAssign: state.dactinhGroupAssign,
      dactinhKnownLabels: state.dactinhKnownLabels,
    }));
  }catch(e){ console.error('Lỗi lưu dữ liệu Đặc Tính (dự phòng localStorage):', e); }
}

async function restoreDacTinh(){
  if(hasDbAPI()){
    try{
      const res = await window.dbAPI.readSheet('ton-kho-an-an', DACTINH_DB_SHEET);
      // LƯU Ý: KHÔNG được kiểm tra thêm "res.data.length" ở đây — đọc database Excel THÀNH
      // CÔNG nhưng TRẢ VỀ MẢNG RỖNG là kết quả HOÀN TOÀN HỢP LỆ (đúng là trạng thái ngay sau
      // khi bấm "Xoá dữ liệu đã lưu" — sheet "DacTinh" đã được ghi rỗng thật sự). Trước đây có
      // thêm điều kiện "&& res.data.length" khiến trường hợp "đọc thành công nhưng rỗng" bị
      // coi nhầm như "đọc thất bại", RƠI XUỐNG đọc localStorage dự phòng — nơi vẫn còn giữ dữ
      // liệu CŨ từ trước khi xoá — khiến đặc tính đã xoá "sống lại" mỗi khi mở lại ứng dụng dù
      // file Excel đã được ghi đúng. Chỉ cần res.ok + res.data là mảng hợp lệ là đủ để tin
      // tưởng tuyệt đối vào kết quả đọc từ database Excel, dù rỗng hay không.
      if(res && res.ok && Array.isArray(res.data)){
        const { known, group } = parseDacTinhSheetRows(res.data);
        state.dactinhKnownLabels = known;
        state.dactinhGroupAssign = group;
        // 1.14: database CHƯA có đặc tính nào (máy mới) → nạp "Đặc tính gốc" (js/dactinh-goc.js) đúng 1 lần, rồi ghi vào database.
        // Cờ localStorage giữ để sau này người dùng chủ động "Xoá dữ liệu đã lưu" thì KHÔNG tự nạp lại.
        if(!res.data.length && Array.isArray(window.KL_DACTINH_GOC) && window.KL_DACTINH_GOC.length){
          let done = false; try{ done = localStorage.getItem(DACTINH_GOC_FLAG) === '1'; }catch(e){}
          if(!done){
            window.KL_DACTINH_GOC.forEach(function(p){
              const label = String(p[0] || '').trim(), g = String(p[1] || '').trim(); if(!label) return;
              const key = normalizeLabelForMatch(label);
              state.dactinhKnownLabels[key] = label;
              if(g) state.dactinhGroupAssign[key] = { group: g, label };
            });
            try{ localStorage.setItem(DACTINH_GOC_FLAG, '1'); }catch(e){}
            persistDacTinh();
          }
        }
        return; // đọc database Excel thành công (kể cả khi rỗng) — xong, không cần fallback
      }
    }catch(e){ /* lỗi đọc (VD file dang bi khoa) — thử phương án dự phòng bên dưới */ }
  }
  // Dự phòng: không có dbAPI, hoặc lỗi đọc database Excel — thử đọc localStorage (dữ
  // liệu từ các phiên chạy trước khi chưa có database Excel, hoặc lỡ mở bằng trình duyệt thường).
  try{
    const raw = localStorage.getItem(DACTINH_STORAGE_KEY);
    if(raw){
      const d = JSON.parse(raw);
      if(d.dactinhGroupAssign) state.dactinhGroupAssign = d.dactinhGroupAssign;
      if(d.dactinhKnownLabels) state.dactinhKnownLabels = d.dactinhKnownLabels;
    }
  }catch(e){ /* chưa có dữ liệu lưu trước đó, hoặc dữ liệu lỗi — bỏ qua, dùng mặc định rỗng */ }
}

// ---------------------------------------------------------------------------
// LƯU RIÊNG cho module "Tổng Hợp" > "Xuất Hàng" (danh sách phiếu xuất) — cùng cơ chế với
// persistDacTinh/restoreDacTinh ở trên: nguồn lưu CHÍNH là 1 sheet riêng trong database Excel
// cục bộ qua window.dbAPI (offline hoàn toàn), dự phòng localStorage khi không chạy trong
// Electron. Toàn bộ state ghi gọn thành 1 chuỗi JSON duy nhất (giống LoadDataMeta) vì đây là
// danh sách phiếu, không phải dữ liệu dạng bảng lớn.
const XUATHANG_STORAGE_KEY = 'coldstock:xuathang_v1'; // chỉ dùng cho phương án dự phòng localStorage
const XUATHANG_DB_SHEET = 'PhieuXuat';

function buildXuatHangPayload(){
  return { slips: state.xuatSlips, seq: state.xuatSlipSeq };
}
function applyXuatHangPayload(d){
  state.xuatSlips = (d && Array.isArray(d.slips)) ? d.slips : [];
  state.xuatSlipSeq = (d && Number.isFinite(d.seq)) ? d.seq : 1;
}

async function persistXuatHang(){
  const payload = buildXuatHangPayload();
  if(hasDbAPI()){
    try{
      const res = await window.dbAPI.writeSheet('ton-kho-an-an', XUATHANG_DB_SHEET, [{ data: '__JSON__' + JSON.stringify(payload) }]);
      if(res && res.ok) return true; // ghi database Excel thành công — xong, không cần fallback
      console.error('Lỗi lưu phiếu xuất (Xuất Hàng) vào database Excel:', res && res.error);
    }catch(e){ console.error('Lỗi lưu phiếu xuất (Xuất Hàng) vào database Excel:', e); }
  }
  try{ localStorage.setItem(XUATHANG_STORAGE_KEY, JSON.stringify(payload)); }
  catch(e){ console.error('Lỗi lưu phiếu xuất (Xuất Hàng) (dự phòng localStorage):', e); }
  return false;
}

async function restoreXuatHang(){
  if(hasDbAPI()){
    try{
      const res = await window.dbAPI.readSheet('ton-kho-an-an', XUATHANG_DB_SHEET);
      if(res && res.ok && Array.isArray(res.data) && res.data.length){
        const cell = res.data[0] && res.data[0].data;
        if(typeof cell === 'string' && cell.startsWith('__JSON__')){
          applyXuatHangPayload(JSON.parse(cell.slice('__JSON__'.length)));
          return;
        }
      }
      if(res && res.ok) return; // đọc thành công nhưng chưa từng lưu phiếu nào — không cần fallback
    }catch(e){ /* lỗi đọc (VD file đang bị khoá) — thử phương án dự phòng bên dưới */ }
  }
  try{
    const raw = localStorage.getItem(XUATHANG_STORAGE_KEY);
    if(raw) applyXuatHangPayload(JSON.parse(raw));
  }catch(e){ /* chưa có dữ liệu lưu trước đó, hoặc dữ liệu lỗi — bỏ qua, dùng mặc định rỗng */ }
}

// ---------------------------------------------------------------------------
// LƯU RIÊNG cho "Tổng Hợp > Báo cáo ngoài sổ sách" (2 dòng nhập tay "Bột + Sốt + Bếp"/"Kho
// Gửi", state.hlsoManualValues) — TRƯỚC ĐÂY giá trị này bị gộp chung vào 1 ô JSON của sheet
// "LoadDataMeta" (buildLoadDataMeta()), cùng chỗ với dữ liệu Load Data (headers/tên file…):
// người dùng mở file Excel M01_TonKhoAnAn.xlsx lên KHÔNG THẤY được dữ liệu này ở đâu vì nó
// chỉ là 1 chuỗi JSON nằm trong 1 ô, không phải bảng đọc được — và trước bản sửa lỗi này còn
// bị clearData() (nút "Xóa dữ liệu" ở Load Data) xoá mất theo, dù về bản chất đây là số liệu
// nhập tay, độc lập hoàn toàn với file Excel nạp ở Load Data.
// SỬA: tách hẳn ra 1 sheet riêng "BaoCaoNgoaiSoSach", dạng bảng Nhãn/Giá trị đọc được bình
// thường bằng Excel — cùng cơ chế với persistDacTinh/restoreDacTinh (sheet Excel riêng qua
// window.dbAPI, dự phòng localStorage khi không chạy trong Electron).
const NGSS_STORAGE_KEY = 'coldstock:ngoaisosach_v1'; // chỉ dùng cho phương án dự phòng localStorage
const NGSS_DB_SHEET = 'BaoCaoNgoaiSoSach';

// Gộp state.hlsoManualValues thành 1 mảng object phẳng để ghi vào sheet Excel — CHỈ ghi 2
// dòng thuộc "Báo cáo ngoài sổ sách" (NGSS_TABLES), không đụng tới các khoá khác (nếu có) của
// hlsoManualValues.
function buildNgssSheetRows(){
  return NGSS_TABLES.map(t=>{
    const key = t.key();
    const val = state.hlsoManualValues[key];
    return { 'Nhãn': t.label, 'Giá trị (kg)': (val===null || val===undefined) ? '' : val };
  });
}
// Ngược lại: từ mảng dòng đọc được trong sheet Excel, dựng lại state.hlsoManualValues — CHỈ
// cập nhật các khoá đang có trong sheet, giữ nguyên các khoá khác (nếu có) đã có sẵn.
function parseNgssSheetRows(dataRows){
  const out = { ...state.hlsoManualValues };
  (dataRows||[]).forEach(row=>{
    const label = String(row['Nhãn'] ?? '').trim();
    if(!label) return;
    const key = normalizeLabelForMatch(label);
    const raw = row['Giá trị (kg)'];
    const num = (raw==='' || raw===undefined || raw===null) ? null : Number(raw);
    out[key] = (num!==null && !isNaN(num)) ? num : null;
  });
  return out;
}

async function persistNgoaiSoSach(){
  if(hasDbAPI()){
    try{
      const res = await window.dbAPI.writeSheet('ton-kho-an-an', NGSS_DB_SHEET, buildNgssSheetRows());
      if(res && res.ok){
        // 2 số này nằm trong file "Báo cáo tổng tồn (AA)" của điện thoại nhưng KHÔNG làm đổi tồn kho → trước đây
        // báo cáo điện thoại không được dựng lại (tải về vẫn là file cũ). Báo main.js dựng + đẩy lại riêng báo cáo.
        try{ if(window.phoneImportBridge && typeof window.phoneImportBridge.reportChanged === 'function') window.phoneImportBridge.reportChanged('ton-kho-an-an'); }catch(e){ window.klLog && klLog('ton-kho-an-an.js:3263', e); }
        return 'excel'; // ghi database Excel thành công — xong, không cần fallback
      }
      console.error('Lỗi lưu "Báo cáo ngoài sổ sách" vào database Excel:', res && res.error);
    }catch(e){ console.error('Lỗi lưu "Báo cáo ngoài sổ sách" vào database Excel:', e); }
  }
  // Dự phòng: không có dbAPI, hoặc ghi Excel thất bại — vẫn lưu tạm vào localStorage để không
  // mất trắng số liệu vừa nhập trong phiên làm việc hiện tại.
  try{
    localStorage.setItem(NGSS_STORAGE_KEY, JSON.stringify(state.hlsoManualValues));
    return 'localStorage';
  }catch(e){ console.error('Lỗi lưu "Báo cáo ngoài sổ sách" (dự phòng localStorage):', e); return 'error'; }
}

async function restoreNgoaiSoSach(){
  if(hasDbAPI()){
    try{
      const res = await window.dbAPI.readSheet('ton-kho-an-an', NGSS_DB_SHEET);
      if(res && res.ok && Array.isArray(res.data) && res.data.length){
        state.hlsoManualValues = parseNgssSheetRows(res.data);
        return; // đọc database Excel thành công (đã có dữ liệu) — xong, không cần fallback
      }
      if(res && res.ok){
        // Sheet riêng chưa có dữ liệu (VD: file được nâng cấp từ bản cũ, dữ liệu vẫn đang nằm
        // trong LoadDataMeta do restore() ở trên đã đọc được) — MIGRATE: nếu state đang có sẵn
        // giá trị (từ LoadDataMeta cũ), ghi ngay vào sheet riêng này để lần sau mở lên thấy
        // được, không chờ tới lần sửa số liệu tiếp theo mới migrate.
        const hasLegacyValue = NGSS_TABLES.some(t => {
          const v = state.hlsoManualValues[t.key()];
          return v !== null && v !== undefined;
        });
        if(hasLegacyValue) await persistNgoaiSoSach();
        return;
      }
    }catch(e){ /* lỗi đọc (VD file đang bị khoá) — thử phương án dự phòng bên dưới */ }
  }
  // Dự phòng: không có dbAPI, hoặc lỗi đọc database Excel — thử đọc localStorage (dữ liệu từ
  // các phiên chạy trước khi chưa có database Excel, hoặc lỡ mở bằng trình duyệt thường).
  try{
    const raw = localStorage.getItem(NGSS_STORAGE_KEY);
    if(raw) state.hlsoManualValues = { ...state.hlsoManualValues, ...JSON.parse(raw) };
  }catch(e){ /* chưa có dữ liệu lưu trước đó, hoặc dữ liệu lỗi — bỏ qua, dùng mặc định rỗng */ }
}

// Tìm chỉ số cột "Tồn cuối" thực tế trong dữ liệu Load Data đã nạp — so khớp CHÍNH XÁC (không
// dùng headersFuzzyMatch kiểu substring) để không bị lẫn với cột "Tồn cuối (NET)", giống cách
// getTongHopExportColumns() đã xử lý ở trên.
// Popup xác nhận dùng chung (Xác nhận / Hủy) — thay cho confirm() mặc định của trình duyệt,
// dùng cho các thao tác nhạy cảm ở "Xuất Hàng" (trừ tồn kho / hủy trừ tồn kho). onConfirm chỉ
// được gọi khi người dùng bấm "Xác nhận"; bấm "Hủy", bấm ra ngoài, hoặc bấm phím Esc đều coi
// như huỷ thao tác (không gọi onConfirm).
function showConfirmPopup(title, message, onConfirm){
  const old = document.getElementById('confirmOverlay');
  if(old) old.remove();
  const overlay = document.createElement('div');
  overlay.className = 'confirm-overlay';
  overlay.id = 'confirmOverlay';
  overlay.innerHTML = `
    <div class="confirm-box">
      <h4>${escapeHtml(title)}</h4>
      <p>${escapeHtml(message)}</p>
      <div class="confirm-actions">
        <button class="btn btn-ghost" id="confirmBtnCancel">Hủy</button>
        <button class="btn btn-primary" id="confirmBtnOk">Xác nhận</button>
      </div>
    </div>
  `;
  document.body.appendChild(overlay);
  const close = ()=>{ overlay.remove(); document.removeEventListener('keydown', onKey); };
  const onKey = e=>{ if(e.key==='Escape') close(); };
  document.addEventListener('keydown', onKey);
  overlay.addEventListener('click', e=>{ if(e.target === overlay) close(); });
  document.getElementById('confirmBtnCancel').addEventListener('click', close);
  document.getElementById('confirmBtnOk').addEventListener('click', ()=>{ close(); onConfirm(); });
}

function findTonCuoiColIndex(){
  return state.headers.findIndex(h => fuzzyNormalize(h) === fuzzyNormalize('Tồn cuối'));
}

// PC 3.23 — MÁY CON LAN KHI MÁY CHỦ TẮT: phiếu xuất tạo / trừ / hủy trừ vẫn nhập được. Số liệu trong bộ nhớ sửa như thường; thay vì ghi cả sheet (bị chặn),
// gửi LỆNH PHIẾU cho main (lan:phieu-offline): main áp lên bản sao + xếp sổ chờ; máy chủ bật lại sẽ chạy lại lệnh trên số liệu của nó (core/phieu-gop.js).
const m1Lan = { mode: '', offline: false };
(function(){
  try{
    if(!window.lanAPI) return;
    const up = i => { if(i){ m1Lan.mode = i.mode || ''; m1Lan.offline = !!i.offline; } };
    window.lanAPI.getInfo().then(r => up(r && r.info)).catch(()=>{});
    window.lanAPI.onStatus(up);
  }catch(e){ /* máy đơn / không có lanAPI */ }
})();
function m1Offline(){ return m1Lan.mode === 'client' && m1Lan.offline && !!(window.lanAPI && window.lanAPI.phieuOffline); }
async function m1QueuePhieu(op){
  try{
    const r = await window.lanAPI.phieuOffline(op);
    if(r && r.ok) return true;
    alert((r && r.error) || 'Không lưu được thao tác phiếu lúc máy chủ tắt.');
  }catch(e){ alert('Không lưu được thao tác phiếu lúc máy chủ tắt: ' + (e && e.message || e)); }
  return false;
}
function m1TempId(){ return 'PXT-' + Date.now().toString(36).toUpperCase() + Math.random().toString(36).slice(2, 5).toUpperCase(); }

// Tạo 1 phiếu xuất mới từ các dòng đang chọn (state.xuatHangSelected) của module nguồn đang mở
// (state.xuatHangSourceModule). CHỈ ghi nhận phiếu — CHƯA trừ tồn kho (chỉ trừ khi bấm nút
// "Trừ tồn kho" ở danh sách phiếu, xem applyXuatSlipDeduction()).
function createXuatSlip(){
  const modId = state.xuatHangSourceModule;
  const ids = Object.keys(state.xuatHangSelected);
  if(!modId || ids.length===0) return;
  const cols = getFilterModuleColumns(modId);
  const ciOf = label => (cols.find(c=>c.label===label)||{}).ci;
  const maHangCi = ciOf('Mã hàng'), tenHangCi = ciOf('Tên hàng'), sizeCi = ciOf('Size'), hopDongCi = ciOf('Hợp đồng');
  const lines = ids.map(idStr=>{
    const i = Number(idStr);
    const row = state.rows[i] || [];
    const qty = Number(state.xuatHangSelected[idStr]) || 0;
    return {
      i,
      maHang: maHangCi!==undefined ? String(row[maHangCi] ?? '') : '',
      tenHang: tenHangCi!==undefined ? String(row[tenHangCi] ?? '') : '',
      size: sizeCi!==undefined ? String(row[sizeCi] ?? '') : '',
      hopDong: hopDongCi!==undefined ? String(row[hopDongCi] ?? '') : '',
      qty,
    };
  }).filter(l=>l.qty>0);
  if(lines.length===0) return;
  const mod = MODULES.find(m=>m.id===modId);
  if(m1Offline()){ // PC 3.23: máy chủ tắt → mã TẠM (PXT-…), máy chủ cấp mã PX-nnnn khi gộp
    const slip = { id: m1TempId(), moduleId: modId, moduleLabel: mod ? mod.label : modId, createdAt: new Date().toISOString(), lines, deducted: false, deductedAt: null, tam: true };
    m1QueuePhieu({ act: 'create', slip: { localId: slip.id, moduleId: slip.moduleId, moduleLabel: slip.moduleLabel, createdAt: slip.createdAt, lines: lines.map(l => ({ i: l.i, maHang: l.maHang, tenHang: l.tenHang, size: l.size, hopDong: l.hopDong, qty: l.qty })) } }).then(ok => {
      if(!ok){ state.xuatSlips = state.xuatSlips.filter(x => x !== slip); if(typeof renderXuatHangSlipList === 'function') renderXuatHangSlipList(); }
    });
    state.xuatSlips.unshift(slip);
    state.xuatHangSelected = {};
    return;
  }
  state.xuatSlips.unshift({
    id: 'PX-' + String(state.xuatSlipSeq++).padStart(4,'0'),
    moduleId: modId,
    moduleLabel: mod ? mod.label : modId,
    createdAt: new Date().toISOString(),
    lines,
    deducted: false,
    deductedAt: null,
  });
  state.xuatHangSelected = {};
  persistXuatHang();
}

// Trừ tồn kho THẬT cho 1 phiếu xuất: trừ trực tiếp vào cột "Tồn cuối" của đúng dòng dữ liệu
// Load Data (state.rows[i]) — vì mọi module lọc (Thành Phẩm Chính, HLSO, các module Tôm…) đều
// chỉ là 1 CÁCH LỌC trên CÙNG 1 nguồn dữ liệu này, nên trừ ở đây sẽ phản ánh ngay lập tức ở mọi
// nơi khác đang hiển thị dòng đó (bảng module gốc, Báo Cáo Tổng Tồn…). Nếu dòng đã bị xoá/đổi
// (VD nạp lại Load Data bằng file khác) thì bỏ qua dòng đó, không báo lỗi cả phiếu.
function applyXuatSlipDeduction(slip){
  const ci = findTonCuoiColIndex();
  if(ci === -1){ alert('Không tìm thấy cột "Tồn cuối" trong dữ liệu Load Data — không thể trừ tồn kho.'); return false; }
  slip.lines.forEach(line=>{
    const row = state.rows[line.i];
    if(!row) return;
    const cur = parseLooseNumber(row[ci]);
    row[ci] = (isNaN(cur) ? 0 : cur) - line.qty;
  });
  slip.deducted = true;
  slip.deductedAt = new Date().toISOString();
  if(m1Offline()){ m1QueuePhieu({ act: 'deduct', ref: slip.id }); return true; } // PC 3.23: máy chủ tắt → lệnh phiếu, không ghi cả sheet
  persist();
  persistXuatHang();
  return true;
}

// Hủy trừ tồn kho: CỘNG LẠI đúng số lượng đã trừ ở trên cho từng dòng, đưa phiếu về trạng thái
// "Chưa trừ tồn" — cho phép sửa lại rồi trừ lại nếu cần.
function undoXuatSlipDeduction(slip){
  const ci = findTonCuoiColIndex();
  if(ci === -1){ alert('Không tìm thấy cột "Tồn cuối" trong dữ liệu Load Data — không thể hủy trừ tồn kho.'); return false; }
  slip.lines.forEach(line=>{
    const row = state.rows[line.i];
    if(!row) return;
    const cur = parseLooseNumber(row[ci]);
    row[ci] = (isNaN(cur) ? 0 : cur) + line.qty;
  });
  slip.deducted = false;
  slip.deductedAt = null;
  if(m1Offline()){ m1QueuePhieu({ act: 'undo', ref: slip.id }); return true; } // PC 3.23
  persist();
  persistXuatHang();
  return true;
}

async function clearData(){
  state.headers=[]; state.rows=[]; state.sheets=[]; state.currentSheet=null;
  state.fileName=''; state.importedAt=null; state.dataDate=''; state.workbook=null; state.search=''; state.page=1;
  state.selected = new Set(); state.sortCol = null; state.sortDir = 'asc';
  state.colWidths = {};
  state.manualAssign = {};
  Object.keys(FILTER_MODULES).forEach(id => { state.filterModules[id] = makeFilterModuleState(id); });
  // CỐ Ý KHÔNG xoá state.dactinhGroupAssign, state.dactinhKnownLabels VÀ state.hlsoManualValues
  // ở đây: đây đều là dữ liệu NHẬP TAY, độc lập với file Excel vừa nạp ở Load Data — dữ liệu
  // module "Đặc Tính" (danh sách đặc tính đã từng thấy + các đặc tính đã gộp vào nhóm) và dữ
  // liệu 2 bảng "Tổng Hợp > Báo cáo ngoài sổ sách" ("Bột + Sốt + Bếp"/"Kho Gửi", đọc/ghi qua
  // state.hlsoManualValues — xem renderBaoCaoNgoaiSoSach()) phải được GIỮ NGUYÊN dù dữ liệu
  // Load Data của các module khác bị xoá — nên KHÔNG dùng storage.delete() (sẽ xoá sạch toàn
  // bộ, kể cả các mục trên) mà dùng persist() để ghi lại storage với các mục đó vẫn còn
  // nguyên, chỉ các phần dữ liệu khác được làm rỗng.
  await persist();
  renderAll();
}

/* ================= FILE PARSING ================= */
// PC 3.12 — NẠP EXCEL AN TOÀN (Load Data): chọn file → ĐỌC & KIỂM TRA (chưa đổi gì) → hộp "Xem trước khi nạp" (hợp lệ / cần kiểm tra / lỗi, so với dữ liệu đang có, file đã nạp chưa)
// → chỉ khi người dùng bấm nạp mới chụp bản hoàn tác + thay dữ liệu (trước đây chọn file là THAY NGAY, không hỏi). Dữ liệu cũ hoàn tác được: nút "Lịch sử nạp" ở góc trái dưới.
let napTicket = null;
function napCurrent(){ // tóm tắt dữ liệu ĐANG CÓ (trước khi thay)
  if(!state.rows || !state.rows.length) return null;
  const iMa = state.headers.findIndex(h => fuzzyNormalize(h) === fuzzyNormalize('Mã hàng')), iNet = state.headers.findIndex(h => fuzzyNormalize(h) === fuzzyNormalize('Tồn cuối (NET)'));
  const items = new Set(); let kg = 0;
  state.rows.forEach(r => { if(iMa >= 0){ const m = String(r[iMa] ?? '').trim(); if(m) items.add(m); } if(iNet >= 0){ const n = window.NapExcelCore ? NapExcelCore.num(r[iNet]) : Number(r[iNet]); if(n !== null && !isNaN(n)) kg += n; } });
  return { rows: state.rows.length, items: items.size, kg, date: (window.NapExcelCore && state.dataDate) ? String(state.dataDate) : '', fileName: state.fileName };
}
function napInfoNow(sheet){ const c = napCurrent() || { rows: 0, items: 0, kg: 0 }; return { rows: c.rows, items: c.items, kg: c.kg, sheet }; }
function napAfterSheet(name, pr){ // sau khi dữ liệu đã GHI xong (kể cả khi người dùng đổi sang sheet khác của cùng file)
  if(!window.NapExcel || !napTicket) return; const t = napTicket;
  Promise.resolve(pr).then(()=>{ const info = napInfoNow(name); if(!t.committed){ t.committed = true; return NapExcel.commit(t, info); } return NapExcel.update(t, { sheet: name, rows: info.rows, items: info.items, kg: Math.round(info.kg * 100) / 100 }); }).catch(()=>{});
}
async function handleFile(file){ if(typeof window !== 'undefined' && window.klNapXlsx) await window.klNapXlsx(); /* PC 1.85: thư viện Excel nạp lười */
  if(!file) return;
  try{
    const buf = await file.arrayBuffer();
    const wb = (window.XLSX_DOC || XLSX).read(buf, {type:'array'});
    const dd = guessDataDateFromFileName(file.name); // đoán từ tên file — sửa được ở Load Data
    let ticket = null;
    if(window.NapExcel && window.NapExcelCore){
      const P = prepareSheet(wb, wb.SheetNames[0]);
      const chk = NapExcelCore.checkM1(P.headers, P.rows, { requiredColumns: REQUIRED_COLUMNS, headerScore: P.score, headerRowNumber: P.headerRowIdx + 1, dropped: P.dropped, rowNums: P.rowNums });
      if(wb.SheetNames.length > 1) chk.infos.push('File có ' + wb.SheetNames.length + ' sheet — đang nạp sheet đầu tiên “' + wb.SheetNames[0] + '”; bấm tên sheet khác ở Load Data sẽ THAY dữ liệu ngay.');
      chk.infos.push('Số thứ tự dòng bên dưới tính từ dòng dữ liệu đầu tiên sau dòng tiêu đề.');
      if(!dd) chk.warns.push('Chưa đoán được ngày số liệu từ tên file — nhớ nhập ở ô “Ngày số liệu” sau khi nạp.');
      ticket = await NapExcel.review({ module: 'ton-kho-an-an', title: 'Tồn kho An An', file: { name: file.name, size: file.size }, buf, sheetName: wb.SheetNames[0], sheetCount: wb.SheetNames.length, dataDate: dd, check: chk, current: napCurrent(), sheets: [LOADDATA_DB_SHEET, LOADDATA_META_SHEET], confirmText: 'Nạp dữ liệu' });
      if(!ticket) return; // hủy / lỗi nặng → KHÔNG đổi gì
    }
    try{ await m1SaveSnap(); }catch(e){ if(window.klLog) klLog('so-sanh-nap', e); } // PC 3.04: giữ ảnh chụp lần nạp TRƯỚC để so sánh
    napTicket = ticket;
    state.workbook = wb;
    state.sheets = wb.SheetNames;
    state.fileName = file.name;
    state.importedAt = Date.now();
    state.dataDate = dd;
    state.napId = ticket && ticket.id ? ticket.id : '';
    await loadSheet(wb.SheetNames[0]);
  }catch(err){
    alert('Không đọc được file. Vui lòng kiểm tra định dạng Excel (.xlsx, .xls, .csv).\n\n'+err.message);
  }
}

/* ===== PC 3.04 — SO SÁNH 2 LẦN NẠP (lõi thuần: js/so-sanh-nap.js). Ảnh chụp lần nạp trước lưu ở sheet 'LanNapTruoc' (chỉ ghi khi nạp file MỚI). ===== */
const M1_SNAP_SHEET = 'LanNapTruoc';
function m1SnapNow(){
  const H = state.headers, ci = l => H.findIndex(h => headersFuzzyMatch(h, l));
  const iMa = ci('Mã hàng'), iTen = ci('Tên hàng'), iDt = ci('Đặc tính'), iSize = ci('Size'), b = getWeightBasisAllRows();
  if(iMa === -1 || !state.rows.length) return [];
  const iTon = b ? b.qtyCi : -1;
  return state.rows.map(r => {
    const ton = iTon === -1 ? NaN : parseLooseNumber(r[iTon] ?? ''), w = b ? parseLooseNumber(r[b.weightCi] ?? '') : NaN;
    const dtRaw = iDt === -1 ? '' : String(r[iDt] ?? '').trim();
    return { ma: String(r[iMa] ?? '').trim(), ten: iTen === -1 ? '' : String(r[iTen] ?? '').trim(), dt: (dtRaw && getDacTinhAssignedGroup(dtRaw)) || dtRaw,
             size: iSize === -1 ? '' : String(r[iSize] ?? '').trim(), ton: isNaN(ton) ? 0 : ton, kg: (isNaN(ton) || isNaN(w)) ? 0 : ton * w };
  });
}
async function m1SaveSnap(){
  if(!state.rows.length || !hasDbAPI() || !window.SoSanhNap) return;
  const snap = SoSanhNap.build(m1SnapNow());
  if(!snap.length) return;
  const meta = { fileName: state.fileName || '', dataDate: state.dataDate || '', importedAt: state.importedAt || 0, savedAt: Date.now() };
  await window.dbAPI.writeSheet('ton-kho-an-an', M1_SNAP_SHEET, [{ data: '__JSON__' + JSON.stringify({ meta, snap }) }]);
}
async function m1CmpDlg(){
  if(document.getElementById('m1CmpOv')) return;
  if(!window.SoSanhNap){ alert('Thiếu lõi so sánh.'); return; }
  let prev = null;
  try{ const res = await window.dbAPI.readSheet('ton-kho-an-an', M1_SNAP_SHEET); const d = res && res.data && res.data[0] && res.data[0].data; if(d) prev = JSON.parse(String(d).replace(/^__JSON__/, '')); }catch(e){ prev = null; }
  if(!prev || !prev.snap || !prev.snap.length){ alert('Chưa có lần nạp trước để so sánh.\nSau khi nạp file Tồn kho An An MỚI, app sẽ tự giữ lại bản trước đó cho lần so sánh.'); return; }
  const R = SoSanhNap.compare(prev.snap, SoSanhNap.build(m1SnapNow())), S = R.sum;
  const f = (n, d) => Number(n || 0).toLocaleString('en-US', { maximumFractionDigits: d == null ? 0 : d }), sg = (n, d) => (n > 0 ? '+' : '') + f(n, d);
  const ST = { moi: ['Mới xuất hiện', '#1a7f37'], het: ['Đã hết', '#b42318'], giam: ['Giảm', '#b54708'], tang: ['Tăng', '#175cd3'], doi0: ['Không đổi', '#667085'] };
  const grpTb = (arr, title) => `<h4 style="margin:12px 0 4px">${title}</h4><table style="width:100%;border-collapse:collapse;font-size:12px"><thead><tr><th style="text-align:left">${title.includes('Size') ? 'Size' : 'Nhóm'}</th><th>Số mã</th><th>Tồn trước</th><th>Tồn sau</th><th>Chênh</th><th>kg trước</th><th>kg sau</th><th>Chênh kg</th></tr></thead><tbody>${arr.map(g => `<tr><td>${escapeHtml(g.name)}</td><td>${f(g.n)}</td><td>${f(g.before, 1)}</td><td>${f(g.after, 1)}</td><td>${sg(g.diff, 1)}</td><td>${f(g.kgBefore)}</td><td>${f(g.kgAfter)}</td><td>${sg(g.kgDiff)}</td></tr>`).join('')}</tbody></table>`;
  const shown = R.rows.filter(r => r.status !== 'doi0').slice(0, 400);
  const ov = document.createElement('div'); ov.id = 'm1CmpOv'; ov.style.cssText = 'position:fixed;inset:0;z-index:99999;background:rgba(0,0,0,.45);display:flex;align-items:center;justify-content:center;';
  ov.innerHTML = `<div style="background:var(--surface,#fff);color:var(--text,#111);border-radius:10px;padding:16px;width:min(1040px,95vw);max-height:90vh;overflow:auto;box-shadow:0 10px 40px rgba(0,0,0,.4)">
    <h3 style="margin:0 0 4px">⇄ So sánh 2 lần nạp — Tồn kho An An</h3>
    <p style="margin:0 0 8px;opacity:.75;font-size:12px">Trước: ${escapeHtml((prev.meta && prev.meta.fileName) || '—')}${prev.meta && prev.meta.dataDate ? ' (' + escapeHtml(prev.meta.dataDate) + ')' : ''} → Sau: ${escapeHtml(state.fileName || '—')}${state.dataDate ? ' (' + escapeHtml(state.dataDate) + ')' : ''}</p>
    <div style="display:flex;gap:10px;flex-wrap:wrap;margin-bottom:6px">${['moi', 'het', 'giam', 'tang', 'doi0'].map(k => `<div style="border:1px solid #8884;border-radius:6px;padding:6px 10px"><b style="color:${ST[k][1]}">${f(S[k])}</b><div style="font-size:11px">${ST[k][0]}</div></div>`).join('')}
      <div style="border:1px solid #8884;border-radius:6px;padding:6px 10px"><b>${f(S.kgBefore)} → ${f(S.kgAfter)} kg</b><div style="font-size:11px">Tổng kg (${sg(S.kgDiff)})</div></div></div>
    ${grpTb(R.byDt, 'Theo nhóm đặc tính')}${grpTb(R.bySize, 'Theo Size')}
    <h4 style="margin:12px 0 4px">Theo mã hàng (${f(shown.length)}${R.rows.length - R.rows.filter(r => r.status === 'doi0').length > shown.length ? ' đầu tiên' : ''}, bỏ mã không đổi)</h4>
    <table style="width:100%;border-collapse:collapse;font-size:12px"><thead><tr><th style="text-align:left">Mã hàng</th><th style="text-align:left">Nhóm</th><th>Size</th><th>Tình trạng</th><th>Tồn trước</th><th>Tồn sau</th><th>Chênh</th><th>Chênh kg</th></tr></thead><tbody>${shown.map(r => `<tr><td title="${escapeHtml(r.ten)}">${escapeHtml(r.ma)}</td><td>${escapeHtml(r.dt)}</td><td>${escapeHtml(r.size)}</td><td style="color:${ST[r.status][1]}">${ST[r.status][0]}</td><td>${f(r.before, 1)}</td><td>${f(r.after, 1)}</td><td>${sg(r.diff, 1)}</td><td>${sg(r.kgDiff)}</td></tr>`).join('')}</tbody></table>
    <div style="display:flex;gap:8px;justify-content:flex-end;margin-top:12px"><button class="btn btn-ghost" id="m1CmpXls" type="button">Xuất Excel</button><button class="btn btn-primary" id="m1CmpX" type="button">Đóng</button></div></div>`;
  document.body.appendChild(ov);
  const close = () => { if(ov.parentNode) ov.parentNode.removeChild(ov); document.removeEventListener('keydown', esc, true); };
  const esc = e => { if(e.key === 'Escape'){ e.stopPropagation(); close(); } };
  document.addEventListener('keydown', esc, true);
  ov.querySelector('#m1CmpX').onclick = close; ov.addEventListener('mousedown', e => { if(e.target === ov) close(); });
  ov.querySelector('#m1CmpXls').onclick = async () => {
    try{
      if(window.klNapXlsx) await window.klNapXlsx();
      const wb = XLSX.utils.book_new();
      XLSX.utils.book_append_sheet(wb, XLSX.utils.aoa_to_sheet([['Mã hàng', 'Tên hàng', 'Nhóm đặc tính', 'Size', 'Tình trạng', 'Tồn trước', 'Tồn sau', 'Chênh', 'kg trước', 'kg sau', 'Chênh kg']].concat(R.rows.map(r => [r.ma, r.ten, r.dt, r.size, ST[r.status][0], r.before, r.after, r.diff, r.kgBefore, r.kgAfter, r.kgDiff]))), 'TheoMa');
      XLSX.utils.book_append_sheet(wb, XLSX.utils.aoa_to_sheet([['Nhóm đặc tính', 'Số mã', 'Tồn trước', 'Tồn sau', 'Chênh', 'kg trước', 'kg sau', 'Chênh kg']].concat(R.byDt.map(g => [g.name, g.n, g.before, g.after, g.diff, g.kgBefore, g.kgAfter, g.kgDiff]))), 'TheoNhom');
      XLSX.utils.book_append_sheet(wb, XLSX.utils.aoa_to_sheet([['Size', 'Số mã', 'Tồn trước', 'Tồn sau', 'Chênh', 'kg trước', 'kg sau', 'Chênh kg']].concat(R.bySize.map(g => [g.name, g.n, g.before, g.after, g.diff, g.kgBefore, g.kgAfter, g.kgDiff]))), 'TheoSize');
      await writeWorkbookWithFreezeTopRow(wb, 'so_sanh_2_lan_nap.xlsx');
    }catch(e){ alert('Không xuất được Excel: ' + (e && e.message || e)); }
  };
}

// Đọc 1 sheet → {headers, rows,…} KHÔNG đụng state (dùng chung cho xem trước và nạp thật)
function prepareSheet(wb, name){
  const ws = wb.Sheets[name];
  const raw = XLSX.utils.sheet_to_json(ws, {header:1, defval:'', raw:false});
  const filtered = raw.filter(r => r.some(c => String(c).trim() !== ''));

  const {idx: headerRowIdx, score} = detectHeaderRowIndex(filtered);
  const headers = (filtered[headerRowIdx]||[]).map(h => String(h).trim());
  const rowsRaw = filtered.slice(headerRowIdx+1);
  // PC 3.69: ô số có định dạng kiểu "#,###.##" (Excel) hiển thị số nguyên kèm dấu chấm cuối ("30.", "1,250.")
  // → đọc raw:false ra chuỗi có dấu "." thừa, làm cột Tồn cuối bị báo "không phải số". Bỏ dấu chấm thừa
  // (chỉ khi phần trước là số thuần; ô chỉ có "." vẫn giữ nguyên để nhận ra dòng hết tồn).
  rowsRaw.forEach(r => { for(let k = 0; k < r.length; k++){ const v = r[k]; if(typeof v === 'string' && /^\s*\(?-?[\d,]*\d\.\)?\s*$/.test(v)) r[k] = v.trim().replace(/\.(\)?)$/, '$1'); } });

  // ---- Bỏ những DÒNG có cột "Tồn cuối" không có giá trị (rỗng, hoặc chỉ có dấu "." — Excel
  // gốc hay hiển thị "." cho ô trống): đây là các sản phẩm đã HẾT TỒN, không cần hiển thị.
  // Chỉ khớp đúng cột "Tồn cuối" (không khớp nhầm sang "Tồn cuối (NET)" — 2 cột khác nhau)
  // bằng cách so sánh tên cột đã chuẩn hoá (bỏ dấu, bỏ khoảng trắng thừa) phải BẰNG HẲN nhau.
  const isBlankCell = v => {
    const s = String(v ?? '').trim();
    return s === '' || s === '.';
  };
  const tonCuoiColIdx = headers.findIndex(h => fuzzyNormalize(h) === fuzzyNormalize('Tồn cuối'));
  const rowNums = [];
  const rows = [];
  rowsRaw.forEach((r, i) => { if(tonCuoiColIdx === -1 || !isBlankCell(r[tonCuoiColIdx])){ rows.push(r); rowNums.push(i + 1); } });

  // ---- Lược bỏ tiền tố "TPC-" ở đầu cột "Mã hàng" (nếu có), ví dụ:
  // "TPC-VSHMVTNQS8R004L20BDA1" -> "VSHMVTNQS8R004L20BDA1"
  const maHangColIdxForStrip = headers.findIndex(h => headersFuzzyMatch(h, 'Mã hàng'));
  if(maHangColIdxForStrip !== -1){
    rows.forEach(r => {
      const v = String(r[maHangColIdxForStrip] ?? '');
      if(/^\s*TPC-/i.test(v)) r[maHangColIdxForStrip] = v.replace(/^\s*TPC-/i, '');   // PC 1.89: không phân biệt hoa/thường, bỏ cả khoảng trắng đầu
    });
  }
  return { headers, rows, headerRowIdx, score, filtered, dropped: rowsRaw.length - rows.length, rowNums };
}

function loadSheet(name){
  if(!state.workbook) return;
  const {headers, rows, headerRowIdx, score, filtered} = prepareSheet(state.workbook, name);

  state.currentSheet = name;
  state.headers = headers;
  state.rows = rows;
  state.search = '';
  state.page = 1;
  state.selected = new Set();
  state.sortCol = null;
  state.sortDir = 'asc';
  state.colWidths = {};
  state.manualAssign = {};
  Object.keys(FILTER_MODULES).forEach(id => { state.filterModules[id] = makeFilterModuleState(id); });
  state.headerRowInfo = {
    rowNumber: headerRowIdx+1,
    skippedRows: headerRowIdx,
    matchScore: score,
    rawPreview: filtered.slice(0, Math.min(6, filtered.length))
  };
  const pr = persist();
  renderAll();
  napAfterSheet(name, pr);
  return pr;
}

async function downloadTemplate(){ if(typeof window !== 'undefined' && window.klNapXlsx) await window.klNapXlsx(); /* PC 1.85: thư viện Excel nạp lười */
  const templateAoa = [REQUIRED_COLUMNS];
  const ws = XLSX.utils.aoa_to_sheet(templateAoa);
  ws['!cols'] = computeAutoColWidths(templateAoa);
  REQUIRED_COLUMNS.forEach((_, c)=>{
    const addr = XLSX.utils.encode_cell({ r: 0, c });
    if(!ws[addr]) ws[addr] = { t: 's', v: '' };
    ws[addr].s = {
      font: { name: 'Times New Roman', sz: 14, bold: true, color: { rgb: 'FFFFFF' } },
      fill: { patternType: 'solid', fgColor: { rgb: '1F4E78' } },
      alignment: { horizontal: 'center', vertical: 'center', wrapText: true },
      border: XLSX_THIN_BORDER,
    };
  });
  ws['!freeze'] = { xSplit: 0, ySplit: 1 };
  const wb = XLSX.utils.book_new();
  XLSX.utils.book_append_sheet(wb, ws, 'Ton_Kho');
  await writeWorkbookWithFreezeTopRow(wb, 'mau_du_lieu_ton_kho.xlsx');
}

/* ================= RENDER: TOPBAR ================= */
// Đo chiều cao thực tế của topbar để thanh menu con "Phân Loại" (.pl-subnav) dính đúng ngay
// dưới nó khi cuộn trang — topbar có thể đổi chiều cao tùy module (số ô thống kê khác nhau,
// có thể xuống dòng ở màn hình hẹp) nên không dùng 1 con số cố định.
function syncTopbarHeightVar(){
  const tb = document.querySelector('.topbar');
  if(!tb) return;
  document.documentElement.style.setProperty('--topbar-h', tb.offsetHeight + 'px');
}
window.addEventListener('resize', syncTopbarHeightVar);
// PC 3.82: topbar đổi chiều cao (số chip thay đổi / cửa sổ co giãn) → đo lại ngay để thanh menu con sticky không lệch
if(window.ResizeObserver){ const _tb=document.querySelector('.topbar'); if(_tb) new ResizeObserver(syncTopbarHeightVar).observe(_tb); }
function renderTopbar(){
  const mod = MODULES.find(m=>m.id===state.activeModule);
  const pageTitleEl = document.getElementById('pageTitle');
  pageTitleEl.textContent = mod.label;
  // Ẩn tiêu đề trang cho các module con nhóm "Phân Loại": thanh menu con ngang (.pl-subnav) ngay
  // trên bảng đã cho biết đang xem module con nào, nên tiêu đề lớn ở topbar bị trùng lặp — ẩn đi
  // cho gọn và nhường thêm chỗ cho bảng. Các trang khác (Load Data, Tổng Hợp…) vẫn giữ tiêu đề.
  pageTitleEl.style.display = (mod.group === 'phanloai') ? 'none' : '';
  const stats = document.getElementById('topbarStats');

  if(state.activeModule === 'loaddata' && state.rows.length){
    const {matched} = matchedSchema();
    const distinctItems = countDistinctColumnAllRows('Mã hàng');
    const totalFinal = sumColumnAllRows('Tồn cuối');
    const totalWeight = sumTotalWeightAllRows();
    document.getElementById('pageSub').textContent = `Đã nạp từ: ${state.fileName || 'dữ liệu đã lưu'}` + (loadDataDateLabel() ? ` · Số liệu ngày ${loadDataDateLabel()}` : '');
    stats.innerHTML = `
      <div class="stat-chip${state.dataDate ? '' : ' dd-chip-missing'}" title="Ngày số liệu (sửa ở ô Ngày số liệu bên dưới)"><div class="v">${state.dataDate ? escapeHtml(state.dataDate.slice(0,5)) : '—'}</div><div class="l">Ngày số liệu</div></div>
      <div class="stat-chip"><div class="v">${state.rows.length.toLocaleString('en-US')}</div><div class="l">Dòng</div></div>
      ${distinctItems!==null?`<div class="stat-chip"><div class="v">${distinctItems.toLocaleString('en-US')}</div><div class="l">Mã hàng</div></div>`:''}
      ${totalFinal!==null?`<div class="stat-chip"><div class="v">${totalFinal.toLocaleString('en-US')}</div><div class="l">Tổng tồn cuối</div></div>`:''}
      ${totalWeight!==null?`<div class="stat-chip"><div class="v">${totalWeight.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2})}</div><div class="l">Tổng trọng lượng (kg)</div></div>`:''}
      <div class="stat-chip"><div class="v">${state.headers.length}</div><div class="l">Cột</div></div>
      <div class="stat-chip"><div class="v">${matched.length}/${REQUIRED_COLUMNS.length}</div><div class="l">Khớp mẫu</div></div>
    `;
  } else if(state.activeModule === 'loaddata'){
    document.getElementById('pageSub').textContent = 'Nhập dữ liệu tồn kho từ file Excel';
    stats.innerHTML = '';
  } else if(state.activeModule === 'dkloc'){
    document.getElementById('pageSub').textContent = state.rows.length
      ? `Các bước lọc của từng module Phân Loại — nạp từ ${state.fileName || 'dữ liệu đã lưu'}` + (loadDataDateLabel() ? ` · Số liệu ngày ${loadDataDateLabel()}` : '')
      : 'Chưa có dữ liệu — vào mục Load Data để nạp file tồn kho';
    stats.innerHTML = state.rows.length ? `<div class="stat-chip"><div class="v">${state.rows.length.toLocaleString('en-US')}</div><div class="l">Dòng Load Data</div></div>` : '';
  } else if(FILTER_MODULES[state.activeModule] && state.rows.length){
    const modId = state.activeModule;
    const fmCfg = FILTER_MODULES[modId];
    const baseCount = filterModuleBaseRowsCached(modId).length;
    const distinctItems = countDistinctFilterModuleColumn(modId, 'Mã hàng');
    const totalFinal = sumFilterModuleColumn(modId, 'Tồn cuối');
    const totalWeight = sumFilterModuleTotalWeight(modId);
    let subLabel;
    if(fmCfg.excludeAllOtherModules){
      subLabel = `Phần dữ liệu còn lại (không thuộc module nào khác) — nạp từ ${state.fileName || 'dữ liệu đã lưu'}`;
    } else if(fmCfg.primaryMatch){
      subLabel = `Lọc ${fmCfg.primaryMatchLabel} — nạp từ ${state.fileName || 'dữ liệu đã lưu'}`;
    } else {
      const ci = resolveFilterCol(modId);
      const val = ci!==null ? resolveFilterVal(modId, ci) : '';
      subLabel = val
        ? `Lọc ${state.headers[ci]||''} = "${val}" — nạp từ ${state.fileName || 'dữ liệu đã lưu'}`
        : `Nạp từ ${state.fileName || 'dữ liệu đã lưu'}`;
    }
    document.getElementById('pageSub').textContent = subLabel + (loadDataDateLabel() ? ` · Số liệu ngày ${loadDataDateLabel()}` : '');
    stats.innerHTML = ''; // PC 3.91: thẻ tổng chuyển xuống thanh công cụ của bảng (renderFilterModule › fm-sum)
  } else if(FILTER_MODULES[state.activeModule]){
    document.getElementById('pageSub').textContent = 'Chưa có dữ liệu — vào mục Load Data để nạp file tồn kho';
    stats.innerHTML = '';
  } else if(state.activeModule === 'baocao' && state.rows.length){
    document.getElementById('pageSub').textContent = `Tổng hợp trọng lượng theo từng module — nạp từ ${state.fileName || 'dữ liệu đã lưu'}` + (loadDataDateLabel() ? ` · Số liệu ngày ${loadDataDateLabel()}` : '');
    const totalWeight = sumBaoCaoModuleWeights();
    stats.innerHTML = totalWeight!==null
      ? `<div class="stat-chip"><div class="v">${totalWeight.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2})}</div><div class="l">Tổng trọng lượng (kg)</div></div>`
      : '';
  } else if(state.activeModule === 'baocao'){
    document.getElementById('pageSub').textContent = 'Chưa có dữ liệu — vào mục Load Data để nạp file tồn kho';
    stats.innerHTML = '';
  } else {
    document.getElementById('pageSub').textContent = '';
    stats.innerHTML = '';
  }
}

/* ================= RENDER: LOAD DATA ================= */
// Trạng thái "có dữ liệu hay không" ở LẦN VẼ TRƯỚC — dùng để phát hiện đúng lúc CHUYỂN ĐỔI giữa
// 2 trạng thái (dropzone to <-> dropzone gọn + bảng) mà chạy hiệu ứng mượt bên dưới. Các lần vẽ
// lại KHÁC (đổi trang, sắp xếp, lọc, đổi sheet...) vẫn giữ nguyên hasData như cũ nên không kích
// hoạt hiệu ứng gì thêm — chỉ đúng lúc "giật" thật sự (bố cục đổi hẳn) mới chạy animation.
let __ldPrevHasData = null;
// Tab đang mở ở menu "Load Data" (gộp gọn 3 bảng nạp file: chính / vị trí / vị trí Bột thành
// dạng tab thay vì xếp chồng dọc — xem buildLoadDataTabsHtml()).
let ldActiveTab = 'main';

// ================= MENU "LOAD DATA" — BỐ CỤC 2 BẢNG SONG SONG =================
// Bảng 1 (trái) "Nhập file": khung kéo-thả/chọn file + thông tin file + chọn sheet + chẩn đoán cột.
// Bảng 2 (phải) "Dữ liệu đã nạp": hàng nút Nạp Dữ Liệu / Xóa dữ liệu ở TRÊN CÙNG (2 nút rộng bằng
// nhau, căn giữa), bên dưới là các số tổng (Dòng, Mã hàng, Tổng tồn cuối, Tổng trọng lượng, Cột
// khớp mẫu). Giữ NGUYÊN mọi id cũ (dropzone, btnChoose, btnTemplate, btnClear, ldNapDuLieuBtn,
// .sheet-tab) để bindLoadDataEvents() gắn handler y như trước — chỉ đổi cách sắp xếp.
function buildLoadDataSplitHtml(hasData){
  const fmtInt = n => Number(n).toLocaleString('en-US');
  let left = `<section class="ld-panel">
    <div class="ld-panel-head">
      <div class="ph-ic">${icon('upload')}</div>
      <div><h3>Nhập file</h3><p>Excel báo cáo tồn — hệ thống tự nhận dòng tiêu đề và khớp cột mẫu</p></div>
    </div>
    <div class="ld-drop" id="dropzone">
      <div class="dz-ic">${icon('upload')}</div>
      <h4>${hasData ? 'Kéo thả để nạp file khác' : 'Kéo thả file Excel vào đây'}</h4>
      <p>Hỗ trợ <span class="accent">.xlsx · .xls · .csv</span> — hoặc bấm để chọn file</p>
      <div class="dz-actions">
        <button class="btn btn-primary" id="btnChoose">${icon('file')} Chọn file</button>
        <button class="btn btn-ghost" id="btnTemplate">${icon('download')} File mẫu</button>
      </div>
    </div>`;

  let right = `<section class="ld-panel">
    <div class="ld-panel-head">
      <div class="ph-ic">${icon('stack')}</div>
      <div><h3>Dữ liệu đã nạp</h3><p>Kho dùng chung cho mọi menu (Dữ Liệu, Phân Loại, Báo Cáo…)</p></div>
      <div class="ph-right">${hasData ? '<span class="ph-badge ok">● Đã lưu</span>' : '<span class="ph-badge none">Chưa có dữ liệu</span>'}</div>
    </div>
    <div class="ld-actions">
      <button class="btn btn-primary" id="ldNapDuLieuBtn" ${hasData?'':'disabled'} title="Lưu lại toàn bộ dữ liệu hiện tại vào kho dùng chung (menu Dữ Liệu và mọi menu lọc đều đọc từ đây). Dữ liệu vốn đã tự lưu khi nạp file — nút này để chủ động lưu lại cho chắc.">${icon('download')} Nạp Dữ Liệu</button>
      <button class="btn btn-ghost" id="ldCmpBtn" ${hasData?'':'disabled'} title="So sánh dữ liệu đang nạp với LẦN NẠP TRƯỚC: mã tăng, giảm, mới, đã hết; tổng theo nhóm đặc tính và Size">⇄ So sánh lần nạp trước</button>
      <button class="btn btn-danger" id="btnClear" ${hasData?'':'disabled'}>${icon('trash')} Xóa dữ liệu</button>
    </div>`;

  if(hasData){
    const {matched, missing} = matchedSchema();
    left += `<div class="ld-fileinfo">
      <span>File</span><b title="${escapeHtml(state.fileName || '')}">${escapeHtml(state.fileName || 'Dữ liệu đã lưu trước đó')}</b>
      ${dataDateFieldHtml('ldDataDate', state.dataDate)}
      ${state.importedAt ? `<span>Nạp lúc</span><b>${new Date(state.importedAt).toLocaleString('vi-VN')}</b>` : ''}
      ${state.currentSheet ? `<span>Sheet</span><b>${escapeHtml(state.currentSheet)}</b>` : ''}
    </div>`;
    if(state.sheets.length > 1){
      left += `<div class="sheet-tabs">` + state.sheets.map(s =>
        `<button class="sheet-tab ${s===state.currentSheet?'active':''}" data-sheet="${s}">${s}</button>`
      ).join('') + `</div>`;
    }
    if(state.headerRowInfo && state.headerRowInfo.skippedRows > 0){
      left += `<div class="diag-note">
        ${icon('search')}<div>Đã bỏ qua ${state.headerRowInfo.skippedRows} dòng phía trên và dùng
        <b>dòng ${state.headerRowInfo.rowNumber}</b> làm tiêu đề cột (khớp ${state.headerRowInfo.matchScore} cột mẫu).</div>
      </div>`;
    }
    if(matched.length === 0 && state.headerRowInfo){
      left += `<div class="diag-note warn-note">
        ${icon('file')}<div>Không có cột nào khớp mẫu. Tiêu đề đọc được ở dòng ${state.headerRowInfo.rowNumber} là:
        <span class="raw-headers">${state.headers.filter(h=>h).map(h=>escapeHtml(h)).join(' · ') || '(trống)'}</span>
        — kiểm tra lại tên cột trong file, hoặc file có thể dùng ảnh/merge ô làm tiêu đề khiến không đọc được text.</div>
      </div>`;
    }
    if(missing.length){
      left += `<div class="schema-row">` + missing.map(m => `<span class="chip warn">⚠ thiếu: ${m}</span>`).join('') + `</div>`;
    }

    const distinctItems = countDistinctColumnAllRows('Mã hàng');
    const totalFinal = sumColumnAllRows('Tồn cuối');
    const totalWeight = sumTotalWeightAllRows();
    const pct = REQUIRED_COLUMNS.length ? Math.round(matched.length / REQUIRED_COLUMNS.length * 100) : 0;
    right += `<div class="ld-stats">
      ${dataDateStatHtml(state.dataDate, state.importedAt)}
      <div class="ld-stat"><div class="v">${fmtInt(state.rows.length)}</div><div class="l">Dòng</div></div>
      <div class="ld-stat"><div class="v">${distinctItems!==null ? fmtInt(distinctItems) : '—'}</div><div class="l">Mã hàng</div></div>
      <div class="ld-stat"><div class="v">${totalFinal!==null ? fmtInt(totalFinal) : '—'}</div><div class="l">Tổng tồn cuối</div></div>
      <div class="ld-stat"><div class="v">${totalWeight!==null ? totalWeight.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2}) : '—'}</div><div class="l">Tổng trọng lượng (kg)</div></div>
      <div class="ld-stat wide">
        <div class="ld-stat-row">
          <div><div class="v">${matched.length}/${REQUIRED_COLUMNS.length}</div><div class="l">Cột khớp mẫu · ${state.headers.length} cột</div></div>
          <span class="ld-goto">Xem chi tiết ở menu <b>Dữ Liệu →</b></span>
        </div>
        <div class="bar"><i class="${pct < 100 ? 'warn' : ''}" style="width:${pct}%"></i></div>
      </div>
    </div>`;
  } else {
    right += `<div class="ld-empty">Chưa có dữ liệu.<br>Chọn file ở bảng bên trái — các số tổng sẽ hiện ở đây.</div>`;
  }

  left += `</section>`;
  right += `</section>`;
  return `<div class="ld-split">${left}${right}</div>`;
}

// ---- 3 khu nạp file (chính / vị trí / vị trí Bột) gộp lại thành TAB, chỉ hiện 1 bảng tại 1
// thời điểm — trước đây xếp chồng dọc cả 3 khiến trang rất dài. Chấm tròn trên mỗi tab: xanh
// = đã có dữ liệu, xám = chưa có. Đổi tab KHÔNG đụng tới dữ liệu, chỉ đổi phần hiển thị. ----
const LD_TABS = [
  { id:'main',     label:'Dữ Liệu Chính', icon:'stack', note:'Kho dùng chung cho mọi menu (Dữ Liệu, Phân Loại, Báo Cáo…)' },
];
function ldTabHasData(id){
  if(id === 'main') return state.rows.length > 0 && state.headers.length > 0;
  return false;
}
const LD_TAB_PERM = { main:'M01' };
function ldVisibleTabs(){ return LD_TABS.filter(t => m1Can(LD_TAB_PERM[t.id])); }
function buildLoadDataTabsHtml(hasData){
  const ldTabs = ldVisibleTabs();
  if(!ldTabs.length) return `<div class="ld-tabs-wrap"><div class="ld-empty">Tài khoản của bạn chưa được cấp quyền dùng mục này.</div></div>`;
  if(!ldTabs.some(t => t.id === ldActiveTab)) ldActiveTab = ldTabs[0].id;
  const active = ldTabs.find(t => t.id === ldActiveTab);
  const tabsHtml = `<div class="ld-tabbar">` + ldTabs.map(t => `
    <button class="ld-tab ${t.id===ldActiveTab?'active':''}" data-ld-tab="${t.id}">
      ${icon(t.icon)}<span>${t.label}</span><i class="ld-tab-dot ${ldTabHasData(t.id)?'on':''}"></i>
    </button>`).join('') + `</div>
    <div class="ld-tab-note">${escapeHtml(active.note)}</div>`;

  let body;
  body = buildLoadDataSplitHtml(hasData);

  return `<div class="ld-tabs-wrap">${tabsHtml}${body}</div>`;
}

function renderLoadData(){
  const c = document.getElementById('content');
  const hasData = state.rows.length > 0 && state.headers.length > 0;
  const isStateSwitch = __ldPrevHasData !== null && __ldPrevHasData !== hasData;
  __ldPrevHasData = hasData;

  // Menu "Load Data": bố cục dạng tab (xem buildLoadDataTabsHtml). Phần còn lại của hàm bên dưới
  // chỉ phục vụ menu "Dữ Liệu".
  if(state.activeModule !== 'dulieu'){
    const splitHtml = buildLoadDataTabsHtml(hasData);
    if(!isStateSwitch){ c.innerHTML = splitHtml; bindLoadDataEvents(); return; }
    animateLoadDataSwap(c, splitHtml);
    return;
  }

  // Menu "Dữ Liệu": dùng lại TOÀN BỘ bộ render bảng của "Load Data" (tìm theo cột, phân trang,
  // số cam, kéo giãn cột...) nhưng KHÔNG có vùng kéo-thả file, đổi banner thành "Dữ liệu đã lưu
  // (dùng chung cho mọi menu)" + nút Xuất Excel, và thêm thanh tổng (Số dòng / Tồn kiện / Tồn KG)
  // ở cuối bảng. Dữ liệu lấy từ chính state.rows/state.headers đã lưu bền — mọi menu khác cũng
  // dùng chung nguồn này (giống mẫu menu "Dữ Liệu" của module "Tồn Kho Gửi").
  const isDuLieu = state.activeModule === 'dulieu';

  let html = hasData ? `
    <div class="dropzone compact" id="dropzone">
      <div class="dz-ic">${icon('upload')}</div>
      <div class="dz-body">
        <h3>Nhập file khác</h3>
        <p>Kéo thả hoặc bấm để chọn file <span class="accent">.xlsx · .xls · .csv</span> mới</p>
      </div>
      <div class="dz-actions">
        <button class="btn btn-primary" id="btnChoose">${icon('file')} Chọn file</button>
        <button class="btn btn-ghost" id="btnTemplate">${icon('download')} File mẫu</button>
      </div>
    </div>
  ` : `
    <div class="dropzone" id="dropzone">
      <div class="dz-ic">${icon('upload')}</div>
      <h3>Kéo thả file Excel vào đây</h3>
      <p>Hỗ trợ <span class="accent">.xlsx · .xls · .csv</span> — hoặc bấm để chọn file từ máy tính</p>
      <div class="dz-actions">
        <button class="btn btn-primary" id="btnChoose">${icon('file')} Chọn file Excel</button>
        <button class="btn btn-ghost" id="btnTemplate">${icon('download')} Tải file mẫu</button>
      </div>
    </div>
  `;

  // Menu "Dữ Liệu": không có vùng kéo-thả file (chỉ Load Data mới nạp file mới).
  if(isDuLieu){
    html = '';
    if(!hasData){
      html = `<div class="data-wrap show"><div class="empty-state" style="padding:44px 20px;text-align:center;line-height:1.7;">
        Chưa có dữ liệu.<br>Vào menu <b>Load Data</b> nạp file Excel — dữ liệu sẽ được lưu và dùng chung cho tất cả các menu.
      </div></div>`;
    }
  }

  if(hasData){
    const {matched, missing} = matchedSchema();
    html += `<div class="data-wrap show" id="dataWrap">`;

    if(isDuLieu){
      html += `<div class="file-banner">
        <div class="fname">${icon('stack')} <span>Dữ liệu đã lưu <b>(dùng chung cho mọi menu)</b></span></div>
        <div style="display:flex;align-items:center;gap:10px;">
          <button class="btn btn-ghost" id="dlExportBtn">${icon('download')} Xuất Excel</button>
          <button class="btn btn-danger" id="btnClear">${icon('trash')} Xóa bảng dữ liệu</button>
        </div>
      </div>`;
    } else {
      html += `<div class="file-banner">
        <div class="fname">${icon('file')} <span>File: <b>${state.fileName || 'Dữ liệu đã lưu trước đó'}</b></span></div>
        <div style="display:flex;align-items:center;gap:14px;">
          <span class="meta">${state.importedAt ? 'Nạp lúc ' + new Date(state.importedAt).toLocaleString('vi-VN') : ''}</span>
          <button class="btn btn-primary" id="ldNapDuLieuBtn" title="Lưu lại toàn bộ dữ liệu hiện tại vào kho dùng chung (menu Dữ Liệu và mọi menu lọc đều đọc từ đây). Dữ liệu vốn đã tự lưu khi nạp file — nút này để chủ động lưu lại cho chắc.">${icon('download')} Nạp Dữ Liệu</button>
          <button class="btn btn-danger" id="btnClear">${icon('trash')} Xóa dữ liệu</button>
        </div>
      </div>`;
    }

    if(state.sheets.length > 1){
      html += `<div class="sheet-tabs">` + state.sheets.map(s =>
        `<button class="sheet-tab ${s===state.currentSheet?'active':''}" data-sheet="${s}">${s}</button>`
      ).join('') + `</div>`;
    }

    if(state.headerRowInfo && state.headerRowInfo.skippedRows > 0){
      html += `<div class="diag-note">
        ${icon('search')} Đã bỏ qua ${state.headerRowInfo.skippedRows} dòng phía trên và dùng
        <b>dòng ${state.headerRowInfo.rowNumber}</b> làm tiêu đề cột (khớp ${state.headerRowInfo.matchScore} cột mẫu).
      </div>`;
    }
    if(matched.length === 0 && state.headerRowInfo){
      html += `<div class="diag-note warn-note">
        ${icon('file')} Không có cột nào khớp mẫu. Tiêu đề đọc được ở dòng ${state.headerRowInfo.rowNumber} là:
        <span class="raw-headers">${state.headers.filter(h=>h).map(h=>escapeHtml(h)).join(' · ') || '(trống)'}</span>
        — kiểm tra lại tên cột trong file, hoặc file có thể dùng ảnh/merge ô làm tiêu đề khiến không đọc được text.
      </div>`;
    }

    if(missing.length){
      html += `<div class="schema-row">`;
      html += missing.map(m => `<span class="chip warn">⚠ thiếu: ${m}</span>`).join('');
      html += `</div>`;
    }

    const fr = filteredRows();
    const totalPages = Math.max(1, Math.ceil(fr.length / FILTER_PAGE_SIZE));
    if(state.page > totalPages) state.page = totalPages;
    if(state.page < 1) state.page = 1;
    const pageStart = (state.page - 1) * FILTER_PAGE_SIZE;
    const pageRows = fr.slice(pageStart, pageStart + FILTER_PAGE_SIZE);

    // Bảng dữ liệu (tìm theo cột, phân trang, số cam) CHỈ hiển thị ở menu "Dữ Liệu". Menu
    // "Load Data" giờ chỉ còn khung nạp file + trạng thái — việc xem/tìm/lọc/xuất dữ liệu đã
    // chuyển hẳn sang menu "Dữ Liệu".
    if(isDuLieu){
    html += `<div class="toolbar">
      <div class="search-box">
        ${icon('search')}
        <input type="text" id="searchInput" placeholder="Tìm: BTP 50 MKS Nau, mã hàng, tên hàng, hợp đồng…" title="Gõ nhãn Size (BTP 50, NL 85…) + các từ khác: mỗi từ tìm ở mọi cột, không phân biệt dấu/khoảng trắng" value="${state.search.replace(/"/g,'&quot;')}">
      </div>
      <div class="toolbar-right">
        <span id="ldRowCountInfo" style="font-family:var(--mono);font-size:12px;color:var(--text-muted);">
          ${fr.length===0 ? '0 dòng' : `Hiển thị ${(pageStart+1).toLocaleString('en-US')}–${Math.min(pageStart+FILTER_PAGE_SIZE, fr.length).toLocaleString('en-US')} / ${fr.length.toLocaleString('en-US')} dòng`}
        </span>
      </div>
    </div>`;

    html += `<div class="table-shell"><div class="table-scroll ld-table-scroll"><table class="resizable-table" id="ldTable"><colgroup>` +
      `<col style="width:38px">` +
      state.headers.map((h,ci) => `<col data-col-index="${ci}" style="width:${ldColWidth(ci,h)}px">`).join('') +
      `</colgroup><thead><tr>` +
      `<th class="chk-col"><input type="checkbox" id="chkAll"></th>` +
      state.headers.map((h,ci) => {
        const active = state.sortCol === ci;
        const hasFilter = colFilterActive(state.colFilters[ci]);
        // Tay kéo cột "leading": gắn vào <th> KẾ TIẾP (mép trái) — xem chú thích đầy đủ ở
        // js/table-resize.js (hàm addColResizers): <th> dùng position:sticky nên <th> đứng sau
        // luôn đè lên đúng vị trí tay kéo mép phải của <th> đứng trước, chuột không bấm trúng
        // được. Cột CUỐI không có <th> kế tiếp nên vẫn giữ tay kéo mép phải của chính nó.
        const isLast = ci === state.headers.length - 1;
        const leadingResizer = ci > 0
          ? `<span class="col-resizer col-resizer--leading" data-resize-col="${ci-1}" title="Kéo để đổi độ rộng · nhấp đúp để tự căn theo dữ liệu"></span>`
          : '';
        const trailingResizer = isLast
          ? `<span class="col-resizer" data-resize-col="${ci}" title="Kéo để đổi độ rộng · nhấp đúp để tự căn theo dữ liệu"></span>`
          : '';
        return `<th class="${isNumericCol(h)?'num':''} sortable ${active?'sorted':''} ${hasFilter?'has-filter':''}" data-col="${ci}">
          ${leadingResizer}
          <div class="th-row">
            <span class="th-label">${h||'—'}</span>
          </div>
          ${trailingResizer}
        </th>`;
      }).join('') +
      `</tr><tr class="col-search-row">` +
      `<th class="chk-col"></th>` +
      state.headers.map((h,ci) => { const hasFilter = colFilterActive(state.colFilters[ci]); return `<th><div class="col-search-cell"><input type="text" class="ldColSearchInput" data-col-index="${ci}"
          placeholder="Tìm ${escapeHtml((h||'').toLowerCase())}…" value="${escapeHtml(state.colSearch[ci]||'')}"><button type="button" class="th-filter-btn ${hasFilter?'active':''}" data-filter-col="${ci}" title="Lọc theo ${h||'—'}">${icon('search')}</button></div></th>`; }).join('') +
      `</tr></thead><tbody>`;

    if(fr.length === 0){
      html += `<tr><td colspan="${state.headers.length+1}"><div class="empty-state">Không tìm thấy dòng nào khớp với từ khóa tìm kiếm.</div></td></tr>`;
    } else {
      pageRows.forEach(({i, row})=>{
        const rowH = state.rowHeights[i];
        html += `<tr${rowH?` style="height:${rowH}px"`:''}>` +
          `<td class="chk-col"><input type="checkbox" class="rowChk" data-idx="${i}" ${state.selected.has(i)?'checked':''}></td>` +
          state.headers.map((h,ci)=>{
          const val = row[ci] ?? '';
          const cls = isNumericCol(h) ? 'num' : '';
          return `<td class="${cls}">${val===''?'<span class="cell-empty">—</span>':escapeHtml(val)}</td>`;
        }).join('') + '</tr>';
      });
    }
    html += `</tbody></table></div>`;

    // Phân trang: bảng có thể mang hàng nghìn dòng (file Excel tải lên thường xuyên 4-5000
    // dòng) — vẽ hết TẤT CẢ dòng cùng lúc vào DOM gây giật/lag nặng (đo thực tế ~6 giây/lần vẽ
    // với 5000 dòng). Chỉ vẽ TỐI ĐA FILTER_PAGE_SIZE (50) dòng/trang, có nút chuyển trang —
    // dùng lại đúng biến state.page vốn đã có sẵn (được reset về 1 mỗi khi đổi tìm kiếm/lọc/sắp
    // xếp ở các nơi khác trong file) nhưng trước đây CHƯA từng được dùng để giới hạn số dòng vẽ.
    { // PC 7.38: LUÔN có khung phân trang (ẩn khi ít dòng) → gõ tìm kiếm không còn phải vẽ lại cả trang khi số dòng vượt/giảm dưới 10
      html += `<div class="pagination" id="ldPaginationBox"${(fr.length > 10 || FILTER_PAGE_SIZE < fr.length) ? '' : ' style="display:none"'}>
        <button type="button" class="btn btn-ghost btn-sm" id="ldPagePrev" ${state.page<=1?'disabled':''}>${icon('file')} Trước</button>
        <span class="pagination-info">Trang ${state.page.toLocaleString('en-US')} / ${totalPages.toLocaleString('en-US')}</span>
        <button type="button" class="btn btn-ghost btn-sm" id="ldPageNext" ${state.page>=totalPages?'disabled':''}>Sau ${icon('file')}</button>
        <select id="ldPageSizeSelect" class="btn btn-ghost btn-sm" style="cursor:pointer;">
          <option value="10" ${FILTER_PAGE_SIZE===10?'selected':''}>10 dòng/trang</option>
          <option value="15" ${FILTER_PAGE_SIZE===15?'selected':''}>15 dòng/trang</option>
          <option value="20" ${FILTER_PAGE_SIZE===20?'selected':''}>20 dòng/trang</option>
          <option value="25" ${FILTER_PAGE_SIZE===25?'selected':''}>25 dòng/trang</option>
        </select>
      </div>`;
    }

    html += `</div>`; // table-shell
    } else {
      // Menu "Load Data": KHÔNG hiển thị bảng nữa — chỉ báo dữ liệu đã sẵn sàng và hướng dẫn
      // sang menu "Dữ Liệu" để xem/tìm/lọc/xuất.
      html += `<div class="ld-ready-note">
        ${icon('stack')}
        <div>
          <b>Đã có ${fr.length.toLocaleString('en-US')} dòng dữ liệu.</b>
          Mở menu <b class="ld-ready-link">Dữ Liệu</b> để xem, tìm và lọc theo cột, cùng xuất Excel.
          Bấm <b class="ld-ready-link">⇩ Nạp Dữ Liệu</b> ở trên để chủ động lưu lại vào kho dùng chung.
        </div>
      </div>`;
    }

    // Thanh tổng cuối bảng cho menu "Dữ Liệu" (giống mẫu Module "Tồn Kho Gửi"):
    // Số dòng, Tồn kiện = Σ cột "Tồn cuối" (kiện), Tồn KG = Σ cột "Tồn cuối (net)" (khối lượng).
    // Cộng trên tập dòng ĐANG HIỂN THỊ (đã áp tìm/lọc theo cột), không theo trang.
    if(isDuLieu){
      const tonCuoiCi = state.headers.findIndex(h => normalize(h) === 'tồn cuối');
      const netCi = state.headers.findIndex(h => normalize(h) === 'tồn cuối (net)');
      let sumKien = 0, sumKg = 0;
      fr.forEach(({row})=>{
        if(tonCuoiCi >= 0){ const n = parseLooseNumber(row[tonCuoiCi]); if(!isNaN(n)) sumKien += n; }
        if(netCi >= 0){ const n = parseLooseNumber(row[netCi]); if(!isNaN(n)) sumKg += n; }
      });
      html += `<div class="dl-summary">
        <div class="stat-chip"><div class="v">${fr.length.toLocaleString('en-US')}</div><div class="l">Số dòng</div></div>
        <div class="stat-chip"><div class="v">${sumKien.toLocaleString('en-US')}</div><div class="l">Tồn kiện</div></div>
        <div class="stat-chip"><div class="v">${sumKg.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2})}</div><div class="l">Tồn KG</div></div>
      </div>`;
    }

    html += `</div>`; // data-wrap
  }

  if(!isStateSwitch){
    c.innerHTML = html;
    bindLoadDataEvents();
    fillResizableTableWidth(document.getElementById('ldTable'));
    return;
  }
  animateLoadDataSwap(c, html);
}

// Hiệu ứng chuyển MƯỢT đúng lúc bố cục Load Data đổi hẳn (chưa có dữ liệu <-> đã có dữ liệu) —
// 2 trạng thái cao thấp khác nhau rất nhiều (dropzone to/gọn, có/không có bảng) nên đổi thẳng
// bằng innerHTML sẽ nhảy khung ngay lập tức. Dùng kỹ thuật FLIP đơn giản: đo chiều cao CŨ, khoá
// cứng + cho mờ dần; thay nội dung MỚI vào (còn đang bị khoá chiều cao cũ nên chưa hiện đúng);
// đo chiều cao THẬT của nội dung mới bằng scrollHeight (vẫn đo đúng dù đang bị overflow:hidden
// giới hạn); rồi cho trình duyệt tự chạy transition từ chiều cao CŨ sang chiều cao MỚI trong lúc
// nội dung mới hiện dần lên. Xong thì gỡ hết style inline để #content trở lại tự nhiên (flex:1,
// cao theo nội dung) như cũ — tránh các lần renderLoadData() SAU (đổi trang, lọc...) bị dính
// chiều cao cứng còn sót lại của lần chuyển đổi này.
function animateLoadDataSwap(c, newHtml){
  const oldHeight = c.getBoundingClientRect().height;
  c.style.transition = 'none';
  c.style.overflow = 'hidden';
  c.style.height = oldHeight + 'px';
  c.style.opacity = '1';
  // ép reflow để trình duyệt "chốt" đúng chiều cao CŨ ở trên trước khi đổi opacity bên dưới,
  // nếu không 2 lần đổi style liền nhau có thể bị gộp làm 1, mất hẳn hiệu ứng mờ dần.
  void c.offsetHeight;
  c.style.transition = `opacity ${getComputedStyle(document.documentElement).getPropertyValue('--fast') || '.15s'}`;
  c.style.opacity = '0';

  setTimeout(function(){
    c.innerHTML = newHtml;
    bindLoadDataEvents();
    fillResizableTableWidth(document.getElementById('ldTable'));

    const newHeight = c.scrollHeight;
    c.style.transition = 'none';
    c.style.opacity = '0';
    void c.offsetHeight; // ép reflow lần nữa trước khi bật transition cho chiều cao + opacity mới
    c.style.transition = `height .32s cubic-bezier(.4,0,.2,1), opacity .22s cubic-bezier(.4,0,.2,1) .06s`;
    c.style.height = newHeight + 'px';
    c.style.opacity = '1';

    let done = false;
    function cleanup(){
      if(done) return; done = true;
      c.style.transition = '';
      c.style.height = '';
      c.style.overflow = '';
      c.style.opacity = '';
      c.removeEventListener('transitionend', onEnd);
    }
    function onEnd(e){ if(e.target === c && e.propertyName === 'height') cleanup(); }
    c.addEventListener('transitionend', onEnd);
    setTimeout(cleanup, 500); // lưới an toàn phòng transitionend không bắn (vd chiều cao mới = cũ)
  }, 160);
}

function escapeHtml(v){
  return String(v).replace(/[&<>"']/g, s => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[s]));
}

/* ============================================================
   BÔI ĐEN Ô ĐỂ TÍNH TỔNG KIỂU EXCEL — dùng CHUNG cho bất kỳ bảng nào, gắn bằng cách gọi
   initExcelLikeSelection(table) sau mỗi lần bảng đó được vẽ lại. Kéo chuột từ 1 ô sang các ô
   khác (trong CÙNG 1 bảng) để chọn vùng chữ nhật — giống hệt cách bôi đen trong Excel; thanh
   trạng thái cố định dưới màn hình (#excelStatusBar) sẽ hiện Tổng/Trung bình/Nhỏ nhất/Lớn nhất
   của các ô có giá trị số trong vùng đã chọn. Bấm ra ngoài bảng hoặc bấm nút "Bỏ chọn" để tắt.
   Làm MẪU trước cho bảng Load Data (#ldTable) — nếu ổn sẽ áp dụng thêm cho các bảng khác.
   ============================================================ */
// SỬA RÒ RỈ BỘ NHỚ (rà soát PC 2.9): hàm này được gọi lại SAU MỖI LẦN VẼ bảng (8 chỗ + initExcelLikeSelectionForContent
// ở renderAll). Trước đây mỗi lần gọi lại gắn thêm 2 listener 'mouseup' + 'copy' lên document (giữ tham chiếu bảng CŨ đã
// bị thay → không giải phóng được bảng hàng nghìn dòng) và gắn lại listener cho TỪNG ô (bảng không vẽ lại thì mỗi ô bị
// gắn chồng nhiều lần). Dùng lâu (lọc / chuyển trang cả ngày) → bộ nhớ tăng dần, thao tác chậm dần.
// Nay: listener document gắn ĐÚNG 1 LẦN (_m1Xl); mỗi bảng chỉ khởi tạo 1 lần (table.__xlInit) và bắt sự kiện kiểu ủy
// quyền trên chính bảng (tbody vẽ lại vẫn chạy, không gắn từng ô).
const _m1Xl = { stop: null, copy: null };
document.addEventListener('mouseup', ()=>{ if(_m1Xl.stop) _m1Xl.stop(); });
document.addEventListener('copy', (e)=>{ if(_m1Xl.copy) _m1Xl.copy(e); });
function initExcelLikeSelection(table){
  if(!table || table.__xlInit) return;
  table.__xlInit = true;
  table.classList.add('excel-select-enabled');
  const statusBar = document.getElementById('excelStatusBar');
  let isSelecting = false;
  let anchorCell = null;
  let lastRange = null; // {r1,r2,c1,c2} của vùng đang bôi đen trong bảng NÀY — dùng để copy khi bấm Ctrl+C

  function getCellPos(td){
    const tr = td.closest('tr');
    if(!tr || !tr.parentElement) return null;
    return { row: tr.rowIndex, col: td.cellIndex };
  }
  function clearHighlight(){
    table.querySelectorAll('td.cell-selected').forEach(td=>td.classList.remove('cell-selected'));
  }
  function clearSelection(){
    clearHighlight();
    lastRange = null;
    if(statusBar) statusBar.style.display = 'none';
  }
  function highlightRange(a, b){
    clearHighlight();
    const r1 = Math.min(a.row, b.row), r2 = Math.max(a.row, b.row);
    const c1 = Math.min(a.col, b.col), c2 = Math.max(a.col, b.col);
    lastRange = { r1, r2, c1, c2 };
    _m1Xl.copy = copyRange;
    const selectedCells = [];
    for(let r=r1; r<=r2; r++){
      const tr = table.rows[r];
      if(!tr) continue;
      for(let c=c1; c<=c2; c++){
        const td = tr.cells[c];
        if(!td) continue;
        td.classList.add('cell-selected');
        selectedCells.push(td);
      }
    }
    updateStatusBar(selectedCells);
  }
  function updateStatusBar(cells){
    if(!statusBar) return;
    if(cells.length < 2){ statusBar.style.display = 'none'; return; }
    const nums = [];
    cells.forEach(td=>{
      const raw = td.textContent.trim();
      if(!raw || raw === '—') return;
      const n = parseLooseNumber(raw);
      if(!isNaN(n)) nums.push(n);
    });
    if(nums.length === 0){ statusBar.style.display = 'none'; return; }
    const sum = nums.reduce((a,b)=>a+b, 0);
    const avg = sum / nums.length;
    const fmt = n => n.toLocaleString('en-US', { maximumFractionDigits: 2 });
    statusBar.innerHTML = `
      <span>Đã chọn: <b>${cells.length.toLocaleString('en-US')}</b> ô (${nums.length.toLocaleString('en-US')} số)</span>
      <span>Tổng: <b>${fmt(sum)}</b></span>
      <span>Trung bình: <b>${fmt(avg)}</b></span>
      <span>Nhỏ nhất: <b>${fmt(Math.min(...nums))}</b></span>
      <span>Lớn nhất: <b>${fmt(Math.max(...nums))}</b></span>
      <button type="button" class="esb-clear" id="esbClearBtn">Bỏ chọn ✕</button>
    `;
    statusBar.style.display = 'flex';
    const clearBtn = document.getElementById('esbClearBtn');
    if(clearBtn) clearBtn.addEventListener('click', clearSelection);
  }

  // ủy quyền sự kiện trên bảng (ô thuộc tbody của CHÍNH bảng này)
  const cellOf = (e)=>{ const td = e.target.closest && e.target.closest('td'); return td && td.closest('table') === table && td.parentElement && td.parentElement.parentElement && td.parentElement.parentElement.tagName === 'TBODY' ? td : null; };
  table.addEventListener('mousedown', (e)=>{
    const td = cellOf(e); if(!td) return;
    // Bấm chuột phải, hoặc bấm vào checkbox/input/button/link bên trong ô (VD cột chọn dòng,
    // ô tìm kiếm) thì để hành vi mặc định, không bắt đầu bôi đen.
    if(e.button !== 0 || e.target.closest('input, button, a, textarea, select')) return;
    const pos = getCellPos(td);
    if(!pos) return;
    isSelecting = true;
    _m1Xl.stop = ()=>{ isSelecting = false; };
    anchorCell = pos;
    highlightRange(anchorCell, anchorCell);
    e.preventDefault();
  });
  table.addEventListener('mouseover', (e)=>{
    if(!isSelecting || !anchorCell) return;
    const td = cellOf(e); if(!td) return;
    const pos = getCellPos(td);
    if(pos) highlightRange(anchorCell, pos);
  });

  // Bấm Ctrl+C (hoặc Cmd+C trên Mac) khi đang có vùng ô bôi đen kiểu Excel trong CHÍNH bảng này
  // -> copy nội dung các ô đó vào clipboard (dạng TSV: mỗi dòng xuống dòng, mỗi ô cách nhau
  // bằng Tab, dán thẳng vào Excel/Google Sheets được). Bảng dùng user-select:none nên phải tự đọc
  // nội dung từ đúng vùng đang bôi đen (lastRange). Listener 'copy' của document gắn 1 lần ở trên
  // (_m1Xl), gọi hàm của bảng vừa được bôi đen gần nhất.

  const copyRange = (e)=>{
    if(!lastRange || !table.querySelector('td.cell-selected')) return;
    const rows = [];
    for(let r=lastRange.r1; r<=lastRange.r2; r++){
      const tr = table.rows[r]; if(!tr) continue;
      const cols = [];
      for(let c=lastRange.c1; c<=lastRange.c2; c++){
        const td = tr.cells[c];
        cols.push(td ? td.textContent.trim() : '');
      }
      rows.push(cols.join('\t'));
    }
    e.clipboardData.setData('text/plain', rows.join('\n'));
    e.preventDefault();
  };
}


// Trì hoãn (debounce) 1 hàm — dùng cho các ô tìm kiếm: khi bảng có nhiều dòng, render lại
// toàn bộ bảng trên MỖI phím gõ sẽ giật/lag; debounce chỉ render lại sau khi người dùng
// ngừng gõ một khoảng ngắn, gõ nhanh nhiều ký tự vẫn mượt.
function debounce(fn, wait){
  // Dùng bản chung (lib/theme-sync.js): như cũ + chờ gõ dấu (IME) ghép xong mới chạy.
  if(window.KLAType) return KLAType.debounce(fn, wait);
  let t;
  return function(...args){
    clearTimeout(t);
    t = setTimeout(()=>fn.apply(this, args), wait);
  };
}
const SEARCH_DEBOUNCE_MS = 200;

/* ================= LỌC THEO CỘT KIỂU EXCEL AUTOFILTER (popup checkbox nhiều giá trị) =================
   Dùng CHUNG cho cả 3 nơi đang có icon lọc cột trong module này: bảng "Load Data" (cột động,
   khoá = chỉ số cột), 7 bảng module lọc (Thành Phẩm Chính, HLSO, Tôm…, khoá = chỉ số cột trong
   dữ liệu Load Data), và "Bảng Tổng Hợp HLSO" (1 cột duy nhất, khoá cố định "hlsoTitle").
   Quy ước lưu trữ: filtersObj[key] là 1 Set các giá trị đang được TICK CHỌN — khi tất cả giá
   trị hiện có đều được chọn (tương đương "không lọc") thì xoá hẳn khoá đó khỏi filtersObj.
   colFilterActive() / ensureColSearchPopoverEl() / closeColSearchPopover() /
   withScrollCloseSuppressed() / openColSearchPopoverGeneric() giờ nằm ở js/table-col-search.js
   (dùng chung toàn app) — file .html của module này đã thêm thẻ <script> nạp file đó TRƯỚC file
   .js này. cascadeExcept() dưới đây vẫn giữ ở module này vì chỉ module này dùng (lọc liên kết
   theo tầng cho bảng "Load Data" nhiều cột động, khác cách NXT Bột/Sốt làm qua
   cascadedColValues() dùng chung). */
// Lọc liên kết theo tầng (cascading filter): trước khi lấy danh sách giá trị để hiển thị trong
// dropdown của 1 cột, loại bỏ khỏi "rows" những dòng không khớp bộ lọc của các CỘT KHÁC đang bật
// (bỏ qua bộ lọc của chính cột "excludeKey") — nhờ vậy danh sách chọn chỉ còn các giá trị còn
// khả dụng theo những gì đã lọc ở cột khác, giống hệt cơ chế đã dùng ở module "Tồn Kho
// Gửi". valueOf(item, key) trả về giá trị thô của "item" ứng với khoá cột "key" (dùng khoá dạng
// chuỗi để so khớp đúng với các khoá không phải số, ví dụ cột ảo "__tpcStatus").
function cascadeExcept(rows, filtersObj, excludeKey, valueOf){
  const others = Object.keys(filtersObj).filter(k => k !== String(excludeKey) && colFilterActive(filtersObj[k]));
  if(!others.length) return rows;
  return rows.filter(r => others.every(k => filtersObj[k].has(valueOf(r, k))));
}
// ensureColSearchPopoverEl/closeColSearchPopover/withScrollCloseSuppressed/openColSearchPopoverGeneric
// + listener đóng popup khi click ra ngoài/cuộn/đổi cỡ cửa sổ giờ nằm ở js/table-col-search.js
// (dùng chung toàn app).

/* ---------- Resizable columns (module Load Data) ---------- */
const LD_COL_MIN_WIDTH = 70;
const LD_COL_MAX_WIDTH = 420; // chặn 1 cột có ô dữ liệu siêu dài (VD ghi chú) chiếm hết khung bảng

// SỬA LỖI "cột không tự dãn theo chiều dài dữ liệu": ldDefaultColWidth() CŨ chỉ tính độ rộng
// dựa theo ĐỘ DÀI TÊN CỘT (header) — ví dụ "Mã hàng" (7 ký tự) ra ~90px — trong khi DỮ LIỆU
// thật của cột đó (VD mã hàng "PBXPICYEDK...") dài hơn hẳn tiêu đề, nên luôn bị cắt "...". Cột
// số thậm chí còn cố định cứng 100px bất kể giá trị dài bao nhiêu. Nay đo ĐỘ RỘNG TỰ NHIÊN thật
// bằng canvas (giống cách js/table-resize.js đã làm cho các bảng khác trong app): rộng nhất
// giữa tiêu đề và MỌI ô dữ liệu đang có trong cột đó, theo đúng font/padding thật của
// thead th / tbody td (xem css/ton-kho-an-an.css). Nhờ vậy nhấp đúp vào tay kéo (vốn chỉ xoá độ
// rộng người dùng đã tự kéo tay rồi vẽ lại bảng — xem bindLdColResize() bên dưới) giờ cũng tự
// bung đúng khít nội dung thay vì chỉ quay về mức hẹp tính theo tên cột như trước.
let __ldAutoWidthCache = { rowsRef: null, headersRef: null, widths: null };
let __ldMeasureCtx = null;
function ldTextWidth(text, font){
  if(!__ldMeasureCtx) __ldMeasureCtx = document.createElement('canvas').getContext('2d');
  __ldMeasureCtx.font = font;
  return __ldMeasureCtx.measureText(text === null || text === undefined ? '' : String(text)).width;
}
function ldComputeAutoWidths(){
  // Font/size khớp đúng CSS thật: thead th{font-family:var(--body);font-weight:500;font-size:11px;
  // text-transform:uppercase} và tbody td{font-family:var(--body);font-size:12.5px} (cột số dùng
  // thêm font-family:var(--mono), nhưng --mono/--body cùng là 'Inter' trong theme hiện tại).
  const headFont = '500 11px Inter, sans-serif';
  const cellFont = '400 12.5px Inter, sans-serif';
  // Đệm mép cho tiêu đề: padding 14px×2 (28) + nút lọc (~26) + mũi tên sắp xếp (~16) + tay kéo
  // (~8) — đủ chỗ để tên cột không bị chính các icon trên tiêu đề che mất.
  const headPad = 28 + 26 + 16 + 8;
  const cellPad = 28 + 6; // padding 10px 14px của tbody td (10px 2 bên) + đệm chống răng cưa chữ
  return state.headers.map((h, ci)=>{
    let maxW = ldTextWidth(String(h||'').toUpperCase(), headFont) + headPad;
    const font = cellFont; // số hay chữ đều đo bằng cùng cỡ chữ 12.5px thật của ô
    for(let i=0; i<state.rows.length; i++){
      const raw = state.rows[i][ci];
      if(raw === '' || raw === null || raw === undefined) continue;
      const w = ldTextWidth(raw, font) + cellPad;
      if(w > maxW) maxW = w;
    }
    return Math.max(LD_COL_MIN_WIDTH, Math.min(LD_COL_MAX_WIDTH, Math.ceil(maxW)));
  });
}
// Chỉ tính lại khi state.rows/state.headers thực sự đổi THAM CHIẾU (nạp file mới, phục hồi dữ
// liệu đã lưu…) — không tính lại trên mỗi lần vẽ bảng (gõ tìm kiếm, lọc, đổi trang…) vì bảng có
// thể tới vài nghìn dòng, tính lại mỗi phím gõ sẽ giật.
function ldAutoColWidth(ci, header){
  if(__ldAutoWidthCache.rowsRef !== state.rows || __ldAutoWidthCache.headersRef !== state.headers){
    __ldAutoWidthCache.widths = ldComputeAutoWidths();
    __ldAutoWidthCache.rowsRef = state.rows;
    __ldAutoWidthCache.headersRef = state.headers;
  }
  const w = __ldAutoWidthCache.widths[ci];
  return (w !== undefined) ? w : ldDefaultColWidth(ci, header);
}
// Vẫn giữ lại làm phương án dự phòng (VD chưa có state.rows/headers lúc gọi) — không còn là
// nguồn chính quyết định độ rộng cột nữa, xem ldAutoColWidth() ở trên.
function ldDefaultColWidth(ci, header){
  if(isNumericCol(header)) return 100;
  return Math.max(90, Math.min(220, String(header||'').length * 10 + 50));
}
function ldColWidth(ci, header){
  return state.colWidths[ci] || ldAutoColWidth(ci, header);
}
// Nhấp đúp viền cột bảng Dữ Liệu → bung vừa khít dữ liệu dài nhất của cột (trần rộng
// COL_AUTOFIT_MAX, không bị chặn ở 420px như độ rộng tự động mặc định).
function autoFitLdColumn(ci){
  const idx = Number(ci);
  if(isNaN(idx) || !state.headers[idx]) return;
  const values = state.rows.map(r => r[idx]);
  state.colWidths[idx] = measureColFitWidth(state.headers[idx], values, COL_AUTOFIT_MAX, 0);
  renderLoadData();
}
// Trạng thái kéo cột dùng chung, gắn listener ở cấp document MỘT LẦN duy nhất
// (xem cuối file) để tránh chồng listener mỗi lần renderLoadData() vẽ lại bảng.
let ldColDrag = null;
function bindLdColResize(){
  document.querySelectorAll('#dataWrap .col-resizer').forEach(handle=>{
    // Tay kéo giờ được GẮN VÀO <th> kế tiếp (xem nơi tạo HTML ở renderLoadData()) nên luôn nổi
    // trên đường biên chung — không cần "nâng z-index khi hover" nữa (cách cũ không hoạt động
    // vì tay kéo bị che nên sự kiện hover không bao giờ bắn ra). Việc đổi độ rộng cũng không
    // phụ thuộc <th> nào đang chứa tay kéo — chỉ dựa vào data-resize-col để tìm đúng <col>.
    handle.addEventListener('mousedown', e=>{
      e.preventDefault();
      e.stopPropagation();
      const ci = handle.dataset.resizeCol;
      const colEl = document.querySelector(`#dataWrap col[data-col-index="${ci}"]`);
      if(!colEl) return;
      ldColDrag = { ci, colEl, startX: e.clientX, startWidth: colEl.getBoundingClientRect().width };
      handle.classList.add('resizing');
      document.body.style.cursor = 'col-resize';
      document.body.style.userSelect = 'none';
    });
    handle.addEventListener('dblclick', e=>{
      e.preventDefault();
      e.stopPropagation();
      autoFitLdColumn(handle.dataset.resizeCol);
    });
  });
}

function bindLoadDataEvents(){
  const dz = document.getElementById('dropzone');
  const fileInput = document.getElementById('fileInput');
  const btnChoose = document.getElementById('btnChoose');
  const btnTemplate = document.getElementById('btnTemplate');

  if(dz){
    dz.addEventListener('click', ()=>fileInput.click());
    dz.addEventListener('dragover', e=>{e.preventDefault(); dz.classList.add('drag');});
    dz.addEventListener('dragleave', ()=>dz.classList.remove('drag'));
    dz.addEventListener('drop', e=>{
      e.preventDefault(); dz.classList.remove('drag');
      if(e.dataTransfer.files.length) handleFile(e.dataTransfer.files[0]);
    });
  }
  if(btnChoose) btnChoose.addEventListener('click', e=>{ e.stopPropagation(); fileInput.click(); });
  if(btnTemplate) btnTemplate.addEventListener('click', e=>{ e.stopPropagation(); downloadTemplate(); });

  const ldCmpBtn = document.getElementById('ldCmpBtn');
  if(ldCmpBtn) ldCmpBtn.addEventListener('click', e=>{ e.stopPropagation(); m1CmpDlg(); });
  const btnClear = document.getElementById('btnClear');
  if(btnClear) btnClear.addEventListener('click', ()=>{
    showConfirmPopup(
      'Xóa dữ liệu Load Data?',
      'Toàn bộ dữ liệu Excel đã nạp (bảng dữ liệu, các module lọc...) sẽ bị xoá. Dữ liệu nhập tay ở "Tổng Hợp > Báo cáo ngoài sổ sách" và module "Đặc Tính" sẽ KHÔNG bị ảnh hưởng.',
      clearData
    );
  });

  // Nút "Xuất Excel" của menu "Dữ Liệu" (chỉ tồn tại khi đang xem menu đó).
  const dlExportBtn = document.getElementById('dlExportBtn');
  if(dlExportBtn) dlExportBtn.addEventListener('click', exportDuLieuToExcel);

  // Nút "Nạp Dữ Liệu" ở menu "Load Data": chủ động lưu lại toàn bộ dữ liệu hiện tại vào kho
  // dùng chung (persist) rồi phản hồi ngay trên nút. Dữ liệu vốn đã tự lưu khi nạp file — nút
  // này để người dùng chủ động lưu lại cho chắc (và tự kiểm tra lưu có thành công không).
  const ldNapDuLieuBtn = document.getElementById('ldNapDuLieuBtn');
  if(ldNapDuLieuBtn) ldNapDuLieuBtn.addEventListener('click', async ()=>{
    if(!state.rows.length){ return; }
    const orig = ldNapDuLieuBtn.innerHTML;
    ldNapDuLieuBtn.disabled = true;
    let ok = true;
    try{ await persist(); }
    catch(e){ ok = false; console.error('Lỗi khi Nạp Dữ Liệu (persist):', e); }
    ldNapDuLieuBtn.innerHTML = ok
      ? `${icon('check')} Đã lưu ${state.rows.length.toLocaleString('en-US')} dòng`
      : `${icon('trash')} Lưu lỗi — thử lại`;
    setTimeout(()=>{ ldNapDuLieuBtn.innerHTML = orig; ldNapDuLieuBtn.disabled = false; }, 1800);
  });

  bindDataDateInput('ldDataDate', ()=> state.dataDate, async v=>{ state.dataDate = v; await persist(); });

  // Tab chuyển giữa 3 khu nạp file (chính / vị trí / vị trí Bột) — xem buildLoadDataTabsHtml().
  document.querySelectorAll('.ld-tab').forEach(btn => {
    btn.addEventListener('click', () => {
      const id = btn.getAttribute('data-ld-tab');
      if(id === ldActiveTab) return;
      ldActiveTab = id;
      renderLoadData();
    });
  });

  const ldPagePrev = document.getElementById('ldPagePrev');
  const ldPageNext = document.getElementById('ldPageNext');
  if(ldPagePrev) ldPagePrev.addEventListener('click', ()=>{ state.page = Math.max(1, state.page-1); renderLoadData(); });
  if(ldPageNext) ldPageNext.addEventListener('click', ()=>{ state.page = state.page+1; renderLoadData(); });
  const ldPageSizeSelect = document.getElementById('ldPageSizeSelect');
  if(ldPageSizeSelect) ldPageSizeSelect.addEventListener('change', ()=>{
    FILTER_PAGE_SIZE = parseInt(ldPageSizeSelect.value, 10);
    state.page = 1;
    renderLoadData();
  });

  document.querySelectorAll('.sheet-tab').forEach(tab=>{
    tab.addEventListener('click', ()=>loadSheet(tab.dataset.sheet));
  });

  document.querySelectorAll('#dataWrap .th-label').forEach(label=>{
    label.addEventListener('click', ()=>{
      const th = label.closest('th');
      const ci = Number(th.dataset.col);
      if(state.sortCol === ci){
        state.sortDir = state.sortDir === 'asc' ? 'desc' : 'asc';
      } else {
        state.sortCol = ci;
        state.sortDir = 'asc';
      }
      state.page = 1;
      renderLoadData();
    });
  });

  // Icon phễu: mở popup lọc theo cột kiểu Excel AutoFilter (checkbox nhiều giá trị, lọc ngay
  // khi tick) — giống hệt cơ chế đã dùng ở 7 module lọc (Thành Phẩm Chính/Tạm, HLSO, Tôm…),
  // đồng bộ giao diện toàn ứng dụng.
  document.querySelectorAll('#dataWrap .th-filter-btn').forEach(btn=>{
    btn.addEventListener('click', e=>{
      e.stopPropagation();
      const ci = Number(btn.dataset.filterCol);
      const label = state.headers[ci] || '';
      const searchedBase = Object.entries(state.colSearch).filter(([,v]) => String(v||'').trim() !== '').reduce((rows,[k,q]) => rows.filter(row => fuzzyIncludes(row[Number(k)], q)), state.rows);
      openColSearchPopoverGeneric(btn, ci, label, ()=>cascadeExcept(searchedBase, state.colFilters, ci, (row,k)=>normalizeCellForExactFilter(row[Number(k)])).map(row=>normalizeCellForExactFilter(row[ci])), state.colFilters, ()=>{ state.page = 1; renderLoadDataRows(); });
    });
  });

  const searchInput = document.getElementById('searchInput');
  if(searchInput){
    const doSearchRender = debounce(()=>{
      renderLoadDataRows();
      renderTopbar();
      // renderLoadDataRows() không đụng tới #searchInput nên không cần khôi phục focus nữa —
      // giữ đoạn này chỉ để phòng trường hợp hiếm renderLoadDataRows() phải rơi về vẽ lại đầy đủ
      // (renderLoadData(), xem chú thích ở đó) — lúc đó ô input MỚI thật sự bị tạo lại.
      const val = document.getElementById('searchInput');
      if(val && val !== searchInput){ val.focus(); val.selectionStart = val.selectionEnd = val.value.length; }
    }, SEARCH_DEBOUNCE_MS);
    searchInput.addEventListener('input', e=>{
      state.search = e.target.value;
      state.page = 1;
      doSearchRender();
    });
  }

  // Ô tìm kiếm riêng theo từng cột (dòng ngay dưới tiêu đề) — lọc kiểu "chứa". Dùng
  // renderLoadDataRows() (chỉ vẽ lại <tbody>, KHÔNG đụng tới chính ô .ldColSearchInput đang gõ)
  // nên không còn cần khôi phục focus/vị trí con trỏ theo cách cũ nữa — ô nhập không bao giờ bị
  // hủy/tạo lại giữa chừng nên gõ tiếng Việt có dấu (Telex/VNI) không còn bị ngắt quãng.
  const doColSearchRender = debounce(()=>{
    renderLoadDataRows();
  }, SEARCH_DEBOUNCE_MS);
  document.querySelectorAll('#dataWrap .ldColSearchInput').forEach(inp=>{
    inp.addEventListener('input', ()=>{
      const ci = inp.dataset.colIndex;
      state.colSearch[ci] = inp.value;
      state.page = 1;
      const wrap = document.getElementById('dataWrap');
      if(wrap) wrap.dataset.lastFocusCol = ci;
      doColSearchRender();
    });
  });

  bindLoadDataRowCheckboxes();
  bindLdColResize();
}

// Gắn sự kiện cho checkbox chọn dòng (cột đầu bảng "Load Data") + checkbox "chọn tất cả" ở tiêu
// đề — TÁCH RIÊNG khỏi bindLoadDataEvents() để có thể gọi lại một mình sau khi renderLoadDataRows()
// vẽ lại PHẦN <tbody> (tạo checkbox mới, cần gắn lại sự kiện), mà không phải gắn lại toàn bộ sự
// kiện của toolbar/tiêu đề cột (vốn không đổi, xem renderLoadDataRows() bên dưới).
function bindLoadDataRowCheckboxes(){
  const rowChks = document.querySelectorAll('#dataWrap .rowChk');
  const chkAll = document.getElementById('chkAll');

  function syncChkAll(){
    if(!chkAll) return;
    const ids = Array.from(rowChks).map(el => Number(el.dataset.idx));
    const allSel = ids.length>0 && ids.every(i=>state.selected.has(i));
    const someSel = ids.some(i=>state.selected.has(i));
    chkAll.checked = allSel;
    chkAll.indeterminate = !allSel && someSel;
  }
  syncChkAll();

  rowChks.forEach(chk=>{
    chk.addEventListener('change', ()=>{
      const idx = Number(chk.dataset.idx);
      if(chk.checked) state.selected.add(idx); else state.selected.delete(idx);
      syncChkAll();
    });
  });

  if(chkAll){
    chkAll.addEventListener('change', ()=>{
      const ids = Array.from(rowChks).map(el => Number(el.dataset.idx));
      if(chkAll.checked) ids.forEach(i=>state.selected.add(i));
      else ids.forEach(i=>state.selected.delete(i));
      rowChks.forEach(chk=>{ chk.checked = state.selected.has(Number(chk.dataset.idx)); });
      chkAll.indeterminate = false;
    });
  }
}

/* ============================================================
   VẼ LẠI CHỈ PHẦN DÒNG DỮ LIỆU (KHÔNG đụng vào toolbar/thead/col-search-row/dropzone) — dùng
   thay cho renderLoadData() (vẽ lại TOÀN BỘ khu vực nội dung) mỗi khi người dùng đang GÕ vào ô
   tìm kiếm (chung hoặc riêng theo cột).
   LÝ DO CẦN HÀM RIÊNG NÀY: renderLoadData() thay `#content`.innerHTML TOÀN BỘ, nghĩa là chính ô
   <input> người dùng đang gõ dở cũng bị HỦY VÀ TẠO LẠI mỗi lần debounce chạy — dù có khôi phục
   lại focus/vị trí con trỏ sau đó, việc ô nhập bị hủy giữa chừng vẫn làm NGẮT QUÃNG quá trình
   ghép dấu khi gõ tiếng Việt có dấu (Telex/VNI), cảm giác như "giật", không gõ liên tục được.
   Hàm này chỉ thay <tbody> của #ldTable + dòng chữ "Hiển thị X–Y / Z dòng" + khung phân trang —
   ô tìm kiếm (#searchInput, .ldColSearchInput) và toàn bộ tiêu đề cột KHÔNG BỊ ĐỤNG TỚI, nên
   không bao giờ mất focus/ngắt gõ dấu dù render bao nhiêu lần.
   Chỉ áp dụng được khi bảng #ldTable đã tồn tại sẵn (đã render đầy đủ ít nhất 1 lần) VÀ việc
   phân trang có/không xuất hiện KHÔNG đổi (ví dụ từ "có nhiều trang" thành "chỉ 1 trang" hoặc
   ngược lại) — 2 trường hợp không thỏa thì tự động gọi lại renderLoadData() đầy đủ như cũ. */
function renderLoadDataRows(){
  const table = document.getElementById('ldTable');
  const tbody = table && table.querySelector('tbody');
  if(!tbody){ renderLoadData(); return; }

  const fr = filteredRows();
  const totalPages = Math.max(1, Math.ceil(fr.length / FILTER_PAGE_SIZE));
  if(state.page > totalPages) state.page = totalPages;
  if(state.page < 1) state.page = 1;
  const pageStart = (state.page - 1) * FILTER_PAGE_SIZE;
  const pageRows = fr.slice(pageStart, pageStart + FILTER_PAGE_SIZE);

  const needsPagination = fr.length > 10 || FILTER_PAGE_SIZE < fr.length;
  const paginationBox = document.getElementById('ldPaginationBox');
  if(paginationBox) paginationBox.style.display = needsPagination ? '' : 'none'; // PC 7.38: chỉ ẩn / hiện, không vẽ lại cả trang

  tbody.innerHTML = fr.length === 0
    ? `<tr><td colspan="${state.headers.length+1}"><div class="empty-state">Không tìm thấy dòng nào khớp với từ khóa tìm kiếm.</div></td></tr>`
    : pageRows.map(({i, row})=>{
        const rowH = state.rowHeights[i];
        return `<tr${rowH?` style="height:${rowH}px"`:''}>` +
          `<td class="chk-col"><input type="checkbox" class="rowChk" data-idx="${i}" ${state.selected.has(i)?'checked':''}></td>` +
          state.headers.map((h,ci)=>{
            const val = row[ci] ?? '';
            const cls = isNumericCol(h) ? 'num' : '';
            return `<td class="${cls}">${val===''?'<span class="cell-empty">—</span>':escapeHtml(val)}</td>`;
          }).join('') + '</tr>';
      }).join('');

  const rowCountEl = document.getElementById('ldRowCountInfo');
  if(rowCountEl){
    rowCountEl.textContent = fr.length===0 ? '0 dòng' : `Hiển thị ${(pageStart+1).toLocaleString('en-US')}–${Math.min(pageStart+FILTER_PAGE_SIZE, fr.length).toLocaleString('en-US')} / ${fr.length.toLocaleString('en-US')} dòng`;
  }
  if(paginationBox){
    const prevBtn = document.getElementById('ldPagePrev');
    const nextBtn = document.getElementById('ldPageNext');
    const infoSpan = paginationBox.querySelector('.pagination-info');
    if(prevBtn) prevBtn.disabled = state.page<=1;
    if(nextBtn) nextBtn.disabled = state.page>=totalPages;
    if(infoSpan) infoSpan.textContent = `Trang ${state.page.toLocaleString('en-US')} / ${totalPages.toLocaleString('en-US')}`;
  }

  bindLoadDataRowCheckboxes();
  initExcelLikeSelection(table);
}

/* ================= RENDER: MODULE LỌC DÙNG CHUNG ================= */
/* ================= RENDER: THANH MENU CON "PHÂN LOẠI" ================= */
// Hiện ngay phía trên bảng dữ liệu khi module con đang xem thuộc nhóm "phanloai" — mẫu giống
// thanh menu con "Phân Loại" ở module "Tồn Kho Gửi".
function renderPhanLoaiSubnav(activeId){
  const items = MODULES.filter(m=>m.group==='phanloai');
  let n = 0;
  return `<div class="pl-subnav">` + items.map(m=>{
    if(m.tab){
      // PC 1.24: tab "Điều kiện lọc" — không đánh số, tách riêng bên phải 8 module
      return `<button type="button" class="pl-subtab-btn pl-tab-dk ${m.id===activeId?'active':''}" data-plsub="${m.id}" title="Xem các bước lọc và điều kiện của từng module Phân Loại">${icon('search','ic')}${m.label}</button>`;
    }
    n++;
    return `
    <button type="button" class="pl-subtab-btn ${m.id===activeId?'active':''}" data-plsub="${m.id}">
      <span class="num">${String(n).padStart(2,'0')}</span>${m.label}
    </button>
  `;
  }).join('') + `</div>`;
}
function bindPhanLoaiSubnav(){
  document.querySelectorAll('.pl-subtab-btn').forEach(btn=>{
    btn.addEventListener('click', ()=>{
      const target = btn.dataset.plsub;
      if(target === state.activeModule) return;
      // Sang tab "Điều kiện lọc" từ 1 module → mở sẵn điều kiện của module đang xem
      if(target === 'dkloc' && FILTER_MODULES[state.activeModule]) state.dkLocMod = state.activeModule;
      state.activeModule = target;
      if(target !== 'dkloc') state.phanLoaiSub = target;
      renderAll();
    });
  });
}

/* ================= TAB "ĐIỀU KIỆN LỌC" (PC 1.24) =================
   Trang riêng trong nhóm Phân Loại: với MỖI module lọc liệt kê đúng từng bước lọc theo thứ tự áp dụng
   (điều kiện chính → phụ → ưu tiên Khách hàng → tự thêm → chuyển thủ công) kèm số dòng CÒN LẠI / BỊ LOẠI ở từng bước.
   Hàm dkLocFunnel chạy lại ĐÚNG các bước của filterModuleBaseRows (chỉ đọc, không đổi dữ liệu) — nếu sửa quy tắc ở
   filterModuleBaseRows thì sửa cả ở đây; bước cuối "chuyển thủ công" tính bằng chênh lệch với kết quả thật nên số dòng cuối luôn khớp bảng. */
function dkLocColIndex(colName){
  return colName ? state.headers.findIndex(h => headersFuzzyMatch(h, colName)) : -1;
}
function dkLocCountConditions(modId){
  const cfg = FILTER_MODULES[modId];
  const st = state.filterModules[modId];
  return 1 + ((cfg.extraFilters || []).length) + ((st && st.customFilters) ? st.customFilters.length : 0);
}
// Điều kiện KHÔNG áp dụng được (không thấy cột trong Load Data) — bị bỏ qua âm thầm khi lọc nên cần báo
function dkLocWarnings(modId){
  const cfg = FILTER_MODULES[modId];
  const st = state.filterModules[modId];
  const out = [];
  if(!state.headers.length) return out;
  if(cfg.primaryMatch && dkLocColIndex(cfg.primaryMatchCol) === -1) out.push(`Điều kiện chính: không thấy cột "${cfg.primaryMatchCol}"`);
  (cfg.extraFilters || []).forEach((ef, idx)=>{ if(dkLocColIndex(ef.col) === -1) out.push(`Điều kiện phụ #${idx+1}: không thấy cột "${ef.col}"`); });
  ((st && st.customFilters) || []).forEach(cf=>{ if(dkLocColIndex(cf.colLabel) === -1) out.push(`Điều kiện tự thêm: không thấy cột "${cf.colLabel}"`); });
  return out;
}
// Chạy từng bước lọc, ghi lại số dòng còn lại sau mỗi bước
function dkLocFunnel(modId){
  const cfg = FILTER_MODULES[modId];
  const st = state.filterModules[modId];
  const total = state.rows.length;
  const steps = [];
  let prev = total;
  const add = (kind, label, rows, extra)=>{
    steps.push(Object.assign({ kind, label, remaining: rows.length, removed: prev - rows.length }, extra || {}));
    prev = rows.length;
  };
  const allRows = () => state.rows.map((row,i)=>({i,row}));
  let base;
  if(cfg.excludeAllOtherModules){
    const otherIds = Object.keys(FILTER_MODULES).filter(id => id !== modId);
    const matchers = otherIds.map(id => buildModuleMatcher(id));
    base = allRows().filter(({row}) => !matchers.some(fn => fn(row)));
    const names = otherIds.map(id => (MODULES.find(m=>m.id===id)||{}).label || id).join(', ');
    add('main', 'Lấy toàn bộ Load Data, loại mọi dòng đã thuộc module khác', base, { detail: names, locked: true });
  } else if(cfg.primaryMatch){
    const ci = dkLocColIndex(cfg.primaryMatchCol);
    base = ci === -1 ? allRows() : allRows().filter(({row}) => cfg.primaryMatch(row[ci]));
    add('main', cfg.primaryMatchLabel || 'Điều kiện chính', base, { col: cfg.primaryMatchCol, warn: ci === -1, locked: !!cfg.locked });
  } else {
    const ci = resolveFilterCol(modId);
    if(ci === null){
      base = allRows();
      add('main', 'Điều kiện chính', base, { warn: true });
    } else {
      const val = resolveFilterVal(modId, ci);
      base = !val ? allRows() : allRows().filter(({row}) => normalize(row[ci]) === normalize(val));
      add('main', `${state.headers[ci] || 'Cột'} = "${val}"`, base, { col: state.headers[ci], locked: !!cfg.locked });
    }
  }
  (cfg.extraFilters || []).forEach((ef, idx)=>{
    const ci = dkLocColIndex(ef.col);
    if(ci !== -1) base = base.filter(({row}) => ef.test(row, ci));
    add('extra', ef.label, base, { col: ef.col, warn: ci === -1, locked: !!cfg.locked, num: idx + 1 });
  });
  if(!cfg.excludeAllOtherModules){
    const priorityIds = moduleHasKhachHangCondition(modId) ? [] : Object.keys(FILTER_MODULES).filter(id => id !== modId && moduleHasKhachHangCondition(id));
    if(priorityIds.length){
      const pm = priorityIds.map(id => buildModuleMatcher(id));
      base = base.filter(({row}) => !pm.some(fn => fn(row)));
      add('priority', 'Ưu tiên theo điều kiện "Khách hàng" của module khác (tránh đếm trùng ở Báo Cáo)', base,
        { detail: priorityIds.map(id => (MODULES.find(m=>m.id===id)||{}).label || id).join(', '), locked: true });
    }
  }
  ((st && st.customFilters) || []).forEach(cf=>{
    const ci = dkLocColIndex(cf.colLabel);
    if(ci !== -1) base = base.filter(({row}) => testCustomFilter(row[ci], cf));
    add('custom', customFilterDesc(cf), base, { col: cf.colLabel, warn: ci === -1, cfId: cf.id });
  });
  // Chuyển thủ công: kết quả THẬT của module (đã tính cả dòng chuyển đi / chuyển đến) so với các bước ở trên
  const finalList = filterModuleBaseRowsCached(modId);
  const finalRows = finalList.length;
  if(finalRows !== prev || base.length !== finalRows){
    const baseSet = new Set(base.map(b => b.i)), finalSet = new Set(finalList.map(b => b.i));
    const outN = base.filter(b => !finalSet.has(b.i)).length, inN = finalList.filter(b => !baseSet.has(b.i)).length;
    steps.push({ kind:'manual', label:'Chuyển thủ công giữa các module (⇄)', remaining: finalRows, removed: prev - finalRows,
      detail: `${inN} dòng chuyển đến module này · ${outN} dòng chuyển đi` });
    prev = finalRows;
  }
  return { total, steps, final: finalRows };
}

const DK_KIND_LABEL = { main:'Chính', extra:'Phụ', priority:'Ưu tiên', custom:'Tự thêm', manual:'Thủ công' };

function renderDkLoc(){
  const c = document.getElementById('content');
  const subnav = renderPhanLoaiSubnav('dkloc');
  if(!(state.rows.length > 0 && state.headers.length > 0)){
    c.innerHTML = subnav + `
      <div class="placeholder">
        <div class="ph-ic">${icon('search','ic')}</div>
        <h2>Chưa có dữ liệu tồn kho</h2>
        <p>Vào mục <b style="color:var(--frost)">Load Data</b> để nạp file Excel tồn kho trước — các bước lọc sẽ hiện tại đây.</p>
        <div class="tag" id="btnGoLoadData" style="cursor:pointer;">ĐI TỚI LOAD DATA</div>
      </div>`;
    bindPhanLoaiSubnav();
    const goBtn = document.getElementById('btnGoLoadData');
    if(goBtn) goBtn.addEventListener('click', ()=>{ state.activeModule = 'loaddata'; renderAll(); });
    return;
  }
  const mods = MODULES.filter(m => FILTER_MODULES[m.id]);
  if(!FILTER_MODULES[state.dkLocMod]) state.dkLocMod = mods[0].id;
  const modId = state.dkLocMod;
  const mod = MODULES.find(m => m.id === modId);
  const st = state.filterModules[modId];
  const fn = dkLocFunnel(modId);
  const warns = dkLocWarnings(modId);

  // Hàng chip chọn module (kèm số dòng của từng module)
  const chips = mods.map(m=>{
    const n = filterModuleBaseRowsCached(m.id).length;
    const w = dkLocWarnings(m.id).length;
    return `<button type="button" class="dk-mod ${m.id===modId?'active':''}" data-dkmod="${m.id}" style="--tc:${m.color}">
      <span class="dk-dot"></span><span class="dk-mod-name">${escapeHtml(m.label)}</span>
      <span class="dk-mod-n">${n.toLocaleString('en-US')}</span>${w?`<span class="dk-mod-warn" title="${w} điều kiện không áp dụng được">⚠</span>`:''}
    </button>`;
  }).join('');

  const stepRows = fn.steps.map((sp, idx)=>{
    const status = sp.warn
      ? `<span class="dk-st dk-st-warn" title="Không thấy cột trong Load Data — điều kiện này đang bị bỏ qua">⚠ Không áp dụng</span>`
      : (sp.locked ? `<span class="dk-st dk-st-lock">${icon('lock','ic')} Cố định</span>` : (sp.kind==='manual' ? `<span class="dk-st">Theo thao tác</span>` : `<span class="dk-st dk-st-ok">${icon('check','ic')} Đang áp dụng</span>`));
    const del = sp.kind==='custom' && sp.cfId ? `<button type="button" class="custom-filter-del dk-del" data-cf-id="${sp.cfId}" title="Xóa điều kiện này">×</button>` : '';
    const rem = sp.removed;
    const remTxt = rem === 0 ? '<span class="dk-zero">0</span>' : (rem > 0 ? `−${rem.toLocaleString('en-US')}` : `+${(-rem).toLocaleString('en-US')}`);
    return `<tr class="dk-row dk-${sp.kind}">
      <td class="dk-n">${idx+1}</td>
      <td><span class="dk-kind dk-kind-${sp.kind}">${DK_KIND_LABEL[sp.kind]}${sp.num && fn.steps.filter(x=>x.kind==='extra').length>1 ? ' #'+sp.num : ''}</span></td>
      <td class="dk-cond">${escapeHtml(sp.label)}${sp.detail?`<div class="dk-sub">${escapeHtml(sp.detail)}</div>`:''}</td>
      <td>${status}</td>
      <td class="num">${sp.remaining.toLocaleString('en-US')}</td>
      <td class="num dk-rem ${rem<0?'dk-add':''}">${remTxt}</td>
      <td class="dk-act">${del}</td>
    </tr>`;
  }).join('');

  const dl = state.dkLast || {};
  const colOpts = state.headers.map((h,ci)=>`<option value="${escapeHtml(h||'')}" ${(h||'')===dl.col?'selected':''}>${escapeHtml(h||'(cột '+(ci+1)+')')}</option>`).join('');
  const opOpts = CUSTOM_FILTER_OPS.map(([v,l])=>`<option value="${v}" ${v===dl.op?'selected':''}>${l}</option>`).join('');

  c.innerHTML = subnav + `
  <div class="data-wrap show dk-wrap" id="dkLocWrap">
    <div class="diag-note">${icon('search')} Dữ liệu lấy trực tiếp từ file đã nạp ở <b>Load Data</b>: <b>${escapeHtml(state.fileName || 'dữ liệu đã lưu')}</b> (${state.rows.length.toLocaleString('en-US')} dòng). Điều kiện áp dụng lần lượt từ trên xuống; mỗi dòng cho biết số dòng còn lại và số dòng bị loại ở bước đó.</div>
    <div class="dk-mods">${chips}</div>

    <div class="dk-card">
      <div class="dk-head">
        <div class="dk-title"><span class="dk-dot" style="--tc:${mod.color}"></span>${escapeHtml(mod.label)}</div>
        <div class="dk-sum"><b>${fn.final.toLocaleString('en-US')}</b> / ${fn.total.toLocaleString('en-US')} dòng Load Data</div>
        <button type="button" class="btn btn-ghost" id="dkOpenMod" style="padding:5px 12px;font-size:12px;">Xem bảng dữ liệu ${icon('chevronR','ic')}</button>
      </div>
      ${warns.length ? `<div class="diag-note warn-note">${icon('file')} ${warns.map(escapeHtml).join(' · ')} — các điều kiện này đang bị bỏ qua. Kiểm tra tên cột trong file Excel.</div>` : ''}
      <div class="dk-tablewrap"><table class="dk-table">
        <thead><tr><th>#</th><th>Loại</th><th>Điều kiện</th><th>Trạng thái</th><th class="num">Còn lại</th><th class="num">Loại bỏ</th><th></th></tr></thead>
        <tbody>
          <tr class="dk-row dk-start"><td class="dk-n">0</td><td><span class="dk-kind">Gốc</span></td><td class="dk-cond">Toàn bộ dữ liệu Load Data</td><td></td><td class="num">${fn.total.toLocaleString('en-US')}</td><td class="num"></td><td></td></tr>
          ${stepRows}
          <tr class="dk-row dk-final"><td></td><td></td><td class="dk-cond"><b>Kết quả của module</b></td><td></td><td class="num"><b>${fn.final.toLocaleString('en-US')}</b></td><td class="num">−${(fn.total-fn.final).toLocaleString('en-US')}</td><td></td></tr>
        </tbody>
      </table></div>
      <div class="dk-hint">${icon('lock','ic')} Điều kiện <b>Chính / Phụ</b> đã cố định trong phần mềm (không sửa qua giao diện). <b>Điều kiện tự thêm</b> cộng dồn (VÀ) với chúng, không xóa được các điều kiện gốc.</div>
    </div>

    <div class="dk-card">
      <div class="dk-head"><div class="dk-title">Thêm điều kiện cho ${escapeHtml(mod.label)}</div></div>
      <div class="dk-form" id="dkForm">
        <label>Cột <select id="dkCfCol">${colOpts}</select></label>
        <label>Điều kiện <select id="dkCfOp">${opOpts}</select></label>
        <label id="dkPosBox" style="display:none;">Ký tự từ <input type="number" id="dkCfFrom" min="1" value="${dl.from||2}" style="width:58px;"> đến <input type="number" id="dkCfTo" min="1" value="${dl.to||4}" style="width:58px;"></label>
        <label id="dkValBox">Giá trị <input type="text" id="dkCfVal" placeholder="Giá trị…" style="min-width:200px;"></label>
        <button type="button" class="btn btn-primary" id="dkCfAdd" style="padding:6px 14px;font-size:12px;">Thêm</button>
      </div>
      <div class="dk-hint" id="dkOpHint"></div>
      <div class="dk-hint dk-preview" id="dkPreview"></div>
    </div>
  </div>`;
  bindPhanLoaiSubnav();
  bindDkLocEvents(modId);
}

const DK_OP_HINT = {
  in: 'Nhiều giá trị ngăn bằng dấu ; hoặc | — dòng khớp nếu ô bằng MỘT trong các giá trị (VD: ECUADOR; ẤN ĐỘ).',
  notin: 'Nhiều giá trị ngăn bằng dấu ; hoặc | — dòng bị loại nếu ô bằng một trong các giá trị.',
  pos_in: 'Lấy các ký tự từ vị trí … đến … (tính từ 1) của ô rồi so với danh sách (VD Mã hàng, vị trí 2–4, giá trị RHO; RHL).',
  pos_notin: 'Như trên nhưng LOẠI các dòng có đoạn ký tự đó (VD vị trí 2–4 không thuộc RBR; CBR).',
  startswith: 'Không phân biệt hoa thường / dấu / khoảng trắng.', endswith: 'Không phân biệt hoa thường / dấu / khoảng trắng.',
  contains: 'Không phân biệt hoa thường / dấu / khoảng trắng.', notcontains: 'Không phân biệt hoa thường / dấu / khoảng trắng.',
  gt: 'So sánh số (nhận 1.234,5 và 1,234.5).', gte: 'So sánh số (nhận 1.234,5 và 1,234.5).', lt: 'So sánh số.', lte: 'So sánh số.',
};

function bindDkLocEvents(modId){
  const st = state.filterModules[modId];
  document.querySelectorAll('.dk-mod').forEach(b=> b.addEventListener('click', ()=>{ state.dkLocMod = b.dataset.dkmod; renderDkLoc(); }));
  const open = document.getElementById('dkOpenMod');
  if(open) open.addEventListener('click', ()=>{ state.activeModule = modId; renderAll(); });

  const opSel = document.getElementById('dkCfOp');
  const colSel = document.getElementById('dkCfCol');
  const valIn = document.getElementById('dkCfVal');
  const fromIn = document.getElementById('dkCfFrom');
  const toIn = document.getElementById('dkCfTo');
  const posBox = document.getElementById('dkPosBox');
  const valBox = document.getElementById('dkValBox');
  const hint = document.getElementById('dkOpHint');
  const prev = document.getElementById('dkPreview');

  // Đọc form → đối tượng điều kiện (hoặc null + lý do lỗi)
  function readForm(){
    const op = opSel.value;
    const cf = { id: 'cf_'+Date.now()+'_'+Math.random().toString(36).slice(2,7), colLabel: colSel.value, op, value: valIn.value.trim() };
    if(!cf.colLabel) return { err: 'Chọn cột cần lọc' };
    if(customFilterOpNeedsValue(op) && !cf.value) return { err: 'Nhập giá trị cần so khớp' };
    if((op==='gt'||op==='lt'||op==='gte'||op==='lte') && isNaN(parseLooseNumber(cf.value))) return { err: 'Giá trị so sánh phải là số' };
    if(customFilterOpIsPos(op)){
      const f = parseInt(fromIn.value,10), t = parseInt(toIn.value,10);
      if(!(f >= 1) || !(t >= f)) return { err: 'Vị trí ký tự không hợp lệ (từ ≥ 1, đến ≥ từ)' };
      cf.from = f; cf.to = t;
      if(!customFilterList(cf.value).length) return { err: 'Nhập ít nhất 1 giá trị' };
    }
    if((op==='in'||op==='notin') && !customFilterList(cf.value).length) return { err: 'Nhập ít nhất 1 giá trị' };
    return { cf };
  }
  // Xem trước: điều kiện này sẽ làm module còn bao nhiêu dòng nếu thêm (không ghi gì)
  function refreshPreview(){
    const needVal = customFilterOpNeedsValue(opSel.value);
    valBox.style.display = needVal ? '' : 'none';
    posBox.style.display = customFilterOpIsPos(opSel.value) ? '' : 'none';
    hint.textContent = DK_OP_HINT[opSel.value] || '';
    const r = readForm();
    if(r.err){ prev.textContent = ''; return; }
    const ci = dkLocColIndex(r.cf.colLabel);
    if(ci === -1){ prev.textContent = ''; return; }
    const cur = filterModuleBaseRowsCached(modId);
    const kept = cur.filter(({row}) => testCustomFilter(row[ci], r.cf)).length;
    prev.innerHTML = `Xem trước: thêm điều kiện này thì module còn <b>${kept.toLocaleString('en-US')}</b> / ${cur.length.toLocaleString('en-US')} dòng (loại ${(cur.length-kept).toLocaleString('en-US')}).`;
  }
  [opSel, colSel].forEach(el => el.addEventListener('change', refreshPreview));
  [valIn, fromIn, toIn].forEach(el => el.addEventListener('input', debounce(refreshPreview, 250)));
  refreshPreview();

  const addBtn = document.getElementById('dkCfAdd');
  const doAdd = ()=>{
    const r = readForm();
    if(r.err){ toast(r.err, true); return; }
    state.dkLast = { col: r.cf.colLabel, op: r.cf.op, from: r.cf.from || 2, to: r.cf.to || 4 };
    st.customFilters.push(r.cf); // mảng dùng CHUNG với customFiltersStore[modId] — xem makeFilterModuleState()
    persistCustomFilters();
    _fmBaseRowsCache = {};
    st.page = 1;
    renderDkLoc();
  };
  if(addBtn) addBtn.addEventListener('click', doAdd);
  if(valIn) valIn.addEventListener('keydown', e=>{ if(e.key === 'Enter'){ e.preventDefault(); doAdd(); } });

  document.querySelectorAll('#dkLocWrap .dk-del').forEach(btn=>{
    btn.addEventListener('click', ()=>{
      if(m1DtDenied()) return; // PC 7.54: xóa điều kiện phân loại làm đổi số liệu → chỉ Quản lý trở lên
      // Xóa TRỰC TIẾP trên mảng dùng chung (splice) — không gán lại tham chiếu mới (xem chú thích ở makeFilterModuleState)
      const idx = st.customFilters.findIndex(cf => cf.id === btn.dataset.cfId);
      if(idx !== -1) st.customFilters.splice(idx, 1);
      persistCustomFilters();
      _fmBaseRowsCache = {};
      st.page = 1;
      renderDkLoc();
    });
  });
}

// Tính toán TOÀN BỘ dữ liệu cần để vẽ 1 module lọc (cột hiển thị, dòng đã lọc/sắp xếp, trạng
// thái TPC, phân trang...) — TÁCH RIÊNG khỏi renderFilterModule() để renderFilterModuleRows()
// (vẽ lại NHẸ, chỉ phần dòng dữ liệu) có thể dùng lại y hệt logic này, không phải chép lại.
function computeFilterModuleView(modId){
  const st = state.filterModules[modId];
  const fmCfg = FILTER_MODULES[modId];
  const hasPrimaryMatch = !!fmCfg.primaryMatch;
  const hasCustomBase = hasPrimaryMatch || !!fmCfg.excludeAllOtherModules;
  const filterCi = hasCustomBase ? null : resolveFilterCol(modId);
  const filterVal = (!hasCustomBase && filterCi!==null) ? resolveFilterVal(modId, filterCi) : '';
  const filterValues = (!hasCustomBase && filterCi!==null) ? distinctColumnValues(filterCi) : [];
  const isLocked = !!fmCfg.locked;

  const cols = getFilterModuleColumns(modId);
  const weightBasis = getFilterModuleWeightBasis(modId);
  const displayCols = cols.map(c => ({ ...c, virtual: false }));
  if(weightBasis){
    const insertAt = displayCols.findIndex(c => c.label === 'Trọng lượng') + 1;
    displayCols.splice(insertAt, 0, { label: 'Tổng KL (kg)', virtual: true, basis: weightBasis });
  }
  // Cột "Trạng thái" — CHỈ riêng tab "Thành Phẩm Chính" (tpc): cảnh báo tồn kho gộp theo
  // đúng cặp (Mã hàng + Hợp đồng) — 1 mã hàng có thể có nhiều dòng (khác Size) nên phải CỘNG
  // TỔNG "Tồn cuối" của mọi dòng cùng cặp Mã hàng+Hợp đồng lại trước khi xét ngưỡng, không
  // xét riêng từng dòng (dễ báo sai nếu 1 dòng size nào đó thấp nhưng dòng khác vẫn còn nhiều).
  // Ngưỡng: <=0 = Hết hàng (đỏ), <8 = Sắp hết (vàng), còn lại = Còn hàng (xanh).
  const tpcStatusByGroup = modId === 'tpc' ? getTpcStatusInfo() : null;
  if(tpcStatusByGroup) displayCols.push({ label: 'Trạng thái', statusBadge: true });
  let fr = filterModuleFilteredRows(modId);
  if(st.showManualOnly){
    fr = fr.filter(({i}) => state.manualAssign[i] === modId);
  }

  // Phân trang: dữ liệu Load Data thường 4-5000 dòng, module lọc có thể giữ lại phần lớn số
  // đó — vẽ HẾT vào DOM cùng lúc gây giật/lag nặng (đo thực tế nhiều giây với ~3000 dòng). Chỉ
  // vẽ TỐI ĐA FILTER_PAGE_SIZE (50) dòng/trang; TỔNG/tfoot vẫn tính trên fr ĐẦY ĐỦ (KHÔNG theo
  // trang) — dùng lại đúng st.page vốn đã có sẵn trong makeFilterModuleState() nhưng trước đây
  // CHƯA từng được dùng để giới hạn số dòng vẽ.
  const fmTotalPages = Math.max(1, Math.ceil(fr.length / FILTER_PAGE_SIZE));
  if(st.page > fmTotalPages) st.page = fmTotalPages;
  if(st.page < 1) st.page = 1;
  const fmPageStart = (st.page - 1) * FILTER_PAGE_SIZE;
  const frPage = fr.slice(fmPageStart, fmPageStart + FILTER_PAGE_SIZE);

  // LỖI ĐÃ SỬA (2026-09): trước đây object trả về THIẾU "st" (dù đã tính ở dòng đầu hàm) —
  // renderFilterModuleRows() destructure {st,...} = computeFilterModuleView(modId) nên luôn
  // nhận st=undefined. Phần lớn trường hợp không lộ ra vì rơi vào nhánh "vẽ lại toàn trang" (an
  // toàn), NHƯNG đúng lúc số dòng kết quả không đổi trạng thái phân trang (has/không có khung
  // phân trang) — ví dụ chọn lọc ra 0 dòng rồi chọn tiếp ra 8 dòng, cả 2 đều CHƯA cần phân trang
  // — code đi vào nhánh "chỉ vẽ lại <tbody>" và crash ngay khi đọc "st.rowHeights"/
  // "st.showManualOnly", khiến số dòng + bảng bị ĐỨNG LẠI ở kết quả lần lọc TRƯỚC ĐÓ (trông y hệt
  // "chọn giá trị lọc ra 0 dòng" dù giá trị đó thực ra có dữ liệu) — đây chính là nguyên nhân của
  // lỗi "chọn 1 giá trị lọc ra 0 dòng, chọn thêm giá trị khác thì cả 2 lại cùng hiện ra" đã báo.
  return { st, fmCfg, hasPrimaryMatch, hasCustomBase, filterCi, filterVal, filterValues, isLocked,
    cols, displayCols, tpcStatusByGroup, fr, frPage, fmTotalPages, fmPageStart };
}

function renderFilterModule(modId){
  const mod = MODULES.find(m=>m.id===modId);
  const st = state.filterModules[modId];
  const c = document.getElementById('content');
  const hasData = state.rows.length > 0 && state.headers.length > 0;

  // Lưu lại vị trí cuộn của bảng TRƯỚC khi vẽ lại — tránh bị nhảy về đầu bảng mỗi khi tick
  // checkbox / đổi lựa chọn (vốn dĩ gọi lại renderFilterModule để cập nhật UI, nhưng thay
  // c.innerHTML luôn tạo phần tử .table-scroll MỚI nên mất vị trí cuộn cũ nếu không khôi phục).
  const prevScrollEl = document.querySelector(`#${modId}Wrap .table-scroll`);
  const prevScrollTop = prevScrollEl ? prevScrollEl.scrollTop : 0;
  const prevScrollLeft = prevScrollEl ? prevScrollEl.scrollLeft : 0;

  // Danh sách module con của nhóm "Phân Loại" hiện ở 1 dòng menu ngang RIÊNG ngay trên bảng
  // dữ liệu (thay cho dropdown ở thanh menu chính) — bấm vào chuyển bảng nhanh, không phải mở
  // lại thanh menu chính mỗi lần.
  const subnavHtml = mod.group==='phanloai' ? renderPhanLoaiSubnav(modId) : '';

  if(!hasData){
    c.innerHTML = subnavHtml + `
      <div class="placeholder">
        <div class="ph-ic">${icon(mod.icon,'ic')}</div>
        <h2>Chưa có dữ liệu tồn kho</h2>
        <p>Vào mục <b style="color:var(--frost)">Load Data</b> để nạp file Excel tồn kho trước — dữ liệu sẽ tự động hiển thị tại đây, không cần nạp lại.</p>
        <div class="tag" id="btnGoLoadData" style="cursor:pointer;">ĐI TỚI LOAD DATA</div>
      </div>
    `;
    if(mod.group==='phanloai') bindPhanLoaiSubnav();
    const goBtn = document.getElementById('btnGoLoadData');
    if(goBtn) goBtn.addEventListener('click', ()=>{
      state.activeModule = 'loaddata';
      renderAll();
    });
    return;
  }

  const {
    fmCfg, hasPrimaryMatch, hasCustomBase, filterCi, filterVal, filterValues, isLocked,
    cols, displayCols, tpcStatusByGroup, fr, frPage, fmTotalPages, fmPageStart
  } = computeFilterModuleView(modId);

  let html = `<div class="data-wrap show" id="${modId}Wrap">`;

  // PC 1.24: các ghi chú điều kiện lọc (nguồn dữ liệu, điều kiện chính / phụ, điều kiện tự thêm + nút
  // "+ Thêm điều kiện") đã chuyển sang TAB RIÊNG "Điều kiện lọc" (renderDkLoc) — ở đây chỉ còn 1 nút
  // nhảy sang tab đó (kèm số điều kiện, ⚠ nếu có điều kiện không áp dụng được) + cảnh báo thiếu cột.
  const dkCount = dkLocCountConditions(modId);
  const dkWarn = dkLocWarnings(modId).length;

  html += `<div class="filter-line">`;
  html += `<button type="button" class="dk-goto" id="${modId}BtnGoDk" title="Xem từng bước lọc, số dòng bị loại, thêm / xóa điều kiện tự thêm">
    ${icon('search','ic')}<span>Điều kiện lọc (${dkCount})</span>${dkWarn?`<span class="dk-goto-warn" title="${dkWarn} điều kiện không áp dụng được">⚠ ${dkWarn}</span>`:''}${icon('chevronR','ic')}
  </button>`;

  // Bộ chọn tiêu chí lọc: cột + giá trị — chỉ hiện khi module KHÔNG dùng điều kiện chính cố định
  // (primaryMatch/excludeAllOtherModules), vì các trường hợp đó tiêu chí không chọn qua dropdown được.
  if(!hasCustomBase){
    html += `<div class="filter-row">
      <label for="${modId}FilterColSelect">Lọc theo cột:</label>
      <select id="${modId}FilterColSelect" ${isLocked?'disabled':''}>
        ${state.headers.map((h,ci)=>`<option value="${ci}" ${ci===filterCi?'selected':''}>${escapeHtml(h||'(cột '+(ci+1)+')')}</option>`).join('')}
      </select>
      <label for="${modId}FilterValSelect">Giá trị:</label>
      <select id="${modId}FilterValSelect" ${(filterValues.length===0||isLocked)?'disabled':''}>
        ${filterValues.length===0
          ? `<option>(không có dữ liệu)</option>`
          : filterValues.map(v=>`<option value="${escapeHtml(v)}" ${v===filterVal?'selected':''}>${escapeHtml(v)}</option>`).join('')}
      </select>
      ${isLocked ? `<span class="lock-badge" title="Bộ lọc đã khóa">${icon('lock')} Đã khóa</span>` : ''}
    </div>`;
  } else if(isLocked){
    html += `<div class="filter-row"><span class="lock-badge" title="Điều kiện đã khóa">${icon('lock')} Đã khóa</span></div>`;
  }
  html += `</div>`; // đóng .filter-line

  if(cols.length === 0){
    html += `<div class="diag-note warn-note">${icon('file')} Không tìm thấy cột phù hợp trong dữ liệu đã nạp ở Load Data. Kiểm tra lại tên cột trong file Excel (Mã hàng, Tên hàng, Tồn cuối…).</div>`;
  }

  const manualCountHere = countManualInModule(modId);
  const otherFilterModules = MODULES.filter(m => FILTER_MODULES[m.id] && m.id !== modId);
  const selCount = st.selected.size;

  // PC 1.52: thanh "Ngưỡng Sắp hết / số dòng / Xuất Excel" GỘP vào CHUNG HÀNG với thanh chuyển mã hàng bên dưới (trước đây là 1 hàng riêng) → bảng hiển thị được nhiều dòng hơn.
  // PC 3.91: tổng tham khảo (Mã hàng · Tồn cuối · Trọng lượng) gắn vào thanh công cụ của bảng, thay cho chip trên thanh menu
  const _fmN = (n,d)=>n.toLocaleString('en-US',d?{minimumFractionDigits:2,maximumFractionDigits:2}:undefined);
  const _fmMa = countDistinctFilterModuleColumn(modId, 'Mã hàng');
  const _fmTc = sumFilterModuleColumn(modId, 'Tồn cuối');
  const _fmKg = sumFilterModuleTotalWeight(modId);
  const fmSumHtml = `<span class="fm-sum" title="Tổng của module (chưa tính ô tìm / lọc cột; tổng theo bảng đang lọc ở dòng Tổng cuối bảng)">`
    + (_fmMa!==null?`<span><b>${_fmN(_fmMa)}</b> mã hàng</span>`:'')
    + (_fmTc!==null?`<span><b>${_fmN(_fmTc)}</b> tồn cuối</span>`:'')
    + (_fmKg!==null?`<span><b>${_fmN(_fmKg,1)}</b> kg</span>`:'')
    + `</span>`;

  const tbRightHtml = `<div class="toolbar-right" style="margin-left:auto;">
      ${modId==='tpc' ? `<span class="tpc-threshold-box" title="Số Tồn cuối dùng làm mốc phân loại 'Sắp hết' (vàng) ở cột Trạng thái — lớn hơn 0 và nhỏ hơn hoặc bằng số này sẽ bị đánh dấu Sắp hết.">
        <label for="tpcThresholdInput" style="font-size:12px;color:var(--text-muted);white-space:nowrap;">Ngưỡng Sắp hết ≤</label>
        <input type="number" id="tpcThresholdInput" min="0" step="1" value="${escapeHtml(String(state.tpcLowStockThreshold))}" style="width:64px;">
      </span>` : ''}
      ${fmSumHtml}
      <span id="${modId}RowCountInfo" style="font-family:var(--mono);font-size:12px;color:var(--text-muted);">
        ${fr.length.toLocaleString('en-US')} dòng
      </span>
      ${manualCountHere>0 ? `<span class="manual-note-badge clickable ${st.showManualOnly?'active':''}" id="${modId}BtnManualOnly" title="Bấm để ${st.showManualOnly?'bỏ lọc, xem lại toàn bộ':'chỉ hiện các dòng đã chuyển thủ công vào module này'}">⇄ ${manualCountHere} chuyển thủ công${st.showManualOnly?' · đang lọc':''}</span>
        ${m1CanWrite ? `<button class="btn btn-ghost" id="${modId}BtnRevertAll" style="padding:5px 10px;font-size:11px;">Trả về tự động toàn bộ</button>` : ''}` : ''}
      <button class="btn btn-ghost" id="${modId}BtnExport" ${fr.length===0?'disabled':''}>${icon('download')} Xuất Excel</button>
    </div>`;

  // fmTotalPages/frPage/fmPageStart đã tính sẵn trong computeFilterModuleView(modId) ở đầu hàm
  // (dùng chung với renderFilterModuleRows() bên dưới — xem chú thích ở đó).

  // Thanh chuyển mã hàng qua lại giữa 7 module: chọn 1 hoặc nhiều dòng (checkbox đầu bảng),
  // chọn module đích, bấm Chuyển — dòng sẽ biến mất khỏi module hiện tại và chuyển hẳn sang
  // module đích (ghi đè điều kiện lọc tự động), áp dụng cho mọi bảng dữ liệu của 7 module.
  if(!m1CanWrite){
    // PC 7.54: người dùng cấp 1 / 2 — khóa chuyển mã hàng giữa các module (chỉ còn dòng thông báo)
    html += `<div class="move-bar"><span class="move-count" style="opacity:.75;">🔒 Chỉ vai trò Quản lý trở lên được chuyển mã hàng giữa các module</span>${tbRightHtml}</div>`;
  } else html += `<div class="move-bar">
    <span class="move-count">${selCount>0 ? `${selCount.toLocaleString('en-US')} mã hàng đã chọn` : 'Tick chọn dòng để chuyển mã hàng sang module khác'}</span>
    <select id="${modId}MoveTarget" ${selCount===0?'disabled':''}>
      ${otherFilterModules.map(m=>`<option value="${m.id}">${escapeHtml(m.label)}</option>`).join('')}
    </select>
    <button class="btn btn-primary" id="${modId}BtnMove" ${selCount===0?'disabled':''}>⇄ Chuyển đến module đã chọn</button>
    <button class="btn btn-ghost" id="${modId}BtnClearSel" ${selCount===0?'disabled':''}>Bỏ chọn</button>
    ${tbRightHtml}
  </div>`;

  html += `<div class="table-shell"><div class="table-scroll"><table class="resizable-table"><colgroup>` +
    `<col style="width:38px">` +
    displayCols.map(col => `<col data-col-label="${col.label.replace(/"/g,'&quot;')}" style="width:${filterModuleColWidth(modId,col)}px">`).join('') +
    `</colgroup><thead><tr>` +
    `<th class="chk-col"><input type="checkbox" id="${modId}ChkAll"></th>` +
    displayCols.map((col, i) => {
      // Tay kéo cột "leading": gắn vào <th> KẾ TIẾP (mép trái) — xem chú thích đầy đủ ở
      // js/table-resize.js (hàm addColResizers): <th> dùng position:sticky nên <th> đứng sau
      // luôn đè lên đúng vị trí tay kéo mép phải của <th> đứng trước, chuột không bấm trúng
      // được. Cột CUỐI không có <th> kế tiếp nên vẫn giữ tay kéo mép phải của chính nó.
      const isLast = i === displayCols.length - 1;
      const prevCol = i > 0 ? displayCols[i-1] : null;
      const leadingResizer = prevCol
        ? `<span class="col-resizer col-resizer--leading" data-resize-label="${prevCol.label.replace(/"/g,'&quot;')}" title="Kéo để đổi độ rộng · nhấp đúp để tự căn theo dữ liệu"></span>`
        : '';
      const trailingResizer = isLast
        ? `<span class="col-resizer" data-resize-label="${col.label.replace(/"/g,'&quot;')}" title="Kéo để đổi độ rộng · nhấp đúp để tự căn theo dữ liệu"></span>`
        : '';
      if(col.virtual){
        const hasFilter = colFilterActive(st.colFilters.__tongKL);
        return `<th class="num ${hasFilter?'has-filter':''}" title="Tính = Tồn cuối × Trọng lượng đơn vị">
          ${leadingResizer}
          <div class="th-row"><span class="th-label" style="cursor:default;">${col.label}</span></div>
          ${trailingResizer}
        </th>`;
      }
      if(col.statusBadge){
        const hasFilter = colFilterActive(st.colFilters.__tpcStatus);
        const thTxt = String(state.tpcLowStockThreshold);
        return `<th class="${hasFilter?'has-filter':''}" title="Cảnh báo tồn kho gộp theo Mã hàng + Hợp đồng: Hết hàng (≤0) · Sắp hết (&gt;0 và ≤${escapeHtml(thTxt)}) · Còn hàng (&gt;${escapeHtml(thTxt)})">
          ${leadingResizer}
          <div class="th-row">
            <span class="th-label" style="cursor:default;">${col.label}</span>
          </div>
          ${trailingResizer}
        </th>`;
      }
      const active = st.sortCol === col.ci;
      const hasFilter = colFilterActive(st.colFilters[col.ci]);
      return `<th class="${isNumericCol(col.label)?'num':''} sortable ${active?'sorted':''} ${hasFilter?'has-filter':''}" data-col="${col.ci}">
        ${leadingResizer}
        <div class="th-row">
          <span class="th-label" data-sort-col="${col.ci}">${col.label}</span>
        </div>
        ${trailingResizer}
      </th>`;
    }).join('') +
    `</tr><tr class="col-search-row">` +
    `<th class="chk-col"></th>` +
    displayCols.map(col => {
      if(col.virtual){
        const hasFilter = colFilterActive(st.colFilters.__tongKL);
        return `<th><div class="col-search-cell"><input type="text" class="fmColSearchInput" data-col-index="__tongKL"
            placeholder="Tìm ${escapeHtml(col.label.toLowerCase())}…" value="${escapeHtml(st.colSearch.__tongKL||'')}"><button type="button" class="th-filter-btn ${hasFilter?'active':''}" data-filter-col="__tongKL" title="Lọc theo ${col.label}">${icon('search')}</button></div></th>`;
      }
      const key = col.statusBadge ? '__tpcStatus' : col.ci;
      const hasFilter = colFilterActive(st.colFilters[key]);
      const filterTitle = col.statusBadge ? '__tpcStatus' : col.ci;
      return `<th><div class="col-search-cell"><input type="text" class="fmColSearchInput" data-col-index="${key}"
          placeholder="Tìm ${escapeHtml(col.label.toLowerCase())}…" value="${escapeHtml(st.colSearch[key]||'')}"><button type="button" class="th-filter-btn ${hasFilter?'active':''}" data-filter-col="${filterTitle}" title="Lọc theo ${col.label}">${icon('search')}</button></div></th>`;
    }).join('') +
    `</tr></thead><tbody>`;

  if(cols.length === 0 || fr.length === 0){
    const emptyMsg = cols.length===0
      ? 'Không có cột nào để hiển thị.'
      : (st.showManualOnly ? 'Không có dòng nào khớp tìm kiếm/lọc trong số các dòng đã chuyển thủ công.' : 'Không tìm thấy dòng nào khớp với tiêu chí lọc / từ khóa tìm kiếm.');
    html += `<tr><td colspan="${Math.max(displayCols.length+1,2)}"><div class="empty-state">${emptyMsg}</div></td></tr>`;
  } else {
    frPage.forEach(({i, row})=>{
      const isManual = state.manualAssign[i] !== undefined;
      const rowH = (st.rowHeights||{})[i];
      html += `<tr${rowH?` style="height:${rowH}px"`:''}>` +
        `<td class="chk-col"><input type="checkbox" class="fmRowChk" data-idx="${i}" ${st.selected.has(i)?'checked':''}></td>` +
        displayCols.map((col, colIdx)=>{
          if(col.virtual){
            const v = filterModuleRowTotalWeight(row, col.basis);
            return `<td class="num">${v===null?'<span class="cell-empty">—</span>':v.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2})}</td>`;
          }
          if(col.statusBadge){
            const status = getTpcRowStatus(tpcStatusByGroup, row);
            let badge;
            if(status === 'Hết hàng') badge = `<span class="stock-badge out">Hết hàng</span>`;
            else if(status === 'Sắp hết') badge = `<span class="stock-badge low">Sắp hết</span>`;
            else badge = `<span class="stock-badge ok">Còn hàng</span>`;
            return `<td>${badge}</td>`;
          }
          const val = row[col.ci] ?? '';
          const displayVal = applySizePrefix(modId, col.label, val);
          const cls = isNumericCol(col.label) ? 'num' : '';
          const badge = (colIdx===0 && isManual)
            ? `<span class="manual-item-badge" data-revert-idx="${i}" title="${m1CanWrite ? 'Đã chuyển thủ công vào module này — bấm để trả về tự động' : 'Đã chuyển thủ công vào module này'}">⇄</span>`
            : '';
          return `<td class="${cls}">${badge}${val===''?'<span class="cell-empty">—</span>':escapeHtml(displayVal)}</td>`;
        }).join('') + '</tr>';
    });
  }
  html += `</tbody>`;

  if(cols.length > 0 && fr.length > 0){
    const totals = filterModuleComputeTotals(fr, displayCols);
    let labelPlaced = false;
    html += `<tfoot><tr><td class="chk-col"></td>` + displayCols.map((col, idx)=>{
      const t = totals[idx];
      if(t === undefined){
        if(!labelPlaced){ labelPlaced = true; return `<td class="total-label">Tổng (${fr.length.toLocaleString('en-US')} dòng)</td>`; }
        return `<td></td>`;
      }
      return `<td class="num">${t===null?'<span class="cell-empty">—</span>':t.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2})}</td>`;
    }).join('') + `</tr></tfoot>`;
  }

  html += `</table></div>`;

  { // PC 7.38: LUÔN có khung phân trang (ẩn khi ít dòng) — xem chú thích ở renderLoadData
    html += `<div class="pagination" id="${modId}PaginationBox"${(fr.length > 10 || FILTER_PAGE_SIZE < fr.length) ? '' : ' style="display:none"'}>
      <button type="button" class="btn btn-ghost btn-sm" id="${modId}PagePrev" ${st.page<=1?'disabled':''}>${icon('file')} Trước</button>
      <span class="pagination-info">Trang ${st.page.toLocaleString('en-US')} / ${fmTotalPages.toLocaleString('en-US')}</span>
      <button type="button" class="btn btn-ghost btn-sm" id="${modId}PageNext" ${st.page>=fmTotalPages?'disabled':''}>Sau ${icon('file')}</button>
      <select id="${modId}PageSizeSelect" class="btn btn-ghost btn-sm" style="cursor:pointer;">
        <option value="10" ${FILTER_PAGE_SIZE===10?'selected':''}>10 dòng/trang</option>
        <option value="15" ${FILTER_PAGE_SIZE===15?'selected':''}>15 dòng/trang</option>
        <option value="20" ${FILTER_PAGE_SIZE===20?'selected':''}>20 dòng/trang</option>
        <option value="25" ${FILTER_PAGE_SIZE===25?'selected':''}>25 dòng/trang</option>
      </select>
    </div>`;
  }

  html += `</div>`; // table-shell
  html += `</div>`; // data-wrap

  c.innerHTML = subnavHtml + html;
  if(prevScrollEl){ const nw = document.getElementById(`${modId}Wrap`); if(nw) nw.classList.add('no-anim'); } // PC 7.38: vẽ lại cùng trang (tick ô, đổi trang…) không phát lại hiệu ứng trượt vào → hết nháy
  if(mod.group==='phanloai') bindPhanLoaiSubnav();
  const newScrollEl = document.querySelector(`#${modId}Wrap .table-scroll`);
  if(newScrollEl && (prevScrollTop || prevScrollLeft)){
    newScrollEl.scrollTop = prevScrollTop;
    newScrollEl.scrollLeft = prevScrollLeft;
  }
  bindFilterModuleEvents(modId);
  fillResizableTableWidth(document.querySelector(`#${modId}Wrap table.resizable-table`));
}

/* Tương tự renderLoadDataRows() — chỉ vẽ lại <tbody>/<tfoot> + số dòng + khung phân trang của
   1 module lọc, KHÔNG đụng tới toolbar/tiêu đề cột (ô .fmColSearchInput, #${modId}SearchInput
   không bao giờ bị hủy/tạo lại), dùng khi người dùng đang GÕ vào ô tìm kiếm của module lọc —
   tránh ngắt quãng gõ tiếng Việt có dấu, xem chú thích đầy đủ ở renderLoadDataRows(). */
function renderFilterModuleRows(modId){
  const wrap = document.getElementById(`${modId}Wrap`);
  const table = wrap && wrap.querySelector('table.resizable-table');
  const tbody = table && table.querySelector('tbody');
  if(!tbody){ renderFilterModule(modId); return; }

  const { st, displayCols, tpcStatusByGroup, fr, frPage, fmTotalPages } = computeFilterModuleView(modId);

  const needsPagination = fr.length > 10 || FILTER_PAGE_SIZE < fr.length;
  const paginationBox = document.getElementById(`${modId}PaginationBox`);
  if(paginationBox) paginationBox.style.display = needsPagination ? '' : 'none'; // PC 7.38: chỉ ẩn / hiện, không vẽ lại cả trang

  if(fr.length === 0){
    const emptyMsg = st.showManualOnly ? 'Không có dòng nào khớp tìm kiếm/lọc trong số các dòng đã chuyển thủ công.' : 'Không tìm thấy dòng nào khớp với tiêu chí lọc / từ khóa tìm kiếm.';
    tbody.innerHTML = `<tr><td colspan="${Math.max(displayCols.length+1,2)}"><div class="empty-state">${emptyMsg}</div></td></tr>`;
  } else {
    tbody.innerHTML = frPage.map(({i, row})=>{
      const isManual = state.manualAssign[i] !== undefined;
      const rowH = (st.rowHeights||{})[i];
      return `<tr${rowH?` style="height:${rowH}px"`:''}>` +
        `<td class="chk-col"><input type="checkbox" class="fmRowChk" data-idx="${i}" ${st.selected.has(i)?'checked':''}></td>` +
        displayCols.map((col, colIdx)=>{
          if(col.virtual){
            const v = filterModuleRowTotalWeight(row, col.basis);
            return `<td class="num">${v===null?'<span class="cell-empty">—</span>':v.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2})}</td>`;
          }
          if(col.statusBadge){
            const status = getTpcRowStatus(tpcStatusByGroup, row);
            let badge;
            if(status === 'Hết hàng') badge = `<span class="stock-badge out">Hết hàng</span>`;
            else if(status === 'Sắp hết') badge = `<span class="stock-badge low">Sắp hết</span>`;
            else badge = `<span class="stock-badge ok">Còn hàng</span>`;
            return `<td>${badge}</td>`;
          }
          const val = row[col.ci] ?? '';
          const displayVal = applySizePrefix(modId, col.label, val);
          const cls = isNumericCol(col.label) ? 'num' : '';
          const badge = (colIdx===0 && isManual)
            ? `<span class="manual-item-badge" data-revert-idx="${i}" title="${m1CanWrite ? 'Đã chuyển thủ công vào module này — bấm để trả về tự động' : 'Đã chuyển thủ công vào module này'}">⇄</span>`
            : '';
          return `<td class="${cls}">${badge}${val===''?'<span class="cell-empty">—</span>':escapeHtml(displayVal)}</td>`;
        }).join('') + '</tr>';
    }).join('');
  }

  const tfoot = table.querySelector('tfoot');
  if(tfoot){
    if(fr.length > 0){
      const totals = filterModuleComputeTotals(fr, displayCols);
      let labelPlaced = false;
      tfoot.innerHTML = `<tr><td class="chk-col"></td>` + displayCols.map((col, idx)=>{
        const t = totals[idx];
        if(t === undefined){
          if(!labelPlaced){ labelPlaced = true; return `<td class="total-label">Tổng (${fr.length.toLocaleString('en-US')} dòng)</td>`; }
          return `<td></td>`;
        }
        return `<td class="num">${t===null?'<span class="cell-empty">—</span>':t.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2})}</td>`;
      }).join('') + `</tr>`;
    } else {
      tfoot.innerHTML = '';
    }
  }

  const rowCountEl = document.getElementById(`${modId}RowCountInfo`);
  if(rowCountEl) rowCountEl.textContent = `${fr.length.toLocaleString('en-US')} dòng`;
  if(paginationBox){
    const prevBtn = document.getElementById(`${modId}PagePrev`);
    const nextBtn = document.getElementById(`${modId}PageNext`);
    const infoSpan = paginationBox.querySelector('.pagination-info');
    if(prevBtn) prevBtn.disabled = st.page<=1;
    if(nextBtn) nextBtn.disabled = st.page>=fmTotalPages;
    if(infoSpan) infoSpan.textContent = `Trang ${st.page.toLocaleString('en-US')} / ${fmTotalPages.toLocaleString('en-US')}`;
  }

  // Gắn lại sự kiện cho checkbox chọn dòng vừa tạo lại (tbody mới) — KHÔNG gọi lại toàn bộ
  // bindFilterModuleEvents(modId) (sẽ gắn lại luôn cả sự kiện của toolbar/tiêu đề, không cần).
  const rowChks = wrap.querySelectorAll('.fmRowChk');
  const chkAll = document.getElementById(`${modId}ChkAll`);
  function syncFmChkAll(){
    if(!chkAll) return;
    const ids = Array.from(rowChks).map(el => Number(el.dataset.idx));
    const st2 = state.filterModules[modId];
    const allSel = ids.length>0 && ids.every(i=>st2.selected.has(i));
    const someSel = ids.some(i=>st2.selected.has(i));
    chkAll.checked = allSel;
    chkAll.indeterminate = !allSel && someSel;
  }
  syncFmChkAll();
  rowChks.forEach(chk=>{
    chk.addEventListener('change', ()=>{
      const idx = Number(chk.dataset.idx);
      const st2 = state.filterModules[modId];
      if(chk.checked) st2.selected.add(idx); else st2.selected.delete(idx);
      renderFilterModule(modId);
    });
  });

  initExcelLikeSelection(table);
}

function bindFilterModuleEvents(modId){
  const st = state.filterModules[modId];

  const fmPagePrev = document.getElementById(`${modId}PagePrev`);
  const fmPageNext = document.getElementById(`${modId}PageNext`);
  if(fmPagePrev) fmPagePrev.addEventListener('click', ()=>{ st.page = Math.max(1, st.page-1); renderFilterModule(modId); });
  if(fmPageNext) fmPageNext.addEventListener('click', ()=>{ st.page = st.page+1; renderFilterModule(modId); });
  const fmPageSizeSelect = document.getElementById(`${modId}PageSizeSelect`);
  if(fmPageSizeSelect) fmPageSizeSelect.addEventListener('change', ()=>{
    FILTER_PAGE_SIZE = parseInt(fmPageSizeSelect.value, 10);
    st.page = 1;
    renderFilterModule(modId);
  });

  // PC 1.24: nút nhảy sang tab "Điều kiện lọc" (mở sẵn điều kiện của module này)
  const btnGoDk = document.getElementById(`${modId}BtnGoDk`);
  if(btnGoDk) btnGoDk.addEventListener('click', ()=>{ state.dkLocMod = modId; state.activeModule = 'dkloc'; renderAll(); });

  const colSelect = document.getElementById(`${modId}FilterColSelect`);
  if(colSelect){
    colSelect.addEventListener('change', e=>{
      st.filterCol = Number(e.target.value);
      st.filterVal = ''; // để tự chọn lại giá trị mặc định cho cột mới
      st.page = 1;
      renderTopbar();
      renderFilterModule(modId);
    });
  }
  const valSelect = document.getElementById(`${modId}FilterValSelect`);
  if(valSelect){
    valSelect.addEventListener('change', e=>{
      st.filterVal = e.target.value;
      st.page = 1;
      renderTopbar();
      renderFilterModule(modId);
    });
  }

  // (PC 1.24: form + chip điều kiện tự thêm chuyển sang tab "Điều kiện lọc" — xem bindDkLocEvents)

  // Ô nhập tay ngưỡng "Sắp hết" (chỉ tab Thành Phẩm Chính) — đổi ngưỡng thì chỉ cần vẽ lại
  // module này (getTpcRowStatus đọc trực tiếp state.tpcLowStockThreshold mỗi lần gọi, KHÔNG
  // nằm trong _tpcStatusCache — cache đó chỉ lưu TỔNG Tồn cuối theo nhóm, độc lập với ngưỡng).
  if(modId === 'tpc'){
    const thresholdInput = document.getElementById('tpcThresholdInput');
    if(thresholdInput){
      const applyThreshold = debounce(()=>{
        const input = document.getElementById('tpcThresholdInput');
        const n = input ? Number(input.value) : NaN;
        state.tpcLowStockThreshold = (Number.isFinite(n) && n >= 0) ? n : TPC_THRESHOLD_DEFAULT;
        saveTpcThreshold(state.tpcLowStockThreshold);
        renderFilterModule(modId);
        const el = document.getElementById('tpcThresholdInput');
        if(el){ el.focus(); }
      }, SEARCH_DEBOUNCE_MS);
      thresholdInput.addEventListener('input', applyThreshold);
    }
  }

  const btnExport = document.getElementById(`${modId}BtnExport`);
  if(btnExport) btnExport.addEventListener('click', ()=>exportFilterModuleToExcel(modId));

  const searchInput = document.getElementById(`${modId}SearchInput`);
  if(searchInput){
    const doSearchRender = debounce(()=>{
      renderFilterModuleRows(modId);
      // renderFilterModuleRows() không đụng tới ô tìm kiếm nên không cần khôi phục focus nữa —
      // chỉ còn hữu ích nếu nó phải rơi về renderFilterModule() đầy đủ (trường hợp hiếm).
      const val = document.getElementById(`${modId}SearchInput`);
      if(val && val !== searchInput){ val.focus(); val.selectionStart = val.selectionEnd = val.value.length; }
    }, SEARCH_DEBOUNCE_MS);
    searchInput.addEventListener('input', e=>{
      st.search = e.target.value;
      st.page = 1;
      doSearchRender();
    });
  }

  // Sắp xếp: chỉ bắt sự kiện trên phần nhãn cột, tránh trùng với nút kính lúp
  document.querySelectorAll(`#${modId}Wrap .th-label`).forEach(label=>{
    label.addEventListener('click', ()=>{
      if(label.dataset.sortCol === undefined) return; // cột tính toán (Tổng KL) không hỗ trợ sắp xếp
      const ci = Number(label.dataset.sortCol);
      if(st.sortCol === ci){
        st.sortDir = st.sortDir === 'asc' ? 'desc' : 'asc';
      } else {
        st.sortCol = ci;
        st.sortDir = 'asc';
      }
      st.page = 1;
      renderFilterModule(modId);
    });
  });

  // Icon phễu: mở popup lọc theo cột kiểu Excel AutoFilter (checkbox nhiều giá trị, lọc ngay khi tick)
  document.querySelectorAll(`#${modId}Wrap .th-filter-btn`).forEach(btn=>{
    btn.addEventListener('click', e=>{
      e.stopPropagation();
      const rawCol = btn.dataset.filterCol;
      // Danh sách giá trị đề xuất trong popup CŨNG lọc liên kết theo tầng dựa trên các ô tìm
      // kiếm "chứa" đang gõ ở cột khác (giống hệt cơ chế bảng nguồn "Xuất Hàng").
      const searchedBase = Object.entries(st.colSearch)
        .filter(([k,v]) => k !== rawCol && String(v||'').trim() !== '')
        .reduce((rows,[k,q]) => rows.filter(({row}) => {
          const val = k==='__tpcStatus' ? getTpcRowStatus(getTpcStatusInfo()||{totals:new Map()}, row)
            : k==='__tongKL' ? filterModuleTongKLDisplay(row, getFilterModuleWeightBasis(modId)||{})
            : row[Number(k)];
          return fuzzyIncludes(val, q);
        }), filterModuleBaseRowsCached(modId));
      if(rawCol === '__tpcStatus'){
        // Cột ẢO "Trạng thái" (chỉ tab Thành Phẩm Chính) — không đọc trực tiếp từ row[ci] như
        // các cột thật, mà tính lại trạng thái từng dòng qua getTpcRowStatus().
        const info = getTpcStatusInfo();
        if(!info) return;
        const tpcValueOf = (item,k)=> k==='__tpcStatus' ? getTpcRowStatus(info, item.row) : normalizeCellForExactFilter(item.row[Number(k)]);
        openColSearchPopoverGeneric(
          btn, '__tpcStatus', 'Trạng thái',
          ()=>cascadeExcept(searchedBase, st.colFilters, '__tpcStatus', tpcValueOf).map(({row})=>getTpcRowStatus(info, row)),
          st.colFilters,
          ()=>{ st.page = 1; renderFilterModuleRows(modId); }
        );
        return;
      }
      if(rawCol === '__tongKL'){
        // Cột ẢO "Tổng KL (kg)" — không đọc trực tiếp từ row[ci], mà tính lại (Tồn cuối (NET) ×
        // Trọng lượng đơn vị) từng dòng qua filterModuleTongKLDisplay(), cùng chuỗi đã hiển thị
        // trên bảng để danh sách chọn trong popup luôn khớp đúng với những gì đang nhìn thấy.
        const basis = getFilterModuleWeightBasis(modId);
        if(!basis) return;
        const tongKLValueOf = (item,k)=> k==='__tongKL' ? filterModuleTongKLDisplay(item.row, basis) : normalizeCellForExactFilter(item.row[Number(k)]);
        openColSearchPopoverGeneric(
          btn, '__tongKL', 'Tổng KL (kg)',
          ()=>cascadeExcept(searchedBase, st.colFilters, '__tongKL', tongKLValueOf).map(({row})=>filterModuleTongKLDisplay(row, basis)),
          st.colFilters,
          ()=>{ st.page = 1; renderFilterModuleRows(modId); }
        );
        return;
      }
      const ci = Number(rawCol);
      const label = state.headers[ci] || '';
      const colValueOf = (item,k)=> k==='__tpcStatus' ? getTpcRowStatus(getTpcStatusInfo(), item.row) : normalizeCellForExactFilter(item.row[Number(k)]);
      openColSearchPopoverGeneric(btn, ci, label, ()=>cascadeExcept(searchedBase, st.colFilters, ci, colValueOf).map(({row})=>normalizeCellForExactFilter(row[ci])), st.colFilters, ()=>{ st.page = 1; renderFilterModuleRows(modId); });
    });
  });

  // Ô tìm kiếm riêng theo từng cột (dòng ngay dưới tiêu đề) — lọc kiểu "chứa". Dùng
  // renderFilterModuleRows() (chỉ vẽ lại <tbody>/<tfoot>, KHÔNG đụng tới chính ô
  // .fmColSearchInput đang gõ) nên không còn cần khôi phục focus/vị trí con trỏ như cũ nữa.
  const fmDoColSearchRender = debounce(()=>{
    renderFilterModuleRows(modId);
  }, SEARCH_DEBOUNCE_MS);
  document.querySelectorAll(`#${modId}Wrap .fmColSearchInput`).forEach(inp=>{
    inp.addEventListener('input', ()=>{
      const key = inp.dataset.colIndex;
      st.colSearch[key] = inp.value;
      st.page = 1;
      const wrap = document.getElementById(`${modId}Wrap`);
      if(wrap) wrap.dataset.lastFocusCol = key;
      fmDoColSearchRender();
    });
  });

  // ---------- Chọn dòng (checkbox) + chuyển mã hàng qua module khác ----------
  const rowChks = document.querySelectorAll(`#${modId}Wrap .fmRowChk`);
  const chkAll = document.getElementById(`${modId}ChkAll`);

  function syncFmChkAll(){
    if(!chkAll) return;
    const ids = Array.from(rowChks).map(el => Number(el.dataset.idx));
    const allSel = ids.length>0 && ids.every(i=>st.selected.has(i));
    const someSel = ids.some(i=>st.selected.has(i));
    chkAll.checked = allSel;
    chkAll.indeterminate = !allSel && someSel;
  }
  syncFmChkAll();

  rowChks.forEach(chk=>{
    chk.addEventListener('change', ()=>{
      const idx = Number(chk.dataset.idx);
      if(chk.checked) st.selected.add(idx); else st.selected.delete(idx);
      renderFilterModule(modId);
    });
  });

  if(chkAll){
    chkAll.addEventListener('change', ()=>{
      const ids = Array.from(rowChks).map(el => Number(el.dataset.idx));
      if(chkAll.checked) ids.forEach(i=>st.selected.add(i));
      else ids.forEach(i=>st.selected.delete(i));
      renderFilterModule(modId);
    });
  }

  const btnMove = document.getElementById(`${modId}BtnMove`);
  const moveTargetSelect = document.getElementById(`${modId}MoveTarget`);
  if(btnMove){
    btnMove.addEventListener('click', ()=>{
      if(m1DtDenied()) return;
      if(st.selected.size === 0 || !moveTargetSelect) return;
      const targetModId = moveTargetSelect.value;
      const targetLabel = (MODULES.find(m=>m.id===targetModId)||{}).label || targetModId;
      const n = st.selected.size;
      if(!confirm(`Chuyển ${n.toLocaleString('en-US')} mã hàng đã chọn sang module "${targetLabel}"?`)) return;
      moveRowsToModule(Array.from(st.selected), targetModId);
      st.selected = new Set();
      renderAll();
    });
  }

  const btnClearSel = document.getElementById(`${modId}BtnClearSel`);
  if(btnClearSel){
    btnClearSel.addEventListener('click', ()=>{
      st.selected = new Set();
      renderFilterModule(modId);
    });
  }

  // Huy hiệu "⇄ N chuyển thủ công" — bấm để bật/tắt chế độ chỉ hiện các dòng đã chuyển thủ
  // công vào module này, giúp tìm nhanh không cần cuộn/tìm trong toàn bộ bảng.
  const btnManualOnly = document.getElementById(`${modId}BtnManualOnly`);
  if(btnManualOnly){
    btnManualOnly.addEventListener('click', ()=>{
      st.showManualOnly = !st.showManualOnly;
      st.page = 1;
      renderFilterModule(modId);
    });
  }

  const btnRevertAll = document.getElementById(`${modId}BtnRevertAll`);
  if(btnRevertAll){
    btnRevertAll.addEventListener('click', ()=>{
      if(m1DtDenied()) return;
      const idxs = Object.keys(state.manualAssign)
        .map(Number)
        .filter(i => state.manualAssign[i] === modId);
      if(idxs.length === 0) return;
      if(!confirm(`Trả ${idxs.length.toLocaleString('en-US')} mã hàng đã chuyển thủ công về lại chế độ lọc tự động?`)) return;
      revertRowsToAuto(idxs);
      renderAll();
    });
  }

  // Badge "⇄" trên từng dòng đã chuyển thủ công — bấm để trả riêng dòng đó về tự động.
  document.querySelectorAll(`#${modId}Wrap .manual-item-badge`).forEach(badge=>{
    badge.addEventListener('click', e=>{
      e.stopPropagation();
      if(m1DtDenied()) return;
      const idx = Number(badge.dataset.revertIdx);
      revertRowsToAuto([idx]);
      renderAll();
    });
  });

  bindFilterModuleColResize(modId);
}

/* ================= RENDER: BÁO CÁO ================= */
// Bảng Tổng Hợp HLSO: cột (thị trường) và hàng (nhãn) CỐ ĐỊNH theo mẫu — khai báo ở phạm vi
// module để vừa dùng khi vẽ bảng (renderBaoCao) vừa dùng khi xuất Excel (exportHlsoSummaryToExcel).
const HLSO_SUMMARY_COLUMNS = [
  'MKS', 'NHẬT, ASC, HQ, MỸ', 'NHẬT, ASC-A,B, HQ, MỸ', 'NHẬT, ASC-B, HQ, MỸ', 'NHẬT, ASC, HQ',
  'NHẬT, EU, HQ', 'NHẬT, ASC-A,B, HQ', 'NHẬT, ASC-B, HQ', 'NHẬT, ASC', 'NHẬT, HQ', 'NHẬT, EU',
  'ASC, HQ, MỸ', 'ASC-A, B, HQ, MỸ', 'ASC-B, HQ, MỸ', 'ASC, HQ', 'ASC-A,B, HQ', 'ASC-B, HQ', 'ASC',
  'EU, HQ, MỸ', 'EU, HQ', 'EU', 'HQ', 'KHÔNG ĐẠT',
];
const HLSO_SUMMARY_ROWS = [
  'Thành phẩm chính', 'Thành phẩm tạm', 'Tôm BM', 'Bột + Sốt + Bếp',
];
// "Thành phẩm tạm" tách riêng thành 1 bảng nhỏ độc lập (không nằm trong bảng lớn ở trên) —
// cột "Tổng" của bảng nhỏ này LẤY THẲNG số liệu "Tổng trọng lượng (kg)" của module Thành Phẩm
// Tạm (id 'tptam') trên bảng "Báo Cáo Tổng Tồn", để luôn khớp với số đang hiển thị ở đó.
// Các cụm "1 module + danh sách Size lấy động" gắn TRỰC TIẾP trong bảng lớn "Bảng Tổng Hợp
// HLSO": mỗi cụm có 1 hàng tiêu đề cố định (VD "TÔM HLSO") + các hàng Size NGAY SAU nó, lấy
// động từ cột "Size" của ĐÚNG module đó (sắp xếp từ trên xuống). Thêm module mới vào danh sách
// này là tự động có thêm 1 cụm mới trên bảng, không cần sửa chỗ khác.
const HLSO_SIZE_GROUPS = [
  { moduleId: 'hlso',  title: 'TÔM HLSO' },
  { moduleId: 'tomgc', title: 'TÔM GIA CÔNG' },
  { moduleId: 'tomnk', title: 'TÔM NHẬP KHẨU' },
  { moduleId: 'tomnl', title: 'TÔM NGUYÊN LIỆU' },
  { moduleId: 'tomgs', title: 'TÔM GIÁM SÁT' },
];
// Sắp xếp Size theo 3 LOẠI (mỗi loại gộp riêng, sắp xếp tăng dần TRONG loại đó, rồi nối các
// loại lại theo đúng thứ tự 1 → 2 → 3):
//   Loại 1: Size dạng "MXX", "4LXX"… (kết thúc bằng 2 ký tự "XX")
//   Loại 2: Size có dấu "/" (VD "16/20", "91/120"…)
//   Loại 3: Size còn lại (số thường: "20", "30", "100"…)
function getSizeSortCategory(size){
  const s = String(size||'').trim();
  if(/XX$/i.test(s)) return 0;
  if(s.includes('/')) return 1;
  return 2;
}
function compareSizeValues(a, b){
  const ca = getSizeSortCategory(a), cb = getSizeSortCategory(b);
  if(ca !== cb) return ca - cb;
  return String(a).localeCompare(String(b), 'vi', { numeric: true, sensitivity: 'base' });
}
// Danh sách giá trị Size (cột "Size") xuất hiện trong dữ liệu của 1 module lọc cụ thể — sắp
// xếp từ trên xuống, dùng cho các hàng Size bên dưới mỗi tiêu đề cụm (VD "TÔM HLSO").
function getModuleSizeStats(moduleId){
  if(_fmSizeStatsCache[moduleId]) return _fmSizeStatsCache[moduleId];
  const stats = { order: [], bySize: {} };
  _fmSizeStatsCache[moduleId] = stats; // gán trước để tránh đệ quy vô hạn nếu bị gọi lồng
  if(!state.rows.length || !state.headers.length) return stats;
  const sizeCi = state.headers.findIndex(h => headersFuzzyMatch(h, 'Size'));
  if(sizeCi === -1) return stats;
  const dtCi = state.headers.findIndex(h => headersFuzzyMatch(h, 'Đặc tính'));
  const basis = getFilterModuleWeightBasis(moduleId);
  if(!basis) return stats;
  filterModuleBaseRowsCached(moduleId).forEach(({row})=>{
    const size = String(row[sizeCi] ?? '').trim();
    if(size === '') return;
    let entry = stats.bySize[size];
    if(!entry){ entry = { total: 0, hasTotal: false, byGroup: {} }; stats.bySize[size] = entry; stats.order.push(size); }
    const v = filterModuleRowTotalWeight(row, basis);
    if(v === null) return;
    // CHỈ cộng vào cột "Tổng" (và vào đúng 1 trong 11 cột nhóm thị trường) khi giá trị "Đặc
    // tính" của dòng đó ĐÃ được gộp vào 1 nhóm ở bảng "Gộp Đặc Tính" (module Đặc Tính). Dòng có
    // "Đặc tính" trống HOẶC chưa gộp nhóm nào thì KHÔNG được tính vào "Tổng" — trước đây "Tổng"
    // cộng dồn TẤT CẢ các dòng bất kể đã gộp hay chưa, khiến "Tổng" không khớp với tổng của 11
    // cột nhóm thị trường cộng lại khi còn đặc tính chưa gộp.
    let grp = null;
    if(dtCi !== -1){
      const dtLabel = String(row[dtCi] ?? '').trim();
      if(dtLabel !== '') grp = getDacTinhAssignedGroup(dtLabel);
    }
    if(!grp) return;
    entry.total += v; entry.hasTotal = true;
    let g = entry.byGroup[grp];
    if(!g){ g = { sum: 0, has: false }; entry.byGroup[grp] = g; }
    g.sum += v; g.has = true;
  });
  stats.order.sort(compareSizeValues);
  return stats;
}
function getModuleSizeList(moduleId){
  if(!state.rows.length || !state.headers.length) return [];
  return getModuleSizeStats(moduleId).order.slice();
}
function getModuleSizeTotalWeight(moduleId, size){
  if(!state.rows.length || !state.headers.length) return null;
  const e = getModuleSizeStats(moduleId).bySize[size];
  if(!e) return null;
  return e.hasTotal ? e.total : null;
}
function getGroupTotalWeight(moduleId){
  const stats = getModuleSizeStats(moduleId);
  let sum = 0, has = false;
  stats.order.forEach(size=>{
    const e = stats.bySize[size];
    if(e.hasTotal){ sum += e.total; has = true; }
  });
  return has ? sum : null;
}
// Danh sách hàng ĐẦY ĐỦ của "Bảng Tổng Hợp HLSO", mỗi phần tử {label, kind, moduleId?}:
// kind 'static' = nhãn cố định (Thành phẩm tạm…, so khớp qua findBaoCaoTotalWeightByLabel);
// kind 'group-title' = hàng tiêu đề 1 cụm module (VD "TÔM HLSO");
// kind 'group-size' = 1 hàng Size bên trong đúng cụm module đó (có moduleId đi kèm để KHÔNG
// bị lẫn khi 2 module khác nhau cùng có 1 giá trị Size giống nhau).
// Dùng chung cho cả render bảng lẫn xuất Excel để số liệu luôn khớp nhau.
function getHlsoSummaryRows(){
  const rows = HLSO_SUMMARY_ROWS.map(label => ({ label, displayLabel: label, kind: 'static' }));
  HLSO_SIZE_GROUPS.forEach(g=>{
    rows.push({ label: g.title, displayLabel: g.title, kind: 'group-title', moduleId: g.moduleId });
    getModuleSizeList(g.moduleId).forEach(size=>{
      // label = giá trị Size GỐC (dùng để tra cứu số liệu, khớp đúng dữ liệu nguồn); displayLabel
      // = giá trị có gắn tiền tố TGC/NL/TGS (chỉ dùng để HIỂN THỊ/XUẤT EXCEL) — tách riêng 2 giá
      // trị để không làm sai lệch việc tra cứu/tính tổng theo Size thật trong dữ liệu.
      rows.push({ label: size, displayLabel: applySizePrefix(g.moduleId, 'Size', size), kind: 'group-size', moduleId: g.moduleId });
    });
  });
  return rows;
}
// PC 2.11: danh sách hàng DÙNG ĐỂ HIỂN THỊ / XUẤT EXCEL của "Bảng Tổng Hợp HLSO" — bỏ các hàng không còn tồn
// (Tổng và mọi cột thị trường đều rỗng / 0); hàng tiêu đề cụm (TÔM HLSO…) chỉ giữ khi cụm còn ít nhất 1 hàng Size còn tồn.
// getHlsoSummaryRows() GIỮ NGUYÊN (đủ hàng) cho các chỗ tính toán khác (Đối soát, Tỉ lệ %…).
function getHlsoSummaryRowsInStock(){
  const all = getHlsoSummaryRows();
  const inStock = (rowDesc) => {
    if(hasStockWeight(getHlsoSummaryRowTotal(rowDesc))) return true;
    return HLSO_SUMMARY_COLUMNS.some(mg => hasStockWeight(getHlsoSummaryRowGroupWeight(rowDesc, mg)));
  };
  const out = [];
  let pendingTitle = null, titleUsed = false;
  all.forEach(rowDesc=>{
    if(rowDesc.kind === 'group-title'){ pendingTitle = rowDesc; titleUsed = false; return; }
    if(rowDesc.kind === 'group-size'){
      if(!inStock(rowDesc)) return;
      if(pendingTitle && !titleUsed){ out.push(pendingTitle); titleUsed = true; }
      out.push(rowDesc); return;
    }
    if(inStock(rowDesc)) out.push(rowDesc); // hàng 'static' (Thành phẩm chính…)
  });
  return out;
}
// Tính "Tổng" cho 1 hàng theo cấu trúc getHlsoSummaryRows() ở trên.
function getHlsoSummaryRowTotal(rowDesc){
  if(rowDesc.kind === 'group-title') return getGroupTotalWeight(rowDesc.moduleId);
  if(rowDesc.kind === 'group-size') return getModuleSizeTotalWeight(rowDesc.moduleId, rowDesc.label);
  return findBaoCaoTotalWeightByLabel(rowDesc.label);
}

// ---------- Số liệu từng ô của 11 cột thị trường (HLSO_SUMMARY_COLUMNS) trong "Bảng Tổng
// Hợp HLSO" ----------
// Với 1 hàng Size cụ thể (VD size "1L" của module 'hlso') + 1 nhóm thị trường (VD "MKS"):
// cộng dồn "Tổng KL (kg)" của CÁC DÒNG DỮ LIỆU GỐC thuộc ĐÚNG module đó, ĐÚNG Size đó, VÀ có
// giá trị cột "Đặc tính" đã được gán vào ĐÚNG nhóm marketGroup ở bảng "Gộp Đặc Tính" (module
// Đặc Tính, state.dactinhGroupAssign — xem getDacTinhAssignedGroup). Dòng có "Đặc tính" trống,
// chưa gán nhóm nào, hoặc gán nhóm KHÁC thì không được cộng vào ô này.
function getModuleSizeGroupWeight(moduleId, size, marketGroup){
  if(!state.rows.length || !state.headers.length) return null;
  const e = getModuleSizeStats(moduleId).bySize[size];
  if(!e) return null;
  const g = e.byGroup[marketGroup];
  return (g && g.has) ? g.sum : null;
}
// Tổng của 1 cụm (VD "TÔM HLSO") cho 1 cột thị trường = cộng dồn giá trị cột đó của TẤT CẢ
// các hàng Size bên dưới cụm (tương tự getGroupTotalWeight nhưng lọc thêm theo marketGroup).
function getGroupTitleGroupWeight(moduleId, marketGroup){
  const stats = getModuleSizeStats(moduleId);
  let sum = 0, has = false;
  stats.order.forEach(size=>{
    const g = stats.bySize[size].byGroup[marketGroup];
    if(g && g.has){ sum += g.sum; has = true; }
  });
  return has ? sum : null;
}
// Giá trị 1 ô (hàng rowDesc × cột marketGroup) trong "Bảng Tổng Hợp HLSO". Chỉ các hàng
// 'group-title'/'group-size' (5 cụm Tôm HLSO/Gia Công/Nhập Khẩu/Nguyên Liệu/Giám Sát) mới có
// số liệu theo nhóm thị trường — các hàng 'static' (Thành phẩm chính, Bột + Sốt + Bếp) không
// thuộc phạm vi gộp Đặc Tính nên luôn để trống (null) ở 11 cột này, chỉ có cột "Tổng".
function getHlsoSummaryRowGroupWeight(rowDesc, marketGroup){
  if(rowDesc.kind === 'group-title') return getGroupTitleGroupWeight(rowDesc.moduleId, marketGroup);
  if(rowDesc.kind === 'group-size') return getModuleSizeGroupWeight(rowDesc.moduleId, rowDesc.label, marketGroup);
  return null;
}

// Danh sách chỉ số cột (trong HLSO_SUMMARY_COLUMNS) có ÍT NHẤT 1 dòng Size mang số liệu thật
// — dùng để ẨN các cột thị trường hoàn toàn trống ("-" ở mọi dòng) khi state.hlsoHideEmptyCols
// bật, cho bảng gọn hơn. Chỉ cần quét các dòng 'group-size' (dòng 'group-title' chỉ là tổng
// của chính các dòng size đó nên không cần quét thêm).
function getHlsoColumnsWithData(){
  const sizeRowDescs = getHlsoSummaryRows().filter(r => r.kind === 'group-size');
  return HLSO_SUMMARY_COLUMNS.map((marketGroup)=>
    sizeRowDescs.some(rowDesc=>{
      const v = getModuleSizeGroupWeight(rowDesc.moduleId, rowDesc.label, marketGroup);
      return v !== null && v !== undefined && !isNaN(v);
    })
  );
}

// Tổng "TỔNG TỒN" của "Bảng Tổng Hợp HLSO" (cộng dồn cột "Tổng" của các dòng Size, bỏ qua dòng
// tiêu đề cụm để không tính trùng) — TÁCH RIÊNG thành hàm dùng chung cho cả
// buildHlsoSummarySectionHtml() (vẽ bảng) lẫn phần "Đối Soát" (so sánh với 8 module), vì 2 chỗ
// này không còn cùng phạm vi biến sau khi bảng HLSO được tách hàm riêng.
function getHlsoGrandTotal(){
  let total = 0, has = false;
  getHlsoSummaryRows().forEach(rowDesc=>{
    if(rowDesc.kind === 'group-title') return;
    const totalVal = getHlsoSummaryRowTotal(rowDesc);
    if(totalVal!==null && totalVal!==undefined && !isNaN(totalVal)){ total += totalVal; has = true; }
  });
  return { total, has };
}

/* ================= ĐỐI SOÁT: dòng gây lệch giữa "Tổng Bảng Tổng Hợp HLSO" và "Tổng cộng 8
   module" =================
   getModuleSizeStats() (dùng cho "Bảng Tổng Hợp HLSO") CHỈ cộng vào "Tổng" những dòng có cột
   "Đặc tính" ĐÃ được gộp vào 1 trong 11 nhóm ở bảng "Gộp Đặc Tính" (xem chú thích tại đó) —
   trong khi "Tổng cộng 8 module" cộng TẤT CẢ dòng của cả 8 module, bất kể "Đặc tính" đã gộp
   nhóm hay chưa. Nên phần chênh lệch (HLSO vs 8 module) — nếu là số ÂM — hầu hết đến từ các
   dòng thuộc 5 cụm module Size (TÔM HLSO/GIA CÔNG/NHẬP KHẨU/NGUYÊN LIỆU/GIÁM SÁT — xem
   HLSO_SIZE_GROUPS) mà cột "Đặc tính" đang TRỐNG hoặc CHƯA được gộp nhóm. Hàm này quét lại
   đúng những dòng đó để chỉ thẳng ra Mã hàng/Tên hàng/Size/Đặc tính cụ thể nào đang gây lệch,
   thay vì chỉ báo 1 con số tổng chênh lệch không rõ nguồn gốc.
*/
function computeHlsoDacTinhGapRows(){
  if(!state.rows.length || !state.headers.length) return null;
  const dtCi = state.headers.findIndex(h => headersFuzzyMatch(h, 'Đặc tính'));
  const maCi = state.headers.findIndex(h => headersFuzzyMatch(h, 'Mã hàng'));
  const tenCi = state.headers.findIndex(h => headersFuzzyMatch(h, 'Tên hàng'));
  const sizeCi = state.headers.findIndex(h => headersFuzzyMatch(h, 'Size'));

  const gapRows = [];
  let gapWeight = 0;

  HLSO_SIZE_GROUPS.forEach(g=>{
    const basis = getFilterModuleWeightBasis(g.moduleId);
    if(!basis) return;
    filterModuleBaseRowsCached(g.moduleId).forEach(({row})=>{
      const dtLabel = dtCi!==-1 ? String(row[dtCi] ?? '').trim() : '';
      const grp = dtLabel !== '' ? getDacTinhAssignedGroup(dtLabel) : null;
      if(grp) return; // đã gộp nhóm — có tính vào "Bảng Tổng Hợp HLSO", không phải dòng gây lệch
      const w = filterModuleRowTotalWeight(row, basis);
      if(w !== null) gapWeight += w;
      gapRows.push({
        maHang: maCi!==-1 ? row[maCi] : '',
        tenHang: tenCi!==-1 ? row[tenCi] : '',
        size: sizeCi!==-1 ? row[sizeCi] : '',
        dacTinh: dtLabel === '' ? '(trống)' : dtLabel,
        module: g.title,
        weight: w,
      });
    });
  });

  gapRows.sort((a,b)=> (b.weight||0) - (a.weight||0));
  return { gapRows, gapWeight };
}

// Dựng HTML cho TOÀN BỘ khu vực "Bảng Tổng Hợp HLSO" (tiêu đề + nút + bảng + ghi chú cảnh báo
// cột thiếu) — tách riêng khỏi renderBaoCao() để có thể gọi lại ĐỘC LẬP (refreshHlsoSummarySection()
// bên dưới) khi bấm nút "Ẩn/Hiện cột trống", tránh phải vẽ lại toàn trang gây giật màn hình và
// mất vị trí cuộn. Tự tính lại recon (đối soát) nên không phụ thuộc biến closure của renderBaoCao().
function buildHlsoSummarySectionHtml(){
  const recon = computeModuleReconciliation();

  // Cột nào KHÔNG có số liệu ở bất kỳ dòng Size nào — ẩn bớt cho bảng gọn (mặc định ẨN, có nút
  // ở trên để bật lại xem đủ). Luôn còn ít nhất các cột CÓ số liệu, không bao giờ ẩn hết sạch.
  const hlsoColHasData = getHlsoColumnsWithData();
  const hlsoEmptyColCount = hlsoColHasData.filter(h=>!h).length;
  const hlsoVisibleColIdx = state.hlsoHideEmptyCols
    ? HLSO_SUMMARY_COLUMNS.map((_,i)=>i).filter(i=>hlsoColHasData[i])
    : HLSO_SUMMARY_COLUMNS.map((_,i)=>i);

  let html = `<div style="display:flex;align-items:center;gap:12px;margin:4px 0 8px;min-height:33px;">
      <h3 style="font-family:var(--display);font-size:15px;font-weight:600;color:var(--text);">Bảng Tổng Hợp HLSO</h3>
      <button class="btn btn-ghost" id="btnExportHlsoSummary">${icon('download')} Xuất Excel</button>
      <button class="btn btn-ghost" id="btnGoDacTinhFromHlso">${icon('stack')} Đặc Tính</button>
      <button class="btn btn-ghost" id="btnFullHlsoSummary" data-kl-full="#hlsoSummarySection" title="Mở rộng bảng ra toàn màn hình">⛶ <span class="kl-fs-txt">Toàn màn hình</span></button>
      ${hlsoEmptyColCount > 0 ? `<button class="btn btn-ghost" id="btnToggleHlsoEmptyCols">${state.hlsoHideEmptyCols ? `${icon('search')} Hiện cột trống (${hlsoEmptyColCount})` : 'Ẩn cột trống'}</button>` : ''}
    </div>`;

  // Tính trước TỔNG TỒN (cột "Tổng") để hiển thị ngay trên đầu cột header, dùng lại kết quả
  // này cho cả header VÀ dòng "TỔNG TỒN" ở footer bên dưới — đảm bảo 2 nơi luôn khớp số nhau.
  const { total: hlsoGrandTotal, has: hlsoGrandTotalHas } = getHlsoGrandTotal();
  const hlsoGrandTotalHeaderHtml = hlsoGrandTotalHas ? hlsoGrandTotal.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2}) : '-';

  html += `<div class="table-shell"><div class="table-scroll table-wrap"><table class="hlso-summary-table"><thead><tr>
      <th class="${colFilterActive(state.hlsoSummaryColFilters.hlsoTitle)?'has-filter':''}">
        <div class="th-row">
          <span class="th-label" style="cursor:default;">Size</span>
          <button type="button" class="th-filter-btn ${colFilterActive(state.hlsoSummaryColFilters.hlsoTitle)?'active':''}" data-filter-col="hlsoTitle" title="Lọc theo Size">${icon('search')}</button>
        </div>
      </th>
      ${hlsoVisibleColIdx.map(gi=>`<th>${escapeHtml(HLSO_SUMMARY_COLUMNS[gi])}</th>`).join('')}
      <th class="num" title="Tổng">${hlsoGrandTotalHeaderHtml}</th>
      <th>Size</th>
    </tr></thead><tbody>`;
  // Tổng theo TỪNG cột thị trường (dòng footer) — cộng dồn song song với hlsoGrandTotal.
  const colTotals = HLSO_SUMMARY_COLUMNS.map(()=>({ sum: 0, has: false }));
  const hlsoTitleFilterSet = state.hlsoSummaryColFilters.hlsoTitle;
  getHlsoSummaryRowsInStock().forEach(rowDesc=>{
    const totalVal = getHlsoSummaryRowTotal(rowDesc);
    const totalCellHtml = totalVal!==null?totalVal.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2}):'<span class="cell-empty">-</span>';
    const isGroupTitle = rowDesc.kind === 'group-title';
    const allColsHtml = HLSO_SUMMARY_COLUMNS.map((marketGroup, gi)=>{
      const gVal = getHlsoSummaryRowGroupWeight(rowDesc, marketGroup);
      if(!isGroupTitle && gVal!==null && gVal!==undefined && !isNaN(gVal)){ colTotals[gi].sum += gVal; colTotals[gi].has = true; }
      const gCellHtml = gVal!==null ? gVal.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2}) : '<span class="cell-empty">-</span>';
      return `<td class="num">${gCellHtml}</td>`;
    });
    const groupCellsHtml = hlsoVisibleColIdx.map(gi=>allColsHtml[gi]).join('');
    if(!colFilterActive(hlsoTitleFilterSet) || hlsoTitleFilterSet.has(normalizeCellForExactFilter(rowDesc.displayLabel))){
      html += `<tr${isGroupTitle ? ' class="hlso-group-title-row"' : ''}>
        <td style="font-weight:700;color:${isGroupTitle ? 'var(--frost)' : 'var(--text)'};">${escapeHtml(rowDesc.displayLabel)}</td>
        ${groupCellsHtml}
        <td class="num">${totalCellHtml}</td>
        <td style="color:${isGroupTitle ? 'var(--ice-dim)' : 'var(--text-muted)'};">${escapeHtml(rowDesc.displayLabel)}</td>
      </tr>`;
    }
  });
  html += `</tbody>`;
  html += `<tfoot><tr>
      <td class="total-label">TỔNG TỒN</td>
      ${hlsoVisibleColIdx.map(gi=>`<td class="num">${colTotals[gi].has?colTotals[gi].sum.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2}):'<span class="cell-empty">-</span>'}</td>`).join('')}
      <td class="num">${hlsoGrandTotalHas?hlsoGrandTotal.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2}):'<span class="cell-empty">-</span>'}</td>
      <td></td>
    </tr></tfoot>`;
  html += `</table></div></div>`;

  if(recon && recon.columnWarnings.length){
    html += `<div class="diag-note warn-note" style="margin-top:10px;">${icon('file')} Module đang THIẾU cột lọc (điều kiện bị bỏ qua hoàn toàn — dễ gây trùng): `
      + recon.columnWarnings.map(w=>`<b>${escapeHtml(w.label)}</b>: thiếu "${w.missing.map(escapeHtml).join('", "')}"`).join(' &nbsp;·&nbsp; ')
      + `</div>`;
  }
  return html;
}

// Gắn sự kiện cho các nút/icon BÊN TRONG khu vực "Bảng Tổng Hợp HLSO" — gọi lại mỗi khi khu vực
// này được vẽ (cả lúc renderBaoCao() vẽ toàn trang lẫn lúc refreshHlsoSummarySection() vẽ riêng).
function attachHlsoSummarySectionListeners(){
  const btnExportHlso = document.getElementById('btnExportHlsoSummary');
  if(btnExportHlso) btnExportHlso.addEventListener('click', exportHlsoSummaryToExcel);
  const btnGoDacTinh = document.getElementById('btnGoDacTinhFromHlso');
  if(btnGoDacTinh) btnGoDacTinh.addEventListener('click', ()=>{
    state.activeModule = 'dactinh';
    renderAll();
  });
  const btnToggleEmptyCols = document.getElementById('btnToggleHlsoEmptyCols');
  if(btnToggleEmptyCols) btnToggleEmptyCols.addEventListener('click', ()=>{
    state.hlsoHideEmptyCols = !state.hlsoHideEmptyCols;
    refreshHlsoSummarySection();
  });
  const hlsoFilterBtn = document.querySelector('#hlsoSummarySection .th-filter-btn[data-filter-col="hlsoTitle"]');
  if(hlsoFilterBtn){
    hlsoFilterBtn.addEventListener('click', e=>{
      e.stopPropagation();
      openColSearchPopoverGeneric(hlsoFilterBtn, 'hlsoTitle', 'Size', ()=>getHlsoSummaryRowsInStock().filter(r=>r.kind!=='group-title').map(r=>normalizeCellForExactFilter(r.displayLabel)), state.hlsoSummaryColFilters, refreshHlsoSummarySection);
    });
  }
}

// PC 5.33 — dữ liệu cho "Bảng Tổng Hợp Tùy Chọn": CHỈ ĐỌC lại đúng các dòng / cột của bảng HLSO (cùng hàm với
// buildHlsoSummarySectionHtml), không tính lại từ tồn kho, không đụng state.hlsoSummaryColFilters.
function getHlsoCustomSummaryData(){
  const rows = []; let curTitle = '';
  getHlsoSummaryRowsInStock().forEach(rd=>{
    const isT = rd.kind === 'group-title';
    if(isT) curTitle = rd.displayLabel;
    rows.push({
      key: (isT ? 'T|' : 'S|' + curTitle + '|') + rd.displayLabel,
      label: rd.displayLabel,
      kind: isT ? 'title' : 'size',
      xKind: rd.kind, // PC 5.42: loại dòng gốc ('static' | 'group-title' | 'group-size') để file Excel tô màu đúng mẫu HLSO
      cells: HLSO_SUMMARY_COLUMNS.map(mg=>{ const v = getHlsoSummaryRowGroupWeight(rd, mg); return (v===null || v===undefined || isNaN(v)) ? null : v; }),
      total: getHlsoSummaryRowTotal(rd)
    });
  });
  return { cols: HLSO_SUMMARY_COLUMNS.slice(), rows };
}
function mountHlsoCustomSummary(){
  const el = document.getElementById('hlsoCustomSummary');
  if(!el || !window.KLCustomSummary) return;
  window.KLCustomSummary.mount(el, { key:'m1-hlso', title:'Bảng Tổng Hợp Tùy Chọn (HLSO)', getData:getHlsoCustomSummaryData, onExport:exportHlsoCustomToExcel });
}
// PC 5.42 — nút "Xuất Excel" của Bảng Tổng Hợp Tùy Chọn: file theo ĐÚNG MẪU của "Bảng Tổng Hợp HLSO"
// (hỏi ngày báo cáo, dòng Ngày báo cáo, tiêu đề + 2 dòng TỔNG, tô màu dòng tiêu đề nhóm / dòng riêng lẻ,
// cột Size lặp lại cuối, cố định dòng đầu, in vừa trang), chỉ khác là chỉ gồm Size + Đặc Tính đang chọn.
async function exportHlsoCustomToExcel(payload){
  const reportDateStr = await promptForReportDate();
  if(!reportDateStr) return; // bấm Hủy — không xuất file
  const wb = buildHlsoSummaryWorkbook(reportDateStr, payload);
  await writeWorkbookWithFreezeTopRow(wb, `BangTongHopTuyChonHLSO_${exportFileStamp()}.xlsx`);
}

// Vẽ lại RIÊNG khu vực "Bảng Tổng Hợp HLSO" (không đụng phần còn lại của trang) — dùng khi bấm
// nút "Ẩn/Hiện cột trống" hoặc lọc theo Size, giữ nguyên vị trí cuộn ngang/dọc của bảng thay vì
// vẽ lại toàn trang (nguyên nhân gây giật màn hình trước đây).
function refreshHlsoSummarySection(){
  const wrap = document.getElementById('hlsoSummarySection');
  if(!wrap) return;
  const oldScrollEl = wrap.querySelector('.table-scroll');
  const scrollLeft = oldScrollEl ? oldScrollEl.scrollLeft : 0;
  const scrollTop = oldScrollEl ? oldScrollEl.scrollTop : 0;
  wrap.innerHTML = buildHlsoSummarySectionHtml();
  attachHlsoSummarySectionListeners();
  initExcelLikeSelection(wrap.querySelector('table'));
  const newScrollEl = wrap.querySelector('.table-scroll');
  if(newScrollEl){ newScrollEl.scrollLeft = scrollLeft; newScrollEl.scrollTop = scrollTop; }
  if(window.klFullscreen) window.klFullscreen.sync(); // PC 2.11: nút Toàn màn hình vừa được vẽ lại
}



// Chuẩn hoá nhãn để so khớp không phân biệt hoa/thường, khoảng trắng thừa — dùng khi đối
// chiếu tên hàng "static" của "Bảng Tổng Hợp HLSO" với tên module trên bảng "Báo Cáo Tổng Tồn".
function normalizeLabelForMatch(s){
  return String(s||'').trim().toLowerCase();
}
// Với 1 nhãn hàng "static" (VD "Thành phẩm chính"), tìm module CÙNG TÊN trong 7 module lọc,
// rồi lấy đúng "Tổng trọng lượng (kg)" đang hiển thị ở bảng "Báo Cáo Tổng Tồn" cho module đó.
// Riêng nhãn trùng BAOCAO_MANUAL_LABEL ("Bột + Sốt + Bếp") thì lấy thẳng giá trị nhập tay đang
// lưu ở state.hlsoManualValues. Không khớp gì → null. (Các hàng "group-title"/"group-size" —
// VD "TÔM HLSO" và các Size bên dưới — KHÔNG đi qua hàm này, xem getHlsoSummaryRowTotal.)
function findBaoCaoTotalWeightByLabel(label){
  const target = normalizeLabelForMatch(label);
  if(target === normalizeLabelForMatch(BAOCAO_MANUAL_LABEL)){
    const v = state.hlsoManualValues[target];
    return (v===undefined) ? null : v;
  }
  const mod = MODULES.find(m => FILTER_MODULES[m.id] && normalizeLabelForMatch(m.label) === target);
  if(!mod) return null;
  return sumFilterModuleTotalWeight(mod.id);
}

// ---- Nút "Nạp dữ liệu" ở bảng "Báo Cáo Kho Gửi": lấy TỔNG trọng lượng từ Tồn kho gửi (Tồn Kho Gửi) ----
// Công thức GIỐNG HỆT ô Tổng KG của Module 2 / trang chính (CLAUDE.md › "Tổng KG — 1 công thức"):
// Σ cột "Trọng Lượng (KG)" của sheet DuLieuChinh (module2_tonkhogui.xlsx), đọc số kiểu 1,234.5 / 1.234,5.
// Ghi vào CÙNG ô với "Tổng Hợp > Báo cáo ngoài sổ sách" (state.hlsoManualValues + sheet BaoCaoNgoaiSoSach)
// nên 2 nơi luôn khớp; vẫn sửa tay được ở Tổng Hợp như cũ. Số liệu Module 2 = lần bấm "Nạp Dữ Liệu" gần nhất
// bên Tồn kho gửi (đọc từ database Excel, không phải bảng đang mở chưa lưu).
async function readModule2TotalKg(){
  if(!hasDbAPI()) throw new Error('Chỉ dùng được trong ứng dụng (không đọc được database Tồn kho gửi).');
  const res = await window.dbAPI.readSheet('ton-kho-gui', 'DuLieuChinh');
  if(!res || !res.ok) throw new Error((res && res.error) || 'Không đọc được dữ liệu Tồn kho gửi.');
  const rows = Array.isArray(res.data) ? res.data : [];
  let kg = 0, count = 0;
  rows.forEach(r=>{
    const w = parseLooseNumber(r && r['Trọng Lượng (KG)']);
    if(!isNaN(w)){ kg += w; count++; }
  });
  let dataDate = '', importedAt = '';
  try{
    const m = await window.dbAPI.readSheet('ton-kho-gui', 'DuLieuMeta');
    const cell = (m && m.ok && Array.isArray(m.data) && m.data[0]) ? m.data[0].data : null;
    if(typeof cell === 'string' && cell.startsWith('__JSON__')){
      const meta = JSON.parse(cell.slice('__JSON__'.length));
      if(meta && meta.dataDate) dataDate = String(meta.dataDate);
      if(meta && meta.importedAt) importedAt = String(meta.importedAt);
    }
  }catch(e){ /* thiếu meta — không sao, chỉ không ghi được ngày số liệu */ }
  return { kg: Math.round(kg * 100) / 100, rows: rows.length, count, dataDate, importedAt };
}
// ---- PC 3.72: Kho Gửi TỰ CẬP NHẬT từ Tồn kho gửi (không cần bấm "Nạp dữ liệu") ----
// Chạy đúng 3 lúc, KHÔNG gọi liên tục: (1) khi vào / vẽ lại trang Báo Cáo, (2) khi quay lại cửa sổ/tab đang ở Báo Cáo,
// (3) khi Module 2 báo vừa nạp file xong (BroadcastChannel / localStorage / postMessage — cái nào chạy được thì dùng).
// Chỉ ghi khi dữ liệu Tồn kho gửi THỰC SỰ đổi so với lần đồng bộ trước (chữ ký = tổng kg | số dòng | ngày số liệu | lúc nạp) nên
// số sửa tay ở Tổng Hợp không bị ghi đè lung tung. Nút "Nạp dữ liệu" vẫn còn để ép cập nhật ngay.
const KHOGUI_SYNC_SIG_KEY = 'kl:khoGuiSyncSig';
let khoGuiSyncBusy = false, khoGuiLastCheck = 0;
const khoGuiSigOf = (i) => [i.kg, i.rows, i.dataDate || '', i.importedAt || ''].join('|');
function khoGuiSaveSig(info){ try{ localStorage.setItem(KHOGUI_SYNC_SIG_KEY, khoGuiSigOf(info)); }catch(e){} }
function khoGuiShowNote(info, err){
  const el = document.getElementById('khoGuiSyncNote'); if(!el) return;
  if(err){ el.textContent = '⚠ ' + err; el.style.color = '#e6b34a'; return; }
  const t = new Date(); const hh = String(t.getHours()).padStart(2,'0') + ':' + String(t.getMinutes()).padStart(2,'0');
  el.textContent = 'Tự cập nhật theo Tồn kho gửi' + (info && info.dataDate ? ' · số liệu ngày ' + info.dataDate : '') + ' · ' + hh;
  el.style.color = '';
}
async function autoSyncKhoGui(force){
  if(khoGuiSyncBusy || !hasDbAPI()) return;
  const now = Date.now();
  if(!force && now - khoGuiLastCheck < 2500) return;   // vẽ lại liên tiếp trong vài giây → không đọc lại database
  khoGuiSyncBusy = true; khoGuiLastCheck = now;
  try{
    const info = await readModule2TotalKg();
    if(!info.rows){ khoGuiShowNote(null, 'Tồn kho gửi chưa có dữ liệu — giữ số hiện tại'); return; }
    let last = ''; try{ last = localStorage.getItem(KHOGUI_SYNC_SIG_KEY) || ''; }catch(e){}
    const key = normalizeLabelForMatch(BAOCAO_KHOGUI_LABEL);
    if(khoGuiSigOf(info) === last){ khoGuiShowNote(info, false); return; }   // Module 2 chưa đổi
    const oldVal = state.hlsoManualValues[key];
    state.hlsoManualValues[key] = info.kg;
    const cell = document.getElementById('khoGuiValCell');
    const fmt = v => Number(v).toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2});
    if(cell) cell.textContent = fmt(info.kg);
    const where = await persistNgoaiSoSach();
    if(where === 'excel'){
      khoGuiSaveSig(info); khoGuiShowNote(info, false);
      if(oldVal === undefined || oldVal === null || Math.abs(oldVal - info.kg) >= 0.005)
        toast(`Kho Gửi tự cập nhật từ Tồn kho gửi: ${fmt(info.kg)} kg` + (info.dataDate ? ` (số liệu ngày ${info.dataDate})` : '') + (oldVal != null && !isNaN(oldVal) ? ` · trước đó ${fmt(oldVal)}` : '') + '.');
    }else{
      toast(`Kho Gửi: đã lấy ${fmt(info.kg)} kg từ Tồn kho gửi nhưng CHƯA lưu được vào database.`, true);
    }
  }catch(e){
    console.error('Tự cập nhật Kho Gửi từ Tồn kho gửi:', e);
    khoGuiShowNote(null, 'Không đọc được Tồn kho gửi — bấm "Nạp dữ liệu" để thử lại');
  }finally{ khoGuiSyncBusy = false; }
}
(function hookKhoGuiAutoSync(){
  const onM2 = () => { autoSyncKhoGui(true); };
  try{ const bc = new BroadcastChannel('kl-m2-changed'); bc.onmessage = onM2; }catch(e){ window.klLog && klLog('ton-kho-an-an.js:6155', e); }
  window.addEventListener('storage', (e) => { if(e.key === 'kl:m2-changed') onM2(); });
  window.addEventListener('message', (e) => { if(e && e.data && e.data.type === 'klg-data-changed') onM2(); });
  const back = () => { if(!document.hidden && state.activeModule === 'baocao') autoSyncKhoGui(false); };
  document.addEventListener('visibilitychange', back);
  window.addEventListener('focus', back);
})();

async function napKhoGuiTuModule2(btn){
  if(btn){ btn.disabled = true; }
  let info;
  try{ info = await readModule2TotalKg(); }
  catch(e){ console.error('Nạp Kho Gửi từ Tồn kho gửi:', e); toast(e.message || 'Lỗi đọc dữ liệu Tồn kho gửi', true); if(btn) btn.disabled = false; return; }
  if(btn) btn.disabled = false;
  if(!info.rows){ toast('Tồn Kho Gửi chưa có dữ liệu — hãy "Nạp Dữ Liệu" bên Tồn kho gửi trước.', true); return; }
  const key = normalizeLabelForMatch(BAOCAO_KHOGUI_LABEL);
  const oldVal = state.hlsoManualValues[key];
  const fmt = v => Number(v).toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2});
  const dateTxt = info.dataDate ? ` (số liệu ngày ${info.dataDate})` : '';
  const apply = async ()=>{
    state.hlsoManualValues[key] = info.kg;
    const cell = document.getElementById('khoGuiValCell');
    if(cell) cell.textContent = fmt(info.kg);
    const where = await persistNgoaiSoSach();
    if(where === 'excel'){ khoGuiSaveSig(info); khoGuiShowNote(info, false); }
    if(where === 'excel') toast(`Đã nạp Kho Gửi: ${fmt(info.kg)} kg từ Tồn kho gửi${dateTxt}.`);
    else toast(`Đã nạp ${fmt(info.kg)} kg nhưng CHƯA lưu được vào database (${where === 'localStorage' ? 'chỉ lưu tạm' : 'lỗi lưu'}).`, true);
  };
  if(oldVal !== null && oldVal !== undefined && Math.abs(oldVal - info.kg) >= 0.005){
    showConfirmPopup('Thay số Kho Gửi?',
      `Hiện tại: ${fmt(oldVal)} kg → Tồn kho gửi${dateTxt}: ${fmt(info.kg)} kg (${info.rows.toLocaleString('en-US')} dòng). Thay bằng số của Module 2?`,
      apply);
  } else {
    await apply();
  }
}

function renderBaoCao(){
  const c = document.getElementById('content');
  const hasData = state.rows.length > 0 && state.headers.length > 0;

  if(!hasData){
    c.innerHTML = `
      <div class="placeholder">
        <div class="ph-ic">${icon('chart','ic')}</div>
        <h2>Chưa có dữ liệu để báo cáo</h2>
        <p>Vào mục <b style="color:var(--frost)">Load Data</b> để nạp file Excel tồn kho trước — báo cáo sẽ tự động tổng hợp từ dữ liệu đó.</p>
        <div class="tag" id="btnGoLoadDataBaoCao" style="cursor:pointer;">ĐI TỚI LOAD DATA</div>
      </div>
    `;
    const goBtn = document.getElementById('btnGoLoadDataBaoCao');
    if(goBtn) goBtn.addEventListener('click', ()=>{
      state.activeModule = 'loaddata';
      renderAll();
    });
    return;
  }

  // Danh sách hàng bảng "Báo Cáo Tổng Tồn": dòng đầu là "Bột + Sốt + Bếp" (nhập tay), sau đó
  // là 7 module lọc, giữ đúng thứ tự hiển thị trên thanh điều hướng.
  const rows = getBaoCaoTongTonRows();
  const totalWeight = getBaoCaoTongTonTotal(rows);

  let html = `<div class="data-wrap show" id="baocaoWrap">`;
  html += `<div class="diag-note">${icon('search')} Tổng hợp trọng lượng tồn kho theo từng module, tính trên dữ liệu đã nạp ở <b>Load Data</b>: <b>${state.fileName || 'dữ liệu đã lưu'}</b>.</div>`;

  // ---------- Đối soát: tìm nguyên nhân TỔNG 7 MODULE lệch so với tổng Load Data ----------
  const recon = computeModuleReconciliation();

  // Hàng ngang: "Báo Cáo Tổng Tồn" bên trái, "Bảng Tổng Hợp HLSO" bên phải — align-items:stretch
  // để CỘT TRÁI tự giãn theo đúng chiều cao của cột phải (thường cao hơn vì có nhiều dòng Size),
  // 2 bảng luôn có chiều cao bằng nhau, không còn cảm giác lệch/hụt.
  // PC 5.34 — PHƯƠNG ÁN A: "Báo Cáo Tổng Tồn" + "Báo Cáo Kho Gửi" xếp thành 1 HÀNG NGANG nhỏ ở trên; "Bảng Tổng Hợp HLSO"
  // nằm RIÊNG 1 dòng bên dưới, rộng hết chiều ngang để thấy nhiều cột hơn (trước đây chỉ chiếm nửa phải).
  html += `<div style="display:flex;flex-direction:column;gap:20px;align-items:stretch;">`;

  let reconHtml = '';
  if(recon){
    const totalAllRows = sumTotalWeightAllRows();
    const diff = (totalWeight!==null && totalAllRows!==null) ? (totalWeight - totalAllRows) : null;
    // So sánh thêm với "Bảng Tổng Hợp HLSO" — nếu 2 số này lệch nhau nghĩa là phần gộp theo
    // Size/thị trường ở Bảng Tổng Hợp HLSO đang thiếu/thừa dữ liệu so với 8 module lọc gốc,
    // cần rà lại cấu hình HLSO_SIZE_GROUPS hoặc dữ liệu cột "Đặc tính".
    const { total: hlsoGrandTotal, has: hlsoGrandTotalHas } = getHlsoGrandTotal();
    const hlsoVsModuleDiff = (hlsoGrandTotalHas && totalWeight!==null) ? (hlsoGrandTotal - totalWeight) : null;

    reconHtml += `<div id="baocaoDoiSoatCol" style="flex:1 1 360px;min-width:320px;max-width:620px;display:flex;flex-direction:column;">`;
    reconHtml += `<div style="display:flex;align-items:center;margin:4px 0 8px;min-height:33px;">
      <h3 style="font-family:var(--display);font-size:15px;font-weight:600;color:var(--text);">Đối Soát: 8 Module vs Load Data</h3>
    </div>`;

    const reconDupCols = [
      { key:'maHang', label:'Mã hàng' }, { key:'tenHang', label:'Tên hàng' }, { key:'size', label:'Size' },
      { key:'modules', label:'Thuộc module' }, { key:'weight', label:'Trọng lượng dòng (kg)' }, { key:'excess', label:'Đếm dư (kg)' }
    ];
    const reconMissingCols = [
      { key:'maHang', label:'Mã hàng' }, { key:'tenHang', label:'Tên hàng' }, { key:'size', label:'Size' },
      { key:'weight', label:'Trọng lượng dòng (kg)' }
    ];
    const hlsoGapCols = [
      { key:'maHang', label:'Mã hàng' }, { key:'tenHang', label:'Tên hàng' }, { key:'size', label:'Size' },
      { key:'dacTinh', label:'Đặc tính' }, { key:'module', label:'Thuộc cụm' }, { key:'weight', label:'Trọng lượng dòng (kg)' }
    ];
    const hlsoGap = computeHlsoDacTinhGapRows();

    reconHtml += '<div class="doisoat-card">';
    reconHtml += '<div class="ds-title">Đối Soát: 8 Module vs Load Data</div>';
    reconHtml += '<table class="ds-table"><thead><tr><th></th><th>TRỌNG LƯỢNG (KG)</th></tr></thead><tbody>';
    reconHtml += `<tr><td class="ds-label">Tổng toàn bộ dữ liệu Load Data</td><td class="ds-value">${ds_fmtKg(totalAllRows)}</td></tr>`;
    reconHtml += `<tr><td class="ds-label">Tổng cộng 8 module (bảng bên trái)</td><td class="ds-value">${ds_fmtKg(totalWeight)}</td></tr>`;
    reconHtml += `<tr class="${ds_diffRowClass(diff)}"><td class="ds-label">Chênh lệch</td><td class="ds-value">${ds_fmtKg(diff)}</td></tr>`;
    reconHtml += `<tr><td class="ds-label">Tổng Bảng Tổng Hợp HLSO</td><td class="ds-value">${hlsoGrandTotalHas ? ds_fmtKg(hlsoGrandTotal) : '<span class="cell-empty">—</span>'}</td></tr>`;
    reconHtml += `<tr class="${ds_diffRowClass(hlsoVsModuleDiff)}"><td class="ds-label">Chênh lệch (HLSO vs 8 module)</td><td class="ds-value">${hlsoVsModuleDiff!==null ? ds_fmtKg(hlsoVsModuleDiff) : '<span class="cell-empty">—</span>'}</td></tr>`;
    if(hlsoGap){
      reconHtml += `<tr class="ds-sub-row${doiSoatOpenSections.hlsoGap ? ' ds-open' : ''}" onclick="ds_toggleSection('hlsoGap')"><td class="ds-label">└ Do dòng chưa gộp "Đặc tính" (chưa cộng vào Bảng Tổng Hợp HLSO) <span class="ds-hint">${hlsoGap.gapRows.length ? '(bấm để xem chi tiết)' : ''}</span></td><td class="ds-value">−${hlsoGap.gapWeight.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2})}<span class="ds-sub">${hlsoGap.gapRows.length.toLocaleString('en-US')} dòng</span></td></tr>`;
      reconHtml += ds_renderDetailRow('hlsoGap', hlsoGap.gapRows, hlsoGapCols);
    }
    reconHtml += `<tr class="ds-sub-row${doiSoatOpenSections.overlap ? ' ds-open' : ''}" onclick="ds_toggleSection('overlap')"><td class="ds-label">└ Do dòng đếm ở ≥2 module (trùng) <span class="ds-hint">${recon.overlapRows.length ? '(bấm để xem chi tiết)' : ''}</span></td>` +
      `<td class="ds-value">+${recon.overlapExcessWeight.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2})}<span class="ds-sub">${recon.overlapRows.length.toLocaleString('en-US')} dòng</span></td></tr>`;
    reconHtml += ds_renderDetailRow('overlap', recon.overlapRows, reconDupCols);
    reconHtml += `<tr class="ds-sub-row${doiSoatOpenSections.orphan ? ' ds-open' : ''}" onclick="ds_toggleSection('orphan')"><td class="ds-label">└ Do dòng không thuộc module nào (kể cả T.Phẩm Tạm) <span class="ds-hint">${recon.orphanRows.length ? '(bấm để xem chi tiết)' : ''}</span></td>` +
      `<td class="ds-value">−${recon.orphanWeight.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2})}<span class="ds-sub">${recon.orphanRows.length.toLocaleString('en-US')} dòng</span></td></tr>`;
    reconHtml += ds_renderDetailRow('orphan', recon.orphanRows, reconMissingCols);
    reconHtml += '</tbody></table></div>';

    reconHtml += `</div>`;
  }
  // PC 5.35 — GỘP \"Báo Cáo Tổng Tồn\" + \"Báo Cáo Kho Gửi\" thành 1 BẢNG: dòng \"Kho Gửi\" nằm ngay dưới dòng
  // TỔNG TỒN KHO (vẫn KHÔNG cộng vào tổng); nút \"Nạp dữ liệu\" (dự phòng, chỉ để kiểm tra vì Kho Gửi đã tự cập nhật
  // theo Tồn kho gửi) thu thành 1 icon nhỏ đứng trước chữ \"Kho Gửi\"; xuất Excel dùng 1 file chung (đã gồm Kho Gửi ở
  // cuối sheet \"Tổng Tồn\"). Bảng \"Đối Soát\" dời lên nằm CẠNH bảng này để lấp khoảng trống.
  const khoGuiKey = normalizeLabelForMatch(BAOCAO_KHOGUI_LABEL);
  const khoGuiVal = state.hlsoManualValues[khoGuiKey];
  const khoGuiValNum = (khoGuiVal===undefined) ? null : khoGuiVal;
  const khoGuiCellHtml = (khoGuiValNum!==null ? khoGuiValNum.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2}) : '<span class="cell-empty">—</span>');
  html += `<div id="baocaoTopRow" style="display:flex;flex-direction:row;flex-wrap:wrap;gap:20px;align-items:flex-start;">`;
  html += `<div style="flex:0 1 auto;min-width:300px;display:flex;flex-direction:column;">`;
  html += `<div style="display:flex;align-items:center;gap:12px;margin:4px 0 8px;min-height:33px;flex-wrap:wrap;">
      <h3 style="font-family:var(--display);font-size:15px;font-weight:600;color:var(--text);">Báo Cáo Tổng Tồn</h3>
      <button class="btn btn-ghost" id="btnExportBaoCaoAll" title="Một file Excel chung: tổng tồn + Kho Gửi">${icon('download')} Xuất excel tổng tồn</button>
      <button class="btn btn-ghost" id="btnExportBaoCaoHlso" title="Cùng cấu trúc file, chỉ gồm Tôm HLSO, Tôm Gia Công, Tôm Nhập Khẩu, Tôm Nguyên Liệu, Tôm Giám Sát">${icon('download')} Xuất excel HLSO</button>
    </div>`;
  // Dòng nhập tay "Bột + Sốt + Bếp" — CHỈ HIỂN THỊ tại đây (đã bỏ nút Sửa/Xác nhận): số liệu
  // giờ được nhập/sửa DUY NHẤT tại "Tổng Hợp > Báo cáo ngoài sổ sách" (renderBaoCaoNgoaiSoSach),
  // cùng đọc/ghi 1 giá trị trong state.hlsoManualValues nên sửa bên đó tự động hiện lại ở đây.
  html += `<div class="table-shell baocao-shell" style="max-width:620px;"><div class="table-scroll"><table class="baocao-table"><thead><tr>
      <th></th>
      <th class="num">Tổng trọng lượng (kg)</th>
      <th></th>
    </tr></thead><tbody>`;
  rows.forEach(r=>{
    const cellHtml = (r.weight!==null ? r.weight.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2}) : '<span class="cell-empty">—</span>');
    const actionsHtml = r.manual
      ? `<span class="baocao-manual-hint baocao-goto-tonghop" style="font-size:10.5px;color:var(--frost);white-space:nowrap;cursor:pointer;text-decoration:underline;text-underline-offset:2px;">Sửa tại Tổng Hợp</span>`
      : '';
    html += `<tr>
      <td style="font-weight:700;color:var(--text);">${escapeHtml(r.label)}</td>
      <td class="num">${cellHtml}</td>
      <td style="border-right:none;white-space:nowrap;">${actionsHtml}</td>
    </tr>`;
  });
  html += `</tbody>`;
  html += `<tfoot><tr>
      <td class="total-label">TỔNG TỒN KHO</td>
      <td class="num">${totalWeight!==null ? totalWeight.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2}) : '<span class="cell-empty">—</span>'}</td>
      <td></td>
    </tr>
    <tr class="baocao-khogui-row">
      <td>
        <div style="display:flex;align-items:center;gap:8px;">
          <button type="button" class="btn btn-ghost baocao-icon-btn" id="btnNapKhoGuiM2" aria-label="Nạp dữ liệu Kho Gửi" title="Nạp dữ liệu Kho Gửi từ Tồn kho gửi (dự phòng, dùng để kiểm tra — Kho Gửi đã tự cập nhật khi vào trang Báo Cáo và khi nạp file ở Module 2)">${icon('upload')}</button>
          <div>
            <div class="baocao-khogui-name">${escapeHtml(BAOCAO_KHOGUI_LABEL === 'Báo Cáo Kho Gửi' ? 'Kho Gửi' : BAOCAO_KHOGUI_LABEL)}</div>
            <div id="khoGuiSyncNote" class="baocao-khogui-note"></div>
          </div>
        </div>
      </td>
      <td class="num" id="khoGuiValCell">${khoGuiCellHtml}</td>
      <td style="border-right:none;white-space:nowrap;"><span class="baocao-manual-hint baocao-goto-tonghop" style="font-size:10.5px;color:var(--frost);white-space:nowrap;cursor:pointer;text-decoration:underline;text-underline-offset:2px;">Sửa tại Tổng Hợp</span></td>
    </tr></tfoot>`;
  html += `</table></div></div>`;
  html += `</div>`; // đóng cột bảng Báo Cáo Tổng Tồn (đã gộp Kho Gửi)
  html += reconHtml; // PC 5.35: Đối Soát nằm cạnh bảng tổng tồn
  html += `</div>`; // PC 5.35: đóng #baocaoTopRow

  // ---------- Cột 2 của DÒNG 1: "Bảng Tổng Hợp HLSO" — đặt song song ngay bên phải "Báo Cáo
  // Tổng Tồn" theo đúng yêu cầu bố cục (flex:1 để co giãn lấp phần còn lại của dòng, bảng tự
  // cuộn ngang bên trong nếu không đủ chỗ vì có tới 11 cột thị trường). Nội dung tách thành hàm
  // riêng buildHlsoSummarySectionHtml() để nút "Ẩn/Hiện cột trống" có thể vẽ lại RIÊNG mỗi bảng
  // này (qua refreshHlsoSummarySection()) thay vì vẽ lại toàn bộ trang — tránh giật màn hình/
  // mất vị trí cuộn khi bấm nút. ----------
  html += `<div id="hlsoSummarySection" style="width:100%;min-width:0;">${buildHlsoSummarySectionHtml()}</div>`;

  html += `</div>`; // đóng DÒNG 1 (Báo Cáo Tổng Tồn + Bảng Tổng Hợp HLSO)

  // PC 5.33: "Bảng Tổng Hợp Tùy Chọn" — bảng RIÊNG lọc Size + Đặc Tính đã gộp từ bảng HLSO ngay trên (kl-custom-summary.js).
  html += `<div id="hlsoCustomSummary"></div>`;

  // ---------- DÒNG 2: "Đối Soát: 8 Module vs Load Data" + "Tỉ Lệ % Tồn Kho" + "Tỉ Lệ % Theo
  // Size" nằm song song cùng dòng, theo đúng thứ tự yêu cầu. ----------
  html += `<div style="display:flex;gap:20px;flex-wrap:wrap;align-items:flex-start;margin-top:20px;">`;

  // TỔNG TỒN KHO (dùng lại đúng dữ liệu rows/totalWeight của bảng "Báo Cáo Tổng Tồn" ở trên để
  // luôn khớp số liệu). Sắp xếp giảm dần theo trọng lượng để thấy ngay module nào chiếm dụng
  // nhiều nhất, kèm thanh tỉ lệ trực quan. */
  html += `<div style="flex:0 0 auto;min-width:300px;">`;
  html += `<div style="display:flex;align-items:center;gap:12px;margin:4px 0 8px;min-height:33px;">
      <h3 style="font-family:var(--display);font-size:15px;font-weight:600;color:var(--text);">Tỉ Lệ % Tồn Kho</h3>
    </div>`;
  html += `<div class="table-shell baocao-shell" style="max-width:620px;"><div class="table-scroll"><table class="baocao-table"><thead><tr>
      <th></th>
      <th class="num">Trọng lượng (kg)</th>
      <th class="num">% Chiếm dụng</th>
    </tr></thead><tbody>`;
  const pctRows = rows
    .map(r => ({ ...r, pct: (totalWeight && r.weight!==null) ? (r.weight / totalWeight * 100) : null }))
    .sort((a, b) => (b.weight||0) - (a.weight||0));
  pctRows.forEach(r=>{
    const weightHtml = (r.weight!==null ? r.weight.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2}) : '<span class="cell-empty">—</span>');
    const pctVal = r.pct!==null ? r.pct.toFixed(1) : null;
    const pctHtml = pctVal!==null
      ? `<div style="display:flex;align-items:center;gap:8px;justify-content:flex-end;">
           <div style="flex:1;max-width:64px;height:6px;border-radius:3px;background:var(--border);overflow:hidden;">
             <div style="width:${Math.min(100, r.pct).toFixed(1)}%;height:100%;background:var(--ice, var(--accent));"></div>
           </div>
           <span style="min-width:42px;text-align:right;">${pctVal}%</span>
         </div>`
      : '<span class="cell-empty">—</span>';
    html += `<tr>
      <td style="font-weight:700;color:var(--text);">${escapeHtml(r.label)}</td>
      <td class="num">${weightHtml}</td>
      <td class="num">${pctHtml}</td>
    </tr>`;
  });
  html += `</tbody>`;
  html += `<tfoot><tr>
      <td class="total-label">TỔNG</td>
      <td class="num">${totalWeight!==null ? totalWeight.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2}) : '<span class="cell-empty">—</span>'}</td>
      <td class="num">${totalWeight!==null ? '100.0%' : '<span class="cell-empty">—</span>'}</td>
    </tr></tfoot>`;
  html += `</table></div></div>`;
  html += `</div>`;

  // ---------- Bảng MỚI: "Tỉ Lệ % Theo Size" — CHỈ lấy các dòng Size đang hiển thị trong
  // "Bảng Tổng Hợp HLSO" bên dưới (xem getBaoCaoSizeBreakdown()), khung cố định chiều cao có
  // cuộn riêng + tiêu đề dính (class "baocao-shell-fixed") vì số dòng Size có thể khá nhiều.
  // Có thêm lọc kiểu Excel (chọn nhiều Size cùng lúc) — chọn xong cộng dồn TỔNG % của các Size
  // đã chọn ở dòng "TỔNG ĐÃ CHỌN" riêng (xem renderBaoCaoSizePctBody()/toggleBaoCaoSizePctPopover()
  // phía dưới hàm này — giống hệt cách làm ở module "Tồn Kho Gửi").
  // Nếu "Bảng Tổng Hợp HLSO" chưa có dòng Size nào (chưa đủ dữ liệu) thì ẩn hẳn bảng này. */
  const sizeBreakdown = getBaoCaoSizeBreakdown();
  baocaoSizePctAllRows = sizeBreakdown ? sizeBreakdown.rows : [];
  baocaoSizePctGrandTotal = sizeBreakdown ? sizeBreakdown.total : 0;
  if(baocaoSizePctSelectedSet){
    const validLabels = new Set(baocaoSizePctAllRows.map(r=>r.label));
    Array.from(baocaoSizePctSelectedSet).forEach(l => { if(!validLabels.has(l)) baocaoSizePctSelectedSet.delete(l); });
    if(baocaoSizePctSelectedSet.size === 0) baocaoSizePctSelectedSet = null;
  }
  if(sizeBreakdown && sizeBreakdown.rows.length > 0){
    html += `<div style="flex:0 0 auto;min-width:300px;">`;
    html += `<div style="display:flex;align-items:center;gap:12px;margin:4px 0 8px;min-height:33px;">
        <h3 style="font-family:var(--display);font-size:15px;font-weight:600;color:var(--text);">Tỉ Lệ % Theo Size</h3>
      </div>`;
    html += `<div class="table-shell baocao-shell baocao-shell-fixed" style="max-width:620px;"><div class="table-scroll"><table class="baocao-table"><thead><tr>
        <th>Size <button type="button" id="btnBaoCaoSizePctFilter" title="Lọc theo Size" style="margin-left:4px;cursor:pointer;background:none;border:none;color:inherit;font-size:12px;vertical-align:middle;">⏷</button></th>
        <th class="num">Trọng lượng (kg)</th>
        <th class="num">% Chiếm dụng</th>
      </tr></thead><tbody id="baocaoSizePctTbody"></tbody><tfoot id="baocaoSizePctTfoot"></tfoot></table></div></div>`;
    html += `</div>`;
  }

  html += `</div>`; // đóng DÒNG 2 (Đối Soát + Tỉ Lệ % Tồn Kho + Tỉ Lệ % Theo Size)

  html += `</div>`;

  c.innerHTML = html;

  renderBaoCaoSizePctBody();
  const btnSizePctFilter = document.getElementById('btnBaoCaoSizePctFilter');
  if(btnSizePctFilter) btnSizePctFilter.addEventListener('click', (e)=>{ e.stopPropagation(); toggleBaoCaoSizePctPopover(btnSizePctFilter); });

  const btnExportAll = document.getElementById('btnExportBaoCaoAll');
  if(btnExportAll) btnExportAll.addEventListener('click', exportBaoCaoAllModulesToExcel);
  const btnExportHlso = document.getElementById('btnExportBaoCaoHlso');
  if(btnExportHlso) btnExportHlso.addEventListener('click', exportBaoCaoHlsoToExcel);
  attachHlsoSummarySectionListeners();
  mountHlsoCustomSummary();

  // "Sửa tại Tổng Hợp" (dòng "Bột + Sốt + Bếp" + bảng "Báo Cáo Kho Gửi") — chuyển thẳng sang
  // module "Tổng Hợp" > tab con "Báo cáo ngoài sổ sách" (renderBaoCaoNgoaiSoSach), đúng nơi
  // 2 giá trị này thực sự được nhập/sửa (xem ghi chú ở trên).
  const btnNapKhoGui = document.getElementById('btnNapKhoGuiM2');
  if(btnNapKhoGui) btnNapKhoGui.addEventListener('click', ()=>napKhoGuiTuModule2(btnNapKhoGui));
  autoSyncKhoGui(false);   // PC 3.72: vào trang Báo Cáo → tự lấy số Kho Gửi mới nhất từ Tồn kho gửi

  document.querySelectorAll('#baocaoWrap .baocao-goto-tonghop').forEach(el=>{
    el.addEventListener('click', ()=>{
      state.activeModule = 'tonghop';
      state.tonghopSubTab = 'ngoaisoSach';
      renderAll();
    });
  });

  // Đã bỏ cụm nút Sửa/Xác nhận của dòng "Bột + Sốt + Bếp" và bảng "Báo Cáo Kho Gửi" tại đây —
  // sửa số liệu duy nhất tại "Tổng Hợp > Báo cáo ngoài sổ sách" (renderBaoCaoNgoaiSoSach), nên
  // không còn input/nút nào để gắn sự kiện ở khu vực "Báo Cáo Tổng Tồn" nữa.
}

// Xuất Excel riêng cho "Bảng Tổng Hợp HLSO" — CHỈ lấy đúng dữ liệu đang hiển thị trong bảng
// đó (cột Tiêu Đề + các cột thị trường cố định + cột Tổng), không kéo theo dữ liệu của bảng
// "Báo Cáo Tổng Tồn" hay bảng Đối Soát bên cạnh. Cột "Tổng" lấy số thật khi tên hàng khớp
// với 1 module trên "Báo Cáo Tổng Tồn" (xem findBaoCaoTotalWeightByLabel), giống hệt trên
// giao diện — các hàng chưa có module tương ứng vẫn để trống.
async function exportHlsoSummaryToExcel(){
  const reportDateStr = await promptForReportDate();
  if(!reportDateStr) return; // người dùng bấm Hủy — không xuất file
  const wb = buildHlsoSummaryWorkbook(reportDateStr);
  await writeWorkbookWithFreezeTopRow(wb, `BangTongHopHLSO_${exportFileStamp()}.xlsx`);
}
// Dựng workbook "Bảng Tổng Hợp HLSO" — dùng chung cho nút Xuất Excel trên PC và file báo cáo gửi
// lên điện thoại (buildPhoneReportSnapshot).
function buildHlsoSummaryWorkbook(reportDateStr, custom){
  // PC 5.42: custom = { columns:[tên cột], rows:[{label, kind, cells:[số|null…], total}] } → dựng CÙNG file nhưng chỉ phần đang chọn
  // ở "Bảng Tổng Hợp Tùy Chọn" (không bỏ cột trống, cột tổng đổi tên "Tổng chọn", dòng tổng "TỔNG CHỌN"). Không truyền = bảng HLSO đầy đủ như cũ.
  const rows = custom ? null : getHlsoSummaryRowsInStock(); // PC 2.11: bỏ hàng không còn tồn
  const srcColumns = custom ? custom.columns : HLSO_SUMMARY_COLUMNS;

  // Tính trước toàn bộ giá trị từng ô (theo cột thị trường gốc), đồng thời xác định cột nào
  // CÓ ít nhất 1 giá trị thực trong cả bảng — cột nào TOÀN BỘ đều trống ("-") sẽ bị loại khỏi
  // file Excel xuất ra theo yêu cầu, không xuất cột rỗng vô ích.
  const rawRows = custom
    ? custom.rows.map(r => ({
        rowDesc: { displayLabel: r.label, kind: r.kind },
        isGroupTitle: r.kind === 'group-title',
        totalVal: (typeof r.total === 'number' && isFinite(r.total)) ? r.total : null,
        groupVals: r.cells.map(v => (typeof v === 'number' && isFinite(v)) ? v : null),
      }))
    : rows.map(rowDesc => {
        const isGroupTitle = rowDesc.kind === 'group-title';
        const totalVal = getHlsoSummaryRowTotal(rowDesc);
        const groupVals = HLSO_SUMMARY_COLUMNS.map(marketGroup => getHlsoSummaryRowGroupWeight(rowDesc, marketGroup));
        return { rowDesc, isGroupTitle, totalVal, groupVals };
      });

  const colHasValue = srcColumns.map((_, gi) =>
    custom ? true : rawRows.some(({ groupVals }) => {
      const v = groupVals[gi];
      return v !== null && v !== undefined && !isNaN(v);
    })
  );
  const keptColIdx = srcColumns.map((_, gi) => gi).filter(gi => colHasValue[gi]);
  const keptColumns = keptColIdx.map(gi => srcColumns[gi]);

  // Tính trước TỔNG TỒN — dùng cho dòng "TỔNG TỒN" (nay xuất hiện ở CẢ 2 nơi trong file Excel:
  // ngay sau dòng tiêu đề VÀ ở cuối bảng như trước). Ô tiêu đề cột "Tổng" GIỮ NGUYÊN chữ "Tổng"
  // (khôi phục lại — không còn bị thay bằng số như trước; giao diện trên màn hình không đổi,
  // 2 thay đổi này CHỈ áp dụng cho file Excel xuất ra).
  let grandTotal = 0, grandTotalHas = false;
  rawRows.forEach(({ isGroupTitle, totalVal })=>{
    if(!isGroupTitle && totalVal!==null && totalVal!==undefined && !isNaN(totalVal)){ grandTotal += totalVal; grandTotalHas = true; }
  });

  const header = ['Size', ...keptColumns, custom ? 'Tổng chọn' : 'Tổng', 'Size'];
  // Dòng tiêu đề "Ngày báo cáo" chèn TRÊN CÙNG (row 0) — theo đúng ngày người dùng chọn ở hộp
  // thoại phía trên, không tự lấy ngày hiện tại. Header dữ liệu dịch xuống row 1.
  const aoa = [[`Ngày báo cáo: ${reportDateStr}`, ...keptColumns.map(()=>''), '', '']];
  const titleRowIdx = 0;
  aoa.push(header);
  // Ghi lại LOẠI của từng dòng song song với aoa (cùng chỉ số dòng) để tô màu/in đậm riêng
  // sau khi styleExportedSheet() áp style chung (viền, căn lề, định dạng số...) — 'header' =
  // dòng tiêu đề cột, 'static' = 3 dòng đơn lẻ (Thành phẩm tạm/chính, Bột+Sốt+Bếp — dữ liệu
  // RIÊNG BIỆT, không thuộc cụm Tôm nào), 'group-title' = dòng tiêu đề 1 cụm (VD "TÔM HLSO"),
  // 'group-size' = dòng Size thường bên trong 1 cụm, 'total' = dòng "TỔNG TỒN" (đầu VÀ cuối bảng).
  const rowKinds = ['title', 'header'];
  const colTotals = keptColumns.map(()=>({ sum: 0, has: false }));
  rawRows.forEach(({ rowDesc, isGroupTitle, totalVal, groupVals })=>{
    const keptVals = keptColIdx.map((gi, ki)=>{
      const gVal = groupVals[gi];
      if(!isGroupTitle && gVal!==null && gVal!==undefined && !isNaN(gVal)){ colTotals[ki].sum += gVal; colTotals[ki].has = true; }
      return (gVal!==null && gVal!==undefined) ? gVal : '';
    });
    aoa.push([rowDesc.displayLabel, ...keptVals, (totalVal!==null && totalVal!==undefined) ? totalVal : '', rowDesc.displayLabel]);
    rowKinds.push(rowDesc.kind); // 'static' | 'group-title' | 'group-size'
  });

  const totalRowValues = [custom ? 'TỔNG CHỌN' : 'TỔNG TỒN', ...colTotals.map(ct => ct.has ? ct.sum : ''), grandTotalHas ? grandTotal : '', ''];
  // Chèn thêm 1 dòng "TỔNG TỒN" NGAY SAU dòng tiêu đề cột (đưa giá trị tổng lên trên) — dòng
  // "TỔNG TỒN" gốc ở cuối bảng vẫn giữ nguyên, không bị thay thế/xoá. Chèn ở index 2 (sau dòng
  // "Ngày báo cáo" ở index 0 và dòng tiêu đề cột ở index 1).
  aoa.splice(2, 0, totalRowValues.slice());
  rowKinds.splice(2, 0, 'total');
  aoa.push(totalRowValues.slice());
  rowKinds.push('total');

  const ws = XLSX.utils.aoa_to_sheet(aoa);
  ws['!cols'] = computeAutoColWidths(aoa);
  const displayColsForStyle = header.map(h => ({ label: h, virtual: h !== 'SIZE' }));
  const totalRowIdx = aoa.length - 1;
  const headerRowIdx = 1;
  styleExportedSheet(ws, displayColsForStyle, totalRowIdx, { headerRowIdx });
  // styleExportedSheet() ở trên chỉ tự động tô style cho dòng tiêu đề cột (headerRowIdx) và
  // dòng CUỐI (totalRowIdx) — cần tô thủ công thêm 2 chỗ: dòng "Ngày báo cáo" (title, không tô
  // nền, chỉ đậm + căn giữa, hợp nhất toàn bộ chiều rộng bảng) và dòng "TỔNG TỒN" vừa chèn thêm
  // ở TRÊN (dòng chỉ số 2), dùng đúng style giống hệt dòng Tổng ở cuối bảng cho đồng nhất.
  {
    // Tiêu đề "Ngày báo cáo" CĂN TRÁI (theo đúng yêu cầu) — trước đó đã lỡ đổi thành căn giữa,
    // nay đổi lại. Vẫn gán style cho TOÀN BỘ các ô trong vùng gộp (không chỉ ô đầu) để tránh ô
    // nào đó lỡ giữ lại style căn lề khác từ styleExportedSheet() ở trên.
    for(let c = 0; c < displayColsForStyle.length; c++){
      const titleAddr = XLSX.utils.encode_cell({ r: titleRowIdx, c });
      if(!ws[titleAddr]) ws[titleAddr] = { t: 's', v: '' };
      ws[titleAddr].s = {
        font: { name: 'Times New Roman', sz: 14, bold: true },
        alignment: { horizontal: 'left', vertical: 'center' },
        border: XLSX_THIN_BORDER,
      };
    }
    ws['!merges'] = ws['!merges'] || [];
    ws['!merges'].push({ s: { r: titleRowIdx, c: 0 }, e: { r: titleRowIdx, c: displayColsForStyle.length - 1 } });

    const numFmtTop = '#,##0.00';
    for(let c = 0; c < displayColsForStyle.length; c++){
      const addr = XLSX.utils.encode_cell({ r: 2, c });
      if(!ws[addr]) ws[addr] = { t: 's', v: '' };
      const isNumericColumn = c > 0 && c < displayColsForStyle.length - 1;
      ws[addr].s = {
        font: { name: 'Times New Roman', sz: 14, bold: true },
        fill: { patternType: 'solid', fgColor: { rgb: 'DDEBF7' } },
        alignment: { horizontal: isNumericColumn ? 'right' : 'left', vertical: 'center' },
        border: { ...XLSX_THIN_BORDER, top: { style: 'medium', color: { rgb: '000000' } } },
      };
      if(isNumericColumn && typeof ws[addr].v === 'number') ws[addr].z = numFmtTop;
    }
  }

  // ---------- Tô màu riêng + in đậm cho từng loại dòng, ĐÈ LÊN style chung ở trên ----------
  // Màu "static" (Thành phẩm tạm/chính, Bột+Sốt+Bếp): vàng nhạt — đánh dấu đây là dữ liệu
  // ĐƠN LẺ, RIÊNG BIỆT, không thuộc cụm Tôm nào bên dưới.
  const STATIC_ROW_FILL = 'FCE9B0';
  // Màu "group-title" (TÔM HLSO / TÔM GIA CÔNG / TÔM NHẬP KHẨU / TÔM NGUYÊN LIỆU / TÔM GIÁM
  // SÁT): xanh lá nhạt — đánh dấu đây là dòng TIÊU ĐỀ của 1 cụm, phân biệt với các dòng Size
  // dữ liệu thường ngay bên dưới nó (không tô màu, giữ nền trắng mặc định).
  const GROUP_TITLE_ROW_FILL = 'D6EAD3';
  const lastColIdx = displayColsForStyle.length - 2; // cột "Tổng" (cột kề trước cột "Size" cuối cùng)

  for(let r = 1; r < totalRowIdx; r++){
    const kind = rowKinds[r];
    if(kind !== 'static' && kind !== 'group-title') continue;
    const fill = kind === 'static' ? STATIC_ROW_FILL : GROUP_TITLE_ROW_FILL;
    for(let c = 0; c < displayColsForStyle.length; c++){
      const addr = XLSX.utils.encode_cell({ r, c });
      if(!ws[addr]) ws[addr] = { t: 's', v: '' };
      const isNumericColumn = c > 0 && c < displayColsForStyle.length - 1; // trừ 2 cột "Size" (đầu/cuối) đều là số
      ws[addr].s = {
        font: { name: 'Times New Roman', sz: 14, bold: true },
        fill: { patternType: 'solid', fgColor: { rgb: fill } },
        alignment: { horizontal: isNumericColumn ? 'right' : 'left', vertical: 'center' },
        border: XLSX_THIN_BORDER,
      };
      if(isNumericColumn && typeof ws[addr].v === 'number') ws[addr].z = '#,##0.00';
    }
  }

  // In đậm CỘT "Tổng" (cột cuối) cho TẤT CẢ các dòng dữ liệu — đây là số liệu tính tổng của
  // từng dòng, cần nổi bật hơn các cột thị trường chi tiết bên cạnh. Dòng static/group-title
  // đã được in đậm cả dòng ở trên rồi nên không bị ảnh hưởng gì thêm; dòng group-size (Size
  // thường) trước đó không đậm — giờ riêng cột "Tổng" của nó sẽ đậm lên.
  for(let r = 1; r < totalRowIdx; r++){
    const addr = XLSX.utils.encode_cell({ r, c: lastColIdx });
    if(!ws[addr]) continue;
    const prevStyle = ws[addr].s || {};
    ws[addr].s = { ...prevStyle, font: { ...(prevStyle.font||{ name: 'Times New Roman', sz: 14 }), bold: true } };
  }

  const wb = XLSX.utils.book_new();
  XLSX.utils.book_append_sheet(wb, ws, (custom ? 'Bang Tong Hop Tuy Chon' : 'Bang Tong Hop HLSO').slice(0, 31));
  return wb;
}

/* ================= RENDER: ĐẶC TÍNH ================= */
// Tổng hợp TOÀN BỘ giá trị của cột "Đặc tính" GỘP TỪ 5 MODULE CHÍNH: HLSO, Tôm Gia Công, Tôm
// Nhập Khẩu, Tôm Nguyên Liệu, Tôm Giám Sát (dùng chung danh sách module với HLSO_SIZE_GROUPS
// — Bảng Tổng Hợp HLSO) — KHÔNG còn lấy toàn bộ dữ liệu Load Data như trước. Mỗi giá trị duy
// nhất thành 1 hàng, kèm số dòng và tổng "Tổng KL (kg)" (= Tồn cuối × Trọng lượng), sắp xếp
// theo bảng chữ cái. Nếu 1 dòng dữ liệu vô tình thuộc >1 trong 5 module cùng lúc, chỉ tính 1
// lần (loại trùng theo chỉ số dòng gốc trong state.rows).
function getDacTinhBreakdown(){
  const ci = (state.headers.length) ? state.headers.findIndex(h => headersFuzzyMatch(h, 'Đặc tính')) : -1;
  const basis = getWeightBasisAllRows();

  // ---------- Bước 1: tính các đặc tính "SỐNG" — đang thực sự có trong dữ liệu Load Data hiện
  // tại (nếu có dữ liệu và tìm thấy cột "Đặc tính") ----------
  const liveMap = new Map(); // label gốc -> {label, count, weight, hasWeight}
  if(state.rows.length && ci !== -1){
    const moduleIds = HLSO_SIZE_GROUPS.map(g => g.moduleId);
    const seen = new Set();
    const combinedRows = [];
    moduleIds.forEach(modId=>{
      filterModuleBaseRowsCached(modId).forEach(({i, row})=>{
        if(seen.has(i)) return;
        seen.add(i);
        combinedRows.push(row);
      });
    });
    combinedRows.forEach(row=>{
      const label = String(row[ci] ?? '').trim();
      if(label === '') return;
      if(!liveMap.has(label)) liveMap.set(label, { label, count: 0, weight: 0, hasWeight: false });
      const entry = liveMap.get(label);
      entry.count += 1;
      if(basis){
        const w = filterModuleRowTotalWeight(row, basis);
        if(w !== null){ entry.weight += w; entry.hasWeight = true; }
      }
    });
  }

  // ---------- Bước 2: cộng dồn (KHÔNG BAO GIỜ xoá) vào "sổ" state.dactinhKnownLabels — mọi
  // đặc tính từng xuất hiện, dù dữ liệu Load Data sau này bị xoá/đổi/không còn nhãn đó nữa thì
  // nhãn vẫn được giữ lại vĩnh viễn ở đây. Chỉ ghi lại (persist) khi thực sự có nhãn mới. ----------
  let knownChanged = false;
  liveMap.forEach((_, label)=>{
    const key = normalizeLabelForMatch(label);
    if(!state.dactinhKnownLabels[key]){
      state.dactinhKnownLabels[key] = label;
      knownChanged = true;
    }
  });
  if(knownChanged) persistDacTinh();

  // ---------- Bước 3: kết quả hiển thị = TOÀN BỘ nhãn đã từng biết (kể cả nhãn đã gộp vào 1
  // nhóm ở "Gộp Đặc Tính" từ trước) HỢP với dữ liệu sống hiện tại. Nhãn nào không còn trong dữ
  // liệu Load Data hiện tại thì hiển thị với số dòng/khối lượng = 0 (đánh dấu present:false) —
  // vẫn giữ nguyên trạng thái đã gộp hoặc chưa gộp của nó. ----------
  const resultMap = new Map(liveMap); // copy, key = label gốc
  Object.entries(state.dactinhKnownLabels).forEach(([key, knownLabel])=>{
    const alreadyLive = Array.from(liveMap.keys()).some(l => normalizeLabelForMatch(l) === key);
    if(!alreadyLive){
      resultMap.set(knownLabel, { label: knownLabel, count: 0, weight: 0, hasWeight: false, present: false });
    }
  });
  resultMap.forEach(entry=>{ if(entry.present === undefined) entry.present = true; });

  return Array.from(resultMap.values())
    .sort((a,b)=> a.label.localeCompare(b.label, 'vi', { numeric: true, sensitivity: 'base' }));
}

// Với 1 nhãn "Đặc Tính", trả về tên nhóm (1 trong 11 nhóm HLSO_SUMMARY_COLUMNS) đã được gán ở
// bảng "Gộp Đặc Tính", hoặc null nếu chưa gán.
function getDacTinhAssignedGroup(label){
  const key = normalizeLabelForMatch(label);
  const entry = state.dactinhGroupAssign[key];
  return entry ? entry.group : null;
}
// Danh sách nhãn "Đặc Tính" GỐC (giữ nguyên hoa/thường) đã gán vào ĐÚNG 1 nhóm, sắp xếp theo
// bảng chữ cái — dùng để vẽ bảng "Gộp Đặc Tính" (11 nhóm cố định). Trả về {key, label}.
function getDacTinhGroupMembers(groupName){
  return Object.entries(state.dactinhGroupAssign)
    .filter(([, entry]) => entry.group === groupName)
    .map(([key, entry]) => ({ key, label: entry.label || key }))
    .sort((a,b)=> a.label.localeCompare(b.label, 'vi', { numeric: true, sensitivity: 'base' }));
}

function renderDacTinh(){
  const c = document.getElementById('content');
  const hasData = state.rows.length > 0 && state.headers.length > 0;

  // Lưu lại vị trí cuộn của CẢ 2 bảng (Đặc Tính bên trái + Gộp Đặc Tính bên phải) TRƯỚC khi vẽ
  // lại — tránh bị nhảy về đầu bảng (mất vị trí đang xem, VD đang tick checkbox ở dòng 50 thì
  // bị kéo tuột về dòng 1) mỗi khi tick checkbox / gộp nhóm / tìm kiếm (đều gọi lại
  // renderDacTinh() để cập nhật UI, nhưng thay c.innerHTML luôn tạo phần tử .table-scroll MỚI
  // nên mất vị trí cuộn cũ nếu không khôi phục — giống cơ chế đã áp dụng ở renderFilterModule()).
  const prevScrollEls = Array.from(document.querySelectorAll('#dactinhWrap .table-scroll'));
  const prevScrolls = prevScrollEls.map(el => ({ top: el.scrollTop, left: el.scrollLeft }));

  // Module "Đặc Tính" có "sổ" riêng (state.dactinhKnownLabels + state.dactinhGroupAssign) được
  // lưu VĨNH VIỄN, không phụ thuộc dữ liệu Load Data hiện tại — nên chỉ hiện màn hình "Chưa có
  // dữ liệu" khi CHƯA TỪNG có đặc tính nào được ghi nhận (sổ trống) VÀ dữ liệu hiện tại cũng
  // trống. Nếu đã từng có (dù dữ liệu Load Data bị xoá sau đó), vẫn hiển thị bảng như thường.
  const hasKnownDacTinh = Object.keys(state.dactinhKnownLabels).length > 0;

  if(!hasData && !hasKnownDacTinh){
    c.innerHTML = `
      <div class="placeholder">
        <div class="ph-ic">${icon('stack','ic')}</div>
        <h2>Chưa có dữ liệu</h2>
        <p>Vào mục <b style="color:var(--frost)">Load Data</b> để nạp file Excel tồn kho trước — bảng Đặc Tính sẽ tự động tổng hợp từ dữ liệu đó.</p>
        <div class="tag" id="btnGoLoadDataDacTinh" style="cursor:pointer;">ĐI TỚI LOAD DATA</div>
      </div>
    `;
    const goBtn = document.getElementById('btnGoLoadDataDacTinh');
    if(goBtn) goBtn.addEventListener('click', ()=>{
      state.activeModule = 'loaddata';
      renderAll();
    });
    return;
  }

  const ci = hasData ? state.headers.findIndex(h => headersFuzzyMatch(h, 'Đặc tính')) : -1;
  const rows = getDacTinhBreakdown();
  // Dòng đã được gán vào 1 nhóm rồi thì KHÔNG được chọn lại (checkbox disabled) — nếu lỡ đang
  // nằm trong state.dactinhSelected (VD do dữ liệu đổi) thì dọn ra luôn để move-bar không lệch.
  rows.forEach((r,i)=>{ if(getDacTinhAssignedGroup(r.label)) state.dactinhSelected.delete(i); });
  const selCount = state.dactinhSelected.size;
  // Số đặc tính CHƯA gộp vào nhóm nào (tính trên toàn bộ danh sách, không phụ thuộc ô tìm kiếm).
  const unassignedCount = rows.filter(r => !getDacTinhAssignedGroup(r.label)).length;

  let html = `<div class="data-wrap show" id="dactinhWrap">`;
  if(hasData){
    html += `<div class="diag-note">${icon('search')} Tổng hợp theo cột "Đặc tính", GỘP TỪ 5 module: <b>HLSO, Tôm Gia Công, Tôm Nhập Khẩu, Tôm Nguyên Liệu, Tôm Giám Sát</b>, tính trên dữ liệu đã nạp ở <b>Load Data</b>: <b>${state.fileName || 'dữ liệu đã lưu'}</b>. Các đặc tính đã ghi nhận trước đó luôn được GIỮ LẠI dù dữ liệu nguồn có thay đổi.</div>`;
  } else {
    html += `<div class="diag-note">${icon('search')} Dữ liệu Load Data hiện đang trống — danh sách dưới đây là các đặc tính (đã gộp hoặc chưa gộp) được LƯU LẠI từ trước, không bị mất khi xoá dữ liệu ở module khác. Nạp lại dữ liệu ở <b>Load Data</b> để tự động cập nhật, bổ sung thêm đặc tính mới nếu có.</div>`;
  }

  // Lưu ý: dòng "Không tìm thấy cột" (nếu có) và bảng dữ liệu KHÔNG loại trừ nhau — dữ liệu đã
  // lưu trước đó (state.dactinhKnownLabels) vẫn phải hiển thị đầy đủ ở bảng bên dưới ngay cả
  // khi file vừa nạp không có cột "Đặc tính".
  if(hasData && ci === -1){
    html += `<div class="diag-note warn-note">${icon('search')} Không tìm thấy cột "Đặc tính" trong dữ liệu vừa nạp${rows.length ? ' — danh sách dưới đây vẫn giữ các đặc tính đã ghi nhận trước đó' : ''}.</div>`;
  }
  if(!rows.length){
    html += `<div class="diag-note warn-note">${icon('search')} Cột "Đặc tính" chưa có giá trị nào trong dữ liệu.</div>`;
  } else {
    // Thanh chuyển (move-bar) đặt Ở TRÊN CÙNG, ngang toàn bộ chiều rộng — nằm TRƯỚC khối 2
    // bảng song song, để "Đặc Tính" và "Gộp Đặc Tính" bắt đầu ngang hàng nhau (không bị lệch
    // xuống vì các dòng phụ như "Tổng Số Dòng"/tìm kiếm chỉ thuộc riêng bảng trái).
    // Chọn dòng (checkbox) ở bảng trái → chọn 1 trong 11 nhóm → bấm Chuyển. Dòng đã chuyển vẫn
    // hiển thị nguyên trong danh sách, chỉ làm MỜ đi và không cho chọn lại (xem CSS
    // .dactinh-assigned-row / checkbox disabled bên dưới).
    if(!m1CanWrite){
      // PC 7.54: người dùng cấp 1 / 2 chỉ xem bảng Đặc Tính — không gộp / bỏ gộp / xóa
      html += `<div class="move-bar" id="dactinhMoveBar"><span class="move-count" style="opacity:.75;">🔒 Chỉ vai trò Quản lý trở lên được sửa Đặc Tính (gộp nhóm / bỏ gộp / xóa)</span></div>`;
    } else html += `<div class="move-bar" id="dactinhMoveBar">
        <span class="move-count">${selCount>0 ? `${selCount.toLocaleString('en-US')} đặc tính đã chọn` : 'Tick chọn dòng để gộp vào 1 nhóm'}</span>
        <select id="dactinhGroupTarget" ${selCount===0?'disabled':''}>
          ${HLSO_SUMMARY_COLUMNS.map(g=>`<option value="${escapeHtml(g)}">${escapeHtml(g)}</option>`).join('')}
        </select>
        <button class="btn btn-primary" id="dactinhBtnMove" ${selCount===0?'disabled':''}>⇄ Gộp vào nhóm đã chọn</button>
        <button class="btn btn-ghost" id="dactinhBtnClearSel" ${selCount===0?'disabled':''}>Bỏ chọn</button>
        <span style="flex:1;"></span>
        <button class="btn btn-danger" id="dactinhBtnClearSaved" title="Xoá toàn bộ đặc tính đã ghi nhận VÀ toàn bộ đặc tính đã gộp nhóm — dùng khi muốn bắt đầu lại từ đầu">${icon('trash')} Xoá dữ liệu đã lưu</button>
      </div>`;

    // 2 bảng nằm SONG SONG (flex ngang), CĂN GIỮA cả cụm trong khung (justify-content:center)
    // thay vì dồn về bên trái/phải, và CO DÃN theo chiều rộng màn hình (flex:1 thay vì flex:0
    // + max-width nhỏ cố định như trước) — trên màn hình rộng 2 bảng sẽ nới rộng ra cân đối,
    // trên màn hình hẹp thì tự động xuống dòng (flex-wrap) và co lại vừa khung.
    // Ghi chú: min-width của 2 cột bên dưới (240px + 260px + gap 20px = 520px) đặt THẤP hơn
    // mức "lý tưởng" cũ (480/540px) để 2 bảng vẫn đứng SONG SONG được ngay cả khi cửa sổ ứng
    // dụng chỉ rộng ở mức tối thiểu (~900px, xem minWidth trong main.js) — trừ sidebar (232px)
    // + padding 2 bên (68px) chỉ còn khoảng 585-600px cho khối này; min-width cũ (680px) sẽ
    // luôn bị bể xuống dòng ở cỡ cửa sổ thông thường.
    html += `<div style="display:flex;gap:20px;flex-wrap:wrap;align-items:flex-start;justify-content:center;width:100%;">`;

    // ---------- Bảng trái: Đặc Tính ----------
    // Header đồng bộ với bảng phải: tiêu đề + "Tổng số dòng" + thanh tìm kiếm + nút xuất Excel
    // đều nằm CHUNG 1 HÀNG, cùng chiều cao với header bảng phải — để cả 2 bảng bắt đầu ngang
    // hàng nhau, không bị lệch xuống do các dòng phụ riêng của bảng trái.
    html += `<div style="flex:1 1 420px;min-width:240px;max-width:820px;">`;
    html += `<div style="display:flex;align-items:center;gap:10px;margin:4px 0 8px;flex-wrap:wrap;">
        <h3 style="font-family:var(--display);font-size:15px;font-weight:600;color:var(--text);white-space:nowrap;">Đặc Tính</h3>
        <span style="font-family:var(--mono);font-size:10.5px;color:var(--text-muted);white-space:nowrap;">
          Tổng: <span style="color:var(--frost);font-weight:600;">${rows.length.toLocaleString('en-US')}</span>
        </span>
        <span style="font-family:var(--mono);font-size:10.5px;color:var(--text-muted);white-space:nowrap;" title="Số đặc tính chưa được gộp vào nhóm nào ở bảng Gộp Đặc Tính">
          Chưa gộp: <span id="dactinhUnassignedCount" style="color:${unassignedCount ? 'var(--accent, #f5a623)' : 'var(--frost)'};font-weight:600;">${unassignedCount.toLocaleString('en-US')}</span>
        </span>
        <div class="search-box" style="flex:1;min-width:140px;margin:0;">
          ${icon('search')}
          <input type="text" id="dactinhSearchInput" placeholder="Tìm nhanh theo đặc tính…" value="${state.dactinhSearch.replace(/"/g,'&quot;')}">
        </div>
        <button class="btn btn-ghost" id="btnExportDacTinh" style="white-space:nowrap;">${icon('download')} Xuất Excel</button>
      </div>`;

    // Lọc theo từ khóa tìm kiếm + áp hướng sắp xếp (A→Z / Z→A) — GIỮ NGUYÊN chỉ số gốc trong
    // mảng `rows` (từ getDacTinhBreakdown) cho từng dòng hiển thị, để checkbox/move-bar/gán
    // nhóm vẫn tham chiếu đúng dòng dù danh sách hiển thị đã lọc/sắp xếp lại.
    // PC 7.39: tìm kiểu Module 2 — không phân biệt dấu / hoa thường / khoảng trắng; nhiều từ thì mỗi từ tìm riêng (VÀ).
    const searchTerm = fuzzyNormalize(state.dactinhSearch).trim();
    let displayIdxs = rows.map((_, i) => i);
    if(searchTerm){
      const stWords = searchTerm.split(' ').filter(Boolean), stCompact = noSpace(searchTerm);
      displayIdxs = displayIdxs.filter(i => {
        const lb = fuzzyNormalize(rows[i].label);
        return noSpace(lb).includes(stCompact) || stWords.every(w => lb.includes(w));
      });
    }
    if(state.dactinhSortDir === 'desc') displayIdxs.reverse();
    // ƯU TIÊN các đặc tính CHƯA gộp lên ĐẦU danh sách, đặc tính ĐÃ gộp xuống cuối — vẫn giữ
    // nguyên thứ tự A→Z/Z→A đã áp dụng ở trên BÊN TRONG từng nhóm (chưa gộp / đã gộp) nhờ sort
    // ổn định (giữ lại vị trí tương đối `pos` ban đầu làm tiêu chí phụ khi 2 dòng cùng trạng
    // thái gộp). Giống hệt cách bảng "Đặc Tính" ở module 2 (Tồn Kho Gửi) đang làm.
    displayIdxs = displayIdxs
      .map((idx, pos) => ({ idx, pos, assigned: !!getDacTinhAssignedGroup(rows[idx].label) }))
      .sort((a,b) => (a.assigned === b.assigned) ? (a.pos - b.pos) : (a.assigned ? 1 : -1))
      .map(r => r.idx);

    html += `<div class="table-shell" style="max-width:100%;"><div class="table-scroll table-wrap"><table class="baocao-table"><thead><tr>
        <th class="chk-col"><input type="checkbox" id="dactinhChkAll"></th>
        <th class="sortable sorted" id="dactinhSortToggle" style="cursor:pointer;" title="Bấm để sắp xếp">
          <span class="th-label">Đặc Tính</span>
        </th>
      </tr></thead><tbody>`;
    if(!displayIdxs.length){
      html += `<tr><td colspan="2"><div class="empty-state">Không tìm thấy đặc tính nào khớp với từ khóa tìm kiếm.</div></td></tr>`;
    }
    displayIdxs.forEach(i=>{
      const r = rows[i];
      const assignedGroup = getDacTinhAssignedGroup(r.label);
      html += `<tr${assignedGroup ? ' class="dactinh-assigned-row"' : ''}>
        <td class="chk-col"><input type="checkbox" class="dactinhRowChk" data-idx="${i}"
          ${state.dactinhSelected.has(i)?'checked':''} ${(assignedGroup || !m1CanWrite)?'disabled':''}></td>
        <td style="font-weight:700;color:${assignedGroup?'var(--text-faint)':'var(--text)'};">
          ${escapeHtml(r.label)}
          ${assignedGroup ? `<span class="manual-note-badge" style="margin-left:6px;">→ ${escapeHtml(assignedGroup)}</span>` : ''}
          ${r.present === false ? `<span class="manual-note-badge" style="margin-left:6px;color:var(--text-faint);border-color:var(--border-strong);" title="Đặc tính này đã được lưu lại trước đó, hiện không còn trong dữ liệu Load Data đang nạp">đã lưu, hiện không có trong dữ liệu</span>` : ''}
        </td>
      </tr>`;
    });
    html += `</tbody></table></div></div>`;
    html += `</div>`;

    // ---------- Bảng phải: Gộp Đặc Tính (11 nhóm cố định) ----------
    // Header cùng chiều cao (cùng gap/margin/font) với header bảng trái để 2 bảng thẳng hàng.
    html += `<div style="flex:1 1 460px;min-width:260px;max-width:900px;">`;
    html += `<div style="display:flex;align-items:center;gap:10px;margin:4px 0 8px;flex-wrap:wrap;">
        <h3 style="font-family:var(--display);font-size:15px;font-weight:600;color:var(--text);white-space:nowrap;">Gộp Đặc Tính</h3>
        <span style="flex:1;"></span>
        <button class="btn btn-ghost" id="btnExportDacTinhGop" style="white-space:nowrap;">${icon('download')} Xuất Excel</button>
      </div>`;
    html += `<div class="table-shell" style="max-width:100%;"><div class="table-scroll table-wrap"><table class="baocao-table"><thead><tr>
        <th>Nhóm</th>
        <th>Đặc Tính đã gộp</th>
      </tr></thead><tbody>`;
    HLSO_SUMMARY_COLUMNS.forEach(groupName=>{
      const members = getDacTinhGroupMembers(groupName);
      html += `<tr>
        <td style="font-weight:700;color:var(--frost);white-space:nowrap;">${escapeHtml(groupName)}</td>
        <td>${members.length
          ? members.map(m=> m1CanWrite
              ? `<span class="manual-item-badge dactinh-unassign-badge" data-key="${escapeHtml(m.key)}" title="Bấm để bỏ gộp">${escapeHtml(m.label)} ⇄</span>`
              : `<span class="manual-item-badge" style="cursor:default;" data-key="${escapeHtml(m.key)}">${escapeHtml(m.label)}</span>`).join(' ')
          : '<span class="cell-empty">—</span>'}</td>
      </tr>`;
    });
    html += `</tbody></table></div></div>`;
    html += `</div>`;

    html += `</div>`;
  }

  html += `</div>`;
  c.innerHTML = html;

  // Khôi phục vị trí cuộn đã lưu ở trên cho từng bảng, theo ĐÚNG THỨ TỰ xuất hiện trong DOM
  // (bảng "Đặc Tính" trái trước, bảng "Gộp Đặc Tính" phải sau — không đổi thứ tự khi vẽ).
  const newScrollEls = document.querySelectorAll('#dactinhWrap .table-scroll');
  newScrollEls.forEach((el, idx)=>{
    const prev = prevScrolls[idx];
    if(prev && (prev.top || prev.left)){
      el.scrollTop = prev.top;
      el.scrollLeft = prev.left;
    }
  });

  const btnExport = document.getElementById('btnExportDacTinh');
  if(btnExport) btnExport.addEventListener('click', exportDacTinhToExcel);
  const btnExportGop = document.getElementById('btnExportDacTinhGop');
  if(btnExportGop) btnExportGop.addEventListener('click', exportDacTinhGopToExcel);

  // ---------- Thanh tìm kiếm thông minh ----------
  const dactinhSearchInput = document.getElementById('dactinhSearchInput');
  if(dactinhSearchInput){
    // renderDacTinh() vẽ lại cả khối (kể cả ô này) → KLAType.keepFocus trả lại đúng ô, đúng vị trí
    // con trỏ (không nhảy về cuối), giữ vị trí cuộn, không mất chữ gõ trong lúc đang vẽ.
    const doSearchRender = KLAType.debounce(()=>{
      KLAType.keepFocus(renderDacTinh);
    }, SEARCH_DEBOUNCE_MS);
    dactinhSearchInput.addEventListener('input', e=>{
      state.dactinhSearch = e.target.value;
      doSearchRender();
    });
  }

  // ---------- Tiêu đề "Đặc Tính" — bấm để đổi hướng sắp xếp A→Z / Z→A ----------
  const sortToggle = document.getElementById('dactinhSortToggle');
  if(sortToggle) sortToggle.addEventListener('click', ()=>{
    state.dactinhSortDir = state.dactinhSortDir === 'asc' ? 'desc' : 'asc';
    renderDacTinh();
  });

  // ---------- Checkbox chọn dòng — bảng "Đặc Tính" ----------
  // QUAN TRỌNG: KHÔNG gọi renderDacTinh() (vẽ lại toàn bộ 2 bảng + toàn bộ HTML của module) mỗi
  // khi tick 1 checkbox — đó chính là nguyên nhân gây "giật" màn hình khi thao tác (thay
  // innerHTML của cả khối lớn liên tục chỉ để cập nhật 1 dòng tick chọn). Thay vào đó chỉ cập
  // nhật state + vá lại đúng phần UI nhỏ liên quan (thanh "move-bar" phía trên + checkbox
  // "chọn tất cả") — bảng dữ liệu giữ nguyên trong DOM nên không bị giật/nhảy vị trí cuộn.
  const rowChks = document.querySelectorAll('.dactinhRowChk');
  const chkAll = document.getElementById('dactinhChkAll');
  function syncDacTinhChkAll(){
    if(!chkAll) return;
    const ids = Array.from(rowChks).filter(el=>!el.disabled).map(el => Number(el.dataset.idx));
    const allSel = ids.length>0 && ids.every(i=>state.dactinhSelected.has(i));
    const someSel = ids.some(i=>state.dactinhSelected.has(i));
    chkAll.checked = allSel;
    chkAll.indeterminate = !allSel && someSel;
  }
  // Cập nhật thanh "move-bar" (số lượng đã chọn + bật/tắt select + 2 nút Gộp/Bỏ chọn) tại chỗ,
  // không đụng tới bảng dữ liệu — dùng sau mỗi lần tick/bỏ tick 1 hoặc nhiều checkbox.
  function updateDactinhMoveBar(){
    const bar = document.getElementById('dactinhMoveBar');
    if(!bar) return;
    const selCountNow = state.dactinhSelected.size;
    const countEl = bar.querySelector('.move-count');
    if(countEl) countEl.textContent = selCountNow>0 ? `${selCountNow.toLocaleString('en-US')} đặc tính đã chọn` : 'Tick chọn dòng để gộp vào 1 nhóm';
    const sel = document.getElementById('dactinhGroupTarget');
    if(sel) sel.disabled = selCountNow===0;
    const moveBtn = document.getElementById('dactinhBtnMove');
    if(moveBtn) moveBtn.disabled = selCountNow===0;
    const clearBtn = document.getElementById('dactinhBtnClearSel');
    if(clearBtn) clearBtn.disabled = selCountNow===0;
    syncDacTinhChkAll();
  }
  syncDacTinhChkAll();
  rowChks.forEach(chk=>{
    chk.addEventListener('change', ()=>{
      const idx = Number(chk.dataset.idx);
      if(chk.checked) state.dactinhSelected.add(idx); else state.dactinhSelected.delete(idx);
      updateDactinhMoveBar();
    });
  });
  if(chkAll){
    chkAll.addEventListener('change', ()=>{
      const ids = Array.from(rowChks).filter(el=>!el.disabled).map(el => Number(el.dataset.idx));
      if(chkAll.checked) ids.forEach(i=>state.dactinhSelected.add(i));
      else ids.forEach(i=>state.dactinhSelected.delete(i));
      // Cập nhật trực tiếp trạng thái checked của từng checkbox dòng (không vẽ lại bảng).
      rowChks.forEach(el=>{ if(!el.disabled) el.checked = chkAll.checked; });
      updateDactinhMoveBar();
    });
  }

  // ---------- Nút "Bỏ chọn" ----------
  const btnClearSel = document.getElementById('dactinhBtnClearSel');
  if(btnClearSel) btnClearSel.addEventListener('click', ()=>{
    state.dactinhSelected.clear();
    rowChks.forEach(el=>{ el.checked = false; });
    updateDactinhMoveBar();
  });

  // ---------- Nút "Xoá dữ liệu đã lưu" — xoá TOÀN BỘ "sổ" đặc tính đã lưu vĩnh viễn: cả
  // state.dactinhKnownLabels (danh sách đặc tính đã ghi nhận — làm rỗng bảng "Đặc Tính") lẫn
  // state.dactinhGroupAssign (đặc tính đã gộp nhóm — làm rỗng bảng "Gộp Đặc Tính"). Dùng khi
  // muốn dọn sạch để gộp lại từ đầu (VD sau khi đổi bộ chỉ tiêu nhóm thị trường). Bắt buộc xác
  // nhận vì đây là thao tác KHÔNG THỂ hoàn tác — mất hết công gộp nhóm đã làm từ trước.
  const btnClearSaved = document.getElementById('dactinhBtnClearSaved');
  if(btnClearSaved) btnClearSaved.addEventListener('click', async ()=>{
    if(m1DtDenied()) return;
    const totalKnown = Object.keys(state.dactinhKnownLabels).length;
    const totalGrouped = Object.keys(state.dactinhGroupAssign).length;
    if(totalKnown === 0 && totalGrouped === 0){
      alert('Chưa có dữ liệu Đặc Tính nào được lưu để xoá.');
      return;
    }
    const ok = confirm(
      `Xoá TOÀN BỘ dữ liệu đã lưu trước đó của "Đặc Tính" (${totalKnown.toLocaleString('en-US')} đặc tính đã ghi nhận) ` +
      `và "Gộp Đặc Tính" (${totalGrouped.toLocaleString('en-US')} đặc tính đã gộp nhóm)?\n\n` +
      `Hành động này KHÔNG THỂ hoàn tác — toàn bộ công gộp nhóm đã làm sẽ mất hết.`
    );
    if(!ok) return;
    state.dactinhKnownLabels = {};
    state.dactinhGroupAssign = {};
    state.dactinhSelected.clear();
    // QUAN TRỌNG: phải AWAIT ghi xong hẳn xuống database TRƯỚC KHI vẽ lại giao diện — nếu
    // không, renderDacTinh() bên dưới có thể chạy tới getDacTinhBreakdown(), phát hiện đặc
    // tính vừa xoá VẪN CÒN trong dữ liệu Load Data đang mở (đúng thiết kế: đặc tính đang tồn
    // tại thì không bao giờ bị quên), và tự lưu lại đặc tính đó gần NHƯ CÙNG LÚC với lệnh xoá
    // ở trên — 2 lệnh ghi database chạy đua nhau (race condition), lệnh nào hoàn tất SAU sẽ
    // thắng, không cố định lệnh nào — có lúc xoá thành công, có lúc bị ghi đè lại y như cũ dù
    // giao diện đang hiển thị đã xoá. Chờ ghi xong hẳn rồi mới vẽ lại sẽ xoá tuần tự, không
    // còn chạy đua.
    await persistDacTinh();
    renderDacTinh();
  });

  // ---------- Nút "Gộp vào nhóm đã chọn" — gán các dòng đang tick vào nhóm chọn trong select,
  // LƯU LẠI (persist), rồi vẽ lại: các dòng vừa gộp sẽ mờ đi và mất khỏi lựa chọn. ----------
  const btnMove = document.getElementById('dactinhBtnMove');
  const groupTargetSelect = document.getElementById('dactinhGroupTarget');
  if(btnMove){
    btnMove.addEventListener('click', async ()=>{
      if(m1DtDenied()) return;
      if(state.dactinhSelected.size === 0 || !groupTargetSelect) return;
      const targetGroup = groupTargetSelect.value;
      state.dactinhSelected.forEach(idx=>{
        const r = rows[idx];
        if(r) state.dactinhGroupAssign[normalizeLabelForMatch(r.label)] = { group: targetGroup, label: r.label };
      });
      state.dactinhSelected.clear();
      await persistDacTinh();
      renderDacTinh();
    });
  }

  // ---------- Bấm vào 1 đặc tính đã gộp (bên bảng phải) để BỎ GỘP, trả nó về danh sách chọn
  // được ở bảng trái. ----------
  document.querySelectorAll('.dactinh-unassign-badge').forEach(badge=>{
    badge.addEventListener('click', async ()=>{
      if(m1DtDenied()) return;
      delete state.dactinhGroupAssign[badge.dataset.key];
      await persistDacTinh();
      renderDacTinh();
    });
  });
}

// Xuất Excel bảng "Đặc Tính" — CHỈ lấy đúng 1 cột "Đặc Tính" đang hiển thị.
async function exportDacTinhTableToExcel(sheetName, filePrefix){ if(typeof window !== 'undefined' && window.klNapXlsx) await window.klNapXlsx(); /* PC 1.85: thư viện Excel nạp lười */
  const rows = getDacTinhBreakdown();
  const aoa = [['Đặc Tính']];
  rows.forEach(r=>{
    aoa.push([r.label]);
  });

  const ws = XLSX.utils.aoa_to_sheet(aoa);
  ws['!cols'] = computeAutoColWidths(aoa);
  const totalRowIdx = aoa.length - 1;
  for(let r = 0; r <= totalRowIdx; r++){
    const addr = XLSX.utils.encode_cell({ r, c: 0 });
    if(!ws[addr]) ws[addr] = { t: 's', v: '' };
    if(r === 0){
      ws[addr].s = {
        font: { name: 'Times New Roman', sz: 14, bold: true, color: { rgb: 'FFFFFF' } },
        fill: { patternType: 'solid', fgColor: { rgb: '1F4E78' } },
        alignment: { horizontal: 'center', vertical: 'center', wrapText: true },
        border: XLSX_THIN_BORDER,
      };
    } else {
      ws[addr].s = {
        font: { name: 'Times New Roman', sz: 14 },
        alignment: { horizontal: 'left', vertical: 'center' },
        border: XLSX_THIN_BORDER,
      };
    }
  }
  ws['!autofilter'] = { ref: 'A1:A1' };
  ws['!freeze'] = { xSplit: 0, ySplit: 1 };

  const wb = XLSX.utils.book_new();
  XLSX.utils.book_append_sheet(wb, ws, sheetName.slice(0, 31));

  const ts = new Date();
  const stamp = `${ts.getFullYear()}${String(ts.getMonth()+1).padStart(2,'0')}${String(ts.getDate()).padStart(2,'0')}_${String(ts.getHours()).padStart(2,'0')}${String(ts.getMinutes()).padStart(2,'0')}`;
  await writeWorkbookWithFreezeTopRow(wb, `${filePrefix}_${stamp}.xlsx`);
}
function exportDacTinhToExcel(){
  exportDacTinhTableToExcel('Dac Tinh', 'DacTinh');
}

// Xuất Excel bảng "Gộp Đặc Tính" — 2 cột: Nhóm (1 trong 11 nhóm cố định) + Đặc Tính đã gộp
// (mỗi đặc tính 1 dòng riêng, lặp lại tên nhóm ở cột đầu để dễ lọc/pivot trong Excel).
async function exportDacTinhGopToExcel(){ if(typeof window !== 'undefined' && window.klNapXlsx) await window.klNapXlsx(); /* PC 1.85: thư viện Excel nạp lười */
  const aoa = [['Nhóm', 'Đặc Tính']];
  HLSO_SUMMARY_COLUMNS.forEach(groupName=>{
    const members = getDacTinhGroupMembers(groupName);
    if(members.length){
      members.forEach(m=> aoa.push([groupName, m.label]));
    } else {
      aoa.push([groupName, '']);
    }
  });

  const ws = XLSX.utils.aoa_to_sheet(aoa);
  ws['!cols'] = computeAutoColWidths(aoa);
  const totalRowIdx = aoa.length - 1;
  for(let r = 0; r <= totalRowIdx; r++){
    for(let c = 0; c < 2; c++){
      const addr = XLSX.utils.encode_cell({ r, c });
      if(!ws[addr]) ws[addr] = { t: 's', v: '' };
      if(r === 0){
        ws[addr].s = {
          font: { name: 'Times New Roman', sz: 14, bold: true, color: { rgb: 'FFFFFF' } },
          fill: { patternType: 'solid', fgColor: { rgb: '1F4E78' } },
          alignment: { horizontal: 'center', vertical: 'center', wrapText: true },
          border: XLSX_THIN_BORDER,
        };
      } else {
        ws[addr].s = {
          font: { name: 'Times New Roman', sz: 14 },
          alignment: { horizontal: 'left', vertical: 'center' },
          border: XLSX_THIN_BORDER,
        };
      }
    }
  }
  ws['!autofilter'] = { ref: 'A1:B1' };
  ws['!freeze'] = { xSplit: 0, ySplit: 1 };

  const wb = XLSX.utils.book_new();
  XLSX.utils.book_append_sheet(wb, ws, 'Gop Dac Tinh'.slice(0, 31));

  const ts = new Date();
  const stamp = `${ts.getFullYear()}${String(ts.getMonth()+1).padStart(2,'0')}${String(ts.getDate()).padStart(2,'0')}_${String(ts.getHours()).padStart(2,'0')}${String(ts.getMinutes()).padStart(2,'0')}`;
  await writeWorkbookWithFreezeTopRow(wb, `GopDacTinh_${stamp}.xlsx`);
}

/* ================= RENDER: PLACEHOLDER (module chưa xây dựng) ================= */
function renderPlaceholder(){
  const mod = MODULES.find(m=>m.id===state.activeModule);
  const c = document.getElementById('content');
  c.innerHTML = `
    <div class="placeholder">
      <div class="ph-ic">${icon(mod.icon,'ic')}</div>
      <h2>${mod.label}</h2>
      <p>Chức năng "${mod.label}" sẽ được xây dựng ở giai đoạn tiếp theo.</p>
      <div class="tag">ĐANG PHÁT TRIỂN</div>
    </div>
  `;
}

/* ================= MODULE "TỔNG HỢP" ================= */
// Danh sách module NGUỒN cho phép chọn ở menu con "Xuất Hàng" — đúng 8 module lọc hiện có
// (Thành Phẩm Chính, Thành Phẩm Tạm, Tôm BM, HLSO, Gia Công, Nhập Khẩu, Nguyên Liệu, Giám Sát).
// Lấy trực tiếp từ FILTER_MODULES nên tự động cập nhật nếu sau này thêm/bớt module lọc.
function xuatHangSourceModuleList(){
  return MODULES.filter(m => FILTER_MODULES[m.id]);
}

// Menu con của module "Tổng Hợp": "Xuất Hàng" (đã có chức năng) + "Báo cáo ngoài sổ sách"
// (mới thêm chỗ vào menu, xây dựng sau — xem renderBaoCaoNgoaiSoSach bên dưới).
const TONGHOP_SUBTABS = [
  {id:'ngoaisoSach',  label:'Báo cáo ngoài sổ sách'},
  {id:'xuathang',     label:'Xuất Hàng'},
];

function renderTongHop(){
  const c = document.getElementById('content');
  c.innerHTML = `
    <div class="subtabs" id="tonghopSubtabs">
      ${TONGHOP_SUBTABS.map(t=>`<div class="subtab${state.tonghopSubTab===t.id?' active':''}" data-tab="${t.id}">${t.label}</div>`).join('')}
    </div>
    <div id="tonghopSubContent"></div>
  `;
  document.querySelectorAll('#tonghopSubtabs .subtab').forEach(el=>{
    el.addEventListener('click', ()=>{
      const tab = el.getAttribute('data-tab');
      if(state.tonghopSubTab === tab) return;
      state.tonghopSubTab = tab;
      renderTongHop();
    });
  });
  if(state.tonghopSubTab === 'ngoaisoSach'){
    renderBaoCaoNgoaiSoSach();
  } else {
    renderXuatHang();
  }
}

/* ---------- "Tổng Hợp" > "Báo cáo ngoài sổ sách" ----------
   2 bảng nhập tay LIÊN KẾT TRỰC TIẾP với dòng "Bột + Sốt + Bếp"/"Kho Gửi" ở bảng "Báo Cáo Tổng
   Tồn" (module Báo Cáo) — cùng đọc/ghi 1 giá trị trong state.hlsoManualValues (khoá qua
   normalizeLabelForMatch(BAOCAO_MANUAL_LABEL)/normalizeLabelForMatch(BAOCAO_KHOGUI_LABEL)) và
   lưu qua CHÍNH persist() đã dùng cho Load Data (buildLoadDataMeta() đã gồm hlsoManualValues).
   Tab này THAY THẾ cho việc nhập tay trực tiếp ở "Báo Cáo Tổng Tồn" — 2 dòng đó ở renderBaoCao()
   giờ chỉ còn hiển thị (đã bỏ nút Sửa/Xác nhận tại đó), sửa số liệu duy nhất tại đây. */
const NGSS_TABLES = [
  { field:'botsotbep', label:BAOCAO_MANUAL_LABEL,  key:()=>normalizeLabelForMatch(BAOCAO_MANUAL_LABEL) },
  { field:'khogui',    label:BAOCAO_KHOGUI_LABEL,   key:()=>normalizeLabelForMatch(BAOCAO_KHOGUI_LABEL) },
];

function renderNgoaiSoSachTable(t){
  const key = t.key();
  const val = state.hlsoManualValues[key];
  const editing = state.ngoaiSoSachEditing[t.field];
  const cellHtml = editing
    ? `<input type="text" inputmode="decimal" class="ngss-manual-input" data-field="${t.field}" data-key="${escapeHtml(key)}"
        value="${val!==null && val!==undefined ? val : ''}" placeholder="Nhập tay"
        style="width:100%;min-width:70px;background:var(--surface);border:1px solid var(--border-strong);border-radius:6px;
        padding:4px 6px;color:var(--frost);font-family:var(--mono);font-size:12px;text-align:right;">`
    : (val!==null && val!==undefined ? val.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2}) : '<span class="cell-empty">—</span>');
  const actionsHtml = `<div style="display:flex;gap:4px;">
      <button class="btn btn-ghost btn-tiny ngss-edit-btn" data-field="${t.field}" ${editing?'disabled':''}>${icon('edit')} Sửa</button>
      <button class="btn btn-primary btn-tiny ngss-confirm-btn" data-field="${t.field}" ${editing?'':'disabled'}>${icon('check')} Xác nhận</button>
    </div>`;
  return `<div style="flex:0 0 auto;min-width:280px;">
    <div style="display:flex;align-items:center;gap:12px;margin:4px 0 8px;min-height:33px;">
      <h3 style="font-family:var(--display);font-size:15px;font-weight:600;color:var(--text);">${escapeHtml(t.label)}</h3>
    </div>
    <div class="table-shell baocao-shell" style="max-width:400px;"><div class="table-scroll"><table class="baocao-table"><thead><tr>
        <th></th><th class="num">Net (kg)</th><th></th>
      </tr></thead><tbody>
      <tr>
        <td style="font-weight:700;color:var(--text);">${escapeHtml(t.label)}</td>
        <td class="num">${cellHtml}</td>
        <td style="border-right:none;white-space:nowrap;">${actionsHtml}</td>
      </tr>
      </tbody></table></div></div>
  </div>`;
}

function renderBaoCaoNgoaiSoSach(){
  const wrap = document.getElementById('tonghopSubContent');
  wrap.innerHTML = `
    <div class="card" style="margin-bottom:0;">
      <p class="hint">Nhập số liệu tại đây — <b style="color:var(--frost)">liên kết trực tiếp</b> với dòng "${escapeHtml(BAOCAO_MANUAL_LABEL)}"/"${escapeHtml(BAOCAO_KHOGUI_LABEL)}" ở bảng "Báo Cáo Tổng Tồn" (mục Báo Cáo). Sửa ở đây tự động cập nhật bên đó.</p>
      <div style="display:flex;gap:20px;flex-wrap:wrap;align-items:flex-start;">
        ${NGSS_TABLES.map(t=>renderNgoaiSoSachTable(t)).join('')}
      </div>
    </div>
  `;

  const commitNgss = async (field)=>{
    const inp = wrap.querySelector(`.ngss-manual-input[data-field="${field}"]`);
    if(inp){
      const raw = inp.value.trim().replace(/,/g,'');
      const num = raw === '' ? null : parseFloat(raw);
      state.hlsoManualValues[inp.dataset.key] = (num!==null && !isNaN(num)) ? num : null;
      // Chờ persist() xong (KHÔNG bắn-và-quên) để biết CHẮC CHẮN dữ liệu đã ghi vào database
      // Excel (sheet riêng "BaoCaoNgoaiSoSach" trong M01_TonKhoAnAn.xlsx) hay chỉ mới lưu
      // tạm trong trình duyệt/bị lỗi — nếu không phải 'excel', báo ngay cho người dùng biết
      // thay vì im lặng như trước (dễ gây hiểu lầm là "đã lưu" trong khi thực ra chưa ghi được
      // vào file Excel).
      const via = await persistNgoaiSoSach();
      if(via === 'localStorage'){
        alert('CHƯA lưu được vào database Excel (sheet "BaoCaoNgoaiSoSach" trong M01_TonKhoAnAn.xlsx) — số liệu chỉ mới lưu TẠM trong trình duyệt.\n\nNguyên nhân thường gặp: đang mở file HTML trực tiếp bằng trình duyệt thường thay vì mở qua ứng dụng Electron (Electron.vbs / Chay_Lan_Dau.bat). Hãy đóng và mở lại đúng cách để dữ liệu được lưu vào Excel.');
      } else if(via === 'error'){
        alert('LƯU THẤT BẠI — không ghi được vào database Excel LẪN trình duyệt. Vui lòng thử lại hoặc kiểm tra quyền ghi file.');
      }
    }
    state.ngoaiSoSachEditing[field] = false;
    renderBaoCaoNgoaiSoSach();
  };
  wrap.querySelectorAll('.ngss-edit-btn').forEach(btn=>{
    btn.addEventListener('click', ()=>{
      const field = btn.dataset.field;
      state.ngoaiSoSachEditing[field] = true;
      renderBaoCaoNgoaiSoSach();
      const inp = wrap.querySelector(`.ngss-manual-input[data-field="${field}"]`);
      if(inp){ inp.focus(); inp.select(); }
    });
  });
  wrap.querySelectorAll('.ngss-confirm-btn').forEach(btn=>{
    btn.addEventListener('click', ()=> commitNgss(btn.dataset.field));
  });
  wrap.querySelectorAll('.ngss-manual-input').forEach(inp=>{
    inp.addEventListener('keydown', e=>{ if(e.key==='Enter'){ commitNgss(inp.dataset.field); } });
  });
}

/* ---------- "Tổng Hợp" > "Xuất Hàng" ---------- */
function renderXuatHang(){
  const wrap = document.getElementById('tonghopSubContent');
  const hasData = state.rows.length > 0 && state.headers.length > 0;
  if(!hasData){
    wrap.innerHTML = `
      <div class="placeholder">
        <div class="ph-ic">${icon('stack','ic')}</div>
        <h2>Chưa có dữ liệu tồn kho</h2>
        <p>Vào mục <b style="color:var(--frost)">Load Data</b> để nạp file Excel tồn kho trước — dữ liệu sẽ tự động hiển thị tại đây.</p>
        <div class="tag" id="xhBtnGoLoadData" style="cursor:pointer;">ĐI TỚI LOAD DATA</div>
      </div>
    `;
    const goBtn = document.getElementById('xhBtnGoLoadData');
    if(goBtn) goBtn.addEventListener('click', ()=>{ state.activeModule = 'loaddata'; renderAll(); });
    return;
  }

  const modId = state.xuatHangSourceModule;
  const ids = Object.keys(state.xuatHangSelected);
  const totalQty = ids.reduce((s,id)=>s + (Number(state.xuatHangSelected[id])||0), 0);

  let html = `<div id="xhFsRoot" class="kl-fs-root"><div class="kl-fs-root-bar"><button type="button" class="btn btn-ghost" data-kl-full="#xhFsRoot" data-kl-full-mode="cover" title="Mở rộng bảng ra toàn màn hình">⛶ <span class="kl-fs-txt">Toàn màn hình</span></button></div>
  <div class="card">
    <h3>1. Chọn bảng để xuất</h3>
    <p class="hint">Chỉ chọn <b style="color:var(--frost)">1 bảng tại 1 thời điểm</b> — không gộp số liệu nhiều module lại. Tick chọn dòng, nhập số lượng cần xuất, rồi bấm "Tạo phiếu xuất".</p>
    <div class="module-picker">
      ${xuatHangSourceModuleList().map(m=>
        `<div class="mchip ${m.id===modId?'active':''}" data-m="${m.id}">${escapeHtml(m.label)}</div>`
      ).join('')}
    </div>
    <div id="xhSourceTableWrap"></div>
    <div class="toolbar-bottom">
      <div class="sel-info">${ids.length===0
          ? 'Chưa chọn dòng nào'
          : `Đã chọn <b>${ids.length.toLocaleString('en-US')}</b> dòng — tổng SL xuất: <b>${totalQty.toLocaleString('en-US')}</b>`}</div>
      <button class="btn btn-primary" id="xhBtnCreateSlip" ${ids.length===0?'disabled':''}>+ Tạo phiếu xuất</button>
    </div>
  </div>
  </div><!-- PC 4.92: hết khung phóng to (chỉ bảng chọn hàng) -->
  <div class="card">
    <h3>2. Danh sách phiếu xuất</h3>
    <p class="hint">Phiếu mới tạo ở trạng thái <span class="badge warn">Chưa trừ tồn</span> — số liệu module nguồn <b style="color:var(--frost)">chưa bị thay đổi</b>. Chỉ khi bấm "Trừ tồn kho" hệ thống mới thực sự trừ số lượng khỏi cột "Tồn cuối" của module nguồn. Có thể bấm "Hủy trừ tồn kho" để hoàn tác (cộng lại đúng số đã trừ).</p>
    <div id="xhSlipListWrap"></div>
  </div>`;
  wrap.innerHTML = html;
  if(window.klFullscreen) window.klFullscreen.sync(); // PC 2.46

  wrap.querySelectorAll('.mchip').forEach(chip=>{
    chip.addEventListener('click', ()=>{
      state.xuatHangSourceModule = chip.dataset.m;
      state.xuatHangSelected = {};
      state.xuatHangColSearch = {};
      state.xuatHangColFilters = {};
      state.xuatHangPage = 1;
      renderXuatHang();
    });
  });

  renderXuatHangSourceTable();
  renderXuatHangSlipList();

  const btnCreate = document.getElementById('xhBtnCreateSlip');
  if(btnCreate) btnCreate.addEventListener('click', ()=>{
    createXuatSlip();
    renderXuatHang();
  });
}

function renderXuatHangSourceTable(){
  const modId = state.xuatHangSourceModule;
  const tWrap = document.getElementById('xhSourceTableWrap');
  if(!tWrap) return;
  if(!modId){
    tWrap.innerHTML = `<div class="empty">Chọn 1 bảng ở trên để xem dữ liệu.</div>`;
    return;
  }
  let fr = filterModuleFilteredRows(modId);
  const cols = getFilterModuleColumns(modId);
  const tonCuoiCol = cols.find(c=>c.label==='Tồn cuối');
  const xhValueOf = (row, col) => applySizePrefix(modId, col.label, row[col.ci] ?? '');

  // Áp dụng thêm ô tìm kiếm riêng theo từng cột (dòng ngay dưới tiêu đề) — lọc kiểu "chứa",
  // không phân biệt hoa/thường/dấu, CỘNG DỒN giữa các cột đang có nội dung tìm kiếm.
  const activeColSearch = Object.entries(state.xuatHangColSearch).filter(([,v]) => String(v||'').trim() !== '');
  if(activeColSearch.length){
    fr = fr.filter(({row}) => activeColSearch.every(([label, q])=>{
      const col = cols.find(c=>c.label===label);
      if(!col) return true;
      return fuzzyIncludes(xhValueOf(row, col), q);
    }));
  }

  // Bộ lọc theo cột kiểu Excel AutoFilter (icon phễu, checkbox nhiều giá trị) — CỘNG DỒN với ô
  // tìm kiếm "chứa" ở trên, khoá theo tên cột (label) vì bảng này gộp dữ liệu từ nhiều module
  // nguồn khác nhau nên không có 1 chỉ số cột cố định như các bảng khác.
  const activeColFilters = Object.entries(state.xuatHangColFilters).filter(([,v]) => colFilterActive(v));
  if(activeColFilters.length){
    fr = fr.filter(({row}) => activeColFilters.every(([label, set])=>{
      const col = cols.find(c=>c.label===label);
      if(!col) return true;
      return set.has(normalizeCellForExactFilter(xhValueOf(row, col)));
    }));
  }

  // Lọc liên kết theo tầng (cascading filter): danh sách giá trị hiện trong dropdown của 1 cột
  // chỉ tính trên các dòng đã khớp ô tìm kiếm + bộ lọc của các CỘT KHÁC (bỏ qua cột đang mở),
  // giống hệt cơ chế đã dùng ở module "Tồn Kho Gửi".
  function xhCascadedRows(excludeLabel){
    let base = filterModuleFilteredRows(modId);
    const otherSearch = Object.entries(state.xuatHangColSearch).filter(([label, v]) => label !== excludeLabel && String(v||'').trim() !== '');
    if(otherSearch.length){
      base = base.filter(({row}) => otherSearch.every(([label, q])=>{
        const col = cols.find(c=>c.label===label);
        if(!col) return true;
        return fuzzyIncludes(xhValueOf(row, col), q);
      }));
    }
    return cascadeExcept(base, state.xuatHangColFilters, excludeLabel, (item, label)=>{
      const col = cols.find(c=>c.label===label);
      return col ? normalizeCellForExactFilter(xhValueOf(item.row, col)) : '';
    });
  }

  // Phân trang: chỉ vẽ tối đa XH_PAGE_SIZE dòng/trang (giống 7 module lọc) — tránh vẽ
  // hàng nghìn dòng cùng lúc vào DOM gây giật/lag. Tổng số trang tính trên TOÀN BỘ fr đã lọc
  // (kể cả tìm kiếm theo cột ở trên), không phải theo trang.
  const xhTotalPages = Math.max(1, Math.ceil(fr.length / XH_PAGE_SIZE));
  if(state.xuatHangPage > xhTotalPages) state.xuatHangPage = xhTotalPages;
  if(state.xuatHangPage < 1) state.xuatHangPage = 1;
  const xhPageStart = (state.xuatHangPage - 1) * XH_PAGE_SIZE;
  const frPage = fr.slice(xhPageStart, xhPageStart + XH_PAGE_SIZE);

  let html = `<div class="table-shell"><div class="table-scroll"><table><thead><tr>
    <th class="chk-col" style="width:34px;"></th>
    ${cols.map(c=>{
      const hasFilter = colFilterActive(state.xuatHangColFilters[c.label]);
      return `<th class="${isNumericCol(c.label)?'num':''} ${hasFilter?'has-filter':''}">
        <div class="th-row">
          <span class="th-label" style="cursor:default;">${escapeHtml(c.label)}</span>
        </div>
      </th>`;
    }).join('')}
    <th class="num" style="width:90px;">SL xuất</th>
  </tr>
  <tr class="col-search-row">
    <th class="chk-col"></th>
    ${cols.map(c=>{ const hasFilter = colFilterActive(state.xuatHangColFilters[c.label]); return `<th><div class="col-search-cell"><input type="text" class="xhColSearchInput" data-label="${escapeHtml(c.label)}"
        placeholder="Tìm ${escapeHtml(c.label.toLowerCase())}…" value="${escapeHtml(state.xuatHangColSearch[c.label]||'')}"><button type="button" class="th-filter-btn ${hasFilter?'active':''}" data-filter-label="${escapeHtml(c.label)}" title="Lọc theo ${escapeHtml(c.label)}">${icon('search')}</button></div></th>`; }).join('')}
    <th></th>
  </tr>
  </thead><tbody>`;
  if(fr.length===0){
    html += `<tr><td colspan="${cols.length+2}" class="empty">Không có dòng nào khớp.</td></tr>`;
  } else {
    frPage.forEach(({i,row})=>{
      const sel = state.xuatHangSelected[i];
      const checked = sel !== undefined;
      const maxQty = tonCuoiCol ? (parseLooseNumber(row[tonCuoiCol.ci]) || 0) : undefined;
      html += `<tr>
        <td><input type="checkbox" class="chk xhRowChk" data-i="${i}" ${checked?'checked':''}></td>
        ${cols.map(c=>{
          const v = xhValueOf(row, c);
          return `<td class="${isNumericCol(c.label)?'num':''}">${escapeHtml(v)}</td>`;
        }).join('')}
        <td class="num"><input type="number" class="qty-input xhQtyInput" data-i="${i}" min="0"
          ${maxQty!==undefined?`max="${maxQty}"`:''}
          value="${checked?sel:''}" ${checked?'':'disabled'}></td>
      </tr>`;
    });
  }
  html += `</tbody></table></div>`;
  if(fr.length > 10 || XH_PAGE_SIZE < fr.length){
    html += `<div class="pagination">
      <button type="button" class="btn btn-ghost btn-sm" id="xhPagePrev" ${state.xuatHangPage<=1?'disabled':''}>${icon('file')} Trước</button>
      <span class="pagination-info">Trang ${state.xuatHangPage.toLocaleString('en-US')} / ${xhTotalPages.toLocaleString('en-US')} — ${fr.length.toLocaleString('en-US')} dòng</span>
      <button type="button" class="btn btn-ghost btn-sm" id="xhPageNext" ${state.xuatHangPage>=xhTotalPages?'disabled':''}>Sau ${icon('file')}</button>
      <select id="xhPageSizeSelect" class="btn btn-ghost btn-sm" style="cursor:pointer;">
        <option value="300" ${XH_PAGE_SIZE===300?'selected':''}>300 dòng/trang</option>
        <option value="400" ${XH_PAGE_SIZE===400?'selected':''}>400 dòng/trang</option>
        <option value="500" ${XH_PAGE_SIZE===500?'selected':''}>500 dòng/trang</option>
      </select>
    </div>`;
  }
  html += `</div>`;
  tWrap.innerHTML = html;

  const pagePrev = document.getElementById('xhPagePrev');
  const pageNext = document.getElementById('xhPageNext');
  if(pagePrev) pagePrev.addEventListener('click', ()=>{ state.xuatHangPage = Math.max(1, state.xuatHangPage-1); renderXuatHangSourceTable(); });
  if(pageNext) pageNext.addEventListener('click', ()=>{ state.xuatHangPage = state.xuatHangPage+1; renderXuatHangSourceTable(); });
  const xhPageSizeSelect = document.getElementById('xhPageSizeSelect');
  if(xhPageSizeSelect) xhPageSizeSelect.addEventListener('change', ()=>{
    XH_PAGE_SIZE = parseInt(xhPageSizeSelect.value, 10);
    state.xuatHangPage = 1;
    renderXuatHangSourceTable();
  });

  const doColSearchRender = debounce(()=>{
    renderXuatHangSourceRows();
  }, SEARCH_DEBOUNCE_MS);
  tWrap.querySelectorAll('.xhColSearchInput').forEach(inp=>{
    inp.addEventListener('input', ()=>{
      const label = inp.dataset.label;
      state.xuatHangColSearch[label] = inp.value;
      state.xuatHangPage = 1;
      tWrap.dataset.lastFocusLabel = label;
      doColSearchRender();
    });
  });

  // Icon phễu: mở popup lọc theo cột kiểu Excel AutoFilter (checkbox nhiều giá trị) — danh sách
  // giá trị đề xuất được lọc liên kết theo tầng dựa trên ô tìm kiếm + bộ lọc của các cột khác.
  tWrap.querySelectorAll('.th-filter-btn[data-filter-label]').forEach(btn=>{
    btn.addEventListener('click', e=>{
      e.stopPropagation();
      const label = btn.dataset.filterLabel;
      const col = cols.find(c=>c.label===label);
      if(!col) return;
      openColSearchPopoverGeneric(
        btn, label, label,
        ()=>xhCascadedRows(label).map(({row})=>normalizeCellForExactFilter(xhValueOf(row, col))),
        state.xuatHangColFilters,
        ()=>{ state.xuatHangPage = 1; renderXuatHangSourceRows(); }
      );
    });
  });

  tWrap.querySelectorAll('.xhRowChk').forEach(chk=>{
    chk.addEventListener('change', ()=>{
      const i = Number(chk.dataset.i);
      if(chk.checked){
        const row = state.rows[i] || [];
        const dflt = tonCuoiCol ? (parseLooseNumber(row[tonCuoiCol.ci]) || 0) : 0;
        state.xuatHangSelected[i] = dflt;
      } else {
        delete state.xuatHangSelected[i];
      }
      renderXuatHang();
    });
  });
  tWrap.querySelectorAll('.xhQtyInput').forEach(inp=>{
    inp.addEventListener('input', ()=>{
      const i = Number(inp.dataset.i);
      if(i in state.xuatHangSelected){
        state.xuatHangSelected[i] = Number(inp.value) || 0;
        const infoEl = document.querySelector('.sel-info');
        if(infoEl){
          const ids = Object.keys(state.xuatHangSelected);
          const totalQty = ids.reduce((s,id)=>s + (Number(state.xuatHangSelected[id])||0), 0);
          infoEl.innerHTML = `Đã chọn <b>${ids.length.toLocaleString('en-US')}</b> dòng — tổng SL xuất: <b>${totalQty.toLocaleString('en-US')}</b>`;
        }
      }
    });
  });
}

/* Tương tự renderLoadDataRows()/renderFilterModuleRows() — chỉ vẽ lại <tbody> + khung phân
   trang, KHÔNG đụng tới hàng .xhColSearchInput đang gõ, dùng khi người dùng đang GÕ tìm kiếm
   theo cột ở bảng nguồn "Xuất Hàng" (tránh ngắt quãng gõ tiếng Việt có dấu). */
function renderXuatHangSourceRows(){
  const modId = state.xuatHangSourceModule;
  const tWrap = document.getElementById('xhSourceTableWrap');
  const tbody = tWrap && tWrap.querySelector('tbody');
  if(!tbody || !modId){ renderXuatHangSourceTable(); return; }

  let fr = filterModuleFilteredRows(modId);
  const cols = getFilterModuleColumns(modId);
  const tonCuoiCol = cols.find(c=>c.label==='Tồn cuối');
  const xhValueOf = (row, col) => applySizePrefix(modId, col.label, row[col.ci] ?? '');

  const activeColSearch = Object.entries(state.xuatHangColSearch).filter(([,v]) => String(v||'').trim() !== '');
  if(activeColSearch.length){
    fr = fr.filter(({row}) => activeColSearch.every(([label, q])=>{
      const col = cols.find(c=>c.label===label);
      if(!col) return true;
      return fuzzyIncludes(xhValueOf(row, col), q);
    }));
  }
  const activeColFilters = Object.entries(state.xuatHangColFilters).filter(([,v]) => colFilterActive(v));
  if(activeColFilters.length){
    fr = fr.filter(({row}) => activeColFilters.every(([label, set])=>{
      const col = cols.find(c=>c.label===label);
      if(!col) return true;
      return set.has(normalizeCellForExactFilter(xhValueOf(row, col)));
    }));
  }

  const xhTotalPages = Math.max(1, Math.ceil(fr.length / XH_PAGE_SIZE));
  if(state.xuatHangPage > xhTotalPages) state.xuatHangPage = xhTotalPages;
  if(state.xuatHangPage < 1) state.xuatHangPage = 1;
  const xhPageStart = (state.xuatHangPage - 1) * XH_PAGE_SIZE;
  const frPage = fr.slice(xhPageStart, xhPageStart + XH_PAGE_SIZE);

  const needsPagination = fr.length > 10 || XH_PAGE_SIZE < fr.length;
  const paginationBox = tWrap.querySelector('.pagination');
  if(needsPagination !== !!paginationBox){
    renderXuatHangSourceTable(); // số trang chuyển có/không — vẽ lại đầy đủ cho chắc (hiếm gặp)
    return;
  }

  tbody.innerHTML = fr.length===0
    ? `<tr><td colspan="${cols.length+2}" class="empty">Không có dòng nào khớp.</td></tr>`
    : frPage.map(({i,row})=>{
        const sel = state.xuatHangSelected[i];
        const checked = sel !== undefined;
        const maxQty = tonCuoiCol ? (parseLooseNumber(row[tonCuoiCol.ci]) || 0) : undefined;
        return `<tr>
          <td><input type="checkbox" class="chk xhRowChk" data-i="${i}" ${checked?'checked':''}></td>
          ${cols.map(c=>{
            const v = xhValueOf(row, c);
            return `<td class="${isNumericCol(c.label)?'num':''}">${escapeHtml(v)}</td>`;
          }).join('')}
          <td class="num"><input type="number" class="qty-input xhQtyInput" data-i="${i}" min="0"
            ${maxQty!==undefined?`max="${maxQty}"`:''}
            value="${checked?sel:''}" ${checked?'':'disabled'}></td>
        </tr>`;
      }).join('');

  if(paginationBox){
    const prevBtn = document.getElementById('xhPagePrev');
    const nextBtn = document.getElementById('xhPageNext');
    const infoSpan = paginationBox.querySelector('.pagination-info');
    if(prevBtn) prevBtn.disabled = state.xuatHangPage<=1;
    if(nextBtn) nextBtn.disabled = state.xuatHangPage>=xhTotalPages;
    if(infoSpan) infoSpan.textContent = `Trang ${state.xuatHangPage.toLocaleString('en-US')} / ${xhTotalPages.toLocaleString('en-US')} — ${fr.length.toLocaleString('en-US')} dòng`;
  }

  // Gắn lại sự kiện cho checkbox/ô số lượng vừa tạo lại (tbody mới) — KHÔNG cần gắn lại sự kiện
  // của toolbar/tiêu đề cột (không đổi).
  tWrap.querySelectorAll('.xhRowChk').forEach(chk=>{
    chk.addEventListener('change', ()=>{
      const i = Number(chk.dataset.i);
      if(chk.checked){
        const row = state.rows[i] || [];
        const dflt = tonCuoiCol ? (parseLooseNumber(row[tonCuoiCol.ci]) || 0) : 0;
        state.xuatHangSelected[i] = dflt;
      } else {
        delete state.xuatHangSelected[i];
      }
      renderXuatHang();
    });
  });
  tWrap.querySelectorAll('.xhQtyInput').forEach(inp=>{
    inp.addEventListener('input', ()=>{
      const i = Number(inp.dataset.i);
      if(i in state.xuatHangSelected){
        state.xuatHangSelected[i] = Number(inp.value) || 0;
        const infoEl = document.querySelector('.sel-info');
        if(infoEl){
          const ids = Object.keys(state.xuatHangSelected);
          const totalQty = ids.reduce((s,id)=>s + (Number(state.xuatHangSelected[id])||0), 0);
          infoEl.innerHTML = `Đã chọn <b>${ids.length.toLocaleString('en-US')}</b> dòng — tổng SL xuất: <b>${totalQty.toLocaleString('en-US')}</b>`;
        }
      }
    });
  });
}

function renderXuatHangSlipList(){
  const wrap = document.getElementById('xhSlipListWrap');
  if(!wrap) return;
  if(state.xuatSlips.length===0){
    wrap.innerHTML = `<div class="empty">Chưa có phiếu xuất nào.</div>`;
    return;
  }
  let html = `<div class="table-shell"><div class="table-scroll" style="max-height:none;"><table data-kcf data-kcf-key="m1:phieuxuat" data-kl-sort="m1:phieuxuat"><thead><tr>
    <th>Mã phiếu</th><th>Module nguồn</th><th>Ngày tạo</th><th class="num">Số dòng</th>
    <th class="num">Tổng SL</th><th>Trạng thái</th><th></th>
  </tr></thead><tbody>`;
  state.xuatSlips.forEach(s=>{
    const d = new Date(s.createdAt);
    const dateStr = isNaN(d.getTime()) ? '' : d.toLocaleDateString('vi-VN');
    const totalQty = s.lines.reduce((sum,l)=>sum+(Number(l.qty)||0),0);
    html += `<tr>
      <td style="font-family:var(--mono);color:var(--frost);">${escapeHtml(s.tam ? 'Chờ mã máy chủ' : s.id)}${s.tam ? ` <span class="badge warn" title="Phiếu nhập lúc máy chủ tắt (mã tạm ${escapeHtml(s.id)}) — máy chủ sẽ cấp mã PX-… khi gộp">tạm</span>` : ''}${s.thieuTon ? ' <span class="badge warn" title="Lúc gộp, tồn không đủ để trừ — xem Cài đặt › Mạng LAN › Xung đột khi gộp">thiếu tồn</span>' : ''}</td>
      <td>${escapeHtml(s.moduleLabel)}</td>
      <td style="font-family:var(--mono);">${dateStr}</td>
      <td class="num">${s.lines.length.toLocaleString('en-US')}</td>
      <td class="num">${totalQty.toLocaleString('en-US')}</td>
      <td>${s.deducted ? '<span class="badge ok">Đã trừ tồn</span>' : '<span class="badge warn">Chưa trừ tồn</span>'}</td>
      <td>${s.deducted
          ? `<button class="btn btn-tiny" data-undo-slip="${s.id}">Hủy trừ tồn kho</button>`
          : `<button class="btn btn-ok btn-tiny" data-deduct-slip="${s.id}">Trừ tồn kho</button>`}</td>
    </tr>`;
  });
  html += `</tbody></table></div></div>`;
  wrap.innerHTML = html;

  wrap.querySelectorAll('[data-deduct-slip]').forEach(btn=>{
    btn.addEventListener('click', ()=>{
      const s = state.xuatSlips.find(x=>x.id===btn.dataset.deductSlip);
      if(!s || s.deducted) return;
      showConfirmPopup(
        'Trừ tồn kho',
        `Xác nhận trừ tồn kho cho phiếu ${s.id} (module "${s.moduleLabel}")?\nSố lượng tương ứng sẽ bị trừ khỏi cột "Tồn cuối" của module nguồn.`,
        ()=>{ if(applyXuatSlipDeduction(s)){ renderXuatHangSlipList(); renderXuatHangSourceTable(); } }
      );
    });
  });
  wrap.querySelectorAll('[data-undo-slip]').forEach(btn=>{
    btn.addEventListener('click', ()=>{
      const s = state.xuatSlips.find(x=>x.id===btn.dataset.undoSlip);
      if(!s || !s.deducted) return;
      showConfirmPopup(
        'Hủy trừ tồn kho',
        `Xác nhận hủy trừ tồn kho cho phiếu ${s.id} (module "${s.moduleLabel}")?\nSố lượng đã trừ trước đó sẽ được cộng lại vào cột "Tồn cuối".`,
        ()=>{ if(undoXuatSlipDeduction(s)){ renderXuatHangSlipList(); renderXuatHangSourceTable(); } }
      );
    });
  });
}

/* ================= MASTER RENDER ================= */
// Bật tính năng "bôi đen tính tổng kiểu Excel" cho TẤT CẢ bảng đang hiển thị trong #content —
// gọi lại sau MỖI lần vẽ trang (renderAll()) hoặc vẽ lại riêng 1 khu vực (VD
// refreshHlsoSummarySection(), renderBaoCaoSizePctBody()) vì initExcelLikeSelection() cần chạy
// lại trên các <td> MỚI mỗi khi nội dung bảng bị thay thế.
function initExcelLikeSelectionForContent(){
  document.querySelectorAll('#content table').forEach(t=>initExcelLikeSelection(t));
}
/* PC 2.49: khối "TỒN THEO VỊ TRÍ" (nạp file vị trí, bảng, phiếu xuất vị trí) ĐÃ TÁCH sang module riêng "Tồn Vị Trí An An" (html/ton-vi-tri-an-an.html + js/ton-vi-tri-an-an.js). */
let XH_PAGE_SIZE = 300; // PC 5.06: mặc định 300 (trước 500). PC 5.03: bảng chọn hàng của Xuất hàng (M1) — riêng, chỉ 300 / 400 / 500 dòng/trang (không dùng chung FILTER_PAGE_SIZE của Load Data)

/* =====================================================================================
   VỊ TRÍ BỘT — tính năng ĐỘC LẬP với dữ liệu Load Data chính của module (và độc lập luôn với
   "Tồn theo vị trí" ở trên — 2 khu dữ liệu riêng biệt, không dùng chung).
   - Nguồn: file "Vị trí Bột" do người dùng cung cấp — dòng đầu "NGÀY:" (ngày số liệu), dòng kế
     tiếp là tiêu đề cột: VT, MÃ HÀNG, SL, NSX, HSD, NHẬP, NGÀY XUẤT, VỊ TRÍ CŨ, VỊ TRÍ MỚI,
     GHI CHÚ — nạp ở khu "Tải dữ liệu vị trí Bột" trong menu Load Data.
   - Lưu RIÊNG vào 2 sheet ViTriBotMeta + ViTriBot của M01_TonKhoAnAn.xlsx — nạp/xóa ở đây
     KHÔNG động tới state.rows/LoadData hay dữ liệu "Tồn theo vị trí" (vtState) và ngược lại.
   - Bảng dữ liệu (tìm kiếm chung + tìm/lọc riêng từng cột, phân trang) + chọn dòng/SL xuất →
     "Tạo phiếu xuất": LƯU PHIẾU VÀ TRỪ NGAY cột SL (09/2026, giống phiếu từ điện thoại) — phiếu nằm
     chung danh sách vtbPhoneSlips (cột Nguồn = PC / 📱), hủy trừ / trừ lại / xóa được như nhau.
   ===================================================================================== */
// PC 2.49: bản sao của vtHeaderKey() (nay nằm ở module Tồn Vị Trí An An) — Vị Trí Bột còn dùng để nhận diện tên cột.
function vtHeaderKey(h){
  return String(h ?? '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/gi, 'd')
    .toLowerCase().replace(/[^a-z0-9]/g, '');
}
const VITRIBOT_META_SHEET = 'ViTriBotMeta';
const VITRIBOT_DB_SHEET = 'ViTriBot';
const VITRIBOT_STORAGE_KEY = 'coldstock:vitribot'; // chỉ dùng khi không có window.dbAPI
const VITRIBOT_TEMPLATE_COLUMNS = ['VT','MÃ HÀNG','SL','NSX','HSD','NHẬP','NGÀY XUẤT','VỊ TRÍ CŨ','VỊ TRÍ MỚI','GHI CHÚ'];
// Tên cột khi LƯU vào sheet ViTriBot (mở database bằng Excel vẫn đọc được) — khớp đúng tên cột
// của file mẫu để dễ đối chiếu.
const VITRIBOT_STORE_KEYS = { vt:'VT', ma:'MÃ HÀNG', sl:'SL', nsx:'NSX', hsd:'HSD', nhap:'NHẬP',
  ngayXuat:'NGÀY XUẤT', vtCu:'VỊ TRÍ CŨ', vtMoi:'VỊ TRÍ MỚI', ghiChu:'GHI CHÚ' };

const vtbState = { rows: [], fileName: '', importedAt: null, dataDate: '', sheet: '', headerRowNumber: 0, skippedRows: 0 };
// 30/09/2026: dòng Số lượng = 0 (đã hết hàng) LUÔN bị lược bỏ khỏi bảng "Vị Trí Bột" (người dùng yêu cầu) — thay cho nút
// bật/tắt "Ẩn dòng SL = 0" trước đây. Chỉ ẩn khi xem, KHÔNG xoá khỏi dữ liệu (khoá dòng điện thoại tính trên cả danh sách).
const vtbUi = { q: '', colQ: {}, colF: {}, page: 1, pageSize: 300, sel: new Map(), sort: null };

// ---- Nhận diện cột (dùng chung cách chuẩn hoá với vtHeaderKey ở khối "Tồn theo vị trí") ----
const VTB_COL_ALIASES = {
  vt:       ['vt','vitri','vitrikho'],
  ma:       ['mahang','mahh','masanpham','itemcode'],
  sl:       ['sl','soluong','slton'],
  nsx:      ['nsx','ngaysanxuat'],
  hsd:      ['hsd','hansudung'],
  nhap:     ['nhap','ngaynhap'],
  ngayXuat: ['ngayxuat','ngayxuatkho'],
  vtCu:     ['vitricu'],
  vtMoi:    ['vitrimoi'],
  ghiChu:   ['ghichu'],
};
const VTB_REQUIRED = { vt:'VT', ma:'MÃ HÀNG', sl:'SL' };
function vtbMapColumns(row){
  const keys = (row || []).map(vtHeaderKey);
  const map = {};
  Object.entries(VTB_COL_ALIASES).forEach(([field, aliases])=>{
    const i = keys.findIndex(k => aliases.includes(k));
    if(i >= 0) map[field] = i;
  });
  return map;
}
function vtbNum(v){
  if(typeof v === 'number') return isFinite(v) ? v : 0;
  const n = parseLooseNumber(v);
  return isNaN(n) ? 0 : n;
}
// ---- Định dạng lại các cột ngày (NSX/HSD/NHẬP/NGÀY XUẤT) về "dd/mm/yyyy" ----
// File nguồn có thể có ngày ở nhiều dạng khác nhau tuỳ định dạng ô Excel gốc
// (yyyy-mm-dd, d/m/yyyy, m/d/yyyy, số serial Excel, "23092026"…) — luôn quy về dd/mm/yyyy
// khi hiển thị/lưu. Chuỗi không nhận diện được thì giữ nguyên, không làm mất dữ liệu.
function vtbExcelSerialToDate(n){
  // Mốc ngày Excel (1899-12-30, tính luôn lỗi năm nhuận 1900 mà Excel/SheetJS dùng)
  return new Date(Math.round((n - 25569) * 86400 * 1000));
}
function vtbFmtDate(v){
  const t = String(v ?? '').trim();
  if(!t) return '';
  // Số serial Excel (vd 45905) — chỉ nhận khi ra năm hợp lý 1990-2100
  if(/^\d{4,6}$/.test(t)){
    const n = Number(t);
    if(isFinite(n) && n > 0){
      const d = vtbExcelSerialToDate(n);
      const y = d.getUTCFullYear(), mo = d.getUTCMonth() + 1, da = d.getUTCDate();
      if(y >= 1990 && y <= 2100 && ddmmValid(da, mo, y)) return ddmmFmt(da, mo, y);
    }
  }
  // "23092026" (8 số liền nhau, ddmmyyyy)
  let m = t.match(/^(\d{2})(\d{2})(\d{4})$/);
  if(m){
    const d = Number(m[1]), mo = Number(m[2]), y = Number(m[3]);
    if(ddmmValid(d, mo, y)) return ddmmFmt(d, mo, y);
  }
  // ISO: yyyy-mm-dd / yyyy/mm/dd / yyyy.mm.dd (năm đứng đầu)
  m = t.match(/^(\d{4})\s*[.\-\/]\s*(\d{1,2})\s*[.\-\/]\s*(\d{1,2})/);
  if(m){
    const y = Number(m[1]), mo = Number(m[2]), d = Number(m[3]);
    if(ddmmValid(d, mo, y)) return ddmmFmt(d, mo, y);
  }
  // d/m/y hoặc m/d/y (năm ở cuối, 2 hoặc 4 số) — mặc định hiểu d/m/y (kiểu VN);
  // nếu số đầu > 12 (không thể là tháng) mà số sau <= 12 thì hiểu ngược lại là m/d/y (kiểu Mỹ).
  // SỬA 30/09/2026: trước đây \"(\d{2}|\d{4})\" không có neo cuối → năm 4 số chỉ lấy được 2 số đầu (\"19/11/2025\" → 2020).
  m = t.match(/^(\d{1,2})\s*[.\-\/]\s*(\d{1,2})\s*[.\-\/]\s*(\d{4}|\d{2})(?!\d)/);
  if(m){
    const a = Number(m[1]), b = Number(m[2]);
    const y = ddmmYear(a, b, m[3]);
    let d = a, mo = b;
    if(a > 12 && b <= 12){ d = a; mo = b; }
    else if(b > 12 && a <= 12){ d = b; mo = a; }
    if(ddmmValid(d, mo, y)) return ddmmFmt(d, mo, y);
  }
  return t; // không nhận diện được — giữ nguyên chuỗi gốc
}
function vtbBuildRow(o){
  const r = {
    vt: String(o.vt ?? '').trim(), ma: String(o.ma ?? '').trim(), sl: vtbNum(o.sl),
    nsx: vtbFmtDate(o.nsx), hsd: vtbFmtDate(o.hsd), nhap: vtbFmtDate(o.nhap),
    ngayXuat: vtbFmtDate(o.ngayXuat), vtCu: String(o.vtCu ?? '').trim(), vtMoi: String(o.vtMoi ?? '').trim(),
    ghiChu: String(o.ghiChu ?? '').trim(),
  };
  r._g = fuzzyNormalize([r.vt, r.ma, r.vtCu, r.vtMoi, r.ghiChu, r.nsx, r.hsd].join(' ')); // chuỗi cho ô tìm chung
  r._gc = noSpace(r._g); // bản bỏ dấu cách — tìm không tính khoảng trắng
  return r;
}
function vtbFmt(n, d){ return Number(n || 0).toLocaleString('en-US', {minimumFractionDigits: d || 0, maximumFractionDigits: d || 0}); }
function vtbFmtQty(n){ return Number(n || 0).toLocaleString('en-US', {maximumFractionDigits: 3}); }

function vtbResetUi(){
  vtbUi.q = ''; vtbUi.colQ = {}; vtbUi.colF = {}; vtbUi.page = 1; vtbUi.sel.clear();
}

// ---- Lưu / khôi phục (sheet riêng ViTriBotMeta + ViTriBot) ----
function vtbMeta(){
  return { fileName: vtbState.fileName, importedAt: vtbState.importedAt, dataDate: vtbState.dataDate || '', sheet: vtbState.sheet,
           headerRowNumber: vtbState.headerRowNumber, skippedRows: vtbState.skippedRows };
}
function vtbApplyMeta(m){
  m = m || {};
  vtbState.fileName = m.fileName || ''; vtbState.importedAt = m.importedAt || null; vtbState.sheet = m.sheet || '';
  vtbState.dataDate = m.dataDate || '';
  vtbState.headerRowNumber = m.headerRowNumber || 0; vtbState.skippedRows = m.skippedRows || 0;
}
function vtbToStored(r){
  const o = {};
  Object.entries(VITRIBOT_STORE_KEYS).forEach(([f, col]) => { o[col] = r[f]; });
  return o;
}
function vtbFromStored(o){
  const x = {};
  Object.entries(VITRIBOT_STORE_KEYS).forEach(([f, col]) => { x[f] = o[col]; });
  return vtbBuildRow(x);
}
async function persistViTriBot(){
  const hasRows = vtbState.rows.length > 0;
  if(hasDbAPI()){
    try{
      if(window.KLLuuDong){ // PC 1.67: lưu theo dòng (Meta ghi kèm cùng 1 giao dịch); không chắc chắn → tự lưu cả sheet
        const r = await window.KLLuuDong.save('ton-kho-an-an', VITRIBOT_DB_SHEET, vtbState.rows.map(vtbToStored), { [VITRIBOT_META_SHEET]: hasRows ? [{ data: '__JSON__' + JSON.stringify(vtbMeta()) }] : [] });
        if(r && r.ok) return 'excel';
        console.error('Lỗi lưu dữ liệu vị trí Bột vào database Excel:', r && r.error);
      }else{
      const rMeta = await window.dbAPI.writeSheet('ton-kho-an-an', VITRIBOT_META_SHEET,
        hasRows ? [{ data: '__JSON__' + JSON.stringify(vtbMeta()) }] : []);
      const rData = await window.dbAPI.writeSheet('ton-kho-an-an', VITRIBOT_DB_SHEET, vtbState.rows.map(vtbToStored));
      if(rMeta && rMeta.ok && rData && rData.ok) return 'excel';
      console.error('Lỗi lưu dữ liệu vị trí Bột vào database Excel:', (rMeta && rMeta.error) || (rData && rData.error) || 'writeSheet trả về ok=false');
      }
    }catch(e){ console.error('Lỗi lưu dữ liệu vị trí Bột vào database Excel:', e); }
  }
  try{
    if(hasRows) localStorage.setItem(VITRIBOT_STORAGE_KEY, JSON.stringify({ ...vtbMeta(), rows: vtbState.rows.map(vtbToStored) }));
    else localStorage.removeItem(VITRIBOT_STORAGE_KEY);
    return 'localStorage';
  }catch(e){ console.error('Lỗi lưu dữ liệu vị trí Bột (dự phòng localStorage):', e); throw e; }
}
async function restoreViTriBot(){
  if(hasDbAPI()){
    try{
      const resMeta = await window.dbAPI.readSheet('ton-kho-an-an', VITRIBOT_META_SHEET);
      if(resMeta && resMeta.ok){
        const cell = Array.isArray(resMeta.data) && resMeta.data[0] && resMeta.data[0].data;
        if(typeof cell === 'string' && cell.startsWith('__JSON__')){
          vtbApplyMeta(JSON.parse(cell.slice('__JSON__'.length)));
          const resData = await window.dbAPI.readSheet('ton-kho-an-an', VITRIBOT_DB_SHEET);
          if(window.KLLuuDong) window.KLLuuDong.seed('ton-kho-an-an', VITRIBOT_DB_SHEET, resData && resData.ok ? resData.data : null); // PC 1.67
          vtbState.rows = (resData && resData.ok && Array.isArray(resData.data))
            ? resData.data.map(vtbFromStored).filter(r => r.vt || r.ma) : [];
        }
        return; // đọc database thành công (có hoặc chưa có dữ liệu vị trí Bột)
      }
    }catch(e){ /* lỗi đọc — thử dự phòng bên dưới */ }
  }
  try{
    const raw = localStorage.getItem(VITRIBOT_STORAGE_KEY);
    if(raw){ const d = JSON.parse(raw); vtbApplyMeta(d); vtbState.rows = (d.rows || []).map(vtbFromStored).filter(r => r.vt || r.ma); }
  }catch(e){ /* không có dữ liệu dự phòng */ }
}


const VTB_PHONE_DB_SHEET = 'ViTriBotPhieuDienThoai';
const VTB_PHONE_STORAGE_KEY = 'coldstock:vitribot_phieu_dienthoai';
let vtbPhoneSlips = [];
let vtbPhoneSeq = 1;
function vtbPhoneKeys(rows){
  const seen = new Map();
  const t = v => String(v ?? '').trim();
  return rows.map(r => {
    const ma = t(r.ma), vt = t(r.vt);
    if(!ma && !vt) return '';
    const base = [ma, vt, t(r.nsx), t(r.hsd)].join('|');
    const n = seen.get(base) || 0;
    seen.set(base, n + 1);
    return base + '#' + n;
  });
}
async function persistVtbPhoneSlips(){
  const payload = { slips: vtbPhoneSlips, seq: vtbPhoneSeq };
  if(hasDbAPI()){
    try{
      const res = await window.dbAPI.writeSheet('ton-kho-an-an', VTB_PHONE_DB_SHEET, [{ data: '__JSON__' + JSON.stringify(payload) }]);
      if(res && res.ok) return true;
      console.error('Lỗi lưu phiếu điện thoại (Vị Trí Bột):', res && res.error);
    }catch(e){ console.error('Lỗi lưu phiếu điện thoại (Vị Trí Bột):', e); }
  }
  try{ localStorage.setItem(VTB_PHONE_STORAGE_KEY, JSON.stringify(payload)); }catch(e){}
  return false;
}
async function restoreVtbPhoneSlips(){
  const apply = d => {
    vtbPhoneSlips = (d && Array.isArray(d.slips)) ? d.slips : [];
    vtbPhoneSeq = (d && Number.isFinite(d.seq)) ? d.seq : 1;
  };
  if(hasDbAPI()){
    try{
      const res = await window.dbAPI.readSheet('ton-kho-an-an', VTB_PHONE_DB_SHEET);
      if(res && res.ok){
        const cell = Array.isArray(res.data) && res.data[0] && res.data[0].data;
        if(typeof cell === 'string' && cell.startsWith('__JSON__')) apply(JSON.parse(cell.slice('__JSON__'.length)));
        return;
      }
    }catch(e){ /* thử dự phòng */ }
  }
  try{ const raw = localStorage.getItem(VTB_PHONE_STORAGE_KEY); if(raw) apply(JSON.parse(raw)); }catch(e){}
}
// Trừ (sign = -1) hoặc hoàn (sign = +1) cột "SL" cho các dòng của 1 phiếu; trả về số dòng không còn tìm thấy.
function vtbPhoneApply(slip, sign){
  const keys = vtbPhoneKeys(vtbState.rows);
  const idx = new Map(keys.map((k, i) => [k, i]).filter(([k]) => k));
  let missing = 0;
  slip.lines.forEach(l => {
    const i = idx.get(l.key);
    if(i === undefined){ missing++; return; }
    vtbState.rows[i].sl = vtbNum(vtbState.rows[i].sl) + sign * (Number(l.qty) || 0);
  });
  return missing;
}
function vtbPhoneRefreshView(){ /* PC 1.62: không còn trang Vị Trí Bột để vẽ lại */ }
async function importPhonePhieuVTB(list){
  try{ await m1InitDone; }catch(e){ if(window.klLog) klLog('ton-kho-an-an.js:9668',e); }
  const keys = vtbPhoneKeys(vtbState.rows);
  const idx = new Map(keys.map((k, i) => [k, i]).filter(([k]) => k));
  const importedIds = [];
  let newCount = 0;
  const now = new Date().toISOString();
  (list || []).forEach(ph => {
    if(vtbPhoneSlips.some(sl => sl.phoneId === ph.id)){ importedIds.push(ph.id); return; }
    const pos = (ph.items || []).map(it => idx.get(String(it.itemKey || '').trim()));
    if(!pos.length || pos.some(i => i === undefined)){ console.error('Phiếu điện thoại (Vị Trí Bột) không khớp dòng:', ph.id); return; }
    const lines = ph.items.map((it, n) => {
      const r = vtbState.rows[pos[n]];
      const qty = Number(it.soLuongXuat) || 0;
      r.sl = vtbNum(r.sl) - qty;
      return { key: String(it.itemKey), vt: r.vt, ma: r.ma, nsx: r.nsx, hsd: r.hsd, nhap: r.nhap, ghiChu: r.ghiChu, qty };
    });
    vtbPhoneSlips.unshift({
      id: 'VTB-' + String(vtbPhoneSeq++).padStart(4, '0'),
      phoneId: ph.id, maPhieu: ph.maPhieu || '', nguoiTao: ph.nguoiTao || '', ghiChu: ph.ghiChu || '',
      createdAt: ph.ngayTao || now, lines, deducted: true, deductedAt: now, loadAt: Number(vtbState.importedAt) || 0, // PC 3.28: gắn đợt nạp để khóa khi nạp file mới
    });
    importedIds.push(ph.id);
    newCount++;
  });
  if(newCount){
    let okRows = true;
    try{ okRows = (await persistViTriBot()) === 'excel'; }catch(e){ okRows = false; }
    const okSlips = await persistVtbPhoneSlips();
    vtbPhoneRefreshView();
    if(hasDbAPI() && (!okRows || !okSlips)) return { ok:false, error:'Tồn kho An An không lưu được dữ liệu Vị Trí Bột vào Excel (file đang mở trong Excel?).' };
  }
  return { ok:true, importedIds, newCount };
}

function renderAll(){
  clearRenderCaches();
  if(!m1EnsureAllowedModule()){
    renderNav();
    const c0 = document.getElementById('content');
    if(c0) c0.innerHTML = '<div class="ld-empty" style="margin:40px auto;max-width:520px;text-align:center">Tài khoản của bạn chưa được cấp quyền Tồn kho An An nào (Tồn An An / Vị trí bột).<br>Nhờ quản trị bật ở Cài đặt › Tài khoản.</div>';
    return;
  }
  const __activeMod = MODULES.find(m=>m.id===state.activeModule);
  if(__activeMod && __activeMod.group==='phanloai' && !__activeMod.tab) state.phanLoaiSub = state.activeModule;
  renderNav();
  renderTopbar();
  if(state.activeModule === 'dkloc'){
    renderDkLoc();
  } else if(FILTER_MODULES[state.activeModule]){
    renderFilterModule(state.activeModule);
  } else if(state.activeModule === 'loaddata' || state.activeModule === 'dulieu'){
    renderLoadData();
  } else if(state.activeModule === 'baocao'){
    renderBaoCao();
  } else if(state.activeModule === 'dactinh'){
    renderDacTinh();
  } else if(state.activeModule === 'tonghop'){
    renderTongHop();
  } else if(state.activeModule === 'theodoi' && window.renderTheoDoiTon){
    window.renderTheoDoiTon(); // PC 3.92
  } else {
    renderPlaceholder();
  }
  if(window.tdtAfterRender) window.tdtAfterRender(); // PC 3.92: chụp ảnh tồn tháng cho Theo Dõi Tồn
  initExcelLikeSelectionForContent();
  requestAnimationFrame(syncTopbarHeightVar);
}

/* ================= CLOCK =================
   Đồng hồ sidebar đã được ẩn trong module này theo yêu cầu. */

/* ================= INIT ================= */
document.getElementById('fileInput').addEventListener('change', e=>{
  if(e.target.files.length) handleFile(e.target.files[0]);
  e.target.value = '';
});

// Listener kéo-thả cột (module Load Data) — gắn một lần duy nhất ở cấp document,
// dùng biến toàn cục ldColDrag để tránh chồng listener mỗi lần renderLoadData().
document.addEventListener('mousemove', e=>{
  if(!ldColDrag) return;
  const dx = e.clientX - ldColDrag.startX;
  if(Math.abs(dx) >= 2) ldColDrag.moved = true;
  const newW = Math.max(LD_COL_MIN_WIDTH, Math.round(ldColDrag.startWidth + dx));
  ldColDrag.colEl.style.width = newW + 'px';
  // QUAN TRỌNG: khoá lại độ rộng THẬT của <table> theo tổng <col> mới NGAY trong lúc kéo —
  // thiếu bước này, <table> vẫn ở "auto" nên trình duyệt tính lại theo nội dung ô thay vì
  // theo đúng con số vừa kéo (xem chú thích ở syncTableWidthToCols()), khiến việc kéo hẹp/
  // rộng cột trông như "không ăn".
  syncTableWidthToCols(ldColDrag.colEl.closest('table'));
});
document.addEventListener('mouseup', ()=>{
  if(!ldColDrag) return;
  // Chỉ lưu khi THẬT SỰ có kéo — trước đây 1 cú nhấp (kể cả cú nhấp đầu của nhấp đúp) vào viền
  // cũng ghim cứng độ rộng hiện tại vào state, khiến cột không bao giờ tự căn lại theo dữ liệu.
  if(ldColDrag.moved) state.colWidths[ldColDrag.ci] = parseInt(ldColDrag.colEl.style.width, 10);
  ldColDrag = null;
  document.querySelectorAll('#dataWrap .col-resizer.resizing').forEach(h=>h.classList.remove('resizing'));
  document.body.style.cursor = '';
  document.body.style.userSelect = '';
});

// Listener kéo-thả cột cho các module lọc dùng chung (Thành Phẩm Chính, HLSO…)
// — gắn một lần duy nhất ở cấp document, dùng biến toàn cục filterModuleColDrag để tránh
// chồng listener mỗi lần renderFilterModule() vẽ lại bảng.
document.addEventListener('mousemove', e=>{
  if(!filterModuleColDrag) return;
  const dx = e.clientX - filterModuleColDrag.startX;
  if(Math.abs(dx) >= 2) filterModuleColDrag.moved = true;
  const newW = Math.max(FM_COL_MIN_WIDTH, Math.round(filterModuleColDrag.startWidth + dx));
  filterModuleColDrag.colEl.style.width = newW + 'px';
  // Xem chú thích tương ứng ở listener kéo cột "ldColDrag" phía trên.
  syncTableWidthToCols(filterModuleColDrag.colEl.closest('table'));
});
document.addEventListener('mouseup', ()=>{
  if(!filterModuleColDrag) return;
  // Xem chú thích tương ứng ở listener mouseup của ldColDrag phía trên.
  if(filterModuleColDrag.moved) state.filterModules[filterModuleColDrag.modId].colWidths[filterModuleColDrag.label] = parseInt(filterModuleColDrag.colEl.style.width, 10);
  const modId = filterModuleColDrag.modId;
  filterModuleColDrag = null;
  document.querySelectorAll(`#${modId}Wrap .col-resizer.resizing`).forEach(h=>h.classList.remove('resizing'));
  document.body.style.cursor = '';
  document.body.style.userSelect = '';
});

/* ============================================================
   KÉO GIÃN CỘT/DÒNG KHI RÊ CHUỘT VÀO BẤT KỲ VIỀN NÀO
   Bổ sung thêm cho các tay cầm ".col-resizer" ở trên (vẫn hoạt động bình
   thường, không đổi): giờ đây có thể rê chuột vào viền trái/phải của BẤT
   KỲ ô nào (tiêu đề hay ô dữ liệu) để đổi độ rộng cột, và viền dưới của
   một ô dữ liệu để đổi chiều cao của dòng đó — áp dụng cho cả bảng Load
   Data (#dataWrap) lẫn mọi bảng của 7 module lọc (#{modId}Wrap).
   Cột: tái sử dụng ĐÚNG biến kéo (ldColDrag/filterModuleColDrag) và các
   listener mousemove/mouseup đã có ở trên, nên vẫn lưu vào state.colWidths
   như cũ. Dòng: dùng 2 biến kéo mới (ldRowDrag/filterModuleRowDrag), lưu
   vào state.rowHeights / state.filterModules[modId].rowHeights (chỉ tồn
   tại trong phiên làm việc hiện tại, giống hệt cách colWidths đang hoạt
   động — không ghi ra database Excel/localStorage).
============================================================ */
const BORDER_RESIZE_EDGE = 6; // px — khoảng cách tính từ viền để coi là đang rê viền
const ROW_RESIZE_MIN_HEIGHT = 24;
let ldRowDrag = null;
let filterModuleRowDrag = null;

function borderResizeRowIdx(tr){
  const idxEl = tr.querySelector('[data-idx]');
  return idxEl ? idxEl.getAttribute('data-idx') : null;
}

// Con trỏ chuột: đổi thành col-resize/row-resize khi rê gần viền, kể cả khi không đứng ngay
// trên tay cầm ".col-resizer". Bỏ qua hoàn toàn khi đang kéo dở (đã có listener riêng lo phần
// đó) hoặc khi đang rê ngay trên tay cầm (đã có cursor riêng của CSS cho ".col-resizer").
document.addEventListener('mousemove', e=>{
  if(ldColDrag || filterModuleColDrag || ldRowDrag || filterModuleRowDrag) return;
  const cell = e.target.closest && e.target.closest('td, th');
  if(!cell || !cell.closest('.data-wrap')) return;
  if(e.target.closest('.col-resizer')) return;
  const tr = cell.parentElement;
  const posIdx = Array.from(tr.children).indexOf(cell);
  const rect = cell.getBoundingClientRect();
  const nearRight = posIdx > 0 && rect.right - e.clientX <= BORDER_RESIZE_EDGE;
  const nearLeft = posIdx > 1 && e.clientX - rect.left <= BORDER_RESIZE_EDGE;
  const nearBottom = tr.parentElement && tr.parentElement.tagName === 'TBODY' && rect.bottom - e.clientY <= BORDER_RESIZE_EDGE;
  if(nearRight || nearLeft) cell.style.cursor = 'col-resize';
  else if(nearBottom) cell.style.cursor = 'row-resize';
  else cell.style.cursor = '';
});

// SỬA LỖI "nhấp đúp vào viền cột không tự căn theo dữ liệu": tính năng rê viền ô ở dưới chỉ có
// mousedown (kéo) — KHÔNG có dblclick, nên nhấp đúp vào viền (thay vì trúng đúng tay cầm 6px)
// không có tác dụng gì. Nay nhấp đúp viền trái/phải của bất kỳ ô tiêu đề/ô dữ liệu nào → bung
// cột tương ứng vừa khít dữ liệu dài nhất (giống Excel). Dùng đúng quy tắc xác định cột như
// listener mousedown bên dưới.
document.addEventListener('dblclick', e=>{
  const cell = e.target.closest && e.target.closest('td, th');
  if(!cell || cell.closest('tfoot')) return;
  if(e.target.closest('.col-resizer')) return; // tay cầm đã có dblclick riêng
  const wrap = cell.closest('.data-wrap');
  if(!wrap) return;
  const table = cell.closest('table');
  const tr = cell.parentElement;
  const rect = cell.getBoundingClientRect();
  const posIdx = Array.from(tr.children).indexOf(cell);
  const nearRight = posIdx > 0 && rect.right - e.clientX <= BORDER_RESIZE_EDGE;
  const nearLeft = posIdx > 1 && e.clientX - rect.left <= BORDER_RESIZE_EDGE;
  if(!nearRight && !nearLeft) return;
  const colgroup = table && table.querySelector(':scope > colgroup');
  if(!colgroup) return;
  const colEl = colgroup.children[nearLeft ? posIdx - 1 : posIdx];
  if(!colEl) return;
  e.preventDefault();
  if(wrap.id === 'dataWrap'){
    if(colEl.dataset.colIndex !== undefined) autoFitLdColumn(colEl.dataset.colIndex);
  } else {
    const modId = wrap.id.slice(0, -4);
    if(colEl.dataset.colLabel && state.filterModules[modId]) autoFitFilterModuleColumn(modId, colEl.dataset.colLabel);
  }
});

document.addEventListener('mousedown', e=>{
  if(ldColDrag || filterModuleColDrag || ldRowDrag || filterModuleRowDrag) return;
  const cell = e.target.closest && e.target.closest('td, th');
  if(!cell) return;
  if(e.target.closest('.col-resizer')) return; // đã có tay cầm riêng xử lý click này
  const wrap = cell.closest('.data-wrap');
  if(!wrap) return;
  const table = cell.closest('table');
  const tr = cell.parentElement;
  const rect = cell.getBoundingClientRect();
  const posIdx = Array.from(tr.children).indexOf(cell);
  const nearRight = posIdx > 0 && rect.right - e.clientX <= BORDER_RESIZE_EDGE;
  const nearLeft = posIdx > 1 && e.clientX - rect.left <= BORDER_RESIZE_EDGE;
  const nearBottom = tr.parentElement && tr.parentElement.tagName === 'TBODY' && rect.bottom - e.clientY <= BORDER_RESIZE_EDGE;

  if(nearRight || nearLeft){
    const colgroup = table && table.querySelector(':scope > colgroup');
    if(!colgroup) return;
    let pos = nearLeft ? posIdx - 1 : posIdx;
    const colEl = colgroup.children[pos];
    if(!colEl) return;
    e.preventDefault();
    if(wrap.id === 'dataWrap'){
      const ci = colEl.dataset.colIndex;
      if(ci === undefined) return;
      ldColDrag = { ci, colEl, startX: e.clientX, startWidth: colEl.getBoundingClientRect().width };
    } else {
      const modId = wrap.id.slice(0, -4);
      const label = colEl.dataset.colLabel;
      if(!label || !state.filterModules[modId]) return;
      filterModuleColDrag = { modId, label, colEl, startX: e.clientX, startWidth: colEl.getBoundingClientRect().width };
    }
    document.body.style.cursor = 'col-resize';
    document.body.style.userSelect = 'none';
    return;
  }

  if(nearBottom){
    const rowIdx = borderResizeRowIdx(tr);
    if(rowIdx === null) return;
    e.preventDefault();
    if(wrap.id === 'dataWrap'){
      ldRowDrag = { idx: rowIdx, tr, startY: e.clientY, startHeight: tr.getBoundingClientRect().height };
    } else {
      const modId = wrap.id.slice(0, -4);
      if(!state.filterModules[modId]) return;
      filterModuleRowDrag = { modId, idx: rowIdx, tr, startY: e.clientY, startHeight: tr.getBoundingClientRect().height };
    }
    document.body.style.cursor = 'row-resize';
    document.body.style.userSelect = 'none';
  }
});

document.addEventListener('mousemove', e=>{
  if(!ldRowDrag) return;
  const dy = e.clientY - ldRowDrag.startY;
  ldRowDrag.tr.style.height = Math.max(ROW_RESIZE_MIN_HEIGHT, Math.round(ldRowDrag.startHeight + dy)) + 'px';
});
document.addEventListener('mouseup', ()=>{
  if(!ldRowDrag) return;
  state.rowHeights[ldRowDrag.idx] = parseInt(ldRowDrag.tr.style.height, 10);
  ldRowDrag = null;
  document.body.style.cursor = '';
  document.body.style.userSelect = '';
});

document.addEventListener('mousemove', e=>{
  if(!filterModuleRowDrag) return;
  const dy = e.clientY - filterModuleRowDrag.startY;
  filterModuleRowDrag.tr.style.height = Math.max(ROW_RESIZE_MIN_HEIGHT, Math.round(filterModuleRowDrag.startHeight + dy)) + 'px';
});
document.addEventListener('mouseup', ()=>{
  if(!filterModuleRowDrag) return;
  const st = state.filterModules[filterModuleRowDrag.modId];
  if(st){
    if(!st.rowHeights) st.rowHeights = {};
    st.rowHeights[filterModuleRowDrag.idx] = parseInt(filterModuleRowDrag.tr.style.height, 10);
  }
  filterModuleRowDrag = null;
  document.body.style.cursor = '';
  document.body.style.userSelect = '';
});
/* ===== NHẬN PHIẾU XUẤT TỪ ĐIỆN THOẠI (xem pullPhieuXuatVeLocal() trong main.js) =====
   Module này giữ state.rows + state.xuatSlips trong bộ nhớ và ghi đè cả sheet mỗi lần lưu, nên khi
   đang mở thì main.js KHÔNG tự ghi phiếu điện thoại vào Excel (sẽ bị ghi đè mất) — main.js gửi phiếu
   sang đây, module tự thêm vào "Tổng Hợp > Xuất Hàng" và TRỪ NGAY "Tồn cuối" (giống bấm "Trừ tồn
   kho"; vẫn "Hủy trừ tồn kho" được như phiếu thường). Đăng ký PHẢI chạy trước init() bên dưới.
   Dòng được xác định bằng itemKey: "id:<Item ID>" hoặc "r:<số thứ tự dòng>" — GIỐNG cột "_key" mà
   readModule1LoadDataLabeled() trong main.js đẩy lên Google Sheet.
   Phiếu nguồn "Tồn theo vị trí" (kind 'vitri') từ PC 2.49 do module Tồn Vị Trí An An nhận (js/ton-vi-tri-an-an.js);
   phiếu nguồn "Vị Trí Bột" (payload.kind === 'vitribot') đi nhánh importPhonePhieuVTB(). */
let m1InitDone = Promise.resolve();
function m1FindPhoneRow(it){
  const key = String(it.itemKey || '');
  const idIdx = state.headers.findIndex(h => fuzzyNormalize(h) === 'item id');
  if(key.startsWith('id:') && idIdx >= 0){
    const want = key.slice(3);
    return state.rows.findIndex(r => String(r[idIdx] ?? '').trim() === want);
  }
  const i = Number(key.startsWith('r:') ? key.slice(2) : it.rowIndex);
  if(!Number.isInteger(i) || i < 0 || i >= state.rows.length) return -1;
  const maIdx = state.headers.findIndex(h => fuzzyNormalize(h) === fuzzyNormalize('Mã hàng'));
  if(maIdx >= 0 && it.maHang && String(state.rows[i][maIdx] ?? '').trim() !== String(it.maHang).trim()) return -1;
  return i;
}
async function importPhonePhieuM1(list){
  try{ await m1InitDone; }catch(e){ if(window.klLog) klLog('ton-kho-an-an.js:9974',e); }
  const ci = findTonCuoiColIndex();
  if(ci === -1) return { ok:false, error:'Tồn kho An An không có cột "Tồn cuối" — không nhập được phiếu.' };
  const importedIds = [];
  let newCount = 0;
  const now = new Date().toISOString();
  (list || []).forEach(ph=>{
    if(state.xuatSlips.some(sl => sl.phoneId === ph.id)){ importedIds.push(ph.id); return; }
    const lines = [];
    let bad = false;
    (ph.items || []).forEach(it=>{
      const i = m1FindPhoneRow(it);
      if(i < 0){ bad = true; return; }
      lines.push({ i, maHang: String(it.maHang ?? ''), tenHang: String(it.tenHang ?? ''), size: String(it.size ?? ''), hopDong: String(it.hopDong ?? ''), qty: Number(it.soLuongXuat) || 0 });
    });
    if(bad || !lines.length){ console.error('Phiếu điện thoại không khớp dòng Load Data:', ph.id); return; }
    lines.forEach(l=>{
      const cur = parseLooseNumber(state.rows[l.i][ci]);
      state.rows[l.i][ci] = (isNaN(cur) ? 0 : cur) - l.qty;
    });
    state.xuatSlips.unshift({
      id: 'PX-' + String(state.xuatSlipSeq++).padStart(4,'0'),
      moduleId: 'dienthoai',
      moduleLabel: '📱 Điện thoại' + (ph.nguoiTao ? ' — ' + ph.nguoiTao : '') + (ph.maPhieu ? ' (' + ph.maPhieu + ')' : ''),
      createdAt: ph.ngayTao || now,
      lines, deducted: true, deductedAt: now,
      phoneId: ph.id, phoneMaPhieu: ph.maPhieu || '', nguoiTao: ph.nguoiTao || '', ghiChu: ph.ghiChu || '',
    });
    importedIds.push(ph.id);
    newCount++;
  });
  if(newCount){
    const where = await persist();
    const okSlips = await persistXuatHang();
    try{ renderXuatHangSlipList(); }catch(e){ window.klLog && klLog('ton-kho-an-an.js:8317', e); }
    try{ if(document.getElementById('xhSourceTableWrap') || state.xuatHangSourceModule) renderXuatHangSourceTable(); }catch(e){ window.klLog && klLog('ton-kho-an-an.js:8318', e); }
    if(hasDbAPI() && (where !== 'excel' || !okSlips)){
      return { ok:false, error:'Tồn kho An An không lưu được database Excel (file đang mở trong Excel?).' };
    }
  }
  return { ok:true, importedIds, newCount };
}
/* ================= BÁO CÁO EXCEL CHO ĐIỆN THOẠI =================
   main.js (khi đẩy bảng M01 lên Google Sheet) nhờ Module 01 đang mở dựng SẴN 2 file Excel —
   "Báo Cáo Tổng Tồn" và "Bảng Tổng Hợp HLSO" — bằng ĐÚNG các hàm của nút Xuất Excel trên PC
   (buildBaoCaoAllModulesWorkbook / buildHlsoSummaryWorkbook), rồi gửi dạng base64 lên tab BaoCao_M01.
   Điện thoại chỉ tải file về → lưu / chia sẻ, KHÔNG tự tính lại (công thức Báo Cáo quá phức tạp để chép).
   Ngày báo cáo = "Ngày số liệu" của Load Data (đúng giá trị hộp thoại PC điền sẵn), thiếu thì lấy hôm nay.
   Mỗi file dựng trong try/catch riêng: 1 file lỗi không làm mất file còn lại. */
/* ================= PHÂN LOẠI CHO ĐIỆN THOẠI (PC 1.56 / ĐT 1.12) =================
   Dùng LẠI đúng dữ liệu đã phân loại của 8 module (filterModuleBaseRowsCached) và đúng cách tính "Tổng KL (kg)" (Tồn cuối × Trọng lượng):
   mỗi loại → tổng NET + từng Size (số dòng, NET) + tối đa 25 mã hàng lớn nhất trong Size. Thành Phẩm Chính: mã × hợp đồng "Sắp hết"
   theo ngưỡng đang đặt (getTpcRowStatus). Đi chung đường với file báo cáo (tab BaoCao_M01, file key 'phanloai') nên không đổi Apps Script. */
function plUtf8B64(obj){
  const bytes = new TextEncoder().encode(JSON.stringify(obj));
  let bin = '';
  for(let i = 0; i < bytes.length; i += 0x8000) bin += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
  return btoa(bin);
}
function plBuildCross(sizeList, cell, dtTot, maxCols){
  // PC 1.64 — bảng chéo Size × Đặc tính (kg) cho điện thoại: tối đa maxCols đặc tính lớn nhất, phần còn lại gộp cột "Khác"
  const r1 = (n) => Math.round(n * 10) / 10;
  const order = Array.from(dtTot.entries()).sort((a, b) => b[1] - a[1]).map(x => x[0]);
  const main = order.slice(0, maxCols), rest = order.slice(maxCols);
  const cols = main.map(l => ({ label: l })).concat(rest.length ? [{ label: 'Khác', other: rest.length }] : []);
  const rows = sizeList.map(sz => {
    const m = cell.get(sz.label) || new Map();
    const cells = main.map(l => r1(m.get(l) || 0));
    if(rest.length) cells.push(r1(rest.reduce((a, l) => a + (m.get(l) || 0), 0)));
    let tot = 0; m.forEach(v => { tot += v; });
    return { label: sz.label, cells, total: r1(tot) };
  });
  return { cols, rows, totals: cols.map((_, i) => r1(rows.reduce((a, r) => a + r.cells[i], 0))), total: r1(rows.reduce((a, r) => a + r.total, 0)) };
}
function buildPhanLoaiSnapshot(){
  const out = { v:1, nguon:'m01', threshold: Number(state.tpcLowStockThreshold), loai:[] };
  if(!state.rows.length || !state.headers.length) return out;
  const hdr = (name) => state.headers.findIndex(h => headersFuzzyMatch(h, name));
  const sizeCi = hdr('Size'), maCi = hdr('Mã hàng'), dtCi = hdr('Đặc tính'), hdCi = hdr('Hợp đồng');
  const r1 = (n) => Math.round(n * 10) / 10;
  MODULES.filter(m => m.group === 'phanloai' && !m.tab && FILTER_MODULES[m.id]).forEach(mod => {
    const basis = getFilterModuleWeightBasis(mod.id); if(!basis) return;
    const isTpc = mod.id === 'tpc', info = isTpc ? getTpcStatusInfo() : null;
    const bySize = new Map(), crossCell = new Map(), dtTot = new Map(); let total = 0, rows = 0;
    filterModuleBaseRowsCached(mod.id).forEach(({row}) => {
      const v = filterModuleRowTotalWeight(row, basis); rows++;
      const size = sizeCi >= 0 ? String(row[sizeCi] ?? '').trim() : '';
      const label = size || '(Không có size)';
      let sz = bySize.get(label); if(!sz){ sz = { label, net:0, rows:0, items:new Map() }; bySize.set(label, sz); }
      sz.rows++; if(v !== null){ sz.net += v; total += v; }
      if(v !== null && dtCi >= 0){ // bảng chéo Size × Đặc tính
        const dtl = String(row[dtCi] ?? '').trim() || '(Trống)';
        let cm = crossCell.get(label); if(!cm){ cm = new Map(); crossCell.set(label, cm); }
        cm.set(dtl, (cm.get(dtl) || 0) + v); dtTot.set(dtl, (dtTot.get(dtl) || 0) + v);
      }
      const ma = maCi >= 0 ? String(row[maCi] ?? '').trim() : '', hd = (isTpc && hdCi >= 0) ? String(row[hdCi] ?? '').trim() : '';
      const key = ma + '|' + hd; let it = sz.items.get(key);
      if(!it){ it = { ma, sub:hd, dt: dtCi >= 0 ? String(row[dtCi] ?? '').trim() : '', net:0, low:false }; sz.items.set(key, it); }
      if(v !== null) it.net += v;
      if(info && getTpcRowStatus(info, row) === 'Sắp hết') it.low = true;
    });
    const sizes = Array.from(bySize.values()).map(sz => {
      const all = Array.from(sz.items.values()).sort((a, b) => b.net - a.net);
      const low = all.filter(i => i.low).length;
      return { label: sz.label, net: r1(sz.net), rows: sz.rows, low, more: Math.max(0, all.length - 25),
        items: all.slice(0, 25).map(i => ({ ma:i.ma, sub:i.sub, dt:i.dt, net:r1(i.net), low: i.low ? 1 : 0 })) };
    }).sort((a, b) => compareSizeValues(a.label, b.label));
    const lo = { id: mod.id, name: mod.label, color: mod.color, net: r1(total), rows, sizes };
    if(dtTot.size) lo.cross = plBuildCross(sizes, crossCell, dtTot, 6);
    out.loai.push(lo);
  });
  return out;
}
async function buildPhoneReportSnapshot(){
  try{ await m1InitDone; }catch(e){ if(window.klLog) klLog('ton-kho-an-an.js:10088',e); }
  const now = new Date();
  const p2 = n => String(n).padStart(2,'0');
  const today = `${p2(now.getDate())}/${p2(now.getMonth()+1)}/${now.getFullYear()}`;
  const dateStr = state.dataDate || today;
  // importedAt: để main.js so với LoadDataMeta trong database — khác nhau = trang này đang giữ số liệu CŨ (PC 2.6)
  const snap = { ok:true, generatedAt: now.toISOString(), dataDate: state.dataDate || '', importedAt: state.importedAt || null, reportDate: dateStr, files: [], errors: [] };
  if(!state.rows.length || !state.headers.length) return snap; // chưa nạp Load Data — không có báo cáo
  // Phiếu điện thoại vừa trừ "Tồn cuối" có thể chưa kịp vẽ lại → xóa cache để tính trên số mới nhất.
  try{ clearRenderCaches(); }catch(e){ window.klLog && klLog('ton-kho-an-an.js:8406', e); }
  const stamp = exportFileStamp();
  const jobs = [
    { key:'tongton', title:'Báo cáo tổng tồn (AA)', name:`BaoCao_TongTon_7Module_${stamp}.xlsx`, build: () => buildBaoCaoAllModulesWorkbook(dateStr) },
    { key:'hlso', title:'Báo cáo tổng hợp HLSO (AA)', name:`BangTongHopHLSO_${stamp}.xlsx`, build: () => buildHlsoSummaryWorkbook(dateStr) },
  ];
  for(const job of jobs){
    try{
      const wb = job.build();
      if(!wb) continue;
      const zip = await buildFrozenWorkbookZip(wb);
      const b64 = await zip.generateAsync({ type:'base64', compression:'DEFLATE' });
      snap.files.push({ key: job.key, title: job.title, name: job.name, b64 });
    }catch(e){
      console.error('Lỗi dựng file báo cáo cho điện thoại (' + job.title + '):', e);
      snap.errors.push(job.title + ': ' + String(e && e.message || e));
    }
  }
  // PC 1.56: dữ liệu "Phân loại" cho điện thoại — để SAU CÙNG (điện thoại lấy file Excel đầu tiên làm mốc giờ lập). Lỗi ở đây không ảnh hưởng 2 file Excel.
  try{
    const pl = buildPhanLoaiSnapshot();
    if(pl.loai.length) snap.files.push({ key:'phanloai', title:'Phân loại (AA)', name:'PhanLoai_M01.json', b64: plUtf8B64(pl) });
  }catch(e){
    console.error('Lỗi dựng dữ liệu Phân loại cho điện thoại (Module 01):', e);
    snap.errors.push('Phân loại: ' + String(e && e.message || e));
  }
  return snap;
}

if(typeof window !== 'undefined' && window.phoneImportBridge){
  window.phoneImportBridge.onImportRequest(async (payload)=>{
    if(payload && payload.kind === 'baocao'){
      let snap;
      try{ snap = await buildPhoneReportSnapshot(); }
      catch(e){ console.error('Lỗi dựng báo cáo điện thoại (Module 01):', e); snap = { ok:false, error:String(e && e.message || e) }; }
      try{ await window.phoneImportBridge.sendResult(payload.reqId, snap); }catch(e){ if(window.klLog) klLog('ton-kho-an-an.js:10132',e); }
      return;
    }
    let result;
    try{
      result = (payload && payload.kind === 'vitribot')
        ? await importPhonePhieuVTB(payload.list)
        : await importPhonePhieuM1(payload && payload.list);
    }
    catch(e){ console.error('Lỗi nhập phiếu xuất điện thoại (Module 01):', e); result = { ok:false, error:String(e && e.message || e) }; }
    try{ await window.phoneImportBridge.sendResult(payload && payload.reqId, result); }catch(e){ if(window.klLog) klLog('ton-kho-an-an.js:10144',e); }
  });
  window.phoneImportBridge.register('ton-kho-an-an').catch(()=>{});
}

m1InitDone = (async function init(){
  await m1LoadPerm();
  await restore();
  // khôi phục riêng "Đặc Tính"/"Gộp Đặc Tính" (ưu tiên đọc từ database Excel qua window.dbAPI,
  // dự phòng localStorage) — luôn chạy đúng dù restore() ở trên (dựa vào window.storage bị
  // lỗi) có thành công hay không. PHẢI await: đây là lệnh gọi IPC bất đồng bộ sang main.js khi
  // chạy trong Electron, nếu không chờ thì renderAll() bên dưới có thể vẽ giao diện TRƯỚC khi
  // dữ liệu Đặc Tính đọc xong, khiến lần đầu mở app hiện sai (trống) rồi mới cập nhật lại.
  await restoreDacTinh();
  await restoreXuatHang();
  await restoreNgoaiSoSach();
  await restoreCustomFilters();
  await restoreViTriBot(); // dữ liệu "Vị Trí Bột" (sheet riêng ViTriBotMeta/ViTriBot)
  await restoreVtbPhoneSlips(); // phiếu điện thoại xuất từ Vị Trí Bột (sheet ViTriBotPhieuDienThoai)
  if(window.tdtRestore) await window.tdtRestore(); // PC 3.92: Theo Dõi Tồn (sheet TheoDoiTon)
  renderAll();
})();

/* PC 3.25 — LÀM MỚI TẠI CHỖ (js/lan-status.js › refreshFrame): máy khác vừa ghi → đọc lại số liệu + phiếu rồi vẽ lại, KHÔNG tải lại cả khung. Lỗi → ném ra để trang tự tải lại như cũ. */
window.__klaRefresh = async function(){
  await restore(); await restoreDacTinh(); await restoreXuatHang(); await restoreNgoaiSoSach(); await restoreCustomFilters();
  await restoreViTriBot(); await restoreVtbPhoneSlips();
  if(window.tdtRestore) await window.tdtRestore();
  renderAll();
};

/* Liên kết sâu từ "Phiếu Xuất Toàn Hệ Thống" (Module 09 — xem js/deeplink.js): chuyển tới đúng nơi hiển thị
   phiếu xuất của Module 01. view: 'tonghop-xuathang' (Tổng Hợp > Xuất Hàng) | 'vitribot' (từ PC 2.49 'tonvitri' do Tồn Vị Trí An An nhận). */
window.__klaOpenView = async function(view){
  try{ await m1InitDone; }catch(e){ if(window.klLog) klLog('ton-kho-an-an.js:10171',e); }
  state.activeModule = 'tonghop'; state.tonghopSubTab = 'xuathang'; // PC 2.49: 'tonvitri' do module Tồn Vị Trí An An nhận
  renderAll(); // renderAll tự chuyển sang menu được phép nếu tài khoản không có quyền mục này
};


/* PC 7.2: quyền có thể vừa đổi ở Cài đặt › Tài khoản — đọc lại khi quay về module, đổi thì vẽ lại menu. */
window.addEventListener('focus', async function(){
  const before = JSON.stringify(m1Pc);
  await m1LoadPerm();
  if(JSON.stringify(m1Pc) !== before){ try{ await m1InitDone; }catch(e){ if(window.klLog) klLog('ton-kho-an-an.js:10183',e); } renderAll(); }
});

// PC 3.12 — nút "⟲ Lịch sử nạp" (xem các đợt nạp Excel + HOÀN TÁC đợt mới nhất)
window.addEventListener('load', ()=>{ try{ if(window.NapExcel) NapExcel.mountButton({ module: 'ton-kho-an-an', title: 'Tồn kho An An', sheets: [LOADDATA_DB_SHEET, LOADDATA_META_SHEET], onUndone: ()=>{ try{ if(window.parent && window.parent !== window) window.parent.postMessage({ type:'kla-loaddata-changed' }, '*'); }catch(e){} location.reload(); } }); }catch(e){ console.error(e); } });
