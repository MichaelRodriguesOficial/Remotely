<#
.SYNOPSIS
    Compila os agents e os clientes de desktop do Remotely (.NET 10) e monta a pasta Content.

.DESCRIPTION
    Gera os mesmos arquivos que o servidor distribui em wwwroot/Content:

      Remotely-Win-x64.zip, Remotely-Win-x86.zip   agent + desktop (Windows)
      Remotely-Linux.zip                           agent + desktop (Linux x64)
      Remotely-MacOS-x64.zip, Remotely-MacOS-arm64.zip  agent (macOS)
      Win-x64/Remotely_Desktop.exe, Win-x86/Remotely_Desktop.exe  suporte instantâneo
      Linux-x64/Remotely_Desktop                   suporte instantâneo (Linux)
      AgentVersion.txt                             versão dos agents acima

    Os scripts de instalação (Install-*.sh / Install-Remotely.ps1) já estão no repositório
    e são copiados junto. Precisa do SDK do .NET 10 e roda no Windows (por causa do
    Desktop.Win, que usa WinForms/DirectX).

.PARAMETER Version
    Versão gravada nos binários. Padrão: data do último commit no formato yyyy.MM.dd.HHmm,
    o mesmo esquema usado pelo Remotely original.

.PARAMETER OutDir
    Pasta de saída. Padrão: Server/wwwroot/Content.

.EXAMPLE
    pwsh ./Utilities/Build-Agents.ps1
    pwsh ./Utilities/Build-Agents.ps1 -Version 2026.11.20.1000 -OutDir ./artifacts/Content
#>
[CmdletBinding()]
param(
    [string]$Version = "",
    [string]$OutDir = ""
)

$ErrorActionPreference = "Stop"
$Root = (Get-Item -Path $PSScriptRoot).Parent.FullName

if (-not $Version) {
    $commitDate = git -C $Root show -s --format=%ci
    $Version = [DateTimeOffset]::Parse($commitDate).ToUniversalTime().ToString("yyyy.MM.dd.HHmm")
}
if (-not $OutDir) {
    $OutDir = Join-Path $Root "Server/wwwroot/Content"
}
$OutDir = [IO.Path]::GetFullPath($OutDir)
$PublishRoot = Join-Path $Root "Agent/bin/publish"

Write-Host "Versão dos agents: $Version"
Write-Host "Saída: $OutDir"

function Invoke-Dotnet {
    param([string[]]$Arguments)
    Write-Host "dotnet $($Arguments -join ' ')" -ForegroundColor Cyan
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments[0]) falhou (código $LASTEXITCODE)."
    }
}

# Propriedades comuns: versão, sem símbolos de depuração e sem pastas de idiomas extras.
$common = @(
    "-c", "Release",
    "-p:Version=$Version",
    "-p:FileVersion=$Version",
    "-p:InformationalVersion=$Version",
    "-p:DebugType=None",
    "-p:DebugSymbols=false"
)

if (Test-Path $PublishRoot) {
    Remove-Item -Path $PublishRoot -Recurse -Force
}
New-Item -Path $OutDir -ItemType Directory -Force | Out-Null

# ---------- Agents ----------
$agentRids = @("win-x64", "win-x86", "linux-x64", "osx-x64", "osx-arm64")
foreach ($rid in $agentRids) {
    Invoke-Dotnet (@("publish", (Join-Path $Root "Agent/Agent.csproj"),
        "-r", $rid, "--self-contained", "true",
        "-o", (Join-Path $PublishRoot $rid)) + $common)
}

# ---------- Desktop empacotado junto com o agent ----------
Invoke-Dotnet (@("publish", (Join-Path $Root "Desktop.Win/Desktop.Win.csproj"),
    "-p:PublishProfile=packaged-win-x64",
    "-o", (Join-Path $PublishRoot "win-x64/Desktop")) + $common)

Invoke-Dotnet (@("publish", (Join-Path $Root "Desktop.Win/Desktop.Win.csproj"),
    "-p:PublishProfile=packaged-win-x86",
    "-o", (Join-Path $PublishRoot "win-x86/Desktop")) + $common)

Invoke-Dotnet (@("publish", (Join-Path $Root "Desktop.Linux/Desktop.Linux.csproj"),
    "-p:PublishProfile=packaged-linux-x64",
    "-o", (Join-Path $PublishRoot "linux-x64/Desktop")) + $common)

# ---------- Clientes de suporte instantâneo (arquivo único) ----------
Invoke-Dotnet (@("publish", (Join-Path $Root "Desktop.Win/Desktop.Win.csproj"),
    "-p:PublishProfile=desktop-win-x64",
    "-o", (Join-Path $OutDir "Win-x64")) + $common)

Invoke-Dotnet (@("publish", (Join-Path $Root "Desktop.Win/Desktop.Win.csproj"),
    "-p:PublishProfile=desktop-win-x86",
    "-o", (Join-Path $OutDir "Win-x86")) + $common)

Invoke-Dotnet (@("publish", (Join-Path $Root "Desktop.Linux/Desktop.Linux.csproj"),
    "-p:PublishProfile=desktop-linux-x64",
    "-o", (Join-Path $OutDir "Linux-x64")) + $common)

# ---------- Compactação ----------
$zips = [ordered]@{
    "win-x64"   = "Remotely-Win-x64.zip"
    "win-x86"   = "Remotely-Win-x86.zip"
    "linux-x64" = "Remotely-Linux.zip"
    "osx-x64"   = "Remotely-MacOS-x64.zip"
    "osx-arm64" = "Remotely-MacOS-arm64.zip"
}
foreach ($entry in $zips.GetEnumerator()) {
    $source = Join-Path $PublishRoot $entry.Key
    $target = Join-Path $OutDir $entry.Value
    if (Test-Path $target) {
        Remove-Item $target -Force
    }
    Write-Host "Compactando $($entry.Value)..."
    Compress-Archive -Path (Join-Path $source "*") -DestinationPath $target -CompressionLevel Optimal
}

# Os clientes de arquivo único não precisam de nada além do executável.
foreach ($dir in @("Win-x64", "Win-x86", "Linux-x64")) {
    Get-ChildItem -Path (Join-Path $OutDir $dir) -File |
        Where-Object { $_.Name -notin @("Remotely_Desktop.exe", "Remotely_Desktop") } |
        Remove-Item -Force
}

# Scripts de instalação versionados no repositório.
$scriptsDir = Join-Path $Root "Server/wwwroot/Content"
if ([IO.Path]::GetFullPath($scriptsDir) -ne $OutDir) {
    Get-ChildItem -Path $scriptsDir -File |
        Where-Object { $_.Name -like "Install-*" } |
        Copy-Item -Destination $OutDir -Force
}

Set-Content -Path (Join-Path $OutDir "AgentVersion.txt") -Value $Version -NoNewline -Encoding ascii

Write-Host ""
Write-Host "Conteúdo gerado:" -ForegroundColor Green
Get-ChildItem -Path $OutDir -Recurse -File |
    Sort-Object FullName |
    ForEach-Object {
        "{0,10:N1} MB  {1}" -f ($_.Length / 1MB), $_.FullName.Substring($OutDir.Length + 1)
    }
