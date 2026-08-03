# SadGBA

SadGBA é um emulador de Game Boy Advance escrito em C# e .NET 10.

Este primeiro estágio estabelece somente a fundação do núcleo:

- estado básico da CPU ARM7TDMI;
- barramento de memória de 32 bits;
- EWRAM de 256 KiB e IWRAM de 32 KiB;
- solução e configuração inicial do projeto.

O projeto está em desenvolvimento inicial e ainda não executa jogos.

## Compilar

```powershell
dotnet build SadGBA.slnx
```

BIOS e ROMs não são distribuídas com o projeto.

