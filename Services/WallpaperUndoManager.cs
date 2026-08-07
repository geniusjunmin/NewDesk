using System;
using System.Collections.Generic;
using System.Text.Json;
using NewDesk.Models;

namespace NewDesk.Services;

public class WallpaperUndoManager
{
    private readonly Stack<string> _undoStack = new();
    private readonly Stack<string> _redoStack = new();
    private const int MaxHistory = 50;

    public event EventHandler? StateChanged;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public void PushSnapshot(List<TextElementState> elements)
    {
        try
        {
            string snapshotStr = JsonSerializer.Serialize(elements);
            if (_undoStack.Count > 0 && _undoStack.Peek() == snapshotStr)
                return; // Avoid duplicate consecutive snapshots

            _undoStack.Push(snapshotStr);
            if (_undoStack.Count > MaxHistory)
            {
                // Trim oldest items
                var list = new List<string>(_undoStack);
                list.RemoveAt(list.Count - 1);
                _undoStack.Clear();
                for (int i = list.Count - 1; i >= 0; i--) _undoStack.Push(list[i]);
            }
            _redoStack.Clear();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("WallpaperUndoManager.PushSnapshot", ex);
        }
    }

    public List<TextElementState>? Undo(List<TextElementState> currentElements)
    {
        if (!CanUndo) return null;

        try
        {
            string currentSnapshot = JsonSerializer.Serialize(currentElements);
            _redoStack.Push(currentSnapshot);

            string prevSnapshot = _undoStack.Pop();
            StateChanged?.Invoke(this, EventArgs.Empty);
            return JsonSerializer.Deserialize<List<TextElementState>>(prevSnapshot);
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("WallpaperUndoManager.Undo", ex);
            return null;
        }
    }

    public List<TextElementState>? Redo(List<TextElementState> currentElements)
    {
        if (!CanRedo) return null;

        try
        {
            string currentSnapshot = JsonSerializer.Serialize(currentElements);
            _undoStack.Push(currentSnapshot);

            string nextSnapshot = _redoStack.Pop();
            StateChanged?.Invoke(this, EventArgs.Empty);
            return JsonSerializer.Deserialize<List<TextElementState>>(nextSnapshot);
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("WallpaperUndoManager.Redo", ex);
            return null;
        }
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
