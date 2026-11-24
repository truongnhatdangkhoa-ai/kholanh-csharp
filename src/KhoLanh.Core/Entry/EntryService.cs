using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClosedXML.Excel;
using KhoLanh.Core.Grid;
using KhoLanh.Core.Models;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;

namespace KhoLanh.Core.Entry;

/// <summary>
/// Module NHẬP LIỆU dùng chung: mỗi "trang" là một bảng kiểu Excel (cột tự đặt hoặc theo mẫu).
/// Lưu vào database module "nhap-lieu" (đi qua ISheetSource nên tự đồng bộ LAN):
///  - sheet "DanhSach": mỗi dòng một trang (id, tên, cột, mẫu, ngày tạo / sửa, người sửa, đã xóa);
///  - sheet "T-&lt;id&gt;": dữ liệu của trang (mỗi dòng một object c0..cN);
///  - sheet "Mau": các mẫu do người dùng lưu.
/// Mỗi trang là một sheet riêng nên hai máy sửa hai trang khác nhau không đụng nhau.
/// </summary>
public sealed class EntryService
{
    public const string Db = "nhap-lieu", PermKey = "m6";
    private const string ListSheet = "DanhSach", TplSheet = "Mau";
    private readonly Func<ISheetSource> _src;
    private readonly Session _session;
    private readonly AuditService _audit;

    public EntryService(Func<ISheetSource> source, Session session, AuditService audit) { _src = source; _session = session; _audit = audit; }

    public record PageInfo(string Id, string Name, string Template, string CreatedAt, string UpdatedAt, string UpdatedBy, int Rows, bool Deleted, List<GridColumn> Columns);
    /// <summary>Mẫu cột. <paramref name="Edited"/> = mẫu có sẵn đã được sửa (lưu đè trong sheet "Mau", "Về mặc định" để bỏ).</summary>
    public record Template(string Id, string Name, List<GridColumn> Columns, bool BuiltIn, bool Edited = false);

    /// <summary>Khóa lưu màu tô ô của một dòng ("c1:y,c3:r" — theo mã cột, xem <see cref="CellColors"/>).</summary>
    public const string ColorKey = "_bg";

    public static readonly Template[] BuiltIn =
    {
        new("trong", "Trống (cột A–J)", Enumerable.Range(0, 10).Select(i => new GridColumn("c" + i, GridModel.ColumnName(i), 110)).ToList(), true),
        new("nhap-kho", "Nhập kho", Cols(("Ngày", "date", 100, EntryRules.Today, ""), ("Mã hàng", "text", 190, "", ""), ("Tên hàng", "text", 240, EntryRules.Ten, ""), ("Size", "text", 80, EntryRules.Size, ""),
            ("Số thùng", "number", 90, "", ""), ("Kg / thùng", "number", 90, EntryRules.Tl, ""), ("Tổng kg", "number", 100, "", "=[Số thùng]*[Kg / thùng]"), ("Vị trí", "text", 90, "", ""), ("Khách hàng", "text", 130, "", ""), ("Ghi chú", "text", 200, "", "")), true),
        new("xuat-kho", "Xuất kho", Cols(("Ngày", "date", 100, EntryRules.Today, ""), ("Số phiếu", "text", 110, "", ""), ("Mã hàng", "text", 190, "", ""), ("Tên hàng", "text", 240, EntryRules.Ten, ""), ("Size", "text", 80, EntryRules.Size, ""),
            ("Số thùng", "number", 90, "", ""), ("Tổng kg", "number", 100, "", ""), ("Vị trí", "text", 90, "", ""), ("Khách hàng", "text", 130, "", ""), ("Ghi chú", "text", 200, "", "")), true),
        // 1.62: mẫu kiểm kê mới — Size / Trọng lượng theo mã, Net = Số lượng × Trọng lượng, Ngày nhập = ngày tạo dòng (không tự đổi khi sửa)
        // 1.71: Net(kg) → Tổng Net(kg); thêm Số lượng xuất · Số lượng còn lại (= Số lượng − SL xuất, là số đối chiếu kiểm kê) ·
        //       Tổng Net(kg) còn lại (= SL còn lại × Trọng lượng) · Ngày xuất (tự điền khi gõ SL xuất)
        new("kiem-ke", "Kiểm kê", Cols(("Vị trí", "text", 110, "", ""), ("Mã hàng", "text", 190, "", ""), ("Size", "text", 90, EntryRules.Size, ""),
            ("Trọng lượng", "number", 100, EntryRules.Tl, ""), ("Số lượng", "number", 90, "", ""), ("Tổng Net(kg)", "number", 110, "", "=[Số lượng]*[Trọng lượng]"),
            ("Ghi chú", "text", 200, "", ""), ("Ngày nhập", "date", 100, EntryRules.Today, ""),
            (KkXuat, "number", 110, "", ""), (KkConLai, "number", 120, "", "=[Số lượng]-[Số lượng xuất]"),
            (KkNetConLai, "number", 140, "", "=[Số lượng còn lại]*[Trọng lượng]"), ("Ngày xuất", "date", 100, EntryRules.NgayXuat, "")), true),
    };

    public const string KkXuat = "Số lượng xuất", KkConLai = "Số lượng còn lại", KkNetConLai = "Tổng Net(kg) còn lại";

    /// <summary>1.71: trang / mẫu kiểm kê tạo trước bản này → đổi tên Net(kg) thành Tổng Net(kg) và thêm 4 cột mới (dữ liệu cũ giữ nguyên;
    /// cột công thức tính lại khi lưu). Chỉ áp khi trang có đủ Số lượng + Trọng lượng.</summary>
    public static List<GridColumn> UpgradeKiemKe(List<GridColumn> cols)
    {
        string F(GridColumn c) => GridFilter.Fold(c.Header).Replace(" ", "");
        if (!cols.Any(c => F(c) == "soluong") || !cols.Any(c => F(c) == "trongluong")) return cols;
        var res = cols.Select(c => F(c) is "net(kg)" or "netkg" or "net" && c.Formula.Replace(" ", "") == "=[Sốlượng]*[Trọnglượng]" ? new GridColumn(c.Key, "Tổng Net(kg)", c.Width, c.Type) { Auto = c.Auto, Formula = c.Formula, ReadOnly = c.ReadOnly } : c).ToList();
        var next = res.Select(c => c.Key.StartsWith("c") && int.TryParse(c.Key[1..], out var n) ? n : -1).DefaultIfEmpty(-1).Max() + 1;
        void Add(string h, string type, double w, string auto, string fx)
        {
            if (res.Any(c => GridFilter.Fold(c.Header).Trim() == GridFilter.Fold(h).Trim())) return;
            res.Add(new GridColumn("c" + next++, h, w, type) { Auto = auto, Formula = fx });
        }
        Add(KkXuat, "number", 110, "", "");
        Add(KkConLai, "number", 120, "", "=[Số lượng]-[Số lượng xuất]");
        Add(KkNetConLai, "number", 140, "", "=[Số lượng còn lại]*[Trọng lượng]");
        Add("Ngày xuất", "date", 100, EntryRules.NgayXuat, "");
        return res;
    }

    private static List<GridColumn> Cols(params (string h, string t, double w, string auto, string fx)[] cols) =>
        cols.Select((c, i) => new GridColumn("c" + i, c.h, c.w, c.t) { Auto = c.auto, Formula = c.fx }).ToList();

    // ------------------------------------------------------------------ quyền

    private void Require(string level)
    {
        if (!_session.Can(PermKey, level))
            throw new BusinessException(level == Perm.Edit ? "Tài khoản không có quyền sửa Nhập liệu." : "Tài khoản không có quyền xem Nhập liệu.");
    }

    public bool CanEdit => _session.Can(PermKey, Perm.Edit);
    public bool CanExport => _session.Can(PermKey, Perm.Export);

    // ------------------------------------------------------------------ danh sách trang

    private JsonArray ReadArr(string sheet) => JsonNode.Parse(_src().Read(Db, sheet)) as JsonArray ?? new JsonArray();

    private static string S(JsonNode? n) => n switch { null => "", JsonValue v when v.TryGetValue<string>(out var s) => s, _ => n.ToString() };

    private static List<GridColumn> ParseCols(string json)
    {
        try
        {
            return (JsonNode.Parse(json) as JsonArray ?? new JsonArray()).OfType<JsonObject>()
                .Select(o => new GridColumn(S(o["k"]), S(o["h"]), double.TryParse(S(o["w"]), NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ? w : 110, S(o["t"]) is { Length: > 0 } t ? t : "text")
                    { Auto = S(o["a"]), Formula = S(o["f"]) })
                .ToList();
        }
        catch { return new(); }
    }

    private static string ColsJson(IEnumerable<GridColumn> cols) =>
        new JsonArray(cols.Select(c =>
        {
            var o = new JsonObject { ["k"] = c.Key, ["h"] = c.Header, ["w"] = Math.Round(c.Width), ["t"] = c.Type };
            if (c.Auto.Length > 0) o["a"] = c.Auto;          // 1.62: tự điền / công thức (trang cũ không có → cột nhập tay như trước)
            if (c.Formula.Length > 0) o["f"] = c.Formula;
            return (JsonNode)o;
        }).ToArray()).ToJsonString();

    private static PageInfo ToInfo(JsonObject o)
    {
        var cols = ParseCols(S(o["cot"]));
        if (S(o["mau"]) == "kiem-ke") cols = UpgradeKiemKe(cols);   // 1.71
        return new(S(o["id"]), S(o["ten"]), S(o["mau"]), S(o["tao"]), S(o["sua"]), S(o["nguoi"]),
            int.TryParse(S(o["dong"]), out var n) ? n : 0, S(o["xoa"]) == "1", cols);
    }

    private static JsonObject ToRow(PageInfo p) => new()
    {
        ["id"] = p.Id, ["ten"] = p.Name, ["mau"] = p.Template, ["tao"] = p.CreatedAt, ["sua"] = p.UpdatedAt, ["nguoi"] = p.UpdatedBy,
        ["dong"] = p.Rows, ["xoa"] = p.Deleted ? "1" : "", ["cot"] = ColsJson(p.Columns),
    };

    public List<PageInfo> Pages(bool deleted = false)
    {
        Require(Perm.View);
        return ReadArr(ListSheet).OfType<JsonObject>().Select(ToInfo).Where(p => p.Id.Length > 0 && p.Deleted == deleted)
            .OrderByDescending(p => p.UpdatedAt, StringComparer.Ordinal).ToList();
    }

    /// <summary>Đọc danh sách trang / dữ liệu trang không xét quyền Nhập liệu — cho module khác (Đối chiếu kiểm kê) chỉ ĐỌC số liệu.</summary>
    internal List<PageInfo> PagesForRead() => ReadArr(ListSheet).OfType<JsonObject>().Select(ToInfo).Where(p => p.Id.Length > 0 && !p.Deleted)
        .OrderByDescending(p => p.UpdatedAt, StringComparer.Ordinal).ToList();
    internal List<string[]> LoadForRead(PageInfo p)
    {
        var keys = p.Columns.Select(c => c.Key).ToList();
        return ReadArr(DataSheet(p.Id)).OfType<JsonObject>().Select(o => keys.Select(k => S(o[k])).ToArray()).ToList();
    }

    private void SaveList(Func<List<PageInfo>, List<PageInfo>> change)
    {
        // Danh sách trang dùng chung mọi máy: máy khác vừa đổi → đọc lại rồi áp lại thay đổi của mình (tối đa 3 lần).
        for (var attempt = 0; ; attempt++)
        {
            var list = change(ReadArr(ListSheet).OfType<JsonObject>().Select(ToInfo).ToList());
            try
            {
                _src().Write(Db, new Dictionary<string, JsonArray?> { [ListSheet] = new JsonArray(list.Select(p => (JsonNode)ToRow(p)).ToArray()) });
                return;
            }
            catch (SheetConflictException) when (attempt < 2) { }
        }
    }

    private static string Now => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    private static string DataSheet(string id) => "T-" + id;

    public PageInfo Create(string name, Template tpl)
    {
        Require(Perm.Edit);
        name = string.IsNullOrWhiteSpace(name) ? tpl.Name + " " + DateTime.Now.ToString("dd/MM/yyyy") : name.Trim();
        var id = DateTime.Now.ToString("yyMMddHHmmss") + Random.Shared.Next(100, 999);
        var p = new PageInfo(id, name, tpl.Id, Now, Now, _session.Username, 0, false, tpl.Columns.Select(Clone).ToList());
        SaveList(l => { l.Add(p); return l; });
        _src().Write(Db, new Dictionary<string, JsonArray?> { [DataSheet(id)] = new JsonArray() });
        _audit.Log(_session.Username, "Nhập liệu: tạo trang", name);
        return p;
    }

    private static GridColumn Clone(GridColumn c) => c.Clone();

    public void Rename(string id, string name)
    {
        Require(Perm.Edit);
        if (string.IsNullOrWhiteSpace(name)) throw new BusinessException("Tên trang không được để trống.");
        SaveList(l => l.Select(p => p.Id == id ? p with { Name = name.Trim(), UpdatedAt = Now, UpdatedBy = _session.Username } : p).ToList());
    }

    /// <summary>Đưa vào thùng rác (khôi phục được) hoặc xóa hẳn nếu đã ở thùng rác.</summary>
    public void Delete(string id, bool forever = false)
    {
        Require(Perm.Edit);
        string name = "";
        SaveList(l =>
        {
            name = l.FirstOrDefault(p => p.Id == id)?.Name ?? "";
            return forever ? l.Where(p => p.Id != id).ToList() : l.Select(p => p.Id == id ? p with { Deleted = true, UpdatedAt = Now } : p).ToList();
        });
        if (forever) _src().Write(Db, new Dictionary<string, JsonArray?> { [DataSheet(id)] = null });
        _audit.Log(_session.Username, forever ? "Nhập liệu: xóa hẳn trang" : "Nhập liệu: xóa trang", name);
    }

    public void Restore(string id)
    {
        Require(Perm.Edit);
        SaveList(l => l.Select(p => p.Id == id ? p with { Deleted = false, UpdatedAt = Now } : p).ToList());
    }

    // ------------------------------------------------------------------ dữ liệu trang

    public List<string[]> Load(PageInfo p)
    {
        Require(Perm.View);
        var keys = p.Columns.Select(c => c.Key).ToList();
        return ReadArr(DataSheet(p.Id)).OfType<JsonObject>().Select(o => keys.Select(k => S(o[k])).ToArray()).ToList();
    }

    /// <summary>Như <see cref="Load"/> nhưng mỗi dòng thêm 1 phần tử cuối = màu tô ô của dòng (<see cref="ColorKey"/>).</summary>
    public List<string[]> LoadWithColors(PageInfo p)
    {
        Require(Perm.View);
        var keys = p.Columns.Select(c => c.Key).Append(ColorKey).ToList();
        return ReadArr(DataSheet(p.Id)).OfType<JsonObject>().Select(o => keys.Select(k => S(o[k])).ToArray()).ToList();
    }

    /// <summary>
    /// Lưu dữ liệu + cột của trang (bỏ dòng trống ở cuối; cột công thức được tính lại). Dòng dài hơn số cột 1 phần tử → phần tử cuối là màu ô.
    /// Trang đã bị máy khác sửa → SheetConflictException.
    /// </summary>
    public PageInfo Save(PageInfo p, IReadOnlyList<GridColumn> columns, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        Require(Perm.Edit);
        var fx = EntryRules.RecomputeAll(columns, rows);
        if (fx.Count > 0)
        {
            var copy = rows.Select(r => r.ToArray()).ToList();
            foreach (var (r, c, v) in fx) if (c < copy[r].Length) copy[r][c] = v;
            rows = copy;
        }
        var n = rows.Count;
        while (n > 0 && rows[n - 1].All(string.IsNullOrWhiteSpace)) n--;
        var arr = new JsonArray();
        for (var r = 0; r < n; r++)
        {
            var o = new JsonObject();
            for (var c = 0; c < columns.Count; c++)
            {
                var v = c < rows[r].Count ? rows[r][c] : "";
                if (v.Length > 0) o[columns[c].Key] = v;
            }
            if (rows[r].Count > columns.Count && CellColors.Clean(rows[r][columns.Count], columns) is { Length: > 0 } bg) o[ColorKey] = bg;
            arr.Add(o);
        }
        _src().Write(Db, new Dictionary<string, JsonArray?> { [DataSheet(p.Id)] = arr });
        var updated = p with { Columns = columns.Select(Clone).ToList(), Rows = n, UpdatedAt = Now, UpdatedBy = _session.Username };
        SaveList(l => l.Select(x => x.Id == p.Id ? updated with { Name = x.Name } : x).ToList());
        return updated;
    }

    // ------------------------------------------------------------------ mẫu

    public List<Template> Templates()
    {
        var rows = ReadArr(TplSheet).OfType<JsonObject>()
            .Select(o => new Template(S(o["id"]), S(o["ten"]), ParseCols(S(o["cot"])), false))
            .Where(t => t.Id.Length > 0 && t.Columns.Count > 0).ToList();
        var over = rows.Where(t => BuiltIn.Any(b => b.Id == t.Id)).ToDictionary(t => t.Id);
        var builtIn = BuiltIn.Select(b => over.TryGetValue(b.Id, out var o) ? o with { BuiltIn = true, Edited = true, Columns = b.Id == "kiem-ke" ? UpgradeKiemKe(o.Columns) : o.Columns } : b);
        return builtIn.Concat(rows.Where(t => !over.ContainsKey(t.Id)).OrderBy(t => t.Name, StringComparer.CurrentCulture)).ToList();
    }

    /// <summary>
    /// Lưu mẫu. Có <paramref name="id"/> → sửa đúng mẫu đó (mẫu có sẵn thì lưu bản sửa đè lên, "Về mặc định" để bỏ);
    /// không có → mẫu trùng tên thì thay, còn lại thêm mẫu mới.
    /// </summary>
    public Template SaveTemplate(string name, IEnumerable<GridColumn> columns, string? id = null)
    {
        Require(Perm.Edit);
        if (string.IsNullOrWhiteSpace(name)) throw new BusinessException("Đặt tên cho mẫu.");
        var cols = columns.Select(Clone).ToList();
        if (cols.Count == 0) throw new BusinessException("Mẫu phải có ít nhất một cột.");
        var dup = cols.GroupBy(c => c.Header.Trim(), StringComparer.CurrentCultureIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null) throw new BusinessException($"Mẫu có 2 cột trùng tên “{dup.Key}”.");
        foreach (var c in cols.Where(c => c.Formula.Length > 0))
            if (CongThuc.Check(c.Formula) is { } err) throw new BusinessException($"Cột “{c.Header}”: {err}");
        var arr = ReadArr(TplSheet);
        var nm = name.Trim();
        bool Same(JsonObject o) => S(o["ten"]).Equals(nm, StringComparison.CurrentCultureIgnoreCase);
        var builtIn = id is not null ? BuiltIn.FirstOrDefault(b => b.Id == id) : BuiltIn.FirstOrDefault(b => b.Name.Equals(nm, StringComparison.CurrentCultureIgnoreCase));
        var existing = builtIn is not null ? arr.OfType<JsonObject>().FirstOrDefault(o => S(o["id"]) == builtIn.Id)
            : id is not null ? arr.OfType<JsonObject>().FirstOrDefault(o => S(o["id"]) == id)
            : arr.OfType<JsonObject>().FirstOrDefault(Same);
        if (id is not null && builtIn is null && arr.OfType<JsonObject>().Any(o => Same(o) && S(o["id"]) != id))
            throw new BusinessException($"Đã có mẫu khác tên “{nm}”.");
        var newId = builtIn?.Id ?? (existing is not null ? S(existing["id"]) : "u" + DateTime.Now.ToString("yyMMddHHmmss") + Random.Shared.Next(10, 99));
        if (existing is not null) arr.Remove(existing);
        arr.Add(new JsonObject { ["id"] = newId, ["ten"] = name.Trim(), ["cot"] = ColsJson(cols) });
        _src().Write(Db, new Dictionary<string, JsonArray?> { [TplSheet] = arr });
        _audit.Log(_session.Username, "Nhập liệu: lưu mẫu", name.Trim());
        return new Template(newId, name.Trim(), cols, builtIn is not null, builtIn is not null);
    }

    /// <summary>Xóa mẫu tự làm; với mẫu có sẵn đã sửa → trở về mặc định.</summary>
    public void DeleteTemplate(string id)
    {
        Require(Perm.Edit);
        var arr = ReadArr(TplSheet);
        foreach (var o in arr.OfType<JsonObject>().Where(o => S(o["id"]) == id).ToList()) arr.Remove(o);
        _src().Write(Db, new Dictionary<string, JsonArray?> { [TplSheet] = arr });
        _audit.Log(_session.Username, BuiltIn.Any(b => b.Id == id) ? "Nhập liệu: mẫu về mặc định" : "Nhập liệu: xóa mẫu", id);
    }

    // ------------------------------------------------------------------ Excel

    /// <summary>Đọc một trang tính Excel: dòng đầu (có chữ) làm tiêu đề cột.</summary>
    public static (List<GridColumn> cols, List<string[]> rows, List<string> sheets) ReadExcel(string path, string? sheetName = null, bool firstRowHeader = true)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var wb = new XLWorkbook(fs);
            var names = wb.Worksheets.Select(w => w.Name).ToList();
            var ws = sheetName is not null && wb.Worksheets.TryGetWorksheet(sheetName, out var w0) ? w0 : wb.Worksheets.First();
            var used = ws.RangeUsed();
            if (used is null) return (new(), new(), names);
            int r1 = used.FirstRow().RowNumber(), r2 = used.LastRow().RowNumber(), c1 = 1, c2 = used.LastColumn().ColumnNumber();
            string Cell(int r, int c)
            {
                var cell = ws.Cell(r, c);
                if (cell.IsEmpty()) return "";
                if (cell.DataType == XLDataType.DateTime) return cell.GetDateTime().ToString(cell.GetDateTime().TimeOfDay == TimeSpan.Zero ? "dd/MM/yyyy" : "dd/MM/yyyy HH:mm");
                if (cell.DataType == XLDataType.Number) return cell.GetDouble().ToString("0.############", CultureInfo.InvariantCulture);
                return cell.GetFormattedString();
            }
            var cols = new List<GridColumn>();
            for (var c = c1; c <= c2; c++)
            {
                var h = firstRowHeader ? Cell(r1, c).Trim() : "";
                cols.Add(new GridColumn("c" + (c - c1), h.Length > 0 ? h : GridModel.ColumnName(c - 1), 110));
            }
            var rows = new List<string[]>();
            for (var r = firstRowHeader ? r1 + 1 : r1; r <= r2; r++)
                rows.Add(Enumerable.Range(c1, c2 - c1 + 1).Select(c => Cell(r, c)).ToArray());
            // đoán kiểu cột số / độ rộng
            for (var i = 0; i < cols.Count; i++)
            {
                var vals = rows.Select(x => x[i]).Where(v => v.Length > 0).Take(200).ToList();
                if (vals.Count > 0 && vals.All(v => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) && !vals.Any(v => v.Length > 1 && v[0] == '0' && char.IsDigit(v[1])))
                    cols[i].Type = "number";
                var len = Math.Max(cols[i].Header.Length, vals.Count == 0 ? 6 : vals.Max(v => v.Length));
                cols[i].Width = Math.Clamp(len * 8 + 20, 60, 320);
            }
            return (cols, rows, names);
        }
        catch (Exception e) when (e is not BusinessException)
        {
            throw new BusinessException("Không đọc được file Excel: " + e.Message);
        }
    }

    /// <summary>Xuất Excel (dòng dài hơn số cột 1 phần tử → phần tử cuối là màu ô, tô nền ô tương ứng).</summary>
    public void ExportExcel(string path, string title, IReadOnlyList<GridColumn> columns, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        Require(Perm.Export);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SafeSheetName(title));
        for (var c = 0; c < columns.Count; c++)
        {
            var h = ws.Cell(1, c + 1);
            h.Value = columns[c].Header;
            h.Style.Font.Bold = true;
            h.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCE6F1");
            ws.Column(c + 1).Width = Math.Max(6, columns[c].Width / 7.5);
        }
        var n = rows.Count;
        while (n > 0 && rows[n - 1].Take(columns.Count).All(string.IsNullOrWhiteSpace)) n--;
        for (var r = 0; r < n; r++)
            for (var c = 0; c < columns.Count; c++)
            {
                var v = c < rows[r].Count ? rows[r][c] : "";
                if (v.Length == 0) continue;
                var cell = ws.Cell(r + 2, c + 1);
                if (columns[c].Type == "number" && TryNumber(v, out var d)) cell.Value = d;
                else if (columns[c].Type == "date" && DateTime.TryParseExact(v, new[] { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                { cell.Value = dt; cell.Style.DateFormat.Format = "dd/MM/yyyy"; }
                else cell.Value = v;
            }
        for (var r = 0; r < n; r++)
            if (rows[r].Count > columns.Count)
                foreach (var (key, hex) in CellColors.Parse(rows[r][columns.Count]))
                {
                    var c = columns.ToList().FindIndex(x => x.Key == key);
                    if (c >= 0) ws.Cell(r + 2, c + 1).Style.Fill.BackgroundColor = XLColor.FromHtml(hex);
                }
        var range = ws.Range(1, 1, Math.Max(1, n + 1), Math.Max(1, columns.Count));
        range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.SheetView.FreezeRows(1);
        wb.SaveAs(path);
        _audit.Log(_session.Username, "Nhập liệu: xuất Excel", $"{title} → {path}");
    }

    private static string SafeSheetName(string s)
    {
        var t = new string(s.Where(ch => !"[]:*?/\\".Contains(ch)).ToArray()).Trim();
        if (t.Length == 0) t = "Trang 1";
        return t.Length > 31 ? t[..31] : t;
    }

    /// <summary>Đọc số kiểu Việt Nam lẫn quốc tế: "1.234,5" / "1,234.5" / "1234.5".</summary>
    public static bool TryNumber(string s, out double d)
    {
        s = s.Trim().Replace(" ", "");
        if (System.Text.RegularExpressions.Regex.IsMatch(s, @"^-?\d{1,3}(\.\d{3}){2,}$")) s = s.Replace(".", ""); // 1.234.567 (kiểu Việt)
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return true;
        if (s.Contains(',') && s.Contains('.'))
        {
            var vn = s.LastIndexOf(',') > s.LastIndexOf('.');
            var t = vn ? s.Replace(".", "").Replace(',', '.') : s.Replace(",", "");
            return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
        }
        if (s.Contains(','))
        {
            var parts = s.Split(',');
            var t = parts.Length == 2 && parts[1].Length != 3 ? s.Replace(',', '.') : s.Replace(",", "");
            return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
        }
        return false;
    }
}
