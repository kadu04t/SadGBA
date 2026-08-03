using SadGBA.Core.Bus;
using SadGBA.Core.Cpu;

namespace SadGBA.Core;

/// <summary>Coordinates the GBA CPU, system bus, and clocked devices.</summary>
public sealed class GbaMachine
{
    public const uint CartridgeEntryPoint = 0x0800_0000;

    public GbaBus Bus { get; }
    public Arm7Tdmi Cpu { get; }
    public ulong ClockCycles { get; private set; }

    public GbaMachine() : this(new GbaBus()) { }

    public GbaMachine(GbaBus bus)
    {
        Bus = bus ?? throw new ArgumentNullException(nameof(bus));
        Cpu = new Arm7Tdmi(Bus);
    }

    public void LoadBios(ReadOnlySpan<byte> image) => Bus.Bios.Load(image);
    public void LoadBios(string path) => Bus.Bios.Load(path);
    public void LoadCartridge(ReadOnlySpan<byte> image) => Bus.GamePak.Load(image);
    public void LoadCartridge(string path) => Bus.GamePak.Load(path);

    public void Reset(bool skipBios = false)
    {
        if (skipBios && !Bus.GamePak.IsLoaded)
            throw new InvalidOperationException("Não é possível pular a BIOS sem um cartucho carregado.");
        if (!skipBios && !Bus.Bios.IsLoaded)
            throw new InvalidOperationException("Carregue uma BIOS válida ou use skipBios.");

        Bus.Reset();
        ClockCycles = 0;
        if (skipBios)
        {
            Cpu.InitializeForCartridge(CartridgeEntryPoint);
            Bus.Io.InitializeAfterBiosSkip();
        }
        else
        {
            Cpu.Reset();
        }
    }

    public void Step()
    {
        if (Bus.Io.Halted || Bus.Io.Stopped)
        {
            Bus.Io.Tick(1);
            ClockCycles++;
            if (Bus.Io.Interrupts.WakePending)
                Bus.Io.Resume();
            return;
        }

        Cpu.Step();
        Bus.Io.Tick(Cpu.LastStepCycles);
        ClockCycles += Cpu.LastStepCycles;
    }

    public void Run(ulong instructionCount)
    {
        for (ulong index = 0; index < instructionCount; index++)
            Step();
    }

    public bool RunUntil(Func<GbaMachine, bool> stopCondition, ulong maximumInstructions)
    {
        ArgumentNullException.ThrowIfNull(stopCondition);
        for (ulong index = 0; index < maximumInstructions; index++)
        {
            Step();
            if (stopCondition(this)) return true;
        }
        return false;
    }
}
