# CI-only historical archive. Does not inspect or modify a user's PC.
# Historical Windows EXEs are NEWLY REBUILT from pinned commits, not originals.
$ErrorActionPreference = 'Stop'
$history = @(
    @{version='0.1.0'; sha='eb98abcce8f6cc4fd5cd828875f2ad3d0f88d7b5'},
    @{version='0.2.0'; sha='a6b14124df3ae69853940812b9e8471e9461fb1d'},
    @{version='0.2.1'; sha='8835f545a8298720538ff641bdb5f420169907ec'},
    @{version='0.3.0'; sha='d2dd9022b538e5f3ba8a39ca7d55dd6e3a428798'},
    @{version='0.4.0'; sha='8e2979b5397229ff17826c72c6e0916b2a43edc0'}
)
git config user.name 'github-actions[bot]'
git config user.email '41898282+github-actions[bot]@users.noreply.github.com'

# Complete preflight before any remote mutation.
foreach ($entry in $history) {
    $sha = $entry.sha
    $tag = 'v' + $entry.version
    git cat-file -e "$sha^{commit}"
    if ($LASTEXITCODE -ne 0) { throw "Missing pinned commit: $sha" }
    $projectRef = $sha + ':src/uUnityBridge.csproj'
    $project = (git show $projectRef) -join [Environment]::NewLine
    if ($LASTEXITCODE -ne 0 -or
        $project -notmatch ('<Version>' + [regex]::Escape($entry.version) + '</Version>')) {
        throw "Pinned source Version mismatch: $tag"
    }
    gh release view $tag -R $env:GITHUB_REPOSITORY --json tagName 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { Write-Host "PRESERVE existing $tag"; continue }
    $remoteTags = (git ls-remote --tags origin ("refs/tags/" + $tag)) -join ''
    if ($LASTEXITCODE -ne 0 -or $remoteTags.Trim() -ne '') {
        throw "Tag exists without Release, or remote lookup failed: $tag"
    }
    Write-Host "PRECHECK OK $tag $sha"
}
if ($env:GITHUB_EVENT_NAME -eq 'pull_request') {
    Write-Host 'DRY RUN only. PR cannot create Releases.'
    return
}
if ($env:GITHUB_REF -ne 'refs/heads/main') { throw 'Releases may only be created from main.' }
foreach ($entry in $history) {
    $tag = 'v' + $entry.version
    $sha = $entry.sha
    gh release view $tag -R $env:GITHUB_REPOSITORY --json tagName 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { Write-Host "SKIP existing $tag"; continue }

    $worktree = Join-Path $env:RUNNER_TEMP ("source-" + $entry.version)
    $outDir = Join-Path $env:RUNNER_TEMP ("publish-" + $entry.version)
    $packageDir = Join-Path $env:RUNNER_TEMP ("package-" + $entry.version)
    New-Item -ItemType Directory -Force $packageDir | Out-Null
    git worktree add --detach $worktree $sha
    if ($LASTEXITCODE -ne 0) { throw "Cannot checkout pinned source $sha" }
    dotnet publish (Join-Path $worktree 'src/uUnityBridge.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $outDir
    if ($LASTEXITCODE -ne 0) { throw "Rebuild failed $tag" }
    $exe = Join-Path $outDir 'uUnityBridge.exe'
    if (-not (Test-Path $exe)) { throw "Main EXE missing $tag" }
    if ($entry.version -eq '0.4.0') {
        dotnet publish (Join-Path $worktree 'updater/uUnityBridge.Updater.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $outDir
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path (Join-Path $outDir 'uUnityBridge.Updater.exe'))) {
            throw "Updater EXE reconstruction failed $tag"
        }
    }
    $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion
    if (-not $fileVersion.StartsWith($entry.version + '.')) {
        throw "EXE version mismatch: $tag ($fileVersion)"
    }
    $binaryFiles = @(Get-ChildItem -LiteralPath $outDir -File)
    foreach ($binary in $binaryFiles) {
        Copy-Item -LiteralPath $binary.FullName -Destination $packageDir
    }
    $shaLines = @($binaryFiles | ForEach-Object {
        (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name
    })
    [IO.File]::WriteAllLines((Join-Path $packageDir 'SHA256SUMS.txt'), [string[]]$shaLines)
    @(
        "Historical reconstruction of uUnityBridge $tag",
        "Newly built from source commit $sha. NOT byte-identical to original EXE.",
        "Original source is available from GitHub's source archives.",
        "This is not for auto-installation. Prefer the newest stable release."
    ) | Set-Content -LiteralPath (Join-Path $packageDir 'README-HISTORY.txt') -Encoding UTF8
    $zip = Join-Path $env:RUNNER_TEMP ("uUnityBridge-$tag-HISTORICAL-REBUILD-win-x64.zip")
    Compress-Archive -Path (Join-Path $packageDir '*') -DestinationPath $zip -CompressionLevel Optimal
    if (-not (Test-Path $zip)) { throw "Could not create ZIP for $tag" }
    $notes = Join-Path $env:RUNNER_TEMP ("release-$tag.md")
    @(
        "## uUnityBridge $tag — исторический выпуск",
        "",
        "- Исходный коммит: $sha",
        "- GitHub прилагает исходники автоматически.",
        "- В приложенном архиве находится **новая реконструированная** сборка из этого коммита.",
        "- Она не обязательно совпадает побайтно с первоначальным бинарником.",
        "- SHA256SUMS.txt содержит хеши файлов.",
        "- Это исторический архив, не использовать для автоматического обновления."
    ) | Set-Content -LiteralPath $notes -Encoding UTF8

    # GitHub API creates the lightweight tag pointing at the pinned commit.
    # Do NOT push Git tags from Actions: workflow-modification permission is
    # intentionally not granted to the managed GitHub App token.
    gh release create $tag $zip -R $env:GITHUB_REPOSITORY --target $sha --title "uUnityBridge $tag (история)" --notes-file $notes --latest=false
    if ($LASTEXITCODE -ne 0) { throw "Release creation failed $tag" }
    $created = (gh api ("repos/" + $env:GITHUB_REPOSITORY + "/git/ref/tags/" + $tag) --jq '.object.sha').Trim()
    if ($LASTEXITCODE -ne 0 -or $created -ne $sha) {
        throw "Historical Release tag does not point to expected commit $sha"
    }
    Write-Host "ARCHIVED $tag -> $sha"
    git worktree remove --force $worktree
    if ($LASTEXITCODE -ne 0) { throw 'Could not clean CI worktree' }
}
