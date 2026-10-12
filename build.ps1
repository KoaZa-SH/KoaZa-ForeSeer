$ErrorActionPreference = 'Stop'
Write-Host 'Building KoaZa Foreseer (Release)...' -ForegroundColor Cyan
dotnet restore .\KoaZa.Foreseer.csproj
dotnet build .\KoaZa.Foreseer.csproj -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host 'Build complete. To publish self-contained x64, run:' -ForegroundColor Green
Write-Host 'dotnet publish .\KoaZa.Foreseer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\publish\win-x64'
