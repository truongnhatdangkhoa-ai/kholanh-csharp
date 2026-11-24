using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using KhoLanh.Core.Models;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;
using static KhoLanh.Core.SoKho.SoKhoCore;

namespace KhoLanh.Core.SoKho;

/// <summary>
/// Module NXT Bột/Sốt (Sổ kho) — dùng CHUNG database "nxt-bot-sot" với app cũ: SoCai (sổ cái) · SoKhoDinhMuc · SoKhoSanLuong · SoKhoCauHinh · _SoKhoLichSu.
/// Mỗi lần ghi = 1 THAO TÁC: đọc lại sổ mới nhất, áp thêm / sửa / xóa theo id (không đè dòng máy khác vừa ghi), ghi kèm lịch sử ảnh trước / sau để hoàn tác.
/// </summary>
public sealed class SoKhoService
{
    public const string PermKey = "m3sk";
    private readonly Func<ISheetSource> _src;
    private readonly Session _session;
    private readonly AuditService _audit;
    private readonly object _lock = new();
    public Func<DateTime> Now { get; set; } = () => DateTime.Now;

    public SoKhoService(Func<ISheetSource> src, Session session, AuditService audit) { _src = src; _session = session; _audit = audit; }

    public bool CanView => _session.Can(PermKey, Perm.View);
    public bool CanEdit => _session.Can(PermKey, Perm.Edit);
    public bool CanExport => _session.Can(PermKey, Perm.Export);
    private void RequireEdit() { if (!CanEdit) throw new BusinessException("Tài khoản không có quyền sửa NXT Bột/Sốt."); }

    public List<SkLine> Lines { get; private set; } = new();
    public int DroppedDup { get; private set; }
    public List<Norm> Norms { get; private set; } = new();
    public List<Output> Outputs { get; private set; } = new();
    public List<HistRow> Hist { get; private set; } = new();
    /// <summary>Cấu hình: dung sai định mức (%), báo hạn dùng trước (ngày), tồn tối thiểu theo mã.</summary>
    public double Tol { get; set; } = 3;
    public int HsdDays { get; set; } = 30;
    public Dictionary<string, double> MinStock { get; set; } = new();

    private JsonArray Read(string sheet) { try { return JsonNode.Parse(_src().Read(Db, sheet)) as JsonArray ?? new JsonArray(); } catch { return new JsonArray(); } }
    private static double? N(JsonNode? n) => ParseNum(n) is { } d && !double.IsNaN(d) ? d : null;

    public void Load()
    {
        if (!CanView) throw new BusinessException("Tài khoản không có quyền xem NXT Bột/Sốt.");
        var (lines, dropped) = DedupeById(Read(SLedger).OfType<JsonObject>().Select(NormLine));
        Lines = lines; DroppedDup = dropped.Count;
        Norms = Read(SNorms).OfType<JsonObject>().Select(o => new Norm(S(o["id"]), S(o["hopDong"]), S(o["maHang"]), S(o["size"]), N(o["dinhMuc"]), S(o["donVi"]), S(o["ghiChu"]))).ToList();
        Templates = Read(STemplates).OfType<JsonObject>().Select(SkTemplate.FromStore).ToList(); if (Templates.Count == 0) Templates = new() { SkTemplate.Default() };
        Outputs = Read(SOutputs).OfType<JsonObject>().Select(o => new Output(S(o["id"]), S(o["hopDong"]), N(o["thanhPham"]), S(o["khach"]), S(o["ghiChu"]))).ToList();
        var cfg = Read(SConfig).OfType<JsonObject>().ToDictionary(o => S(o["key"]), o => S(o["value"]));
        Tol = double.TryParse(cfg.GetValueOrDefault("tol"), NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ? t : 3;
        HsdDays = int.TryParse(cfg.GetValueOrDefault("hsdDays"), out var h) ? h : 30;
        MinStock = new();
        try { if (JsonNode.Parse(cfg.GetValueOrDefault("min") is { Length: > 0 } m ? m : "{}") is JsonObject mo) foreach (var (k, v) in mo) if (N(v) is { } d) MinStock[k] = d; } catch { }
        var cut = Now().ToUniversalTime().AddDays(-HistDays);
        Hist = Read(SHist).OfType<JsonObject>().Select(HistFromStore).Where(r => !DateTime.TryParse(r.Luc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) || d.ToUniversalTime() >= cut).ToList();
    }

    // ------------------------------------------------------------------ ghi sổ (1 thao tác)

    public sealed record CommitResult(int Added, int Removed, int Updated, string OpId);

    /// <summary>Thêm / xóa / sửa dòng sổ trong 1 thao tác (đọc sổ mới nhất trước), ghi kèm lịch sử để hoàn tác.</summary>
    public CommitResult Commit(IEnumerable<SkLine> add, IEnumerable<string> removeIds, IEnumerable<SkLine> update, string loai, string moTa)
    {
        RequireEdit();
        lock (_lock)
        {
            var cur = Read(SLedger).OfType<JsonObject>().ToList();
            var byId = new Dictionary<string, int>(); for (var i = 0; i < cur.Count; i++) if (S(cur[i]["id"]) is { Length: > 0 } id) byId.TryAdd(id, i);
            var added = new List<JsonObject>(); var removed = new List<JsonObject>(); var updated = new List<(JsonObject, JsonObject)>();
            var rm = removeIds.ToHashSet();
            foreach (var u in update)
            {
                if (!byId.TryGetValue(u.Id, out var i)) continue;
                var after = ToStore(u); if (RowSig(after) == RowSig(cur[i])) continue;
                updated.Add((after, cur[i])); cur[i] = after;
            }
            var keep = new List<JsonObject>();
            foreach (var r in cur) { if (rm.Contains(S(r["id"]))) removed.Add(r); else keep.Add(r); }
            foreach (var a in add) { var o = ToStore(a); keep.Add(o); added.Add(o); }
            if (added.Count + removed.Count + updated.Count == 0) return new CommitResult(0, 0, 0, "");
            var opId = "op" + NewId(); var now = Now().ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
            var hist = Read(SHist); foreach (var h in HistRows(opId, _session.Username, loai, moTa, added, removed, updated, now)) hist.Add(HistToStore(h));
            _src().Write(Db, new Dictionary<string, JsonArray?> { [SLedger] = new JsonArray(keep.Select(x => (JsonNode)x.DeepClone()).ToArray()), [SHist] = hist });
            _audit.Log(_session.Username, "NXT Bột/Sốt", $"{moTa} (+{added.Count} / sửa {updated.Count} / xóa {removed.Count})");
            Load();
            return new CommitResult(added.Count, removed.Count, updated.Count, opId);
        }
    }

    /// <summary>Kiểm tra phiếu trước khi lưu: lỗi từng dòng (chặn), nhắc (không chặn), định mức LSX, xuất chỉ định lô bị tràn.</summary>
    public sealed record DraftCheck(List<(int Row, Check Check)> Rows, List<DmCheckRow> DinhMuc, List<(SkLine Line, Spill Spill)> Spills)
    {
        public bool HasError => Rows.Any(r => r.Check.Level == "err");
    }
    public DraftCheck CheckDraft(IList<SkLine> draft, IEnumerable<string>? replacingIds = null)
    {
        var rep = replacingIds?.ToHashSet() ?? new();
        var baseLines = Lines.Where(l => !rep.Contains(l.Id)).ToList(); var known = KnownSets(Lines);
        var rows = draft.Select((l, i) => (i, Validate(l, known))).ToList();
        return new DraftCheck(rows, DinhMucCheck(baseLines, draft, Norms, Outputs, Tol).Rows, XuatLoCheck(baseLines, draft));
    }

    /// <summary>Lưu phiếu (nhập / xuất / chuyển): <paramref name="replacingIds"/> = các dòng cũ của phiếu đang sửa.</summary>
    public CommitResult SavePhieu(IList<SkLine> draft, IEnumerable<string>? replacingIds, string loai, string ct)
    {
        var rep = replacingIds?.ToList() ?? new();
        var chk = CheckDraft(draft, rep);
        if (chk.HasError) throw new BusinessException("Phiếu còn lỗi: " + string.Join("; ", chk.Rows.Where(r => r.Check.Level == "err").Take(5).Select(r => $"dòng {r.Row + 1}: {string.Join(", ", r.Check.Errs)}")));
        var luc = Now().ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
        var keepIds = draft.Select(d => d.Id).ToHashSet();
        var upd = draft.Where(d => rep.Contains(d.Id)).ToList(); var add = draft.Where(d => !rep.Contains(d.Id)).ToList();
        foreach (var l in draft) { if (l.Nguoi.Length == 0) l.Nguoi = _session.Username; if (l.Luc.Length == 0) l.Luc = luc; }
        return Commit(add, rep.Where(id => !keepIds.Contains(id)), upd, loai, $"{(rep.Count > 0 ? "Sửa" : "Lưu")} phiếu {LoaiTen.GetValueOrDefault(loai, loai).ToLower()} {ct}");
    }

    public CommitResult DeleteLines(IEnumerable<string> ids, string moTa) => Commit(Array.Empty<SkLine>(), ids, Array.Empty<SkLine>(), "xoa", moTa);

    public (CommitResult? R, int Dropped, bool Exists) Snapshot(string date)
    {
        RequireEdit();
        var (ls, dropped, exists) = SoKhoCore.Snapshot(Lines, date, _session.Username);
        if (exists || ls.Count == 0) return (null, dropped, exists);
        return (Commit(ls, Array.Empty<string>(), Array.Empty<SkLine>(), "chot", $"Chốt tồn ngày {Dmy(date)} ({ls.Count} lô)"), dropped, false);
    }

    public CommitResult ApplyKiemKe(IList<SkLine> adj, string ngay) => Commit(adj, Array.Empty<string>(), Array.Empty<SkLine>(), "kiemke", $"Kiểm kê ngày {Dmy(ngay)}: {adj.Count} dòng điều chỉnh");

    /// <summary>Hoàn tác 1 thao tác (chỉ dòng chưa bị sửa tiếp). Trả số dòng đã đưa về + danh sách xung đột.</summary>
    public (int Done, List<(string Id, string Why)> Conflicts) Undo(string opId)
    {
        RequireEdit();
        lock (_lock)
        {
            var hist = Read(SHist).OfType<JsonObject>().ToList();
            var ops = hist.Select(HistFromStore).Where(h => h.OpId == opId).ToList();
            if (ops.Count == 0) throw new BusinessException("Không còn thao tác này trong lịch sử.");
            if (ops.All(h => h.Undone.Length > 0)) throw new BusinessException("Thao tác này đã được hoàn tác.");
            var cur = Read(SLedger).OfType<JsonObject>().ToList();
            var plan = UndoPlan(ops, cur);
            var rm = plan.Remove.ToHashSet(); var upd = plan.Update.ToDictionary(x => S(x["id"]));
            var before = cur.ToDictionary(x => S(x["id"]), x => x, StringComparer.Ordinal);
            var next = cur.Where(r => !rm.Contains(S(r["id"]))).Select(r => upd.TryGetValue(S(r["id"]), out var b) ? b : r).ToList();
            next.AddRange(plan.Restore);
            var n = plan.Remove.Count + plan.Restore.Count + plan.Update.Count;
            var stamp = Now().ToUniversalTime().ToString("o");
            foreach (var h in hist) if (S(h["opId"]) == opId) h["undone"] = stamp;
            // hoàn tác cũng là 1 thao tác (hoàn tác ngược lại được)
            var newOp = "op" + NewId();
            foreach (var r in HistRows(newOp, _session.Username, "hoantac", "Hoàn tác: " + ops[0].MoTa, plan.Restore, plan.Remove.Where(before.ContainsKey).Select(id => before[id]),
                         plan.Update.Select(b => (b, before.GetValueOrDefault(S(b["id"])) ?? b)), stamp)) hist.Add(HistToStore(r));
            _src().Write(Db, new Dictionary<string, JsonArray?> { [SLedger] = new JsonArray(next.Select(x => (JsonNode)x.DeepClone()).ToArray()), [SHist] = new JsonArray(hist.Select(x => (JsonNode)x.DeepClone()).ToArray()) });
            _audit.Log(_session.Username, "NXT Bột/Sốt", $"Hoàn tác {ops[0].MoTa}: {n} dòng" + (plan.Conflicts.Count > 0 ? $", {plan.Conflicts.Count} xung đột giữ nguyên" : ""));
            Load();
            return (n, plan.Conflicts);
        }
    }

    // ------------------------------------------------------------------ định mức / sản lượng / cấu hình

    /// <summary>Mẫu cột (sheet SoKhoMau, dùng chung định dạng app cũ).</summary>
    public List<SkTemplate> Templates { get; private set; } = new() { SkTemplate.Default() };
    public SkTemplate TemplateFor(string loai) => SkTemplate.For(Templates, loai);
    public void SaveTemplates(IEnumerable<SkTemplate> list)
    {
        if (!CanEdit) throw new BusinessException("Bạn chỉ có quyền xem.");
        var l = list.ToList(); if (l.Count == 0) l.Add(SkTemplate.Default());
        _src().Write(Db, new Dictionary<string, JsonArray?> { [STemplates] = new JsonArray(l.Select(t => (JsonNode)t.ToStore()).ToArray()) });
        Templates = l;
    }

    public void SaveNorms(IEnumerable<Norm> rows)
    {
        RequireEdit();
        _src().Write(Db, new Dictionary<string, JsonArray?> { [SNorms] = new JsonArray(rows.Where(r => r.MaHang.Trim().Length > 0).Select(r => (JsonNode)new JsonObject
        { ["id"] = r.Id.Length > 0 ? r.Id : NewId(), ["hopDong"] = r.HopDong.Trim(), ["maHang"] = r.MaHang.Trim(), ["size"] = r.Size.Trim(), ["dinhMuc"] = r.DinhMuc is { } d ? d : "", ["donVi"] = Unit(r.DonVi).Key, ["ghiChu"] = r.GhiChu }).ToArray()) });
        _audit.Log(_session.Username, "NXT Bột/Sốt", "Lưu định mức"); Load();
    }
    public void SaveOutputs(IEnumerable<Output> rows)
    {
        RequireEdit();
        _src().Write(Db, new Dictionary<string, JsonArray?> { [SOutputs] = new JsonArray(rows.Where(r => r.HopDong.Trim().Length > 0).Select(r => (JsonNode)new JsonObject
        { ["id"] = r.Id.Length > 0 ? r.Id : NewId(), ["hopDong"] = r.HopDong.Trim(), ["thanhPham"] = r.ThanhPham is { } d ? d : "", ["khach"] = r.Khach.Trim(), ["ghiChu"] = r.GhiChu }).ToArray()) });
        _audit.Log(_session.Username, "NXT Bột/Sốt", "Lưu sản lượng thành phẩm"); Load();
    }
    public void SaveConfig(double tol, int hsdDays, Dictionary<string, double> min)
    {
        RequireEdit();
        var mo = new JsonObject(); foreach (var (k, v) in min) mo[k] = v;
        _src().Write(Db, new Dictionary<string, JsonArray?> { [SConfig] = new JsonArray(
            new JsonObject { ["key"] = "tol", ["value"] = tol.ToString(CultureInfo.InvariantCulture) },
            new JsonObject { ["key"] = "hsdDays", ["value"] = hsdDays.ToString() },
            new JsonObject { ["key"] = "min", ["value"] = mo.ToJsonString() }) });
        Load();
    }

    /// <summary>Mã còn tồn dưới mức tối thiểu đã đặt.</summary>
    public List<(string Ma, double Sl, double Net, double Min)> BelowMin()
    {
        if (MinStock.Count == 0) return new();
        var st = Stock(Lines, null, "maHang").ToDictionary(r => r["maHang"]);
        return MinStock.Select(m => (m.Key, st.TryGetValue(m.Key, out var r) ? r.Sl : 0, st.TryGetValue(m.Key, out var r2) ? r2.Net : 0, m.Value)).Where(x => x.Item2 < x.Value).ToList();
    }

    // ------------------------------------------------------------------ nạp / xuất Excel

    private static readonly (string Key, string[] Names)[] ImportCols =
    {
        ("ngay", new[] { "ngay", "ngay phat sinh", "ngay chung tu" }), ("loai", new[] { "loai", "loai phieu", "nghiep vu" }), ("chungTu", new[] { "chung tu", "so chung tu", "so phieu" }),
        ("viTri", new[] { "vi tri", "vi tri di" }), ("viTriDen", new[] { "vi tri den" }), ("maHang", new[] { "ma hang", "ma" }), ("size", new[] { "size", "quy cach" }),
        ("soLuong", new[] { "so luong", "so luong (bao)", "sl", "bao" }), ("netDv", new[] { "net (kg/don vi)", "net/don vi", "kg/bao", "net don vi" }), ("net", new[] { "tong (kg)", "net (kg)", "tong kg", "khoi luong", "net" }),
        ("hopDong", new[] { "hop dong", "lsx", "lsx / hop dong" }), ("hanDung", new[] { "han dung", "hsd", "han su dung" }), ("ngayNhap", new[] { "ngay nhap", "nhap" }), ("ghiChu", new[] { "ghi chu" }),
    };
    private static readonly Dictionary<string, string> LoaiAlias = new() { ["ton dau"] = "tondau", ["tondau"] = "tondau", ["nhap"] = "nhap", ["nhap kho"] = "nhap", ["xuat"] = "xuat", ["xuat kho"] = "xuat", ["chuyen"] = "chuyen", ["chuyen vi tri"] = "chuyen", ["dieu chinh"] = "dieuchinh", ["dieuchinh"] = "dieuchinh" };

    /// <summary>Đọc file Excel (dòng tiêu đề có "Mã hàng") → các dòng sổ (chưa lưu). Thiếu cột Loại thì dùng <paramref name="loaiMacDinh"/>.</summary>
    public static List<SkLine> ReadExcel(string path, string loaiMacDinh)
    {
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheets.First(); var used = ws.RangeUsed(); if (used is null) return new();
        var r0 = used.FirstRow().RowNumber(); var rN = used.LastRow().RowNumber(); var cN = used.LastColumn().ColumnNumber();
        int head = -1; var map = new Dictionary<int, string>();
        for (var r = r0; r <= Math.Min(rN, r0 + 15) && head < 0; r++)
        {
            var cand = new Dictionary<int, string>();
            for (var c = 1; c <= cN; c++)
            {
                var h = TroLy.TroLyMaHang.Fold(ws.Cell(r, c).GetString()).Trim();
                foreach (var (k, names) in ImportCols) if (!cand.ContainsValue(k) && names.Contains(h)) { cand[c] = k; break; }
            }
            if (cand.ContainsValue("maHang")) { head = r; map = cand; }
        }
        if (head < 0) throw new BusinessException("Không thấy dòng tiêu đề có cột \"Mã hàng\" trong 15 dòng đầu.");
        var o = new List<SkLine>();
        for (var r = head + 1; r <= rN; r++)
        {
            var raw = new JsonObject();
            foreach (var (c, k) in map)
            {
                var cell = ws.Cell(r, c);
                raw[k] = cell.DataType == XLDataType.Number ? JsonValue.Create(cell.GetDouble()) : cell.DataType == XLDataType.DateTime ? JsonValue.Create(Iso(cell.GetDateTime())) : JsonValue.Create(cell.GetString().Trim());
            }
            if (S(raw["maHang"]).Length == 0) continue;
            raw["loai"] = LoaiAlias.GetValueOrDefault(TroLy.TroLyMaHang.Fold(S(raw["loai"])).Trim(), loaiMacDinh);
            raw["nguon"] = "excel";
            o.Add(NormLine(raw));
        }
        return o;
    }

    public CommitResult ImportExcel(string path, string loaiMacDinh)
    {
        var ls = ReadExcel(path, loaiMacDinh);
        var known = KnownSets(Lines); var bad = ls.Select((l, i) => (i, Validate(l, known))).Where(x => x.Item2.Level == "err").ToList();
        if (bad.Count > 0) throw new BusinessException($"File có {bad.Count} dòng lỗi — chưa nạp gì. Ví dụ: " + string.Join("; ", bad.Take(5).Select(b => $"dòng {b.i + 1}: {string.Join(", ", b.Item2.Errs)}")));
        return Commit(ls, Array.Empty<string>(), Array.Empty<SkLine>(), "nap", $"Nạp Excel {Path.GetFileName(path)}: {ls.Count} dòng");
    }

    public void ExportLedger(IEnumerable<SkLine> lines, string path)
    {
        using var wb = new XLWorkbook(); var ws = wb.AddWorksheet("So cai");
        string[] h = { "Ngày", "Loại", "Chứng từ", "Vị trí", "Vị trí đến", "Mã hàng", "Size", "Số lượng", "Net (kg/đv)", "Tổng (kg)", "Hợp đồng / LSX", "Hạn dùng", "Ngày nhập", "Ghi chú", "Nguồn", "Người" };
        for (var i = 0; i < h.Length; i++) ws.Cell(1, i + 1).Value = h[i];
        var r = 2;
        foreach (var l in lines)
        {
            object?[] v = { Dmy(l.Ngay), LoaiTen.GetValueOrDefault(l.Loai, l.Loai), l.ChungTu, l.ViTri, l.ViTriDen, l.MaHang, l.Size, l.SoLuong, l.NetDv, l.Net, l.HopDong, Dmy(l.HanDung), Dmy(l.NgayNhap), l.GhiChu, l.Nguon, l.Nguoi };
            for (var i = 0; i < v.Length; i++) ws.Cell(r, i + 1).Value = v[i] switch { double d => d, null => "", var x => x.ToString() };
            r++;
        }
        Finish(ws, h.Length); wb.SaveAs(path);
    }

    /// <summary>Xuất bảng bất kỳ (tiêu đề + dòng) — dùng cho Tồn kho / NXT / Định mức.</summary>
    public static void ExportTable(string sheet, IList<string> head, IEnumerable<object?[]> rows, string path)
    {
        using var wb = new XLWorkbook(); var ws = wb.AddWorksheet(sheet.Length > 31 ? sheet[..31] : sheet);
        for (var i = 0; i < head.Count; i++) ws.Cell(1, i + 1).Value = head[i];
        var r = 2;
        foreach (var v in rows) { for (var i = 0; i < v.Length; i++) ws.Cell(r, i + 1).Value = v[i] switch { double d => d, int n => n, null => "", var x => x.ToString() }; r++; }
        Finish(ws, head.Count); wb.SaveAs(path);
    }
    private static void Finish(IXLWorksheet ws, int cols)
    {
        var hr = ws.Range(1, 1, 1, cols); hr.Style.Font.Bold = true; hr.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCE6F1");
        ws.RangeUsed()?.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetInsideBorder(XLBorderStyleValues.Thin);
        ws.SheetView.FreezeRows(1); ws.Columns().AdjustToContents(8, 60);
    }

    /// <summary>Phiếu in (HTML A4 ngang) theo 1 số chứng từ — mở bằng trình duyệt để In / Lưu PDF.</summary>
    public static string PhieuHtml(IList<SkLine> ls, string ct, string user)
    {
        static string E(string? v) => System.Net.WebUtility.HtmlEncode(v ?? "");
        static string F(double? n, int d = 2) => n is { } x ? x.ToString("#,0." + new string('#', d), CultureInfo.InvariantCulture) : "";
        var tt = new Dictionary<string, string> { ["nhap"] = "PHIẾU NHẬP KHO", ["xuat"] = "PHIẾU XUẤT KHO", ["chuyen"] = "PHIẾU CHUYỂN VỊ TRÍ", ["dieuchinh"] = "PHIẾU ĐIỀU CHỈNH", ["tondau"] = "PHIẾU TỒN ĐẦU" };
        var ks = ls.Select(l => l.Loai).Distinct().ToList(); var title = ks.Count == 1 ? tt.GetValueOrDefault(ks[0], "PHIẾU") : "PHIẾU NHẬP / XUẤT KHO";
        var ngay = ls.Select(l => l.Ngay).Where(x => x.Length > 0).DefaultIfEmpty("").Min() ?? ""; var hd = string.Join(", ", ls.Select(l => l.HopDong).Where(x => x.Length > 0).Distinct());
        var rows = string.Concat(ls.Select((l, i) => $"<tr><td class=c>{i + 1}</td><td>{E(l.MaHang)}</td><td>{E(l.Size)}</td><td>{E(l.ViTri)}{(l.ViTriDen.Length > 0 ? " → " + E(l.ViTriDen) : "")}</td><td class=r>{F(l.SoLuong)}</td><td class=r>{F(l.NetDv)}</td><td class=r>{F(l.Net)}</td><td>{E(Dmy(l.HanDung))}</td><td>{E(l.HopDong)}</td><td>{E(l.GhiChu)}</td></tr>"));
        return "<!doctype html><html lang=vi><head><meta charset=utf-8><title>" + E(title + " " + ct) + "</title><style>@page{size:A4 landscape;margin:12mm}body{font:13px Segoe UI,Arial,sans-serif;margin:0}h1{text-align:center;font-size:20px;margin:0 0 6px}.meta{display:flex;flex-wrap:wrap;gap:4px 28px;margin:8px 0 12px}table{border-collapse:collapse;width:100%}th,td{border:1px solid #000;padding:4px 6px}th{background:#eee}.c{text-align:center}.r{text-align:right}tfoot td{font-weight:700}.sig{display:flex;justify-content:space-around;margin-top:36px;text-align:center}.sig div{width:22%}.sig small{display:block;color:#444}</style></head><body>"
            + $"<h1>{E(title)}</h1><div class=meta><div><b>Số chứng từ:</b> {E(ct)}</div><div><b>Ngày:</b> {E(Dmy(ngay))}</div>" + (hd.Length > 0 ? $"<div><b>Hợp đồng / LSX:</b> {E(hd)}</div>" : "") + "<div><b>Kho:</b> NXT Bột / Sốt</div></div>"
            + "<table><thead><tr><th>STT</th><th>Mã hàng</th><th>Size</th><th>Vị trí</th><th>SL (bao)</th><th>Net/đv</th><th>Tổng (kg)</th><th>Hạn dùng</th><th>Hợp đồng</th><th>Ghi chú</th></tr></thead><tbody>" + rows
            + $"</tbody><tfoot><tr><td colspan=4 class=r>Tổng cộng</td><td class=r>{F(ls.Sum(l => l.SoLuong ?? 0))}</td><td></td><td class=r>{F(ls.Sum(l => l.Net ?? 0))}</td><td colspan=3></td></tr></tfoot></table>"
            + $"<div class=sig><div><b>Người lập phiếu</b><small>(ký, ghi rõ họ tên)</small><br><br>{E(user)}</div><div><b>Thủ kho</b><small>(ký, ghi rõ họ tên)</small></div><div><b>Người giao / nhận</b><small>(ký, ghi rõ họ tên)</small></div><div><b>Kế toán</b><small>(ký, ghi rõ họ tên)</small></div></div></body></html>";
    }
}
