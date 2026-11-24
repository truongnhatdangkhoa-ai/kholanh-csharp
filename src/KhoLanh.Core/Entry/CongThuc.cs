using System.Globalization;
using System.Text;

namespace KhoLanh.Core.Entry;

/// <summary>
/// CÔNG THỨC kiểu Excel cho cột của trang Nhập liệu (1.62). Mỗi cột có một công thức, tính cho từng dòng:
///  · tham chiếu cột: <c>[Tên cột]</c> hoặc chữ cột như Excel (<c>E</c>, <c>E2</c> — số dòng bỏ qua, luôn là dòng đang tính);
///  · toán tử + - * / ^ % &amp; = &lt;&gt; &lt; &gt; &lt;= &gt;=, chuỗi "…", số 1.5, TRUE / FALSE; dấu phân cách tham số "," hoặc ";";
///  · hàm: SUM, AVERAGE, MIN, MAX, COUNT, ROUND, ROUNDUP, ROUNDDOWN, INT, ABS, MOD, IF, IFERROR, AND, OR, NOT,
///    CONCAT, LEFT, RIGHT, MID, LEN, UPPER, LOWER, TRIM, VALUE, TODAY.
/// Lỗi trả mã như Excel: #DIV/0!, #VALUE!, #NAME?, #REF!, #ERROR!.
/// </summary>
public static class CongThuc
{
    public static readonly (string Name, string Help)[] Functions =
    {
        ("SUM", "SUM([Số lượng], [Kg]) — cộng"), ("AVERAGE", "AVERAGE(a, b, …) — trung bình"), ("MIN", "MIN(a, b, …)"), ("MAX", "MAX(a, b, …)"),
        ("COUNT", "COUNT(a, b, …) — đếm ô có số"), ("ROUND", "ROUND(số, chữ số thập phân)"), ("ROUNDUP", "ROUNDUP(số, chữ số) — làm tròn lên"),
        ("ROUNDDOWN", "ROUNDDOWN(số, chữ số) — làm tròn xuống"), ("INT", "INT(số) — phần nguyên"), ("ABS", "ABS(số) — trị tuyệt đối"), ("MOD", "MOD(số, chia) — số dư"),
        ("IF", "IF(điều kiện, khi đúng, khi sai)"), ("IFERROR", "IFERROR(biểu thức, khi lỗi)"), ("AND", "AND(đk1, đk2, …)"), ("OR", "OR(đk1, đk2, …)"), ("NOT", "NOT(đk)"),
        ("CONCAT", "CONCAT(a, b, …) — nối chữ (hoặc a & b)"), ("LEFT", "LEFT(chữ, n)"), ("RIGHT", "RIGHT(chữ, n)"), ("MID", "MID(chữ, bắt đầu, n)"),
        ("LEN", "LEN(chữ) — độ dài"), ("UPPER", "UPPER(chữ)"), ("LOWER", "LOWER(chữ)"), ("TRIM", "TRIM(chữ) — bỏ khoảng trắng thừa"),
        ("VALUE", "VALUE(chữ) — đổi ra số"), ("TODAY", "TODAY() — ngày hôm nay"),
    };

    private sealed class FxError : Exception { public readonly string Code; public FxError(string code) : base(code) { Code = code; } }

    /// <summary>Một dòng đang tính: tra giá trị theo tên cột (không phân biệt hoa thường, dấu) hoặc theo chỉ số cột.</summary>
    public interface IRow { string? ByName(string header); string? ByIndex(int col); }

    private abstract class Node { public abstract object Eval(IRow row); }

    // ------------------------------------------------------------------ công khai

    private static readonly Dictionary<string, Node> Cache = new();
    private static readonly object Lock = new();

    /// <summary>Kiểm tra cú pháp: null = hợp lệ, còn lại là thông báo lỗi.</summary>
    public static string? Check(string formula)
    {
        try { Compile(formula); return null; }
        catch (FxError e) { return e.Code == "#NAME?" ? "Có tên hàm / tên cột không hiểu." : "Công thức sai cú pháp."; }
    }

    /// <summary>Tính công thức cho một dòng → chuỗi ghi vào ô (số dạng 1234.5; lỗi dạng #DIV/0!).</summary>
    public static string Eval(string formula, IRow row)
    {
        try { return Str(Compile(formula).Eval(row)); }
        catch (FxError e) { return e.Code; }
        catch (Exception) { return "#ERROR!"; }
    }

    /// <summary>Định dạng số lưu trong ô: bỏ sai số dấu phẩy động, dạng quốc tế "1234.5".</summary>
    public static string Num(double d) => double.IsFinite(d) ? Math.Round(d, 6).ToString("0.######", CultureInfo.InvariantCulture) : "#VALUE!";

    private static Node Compile(string formula)
    {
        var f = (formula ?? "").Trim();
        if (f.StartsWith('=')) f = f[1..];
        lock (Lock)
        {
            if (Cache.TryGetValue(f, out var n)) return n;
            var p = new Parser(f); n = p.ParseAll();
            if (Cache.Count > 500) Cache.Clear();
            Cache[f] = n;
            return n;
        }
    }

    // ------------------------------------------------------------------ giá trị

    private static double ToNum(object v) => v switch
    {
        double d => d,
        bool b => b ? 1 : 0,
        string s when s.Trim().Length == 0 => 0,
        string s when EntryService.TryNumber(s, out var d) => d,
        string s when s.StartsWith('#') => throw new FxError(s),
        _ => throw new FxError("#VALUE!"),
    };
    private static bool ToBool(object v) => v switch
    {
        bool b => b, double d => d != 0,
        string s when s.Trim().Length == 0 => false,
        string s when s.Trim().Equals("TRUE", StringComparison.OrdinalIgnoreCase) => true,
        string s when s.Trim().Equals("FALSE", StringComparison.OrdinalIgnoreCase) => false,
        string s when EntryService.TryNumber(s, out var d) => d != 0,
        _ => throw new FxError("#VALUE!"),
    };
    /// <summary>Chuỗi là mã lỗi công thức (#DIV/0!, #VALUE!, #NAME?…).</summary>
    public static bool IsError(string? s) => s is { Length: > 1 } && s[0] == '#' && (s.EndsWith('!') || s.EndsWith('?'));
    private static string Str(object v) => v switch { double d => Num(d), bool b => b ? "TRUE" : "FALSE", string s => s, _ => "" };

    private static int Cmp(object a, object b)
    {
        bool IsNum(object x, out double d) { d = 0; return x is double dd ? (d = dd) == dd : x is bool bb ? (d = bb ? 1 : 0) == d : x is string s && s.Trim().Length > 0 && EntryService.TryNumber(s, out d); }
        if (IsNum(a, out var x) && IsNum(b, out var y)) return x.CompareTo(y);
        return string.Compare(Str(a).Trim(), Str(b).Trim(), CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);
    }

    // ------------------------------------------------------------------ nút cây

    private sealed class Const : Node { public object V = ""; public override object Eval(IRow row) => V; }
    private sealed class Ref : Node
    {
        public string? Name; public int Index = -1;
        public override object Eval(IRow row) => (Name is not null ? row.ByName(Name) : row.ByIndex(Index)) ?? throw new FxError("#REF!");
    }
    private sealed class Unary : Node
    {
        public char Op; public Node A = null!;
        public override object Eval(IRow row) => Op switch { '-' => -ToNum(A.Eval(row)), '%' => ToNum(A.Eval(row)) / 100, _ => ToNum(A.Eval(row)) };
    }
    private sealed class Bin : Node
    {
        public string Op = ""; public Node A = null!, B = null!;
        public override object Eval(IRow row)
        {
            object a = A.Eval(row), b = B.Eval(row);
            switch (Op)
            {
                case "&": return Str(a) + Str(b);
                case "=": return Cmp(a, b) == 0;
                case "<>": return Cmp(a, b) != 0;
                case "<": return Cmp(a, b) < 0;
                case ">": return Cmp(a, b) > 0;
                case "<=": return Cmp(a, b) <= 0;
                case ">=": return Cmp(a, b) >= 0;
            }
            // nhân / chia với ô trống → ô trống (Net = Số lượng × Trọng lượng chưa đủ số thì để trống, không ghi 0)
            if (Op is "*" or "/" && (a is string { Length: 0 } || b is string { Length: 0 } || a is string sa && sa.Trim().Length == 0 || b is string sb && sb.Trim().Length == 0)) return "";
            double x = ToNum(a), y = ToNum(b);
            return Op switch
            {
                "+" => x + y, "-" => x - y, "*" => x * y,
                "/" => y == 0 ? throw new FxError("#DIV/0!") : x / y,
                "^" => Math.Pow(x, y),
                _ => throw new FxError("#ERROR!"),
            };
        }
    }
    private sealed class Call : Node
    {
        public string Name = ""; public List<Node> Args = new();
        private double N(IRow r, int i) => ToNum(Args[i].Eval(r));
        private string S(IRow r, int i) => Str(Args[i].Eval(r));
        private void Need(int min, int max = int.MaxValue) { if (Args.Count < min || Args.Count > max) throw new FxError("#VALUE!"); }
        private IEnumerable<double> Nums(IRow r, bool skipText)
        {
            foreach (var a in Args)
            {
                var v = a.Eval(r);
                if (v is string s && (s.Trim().Length == 0 || (skipText && !EntryService.TryNumber(s, out _)))) continue;
                yield return ToNum(v);
            }
        }
        private static double Round(double x, double digits, MidpointRounding m)
        {
            var p = Math.Pow(10, (int)digits);
            return m == MidpointRounding.AwayFromZero ? Math.Round(x * p, MidpointRounding.AwayFromZero) / p
                 : m == MidpointRounding.ToPositiveInfinity ? Math.Sign(x) * Math.Ceiling(Math.Abs(x) * p - 1e-9) / p
                 : Math.Sign(x) * Math.Floor(Math.Abs(x) * p + 1e-9) / p;
        }
        public override object Eval(IRow r)
        {
            switch (Name)
            {
                case "SUM": return Nums(r, true).Sum();
                case "AVERAGE": { var l = Nums(r, true).ToList(); return l.Count == 0 ? throw new FxError("#DIV/0!") : l.Average(); }
                case "MIN": { var l = Nums(r, true).ToList(); return l.Count == 0 ? 0.0 : l.Min(); }
                case "MAX": { var l = Nums(r, true).ToList(); return l.Count == 0 ? 0.0 : l.Max(); }
                case "COUNT": return (double)Args.Select(a => a.Eval(r)).Count(v => v is double || (v is string s && s.Trim().Length > 0 && EntryService.TryNumber(s, out _)));
                case "ROUND": Need(1, 2); return Round(N(r, 0), Args.Count > 1 ? N(r, 1) : 0, MidpointRounding.AwayFromZero);
                case "ROUNDUP": Need(1, 2); return Round(N(r, 0), Args.Count > 1 ? N(r, 1) : 0, MidpointRounding.ToPositiveInfinity);
                case "ROUNDDOWN": Need(1, 2); return Round(N(r, 0), Args.Count > 1 ? N(r, 1) : 0, MidpointRounding.ToZero);
                case "INT": Need(1, 1); return Math.Floor(N(r, 0));
                case "ABS": Need(1, 1); return Math.Abs(N(r, 0));
                case "MOD": { Need(2, 2); var d = N(r, 1); if (d == 0) throw new FxError("#DIV/0!"); var x = N(r, 0); return x - d * Math.Floor(x / d); }
                case "IF": Need(1, 3); return ToBool(Args[0].Eval(r)) ? (Args.Count > 1 ? Args[1].Eval(r) : true) : (Args.Count > 2 ? Args[2].Eval(r) : false);
                case "IFERROR":
                    Need(2, 2);
                    try { var v = Args[0].Eval(r); if ((v is string s && IsError(s)) || (v is double d && !double.IsFinite(d))) return Args[1].Eval(r); return v; }
                    catch (FxError) { return Args[1].Eval(r); }
                case "AND": Need(1); return Args.All(a => ToBool(a.Eval(r)));
                case "OR": Need(1); return Args.Any(a => ToBool(a.Eval(r)));
                case "NOT": Need(1, 1); return !ToBool(Args[0].Eval(r));
                case "CONCAT" or "CONCATENATE": return string.Concat(Args.Select(a => Str(a.Eval(r))));
                case "LEFT": { Need(1, 2); var s = S(r, 0); var n = Args.Count > 1 ? (int)N(r, 1) : 1; return s[..Math.Clamp(n, 0, s.Length)]; }
                case "RIGHT": { Need(1, 2); var s = S(r, 0); var n = Math.Clamp(Args.Count > 1 ? (int)N(r, 1) : 1, 0, s.Length); return s[^n..]; }
                case "MID": { Need(3, 3); var s = S(r, 0); var st = Math.Max(1, (int)N(r, 1)) - 1; var n = Math.Max(0, (int)N(r, 2)); return st >= s.Length ? "" : s.Substring(st, Math.Min(n, s.Length - st)); }
                case "LEN": Need(1, 1); return (double)S(r, 0).Length;
                case "UPPER": Need(1, 1); return S(r, 0).ToUpper(CultureInfo.CurrentCulture);
                case "LOWER": Need(1, 1); return S(r, 0).ToLower(CultureInfo.CurrentCulture);
                case "TRIM": Need(1, 1); return string.Join(' ', S(r, 0).Split(' ', StringSplitOptions.RemoveEmptyEntries));
                case "VALUE": Need(1, 1); return EntryService.TryNumber(S(r, 0), out var dv) ? dv : throw new FxError("#VALUE!");
                case "TODAY": Need(0, 0); return DateTime.Today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            }
            throw new FxError("#NAME?");
        }
    }

    // ------------------------------------------------------------------ phân tích cú pháp

    private static readonly HashSet<string> Known = Functions.Select(f => f.Name).Append("CONCATENATE").ToHashSet();

    private sealed class Parser
    {
        private readonly string _s; private int _i;
        public Parser(string s) { _s = s; }

        public Node ParseAll()
        {
            if (_s.Trim().Length == 0) return new Const { V = "" };
            var n = Cmp(); Ws();
            if (_i < _s.Length) throw new FxError("#ERROR!");
            return n;
        }
        private void Ws() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }
        private bool Eat(string t) { Ws(); if (string.CompareOrdinal(_s, _i, t, 0, t.Length) == 0) { _i += t.Length; return true; } return false; }

        private Node Cmp()
        {
            var a = Concat();
            while (true)
            {
                string? op = Eat("<>") ? "<>" : Eat("<=") ? "<=" : Eat(">=") ? ">=" : Eat("=") ? "=" : Eat("<") ? "<" : Eat(">") ? ">" : null;
                if (op is null) return a;
                a = new Bin { Op = op, A = a, B = Concat() };
            }
        }
        private Node Concat() { var a = Add(); while (Eat("&")) a = new Bin { Op = "&", A = a, B = Add() }; return a; }
        private Node Add()
        {
            var a = Mul();
            while (true) { if (Eat("+")) a = new Bin { Op = "+", A = a, B = Mul() }; else if (Eat("-")) a = new Bin { Op = "-", A = a, B = Mul() }; else return a; }
        }
        private Node Mul()
        {
            var a = Pow();
            while (true) { if (Eat("*")) a = new Bin { Op = "*", A = a, B = Pow() }; else if (Eat("/")) a = new Bin { Op = "/", A = a, B = Pow() }; else return a; }
        }
        private Node Pow() { var a = Un(); while (Eat("^")) a = new Bin { Op = "^", A = a, B = Un() }; return a; }
        private Node Un()
        {
            if (Eat("-")) return new Unary { Op = '-', A = Un() };
            if (Eat("+")) return Un();
            var p = Primary();
            while (Eat("%")) p = new Unary { Op = '%', A = p };
            return p;
        }
        private Node Primary()
        {
            Ws();
            if (_i >= _s.Length) throw new FxError("#ERROR!");
            var ch = _s[_i];
            if (ch == '(') { _i++; var n = Cmp(); if (!Eat(")")) throw new FxError("#ERROR!"); return n; }
            if (ch == '"')
            {
                var sb = new StringBuilder(); _i++;
                while (true)
                {
                    if (_i >= _s.Length) throw new FxError("#ERROR!");
                    if (_s[_i] == '"') { if (_i + 1 < _s.Length && _s[_i + 1] == '"') { sb.Append('"'); _i += 2; continue; } _i++; break; }
                    sb.Append(_s[_i++]);
                }
                return new Const { V = sb.ToString() };
            }
            if (ch == '[')
            {
                var end = _s.IndexOf(']', _i + 1);
                if (end < 0) throw new FxError("#ERROR!");
                var name = _s[(_i + 1)..end].Trim(); _i = end + 1;
                if (name.Length == 0) throw new FxError("#NAME?");
                return new Ref { Name = name };
            }
            if (char.IsDigit(ch) || (ch == '.' && _i + 1 < _s.Length && char.IsDigit(_s[_i + 1])))
            {
                var st = _i;
                while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) _i++;
                if (_i < _s.Length && (_s[_i] is 'e' or 'E') && _i + 1 < _s.Length && (char.IsDigit(_s[_i + 1]) || _s[_i + 1] is '-' or '+'))
                { _i += 2; while (_i < _s.Length && char.IsDigit(_s[_i])) _i++; }
                if (!double.TryParse(_s[st.._i], NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) throw new FxError("#ERROR!");
                return new Const { V = d };
            }
            if (char.IsLetter(ch) || ch == '_' || ch == '$')
            {
                var st = _i;
                while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] is '_' or '.' or '$')) _i++;
                var word = _s[st.._i].Replace("$", "").ToUpperInvariant();
                Ws();
                if (_i < _s.Length && _s[_i] == '(')
                {
                    _i++;
                    if (!Known.Contains(word)) throw new FxError("#NAME?");
                    var call = new Call { Name = word };
                    if (!Eat(")"))
                    {
                        do call.Args.Add(Cmp()); while (Eat(",") || Eat(";"));
                        if (!Eat(")")) throw new FxError("#ERROR!");
                    }
                    return call;
                }
                if (word is "TRUE") return new Const { V = true };
                if (word is "FALSE") return new Const { V = false };
                var m = System.Text.RegularExpressions.Regex.Match(word, "^([A-Z]{1,2})[0-9]*$");
                if (m.Success)
                {
                    var idx = 0; foreach (var c in m.Groups[1].Value) idx = idx * 26 + (c - 'A' + 1);
                    return new Ref { Index = idx - 1 };
                }
                throw new FxError("#NAME?");
            }
            throw new FxError("#ERROR!");
        }
    }
}
