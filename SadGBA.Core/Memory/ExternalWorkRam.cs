namespace SadGBA.Core.Memory;

/// <summary>256 KiB external WRAM mapped at 0x02000000.</summary>
public sealed class ExternalWorkRam : ByteMemory
{
    public const int SizeInBytes = 256 * 1024;
    public ExternalWorkRam() : base(SizeInBytes) { }
}
