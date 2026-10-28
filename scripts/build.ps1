[CmdletBinding()]
param(
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Chained Beasts',
    [switch]$Package,
    [switch]$Deploy,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$version = '0.6.2'
$nativeHash = 'ba3ef02fa87da4c41bb27d7610539c9578132204f0fb5713833e2ef93415ce29'
$assemblies = @('ChainedBeasts.Matchmaking.dll', 'ChainedBeasts.Matchmaking.Core.dll')
$project = Join-Path $repoRoot 'src\ChainedBeasts.Matchmaking\ChainedBeasts.Matchmaking.csproj'
$output = Join-Path $repoRoot 'src\ChainedBeasts.Matchmaking\bin\Release\net6.0'
function Get-CheckSourceHash {
    $sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src'), (Join-Path $repoRoot 'tests') -File -Recurse |
        Where-Object { $_.Extension -in @('.cs', '.csproj', '.props', '.alpha') -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })
    $sourceFiles += Get-Item -LiteralPath $PSCommandPath
    $buffer = [IO.MemoryStream]::new()
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        foreach ($file in $sourceFiles | Sort-Object FullName) {
            $relative = [IO.Path]::GetRelativePath($repoRoot, $file.FullName).Replace('\', '/')
            $bytes = [IO.File]::ReadAllBytes($file.FullName)
            $header = [Text.Encoding]::UTF8.GetBytes($relative + [char]0 + $bytes.Length.ToString([Globalization.CultureInfo]::InvariantCulture) + [char]0)
            $buffer.Write($header, 0, $header.Length); $buffer.Write($bytes, 0, $bytes.Length)
        }
        $buffer.Position = 0
        return [Convert]::ToHexString($sha.ComputeHash($buffer)).ToLowerInvariant()
    }
    finally { $buffer.Dispose(); $sha.Dispose() }
}
if (-not $SkipBuild) {
    & dotnet build $project -c Release --nologo "-p:GameDir=$GameDir"
    if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
    $coreChecks = @(& dotnet run --project (Join-Path $repoRoot 'tests\CoreChecks\CoreChecks.csproj') -c Release)
    if ($LASTEXITCODE -ne 0) { throw 'Nongame checks failed.' }
    Write-Output $coreChecks
    $adapterChecks = @(& dotnet run --project (Join-Path $repoRoot 'tests\PingIntegrationChecks\PingIntegrationChecks.csproj') -c Release -- (Join-Path $output $assemblies[0]))
    if ($LASTEXITCODE -ne 0) { throw 'Ping adapter/controller/artifact checks failed.' }
    Write-Output $adapterChecks
    $presentationChecks = @(& dotnet run --project (Join-Path $repoRoot 'tests\PingPresentationChecks\PingPresentationChecks.csproj') -c Release)
    if ($LASTEXITCODE -ne 0) { throw 'Ping presentation checks failed.' }
    Write-Output $presentationChecks
}
foreach ($file in $assemblies) {
    $path = Join-Path $output $file
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing build output: $file" }
    if ([Reflection.AssemblyName]::GetAssemblyName($path).Version.ToString() -ne "$version.0") { throw "Unexpected assembly version: $file" }
}
$files = @($assemblies | ForEach-Object {
    [ordered]@{ file = $_; sha256 = (Get-FileHash -LiteralPath (Join-Path $output $_) -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$artifactDir = Join-Path $repoRoot "artifacts\$version"
New-Item -ItemType Directory -Path $artifactDir -Force | Out-Null
$verificationPath = Join-Path $artifactDir 'build-checks.json'
$sourceHash = Get-CheckSourceHash
if (-not $SkipBuild) {
    [ordered]@{ version = $version; checkedAtUtc = [DateTime]::UtcNow.ToString('o');
        sourceSha256 = $sourceHash; runtimeVerified = $false; files = $files;
        core = $coreChecks; adaptersAndArtifact = $adapterChecks; presentation = $presentationChecks } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $verificationPath -Encoding utf8
}
elseif ($Package -or $Deploy) {
    if (-not (Test-Path -LiteralPath $verificationPath)) { throw 'No build/check record. Run this script without -SkipBuild first.' }
    $verification = Get-Content -LiteralPath $verificationPath -Raw | ConvertFrom-Json
    if ($verification.version -ne $version -or $verification.sourceSha256 -ne $sourceHash) { throw 'Source changed since the checks. Rebuild without -SkipBuild.' }
    foreach ($file in $files) {
        $recorded = @($verification.files | Where-Object { $_.file -eq $file.file })
        if ($recorded.Count -ne 1 -or $recorded[0].sha256 -ne $file.sha256) { throw 'DLL bytes changed since the checks. Rebuild without -SkipBuild.' }
    }
}
if ($Package) {
    $stage = Join-Path $artifactDir ('stage-' + [Guid]::NewGuid().ToString('N'))
    $stagePlugins = Join-Path $stage 'BepInEx\plugins\ChainedBeasts.Matchmaking'
    New-Item -ItemType Directory -Path $stagePlugins -Force | Out-Null
    foreach ($file in $assemblies) { Copy-Item -LiteralPath (Join-Path $output $file) -Destination (Join-Path $stagePlugins $file) }
    foreach ($file in @('README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $repoRoot $file) -Destination (Join-Path $stage $file) }
    $files | ForEach-Object { "$($_.sha256)  BepInEx/plugins/ChainedBeasts.Matchmaking/$($_.file)" } |
        Set-Content -LiteralPath (Join-Path $stage 'SHA256SUMS.txt') -Encoding utf8
    $zip = Join-Path $artifactDir "ChainedBeasts.Matchmaking-$version-preview.zip"
    $temporaryZip = Join-Path $artifactDir ([Guid]::NewGuid().ToString('N') + '.zip')
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $temporaryZip)
    Move-Item -LiteralPath $temporaryZip -Destination $zip -Force
    [ordered]@{ version = $version; game = '1.0.2'; steamBuild = '26231777'; nativeSha256 = $nativeHash;
        runtimeVerified = $false; publishedRelease = $false; files = $files;
        archive = (Split-Path $zip -Leaf); sha256 = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant() } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $artifactDir 'package.json') -Encoding utf8
    Write-Output "Package: $zip"
}
if ($Deploy) {
    if (Get-Process -Name ChainedBeasts -ErrorAction SilentlyContinue) { throw 'Close Chained Beasts before deployment. Build/package completed; nothing deployed.' }
    $gameRoot = [IO.Path]::GetFullPath($GameDir)
    if (-not (Test-Path -LiteralPath (Join-Path $gameRoot 'ChainedBeasts.exe'))) { throw 'Game directory is invalid.' }
    if ((Get-FileHash -LiteralPath (Join-Path $gameRoot 'GameAssembly.dll') -Algorithm SHA256).Hash.ToLowerInvariant() -ne $nativeHash) {
        throw 'Game build is different from the inspected 1.0.2 build; nothing deployed.'
    }
    $pluginRoot = Join-Path $gameRoot 'BepInEx\plugins'
    if (-not (Test-Path -LiteralPath (Join-Path $gameRoot 'BepInEx\core\BepInEx.Unity.IL2CPP.dll'))) { throw 'Install the IL2CPP loader first.' }
    $target = Join-Path $pluginRoot 'ChainedBeasts.Matchmaking'
    $targetFull = [IO.Path]::GetFullPath($target)
    foreach ($existing in Get-ChildItem -LiteralPath $pluginRoot -Filter '*.dll' -File -Recurse) {
        try { $identity = [Reflection.AssemblyName]::GetAssemblyName($existing.FullName).Name } catch { continue }
        if (($identity + '.dll') -in $assemblies -and $existing.FullName -ne (Join-Path $targetFull ($identity + '.dll'))) {
            throw "Duplicate matchmaking assembly at $($existing.FullName). Remove the old copy before deploying."
        }
    }
    $otherPlugins = @(Get-ChildItem -LiteralPath $pluginRoot -Filter '*.dll' -File -Recurse |
        Where-Object { -not $_.FullName.StartsWith($targetFull + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) } |
        ForEach-Object { [ordered]@{ path = $_.FullName; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
    $backup = Join-Path $repoRoot ('backups\deploy-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Path $backup | Out-Null
    if (Test-Path -LiteralPath $target) { Copy-Item -LiteralPath $target -Destination (Join-Path $backup 'ChainedBeasts.Matchmaking') -Recurse }
    $config = Join-Path $gameRoot 'BepInEx\config\local.chainedbeasts.matchmaking.cfg'
    if (Test-Path -LiteralPath $config) { Copy-Item -LiteralPath $config -Destination $backup }
    $userData = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\LocalLow\Featherweight Games\ChainedBeasts'
    if (Test-Path -LiteralPath $userData) { Copy-Item -LiteralPath $userData -Destination (Join-Path $backup 'UserData') -Recurse }
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    # Check again after backups. Never launch or terminate the game from this script.
    if (Get-Process -Name ChainedBeasts -ErrorAction SilentlyContinue) { throw 'Game started during backup; no DLLs deployed.' }
    foreach ($file in $assemblies) { Copy-Item -LiteralPath (Join-Path $output $file) -Destination (Join-Path $target $file) -Force }
    foreach ($file in $files) {
        if ((Get-FileHash -LiteralPath (Join-Path $target $file.file) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) { throw 'Deployment hash mismatch.' }
    }
    foreach ($file in $otherPlugins) {
        if ((Get-FileHash -LiteralPath $file.path -Algorithm SHA256).Hash -ne $file.sha256) { throw "Unexpected change to another plugin: $($file.path)" }
    }
    [ordered]@{ version = $version; installed = $target; backup = $backup; runtimeVerified = $false; files = $files; otherPluginsUnchanged = $true } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $artifactDir 'deployment.json') -Encoding utf8
    Write-Output "Deployed matchmaking only: $target"
    Write-Output "Rollback backup: $backup"
}
