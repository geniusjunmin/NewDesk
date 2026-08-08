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
using NewDesk.Models;
using NewDesk.Services;

namespace NewDesk.Views;

public partial class DynamicInfoView : UserControl
{
    private static readonly HttpClient HttpClient = new();
    private List<DynamicDataSource> _sources = new();
    private DynamicDataSource? _selectedSource;
    private string _selectedPreset = "Weather";

    public DynamicInfoView()
    {
        InitializeComponent();
        Loaded += DynamicInfoView_Loaded;
    }

    private void DynamicInfoView_Loaded(object sender, RoutedEventArgs e)
    {
        LoadSources();
    }

    private void LoadSources()
    {
        _sources = DynamicDataService.LoadSources();
        if (_sources.Count == 0)
        {
            _sources = DynamicDataService.GetDefaultPresets();
            DynamicDataService.SaveSources(_sources);
        }

        if (_sources.Count > 0)
        {
            _selectedSource = _sources[0];
            PopulateSourceToUI(_selectedSource);
        }
    }

    private void PopulateSourceToUI(DynamicDataSource source)
    {
        ApiUrlTextBox.Text = source.Url;
        ApiRegexTextBox.Text = source.ExtractionRule;
        ApiPrefixTextBox.Text = source.FormatPrefix;
        ApiSuffixTextBox.Text = source.FormatSuffix;
        PreviewResultText.Text = !string.IsNullOrEmpty(source.LastCachedValue) ? source.LastCachedValue : "点击“测试连接”查看 API 返回数据";
    }

    private void SyncUIToSource(DynamicDataSource source)
    {
        source.Url = ApiUrlTextBox.Text.Trim();
        source.ExtractionRule = ApiRegexTextBox.Text.Trim();
        source.FormatPrefix = ApiPrefixTextBox.Text;
        source.FormatSuffix = ApiSuffixTextBox.Text;
    }

    private void PresetCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string tag)
        {
            _selectedPreset = tag;
            if (tag == "Weather")
            {
                ApiUrlTextBox.Text = "https://wttr.in/Beijing?format=j1";
                ApiRegexTextBox.Text = "$.current_condition[0].temp_C";
                ApiPrefixTextBox.Text = "北京：";
                ApiSuffixTextBox.Text = "°C";
            }
            else if (tag == "Crypto")
            {
                ApiUrlTextBox.Text = "https://api.coindesk.com/v1/bpi/currentprice.json";
                ApiRegexTextBox.Text = "$.bpi.USD.rate";
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
                : ApiUrlTextBox.Text.Trim();

            if (string.IsNullOrEmpty(url))
            {
                ToastManager.Show("提示", "请输入有效的 API URL。", ToastType.Warning);
                return;
            }

            var tempSource = new DynamicDataSource
            {
                Name = "Test Source",
                Url = url,
                ExtractionType = ApiRegexTextBox.Text.StartsWith("$") ? "JsonPath" : "Regex",
                ExtractionRule = ApiRegexTextBox.Text.Trim(),
                FormatPrefix = ApiPrefixTextBox.Text,
                FormatSuffix = ApiSuffixTextBox.Text
            };

            string resultValue = await DynamicDataService.FetchValueAsync(tempSource, forceRefresh: true);
            PreviewResultText.Text = resultValue;

            // Fetch raw JSON to populate JsonTreeView selector
            try
            {
                string response = await HttpClient.GetStringAsync(url);
                using var doc = JsonDocument.Parse(response);
                PopulateJsonTree(doc.RootElement);
                JsonSelectorBorder.Visibility = Visibility.Visible;
            }
            catch
            {
                JsonSelectorBorder.Visibility = Visibility.Collapsed;
            }

            ToastManager.Show("连接成功", "已成功提取 API 数据！", ToastType.Success);
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
        var rootItem = CreateJsonTreeNode("$", "$", element);
        JsonTreeView.Items.Add(rootItem);
        rootItem.IsExpanded = true;
    }

    private TreeViewItem CreateJsonTreeNode(string keyName, string fullPath, JsonElement element)
    {
        var item = new TreeViewItem
        {
            Header = $"{keyName} ({element.ValueKind})",
            Tag = fullPath
        };

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                string childPath = fullPath == "$" ? $"$.{prop.Name}" : $"{fullPath}.{prop.Name}";
                item.Items.Add(CreateJsonTreeNode(prop.Name, childPath, prop.Value));
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (var arrElem in element.EnumerateArray())
            {
                string childPath = $"{fullPath}[{index}]";
                item.Items.Add(CreateJsonTreeNode($"[{index++}]", childPath, arrElem));
            }
        }
        else
        {
            item.Header = $"{keyName}: {element}";
        }

        return item;
    }

    private void JsonTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (JsonTreeView.SelectedItem is TreeViewItem item && item.Tag is string jsonPath)
        {
            ApiRegexTextBox.Text = jsonPath;
            ToastManager.Show("自动提取规则", $"已自动填充 JsonPath 提取规则: {jsonPath}", ToastType.Info);
        }
    }

    private void SaveApiConfigButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSource == null && _sources.Count > 0)
        {
            _selectedSource = _sources[0];
        }

        if (_selectedSource == null)
        {
            _selectedSource = new DynamicDataSource { Name = "默认 API 数据源" };
            _sources.Add(_selectedSource);
        }

        SyncUIToSource(_selectedSource);

        var res = DynamicDataService.SaveSources(_sources);
        if (res.IsSuccess)
        {
            ToastManager.Show("配置已保存", "动态信息与 API 规则已成功保存！", ToastType.Success);
        }
        else
        {
            ToastManager.Show("保存失败", $"动态信息保存失败: {res.Message}", ToastType.Error);
        }
    }
}
