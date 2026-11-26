using System.Text.Json.Nodes;
using ClosedXML.Excel;
using KhoLanh.Core;
using KhoLanh.Core.ChamCong;
using KhoLanh.Core.Services;
using KhoLanh.Core.TonKho;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>Chấm công (1.129) — đối chiếu với bảng chấm công kho tháng 10/2026 người dùng làm tay (Data/chamcong-golden.json, đã bỏ tên).</summary>
public class ChamCongTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "kl-cc-" + Guid.NewGuid().ToString("N"));
    private readonly List<AppServices> _live = new();
    public void Dispose()
    {
        foreach (var s in _live) s.Lan.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tmp, true); } catch { }
    }

    [Fact]
    public void Tinh_gio_khop_bang_nguoi_dung_lam_tay()
    {
        var j = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "chamcong-golden.json")))!["rows"]!.AsArray();
        var c = new CcCauHinh();
        var lech = new List<string>();
        foreach (var r in j.OfType<JsonObject>())
        {
            var k = CcCore.Tinh(CcCore.ParseTime(r["vao"]!.ToString()), CcCore.ParseTime(r["ra"]!.ToString()), r["nhom"]!.ToString(), r["vsom"]!.GetValue<double>(), c);
            if (Math.Abs(k.Gio - r["chot"]!.GetValue<double>()) > 1e-9) lech.Add($"{r["nv"]} {r["ngay"]} {r["vao"]}-{r["ra"]}: tính {k.Gio} / người dùng {r["chot"]}");
        }
        Assert.Equal(682, j.Count);
        // 2 dòng người dùng chỉnh tay riêng (giờ chốt thấp hơn quy tắc) — app để sửa tay ở cột Chốt
        Assert.Equal(new[] { "NV15 2026-10-05 06:57-18:40: tính 10 / người dùng 8.5", "NV21 2026-10-06 06:36-13:10: tính 6 / người dùng 4.5" }, lech);
    }

    [Fact]
    public void Lam_tron_va_nghi()
    {
        var c = new CcCauHinh();
        Assert.Equal(7 * 60, CcCore.RoundIn(6 * 60 + 57, c));
        Assert.Equal(7 * 60, CcCore.RoundIn(7 * 60 + 4, c));
        Assert.Equal(7 * 60 + 30, CcCore.RoundIn(7 * 60 + 5, c));
        Assert.Equal(8 * 60, CcCore.RoundIn(7 * 60 + 35, c));
        Assert.Equal(16 * 60 + 30, CcCore.RoundOut(16 * 60 + 31, c));
        Assert.Equal(16 * 60, CcCore.RoundOut(16 * 60 + 25, c));
        Assert.Equal(17 * 60, CcCore.RoundOut(16 * 60 + 56, c));
        Assert.Equal(8, CcCore.Tinh(CcCore.ParseTime("06:57"), CcCore.ParseTime("16:31"), "XUẤT", 0, c).Gio);       // 7:00–16:30 − 1:30
        Assert.Equal(9, CcCore.Tinh(CcCore.ParseTime("06:57"), CcCore.ParseTime("16:31"), "XUẤT", 1, c).Gio);       // V.Sớm 1 → nghỉ 0:30
        Assert.Equal(8.5, CcCore.Tinh(CcCore.ParseTime("06:57"), CcCore.ParseTime("16:31"), "Nhập", 0, c).Gio);     // nghỉ 1:00
        Assert.Equal(4, CcCore.Tinh(CcCore.ParseTime("06:57"), CcCore.ParseTime("11:01"), "XUẤT", 0, c).Gio);       // nửa ngày: không trừ trưa
        Assert.Equal(14, CcCore.Tinh(CcCore.ParseTime("06:59"), CcCore.ParseTime("22:31"), "XUẤT", 1, c).Gio);      // ≥ 15 tiếng: trừ thêm 1 giờ tối
        var thieu = CcCore.Tinh(CcCore.ParseTime("06:58"), null, "XUẤT", 0, c);
        Assert.Equal(0, thieu.Gio); Assert.StartsWith("Chỉ có 1 lần chấm (06:58)", thieu.CanhBao);
        Assert.Contains("quá sớm", CcCore.Tinh(CcCore.ParseTime("00:51"), CcCore.ParseTime("16:31"), "XUẤT", 0, c).CanhBao);
        Assert.Equal(0, CcCore.Tinh(CcCore.ParseTime("06:58"), CcCore.ParseTime("16:30"), "KHÁC", 0, c).Nghi);       // bộ phận chưa khai báo: không trừ nghỉ
        Assert.Equal(new DateOnly(2026, 10, 1), CcCore.ParseDate("01/10/2026"));
        Assert.Equal(new DateOnly(2026, 10, 1), CcCore.ParseDate("2026-10-01 00:00:00"));
        Assert.Equal(6 * 60 + 57, CcCore.ParseTime("6:57"));
        Assert.Null(CcCore.ParseTime("-"));
        Assert.Equal("T.5", CcCore.Thu(new DateOnly(2026, 10, 1)));
        Assert.Equal("CN", CcCore.Thu(new DateOnly(2026, 10, 4)));
    }

    private string MayFile(bool coVSom = false)
    {
        var p = Path.Combine(_tmp, "cham-cong-goc.xlsx");
        Directory.CreateDirectory(_tmp);
        using var wb = new XLWorkbook(); var ws = wb.AddWorksheet("Sheet1");
        ws.Cell(1, 1).Value = "CHI TIẾT CHẤM CÔNG"; ws.Cell(2, 1).Value = "Từ ngày 01/10/2026 đến ngày 31/10/2026";
        var h = new List<string> { "Mã nhân viên", "Tên nhân viên", "Phòng ban", "Chức vụ", "Ngày", "Thứ", "Giờ vào", "Giờ ra", "Công", "Giờ" };
        if (coVSom) h.Add("V.Sớm");
        for (var c = 0; c < h.Count; c++) ws.Cell(3, c + 1).Value = h[c];
        var r = 4;
        void Add(string ma, string ten, string pb, string cv, int ngay, string vao, string ra, double vs = 0)
        {
            ws.Cell(r, 1).Value = ma; ws.Cell(r, 2).Value = ten; ws.Cell(r, 3).Value = pb; ws.Cell(r, 4).Value = cv;
            ws.Cell(r, 5).Value = new DateTime(2026, 10, ngay); ws.Cell(r, 5).Style.DateFormat.Format = "dd/MM/yyyy";
            ws.Cell(r, 6).Value = "T.x"; ws.Cell(r, 7).Value = vao; ws.Cell(r, 8).Value = ra; ws.Cell(r, 9).Value = 1; ws.Cell(r, 10).Value = 8;
            if (coVSom && vs != 0) ws.Cell(r, 11).Value = vs;
            r++;
        }
        Add("100", "Người Kho A", "Kho", coVSom ? "XUẤT" : "", 1, "06:57", "16:31", 1);
        Add("100", "Người Kho A", "Kho", coVSom ? "XUẤT" : "", 2, "06:57", "");
        Add("101", "Người Kho B", "Kho", coVSom ? "NHẬP" : "", 1, "06:50", "17:00");
        Add("200", "Công nhân C", "Công Nhân", "", 1, "06:00", "14:00");
        wb.SaveAs(p);
        return p;
    }

    private AppServices Svc()
    {
        var s = new AppServices(Path.Combine(_tmp, "db", "kho.db"));
        s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        s.Auth.Login("admin", "matkhau1");
        _live.Add(s);
        return s;
    }

    [Fact]
    public void Nap_file_may_cham_cong_sua_tay_nguoi_khong_ma_va_xuat_excel()
    {
        var s = Svc(); var cc = s.ChamCong;
        cc.Load();
        var f = ChamCongService.ReadFile(MayFile());
        Assert.Equal("2026-10", f.Thang);
        Assert.Equal(3, f.PhongBan["Kho"]); Assert.Equal(1, f.PhongBan["Công Nhân"]);
        var kq = cc.Commit(f, new[] { "kho" });                       // so phòng ban không phân biệt hoa / dấu
        Assert.Equal(3, kq.Dong); Assert.Equal(2, kq.NvMoi);
        Assert.Equal(new[] { "100", "101" }, cc.NhanVien.Select(n => n.Ma));

        // khai bộ phận + thêm người không có mã máy (làm thay, lấy giờ của người khác)
        var list = cc.NhanVien.ToList();
        list[0].Nhom = "Xuất"; list[1].Nhom = "nhap";
        list.Add(new CcNhanVien { Ten = "Người làm thay", Nhom = "XUẤT" });
        cc.SaveNhanVien(list);
        cc.Load();
        Assert.Equal(new[] { "XUẤT", "NHẬP", "XUẤT" }, cc.NhanVien.Select(n => n.Nhom));
        var thay = cc.NhanVien[2]; var a = cc.NhanVien[0];
        Assert.StartsWith("t:", thay.Id);

        var t = cc.Tinh("2026-10");
        Assert.Equal(31, t.Ngays.Count);
        Assert.Equal(8, t.Dong[(a.Id, new DateOnly(2026, 10, 1))].Chot);
        Assert.StartsWith("Chỉ có 1 lần chấm", t.Dong[(a.Id, new DateOnly(2026, 10, 2))].CanhBao);
        Assert.Equal(9, t.Dong[(cc.NhanVien[1].Id, new DateOnly(2026, 10, 1))].Chot);  // 7:00–17:00 − 1:00

        cc.SaveSua("2026-10", new[]
        {
            new CcSua { Nv = a.Id, Ngay = new DateOnly(2026, 10, 2), Ra = "17:3", GhiChu = "quên bấm ra" },  // sửa giờ ra
            new CcSua { Nv = a.Id, Ngay = new DateOnly(2026, 10, 1), VSom = 1 },                              // bớt nghỉ 1 giờ
            new CcSua { Nv = thay.Id, Ngay = new DateOnly(2026, 10, 1), ChepTu = a.Id },                       // giờ như người A
            new CcSua { Nv = thay.Id, Ngay = new DateOnly(2026, 10, 3), Vao = "08:00", Ra = "12:00", Chot = 3.5 },
        });
        t = cc.Tinh("2026-10");
        var d2 = t.Dong[(a.Id, new DateOnly(2026, 10, 2))];
        Assert.Equal("17:03", d2.Ra); Assert.True(d2.SuaRa); Assert.Equal(8.5, d2.Chot); Assert.Equal("", d2.CanhBao);
        Assert.Equal(9, t.Dong[(a.Id, new DateOnly(2026, 10, 1))].Chot);
        var c1 = t.Dong[(thay.Id, new DateOnly(2026, 10, 1))];
        Assert.Equal("06:57", c1.Vao); Assert.Equal(8, c1.Chot);   // chép giờ, không chép V.Sớm Assert.Equal("Người Kho A", c1.ChepTu);
        Assert.Equal(3.5, t.Dong[(thay.Id, new DateOnly(2026, 10, 3))].Chot);
        Assert.Equal(17.5, t.TongGio(a)); Assert.Equal(2, t.SoNgay(a));
        Assert.Equal(11.5, t.TongGio(thay));
        Assert.Throws<BusinessException>(() => cc.SaveSua("2026-10", new[] { new CcSua { Nv = a.Id, Ngay = new DateOnly(2026, 10, 4), Vao = "25:00" } }));
        // xóa sửa tay = ghi sửa rỗng
        cc.SaveSua("2026-10", new[] { new CcSua { Nv = thay.Id, Ngay = new DateOnly(2026, 10, 3) } });
        Assert.Equal(0, cc.Tinh("2026-10").Dong[(thay.Id, new DateOnly(2026, 10, 3))].Chot);
        Assert.Equal(new[] { "2026-10" }, cc.Thangs());

        // nạp lại file tháng đó: giữ sửa tay, không thêm trùng nhân viên
        Assert.Equal(0, cc.Commit(ChamCongService.ReadFile(MayFile()), new[] { "Kho" }).NvMoi);
        Assert.Equal(8.5, cc.Tinh("2026-10").Dong[(a.Id, new DateOnly(2026, 10, 2))].Chot);

        var xl = Path.Combine(_tmp, "bang-cong.xlsx");
        ChamCongService.Export(cc.Tinh("2026-10"), xl);
        using var wb = new XLWorkbook(xl);
        var bc = wb.Worksheet("Bảng công");
        Assert.Equal("Người Kho A", bc.Cell(4, 3).GetString());
        Assert.Equal(8, bc.Cell(4, 5).GetDouble());                  // ngày 1: 9 giờ → sang 1 giờ cho ngày nghỉ gần nhất
        Assert.Equal(17.5, bc.Cell(4, 5 + 31 + 2).GetDouble());      // tổng giờ sau sang (không đổi)
        Assert.Equal("Sang công", wb.Worksheet("Sang công").Cell(1, 1).GetString() == "Mã nhân viên" ? "Sang công" : "");
        Assert.Equal("Chốt", wb.Worksheet("Chi tiết").Cell(3, 14).GetString());
    }

    [Fact]
    public void Nap_bang_da_lam_tay_lay_bo_phan_va_v_som()
    {
        var s = Svc(); var cc = s.ChamCong; cc.Load();
        var kq = cc.Commit(ChamCongService.ReadFile(MayFile(coVSom: true)), new[] { "Kho" });
        Assert.Equal(1, kq.VSom);
        Assert.Equal(new[] { "XUẤT", "NHẬP" }, cc.NhanVien.Select(n => n.Nhom));
        var t = cc.Tinh("2026-10");
        Assert.Equal(9, t.Dong[(cc.NhanVien[0].Id, new DateOnly(2026, 10, 1))].Chot);   // V.Sớm 1 của file → nghỉ 0:30
    }

    private static List<(DateOnly, double, bool)> Days(params double[] gio) =>
        gio.Select((g, i) => (new DateOnly(2026, 10, i + 1), g, false)).ToList();

    [Fact]
    public void Sang_cong_gio_du_bu_ngay_thieu()
    {
        // ví dụ người dùng: ngày 5 làm 9 tiếng, ngày 6 làm 7 tiếng → sang 1 tiếng cho ngày 6
        var (sau, lan, con) = CcCore.SangCong(Days(9, 7), 8);
        Assert.Equal(new[] { 8.0, 8.0 }, sau); Assert.Equal(0, con);
        Assert.Equal(new CcCore.Sang(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2), 1), Assert.Single(lan));
        // ngày đi làm thiếu được bù trước ngày nghỉ hẳn (dù ngày nghỉ gần hơn)
        (sau, _, _) = CcCore.SangCong(Days(10, 0, 6), 8);
        Assert.Equal(new[] { 8.0, 0, 8 }, sau);
        // ngày nghỉ hẳn cũng được bù khi còn giờ dư
        (sau, _, con) = CcCore.SangCong(Days(12, 0, 12), 8);
        Assert.Equal(new[] { 8.0, 8, 8 }, sau); Assert.Equal(0, con);
        // lấy từ ngày dư gần nhất
        (sau, lan, _) = CcCore.SangCong(Days(10, 7, 8, 8, 7, 12), 8);
        Assert.Equal(new[] { 9.0, 8, 8, 8, 8, 11 }, sau);
        Assert.Equal(new DateOnly(2026, 10, 6), lan.Single(l => l.Den.Day == 5).Tu);
        // giờ dư còn lại = tăng ca chưa dùng; Chủ nhật cũng cho giờ dư
        (sau, _, con) = CcCore.SangCong(Days(12, 8), 8);
        Assert.Equal(new[] { 12.0, 8 }, sau); Assert.Equal(4, con);
        // giờ dư tháng trước
        (sau, lan, con) = CcCore.SangCong(Days(6), 8, duTruoc: 3);
        Assert.Equal(new[] { 8.0 }, sau); Assert.Equal(1, con); Assert.Null(Assert.Single(lan).Tu);
        // ngày sửa tay "Sau sang" không cho / không nhận
        var d = Days(10, 6); d[1] = (d[1].Item1, 6, true);
        (sau, lan, con) = CcCore.SangCong(d, 8);
        Assert.Equal(new[] { 10.0, 6 }, sau); Assert.Empty(lan); Assert.Equal(2, con);
    }

    [Fact]
    public void Sang_cong_trong_thang_va_bu_gio_du_thang_truoc()
    {
        var s = Svc(); var cc = s.ChamCong; cc.Load();
        cc.SaveNhanVien(new() { new CcNhanVien { Ten = "Người A", Nhom = "XUẤT" } });
        cc.Load(); var a = cc.NhanVien[0];
        DateOnly D(int m, int d) => new(2026, m, d);
        // tháng 10: ngày 5 làm 9 giờ, ngày 6 làm 7 giờ, ngày 7 làm 12 giờ (chốt sửa tay để khỏi cần giờ máy)
        cc.SaveSua("2026-10", new[] { (5, 9.0), (6, 7.0), (7, 12.0) }.Select(x => new CcSua { Nv = a.Id, Ngay = D(10, x.Item1), Chot = x.Item2 }));
        var t = cc.Tinh("2026-10");
        Assert.Equal(8, t.Dong[(a.Id, D(10, 5))].SauSang);
        Assert.Equal(8, t.Dong[(a.Id, D(10, 6))].SauSang);
        Assert.Equal(8, t.Dong[(a.Id, D(10, 7))].SauSang);           // 4 giờ dư bù ngày nghỉ gần nhất (ngày 8)
        Assert.Equal(4, t.Dong[(a.Id, D(10, 8))].SauSang);
        Assert.Equal(28, t.TongSau(a)); Assert.Equal(3, t.NgayDu(a));
        Assert.Equal("nhận 1 giờ từ 05/10", ChamCongService.SangText(t.Dong[(a.Id, D(10, 6))]));
        // sửa tay giờ sau sang: ngày 8 giữ 0 → 4 giờ dư sang ngày nghỉ gần nhất còn lại (ngày 9)
        cc.SaveSua("2026-10", new[] { new CcSua { Nv = a.Id, Ngay = D(10, 8), SauSang = 0 } });
        t = cc.Tinh("2026-10");
        Assert.Equal(0, t.Dong[(a.Id, D(10, 8))].SauSang); Assert.Equal(4, t.Dong[(a.Id, D(10, 9))].SauSang);

        // ngày 9 nghỉ CÓ PHÉP → không bù; 4 giờ dư sang ngày nghỉ không phép gần nhất (ngày 4 và 10 cùng cách 3 ngày → ngày sớm hơn)
        cc.SaveSua("2026-10", new[] { new CcSua { Nv = a.Id, Ngay = D(10, 9), Phep = true } });
        t = cc.Tinh("2026-10");
        Assert.True(t.Dong[(a.Id, D(10, 9))].Phep); Assert.Equal(0, t.Dong[(a.Id, D(10, 9))].SauSang);
        Assert.Equal(4, t.Dong[(a.Id, D(10, 4))].SauSang); Assert.Equal(1, t.NgayPhep(a));
        // tháng 10 cho làm đủ mọi ngày + ngày 20 làm 30 giờ (chốt sửa tay) → sau khi bù ngày 8 / 9… vẫn còn giờ dư
        cc.SaveSua("2026-10", Enumerable.Range(1, 31).Where(d => d is not (5 or 6 or 7 or 8 or 9)).Select(d => new CcSua { Nv = a.Id, Ngay = D(10, d), Chot = d == 20 ? 30 : 8 }));
        var t10 = cc.Tinh("2026-10");
        Assert.Equal(1 + 4 + 22 - 1, t10.DuCon[a.Id]);                            // dư ngày 5 / 7 / 20, đã bù 1 giờ cho ngày 6 (ngày 8 khóa tay 0 giờ, ngày 9 nghỉ phép)
        // tháng 11: ngày 1 làm 4 giờ — chưa bật "bù giờ dư tháng trước" thì không được bù
        cc.SaveSua("2026-11", new[] { new CcSua { Nv = a.Id, Ngay = D(11, 1), Chot = 4 } });
        Assert.Equal(4, cc.Tinh("2026-11").Dong[(a.Id, D(11, 1))].SauSang);
        Assert.Equal(t10.DuCon.Values.Sum(), cc.DuThangTruoc("2026-11"));
        cc.SetBuThangTruoc("2026-11", true);
        var t11 = cc.Tinh("2026-11");
        Assert.Equal(8, t11.Dong[(a.Id, D(11, 1))].SauSang);
        Assert.Equal("nhận 4 giờ từ tháng trước", ChamCongService.SangText(t11.Dong[(a.Id, D(11, 1))]));
        Assert.Equal(t10.DuCon[a.Id], t11.DuTruoc[a.Id]);
    }

    [Fact]
    public void Nhan_vien_trung_ma_bi_chan_va_quyen_xem_khong_sua()
    {
        var s = Svc(); var cc = s.ChamCong; cc.Load();
        Assert.Throws<BusinessException>(() => cc.SaveNhanVien(new() { new CcNhanVien { Ma = "1", Ten = "A" }, new CcNhanVien { Ma = "1", Ten = "B" } }));
        s.Auth.CreateUser("nhanvien", "Nhân viên", "matkhau2", KhoLanh.Core.Models.Roles.Staff);
        s.Auth.Logout(); s.Auth.Login("nhanvien", "matkhau2");
        Assert.False(cc.CanEdit);
        Assert.Throws<BusinessException>(() => cc.SaveCauHinh(new CcCauHinh()));
    }
}
