using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using NewDesk.Models;

namespace NewDesk.Services;

public sealed class HotkeyApplyResult
{
    public bool Success { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;

    public static HotkeyApplyResult Succeeded() => new() { Success = true };
    public static HotkeyApplyResult Failed(string error) => new() { ErrorMessage = error };
}

public interface IHotkeyRegistrationBackend
{
    void UnregisterAll();
    bool RegisterHotkey(string name, HotkeyBinding binding, Action action);
}

public sealed class MultiGlobalHotkeyRegistrationBackend : IHotkeyRegistrationBackend
{
    public void UnregisterAll() => MultiGlobalHotkeyService.UnregisterAll();

    public bool RegisterHotkey(string name, HotkeyBinding binding, Action action)
    {
        return MultiGlobalHotkeyService.RegisterHotkey(
            name,
            binding.Modifiers,
            (uint)KeyInterop.VirtualKeyFromKey(binding.Key),
            action);
    }
}

public static class HotkeyConfigurationTransaction
{
    public static HotkeyApplyResult TryApply(
        AppSettings current,
        AppSettings candidate,
        IHotkeyRegistrationBackend backend,
        IReadOnlyDictionary<string, Action> actions,
        Action<AppSettings> persist)
    {
        var candidateBindings = GetBindings(candidate);
        var duplicate = candidateBindings
            .GroupBy(item => (item.Binding.Modifiers, item.Binding.Key))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
        {
            return HotkeyApplyResult.Failed($"NewDesk 内部快捷键重复：{string.Join("、", duplicate.Select(item => item.Name))}");
        }

        var oldBindings = GetBindings(current);
        backend.UnregisterAll();
        string? registrationFailure = RegisterAll(candidateBindings, backend, actions);
        if (registrationFailure != null)
        {
            return RollBack(oldBindings, backend, actions, registrationFailure);
        }

        try
        {
            persist(candidate);
            return HotkeyApplyResult.Succeeded();
        }
        catch (Exception ex)
        {
            return RollBack(oldBindings, backend, actions, $"快捷键设置保存失败：{ex.Message}");
        }
    }

    private static HotkeyApplyResult RollBack(
        IReadOnlyList<(string Name, HotkeyBinding Binding)> oldBindings,
        IHotkeyRegistrationBackend backend,
        IReadOnlyDictionary<string, Action> actions,
        string originalFailure)
    {
        backend.UnregisterAll();
        string? rollbackFailure = RegisterAll(oldBindings, backend, actions);
        if (rollbackFailure != null)
        {
            var critical = $"CRITICAL: 快捷键回滚失败。{rollbackFailure}";
            AppDataPath.LogError("HotkeyConfigurationTransaction.Rollback", new InvalidOperationException(critical));
            return HotkeyApplyResult.Failed($"{originalFailure}；{critical}");
        }

        return HotkeyApplyResult.Failed($"{originalFailure}；已恢复原快捷键。");
    }

    private static string? RegisterAll(
        IReadOnlyList<(string Name, HotkeyBinding Binding)> bindings,
        IHotkeyRegistrationBackend backend,
        IReadOnlyDictionary<string, Action> actions)
    {
        foreach (var item in bindings)
        {
            if (!actions.TryGetValue(item.Name, out var action) || !backend.RegisterHotkey(item.Name, item.Binding, action))
            {
                return $"快捷键 [{item.Binding}] 注册失败或已被占用";
            }
        }

        return null;
    }

    private static IReadOnlyList<(string Name, HotkeyBinding Binding)> GetBindings(AppSettings settings) =>
    [
        ("Main Window", settings.MainWindowHotkey),
        ("AI Quick Search", settings.AiQuickHotkey),
        ("Command Palette", settings.CommandPaletteHotkey),
        ("Clipboard AI", settings.ClipboardAiHotkey)
    ];
}
