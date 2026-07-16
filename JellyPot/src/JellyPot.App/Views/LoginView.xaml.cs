using System.Windows;
using System.Windows.Controls;
using JellyPot.App.ViewModels;

namespace JellyPot.App.Views;

public partial class LoginView : UserControl
{
    public LoginView() => InitializeComponent();

    private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel viewModel && sender is PasswordBox passwordBox)
            viewModel.Password = passwordBox.Password;
    }
}
