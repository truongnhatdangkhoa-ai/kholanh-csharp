// Sinh đáp án mẫu cho lõi Số liệu cuối kỳ (C#) bằng chính lõi JS của app cũ.
const D = '/home/user/kholanh-anan-pc/app/html/quan-ly-kho/';
const I = require(D + 'dc2-import.js'), R = require(D + 'dc2-engine.js'), H = require(D + 'dc2-history.js');
// mã 21 ký tự: SP(4) CĐ(1) ĐG(1) HC(1) TT(1) KH(2) MB(3) SIZE(4) TL(2) QC(2)
const M = (sp, size, tl = '10', qc = 'IM', mid = 'XXXR00') => (sp + 'BTN' + mid.slice(0, 1) + mid.slice(1, 3) + mid.slice(3) + size + tl + qc);
const A1 = M('VRHL', '0040'), A2 = M('VRHL', '0041'), A3 = M('VRHL', '0050'), A4 = M('VRHL', '0060'), A5 = M('VRPD', '0070'), A6 = M('VRPD', '0071'), A7 = M('VRPD', '0072');
const A8 = M('VRCO', '0080'), A9 = M('VRCO', '0090'), A10 = M('VRCO', '0100'), A11 = M('VRNO', '0110'), A12 = M('VRNO', '0120'), A13 = M('VRZZ', '0130'), A14 = M('VRYY', '0140');
const anan = [['BÁO CÁO NHẬP XUẤT TỒN KHO'], ['Từ ngày 01/10/2026 đến ngày 31/10/2026'], [],
  ['Mã hàng', 'Tên hàng', 'Tồn đầu', 'Nhập', 'Xuất', 'Tồn cuối', 'Trọng lượng', 'Tồn cuối (NET)'],
  ['TPC-' + A1, 'Tôm HLSO 31/40', 10, 5, 3, 12, 10, 120],            // khớp
  ['TPC-' + A2, 'Tôm HLSO 31/40', 20, 0, 0, 20, 10, 200],            // nhầm mã với A3 (A2 +50, A3 −50)
  [A3, 'Tôm HLSO 41/50', 5, 0, 0, 5, 10, 50],
  [A4, 'Tôm HLSO 51/60', 8, 2, 1, 9, 10, 95],                         // sai trọng lượng NET (95 ≠ 90)
  [A5, 'Tôm PD 61/70', 30, 0, 0, 30, 10, 300],                        // chia mã: A5 +30 ; A6 −10, A7 −20
  [A8, 'Tôm CO 71/80', 7, 0, 0, 7, 10, 70],                           // chỉ có ở kho
  [A11, 'Tôm NO', 4, 1, 0, 6, 10, 60],                                // tự không khớp (4+1−0 ≠ 6)
  [A12, 'Tôm NO 2', -1, 0, 0, -1, 10, -10],                           // tồn âm
  [M('BOTX', '0001'), 'Bột chiên xù', 1, 0, 0, 1, 25, 25],             // hàng Bột (ẩn)
  [A13, 'Tôm ZZ', 12, 0, 0, 12, 10, 120],                             // đơn vị: kế toán ghi 12 (số thùng)
  [A14, 'Tôm YY', 10, 0, 0, 10, 10, 100],                             // lệch có chứng từ bằng chênh lệch
  ['Tổng cộng', '', '', '', '', '', '', 999],
];
const nxt = [['BÁO CÁO NHẬP XUẤT TỒN THÀNH PHẨM'], ['Từ ngày 01/10/2026 đến ngày 31/10/2026'],
  ['Mã thành phẩm', 'Tên thành phẩm', 'ĐVT', 'Tồn đầu kỳ', 'Nhập trong kỳ', 'Xuất trong kỳ', 'Tồn CK'],
  [A1, 'Tôm HLSO 31/40', 'kg', 100, 50, 30, 120],
  [A2, 'Tôm HLSO 31/40', 'kg', 150, 0, 0, 150],
  [A3, 'Tôm HLSO 41/50', 'kg', 100, 0, 0, 100],
  [A4, 'Tôm HLSO 51/60', 'kg', 80, 20, 10, 90],
  [A5, 'Tôm PD 61/70', 'kg', 270, 0, 0, 270],
  [A6, 'Tôm PD 61/70', 'kg', 10, 0, 0, 10],
  [A7, 'Tôm PD 61/70', 'kg', 20, 0, 0, 20],
  [A9, 'Tôm CO 81/90', 'kg', 40, 0, 0, 40],                            // chỉ có ở kế toán
  [A10, 'Tôm CO', 'kg', 0, 0, 0, ''],                                  // mã có trong báo cáo, không phát sinh
  [A11, 'Tôm NO', 'kg', 40, 10, 0, 50],
  [A12, 'Tôm NO 2', 'kg', 0, 0, 0, 0],
  [A13, 'Tôm ZZ', 'kg', 12, 0, 0, 12],
  [A14, 'Tôm YY', 'kg', 100, 0, 37.5, 62.5],
  [A6.slice(0, 20) + 'X', 'Mã gửi', 'kg', 0, 0, 0, 0],
];
const ct = [['Số CT', 'Ngày', 'Kho', 'Mã', 'Tên', 'LSX', 'NX', 'X', 'Kiện', 'SLKG', 'Net'],
  ['XK01', 46300, 'K1', A14, 'Tôm YY', 'LSX1', 'XK', 'X', 3, 37.5, 12.5],
  ['NK01', 46297, 'K1', A1, 'Tôm', '', 'NH', 'N', 5, 50, 10],
  ['XK02', 46301, 'K1', A1, 'Tôm', '', 'XK', 'X', 3, 30, 10],
  ['NK02', 46298, 'K1', A4, 'Tôm', '', 'NH', 'N', 2, 20, 10],
  ['XK03', 46302, 'K1', A4, 'Tôm', '', 'XK', 'X', 1, 10, 10],
  ['NK03', 46299, 'K1', A11, 'Tôm', '', 'NH', 'N', 1, 10, 10],
  ['NK03', 46299, 'K1', A11, 'Tôm', '', 'NH', 'N', 1, 10, 10],          // trùng
  ['XK09', 46340, 'K1', A13, 'Tôm', '', 'XK', 'X', 1, 5, 10],           // sau cuối kỳ + net lệch
  ['XK10', '31/09/2026', 'K1', A13, 'Tôm', '', 'XK', '', 1, 1, 1],      // ngày lỗi, chiều suy từ NX trống → lỗi
];
const dm = [['DANH MỤC THÀNH PHẨM']].concat([A1, A2, A3, A4, A5, A6, A7, A9, A10, A11, A12, A13, A14].concat(Array.from({ length: 10 }, (_, i) => M('VRQQ', String(1000 + i)))).map((m) => [m, 'Tên ' + m]));
const gui = [['BÁO CÁO TỒN KHO GỬI'], ['Từ ngày 31/10/2026 đến ngày 31/10/2026'],
  ['Mã hàng', 'Tên hàng', 'Số lô', 'Size', 'Quy cách', 'Ngày nhập kho', 'Phiếu nhập', 'Tồn đầu kỳ', '', 'Nhập trong kỳ', '', 'Xuất trong kỳ', '', 'Tồn cuối kỳ', ''],
  ['', '', '', '', '', '', '', 'Số kiện', 'Trọng lượng (KG)', 'Số kiện', 'Trọng lượng (KG)', 'Số kiện', 'Trọng lượng (KG)', 'Số kiện', 'Trọng lượng (KG)'],
  [A2.slice(0, 7) + A2.slice(13) + '0046', 'Tôm HLSO gửi', 'L01', '31/40', '', 46280, 'PN1', 0, 0, 0, 0, 0, 0, 0, 0],
  [A9.slice(0, 7) + A9.slice(13) + '0046', 'Tôm CO gửi', 'L02 TGS', '81/90', '', 46290, 'PN2', 3, 30, 1, 10, 0, 0, 4, 40],
  [A1, 'Tôm 21 ký tự', 'L03', '31/40', '', 46295, 'PN3', 0, 0, 0, 0, 0, 0, 0, 0],
  ['ABC123', 'Mã lạ', 'L04', '', '', '', '', 0, 0, 0, 0, 0, 0, 1, 5],
];
const ketoanR = I.readKetoan(nxt, {}); const ctR = I.readCT(ct, {}); const dmR = I.readDanhMuc(dm);
const ananR = I.readAnAn(anan, {}); const guiR = I.readGui(gui, {});
const period = { from: '2026-10-01', to: '2026-10-31' };
const sources = { anan: ananR, ketoan: Object.assign({}, ketoanR, { extra: { ct: ctR } }), gui: guiR };
const input = { sources, ct: ctR.rows, danhMuc: dmR.codes, period, options: {} };
let res = R.reconcile(input);
// thao tác người dùng: chuyển nhầm mã A2 → A3 50 kg, xác nhận A8, rồi hoàn tác xác nhận
const st = H.newState(); const by = { by: 'admin', at: '2026-11-01T00:00:00.000Z' };
const t1 = H.transfer(st, A2, A3, 50, Object.assign({ reason: 'ghi nhầm mã' }, by));
const t2 = H.confirm(st, A8, { cause: 'chi-co-1-ben', diffAtDecision: 70, adjQty: -70 }, Object.assign({ reason: 'kế toán chưa ghi' }, by, { at: '2026-11-01T00:00:01.000Z' }));
const t3 = H.setNote(st, A5, 'xem lại phiếu', Object.assign({}, by, { at: '2026-11-01T00:00:02.000Z' }));
const u = H.undo(st, t3.entry.id, Object.assign({}, by, { at: '2026-11-01T00:00:03.000Z' }));
const kpi2 = res.overlay(st.decisions, st.adjustments);
const dumpItem = (i) => ({ ma: i.ma, ten: i.ten, aa: i.aa, gui: i.gui, kt: i.kt, kho: i.kho, diff: i.diff, status: i.status, group: i.group, rootKnown: i.rootKnown, statusUi: i.statusUi, diffAfter: i.diffAfter, adj: i.adj, adjKho: i.adjKho, adjKt: i.adjKt,
  tinh: [i.tinh_aa, i.tinh_gui, i.tinh_kt], decomp: i.decomp ? [i.decomp.dau.d, i.decomp.nhap.d, i.decomp.xuat.d, i.decomp.resid] : null,
  causes: i.causes.map((c) => ({ code: c.code, basis: c.basis, conf: c.conf, evidence: c.evidence, action: c.action })),
  cands: i.cands.map((c) => ({ ma: c.ma, score: c.score, conf: c.conf, kind: c.kind, exact: c.exact, reasons: c.reasons })),
  combos: i.combos.map((g) => ({ codes: g.codes, sum: g.sum, conf: g.conf, reasons: g.reasons })),
  ct: { n: i.ct.lines.length, nFlag: i.ct.nFlag, flags: i.ct.lines.map((l) => l.flags.map((f) => f.code).join(',')) }, tags: i.tags });
const out = {
  aoa: { anan, nxt, ct, dm, gui }, period,
  readers: { anan: { rows: ananR.rows, period: ananR.period, stats: ananR.stats }, ketoan: { rows: ketoanR.rows, known: ketoanR.knownCodes, stats: ketoanR.stats }, ct: { rows: ctR.rows, stats: ctR.stats }, dm: dmR.codes.length, gui: { rows: guiR.rows, period: guiR.period, stats: guiR.stats } },
  items: res.items.map(dumpItem), kpi: res.kpi, validation: res.validation, offsets: res.offsets,
  state: { decisions: st.decisions, adjustments: st.adjustments.map((a) => ({ ma: a.ma, qty: a.qty, side: a.side, cancelled: a.cancelled })), history: st.history.map((e) => ({ action: e.action, ma: e.ma, mas: e.mas, summary: e.summary, undone: e.undone })) },
  undoOk: u.ok,
};
require('fs').writeFileSync('ck-golden.json', JSON.stringify(out, null, 1));
console.log(res.items.length, 'items', JSON.stringify(res.kpi));
res.items.forEach((i) => console.log(i.ma, i.diff, i.status, i.causes.map((c) => c.code + '/' + c.conf).join(' '), i.cands.map((c) => c.ma.slice(-8) + c.conf).join(' '), i.statusUi));
console.log(JSON.stringify(res.validation.map((v) => v.text)));
