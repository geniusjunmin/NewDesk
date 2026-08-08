using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using NewDesk.Models;

namespace NewDesk.Services;

public class HotkeyRegistrationInfo
{
    public int Id { get; set; }
    public uint Modifiers { get; set; }
    public uint Key { get; set; }
    public string Name { get; set; } = string.Empty;
    public Action Action { get; set; } = () => { };
}

public static class MultiGlobalHotkeyService
{
    private static readonly Dictionary<int, HotkeyRegistrationInfo> Hotkeys = new();
    private static HwndSource? _hwndSource;
    private static int _nextId = 9000;

    public static void Initialize(System.Windows.Window window)
    {
        var helper = new WindowInteropHelper(window);
        IntPtr handle = helper.Handle;

        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(HwndHook);
    }

    public static void UnregisterAll()
    {
        if (_hwndSource == null) return;
        IntPtr handle = _hwndSource.Handle;

        foreach (var kvp in Hotkeys)
        {
            NativeMethods.UnregisterHotKey(handle, kvp.Key);
        }
        Hotkeys.Clear();
    }

    public static bool RegisterHotkey(string name, uint modifiers, uint vk, Action action)
    {
        if (_hwndSource == null) return false;

        int id = _nextId++;
        IntPtr handle = _hwndSource.Handle;

        bool success = NativeMethods.RegisterHotKey(handle, id, modifiers, vk);
        if (success)
        {
            Hotkeys[id] = new HotkeyRegistrationInfo
            {
                Id = id,
                Modifiers = modifiers,
                Key = vk,
                Name = name,
                Action = action
            };
            AppDataPath.LogInfo($"Successfully registered global hotkey [{name}] (ID: {id})");
        }
        else
        {
            AppDataPath.LogError($"Failed to register hotkey [{name}]. Conflicts with another application.", new Exception("RegisterHotKey failure"));
        }
        return success;
    }

    public static bool TryRebindHotkeyTransactional(
        string name,
        HotkeyBinding newBinding,
        HotkeyBinding oldBinding,
        Action action,
        out string errorMessage)
    {
        errorMessage = string.Empty;
        if (_hwndSource == null)
        {
            errorMessage = "快捷键服务尚未初始化。";
            return false;
        }

        uint newVk = (uint)KeyInterop.VirtualKeyFromKey(newBinding.Key);
        uint oldVk = (uint)KeyInterop.VirtualKeyFromKey(oldBinding.Key);

        // Unregister existing hotkey for this name if any
        int existingId = -1;
        foreach (var kvp in Hotkeys)
        {
            if (kvp.Value.Name == name)
            {
                existingId = kvp.Key;
                break;
            }
        }

        if (existingId != -1)
        {
            NativeMethods.UnregisterHotKey(_hwndSource.Handle, existingId);
            Hotkeys.Remove(existingId);
        }

        bool success = RegisterHotkey(name, newBinding.Modifiers, newVk, action);
        if (!success)
        {
            // Rollback to old binding!
            RegisterHotkey(name, oldBinding.Modifiers, oldVk, action);
            errorMessage = $"快捷键 [{newBinding}] 注册失败，已被其他程序占用。已恢复为原快捷键 [{oldBinding}]。";
            return false;
        }

        return true;
    }

    private static IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (Hotkeys.TryGetValue(id, out var info))
            {
                info.Action.Invoke();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }
}
