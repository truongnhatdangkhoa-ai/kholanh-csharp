// 1.106 — Đối chiếu kiểm mật khẩu của Code.gs (lệnh pcDangNhap, PBKDF2 viết bằng JavaScript thuần) với app PC.
// Chạy: node dangnhap-maymoi-golden.gen.js <đường dẫn Code.gs>  → ghi dangnhap-maymoi-golden.json (mật khẩu, muối, mã băm dạng "h:hex"
// như app PC ghi tab TaiKhoan, kết quả dnKiemMk_ của Code.gs cho mật khẩu đúng / sai). Test C# kiểm cùng bộ số bằng AuthService.Verify.
const fs = require('fs'), crypto = require('crypto'), path = require('path');
eval(fs.readFileSync(process.argv[2], 'utf8'));
const cases = [];
for (const [pw, salt] of [['matkhau2', '00112233445566778899aabbccddeeff'], ['Mật khẩu có dấu ✓', 'ffeeddccbbaa99887766554433221100'], ['Kho@2026!', '0f1e2d3c4b5a69788796a5b4c3d2e1f0']]) {
  const bam = crypto.pbkdf2Sync(Buffer.from(pw, 'utf8'), Buffer.from(salt, 'hex'), 100000, 32, 'sha256').toString('hex');
  const muoi = 'h:' + salt, mk = 'h:' + bam;
  cases.push({ matKhau: pw, muoi, bam: mk, dung: dnKiemMk_(pw, muoi, mk), sai: dnKiemMk_(pw + 'x', muoi, mk),
    base64: dnKiemMk_(pw, Buffer.from(salt, 'hex').toString('base64'), Buffer.from(bam, 'hex').toString('base64')) });
}
fs.writeFileSync(path.join(__dirname, 'dangnhap-maymoi-golden.json'), JSON.stringify(cases, null, 1));
console.log(cases.map(c => c.dung + '/' + c.sai + '/' + c.base64).join(' '));
