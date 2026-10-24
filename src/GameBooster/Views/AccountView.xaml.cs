using System.Windows;
using GameBooster.ViewModels;

namespace GameBooster.Views;

public partial class AccountView : System.Windows.Controls.UserControl
{
    public AccountView() => InitializeComponent();

    private void Login_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is AccountViewModel viewModel)
            viewModel.LoginCommand.Execute(PasswordInput.Password);
    }

    private void Register_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is AccountViewModel viewModel)
            viewModel.RegisterCommand.Execute(PasswordInput.Password);
    }
}
