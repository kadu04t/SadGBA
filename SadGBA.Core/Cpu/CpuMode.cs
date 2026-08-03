namespace SadGBA.Core.Cpu;

public enum CpuMode : uint
{
    User = 0x10,
    Fiq = 0x11,
    Irq = 0x12,
    Supervisor = 0x13,
    Abort = 0x17,
    Undefined = 0x1B,
    System = 0x1F,
}

public enum CpuException
{
    None,
    UndefinedInstruction,
    SoftwareInterrupt,
    Irq,
}

