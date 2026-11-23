using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KhoLanh.Core.Services;

namespace KhoLanh.Core.Scan;

/// <summary>1 trang tài liệu: ảnh gốc (JPEG) + cách cắt / xoay / chế độ làm sạch.</summary>
public sealed class ScanPage
{
    public byte[] Source = Array.Empty<byte>();   // JPEG ảnh gốc (đã thu về ≤ MaxSide)
    public List<ScanPoint> Quad = ScanCore.DefaultQuad();
    public bool CropOn;
    public int Rot;
    public string Mode = ScanCore.DefaultMode;
    public ScanParams? P;
}

public sealed class ScanDoc
{
    public string Id = "", Name = "";
    public DateTime Created = DateTime.Now, Updated = DateTime.Now;
    public List<ScanPage> Pages = new();
}

public sealed record ScanDocInfo(string Id, string Name, DateTime Updated, int Pages, long Bytes);

/// <summary>
/// Tài liệu Scan lưu RIÊNG từng tài khoản trên máy (thư mục scan/&lt;tài khoản&gt;/&lt;mã&gt;/: meta.json + ảnh gốc từng trang),
/// kèm bản nháp tự lưu ("_nhap") để tắt / mở lại phần mềm vẫn còn trang đang làm. Không đồng bộ LAN (ảnh lớn, dữ liệu cá nhân).
/// </summary>
public sealed class ScanStore
{
    public const string PermKey = "m15", DraftId = "_nhap";
    private readonly string _root;
    private readonly Session _session;
    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public ScanStore(string root, Session session) { _root = root; _session = session; }

    public bool CanUse => _session.Can(PermKey, Models.Perm.Edit);

    private string UserDir
    {
        get
        {
            var u = _session.CurrentUser?.Username ?? "_";
            var safe = new string(u.Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' ? ch : '_').ToArray());
            return Path.Combine(_root, safe.Length > 0 ? safe : "_");
        }
    }

    private string DocDir(string id)
    {
        if (id.Length == 0 || id.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '_' or '-'))) throw new BusinessException("Mã tài liệu không hợp lệ.");
        return Path.Combine(UserDir, id);
    }

    public static string NewId() => "d" + DateTime.Now.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);

    public List<ScanDocInfo> List()
    {
        var outp = new List<ScanDocInfo>();
        if (!Directory.Exists(UserDir)) return outp;
        foreach (var dir in Directory.GetDirectories(UserDir))
        {
            var id = Path.GetFileName(dir); if (id == DraftId) continue;
            try
            {
                var meta = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "meta.json")));
                var bytes = Directory.GetFiles(dir).Sum(f => new FileInfo(f).Length);
                outp.Add(new ScanDocInfo(id, meta?["name"]?.GetValue<string>() ?? id, DateTime.TryParse(meta?["updated"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var up) ? up : Directory.GetLastWriteTime(dir), (meta?["pages"] as JsonArray)?.Count ?? 0, bytes));
            }
            catch { /* thư mục hỏng: bỏ qua */ }
        }
        return outp.OrderByDescending(d => d.Updated).ToList();
    }

    public void Save(ScanDoc doc)
    {
        var dir = DocDir(doc.Id); var tmp = dir + ".tmp";
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        Directory.CreateDirectory(tmp);
        var pages = new JsonArray();
        for (var i = 0; i < doc.Pages.Count; i++)
        {
            var pg = doc.Pages[i]; var file = $"p{i + 1:000}.jpg";
            File.WriteAllBytes(Path.Combine(tmp, file), pg.Source);
            var o = new JsonObject
            {
                ["file"] = file, ["cropOn"] = pg.CropOn, ["rot"] = pg.Rot, ["mode"] = pg.Mode,
                ["quad"] = new JsonArray(pg.Quad.Select(q => (JsonNode)new JsonObject { ["x"] = q.X, ["y"] = q.Y }).ToArray()),
            };
            if (pg.P is { } p) o["p"] = new JsonObject { ["kieu"] = p.Kieu, ["bong"] = p.Bong, ["trang"] = p.Trang, ["dam"] = p.Dam, ["li"] = p.Li, ["gam"] = p.Gam, ["nhat"] = p.Nhat };
            pages.Add(o);
        }
        doc.Updated = DateTime.Now;
        var meta = new JsonObject { ["name"] = doc.Name, ["created"] = doc.Created.ToString("o", CultureInfo.InvariantCulture), ["updated"] = doc.Updated.ToString("o", CultureInfo.InvariantCulture), ["pages"] = pages };
        File.WriteAllText(Path.Combine(tmp, "meta.json"), meta.ToJsonString(Opt), Encoding.UTF8);
        var old = dir + ".old";
        if (Directory.Exists(old)) Directory.Delete(old, true);
        if (Directory.Exists(dir)) Directory.Move(dir, old);
        Directory.Move(tmp, dir);
        if (Directory.Exists(old)) Directory.Delete(old, true);
    }

    public ScanDoc? Load(string id)
    {
        var dir = DocDir(id); var mf = Path.Combine(dir, "meta.json");
        if (!File.Exists(mf)) return null;
        var meta = JsonNode.Parse(File.ReadAllText(mf)) as JsonObject ?? new JsonObject();
        var doc = new ScanDoc { Id = id, Name = meta["name"]?.GetValue<string>() ?? id };
        if (DateTime.TryParse(meta["created"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var cr)) doc.Created = cr;
        if (DateTime.TryParse(meta["updated"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var up)) doc.Updated = up;
        foreach (var n in (meta["pages"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            var file = Path.Combine(dir, Path.GetFileName(n["file"]?.GetValue<string>() ?? ""));
            if (!File.Exists(file)) continue;
            var pg = new ScanPage { Source = File.ReadAllBytes(file), CropOn = n["cropOn"]?.GetValue<bool>() ?? false, Rot = (int)(n["rot"]?.GetValue<double>() ?? 0), Mode = n["mode"]?.GetValue<string>() ?? ScanCore.DefaultMode };
            if (n["quad"] is JsonArray q && q.Count == 4) pg.Quad = q.Select(x => new ScanPoint(x?["x"]?.GetValue<double>() ?? 0, x?["y"]?.GetValue<double>() ?? 0)).ToList();
            if (n["p"] is JsonObject p)
                pg.P = new ScanParams(p["kieu"]?.GetValue<string>() ?? "xam", p["bong"]?.GetValue<double>() ?? 80, p["trang"]?.GetValue<double>() ?? 50, p["dam"]?.GetValue<double>() ?? 50, p["li"]?.GetValue<bool>() ?? false, p["gam"]?.GetValue<double>() ?? 1, p["nhat"]?.GetValue<double>() ?? 0);
            doc.Pages.Add(pg);
        }
        return doc;
    }

    public void Delete(string id) { var dir = DocDir(id); if (Directory.Exists(dir)) Directory.Delete(dir, true); }

    // ---------------- thiết lập riêng tài khoản (chế độ mặc định, khổ PDF…)
    public string? GetSetting(string key)
    {
        try { var f = Path.Combine(UserDir, "thiet-lap.json"); if (!File.Exists(f)) return null; return (JsonNode.Parse(File.ReadAllText(f)) as JsonObject)?[key]?.GetValue<string>(); } catch { return null; }
    }

    public void SetSetting(string key, string value)
    {
        try
        {
            Directory.CreateDirectory(UserDir); var f = Path.Combine(UserDir, "thiet-lap.json");
            var o = File.Exists(f) ? JsonNode.Parse(File.ReadAllText(f)) as JsonObject ?? new JsonObject() : new JsonObject();
            o[key] = value; File.WriteAllText(f, o.ToJsonString(Opt));
        }
        catch { /* bỏ qua */ }
    }
}

/// <summary>Ghi PDF gồm các ảnh JPEG (mỗi ảnh 1 trang) — không cần thư viện ngoài.</summary>
public static class JpegPdf
{
    public sealed record Page(byte[] Jpeg, int W, int H, bool Gray, double PtW = 0, double PtH = 0);

    /// <param name="a4">true = khổ A4 (ảnh căn giữa, xoay khổ theo chiều ảnh); false = trang vừa khít ảnh (0,75 pt / điểm ảnh).</param>
    public static byte[] Build(IEnumerable<Page> pages, bool a4, string title = "")
    {
        var ms = new MemoryStream(); var offs = new List<long>();
        void W(string s) { var b = Encoding.ASCII.GetBytes(s); ms.Write(b, 0, b.Length); }
        string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        var list = pages.ToList(); if (list.Count == 0) throw new BusinessException("Chưa có trang nào.");
        W("%PDF-1.4\n"); ms.Write(new byte[] { 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A });
        // 1 = catalog, 2 = pages, 3 = info, rồi mỗi trang 3 đối tượng: page, image, content
        var n = list.Count; var pageIds = Enumerable.Range(0, n).Select(i => 4 + i * 3).ToList();
        void Obj(int id, Action body) { while (offs.Count < id) offs.Add(0); offs[id - 1] = ms.Position; W(id + " 0 obj\n"); body(); W("\nendobj\n"); }
        Obj(1, () => W("<< /Type /Catalog /Pages 2 0 R >>"));
        Obj(2, () => W("<< /Type /Pages /Kids [" + string.Join(" ", pageIds.Select(i => i + " 0 R")) + "] /Count " + n + " >>"));
        Obj(3, () => W("<< /Producer (Kho Lanh - Scan tai lieu) /Title " + PdfText(title) + " >>"));
        for (var i = 0; i < n; i++)
        {
            var pg = list[i]; int pid = pageIds[i], iid = pid + 1, cid = pid + 2;
            var land = pg.W > pg.H; double pw, ph, w, h, x0, y0;
            if (a4) { pw = land ? 841.89 : 595.28; ph = land ? 595.28 : 841.89; var k = Math.Min(pw / pg.W, ph / pg.H); w = pg.W * k; h = pg.H * k; x0 = (pw - w) / 2; y0 = (ph - h) / 2; }
            else if (pg.PtW > 0 && pg.PtH > 0) { pw = pg.PtW; ph = pg.PtH; w = pw; h = ph; x0 = 0; y0 = 0; }
            else { pw = pg.W * 0.75; ph = pg.H * 0.75; w = pw; h = ph; x0 = 0; y0 = 0; }
            Obj(pid, () => W($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(pw)} {F(ph)}] /Resources << /XObject << /Im0 {iid} 0 R >> >> /Contents {cid} 0 R >>"));
            Obj(iid, () =>
            {
                W($"<< /Type /XObject /Subtype /Image /Width {pg.W} /Height {pg.H} /ColorSpace /{(pg.Gray ? "DeviceGray" : "DeviceRGB")} /BitsPerComponent 8 /Filter /DCTDecode /Length {pg.Jpeg.Length} >>\nstream\n");
                ms.Write(pg.Jpeg, 0, pg.Jpeg.Length); W("\nendstream");
            });
            var content = $"q {F(w)} 0 0 {F(h)} {F(x0)} {F(y0)} cm /Im0 Do Q";
            Obj(cid, () => W($"<< /Length {content.Length} >>\nstream\n{content}\nendstream"));
        }
        var xref = ms.Position;
        W("xref\n0 " + (offs.Count + 1) + "\n0000000000 65535 f \n");
        foreach (var o in offs) W(o.ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n");
        W("trailer\n<< /Size " + (offs.Count + 1) + " /Root 1 0 R /Info 3 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
        return ms.ToArray();
    }

    // chuỗi PDF dạng UTF-16BE có BOM (tiêu đề tiếng Việt)
    private static string PdfText(string s)
    {
        var b = Encoding.BigEndianUnicode.GetBytes(s ?? "");
        return "<FEFF" + Convert.ToHexString(b) + ">";
    }
}
