# Gera o pacote para distribuir: dist\Riffstorm-windows.zip
# (executável único com o .NET embutido - o jogador não precisa instalar nada)
# uso:  powershell -ExecutionPolicy Bypass -File build-release.ps1
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$dotnet = "C:\Program Files\dotnet\dotnet.exe"
$out = "dist\Riffstorm"
if (Test-Path dist) { Remove-Item dist -Recurse -Force }
& $dotnet publish Riffstorm.csproj -c Release -r win-x64 --self-contained true -o $out -v q -nologo `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:EnableCompressionInSingleFile=true
if ($LASTEXITCODE -ne 0) { throw "falha na compilação" }
Copy-Item "pacote\*" $out -Recurse -Force
Compress-Archive -Path $out -DestinationPath "dist\Riffstorm-windows.zip" -CompressionLevel Optimal
Write-Host "OK -> $PSScriptRoot\dist\Riffstorm-windows.zip"
