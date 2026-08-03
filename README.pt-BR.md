# SadGBA

<p align="center">
  Um emulador experimental de Game Boy Advance escrito em C# e .NET 10.
</p>

[English](README.md) | **Português (Brasil)**

O SadGBA é construído com subsistemas orientados ao hardware, avanço
determinístico por ciclos e um núcleo testável que não depende de interface
gráfica. O projeto ainda é um alpha inicial: sua arquitetura e várias bases de
hardware estão implementadas, mas compatibilidade com jogos comerciais ainda
não é um objetivo de lançamento.

Versão atual: **0.1.0-alpha.1**.

## Estado atual

- O interpretador ARM7TDMI executa instruções ARM e Thumb essenciais do ARMv4T.
- Os caminhos com BIOS e entrada direta no Game Pak inicializam modos e pilhas separados.
- O barramento mapeia BIOS, EWRAM, IWRAM, MMIO, palette RAM, VRAM, OAM, ROM e SRAM.
- O timing de vídeo modela scanlines, HBlank, VBlank, VCount e conclusão de frames.
- O rasterizador por software suporta backgrounds de texto do modo 0 e os modos bitmap 3, 4 e 5.
- Timers, interrupções, keypad, HALT, DMA imediato e DMA por HBlank/VBlank estão modelados.
- A CLI headless executa ROMs por instruções ou frames e exporta screenshots PPM.
- A suíte automatizada possui atualmente 36 testes unitários e de integração.

| Área | Estado |
| --- | --- |
| CPU | ARM7TDMI interpretado com estados ARM/Thumb, CPSR/SPSR, exceções, IRQs e registradores com bancos |
| Memória | BIOS, EWRAM de 256 KiB, IWRAM de 32 KiB, MMIO, memória de vídeo, janelas da ROM e SRAM |
| PPU | Timing do LCD, backgrounds de texto do modo 0, modos bitmap 3–5, conversão RGB555 e framebuffer 240×160 |
| DMA e timers | Quatro canais DMA, gatilhos imediato/HBlank/VBlank, quatro timers e pedidos de interrupção |
| Entrada e áudio | Keypad ativo em nível baixo; registradores da APU, wave RAM, FIFOs Direct Sound e base de timing |
| Diagnóstico | Execução headless, trace, dump de registradores, SRAM, screenshots e testes automatizados |

Os detalhes estão no [documento de arquitetura](docs/ARCHITECTURE.md).

## Compatibilidade

O SadGBA ainda não é considerado compatível com jogos comerciais. Os
subsistemas implementados são suficientes para testes focados de hardware e
experimentos iniciais com homebrew, mas recursos ausentes da PPU, APU,
armazenamento de cartucho e casos extremos da CPU ainda podem impedir jogos de
iniciar ou progredir corretamente.

Resultados de compatibilidade só serão adicionados após testes reproduzíveis
com revisões de ROM, configurações e problemas conhecidos documentados.

## Uso rápido

O SadGBA não inclui BIOS ou jogos de Game Boy Advance. Use apenas dumps obtidos
legalmente de hardware e cartuchos que você possui.

Execute diretamente uma imagem de Game Pak sem BIOS:

```powershell
dotnet run -c Release --project SadGBA.Cli -- `
  .\GamesGBA\Game.gba --steps 100000 --dump-registers
```

Execute por uma BIOS de 16 KiB obtida legalmente:

```powershell
dotnet run -c Release --project SadGBA.Cli -- `
  .\GamesGBA\Game.gba --bios .\BiosGBA\gba_bios.bin --steps 100000
```

Execute um frame e exporte o framebuffer final:

```powershell
dotnet run -c Release --project SadGBA.Cli -- `
  .\GamesGBA\Game.gba --frames 1 --screenshot .\frame.ppm
```

Outras opções da CLI incluem `--trace` e `--save`.

## Compilação e testes

Requisitos:

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- PowerShell para o script auxiliar de validação

```powershell
dotnet restore SadGBA.slnx
dotnet build SadGBA.slnx -c Release
dotnet test SadGBA.slnx -c Release
```

Execute a sequência completa de validação local:

```powershell
.\scripts\validate.ps1
```

## Documentação

- [Arquitetura, mapa de memória e limitações atuais](docs/ARCHITECTURE.md)
- [README em inglês](README.md)

Documentos específicos de CPU, vídeo, áudio, dispositivos e compatibilidade
serão adicionados conforme esses subsistemas amadurecerem.

## Estrutura do projeto

```text
SadGBA.Core/  Hardware emulado e estado determinístico da máquina
SadGBA.Cli/   Execução headless e ferramentas de diagnóstico
SadGBA.Tests/ Testes unitários e de integração
docs/         Arquitetura e futura documentação técnica
scripts/      Ferramentas de validação local
```

## Limitações conhecidas

- Casos extremos do ARMv4T, pipeline, alinhamento e timing ainda precisam de conformidade.
- Backgrounds afins, sprites, janelas, mosaic, blending e composição completa da PPU não existem.
- A APU ainda não sintetiza saída PSG ou Direct Sound.
- DMA de FIFO, waitstates configuráveis, prefetch e timing sequencial do Game Pak estão incompletos.
- Saves Flash/EEPROM, serial, RTC, sensores e hardware especial de cartucho não estão implementados.
- Não existe frontend SDL em tempo real, áudio do host, debugger visual ou save states.

## Filosofia

O SadGBA segue a mesma direção orientada ao hardware do SadPSX: estado
explícito dos dispositivos, timing determinístico, responsabilidade clara por
subsistema e testes de regressão são preferidos a atalhos ligados a uma
interface específica. O objetivo é manter o emulador aberto e educativo sem
esconder o comportamento do hardware em abstrações do sistema hospedeiro.

## Aviso legal

Game Boy Advance e Nintendo são marcas da Nintendo. O SadGBA é um projeto
independente e não possui afiliação ou aprovação da Nintendo. Nenhuma BIOS,
jogo, material de criptografia ou software protegido do console é distribuído
neste repositório.

## Licença

O SadGBA é licenciado sob a [GNU GPL v3](LICENSE).

O projeto pretende permanecer aberto, educativo e colaborativo. Versões
modificadas distribuídas também devem permanecer abertas sob a GPL.
