using SadGBA.Core.Memory;

namespace SadGBA.Core.Bus;

/// <summary>
/// Primeiro esqueleto do barramento do GBA, contendo apenas as duas regiões
/// de RAM usadas pela CPU.
/// </summary>
public sealed class GbaBus
{
    public ExternalWorkRam ExternalRam { get; } = new();
    public InternalWorkRam InternalRam { get; } = new();

    public byte Read8(uint address) => (address >> 24) switch
    {
        0x02 => ExternalRam.Read8(address),
        0x03 => InternalRam.Read8(address),
        _ => 0,
    };

    public ushort Read16(uint address)
    {
        address &= ~1u;
        return (ushort)(Read8(address) | (Read8(address + 1) << 8));
    }

    public uint Read32(uint address)
    {
        address &= ~3u;
        return (uint)(Read8(address) |
            (Read8(address + 1) << 8) |
            (Read8(address + 2) << 16) |
            (Read8(address + 3) << 24));
    }

    public ushort ReadInstruction16(uint address) => Read16(address);
    public uint ReadInstruction32(uint address) => Read32(address);

    public void Write8(uint address, byte value)
    {
        switch (address >> 24)
        {
            case 0x02: ExternalRam.Write8(address, value); break;
            case 0x03: InternalRam.Write8(address, value); break;
        }
    }

    public void Write16(uint address, ushort value)
    {
        address &= ~1u;
        Write8(address, (byte)value);
        Write8(address + 1, (byte)(value >> 8));
    }

    public void Write32(uint address, uint value)
    {
        address &= ~3u;
        Write16(address, (ushort)value);
        Write16(address + 2, (ushort)(value >> 16));
    }

    public void Reset()
    {
        ExternalRam.Clear();
        InternalRam.Clear();
    }
}

