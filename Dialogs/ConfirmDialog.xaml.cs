using System.Windows;
using System.Windows.Media;

namespace NewDesk.Dialogs;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, bool isDanger = true, string confirmText = "删除", string cancelText = "取消")
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.Content = cancelText;

        if (!isDanger)
        {
            ConfirmButton.Style = (Style)FindResource("ModernButton");
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
