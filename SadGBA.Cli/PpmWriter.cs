using System.Text;

namespace SadGBA.Cli;

internal static class PpmWriter
{
    public static void Write(string path, ReadOnlySpan<uint> pixels, int width, int height)
    {
        if (pixels.Length != checked(width * height))
            throw new ArgumentException("O tamanho do framebuffer não corresponde às dimensões informadas.", nameof(pixels));

        using FileStream stream = File.Create(path);
        byte[] header = Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n");
        stream.Write(header);

        byte[] row = new byte[width * 3];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                uint color = pixels[(y * width) + x];
                row[(x * 3) + 0] = (byte)(color >> 16);
                row[(x * 3) + 1] = (byte)(color >> 8);
                row[(x * 3) + 2] = (byte)color;
            }
            stream.Write(row);
        }
    }
}

