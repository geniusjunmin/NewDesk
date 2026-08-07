using System.Windows;

namespace NewDesk;

public partial class MasterPasswordWindow : Window
{
    public string Password => MasterPasswordBox.Password;
    private bool _isCreateMode;

    public MasterPasswordWindow(bool isCreateMode)
    {
        InitializeComponent();
        _isCreateMode = isCreateMode;

        if (_isCreateMode)
        {
            Title = "创建主密码";
            InstructionTextBlock.Text = "请创建一个新的主密码:";
            ConfirmTextBlock.Visibility = Visibility.Visible;
            ConfirmPasswordBox.Visibility = Visibility.Visible;
        }
        else
        {
            Title = "输入主密码";
            InstructionTextBlock.Text = "请输入您的主密码:";
        }
        
        MasterPasswordBox.Focus();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorTextBlock.Text = string.Empty;
        ErrorTextBlock.Visibility = Visibility.Collapsed;

        if (_isCreateMode)
        {
            if (string.IsNullOrWhiteSpace(MasterPasswordBox.Password))
            {
                ErrorTextBlock.Text = "密码不能为空。";
                ErrorTextBlock.Visibility = Visibility.Visible;
                return;
            }
            if (MasterPasswordBox.Password != ConfirmPasswordBox.Password)
            {
                ErrorTextBlock.Text = "两次输入的密码不匹配。";
                ErrorTextBlock.Visibility = Visibility.Visible;
                return;
            }
        }
        
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
