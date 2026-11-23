using System.Text.Json.Nodes;
using KhoLanh.Core.Models;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;
using KhoLanh.Core.TongHop;

namespace KhoLanh.Core.PhieuKho;

/// <summary>
/// Module Phiếu xuất kho (C#, viết lại theo "Phiếu nhập/xuất kho" của app cũ). Giai đoạn 1: Phiếu XUẤT KHẨU.
/// Dùng CHUNG dữ liệu với app cũ: database "phieu-nhap-xuat", sheet PNX_Phieu (mỗi phiếu 1 dòng: id, loai, ngay, soPhieu, lsx, khach, capNhat, data = "__JSON__" + JSON phiếu).
/// Xuất Excel = điền thẳng vào file mẫu .xlsm nhúng sẵn (giữ nguyên định dạng, hình, macro).
/// </summary>
public sealed class PhieuKhoService
{
    public const string Db = "phieu-nhap-xuat", SheetPhieu = "PNX_Phieu", SheetLsx = "PNX_ListSX", PermKey = "m13";

    public sealed record ListRec(string Lsx, string Khach, string File, string CapNhat, ListData Data);

    public List<Phieu> List { get; private set; } = new();
    /// <summary>Kho "List sản xuất" đã nạp, mỗi LSX 1 bản — gõ số LSX ở phiếu là điền lại được, khỏi nạp PDF nữa.</summary>
    public List<ListRec> Lists { get; private set; } = new();
    public bool Loaded { get; private set; }

    private readonly Func<ISheetSource> _src;
    private readonly Session _session;
    private readonly AuditService _audit;
    private readonly TongHopService? _tongHop;
    public PhieuKhoService(Func<ISheetSource> source, Session session, AuditService audit, TongHopService? tongHop = null) { _src = source; _session = session; _audit = audit; _tongHop = tongHop; }

    public bool CanView => _session.Can(PermKey, Perm.View);
    public bool CanEdit => _session.Can(PermKey, Perm.Edit);
    public bool CanExport => _session.Can(PermKey, Perm.Export);
    private void RequireEdit() { if (!CanEdit) throw new BusinessException("Tài khoản không có quyền sửa Phiếu xuất kho (lập / sửa / xóa phiếu)."); }
    private void Log(string action, string detail) => _audit.Log(_session.Username, "Phiếu xuất kho: " + action, detail);

    public void Load()
    {
        if (!CanView) throw new BusinessException("Tài khoản không có quyền xem Phiếu xuất kho.");
        JsonArray rows;
        try { rows = JsonNode.Parse(_src().Read(Db, SheetPhieu)) as JsonArray ?? new JsonArray(); } catch { rows = new JsonArray(); }
        var list = new List<Phieu>();
        foreach (var o in rows.OfType<JsonObject>())
        {
            var d = o["data"]?.ToString() ?? "";
            if (d.StartsWith("__JSON__")) d = d[8..];
            if (PhieuKhoCore.FromJson(d) is { } p) list.Add(p);
        }
        List = list.OrderByDescending(p => p.CapNhat, StringComparer.Ordinal).ToList();
        JsonArray lrows;
        try { lrows = JsonNode.Parse(_src().Read(Db, SheetLsx)) as JsonArray ?? new JsonArray(); } catch { lrows = new JsonArray(); }
        var lists = new List<ListRec>();
        foreach (var o in lrows.OfType<JsonObject>())
        {
            var d = o["data"]?.ToString() ?? ""; if (d.StartsWith("__JSON__")) d = d[8..];
            if (ListSx.FromJson(d) is { Ok: true } ld) lists.Add(new ListRec(o["lsx"]?.ToString() ?? ld.Lsx, o["khach"]?.ToString() ?? ld.Khach, o["file"]?.ToString() ?? "", o["capNhat"]?.ToString() ?? "", ld));
        }
        Lists = lists.OrderByDescending(x => x.CapNhat, StringComparer.Ordinal).ToList();
        Loaded = true;
    }

    private void SaveAll()
    {
        var rows = new JsonArray(List.Select(p => (JsonNode?)new JsonObject
        {
            ["id"] = p.Id, ["loai"] = p.Loai, ["ngay"] = p.Ngay, ["soPhieu"] = p.SoPhieu, ["lsx"] = p.Lsx, ["khach"] = p.Khach, ["capNhat"] = p.CapNhat,
            ["data"] = "__JSON__" + PhieuKhoCore.ToJson(p),
        }).ToArray());
        _src().Write(Db, new Dictionary<string, JsonArray?> { [SheetPhieu] = rows });
    }

    private void SaveLists()
    {
        var rows = new JsonArray(Lists.Select(x => (JsonNode?)new JsonObject
        {
            ["lsx"] = x.Lsx, ["khach"] = x.Khach, ["hopDong"] = x.Data.HopDong, ["soDong"] = x.Data.Rows.Count, ["file"] = x.File, ["capNhat"] = x.CapNhat, ["data"] = "__JSON__" + ListSx.ToJson(x.Data),
        }).ToArray());
        _src().Write(Db, new Dictionary<string, JsonArray?> { [SheetLsx] = rows });
    }

    public ListRec? FindList(string? lsx) => string.IsNullOrWhiteSpace(lsx) ? null : Lists.FirstOrDefault(x => PhieuKhoCore.LsxNorm(x.Lsx) == PhieuKhoCore.LsxNorm(lsx));

    /// <summary>Đọc 1 file List sản xuất (PDF hoặc Excel). Chưa lưu gì.</summary>
    public ListData ReadList(string path)
    {
        RequireEdit();
        return ListSx.ReadFile(path);
    }

    /// <summary>Lưu (thêm / thay) List của 1 LSX vào kho. Giữ nguyên Tên hàng / Mã hàng / Mã lô đã gán ở bản cũ cho size trùng tên.</summary>
    public ListRec SaveListData(ListData d, string file)
    {
        RequireEdit();
        if (d.Lsx.Trim().Length == 0) throw new BusinessException("List không có số LSX — điền số LSX trước khi lưu vào kho.");
        var rec = new ListRec(d.Lsx.Trim(), d.Khach, file, DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"), d);
        var i = Lists.FindIndex(x => PhieuKhoCore.LsxNorm(x.Lsx) == PhieuKhoCore.LsxNorm(rec.Lsx));
        if (i >= 0) Lists[i] = rec; else Lists.Insert(0, rec);
        SaveLists();
        Log("Lưu List sản xuất", $"LSX {rec.Lsx}: {d.Rows.Count} ngày × {d.Sizes.Count} size");
        return rec;
    }

    public void DeleteList(string lsx)
    {
        RequireEdit();
        var n = Lists.RemoveAll(x => PhieuKhoCore.LsxNorm(x.Lsx) == PhieuKhoCore.LsxNorm(lsx));
        if (n > 0) { SaveLists(); Log("Xóa List sản xuất", "LSX " + lsx); }
    }

    /// <summary>Hàm tra Packing từ mã hàng bằng Mã hóa sản phẩm (đoạn 9 trọng lượng, đoạn 10 quy cách); null nếu chưa nối Tổng hợp.</summary>
    public Func<string, ListSxApply.PackResult>? PackFunc()
    {
        if (_tongHop is null) return null;
        try { _tongHop.Load(); } catch { return null; }
        var dict = _tongHop.SegmentData.ToDictionary(p => p.Key, p => p.Value.GroupBy(e => e.Code.Trim().ToUpperInvariant()).ToDictionary(g => g.Key, g => g.First().Name));
        string? NameOf(int seg, string code) => dict.TryGetValue(seg, out var d) && d.TryGetValue(code.ToUpperInvariant(), out var n) ? n : null;
        return ma => ListSxApply.PackingFromMa(ma, NameOf);
    }

    /// <summary>Đưa List vào phiếu đang soạn (không lưu phiếu). Packing tự lấy từ mã hàng khi List không ghi quy cách.</summary>
    public ListSxApply.ApplyResult ApplyList(Phieu p, ListData d, bool append)
    {
        RequireEdit();
        return ListSxApply.Apply(p, d, null, append, PackFunc());
    }

    public Phieu NewPhieu()
    {
        var p = PhieuKhoCore.NewPhieu();
        PhieuKhoCore.RefreshNumbers(List, p);
        return p;
    }

    /// <summary>Lưu (thêm / sửa) 1 phiếu. Trả về bản đã lưu.</summary>
    public Phieu Save(Phieu p)
    {
        RequireEdit();
        p = PhieuKhoCore.Clone(p);
        foreach (var m in p.Mh) foreach (var r in m.DongList) r.Date = PhieuKhoCore.NormDate(r.Date);
        var isNew = p.Id.Length == 0;
        if (isNew) p.Id = "px" + DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString("x") + Guid.NewGuid().ToString("N")[..3];
        p.CapNhat = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
        p.DaSua = true;
        var i = List.FindIndex(x => x.Id == p.Id);
        if (i >= 0) List[i] = p; else List.Insert(0, p);
        SaveAll();
        Log(isNew ? "Tạo phiếu" : "Sửa phiếu", $"{p.SoPhieu} ({PhieuKhoCore.LoaiTen(p.Loai)})");
        return p;
    }

    public void Delete(string id)
    {
        RequireEdit();
        var p = List.FirstOrDefault(x => x.Id == id) ?? throw new BusinessException("Không tìm thấy phiếu.");
        List.Remove(p);
        SaveAll();
        Log("Xóa phiếu", p.SoPhieu);
    }

    public ThongKeThang.Data ThongKe(string monthKey) => ThongKeThang.Build(List, monthKey);

    public void ExportThongKe(string monthKey, string path)
    {
        if (!CanExport) throw new BusinessException("Tài khoản không có quyền xuất file Phiếu xuất kho.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(monthKey, @"^\d{4}-(0[1-9]|1[0-2])$")) throw new BusinessException("Tháng phải dạng yyyy-mm (VD 2026-11).");
        var d = ThongKe(monthKey);
        if (d.Groups.Count == 0) throw new BusinessException($"Tháng {monthKey[5..7]}/{monthKey[0..4]} chưa có phiếu xuất khẩu nào đã lưu.");
        ThongKeThang.Export(d, path);
        Log("Thống kê tháng", $"{monthKey}: {d.Groups.Count} phiếu, {d.TongThang:#,0.##} kiện");
    }

    public static byte[] Template()
    {
        using var s = typeof(PhieuKhoService).Assembly.GetManifestResourceStream("KhoLanh.Core.PhieuKho.mau-xuat-khau.xlsm")
            ?? throw new InvalidOperationException("Thiếu file mẫu Phiếu xuất kho.");
        using var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray();
    }

    /// <summary>Xuất phiếu ra file .xlsm bằng cách điền vào mẫu. Phiếu phải qua kiểm tra; đã xuất = coi là ĐÃ IN (không tự đổi số nữa).</summary>
    public void Export(Phieu p, string path)
    {
        if (!CanExport) throw new BusinessException("Tài khoản không có quyền xuất file Phiếu xuất kho.");
        p = PhieuKhoCore.Clone(p);
        foreach (var m in p.Mh) foreach (var r in m.DongList) r.Date = PhieuKhoCore.NormDate(r.Date);
        var errs = PhieuKhoCore.Validate(p);
        if (errs.Count > 0) throw new BusinessException(string.Join(" ", errs.Take(3)) + (errs.Count > 3 ? $" (+{errs.Count - 3} lỗi nữa)" : ""));
        var plan = PhieuKhoCore.BuildPlan(p);
        File.WriteAllBytes(path, XlsxTemplate.Fill(Template(), plan.Cells, plan.Options));
        Log("Xuất Excel", $"{p.SoPhieu} → {Path.GetFileName(path)}");
        if (CanEdit && p.Id.Length > 0 && List.FirstOrDefault(x => x.Id == p.Id) is { } saved)
        {
            saved.XuatLuc = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
            try { SaveAll(); } catch { /* không ghi được dấu "đã xuất" thì bỏ qua */ }
        }
    }
}
