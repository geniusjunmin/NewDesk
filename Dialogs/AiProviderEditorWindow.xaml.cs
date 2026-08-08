using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using NewDesk.Models.Ai;
using NewDesk.Models.Security;
using NewDesk.Services.Ai;
using NewDesk.Services.Security;

namespace NewDesk.Dialogs;

public partial class AiProviderEditorWindow : Window
{
    private readonly AiProviderConfig _config;
    private bool _isInitializing = true;

    public AiProviderConfig Config => _config;

    public AiProviderEditorWindow(AiProviderConfig? config = null)
    {
        InitializeComponent();
        _config = config?.Clone() ?? new AiProviderConfig();

        Loaded += AiProviderEditorWindow_Loaded;
    }

    private void AiProviderEditorWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _isInitializing = true;
        try
        {
            KindComboBox.SelectedIndex = (int)_config.Kind;
            NameTextBox.Text = _config.Name;
            BaseUrlTextBox.Text = _config.BaseUrl;
            ModelComboBox.Text = _config.SelectedModel;
            StreamingCheckBox.IsChecked = _config.Streaming;
            DefaultCheckBox.IsChecked = _config.IsDefault;

            if (!string.IsNullOrEmpty(_config.SecretId))
            {
                string? secret = SecretStorageService.GetSecret(_config.SecretId);
                ApiKeyPasswordBox.Password = secret ?? "";
                ApiKeyTextBox.Text = secret ?? "";
            }
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private void KindComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;

        var kind = (AiProviderKind)KindComboBox.SelectedIndex;
        var preset = AiProviderTemplateRegistry.GetTemplates().Find(p => p.Kind == kind);
        if (preset != null)
        {
            NameTextBox.Text = preset.Name;
            BaseUrlTextBox.Text = preset.BaseUrl;
            ModelComboBox.Text = preset.SelectedModel;
        }
    }

    private void ShowKeyToggle_Click(object sender, RoutedEventArgs e)
    {
        if (ShowKeyToggle.IsChecked == true)
        {
            ApiKeyTextBox.Text = ApiKeyPasswordBox.Password;
            ApiKeyTextBox.Visibility = Visibility.Visible;
            ApiKeyPasswordBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            ApiKeyPasswordBox.Password = ApiKeyTextBox.Text;
            ApiKeyPasswordBox.Visibility = Visibility.Visible;
            ApiKeyTextBox.Visibility = Visibility.Collapsed;
        }
    }

    private void ValidateConfigBeforeAction(AiProviderConfig config, string apiKey, bool isSaving = false)
    {
        if (string.IsNullOrWhiteSpace(config.Name))
        {
            throw new InvalidOperationException("服务提供商名称不能为空。");
        }

        EndpointSecurityPolicy.ValidateEndpoint(config.BaseUrl, !string.IsNullOrEmpty(apiKey));

        if (isSaving && string.IsNullOrWhiteSpace(config.SelectedModel))
        {
            throw new InvalidOperationException("保存配置时模型 ID 不能为空，请选择或手动输入模型 ID。");
        }
    }

    private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        TestConnectionButton.IsEnabled = false;
        TestConnectionButton.Content = "⚡ 测试中...";
        TestResultBorder.Visibility = Visibility.Collapsed;

        SyncConfigFromUI();

        string key = ShowKeyToggle.IsChecked == true ? ApiKeyTextBox.Text : ApiKeyPasswordBox.Password;

        try
        {
            ValidateConfigBeforeAction(_config, key, isSaving: false);
        }
        catch (Exception ex)
        {
            TestResultBorder.Visibility = Visibility.Visible;
            TestResultTitle.Text = "❌ 配置验证失败";
            TestResultTitle.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
            TestResultDetail.Text = ex.Message;
            TestConnectionButton.IsEnabled = true;
            TestConnectionButton.Content = "⚡ 测试连接";
            return;
        }

        var testConfig = _config.Clone();
        var tempSecretId = "temp_test_" + Guid.NewGuid().ToString("N");

        if (!string.IsNullOrEmpty(key))
        {
            SecretStorageService.SaveSecret(tempSecretId, key);
            testConfig.SecretId = tempSecretId;
        }

        try
        {
            var provider = AiProviderFactory.CreateProvider(testConfig);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var res = await provider.TestConnectionAsync(cts.Token);

            TestResultBorder.Visibility = Visibility.Visible;
            if (res.IsSuccess)
            {
                TestResultTitle.Text = "✓ 连接成功";
                TestResultTitle.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");
                TestResultDetail.Text = $"响应延迟: {res.LatencyMs} ms | 可用模型: {res.ModelCount} 个";
            }
            else
            {
                TestResultTitle.Text = "❌ 连接失败";
                TestResultTitle.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
                TestResultDetail.Text = res.Message;
            }
        }
        catch (Exception ex)
        {
            TestResultBorder.Visibility = Visibility.Visible;
            TestResultTitle.Text = "❌ 连接异常";
            TestResultTitle.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
            TestResultDetail.Text = ex.Message;
        }
        finally
        {
            SecretStorageService.DeleteSecret(tempSecretId);
            TestConnectionButton.IsEnabled = true;
            TestConnectionButton.Content = "⚡ 测试连接";
        }
    }

    private async void FetchModelsButton_Click(object sender, RoutedEventArgs e)
    {
        FetchModelsButton.IsEnabled = false;
        FetchModelsButton.Content = "⟳ 获取中...";

        SyncConfigFromUI();

        string key = ShowKeyToggle.IsChecked == true ? ApiKeyTextBox.Text : ApiKeyPasswordBox.Password;

        try
        {
            ValidateConfigBeforeAction(_config, key, isSaving: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"配置验证失败: {ex.Message}", "获取模型失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            FetchModelsButton.IsEnabled = true;
            FetchModelsButton.Content = "⟳ 自动获取模型";
            return;
        }

        var testConfig = _config.Clone();
        var tempSecretId = "temp_fetch_" + Guid.NewGuid().ToString("N");

        if (!string.IsNullOrEmpty(key))
        {
            SecretStorageService.SaveSecret(tempSecretId, key);
            testConfig.SecretId = tempSecretId;
        }

        try
        {
            var provider = AiProviderFactory.CreateProvider(testConfig);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var models = await provider.GetModelsAsync(cts.Token);

            ModelComboBox.Items.Clear();
            foreach (var m in models)
            {
                ModelComboBox.Items.Add(m.Id);
            }
            if (ModelComboBox.Items.Count > 0)
            {
                ModelComboBox.SelectedIndex = 0;
            }
            else
            {
                MessageBox.Show("无法自动获取模型列表，请手动输入 Model ID。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法自动获取模型列表: {ex.Message}，请手动输入 Model ID。", "获取模型提示", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SecretStorageService.DeleteSecret(tempSecretId);
            FetchModelsButton.IsEnabled = true;
            FetchModelsButton.Content = "⟳ 自动获取模型";
        }
    }

    private void SyncConfigFromUI()
    {
        _config.Kind = (AiProviderKind)KindComboBox.SelectedIndex;
        _config.Name = NameTextBox.Text.Trim();
        _config.BaseUrl = BaseUrlTextBox.Text.Trim();
        _config.SelectedModel = ModelComboBox.Text.Trim();
        _config.Streaming = StreamingCheckBox.IsChecked == true;
        _config.IsDefault = DefaultCheckBox.IsChecked == true;

        if (_config.Kind == AiProviderKind.Claude) _config.Protocol = AiApiProtocol.AnthropicMessages;
        else if (_config.Kind == AiProviderKind.OpenAI || _config.Kind == AiProviderKind.XAI) _config.Protocol = AiApiProtocol.Responses;
        else _config.Protocol = AiApiProtocol.ChatCompletions;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SyncConfigFromUI();

        string key = ShowKeyToggle.IsChecked == true ? ApiKeyTextBox.Text : ApiKeyPasswordBox.Password;

        try
        {
            ValidateConfigBeforeAction(_config, key, isSaving: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrEmpty(_config.SecretId))
        {
            _config.SecretId = "secret_" + Guid.NewGuid().ToString("N");
        }

        if (!string.IsNullOrEmpty(key))
        {
            SecretStorageService.SaveSecret(_config.SecretId, key);
        }

        AiProviderRegistry.SaveProvider(_config);
        DialogResult = true;
        Close();
    }
}
