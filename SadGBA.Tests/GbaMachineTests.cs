using SadGBA.Core;
using SadGBA.Core.Cpu;
using SadGBA.Core.Interrupts;
using Xunit;

namespace SadGBA.Tests;

public sealed class GbaMachineTests
{
    [Fact]
    public void SkipBiosStartsAtCartridgeEntryAndRunsCode()
    {
        var machine = new GbaMachine();
        machine.LoadCartridge([0x2A, 0x00, 0xA0, 0xE3]); // mov r0, #42
        machine.Reset(skipBios: true);

        machine.Step();

        Assert.Equal(42u, machine.Cpu.GetRegister(0));
        Assert.Equal(0x0800_0004u, machine.Cpu.Pc);
        Assert.True(machine.ClockCycles > 0);
    }

    [Fact]
    public void SkipBiosInitializesBankedStackPointersAndPostFlag()
    {
        var machine = new GbaMachine();
        machine.LoadCartridge([0, 0, 0, 0]);
        machine.Reset(skipBios: true);

        Assert.Equal(CpuMode.System, machine.Cpu.Mode);
        Assert.Equal(0x0300_7F00u, machine.Cpu.GetRegister(13));
        Assert.Equal(1, machine.Bus.Io.PostBootFlag);

        machine.Cpu.SetProgramStatus((uint)CpuMode.Irq | Arm7Tdmi.IrqDisableFlag | Arm7Tdmi.FiqDisableFlag);
        Assert.Equal(0x0300_7FA0u, machine.Cpu.GetRegister(13));
        machine.Cpu.SetProgramStatus((uint)CpuMode.Supervisor | Arm7Tdmi.IrqDisableFlag | Arm7Tdmi.FiqDisableFlag);
        Assert.Equal(0x0300_7FE0u, machine.Cpu.GetRegister(13));
    }

    [Fact]
    public void HaltKeepsDevicesRunningAndWakesOnEnabledInterrupt()
    {
        var machine = new GbaMachine();
        machine.LoadCartridge([0, 0, 0, 0]);
        machine.Reset(skipBios: true);
        machine.Bus.Write16(0x0400_0200, 1);
        machine.Bus.Write8(0x0400_0301, 0);
        ulong instructionCount = machine.Cpu.InstructionCount;

        machine.Step();
        Assert.True(machine.Bus.Io.Halted);
        Assert.Equal(instructionCount, machine.Cpu.InstructionCount);

        machine.Bus.Io.Interrupts.Request(InterruptSource.VBlank);
        machine.Step();
        Assert.False(machine.Bus.Io.Halted);
    }

    [Fact]
    public void NormalResetRequiresLoadedBios()
    {
        var machine = new GbaMachine();
        Assert.Throws<InvalidOperationException>(() => machine.Reset());
    }
}
