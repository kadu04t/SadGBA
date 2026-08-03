using System.Text;

namespace SadGBA.Core.Cartridge;

public sealed record CartridgeHeader(
    string Title,
    string GameCode,
    string MakerCode,
    byte SoftwareVersion,
    byte Complement,
    bool HeaderChecksumValid)
{
    internal static CartridgeHeader Parse(ReadOnlySpan<byte> rom)
    {
        if (rom.Length < 0xC0)
            return new(string.Empty, string.Empty, string.Empty, 0, 0, false);

        string title = ReadAscii(rom[0xA0..0xAC]);
        string gameCode = ReadAscii(rom[0xAC..0xB0]);
        string makerCode = ReadAscii(rom[0xB0..0xB2]);
        byte checksum = 0;
        for (int index = 0xA0; index <= 0xBC; index++)
            checksum -= rom[index];
        checksum -= 0x19;

        return new(title, gameCode, makerCode, rom[0xBC], rom[0xBD], checksum == rom[0xBD]);
    }

    private static string ReadAscii(ReadOnlySpan<byte> bytes) =>
        Encoding.ASCII.GetString(bytes).TrimEnd('\0', ' ');
}

