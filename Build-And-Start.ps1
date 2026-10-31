#requires -version 5.1
[CmdletBinding()]
param([switch]$PublishPublic, [switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\uUnityBridge.csproj'
$dist = Join-Path $root 'dist\win-x64'

function Run-Checked([string]$exe, [string[]]$cmdArgs) {
    & $exe @cmdArgs
    if ($LASTEXITCODE -ne 0) { throw "$exe exited with code $LASTEXITCODE" }
}

Write-Host 'uUnityBridge: compile Windows desktop application (no installers, no admin).' -ForegroundColor Cyan
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Required .NET 8 SDK missing; no installation attempted.' }
Run-Checked 'dotnet' @('publish', $project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-o', $dist)
$exePath = Join-Path $dist 'uUnityBridge.exe'
if (-not (Test-Path -LiteralPath $exePath)) { throw 'Build ended without uUnityBridge.exe' }
Write-Host "Built: $exePath" -ForegroundColor Green

if ($PublishPublic) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'GitHub CLI gh missing.' }
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw 'Git missing.' }
    Run-Checked 'gh' @('auth', 'status')
    $login = (& gh api user --jq '.login').Trim()
    if ($LASTEXITCODE -ne 0 -or $login -ne 'sunpole') { throw "GitHub account mismatch: $login; expected sunpole" }
    if (Test-Path -LiteralPath (Join-Path $root '.git')) { throw 'Local Git repo already exists; publish aborted to avoid altering existing history.' }
    Run-Checked 'git' @('-C', $root, 'init', '-b', 'main')
    Run-Checked 'git' @('-C', $root, 'add', '--', '.gitignore', 'README.md', 'Build-And-Start.ps1', 'src/uUnityBridge.csproj', 'src/Program.cs', 'src/GhClient.cs', 'src/MainForm.cs', 'docs/PATCH_PROTOCOL.md', 'docs/VALIDATION.md', '.github/workflows/windows-build.yml')
    Run-Checked 'git' @('-C', $root, 'commit', '-m', 'feat: initial uUnityBridge Windows desktop prototype')
    Run-Checked 'gh' @('repo', 'create', 'sunpole/uUnityBridge', '--public', '--source', $root, '--remote', 'origin', '--push')
    Write-Host 'Public repository created: https://github.com/sunpole/uUnityBridge' -ForegroundColor Green
}

if (-not $BuildOnly) {
    # Create a convenience shortcut for the desktop GUI. Does not require admin.
    try {
        $desktop = [Environment]::GetFolderPath('DesktopDirectory')
        if (-not [string]::IsNullOrWhiteSpace($desktop)) {
            $shortcutPath = Join-Path $desktop 'uUnityBridge.lnk'
            if (-not (Test-Path -LiteralPath $shortcutPath)) {
                $shell = New-Object -ComObject WScript.Shell
                $shortcut = $shell.CreateShortcut($shortcutPath)
                $shortcut.TargetPath = $exePath
                $shortcut.WorkingDirectory = $dist
                $shortcut.Description = 'uUnityBridge - GitHub patches and status'
                $shortcut.Save()
                Write-Host "Created shortcut: $shortcutPath"
            }
        }
    }
    catch { Write-Warning 'Could not create desktop shortcut; EXE remains available in dist\win-x64.' }
    Start-Process -FilePath $exePath -WorkingDirectory $dist
    Write-Host 'Desktop window launch requested; actual GUI operation still needs testing on this PC.'
}
