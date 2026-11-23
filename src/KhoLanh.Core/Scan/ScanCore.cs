namespace KhoLanh.Core.Scan;

/// <summary>Ảnh RGBA 8 bit (không phụ thuộc thư viện đồ hoạ — giải mã / mã hoá ảnh do giao diện làm).</summary>
public sealed class RgbaImage
{
    public readonly int W, H;
    public readonly byte[] Px;
    public RgbaImage(int w, int h, byte[]? px = null) { W = Math.Max(1, w); H = Math.Max(1, h); Px = px ?? new byte[W * H * 4]; }
    public RgbaImage Copy() => new(W, H, (byte[])Px.Clone());
}

public sealed record ScanPoint(double X, double Y);

/// <summary>Thông số xử lý 1 trang: kieu = mau | muc | xam | bw | goc · bong (khử bóng) · trang (độ trắng nền) · dam (độ đậm chữ) · li (xóa ô li) · nhat (làm nhạt dòng kẻ) · gam.</summary>
public sealed record ScanParams(string Kieu, double Bong, double Trang, double Dam, bool Li, double Gam, double Nhat);

public sealed record ScanMode(string Key, string Ten, string Full, string Tip, ScanParams P);

/// <summary>
/// LÕI "Scan tài liệu" (chuyển từ scan-core.js): mọi xử lý chạy trên máy, không gửi ảnh đi đâu.
///  Bước 1 — tự tìm và cắt trang giấy (DetectQuad → Warp): vùng giấy sáng lớn nhất, 4 cạnh khớp đường thẳng, nắn phối cảnh, cắt mép tối.
///  Bước 2 — khử bóng + làm trắng nền (BackgroundMap → Enhance): ước lượng nền bằng phép "đóng" hình thái học rồi chia ra.
///  Bước 3 — bản in: nền trắng, chữ đen, tăng tương phản; xóa / làm nhạt ô li, dòng kẻ (giữ nét chữ cắt ngang).
/// </summary>
public static class ScanCore
{
    public const int MaxSide = 2600;
    public const int PdfMaxPages = 200;
    public const string DefaultMode = "bw";

    public static readonly Dictionary<string, string> Kieu = new() { ["goc"] = "Gốc", ["mau"] = "Màu", ["muc"] = "Màu mực", ["xam"] = "Xám", ["bw"] = "Đen trắng" };

    public static readonly ScanMode[] Modes =
    {
        new("mau", "Màu", "Chế độ tài liệu màu", "Tài liệu màu: khử bóng, làm trắng nền, giữ màu con dấu đỏ / bút xanh / hình ảnh.", new("mau", 100, 50, 35, false, 1, 0)),
        new("bw", "Trắng đen", "Chế độ trắng đen để photo (ưu tiên)", "Ưu tiên để in / photo: nền trắng tuyệt đối, chữ đen đậm, dòng kẻ / ô li làm nhạt để chữ không lẫn với viền.", new("bw", 100, 55, 50, false, 1, 80)),
        new("muc", "Màu mực", "Chế độ giữ màu mực", "Nền trắng tuyệt đối, chữ giữ ĐÚNG MÀU bút (đen / xanh / đỏ) và đậm, rõ nét, dòng kẻ làm nhạt — hợp phiếu viết nhiều màu bút.", new("muc", 100, 55, 50, false, 1, 80)),
        new("tay", "Chữ tay", "Chế độ giữ nét chữ viết tay", "Vở / giấy ghi tay: giữ nét bút (cả nét mờ, bút chì), nền trắng, tự xóa ô li để chữ nổi rõ.", new("xam", 100, 60, 60, true, 0.8, 0)),
        new("tuy", "Tùy chỉnh", "Chế độ tùy chỉnh", "Tự chọn kiểu (Gốc / Màu / Xám / Đen trắng) và chỉnh từng thanh trượt.", new("xam", 80, 50, 50, false, 1, 0)),
    };

    public static ScanMode ModeOf(string? key) => Modes.FirstOrDefault(m => m.Key == key) ?? Modes.First(m => m.Key == (key is "xam" or "goc" ? "tuy" : DefaultMode));

    private static double Clamp(double v, double a, double b) => double.IsNaN(v) ? a : v < a ? a : v > b ? b : v;

    /// <summary>Thông số đầy đủ: mặc định của chế độ, đè bằng p (đã lưu / người dùng chỉnh).</summary>
    public static ScanParams Resolve(string? mode, ScanParams? p)
    {
        var m = ModeOf(mode); var b = m.P;
        if (mode == "goc" && p is null) p = b with { Kieu = "goc" };
        var o = p ?? b;
        return new ScanParams(Kieu.ContainsKey(o.Kieu) ? o.Kieu : b.Kieu, Clamp(o.Bong, 0, 100), Clamp(o.Trang, 0, 100), Clamp(o.Dam, 0, 100), o.Li, Clamp(o.Gam, 0.4, 1.6), Clamp(o.Nhat, 0, 100));
    }

    public static List<ScanPoint> DefaultQuad() => new() { new(0.06, 0.06), new(0.94, 0.06), new(0.94, 0.94), new(0.06, 0.94) };

    // ================================================================== ảnh: thu nhỏ, xoay
    public static (int w, int h) FitSize(int w, int h, int max) { var k = Math.Min(1.0, max / (double)Math.Max(w, h)); return ((int)Math.Round(w * k), (int)Math.Round(h * k)); }

    /// <summary>Thu nhỏ (trung bình vùng — mượt như canvas chất lượng cao); ảnh đã nhỏ hơn max thì trả lại chính nó.</summary>
    public static RgbaImage Scale(RgbaImage src, int max)
    {
        if (max <= 0 || Math.Max(src.W, src.H) <= max) return src;
        var (w, h) = FitSize(src.W, src.H, max); return Resize(src, Math.Max(1, w), Math.Max(1, h));
    }

    public static RgbaImage Resize(RgbaImage src, int w, int h)
    {
        var o = new RgbaImage(w, h); double sx = src.W / (double)w, sy = src.H / (double)h; var s = src.Px; var d = o.Px;
        for (var y = 0; y < h; y++)
        {
            double y0 = y * sy, y1 = (y + 1) * sy; int iy0 = (int)y0, iy1 = Math.Min(src.H - 1, (int)Math.Ceiling(y1) - 1);
            for (var x = 0; x < w; x++)
            {
                double x0 = x * sx, x1 = (x + 1) * sx; int ix0 = (int)x0, ix1 = Math.Min(src.W - 1, (int)Math.Ceiling(x1) - 1);
                double r = 0, g = 0, b = 0, a = 0, wt = 0;
                for (var yy = iy0; yy <= iy1; yy++)
                {
                    var fy = Math.Min(yy + 1, y1) - Math.Max(yy, y0); if (fy <= 0) continue;
                    for (var xx = ix0; xx <= ix1; xx++)
                    {
                        var fx = Math.Min(xx + 1, x1) - Math.Max(xx, x0); if (fx <= 0) continue;
                        var f = fx * fy; var p = (yy * src.W + xx) * 4; r += s[p] * f; g += s[p + 1] * f; b += s[p + 2] * f; a += s[p + 3] * f; wt += f;
                    }
                }
                var q = (y * w + x) * 4; if (wt <= 0) wt = 1;
                d[q] = (byte)(r / wt + 0.5); d[q + 1] = (byte)(g / wt + 0.5); d[q + 2] = (byte)(b / wt + 0.5); d[q + 3] = (byte)(a / wt + 0.5);
            }
        }
        return o;
    }

    /// <summary>Ảnh có kênh trong suốt → trộn lên nền trắng.</summary>
    public static void FlattenOnWhite(RgbaImage img)
    {
        var d = img.Px;
        for (var p = 0; p < d.Length; p += 4)
        {
            var a = d[p + 3]; if (a == 255) continue;
            for (var c = 0; c < 3; c++) d[p + c] = (byte)((d[p + c] * a + 255 * (255 - a)) / 255);
            d[p + 3] = 255;
        }
    }

    public static RgbaImage Rotate(RgbaImage src, int deg)
    {
        deg = ((deg % 360) + 360) % 360; if (deg == 0) return src;
        var swap = deg is 90 or 270; var o = new RgbaImage(swap ? src.H : src.W, swap ? src.W : src.H);
        for (var y = 0; y < src.H; y++)
            for (var x = 0; x < src.W; x++)
            {
                int nx, ny;
                switch (deg) { case 90: nx = src.H - 1 - y; ny = x; break; case 180: nx = src.W - 1 - x; ny = src.H - 1 - y; break; default: nx = y; ny = src.W - 1 - x; break; }
                Buffer.BlockCopy(src.Px, (y * src.W + x) * 4, o.Px, (ny * o.W + nx) * 4, 4);
            }
        return o;
    }

    // ================================================================== bộ lọc dùng chung
    private static float[] BoxBlur(float[] a, int w, int h, int r)
    {
        var t = new float[a.Length]; var o = new float[a.Length]; var win = 2 * r + 1;
        for (var y = 0; y < h; y++)
        {
            var row = y * w; double s = 0;
            for (var x = -r; x <= r; x++) s += a[row + Math.Min(w - 1, Math.Max(0, x))];
            for (var x = 0; x < w; x++) { t[row + x] = (float)(s / win); s += a[row + Math.Min(w - 1, x + r + 1)] - a[row + Math.Max(0, x - r)]; }
        }
        for (var x = 0; x < w; x++)
        {
            double s = 0;
            for (var y = -r; y <= r; y++) s += t[Math.Min(h - 1, Math.Max(0, y)) * w + x];
            for (var y = 0; y < h; y++) { o[y * w + x] = (float)(s / win); s += t[Math.Min(h - 1, y + r + 1) * w + x] - t[Math.Max(0, y - r) * w + x]; }
        }
        return o;
    }

    private static float[] RankFilter(float[] a, int w, int h, int R, bool isMax)
    {
        var t = new float[a.Length]; var o = new float[a.Length];
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
        {
            var m = a[y * w + x];
            for (var k = -R; k <= R; k++) { var q = x + k; q = q < 0 ? 0 : q >= w ? w - 1 : q; var v = a[y * w + q]; if (isMax ? v > m : v < m) m = v; }
            t[y * w + x] = m;
        }
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
        {
            var m = t[y * w + x];
            for (var k = -R; k <= R; k++) { var q = y + k; q = q < 0 ? 0 : q >= h ? h - 1 : q; var v = t[q * w + x]; if (isMax ? v > m : v < m) m = v; }
            o[y * w + x] = m;
        }
        return o;
    }

    private static byte[] LumOf(byte[] d, int n)
    {
        var lum = new byte[n];
        for (int i = 0, p = 0; i < n; i++, p += 4) lum[i] = (byte)((d[p] * 77 + d[p + 1] * 150 + d[p + 2] * 29) >> 8);
        return lum;
    }

    // ================================================================== BƯỚC 1 — tự tìm 4 góc trang giấy
    private sealed record Paper(List<double[]> Hull, double HullArea, double[][] Rough, double R, double A);

    private static Paper? AnalyzePaper(float[] f, int w, int h, double T)
    {
        var n = w * h; var mask = new byte[n]; var bright = 0;
        for (var i = 0; i < n; i++) if (f[i] > T) { mask[i] = 1; bright++; }
        if (bright > n * 0.985 || bright < n * 0.12) return null;
        var lab = new int[n]; var stack = new int[n]; int bestLab = 0, bestSize = 0, cur = 0;
        for (var i = 0; i < n; i++)
        {
            if (mask[i] == 0 || lab[i] != 0) continue;
            cur++; var sp = 0; var size = 0; stack[sp++] = i; lab[i] = cur;
            while (sp > 0)
            {
                var q = stack[--sp]; size++; var cx = q % w; var cy = q / w;
                if (cx > 0 && mask[q - 1] != 0 && lab[q - 1] == 0) { lab[q - 1] = cur; stack[sp++] = q - 1; }
                if (cx < w - 1 && mask[q + 1] != 0 && lab[q + 1] == 0) { lab[q + 1] = cur; stack[sp++] = q + 1; }
                if (cy > 0 && mask[q - w] != 0 && lab[q - w] == 0) { lab[q - w] = cur; stack[sp++] = q - w; }
                if (cy < h - 1 && mask[q + w] != 0 && lab[q + w] == 0) { lab[q + w] = cur; stack[sp++] = q + w; }
            }
            if (size > bestSize) { bestSize = size; bestLab = cur; }
        }
        if (bestSize < n * 0.12) return null;
        var pts = new List<double[]>();
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
        {
            var i = y * w + x; if (lab[i] != bestLab) continue;
            if (x == 0 || y == 0 || x == w - 1 || y == h - 1 || lab[i - 1] != bestLab || lab[i + 1] != bestLab || lab[i - w] != bestLab || lab[i + w] != bestLab) pts.Add(new[] { x + 0.5, y + 0.5 });
        }
        var hull = ConvexHull(pts); if (hull.Count < 4) return null;
        var hullArea = PolyArea(hull); double[] tl = hull[0], tr = hull[0], br = hull[0], bl = hull[0];
        foreach (var P in hull.Skip(1))
        {
            if (P[0] + P[1] < tl[0] + tl[1]) tl = P;
            if (P[0] - P[1] > tr[0] - tr[1]) tr = P;
            if (P[0] + P[1] > br[0] + br[1]) br = P;
            if (P[0] - P[1] < bl[0] - bl[1]) bl = P;
        }
        var rough = new[] { tl, tr, br, bl }; var qa = PolyArea(rough);
        if (qa == 0) return null;
        return new Paper(hull, hullArea, rough, Math.Min(1, qa / hullArea), hullArea / n);
    }

    // Dò giấy theo MÀU: giấy gần như không màu còn bàn gỗ / vải có màu rõ. Mốc so sánh = vùng giữa ảnh.
    private static Paper? PaperByColor(byte[] sd, byte[] lum, int w, int h)
    {
        var n = w * h; var S = new float[n]; var L = new float[n];
        for (int i = 0, p = 0; i < n; i++, p += 4)
        {
            int mx = Math.Max(sd[p], Math.Max(sd[p + 1], sd[p + 2])), mn = Math.Min(sd[p], Math.Min(sd[p + 1], sd[p + 2]));
            S[i] = mx > 0 ? (mx - mn) / (float)mx * 255 : 0; L[i] = lum[i];
        }
        S = BoxBlur(BoxBlur(S, w, h, 2), w, h, 2); L = BoxBlur(BoxBlur(L, w, h, 2), w, h, 2);
        var hs = new int[256]; var hl = new int[256]; var cnt = 0;
        for (var y = (int)Math.Floor(h * 0.3); y < (int)Math.Ceiling(h * 0.7); y++) for (var x = (int)Math.Floor(w * 0.3); x < (int)Math.Ceiling(w * 0.7); x++) { var i = y * w + x; hs[Math.Min(255, (int)S[i])]++; hl[Math.Min(255, (int)L[i])]++; cnt++; }
        int acc = 0, sMed = 0, lMed = 0;
        for (var v = 0; v < 256; v++) { acc += hs[v]; if (acc >= cnt / 2.0) { sMed = v; break; } }
        acc = 0; for (var v = 0; v < 256; v++) { acc += hl[v]; if (acc >= cnt / 2.0) { lMed = v; break; } }
        if (sMed > 70 || lMed < 60) return null;
        double sT = Math.Min(75, Math.Max(30, sMed + 26)), lT = lMed * 0.55; var f = new float[n]; var bright = 0;
        for (var i = 0; i < n; i++) { f[i] = S[i] < sT && L[i] > lT ? 255 : 0; if (f[i] > 0) bright++; }
        if (bright > n * 0.93 || bright < n * 0.15) return null;
        var cd = AnalyzePaper(f, w, h, 128);
        return cd is not null && cd.R >= 0.93 && cd.A < 0.93 ? cd : null;
    }

    // Dò giấy theo ĐỘ SÁNG: Otsu 2 lớp nền tối / giấy sáng, thử nhiều ngưỡng rồi chọn tờ giấy "vuông vức" nhất.
    private static Paper? PaperByLum(float[] f, int w, int h, int n)
    {
        var hist = new long[256]; for (var i = 0; i < n; i++) hist[(int)f[i]]++;
        double sumAll = 0; for (var t = 0; t < 256; t++) sumAll += t * (double)hist[t];
        double wB = 0, sumB = 0, best = -1, mBest = 0, mFbest = 0;
        for (var t = 0; t < 256; t++)
        {
            wB += hist[t]; if (wB == 0) continue; var wF = n - wB; if (wF == 0) break;
            sumB += t * (double)hist[t]; var mB = sumB / wB; var mF = (sumAll - sumB) / wF;
            var v = wB * wF * (mB - mF) * (mB - mF);
            if (v > best) { best = v; mBest = mB; mFbest = mF; }
        }
        if (mFbest - mBest < 30) return null;
        Paper? bestC = null; double bestScore = -1;
        foreach (var fr in new[] { 0.04, 0.1, 0.18, 0.26, 0.34, 0.42 })
        {
            var cd = AnalyzePaper(f, w, h, mBest + (mFbest - mBest) * fr); if (cd is null) continue;
            var sc = cd.R * cd.R * cd.R + 0.3 * cd.A;
            if (cd.R >= 0.93 && sc > bestScore) { bestScore = sc; bestC = cd; }
        }
        return bestC;
    }

    /// <summary>4 góc trang giấy [TL, TR, BR, BL] theo tỉ lệ 0–1 của ảnh, hoặc null (không thấy rìa giấy → người dùng chỉnh tay).</summary>
    public static List<ScanPoint>? DetectQuad(RgbaImage src)
    {
        var sm = Scale(src, 360); int w = sm.W, h = sm.H, n = w * h;
        if (w < 24 || h < 24) return null;
        var sd = sm.Px; var lum0 = LumOf(sd, n); var f = new float[n];
        for (var i = 0; i < n; i++) f[i] = lum0[i];
        f = BoxBlur(BoxBlur(f, w, h, 2), w, h, 2);
        var bestC = PaperByColor(sd, lum0, w, h) ?? PaperByLum(f, w, h, n);
        if (bestC is null) return null;
        var hull = bestC.Hull; var rough = bestC.Rough; double diag = Math.Max(w, h); var fitted = true;
        var samples = new List<double[]>();
        for (var k = 0; k < hull.Count; k++)
        {
            var A = hull[k]; var B = hull[(k + 1) % hull.Count]; var len = Math.Max(1, Math.Sqrt((B[0] - A[0]) * (B[0] - A[0]) + (B[1] - A[1]) * (B[1] - A[1]))); var steps = (int)Math.Ceiling(len);
            for (var u = 0; u < steps; u++) samples.Add(new[] { A[0] + (B[0] - A[0]) * u / steps, A[1] + (B[1] - A[1]) * u / steps });
        }
        var lines = new List<(double x, double y, double dx, double dy)>();
        for (var s = 0; s < 4; s++)
        {
            var S0 = rough[s]; var S1 = rough[(s + 1) % 4]; double dx = S1[0] - S0[0], dy = S1[1] - S0[1], L = Math.Sqrt(dx * dx + dy * dy);
            if (L < 8) { fitted = false; break; }
            double ux = dx / L, uy = dy / L; var sel = new List<double[]>();
            foreach (var smp in samples)
            {
                double px = smp[0] - S0[0], py = smp[1] - S0[1], tt = (px * ux + py * uy) / L, dd = Math.Abs(px * uy - py * ux);
                if (tt > 0.15 && tt < 0.85 && dd < diag * 0.05) sel.Add(smp);
            }
            if (sel.Count < 8) { fitted = false; break; }
            double mx = sel.Average(p => p[0]), my = sel.Average(p => p[1]), sxx = 0, syy = 0, sxy = 0;
            foreach (var p in sel) { double ex = p[0] - mx, ey = p[1] - my; sxx += ex * ex; syy += ey * ey; sxy += ex * ey; }
            var ang = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
            lines.Add((mx, my, Math.Cos(ang), Math.Sin(ang)));
        }
        var quad = rough.Select(p => new[] { p[0], p[1] }).ToArray();
        if (fitted && lines.Count == 4)
        {
            var cand = new List<double[]>(); var ok = true;
            for (var s = 0; s < 4; s++)
            {
                var l1 = lines[(s + 3) % 4]; var l2 = lines[s]; var den = l1.dx * l2.dy - l1.dy * l2.dx;
                if (Math.Abs(den) < 1e-3) { ok = false; break; }
                var tq = ((l2.x - l1.x) * l2.dy - (l2.y - l1.y) * l2.dx) / den;
                cand.Add(new[] { l1.x + l1.dx * tq, l1.y + l1.dy * tq });
            }
            if (ok) { var ar = PolyArea(cand); if (ar > bestC.HullArea * 0.85 && ar < bestC.HullArea * 1.3) quad = cand.ToArray(); }
        }
        quad = quad.Select(p => new[] { Math.Min(w, Math.Max(0, p[0])), Math.Min(h, Math.Max(0, p[1])) }).ToArray();
        var qa = PolyArea(quad);
        if (qa < n * 0.15 || qa > n * 0.985 || !IsConvex(quad)) return null;
        var q0 = quad.Select(p => new ScanPoint(p[0] / w, p[1] / h)).ToList();
        var qt = TrimDarkEdges(src, q0); const double m = 0.008;
        return new() { QuadPoint(qt, m, m), QuadPoint(qt, 1 - m, m), QuadPoint(qt, 1 - m, 1 - m), QuadPoint(qt, m, 1 - m) };
    }

    private static double PolyArea(IList<double[]> p) { double a = 0; for (var i = 0; i < p.Count; i++) { var j = (i + 1) % p.Count; a += p[i][0] * p[j][1] - p[j][0] * p[i][1]; } return Math.Abs(a) / 2; }

    private static bool IsConvex(double[][] p)
    {
        var sg = 0;
        for (var i = 0; i < 4; i++)
        {
            var a = p[i]; var b = p[(i + 1) % 4]; var c = p[(i + 2) % 4]; var z = (b[0] - a[0]) * (c[1] - b[1]) - (b[1] - a[1]) * (c[0] - b[0]);
            if (z == 0) return false; var s = z > 0 ? 1 : -1; if (sg == 0) sg = s; else if (s != sg) return false;
        }
        return true;
    }

    private static List<double[]> ConvexHull(List<double[]> pts)
    {
        var sorted = pts.OrderBy(p => p[0]).ThenBy(p => p[1]).ToList();
        static double Cross(double[] o, double[] a, double[] b) => (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]);
        var u = new List<double[]>();
        foreach (var p in sorted) { while (u.Count >= 2 && Cross(u[^2], u[^1], p) <= 0) u.RemoveAt(u.Count - 1); u.Add(p); }
        var l = new List<double[]>();
        for (var i = sorted.Count - 1; i >= 0; i--) { var p = sorted[i]; while (l.Count >= 2 && Cross(l[^2], l[^1], p) <= 0) l.RemoveAt(l.Count - 1); l.Add(p); }
        u.RemoveAt(u.Count - 1); l.RemoveAt(l.Count - 1); u.AddRange(l); return u;
    }

    /// <summary>Điểm (u, v) ∈ [0,1]² của tứ giác q (nội suy song tuyến giữa 4 góc).</summary>
    public static ScanPoint QuadPoint(IList<ScanPoint> q, double u, double v)
    {
        double tx = q[0].X + (q[1].X - q[0].X) * u, ty = q[0].Y + (q[1].Y - q[0].Y) * u, bx = q[3].X + (q[2].X - q[3].X) * u, by = q[3].Y + (q[2].Y - q[3].Y) * u;
        return new ScanPoint(tx + (bx - tx) * v, ty + (by - ty) * v);
    }

    // Cắt nốt mép còn tối (gáy lò xo, viền bàn sót lại): tối đa 12% mỗi cạnh.
    private static List<ScanPoint> TrimDarkEdges(RgbaImage src, List<ScanPoint> q)
    {
        var sm = Warp(src, q, 240); int w = sm.W, h = sm.H, n = w * h;
        if (w < 40 || h < 40) return q;
        var lum = LumOf(sm.Px, n); var hist = new int[256]; var cnt = 0;
        for (var y = (int)Math.Floor(h * 0.25); y < (int)Math.Ceiling(h * 0.75); y++) for (var x = (int)Math.Floor(w * 0.25); x < (int)Math.Ceiling(w * 0.75); x++) { hist[lum[y * w + x]]++; cnt++; }
        int acc = 0, med = 255; for (var v = 0; v < 256; v++) { acc += hist[v]; if (acc >= cnt / 2.0) { med = v; break; } }
        double dk = med * 0.3, FR = 0.4;
        double RowDark(int yy) { var c = 0; for (var xx = 0; xx < w; xx++) if (lum[yy * w + xx] < dk) c++; return c / (double)w; }
        double ColDark(int xx) { var c = 0; for (var yy = 0; yy < h; yy++) if (lum[yy * w + xx] < dk) c++; return c / (double)h; }
        int t = 0, b = 0, l = 0, r = 0, mh = (int)Math.Floor(h * 0.12), mw = (int)Math.Floor(w * 0.12);
        while (t < mh && RowDark(t) > FR) t++;
        while (b < mh && RowDark(h - 1 - b) > FR) b++;
        while (l < mw && ColDark(l) > FR) l++;
        while (r < mw && ColDark(w - 1 - r) > FR) r++;
        t++; b++; l++; r++;
        double u0 = l / (double)w, u1 = 1 - r / (double)w, v0 = t / (double)h, v1 = 1 - b / (double)h;
        return new() { QuadPoint(q, u0, v0), QuadPoint(q, u1, v0), QuadPoint(q, u1, v1), QuadPoint(q, u0, v1) };
    }

    /// <summary>Nắn thẳng góc: cắt tứ giác q (4 góc 0–1: TL, TR, BR, BL) thành ảnh chữ nhật (biến đổi phối cảnh, nội suy song tuyến).</summary>
    public static RgbaImage Warp(RgbaImage src, IList<ScanPoint> q, int maxSide = MaxSide)
    {
        int W0 = src.W, H0 = src.H; var P = q.Select(p => (x: p.X * W0, y: p.Y * H0)).ToArray();
        static double Dist((double x, double y) a, (double x, double y) b) => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));
        double ow = (Dist(P[0], P[1]) + Dist(P[3], P[2])) / 2, oh = (Dist(P[0], P[3]) + Dist(P[1], P[2])) / 2;
        var k = Math.Min(1, (maxSide > 0 ? maxSide : MaxSide) / Math.Max(ow, Math.Max(oh, 1)));
        int w = Math.Max(2, (int)Math.Round(ow * k)), h = Math.Max(2, (int)Math.Round(oh * k));
        var dst = new (double x, double y)[] { (0, 0), (w, 0), (w, h), (0, h) }; var A = new double[8][];
        for (var i = 0; i < 4; i++)
        {
            double x = dst[i].x, y = dst[i].y, X = P[i].x, Y = P[i].y;
            A[2 * i] = new[] { x, y, 1, 0, 0, 0, -x * X, -y * X, X }; A[2 * i + 1] = new[] { 0, 0, 0, x, y, 1, -x * Y, -y * Y, Y };
        }
        for (var i = 0; i < 8; i++)
        {
            var piv = i; for (var j = i + 1; j < 8; j++) if (Math.Abs(A[j][i]) > Math.Abs(A[piv][i])) piv = j;
            (A[i], A[piv]) = (A[piv], A[i]);
            var dv = A[i][i]; if (Math.Abs(dv) < 1e-12) return Scale(src, maxSide);
            for (var c = i; c < 9; c++) A[i][c] /= dv;
            for (var j = 0; j < 8; j++) if (j != i) { var fct = A[j][i]; if (fct != 0) for (var c = i; c < 9; c++) A[j][c] -= fct * A[i][c]; }
        }
        double h0 = A[0][8], h1 = A[1][8], h2 = A[2][8], h3 = A[3][8], h4 = A[4][8], h5 = A[5][8], h6 = A[6][8], h7 = A[7][8];
        var o = new RgbaImage(w, h); var od = o.Px; var sd = src.Px;
        Parallel.For(0, h, yy =>
        {
            var py = yy + 0.5; var p4 = yy * w * 4;
            for (var xx = 0; xx < w; xx++, p4 += 4)
            {
                var px = xx + 0.5; var den = h6 * px + h7 * py + 1; var sx = (h0 * px + h1 * py + h2) / den - 0.5; var sy = (h3 * px + h4 * py + h5) / den - 0.5;
                if (sx < 0) sx = 0; else if (sx > W0 - 1) sx = W0 - 1; if (sy < 0) sy = 0; else if (sy > H0 - 1) sy = H0 - 1;
                int x0 = (int)sx, y0 = (int)sy, x1 = x0 + 1 < W0 ? x0 + 1 : x0, y1 = y0 + 1 < H0 ? y0 + 1 : y0; double tx = sx - x0, ty = sy - y0;
                int i00 = (y0 * W0 + x0) * 4, i10 = (y0 * W0 + x1) * 4, i01 = (y1 * W0 + x0) * 4, i11 = (y1 * W0 + x1) * 4;
                for (var c = 0; c < 3; c++)
                {
                    double a0 = sd[i00 + c] + (sd[i10 + c] - sd[i00 + c]) * tx, a1 = sd[i01 + c] + (sd[i11 + c] - sd[i01 + c]) * tx;
                    od[p4 + c] = (byte)(a0 + (a1 - a0) * ty + 0.5);
                }
                od[p4 + 3] = 255;
            }
        });
        return o;
    }

    // ================================================================== BƯỚC 2 — khử bóng, làm trắng nền
    private sealed record Bg(float[] G, int Gw, int Gh, int F);

    private static Bg BackgroundMap(byte[] lum, int W, int H)
    {
        var M = Math.Max(W, H); var f = Math.Max(1, (int)Math.Round(M / 600.0)); int gw = (W + f - 1) / f, gh = (H + f - 1) / f;
        var g = new float[gw * gh]; var cn = new float[gw * gh];
        for (var y = 0; y < H; y++) { var gr = y / f * gw; for (var x = 0; x < W; x++) { var gi = gr + x / f; g[gi] += lum[y * W + x]; cn[gi]++; } }
        for (var gi = 0; gi < g.Length; gi++) g[gi] /= cn[gi];
        var R = Math.Max(2, (int)Math.Round(0.006 * M / f));
        g = RankFilter(RankFilter(g, gw, gh, R, true), gw, gh, R, false);
        g = BoxBlur(g, gw, gh, 1);
        for (var gi = 0; gi < g.Length; gi++) if (g[gi] < 40) g[gi] = 40;
        return new Bg(g, gw, gh, f);
    }

    // Mặt nạ Ô LI / DÒNG KẺ: đoạn ngang / dọc dài ≥ 12% cạnh ảnh và MẢNH. sep = trả riêng ngang / dọc (không khôi phục điểm sát nét chữ).
    private static (byte[] h, byte[] v) LineMaskSep(byte[] nv, int W, int H, double thr) { var (_, lh, lv) = LineMaskCore(nv, W, H, thr, true); return (lh!, lv!); }
    private static byte[] LineMask(byte[] nv, int W, int H, double thr) => LineMaskCore(nv, W, H, thr, false).rm;

    private static (byte[] rm, byte[]? lh, byte[]? lv) LineMaskCore(byte[] nv, int W, int H, double thr, bool sep)
    {
        var n = W * H; var ink = new byte[n]; var lm = new byte[n]; var lh = sep ? new byte[n] : null; var lv = sep ? new byte[n] : null;
        for (var i = 0; i < n; i++) ink[i] = (byte)(nv[i] < thr ? 1 : 0);
        int T = Math.Max(3, (int)Math.Round(Math.Max(W, H) * 0.004)), Lh = Math.Max(60, (int)Math.Round(W * 0.12)), Lv = Math.Max(60, (int)Math.Round(H * 0.12));
        void Run(Func<int, bool> get, int len, int lineLen, Func<int, bool> nbA, Func<int, bool> nbB, Action<int> set)
        {
            var k = 0;
            while (k < len)
            {
                if (!get(k)) { k++; continue; }
                int st = k, en = k, gap = 0; k++;
                while (k < len) { if (get(k)) { en = k; gap = 0; } else if (++gap > 4) break; k++; }
                if (en - st + 1 >= lineLen)
                {
                    var thick = 0; for (var j = st; j <= en; j++) if (nbA(j) || nbB(j)) thick++;
                    if (thick < (en - st + 1) * 0.4) for (var j = st; j <= en; j++) set(j);
                }
                k = en + 1;
            }
        }
        for (var y = 0; y < H; y++)
        {
            int row = y * W, ya = y - (T + 1), yb = y + (T + 1);
            Run(k => ink[row + k] != 0, W, Lh, k => ya >= 0 && ink[ya * W + k] != 0, k => yb < H && ink[yb * W + k] != 0, k => { lm[row + k] = 1; if (lh is not null) lh[row + k] = 1; });
        }
        for (var x = 0; x < W; x++)
        {
            int xx = x, xa = x - (T + 1), xb = x + (T + 1);
            Run(k => ink[k * W + xx] != 0, H, Lv, k => xa >= 0 && ink[k * W + xa] != 0, k => xb < W && ink[k * W + xb] != 0, k => { lm[k * W + xx] = 1; if (lv is not null) lv[k * W + xx] = 1; });
        }
        if (sep) return (lm, lh, lv);
        var rm = (byte[])lm.Clone();
        for (var y = 0; y < H; y++) for (var x = 0; x < W; x++)
        {
            var i = y * W + x; if (lm[i] == 0) continue;
            if ((x > 0 && ink[i - 1] != 0 && lm[i - 1] == 0) || (x < W - 1 && ink[i + 1] != 0 && lm[i + 1] == 0) || (y > 0 && ink[i - W] != 0 && lm[i - W] == 0) || (y < H - 1 && ink[i + W] != 0 && lm[i + W] == 0)) rm[i] = 0;
        }
        return (rm, null, null);
    }

    private static byte[] DilateBin(byte[] a, int W, int H, int rx, int? ryN = null)
    {
        var ry = ryN ?? rx; var t = new byte[a.Length]; var o = new byte[a.Length];
        for (var y = 0; y < H; y++)
        {
            var row = y * W; var c = 0;
            for (var x = 0; x <= rx && x < W; x++) c += a[row + x];
            for (var x = 0; x < W; x++) { t[row + x] = (byte)(c > 0 ? 1 : 0); int hi = x + rx + 1, lo = x - rx; if (hi < W) c += a[row + hi]; if (lo >= 0) c -= a[row + lo]; }
        }
        for (var x = 0; x < W; x++)
        {
            var c = 0;
            for (var y = 0; y <= ry && y < H; y++) c += t[y * W + x];
            for (var y = 0; y < H; y++) { o[y * W + x] = (byte)(c > 0 ? 1 : 0); int hi = y + ry + 1, lo = y - ry; if (hi < H) c += t[hi * W + x]; if (lo >= 0) c -= t[lo * W + x]; }
        }
        return o;
    }

    private static byte[] ThinLines(byte[] thin, int W, int H, bool horiz, int minLen, int rr)
    {
        var n = W * H; var br = horiz ? DilateBin(thin, W, H, rr, 1) : DilateBin(thin, W, H, 1, rr);
        var lab = new int[n]; var stack = new int[n]; var cur = 0; var good = new List<bool> { false }; var outp = new byte[n];
        for (var i = 0; i < n; i++)
        {
            if (br[i] == 0 || lab[i] != 0) continue;
            cur++; var sp = 0; stack[sp++] = i; lab[i] = cur; int x0 = i % W, x1 = x0, y0 = i / W, y1 = y0;
            while (sp > 0)
            {
                var q = stack[--sp]; int cx = q % W, cy = q / W;
                if (cx < x0) x0 = cx; if (cx > x1) x1 = cx; if (cy < y0) y0 = cy; if (cy > y1) y1 = cy;
                for (var d = 0; d < 8; d++)
                {
                    var nx = cx + (d is 0 or 3 or 5 ? -1 : d is 2 or 4 or 7 ? 1 : 0); var ny = cy + (d < 3 ? -1 : d > 4 ? 1 : 0);
                    if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                    if (br[ny * W + nx] != 0 && lab[ny * W + nx] == 0) { lab[ny * W + nx] = cur; stack[sp++] = ny * W + nx; }
                }
            }
            int len = horiz ? x1 - x0 + 1 : y1 - y0 + 1, wid = horiz ? y1 - y0 + 1 : x1 - x0 + 1;
            good.Add(len >= minLen && wid <= Math.Max(6, len * 0.12));
        }
        for (var i = 0; i < n; i++) if (thin[i] != 0 && good[lab[i]]) outp[i] = 1;
        return outp;
    }

    // Mặt nạ DÒNG KẺ CẦN LÀM NHẠT: dòng dài liền (trừ chỗ nét chữ cắt qua) + dòng mảnh đứt đoạn / chấm chấm. tt = dưới mức này là mực chữ thật.
    private static byte[] FadeMask(byte[] nv, int W, int H, double thr, double tt)
    {
        var n = W * H; var (lh, lv) = LineMaskSep(nv, W, H, thr); var fm = new byte[n]; var un = new byte[n]; var tx = new byte[n];
        var M = Math.Max(W, H); int T = Math.Max(3, (int)Math.Round(M * 0.004)), maxT = T * 2 + 6, R = Math.Max(5, (int)Math.Round(T * 0.7));
        var tmax = Math.Max(1, (int)Math.Round(M / 1300.0)); var ink = new byte[n]; var vt = new byte[n]; var ht = new byte[n];
        var thrT = Math.Min(thr, tt + 40);
        for (var i = 0; i < n; i++) ink[i] = (byte)(nv[i] < thrT ? 1 : 0);
        for (var x = 0; x < W; x++) { var y = 0; while (y < H) { if (ink[y * W + x] == 0) { y++; continue; } var st = y; while (y < H && ink[y * W + x] != 0) y++; if (y - st <= tmax) for (var k = st; k < y; k++) vt[k * W + x] = 1; } }
        for (var y = 0; y < H; y++) { var x = 0; while (x < W) { if (ink[y * W + x] == 0) { x++; continue; } var st = x; while (x < W && ink[y * W + x] != 0) x++; if (x - st <= tmax) for (var k = st; k < x; k++) ht[y * W + k] = 1; } }
        int rr = Math.Max(5, (int)Math.Round(M * 0.003)), Lh = Math.Max(48, (int)Math.Round(W * 0.02)), Lv = Math.Max(48, (int)Math.Round(H * 0.02));
        var lhT = ThinLines(vt, W, H, true, Lh, rr); var lvT = ThinLines(ht, W, H, false, Lv, rr);
        var z0 = new byte[n]; for (var i = 0; i < n; i++) z0[i] = (byte)(lh[i] | lv[i]);
        z0 = DilateBin(z0, W, H, 1);
        var core = new byte[n];
        for (var y = 1; y < H - 1; y++) for (var x = 1; x < W - 1; x++)
        {
            var i = y * W + x;
            if (ink[i] != 0 && z0[i] == 0 && ink[i - 1] != 0 && ink[i + 1] != 0 && ink[i - W] != 0 && ink[i + W] != 0 && ink[i - W - 1] != 0 && ink[i - W + 1] != 0 && ink[i + W - 1] != 0 && ink[i + W + 1] != 0) core[i] = 1;
        }
        var prot = DilateBin(core, W, H, Math.Max(2, (int)Math.Round(M * 0.0008)));
        for (var i = 0; i < n; i++) if (prot[i] != 0) { lhT[i] = 0; lvT[i] = 0; }
        for (var i = 0; i < n; i++) un[i] = (byte)(lh[i] | lv[i] | lhT[i] | lvT[i]);
        var zone = DilateBin(un, W, H, 2);
        for (var i = 0; i < n; i++) tx[i] = (byte)(nv[i] < tt && zone[i] == 0 && !(vt[i] != 0 && ht[i] != 0) ? 1 : 0);
        bool Hit(int x0, int x1, int y0, int y1)
        {
            if (x0 < 0) x0 = 0; if (y0 < 0) y0 = 0; if (x1 > W - 1) x1 = W - 1; if (y1 > H - 1) y1 = H - 1;
            for (var yy = y0; yy <= y1; yy++) for (var xx = x0; xx <= x1; xx++) if (tx[yy * W + xx] != 0) return true;
            return false;
        }
        for (var y = 0; y < H; y++) for (var x = 0; x < W; x++)
        {
            var i = y * W + x;
            if ((lhT[i] | lvT[i]) != 0) { fm[i] = 1; continue; }
            if ((lh[i] | lv[i]) == 0) continue;
            var fade = false;
            if (lh[i] != 0)
            {
                var a = y; while (a > 0 && y - a < maxT && lh[(a - 1) * W + x] != 0) a--;
                var b = y; while (b < H - 1 && b - y < maxT && lh[(b + 1) * W + x] != 0) b++;
                if (!(Hit(x - 1, x + 1, a - 1 - R, a - 1) && Hit(x - 1, x + 1, b + 1, b + 1 + R))) fade = true;
            }
            if (!fade && lv[i] != 0)
            {
                var a = x; while (a > 0 && x - a < maxT && lv[y * W + a - 1] != 0) a--;
                var b = x; while (b < W - 1 && b - x < maxT && lv[y * W + b + 1] != 0) b++;
                if (!(Hit(a - 1 - R, a - 1, y - 1, y + 1) && Hit(b + 1, b + 1 + R, y - 1, y + 1))) fade = true;
            }
            if (fade) fm[i] = 1;
        }
        var hs = new int[256]; var cnt = 0;
        for (var i = 0; i < n; i++) if (fm[i] != 0) { hs[nv[i]]++; cnt++; }
        var darkCut = 0;
        if (cnt > 50) { int acc = 0, med = 0; for (; med < 255; med++) { acc += hs[med]; if (acc >= cnt / 2.0) break; } darkCut = (int)Math.Round(med * 0.72); if (darkCut > tt) darkCut = (int)tt; }
        var outp = DilateBin(fm, W, H, 1);
        if (darkCut > 0) for (var i = 0; i < n; i++) if (outp[i] != 0 && nv[i] < darkCut) outp[i] = 0;
        return outp;
    }

    // ================================================================== BƯỚC 3 — bản in
    /// <summary>Làm trắng nền, chữ đen, tăng tương phản (sửa trực tiếp ảnh c).</summary>
    public static RgbaImage Enhance(RgbaImage c, ScanParams p)
    {
        int W = c.W, H = c.H, n = W * H; var d = c.Px; var lum = LumOf(d, n);
        bool mui = p.Kieu == "muc", color = p.Kieu == "mau" || mui, bw = p.Kieu == "bw";
        var s = p.Bong / 100; var bg = s > 0 ? BackgroundMap(lum, W, H) : null;
        var nv = new byte[n]; var kk = color ? new float[n] : null;
        Parallel.For(0, H, y =>
        {
            int r0 = 0, r1 = 0; double ty = 0;
            if (bg is not null)
            {
                var fy = (y + 0.5) / bg.F - 0.5; if (fy < 0) fy = 0; if (fy > bg.Gh - 1) fy = bg.Gh - 1;
                var y0 = (int)fy; var y1 = Math.Min(bg.Gh - 1, y0 + 1); ty = fy - y0; r0 = y0 * bg.Gw; r1 = y1 * bg.Gw;
            }
            for (var x = 0; x < W; x++)
            {
                double k = 1;
                if (bg is not null)
                {
                    var fx = (x + 0.5) / bg.F - 0.5; if (fx < 0) fx = 0; if (fx > bg.Gw - 1) fx = bg.Gw - 1;
                    var x0 = (int)fx; var x1 = Math.Min(bg.Gw - 1, x0 + 1); var tx = fx - x0; var g = bg.G;
                    double top = g[r0 + x0] + (g[r0 + x1] - g[r0 + x0]) * tx, bot = g[r1 + x0] + (g[r1 + x1] - g[r1 + x0]) * tx, b = top + (bot - top) * ty;
                    b = 255 + (b - 255) * s; k = 255 / b;
                }
                var i = y * W + x; var v = lum[i] * k; nv[i] = (byte)(v > 255 ? 255 : v);
                if (kk is not null) kk[i] = (float)k;
            }
        });
        double hi = 250 - 0.6 * p.Trang, lo = 30 + 1.1 * p.Dam; if (lo > hi - 12) lo = hi - 12;
        var lut = new byte[256]; var lutc = new byte[256]; double th = lo + 0.68 * (hi - lo), sw = 0.16 * (hi - lo), loC = lo * 0.45;
        double m0 = 1 - hi / 255, m1 = 1 - (lo + 0.45 * (hi - lo)) / 255; if (m1 < m0 + 0.05) m1 = m0 + 0.05;
        for (var v = 0; v < 256; v++)
        {
            var t = (v - lo) / (hi - lo); t = t < 0 ? 0 : t > 1 ? 1 : t; if (p.Gam != 1) t = Math.Pow(t, p.Gam);
            if (bw) { t = (v - (th - sw)) / (2 * sw); t = t < 0 ? 0 : t > 1 ? 1 : t; lut[v] = (byte)Math.Round(255 * t * t * (3 - 2 * t), MidpointRounding.AwayFromZero); }
            else lut[v] = (byte)Math.Round(255 * t, MidpointRounding.AwayFromZero);
            t = (v - loC) / (hi - loC); t = t < 0 ? 0 : t > 1 ? 1 : t; lutc[v] = (byte)Math.Round(255 * t, MidpointRounding.AwayFromZero);
        }
        var rm = p.Li ? LineMask(nv, W, H, Math.Min(250, hi + 22)) : null;
        var fm = !p.Li && p.Nhat > 0 ? FadeMask(nv, W, H, Math.Min(250, hi + 22), Math.Min(200, lo + 0.45 * (hi - lo))) : null;
        var fl = (int)Math.Round(90 + 165 * p.Nhat / 100);
        double[] wbal = { 1, 1, 1 };
        if (color)
        {
            var hst = new int[256]; for (var i = 0; i < n; i += 3) hst[nv[i]]++;
            var tot = 0; var cut = 255; var need = Math.Ceiling(n / 3.0) * 0.3; for (; cut > 0; cut--) { tot += hst[cut]; if (tot >= need) break; }
            double sr = 0, sg = 0, sb = 0, sl = 0;
            for (int i = 0, p4 = 0; i < n; i += 3, p4 += 12) if (nv[i] >= cut) { sr += d[p4] * kk![i]; sg += d[p4 + 1] * kk[i]; sb += d[p4 + 2] * kk[i]; sl += nv[i]; }
            if (sl > 0) { static double Cl(double q) => q < 0.7 ? 0.7 : q > 1.3 ? 1.3 : q; wbal = new[] { Cl(sr / sl), Cl(sg / sl), Cl(sb / sl) }; }
        }
        Parallel.For(0, H, y =>
        {
            for (int i = y * W, o4 = y * W * 4, e = i + W; i < e; i++, o4 += 4)
            {
                if (rm is not null && rm[i] != 0) { d[o4] = d[o4 + 1] = d[o4 + 2] = 255; continue; }
                if (mui)
                {
                    double D0 = 1 - Math.Min(255, d[o4] * kk![i] / wbal[0]) / 255, D1 = 1 - Math.Min(255, d[o4 + 1] * kk[i] / wbal[1]) / 255, D2 = 1 - Math.Min(255, d[o4 + 2] * kk[i] / wbal[2]) / 255;
                    if (D0 < 0) D0 = 0; if (D1 < 0) D1 = 0; if (D2 < 0) D2 = 0;
                    double dm = Math.Max(D0, Math.Max(D1, D2)), gn = 0;
                    if (dm > m0) { var t = (dm - m0) / (m1 - m0); t = t > 1 ? 1 : t; if (p.Gam != 1) t = Math.Pow(t, p.Gam); gn = t * t * (3 - 2 * t); gn /= dm; }
                    D0 *= gn; D1 *= gn; D2 *= gn;
                    d[o4] = (byte)Math.Round(255 * (1 - (D0 > 1 ? 1 : D0)), MidpointRounding.AwayFromZero); d[o4 + 1] = (byte)Math.Round(255 * (1 - (D1 > 1 ? 1 : D1)), MidpointRounding.AwayFromZero); d[o4 + 2] = (byte)Math.Round(255 * (1 - (D2 > 1 ? 1 : D2)), MidpointRounding.AwayFromZero);
                }
                else if (color)
                {
                    for (var ch = 0; ch < 3; ch++) { var v = d[o4 + ch] * kk![i] / wbal[ch]; v = v > 255 ? 255 : v < 0 ? 0 : v; d[o4 + ch] = lutc[(int)v]; }
                }
                else { var v = lut[nv[i]]; d[o4] = d[o4 + 1] = d[o4 + 2] = v; }
                if (fm is not null && fm[i] != 0) { if (d[o4] < fl) d[o4] = (byte)fl; if (d[o4 + 1] < fl) d[o4 + 1] = (byte)fl; if (d[o4 + 2] < fl) d[o4 + 2] = (byte)fl; }
            }
        });
        return c;
    }

    /// <summary>Xử lý 1 trang → ảnh mới: cắt (quad) → xoay → làm sạch theo chế độ.</summary>
    public static RgbaImage Process(RgbaImage src, IList<ScanPoint>? quad, int rot, string mode, ScanParams? p, int maxSide = MaxSide)
    {
        var c = quad is { Count: 4 } ? Warp(src, quad, maxSide) : Scale(src, maxSide);
        c = Rotate(c, rot);
        if (ReferenceEquals(c, src)) c = src.Copy();
        var pp = Resolve(mode, p);
        return pp.Kieu == "goc" ? c : Enhance(c, pp);
    }
}
