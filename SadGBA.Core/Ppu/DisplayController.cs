using SadGBA.Core.Interrupts;

namespace SadGBA.Core.Ppu;

/// <summary>
/// Provides deterministic GBA LCD timing and scanline rendering for text and
/// bitmap backgrounds.
/// </summary>
public sealed class DisplayController
{
    public const int ScreenWidth = 240;
    public const int ScreenHeight = 160;
    public const int CyclesPerScanline = 1232;
    public const int HDrawCycles = 960;
    public const int TotalScanlines = 228;

    private readonly InterruptController _interrupts;
    private readonly uint[] _frameBuffer = new uint[ScreenWidth * ScreenHeight];
    private readonly ushort[] _backgroundControl = new ushort[4];
    private readonly ushort[] _backgroundHorizontalOffset = new ushort[4];
    private readonly ushort[] _backgroundVerticalOffset = new ushort[4];
    private VideoMemory? _video;
    private int _lineCycles;

    public ushort DisplayControl { get; private set; }
    public ushort DisplayStatusControl { get; private set; }
    public ushort VerticalCount { get; private set; }
    public ulong FrameCount { get; private set; }
    public bool InHBlank => _lineCycles >= HDrawCycles;
    public bool InVBlank => VerticalCount >= ScreenHeight;
    public ReadOnlyMemory<uint> FrameBuffer => _frameBuffer;

    public event Action? FrameCompleted;
    public event Action? HBlankStarted;
    public event Action? VBlankStarted;

    public DisplayController(InterruptController interrupts) =>
        _interrupts = interrupts ?? throw new ArgumentNullException(nameof(interrupts));

    internal void ConnectVideoMemory(VideoMemory video) =>
        _video = video ?? throw new ArgumentNullException(nameof(video));

    public ushort ReadRegister(uint offset) => offset switch
    {
        0x000 => DisplayControl,
        0x004 => (ushort)((DisplayStatusControl & 0xFFF8) |
            (InVBlank ? 1 : 0) | (InHBlank ? 2 : 0) | (VCountMatches ? 4 : 0)),
        0x006 => VerticalCount,
        >= 0x008 and <= 0x00E => _backgroundControl[(offset - 0x008) / 2],
        >= 0x010 and <= 0x01E when (offset & 2) == 0 => _backgroundHorizontalOffset[(offset - 0x010) / 4],
        >= 0x010 and <= 0x01E => _backgroundVerticalOffset[(offset - 0x010) / 4],
        _ => 0,
    };

    public void WriteRegister(uint offset, ushort value)
    {
        switch (offset)
        {
            case 0x000: DisplayControl = value; break;
            case 0x004: DisplayStatusControl = (ushort)(value & 0xFFF8); break;
            case >= 0x008 and <= 0x00E:
                _backgroundControl[(offset - 0x008) / 2] = value;
                break;
            case >= 0x010 and <= 0x01E when (offset & 2) == 0:
                _backgroundHorizontalOffset[(offset - 0x010) / 4] = (ushort)(value & 0x01FF);
                break;
            case >= 0x010 and <= 0x01E:
                _backgroundVerticalOffset[(offset - 0x010) / 4] = (ushort)(value & 0x01FF);
                break;
        }
    }

    public void Tick(uint cycles)
    {
        while (cycles > 0)
        {
            int boundary = InHBlank ? CyclesPerScanline : HDrawCycles;
            uint advance = Math.Min(cycles, (uint)(boundary - _lineCycles));
            _lineCycles += (int)advance;
            cycles -= advance;

            if (_lineCycles == HDrawCycles)
            {
                if (VerticalCount < ScreenHeight)
                    RenderScanline(VerticalCount);
                HBlankStarted?.Invoke();
                if ((DisplayStatusControl & (1 << 4)) != 0)
                    _interrupts.Request(InterruptSource.HBlank);
            }
            else if (_lineCycles == CyclesPerScanline)
            {
                _lineCycles = 0;
                VerticalCount++;
                if (VerticalCount == ScreenHeight)
                {
                    VBlankStarted?.Invoke();
                    if ((DisplayStatusControl & (1 << 3)) != 0)
                        _interrupts.Request(InterruptSource.VBlank);
                }

                if (VerticalCount >= TotalScanlines)
                {
                    VerticalCount = 0;
                    FrameCount++;
                    FrameCompleted?.Invoke();
                }

                if (VCountMatches && (DisplayStatusControl & (1 << 5)) != 0)
                    _interrupts.Request(InterruptSource.VCounter);
            }
        }
    }

    public void Reset()
    {
        DisplayControl = 0x0080;
        DisplayStatusControl = 0;
        VerticalCount = 0;
        FrameCount = 0;
        _lineCycles = 0;
        Array.Clear(_backgroundControl);
        Array.Clear(_backgroundHorizontalOffset);
        Array.Clear(_backgroundVerticalOffset);
        Array.Fill(_frameBuffer, 0xFFFF_FFFF);
    }

    private bool VCountMatches => VerticalCount == ((DisplayStatusControl >> 8) & 0xFF);

    private void RenderScanline(int line)
    {
        Span<uint> output = _frameBuffer.AsSpan(line * ScreenWidth, ScreenWidth);
        if ((DisplayControl & (1 << 7)) != 0)
        {
            output.Fill(0xFFFF_FFFF);
            return;
        }

        VideoMemory? video = _video;
        if (video is null)
        {
            output.Clear();
            return;
        }

        uint backdrop = ExpandColor(video.ReadPalette16(0));
        int mode = DisplayControl & 7;
        if (mode is 1 or 2 or > 5)
        {
            output.Fill(backdrop);
            return;
        }

        switch (mode)
        {
            case 0: RenderMode0(video, output, line, backdrop); break;
            case 3 when (DisplayControl & (1 << 10)) != 0: RenderMode3(video, output, line); break;
            case 4 when (DisplayControl & (1 << 10)) != 0: RenderMode4(video, output, line); break;
            case 5 when (DisplayControl & (1 << 10)) != 0: RenderMode5(video, output, line, backdrop); break;
            default: output.Fill(backdrop); break;
        }
    }

    private static void RenderMode3(VideoMemory video, Span<uint> output, int line)
    {
        uint row = (uint)(line * ScreenWidth * 2);
        for (int x = 0; x < ScreenWidth; x++)
            output[x] = ExpandColor(video.ReadVram16(row + (uint)(x * 2)));
    }

    private void RenderMode0(VideoMemory video, Span<uint> output, int line, uint backdrop)
    {
        output.Fill(backdrop);
        for (int priority = 3; priority >= 0; priority--)
        {
            for (int background = 3; background >= 0; background--)
            {
                if ((DisplayControl & (1 << (8 + background))) == 0 ||
                    (_backgroundControl[background] & 3) != priority)
                {
                    continue;
                }

                RenderTextBackground(video, output, line, background);
            }
        }
    }

    private void RenderTextBackground(VideoMemory video, Span<uint> output, int line, int background)
    {
        ushort control = _backgroundControl[background];
        bool color256 = (control & (1 << 7)) != 0;
        int size = control >> 14;
        int width = size is 1 or 3 ? 512 : 256;
        int height = size is 2 or 3 ? 512 : 256;
        int sourceY = (line + _backgroundVerticalOffset[background]) & (height - 1);
        int tileY = sourceY >> 3;
        int pixelY = sourceY & 7;
        uint characterBase = (uint)((control >> 2) & 3) * 0x4000;
        uint screenBase = (uint)((control >> 8) & 0x1F) * 0x800;

        for (int x = 0; x < ScreenWidth; x++)
        {
            int sourceX = (x + _backgroundHorizontalOffset[background]) & (width - 1);
            int tileX = sourceX >> 3;
            int pixelX = sourceX & 7;
            int screenBlock = size switch
            {
                1 => tileX >> 5,
                2 => tileY >> 5,
                3 => ((tileY >> 5) * 2) + (tileX >> 5),
                _ => 0,
            };
            uint mapOffset = screenBase + (uint)(screenBlock * 0x800) +
                (uint)((((tileY & 31) * 32) + (tileX & 31)) * 2);
            ushort entry = video.ReadVram16(mapOffset);
            int tileNumber = entry & 0x03FF;
            int tilePixelX = (entry & (1 << 10)) != 0 ? 7 - pixelX : pixelX;
            int tilePixelY = (entry & (1 << 11)) != 0 ? 7 - pixelY : pixelY;

            int paletteIndex;
            if (color256)
            {
                uint tileAddress = characterBase + (uint)(tileNumber * 64 + tilePixelY * 8 + tilePixelX);
                paletteIndex = video.ReadVram8(tileAddress);
            }
            else
            {
                uint tileAddress = characterBase + (uint)(tileNumber * 32 + tilePixelY * 4 + (tilePixelX >> 1));
                byte packed = video.ReadVram8(tileAddress);
                int color = (packed >> ((tilePixelX & 1) * 4)) & 0xF;
                paletteIndex = (((entry >> 12) & 0xF) * 16) + color;
            }

            // Palette index zero is transparent on text backgrounds.
            if ((paletteIndex & (color256 ? 0xFF : 0xF)) != 0)
                output[x] = ExpandColor(video.ReadPalette16((uint)paletteIndex * 2));
        }
    }

    private void RenderMode4(VideoMemory video, Span<uint> output, int line)
    {
        uint page = (DisplayControl & (1 << 4)) != 0 ? 0xA000u : 0;
        uint row = page + (uint)(line * ScreenWidth);
        for (int x = 0; x < ScreenWidth; x++)
        {
            byte paletteIndex = video.ReadVram8(row + (uint)x);
            output[x] = ExpandColor(video.ReadPalette16((uint)paletteIndex * 2));
        }
    }

    private void RenderMode5(VideoMemory video, Span<uint> output, int line, uint backdrop)
    {
        const int mode5Width = 160;
        const int mode5Height = 128;
        output.Fill(backdrop);
        if (line >= mode5Height)
            return;

        uint page = (DisplayControl & (1 << 4)) != 0 ? 0xA000u : 0;
        uint row = page + (uint)(line * mode5Width * 2);
        for (int x = 0; x < mode5Width; x++)
            output[x] = ExpandColor(video.ReadVram16(row + (uint)(x * 2)));
    }

    private static uint ExpandColor(ushort color)
    {
        uint red5 = (uint)(color & 0x1F);
        uint green5 = (uint)((color >> 5) & 0x1F);
        uint blue5 = (uint)((color >> 10) & 0x1F);
        uint red = (red5 << 3) | (red5 >> 2);
        uint green = (green5 << 3) | (green5 >> 2);
        uint blue = (blue5 << 3) | (blue5 >> 2);
        return 0xFF00_0000 | (red << 16) | (green << 8) | blue;
    }
}
