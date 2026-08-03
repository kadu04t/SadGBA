using SadGBA.Core.Bus;
using Xunit;

namespace SadGBA.Tests.Memory;

public sealed class BusTests
{
    [Fact]
    public void WorkRamRegionsRoundTripAndMirror()
    {
        var bus = new GbaBus();
        bus.Write32(0x0200_0010, 0x1234_5678);
        bus.Write16(0x0300_0020, 0xABCD);

        Assert.Equal(0x1234_5678u, bus.Read32(0x0204_0010));
        Assert.Equal(0xABCD, bus.Read16(0x0300_8020));
    }

    [Fact]
    public void MemoryIsLittleEndian()
    {
        var bus = new GbaBus();
        bus.Write32(0x0200_0000, 0x1234_5678);
        Assert.Equal(0x78, bus.Read8(0x0200_0000));
        Assert.Equal(0x56, bus.Read8(0x0200_0001));
        Assert.Equal(0x34, bus.Read8(0x0200_0002));
        Assert.Equal(0x12, bus.Read8(0x0200_0003));
    }

    [Fact]
    public void VramUpperMirrorMapsToObjectRegion()
    {
        var bus = new GbaBus();
        bus.Write16(0x0601_0000, 0xCAFE);
        Assert.Equal(0xCAFE, bus.Read16(0x0601_8000));
    }

    [Fact]
    public void PaletteByteWriteReplicatesAcrossHalfword()
    {
        var bus = new GbaBus();
        bus.Write8(0x0500_0001, 0x5A);
        Assert.Equal(0x5A5A, bus.Read16(0x0500_0000));
    }

    [Fact]
    public void OamIgnoresByteWrites()
    {
        var bus = new GbaBus();
        bus.Write8(0x0700_0000, 0xFF);
        Assert.Equal(0, bus.Read8(0x0700_0000));
    }

    [Fact]
    public void GamePakIsVisibleThroughAllRomWindows()
    {
        var bus = new GbaBus();
        bus.GamePak.Load([0x11, 0x22, 0x33, 0x44]);
        Assert.Equal(0x4433_2211u, bus.Read32(0x0800_0000));
        Assert.Equal(0x4433_2211u, bus.Read32(0x0A00_0000));
        Assert.Equal(0x4433_2211u, bus.Read32(0x0C00_0000));
    }
}

