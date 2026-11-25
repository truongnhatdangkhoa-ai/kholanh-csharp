using System.Text.Json.Nodes;
using ClosedXML.Excel;
using KhoLanh.Core.TonKho;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KhoLanh.Core.Tests;

public class AnAnTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-aa-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;

    public AnAnTests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        _s.Auth.Login("admin", "matkhau1");
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private static readonly string[] Heads = { "Item ID", "Mã hàng", "Tên hàng", "Đặc tính", "Hợp đồng", "ĐVT", "Tồn đầu", "Nhập", "Xuất", "Tồn cuối", "Tồn cuối (NET)", "Trọng lượng", "Nguyên liệu", "Qui cách đông", "Tình trạng", "Hóa chất", "Thị trường", "Khách hàng", "Mạ băng", "Size", "Qui cách gói" };

    /// <summary>File kiểu báo cáo thật: 1 dòng tựa phía trên, tiêu đề ở dòng 2; mỗi dòng (mã, đặc tính, hợp đồng, tồn cuối, trọng lượng, size).</summary>
    private string Xlsx(string name, params (string ma, string dt, string hd, double? ton, double tl, string size)[] rows)
    {
        var path = Path.Combine(_dir, name);
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Sheet");
        ws.Cell(1, 1).Value = "Báo cáo - Kho - 05-Nhập xuất tồn";
        for (var i = 0; i < Heads.Length; i++) ws.Cell(2, i + 1).Value = Heads[i];
        var r = 3;
        foreach (var x in rows)
        {
            ws.Cell(r, 1).Value = r; ws.Cell(r, 2).Value = "TPC-" + x.ma; ws.Cell(r, 3).Value = "Tôm " + x.ma; ws.Cell(r, 4).Value = x.dt; ws.Cell(r, 5).Value = x.hd; ws.Cell(r, 6).Value = "Thùng";
            if (x.ton is double t) { ws.Cell(r, 10).Value = t; ws.Cell(r, 11).Value = t * x.tl; }
            ws.Cell(r, 12).Value = x.tl; ws.Cell(r, 20).Value = x.size; r++;
        }
        wb.SaveAs(path);
        return path;
    }

    [Fact]
    public void Data_date_parse_and_guess_like_old_app()
    {
        var now = new DateTime(2026, 11, 22);
        Assert.Equal("23/09/2026", DataDate.Parse("23/9", now));
        Assert.Equal("23/09/2026", DataDate.Parse("23.09.2026", now));
        Assert.Equal("23/09/2026", DataDate.Parse("23-9-26", now));
        Assert.Equal("23/09/2026", DataDate.Parse("23092026", now));
        Assert.Equal("", DataDate.Parse("31/2", now));
        Assert.Equal("21/11/2026", DataDate.Guess("TON KHO AN AN 21.11.xlsx", now));
        Assert.Equal("28/12/2025", DataDate.Guess("TON 28.12.xlsx", new DateTime(2026, 1, 3)));   // tương lai quá 7 ngày → năm trước
        Assert.Equal("23/09/2026", DataDate.Guess("Ton_23-09-2026.xlsx", now));
        Assert.Equal("", DataDate.Guess("tonkho.xlsx", now));
        Assert.Equal("2026-11-21", DataDate.Iso("21/11/2026"));
    }

    [Fact]
    public void Nap_check_numbers_and_problems()
    {
        Assert.Null(NapExcel.Num("."));
        Assert.Equal(-100, NapExcel.Num("(100)"));
        Assert.Equal(1234, NapExcel.Num("1,234"));
        Assert.Equal(30, NapExcel.Num("30."));
        Assert.True(double.IsNaN(NapExcel.Num("abc")!.Value));
        var rows = new List<string[]>
        {
            new[] { "1", "A", "x", "D1", "HD", "T", "", "", "", "5", "50" },
            new[] { "2", "A", "x", "D1", "HD", "T", "", "", "", "-1", "abc" },
            new[] { "3", "", "x", "D1", "HD", "T", "", "", "", "2", "20" },
        };
        var c = NapExcel.CheckM1(Heads.Take(11).ToList(), rows, 2, 0, null);
        Assert.Equal(3, c.Total); Assert.Equal(1, c.Err); Assert.Equal(1, c.Warn); Assert.Equal(1, c.Ok);
        Assert.Equal(70, c.Kg); Assert.Equal(1, c.Items);
        Assert.Contains(c.Problems, p => p.Msg.Contains("Trùng mã hàng"));
    }

    [Fact]
    public void Goi_y_nhap_lieu_lay_moi_ma_ton_An_An_ke_ca_ma_da_co_o_vi_tri()
    {
        // 1.89: Tìm nhanh (Rows) bỏ dòng M01 khi mã đã có ở Tồn vị trí — nhưng nguồn gợi ý Nhập liệu (Data.AnAn) phải giữ ĐỦ mọi mã còn tồn của An An
        var aa = _s.AnAn; aa.Load();
        aa.Commit(aa.Preview(Xlsx("TON 20.11.xlsx", ("VRHLITNXXXR000050JRA1", "12.N-EU-H", "26.10.001", 24, 9, "50"), ("B2", "EU", "26.10.002", 5, 3, "21/25"))), keepUndo: false);
        var vt = _s.ViTri; vt.Load();
        vt.Import(new KhoLanh.Core.ViTri.ViTriService.FileData { FileName = "vt.xlsx", Rows = { vt.Build("VRHLITNXXXR000050JRA1", "Tôm - 12.N-EU-H - 26.10.001", "Ctn", "", "TG1.A-01", 24, 9) } });
        var d = new KhoLanh.Core.TimNhanh.GsService(_s).Build();
        Assert.DoesNotContain(d.Rows, r => r.Src == "M01" && r.Ma == "VRHLITNXXXR000050JRA1");   // Tìm nhanh: đã có ở vị trí
        Assert.Equal(new[] { "B2", "VRHLITNXXXR000050JRA1" }, d.AnAn.Select(r => r.Ma).OrderBy(x => x).ToArray());
        var sug = KhoLanh.Core.Entry.MaGoiY.Build(d.AnAn, Array.Empty<(string, string)>(), d.Seg);
        Assert.Equal("VRHLITNXXXR000050JRA1", Assert.Single(KhoLanh.Core.Entry.MaGoiY.Search(sug, "0050")).Ma);
    }

    [Fact]
    public void Import_review_commit_history_undo()
    {
        var aa = _s.AnAn; aa.Load();
        var f1 = Xlsx("TON 20.11.xlsx", ("A1", "KD", "26.10.001", 10, 2, "16/20"), ("B2", "EU", "26.10.002", 5, 3, "21/25"), ("C3", "EU", "", null, 1, "")); // C3 hết tồn → bỏ
        var pv = aa.Preview(f1);
        Assert.False(pv.Blocking);
        Assert.Equal("20/11/2026".Substring(0, 6), pv.DataDate.Substring(0, 6));
        Assert.Equal(2, pv.Check.Total);
        aa.Commit(pv);
        Assert.Equal(2, aa.D.Rows.Count);
        Assert.Equal("A1", aa.D.Cell(0, 1));                        // bỏ "TPC-"
        var log = aa.History(); Assert.Single(log);
        Assert.Equal("applied", NapExcel.S(log[0]["status"]));
        Assert.Equal(35.0, log[0]["kg"]!.GetValue<double>());
        Assert.True(aa.CanUndo(log[0], log));

        // nạp lần 2 → lần 1 "replaced", ảnh chụp lần trước để so sánh
        var f2 = Xlsx("TON 21.11.xlsx", ("A1", "KD", "26.10.001", 4, 2, "16/20"), ("D4", "EU", "", 7, 1, "21/25"));
        var pv2 = aa.Preview(f2);
        aa.Commit(pv2);
        log = aa.History();
        Assert.Equal(new[] { "applied", "replaced" }, log.Select(x => NapExcel.S(x["status"])));
        var cmp = aa.CompareWithPrevious()!.Value;
        Assert.Equal("TON 20.11.xlsx", cmp.Prev.FileName);
        Assert.Equal(1, cmp.R.Sum.Moi); Assert.Equal(1, cmp.R.Sum.Het); Assert.Equal(1, cmp.R.Sum.Giam);

        // nạp lại đúng file đã nạp → cảnh báo
        var pv3 = aa.Preview(f2);
        Assert.Contains(pv3.Warns, w => w.Contains("ĐÃ ĐƯỢC NẠP"));

        // hoàn tác đợt mới nhất → dữ liệu về như file 1, đợt 1 "applied" lại
        aa.UndoNap(NapExcel.S(log[0]["id"]));
        Assert.Equal(2, aa.D.Rows.Count);
        Assert.Equal("B2", aa.D.Cell(1, 1));
        log = aa.History();
        Assert.Equal(new[] { "undone", "applied" }, log.Select(x => NapExcel.S(x["status"])));

        // bản đọc lại từ database (máy khác / mở lại) thấy đúng dữ liệu
        var again = new AnAnService(() => _s.Lan, _s.Session, _s.Audit); again.Load();
        Assert.Equal("TON 20.11.xlsx", again.D.FileName);
    }

    [Fact]
    public void Slip_deduct_and_undo_write_numbers_to_load_data()
    {
        var aa = _s.AnAn; aa.Load();
        aa.Commit(aa.Preview(Xlsx("TON 20.11.xlsx", ("A1", "KD", "26.10.001", 10, 2, "16/20"), ("B2", "EU", "26.10.002", 5, 3, "21/25"))), keepUndo: false);
        var slip = aa.CreateSlip("tpc", new Dictionary<int, double> { [1] = 2, [0] = 3 })!;
        Assert.Equal("PX-0001", AnAnService.SlipId(slip));
        Assert.Equal(new[] { 0, 1 }, AnAnService.SlipLines(slip).Select(l => l["i"]!.GetValue<int>()));
        aa.DeductSlip("PX-0001");
        var tc = aa.D.Col("Tồn cuối");
        Assert.Equal("7", aa.D.Cell(0, tc)); Assert.Equal("3", aa.D.Cell(1, tc));
        var raw = JsonNode.Parse(_s.Lan.Read(AnAnStore.Db, AnAnStore.SLoadData))!.AsArray();
        Assert.Equal(7, raw[0]!["c" + tc]!.GetValue<double>());           // ghi dạng SỐ như app cũ
        aa.UndoSlip("PX-0001");
        Assert.Equal("10", aa.D.Cell(0, tc));
        var re = new AnAnService(() => _s.Lan, _s.Session, _s.Audit); re.Load();
        Assert.Single(re.Slips); Assert.False(AnAnService.SlipDeducted(re.Slips[0])); Assert.Equal(2, re.SlipSeq);
    }

    [Fact]
    public void Kho_gui_syncs_from_ton_kho_gui_when_it_changes()
    {
        var aa = _s.AnAn; aa.Load();
        _s.Lan.Write("ton-kho-gui", new Dictionary<string, JsonArray?>
        {
            ["DuLieuChinh"] = new JsonArray(new JsonObject { ["Trọng Lượng (KG)"] = "1,200.5" }, new JsonObject { ["Trọng Lượng (KG)"] = "300" }),
            ["DuLieuMeta"] = new JsonArray(new JsonObject { ["data"] = "__JSON__{\"dataDate\":\"21/11/2026\",\"importedAt\":1}" }),
        });
        var r = aa.SyncKhoGui(false)!.Value;
        Assert.True(r.Changed); Assert.Equal(1500.5, aa.D.KhoGui);
        Assert.False(aa.SyncKhoGui(false)!.Value.Changed);           // chưa đổi → không ghi lại
        var re = new AnAnService(() => _s.Lan, _s.Session, _s.Audit); re.Load();
        Assert.Equal(1500.5, re.D.KhoGui);
    }

    // ------------------------------------------------------------------ so với bản WebView2 trên dữ liệu thật (chỉ chạy trên máy có file riêng)

    private const string Seed = "/home/user/oracle/seed.js", RealXlsx = "/home/user/mau-goc/TON KHO AN AN 21.11.xlsx", Oracle = "/home/user/cmp/old-anan/db.json";

    [Fact]
    public void Real_data_matches_old_app_when_available()
    {
        if (!File.Exists(Seed) || !File.Exists(RealXlsx) || !File.Exists(Oracle)) return;
        var seedText = File.ReadAllText(Seed); seedText = seedText[(seedText.IndexOf('=') + 1)..].Trim().TrimEnd(';');
        var byDb = new Dictionary<string, Dictionary<string, JsonArray?>>();
        foreach (var (k, v) in JsonNode.Parse(seedText)!.AsObject())
        {
            var p = k.Split("::"); if (!byDb.TryGetValue(p[0], out var m)) byDb[p[0]] = m = new();
            m[p[1]] = v as JsonArray;
        }
        foreach (var (db, sheets) in byDb) _s.Lan.Write(db, sheets);
        var aa = _s.AnAn; aa.Load();
        var pv = aa.Preview(RealXlsx);
        aa.Commit(pv, keepUndo: false);
        var oracle = JsonNode.Parse(File.ReadAllText(Oracle))!.AsObject();
        var exp = oracle["LoadData"]!.AsArray(); var got = JsonNode.Parse(_s.Lan.Read(AnAnStore.Db, AnAnStore.SLoadData))!.AsArray();
        Assert.Equal(exp.Count, got.Count);
        for (var i = 0; i < exp.Count; i++) Assert.Equal(exp[i]!.ToJsonString(), got[i]!.ToJsonString());
        var snapE = JsonNode.Parse(oracle["LanNapTruoc"]![0]!["data"]!.GetValue<string>()[8..])!["snap"]!.AsArray();
        var snapG = JsonNode.Parse(JsonNode.Parse(_s.Lan.Read(AnAnStore.Db, AnAnStore.SSnap))![0]!["data"]!.GetValue<string>()[8..])!["snap"]!.AsArray();
        Assert.Equal(snapE.Count, snapG.Count);
        var last = aa.History()[0];
        Assert.Equal(3845, last["rows"]!.GetValue<int>()); Assert.Equal(618, last["items"]!.GetValue<int>()); Assert.Equal(1429158.52, last["kg"]!.GetValue<double>());
        Assert.Equal(oracle["DacTinh"]!.ToJsonString(), JsonNode.Parse(_s.Lan.Read(AnAnStore.Db, AnAnStore.SDacTinh))!.ToJsonString());
        var sync = aa.SyncKhoGui(true)!.Value; Assert.Equal(983086.6, sync.Info.Kg);
    }
}
