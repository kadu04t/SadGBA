using SadGBA.Core.Bus;
using SadGBA.Core.Interrupts;
using SadGBA.Core.Ppu;
using Xunit;

namespace SadGBA.Tests.Devices;

public sealed class TimingTests
{
    [Fact]
    public void DisplayEntersHBlankAndRequestsInterrupt()
    {
        var io = new IoRegisters();
        io.Display.WriteRegister(0x004, 1 << 4);
        io.Tick(DisplayController.HDrawCycles);
        Assert.True(io.Display.InHBlank);
        Assert.NotEqual(0, io.Interrupts.RequestedSources & (ushort)InterruptSource.HBlank);
    }

    [Fact]
    public void DisplayCompletesFrameAfterTwoHundredTwentyEightLines()
    {
        var io = new IoRegisters();
        io.Tick(DisplayController.CyclesPerScanline * DisplayController.TotalScanlines);
        Assert.Equal(1ul, io.Display.FrameCount);
        Assert.Equal(0, io.Display.VerticalCount);
    }

    [Fact]
    public void TimerReloadsAndRequestsInterruptOnOverflow()
    {
        var io = new IoRegisters();
        io.Timers.WriteRegister(0x100, 0xFFFE);
        io.Timers.WriteRegister(0x102, 0x00C0);
        io.Tick(2);
        Assert.Equal(0xFFFE, io.Timers.ReadRegister(0x100));
        Assert.NotEqual(0, io.Interrupts.RequestedSources & (ushort)InterruptSource.Timer0);
    }

    [Fact]
    public void Mode3RendersRgb555PixelIntoFrameBuffer()
    {
        var bus = new GbaBus();
        bus.Write16(0x0400_0000, 3 | (1 << 10));
        bus.Write16(0x0600_0000, 0x001F);

        bus.Io.Tick(DisplayController.HDrawCycles);

        Assert.Equal(0xFFFF_0000u, bus.Io.Display.FrameBuffer.Span[0]);
    }

    [Fact]
    public void Mode4UsesPaletteAndSelectedPage()
    {
        var bus = new GbaBus();
        bus.Write16(0x0400_0000, 4 | (1 << 4) | (1 << 10));
        bus.Write16(0x0500_0002, 0x03E0);
        bus.Write16(0x0600_A000, 0x0101);

        bus.Io.Tick(DisplayController.HDrawCycles);

        Assert.Equal(0xFF00_FF00u, bus.Io.Display.FrameBuffer.Span[0]);
        Assert.Equal(0xFF00_FF00u, bus.Io.Display.FrameBuffer.Span[1]);
    }

    [Fact]
    public void Mode5Uses160By128DrawingArea()
    {
        var bus = new GbaBus();
        bus.Write16(0x0400_0000, 5 | (1 << 4) | (1 << 10));
        bus.Write16(0x0500_0000, 0x03E0);
        bus.Write16(0x0600_A000, 0x7C00);

        bus.Io.Tick(DisplayController.HDrawCycles);

        Assert.Equal(0xFF00_00FFu, bus.Io.Display.FrameBuffer.Span[0]);
        Assert.Equal(0xFF00_FF00u, bus.Io.Display.FrameBuffer.Span[200]);
    }

    [Fact]
    public void ForcedBlankProducesWhiteScanline()
    {
        var bus = new GbaBus();
        bus.Write16(0x0400_0000, 1 << 7);
        bus.Io.Tick(DisplayController.HDrawCycles);
        Assert.All(bus.Io.Display.FrameBuffer.Span[..DisplayController.ScreenWidth].ToArray(),
            pixel => Assert.Equal(0xFFFF_FFFFu, pixel));
    }

    [Fact]
    public void Mode0RendersFourBitTileBackground()
    {
        var bus = new GbaBus();
        bus.Write16(0x0400_0000, 1 << 8); // BG0 habilitado, modo 0
        bus.Write16(0x0400_0008, 8 << 8); // mapa no screen block 8
        bus.Write16(0x0500_0002, 0x001F); // palette 1 = vermelho
        bus.Write16(0x0600_0020, 0x0001); // primeiro pixel do tile 1 = índice 1
        bus.Write16(0x0600_4000, 1);      // célula 0 usa tile 1

        bus.Io.Tick(DisplayController.HDrawCycles);

        Assert.Equal(0xFFFF_0000u, bus.Io.Display.FrameBuffer.Span[0]);
        Assert.Equal(0xFF00_0000u, bus.Io.Display.FrameBuffer.Span[1]);
    }

    [Fact]
    public void Mode0HonorsHorizontalScrollAndTileFlip()
    {
        var bus = new GbaBus();
        bus.Write16(0x0400_0000, 1 << 8);
        bus.Write16(0x0400_0008, 8 << 8);
        bus.Write16(0x0400_0010, 7);      // HOFS BG0
        bus.Write16(0x0500_0002, 0x7C00); // palette 1 = azul
        bus.Write16(0x0600_0022, 0x1000); // pixel x=7 do tile 1 = índice 1
        bus.Write16(0x0600_4000, 1 | (1 << 10)); // tile com flip horizontal

        bus.Io.Tick(DisplayController.HDrawCycles);

        // Scroll 7 seleciona x=7, flip o transforma em pixel 0 (transparente).
        Assert.Equal(0xFF00_0000u, bus.Io.Display.FrameBuffer.Span[0]);
    }
}
