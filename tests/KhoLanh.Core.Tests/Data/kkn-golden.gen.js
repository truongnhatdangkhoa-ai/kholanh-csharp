// Sinh đáp án mẫu cho Kiểm kê nhanh (C#) bằng CHÍNH kk-nhanh-core.js của app cũ. Chạy: cd tests/KhoLanh.Core.Tests/Data && node kkn-golden.gen.js
const fs = require('fs');
const C = require('/home/user/kholanh-anan-pc/app/html/js/kk-nhanh-core.js');
const aoas = [
  [['BÁO CÁO TỒN KHO'], [], ['STT', 'Mã hàng', 'Tên hàng', 'Tồn cuối', 'Trọng lượng']],
  [['Mã  Hàng', 'Tên', 'Net (kg)', 'Số lượng']],
  [['x'], ['Mã hàng', 'Tên', 'Net', 'Vị trí']],
  [['Mã hàng', 'Khách hàng', 'Số kiện', 'Net']],
  [['MaHang', 'Mã cũ', 'SL']],
  [['Tên', 'Số lượng'], ['a', 1]],
  [['Ma hang', 'TLG']],
  [['Mã hàng', 'Tồn kho', 'Kho gửi']],
  [],
];
const detect = aoas.map((a) => C.detectKind(a));
const rows = [
  { key: 'A1', ma: 'A1', ten: 'Tôm sú', status: 'Khớp', dKg: 0 },
  { key: 'A2', ma: 'A2', ten: '', status: 'Thừa', dKg: 12.345 },
  { key: 'A3', ma: 'A3', ten: 'Cá', status: 'Thiếu', dKg: -40 },
  { key: 'A4', ma: 'a4', ten: 'Mực ống', status: 'Lệch kg', dKg: 3.333 },
  { key: 'B5', ma: 'B5', ten: '', status: 'Chỉ có ở kiểm kê', dKg: 20 },
  { key: 'B6', ma: 'B6', ten: 'Ghẹ', status: 'Chưa kiểm kê', dKg: -20 },
  { key: 'B7', ma: 'B7', ten: 'Ghẹ  xanh', status: 'Chỉ có ở kiểm kê', dKg: 40 },
  { key: 'B8', ma: 'B8', ten: 'x', status: 'Chưa kiểm kê', dKg: -40 },
  { key: 'C9', ma: 'C 9', ten: 'Tôm thẻ', status: 'Thiếu', dKg: -40 },
];
const nnArr = [['B5', { code: 'sai-ma', side: 'kk', fixTo: 'B6', strong: true, level: 'ng' }], ['B6', { code: 'sai-ma', side: 'ton', strong: true, level: 'ng' }],
  ['B7', { code: 'sai-ma', side: 'kk', fixTo: 'B8', strong: false, level: 'ng' }], ['A3', { code: 'lech-kien', level: 'xd' }], ['A4', { code: 'cx', level: 'cx' }]];
const nn = new Map(nnArr);
const sum = C.summarize(rows, nn);
const ctxs = [{}, { hasKK: true, hasAn: true, hasKT: true, dirty: 0, locked: false, nPages: 3 }, { hasKK: true, hasAn: false, dirty: 2, locked: true }];
const todo = ctxs.map((c) => C.todo(sum, c).map((t) => [t.id, t.tone, t.act, t.label]));
const filters = [['LECH', ''], ['ALL', ''], ['Thiếu', ''], ['', 'tom'], ['ALL', 'c9'], ['ALL', 'ghe xanh'], ['Khớp', 'zz']];
const filt = filters.map(([f, q]) => C.filterRows(rows, f, q).map((r) => r.key));
const strip = ['Mã  Hàng', 'Đặc tính (kg)', '  TỒN-CUỐI kỳ ', 'Trọng lượng/kiện'].map(C.strip);
fs.writeFileSync('kkn-golden.json', JSON.stringify({ aoas, detect, rows, nn: nnArr, sum, ctxs, todo, filters, filt, strip }, null, 1));
console.log(JSON.stringify(detect.map((d) => d.kind)), JSON.stringify(sum), JSON.stringify(filt));
