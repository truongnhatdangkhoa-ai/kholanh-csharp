using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace KhoLanh.Core.KhangSinh;

/// <summary>
/// 1.175 — Kết quả kháng sinh TÔM NHẬP KHẨU: phiếu "KẾT QUẢ PHÂN TÍCH DƯ LƯỢNG KHÁNG SINH" của cả cont hàng nhập (không có lô).
/// Định danh: INV · Contract · TP (số trong Contract, vd TP-143 → 143) · Size · số mẫu.
/// Lưu cùng sheet <c>ket-qua-khang-sinh/KhangSinh</c>: khóa <c>i:nk:&lt;id&gt;</c> = MỘT Size của một phiếu; bản bị phiếu cập nhật thay
/// được giữ ở khóa <c>i:nkh:&lt;id&gt;</c> (lịch sử). Điện thoại nhận cùng khóa (bản gọn, xem <see cref="Compact"/>).
/// </summary>
public static class KhangSinhNhapKhau
{
    public const string Prefix = "i:nk:", HistPrefix = "i:nkh:";
    public static readonly string[] Keys = KhangSinhCore.FieldKeys;           // enro cipro oxy doxy sulfo aoz cap
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ================================================================== mô hình

    public sealed class Mau
    {
        public int Stt;
        public string Ten = "", NgayNhap = "";        // ngày nhập ISO
        public int Kien;
        public Dictionary<string, string> Inputs = new();
        public string Kq = "", DatTinh = "", GhiChu = "";
        /// <summary>Các chỉ tiêu phát hiện được (không phải ND / *** / trống): (khóa, nhãn, giá trị).</summary>
        public List<(string Key, string Label, string Value)> Nhiem
        {
            get
            {
                var o = new List<(string, string, string)>();
                foreach (var (k, l) in KhangSinhCore.Fields)
                {
                    var v = (Inputs.GetValueOrDefault(k) ?? "").Trim();
                    if (v.Length == 0 || v.Equals("ND", StringComparison.OrdinalIgnoreCase) || v.StartsWith("*")) continue;
                    o.Add((k, l, v));
                }
                return o;
            }
        }
        public bool IsNhiem => Nhiem.Count > 0;
        public JsonObject ToJson()
        {
            var inp = new JsonArray(Keys.Select(k => (JsonNode)(Inputs.GetValueOrDefault(k) ?? "")).ToArray());
            return new JsonObject { ["s"] = Stt, ["t"] = Ten, ["d"] = NgayNhap, ["k"] = Kien, ["i"] = inp, ["kq"] = Kq, ["dt"] = DatTinh, ["gc"] = GhiChu };
        }
        public static Mau FromJson(JsonObject o)
        {
            var m = new Mau
            {
                Stt = (int)(KhangSinhCore.NumOf(o["s"]) ?? 0), Ten = KhangSinhCore.Str(o["t"]), NgayNhap = KhangSinhCore.Str(o["d"]),
                Kien = (int)(KhangSinhCore.NumOf(o["k"]) ?? 0), Kq = KhangSinhCore.Str(o["kq"]), DatTinh = KhangSinhCore.Str(o["dt"]), GhiChu = KhangSinhCore.Str(o["gc"]),
            };
            if (o["i"] is JsonArray a) for (int i = 0; i < Keys.Length && i < a.Count; i++) m.Inputs[Keys[i]] = KhangSinhCore.Str(a[i]);
            return m;
        }
    }

    /// <summary>Một Size của một phiếu (= một bản ghi lưu).</summary>
    public sealed class Nhom
    {
        public string Key = "";
        public JsonObject J = new();
        public string So => KhangSinhCore.Str(J["so"]);
        public string Date => KhangSinhCore.Str(J["date"]);                    // ngày phân tích ISO
        public string Country => KhangSinhCore.Str(J["country"]);
        public string Invoice => KhangSinhCore.Str(J["invoice"]);
        public string InvDate => KhangSinhCore.Str(J["invDate"]);
        public string Contract => KhangSinhCore.Str(J["contract"]);
        public string Tp => KhangSinhCore.Str(J["tp"]);
        public string Hang => KhangSinhCore.Str(J["hang"]);                    // "Tôm HLSO"
        public string Size => KhangSinhCore.Str(J["size"]);
        public string File => KhangSinhCore.Str(J["file"]);
        public string SavedAt => KhangSinhCore.Str(J["savedAt"]);
        public string ReplacedBy => KhangSinhCore.Str(J["replacedBy"]);        // chỉ ở lịch sử: số phiếu thay thế
        public bool IsHistory => Key.StartsWith(HistPrefix, StringComparison.Ordinal);
        private List<Mau>? _mau;
        public List<Mau> Samples => _mau ??= (J["mau"] as JsonArray)?.OfType<JsonObject>().Select(Mau.FromJson).ToList() ?? new();
        public static Nhom? Parse(string key, string json)
        {
            try { return JsonNode.Parse(json) is JsonObject j && KhangSinhCore.Str(j["kind"]) == "nk" ? new Nhom { Key = key, J = j } : null; }
            catch { return null; }
        }
        public string NgayNhap => Samples.Select(m => m.NgayNhap).FirstOrDefault(x => x.Length > 0) ?? "";
        public int Kien => Samples.Sum(m => m.Kien);
        public string Label => $"TP {(Tp.Length > 0 ? Tp : "—")} · INV {Invoice} · {Hang} {Size}".Trim();
    }

    /// <summary>Tóm tắt kết quả một Size: số mẫu theo đặc tính + các mẫu nhiễm.</summary>
    public sealed class Tom
    {
        public int Total, Nhiem, Kien;
        public List<(string KyHieu, string Dat, int N, List<int> Stts)> Groups = new();
        public List<(int Stt, string Label, string Value)> NhiemList = new();
        public bool AllSame => Groups.Count == 1;
        public string? KyHieu => AllSame ? Groups[0].KyHieu : null;
        public string? Dat => AllSame ? Groups[0].Dat : null;
        /// <summary>"28 mẫu 23.MKS · 2 mẫu nhiễm 10.N-AS2-M-H".</summary>
        public string Text
        {
            get
            {
                if (Total == 0) return "chưa có mẫu";
                if (AllSame && Nhiem == 0) return $"{Total}/{Total} mẫu {Groups[0].KyHieu}";
                var clean = Groups.Where(g => g.Stts.Any(s => !NhiemList.Any(n => n.Stt == s))).ToList();
                var parts = new List<string>();
                foreach (var g in Groups)
                {
                    var nh = g.Stts.Count(s => NhiemList.Any(n => n.Stt == s));
                    if (g.N - nh > 0) parts.Add($"{g.N - nh} mẫu {g.KyHieu}");
                    if (nh > 0) parts.Add($"{nh} mẫu nhiễm {g.KyHieu}");
                }
                return $"{Total} mẫu: " + string.Join(" · ", parts);
            }
        }
        public string NhiemText => string.Join(" · ", NhiemList.GroupBy(n => n.Stt).Select(g => $"mẫu {g.Key} " + string.Join(", ", g.Select(x => $"{x.Label} {x.Value}"))));
    }
    public static Tom Summarize(Nhom n)
    {
        var t = new Tom { Total = n.Samples.Count, Kien = n.Kien };
        foreach (var m in n.Samples)
        {
            var ky = m.DatTinh.Length > 0 ? m.DatTinh : "(không ghi)";
            var i = t.Groups.FindIndex(g => g.KyHieu == ky);
            if (i < 0) { t.Groups.Add((ky, m.Kq, 1, new List<int> { m.Stt })); } else { var g = t.Groups[i]; g.Stts.Add(m.Stt); t.Groups[i] = (g.KyHieu, g.Dat, g.N + 1, g.Stts); }
            foreach (var (_, l, v) in m.Nhiem) t.NhiemList.Add((m.Stt, l, v));
        }
        t.Nhiem = n.Samples.Count(m => m.IsNhiem);
        return t;
    }

    // ================================================================== đọc PDF

    public sealed class Phieu
    {
        public string File = "", So = "", Date = "", Title = "", Country = "", Invoice = "", InvDate = "", Contract = "", Tp = "";
        public Dictionary<string, string> Lod = new();
        public List<Mau> Mau = new();
        public List<string> Issues = new();               // lỗi / cảnh báo khi đọc
        public byte[] Bytes = Array.Empty<byte>();
        public List<(string Hang, string Size, List<Mau> Mau)> BySize() =>
            Mau.GroupBy(m => HangSize(m.Ten)).Select(g => (g.Key.Hang, g.Key.Size, g.OrderBy(m => m.Stt).ToList())).ToList();
    }

    private static readonly Regex NameRe = new(@"^(?<hang>.*?)\s+(?<size>\d{1,3}(?:\s*/\s*\d{1,3})?)\s+nh[aậ]p\s+kh[aẩ]u", RegexOptions.IgnoreCase);
    public static (string Hang, string Size) HangSize(string ten)
    {
        var m = NameRe.Match(ten.Trim());
        return m.Success ? (m.Groups["hang"].Value.Trim(), Regex.Replace(m.Groups["size"].Value, @"\s+", "")) : (ten.Trim(), "");
    }

    public static bool IsNhapKhau(IReadOnlyList<PdfText.Page> pages)
    {
        var all = string.Join(" ", pages.SelectMany(p => p.Items).Select(t => t.Text));
        return Regex.IsMatch(all, @"nh[aậ]p kh[aẩ]u", RegexOptions.IgnoreCase) && Regex.IsMatch(all, @"INVOICE", RegexOptions.IgnoreCase)
            && Regex.IsMatch(all, @"PH[AÂ]N T[IÍ]CH D[ƯU] L[ƯU][ỢO]NG", RegexOptions.IgnoreCase) && !Regex.IsMatch(all, @"nguy[eê]n li[eệ]u \(KHO\)", RegexOptions.IgnoreCase)
            && !Regex.IsMatch(all, @"K[EẾ]T QU[AẢ] L[AẦ]N\s*[12]", RegexOptions.IgnoreCase);
    }

    /// <summary>Đọc 1 file PDF; null nếu không phải phiếu tôm nhập khẩu.</summary>
    public static Phieu? Read(byte[] bytes, string fileName)
    {
        var pages = PdfText.Read(bytes);
        if (!IsNhapKhau(pages)) return null;
        var p = Parse(pages); p.File = fileName; p.Bytes = bytes;
        if (p.Tp.Length == 0) { var m = Regex.Match(fileName, @"TP[\s_\-]*(\d+)", RegexOptions.IgnoreCase); if (m.Success) p.Tp = m.Groups[1].Value; }
        return p;
    }

    private static string DateIso(string s)
    {
        var m = Regex.Match(s ?? "", @"(\d{1,2})[./\-](\d{1,2})[./\-](\d{4})");
        return m.Success ? $"{m.Groups[3].Value}-{m.Groups[2].Value.PadLeft(2, '0')}-{m.Groups[1].Value.PadLeft(2, '0')}" : "";
    }

    public static Phieu Parse(IReadOnlyList<PdfText.Page> pages)
    {
        var res = new Phieu();
        // ---- đầu phiếu (trang 1): ghép chữ theo dòng
        var head = pages[0].Items.Where(t => t.Y > 0).GroupBy(t => Math.Round(t.Y / 3)).OrderByDescending(g => g.Key)
            .Select(g => string.Join(" ", g.OrderBy(t => t.X).Select(t => t.Text))).ToList();
        var text = string.Join("\n", head);
        var inv = Regex.Match(text, @"INVOICE(?:\s*NO)?\s*[:.]?\s*(?<v>[A-Z0-9][A-Z0-9/\-.]*[A-Z0-9])", RegexOptions.IgnoreCase);
        if (inv.Success) res.Invoice = inv.Groups["v"].Value.TrimEnd('.', ',');
        var invDate = Regex.Match(text, @"INVOICE[^\n]*?(?:DATE|DT)\.?\s*:?\s*(\d{1,2}[./\-]\d{1,2}[./\-]\d{4})", RegexOptions.IgnoreCase);
        if (invDate.Success) res.InvDate = DateIso(invDate.Groups[1].Value);
        var con = Regex.Match(text, @"CONTRACT(?:\s*NO)?\s*[:.]?\s*(?<v>[A-Z0-9][A-Z0-9/\-]*[A-Z0-9])", RegexOptions.IgnoreCase);
        if (con.Success) res.Contract = Regex.Replace(con.Groups["v"].Value, @"DATE\d[\d\-]*$", "", RegexOptions.IgnoreCase).TrimEnd('-');
        var tp = Regex.Match(res.Contract, @"^TP[\s\-]*(\d+)", RegexOptions.IgnoreCase);
        if (tp.Success) res.Tp = tp.Groups[1].Value;
        var ct = Regex.Match(text, @"BTP\s+(?:T[ÔO]M\s+)?(?<c>.+?)\s+(?:NH[AẬ]P\b|\d{2,3}/\d{2,3}|INVOICE)", RegexOptions.IgnoreCase);
        if (ct.Success) res.Country = Titled(ct.Groups["c"].Value);
        var tl = head.FirstOrDefault(l => Regex.IsMatch(l, @"BTP", RegexOptions.IgnoreCase));
        res.Title = tl is null ? "" : Regex.Replace(tl, @"\s+", " ").Trim();
        var d = Regex.Match(text, @"Date of analysis\)\s*:?\s*(\d{1,2}[./\-]\d{1,2}[./\-]\d{4})", RegexOptions.IgnoreCase);
        if (d.Success) res.Date = DateIso(d.Groups[1].Value);
        var so = Regex.Match(text, @"S[ốo]\s*\(No\)\s*:?\s*(?<s>\d{1,3}\s*/?\s*KS\s*\d{1,2})", RegexOptions.IgnoreCase);
        if (so.Success) res.So = Regex.Replace(so.Groups["s"].Value, @"\s+", " ").Trim();

        // ---- từng trang: cột theo dòng tiêu đề, mỗi mẫu quanh số thứ tự bên trái
        foreach (var pg in pages)
        {
            var items = pg.Items;
            double Hx(params string[] names)
            {
                var t = items.FirstOrDefault(x => names.Any(n => x.Text.Trim().StartsWith(n, StringComparison.OrdinalIgnoreCase)) && x.Y > 330);
                return t is null ? double.NaN : t.X + t.W / 2;
            }
            var hdrY = items.Where(t => t.Text.Trim() == "Enro").Select(t => t.Y).DefaultIfEmpty(double.NaN).First();
            if (double.IsNaN(hdrY)) continue;
            var cols = new[] { Hx("Enro"), Hx("Cipro"), Hx("Oxy"), Hx("Doxy"), Hx("Sulfo"), Hx("AOZ"), Hx("CAP") };
            if (cols.Any(double.IsNaN)) { res.Issues.Add($"Trang {pg.No}: không thấy đủ 7 cột chỉ tiêu."); continue; }
            PdfText.Tok? H(string name) => items.FirstOrDefault(t => Math.Abs(t.Y - hdrY) < 18 && KhangSinhCore.NormText(t.Text) == KhangSinhCore.NormText(name));
            var kqT = H("KẾT QUẢ"); var dtT = H("Đặt Tính"); var gcT = H("Ghi chú");
            if (kqT is null || dtT is null || gcT is null) { res.Issues.Add($"Trang {pg.No}: không thấy cột Kết quả / Đặt tính / Ghi chú."); continue; }
            double kqC = kqT.X + kqT.W / 2, dtC = dtT.X + dtT.W / 2, gcC = gcT.X + gcT.W / 2;
            double b1 = (cols[6] + kqC) / 2, b2 = (kqC + dtC) / 2, b3 = (dtC + gcC) / 2;
            var lodTok = items.FirstOrDefault(t => t.Text.Trim().StartsWith("LOD", StringComparison.OrdinalIgnoreCase));
            var topY = lodTok is null ? hdrY - 60 : lodTok.Y;
            if (lodTok is not null && res.Lod.Count == 0)
                foreach (var t in items.Where(t => Math.Abs(t.Y - lodTok.Y) < 4 && t.X > cols[0] - 20))
                {
                    var ci = NearestCol(cols, t.X + t.W / 2); if (ci >= 0 && Regex.IsMatch(t.Text, @"^\d+([.,]\d+)?$")) res.Lod[Keys[ci]] = t.Text.Replace(',', '.');
                }
            var anchors = items.Where(t => Regex.IsMatch(t.Text.Trim(), @"^\d{1,3}$") && t.X < 70 && t.Y < topY - 6).OrderByDescending(t => t.Y).ToList();
            if (anchors.Count == 0) continue;
            var minY = anchors[^1].Y - 20;
            var groups = anchors.ToDictionary(a => a, _ => new List<PdfText.Tok>());
            foreach (var t in items.Where(t => t.Y < topY - 6 && t.Y > minY && !anchors.Contains(t)))
            {
                var a = anchors.OrderBy(x => Math.Abs(x.Y - t.Y)).First();
                if (Math.Abs(a.Y - t.Y) <= 18 && t.W < 250) groups[a].Add(t);
            }
            foreach (var a in anchors)
            {
                var g = groups[a];
                var m = new Mau { Stt = int.Parse(a.Text.Trim()) };
                var nameToks = g.Where(t => t.X < 200 && t.X > 45).OrderByDescending(t => Math.Round(t.Y)).ThenBy(t => t.X).ToList();
                var lines = nameToks.GroupBy(t => Math.Round(t.Y / 2)).OrderByDescending(x => x.Key).Select(x => string.Join(" ", x.OrderBy(t => t.X).Select(t => t.Text))).ToList();
                var ten = lines.FirstOrDefault(l => NameRe.IsMatch(l)) ?? lines.FirstOrDefault() ?? "";
                m.Ten = Regex.Replace(ten, @"\s+", " ").Trim();
                var info = lines.FirstOrDefault(l => Regex.IsMatch(l, @"ki[eệ]n", RegexOptions.IgnoreCase)) ?? "";
                var k = Regex.Match(info, @"(\d+)\s*ki[eệ]n", RegexOptions.IgnoreCase); if (k.Success) m.Kien = int.Parse(k.Groups[1].Value);
                var sm = Regex.Match(info, @"M[ẫa]u\s*(\d+)", RegexOptions.IgnoreCase); if (sm.Success) m.Stt = int.Parse(sm.Groups[1].Value);
                var dt = g.FirstOrDefault(t => Regex.IsMatch(t.Text.Trim(), @"^\d{1,2}[./]\d{1,2}[./]\d{4}$"));
                if (dt is not null) m.NgayNhap = DateIso(dt.Text);
                foreach (var t in g.Where(t => t.X > cols[0] - 25 && t.X + t.W / 2 < b1 && Math.Abs(t.Y - a.Y) < 6))
                {
                    var ci = NearestCol(cols, t.X + t.W / 2); if (ci >= 0) m.Inputs[Keys[ci]] = t.Text.Trim();
                }
                string Join(IEnumerable<PdfText.Tok> ts, string sep) => string.Join(sep, ts.OrderByDescending(t => Math.Round(t.Y / 2)).ThenBy(t => t.X).Select(t => t.Text.Trim()));
                m.Kq = Regex.Replace(Join(g.Where(t => t.X + t.W / 2 >= b1 && t.X + t.W / 2 < b2), " "), @"\s+", " ").Trim();
                m.DatTinh = Join(g.Where(t => t.X + t.W / 2 >= b2 && t.X + t.W / 2 < b3), "").Replace(" ", "");
                m.GhiChu = Regex.Replace(Join(g.Where(t => t.X + t.W / 2 >= b3), " "), @"\s+", " ").Trim();
                foreach (var key in Keys) if (!m.Inputs.ContainsKey(key)) m.Inputs[key] = "";
                res.Mau.Add(m);
            }
        }
        res.Mau = res.Mau.OrderBy(m => m.Stt).ToList();
        FillMissingSize(res);
        Check(res);
        return res;
    }

    private static int NearestCol(double[] cols, double x)
    {
        var best = -1; var bd = double.MaxValue;
        for (int i = 0; i < cols.Length; i++) { var d = Math.Abs(cols[i] - x); if (d < bd) { bd = d; best = i; } }
        return bd < 22 ? best : -1;
    }
    private static string Titled(string s) => string.Join(" ", s.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));

    /// <summary>
    /// 1.182: mẫu có ô tên hàng không ghi Size (vd "Ngày 02.02, 60 kiện, Mẫu 28 :" — mẫu lấy thêm theo ngày) mà các mẫu còn lại của phiếu
    /// cùng MỘT hàng + Size → mẫu đó thuộc Size ấy (tên hàng lấy của mẫu khác, chữ gốc chuyển vào Ghi chú, ghi chú đọc ở Issues).
    /// Phiếu có nhiều Size → không đoán (vẫn báo "không đọc được tên hàng / Size"). Trước đây mỗi mẫu như vậy thành 1 "Size" rỗng riêng.
    /// </summary>
    public static void FillMissingSize(Phieu p)
    {
        var known = p.Mau.Where(m => HangSize(m.Ten).Size.Length > 0).ToList();
        var sizes = known.Select(m => HangSize(m.Ten)).Distinct().ToList();
        if (sizes.Count != 1) return;
        var ten = known[0].Ten;
        foreach (var m in p.Mau.Where(m => HangSize(m.Ten).Size.Length == 0))
        {
            var goc = m.Ten.Trim().TrimEnd(':', ' ').Trim();
            m.Ten = ten;
            if (goc.Length > 0) m.GhiChu = (goc + (m.GhiChu.Length > 0 ? " · " + m.GhiChu : "")).Trim();
            p.Issues.Add($"– Mẫu {m.Stt}: tên hàng ghi \"{(goc.Length > 0 ? goc : "(trống)")}\" → lấy theo {sizes[0].Hang} {sizes[0].Size} của phiếu.");
        }
    }

    /// <summary>Kiểm tra sau khi đọc: thiếu thông tin, số mẫu không liên tục, kết quả lệch bảng Quy định.</summary>
    public static void Check(Phieu p, IReadOnlyList<KhangSinhCore.Rule>? rules = null)
    {
        p.Issues.RemoveAll(i => i.StartsWith("•"));
        void Add(string s) => p.Issues.Add("• " + s);
        if (p.Invoice.Length == 0) Add("Không đọc được số INVOICE.");
        if (p.Contract.Length == 0) Add("Không đọc được số CONTRACT.");
        if (p.Date.Length == 0) Add("Không đọc được ngày phân tích.");
        if (p.Mau.Count == 0) { Add("Không đọc được mẫu nào."); return; }
        var stts = p.Mau.Select(m => m.Stt).ToList();
        var miss = Enumerable.Range(1, stts.Max()).Except(stts).ToList();
        if (miss.Count > 0) Add("Thiếu mẫu số: " + string.Join(", ", miss.Take(10)) + (miss.Count > 10 ? "…" : "") + ".");
        var dup = stts.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dup.Count > 0) Add("Trùng mẫu số: " + string.Join(", ", dup) + ".");
        foreach (var m in p.Mau)
        {
            if (m.Ten.Length == 0 || HangSize(m.Ten).Size.Length == 0) Add($"Mẫu {m.Stt}: không đọc được tên hàng / Size.");
            if (m.DatTinh.Length == 0) Add($"Mẫu {m.Stt}: không đọc được Đặc tính.");
            if (m.Inputs.Values.Any(v => v.Length == 0)) Add($"Mẫu {m.Stt}: thiếu giá trị chỉ tiêu.");
            if (m.Kien == 0) Add($"Mẫu {m.Stt}: không đọc được số kiện.");
            if (m.DatTinh.Length > 0)
            {
                int.TryParse(Regex.Match(m.DatTinh, @"^\d+").Value, out var stt);
                var rule = stt > 0 ? (rules ?? KhangSinhCore.DefaultRules()).FirstOrDefault(r => r.Stt == stt) : null;
                if (rule is null) Add($"Mẫu {m.Stt}: Đặc tính \"{m.DatTinh}\" không có trong bảng Quy định.");
                else
                {
                    var best = KhangSinhCore.PickBest(rules ?? KhangSinhCore.DefaultRules(), m.Inputs);
                    if (best is not null && best.Stt != rule.Stt) Add($"Mẫu {m.Stt}: phiếu ghi {m.DatTinh} nhưng chỉ tiêu theo bảng Quy định ra {best.KyHieu}.");
                }
            }
        }
    }

    // ================================================================== bản ghi

    public static string KeyFor(string so, string invoice, string size) => Regex.Replace($"{so}|{invoice}|{size}".ToUpperInvariant(), @"\s+", "");

    /// <summary>Mỗi Size một bản ghi.</summary>
    public static List<JsonObject> ToRecords(Phieu p, string savedAt)
    {
        var o = new List<JsonObject>();
        foreach (var (hang, size, mau) in p.BySize())
            o.Add(new JsonObject
            {
                ["kind"] = "nk", ["so"] = p.So, ["date"] = p.Date, ["country"] = p.Country, ["invoice"] = p.Invoice, ["invDate"] = p.InvDate,
                ["contract"] = p.Contract, ["tp"] = p.Tp, ["hang"] = hang, ["size"] = size, ["file"] = p.File, ["savedAt"] = savedAt,
                ["lod"] = new JsonObject(p.Lod.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))),
                ["mau"] = new JsonArray(mau.Select(m => (JsonNode)m.ToJson()).ToArray()),
            });
        return o;
    }

    /// <summary>Phiếu đọc được → các Size dạng bản ghi (dùng cho tra cứu / thử trước khi lưu).</summary>
    public static List<Nhom> Parse2(Phieu p) => ToRecords(p, "").Select(r => Nhom.Parse(Prefix + "x", r.ToJsonString())!).ToList();

    public static List<Nhom> ParseRows(IEnumerable<(string Key, string Value)> rows, bool history = false) =>
        rows.Where(r => r.Key.StartsWith(history ? HistPrefix : Prefix, StringComparison.Ordinal))
            .Select(r => Nhom.Parse(r.Key, r.Value)).Where(n => n is not null).Cast<Nhom>()
            .OrderByDescending(n => n.Date, StringComparer.Ordinal).ThenBy(n => n.Invoice, StringComparer.Ordinal).ThenBy(n => n.Size, StringComparer.Ordinal).ToList();

    /// <summary>Phiếu mới thay bản cũ khi cùng INV + Contract + Size (phiếu "Cập nhật").</summary>
    public static bool Replaces(Nhom old, JsonObject neu) =>
        Fold(old.Invoice) == Fold(KhangSinhCore.Str(neu["invoice"])) && Fold(old.Contract) == Fold(KhangSinhCore.Str(neu["contract"]))
        && Fold(old.Size) == Fold(KhangSinhCore.Str(neu["size"]));

    // ================================================================== tra cứu

    public static string Fold(string? s) => Regex.Replace(KhangSinhCore.NormText(s), @"[^a-z0-9]", "");

    public sealed record Query(string? Tp, string? Size, List<string> Words)
    {
        public bool Empty => Tp is null && Size is null && Words.Count == 0;
    }

    /// <summary>"TP143 size 31/40" / "tp-143 31/40" / "INV MNSF-155" / "143".</summary>
    public static Query ParseQuery(string? q)
    {
        var s = (q ?? "").Trim(); string? tp = null, size = null;
        var m = Regex.Match(s, @"(?<![A-Za-z0-9])TP[\s_\-]*(\d+)", RegexOptions.IgnoreCase);
        if (m.Success) { tp = m.Groups[1].Value.TrimStart('0'); if (tp.Length == 0) tp = "0"; s = s.Remove(m.Index, m.Length); }
        m = Regex.Match(s, @"(?<!\d)(\d{2,3})\s*/\s*(\d{2,3})(?!\d)");
        if (m.Success) { size = m.Groups[1].Value + "/" + m.Groups[2].Value; s = s.Remove(m.Index, m.Length); }
        else
        {
            m = Regex.Match(s, @"size\s*[:\-]?\s*(\d{2,3})(?!\d)", RegexOptions.IgnoreCase);
            if (m.Success) { size = m.Groups[1].Value; s = s.Remove(m.Index, m.Length); }
        }
        s = Regex.Replace(s, @"\b(size|inv|invoice|contract|hlso|nk)\b", " ", RegexOptions.IgnoreCase);
        var words = s.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(Fold).Where(w => w.Length > 0).ToList();
        return new Query(tp, size, words);
    }

    public static bool Matches(Nhom n, Query q)
    {
        if (q.Empty) return true;
        if (q.Tp is not null && !(n.Tp.TrimStart('0') == q.Tp)) return false;
        if (q.Size is not null)
        {
            var s = Regex.Replace(n.Size, @"\s+", "");
            if (!(s == q.Size || s.StartsWith(q.Size + "/", StringComparison.Ordinal))) return false;
        }
        var hay = Fold(n.Invoice) + "|" + Fold(n.Contract) + "|" + Fold(n.Country) + "|" + Fold(n.Hang) + "|" + Fold(n.So) + "|" + (n.Tp.Length > 0 ? "tp" + n.Tp : "") + "|" + Fold(n.Date);
        foreach (var w in q.Words)
        {
            if (hay.Contains(w, StringComparison.Ordinal)) continue;
            if (Regex.IsMatch(w, @"^\d+$") && n.Tp.TrimStart('0') == w.TrimStart('0')) continue;       // "143" = TP 143
            if (Regex.IsMatch(w, @"^\d+$") && n.Samples.Any(m => m.Stt.ToString() == w)) continue;     // số mẫu
            return false;
        }
        return true;
    }

    public static List<Nhom> Find(IEnumerable<Nhom> all, string? query)
    {
        var q = ParseQuery(query);
        return all.Where(n => !n.IsHistory && Matches(n, q)).ToList();
    }

    /// <summary>Kết quả để điền vào phiếu xuất nội địa: duy nhất 1 Size + mọi mẫu cùng đặc tính + không nhiễm → KsInfo; còn lại → lý do.</summary>
    public sealed record Pick(CargoKs? Ks, string Error, List<Nhom> Hits);
    public sealed record CargoKs(int Stt, string KyHieu, string Dat);
    public static Pick ForCargo(IEnumerable<Nhom> all, string text)
    {
        var q = ParseQuery(text);
        if (q.Empty || (q.Tp is null && q.Words.Count == 0)) return new(null, "", new());
        var hits = Find(all, text);
        if (hits.Count == 0) return new(null, "", hits);
        if (hits.Count > 1)
        {
            var sizes = string.Join(", ", hits.Select(h => $"{h.Hang} {h.Size} ({h.So} · INV {h.Invoice})").Take(6));
            return new(null, $"\"{text.Trim()}\" khớp {hits.Count} phiếu tôm nhập khẩu: {sizes} — gõ thêm Size (vd 31/40) hoặc INV.", hits);
        }
        var n = hits[0]; var t = Summarize(n);
        if (t.Total == 0) return new(null, $"{n.Label}: phiếu chưa có mẫu.", hits);
        if (t.Nhiem > 0 || !t.AllSame)
            return new(null, $"{n.Label} (phiếu {n.So}): {t.Text}" + (t.Nhiem > 0 ? $" — mẫu nhiễm: {t.NhiemText}" : "") + ". Chọn số kết quả bằng ô \"Số kết quả\" (tay).", hits);
        var ky = t.KyHieu!; var stt = int.TryParse(Regex.Match(ky, @"^\d+").Value, out var s) ? s : 0;
        if (stt == 0) return new(null, $"{n.Label}: đặc tính \"{ky}\" không đọc được số.", hits);
        return new(new CargoKs(stt, ky, t.Dat ?? ""), "", hits);
    }

    // ================================================================== điện thoại

    /// <summary>Bản gọn đẩy sang điện thoại (JSON không có tiền tố, bắt đầu bằng "{"): mỗi Size một dòng, không gửi 30 dòng ND.
    /// Trường: v=1, so, d (ngày phân tích), c (nước), inv, invd, ct (contract), tp, h (hàng), sz (size), n (số mẫu), kien, dn (ngày nhập),
    /// g = [[đặc tính, kết quả, số mẫu, [số thứ tự mẫu]]], x = mẫu nhiễm [[stt, kien, [[chỉ tiêu, giá trị]], đặc tính, kết quả, ghi chú]], lod.</summary>
    public static string Compact(string key, string json)
    {
        var n = Nhom.Parse(key, json); if (n is null) return json;
        var t = Summarize(n);
        var g = new JsonArray(t.Groups.Select(x => (JsonNode)new JsonArray(x.KyHieu, x.Dat, x.N, new JsonArray(x.Stts.Select(s => (JsonNode)s).ToArray()))).ToArray());
        var x = new JsonArray(n.Samples.Where(m => m.IsNhiem).Select(m => (JsonNode)new JsonArray(m.Stt, m.Kien,
            new JsonArray(m.Nhiem.Select(z => (JsonNode)new JsonArray(z.Label, z.Value)).ToArray()), m.DatTinh, m.Kq, m.GhiChu)).ToArray());
        var o = new JsonObject
        {
            ["v"] = 1, ["so"] = n.So, ["d"] = n.Date, ["c"] = n.Country, ["inv"] = n.Invoice, ["invd"] = n.InvDate, ["ct"] = n.Contract, ["tp"] = n.Tp,
            ["h"] = n.Hang, ["sz"] = n.Size, ["n"] = t.Total, ["kien"] = t.Kien, ["dn"] = n.NgayNhap, ["g"] = g, ["x"] = x,
        };
        if (n.IsHistory) { o["old"] = 1; o["by"] = n.ReplacedBy; }
        if (n.J["lod"] is JsonObject lod) o["lod"] = lod.DeepClone();
        return o.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }
}
