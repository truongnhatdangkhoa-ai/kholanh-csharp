using KhoLanh.Core.Entry;
using KhoLanh.Core.SoDo;

namespace KhoLanh.Core.KiemKe;

/// <summary>
/// 1.73 (K2) — kiểm tra trang Nhập liệu kiểm kê khi gõ / dán (chỉ báo, không chặn lưu):
/// cùng Vị trí + Mã hàng ≥ 2 dòng (trùng — vàng), Vị trí không có trong Sơ đồ kho (vàng), Mã hàng lạ (vàng),
/// Số lượng / SL xuất không phải số, âm, hoặc SL xuất &gt; Số lượng (đỏ).
/// Hàm thuần O(số dòng) — giao diện gọi lại sau khi ngừng gõ (gộp nhiều thay đổi một lần), không quét theo từng phím.
/// </summary>
public static class KkKiemTra
{
    public enum Loai { Trung, ViTri, Ma, So }
    public sealed record Van(int Row, int Col, Loai Loai, bool Do, string Text);
    /// <summary>Chỉ số cột (−1 = trang không có cột đó).</summary>
    public sealed record Cot(int Vt, int Ma, int Sl, int SlXuat);

    public sealed class KetQua
    {
        public static readonly KetQua Rong = new();
        /// <summary>Mọi vấn đề, xếp theo dòng rồi cột.</summary>
        public List<Van> Items = new();
        private readonly Dictionary<long, List<Van>> _cell = new();
        public int TrungDong, ViTriLa, MaLa, SoLoi;
        public bool CoDo => Items.Any(v => v.Do);
        public bool Empty => Items.Count == 0;

        internal void Add(Van v)
        {
            Items.Add(v);
            var k = K(v.Row, v.Col);
            if (!_cell.TryGetValue(k, out var l)) _cell[k] = l = new();
            l.Add(v);
        }
        private static long K(int r, int c) => ((long)r << 16) | (uint)c;
        public IReadOnlyList<Van> At(int r, int c) => _cell.TryGetValue(K(r, c), out var l) ? l : Array.Empty<Van>();
        /// <summary>null = ô không có vấn đề; true = đỏ; false = vàng.</summary>
        public bool? Muc(int r, int c) { var l = At(r, c); return l.Count == 0 ? null : l.Any(v => v.Do); }
        public string? Tip(int r, int c) { var l = At(r, c); return l.Count == 0 ? null : string.Join("\n", l.Select(v => v.Text)); }

        /// <summary>"N dòng trùng · N vị trí lạ · N mã lạ · N số lỗi" (chỉ phần khác 0).</summary>
        public string TomTat()
        {
            var p = new List<string>();
            if (TrungDong > 0) p.Add($"{TrungDong:#,0} dòng trùng");
            if (ViTriLa > 0) p.Add($"{ViTriLa:#,0} vị trí lạ");
            if (MaLa > 0) p.Add($"{MaLa:#,0} mã lạ");
            if (SoLoi > 0) p.Add($"{SoLoi:#,0} số lỗi");
            return string.Join(" · ", p);
        }

        /// <summary>Vấn đề kế tiếp sau ô (r, c) theo thứ tự dòng → cột, hết thì quay về đầu.</summary>
        public Van? Next(int r, int c)
        {
            if (Items.Count == 0) return null;
            foreach (var v in Items) if (v.Row > r || (v.Row == r && v.Col > c)) return v;
            return Items[0];
        }
    }

    public static Cot? Cols(IEnumerable<string> headers)
    {
        var c = KiemKeCore.PageCols(headers);
        if (!c.ContainsKey("ma")) return null;
        return new Cot(c.GetValueOrDefault("vt", -1), c["ma"], c.GetValueOrDefault("sk", -1), c.GetValueOrDefault("xs", -1));
    }

    /// <summary>
    /// Kiểm tra <paramref name="rows"/> dòng. <paramref name="viTriCo"/>: mã vị trí chuẩn (TG1.A.01.1.1) có trong Sơ đồ kho không — null = bỏ qua (chưa nạp / chưa cấu hình);
    /// <paramref name="maCo"/>: mã hàng có trong Tồn kho An An / Danh mục không — null = bỏ qua.
    /// </summary>
    public static KetQua Check(int rows, Func<int, int, string> get, Cot cot, Func<string, bool>? viTriCo = null, Func<string, bool>? maCo = null)
    {
        var o = new KetQua();
        string G(int r, int c) => c < 0 ? "" : (get(r, c) ?? "").Trim();
        // trùng Vị trí + Mã hàng
        var groups = new Dictionary<string, List<int>>();
        if (cot.Vt >= 0)
            for (var r = 0; r < rows; r++)
            {
                string vt = G(r, cot.Vt), ma = G(r, cot.Ma);
                if (vt.Length == 0 || ma.Length == 0) continue;
                var key = (SoDoCore.Parse(vt)?.Code ?? vt.ToUpperInvariant()) + "|" + KiemKeCore.NormKey(ma);
                if (!groups.TryGetValue(key, out var l)) groups[key] = l = new();
                l.Add(r);
            }
        var dup = new Dictionary<int, List<int>>();
        foreach (var l in groups.Values) if (l.Count > 1) foreach (var r in l) dup[r] = l;

        for (var r = 0; r < rows; r++)
        {
            if (dup.TryGetValue(r, out var same))
            {
                var others = same.Where(x => x != r).Select(x => (x + 1).ToString("#,0")).ToList();
                var t = $"Trùng Vị trí + Mã hàng với dòng {string.Join(", ", others.Take(6))}{(others.Count > 6 ? "…" : "")} — kiểm tra có đếm / nhập 2 lần không.";
                o.Add(new Van(r, cot.Ma, Loai.Trung, false, t));
                o.Add(new Van(r, cot.Vt, Loai.Trung, false, t));
                o.TrungDong++;
            }
            if (cot.Vt >= 0 && viTriCo is not null)
            {
                var vt = G(r, cot.Vt);
                if (vt.Length > 0)
                {
                    var p = SoDoCore.Parse(vt);
                    if (p is null) { o.Add(new Van(r, cot.Vt, Loai.ViTri, false, $"Vị trí “{vt}” không đúng dạng mã vị trí (vd TG1.A.01.1.1).")); o.ViTriLa++; }
                    else if (!viTriCo(p.Code)) { o.Add(new Van(r, cot.Vt, Loai.ViTri, false, $"Vị trí {p.Code} không có trong Sơ đồ kho.")); o.ViTriLa++; }
                }
            }
            if (maCo is not null)
            {
                var ma = G(r, cot.Ma);
                if (ma.Length > 0 && !maCo(ma)) { o.Add(new Van(r, cot.Ma, Loai.Ma, false, $"Mã “{ma}” không có trong Tồn kho An An lẫn Danh mục mã hàng.")); o.MaLa++; }
            }
            double? sl = null;
            if (cot.Sl >= 0) sl = So(r, cot.Sl, "Số lượng");
            if (cot.SlXuat >= 0 && So(r, cot.SlXuat, "Số lượng xuất") is double xs && sl is double s && xs > s + 1e-9)
            {
                o.Add(new Van(r, cot.SlXuat, Loai.So, true, $"Số lượng xuất ({xs:#,0.###}) lớn hơn Số lượng ({s:#,0.###}).")); o.SoLoi++;
            }
        }
        o.Items.Sort((a, b) => a.Row != b.Row ? a.Row.CompareTo(b.Row) : a.Col.CompareTo(b.Col));
        return o;

        double? So(int r, int c, string ten)
        {
            var v = G(r, c);
            if (v.Length == 0) return null;
            if (!EntryService.TryNumber(v, out var d) || !double.IsFinite(d)) { o.Add(new Van(r, c, Loai.So, true, $"{ten} “{v}” không phải số.")); o.SoLoi++; return null; }
            if (d < 0) { o.Add(new Van(r, c, Loai.So, true, $"{ten} âm ({d:#,0.###}).")); o.SoLoi++; return null; }
            return d;
        }
    }
}
