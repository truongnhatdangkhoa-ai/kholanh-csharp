// Đáp án mẫu: chứng từ nhập / xuất An An (readMovementAnAn) + đối chiếu có chứng từ An An — sinh bằng lõi JS app cũ.
// Chạy: node cm-golden.gen.js (cần ck-golden.json cùng thư mục, app cũ ở /home/user/kholanh-anan-pc).
const D = '/home/user/kholanh-anan-pc/app/html/quan-ly-kho/';
const I = require(D + 'dc2-import.js'), R = require(D + 'dc2-engine.js');
const ck = JSON.parse(require('fs').readFileSync(__dirname + '/ck-golden.json', 'utf8'));
const A = ck.aoa.anan.slice(4, 15).map((r) => String(r[0]).replace(/^TPC-/, ''));
const [A1, , , A4, , , , , , , A14] = A;
const nhap = [['BÁO CÁO NHẬP TRONG KỲ'], ['Từ ngày 01/10/2026 đến ngày 31/10/2026'], [],
  ['Ngày nhập', 'Số lệnh', 'Mã hàng', 'Tên hàng', 'Loại nhập', 'Số lượng', 'Trọng lượng', 'Net'],
  ['Nhập sản xuất', '', '', '', '', 'Sum: 7', '', 'Sum: 70'],
  ['05/10/2026', 'PN-01', 'TPC-' + A1, 'Tôm HLSO', '', 5, 10, 50],
  [46298, 'PN-02', A4, 'Tôm HLSO 51/60', 'Nhập sản xuất', 2, 10, 20],
  ['Nhập trả hàng', '', '', '', '', '', '', 'Sum: 9'],
  ['07/10/2026', 'PN-03', A4, 'Tôm', 'Nhập khác', 1, 9, 9],           // loại khác tên nhóm
  ['32/10/2026', 'PN-04', A4, 'Tôm', '', 1, 10, 10],                   // ngày lỗi
  ['08/10/2026', '', A1, 'Tôm', '', 1, 10, 10],                        // thiếu số phiếu
  ['08/10/2026', 'PN-05', A1, 'Tôm', '', 1, 10, 'abc'],                // net lỗi
  ['Tổng cộng', '', '', '', '', '', '', 999],
];
const xuat = [['BÁO CÁO XUẤT TRONG KỲ'], [],
  ['Ngày xuất', 'Mã phiếu xuất', 'Kho', 'Mã hàng', 'Tên hàng', 'Khách hàng', 'Số lượng', 'KL Net'],
  ['02/10/2026', 'PX-01', 'K1', A1, 'Tôm', 'CGC', 3, 30],
  ['10/10/2026', 'PX-02', 'K1', A14, 'Tôm YY', 'MATSUOKA', 3, 37.5],
  ['12/10/2026', 'PX-03', 'K1', A4, 'Tôm', '', 1, 10],
  ['15/11/2026', 'PX-04', 'K1', A14, 'Tôm', '', 1, 12.5],               // ngoài kỳ
  [],
  ['', '', '', '', '', '', '', ''],
];
const rn = I.readMovementAnAn(nhap, 'anan_nhap', {}), rx = I.readMovementAnAn(xuat, 'anan_xuat', {});
const ananR = I.readAnAn(ck.aoa.anan, {}), ketoanR = I.readKetoan(ck.aoa.nxt, {}), ctR = I.readCT(ck.aoa.ct, {}), dmR = I.readDanhMuc(ck.aoa.dm);
const period = { from: '2026-10-01', to: '2026-10-31' };
const sources = { anan: ananR, ketoan: Object.assign({}, ketoanR, { extra: { ct: ctR } }), anan_nhap: rn, anan_xuat: rx };
const res = R.reconcile({ sources, ct: ctR.rows.concat(rn.rows, rx.rows), danhMuc: dmR.codes, period, options: {} });
const pick = (r) => ({ ok: r.ok, error: r.error || '', headerRow: r.headerRow, mapping: r.mapping, labels: r.labels, rows: r.rows, period: r.period, stats: r.stats, issues: r.issues });
const out = {
  aoa: { nhap, xuat }, nhap: pick(rn), xuat: pick(rx),
  items: res.items.map((i) => ({ ma: i.ma, diff: i.diff, status: i.status, causes: i.causes.map((c) => c.code + '/' + c.conf), ct: { n: i.ct.lines.length, nFlag: i.ct.nFlag, flags: i.ct.lines.map((l) => l.src + ':' + l.phieu + ':' + l.flags.map((f) => f.code).join(',')) } })),
  validation: res.validation,
};
require('fs').writeFileSync(__dirname + '/cm-golden.json', JSON.stringify(out, null, 1));
console.log(JSON.stringify(pick(rn).stats), JSON.stringify(pick(rx).stats));
console.log(rn.rows.length, rx.rows.length, JSON.stringify(rn.issues), JSON.stringify(res.validation.map((v) => v.text)));
res.items.forEach((i) => console.log(i.ma.slice(-10), i.ct.lines.length, i.ct.lines.map((l) => l.src + ':' + l.phieu + ':' + l.flags.map((f) => f.code).join(',')).join(' ')));
