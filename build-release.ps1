param([string]$IsccPath, [string]$SigningCertificateThumbprint)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (-not $IsccPath) {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'))
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $IsccPath = $command.Source }
    else { $IsccPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1 }
}
if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath)) {
    throw 'Inno Setup compiler not found. Supply -IsccPath with the path to ISCC.exe.'
}

& (Join-Path $root 'build.ps1')
$exe = Join-Path $root 'build\Rasklad.exe'
$fileVersion = [version](Get-Item -LiteralPath $exe).VersionInfo.FileVersion
$version = '{0}.{1}.{2}' -f $fileVersion.Major, $fileVersion.Minor, $fileVersion.Build
$name = "Rasklad-$version-windows-x64"
$dist = Join-Path $root 'dist'
if ($SigningCertificateThumbprint) {
    & (Join-Path $root 'sign-file.ps1') -FilePath $exe -CertificateThumbprint $SigningCertificateThumbprint
}
$stage = Join-Path $root ('build\package-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dist, $stage, (Join-Path $stage 'Licenses'), (Join-Path $stage 'docs') -Force | Out-Null

# Explicit allowlist: never package local settings, learned words, diagnostics, or build fixtures.
Copy-Item -LiteralPath $exe -Destination (Join-Path $stage 'Rasklad.exe')
if ($SigningCertificateThumbprint) {
    $certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$SigningCertificateThumbprint"
    $publicCertificate = Join-Path $dist 'Rasklad-signing.cer'
    Export-Certificate -Cert $certificate -FilePath $publicCertificate -Type CERT | Out-Null
    Copy-Item -LiteralPath $publicCertificate -Destination $stage
}
foreach ($file in @('README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'CHANGELOG.md')) {
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $stage
}
Copy-Item -LiteralPath (Join-Path $root 'assets\Rasklad.ico') -Destination $stage
Copy-Item -LiteralPath (Join-Path $root 'assets\Rasklad-tray.ico') -Destination $stage
Copy-Item -LiteralPath (Join-Path $root 'docs\copilot.ahk') -Destination (Join-Path $stage 'docs')
Copy-Item -LiteralPath (Join-Path $root 'dictionaries\FOLDOC.txt') -Destination (Join-Path $stage 'Licenses\FOLDOC-source.txt')
Copy-Item -LiteralPath (Join-Path $root 'dictionaries\ru_terms.tsv') -Destination (Join-Path $stage 'Licenses\ru_terms.tsv')
Copy-Item -LiteralPath (Join-Path $root 'dictionaries\NOTICE.txt') -Destination (Join-Path $stage 'Licenses\NOTICE.txt')
Copy-Item -LiteralPath (Join-Path $root 'dictionaries\Rasklad-LICENSE.txt') -Destination (Join-Path $stage 'Licenses\Rasklad-MIT.txt')

Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
function New-ReleaseZip([string]$Source, [string]$Destination) {
    if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Force }
    $stream = [IO.File]::Create($Destination)
    $archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $Source -Recurse -File -Force) {
            $relative = $file.FullName.Substring($Source.Length + 1).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose(); $stream.Dispose() }
}
$portable = Join-Path $dist "$name-portable.zip"
New-ReleaseZip $stage $portable

$sourceStage = Join-Path $root ('build\source-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $sourceStage -Force | Out-Null
foreach ($folder in @('src', 'assets', 'dictionaries', 'installer', '.github', 'docs', 'certificates')) {
    Copy-Item -LiteralPath (Join-Path $root $folder) -Destination $sourceStage -Recurse
}
foreach ($file in @('README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'RELEASING.md', 'CHANGELOG.md', '.gitignore',
    'build.ps1', 'build-release.ps1', 'make-icon.ps1', 'sign-file.ps1')) {
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $sourceStage
}
$sourceZip = Join-Path $dist "Rasklad-$version-source.zip"
New-ReleaseZip $sourceStage $sourceZip

& $IsccPath "/DAppVersion=$version" "/DPackageDir=$stage" "/DReleaseDir=$dist" (Join-Path $root 'installer\Rasklad.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed: $LASTEXITCODE" }
$setup = Join-Path $dist "$name-Setup.exe"
if ($SigningCertificateThumbprint) {
    & (Join-Path $root 'sign-file.ps1') -FilePath $setup -CertificateThumbprint $SigningCertificateThumbprint
}
$notes = [IO.File]::ReadAllText((Join-Path $root 'CHANGELOG.md'))
[IO.File]::WriteAllText((Join-Path $dist "Rasklad-$version-release-notes.md"), $notes, [Text.UTF8Encoding]::new($false))
$checksumFiles = @($setup, $portable, $sourceZip)
if ($SigningCertificateThumbprint) { $checksumFiles += $publicCertificate }
$checksums = foreach ($file in $checksumFiles) {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant(), (Split-Path $file -Leaf)
}
[IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'), $checksums, [Text.UTF8Encoding]::new($false))
Write-Output "Release ${version}: $dist"
