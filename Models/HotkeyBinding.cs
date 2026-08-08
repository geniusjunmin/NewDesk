using System.Windows.Input;

namespace NewDesk.Models;

public class HotkeyBinding
{
    public uint Modifiers { get; set; }
    public Key Key { get; set; }

    public HotkeyBinding() { }

    public HotkeyBinding(uint modifiers, Key key)
    {
        Modifiers = modifiers;
        Key = key;
    }

    public override string ToString()
    {
        string modStr = "";
        if ((Modifiers & (uint)Services.HotkeyModifiers.Ctrl) != 0) modStr += "Ctrl + ";
        if ((Modifiers & (uint)Services.HotkeyModifiers.Alt) != 0) modStr += "Alt + ";
        if ((Modifiers & (uint)Services.HotkeyModifiers.Shift) != 0) modStr += "Shift + ";

        return modStr + Key.ToString();
    }
}
