using System.Windows;

namespace NewDesk;

public partial class InputDialog : Window
{
    public string InputText => InputTextBox.Text;

    public InputDialog(string prompt)
    {
        InitializeComponent();
        PromptText.Text = prompt;
        InputTextBox.Focus();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(InputTextBox.Text))
        {
            new ReminderToastWindow("提示", "名称不能为空。").Show();
            return;
        }
        DialogResult = true;
    }
}
