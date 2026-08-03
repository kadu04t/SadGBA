using SadGBA.Core.Apu;
using SadGBA.Core.Dma;
using SadGBA.Core.Input;
using SadGBA.Core.Interrupts;
using SadGBA.Core.Ppu;
using SadGBA.Core.Timers;

namespace SadGBA.Core.Bus;

public sealed class IoRegisters
{
    public InterruptController Interrupts { get; }
    public DisplayController Display { get; }
    public TimerController Timers { get; }
    public Keypad Keypad { get; }
    public DmaController Dma { get; }
    public SoundController Sound { get; }
    public ushort WaitStateControl { get; private set; }
    public byte PostBootFlag { get; private set; }
    public bool Halted { get; private set; }
    public bool Stopped { get; private set; }

    public IoRegisters()
    {
        Interrupts = new InterruptController();
        Display = new DisplayController(Interrupts);
        Timers = new TimerController(Interrupts);
        Keypad = new Keypad();
        Dma = new DmaController(Interrupts);
        Sound = new SoundController();
        Display.VBlankStarted += () => Dma.Trigger(1);
        Display.HBlankStarted += () => Dma.Trigger(2);
    }

    public byte Read8(uint offset)
    {
        ushort value = Read16(offset & ~1u);
        return (byte)(value >> ((int)(offset & 1) * 8));
    }

    public ushort Read16(uint offset)
    {
        offset &= 0x3FE;
        if (offset <= 0x01E)
            return Display.ReadRegister(offset);
        if (offset is >= 0x060 and <= 0x09E)
            return Sound.Read16(offset);
        if (offset is >= 0x0B0 and <= 0x0DE)
            return Dma.ReadRegister(offset);
        if (offset is >= 0x100 and <= 0x10E)
            return Timers.ReadRegister(offset);
        if (offset == 0x130)
            return Keypad.KeyInput;
        if (offset == 0x204)
            return WaitStateControl;
        if (offset is 0x200 or 0x202 or 0x208)
            return Interrupts.ReadRegister(offset);
        if (offset == 0x300)
            return PostBootFlag;
        return 0;
    }

    public uint Read32(uint offset) => (uint)(Read16(offset) | (Read16(offset + 2) << 16));

    public void Write8(uint offset, byte value)
    {
        uint register = offset & 0x3FF;
        if (register == 0x300)
        {
            PostBootFlag = (byte)(value & 1);
            return;
        }
        if (register == 0x301)
        {
            Stopped = (value & 0x80) != 0;
            Halted = !Stopped;
            return;
        }

        uint aligned = offset & ~1u;
        ushort current = Read16(aligned);
        int shift = (int)(offset & 1) * 8;
        Write16(aligned, (ushort)((current & ~(0xFF << shift)) | (value << shift)));
    }

    public void Write16(uint offset, ushort value)
    {
        offset &= 0x3FE;
        if (offset <= 0x01E)
            Display.WriteRegister(offset, value);
        else if (offset is >= 0x060 and <= 0x0A6)
            Sound.Write16(offset, value);
        else if (offset is >= 0x0B0 and <= 0x0DE)
            Dma.WriteRegister(offset, value);
        else if (offset is >= 0x100 and <= 0x10E)
            Timers.WriteRegister(offset, value);
        else if (offset is 0x200 or 0x202 or 0x208)
            Interrupts.WriteRegister(offset, value);
        else if (offset == 0x204)
            WaitStateControl = (ushort)(value & 0x5FFF);
        else if (offset == 0x300)
        {
            PostBootFlag = (byte)(value & 1);
            Stopped = (value & 0x8000) != 0;
            Halted = !Stopped;
        }
    }

    public void Write32(uint offset, uint value)
    {
        Write16(offset, (ushort)value);
        Write16(offset + 2, (ushort)(value >> 16));
    }

    public void Tick(uint cycles)
    {
        Display.Tick(cycles);
        Timers.Tick(cycles);
        Sound.Tick(cycles);
    }

    public void Reset()
    {
        Interrupts.Reset();
        Display.Reset();
        Timers.Reset();
        Dma.Reset();
        Sound.Reset();
        Keypad.Reset();
        WaitStateControl = 0;
        PostBootFlag = 0;
        Halted = false;
        Stopped = false;
    }

    public void InitializeAfterBiosSkip() => PostBootFlag = 1;

    public void Resume()
    {
        Halted = false;
        Stopped = false;
    }
}
