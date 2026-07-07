<#
.SYNOPSIS
  Produces a self-contained, portable, universal build of Gentastic (no .NET install required) and zips it.
.DESCRIPTION
  A RID-specific self-contained publish flattens/dedupes the StableDiffusion.NET native backends
  (they all ship as runtimes/win-x64/native/<variant>/stable-diffusion.dll). This script reconstructs
  that folder from the NuGet packages so the loader finds the backends at runtime.

  The shipped build is universal: it bundles CPU + Vulkan + NVIDIA CUDA 12 so a single download works
  on AMD/Intel (Vulkan), NVIDIA (CUDA - GTX 10-series through RTX 50-series), and CPU. The runtime
  detector auto-selects the best available backend. CUDA is only pulled in here (via -p:IncludeCuda=true),
  not during normal dev/CI builds, so those stay lean.
.EXAMPLE
  powershell -File scripts/publish-portable.ps1
#>
param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$outDir = Join-Path $root "dist\Gentastic-$Runtime"
$zipPath = Join-Path $root "dist\Gentastic-portable-$Runtime.zip"

if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

dotnet publish (Join-Path $root "src\Gentastic.App\Gentastic.App.csproj") `
    -c $Configuration -r $Runtime --self-contained true -o $outDir -p:IncludeCuda=true

# Reconstruct runtimes/<rid>/native/<variant>/ from the backend packages (publish flattens them).
$nativeDst = Join-Path $outDir "runtimes\$Runtime\native"
Remove-Item (Join-Path $outDir "stable-diffusion.dll") -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $nativeDst | Out-Null

$backendPkgs = @(
    "stablediffusion.net.backend.vulkan",
    "stablediffusion.net.backend.cpu",
    "stablediffusion.net.backend.cuda12.windows"
)
$packages = Join-Path $env:USERPROFILE ".nuget\packages"
foreach ($pkg in $backendPkgs) {
    $ver = Get-ChildItem (Join-Path $packages $pkg) -Directory | Sort-Object Name -Descending | Select-Object -First 1
    $src = Join-Path $ver.FullName "runtimes\$Runtime\native"
    if (Test-Path $src) { Copy-Item (Join-Path $src '*') $nativeDst -Recurse -Force }
}
Write-Host "Native backends: $((Get-ChildItem $nativeDst -Directory).Name -join ', ')"

# Bundle the license + third-party notices so the redistribution meets the MIT/Apache/LGPL/OFL/CC-BY
# attribution obligations (see THIRD_PARTY_NOTICE.md). HPPH (LGPL-2.1) requires the notice + license
# text to travel with the binary; the others require reproducing their notices.
Copy-Item (Join-Path $root "LICENSE") $outDir -Force
Copy-Item (Join-Path $root "THIRD_PARTY_NOTICE.md") $outDir -Force
Copy-Item (Join-Path $root "licenses") $outDir -Recurse -Force
Write-Host "Bundled: LICENSE, THIRD_PARTY_NOTICE.md, licenses/"

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $outDir '*') -DestinationPath $zipPath

$sizeMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "Portable (universal: CPU + Vulkan + CUDA) build: $zipPath ($sizeMb MB)"
