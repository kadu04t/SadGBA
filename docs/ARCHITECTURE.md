# Arquitetura do SadGBA

## Princípios

O núcleo modela o hardware e não conhece janela, áudio do sistema operacional
ou controles físicos. Todos os dispositivos avançam por ciclos fornecidos pela
CPU, o que mantém testes e execuções reproduzíveis.

## Composição

`GbaMachine` é a raiz do sistema. Ela contém `Arm7Tdmi` e `GbaBus`; depois de
cada instrução, encaminha os ciclos consumidos para os dispositivos MMIO.
`GbaBus` é proprietário das regiões de memória e roteia acessos para BIOS,
EWRAM, IWRAM, registradores, palette, VRAM, OAM, ROM e SRAM.

```text
GbaMachine
├── Arm7Tdmi (ARM + Thumb, CPSR, exceções e bancos SP/LR)
└── GbaBus
    ├── BiosRom
    ├── ExternalWorkRam / InternalWorkRam
    ├── VideoMemory (palette, VRAM e OAM)
    ├── GamePak (ROM, header e SRAM)
    └── IoRegisters
        ├── DisplayController
        ├── TimerController
        ├── InterruptController
        ├── DmaController
        ├── SoundController
        └── Keypad
```

## Mapa de memória

| Faixa | Dispositivo |
| --- | --- |
| `00000000–00003FFF` | BIOS, 16 KiB |
| `02000000–02FFFFFF` | EWRAM, 256 KiB espelhada |
| `03000000–03FFFFFF` | IWRAM, 32 KiB espelhada |
| `04000000–040003FF` | registradores de I/O |
| `05000000–05FFFFFF` | palette RAM |
| `06000000–06FFFFFF` | VRAM de 96 KiB e espelhos |
| `07000000–07FFFFFF` | OAM |
| `08000000–0DFFFFFF` | três janelas da ROM do Game Pak |
| `0E000000–0FFFFFFF` | SRAM do cartucho |

## Limitações do marco inicial

- O interpretador cobre as famílias ARM/Thumb mais usadas, mas ainda não é uma
  implementação ARMv4T completa ou validada por suíte de conformidade.
- A PPU produz temporização de HBlank/VBlank/VCount e rasteriza o modo 0 por
  tiles (quatro backgrounds de texto, prioridades, scroll, flips, 4/8 bpp e
  mapas 256/512) e os modos bitmap 3, 4 e 5 para um framebuffer ARGB de
  240x160. Sprites, backgrounds afins, janelas e efeitos ainda não existem.
- O boot sem BIOS prepara os bancos de pilha IRQ/Supervisor/System, entra no
  Game Pak em `08000000` e marca `POSTFLG`. `HALTCNT` pausa apenas a CPU;
  dispositivos continuam avançando até uma fonte habilitada acordá-la.
- Waitstates ainda são estimativas fixas; `WAITCNT`, prefetch e acesso sequencial
  serão modelados em um marco posterior.
- DMA imediato e gatilhos HBlank/VBlank já funcionam; o gatilho especial de
  FIFO ainda precisa ser ligado aos timers da APU.
- A APU preserva registradores/FIFOs e relógio de amostragem, mas ainda não
  sintetiza PSG/Direct Sound. Serial e formatos Flash/EEPROM não existem ainda.
