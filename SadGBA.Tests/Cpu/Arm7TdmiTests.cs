using SadGBA.Core.Bus;
using SadGBA.Core.Cpu;
using Xunit;

namespace SadGBA.Tests.Cpu;

public sealed class Arm7TdmiTests
{
    [Fact]
    public void ResetStartsInArmSupervisorModeWithInterruptsDisabled()
    {
        var cpu = new Arm7Tdmi();
        Assert.Equal(CpuMode.Supervisor, cpu.Mode);
        Assert.False(cpu.ThumbState);
        Assert.True(cpu.IrqDisabled);
        Assert.Equal(0u, cpu.Pc);
    }

    [Fact]
    public void ArmMoveAndAddExecute()
    {
        var bus = new GbaBus();
        bus.Write32(0x0300_0000, 0xE3A0_0005); // mov r0, #5
        bus.Write32(0x0300_0004, 0xE280_1003); // add r1, r0, #3
        var cpu = new Arm7Tdmi(bus);
        cpu.Reset(0x0300_0000);

        cpu.Step(); cpu.Step();

        Assert.Equal(5u, cpu.GetRegister(0));
        Assert.Equal(8u, cpu.GetRegister(1));
        Assert.Equal(0x0300_0008u, cpu.Pc);
    }

    [Fact]
    public void ArmStoreAndLoadUseBus()
    {
        var bus = new GbaBus();
        bus.Write32(0x0300_0000, 0xE582_1000); // str r1, [r2]
        bus.Write32(0x0300_0004, 0xE592_3000); // ldr r3, [r2]
        var cpu = new Arm7Tdmi(bus);
        cpu.Reset(0x0300_0000);
        cpu.SetRegister(1, 0xDEAD_BEEF);
        cpu.SetRegister(2, 0x0200_0040);

        cpu.Step(); cpu.Step();

        Assert.Equal(0xDEAD_BEEFu, bus.Read32(0x0200_0040));
        Assert.Equal(0xDEAD_BEEFu, cpu.GetRegister(3));
    }

    [Fact]
    public void BranchExchangeEntersThumbState()
    {
        var bus = new GbaBus();
        bus.Write32(0x0300_0000, 0xE12F_FF10); // bx r0
        var cpu = new Arm7Tdmi(bus);
        cpu.Reset(0x0300_0000);
        cpu.SetRegister(0, 0x0300_0021);

        cpu.Step();

        Assert.True(cpu.ThumbState);
        Assert.Equal(0x0300_0020u, cpu.Pc);
    }

    [Fact]
    public void ThumbMoveAndAddExecute()
    {
        var bus = new GbaBus();
        bus.Write16(0x0300_0000, 0x2005); // mov r0, #5
        bus.Write16(0x0300_0002, 0x3003); // add r0, #3
        var cpu = new Arm7Tdmi(bus);
        cpu.Reset(0x0300_0000);
        cpu.SetProgramStatus(cpu.Cpsr | Arm7Tdmi.ThumbFlag);

        cpu.Step(); cpu.Step();

        Assert.Equal(8u, cpu.GetRegister(0));
        Assert.Equal(0x0300_0004u, cpu.Pc);
    }

    [Fact]
    public void EnabledIrqEntersIrqVectorAndBanksLinkRegister()
    {
        var bus = new GbaBus();
        var cpu = new Arm7Tdmi(bus);
        cpu.Reset(0x0300_0000);
        cpu.SetProgramStatus((uint)CpuMode.Supervisor);
        bus.Io.Interrupts.WriteRegister(0x200, 1);
        bus.Io.Interrupts.WriteRegister(0x208, 1);
        bus.Io.Interrupts.Request(SadGBA.Core.Interrupts.InterruptSource.VBlank);

        cpu.Step();

        Assert.Equal(CpuMode.Irq, cpu.Mode);
        Assert.Equal(0x18u, cpu.Pc);
        Assert.Equal(0x0300_0004u, cpu.GetRegister(14));
        Assert.Equal(CpuException.Irq, cpu.LastException);
    }

    [Fact]
    public void MsrCanSwitchPrivilegedProcessorMode()
    {
        var bus = new GbaBus();
        bus.Write32(0x0300_0000, 0xE121_F000); // msr cpsr_c, r0
        var cpu = new Arm7Tdmi(bus);
        cpu.Reset(0x0300_0000);
        cpu.SetRegister(0, (uint)CpuMode.System);

        cpu.Step();

        Assert.Equal(CpuMode.System, cpu.Mode);
        Assert.False(cpu.IrqDisabled);
    }

    [Fact]
    public void UnsignedLongMultiplyWritesHighAndLowRegisters()
    {
        var bus = new GbaBus();
        bus.Write32(0x0300_0000, 0xE081_0392); // umull r0, r1, r2, r3
        var cpu = new Arm7Tdmi(bus);
        cpu.Reset(0x0300_0000);
        cpu.SetRegister(2, uint.MaxValue);
        cpu.SetRegister(3, 2);

        cpu.Step();

        Assert.Equal(0xFFFF_FFFEu, cpu.GetRegister(0));
        Assert.Equal(1u, cpu.GetRegister(1));
    }

    [Fact]
    public void ThumbRegisterOffsetLoadReadsMemory()
    {
        var bus = new GbaBus();
        bus.Write16(0x0300_0000, 0x5891); // ldr r1, [r2, r2]
        bus.Write32(0x0200_0008, 0xAABB_CCDD);
        var cpu = new Arm7Tdmi(bus);
        cpu.Reset(0x0300_0000);
        cpu.SetRegister(2, 0x0100_0004);
        cpu.SetProgramStatus(cpu.Cpsr | Arm7Tdmi.ThumbFlag);

        cpu.Step();

        Assert.Equal(0xAABB_CCDDu, cpu.GetRegister(1));
    }

    [Fact]
    public void FiqModeBanksRegistersEightThroughFourteen()
    {
        var cpu = new Arm7Tdmi();
        cpu.SetRegister(8, 0x1111_1111);
        cpu.SetRegister(13, 0xAAAA_AAAA);

        cpu.SetProgramStatus((uint)CpuMode.Fiq | Arm7Tdmi.IrqDisableFlag | Arm7Tdmi.FiqDisableFlag);
        Assert.Equal(0u, cpu.GetRegister(8));
        Assert.Equal(0u, cpu.GetRegister(13));
        cpu.SetRegister(8, 0x2222_2222);
        cpu.SetRegister(13, 0xBBBB_BBBB);

        cpu.SetProgramStatus((uint)CpuMode.System | Arm7Tdmi.IrqDisableFlag | Arm7Tdmi.FiqDisableFlag);
        Assert.Equal(0x1111_1111u, cpu.GetRegister(8));
        Assert.Equal(0u, cpu.GetRegister(13));

        cpu.SetProgramStatus((uint)CpuMode.Fiq | Arm7Tdmi.IrqDisableFlag | Arm7Tdmi.FiqDisableFlag);
        Assert.Equal(0x2222_2222u, cpu.GetRegister(8));
        Assert.Equal(0xBBBB_BBBBu, cpu.GetRegister(13));
    }
}
