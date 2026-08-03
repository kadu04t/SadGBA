using System.Globalization;
using SadGBA.Cli;
using SadGBA.Core;
using SadGBA.Core.Ppu;

if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase))
{
    PrintUsage();
    return args.Length == 0 ? 1 : 0;
}

string romPath = Path.GetFullPath(args[0]);
string? biosPath = null;
string? savePath = null;
string? screenshotPath = null;
ulong steps = 100_000;
ulong? frames = null;
bool stepsSpecified = false;
bool trace = false;
bool dumpRegisters = false;

try
{
    for (int index = 1; index < args.Length; index++)
    {
        switch (args[index])
        {
            case "--bios": biosPath = Path.GetFullPath(ReadValue(args, ref index)); break;
            case "--save": savePath = Path.GetFullPath(ReadValue(args, ref index)); break;
            case "--screenshot": screenshotPath = Path.GetFullPath(ReadValue(args, ref index)); break;
            case "--steps":
                string text = ReadValue(args, ref index);
                if (!ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out steps))
                    throw new ArgumentException($"Quantidade de passos inválida: {text}.");
                stepsSpecified = true;
                break;
            case "--frames":
                string frameText = ReadValue(args, ref index);
                if (!ulong.TryParse(frameText, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsedFrames) || parsedFrames == 0)
                    throw new ArgumentException($"Quantidade de frames inválida: {frameText}.");
                frames = parsedFrames;
                break;
            case "--trace": trace = true; break;
            case "--dump-registers": dumpRegisters = true; break;
            default: throw new ArgumentException($"Opção desconhecida: {args[index]}.");
        }
    }
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine($"Erro: {exception.Message}");
    PrintUsage();
    return 1;
}

if (!File.Exists(romPath))
{
    Console.Error.WriteLine($"Erro: ROM não encontrada: {romPath}");
    return 1;
}
if (biosPath is not null && !File.Exists(biosPath))
{
    Console.Error.WriteLine($"Erro: BIOS não encontrada: {biosPath}");
    return 1;
}

var machine = new GbaMachine();
try
{
    machine.LoadCartridge(romPath);
    if (biosPath is not null)
        machine.LoadBios(biosPath);
    if (savePath is not null && File.Exists(savePath))
        machine.Bus.GamePak.ImportSave(File.ReadAllBytes(savePath));
    machine.Reset(skipBios: biosPath is null);
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
{
    Console.Error.WriteLine($"Erro ao preparar a máquina: {exception.Message}");
    return 1;
}

var header = machine.Bus.GamePak.Header;
Console.WriteLine($"ROM: {romPath}");
Console.WriteLine($"Jogo: {(string.IsNullOrEmpty(header.Title) ? "(sem título)" : header.Title)} [{header.GameCode}]");
Console.WriteLine($"BIOS: {(biosPath ?? "HLE/ignorada; execução direta do cartucho")}");
Console.WriteLine($"PC inicial: 0x{machine.Cpu.Pc:X8}");

if (screenshotPath is not null && frames is null && !stepsSpecified)
    frames = 1;

ulong executed = 0;
ulong targetFrame = machine.Bus.Io.Display.FrameCount + (frames ?? 0);
while (frames.HasValue ? machine.Bus.Io.Display.FrameCount < targetFrame : executed < steps)
{
    if (trace)
    {
        uint address = machine.Cpu.Pc;
        bool thumb = machine.Cpu.ThumbState;
        machine.Step();
        Console.WriteLine($"{executed,10}  {address:X8}  {(thumb ? "T" : "A")}  {machine.Cpu.LastInstruction:X8}");
    }
    else
    {
        machine.Step();
    }
    executed++;
}

Console.WriteLine($"Instruções: {machine.Cpu.InstructionCount}");
Console.WriteLine($"Ciclos: {machine.ClockCycles}");
Console.WriteLine($"PC final: 0x{machine.Cpu.Pc:X8}");
Console.WriteLine($"Frames: {machine.Bus.Io.Display.FrameCount}");

if (dumpRegisters)
{
    for (int index = 0; index < 16; index += 4)
        Console.WriteLine(string.Join("  ", Enumerable.Range(index, 4).Select(r => $"r{r:D2}=0x{machine.Cpu.GetRegister(r):X8}")));
    Console.WriteLine($"CPSR=0x{machine.Cpu.Cpsr:X8}  modo={machine.Cpu.Mode}  estado={(machine.Cpu.ThumbState ? "Thumb" : "ARM")}");
}

if (savePath is not null)
{
    File.WriteAllBytes(savePath, machine.Bus.GamePak.ExportSave());
    Console.WriteLine($"Save SRAM gravado: {savePath}");
}

if (screenshotPath is not null)
{
    try
    {
        PpmWriter.Write(screenshotPath, machine.Bus.Io.Display.FrameBuffer.Span,
            DisplayController.ScreenWidth, DisplayController.ScreenHeight);
        Console.WriteLine($"Screenshot PPM gravada: {screenshotPath}");
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
    {
        Console.Error.WriteLine($"Erro ao gravar screenshot: {exception.Message}");
        return 1;
    }
}

return 0;

static string ReadValue(string[] arguments, ref int index)
{
    if (++index >= arguments.Length)
        throw new ArgumentException($"A opção {arguments[index - 1]} exige um valor.");
    return arguments[index];
}

static void PrintUsage()
{
    Console.WriteLine("SadGBA CLI - executor headless do núcleo do emulador");
    Console.WriteLine();
    Console.WriteLine("Uso:");
    Console.WriteLine("  dotnet run --project SadGBA.Cli -- jogo.gba [opções]");
    Console.WriteLine();
    Console.WriteLine("Opções:");
    Console.WriteLine("  --bios arquivo.bin     Usa uma BIOS real de 16 KiB");
    Console.WriteLine("  --steps N              Executa N instruções (padrão: 100000)");
    Console.WriteLine("  --frames N             Executa até completar N frames");
    Console.WriteLine("  --trace                Mostra cada instrução executada");
    Console.WriteLine("  --dump-registers       Mostra registradores ao terminar");
    Console.WriteLine("  --save arquivo.sav     Importa/exporta SRAM de 64 KiB");
    Console.WriteLine("  --screenshot tela.ppm  Exporta o framebuffer final em PPM");
    Console.WriteLine("  --help                 Mostra esta ajuda");
}
