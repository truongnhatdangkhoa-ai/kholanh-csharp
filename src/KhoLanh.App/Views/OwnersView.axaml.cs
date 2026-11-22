using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using KhoLanh.Core.Models;

namespace KhoLanh.App.Views;

public partial class OwnersView : UserControl, IPage
{
    public string Title => "Danh mục chủ hàng";
    public string Subtitle => "Người sở hữu hàng: hàng của kho hoặc hàng khách gửi.";
    private Owner _editing = new();
    private List<Owner> _data = new();

    public OwnersView()
    {
        InitializeComponent();
        var canEdit = App.Services.Session.Can("owners", Perm.Edit);
        SaveButton.IsVisible = DeleteButton.IsVisible = NewButton.IsVisible = canEdit;
        ReadOnlyText.IsVisible = !canEdit;
        SearchBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Load(); };
        Load();
        Edit(new Owner());
    }

    private void Load()
    {
        _data = App.Services.Catalog.ListOwners(SearchBox.Text, InactiveBox.IsChecked == true);
        Grid.ItemsSource = _data;
        SummaryText.Text = $"{_data.Count} chủ hàng";
    }

    private void Edit(Owner o)
    {
        _editing = o;
        FormTitle.Text = o.Id == 0 ? "Chủ hàng mới" : $"Sửa chủ hàng {o.Code}";
        CodeBox.Text = o.Code; NameBox.Text = o.Name; PhoneBox.Text = o.Phone; AddressBox.Text = o.Address; NoteBox.Text = o.Note;
        ActiveBox.IsChecked = o.IsActive;
        DeleteButton.IsEnabled = o.Id != 0;
    }

    private void OnSelect(object? sender, SelectionChangedEventArgs e)
    {
        if (Grid.SelectedItem is Owner o)
            Edit(new Owner { Id = o.Id, Code = o.Code, Name = o.Name, Phone = o.Phone, Address = o.Address, Note = o.Note, IsActive = o.IsActive });
    }

    private void OnSearch(object? sender, RoutedEventArgs e) => Load();
    private void OnNew(object? sender, RoutedEventArgs e) { Grid.SelectedItem = null; Edit(new Owner()); CodeBox.Focus(); }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        var ok = await Ui.Run(this, () =>
        {
            _editing.Code = CodeBox.Text ?? ""; _editing.Name = NameBox.Text ?? ""; _editing.Phone = (PhoneBox.Text ?? "").Trim();
            _editing.Address = (AddressBox.Text ?? "").Trim(); _editing.Note = (NoteBox.Text ?? "").Trim(); _editing.IsActive = ActiveBox.IsChecked == true;
            App.Services.Catalog.SaveOwner(_editing);
        });
        if (!ok) return;
        var id = _editing.Id;
        Load();
        Grid.SelectedItem = _data.FirstOrDefault(x => x.Id == id);
    }

    private async void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (_editing.Id == 0) return;
        if (!await Ui.Confirm(this, $"Xóa chủ hàng {_editing.Code}? Nếu đã có phiếu, phần mềm chỉ chuyển sang ngừng dùng.", "Xóa chủ hàng", "Xóa")) return;
        string msg = "";
        if (await Ui.Run(this, () => msg = App.Services.Catalog.DeleteOwner(_editing.Id)))
        {
            Load(); Edit(new Owner());
            await Ui.Message(this, msg);
        }
    }
}
