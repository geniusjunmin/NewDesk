using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using NewDesk.Models;
using NewDesk.Services;

namespace NewDesk.Dialogs;

public partial class ApiDataWizardWindow : Window
{
    private static readonly HttpClient HttpClient = new();
    private readonly TextElementState _element;
    private string _extractedValue = "18";

    public ApiDataWizardWindow(TextElementState element)
    {
        InitializeComponent();
        _element = element;

        ApiUrlTextBox.Text = string.IsNullOrEmpty(element.ApiUrl) ? "https://wttr.in/Beijing?format=j1" : element.ApiUrl;
        RegexTextBox.Text = element.ApiRegex ?? "";
        PrefixTextBox.Text = element.ApiPrefix ?? "数据：";
        SuffixTextBox.Text = element.ApiSuffix ?? "";
        FormattingTextBox.Text = element.ApiFormatting ?? "";

        UpdatePreview();
    }

    private async void TestConnectButton_Click(object sender, RoutedEventArgs e)
    {
        TestConnectButton.IsEnabled = false;
        TestConnectButton.Content = "⟳ 测试中...";
        StatusText.Text = "正在尝试连接 API 地址...";

        try
        {
            string url = ApiUrlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(url)) return;

            string json = await HttpClient.GetStringAsync(url);
            StatusText.Text = "✓ API 连接成功！已解析返回的 JSON 数据。";

            try
            {
                using var doc = JsonDocument.Parse(json);
                PopulateJsonTree(doc.RootElement);
                Step2Border.Visibility = Visibility.Visible;
            }
            catch
            {
                Step2Border.Visibility = Visibility.Collapsed;
            }

            if (!string.IsNullOrEmpty(RegexTextBox.Text))
            {
                var match = Regex.Match(json, RegexTextBox.Text);
                if (match.Success)
                {
                    _extractedValue = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
                }
            }
            else
            {
                _extractedValue = json[..Math.Min(30, json.Length)];
            }

            UpdatePreview();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"❌ 连接失败: {ex.Message}";
        }
        finally
        {
            TestConnectButton.IsEnabled = true;
            TestConnectButton.Content = "测试连接";
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
                string[] parts = header.Split(':');
                string keyName = parts[0].Trim();
                _extractedValue = parts[1].Trim();

                RegexTextBox.Text = "\"" + keyName + "\":\\s*\"?([^\",\\}\\]]+)\"?";
                UpdatePreview();
            }
        }
    }

    private void FormatPreview_Changed(object sender, TextChangedEventArgs e)
    {
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (PreviewText != null)
        {
            PreviewText.Text = $"{PrefixTextBox.Text}{_extractedValue}{SuffixTextBox.Text}";
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _element.DynamicType = "Api";
        _element.ApiUrl = ApiUrlTextBox.Text.Trim();
        _element.ApiRegex = RegexTextBox.Text.Trim();
        _element.ApiPrefix = PrefixTextBox.Text;
        _element.ApiSuffix = SuffixTextBox.Text;
        _element.ApiFormatting = FormattingTextBox.Text.Trim();
        _element.Text = "{API数据}";

        DialogResult = true;
        Close();
    }
}
