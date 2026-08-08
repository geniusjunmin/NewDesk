using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NewDesk.Models.Ai;

namespace NewDesk.Dialogs;

public partial class CloudAiConsentDialog : Window
{
    public bool IsAllowed { get; private set; }

    public CloudAiConsentDialog(CloudSendPreview preview)
    {
        InitializeComponent();

        TxtProvider.Text = preview.ProviderName;
        TxtModel.Text = preview.Model;
        TxtEndpoint.Text = preview.EndpointHost;
        TxtPreview.Text = preview.SanitizedPreview;

        if (preview.IncludesReminderContext)
        {
            PanelContextItems.Children.Add(CreateContextItem("✓ 提醒事项上下文"));
        }
        if (preview.IncludesWallpaperContext)
        {
            PanelContextItems.Children.Add(CreateContextItem("✓ 壁纸元素上下文"));
        }
        if (preview.IncludesDynamicDataContext)
        {
            PanelContextItems.Children.Add(CreateContextItem("✓ 动态数据源上下文"));
        }
        if (preview.IncludesClipboard)
        {
            PanelContextItems.Children.Add(CreateContextItem("✓ 剪贴板内容"));
        }
    }

    private TextBlock CreateContextItem(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8")),
            FontSize = 12,
            Margin = new Thickness(0, 2, 0, 2)
        };
    }

    private void BtnAllow_Click(object sender, RoutedEventArgs e)
    {
        IsAllowed = true;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        IsAllowed = false;
        DialogResult = false;
        Close();
    }
}
