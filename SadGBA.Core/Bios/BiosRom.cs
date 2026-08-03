namespace SadGBA.Core.Bios;

/// <summary>ROM de sistema do Game Boy Advance (16 KiB, somente leitura).</summary>
public sealed class BiosRom
{
    public const int SizeInBytes = 16 * 1024;
    private readonly byte[] _data = new byte[SizeInBytes];

    public bool IsLoaded { get; private set; }

    public void Load(ReadOnlySpan<byte> image)
    {
        if (image.Length != SizeInBytes)
            throw new ArgumentException($"A BIOS do GBA deve ter exatamente {SizeInBytes} bytes; recebidos {image.Length}.", nameof(image));

        image.CopyTo(_data);
        IsLoaded = true;
    }

    public void Load(string path) => Load(File.ReadAllBytes(path));

    public byte Read8(uint offset) => _data[offset & (SizeInBytes - 1)];
    public ushort Read16(uint offset) => (ushort)(Read8(offset & ~1u) | (Read8((offset & ~1u) + 1) << 8));
    public uint Read32(uint offset) => (uint)(Read16(offset & ~3u) | (Read16((offset & ~3u) + 2) << 16));
}

