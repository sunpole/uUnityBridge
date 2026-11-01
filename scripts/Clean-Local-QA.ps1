<#
.SYNOPSIS
  Проверка и обратимая очистка старых QA-копий uUnityBridge.
.DESCRIPTION
  Без параметров: только просмотр. С -Clean: перенос заранее известного
  набора тестовых копий в Корзину Windows после проверок.
  Не затрагивает uUnityBridge_App, исходники uUnityBridge, историю ANS,
  папку %LOCALAPPDATA%, ярлыки и сторонние проекты.
#>
[CmdletBinding()]
param([switch]$Clean)
$ErrorActionPreference = 'Stop'
$root = 'C:\!CODE_CLUB\new 2026\017_uunity'
$app = Join-Path $root 'uUnityBridge_App'
$repo = 'sunpole/uUnityBridge'
$names = @('QA-0.2.1','QA-0.3.0','QA-0.4.0')
# Exact names only: debug symbol PDBs are normal CI publish artifacts.
# Never allow arbitrary *.pdb; unknown names, directories and links stay protected.
$allowedFiles = @(
    'uUnityBridge.exe',
    'uUnityBridge.Updater.exe',
    'uUnityBridge.exe.sha256',
    'uUnityBridge.pdb',
    'uUnityBridge.Updater.pdb'
)
$expected = @{
    'QA-0.2.1' = @('0.2.1')
    'QA-0.3.0' = @('0.3.0')
    'QA-0.4.0' = @('0.4.0','0.4.1','0.4.2')
}

function Assert-OrdinaryDirectory([string]$path) {
    if (-not (Test-Path -LiteralPath $path -PathType Container)) {
        throw "Required directory does not exist: $path"
    }
    $item = Get-Item -LiteralPath $path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Symbolic link or junction is not allowed: $path"
    }
}
Assert-OrdinaryDirectory $root
Assert-OrdinaryDirectory $app
$installed = Join-Path $app 'uUnityBridge.exe'
$updater = Join-Path $app 'uUnityBridge.Updater.exe'
if (-not (Test-Path -LiteralPath $installed -PathType Leaf) -or
    -not (Test-Path -LiteralPath $updater -PathType Leaf)) {
    throw 'The stable installed app/updater pair is missing. Nothing changed.'
}
$installedVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($installed).FileVersion
if ($installedVersion -notmatch '^0[.]4[.]2([.]|$)') {
    throw "Unexpected installed app version: $installedVersion. Nothing changed."
}

# Validate every expected historical GitHub Release before touching QA.
$tags = @('v0.1.0','v0.2.0','v0.2.1','v0.3.0','v0.4.0','v0.4.1','v0.4.2')
foreach ($tag in $tags) {
    $meta = gh release view $tag -R $repo --json tagName,assets 2>$null
    if ($LASTEXITCODE -ne 0) { throw "Release missing or GitHub unreachable: $tag" }
    $release = $meta | ConvertFrom-Json
    if ($release.tagName -ne $tag -or @($release.assets).Count -eq 0) {
        throw "Release has no archived artifacts: $tag"
    }
}

$candidates = @()
foreach ($name in $names) {
    $path = Join-Path $root $name
    if (-not (Test-Path -LiteralPath $path)) { continue }
    Assert-OrdinaryDirectory $path
    $children = @(Get-ChildItem -LiteralPath $path -Force)
    if ($children.Count -eq 0) {
        Write-Host "SKIP empty $name"
        continue
    }
    if (@($children | Where-Object { $_.PSIsContainer }).Count -gt 0) {
        Write-Warning "$name contains subdirectories; not safe for automatic cleanup."
        continue
    }
    $unexpected = @($children | Where-Object {
        $_.Name -notin $allowedFiles -or
        ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
    })
    if ($unexpected.Count -ne 0) {
        Write-Warning "$name contains additional/linked files: $($unexpected.Name -join ', '). Preserved."
        continue
    }
    # Treat only the two known PDBs as non-executable debug symbols.
    # Reject unexpectedly large symbols rather than collecting arbitrary data.
    $symbols = @($children | Where-Object { $_.Extension -ieq '.pdb' })
    if (@($symbols | Where-Object { $_.Length -gt 100MB }).Count -ne 0) {
        Write-Warning "$name contains an unexpectedly large PDB; preserved."
        continue
    }
    $qaExe = Join-Path $path 'uUnityBridge.exe'
    if (-not (Test-Path -LiteralPath $qaExe -PathType Leaf)) {
        Write-Warning "$name has no uUnityBridge.exe. Preserved."
        continue
    }
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($qaExe).FileVersion
    $ok = @($expected[$name] | Where-Object { $version -match ('^' + [regex]::Escape($_) + '([.]|$)') })
    if ($ok.Count -eq 0) {
        Write-Warning "$name contains unexpected EXE version $version. Preserved."
        continue
    }
    $bytes = ($children | Measure-Object Length -Sum).Sum
    $candidates += [pscustomobject]@{
        Folder = $name
        Path = $path
        Version = $version
        Files = $children.Count
        MB = [math]::Round($bytes / 1MB, 1)
    }
}

Write-Host ''
Write-Host 'Candidate QA copies (stable App and Git source always protected):'
$candidates | Format-Table -AutoSize
if (-not $Clean) {
    Write-Host 'PREVIEW ONLY: nothing moved or deleted.'
    Write-Host 'Use -Clean only if you want to move these exact QA folders to the Windows Recycle Bin.'
    return
}
if ($candidates.Count -eq 0) {
    Write-Host 'Nothing eligible for cleanup.'
    return
}
if (Get-Process -Name uUnityBridge -ErrorAction SilentlyContinue) {
    throw 'Close all uUnityBridge windows before cleanup. No files changed.'
}
Add-Type -AssemblyName Microsoft.VisualBasic
foreach ($entry in $candidates) {
    # The Windows Recycle Bin allows the user to restore the original local files.
    [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteDirectory(
        $entry.Path,
        [Microsoft.VisualBasic.FileIO.UIOption]::OnlyErrorDialogs,
        [Microsoft.VisualBasic.FileIO.RecycleOption]::SendToRecycleBin
    )
    if (Test-Path -LiteralPath $entry.Path) {
        throw "Move to Recycle Bin did not complete: $($entry.Folder)"
    }
    Write-Host "RECYCLED: $($entry.Folder) ($($entry.MB) MB)"
}
Write-Host "PASS: eligible QA folders moved to Recycle Bin, stable installation not touched."
