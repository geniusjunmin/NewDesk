using System.Windows;

namespace NewDesk;

public partial class InputDialog : Window
{
    public string InputText => InputTextBox.Text;

    public InputDialog(string prompt) : this("输入提示", prompt, "")
    {
    }

    public InputDialog(string title, string prompt, string defaultText = "")
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        InputTextBox.Text = defaultText;
        InputTextBox.Focus();
        InputTextBox.SelectAll();
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
