using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;

namespace KhoLanh.App.Views;

public class LineRow
{
    public int Index { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public VoucherLine Line { get; set; } = new();
}

public partial class VoucherEditView : UserControl, IPage
{
    private readonly string _type;
    private readonly ObservableCollection<LineRow> _rows = new();
    private List<Product> _products = new();
    private StockItem? _picked;

    private bool IsIn => _type == VoucherTypes.In;
    public string Title => IsIn ? "Lập phiếu nhập kho" : "Lập phiếu xuất kho";
    public string Subtitle => IsIn
        ? "Nhập hàng vào kho theo mã hàng, size, lô và vị trí."
        : "Xuất hàng khỏi kho. Phần mềm chặn xuất quá số tồn của đúng lô / vị trí.";

    public VoucherEditView() : this(VoucherTypes.In) { }

    public VoucherEditView(string type)
    {
        _type = type;
        InitializeComponent();
        var s = App.Services;
        PartnerLabel.Text = IsIn ? "Người giao hàng / nhà cung cấp" : "Người nhận hàng / khách hàng";
        QtyLabel.Text = "Số lượng";
        HintText.Text = IsIn
            ? "Mẹo: gõ vài ký tự mã hàng rồi chọn; Enter ở ô Số lượng / Số kg để thêm dòng. Bỏ trống Số kg thì phần mềm tự tính theo Kg/đơn vị của mã hàng."
            : "Mẹo: chọn mã hàng, rồi nhấp đúp một dòng trong bảng Tồn có thể xuất để điền sẵn size / lô / vị trí. Bỏ trống Số kg thì tự tính theo tỷ lệ kg của lô đó.";
        if (!IsIn)
        {
            BodyGrid.RowDefinitions[0].Height = new GridLength(230);
            BodyGrid.RowSpacing = 14;
            StockCard.IsVisible = true;
        }

        _products = s.Catalog.ListProducts();
        ProductBox.ItemsSource = _products;
        ProductBox.ValueMemberBinding = new Binding(nameof(Product.Display));
        ProductBox.ItemFilter = (search, item) =>
            item is Product p && (string.IsNullOrEmpty(search)
                || p.Code.Contains(search, StringComparison.OrdinalIgnoreCase)
                || p.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
        ProductBox.SelectionChanged += (_, _) => OnProductChanged();
        LocationBox.ItemsSource = s.Stock.DistinctLocations();

        var owners = s.Catalog.ListOwners();
        OwnerBox.ItemsSource = owners;
        OwnerBox.SelectedIndex = owners.Count > 0 ? 0 : -1;
        OwnerBox.SelectionChanged += (_, _) => RefreshStock();
        DateBox.SelectedDate = DateTime.Today;
        DateBox.SelectedDateChanged += (_, _) => UpdateNumber();

        LinesGrid.ItemsSource = _rows;
        foreach (var tb in new Control[] { QtyBox, KgBox, LineNoteBox, LotBox })
            tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; OnAddLine(null, new RoutedEventArgs()); } };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { e.Handled = true; OnSave(null, new RoutedEventArgs()); }
        };
        UpdateNumber();
        UpdateTotals();
        AttachedToVisualTree += (_, _) => ProductBox.Focus();
    }

    /// <summary>Điền sẵn từ một phiếu cũ (lập lại phiếu đã hủy / phiếu tương tự).</summary>
    public void Prefill(Voucher v, IEnumerable<VoucherLineView> lines)
    {
        if (OwnerBox.ItemsSource is IEnumerable<Owner> owners)
            OwnerBox.SelectedItem = owners.FirstOrDefault(o => o.Id == v.OwnerId) ?? OwnerBox.SelectedItem;
        PartnerBox.Text = v.Partner; VehicleBox.Text = v.Vehicle; NoteBox.Text = v.Note;
        foreach (var l in lines)
        {
            _rows.Add(new LineRow
            {
                Index = _rows.Count + 1, ProductCode = l.ProductCode, ProductName = l.ProductName,
                Line = new VoucherLine { ProductId = l.ProductId, Size = l.Size, Lot = l.Lot, Location = l.Location, Quantity = l.Quantity, Weight = l.Weight, Note = l.Note },
            });
        }
        UpdateTotals();
    }

    private Product? SelectedProduct => ProductBox.SelectedItem as Product;
    private Owner? SelectedOwner => OwnerBox.SelectedItem as Owner;

    private void UpdateNumber()
    {
        NoBox.Text = App.Services.Vouchers.PreviewNumber(_type, DateBox.SelectedDate ?? DateTime.Today) ;
    }

    private void OnProductChanged()
    {
        _picked = null;
        var p = SelectedProduct;
        SizeBox.ItemsSource = p?.SizeList.ToList() ?? new List<string>();
        RefreshStock();
    }

    private void RefreshStock()
    {
        if (IsIn) return;
        var p = SelectedProduct;
        var o = SelectedOwner;
        if (p is null || o is null) { StockGrid.ItemsSource = null; return; }
        // Trừ đi số đã đưa vào phiếu đang lập để thấy phần còn lại.
        var stock = App.Services.Stock.Stock(o.Id).Where(x => x.ProductId == p.Id).ToList();
        foreach (var st in stock)
        {
            var used = _rows.Where(r => r.Line.ProductId == st.ProductId && r.Line.Size == st.Size && r.Line.Lot == st.Lot && r.Line.Location == st.Location).ToList();
            st.Quantity -= used.Sum(r => r.Line.Quantity);
            st.Weight -= used.Sum(r => r.Line.Weight);
        }
        StockGrid.ItemsSource = stock.Where(x => x.Quantity > 1e-9).OrderBy(x => x.FirstIn).ToList();
    }

    private void OnPickStock(object? sender, TappedEventArgs e)
    {
        if (StockGrid.SelectedItem is not StockItem st) return;
        _picked = st;
        SizeBox.Text = st.Size;
        LotBox.Text = st.Lot;
        LocationBox.Text = st.Location;
        QtyBox.Text = Ui.Num(st.Quantity);
        KgBox.Text = "";
        QtyBox.Focus();
        QtyBox.SelectAll();
    }

    private async void OnAddLine(object? sender, RoutedEventArgs e)
    {
        await Ui.Run(this, () =>
        {
            var p = SelectedProduct
                    ?? _products.FirstOrDefault(x => string.Equals(x.Code, (ProductBox.Text ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                    ?? throw new BusinessException("Chưa chọn mã hàng (gõ mã rồi chọn trong danh sách).");
            var qty = Ui.ParseNumber(QtyBox.Text, "Số lượng");
            if (qty <= 0) throw new BusinessException("Số lượng phải lớn hơn 0.");
            double kg;
            if (string.IsNullOrWhiteSpace(KgBox.Text))
            {
                var line0 = (SizeBox.Text ?? "").Trim();
                if (!IsIn && _picked is { } st && st.Size == line0 && st.Lot == (LotBox.Text ?? "").Trim() && st.Quantity > 0)
                    kg = Math.Round(st.Weight / st.Quantity * qty, 2);
                else kg = Math.Round(p.KgPerUnit * qty, 2);
            }
            else kg = Ui.ParseNumber(KgBox.Text, "Số kg");
            if (kg < 0) throw new BusinessException("Số kg không được âm.");
            var line = new VoucherLine
            {
                ProductId = p.Id, Size = (SizeBox.Text ?? "").Trim(), Lot = (LotBox.Text ?? "").Trim(),
                Location = (LocationBox.Text ?? "").Trim().ToUpperInvariant(), Quantity = qty, Weight = kg,
                Note = (LineNoteBox.Text ?? "").Trim(),
            };
            _rows.Add(new LineRow { Index = _rows.Count + 1, ProductCode = p.Code, ProductName = p.Name, Line = line });
            QtyBox.Text = ""; KgBox.Text = ""; LineNoteBox.Text = "";
            _picked = null;
            UpdateTotals();
            RefreshStock();
            if (IsIn) QtyBox.Focus(); else StockGrid.Focus();
        });
    }

    private void OnRemoveLine(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not LineRow row) return;
        _rows.Remove(row);
        for (var i = 0; i < _rows.Count; i++) _rows[i].Index = i + 1;
        LinesGrid.ItemsSource = null;
        LinesGrid.ItemsSource = _rows;
        UpdateTotals();
        RefreshStock();
    }

    private void UpdateTotals()
    {
        TotalText.Text = $"{_rows.Count} dòng · {Ui.Num(_rows.Sum(r => r.Line.Quantity))} đơn vị · {Ui.Num(_rows.Sum(r => r.Line.Weight))} kg";
    }

    private void OnReset(object? sender, RoutedEventArgs e) => Reset();

    private void Reset()
    {
        _rows.Clear();
        PartnerBox.Text = ""; VehicleBox.Text = ""; NoteBox.Text = "";
        ProductBox.Text = ""; SizeBox.Text = ""; LotBox.Text = ""; LocationBox.Text = ""; QtyBox.Text = ""; KgBox.Text = "";
        UpdateNumber(); UpdateTotals(); RefreshStock();
        ProductBox.Focus();
    }

    private async Task<Voucher?> Save()
    {
        Voucher? saved = null;
        if (_rows.Count == 0)
        {
            await Ui.Message(this, "Phiếu chưa có dòng hàng nào. Nhập mã hàng, số lượng rồi bấm \"+ Thêm dòng\".");
            return null;
        }
        var ok = await Ui.Run(this, () =>
        {
            var v = new Voucher
            {
                Type = _type, Date = DateBox.SelectedDate ?? DateTime.Today,
                OwnerId = SelectedOwner?.Id ?? throw new BusinessException("Chưa chọn chủ hàng."),
                Partner = PartnerBox.Text ?? "", Vehicle = VehicleBox.Text ?? "", Note = NoteBox.Text ?? "",
                Lines = _rows.Select(r => r.Line).ToList(),
            };
            saved = App.Services.Vouchers.Create(v);
        });
        if (!ok || saved is null) return null;
        var main = MainWindow.Of(this);
        Reset();
        LocationBox.ItemsSource = App.Services.Stock.DistinctLocations();
        main?.SetSubtitle($"Đã lưu phiếu {saved.No} lúc {DateTime.Now:HH:mm}. Có thể lập phiếu tiếp.");
        return saved;
    }

    private async void OnSave(object? sender, RoutedEventArgs e) => await Save();

    private async void OnSavePrint(object? sender, RoutedEventArgs e)
    {
        var v = await Save();
        if (v is not null) await PrintHelper.Print(this, v.Id);
    }
}
