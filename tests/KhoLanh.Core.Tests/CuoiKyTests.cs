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

public class CuoiKyServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-ck-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;
    public CuoiKyServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "QT", "matkhau1"); _s.Auth.Login("admin", "matkhau1");
    }
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private string Xlsx(string name, params (string sheet, object?[][] rows)[] sheets)
    {
        var p = Path.Combine(_dir, name);
        using var wb = new ClosedXML.Excel.XLWorkbook();
        foreach (var (sn, rows) in sheets)
        {
            var ws = wb.AddWorksheet(sn);
            for (var r = 0; r < rows.Length; r++) for (var c = 0; c < rows[r].Length; c++)
            {
                var v = rows[r][c]; var cell = ws.Cell(r + 1, c + 1);
                if (v is double d) cell.Value = d; else if (v is int i) cell.Value = i; else if (v is DateTime dt) cell.Value = dt; else if (v is string s) cell.Value = s;
            }
        }
        wb.SaveAs(p); return p;
    }

    [Fact]
    public void Lay_tu_module_sua_o_nguon_va_chung_tu_An_An()
    {
        const string A = "VRHLBTNXXXR000040IM10", B = "VRHLBTNXXXR000041IM10";
        JsonObject Cell(string j) => new() { ["data"] = "__JSON__" + j };
        // dữ liệu đã nạp trong module Tồn kho An An (LoadData c0..cN + LoadDataMeta.headers)
        _s.Lan.Write("ton-kho-an-an", new Dictionary<string, JsonArray?>
        {
            ["LoadData"] = new JsonArray(new JsonObject { ["c0"] = "TPC-" + A, ["c1"] = "Tôm 31/40", ["c2"] = 12, ["c3"] = 10, ["c4"] = 120 }, new JsonObject { ["c0"] = B, ["c1"] = "Tôm 31/40", ["c2"] = 5, ["c3"] = 10, ["c4"] = 50 }),
            ["LoadDataMeta"] = new JsonArray(Cell("{\"headers\":[\"Mã hàng\",\"Tên hàng\",\"Tồn cuối\",\"Trọng lượng\",\"Tồn cuối (NET)\"],\"fileName\":\"TON AN AN 31.10.xlsx\",\"dataDate\":\"31/10/2026\"}")),
        });
        var ck = _s.CuoiKy;
        Assert.Equal("TON AN AN 31.10.xlsx · dữ liệu ngày 31/10/2026", ck.ModuleInfo("anan"));
        Assert.Null(ck.ModuleInfo("gui"));
        Assert.Throws<KhoLanh.Core.Services.BusinessException>(() => ck.LoadFromModule("gui"));
        var an = ck.LoadFromModule("anan");
        Assert.True(an.Ok, an.Error); Assert.Equal("module Tồn kho An An", an.FromModule); Assert.Equal(2, an.Rows.Count); Assert.Equal(120, an.Rows[0].Kg);
        var kt = Xlsx("kt.xlsx", ("NXT", new[]
        {
            new object?[] { "Từ ngày 01/10/2026 đến ngày 31/10/2026" },
            new object?[] { "Mã thành phẩm", "Tên thành phẩm", "Tồn đầu kỳ", "Nhập trong kỳ", "Xuất trong kỳ", "Tồn CK" },
            new object?[] { A, "Tôm", 120, 0, 0, 100 }, new object?[] { B, "Tôm", 50, 0, 0, 50 },
        }));
        Assert.True(ck.LoadFile("ketoan", kt).Ok);
        // chứng từ xuất An An
        var xu = Xlsx("xuat.xlsx", ("Xuat", new[]
        {
            new object?[] { "Ngày xuất", "Số phiếu xuất", "Mã hàng", "Tên hàng", "Net" },
            new object?[] { "05/10/2026", "PX-1", A, "Tôm", 20.0 },
        }));
        var xs = ck.LoadFile("anan_xuat", xu);
        Assert.True(xs.Ok, xs.Error); Assert.Single(xs.Vouchers); Assert.Equal("X", xs.Vouchers[0].Dir);
        var r = ck.Run();
        Assert.Equal(20, r.ByMa[A].Diff);
        Assert.Contains(r.ByMa[A].Ct.Lines, l => l.V.Src == "anan" && l.V.Phieu == "PX-1");
        // sửa ô số lượng của dòng An An → đối chiếu lại khớp; trả lại giá trị gốc → lệch lại; hoàn tác → khớp
        var row = ck.Files["anan"].Rows.First(x => x.Ma == A);
        ck.EditCell("anan", row, "kg", "100", "kiểm lại sổ");
        Assert.True(ck.IsEdited("anan", row, "kg"));
        Assert.Equal(0, ck.Run().ByMa[A].Diff);
        Assert.Throws<KhoLanh.Core.Services.BusinessException>(() => ck.EditCell("anan", row, "kg", "100"));
        ck.RevertCell("anan", row, "kg");
        Assert.False(ck.IsEdited("anan", row, "kg")); Assert.Equal(20, ck.Run().ByMa[A].Diff);
        ck.Undo(ck.State.History[^1].Id);
        Assert.Equal(0, ck.Run().ByMa[A].Diff);
        Assert.StartsWith("Sửa dữ liệu Tồn kho An An (dòng 2): số lượng 120 → 100", ck.State.History.First(h => h.Action == "sua-du-lieu").Summary);
        // máy khác mở lại: nguồn từ module + chứng từ xuất + phần sửa còn nguyên
        var ck2 = new CkService(() => _s.Lan, _s.Session, _s.Settings);
        Assert.True(ck2.Load());
        Assert.Equal("module Tồn kho An An", ck2.Files["anan"].FromModule);
        Assert.Single(ck2.Files["anan_xuat"].Vouchers);
        Assert.Equal(0, ck2.Run().ByMa[A].Diff);
    }

    [Fact]
    public void Nap_file_doi_chieu_xu_ly_va_mo_lai()
    {
        const string A = "VRHLBTNXXXR000040IM10", B = "VRHLBTNXXXR000041IM10";
        var an = Xlsx("an.xlsx", ("NXT", new[]
        {
            new object?[] { "Từ ngày 01/10/2026 đến ngày 31/10/2026" }, Array.Empty<object?>(),
            new object?[] { "Mã hàng", "Tên hàng", "Tồn cuối", "Trọng lượng", "Tồn cuối (NET)" },
            new object?[] { "TPC-" + A, "Tôm 31/40", 12, 10, 170 }, new object?[] { B, "Tôm 31/40", 5, 10, 50 },
        }));
        var kt = Xlsx("kt.xlsx", ("NXT", new[]
        {
            new object?[] { "Mã thành phẩm", "Tên thành phẩm", "Tồn đầu kỳ", "Nhập trong kỳ", "Xuất trong kỳ", "Tồn CK" },
            new object?[] { A, "Tôm", 120, 0, 0, 120 }, new object?[] { B, "Tôm", 100, 0, 0, 100 },
        }), ("Nhap lieu", new[]
        {
            new object?[] { "Số CT", "Ngày", "Mã", "NX", "SLKG" }, new object?[] { "XK1", new DateTime(2026, 10, 5), B, "XK", 50.0 },
        }));
        var ck = _s.CuoiKy;
        Assert.True(ck.LoadFile("anan", an).Ok);
        var k = ck.LoadFile("ketoan", kt);
        Assert.True(k.Ok); Assert.NotNull(k.Ct); Assert.Equal("2026-10-05", k.Ct!.Vouchers[0].Ngay);
        Assert.Equal(new CkPeriod("2026-10-01", "2026-10-31"), ck.Period);
        var r = ck.Run();
        Assert.Equal(2, r.Kpi.Lech);
        Assert.Equal(50, r.ByMa[A].Diff); Assert.Equal(-50, r.ByMa[B].Diff);
        Assert.Equal(B, r.ByMa[A].Cands[0].Ma);
        ck.Transfer(A, B, 50, "ghi nhầm mã", "kho");
        Assert.Equal(0, r.ByMa[A].DiffAfter); Assert.Equal("xong", r.ByMa[A].StatusUi);
        Assert.Throws<KhoLanh.Core.Services.BusinessException>(() => ck.Transfer(A, B, 10, "", "kho"));
        var x = ck.BuildExcel(); Assert.Equal(10, x.Sheets.Count);
        // máy khác mở lại: cùng dữ liệu + thao tác
        var ck2 = new CkService(() => _s.Lan, _s.Session, _s.Settings);
        Assert.True(ck2.Load());
        Assert.Equal(2, ck2.Files.Count); Assert.Single(ck2.Files["ketoan"].Ct!.Vouchers);
        var r2 = ck2.Run();
        Assert.Equal(0, r2.ByMa[B].DiffAfter);
        Assert.Equal(2, ck2.State.Adjustments.Count);
        ck2.Undo(ck2.State.History[^1].Id);
        Assert.Equal(-50, r2.ByMa[B].DiffAfter);
        ck2.Clear();
        Assert.False(new CkService(() => _s.Lan, _s.Session, _s.Settings).Load());
    }
}
