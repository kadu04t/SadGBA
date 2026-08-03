namespace SadGBA.Core.Bus;

public enum MemoryAccessKind { Read, Write, InstructionFetch }

public readonly record struct MemoryAccess(
    uint Address,
    MemoryAccessKind Kind,
    int Width,
    uint Value);

