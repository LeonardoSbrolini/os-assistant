<#
.SYNOPSIS
  Roda os testes, publica o Assistente (exe único, sem precisar instalar .NET)
  e gera o instalador com o Inno Setup.

.EXAMPLE
  .\build.ps1
  .\build.ps1 -Version 0.2.0
  .\build.ps1 -SkipInstaller
#>
param(
    [string]$Version = "0.1.0",
    [switch]$SkipTests,
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$publishDir = Join-Path $root "artifacts\publish"

if (-not $SkipTests) {
    Write-Host "==> Testes" -ForegroundColor Cyan
    dotnet test "$root\tests\Assistente.Core.Tests\Assistente.Core.Tests.csproj" -c Release
    if ($LASTEXITCODE -ne 0) { throw "Os testes falharam." }
}

Write-Host "==> Publicando versão $Version" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish "$root\src\Assistente.App\Assistente.App.csproj" `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:Version=$Version `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "Falha ao publicar." }
Write-Host "Executável: $publishDir\Assistente.exe" -ForegroundColor Green

if ($SkipInstaller) { return }

Write-Host "==> Instalador" -ForegroundColor Cyan
$iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
$isccPath = if ($iscc) { $iscc.Source } else {
    @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $isccPath) {
    Write-Warning "Inno Setup 6 não encontrado. Instale com:  winget install JRSoftware.InnoSetup"
    Write-Warning "O executável já está pronto em $publishDir (dá para usar sem instalador)."
    return
}

& $isccPath "/DMyAppVersion=$Version" "$root\installer\Assistente.iss"
if ($LASTEXITCODE -ne 0) { throw "Falha ao gerar o instalador." }
Write-Host "Instalador gerado em $root\artifacts\installer" -ForegroundColor Green
