using System.Collections.Generic;
using System.Windows;
using NewDesk.Models;
using NewDesk.Services;

namespace NewDesk.Dialogs;

public partial class DynamicDataSourcePickerDialog : Window
{
    public DynamicDataSource? SelectedDataSource { get; private set; }

    public DynamicDataSourcePickerDialog()
    {
        InitializeComponent();
        Loaded += DynamicDataSourcePickerDialog_Loaded;
    }

    private void DynamicDataSourcePickerDialog_Loaded(object sender, RoutedEventArgs e)
    {
        var sources = DynamicDataService.LoadSources();
        if (sources.Count == 0)
        {
            sources = DynamicDataService.GetDefaultPresets();
        }

        DataSourcesListBox.ItemsSource = sources;
        if (sources.Count > 0)
        {
            DataSourcesListBox.SelectedIndex = 0;
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataSourcesListBox.SelectedItem is DynamicDataSource source)
        {
            SelectedDataSource = source;
            DialogResult = true;
            Close();
        }
        else
        {
            ToastManager.Show("提示", "请先选择一个数据源。", ToastType.Warning);
        }
    }
}
