using System.Text;
using SadGBA.Core.Cartridge;
using Xunit;

namespace SadGBA.Tests.Cartridge;

public sealed class GamePakTests
{
    [Fact]
    public void ParsesAndValidatesHeader()
    {
        var rom = new byte[0xC0];
        Encoding.ASCII.GetBytes("SAD ADVENTUR").CopyTo(rom, 0xA0);
        Encoding.ASCII.GetBytes("ASDE").CopyTo(rom, 0xAC);
        Encoding.ASCII.GetBytes("01").CopyTo(rom, 0xB0);
        rom[0xB2] = 0x96;
        byte checksum = 0;
        for (int index = 0xA0; index <= 0xBC; index++) checksum -= rom[index];
        rom[0xBD] = (byte)(checksum - 0x19);

        var pak = new GamePak();
        pak.Load(rom);

        Assert.Equal("SAD ADVENTUR", pak.Header.Title);
        Assert.Equal("ASDE", pak.Header.GameCode);
        Assert.True(pak.Header.HeaderChecksumValid);
    }

    [Fact]
    public void SramCanBeExportedAndImported()
    {
        var first = new GamePak();
        first.WriteSram(42, 0xCC);
        var second = new GamePak();
        second.ImportSave(first.ExportSave());
        Assert.Equal(0xCC, second.ReadSram(42));
    }
}

