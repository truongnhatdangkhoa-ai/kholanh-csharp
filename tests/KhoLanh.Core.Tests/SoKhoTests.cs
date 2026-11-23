using System.Text.Json.Nodes;
using KhoLanh.Core.SoKho;
using Xunit;
using static KhoLanh.Core.SoKho.SoKhoCore;

namespace KhoLanh.Core.Tests;

/// <summary>
/// Sổ kho (NXT Bột/Sốt): đối chiếu lõi C# với CHÍNH lõi JS của app cũ (so-kho-core.js) trên 263 dòng sổ ngẫu nhiên —
/// Data/sokho-oracle.json do node sinh (FIFO, xuất theo Net, chuyển vị trí, điều chỉnh khác chiều, mốc tồn đầu, NXT, chốt tồn, định mức).
/// </summary>
public class SoKhoTests
{
    private static JsonObject Oracle() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "sokho-oracle.json")))!.AsObject();
    private static List<SkLine> Lines(JsonObject o) => DedupeById(o["raw"]!.AsArray().OfType<JsonObject>().Select(NormLine)).Lines;

    private static void Same(JsonNode? exp, object? got, string where)
    {
        if (exp is null) { Assert.True(got is null || (got is double d && double.IsNaN(d)), where + " phải trống"); return; }
        if (exp is JsonValue v && v.TryGetValue<double>(out var e))
        {
            var g = got switch { double x => x, int i => i, null => double.NaN, _ => double.Parse(got.ToString()!) };
            Assert.True(Math.Abs(e - g) < 1e-6, $"{where}: JS {e} · C# {g}");
            return;
        }
        Assert.Equal(S(exp), got?.ToString() ?? "");
    }
    private static void Rows(JsonArray exp, IList<object?[]> got, string what)
    {
        Assert.True(exp.Count == got.Count, $"{what}: JS {exp.Count} dòng · C# {got.Count} dòng");
        for (var i = 0; i < exp.Count; i++)
        {
            var e = exp[i]!.AsArray();
            for (var j = 0; j < e.Count; j++) Same(e[j], got[i][j], $"{what}[{i}][{j}]");
        }
    }

    [Fact]
    public void Chuan_hoa_dong_giong_app_cu()
    {
        var o = Oracle(); var L = Lines(o);
        Rows(o["norm"]!.AsArray(), L.Select(l => new object?[] { l.Id, l.Loai, l.Ngay, l.SoLuong, l.Net, l.NetDv, l.HanDung, l.NgayNhap, l.Bad.Count }).ToList(), "norm");
        Assert.Equal(1234, L.Single(l => l.Id == "n1").SoLuong);
        Assert.Equal(1234.5, L.Single(l => l.Id == "n1").Net);
        Assert.Contains("Số lượng không đọc được", L.Single(l => l.Id == "bad").Bad);
    }

    [Fact]
    public void Ton_theo_lo_FIFO_moc_ton_dau_giong_app_cu()
    {
        var o = Oracle(); var L = Lines(o);
        object?[] Lot(SkLot l) => new object?[] { l.ViTri, l.MaHang, l.Size, l.HanDung, l.NgayNhap, l.HopDong, l.ChungTu, l.Sl, l.Net };
        Rows(o["all"]!.AsArray(), Compute(L).Lots.Select(Lot).ToList(), "tồn tất cả");
        var up = Compute(L, "2026-10-26");
        Rows(o["upTo"]!.AsArray(), up.Lots.Select(Lot).ToList(), "tồn đến 26/10");
        Assert.Equal(S(o["base"]), up.Base);
        Assert.Equal(o["neg"]!.GetValue<int>(), Negatives(L).Count);
    }

    [Fact]
    public void Ton_gom_NXT_chot_ton_dinh_muc_giong_app_cu()
    {
        var o = Oracle(); var L = Lines(o);
        Rows(o["stock"]!.AsArray(), Stock(L, "2026-11-10", "maHang", "viTri").Select(r => new object?[] { r["maHang"], r["viTri"], r.Sl, r.Net, r.Lots }).ToList(), "tồn gom");
        Rows(o["nxt"]!.AsArray(), Nxt(L, "2026-10-11", "2026-11-05", "maHang", "size").Select(r => new object?[] { r["maHang"], r["size"], r.DauSl, r.DauNet, r.NhapSl, r.NhapNet, r.XuatSl, r.XuatNet, r.DcSl, r.DcNet, r.CuoiSl, r.CuoiNet }).ToList(), "NXT");
        Rows(o["snap"]!.AsArray(), Snapshot(L, "2026-10-31", "x").Lines.Select(l => new object?[] { l.ViTri, l.MaHang, l.Size, l.HanDung, l.NgayNhap, l.SoLuong, l.Net }).ToList(), "chốt tồn");
        var dm = DinhMuc(L, new[] { new Norm("n1", "", "BOT-01", "", 5, "kg/100kg", ""), new Norm("n2", "LSX 26.10.002", "SOT-01", "", 2, "bao/tan", "") },
            new[] { new Output("", "LSX 26.10.001", 20000, "A", ""), new Output("", "LSX 26.10.002", 15000, "B", "") });
        Rows(o["dm"]!["rows"]!.AsArray(), dm.Rows.Select(r => new object?[] { r.HopDong, r.MaHang, r.DuKien, r.ThucXuat, r.Chenh, r.Pct, r.Status }).ToList(), "định mức");
        Rows(o["dm"]!["missing"]!.AsArray(), dm.Missing.Select(m => new object?[] { m.HopDong, m.MaHang, m.Size, m.ThucXuatSl, m.ThucXuatNet }).ToList(), "thiếu định mức");
        var hsd = o["hsd"]!.AsArray().Select(x => S(x)).ToList();
        Assert.Equal(hsd, new[] { HsdFromText("6 tháng", "31/08/2026"), HsdFromText("1 năm", "2028-02-29"), HsdFromText("2 tuần", "2026-10-01"), HsdFromText("abc", "2026-10-01") });
    }

    private static SkLine N(string id, string loai, string ngay, string ma, double? sl, double? net, string vt = "A1", string hd = "", string nn = "", string han = "") =>
        NormLine(new JsonObject { ["id"] = id, ["loai"] = loai, ["ngay"] = ngay, ["maHang"] = ma, ["soLuong"] = sl, ["net"] = net, ["viTri"] = vt, ["hopDong"] = hd, ["ngayNhap"] = nn, ["hanDung"] = han });

    [Fact]
    public void Kiem_ke_xuat_chi_dinh_lo_lich_su_hoan_tac()
    {
        var L = new List<SkLine> { N("a", "nhap", "2026-10-01", "B", 10, 250, han: "2027-01-01"), N("b", "nhap", "2026-10-05", "B", 4, 100, han: "2027-02-01") };
        var kk = KiemKePlan(L, "2026-10-10", new[] { new KkCount("B", "", "A1", 12), new KkCount("C", "", "A1", 2) }, "u");
        Assert.Equal(2, kk.NLech);
        Assert.Contains(kk.Rows, r => r.MaHang == "B" && r.Kind == "thieu" && r.Chenh == -2 && r.Net == -50);
        Assert.Contains(kk.Rows, r => r.MaHang == "C" && r.Kind == "moi" && r.ThieuNet);
        Assert.All(kk.Adj, a => Assert.Equal("dieuchinh", a.Loai));
        // xuất chỉ định lô hạn 01/02/2027 (chỉ có 4 bao) 6 bao → tràn 2 bao sang lô khác
        var sp = XuatLoCheck(L, new[] { N("x", "xuat", "2026-10-11", "B", 6, null, han: "2027-02-01", nn: "2026-10-05") });
        Assert.Equal(2, Assert.Single(sp).Spill.Sl);
        // lịch sử + hoàn tác: thêm 1 dòng rồi hoàn tác; sửa tiếp thì xung đột
        var add = ToStore(N("c", "xuat", "2026-10-12", "B", 1, null));
        var h = HistRows("op1", "u", "luu", "x", new[] { add }, Array.Empty<JsonObject>(), Array.Empty<(JsonObject, JsonObject)>(), "2026-10-12T00:00:00Z");
        var cur = L.Select(ToStore).Append(add).ToList();
        Assert.Equal(new[] { "c" }, UndoPlan(h, cur).Remove);
        add["soLuong"] = 3;
        Assert.Single(UndoPlan(h, cur).Conflicts);
        Assert.Equal("BS-1210-01", NextSo(L, "2026-10-12"));
        Assert.Equal("Thiếu mã hàng", Validate(N("z", "nhap", "2026-10-01", "", 1, 1)).Errs.First());
    }
}
