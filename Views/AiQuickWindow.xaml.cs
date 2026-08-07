using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using NewDesk.Models.Ai;
using NewDesk.Services;
using NewDesk.Services.Ai;

namespace NewDesk.Views;

public partial class AiQuickWindow : Window
{
    private CancellationTokenSource? _streamCts;
    private bool _isGenerating = false;

    public AiQuickWindow()
    {
        InitializeComponent();
        Loaded += AiQuickWindow_Loaded;
        Deactivated += AiQuickWindow_Deactivated;
    }

    private void AiQuickWindow_Loaded(object sender, RoutedEventArgs e)
    {
        PositionTopCenter();
        QuickInputTextBox.Focus();
    }

    public void PositionTopCenter()
    {
        double screenWidth = SystemParameters.PrimaryScreenWidth;
        double screenHeight = SystemParameters.PrimaryScreenHeight;

        Left = (screenWidth - Width) / 2;
        Top = screenHeight * 0.15;
    }

    private void AiQuickWindow_Deactivated(object? sender, EventArgs e)
    {
        if (PinToggle.IsChecked != true && !_isGenerating)
        {
            Hide();
        }
    }

    private async void QuickInputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await ExecuteQuickAiQueryAsync();
        }
    }

    private async void SendQuickButton_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteQuickAiQueryAsync();
    }

    private async Task ExecuteQuickAiQueryAsync()
    {
        if (_isGenerating) return;

        string prompt = QuickInputTextBox.Text.Trim();
        if (string.IsNullOrEmpty(prompt)) return;

        var defaultProvider = AiProviderRegistry.GetDefaultProvider();
        if (defaultProvider == null)
        {
            ResultTextBlock.Text = "❌ 未配置默认 AI 服务，请先进入设置菜单添加 AI 提供商。";
            return;
        }

        _isGenerating = true;
        SendQuickButton.IsEnabled = false;
        ResultTextBlock.Foreground = (Brush)FindResource("TextPrimaryBrush");
        ResultTextBlock.Text = "⟳ 正在生成回答...";
        _streamCts = new CancellationTokenSource();

        try
        {
            var provider = AiProviderFactory.CreateProvider(defaultProvider);
            var req = new AiRequest
            {
                Model = defaultProvider.SelectedModel,
                Messages = new List<AiMessage> { new AiMessage { Role = "user", Content = prompt } },
                SystemPrompt = "你是一个高效率的 Windows 桌面 AI 助手，请简明扼要地回答用户问题。",
                Stream = defaultProvider.Streaming
            };

            if (defaultProvider.Streaming)
            {
                ResultTextBlock.Text = "";
                await foreach (var chunk in provider.StreamAsync(req, _streamCts.Token))
                {
                    if (!string.IsNullOrEmpty(chunk.TextDelta))
                    {
                        ResultTextBlock.Text += chunk.TextDelta;
                        ResultScrollViewer.ScrollToEnd();
                    }
                }
            }
            else
            {
                var resp = await provider.CompleteAsync(req, _streamCts.Token);
                ResultTextBlock.Text = resp.Content;
            }
        }
        catch (OperationCanceledException)
        {
            ResultTextBlock.Text += " [已停止]";
        }
        catch (Exception ex)
        {
            ResultTextBlock.Text = $"❌ AI 响应异常: {ex.Message}";
        }
        finally
        {
            _isGenerating = false;
            SendQuickButton.IsEnabled = true;
            _streamCts?.Dispose();
            _streamCts = null;
        }
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(ResultTextBlock.Text))
        {
            Clipboard.SetText(ResultTextBlock.Text);
            ToastManager.Show("已复制", "回答文本已成功复制到剪贴板！", ToastType.Info);
        }
    }
}
