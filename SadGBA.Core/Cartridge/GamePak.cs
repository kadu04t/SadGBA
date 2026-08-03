namespace SadGBA.Core.Cartridge;

/// <summary>Cartucho Game Pak. ROM é somente leitura; SRAM persiste separadamente.</summary>
public sealed class GamePak
{
    public const int MaximumRomSize = 32 * 1024 * 1024;
    public const int SramSize = 64 * 1024;

    private byte[] _rom = [];
    private readonly byte[] _sram = new byte[SramSize];

    public bool IsLoaded => _rom.Length != 0;
    public int RomSize => _rom.Length;
    public CartridgeHeader Header { get; private set; } = CartridgeHeader.Parse([]);

    public void Load(ReadOnlySpan<byte> image)
    {
        if (image.IsEmpty)
            throw new ArgumentException("A ROM não pode estar vazia.", nameof(image));
        if (image.Length > MaximumRomSize)
            throw new ArgumentException($"A ROM excede o limite de {MaximumRomSize} bytes do GBA.", nameof(image));

        _rom = image.ToArray();
        Header = CartridgeHeader.Parse(_rom);
    }

    public void Load(string path) => Load(File.ReadAllBytes(path));

    public byte ReadRom8(uint offset) => _rom.Length == 0 ? (byte)0xFF : _rom[offset % (uint)_rom.Length];
    public ushort ReadRom16(uint offset) => (ushort)(ReadRom8(offset & ~1u) | (ReadRom8((offset & ~1u) + 1) << 8));
    public uint ReadRom32(uint offset) => (uint)(ReadRom16(offset & ~3u) | (ReadRom16((offset & ~3u) + 2) << 16));

    public byte ReadSram(uint offset) => _sram[offset & (SramSize - 1)];
    public void WriteSram(uint offset, byte value) => _sram[offset & (SramSize - 1)] = value;

    public byte[] ExportSave() => (byte[])_sram.Clone();

    public void ImportSave(ReadOnlySpan<byte> save)
    {
        if (save.Length != SramSize)
            throw new ArgumentException($"O save SRAM deve ter {SramSize} bytes.", nameof(save));
        save.CopyTo(_sram);
    }
}
