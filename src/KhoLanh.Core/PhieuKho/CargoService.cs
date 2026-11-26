using KhoLanh.Core.KiemKe;
using System.Text.Json.Nodes;
using KhoLanh.Core.KhangSinh;
using KhoLanh.Core.Models;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;

namespace KhoLanh.Core.PhieuKho;

/// <summary>
/// Cargo list + phiếu xuất kho cho Xuất Thuận Phước / Gia công / Gửi kho (hàng tạm, không LSX; mỗi cont = 1 Cargo + 1 phiếu).
/// Dùng CHUNG dữ liệu với app cũ: database "phieu-nhap-xuat", sheet PNX_Cargo / PNX_CargoGC / PNX_CargoGK (mỗi cont 1 dòng, data = "__JSON__" + JSON), danh mục ở PNX_CargoCfg.
/// Kết quả kháng sinh đọc từ module Kết quả kháng sinh (sheet KhangSinh, khóa "s:batch:…").
/// </summary>
public sealed class CargoService
{
    public const string Db = PhieuKhoService.Db, SheetCfg = "PNX_CargoCfg", PermKey = PhieuKhoService.PermKey;

    private readonly Func<ISheetSource> _src;
    private readonly Session _session;
    private readonly AuditService _audit;
    public CargoService(Func<ISheetSource> source, Session session, AuditService audit) { _src = source; _session = session; _audit = audit; }

    public bool CanView => _session.Can(PermKey, Perm.View);
    public bool CanEdit => _session.Can(PermKey, Perm.Edit);
    public bool CanExport => _session.Can(PermKey, Perm.Export);
    private void RequireView() { if (!CanView) throw new BusinessException("Tài khoản không có quyền xem Phiếu xuất kho."); }
    private void RequireEdit() { if (!CanEdit) throw new BusinessException("Tài khoản không có quyền sửa Cargo list (lập / sửa / xóa cont, danh mục)."); }
    private void Log(string action, string detail) => _audit.Log(_session.Username, "Cargo list: " + action, detail);

    public CargoCfg Cfg { get; private set; } = CargoCore.NormCfg(CargoCore.DefaultCfg());
    public CargoCore.KsIndex Ks { get; private set; } = new();
    public DateTime KsAt { get; private set; }

    private JsonArray Read(string db, string sheet)
    {
        try { return JsonNode.Parse(_src().Read(db, sheet)) as JsonArray ?? new JsonArray(); } catch { return new JsonArray(); }
    }

    /// <summary>Đọc danh mục + kết quả kháng sinh. Gọi khi mở màn hình và khi bấm "Tra lại".</summary>
    public void LoadRefs()
    {
        RequireView();
        Cfg = CargoCore.NormCfg(CargoCore.DefaultCfg());
        var row = Read(Db, SheetCfg).OfType<JsonObject>().FirstOrDefault(o => o["id"]?.ToString() == "cfg");
        if (row is not null)
        {
            var d = row["data"]?.ToString() ?? ""; if (d.StartsWith("__JSON__")) d = d[8..];
            if (CargoCore.CfgFromJson(d) is { } c) Cfg = CargoCore.NormCfg(c);
        }
        Ks = CargoCore.KsFromRows(Read(KhangSinhService.Db, KhangSinhService.Sheet)); KsAt = DateTime.Now;   // app cũ lưu "s:batch:…" / "l:batch:…"
        LoadStock();
    }

    public void SaveCfg(CargoCfg cfg)
    {
        RequireEdit();
        var c = CargoCore.NormCfg(cfg);
        _src().Write(Db, new Dictionary<string, JsonArray?> { [SheetCfg] = new JsonArray(new JsonObject { ["id"] = "cfg", ["capNhat"] = DateTime.UtcNow.ToString("o"), ["data"] = "__JSON__" + CargoCore.CfgToJson(c) }) });
        Cfg = c; Log("Lưu danh mục", $"{c.Loai.Count} loại hàng, {c.Qc.Count} quy cách");
    }

    public List<CargoRec> List(CargoProfile prof)
    {
        RequireView();
        var list = new List<CargoRec>();
        foreach (var o in Read(Db, prof.Sheet).OfType<JsonObject>())
        {
            var d = o["data"]?.ToString() ?? ""; if (d.StartsWith("__JSON__")) d = d[8..];
            if (CargoCore.FromJson(d) is { Id.Length: > 0 } c) list.Add(c);
        }
        return list.OrderByDescending(c => c.Ngay, StringComparer.Ordinal).ThenByDescending(c => c.CapNhat, StringComparer.Ordinal).ToList();
    }

    private void SaveAll(CargoProfile prof, List<CargoRec> list)
    {
        var rows = new JsonArray(list.Select(p => (JsonNode?)new JsonObject
        {
            ["id"] = p.Id, ["ngay"] = p.Ngay, ["soPhieu"] = p.SoPhieu, ["kho"] = p.Kho, ["kien"] = p.Blocks.Sum(b => b.Lines.Sum(CargoCore.LineTotal)), ["capNhat"] = p.CapNhat,
            ["data"] = "__JSON__" + CargoCore.ToJson(p),
        }).ToArray());
        _src().Write(Db, new Dictionary<string, JsonArray?> { [prof.Sheet] = rows });
    }

    /// <summary>Cont mới: số phiếu tự cấp theo tháng / năm của đúng loại.</summary>
    public CargoRec NewRec(CargoProfile prof, IEnumerable<CargoRec> existing)
    {
        var c = new CargoRec { Loai = prof.Loai, Ngay = DateTime.Today.ToString("yyyy-MM-dd") };
        c.SoPhieu = CargoCore.NextSo(existing, c.Ngay, "", prof.Code); c.SoPhieuTuCap = c.SoPhieu;
        if (prof.Gk) { c.KhoNhan = prof.KhoNhan; c.ContSo = CargoCore.ContSoOf(c, prof.Code); c.ContSoTuCap = c.ContSo; }
        return c;
    }

    public CargoRec Save(CargoProfile prof, CargoRec rec)
    {
        RequireEdit();
        var c = CargoCore.Clone(rec); var list = List(prof);
        var isNew = c.Id.Length == 0;
        if (isNew) c.Id = "cg" + DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString("x") + Guid.NewGuid().ToString("N")[..3];
        c.CapNhat = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
        var i = list.FindIndex(x => x.Id == c.Id); if (i >= 0) list[i] = c; else list.Insert(0, c);
        SaveAll(prof, list);
        Log(isNew ? "Tạo cont" : "Sửa cont", $"{prof.Ten}: {c.SoPhieu}");
        return c;
    }

    public void Delete(CargoProfile prof, string id)
    {
        RequireEdit();
        var list = List(prof); var c = list.FirstOrDefault(x => x.Id == id) ?? throw new BusinessException("Không tìm thấy cont.");
        list.Remove(c); SaveAll(prof, list);
        Log("Xóa cont", $"{prof.Ten}: {c.SoPhieu}");
    }

    public static byte[] Template(CargoProfile prof)
    {
        using var s = typeof(CargoService).Assembly.GetManifestResourceStream($"KhoLanh.Core.PhieuKho.mau-{prof.Mau}.xlsx")
            ?? throw new InvalidOperationException("Thiếu file mẫu Cargo " + prof.Mau);
        using var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray();
    }

    public List<CargoStock.Item> Stock { get; private set; } = new();
    /// <summary>Dòng tồn gốc (đủ Đặc tính / Trọng lượng) — "Chọn từ tồn" gộp theo size + đặc tính (1.72).</summary>
    public List<KiemKeCore.Stock> StockRows { get; private set; } = new();
    public List<CargoStock.SizeRow> StockBySize(CargoRec? rec) => CargoStock.BySize(StockRows, Cfg, rec, Ks);
    public string StockInfo { get; private set; } = "";
    /// <summary>Đọc Tồn kho An An (Load Data) để dò tồn theo mã hàng. Không có file tồn → danh sách rỗng.</summary>
    public void LoadStock()
    {
        Stock = new(); StockRows = new(); StockInfo = "";
        try
        {
            var meta = Read("ton-kho-an-an", "LoadDataMeta"); var mj = meta.Count > 0 && meta[0] is JsonObject mo ? JsonNode.Parse((mo["data"]?.ToString() ?? "").Replace("__JSON__", "")) as JsonObject : null;
            var headers = (mj?["headers"] as JsonArray ?? new JsonArray()).Select(x => x?.ToString() ?? "").ToList(); if (headers.Count == 0) return;
            var rows = Read("ton-kho-an-an", "LoadData").OfType<JsonObject>().Select(o => Enumerable.Range(0, headers.Count).Select(i => o["c" + i]?.ToString() ?? "").ToArray()).ToList();
            StockRows = KiemKeCore.MakeStock(headers, rows);
            Stock = CargoStock.Aggregate(StockRows);
            StockInfo = $"Tồn kho An An: {Stock.Count:#,0} mã (file {mj?["fileName"]}, ngày {mj?["dataDate"]})";
        }
        catch { Stock = new(); StockRows = new(); }
    }
    public List<CargoStock.Result> StockCheck(CargoRec rec) => Stock.Count == 0 ? new() : CargoStock.CheckRec(Cfg, rec, Ks, Stock);
    /// <summary>1.88: cảnh báo từng dòng (Size-quy cách sai, không có / hết / vượt tồn An An) — chỉ cảnh báo.</summary>
    public List<CargoStock.LineWarn> LineWarnings(CargoRec rec) => CargoStock.LineWarnings(Cfg, rec, Ks, Stock);

    /// <summary>Đọc từ điển Mã hóa sản phẩm (menu 9 trọng lượng, 10 quy cách đóng thùng) + Danh sách mã hàng.</summary>
    public (CargoSuggest.Dict Dict, List<string> Ma) LoadSuggestRefs()
    {
        var d = new CargoSuggest.Dict();
        foreach (var (k, list) in KhoLanh.Core.TongHop.TongHopService.FixedCatalog()) if (k is 9 or 10) foreach (var e in list) (k == 9 ? d.W : d.Q)[e.Code.Trim().ToUpperInvariant()] = e.Name;
        foreach (var o in Read("tong-hop", "MaHoa").OfType<JsonObject>())
        {
            var t = o["Tổ hợp"]?.ToString(); var c = (o["Mã"]?.ToString() ?? "").Trim().ToUpperInvariant(); if (c.Length == 0) continue;
            if (t == "9") d.W[c] = o["Tên"]?.ToString() ?? ""; else if (t == "10") d.Q[c] = o["Tên"]?.ToString() ?? "";
        }
        var ma = Read("tong-hop", "MaDaTao").OfType<JsonObject>().Select(o => o["Mã"]?.ToString() ?? "").Where(x => x.Length > 0).ToList();
        return (d, ma);
    }

    public sealed record Check(double Kien, List<string> Problems);

    /// <summary>Kiểm tra trước khi xuất: các dòng còn lỗi (thiếu kết quả kháng sinh, quy cách…) chặn xuất để không ra phiếu sai.</summary>
    public Check CheckRec(CargoProfile prof, CargoRec rec)
    {
        if (prof.Gk) { var g = CargoCore.BuildGk(Cfg, rec, Ks); return new Check(g.Kien, g.Problems.Select(p => p.Msg).Distinct().ToList()); }
        var ph = CargoCore.BuildPhieu(Cfg, rec, Ks); return new Check(ph.Kien, ph.Problems.Select(p => p.Msg).Distinct().ToList());
    }

    public void Export(CargoProfile prof, CargoRec rec, string path)
    {
        if (!CanExport) throw new BusinessException("Tài khoản không có quyền xuất file Cargo list.");
        LoadRefs();
        var chk = CheckRec(prof, rec);
        if (chk.Kien <= 0) throw new BusinessException("Chưa có số kiện nào để xuất.");
        if (chk.Problems.Count > 0) throw new BusinessException($"Còn {chk.Problems.Count} lỗi chưa xử lý: {chk.Problems[0]}");
        if (rec.SoPhieu.Trim().Length == 0) throw new BusinessException("Chưa có Số phiếu.");
        var d = prof.Gk ? CargoXlsx.ToDataGk(Cfg, rec, Ks, prof) : CargoXlsx.ToDataTp(Cfg, rec, Ks, prof);
        var bytes = prof.Gk ? CargoXlsx.BuildGkFile(Template(prof), d) : CargoXlsx.BuildTp(Template(prof), d);
        File.WriteAllBytes(path, bytes);
        Log("Xuất Excel", $"{prof.Ten}: {rec.SoPhieu} → {Path.GetFileName(path)}");
    }
}
