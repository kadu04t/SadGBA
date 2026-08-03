namespace SadGBA.Core.Memory;

/// <summary>WRAM interna de 32 KiB, mapeada em 0x03000000.</summary>
public sealed class InternalWorkRam : ByteMemory
{
    public const int SizeInBytes = 32 * 1024;
    public InternalWorkRam() : base(SizeInBytes) { }
}

