using System.Windows;

namespace NewDesk;

public partial class ChangeMasterPasswordWindow : Window
{
    public string OldPassword => OldPasswordBox.Password;
    public string NewPassword => NewPasswordBox.Password;

    public ChangeMasterPasswordWindow()
    {
        InitializeComponent();
        OldPasswordBox.Focus();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorTextBlock.Text = string.Empty;
        ErrorTextBlock.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(NewPasswordBox.Password))
        {
            ErrorTextBlock.Text = "新密码不能为空。";
            ErrorTextBlock.Visibility = Visibility.Visible;
            return;
        }

        if (NewPasswordBox.Password != ConfirmNewPasswordBox.Password)
        {
            ErrorTextBlock.Text = "两次输入的新密码不匹配。";
            ErrorTextBlock.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }
}
