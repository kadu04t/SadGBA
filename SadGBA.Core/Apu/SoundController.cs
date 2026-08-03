namespace SadGBA.Core.Apu;

public readonly record struct StereoSample(short Left, short Right);

/// <summary>
/// Estado inicial da APU. Preserva registradores, wave RAM e FIFOs Direct
/// Sound e fornece um relógio de amostragem; síntese PSG será adicionada depois.
/// </summary>
public sealed class SoundController
{
    public const uint CyclesPerSample = 512; // 16,777,216 Hz / 32,768 Hz
    private readonly byte[] _registers = new byte[0x50];
    private readonly byte[] _waveRam = new byte[0x10];
    private readonly Queue<byte> _fifoA = new(32);
    private readonly Queue<byte> _fifoB = new(32);
    private uint _sampleCycles;

    public bool MasterEnabled => (_registers[0x24] & 0x80) != 0;
    public ulong SamplesGenerated { get; private set; }
    public event Action<StereoSample>? SampleGenerated;

    public byte Read8(uint offset)
    {
        if (offset is >= 0x090 and <= 0x09F)
            return _waveRam[offset - 0x090];
        if (offset is >= 0x060 and <= 0x08F)
            return _registers[offset - 0x060];
        return 0;
    }

    public ushort Read16(uint offset) => (ushort)(Read8(offset) | (Read8(offset + 1) << 8));

    public void Write8(uint offset, byte value)
    {
        if (offset is >= 0x090 and <= 0x09F)
            _waveRam[offset - 0x090] = value;
        else if (offset is >= 0x060 and <= 0x08F)
        {
            if (!MasterEnabled && offset != 0x084)
                return;
            _registers[offset - 0x060] = value;
            if (offset == 0x084 && (value & 0x80) == 0)
                Array.Clear(_registers, 0, 0x24);
        }
        else if (offset is >= 0x0A0 and <= 0x0A3)
            Enqueue(_fifoA, value);
        else if (offset is >= 0x0A4 and <= 0x0A7)
            Enqueue(_fifoB, value);
    }

    public void Write16(uint offset, ushort value)
    {
        Write8(offset, (byte)value);
        Write8(offset + 1, (byte)(value >> 8));
    }

    public void Tick(uint cycles)
    {
        _sampleCycles += cycles;
        while (_sampleCycles >= CyclesPerSample)
        {
            _sampleCycles -= CyclesPerSample;
            SamplesGenerated++;
            SampleGenerated?.Invoke(default);
        }
    }

    public byte PopFifoA() => _fifoA.Count == 0 ? (byte)0 : _fifoA.Dequeue();
    public byte PopFifoB() => _fifoB.Count == 0 ? (byte)0 : _fifoB.Dequeue();

    public void Reset()
    {
        Array.Clear(_registers);
        Array.Clear(_waveRam);
        _fifoA.Clear();
        _fifoB.Clear();
        _sampleCycles = 0;
        SamplesGenerated = 0;
    }

    private static void Enqueue(Queue<byte> fifo, byte value)
    {
        if (fifo.Count < 32)
            fifo.Enqueue(value);
    }
}

