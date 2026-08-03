# SadGBA

SadGBA é um emulador de Game Boy Advance escrito em C#/.NET 10. O projeto
segue a separação arquitetural do SadPSX: o núcleo não depende de interface
gráfica, a CLI serve para execução e diagnóstico, e cada subsistema possui
testes próprios.

> Estado atual: primeiro marco de desenvolvimento. O mapa de memória, a CPU
> ARM7TDMI básica, modo 0 por tiles, modos bitmap 3/4/5, framebuffer, temporização de vídeo,
> timers, interrupções, keypad, ROM e SRAM já têm uma implementação executável.
> Ainda não há frontend em tempo real, backgrounds por tiles, sprites, síntese
> de áudio nem compatibilidade ampla com jogos comerciais.

## Projetos

- `SadGBA.Core`: CPU, barramento, memória e dispositivos emulados.
- `SadGBA.Cli`: execução headless de ROMs, trace e dump de registradores.
- `SadGBA.Tests`: testes unitários e de integração do núcleo.

## Compilar e testar

```powershell
dotnet build SadGBA.slnx
dotnet test SadGBA.slnx
```

## Executar

Sem BIOS (entrada direta na ROM, útil durante o desenvolvimento):

```powershell
dotnet run --project SadGBA.Cli -- caminho\jogo.gba --steps 100000 --dump-registers
```

Com uma BIOS real de GBA, que deve possuir exatamente 16 KiB:

```powershell
dotnet run --project SadGBA.Cli -- caminho\jogo.gba --bios caminho\gba_bios.bin --steps 100000
```

Executar um frame e salvar o framebuffer como imagem PPM:

```powershell
dotnet run --project SadGBA.Cli -- caminho\jogo.gba --frames 1 --screenshot tela.ppm
```

Arquivos de BIOS e jogos não são distribuídos pelo projeto. Use somente dumps
obtidos legalmente do seu próprio hardware e cartuchos.

## Próximos marcos

1. Completar ARMv4T (coprocessor/undefined e casos extremos de alinhamento/pipeline).
2. Completar DMA de FIFO e waitstates configuráveis por `WAITCNT`.
3. PPU modos afins 1–2, sprites, janelas e blending.
4. APU com canais PSG e FIFO Direct Sound.
5. Saves Flash/EEPROM e detecção do tipo de backup.
6. Frontend SDL, entrada, áudio e ferramentas de depuração.

Veja [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) para detalhes.
