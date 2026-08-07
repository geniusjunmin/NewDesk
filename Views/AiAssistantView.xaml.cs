using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NewDesk.Models.Ai;
using NewDesk.Services;
using NewDesk.Services.Ai;

namespace NewDesk.Views;

public partial class AiAssistantView : UserControl
{
    private AiConversation? _currentConversation;
    private AiProviderConfig? _currentProviderConfig;
    private CancellationTokenSource? _streamCts;
    private bool _isGenerating = false;

    public AiAssistantView()
    {
        InitializeComponent();
        Loaded += AiAssistantView_Loaded;
    }

    private void AiAssistantView_Loaded(object sender, RoutedEventArgs e)
    {
        LoadProvidersCombo();
        LoadConversationsList();
    }

    private void LoadProvidersCombo()
    {
        ProviderComboBox.Items.Clear();
        var providers = AiProviderRegistry.GetAllProviders().Where(p => p.IsEnabled).ToList();
        foreach (var p in providers)
        {
            ProviderComboBox.Items.Add(p.Name);
        }

        var defaultProvider = AiProviderRegistry.GetDefaultProvider();
        if (defaultProvider != null)
        {
            int idx = providers.FindIndex(p => p.ProviderId == defaultProvider.ProviderId);
            if (idx >= 0) ProviderComboBox.SelectedIndex = idx;
        }
        else if (ProviderComboBox.Items.Count > 0)
        {
            ProviderComboBox.SelectedIndex = 0;
        }
    }

    private void ProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProviderComboBox.SelectedIndex < 0) return;

        var providers = AiProviderRegistry.GetAllProviders().Where(p => p.IsEnabled).ToList();
        if (ProviderComboBox.SelectedIndex < providers.Count)
        {
            _currentProviderConfig = providers[ProviderComboBox.SelectedIndex];
            UpdateBadge();
        }
    }

    private void UpdateBadge()
    {
        if (_currentProviderConfig == null) return;
        bool isLocal = _currentProviderConfig.Kind == AiProviderKind.Ollama || _currentProviderConfig.Kind == AiProviderKind.LMStudio || _currentProviderConfig.BaseUrl.Contains("localhost");
        if (isLocal)
        {
            LocalCloudBadgeText.Text = "🏠 本地 LLM 模型";
            LocalCloudBadgeText.Foreground = (Brush)FindResource("SuccessBrush");
        }
        else
        {
            LocalCloudBadgeText.Text = "☁️ 云端 API";
            LocalCloudBadgeText.Foreground = (Brush)FindResource("PrimaryBrush");
        }
    }

    private void LoadConversationsList()
    {
        var list = AiConversationService.GetConversations();
        ConversationsListBox.ItemsSource = list;
        if (list.Count > 0 && _currentConversation == null)
        {
            ConversationsListBox.SelectedIndex = 0;
        }
        else if (list.Count == 0)
        {
            NewChatButton_Click(this, new RoutedEventArgs());
        }
    }

    private void ConversationsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ConversationsListBox.SelectedItem is AiConversation conv)
        {
            _currentConversation = conv;
            RenderCurrentConversation();
        }
    }

    private void NewChatButton_Click(object sender, RoutedEventArgs e)
    {
        _currentConversation = AiConversationService.CreateNewConversation();
        LoadConversationsList();
        ConversationsListBox.SelectedItem = _currentConversation;
    }

    private void DeleteConversationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is AiConversation conv)
        {
            AiConversationService.DeleteConversation(conv.Id);
            _currentConversation = null;
            LoadConversationsList();
        }
    }

    private void ClearAllChatsButton_Click(object sender, RoutedEventArgs e)
    {
        AiConversationService.ClearAll();
        _currentConversation = null;
        LoadConversationsList();
    }

    private void RenderCurrentConversation()
    {
        MessagesStackPanel.Children.Clear();
        if (_currentConversation == null) return;

        foreach (var msg in _currentConversation.Messages)
        {
            AddMessageUI(msg);
        }
        MessagesScrollViewer.ScrollToEnd();
    }

    private void AddMessageUI(AiMessage msg)
    {
        bool isUser = msg.Role == "user";

        var border = new Border
        {
            Style = (Style)FindResource("CardStyle"),
            CornerRadius = new CornerRadius(12),
            Margin = isUser ? new Thickness(60, 6, 0, 6) : new Thickness(0, 6, 60, 6),
            Padding = new Thickness(14),
            HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Background = isUser ? (Brush)FindResource("PrimaryBrush") : (Brush)FindResource("SurfaceSecondaryBackground")
        };

        var panel = new StackPanel();

        // Role & Time
        var headerText = new TextBlock
        {
            Text = isUser ? "你" : "🤖 NewDesk AI",
            Style = (Style)FindResource("CaptionTextStyle"),
            Foreground = isUser ? new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)) : (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 4)
        };
        panel.Children.Add(headerText);

        // Reasoning Panel if available
        if (!string.IsNullOrEmpty(msg.ReasoningContent))
        {
            var exp = new Expander
            {
                Header = "🧠 思考过程 (Reasoning)",
                IsExpanded = false,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 0, 0, 6),
                Content = new TextBlock
                {
                    Text = msg.ReasoningContent,
                    Style = (Style)FindResource("CaptionTextStyle"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(4)
                }
            };
            panel.Children.Add(exp);
        }

        // Content
        var contentText = new TextBlock
        {
            Text = msg.Content,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Foreground = isUser ? Brushes.White : (Brush)FindResource("TextPrimaryBrush")
        };
        panel.Children.Add(contentText);

        border.Child = panel;
        MessagesStackPanel.Children.Add(border);
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        await SendMessageAsync();
    }

    private async void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
        {
            e.Handled = true;
            await SendMessageAsync();
        }
    }

    private async Task SendMessageAsync()
    {
        if (_isGenerating) return;

        string prompt = InputTextBox.Text.Trim();
        if (string.IsNullOrEmpty(prompt)) return;
        if (_currentProviderConfig == null)
        {
            ToastManager.Show("未配置 AI 服务", "请先在【设置 -> AI 服务配置】中添加至少一个可用的 AI 提供商。", ToastType.Warning);
            return;
        }

        if (_currentConversation == null)
        {
            _currentConversation = AiConversationService.CreateNewConversation();
        }

        InputTextBox.Text = string.Empty;

        // User Message
        var userMsg = new AiMessage { Role = "user", Content = prompt, Timestamp = DateTime.Now };
        _currentConversation.Messages.Add(userMsg);
        AddMessageUI(userMsg);
        MessagesScrollViewer.ScrollToEnd();

        // AI Assistant Message Draft
        var aiMsg = new AiMessage { Role = "assistant", Content = "", Timestamp = DateTime.Now };
        _currentConversation.Messages.Add(aiMsg);

        // Create UI Border for Streaming
        var aiBorder = new Border
        {
            Style = (Style)FindResource("CardStyle"),
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(0, 6, 60, 6),
            Padding = new Thickness(14),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = (Brush)FindResource("SurfaceSecondaryBackground")
        };
        var aiPanel = new StackPanel();
        aiPanel.Children.Add(new TextBlock
        {
            Text = "🤖 NewDesk AI",
            Style = (Style)FindResource("CaptionTextStyle"),
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 4)
        });
        var aiTextBlock = new TextBlock
        {
            Text = "⟳ 正在思考...",
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextPrimaryBrush")
        };
        aiPanel.Children.Add(aiTextBlock);
        aiBorder.Child = aiPanel;
        MessagesStackPanel.Children.Add(aiBorder);
        MessagesScrollViewer.ScrollToEnd();

        _isGenerating = true;
        SendButton.IsEnabled = false;
        StopButton.Visibility = Visibility.Visible;
        _streamCts = new CancellationTokenSource();

        try
        {
            var turnRequest = new AiTurnRequest
            {
                UserPrompt = prompt,
                ConversationHistory = AiConversationService.GetTruncatedContextMessages(_currentConversation.Messages),
                PreferredProvider = _currentProviderConfig,
                DataSensitivity = DataSensitivity.Personal,
                ConfirmationCallback = async pending =>
                {
                    var confirmDialog = new Dialogs.ConfirmDialog("AI 工具操作请求", pending.HumanReadablePreview)
                    {
                        Owner = Window.GetWindow(this)
                    };
                    return confirmDialog.ShowDialog() == true;
                }
            };

            var progress = new Progress<AiStreamChunk>(chunk =>
            {
                if (!string.IsNullOrEmpty(chunk.TextDelta))
                {
                    aiMsg.Content += chunk.TextDelta;
                    aiTextBlock.Text = aiMsg.Content;
                    MessagesScrollViewer.ScrollToEnd();
                }
            });

            aiTextBlock.Text = "";
            var finalResp = await AiOrchestrator.ExecuteTurnAsync(turnRequest, progress, _streamCts.Token);
            if (string.IsNullOrEmpty(aiMsg.Content) && !string.IsNullOrEmpty(finalResp.Content))
            {
                aiMsg.Content = finalResp.Content;
                aiTextBlock.Text = aiMsg.Content;
            }

            AiConversationService.SaveConversation(_currentConversation);
        }
        catch (OperationCanceledException)
        {
            aiMsg.Content += " [已停止生成]";
            aiTextBlock.Text = aiMsg.Content;
        }
        catch (Exception ex)
        {
            aiTextBlock.Text = $"❌ AI 生成异常: {ex.Message}";
            ToastManager.Show("AI 服务异常", ex.Message, ToastType.Error);
        }
        finally
        {
            _isGenerating = false;
            SendButton.IsEnabled = true;
            StopButton.Visibility = Visibility.Collapsed;
            _streamCts?.Dispose();
            _streamCts = null;
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _streamCts?.Cancel();
    }
}
