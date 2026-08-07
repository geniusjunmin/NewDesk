using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NewDesk.Services;

namespace NewDesk;

public class ApiConfig
{
    public string Url { get; set; } = string.Empty;
    public string Regex { get; set; } = string.Empty;
    public string Formatting { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;
    public string Suffix { get; set; } = string.Empty;
}

public partial class ApiTextConfigWindow : Window
{
    private static readonly HttpClient HttpClient = new();
    public ApiConfig? Config { get; private set; }
    private string _rawResponse = string.Empty;

    public ApiTextConfigWindow()
    {
        InitializeComponent();
    }

    public ApiTextConfigWindow(ApiConfig config) : this()
    {
        Config = config;
        
        // Explicitly set to Custom (Index 0) first. 
        PresetComboBox.SelectedIndex = 0; 
        
        ApiUrlTextBox.Text = config.Url;
        ApiRegexTextBox.Text = config.Regex;
        ApiPrefixTextBox.Text = config.Prefix;
        ApiSuffixTextBox.Text = config.Suffix;
        
        foreach (ComboBoxItem item in ApiFormattingComboBox.Items)
        {
            if (item.Content.ToString() == config.Formatting)
            {
                ApiFormattingComboBox.SelectedItem = item;
                break;
            }
        }
    }

    private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetParamGrid == null) return; 

        var selectedItem = PresetComboBox.SelectedItem as ComboBoxItem;
        if (selectedItem == null) return;

        string preset = selectedItem.Content.ToString() ?? "";

        switch (preset)
        {
            case "天气 - 当前温度 (wttr.in)":
                PresetParamGrid.Visibility = Visibility.Visible;
                PresetParamLabel.Content = "城市名称:";
                ApiUrlTextBox.Text = "https://wttr.in/{CITY}?format=j1";
                ApiRegexTextBox.Text = @"""temp_C"": ""(-?\d+)""";
                ApiUrlTextBox.IsEnabled = false;
                ApiRegexTextBox.IsEnabled = false;
                ApiFormattingComboBox.IsEnabled = false;
                ApiFormattingComboBox.SelectedIndex = 0;
                break;
            case "天气 - 天气描述 (wttr.in)":
                PresetParamGrid.Visibility = Visibility.Visible;
                PresetParamLabel.Content = "城市名称:";
                ApiUrlTextBox.Text = "https://wttr.in/{CITY}?format=j1";
                ApiRegexTextBox.Text = @"""lang_zh-cn"":\s*\[\s*\{\s*""value"": ""([^""]+)""\s*\}\s*\]";
                ApiUrlTextBox.IsEnabled = false;
                ApiRegexTextBox.IsEnabled = false;
                ApiFormattingComboBox.IsEnabled = false;
                ApiFormattingComboBox.SelectedIndex = 0;
                break;
            case "自定义":
            default:
                PresetParamGrid.Visibility = Visibility.Collapsed;
                // Don't clear if we just loaded the window from an existing config
                if (IsLoaded)
                {
                    ApiUrlTextBox.IsEnabled = true;
                    ApiRegexTextBox.IsEnabled = true;
                    ApiFormattingComboBox.IsEnabled = true;
                }
                break;
        }
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        string url = ApiUrlTextBox.Text;
        if (PresetComboBox.SelectedIndex > 0)
        {
            if (string.IsNullOrWhiteSpace(PresetParamTextBox.Text))
            {
                Services.ToastManager.Show("提示", "请输入预设参数（如城市名）。", Services.ToastType.Warning);
                return;
            }
            url = url.Replace("{CITY}", PresetParamTextBox.Text);
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            Services.ToastManager.Show("提示", "请输入 API URL。", Services.ToastType.Warning);
            return;
        }

        TestButton.IsEnabled = false;
        TestButton.Content = "加载中...";
        ResultPreviewTextBlock.Text = "正在请求数据...";
        RawResponseTextBox.Text = "";
        JsonHelperContainer.Children.Clear();
        JsonHelperScroll.Visibility = Visibility.Collapsed;
        RawResponseTextBox.Visibility = Visibility.Visible;
        JsonHelperTip.Visibility = Visibility.Collapsed;

        try
        {
            _rawResponse = await HttpClient.GetStringAsync(url);
            RawResponseTextBox.Text = _rawResponse;
            
            // Try parse JSON for helper
            try
            {
                using var doc = JsonDocument.Parse(_rawResponse);
                PopulateJsonHelper(doc.RootElement);
                JsonHelperScroll.Visibility = Visibility.Visible;
                RawResponseTextBox.Visibility = Visibility.Collapsed;
                JsonHelperTip.Visibility = Visibility.Visible;
            }
            catch { /* Not a JSON or too complex */ }

            UpdatePreview();
        }
        catch (Exception ex)
        {
            Services.ToastManager.Show("错误", $"请求失败: {ex.Message}", Services.ToastType.Error);
            ResultPreviewTextBlock.Text = "请求失败";
        }
        finally
        {
            TestButton.IsEnabled = true;
            TestButton.Content = "测试连接";
        }
    }

    private void PopulateJsonHelper(JsonElement element, string prefix = "")
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                string key = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                
                if (prop.Value.ValueKind == JsonValueKind.Object || prop.Value.ValueKind == JsonValueKind.Array)
                {
                    PopulateJsonHelper(prop.Value, key);
                }
                else
                {
                    string fieldName = prop.Name;
                    string fieldValue = prop.Value.ToString();
                    var btn = new Button
                    {
                        Content = $"{key}: {fieldValue}",
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Margin = new Thickness(0, 2, 0, 2),
                        Padding = new Thickness(8, 4, 8, 4),
                        Background = Brushes.Transparent,
                        BorderBrush = (Brush)FindResource("BorderColor"),
                        BorderThickness = new Thickness(0, 0, 0, 1),
                        Cursor = Cursors.Hand,
                        Tag = fieldName
                    };
                    btn.Click += (s, e) => {
                        try
                        {
                            // Generate Regex for this field
                            // Escape field name to handle special characters
                            string escapedName = Regex.Escape(fieldName);
                            ApiRegexTextBox.Text = $@"""{escapedName}"":\s*""?([^"",\]\s}}]+)""?";
                            Services.ToastManager.Show("提示", "正则表达式已自动刷新。", Services.ToastType.Info);
                        }
                        catch (Exception ex)
                        {
                            Services.ToastManager.Show("发生错误", ex.Message, Services.ToastType.Error);
                        }
                    };
                    JsonHelperContainer.Children.Add(btn);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (var item in element.EnumerateArray())
            {
                if (index < 5) // Only show first few items to avoid clutter
                    PopulateJsonHelper(item, $"{prefix}[{index}]");
                index++;
            }
        }
    }

    private void ContentChanged_TriggerPreview(object sender, TextChangedEventArgs e)
    {
        UpdatePreview();
    }

    private void ApiFormattingComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (ResultPreviewTextBlock == null || string.IsNullOrEmpty(_rawResponse)) return;

        string regexPattern = ApiRegexTextBox.Text;
        if (string.IsNullOrEmpty(regexPattern))
        {
            ResultPreviewTextBlock.Text = "请输入正则表达式";
            return;
        }

        try
        {
            // Add a timeout to prevent hang on catastrophic backtracking
            var match = Regex.Match(_rawResponse, regexPattern, RegexOptions.None, TimeSpan.FromMilliseconds(500));
            if (match.Success)
            {
                string value = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
                
                // Formatting
                if (ApiFormattingComboBox.SelectedIndex == 1) // B -> MB/GB
                {
                    if (long.TryParse(value, out long b))
                    {
                        if (b >= 1024 * 1024 * 1024) value = (b / 1024.0 / 1024.0 / 1024.0).ToString("F2") + " GB";
                        else value = (b / 1024.0 / 1024.0).ToString("F2") + " MB";
                    }
                }
                
                // Apply Prefix/Suffix
                string result = ApiPrefixTextBox.Text + value + ApiSuffixTextBox.Text;
                ResultPreviewTextBlock.Text = result;
            }
            else
            {
                ResultPreviewTextBlock.Text = "(匹配失败)";
            }
        }
        catch (RegexMatchTimeoutException)
        {
            ResultPreviewTextBlock.Text = "(正则匹配超时)";
        }
        catch (Exception ex)
        {
            ResultPreviewTextBlock.Text = $"(正则错误: {ex.Message})";
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        string url = ApiUrlTextBox.Text;
        string regex = ApiRegexTextBox.Text;

        if (PresetComboBox.SelectedIndex > 0)
        {
            if (string.IsNullOrWhiteSpace(PresetParamTextBox.Text))
            {
                Services.ToastManager.Show("输入错误", "请输入城市名称。", Services.ToastType.Warning);
                return;
            }
            url = url.Replace("{CITY}", PresetParamTextBox.Text);
        }

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(regex))
        {
            Services.ToastManager.Show("输入错误", "API URL 和正则表达式不能为空。", Services.ToastType.Warning);
            return;
        }

        Config = new ApiConfig
        {
            Url = url,
            Regex = regex,
            Formatting = (ApiFormattingComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "无",
            Prefix = ApiPrefixTextBox.Text,
            Suffix = ApiSuffixTextBox.Text
        };

        DialogResult = true;
    }
}
