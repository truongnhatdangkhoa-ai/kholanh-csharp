using System.Text.Json.Nodes;
using KhoLanh.Core.CuoiKy;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>
/// Lõi Số liệu cuối kỳ phải ra ĐÚNG kết quả như dc2-*.js của app cũ: Data/ck-golden.json do Data/ck-golden.gen.js tạo bằng chính lõi JS
/// (cùng file đầu vào → cùng dòng đọc được, cùng chênh lệch, nguyên nhân, gợi ý mã, cờ chứng từ, KPI, thao tác người dùng).
/// </summary>
public class CuoiKyTests
{
    private static readonly JsonObject G = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "ck-golden.json")))!;

    private static List<IReadOnlyList<object?>> Aoa(string k) => G["aoa"]![k]!.AsArray().Select(r => (IReadOnlyList<object?>)r!.AsArray().Select(c => c is JsonValue v ? (v.TryGetValue<double>(out var d) ? d : (object?)v.GetValue<string>()) : null).ToList()).ToList();
    private static double? N(JsonNode? n) => n is null ? null : n.GetValue<double>();
    private static string S(JsonNode? n) => n?.GetValue<string>() ?? "";

    private static (CkResult R, CkInput Inp, CkSource An, CkSource Kt, CkSource Gui, CkSource Ct) Run()
    {
        var kt = CkImport.ReadKetoan(Aoa("nxt")); var ct = CkImport.ReadCT(Aoa("ct")); var dm = CkImport.ReadDanhMuc(Aoa("dm"));
        var an = CkImport.ReadAnAn(Aoa("anan")); var gui = CkImport.ReadGui(Aoa("gui"));
        kt.Ct = ct;
        var p = G["period"]!;
        var inp = new CkInput { AnAn = an, KeToan = kt, Gui = gui, Ct = ct.Vouchers, DanhMuc = dm, Period = new CkPeriod(S(p["from"]), S(p["to"])) };
        return (CkEngine.Reconcile(inp), inp, an, kt, gui, ct);
    }

    [Fact]
    public void Readers_match_js()
    {
        var (_, _, an, kt, gui, ct) = Run();
        var rd = G["readers"]!;
        void Rows(JsonArray exp, List<CkRow> act)
        {
            Assert.Equal(exp.Count, act.Count);
            for (var i = 0; i < exp.Count; i++)
            {
                var e = exp[i]!; var a = act[i];
                Assert.Equal(S(e["ma"]), a.Ma); Assert.Equal(N(e["kg"]), a.Kg); Assert.Equal(e["row"]!.GetValue<int>(), a.Row);
                Assert.Equal(N(e["dauKg"]), a.DauKg); Assert.Equal(N(e["nhapKg"]), a.NhapKg); Assert.Equal(N(e["xuatKg"]), a.XuatKg);
                Assert.Equal(N(e["selfLech"]), a.SelfLech); Assert.Equal(N(e["nhetLech"]), a.NhetLech);
                if (e["via"] is not null) { Assert.Equal(S(e["via"]), a.Via); Assert.Equal(S(e["maGui"]), a.MaGui); Assert.Equal(S(e["ngayNhap"]), a.NgayNhap ?? ""); }
            }
        }
        Rows(rd["anan"]!["rows"]!.AsArray(), an.Rows);
        Rows(rd["ketoan"]!["rows"]!.AsArray(), kt.Rows);
        Rows(rd["gui"]!["rows"]!.AsArray(), gui.Rows);
        Assert.Equal(rd["ketoan"]!["known"]!.AsArray().Select(S), kt.KnownCodes);
        Assert.Equal(S(rd["anan"]!["period"]!["from"]), an.Period!.From);
        Assert.Equal(S(rd["gui"]!["period"]!["to"]), gui.Period!.To);
        var cv = rd["ct"]!["rows"]!.AsArray();
        Assert.Equal(cv.Count, ct.Vouchers.Count);
        for (var i = 0; i < cv.Count; i++) { Assert.Equal(S(cv[i]!["id"]), ct.Vouchers[i].Id); Assert.Equal(S(cv[i]!["ngay"]), ct.Vouchers[i].Ngay); Assert.Equal(S(cv[i]!["dir"]), ct.Vouchers[i].Dir); Assert.Equal(N(cv[i]!["netLech"]), ct.Vouchers[i].NetLech); }
        foreach (var (k, s) in new[] { ("anan", an), ("ketoan", kt), ("gui", gui), ("ct", ct) })
        {
            var counts = rd[k]!["stats"]!["counts"]!.AsObject();
            foreach (var (key, v) in counts) if (key != "_all") Assert.True(s.Stats.Counts.GetValueOrDefault(key) == v!.GetValue<int>(), $"{k}.{key}: JS {v} ≠ C# {s.Stats.Counts.GetValueOrDefault(key)}");
        }
        Assert.Equal(G["readers"]!["dm"]!.GetValue<int>(), CkImport.ReadDanhMuc(Aoa("dm"))!.Count);
    }

    [Fact]
    public void Reconcile_matches_js()
    {
        var (R, _, _, _, _, _) = Run();
        var items = G["items"]!.AsArray();
        Assert.Equal(items.Count, R.Items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var e = items[i]!; var a = R.Items[i]; var at = $"mã {a.Ma}";
            Assert.Equal(S(e["ma"]), a.Ma);
            Assert.Equal(N(e["aa"]), a.Aa); Assert.Equal(N(e["gui"]), a.Gui); Assert.Equal(N(e["kt"]), a.Kt); Assert.Equal(N(e["kho"]), a.Kho); Assert.Equal(N(e["diff"]), a.Diff);
            Assert.Equal(S(e["status"]), a.Status); Assert.True(S(e["group"]) == a.Group, at + " group");
            Assert.Equal(e["rootKnown"]!.GetValue<bool>(), a.RootKnown);
            var dc = e["decomp"] as JsonArray;
            Assert.Equal(dc is null, a.Decomp is null);
            if (dc is not null) Assert.Equal(dc.Select(N), new double?[] { a.Decomp!.Dau.D, a.Decomp.Nhap.D, a.Decomp.Xuat.D, a.Decomp.Resid });
            var ec = e["causes"]!.AsArray();
            Assert.True(ec.Select(c => S(c!["code"]) + "/" + S(c["basis"]) + "/" + S(c["conf"])).SequenceEqual(a.Causes.Select(c => c.Code + "/" + c.Basis + "/" + c.Conf)), at + ": " + string.Join(" ", a.Causes.Select(c => c.Code + "/" + c.Conf)));
            for (var k = 0; k < ec.Count; k++)
            {
                Assert.Equal(ec[k]!["evidence"]!.AsArray().Select(S), a.Causes[k].Evidence);
                Assert.Equal(S(ec[k]!["action"]), a.Causes[k].Action);
            }
            var cands = e["cands"]!.AsArray();
            Assert.True(cands.Select(c => S(c!["ma"]) + ":" + N(c["score"]) + ":" + S(c["conf"]) + ":" + S(c["kind"])).SequenceEqual(a.Cands.Select(c => c.Ma + ":" + c.Score + ":" + c.Conf + ":" + c.Kind)), at + " cands");
            for (var k = 0; k < cands.Count; k++) Assert.Equal(cands[k]!["reasons"]!.AsArray().Select(S), a.Cands[k].Reasons);
            var combos = e["combos"]!.AsArray();
            Assert.Equal(combos.Select(c => string.Join("+", c!["codes"]!.AsArray().Select(S)) + "=" + N(c["sum"]) + S(c["conf"])), a.Combos.Select(c => string.Join("+", c.Codes) + "=" + c.Sum + c.Conf));
            var ctf = e["ct"]!;
            Assert.Equal(ctf["n"]!.GetValue<int>(), a.Ct.Lines.Count); Assert.Equal(ctf["nFlag"]!.GetValue<int>(), a.Ct.NFlag);
            Assert.Equal(ctf["flags"]!.AsArray().Select(S), a.Ct.Lines.Select(l => string.Join(",", l.Flags.Select(f => f.Code))));
        }
        var v = G["validation"]!.AsArray().Select(x => S(x!["level"]) + ":" + S(x["text"])).ToList();
        Assert.Equal(v, R.Validation.Select(x => x.Level + ":" + x.Text));
    }

    [Fact]
    public void User_actions_and_kpi_match_js()
    {
        var (R, _, _, _, _, _) = Run();
        var items = G["items"]!.AsArray();
        string A1 = S(items.First(x => S(x!["ma"]).StartsWith("VRHLBTNXXXR000041", StringComparison.Ordinal))!["ma"]);
        string A3 = S(items.First(x => S(x!["ma"]).StartsWith("VRHLBTNXXXR000050", StringComparison.Ordinal))!["ma"]);
        string A8 = S(items.First(x => S(x!["ma"]).StartsWith("VRCOBTNXXXR000080", StringComparison.Ordinal))!["ma"]);
        string A5 = S(items.First(x => S(x!["ma"]).StartsWith("VRPDBTNXXXR000070", StringComparison.Ordinal))!["ma"]);
        var st = new CkState();
        Assert.True(CkHistory.Transfer(st, A1, A3, 50, new CkMeta("admin", "2026-11-01T00:00:00.000Z", "ghi nhầm mã")).Ok);
        Assert.True(CkHistory.Confirm(st, A8, "chi-co-1-ben", "", 70, -70, null, new CkMeta("admin", "2026-11-01T00:00:01.000Z", "kế toán chưa ghi")).Ok);
        var t3 = CkHistory.SetNote(st, A5, "xem lại phiếu", new CkMeta("admin", "2026-11-01T00:00:02.000Z"));
        Assert.True(CkHistory.Undo(st, t3.Entry!.Id, new CkMeta("admin", "2026-11-01T00:00:03.000Z")).Ok);
        Assert.False(CkHistory.Undo(st, t3.Entry!.Id, new CkMeta("admin")).Ok);
        var kpi = R.Overlay(st);
        // KPI của JS được tính SAU thao tác (overlay)
        var k = G["kpi"]!;
        Assert.Equal(k["tong"]!.GetValue<int>(), kpi.Tong); Assert.Equal(k["lech"]!.GetValue<int>(), kpi.Lech); Assert.Equal(k["chuaXacDinh"]!.GetValue<int>(), kpi.ChuaXacDinh);
        Assert.Equal(k["daXuLy"]!.GetValue<int>(), kpi.DaXuLy); Assert.Equal(k["tienDo"]!.GetValue<int>(), kpi.TienDo);
        Assert.Equal(N(k["tongKho"]), kpi.TongKho); Assert.Equal(N(k["tongKt"]), kpi.TongKt); Assert.Equal(N(k["lechRong"]), kpi.LechRong); Assert.Equal(N(k["lechTuyetDoiSau"]), kpi.LechTuyetDoiSau);
        Assert.Equal(k["thieu"]!.GetValue<int>(), kpi.Thieu); Assert.Equal(k["quyCach"]!.GetValue<int>(), kpi.QuyCach); Assert.Equal(k["saiMa"]!.GetValue<int>(), kpi.SaiMa); Assert.Equal(k["chungTu"]!.GetValue<int>(), kpi.ChungTu);
        Assert.Equal(k["boQuaBot"]!.GetValue<int>(), kpi.BoQuaBot);
        foreach (var e in items)
        {
            var a = R.ByMa[S(e!["ma"])];
            Assert.True(S(e["statusUi"]) == a.StatusUi, a.Ma + " statusUi " + a.StatusUi);
            Assert.Equal(N(e["diffAfter"]), a.DiffAfter); Assert.Equal(N(e["adjKho"]), a.AdjKho);
        }
        var hs = G["state"]!["history"]!.AsArray();
        Assert.Equal(hs.Select(h => S(h!["action"]) + "|" + S(h["summary"]) + "|" + h["undone"]!.GetValue<bool>()), st.History.Select(h => h.Action + "|" + h.Summary + "|" + h.Undone));
        Assert.Equal(hs.Select(h => string.Join(",", h!["mas"]!.AsArray().Select(S))), st.History.Select(h => string.Join(",", h.Mas)));
    }
}
