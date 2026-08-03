namespace SadGBA.Core.Input;

[Flags]
public enum GbaButton : ushort
{
    A = 1 << 0,
    B = 1 << 1,
    Select = 1 << 2,
    Start = 1 << 3,
    Right = 1 << 4,
    Left = 1 << 5,
    Up = 1 << 6,
    Down = 1 << 7,
    R = 1 << 8,
    L = 1 << 9,
}

/// <summary>Estado das dez teclas; KEYINPUT usa lógica ativa em nível baixo.</summary>
public sealed class Keypad
{
    private ushort _pressed;

    public ushort KeyInput => (ushort)(~_pressed & 0x03FF);

    public void SetPressed(GbaButton button, bool pressed)
    {
        if (pressed)
            _pressed |= (ushort)button;
        else
            _pressed &= (ushort)~button;
    }

    public bool IsPressed(GbaButton button) => (_pressed & (ushort)button) != 0;
    public void Reset() => _pressed = 0;
}

