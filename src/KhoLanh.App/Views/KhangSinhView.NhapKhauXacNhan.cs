using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using KhoLanh.App.Controls;
using KhoLanh.Core.Grid;
using KhoLanh.Core.KhangSinh;
using static KhoLanh.Core.KhangSinh.KhangSinhCore;
using Nk = KhoLanh.Core.KhangSinh.KhangSinhNhapKhau;

namespace KhoLanh.App.Views;

/// <summary>
/// 1.183 — Hộp xác nhận nạp tôm nhập khẩu có KIỂM SOÁT (người dùng: "cho sửa giá trị các cột trước khi nạp", "làm sao biết app đọc đúng hay sai",
/// "kiểm soát đúng sai khi nạp nhiều file"): mỗi Size 1 dòng · cột Tình trạng (xanh đọc đủ / vàng cần xem / đỏ thiếu thông tin cốt lõi — không cho nạp) ·
/// ô định danh sửa được tại chỗ (Phiếu, Ngày PT, Nước, INVOICE, Contract, TP, Hàng, Size; ô đã sửa tô xanh dương, rê chuột hiện giá trị đọc từ PDF) ·
/// "Mở mẫu" sửa từng mẫu (số mẫu, đặc tính, kiện, chỉ tiêu) · "Xem file gốc" mở PDF · cột "Mẫu đọc" so số mẫu đọc với số mẫu ghi trong file ·
/// ô Duyệt: dòng xanh tích sẵn, vàng phải tự tích, đỏ không tích được · chỉ nạp các dòng đã duyệt.
/// </summary>
public sealed partial class KhangSinhView
{
    private sealed class NkRow
    {
        public int Pi; public Nk.Phieu P = null!; public string Hang = "", Size = ""; public List<Nk.Mau> Mau = new();
        public string Key => Pi + "#" + (Mau.Count == 0 ? 0 : Mau.Min(m => m.Stt));
        public int Lv; public string Why = "";
    }
    // 0 Duyệt (ô tích của lưới) · cột dữ liệu:
    private const int CTt = 0, CFile = 1, CSo = 2, CNgay = 3, CNuoc = 4, CInv = 5, CCt = 6, CTp = 7, CHang = 8, CSize = 9, CMauDoc = 10, CMau = 11, CKien = 12, CNhiem = 13, CNap = 14;

    internal async Task<List<Nk.Phieu>?> NkConfirm(List<Nk.Phieu> list)
    {
        var orig = list;
        var work = list.Select(p => p.Clone()).ToList();
        foreach (var p in work) Nk.Check(p, Svc.Loaded ? Svc.Rules : null);
        var conf = new List<(Nk.Nhom Old, string NewSo, string Size)>();
        var approved = new Dictionary<string, bool>();
        var seen = new HashSet<string>();
        var rows = new List<NkRow>(); var vis = new List<NkRow>();
        var onlyCheck = new CheckBox { Content = "Chỉ dòng cần xem (vàng / đỏ)", FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
        var model = new GridModel();
        var cols = new List<GridColumn>
        {
            new("tt", "Tình trạng", 190) { ReadOnly = true }, new("file", "File", 150) { ReadOnly = true }, new("so", "Phiếu", 76), new("d", "Ngày PT", 86), new("c", "Nước", 70),
            new("inv", "INVOICE", 140), new("ct", "Contract", 140), new("tp", "TP", 56), new("h", "Hàng", 96), new("sz", "Size", 62),
            new("md", "Mẫu đọc", 70) { ReadOnly = true }, new("mau", "Mẫu · đặc tính", 200) { ReadOnly = true }, new("kien", "Kiện", 60) { ReadOnly = true },
            new("nhiem", "Mẫu nhiễm", 170) { ReadOnly = true }, new("nap", "Nạp", 130) { ReadOnly = true },
        };
        SheetGrid grid = null!;
        var head = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13.5, Margin = new Thickness(0, 0, 0, 6) };
        var hint = new TextBlock { Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
            Text = "Bấm đúp ô để sửa Phiếu / Ngày / Nước / INVOICE / Contract / TP / Hàng / Size (ô đã sửa tô xanh dương; rê chuột xem giá trị đọc từ PDF). \"Mở mẫu\" sửa từng mẫu. Dòng xanh được tích sẵn; dòng vàng phải xem rồi tự tích; dòng đỏ thiếu thông tin cốt lõi — sửa xong mới nạp được." };
        var warnTxt = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };
        var warnBox = new Border
        {
            BorderBrush = AppTheme.Brush("#D8DEE6"), Background = AppTheme.Brush("#F6F8FA"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6), Margin = new Thickness(0, 8, 0, 0),
            Child = new ScrollViewer { MaxHeight = 120, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = warnTxt },
        };
        var ok = new Button { IsDefault = true, Classes = { "primary" } };
        var no = new Button { Content = "Đóng", IsCancel = true };
        var btnMau = new Button { Content = "Mở mẫu…" };
        var btnPdf = new Button { Content = "Xem file gốc" };
        var btnUndo = new Button { Content = "Hoàn lại dòng đọc từ PDF" };
        var building = false;

        string[] Cells(NkRow r, List<(Nk.Nhom Old, string NewSo, string Size)> cf)
        {
            var p = r.P;
            var n = Nk.Nhom.Parse("x", Nk.ToRecords(p.Clone(m => r.Mau.Contains(m)), "").FirstOrDefault()?.ToJsonString() ?? "{}");
            var t = n is null ? null : Nk.Summarize(n);
            var old = cf.FirstOrDefault(c => c.NewSo == p.So && c.Size == r.Size);
            return new[]
            {
                (r.Lv == 0 ? "✔ " : r.Lv == 1 ? "⚠ " : "✖ ") + r.Why, p.File, p.So, Dmy(p.Date), p.Country, p.Invoice, p.Contract, p.Tp, r.Hang, r.Size,
                Nk.MauDoc(p), t?.Text ?? "", t is null ? "" : t.Kien.ToString("#,0", Ui.Vi), t is { Nhiem: > 0 } ? t.NhiemText : "",
                r.Lv == 2 ? "— (sửa trước)" : old.Old is not null ? $"Thay phiếu {old.Old.So}" : "Mới",
            };
        }

        List<NkRow> Build(List<Nk.Phieu> src)
        {
            var o = new List<NkRow>();
            for (int i = 0; i < src.Count; i++)
                foreach (var (h, sz, ms) in src[i].BySize())
                {
                    var (lv, why) = Nk.Status(src[i], h, sz, ms);
                    o.Add(new NkRow { Pi = i, P = src[i], Hang = h, Size = sz, Mau = ms, Lv = lv, Why = why });
                }
            return o;
        }
        var origCells = new Dictionary<string, string[]>();
        var origRows = Build(orig); foreach (var r in origRows) origCells[r.Key] = Cells(r, new());
        // 1 file nhiều Size: khóa theo số mẫu đầu — sau khi sửa Size vẫn tìm lại được giá trị gốc

        void Reload()
        {
            building = true;
            var keepSel = grid is null || grid.Row < 0 || grid.Row >= vis.Count ? null : vis[grid.Row].Key;
            rows = Build(work);
            conf = Svc.Loaded ? Svc.NkConflicts(work) : new();
            foreach (var r in rows)
            {
                if (r.Lv == 2) approved[r.Key] = false;
                else if (!approved.ContainsKey(r.Key)) approved[r.Key] = r.Lv == 0;
                seen.Add(r.Key);
            }
            vis = onlyCheck.IsChecked == true ? rows.Where(r => r.Lv > 0).ToList() : rows;
            model.Load(cols, vis.Select(r => (IEnumerable<string?>)Cells(r, conf)));
            if (grid is not null)
            {
                grid.Refresh();
                grid.SetChecked(Enumerable.Range(0, vis.Count).Where(i => approved.GetValueOrDefault(vis[i].Key)), true);
                var k = keepSel is null ? -1 : vis.FindIndex(r => r.Key == keepSel);
                if (k >= 0) grid.Select(k, 0);
            }
            Summary();
            building = false;
        }

        void Summary()
        {
            var tick = rows.Count(r => approved.GetValueOrDefault(r.Key));
            int xanh = rows.Count(r => r.Lv == 0), vang = rows.Count(r => r.Lv == 1), @do = rows.Count(r => r.Lv == 2);
            var mau = rows.Where(r => approved.GetValueOrDefault(r.Key)).Sum(r => r.Mau.Count);
            var nhiem = rows.Count(r => approved.GetValueOrDefault(r.Key) && r.Mau.Any(m => m.IsNhiem));
            head.Text = $"{work.Count} file · {rows.Count} dòng (mỗi Size 1 dòng): {xanh} đọc đủ · {vang} cần xem · {@do} thiếu thông tin · đã duyệt {tick} dòng / {mau:#,0} mẫu"
                + (nhiem > 0 ? $" · {nhiem} dòng có mẫu nhiễm" : "");
            ok.Content = $"Nạp {tick} dòng đã duyệt";
            ok.IsEnabled = tick > 0;
            var w = new System.Text.StringBuilder();
            foreach (var p in work.Where(p => p.Issues.Count > 0 || p.Edits.Count > 0))
            {
                w.AppendLine("■ " + p.File);
                foreach (var i in p.Issues) w.AppendLine("   " + i);
                foreach (var e in p.Edits) w.AppendLine("   ✎ đã sửa tay: " + e);
            }
            warnTxt.Text = w.Length == 0 ? "Đọc đủ thông tin, không có cảnh báo." : w.ToString().TrimEnd();
            warnBox.Background = AppTheme.Brush(vang + @do > 0 ? "#FFF2D6" : "#F6F8FA");
            warnBox.BorderBrush = AppTheme.Brush(vang + @do > 0 ? "#E0B25C" : "#D8DEE6");
        }

        grid = new SheetGrid(model)
        {
            SpareRows = 0, MinRows = 3, RowChecks = true,
            CellBackground = (r, c) =>
            {
                if (r < 0 || r >= vis.Count) return null;
                var v = vis[r];
                if (c == CTt) return v.Lv == 0 ? AppTheme.Brush("#E3F4EA") : v.Lv == 1 ? AppTheme.Brush("#FFF2D6") : AppTheme.Brush("#FDE8E6");
                if (c == CMauDoc && v.P.Mau.Count < v.P.MaxStt) return AppTheme.Brush("#FDE8E6");
                if ((c == CMau || c == CNhiem) && v.Mau.Any(m => m.IsNhiem)) return AppTheme.Brush("#FFF2D6");
                if (c is >= CSo and <= CSize && origCells.TryGetValue(v.Key, out var oc) && oc[c] != model.Get(r, c)) return AppTheme.Brush("#DCEBFF");
                return null;
            },
            CellTip = (r, c) =>
            {
                if (r < 0 || r >= vis.Count) return null;
                var v = vis[r];
                if (c == CTt) return v.Why;
                if (c is >= CSo and <= CSize && origCells.TryGetValue(v.Key, out var oc) && oc[c] != model.Get(r, c)) return $"Đã sửa tay — đọc từ PDF: \"{oc[c]}\"";
                return null;
            },
        };
        model.Changed += () =>
        {
            if (building) return;
            // người dùng vừa sửa 1 ô định danh → ghi vào phiếu rồi tính lại
            for (int r = 0; r < vis.Count && r < model.RowCount; r++)
            {
                var v = vis[r];
                var cur = Cells(v, conf);
                for (int c = CSo; c <= CSize; c++)
                {
                    var nv = model.Get(r, c).Trim();
                    if (nv == (cur[c] ?? "")) continue;
                    var p = v.P;
                    string? err = null; var was = cur[c];
                    switch (c)
                    {
                        case CSo: p.So = nv; break;
                        case CNgay: { var d = Nk.ParseDate(nv); if (d is null) err = "Ngày không hiểu (gõ 25.11.2026 hoặc 25/11/2026)."; else p.Date = d; break; }
                        case CNuoc: p.Country = nv; break;
                        case CInv: p.Invoice = nv; break;
                        case CCt: p.Contract = nv; break;
                        case CTp: p.Tp = nv.TrimStart('0').Length == 0 ? nv : nv.TrimStart('0'); break;
                        case CHang: Nk.SetHangSize(v.Mau, nv, v.Size); break;
                        case CSize: Nk.SetHangSize(v.Mau, v.Hang, System.Text.RegularExpressions.Regex.Replace(nv, @"\s+", "")); break;
                    }
                    if (err is null) p.Edits.Add($"{cols[c].Header}: {(was.Length == 0 ? "(trống)" : was)} → {(nv.Length == 0 ? "(trống)" : nv)}");
                    Nk.Check(p, Svc.Loaded ? Svc.Rules : null);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => { Reload(); if (err is not null) _ = Ui.Message(this, err); });
                    return;
                }
            }
        };
        grid.ChecksChanged += () =>
        {
            if (building) return;
            var on = grid.CheckedRows.ToHashSet();
            var fix = new List<int>();
            for (int i = 0; i < vis.Count; i++)
            {
                var tick = on.Contains(i);
                if (tick && vis[i].Lv == 2) { fix.Add(i); continue; }
                approved[vis[i].Key] = tick;
            }
            if (fix.Count > 0) { building = true; grid.SetChecked(fix, false); building = false; warnTxt.Text = "Dòng đỏ (thiếu thông tin cốt lõi) chưa nạp được — sửa ô còn thiếu trước."; }
            else Summary();
        };

        NkRow? Cur() => grid.Row >= 0 && grid.Row < vis.Count ? vis[grid.Row] : null;
        var w2 = new Window
        {
            Title = "Nạp phiếu tôm nhập khẩu", Width = 1280, Height = 640, MinWidth = 640, MinHeight = 380, CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, Background = AppTheme.Card,
        };
        btnPdf.Click += (_, _) =>
        {
            var r = Cur(); if (r is null) { warnTxt.Text = "Chọn một dòng trước."; return; }
            try
            {
                var f = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KhoLanh_xem_" + System.IO.Path.GetFileName(r.P.File));
                if (!f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) f += ".pdf";
                File.WriteAllBytes(f, r.P.Bytes); Ui.OpenExternal(f);
            }
            catch (Exception ex) { warnTxt.Text = "Không mở được file gốc: " + ex.Message; }
        };
        btnUndo.Click += (_, _) =>
        {
            var r = Cur(); if (r is null) { warnTxt.Text = "Chọn một dòng trước."; return; }
            var o = orig[r.Pi]; work[r.Pi] = o.Clone(); Nk.Check(work[r.Pi], Svc.Loaded ? Svc.Rules : null); Reload();
        };
        btnMau.Click += async (_, _) =>
        {
            var r = Cur(); if (r is null) { warnTxt.Text = "Chọn một dòng trước."; return; }
            if (await NkEditMau(w2, r.P, r.Mau)) { Nk.Check(r.P, Svc.Loaded ? Svc.Rules : null); Reload(); }
        };
        onlyCheck.IsCheckedChanged += (_, _) => Reload();
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 6) };
        bar.Children.Add(btnMau); bar.Children.Add(btnPdf); bar.Children.Add(btnUndo); bar.Children.Add(onlyCheck);

        var body = new DockPanel();
        var top = new StackPanel(); top.Children.Add(head); top.Children.Add(hint); top.Children.Add(bar);
        DockPanel.SetDock(top, Dock.Top); body.Children.Add(top);
        var btns = Ui.Buttons(no, ok); DockPanel.SetDock(btns, Dock.Bottom); body.Children.Add(btns);
        DockPanel.SetDock(warnBox, Dock.Bottom); body.Children.Add(warnBox);
        body.Children.Add(grid);
        w2.Content = new Border { Padding = new Thickness(18, 14), Child = body };
        if (Ui.Owner(this) is { } owner)
        {
            var sc = owner.Screens.ScreenFromVisual(owner)?.WorkingArea;
            if (sc is { } a) { var k = owner.RenderScaling; w2.Width = Math.Min(w2.Width, a.Width / k - 40); w2.Height = Math.Min(w2.Height, a.Height / k - 60); }
        }
        Reload();
        List<Nk.Phieu>? result = null;
        ok.Click += (_, _) =>
        {
            var ch = new List<Nk.Phieu>();
            for (int i = 0; i < work.Count; i++)
            {
                var keep = rows.Where(r => r.Pi == i && approved.GetValueOrDefault(r.Key)).SelectMany(r => r.Mau).ToHashSet();
                if (keep.Count > 0) ch.Add(work[i].Clone(m => keep.Contains(m)));
            }
            _nkSkipped = rows.Count(r => !approved.GetValueOrDefault(r.Key));
            result = ch; w2.Close();
        };
        no.Click += (_, _) => w2.Close();
        LastNkDialog = w2;
        await Ui.Show(this, w2);
        return result;
    }

    /// <summary>Sửa từng mẫu của 1 Size: số mẫu, đặc tính, kiện, ngày nhập, các chỉ tiêu. true = có thay đổi.</summary>
    private async Task<bool> NkEditMau(Window owner, Nk.Phieu p, List<Nk.Mau> mau)
    {
        var heads = new[] { "Mẫu", "Đặc tính", "Kiện", "Ngày nhập" }.Concat(FieldKeys.Select(k => Fields.First(f => f.Key == k).Label)).ToArray();
        var cols = heads.Select((h, i) => new GridColumn("c" + i, h, i == 1 ? 110 : i == 3 ? 100 : 62)).ToList();
        string[] Row(Nk.Mau m) => new[] { m.Stt.ToString(), m.DatTinh, m.Kien.ToString(), Dmy(m.NgayNhap) }.Concat(FieldKeys.Select(k => m.Inputs.GetValueOrDefault(k) ?? "")).ToArray();
        var model = new GridModel();
        model.Load(cols, mau.Select(m => (IEnumerable<string?>)Row(m)));
        var grid = new SheetGrid(model) { SpareRows = 0, MinRows = 4, RowChecks = false };
        var ok = new Button { Content = "Áp dụng", IsDefault = true, Classes = { "primary" } };
        var no = new Button { Content = "Đóng", IsCancel = true };
        var note = new TextBlock { Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
            Text = $"Phiếu {p.So} · {mau.Count} mẫu. Sửa trực tiếp trên ô: chỉ tiêu gõ ND / *** / số (vd 0.5). Kiện là số nguyên. Ngày nhập gõ 25.11.2026." };
        var body = new DockPanel();
        DockPanel.SetDock(note, Dock.Top); body.Children.Add(note);
        var btns = Ui.Buttons(no, ok); DockPanel.SetDock(btns, Dock.Bottom); body.Children.Add(btns);
        body.Children.Add(grid);
        var w = new Window
        {
            Title = "Sửa mẫu — " + p.File, Width = 980, Height = 520, MinWidth = 600, MinHeight = 300, CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, Background = AppTheme.Card,
            Content = new Border { Padding = new Thickness(18, 14), Child = body },
        };
        var changed = false; string? err = null;
        ok.Click += (_, _) =>
        {
            var ed = new List<string>(); var tmp = mau.Select(m => m.Clone()).ToList();
            for (int r = 0; r < tmp.Count; r++)
            {
                var m = tmp[r];
                if (!int.TryParse(model.Get(r, 0).Trim(), out var stt) || stt <= 0) { err = $"Dòng {r + 1}: số mẫu phải là số nguyên dương."; break; }
                if (!int.TryParse(model.Get(r, 2).Trim().Replace(",", ""), out var kien) || kien < 0) { err = $"Mẫu {stt}: số kiện phải là số nguyên."; break; }
                var d = Nk.ParseDate(model.Get(r, 3));
                if (d is null) { err = $"Mẫu {stt}: ngày nhập không hiểu."; break; }
                m.Stt = stt; m.DatTinh = model.Get(r, 1).Trim(); m.Kien = kien; m.NgayNhap = d;
                for (int k = 0; k < FieldKeys.Length; k++) m.Inputs[FieldKeys[k]] = model.Get(r, 4 + k).Trim();
            }
            if (err is null)
            {
                if (tmp.Select(m => m.Stt).Distinct().Count() != tmp.Count) err = "Có số mẫu bị trùng.";
                else
                {
                    for (int r = 0; r < tmp.Count; r++)
                    {
                        var a = Row(mau[r]); var b = Row(tmp[r]);
                        if (!a.SequenceEqual(b)) ed.Add($"mẫu {mau[r].Stt}" + (mau[r].Stt != tmp[r].Stt ? $"→{tmp[r].Stt}" : "") + ": " + string.Join(", ", Enumerable.Range(0, heads.Length).Where(i => a[i] != b[i]).Select(i => $"{heads[i]} {a[i]}→{b[i]}")));
                    }
                    if (ed.Count > 0)
                    {
                        for (int r = 0; r < tmp.Count; r++) { var m = mau[r]; var t = tmp[r]; m.Stt = t.Stt; m.DatTinh = t.DatTinh; m.Kien = t.Kien; m.NgayNhap = t.NgayNhap; m.Inputs = t.Inputs; }
                        p.Edits.AddRange(ed); changed = true;
                    }
                }
            }
            if (err is not null) { _ = Ui.Message(w, err); err = null; return; }
            w.Close();
        };
        no.Click += (_, _) => w.Close();
        await Ui.Show(owner, w);
        return changed;
    }
}
