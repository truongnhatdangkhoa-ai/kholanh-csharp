using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using KhoLanh.Core.Grid;

namespace KhoLanh.App.Controls;

/// <summary>
/// Danh sách lọc phân tầng của một cột (mở từ nút ▾ ở tiêu đề bảng): ô tìm trong danh sách, Chọn tất cả,
/// mục theo tầng (Năm › Tháng › Ngày với cột ngày), số dòng mỗi giá trị; chỉ hiện giá trị còn lại sau bộ lọc các cột khác.
/// </summary>
internal static class FilterMenu
{
    private sealed class Node
    {
        public required FilterNode N;
        public Node? Parent;
        public List<Node> Kids = new();
        public bool Expanded;
        public bool Leaf => N.Value is not null;
        public bool On;                       // chỉ dùng cho mục lá
        public bool Match = true;             // khớp ô tìm (lá) / có lá con khớp (nhóm)
        public IEnumerable<Node> Leaves() => Leaf ? new[] { this } : Kids.SelectMany(k => k.Leaves());
        public bool? State()
        {
            if (Leaf) return On;
            var ls = Leaves().Where(l => l.Match).ToList();
            if (ls.Count == 0) return false;
            var n = ls.Count(l => l.On);
            return n == 0 ? false : n == ls.Count ? true : null;
        }
    }

    /// <param name="apply">(giá trị được giữ — null = mọi giá trị, có đặt lọc danh sách không)</param>
    public static Control Build(string header, List<FilterNode> tree, HashSet<string>? allowed,
        Action<HashSet<string>?, bool> apply, Action clearColumn, Action cancel)
    {
        Node Wrap(FilterNode f, Node? parent)
        {
            var n = new Node { N = f, Parent = parent, Expanded = f.Level == 0 && tree.Count <= 6 };
            n.On = f.Value is null || allowed is null || allowed.Contains(f.Value);
            foreach (var k in f.Children) n.Kids.Add(Wrap(k, n));
            return n;
        }
        var roots = tree.Select(t => Wrap(t, null)).ToList();
        var all = roots.SelectMany(r => r.Leaves()).ToList();
        var searching = false;

        var list = new ListBox { Height = 280, Background = Brushes.Transparent, BorderThickness = new Thickness(1), BorderBrush = AppTheme.Brush("#B9C3D0") };
        list.Styles.Add(new Avalonia.Styling.Style(x => x.OfType<ListBoxItem>())
        {
            Setters = { new Avalonia.Styling.Setter(ListBoxItem.PaddingProperty, new Thickness(4, 0)), new Avalonia.Styling.Setter(Layoutable.MinHeightProperty, 0d) },
        });
        var allBox = new CheckBox { Content = "(Chọn tất cả)", IsThreeState = true, FontWeight = FontWeight.SemiBold };
        var search = new TextBox { Watermark = "Tìm trong danh sách…" };
        var count = new TextBlock { FontSize = 11.5, Opacity = 0.75 };
        var okBtn = new Button { Content = "Áp dụng", IsDefault = true, MinWidth = 86, HorizontalContentAlignment = HorizontalAlignment.Center };
        okBtn.Classes.Add("primary");

        void Refresh()
        {
            var flat = new List<Node>();
            void Add(Node n)
            {
                if (!n.Match) return;
                flat.Add(n);
                if (!n.Leaf && (n.Expanded || searching)) foreach (var k in n.Kids) Add(k);
            }
            foreach (var r in roots) Add(r);
            list.ItemsSource = flat;
            var ms = all.Where(l => l.Match).ToList();
            var on = ms.Count(l => l.On);
            allBox.IsChecked = ms.Count == 0 ? false : on == 0 ? false : on == ms.Count ? true : null;
            count.Text = $"{all.Count:N0} giá trị · chọn {all.Count(l => l.On && (l.Match || !searching)):N0}";
            okBtn.IsEnabled = ms.Any(l => l.On) || !searching && all.Count == 0;
        }

        list.ItemTemplate = new FuncDataTemplate<Node>((n, _) =>
        {
            if (n is null) return new TextBlock();
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(n.N.Level * 16, 0, 0, 0) };
            if (!n.Leaf)
            {
                var tg = new Button { Content = n.Expanded || searching ? "▾" : "▸", Padding = new Thickness(4, 0), MinHeight = 0, MinWidth = 0, Width = 20, Height = 20, BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontSize = 12 };
                tg.Classes.Add("ghost");
                tg.Click += (_, _) => { n.Expanded = !n.Expanded; Refresh(); };
                row.Children.Add(tg);
            }
            else if (n.N.Level > 0 || roots.Any(r => !r.Leaf)) row.Children.Add(new Border { Width = 20 });
            var cb = new CheckBox { IsChecked = n.State(), IsThreeState = !n.Leaf, MinHeight = 0, Height = 26, Padding = new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Center };
            cb.Content = new TextBlock
            {
                Inlines = new Avalonia.Controls.Documents.InlineCollection
                {
                    new Avalonia.Controls.Documents.Run(n.N.Label.Length > 60 ? n.N.Label[..58] + "…" : n.N.Label) { FontWeight = n.Leaf ? FontWeight.Normal : FontWeight.SemiBold },
                    new Avalonia.Controls.Documents.Run($"  ({n.N.Count:N0})") { Foreground = AppTheme.Brush("#5D6B78"), FontSize = 11.5 },
                },
            };
            cb.Click += (_, _) =>
            {
                var turnOn = n.State() != true;
                foreach (var l in n.Leaves().Where(l => l.Match || !searching)) l.On = turnOn;
                Refresh();
            };
            row.Children.Add(cb);
            return row;
        });

        allBox.Click += (_, _) =>
        {
            var turnOn = allBox.IsChecked != false;   // sau khi bấm: từ "một phần / bỏ" → chọn hết
            var ms = all.Where(l => l.Match).ToList();
            turnOn = !ms.All(l => l.On);
            foreach (var l in ms) l.On = turnOn;
            Refresh();
        };
        search.TextChanged += (_, _) =>
        {
            var q = GridFilter.Fold((search.Text ?? "").Trim());
            searching = q.Length > 0;
            foreach (var l in all) l.Match = !searching || GridFilter.Fold(l.N.Label).Contains(q, StringComparison.Ordinal);
            void Up(Node n) { if (!n.Leaf) { foreach (var k in n.Kids) Up(k); n.Match = n.Kids.Any(k => k.Match); } }
            foreach (var r in roots) Up(r);
            // Excel: khi đang tìm, mặc định chọn hết các mục khớp
            if (searching) foreach (var l in all.Where(l => l.Match)) l.On = true;
            Refresh();
        };
        okBtn.Click += (_, _) =>
        {
            var keep = all.Where(l => l.On && (l.Match || !searching)).Select(l => l.N.Value!).ToHashSet();
            if (!searching && keep.Count == all.Count) apply(null, true);   // chọn hết = bỏ lọc danh sách
            else apply(keep, true);
        };
        var clr = new Button { Content = "Bỏ lọc cột" }; clr.Classes.Add("ghost"); clr.Click += (_, _) => clearColumn();
        var cnc = new Button { Content = "Hủy" }; cnc.Click += (_, _) => cancel();

        var buttons = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(clr, Dock.Left); buttons.Children.Add(clr);
        var rightBtns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        rightBtns.Children.Add(cnc); rightBtns.Children.Add(okBtn);
        DockPanel.SetDock(rightBtns, Dock.Right); buttons.Children.Add(rightBtns);

        var root = new StackPanel { Spacing = 6, Width = 360 };
        root.Children.Add(new TextBlock { Text = "Lọc: " + (string.IsNullOrWhiteSpace(header) ? "cột" : header), FontWeight = FontWeight.Bold, FontSize = 14 });
        root.Children.Add(search);
        root.Children.Add(allBox);
        root.Children.Add(list);
        root.Children.Add(count);
        root.Children.Add(buttons);
        Refresh();
        return root;
    }
}
