# build.ps1 - compile Voice2Txt.cs to bin\Voice2Txt.exe
#
# Two build paths:
#   1. .NET SDK (if `dotnet` is on PATH): native ARM64 single-file exe
#      (win-arm64). Optionally install the SDK with:  .\build.ps1 -InstallSdk
#   2. Fallback: Windows PowerShell 5.1 Add-Type -> standalone .NET Framework
#      exe (runs fine on ARM64 Windows, but through x64 emulation).
#
# Usage:
#   .\build.ps1                 # auto: SDK if available, else 5.1 fallback (win-arm64)
#   .\build.ps1 -Arch x64       # native win-x64 single file (bin-x64\)
#   .\build.ps1 -SelfContained  # SDK path: bundle the runtime (~70 MB)
#   .\build.ps1 -ForceFramework # skip SDK even if installed
#   .\build.ps1 -InstallSdk     # winget install .NET SDK 8 (ARM64)

param(
    [string]$Arch = 'arm64',
    [switch]$SelfContained,
    [switch]$ForceFramework,
    [switch]$InstallSdk
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$src = Join-Path $root 'Voice2Txt.cs'
$outDir = Join-Path $root 'bin'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$exe = Join-Path $outDir 'Voice2Txt.exe'

if ($InstallSdk) {
    Write-Host 'Installing .NET SDK 8 (arm64) via winget...'
    winget install --id Microsoft.DotNet.SDK.8 --accept-source-agreements --accept-package-agreements
    Write-Host 'Installed. Open a NEW shell so `dotnet` is on PATH, then run .\build.ps1 again.'
    return
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnet -and -not $ForceFramework) {
    # ---- Path 1: native via .NET SDK ----
    if ($Arch -eq 'x64') {
        $outDir = Join-Path $root 'bin-x64'
        New-Item -ItemType Directory -Force -Path $outDir | Out-Null
        $exe = Join-Path $outDir 'Voice2Txt.exe'
    }
    $projDir = Join-Path $root 'build'
    New-Item -ItemType Directory -Force -Path $projDir | Out-Null
    $sc = if ($SelfContained) { 'true' } else { 'false' }
    $csproj = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <RuntimeIdentifier>win-$Arch</RuntimeIdentifier>
    <SelfContained>$sc</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <AssemblyName>Voice2Txt</AssemblyName>
    <RootNamespace>Voice2Txt</RootNamespace>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <LangVersion>latest</LangVersion>
    <ApplicationIcon>..\assets\icon.ico</ApplicationIcon>
    <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="..\Voice2Txt.cs" />
    <EmbeddedResource Include="..\assets\icon.ico" Link="icon.ico" />
  </ItemGroup>
</Project>
"@
    Set-Content -Path (Join-Path $projDir 'Voice2Txt.csproj') -Value $csproj -Encoding UTF8
    & dotnet publish (Join-Path $projDir 'Voice2Txt.csproj') -c Release -o $outDir
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
    Copy-Item (Join-Path $root 'assets\icon.ico') (Join-Path $outDir 'icon.ico') -Force
    Write-Host "Built (native win-arm64): $exe"
    return
}

# ---- Path 2: standalone exe via Windows PowerShell 5.1 Add-Type ----
if ($PSVersionTable.PSVersion.Major -ge 7) {
    # Re-run this script under Windows PowerShell 5.1 (the C# 5 compiler).
    & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'build.ps1') -ForceFramework
    return
}

Write-Host 'No .NET SDK found -> building standalone .NET Framework exe (runs on ARM64 via emulation).'
Write-Host 'For a native ARM64 exe run:  .\build.ps1 -InstallSdk'
Add-Type -TypeDefinition (Get-Content -Raw -Encoding UTF8 $src) `
    -ReferencedAssemblies System.Windows.Forms, System.Drawing, System.Net.Http `
    -OutputType WindowsApplication -OutputAssembly $exe
Copy-Item (Join-Path $root 'assets\icon.ico') (Join-Path $outDir 'icon.ico') -Force
Write-Host "Built: $exe"
