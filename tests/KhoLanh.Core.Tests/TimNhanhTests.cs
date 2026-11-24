using System.Text.Json.Nodes;
using KhoLanh.Core.SoKho;
using KhoLanh.Core.TimNhanh;
using Microsoft.Data.Sqlite;
using Xunit;
using static KhoLanh.Core.SoKho.SoKhoCore;

namespace KhoLanh.Core.Tests;

public class TimNhanhTests : IDisposable
{
    private static GsRow R(string src, string ma, string vt = "", string tag = "", string hd = "", string dt = "", double ton = 1, string ten = "") =>
        new() { Src = src, Ma = ma, Vt = vt, Tag = tag, Hd = hd, Dt = dt, Ton = ton, Ten = ten };

    private static Gs.Data Sample() => Gs.Prepare(new[]
    {
        R("M01VT", "VRHLBTNXECR001620IMA1", "TG1.A.01.1.1", "BTP 16", "HD-2614", "Semi", 8, "Tôm thẻ bóc vỏ"),
        R("M01VT", "VRHLBTNXECR005160IMD1", "TG1.A.01.4.3", "BTP 50", "HD-2614", "Semi", 100),
        R("M02", "VRHOITN0085JPA10023", "", "NL85", "", "Mạ băng 20%", 12),
        R("M02", "VRHOITN0050JPA10023", "", "BTP 50", "", "MKS", 30),
        R("M03", "BOT-CHIEN-XU", "B1", "", "LSX 26.11.020", "", 40, "Bột chiên xù"),
        R("M01", "TPC-VRHLBTNXECR001620IMA1", "", "BTP 16", "HD-2620", "IQF", 5),
    });
    private static List<string> Q(Gs.Data d, string q) => Gs.Search(d, q, out _).Select(r => r.Src + ":" + r.Ma).ToList();

    [Fact]
    public void So_khop_giong_app_cu()
    {
        var d = Sample();
        // bỏ dấu + không phân biệt hoa thường; các từ tìm riêng ở cột bất kỳ (VÀ)
        Assert.Equal(new[] { "M03:BOT-CHIEN-XU" }, Q(d, "bot CHIEN"));
        Assert.Equal(new[] { "M02:VRHOITN0085JPA10023" }, Q(d, "ma bang 20"));
        Assert.Equal(new[] { "M01VT:VRHLBTNXECR001620IMA1" }, Q(d, "tom the a.01.1"));
        // bỏ khoảng trắng: "hd2614" = "HD-2614"? không (có dấu -); "lsx26.11" = "LSX 26.11"
        Assert.Equal(new[] { "M03:BOT-CHIEN-XU" }, Q(d, "lsx26.11.020"));
        // phần đầu là nhãn Size → chỉ dòng có đúng nhãn đó, phần còn lại lọc tiếp
        Assert.Equal(new[] { "M02:VRHOITN0050JPA10023" }, Q(d, "BTP 50 MKS"));
        Assert.Equal(new[] { "M01VT:VRHLBTNXECR005160IMD1", "M02:VRHOITN0050JPA10023" }, Q(d, "btp 50").OrderBy(x => x).ToList());
        // cả ô dạng nhãn: "BTP 1" → nhãn bắt đầu bằng btp1; có dấu cách cuối → đúng nhãn
        Assert.Equal(2, Q(d, "BTP 1").Count);
        Assert.Empty(Q(d, "BTP 1 "));
        Assert.Equal(2, Q(d, "BTP 16 ").Count);
        Assert.Single(Q(d, "nl 85"));
        // mã gộp bỏ "TPC-": 2 dòng cùng mã → xếp liền nhau theo thứ tự nguồn
        var hit = Gs.Search(d, "001620", out _);
        Assert.Equal(new[] { "M01", "M01VT" }, hit.Select(r => r.Src));
        Assert.Equal(hit[0].Key, hit[1].Key);
        Assert.Empty(Q(d, "   "));
    }

    [Fact]
    public void To_sang_bo_dau_bo_khoang_trang()
    {
        var mk = Gs.Highlight("Tôm thẻ BTP 50", new[] { "the", "btp50" });
        Assert.Equal("    ___ ______", new string(mk.Select(b => b ? '_' : ' ').ToArray()));
        Assert.Equal("A.01.1.1", Gs.ShortPlace("TG1.A.01.1.1"));
    }

    [Fact]
    public void Tim_phieu_LSX_cont_va_giai_ma()
    {
        GsDoc D(string id, params string[] f) => new("pnx", id, "m13", "Phiếu " + f[0], "", string.Join(" | ", f.Select(GsDocs.Fold)));
        var docs = new[] { D("1", "21.11 XK 05", "LSX 26.11.020", "CGC", "TGHU8812345"), D("2", "22.11 XK 06", "LSX 26.11.031", "MATSUOKA", "MSKU1234567"), D("3", "XK 07", "26.11.03", "Thuận Phước", "") };
        Assert.Equal(new[] { "1" }, GsDocs.Search(docs, "tghu88").Select(d => d.Id));
        Assert.Equal(new[] { "3", "2" }, GsDocs.Search(docs, "26.11.03").Select(d => d.Id));   // khớp đầu trường lên trước, rồi theo thứ tự
        Assert.Equal(new[] { "3" }, GsDocs.Search(docs, "thuan phuoc").Select(d => d.Id));
        Assert.Empty(GsDocs.Search(docs, "  "));
        var data = new Gs.Data(); data.Seg[8] = new() { ["3140"] = "Size 31/40" }; data.Seg[1] = new() { ["VRHL"] = "Tôm thẻ" };
        var dec = Gs.Decode(data, "TPC-VRHLBTNXECR003140IMC1");
        Assert.Equal(10, dec.Count);
        Assert.Equal(("Tên sản phẩm", "VRHL", (string?)"Tôm thẻ"), dec[0]);
        Assert.Equal(("Size", "3140", (string?)"Size 31/40"), dec[7]);
        Assert.Null(dec[9].Name);
        Assert.Empty(Gs.Decode(data, "BOT-A"));
    }

    // ---- gom dữ liệu thật từ các module
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-gs-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;
    public TimNhanhTests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "QT", "matkhau1"); _s.Auth.Login("admin", "matkhau1");
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Gom_ton_NXT_bot_va_Vi_tri_bot()
    {
        SkLine L(string loai, string ngay, string ma, double sl, double net, string vt) =>
            NormLine(new JsonObject { ["loai"] = loai, ["ngay"] = ngay, ["maHang"] = ma, ["soLuong"] = sl, ["netDv"] = net, ["viTri"] = vt, ["chungTu"] = "X", ["hanDung"] = "2027-01-31" });
        var sk = _s.SoKho; sk.Load();
        sk.Commit(new[] { L("nhap", "2026-11-01", "BOT-A", 10, 25, "B1"), L("nhap", "2026-11-01", "SOT-B", 4, 20, "S1"), L("xuat", "2026-11-03", "SOT-B", 4, 20, "S1") }, Array.Empty<string>(), Array.Empty<SkLine>(), "nhap", "test");
        _s.Vtb.Load(); _s.Vtb.Push(VtbService.FromLedger(sk.Lines, "2026-11-05"), "2026-11-05");
        var d = new GsService(_s).Build();
        Assert.Empty(d.Errors);
        var rows = d.Rows.Select(r => $"{r.Src}|{r.Vt}|{r.Ma}|{r.Ton}|{r.Kg}").OrderBy(x => x).ToList();
        Assert.Equal(new[] { "M01VTB|B1|BOT-A|10|", "M03|B1|BOT-A|10|250" }, rows);   // SOT-B hết tồn → bỏ
        Assert.Equal(2, Gs.Search(d, "bot a", out _).Count);
        Assert.Contains(Gs.Search(d, "bot a hsd 31/01/2027", out _), r => r.Src == "M03");
    }
}
