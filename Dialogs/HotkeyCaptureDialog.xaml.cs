using System.Text;
using System.Windows;
using System.Windows.Input;
using NewDesk.Models;
using NewDesk.Services;

namespace NewDesk.Dialogs;

public partial class HotkeyCaptureDialog : Window
{
    public HotkeyBinding? CapturedBinding { get; private set; }

    public HotkeyCaptureDialog()
    {
        InitializeComponent();
        PreviewKeyDown += HotkeyCaptureDialog_PreviewKeyDown;
    }

    private void HotkeyCaptureDialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Skip lone modifier keys
        if (key == Key.LeftCtrl || key == Key.RightCtrl ||
            key == Key.LeftAlt || key == Key.RightAlt ||
            key == Key.LeftShift || key == Key.RightShift ||
            key == Key.LWin || key == Key.RWin)
        {
            return;
        }

        uint mods = HotkeyModifiers.None;
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) mods |= HotkeyModifiers.Ctrl;
        if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt)) mods |= HotkeyModifiers.Alt;
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) mods |= HotkeyModifiers.Shift;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) mods |= HotkeyModifiers.Win;

        if (mods == HotkeyModifiers.None)
        {
            TxtCapturedHotkey.Text = "必须包含修饰键 (Ctrl / Alt / Shift)";
            BtnConfirm.IsEnabled = false;
            return;
        }

        CapturedBinding = new HotkeyBinding(mods, key);
        TxtCapturedHotkey.Text = CapturedBinding.ToString();
        BtnConfirm.IsEnabled = true;
    }

    private void BtnConfirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        CapturedBinding = null;
        DialogResult = false;
        Close();
    }
}
