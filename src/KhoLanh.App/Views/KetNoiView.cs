using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using KhoLanh.App.Controls;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;

namespace KhoLanh.App.Views;

/// <summary>
/// KẾT NỐI (1.105 — người dùng chọn phương án A, mẫu anh-giao-dien/mau-he-thong/A1, A2): MỌI thứ "kết nối tài khoản / mã / đăng nhập Google"
/// gom về 1 trang, mỗi kết nối 1 thẻ: tên dễ hiểu · Đã kết nối / Chưa / Lỗi · 1 nút chính, việc ít dùng vào "⋯ Thêm".
///  • Google Sheet chung (tài khoản dùng chung + điện thoại — 1 Sheet, 1 Apps Script từ 1.104) · Gmail sao lưu (Drive cá nhân) ·
///    Đăng nhập bằng Google · Gmail nhận lịch xuất · Cập nhật phần mềm (GitHub).
///  • 1.108: bỏ khung "Máy mới? Chỉ cần 2 bước" + nút "Chép mã cho máy khác" (máy mới chỉ cần đăng nhập, 1.106 / 1.107); mã KLKN1- còn ở ⋯ Thêm (dự phòng).
/// Trước đây nằm rải ở Cài đặt máy (tài khoản dùng chung, GitHub), hộp "Kết nối điện thoại…", Tài khoản (Đăng nhập Google).
/// </summary>
public sealed class KetNoiView : UserControl, IPage, IKeepFresh
{
    public string Title => "Kết nối";
    public string Subtitle => "Mọi kết nối của máy này ở một chỗ: Google Sheet chung (tài khoản + điện thoại), Gmail sao lưu, đăng nhập Google, Gmail lịch xuất, cập nhật phần mềm. Máy khác chỉ cần đăng nhập bằng tài khoản được cấp — app tự kết nối.";

    private static KhoLanh.Core.AppServices S => App.Services;
    private static bool Admin => Roles.IsAdmin(S.Session.Role);
    private readonly StackPanel _cards = new() { Spacing = 10 };
    private readonly Border _banner = new() { CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10) };
    private readonly TextBlock _msg = new() { Classes = { "muted" }, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Tag = "keep" };

    /// <summary>Thẻ đang hiện (công cụ chụp kiểm).</summary>
    internal List<(string Name, string State)> Shown { get; } = new();

    public KetNoiView()
    {
        var left = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 6, 0) };
        left.Children.Add(_banner); left.Children.Add(_cards); left.Children.Add(_msg);
        var right = new StackPanel { Spacing = 10, Width = 330 };
        right.Children.Add(Legend());
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,Auto") };
        var sv = new ScrollViewer { Content = left, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        g.Children.Add(sv);
        var rs = new ScrollViewer { Content = right, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetColumn(rs, 2); g.Children.Add(rs);
        Content = g;
        AttachedToVisualTree += (_, _) => Render();
    }

    // ---------------- vẽ ----------------

    internal void Render()
    {
        _cards.Children.Clear(); Shown.Clear();
        _cards.Children.Add(SheetCard());
        if (S.DriveCaNhan.CanUse) _cards.Children.Add(GmailBackupCard());
        if (Admin) _cards.Children.Add(GoogleLoginCard());
        if (S.LichXuat.CanMail) _cards.Children.Add(MailCard());
        if (Admin) _cards.Children.Add(GithubCard());
        var ok = Shown.Count(x => x.State == "ok"); var bad = Shown.Where(x => x.State == "bad").Select(x => x.Name).ToList();
        var miss = Shown.Where(x => x.State is "off" or "warn").Select(x => x.Name).ToList();
        var (bg, fg, text) = bad.Count > 0 ? ("#FDE8E6", "#B42318", $"Có {bad.Count} kết nối bị lỗi: {string.Join(", ", bad)}. Xem dòng đỏ trong thẻ để biết cách sửa.")
            : miss.Count > 0 ? ("#FFF2D6", "#9A5B00", $"Máy này đã kết nối {ok} / {Shown.Count} mục. Chưa xong: {string.Join(", ", miss)}.")
            : ("#E3F4EA", "#1E7B4A", $"Máy này đã kết nối đủ {ok} / {Shown.Count} mục.");
        _banner.Background = AppTheme.Brush(bg);
        _banner.Child = new TextBlock { Text = text, Foreground = AppTheme.Brush(fg), FontWeight = FontWeight.SemiBold, FontSize = 13.5, TextWrapping = TextWrapping.Wrap };
    }

    private static Border Pill(string kind, string text)
    {
        var b = new Border { Classes = { "pill" }, VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = text } };
        if (kind is "ok" or "warn" or "bad") b.Classes.Add(kind);
        else { b.Background = AppTheme.Brush("#EEF1F5"); ((TextBlock)b.Child).Foreground = AppTheme.Brush("#5D6B78"); }
        return b;
    }

    private static string PillText(string kind, string ok) => kind switch { "ok" => "● " + ok, "bad" => "● Lỗi", "warn" => "● Chưa xong", _ => "○ Chưa kết nối" };

    private Border Card(string name, string icon, string iconBg, string kind, string pillText, string desc, string? detail, params Control[] actions)
    {
        Shown.Add((name, kind));
        var ic = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(10), Background = AppTheme.Brush(iconBg), VerticalAlignment = VerticalAlignment.Top,
            Child = new Border { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = Icon.Make(icon, 20) } };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        head.Children.Add(new TextBlock { Text = name, FontSize = 14.5, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(Pill(kind, pillText));
        var mid = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        mid.Children.Add(head);
        mid.Children.Add(new TextBlock { Text = desc, Classes = { "muted" }, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Tag = "keep" });
        if (detail is { Length: > 0 })
            mid.Children.Add(new TextBlock { Text = detail, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Tag = "keep",
                Foreground = kind == "bad" ? AppTheme.Danger : kind == "warn" ? AppTheme.Brush("#9A5B00") : AppTheme.Muted });
        var act = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var a in actions) { a.HorizontalAlignment = HorizontalAlignment.Right; act.Children.Add(a); }
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 14 };
        g.Children.Add(ic); Grid.SetColumn(mid, 1); g.Children.Add(mid); Grid.SetColumn(act, 2); g.Children.Add(act);
        return new Border { Classes = { "card" }, Padding = new Thickness(16, 14), Child = g };
    }

    private Button Btn(string text, Func<Task> click, string? cls = null)
    {
        var b = new Button { Content = text }; if (cls is not null) b.Classes.Add(cls);
        b.Click += async (_, _) => { await click(); Render(); };
        return b;
    }

    private Button More(params (string Text, Func<Task> Click, bool Danger)[] items)
    {
        var b = new Button { Content = "⋯ Thêm", Classes = { "ghost" } };
        var menu = new MenuFlyout();
        foreach (var (t, c, d) in items)
        {
            var mi = new MenuItem { Header = t };
            if (d) mi.Foreground = AppTheme.Danger;
            mi.Click += async (_, _) => { await c(); Render(); };
            menu.Items.Add(mi);
        }
        b.Flyout = menu;
        return b;
    }

    private static string When(DateTime d) => d.Date == DateTime.Today ? d.ToString("HH:mm") : d.ToString("HH:mm dd/MM");

    // ---------------- thẻ 1: Google Sheet chung (tài khoản + điện thoại) ----------------

    private Border SheetCard()
    {
        var a = S.Accounts; var ph = S.Phone;
        const string name = "Google Sheet chung", desc = "Dùng cho tài khoản đăng nhập (mọi máy chung tài khoản, phân quyền) và điện thoại (xem tồn, gửi phiếu). 1 Sheet, 1 mã.";
        var err = a.LastError ?? ph.LastError;
        string kind; string? detail; var acts = new List<Control>();
        if (!a.Enabled && !ph.Enabled)
        {
            kind = "off"; detail = "Máy này dùng tài khoản riêng, chưa nối điện thoại.";
            if (Admin) acts.Add(Btn("Kết nối…", () => Wizard(this), "primary"));
        }
        else if (a.Enabled && ph.Enabled && a.OnPhoneSheet)
        {
            kind = "ok";
            detail = "Tài khoản + điện thoại cùng 1 Google Sheet" + (a.LastPull.Length > 0 ? $" · tải tài khoản lúc {a.LastPull}" : "") + (a.CanWrite ? " · máy này sửa được tài khoản" : "");
        }
        else if (a.Enabled && !a.OnPhoneSheet)
        {
            kind = "warn";
            detail = ph.Enabled ? "Tài khoản còn nằm ở Google Sheet riêng (2 Sheet). Bấm \"Gộp vào Sheet điện thoại\" để chỉ còn 1 Sheet + 1 mã."
                                : "Tài khoản đã dùng chung, nhưng máy này chưa nối điện thoại (chưa có mã PC_TOKEN).";
            if (Admin) acts.Add(ph.Enabled ? Btn("Gộp vào Sheet điện thoại", MoveToPhone, "primary") : Btn("Nhập mã điện thoại…", () => PhoneConnect.ShowAsync(this), "primary"));
        }
        else if (!a.Enabled)
        {
            kind = "warn"; detail = "Điện thoại đã nối, nhưng tài khoản chưa dùng chung — mỗi máy vẫn có danh sách tài khoản riêng.";
            if (Admin) acts.Add(Btn("Bật tài khoản dùng chung", MoveToPhone, "primary"));
        }
        else
        {
            kind = "warn"; detail = "Tài khoản đã dùng chung, nhưng máy này thiếu mã điện thoại (PC_TOKEN).";
            if (Admin) acts.Add(Btn("Nhập mã điện thoại…", () => PhoneConnect.ShowAsync(this), "primary"));
        }
        if (err is not null && kind != "off")
        {
            kind = "bad";
            detail = $"Lỗi lúc {When(err.At)}: {err.Msg}" + (err.Kind == ConnErrorLog.KindPhoneScript ? "" : " — bấm ⋯ Thêm › Kiểm tra kết nối để thử lại.");
        }
        if (Admin && (a.Enabled || ph.Enabled))
        {
            var items = new List<(string, Func<Task>, bool)>();
            items.Add(("Kiểm tra kết nối", CheckNow, false));
            if (a.Enabled) items.Add(("Đồng bộ tài khoản ngay", PullAccounts, false));
            if (a.Enabled) items.Add(("Chép mã kết nối máy (dự phòng)", () => CopyCode(false), false));
            if (a.Enabled) items.Add(("Chép mã cho máy quản trị…", () => CopyCode(true), false));
            if (a.Enabled) items.Add(("Tạo bộ cài cho máy mới…", Bundle, false));
            items.Add(("Mã điện thoại (PC_TOKEN) / đường dẫn…", () => PhoneConnect.ShowAsync(this), false));
            if (a.Enabled && !a.OnPhoneSheet && a.CanWrite) items.Add(("Cập nhật mã Apps Script (Sheet tài khoản riêng)", UpgradeScript, false));
            if (a.Enabled) items.Add(("Ngắt tài khoản dùng chung…", DisconnectAccounts, true));
            acts.Add(More(items.ToArray()));
        }
        return Card(name, "users", "#E6F0FC", kind, PillText(kind, "Đã kết nối"), desc, detail, acts.ToArray());
    }

    private async Task CopyCode(bool admin)
    {
        if (admin && !await Ui.Confirm(this, "Mã cho máy QUẢN TRỊ cho phép máy đó thêm / sửa / phân quyền mọi tài khoản. Chỉ gửi cho máy của người quản trị. Tiếp tục?", "Mã máy quản trị", "Chép mã")) return;
        string? code = null;
        if (!await Ui.Run(this, () => code = S.Accounts.ConnectionCode(admin))) return;
        await Copy(code!);
        _msg.Text = (admin ? "Đã chép mã cho máy quản trị." : "Đã chép mã kết nối máy (KLKN1-…).")
            + " Thường không cần: máy khác chỉ cần đăng nhập. Mã chỉ dùng khi Google Sheet chưa có Code.gs mới — ở máy đó, màn đăng nhập lần đầu bấm \"Có mã kết nối KLKN1-? Dán mã…\"."
            + (S.Phone.Enabled ? " Mã gồm luôn mã điện thoại." : "");
    }

    private async Task Copy(string text)
    {
        var cb = TopLevel.GetTopLevel(this)?.Clipboard;
        if (cb is not null) await cb.SetTextAsync(text);
    }

    private async Task CheckNow()
    {
        var parts = new List<string>();
        await Ui.RunAsync(this, async () =>
        {
            if (S.Phone.Enabled)
            {
                try { var (t, sh) = await S.Phone.PingAsync(); parts.Add($"✔ Điện thoại: mở được Google Sheet \"{t}\" ({sh.Count} tab)."); }
                catch (Exception e) { parts.Add("✖ Điện thoại: " + e.Message); }
            }
            if (S.Accounts.Enabled)
            {
                try { var n = await S.Accounts.PullAsync(TimeSpan.FromSeconds(30)); parts.Add($"✔ Tài khoản: đọc được {n} tài khoản."); }
                catch (Exception e) { parts.Add("✖ Tài khoản: " + e.Message); }
            }
        });
        _msg.Text = string.Join("\n", parts);
    }

    private async Task PullAccounts()
    {
        int n = 0;
        if (await Ui.RunAsync(this, async () => n = await S.Accounts.PullAsync(TimeSpan.FromSeconds(30)))) _msg.Text = $"Đã đồng bộ {n} tài khoản từ Google Sheet.";
    }

    private async Task MoveToPhone()
    {
        var a = S.Accounts;
        var msg = a.Enabled
            ? "Chép toàn bộ tài khoản từ Google Sheet tài khoản hiện tại sang Google Sheet điện thoại (giữ nguyên mã, các máy khác tự chuyển theo ở lần đăng nhập sau). "
            : "Đưa tài khoản của máy này lên Google Sheet điện thoại để mọi máy dùng chung. ";
        msg += "Cần Apps Script của Sheet điện thoại là bản mới (Code.gs có phần tài khoản). Tiếp tục?";
        if (!await Ui.Confirm(this, msg, "Tài khoản dùng chung", "Đồng ý")) return;
        _msg.Text = "Đang chuyển sang Google Sheet điện thoại…";
        AccountSyncService.MoveResult? r = null;
        if (!await Ui.RunAsync(this, async () => r = await a.MoveToPhoneSheetAsync(S.Phone))) { _msg.Text = ""; return; }
        _msg.Text = $"Xong: {r!.Users} tài khoản dùng chung qua Google Sheet điện thoại."
            + (r.OldMarked ? " Các máy khác tự chuyển theo ở lần đăng nhập sau. Giữ Sheet tài khoản cũ vài ngày rồi mới xóa." : " Máy khác chỉ cần đăng nhập bằng tài khoản được cấp.");
    }

    private async Task UpgradeScript()
    {
        string? script = null;
        if (!await Ui.Run(this, () => script = S.Accounts.UpgradeScript())) return;
        var file = Path.Combine(App.DataFolder, "TaiKhoan-AppsScript.gs");
        try { File.WriteAllText(file, script!); } catch { }
        await Copy(script!);
        _msg.Text = $"Đã chép mã Apps Script bản mới cho Sheet TÀI KHOẢN RIÊNG (lưu ở {file}). Không dán vào Sheet điện thoại. Nên bấm \"Gộp vào Sheet điện thoại\" để chỉ còn 1 Sheet.";
    }

    private async Task DisconnectAccounts()
    {
        if (!await Ui.Confirm(this, "Ngắt tài khoản dùng chung: máy này thôi lấy tài khoản từ Google Sheet, giữ nguyên danh sách tài khoản hiện có trong máy. Điện thoại vẫn đồng bộ bình thường. Tiếp tục?", "Ngắt kết nối", "Ngắt")) return;
        if (await Ui.Run(this, () => S.Accounts.Disconnect())) _msg.Text = "Đã ngắt. Máy này dùng danh sách tài khoản riêng.";
    }

    /// <summary>Thư mục chép sang máy mới: KhoLanh.exe + ket-noi.txt (mã kết nối máy + mã LAN). Máy mới mở lên chỉ cần đăng nhập.</summary>
    private async Task Bundle()
    {
        var root = await Ui.OpenFolder(this, "Chọn nơi tạo bộ cài (VD: USB hoặc Desktop)");
        if (root is null) return;
        string? dir = null;
        if (!await Ui.RunAsync(this, () => Task.Run(() =>
        {
            var text = S.Accounts.BundleText();
            if (S.Lan.BundleLine() is { } lan)
                text += "# Mã LAN: máy mới tự làm máy con, dùng chung dữ liệu với máy chủ trong mạng nội bộ.\r\n" + lan + "\r\n";
            dir = Path.Combine(root, "KhoLanh-cai-may-moi");
            Directory.CreateDirectory(dir);
            if (AppUpdater.ExePath is { } exe) File.Copy(exe, Path.Combine(dir, "KhoLanh.exe"), true);
            File.WriteAllText(Path.Combine(dir, AccountSyncService.BundleFile), text);
            File.WriteAllText(Path.Combine(dir, "DOC-TRUOC.txt"),
                "Cài Kho Lạnh cho máy mới:\r\n" +
                "1. Chép CẢ thư mục này sang máy mới (VD vào Desktop). Giữ ket-noi.txt cạnh KhoLanh.exe.\r\n" +
                "2. Mở KhoLanh.exe (máy cần Internet lần đầu). Phần mềm tự nhận tài khoản, phân quyền và mã điện thoại từ Google Sheet.\r\n" +
                "3. Đăng nhập bằng tài khoản được cấp. Phần mềm tự cập nhật bản mới, không cần cài lại.\r\n" +
                "Nếu Windows hỏi (SmartScreen): bấm Thông tin thêm > Vẫn chạy.\r\n");
        }))) return;
        _msg.Text = $"Đã tạo bộ cài ở {dir}. Chép cả thư mục sang máy mới, mở KhoLanh.exe là chỉ cần đăng nhập."
                    + (AppUpdater.ExePath is null ? " (Chưa chép được KhoLanh.exe — hãy tự chép file exe vào thư mục đó.)" : "")
                    + (S.Updates.HasToken ? "" : " Lưu ý: chưa có mã GitHub nên máy mới chưa tự cập nhật được.");
        Ui.Exported(this, dir!);
    }

    // ---------------- hướng dẫn kết nối lần đầu ----------------

    private static Control Legend()
    {
        var p = new StackPanel { Spacing = 6 };
        p.Children.Add(new TextBlock { Text = "Ý nghĩa nhãn", FontWeight = FontWeight.Bold });
        void Row(string kind, string pill, string text)
        {
            var r = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            r.Children.Add(Pill(kind, pill)); r.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 });
            p.Children.Add(r);
        }
        Row("ok", "● Đã kết nối", "dùng được");
        Row("warn", "● Chưa xong", "còn 1 bước, bấm nút xanh");
        Row("off", "○ Chưa kết nối", "chưa cài");
        Row("bad", "● Lỗi", "đọc dòng đỏ để sửa");
        return new Border { Classes = { "card" }, Padding = new Thickness(16, 12), Child = p };
    }

    /// <summary>Dán mã kết nối máy (KLKN1- / KLTK1- cũ) → nhận tài khoản dùng chung + mã điện thoại + mã GitHub.</summary>
    internal static async Task<bool> JoinWithCode(Control owner, string code, Action<string> say)
    {
        code = code.Trim();
        if (code.Length == 0) { await Ui.Message(owner, "Dán mã kết nối (bắt đầu bằng KLKN1-) vào ô trước."); return false; }
        if (!AccountSyncService.IsCode(code)) { await Ui.Message(owner, "Mã kết nối phải bắt đầu bằng KLKN1- (hoặc KLTK1- bản cũ). Chép lại đủ cả dòng mã ở máy Quản trị › Hệ thống › Kết nối › ⋯ Thêm › Chép mã kết nối máy."); return false; }
        if (S.Accounts.Enabled && !await Ui.Confirm(owner, "Máy này đang kết nối Google Sheet. Dán mã mới sẽ thay kết nối cũ (danh sách tài khoản lấy theo mã mới). Tiếp tục?", "Kết nối", "Thay kết nối")) return false;
        say("Đang kết nối…");
        AccountSyncService.ConnectResult? r = null;
        if (!await Ui.RunAsync(owner, async () => r = await S.Accounts.ConnectAsync(code))) { say(""); return false; }
        say($"Đã kết nối: nhận {r!.Users} tài khoản" + (r.Admin ? " (máy quản trị)" : "") + (S.Phone.Enabled ? " + mã điện thoại" : "") + ".");
        return true;
    }

    /// <summary>Kết nối lần đầu: hỏi máy này là máy đầu tiên (Quản trị tạo) hay đã có máy khác (dán mã), rồi làm từng bước.</summary>
    /// <summary>Hộp kết nối lần đầu đang / vừa mở (công cụ chụp).</summary>
    internal static Window? LastWizard { get; private set; }
    internal Task WizardForTest() => Wizard(this);

    private static async Task Wizard(KetNoiView owner)
    {
        var join = new RadioButton { GroupName = "kn", IsChecked = true, Content = Opt("Đã có máy khác kết nối", "Chỉ cần mã từ máy Quản trị. Thường chọn cái này.") };
        var first = new RadioButton { GroupName = "kn", Content = Opt("Máy đầu tiên (Quản trị)", "Chưa máy nào kết nối — dùng Google Sheet điện thoại làm Sheet chung.") };
        var code = new TextBox { Watermark = "Dán mã KLKN1-… vào đây", FontSize = 14 };
        var tok = new TextBox { Watermark = "Mã PC_TOKEN (Apps Script › Cài đặt dự án › Thuộc tính tập lệnh)", PasswordChar = '•', Text = S.Phone.Token };
        var msg = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Tag = "keep" };
        var paneJoin = new StackPanel { Spacing = 8, Children =
        {
            new TextBlock { Text = "Bước 2: Dán mã kết nối", FontWeight = FontWeight.Bold, FontSize = 14 },
            new TextBlock { Text = "Mã do máy Quản trị gửi (bắt đầu bằng KLKN1-). Bấm vào ô rồi nhấn Ctrl + V.", Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, Tag = "keep" },
            code,
        } };
        var paneFirst = new StackPanel { Spacing = 8, IsVisible = false, Children =
        {
            new TextBlock { Text = "Bước 2: Nối Google Sheet điện thoại", FontWeight = FontWeight.Bold, FontSize = 14 },
            new TextBlock { TextWrapping = TextWrapping.Wrap, Classes = { "muted" }, FontSize = 12.5, Tag = "keep",
                Text = "a) Google Sheet điện thoại phải dán Code.gs bản mới (Tiện ích mở rộng › Apps Script › dán › Lưu › Triển khai › Quản lý triển khai › ✏ › Phiên bản mới).\n"
                     + "b) Dán mã PC_TOKEN vào ô dưới (đường dẫn Web App đã cài sẵn).\n"
                     + "c) Bấm Kết nối: app đưa tài khoản của máy này lên Sheet và bật dùng chung." },
            tok,
        } };
        join.IsCheckedChanged += (_, _) => { paneJoin.IsVisible = join.IsChecked == true; paneFirst.IsVisible = !paneJoin.IsVisible; msg.Text = ""; };
        var steps = new TextBlock { Text = "Bước 1: Máy này là…", FontWeight = FontWeight.Bold, FontSize = 14 };
        var ok = new Button { Content = "Kết nối ›", Classes = { "primary" }, IsDefault = true }; var close = new Button { Content = "Đóng", IsCancel = true };
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(steps);
        body.Children.Add(new StackPanel { Spacing = 6, Children = { join, first } });
        body.Children.Add(new Border { Height = 1, Background = AppTheme.Brush("#DDE3EA"), Margin = new Thickness(0, 4) });
        body.Children.Add(paneJoin); body.Children.Add(paneFirst); body.Children.Add(msg);
        body.Children.Add(Ui.Buttons(close, ok));
        var w = Ui.Dialog("Kết nối Google Sheet chung", body, 600);
        LastWizard = w;
        close.Click += (_, _) => w.Close();
        ok.Click += async (_, _) =>
        {
            ok.IsEnabled = false;
            try
            {
                if (join.IsChecked == true)
                {
                    if (await JoinWithCode(w, code.Text ?? "", m => msg.Text = m)) { owner._msg.Text = msg.Text; w.Close(); }
                    return;
                }
                if (string.IsNullOrWhiteSpace(tok.Text)) { msg.Foreground = AppTheme.Danger; msg.Text = "Chưa nhập mã PC_TOKEN."; return; }
                msg.Foreground = AppTheme.Muted; msg.Text = "Đang kiểm tra Google Sheet điện thoại…";
                if (!await Ui.Run(w, () => S.Phone.Save(S.Phone.CustomUrl ? S.Phone.Url : "", tok.Text ?? ""))) return;
                try { await S.Phone.PingAsync(); }
                catch (Exception e) { msg.Foreground = AppTheme.Danger; msg.Text = "Chưa mở được Google Sheet điện thoại: " + e.Message; return; }
                msg.Text = "Đang đưa tài khoản lên Google Sheet…";
                AccountSyncService.MoveResult? r = null;
                if (!S.Accounts.Enabled && !await Ui.RunAsync(w, async () => r = await S.Accounts.MoveToPhoneSheetAsync(S.Phone))) { msg.Text = "Điện thoại đã nối; tài khoản chưa bật dùng chung (xem lỗi vừa báo)."; return; }
                owner._msg.Text = "Đã kết nối Google Sheet chung" + (r is not null ? $": {r.Users} tài khoản dùng chung" : "") + ". Máy khác chỉ cần đăng nhập bằng tài khoản được cấp.";
                w.Close();
            }
            finally { ok.IsEnabled = true; }
        };
        await Ui.Show(owner, w);
        owner.Render();
    }

    private static Control Opt(string title, string desc) => new StackPanel { Spacing = 2, Children =
    {
        new TextBlock { Text = title, FontWeight = FontWeight.Bold },
        new TextBlock { Text = desc, Classes = { "muted" }, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Tag = "keep" },
    } };

    // ---------------- các thẻ khác ----------------

    private Border GmailBackupCard()
    {
        var d = S.DriveCaNhan; var c = d.Config; var client = d.Client;
        var kind = c.LastErr.Length > 0 && c.Linked ? "bad" : c.Linked ? "ok" : "off";
        var detail = c.Linked
            ? $"Gmail: {c.Email}" + (c.Last is { } l ? $" · sao lưu lần cuối {When(l)}" : " · chưa sao lưu lần nào") + (c.LastErr.Length > 0 ? " · lỗi lần trước: " + c.LastErr : "")
            : !client.Ok ? "Máy này chưa có Client ID Google — bấm Đăng nhập Gmail, Quản trị nhập 1 lần." : null;
        var b = Btn(c.Linked ? "Xem bản sao lưu" : "Đăng nhập Gmail", () => { MainWindow.Of(this)?.Navigate("dcn"); return Task.CompletedTask; }, c.Linked ? null : "primary");
        return Card("Gmail sao lưu (Google Drive)", "cloud", "#FDEDE4", kind, PillText(kind, "Đã kết nối"),
            "Đăng nhập Gmail để app tự gửi bản sao lưu lên Google Drive của bạn. Máy hỏng vẫn lấy lại được dữ liệu.", detail, b);
    }

    private Border GoogleLoginCard()
    {
        var g = S.Google;
        var kind = g.Ready ? "ok" : "off";
        return Card("Đăng nhập bằng Google", "user", "#EEEBFC", kind, PillText(kind, "Đã bật"),
            "Cho phép mọi người bấm \"Đăng nhập bằng Google\" ở màn hình đăng nhập (gán Email Google cho tài khoản ở Hệ thống › Tài khoản).", null,
            Btn(g.Ready ? "Sửa" : "Bật…", () => UsersView.GoogleConfigDialog(this)));
    }

    private Border MailCard()
    {
        var cfg = S.LichXuat.MailCfg(); var st = S.LichXuat.MailStatus;
        var kind = !cfg.Ready ? "off" : st.StartsWith("✖") ? "bad" : "ok";
        var detail = cfg.Ready ? cfg.Email + (st.Length > 0 ? " · " + st.TrimStart('✓', '✖', ' ') : "") : null;
        return Card("Gmail nhận lịch xuất", "mail", "#FFF2D6", kind, PillText(kind, "Đã kết nối"),
            "Đọc thư lịch xuất (LSX) của người gửi tin cậy để tự lên lịch. Mật khẩu ứng dụng lưu riêng trên máy này.", detail,
            Btn(cfg.Ready ? "Mở" : "Kết nối…", async () =>
            {
                var main = MainWindow.Of(this); if (main is null) return;
                main.Navigate("lx");
                if (main.PageHost.Content is TongQuanView t) await t.OpenEmailForTest();
            }, cfg.Ready ? null : "primary"));
    }

    private Border GithubCard()
    {
        var u = S.Updates;
        var kind = u.HasToken ? "ok" : "warn";
        return Card("Cập nhật phần mềm (GitHub)", "download", "#EEF1F5", kind, kind == "ok" ? "● Đã kết nối" : "● Chưa có mã",
            "Để app tự tải bản mới. Thường không cần đụng tới.", $"Kho: {u.Repo}" + (u.Auto ? " · tự cập nhật" : " · đang tắt tự cập nhật") + (u.LastCheck.Length > 0 ? $" · hỏi lần cuối {u.LastCheck}" : ""),
            Btn("Sửa", () => GithubDialog(this)));
    }

    private static async Task GithubDialog(Control owner)
    {
        var u = S.Updates;
        var repo = new TextBox { Text = u.Repo };
        var tok = new TextBox { PasswordChar = '•', Watermark = u.HasToken ? "Đã có mã — để trống = giữ nguyên" : "Chưa có mã — dán token vào đây" };
        var auto = new CheckBox { Content = "Tự động kiểm tra và tải bản mới", IsChecked = u.Auto };
        var msg = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Tag = "keep" };
        var ok = new Button { Content = "Lưu", Classes = { "primary" }, IsDefault = true }; var close = new Button { Content = "Đóng", IsCancel = true };
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(new TextBlock { Text = "Kho GitHub", Classes = { "label" } }); body.Children.Add(repo);
        body.Children.Add(new TextBlock { Text = "Mã truy cập (token chỉ-đọc, cần khi kho riêng tư)", Classes = { "label" } }); body.Children.Add(tok);
        body.Children.Add(auto); body.Children.Add(msg); body.Children.Add(Ui.Buttons(close, ok));
        var w = Ui.Dialog("Cập nhật phần mềm (GitHub)", body, 520);
        ok.Click += (_, _) =>
        {
            try { u.SaveConfig(repo.Text ?? "", string.IsNullOrWhiteSpace(tok.Text) ? null : tok.Text, auto.IsChecked == true); w.Close(); }
            catch (Exception e) { msg.Foreground = AppTheme.Danger; msg.Text = e.Message; }
        };
        close.Click += (_, _) => w.Close();
        await Ui.Show(owner, w);
    }
}
