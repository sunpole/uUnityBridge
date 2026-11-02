# Stage QA Desktop and Agent WITHOUT touching the installed uUnityBridge_App.
# This stages files only. Running QA is a separate, explicit user action.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidatePattern('^[0-9a-f]{40}$')][string]$ExpectedCommit,
    [Parameter(Mandatory=$true)][long]$DesktopRun,
    [Parameter(Mandatory=$true)][long]$AgentRun
)
$ErrorActionPreference = 'Stop'
$repo = 'sunpole/uUnityBridge'
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'GitHub CLI gh is required.' }
foreach ($id in @($DesktopRun, $AgentRun)) {
    $raw = & gh run view $id -R $repo --json headSha,conclusion
    if ($LASTEXITCODE -ne 0) { throw "Unable to validate CI run $id" }
    $result = $raw | ConvertFrom-Json
    if ($result.headSha -ne $ExpectedCommit -or $result.conclusion -ne 'success') {
        throw "CI run $id does not match the expected successful commit."
    }
}

$qa = Join-Path $env:LOCALAPPDATA ('uUnityBridge-QA\' + $ExpectedCommit.Substring(0,12))
if (Test-Path -LiteralPath $qa) { throw "Refusing to overwrite an existing QA directory: $qa" }
$desktop = Join-Path $qa 'Desktop'
$agent = Join-Path $qa 'Agent'
[void](New-Item -ItemType Directory -Path $desktop -Force)
[void](New-Item -ItemType Directory -Path $agent -Force)
& gh run download $DesktopRun -R $repo -n 'uUnityBridge-windows-x64-ci' -D $desktop
if ($LASTEXITCODE -ne 0) { throw 'Desktop CI artifact download failed.' }
& gh run download $AgentRun -R $repo -n 'uUnityBridge-agent-p0008-preview' -D $agent
if ($LASTEXITCODE -ne 0) { throw 'Agent CI artifact download failed.' }

$desktopExe = Join-Path $desktop 'uUnityBridge.exe'
$agentExe = Join-Path $agent 'uUnityBridge.Agent.exe'
$shaFile = Join-Path $desktop 'uUnityBridge.exe.sha256'
foreach ($file in @($desktopExe,$agentExe,$shaFile)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing CI artifact: $file" }
}
$expectedHash = ((Get-Content -LiteralPath $shaFile -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
$actualHash = (Get-FileHash -LiteralPath $desktopExe -Algorithm SHA256).Hash.ToLowerInvariant()
if ($expectedHash -notmatch '^[0-9a-f]{64}$' -or $expectedHash -ne $actualHash) {
    throw 'Downloaded Desktop SHA-256 is not the value produced by CI.'
}
& $agentExe --self-test
if ($LASTEXITCODE -ne 0) { throw 'Agent self-test failed. Do not launch this QA.' }

$launcher = @'
$ErrorActionPreference = 'Stop'
$oldFlag = [Environment]::GetEnvironmentVariable('UUB_UNITY_QA','Process')
$env:UUB_UNITY_QA = '1'
$base = Split-Path -Parent $PSCommandPath
$qaAgent = Join-Path $base 'Agent\uUnityBridge.Agent.exe'
$qaDesktop = Join-Path $base 'Desktop\uUnityBridge.exe'
$process = $null
try {
    # Different agent mutex, Named Pipe and local consent file from stable.
    $process = Start-Process -FilePath $qaAgent -ArgumentList '--serve-qa' -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 2
    if ($process.HasExited) { throw 'QA Agent could not start. The working agent was not modified.' }
    Write-Host 'UNITY QA MODE: production bridge and startup were not modified.'
    Write-Host 'In GUI choose UNITY · ПАПКА, then UNITY · СОЗДАТЬ.'
    $desktop = Start-Process -FilePath $qaDesktop -PassThru -Wait
    Write-Host ('QA Desktop exit code: ' + $desktop.ExitCode)
}
finally {
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
    if ($null -eq $oldFlag) { Remove-Item Env:UUB_UNITY_QA -ErrorAction SilentlyContinue }
    else { $env:UUB_UNITY_QA = $oldFlag }
}
'@
$launcherFile = Join-Path $qa 'Run-Unity-QA.ps1'
Set-Content -LiteralPath $launcherFile -Value $launcher -Encoding UTF8
Write-Host 'PASS: QA builds staged and Agent tests passed.'
Write-Host "QA path: $qa"
Write-Host 'No installed EXEs, startup entries or production settings were changed.'
Write-Host "To run the isolated QA explicitly: & '$launcherFile'"
