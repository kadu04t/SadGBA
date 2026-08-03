using System.Numerics;
using SadGBA.Core.Bus;

namespace SadGBA.Core.Cpu;

/// <summary>
/// Interpretador ARM7TDMI. Implementa o conjunto ARMv4T essencial e mantém
/// ARM e Thumb no mesmo pipeline lógico, com bancos de SP/LR por modo.
/// </summary>
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
    private readonly uint[] _bankedSp = new uint[6];
    private readonly uint[] _bankedLr = new uint[6];
    private readonly uint[] _savedStatus = new uint[6];
    private readonly uint[] _userHighRegisters = new uint[5];
    private readonly uint[] _fiqHighRegisters = new uint[5];
    private uint _currentAddress;
    private bool _currentThumb;

    public uint Cpsr { get; private set; }
    public CpuMode Mode => (CpuMode)(Cpsr & 0x1F);
    public bool ThumbState => (Cpsr & ThumbFlag) != 0;
    public bool IrqDisabled => (Cpsr & IrqDisableFlag) != 0;
    public uint Pc => _registers[15];
    public ulong InstructionCount { get; private set; }
    public ulong ClockCycles { get; private set; }
    public uint LastStepCycles { get; private set; }
    public uint LastInstructionAddress { get; private set; }
    public uint LastInstruction { get; private set; }
    public CpuException LastException { get; private set; }

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
        if (index == 15)
            value &= ThumbState ? ~1u : ~3u;
        _registers[index] = value;
    }

    public void SetProgramStatus(uint value)
    {
        CpuMode mode = (CpuMode)(value & 0x1F);
        if (!Enum.IsDefined(mode))
            throw new ArgumentException($"Modo inválido no CPSR: 0x{(uint)mode:X2}.", nameof(value));
        SwitchMode(mode);
        Cpsr = value;
        _registers[15] &= ThumbState ? ~1u : ~3u;
    }

    public void Reset(uint pc = 0)
    {
        Array.Clear(_registers);
        Array.Clear(_bankedSp);
        Array.Clear(_bankedLr);
        Array.Clear(_savedStatus);
        Array.Clear(_userHighRegisters);
        Array.Clear(_fiqHighRegisters);
        Cpsr = (uint)CpuMode.Supervisor | IrqDisableFlag | FiqDisableFlag;
        _registers[15] = pc & ~3u;
        InstructionCount = 0;
        ClockCycles = 0;
        LastStepCycles = 0;
        LastInstructionAddress = 0;
        LastInstruction = 0;
        LastException = CpuException.None;
    }

    /// <summary>Prepara bancos de pilha e estado usados na entrada de um Game Pak sem executar a BIOS.</summary>
    public void InitializeForCartridge(uint entryPoint)
    {
        Reset();

        SetProgramStatus((uint)CpuMode.Irq | IrqDisableFlag | FiqDisableFlag);
        SetRegister(13, 0x0300_7FA0);

        SetProgramStatus((uint)CpuMode.Supervisor | IrqDisableFlag | FiqDisableFlag);
        SetRegister(13, 0x0300_7FE0);

        SetProgramStatus((uint)CpuMode.System | IrqDisableFlag | FiqDisableFlag);
        SetRegister(13, 0x0300_7F00);
        SetRegister(15, entryPoint);
    }

    public void Step()
    {
        LastStepCycles = 0;
        LastException = CpuException.None;
        if (!IrqDisabled && _bus.Io.Interrupts.IrqPending)
        {
            EnterException(CpuException.Irq, CpuMode.Irq, 0x18, Pc + 4);
            CompleteStep(3);
            return;
        }

        _currentAddress = Pc;
        _currentThumb = ThumbState;
        LastInstructionAddress = _currentAddress;

        uint fetchCycles;
        if (_currentThumb)
        {
            ushort instruction = _bus.ReadInstruction16(_currentAddress);
            LastInstruction = instruction;
            _registers[15] = _currentAddress + 2;
            fetchCycles = _bus.EstimateAccessCycles(_currentAddress, 2);
            ExecuteThumb(instruction);
        }
        else
        {
            uint instruction = _bus.ReadInstruction32(_currentAddress);
            LastInstruction = instruction;
            _registers[15] = _currentAddress + 4;
            fetchCycles = _bus.EstimateAccessCycles(_currentAddress, 4);
            ExecuteArm(instruction);
        }

        InstructionCount++;
        CompleteStep(fetchCycles);
    }

    private void ExecuteArm(uint instruction)
    {
        int condition = (int)(instruction >> 28);
        if (!ConditionPassed(condition))
            return;

        if ((instruction & 0x0FFF_FFF0) == 0x012F_FF10)
        {
            BranchExchange(ReadOperandRegister((int)(instruction & 0xF)));
            AddCycles(2);
            return;
        }

        if ((instruction & 0x0FBF_0FFF) == 0x010F_0000)
        {
            int rd = (int)((instruction >> 12) & 0xF);
            bool saved = (instruction & (1 << 22)) != 0;
            WriteResult(rd, saved ? ReadSavedStatus() : Cpsr);
            return;
        }

        if ((instruction & 0x0FB0_FFF0) == 0x0120_F000)
        {
            WriteStatusRegister(
                ReadOperandRegister((int)(instruction & 0xF)),
                (int)((instruction >> 16) & 0xF),
                (instruction & (1 << 22)) != 0);
            return;
        }

        if ((instruction & 0x0FB0_F000) == 0x0320_F000)
        {
            uint immediate = BitOperations.RotateRight(
                instruction & 0xFF,
                (int)((instruction >> 8) & 0xF) * 2);
            WriteStatusRegister(immediate, (int)((instruction >> 16) & 0xF),
                (instruction & (1 << 22)) != 0);
            return;
        }

        if ((instruction & 0x0E00_0000) == 0x0A00_0000)
        {
            int displacement = (int)(instruction << 8) >> 6;
            if ((instruction & (1 << 24)) != 0)
                _registers[14] = _currentAddress + 4;
            _registers[15] = unchecked(_currentAddress + 8u + (uint)displacement) & ~3u;
            AddCycles(2);
            return;
        }

        if ((instruction & 0x0F00_0000) == 0x0F00_0000)
        {
            EnterException(CpuException.SoftwareInterrupt, CpuMode.Supervisor, 0x08, _registers[15]);
            AddCycles(2);
            return;
        }

        if ((instruction & 0x0FB0_0FF0) == 0x0100_0090)
        {
            ExecuteSwap(instruction);
            return;
        }

        if ((instruction & 0x0F80_00F0) == 0x0080_0090)
        {
            ExecuteLongMultiply(instruction);
            return;
        }

        if ((instruction & 0x0FC0_00F0) == 0x0000_0090)
        {
            ExecuteMultiply(instruction);
            return;
        }

        if ((instruction & 0x0E00_0090) == 0x0000_0090)
        {
            ExecuteHalfwordTransfer(instruction);
            return;
        }

        if ((instruction & 0x0E00_0000) == 0x0800_0000)
        {
            ExecuteBlockTransfer(instruction);
            return;
        }

        if ((instruction & 0x0C00_0000) == 0x0400_0000)
        {
            ExecuteSingleTransfer(instruction);
            return;
        }

        if ((instruction & 0x0C00_0000) == 0)
        {
            ExecuteDataProcessing(instruction);
            return;
        }

        RaiseUndefined();
    }

    private void ExecuteDataProcessing(uint instruction)
    {
        int opcode = (int)((instruction >> 21) & 0xF);
        bool setFlags = (instruction & (1 << 20)) != 0;
        int rn = (int)((instruction >> 16) & 0xF);
        int rd = (int)((instruction >> 12) & 0xF);
        uint left = ReadOperandRegister(rn);
        uint right = DecodeArmOperand2(instruction, out bool shifterCarry);
        uint carryIn = IsFlagSet(CarryFlag) ? 1u : 0u;
        uint result;
        bool carry = false;
        bool overflow = false;
        bool writesResult = opcode is not (8 or 9 or 10 or 11);

        switch (opcode)
        {
            case 0: result = left & right; carry = shifterCarry; break;
            case 1: result = left ^ right; carry = shifterCarry; break;
            case 2: result = left - right; carry = left >= right; overflow = SubOverflow(left, right, result); break;
            case 3: result = right - left; carry = right >= left; overflow = SubOverflow(right, left, result); break;
            case 4: result = left + right; carry = result < left; overflow = AddOverflow(left, right, result); break;
            case 5:
                ulong adc = (ulong)left + right + carryIn;
                result = (uint)adc; carry = adc > uint.MaxValue; overflow = AddOverflow(left, right + carryIn, result); break;
            case 6:
                ulong subtrahend = (ulong)right + (1u - carryIn);
                result = unchecked(left - (uint)subtrahend); carry = (ulong)left >= subtrahend; overflow = SubOverflow(left, (uint)subtrahend, result); break;
            case 7:
                ulong reverseSubtrahend = (ulong)left + (1u - carryIn);
                result = unchecked(right - (uint)reverseSubtrahend); carry = (ulong)right >= reverseSubtrahend; overflow = SubOverflow(right, (uint)reverseSubtrahend, result); break;
            case 8: result = left & right; carry = shifterCarry; break;
            case 9: result = left ^ right; carry = shifterCarry; break;
            case 10: result = left - right; carry = left >= right; overflow = SubOverflow(left, right, result); break;
            case 11: result = left + right; carry = result < left; overflow = AddOverflow(left, right, result); break;
            case 12: result = left | right; carry = shifterCarry; break;
            case 13: result = right; carry = shifterCarry; break;
            case 14: result = left & ~right; carry = shifterCarry; break;
            default: result = ~right; carry = shifterCarry; break;
        }

        if (writesResult)
            WriteResult(rd, result);

        if (setFlags || !writesResult)
        {
            if (rd == 15 && writesResult && HasSavedStatus(Mode))
                SetProgramStatus(ReadSavedStatus());
            else
                SetNzcv(result, carry, overflow, opcode is >= 2 and <= 7 or 10 or 11);
        }
    }

    private void ExecuteMultiply(uint instruction)
    {
        bool accumulate = (instruction & (1 << 21)) != 0;
        bool setFlags = (instruction & (1 << 20)) != 0;
        int rd = (int)((instruction >> 16) & 0xF);
        int rn = (int)((instruction >> 12) & 0xF);
        int rs = (int)((instruction >> 8) & 0xF);
        int rm = (int)(instruction & 0xF);
        uint result = unchecked(ReadOperandRegister(rm) * ReadOperandRegister(rs));
        if (accumulate)
            result = unchecked(result + ReadOperandRegister(rn));
        WriteResult(rd, result);
        if (setFlags)
            SetNz(result);
        AddCycles(1);
    }

    private void ExecuteLongMultiply(uint instruction)
    {
        bool signed = (instruction & (1 << 22)) != 0;
        bool accumulate = (instruction & (1 << 21)) != 0;
        bool setFlags = (instruction & (1 << 20)) != 0;
        int rdHigh = (int)((instruction >> 16) & 0xF);
        int rdLow = (int)((instruction >> 12) & 0xF);
        int rs = (int)((instruction >> 8) & 0xF);
        int rm = (int)(instruction & 0xF);
        ulong result = signed
            ? unchecked((ulong)((long)(int)ReadOperandRegister(rm) * (long)(int)ReadOperandRegister(rs)))
            : (ulong)ReadOperandRegister(rm) * ReadOperandRegister(rs);

        if (accumulate)
            result = unchecked(result + ((ulong)_registers[rdHigh] << 32) + _registers[rdLow]);

        _registers[rdLow] = (uint)result;
        _registers[rdHigh] = (uint)(result >> 32);
        if (setFlags)
        {
            Cpsr = (Cpsr & ~(NegativeFlag | ZeroFlag)) |
                ((result & (1ul << 63)) != 0 ? NegativeFlag : 0) |
                (result == 0 ? ZeroFlag : 0);
        }
        AddCycles(2);
    }

    private void ExecuteSwap(uint instruction)
    {
        bool byteTransfer = (instruction & (1 << 22)) != 0;
        int rn = (int)((instruction >> 16) & 0xF);
        int rd = (int)((instruction >> 12) & 0xF);
        int rm = (int)(instruction & 0xF);
        uint address = ReadOperandRegister(rn);
        uint loaded;
        if (byteTransfer)
        {
            loaded = _bus.Read8(address);
            _bus.Write8(address, (byte)ReadOperandRegister(rm));
        }
        else
        {
            loaded = BitOperations.RotateRight(_bus.Read32(address), (int)(address & 3) * 8);
            _bus.Write32(address, ReadOperandRegister(rm));
        }
        WriteResult(rd, loaded);
        AddCycles(_bus.EstimateAccessCycles(address, byteTransfer ? 1 : 4) * 2);
    }

    private void ExecuteSingleTransfer(uint instruction)
    {
        bool registerOffset = (instruction & (1 << 25)) != 0;
        bool pre = (instruction & (1 << 24)) != 0;
        bool up = (instruction & (1 << 23)) != 0;
        bool byteTransfer = (instruction & (1 << 22)) != 0;
        bool writeBack = (instruction & (1 << 21)) != 0;
        bool load = (instruction & (1 << 20)) != 0;
        int rn = (int)((instruction >> 16) & 0xF);
        int rd = (int)((instruction >> 12) & 0xF);
        uint offset = registerOffset ? DecodeShiftedRegister(instruction, out _) : instruction & 0xFFF;
        uint basis = ReadOperandRegister(rn);
        uint adjusted = up ? basis + offset : basis - offset;
        uint address = pre ? adjusted : basis;

        if (load)
        {
            uint value;
            if (byteTransfer)
                value = _bus.Read8(address);
            else
            {
                uint aligned = _bus.Read32(address);
                value = BitOperations.RotateRight(aligned, (int)(address & 3) * 8);
            }
            WriteResult(rd, value);
        }
        else if (byteTransfer)
        {
            _bus.Write8(address, (byte)ReadOperandRegister(rd));
        }
        else
        {
            _bus.Write32(address, ReadOperandRegister(rd));
        }

        if (!pre || writeBack)
            WriteResult(rn, adjusted);
        AddCycles(_bus.EstimateAccessCycles(address, byteTransfer ? 1 : 4));
    }

    private void ExecuteHalfwordTransfer(uint instruction)
    {
        bool pre = (instruction & (1 << 24)) != 0;
        bool up = (instruction & (1 << 23)) != 0;
        bool immediate = (instruction & (1 << 22)) != 0;
        bool writeBack = (instruction & (1 << 21)) != 0;
        bool load = (instruction & (1 << 20)) != 0;
        int rn = (int)((instruction >> 16) & 0xF);
        int rd = (int)((instruction >> 12) & 0xF);
        int kind = (int)((instruction >> 5) & 3);
        uint offset = immediate
            ? ((instruction >> 4) & 0xF0) | (instruction & 0xF)
            : ReadOperandRegister((int)(instruction & 0xF));
        uint basis = ReadOperandRegister(rn);
        uint adjusted = up ? basis + offset : basis - offset;
        uint address = pre ? adjusted : basis;

        if (load)
        {
            uint value = kind switch
            {
                1 => _bus.Read16(address),
                2 => unchecked((uint)(int)(sbyte)_bus.Read8(address)),
                3 => unchecked((uint)(int)(short)_bus.Read16(address)),
                _ => 0,
            };
            WriteResult(rd, value);
        }
        else if (kind == 1)
        {
            _bus.Write16(address, (ushort)ReadOperandRegister(rd));
        }
        else
        {
            RaiseUndefined();
            return;
        }

        if (!pre || writeBack)
            WriteResult(rn, adjusted);
        AddCycles(_bus.EstimateAccessCycles(address, 2));
    }

    private void ExecuteBlockTransfer(uint instruction)
    {
        bool pre = (instruction & (1 << 24)) != 0;
        bool up = (instruction & (1 << 23)) != 0;
        bool writeBack = (instruction & (1 << 21)) != 0;
        bool load = (instruction & (1 << 20)) != 0;
        int rn = (int)((instruction >> 16) & 0xF);
        ushort list = (ushort)instruction;
        int count = BitOperations.PopCount(list);
        if (count == 0)
        {
            list = 1 << 15;
            count = 16;
        }

        uint basis = ReadOperandRegister(rn);
        uint address = up
            ? basis + (pre ? 4u : 0u)
            : basis - (uint)(count * 4) + (pre ? 0u : 4u);

        for (int register = 0; register < 16; register++)
        {
            if ((list & (1 << register)) == 0)
                continue;
            if (load)
                WriteResult(register, _bus.Read32(address));
            else
                _bus.Write32(address, ReadOperandRegister(register));
            address += 4;
        }

        if (writeBack)
            WriteResult(rn, up ? basis + (uint)(count * 4) : basis - (uint)(count * 4));
        AddCycles((uint)count);
    }

    private void ExecuteThumb(ushort instruction)
    {
        if ((instruction & 0xF800) == 0x1800)
        {
            int rd = instruction & 7;
            int rs = (instruction >> 3) & 7;
            bool immediate = (instruction & (1 << 10)) != 0;
            bool subtract = (instruction & (1 << 9)) != 0;
            uint left = _registers[rs];
            uint right = immediate ? (uint)((instruction >> 6) & 7) : _registers[(instruction >> 6) & 7];
            uint result = subtract ? left - right : left + right;
            _registers[rd] = result;
            SetNzcv(result, subtract ? left >= right : result < left,
                subtract ? SubOverflow(left, right, result) : AddOverflow(left, right, result), true);
            return;
        }

        if ((instruction & 0xE000) == 0)
        {
            int operation = (instruction >> 11) & 3;
            int amount = (instruction >> 6) & 0x1F;
            int rs = (instruction >> 3) & 7;
            int rd = instruction & 7;
            uint result = Shift(_registers[rs], operation, amount, true, out bool carry);
            _registers[rd] = result;
            SetNzCarry(result, carry);
            return;
        }

        if ((instruction & 0xE000) == 0x2000)
        {
            int operation = (instruction >> 11) & 3;
            int rd = (instruction >> 8) & 7;
            uint immediate = (uint)(instruction & 0xFF);
            uint left = _registers[rd];
            uint result = operation switch { 0 => immediate, 1 or 3 => left - immediate, _ => left + immediate };
            if (operation != 1)
                _registers[rd] = result;
            if (operation == 0)
                SetNz(result);
            else
                SetNzcv(result, operation is 1 or 3 ? left >= immediate : result < left,
                    operation is 1 or 3 ? SubOverflow(left, immediate, result) : AddOverflow(left, immediate, result), true);
            return;
        }

        if ((instruction & 0xFC00) == 0x4000)
        {
            ExecuteThumbAlu(instruction);
            return;
        }

        if ((instruction & 0xFC00) == 0x4400)
        {
            int operation = (instruction >> 8) & 3;
            int rd = (instruction & 7) | ((instruction >> 4) & 8);
            int rs = (instruction >> 3) & 0xF;
            uint left = ReadThumbRegister(rd);
            uint right = ReadThumbRegister(rs);
            if (operation == 3)
                BranchExchange(right);
            else if (operation == 0)
                WriteThumbRegister(rd, left + right);
            else if (operation == 1)
            {
                uint result = left - right;
                SetNzcv(result, left >= right, SubOverflow(left, right, result), true);
            }
            else
                WriteThumbRegister(rd, right);
            return;
        }

        if ((instruction & 0xF800) == 0x4800)
        {
            int rd = (instruction >> 8) & 7;
            uint address = ((_currentAddress + 4) & ~3u) + (uint)((instruction & 0xFF) << 2);
            _registers[rd] = _bus.Read32(address);
            AddCycles(_bus.EstimateAccessCycles(address, 4));
            return;
        }

        if ((instruction & 0xF000) == 0x5000)
        {
            bool signedOrHalfword = (instruction & (1 << 9)) != 0;
            int operation = (instruction >> 10) & 3;
            int ro = (instruction >> 6) & 7;
            int rb = (instruction >> 3) & 7;
            int rd = instruction & 7;
            uint address = _registers[rb] + _registers[ro];

            if (!signedOrHalfword)
            {
                switch (operation)
                {
                    case 0: _bus.Write32(address, _registers[rd]); break;
                    case 1: _bus.Write8(address, (byte)_registers[rd]); break;
                    case 2: _registers[rd] = _bus.Read32(address); break;
                    case 3: _registers[rd] = _bus.Read8(address); break;
                }
            }
            else
            {
                switch (operation)
                {
                    case 0: _bus.Write16(address, (ushort)_registers[rd]); break;
                    case 1: _registers[rd] = unchecked((uint)(int)(sbyte)_bus.Read8(address)); break;
                    case 2: _registers[rd] = _bus.Read16(address); break;
                    case 3: _registers[rd] = unchecked((uint)(int)(short)_bus.Read16(address)); break;
                }
            }
            AddCycles(_bus.EstimateAccessCycles(address, operation is 1 or 3 ? 1 : signedOrHalfword ? 2 : 4));
            return;
        }

        if ((instruction & 0xE000) == 0x6000)
        {
            bool byteTransfer = (instruction & (1 << 12)) != 0;
            bool load = (instruction & (1 << 11)) != 0;
            uint immediate = (uint)((instruction >> 6) & 0x1F) << (byteTransfer ? 0 : 2);
            int rb = (instruction >> 3) & 7;
            int rd = instruction & 7;
            uint address = _registers[rb] + immediate;
            if (load)
                _registers[rd] = byteTransfer ? _bus.Read8(address) : _bus.Read32(address);
            else if (byteTransfer)
                _bus.Write8(address, (byte)_registers[rd]);
            else
                _bus.Write32(address, _registers[rd]);
            AddCycles(_bus.EstimateAccessCycles(address, byteTransfer ? 1 : 4));
            return;
        }

        if ((instruction & 0xF000) == 0x8000)
        {
            bool load = (instruction & (1 << 11)) != 0;
            uint offset = (uint)((instruction >> 6) & 0x1F) << 1;
            int rb = (instruction >> 3) & 7;
            int rd = instruction & 7;
            uint address = _registers[rb] + offset;
            if (load) _registers[rd] = _bus.Read16(address);
            else _bus.Write16(address, (ushort)_registers[rd]);
            AddCycles(_bus.EstimateAccessCycles(address, 2));
            return;
        }

        if ((instruction & 0xF000) == 0x9000)
        {
            bool load = (instruction & (1 << 11)) != 0;
            int rd = (instruction >> 8) & 7;
            uint address = _registers[13] + (uint)((instruction & 0xFF) << 2);
            if (load) _registers[rd] = _bus.Read32(address);
            else _bus.Write32(address, _registers[rd]);
            AddCycles(_bus.EstimateAccessCycles(address, 4));
            return;
        }

        if ((instruction & 0xF000) == 0xA000)
        {
            int rd = (instruction >> 8) & 7;
            uint basis = (instruction & (1 << 11)) != 0 ? _registers[13] : (_currentAddress + 4) & ~3u;
            _registers[rd] = basis + (uint)((instruction & 0xFF) << 2);
            return;
        }

        if ((instruction & 0xFF00) == 0xB000)
        {
            uint amount = (uint)(instruction & 0x7F) << 2;
            _registers[13] = (instruction & 0x80) != 0 ? _registers[13] - amount : _registers[13] + amount;
            return;
        }

        if ((instruction & 0xF600) == 0xB400)
        {
            ExecuteThumbPushPop(instruction);
            return;
        }

        if ((instruction & 0xF000) == 0xC000)
        {
            ExecuteThumbMultiple(instruction);
            return;
        }

        if ((instruction & 0xF000) == 0xD000)
        {
            int condition = (instruction >> 8) & 0xF;
            if (condition == 0xF)
                EnterException(CpuException.SoftwareInterrupt, CpuMode.Supervisor, 0x08, _registers[15]);
            else if (condition != 0xE && ConditionPassed(condition))
            {
                int displacement = (sbyte)(instruction & 0xFF) << 1;
                _registers[15] = unchecked(_currentAddress + 4u + (uint)displacement) & ~1u;
                AddCycles(2);
            }
            else if (condition == 0xE)
                RaiseUndefined();
            return;
        }

        if ((instruction & 0xF800) == 0xE000)
        {
            int displacement = (instruction & 0x7FF) << 1;
            if ((displacement & 0x800) != 0)
                displacement |= unchecked((int)0xFFFF_F000);
            _registers[15] = unchecked(_currentAddress + 4u + (uint)displacement) & ~1u;
            AddCycles(2);
            return;
        }

        if ((instruction & 0xF800) == 0xF000)
        {
            int high = instruction & 0x7FF;
            if ((high & 0x400) != 0)
                high |= unchecked((int)0xFFFF_F800);
            _registers[14] = unchecked(_currentAddress + 4u + ((uint)high << 12));
            return;
        }

        if ((instruction & 0xF800) == 0xF800)
        {
            uint target = _registers[14] + (uint)((instruction & 0x7FF) << 1);
            _registers[14] = (_currentAddress + 2) | 1u;
            _registers[15] = target & ~1u;
            AddCycles(2);
            return;
        }

        RaiseUndefined();
    }

    private void ExecuteThumbAlu(ushort instruction)
    {
        int operation = (instruction >> 6) & 0xF;
        int rs = (instruction >> 3) & 7;
        int rd = instruction & 7;
        uint left = _registers[rd];
        uint right = _registers[rs];
        uint result;
        bool carry = IsFlagSet(CarryFlag);
        bool overflow = IsFlagSet(OverflowFlag);
        bool write = true;
        bool arithmetic = false;
        switch (operation)
        {
            case 0: result = left & right; break;
            case 1: result = left ^ right; break;
            case 2: result = Shift(left, 0, (int)(right & 0xFF), false, out carry); break;
            case 3: result = Shift(left, 1, (int)(right & 0xFF), false, out carry); break;
            case 4: result = Shift(left, 2, (int)(right & 0xFF), false, out carry); break;
            case 5:
                ulong adc = (ulong)left + right + (carry ? 1u : 0u); result = (uint)adc; carry = adc > uint.MaxValue; overflow = AddOverflow(left, right, result); arithmetic = true; break;
            case 6:
                uint borrow = carry ? 0u : 1u; result = left - right - borrow; carry = (ulong)left >= (ulong)right + borrow; overflow = SubOverflow(left, right + borrow, result); arithmetic = true; break;
            case 7: result = BitOperations.RotateRight(left, (int)(right & 31)); carry = (result & NegativeFlag) != 0; break;
            case 8: result = left & right; write = false; break;
            case 9: result = 0u - right; carry = right == 0; overflow = right == 0x8000_0000; arithmetic = true; break;
            case 10: result = left - right; carry = left >= right; overflow = SubOverflow(left, right, result); write = false; arithmetic = true; break;
            case 11: result = left + right; carry = result < left; overflow = AddOverflow(left, right, result); write = false; arithmetic = true; break;
            case 12: result = left | right; break;
            case 13: result = left * right; AddCycles(1); break;
            case 14: result = left & ~right; break;
            default: result = ~right; break;
        }
        if (write) _registers[rd] = result;
        SetNzcv(result, carry, overflow, arithmetic || operation is 2 or 3 or 4 or 7);
    }

    private void ExecuteThumbPushPop(ushort instruction)
    {
        bool load = (instruction & (1 << 11)) != 0;
        bool extra = (instruction & (1 << 8)) != 0;
        ushort list = (ushort)(instruction & 0xFF);
        int count = BitOperations.PopCount(list) + (extra ? 1 : 0);
        if (!load)
        {
            uint address = _registers[13] - (uint)(count * 4);
            uint cursor = address;
            for (int register = 0; register < 8; register++)
                if ((list & (1 << register)) != 0) { _bus.Write32(cursor, _registers[register]); cursor += 4; }
            if (extra) _bus.Write32(cursor, _registers[14]);
            _registers[13] = address;
        }
        else
        {
            uint address = _registers[13];
            for (int register = 0; register < 8; register++)
                if ((list & (1 << register)) != 0) { _registers[register] = _bus.Read32(address); address += 4; }
            if (extra) { _registers[15] = _bus.Read32(address) & ~1u; address += 4; }
            _registers[13] = address;
        }
        AddCycles((uint)count);
    }

    private void ExecuteThumbMultiple(ushort instruction)
    {
        bool load = (instruction & (1 << 11)) != 0;
        int rb = (instruction >> 8) & 7;
        ushort list = (ushort)(instruction & 0xFF);
        uint address = _registers[rb];
        for (int register = 0; register < 8; register++)
        {
            if ((list & (1 << register)) == 0) continue;
            if (load) _registers[register] = _bus.Read32(address);
            else _bus.Write32(address, _registers[register]);
            address += 4;
        }
        _registers[rb] = address;
        AddCycles((uint)Math.Max(1, BitOperations.PopCount(list)));
    }

    private uint DecodeArmOperand2(uint instruction, out bool carry)
    {
        if ((instruction & (1 << 25)) != 0)
        {
            uint immediate = instruction & 0xFF;
            int rotation = (int)((instruction >> 8) & 0xF) * 2;
            uint value = BitOperations.RotateRight(immediate, rotation);
            carry = rotation == 0 ? IsFlagSet(CarryFlag) : (value & NegativeFlag) != 0;
            return value;
        }
        return DecodeShiftedRegister(instruction, out carry);
    }

    private uint DecodeShiftedRegister(uint instruction, out bool carry)
    {
        uint value = ReadOperandRegister((int)(instruction & 0xF));
        int type = (int)((instruction >> 5) & 3);
        bool byRegister = (instruction & (1 << 4)) != 0;
        int amount = byRegister
            ? (int)(ReadOperandRegister((int)((instruction >> 8) & 0xF)) & 0xFF)
            : (int)((instruction >> 7) & 0x1F);
        return Shift(value, type, amount, !byRegister, out carry);
    }

    private uint Shift(uint value, int type, int amount, bool immediateEncoding, out bool carry)
    {
        carry = IsFlagSet(CarryFlag);
        switch (type)
        {
            case 0:
                if (amount == 0) return value;
                if (amount < 32) { carry = ((value >> (32 - amount)) & 1) != 0; return value << amount; }
                carry = amount == 32 && (value & 1) != 0; return 0;
            case 1:
                if (amount == 0 && immediateEncoding) amount = 32;
                else if (amount == 0) return value;
                if (amount < 32) { carry = ((value >> (amount - 1)) & 1) != 0; return value >> amount; }
                carry = amount == 32 && (value & NegativeFlag) != 0; return 0;
            case 2:
                if (amount == 0 && immediateEncoding) amount = 32;
                else if (amount == 0) return value;
                if (amount < 32) { carry = ((value >> (amount - 1)) & 1) != 0; return unchecked((uint)((int)value >> amount)); }
                carry = (value & NegativeFlag) != 0; return carry ? uint.MaxValue : 0;
            default:
                if (amount == 0 && immediateEncoding)
                {
                    bool oldCarry = carry;
                    carry = (value & 1) != 0;
                    return (value >> 1) | (oldCarry ? NegativeFlag : 0);
                }
                if (amount == 0) return value;
                int rotation = amount & 31;
                if (rotation == 0) { carry = (value & NegativeFlag) != 0; return value; }
                uint rotated = BitOperations.RotateRight(value, rotation);
                carry = (rotated & NegativeFlag) != 0;
                return rotated;
        }
    }

    private bool ConditionPassed(int condition)
    {
        bool n = IsFlagSet(NegativeFlag);
        bool z = IsFlagSet(ZeroFlag);
        bool c = IsFlagSet(CarryFlag);
        bool v = IsFlagSet(OverflowFlag);
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

    private void BranchExchange(uint target)
    {
        bool thumb = (target & 1) != 0;
        Cpsr = thumb ? Cpsr | ThumbFlag : Cpsr & ~ThumbFlag;
        _registers[15] = target & (thumb ? ~1u : ~3u);
    }

    private void EnterException(CpuException exception, CpuMode mode, uint vector, uint returnAddress)
    {
        uint oldStatus = Cpsr;
        SwitchMode(mode);
        _savedStatus[BankIndex(mode)] = oldStatus;
        Cpsr = (oldStatus & ~(ThumbFlag | 0x1Fu)) | (uint)mode | IrqDisableFlag;
        _registers[14] = returnAddress;
        _registers[15] = vector;
        LastException = exception;
    }

    private void RaiseUndefined() =>
        EnterException(CpuException.UndefinedInstruction, CpuMode.Undefined, 0x04, _registers[15]);

    private uint ReadOperandRegister(int index) => index == 15
        ? _currentAddress + (_currentThumb ? 4u : 8u)
        : _registers[index];

    private uint ReadThumbRegister(int index) => index == 15 ? (_currentAddress + 4) & ~1u : _registers[index];

    private void WriteThumbRegister(int index, uint value)
    {
        if (index == 15) _registers[15] = value & ~1u;
        else _registers[index] = value;
    }

    private void WriteResult(int index, uint value)
    {
        if (index == 15) _registers[15] = value & (ThumbState ? ~1u : ~3u);
        else _registers[index] = value;
    }

    private void SwitchMode(CpuMode newMode)
    {
        CpuMode oldMode = Mode;
        if (newMode == oldMode) return;
        if (oldMode == CpuMode.Fiq)
        {
            Array.Copy(_registers, 8, _fiqHighRegisters, 0, 5);
            Array.Copy(_userHighRegisters, 0, _registers, 8, 5);
        }
        else if (newMode == CpuMode.Fiq)
        {
            Array.Copy(_registers, 8, _userHighRegisters, 0, 5);
            Array.Copy(_fiqHighRegisters, 0, _registers, 8, 5);
        }

        int oldBank = BankIndex(oldMode);
        _bankedSp[oldBank] = _registers[13];
        _bankedLr[oldBank] = _registers[14];
        int newBank = BankIndex(newMode);
        _registers[13] = _bankedSp[newBank];
        _registers[14] = _bankedLr[newBank];
    }

    private uint ReadSavedStatus() => _savedStatus[BankIndex(Mode)];
    private static bool HasSavedStatus(CpuMode mode) => mode is not (CpuMode.User or CpuMode.System);

    private void WriteStatusRegister(uint value, int fieldMask, bool saved)
    {
        uint mask = 0;
        if ((fieldMask & 1) != 0) mask |= 0x0000_00FF;
        if ((fieldMask & 2) != 0) mask |= 0x0000_FF00;
        if ((fieldMask & 4) != 0) mask |= 0x00FF_0000;
        if ((fieldMask & 8) != 0) mask |= 0xFF00_0000;

        if (saved)
        {
            if (HasSavedStatus(Mode))
            {
                int bank = BankIndex(Mode);
                _savedStatus[bank] = (_savedStatus[bank] & ~mask) | (value & mask);
            }
            return;
        }

        if (Mode == CpuMode.User)
            mask &= 0xFF00_0000;
        uint status = (Cpsr & ~mask) | (value & mask);
        CpuMode targetMode = (CpuMode)(status & 0x1F);
        if (!Enum.IsDefined(targetMode))
            status = (status & ~0x1Fu) | (uint)Mode;
        SetProgramStatus(status);
    }

    private static int BankIndex(CpuMode mode) => mode switch
    {
        CpuMode.Fiq => 1,
        CpuMode.Irq => 2,
        CpuMode.Supervisor => 3,
        CpuMode.Abort => 4,
        CpuMode.Undefined => 5,
        _ => 0,
    };

    private void SetNz(uint result)
    {
        Cpsr = (Cpsr & ~(NegativeFlag | ZeroFlag)) |
            ((result & NegativeFlag) != 0 ? NegativeFlag : 0) |
            (result == 0 ? ZeroFlag : 0);
    }

    private void SetNzCarry(uint result, bool carry)
    {
        SetNz(result);
        Cpsr = carry ? Cpsr | CarryFlag : Cpsr & ~CarryFlag;
    }

    private void SetNzcv(uint result, bool carry, bool overflow, bool updateOverflow)
    {
        SetNzCarry(result, carry);
        if (updateOverflow)
            Cpsr = overflow ? Cpsr | OverflowFlag : Cpsr & ~OverflowFlag;
    }

    private bool IsFlagSet(uint flag) => (Cpsr & flag) != 0;
    private static bool AddOverflow(uint left, uint right, uint result) => ((~(left ^ right) & (left ^ result)) & NegativeFlag) != 0;
    private static bool SubOverflow(uint left, uint right, uint result) => (((left ^ right) & (left ^ result)) & NegativeFlag) != 0;

    private void AddCycles(uint cycles) => LastStepCycles += cycles;

    private void CompleteStep(uint fetchCycles)
    {
        LastStepCycles += fetchCycles;
        ClockCycles += LastStepCycles;
    }

    private static void ValidateRegister(int index)
    {
        if ((uint)index >= 16)
            throw new ArgumentOutOfRangeException(nameof(index));
    }
}
