# SadGBA

<p align="center">
  An experimental Game Boy Advance emulator written in C# and .NET 10.
</p>

**English** | [Português (Brasil)](README.pt-BR.md)

SadGBA is built around hardware-oriented subsystem boundaries, deterministic
cycle advancement, and a testable core that does not depend on a graphical
frontend. The project is still an early alpha: its architecture and several
hardware foundations are in place, but commercial game compatibility is not
yet a release target.

Current version: **0.1.0-alpha.1**.

## Current Status

- The ARM7TDMI interpreter executes essential ARMv4T ARM and Thumb instructions.
- BIOS and direct Game Pak entry paths initialize separate processor modes and stacks.
- The memory bus maps BIOS, EWRAM, IWRAM, MMIO, palette RAM, VRAM, OAM, ROM, and SRAM.
- Video timing models scanlines, HBlank, VBlank, VCount, and frame completion.
- The software renderer supports mode 0 text backgrounds and bitmap modes 3, 4, and 5.
- Timers, interrupts, keypad input, HALT, immediate DMA, and HBlank/VBlank DMA are modeled.
- The headless CLI can run ROMs by instruction or frame count and export PPM screenshots.
- The automated suite currently contains 36 unit and integration tests.

| Area | State |
| --- | --- |
| CPU | Interpreted ARM7TDMI with ARM/Thumb states, CPSR/SPSR behavior, exceptions, IRQs, and banked registers |
| Memory | BIOS, 256 KiB EWRAM, 32 KiB IWRAM, MMIO, video memory, Game Pak ROM windows, and SRAM |
| PPU | LCD timing, mode 0 text backgrounds, bitmap modes 3–5, RGB555 conversion, and a 240×160 framebuffer |
| DMA and timers | Four DMA channels, immediate/HBlank/VBlank triggers, four hardware timers, and interrupt requests |
| Input and audio | Active-low keypad state; APU registers, wave RAM, Direct Sound FIFOs, and sample timing foundation |
| Diagnostics | Headless execution, instruction trace, register dump, SRAM import/export, screenshots, and automated tests |

Detailed implementation notes are available in the [architecture document](docs/ARCHITECTURE.md).

## Compatibility

SadGBA is not currently considered compatible with commercial games. The
implemented subsystems are sufficient for focused hardware tests and early
homebrew experiments, but missing PPU, APU, cartridge backup, and CPU edge
cases can still prevent games from booting or progressing correctly.

Compatibility claims will only be added after repeatable tests with documented
ROM revisions, execution settings, and known issues.

## Quick Start

SadGBA does not include a Game Boy Advance BIOS or game images. Use only dumps
made legally from hardware and cartridges you own.

Run directly from a Game Pak image without a BIOS:

```powershell
dotnet run -c Release --project SadGBA.Cli -- `
  .\GamesGBA\Game.gba --steps 100000 --dump-registers
```

Run through a legally dumped 16 KiB BIOS:

```powershell
dotnet run -c Release --project SadGBA.Cli -- `
  .\GamesGBA\Game.gba --bios .\BiosGBA\gba_bios.bin --steps 100000
```

Run one frame and export the final framebuffer:

```powershell
dotnet run -c Release --project SadGBA.Cli -- `
  .\GamesGBA\Game.gba --frames 1 --screenshot .\frame.ppm
```

Additional CLI options include `--trace` and `--save`.

## Building and Testing

Requirements:

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- PowerShell for the validation helper script

```powershell
dotnet restore SadGBA.slnx
dotnet build SadGBA.slnx -c Release
dotnet test SadGBA.slnx -c Release
```

Run the complete local validation sequence:

```powershell
.\scripts\validate.ps1
```

## Documentation

- [Architecture, memory map, and current limitations](docs/ARCHITECTURE.md)
- [Portuguese README](README.pt-BR.md)

More focused CPU, video, audio, device, and compatibility documents will be
added as those subsystems mature.

## Project Layout

```text
SadGBA.Core/  Emulated GBA hardware and deterministic machine state
SadGBA.Cli/   Headless execution and diagnostic tools
SadGBA.Tests/ Unit and integration tests
docs/         Architecture and future technical documentation
scripts/      Local validation helpers
```

## Known Limitations

- ARMv4T edge cases, pipeline behavior, alignment, and instruction timing still need conformance work.
- Affine backgrounds, sprites, windows, mosaic, blending, and complete PPU composition are missing.
- The APU does not yet synthesize PSG or Direct Sound output.
- DMA FIFO timing, configurable waitstates, prefetch, and sequential Game Pak timing are incomplete.
- Flash and EEPROM saves, serial communication, RTC, sensors, and special cartridge hardware are not implemented.
- There is no real-time SDL frontend, host audio output, debugger UI, or save-state system.

## Philosophy

SadGBA follows the same hardware-oriented direction as SadPSX: explicit device
state, deterministic timing, clear subsystem ownership, and regression tests
are preferred over shortcuts tied to a specific frontend. The goal is to keep
the emulator open and educational without hiding hardware behavior behind
host-specific abstractions.

## Legal Notice

Game Boy Advance and Nintendo are trademarks of Nintendo. SadGBA is an
independent project and is not affiliated with or endorsed by Nintendo. No
BIOS, games, encryption material, or copyrighted console software are
distributed with this repository.

## License

SadGBA is licensed under the [GNU GPL v3](LICENSE).

The project is intended to remain open, educational, and collaborative.
Distributed modified versions must also remain open under the GPL.
