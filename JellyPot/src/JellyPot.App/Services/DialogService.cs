using Microsoft.Win32;
using System.Windows;
using JellyPot.App.Models;
using JellyPot.App.Views;

namespace JellyPot.App.Services;

public sealed class DialogService
{
    public string? SelectFolder(string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择 Windows 可访问的电影根目录",
            Multiselect = false,
            InitialDirectory = !string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory) ? initialDirectory : null
        };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? SelectPotPlayer()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 PotPlayer 可执行文件",
            Filter = "PotPlayer (*.exe)|*.exe|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void ShowError(string title, string message) => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    public void ShowInfo(string title, string message) => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    public bool Confirm(string title, string message) => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public MediaCategory? ShowAddCategory(IEnumerable<JellyfinLibrary> libraries)
    {
        var window = new AddCategoryWindow(libraries) { Owner = Application.Current.MainWindow };
        return window.ShowDialog() == true ? window.ResultCategory : null;
    }
}
