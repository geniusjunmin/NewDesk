using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ColorPickerWPF;
using Microsoft.Win32;
using NewDesk.Dialogs;
using NewDesk.Models;
using NewDesk.Services;

namespace NewDesk;

public partial class WallpaperEditorWindow : Window
{
    private List<WallpaperState> _wallpapers = new();
    private WallpaperState? _currentWallpaper;
    private TextElementState? _selectedElement;

    private readonly WallpaperUndoManager _undoManager = new();
    private bool _isInitializing = true;
    private bool _isDirty = false;
    private bool _isPreviewMode = false;

    private double _zoomFactor = 1.0;
    private bool _isDraggingElement = false;
    private Point _dragStartPoint;
    private double _dragStartElementX;
    private double _dragStartElementY;
    private UIElement? _draggedVisualElement;

    public WallpaperEditorWindow() : this(null)
    {
    }

    public WallpaperEditorWindow(WallpaperState? wallpaperState = null)
    {
        InitializeComponent();

        _undoManager.StateChanged += (s, e) => UpdateUndoRedoButtons();

        Loaded += WallpaperEditorWindow_Loaded;
    }

    private void WallpaperEditorWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _isInitializing = true;
        try
        {
            LoadSystemFonts();
            LoadWallpapersList();
            LoadRotationSettings();

            if (_currentWallpaper == null && _wallpapers.Count > 0)
            {
                SelectWallpaper(_wallpapers[0]);
            }
            else if (_currentWallpaper != null)
            {
                SelectWallpaper(_currentWallpaper);
            }
            else
            {
                UpdateEmptyState();
            }
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("WallpaperEditorWindow_Loaded", ex);
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private void LoadSystemFonts()
    {
        FontFamilyComboBox.Items.Clear();
        foreach (var font in Fonts.SystemFontFamilies.OrderBy(f => f.Source))
        {
            FontFamilyComboBox.Items.Add(font.Source);
        }
        FontFamilyComboBox.SelectedItem = "Microsoft YaHei";
    }

    private void LoadWallpapersList()
    {
        _wallpapers = WallpaperService.LoadWallpapers();
        FilterWallpaperList();
    }

    private void FilterWallpaperList()
    {
        string search = WallpaperSearchTextBox.Text.Trim().ToLower();
        var filtered = string.IsNullOrEmpty(search)
            ? _wallpapers
            : _wallpapers.Where(w => w.Name.ToLower().Contains(search)).ToList();

        WallpaperListBox.ItemsSource = null;
        WallpaperListBox.ItemsSource = filtered;
        WallpaperCountText.Text = $"共 {_wallpapers.Count} 张壁纸";

        UpdateEmptyState();
    }

    private void LoadRotationSettings()
    {
        var settings = SettingsService.LoadSettings();
        EnableRotationCheckBox.IsChecked = settings.IsWallpaperRotationEnabled;
        RotationModeComboBox.SelectedIndex = settings.WallpaperRotationMode == WallpaperRotationMode.Sequential ? 0 : 1;

        RotationIntervalComboBox.SelectedIndex = settings.WallpaperRotationIntervalMinutes switch
        {
            5 => 0,
            10 => 1,
            15 => 2,
            30 => 3,
            60 => 4,
            120 => 5,
            _ => 3
        };
    }

    private void SelectWallpaper(WallpaperState state)
    {
        if (_isDirty)
        {
            PromptSaveUnsavedChanges();
        }

        _currentWallpaper = state;
        WallpaperListBox.SelectedItem = state;
        _undoManager.Clear();

        // Phase 21: Resolve asset path with fallback
        string resolvedPath = WallpaperService.ResolveAssetPath(state.BackgroundImagePath, state.BackgroundAssetId);

        if (!string.IsNullOrEmpty(resolvedPath) && File.Exists(resolvedPath))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(resolvedPath);
                bmp.EndInit();
                BackgroundImage.Source = bmp;

                if (state.DesignWidth <= 0 || state.DesignHeight <= 0)
                {
                    state.DesignWidth = bmp.PixelWidth > 0 ? bmp.PixelWidth : 1920;
                    state.DesignHeight = bmp.PixelHeight > 0 ? bmp.PixelHeight : 1080;
                }
            }
            catch
            {
                BackgroundImage.Source = null;
            }
        }
        else
        {
            BackgroundImage.Source = null;
        }

        CanvasBorder.Width = _currentWallpaper.DesignWidth;
        CanvasBorder.Height = _currentWallpaper.DesignHeight;
        MainCanvas.Width = _currentWallpaper.DesignWidth;
        MainCanvas.Height = _currentWallpaper.DesignHeight;

        _selectedElement = null;
        _isDirty = false;
        UpdateWindowTitle();
        FitToScreen();
        RenderCanvasElements();
        UpdatePropertiesPanel();
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        EmptyCanvasState.Visibility = _wallpapers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CanvasBorder.Visibility = _wallpapers.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RenderCanvasElements()
    {
        if (_currentWallpaper == null) return;

        MainCanvas.Children.Clear();
        MainCanvas.Children.Add(SelectionBorder);

        // Phase 23: Omit invisible elements
        foreach (var element in _currentWallpaper.TextElements.Where(e => e.IsVisible).OrderBy(e => e.ZIndex))
        {
            var textBlock = CreateTextBlockForElement(element);
            Canvas.SetLeft(textBlock, element.X);
            Canvas.SetTop(textBlock, element.Y);
            Canvas.SetZIndex(textBlock, element.ZIndex);

            textBlock.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                SelectElement(element);

                // Phase 23: Reject drag if element is locked
                if (!element.IsLocked)
                {
                    StartElementDrag(e, textBlock);
                }
            };

            MainCanvas.Children.Add(textBlock);
        }

        UpdateSelectionBorder();
        UpdateLayersList();
    }

    private TextBlock CreateTextBlockForElement(TextElementState element)
    {
        string text = WallpaperTextRenderer.GetRenderText(element, DynamicDataService.LoadSources());

        Color color;
        try { color = (Color)ColorConverter.ConvertFromString(element.Color); }
        catch { color = Colors.White; }

        var tb = new TextBlock
        {
            Text = text,
            FontSize = element.FontSize,
            FontFamily = new FontFamily(element.FontFamily),
            Foreground = new SolidColorBrush(color),
            FontWeight = element.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = element.Italic ? FontStyles.Italic : FontStyles.Normal,
            Cursor = element.IsLocked ? Cursors.Arrow : Cursors.SizeAll,
            Tag = element
        };

        if (element.Underline)
        {
            tb.TextDecorations = TextDecorations.Underline;
        }

        tb.TextAlignment = element.Alignment switch
        {
            "Center" => TextAlignment.Center,
            "Right" => TextAlignment.Right,
            _ => TextAlignment.Left
        };

        return tb;
    }

    private void SelectElement(TextElementState? element)
    {
        _selectedElement = element;
        UpdateSelectionBorder();
        UpdatePropertiesPanel();
    }

    private void UpdateSelectionBorder()
    {
        if (_selectedElement == null || !_selectedElement.IsVisible || _isPreviewMode)
        {
            SelectionBorder.Visibility = Visibility.Collapsed;
            return;
        }

        TextBlock? target = null;
        foreach (UIElement child in MainCanvas.Children)
        {
            if (child is TextBlock tb && tb.Tag == _selectedElement)
            {
                target = tb;
                break;
            }
        }

        if (target != null)
        {
            SelectionBorder.Visibility = Visibility.Visible;
            Canvas.SetLeft(SelectionBorder, _selectedElement.X - 4);
            Canvas.SetTop(SelectionBorder, _selectedElement.Y - 4);
            Canvas.SetZIndex(SelectionBorder, 9999);

            target.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            SelectionBorder.Width = Math.Max(20, target.ActualWidth + 8);
            SelectionBorder.Height = Math.Max(20, target.ActualHeight + 8);
        }
        else
        {
            SelectionBorder.Visibility = Visibility.Collapsed;
        }
    }

    private void StartElementDrag(MouseButtonEventArgs e, UIElement visualElement)
    {
        if (_selectedElement == null || _selectedElement.IsLocked || _isPreviewMode) return;

        _isDraggingElement = true;
        _draggedVisualElement = visualElement;
        _dragStartPoint = e.GetPosition(MainCanvas);
        _dragStartElementX = _selectedElement.X;
        _dragStartElementY = _selectedElement.Y;

        PushUndoSnapshot();
        Mouse.Capture(MainCanvas);
        MainCanvas.MouseMove += MainCanvas_MouseMove;
        MainCanvas.MouseLeftButtonUp += MainCanvas_MouseLeftButtonUp;
    }

    // Phase 24: Direct visual positioning on MouseMove without full canvas re-renders!
    private void MainCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingElement && _selectedElement != null && _draggedVisualElement != null)
        {
            Point current = e.GetPosition(MainCanvas);
            double deltaX = current.X - _dragStartPoint.X;
            double deltaY = current.Y - _dragStartPoint.Y;

            double newX = Math.Max(0, _dragStartElementX + deltaX);
            double newY = Math.Max(0, _dragStartElementY + deltaY);

            _selectedElement.X = newX;
            _selectedElement.Y = newY;

            Canvas.SetLeft(_draggedVisualElement, newX);
            Canvas.SetTop(_draggedVisualElement, newY);
            Canvas.SetLeft(SelectionBorder, newX - 4);
            Canvas.SetTop(SelectionBorder, newY - 4);

            PositionXTextBox.Text = Math.Round(newX).ToString();
            PositionYTextBox.Text = Math.Round(newY).ToString();
        }
    }

    private void MainCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingElement)
        {
            _isDraggingElement = false;
            _draggedVisualElement = null;
            Mouse.Capture(null);
            MainCanvas.MouseMove -= MainCanvas_MouseMove;
            MainCanvas.MouseLeftButtonUp -= MainCanvas_MouseLeftButtonUp;
            MarkDirty();
        }
    }

    private void UpdatePropertiesPanel()
    {
        _isInitializing = true;
        try
        {
            if (_selectedElement == null)
            {
                SelectedElementTag.Text = "● 未选中任何元素";
                ContentTextBox.Text = "";
                PositionXTextBox.Text = "0";
                PositionYTextBox.Text = "0";
                return;
            }

            SelectedElementTag.Text = $"● {_selectedElement.Text}";

            FontFamilyComboBox.SelectedItem = _selectedElement.FontFamily;
            FontSizeSlider.Value = _selectedElement.FontSize;
            FontSizeTextBox.Text = _selectedElement.FontSize.ToString();

            BoldToggle.IsChecked = _selectedElement.Bold;
            ItalicToggle.IsChecked = _selectedElement.Italic;
            UnderlineToggle.IsChecked = _selectedElement.Underline;

            AlignLeftRadio.IsChecked = _selectedElement.Alignment == "Left";
            AlignCenterRadio.IsChecked = _selectedElement.Alignment == "Center";
            AlignRightRadio.IsChecked = _selectedElement.Alignment == "Right";

            PositionXTextBox.Text = Math.Round(_selectedElement.X).ToString();
            PositionYTextBox.Text = Math.Round(_selectedElement.Y).ToString();

            ShadowCheckBox.IsChecked = _selectedElement.ShadowEnabled;
            StrokeCheckBox.IsChecked = _selectedElement.StrokeEnabled;
            BackgroundCheckBox.IsChecked = _selectedElement.BackgroundEnabled;

            ContentTextBox.Text = _selectedElement.Text;
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private void AddElement(TextElementState element)
    {
        if (_currentWallpaper == null) return;

        PushUndoSnapshot();
        _currentWallpaper.TextElements.Add(element);
        SelectElement(element);
        RenderCanvasElements();
        MarkDirty();
    }

    private void AddGregorianDateButton_Click(object sender, RoutedEventArgs e)
    {
        AddElement(new TextElementState
        {
            Text = "{公历日期}",
            DynamicType = "GregorianDate",
            X = 100,
            Y = 100,
            FontSize = 48,
            Color = "#FFFFFF"
        });
    }

    private void AddLunarDateButton_Click(object sender, RoutedEventArgs e)
    {
        AddElement(new TextElementState
        {
            Text = "{农历日期}",
            DynamicType = "LunarDate",
            X = 100,
            Y = 160,
            FontSize = 36,
            Color = "#2563EB"
        });
    }

    private void AddDayOfWeekButton_Click(object sender, RoutedEventArgs e)
    {
        AddElement(new TextElementState
        {
            Text = "{星期}",
            DynamicType = "DayOfWeek",
            X = 100,
            Y = 220,
            FontSize = 36,
            Color = "#EF4444"
        });
    }

    // Phase 22: Dynamic Data Source Picker
    private void AddApiDataButton_Click(object sender, RoutedEventArgs e)
    {
        var sources = DynamicDataService.LoadSources();
        if (sources.Count == 0)
        {
            ToastManager.Show("无可用的数据源", "请先在【API 动态信息】模块中添加至少一个数据源。", ToastType.Info);
            return;
        }

        var source = sources[0];
        AddElement(new TextElementState
        {
            Text = $"{{{source.Name}}}",
            DynamicType = "DataSource",
            DataSourceId = source.Id,
            X = 100,
            Y = 280,
            FontSize = 48,
            Color = "#10B981"
        });
    }

    private void AddCustomTextButton_Click(object sender, RoutedEventArgs e)
    {
        AddElement(new TextElementState
        {
            Text = "新自定义文本",
            DynamicType = null,
            X = 100,
            Y = 340,
            FontSize = 36,
            Color = "#FFFFFF"
        });
    }

    private void PushUndoSnapshot()
    {
        if (_currentWallpaper != null)
        {
            _undoManager.PushSnapshot(_currentWallpaper.TextElements);
        }
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentWallpaper == null) return;
        var prev = _undoManager.Undo(_currentWallpaper.TextElements);
        if (prev != null)
        {
            _currentWallpaper.TextElements = prev;
            RenderCanvasElements();
            MarkDirty();
        }
    }

    private void RedoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentWallpaper == null) return;
        var next = _undoManager.Redo(_currentWallpaper.TextElements);
        if (next != null)
        {
            _currentWallpaper.TextElements = next;
            RenderCanvasElements();
            MarkDirty();
        }
    }

    private void UpdateUndoRedoButtons()
    {
        UndoButton.IsEnabled = _undoManager.CanUndo;
        RedoButton.IsEnabled = _undoManager.CanRedo;
    }

    private void MarkDirty()
    {
        _isDirty = true;
        UpdateWindowTitle();
    }

    private void UpdateWindowTitle()
    {
        string name = _currentWallpaper?.Name ?? "新建壁纸";
        Title = _isDirty ? $"NewDesk 桌面编辑器 - {name} *" : $"NewDesk 桌面编辑器 - {name}";
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentWallpaper();
    }

    // Phase 27: Save wallpapers via WallpaperService
    private void SaveCurrentWallpaper()
    {
        try
        {
            var res = WallpaperService.SaveWallpapers(_wallpapers);
            if (res.IsSuccess)
            {
                _isDirty = false;
                UpdateWindowTitle();
                ToastManager.Show("保存成功", "壁纸配置已保存！", ToastType.Success);
            }
            else
            {
                ToastManager.Show("保存错误", res.Message, ToastType.Error);
            }
        }
        catch (Exception ex)
        {
            ToastManager.Show("保存错误", ex.Message, ToastType.Error);
        }
    }

    private async void ApplyToDesktopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentWallpaper == null) return;

        SaveCurrentWallpaper();
        try
        {
            var settings = SettingsService.LoadSettings();
            settings.CurrentWallpaperName = _currentWallpaper.Name;
            SettingsService.SaveSettings(settings);

            await WallpaperService.GenerateAndSetWallpaperAsync(_currentWallpaper);
            ToastManager.Show("应用成功", $"“{_currentWallpaper.Name}”已成功设为当前 Windows 桌面壁纸。", ToastType.Success);
        }
        catch (Exception ex)
        {
            ToastManager.Show("应用失败", ex.Message, ToastType.Error);
        }
    }

    // Phase 20 & 21: Import background image with WallpaperService.SaveWallpaperAsset
    private void ImportImageButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Filter = "图片文件 (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp"
            };

            if (dialog.ShowDialog() == true)
            {
                string assetId = WallpaperService.SaveWallpaperAsset(dialog.FileName);
                string assetPath = WallpaperService.GetAssetPath(assetId);

                double width = 1920;
                double height = 1080;
                try
                {
                    var bmp = BitmapFrame.Create(new Uri(assetPath), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    if (bmp.PixelWidth > 0 && bmp.PixelHeight > 0)
                    {
                        width = bmp.PixelWidth;
                        height = bmp.PixelHeight;
                    }
                }
                catch { }

                string name = Path.GetFileNameWithoutExtension(dialog.FileName);
                var newState = new WallpaperState
                {
                    Name = name,
                    BackgroundImagePath = assetPath,
                    BackgroundAssetId = assetId,
                    DesignWidth = width,
                    DesignHeight = height
                };

                _wallpapers.Add(newState);
                SelectWallpaper(newState);
                SaveCurrentWallpaper();
                FilterWallpaperList();
            }
        }
        catch (Exception ex)
        {
            ToastManager.Show("导入错误", ex.Message, ToastType.Error);
        }
    }

    private void DeleteElementButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentWallpaper != null && _selectedElement != null)
        {
            PushUndoSnapshot();
            _currentWallpaper.TextElements.Remove(_selectedElement);
            SelectElement(null);
            RenderCanvasElements();
            MarkDirty();
        }
    }

    private void FontProperty_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || _selectedElement == null) return;
        PushUndoSnapshot();
        _selectedElement.FontFamily = FontFamilyComboBox.SelectedItem?.ToString() ?? "Microsoft YaHei";
        RenderCanvasElements();
        MarkDirty();
    }

    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing || _selectedElement == null) return;
        _selectedElement.FontSize = Math.Round(FontSizeSlider.Value);
        FontSizeTextBox.Text = _selectedElement.FontSize.ToString();
        RenderCanvasElements();
        MarkDirty();
    }

    private void FontSizeTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _selectedElement == null) return;
        if (double.TryParse(FontSizeTextBox.Text, out double size))
        {
            PushUndoSnapshot();
            _selectedElement.FontSize = Math.Clamp(size, 8, 300);
            RenderCanvasElements();
            MarkDirty();
        }
    }

    private void TextFormat_Click(object sender, RoutedEventArgs e)
    {
        if (_isInitializing || _selectedElement == null) return;
        PushUndoSnapshot();
        _selectedElement.Bold = BoldToggle.IsChecked == true;
        _selectedElement.Italic = ItalicToggle.IsChecked == true;
        _selectedElement.Underline = UnderlineToggle.IsChecked == true;
        RenderCanvasElements();
        MarkDirty();
    }

    private void ColorPickerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedElement == null) return;
        if (ColorPickerWindow.ShowDialog(out Color newColor))
        {
            PushUndoSnapshot();
            _selectedElement.Color = $"#{newColor.A:X2}{newColor.R:X2}{newColor.G:X2}{newColor.B:X2}";
            RenderCanvasElements();
            MarkDirty();
        }
    }

    private void Align_Checked(object sender, RoutedEventArgs e)
    {
        if (_isInitializing || _selectedElement == null) return;
        PushUndoSnapshot();
        if (AlignLeftRadio.IsChecked == true) _selectedElement.Alignment = "Left";
        else if (AlignCenterRadio.IsChecked == true) _selectedElement.Alignment = "Center";
        else if (AlignRightRadio.IsChecked == true) _selectedElement.Alignment = "Right";
        RenderCanvasElements();
        MarkDirty();
    }

    private void Position_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _selectedElement == null) return;
        if (double.TryParse(PositionXTextBox.Text, out double x) && double.TryParse(PositionYTextBox.Text, out double y))
        {
            _selectedElement.X = Math.Max(0, x);
            _selectedElement.Y = Math.Max(0, y);
            RenderCanvasElements();
            MarkDirty();
        }
    }

    // Phase 25: Exact alignment math using element size measurement
    private (double Width, double Height) GetElementSize(TextElementState elem)
    {
        var tb = CreateTextBlockForElement(elem);
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return (tb.DesiredSize.Width, tb.DesiredSize.Height);
    }

    private void QuickAlignLeft_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedElement == null) return;
        PushUndoSnapshot();
        _selectedElement.X = 0;
        RenderCanvasElements();
        UpdatePropertiesPanel();
        MarkDirty();
    }

    private void QuickAlignCenterH_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedElement == null || _currentWallpaper == null) return;
        PushUndoSnapshot();
        var (w, _) = GetElementSize(_selectedElement);
        _selectedElement.X = Math.Max(0, (_currentWallpaper.DesignWidth - w) / 2);
        RenderCanvasElements();
        UpdatePropertiesPanel();
        MarkDirty();
    }

    private void QuickAlignRight_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedElement == null || _currentWallpaper == null) return;
        PushUndoSnapshot();
        var (w, _) = GetElementSize(_selectedElement);
        _selectedElement.X = Math.Max(0, _currentWallpaper.DesignWidth - w);
        RenderCanvasElements();
        UpdatePropertiesPanel();
        MarkDirty();
    }

    private void QuickAlignTop_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedElement == null) return;
        PushUndoSnapshot();
        _selectedElement.Y = 0;
        RenderCanvasElements();
        UpdatePropertiesPanel();
        MarkDirty();
    }

    private void QuickAlignCenterV_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedElement == null || _currentWallpaper == null) return;
        PushUndoSnapshot();
        var (_, h) = GetElementSize(_selectedElement);
        _selectedElement.Y = Math.Max(0, (_currentWallpaper.DesignHeight - h) / 2);
        RenderCanvasElements();
        UpdatePropertiesPanel();
        MarkDirty();
    }

    private void QuickAlignBottom_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedElement == null || _currentWallpaper == null) return;
        PushUndoSnapshot();
        var (_, h) = GetElementSize(_selectedElement);
        _selectedElement.Y = Math.Max(0, _currentWallpaper.DesignHeight - h);
        RenderCanvasElements();
        UpdatePropertiesPanel();
        MarkDirty();
    }

    private void EffectCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_isInitializing || _selectedElement == null) return;
        PushUndoSnapshot();
        _selectedElement.ShadowEnabled = ShadowCheckBox.IsChecked == true;
        _selectedElement.StrokeEnabled = StrokeCheckBox.IsChecked == true;
        _selectedElement.BackgroundEnabled = BackgroundCheckBox.IsChecked == true;
        RenderCanvasElements();
        MarkDirty();
    }

    private void ContentTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _selectedElement == null) return;
        _selectedElement.Text = ContentTextBox.Text;
        RenderCanvasElements();
        MarkDirty();
    }

    private void TabRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (PropertiesScrollViewer == null || LayersPanelContainer == null) return;
        if (EditTabRadio.IsChecked == true)
        {
            PropertiesScrollViewer.Visibility = Visibility.Visible;
            LayersPanelContainer.Visibility = Visibility.Collapsed;
        }
        else
        {
            PropertiesScrollViewer.Visibility = Visibility.Collapsed;
            LayersPanelContainer.Visibility = Visibility.Visible;
            UpdateLayersList();
        }
    }

    private void UpdateLayersList()
    {
        if (_currentWallpaper == null) return;
        LayersListBox.ItemsSource = null;
        LayersListBox.ItemsSource = _currentWallpaper.TextElements.OrderByDescending(e => e.ZIndex).ToList();
    }

    private void LayersListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LayersListBox.SelectedItem is TextElementState elem)
        {
            SelectElement(elem);
        }
    }

    private void MoveLayerUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TextElementState elem)
        {
            PushUndoSnapshot();
            elem.ZIndex++;
            RenderCanvasElements();
            MarkDirty();
        }
    }

    private void MoveLayerDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TextElementState elem)
        {
            PushUndoSnapshot();
            elem.ZIndex = Math.Max(0, elem.ZIndex - 1);
            RenderCanvasElements();
            MarkDirty();
        }
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ZoomText == null || CanvasBorder == null) return;
        _zoomFactor = ZoomSlider.Value / 100.0;
        ZoomText.Text = $"{Math.Round(ZoomSlider.Value)}%";

        var transform = new ScaleTransform(_zoomFactor, _zoomFactor);
        CanvasBorder.LayoutTransform = transform;
    }

    private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
    {
        ZoomSlider.Value = Math.Max(25, ZoomSlider.Value - 15);
    }

    private void ZoomInButton_Click(object sender, RoutedEventArgs e)
    {
        ZoomSlider.Value = Math.Min(300, ZoomSlider.Value + 15);
    }

    private void ZoomFitButton_Click(object sender, RoutedEventArgs e)
    {
        FitToScreen();
    }

    private void FitToScreen()
    {
        if (_currentWallpaper == null || CanvasContainerGrid == null) return;
        double availableW = CanvasContainerGrid.ActualWidth > 0 ? CanvasContainerGrid.ActualWidth - 40 : 800;
        double availableH = CanvasContainerGrid.ActualHeight > 0 ? CanvasContainerGrid.ActualHeight - 40 : 500;

        double fitScale = Math.Min(availableW / _currentWallpaper.DesignWidth, availableH / _currentWallpaper.DesignHeight);
        ZoomSlider.Value = Math.Clamp(fitScale * 100.0, 25, 300);
    }

    private void AlignLeftCanvas_Click(object sender, RoutedEventArgs e) => QuickAlignLeft_Click(sender, e);
    private void AlignCenterCanvas_Click(object sender, RoutedEventArgs e) => QuickAlignCenterH_Click(sender, e);
    private void AlignRightCanvas_Click(object sender, RoutedEventArgs e) => QuickAlignRight_Click(sender, e);

    private void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        _isPreviewMode = PreviewButton.IsChecked == true;
        UpdateSelectionBorder();
    }

    private void CanvasContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Source == CanvasContainerGrid || e.Source == MainCanvas || e.Source == BackgroundImage)
        {
            SelectElement(null);
        }
    }

    private void WallpaperListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (WallpaperListBox.SelectedItem is WallpaperState selected)
        {
            SelectWallpaper(selected);
        }
    }

    private void WallpaperSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterWallpaperList();
    }

    private void RotationSettings_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        var settings = SettingsService.LoadSettings();
        settings.IsWallpaperRotationEnabled = EnableRotationCheckBox.IsChecked == true;
        settings.WallpaperRotationMode = RotationModeComboBox.SelectedIndex == 0 ? WallpaperRotationMode.Sequential : WallpaperRotationMode.Random;

        settings.WallpaperRotationIntervalMinutes = RotationIntervalComboBox.SelectedIndex switch
        {
            0 => 5,
            1 => 10,
            2 => 15,
            3 => 30,
            4 => 60,
            5 => 120,
            _ => 15
        };

        SettingsService.SaveSettings(settings);
    }

    private void WallpaperMoreMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is WallpaperState targetState)
        {
            var cm = new ContextMenu();

            var renameItem = new MenuItem { Header = "✏ 重命名" };
            renameItem.Click += (s, ev) =>
            {
                var dialog = new InputDialog("重命名壁纸", "请输入新的壁纸名称：", targetState.Name) { Owner = this };
                if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.InputText))
                {
                    targetState.Name = dialog.InputText;
                    SaveCurrentWallpaper();
                    FilterWallpaperList();
                }
            };

            var duplicateItem = new MenuItem { Header = "📋 复制壁纸" };
            duplicateItem.Click += (s, ev) =>
            {
                var copyState = new WallpaperState
                {
                    Name = $"{targetState.Name} (副本)",
                    BackgroundImagePath = targetState.BackgroundImagePath,
                    BackgroundAssetId = targetState.BackgroundAssetId,
                    DesignWidth = targetState.DesignWidth,
                    DesignHeight = targetState.DesignHeight,
                    TextElements = targetState.TextElements.Select(elem => elem.Clone()).ToList()
                };
                _wallpapers.Add(copyState);
                SaveCurrentWallpaper();
                FilterWallpaperList();
            };

            var applyItem = new MenuItem { Header = "↻ 设为当前壁纸" };
            applyItem.Click += async (s, ev) =>
            {
                SelectWallpaper(targetState);
                await WallpaperService.GenerateAndSetWallpaperAsync(targetState);
                ToastManager.Show("成功", $"已设置壁纸“{targetState.Name}”。", ToastType.Success);
            };

            var deleteItem = new MenuItem { Header = "🗑 删除壁纸" };
            deleteItem.Click += (s, ev) =>
            {
                var confirm = new ConfirmDialog("删除壁纸", $"确定要删除“{targetState.Name}”吗？", isDanger: true) { Owner = this };
                if (confirm.ShowDialog() == true)
                {
                    _wallpapers.Remove(targetState);
                    SaveCurrentWallpaper();
                    FilterWallpaperList();
                }
            };

            cm.Items.Add(renameItem);
            cm.Items.Add(duplicateItem);
            cm.Items.Add(applyItem);
            cm.Items.Add(new Separator());
            cm.Items.Add(deleteItem);

            cm.IsOpen = true;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.Z)
            {
                UndoButton_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Y)
            {
                RedoButton_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.S)
            {
                SaveCurrentWallpaper();
                e.Handled = true;
            }
            else if (e.Key == Key.D && _selectedElement != null)
            {
                AddElement(_selectedElement.Clone());
                e.Handled = true;
            }
            else if (e.Key == Key.D0)
            {
                FitToScreen();
                e.Handled = true;
            }
            else if (e.Key == Key.D1)
            {
                ZoomSlider.Value = 100;
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Delete && _selectedElement != null)
        {
            DeleteElementButton_Click(sender, e);
            e.Handled = true;
        }
        else if (_selectedElement != null && !_selectedElement.IsLocked)
        {
            double step = Keyboard.Modifiers == ModifierKeys.Shift ? 10 : 1;
            if (e.Key == Key.Left) { _selectedElement.X = Math.Max(0, _selectedElement.X - step); e.Handled = true; }
            else if (e.Key == Key.Right) { _selectedElement.X += step; e.Handled = true; }
            else if (e.Key == Key.Up) { _selectedElement.Y = Math.Max(0, _selectedElement.Y - step); e.Handled = true; }
            else if (e.Key == Key.Down) { _selectedElement.Y += step; e.Handled = true; }

            if (e.Handled)
            {
                RenderCanvasElements();
                UpdatePropertiesPanel();
                MarkDirty();
            }
        }
    }

    private void PromptSaveUnsavedChanges()
    {
        if (!_isDirty) return;
        var confirm = new ConfirmDialog("未保存修改", "当前壁纸存在未保存的修改，是否立即保存？") { Owner = this };
        if (confirm.ShowDialog() == true)
        {
            SaveCurrentWallpaper();
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_isDirty)
        {
            var confirm = new ConfirmDialog("未保存修改", "检测到未保存的配置修改，确定要退出编辑器吗？", isDanger: true) { Owner = this };
            if (confirm.ShowDialog() != true)
            {
                e.Cancel = true;
            }
        }
    }
}