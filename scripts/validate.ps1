$ErrorActionPreference = 'Stop'

dotnet restore SadGBA.slnx
dotnet build SadGBA.slnx --no-restore
dotnet test SadGBA.slnx --no-build
