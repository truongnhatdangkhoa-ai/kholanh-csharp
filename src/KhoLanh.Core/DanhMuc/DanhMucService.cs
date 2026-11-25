using System.Text.Json.Nodes;
using ClosedXML.Excel;
using KhoLanh.Core.Models;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;
using KhoLanh.Core.TongHop;
using static KhoLanh.Core.DanhMuc.DanhMucMa;

namespace KhoLanh.Core.DanhMuc;

/// <summary>
/// DANH MỤC MÃ HÀNG DÙNG CHUNG: gốc là "Danh sách mã hàng" của Tổng hợp (db tong-hop, sheet MaDaTao: STT | Mã | Ghi chú | Đủ mã) —
/// sửa ở đây là sửa đúng danh sách đó (ghi kèm M8_Meta như app cũ) rồi cập nhật luôn Tổng hợp đang nạp để không bị ghi đè.
/// Đổi mã ở đây KHÔNG sửa mã trong tồn kho / phiếu cũ — chỉ báo nơi đang dùng.
/// </summary>
public sealed class DanhMucService
{
    public const string PermKey = "m3dm", Sheet = "MaDaTao";

    public sealed class Row { public string Id = ""; public string Code = ""; public string Note = ""; }

    public List<Row> Rows { get; private set; } = new();
    public SegMap Seg { get; private set; } = new();
    public Checks Chk { get; private set; } = new();
    public Dictionary<string, Use> UseMap { get; private set; } = new();
    public List<string> Errors { get; } = new();

    private readonly Func<ISheetSource> _src;
    private readonly Session _session;
    private readonly AuditService _audit;
    private readonly Func<TongHopService?> _th;
    private int _seq;

    public DanhMucService(Func<ISheetSource> source, Session session, AuditService audit, Func<TongHopService?> tongHop)
    { _src = source; _session = session; _audit = audit; _th = tongHop; }

    public bool CanEdit => _session.Can(PermKey, Perm.Edit);
    public bool CanExport => _session.Can(PermKey, Perm.Export);
    public string NewId() => "n" + DateTime.UtcNow.Ticks.ToString("x") + (++_seq).ToString("x");

    private JsonArray Read(string db, string sheet)
    {
        try { return JsonNode.Parse(_src().Read(db, sheet)) as JsonArray ?? new JsonArray(); }
        catch (Exception e) { Errors.Add($"{db} / {sheet}: {e.Message}"); return new JsonArray(); }
    }

    /// <summary>Nạp danh mục + Mã hóa sản phẩm + đếm nơi đang dùng mã.</summary>
    public void Load()
    {
        if (!_session.Can(PermKey)) throw new BusinessException("Tài khoản không có quyền xem Danh mục mã hàng.");
        Errors.Clear();
        Rows = Read(TongHopService.Db, Sheet).OfType<JsonObject>().Select((r, i) => new Row { Id = "m" + i, Code = NormCode(Str(r["Mã"])), Note = r["Ghi chú"] is null ? "" : Str(r["Ghi chú"]) }).ToList();
        var fixed1 = TongHopService.FixedCatalog().TryGetValue(1, out var l) ? l.Select(e => (e.Code, e.Name)) : Enumerable.Empty<(string, string)>();
        Seg = SegMap.Build(fixed1, Read(TongHopService.Db, "MaHoa"));
        UseMap = CountUse(UseSources.Select(u => (u.Tag, (JsonArray?)Read(u.Db, u.Sheet))));
        Recheck();
    }
    public void Recheck() => Chk = Check(Seg, Rows.Select(r => r.Code));

    public List<Piece> DecodeOf(string code) => Decode(Seg, code);
    public string NoteFor(string code) => NoteOf(Seg, code);
    public Status? StatusFor(string code) => StatusOf(Seg, Chk, code);
    public string UseOf(string code) => UseText(UseMap, code);
    /// <summary>Các bảng ảnh hưởng số "Cần xử lý": danh mục, Mã hóa sản phẩm, các nguồn đang dùng mã.</summary>
    public static IEnumerable<(string Db, string Sheet)> WatchedSheets =>
        new[] { (TongHopService.Db, Sheet), (TongHopService.Db, "MaHoa") }.Concat(UseSources.Select(u => (u.Db, u.Sheet)));
    /// <summary>Dấu phiên bản của <see cref="WatchedSheets"/> (từ danh sách db / sheet / phiên bản) — đổi thì cần tính lại số "Cần xử lý".</summary>
    public static string Fingerprint(IEnumerable<(string DbKey, string Sheet, long Version)> versions)
    {
        var w = WatchedSheets.Select(x => x.Db + "/" + x.Sheet).ToHashSet();
        return string.Join(";", versions.Where(v => w.Contains(v.DbKey + "/" + v.Sheet)).Select(v => v.DbKey + "/" + v.Sheet + "=" + v.Version).OrderBy(x => x, StringComparer.Ordinal));
    }
    /// <summary>Số "Cần xử lý" (mã chưa có trong danh mục + mã trùng + nghi gõ sai) — tính ngầm cho thanh menu.</summary>
    public int XlCount() { Load(); return XlNow().Count; }

    public Xl XlNow() => XlData(Seg, Chk, UseMap, Rows.Select(r => r.Code).ToList());
    public bool HasMissingSeg(string code) => code.Length == Len && Decode(Seg, code).Any(x => !x.Ok);

    /// <summary>Lọc như app cũ: ô tìm (không dấu, mã + diễn giải) + bộ lọc all / trung / sai / doan; tối đa <paramref name="cap"/> dòng.</summary>
    public List<Row> Visible(string q, string f, int cap = 2000)
    {
        var fq = TimNhanh.Gs.Norm(q.Trim()); var o = new List<Row>();
        foreach (var r in Rows)
        {
            if (o.Count >= cap) break;
            if (fq.Length > 0 && !TimNhanh.Gs.Norm(r.Code + " " + r.Note).Contains(fq, StringComparison.Ordinal)) continue;
            if (f == "trung" && Chk.DupOf(r.Code) < 2) continue;
            if (f == "sai" && !(Chk.Near.TryGetValue(r.Code, out var nr) && nr.Count > 0)) continue;
            if (f == "doan" && !HasMissingSeg(r.Code)) continue;
            o.Add(r);
        }
        return o;
    }

    /// <summary>
    /// Áp thay đổi từ lưới: <paramref name="edits"/> = (id hoặc null cho dòng mới, mã, diễn giải, id dòng đứng trước trên lưới), <paramref name="delete"/> = id cần xóa.
    /// Dòng mới chèn ngay sau dòng đứng trước nó (không có → cuối danh mục); dòng mới để trống diễn giải thì tự dựng từ mã.
    /// </summary>
    public int Apply(IEnumerable<(string? Id, string Code, string Note, string? After)> edits, IEnumerable<string> delete, string moTa)
    {
        if (!CanEdit) throw new BusinessException("Tài khoản không có quyền sửa Danh mục mã hàng.");
        var byId = Rows.ToDictionary(r => r.Id); var n = 0;
        foreach (var (id, code0, note, after) in edits)
        {
            var code = NormCode(code0); if (code.Length == 0) continue;
            if (id is not null && byId.TryGetValue(id, out var r)) { if (r.Code == code && r.Note == note) continue; r.Code = code; r.Note = note; n++; continue; }
            var nr = new Row { Id = id ?? NewId(), Code = code, Note = note.Length > 0 ? note : NoteOf(Seg, code) };
            var at = after is not null && byId.TryGetValue(after, out var prev) ? Rows.IndexOf(prev) + 1 : Rows.Count;
            Rows.Insert(at, nr); byId[nr.Id] = nr; n++;
        }
        var del = new HashSet<string>(delete);
        if (del.Count > 0) { var before = Rows.Count; Rows = Rows.Where(r => !del.Contains(r.Id)).ToList(); n += before - Rows.Count; }
        if (n > 0) Write(moTa, n);
        return n;
    }

    /// <summary>Thêm các mã đang dùng mà chưa có trong danh mục (diễn giải tự dựng).</summary>
    public int AddMissing(IEnumerable<string> codes)
    {
        if (!CanEdit) throw new BusinessException("Tài khoản không có quyền sửa Danh mục mã hàng.");
        var have = new HashSet<string>(Rows.Select(r => r.Code)); var n = 0;
        foreach (var c0 in codes) { var c = NormCode(c0); if (c.Length == 0 || !have.Add(c)) continue; Rows.Add(new Row { Id = NewId(), Code = c, Note = NoteOf(Seg, c) }); n++; }
        if (n > 0) Write($"Thêm {n} mã đang dùng chưa có trong danh mục", n);
        return n;
    }

    private void Write(string moTa, int n)
    {
        var data = new JsonArray(Rows.Select((r, i) => (JsonNode)new JsonObject { ["STT"] = i + 1, ["Mã"] = r.Code, ["Ghi chú"] = r.Note, ["Đủ mã"] = r.Code.Length == Len ? 1 : 0 }).ToArray());
        var meta = new JsonArray(new JsonObject { ["data"] = "__JSON__" + new JsonObject { ["savedAt"] = DateTimeOffset.Now.ToUnixTimeMilliseconds(), ["version"] = 2 }.ToJsonString() });
        _src().Write(TongHopService.Db, new Dictionary<string, JsonArray?> { [Sheet] = data, ["M8_Meta"] = meta });
        // Tổng hợp giữ danh sách trong bộ nhớ và ghi lại toàn bộ khi lưu → cập nhật theo để không ghi đè thay đổi vừa làm.
        if (_th() is { } th) th.Results = Rows.Select((r, i) => new TongHopService.Result { Stt = i + 1, Code = r.Code, Note = r.Note, Complete = r.Code.Length == Len }).ToList();
        Recheck();
        _audit.Log(_session.Username, "Danh mục mã hàng", $"{moTa} ({n} dòng)");
    }

    public static readonly string[] ExportHead = { "STT", "Mã hàng", "Diễn giải", "Size (đoạn 8)", "Trọng lượng (đoạn 9)", "Đang dùng ở", "Kiểm tra" };
    public List<object?[]> ExportRows() => Rows.Select((r, i) =>
    {
        var d = r.Code.Length == Len ? Decode(Seg, r.Code) : new(); var s = StatusFor(r.Code);
        return new object?[] { i + 1, r.Code, r.Note, d.Count > 7 ? d[7].Name ?? "" : "", d.Count > 8 ? d[8].Name ?? "" : "", UseOf(r.Code), s is null ? "" : string.Join("; ", s.Msgs) };
    }).ToList();

    public void Export(string path)
    {
        if (!CanExport) throw new BusinessException("Tài khoản không có quyền xuất file Danh mục mã hàng.");
        SoKho.SoKhoService.ExportTable("Danh muc ma hang", ExportHead, ExportRows(), path);
    }
}
