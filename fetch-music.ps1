param([string]$ArchivePath)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$expected = '34f7499b8044cddc914ae66670002d1996f18862322afbf84517e13173c3e291'
$destination = [IO.Path]::GetFullPath((Join-Path $projectRoot 'src')) + [IO.Path]::DirectorySeparatorChar
if (-not $ArchivePath) {
    $catalog = Get-Content (Join-Path $destination 'Media/catalog.json') -Raw | ConvertFrom-Json
    $missing = @($catalog | Where-Object { -not (Test-Path -LiteralPath (Join-Path $destination $_.File)) -or -not (Test-Path -LiteralPath (Join-Path $destination $_.Cover)) })
    if ($missing.Count -eq 0) { Write-Host 'All music and covers are already installed.'; return }
    $ArchivePath = Join-Path $projectRoot 'music-pack.download.zip'
    Invoke-WebRequest -Uri 'https://github.com/jonacitrusthyng/vertcoin-bitch-blast/releases/download/v0.5.0/Vertcoin-Bitch-Blast-v0.5.0-Music-Pack.zip' -OutFile $ArchivePath
}
if ((Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash -ne $expected) { throw 'Music pack checksum mismatch. No files were extracted.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $ArchivePath))
try {
    foreach ($entry in $zip.Entries) {
        $target = [IO.Path]::GetFullPath((Join-Path $destination $entry.FullName))
        if (-not $target.StartsWith($destination, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid archive path.' }
        if ($entry.Name -eq '') { continue }
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$target,$true)
    }
} finally { $zip.Dispose() }
Write-Host 'Verified music and cover pack installed. Run build.ps1 next.'
