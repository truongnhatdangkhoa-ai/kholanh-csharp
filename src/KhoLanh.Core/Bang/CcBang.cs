using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using KhoLanh.Core.SoKho;

namespace KhoLanh.Core.Bang;

/// <summary>Bảng = tiêu đề + dòng; ô là string / double / null.</summary>
public sealed class CcTable
{
    public List<string> Head = new();
    public List<object?[]> Rows = new();
    public CcTable() { }
    public CcTable(IEnumerable<string> head, IEnumerable<object?[]> rows) { Head = head.ToList(); Rows = rows.ToList(); }
}

/// <summary>
/// LÕI XỬ LÝ BẢNG dùng chung cho "Xử lý PDF &amp; Excel" + "So sánh &amp; Tra cứu bảng" (chuyển từ cc-bang-engine.js):
/// ParsePages · CleanTable (làm sạch) · MergeStack (gộp theo tên cột) · SplitByColumn / SplitByRows · CompareTables · LookupTables.
/// Số / ngày đọc theo quy chuẩn chung của app (KLNum / KLDate).
/// </summary>
public static class CcBang
{
    public static bool Blank(object? v) => v is null || (v is string s && Regex.Replace(s, @"[\s   ]+", "").Length == 0);
    public static string Str(object? v) => v switch
    {
        null => "", string s => s, double d => JsNum(d), int i => i.ToString(CultureInfo.InvariantCulture), DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), bool b => b ? "true" : "false",
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "",
    };
    private static string JsNum(double d) => d == Math.Floor(d) && Math.Abs(d) < 1e15 ? ((long)d).ToString(CultureInfo.InvariantCulture) : d.ToString("R", CultureInfo.InvariantCulture);
    public static string Squash(string s) => Regex.Replace(Regex.Replace(s, "[   ​﻿]", " "), @"[ \t\r\n]+", " ").Trim();
    public static string StripVN(string s)
    {
        var t = s.Normalize(NormalizationForm.FormD); var sb = new StringBuilder();
        foreach (var ch in t) if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().Replace('đ', 'd').Replace('Đ', 'D');
    }
    public static string ColName(int i) { int n = i + 1; var s = ""; while (n > 0) { var m = (n - 1) % 26; s = (char)('A' + m) + s; n = (n - 1) / 26; } return s; }
    private static double? KN(object? v) => v switch { null => null, double d => double.IsFinite(d) ? d : double.NaN, int i => i, bool => double.NaN, _ => SoKhoCore.ParseNum(Str(v)) };
    public static bool IsNum(object? v) => !Blank(v) && KN(v) is { } n && double.IsFinite(n);
    private static string? KD(object? v)
    {
        if (v is null) return null;
        if (v is double d) { if (!double.IsFinite(d) || d < 20000 || d > 80000) return null; var r = SoKhoCore.Iso(DateTime.UnixEpoch.AddMilliseconds(Math.Round((d - 25569) * 86400000))); return r; }
        var s = SoKhoCore.ParseDate(Str(v)); return s.Length > 0 ? s : null;
    }
    private static string Dmy(string iso) => iso.Substring(8, 2) + "/" + iso.Substring(5, 2) + "/" + iso[..4];
    private static string TitleCase(string s) => Regex.Replace(s.ToLowerInvariant(), @"(^|\s)(\S)", m => m.Groups[1].Value + m.Groups[2].Value.ToUpperInvariant());

    // ---------------------------------------------------------------- trang PDF: "1-3,5,8-" → [0,1,2,4,7,…]
    public static (List<int> Pages, string Error) ParsePages(string? text, int total)
    {
        var outp = new List<int>(); var seen = new HashSet<int>(); var t = (text ?? "").Trim();
        if (t.Length == 0) return (new(), "Chưa nhập trang.");
        foreach (var part in Regex.Split(t, @"[,;\s]+").Where(x => x.Length > 0))
        {
            var m = Regex.Match(part, @"^(\d*)\s*-\s*(\d*)$"); var single = Regex.IsMatch(part, @"^\d+$");
            int a, b;
            if (single) a = b = int.Parse(part, CultureInfo.InvariantCulture);
            else if (m.Success && (m.Groups[1].Value.Length > 0 || m.Groups[2].Value.Length > 0)) { a = m.Groups[1].Value.Length > 0 ? int.Parse(m.Groups[1].Value) : 1; b = m.Groups[2].Value.Length > 0 ? int.Parse(m.Groups[2].Value) : total; }
            else return (new(), $"Không hiểu \"{part}\". Ví dụ: 1-3, 5, 8-");
            if (a < 1 || b < 1 || a > total || b > total) return (new(), $"Trang ngoài phạm vi 1–{total} (\"{part}\").");
            if (a > b) (a, b) = (b, a);
            for (var p = a; p <= b; p++) if (seen.Add(p)) outp.Add(p - 1);
        }
        return (outp, "");
    }

    // ---------------------------------------------------------------- LÀM SẠCH
    public sealed class ColOpt { public bool Num, Date, Upper, Lower, Title; }
    public sealed class CleanOpts
    {
        public bool Trim = true, BlankRows = true, NumAll, DateAll;
        public List<int>? Dedupe;                // null = không bỏ trùng; rỗng = so mọi cột
        public Dictionary<int, ColOpt> Cols = new();
    }
    public sealed record LogItem(int R, int C, string From, string To, string Why);
    public sealed record Issue(int R, int C, string V, string Why);
    public sealed class CleanResult { public CcTable Table = new(); public List<int> SrcRows = new(); public List<LogItem> Log = new(); public List<Issue> Issues = new(); public int BlankRows, Dups; }

    public static CleanResult CleanTable(CcTable tbl, CleanOpts? opts = null)
    {
        opts ??= new(); var log = new List<LogItem>(); var issues = new List<Issue>();
        var head = tbl.Head.ToList(); var rows = tbl.Rows.Select(r => { var x = r.ToList(); while (x.Count < head.Count) x.Add(null); return x.ToArray(); }).ToList();
        var keep = Enumerable.Range(0, rows.Count).ToList();
        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            for (var c = 0; c < head.Count; c++)
            {
                if (row[c] is not string v) continue;
                if (opts.Trim) { var s = Squash(v); if (s != v) { log.Add(new(keep[r], c, v, s, "Cắt khoảng trắng")); v = s; } }
                var co = opts.Cols.GetValueOrDefault(c) ?? new ColOpt();
                if ((co.Upper || co.Lower || co.Title) && v.Length > 0)
                {
                    var s = co.Upper ? v.ToUpperInvariant() : co.Lower ? v.ToLowerInvariant() : TitleCase(v);
                    if (s != v) { log.Add(new(keep[r], c, v, s, "Đổi chữ hoa / thường")); v = s; }
                }
                object? nv = v;
                if ((co.Num || opts.NumAll) && v.Length > 0)
                {
                    var n = SoKhoCore.ParseNum(v);
                    if (n is { } d && double.IsFinite(d)) { log.Add(new(keep[r], c, v, JsNum(d), "Chữ → số")); nv = d; }
                    else if (n is { } dn && double.IsNaN(dn)) issues.Add(new(keep[r], c, v, "Không đọc được thành số"));
                }
                else if ((co.Date || opts.DateAll) && v.Length > 0)
                {
                    var d = SoKhoCore.ParseDate(v);
                    if (d.Length > 0) { var s = Dmy(d); if (s != v) { log.Add(new(keep[r], c, v, s, "Ngày → dd/mm/yyyy")); nv = s; } }
                    else issues.Add(new(keep[r], c, v, "Không đọc được thành ngày"));
                }
                row[c] = nv;
            }
        }
        int blankRows = 0, dups = 0;
        if (opts.BlankRows)
        {
            var k = new List<object?[]>(); var ks = new List<int>();
            for (var r = 0; r < rows.Count; r++) { if (rows[r].All(Blank)) blankRows++; else { k.Add(rows[r]); ks.Add(keep[r]); } }
            rows = k; keep = ks;
        }
        if (opts.Dedupe is not null)
        {
            var cols = opts.Dedupe.Count == 0 ? Enumerable.Range(0, head.Count).ToList() : opts.Dedupe;
            var seen = new HashSet<string>(); var k = new List<object?[]>(); var ks = new List<int>();
            for (var r = 0; r < rows.Count; r++)
            {
                var key = string.Join("\u0001", cols.Select(c => { var v = c < rows[r].Length ? rows[r][c] : null; return IsNum(v) ? "n" + JsNum(KN(v)!.Value) : "s" + Squash(Str(v)).ToLowerInvariant(); }));
                if (!seen.Add(key)) dups++; else { k.Add(rows[r]); ks.Add(keep[r]); }
            }
            rows = k; keep = ks;
        }
        var alive = keep.ToHashSet();
        return new CleanResult { Table = new CcTable(head, rows), SrcRows = keep, Log = log.Where(l => alive.Contains(l.R)).ToList(), Issues = issues.Where(x => alive.Contains(x.R)).ToList(), BlankRows = blankRows, Dups = dups };
    }

    // ---------------------------------------------------------------- GỘP (xếp chồng, ghép cột theo tên)
    public static (CcTable Table, List<string> Notes) MergeStack(IEnumerable<(string Name, CcTable Tbl)> items)
    {
        var list = items.ToList(); static string Norm(string h) => Squash(h).ToLowerInvariant();
        var head = new List<string>(); var idx = new Dictionary<string, int>(); var notes = new List<string>();
        foreach (var it in list) foreach (var h in it.Tbl.Head) { var k = Norm(h); if (k.Length > 0 && !idx.ContainsKey(k)) { idx[k] = head.Count; head.Add(h.Trim()); } }
        var rows = new List<object?[]>();
        foreach (var it in list)
        {
            var map = it.Tbl.Head.Select(h => idx.TryGetValue(Norm(h), out var i) ? i : (int?)null).ToList();
            var missing = head.Where((_, i) => !map.Contains(i)).ToList();
            if (missing.Count > 0) notes.Add($"\"{it.Name}\" thiếu cột: {string.Join(", ", missing)}");
            foreach (var r in it.Tbl.Rows)
            {
                if (r.All(Blank)) continue;
                var x = new object?[head.Count + 1];
                for (var c = 0; c < r.Length; c++) if (c < map.Count && map[c] is { } m) x[m] = r[c];
                x[head.Count] = it.Name; rows.Add(x);
            }
        }
        return (new CcTable(head.Append("Nguồn"), rows), notes);
    }

    // ---------------------------------------------------------------- TÁCH
    public static string SafeName(object? s) { var t = Regex.Replace(Str(s), "[\\\\/:*?\"<>|\r\n\t]+", "_").Trim(); if (t.Length > 80) t = t[..80]; return t.Length > 0 ? t : "(trống)"; }

    public static List<(string Key, string Name, CcTable Tbl)> SplitByColumn(CcTable tbl, int col)
    {
        var m = new Dictionary<string, List<object?[]>>(); var order = new List<string>();
        foreach (var r in tbl.Rows)
        {
            if (r.All(Blank)) continue; var k = Squash(Str(col < r.Length ? r[col] : null)); var key = k.Length == 0 ? "(trống)" : k;
            if (!m.TryGetValue(key, out var l)) { m[key] = l = new(); order.Add(key); } l.Add(r);
        }
        var used = new HashSet<string>(); var outp = new List<(string, string, CcTable)>();
        foreach (var key in order)
        {
            var nm = SafeName(key); var n = nm; var i = 2;
            while (used.Contains(n.ToLowerInvariant())) n = $"{nm} ({i++})";
            used.Add(n.ToLowerInvariant()); outp.Add((key, n, new CcTable(tbl.Head, m[key])));
        }
        return outp;
    }

    public static List<(string Name, CcTable Tbl)> SplitByRows(CcTable tbl, int n)
    {
        n = Math.Max(1, n); var outp = new List<(string, CcTable)>();
        for (var i = 0; i < tbl.Rows.Count; i += n) outp.Add(($"Phần {outp.Count + 1}", new CcTable(tbl.Head, tbl.Rows.Skip(i).Take(n))));
        return outp;
    }

    // ---------------------------------------------------------------- SO SÁNH / TRA CỨU
    public sealed class MatchOpts { public bool IgnoreCase = true, IgnoreDiacritics; public double Tol; }

    private static object? Cell(object?[] r, int c) => c < r.Length ? r[c] : null;
    private static string KeyOf(object?[] row, IList<int> cols, MatchOpts o) => string.Join("\u0001", cols.Select(c =>
    {
        var v = Cell(row, c); if (Blank(v)) return "";
        if (IsNum(v)) return "n" + JsNum(KN(v)!.Value);
        var s = Squash(Str(v)); if (o.IgnoreCase) s = s.ToLowerInvariant(); if (o.IgnoreDiacritics) s = StripVN(s); return "s" + s;
    }));
    private static string KeyText(object?[] row, IList<int> cols) => string.Join(" | ", cols.Select(c => Str(Cell(row, c)).Trim()));

    public static bool SameVal(object? a, object? b, MatchOpts o)
    {
        if (Blank(a) && Blank(b)) return true; if (Blank(a) || Blank(b)) return false;
        if (IsNum(a) && IsNum(b)) return Math.Abs(KN(a)!.Value - KN(b)!.Value) <= o.Tol + 1e-9;
        var da = a is string ? KD(a) : null; var db = b is string ? KD(b) : null;
        if (da is not null && db is not null) return da == db;
        string x = Squash(Str(a)), y = Squash(Str(b));
        if (o.IgnoreCase) { x = x.ToLowerInvariant(); y = y.ToLowerInvariant(); }
        if (o.IgnoreDiacritics) { x = StripVN(x); y = StripVN(y); }
        return x == y;
    }

    private sealed record Index(Dictionary<string, List<int>> M, List<string> Order, Dictionary<string, List<int>> Dups, List<int> Empty);
    private static Index IndexRows(CcTable tbl, IList<int> cols, MatchOpts o)
    {
        var m = new Dictionary<string, List<int>>(); var order = new List<string>(); var empty = new List<int>();
        for (var i = 0; i < tbl.Rows.Count; i++)
        {
            var r = tbl.Rows[i]; if (r.All(Blank)) continue; var k = KeyOf(r, cols, o);
            if (cols.All(c => Blank(Cell(r, c)))) { empty.Add(i); continue; }
            if (!m.TryGetValue(k, out var l)) { m[k] = l = new(); order.Add(k); } l.Add(i);
        }
        return new Index(m, order, order.Where(k => m[k].Count > 1).ToDictionary(k => k, k => m[k]), empty);
    }

    public sealed record Pair(int A, int B, string Name);
    public sealed record DiffCell(string Name, string A, string B);
    public sealed record DiffRow(string Key, int RowA, int RowB, List<DiffCell> Cells);
    public sealed record DupGroup(string Key, List<int> Rows);
    public sealed class CompareResult
    {
        public List<int> OnlyA = new(), OnlyB = new(), EmptyKeyA = new(), EmptyKeyB = new();
        public List<DiffRow> Diff = new();
        public List<(int RowA, int RowB)> Same = new();
        public List<DupGroup> DupA = new(), DupB = new();
    }

    public static CompareResult CompareTables(CcTable A, CcTable B, IList<int> keyA, IList<int> keyB, IList<Pair> pairs, MatchOpts o)
    {
        var ia = IndexRows(A, keyA, o); var ib = IndexRows(B, keyB, o); var res = new CompareResult();
        foreach (var k in ia.Order)
        {
            var rowsA = ia.M[k];
            if (!ib.M.TryGetValue(k, out var rowsB)) { res.OnlyA.AddRange(rowsA); continue; }
            int ra = rowsA[0], rb = rowsB[0]; var cells = new List<DiffCell>();
            foreach (var p in pairs) { var a = Cell(A.Rows[ra], p.A); var b = Cell(B.Rows[rb], p.B); if (!SameVal(a, b, o)) cells.Add(new DiffCell(p.Name, Str(a), Str(b))); }
            if (cells.Count > 0) res.Diff.Add(new DiffRow(KeyText(A.Rows[ra], keyA), ra, rb, cells)); else res.Same.Add((ra, rb));
        }
        foreach (var k in ib.Order) if (!ia.M.ContainsKey(k)) res.OnlyB.AddRange(ib.M[k]);
        res.DupA = ia.Dups.Select(kv => new DupGroup(KeyText(A.Rows[kv.Value[0]], keyA), kv.Value)).ToList();
        res.DupB = ib.Dups.Select(kv => new DupGroup(KeyText(B.Rows[kv.Value[0]], keyB), kv.Value)).ToList();
        res.EmptyKeyA = ia.Empty; res.EmptyKeyB = ib.Empty;
        return res;
    }

    public sealed class LookupResult { public CcTable Table = new(); public int Found, NotFound, Multi, EmptyKey, DupB; public List<int> Miss = new(); }

    /// <summary>Tra cứu kiểu VLOOKUP: kéo các cột <paramref name="pull"/> của B sang A theo cột khóa. dup: first | last | sum | join | count.</summary>
    public static LookupResult LookupTables(CcTable A, CcTable B, IList<int> keyA, IList<int> keyB, IList<int> pull, string dup, MatchOpts o, string suffix = " (B)")
    {
        var ib = IndexRows(B, keyB, o); var res = new LookupResult { DupB = ib.Dups.Count };
        var heads = pull.Select(c => { var h = Str(c < B.Head.Count ? B.Head[c] : null).Trim(); if (h.Length == 0) h = "Cột " + ColName(c); if (A.Head.Any(x => Squash(x).ToLowerInvariant() == h.ToLowerInvariant())) h += suffix; return h; }).ToList();
        var rows = new List<object?[]>();
        for (var i = 0; i < A.Rows.Count; i++)
        {
            var r = A.Rows[i]; var outp = r.ToList(); while (outp.Count < A.Head.Count) outp.Add(null);
            if (r.All(Blank) || keyA.All(c => Blank(Cell(r, c)))) { rows.Add(outp.Concat(pull.Select(_ => (object?)null)).ToArray()); if (!r.All(Blank)) res.EmptyKey++; continue; }
            if (!ib.M.TryGetValue(KeyOf(r, keyA, o), out var hit)) { res.NotFound++; res.Miss.Add(i); rows.Add(outp.Concat(pull.Select(_ => (object?)null)).ToArray()); continue; }
            res.Found++; if (hit.Count > 1) res.Multi++;
            foreach (var c in pull)
            {
                var vals = hit.Select(j => Cell(B.Rows[j], c)).ToList(); object? v;
                if (dup == "last") v = vals[^1];
                else if (dup == "sum") { var nums = vals.Where(IsNum).Select(x => KN(x)!.Value).ToList(); v = nums.Count > 0 ? nums.Sum() : vals[0]; }
                else if (dup == "join") v = string.Join("; ", vals.Where(x => !Blank(x)).Select(x => Str(x).Trim()).Distinct());
                else if (dup == "count") v = (double)hit.Count;
                else v = vals[0];
                outp.Add(v);
            }
            rows.Add(outp.ToArray());
        }
        res.Table = new CcTable(A.Head.Concat(heads), rows);
        return res;
    }

    // ---------------------------------------------------------------- đọc / ghi Excel
    /// <summary>Sheet Excel → bảng (dòng tiêu đề = <paramref name="headerRow"/>, 0-based); số giữ là số.</summary>
    public static CcTable FromAoa(IReadOnlyList<IReadOnlyList<object?>> aoa, int headerRow = 0)
    {
        if (aoa.Count == 0 || headerRow >= aoa.Count) return new CcTable();
        var width = aoa.Skip(headerRow).Select(r => r.Count).DefaultIfEmpty(0).Max();
        var head = Enumerable.Range(0, width).Select(c => { var h = Str(c < aoa[headerRow].Count ? aoa[headerRow][c] : null).Trim(); return h.Length > 0 ? h : "Cột " + ColName(c); }).ToList();
        var rows = aoa.Skip(headerRow + 1).Select(r => Enumerable.Range(0, width).Select(c => { var v = c < r.Count ? r[c] : null; return v is string s && s.Length == 0 ? null : v; }).ToArray()).ToList();
        while (rows.Count > 0 && rows[^1].All(Blank)) rows.RemoveAt(rows.Count - 1);
        return new CcTable(head, rows);
    }

    /// <summary>Đoán dòng tiêu đề: dòng đầu tiên (trong 15 dòng) có ≥ 2 ô chữ và nhiều ô nhất.</summary>
    public static int GuessHeader(IReadOnlyList<IReadOnlyList<object?>> aoa)
    {
        var best = 0; var bestN = -1;
        for (var r = 0; r < Math.Min(aoa.Count, 15); r++) { var n = aoa[r].Count(v => v is string s && s.Trim().Length > 0); if (n >= 2 && n > bestN) { best = r; bestN = n; } }
        return best;
    }
}
