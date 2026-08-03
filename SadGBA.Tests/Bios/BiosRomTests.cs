using SadGBA.Core.Bios;
using Xunit;

namespace SadGBA.Tests.Bios;

public sealed class BiosRomTests
{
    [Fact]
    public void RequiresExactlySixteenKibibytes()
    {
        var bios = new BiosRom();
        Assert.Throws<ArgumentException>(() => bios.Load(new byte[1]));
    }

    [Fact]
    public void LoadedImageCanBeReadLittleEndian()
    {
        var image = new byte[BiosRom.SizeInBytes];
        image[0] = 0x78; image[1] = 0x56; image[2] = 0x34; image[3] = 0x12;
        var bios = new BiosRom();
        bios.Load(image);
        Assert.True(bios.IsLoaded);
        Assert.Equal(0x1234_5678u, bios.Read32(0));
    }
}

