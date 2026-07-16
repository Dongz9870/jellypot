using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using JellyPot.App.Models;

namespace JellyPot.App.Views;

public partial class AddCategoryWindow : Window
{
    private readonly List<JellyfinLibrary> _libraries;
    public MediaCategory? ResultCategory { get; private set; }

    public AddCategoryWindow(IEnumerable<JellyfinLibrary> libraries)
    {
        InitializeComponent();
        _libraries = libraries.Where(x => x.CollectionType is "movies" or "tvshows").ToList();
        RefreshLibraries();
    }

    private void MediaTypeBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsInitialized) RefreshLibraries();
    }

    private void RefreshLibraries()
    {
        var itemType = SelectedItemType();
        var collectionType = itemType == "Series" ? "tvshows" : "movies";
        LibraryBox.ItemsSource = _libraries.Where(x => string.Equals(x.CollectionType, collectionType, StringComparison.OrdinalIgnoreCase)).ToList();
        LibraryBox.SelectedIndex = LibraryBox.Items.Count > 0 ? 0 : -1;
    }

    private void BrowseWindowsRoot_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择此分类的 Windows 片源根目录", Multiselect = false };
        if (dialog.ShowDialog(this) == true) WindowsRootBox.Text = dialog.FolderName;
    }

    private void Add_OnClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        var name = CategoryNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) { ErrorText.Text = "请输入分类名称。"; return; }
        if (LibraryBox.SelectedItem is not JellyfinLibrary library) { ErrorText.Text = "当前媒体类型没有可用的 Jellyfin 片源库。"; return; }
        var serverRoot = ServerRootBox.Text.Trim();
        var windowsRoot = WindowsRootBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(serverRoot) != string.IsNullOrWhiteSpace(windowsRoot))
        {
            ErrorText.Text = "创建路径映射时，服务端根路径和 Windows 根路径必须同时填写。";
            return;
        }
        ResultCategory = new MediaCategory
        {
            Name = name,
            ItemType = SelectedItemType(),
            LibraryId = library.Id,
            ServerRoot = serverRoot,
            WindowsRoot = windowsRoot
        };
        DialogResult = true;
    }

    private string SelectedItemType() => (MediaTypeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Movie";
    private void Cancel_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
