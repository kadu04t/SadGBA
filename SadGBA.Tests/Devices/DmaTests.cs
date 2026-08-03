using SadGBA.Core.Bus;
using SadGBA.Core.Interrupts;
using Xunit;

namespace SadGBA.Tests.Devices;

public sealed class DmaTests
{
    [Fact]
    public void ImmediateDmaCopiesWordsAndRaisesInterrupt()
    {
        var bus = new GbaBus();
        bus.Write32(0x0200_0000, 0x1122_3344);
        bus.Write32(0x0200_0004, 0x5566_7788);

        bus.Write32(0x0400_00B0, 0x0200_0000); // DMA0SAD
        bus.Write32(0x0400_00B4, 0x0300_0000); // DMA0DAD
        bus.Write16(0x0400_00B8, 2);
        bus.Write16(0x0400_00BA, 0xC400); // enable, IRQ, 32 bit, immediate

        Assert.Equal(0x1122_3344u, bus.Read32(0x0300_0000));
        Assert.Equal(0x5566_7788u, bus.Read32(0x0300_0004));
        Assert.NotEqual(0, bus.Io.Interrupts.RequestedSources & (ushort)InterruptSource.Dma0);
        Assert.Equal(0, bus.Io.Dma.ReadRegister(0x0BA) & 0x8000);
    }
}

