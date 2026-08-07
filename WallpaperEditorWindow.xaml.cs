using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ColorPickerWPF;
using Microsoft.Win32;
using NewDesk.Models;
using NewDesk.Services;

namespace NewDesk;

public partial class WallpaperEditorWindow : Window
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

    private const int SPI_SETDESKWALLPAPER = 20;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDWININICHANGE = 0x02;
    private static readonly string WallpapersPath = Path.Combine(AppContext.BaseDirectory, "wallpapers.json");
    private static readonly HttpClient HttpClient = new();
    
    private bool _isDragging;
    private Point _mouseOffset;
    private UIElement? _draggedElement;
    private TextBlock? _selectedText;
    private Adorner? _selectionAdorner;
    private string? _backgroundImagePath;
    private List<WallpaperState> _wallpaperStates = new();

    public WallpaperEditorWindow()
    {
        InitializeComponent();
        LoadSystemFonts();
        LoadWallpapers();
        LoadRotationSettings();
        
        MainCanvas.MouseLeftButtonDown += (s, e) =>
        {
            if (e.Source == MainCanvas) SelectText(null);
        };
    }

    private void LoadRotationSettings()
    {
        try
        {
            var settings = SettingsService.LoadSettings();
            if (EnableRotationCheckBox != null)
                EnableRotationCheckBox.IsChecked = settings.IsWallpaperRotationEnabled;
            
            // Set Interval
            if (RotationIntervalComboBox != null)
            {
                foreach (ComboBoxItem item in RotationIntervalComboBox.Items)
                {
                    if (item.Tag != null && int.TryParse(item.Tag.ToString(), out int val) && val == settings.WallpaperRotationIntervalMinutes)
                    {
                        RotationIntervalComboBox.SelectedItem = item;
                        break;
                    }
                }
                if (RotationIntervalComboBox.SelectedItem == null) RotationIntervalComboBox.SelectedIndex = 3; // Default 30m
            }

            // Set Mode
            if (RotationModeComboBox != null)
            {
                foreach (ComboBoxItem item in RotationModeComboBox.Items)
                {
                    if (item.Tag != null && item.Tag.ToString() == settings.WallpaperRotationMode.ToString())
                    {
                        RotationModeComboBox.SelectedItem = item;
                        break;
                    }
                }
                if (RotationModeComboBox.SelectedItem == null) RotationModeComboBox.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load rotation settings: {ex.Message}");
        }
    }

    private void RotationSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _wallpaperStates == null) return;

        try
        {
            var settings = SettingsService.LoadSettings();
            settings.IsWallpaperRotationEnabled = EnableRotationCheckBox.IsChecked ?? false;
            
            if (RotationIntervalComboBox.SelectedItem is ComboBoxItem intervalItem && intervalItem.Tag != null)
                settings.WallpaperRotationIntervalMinutes = int.Parse(intervalItem.Tag.ToString()!);
                
            if (RotationModeComboBox.SelectedItem is ComboBoxItem modeItem && modeItem.Tag != null)
                settings.WallpaperRotationMode = Enum.Parse<WallpaperRotationMode>(modeItem.Tag.ToString()!);

            SettingsService.SaveSettings(settings);

        // Update Service
        if (settings.IsWallpaperRotationEnabled)
        {
            if (_wallpaperStates == null || _wallpaperStates.Count == 0)
            {
                Services.ToastManager.Show("提示", "壁纸列表为空，请先保存至少一张壁纸以开启轮换。", Services.ToastType.Warning);
                EnableRotationCheckBox.IsChecked = false;
                settings.IsWallpaperRotationEnabled = false;
                SettingsService.SaveSettings(settings);
                return;
            }
            WallpaperService.StartRotation(_wallpaperStates, settings.WallpaperRotationIntervalMinutes, settings.WallpaperRotationMode);
        }
        else
        {
            WallpaperService.StopRotation();
        }
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"Error changing rotation settings: {ex.Message}");
    }
}

    #region Dynamic Text Helpers

    private string GetGregorianDateString() => DateTime.Now.ToString("yyyy年M月d日");

    private string GetLunarDateString()
    {
        try
        {
            var calendar = new ChineseLunisolarCalendar();
            var date = DateTime.Now;
            var year = calendar.GetYear(date);
            var month = calendar.GetMonth(date);
            var day = calendar.GetDayOfMonth(date);
            var leapMonth = calendar.GetLeapMonth(year);

            string[] lunarMonthNames = { "正月", "二月", "三月", "四月", "五月", "六月", "七月", "八月", "九月", "十月", "冬月", "腊月" };
            string[] lunarDayNames = { "初一", "初二", "初三", "初四", "初五", "初六", "初七", "初八", "初九", "初十", "十一", "十二", "十三", "十四", "十五", "十六", "十七", "十八", "十九", "二十", "廿一", "廿二", "廿三", "廿四", "廿五", "廿六", "廿七", "廿八", "廿九", "三十" };

            string monthString;
            if (leapMonth > 0 && month >= leapMonth)
            {
                if (month == leapMonth)
                {
                    // 闰月，显示前一个月的名字加上“闰”
                    int prevMonthIndex = month - 2;
                    if (prevMonthIndex >= 0 && prevMonthIndex < lunarMonthNames.Length)
                        monthString = "闰" + lunarMonthNames[prevMonthIndex];
                    else
                        monthString = "闰月";
                }
                else
                {
                    // 闰月之后的月份，索引需要减1（因为插入了一个闰月）
                    int realMonthIndex = month - 2;
                    if (realMonthIndex >= 0 && realMonthIndex < lunarMonthNames.Length)
                        monthString = lunarMonthNames[realMonthIndex];
                    else
                         monthString = "未知月";
                }
            }
            else
            {
                if (month - 1 >= 0 && month - 1 < lunarMonthNames.Length)
                    monthString = lunarMonthNames[month - 1];
                else
                    monthString = "未知月";
            }

            string dayString;
            if (day - 1 >= 0 && day - 1 < lunarDayNames.Length)
                dayString = lunarDayNames[day - 1];
            else
                dayString = "未知日";

            return $"农历 {monthString}{dayString}";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Lunar Date Error: {ex.Message}");
            return "农历日期获取失败";
        }
    }

    private string GetDayOfWeekString() => DateTime.Now.ToString("dddd", new CultureInfo("zh-CN"));

    private static string GetMB(long b) => (b / 1024.0 / 1024.0).ToString("F2") + " MB";
    private static string GetGB(long b) => (b / 1024.0 / 1024.0 / 1024.0).ToString("F2") + " GB";
    private static string GetSize(string bstr)
    {
        if (long.TryParse(bstr, out long b))
        {
            if (b >= 1024 * 1024 * 1024) return (b / 1024.0 / 1024.0 / 1024.0).ToString("F2") + " GB";
            return (b / 1024.0 / 1024.0).ToString("F2") + " MB";
        }
        return bstr;
    }

    private async Task<string> GetApiTextAsync(ApiConfig apiConfig)
    {
        try
        {
            string content = await HttpClient.GetStringAsync(apiConfig.Url);
            var match = Regex.Match(content, apiConfig.Regex);
            if (!match.Success) return "(Regex Fail)";

            string extractedValue = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;

            if (apiConfig.Formatting == "流量单位转换 (B -> MB/GB)")
            {
                extractedValue = GetSize(extractedValue);
            }
            return apiConfig.Prefix + extractedValue + apiConfig.Suffix;
        }
        catch (Exception)
        {
            return "(API Fail)";
        }
    }

    #endregion

    private void LoadSystemFonts()
    {
        var fonts = new[] { "宋体", "楷体", "微软雅黑", "Arial", "Times New Roman", "Verdana" };
        foreach (var font in fonts) FontFamilyComboBox.Items.Add(new FontFamily(font));
        FontFamilyComboBox.SelectedIndex = 0;
    }

    private void LoadWallpapers()
    {
        if (!File.Exists(WallpapersPath)) return;
        try
        {
            string json = File.ReadAllText(WallpapersPath);
            _wallpaperStates = JsonSerializer.Deserialize<List<WallpaperState>>(json) ?? new List<WallpaperState>();
            WallpaperListBox.ItemsSource = _wallpaperStates.Select(w => w.Name);
        }
        catch (Exception ex) { Services.ToastManager.Show("错误", $"加载壁纸列表时出错: {ex.Message}", Services.ToastType.Error); }
    }

    private void SaveWallpapers()
    {
        try
        {
            string json = JsonSerializer.Serialize(_wallpaperStates, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(WallpapersPath, json);
        }
        catch (Exception ex) { Services.ToastManager.Show("错误", $"保存壁纸列表时出错: {ex.Message}", Services.ToastType.Error); }
    }

    private void ImportImageButton_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new OpenFileDialog
        {
            Filter = "Image files (*.png;*.jpeg;*.jpg;*.bmp)|*.png;*.jpeg;*.jpg;*.bmp|All files (*.*)|*.*"
        };
        if (openFileDialog.ShowDialog() == true)
        {
            try
            {
                _backgroundImagePath = openFileDialog.FileName;
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(_backgroundImagePath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad; // Load fully so we can access width/height
                bitmap.EndInit();

                BackgroundImageBrush.ImageSource = bitmap;
                
                // Update Canvas size to match image dimensions for 1:1 editing
                MainCanvas.Width = bitmap.PixelWidth;
                MainCanvas.Height = bitmap.PixelHeight;
            }
            catch (Exception ex)
            {
                Services.ToastManager.Show("错误", $"无法加载图片: {ex.Message}", Services.ToastType.Error);
            }
        }
    }

    private void AddTextButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedTypeItem = TextTypeComboBox.SelectedItem as ComboBoxItem;
        if (selectedTypeItem == null) return;

        string selectedType = selectedTypeItem.Content.ToString() ?? string.Empty;
        string text = "";
        object? tag = null;

        switch (selectedType)
        {
            case "公历日期":
                tag = "GregorianDate";
                text = "{公历日期}";
                break;
            case "农历日期":
                tag = "LunarDate";
                text = "{农历日期}";
                break;
            case "星期":
                tag = "DayOfWeek";
                text = "{星期}";
                break;
            case "来自API":
                var apiDialog = new ApiTextConfigWindow();
                if (apiDialog.ShowDialog() == true && apiDialog.Config != null)
                {
                    text = "{API数据}";
                    tag = apiDialog.Config;
                }
                else return; // User cancelled
                break;
            default: // 静态文本
                text = "双击编辑";
                break;
        }

        var newText = new TextBlock
        {
            Text = text,
            Tag = tag,
            FontSize = FontSizeSlider.Value,
            FontFamily = (FontFamily)FontFamilyComboBox.SelectedItem,
            Foreground = FontColorBrush.Clone(),
            Cursor = Cursors.Hand
        };

        newText.MouseLeftButtonDown += TextBlock_MouseLeftButtonDown;
        newText.MouseMove += TextBlock_MouseMove;
        newText.MouseLeftButtonUp += TextBlock_MouseLeftButtonUp;

        MainCanvas.Children.Add(newText);
        Canvas.SetLeft(newText, 20);
        Canvas.SetTop(newText, 20);
        SelectText(newText);
    }

    private void TextBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBlock textBlock)
        {
            SelectText(textBlock);
            e.Handled = true;
            if (e.ClickCount == 2) CreateEditTextBox(textBlock);
            else
            {
                _draggedElement = textBlock;
                _isDragging = true;
                _mouseOffset = e.GetPosition(_draggedElement);
                _draggedElement.CaptureMouse();
            }
        }
    }

    private void TextBlock_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging && _draggedElement != null && _draggedElement is TextBlock draggedText)
        {
            Point currentPos = e.GetPosition(MainCanvas);
            double newLeft = currentPos.X - _mouseOffset.X;
            double newTop = currentPos.Y - _mouseOffset.Y;

            // Smart Snapping Logic
            if (SmartSnappingCheckBox.IsChecked == true)
            {
                const double SnapThreshold = 10.0;
                
                // Snap to Canvas Edges
                if (Math.Abs(newLeft) < SnapThreshold) newLeft = 0; // Left
                else if (Math.Abs(newLeft - (MainCanvas.Width - draggedText.ActualWidth)) < SnapThreshold) newLeft = MainCanvas.Width - draggedText.ActualWidth; // Right
                else if (Math.Abs(newLeft - (MainCanvas.Width - draggedText.ActualWidth) / 2) < SnapThreshold) newLeft = (MainCanvas.Width - draggedText.ActualWidth) / 2; // Center H

                if (Math.Abs(newTop) < SnapThreshold) newTop = 0; // Top
                else if (Math.Abs(newTop - (MainCanvas.Height - draggedText.ActualHeight)) < SnapThreshold) newTop = MainCanvas.Height - draggedText.ActualHeight; // Bottom
                else if (Math.Abs(newTop - (MainCanvas.Height - draggedText.ActualHeight) / 2) < SnapThreshold) newTop = (MainCanvas.Height - draggedText.ActualHeight) / 2; // Center V

                // Snap to other elements
                foreach (var child in MainCanvas.Children)
                {
                    if (child is TextBlock other && other != draggedText && other.Visibility == Visibility.Visible)
                    {
                        double otherLeft = Canvas.GetLeft(other);
                        double otherTop = Canvas.GetTop(other);
                        double otherRight = otherLeft + other.ActualWidth;
                        double otherBottom = otherTop + other.ActualHeight;
                        double otherCenterX = otherLeft + other.ActualWidth / 2;
                        double otherCenterY = otherTop + other.ActualHeight / 2;

                        double draggedRight = newLeft + draggedText.ActualWidth;
                        double draggedBottom = newTop + draggedText.ActualHeight;
                        double draggedCenterX = newLeft + draggedText.ActualWidth / 2;
                        double draggedCenterY = newTop + draggedText.ActualHeight / 2;

                        // Horizontal Snapping
                        if (Math.Abs(newLeft - otherLeft) < SnapThreshold) newLeft = otherLeft; // Left-Left
                        else if (Math.Abs(newLeft - otherRight) < SnapThreshold) newLeft = otherRight; // Left-Right
                        else if (Math.Abs(draggedRight - otherLeft) < SnapThreshold) newLeft = otherLeft - draggedText.ActualWidth; // Right-Left
                        else if (Math.Abs(draggedRight - otherRight) < SnapThreshold) newLeft = otherRight - draggedText.ActualWidth; // Right-Right
                        else if (Math.Abs(draggedCenterX - otherCenterX) < SnapThreshold) newLeft = otherCenterX - draggedText.ActualWidth / 2; // Center-Center

                        // Vertical Snapping
                        if (Math.Abs(newTop - otherTop) < SnapThreshold) newTop = otherTop; // Top-Top
                        else if (Math.Abs(newTop - otherBottom) < SnapThreshold) newTop = otherBottom; // Top-Bottom
                        else if (Math.Abs(draggedBottom - otherTop) < SnapThreshold) newTop = otherTop - draggedText.ActualHeight; // Bottom-Top
                        else if (Math.Abs(draggedBottom - otherBottom) < SnapThreshold) newTop = otherBottom - draggedText.ActualHeight; // Bottom-Bottom
                         else if (Math.Abs(draggedCenterY - otherCenterY) < SnapThreshold) newTop = otherCenterY - draggedText.ActualHeight / 2; // Center-Center
                    }
                }
            }

            Canvas.SetLeft(_draggedElement, newLeft);
            Canvas.SetTop(_draggedElement, newTop);
        }
    }

    private void TextBlock_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedElement != null)
        {
            _isDragging = false;
            _draggedElement.ReleaseMouseCapture();
            _draggedElement = null;
        }
    }

    private void CreateEditTextBox(TextBlock textBlock)
    {
        if (textBlock.Visibility == Visibility.Collapsed) return;

        if (textBlock.Tag is ApiConfig apiConfig)
        {
            var apiDialog = new ApiTextConfigWindow(apiConfig);
            if (apiDialog.ShowDialog() == true && apiDialog.Config != null)
            {
                textBlock.Tag = apiDialog.Config;
                // Optional: Update text immediately if we had a way to fetch it, 
                // but for now keeping it as is or resetting to placeholder is fine.
                // textBlock.Text = "{API数据}"; 
                Services.ToastManager.Show("提示", "API配置已更新，将在保存或刷新时生效。", Services.ToastType.Info);
            }
            return;
        }

        if (textBlock.Tag != null)
        {
             Services.ToastManager.Show("提示", "此动态文本不支持编辑内容。", Services.ToastType.Warning);
             return;
        }

        var editPanel = new StackPanel { Orientation = Orientation.Horizontal };
        var editTextBox = new TextBox { Text = textBlock.Text, FontSize = textBlock.FontSize, FontFamily = textBlock.FontFamily, Foreground = Brushes.Black, Background = Brushes.White, Width = Math.Max(textBlock.ActualWidth, 100) + 20, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        var saveButton = new Button { Content = "保存", Margin = new Thickness(5, 0, 0, 0) };
        editPanel.Children.Add(editTextBox);
        editPanel.Children.Add(saveButton);
        Canvas.SetLeft(editPanel, Canvas.GetLeft(textBlock));
        Canvas.SetTop(editPanel, Canvas.GetTop(textBlock));
        textBlock.Visibility = Visibility.Collapsed;
        MainCanvas.Children.Add(editPanel);
        saveButton.Click += (s, ev) => FinishEditing(editPanel, editTextBox, textBlock);
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() => { editTextBox.Focus(); editTextBox.SelectAll(); }));
    }

    private void FinishEditing(StackPanel editPanel, TextBox editTextBox, TextBlock textBlock)
    {
        if (!MainCanvas.Children.Contains(editPanel)) return;
        textBlock.Text = editTextBox.Text;
        textBlock.Visibility = Visibility.Visible;
        MainCanvas.Children.Remove(editPanel);
        SelectText(textBlock);
    }

    private void SelectText(TextBlock? textBlock)
    {
        if (_selectionAdorner != null) AdornerLayer.GetAdornerLayer(_selectionAdorner.AdornedElement)?.Remove(_selectionAdorner);
        _selectionAdorner = null;
        _selectedText = textBlock;
        if (_selectedText != null)
        {
            FontSizeSlider.Value = _selectedText.FontSize;
            FontFamilyComboBox.SelectedItem = _selectedText.FontFamily;
            FontColorBrush.Color = (_selectedText.Foreground as SolidColorBrush)?.Color ?? Colors.White;
            var adornerLayer = AdornerLayer.GetAdornerLayer(_selectedText);
            if (adornerLayer != null)
            {
                _selectionAdorner = new SelectionAdorner(_selectedText);
                adornerLayer.Add(_selectionAdorner);
            }
        }
    }

    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_selectedText != null) { _selectedText.FontSize = e.NewValue; if(FontSizeValueTextBlock != null) FontSizeValueTextBlock.Text = ((int)e.NewValue).ToString(); }
    }

    private void FontFamilyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selectedText != null && FontFamilyComboBox.SelectedItem is FontFamily fontFamily) _selectedText.FontFamily = fontFamily;
    }

    private void FontColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedText == null) return;
        if (ColorPickerWindow.ShowDialog(out Color newColor)) { var newBrush = new SolidColorBrush(newColor); _selectedText.Foreground = newBrush; FontColorBrush.Color = newColor; }
    }

    private async void SetWallpaperButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_backgroundImagePath) || !File.Exists(_backgroundImagePath))
        {
            Services.ToastManager.Show("提示", "请先导入一张图片。", Services.ToastType.Warning);
            return;
        }

        try
        {
            var state = CreateStateFromCanvas();
            
            // Save current wallpaper name to settings so we can auto-load on startup
            if (WallpaperListBox.SelectedItem is string name)
            {
                var settings = SettingsService.LoadSettings();
                settings.CurrentWallpaperName = name;
                SettingsService.SaveSettings(settings);
            }

            await WallpaperService.GenerateAndSetWallpaperAsync(state);
            WallpaperService.StartAutoRefresh(state);

            Services.ToastManager.Show("成功", "桌面背景已设置！", Services.ToastType.Success);
        }
        catch (Exception ex) { Services.ToastManager.Show("错误", $"设置桌面时发生错误: {ex.Message}", Services.ToastType.Error); }
    }

    private void WallpaperListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WallpaperListBox.SelectedItem is string name) { var state = _wallpaperStates.FirstOrDefault(w => w.Name == name); if (state != null) LoadStateOntoCanvas(state); }
    }

    private void SaveAsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new InputDialog("请输入壁纸名称:");
        if (dialog.ShowDialog() == true)
        {
            var newState = CreateStateFromCanvas();
            newState.Name = dialog.InputText;
            _wallpaperStates.Add(newState);
            SaveWallpapers();
            WallpaperListBox.ItemsSource = _wallpaperStates.Select(w => w.Name).ToList();
            WallpaperListBox.SelectedItem = newState.Name;
        }
    }

    private void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (WallpaperListBox.SelectedItem is string name)
        {
            var state = _wallpaperStates.FirstOrDefault(w => w.Name == name);
            if (state != null)
            {
                var updatedState = CreateStateFromCanvas();
                state.BackgroundImagePath = updatedState.BackgroundImagePath;
                state.TextElements = updatedState.TextElements;
                state.RefreshIntervalMinutes = updatedState.RefreshIntervalMinutes; // Ensure interval is updated
                SaveWallpapers();
                Services.ToastManager.Show("提示", "壁纸更新成功！", Services.ToastType.Success);
            }
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (WallpaperListBox.SelectedItem is string name)
        {
            var state = _wallpaperStates.FirstOrDefault(w => w.Name == name);
            if (state != null && MessageBox.Show($"确定要删除壁纸 '{name}' 吗？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                _wallpaperStates.Remove(state);
                SaveWallpapers();
                WallpaperListBox.ItemsSource = _wallpaperStates.Select(w => w.Name).ToList();
                ClearCanvas();
            }
        }
    }

    #region Alignment
    private void AlignLeftButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedText != null) Canvas.SetLeft(_selectedText, 10);
    }

    private void AlignRightButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedText != null) Canvas.SetLeft(_selectedText, MainCanvas.Width - _selectedText.ActualWidth - 10);
    }

    private void AlignTopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedText != null) Canvas.SetTop(_selectedText, 10);
    }

    private void AlignBottomButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedText != null) Canvas.SetTop(_selectedText, MainCanvas.Height - _selectedText.ActualHeight - 10);
    }

    private void AlignCenterHButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedText != null) Canvas.SetLeft(_selectedText, (MainCanvas.Width - _selectedText.ActualWidth) / 2);
    }

    private void AlignCenterVButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedText != null) Canvas.SetTop(_selectedText, (MainCanvas.Height - _selectedText.ActualHeight) / 2);
    }
    #endregion

    private WallpaperState CreateStateFromCanvas()
    {
        int interval = 0;
        if (RefreshIntervalComboBox.SelectedIndex > 0)
        {
             string content = (RefreshIntervalComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "";
             if (content.Contains("1 分钟")) interval = 1;
             else if (content.Contains("5 分钟")) interval = 5;
             else if (content.Contains("15 分钟")) interval = 15;
             else if (content.Contains("30 分钟")) interval = 30;
             else if (content.Contains("1 小时")) interval = 60;
        }

        return new WallpaperState
        {
            Name = (WallpaperListBox.SelectedItem as string) ?? "未命名",
            BackgroundImagePath = _backgroundImagePath,
            RefreshIntervalMinutes = interval,
            DesignWidth = MainCanvas.Width,
            DesignHeight = MainCanvas.Height,
            TextElements = MainCanvas.Children.OfType<TextBlock>().Select(tb =>
            {
                var textState = new TextElementState { Text = tb.Text, X = Canvas.GetLeft(tb), Y = Canvas.GetTop(tb), FontSize = tb.FontSize, FontFamily = tb.FontFamily.Source, Color = (tb.Foreground as SolidColorBrush)?.Color.ToString() ?? "#FFFFFFFF" };
                if (tb.Tag is string dynamicType) textState.DynamicType = dynamicType;
                else if (tb.Tag is ApiConfig apiConfig)
                {
                    textState.DynamicType = "Api";
                    textState.ApiUrl = apiConfig.Url;
                    textState.ApiRegex = apiConfig.Regex;
                    textState.ApiFormatting = apiConfig.Formatting;
                    textState.ApiPrefix = apiConfig.Prefix;
                    textState.ApiSuffix = apiConfig.Suffix;
                }
                return textState;
            }).ToList()
        };
    }

    private void LoadStateOntoCanvas(WallpaperState state)
    {
        ClearCanvas();
        if (!string.IsNullOrEmpty(state.BackgroundImagePath) && File.Exists(state.BackgroundImagePath))
        {
            _backgroundImagePath = state.BackgroundImagePath;
            BackgroundImageBrush.ImageSource = new BitmapImage(new Uri(state.BackgroundImagePath));
        }
        
        // Set Interval
        RefreshIntervalComboBox.SelectedIndex = 0;
        if (state.RefreshIntervalMinutes == 1) RefreshIntervalComboBox.SelectedIndex = 1;
        else if (state.RefreshIntervalMinutes == 5) RefreshIntervalComboBox.SelectedIndex = 2;
        else if (state.RefreshIntervalMinutes == 15) RefreshIntervalComboBox.SelectedIndex = 3;
        else if (state.RefreshIntervalMinutes == 30) RefreshIntervalComboBox.SelectedIndex = 4;
        else if (state.RefreshIntervalMinutes == 60) RefreshIntervalComboBox.SelectedIndex = 5;

        foreach (var textState in state.TextElements)
        {
            object? tag = null;
            if (textState.DynamicType == "Api") tag = new ApiConfig 
            { 
                Url = textState.ApiUrl ?? "", 
                Regex = textState.ApiRegex ?? "", 
                Formatting = textState.ApiFormatting ?? "",
                Prefix = textState.ApiPrefix ?? "",
                Suffix = textState.ApiSuffix ?? ""
            };
            else if (!string.IsNullOrEmpty(textState.DynamicType)) tag = textState.DynamicType;

            var textBlock = new TextBlock { Text = textState.Text, Tag = tag, FontSize = textState.FontSize, FontFamily = new FontFamily(textState.FontFamily), Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(textState.Color)), Cursor = Cursors.Hand };
            textBlock.MouseLeftButtonDown += TextBlock_MouseLeftButtonDown;
            textBlock.MouseMove += TextBlock_MouseMove;
            textBlock.MouseLeftButtonUp += TextBlock_MouseLeftButtonUp;
            MainCanvas.Children.Add(textBlock);
            Canvas.SetLeft(textBlock, textState.X);
            Canvas.SetTop(textBlock, textState.Y);
        }
    }

    private void ClearCanvas()
    {
        SelectText(null);
        _backgroundImagePath = null;
        BackgroundImageBrush.ImageSource = null;
        MainCanvas.Children.Clear();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && _selectedText != null)
        {
            if (_selectionAdorner != null) AdornerLayer.GetAdornerLayer(_selectionAdorner.AdornedElement)?.Remove(_selectionAdorner);
            MainCanvas.Children.Remove(_selectedText);
            _selectedText = null;
            _selectionAdorner = null;
        }
    }
    
    private class SelectionAdorner : Adorner
    {
        public SelectionAdorner(UIElement adornedElement) : base(adornedElement) { }
        protected override void OnRender(DrawingContext dc) => dc.DrawRectangle(null, new Pen(Brushes.DodgerBlue, 2), new Rect(AdornedElement.DesiredSize));
    }
}