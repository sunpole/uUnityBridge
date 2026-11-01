# Owner-reviewed one-time historical archive publication + optional reversible QA cleanup.
[CmdletBinding()]
param([switch]$Clean, [switch]$SelfTest)
$ErrorActionPreference = 'Stop'
$repo = 'sunpole/uUnityBridge'
$history = @(
 @{v='0.1.0';sha='eb98abcce8f6cc4fd5cd828875f2ad3d0f88d7b5'},
 @{v='0.2.0';sha='a6b14124df3ae69853940812b9e8471e9461fb1d'},
 @{v='0.2.1';sha='8835f545a8298720538ff641bdb5f420169907ec'},
 @{v='0.3.0';sha='d2dd9022b538e5f3ba8a39ca7d55dd6e3a428798'},
 @{v='0.4.0';sha='8e2979b5397229ff17826c72c6e0916b2a43edc0'}
)
function Select-HistoricalRunId([string]$Json, [string]$Head) {
    # PowerShell 5.1 sometimes passes JSON arrays as a single pipeline item.
    # Explicit iteration avoids expanding all databaseId values into gh arguments.
$parsedRuns = ConvertFrom-Json -InputObject ($Json)
$matchingRuns = @()
foreach ($item in $parsedRuns) {
    if ($item.status -eq 'completed' -and $item.conclusion -eq 'success' -and
        $item.event -eq 'push' -and $item.headSha -eq $Head) {
        $matchingRuns += $item
    }
}
if ($matchingRuns.Count -eq 0) {
    throw 'No successful archived-build run for exact main commit.'
}
$selectedRun = $matchingRuns | Sort-Object -Property databaseId -Descending | Select-Object -First 1
    $id = [string]$selectedRun.databaseId
    if ($id -notmatch '^[1-9][0-9]*$') {
        throw "Invalid or non-scalar GitHub run ID: $id"
    }
    return $id
}

# Pure preflight: "missing" is expected and must never require a CLI call
# that emits HTTP 404 to PowerShell stderr.
function Get-HistoricalReleaseState($Releases, $Refs, [string]$Tag, [string]$Sha) {
    $releaseCount = 0
    foreach ($release in $Releases) {
        if ($release.tagName -ceq $Tag) { $releaseCount++ }
    }
    $refCount = 0
    $refSha = ''
    $refType = ''
    foreach ($reference in $Refs) {
        if ($reference.ref -ceq ('refs/tags/' + $Tag)) {
            $refCount++
            $refSha = [string]$reference.object.sha
            $refType = [string]$reference.object.type
        }
    }
    if ($releaseCount -gt 1 -or $refCount -gt 1) {
        throw "Ambiguous GitHub release or tag: $Tag"
    }
    if ($releaseCount -eq 1) {
        if ($refCount -ne 1 -or $refType -ne 'commit' -or $refSha -ne $Sha) {
            throw "Existing release target mismatch: $Tag"
        }
        return 'published'
    }
    if ($refCount -ne 0) { throw "Existing tag without release: $Tag" }
    return 'missing'
}

if ($SelfTest) {
    $sha = ('a' * 40)
    $sample = @(1..8 | ForEach-Object {
        [pscustomobject]@{
            databaseId=(400000 + $_); status='completed'; conclusion='failure';
            event='push'; headSha=('b' * 40)
        }
    })
    $sample[6].conclusion = 'success'
    $sample[6].headSha = $sha
    $json = ConvertTo-Json -InputObject $sample -Depth 3
    $chosen = Select-HistoricalRunId -Json $json -Head $sha
    if ($chosen -ne '400007') {
        throw "CI run ID selection test failed: $chosen"
    }
    Write-Host 'PASS: 8 GitHub runs reduced to the single matching run ID'
    $sampleSha = 'c' * 40
    $empty = @()
    if ((Get-HistoricalReleaseState $empty $empty 'v0.1.0' $sampleSha) -ne 'missing') {
        throw 'Missing historical Release was not recognized correctly.'
    }
    $published = @([pscustomobject]@{tagName='v0.1.0'})
    $refs = @([pscustomobject]@{ref='refs/tags/v0.1.0'; object=[pscustomobject]@{type='commit'; sha=$sampleSha}})
    if ((Get-HistoricalReleaseState $published $refs 'v0.1.0' $sampleSha) -ne 'published') {
        throw 'Existing historical Release was not preserved.'
    }
    $orphanRejected = $false
    try { Get-HistoricalReleaseState $empty $refs 'v0.1.0' $sampleSha | Out-Null }
    catch { $orphanRejected = $true }
    if (-not $orphanRejected) { throw 'Orphaned historical tag was not rejected.' }
    Write-Host 'PASS: missing, published and orphaned release states without 404 probes.'
    return
}

$who = gh api user --jq .login
if ($LASTEXITCODE -ne 0 -or $who.Trim() -ne 'sunpole') { throw 'gh login must be sunpole.' }
$mainSha = (gh api ("repos/" + $repo + "/git/ref/heads/main") --jq '.object.sha').Trim()
if ($LASTEXITCODE -ne 0 -or $mainSha.Length -ne 40) { throw 'Main commit unavailable.' }
$runList = gh run list -R $repo -w archive-historical-releases.yml -L 20 --json databaseId,status,conclusion,event,headSha
if ($LASTEXITCODE -ne 0) { throw 'GitHub Actions unavailable.' }
$runId = Select-HistoricalRunId -Json ($runList -join [Environment]::NewLine) -Head $mainSha

# Query existing releases AND tag refs in bulk. Missing release/tag is a normal
# state; probing each absent tag with gh release view or gh api returns 404 on
# stderr and PowerShell 5.1 throws NativeCommandError under Stop preference.
$releaseListJson = gh release list -R $repo --limit 100 --json tagName
if ($LASTEXITCODE -ne 0) { throw 'Cannot list GitHub Releases.' }
$releaseList = ConvertFrom-Json -InputObject ($releaseListJson -join [Environment]::NewLine)
$tagRefsJson = gh api ("repos/" + $repo + "/git/matching-refs/tags/v")
if ($LASTEXITCODE -ne 0) { throw 'Cannot list GitHub tag references.' }
$tagRefs = ConvertFrom-Json -InputObject ($tagRefsJson -join [Environment]::NewLine)
Write-Host "Preflight: $($releaseList.Count) releases, $($tagRefs.Count) tags."

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
   $state = Get-HistoricalReleaseState $releaseList $tagRefs $tag $sha
   if ($state -eq 'published') {
     Write-Host "PRESERVE $tag"
     continue
   }
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
