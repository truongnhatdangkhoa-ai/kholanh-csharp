// Chạy thử TaiKhoan.gs trong Node với Google Sheet giả (node tests/apps-script/kiem-tra.js).
'use strict';
const fs = require('fs'), path = require('path'), vm = require('vm'), assert = require('assert');
const src = fs.readFileSync(path.join(__dirname, '../../src/KhoLanh.Core/Accounts/TaiKhoan.gs'), 'utf8')
  .replace('__MA_DOC__', 'doc1').replace('__MA_QUAN_TRI__', 'qt1');

// Sheet giả: lưu ô như Google Sheet (chuỗi bắt đầu bằng ' → bỏ dấu ', bắt đầu bằng = → công thức hỏng).
function makeSheet() {
  const cells = [];
  const sh = {
    getLastRow: () => cells.length,
    getLastColumn: () => cells.reduce((m, r) => Math.max(m, (r || []).length), 0),
    getRange: (r, c, nr, nc) => ({
      setValues(v) { for (let i = 0; i < nr; i++) { cells[r - 1 + i] = cells[r - 1 + i] || []; for (let j = 0; j < nc; j++) { let x = v[i][j]; if (typeof x === 'string' && x[0] === "'") x = x.slice(1); else if (typeof x === 'string' && x[0] === '=') x = '#ERROR!'; cells[r - 1 + i][c - 1 + j] = x; } } return this; },
      getValues() { const o = []; for (let i = 0; i < nr; i++) { const row = []; for (let j = 0; j < nc; j++) row.push(((cells[r - 1 + i] || [])[c - 1 + j]) ?? ''); o.push(row); } return o; },
      clearContent() { for (let i = 0; i < nr; i++) if (cells[r - 1 + i]) for (let j = 0; j < nc; j++) cells[r - 1 + i][c - 1 + j] = ''; while (cells.length && cells[cells.length - 1].every(x => x === '')) cells.pop(); return this; },
      setFontWeight() { return this; }, setNumberFormat() { return this; },
    }),
    setFrozenRows() {}, cells,
  };
  return sh;
}
let sheet = null;
const ctx = {
  SpreadsheetApp: { getActiveSpreadsheet: () => ({ getSheetByName: () => sheet, insertSheet: () => (sheet = makeSheet()) }) },
  LockService: { getScriptLock: () => ({ waitLock() {}, releaseLock() {} }) },
  ContentService: { MimeType: { JSON: 'json' }, createTextOutput: t => ({ setMimeType() { return JSON.parse(t); } }) },
};
vm.createContext(ctx);
vm.runInContext(src, ctx);
const post = o => ctx.doPost({ postData: { contents: JSON.stringify(o) } });

assert.strictEqual(ctx.doGet().ok, true);
assert.strictEqual(post({ token: 'sai', action: 'list' }).ok, false);
assert.deepStrictEqual(post({ token: 'doc1', action: 'list' }), { ok: true, users: [] });
const u = n => ({ username: n, ho_ten: '=Tên ' + n, vai_tro: 'staff', dang_dung: '1', quyen: '{"m1":"sua"}', mat_khau_bam: 'h:ab', muoi: 'h:cd' });
assert.strictEqual(post({ token: 'doc1', action: 'save', user: u('a') }).ok, false, 'thiếu mã quản trị phải bị từ chối');
assert.strictEqual(post({ token: 'doc1', adminKey: 'qt1', action: 'replaceAll', users: [u('admin'), u('nv1')] }).ok, true);
assert.strictEqual(post({ token: 'doc1', adminKey: 'qt1', action: 'save', user: { ...u('NV1'), ho_ten: 'Mới' } }).ok, true);
assert.strictEqual(post({ token: 'doc1', adminKey: 'qt1', action: 'save', user: u('nv2') }).ok, true);
let l = post({ token: 'doc1', action: 'list' });
assert.deepStrictEqual(l.users.map(x => x.username), ['admin', 'NV1', 'nv2']);
assert.strictEqual(l.users[1].ho_ten, 'Mới');
assert.strictEqual(l.users[0].ho_ten, '=Tên admin', 'chữ bắt đầu bằng = phải giữ nguyên');
assert.strictEqual(l.users[0].quyen, '{"m1":"sua"}');
assert.strictEqual(post({ token: 'doc1', adminKey: 'qt1', action: 'replaceAll', users: [u('x')] }).ok, true);
l = post({ token: 'doc1', action: 'list' });
assert.deepStrictEqual(l.users.map(x => x.username), ['x']);
assert.strictEqual(post({ token: 'doc1', adminKey: 'qt1', action: 'replaceAll', users: [] }).ok, false);
// 1.70: cột email_google — lưu / đọc được, Sheet tạo bằng bản cũ (9 cột) tự thêm tiêu đề cột mới, dữ liệu cũ giữ nguyên.
assert.strictEqual(post({ token: 'doc1', adminKey: 'qt1', action: 'save', user: { ...u('NV9'), email_google: 'nv9@gmail.com' } }).ok, true);
assert.strictEqual(post({ token: 'doc1', action: 'list' }).users.find(x => x.username === 'NV9').email_google, 'nv9@gmail.com');
for (const r of sheet.cells) r.length = 9;
const l2 = post({ token: 'doc1', action: 'list' });
assert.strictEqual(sheet.cells[0][9], 'email_google');
assert.strictEqual(l2.users.find(x => x.username === 'NV9').email_google, '');
assert.strictEqual(l2.users.find(x => x.username === 'NV9').muoi, 'h:cd');
console.log('TaiKhoan.gs: tất cả kiểm tra đạt');
