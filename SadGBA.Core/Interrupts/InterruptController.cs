namespace SadGBA.Core.Interrupts;

/// <summary>Controla IE, IF e IME e produz a linha IRQ combinada da CPU.</summary>
public sealed class InterruptController
{
    private const ushort ValidMask = 0x3FFF;

    public ushort EnabledSources { get; private set; }
    public ushort RequestedSources { get; private set; }
    public bool MasterEnable { get; private set; }
    public bool IrqPending => MasterEnable && (EnabledSources & RequestedSources) != 0;
    public bool WakePending => (EnabledSources & RequestedSources) != 0;

    public void Request(InterruptSource source) => RequestedSources |= (ushort)((ushort)source & ValidMask);

    public ushort ReadRegister(uint offset) => offset switch
    {
        0x200 => EnabledSources,
        0x202 => RequestedSources,
        0x208 => MasterEnable ? (ushort)1 : (ushort)0,
        _ => 0,
    };

    public void WriteRegister(uint offset, ushort value)
    {
        switch (offset)
        {
            case 0x200: EnabledSources = (ushort)(value & ValidMask); break;
            case 0x202: RequestedSources &= (ushort)~(value & ValidMask); break;
            case 0x208: MasterEnable = (value & 1) != 0; break;
        }
    }

    public void Reset()
    {
        EnabledSources = 0;
        RequestedSources = 0;
        MasterEnable = false;
    }
}
