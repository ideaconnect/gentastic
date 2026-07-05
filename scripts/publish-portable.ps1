<#
.SYNOPSIS
  Produces a self-contained, portable build of Gentastic (no .NET install required) and zips it.
.DESCRIPTION
  A RID-specific self-contained publish flattens/dedupes the StableDiffusion.NET native backends
  (they all ship as runtimes/win-x64/native/<variant>/stable-diffusion.dll). This script reconstructs
  that folder from the NuGet packages so the loader finds the backends at runtime.

  The default build bundles the CPU + Vulkan backends. Pass -IncludeCuda to additionally bundle the
  NVIDIA CUDA 12 backend (~177 MB, NVIDIA-only) into a separate "-cuda" variant; the runtime detector
  lights it up automatically on machines with an NVIDIA GPU + the CUDA 12 toolkit.
.EXAMPLE
  powershell -File scripts/publish-portable.ps1
.EXAMPLE
  powershell -File scripts/publish-portable.ps1 -IncludeCuda
#>
param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [switch]$IncludeCuda
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$suffix = if ($IncludeCuda) { "-cuda" } else { "" }
$outDir = Join-Path $root "dist\Gentastic-$Runtime$suffix"
$zipPath = Join-Path $root "dist\Gentastic-portable-$Runtime$suffix.zip"

if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

$publishArgs = @("-c", $Configuration, "-r", $Runtime, "--self-contained", "true", "-o", $outDir)
if ($IncludeCuda) { $publishArgs += "-p:IncludeCuda=true" }
dotnet publish (Join-Path $root "src\Gentastic.App\Gentastic.App.csproj") @publishArgs

# Reconstruct runtimes/<rid>/native/<variant>/ from the backend packages (publish flattens them).
$nativeDst = Join-Path $outDir "runtimes\$Runtime\native"
Remove-Item (Join-Path $outDir "stable-diffusion.dll") -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $nativeDst | Out-Null

$backendPkgs = @("stablediffusion.net.backend.vulkan", "stablediffusion.net.backend.cpu")
if ($IncludeCuda) { $backendPkgs += "stablediffusion.net.backend.cuda12.windows" }

$packages = Join-Path $env:USERPROFILE ".nuget\packages"
foreach ($pkg in $backendPkgs) {
    $ver = Get-ChildItem (Join-Path $packages $pkg) -Directory | Sort-Object Name -Descending | Select-Object -First 1
    $src = Join-Path $ver.FullName "runtimes\$Runtime\native"
    if (Test-Path $src) { Copy-Item (Join-Path $src '*') $nativeDst -Recurse -Force }
}
Write-Host "Native backends: $((Get-ChildItem $nativeDst -Directory).Name -join ', ')"

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $outDir '*') -DestinationPath $zipPath

$sizeMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "Portable build: $zipPath ($sizeMb MB)"
