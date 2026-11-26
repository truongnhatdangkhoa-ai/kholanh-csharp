using KhoLanh.Core.LichXuat;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>1.122 — email «Thông tin xe» / đổi lịch như app cũ (ảnh người dùng: phiếu LSX 26.09.020 tự tạo chỉ có Số cont, thiếu tài xế / SĐT / số xe / seal / booking).</summary>
public class ThongTinXeEmailTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-xe-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;
    private static readonly DateTime Now = new(2026, 11, 26, 9, 0, 0);
    private LichXuatService Lx => _s.LichXuat;
    private static readonly MailConfig Cfg = new() { Auto = true, Days = 7 };

    // đúng nội dung thư trong ảnh người dùng gửi
    private const string XeMail = "Dear All!\n\nThông tin xe:\nHọ tên tài xế: Lê Minh Đức\nSố điện thoại: 0939177714\nSố xe: 51C-63773\nSố cont: IAAU8692960\nSố seal: IAAJ420482\nSố booking: A59GX37929\nNhiệt độ cài đặt cont: -18℃\n\nThanks!\n\nVào Thứ 5, 26 thg 11, 2026 vào lúc 08:15 tien ha <a@b.vn> đã viết:\n> cũ";

    public ThongTinXeEmailTests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        _s.Auth.Login("admin", "matkhau1");
        Lx.Now = () => Now;
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private static LichXuatEmail.Msg M(string id, string th, DateTime d, string from, string subj, string text) => new(id, th, d, from, subj, text);
    private static List<LichXuatEmail.Msg> Chuoi(params LichXuatEmail.Msg[] more)
    {
        var l = new List<LichXuatEmail.Msg> { M("g", "t", Now.AddDays(-3), "Sale <sale@an.vn>", "AN AN - LSX 26.09.020 - TOYO REIZO - KEO DONG - ETD 26/11", "x") };
        l.AddRange(more); return l;
    }

    [Fact]
    public void Thu_xe_den_truoc_khi_co_phieu_thi_phieu_tao_sau_du_thong_tin_xe()
    {
        var lich = Lx.Add(new LichXuatService.Input { Ngay = "2026-11-26", Lsx = "26.09.020" });
        var r = Lx.ProcessMessages(Chuoi(M("x", "t", Now.AddMinutes(-40), "tien ha <ha@x.vn>", "Re: AN AN - LSX 26.09.020 - TOYO REIZO - KEO DONG - ETD 26/11", XeMail)), Cfg);
        var row = Lx.EmailRows().Single(e => e.Id == "x");
        Assert.Equal("Đã duyệt", row.TrangThai);
        Assert.Contains("chưa có phiếu", row.KetQua);
        Lx.Load(); Assert.Equal("IAAU8692960", Lx.Rows.Single().ContSo);

        var (p, created, notes) = Lx.CreatePhieu(new[] { lich.Id });
        Assert.True(created);
        _s.PhieuKho.Load(); var x = _s.PhieuKho.List.Single(z => z.Id == p.Id).Xe;
        Assert.Equal("Lê Minh Đức", x.TaiXe); Assert.Equal("0939177714", x.Sdt); Assert.Equal("51C-63773", x.SoXe);
        Assert.Equal("IAAU8692960", x.Cont); Assert.Equal("IAAJ420482", x.Seal); Assert.Equal("A59GX37929", x.Booking); Assert.Equal("-18℃", x.NhietDo);
        Assert.Contains(notes, n => n.Contains("thông tin xe"));
    }

    [Fact]
    public void Phieu_tao_tay_sau_khi_thu_den_luot_quet_sau_tu_dien_va_thu_moi_thang()
    {
        Lx.Add(new LichXuatService.Input { Ngay = "2026-11-26", Lsx = "26.09.020" });
        var msgs = Chuoi(M("x", "t", Now.AddMinutes(-40), "tien ha <ha@x.vn>", "Re: LSX 26.09.020 - KEO DONG", XeMail));
        Lx.ProcessMessages(msgs, Cfg);
        // phiếu tạo tay sau khi thư đến (chưa có thông tin xe)
        var pnx = _s.PhieuKho; pnx.Load();
        var p = pnx.NewPhieu(); p.Loai = "xuat_khau"; p.Lsx = "26.09.020"; p.Ngay = "2026-11-26";
        p = pnx.Save(p);
        var r = Lx.ProcessMessages(msgs, Cfg);   // lượt quét sau: không có thư mới
        Assert.Equal(0, r.New);
        pnx.Load(); Assert.Equal("51C-63773", pnx.List.Single(z => z.Id == p.Id).Xe.SoXe);
        // đổi xe: thư mới → ghi đè (thư sau cùng thắng, như app cũ)
        msgs.Add(M("x2", "t", Now, "tien ha <ha@x.vn>", "Re: LSX 26.09.020 - KEO DONG", XeMail.Replace("51C-63773", "51D-11111").Replace("Lê Minh Đức", "Trần Văn B")));
        r = Lx.ProcessMessages(msgs, Cfg);
        Assert.Equal(1, r.XeFilled);
        pnx.Load(); var x = pnx.List.Single(z => z.Id == p.Id).Xe;
        Assert.Equal("51D-11111", x.SoXe); Assert.Equal("Trần Văn B", x.TaiXe);
    }

    [Fact]
    public void Thu_doi_lich_cho_duyet_tu_ap_khi_lich_co_sau()
    {
        Lx.SaveTrust("sale@an.vn", true);
        var msgs = new List<LichXuatEmail.Msg>
        {
            M("g", "t", Now.AddDays(-10), "Khác <k@x.vn>", "LSX 26.09.021 - KEO DONG - ETD 28/11", "x"),   // thư gốc cũ, người khác gửi
            M("d", "t", Now.AddHours(-2), "Sale <sale@an.vn>", "Re: LSX 26.09.021 - KEO DONG - ETD 28/11", "Đổi ngày kéo đóng sang ngày 30/11"),
        };
        var r = Lx.ProcessMessages(msgs, Cfg);
        Assert.Equal(0, r.Auto);   // LSX chưa có lịch → chờ
        Lx.Add(new LichXuatService.Input { Ngay = "2026-11-28", Lsx = "26.09.021" });
        r = Lx.ProcessMessages(msgs, Cfg);   // lượt sau: thư chờ của người tin cậy được thử lại (app cũ emAuto)
        Assert.Equal(1, r.Auto);
        Lx.Load(); Assert.Equal("2026-11-30", Lx.Rows.Single().Ngay);
        Assert.Equal("Đã duyệt", Lx.EmailRows().Single(e => e.Id == "d").TrangThai);
    }
}
