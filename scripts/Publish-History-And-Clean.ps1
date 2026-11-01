# Owner-reviewed one-time historical archive publication + optional reversible QA cleanup.
[CmdletBinding()]
param([switch]$Clean)
$ErrorActionPreference = 'Stop'
$repo = 'sunpole/uUnityBridge'
$history = @(
 @{v='0.1.0';sha='eb98abcce8f6cc4fd5cd828875f2ad3d0f88d7b5'},
 @{v='0.2.0';sha='a6b14124df3ae69853940812b9e8471e9461fb1d'},
 @{v='0.2.1';sha='8835f545a8298720538ff641bdb5f420169907ec'},
 @{v='0.3.0';sha='d2dd9022b538e5f3ba8a39ca7d55dd6e3a428798'},
 @{v='0.4.0';sha='8e2979b5397229ff17826c72c6e0916b2a43edc0'}
)
$who = gh api user --jq .login
if ($LASTEXITCODE -ne 0 -or $who.Trim() -ne 'sunpole') { throw 'gh login must be sunpole.' }
$mainSha = (gh api ("repos/" + $repo + "/git/ref/heads/main") --jq '.object.sha').Trim()
if ($LASTEXITCODE -ne 0 -or $mainSha.Length -ne 40) { throw 'Main commit unavailable.' }
$runList = gh run list -R $repo -w archive-historical-releases.yml -L 20 --json databaseId,status,conclusion,event,headSha
if ($LASTEXITCODE -ne 0) { throw 'GitHub Actions unavailable.' }
# Important: in Windows PowerShell 5.1 a JSON array may travel through
# the pipeline as ONE nested array. Property access then expands ALL run IDs.
# Enumerate the parsed objects explicitly and require one scalar run ID.
$parsedRuns = ConvertFrom-Json -InputObject ($runList -join [Environment]::NewLine)
$matchingRuns = @()
foreach ($item in $parsedRuns) {
    if ($item.status -eq 'completed' -and $item.conclusion -eq 'success' -and
        $item.event -eq 'push' -and $item.headSha -eq $mainSha) {
        $matchingRuns += $item
    }
}
if ($matchingRuns.Count -eq 0) {
    throw 'No successful archived-build run for exact main commit.'
}
$selectedRun = $matchingRuns | Sort-Object -Property databaseId -Descending | Select-Object -First 1
$runId = [string]$selectedRun.databaseId
if ($runId -notmatch '^[1-9][0-9]*
$temp = Join-Path $env:TEMP ('uub-history-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
 Write-Host "Downloading exactly one verified run: $runId (main $mainSha)"
 gh run download $runId -R $repo -n uUnityBridge-historical-rebuilds-ci -D $temp
 if ($LASTEXITCODE -ne 0) { throw 'Download of historical archives failed.' }
 foreach ($entry in $history) {
   $tag = 'v' + $entry.v
   $sha = $entry.sha
   $zip = Join-Path $temp ('uUnityBridge-' + $tag + '-HISTORICAL-REBUILD-win-x64.zip')
   if (-not (Test-Path -LiteralPath $zip -PathType Leaf)) { throw "ZIP missing: $tag" }
   $dir = Join-Path $temp ('inspect-' + $entry.v)
   Expand-Archive -LiteralPath $zip -DestinationPath $dir
   $exe = Join-Path $dir 'uUnityBridge.exe'
   $sums = Join-Path $dir 'SHA256SUMS.txt'
   $readme = Join-Path $dir 'README-HISTORY.txt'
   if (-not (Test-Path $exe) -or -not (Test-Path $sums) -or -not (Test-Path $readme)) {
     throw "Historical rebuild package incomplete: $tag"
   }
   $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion
   if (-not $fileVersion.StartsWith($entry.v + '.')) { throw "Rebuilt version mismatch: $tag" }
   foreach ($line in Get-Content -LiteralPath $sums) {
     if ($line -notmatch '^([a-f0-9]{64})  ([A-Za-z0-9_.-]+)$') { throw "Invalid checksums: $tag" }
     $hash = $Matches[1]
     $candidate = Join-Path $dir $Matches[2]
     if (-not (Test-Path -LiteralPath $candidate -PathType Leaf) -or
        (Get-FileHash $candidate -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) {
       throw "SHA256 mismatch: $tag"
     }
   }
   $existing = gh release view $tag -R $repo --json tagName 2>$null
   if ($LASTEXITCODE -eq 0) {
     $refSha = (gh api ("repos/" + $repo + "/git/ref/tags/" + $tag) --jq '.object.sha').Trim()
     if ($LASTEXITCODE -ne 0 -or $refSha -ne $sha) { throw "Existing release target mismatch: $tag" }
     Write-Host "PRESERVE $tag"
     continue
   }
   $refExisting = gh api ("repos/" + $repo + "/git/ref/tags/" + $tag) --jq '.ref' 2>$null
   if ($LASTEXITCODE -eq 0 -and $refExisting) { throw "Existing tag without release: $tag" }
   $notes = @(
      "Исторический выпуск $tag",
      "Исходный коммит: $sha",
      "Архив содержит НОВУЮ РЕКОНСТРУИРОВАННУЮ Windows-сборку из старого кода.",
      "Не гарантируется побайтовое совпадение с первоначальным EXE.",
      "Этот выпуск не предназначен для автоматического обновления."
   ) -join [Environment]::NewLine
   gh release create $tag $zip -R $repo --target $sha --title "uUnityBridge $tag (история)" --notes $notes --latest=false
   if ($LASTEXITCODE -ne 0) { throw "Failed to create release $tag" }
   $refSha = (gh api ("repos/" + $repo + "/git/ref/tags/" + $tag) --jq '.object.sha').Trim()
   if ($LASTEXITCODE -ne 0 -or $refSha -ne $sha) { throw "Created tag mismatch $tag" }
   Write-Host "PUBLISHED $tag"
 }
 foreach ($tag in @('v0.4.1','v0.4.2')) {
   gh release view $tag -R $repo --json tagName | Out-Null
   if ($LASTEXITCODE -ne 0) { throw "Current Release missing: $tag" }
 }
 Write-Host 'PASS: seven historical/current releases are available.'
 if ($Clean) {
    $cleanup = Join-Path $PSScriptRoot 'Clean-Local-QA.ps1'
    if (-not (Test-Path $cleanup)) { throw 'Cleanup script missing. QA folders were not touched.' }
    & $cleanup -Clean
 } else {
    Write-Host 'No user files changed. Use -Clean to move eligible QA copies to Recycle Bin.'
 }
}
finally {
 if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force }
}
) {
    throw "Invalid or non-scalar GitHub run ID: $runId"
}
$temp = Join-Path $env:TEMP ('uub-history-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
 gh run download $run.databaseId -R $repo -n uUnityBridge-historical-rebuilds-ci -D $temp
 if ($LASTEXITCODE -ne 0) { throw 'Download of historical archives failed.' }
 foreach ($entry in $history) {
   $tag = 'v' + $entry.v
   $sha = $entry.sha
   $zip = Join-Path $temp ('uUnityBridge-' + $tag + '-HISTORICAL-REBUILD-win-x64.zip')
   if (-not (Test-Path -LiteralPath $zip -PathType Leaf)) { throw "ZIP missing: $tag" }
   $dir = Join-Path $temp ('inspect-' + $entry.v)
   Expand-Archive -LiteralPath $zip -DestinationPath $dir
   $exe = Join-Path $dir 'uUnityBridge.exe'
   $sums = Join-Path $dir 'SHA256SUMS.txt'
   $readme = Join-Path $dir 'README-HISTORY.txt'
   if (-not (Test-Path $exe) -or -not (Test-Path $sums) -or -not (Test-Path $readme)) {
     throw "Historical rebuild package incomplete: $tag"
   }
   $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion
   if (-not $fileVersion.StartsWith($entry.v + '.')) { throw "Rebuilt version mismatch: $tag" }
   foreach ($line in Get-Content -LiteralPath $sums) {
     if ($line -notmatch '^([a-f0-9]{64})  ([A-Za-z0-9_.-]+)$') { throw "Invalid checksums: $tag" }
     $hash = $Matches[1]
     $candidate = Join-Path $dir $Matches[2]
     if (-not (Test-Path -LiteralPath $candidate -PathType Leaf) -or
        (Get-FileHash $candidate -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) {
       throw "SHA256 mismatch: $tag"
     }
   }
   $existing = gh release view $tag -R $repo --json tagName 2>$null
   if ($LASTEXITCODE -eq 0) {
     $refSha = (gh api ("repos/" + $repo + "/git/ref/tags/" + $tag) --jq '.object.sha').Trim()
     if ($LASTEXITCODE -ne 0 -or $refSha -ne $sha) { throw "Existing release target mismatch: $tag" }
     Write-Host "PRESERVE $tag"
     continue
   }
   $refExisting = gh api ("repos/" + $repo + "/git/ref/tags/" + $tag) --jq '.ref' 2>$null
   if ($LASTEXITCODE -eq 0 -and $refExisting) { throw "Existing tag without release: $tag" }
   $notes = @(
      "Исторический выпуск $tag",
      "Исходный коммит: $sha",
      "Архив содержит НОВУЮ РЕКОНСТРУИРОВАННУЮ Windows-сборку из старого кода.",
      "Не гарантируется побайтовое совпадение с первоначальным EXE.",
      "Этот выпуск не предназначен для автоматического обновления."
   ) -join [Environment]::NewLine
   gh release create $tag $zip -R $repo --target $sha --title "uUnityBridge $tag (история)" --notes $notes --latest=false
   if ($LASTEXITCODE -ne 0) { throw "Failed to create release $tag" }
   $refSha = (gh api ("repos/" + $repo + "/git/ref/tags/" + $tag) --jq '.object.sha').Trim()
   if ($LASTEXITCODE -ne 0 -or $refSha -ne $sha) { throw "Created tag mismatch $tag" }
   Write-Host "PUBLISHED $tag"
 }
 foreach ($tag in @('v0.4.1','v0.4.2')) {
   gh release view $tag -R $repo --json tagName | Out-Null
   if ($LASTEXITCODE -ne 0) { throw "Current Release missing: $tag" }
 }
 Write-Host 'PASS: seven historical/current releases are available.'
 if ($Clean) {
    $cleanup = Join-Path $PSScriptRoot 'Clean-Local-QA.ps1'
    if (-not (Test-Path $cleanup)) { throw 'Cleanup script missing. QA folders were not touched.' }
    & $cleanup -Clean
 } else {
    Write-Host 'No user files changed. Use -Clean to move eligible QA copies to Recycle Bin.'
 }
}
finally {
 if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force }
}
