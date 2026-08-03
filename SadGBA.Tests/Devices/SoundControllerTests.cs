using SadGBA.Core.Apu;
using Xunit;

namespace SadGBA.Tests.Devices;

public sealed class SoundControllerTests
{
    [Fact]
    public void SampleClockIsDeterministic()
    {
        var sound = new SoundController();
        int emitted = 0;
        sound.SampleGenerated += _ => emitted++;
        sound.Tick(SoundController.CyclesPerSample * 3);
        Assert.Equal(3, emitted);
        Assert.Equal(3ul, sound.SamplesGenerated);
    }

    [Fact]
    public void DirectSoundFifoPreservesByteOrder()
    {
        var sound = new SoundController();
        sound.Write16(0x0A0, 0x2211);
        Assert.Equal(0x11, sound.PopFifoA());
        Assert.Equal(0x22, sound.PopFifoA());
    }
}
