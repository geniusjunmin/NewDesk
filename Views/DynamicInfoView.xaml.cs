using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NewDesk.Models;
using NewDesk.Services;

namespace NewDesk.Views;

public partial class DynamicInfoView : UserControl
{
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
            SourceListBox.ItemsSource = _sources;
            SourceListBox.SelectedItem = _selectedSource;
            PopulateSourceToUI(_selectedSource);
        }
    }

    private void PopulateSourceToUI(DynamicDataSource source)
    {
        _selectedPreset = "Custom";
        SourceNameTextBox.Text = source.Name;
        ApiUrlTextBox.Text = source.Url;
        ApiRegexTextBox.Text = source.ExtractionRule;
        ExtractionTypeComboBox.SelectedIndex = source.ExtractionType switch
        {
            "Regex" => 1,
            "Raw" => 2,
            _ => 0
        };
        ApiPrefixTextBox.Text = source.FormatPrefix;
        ApiSuffixTextBox.Text = source.FormatSuffix;
        PreviewResultText.Text = !string.IsNullOrEmpty(source.LastCachedValue) ? source.LastCachedValue : "点击“测试连接”查看 API 返回数据";
    }

    private void SyncUIToSource(DynamicDataSource source)
    {
        source.Name = string.IsNullOrWhiteSpace(SourceNameTextBox.Text) ? "未命名数据源" : SourceNameTextBox.Text.Trim();
        source.Url = ApiUrlTextBox.Text.Trim();
        source.ExtractionRule = ApiRegexTextBox.Text.Trim();
        source.ExtractionType = (ExtractionTypeComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "JsonPath";
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
                ExtractionTypeComboBox.SelectedIndex = 0;
                ApiPrefixTextBox.Text = "北京：";
                ApiSuffixTextBox.Text = "°C";
            }
            else if (tag == "Crypto")
            {
                ApiUrlTextBox.Text = "https://api.coindesk.com/v1/bpi/currentprice.json";
                ApiRegexTextBox.Text = "$.bpi.USD.rate";
                ExtractionTypeComboBox.SelectedIndex = 0;
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
                ExtractionType = (ExtractionTypeComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "JsonPath",
                ExtractionRule = ApiRegexTextBox.Text.Trim(),
                FormatPrefix = ApiPrefixTextBox.Text,
                FormatSuffix = ApiSuffixTextBox.Text,
                Method = _selectedSource?.Method ?? "GET",
                Headers = _selectedSource != null ? new Dictionary<string, string>(_selectedSource.Headers) : new(),
                SecretHeaders = _selectedSource != null ? new Dictionary<string, string>(_selectedSource.SecretHeaders) : new()
            };

            var result = await DynamicDataService.TestSourceAsync(tempSource);
            PreviewResultText.Text = result.Success ? result.Value : result.Error;
            if (!result.Success)
            {
                JsonSelectorBorder.Visibility = Visibility.Collapsed;
                ToastManager.Show("连接失败", result.Error, ToastType.Error);
                return;
            }

            try
            {
                using var doc = JsonDocument.Parse(result.RawContent);
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
            ExtractionTypeComboBox.SelectedIndex = 0;
            ToastManager.Show("自动提取规则", $"已自动填充 JsonPath 提取规则: {jsonPath}", ToastType.Info);
        }
    }

    private void SaveApiConfigButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSource == null)
        {
            _selectedSource = new DynamicDataSource { Name = "未命名数据源" };
            _sources.Add(_selectedSource);
        }

        SyncUIToSource(_selectedSource);

        var res = DynamicDataService.SaveSources(_sources);
        if (res.IsSuccess)
        {
            SourceListBox.ItemsSource = null;
            SourceListBox.ItemsSource = _sources;
            SourceListBox.SelectedItem = _selectedSource;
            ToastManager.Show("配置已保存", "动态信息与 API 规则已成功保存！", ToastType.Success);
        }
        else
        {
            ToastManager.Show("保存失败", $"动态信息保存失败: {res.Message}", ToastType.Error);
        }
    }

    private void SourceListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourceListBox.SelectedItem is not DynamicDataSource source || ReferenceEquals(source, _selectedSource)) return;
        _selectedSource = source;
        PopulateSourceToUI(source);
    }

    private void NewSourceButton_Click(object sender, RoutedEventArgs e)
    {
        var source = new DynamicDataSource { Name = "新数据源", ExtractionType = "JsonPath" };
        _sources.Add(source);
        SourceListBox.ItemsSource = null;
        SourceListBox.ItemsSource = _sources;
        SourceListBox.SelectedItem = source;
    }

    private void DeleteSourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSource == null) return;
        var removed = _selectedSource;
        int removedIndex = _sources.IndexOf(removed);
        _sources.Remove(removed);
        _selectedSource = null;
        var result = DynamicDataService.SaveSources(_sources);
        if (!result.IsSuccess)
        {
            _sources.Insert(Math.Max(0, removedIndex), removed);
            _selectedSource = removed;
            ToastManager.Show("删除失败", result.Message, ToastType.Error);
            return;
        }

        SourceListBox.ItemsSource = null;
        SourceListBox.ItemsSource = _sources;
        if (_sources.Count > 0) SourceListBox.SelectedIndex = 0;
    }
}
