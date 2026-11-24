// Đáp án mẫu: mẫu cột NXT Bột/Sốt (SoKhoMau) — templateFromStore / templateToStore / templateFor của so-kho-core.js. Chạy: node sk-tpl-golden.gen.js
const J = '/home/user/kholanh-anan-pc/app/html/js/';
require(J + 'kl-num.js');
const SK = require(J + 'so-kho-core.js');
const stores = [
  { id: 'm1', ten: 'Mẫu xuất', loai: 'xuat', macDinh: '', columnsJson: JSON.stringify([{ key: 'maHang', label: 'Mã', hidden: true }, { key: 'viTri', label: 'VT', w: 30 }, { key: 'soLuong', label: 'Bao' }, { key: 'net', label: 'Net (kg)' }, { key: 'x_so_xe', label: 'Số xe', type: 'text', w: 150 }, { key: 'x_nhiet', label: '', type: 'number' }, { key: 'bad', label: 'Lạ' }, { key: 'hopDong', removed: true }, { key: 'maHang', removed: true }, { key: 'ghiChu', hidden: true }]) },
  { id: 'm2', ten: 'Mẫu chung', loai: '', macDinh: '1', columnsJson: '[]' },
  { id: '', ten: '', loai: 'abc', macDinh: 1, columnsJson: 'không phải json' },
];
const tpls = stores.map((r) => SK.templateFromStore(r, 'M03'));
const out = {
  stores,
  templates: tpls.map((t) => ({ ten: t.ten, loai: t.loai, macDinh: t.macDinh, removed: t.removed, columns: t.columns.map((c) => ({ key: c.key, label: c.label, type: c.type, hidden: c.hidden, w: c.w, builtin: c.builtin })) })),
  stored: SK.templateToStore(tpls[0]),
  eval: [
    [[{ c: 'soLuong' }, '*', { c: 'netDv' }], { soLuong: 4, netDv: 25 }],
    [[{ c: 'a' }, '+', { c: 'b' }, '*', { n: 2 }], { a: 1, b: 3 }],
    [[{ c: 'a' }, '+', { c: 'b' }, '*', { n: 2 }], { a: 1, b: null }],
    [[{ c: 'a' }, '-', { c: 'b' }], { a: null, b: null }],
    [[{ c: 'a' }, '/', { c: 'b' }], { a: 1, b: 0 }],
    [[{ c: 'a' }, '/', { n: 3 }], { a: 10 }],
    [[{ c: 'a' }, '-', { c: 'b' }, '/', { n: 4 }, '+', { n: 0.5 }], { a: 7, b: 2 }],
  ].map(([f, vals]) => ({ f, vals, out: SK.evalFormula(f, (k) => (k in vals ? vals[k] : null)) })),
  pick: { xuat: SK.templateFor(tpls, 'xuat', 'M03').ten, nhap: SK.templateFor(tpls, 'nhap', 'M03').ten, none: SK.templateFor([], 'nhap', 'M03').ten },
};
require('fs').writeFileSync(__dirname + '/sk-tpl-golden.json', JSON.stringify(out, null, 1));
console.log(JSON.stringify(out.templates[0].columns.map((c) => c.key + ':' + c.label + ':' + c.hidden + ':' + c.w)), JSON.stringify(out.pick), out.templates[2].loai, out.templates[2].ten);
