# Prepare isolated QA artifacts only. Never replace the production uUnityBridge_App.
# The generated launcher is run separately and only after the user asks for QA.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidatePattern('^[0-9a-f]{40}$')][string]$ExpectedCommit,
    [Parameter(Mandatory=$true)][long]$RunId
)
$ErrorActionPreference = 'Stop'
$repo = 'sunpole/uUnityBridge'
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'GitHub CLI (gh) is missing; production is unchanged.'
}
$run = & gh run view $RunId -R $repo --json headSha,conclusion,workflowName
if ($LASTEXITCODE -ne 0) { throw 'Cannot authenticate and check the expected CI run.' }
$verified = $run | ConvertFrom-Json
if ($verified.headSha -ne $ExpectedCommit -or $verified.conclusion -ne 'success' -or
    $verified.workflowName -ne 'Windows desktop build (uUnityBridge)') {
    throw 'BLOCKED: CI run must be successful Desktop+Agent workflow at the exact approved SHA.'
}

$qa = Join-Path $env:LOCALAPPDATA ('uUnityBridge-QA\' + $ExpectedCommit.Substring(0,12))
if (Test-Path -LiteralPath $qa) {
    throw "BLOCKED: QA folder already exists; refusing to overwrite it: $qa"
}
$desktop = Join-Path $qa 'Desktop'
$agent = Join-Path $qa 'Agent'
[void](New-Item -ItemType Directory -Path $desktop -Force)
[void](New-Item -ItemType Directory -Path $agent -Force)
& gh run download $RunId -R $repo -n 'uUnityBridge-windows-x64-ci' -D $desktop
if ($LASTEXITCODE -ne 0) { throw 'QA Desktop artifact not available.' }
& gh run download $RunId -R $repo -n 'uUnityBridge-agent-p0008-preview' -D $agent
if ($LASTEXITCODE -ne 0) { throw 'QA Agent artifact not available.' }

function Check-QAHash([string]$exe, [string]$shaFile) {
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf) -or
        -not (Test-Path -LiteralPath $shaFile -PathType Leaf)) {
        throw 'BLOCKED: one or more downloaded executable/hash files are missing.'
    }
    $line = (Get-Content -LiteralPath $shaFile -Raw).Trim()
    $expected = ($line -split '\s+')[0].ToLowerInvariant()
    $actual = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expected -notmatch '^[0-9a-f]{64}$' -or $expected -ne $actual) {
        throw 'BLOCKED: a CI executable did not match its declared SHA-256.'
    }
}
$desktopExe = Join-Path $desktop 'uUnityBridge.exe'
$agentExe = Join-Path $agent 'uUnityBridge.Agent.exe'
Check-QAHash $desktopExe (Join-Path $desktop 'uUnityBridge.exe.sha256')
Check-QAHash $agentExe (Join-Path $agent 'uUnityBridge.Agent.exe.sha256')

& $agentExe --self-test
if ($LASTEXITCODE -ne 0) { throw 'BLOCKED: the staged QA Agent self-test failed.' }

$launcher = @'
$ErrorActionPreference = 'Stop'
$oldQa = [Environment]::GetEnvironmentVariable('UUB_UNITY_QA','Process')
$env:UUB_UNITY_QA = '1'
$base = Split-Path -Parent $PSCommandPath
$qaAgent = Join-Path $base 'Agent\uUnityBridge.Agent.exe'
$qaDesktop = Join-Path $base 'Desktop\uUnityBridge.exe'
$process = $null
try {
    $process = Start-Process -FilePath $qaAgent -ArgumentList '--serve-qa' -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 2
    if ($process.HasExited) { throw 'BLOCKED: QA Agent exited unexpectedly.' }
    Write-Host 'PASS: isolated QA started; production EXEs and background agent unchanged.'
    Write-Host 'PowerShell waits while QA Desktop is open. This is expected.'
    Write-Host 'Use UNITY LOGS or UNITY FRAME. Only approved Unity window is captured.'
    $desktop = Start-Process -FilePath $qaDesktop -PassThru -Wait
    Write-Host ('QA Desktop exited with code ' + $desktop.ExitCode)
}
finally {
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
    if ($null -eq $oldQa) { Remove-Item Env:UUB_UNITY_QA -ErrorAction SilentlyContinue }
    else { $env:UUB_UNITY_QA = $oldQa }
}
'@
$launcherFile = Join-Path $qa 'Run-Unity-QA.ps1'
Set-Content -LiteralPath $launcherFile -Value $launcher -Encoding UTF8
Write-Host 'PASS: matching Desktop/Agent binaries staged; both SHA-256 verified.'
Write-Host "QA location: $qa"
Write-Host 'No production EXE, startup entry or production consent was modified.'
Write-Host "To launch QA only: & '$launcherFile'"
