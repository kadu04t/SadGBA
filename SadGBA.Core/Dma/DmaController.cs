using SadGBA.Core.Bus;
using SadGBA.Core.Interrupts;

namespace SadGBA.Core.Dma;

/// <summary>
/// Models the four GBA DMA channels. Immediate transfers run synchronously;
/// HBlank, VBlank, and FIFO events can trigger deferred channels.
/// </summary>
public sealed class DmaController
{
    private readonly DmaChannel[] _channels = [new(), new(), new(), new()];
    private readonly InterruptController _interrupts;
    private GbaBus? _bus;

    public DmaController(InterruptController interrupts) =>
        _interrupts = interrupts ?? throw new ArgumentNullException(nameof(interrupts));

    internal void Connect(GbaBus bus) => _bus = bus;

    public ushort ReadRegister(uint offset)
    {
        if (!TryDecode(offset, out DmaChannel channel, out uint registerOffset))
            return 0;
        return registerOffset switch
        {
            0 => (ushort)channel.Source,
            2 => (ushort)(channel.Source >> 16),
            4 => (ushort)channel.Destination,
            6 => (ushort)(channel.Destination >> 16),
            8 => channel.WordCount,
            10 => channel.Control,
            _ => 0,
        };
    }

    public void WriteRegister(uint offset, ushort value)
    {
        if (!TryDecode(offset, out DmaChannel channel, out uint registerOffset))
            return;

        switch (registerOffset)
        {
            case 0: channel.Source = (channel.Source & 0xFFFF_0000) | value; break;
            case 2: channel.Source = (channel.Source & 0x0000_FFFF) | ((uint)value << 16); break;
            case 4: channel.Destination = (channel.Destination & 0xFFFF_0000) | value; break;
            case 6: channel.Destination = (channel.Destination & 0x0000_FFFF) | ((uint)value << 16); break;
            case 8: channel.WordCount = value; break;
            case 10:
                bool wasEnabled = channel.Enabled;
                channel.Control = value;
                if (!wasEnabled && channel.Enabled)
                {
                    channel.Latch();
                    if (channel.StartTiming == 0)
                        Execute(channel);
                }
                break;
        }
    }

    /// <summary>Triggers channels configured for the given timing (1=VBlank, 2=HBlank, 3=special).</summary>
    public void Trigger(int startTiming)
    {
        foreach (DmaChannel channel in _channels)
            if (channel.Enabled && channel.StartTiming == startTiming)
                Execute(channel);
    }

    public void Reset()
    {
        foreach (DmaChannel channel in _channels)
            channel.Reset();
    }

    private void Execute(DmaChannel channel)
    {
        GbaBus bus = _bus ?? throw new InvalidOperationException("O DMA ainda não foi conectado ao barramento.");
        int channelIndex = Array.IndexOf(_channels, channel);
        uint count = channel.WordCount;
        if (count == 0)
            count = channelIndex == 3 ? 0x1_0000u : 0x4000u;

        int width = channel.Transfer32Bit ? 4 : 2;
        uint source = channel.CurrentSource;
        uint destination = channel.CurrentDestination;
        uint originalDestination = destination;

        for (uint index = 0; index < count; index++)
        {
            if (width == 4)
                bus.Write32(destination, bus.Read32(source));
            else
                bus.Write16(destination, bus.Read16(source));
            source = Adjust(source, channel.SourceControl, width, destination: false);
            destination = Adjust(destination, channel.DestinationControl, width, destination: true);
        }

        channel.CurrentSource = source;
        channel.CurrentDestination = channel.DestinationControl == 3 ? originalDestination : destination;
        if (channel.IrqEnabled)
            _interrupts.Request((InterruptSource)((ushort)InterruptSource.Dma0 << channelIndex));

        // Repeat only keeps channels enabled for non-immediate triggers.
        if (!channel.Repeat || channel.StartTiming == 0)
            channel.Control &= 0x7FFF;
    }

    private static uint Adjust(uint address, int control, int width, bool destination) => control switch
    {
        0 => address + (uint)width,
        1 => address - (uint)width,
        2 => address,
        3 when destination => address + (uint)width,
        _ => address,
    };

    private bool TryDecode(uint offset, out DmaChannel channel, out uint registerOffset)
    {
        if (offset is < 0x0B0 or > 0x0DE)
        {
            channel = null!;
            registerOffset = 0;
            return false;
        }
        int index = (int)((offset - 0x0B0) / 12);
        if ((uint)index >= 4)
        {
            channel = null!;
            registerOffset = 0;
            return false;
        }
        channel = _channels[index];
        registerOffset = (offset - 0x0B0) % 12;
        return true;
    }

    private sealed class DmaChannel
    {
        public uint Source;
        public uint Destination;
        public ushort WordCount;
        public ushort Control;
        public uint CurrentSource;
        public uint CurrentDestination;
        public bool Enabled => (Control & 0x8000) != 0;
        public bool IrqEnabled => (Control & 0x4000) != 0;
        public int StartTiming => (Control >> 12) & 3;
        public bool Transfer32Bit => (Control & (1 << 10)) != 0;
        public bool Repeat => (Control & (1 << 9)) != 0;
        public int SourceControl => (Control >> 7) & 3;
        public int DestinationControl => (Control >> 5) & 3;

        public void Latch()
        {
            CurrentSource = Source;
            CurrentDestination = Destination;
        }

        public void Reset()
        {
            Source = Destination = CurrentSource = CurrentDestination = 0;
            WordCount = Control = 0;
        }
    }
}
