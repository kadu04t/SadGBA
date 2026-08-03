namespace SadGBA.Core.Memory;

/// <summary>32 KiB internal WRAM mapped at 0x03000000.</summary>
public sealed class InternalWorkRam : ByteMemory
{
    public const int SizeInBytes = 32 * 1024;
    public InternalWorkRam() : base(SizeInBytes) { }
}
