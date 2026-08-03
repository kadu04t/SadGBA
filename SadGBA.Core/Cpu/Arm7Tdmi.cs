using System.Numerics;
using SadGBA.Core.Bus;

namespace SadGBA.Core.Cpu;

/// <summary>Primeiro interpretador ARM do processador ARM7TDMI.</summary>
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
    private uint _instructionAddress;

    public uint Cpsr { get; private set; }
    public CpuMode Mode => (CpuMode)(Cpsr & 0x1F);
    public bool ThumbState => (Cpsr & ThumbFlag) != 0;
    public bool IrqDisabled => (Cpsr & IrqDisableFlag) != 0;
    public uint Pc => _registers[15];
    public ulong InstructionCount { get; private set; }
    public ulong ClockCycles { get; private set; }
    public uint LastStepCycles { get; private set; }
    public uint LastInstruction { get; private set; }

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
        _registers[index] = index == 15 ? value & ~3u : value;
    }

    public void SetProgramStatus(uint value)
    {
        CpuMode mode = (CpuMode)(value & 0x1F);
        if (!Enum.IsDefined(mode))
            throw new ArgumentException($"Modo inválido: 0x{(uint)mode:X2}.", nameof(value));
        Cpsr = value;
    }

    public void Reset(uint pc = 0)
    {
        Array.Clear(_registers);
        Cpsr = (uint)CpuMode.Supervisor | IrqDisableFlag | FiqDisableFlag;
        _registers[15] = pc & ~3u;
        InstructionCount = 0;
        ClockCycles = 0;
        LastStepCycles = 0;
        LastInstruction = 0;
    }

    public void Step()
    {
        LastStepCycles = 0;
        _instructionAddress = Pc;
        LastInstruction = _bus.ReadInstruction32(Pc);
        _registers[15] += 4;
        ExecuteArm(LastInstruction);
        InstructionCount++;
        LastStepCycles += _bus.EstimateAccessCycles(_instructionAddress, 4);
        ClockCycles += LastStepCycles;
    }

    private void ExecuteArm(uint instruction)
    {
        if (!ConditionPassed((int)(instruction >> 28)))
            return;

        if ((instruction & 0x0FFF_FFF0) == 0x012F_FF10)
        {
            uint target = ReadRegister((int)(instruction & 0xF));
            Cpsr = (target & 1) != 0 ? Cpsr | ThumbFlag : Cpsr & ~ThumbFlag;
            _registers[15] = target & ((target & 1) != 0 ? ~1u : ~3u);
            LastStepCycles += 2;
            return;
        }

        if ((instruction & 0x0E00_0000) == 0x0A00_0000)
        {
            int displacement = (int)(instruction << 8) >> 6;
            if ((instruction & (1 << 24)) != 0)
                _registers[14] = _instructionAddress + 4;
            _registers[15] = unchecked(_instructionAddress + 8u + (uint)displacement) & ~3u;
            LastStepCycles += 2;
            return;
        }

        if ((instruction & 0x0C00_0000) == 0x0400_0000)
        {
            ExecuteTransfer(instruction);
            return;
        }

        if ((instruction & 0x0C00_0000) == 0)
            ExecuteDataProcessing(instruction);
    }

    private void ExecuteDataProcessing(uint instruction)
    {
        int opcode = (int)((instruction >> 21) & 0xF);
        bool setFlags = (instruction & (1 << 20)) != 0;
        int rn = (int)((instruction >> 16) & 0xF);
        int rd = (int)((instruction >> 12) & 0xF);
        uint left = ReadRegister(rn);
        uint right = DecodeOperand(instruction, out bool shifterCarry);
        uint result;
        bool carry;
        bool write = opcode is not (8 or 9 or 10 or 11);

        switch (opcode)
        {
            case 0: result = left & right; carry = shifterCarry; break;
            case 1: result = left ^ right; carry = shifterCarry; break;
            case 2: result = left - right; carry = left >= right; break;
            case 4: result = left + right; carry = result < left; break;
            case 8: result = left & right; carry = shifterCarry; break;
            case 9: result = left ^ right; carry = shifterCarry; break;
            case 10: result = left - right; carry = left >= right; break;
            case 11: result = left + right; carry = result < left; break;
            case 12: result = left | right; carry = shifterCarry; break;
            case 13: result = right; carry = shifterCarry; break;
            case 14: result = left & ~right; carry = shifterCarry; break;
            case 15: result = ~right; carry = shifterCarry; break;
            default: return;
        }

        if (write)
            WriteRegister(rd, result);
        if (setFlags || !write)
            SetFlags(result, carry);
    }

    private void ExecuteTransfer(uint instruction)
    {
        bool pre = (instruction & (1 << 24)) != 0;
        bool up = (instruction & (1 << 23)) != 0;
        bool byteTransfer = (instruction & (1 << 22)) != 0;
        bool writeBack = (instruction & (1 << 21)) != 0;
        bool load = (instruction & (1 << 20)) != 0;
        int rn = (int)((instruction >> 16) & 0xF);
        int rd = (int)((instruction >> 12) & 0xF);
        uint basis = ReadRegister(rn);
        uint offset = instruction & 0xFFF;
        uint adjusted = up ? basis + offset : basis - offset;
        uint address = pre ? adjusted : basis;

        if (load)
            WriteRegister(rd, byteTransfer ? _bus.Read8(address) : _bus.Read32(address));
        else if (byteTransfer)
            _bus.Write8(address, (byte)ReadRegister(rd));
        else
            _bus.Write32(address, ReadRegister(rd));

        if (!pre || writeBack)
            WriteRegister(rn, adjusted);
        LastStepCycles += _bus.EstimateAccessCycles(address, byteTransfer ? 1 : 4);
    }

    private uint DecodeOperand(uint instruction, out bool carry)
    {
        if ((instruction & (1 << 25)) == 0)
        {
            carry = (Cpsr & CarryFlag) != 0;
            return ReadRegister((int)(instruction & 0xF));
        }

        int rotation = (int)((instruction >> 8) & 0xF) * 2;
        uint value = BitOperations.RotateRight(instruction & 0xFF, rotation);
        carry = rotation == 0 ? (Cpsr & CarryFlag) != 0 : (value & NegativeFlag) != 0;
        return value;
    }

    private bool ConditionPassed(int condition)
    {
        bool n = (Cpsr & NegativeFlag) != 0;
        bool z = (Cpsr & ZeroFlag) != 0;
        bool c = (Cpsr & CarryFlag) != 0;
        bool v = (Cpsr & OverflowFlag) != 0;
        return condition switch
        {
            0 => z,
            1 => !z,
            2 => c,
            3 => !c,
            4 => n,
            5 => !n,
            6 => v,
            7 => !v,
            8 => c && !z,
            9 => !c || z,
            10 => n == v,
            11 => n != v,
            12 => !z && n == v,
            13 => z || n != v,
            14 => true,
            _ => false,
        };
    }

    private uint ReadRegister(int index) => index == 15 ? _instructionAddress + 8 : _registers[index];

    private void WriteRegister(int index, uint value)
    {
        if (index == 15)
            _registers[15] = value & ~3u;
        else
            _registers[index] = value;
    }

    private void SetFlags(uint result, bool carry)
    {
        Cpsr = (Cpsr & ~(NegativeFlag | ZeroFlag | CarryFlag)) |
            ((result & NegativeFlag) != 0 ? NegativeFlag : 0) |
            (result == 0 ? ZeroFlag : 0) |
            (carry ? CarryFlag : 0);
    }

    private static void ValidateRegister(int index)
    {
        if ((uint)index >= 16)
            throw new ArgumentOutOfRangeException(nameof(index));
    }
}
