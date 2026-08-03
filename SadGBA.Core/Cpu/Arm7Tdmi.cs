using SadGBA.Core.Bus;

namespace SadGBA.Core.Cpu;

/// <summary>Estado inicial do processador ARM7TDMI do Game Boy Advance.</summary>
public sealed class Arm7Tdmi
{
    public const uint NegativeFlag = 1u << 31;
    public const uint ZeroFlag = 1u << 30;
    public const uint CarryFlag = 1u << 29;
    public const uint OverflowFlag = 1u << 28;
    public const uint IrqDisableFlag = 1u << 7;
    public const uint FiqDisableFlag = 1u << 6;
    public const uint ThumbFlag = 1u << 5;

    private readonly GbaBus _bus;
    private readonly uint[] _registers = new uint[16];

    public uint Cpsr { get; private set; }
    public CpuMode Mode => (CpuMode)(Cpsr & 0x1F);
    public bool ThumbState => (Cpsr & ThumbFlag) != 0;
    public uint Pc => _registers[15];
    public ulong InstructionCount { get; private set; }

    public Arm7Tdmi(GbaBus bus)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        Reset();
    }

    public Arm7Tdmi() : this(new GbaBus()) { }

    public uint GetRegister(int index)
    {
        ValidateRegister(index);
        return _registers[index];
    }

    public void SetRegister(int index, uint value)
    {
        ValidateRegister(index);
        _registers[index] = index == 15
            ? value & (ThumbState ? ~1u : ~3u)
            : value;
    }

    public void Reset(uint pc = 0)
    {
        Array.Clear(_registers);
        Cpsr = (uint)CpuMode.Supervisor | IrqDisableFlag | FiqDisableFlag;
        _registers[15] = pc & ~3u;
        InstructionCount = 0;
    }

    /// <summary>
    /// Busca uma instrução e avança o PC. A decodificação ARM/Thumb será
    /// construída incrementalmente sobre este primeiro pipeline.
    /// </summary>
    public void Step()
    {
        if (ThumbState)
        {
            _ = _bus.ReadInstruction16(Pc);
            _registers[15] += 2;
        }
        else
        {
            _ = _bus.ReadInstruction32(Pc);
            _registers[15] += 4;
        }

        InstructionCount++;
    }

    private static void ValidateRegister(int index)
    {
        if ((uint)index >= 16)
            throw new ArgumentOutOfRangeException(nameof(index));
    }
}
