$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$catalog = Get-Content (Join-Path $projectRoot 'src/Media/catalog.json') -Raw | ConvertFrom-Json
foreach ($track in $catalog) {
    if (-not (Test-Path -LiteralPath (Join-Path $projectRoot ('src/' + $track.File)))) { throw 'Music is missing. Run fetch-music.ps1 or use the full Source ZIP first.' }
}
dotnet publish (Join-Path $projectRoot 'src/VertcoinBitchBlast.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o (Join-Path $projectRoot 'build/app')
if ($LASTEXITCODE -ne 0) { throw 'App build failed.' }
dotnet publish (Join-Path $projectRoot 'tests/OcmTests.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -o (Join-Path $projectRoot 'build/tests')
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
Write-Host 'Built build/app/VertcoinBitchBlast.exe and build/tests/OcmTests.exe.'
