using System.Runtime.CompilerServices;

namespace SadGBA.Core.Memory;

/// <summary>Little-endian memory block with mask-based mirroring.</summary>
public abstract class ByteMemory
{
    private readonly byte[] _data;
    private readonly uint _mask;

    protected ByteMemory(int sizeInBytes)
    {
        if (sizeInBytes <= 0 || (sizeInBytes & (sizeInBytes - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(sizeInBytes), "O tamanho deve ser uma potência de dois.");

        _data = new byte[sizeInBytes];
        _mask = (uint)sizeInBytes - 1;
    }

    public int Length => _data.Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte Read8(uint offset) => _data[offset & _mask];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort Read16(uint offset)
    {
        offset &= ~1u;
        return (ushort)(Read8(offset) | (Read8(offset + 1) << 8));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint Read32(uint offset)
    {
        offset &= ~3u;
        return (uint)(Read8(offset) | (Read8(offset + 1) << 8) |
            (Read8(offset + 2) << 16) | (Read8(offset + 3) << 24));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write8(uint offset, byte value) => _data[offset & _mask] = value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write16(uint offset, ushort value)
    {
        offset &= ~1u;
        Write8(offset, (byte)value);
        Write8(offset + 1, (byte)(value >> 8));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write32(uint offset, uint value)
    {
        offset &= ~3u;
        Write8(offset, (byte)value);
        Write8(offset + 1, (byte)(value >> 8));
        Write8(offset + 2, (byte)(value >> 16));
        Write8(offset + 3, (byte)(value >> 24));
    }

    public void Clear() => Array.Clear(_data);
}
