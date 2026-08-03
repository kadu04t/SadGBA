using SadGBA.Core.Bios;
using SadGBA.Core.Cartridge;
using SadGBA.Core.Memory;
using SadGBA.Core.Ppu;

namespace SadGBA.Core.Bus;

/// <summary>Routes the Game Boy Advance 32-bit physical address space and its mirrors.</summary>
public sealed class GbaBus
{
    public BiosRom Bios { get; }
    public ExternalWorkRam ExternalRam { get; }
    public InternalWorkRam InternalRam { get; }
    public IoRegisters Io { get; }
    public GamePak GamePak { get; }
    public VideoMemory Video { get; }

    public event Action<MemoryAccess>? MemoryAccessed;

    public GbaBus()
        : this(new BiosRom(), new ExternalWorkRam(), new InternalWorkRam(), new IoRegisters(), new GamePak()) { }

    public GbaBus(BiosRom bios, ExternalWorkRam externalRam, InternalWorkRam internalRam, IoRegisters io, GamePak gamePak)
    {
        Bios = bios ?? throw new ArgumentNullException(nameof(bios));
        ExternalRam = externalRam ?? throw new ArgumentNullException(nameof(externalRam));
        InternalRam = internalRam ?? throw new ArgumentNullException(nameof(internalRam));
        Io = io ?? throw new ArgumentNullException(nameof(io));
        GamePak = gamePak ?? throw new ArgumentNullException(nameof(gamePak));
        Video = new VideoMemory();
        Io.Dma.Connect(this);
        Io.Display.ConnectVideoMemory(Video);
    }

    public byte Read8(uint address)
    {
        byte value = (address >> 24) switch
        {
            0x00 when address < BiosRom.SizeInBytes => Bios.Read8(address),
            0x02 => ExternalRam.Read8(address),
            0x03 => InternalRam.Read8(address),
            0x04 when (address & 0x00FF_FFFF) < 0x400 => Io.Read8(address),
            0x05 => Video.ReadPalette8(address),
            0x06 => Video.ReadVram8(address),
            0x07 => Video.ReadOam8(address),
            >= 0x08 and <= 0x0D => GamePak.ReadRom8(address & 0x01FF_FFFF),
            0x0E or 0x0F => GamePak.ReadSram(address),
            _ => 0,
        };
        Notify(address, MemoryAccessKind.Read, 1, value);
        return value;
    }

    public ushort Read16(uint address)
    {
        address &= ~1u;
        ushort value = (ushort)(Read8Silent(address) | (Read8Silent(address + 1) << 8));
        Notify(address, MemoryAccessKind.Read, 2, value);
        return value;
    }

    public uint Read32(uint address)
    {
        address &= ~3u;
        uint value = (uint)(Read8Silent(address) | (Read8Silent(address + 1) << 8) |
            (Read8Silent(address + 2) << 16) | (Read8Silent(address + 3) << 24));
        Notify(address, MemoryAccessKind.Read, 4, value);
        return value;
    }

    public ushort ReadInstruction16(uint address)
    {
        ushort value = Read16(address);
        Notify(address, MemoryAccessKind.InstructionFetch, 2, value);
        return value;
    }

    public uint ReadInstruction32(uint address)
    {
        uint value = Read32(address);
        Notify(address, MemoryAccessKind.InstructionFetch, 4, value);
        return value;
    }

    public void Write8(uint address, byte value)
    {
        switch (address >> 24)
        {
            case 0x02: ExternalRam.Write8(address, value); break;
            case 0x03: InternalRam.Write8(address, value); break;
            case 0x04 when (address & 0x00FF_FFFF) < 0x400: Io.Write8(address, value); break;
            case 0x05:
                Video.WritePalette8(address, value);
                break;
            case 0x06:
                Video.WriteVram8(address, value);
                break;
            case 0x07: Video.WriteOam8(address, value); break;
            case 0x0E:
            case 0x0F: GamePak.WriteSram(address, value); break;
        }
        Notify(address, MemoryAccessKind.Write, 1, value);
    }

    public void Write16(uint address, ushort value)
    {
        address &= ~1u;
        if ((address >> 24) == 0x04)
            Io.Write16(address, value);
        else if ((address >> 24) == 0x07)
            Video.WriteOam16(address, value);
        else if ((address >> 24) == 0x06)
            Video.WriteVram16(address, value);
        else if ((address >> 24) == 0x05)
            Video.WritePalette16(address, value);
        else
        {
            Write8Silent(address, (byte)value);
            Write8Silent(address + 1, (byte)(value >> 8));
        }
        Notify(address, MemoryAccessKind.Write, 2, value);
    }

    public void Write32(uint address, uint value)
    {
        address &= ~3u;
        if ((address >> 24) == 0x04)
            Io.Write32(address, value);
        else
        {
            Write16Silent(address, (ushort)value);
            Write16Silent(address + 2, (ushort)(value >> 16));
        }
        Notify(address, MemoryAccessKind.Write, 4, value);
    }

    public uint EstimateAccessCycles(uint address, int width)
    {
        return (address >> 24) switch
        {
            0x02 => width == 4 ? 6u : 3u,
            0x08 or 0x0A or 0x0C => width == 4 ? 8u : 5u,
            0x09 or 0x0B or 0x0D => width == 4 ? 6u : 3u,
            0x0E or 0x0F => 5u,
            _ => 1u,
        };
    }

    public void Reset()
    {
        ExternalRam.Clear();
        InternalRam.Clear();
        Video.Clear();
        Io.Reset();
    }

    private byte Read8Silent(uint address) => (address >> 24) switch
    {
        0x00 when address < BiosRom.SizeInBytes => Bios.Read8(address),
        0x02 => ExternalRam.Read8(address),
        0x03 => InternalRam.Read8(address),
        0x04 when (address & 0x00FF_FFFF) < 0x400 => Io.Read8(address),
        0x05 => Video.ReadPalette8(address),
        0x06 => Video.ReadVram8(address),
        0x07 => Video.ReadOam8(address),
        >= 0x08 and <= 0x0D => GamePak.ReadRom8(address & 0x01FF_FFFF),
        0x0E or 0x0F => GamePak.ReadSram(address),
        _ => 0,
    };

    private void Write8Silent(uint address, byte value)
    {
        switch (address >> 24)
        {
            case 0x02: ExternalRam.Write8(address, value); break;
            case 0x03: InternalRam.Write8(address, value); break;
            case 0x04: Io.Write8(address, value); break;
            case 0x05: Video.WritePalette8(address, value); break;
            case 0x06: Video.WriteVram8(address, value); break;
            case 0x07: Video.WriteOam8(address, value); break;
            case 0x0E:
            case 0x0F: GamePak.WriteSram(address, value); break;
        }
    }

    private void Write16Silent(uint address, ushort value)
    {
        if ((address >> 24) == 0x05)
        {
            Video.WritePalette16(address, value);
            return;
        }
        if ((address >> 24) == 0x06)
        {
            Video.WriteVram16(address, value);
            return;
        }
        if ((address >> 24) == 0x07)
        {
            Video.WriteOam16(address, value);
            return;
        }
        Write8Silent(address, (byte)value);
        Write8Silent(address + 1, (byte)(value >> 8));
    }

    private void Notify(uint address, MemoryAccessKind kind, int width, uint value) =>
        MemoryAccessed?.Invoke(new(address, kind, width, value));
}
