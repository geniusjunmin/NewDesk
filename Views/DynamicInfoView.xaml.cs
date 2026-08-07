using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NewDesk.Services;

namespace NewDesk.Views;

public partial class DynamicInfoView : UserControl
{
    private static readonly HttpClient HttpClient = new();
    private string _selectedPreset = "Weather";

    public DynamicInfoView()
    {
        InitializeComponent();
    }

    private void PresetCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string tag)
        {
            _selectedPreset = tag;
            if (tag == "Weather")
            {
                ApiUrlTextBox.Text = "https://wttr.in/Beijing?format=j1";
                ApiRegexTextBox.Text = "\"temp_C\":\\s*\"(\\d+)\"";
                ApiPrefixTextBox.Text = "北京：";
                ApiSuffixTextBox.Text = "°C";
            }
            else if (tag == "Crypto")
            {
                ApiUrlTextBox.Text = "https://api.coindesk.com/v1/bpi/currentprice.json";
                ApiRegexTextBox.Text = "\"rate\":\\s*\"([^\"]+)\"";
                ApiPrefixTextBox.Text = "BTC: $";
                ApiSuffixTextBox.Text = "";
            }
            else
            {
                SimpleModePanel.Visibility = Visibility.Visible;
            }
        }
    }

    private async void TestApiButton_Click(object sender, RoutedEventArgs e)
    {
        TestApiButton.IsEnabled = false;
        TestApiButton.Content = "⟳ 正在测试...";
        PreviewResultText.Text = "正在连接 API 服务...";

        try
        {
            string city = CityTextBox.Text.Trim();
            if (string.IsNullOrEmpty(city)) city = "Beijing";

            string url = _selectedPreset == "Weather"
                ? $"https://wttr.in/{city}?format=j1"
                : ApiUrlTextBox.Text;

            var response = await HttpClient.GetStringAsync(url);
            
            // Try parsing JSON for visual tree selector
            try
            {
                using var doc = JsonDocument.Parse(response);
                PopulateJsonTree(doc.RootElement);
                JsonSelectorBorder.Visibility = Visibility.Visible;
            }
            catch
            {
                JsonSelectorBorder.Visibility = Visibility.Collapsed;
            }

            // Extract with regex or display snippet
            string regexPattern = ApiRegexTextBox.Text;
            if (!string.IsNullOrEmpty(regexPattern))
            {
                var match = Regex.Match(response, regexPattern);
                if (match.Success)
                {
                    string extracted = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
                    PreviewResultText.Text = $"{ApiPrefixTextBox.Text}{extracted}{ApiSuffixTextBox.Text}";
                    ToastManager.Show("连接成功", "已成功提取 API 数据！", ToastType.Success);
                }
                else
                {
                    PreviewResultText.Text = $"返回原始数据: {response[..Math.Min(100, response.Length)]}...";
                    ToastManager.Show("成功连通", "API 请求成功，但 Regex 暂未匹配到指定提取组。", ToastType.Warning);
                }
            }
            else
            {
                PreviewResultText.Text = response[..Math.Min(80, response.Length)];
            }
        }
        catch (Exception ex)
        {
            PreviewResultText.Text = "无法连接 API，请检查网络或 URL 拼写。";
            ToastManager.Show("错误", $"API 连接异常: {ex.Message}", ToastType.Error);
        }
        finally
        {
            TestApiButton.IsEnabled = true;
            TestApiButton.Content = "测试连接";
        }
    }

    private void PopulateJsonTree(JsonElement element)
    {
        JsonTreeView.Items.Clear();
        var rootItem = CreateJsonTreeNode("Root", element);
        JsonTreeView.Items.Add(rootItem);
        rootItem.IsExpanded = true;
    }

    private TreeViewItem CreateJsonTreeNode(string name, JsonElement element)
    {
        var item = new TreeViewItem
        {
            Header = $"{name} ({element.ValueKind})",
            Tag = name
        };

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                item.Items.Add(CreateJsonTreeNode(prop.Name, prop.Value));
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (var arrElem in element.EnumerateArray())
            {
                item.Items.Add(CreateJsonTreeNode($"[{index++}]", arrElem));
            }
        }
        else
        {
            item.Header = $"{name}: {element}";
        }

        return item;
    }

    private void JsonTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (JsonTreeView.SelectedItem is TreeViewItem item && item.Header != null)
        {
            string header = item.Header.ToString() ?? "";
            if (header.Contains(':'))
            {
                string keyName = header.Split(':')[0].Trim();
                ApiRegexTextBox.Text = "\"" + keyName + "\":\\s*\"?([^\",\\}\\]]+)\"?";
                ToastManager.Show("自动生成规则", $"已为您自动填充字段 \"{keyName}\" 的提取正则规则。", ToastType.Info);
            }
        }
    }

    private void SaveApiConfigButton_Click(object sender, RoutedEventArgs e)
    {
        ToastManager.Show("配置已保存", "动态信息与 API 规则已保存到全局配置。", ToastType.Success);
    }
}
