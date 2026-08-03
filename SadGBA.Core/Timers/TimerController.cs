using SadGBA.Core.Interrupts;

namespace SadGBA.Core.Timers;

public sealed class TimerController
{
    private readonly TimerChannel[] _channels = new TimerChannel[4];
    private readonly InterruptController _interrupts;

    public TimerController(InterruptController interrupts)
    {
        _interrupts = interrupts ?? throw new ArgumentNullException(nameof(interrupts));
        for (int index = 0; index < _channels.Length; index++)
            _channels[index] = new TimerChannel();
    }

    public ushort ReadRegister(uint offset)
    {
        int channel = (int)((offset - 0x100) / 4);
        if ((uint)channel >= 4)
            return 0;
        return (offset & 2) == 0 ? _channels[channel].Counter : _channels[channel].Control;
    }

    public void WriteRegister(uint offset, ushort value)
    {
        int channel = (int)((offset - 0x100) / 4);
        if ((uint)channel >= 4)
            return;

        TimerChannel timer = _channels[channel];
        if ((offset & 2) == 0)
        {
            timer.Reload = value;
            return;
        }

        bool wasEnabled = timer.Enabled;
        timer.Control = (ushort)(value & 0x00C7);
        if (!wasEnabled && timer.Enabled)
        {
            timer.Counter = timer.Reload;
            timer.Accumulator = 0;
        }
    }

    public void Tick(uint cycles)
    {
        uint cascadeTicks = 0;
        for (int index = 0; index < 4; index++)
        {
            TimerChannel timer = _channels[index];
            if (!timer.Enabled)
            {
                cascadeTicks = 0;
                continue;
            }

            uint ticks;
            if (timer.Cascade && index != 0)
            {
                ticks = cascadeTicks;
            }
            else
            {
                uint prescaler = timer.Prescaler;
                ulong accumulated = timer.Accumulator + cycles;
                ticks = (uint)(accumulated / prescaler);
                timer.Accumulator = (uint)(accumulated % prescaler);
            }

            cascadeTicks = Advance(timer, ticks, index);
        }
    }

    public void Reset()
    {
        foreach (TimerChannel timer in _channels)
            timer.Reset();
    }

    private uint Advance(TimerChannel timer, uint ticks, int channel)
    {
        uint overflows = 0;
        while (ticks > 0)
        {
            uint untilOverflow = 0x1_0000u - timer.Counter;
            if (ticks < untilOverflow)
            {
                timer.Counter += (ushort)ticks;
                break;
            }

            ticks -= untilOverflow;
            timer.Counter = timer.Reload;
            overflows++;
            if (timer.IrqEnabled)
                _interrupts.Request((InterruptSource)((ushort)InterruptSource.Timer0 << channel));
        }
        return overflows;
    }

    private sealed class TimerChannel
    {
        public ushort Reload;
        public ushort Counter;
        public ushort Control;
        public uint Accumulator;
        public bool Enabled => (Control & 0x80) != 0;
        public bool IrqEnabled => (Control & 0x40) != 0;
        public bool Cascade => (Control & 0x04) != 0;
        public uint Prescaler => (Control & 3) switch { 0 => 1, 1 => 64, 2 => 256, _ => 1024 };

        public void Reset()
        {
            Reload = Counter = Control = 0;
            Accumulator = 0;
        }
    }
}

