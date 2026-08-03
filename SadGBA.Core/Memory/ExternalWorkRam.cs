namespace SadGBA.Core.Memory;

/// <summary>WRAM externa de 256 KiB, mapeada em 0x02000000.</summary>
public sealed class ExternalWorkRam : ByteMemory
{
    public const int SizeInBytes = 256 * 1024;
    public ExternalWorkRam() : base(SizeInBytes) { }
}

