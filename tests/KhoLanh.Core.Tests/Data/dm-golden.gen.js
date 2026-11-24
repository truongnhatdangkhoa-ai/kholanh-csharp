// Sinh đáp án mẫu cho Danh mục mã hàng (C#) bằng CHÍNH danh-muc-ma-hang.js của app cũ (cắt phần lõi: decode / noteOf / checks / statusOf / loadAll / xlData).
// Chạy: cd tests/KhoLanh.Core.Tests/Data && node dm-golden.gen.js
const fs = require('fs');
const src = fs.readFileSync('/home/user/kholanh-anan-pc/app/html/js/danh-muc-ma-hang.js', 'utf8');
const core = src.slice(src.indexOf('  var DB = '), src.indexOf('  // ---------- lưới'));
const xl = src.slice(src.indexOf('  function xlData()'), src.indexOf('  function xlBadge()'));
const make = new Function('window', 'document', core + xl + '\nreturn { S, decode, noteOf, checks, statusOf, loadAll, useText, xlData };');
// mã 21 ký tự = 4 + 1 + 1 + 1 + 1 + 2 + 3 + 4 + 2 + 2
const C = (a, b, c, d, e, f, g, h, i, j) => a + b + c + d + e + f + g + h + i + j;
const ok1 = C('VRHL', 'B', 'T', 'N', 'X', 'EC', '001', '1620', '10', '01');
const ok2 = C('VRHL', 'B', 'T', 'N', 'X', 'EC', '001', '2130', '10', '01');      // khác size (hợp lệ) → không báo gần giống
const typo = C('VRHL', 'B', 'T', 'N', 'X', 'EC', '001', '1629', '10', '01');    // size 1629 chưa khai báo → nghi gõ sai so với ok1
const none = C('PRHL', 'X', 'X', 'X', 'X', 'XX', 'XXX', 'XXXX', '05', '02');   // đoạn 2–8 toàn X = hợp lệ
const unk = C('ZZZZ', 'B', 'T', 'N', 'X', 'EC', '001', '1620', '99', '01');     // đoạn 1 + 9 chưa khai báo
const data = [ok1, ok2, typo, ok1, none, unk, 'ABC123', '', ' vrhl btnx ec00121301001 ', 'vrhlbtnxec001162010o1'].map((c, i) => ({ STT: i + 1, 'Mã': c, 'Ghi chú': i === 1 ? 'Tôm sú PTO' : '' }));
const maHoa = [
  [2, 'B', 'Block'], [3, 'T', 'Túi'], [4, 'N', 'Không hóa chất'], [5, 'X', 'Xuất khẩu'], [6, 'EC', 'Ecuador'], [7, '001', 'Mạ 10%'],
  [8, '1620', '16/20'], [8, '2130', '21/30'], [9, '10', '1 kg'], [9, '05', '0.5 kg'], [10, '01', '10 túi / thùng'], [10, '02', ''], [1, ' PRHL ', 'Tôm thẻ HLSO'], [11, 'Q', 'bỏ'],
].map(([s, m, t]) => ({ 'Tổ hợp': s, 'Mã': m, 'Tên': t }));
const fixed = [{ code: 'VRHL', name: 'Tôm sú HLSO' }, { code: 'vrhl', name: 'Tôm sú HLSO (thường)' }];
const use = {
  'ton-kho-an-an/LoadData': [{ c0: ok1, c1: 'x' }, { c0: 'TPC-' + ok1, c3: ok1 }, { c0: ' ' + C('NEWW', 'B', 'T', 'N', 'X', 'EC', '001', '1620', '10', '01').toLowerCase() + ' ' }, { c0: 12345 }],
  'ton-vi-tri-an-an/ViTri': [{ ma: ok2 }, { ma: 'tpc-' + typo }],
  'ton-kho-an-an/ViTriBot': [{ m: none }],
  'ton-kho-gui/DuLieuChinh': [{ a: ok1 }, { b: C('NEWW', 'B', 'T', 'N', 'X', 'EC', '001', '2130', '10', '01') }],
  'nxt-bot-sot/SoCai': [{ maHang: ok1, ghiChu: ok1 }, { maHang: unk }],
};
const rd = (db, sh) => Promise.resolve(db === 'tong-hop' ? (sh === 'MaDaTao' ? data : maHoa) : (use[db + '/' + sh] || []));
const M = make({ KLSEG1_FIXED: fixed, KLUtil: { escLite: (v) => String(v) }, dbAPI: { readSheet: (db, sh) => rd(db, sh).then((d) => ({ ok: true, data: d })) } });
M.loadAll().then(() => {
  const codes = M.S.all.map((r) => r.code);
  const out = {
    data, maHoa, fixed, useSrc: use,
    codes,
    decode: codes.map((c) => M.decode(c).map((x) => [x.seg, x.code, x.name === undefined ? null : x.name, x.ok])),
    note: codes.map(M.noteOf),
    status: codes.map((c) => { const s = M.statusOf(c); return s ? [s.level, s.msgs] : null; }),
    useText: codes.concat(['NEWWBTNXEC001162010O1']).map(M.useText),
    useCount: Array.from(M.S.use.entries()).map(([c, u]) => [c, u.M01, u.M02, u.SK]),
    xl: (() => { const d = M.xlData(); return { missing: d.missing, dups: d.dups, near: d.near, miss: d.miss.map((r) => r.code) }; })(),
  };
  fs.writeFileSync('dm-golden.json', JSON.stringify(out, null, 1));
  console.log(JSON.stringify(out.status), JSON.stringify(out.xl));
});
