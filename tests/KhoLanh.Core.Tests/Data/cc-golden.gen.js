// Sinh đáp án mẫu cho lõi bảng (C#) bằng chính cc-bang-engine.js của app cũ.
const E = require('/home/user/kholanh-anan-pc/app/html/js/cc-bang-engine.js');
const A = { head: ['Mã', 'Tên', 'SL', 'Ngày', 'Kho'], rows: [
  ['  A01 ', 'Tôm  sú ', '1,234', '5/10/26', 'k1'], ['A02', 'Cá basa', '12,5', '2026-10-06', 'K1'], [null, '', '  ', null, null],
  ['a01', 'Tôm sú', 1234, '05/10/2026', 'k1'], ['A03', 'mực ống', 'abc', '31/02/2026', 'K2'], ['A04', 'Ghẹ', '(100)', 46300, 'k2'], ['A05', '', '7', '', 'K3'] ] };
const B = { head: ['MA', 'Tên', 'SL', 'Giá'], rows: [
  ['A01', 'Tom su', 1234, 10], ['A02', 'Cá basa', 12.6, 20], ['A02', 'Cá basa 2', 3, 30], ['A09', 'Lạ', 1, 1], ['', 'Không mã', 2, 2] ] };
const clean = E.cleanTable(A, { dedupe: { cols: [0] }, cols: { 1: { title: true }, 2: { num: true }, 3: { date: true }, 4: { upper: true } } });
const clean2 = E.cleanTable(A, { trim: false, blankRows: false });
const merge = E.mergeStack([{ name: 'f1.xlsx', tbl: A }, { name: 'f2.xlsx', tbl: B }]);
const split = E.splitByColumn(A, 4).map((x) => ({ key: x.key, name: x.name, n: x.tbl.rows.length }));
const splitR = E.splitByRows(A, 3).map((x) => ({ name: x.name, n: x.tbl.rows.length }));
const o = { keyA: [0], keyB: [0], pairs: [{ a: 1, b: 1, name: 'Tên' }, { a: 2, b: 2, name: 'SL' }], ignoreDiacritics: true, tol: 0.05 };
const cmp = E.compareTables(A, B, o);
const lk = ['first', 'last', 'sum', 'join', 'count'].map((d) => E.lookupTables(A, B, { keyA: [0], keyB: [0], pull: [1, 3], dup: d }));
const pages = ['1-3,5,8-', '', '2-1', '0', 'x', '7-'].map((t) => E.parsePages(t, 9));
require('fs').writeFileSync('cc-golden.json', JSON.stringify({ A, B, clean, clean2: { rows: clean2.rows, log: clean2.log.length }, merge, split, splitR, cmp, lk, pages }, null, 1));
console.log(JSON.stringify(clean.rows), clean.removed, JSON.stringify(cmp.diff), lk[2].rows.map((r) => r.slice(5).join('|')));
