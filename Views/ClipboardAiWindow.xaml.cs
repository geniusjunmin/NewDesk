using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NewDesk.Models.Ai;
using NewDesk.Services;
using NewDesk.Services.Ai;

namespace NewDesk.Views;

public partial class ClipboardAiWindow : Window
{
    private string _clipboardText = string.Empty;
    private CancellationTokenSource? _cts;

    public ClipboardAiWindow()
    {
        InitializeComponent();
        Loaded += ClipboardAiWindow_Loaded;
        PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
    }

    private void ClipboardAiWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                _clipboardText = Clipboard.GetText();
                CharCountText.Text = $" ({_clipboardText.Length} 字符)";
            }
            else
            {
                _clipboardText = string.Empty;
                CharCountText.Text = " (剪贴板为空)";
            }
        }
        catch
        {
            _clipboardText = string.Empty;
        }
    }

    private async void Action_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string actionTag)
        {
            if (string.IsNullOrEmpty(_clipboardText))
            {
                ToastManager.Show("剪贴板为空", "未能读取到有效文本。", ToastType.Warning);
                return;
            }

            string prompt = actionTag switch
            {
                "TranslateEn" => $"请将以下文本翻译为英文：\n{_clipboardText}",
                "TranslateZh" => $"请将以下文本翻译为中文：\n{_clipboardText}",
                "Polish" => $"请对以下文本进行润色与语法优化：\n{_clipboardText}",
                "Summarize" => $"请对以下文本提取核心要点总结：\n{_clipboardText}",
                "Reply" => $"请为以下消息草拟一份得体专业的回复：\n{_clipboardText}",
                _ => _clipboardText
            };

            ResultTextBox.Text = "⟳ 正在使用 AI 处理剪贴板内容...";
            _cts = new CancellationTokenSource();

            try
            {
                var turnRequest = new AiTurnRequest
                {
                    UserPrompt = prompt,
                    TaskProfile = AiTaskProfile.FastCommand,
                    DataSensitivity = DataSensitivity.Personal
                };

                var progress = new Progress<AiStreamChunk>(chunk =>
                {
                    if (!string.IsNullOrEmpty(chunk.TextDelta))
                    {
                        if (ResultTextBox.Text.StartsWith("⟳")) ResultTextBox.Text = "";
                        ResultTextBox.Text += chunk.TextDelta;
                    }
                });

                var resp = await AiOrchestrator.ExecuteTurnAsync(turnRequest, progress, _cts.Token);
                if (string.IsNullOrEmpty(ResultTextBox.Text) && !string.IsNullOrEmpty(resp.Content))
                {
                    ResultTextBox.Text = resp.Content;
                }
            }
            catch (Exception ex)
            {
                ResultTextBox.Text = $"❌ AI 处理异常: {ex.Message}";
            }
        }
    }

    private void CopyResultButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(ResultTextBox.Text) && !ResultTextBox.Text.StartsWith("⟳") && !ResultTextBox.Text.StartsWith("❌"))
        {
            Clipboard.SetText(ResultTextBox.Text);
            ToastManager.Show("成功", "AI 处理结果已复制至剪贴板！", ToastType.Success);
        }
    }
}
