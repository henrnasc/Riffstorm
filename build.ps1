# Compila o Riffstorm e publica o executável em .\jogo
# uso:  powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
if (Get-Process Riffstorm -ErrorAction SilentlyContinue) { throw "Feche o jogo antes de compilar." }
$dotnet = "C:\Program Files\dotnet\dotnet.exe"
& $dotnet publish Riffstorm.csproj -c Release -o jogo -v q -nologo
Remove-Item jogo\*.pdb -ErrorAction SilentlyContinue
Write-Host "OK -> $PSScriptRoot\jogo\Riffstorm.exe"
