namespace KhoLanh.Core.Container3D;

/// <summary>
/// TÍNH CBM / XẾP PALLET (chuyển từ c3d-cbm.js): CBM từng mặt hàng, số thùng / pallet (cắt guillotine trên mặt pallet),
/// số pallet cần, số pallet cont chứa được (sàn × số tầng chồng). Mỗi mặt hàng 1 pallet riêng, không trộn.
/// </summary>
public static class C3dCbm
{
    private const double EPS = 1e-6;

    public sealed class Pallet { public double L = 1200, W = 1000, Base = 150, MaxH = 1800, MaxKg = 1000; }

    public static double CbmBox(C3dSku k) => k.L * k.W * k.H / 1e9;

    /// <summary>Số hình chữ nhật a×b (cho xoay 90°) nhét được trong W×L, cắt guillotine (quy hoạch động trên các điểm cắt).</summary>
    public static int Pack2D(double W, double L, double a, double b)
    {
        if (a <= 0 || b <= 0) return 0;
        double[] Pts(double D)
        {
            var s = new SortedSet<double> { 0 };
            for (var i = 0; i * a <= D + EPS; i++) for (var j = 0; i * a + j * b <= D + EPS; j++) s.Add(Math.Floor((i * a + j * b) * 1000 + 0.5) / 1000);
            return s.ToArray();
        }
        var px = Pts(W); var py = Pts(L); var memo = new Dictionary<(double, double), int>();
        static double Down(double[] arr, double v) { double r = 0; foreach (var x in arr) { if (x <= v + EPS) r = x; else break; } return r; }
        int F(double w, double l)
        {
            w = Down(px, w); l = Down(py, l);
            if (w < Math.Min(a, b) - EPS || l < Math.Min(a, b) - EPS) return 0;
            if (memo.TryGetValue((w, l), out var m)) return m;
            var best = Math.Max((int)Math.Floor((w + EPS) / a) * (int)Math.Floor((l + EPS) / b), (int)Math.Floor((w + EPS) / b) * (int)Math.Floor((l + EPS) / a));
            foreach (var x in px) { if (x <= EPS || x >= w - EPS) continue; if (x > w / 2 + EPS) break; best = Math.Max(best, F(x, l) + F(w - x, l)); }
            foreach (var y in py) { if (y <= EPS || y >= l - EPS) continue; if (y > l / 2 + EPS) break; best = Math.Max(best, F(w, y) + F(w, l - y)); }
            memo[(w, l)] = best; return best;
        }
        return F(W, L);
    }

    public sealed class PalletPlan
    {
        public bool Ok; public string Why = "", Vertical = "", Limited = "";
        public int PerPallet, PerLayer, Layers, FullLayers, Pallets, LastPallet;
        public double H, A, B, Height, KgPerPallet;
    }

    public static PalletPlan PlanPallet(C3dSku k, Pallet pal)
    {
        var dims = new[] { k.L, k.W, k.H }; var best = new PalletPlan(); var seen = new HashSet<string>(); var avail = pal.MaxH - pal.Base;
        for (var v = 0; v < 3; v++)
        {
            double h = dims[v], a = dims[(v + 1) % 3], b = dims[(v + 2) % 3];
            var sig = Math.Min(a, b) + "x" + Math.Max(a, b) + "|" + h; if (!seen.Add(sig)) continue;
            var layers = (int)Math.Floor((avail + EPS) / h); if (layers < 1) continue;
            var per = Pack2D(pal.W, pal.L, a, b); if (per < 1) continue;
            var n = per * layers; var byKg = int.MaxValue; if (pal.MaxKg > 0 && k.Kg > 0) byKg = (int)Math.Floor(pal.MaxKg / k.Kg);
            var limited = byKg < n ? "kg" : ""; n = Math.Min(n, byKg); if (n < 1) continue;
            if (n > best.PerPallet || (n == best.PerPallet && h < (best.H > 0 ? best.H : 1e18)))
            {
                var used = Math.Min(layers, (int)Math.Ceiling(n / (double)per));
                best = new PalletPlan { PerPallet = n, PerLayer = per, Layers = used, FullLayers = layers, H = h, A = a, B = b, Vertical = "LWH"[v].ToString(), Limited = limited, Height = pal.Base + used * h };
            }
        }
        if (best.PerPallet == 0) return new PalletPlan { Ok = false, Why = "Thùng không vừa pallet (quá cao / quá nặng)." };
        best.Ok = true; best.Pallets = (int)Math.Ceiling(k.Qty / (double)best.PerPallet); best.LastPallet = k.Qty - (best.Pallets - 1) * best.PerPallet;
        best.KgPerPallet = best.PerPallet * k.Kg;
        return best;
    }

    public sealed record Floor(int FloorCount, int Tiers, int Total, double Lu, double Wu, double EffH);

    public static Floor FloorPlan(C3dContainer cont, Pallet pal, double palH)
    {
        var Lu = cont.L - cont.ClearFront - cont.ClearRear; var Wu = cont.W - 2 * cont.ClearSide;
        var red = cont.RedLine > 0 ? cont.RedLine : cont.H; var effH = Math.Min(cont.H, Math.Min(red, cont.DoorH > 0 ? cont.DoorH : cont.H));
        var floor = Pack2D(Wu, Lu, pal.W, pal.L); var tiers = palH > 0 ? Math.Max(1, (int)Math.Floor((effH + EPS) / palH)) : 1;
        return new Floor(floor, tiers, floor * tiers, Lu, Wu, effH);
    }

    public sealed class Summary
    {
        public List<(C3dSku k, PalletPlan plan)> Items = new();
        public int TotalPallets, Bad, Qty;
        public double Tallest, CbmTotal, Kg, ContVol, FillPct;
        public Floor Floor = null!;
        public bool KgOver, PalletsFit;
    }

    public static Summary Summarize(IEnumerable<C3dSku> skus, C3dContainer cont, Pallet pal)
    {
        var all = skus.ToList(); var s = new Summary();
        s.Items = all.Where(k => k.Qty > 0 && k.L > 0 && k.W > 0 && k.H > 0).Select(k => (k, PlanPallet(k, pal))).ToList();
        s.TotalPallets = s.Items.Sum(x => x.plan.Ok ? x.plan.Pallets : 0);
        s.Tallest = s.Items.Where(x => x.plan.Ok).Select(x => x.plan.Height).DefaultIfEmpty(0).Max();
        s.Floor = FloorPlan(cont, pal, s.Tallest);
        s.CbmTotal = all.Sum(k => CbmBox(k) * k.Qty); s.Kg = all.Sum(k => k.Kg * k.Qty); s.Qty = all.Sum(k => k.Qty);
        s.ContVol = cont.L * cont.W * Math.Min(cont.H, cont.RedLine > 0 ? cont.RedLine : cont.H) / 1e9;
        s.FillPct = s.ContVol > 0 ? s.CbmTotal / s.ContVol * 100 : 0;
        s.KgOver = cont.MaxPayload > 0 && s.Kg > cont.MaxPayload; s.PalletsFit = s.TotalPallets <= s.Floor.Total; s.Bad = s.Items.Count(x => !x.plan.Ok);
        return s;
    }
}
