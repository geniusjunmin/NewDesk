using System.Windows.Input;
using NewDesk.Models;
using NewDesk.Services;
using Xunit;

namespace NewDesk.Tests;

public class HotkeyTests
{
    private static readonly IReadOnlyDictionary<string, Action> Actions = new Dictionary<string, Action>
    {
        ["Main Window"] = () => { },
        ["AI Quick Search"] = () => { },
        ["Command Palette"] = () => { },
        ["Clipboard AI"] = () => { }
    };

    [Fact]
    public void NewHotkeySuccess_Persists()
    {
        var current = new AppSettings();
        var candidate = CloneWithMainKey(current, Key.F8);
        AppSettings? persisted = null;

        var result = HotkeyConfigurationTransaction.TryApply(current, candidate, new FakeBackend(), Actions, value => persisted = value);

        Assert.True(result.Success);
        Assert.Same(candidate, persisted);
        Assert.Equal(Key.F8, persisted!.MainWindowHotkey.Key);
    }

    [Fact]
    public void NewHotkeyConflict_DoesNotPersist()
    {
        var current = new AppSettings();
        var candidate = CloneWithMainKey(current, Key.F8);
        bool persisted = false;
        var backend = new FakeBackend { FailKey = Key.F8 };

        var result = HotkeyConfigurationTransaction.TryApply(current, candidate, backend, Actions, _ => persisted = true);

        Assert.False(result.Success);
        Assert.False(persisted);
    }

    [Fact]
    public void Conflict_RestoresOldRegistration()
    {
        var current = new AppSettings();
        var candidate = CloneWithMainKey(current, Key.F8);
        var backend = new FakeBackend { FailKey = Key.F8 };

        var result = HotkeyConfigurationTransaction.TryApply(current, candidate, backend, Actions, _ => { });

        Assert.False(result.Success);
        Assert.Contains(backend.RegistrationBatches, batch =>
            batch.Any(item => item.Name == "Main Window" && item.Key == current.MainWindowHotkey.Key));
    }

    [Fact]
    public void DuplicateInternalBinding_Rejected()
    {
        var current = new AppSettings();
        var candidate = CloneWithMainKey(current, current.AiQuickHotkey.Key, current.AiQuickHotkey.Modifiers);
        var backend = new FakeBackend();

        var result = HotkeyConfigurationTransaction.TryApply(current, candidate, backend, Actions, _ => { });

        Assert.False(result.Success);
        Assert.Contains("重复", result.ErrorMessage);
        Assert.Empty(backend.RegistrationBatches);
    }

    [Fact]
    public void MainWindowUsesCandidateNotStaleSettings()
    {
        var current = new AppSettings();
        var candidate = CloneWithMainKey(current, Key.F8);
        var backend = new FakeBackend();

        var result = HotkeyConfigurationTransaction.TryApply(current, candidate, backend, Actions, _ => { });

        Assert.True(result.Success);
        Assert.Contains(backend.RegistrationBatches.SelectMany(batch => batch), item =>
            item.Name == "Main Window" && item.Key == Key.F8);
    }

    private static AppSettings CloneWithMainKey(AppSettings source, Key key, uint? modifiers = null)
    {
        return new AppSettings
        {
            MainWindowHotkey = new HotkeyBinding(modifiers ?? source.MainWindowHotkey.Modifiers, key),
            AiQuickHotkey = new HotkeyBinding(source.AiQuickHotkey.Modifiers, source.AiQuickHotkey.Key),
            CommandPaletteHotkey = new HotkeyBinding(source.CommandPaletteHotkey.Modifiers, source.CommandPaletteHotkey.Key),
            ClipboardAiHotkey = new HotkeyBinding(source.ClipboardAiHotkey.Modifiers, source.ClipboardAiHotkey.Key)
        };
    }

    private sealed class FakeBackend : IHotkeyRegistrationBackend
    {
        private List<(string Name, Key Key)> _currentBatch = [];
        public Key? FailKey { get; init; }
        public List<List<(string Name, Key Key)>> RegistrationBatches { get; } = [];

        public void UnregisterAll()
        {
            _currentBatch = [];
            RegistrationBatches.Add(_currentBatch);
        }

        public bool RegisterHotkey(string name, HotkeyBinding binding, Action action)
        {
            if (binding.Key == FailKey) return false;
            _currentBatch.Add((name, binding.Key));
            return true;
        }
    }
}
