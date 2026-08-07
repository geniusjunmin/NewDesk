using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace NewDesk.Services;

public class GlobalHotkeyService : IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HwndSource? _source;
    private readonly MainWindow _window;
    private const int HotkeyId = 9000;

    public GlobalHotkeyService(MainWindow window)
    {
        _window = window;
    }

    public void Register(uint modifiers, uint virtualKey)
    {
        try
        {
            var helper = new WindowInteropHelper(_window);
            if (helper.Handle == IntPtr.Zero)
            {
                // Window handle not ready
                return;
            }
            
            _source = HwndSource.FromHwnd(helper.Handle);
            if (_source == null)
            {
                return;
            }
            
            _source.AddHook(HwndHook);

            if (!RegisterHotKey(helper.Handle, HotkeyId, modifiers, virtualKey))
            {
                // Hotkey registration failed, but don't throw
                System.Diagnostics.Debug.WriteLine("Failed to register hotkey");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error registering hotkey: {ex.Message}");
        }
    }

    public void Unregister()
    {
        try
        {
            if (_source == null) return;
            var helper = new WindowInteropHelper(_window);
            if (helper.Handle != IntPtr.Zero)
            {
                UnregisterHotKey(helper.Handle, HotkeyId);
            }
            _source.RemoveHook(HwndHook);
            _source = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error unregistering hotkey: {ex.Message}");
        }
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            _window.ShowWindow();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        GC.SuppressFinalize(this);
    }
}

public static class HotkeyModifiers
{
    public const uint None = 0;
    public const uint Alt = 1;
    public const uint Ctrl = 2;
    public const uint Shift = 4;
    public const uint Win = 8;
}
