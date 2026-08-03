namespace SadGBA.Core.Ppu;

/// <summary>Palette RAM, VRAM e OAM com os espelhos e larguras do hardware.</summary>
public sealed class VideoMemory
{
    public const int PaletteSize = 1024;
    public const int VramSize = 96 * 1024;
    public const int OamSize = 1024;

    private readonly byte[] _palette = new byte[PaletteSize];
    private readonly byte[] _vram = new byte[VramSize];
    private readonly byte[] _oam = new byte[OamSize];

    public byte ReadPalette8(uint address) => _palette[address & 0x3FF];
    public ushort ReadPalette16(uint address) => Read16(_palette, address & 0x3FE);
    public byte ReadVram8(uint address) => _vram[MapVram(address)];
    public ushort ReadVram16(uint address) => Read16(_vram, (uint)MapVram(address & ~1u));
    public byte ReadOam8(uint address) => _oam[address & 0x3FF];

    public void WritePalette8(uint address, byte value)
    {
        uint offset = address & 0x3FE;
        _palette[offset] = value;
        _palette[offset + 1] = value;
    }

    public void WritePalette16(uint address, ushort value) =>
        Write16(_palette, address & 0x3FE, value);

    public void WriteVram8(uint address, byte value)
    {
        int offset = MapVram(address & ~1u);
        _vram[offset] = value;
        _vram[offset + 1] = value;
    }

    public void WriteVram16(uint address, ushort value) =>
        Write16(_vram, (uint)MapVram(address & ~1u), value);

    /// <summary>OAM ignora escritas de byte feitas pela CPU.</summary>
    public void WriteOam8(uint address, byte value)
    {
        _ = address;
        _ = value;
    }

    public void WriteOam16(uint address, ushort value) =>
        Write16(_oam, address & 0x3FE, value);

    public void Clear()
    {
        Array.Clear(_palette);
        Array.Clear(_vram);
        Array.Clear(_oam);
    }

    private static ushort Read16(byte[] memory, uint offset) =>
        (ushort)(memory[offset] | (memory[offset + 1] << 8));

    private static void Write16(byte[] memory, uint offset, ushort value)
    {
        memory[offset] = (byte)value;
        memory[offset + 1] = (byte)(value >> 8);
    }

    private static int MapVram(uint address)
    {
        uint offset = address & 0x1FFFF;
        if (offset >= 0x18000)
            offset -= 0x8000;
        return (int)offset;
    }
}

